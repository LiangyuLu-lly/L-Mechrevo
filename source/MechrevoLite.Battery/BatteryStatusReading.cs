namespace MechrevoLite.Battery;

internal sealed record BatteryStatusReading(uint PowerState, int? RateMilliwatts, uint CapacityMilliwattHours, uint VoltageMillivolts)
{
	internal bool IsOnLine => (PowerState & 1) != 0;

	internal bool IsCharging
	{
		get
		{
			if ((PowerState & 4) == 0)
			{
				int? rateMilliwatts = RateMilliwatts;
				if (rateMilliwatts.HasValue)
				{
					return rateMilliwatts.GetValueOrDefault() > 0;
				}
				return false;
			}
			return true;
		}
	}

	internal bool IsDischarging
	{
		get
		{
			if ((PowerState & 2) == 0)
			{
				int? rateMilliwatts = RateMilliwatts;
				if (rateMilliwatts.HasValue)
				{
					return rateMilliwatts.GetValueOrDefault() < 0;
				}
				return false;
			}
			return true;
		}
	}

	internal const uint PowerOnLine = 1u;

	internal const uint Discharging = 2u;

	internal const uint Charging = 4u;

	internal const uint Critical = 8u;
}
