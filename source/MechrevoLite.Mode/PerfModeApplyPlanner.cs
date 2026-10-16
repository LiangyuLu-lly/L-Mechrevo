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
		if (perfModeRoute == PerfModeRoute.FirmwareSlot)
		{
			if ((object)slotPlan == null)
			{
				throw new ArgumentNullException("slotPlan", "走固件自定义档的模式必须先做档位决策");
			}
			if (includeSwitch)
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
		if (perfModeRoute == PerfModeRoute.FirmwareSlot && (object)slotPlan != null && slotPlan.NeedsParameterWrite)
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
			if (settings.Pl1.HasValue)
			{
				list.Add(Field("PL1", settings.Pl1.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.Pl2.HasValue)
			{
				list.Add(Field("PL2", settings.Pl2.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.Pl4.HasValue)
			{
				list.Add(Field("PL4", settings.Pl4.Value, PerfApplyVerify.CpuPackagePower));
			}
			if (settings.TccTarget.HasValue)
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
