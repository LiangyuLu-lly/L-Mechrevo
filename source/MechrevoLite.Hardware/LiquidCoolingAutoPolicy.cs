using System;

namespace MechrevoLite.Hardware;

internal static class LiquidCoolingAutoPolicy
{
	public enum RefreshAction
	{
		None,
		EnableVendorAuto
	}

	internal static readonly TimeSpan TemperatureFreshness = TimeSpan.FromSeconds(30L);

	internal static bool IsVendorAutoActive(int reportedFanCtrl)
	{
		return reportedFanCtrl == 4;
	}

	internal static bool TemperatureUsable(int temperatureC)
	{
		return temperatureC > 0;
	}

	internal static int ResolveCoolingTemperature(int cpuTemp, int gpuTemp, bool fresh)
	{
		if (!fresh)
		{
			return 0;
		}
		return Math.Max(cpuTemp, gpuTemp);
	}

	internal static RefreshAction DecideGcuRefresh(bool pumpAuto, bool fanAuto, int reportedFanCtrl)
	{
		if (!pumpAuto && !fanAuto)
		{
			return RefreshAction.None;
		}
		if (!IsVendorAutoActive(reportedFanCtrl))
		{
			return RefreshAction.EnableVendorAuto;
		}
		return RefreshAction.None;
	}

	internal static bool ShouldTransmitVendorAuto(int selectedProfile)
	{
		return selectedProfile == -1;
	}
}
