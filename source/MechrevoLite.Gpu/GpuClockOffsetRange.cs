namespace MechrevoLite.Gpu;

public sealed record GpuClockOffsetRange(int Minimum, int Maximum, int Current)
{
	public bool IsAdjustable => Maximum > Minimum;

	public bool Contains(int value)
	{
		if (IsAdjustable && value >= Minimum)
		{
			return value <= Maximum;
		}
		return false;
	}
}
