using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

internal sealed class CpuPowerSampler : IDisposable
{
	private const string CategoryName = "Energy Meter";

	private const string CounterName = "Power";

	// Wujie/APU 上 Energy Meter 同时有 "Apu Power" 和 RAPL package 计数器；
	// "Apu Power" 是 APU 整体（含核显）功耗，不能当作 CPU 功耗，放到最后。
	private static readonly string[] PreferredInstances = new string[5] { "RAPL_Package0_PKG", "CPU Power", "Socket Power", "Current Socket Power", "Apu Power" };

	private readonly object _sync = new object();

	private PerformanceCounter? _counter;

	private int _initializing;

	private long _nextInitializationTick;

	private bool _disposed;

	public float? Sample()
	{
		PerformanceCounter counter;
		lock (_sync)
		{
			if (_disposed)
			{
				return null;
			}
			counter = _counter;
		}
		if (counter == null)
		{
			StartInitialization();
			return null;
		}
		try
		{
			return NormalizeMilliwatts(counter.NextValue());
		}
		catch (Exception error)
		{
			InvalidateCounter(counter, error);
			return null;
		}
	}

	public void Reset()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			_nextInitializationTick = 0L;
		}
		StartInitialization();
	}

	private static float? NormalizeMilliwatts(float milliwatts)
	{
		if (!float.IsFinite(milliwatts) || !(milliwatts > 0f))
		{
			return null;
		}
		return milliwatts / 1000f;
	}

	private void StartInitialization()
	{
		long tickCount = Environment.TickCount64;
		lock (_sync)
		{
			if (_disposed || _counter != null || tickCount < _nextInitializationTick || Interlocked.CompareExchange(ref _initializing, 1, 0) != 0)
			{
				return;
			}
		}
		Task.Run((Action)Initialize);
	}

	private void Initialize()
	{
		PerformanceCounter performanceCounter = null;
		PerformanceCounter performanceCounter2 = null;
		try
		{
			string[] instances = new PerformanceCounterCategory("Energy Meter").GetInstanceNames();
			string text = PreferredInstances.FirstOrDefault((string candidate) => instances.Contains(candidate, StringComparer.OrdinalIgnoreCase));
			if (text == null)
			{
				Logger.WriteLineThrottled("cpu-energy-meter", "Energy Meter CPU power counter is unavailable.", 30000);
				return;
			}
			performanceCounter = new PerformanceCounter("Energy Meter", "Power", text, readOnly: true);
			performanceCounter.NextValue();
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				performanceCounter2 = _counter;
				_counter = performanceCounter;
				performanceCounter = null;
			}
			Logger.WriteLine("CPU power source: Energy Meter " + text);
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("cpu-energy-meter", "Energy Meter CPU power initialization failed: " + ex.Message, 30000);
		}
		finally
		{
			performanceCounter2?.Dispose();
			performanceCounter?.Dispose();
			lock (_sync)
			{
				Interlocked.Exchange(ref _initializing, 0);
				if (_counter == null)
				{
					_nextInitializationTick = Environment.TickCount64 + 30000;
				}
			}
		}
	}

	private void InvalidateCounter(PerformanceCounter counter, Exception error)
	{
		bool flag = false;
		lock (_sync)
		{
			if (_counter == counter)
			{
				_counter = null;
				_nextInitializationTick = Environment.TickCount64 + 5000;
				flag = true;
			}
		}
		if (flag)
		{
			counter.Dispose();
			Logger.WriteLineThrottled("cpu-energy-meter", "Energy Meter CPU power read failed: " + error.Message);
		}
	}

	public void Dispose()
	{
		PerformanceCounter counter;
		lock (_sync)
		{
			_disposed = true;
			counter = _counter;
			_counter = null;
		}
		counter?.Dispose();
	}
}
