using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 2026-10-09（本地功耗采样 NVML 原生崩溃修复）：加固后的 NVML 封装。
///
/// 事故依据（用户提供的 .NET Runtime 1026 / Application Error 1000，异常代码 0xc0000005，出错模块 coreclr.dll）：
///   LibreHardwareMonitor.Interop.NvidiaML.NvmlDeviceGetPowerUsage / Gpu.NvidiaGpu.Update()
///   ← LhmMonitor.Update() ← HardwareControl.RefreshLocalMonitoring() ← SampleLocalPower()
///   ← CustomModeForm.UpdatePowerWallVerdict() ← 构造函数里挂的 2s Timer。
///
/// 本类提供四层防护（缺一不可）：
///   (a) 异常防护：SEHException / AccessViolationException / Exception 全部就地吃掉，只记日志并返回 null；
///   (b) 句柄自愈：**从不缓存 device handle**（每次现取现用、并对 0 句柄做校验），
///       连续失败到阈值后 nvmlShutdown + nvmlInit_v2 重建，失败再升级；
///   (c) 降级而非致命：重建无效则进入隔离期，期间一次原生调用都不发（<see cref="Unavailable"/> = true），
///       到期做一次探测性重试（退避 60s → 10 分钟封顶），成功后自动恢复；
///   (d)(e) 生命周期与降频：结果按 <see cref="SnapshotTtlMs"/> 缓存，1 秒级调用方（悬浮窗）不会把原生调用频率放大。
/// 另外每次原生调用前都用 <see cref="NativeDriverGuard"/> 确认 nvml.dll 仍映射在初始化时的同一基址。
///
/// 2026-10-23（耀世 WER 0xc0000005）追加第 (f) 层：**显示/电源过渡保护** —— 显示设置变化
/// （分辨率/刷新率/MUX/进入睡眠前的显示重配）后 8s、电源挂起/恢复后 20s 内一律不发 NVML 原生调用
/// （到期重建）。真机证据：显示设置连续变化（10:53:33.061 起）后 1.3s，采样泵在
/// nvmlDeviceGetPowerUsage 发生原生 AV；原有防护只覆盖 ConsoleDisplayState 熄屏通知，漏掉了这段过渡窗口。
/// </summary>
public static class NvmlHelper
{
	private struct nvmlMemory_t
	{
		public ulong total;

		public ulong free;

		public ulong used;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：遥测里 GPU 占用率显示不对）：⭐ <c>nvmlUtilization_t</c>
	/// <para>⭐ 两个字段**都是 0–100 的百分比**（⛔ 不是 0–1、⛔ 不是 0–1000）：
	/// <list type="bullet">
	/// <item><c>gpu</c> = ⭐ **GPU 核心（graphics/compute）利用率** —— ⭐ 界面要的就是它 ✅</item>
	/// <item><c>memory</c> = ⭐ **显存控制器利用率** —— ⚠️ 只是"忙不忙"的百分比，**不是**显存占用率
	/// （⛔ 绝不能用它冒充核心占用率，也不能用它冒充"显存已用 %"）⚠️</item>
	/// </list></para>
	/// </summary>
	private struct nvmlUtilization_t
	{
		public uint gpu;

		public uint memory;
	}

	internal const string NvmlModuleName = "nvml.dll";

	private const string NvmlDll = "nvml.dll";

	private const int NVML_SUCCESS = 0;

	/// <summary>(e) 降频：同一份快照在 TTL 内复用，避免 1s 级调用方把原生调用频率放大。</summary>
	internal const int SnapshotTtlMs = 1500;

	/// <summary>(b) 连续失败到该阈值就重建 NVML（句柄/上下文）。</summary>
	internal const int FailureThreshold = 4;

	/// <summary>(b) 重建这么多次仍失败就降级（隔离）。</summary>
	private const int MaxReinitBeforeQuarantine = 2;

	private const long InitRetryIntervalMs = 5000L;

	/// <summary>(c) 隔离起步 60s，每次重试仍失败翻倍，上限 10 分钟。</summary>
	private const long QuarantineBaseMs = 60000L;

	private const long QuarantineMaxMs = 600000L;

	private static readonly object _lock = new object();

	static NvmlHelper()
	{
		// 2026-10-23（耀世 WER 0xc0000005）：显示/电源过渡保护的事件钩子（失败不影响其余防护）。
		TryInstallTransitionHooks();
	}

	private static bool _init;

	private static long _lastInitAttempt;

	/// <summary>本进程内彻底停用 NVML（模块失效或已确认原生异常）——之后一次原生调用都不发。</summary>
	private static bool _disabled;

	private static string _disabledReason = "";

	private static bool _moduleGuardArmed;

	private static int _consecutiveFailures;

	private static int _lastError;

	private static int _reinitCount;

	private static long _quarantineUntilTicks;

	private static long _quarantineMs = 60000L;

	private static long _snapshotTicks = -1L;

	private static uint _snapshotIndex;

	private static float? _snapshotPower;

	private static long? _snapshotUsedMb;

	private static long? _snapshotTotalMb;

	// ⭐⭐ 2026-10-16（0.291.9 · "接传感器" · ⭐ 通用缺口）：GPU 温度快照。
	//    ⭐ 起因：⭐ 用户确认「⭐ 对于所有机型都是这样子」—— ⭐ 即
	//      ⭐ ① LM 自带的 LHM 一旦检测到 NVIDIA 就**关掉 GPU 那支**（⭐ NVML 崩溃修复 ⚠️）
	//      ⭐ ② 宝龙达协议里**没有 GPU 温度功能号** ⚠️
	//    ⇒ ⭐ ⭐ **GPU 温度对所有机型都是缺口** ⇒ ⭐ 补在这里（⭐ 通用、⭐ 与后端无关 ✅）
	//    ⚠️ 沿用本类既有加固：⭐ 句柄每次现取 · ⭐ 模块存活守卫 · ⭐ 失败隔离 · ⭐ 快照 TTL ✅
	private static uint? _snapshotTempCelsius;

	// ⭐⭐ 2026-10-16（修「待机时 GPU 温度只显示 —」，用户反馈「显卡没读数是因为占用低待机了」但要求改善观感）：
	//    ⚠️ 为什么需要这两个字段：⭐ 既有的 `_lastError`（⭐ :70 ✅）**只在四个查询全失败时才更新**（⭐ 见
	//      `NoteFailureLocked` 的那次调用 ⚠️）⇒ ⭐ 待机时那种「⭐ 功耗查得到、⭐ 温度查不到」的组合
	//      ⭐ ⭐ **根本记不下来** ❌ ⇒ ⭐ 于是分不清下面这两种**语义完全不同**的状态 ⚠️：
	//        ① ⭐ 设备**在**、⭐ 只是温度这个查询没给值 ⇒ ⭐ 低功耗/待机态 ✅（⭐ 正常，⭐ 该显示"待机" ✅）
	//        ② ⭐ 设备**整个不可达**（⭐ 驱动异常/⭐ 卡掉了 ⚠️）⇒ ⭐ 真故障 ⇒ ⭐ 该显示"—" ✅
	//    ⭐ 判据：⭐ `_deviceHandleAlive`（⭐ 句柄拿到了 ⇒ 卡在 ✅）＋ `_lastTempError`（⭐ 温度查询专属返回码 ✅）
	private static int _lastTempError = -1;

	private static bool _deviceHandleAlive;

	/// <summary>⭐ <c>_lastTempError</c> 的专用码：⭐ 查询**返回成功、但值无效**（⭐ 0 或超范围 ⚠️）。
	/// <para>⚠️ 为什么单列一码：⭐ 低功耗/待机态下 <c>nvmlDeviceGetTemperature</c> 常**返回成功却给 0** ⚠️，
	/// 那与"⭐ 查询直接报错"是两种不同的失败路径 ✅ ⇒ ⭐ 判"待机"时必须一并认 ✅。</para></summary>
	private const int TempValueInvalid = -2;

	[DllImport("nvml.dll")]
	private static extern int nvmlInit_v2();

	[DllImport("nvml.dll")]
	private static extern int nvmlShutdown();

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerUsage(nint device, out uint powerMilliWatts);
	// （显卡功耗上限 · 参考 JiaolongControl 的 NVML 做法，只取协议）
	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerManagementLimitConstraints(nint device, out uint minLimitMilliWatts, out uint maxLimitMilliWatts);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerManagementLimit(nint device, out uint limitMilliWatts);
	// （机主：「恢复出厂设置」不生效）： 显卡功耗上限的**出厂默认值**（NVML 官方导出）。
	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerManagementDefaultLimit(nint device, out uint defaultLimitMilliWatts);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceSetPowerManagementLimit(nint device, uint limitMilliWatts);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMemoryInfo(nint device, out nvmlMemory_t memory);

	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ <c>nvmlDeviceGetTemperature</c>（⭐ NVML 官方导出 ✅）。
	/// <para>⭐ <paramref name="sensorType" /> 传 ⭐ <see cref="NvmlTemperatureGpu" />=<c>0</c>
	/// （⭐ `NVML_TEMPERATURE_GPU` ✅）。</para>
	/// <para>⚠️ 只取 GPU **核心**温度 —— ⭐ 热点/显存那类要么是别的导出、⭐ 要么需要更高权限，
	/// ⭐ 这里只补"界面最需要的那个数" ✅。</para>
	/// </summary>
	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetTemperature(nint device, int sensorType, out uint tempCelsius);

	/// <summary>⭐ `NVML_TEMPERATURE_GPU`（⭐ NVML 头文件里的枚举值 = 0 ✅）。</summary>
	private const int NvmlTemperatureGpu = 0;

	// ⭐⭐ 2026-10-16（"接传感器" · ⭐ 用户 2026-10-16 明确）：⭐ "显卡 / cpu 频率温度 LHM 应该都能读" ✅
	//    ⚠️ 但**生产里 LHM 的 GPU 那支被我们主动关掉**（⭐ `_nvidiaGpuSkipped`，⭐ NVML 原生崩溃修复 ⚠️）
	//    ⇒ ⭐ NVIDIA 机型的 **GPU 频率**也得从这里补 ✅
	//    ⭐（⭐ 温度已经补了，⭐ 见 `GetGpuTemperature` ✅）—— ⭐ 数据与 LHM 同源、⭐ 路径更安全 ✅
	private static uint? _snapshotClockMhz;

	/// <summary>
	/// ⭐ <c>nvmlDeviceGetClockInfo</c>（⭐ NVML 官方导出 ✅）。
	/// <para>⭐ <paramref name="clockType" /> 传 ⭐ <see cref="NvmlClockGraphics" />=<c>0</c>
	/// （⭐ `NVML_CLOCK_GRAPHICS` = 核心频率 ✅）。</para>
	/// </summary>
	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetClockInfo(nint device, int clockType, out uint clockMhz);

	/// <summary>⭐ `NVML_CLOCK_GRAPHICS`（⭐ NVML 头文件枚举值 = 0 ✅）。</summary>
	private const int NvmlClockGraphics = 0;

	// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ GPU 核心利用率快照。
	//   ⚠️ 旧链路里 `HardwareControl.gpuUsage` **只**来自 GCU/MQTT 推送（`hw.GpuUsage`）⚠️
	//   ⇒ ⭐ 宝龙达（无 GCU）机型永远是 0/空 ⇒ 界面画 0% ❌。
	//   ⭐ 现在补本地来源：⭐ `nvmlDeviceGetUtilizationRates().gpu`（0–100，⭐ 核心/graphics 口径）✅
	//   ⭐ 与功耗/温度/频率**同一次快照、同一把句柄、同一 TTL** ⇒ ⛔ 不放大原生调用频率 ✅
	private static int? _snapshotGpuUtilization;

	/// <summary>⭐ 同一次快照的**显存控制器利用率**（⚠️ 仅供诊断参考，⛔ 不上界面冒充核心占用 ✅）。</summary>
	private static int? _snapshotGpuMemoryUtilization;

	/// <summary>⭐ `nvmlDeviceGetUtilizationRates` 的专属最后返回码（⭐ -1 = 本进程还没查过 ✅）。</summary>
	private static int _lastUtilizationError = -1;

	/// <summary>
	/// ⭐ <c>nvmlDeviceGetUtilizationRates</c>（⭐ NVML 官方导出 ✅）。
	/// <para>⚠️ 参数是 <c>nvmlDevice_t</c>；返回两个 **0–100** 的百分比（见 <see cref="nvmlUtilization_t" />）。</para>
	/// </summary>
	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetUtilizationRates(nint device, out nvmlUtilization_t utilization);

	/// <summary>(c) 已降级：隔离期内或模块已失效，调用方应完全跳过本类（一次原生调用都不要发）。</summary>
	internal static bool Unavailable
	{
		get
		{
			if (_disabled)
			{
				return true;
			}
			long until = Interlocked.Read(in _quarantineUntilTicks);
			return until != 0L && Environment.TickCount64 < until;
		}
	}

	internal static bool ModuleGone => _disabled;

	internal static int ConsecutiveFailures => Volatile.Read(in _consecutiveFailures);

	internal static int LastError => Volatile.Read(in _lastError);

	/// <summary>⭐ 温度查询（<c>nvmlDeviceGetTemperature</c>）的**专属**最后返回码。
	/// <para>⚠️ 与 <see cref="LastError" /> 不同：那个只在四个查询**全失败**时才更新
	/// ⇒ ⭐ 待机时那种「⭐ 功耗查得到、⭐ 温度查不到」的组合，⭐ 它**记不到** ❌。
	/// <c>-1</c> = 本进程内还没查过温度 ✅。</para></summary>
	internal static int LastTempError => Volatile.Read(in _lastTempError);

	/// <summary>⭐⭐ 判据：⭐ <b>设备在、⭐ 但温度取不到</b> ⇒ ⭐ 低功耗/待机态 ✅（⛔ 不是故障 ⚠️）。
	/// <para>三条同时成立才算：⭐ ① 拿到过有效设备句柄（⭐ 说明卡在 ✅）·
	/// ② 温度查询的返回码不是 <c>NVML_SUCCESS</c>（⭐ 说明确实没给出值 ⚠️）·
	/// ③ 快照里**从来没有**过有效温度（⭐ 有旧值就轮不到这里 ✅）。</para>
	/// <para>⚠️ 用途：⭐ 让界面把这种状态显示成「待机」，⭐ 而不是甩一个看着像故障的「—」✅。</para>
	/// <para>⛔ <b>绝不用它编造温度数字</b> —— 它只回答"⭐ 为什么没有数字"✅，⭐ 不提供数字 ✅。</para></summary>
	internal static bool TempUnavailableWhileDeviceAlive
	{
		get
		{
			if (!Volatile.Read(in _deviceHandleAlive))
			{
				return false;
			}
			if (Volatile.Read(in _lastTempError) == NVML_SUCCESS)
			{
				return false;
			}
			lock (_lock)
			{
				return !_snapshotTempCelsius.HasValue;
			}
		}
	}

	public static void Init()
	{
		lock (_lock)
		{
			InitLocked();
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ **GPU 核心温度（摄氏度）** —— ⭐ 补"所有机型都缺的那一项" ✅
	/// <para>⭐ 用途：⭐ 宝龙达机型没有 GCU 推送、⭐ 而 LHM 的 GPU 那支在检测到 NVIDIA 时被关
	/// ⇒ ⭐ GPU 温度只能从这里来 ✅；⭐ 老同方/GCU 机型若 GCU 报了温度则以 GCU 为准（⭐ 调用方可自行取舍 ✅）。</para>
	/// <para>⭐ 沿用本类加固：⭐ 快照 TTL 复用 · ⭐ 失败计数与隔离 · ⭐ 拿不到句柄就绝不往下传 ✅
	/// ⇒ ⭐ 取不到时返回 <c>null</c>（⭐ 调用方跳过即可，⛔ 不会抛 ✅）。</para>
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ **GPU 核心频率（MHz）** —— ⭐ 补 NVIDIA 机型上"LHM 读不到"的那一项 ✅
	/// <para>⭐ 依据用户 2026-10-16：⭐ "显卡/cpu 频率温度 LHM 应该都能读" ✅
	/// —— ⭐ LHM **库**确实能读，⭐ 但**我们的用法**在检测到 NVIDIA 时关掉了 GPU 那支
	/// （⭐ NVML 原生崩溃修复 ⚠️）⇒ ⭐ 所以 NVIDIA 机型由本方法提供 ✅，⭐ 数据同源 ✅</para>
	/// <para>⭐ 取不到返回 <c>null</c>（⭐ 调用方跳过即可 ✅）。</para>
	/// </summary>
	public static float? GetGpuClockMhz(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			if (!_snapshotClockMhz.HasValue)
			{
				return null;
			}
			return _snapshotClockMhz.Value;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ <b>GPU 核心利用率（0–100 %）</b> ✅
	/// —— ⭐ 取自 <c>nvmlDeviceGetUtilizationRates().gpu</c>（⭐ graphics/compute 口径 ✅）。
	/// <para>⚠️ 与"显存占用率"**不是一回事**：⭐ 那个是 `used/total`（<see cref="GetMemoryInfo" />），
	/// ⛔ 不要混用（⛔ 更不能用本值冒充显存占用）✅。</para>
	/// <para>⚠️ 取不到（NVML 隔离 / 句柄失效 / 低功耗返回异常）⇒ <c>null</c>（⭐ 调用方保留旧值 +
	/// 不更新时间戳 ⇒ 界面按"过期"显示「—」，⛔ 不显示 0% 冒充 ✅）。</para>
	/// </summary>
	internal static int? GetGpuUtilization(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			return _snapshotGpuUtilization;
		}
	}

	/// <summary>⭐ 同一次快照的**显存控制器利用率**（⚠️ 仅诊断参考，⛔ 上界面会误导 ✅）。</summary>
	internal static int? GetGpuMemoryUtilizationForDiagnostics(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			return _snapshotGpuMemoryUtilization;
		}
	}

	/// <summary>⭐ `nvmlDeviceGetUtilizationRates` 的专属最后返回码（⭐ -1 = 还没查过 · 0 = 成功 ✅；诊断用）。</summary>
	internal static int LastUtilizationError => Volatile.Read(in _lastUtilizationError);

	/// <summary>⭐ 设备句柄是否拿到过（⭐ 诊断用：判"卡在但没值" vs "卡不在" ✅）。</summary>
	internal static bool DeviceHandleAlive => Volatile.Read(in _deviceHandleAlive);

	/// <summary>⭐ NVML 快照年龄（毫秒；⭐ -1 = 还没有任何快照 ✅；⭐ 诊断用）。</summary>
	internal static long SnapshotAgeMs
	{
		get
		{
			lock (_lock)
			{
				if (_snapshotTicks < 0L)
				{
					return -1L;
				}
				return Environment.TickCount64 - _snapshotTicks;
			}
		}
	}

	public static float? GetGpuTemperature(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			if (!_snapshotTempCelsius.HasValue)
			{
				return null;
			}
			return _snapshotTempCelsius.Value;
		}
	}

	public static float? GetGpuPower(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			return _snapshotPower;
		}
	}

	public static (long usedMb, long totalMb)? GetMemoryInfo(uint gpuIndex = 0u)
	{
		lock (_lock)
		{
			RefreshLocked(gpuIndex);
			if (!_snapshotUsedMb.HasValue || !_snapshotTotalMb.HasValue)
			{
				return null;
			}
			return (_snapshotUsedMb.Value, _snapshotTotalMb.Value);
		}
	}

	/// <summary>(b)(c) 重建入口：清掉失败计数与隔离期，下次采样重新 nvmlInit_v2 并重取句柄。模块已失效时内部直接跳过。</summary>
	internal static void Reset()
	{
		lock (_lock)
		{
			_consecutiveFailures = 0;
			_reinitCount = 0;
			_lastError = 0;
			// ⭐⭐ 2026-10-16：⭐ 温度专属状态一并复位 ⇒ ⭐ 重建后重新判定"⭐ 是待机还是故障" ✅
			//   （⭐ 不复位的话，重建前那次「卡不在」的结论会残留 ⇒ ⭐ 界面可能一直当故障 ⚠️）
			Volatile.Write(ref _lastTempError, -1);
			Volatile.Write(ref _deviceHandleAlive, false);
			_quarantineUntilTicks = 0L;
			_quarantineMs = QuarantineBaseMs;
			_init = false;
			_lastInitAttempt = 0L;
			InvalidateSnapshotLocked();
			if (!_disabled)
			{
				Logger.WriteLine("NVML 已请求重建（失败计数清零，下次采样重新初始化并重取设备句柄）");
			}
		}
	}

	public static void Shutdown()
	{
		lock (_lock)
		{
			if (!_init)
			{
				return;
			}
			if (ModuleTrustedLocked("Shutdown 前"))
			{
				try
				{
					int value = nvmlShutdown();
					Logger.WriteLine($"NVML Shutdown: {value}");
				}
				catch (SEHException ex)
				{
					DisableLocked("nvmlShutdown 原生异常", ex);
				}
				catch (AccessViolationException ex)
				{
					DisableLocked("nvmlShutdown 原生异常", ex);
				}
				catch (Exception ex)
				{
					Logger.WriteLineThrottled("nvml-shutdown", "NVML shutdown failed: " + ex.GetType().Name + " " + ex.Message, 60000);
				}
			}
			_init = false;
			_lastInitAttempt = 0L;
			InvalidateSnapshotLocked();
		}
	}

	private static void InitLocked()
	{
		if (_init || _disabled)
		{
			return;
		}
		long tickCount = Environment.TickCount64;
		if (tickCount - _lastInitAttempt < InitRetryIntervalMs)
		{
			return;
		}
		_lastInitAttempt = tickCount;
		if (!ModuleTrustedLocked("初始化前"))
		{
			return;
		}
		try
		{
			int num = nvmlInit_v2();
			_init = num == NVML_SUCCESS;
			if (!_init)
			{
				Logger.WriteLineThrottled("nvml-init", $"NVML Init: {num}", 60000);
				return;
			}
			// (b) 记住初始化成功时 nvml.dll 的基址：之后每次调用前比对，模块被卸载/换址就再也不碰它。
			nint num2 = NativeDriverGuard.Query(NvmlModuleName);
			_moduleGuardArmed = num2 != 0;
			if (_moduleGuardArmed)
			{
				NativeDriverGuard.Remember(NvmlModuleName, num2);
			}
			else
			{
				Logger.WriteLine("NVML 初始化成功但拿不到 nvml.dll 模块句柄，模块存活守卫未启用（其余防护仍在）");
			}
			Logger.WriteLine($"NVML Init: {num}（设备句柄每次现取，不缓存）");
		}
		catch (SEHException ex)
		{
			DisableLocked("nvmlInit_v2 原生异常", ex);
		}
		catch (AccessViolationException ex)
		{
			DisableLocked("nvmlInit_v2 原生异常", ex);
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("nvml-init", "NVML Init exception: " + ex.GetType().Name + " " + ex.Message, 60000);
		}
	}

	/// <summary>
	/// (b) 重建一次：先 nvmlShutdown（带模块守卫），再允许立即 nvmlInit_v2；失败的 device handle 绝不带过来。
	/// </summary>
	private static void ReinitLocked(string reason)
	{
		if (ModuleTrustedLocked("重建前"))
		{
			try
			{
				if (_init)
				{
					int num = nvmlShutdown();
					Logger.WriteLine($"NVML 重建前 Shutdown: {num}（原因：{reason}）");
				}
			}
			catch (SEHException ex)
			{
				DisableLocked("nvmlShutdown 原生异常", ex);
				return;
			}
			catch (AccessViolationException ex)
			{
				DisableLocked("nvmlShutdown 原生异常", ex);
				return;
			}
			catch (Exception ex)
			{
				Logger.WriteLineThrottled("nvml-shutdown", "NVML 重建前 shutdown 失败: " + ex.GetType().Name + " " + ex.Message, 60000);
			}
		}
		_init = false;
		_lastInitAttempt = 0L;
		InvalidateSnapshotLocked();
	}

	/// <summary>模块存活判定：基址变了就**永久**停用（CoreCLR 桩里缓存的导出地址无法在本进程内安全重解析）。</summary>
	private static bool ModuleTrustedLocked(string phase)
	{
		if (!_moduleGuardArmed)
		{
			return true;
		}
		if (!NativeDriverGuard.IsStale(NvmlModuleName, out nint _))
		{
			return true;
		}
		DisableLocked($"nvml.dll 已不在初始化时的基址（{phase}）：模块被卸载/重载，缓存导出地址失效");
		return false;
	}

	private static void DisableLocked(string reason, Exception ex = null)
	{
		if (_disabled)
		{
			return;
		}
		_disabled = true;
		_disabledReason = reason;
		_init = false;
		_quarantineUntilTicks = long.MaxValue;
		InvalidateSnapshotLocked();
		string text = ((ex == null) ? "" : $" {ex.GetType().Name}: {ex.Message}");
		Logger.WriteLine($"NVML 已在本进程停用（GPU 功耗/显存采样降级）：{reason}{text}");
	}

	// 2026-10-22（JIGUANG：待机熄屏再亮屏 0xc0000005）：显卡驱动在熄屏/唤醒时会重置，
	// 期间任何 NVML 原生调用都可能踩到失效句柄 ⇒ 熄屏期间完全跳过采样，亮屏后重建再恢复。
	private static int _displaySuspended;

	// 2026-10-23（耀世 WER 0xc0000005）：显示/电源过渡保护。
	// 证据：GCU 日志 10:53:33.061 起 SystemEvents_DisplaySettingsChanged 连续 4 次（至 10:53:37.55），
	//       L-Mechrevo 10:53:34.409 在 nvmlDeviceGetPowerUsage 原生 AV（栈：GetGpuUtilization → RefreshLocked
	//       ← SampleLocalGpuUsage ← 采样泵）。原有防护只在收到 ConsoleDisplayState「显示器关闭」后暂停，
	//       而显示设置变化/进入睡眠前的显示重配**先于**该通知发生，采样泵仍会调 NVML ⇒ 这段过渡窗口必须自己挡。
	// 做法：过渡事件后一段时间内**不发任何 NVML 原生调用**，到期再 Reset 重建（不阻塞事件线程）。
	/// <summary>显示设置变化（分辨率/刷新率/MUX/全屏切换）后的保护窗口：真机证据里 1.3s 后即崩，
	/// 8s 已远超过渡窗口；同时短于功耗/占用率 10s 新鲜度 ⇒ 正常情况下界面不会因为保护期而变「—」。</summary>
	internal const int DisplayChangeGuardMs = 8000;

	/// <summary>电源挂起/恢复（睡眠/唤醒）与亮屏后的保护窗口：驱动重初始化通常需要更久，沿用既有 15s 量级。</summary>
	internal const int SleepResumeGuardMs = 20000;

	private static long _transitionGuardUntilTicks;

	private static int _transitionHookState;

	/// <summary>2026-10-22（JIGUANG 待机亮屏）：采样泵判定 NVML 卡死 ⇒ 本进程停止一切 NVML 原生调用。
	/// <para>卡在原生调用里的线程无法安全终止，只能保证不再产生新的调用（不再叠加卡死线程）。</para></summary>
	internal static void QuarantineAfterNativeHang(string reason)
	{
		lock (_lock)
		{
			DisableLocked(reason);
		}
	}

	internal static void SuspendForDisplayOff()
	{
		Volatile.Write(ref _displaySuspended, 1);
		Logger.WriteLine("NVML: 显示器关闭 ⇒ 暂停采样（避免驱动重置期原生调用）");
	}

	internal static void ResumeForDisplayOn()
	{
		Volatile.Write(ref _displaySuspended, 0);
		Reset();
		Logger.WriteLine("NVML: 显示器打开 ⇒ 已请求重建（下次采样重取句柄）");
		// 2026-10-23：亮屏后驱动仍可能在重初始化 ⇒ 同样进入过渡保护（保护所有调用方，不只是采样泵）。
		NoteNativeTransition("显示器打开", SleepResumeGuardMs);
	}

	/// <summary>
	/// 2026-10-23（耀世 WER 0xc0000005）：安装**显示/电源过渡保护**的事件钩子（只装一次）。
	/// <para>覆盖：⭐ 显示设置变化（分辨率/刷新率/MUX 切换/进入睡眠前的显示重配）· ⭐ 电源挂起/恢复。
	/// ⚠️ 失败只记一行：钩子装不上时其余四层防护照旧，⛔ 不影响启动。</para>
	/// </summary>
	private static void TryInstallTransitionHooks()
	{
		if (Interlocked.CompareExchange(ref _transitionHookState, 1, 0) != 0)
		{
			return;
		}
		try
		{
			Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChangedForNvml;
			Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChangedForNvml;
			Logger.WriteLine("NVML: 已启用显示/电源过渡保护（显示变化后 " + (DisplayChangeGuardMs / 1000)
				+ "s / 睡眠唤醒后 " + (SleepResumeGuardMs / 1000) + "s 内不发原生调用，到期重建）");
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("nvml-transition-hook", "NVML: 订阅显示/电源事件失败（其余防护不受影响）：" + ex.Message, 60000);
		}
	}

	private static void OnDisplaySettingsChangedForNvml(object sender, EventArgs e)
	{
		NoteNativeTransition("显示设置变化", DisplayChangeGuardMs);
	}

	private static void OnPowerModeChangedForNvml(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
	{
		if (e.Mode == Microsoft.Win32.PowerModes.Suspend || e.Mode == Microsoft.Win32.PowerModes.Resume)
		{
			NoteNativeTransition("电源 " + e.Mode, SleepResumeGuardMs);
		}
	}

	/// <summary>
	/// 2026-10-23：⭐ <b>进入显示/电源过渡保护期</b> ✅ —— ⭐ 从现在起 <paramref name="guardMs"/> 内
	/// <see cref="RefreshLocked" /> 不再发任何 NVML 原生调用；⭐ 到期后自动 <see cref="Reset" /> 重建再恢复。
	/// <para>⚠️ 事件的连续爆发（显示重配通常 3~5 次）⇒ ⭐ 每次都**延长**保护期，⭐ 只有最后一个事件负责重建
	/// （旧任务发现保护期已被更新就退出）✅。</para>
	/// <para>⚠️ 不在事件线程上做任何原生调用/阻塞等待（只置时间戳 + 后台延时）✅。</para>
	/// </summary>
	private static void NoteNativeTransition(string reason, int guardMs)
	{
		long until = Environment.TickCount64 + guardMs;
		Interlocked.Exchange(ref _transitionGuardUntilTicks, until);
		Logger.WriteLineThrottled("nvml-transition",
			$"NVML: {reason} ⇒ 暂停原生调用 {guardMs / 1000}s（避免显示/驱动过渡期 0xc0000005）", 3000);
		_ = Task.Run(async delegate
		{
			await Task.Delay(guardMs + 1500).ConfigureAwait(continueOnCapturedContext: false);
			// 期间又有新的过渡 ⇒ 保护期已被延长，由最后一次事件负责重建
			if (Interlocked.Read(in _transitionGuardUntilTicks) != until)
			{
				return;
			}
			Reset();
			Logger.WriteLine("NVML: 显示/电源过渡保护期结束 ⇒ 已请求重建（下次采样重取句柄）");
		});
	}

	/// <summary>⭐ 2026-10-23：当前是否处于显示/电源过渡保护期（⭐ 只读，诊断用 ✅）。</summary>
	internal static bool TransitionGuarded
	{
		get
		{
			long until = Interlocked.Read(in _transitionGuardUntilTicks);
			return until != 0L && Environment.TickCount64 < until;
		}
	}

	private static void RefreshLocked(uint gpuIndex)
	{
		long tickCount = Environment.TickCount64;
		if (_snapshotIndex == gpuIndex && _snapshotTicks >= 0L && tickCount - _snapshotTicks < SnapshotTtlMs)
		{
			return;
		}
		if (_disabled)
		{
			InvalidateSnapshotLocked();
			return;
		}
		if (Unavailable)
		{
			// (c) 隔离期内一次原生调用都不发。
			InvalidateSnapshotLocked();
			return;
		}
		if (Volatile.Read(in _displaySuspended) != 0)
		{
			InvalidateSnapshotLocked();
			return;
		}
		// 2026-10-23（耀世 WER 0xc0000005）：显示/电源过渡窗口内一次 NVML 原生调用都不发（见 TransitionGuardMs）。
		if (TransitionGuarded)
		{
			InvalidateSnapshotLocked();
			return;
		}
		InitLocked();
		_snapshotIndex = gpuIndex;
		_snapshotTicks = tickCount;
		_snapshotPower = null;
		_snapshotUsedMb = null;
		_snapshotTotalMb = null;
		// ⭐ 2026-10-18：⭐ 占用率**每轮先清空**再查 —— ⚠️ 否则本轮查询失败时会继续返回上一轮的旧值，
		//   ⭐ 上层会误以为"一直有新鲜值"⇒ ⛔ 违背"取不到就显示「—」"的口径 ✅
		_snapshotGpuUtilization = null;
		_snapshotGpuMemoryUtilization = null;
		if (!_init)
		{
			return;
		}
		if (!ModuleTrustedLocked("采样前"))
		{
			return;
		}
		try
		{
			nint device = 0;
			int num = nvmlDeviceGetHandleByIndex_v2(gpuIndex, out device);
			if (num != NVML_SUCCESS)
			{
				// ⭐ 拿不到句柄 ⇒ ⭐ 卡不在了（⭐ 或驱动异常 ⚠️）⇒ ⭐ 这与"待机"是两回事 ✅
				Volatile.Write(ref _deviceHandleAlive, false);
				NoteFailureLocked("nvmlDeviceGetHandleByIndex_v2", num);
				return;
			}
			if (device == 0)
			{
				// 句柄校验：拿不到有效句柄时绝不往下传，避免把 0/垃圾指针交给驱动。
				Volatile.Write(ref _deviceHandleAlive, false);
				NoteFailureLocked("nvmlDeviceGetHandleByIndex_v2 返回 0 句柄", -1);
				return;
			}
			// ⭐⭐ 2026-10-16：⭐ 句柄有效 ⇒ ⭐ **卡在** ✅ —— 这是"待机"判定的必要条件之一 ⚠️
			//   ⭐ 待机时句柄照样拿得到（⭐ 卡没被移走 ✅），⭐ 只是温度那个查询不给值 ⚠️；
			//   ⭐ 而真故障（⭐ 驱动异常/⭐ 卡掉了 ❌）连句柄都拿不到 ⇒ ⭐ 两者由此分开 ✅
			Volatile.Write(ref _deviceHandleAlive, true);
			int num2 = nvmlDeviceGetPowerUsage(device, out uint powerMilliWatts);
			if (num2 == NVML_SUCCESS && powerMilliWatts > 0 && (float)powerMilliWatts <= 200000f)
			{
				_snapshotPower = (float)powerMilliWatts / 1000f;
			}
			int num3 = nvmlDeviceGetMemoryInfo(device, out nvmlMemory_t memory);
			if (num3 == NVML_SUCCESS && memory.total > 0L)
			{
				_snapshotUsedMb = (long)(memory.used / 1048576);
				_snapshotTotalMb = (long)(memory.total / 1048576);
			}
			// ⭐⭐ 2026-10-16：⭐ GPU 核心温度（⭐ 与功耗/显存同一把句柄、同一次快照 ✅）
			//    ⭐ 合理性校验：⭐ (0, 150] 摄氏度 —— ⭐ NVML 取不到时常返回 0 ⚠️，⭐ 那不算有效温度 ✅
			int num4 = nvmlDeviceGetTemperature(device, NvmlTemperatureGpu, out uint tempCelsius);
			bool tempValid = num4 == NVML_SUCCESS && tempCelsius > 0 && tempCelsius <= 150;
			// ⭐⭐ 2026-10-16：⭐ 温度查询的**专属**结果码 —— ⭐ 与 `_lastError` 分开记 ⚠️
			//   （⭐ 那个只在四个查询**全失败**时才更新 ⇒ ⭐ 待机这种「⭐ 功耗成功、⭐ 温度失败」
			//    的组合它**记不到** ❌）
			//   ⭐ 编码：⭐ `NVML_SUCCESS`(0) = 拿到有效温度 ✅ · ⭐ 其他 ≥0 = NVML 原始返回码 ·
			//     ⭐ `TempValueInvalid`(-2) = 返回成功但值无效（⭐ 0 或超范围 ⚠️）——
			//     ⭐ 后者恰是"低功耗态下 NVML 返回 0"的签名 ✅ ⇒ ⭐ 必须与"查询报错"分开 ✅
			Volatile.Write(ref _lastTempError, tempValid ? NVML_SUCCESS : ((num4 == NVML_SUCCESS) ? TempValueInvalid : num4));
			if (tempValid)
			{
				_snapshotTempCelsius = tempCelsius;
			}
			// ⭐⭐ 2026-10-16：⭐ GPU 核心频率（⭐ 与上面同一把句柄、同一次快照 ✅）
			//    ⭐ 合理性：(0, 4000] MHz —— ⭐ 取不到时 NVML 常返回 0 ⚠️，⭐ 那不算有效频率 ✅
			int num8 = nvmlDeviceGetClockInfo(device, NvmlClockGraphics, out uint clockMhz);
			if (num8 == NVML_SUCCESS && clockMhz > 0 && clockMhz <= 4000)
			{
				_snapshotClockMhz = clockMhz;
			}
			// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ GPU 核心利用率（⭐ 与上面同一把句柄、同一次快照 ✅）
			//   ⚠️ 合理性：(0, 100] 的百分比 —— ⭐ `0` **是合法值**（待机时真的 0%）⚠️
			//   ⇒ ⭐ 与温度不同，这里**不能**把 0 当无效（⭐ 否则待机时永远显示「—」/旧值 ❌）
			//   ⭐ 判据只看返回码 + 上界 ≤100 ✅
			int num9 = nvmlDeviceGetUtilizationRates(device, out nvmlUtilization_t utilization);
			bool utilizationValid = num9 == NVML_SUCCESS && utilization.gpu <= 100u;
			Volatile.Write(ref _lastUtilizationError, utilizationValid ? NVML_SUCCESS : num9);
			if (utilizationValid)
			{
				_snapshotGpuUtilization = (int)utilization.gpu;
				// ⚠️ 显存控制器利用率只做诊断参考（⭐ 不是显存占用率，⛔ 不上界面冒充）
				if (utilization.memory <= 100u)
				{
					_snapshotGpuMemoryUtilization = (int)utilization.memory;
				}
			}
			// 只要有一个查询成功就说明句柄/上下文是活的（功耗返回 0 是低功耗态，不是失败）。
			if (num2 == NVML_SUCCESS || num3 == NVML_SUCCESS || num4 == NVML_SUCCESS || num8 == NVML_SUCCESS || num9 == NVML_SUCCESS)
			{
				NoteSuccessLocked();
				return;
			}
			NoteFailureLocked("nvmlDeviceGetPowerUsage/nvmlDeviceGetMemoryInfo/nvmlDeviceGetTemperature/nvmlDeviceGetClockInfo/nvmlDeviceGetUtilizationRates", num2);
		}
		catch (SEHException ex)
		{
			DisableLocked("NVML 采样原生异常", ex);
		}
		catch (AccessViolationException ex)
		{
			DisableLocked("NVML 采样原生异常", ex);
		}
		catch (Exception ex)
		{
			NoteFailureLocked("NVML 采样异常 " + ex.GetType().Name + ": " + ex.Message, -1);
		}
	}

	private static void NoteSuccessLocked()
	{
		if (Interlocked.Exchange(ref _consecutiveFailures, 0) != 0)
		{
			Logger.WriteLine("NVML 采样已恢复（连续失败计数清零）");
		}
		_reinitCount = 0;
	}

	/// <summary>(b)(c) 失败计数 → 重建 → 仍失败则隔离降级（退避翻倍，到期才重试）。</summary>
	private static void NoteFailureLocked(string what, int code)
	{
		_lastError = code;
		int num = Interlocked.Increment(ref _consecutiveFailures);
		if (num < FailureThreshold)
		{
			Logger.WriteLineThrottled("nvml-fail", $"NVML 采样失败({num}/{FailureThreshold})：{what} code={code}", 30000);
			return;
		}
		Interlocked.Exchange(ref _consecutiveFailures, 0);
		if (_reinitCount < MaxReinitBeforeQuarantine)
		{
			int num2 = ++_reinitCount;
			Logger.WriteLine($"NVML 连续 {FailureThreshold} 次采样失败（{what} code={code}）：重建 NVML 句柄/上下文（第 {num2}/{MaxReinitBeforeQuarantine} 次）");
			ReinitLocked($"{what} code={code}");
			return;
		}
		_reinitCount = 0;
		_init = false;
		_lastInitAttempt = 0L;
		InvalidateSnapshotLocked();
		_quarantineUntilTicks = Environment.TickCount64 + _quarantineMs;
		Logger.WriteLine($"NVML 重建无效（{what} code={code}）：GPU 功耗/显存采样降级停用 {_quarantineMs / 1000}s，到期自动重试一次");
		_quarantineMs = Math.Min(_quarantineMs * 2, QuarantineMaxMs);
	}

	private static void InvalidateSnapshotLocked()
	{
		_snapshotTicks = -1L;
		_snapshotPower = null;
		_snapshotUsedMb = null;
		_snapshotTotalMb = null;
		// ⭐ 2026-10-16：⭐ 温度与频率同样要作废（⭐ 否则会拿旧值冒充新读数 ⚠️）
		_snapshotTempCelsius = null;
		_snapshotClockMhz = null;
		// ⭐ 2026-10-18：⭐ 占用率也一并作废（⭐ 免得拿旧快照冒充实时占用 ✅）
		_snapshotGpuUtilization = null;
		_snapshotGpuMemoryUtilization = null;
		Volatile.Write(ref _lastUtilizationError, -1);
		// ⭐⭐ 2026-10-16（修"待机显示 —"）：⭐ 快照作废 ⇒ ⭐ "⭐ 设备还在不在"与"⭐ 温度为什么没有"
		//   这两个结论**同时失效** ⚠️ ⇒ ⭐ 一并复位，⭐ 免得拿陈旧结论去判"待机" ✅
		//   （⭐ 下一次成功采样会重新把 `_deviceHandleAlive` 置真 ✅）
		Volatile.Write(ref _deviceHandleAlive, false);
		Volatile.Write(ref _lastTempError, -1);
	}

	internal static string DisabledReason => _disabledReason;

	/// <summary>
	/// ⭐ 读显卡功耗上限的**驱动允许区间**（W）。⭐ 失败（无 NVML/无约束/该机型不支持）⇒ false。
	/// <para>⚠️ 单位：NVML 是毫瓦，这里统一换成 **W** 再返回 ✅</para>
	/// </summary>
	internal static bool TryGetPowerLimitBounds(out int minW, out int maxW, out int currentW)
	{
		minW = 0;
		maxW = 0;
		currentW = 0;
		if (Unavailable)
		{
			return false;
		}
		try
		{
			if (nvmlDeviceGetHandleByIndex_v2(0u, out nint device) != 0 || device == 0)
			{
				return false;
			}
			if (nvmlDeviceGetPowerManagementLimitConstraints(device, out uint minMw, out uint maxMw) != 0)
			{
				return false;
			}
			if (nvmlDeviceGetPowerManagementLimit(device, out uint curMw) != 0)
			{
				curMw = 0u;
			}
			minW = (int)(minMw / 1000u);
			maxW = (int)(maxMw / 1000u);
			currentW = (int)(curMw / 1000u);
			return maxW > 0 && maxW >= minW;
		}
		catch
		{
			return false;
		}
	}
	// （机主：「恢复出厂设置」不生效）： 读显卡功耗上限的**出厂默认值**（W）。
	internal static bool TryGetDefaultPowerLimitWatts(out int watts)
	{
		watts = 0;
		if (Unavailable)
		{
			return false;
		}
		try
		{
			if (nvmlDeviceGetHandleByIndex_v2(0u, out nint device) != 0 || device == 0)
			{
				return false;
			}
			if (nvmlDeviceGetPowerManagementDefaultLimit(device, out uint defaultMw) != 0)
			{
				return false;
			}
			watts = (int)(defaultMw / 1000u);
			return watts > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// ⭐ 设显卡功耗上限（W）：⭐ 先读约束并**钳制**，写入后**回读**（偏差 ≤1 W 才算成功）✅
	/// <para>⛔ 不猜范围、⛔ 不假装成功：失败就是 false，调用方按"没支持"处理 ✅</para>
	/// </summary>
	internal static bool TrySetPowerLimitWatts(int watts, out int appliedW)
	{
		appliedW = 0;
		if (Unavailable)
		{
			return false;
		}
		try
		{
			if (nvmlDeviceGetHandleByIndex_v2(0u, out nint device) != 0 || device == 0)
			{
				return false;
			}
			if (nvmlDeviceGetPowerManagementLimitConstraints(device, out uint minMw, out uint maxMw) != 0)
			{
				return false;
			}
			uint want = (uint)Math.Clamp(watts * 1000, (int)minMw, (int)maxMw);
			if (nvmlDeviceSetPowerManagementLimit(device, want) != 0)
			{
				return false;
			}
			if (nvmlDeviceGetPowerManagementLimit(device, out uint curMw) != 0)
			{
				return false;
			}
			appliedW = (int)(curMw / 1000u);
			return Math.Abs(appliedW - (int)(want / 1000u)) <= 1;
		}
		catch
		{
			return false;
		}
	}
}
