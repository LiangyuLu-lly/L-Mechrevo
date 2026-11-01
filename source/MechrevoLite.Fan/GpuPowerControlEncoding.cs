namespace MechrevoLite.Fan;

public static class GpuPowerControlEncoding
{
	public const int ControlAddress = 1859;

	public const int ConfigurableTgpAddress = 1860;

	public const int DynamicBoostAddress = 1861;

	public const int MaximumTgpAddress = 1862;

	public const int CtgpEnableBit = 2;

	public const int DynamicBoostEnableBit = 1;

	public const int DynamicBoostFunCtrlBit = 0;

	public static byte EncodeControl(bool ctgpEnabled, bool dynamicBoostEnabled, bool dynamicBoostFunCtrl)
	{
		int num = 0;
		if (ctgpEnabled)
		{
			num |= 4;
		}
		if (dynamicBoostEnabled)
		{
			num |= 2;
		}
		if (dynamicBoostFunCtrl)
		{
			num |= 1;
		}
		return (byte)num;
	}

	public static (bool CtgpEnabled, bool DynamicBoostEnabled, bool DynamicBoostFunCtrl) DecodeControl(byte value)
	{
		return (CtgpEnabled: (value & 4) != 0, DynamicBoostEnabled: (value & 2) != 0, DynamicBoostFunCtrl: (value & 1) != 0);
	}
}
