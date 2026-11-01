using System;
using System.Globalization;

namespace MechrevoLite.Mode;

public static class ModeDetailEncoding
{
	public const int DefaultTjMax = 100;

	public static int TccOffset(int tjMax, int target)
	{
		return Math.Clamp(((tjMax > 0) ? tjMax : 100) - target, 0, 100);
	}

	public static int Pl4WireWatts(int watts, bool amdPlatform, int scale)
	{
		if (!amdPlatform && scale > 0)
		{
			return watts / scale;
		}
		return watts;
	}

	public static string Pl(int watts)
	{
		return watts.ToString(CultureInfo.InvariantCulture);
	}
}
