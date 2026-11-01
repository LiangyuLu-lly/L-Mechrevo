using System;

namespace MechrevoLite.Hardware;

internal sealed class LightingRestoreCoordinator
{
	private readonly object _sync = new object();

	private int _completedGeneration = -1;

	private int _inFlightGeneration = -1;

	private bool _inFlight;

	internal LightingRestoreDecision TryBegin(int generation, bool force)
	{
		lock (_sync)
		{
			if (_inFlight)
			{
				return LightingRestoreDecision.DeferInFlight;
			}
			if (!force && generation <= _completedGeneration)
			{
				return LightingRestoreDecision.SkipCompleted;
			}
			_inFlight = true;
			_inFlightGeneration = generation;
			return LightingRestoreDecision.Run;
		}
	}

	internal void Complete(int generation, bool success)
	{
		lock (_sync)
		{
			if (_inFlight && _inFlightGeneration == generation)
			{
				_inFlight = false;
				_inFlightGeneration = -1;
				if (success)
				{
					_completedGeneration = Math.Max(_completedGeneration, generation);
				}
			}
		}
	}
}
