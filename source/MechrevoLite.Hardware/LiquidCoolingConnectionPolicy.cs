namespace MechrevoLite.Hardware;

internal static class LiquidCoolingConnectionPolicy
{
	internal static LiquidCoolingControlRoute ResolveRoute(bool directBleConnected, bool gcuControllable, bool bluetoothObserved)
	{
		if (directBleConnected)
		{
			return LiquidCoolingControlRoute.DirectBle;
		}
		if (gcuControllable)
		{
			return LiquidCoolingControlRoute.Gcu;
		}
		if (!bluetoothObserved)
		{
			return LiquidCoolingControlRoute.None;
		}
		return LiquidCoolingControlRoute.BluetoothObserved;
	}

	internal static bool ShouldUseAutomaticDirectFallback(bool directBleConnected)
	{
		return !directBleConnected;
	}

	internal static bool ShouldSkipDirectFallbackBecauseGcuHoldsRadio(string? gcuConnectString)
	{
		switch (gcuConnectString)
		{
		case "Scanning":
		case "Connecting":
		case "IsConnectable":
			return true;
		default:
			return false;
		}
	}

	internal static bool ShouldRetryGcuConnection(bool hardwareConnected, bool serviceAvailable, LiquidCoolingControlRoute route, bool actionSupportReported, bool actionSupported, int attempts, long sinceLastAttemptMs)
	{
		bool flag = hardwareConnected & serviceAvailable;
		if (flag)
		{
			bool flag2 = (uint)route <= 1u;
			flag = flag2;
		}
		if (flag && (!actionSupportReported || actionSupported) && attempts < 5)
		{
			return sinceLastAttemptMs >= 8000;
		}
		return false;
	}

	internal static bool ShouldRestoreSavedGcuLighting(bool directBleConnected, bool gcuControllable, bool gcuStatusFresh, string? savedProfile)
	{
		if (!directBleConnected & gcuControllable & gcuStatusFresh)
		{
			return !string.IsNullOrWhiteSpace(savedProfile);
		}
		return false;
	}
}
