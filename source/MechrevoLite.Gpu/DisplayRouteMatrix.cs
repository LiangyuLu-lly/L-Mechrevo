using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Gpu;

public static class DisplayRouteMatrix
{
	public const string ToggleOn = "DGPU_DIRECT_CONNECT_TOGGLE_ON";

	public const string ToggleOff = "DGPU_DIRECT_CONNECT_TOGGLE_OFF";

	public const string ToggleIgpu = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU";

	public const string Restart = "DGPU_DIRECT_CONNECT_RESTART";

	public const string IgpuOnlyOn = "IGPU_ONLY_CONNECT_RB_ON";

	public const string IgpuOnlyOff = "IGPU_ONLY_CONNECT_RB_OFF";

	public const string IgpuOnlyAuto = "IGPU_ONLY_CONNECT_RB_AUTO";

	public const string HotSwapOn = "GPU_HOTSWAP_ON";

	public const string HotSwapOff = "GPU_HOTSWAP_OFF";

	private static readonly IReadOnlySet<string> KnownActions = new HashSet<string>(StringComparer.Ordinal) { "DGPU_DIRECT_CONNECT_TOGGLE_ON", "DGPU_DIRECT_CONNECT_TOGGLE_OFF", "DGPU_DIRECT_CONNECT_TOGGLE_IGPU", "DGPU_DIRECT_CONNECT_RESTART", "IGPU_ONLY_CONNECT_RB_ON", "IGPU_ONLY_CONNECT_RB_OFF", "IGPU_ONLY_CONNECT_RB_AUTO", "GPU_HOTSWAP_ON", "GPU_HOTSWAP_OFF" };

	public static IReadOnlyList<GenerationRouteFacts> Rows { get; } = new GenerationRouteFacts[4]
	{
		new GenerationRouteFacts(DgpuGenerationKind.Gen30, new RouteCell(EvidenceMark.Inferred, "控制台侧（INFERRED——程序集为 .NET Native 无 IL，依据元数据标识符堆 + 完整 PDB 符号表 + 全载荷 0 命中）：MQTT `Setting/Control`；动作词汇仅 DGPU_DIRECT_CONNECT_TOGGLE_ON/_OFF；DgpuSwitchView.Toggle_PointerPressed(d__18) + SettingViewModel.DGpuDirectConnectionSwitch；DGPU_DIRECT_CONNECT_TOGGLE_IGPU 不在枚举内。载荷字段名为家族推断", ".omo/evidence/g30-console-decompile.md（cc-41747-13/decompiled/30-series-console-protocol.md）"), new RouteCell(EvidenceMark.Unknown, "NvramVariable.SetFwVars(\"OemDisplayMode\") 字符串在，但 30 系服务 MySettingManager 未反编译，写路径形态未知", "gpu-mode-matrix.md A (30 行, UNKNOWN#5)"), new RouteCell(EvidenceMark.ProvenAbsent, "30 系 .NET Native 元数据枚举堆里 DGPU_DIRECT_CONNECT_* 只有 _ON/_OFF；IGPU_ONLY_* / IGPUonly/* 全载荷 0 命中，无 PDB 符号", "g30-console-decompile.md / cc-41747-13/scans/absence.txt"), new RouteCell(EvidenceMark.ProvenAbsent, "30 系载荷 *_RESTART 0 命中，无 PDB 符号（唯一 restart 命中是 BCL FileSystemWatcher.Restart）", "g30-console-decompile.md / cc-41747-13/scans/absence.txt"), new RouteCell(EvidenceMark.ProvenAbsent, "30 系载荷 HOTSWAP 0 命中（大小写不敏感），无 PDB 符号", "g30-console-decompile.md / cc-41747-13/scans/absence.txt"), new string[2] { "DGPU_DIRECT_CONNECT_TOGGLE_ON", "DGPU_DIRECT_CONNECT_TOGGLE_OFF" }, Array.Empty<OemDisplayModeEncoding>()),
		new GenerationRouteFacts(DgpuGenerationKind.Gen40, new RouteCell(EvidenceMark.Proven, "控制台侧（PROVEN——逐方法反编译）：MQTT `Setting/Control`；动作词汇 DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF/IGPU、DGPU_DIRECT_CONNECT_RESTART、IGPU_ONLY_CONNECT_RB_ON/OFF（带双显三模档）、GETSTATUS", "MyControlCenter/Topic.cs:51,123-125 / MyControlCenter/MqttClientCtrl.cs:59-65,92-113 / MySettingManager.cs:1229-1271"), new RouteCell(EvidenceMark.Proven, "服务侧 SetFwVars(\"OemDisplayMode\")（NVRAM + WMI 0x30000000x iGPU-only）", "MySettingManager.cs:1229-1271 / WMIEC.cs:403-472 / GCUService.decompiled.cs:45504-45532"), new RouteCell(EvidenceMark.Proven, "IGPU_ONLY_CONNECT_RB_ON/OFF（WMI 0x300000001/0x300000000）", "WMIEC.cs:403-472 / gpu-mode-matrix.md A (40A)"), new RouteCell(EvidenceMark.Proven, "DGPU_DIRECT_CONNECT_RESTART → shutdown /r /t 0", "MySettingManager.cs:1292-1311 / gpu-mode-matrix.md A"), new RouteCell(EvidenceMark.Unknown, "40 系无热切换动作词汇（GPU_HOTSWAP_* 0 命中）", "gpu-mode-matrix.md A"), new string[6] { "DGPU_DIRECT_CONNECT_TOGGLE_ON", "DGPU_DIRECT_CONNECT_TOGGLE_OFF", "DGPU_DIRECT_CONNECT_TOGGLE_IGPU", "IGPU_ONLY_CONNECT_RB_ON", "IGPU_ONLY_CONNECT_RB_OFF", "DGPU_DIRECT_CONNECT_RESTART" }, new OemDisplayModeEncoding[2]
		{
			new OemDisplayModeEncoding("AMD", 1, 0, 2, "MySettingManager.cs:1229-1271"),
			new OemDisplayModeEncoding("Intel", 2, 4, 1, "MySettingManager.cs:1229-1271")
		})
		{
			ThreeMode = true
		},
		new GenerationRouteFacts(DgpuGenerationKind.Gen40, new RouteCell(EvidenceMark.Proven, "控制台侧（PROVEN——逐方法反编译）：MQTT `Setting/Control`；动作词汇 DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF、DGPU_DIRECT_CONNECT_RESTART、GETSTATUS；**无 IGPU_ONLY_***（不带双显三模档）", "ControlCenter_5.17.49.19 控制台反编译 / gpu-mode-matrix.md A (40B)"), new RouteCell(EvidenceMark.Proven, "服务侧 SetFwVars(\"OemDisplayMode\")（仅 NVRAM，无 WMI iGPU-only 路径）", "MySettingManager.cs:1229-1271 / GCUService.decompiled.cs:45504-45532"), new RouteCell(EvidenceMark.ProvenAbsent, "不带双显三模档：IGPU_ONLY_* 在该档控制台载荷中 0 命中", "ControlCenter_5.17.49.19 控制台反编译 / gpu-mode-matrix.md A (40B)"), new RouteCell(EvidenceMark.Proven, "DGPU_DIRECT_CONNECT_RESTART → shutdown /r /t 0", "MySettingManager.cs:1292-1311 / gpu-mode-matrix.md A"), new RouteCell(EvidenceMark.Unknown, "40 系无热切换动作词汇（GPU_HOTSWAP_* 0 命中）", "gpu-mode-matrix.md A"), new string[4] { "DGPU_DIRECT_CONNECT_TOGGLE_ON", "DGPU_DIRECT_CONNECT_TOGGLE_OFF", "DGPU_DIRECT_CONNECT_TOGGLE_IGPU", "DGPU_DIRECT_CONNECT_RESTART" }, new OemDisplayModeEncoding[2]
		{
			new OemDisplayModeEncoding("AMD", 1, 0, 2, "MySettingManager.cs:1229-1271"),
			new OemDisplayModeEncoding("Intel", 2, 4, 1, "MySettingManager.cs:1229-1271")
		})
		{
			ThreeMode = false
		},
		new GenerationRouteFacts(DgpuGenerationKind.Gen50, new RouteCell(EvidenceMark.Proven, "控制台侧（PROVEN——真实反编译的 C# 代码）：MQTT `Setting/Control`；动作词汇 DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF/IGPU、IGPU_ONLY_CONNECT_RB_ON/OFF/AUTO、DGPU_DIRECT_CONNECT_RESTART、GPU_HOTSWAP_ON/OFF", "CCUWinUI.decompiled.cs:86477-86515,53527-53729,139137-139174"), new RouteCell(EvidenceMark.Unknown, "50 系服务 IL 混淆，实际写路径（NVRAM/EC/DLL）不可静态确定", "gpu-mode-matrix.md A (50 行) / UNKNOWN#1"), new RouteCell(EvidenceMark.Proven, "IGPU_ONLY_CONNECT_RB_ON/OFF，每 2 s 重发、count>60 放弃、每第 4 次额外重发", "CCUWinUI.decompiled.cs:53527-53580"), new RouteCell(EvidenceMark.Proven, "DGPU_DIRECT_CONNECT_RESTART 在 Task.Delay(800) 后发出", "CCUWinUI.decompiled.cs:86511-86515"), new RouteCell(EvidenceMark.Inferred, "GPU_HOTSWAP_ON/OFF 枚举存在，但处理器是 log-only no-op，wire 用法未证", "CCUWinUI.decompiled.cs:53420-53435,139141-139142"), new string[9] { "DGPU_DIRECT_CONNECT_TOGGLE_ON", "DGPU_DIRECT_CONNECT_TOGGLE_OFF", "DGPU_DIRECT_CONNECT_TOGGLE_IGPU", "IGPU_ONLY_CONNECT_RB_ON", "IGPU_ONLY_CONNECT_RB_OFF", "IGPU_ONLY_CONNECT_RB_AUTO", "DGPU_DIRECT_CONNECT_RESTART", "GPU_HOTSWAP_ON", "GPU_HOTSWAP_OFF" }, Array.Empty<OemDisplayModeEncoding>())
	};

	public static GenerationRouteFacts? Find(DgpuGenerationKind generation)
	{
		return Rows.FirstOrDefault((GenerationRouteFacts row) => row.Generation == generation);
	}

	public static GenerationRouteFacts? FindTier(DgpuGenerationKind generation, bool threeMode)
	{
		return Rows.FirstOrDefault((GenerationRouteFacts row) => row.Generation == generation && (!row.ThreeMode.HasValue || row.ThreeMode == threeMode));
	}

	public static bool TierFromCapability(int igpuModeOnlySupport)
	{
		return igpuModeOnlySupport == 1;
	}

	public static bool TierFromCapability(int? mqttIgpuModeOnlySupport, bool persistedThreeModeCandidate)
	{
		if (mqttIgpuModeOnlySupport.HasValue)
		{
			int valueOrDefault = mqttIgpuModeOnlySupport.GetValueOrDefault();
			return valueOrDefault == 1;
		}
		return persistedThreeModeCandidate;
	}

	public static EvidenceMark ServiceWritePathMark(DgpuGenerationKind generation)
	{
		return Find(generation)?.ServiceWritePath.Mark ?? EvidenceMark.Unknown;
	}

	public static bool IsDisplayRouteWritePathProven(DgpuGenerationKind generation)
	{
		return ServiceWritePathMark(generation) == EvidenceMark.Proven;
	}

	public static bool AllowsIgpuOnly(DgpuGenerationKind generation)
	{
		IReadOnlyList<string> source = ConsoleActions(generation);
		if (!source.Contains("IGPU_ONLY_CONNECT_RB_ON", StringComparer.Ordinal) && !source.Contains("IGPU_ONLY_CONNECT_RB_OFF", StringComparer.Ordinal))
		{
			return source.Contains("IGPU_ONLY_CONNECT_RB_AUTO", StringComparer.Ordinal);
		}
		return true;
	}

	public static bool AllowsRestart(DgpuGenerationKind generation)
	{
		return ConsoleActions(generation).Contains("DGPU_DIRECT_CONNECT_RESTART", StringComparer.Ordinal);
	}

	public static bool AllowsHotSwap(DgpuGenerationKind generation)
	{
		IReadOnlyList<string> source = ConsoleActions(generation);
		if (!source.Contains("GPU_HOTSWAP_ON", StringComparer.Ordinal))
		{
			return source.Contains("GPU_HOTSWAP_OFF", StringComparer.Ordinal);
		}
		return true;
	}

	public static IReadOnlyList<string> ConsoleActions(DgpuGenerationKind generation)
	{
		return Find(generation)?.ConsoleActions ?? Array.Empty<string>();
	}

	public static bool IsKnownAction(string action)
	{
		if (!string.IsNullOrWhiteSpace(action))
		{
			return KnownActions.Contains(action);
		}
		return false;
	}
}
