using System.Diagnostics;
using System.Threading;

namespace MechrevoLite.Hardware;

internal sealed class SystemFrameClock : IFrameClock
{
	public long Frequency => Stopwatch.Frequency;

	public long Timestamp => Stopwatch.GetTimestamp();

	public void Sleep(int milliseconds)
	{
		Thread.Sleep(milliseconds);
	}

	public void Spin()
	{
		Thread.SpinWait(20);
	}
}
