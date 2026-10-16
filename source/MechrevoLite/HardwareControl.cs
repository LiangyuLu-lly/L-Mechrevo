using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Battery;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;

namespace MechrevoLite;

public static class HardwareControl
{
	// ⭐⭐ 2026-10-16：⭐ "CPU 温度已切到 LHM" 只记一次（⭐ 免得 3 秒一行刷爆日志 ⚠️）
	private static int _lhmCpuTempLogged;

	public static int cpuTemp;

	public static int? cpuUsage;

	public static int? cpuFan;

	public static int cpuFanRPM;

	public static int gpuTemp;

	public static int? gpuUsage;

	public static int? gpuFan;

	public static int gpuFanRPM;

	public static int? midFan;

	public static int cpuFrequency;

	public static int gpuCoreFreq;

	public static float? cpuPower;

	public static float? gpuPower;

	public static int? ramUsage;

	public static int? ramUsedMb;

	public static int? vramUsage;

	public static int? vramUsedMb;

	public static int batteryCapacity;

	public static int batteryCharge;

	public static decimal batteryHealth = -1m;

	public static decimal? batteryRate;

	public static bool chargeWatt;

	internal static Func<decimal?> batteryRateReader = ReadBatteryStatusWatts;

	internal const int BatteryRateRefreshIntervalMs = 5000;

	private static long _lastBatteryRateRead;

	public static LhmMonitor? lhm;

	private static readonly object lhmLock = new object();

	private static CpuPowerSampler? cpuPowerSampler;

	private static CpuFrequencySampler? cpuFreqSampler;

	// ================= 2026-10-09（NVML 原生崩溃修复）：本地功耗采样链健康状态机 =================
	// 事故依据：.NET Runtime 事件 1026 + Application Error 1000 的堆栈为
	//   LibreHardwareMonitor.Interop.NvidiaML.NvmlDeviceGetPowerUsage(NvmlDevice)   ← 出错模块 coreclr.dll，异常代码 0xc0000005
	//   ← LibreHardwareMonitor.Hardware.Gpu.NvidiaGpu.Update() ← LhmMonitor.Update()
	//   ← HardwareControl.RefreshLocalMonitoring() ← SampleLocalPower()
	//   ← CustomModeForm.UpdatePowerWallVerdict() ← 构造函数里挂的 2s Timer（消息循环）
	// 即：崩在「自定义窗功耗墙判定 → 本地功耗采样 → LHM/NVML」这条链上，与 NVAPI/OC 无关。
	// 本文件提供 (a) 异常防护 (b) 句柄自愈 (c) 降级不致命 (d) 生命周期 (e) 降频 的调用侧实现。

	/// <summary>(a) 采样步骤连续失败到该阈值就视为硬失败。</summary>
	internal const int SamplingFailureThreshold = 3;

	/// <summary>(b) 最多重建几次（NVML 重置 + LHM 监控对象重建），之后进入隔离降级。</summary>
	internal const int MaxRebuildAttempts = 4;

	/// <summary>(c) 降级隔离后自动重试的起始间隔（翻倍退避，上限 10 分钟）。</summary>
	internal const long QuarantineBaseMs = 120000L;

	internal const long QuarantineMaxMs = 600000L;

	private static int _samplingFailures;

	private static int _rebuildAttempts;

	private static long _nextRebuildAtTicks;

	private static int _lhmRebuildInFlight;

	private static bool _localSamplingQuarantined;

	private static long _quarantineUntilTicks;

	private static long _quarantineMs = 120000L;

	private static string _quarantineReason = "";

	internal static readonly PowerWallVerifier powerWall = new PowerWallVerifier();

	public static bool readFans;

	public static bool readUsage;

	public static bool readMemory;

	public static bool readPower;

	public static bool readBattery;

	public static IGpuControl? GpuControl => null;

	private static BatteryStatusReading? _batteryStatus;

	internal const int PowerInputReadTimeoutMs = 250;

	private static PowerInputSampler _powerInputSampler = CreatePowerInputSampler();

	private static int _powerInputRefreshing;

	internal static BatteryStatusReading? BatteryStatus => Volatile.Read(in _batteryStatus);

	internal static PowerInputSample? PowerInput => Volatile.Read(in _powerInputSampler).Current;

	private static decimal? ReadBatteryStatusWatts()
	{
		BatteryStatusReading? reading = BatteryRateReader.ReadStatus();
		Volatile.Write(ref _batteryStatus, reading);
		return BatteryRateReader.FromMilliwatts(reading?.RateMilliwatts);
	}

	private static PowerInputSampler CreatePowerInputSampler()
	{
		return new PowerInputSampler(() => MechrevoService.EcReadTransportFactory(), () => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online, PowerInputDecodeEnabled);
	}

	internal static bool PowerInputDecodeEnabled()
	{
		// 群友版无 10/20 系独显分级（Gen1020），EC 供电寄存器对 30/40/50 系均解码；
		// 读取失败由采样器回退为 ExternalUnknown，不误报具体供电方式。
		return true;
	}

	internal static void RefreshPowerInput(bool force = false)
	{
		PowerInputSampler sampler = Volatile.Read(in _powerInputSampler);
		if (!sampler.NeedsRefresh(force) || Interlocked.CompareExchange(ref _powerInputRefreshing, 1, 0) != 0)
		{
			return;
		}
		Task task;
		try
		{
			task = Task.Run(() =>
			{
				try
				{
					sampler.Refresh(force);
				}
				catch (Exception ex)
				{
					Logger.WriteLineIfChanged("power-input", "Power input refresh failed: " + ex.Message);
				}
				finally
				{
					Interlocked.Exchange(ref _powerInputRefreshing, 0);
				}
			});
		}
		catch
		{
			Interlocked.Exchange(ref _powerInputRefreshing, 0);
			return;
		}
		try
		{
			task.Wait(250);
		}
		catch
		{
		}
	}

	internal static void RefreshBatteryRate(bool force = false)
	{
		long tickCount = Environment.TickCount64;
		if (force || tickCount - _lastBatteryRateRead >= 5000)
		{
			_lastBatteryRateRead = tickCount;
			batteryRate = batteryRateReader();
		}
	}

	public static void EnableLocalMonitoring()
	{
		lock (lhmLock)
		{
			if (lhm == null)
			{
				lhm = new LhmMonitor();
			}
			if (cpuPowerSampler == null)
			{
				cpuPowerSampler = new CpuPowerSampler();
			}
			if (cpuFreqSampler == null)
			{
				cpuFreqSampler = new CpuFrequencySampler();
			}
			// 重新启用监控时清掉上一次的降级状态。
			_localSamplingQuarantined = false;
			_quarantineUntilTicks = 0L;
			_quarantineMs = QuarantineBaseMs;
			_rebuildAttempts = 0;
			_nextRebuildAtTicks = 0L;
			Interlocked.Exchange(ref _samplingFailures, 0);
		}
	}

	public static void DisableLocalMonitoring()
	{
		lock (lhmLock)
		{
			// (d) 生命周期：先断开引用再 Dispose；LhmMonitor.Dispose() 内部置 _disposed，
			// 之后即便还有残留的定时器 tick 落到 RefreshLocalMonitoring，也只会看到 lhm == null 直接早退，
			// 绝不会在监控对象已释放后继续采样。
			LhmMonitor? lhmMonitor = lhm;
			lhm = null;
			lhmMonitor?.Dispose();
			cpuPowerSampler?.Dispose();
			cpuPowerSampler = null;
			cpuFreqSampler?.Dispose();
			cpuFreqSampler = null;
			cpuPower = (gpuPower = null);
			vramUsedMb = (vramUsage = null);
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9 · "接传感器"）：⭐ **传感器取值优先级** —— ⭐ **对所有机型生效** ✅
	///
	/// <para>⭐⭐ <b>用户定的规则</b>（2026-10-16 两次确认，⭐ 第二次明确了适用范围）：
	/// 「⭐ 前面说的就是**所有机型**优先用 LHM 的传感器数据，⭐ 读不到的再用它们**自己接口**的」✅
	/// ⇒ ⭐ 即：⭐ <b>LHM 第一优先</b>，⭐ LHM 读不到的部分才回退到 ⭐ **各家自己的厂商接口**
	/// （⭐ GCU/同方 走 MQTT，⭐ 宝龙达走 WMI ✅）。</para>
	///
	/// <para>⭐⭐ <b>实测得到的"LHM 能读 / 读不到"清单</b>（⭐ `--sensor-dump` · ⭐ 用户确认所有机型一致）：
	/// <list type="bullet">
	/// <item>⭐ <b>CPU 温度</b> ⇒ ⭐ **LHM 能读** ✅ ⇒ ⭐ **用 LHM** ✅</item>
	/// <item>⭐ <b>GPU 温度</b> ⇒ ⚠️ LHM 检测到 NVIDIA 就关掉 GPU 那支（⭐ NVML 崩溃修复 ⚠️）
	/// ⇒ ⭐ **AMD/Intel 独显或无独显机型 LHM 可读** ✅，⭐ NVIDIA 机型读不到 ⇒ ⭐ 回退（⭐ 见下 ✅）</item>
	/// <item>⭐ <b>风扇转速</b> ⇒ ❌ ⭐ **LHM 一个都读不到**（⭐ 实测 = 0 个 ✅）
	/// ⇒ ⭐ **必须用厂商接口**（⭐ GCU 推送 / ⭐ 宝龙达功能号 13 ✅）</item>
	/// </list></para>
	///
	/// <para>⭐⭐ <b>本方法的取值顺序</b>：
	/// <list type="number">
	/// <item>⭐ <b>CPU 温度</b>：⭐ **LHM** ⇒ ⭐ 读不到才用 ⭐ <c>hw.CpuTemp</c>（⭐ GCU 或宝龙达已填 ✅）</item>
	/// <item>⭐ <b>GPU 温度</b>：⭐ **LHM** ⇒ ⭐ 读不到才用 ⭐ <c>hw.GpuTemp</c>（⭐ GCU ✅）
	/// ⇒ ⭐ 再不行才用 ⭐ **加固过的 NVML**（⭐ 补"所有机型共同的缺口" ✅）</item>
	/// <item>⭐ <b>风扇转速</b>：⭐ 直接用 ⭐ <c>hw.CpuFanRpm</c> / ⭐ <c>hw.GpuFanRpm</c>
	/// （⭐ 厂商接口填的 ✅）；⭐ 且 ⚠️ **只在当前为 0 时才填** —— ⭐ 免得覆盖 GCU 刚推来的值 ✅</item>
	/// </list></para>
	///
	/// <para>⚠️ <b>对 GCU 机型的影响</b>（⭐ 如实说明，⭐ 这是**用户明确要求的**行为改变 ⚠️）：
	/// ⭐ 以前 GCU 机型的 CPU 温度**只**来自 GCU 推送；⭐ 现在**只要 LHM 读得到就用 LHM** ⚠️
	/// ⇒ ⭐ 数值可能与以前略有差异（⭐ 传感器点位不同 ✅），⭐ 这是**按用户要求**改的 ✅。
	/// ⭐ 首次生效时会 ⭐ **记一行日志**（⭐ 便于回查"显示的温度怎么换了来源" ✅）。</para>
	/// </summary>
	private static void ApplySensorPriority(LhmMonitor lhmMonitor)
	{
		MechrevoHw hw;
		try
		{
			hw = Program.hw;
		}
		catch
		{
			return;
		}
		if (hw == null)
		{
			return;
		}
		bool isBaolongda = hw.UsesBaolongda;
		// ── ① CPU 温度：⭐ LHM 优先（⭐ 所有机型 ✅）⇒ ⭐ 读不到才用厂商的 ✅ ──
		try
		{
			float? fromLhm = lhmMonitor.CpuTemperature;
			if (fromLhm.HasValue && fromLhm.Value > 0f)
			{
				int celsius = (int)Math.Round(fromLhm.Value);
				if (cpuTemp != celsius)
				{
					cpuTemp = celsius;
				}
				// ⭐ 首次（或来源变化后首次）记一行，⭐ 便于回查"温度来源变成了 LHM" ✅
				if (Interlocked.Exchange(ref _lhmCpuTempLogged, 1) == 0)
				{
					Logger.WriteLine($"传感器优先级：⭐ CPU 温度改用 **LM 自带的 LHM**（传感器={lhmMonitor.CpuTemperatureSensorName}，"
						+ $"值={celsius}℃）⇒ ⭐ 按用户要求「所有机型优先用 LHM」✅（后端={(isBaolongda ? "宝龙达" : (hw.UsesBuiltInModes ? "老同方" : "GCU"))}）");
				}
			}
			else
			{
				// ⭐ LHM 读不到 ⇒ ⭐ 回退厂商接口 ✅
				//   ⭐ 宝龙达：`RefreshBaolongdaSensors` 已把值放进 `CpuTemp`/`BaolongdaCpuTemp` ✅
				//   ⭐ GCU / ⭐ 老同方：`CpuTemp` 由推送或老接口填 ✅
				int fromVendor = (hw.BaolongdaCpuTemp > 0) ? hw.BaolongdaCpuTemp : hw.CpuTemp;
				if (fromVendor > 0)
				{
					cpuTemp = fromVendor;
					// ⭐ 备注：⭐ 宝龙达机型上 `RefreshBaolongdaSensors` 也会写 `CpuTemp`，
					//    所以这里两条路都通；⭐ 不重复记账 ✅
				}
			}
		}
		catch
		{
		}
		// ── ② GPU 温度：⭐ LHM 优先 ⇒ ⭐ 厂商（GCU）⇒ ⭐ 再不行 NVML ✅ ──
		try
		{
			bool filled = false;
			float? lhmGpu = lhmMonitor.GpuTemperature;
			if (lhmGpu.HasValue && lhmGpu.Value > 0f)
			{
				gpuTemp = (int)Math.Round(lhmGpu.Value);
				filled = true;
			}
			if (!filled && hw.GpuTemp > 0)
			{
				// ⭐ 厂商接口（⭐ GCU 推送 ✅）
				gpuTemp = hw.GpuTemp;
				filled = true;
			}
			if (!filled && !NvmlHelper.Unavailable)
			{
				// ⭐ 通用缺口兜底：⭐ 加固过的 NVML（⭐ NVIDIA 机型 ✅）
				float? fromNvml = NvmlHelper.GetGpuTemperature();
				if (fromNvml.HasValue && fromNvml.Value > 0f)
				{
					gpuTemp = (int)Math.Round(fromNvml.Value);
				}
			}
		}
		catch
		{
		}
		// ── ③ 风扇转速：⭐ LHM 读不到 ⇒ ⭐ 厂商接口（⭐ 且只补 0，⛔ 不覆盖已有值 ✅）──
		try
		{
			if (cpuFanRPM <= 0)
			{
				int cpuFanFromHw = hw.CpuFanRpm;
				if (cpuFanFromHw > 0)
				{
					cpuFanRPM = cpuFanFromHw;
				}
			}
			if (gpuFanRPM <= 0)
			{
				int gpuFanFromHw = hw.GpuFanRpm;
				if (gpuFanFromHw > 0)
				{
					gpuFanRPM = gpuFanFromHw;
				}
			}
		}
		catch
		{
		}
		// ── ④ GPU 频率：⭐ LHM 优先 ⇒ ⭐ 厂商 ⇒ ⭐ 再不行 NVML ✅ ──
		//   ⭐ 依据用户 2026-10-16：「⭐ 显卡 / cpu 频率温度 LHM 应该都能读」✅
		//   ⚠️ 但 NVIDIA 机型上**我们主动关掉了 LHM 的 GPU 那支**（⭐ NVML 崩溃修复 ⚠️）
		//      ⇒ ⭐ 那种情况由 ⭐ 加固过的 NVML 提供（⭐ 数据同源 ✅）
		//   ⭐ 依据用户 2026-10-16 最新指示：「⭐ 除了风扇先尽量不依赖各家自己的接口」
		//      ⇒ ⭐ 所以 ⭐ **LHM → NVML（我们自己的加固层）→ 厂商**⭐ 这个顺序，
		//        ⭐ 厂商标本只当**最后兜底** ✅
		try
		{
			bool gpuClockFilled = false;
			float? lhmGpuClock = lhmMonitor.GpuClockMhz;
			if (lhmGpuClock.HasValue && lhmGpuClock.Value > 0f)
			{
				gpuCoreFreq = (int)Math.Round(lhmGpuClock.Value);
				gpuClockFilled = true;
			}
			if (!gpuClockFilled && !NvmlHelper.Unavailable)
			{
				float? fromNvmlClock = NvmlHelper.GetGpuClockMhz();
				if (fromNvmlClock.HasValue && fromNvmlClock.Value > 0f)
				{
					gpuCoreFreq = (int)Math.Round(fromNvmlClock.Value);
					gpuClockFilled = true;
				}
			}
			if (!gpuClockFilled && hw.GpuCoreFreq > 0)
			{
				// ⭐ 最后兜底：厂商标本 ✅（⭐ 遵照"尽量不依赖各家接口"⇒ ⭐ 放最后 ✅）
				gpuCoreFreq = hw.GpuCoreFreq;
			}
		}
		catch
		{
		}
		// ── ⑤ CPU 频率：⭐ LHM 优先（⭐ 本方法上方已有同源采样 ✅）⇒ ⭐ 读不到才用厂商 ✅ ──
		try
		{
			float? lhmCpuClock = lhmMonitor.CpuClockMhz;
			if (lhmCpuClock.HasValue && lhmCpuClock.Value > 0f)
			{
				cpuFrequency = (int)Math.Round(lhmCpuClock.Value);
			}
			else if (hw.CpuFrequency > 0)
			{
				cpuFrequency = hw.CpuFrequency;
			}
		}
		catch
		{
		}
	}

	public static void AttachMechrevoHw(MechrevoHw hw)
	{		hw.DataChanged += () =>
		{
			cpuTemp = hw.CpuTemp;
			cpuUsage = hw.CpuUsage;
			cpuFan = hw.CpuFanDuty;
			cpuFanRPM = hw.CpuFanRpm;
			midFan = null;
			// 2026-10-11：CPU 频率不再用 GCU/EC 上报（AMD 上不准、不实时），改由本地
			// 性能计数器采样（% Processor Performance × MaxClockSpeed，任务管理器同源），
			// 见 RefreshLocalMonitoringCore 顶部的 cpu-freq-sample 步骤。
			gpuTemp = hw.GpuTemp;
			gpuUsage = hw.GpuUsage;
			gpuFan = hw.GpuFanDuty;
			gpuFanRPM = hw.GpuFanRpm;
			gpuCoreFreq = hw.GpuCoreFreq;
			ramUsage = hw.RamUsage;
			ramUsedMb = (int)(hw.RamUsedGb * 1024.0);
			batteryCapacity = hw.BatteryPercent;
			batteryCharge = hw.BatteryPercent;
		};
	}

	/// <summary>
	/// (a) 异常防护：所有可能进入原生层（LHM / NVML / EC / WMI）的采样调用都从这里走。
	/// 捕获后只记日志、跳过本次采样，绝不冒泡到 UI 线程（自定义窗的 Timer 回调跑在 WinForms 消息循环里）。
	/// 注意：.NET 上原生 AV 多数情况下不可捕获（进程直接终止），所以本层之外还有 (b) 句柄自愈、
	/// (c) 降级、以及 NativeDriverGuard 的模块存活守卫——几层合起来才是「采样不再带走进程」的完整防线。
	/// </summary>
	internal static bool TryStep(Action step, string key)
	{
		return TryStep(step, key, countsAsHealthFailure: true);
	}

	/// <summary>
	/// <paramref name="countsAsHealthFailure"/> = false 用于「供电/电池」这类与 LHM/NVML 无关的刷新：
	/// 它们失败照样只记日志，但不计入 <c>_samplingFailures</c>，避免把无关的 WMI/EC 抖动误判成
	/// 「本地功耗采样链坏了」而触发重建/降级。
	/// </summary>
	internal static bool TryStep(Action step, string key, bool countsAsHealthFailure)
	{
		try
		{
			step();
			return true;
		}
		catch (SEHException ex)
		{
			LogStepFailure(key, ex, countsAsHealthFailure);
			return false;
		}
		catch (AccessViolationException ex)
		{
			LogStepFailure(key, ex, countsAsHealthFailure);
			return false;
		}
		catch (Exception ex)
		{
			LogStepFailure(key, ex, countsAsHealthFailure);
			return false;
		}
	}

	private static void LogStepFailure(string key, Exception ex)
	{
		LogStepFailure(key, ex, countsAsHealthFailure: true);
	}

	private static void LogStepFailure(string key, Exception ex, bool countsAsHealthFailure)
	{
		if (countsAsHealthFailure)
		{
			Interlocked.Increment(ref _samplingFailures);
		}
		Logger.WriteLineThrottled("local-sample-" + key, $"本地采样步骤失败（已跳过本次采样，不影响 UI 线程）：{key} {ex.GetType().Name} {ex.Message}", 30000);
	}

	private static void RefreshLocalMonitoring()
	{
		lock (lhmLock)
		{
			// (a) 本方法整体再兜一层：任何漏出的异常都不许冒泡到 UI 线程（只记日志、跳过本次采样）。
			// 探针实测过这一层的必要性：`cpuPowerSampler.Sample()`（性能计数器 / MSR 回退）在计数器
			// 缺失、被删除、权限不足等情况下会抛 InvalidOperationException / FileNotFoundException，
			// 原来它不在 TryStep 里，会直接穿出本方法。
			try
			{
				RefreshLocalMonitoringCore();
			}
			catch (SEHException ex)
			{
				LogStepFailure("refresh-local", ex);
			}
			catch (AccessViolationException ex)
			{
				LogStepFailure("refresh-local", ex);
			}
			catch (Exception ex)
			{
				LogStepFailure("refresh-local", ex);
			}
		}
	}

	/// <summary>
	/// 采样主干（由 <see cref="RefreshLocalMonitoring"/> 在同一次持锁内调用）：
	/// 每一步各自兜底（LHM 更新 / LHM 读值 / NVML 采样 / 性能计数器回退），任何一步失败只跳过本次采样。
	/// </summary>
	private static void RefreshLocalMonitoringCore()
	{
		// 2026-10-11：CPU 实时频率采样（性能计数器，任务管理器同源）。与 LHM/NVML 无关，
		// 放在隔离检查之前：即使本地功耗采样链降级停用，频率依然实时刷新。悬浮窗开启时
		// 由 Overlay 的 1000ms Timer 驱动（每次 Tick 都经 ReadSensorsOverlay → SampleLocalPower）。
		TryStep(delegate
		{
			int? perfFreq = cpuFreqSampler?.Sample();
			if (perfFreq.HasValue && perfFreq.Value > 0)
			{
				cpuFrequency = perfFreq.Value;
			}
		}, "cpu-freq-sample");
		// (c) 降级中：一次原生调用都不发；(d) 未启用/已释放：直接早退。
		if (_localSamplingQuarantined)
		{
			return;
		}
		LhmMonitor? lhmMonitor = lhm;
		if (lhmMonitor == null || lhmMonitor.IsDisposed)
		{
			return;
		}
		if (!TryStep(lhmMonitor.Update, "lhm-update"))
		{
			return;
		}
		float? num5 = null;
		float? num = null;
		float? num2 = null;
		int num6 = 0;
		int num7 = 0;
		if (!TryStep(delegate
		{
			num5 = lhmMonitor.CpuClockMhz;
			num = lhmMonitor.CpuPower;
			num2 = lhmMonitor.GpuPower;
			num6 = lhmMonitor.GpuMemoryUsedMb;
			num7 = lhmMonitor.GpuMemoryTotalMb;
		}, "lhm-read"))
		{
			return;
		}
		// ⭐⭐ 2026-10-16（0.291.9 · 宝龙达适配 · "接传感器"）：⭐ 非 GCU 后端的传感器补值 ✅
		//   ⚠️ GCU 机型的 cpuTemp/gpuTemp/风扇是 `DataChanged`（MQTT 推送）填的；
		//      ⭐ 宝龙达**没有 GCU 推送** ⇒ ⭐ 必须由这条"3 秒本地采样"路径补上 ✅
		//   ⭐⭐ 温度优先级（⭐ 用户 2026-10-16 原话）：
		//      「⭐ 接 传感器如果 LM 自带的那个 dll 能读温度**优先用 LM 的传感器**，⭐ 不能的再接」
		//      ⇒ ⭐ **先试 LHM**（⭐ 我们自带的 LibreHardwareMonitor ✅）⇒ ⭐ 读不到才用宝龙达功能号 22 ✅
		ApplySensorPriority(lhmMonitor);
		if (num5.HasValue && num5.Value > 0f)
		{
			cpuFrequency = (int)num5.Value;
		}
		float? gpuPowerViaNvml = null;
		(long, long)? memoryViaNvml = null;
		if (!NvmlHelper.Unavailable)
		{
			// (c) NVML 已隔离/模块失效时跳过；正常时 (b) 句柄每次现取、(e) 结果 TTL 复用。
			TryStep(delegate
			{
				gpuPowerViaNvml = NvmlHelper.GetGpuPower();
				memoryViaNvml = NvmlHelper.GetMemoryInfo();
			}, "nvml-sample");
		}
		// (a) 性能计数器回退也单独兜底：它同样会抛（计数器缺失/被删/权限不足），不能让它穿出去。
		float? fallbackCpuPower = null;
		if (!IsUsablePower(num))
		{
			TryStep(delegate
			{
				fallbackCpuPower = cpuPowerSampler?.Sample();
			}, "cpu-perfcounter-sample");
		}
		cpuPower = ResolveCpuPower(num, fallbackCpuPower);
		gpuPower = ResolveGpuPower(num2, IsUsablePower(num2) ? ((float?)null) : gpuPowerViaNvml);
		(long, long)? tuple = ((num6 > 0 && num7 > 0) ? ((long, long)?)null : memoryViaNvml);
		long num3 = ((num6 > 0) ? num6 : (tuple?.Item1 ?? 0));
		long num4 = ((num7 > 0) ? num7 : (tuple?.Item2 ?? 0));
		vramUsedMb = ((num3 > 0) ? new int?((int)Math.Min(num3, 2147483647L)) : ((int?)null));
		vramUsage = ((num3 > 0 && num4 > 0) ? new int?((int)Math.Clamp(num3 * 100 / num4, 0L, 100L)) : ((int?)null));
	}

	/// <summary>
	/// (b) 句柄自愈 + (c) 降级不致命。
	/// 判据：采样步骤抛异常（计数见 <see cref="LogStepFailure"/>）、LHM 传感器更新连续失败、
	/// 或 NVML 查询连续返回错误码；达到阈值即按退避重建（NVML 重置 → LHM 监控对象 Dispose 后后台重建），
	/// 重建次数用尽仍失败则进入隔离降级：完全停掉本地/GPU 采样（UI 显示不可用），到期自动重试一次。
	/// 「连续拿不到值」只在底层明确报错（错误码/异常）时才算失败——GPU 被 mux 关闭或低功耗时拿不到瓦数属于正常状态，
	/// 不做无意义的重建（否则会不停重建，反而放大触发面）。
	/// </summary>
	private static void MaintainSamplingHealth()
	{
		LhmMonitor? lhmMonitor = lhm;
		bool flag = NvmlHelper.ConsecutiveFailures >= NvmlHelper.FailureThreshold || NvmlHelper.ModuleGone;
		bool flag2 = lhmMonitor != null && lhmMonitor.ConsecutiveFailures >= LhmMonitor.FailureThreshold;
		bool flag3 = Interlocked.CompareExchange(ref _samplingFailures, 0, 0) >= SamplingFailureThreshold;
		if (!flag && !flag2 && !flag3)
		{
			Interlocked.Exchange(ref _samplingFailures, 0);
			_rebuildAttempts = 0;
			_nextRebuildAtTicks = 0L;
			return;
		}
		string text = (flag3 ? "采样步骤连续异常" : (flag2 ? "LHM 传感器更新连续失败" : "NVML 查询连续失败"));
		if (_rebuildAttempts >= MaxRebuildAttempts)
		{
			EnterQuarantine(text);
			return;
		}
		long tickCount = Environment.TickCount64;
		if (_nextRebuildAtTicks != 0 && tickCount < _nextRebuildAtTicks)
		{
			return;
		}
		long num = RebuildCooldownMs(_rebuildAttempts);
		_rebuildAttempts++;
		_nextRebuildAtTicks = tickCount + num;
		Logger.WriteLine($"本地功耗采样硬失败（{text}）：第 {_rebuildAttempts}/{MaxRebuildAttempts} 次重建（NVML 重置 + LHM 监控对象重建），冷却 {num / 1000}s");
		Interlocked.Exchange(ref _samplingFailures, 0);
		NvmlHelper.Reset();
		if (flag2)
		{
			ScheduleLhmRebuild();
		}
	}

	internal static long RebuildCooldownMs(int attempt)
	{
		return Math.Min(5000L << Math.Clamp(attempt, 0, 4), 60000L);
	}

	/// <summary>(b) Dispose 旧的监控对象 → 后台延迟重建 → 重新枚举；重建期间采样返回 null（不显示假数字）。</summary>
	private static void ScheduleLhmRebuild()
	{
		if (Interlocked.CompareExchange(ref _lhmRebuildInFlight, 1, 0) != 0)
		{
			return;
		}
		Task.Run(delegate
		{
			try
			{
				LhmMonitor lhmMonitor = null;
				try
				{
					lhmMonitor = new LhmMonitor();
				}
				catch (SEHException ex)
				{
					Logger.WriteLineThrottled("lhm-rebuild", "LHM 重建原生异常：" + ex.GetType().Name + " " + ex.Message, 60000);
				}
				catch (AccessViolationException ex)
				{
					Logger.WriteLineThrottled("lhm-rebuild", "LHM 重建原生异常：" + ex.GetType().Name + " " + ex.Message, 60000);
				}
				catch (Exception ex)
				{
					Logger.WriteLineThrottled("lhm-rebuild", "LHM 重建失败：" + ex.GetType().Name + " " + ex.Message, 60000);
				}
				LhmMonitor lhmMonitor2;
				lock (lhmLock)
				{
					lhmMonitor2 = lhm;
					lhm = lhmMonitor;
				}
				lhmMonitor2?.Dispose();
				if (lhmMonitor == null)
				{
					Logger.WriteLineThrottled("lhm-rebuild", "LHM 重建未成功，本地功耗采样继续降级（下一轮再试）", 60000);
				}
			}
			finally
			{
				Interlocked.Exchange(ref _lhmRebuildInFlight, 0);
			}
		});
	}

	private static void EnterQuarantine(string reason)
	{
		_localSamplingQuarantined = true;
		_quarantineReason = reason;
		_rebuildAttempts = 0;
		_nextRebuildAtTicks = 0L;
		_quarantineUntilTicks = Environment.TickCount64 + _quarantineMs;
		// 不显示假数字：立即清掉上一次的本地功耗/显存读数（UI 会显示「不可用」）。
		cpuPower = null;
		gpuPower = null;
		vramUsedMb = null;
		vramUsage = null;
		Logger.WriteLine($"本地功耗采样已降级停用（{reason}）：{_quarantineMs / 1000}s 内不再触碰 LHM/NVML，功耗墙判定显示为不可用；到期自动重试恢复");
		_quarantineMs = Math.Min(_quarantineMs * 2, QuarantineMaxMs);
	}

	/// <summary>(c) 降级期结束 → 自动重试恢复（重置 NVML + 后台重建 LHM），下次采样重新走原生链。</summary>
	private static void MaybeRetryQuarantinedSampling()
	{
		if (!_localSamplingQuarantined)
		{
			return;
		}
		if (_quarantineUntilTicks != 0L && Environment.TickCount64 < _quarantineUntilTicks)
		{
			return;
		}
		_localSamplingQuarantined = false;
		_quarantineUntilTicks = 0L;
		_rebuildAttempts = 0;
		Interlocked.Exchange(ref _samplingFailures, 0);
		NvmlHelper.Reset();
		ScheduleLhmRebuild();
		Logger.WriteLine("本地功耗采样降级期结束：尝试恢复（NVML 重置 + LHM 监控对象重建）");
	}

	/// <summary>供设置界面/自动化调用的手动恢复入口。</summary>
	public static void ResetLocalSampling()
	{
		_localSamplingQuarantined = false;
		_quarantineUntilTicks = 0L;
		_quarantineMs = QuarantineBaseMs;
		_rebuildAttempts = 0;
		_nextRebuildAtTicks = 0L;
		Interlocked.Exchange(ref _samplingFailures, 0);
		NvmlHelper.Reset();
		Logger.WriteLine("本地功耗采样：手动重置（恢复尝试）");
	}

	/// <summary>(c) 降级中：UI（功耗墙判定）必须显示「不可用」，不显示任何旧瓦数。</summary>
	internal static bool LocalPowerSamplingUnavailable => _localSamplingQuarantined;

	internal static string LocalPowerSamplingStateText => (_localSamplingQuarantined ? ("本地功耗采样不可用（" + _quarantineReason + "）") : "");

	private static void ObservePowerWall()
	{
		float? num = cpuPower;
		int num2 = Program.hw?.Pl1 ?? (-1);
		if (num.HasValue && num2 > 0)
		{
			powerWall.Observe(num.Value, num2, DateTime.Now);
		}
	}

	internal static float? ResolveCpuPower(float? localPower, float? fallbackPower)
	{
		return SelectUsablePower(localPower, fallbackPower);
	}

	internal static float? ResolveGpuPower(float? localPower, float? fallbackPower)
	{
		return SelectUsablePower(localPower, fallbackPower);
	}

	private static float? SelectUsablePower(float? localPower, float? fallbackPower)
	{
		if (!IsUsablePower(localPower))
		{
			if (!IsUsablePower(fallbackPower))
			{
				return null;
			}
			return fallbackPower;
		}
		return localPower;
	}

	private static bool IsUsablePower(float? power)
	{
		if (power.HasValue && power.GetValueOrDefault() > 0f)
		{
			return float.IsFinite(power.Value);
		}
		return false;
	}

	public static void ReadSensors()
	{
		RefreshPowerInput();
		RefreshBatteryRate();
	}

	public static void ReadSensorsOverlay()
	{
		SampleLocalPower();
	}

	internal static void SampleLocalPower()
	{
		// (a) 异常防护：整条采样链在这里兜底（SampleLocalPower 会被 UI 线程的 2s Timer 直接调用，
		// 任何漏出来的异常都会穿过消息循环把进程带走）。
		try
		{
			RefreshLocalMonitoring();
			// 供电/电池刷新失败不影响「本地功耗采样链」的健康判定（见 TryStep 的 countsAsHealthFailure）。
			TryStep(ObservePowerWall, "powerwall", countsAsHealthFailure: false);
			TryStep(() => RefreshPowerInput(), "power-input", countsAsHealthFailure: false);
			TryStep(() => RefreshBatteryRate(), "battery-rate", countsAsHealthFailure: false);
			if (_localSamplingQuarantined)
			{
				// (c) 降级中：只做上面的轻量刷新，并检查是否到了自动重试的时间点。
				MaybeRetryQuarantinedSampling();
				return;
			}
			MaintainSamplingHealth();
		}
		catch (SEHException ex)
		{
			LogStepFailure("sample-local", ex);
		}
		catch (AccessViolationException ex)
		{
			LogStepFailure("sample-local", ex);
		}
		catch (Exception ex)
		{
			LogStepFailure("sample-local", ex);
		}
	}

	public static int GetBatteryChargePercentage()
	{
		return batteryCharge;
	}

	public static int GetCPUTemp()
	{
		return cpuTemp;
	}

	public static void KillGPUApps()
	{
	}

	public static void ResetCPUPowerCounter()
	{
		cpuPowerSampler?.Reset();
	}

	public static void DisposeGpuControl()
	{
	}

	public static void RecreateGpuControl()
	{
	}
}
