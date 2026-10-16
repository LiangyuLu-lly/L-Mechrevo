using System;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Gpu;

public static class NvPreferredGpuMonitor
{
	private static int _last;

	private static int _refreshing;

	public static NvPreferredGpu Last => (NvPreferredGpu)Volatile.Read(in _last);

	public static event Action? Changed;

	public static void Store(NvPreferredGpu value)
	{
		if (Interlocked.Exchange(ref _last, (int)value) == (int)value)
		{
			return;
		}
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("NVIDIA preferred GPU change handler failed: " + ex.Message);
		}
	}

	public static void RefreshInBackground()
	{
		if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
		{
			return;
		}
		Task.Run(() =>
		{
			try
			{
				Store(NvPreferredGpuReader.ReadFromDriver());
			}
			finally
			{
				Interlocked.Exchange(ref _refreshing, 0);
			}
		});
	}

	internal static void ResetForTests()
	{
		Interlocked.Exchange(ref _last, 0);
	}
}
