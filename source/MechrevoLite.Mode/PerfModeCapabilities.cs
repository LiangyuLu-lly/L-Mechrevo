namespace MechrevoLite.Mode;

public sealed record PerfModeCapabilities
{
	public bool UsesAmdPowerFields { get; init; }

	public bool SupportsPl1 { get; init; } = true;

	public bool SupportsPl2 { get; init; } = true;

	public bool SupportsPl4 { get; init; } = true;

	public bool SupportsTcc { get; init; } = true;

	public bool SupportsTgp { get; init; } = true;

	public bool SupportsDynamicBoost { get; init; } = true;

	public bool SupportsGpuOverclock { get; init; } = true;

	public bool SupportsFanCurve { get; init; } = true;

	public bool SupportsFanBoost { get; init; } = true;

	public bool SupportsFanSwitchSpeed { get; init; } = true;

	public bool SupportsRefreshRate { get; init; } = true;

	public bool SupportsTurboSubMode { get; init; } = true;

	public static readonly PerfModeCapabilities All = new PerfModeCapabilities();
}
