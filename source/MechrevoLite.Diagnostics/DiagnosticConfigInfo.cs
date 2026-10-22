using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;
using Microsoft.Win32;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-09（诊断增强）：诊断包新增「配置与注册表」一节。
/// 背景（逐条对应本会话踩过的坑 —— 这些信息以前都不在包里，每次都要让机主单独跑命令）：
/// ① 极光Pro 的 GCU 变体核对要靠 HKLM\SOFTWARE\L-Mechrevo 的 GcuVariant / GcuPayloadSha256 /
///    GcuServiceExe / GcuInstallerVersion；
/// ② 50 系键盘分流要看 GcuVariant=="50"、KB solution=、KeyboardType —— 包里都缺；
/// ③ "GPU 超频假失败"的根因是档位里已保存的 perf_custom*_ocon / occore / ocmem
///    （ocon=1 且两偏移皆 0 ⇒ 结构性假阴性）—— 本节把档位已保存值直接落进包；
/// ④ 服务/进程/13688 会话持有者原来只有 tools\诊断-GCU连不上.ps1 有，包里没有（用户更愿意只点一个按钮）；
/// ⑤ GCU 已上报/已解析的身份与能力（KeyboardType / solution / ProjectID / 能力位 / GCU 版本）不在包里。
/// ⚠️ 纯只读：只读内存态、注册表、配置快照、SCM 状态与本机 TCP 连接表；
///    **不写**任何配置/注册表/EC，**不新增**任何 MQTT GET 或订阅（第 ⑤ 节只读 Program.hw 里
///    **已经收到并解析**的字段），**不连** 13688（只列本机 TCP 表里已经存在的条目）。
///    每个子块独立 try/catch，任一子项失败只写"该项读取失败"，绝不让诊断包导出失败。
/// </summary>
internal static class DiagnosticConfigInfo
{
	private const int MqttPort = 13688;

	private const int MaxValueChars = 160;

	private const int MaxListItems = 60;

	private const int MaxPerfKeys = 120;

	/// <summary>与硬件/性能/灯效/校色/限充/启动/显示相关的键（perf_* 单独在下一节逐字列出）。</summary>
	private static readonly Regex InterestingKey = new Regex(
		"^(mode_tune_|performance|custom|direct_gpu_oc|gpu|lighting|light|lightbar|rgb|logo|aura|keyboard|hinge|sync|dynamic_lighting|calib|color|gamut|icc|dimming|overdrive|oled|miniled|charge|battery|startup|tray|screen|max_frequency|min_frequency|frequency|hz|refresh|internal_display|dgpu|ec|fan|tcc|pl1|pl2|pl4|tgp|smu|co_|curve|power|skip_powermode|model_override|log_level)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>疑似凭据字段：一律打码，绝不落进包。</summary>
	private static readonly Regex SecretishKey = new Regex(
		"(pwd|pass|secret|token|cred|auth|apikey|api_key|license|privatekey)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	public static string Build()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[配置与注册表]");
		try
		{
			stringBuilder.AppendLine("（只读采集：不写配置/注册表/EC、不发 MQTT 请求、不连 13688；单项失败只写「该项读取失败」）");
			AppendAppConfig(stringBuilder);
			AppendPerfStored(stringBuilder);
			AppendRegistry(stringBuilder);
			AppendGcuReported(stringBuilder);
			AppendRuntime(stringBuilder);
			stringBuilder.Append(XingyaoPmc2.Describe());
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine("配置与注册表采集整体失败（其余各节不受影响）：" + ex.Message);
		}
		stringBuilder.AppendLine();
		return stringBuilder.ToString();
	}

	// ================================================================ ① LM 关键配置

	private static void AppendAppConfig(StringBuilder sb)
	{
		sb.AppendLine("--- ① LM 关键配置（应用内存快照；完整文件见包内 配置\\config.json）---");
		try
		{
			IReadOnlyDictionary<string, object> snapshot = AppConfig.Snapshot();
			List<string> lines = new List<string>();
			int masked = 0;
			int perfKeys = 0;
			foreach (KeyValuePair<string, object> pair in snapshot.OrderBy((KeyValuePair<string, object> p) => p.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (pair.Key.StartsWith("perf_", StringComparison.OrdinalIgnoreCase))
				{
					perfKeys++;
					continue;
				}
				if (!InterestingKey.IsMatch(pair.Key))
				{
					continue;
				}
				if (SecretishKey.IsMatch(pair.Key))
				{
					lines.Add("  " + pair.Key + " = (已打码：疑似凭据字段)");
					masked++;
					continue;
				}
				lines.Add("  " + pair.Key + " = " + Truncate(FormatValue(pair.Value), MaxValueChars));
			}
			sb.AppendLine($"  配置键总数 {snapshot.Count}；下列 {lines.Count} 项为硬件/性能/灯效/校色/限充/启动/显示相关项：");
			foreach (string line in lines)
			{
				sb.AppendLine(line);
			}
			if (masked > 0)
			{
				sb.AppendLine($"  （其中 {masked} 项疑似凭据，已打码）");
			}
			if (perfKeys > 0)
			{
				sb.AppendLine($"  （另有 perf_* 键 {perfKeys} 个，已在下一节「档位已保存值」逐字列出，此处不重复）");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	// ================================================================ ② 档位已保存值

	private static void AppendPerfStored(StringBuilder sb)
	{
		sb.AppendLine("--- ② 档位已保存值（PerfModeStore 内存态 + config.json 原始持久化键；不猜键名）---");
		try
		{
			sb.AppendLine($"  [A] PerfModeStore 解析后的档位（null/- = 该键未持久化） active={SafeString(() => PerfModeStore.ActiveModeId)} HasStoredActive={YesNo(SafeBool(() => PerfModeStore.HasStoredActive))} lastCustom={SafeString(() => PerfModeStore.LastCustomId)} schema={AppConfig.Get("perf_schema", -1)} 自定义档序={string.Join(",", SafeCustomOrdinals())}");
			foreach (PerfModeDefinition definition in PerfModeStore.Load())
			{
				string name = string.IsNullOrWhiteSpace(definition.UserName) ? "" : " 名称=\"" + definition.UserName + "\"";
				sb.AppendLine($"    [{(definition.IsCustom ? "自定义" : "内置")}] {definition.Id}{name} route={definition.Route} 固件签名={Truncate(definition.Settings.FirmwareSignature(), 60)}");
				sb.AppendLine("      " + DescribeSettings(definition.Settings));
				if (definition.IsCustom)
				{
					sb.AppendLine($"      持久化键前缀 perf_{definition.Id}_（plan/pl1/pl2/pl4/tgp/dbon/db/fancpu/fangpu/tccon/tcc/ocon/occore/ocmem/fanboost/fsson/fss/power/boost/hz）");
				}
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  [A] 该项读取失败（" + ex.Message + "）");
		}
		try
		{
			sb.AppendLine("    固件槽位（perf_slot_owner_* / perf_slot_sig_* / perf_slot_used_*）：");
			foreach (FirmwareSlotState slot in PerfModeStore.LoadSlots(5))
			{
				sb.AppendLine($"      slot{slot.Index}: owner={(string.IsNullOrWhiteSpace(slot.OwnerModeId) ? "(空)" : slot.OwnerModeId)} sig={Truncate(slot.Signature ?? "", 40)} lastUsed={slot.LastUsedTicks}（-1=<未使用>）");
			}
		}
		catch (Exception ex2)
		{
			sb.AppendLine("    固件槽位：该项读取失败（" + ex2.Message + "）");
		}
		try
		{
			List<string> raw = new List<string>();
			int total = 0;
			foreach (KeyValuePair<string, object> pair in AppConfig.Snapshot().OrderBy((KeyValuePair<string, object> p) => p.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (!pair.Key.StartsWith("perf_", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				total++;
				if (raw.Count < MaxPerfKeys)
				{
					raw.Add("    " + pair.Key + " = " + Truncate(FormatValue(pair.Value), MaxValueChars));
				}
			}
			sb.AppendLine($"  [B] 原始持久化键（config.json 快照中的 perf_* 共 {total} 个，逐字列出）：");
			foreach (string line in raw)
			{
				sb.AppendLine(line);
			}
			if (total > raw.Count)
			{
				sb.AppendLine($"    …（还有 {total - raw.Count} 个未列出，已限量）");
			}
		}
		catch (Exception ex3)
		{
			sb.AppendLine("  [B] 该项读取失败（" + ex3.Message + "）");
		}
	}

	private static string DescribeSettings(PerfModeSettings s)
	{
		StringBuilder sb = new StringBuilder();
		Add("pl1", s.Pl1);
		Add("pl2", s.Pl2);
		Add("pl4", s.Pl4);
		Add("tgp", s.GpuTgp);
		AddBool("dbon", s.GpuDynamicBoostOn);
		Add("db", s.GpuDynamicBoost);
		AddBool("tccon", s.TccOn);
		Add("tcc", s.TccTarget);
		AddBool("ocon", s.GpuOverclockOn);
		Add("occore", s.GpuCoreOffset);
		Add("ocmem", s.GpuMemoryOffset);
		AddBool("fanboost", s.FanBoost);
		AddBool("fsson", s.FanSwitchSpeedOn);
		Add("fss", s.FanSwitchSpeedMs);
		Add("power", s.WindowsPowerMode);
		Add("boost", s.CpuBoost);
		Add("hz", s.RefreshHz);
		sb.Append("plan=").Append(string.IsNullOrWhiteSpace(s.PowerPlanGuid) ? "-" : s.PowerPlanGuid).Append(' ');
		sb.Append("fancpu=").Append(Curve(s.CpuFanDuty)).Append(' ');
		sb.Append("fangpu=").Append(Curve(s.GpuFanDuty)).Append(' ');
		if (s.GpuOverclockOn == true && (s.GpuCoreOffset ?? 0) == 0 && (s.GpuMemoryOffset ?? 0) == 0)
		{
			sb.Append("⚠️ ocon=1 且 occore=ocmem=0：已保存「开超频」但两偏移皆为 0（结构性假阴性的典型形态，GPU 超频会表现为假失败）");
		}
		return sb.ToString().TrimEnd();
		void Add(string key, int? value)
		{
			sb.Append(key).Append('=').Append(value?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(' ');
		}
		void AddBool(string key, bool? value)
		{
			sb.Append(key).Append('=').Append(value.HasValue ? (value.Value ? "1" : "0") : "-").Append(' ');
		}
		static string Curve(int[]? values)
		{
			if (values == null || values.Length == 0)
			{
				return "-";
			}
			return "[" + string.Join(",", values) + "]";
		}
	}

	// ================================================================ ③ 注册表

	private static void AppendRegistry(StringBuilder sb)
	{
		sb.AppendLine("--- ③ 注册表（只读；以本仓库代码实际读取的键为准，长值/长列表截断）---");
		DumpRegistryKey(sb, "HKLM\\SOFTWARE\\L-Mechrevo（GCU 变体/载荷/安装器登记）", "SOFTWARE\\L-Mechrevo", null, 80);
		string[] rgbKeyboardPaths = new string[3]
		{
			"SOFTWARE\\OEM\\GamingCenter2\\RGBKeyboard",
			"SOFTWARE\\OEM\\GamingCenter\\RGBKeyboard",
			"SOFTWARE\\OEM\\ControlCenter\\RGBKeyboard"
		};
		foreach (string path in rgbKeyboardPaths)
		{
			DumpRegistryKey(sb, "HKLM\\" + path + "（灯效 · MechrevoDeviceCapabilities.RgbKeyboardPaths）", path, null, MaxListItems);
		}
		string[] itemSupportPaths = new string[3]
		{
			"SOFTWARE\\OEM\\GamingCenter2\\ItemSupport",
			"SOFTWARE\\OEM\\GamingCenter\\ItemSupport",
			"SOFTWARE\\OEM\\ControlCenter\\ItemSupport"
		};
		foreach (string path2 in itemSupportPaths)
		{
			DumpRegistryKey(sb, "HKLM\\" + path2 + "（能力位 · MechrevoDeviceCapabilities.ItemSupportPaths）", path2, null, MaxListItems);
		}
		string[] serviceValueNames = new string[6] { "DisplayName", "ImagePath", "ObjectName", "Start", "Type", "DependOnService" };
		DumpRegistryKey(sb, "HKLM\\SYSTEM\\CurrentControlSet\\Services\\GCUBridge（GcuCoexistence.ReadServiceImagePath）", "SYSTEM\\CurrentControlSet\\Services\\GCUBridge", serviceValueNames, 20);
		DumpRegistryKey(sb, "HKLM\\SYSTEM\\CurrentControlSet\\Services\\GCUService", "SYSTEM\\CurrentControlSet\\Services\\GCUService", serviceValueNames, 20);
		string[] biosValueNames = new string[8] { "SystemManufacturer", "SystemProductName", "SystemFamily", "SystemSKU", "BaseBoardManufacturer", "BaseBoardProduct", "BIOSVendor", "BIOSVersion" };
		DumpRegistryKey(sb, "HKLM\\HARDWARE\\DESCRIPTION\\System\\BIOS（ReadMachineIdentity 读的 8 项）", "HARDWARE\\DESCRIPTION\\System\\BIOS", biosValueNames, 20);
		DumpRegistryKey(sb, "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{8714A8D1-0F08-4681-9DF6-A8C4607A58B4}（官方控制台卸载登记 · ScreenControl.cs）", "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{8714A8D1-0F08-4681-9DF6-A8C4607A58B4}", null, 30);
	}

	private static void DumpRegistryKey(StringBuilder sb, string displayPath, string subKey, string[]? onlyNames, int limit)
	{
		try
		{
			List<string> view64 = ReadRegistryView(subKey, RegistryView.Registry64, onlyNames, limit, out int total64, out string? error64);
			List<string> view32 = ReadRegistryView(subKey, RegistryView.Registry32, onlyNames, limit, out int total32, out string? error32);
			if (view64.Count == 0 && view32.Count == 0)
			{
				sb.AppendLine($"  {displayPath}：不存在或读不到值（64 位视图：{error64 ?? "无值"}；32 位视图：{error32 ?? "无值"}）");
				return;
			}
			if (view64.SequenceEqual(view32, StringComparer.Ordinal))
			{
				sb.AppendLine($"  {displayPath}（64 位与 32 位视图相同，共 {total64} 个值）：");
				foreach (string line in view64)
				{
					sb.AppendLine(line);
				}
				return;
			}
			if (view64.Count > 0)
			{
				sb.AppendLine($"  {displayPath}（64 位视图，共 {total64} 个值）：");
				foreach (string line2 in view64)
				{
					sb.AppendLine(line2);
				}
			}
			if (view32.Count > 0)
			{
				sb.AppendLine($"  {displayPath}（32 位视图 = WOW6432Node，共 {total32} 个值）：");
				foreach (string line3 in view32)
				{
					sb.AppendLine(line3);
				}
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine($"  {displayPath}：该项读取失败（{ex.Message}）");
		}
	}

	private static List<string> ReadRegistryView(string subKey, RegistryView view, string[]? onlyNames, int limit, out int total, out string? error)
	{
		List<string> lines = new List<string>();
		total = 0;
		error = null;
		try
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			using RegistryKey? key = baseKey.OpenSubKey(subKey);
			if (key == null)
			{
				error = "键不存在";
				return lines;
			}
			string[] names = key.GetValueNames();
			Array.Sort(names, StringComparer.OrdinalIgnoreCase);
			total = names.Length;
			int shown = 0;
			foreach (string name in names)
			{
				if (onlyNames != null && !onlyNames.Contains(name, StringComparer.OrdinalIgnoreCase))
				{
					continue;
				}
				if (shown >= limit)
				{
					break;
				}
				shown++;
				try
				{
					object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
					string kind = KindName(key.GetValueKind(name));
					string shownName = string.IsNullOrEmpty(name) ? "(默认值)" : name;
					lines.Add("    " + shownName + " (" + kind + ") = " + Truncate(FormatValue(value), MaxValueChars));
				}
				catch (Exception ex)
				{
					lines.Add("    " + name + " = 该项读取失败（" + ex.Message + "）");
				}
			}
			if (total > shown)
			{
				lines.Add($"    …（共 {total} 个值，已限量列前 {shown} 个）");
			}
		}
		catch (Exception ex2)
		{
			error = ex2.Message;
		}
		return lines;
	}

	private static string KindName(RegistryValueKind kind)
	{
		return kind switch
		{
			RegistryValueKind.String => "REG_SZ",
			RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
			RegistryValueKind.MultiString => "REG_MULTI_SZ",
			RegistryValueKind.DWord => "REG_DWORD",
			RegistryValueKind.QWord => "REG_QWORD",
			RegistryValueKind.Binary => "REG_BINARY",
			RegistryValueKind.None => "REG_NONE",
			_ => kind.ToString()
		};
	}

	// ================================================================ ④ GCU 已上报/已解析

	private static void AppendGcuReported(StringBuilder sb)
	{
		sb.AppendLine("--- ④ GCU 已上报/已解析的身份与能力（只读 Program.hw 内存态；不新增任何 MQTT GET/订阅）---");
		try
		{
			MechrevoHw? hw = Program.hw;
			if (hw == null)
			{
				sb.AppendLine("  Program.hw 尚未创建（应用还没完成 GCU 初始化）：该项读取失败。");
				return;
			}
			MechrevoDeviceCapabilities capabilities = hw.Capabilities;
			// 2026-10-15（C-1 收尾）：本节通篇是 GCU 上报内容（下面紧接着「GCU 上报的能力位」），
			// 故「连接」必须用 GCU 语义的 IsGcuConnected，而非 IsConnected（= UsesLegacyMifs || IsGcuConnected）。
			sb.AppendLine($"  连接={YesNo(hw.IsGcuConnected)} KeyboardStatusSeen={YesNo(hw.KeyboardStatusSeen)}");
			sb.AppendLine($"  键盘分流：solution=\"{hw.KeyboardSolution}\" KeyboardType={capabilities.KeyboardType} effect=\"{hw.KeyboardEffect}\" light={hw.KeyboardLight} brightness={hw.KeyboardBrightness} speed={hw.KeyboardSpeed} direction=\"{hw.KeyboardDirection}\" power={YesNo(hw.KeyboardPower)}");
			sb.AppendLine($"  机型码：ProjectId=\"{capabilities.ProjectId}\" BiosProjectId=\"{hw.BiosProjectId ?? "(未收到)"}\" Manufacturer=\"{capabilities.Manufacturer}\" Model=\"{capabilities.Model}\" SystemSKU=\"{capabilities.SystemSku}\"");
			sb.AppendLine($"  校色档位：GCU 实时上报 MqttColorCalibrationLevel={(hw.MqttColorCalibrationLevel.HasValue ? hw.MqttColorCalibrationLevel.Value.ToString(CultureInfo.InvariantCulture) : "(未收到)")} 注册表 ItemSupport ColorCalibrationLevel={capabilities.ColorCalibrationLevel}");
			sb.AppendLine("  GCU 上报的能力位（(未收到) = 该字段 GCU 尚未给出，不是 false）：");
			sb.AppendLine($"    OverclockMenuSupport={Tri(hw.OverclockMenuSupport)} WhisperModeSupport={Tri(hw.WhisperModeSupport)} LchwocSupportReported={Tri(hw.LchwocSupportReported)} DgpuDirectStatusSupport={Tri(hw.DgpuDirectStatusSupport)} IgpuOnlyStatusSupport={Tri(hw.IgpuOnlyStatusSupport)} IgpuSwitchBlocked={Tri(hw.IgpuSwitchBlocked)}");
			sb.AppendLine($"    LightbarLogoSupport={Tri(hw.LightbarLogoSupport)} LightbarBaseSupport={Tri(hw.LightbarBaseSupport)} LightbarNewLogoSupport={Tri(hw.LightbarNewLogoSupport)} LightbarMbLogoSupport={Tri(hw.LightbarMbLogoSupport)} LightbarHingeSupport={Tri(hw.LightbarHingeSupport)}");
			sb.AppendLine($"    LocalDimmingSupport={Tri(hw.LocalDimmingSupport)} LcdOverdriveSupport={Tri(hw.LcdOverdriveSupport)} AmdPlatformReported={Tri(hw.AmdPlatformReported)} LcActionSupported={YesNo(hw.LcActionSupported)}");
			sb.AppendLine($"  当前运行态：OperatingMode={hw.OperatingMode} GpuMode={hw.GpuMode} CustomProfileIndex={hw.CustomProfileIndex} ProfileName=\"{hw.ProfileName}\" LchwocEnable={YesNo(hw.LchwocEnable)} OcSwitch={YesNo(hw.OcSwitch)}");
			sb.AppendLine($"  档位实时值：Pl1={hw.Pl1} Pl2={hw.Pl2} Pl4={hw.Pl4} GpuTgp={hw.GpuTgp} TccOffset={hw.TccOffset} TccTarget={hw.TccTarget} GpuCoreClockOffset={hw.GpuCoreClockOffset} GpuMemClockOffset={hw.GpuMemClockOffset}");
			sb.AppendLine($"  限充/电池：BatteryProtection={hw.BatteryProtection}（-1=未上报）BatteryPercent={hw.BatteryPercent} IsAC={YesNo(hw.IsAC)}");
			sb.AppendLine("  说明：GCU 版本见上方「[液冷 / GCU]」一节（同一值，不重复打印）；HzList/CurrentHz 见「[显示配置]」一节。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
	}

	// ================================================================ ⑤ 运行环境快照

	private static void AppendRuntime(StringBuilder sb)
	{
		sb.AppendLine("--- ⑤ 运行环境快照（只读：SCM 状态查询 + 注册表 + 本机 TCP 连接表；不启停服务、不连 13688）---");
		string[] serviceNames = new string[2] { "GCUBridge", "GCUService" };
		foreach (string serviceName in serviceNames)
		{
			AppendService(sb, serviceName);
		}
		foreach (string processName in serviceNames)
		{
			AppendProcesses(sb, processName);
		}
		AppendPortPeers(sb);
	}

	private static void AppendService(StringBuilder sb, string serviceName)
	{
		string subKey = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
		try
		{
			string? imagePath = null;
			object? start = null;
			try
			{
				using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey);
				imagePath = key?.GetValue("ImagePath")?.ToString();
				start = key?.GetValue("Start");
			}
			catch (Exception ex)
			{
				imagePath = "该项读取失败（" + ex.Message + "）";
			}
			string status;
			try
			{
				QueryServiceState(serviceName, out uint state, out uint processId);
				status = StateName(state) + "(" + state + ") PID=" + ((processId == 0) ? "(无进程)" : processId.ToString(CultureInfo.InvariantCulture));
				if (processId != 0 && !IsProcessAlive((int)processId))
				{
					status += "（该 PID 已不存在：服务可能刚重启或进程名与映像名不同，见下一行进程枚举）";
				}
			}
			catch (Win32Exception wex) when (wex.NativeErrorCode == 1060)
			{
				status = "未安装（SCM 里没有该服务，错误 1060）";
			}
			catch (Win32Exception wex2) when (wex2.NativeErrorCode == 5)
			{
				status = "需要管理员（SCM 查询被拒绝，错误 5）";
			}
			catch (Exception ex2)
			{
				status = "该项读取失败（" + ex2.Message + "）";
			}
			string startText = (start == null) ? "(未读到)" : FormatValue(start) + "(" + StartTypeName(start) + ")";
			sb.AppendLine($"  {serviceName}：Status={status} StartType={startText} ImagePath={Truncate(imagePath ?? "(未读到)", 200)}");
		}
		catch (Exception ex3)
		{
			sb.AppendLine($"  {serviceName}：该项读取失败（{ex3.Message}）");
		}
	}

	private static void AppendProcesses(StringBuilder sb, string processName)
	{
		try
		{
			Process[] processes = Process.GetProcessesByName(processName);
			try
			{
				if (processes.Length == 0)
				{
					sb.AppendLine($"  {processName} 进程：无（当前未运行）");
					return;
				}
				foreach (Process process in processes)
				{
					sb.AppendLine($"  {processName} 进程 PID={process.Id} 启动时间={SafeProcessText(() => process.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} 路径={SafeProcessText(() => process.MainModule?.FileName ?? "(空)")} 文件版本={SafeProcessText(() => FileVersionOf(process.MainModule?.FileName))}");
				}
			}
			finally
			{
				foreach (Process process2 in processes)
				{
					process2.Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine($"  {processName} 进程：该项读取失败（{ex.Message}）");
		}
	}

	private static void AppendPortPeers(StringBuilder sb)
	{
		sb.AppendLine($"  13688 端口（只读本机 TCP 连接表，不发起任何连接）：");
		try
		{
			List<TcpRow> rows = ReadTcpRows();
			int listeners = 0;
			List<int> ownerPids = new List<int>();
			foreach (TcpRow row in rows.Where((TcpRow r) => r.LocalPort == MqttPort && r.State == 2u))
			{
				listeners++;
				ownerPids.Add(row.OwningPid);
				sb.AppendLine($"    监听 {row.LocalAddress}:13688 PID={row.OwningPid}");
			}
			if (listeners == 0)
			{
				sb.AppendLine("    无人监听 13688（当前没有任何进程 LISTEN 13688）。");
			}
			int established = 0;
			foreach (TcpRow row2 in rows.Where((TcpRow r) => r.State == 5u && (r.LocalPort == MqttPort || r.RemotePort == MqttPort)))
			{
				if (established >= 20)
				{
					sb.AppendLine("    …（ESTABLISHED 已限量，前 20 条）");
					break;
				}
				established++;
				sb.AppendLine($"    ESTABLISHED {row2.LocalAddress}:{row2.LocalPort} -> {row2.RemoteAddress}:{row2.RemotePort} PID={row2.OwningPid}");
			}
			if (established == 0)
			{
				sb.AppendLine("    无 ESTABLISHED 会话（若故障当时也如此，说明后端没接上或对端已退场）。");
			}
			sb.AppendLine($"    合计：LISTEN={listeners} ESTABLISHED={established}");
			foreach (int pid in ownerPids.Concat(rows.Where((TcpRow r) => r.State == 5u && (r.LocalPort == MqttPort || r.RemotePort == MqttPort)).Select((TcpRow r) => r.OwningPid)).Distinct().OrderBy((int p) => p))
			{
				sb.AppendLine($"    持有者 PID={pid} 进程={DescribeProcessName(pid)}");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("    该项读取失败（" + ex.Message + "）（简化版：只查本机 IPv4 TCP 表）");
		}
	}

	private static string DescribeProcessName(int pid)
	{
		try
		{
			using Process process = Process.GetProcessById(pid);
			return process.ProcessName + " 路径=" + SafeProcessText(() => process.MainModule?.FileName ?? "(空)") + " 启动=" + SafeProcessText(() => process.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		}
		catch (Exception ex)
		{
			return "(该项读取失败：" + ex.Message + ")";
		}
	}

	private static bool IsProcessAlive(int pid)
	{
		try
		{
			using Process process = Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static string SafeProcessText(Func<string> read)
	{
		try
		{
			string text = read();
			return string.IsNullOrWhiteSpace(text) ? "(空)" : text;
		}
		catch (Win32Exception ex)
		{
			return "(需要管理员权限才能读取：" + ex.NativeErrorCode + ")";
		}
		catch (Exception ex2)
		{
			return "(该项读取失败：" + ex2.Message + ")";
		}
	}

	private static string FileVersionOf(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
		{
			return "(无)";
		}
		FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
		return versionInfo.ProductVersion ?? versionInfo.FileVersion ?? "(无)";
	}

	// ================================================================ 原生查询（只读）

	private const int AfInet = 2;

	private const int TcpTableOwnerPidAll = 5;

	private const uint ScManagerConnect = 1u;

	private const uint ServiceQueryStatus = 4u;

	private const int ScStatusProcessInfo = 0;

	private readonly struct TcpRow
	{
		internal readonly uint State;

		internal readonly string LocalAddress;

		internal readonly int LocalPort;

		internal readonly string RemoteAddress;

		internal readonly int RemotePort;

		internal readonly int OwningPid;

		internal TcpRow(uint state, string localAddress, int localPort, string remoteAddress, int remotePort, int owningPid)
		{
			State = state;
			LocalAddress = localAddress;
			LocalPort = localPort;
			RemoteAddress = remoteAddress;
			RemotePort = remotePort;
			OwningPid = owningPid;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MibTcpRowOwnerPid
	{
		public uint State;

		public uint LocalAddr;

		public uint LocalPort;

		public uint RemoteAddr;

		public uint RemotePort;

		public uint OwningPid;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ServiceStatusProcess
	{
		public uint ServiceType;

		public uint CurrentState;

		public uint ControlsAccepted;

		public uint Win32ExitCode;

		public uint ServiceSpecificExitCode;

		public uint CheckPoint;

		public uint WaitHint;

		public uint ProcessId;

		public uint ServiceFlags;
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order, int addressFamily, int tableClass, int reserved);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, int bufferSize, out int bytesNeeded);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool CloseServiceHandle(IntPtr handle);

	private static List<TcpRow> ReadTcpRows()
	{
		List<TcpRow> rows = new List<TcpRow>();
		int size = 0;
		uint rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
		if (rc != 122 && rc != 0)
		{
			throw new Win32Exception((int)rc, "GetExtendedTcpTable(查询大小)");
		}
		if (size <= 4)
		{
			return rows;
		}
		IntPtr buffer = Marshal.AllocHGlobal(size);
		try
		{
			rc = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
			if (rc != 0)
			{
				throw new Win32Exception((int)rc, "GetExtendedTcpTable");
			}
			int count = Marshal.ReadInt32(buffer);
			int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
			IntPtr tableStart = IntPtr.Add(buffer, 4);
			for (int i = 0; i < count; i++)
			{
				MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(tableStart, i * rowSize));
				rows.Add(new TcpRow(row.State, FormatIpv4(row.LocalAddr), DecodePort(row.LocalPort), FormatIpv4(row.RemoteAddr), DecodePort(row.RemotePort), (int)row.OwningPid));
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
		return rows;
	}

	private static string FormatIpv4(uint address)
	{
		return $"{(address & 0xFF)}.{((address >> 8) & 0xFF)}.{((address >> 16) & 0xFF)}.{((address >> 24) & 0xFF)}";
	}

	private static int DecodePort(uint raw)
	{
		uint port = raw & 0xFFFFu;
		return (int)(((port & 0xFFu) << 8) | ((port >> 8) & 0xFFu));
	}

	private static void QueryServiceState(string serviceName, out uint state, out uint processId)
	{
		state = 0u;
		processId = 0u;
		IntPtr scm = OpenSCManager(null, null, ScManagerConnect);
		if (scm == IntPtr.Zero)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager");
		}
		try
		{
			IntPtr service = OpenService(scm, serviceName, ServiceQueryStatus);
			if (service == IntPtr.Zero)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenService " + serviceName);
			}
			try
			{
				int size = Marshal.SizeOf<ServiceStatusProcess>();
				IntPtr buffer = Marshal.AllocHGlobal(size);
				try
				{
					if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, size, out int _))
					{
						throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatusEx " + serviceName);
					}
					ServiceStatusProcess status = Marshal.PtrToStructure<ServiceStatusProcess>(buffer);
					state = status.CurrentState;
					processId = status.ProcessId;
				}
				finally
				{
					Marshal.FreeHGlobal(buffer);
				}
			}
			finally
			{
				CloseServiceHandle(service);
			}
		}
		finally
		{
			CloseServiceHandle(scm);
		}
	}

	// ================================================================ 公共小工具

	private static string StateName(uint state)
	{
		return state switch
		{
			1u => "Stopped",
			2u => "StartPending",
			3u => "StopPending",
			4u => "Running",
			5u => "ContinuePending",
			6u => "PausePending",
			7u => "Paused",
			_ => "未知状态"
		};
	}

	private static string StartTypeName(object? start)
	{
		if (start == null)
		{
			return "未读到";
		}
		int value;
		try
		{
			value = Convert.ToInt32(start, CultureInfo.InvariantCulture);
		}
		catch
		{
			return "未知";
		}
		return value switch
		{
			0 => "Boot",
			1 => "System",
			2 => "Automatic",
			3 => "Manual",
			4 => "Disabled",
			_ => "未知"
		};
	}

	private static string FormatValue(object? value)
	{
		if (value == null)
		{
			return "(无)";
		}
		try
		{
			switch (value)
			{
			case byte[] bytes:
			{
				int length = Math.Min(bytes.Length, 32);
				string text = Convert.ToHexString(bytes, 0, length);
				return "0x" + text + ((bytes.Length > length) ? $"...(共 {bytes.Length} 字节)" : "");
			}
			case string[] items:
				return "[" + string.Join(", ", items.Take(MaxListItems)) + ((items.Length > MaxListItems) ? $", ...(共 {items.Length} 项)" : "") + "]";
			case string text2:
				return text2;
			case bool flag:
				return flag ? "true" : "false";
			case IFormattable formattable:
				return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "(无)";
			default:
				return value.ToString() ?? "(无)";
			}
		}
		catch
		{
			return "(格式化失败)";
		}
	}

	private static string Truncate(string text, int max)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + $"…(已截断，原长 {text.Length})";
	}

	private static string YesNo(bool value)
	{
		return value ? "是" : "否";
	}

	private static string Tri(bool? value)
	{
		if (!value.HasValue)
		{
			return "(未收到)";
		}
		return value.Value ? "是" : "否";
	}

	private static string SafeString(Func<string?> read)
	{
		try
		{
			return read() ?? "(未设置)";
		}
		catch (Exception ex)
		{
			return "(该项读取失败：" + ex.Message + ")";
		}
	}

	private static bool SafeBool(Func<bool> read)
	{
		try
		{
			return read();
		}
		catch
		{
			return false;
		}
	}

	private static IReadOnlyList<int> SafeCustomOrdinals()
	{
		try
		{
			return PerfModeStore.LoadCustomOrdinals();
		}
		catch
		{
			return Array.Empty<int>();
		}
	}
}
