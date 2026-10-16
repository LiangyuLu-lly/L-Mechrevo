using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;
using MechrevoLite.Display;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-09（诊断增强）：诊断包新增「显示配置」一节。
/// 背景：极光X「多显示器刷新率」与极光Pro23「GCU 连不上」两件事定性时，包里**完全没有任何显示器信息**，
/// 只能靠机主口述"外接屏是 300Hz"。本节把"系统侧看到几块屏、哪块是 eDP 内屏、各自分辨率/刷新率、
/// 活动路径输出技术、以及 GCU 上报的 HzList/CurrentHz"一次性落进包，
/// 这样**一份包**就能看出「GCU 报的档位 vs 系统侧实测」的差异。
/// ⚠️ 只加采集，不改任何判定/显示设置；任一子项读取失败都只写"该项读取失败"，绝不让诊断包导出失败。
/// </summary>
internal static class DiagnosticDisplayInfo
{
	public static string Build()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[显示配置]");
		try
		{
			AppendInternalVerdict(stringBuilder);
			AppendActivePaths(stringBuilder);
			AppendGdiScreens(stringBuilder);
			AppendVideoControllers(stringBuilder);
			AppendMonitorEdid(stringBuilder);
			AppendGcuRefresh(stringBuilder);
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine("显示配置采集整体失败（其余各节不受影响）：" + ex.Message);
		}
		stringBuilder.AppendLine();
		return stringBuilder.ToString();
	}

	private static void AppendInternalVerdict(StringBuilder sb)
	{
		try
		{
			string? text = ScreenNative.FindLaptopScreen(out ScreenNative.InternalScreenConfidence confidence, out string technology, log: false);
			sb.AppendLine($"内屏识别：device={text ?? "(null)"} tech={technology} confidence={confidence}");
			sb.AppendLine((confidence == ScreenNative.InternalScreenConfidence.Embedded) ? "  ⇒ 判定为 eDP/内屏（INTERNAL 或 DISPLAYPORT_EMBEDDED）：刷新率校正/落盘口径以这块屏为准。" : "  ⇒ 未能确认为 eDP 内屏：按既有硬口径，不应据此校正刷新率、也不应把实测值写进 config。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("内屏识别：该项读取失败（" + ex.Message + "）");
		}
	}

	private static void AppendActivePaths(StringBuilder sb)
	{
		sb.AppendLine("活动路径（QueryDisplayConfig · QDC_ONLY_ACTIVE_PATHS）：");
		try
		{
			List<string> list = QueryActivePaths();
			if (list.Count == 0)
			{
				sb.AppendLine("  未枚举到活动路径（该项读取失败或当前无活动屏）。");
				return;
			}
			int num = 0;
			foreach (string item in list)
			{
				num++;
				sb.AppendLine($"  #{num} {item}");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	private static List<string> QueryActivePaths()
	{
		List<string> list = new List<string>();
		uint numPathArrayElements = 0u;
		uint numModeInfoArrayElements = 0u;
		DisplayNative.QUERY_DEVICE_CONFIG_FLAGS flags = DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
		int displayConfigBufferSizes = DisplayNative.GetDisplayConfigBufferSizes(flags, out numPathArrayElements, out numModeInfoArrayElements);
		if (displayConfigBufferSizes != 0)
		{
			throw new InvalidOperationException("GetDisplayConfigBufferSizes rc=" + displayConfigBufferSizes);
		}
		DisplayNative.DISPLAYCONFIG_PATH_INFO[] array = new DisplayNative.DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
		DisplayNative.DISPLAYCONFIG_MODE_INFO[] array2 = new DisplayNative.DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];
		displayConfigBufferSizes = DisplayNative.QueryDisplayConfig(flags, ref numPathArrayElements, array, ref numModeInfoArrayElements, array2, IntPtr.Zero);
		if (displayConfigBufferSizes != 0)
		{
			throw new InvalidOperationException("QueryDisplayConfig rc=" + displayConfigBufferSizes);
		}
		for (int i = 0; i < numPathArrayElements; i++)
		{
			DisplayNative.DISPLAYCONFIG_PATH_INFO dISPLAYCONFIG_PATH_INFO = array[i];
			DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName = default(DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME);
			deviceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
			deviceName.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(deviceName);
			deviceName.header.adapterId = dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId;
			deviceName.header.id = dISPLAYCONFIG_PATH_INFO.targetInfo.id;
			string text = "(target name 读取失败)";
			string text2 = "";
			DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology = dISPLAYCONFIG_PATH_INFO.targetInfo.outputTechnology;
			if (DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName) == 0)
			{
				text = string.IsNullOrWhiteSpace(deviceName.monitorFriendlyDeviceName) ? "(无友好名)" : deviceName.monitorFriendlyDeviceName;
				text2 = deviceName.monitorDevicePath ?? "";
				outputTechnology = deviceName.outputTechnology;
			}
			DisplayNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName2 = default(DisplayNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME);
			deviceName2.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
			deviceName2.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(deviceName2);
			deviceName2.header.adapterId = dISPLAYCONFIG_PATH_INFO.sourceInfo.adapterId;
			deviceName2.header.id = dISPLAYCONFIG_PATH_INFO.sourceInfo.id;
			string text3 = (DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName2) == 0) ? deviceName2.viewGdiDeviceName : "(source name 读取失败)";
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			for (int j = 0; j < numModeInfoArrayElements; j++)
			{
				DisplayNative.DISPLAYCONFIG_MODE_INFO dISPLAYCONFIG_MODE_INFO = array2[j];
				if (dISPLAYCONFIG_MODE_INFO.infoType == DisplayNative.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE && SameLuid(dISPLAYCONFIG_MODE_INFO.adapterId, dISPLAYCONFIG_PATH_INFO.sourceInfo.adapterId) && dISPLAYCONFIG_MODE_INFO.id == dISPLAYCONFIG_PATH_INFO.sourceInfo.id)
				{
					num = (int)dISPLAYCONFIG_MODE_INFO.modeInfo.sourceMode.width;
					num2 = (int)dISPLAYCONFIG_MODE_INFO.modeInfo.sourceMode.height;
				}
				if (dISPLAYCONFIG_MODE_INFO.infoType == DisplayNative.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET && SameLuid(dISPLAYCONFIG_MODE_INFO.adapterId, dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId) && dISPLAYCONFIG_MODE_INFO.id == dISPLAYCONFIG_PATH_INFO.targetInfo.id)
				{
					uint numerator = dISPLAYCONFIG_MODE_INFO.modeInfo.targetMode.targetVideoSignalInfo.vSyncFreq.Numerator;
					uint denominator = dISPLAYCONFIG_MODE_INFO.modeInfo.targetMode.targetVideoSignalInfo.vSyncFreq.Denominator;
					if (denominator != 0)
					{
						num3 = (int)Math.Round((double)numerator / (double)denominator);
					}
				}
			}
			bool flag = outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED;
			bool flag2 = outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL;
			string text4 = ((flag || flag2) ? "内屏" : "外接/虚拟屏");
			list.Add($"友好名={text} 输出技术={outputTechnology}(0x{(uint)outputTechnology:X8}) GDI={text3} 分辨率={num}x{num2} 刷新率={num3}Hz 分类={text4} 设备路径={text2}");
		}
		return list;
	}

	private static bool SameLuid(DisplayNative.LUID a, DisplayNative.LUID b)
	{
		return a.LowPart == b.LowPart && a.HighPart == b.HighPart;
	}

	private static void AppendGdiScreens(StringBuilder sb)
	{
		sb.AppendLine("GDI 屏（System.Windows.Forms.Screen，含 SystemInformation 兜底）：");
		try
		{
			System.Windows.Forms.Screen[] allScreens = System.Windows.Forms.Screen.AllScreens;
			if (allScreens.Length == 0)
			{
				sb.AppendLine("  未枚举到任何屏（该项读取失败）。");
			}
			System.Windows.Forms.Screen[] array = allScreens;
			foreach (System.Windows.Forms.Screen screen in array)
			{
				int refreshRate = ScreenNative.GetRefreshRate(screen.DeviceName);
				int num = ScreenNative.MeasureMaxRefreshRate(screen.DeviceName);
				IReadOnlyList<int> availableRefreshRates = ScreenNative.GetAvailableRefreshRates(screen.DeviceName);
				sb.AppendLine($"  {screen.DeviceName} 主屏={(screen.Primary ? "是" : "否")} 分辨率={screen.Bounds.Width}x{screen.Bounds.Height} 当前刷新率={refreshRate}Hz 系统侧最大={num}Hz 可用档位=[{string.Join(",", availableRefreshRates)}]");
			}
			string text = ReportDpiScale();
			if (text.Length > 0)
			{
				sb.AppendLine("  缩放：" + text);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	private static string ReportDpiScale()
	{
		try
		{
			using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
			if (graphics == null)
			{
				return "";
			}
			return $"主屏 DPI={graphics.DpiX}x{graphics.DpiY}";
		}
		catch
		{
			return "";
		}
	}

	private static void AppendVideoControllers(StringBuilder sb)
	{
		sb.AppendLine("显示适配器（Win32_VideoController · CurrentRefreshRate/MaxRefreshRate）：");
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, CurrentRefreshRate, MaxRefreshRate, MinRefreshRate, CurrentHorizontalResolution, CurrentVerticalResolution, DriverVersion, VideoModeDescription, PNPDeviceID, Status FROM Win32_VideoController");
			int num = 0;
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				num++;
				string text = item["Name"]?.ToString()?.Trim() ?? "未知";
				sb.AppendLine($"  #{num} {text}");
				sb.AppendLine($"      CurrentRefreshRate={Fmt(item["CurrentRefreshRate"])} MaxRefreshRate={Fmt(item["MaxRefreshRate"])} MinRefreshRate={Fmt(item["MinRefreshRate"])}");
				sb.AppendLine($"      CurrentResolution={Fmt(item["CurrentHorizontalResolution"])}x{Fmt(item["CurrentVerticalResolution"])} VideoMode={Fmt(item["VideoModeDescription"])}");
				sb.AppendLine($"      Driver={Fmt(item["DriverVersion"])} Status={Fmt(item["Status"])} PNPDeviceID={Fmt(item["PNPDeviceID"])}");
			}
			if (num == 0)
			{
				sb.AppendLine("  未枚举到显示适配器（该项读取失败）。");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	private static void AppendMonitorEdid(StringBuilder sb)
	{
		sb.AppendLine("显示器 EDID 标识（root\\wmi · WmiMonitorID / WmiMonitorConnectionParams）：");
		try
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\wmi", "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams");
				foreach (ManagementBaseObject item in managementObjectSearcher.Get())
				{
					string key = item["InstanceName"]?.ToString() ?? "";
					dictionary[key] = Fmt(item["VideoOutputTechnology"]);
				}
			}
			catch (Exception ex)
			{
				sb.AppendLine("  WmiMonitorConnectionParams：该项读取失败（" + ex.Message + "）");
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			try
			{
				using ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("root\\wmi", "SELECT InstanceName, ManufacturerName, ProductCodeID, UserFriendlyName, SerialNumberID, YearOfManufacture, WeekOfManufacture FROM WmiMonitorID");
				foreach (ManagementBaseObject item2 in managementObjectSearcher2.Get())
				{
					num++;
					string text = item2["InstanceName"]?.ToString() ?? "";
					string text2 = DecodeUshortString(item2["UserFriendlyName"]);
					string text3 = DecodeUshortString(item2["ManufacturerName"]);
					string text4 = DecodeUshortString(item2["SerialNumberID"]);
					// 2026-10-09（75:00）：显示器 EDID 序列号是**真机 ICC 文件名**（`<PnP>_<序列号>.icm`）里那一处漏打码的来源，
					// 登记进共享敏感值表 ⇒ ① 打码时按它逐字兜底 ② 打码自检也会拿它去全文查零命中（两份口径同一份数据）。
					// 2026-10-09（批次3 #3）：面板号（`BOE0B40` 这类）在 Register 内被挡掉 —— 它是面板信息，不是序列号。
					DiagnosticGcuEvidenceInfo.SensitiveValues.Register(text4, "序列号（显示器 EDID SerialNumberID）");
					string text5 = Fmt(item2["ProductCodeID"]);
					string text6 = Fmt(item2["YearOfManufacture"]);
					string text7 = Fmt(item2["WeekOfManufacture"]);
					string text8 = (dictionary.TryGetValue(text, out string value) ? value : "(无)");
					bool? flag = IsInternalOutputTechnology(value);
					if (flag == true)
					{
						num2++;
					}
					else if (flag == false)
					{
						num3++;
					}
					// 2026-10-09（批次3 #3）：`Serial` 这一格是**本机 EDID 标识**，与包内其它行同一口径走 Mask ——
					// 面板号（BOE0B40 这类）按【面板信息】原样保留（不进敏感值表，见 PanelIdentifiers），
					// 真序列号仍被盖成 <序列号已打码>（否则这一行会与文末「隐私打码自检」互相矛盾）。
					sb.AppendLine($"  #{num} 友好名={(string.IsNullOrWhiteSpace(text2) ? "(空)" : text2)} 厂商={(string.IsNullOrWhiteSpace(text3) ? "(空)" : text3)} ProductCodeID={text5} Serial={((text4.Length > 0) ? DiagnosticGcuEvidenceInfo.Mask(text4) : "(空)")} 年份={text6} 周={text7}");
					sb.AppendLine($"      输出技术={text8}{((flag == true) ? " (内屏/eDP)" : "")} InstanceName={text}");
				}
			}
			catch (Exception ex2)
			{
				sb.AppendLine("  WmiMonitorID：该项读取失败（" + ex2.Message + "）");
			}
			sb.AppendLine($"  分类结论：EDID 条目={num}，其中判为内屏/eDP={num2}，判为外接/虚拟屏={num3}；共枚举到 {System.Windows.Forms.Screen.AllScreens.Length} 块活动 GDI 屏。");
		}
		catch (Exception ex3)
		{
			sb.AppendLine("  该项读取失败（" + ex3.Message + "）");
		}
	}

	/// <summary>WmiMonitorConnectionParams.VideoOutputTechnology 与 DISPLAYCONFIG 输出技术同枚举值。</summary>
	private static bool? IsInternalOutputTechnology(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw) || raw == "(无)")
		{
			return null;
		}
		if (!uint.TryParse(raw.Trim(), out uint result))
		{
			return null;
		}
		if (result == 2147483648u)
		{
			return true;
		}
		return result == 11;
	}

	private static void AppendGcuRefresh(StringBuilder sb)
	{
		sb.AppendLine("GCU 刷新率（Program.hw 当前值，对照系统侧实测）：");
		try
		{
			MechrevoLite.Hardware.MechrevoHw hw = Program.hw;
			if (hw == null)
			{
				sb.AppendLine("  Program.hw 尚未创建（该项读取失败）。");
				return;
			}
			// 2026-10-15（C-1 收尾）：本行的其余字段（HzList / CurrentHz / SupportsDisplayRefresh / DC_HZ）
			// 全部来自 GCU 上报，故「已连接」必须用 GCU 语义的 IsGcuConnected，而非 IsConnected
			// （= UsesLegacyMifs || IsGcuConnected）；否则老机型接口机型上会显示「已连接=是」而 HzList 为空，
			// 自相矛盾且误导诊断。
			sb.AppendLine($"  已连接={((hw.IsGcuConnected ? "是" : "否"))} HzList=[{string.Join(",", hw.HzList)}] CurrentHz={hw.CurrentHz} SupportsDisplayRefresh={(hw.SupportsDisplayRefresh ? "是" : "否")} DC_HZ={(hw.DcHzSeen ? hw.DcHz.ToString() : "?")}");
			sb.AppendLine($"  已落盘 config（写入侧 eDP 门槛）：screen_max={MechrevoLite.AppConfig.Get("screen_max")} max_frequency={MechrevoLite.AppConfig.Get("max_frequency")} frequency={MechrevoLite.AppConfig.Get("frequency")} internal_display=\"{MechrevoLite.AppConfig.GetString("internal_display")}\"");
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	private static string DecodeUshortString(object? value)
	{
		try
		{
			if (value is ushort[] array)
			{
				StringBuilder stringBuilder = new StringBuilder();
				foreach (ushort num in array)
				{
					if (num == 0)
					{
						break;
					}
					stringBuilder.Append((char)num);
				}
				return stringBuilder.ToString().Trim();
			}
			if (value is string text)
			{
				return text.Trim();
			}
		}
		catch
		{
		}
		return "";
	}

	private static string Fmt(object? value)
	{
		if (value == null)
		{
			return "(无)";
		}
		try
		{
			if (value is IFormattable formattable)
			{
				return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "(无)";
			}
			return value.ToString() ?? "(无)";
		}
		catch
		{
			return "(无)";
		}
	}
}
