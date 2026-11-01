using MechrevoLite.Properties;

namespace MechrevoLite.Hardware;

internal static class LiquidCoolingDisplayPolicy
{
	internal const int GcuFanAutoIndex = 4;

	internal static readonly string[] PumpGearLabels = new string[3] { "45%", "60%", "90%" };

	internal static readonly string[] FanGearLabels = new string[4] { "40%", "50%", "60%", "90%" };

	internal static string GearLabel(string percentLabel, int profile, int topProfile)
	{
		if (profile != topProfile)
		{
			return percentLabel;
		}
		return Strings.MaxGear;
	}

	internal static string? GearSummaryLabel(string channelPrefix, string[] gearLabels, int profile, int topProfile)
	{
		if (profile == -1)
		{
			return channelPrefix + Strings.Auto;
		}
		if (profile < 0 || profile >= gearLabels.Length)
		{
			return null;
		}
		return channelPrefix + GearLabel(gearLabels[profile], profile, topProfile);
	}

	internal static int FanProfile(int intentProfile, int reportedFanCtrl)
	{
		if (intentProfile != -1 && reportedFanCtrl != 4)
		{
			return reportedFanCtrl;
		}
		return -1;
	}

	internal static int PumpProfile(int intentProfile, int reportedPumpCtrl)
	{
		if (intentProfile != -1)
		{
			return reportedPumpCtrl;
		}
		return -1;
	}
}
