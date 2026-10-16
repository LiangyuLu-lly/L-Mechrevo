using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using MechrevoLite.Battery;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using Microsoft.Win32;
using PawnIO;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticSystemInfo
{
	internal static string Build()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("L-Mechrevo 诊断包 · 系统信息");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("生成时间: ");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" (本地时间)");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine();
		AppendApp(stringBuilder);
		AppendWindows(stringBuilder);
		AppendMachine(stringBuilder);
		// 2026-10-14（用户原话「要在 LM 里加一条【只读日志】打出机型码字段」）：紧跟 [机型] 之后追加
		// [机型码字段（只读）] 一节 —— 把 Win32_BaseBoard.Product/Manufacturer/Version、
		// Win32_ComputerSystem.Model/Manufacturer/SystemFamily/SystemSKUNumber、
		// Win32_BIOS.SMBIOSBIOSVersion/Version、Win32_ComputerSystemProduct.Name/Version
		// 以及**复用的** BIOS 项目码（ModelRegistry.Read → ModelIdentity.BiosProjectId）原始值写进包。
		// ⚠️ 为什么要进包：默认日志级别是「仅错误」，启动时那条 Info 行默认**看不到** ——
		// 诊断包（机主已经会导出）才是默认级别下唯一可靠的出口。⛔ 严格只读（只查 WMI + 既有 EC 读通道），
		// ⛔ 不写注册表/EC/ACPI/NVRAM；⛔ 序列号字段从不读取；WMI 失败写 unavailable，不抛。
		stringBuilder.Append(MachineCodeProbe.BuildSection());
		AppendCpu(stringBuilder);
		AppendGpus(stringBuilder);
		// 2026-10-09（诊断增强）：[GPU] 之后追加「配置与注册表」一节 ——
		// LM 关键配置 / 档位已保存值 / 注册表（L-Mechrevo·RGBKeyboard·ItemSupport·服务·BIOS）/
		// GCU 已上报已解析的身份与能力 / 服务·进程·13688 会话持有者。
		// 纯只读（不写配置/注册表/EC、不发 MQTT 请求、不连 13688），每个子块独立 try/catch。
		stringBuilder.Append(DiagnosticConfigInfo.Build());
		// 2026-10-09（诊断补采）：紧跟「配置与注册表」之后追加三节 ——
		// [GCU 装后自检]（%ProgramData%\L-Mechrevo\gcu-install-status.json 的 Status/Reason/Checks + 原文全文）、
		// [GCU 关键文件（大小 + SHA256 前 16 位 + FileVersion）]（含「40A 是否落地」结论式提示）、
		// [GCU 就绪键（ItemSupport）]（ServiceReady / ServiceLoadingReady / PerformanceReady 逐个"存在=值/不存在"）、
		// [GCU 侧日志（ControlCenter / MyControlCenter）]（清单 + 每文件 ≤64 KB 尾部 + 关键行抽取，↔ 2026-10-09 第二批扩写）。
		// 纯只读：不写任何文件（不写 GCU 目录/ProgramData）、不新增 MQTT 订阅或 GET、不连 13688、不启停服务。
		stringBuilder.Append(DiagnosticGcuEvidenceInfo.Build());
		// 2026-10-09（诊断补采·第二批）：再追加三节 ——
		// [GCU 安装日志与装后自检状态]（%ProgramData%\L-Mechrevo[\logs]\gcu-install-*.log 清单 + 尾部；两处都没有就明确写"不存在"＝旧安装器的证据）、
		// [GCU 用户档案清单（UserPofiles）]（只列文件名/大小/修改时间，**绝不复制档案内容**；并对照 16 项期望清单报缺失）、
		// [GCU 底座目录清单]（底座根目录树 ≤3 层 + 关键文件名/大小/时间戳 + 安装根 1 层）。
		// 同样纯只读，且与上一节共用同一份打码/尾部实现（口径只有一份）。
		stringBuilder.Append(DiagnosticGcuInventoryInfo.Build());
		// 2026-10-09（诊断增强）：把显示器枚举 / 活动路径 / 适配器刷新率 / EDID 标识 /
		// GCU 侧 HzList·CurrentHz 一并写进包 —— 只加采集，失败也只写"该项读取失败"。
		stringBuilder.Append(DiagnosticDisplayInfo.Build());
		AppendLiquidCooling(stringBuilder);
		AppendCapabilities(stringBuilder);
		AppendPowerInput(stringBuilder);
		// 2026-10-10（诊断补采 · 限充 EC 寄存器）：把限充相关 EC 寄存器**只读**探测一遍写进包 ——
		// 0x07B9（实时上限）/ 0x07D0（复充下限：上游不写、LM 自 2026-10-10 起也停写，只读比对固件推导值）/ 0x07C3（产品线门控）/ 0x0742（上游口径的"生效位"）/
		// 0x0770（ROMID 首字节）/ 0x087F（固件存储区，上游指明是 H2RAM 盲区）。
		// ⚠️ 有了这一节，下一份诊断包才能直接判别"被外部改回"还是"写进去了但没生效"。
		// ⛔ 纯只读：不写 EC/注册表/配置，不改写入行为，不改任何状态；读不到就如实写"不可读 + 原因"。
		AppendChargeLimitEc(stringBuilder);
		return stringBuilder.ToString();
	}

	private static void AppendApp(StringBuilder sb)
	{
		sb.AppendLine("[应用]");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
		handler.AppendLiteral("应用版本（更新器检测串）: ");
		handler.AppendFormatted(Program.ReleaseVersion);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("显示标签: ");
		handler.AppendFormatted(Program.ReleaseLabel);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("可执行文件: ");
		handler.AppendFormatted(Environment.ProcessPath ?? AppContext.BaseDirectory);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("进程架构: ");
		handler.AppendFormatted(RuntimeInformation.ProcessArchitecture);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("管理员运行: ");
		handler.AppendFormatted(YesNo(Safe(() => ProcessHelper.IsUserAdministrator())));
		stringBuilder6.AppendLine(ref handler);
		sb.AppendLine();
	}

	private static void AppendWindows(StringBuilder sb)
	{
		string text = "未知";
		string text2 = "";
		string text3 = Environment.OSVersion.Version.Build.ToString();
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
			if (registryKey != null)
			{
				text = (registryKey.GetValue("ProductName") as string)?.Trim() ?? text;
				text2 = (registryKey.GetValue("DisplayVersion") as string)?.Trim() ?? "";
				string text4 = (registryKey.GetValue("CurrentBuildNumber") as string)?.Trim() ?? text3;
				string text5 = registryKey.GetValue("UBR")?.ToString() ?? "";
				text3 = (string.IsNullOrEmpty(text5) ? text4 : (text4 + "." + text5));
			}
		}
		catch
		{
		}
		sb.AppendLine("[Windows]");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("产品: ");
		handler.AppendFormatted(text);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 3, stringBuilder);
		handler.AppendLiteral("版本: ");
		handler.AppendFormatted(Environment.OSVersion.Version);
		handler.AppendLiteral(" (build ");
		handler.AppendFormatted(text3);
		handler.AppendLiteral(")");
		handler.AppendFormatted((text2.Length > 0) ? (" · " + text2) : "");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("架构: ");
		handler.AppendFormatted(RuntimeInformation.OSArchitecture);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("描述: ");
		handler.AppendFormatted(RuntimeInformation.OSDescription);
		stringBuilder5.AppendLine(ref handler);
		sb.AppendLine();
	}

	private static void AppendMachine(StringBuilder sb)
	{
		MechrevoDeviceCapabilities mechrevoDeviceCapabilities = Safe(() => MechrevoDeviceCapabilities.Current) ?? new MechrevoDeviceCapabilities();
		sb.AppendLine("[机型]");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("厂商: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.Manufacturer));
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("型号: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.Model));
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("系列: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.SystemFamily));
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
		handler.AppendLiteral("SKU: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.SystemSku));
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("主板: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.BaseboardProduct));
		stringBuilder6.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder7 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("BIOS: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.BiosVersion));
		stringBuilder7.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder8 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("项目 ID: ");
		handler.AppendFormatted(Or(mechrevoDeviceCapabilities.ProjectId));
		stringBuilder8.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder9 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("奥驰是否识别为机械革命: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.IsMechrevo));
		stringBuilder9.AppendLine(ref handler);
		sb.AppendLine();
	}

	private static void AppendCpu(StringBuilder sb)
	{
		string value = Safe(() => CpuInfo.Name) ?? "";
		string text = Safe(() => CpuInfo.Caption) ?? "";
		sb.AppendLine("[CPU]");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("名称: ");
		handler.AppendFormatted(Or(value));
		stringBuilder2.AppendLine(ref handler);
		if (text.Length > 0)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
			handler.AppendLiteral("标识: ");
			handler.AppendFormatted(text);
			stringBuilder3.AppendLine(ref handler);
		}
		sb.AppendLine();
	}

	private static void AppendGpus(StringBuilder sb)
	{
		sb.AppendLine("[GPU]");
		List<string>? obj = Safe(QueryGpus) ?? new List<string>();
		if (obj.Count == 0)
		{
			sb.AppendLine("未能枚举显示适配器（WMI 不可用）。");
		}
		foreach (string item in obj)
		{
			sb.AppendLine(item);
		}
		sb.AppendLine();
	}

	private static List<string> QueryGpus()
	{
		List<string> list = new List<string>();
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				string value = item["Name"]?.ToString()?.Trim() ?? "未知";
				string value2 = item["DriverVersion"]?.ToString()?.Trim() ?? "";
				string text = "";
				if (item["DriverDate"] is DateTime dateTime)
				{
					text = dateTime.ToString("yyyy-MM-dd");
				}
				list.Add($"{value}（驱动 {Or(value2)}{((text.Length > 0) ? (" · " + text) : "")}）");
			}
		}
		catch
		{
		}
		return list;
	}

	private static void AppendLiquidCooling(StringBuilder sb)
	{
		sb.AppendLine("[液冷 / GCU]");
		MechrevoHw hw = Program.hw;
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
		handler.AppendLiteral("GCU 已连接: ");
		// 2026-10-15（C-1 收尾）：本节标题与字面量都写明「GCU」，故这里必须用 GCU 语义的 IsGcuConnected，
		// 而不是 IsConnected（= UsesLegacyMifs || IsGcuConnected）。否则老机型接口（MICommonInterface）机型上
		// 会显示「今天 GCU 已连接: 是」，与「GCU 未连接」的事实矛盾，也会让诊断包误导读日志的人。
		handler.AppendFormatted(YesNo(hw?.IsGcuConnected ?? false));
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("GCU 版本: ");
		handler.AppendFormatted(ReadGcuVersion());
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("固件版本: ");
		handler.AppendFormatted(Or(hw?.LcFwVersion));
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("已发现设备数: ");
		handler.AppendFormatted(hw?.LcDeviceMacs.Count ?? 0);
		stringBuilder5.AppendLine(ref handler);
		sb.AppendLine();
	}

	/// <summary>
	/// 读取 GCU 服务版本（优先 GCUService.exe，其次 GCUBridge.exe）。
	/// 2026-10-04：原实现只探 LM 自带目录 {App}\GCU\AiStoneService\...，用官方控制台安装 GCU
	/// （或 NoGCU 版 LM）时该目录不存在，诊断包永远显示"未找到 GCU 服务文件"；
	/// 现补充由服务映像路径推导的候选，见 EnumerateGcuVersionCandidates。
	/// </summary>
	private static string ReadGcuVersion()
	{
		try
		{
			foreach (string path in EnumerateGcuVersionCandidates())
			{
				if (!File.Exists(path))
				{
					continue;
				}
				System.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
				string text = versionInfo.ProductVersion ?? versionInfo.FileVersion;
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text + " (" + Path.GetFileName(path) + ")";
				}
				return "(无版本信息) " + Path.GetFileName(path);
			}
			return "未知（未找到 GCU 服务文件）";
		}
		catch (Exception ex)
		{
			return "读取失败: " + ex.Message;
		}
	}


	/// <summary>
	/// 按优先级枚举 GCU 服务可执行文件候选路径（已去重）。
	/// 1) LM 自带 GCU 目录（LM 安装器安装的 GCU）；
	/// 2) 由已注册服务映像路径 HKLM\SYSTEM\CurrentControlSet\Services\&lt;名&gt;\ImagePath 推导，
	///    官方控制台布局为 &lt;UniwillService&gt;\MyControlCenter\GCUService.exe（本机 1.0.2.70）。
	/// GCUService.exe 优先于 GCUBridge.exe —— 前者版本号更能标识 GCU 构建。
	/// </summary>
	private static List<string> EnumerateGcuVersionCandidates()
	{
		List<string> list = new List<string>();
		AddGcuCandidate(list, Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "MyControlCenter", "GCUService.exe"));
		AddGcuCandidate(list, Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "GCUBridge.exe"));
		string[] serviceNames = new string[2] { "GCUService", "GCUBridge" };
		foreach (string serviceName in serviceNames)
		{
			string exe = null;
			try
			{
				string imagePath = GcuCoexistence.ReadServiceImagePath(serviceName);
				if (!string.IsNullOrWhiteSpace(imagePath))
				{
					exe = imagePath.Trim().Trim('"');
					if (exe.StartsWith(@"\??\", StringComparison.Ordinal))
					{
						exe = exe.Substring(4);
					}
					if (!Path.IsPathRooted(exe))
					{
						exe = null;
					}
				}
			}
			catch
			{
				exe = null;
			}
			if (exe == null)
			{
				continue;
			}
			string dir = null;
			try
			{
				dir = Path.GetDirectoryName(exe);
			}
			catch
			{
				dir = null;
			}
			if (!string.IsNullOrWhiteSpace(dir))
			{
				AddGcuCandidate(list, Path.Combine(dir, "MyControlCenter", "GCUService.exe"));
				AddGcuCandidate(list, Path.Combine(dir, "GCUService.exe"));
			}
			AddGcuCandidate(list, exe);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				AddGcuCandidate(list, Path.Combine(dir, "GCUBridge.exe"));
			}
		}
		return list;
	}

	private static void AddGcuCandidate(List<string> list, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		foreach (string item in list)
		{
			if (string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		list.Add(path);
	}

	private static void AppendCapabilities(StringBuilder sb)
	{
		MechrevoDeviceCapabilities mechrevoDeviceCapabilities = Safe(() => MechrevoDeviceCapabilities.Current) ?? new MechrevoDeviceCapabilities();
		sb.AppendLine("[能力位]");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("机型画像可用: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.ProfileAvailable));
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("键盘灯效: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.Keyboard));
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("灯条: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.Lightbar));
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("可寻址灯条: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.RgbLightbar));
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("Logo 灯: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.LogoLight));
		stringBuilder6.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder7 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
		handler.AppendLiteral("液冷: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.LiquidCooling));
		stringBuilder7.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder8 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("液冷自动模式: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.LiquidCoolingAutoMode));
		stringBuilder8.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder9 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("显卡直连: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.DgpuDirect));
		stringBuilder9.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder10 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("核显模式: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.IgpuOnly));
		stringBuilder10.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder11 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("显卡热切换: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.GpuHotSwap));
		stringBuilder11.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder12 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
		handler.AppendLiteral("NVIDIA 显卡: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.NvidiaGpu));
		stringBuilder12.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder13 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("AMD 平台: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.AmdPlatform));
		stringBuilder13.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder14 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("风扇设置: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.FanSettings));
		stringBuilder14.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder15 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("风扇增强: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.FanBoost));
		stringBuilder15.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder16 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("屏幕刷新率: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.DisplayRefresh));
		stringBuilder16.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder17 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("屏幕校色: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.ColorCalibration));
		stringBuilder17.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder18 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("响应加速: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.LcdOverdrive));
		stringBuilder18.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder19 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("CPU 调优: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.CpuPerformanceTuning));
		stringBuilder19.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder20 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("超频设置: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.OverclockSettings));
		stringBuilder20.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder21 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder);
		handler.AppendLiteral("Turbo 模式: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.TurboMode));
		stringBuilder21.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder22 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
		handler.AppendLiteral("Turbo 子模式: ");
		handler.AppendFormatted(YesNo(mechrevoDeviceCapabilities.TurboSubMode));
		stringBuilder22.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder23 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("键盘类型: ");
		handler.AppendFormatted(mechrevoDeviceCapabilities.KeyboardType);
		stringBuilder23.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder24 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("刷新率档位: ");
		handler.AppendFormatted(mechrevoDeviceCapabilities.DisplayRefreshLevel);
		stringBuilder24.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilderCalib = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("校色档位数: ");
		handler.AppendFormatted(mechrevoDeviceCapabilities.ColorCalibrationLevel);
		stringBuilderCalib.AppendLine(ref handler);
	}

	private static void AppendPowerInput(StringBuilder sb)
	{
		sb.AppendLine("[供电]");
		MechrevoLite.HardwareControl.RefreshPowerInput(true);
		PowerInputSample? powerInputSample = MechrevoLite.HardwareControl.PowerInput;
		if (powerInputSample == null)
		{
			sb.AppendLine("供电采样未就绪。");
			sb.AppendLine();
			return;
		}
		if (!powerInputSample.DecodeEnabled)
		{
			sb.AppendLine("供电采样未启用（机型不支持 EC 供电解码）。");
			sb.AppendLine();
			return;
		}
		string text = powerInputSample.Kind switch
		{
			PowerInputKind.Battery => "电池供电",
			PowerInputKind.Barrel => "圆口供电",
			PowerInputKind.TypeC => "Type-C 供电",
			PowerInputKind.BarrelAndTypeC => "圆口 + Type-C",
			PowerInputKind.ExternalUnknown => "外部供电（方式未知）",
			_ => "未知"
		};
		sb.AppendLine("供电方式: " + text);
		if (powerInputSample.ComplexStatusRaw >= 0)
		{
			sb.AppendLine("复杂状态 0x7CC: 0x" + powerInputSample.ComplexStatusRaw.ToString("X2"));
		}
		else
		{
			sb.AppendLine("复杂状态 0x7CC: 读取失败");
		}
		if (powerInputSample.BiosInfo3Raw >= 0)
		{
			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (PowerInputDecoder.DisplayAdapterWatts(adapterWatts.Value) + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));
			sb.AppendLine("适配器瓦数 0x49F: 0x" + powerInputSample.BiosInfo3Raw.ToString("X2") + " → " + wattsText);
		}
		else
		{
			sb.AppendLine("适配器瓦数 0x49F: 读取失败");
		}
		sb.AppendLine();
	}

	/// <summary>
	/// 2026-10-10（诊断补采 · 限充 EC 寄存器）：[充电上限 EC 寄存器（只读探测）] 一节。
	/// 目的：让**下一份诊断包**能直接判别「被外部改回」还是「写进去了但没生效」——
	/// 0x07B9 回读只说明写进去了，0x0742 bit2 才说明生效；0x07C3 能看出门控是否残留。
	/// ⛔ 纯只读；读不到就如实写"不可读 + 原因"，⛔ 不据此改任何写入行为。
	/// </summary>
	private static void AppendChargeLimitEc(StringBuilder sb)
	{
		sb.AppendLine("[充电上限 EC 寄存器（只读探测）]");
		ChargeLimitEcProbe probe;
		try
		{
			probe = EcChargeLimit.ProbeRegisters();
		}
		catch (Exception ex)
		{
			sb.AppendLine("探测失败: " + ex.Message);
			sb.AppendLine();
			return;
		}
		sb.AppendLine("通道: " + probe.ChannelNote);
		sb.AppendLine($"0x{EcChargeLimit.UpperRegister:X3} 实时充电上限: {DescribeRaw(probe.Upper)}（⭐ 原始字节＝bit7「限充已启用」标志 | 低 7 位百分数；实测 0xD0=bit7|0x50=80%、0xBC=bit7|0x3C=60%、0x00=不限充）");
		int expectedLower = ((probe.Upper < 0) ? (-1) : EcChargeLimit.LowerValueFor(EcChargeLimit.PercentFor(probe.Upper)));
		string lowerCompare = ((probe.Lower < 0 || expectedLower < 0)
			? "（0x7B9 或 0x7D0 读不到，无法比对）"
			: ((probe.Lower == expectedLower)
				? $"（== 上限 − {EcChargeLimit.RechargeHysteresis} = {expectedLower} ⇒ 固件自己推的下限成立，我们以前那笔显式写入是冗余的）"
				: $"（≠ 期望 {expectedLower}（上限 − {EcChargeLimit.RechargeHysteresis}）⇒ 本机固件可能不推导下限、该值仍是旧值：只影响回充时机，**不影响充电上限**）"));
		sb.AppendLine($"0x{EcChargeLimit.LowerRegister:X3} 复充下限: {DescribeRaw(probe.Lower)}（上游**不写**、交由固件 store_limit() 推导；LM 自 2026-10-10 起只读不改：{lowerCompare}）");
		sb.AppendLine($"0x{EcChargeLimit.GateRegister:X3} 产品线门控: {DescribeRaw(probe.Gate)}（上游：非 4/5 时固件不跑限充控制循环）");
		sb.AppendLine($"0x{EcChargeLimit.EffectRegister:X3} 生效位: {DescribeRaw(probe.Effect)} → {probe.DescribeEffect()}");
		sb.AppendLine($"0x{EcChargeLimit.RomIdRegister:X3} ROMID 首字节: {DescribeRaw(probe.RomId)}（上游用它做 4/5 产品线判断）");
		sb.AppendLine($"0x{EcChargeLimit.StoredLimitRegister:X3} 固件存储区: {DescribeRaw(probe.StoredLimit)}（上游指明是 H2RAM 盲区，主机通常读不到）");
		sb.AppendLine("⚠️ 读成 0xFF 时可能是通道对未映射地址的填充值（本机实测 0x770/0x87F 都读成 0xFF），不等于真实值；" + $"0x{EcChargeLimit.GateRegister:X3}=4/5 表示门控开着（LM 用完会还原成原值，残留即还原失败）。");
		sb.AppendLine("判定口径（上游 Leo-stro/mechrevo-charge-limit）: 写 0x7B9 + 门控 0x7C3=4 保持 2.5s → 还原 0x7C3 并回读确认 → 等 ≥7s 读 0x0742 bit2=1 才算生效；0x0742 读不到 = 无法判定（既非成功也非失败）");
		ChargeLimitEffectRecord latest = ChargeLimitEffectEvidence.Latest;
		if (latest.CheckedAt == DateTime.MinValue)
		{
			sb.AppendLine("最近一次真效果判据: 本次会话尚未执行（还没有写入过限充）");
		}
		else
		{
			bool? bit = latest.Probe.EffectBitSet;
			sb.AppendLine($"最近一次真效果判据: {latest.DescribeVerdict()}（命令上限 {latest.Limit}%，判定时间 {latest.CheckedAt:yyyy-MM-dd HH:mm:ss}，0x{EcChargeLimit.EffectRegister:X3}={ChargeLimitEcProbe.FormatRaw(latest.Probe.Effect)}，bit2={(bit.HasValue ? (bit.Value ? "1" : "0") : "?")}）");
		}
		int ecPercent = ((probe.Upper < 0) ? (-1) : EcChargeLimit.PercentFor(probe.Upper));
		sb.AppendLine($"旧判据（0x{EcChargeLimit.UpperRegister:X3} 回读）换算上限: {((ecPercent < 0) ? "不可读" : (ecPercent + "%"))}（已掩 bit7：先 & 0x{EcChargeLimit.PercentMask:X2} 再 Clamp {EcChargeLimit.MinimumPercent}–{EcChargeLimit.MaximumPercent}）");
		sb.AppendLine($"LM 配置: charge_limit={AppConfig.Get("charge_limit", 100)}、charge_limit_lock={AppConfig.Get("charge_limit_lock", 0)}、charge_limit_mode={AppConfig.Get("charge_limit_mode", 80)}、判定键 {EcChargeLimit.VerdictKey}={AppConfig.GetString(EcChargeLimit.VerdictKey)}");
		sb.AppendLine();
	}

	private static string DescribeRaw(int raw)
	{
		if (raw < 0)
		{
			return "不可读";
		}
		return "0x" + raw.ToString("X2") + " (" + raw + ")";
	}

	private static string YesNo(bool value)
	{
		if (!value)
		{
			return "否";
		}
		return "是";
	}

	private static string Or(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return "未知";
	}

	private static T? Safe<T>(Func<T> read)
	{
		try
		{
			return read();
		}
		catch
		{
			return default;
		}
	}
}
