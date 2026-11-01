using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// CPU 实时频率采样：性能计数器「Processor Information\_Total\% Processor Performance」× 基频（Win32_Processor.MaxClockSpeed）。
/// 任务管理器同源（APERF/MPERF 比值），支持 AMD/Intel、无需驱动。2026-10-11 实测结论：
///   - LHM 时钟传感器在本机（AMD 8745H）恒为 0（无 Ring0 驱动读 MSR）→ 不可用；
///   - % of Maximum Frequency / Processor Frequency 在 AMD 上恒报 P0（不随负载变化）→ 不可用；
///   - % Processor Performance 空闲/加压实时变化（加压实测 110% ≈ 4.2GHz boost）→ 与任务管理器一致的实时频率。
/// 频率 = pct% × MaxClockSpeed（与任务管理器「速度」列同款算法）。
/// </summary>
internal sealed class CpuFrequencySampler : IDisposable
{
	private const string CategoryName = "Processor Information";

	private const string CounterName = "% Processor Performance";

	private const string InstanceName = "_Total";

	private readonly object _sync = new object();

	private PerformanceCounter? _counter;

	private int _maxClockMhz;

	private int _initializing;

	private long _nextInitializationTick;

	private bool _disposed;

	/// <summary>返回当前全核平均频率（MHz）；尚未就绪或读取出错时返回 null（由调用方保留旧值）。</summary>
	public int? Sample()
	{
		PerformanceCounter counter;
		int maxClock;
		lock (_sync)
		{
			if (_disposed)
			{
				return null;
			}
			counter = _counter;
			maxClock = _maxClockMhz;
		}
		if (counter == null || maxClock <= 0)
		{
			StartInitialization();
			return null;
		}
		try
		{
			float pct = counter.NextValue();
			// 速率计数器首次采样返回 0（需两次采样才有效），本轮跳过、下一轮才有值。
			if (!float.IsFinite(pct) || pct <= 0f)
			{
				return null;
			}
			return (int)Math.Round(pct * maxClock / 100.0);
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
		PerformanceCounter? counter = null;
		try
		{
			int maxClock = ReadMaxClockMhz();
			if (maxClock <= 0)
			{
				Logger.WriteLineThrottled("cpu-freq", "CPU base clock (Win32_Processor.MaxClockSpeed) unavailable; frequency sampler disabled.", 30000);
				return;
			}
			counter = new PerformanceCounter(CategoryName, CounterName, InstanceName, readOnly: true);
			counter.NextValue();
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				_maxClockMhz = maxClock;
				_counter = counter;
				counter = null;
			}
			Logger.WriteLine("CPU frequency source: % Processor Performance × " + maxClock + " MHz (task-manager parity)");
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("cpu-freq", "CPU frequency counter initialization failed: " + ex.Message, 30000);
		}
		finally
		{
			counter?.Dispose();
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

	private static int ReadMaxClockMhz()
	{
		// 优先注册表 ~MHz（REG_DWORD，无 WMI 类型封包问题）：AMD 8745H 实测 3793（≈基频）。
		try
		{
			using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
			object? value = key?.GetValue("~MHz");
			if (value != null)
			{
				int mhz = Convert.ToInt32(value);
				if (mhz > 0)
				{
					return mhz;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("cpu-freq", "Registry ~MHz read failed: " + ex.Message, 60000);
		}
		// 兜底：WMI MaxClockSpeed（CIM 返回类型不定，用 Convert 兼容 UInt16/UInt32）。
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
			foreach (var obj in searcher.Get())
			{
				object? value = obj["MaxClockSpeed"];
				if (value != null)
				{
					int mhz = Convert.ToInt32(value);
					if (mhz > 0)
					{
						return mhz;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("cpu-freq", "Win32_Processor.MaxClockSpeed query failed: " + ex.Message, 60000);
		}
		return 0;
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
			Logger.WriteLineThrottled("cpu-freq", "CPU frequency counter read failed: " + error.Message, 30000);
		}
	}

	public void Dispose()
	{
		PerformanceCounter? counter;
		lock (_sync)
		{
			_disposed = true;
			counter = _counter;
			_counter = null;
		}
		counter?.Dispose();
	}
}
