namespace MechrevoLite.Gpu;

public static class GpuRowLayouts
{
	public static GpuRowLayout Resolve(bool nvPreference, bool modeSwitch, bool igpuMuxTarget)
	{
		if (nvPreference)
		{
			return GpuRowLayout.NvPreference;
		}
		if (!modeSwitch)
		{
			return GpuRowLayout.Hidden;
		}
		if (!igpuMuxTarget)
		{
			return GpuRowLayout.Mux2;
		}
		return GpuRowLayout.Mux3;
	}

	public static bool HasIgpuSegment(GpuRowLayout layout)
	{
		if ((uint)(layout - 3) <= 1u)
		{
			return true;
		}
		return false;
	}

	public static bool HasPrimarySegments(GpuRowLayout layout)
	{
		return layout != GpuRowLayout.Hidden;
	}

	public static bool UsesRouteReadback(GpuRowLayout layout)
	{
		if ((uint)(layout - 2) <= 2u)
		{
			return true;
		}
		return false;
	}
}
