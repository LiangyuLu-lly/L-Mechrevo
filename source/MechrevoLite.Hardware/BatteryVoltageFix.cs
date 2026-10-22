using System;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 8000 系无界的电池充电电压修复（仅内置通道）：
/// 直接经本机已安装的 inpoutx64 驱动访问 I2EC，不调用任何外部脚本。
/// <para>目标：覆盖 EC 辅助老化降额（17600mV → 16600mV），周期保持 17600mV/2816mA + 解停充门，停止时恢复原值。</para>
/// </summary>
internal static class BatteryVoltageFix
{
	/// <summary>2026-10-23（机主：可以用了，改为默认关；且不能与任何非 100% 限充/非长效模式同时开启）。</summary>
	internal const bool FeatureEnabled = true;

	internal const string ConfigKey = "battery_voltage_fix";

	/// <summary>一次性把旧测试配置改回默认关（只执行一次）。</summary>
	private const string DefaultOffResetKey = "battery_voltage_fix_default_off_reset";

	/// <summary>UI 是否显示入口：受总开关与机型双重限制。</summary>
	internal static bool IsUiVisible => FeatureEnabled && IsSupportedMachine();

	/// <summary>只有「不限充」状态才能开：EC 限充开关、无极滑块、任一带百分比限充（&lt;100）、GCU 非长效模式都算冲突。</summary>
	internal static bool IsChargeLimitNeutral(out string reason)
	{
		reason = "";
		try
		{
			if (MechrevoLite.Battery.BatteryControl.LockEnabled())
			{
				reason = "EC 限充开关已启用";
				return false;
			}
			if (AppConfig.Is("charge_limit_slider"))
			{
				int slider = AppConfig.Get("charge_limit", 100);
				if (slider < 100)
				{
					reason = "无极限充滑块=" + slider + "%";
					return false;
				}
			}
			int display = MechrevoLite.Battery.BatteryControl.ResolveDisplayLimitPercent();
			if (display >= 0 && display < 100)
			{
				reason = "当前充电上限=" + display + "%";
				return false;
			}
			MechrevoHw hw = Program.hw;
			if (hw != null && hw.BatteryProtection >= 0 && hw.BatteryProtection != 0)
			{
				reason = "GCU 非长效模式（BatteryProtection=" + hw.BatteryProtection + "，长效=0）";
				return false;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 限充互斥检查异常（按冲突处理）：" + ex.Message);
			reason = "限充状态读取异常";
			return false;
		}
		return true;
	}

	internal static bool Enabled
	{
		get
		{
			try
			{
				return AppConfig.Get(ConfigKey, 0) == 1;
			}
			catch
			{
				return false;
			}
		}
		set
		{
			if (value && !IsChargeLimitNeutral(out string conflict))
			{
				AppConfig.Set(ConfigKey, 0);
				AppConfig.Flush();
				Logger.WriteError("[battery-fix] 拒绝启用：与限充/非长效模式冲突（" + conflict + "）");
				return;
			}
			AppConfig.Set(ConfigKey, value ? 1 : 0);
			AppConfig.Flush();
			if (value)
			{
				// 2026-10-22（机主：注册开机修复，防止 EC 变回去）：SYSTEM 开机任务常驻保持。
				try
				{
					global::Startup.ScheduleBatteryFix();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[battery-fix] 注册开机保持任务失败（已吞掉）：" + ex.Message);
				}
			}
			else
			{
				// 关闭时停掉内置覆盖并恢复 EC 原值。
				try
				{
					global::Startup.UnscheduleBatteryFix();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[battery-fix] 移除开机保持任务失败（已吞掉）：" + ex.Message);
				}
				Stop();
			}
		}
	}

	internal static bool IsSupportedMachine()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return false;
		}
		if (hw.WujieSmuApu)
		{
			return true;
		}
		// 2026-10-22（UI 探针）：探测模式的合成 hw 不会让 WujieSmuApu 变真（它读真机身份/CPU），
		// 但 wujie 能力档的 Model/Board 含 无界/WUJIE ⇒ 也把这个开关显示出来，便于检查 UI。
		if (Program.UiAuditUseReportedCapabilities)
		{
			string text = ((hw.Capabilities?.Model ?? "") + " " + (hw.Capabilities?.BaseboardProduct ?? "")).Trim();
			return text.IndexOf("WUJIE", StringComparison.OrdinalIgnoreCase) >= 0
				|| text.IndexOf("无界", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	/// <summary>停掉内置覆盖（幂等）。</summary>
	internal static void Stop()
	{
		try
		{
			I2ecInpOut.Stop();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 停止内置覆盖异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	internal static string DescribeAvailability()
	{
		if (I2ecInpOut.TryReadStatus(out string nativeDetail))
		{
			return "内置可用（inpoutx64）：\n" + nativeDetail;
		}
		return "内置不可用：" + I2ecInpOut.InitDetail;
	}

	/// <summary>
	/// 执行修复动作：<c>hold-full</c> / <c>status</c>，只走内置 I2EC 通道；失败时如实返回原因，不回退外部脚本。
	/// </summary>
	internal static (bool Ok, string Detail) Run(string argument)
	{
		bool statusOnly = string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase);
		if (!FeatureEnabled && !statusOnly)
		{
			return (Ok: false, Detail: "无界电池电压修复已暂时屏蔽（实验性功能）");
		}
		if (!statusOnly && !IsChargeLimitNeutral(out string conflict))
		{
			Logger.WriteError("[battery-fix] 拒绝执行：与限充/非长效模式冲突（" + conflict + "）");
			return (Ok: false, Detail: "与限充/非长效模式冲突（" + conflict + "），请先把充电上限调到 100%/长效模式");
		}
		// 2026-10-22（无界集成）：内置通道要求 inpoutx64；随包 DLL/sys 在但驱动没装时先自动安装一次。
		if (!I2ecInpOut.IsAvailable)
		{
			InpOutDriverInstaller.TryEnsureInstalled(out string installDetail);
			Logger.WriteLine("[battery-fix] inpoutx64 自动安装结果：" + installDetail);
		}
		if (string.Equals(argument, "hold-full", StringComparison.OrdinalIgnoreCase))
		{
			if (I2ecInpOut.TryStart(out string nativeStartDetail))
			{
				return (Ok: true, Detail: "内置（inpoutx64）：" + nativeStartDetail);
			}
			Logger.WriteLine("[battery-fix] 内置不可用：" + nativeStartDetail);
			return (Ok: false, Detail: "内置不可用（" + I2ecInpOut.InitDetail + "）：" + nativeStartDetail);
		}
		if (string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase))
		{
			if (I2ecInpOut.TryReadStatus(out string nativeStatus))
			{
				return (Ok: true, Detail: nativeStatus);
			}
			return (Ok: false, Detail: "内置不可用（" + I2ecInpOut.InitDetail + "）");
		}
		return (Ok: false, Detail: "未知动作：" + argument);
	}

	/// <summary>只读状态（内置）。</summary>
	internal static (bool Ok, string Detail) ReadStatus()
	{
		return Run("status");
	}

	internal static void RunOnStartupIfEnabled()
	{
		// 2026-10-23（机主：改成默认关）：旧测试版可能保存了启用态，升级后一次性清掉。
		try
		{
			if (AppConfig.Get(DefaultOffResetKey, 0) == 0)
			{
				AppConfig.Set(ConfigKey, 0);
				AppConfig.Set(DefaultOffResetKey, 1);
				AppConfig.Flush();
				Logger.WriteLine("[battery-fix] 默认关：已清理旧启用态（一次性）");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 默认关清理异常（已吞掉）：" + ex.Message);
		}
		if (!FeatureEnabled)
		{
			// 2026-10-22（机主决定暂时屏蔽）：停掉可能残留的内置覆盖，并清掉已保存的启用态，
			// 避免旧配置/旧 SYSTEM 任务在后台继续改 EC。
			try
			{
				Stop();
				if (Enabled)
				{
					Enabled = false;
					Logger.WriteLine("[battery-fix] 实验性功能已屏蔽 ⇒ 已关闭旧配置并停止覆盖");
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery-fix] 屏蔽清理异常（已吞掉）：" + ex.Message);
			}
			return;
		}
		if (!Enabled)
		{
			// 2026-10-22：上次可能是被强杀（没走到恢复）⇒ 只要落盘原值还在，这里恢复一次。
			// 2026-10-23（机主：关闭时清理开机自动应用）：同时删掉可能残留的 SYSTEM 任务。
			try
			{
				I2ecInpOut.Stop();
				global::Startup.UnscheduleBatteryFix();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery-fix] 启动清理落盘原值/开机任务异常（已吞掉）：" + ex.Message);
			}
			return;
		}
		if (!IsSupportedMachine())
		{
			return;
		}
		if (!IsChargeLimitNeutral(out string conflict))
		{
			Logger.WriteError("[battery-fix] 开机不执行：与限充/非长效模式冲突（" + conflict + "）⇒ 已关闭开关");
			Enabled = false;
			return;
		}
		_ = Task.Run(delegate
		{
			try
			{
				(bool ok, string detail) = Run("hold-full");
				Logger.WriteLine("[battery-fix] 开机 hold-full " + (ok ? "成功" : "失败") + " · " + detail);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery-fix] 开机 hold-full 异常（已吞掉）：" + ex.Message);
			}
		});
	}
}
