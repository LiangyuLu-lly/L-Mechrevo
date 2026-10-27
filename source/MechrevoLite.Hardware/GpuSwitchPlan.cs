namespace MechrevoLite.Hardware;

internal readonly record struct GpuSwitchPlan(GpuSwitchRoute Route, bool RequiresDgpuProcessPreflight);
