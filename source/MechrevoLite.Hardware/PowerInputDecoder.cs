namespace MechrevoLite.Hardware;

public static class PowerInputDecoder
{
	public const int ComplexPowerStatusAddress = 1996;

	public const int BiosInfo3Address = 1183;

	public const int RoundSocketMask = 1;

	public const int TypeCChargingMask = 6;

	public const int AdapterCodeMask = 120;

	public static PowerInputKind Decode(bool acOnline, int complexStatus, bool decodeEnabled)
	{
		if (!acOnline)
		{
			return PowerInputKind.Battery;
		}
		if (!decodeEnabled || complexStatus < 0)
		{
			return PowerInputKind.ExternalUnknown;
		}
		bool round = (complexStatus & 1) != 0;
		bool typeC = (complexStatus & 6) != 0;
		if (round)
		{
			if (!typeC)
			{
				return PowerInputKind.Barrel;
			}
			return PowerInputKind.BarrelAndTypeC;
		}
		if (typeC)
		{
			return PowerInputKind.TypeC;
		}
		return PowerInputKind.ExternalUnknown;
	}

	public static PowerInputKind Reconcile(PowerInputSample? sample, bool acOnlineNow)
	{
		if (!acOnlineNow)
		{
			return PowerInputKind.Battery;
		}
		PowerInputKind? kind = sample?.Kind;
		if (kind.HasValue)
		{
			PowerInputKind value = kind.GetValueOrDefault();
			if ((uint)value > 1u)
			{
				return value;
			}
		}
		return PowerInputKind.ExternalUnknown;
	}

	public static bool TryDecodeAdapterWatts(int raw, out int watts)
	{
		watts = 0;
		if (raw < 0)
		{
			return false;
		}
		watts = (raw & 0x78) switch
		{
			0 => 330,
			8 => 230,
			16 => 180,
			24 => 150,
			32 => 120,
			40 => 90,
			48 => 65,
			56 => 40,
			64 => 280,
			88 => 420,
			_ => 0,
		};
		return watts > 0;
	}

	public static string DisplayAdapterWatts(int watts)
	{
		if (watts == 230)
		{
			return "230/240";
		}
		return watts.ToString();
	}
}
