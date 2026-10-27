namespace MechrevoLite.Hardware;

internal interface IFrameClock
{
	long Frequency { get; }

	long Timestamp { get; }

	void Sleep(int milliseconds);

	void Spin();
}
