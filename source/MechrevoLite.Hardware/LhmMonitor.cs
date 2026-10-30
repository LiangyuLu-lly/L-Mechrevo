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

	/// <summary>⭐ 2026-10-18：⭐ 被 1 s 限频 / 并发更新跳过（未真正 Update）的次数（⭐ 诊断用，见 <see cref="UpdateSkips" />）✅。</summary>
	private int _updateSkips;

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
	// AMD APU 核显功耗源/冻结判据（只读；NVIDIA 路径不变）
	private static int _gpuPowerStableCount;

	private static float _gpuPowerLastValue = float.NaN;

	private static int _gpuPowerChangeCount;

	private static bool _gpuPowerFrozen;
	// （机主：待机恒定属正常，不能只凭"6 次相同"判冻结）： 必须看到 load/temp 在变而功率不动才判冻结
	private static bool _gpuPowerMovementSeen;

	private static int? _gpuPowerLastLoad;

	private static float? _gpuPowerLastTemp;

	private static long _lastGpuPowerLogTick;

	private static float? _lastGpuPowerLoggedValue;

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
	/// ⭐⭐ 2026-10-18（机主：30 系 CPU 温度 80℃ 不动 · 诊断包 <c>lm-…-161007</c>）：
	/// ⭐ <b>CPU 温度来源档位</b>（⭐ 数值越小越优先 ✅）。
	/// <para>⭐ 优先级（⭐ 用户/官方口径）：<c>Tctl/Tdie</c>（AMD 真实结温）＞
	/// <c>CPU Package</c> / <c>Core Average</c> / <c>Core Max</c>（Intel 封装）＞ 其余（<c>Core #N</c> 等）✅。</para>
	/// <para>⚠️ 「ACPI 热区」不在本枚举里 —— ⭐ 它由 <c>HardwareControl</c> 插在 <see cref="Package" />
	/// 与 <see cref="Other" /> 之间参与取源 ✅。</para>
	/// </summary>
	internal enum CpuTemperatureTier
	{
		/// <summary>⭐ 本轮没读到任何 CPU 温度 ✅。</summary>
		None = 0,
		/// <summary>⭐⭐ 最优先：⭐ AMD 的 <c>Core (Tctl/Tdie)</c> / <c>CCD (Tdie)</c> —— **真实结温**（官方控制台口径）✅。</summary>
		TctlTdie = 1,
		/// <summary>⭐ 次优先：⭐ Intel 口径的 <c>CPU Package</c> / <c>Core Average</c> / <c>Core Max</c> ✅。</summary>
		Package = 2,
		/// <summary>⭐ 最后：⭐ 其余温度名（⭐ <c>Core #1</c> 等单核/其他口径 ⚠️ —— 通常比封装/结温低）。</summary>
		Other = 3,
		/// <summary>⛔ 不参与选源：⭐ <c>Distance to TjMax</c> 之类语义相反/无关的温度名 ⚠️。</summary>
		Excluded = 4
	}

	/// <summary>
	/// ⭐ 2026-10-18：⭐ <b>一个 CPU 温度传感器的快照</b>（⭐ 名称 / 值 / 档位 / 上次值变化时刻 ✅）。
	/// <para>⭐ 用途：⭐ ① 诊断包「CPU 温度来源明细」逐行列出（⭐ 值 + 更新时间 ✅）；
	/// ⭐ ② 「疑似异常」提示判据（⭐ 这个值多久没变过 ⚠️）。</para>
	/// </summary>
	internal sealed class CpuTemperatureSensorReading
	{
		internal string Name { get; init; } = "";

		internal float? Value { get; init; }

		internal CpuTemperatureTier Tier { get; init; }

		/// <summary>⭐ 这个传感器的值**上一次真正变化**的时刻（<see cref="Environment.TickCount64" />；0 = 未知）✅。</summary>
		internal long LastChangeTicks { get; init; }

		/// <summary>⭐ 距上次变化的毫秒数（⭐ -1 = 未知 / 还没变过 ✅）。</summary>
		internal long UnchangedMs
		{
			get
			{
				long tick = LastChangeTicks;
				if (tick == 0L)
				{
					return -1L;
				}
				return Environment.TickCount64 - tick;
			}
		}
	}

	/// <summary>⭐ 2026-10-18：⭐ 跨轮保存每个 CPU 温度传感器的「上次值 / 上次变化时刻」（⭐ 卡死与诊断判据用 ✅）。</summary>
	private sealed class CpuTemperatureSensorState
	{
		internal float Value;

		internal long LastChangeTicks;
	}

	/// <summary>⭐ 2026-10-18：⭐ 本轮所有 CPU 温度传感器快照（⭐ 每轮 <see cref="Update" /> 重建、整数组替换 ✅）。</summary>
	private CpuTemperatureSensorReading[] _cpuTemperatureSensors = Array.Empty<CpuTemperatureSensorReading>();

	/// <summary>⭐ 2026-10-18：⭐ 每个 CPU 温度传感器的跨轮状态（⭐ 判"上次变化时刻"✅）。</summary>
	private readonly Dictionary<string, CpuTemperatureSensorState> _cpuTemperatureState = new Dictionary<string, CpuTemperatureSensorState>(StringComparer.Ordinal);

	/// <summary>⭐ 2026-10-18：⭐ 上一次选中的 CPU 温度值 / 它开始保持不变的时刻（⭐ 只用于诊断"多久没变" ✅）。</summary>
	private float? _cpuTemperatureLastValue;

	private long _cpuTemperatureLastChangeTicks;

	/// <summary>⭐ 2026-10-18：⭐ 当前选中 CPU 温度传感器所属档位（⭐ 选源/诊断用 ✅）。</summary>
	internal CpuTemperatureTier CpuTemperatureSelectedTier { get; private set; }

	/// <summary>
	/// ⭐ 2026-10-18：⭐ <b>本机 LHM 暴露的全部 CPU 温度传感器快照</b> ✅
	/// （⭐ 诊断包「CPU 温度来源明细」逐行列出：⭐ 名称 / 值 / 档位 / 上次变化时刻 ✅）。
	/// <para>⚠️ 只读快照、整数组替换 ⇒ ⭐ 诊断线程读取时**不需要加锁**（⭐ 最差读到上一轮 ✅）。</para>
	/// </summary>
	internal IReadOnlyList<CpuTemperatureSensorReading> CpuTemperatureSensors => _cpuTemperatureSensors;

	/// <summary>⭐ 2026-10-18：⭐ 当前选中的 CPU 温度值**已经多久没变过**（毫秒；⭐ -1 = 还没选到 ✅）。</summary>
	internal long CpuTemperatureUnchangedMs
	{
		get
		{
			long tick = _cpuTemperatureLastChangeTicks;
			if (tick == 0L)
			{
				return -1L;
			}
			return Environment.TickCount64 - tick;
		}
	}

	/// <summary>⭐ 2026-10-18：⭐ LHM 枚举到的 CPU 硬件对象数（⭐ 0 ⇒ 温度必然读不到；⭐ 诊断用 ✅）。</summary>
	internal int CpuHardwareCount { get; private set; }

	/// <summary>⭐ 2026-10-18：⭐ LHM 最近一次 <see cref="Update" /> 距今多久（毫秒；⭐ -1 = 还没更新过；⭐ 诊断用 ✅）。</summary>
	internal long LastUpdateAgeMs
	{
		get
		{
			long tick = Interlocked.Read(in _lastUpdateTicks);
			if (tick == 0L)
			{
				return -1L;
			}
			return Environment.TickCount64 - tick;
		}
	}

	/// <summary>⭐ 2026-10-18：⭐ LHM 最近一次 <see cref="Update" /> 的时刻（<see cref="Environment.TickCount64" />；⭐ 0 = 还没更新过 ✅）。</summary>
	internal long LastUpdateTicks => Interlocked.Read(in _lastUpdateTicks);

	/// <summary>
	/// ⭐ 2026-10-18（机主：CPU 温度显示不跟随真实温度）：⭐ <b>被 1 s 限频/并发跳过的 Update 次数</b>（⭐ 诊断用）✅
	/// <para>⭐ 用途：⭐ 区分「本 tick 真的 Update 了」与「在 1 s 限频内、读到的是上一次的缓存」⚠️
	/// —— ⭐ `[cpu-temp]` 日志里会标成 <c>LHM=Update</c> / <c>LHM=缓存(ns前)</c> ✅。</para>
	/// </summary>
	internal int UpdateSkips => Volatile.Read(in _updateSkips);

	/// <summary>
	/// ⭐ 2026-10-18：⭐ 把一个 CPU 温度传感器名**分档**（⭐ 选源用）✅。
	/// <list type="bullet">
	/// <item><c>Tctl</c> / <c>Tdie</c> ⇒ <see cref="CpuTemperatureTier.TctlTdie" />（⭐ AMD 真实结温，官方口径）✅</item>
	/// <item><c>CPU Package</c> / <c>Core Average</c> / <c>Core Max</c> ⇒ <see cref="CpuTemperatureTier.Package" />（⭐ Intel 封装）✅</item>
	/// <item><c>Distance to TjMax</c> 之类 ⇒ <see cref="CpuTemperatureTier.Excluded" />（⭐ 语义相反，⛔ 不参与）⚠️</item>
	/// <item>其余（<c>Core #N</c> 等）⇒ <see cref="CpuTemperatureTier.Other" /> ✅</item>
	/// </list>
	/// </summary>
	internal static CpuTemperatureTier ClassifyCpuTemperatureSensorName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return CpuTemperatureTier.Excluded;
		}
		if (name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("TjMax", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return CpuTemperatureTier.Excluded;
		}
		if (name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return CpuTemperatureTier.TctlTdie;
		}
		if (name.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Core Average", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Core Max", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return CpuTemperatureTier.Package;
		}
		return CpuTemperatureTier.Other;
	}

	/// <summary>
	/// ⭐ 2026-10-18：⭐ 收集本轮一个 CPU 温度传感器（⭐ 值 + 档位 + 上次变化时刻）✅。
	/// <para>⚠️ 变化判据按 **0.05℃** 容差 ⇒ ⭐ 浮点抖动不算"变化"（⛔ 免得把"不变"误判成"在变" ⚠️）。</para>
	/// </summary>
	private void CollectCpuTemperatureSensor(string name, float value, long nowTicks, List<CpuTemperatureSensorReading> readings)
	{
		if (!_cpuTemperatureState.TryGetValue(name, out CpuTemperatureSensorState state))
		{
			state = new CpuTemperatureSensorState
			{
				Value = value,
				LastChangeTicks = nowTicks
			};
			_cpuTemperatureState[name] = state;
		}
		else if (Math.Abs(state.Value - value) > 0.05f)
		{
			state.Value = value;
			state.LastChangeTicks = nowTicks;
		}
		readings.Add(new CpuTemperatureSensorReading
		{
			Name = name,
			Value = value,
			Tier = ClassifyCpuTemperatureSensorName(name),
			LastChangeTicks = state.LastChangeTicks
		});
	}

	/// <summary>
	/// ⭐ 2026-10-18（机主：CPU 温度 80℃ 不动）：⭐ <b>本轮选源</b> ✅ ——
	/// ⭐ 在**本轮全部** CPU 温度传感器里按 <c>Tctl/Tdie &gt; CPU Package &gt; Core Max/其余</c> 选一个 ✅；
	/// ⭐ 同档多个（⭐ 如 CCD1/CCD2 两个 Tdie）⇒ ⭐ **取最高值**（⭐ 最接近"整颗 CPU 最热处" ✅）。
	/// <para>⚠️⚠️ 这条**必须在每轮 <see cref="Update" /> 里重新执行** —— 以前用
	/// <c>!CpuTemperature.HasValue</c> 做闸门 ⇒ ⭐ 一个实例只在第一次读到值后**再也不更新** ❌
	/// （⭐ 真机证据：同一个实例连续 20 分钟恒 90℃、重启后新实例恒 80℃ ⚠️）。</para>
	/// </summary>
	private void SelectCpuTemperature(List<CpuTemperatureSensorReading> readings, long nowTicks)
	{
		_cpuTemperatureSensors = readings.ToArray();
		CpuTemperatureSensorReading best = null;
		foreach (CpuTemperatureSensorReading reading in readings)
		{
			if (reading.Tier == CpuTemperatureTier.Excluded || !reading.Value.HasValue)
			{
				continue;
			}
			if (best == null || reading.Tier < best.Tier
				|| (reading.Tier == best.Tier && reading.Value.Value > best.Value.Value))
			{
				best = reading;
			}
		}
		if (best == null)
		{
			CpuTemperature = null;
			CpuTemperatureSensorName = null;
			CpuTemperatureSelectedTier = CpuTemperatureTier.None;
			_cpuTemperatureLastValue = null;
			_cpuTemperatureLastChangeTicks = 0L;
			return;
		}
		CpuTemperature = best.Value;
		CpuTemperatureSensorName = best.Name;
		CpuTemperatureSelectedTier = best.Tier;
		if (!_cpuTemperatureLastValue.HasValue || Math.Abs(_cpuTemperatureLastValue.Value - best.Value.Value) > 0.05f)
		{
			_cpuTemperatureLastValue = best.Value;
			_cpuTemperatureLastChangeTicks = nowTicks;
		}
	}

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
	/// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ <b>GPU 核心负载（0–100 %）</b> ——
	/// ⭐ 来自 LHM 的 <c>SensorType.Load</c>（⭐ 只在 **非 NVIDIA** 机型才可能命中 ✅：
	/// 本类检测到 NVIDIA 会把 LHM 的 GPU 那支整体关掉 ⚠️ —— ⭐ 那些机型由 <c>NvmlHelper.GetGpuUtilization</c> 提供 ✅）。
	/// <para>⚠️ **每轮 <see cref="Update" /> 都会清空重读**（⛔ 不重蹈 CPU 温度"只写一次"的覆辙 ✅；
	/// 取不到时保持 <c>null</c> ⇒ 界面显示「—」，⛔ 不用 0% 冒充 ✅）。</para>
	/// <para>⚠️ 与"显存占用"不是一回事：⭐ 这里只取 <c>Load</c> 里**核心/3D** 那一支（⭐ 显存/视频/总线负载一律排除 ✅）。</para>
	/// </summary>
	public int? GpuLoadPercent { get; private set; }

	/// <summary>⭐ 实际选中的 GPU 负载传感器名（⭐ 诊断用 ✅）。</summary>
	public string GpuLoadSensorName { get; private set; }

	/// <summary>
	/// ⭐ 2026-10-18：⭐ 判断某 <c>Load</c> 传感器是不是"GPU 核心负载"✅。
	/// <para>⭐ LHM 实测名字：⭐ NVIDIA = <c>GPU Core</c>（⭐ 生产被关）· ⭐ AMD 核显 = <c>D3D 3D</c> ✅。</para>
	/// <para>⚠️ 刻意排除：⭐ <c>Memory</c> / ⭐ <c>Video</c> / ⭐ <c>Bus</c> / ⭐ <c>Copy</c> —— ⭐ 那些不是核心 3D 占用 ⚠️。</para>
	/// </summary>
	internal static bool IsGpuLoadSensorName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Video", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		return name.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0;
	}

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

	/// <summary>核显/共享显存（D3D Shared Memory）已用 MB；独显通常为 0。</summary>
	public int GpuSharedMemoryUsedMb { get; private set; }

	/// <summary>核显/共享显存总量 MB。</summary>
	public int GpuSharedMemoryTotalMb { get; private set; }

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
			// ⭐ 2026-10-18：⭐ 1 s 限频内 ⇒ 本次**没有真正 Update**（⭐ 记数供诊断区分"Update / 缓存" ✅）
			Interlocked.Increment(ref _updateSkips);
			return;
		}
		lock (_updateLock)
		{
			tickCount = Environment.TickCount64;
			if (_disposed || tickCount - _lastUpdateTicks < 1000)
			{
				if (!_disposed)
				{
					Interlocked.Increment(ref _updateSkips);
				}
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
				GpuSharedMemoryTotalMb = 0;
				GpuSharedMemoryUsedMb = 0;
				// ⭐⭐ 2026-10-18（机主：30 系 CPU 温度 80℃ 不动 · 诊断包 lm-…-161007）：
				//   ⚠️⚠️ **根因**：⭐ 这里原来**没有**把 CPU 温度清成 null，而采集分支又带
				//   `!CpuTemperature.HasValue` 闸门 ⚠️ ⇒ ⭐ 一个 <see cref="LhmMonitor" /> 实例
				//   **只在第一次读到值之后就不再更新** ❌（⭐ 真机证据：[cpu-temp] 在同一实例里
				//   连续 20 分钟恒 90℃、重启后新实例恒 80℃ ⚠️）⇒ ⭐ 每轮必须清空、重新选 ✅。
				CpuTemperature = null;
				CpuTemperatureSensorName = null;
				CpuTemperatureSelectedTier = CpuTemperatureTier.None;
				List<CpuTemperatureSensorReading> cpuTempReadings = new List<CpuTemperatureSensorReading>();
				int cpuHardwareCount = 0;
				// ⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ GPU 负载同样**每轮清空重读**（⛔ 不冻结 ✅）
				GpuLoadPercent = null;
				GpuLoadSensorName = null;
				int? gpuLoad = null;
				string gpuLoadName = "";
				bool flag = false;
				bool flag2 = false;
				float? nvidiaPower = null;
				float? amdPower = null;
				float? integratedPower = null;
				if (_computer.Hardware.Count == 0)
				{
					TryReopen();
				}
				// ⭐ 本轮收集所有 GPU 功耗传感器（名称+值）与温度：一次性诊断日志 + AMD 核显选源 ✅
				List<(HardwareType Type, string Hardware, string Name, float Value)> gpuPowerSensors = new List<(HardwareType, string, string, float)>();
				List<(string Name, float Value)> gpuTemps = new List<(string, float)>();
				foreach (IHardware item in _computer.Hardware)
				{
					flag |= item.HardwareType == HardwareType.GpuNvidia;
					flag2 |= item.HardwareType == HardwareType.GpuAmd;
					if (item.HardwareType == HardwareType.Cpu)
					{
						cpuHardwareCount++;
					}
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
							else if (IsGpuHardwareType(item.HardwareType))
							{
								gpuPowerSensors.Add((item.HardwareType, item.Name, sensor.Name, sensor.Value.Value));
								// （门控审计：NVIDIA 路径逐位不变）：NVIDIA 仍用**原窄判据**；
								if (item.HardwareType == HardwareType.GpuNvidia ? IsGpuPowerSensorNameLegacy(sensor.Name) : IsGpuPowerSensorName(sensor.Name))
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
						}
						else if (sensor.SensorType == SensorType.Clock && item.HardwareType == HardwareType.Cpu && !CpuClockMhz.HasValue && IsCpuClockSensorName(sensor.Name))
						{
							float rawClock = sensor.Value.Value;
							// 有的 AMD/LHM 时钟传感器在低频时直接给 MHz（空闲 600、800），
							// 旧逻辑把所有 <1000 都当成 GHz ×1000 ⇒ 600MHz 被显示成 600GHz。
							// 只有明确的 GHz 量级（<20）才换算；600/800 这类按 MHz 原样保留。
							CpuClockMhz = (rawClock > 0f && rawClock < 20f) ? rawClock * 1000f : rawClock;
						}
						// ⭐⭐ 2026-10-18（机主：30 系 CPU 温度 80℃ 不动 · 诊断包 lm-…-161007）：
						//   ⚠️ 原来这里用 `!CpuTemperature.HasValue` 做闸门（⭐ 一旦有值就再也不进）
						//   ⇒ ⭐ 温度**冻结在实例的第一个读数上** ❌（⭐ 真机证据：同一实例 20 分钟恒 90℃、
						//   重启后新实例恒 80℃；⭐ 官方控制台同期也在变 ⚠️）。
						//   ⭐ 现在：⭐ **收集本轮全部 CPU 温度传感器**（⭐ 名称/值/档位/上次变化时刻 ✅），
						//   循环结束后由 `SelectCpuTemperature` 统一按「Tctl/Tdie ＞ CPU Package ＞ 其余」选源 ✅。
						//   ⭐ 官方口径：⭐ AMD 的 `Core (Tctl/Tdie)` = 真实结温（官方控制台读数）；⭐ Intel 为 `CPU Package` ✅
						else if (sensor.SensorType == SensorType.Temperature && item.HardwareType == HardwareType.Cpu)
						{
							CollectCpuTemperatureSensor(sensor.Name, sensor.Value.Value, tickCount, cpuTempReadings);
						}
						// ⭐⭐ 2026-10-16：⭐ GPU 温度（⭐ 只在 LHM 的 GPU 那支启用时才可能命中 ✅）
						else if (sensor.SensorType == SensorType.Temperature && IsGpuHardwareType(item.HardwareType)
							&& !GpuTemperature.HasValue && IsGpuTemperatureSensorName(sensor.Name))
						{
							gpuTemps.Add((sensor.Name, sensor.Value.Value));
							GpuTemperature = sensor.Value;
						}
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
						else if (sensor.SensorType == SensorType.SmallData && IsGpuSharedMemoryTotalSensorName(sensor.Name))
						{
							GpuSharedMemoryTotalMb = (int)sensor.Value.Value;
						}
						else if (sensor.SensorType == SensorType.SmallData && IsGpuSharedMemoryUsedSensorName(sensor.Name))
						{
							GpuSharedMemoryUsedMb = (int)sensor.Value.Value;
						}
						// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ GPU 核心负载（⭐ 只在**非 NVIDIA** 机型命中 ✅）
						//   ⚠️ 关键：⭐ 与本轮温度一样**每轮都收集** ⇒ ⛔ 不会重蹈"只写一次就冻结"的覆辙 ✅
						//   ⚠️ 只取 <c>Load</c> 里的**核心/3D** 那支（⭐ 显存/视频/总线负载由 `IsGpuLoadSensorName` 排除 ✅）
						else if (sensor.SensorType == SensorType.Load && IsGpuHardwareType(item.HardwareType)
							&& IsGpuLoadSensorName(sensor.Name) && sensor.Value.Value >= 0f && sensor.Value.Value <= 100f)
						{
							int load = (int)Math.Round(sensor.Value.Value);
							if (!gpuLoad.HasValue || load > gpuLoad.Value)
							{
								gpuLoad = load;
								gpuLoadName = sensor.Name;
							}
						}
					}
				}
				// ⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ 本轮 GPU 负载结果（⭐ 没有就保持 null ⇒ 界面「—」✅）
				GpuLoadPercent = gpuLoad;
				GpuLoadSensorName = gpuLoadName;
				// ⭐⭐ 2026-10-18（机主：CPU 温度 80℃ 不动）：⭐ 本轮 CPU 温度选源（⭐ 在全部传感器里按档位选 ✅）
				CpuHardwareCount = cpuHardwareCount;
				SelectCpuTemperature(cpuTempReadings, tickCount);
				// （机主：无界 780M 核显功耗不随负载变化）：AMD 核显优先选源 + 全量诊断日志 + 冻结判据
				bool gcuStylePower = false;
				if (ContainsAmdGpuHardware())
				{
					(float? gcuPower, float? gcuTemp, int? gcuLoad, string gcuDetail, string gcuTempDetail) = ReadAmdIgpuByGcuStyle();
					if (gcuPower.HasValue)
					{
						amdPower = gcuPower;
						gcuStylePower = true;
					}
					if (gcuTemp.HasValue)
					{
						GpuTemperature = gcuTemp;
					}
					if (gcuLoad.HasValue)
					{
						GpuLoadPercent = gcuLoad;
					}
					if (!string.IsNullOrEmpty(gcuDetail) && Logger.TryMarkChanged("gpu-power-gcu-style", gcuDetail + " · " + gcuTempDetail))
					{
						Logger.WriteError(gcuDetail);
						Logger.WriteError(gcuTempDetail);
					}
				}
				float? amdIgpuPower = null;
				string amdIgpuSource = "";
				if (!gcuStylePower)
				{
					(amdIgpuPower, amdIgpuSource) = SelectAmdIgpuPower(gpuPowerSensors);
					if (amdIgpuPower.HasValue)
					{
						amdPower = amdIgpuPower;
					}
				}
				float? gpuPowerSelected = SelectGpuPower(flag, nvidiaPower, flag2, amdPower, integratedPower);
				GpuPower = ApplyGpuPowerFreezeGuard(gpuPowerSelected, flag2 && !flag, gpuLoad, GpuTemperature);
				// ⭐ 门控审计：诊断/tick/启用日志只对 **AMD 核显** 打印（NVIDIA 日志/行为逐位不变）✅
				if (flag2 && !flag)
				{
					LogGpuPowerDiagnostics(gpuPowerSensors, gpuTemps, flag, flag2, amdIgpuSource, amdIgpuPower);
					LogGpuPowerTick(gpuPowerSensors, gpuTemps, amdIgpuSource, GpuPower, gpuLoad, GpuTemperature);
					if (!_dumpedSensors)
					{
						Logger.WriteError("[gpu-power] LHM 启用检查：IsGpuEnabled=" + _computer.IsGpuEnabled + " · nvidiaSkipped=" + _nvidiaGpuSkipped
							+ " · GPU 硬件数=" + _computer.Hardware.Count((IHardware h) => IsGpuHardwareType(h.HardwareType)));
					}
				}
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
	// <para> 只读；只影响 AMD 核显，NVIDIA 机型仍走 NVML/原路径 </para>
	private bool ContainsAmdGpuHardware()
	{
		foreach (IHardware hardware in _computer.Hardware)
		{
			if (ContainsAmdGpu(hardware))
			{
				return true;
			}
		}
		return false;
		static bool ContainsAmdGpu(IHardware hardware)
		{
			if (hardware.HardwareType == HardwareType.GpuAmd)
			{
				return true;
			}
			foreach (IHardware sub in hardware.SubHardware)
			{
				if (ContainsAmdGpu(sub))
				{
					return true;
				}
			}
			return false;
		}
	}
	// （机主：直接参照 GCU Monitor igpu_w()）
	private (float? Power, float? Temp, int? Load, string Detail, string TempDetail) ReadAmdIgpuByGcuStyle()
	{
		List<(string Key, float Value)> powers = new List<(string, float)>();
		float? temp = null;
		int? load = null;
		string tempKey = "";
		string loadKey = "";
		foreach (IHardware hardware in _computer.Hardware)
		{
			Walk(hardware);
		}
		void Walk(IHardware hardware)
		{
			try
			{
				hardware.Update();
			}
			catch
			{
				return;
			}
			if (hardware.HardwareType == HardwareType.GpuAmd)
			{
				foreach (ISensor sensor in hardware.Sensors)
				{
					if (!sensor.Value.HasValue)
					{
						continue;
					}
					string key = hardware.Name + "|" + sensor.Name;
					if (sensor.SensorType == SensorType.Power
						&& (sensor.Name.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0
							|| sensor.Name.IndexOf("GPU SoC", StringComparison.OrdinalIgnoreCase) >= 0))
					{
						powers.Add((key, sensor.Value.Value));
					}
					else if (sensor.SensorType == SensorType.Temperature && sensor.Name.IndexOf("VR SoC", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						temp = sensor.Value;
						tempKey = key;
					}
					else if (sensor.SensorType == SensorType.Load && sensor.Name.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0
						&& sensor.Value.Value >= 0f && sensor.Value.Value <= 100f)
					{
						load = (int)Math.Round(sensor.Value.Value);
						loadKey = key;
					}
				}
			}
			foreach (IHardware sub in hardware.SubHardware)
			{
				Walk(sub);
			}
		}
		float? total = null;
		List<string> parts = new List<string>();
		foreach ((string Key, float Value) power in powers)
		{
			float? valid = ValidateIgpuComponent(power.Key, power.Value);
			if (!valid.HasValue)
			{
				continue;
			}
			total = (total ?? 0f) + valid.Value;
			parts.Add(power.Key + "=" + valid.Value.ToString("F1") + "W");
		}
		string detail = total.HasValue
			? ("[gpu-power] GCU 口径：" + string.Join(" + ", parts) + " ⇒ " + total.Value.ToString("F1") + "W")
			: "";
		string tempDetail = "";
		if (temp.HasValue || load.HasValue)
		{
			tempDetail = "[gpu-power] GCU 口径：温度=" + (temp.HasValue ? tempKey + "=" + temp.Value.ToString("F1") + "℃" : "—")
				+ " · 占用=" + (load.HasValue ? loadKey + "=" + load.Value + "%" : "—");
		}
		return (total, temp, load, detail, tempDetail);
	}
	// ⭐ 合理性/尖峰过滤状态（AMD 核显专用；NVIDIA 不进入）✅
	private static readonly Dictionary<string, float> _igpuLastValidWatts = new Dictionary<string, float>();

	private static readonly List<float> _igpuRecentWatts = new List<float>();

	private static int _igpuAbnormalStreak;
	// 丢弃时保留该分量上一次有效值（没有则跳过该分量）；连续异常计数给上层（≥5 ⇒ 显示—）。
	private static float? ValidateIgpuComponent(string name, float value)
	{
		string reason = null;
		if (value > 120f)
		{
			reason = "超 iGPU 合理上限 120W";
		}
		else if (_igpuRecentWatts.Count >= 3)
		{
			List<float> sorted = new List<float>(_igpuRecentWatts);
			sorted.Sort();
			float median = sorted[sorted.Count / 2];
			if (value > median * 5f && value > 50f)
			{
				reason = "尖峰（>中位数×5 且 >50W，中位数=" + median.ToString("F1") + "W）";
			}
		}
		if (reason == null)
		{
			_igpuAbnormalStreak = 0;
			_igpuLastValidWatts[name] = value;
			_igpuRecentWatts.Add(value);
			if (_igpuRecentWatts.Count > 5)
			{
				_igpuRecentWatts.RemoveAt(0);
			}
			return value;
		}
		_igpuAbnormalStreak++;
		Logger.WriteError("[gpu-power] ⚠️ 丢弃异常样本：" + name + "=" + value.ToString("F1") + "W（" + reason + "）");
		if (_igpuLastValidWatts.TryGetValue(name, out float last))
		{
			return last;
		}
		return null;
	}
	private static (float? Power, string Source) SelectAmdIgpuPower(List<(HardwareType Type, string Hardware, string Name, float Value)> sensors)
	{
		List<(string Name, float Value)> candidates = new List<(string, float)>();
		foreach ((HardwareType Type, string Hardware, string Name, float Value) sensor in sensors)
		{
			if (sensor.Type != HardwareType.GpuAmd || !IsPlausibleAmdGpuPowerName(sensor.Name))
			{
				continue;
			}
			candidates.Add((sensor.Name, sensor.Value));
		}
		if (candidates.Count == 0)
		{
			return (null, "");
		}
		// ① SoC + Core 同时存在 ⇒ 相加（板载 iGPU 的两段真实功耗）
		bool hasSoc = false;
		bool hasCore = false;
		float sum = 0f;
		List<string> sumNames = new List<string>();
		foreach ((string Name, float Value) candidate in candidates)
		{
			bool isSoc = candidate.Name.IndexOf("SoC", StringComparison.OrdinalIgnoreCase) >= 0;
			bool isCore = candidate.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0;
			if (isSoc || isCore)
			{
				float? validated = ValidateIgpuComponent(candidate.Name, candidate.Value);
				if (!validated.HasValue)
				{
					continue;
				}
				hasSoc |= isSoc;
				hasCore |= isCore;
				sum += validated.Value;
				sumNames.Add(candidate.Name);
			}
		}
		if (sumNames.Count > 0)
		{
			if (hasSoc && hasCore)
			{
				Logger.WriteLineIfChanged("gpu-power-amd-sum", "[gpu-power] AMD 核显：SoC + Core 同时存在 ⇒ 相加（" + string.Join(" + ", sumNames) + "）");
			}
			return (sum, string.Join("+", sumNames));
		}
		if (_igpuAbnormalStreak >= 5)
		{
			if (Logger.TryMarkChanged("gpu-power-igpu-abnormal-streak", "[gpu-power] ⚠️ 连续异常 ⇒ 显示—"))
			{
				Logger.WriteError("[gpu-power] ⚠️ 连续异常 ⇒ 显示—");
			}
			return (null, "");
		}
		// ② 封装/整卡值
		foreach ((string Name, float Value) candidate in candidates)
		{
			if (candidate.Name.IndexOf("Package Power", StringComparison.OrdinalIgnoreCase) >= 0
				|| candidate.Name.IndexOf("GPU Power", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return (candidate.Value, candidate.Name);
			}
		}
		// ③ 其它 Power 传感器（已排除上限类）作为兜底
		return (candidates[0].Value, candidates[0].Name);
	}

	private static bool IsPlausibleAmdGpuPowerName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		if (name.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("TDP", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		return true;
	}

	/// <summary>⭐ 诊断（去重、默认可见）：把 LHM 上**所有** GPU 功耗/温度传感器名+值打印一次 ✅</summary>
	private static void LogGpuPowerDiagnostics(List<(HardwareType Type, string Hardware, string Name, float Value)> sensors,
		List<(string Name, float Value)> temps, bool nvidiaPresent, bool amdPresent, string selectedSource, float? selectedValue)
	{
		if (sensors.Count == 0 && temps.Count == 0)
		{
			return;
		}
		string hardwareType = nvidiaPresent ? "GpuNvidia" : (amdPresent ? "GpuAmd" : "GpuOther");
		string hardwareName = ((sensors.Count > 0) ? sensors[0].Hardware : "未知");
		string detail = "";
		foreach ((string Name, float Value) temp in temps)
		{
			detail += " · " + temp.Name + "=" + temp.Value.ToString("F1") + "℃";
		}
		foreach ((HardwareType Type, string Hardware, string Name, float Value) sensor in sensors)
		{
			detail += " · " + sensor.Name + "=" + sensor.Value.ToString("F1") + "W";
		}
		string keys = hardwareType + hardwareName + detail;
		if (Logger.TryMarkChanged("gpu-power-lhm-sensors", keys))
		{
			Logger.WriteError("[gpu-power] LHM GPU 硬件：type=" + hardwareType + " · name=" + hardwareName + " · 传感器：" + detail.TrimStart(' ', '·'));
		}
		if (!string.IsNullOrEmpty(selectedSource))
		{
			string selected = "[gpu-power] 选中源=" + selectedSource + " ⇒ 值=" + (selectedValue.HasValue ? selectedValue.Value.ToString("F1") : "null")
				+ "W（变化次数=" + _gpuPowerChangeCount + "）";
			if (Logger.TryMarkChanged("gpu-power-lhm-selected-" + selectedSource, selected))
			{
				Logger.WriteError(selected);
			}
		}
	}

	/// <summary>
	/// ⭐⭐ 冻结值判据（仅 AMD 核显）：连续 6 次（≈15s）读数完全一致 ⇒ 视为冻结，
	/// 返回 null（悬浮窗显示「—」），⛔ 不显示假实时值 ✅。
	/// </summary>
	private static float? ApplyGpuPowerFreezeGuard(float? value, bool amdIgpu, int? gpuLoad, float? gpuTemp)
	{
		if (!amdIgpu || !value.HasValue)
		{
			return value;
		}
		// ⭐ 负载/温度是否在动（待机恒定 = 正常，不能判冻结）✅
		bool moved = (gpuLoad.HasValue && _gpuPowerLastLoad.HasValue && Math.Abs(gpuLoad.Value - _gpuPowerLastLoad.Value) >= 10)
			|| (gpuTemp.HasValue && _gpuPowerLastTemp.HasValue && Math.Abs(gpuTemp.Value - _gpuPowerLastTemp.Value) >= 3f);
		if (moved)
		{
			_gpuPowerMovementSeen = true;
		}
		_gpuPowerLastLoad = gpuLoad;
		_gpuPowerLastTemp = gpuTemp;
		float rounded = (float)Math.Round(value.Value, 1);
		if (float.IsNaN(_gpuPowerLastValue) || Math.Abs(_gpuPowerLastValue - rounded) > 0.05f)
		{
			_gpuPowerLastValue = rounded;
			_gpuPowerChangeCount++;
			_gpuPowerStableCount = 0;
			_gpuPowerFrozen = false;
			_gpuPowerMovementSeen = false;
			return value;
		}
		_gpuPowerStableCount++;
		if (_gpuPowerStableCount >= 6 && _gpuPowerMovementSeen && !_gpuPowerFrozen)
		{
			_gpuPowerFrozen = true;
			Logger.WriteError("[gpu-power] ⚠️ 读数疑似冻结（load/temp 在变但功率恒 " + rounded.ToString("F1") + "W）⇒ 悬浮窗显示「—」");
		}
		return _gpuPowerFrozen ? ((float?)null) : value;
	}

	/// <summary>⭐⭐ 逐 tick 日志（10s 节流）：load/temp/全部功耗传感器/选源/变化状态 ✅</summary>
	private static void LogGpuPowerTick(List<(HardwareType Type, string Hardware, string Name, float Value)> sensors,
		List<(string Name, float Value)> temps, string selectedSource, float? selectedValue, int? gpuLoad, float? gpuTemp)
	{
		long now = Environment.TickCount64;
		if (now - _lastGpuPowerLogTick < 10000)
		{
			return;
		}
		_lastGpuPowerLogTick = now;
		string detail = "";
		foreach ((string Name, float Value) temp in temps)
		{
			detail += "GPU " + temp.Name + "=" + temp.Value.ToString("F1") + "℃ / ";
		}
		foreach ((HardwareType Type, string Hardware, string Name, float Value) sensor in sensors)
		{
			detail += sensor.Name + "=" + sensor.Value.ToString("F1") + "W / ";
		}
		string change = (!_lastGpuPowerLoggedValue.HasValue || !selectedValue.HasValue) ? "初次"
			: ((Math.Abs(_lastGpuPowerLoggedValue.Value - selectedValue.Value) > 0.05f) ? "变化" : "未变");
		if (selectedValue.HasValue)
		{
			_lastGpuPowerLoggedValue = selectedValue.Value;
		}
		Logger.WriteError("[gpu-power] tick：load=" + (gpuLoad.HasValue ? gpuLoad.Value.ToString() + "%" : "—")
			+ " · temp=" + (gpuTemp.HasValue ? gpuTemp.Value.ToString("F1") + "℃" : "—")
			+ " · 传感器：" + detail.TrimEnd(' ', '/')
			+ " · 选源=" + (string.IsNullOrEmpty(selectedSource) ? "未选中" : selectedSource)
			+ " ⇒ " + (selectedValue.HasValue ? selectedValue.Value.ToString("F1") + "W" : "—")
			+ "（与上次相比 " + change + "）");
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
		// 上限/容量类已在上面排除； 不改变 NVIDIA 路径（NVML 仍优先）
		if (text.Contains("GPU", StringComparison.OrdinalIgnoreCase) && text.Contains("Power", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return text.Equals("GPU Package", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Total Board Power", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Board Power", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>⭐ 原窄判据（NVIDIA 专用，保持改动前口径逐位不变）✅</summary>
	private static bool IsGpuPowerSensorNameLegacy(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.Trim();
		if (text.Contains("limit", StringComparison.OrdinalIgnoreCase) || text.Contains("capacity", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("maximum", StringComparison.OrdinalIgnoreCase) || text.Contains("minimum", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return text.Equals("GPU Package", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("GPU Package Power", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Total Board Power", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Board Power", StringComparison.OrdinalIgnoreCase);
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

	private static bool IsGpuSharedMemoryTotalSensorName(string? name)
	{
		return name != null
			&& (name.Equals("D3D Shared Memory Total", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Shared Memory Total", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsGpuSharedMemoryUsedSensorName(string? name)
	{
		return name != null
			&& (name.Equals("D3D Shared Memory Used", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Shared Memory Used", StringComparison.OrdinalIgnoreCase));
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
