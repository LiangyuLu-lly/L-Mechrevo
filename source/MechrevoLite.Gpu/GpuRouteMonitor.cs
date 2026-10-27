using System;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Gpu;

public static class GpuRouteMonitor
{
	public const int StaleAfterMilliseconds = 3000;

	private static readonly object Sync = new object();

	private static GpuRouteReadback _last = GpuRouteReadback.Unavailable;

	private static long _lastTick;

	private static bool _hasReading;

	private static int _refreshing;

	public static GpuRouteReadback Last
	{
		get
		{
			lock (Sync)
			{
				return _last;
			}
		}
	}

	public static event Action? Changed;

	public static GpuRouteReadback Refresh()
	{
		GpuRouteReadback gpuRouteReadback;
		try
		{
			gpuRouteReadback = GpuRouteProbe.Read();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU route readback failed: " + ex.Message);
			gpuRouteReadback = GpuRouteReadback.Unavailable;
		}
		bool flag;
		lock (Sync)
		{
			flag = !_hasReading || gpuRouteReadback != _last;
			_last = gpuRouteReadback;
			_lastTick = Environment.TickCount64;
			_hasReading = true;
		}
		if (flag)
		{
			Logger.WriteLine("GPU route readback: " + gpuRouteReadback);
			try
			{
				Changed?.Invoke();
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("GPU route change handler failed: " + ex2.Message);
			}
		}
		return gpuRouteReadback;
	}

	public static async Task<GpuRouteReadback> RefreshAsync(int timeoutMilliseconds = 500)
	{
		Task<GpuRouteReadback> task = Task.Run((Func<GpuRouteReadback>)Refresh);
		try
		{
			return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TimeoutException)
		{
			Logger.WriteLine($"GPU route readback took longer than {timeoutMilliseconds} ms; using the previous value.");
			return Last;
		}
	}

	public static GpuRouteReadback Snapshot()
	{
		bool hasReading;
		long num;
		lock (Sync)
		{
			hasReading = _hasReading;
			num = Environment.TickCount64 - _lastTick;
		}
		if (!hasReading)
		{
			return Refresh();
		}
		if (num >= 3000 && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
		{
			Task.Run(() =>
			{
				try
				{
					Refresh();
				}
				finally
				{
					Interlocked.Exchange(ref _refreshing, 0);
				}
			});
		}
		return Last;
	}

	internal static void ResetForTests()
	{
		lock (Sync)
		{
			_last = GpuRouteReadback.Unavailable;
			_lastTick = 0L;
			_hasReading = false;
		}
	}
}
