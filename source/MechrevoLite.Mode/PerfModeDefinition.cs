using System;
using System.Globalization;

namespace MechrevoLite.Mode;

public sealed record PerfModeDefinition(string Id, PerfModeKind Kind, string? UserName, PerfModeSettings Settings)
{
	public bool IsCustom => Kind == PerfModeKind.Custom;

	public PerfModeRoute Route
	{
		get
		{
			if (!IsCustom && !Settings.NeedsFirmwareSlot)
			{
				return PerfModeRoute.BuiltIn;
			}
			return PerfModeRoute.FirmwareSlot;
		}
	}

	public bool IsEmulatedBuiltIn
	{
		get
		{
			if (!IsCustom)
			{
				return Settings.NeedsFirmwareSlot;
			}
			return false;
		}
	}

	public static string BuiltInId(PerfModeKind kind)
	{
		return kind switch
		{
			PerfModeKind.Silent => "silent", 
			PerfModeKind.Balanced => "balanced", 
			PerfModeKind.SilentTurbo => "silentturbo", 
			PerfModeKind.Turbo => "turbo", 
			_ => throw new ArgumentOutOfRangeException("kind", kind, "not a built-in mode kind"), 
		};
	}

	public static string CustomId(int ordinal)
	{
		return "custom" + ordinal.ToString(CultureInfo.InvariantCulture);
	}

	public PerfModeDefinition With(PerfModeSettings settings)
	{
		return this with
		{
			Settings = settings
		};
	}
}
