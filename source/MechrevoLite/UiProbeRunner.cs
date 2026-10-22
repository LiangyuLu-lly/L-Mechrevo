using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MechrevoLite.Hardware;

namespace MechrevoLite;

/// <summary>
/// 2026-10-22（机主：UI 探针 —— 强制启用某机型的 UI 看样式）：
/// 无硬件副作用的 **UI 预览模式**：用合成能力位构造窗体，只显示、不下发任何命令。
///
/// <para>用法（CLI，需管理员仅为了正常启动流程；本模式本身不触硬件）：</para>
/// <code>
/// L-Mechrevo.exe --ui-probe list
/// L-Mechrevo.exe --ui-probe wujie:settings
/// L-Mechrevo.exe --ui-probe baolongda:settings
/// L-Mechrevo.exe --ui-probe all:custommode
/// </code>
///
/// <para>可用形态：<c>settings</c>（主界面）、<c>custommode</c>（自定义模式）、<c>fancurve</c>（风扇曲线）；
/// 可用能力档：<c>all</c>（全开）、<c>wujie</c>（无界类）、<c>baolongda</c>（宝龙达类）、<c>gen50</c>（50 系 GCU 类）、<c>minimal</c>（极简）。</para>
///
/// <para>⚠️ 合成 <see cref="MechrevoHw" />（无服务连接）⇒ 任何按钮都不会触达真实硬件；
/// 仅用于检查布局/文案/显隐，不代表真机实际能力。</para>
/// </summary>
internal static class UiProbeRunner
{
	internal static int Run(string argument)
	{
		string text = (argument ?? "").Trim();
		if (text.Length == 0 || text.Equals("list", StringComparison.OrdinalIgnoreCase) || text == "?")
		{
			PrintHelp();
			return 0;
		}
		string profileName;
		string formName;
		int separator = text.IndexOf(':');
		if (separator < 0)
		{
			profileName = text;
			formName = "settings";
		}
		else
		{
			profileName = text.Substring(0, separator).Trim();
			formName = text.Substring(separator + 1).Trim();
		}
		MechrevoDeviceCapabilities caps = BuildCapabilities(profileName);
		if (caps == null)
		{
			Console.WriteLine("未知能力档：" + profileName);
			PrintHelp();
			return 2;
		}
		Form form = BuildForm(formName, caps);
		if (form == null)
		{
			Console.WriteLine("未知窗体：" + formName);
			PrintHelp();
			return 2;
		}
		Program.hw = new MechrevoHw(null, caps);
		Program.UiAuditUseReportedCapabilities = true;
		Logger.WriteLine("[ui-probe] 启动 UI 预览：profile=" + profileName + " form=" + formName + "（合成能力位、无硬件连接、仅显示）");
		string title = "UI 探针 · " + profileName + " · " + formName + "（仅预览，不触硬件）";
		form.Text = title;
		form.FormClosed += delegate
		{
			Logger.WriteLine("[ui-probe] 关闭 UI 预览");
		};
		Application.Run(form);
		return 0;
	}

	private static void PrintHelp()
	{
		Console.WriteLine("UI 探针（强制启用某机型的 UI，仅显示、不触硬件）：");
		Console.WriteLine("  L-Mechrevo.exe --ui-probe list");
		Console.WriteLine("  L-Mechrevo.exe --ui-probe <能力档>:<窗体>");
		Console.WriteLine("  能力档：all（全开）· wujie（无界类）· baolongda（宝龙达类）· gen50（50 系 GCU 类）· minimal（极简）");
		Console.WriteLine("  窗体：settings（主界面）· custommode（自定义模式）· fancurve（风扇曲线）");
		Console.WriteLine("  例：--ui-probe wujie:settings");
		Console.WriteLine("  说明：合成能力位 + 无服务连接 = 只检查布局/显隐/文案，不代表真机能力。");
	}

	private static MechrevoDeviceCapabilities BuildCapabilities(string name)
	{
		bool all = string.Equals(name, "all", StringComparison.OrdinalIgnoreCase);
		bool wujie = string.Equals(name, "wujie", StringComparison.OrdinalIgnoreCase);
		bool baolongda = string.Equals(name, "baolongda", StringComparison.OrdinalIgnoreCase);
		bool gen50 = string.Equals(name, "gen50", StringComparison.OrdinalIgnoreCase);
		bool minimal = string.Equals(name, "minimal", StringComparison.OrdinalIgnoreCase);
		if (!all && !wujie && !baolongda && !gen50 && !minimal)
		{
			return null;
		}
		bool full = all || gen50;
		return new MechrevoDeviceCapabilities
		{
			ProjectId = all ? "UI-PROBE" : (wujie ? "WUJIE" : (baolongda ? "BAOLONGDA" : (gen50 ? "GEN50" : "MINIMAL"))),
			Manufacturer = "MECHREVO",
			Model = all ? "UI 探针（全开）" : (wujie ? "无界（探针）" : (baolongda ? "宝龙达（探针）" : (gen50 ? "50 系 GCU（探针）" : "极简（探针）"))),
			BaseboardProduct = all ? "UI-PROBE" : (wujie ? "WUJIE14XA" : (baolongda ? "MRID6" : (gen50 ? "JIAOLONG" : "MINIMAL"))),
			BiosVersion = "UI-PROBE",
			IsMechrevo = true,
			ProfileAvailable = !minimal,
			Keyboard = all || wujie || baolongda,
			// 无界：暴风雪版 RGB 键盘（单分区）
			Lightbar = all || baolongda,
			RgbLightbar = all || (wujie && false) || baolongda,
			LogoLight = all || baolongda,
			DisplayRefresh = all || gen50 || baolongda,
			ColorCalibration = all || gen50 || baolongda,
			ColorCalibrationLevel = full ? 4 : (baolongda ? 2 : 0),
			DgpuDirect = all || baolongda || gen50,
			IgpuOnly = full,
			FanBoost = all || wujie || baolongda || gen50,
			FanSettings = all || wujie || baolongda,
			OverclockSettings = all || baolongda || gen50,
			CpuPerformanceTuning = all || gen50 || baolongda,
			LiquidCooling = all || gen50,
			LiquidCoolingAutoMode = all || gen50,
			Numpad = full,
			AcRecovery = all || gen50 || baolongda,
			AcRecoveryOn = (all || gen50) ? true : (baolongda ? (bool?)false : null),
			LcdOverdrive = all || gen50,
			LocalDimming = all || gen50,
			TurboMode = all || gen50,
			TurboSubMode = all || gen50,
			SmartBalance = all || gen50,
			RamFan15 = all || gen50,
			NvidiaGpu = all || baolongda || gen50,
			GpuHotSwap = all || baolongda,
			AmdPlatform = wujie || gen50,
			IsOldType = false,
			KeyboardType = all ? 2 : (wujie ? 1 : (baolongda ? 2 : 0))
		};
	}

	private static Form BuildForm(string name, MechrevoDeviceCapabilities caps)
	{
		switch ((name ?? "").Trim().ToLowerInvariant())
		{
		case "settings":
		case "主界面":
			return new SettingsForm();
		case "custommode":
		case "自定义模式":
			return new CustomModeForm();
		case "fancurve":
		case "风扇曲线":
			return new FanCurveForm();
		default:
			return null;
		}
	}
}
