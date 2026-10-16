using System;

namespace MechrevoLite.Mode;

public sealed record PerfLiveSnapshot(int? Pl1 = null, int? Pl2 = null, int? Pl4 = null, bool? TccOn = null, int? TccTarget = null, int? GpuTgp = null, bool? GpuDynamicBoostOn = null, int? GpuDynamicBoost = null, bool? FanSwitchSpeedOn = null, int? FanSwitchSpeedMs = null, int[]? CpuFanDuty = null, int[]? GpuFanDuty = null, bool? GpuOverclockOn = null, int? GpuCoreOffset = null, int? GpuMemoryOffset = null)
{
	public PerfModeSettings FillNulls(PerfModeSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		return settings with
		{
			Pl1 = (settings.Pl1 ?? Pl1),
			Pl2 = (settings.Pl2 ?? Pl2),
			Pl4 = (settings.Pl4 ?? Pl4),
			TccOn = (settings.TccOn ?? TccOn),
			TccTarget = (settings.TccTarget ?? TccTarget),
			GpuTgp = (settings.GpuTgp ?? GpuTgp),
			GpuDynamicBoostOn = (settings.GpuDynamicBoostOn ?? GpuDynamicBoostOn),
			GpuDynamicBoost = (settings.GpuDynamicBoost ?? GpuDynamicBoost),
			FanSwitchSpeedOn = (settings.FanSwitchSpeedOn ?? FanSwitchSpeedOn),
			FanSwitchSpeedMs = (settings.FanSwitchSpeedMs ?? FanSwitchSpeedMs),
			CpuFanDuty = (settings.CpuFanDuty ?? CpuFanDuty),
			GpuFanDuty = (settings.GpuFanDuty ?? GpuFanDuty),
			GpuOverclockOn = (UserTouchedOverclock(settings) ? settings.GpuOverclockOn : GpuOverclockOn),
			GpuCoreOffset = (UserTouchedOverclock(settings) ? settings.GpuCoreOffset : GpuCoreOffset),
			GpuMemoryOffset = (UserTouchedOverclock(settings) ? settings.GpuMemoryOffset : GpuMemoryOffset)
		};
	}

	private static bool UserTouchedOverclock(PerfModeSettings s)
	{
		if (!s.GpuOverclockOn.HasValue && !s.GpuCoreOffset.HasValue)
		{
			return s.GpuMemoryOffset.HasValue;
		}
		return true;
	}
}
