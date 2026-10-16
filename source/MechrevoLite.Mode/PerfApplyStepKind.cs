namespace MechrevoLite.Mode;

public enum PerfApplyStepKind
{
	SwitchBuiltIn,
	SwitchTurboSubMode,
	SwitchFirmwareSlot,
	WriteFirmwareField,
	WriteFanCurve,
	ApplyFanBoost,
	ApplyPowerPlan,
	ApplyCpuBoost,
	ApplyPowerOverlay,
	ApplyRefreshRate,
	ApplyGpuOverclock
}
