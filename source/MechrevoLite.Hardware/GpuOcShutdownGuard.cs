using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using MechrevoLite.Properties;

namespace MechrevoLite.Hardware;

// ==========================================================================================
// 2026-10-10（用户反馈 ③「自定义模式的显卡超频功能如果上次非正常关机应该自动关闭」）：
//   **新功能**（此前没有）。实现方式复用既有的、已经在真机验证过的 CpuCoAutoApplyGuard
//   （CO / FMax 的"上次非正常关机就不自动应用"保底机制）的**同一套判定**，但**完全独立**：
//     * 自己的 4 把键（gpu_oc_guard_* / gpu_oc_clean_exit），自己的"只做一次"闸门；
//     * ⛔ `cpu_co_*` 那一套（属于"每核 CO 与 Intel 通路"的既有验收范围）**一个字都不改** ——
//       既不改 CpuCoAutoApplyGuard，也不改它的键、它的调用点，避免任何回退风险。
//
// 判定手段（= 用户给的实现建议第一条，最稳、不依赖 Windows 事件日志 / dirty-shutdown 标志）：
//   * **正常退出**（托盘退出 / Windows 注销 / 关机 / 重启）时写一个"干净退出"标记
//     `gpu_oc_clean_exit`=1（SessionEnding 与 ApplicationExit 各写一次，幂等）；
//   * 下次启动**读取后立即清除**；
//   * 读到"没有干净退出标记"且不是首次运行 ⇒ 判定上次是非正常关机
//     （崩溃 / 蓝屏 / 强制断电 / 被任务管理器结束都走不到写标记的代码）。
//   * ⚠️ 因此**正常重启 / 正常关机绝不会触发自关** —— 它们都走 SessionEnding/ApplicationExit，
//     标记会被写下；只有"没走到那两处"的退出才算异常。这一条是语义红线，不能放宽。
//   * 每个进程只判定一次（按开机身份缓存），第二次问直接复用结论 —— 避免把一次正常开机
//     误判成非正常（与 CpuCoAutoApplyGuard 同一个理由）。
//
// 自关动作（只在「上次非正常关机」且「显卡超频当时是开的」时做）：
//   ① 写回配置 `gpu_oc`=0，并把 MechrevoService.GpuOverclockEnabled 同步为 false；
//   ② 关掉**驱动侧**那份持久化 `direct_gpu_oc_enabled_<档位>`（存在的档位才动，写 0 并把
//      core/memory 也归零 —— 与 MechrevoHw.SaveDirectGpuOverclockValue("OverClockingSwitch",0)
//      完全同一套表示法）。不做这一步的话，下次进自定义模式会被
//      MechrevoHw.RestoreDirectGpuOverclockProfile 原样恢复回来 ⇒ "自动关闭"等于没关；
//   ③ 关掉**档位记录**里还写着开的那一份 `perf_custom<N>_ocon`（否则下一次"应用"又会把超频推回去）；
//   ④ 硬件在线就**下发一次关闭**（MechrevoService.ApplyGpuOverclock(false) ⇒ 恢复默认偏移）；
//      不在线就只写回配置并明确记一行"未下发"；
//   ⑤ 写日志（说明为什么被关）+ 一条 Toast 提示用户。
// ==========================================================================================
internal static class GpuOcShutdownGuard
{
	/// <summary>显卡超频总开关（与 SettingsForm 那颗同名开关、与自定义模式那一行**同一把键**）。</summary>
	internal const string EnabledKey = "gpu_oc";

	/// <summary>"干净退出"标记（1=上次是正常退出）；启动读取后立即 Remove。</summary>
	internal const string CleanExitKey = "gpu_oc_clean_exit";

	/// <summary>本功能是否已"武装"过（区分首次安装 / 首次运行）。</summary>
	internal const string ArmedKey = "gpu_oc_guard_armed";

	/// <summary>做出本次判定的开机身份（"yyyy-MM-dd HH:mm"）。</summary>
	internal const string BootKey = "gpu_oc_guard_boot";

	/// <summary>本次开机的判定结论（clean / first_run / abnormal）。</summary>
	internal const string VerdictKey = "gpu_oc_guard_verdict";

	private static CpuCoShutdownVerdict _lastVerdict;

	private static int _applied;

	/// <summary>本进程的判定结果（NotEvaluated = 本进程还没判定过）。</summary>
	internal static CpuCoShutdownVerdict LastVerdict => _lastVerdict;

	/// <summary>显卡超频开关当前是否记忆为"开"。</summary>
	internal static bool IsOn => AppConfig.Is(EnabledKey);

	/// <summary>开机身份：当前 UTC 时间 - 开机以来毫秒数 = 这次开机的墙上时刻，取到分钟。</summary>
	internal static string BootIdentity(DateTime utcNow, long tickCount64)
	{
		return (utcNow - TimeSpan.FromMilliseconds(tickCount64)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// 判定"上次是否正常关机"。**每个进程只消耗一次标记**：同一次开机内重复调用直接复用结论。
	/// 结论同时写进 <see cref="BootKey" />/<see cref="VerdictKey" />（只读证据，与 CPU 侧同一套做法）。
	/// </summary>
	internal static CpuCoShutdownVerdict EvaluateForThisBoot()
	{
		string boot = BootIdentity(DateTime.UtcNow, Environment.TickCount64);
		if (AppConfig.GetString(BootKey) == boot)
		{
			CpuCoShutdownVerdict cached = Parse(AppConfig.GetString(VerdictKey));
			if (cached != CpuCoShutdownVerdict.NotEvaluated)
			{
				_lastVerdict = cached;
				Logger.WriteLine($"[gpu-oc] shutdown guard: 本次开机已判定过 -> {CpuCoAutoApplyGuard.Name(cached)} (boot {boot})；干净退出标记不再重读");
				return cached;
			}
		}
		bool armed = AppConfig.Is(ArmedKey);
		bool marker = AppConfig.Is(CleanExitKey);
		CpuCoShutdownVerdict verdict;
		if (!armed)
		{
			// 首次安装 / 首次运行：没有任何标记也不能算异常。
			verdict = CpuCoShutdownVerdict.FirstRun;
		}
		else if (marker)
		{
			verdict = CpuCoShutdownVerdict.Clean;
		}
		else
		{
			verdict = CpuCoShutdownVerdict.Abnormal;
		}
		if (!armed)
		{
			AppConfig.Set(ArmedKey, 1);
		}
		if (marker)
		{
			AppConfig.Remove(CleanExitKey);
		}
		AppConfig.Set(BootKey, boot);
		AppConfig.Set(VerdictKey, CpuCoAutoApplyGuard.Name(verdict));
		AppConfig.Flush();
		_lastVerdict = verdict;
		Logger.WriteLine($"[gpu-oc] shutdown guard: armed={armed} cleanExitMarker={marker} -> {CpuCoAutoApplyGuard.Name(verdict)} (boot {boot})");
		return verdict;
	}

	/// <summary>
	/// 正常退出时调用（SessionEnding 注销/关机/重启，以及 ApplicationExit=托盘退出）。
	/// 幂等，重复调用没坏处。⚠️ 这一行就是"正常退出不触发自关"的**唯一保障**。
	/// </summary>
	internal static void MarkCleanExit(string reason)
	{
		try
		{
			if (Logger.CrashFlushed)
			{
				// 本次进程已经落过崩溃现场：宁可不写标记（下次启动按非正常关机处理，少带一份超频更安全）。
				Logger.WriteLine($"[gpu-oc] clean-exit marker NOT written ({reason}): a crash buffer was flushed in this process");
				return;
			}
			AppConfig.Set(CleanExitKey, 1);
			AppConfig.Flush();
			Logger.WriteLine($"[gpu-oc] clean-exit marker written ({reason}) -> 下次开机按正常关机处理，不会自动关超频");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[gpu-oc] clean-exit marker failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 开机后调用（由 Program.ScheduleGpuOcShutdownGuard 在硬件连接后调度，**全程异步、不阻塞**）。
	/// 上次非正常关机 且 显卡超频当时是开的 ⇒ 自动关闭它（写回配置 + 清驱动侧/档位记录 + 下发 + 提示）。
	/// 同一次开机最多做一次。
	/// </summary>
	internal static async Task<bool> ApplyIfUncleanShutdownAsync(MechrevoService service)
	{
		if (Interlocked.Exchange(ref _applied, 1) != 0)
		{
			return false;
		}
		CpuCoShutdownVerdict verdict;
		bool on;
		try
		{
			verdict = EvaluateForThisBoot();
			on = IsOn;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[gpu-oc] 开机判定失败（不做任何动作）: " + ex.Message);
			return false;
		}
		Logger.WriteLine($"[gpu-oc] 开机判定: verdict={CpuCoAutoApplyGuard.Name(verdict)} ({CpuCoAutoApplyGuard.Describe(verdict)}) {EnabledKey}={on}");
		if (verdict != CpuCoShutdownVerdict.Abnormal)
		{
			return false;
		}
		if (!on)
		{
			// 开关本来就是关的：没有任何东西被带过来，不打扰用户，只留一行日志。
			Logger.WriteLine("[gpu-oc] 上次非正常关机，但显卡超频当时是关的 ⇒ 无需动作");
			return false;
		}
		Logger.WriteLine("[gpu-oc] 上次非正常关机且显卡超频是开的 ⇒ 自动关闭（避免带着一份可能不稳定的显卡超频再开机）");
		AppConfig.Set(EnabledKey, 0);
		MechrevoService.GpuOverclockEnabled = false;
		int directCleared = ClearDirectProfilePersistence();
		int modeSwitchesCleared = ClearPerModeSwitches();
		AppConfig.Flush();
		Logger.WriteLine($"[gpu-oc] 已写回配置 {EnabledKey}=0；驱动侧持久化 direct_gpu_oc_enabled_* 关了 {directCleared} 项；档位记录 perf_custom<N>_ocon 关了 {modeSwitchesCleared} 项；⚠️ CPU 侧的键与判定（cpu_co_*）一律未动");
		bool sent = false;
		if (service != null)
		{
			try
			{
				sent = await service.ApplyGpuOverclock(false).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteLine($"[gpu-oc] 已下发关闭（恢复默认偏移）sent={sent}");
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[gpu-oc] 下发关闭失败（配置已写回，下次开机不会再带超频）: " + ex.Message);
			}
		}
		else
		{
			Logger.WriteLine("[gpu-oc] 没有可用的 MechrevoService（GCU 未连接）⇒ 只写回配置，本次未下发硬件命令");
		}
		try
		{
			ToastForm.ShowNotice(Strings.GpuOcGuardOffToast);
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("[gpu-oc] 提示失败: " + ex2.Message);
		}
		return true;
	}

	/// <summary>
	/// 关掉驱动侧那份持久化（MechrevoHw.DirectGpuProfileKey）。写 0 而不是 Remove —— 与
	/// MechrevoHw.SaveDirectGpuOverclockValue("OverClockingSwitch", 0) 的表示法完全一致
	/// （AppConfig.Is(...) 对 0 同样是"关"，RestoreDirectGpuOverclockProfile 会因此直接返回 false）。
	/// 只动**已经存在**的档位，不凭空造键。
	/// </summary>
	private static int ClearDirectProfilePersistence()
	{
		int cleared = 0;
		for (int profile = 0; profile <= 3; profile++)
		{
			string stamp = profile.ToString(CultureInfo.InvariantCulture);
			if (!AppConfig.Exists("direct_gpu_oc_enabled_" + stamp))
			{
				continue;
			}
			AppConfig.Set("direct_gpu_oc_enabled_" + stamp, 0);
			AppConfig.Set("direct_gpu_oc_core_" + stamp, 0);
			AppConfig.Set("direct_gpu_oc_memory_" + stamp, 0);
			cleared++;
		}
		return cleared;
	}

	/// <summary>关掉档位记录里还写着"开"的那一份（否则下一次"应用"又会把超频推回去）。</summary>
	private static int ClearPerModeSwitches()
	{
		int cleared = 0;
		foreach (int ordinal in PerfModeStore.LoadCustomOrdinals())
		{
			string key = "perf_custom" + ordinal.ToString(CultureInfo.InvariantCulture) + "_ocon";
			if (!AppConfig.Exists(key) || AppConfig.Get(key) == 0)
			{
				continue;
			}
			AppConfig.Set(key, 0);
			cleared++;
		}
		return cleared;
	}

	private static CpuCoShutdownVerdict Parse(string text)
	{
		if (text == CpuCoAutoApplyGuard.CleanName)
		{
			return CpuCoShutdownVerdict.Clean;
		}
		if (text == CpuCoAutoApplyGuard.FirstRunName)
		{
			return CpuCoShutdownVerdict.FirstRun;
		}
		if (text == CpuCoAutoApplyGuard.AbnormalName)
		{
			return CpuCoShutdownVerdict.Abnormal;
		}
		return CpuCoShutdownVerdict.NotEvaluated;
	}
}
