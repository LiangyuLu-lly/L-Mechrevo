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
	ApplyGpuOverclock,
	// <summary>  显卡功耗上限（NVML SetPowerManagementLimit · 毫瓦换算在 NvmlHelper 内）</summary>
	ApplyGpuPowerLimit
}
