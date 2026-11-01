using System.Threading;

namespace MechrevoLite.Hardware;

internal sealed class MqttReconnectCoordinator
{
	private int _inLoop;

	private int _pendingGeneration;

	private int _lastHandledGeneration;

	internal void MarkDisconnectRequested()
	{
		Interlocked.Increment(ref _pendingGeneration);
	}

	internal bool TryEnterLoop()
	{
		if (Interlocked.CompareExchange(ref _inLoop, 1, 0) != 0)
		{
			return false;
		}
		Volatile.Write(ref _lastHandledGeneration, Volatile.Read(in _pendingGeneration));
		return true;
	}

	internal bool ExitLoop()
	{
		Interlocked.Exchange(ref _inLoop, 0);
		return Volatile.Read(in _pendingGeneration) > Volatile.Read(in _lastHandledGeneration);
	}

	internal bool ShouldContinue(bool sessionReady, bool disposed)
	{
		if (!disposed)
		{
			return !sessionReady;
		}
		return false;
	}
}
