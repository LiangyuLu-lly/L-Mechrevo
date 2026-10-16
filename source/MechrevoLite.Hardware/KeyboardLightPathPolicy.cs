namespace MechrevoLite.Hardware;

internal static class KeyboardLightPathPolicy
{
	internal static bool ShouldUseGcuKeyboardFallback(FeatureAvailability hid, bool hidConnected, bool serviceConnected, bool hidBrightnessTookEffect)
	{
		if (!serviceConnected)
		{
			return false;
		}
		if (hid == FeatureAvailability.Unsupported && !hidConnected)
		{
			return true;
		}
		if ((hid == FeatureAvailability.Supported) & hidConnected)
		{
			return !hidBrightnessTookEffect;
		}
		return false;
	}
}
