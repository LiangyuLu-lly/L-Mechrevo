using System;
using Probe;

namespace MechrevoLite.Mode;

public static class PlDefaults
{
	public static readonly int[] GamingAddresses = new int[4] { 1840, 1841, 1842, 1843 };

	public static readonly int[] OfficeAddresses = new int[4] { 1844, 1845, 1846, 1847 };

	public static readonly int[] TurboAddresses = new int[4] { 1959, 1960, 1961, 1962 };

	public static readonly int[] TccAddresses = new int[3] { 2008, 2009, 2010 };

	public static int[] AddressesFor(PlDefaultMode mode)
	{
		return mode switch
		{
			PlDefaultMode.Gaming => GamingAddresses, 
			PlDefaultMode.Office => OfficeAddresses, 
			PlDefaultMode.Turbo => TurboAddresses, 
			_ => throw new ArgumentOutOfRangeException("mode", mode, "unknown PL default mode"), 
		};
	}

	public static int TccAddressFor(PlDefaultMode mode)
	{
		return TccAddresses[(int)mode];
	}

	public static PlDefaultMode? ModeForVisualMode(int visualMode)
	{
		return visualMode switch
		{
			0 => (PlDefaultMode?)PlDefaultMode.Gaming, 
			2 => PlDefaultMode.Office, 
			1 => PlDefaultMode.Turbo, 
			_ => null, 
		};
	}

	public static PlDefaultsResult Read(PlDefaultMode mode, Func<int, int> readByte)
	{
		ArgumentNullException.ThrowIfNull(readByte, "readByte");
		int[] array = AddressesFor(mode);
		int[] array2 = new int[array.Length];
		try
		{
			for (int i = 0; i < array.Length; i++)
			{
				int num = readByte(array[i]);
				if (num < 0)
				{
					return PlDefaultsResult.Unavailable(mode, $"EC {array[i]} unreadable; disabling PL editing for {mode}");
				}
				array2[i] = num;
			}
			int num2 = TccAddressFor(mode);
			int num3 = readByte(num2);
			if (num3 < 0)
			{
				return PlDefaultsResult.Unavailable(mode, $"EC {num2} unreadable; disabling PL editing for {mode}");
			}
			return new PlDefaultsResult(mode, new PlDefaultSet(array2[0], array2[1], array2[2], num3), Editable: true, $"EC defaults read for {mode}");
		}
		catch (Exception ex)
		{
			return PlDefaultsResult.Unavailable(mode, $"EC read failed for {mode}: {ex.Message}");
		}
	}

	public static PlDefaultsResult Read(PlDefaultMode mode, IEcReadTransport transport)
	{
		ArgumentNullException.ThrowIfNull(transport, "transport");
		return Read(mode, transport.ReadByte);
	}
}
