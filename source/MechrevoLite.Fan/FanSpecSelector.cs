using System;
using Probe;

namespace MechrevoLite.Fan;

public static class FanSpecSelector
{
	public const int CapabilityAddress = 1934;

	public const int RamFan1p5CapabilityBit = 6;

	public static bool HasRamFan1p5(IEcReadTransport transport)
	{
		ArgumentNullException.ThrowIfNull(transport, "transport");
		try
		{
			int num = transport.ReadByte(1934);
			return num >= 0 && (num & 0x40) != 0;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("FanSpecSelector capability read failed: " + ex.Message);
			return false;
		}
	}

	public static FanSpecClass Select(bool ramFan1p5Capability)
	{
		if (!ramFan1p5Capability)
		{
			return FanSpecClass.RamFan1;
		}
		return FanSpecClass.RamFan1p5;
	}

	public static FanSpecClass SelectFor(IEcReadTransport transport)
	{
		return Select(HasRamFan1p5(transport));
	}

	public static bool IsReachable(FanSpecClass specClass)
	{
		return specClass != FanSpecClass.RamFan2;
	}

	public static FanSpecClass RequireReachable(FanSpecClass declared)
	{
		if (!IsReachable(declared))
		{
			throw new NotSupportedException($"{declared} has no implementation path: FanTable_Manager2 is never instantiated");
		}
		return declared;
	}
}
