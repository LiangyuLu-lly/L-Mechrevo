using System;
using System.Linq;

namespace MechrevoLite.Gpu;

public static class DisplayRoutePolicy
{
	public static bool AllowsAction(DgpuGenerationKind generation, string action)
	{
		if (string.IsNullOrWhiteSpace(action))
		{
			return false;
		}
		if ((uint)generation <= 1u)
		{
			return DisplayRouteMatrix.IsKnownAction(action);
		}
		return DisplayRouteMatrix.ConsoleActions(generation).Contains(action, StringComparer.Ordinal);
	}

	public static bool AllowsAction(DgpuGenerationKind generation, string action, bool threeMode)
	{
		if (string.IsNullOrWhiteSpace(action))
		{
			return false;
		}
		if ((uint)generation <= 1u)
		{
			return DisplayRouteMatrix.IsKnownAction(action);
		}
		return (DisplayRouteMatrix.FindTier(generation, threeMode)?.ConsoleActions ?? Array.Empty<string>()).Contains(action, StringComparer.Ordinal);
	}
}
