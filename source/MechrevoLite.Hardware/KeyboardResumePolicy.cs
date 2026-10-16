namespace MechrevoLite.Hardware;

internal static class KeyboardResumePolicy
{
	internal static bool ShouldRestoreEffect(bool kbPowerOn)
	{
		return kbPowerOn;
	}

	internal static int EffectRestoreRequests(bool kbPowerOn)
	{
		return kbPowerOn ? 1 : 0;
	}
}
