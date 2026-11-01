namespace MechrevoLite.Mode;

public static class PerfModeMapping
{
	public static int ToServiceMode(PerfModeKind kind)
	{
		return kind switch
		{
			PerfModeKind.Silent => 2, 
			PerfModeKind.Balanced => 0, 
			PerfModeKind.SilentTurbo => 1, 
			PerfModeKind.Turbo => 1, 
			PerfModeKind.Custom => 3, 
			_ => 0, 
		};
	}

	public static int ToFirmwareOperatingMode(PerfModeKind kind)
	{
		return kind switch
		{
			PerfModeKind.Silent => 0, 
			PerfModeKind.Balanced => 1, 
			PerfModeKind.SilentTurbo => 2, 
			PerfModeKind.Turbo => 2, 
			PerfModeKind.Custom => 3, 
			_ => 1, 
		};
	}

	public static PerfModeKind FromFirmware(int operatingMode, bool silentTurbo)
	{
		return operatingMode switch
		{
			0 => PerfModeKind.Silent, 
			1 => PerfModeKind.Balanced, 
			2 => silentTurbo ? PerfModeKind.SilentTurbo : PerfModeKind.Turbo, 
			3 => PerfModeKind.Custom, 
			_ => PerfModeKind.Balanced, 
		};
	}
}
