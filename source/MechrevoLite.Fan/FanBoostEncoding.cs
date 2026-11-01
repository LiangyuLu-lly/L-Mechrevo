using System;
using MechrevoLite.Hardware;

namespace MechrevoLite.Fan;

public static class FanBoostEncoding
{
	public const int Address = 1873;

	public const int BoostMask = 64;

	public const int ModeBitLow = 4;

	public const int ModeBitHigh = 7;

	private static readonly int[] Bases = new int[3] { 0, 160, 16 };

	public static byte Encode(int logicalMode, bool boostEnabled)
	{
		if ((logicalMode < 0 || logicalMode > 2) ? true : false)
		{
			throw new ArgumentOutOfRangeException("logicalMode", logicalMode, "logical mode must be 0..2");
		}
		int num = Bases[logicalMode];
		if (boostEnabled)
		{
			num |= 0x40;
		}
		return (byte)num;
	}

	public static int Decode(byte value)
	{
		bool flag = (value & 0x10) != 0;
		bool flag2 = (value & 0x80) != 0;
		if (!flag && !flag2)
		{
			return 0;
		}
		if (!flag & flag2)
		{
			return 1;
		}
		if (flag && !flag2)
		{
			return 2;
		}
		return 0;
	}

	public static bool BoostEnabled(byte value)
	{
		return (value & 0x40) != 0;
	}

	public static int LogicalModeForSetterParameter(int parameter)
	{
		return parameter switch
		{
			1 => 0, 
			0 => 1, 
			2 => 2, 
			_ => throw new ArgumentOutOfRangeException("parameter", parameter, "setter parameter must be 0/1/2"), 
		};
	}

	public static bool IsAvailable(FeatureMatrix matrix)
	{
		ArgumentNullException.ThrowIfNull(matrix, "matrix");
		return matrix.IsSupported(FeatureBit.FanBoost);
	}
}
