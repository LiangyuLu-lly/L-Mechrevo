using System;
using System.Collections.Generic;
using System.Globalization;

namespace MechrevoLite.Mode;

public static class PerfModeApplyPlanner
{
	public const int FieldDelayMs = 120;

	internal const string BalancedPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

	public static IReadOnlyList<PerfApplyStep> Build(PerfModeDefinition mode, FirmwareSlotPlan? slotPlan, PerfModeCapabilities caps, bool includeSwitch = true, PerfModeRoute? routeOverride = null)
	{
		ArgumentNullException.ThrowIfNull(mode, "mode");
		ArgumentNullException.ThrowIfNull(caps, "caps");
		List<PerfApplyStep> list = new List<PerfApplyStep>(16);
		PerfModeSettings settings = mode.Settings;
		PerfModeRoute perfModeRoute = routeOverride ?? mode.Route;
		// ⭐⭐ 2026-10-16（宝龙达 · 自定义档诚实性）：⭐ 本后端【写不了固件自定义档】时，不生成那两步 ✅
		//   ⭐ 依据见 PerfModeCapabilities.SupportsFirmwareSlot 的长注释 ——
		//      档位步 `SwitchFirmwareSlot` 会经 `MechrevoService.SwitchCustomProfile*` 的两道 `!IsGcuConnected` 闸
		//      而【恒失败】❌；⭐ 而 Runner 一见档位步失败就 `break` ⇒ ⭐ 把后面【所有】步骤（含本该能用的
		//      电源计划/覆盖层/睿频/刷新率）一起废掉 ❌。
		//   ⇒ ⭐ 所以宝龙达下退化为「只做后端无关的那些步骤」＝ 配置保存在本机（⭐ `perf_custom*_` 键本来就存得下 ✅）。
		//   ⚠️ 非 FirmwareSlot 路由 ⇒ 恒 true ⇒ ⭐ 内置档路径【逐位不变】✅；
		//   ⚠️ `caps.SupportsFirmwareSlot` 默认 true ⇒ ⭐ 所有既有后端（GCU/老接口）【逐位不变】✅。
		bool canWriteFirmwareSlot = perfModeRoute != PerfModeRoute.FirmwareSlot || caps.SupportsFirmwareSlot;
		if (perfModeRoute == PerfModeRoute.FirmwareSlot)
		{
			if ((object)slotPlan == null)
			{
				throw new ArgumentNullException("slotPlan", "走固件自定义档的模式必须先做档位决策");
			}
			if (canWriteFirmwareSlot && includeSwitch)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.SwitchFirmwareSlot, null, null, slotPlan.SlotIndex, IsCpuCurve: false, null, PerfApplyVerify.ModeReadback));
			}
		}
		else if (includeSwitch)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.SwitchBuiltIn, null, null, (int)mode.Kind, IsCpuCurve: false, null, PerfApplyVerify.ModeReadback));
			bool flag = caps.SupportsTurboSubMode;
			if (flag)
			{
				PerfModeKind kind = mode.Kind;
				bool flag2 = (uint)(kind - 2) <= 1u;
				flag = flag2;
			}
			if (flag)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.SwitchTurboSubMode, null, null, (mode.Kind == PerfModeKind.SilentTurbo) ? 1 : 0));
			}
		}
		if (canWriteFirmwareSlot && perfModeRoute == PerfModeRoute.FirmwareSlot && (object)slotPlan != null && slotPlan.NeedsParameterWrite)
		{
			if (settings.TccOn.HasValue)
			{
				list.Add(Field("CpuTccOffsetSwitch", settings.TccOn.Value ? 1 : 0));
			}
			if (settings.GpuDynamicBoostOn.HasValue)
			{
				list.Add(Field("GpuDynamicBoostSwitch", settings.GpuDynamicBoostOn.Value ? 1 : 0));
			}
			if (settings.GpuOverclockOn.HasValue)
			{
				list.Add(Field("OverClockingSwitch", settings.GpuOverclockOn.Value ? 1 : 0));
			}
			if (settings.FanSwitchSpeedOn.HasValue)
			{
				list.Add(Field("FanSwitchSpeedEnabled", settings.FanSwitchSpeedOn.Value ? 1 : 0));
			}
			// ⭐⭐ 2026-10-16（宝龙达 · 功耗墙真下发）：⭐ 下面这三项（PL1 / PL2 / CpuTccOffset）
			//   ⭐ 在"能用 WMI 写功耗墙"的后端（宝龙达 ✅）下【改到本方法末尾单独生成】✅
			//   ⭐ 原因：⭐ 它们在宝龙达上**不走固件槽**、而是走 WMI 功能号 23 ✅
			//      ⇒ ⭐ 所以这里加反向条件、避免与那一支重复生成 ✅
			//   ⚠️ `SupportsWmiPowerFields` 默认 false ⇒ ⭐ 所有既有后端【逐位不变】✅
			if (!caps.SupportsWmiPowerFields && settings.Pl1.HasValue)
			{
				list.Add(Field("PL1", settings.Pl1.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (!caps.SupportsWmiPowerFields && settings.Pl2.HasValue)
			{
				list.Add(Field("PL2", settings.Pl2.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.Pl4.HasValue)
			{
				list.Add(Field("PL4", settings.Pl4.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (!caps.SupportsWmiPowerFields && settings.TccTarget.HasValue)
			{
				list.Add(Field("CpuTccOffset", settings.TccTarget.Value));
			}
			if (settings.GpuTgp.HasValue)
			{
				list.Add(Field("GpuConfigurableTGPTarget", settings.GpuTgp.Value, PerfApplyVerify.GpuPowerLimit));
			}
			if (settings.GpuDynamicBoost.HasValue)
			{
				list.Add(Field("GpuDynamicBoost", settings.GpuDynamicBoost.Value, PerfApplyVerify.GpuPowerLimit));
			}
			if (settings.GpuCoreOffset.HasValue)
			{
				list.Add(Field("GpuCoreClockOffsetOC", settings.GpuCoreOffset.Value, PerfApplyVerify.GpuClockOffset));
			}
			if (settings.GpuMemoryOffset.HasValue)
			{
				list.Add(Field("GpuMemoryClockOffsetOC", settings.GpuMemoryOffset.Value, PerfApplyVerify.GpuClockOffset));
			}
			if (settings.FanSwitchSpeedMs.HasValue)
			{
				list.Add(Field("FanSwitchSpeed", settings.FanSwitchSpeedMs.Value));
			}
			int[] cpuFanDuty = settings.CpuFanDuty;
			if (cpuFanDuty != null && cpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: true, settings.CpuFanDuty, PerfApplyVerify.FanDuty));
			}
			int[] gpuFanDuty = settings.GpuFanDuty;
			if (gpuFanDuty != null && gpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: false, settings.GpuFanDuty, PerfApplyVerify.FanDuty));
			}
		}
		else if (perfModeRoute == PerfModeRoute.BuiltIn)
		{
			if (caps.SupportsTcc && settings.TccOn.HasValue)
			{
				list.Add(Field("CpuTccOffsetSwitch", settings.TccOn.Value ? 1 : 0));
			}
			if (caps.SupportsFanSwitchSpeed && settings.FanSwitchSpeedOn.HasValue)
			{
				list.Add(Field("FanSwitchSpeedEnabled", settings.FanSwitchSpeedOn.Value ? 1 : 0));
			}
			if (caps.SupportsTcc && settings.TccTarget.HasValue)
			{
				list.Add(Field("CpuTccOffset", settings.TccTarget.Value));
			}
			if (caps.SupportsFanSwitchSpeed && settings.FanSwitchSpeedMs.HasValue)
			{
				list.Add(Field("FanSwitchSpeed", settings.FanSwitchSpeedMs.Value));
			}
			if (caps.SupportsGpuOverclock && (settings.GpuOverclockOn.HasValue || settings.GpuCoreOffset.HasValue || settings.GpuMemoryOffset.HasValue))
			{
				bool flag3 = settings.GpuOverclockOn ?? true;
				list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyGpuOverclock, null, null, flag3 ? 1 : 0, IsCpuCurve: false, null, PerfApplyVerify.GpuClockOffset, flag3 ? (settings.GpuCoreOffset.GetValueOrDefault(), settings.GpuMemoryOffset.GetValueOrDefault()) : (0, 0)));
			}
		}
		// ⭐⭐ 2026-10-16（宝龙达 · 自定义档 · 功耗墙真下发）：⭐ 把 CPU 功耗墙三项【单独生成】✅
		//   ⭐ 为什么单独：⭐ 它们与"固件槽"无关 —— 后端在 `MechrevoService.SetCustomDetail` 开头
		//      就把这几个键就地摘出来、改写 WMI 功能号 23 ✅
		//      （⭐ 见 `PerfModeCapabilities.SupportsWmiPowerFields` 的注释 ⚠️）
		//   ⭐ 为什么仍要求 `NeedsParameterWrite`：⭐ 那是"⭐ 本次应用确实要改参数"⭐ 的语义 ⚠️
		//      ⇒ ⭐ 只读 / 守卫场景（⭐ `NeedsParameterWrite = false` ✅）**不会**真去写功耗墙 ✅
		//   ⭐ 为什么放这里（if/else 链之外）：⭐ 宝龙达下 `canWriteFirmwareSlot = false` ⇒
		//      上面那个 FirmwareSlot 大块整块跳过（⭐ 连同那些**只走 MQTT**的字段一起 ✅）
		//      ⇒ ⭐ 而功耗墙这三项**另有 WMI 通路**、不该被一起关掉 ✅ —— 这就是本次修的东西 ✅
		//   ⚠️ `SupportsWmiPowerFields` 默认 false ⇒ ⭐ 所有既有后端【逐位不变】✅
		if (caps.SupportsWmiPowerFields && perfModeRoute == PerfModeRoute.FirmwareSlot && slotPlan != null && slotPlan.NeedsParameterWrite)
		{
			if (settings.Pl1.HasValue)
			{
				list.Add(Field("PL1", settings.Pl1.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.Pl2.HasValue)
			{
				list.Add(Field("PL2", settings.Pl2.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.TccTarget.HasValue)
			{
				list.Add(Field("CpuTccOffset", settings.TccTarget.Value));
			}
		}
		// ⭐⭐ 2026-10-17（宝龙达 · 自定义档风扇曲线本地化）：⭐ 无固件槽的后端**不产生** GCU 的
		//   `WriteFanCurve`（上面那个大块整块跳过），但风扇曲线**能**走本机软件闭环（功能号 20/21）✅
		//   ⇒ ⭐ 这里单独生成步骤；执行侧（`PerfModeBackend.WriteFanCurveAsync`）在宝龙达下改为
		//      把"占空比 %"换算成 RPM、存进 `BaolongdaFanCurveStore` 并启用 ✅
		//   ⚠️ 仅 `SupportsWmiPowerFields`（=宝龙达）且确实要改参数时生成 ⇒ GCU/老接口逐位不变 ✅
		if (caps.SupportsWmiPowerFields && perfModeRoute == PerfModeRoute.FirmwareSlot && slotPlan != null && slotPlan.NeedsParameterWrite)
		{
			if (settings.CpuFanDuty != null && settings.CpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: true, settings.CpuFanDuty, PerfApplyVerify.FanDuty));
			}
			if (settings.GpuFanDuty != null && settings.GpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: false, settings.GpuFanDuty, PerfApplyVerify.FanDuty));
			}
		}
		if (settings.FanBoost.HasValue)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyFanBoost, null, null, settings.FanBoost.Value ? 1 : 0));
		}
		bool flag4 = !string.IsNullOrEmpty(settings.PowerPlanGuid);
		if (flag4)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyPowerPlan, null, settings.PowerPlanGuid, 0, IsCpuCurve: false, null, PerfApplyVerify.PowerPlan));
		}
		if (settings.WindowsPowerMode.HasValue && (!flag4 || IsBalancedPlan(settings.PowerPlanGuid)))
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyPowerOverlay, null, null, settings.WindowsPowerMode.Value, IsCpuCurve: false, null, PerfApplyVerify.PowerOverlay));
		}
		if (settings.CpuBoost.HasValue)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyCpuBoost, null, null, settings.CpuBoost.Value, IsCpuCurve: false, null, PerfApplyVerify.CpuBoost));
		}
		int? refreshHz = settings.RefreshHz;
		if (refreshHz.HasValue && refreshHz.GetValueOrDefault() > 0)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyRefreshRate, null, null, settings.RefreshHz.Value, IsCpuCurve: false, null, PerfApplyVerify.RefreshRate));
		}
		return list;
	}

	internal static bool IsBalancedPlan(string? guid)
	{
		if (Guid.TryParse(guid, out var result))
		{
			return result == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
		}
		return false;
	}

	private static PerfApplyStep Field(string wireKey, int value, PerfApplyVerify verify = PerfApplyVerify.ServiceEcho)
	{
		return new PerfApplyStep(PerfApplyStepKind.WriteFirmwareField, wireKey, value.ToString(CultureInfo.InvariantCulture), value, IsCpuCurve: false, null, verify);
	}

	internal static bool IsGateField(string? wireKey)
	{
		switch (wireKey)
		{
		case "CpuTccOffsetSwitch":
		case "GpuDynamicBoostSwitch":
		case "OverClockingSwitch":
		case "FanSwitchSpeedEnabled":
			return true;
		default:
			return false;
		}
	}
}
