using System;
using System.Threading;

namespace MechrevoLite.Helpers;

internal sealed class UiRefreshCoalescer
{
	private int _queued;

	internal void Request(Action<Action> post, Action refresh)
	{
		if (Interlocked.Exchange(ref _queued, 1) != 0)
		{
			return;
		}
		try
		{
			post(() =>
			{
				Interlocked.Exchange(ref _queued, 0);
				refresh();
			});
		}
		catch
		{
			Interlocked.Exchange(ref _queued, 0);
			throw;
		}
	}
}
