using System;

namespace MechrevoLite.Hardware;

internal sealed class FramePacer
{
	private readonly IFrameClock _clock;

	private long _nextTick;

	private long _spinMarginTicks;

	internal FramePacer(IFrameClock clock)
	{
		_clock = clock;
	}

	internal void Pace(int fps)
	{
		long frequency = _clock.Frequency;
		long num = frequency / Math.Clamp(fps, 15, 60);
		long timestamp = _clock.Timestamp;
		if (_nextTick == 0L || timestamp > _nextTick + num * 2)
		{
			_nextTick = timestamp;
		}
		_nextTick += num;
		long num2 = num / 2;
		if (_spinMarginTicks == 0L)
		{
			_spinMarginTicks = frequency / 1000;
		}
		long num3 = Math.Min(_spinMarginTicks, num2);
		long num4 = _nextTick - timestamp;
		if (num4 > num3)
		{
			long num5 = num4 - num3;
			int milliseconds = (int)Math.Max(1L, num5 * 1000 / frequency);
			long timestamp2 = _clock.Timestamp;
			_clock.Sleep(milliseconds);
			long num6 = _clock.Timestamp - timestamp2;
			if (num6 > num5)
			{
				long num7 = num6 - num5;
				if (num7 > _spinMarginTicks)
				{
					_spinMarginTicks = num7;
				}
			}
		}
		long timestamp3 = _clock.Timestamp;
		while (_clock.Timestamp < _nextTick)
		{
			_clock.Spin();
			if (_clock.Timestamp - timestamp3 >= num2)
			{
				break;
			}
		}
	}
}
