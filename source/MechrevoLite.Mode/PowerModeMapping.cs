using System;

namespace MechrevoLite.Mode;

public static class PowerModeMapping
{
	public static ConsoleOperatingMode FromVisualMode(int visualMode)
	{
		return visualMode switch
		{
			0 => ConsoleOperatingMode.Gaming, 
			1 => ConsoleOperatingMode.Turbo, 
			2 => ConsoleOperatingMode.Office, 
			3 => ConsoleOperatingMode.Customize, 
			_ => throw new ArgumentOutOfRangeException("visualMode", visualMode, "unknown visual mode"), 
		};
	}

	public static int ToVisualMode(ConsoleOperatingMode mode)
	{
		return mode switch
		{
			ConsoleOperatingMode.Office => 2, 
			ConsoleOperatingMode.Gaming => 0, 
			ConsoleOperatingMode.Turbo => 1, 
			ConsoleOperatingMode.Customize => 3, 
			_ => throw new ArgumentOutOfRangeException("mode", mode, "unknown console mode"), 
		};
	}

	public static bool TryToVendor(ConsoleOperatingMode mode, out VendorSysPowerMode vendor)
	{
		switch (mode)
		{
		case ConsoleOperatingMode.Office:
			vendor = VendorSysPowerMode.Balanced;
			return true;
		case ConsoleOperatingMode.Gaming:
			vendor = VendorSysPowerMode.Performance;
			return true;
		case ConsoleOperatingMode.Turbo:
			vendor = VendorSysPowerMode.Benchmark;
			return true;
		default:
			vendor = (VendorSysPowerMode)0;
			return false;
		}
	}
}
