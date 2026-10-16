using System;
using System.Globalization;

namespace MechrevoLite.Hardware;

// ==========================================================================================
// 2026-10-05 uifix（用户要求④「增加保底机制」）：
//   「如果检测到上次非正常关机 LM不会开机自动应用并提示」
//
// 判定方式（**首选方案：自记标记**，用户给的实现建议第一条）：
//   * 正常退出（托盘退出 / Windows 注销 / 关机 / 重启）时写一个"干净退出"标记；
//   * 下次启动时**读取后立即清除**；
//   * 读到"没有干净退出标记"（且不是首次运行）-> 判定上次是非正常关机
//     （崩溃、蓝屏、强制断电、被任务管理器结束等都不会走到写标记的代码）。
//
// 复用的既有模式（不另造一套风格）：
//   * 配置键 + `AppConfig.Is/Set/Remove` —— 与 GpuRestartVerifier 的
//     gpu_restart_pending / gpu_restart_tick 同一套做法；
//   * "这一份判定是不是本次开机的" —— 与 GpuRestartVerifier.RebootHappened 同一个思路
//     （它用 Environment.TickCount64 比较），这里把 TickCount64 换算成"开机时刻"再比字符串，
//     避免 TickCount64 在"上次开机跑得久、这次开机跑得快"时出现的歧义；
//   * `Logger.FlushCrashBuffer` —— 已经落过崩溃现场就绝不写"干净退出"标记。
//
// 配置键（`cpu_co_` 前缀 -> ModelScopedConfig.IsMachineScopedKey 自动按机型作用域，
// 与 cpu_co_coreN / cpu_co_auto_apply 同一把锁，不会把 A 机器的判定带到 B 机器）：
//   * cpu_co_clean_exit    —— "干净退出"标记（1=上次是正常退出）；启动读取后立即 Remove
//   * cpu_co_guard_armed   —— 这个功能已经"武装"过（区分首次安装/首次运行）
//   * cpu_co_guard_boot    —— 做出本次判定的开机身份（"yyyy-MM-dd HH:mm"）
//   * cpu_co_guard_verdict —— 本次开机的判定结论（clean / first_run / abnormal）
//
// 每个进程只判定一次；同一次开机内再有人问（例如 `--cpu-co-auto-apply` 与正在运行的
// LM 控制台）直接复用结论 —— 这样"读取后立即清除"不会被第二个进程重复消耗，
// 从而不会把一次正常开机误判成非正常关机。
// ==========================================================================================
internal enum CpuCoShutdownVerdict
{
	// 本进程还没有判定过（UiAudit / 证据工具等不会走到判定的路径）。
	NotEvaluated = 0,
	// 上次是正常退出/关机。
	Clean = 1,
	// 检测到上次是非正常关机（崩溃、断电、被强杀……）。
	Abnormal = 2,
	// 首次安装/首次运行：没有任何标记，**不**算异常。
	FirstRun = 3
}

internal static class CpuCoAutoApplyGuard
{
	internal const string CleanExitKey = "cpu_co_clean_exit";

	internal const string ArmedKey = "cpu_co_guard_armed";

	internal const string BootKey = "cpu_co_guard_boot";

	internal const string VerdictKey = "cpu_co_guard_verdict";

	internal const string CleanName = "clean";

	internal const string FirstRunName = "first_run";

	internal const string AbnormalName = "abnormal";

	private static CpuCoShutdownVerdict _lastVerdict;

	// 证据工具/自检用（与 GpuRestartVerifier.NotifyOverride 同一类显式覆盖点）：
	// 只影响 **UI 显示** 与判定返回值，不会让任何写入路径被打开。
	internal static CpuCoShutdownVerdict? VerdictOverride { get; set; }

	// 本进程的判定结果（NotEvaluated = 本进程还没判定过）。
	internal static CpuCoShutdownVerdict LastVerdict => _lastVerdict;

	internal static bool WasAbnormal => LastVerdict == CpuCoShutdownVerdict.Abnormal;

	// UI 只读访问：优先用本进程的判定；没判定过（UiAudit / 证据工具）时看配置里
	// 本次开机已经写下的结论 —— **只读，不消耗任何标记**。
	internal static CpuCoShutdownVerdict UiVerdict
	{
		get
		{
			if (VerdictOverride.HasValue)
			{
				return VerdictOverride.Value;
			}
			if (_lastVerdict != CpuCoShutdownVerdict.NotEvaluated)
			{
				return _lastVerdict;
			}
			if (AppConfig.GetString(BootKey) != BootIdentity(DateTime.UtcNow, Environment.TickCount64))
			{
				return CpuCoShutdownVerdict.NotEvaluated;
			}
			return Parse(GetStringOrNull(VerdictKey));
		}
	}

	// 开机身份：当前 UTC 时间 - 开机以来毫秒数 = 这次开机的墙上时刻，取到分钟。
	// 同一次开机内恒定；重启后必然变化（TickCount64 从 0 重新开始）。
	internal static string BootIdentity(DateTime utcNow, long tickCount64)
	{
		return (utcNow - TimeSpan.FromMilliseconds(tickCount64)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	}

	// 判定"上次是否正常关机"。**每个进程只消耗一次标记**：同一次开机内重复调用直接复用结论。
	internal static CpuCoShutdownVerdict EvaluateForThisBoot()
	{
		if (VerdictOverride.HasValue)
		{
			_lastVerdict = VerdictOverride.Value;
			Logger.WriteLine($"[cpu-co] shutdown guard: verdict forced by override -> {Name(_lastVerdict)}");
			return _lastVerdict;
		}
		string boot = BootIdentity(DateTime.UtcNow, Environment.TickCount64);
		string? storedVerdict = GetStringOrNull(VerdictKey);
		if (GetStringOrNull(BootKey) == boot)
		{
			CpuCoShutdownVerdict cached = Parse(storedVerdict);
			if (cached != CpuCoShutdownVerdict.NotEvaluated)
			{
				// 本次开机已经判定过（例如 --cpu-co-auto-apply 与正在运行的 LM 控制台）：
				// 直接复用，绝不再读/清一次标记，否则第二次会把自己误判成非正常关机。
				_lastVerdict = cached;
				Logger.WriteLine($"[cpu-co] shutdown guard: already decided this boot -> {Name(cached)} (boot {boot}); clean-exit marker not re-read");
				return cached;
			}
		}
		bool armed = AppConfig.Is(ArmedKey);
		bool marker = AppConfig.Is(CleanExitKey);
		CpuCoShutdownVerdict verdict;
		if (!armed)
		{
			// 首次安装/首次运行：没有任何标记也不能算异常（用户明确要求）。
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
		// 读取后立即清除（用户给的方案原文）；无论判定结论是什么都清，避免标记越积越旧。
		if (marker)
		{
			AppConfig.Remove(CleanExitKey);
		}
		AppConfig.Set(BootKey, boot);
		AppConfig.Set(VerdictKey, Name(verdict));
		AppConfig.Flush();
		_lastVerdict = verdict;
		Logger.WriteLine($"[cpu-co] shutdown guard: armed={armed} cleanExitMarker={marker} -> {Name(verdict)} (boot {boot})");
		return verdict;
	}

	// 正常退出时调用（ApplicationExit / Windows 会话结束）。幂等，重复调用没坏处。
	internal static void MarkCleanExit(string reason)
	{
		try
		{
			if (Logger.CrashFlushed)
			{
				// 本次进程已经落过崩溃现场：宁可不写标记（下次启动按非正常关机处理，
				// 少写一次 CO 比多写一次安全）。
				Logger.WriteLine($"[cpu-co] clean-exit marker NOT written ({reason}): a crash buffer was flushed in this process");
				return;
			}
			AppConfig.Set(CleanExitKey, 1);
			AppConfig.Flush();
			Logger.WriteLine($"[cpu-co] clean-exit marker written ({reason}) -> next start may auto-apply");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[cpu-co] clean-exit marker failed: " + ex.Message);
		}
	}

	internal static string Name(CpuCoShutdownVerdict verdict)
	{
		return verdict switch
		{
			CpuCoShutdownVerdict.Clean => CleanName,
			CpuCoShutdownVerdict.FirstRun => FirstRunName,
			CpuCoShutdownVerdict.Abnormal => AbnormalName,
			_ => "not_evaluated"
		};
	}

	// 人读的一句话（日志 / --cpu-co-auto-apply 输出用）。
	internal static string Describe(CpuCoShutdownVerdict verdict)
	{
		return verdict switch
		{
			CpuCoShutdownVerdict.Clean => "last shutdown was clean",
			CpuCoShutdownVerdict.FirstRun => "first run for this feature (no marker yet) -> treated as clean",
			CpuCoShutdownVerdict.Abnormal => "last shutdown was NOT clean (crash / power loss / forced kill)",
			_ => "not evaluated in this process"
		};
	}

	private static CpuCoShutdownVerdict Parse(string? text)
	{
		if (text == CleanName)
		{
			return CpuCoShutdownVerdict.Clean;
		}
		if (text == FirstRunName)
		{
			return CpuCoShutdownVerdict.FirstRun;
		}
		if (text == AbnormalName)
		{
			return CpuCoShutdownVerdict.Abnormal;
		}
		return CpuCoShutdownVerdict.NotEvaluated;
	}

	private static string? GetStringOrNull(string key)
	{
		return AppConfig.Exists(key) ? AppConfig.GetString(key) : null;
	}
}
