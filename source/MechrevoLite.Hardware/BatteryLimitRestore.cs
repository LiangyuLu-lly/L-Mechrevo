// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **启动核对与恢复** `BatteryLimitRestore` ⭐⭐
//
// ⭐ 机主要求：关机/重启/外部改写后，把 EC 的限充状态恢复到**本地配置**：
//    · 配置=启用 且（mode != 0x48 或 high/low 不一致）⇒ 按配置重新启用（同一套白名单+严格时序+回读）；
//    · 配置=关闭 且 mode == 0x48 ⇒ 按关闭序列恢复默认；
//    · 一致 ⇒ 不写。
//
// ⭐ 时机：① 启动空闲预热（Program.ScheduleIdleBackendWarmup）跑一次；② 打开电池页时再核对一次（幂等）。
// ⛔ 失败不重试、只记日志、不阻塞启动；⛔ 非宝龙达（BoardWhitelist 不命中）直接返回、0 读 0 写 ✅

using System;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 宝龙达限充的启动核对/恢复（配置键 battery_limit_enabled/start/stop）✅。</summary>
internal static class BatteryLimitRestore
{
	internal const string EnabledKey = "battery_limit_enabled";

	internal const string StartKey = "battery_limit_start";

	internal const string StopKey = "battery_limit_stop";

	internal const string AllowInitialDischargeKey = "battery_limit_allow_initial_discharge";

	/// <summary>⭐ 高级开关：允许 SOC ≥ 停充阈值时启用（默认 0；⛔ 不默认开）✅。</summary>
	internal static bool AllowInitialDischarge => AppConfig.Get(AllowInitialDischargeKey, 0) == 1;

	internal static void SetAllowInitialDischarge(bool allow)
	{
		AppConfig.Set(AllowInitialDischargeKey, allow ? 1 : 0);
		AppConfig.Flush();
		Logger.WriteLine("[battery] 允许初次放电=" + (allow ? "开" : "关"));
	}

	private static int _running;

	/// <summary>⭐ 本地是否存过配置（没存过 ⇒ ⛔ 不恢复，避免把参考工具单独设置的值关掉）✅。</summary>
	internal static bool HasSavedConfig()
	{
		return AppConfig.Exists(EnabledKey);
	}

	internal static bool TryLoad(out bool enabled, out int low, out int high)
	{
		enabled = false;
		low = -1;
		high = -1;
		if (!AppConfig.Exists(EnabledKey))
		{
			return false;
		}
		enabled = AppConfig.Get(EnabledKey, 0) == 1;
		low = AppConfig.Get(StartKey, -1);
		high = AppConfig.Get(StopKey, -1);
		return low >= 1 && high <= 100 && low < high;
	}

	/// <summary>⭐ 用户成功启用/关闭/保存后写配置（面板调用）✅。</summary>
	internal static void SaveConfig(bool enabled, int low, int high)
	{
		AppConfig.Set(EnabledKey, enabled ? 1 : 0);
		if (low >= 1)
		{
			AppConfig.Set(StartKey, low);
		}
		if (high >= 1)
		{
			AppConfig.Set(StopKey, high);
		}
		AppConfig.Flush();
	}

	/// <summary>
	/// ⭐ 核对并（必要时）恢复一次。<paramref name="source" /> 只用于日志（启动空闲预热 / 打开电池页）。
	/// <para>⚠️ 幂等：状态与配置一致时⛔ 不写；重入时直接返回；失败不重试 ✅</para>
	/// </summary>
	internal static void RunOnce(string source)
	{
		if (!BatteryLimitPlatform.IsBaolongdaMachine())
		{
			return;
		}
		if (Interlocked.Exchange(ref _running, 1) != 0)
		{
			return;
		}
		try
		{
			if (!TryLoad(out bool enabled, out int low, out int high))
			{
				Logger.WriteLine("[battery] 启动核对：无本地配置（" + source + "）⇒ 跳过（⛔ 不写 EC）");
				return;
			}
			if (!BatteryLimitWriter.TryReadState(out int ecHigh, out int ecLow, out int mode, out string stateDetail))
			{
				Logger.WriteLine("[battery] 启动核对：EC 状态读不到（" + source + "）：" + stateDetail + " ⇒ 不恢复");
				return;
			}
			bool needRestore = enabled
				? (mode != BatteryLimitWriter.ModeActive || ecHigh != high || ecLow != low)
				: (mode == BatteryLimitWriter.ModeActive);
			Logger.WriteLine("[battery] 启动核对：EC(" + stateDetail + ") vs 配置(enabled=" + enabled
				+ " high=" + high + " low=" + low + ") ⇒ 需要恢复=" + needRestore + "（来源=" + source + "）");
			if (!needRestore)
			{
				return;
			}
			if (enabled)
			{
				// ⭐ 机主批准：启动恢复也要守「默认禁止高电量启用」（避免开机掉电）✅
				int soc = (int)Math.Round(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
				if (soc >= 0 && soc >= high && !AllowInitialDischarge)
				{
					Logger.WriteLine("[battery] 启动核对：SOC(" + soc + "%) ≥ 配置停充阈值(" + high
						+ "%) 且未允许初次放电 ⇒ 跳过恢复（避免开机掉电）");
					return;
				}
				bool ok = BatteryLimitWriter.TryEnable(low, high, AllowInitialDischarge, out string enableDetail);
				if (BatteryLimitWriter.TryReadState(out int highAfter, out int lowAfter, out int modeAfter, out _) && ok)
				{
					Logger.WriteLine("[battery] 启动恢复：已写回 high=" + highAfter + "/low=" + lowAfter
						+ "（回读=0x" + modeAfter.ToString("X2") + "）");
				}
				else
				{
					Logger.WriteError("[battery] ⛔ 启动恢复启用失败（high=" + high + " low=" + low
						+ "）⇒ 不重试：" + enableDetail);
				}
			}
			else
			{
				bool ok = BatteryLimitWriter.TryDisable(out string disableDetail);
				if (ok)
				{
					Logger.WriteLine("[battery] 启动恢复：已按关闭序列恢复默认充电（回读=0x00）");
				}
				else
				{
					Logger.WriteError("[battery] ⛔ 启动恢复关闭失败 ⇒ 不重试：" + disableDetail);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[battery] ⛔ 启动核对/恢复异常（已吞掉，不重试）：" + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
			Volatile.Write(ref _running, 0);
		}
	}
}
