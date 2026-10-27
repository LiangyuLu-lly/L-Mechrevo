// ⭐⭐ 2026-10-18（机主：遥测里 CPU/GPU 后面的占用率显示不对）：
//   ⭐ **CPU 占用率本地采样** —— ⭐ 绕开"只有 GCU/MQTT 推送才有占用率"的旧链路 ✅
//
// ⭐ 取材（⭐ 按优先级）：
//   ① ⭐ <b>性能计数器</b> <c>Processor Information\_Total\% Processor Utility</c>
//      —— ⭐ Windows 8+ / 任务管理器**同一个口径**（含睿频折算）✅；
//      ⚠️ 该计数器在个别系统/权限下不可用 ⇒ 自动退到 ②；
//   ② ⭐ <b>性能计数器</b> <c>Processor Information\_Total\% Processor Time</c>（经典时间口径）✅；
//   ③ ⭐ <b>GetSystemTimes 差值</b> —— ⭐ 内核级系统总时间（⭐ 免计数器 / 无需驱动 / 一定有 ✅）：
//      <c>busy = (kernel + user) − idle</c>，<c>usage% = busy / (kernel + user) × 100</c>
//      （⚠️ <c>kernel</c> 时间**已包含 idle** ⇒ 分母要减 idle 后再算，⛔ 不要直接把 idle 当"空闲分母"）。
//
// ⚠️ 采样节流：⭐ 两次真实采样至少间隔 <see cref="MinSampleIntervalMs" />（默认 900 ms）——
//    ⭐ 速率型性能计数器/系统时间差**都要求两次采样**才有意义；⭐ 过密调用只会得到 0/抖动值 ✅。
// ⚠️ 取不到 ⇒ 返回 <c>null</c>（⭐ 由调用方保留旧值、⛔ 不更新时间戳 ⇒ 界面按"过期"显示「—」
//    —— ⛔ **绝不返回 0 冒充"占用 0%」** ✅）。
//
// 行尾：纯 LF（与 CpuFrequencySampler / HardwareControl 一致）✅。

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>⭐ CPU 占用率本地采样器（⭐ 性能计数器优先、GetSystemTimes 兜底）✅。</summary>
internal sealed class CpuUsageSampler : IDisposable
{
	private const string CategoryName = "Processor Information";

	private const string UtilityCounterName = "% Processor Utility";

	private const string TimeCounterName = "% Processor Time";

	private const string InstanceName = "_Total";

	/// <summary>⭐ 两次**真实**采样的最小间隔（毫秒）：⭐ 速率计数器/时间差都需要两次采样才有效 ✅。</summary>
	internal const int MinSampleIntervalMs = 900;

	private readonly object _sync = new object();

	/// <summary>⭐ 性能计数器（⭐ <c>null</c> = 不可用/还没初始化好 ⇒ 走 GetSystemTimes 兜底 ✅）。</summary>
	private PerformanceCounter _counter;

	private string _counterName = "";

	private int _initializing;

	private long _nextInitializationTick;

	private long _nextSystemTimesRetryTick;

	private bool _disposed;

	private int? _lastUsage;

	private long _lastRealSampleTicks;

	private int _lastIntervalMs;

	private string _source = "未就绪";

	private bool _systemTimesPrimed;

	private ulong _prevIdle;

	private ulong _prevKernel;

	private ulong _prevUser;

	/// <summary>⭐ 本采样器当前**实际生效**的来源（⭐ 供日志/诊断包如实标注 ✅）。</summary>
	internal string Source
	{
		get
		{
			lock (_sync)
			{
				return _source;
			}
		}
	}

	/// <summary>⭐ 最近两次真实采样的间隔（毫秒；0 = 还没完成两次 ✅）。</summary>
	internal int LastIntervalMs
	{
		get
		{
			lock (_sync)
			{
				return _lastIntervalMs;
			}
		}
	}

	/// <summary>⭐ 当前是否在用 GetSystemTimes 兜底（⭐ 诊断用：性能计数器不可用时为真 ✅）。</summary>
	internal bool UsingSystemTimesFallback
	{
		get
		{
			lock (_sync)
			{
				return _counter == null && _systemTimesPrimed;
			}
		}
	}

	/// <summary>
	/// ⭐ 采一次 CPU 占用率（0–100）✅。
	/// <para>⚠️ 返回 <c>null</c> = **本轮没有新的有效值**（⭐ 首次采样 / 节流窗口内 / 两个来源都失败）
	/// ⇒ ⭐ 调用方**保留旧值、⛔ 不更新时间戳** ✅。</para>
	/// </summary>
	internal int? Sample()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return null;
			}
			long now = Environment.TickCount64;
			// ⭐ 节流：⭐ 真实采样至少隔 <see cref="MinSampleIntervalMs" />；
			//   ⚠️ 窗口内**返回缓存值**（⭐ 这是"最近一次有效值"，⛔ 不是新采样 ✅）
			if (_lastUsage.HasValue && _lastRealSampleTicks != 0L && now - _lastRealSampleTicks < MinSampleIntervalMs)
			{
				return _lastUsage;
			}
			int? value = null;
			string source = null;
			if (_counter != null)
			{
				try
				{
					float pct = _counter.NextValue();
					// ⚠️ `% Processor Utility` 在睿频/高负载下**可能 >100%**（官方文档明说"可超过 100"）⚠️
					//   ⇒ ⭐ 只要非负且有限就接受，⭐ 再夹到 0–100（⭐ 与任务管理器一样封顶 100% ✅），
					//   ⛔ 不要因为 >100 就判无效 —— 那会让来源在"性能计数器 / GetSystemTimes"之间来回跳 ❌
					if (float.IsFinite(pct) && pct >= 0f)
					{
						value = (int)Math.Round(Math.Clamp(pct, 0f, 100f));
						source = "性能计数器 " + CategoryName + "/" + InstanceName + "/" + _counterName;
					}
				}
				catch (Exception ex)
				{
					InvalidateCounterLocked("CPU 占用计数器读取失败：" + ex.Message);
				}
			}
			if (!value.HasValue)
			{
				if (_counter == null)
				{
					// ⭐ 计数器不可用 ⇒ 后台重试初始化（⭐ 期间由 GetSystemTimes 提供值 ✅）
					StartInitializationLocked(now);
				}
				int? fallback = SampleSystemTimesLocked(now);
				if (fallback.HasValue)
				{
					value = fallback;
					source = "GetSystemTimes 差值（系统总时间口径）";
				}
			}
			if (value.HasValue)
			{
				_lastIntervalMs = (_lastRealSampleTicks == 0L) ? 0 : (int)(now - _lastRealSampleTicks);
				_lastRealSampleTicks = now;
				_lastUsage = value;
				_source = source;
				return value;
			}
			return null;
		}
	}

	/// <summary>⭐ 后台初始化性能计数器（⭐ 不在采样线程上阻塞创建计数器 ✅）。</summary>
	private void StartInitializationLocked(long now)
	{
		if (_disposed || _counter != null || now < _nextInitializationTick || Interlocked.CompareExchange(ref _initializing, 1, 0) != 0)
		{
			return;
		}
		Task.Run((Action)Initialize);
	}

	private void Initialize()
	{
		PerformanceCounter created = null;
		string createdName = "";
		try
		{
			created = new PerformanceCounter(CategoryName, UtilityCounterName, InstanceName, readOnly: true);
			// ⭐ 速率计数器首帧返回 0（需两次采样才有效）⇒ 先预采一次 ✅
			created.NextValue();
			createdName = UtilityCounterName;
			Logger.WriteLine("CPU usage source: 性能计数器 " + CategoryName + "/" + InstanceName + "/" + UtilityCounterName + "（任务管理器同源）");
		}
		catch (Exception ex)
		{
			created?.Dispose();
			created = null;
			Logger.WriteLineThrottled("cpu-usage", "CPU 占用：% Processor Utility 不可用（" + ex.Message + "）⇒ 改试 % Processor Time", 60000);
			try
			{
				created = new PerformanceCounter(CategoryName, TimeCounterName, InstanceName, readOnly: true);
				created.NextValue();
				createdName = TimeCounterName;
				Logger.WriteLine("CPU usage source: 性能计数器 " + CategoryName + "/" + InstanceName + "/" + TimeCounterName);
			}
			catch (Exception ex2)
			{
				created?.Dispose();
				created = null;
				Logger.WriteLineThrottled("cpu-usage",
					"CPU 占用：性能计数器都不可用（" + ex2.Message + "）⇒ 回退 GetSystemTimes 差值（系统总时间口径）", 60000);
			}
		}
		lock (_sync)
		{
			if (!_disposed && created != null)
			{
				_counter = created;
				_counterName = createdName;
				created = null;
				_nextInitializationTick = 0L;
			}
			Interlocked.Exchange(ref _initializing, 0);
			if (_counter == null)
			{
				// ⭐ 失败后 60 s 再试（⭐ 期间 GetSystemTimes 一直能出值 ✅）
				_nextInitializationTick = Environment.TickCount64 + 60000;
			}
		}
		created?.Dispose();
	}

	/// <summary>
	/// ⭐ <c>GetSystemTimes</c> 差值口径（⭐ 系统总时间；⭐ 内核时间**已包含 idle** ✅）：
	/// <c>usage% = ((kernel + user) − idle) / (kernel + user) × 100</c> ✅
	/// </summary>
	private int? SampleSystemTimesLocked(long now)
	{
		if (now < _nextSystemTimesRetryTick)
		{
			return null;
		}
		if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
		{
			_nextSystemTimesRetryTick = now + 30000;
			Logger.WriteLineThrottled("cpu-usage", "CPU 占用：GetSystemTimes 调用失败 ⇒ 30 s 后重试", 60000);
			return null;
		}
		ulong idle = idleTime.ToUInt64();
		ulong kernel = kernelTime.ToUInt64();
		ulong user = userTime.ToUInt64();
		if (!_systemTimesPrimed)
		{
			_prevIdle = idle;
			_prevKernel = kernel;
			_prevUser = user;
			_systemTimesPrimed = true;
			return null;
		}
		ulong idleDelta = idle - _prevIdle;
		ulong kernelDelta = kernel - _prevKernel;
		ulong userDelta = user - _prevUser;
		_prevIdle = idle;
		_prevKernel = kernel;
		_prevUser = user;
		ulong total = kernelDelta + userDelta;
		if (total == 0L)
		{
			return null;
		}
		ulong busy = (total > idleDelta) ? (total - idleDelta) : 0L;
		double pct = busy * 100.0 / total;
		if (!double.IsFinite(pct))
		{
			return null;
		}
		return (int)Math.Round(Math.Clamp(pct, 0.0, 100.0));
	}

	private void InvalidateCounterLocked(string reason)
	{
		PerformanceCounter counter = _counter;
		_counter = null;
		_counterName = "";
		_nextInitializationTick = Environment.TickCount64 + 5000;
		Logger.WriteLineThrottled("cpu-usage", "CPU 占用：" + reason + " ⇒ 暂时改用 GetSystemTimes 差值，5 s 后重试计数器", 30000);
		counter?.Dispose();
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

	// ══════════════════════════════════════════════════════════════════
	//  GetSystemTimes（⭐ kernel32 官方导出）
	// ══════════════════════════════════════════════════════════════════

	[StructLayout(LayoutKind.Sequential)]
	private struct FileTime
	{
		public uint Low;

		public uint High;

		public ulong ToUInt64()
		{
			return ((ulong)High << 32) | Low;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
}
