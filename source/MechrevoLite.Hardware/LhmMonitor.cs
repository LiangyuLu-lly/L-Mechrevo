using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

public class LhmMonitor : IDisposable
{
	private readonly Computer _computer;

	private readonly object _updateLock = new object();

	private long _lastUpdateTicks;

	private long _lastOpenAttemptTicks;

	private bool _dumpedSensors;

	internal const int MaxReopenAttempts = 5;

	private const int ReopenBaseDelayMs = 3000;

	private int _reopenAttempts;

	private bool _reopenGaveUp;

	/// <summary>(e) 2026-10-09：NVIDIA 硬件的原生化更新单独降频（LHM 的 NVML/NVAPI 调用是本次崩溃点所在）。</summary>
	internal const int NvidiaUpdateIntervalMs = 2000;

	/// <summary>(b) 连续失败到该阈值，调用方（HardwareControl）应重建监控对象。</summary>
	internal const int FailureThreshold = 3;

	private volatile bool _disposed;

	private int _consecutiveFailures;

	private long _lastNvidiaUpdateTicks;

	private bool _gpuNativeStale;

	private bool _updateHadFailure;

	private readonly bool _nvidiaGpuSkipped;

	public float? CpuPower { get; private set; }

	public float? CpuClockMhz { get; private set; }

	public float? GpuPower { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达适配 · "接传感器"）：⭐ **CPU 温度（摄氏度）** —— 取自 LM 自带的
	/// LibreHardwareMonitor ✅。
	/// <para>⭐ 这是"LM 自带传感器"这一侧的**温度来源**，⭐ 按用户 2026-10-16 的要求
	/// **优先于宝龙达 WMI 的功能号 22** 使用 ✅。</para>
	/// <para>⚠️ 取不到时为 <c>null</c>（⭐ 例如 LHM 驱动没起来 / ⭐ 非管理员 / ⭐ 传感器名不匹配 ✅）
	/// ⇒ ⭐ 调用方据此**回退到宝龙达 WMI** ✅。</para>
	/// </summary>
	public float? CpuTemperature { get; private set; }

	/// <summary>⭐ 实际选中的 CPU 温度传感器名（⭐ 诊断用：⭐ 让我们知道到底读到的是哪一个 ✅）。</summary>
	public string CpuTemperatureSensorName { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-16（"接传感器"）：⭐ **GPU 温度（摄氏度）** —— 同样来自 LM 自带的 LHM ✅。
	/// <para>⚠️⚠️ <b>生产上多半是 <c>null</c></b>：⭐ 因为本类一旦检测到 NVIDIA 就
	/// <c>IsGpuEnabled = false</c>（⭐ 历史上的 NVML 原生崩溃修复 ⚠️）
	/// ⇒ ⭐ **NVIDIA 机型拿不到** ✅；⭐ **AMD/Intel 独显或无独显机型可以拿到** ✅。</para>
	/// <para>⭐ 这正是"LHM 能读就用 LHM、⭐ 读不到才用厂商接口/⭐ NVML"⭐ 这条通用规则里的"读不到"情形 ✅
	/// ⇒ ⭐ 调用方（<c>HardwareControl.ApplySensorPriority</c>）会自动回退 ✅。</para>
	/// </summary>
	public float? GpuTemperature { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-16（"接传感器"）：⭐ **GPU 核心频率（MHz）** —— 同样来自 LM 自带的 LHM ✅。
	/// <para>⭐ 依据用户 2026-10-16：「⭐ 显卡 / cpu 频率温度 LHM 应该都能读」✅</para>
	/// <para>⚠️ 与 <see cref="GpuTemperature" /> 同一个限制：⭐ NVIDIA 机型下 LHM 的 GPU 那支被关
	/// ⇒ ⭐ 那些机型由加固过的 <c>NvmlHelper</c> 提供 ✅（⭐ 见 <c>HardwareControl.ApplySensorPriority</c> ✅）</para>
	/// </summary>
	public float? GpuClockMhz { get; private set; }

	/// <summary>
	/// ⭐ 判断某个 GPU 温度传感器名是不是我们要的（⭐ 取"核心"那一个 ✅）。
	/// <para>⭐ 实测（⭐ `--sensor-dump`）：⭐ NVIDIA = ⭐ <c>GPU Core</c> ✅；
	/// ⭐ AMD 核显 = ⭐ <c>GPU VR SoC</c> ✅。</para>
	/// <para>⚠️ **刻意不取** <c>Hot Spot</c> / ⭐ <c>Memory Junction</c>（⭐ 那是热点与显存结温，
	/// ⭐ 比核心高十几度 ⚠️）⇒ ⭐ 界面要的是**核心温度**，⭐ 混用会让人以为过热 ✅。</para>
	/// </summary>
	internal static bool IsGpuTemperatureSensorName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		if (name.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Junction", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		return name.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("GPU VR SoC", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>
	/// ⭐ 判断某个 CPU 温度传感器名是不是我们要的。
	/// <para>⭐ 依据实测（⭐ `--sensor-dump`）：⭐ AMD 上是 ⭐ <c>Core (Tctl/Tdie)</c> ✅；
	/// ⭐ Intel 上通常是 ⭐ <c>CPU Package</c> / ⭐ <c>Core Average</c> / ⭐ <c>Core Max</c> ✅。</para>
	/// <para>⚠️ 刻意**不取** <c>Distance to TjMax</c>（⭐ 那是"距离上限还有多少度"，⭐ 语义相反 ❌）；
	/// ⭐ 也**不取**各种 <c>Core #N</c> 单核值（⭐ 想优先要"封装/最高"那种整体值 ✅）。</para>
	/// </summary>
	internal static bool IsCpuTemperatureSensorName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		// ⚠️ 先排除语义相反的
		if (name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("TjMax", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		return name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Core Average", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Core Max", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public int GpuMemoryTotalMb { get; private set; }

	public int GpuMemoryUsedMb { get; private set; }

	/// <summary>(b) 连续失败次数：调用方据此决定重建监控对象。</summary>
	internal int ConsecutiveFailures => Volatile.Read(in _consecutiveFailures);

	/// <summary>LHM 已放弃（枚举不到传感器/驱动需要管理员）：本地功耗采样不可用，但无崩溃风险。</summary>
	internal bool GaveUp => _reopenGaveUp;

	/// <summary>(b) NVIDIA 原生路径已判定失效（模块卸载/换址），已跳过其 Update()。</summary>
	internal bool GpuNativeStale => _gpuNativeStale;

	internal bool IsDisposed => _disposed;

	public LhmMonitor()
	{
		// 2026-10-09（NVML 原生崩溃修复）：检测到 NVIDIA 独显时**不启用** LHM 的 GPU 监控。
		// 事故栈顶正是 LibreHardwareMonitor.Interop.NvidiaML.NvmlDeviceGetPowerUsage ← Gpu.NvidiaGpu.Update()：
		// LHM 内部把 NVML 设备句柄/导出地址长期缓存，NVML 上下文或 nvml.dll 映射失效后调用它会踩到未映射内存，
		// 在 CoreCLR 的 P/Invoke 桩里触发 0xc0000005（出错模块 coreclr.dll），这类 AV 不可捕获、直接带走进程。
		// GPU 功耗/显存改由加固后的 NvmlHelper 提供（每次现取句柄 + 模块存活守卫 + 失败降级），数据来源同样是 NVML，
		// 功能不损失；AMD/Intel 机器不受影响（LHM 走 ADL/自有路径，根本不涉及 NVML）。
		_nvidiaGpuSkipped = DetectNvidiaPresentSafe();
		_computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = !_nvidiaGpuSkipped,
			IsMemoryEnabled = true
		};
		try
		{
			_computer.Open();
			Interlocked.Exchange(ref _lastOpenAttemptTicks, Environment.TickCount64);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("LHM open fail: " + ex.Message);
		}
		// 记录当前已加载的原生驱动模块基址（未加载则跳过记录）：之后一旦发现基址变化，
		// 就说明缓存的导出地址/句柄已失效，必须停用对应原生路径（见 IsNvidiaNativeStale）。
		RememberNativeModule("nvml.dll");
		RememberNativeModule("nvapi64.dll");
		if (_nvidiaGpuSkipped)
		{
			Logger.WriteLine("LHM: 检测到 NVIDIA 独显，未启用 LHM 的 GPU 监控（避开 LHM/NVML 缓存句柄的原生崩溃路径），GPU 功耗/显存改用加固后的 NvmlHelper");
		}
	}

	/// <summary>
	/// 只读注册表判断是否存在 NVIDIA 独显（不触碰任何原生驱动）。
	/// 探测不可用时取「按有 NVIDIA 处理」的保守分支：宁可少一路传感器，也不冒原生崩溃风险。
	/// </summary>
	internal static bool DetectNvidiaPresentSafe()
	{
		try
		{
			IReadOnlyList<GpuAdapter> readOnlyList = GpuAdapterReader.ReadFromRegistry(out bool enumerationAvailable);
			if (!enumerationAvailable)
			{
				Logger.WriteLineThrottled("lhm-nvidia-detect", "GPU 供应商探测不可用，保守按「存在 NVIDIA」处理（不启用 LHM GPU 监控）", 60000);
				return true;
			}
			foreach (GpuAdapter item in readOnlyList)
			{
				if (item.IsNvidia)
				{
					return true;
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("lhm-nvidia-detect", "GPU 供应商探测异常，保守按「存在 NVIDIA」处理：" + ex.Message, 60000);
			return true;
		}
	}

	public void Update()
	{
		// (d) 生命周期：Dispose() 之后绝不再用已关闭的 Computer 采样（残留定时器 tick 直接早退）。
		if (_disposed)
		{
			return;
		}
		long tickCount = Environment.TickCount64;
		if (tickCount - Interlocked.Read(in _lastUpdateTicks) < 1000)
		{
			return;
		}
		lock (_updateLock)
		{
			tickCount = Environment.TickCount64;
			if (_disposed || tickCount - _lastUpdateTicks < 1000)
			{
				return;
			}
			_lastUpdateTicks = tickCount;
			try
			{
				_updateHadFailure = false;
				CpuPower = null;
				CpuClockMhz = null;
				GpuPower = null;
				GpuMemoryTotalMb = 0;
				GpuMemoryUsedMb = 0;
				bool flag = false;
				bool flag2 = false;
				float? nvidiaPower = null;
				float? amdPower = null;
				float? integratedPower = null;
				if (_computer.Hardware.Count == 0)
				{
					TryReopen();
				}
				foreach (IHardware item in _computer.Hardware)
				{
					flag |= item.HardwareType == HardwareType.GpuNvidia;
					flag2 |= item.HardwareType == HardwareType.GpuAmd;
					if (item.HardwareType == HardwareType.GpuNvidia)
					{
						// (b) NVIDIA 原生路径守卫：nvml.dll/nvapi64.dll 被卸载或换址后，LHM 缓存的句柄/函数地址已失效，
						// 再调一次就会踩未映射内存（不可捕获的 0xc0000005）。这里直接跳过该硬件的 Update()。
						if (IsNvidiaNativeStale())
						{
							_gpuNativeStale = true;
							continue;
						}
						// (e) 降频：NVIDIA 原生化更新单独限频，降低触发面（其余硬件仍 1s）。
						if (tickCount - Interlocked.Read(in _lastNvidiaUpdateTicks) < NvidiaUpdateIntervalMs)
						{
							continue;
						}
						Interlocked.Exchange(ref _lastNvidiaUpdateTicks, tickCount);
					}
					if (!TryUpdateHardware(item))
					{
						continue;
					}
					ISensor[] sensors = item.Sensors;
					foreach (ISensor sensor in sensors)
					{
						if (!sensor.Value.HasValue)
						{
							continue;
						}
						if (sensor.SensorType == SensorType.Power)
						{
							if (item.HardwareType == HardwareType.Cpu && IsCpuPowerSensorName(sensor.Name))
							{
								CpuPower = sensor.Value;
							}
							else if (IsGpuHardwareType(item.HardwareType) && IsGpuPowerSensorName(sensor.Name))
							{
								switch (item.HardwareType)
								{
								case HardwareType.GpuNvidia:
									nvidiaPower = sensor.Value;
									break;
								case HardwareType.GpuAmd:
									amdPower = sensor.Value;
									break;
								default:
									integratedPower = sensor.Value;
									break;
								}
							}
						}
						else if (sensor.SensorType == SensorType.Clock && item.HardwareType == HardwareType.Cpu && !CpuClockMhz.HasValue && IsCpuClockSensorName(sensor.Name))
						{
							float num5 = sensor.Value.Value;
							CpuClockMhz = num5 * ((num5 < 1000f) ? 1000f : 1f);
						}
						// ⭐⭐ 2026-10-16（0.291.9 · 宝龙达适配 · "接传感器"）：⭐ **新增温度采集** ✅
						//   ⭐ 起因（⭐ 用户原话）：「⭐ 接 传感器如果 LM 自带的那个 dll 能读温度
						//      **优先用 LM 的传感器**，⭐ 不能的再接」✅
						//   ⭐ 实测（⭐ `--sensor-dump` · ⭐ 本机 翼龙15Pro）：⭐ LHM 能读 **5 个温度** ✅
						//      （⭐ GPU Core 78 / ⭐ Hot Spot 91 / ⭐ 显存 80 / ⭐ iGPU 72 / ⭐ CPU Tctl/Tdie ✅）
						//   ⚠️ 但生产里 `_nvidiaGpuSkipped` 会**关掉 GPU 那支**（⭐ 见构造函数注释：
						//      ⭐ NVML 原生崩溃修复 ⚠️）⇒ ⭐ 生产上 **GPU 温度不来自 LHM** ⚠️，
						//      ⭐ 能稳定拿到的是 ⭐ **CPU 温度** ✅
						//   ⇒ ⭐ 所以这里**只补 CPU 温度**（⭐ 优先取到一个就固定用那个传感器的名字 ✅）；
						//      ⭐ GPU 温度另有来源（⭐ NVML / ⭐ 宝龙达侧后续 ✅）
						else if (sensor.SensorType == SensorType.Temperature && item.HardwareType == HardwareType.Cpu
							&& !CpuTemperature.HasValue && IsCpuTemperatureSensorName(sensor.Name))
						{
							CpuTemperature = sensor.Value;
							CpuTemperatureSensorName = sensor.Name;
						}
						// ⭐⭐ 2026-10-16：⭐ GPU 温度（⭐ 只在 LHM 的 GPU 那支启用时才可能命中 ✅）
						else if (sensor.SensorType == SensorType.Temperature && IsGpuHardwareType(item.HardwareType)
							&& !GpuTemperature.HasValue && IsGpuTemperatureSensorName(sensor.Name))
						{
							GpuTemperature = sensor.Value;
						}
						// ⭐⭐ 2026-10-16：⭐ GPU 核心频率（⭐ 同上，⭐ 只在 GPU 那支启用时 ✅）
						else if (sensor.SensorType == SensorType.Clock && IsGpuHardwareType(item.HardwareType)
							&& !GpuClockMhz.HasValue && sensor.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							GpuClockMhz = sensor.Value;
						}
						else if (sensor.SensorType == SensorType.SmallData && IsGpuMemoryTotalSensorName(sensor.Name))
						{
							GpuMemoryTotalMb = (int)sensor.Value.Value;
						}
						else if (sensor.SensorType == SensorType.SmallData && IsGpuMemoryUsedSensorName(sensor.Name))
						{
							GpuMemoryUsedMb = (int)sensor.Value.Value;
						}
					}
				}
				GpuPower = SelectGpuPower(flag, nvidiaPower, flag2, amdPower, integratedPower);
				if (!_updateHadFailure && Interlocked.Exchange(ref _consecutiveFailures, 0) != 0)
				{
					Logger.WriteLine("LHM 传感器更新已恢复（连续失败计数清零）");
				}
				if (_dumpedSensors || _computer.Hardware.Count <= 0)
				{
					return;
				}
				_dumpedSensors = true;
				try
				{
					IEnumerable<string> contents = _computer.Hardware.SelectMany((IHardware h) => h.Sensors.Select((ISensor s) => $"{h.HardwareType}/{s.SensorType}/{s.Name}")).Distinct();
					File.WriteAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite", "lhm_sensors.txt"), contents);
				}
				catch (Exception ex)
				{
					Logger.WriteLineThrottled("lhm-dump", "LHM sensor dump failed: " + ex.Message);
				}
			}
			catch (SEHException ex2)
			{
				NoteUpdateFailure("LHM 传感器更新原生异常(SEH)", ex2);
			}
			catch (AccessViolationException ex2)
			{
				NoteUpdateFailure("LHM 传感器更新原生异常(AV)", ex2);
			}
			catch (Exception ex2)
			{
				NoteUpdateFailure("LHM 传感器更新失败", ex2);
			}
		}
	}

	/// <summary>(a) 单个硬件的原生化更新单独兜底：一路硬件坏掉不拖垮整轮采样，并把失败计入自愈计数。</summary>
	private bool TryUpdateHardware(IHardware hardware)
	{
		try
		{
			hardware.Update();
			return true;
		}
		catch (SEHException ex)
		{
			_updateHadFailure = true;
			NoteUpdateFailure($"{hardware.HardwareType} 更新原生异常(SEH)", ex);
			return false;
		}
		catch (AccessViolationException ex)
		{
			_updateHadFailure = true;
			NoteUpdateFailure($"{hardware.HardwareType} 更新原生异常(AV)", ex);
			return false;
		}
		catch (Exception ex)
		{
			_updateHadFailure = true;
			NoteUpdateFailure($"{hardware.HardwareType} 更新失败", ex);
			return false;
		}
	}

	private void NoteUpdateFailure(string what, Exception ex)
	{
		int num = Interlocked.Increment(ref _consecutiveFailures);
		Logger.WriteLineThrottled("lhm-update", $"{what}（{num}/{FailureThreshold}）：{ex.GetType().Name} {ex.Message}");
	}

	/// <summary>
	/// (b) NVIDIA 原生模块存活判定：nvml.dll / nvapi64.dll 被卸载或换址后，LHM 缓存的句柄与函数地址全部失效，
	/// 再调用就会踩未映射内存（不可捕获的原生 AV）。这里只查 kernel32，不进入驱动，安全。
	/// </summary>
	private bool IsNvidiaNativeStale()
	{
		if (!NativeDriverGuard.IsStale("nvml.dll", out nint _) && !NativeDriverGuard.IsStale("nvapi64.dll", out nint _))
		{
			return false;
		}
		if (!_gpuNativeStale)
		{
			Logger.WriteLine("LHM: nvml.dll/nvapi64.dll 已不在初始化时的基址（模块卸载或重载），已跳过 NVIDIA 硬件的原生化更新，避免不可捕获的原生崩溃");
		}
		return true;
	}

	private static void RememberNativeModule(string moduleName)
	{
		try
		{
			nint num = NativeDriverGuard.Query(moduleName);
			if (num != 0)
			{
				NativeDriverGuard.Remember(moduleName, num);
			}
		}
		catch
		{
		}
	}

	internal static bool IsGpuHardwareType(HardwareType type)
	{
		return type.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsCpuClockSensorName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.Trim();
		return text.StartsWith("Core", StringComparison.OrdinalIgnoreCase) || text.Contains("Core #", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsCpuPowerSensorName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.Trim();
		if (!text.Equals("Package", StringComparison.OrdinalIgnoreCase) && !text.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) && !text.Equals("Package Power", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("CPU Package Power", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	internal static float? SelectGpuPower(bool nvidiaPresent, float? nvidiaPower, bool amdPresent, float? amdPower, float? integratedPower)
	{
		if (!nvidiaPresent)
		{
			if (!amdPresent)
			{
				return integratedPower;
			}
			return amdPower;
		}
		return nvidiaPower;
	}

	internal static bool IsGpuPowerSensorName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.Trim();
		if (text.Contains("limit", StringComparison.OrdinalIgnoreCase) || text.Contains("capacity", StringComparison.OrdinalIgnoreCase) || text.Contains("maximum", StringComparison.OrdinalIgnoreCase) || text.Contains("minimum", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!text.Equals("GPU Package", StringComparison.OrdinalIgnoreCase) && !text.Equals("GPU Package Power", StringComparison.OrdinalIgnoreCase) && !text.Equals("GPU Power", StringComparison.OrdinalIgnoreCase) && !text.Equals("Total Board Power", StringComparison.OrdinalIgnoreCase) && !text.Equals("Board Power", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("GPU Power", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsGpuMemoryTotalSensorName(string? name)
	{
		if (name != null)
		{
			if (!name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
			{
				return name.Equals("Dedicated Memory Total", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private static bool IsGpuMemoryUsedSensorName(string? name)
	{
		if (name != null)
		{
			if (!name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
			{
				return name.Equals("Dedicated Memory Used", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	internal static int ReopenDelayMs(int attempt)
	{
		return 3000 * (1 << Math.Clamp(attempt, 0, 4));
	}

	internal static bool ShouldAttemptReopen(int attempts, bool gaveUp, long sinceLastAttemptMs)
	{
		if (!gaveUp && attempts < 5)
		{
			return sinceLastAttemptMs >= ReopenDelayMs(attempts);
		}
		return false;
	}

	private void TryReopen()
	{
		long tickCount = Environment.TickCount64;
		if (!ShouldAttemptReopen(_reopenAttempts, _reopenGaveUp, tickCount - Interlocked.Read(in _lastOpenAttemptTicks)))
		{
			return;
		}
		Interlocked.Exchange(ref _lastOpenAttemptTicks, tickCount);
		_reopenAttempts++;
		try
		{
			_computer.Open();
			if (_computer.Hardware.Count > 0)
			{
				_reopenAttempts = 0;
				Logger.WriteLine("LHM reopened after hardware sensors became available.");
			}
			else if (_reopenAttempts >= 5)
			{
				_reopenGaveUp = true;
				Logger.WriteLine($"LHM exposed no sensors after {5} attempts; local power monitoring disabled for this session.");
			}
		}
		catch (Exception ex)
		{
			if (_reopenAttempts >= 5)
			{
				_reopenGaveUp = true;
				Logger.WriteLine($"LHM open failed {5} times, giving up (driver registration usually needs administrator rights): {ex.Message}");
			}
			else
			{
				Logger.WriteLineThrottled("lhm-open", "LHM reopen failed: " + ex.Message);
			}
		}
	}

	public void Dispose()
	{
		// (d) 生命周期：先置 _disposed，再做 Close；此后任何残留的定时器 tick 都会在 Update() 入口早退，
		// 绝不会在监控对象已释放后继续采样。
		_disposed = true;
		lock (_updateLock)
		{
			try
			{
				_computer.Close();
			}
			catch
			{
			}
		}
	}
}
