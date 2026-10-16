using System;
using System.Diagnostics;
using System.IO;
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

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9.1 · 定性日志）：⭐ GPU 温度**三个来源都没给**是否已记过 ✅
	/// （⭐ 0=没记过 · ⭐ 1=记过 ✅）。
	/// <para>⚠️ 为什么要它：⭐ 本状态机每 3 秒跑一次 ⚠️ ⇒ ⭐ 若直接 <c>Logger.WriteError</c>
	/// 就是每 3 秒刷一行 ❌；⭐ 而"⭐ 取不到 GPU 温度"⭐ 这件事**只在默认「仅错误」级别下
	/// 用 <c>WriteError</c> 才保证可见** ⚠️（⭐ <c>WriteLine</c> 要看消息里有没有错误关键词
	/// 才判级别 ⚠️）⇒ ⭐ 所以按**状态翻转只记一次** ✅。</para>
	/// </summary>
	private static int _gpuTempNoneLogged;

	/// <summary>
	/// ⭐⭐ 2026-10-16（修"GPU 温度闪「—」"· 方案 A）：⭐ **外部来源**（LHM / 加固过的 NVML ✅）
	/// 最后一次给出**有效** GPU 温度的时刻（<see cref="Environment.TickCount64"/> ✅）。
	/// <para>⚠️ 为什么需要它：⭐ <c>TryStep("lhm-read")</c> 那道闸门失败时
	/// <c>RefreshLocalMonitoringCore</c> 会**直接 return** ⚠️ ⇒ <c>ApplySensorPriority</c> 根本不跑
	/// ⇒ ⭐ 时间戳不刷新 ⇒ ⭐ 界面**闪成「—」** ❌（⭐ 明明几秒前还读到了值 ✅）。</para>
	/// <para>⭐ 修法（⭐ 与上游开源项目一致 ✅）：⭐ 闸门失败时**只碰托管状态**地续一下时间戳，
	/// ⭐ **在上限之内**继续显示上一次的好值 ✅ ⇒ ⛔ 不闪「—」。</para>
	/// <para>⚠️ <b>0 = 本次会话还没有过外部好值</b> ⇒ ⭐ **不续命** ✅
	/// （⭐ 没有旧值可显示 ⇒ ⭐ 该显示「—」就显示「—」⇒ ⛔ 不伪造 ⚠️）。</para>
	/// <para>⭐ <b>只由"厂商没给值、⭐ LHM/NVML 给了"那一条支路写</b> ✅
	/// ⇒ ⭐ GCU 机型（⭐ 厂商值恒有效 ✅）**永不写它** ⇒ ⭐ <b>行为逐位不变</b> ✅。</para>
	/// </summary>
	private static long _gpuTempExternalGoodTick;

	/// <summary>
	/// ⭐⭐ 2026-10-16（同上）：⭐ **GPU 温度续命上限**（毫秒 ✅）。
	/// <para>⚠️ 为什么必须有上限：⭐ 采样若**长期**失败（⭐ 例如 LHM 被禁用、⭐ GPU 进了低功耗态 ⚠️），
	/// 无限期显示旧值就是**撒谎** ❌ ⇒ ⭐ 超过这个时长就**停止续命** ⇒ ⭐ 界面照旧显示「—」✅，
	/// 并**默认级别可见地记一行**（⭐ 说明"⭐ 续命到期"⭐ ✅）。</para>
	/// <para>⭐ 上限**会被新的好值重置** ✅（⭐ 见 <see cref="_gpuTempExternalGoodTick"/> 的写入点 ✅）。</para>
	/// </summary>
	private const int GpuTempKeepAliveLimitMs = 30000;

	/// <summary>
	/// ⭐⭐ 2026-10-16（同上）：⭐ 续命**状态翻转位**（⭐ 0=未续命 · ⭐ 1=续命中 ✅）。
	/// <para>⚠️ 为什么要它：⭐ 本状态机每 3 秒跑一次 ⚠️ ⇒ ⭐ 直接写日志就是刷屏 ❌；
	/// ⭐ 而"⭐ 续命"⭐ 与"⭐ 续命到期"⭐ 这两件事**各自只该记一次** ✅ ⇒ ⭐ 按状态翻转记 ✅。</para>
	/// </summary>
	private static int _gpuTempKeepAliveState;

	/// <summary>
	/// ⭐⭐ 2026-10-16（冷启动 GPU 温度空窗 · <c>nvidia-smi</c> 早期来源）：
	/// <b>本会话是否已经由"真正的本地来源"（LHM / NVML）给出过有效 GPU 温度</b> ✅（⭐ 0=没有 · ⭐ 1=有 ✅）。
	/// <para>⚠️ <b>它就是 <c>nvidia-smi</c> 的【让位开关】</b>：⭐ 一旦 LHM/NVML 就绪并给出有效值，
	/// 就不再 spawn <c>nvidia-smi</c> ✅ ⇒ ⭐ 避免长期起子进程 ⚠️
	/// （⭐ 一次 spawn 约 150~300 ms ✅ —— ⭐ 本机实测 145 ms ✅）。</para>
	/// </summary>
	private static int _gpuTempRealSourceReady;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <b>我们（<c>HardwareControl</c>）上一次写进 <c>hw.GpuTemp</c> 的值</b> ✅
	/// （⭐ 0 = 本会话还没写过 ✅）。
	/// <para>⚠️⚠️ <b>为什么必须记它</b>：⭐ <see cref="ApplySensorPriority"/> 的判据原来是
	/// <c>if (hw.GpuTemp &gt; 0)</c> 就当成"⭐ 厂商值"⚠️ —— 而
	/// <see cref="MechrevoHw.MarkExternalGpuTemp"/> 写的是**同一个属性** ❌
	/// ⇒ ⭐ ⭐ <b>我们自己写的值在下一轮会被误判成厂商值</b>：
	/// ⭐ ① ⭐ LHM/NVML <b>永远不会再被读取</b>（⭐ 温度冻结在第一个外部值上 ⚠️）；
	/// ⭐ ② ⭐ 本轮的"让位"逻辑永远不触发 ❌。</para>
	/// <para>⭐ 判据：⭐ <c>hw.GpuTemp == _gpuTempFacadeWritten</c> ⇒ ⭐ 这个值是**我们**写的 ✅；
	/// ⭐ 厂商真的写了自己的值 ⇒ ⭐ 两个数不相等 ⇒ ⭐ 自动走回厂商分支 ✅，
	/// 并**在那里清成 0**（⭐ 之后每轮都正确 ✅）。</para>
	/// </summary>
	private static int _gpuTempFacadeWritten;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <c>nvidia-smi</c> <b>最近一次尝试</b>的时刻（⭐ TTL 用 ✅）。
	/// <para>⚠️ 为什么需要 TTL：⭐ 采样本每 3 秒跑一次 ⚠️ ⇒ ⭐ 不能每次都 spawn 子进程 ❌
	/// ⇒ ⭐ 学斗战者（<c>_lastGpuTemp</c> ＋ ⭐ 短 TTL ✅）✅。</para>
	/// </summary>
	private static long _gpuTempSmiLastAttemptTick;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <b>首次采样</b>的时刻（⭐ 宽限期基准 ✅）。
	/// <para>⚠️ 为什么要宽限期：⭐ 本机（⭐ GCU ✅）的厂商值由 MQTT 推送填 ⚠️ ⇒
	/// ⭐ 启动最初一两秒里 <c>hw.GpuTemp</c> 可能**还是 0** ❌ ⇒ ⭐ 若立刻尝试 <c>nvidia-smi</c>，
	/// 就会在"⭐ 厂商本来会给值"⭐ 的机器上也 spawn 一次 ⚠️ ⇒ ⭐ 违反"⭐ 逐位不变"❌。
	/// ⭐ 先等一段时间 ⇒ ⭐ 厂商值到了就 <c>hw.GpuTemp &gt; 0</c> ⇒ ⭐ 本来源**永不触发** ✅。</para>
	/// </summary>
	private static long _gpuTempSmiFirstCallTick;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <b><c>nvidia-smi</c> 可执行文件路径缓存</b> ✅
	/// （⭐ <c>null</c> = 还没找过 · ⭐ <c>""</c> = 找过、**没有** ⇒ ⭐ 静默放弃、⛔ 不再重找 ⚠️）。
	/// <para>⚠️ 非 NVIDIA 机器上这个文件不存在 ⚠️ ⇒ ⭐ <b>必须优雅退化</b>：
	/// ⭐ 静默 <c>return</c>、⛔ 不打错误日志、⛔ 不报错 ✅（⭐ 并缓存负结果 ⇒ ⭐ 不每 3 秒重找一遍 ❌）。</para>
	/// </summary>
	private static string _gpuTempSmiExePath;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <c>nvidia-smi</c> 早期来源的**日志状态翻转位** ✅
	/// （⭐ 0=未用过 · ⭐ 1=成功过 ✅ · ⭐ 2=已让位 ✅ · ⭐ 3=已停用 ⚠️）。
	/// <para>⚠️ 本状态机每 3 秒跑一次 ⚠️ ⇒ ⭐ 直接写日志就是刷屏 ❌ ⇒ ⭐ 按状态翻转只记一次 ✅。</para>
	/// </summary>
	private static int _gpuTempSmiState;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <c>nvidia-smi</c> **连续取不到值**的次数（⭐ 成功即清零 ✅）。
	/// <para>⚠️⚠️ <b>为什么必须有它 —— 防"⭐ 无限 spawn"</b>：⭐ 有 <c>nvidia-smi.exe</c>
	/// **不代表有可用的 NVIDIA 显卡** ⚠️（⭐ 例如换过显卡、⭐ 驱动残留 ✅）⇒ ⭐ 那种机器上
	/// LHM/NVML 永远不会就绪 ⇒ ⭐ "让位"永远不触发 ⇒ ⭐ 本来源就会**每 3 秒起一个子进程、
	/// 永不停止** ❌。⭐ 所以：⭐ 连续失败到 <see cref="NvidiaSmiBackoffAfterMisses"/> ⇒ ⭐ **退避**
	/// （⭐ TTL 放大到 <see cref="NvidiaSmiBackoffTtlMs"/> ✅）；⭐ 到
	/// <see cref="NvidiaSmiGiveUpAfterMisses"/> ⇒ ⭐ **彻底停用本来源** ✅。</para>
	/// </summary>
	private static int _gpuTempSmiMisses;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <b>宽限期长度</b>（毫秒 ✅）。
	/// <para>⚠️ 取值理由：⭐ 厂商（MQTT）推送通常在启动后一两秒内到 ✅ ⇒ ⭐ 5 秒足够判定
	/// "⭐ 这台机的厂商到底给不给 GPU 温度" ⚠️；⭐ 而冷启动空窗约 31 秒 ⚠️
	/// ⇒ ⭐ 仍能覆盖大部分空窗 ✅。</para>
	/// </summary>
	private const int NvidiaSmiStartupGraceMs = 5000;

	/// <summary>⭐⭐（同上）：⭐ <c>nvidia-smi</c> 取值 **TTL**（毫秒 ✅）。</summary>
	private const int NvidiaSmiTtlMs = 2000;

	/// <summary>
	/// ⭐⭐（同上）：⭐ 连续失败到这么多次 ⇒ ⭐ **退避**（⭐ TTL 放大 ✅）。
	/// <para>⚠️ 取值理由：⭐ 3 次 ≈ 启动后 5~11 秒 ✅ ⇒ ⭐ 若那时还取不到，
	/// 说明"⭐ 这台机的 <c>nvidia-smi</c> 不太可能好用"⭐ 的概率已经很高 ⚠️
	/// ⇒ ⭐ 降频而不是立刻放弃（⭐ 免得把"⭐ 开机瞬时抖动"⭐ 误判成"⭐ 永远不行"❌）。</para>
	/// </summary>
	private const int NvidiaSmiBackoffAfterMisses = 3;

	/// <summary>⭐⭐（同上）：⭐ 退避后的 TTL（毫秒 ✅）。</summary>
	private const int NvidiaSmiBackoffTtlMs = 20000;

	/// <summary>
	/// ⭐⭐（同上）：⭐ 连续失败到这么多次 ⇒ ⭐ **彻底停用本来源** ✅
	/// （⭐ 防"⭐ 永不休止地起子进程"❌ ✅）。
	/// <para>⚠️ 取值理由：⭐ 配合退避 ⇒ ⭐ 大约跑满 2~3 分钟才放弃 ✅ ⇒
	/// ⭐ 足够覆盖冷启动空窗（⭐ 约 31 秒 ✅）＋ ⭐ 启动初期抖动 ⚠️，
	/// ⭐ 又不会无限期地空转 ❌。</para>
	/// </summary>
	private const int NvidiaSmiGiveUpAfterMisses = 10;

	/// <summary>
	/// ⭐⭐（同上）：⭐ <c>nvidia-smi</c> 子进程**超时**（毫秒 ✅）。
	/// <para>⚠️ 为什么必须有：⭐ 采样本是在 <c>lhmLock</c> 里跑的 ⚠️ ⇒ ⭐ 子进程若挂住会**
	/// 一直占着锁** ❌ ⇒ ⭐ 必须封顶 ✅（⭐ 正常 150~300 ms ✅ · ⭐ 极少数最坏情况才会走满 ✅）。</para>
	/// </summary>
	private const int NvidiaSmiTimeoutMs = 3000;

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
		
		// ── ① CPU 温度：⭐⭐ 2026-10-16 **回退修正** —— ⭐ **厂商优先、⭐ LHM 兜底** ✅ ──
		//   ⚠️⚠️ 事故：⭐ 我先前写成"⭐ LHM 优先"⇒ ⭐ **用户实测 GCU 机型上 CPU/GPU 温度变成「—」** ❌
		//      ⭐ 根因：⭐ LHM 那条路**没能给出有效值**（⭐ 或给得比 GCU 晚 ⚠️）⇒
		//      ⭐ 而我把"⭐ 厂商值"放在了 **else** 分支里 ⇒ ⭐ 一旦 LHM 分支被进入但值无效，
		//      ⭐ **厂商的实时值就被挡住了** ⚠️
		//   ⇒ ⭐ ⭐ 现在改为 ⭐ **厂商值优先**（⭐ 只要它有效就照原样用 ✅），
		//      ⭐ **只有厂商没给值时**才用 LHM ✅
		//   ⭐ 这样 ⭐ **GCU 机型的显示与改动前【逐位一致】** ✅（⭐ 先把回归修掉 ✅），
		//      ⭐ 而非 GCU 后端（⭐ 本来就没有厂商值 ✅）依旧能拿到 LHM 的温度 ✅
		//   📌 待办：⭐ "⭐ 所有机型优先用 LHM"⭐ 这条 ⭐ **等 LHM 那条路被证明稳定后再恢复** ⚠️
		//      （⭐ 要恢复时 ⭐ 把下面 if/else 顺序对调即可 ✅）
		try
		{
			int cpuFromVendor = (hw.BaolongdaCpuTemp > 0) ? hw.BaolongdaCpuTemp : hw.CpuTemp;
			if (cpuFromVendor > 0)
			{
				// ⭐ 厂商（⭐ GCU 推送 / ⭐ 宝龙达功能号 22 ✅）给了有效值 ⇒ ⭐ 照原样用 ✅
				cpuTemp = cpuFromVendor;
			}
			else
			{
				// ⭐ 厂商没给 ⇒ ⭐ 才用 LM 自带的 LHM 兜底 ✅（⭐ 非 GCU 后端主要靠这一支 ✅）
				float? fromLhm = lhmMonitor.CpuTemperature;
				if (fromLhm.HasValue && fromLhm.Value > 0f)
				{
					cpuTemp = (int)Math.Round(fromLhm.Value);
					if (Interlocked.Exchange(ref _lhmCpuTempLogged, 1) == 0)
					{
						Logger.WriteLine($"传感器兜底：⭐ 厂商未提供 CPU 温度 ⇒ 改用 **LM 自带的 LHM**"
							+ $"（传感器={lhmMonitor.CpuTemperatureSensorName}，值={cpuTemp}℃）✅");
					}
				}
			}
		}
		catch
		{
		}
		// ── ② GPU 温度：⭐⭐ 同样 **厂商优先、⭐ LHM ⇒ ⭐ NVML 兜底** ✅ ──
		try
		{
			bool gpuTempFilled = false;
			// ⭐⭐ 2026-10-16（0.291.9.1）：⭐ 记录"⭐ 这个值是不是厂商给的" ✅ ——
			//   ⭐ 只有**不是**厂商给的（⭐ = LHM/NVML 补的 ✅）才需要写回门面层 ✅
			//   ⇒ ⭐ GCU 机型（⭐ 厂商值恒有效 ✅）走老分支 ⇒ ⭐ 行为逐位不变 ✅
			bool gpuTempFromVendor = false;
			// ⭐⭐ 2026-10-16（nvidia-smi 早期来源）：⭐ 门面层这个值**是不是我们自己写的** ⚠️ ——
			//   ⚠️ 必须分清，否则 `MarkExternalGpuTemp`（⭐ 外部来源 ✅）写的值在下一轮会被
			//      误判成"⭐ 厂商值"❌ ⇒ ⭐ LHM/NVML 永远不会再被读取（⭐ 温度冻结在第一个外部值上 ⚠️）
			//      ＋ ⭐ nvidia-smi 的"让位"永远不触发 ❌。
			//   ⭐ 判据：⭐ "⭐ 我们记过、⭐ 且现在还是那个数"⭐ ⇒ ⭐ 是我们写的 ✅；
			//      ⭐ 厂商写了自己的值 ⇒ ⭐ 两个数不等 ⇒ ⭐ 自动走回厂商分支 ✅。
			bool facadeIsOurs = IsGpuTempFacadeWrittenByUs(hw);
			if (hw.GpuTemp > 0 && !facadeIsOurs)
			{
				gpuTemp = hw.GpuTemp; // ⭐ 厂商（⭐ GCU ✅）优先 ⇒ ⭐ 与改动前一致 ✅
				gpuTempFilled = true;
				gpuTempFromVendor = true;
			}
			else
			{
				// ⭐⭐ 2026-10-16（nvidia-smi 早期来源）：⭐ 本支 = "⭐ 门面层里没有【厂商】值" ✅ ——
				//   ① ⭐ 先问**真正的本地来源**（⭐ LHM ⇒ ⭐ NVML ✅）⇒ ⭐ 它们一旦给值就让 nvidia-smi 让位 ✅
				//   ② ⭐ 都没给、⭐ 而门面层里还有**我们自己写的**早期值 ⇒ ⭐ 沿用它 ✅
				float? lhmGpu = lhmMonitor.GpuTemperature;
				if (lhmGpu.HasValue && lhmGpu.Value > 0f)
				{
					gpuTemp = (int)Math.Round(lhmGpu.Value);
					gpuTempFilled = true;
					// ⭐ LHM 就绪 ⇒ ⭐ nvidia-smi 让位（⭐ 不再 spawn 子进程 ✅）
					MarkGpuTempRealSourceReady();
				}
				if (!gpuTempFilled && !NvmlHelper.Unavailable)
				{
					float? fromNvml = NvmlHelper.GetGpuTemperature();
					if (fromNvml.HasValue && fromNvml.Value > 0f)
					{
						gpuTemp = (int)Math.Round(fromNvml.Value);
						gpuTempFilled = true;
						// ⭐ NVML 就绪 ⇒ ⭐ nvidia-smi 让位（⭐ 不再 spawn 子进程 ✅）
						MarkGpuTempRealSourceReady();
					}
				}
				if (!gpuTempFilled && facadeIsOurs && hw.GpuTemp > 0)
				{
					// ⭐ LHM/NVML 都还没就绪 ⚠️ ⇒ ⭐ 沿用 nvidia-smi 早期来源写的那个值 ✅
					//   ⇒ ⭐ 冷启动那 31 秒不再显示「—」✅（⭐ 值是真的、⭐ 来源已在日志里记明 ✅）
					gpuTemp = hw.GpuTemp;
					gpuTempFilled = true;
				}
			}
			// ⭐⭐ 2026-10-16（0.291.9 · 修"宝龙达机型 GPU 温度显示为「—」"）：
			//   ⭐ 界面那格 GPU 温度的判据是 ⭐ `IsSensorInfoFresh(cpu: false)` ⚠️
			//      ⇒ ⭐ 它读 `_gpuInfoReceivedAt` ⚠️ —— ⭐ 而那个时间戳**只有 GCU/MQTT 推送会写** ❌
			//      ⇒ ⭐ 宝龙达（⭐ 没有 GCU 推送 ⚠️）恒判"不新鲜" ⇒ ⭐ **显示「—」** ❌
			//   ⚠️ 反证：⭐ `_cpuInfoReceivedAt` ⭐ **宝龙达那条路会写** ✅（⭐ `RefreshBaolongdaSensors` ✅）
			//      ⇒ ⭐ ⭐ **所以真机上 CPU 温度正常、GPU 温度是「—」** ✅ —— ⭐ 这个不对称就是证据 ✅
			//   ⭐ 修法：⭐ **这次真拿到 GPU 温度了** ⇒ ⭐ 才打时间戳 ✅
			//      ⚠️ 只在 `gpuTempFilled` 为真时打 ⇒ ⭐ 没拿到就不打 ⇒ ⭐ 界面照旧显示「—」✅（⛔ 不谎报 ✅）
			if (gpuTempFilled)
			{
				if (gpuTempFromVendor)
				{
					// ⭐ 老路：⭐ 厂商值已经在 `hw` 里 ✅ ⇒ ⭐ 只需要时间戳（⭐ 与改动前逐字一致 ✅）
					hw.MarkGpuInfoReceived();
					// ⭐⭐ 2026-10-16（nvidia-smi 早期来源）：⭐ 厂商接管了门面层 ⇒ ⭐ 把"⭐ 我们上次写的值"⭐ 清掉 ✅
					//   ⇒ ⭐ 之后每一轮都会**正确地**走厂商分支 ✅（⭐ 也让 nvidia-smi 那边彻底让位 ✅）
					Interlocked.Exchange(ref _gpuTempFacadeWritten, 0);
				}
				else
				{
					// ⭐⭐ 2026-10-16（0.291.9.1 · 真机反馈"⭐ 风扇转速好了、⭐ GPU 温度还是「—」"⚠️）：
					//   ⚠️⚠️ **根因**：⭐ 主界面那行遥测（⭐ `SettingsForm.TelemetryParts` ✅）读的是
					//      ⭐ ⭐ **`hw.GpuTemp`** ⚠️ —— ⭐ 而本方法原来**只写自己这个
					//      `HardwareControl.gpuTemp` 静态字段** ❌ ⇒ ⭐ LHM/NVML 明明取到了温度、
					//      ⭐ ⭐ **却永远送不到主界面** ⇒ ⭐ 恒显示「—」 ❌
					//      ⚠️ 而风扇能好、⭐ 温度不好 ⚠️ —— ⭐ 因为风扇那条走的是
					//      `hw.CpuFanRpm`（⭐ 门面层属性 ✅），⭐ 两条路**根本不是同一处** ✅
					//   ⇒ ⭐ 修法：⭐ **把外部来源的值写回门面层** ✅（⭐ 连值带时间戳 ✅）
					hw.MarkExternalGpuTemp(gpuTemp);
					// ⭐⭐ 2026-10-16（nvidia-smi 早期来源）：⭐ 记下"⭐ 门面层这个值是【我们】写的" ✅
					//   ⚠️ 必须记 —— 否则下一轮 `hw.GpuTemp > 0` 会把它**误判成厂商值** ❌
					//      ⇒ ⭐ LHM/NVML 永远不再被读取（⭐ 温度冻结在第一个外部值上 ⚠️）
					//      ⇒ ⭐ 且 nvidia-smi 的"让位"永远不触发 ❌。
					Interlocked.Exchange(ref _gpuTempFacadeWritten, gpuTemp);
					// ⭐⭐ 2026-10-16（方案 A）：⭐ 记下"⭐ 外部来源最后一次给出有效值的时刻" ✅
					//   ⇒ ⭐ 闸门失败时可以在此基础上**在上限之内**续命（⛔ 不闪「—」）✅
					//   ⭐ 同时把续命状态复位 ⇒ ⭐ "⭐ 恢复过、⭐ 之后又失败"⭐ 会重新记一次
					//      （⭐ 不刷屏：⭐ 必须真的恢复过一次才会重记 ✅）
					Interlocked.Exchange(ref _gpuTempExternalGoodTick, Environment.TickCount64);
					Interlocked.Exchange(ref _gpuTempKeepAliveState, 0);
					Logger.WriteLineIfChanged("bd-gpu-temp-ext",
						$"GPU 温度（⭐ 非 GCU 来源）：⭐ 厂商未提供 ⇒ ⭐ 由 LHM/NVML 取到 {gpuTemp}℃"
						+ " ⇒ ⭐ 已写回 hw.GpuTemp ＋ ⭐ 打时间戳 ✅（⭐ 主界面那行读的就是它 ✅）");
					// ⭐ 状态翻转（⭐ 之前三个来源都没给 ⇒ 现在有了 ✅）⇒ ⭐ 默认级别可见地记一行 ✅
					if (Interlocked.Exchange(ref _gpuTempNoneLogged, 0) == 1)
					{
						Logger.WriteError($"GPU 温度：⭐ 已恢复 ✅ —— 现在能取到 {gpuTemp}℃"
							+ "（⭐ 由 LHM/NVML 提供 ✅；⭐ 之前有一段三个来源都没给 ⚠️）");
					}
				}
			}
			else
			{
				// ⭐⭐ 2026-10-16（0.291.9.1 · 定性日志）：⭐ 三个来源全没给 ⇒ ⭐ 界面会显示「—」✅
				//   ⭐⚠️ 用 `WriteError` ⇒ ⭐ **默认「仅错误」级别下也会落盘** ✅
				//     （⭐ 机主不用特意开 All 级 ✅；⭐ 而 `WriteLine` 要看消息里有没有错误关键词
				//      ⭐ 才判级别 ⚠️ ⇒ ⭐ 不能保证可见 ❌）
				//   ⭐ 用"⭐ 状态翻转"⭐ 只记一次 ⇒ ⚠️ **不会每 3 秒刷屏** ✅
				if (Interlocked.Exchange(ref _gpuTempNoneLogged, 1) == 0)
				{
					Logger.WriteError($"GPU 温度：⚠️ 厂商/LHM/NVML **三个来源都没给值** ⇒ 界面会显示「—」"
						+ $"（⭐ 厂商值={hw.GpuTemp} · ⭐ LHM={(lhmMonitor.GpuTemperature.HasValue ? lhmMonitor.GpuTemperature.Value.ToString("F0") : "无")}"
						+ $" · ⭐ NVML已隔离={NvmlHelper.Unavailable}）；⭐ 本条只在开始取不到的那一刻记一次 ✅");
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
	/// ⭐⭐ 2026-10-16（修"GPU 温度闪「—」"· 方案 A）：⭐
	/// <c>TryStep("lhm-read")</c> 闸门失败时，**只碰托管状态**地给 GPU 温度续一下时间戳 ✅。
	///
	/// <para>⚠️⚠️ <b>为什么只能碰托管状态</b>：⭐ <c>TryStep("lhm-read")</c> 那道闸门的注释写着
	/// 「⭐ 所有可能进入原生层（LHM / NVML / EC / WMI）的采样都从这里走」⚠️ ——
	/// ⭐ 本方法**绝不进入任何原生层** ✅（⭐ 只读一个 <c>long</c> 字段 ＋ ⭐ 调
	/// <see cref="MechrevoHw.MarkGpuInfoReceived"/> 打时间戳 ✅）
	/// ⇒ ⭐ 所以可以安全地放在**失败分支**里，⛔ 不需要 <c>TryStep</c> 包裹，
	/// ⛔ 也不会削弱那道闸门的原生层 AV 防护 ✅。</para>
	///
	/// <para>⭐ <b>语义</b>：⭐ "⭐ 我几秒前确实读到过有效温度 ✅ ⇒ ⭐ 这一次没读到
	/// ⭐ 不足以判定传感器没了 ⚠️ ⇒ ⭐ 先沿用上一次的好值 ✅" ——
	/// ⭐ 与斗战者（<c>_lastGpuTemp</c> ＋ ⭐ 短 TTL ✅）／蛟龙（⭐ 沿用上次值 ✅）
	/// 等上游开源项目的做法一致 ✅。</para>
	///
	/// <para>⚠️ <b>三条硬边界</b>：
	/// ⭐ ① ⭐ <c>_gpuTempExternalGoodTick == 0</c>（⭐ 本次会话还没有过外部好值 ⚠️）⇒ ⭐ **不续命**
	/// （⭐ 没有旧值可显示 ⇒ ⭐ 该显示「—」就显示「—」⇒ ⛔ 不伪造 ✅）；
	/// ⭐ ② ⭐ 超过 <see cref="GpuTempKeepAliveLimitMs"/> ⇒ ⭐ **停止续命** ⇒ ⭐ 照旧「—」✅；
	/// ⭐ ③ ⭐ 门面层里没有可用值（⭐ <c>hw.GpuTemp &lt;= 0</c> ⚠️）⇒ ⭐ 不续命 ✅。</para>
	/// </summary>
	private static void KeepAliveGpuTempOnSampleFailure()
	{
		// (a) 本方法整体再兜一层：只碰托管状态 ⇒ 理论上不会抛，但绝不冒泡到 UI 线程。
		try
		{
			long lastGoodTick = Interlocked.Read(in _gpuTempExternalGoodTick);
			if (lastGoodTick == 0L)
			{
				// ⭐ 本次会话还没有过外部来源的好值 ⇒ ⛔ 没有旧值可显示 ⇒ 不续命 ✅
				return;
			}
			MechrevoHw hw;
			try
			{
				hw = Program.hw;
			}
			catch
			{
				return;
			}
			if (hw == null || hw.GpuTemp <= 0)
			{
				// ⭐ 门面层里没有可用值 ⇒ ⛔ 不续命（⭐ 免得把「—」变成假的有效 ✅）
				return;
			}
			long ageMs = Environment.TickCount64 - lastGoodTick;
			if (ageMs > GpuTempKeepAliveLimitMs)
			{
				// ── 超过上限 ⇒ 停止续命 ⇒ 界面照旧显示「—」⇒ 只记一次（状态翻转）──
				if (Interlocked.Exchange(ref _gpuTempKeepAliveState, 0) == 1)
				{
					Logger.WriteError($"GPU 温度：⚠️ **续命已到期**（⭐ 最近一次【外部来源】的有效读数是 {ageMs / 1000} 秒前 ⚠️）"
						+ $" ⇒ ⭐ **不再沿用旧值**（⛔ 不再无限期显示它 ⚠️，⭐ 上限 {GpuTempKeepAliveLimitMs / 1000} 秒）；"
						+ "⭐ 若此刻没有别的来源，界面就显示「—」✅（⭐ 下次一取到新的有效值就恢复 ✅）");
				}
				return;
			}
			// ── 在上限之内 ⇒ 打时间戳 ⇒ 界面继续显示上一次的好值（⛔ 不闪「—」）──
			hw.MarkGpuInfoReceived();
			if (Interlocked.Exchange(ref _gpuTempKeepAliveState, 1) == 0)
			{
				Logger.WriteError($"GPU 温度：⭐ 本次采样失败（⭐ lhm-read 闸门 ⚠️）⇒ **暂用上一次的好值"
					+ $" {hw.GpuTemp}℃ 继续显示** ✅（⭐ 已续命 {ageMs / 1000} 秒，"
					+ $"⭐ 上限 {GpuTempKeepAliveLimitMs / 1000} 秒 ⚠️；"
					+ "⛔ 未进入 LHM/NVML/EC/WMI 等原生层 ✅）；⭐ 本条只记一次 ✅");
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// ⭐⭐（同上）：⭐ 标记"⭐ **真正的本地来源**（LHM / NVML）已经就绪" ✅ ⇒ ⭐ <c>nvidia-smi</c>
	/// 从此**让位**（⭐ <see cref="ShouldTryEarlyNvidiaSmi"/> 第 ① 条 ✅）。
	/// <para>⚠️ 让位日志只在"⭐ 确实用过 <c>nvidia-smi</c>"⭐（⭐ <see cref="_gpuTempSmiState"/> 为 1 ✅）
	/// 时记一次 ⚠️ ⇒ ⭐ GCU 机型（⭐ 从没用过、⭐ 状态恒为 0 ✅）<b>零日志影响</b> ✅
	/// ⇒ ⭐ 不影响"⭐ 逐位不变"⭐ 的结论 ✅。</para>
	/// </summary>
	private static void MarkGpuTempRealSourceReady()
	{
		Interlocked.Exchange(ref _gpuTempRealSourceReady, 1);
		if (Interlocked.CompareExchange(ref _gpuTempSmiState, 2, 1) == 1)
		{
			Logger.WriteError("GPU 温度：⭐ nvidia-smi 早期来源**已让位** ✅（⭐ LHM/NVML 已就绪并给出有效值 ✅）"
				+ " ⇒ ⭐ 之后不再 spawn 子进程 ✅；⭐ 本条只记一次 ✅");
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（冷启动 GPU 温度空窗 · <c>nvidia-smi</c> 早期来源）：
	/// ⭐ <b>门面层现在的 <c>hw.GpuTemp</c> 是不是【我们自己】写进去的</b> ✅。
	/// <para>⚠️ 判据：⭐ 我们记过 <see cref="_gpuTempFacadeWritten"/>、⭐ 而且它**现在还是这个数** ✅
	/// ⇒ ⭐ 说明中间**没有别人**（⭐ 厂商 ✅）写过 ⇒ ⭐ 不能当厂商值 ⚠️。</para>
	/// </summary>
	private static bool IsGpuTempFacadeWrittenByUs(MechrevoHw hw)
	{
		if (hw == null)
		{
			return false;
		}
		int written = Volatile.Read(ref _gpuTempFacadeWritten);
		return written > 0 && hw.GpuTemp == written;
	}

	/// <summary>
	/// ⭐⭐（同上）：⭐ 找 <c>nvidia-smi.exe</c> ✅（⭐ 结果**缓存**，⭐ 含负结果 ✅）。
	/// <para>⚠️ 为什么<b>不靠 PATH</b>：⭐ 斗战者参考实现（<c>BellatorFanControl.CandidatePaths()</c> ✅）
	/// 就是**显式枚举安装目录**的 ✅；⭐ 只靠 PATH 很脆 ⚠️（⭐ 我们的进程环境未必带它 ✅）。</para>
	/// <para>⭐ 候选顺序：⭐ ① <c>%SystemRoot%\System32</c>（⭐ 现代驱动放这里 ✅ ——
	/// ⭐ ⚠️ 本机实测**只有这一处存在**：两个 <c>NVIDIA Corporation\NVSMI</c> 都没有 ✅）
	/// ⇒ ⭐ ② <c>%ProgramFiles%\NVIDIA Corporation\NVSMI</c>（⭐ 老驱动 ✅）
	/// ⇒ ⭐ ③ <c>%ProgramFiles(x86)%\…</c> ✅。</para>
	/// <para>⛔ 找不到 ⇒ ⭐ 返回 <c>null</c> 并**缓存空串** ⇒ ⭐ 后续调用**一次都不再枚举** ✅、
	/// ⛔ 不打日志、⛔ 不报错 ✅（⭐ 非 NVIDIA 机器就是这样优雅退化 ✅）。</para>
	/// </summary>
	private static string ResolveNvidiaSmiPath()
	{
		string cached = _gpuTempSmiExePath;
		if (cached != null)
		{
			return (cached.Length == 0) ? null : cached; // ⭐ "" = 已确认没有 ⇒ 静默放弃 ✅
		}
		string found = null;
		try
		{
			string[] candidates =
			{
				Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
			};
			foreach (string candidate in candidates)
			{
				if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
				{
					found = candidate;
					break;
				}
			}
		}
		catch
		{
			found = null;
		}
		_gpuTempSmiExePath = found ?? "";
		return found;
	}

	/// <summary>
	/// ⭐⭐（同上）：⭐ 向 <c>nvidia-smi</c> 要一次 GPU 温度 ✅。
	/// <para>⭐⭐ <b>为什么它能治那 31 秒</b>：⭐ 我们进程内的 NVML 要等启动后约 31 秒才第一次
	/// 初始化成功 ⚠️（⭐ 真机日志：<c>21:42:31</c> 启动 ⇒ ⭐ <c>21:43:03.959</c> 才 <c>NVML Init: 0</c> ✅）；
	/// 而 <c>nvidia-smi</c> 是 <b>另一个进程</b> ✅ ⇒ ⭐ 不受我们进程内的初始化时序影响 ✅。</para>
	/// <para>⭐ 命令：⭐ <c>--query-gpu=temperature.gpu --format=csv,noheader,nounits</c> ⇒
	/// ⭐ 一行纯数字（⭐ ⚠️ 本机实测 145 ms、⭐ 输出 <c>80</c> ✅）⇒
	/// ⭐ 解析失败一律当"⭐ 取不到"⭐ 处理，⛔ <b>不猜值</b> ✅。</para>
	/// <para>⚠️ 读取顺序刻意是"⭐ <b>先等退出、⭐ 再读输出</b>"⭐ —— ⭐ <c>nvidia-smi</c> 的输出只有几十字节 ✅
	/// ⇒ ⭐ 不可能写满管道 ⇒ ⭐ 不会有经典的"⭐ 先读后等"⭐ 死锁 ✅。</para>
	/// </summary>
	private static int? QueryNvidiaSmiTemperature(string exePath)
	{
		try
		{
			using Process process = new Process();
			process.StartInfo.FileName = exePath;
			process.StartInfo.Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits";
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.Start();
			// ⚠️ 超时 ⇒ ⭐ 杀子进程 ＋ ⭐ 当"取不到" ✅（⛔ 绝不无限期占着 lhmLock ⚠️）
			if (!process.WaitForExit(NvidiaSmiTimeoutMs))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				return null;
			}
			if (process.ExitCode != 0)
			{
				return null;
			}
			string stdout = process.StandardOutput.ReadToEnd();
			string firstLine = null;
			foreach (string line in stdout.Split('\n'))
			{
				string trimmed = line.Trim();
				if (trimmed.Length > 0)
				{
					firstLine = trimmed;
					break;
				}
			}
			// ⭐ 理据：⭐ 0℃ 以下 / ⭐ 150℃ 以上都不是"⭐ 真实 GPU 温度"⭐ ⇒ ⭐ 当取不到 ✅（⛔ 不猜 ⚠️）
			if (firstLine != null && int.TryParse(firstLine, out int celsius) && celsius > 0 && celsius < 150)
			{
				return celsius;
			}
			return null;
		}
		catch
		{
			// ⭐ 文件不在 / ⭐ 权限不足 / ⭐ 非 NVIDIA ⇒ ⭐ 一律静默"取不到" ✅（⛔ 不打错误日志 ⚠️）
			return null;
		}
	}

	/// <summary>
	/// ⭐⭐（同上）：⭐ <b>该不该试一次 <c>nvidia-smi</c></b> ✅。
	/// <para>⚠️ 五道判据，⭐ 任何一条不满足就**一次都不 spawn** ✅：
	/// ⭐ ① <b>让位</b> —— LHM/NVML 已给过有效值 ⇒ ⭐ 不再调用 ✅；
	/// ⭐ ② <b>宽限期</b> —— ⭐ 启动后前 <see cref="NvidiaSmiStartupGraceMs"/> ms 不试 ⚠️
	/// （⭐ 先让厂商（MQTT）有机会填值 ⇒ ⭐ 这是"⭐ 非宝龙达 / ⭐ GCU 逐位不变"⭐ 的第一道保护 ✅）；
	/// ⭐ ③ <b>TTL</b> —— ⭐ 距上次尝试不足 <see cref="NvidiaSmiTtlMs"/> ms ⇒ ⭐ 不重复 spawn ✅；
	/// ⭐ ④ <b>厂商已给值</b> —— ⭐ 不做 ✅（⭐ 用 <see cref="IsGpuTempFacadeWrittenByUs"/> 区分
	/// "⭐ 是不是我们自己写的"⚠️ —— ⭐ 第二道保护 ✅）；
	/// ⭐ ⑤ <b>可执行文件存在</b> ⚠️ —— ⭐ 非 NVIDIA 机器静默放弃 ✅。</para>
	/// </summary>
	private static bool ShouldTryEarlyNvidiaSmi()
	{
		// ① 让位：LHM/NVML 已就绪并给过有效值 ⇒ 本来源功成身退
		if (Volatile.Read(ref _gpuTempRealSourceReady) == 1)
		{
			return false;
		}
		int misses = Volatile.Read(ref _gpuTempSmiMisses);
		// ①b 彻底停用：连续失败太多次 ⇒ 推断本机没有可用的 NVIDIA 工具 ⚠️ ⇒ 不再 spawn ✅
		//   ⚠️ 这是"防无限起子进程"的兜底（⭐ 见 _gpuTempSmiMisses 的注释 ✅）
		if (misses >= NvidiaSmiGiveUpAfterMisses)
		{
			return false;
		}
		long now = Environment.TickCount64;
		// ② 宽限期（首次调用时记基准）
		long firstCallTick = Interlocked.Read(in _gpuTempSmiFirstCallTick);
		if (firstCallTick == 0L)
		{
			Interlocked.CompareExchange(ref _gpuTempSmiFirstCallTick, now, 0L);
			firstCallTick = Interlocked.Read(in _gpuTempSmiFirstCallTick);
		}
		if (now - firstCallTick < NvidiaSmiStartupGraceMs)
		{
			return false;
		}
		// ③ TTL（⭐ 若已连续失败到退避阈值 ⇒ 用放大的 TTL ⚠️ —— 免得一直起子进程 ❌）
		int ttlMs = (misses >= NvidiaSmiBackoffAfterMisses) ? NvidiaSmiBackoffTtlMs : NvidiaSmiTtlMs;
		long lastAttemptTick = Interlocked.Read(in _gpuTempSmiLastAttemptTick);
		if (lastAttemptTick != 0L && now - lastAttemptTick < ttlMs)
		{
			return false;
		}
		// ④ 厂商已给值 ⇒ 不做
		MechrevoHw hw;
		try
		{
			hw = Program.hw;
		}
		catch
		{
			return false;
		}
		if (hw == null)
		{
			return false;
		}
		if (hw.GpuTemp > 0 && !IsGpuTempFacadeWrittenByUs(hw))
		{
			return false; // ⭐ 厂商值有效 ⇒ ⭐ 永远不需要 nvidia-smi（⭐ GCU 机型就是这一支 ✅）
		}
		// ⑤ 可执行文件存在
		return ResolveNvidiaSmiPath() != null;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（冷启动 GPU 温度空窗）：⭐ <b><c>nvidia-smi</c> 早期来源</b> ✅ ——
	/// 排在两道闸门（⭐ <c>lhm-update</c> / ⭐ <c>lhm-read</c> ✅）**之外**的第一步 ✅。
	///
	/// <para>⚠️⚠️ <b>为什么必须排在外面</b>：⭐ 那两道闸门**失败就 <c>return</c>** ⚠️ ⇒
	/// ⭐ <see cref="ApplySensorPriority"/> 根本不跑 ❌ ⇒ ⭐ 冷启动那 31 秒里
	/// <b>没有任何代码有机会取 GPU 温度</b> ❌ ⇒ ⭐ 界面恒显示「—」⚠️。
	/// ⭐ 所以本来源**绝不能塞进 <c>lhm-read</c> 的失败分支**（⭐ 那正是"⭐ 跳过"⭐ 的地方 ❌），
	/// 而要**独立成一步、⭐ 排在闸门之前** ✅。</para>
	///
	/// <para>⚠️ 但**闸门本身与它的位置一字未动** ✅ —— ⛔ 没有把 <see cref="ApplySensorPriority"/>
	/// 提到闸门之前（⭐ 那会削弱原生层 AV 防护 ❌）；⭐ 本方法**自己**被 <c>TryStep</c> 包着 ✅。</para>
	///
	/// <para>⭐ <b>记账与既有"⭐ 外部来源"⭐ 完全同款</b>：⭐ 写回门面层（<c>MarkExternalGpuTemp</c> ✅）＋
	/// ⭐ 打 <see cref="_gpuTempExternalGoodTick"/> ⇒ ⭐ 方案 A 的"续命"也能覆盖它 ✅。</para>
	/// </summary>
	private static void RefreshEarlyGpuTempFromNvidiaSmi()
	{
		// ⭐ 用 TryStep 包裹（⭐ 它起外部进程 ⇒ ⭐ 必须与原生层采样同等待遇 ✅）；
		//   ⚠️ 但 countsAsHealthFailure: false —— ⭐ nvidia-smi 取不到跟 LHM/NVML 那条链**无关** ⚠️
		//      ⇒ ⭐ 绝不能因为它失败就触发"⭐ 采样链坏了 ⇒ ⭐ 重建 / ⭐ 降级"❌。
		TryStep(delegate
		{
			if (!ShouldTryEarlyNvidiaSmi())
			{
				return;
			}
			Interlocked.Exchange(ref _gpuTempSmiLastAttemptTick, Environment.TickCount64);
			string exePath = ResolveNvidiaSmiPath();
			if (exePath == null)
			{
				return;
			}
			int? celsius = QueryNvidiaSmiTemperature(exePath);
			if (!celsius.HasValue)
			{
				// ⭐ 取不到 ⇒ ⭐ 什么都不做 ⇒ ⭐ 界面照旧（⭐ 该「—」就「—」⇒ ⛔ 不谎报 ✅）
				//   ⚠️ 但要**计数** ⇒ ⭐ 到阈值就退避 / ⭐ 最终停用 ✅（⭐ 防"⭐ 无限起子进程"❌）
				int misses = Interlocked.Increment(ref _gpuTempSmiMisses);
				if (misses == NvidiaSmiGiveUpAfterMisses && Interlocked.CompareExchange(ref _gpuTempSmiState, 3, 0) == 0)
				{
					Logger.WriteError($"GPU 温度：⚠️ **nvidia-smi 早期来源已停用**（⭐ 连续 {misses} 次取不到值 ⇒ "
						+ "推断本机没有可用的 NVIDIA 工具 ⚠️）⇒ ⭐ 之后**不再 spawn 子进程** ✅；"
						+ "⭐ 在 LHM/NVML 就绪之前界面仍会显示「—」✅（⛔ 不谎报 ⚠️）；⭐ 本条只记一次 ✅");
				}
				return;
			}
			// ⭐ 成功 ⇒ ⭐ 失败计数清零（⭐ 退避 / 停用判定跟着复位 ✅）
			Interlocked.Exchange(ref _gpuTempSmiMisses, 0);
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
			// ⭐ 复核：⭐ 厂商可能就在这几百毫秒里刚给了值 ⇒ ⭐ 立刻让位、⛔ 不覆盖它 ✅
			if (hw.GpuTemp > 0 && !IsGpuTempFacadeWrittenByUs(hw))
			{
				return;
			}
			hw.MarkExternalGpuTemp(celsius.Value);
			Interlocked.Exchange(ref _gpuTempFacadeWritten, celsius.Value);
			Interlocked.Exchange(ref _gpuTempExternalGoodTick, Environment.TickCount64);
			Interlocked.Exchange(ref _gpuTempKeepAliveState, 0);
			// ⭐ 状态翻转：⭐ 之前"⭐ 三个来源都没给"⭐ ⇒ ⭐ 现在有了 ⇒ ⭐ 复位并记一行 ✅
			if (Interlocked.Exchange(ref _gpuTempNoneLogged, 0) == 1)
			{
				Logger.WriteError($"GPU 温度：⭐ 已恢复 ✅ —— 现在能取到 {celsius.Value}℃（⭐ 由 nvidia-smi 早期来源提供 ✅）");
			}
			if (Interlocked.CompareExchange(ref _gpuTempSmiState, 1, 0) == 0)
			{
				Logger.WriteError($"GPU 温度：⭐ 冷启动空窗已由 **nvidia-smi 早期来源**补上 ✅ —— 取到 {celsius.Value}℃"
					+ "（⭐ 本进程自己的 NVML 要等启动后约 31 秒才首次就绪 ⚠️ ⇒ ⭐ 这段时间原来显示「—」❌）；"
					+ "⭐ 一旦 LHM/NVML 就绪本来源就自动让位 ✅；⭐ 本条只记一次 ✅");
			}
		}, "nvidia-smi-early", countsAsHealthFailure: false);
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
		// ⭐⭐ 2026-10-16（冷启动 GPU 温度空窗）：⭐ `nvidia-smi` 早期来源 ✅ ——
		//   ⚠️ 刻意排在**两道闸门【之外】**：⭐ 下面 `lhm-update`、⭐ 再下面 `lhm-read` ⚠️ ——
		//      任何一道失败都会 `return` ⇒ ⭐ `ApplySensorPriority` 不跑 ⇒ ⭐ 那 31 秒里
		//      **没有任何代码有机会取 GPU 温度** ❌ ⇒ ⭐ 界面恒「—」⚠️。
		//   ⭐ 放在这里 ⇒ ⭐ 即使两道闸门都失败、⭐ 也能把早期值补进门面层 ✅。
		//   ⛔ 闸门本身与它的位置**一字未动** ✅（⭐ 本步自己带 TryStep ✅）。
		//   ⚠️ 位置理由：⭐ 排在 `lhm == null` 早退**之后** ⇒ ⭐ "⭐ LHM 被整体关掉"⭐ 的配置下
		//      行为**逐位不变** ✅（⭐ 不因本来源而多出子进程 ⚠️）。
		RefreshEarlyGpuTempFromNvidiaSmi();
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
			// ⭐⭐ 2026-10-16（修"GPU 温度闪「—」"· 方案 A）：⭐ 闸门失败 ⇒
			//   ⭐ **只碰托管状态**地给 GPU 温度续一下时间戳 ✅（⛔ 本调用不进原生层 ⚠️）。
			//   ⚠️ 以前这里**直接 return** ⇒ `ApplySensorPriority` 根本不跑 ⇒ ⭐ 时间戳不刷新
			//      ⇒ ⭐ 界面**闪成「—」** ❌（⭐ 明明几秒前还读到了值 ✅）。
			//   ⭐⭐ 注意：⭐ **闸门本身与它的位置【一字未动】** ✅ ——
			//      ⛔ 没有把 `ApplySensorPriority` 提到闸门之前（⭐ 那会削弱原生层 AV 防护 ❌）。
			KeepAliveGpuTempOnSampleFailure();
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
