using System;
using System.Threading;

namespace MechrevoLite.Helpers;

internal sealed class UserGestureLease
{
	internal const int ValidityMs = 15000;

	private long _expiresAt;

	internal bool IsActive
	{
		get
		{
			long num = Volatile.Read(in _expiresAt);
			if (num != 0L)
			{
				return Environment.TickCount64 <= num;
			}
			return false;
		}
	}

	internal bool Capture()
	{
		if (!NativeMethods.HasFreshUserInput())
		{
			return false;
		}
		Volatile.Write(ref _expiresAt, Environment.TickCount64 + 15000);
		return true;
	}

	internal void Consume()
	{
		Volatile.Write(ref _expiresAt, 0L);
	}

	internal void Reset()
	{
		Volatile.Write(ref _expiresAt, 0L);
	}
}
