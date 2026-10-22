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
	internal const string ConfigKey = "battery_voltage_fix";

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
		if (!Enabled)
		{
			// 2026-10-22：上次可能是被强杀（没走到恢复）⇒ 只要落盘原值还在，这里恢复一次。
			try
			{
				I2ecInpOut.Stop();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery-fix] 启动清理落盘原值异常（已吞掉）：" + ex.Message);
			}
			return;
		}
		if (!IsSupportedMachine())
		{
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
