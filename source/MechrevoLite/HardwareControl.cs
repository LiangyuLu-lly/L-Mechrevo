using System;
using System.Collections.Generic;
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

	/// <summary>⭐ 2026-10-17：ACPI 热区兜底只记一次日志。</summary>
	private static int _acpiCpuTempLogged;

	/// <summary>⭐ ACPI 热区温度缓存的上次读取时刻（TickCount64）。</summary>
	private static long _acpiTempLastTicks;

	/// <summary>⭐ ACPI 热区温度缓存值（℃；0 = 无/失败）。</summary>
	private static int _acpiTempCached;

	/// <summary>
	/// ⭐ 2026-10-17（30 系宝龙达 CPU 温度不识别 · 第三档兜底）：
	/// 读 <c>root\WMI:MSAcpi_ThermalZoneTemperature</c>（**免驱动**，只需管理员）。
	/// <para>⚠️ 单位是 0.1K ⇒ (raw / 10) − 273.15；取多个热区的**最高值**（通常有 CPU 与主板两个）。</para>
	/// <para>⚠️ 用 <see cref="System.Management.ManagementObjectSearcher" />（⛔ 不用 Get-CimInstance ——
	/// 本项目 `DiagnosticBaolongdaInfo.cs:22` 记过：那个口径查不到会**假否定** ⚠️）。</para>
	/// <para>⚠️ 限频 30 秒（WMI 查询不轻）；任何异常都吞掉返回 0（⛔ 不影响别的传感器）。</para>
	/// </summary>
	private static int ReadAcpiThermalZoneCelsius(bool force = false)
	{
		long now = Environment.TickCount64;
		if (!force && now - Interlocked.Read(in _acpiTempLastTicks) < 30000)
		{
			return _acpiTempCached;
		}
		Interlocked.Exchange(ref _acpiTempLastTicks, now);
		int best = 0;
		try
		{
			using System.Management.ManagementObjectSearcher searcher =
				new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
			foreach (System.Management.ManagementBaseObject row in searcher.Get())
			{
				try
				{
					int raw = Convert.ToInt32(row["CurrentTemperature"]);
					int celsius = (int)Math.Round(raw / 10.0 - 273.15);
					// ⚠️ 合理性判据：CPU 温度不可能 ≤ 0℃ 或 ≥ 120℃ ⇒ 那是无效/未初始化的热区
					if (celsius > best && celsius > 0 && celsius < 120)
					{
						best = celsius;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
			// ⛔ 静默：ACPI 热区不是每台机器都有 / 可能被拒绝访问 —— 那只是"没有这一档"，不是故障
		}
		Volatile.Write(ref _acpiTempCached, best);
		return best;
	}

	/// <summary>
	/// ⭐ 2026-10-18（机主：CPU 温度显示不跟随真实温度）：⭐ <b>诊断包专用</b> —— 强制现读一次 ACPI 热区 ✅。
	/// <para>⚠️ 与采样路径共用同一份实现，只**跳过 30 s 限频缓存**（⭐ 导出诊断包是低频手动动作 ⇒ ⭐ 一次现读可接受 ✅）；
	/// ⛔ 纯只读（⭐ 只查 <c>root\WMI:MSAcpi_ThermalZoneTemperature</c>）✅。</para>
	/// </summary>
	internal static int ReadAcpiThermalZoneCelsiusForDiagnostics()
	{
		return ReadAcpiThermalZoneCelsius(force: true);
	}

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
	/// ⭐⭐ 2026-10-16（GPU 温度「完全不显示」BUGFIX · p2 实证）：⭐ 上一次**记录过**的
	/// "本来源为什么没跑"**原因码**（⭐ 见 <see cref="SkipEarlyNvidiaSmi" /> ✅）。
	/// <para>⭐ 作用：⭐ **原因码变化时才打日志** ⇒ ⛔ 不刷屏（⭐ 判据每 3 秒被求值一次 ⚠️）✅。</para>
	/// </summary>
	private static int _gpuTempSmiSkipLogged;

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

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主反馈「开机很久了，悬浮窗 CPU 温度还停在开机时的温度」）：
	/// <see cref="cpuTemp"/> **最后一次被有效值真正写入**的时刻（<see cref="Environment.TickCount64"/>；0 = 本次会话还没写过）。
	/// <para>⚠️ 为什么要单独记：⭐ 界面上「CPU 温度是否新鲜」原来只看 <c>hw.IsSensorInfoFresh(cpu: true)</c> ⚠️ ——
	/// 而那个时间戳由**另一条路**维护（⭐ 宝龙达 3 s 定时器的功能号 22 / ⭐ GCU 推送 ✅），
	/// 与 <see cref="cpuTemp"/> 这个**展示字段**是否真的被刷新过**不是同一件事** ⚠️
	/// （⭐ 真机根因正是：LHM 闸门失败 ⇒ <c>ApplySensorPriority</c> 整段不跑 ⇒ 时间戳在走、值却停住 ❌）。
	/// ⇒ ⭐ 悬浮窗判「过期」必须看**本字段** ✅（⭐ 超 <see cref="CpuTempFreshAge"/> ⇒ 显示「—」⛔ 不显示陈旧值）。</para>
	/// </summary>
	private static long _cpuTempUpdatedTick;

	/// <summary>
	/// ⭐ 2026-10-18：<see cref="cpuTemp"/> 的新鲜度上限（⭐ 悬浮窗用它判「过期」✅）。
	/// <para>⚠️ 取值理由：⭐ 正常刷新频率 ≤ 2.5 s（⭐ 常驻泵 / ⭐ 悬浮窗 1 s / ⭐ 自定义页 2 s ✅），
	/// 10 s 留足 4 倍余量给线程池抖动/系统忙，⛔ 又远短于"开机到现在"那种真正冻结 ✅。</para>
	/// </summary>
	internal static readonly TimeSpan CpuTempFreshAge = TimeSpan.FromMilliseconds(10000.0);

	/// <summary>
	/// ⭐ 2026-10-18：<see cref="_cpuTempUpdatedTick"/> 的只读快照 —— **-1 = 本次会话从未写入过** ✅。
	/// <para>⭐ 供悬浮窗打 <c>[float] cpu temp stale age=…s</c> 诊断日志 ✅。</para>
	/// </summary>
	internal static long CpuTempAgeMs
	{
		get
		{
			long tick = Interlocked.Read(in _cpuTempUpdatedTick);
			if (tick == 0L)
			{
				return -1L;
			}
			return Environment.TickCount64 - tick;
		}
	}

	/// <summary>⭐ 2026-10-18：<see cref="cpuTemp"/> 是否在 <paramref name="maximumAge"/> 内被刷新过 ✅（⭐ 0 次写入 ⇒ 一律判「不新鲜」）。</summary>
	internal static bool CpuTempIsFresh(TimeSpan maximumAge)
	{
		long tick = Interlocked.Read(in _cpuTempUpdatedTick);
		if (tick == 0L)
		{
			return false;
		}
		return Environment.TickCount64 - tick <= (long)maximumAge.TotalMilliseconds;
	}

	/// <summary>⭐ 2026-10-18：CPU 温度刷新**最近一次尝试**的时刻（⭐ 多入口限频去重用 ✅）。</summary>
	private static long _cpuTempLastAttemptTick;

	/// <summary>
	/// ⭐ 2026-10-18：CPU 温度刷新的**最小间隔**（毫秒 ✅）。
	/// <para>⚠️ 同一条采样链有多个入口（⭐ 常驻泵 2.5 s · ⭐ 悬浮窗 1 s · ⭐ 自定义页 2 s ✅）
	/// ⇒ ⭐ 限频 800 ms ⇒ 1 s 的入口每帧都能刷到、⭐ 同一次泵回调里**不会**重复读两遍 WMI ✅。</para>
	/// </summary>
	private const int CpuTempLocalReadIntervalMs = 800;

	/// <summary>⭐ 2026-10-18：CPU 温度「三个来源都没值」是否已按状态翻转记过一次 ✅（0=没记 · 1=记过）。</summary>
	private static int _cpuTempNoneLogged;

	/// <summary>⭐ 2026-10-18：上一次记录过的 CPU 温度来源（"func22" / "LHM" / "ACPI" / "vendor"）—— 来源变化时立即记一行 ✅。</summary>
	private static string _cpuTempSourceLogged = "";

	/// <summary>⭐ 2026-10-18（机主验证：调「节电」后温度必须下降）：⭐ 上一次**记过日志**的 CPU 温度值 —— 值一变就必打一行 ✅。</summary>
	private static int _cpuTempLastLoggedValue = -1;

	/// <summary>⭐ 2026-10-18：⭐ 上一次读到的厂商功能号 22 原始值（⭐ -1 = 没试/不适用 · ⭐ 0 = 可读但无效 · ⭐ &gt;0 = 有效 ✅；⭐ 诊断用）。</summary>
	private static int _cpuTempLastFunc22Celsius = -1;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主实测：调「节电」后**真实温度降了、显示没变**）：⭐ **LHM 是否在刷新**的判定状态 ✅
	/// <para>⚠️ 判据（⭐ 必须**先用日志证明**才能换源）：⭐ ① LHM 值连续 ≥ <see cref="CpuTempLhmWatchMinUnchangedMs" />
	/// （3 分钟）完全不变；⭐ ② ⭐ 同一窗口内 **ACPI 热区变了 ≥ <see cref="CpuTempLhmWatchAcpiDeltaCelsius" />℃**
	/// （= 环境/负载真的在变，而 ACPI 能反映 ⇒ ⭐ 这才有资格说"LHM 没刷新" ✅）。
	/// ⇒ ⭐ 满足才改以 ACPI 热区为主源；⭐ LHM 值一变就自动恢复 ✅。</para>
	/// <para>⛔ 绝不单凭"值不变"判异常 —— ⭐ 80℃ 恒定可能只是这台机的正常待机温度 ⚠️。</para>
	/// </summary>
	private static int _cpuTempLhmWatchValue = -1;

	private static long _cpuTempLhmWatchSinceTick;

	private static int _cpuTempLhmWatchAcpi = -1;

	private static int _cpuTempLhmSuspect;

	/// <summary>⭐ 2026-10-18：⭐ 交叉校验用的上一帧 ACPI / LHM 值（⭐ 判断"ACPI 变了而 LHM 没变"✅）。</summary>
	private static int _cpuTempCrossPrevAcpi = -1;

	private static int _cpuTempCrossPrevLhm = -1;

	/// <summary>⭐ 2026-10-18：⭐ `[cpu-temp] LHM 原始` 日志的去重键（⭐ 传感器集合/值/上次变化时刻不变 ⇒ 不重复打 ✅）。</summary>
	private static string _cpuTempLhmRawKey = "";

	/// <summary>⭐ 2026-10-18：⭐ LHM「未刷新」判定窗口（毫秒；⭐ 3 分钟 ✅）。</summary>
	internal const long CpuTempLhmWatchMinUnchangedMs = 180000L;

	/// <summary>⭐ 2026-10-18：⭐ 同一窗口内 ACPI 至少变化这么多℃ 才允许判「LHM 未刷新」（⭐ 2℃ ✅）。</summary>
	internal const int CpuTempLhmWatchAcpiDeltaCelsius = 2;

	/// <summary>⭐ 2026-10-18：⭐ 当前 CPU 温度来源标签（⭐ 诊断包「CPU 温度来源明细」用 ✅）。</summary>
	internal static string CpuTempSourceName => _cpuTempSourceLogged;

	/// <summary>⭐ 2026-10-18：⭐ 当前是否已判定「LHM 未刷新 ⇒ 改以 ACPI 主源」（⭐ 诊断用 ✅）。</summary>
	internal static bool CpuTempLhmSuspect => Volatile.Read(ref _cpuTempLhmSuspect) == 1;

	/// <summary>⭐ 2026-10-18：⭐ 上一次厂商功能号 22 原始值（⭐ -1=没试/不适用 · ⭐ 0=可读但无效 · ⭐ &gt;0=有效；⭐ 诊断用 ✅）。</summary>
	internal static int CpuTempLastFunc22Celsius => Volatile.Read(ref _cpuTempLastFunc22Celsius);

	public static int? cpuUsage;

	public static int? cpuFan;

	public static int cpuFanRPM;

	public static int gpuTemp;

	public static int? gpuUsage;

	// ⭐⭐ 2026-10-18（机主：遥测里 CPU/GPU 后面的占用率显示不对）：⭐ **占用率的新鲜度/来源体系** ✅
	//   ⚠️⚠️ 旧链路：`cpuUsage` / `gpuUsage` **只**由 `AttachMechrevoHw` 的 `DataChanged` 从
	//   GCU/MQTT 字段（`hw.CpuUsage` / `hw.GpuUsage`）抄进来 ⚠️ ⇒ ⭐ 宝龙达（无 GCU）机型**根本没有来源**，
	//   而悬浮窗又用 `GetValueOrDefault()` 把 `null` 画成 **0%** ❌ ⇒ ⭐ 机主看到"占用率 0 / 不对" ✅。
	//   ⭐ 现在：⭐ 本地采样（CPU = 性能计数器/GetSystemTimes · GPU = NVML 核心利用率/LHM），
	//   并把"值真正被写入"的时刻记下来 ⇒ ⭐ 界面只在**新鲜**时才显示，过期显示「—」（⛔ 不显示 0% 冒充）✅
	/// <summary>⭐ CPU 占用率**真正被写入**的时刻（<see cref="Environment.TickCount64"/>；0 = 还没写过）✅。</summary>
	private static long _cpuUsageUpdatedTick;

	/// <summary>⭐ GPU 占用率**真正被写入**的时刻（<see cref="Environment.TickCount64"/>；0 = 还没写过）✅。</summary>
	private static long _gpuUsageUpdatedTick;

	/// <summary>⭐ CPU 占用率来源（⭐ 如实标注：性能计数器 xxx / GetSystemTimes 差值 / GCU 推送 兜底 ✅）。</summary>
	private static string _cpuUsageSource = "";

	/// <summary>⭐ GPU 占用率来源（⭐ 如实标注：NVML 核心利用率 / LHM 核心负载 / GCU 推送 兜底 ✅）。</summary>
	private static string _gpuUsageSource = "";

	/// <summary>⭐ GPU 占用率对应的 engine/传感器名（⭐ 如 <c>graphics(core)</c> / <c>D3D 3D</c>）✅。</summary>
	private static string _gpuUsageEngine = "";

	private static int _cpuUsageIntervalMs;

	private static int _gpuUsageIntervalMs;

	/// <summary>⭐ 占用率的新鲜度上限：⭐ 采样泵 2.5 s / 悬浮窗 1 s ⇒ 正常永远新鲜；10 s 留足余量 ✅。</summary>
	internal static readonly TimeSpan UsageFreshAge = TimeSpan.FromMilliseconds(10000.0);

	/// <summary>⭐ 当前 CPU 占用率来源（⭐ 诊断包「占用率来源明细」用 ✅）。</summary>
	internal static string CpuUsageSource => _cpuUsageSource;

	/// <summary>⭐ 当前 GPU 占用率来源（⭐ 诊断用 ✅）。</summary>
	internal static string GpuUsageSource => _gpuUsageSource;

	/// <summary>⭐ 当前 GPU 占用率的 engine/传感器名（⭐ 诊断用 ✅）。</summary>
	internal static string GpuUsageEngine => _gpuUsageEngine;

	/// <summary>⭐ CPU 占用率最近两次真实采样的间隔（毫秒；0 = 未知；⭐ 诊断用 ✅）。</summary>
	internal static int CpuUsageIntervalMs => _cpuUsageIntervalMs;

	/// <summary>⭐ GPU 占用率最近两次真实采样的间隔（毫秒；0 = 未知；⭐ 诊断用 ✅）。</summary>
	internal static int GpuUsageIntervalMs => _gpuUsageIntervalMs;

	/// <summary>⭐ CPU 占用率数据年龄（毫秒；-1 = 从未写入；⭐ 诊断用 ✅）。</summary>
	internal static long CpuUsageAgeMs => UsageAgeMs(Interlocked.Read(in _cpuUsageUpdatedTick));

	/// <summary>⭐ GPU 占用率数据年龄（毫秒；-1 = 从未写入；⭐ 诊断用 ✅）。</summary>
	internal static long GpuUsageAgeMs => UsageAgeMs(Interlocked.Read(in _gpuUsageUpdatedTick));

	/// <summary>⭐ CPU 占用率是否在 <paramref name="maximumAge"/> 内被刷新过 ✅（0 次写入 ⇒ 一律判「不新鲜」）。</summary>
	internal static bool CpuUsageIsFresh(TimeSpan maximumAge)
	{
		return UsageIsFresh(Interlocked.Read(in _cpuUsageUpdatedTick), maximumAge);
	}

	/// <summary>⭐ GPU 占用率是否在 <paramref name="maximumAge"/> 内被刷新过 ✅。</summary>
	internal static bool GpuUsageIsFresh(TimeSpan maximumAge)
	{
		return UsageIsFresh(Interlocked.Read(in _gpuUsageUpdatedTick), maximumAge);
	}

	private static long UsageAgeMs(long tick)
	{
		return (tick == 0L) ? (-1L) : (Environment.TickCount64 - tick);
	}

	private static bool UsageIsFresh(long tick, TimeSpan maximumAge)
	{
		if (tick == 0L)
		{
			return false;
		}
		return Environment.TickCount64 - tick <= (long)maximumAge.TotalMilliseconds;
	}

	/// <summary>⭐ 2026-10-18：⭐ CPU 占用率当前是否在用 GetSystemTimes 兜底（⭐ 性能计数器不可用时为真；⭐ 诊断用 ✅）。</summary>
	internal static bool CpuUsageUsingSystemTimesFallback
	{
		get
		{
			CpuUsageSampler sampler = Volatile.Read(ref cpuUsageSampler);
			return sampler != null && sampler.UsingSystemTimesFallback;
		}
	}

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

	/// <summary>⭐ 2026-10-18（机主：CPU 占用率显示不对）：⭐ CPU 占用率本地采样器（⭐ 性能计数器优先、GetSystemTimes 兜底 ✅）。</summary>
	private static CpuUsageSampler cpuUsageSampler;

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

	// ================= 2026-10-17（真机诊断包 0.291.9.9 定因）：「GPU 温度变 —」与「功耗读数不对」是同一个根因 =================
	// 证据（机主诊断包 %TEMP%\_diag9 · 20:34~20:40）：
	//   20:36:25.934 最后一条「GPU 温度（非 GCU 来源）… 45℃」（NVML）
	//   20:36:38.495 遥测行 GPU 温度第一次变成「—」
	//   20:36:29 → 20:40:32 的每一帧遥测：CPU 功率恒 29 W、GPU 功率恒 10 W（4 分钟一位小数都没动），
	//     而同一行的 CPU 温度（60→62）与风扇转速一直在变
	// ⇒ 不是读数错了，是**本地采样链停了**：`RefreshLocalMonitoringCore` 以前只有两个驱动 ——
	//   ① `CustomModeForm._powerWallTimer`（2 s · `Enabled = Visible` ⇒ **页面一关就停**）
	//   ② 悬浮窗 1 s Timer（默认不开）
	//   主界面那个常驻 2 s 遥测定时器（`SettingsForm.RefreshSensors`）**只读不采**
	// ⇒ ① `_gpuInfoReceivedAt` 不再刷新 ⇒ 6 s 后 `TelemetryTemp` 按新鲜度判据把它画成「—」；
	//   ② `cpuPower`/`gpuPower` 是静态字段、**没有任何新鲜度闸门** ⇒ 冻结值被当实时值一直显示。
	// 修法：① 加**与界面无关**的常驻采样泵（本组字段）；② 给功耗加**新鲜度闸门**（见 `LocalPowerIsFresh`）。

	/// <summary>
	/// ⭐⭐ 2026-10-17：⭐ <b>本地监控是否处于「已启用」状态</b>（⭐ <see cref="EnableLocalMonitoring"/> 置真 ·
	/// <see cref="DisableLocalMonitoring"/> 置假 ✅）。
	/// <para>⚠️ 用途：⭐ 区分「LHM 正在后台重建（临时为 null）」与「监控已整体停用/进程正在退出」
	/// —— 前者仍可由 NVML 补 GPU 温度，后者⛔ 一次原生调用都不许发 ✅。</para>
	/// </summary>
	private static bool _localMonitoringEnabled;

	/// <summary>
	/// ⭐⭐ 2026-10-17：<b>与任何界面无关的常驻本地采样泵</b> ✅
	/// （⭐ 每 <see cref="LocalSamplingPumpIntervalMs"/> 跑一次 <see cref="SampleLocalPowerCore"/> 的**核心部分** ✅）。
	///
	/// <para>⚠️⚠️ <b>为什么必须有它</b>：⭐ 在它之前，采样链只由 ⭐ <c>CustomModeForm._powerWallTimer</c>
	/// （<c>Enabled = Visible</c> ⚠️ ⇒ 页面一关就停）＋ ⭐ 悬浮窗 Timer（默认不开）驱动
	/// ⇒ ⭐ 用户关掉自定义模式页后：⭐ GPU 温度 6 s 后变「—」❌、⭐ CPU/GPU 功耗**冻结**在关页那一刻 ❌
	/// （⭐ 真机日志：20:36:29 起 CPU 恒 29 W / GPU 恒 10 W 长达 4 分钟 ⚠️）。</para>
	///
	/// <para>⭐ <b>为什么安全</b>：<list type="bullet">
	/// <item>⭐ 只在 <see cref="EnableLocalMonitoring"/> 之后跑、<see cref="DisableLocalMonitoring"/> 立刻 <c>Dispose</c>
	/// ⇒ ⭐ 退出/释放之后一次都不碰 LHM/NVML ✅；</item>
	/// <item>⭐ 若最近 <see cref="LocalSamplingPumpIdleGuardMs"/> 内**已经有人采过**
	/// （⭐ 自定义模式页 / ⭐ 悬浮窗 / ⭐ 手动入口 ✅）⇒ ⭐ 本轮**直接跳过** ⇒ ⛔ 不重复采样、⛔ 不与 UI 线程抢 <c>lhmLock</c> ✅；</item>
	/// <item>⛔ 不跑「供电/电池」那几项（`includeAuxiliary:false`）—— 那些仍由主界面既有的 2 s 刷新负责 ✅；</item>
	/// <item>⭐ 采样链自身已有 (a)(b)(c)(d) 四层防护 ⇒ ⭐ 回调里再兜一层 try/catch ✅。</item>
	/// </list></para>
	/// </summary>
	private static System.Threading.Timer _localSamplingPump;

	/// <summary>⭐ 采样泵重入保护（⭐ 1 = 本轮回调在跑 ✅）。</summary>
	private static int _localSamplingPumpInFlight;

	// 2026-10-22（JIGUANG 待机亮屏 0xc0000005）：泵回调卡在原生调用时 finally 永不执行 ⇒ 永久静默；
	// 记开始时刻，超过 15s 判卡死并强制放闸（旧线程可能仍阻塞，但至少泵能活过来并留证据）。
	private static long _localSamplingPumpStartedTick;

	// 2026-10-22（实机日志）：卡死一次后若继续重试，会不断产生新的 NVML 阻塞线程，最终仍 0xc0000005；
	// 因此首次判定卡死就隔离 NVML 并停用采样泵（CPU 温度仍有 SettingsForm 的传感器定时器）。
	private static int _localSamplingPumpQuarantined;

	/// <summary>⭐ 最近一次**任何调用方**开始采样的时刻（<see cref="Environment.TickCount64"/>；0 = 还没采过）—— 采样泵据此让位。</summary>
	private static long _lastLocalSampleTick;

	/// <summary>
	/// ⭐ 功耗字段（<see cref="cpuPower"/> / <see cref="gpuPower"/>）**真正被写入**的时刻
	/// （<see cref="Environment.TickCount64"/>；0 = 还没写过）。
	/// <para>⚠️ 与 <see cref="_lastLocalSampleTick"/> 分开记：⭐ 那个是「有人来采过」（可能中途早退），
	/// ⭐ 这个是「值真的被刷新过」⇒ ⭐ 界面的功耗新鲜度判据只能用后者 ✅。</para>
	/// </summary>
	private static long _lastPowerRefreshTick;

	/// <summary>
	/// ⭐ 采样泵周期（ms）。⚠️ 必须**明显短于** <see cref="MechrevoHw.FanInfoMaximumAge"/>（6 s），
	/// 否则 GPU 温度会在两帧之间被新鲜度判据判成「—」❌。
	/// </summary>
	private const int LocalSamplingPumpIntervalMs = 2500;

	/// <summary>⭐ 最近这段时间内已经有人采过 ⇒ 采样泵让位（ms）。</summary>
	private const int LocalSamplingPumpIdleGuardMs = 2000;

	/// <summary>
	/// ⭐ 主界面功耗读数的<b>新鲜度上限</b>：超过就不显示（⛔ 不把冻结值当实时值）。
	/// <para>⭐ 取值理由：⭐ 采样泵 2.5 s 一次 ⇒ 正常情况年龄 ≤ 2.5 s；⭐ 留 4 倍余量给线程池抖动/系统忙，
	/// 仍然**远小于**真机上那种「冻结 4 分钟」的时长 ✅。⭐ 纯显示判据：⛔ 不改任何 W 的换算 ✅。</para>
	/// </summary>
	internal static readonly TimeSpan LocalPowerFreshAge = TimeSpan.FromMilliseconds(10000.0);

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
			// ⭐ 2026-10-18（机主：CPU 占用率显示不对）：⭐ 起动本地 CPU 占用率采样器（⭐ 与 GCU 无关 ✅）
			if (cpuUsageSampler == null)
			{
				cpuUsageSampler = new CpuUsageSampler();
			}
			// 重新启用监控时清掉上一次的降级状态。
			_localSamplingQuarantined = false;
			_quarantineUntilTicks = 0L;
			_quarantineMs = QuarantineBaseMs;
			_rebuildAttempts = 0;
			_nextRebuildAtTicks = 0L;
			Interlocked.Exchange(ref _samplingFailures, 0);
			// ⭐⭐ 2026-10-17：⭐ 起动**常驻采样泵**（⭐ 见 `_localSamplingPump` 的注释 ——
			//   它就是「关掉自定义模式页后 GPU 温度变 —、功耗冻结」的根因修复 ✅）。
			_localMonitoringEnabled = true;
			StartLocalSamplingPumpLocked();
		}
	}

	public static void DisableLocalMonitoring()
	{
		lock (lhmLock)
		{
			// ⭐⭐ 2026-10-17：⭐ 先停采样泵 ⇒ 之后一次都不会再碰 LHM/NVML ✅
			//   （⭐ 与下面 `lhm = null` 同效：即便有残留回调，也会在 `lhm == null` 早退 ✅）
			_localMonitoringEnabled = false;
			StopLocalSamplingPumpLocked();
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
			// ⭐ 2026-10-18（机主：CPU 占用率显示不对）：⭐ 停采样器（⭐ 不留后台性能计数器 ✅）
			cpuUsageSampler?.Dispose();
			cpuUsageSampler = null;
			cpuPower = (gpuPower = null);
			vramUsedMb = (vramUsage = null);
		}
	}

	/// <summary>
	/// ⭐ 2026-10-17：⭐ 起动常驻采样泵（⭐ 调用方必须已持有 <c>lhmLock</c> 并已把 <see cref="_localMonitoringEnabled"/> 置真 ✅）。
	/// <para>⚠️ 幂等：⭐ 已经在跑就什么都不做 ✅；⭐ UI 审计模式不启动（⭐ 与 `SettingsForm._sensorTimer` 同口径 ⚠️）。</para>
	/// </summary>
	private static void StartLocalSamplingPumpLocked()
	{
		if (Program.UiAuditMode || _localSamplingPump != null)
		{
			return;
		}
		Interlocked.Exchange(ref _lastLocalSampleTick, 0L);
		_localSamplingPump = new System.Threading.Timer(LocalSamplingPumpTick, null, LocalSamplingPumpIntervalMs, LocalSamplingPumpIntervalMs);
		Logger.WriteLine($"本地采样泵已启动（{LocalSamplingPumpIntervalMs} ms · ⭐ 与界面无关：自定义模式页/悬浮窗都不开时也照常刷新"
			+ " GPU 温度（NVML）与 CPU/GPU 功耗 ✅ ⇒ ⛔ 不再出现「关页 6 秒后温度变 —、功耗冻结」❌）");
	}

	/// <summary>2026-10-22（JIGUANG 待机亮屏 0xc0000005）：显示器关闭 ⇒ 只停泵、不 Dispose LHM/NVML（保留温度源）。</summary>
	internal static void SuspendLocalSamplingForDisplayOff()
	{
		lock (lhmLock)
		{
			StopLocalSamplingPumpLocked();
		}
		Logger.WriteLine("[pump] 显示器关闭 ⇒ 暂停本地采样泵（避免 GPU 进 D3/驱动重置期原生调用）");
	}

	/// <summary>2026-10-22：显示器打开 ⇒ 延迟 15s 再重建采样泵（给 GPU/驱动重初始化留窗口）。</summary>
	internal static void ResumeLocalSamplingAfterDisplayOn()
	{
		if (Volatile.Read(in _localSamplingPumpQuarantined) != 0)
		{
			Logger.WriteLine("[pump] 采样泵已被隔离（本进程曾检测到卡死）⇒ 显示器打开后不重建");
			return;
		}
		lock (lhmLock)
		{
			Interlocked.Exchange(ref _localSamplingPumpInFlight, 0);
			Interlocked.Exchange(ref _localSamplingPumpStartedTick, 0L);
			StopLocalSamplingPumpLocked();
		}
		_ = Task.Run(async delegate
		{
			await Task.Delay(15000).ConfigureAwait(continueOnCapturedContext: false);
			if (Volatile.Read(in _localSamplingPumpQuarantined) != 0)
			{
				return;
			}
			lock (lhmLock)
			{
				if (Volatile.Read(in _localMonitoringEnabled))
				{
					StartLocalSamplingPumpLocked();
				}
			}
			Logger.WriteLine("[pump] 显示器打开 15s ⇒ 采样泵已重建（重入闸门清零）");
		});
	}

	/// <summary>⭐ 2026-10-17：⭐ 停常驻采样泵（⭐ 调用方必须已持有 <c>lhmLock</c> ✅）。</summary>
	private static void StopLocalSamplingPumpLocked()
	{
		System.Threading.Timer pump = _localSamplingPump;
		_localSamplingPump = null;
		// ⚠️ 只 Dispose、**不等待**正在跑的回调：这里持有 lhmLock，若等回调就可能与"回调正在等 lhmLock"互等 ❌。
		//   回调本身在 `lhm == null` / `_localMonitoringEnabled == false` 时都会立刻早退 ✅。
		pump?.Dispose();
	}

	/// <summary>
	/// ⭐ 2026-10-17：⭐ 采样泵回调 —— ⭐ **只碰采样链，不碰任何界面/控件** ✅。
	/// <para>⭐ 判据：⭐ 最近 <see cref="LocalSamplingPumpIdleGuardMs"/> 内已经有人采过 ⇒ ⭐ 让位（⛔ 不重复采样）✅。</para>
	/// </summary>
	private static void LocalSamplingPumpTick(object state)
	{
		if (Interlocked.CompareExchange(ref _localSamplingPumpInFlight, 1, 0) != 0)
		{
			// 2026-10-22（JIGUANG 待机亮屏 0xc0000005）：回调卡在原生调用里时 finally 不会执行，
			// 原样 return 会让泵永久静默。15s 还没回来 ⇒ 判定卡死，强制放闸重试并留一行证据。
			long stuckSince = Interlocked.Read(in _localSamplingPumpStartedTick);
			if (stuckSince != 0L && Environment.TickCount64 - stuckSince > 15000L)
			{
				// 实机日志（21:09:52 起反复触发）证明：只放闸重试会让新回调继续卡进 NVML，
				// 最终仍在 21:12 以 0xc0000005 终止。⇒ 首次卡死就隔离 NVML + 停泵，不再产生新原生调用。
				Interlocked.Exchange(ref _localSamplingPumpInFlight, 0);
				Interlocked.Exchange(ref _localSamplingPumpStartedTick, 0L);
				Interlocked.Exchange(ref _localSamplingPumpQuarantined, 1);
				System.Threading.Timer stuckPump = _localSamplingPump;
				stuckPump?.Change(Timeout.Infinite, Timeout.Infinite);
				Logger.WriteError("[pump] 采样泵回调超过 15 s 未返回 ⇒ 判定原生调用卡死；已隔离 NVML 并停用采样泵（本进程不再重试）");
				NvmlHelper.QuarantineAfterNativeHang("采样泵回调卡死（>15s 未返回）");
				return;
			}
			return; // ⭐ 上一轮还没跑完 ⇒ ⭐ 本轮直接跳过（⛔ 不堆积）✅
		}
		Interlocked.Exchange(ref _localSamplingPumpStartedTick, Environment.TickCount64);
		try
		{
			if (!Volatile.Read(in _localMonitoringEnabled))
			{
				return;
			}
			long lastTick = Interlocked.Read(in _lastLocalSampleTick);
			if (lastTick != 0L && Environment.TickCount64 - lastTick < LocalSamplingPumpIdleGuardMs)
			{
				return; // ⭐ 刚有人采过（⭐ 自定义模式页/悬浮窗/手动入口 ✅）⇒ ⭐ 让位 ✅
			}
			// ⭐⭐ 2026-10-18（机主：悬浮窗 CPU 温度停在开机时的温度）：⭐⭐ 泵里原来**没有 CPU 温度这一步**
			//   ⇒ ⭐ 现在按「CPU 温度 → GPU 温度 → CPU/GPU 功耗」的顺序先刷 CPU 温度 ✅
			//   ⭐ 来源顺序：⭐ 宝龙达 40 系功能号 22 ⇒ ⭐ LHM ⇒ ⭐ ACPI 热区（⭐ 见 RefreshCpuTempFromLocalSources ✅）
			//   ⚠️ 三源都没值 ⇒ 保持原值且**不更新时间戳** ⇒ ⭐ 上层（悬浮窗）判「过期」显示「—」⛔ 不显示陈旧值 ✅
			RefreshCpuTempFromLocalSources();
			// （机主：「4070 显卡温度读取」才是要修的）： 之前泵里**只**调了
			RefreshGpuTempFromNvmlWhenNoVendor();
			// ⭐ `includeAuxiliary:false`：⛔ 不刷供电/电池（那些仍由主界面既有的 2 s 刷新负责 ✅）
			SampleLocalPowerCore(includeAuxiliary: false);
		}
		catch
		{
			// (a) 采样泵绝不允许把异常带到线程池外层（⭐ 采样链自身已兜底，这里只是最后一道 ✅）
		}
		finally
		{
			Interlocked.Exchange(ref _localSamplingPumpStartedTick, 0L);
			Interlocked.Exchange(ref _localSamplingPumpInFlight, 0);
		}
	}

	/// <summary>
	/// ⭐ 2026-10-17：⭐ 功耗读数是不是「⭐ 刚刚真的被刷新过」✅ —— 供界面把冻结值判成「不可用」⛔ 不冒充实时值。
	/// <para>⚠️ 只看 <see cref="_lastPowerRefreshTick"/>（值真被写入的时刻）⇒ ⭐ 中途早退的采样不算 ✅。</para>
	/// </summary>
	internal static bool LocalPowerIsFresh(TimeSpan maximumAge)
	{
		long tick = Interlocked.Read(in _lastPowerRefreshTick);
		if (tick == 0L)
		{
			return false;
		}
		return Environment.TickCount64 - tick <= (long)maximumAge.TotalMilliseconds;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主反馈「开机很久了，悬浮窗 CPU 温度还停在开机时的温度」）：⭐ <b>常驻 CPU 温度刷新</b> ✅
	/// —— 按 <b>厂商功能号 22 ⇒ LHM ⇒ ACPI 热区</b> 的顺序取值，⭐ **成功才写** <see cref="cpuTemp"/>
	/// ＋ ⭐ **打** <see cref="_cpuTempUpdatedTick"/> 时间戳 ✅。
	///
	/// <para>⚠️⚠️ <b>根因</b>：⭐ 常驻采样泵（<see cref="LocalSamplingPumpTick"/> · 2.5 s）原来只刷
	/// <b>GPU 温度与 CPU/GPU 功耗</b> ⚠️ —— ⭐ 而 CPU 温度**唯一**会被刷的地方
	/// <c>ApplySensorPriority</c> 排在 LHM 的两道闸门**之后** ⚠️ ⇒ ⭐ LHM 一旦不可用 / 更新失败，
	/// 那里就**整段不跑** ❌ ⇒ ⭐ <see cref="cpuTemp"/> 永远停在启动时
	/// （<see cref="AttachMechrevoHw"/> 的 <c>DataChanged</c>）抄进来的那个值 ❌ —— 正是机主看到的现象 ✅。
	/// ⭐ 修法：⭐ 抽成**独立一步**、⭐ 排在 LHM 闸门**之前**（⭐ 与 GPU 那条 <c>gpu-temp-nvml-early</c> 同款 ✅）
	/// ⇒ ⭐ 自定义模式页 / ⭐ 悬浮窗 / ⭐ 常驻泵 **任一采样入口**都会把它刷到 ✅。</para>
	///
	/// <para>⭐ <b>取值顺序</b>（⭐ 与宝龙达「温度来源顺序」声明同口径 ✅）：
	/// <list type="number">
	/// <item>⭐ <b>宝龙达 40 系</b>：⭐ 厂商 WMI <b>功能号 22</b>（<c>wmi.TryReadCpuTemperature</c> ✅）；
	///   ⚠️ 30 系（裸板号 <c>MRID6</c>）的功能号 22 **恒返回 0** ⇒ 已由
	///   <see cref="BaolongdaWmi.VendorTemperatureUnsupported"/> 整条关掉 ⇒ ⭐ 直接落到 LHM ✅；</item>
	/// <item>⭐ <b>LHM</b>（<c>LhmMonitor.CpuTemperature</c> ✅ —— ⭐ 30 系 / ⭐ 22 失败 / ⭐ 非宝龙达没给值 ✅）；</item>
	/// <item>⭐ <b>ACPI 热区</b>（<see cref="ReadAcpiThermalZoneCelsius"/> ✅ —— ⭐ 免驱动 · ⭐ 封装温度口径 · ⭐ 内部限频 30 s ✅）。</item>
	/// </list>
	/// ⚠️ <b>三个来源都没值</b> ⇒ ⭐ **保持原值、⛔ 不更新时间戳** ✅ ⇒ ⭐ 上层（悬浮窗）据此判「过期」并显示「—」
	/// （⛔ 不再把陈旧值当实时值 ❌）。</para>
	///
	/// <para>⚠️ <b>安全边界</b>：⭐ 调用方只在 <c>_localMonitoringEnabled</c> 时调它（⭐ 未启用/正在退出 ⇒ 一次原生调用都不发 ✅）；
	/// ⭐ 外层带 <c>TryStep(..., countsAsHealthFailure:false)</c> ⇒ 失败只跳过本步、⛔ 不参与 LHM/NVML 健康判定
	/// （⭐ WMI/ACPI 抖一下不该触发「采样链坏了 ⇒ 重建/降级」⚠️）；⭐ 内部再限频 <see cref="CpuTempLocalReadIntervalMs"/>
	/// ⇒ ⛔ 不因多入口而重复读 WMI ✅。</para>
	/// </summary>
	private static void RefreshCpuTempFromLocalSources()
	{
		// ⭐ 多入口限频去重：⛔ 同一次采样里重复读 WMI ✅
		long now = Environment.TickCount64;
		long lastAttempt = Interlocked.Read(in _cpuTempLastAttemptTick);
		if (lastAttempt != 0L && now - lastAttempt < CpuTempLocalReadIntervalMs)
		{
			return;
		}
		Interlocked.Exchange(ref _cpuTempLastAttemptTick, now);
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
		// ── ① LHM：⭐⭐ 先**主动 Update()**、再读值（⭐ 机主实测：调「节电」后真实温度降了、我们的显示没变 ⚠️）──
		//   ⚠️⚠️ 本方法以前只读 `lhmMonitor.CpuTemperature` 的**缓存值**，而 LHM 的 `Update()` 排在采样链**后面**
		//      ⇒ ⭐ 悬浮窗 1 s / 自定义页 2 s 这些入口读到的是**上一轮**（甚至更旧）的值 ⚠️；
		//      再加上 `LhmMonitor` 里 `CpuTemperature` 原来**每轮不清空**（已修）⇒ ⭐ 值会冻结 ❌。
		//   ⭐ 现在：⭐ 本步自己先 `Update()` 一次（⭐ LHM 内部自带 1 s 限频 ⇒ ⛔ 不会加倍原生调用 ✅），
		//      并如实标注 `来源=Update`（本轮真更新了）/ `来源=缓存(ns前)`（⭐ 1 s 限频内，读的是上一次的 ✅）。
		LhmMonitor lhmMonitor = lhm;
		LhmMonitor.CpuTemperatureTier lhmTier = LhmMonitor.CpuTemperatureTier.None;
		int lhmCelsius = 0;
		string lhmSensor = "";
		string lhmUpdateKind = "无";
		if (lhmMonitor != null && !lhmMonitor.IsDisposed)
		{
			long beforeUpdateTicks = lhmMonitor.LastUpdateTicks;
			if (_localMonitoringEnabled && !_localSamplingQuarantined)
			{
				// ⚠️ 只在**未降级**时调原生 Update（⭐ 降级期一次原生调用都不发 ✅）；
				//    `countsAsHealthFailure:false` ⇒ ⛔ 不把这里的失败重复计入 LHM 健康判定 ✅
				TryStep(lhmMonitor.Update, "lhm-update-cpu-temp", countsAsHealthFailure: false);
			}
			bool updatedThisCall = lhmMonitor.LastUpdateTicks != beforeUpdateTicks;
			lhmUpdateKind = (updatedThisCall ? "Update" : ("缓存(" + FormatAgeSeconds(lhmMonitor.LastUpdateAgeMs) + "前)"));
			float? fromLhm = lhmMonitor.CpuTemperature;
			if (fromLhm.HasValue && fromLhm.Value > 0f && fromLhm.Value < 120f)
			{
				lhmCelsius = (int)Math.Round(fromLhm.Value);
				lhmTier = lhmMonitor.CpuTemperatureSelectedTier;
				lhmSensor = lhmMonitor.CpuTemperatureSensorName;
			}
			// ⭐ 原始诊断日志：⭐ 列出**本机 LHM 全部 CPU 温度传感器**（名称/值/上次变化时刻/档位）
			//   ⭐ 值一变必打一行（⭐ 机主调节电后应能看到 80→7x）✅
			LogLhmCpuTemperatureRaw(lhmMonitor, lhmUpdateKind, lhmCelsius, lhmSensor);
		}
		// ── ② 宝龙达 40 系：厂商功能号 22（⭐ 30 系已被 VendorTemperatureUnsupported 整条关掉）/ 非宝龙达：GCU 推送 ──
		int vendorCelsius = 0;
		int func22Raw = -1;
		string func22Text;
		if (hw.UsesBaolongda)
		{
			if (BaolongdaWmi.VendorTemperatureUnsupported)
			{
				func22Raw = 0;
				func22Text = "22=不可用(30系已知)";
			}
			else
			{
				BaolongdaWmi wmi = hw.BaolongdaWmi;
				if (wmi != null)
				{
					try
					{
						if (wmi.TryReadCpuTemperature(out int vendor) && vendor > 0 && vendor < 120)
						{
							vendorCelsius = vendor;
							func22Raw = vendor;
							func22Text = $"22={vendor}℃";
						}
						else
						{
							func22Raw = 0;
							func22Text = "22=读不到/无效";
						}
					}
					catch (Exception ex)
					{
						func22Text = "22=异常";
						Logger.WriteLineIfChanged("cpu-temp-vendor-fail", "[cpu-temp] 厂商功能号 22 读取异常（转 LHM/ACPI）：" + ex.Message);
					}
				}
				else
				{
					func22Text = "22=无WMI对象";
				}
			}
		}
		else
		{
			func22Text = "22=不适用(非宝龙达)";
			vendorCelsius = (hw.CpuTemp > 0) ? hw.CpuTemp : 0;
		}
		Interlocked.Exchange(ref _cpuTempLastFunc22Celsius, func22Raw);
		// ── ③ ACPI 热区（⭐ 交叉校验 + 兜底；⭐ 内部限频 30 s，但**每轮都读**以做"同一 tick"对照 ✅）──
		int acpi = ReadAcpiThermalZoneCelsius();
		// ── ④ 交叉校验 + 「LHM 是否在刷新」判定（⭐ 机主方向：先用日志证明、再决定换不换源 ⚠️）──
		NoteCpuTempCrossCheck(lhmCelsius, acpi);
		bool lhmSuspect = NoteCpuTempLhmRefreshWatch(lhmCelsius, acpi, lhmSensor);
		// ── ⑤ 取源优先级：厂商(func22/GCU) ＞ LHM Tctl/Tdie ＞ LHM Package ＞ ACPI ＞ LHM 其余 ──
		//   ⚠️ LHM 被判「未刷新」时：⭐ 改用 ACPI 主源、LHM 次源（⭐ 依据 = 上面已经用日志证明"ACPI 在变" ✅）
		//   ⛔ 整个选择过程**不修改任何数值**（不做偏移/补偿）✅
		int celsius = 0;
		string source = "";
		// NUC/UMI 的 GCU 厂商温度可能长期停在一个值（风扇却按真实温度转），
		// 这两类机型改为 LHM 优先；其它机型仍保持厂商优先。
		bool preferLhmCpu = (hw.IsIntelNucMachine || hw.IsUmiMachine) && !lhmSuspect && lhmCelsius > 0;
		if (preferLhmCpu)
		{
			celsius = lhmCelsius;
			source = (lhmTier == LhmMonitor.CpuTemperatureTier.TctlTdie)
				? "LHM/TctlTdie"
				: (lhmTier == LhmMonitor.CpuTemperatureTier.Package ? "LHM/Package" : "LHM/Other");
		}
		else if (vendorCelsius > 0)
		{
			celsius = vendorCelsius;
			source = (hw.UsesBaolongda ? "func22" : "vendor");
		}
		else if (lhmSuspect && acpi > 0)
		{
			celsius = acpi;
			source = "ACPI(主源·LHM未刷新)";
		}
		else if (lhmCelsius > 0 && lhmTier == LhmMonitor.CpuTemperatureTier.TctlTdie)
		{
			celsius = lhmCelsius;
			source = "LHM/TctlTdie";
		}
		else if (lhmCelsius > 0 && lhmTier == LhmMonitor.CpuTemperatureTier.Package)
		{
			celsius = lhmCelsius;
			source = "LHM/Package";
		}
		else if (acpi > 0)
		{
			celsius = acpi;
			source = "ACPI";
		}
		else if (lhmCelsius > 0)
		{
			celsius = lhmCelsius;
			source = "LHM/Other";
		}
		if (celsius <= 0)
		{
			// ⭐ 三个来源都没值 ⇒ ⭐ **保持原值**、⛔ **不更新时间戳** ⇒ ⭐ 上层能判「过期」✅
			if (Interlocked.Exchange(ref _cpuTempNoneLogged, 1) == 0)
			{
				Logger.WriteError("[cpu-temp] ⚠️ 三个来源都没值（厂商 func22 / LHM / ACPI 热区）"
					+ $"（⭐ LHM={lhmUpdateKind} · LHM传感器值={(lhmCelsius > 0 ? lhmCelsius + "℃" : "无")} · "
					+ $"{func22Text} · ACPI={(acpi > 0 ? acpi + "℃" : "读不到")}）"
					+ " ⇒ 保持原值且**不更新时间戳** ⇒ 悬浮窗超过 "
					+ CpuTempFreshAge.TotalSeconds.ToString("F0")
					+ " s 会显示「—」（⛔ 不再显示陈旧值）；⭐ 本条只在「开始取不到」的那一刻记一次");
			}
			return;
		}
		cpuTemp = celsius;
		// ⭐⭐ 2026-10-18：⭐ 同时**写回门面层** ＋ 打 `_cpuInfoReceivedAt`（⭐ 复用既有
		//   `IsSensorInfoFresh(cpu: true)` 体系 ✅ —— 主界面那行遥测读的就是 `hw.CpuTemp` ⚠️）
		//   ⚠️ 只在**非厂商推送来源**（func22 / LHM / ACPI）时写 ⇒ ⭐ GCU 机型的「厂商值优先」语义 ⛔ 一字不改 ✅
		//   ⚠️ 30 系宝龙达的功能号 22 是关掉的 ⇒ ⭐ 这一笔让 LHM/ACPI 兜底值也能进主界面（⛔ 不再恒「—」）✅
		if (!string.Equals(source, "vendor", StringComparison.Ordinal))
		{
			hw.MarkExternalCpuTemp(celsius);
		}
		long updated = Environment.TickCount64;
		long previous = Interlocked.Exchange(ref _cpuTempUpdatedTick, updated);
		bool recovered = Interlocked.Exchange(ref _cpuTempNoneLogged, 0) == 1;
		double ageSeconds = ((previous == 0L) ? 0.0 : (updated - previous) / 1000.0);
		string sensorTag = ((lhmSensor.Length > 0 && source.StartsWith("LHM", StringComparison.Ordinal)) ? ("(" + lhmSensor + ")") : "");
		// ⭐ 2026-10-18（机主验证方式：调「节电」后温度应下降、日志应有值变化行）：
		//   ⭐ 这一行**含 ACPI 与功能号 22 的同一 tick 读数** ⇒ ⭐ 下次诊断包可直接交叉校验"谁在变、谁没变" ✅
		string line = $"[cpu-temp] 来源={source}{sensorTag} · 值={celsius}℃ · ACPI={acpi}℃ · {func22Text} · LHM={lhmUpdateKind} · age={ageSeconds:F1}s";
		bool sourceChanged = !string.Equals(_cpuTempSourceLogged, source, StringComparison.Ordinal);
		_cpuTempSourceLogged = source;
		bool valueChanged = _cpuTempLastLoggedValue != celsius;
		_cpuTempLastLoggedValue = celsius;
		if (recovered)
		{
			// ⭐ 状态翻转（⭐ 之前三源都没值 ⇒ 现在有了 ✅）⇒ ⭐ 默认级别可见地记一行 ✅
			Logger.WriteError("[cpu-temp] ⭐ 已恢复 ✅ —— " + line);
		}
		else if (sourceChanged || valueChanged || previous == 0L)
		{
			// ⭐ 来源变化 / ⭐ **值变化（机主复现点：调节电后必须看到这一行 ✅）** / ⭐ 首次成功 ⇒ ⭐ 立即记一行 ✅
			Logger.WriteLine(line);
		}
		else
		{
			// ⭐ 去重：来源/值都没有状态变化时 60 s 一条心跳（⭐ 含 age ⇒ 能看出刷新间隔 ✅）
			Logger.WriteLineThrottled("cpu-temp", line, 60000);
		}
	}

	/// <summary>⭐ 2026-10-18：⭐ 毫秒 → 人读"距离现在多久"（⭐ -1 ⇒ "从未"；⭐ 诊断/日志共用 ✅）。</summary>
	private static string FormatAgeSeconds(long ageMs)
	{
		return (ageMs < 0L) ? "从未" : ((ageMs / 1000.0).ToString("F1") + "s");
	}

	/// <summary>
	/// ⭐ 2026-10-18：⭐ <see cref="Environment.TickCount64" /> → 本地时钟 <c>HH:mm:ss</c>（⭐ 诊断里"上次变化时刻"用**绝对时刻**
	/// ⇒ ⭐ 内容稳定、日志才能去重 ✅；⭐ 与 <see cref="DateTime.Now" /> 的误差只来自调用瞬间，诊断足够 ✅）。
	/// </summary>
	private static string FormatTickAsClock(long tick)
	{
		if (tick <= 0L)
		{
			return "未知";
		}
		return DateTime.Now.AddMilliseconds(-(Environment.TickCount64 - tick)).ToString("HH:mm:ss");
	}

	/// <summary>⭐ 2026-10-18：⭐ LHM CPU 温度档位 → 人读标签（⭐ 诊断行用 ✅）。</summary>
	private static string CpuTempTierLabel(LhmMonitor.CpuTemperatureTier tier)
	{
		switch (tier)
		{
		case LhmMonitor.CpuTemperatureTier.TctlTdie:
			return "Tctl/Tdie";
		case LhmMonitor.CpuTemperatureTier.Package:
			return "Package";
		case LhmMonitor.CpuTemperatureTier.Other:
			return "其余";
		case LhmMonitor.CpuTemperatureTier.Excluded:
			return "不参与选源";
		default:
			return "无";
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：CPU 温度 80℃ 不动 · 调节电后真实温度变了显示没变）：
	/// ⭐ <b>`[cpu-temp] LHM 原始` 诊断日志</b> ✅ —— ⭐ 列出**本机 LHM 全部 CPU 温度传感器**
	/// （⭐ 名称 / 值 / 上次变化时刻 / 档位），⭐ 并标出"本轮是 Update 还是缓存" ✅。
	/// <para>⭐ 去重口径：⭐ 传感器集合 / 值 / 上次变化时刻（绝对时刻）都不变 ⇒ ⭐ 不重复打（⭐ 30 s 心跳例外）；
	/// ⭐ **任何一个值一变 ⇒ 立即打一行**（⭐ 机主调节电后应能看到 80→7x 的变化行 ✅）。</para>
	/// </summary>
	private static void LogLhmCpuTemperatureRaw(LhmMonitor monitor, string updateKind, int selectedCelsius, string selectedSensor)
	{
		IReadOnlyList<LhmMonitor.CpuTemperatureSensorReading> sensors = monitor.CpuTemperatureSensors;
		string all = "";
		long selectedLastChange = 0L;
		foreach (LhmMonitor.CpuTemperatureSensorReading sensor in sensors)
		{
			if (all.Length > 0)
			{
				all += " · ";
			}
			all += $"{sensor.Name}={sensor.Value:F1}℃(updatedAt={FormatTickAsClock(sensor.LastChangeTicks)}, {CpuTempTierLabel(sensor.Tier)})";
			if (selectedSensor.Length > 0 && string.Equals(sensor.Name, selectedSensor, StringComparison.Ordinal))
			{
				selectedLastChange = sensor.LastChangeTicks;
			}
		}
		if (all.Length == 0)
		{
			all = "（LHM 没给出 CPU 温度传感器）";
		}
		string selectedText = (selectedCelsius > 0)
			? ($"sensor={selectedSensor} value={selectedCelsius}℃ updatedAt={FormatTickAsClock(selectedLastChange)}")
			: "sensor=（无有效值）";
		string line = $"[cpu-temp] LHM 原始：{selectedText} · 来源={updateKind} · CPU硬件={monitor.CpuHardwareCount}"
			+ $" · LHM更新={FormatAgeSeconds(monitor.LastUpdateAgeMs)}前 · 全部=[{all}]";
		string dedupKey = selectedText + "|" + monitor.CpuHardwareCount + "|[" + all + "]";
		if (!string.Equals(_cpuTempLhmRawKey, dedupKey, StringComparison.Ordinal))
		{
			_cpuTempLhmRawKey = dedupKey;
			Logger.WriteLine(line);
		}
		else
		{
			// ⭐ 完全没变化 ⇒ ⭐ 30 s 一条心跳（⛔ 不刷屏）✅
			Logger.WriteLineThrottled("cpu-temp-lhm-raw", line, 30000);
		}
	}

	/// <summary>
	/// ⭐ 2026-10-18（机主方向 ③：同一 tick 交叉校验）：⭐ 若 **ACPI 变了而 LHM 没变** ⇒ ⭐ 明确记一行
	/// "本轮 LHM 缓存没更新"（⭐ 限流 30 s ⇒ ⛔ 不刷屏）；⭐ 这是"要不要换主源"的**直接证据** ✅。
	/// </summary>
	private static void NoteCpuTempCrossCheck(int lhmCelsius, int acpi)
	{
		if (_cpuTempCrossPrevAcpi > 0 && acpi > 0 && acpi != _cpuTempCrossPrevAcpi
			&& _cpuTempCrossPrevLhm > 0 && lhmCelsius > 0 && lhmCelsius == _cpuTempCrossPrevLhm)
		{
			Logger.WriteLineThrottled("cpu-temp-cross",
				$"[cpu-temp] ⚠️ 交叉校验：ACPI {_cpuTempCrossPrevAcpi}℃ → {acpi}℃ 变了，而 LHM 仍 {lhmCelsius}℃"
				+ " ⇒ ⭐ 本轮 LHM 缓存没更新（⭐ 若持续出现，会按「LHM 未刷新」判据自动改以 ACPI 为主源 ✅）", 30000);
		}
		if (acpi > 0)
		{
			_cpuTempCrossPrevAcpi = acpi;
		}
		if (lhmCelsius > 0)
		{
			_cpuTempCrossPrevLhm = lhmCelsius;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：调节电后真实温度降了、显示没变）：⭐ <b>「LHM 未刷新」判定</b> ✅
	/// —— ⭐ 只有**两条同时成立**才判：⭐ ① LHM 值连续 ≥3 分钟完全不变；⭐ ② 同一窗口内 ACPI 变化 ≥2℃
	/// （⭐ 证明环境/负载确实在变、而 ACPI 能反映 ✅）。⭐ 判为真后：⭐ 取源改以 ACPI 为主源；
	/// ⭐ LHM 值一变 ⇒ 立即解除、恢复 LHM 主源 ✅。
	/// <para>⛔ 不单凭"值不变"下结论（⭐ 80℃ 恒定可能只是正常待机温度 ⚠️）；⛔ 不修改任何数值 ✅。</para>
	/// </summary>
	/// <returns>⭐ 当前是否处于「LHM 未刷新」状态 ✅。</returns>
	private static bool NoteCpuTempLhmRefreshWatch(int lhmCelsius, int acpi, string sensorName)
	{
		long now = Environment.TickCount64;
		if (lhmCelsius <= 0)
		{
			_cpuTempLhmWatchValue = -1;
			return Volatile.Read(ref _cpuTempLhmSuspect) == 1;
		}
		if (lhmCelsius != _cpuTempLhmWatchValue)
		{
			_cpuTempLhmWatchValue = lhmCelsius;
			_cpuTempLhmWatchSinceTick = now;
			_cpuTempLhmWatchAcpi = acpi;
			if (Interlocked.Exchange(ref _cpuTempLhmSuspect, 0) == 1)
			{
				Logger.WriteError($"[cpu-temp] ⭐ LHM 值已恢复变化（={lhmCelsius}℃）⇒ 解除「LHM 未刷新」判定，恢复 **LHM 主源** ✅");
			}
			return false;
		}
		long unchangedMs = now - _cpuTempLhmWatchSinceTick;
		if (unchangedMs >= CpuTempLhmWatchMinUnchangedMs
			&& acpi > 0 && _cpuTempLhmWatchAcpi > 0
			&& Math.Abs(acpi - _cpuTempLhmWatchAcpi) >= CpuTempLhmWatchAcpiDeltaCelsius
			&& Interlocked.Exchange(ref _cpuTempLhmSuspect, 1) == 0)
		{
			Logger.WriteError($"[cpu-temp] ⚠️ 判定 **LHM 未刷新**：sensor={sensorName} · 值={lhmCelsius}℃ 已 "
				+ $"{unchangedMs / 60000.0:F1} 分钟完全不变，而同一窗口 ACPI 热区 {_cpuTempLhmWatchAcpi}℃ → {acpi}℃ 在变化"
				+ " ⇒ ⭐ 本机改以 **ACPI 热区**为主源（LHM 次源）；⭐ 一旦 LHM 值再次变化就自动恢复 ✅"
				+ "（⭐ 依据：先由日志证明 ACPI 会随负载/节电变化；⛔ 未改任何数值、⛔ 未做偏移补偿）");
		}
		return Volatile.Read(ref _cpuTempLhmSuspect) == 1;
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
			// 无界/星耀 AMD：厂商通道给的是热区/封装口径，和 HWiNFO 的 CPU Package 差很多，
			// 这两类机型改为 LHM 优先，LHM 无值才回退厂商/ACPI。
			// 无界/星耀 AMD APU 的厂商通道是 ACPI/热区口径（用户截图 67℃），
			// HWiNFO 的 Tctl/Tdie 才是 CPU 真实温度（38℃）；这两种 APU 都优先 LHM。
			bool preferLhm = hw.SmuCustomApu || hw.WujieSmuApu;
			float? fromLhm = lhmMonitor.CpuTemperature;
			if (preferLhm && fromLhm.HasValue && fromLhm.Value > 0f)
			{
				cpuTemp = (int)Math.Round(fromLhm.Value);
				hw.MarkCpuInfoReceived();
			}
			else if (cpuFromVendor > 0)
			{
				// ⭐ 厂商（⭐ GCU 推送 / ⭐ 宝龙达功能号 22 ✅）给了有效值 ⇒ ⭐ 照原样用 ✅
				cpuTemp = cpuFromVendor;
			}
			else
			{
				// ⭐ 厂商没给 ⇒ ⭐ 才用 LM 自带的 LHM 兜底 ✅（⭐ 非 GCU 后端主要靠这一支 ✅）
				if (fromLhm.HasValue && fromLhm.Value > 0f)
				{
					cpuTemp = (int)Math.Round(fromLhm.Value);
					// ⭐⭐ 2026-10-17（修「30 系 CPU 温度不识别」）：⭐ LHM 给出有效值时**也要盖 CPU 时间戳** ⚠️
					//   否则 `IsSensorInfoFresh(cpu:true)` 恒 false ⇒ 界面永远显示「—」❌
					hw.MarkCpuInfoReceived();
					if (Interlocked.Exchange(ref _lhmCpuTempLogged, 1) == 0)
					{
						Logger.WriteLine($"传感器兜底：⭐ 厂商未提供 CPU 温度 ⇒ 改用 **LM 自带的 LHM**"
							+ $"（传感器={lhmMonitor.CpuTemperatureSensorName}，值={cpuTemp}℃）✅");
					}
				}
				else
				{
					// ⭐⭐ 2026-10-17（30 系宝龙达 CPU 温度不识别 · 第三档兜底）：
					//   ⭐ 30 系宝龙达的 WMI 功能号 22 **返回 0**（无效）；而 LHM 读 CPU 核心温度需要
					//      Ring0 驱动（PawnIO 读 MSR）⚠️ ⇒ 没装驱动时 LHM 也给不出值 ❌
					//   ⭐ 但 ACPI 热区（`MSAcpi_ThermalZoneTemperature`）**免驱动**、只需管理员 ✅
					//      ⇒ ⭐ 作为**最后一档**兜底：ACPI 给什么就是什么（通常是封装温度，比核心温度低几度）
					//   ⚠️ 语义如实：⭐ 这是**热区温度**，⛔ 不是核心温度 —— 但**有胜于无**（总比「—」好）
					//   ⚠️ 限频 30s（WMI 查询不轻），失败静默不计入崩溃
					int acpiTemp = ReadAcpiThermalZoneCelsius();
					if (acpiTemp > 0)
					{
						cpuTemp = acpiTemp;
						if (Interlocked.Exchange(ref _acpiCpuTempLogged, 1) == 0)
						{
							Logger.WriteLine($"传感器兜底：⭐ 厂商与 LHM 都没给 CPU 温度 ⇒ 改用 **ACPI 热区**"
								+ $"（值={acpiTemp}℃ ⚠️ 封装温度口径，非核心温度）✅");
						}
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
			// ⭐⭐ 2026-10-17（真机反馈「40 系官方控制台能读显卡温度，但 LM 的 4070 读不到」）：
			//   ⭐ 宝龙达**没有厂商 GPU 温度**（无 GCU 推送）⚠️ ⇒ 这条「厂商值」分支对它**不该成立** ❌
			//   ⚠️ 而它是**静默的**（不进任何日志）⇒ 真机诊断包里既没有"取到"也没有"没取到"的行
			//      ⇒ ⭐ 表现就是"读不到但查不出原因" ✅（本次就是这么被卡住的）
			//   ⇒ ⭐ 宝龙达下**强制走 LHM/NVML 支路** ✅；并在拿到/没拿到时都留下**可诊断的日志** ✅
			//   ⚠️ 非宝龙达（GCU 机型）⇒ `!isBaolongda` 恒 true ⇒ ⭐ 条件与改动前**逐字一致** ✅
			bool isBaolongdaGpu = Program.hw?.UsesBaolongda ?? false;
			if (hw.GpuTemp > 0 && !facadeIsOurs && !isBaolongdaGpu)
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
				// ⭐ 2026-10-17：宝龙达下把"问谁 / 各自给了什么"记下来（⚠️ 限流 60s，⛔ 不刷屏）
				if (isBaolongdaGpu)
				{
					Logger.WriteLineThrottled("bd-gpu-temp-probe",
						$"GPU 温度探路（宝龙达）：门面值={hw.GpuTemp} · facadeIsOurs={facadeIsOurs}"
						+ $" · LHM={(lhmMonitor.GpuTemperature.HasValue ? lhmMonitor.GpuTemperature.Value.ToString("F0") + "℃" : "无")}"
						+ $" · NVML已隔离={NvmlHelper.Unavailable} · 将依次试 LHM ⇒ NVML ⇒ nvidia-smi 早期值", 60000);
				}
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
					// ⭐⭐ 2026-10-16（修「待机时 GPU 温度只显示 —」）：⭐ 这里的关键是**把两种完全不同的
					//   状态分开** ✅ —— ⭐ 用户已经指出原因：「⭐ 显卡没读数是因为占用低待机了」✅，
					//   但"⭐ 待机"与"⭐ 真故障"在界面上**长得一模一样**（⭐ 都是「—」⚠️）⇒ ⭐ 观感像故障 ❌。
					//   ⭐ 判据见 `NvmlHelper.TempUnavailableWhileDeviceAlive`：
					//     ① ⭐ 句柄拿得到（⭐ 卡还在 ✅）＋ ⭐ 温度查询没给有效值 ⇒ ⭐ 低功耗/待机态 ✅
					//     ② ⭐ 连句柄都拿不到、⭐ 或 NVML 已隔离 ⚠️ ⇒ ⭐ 真故障 ⇒ ⭐ 维持「—」✅
					//   ⭐ 同时把**温度专属返回码**打出来 —— 这是"待机时 NVML 到底返回什么"的**唯一证据** ⚠️
					//     （⭐ 既有 `_lastError` 只在四个查询全失败时更新 ⇒ ⭐ 这个组合它记不到 ❌）
					bool nvmlAliveNoTemp = NvmlHelper.TempUnavailableWhileDeviceAlive;
					string verdict = nvmlAliveNoTemp
						? "⇒ ⭐ 判定为**待机/低功耗态** ✅（⭐ 设备句柄有效、⭐ 只是温度这个查询没给值 ✅）"
						: "⇒ ⭐ 未能判定为待机 ⚠️（⭐ 句柄也拿不到、⭐ 或 NVML 已隔离）⇒ ⭐ 维持「—」✅";
					Logger.WriteError($"GPU 温度：⚠️ 厂商/LHM/NVML **三个来源都没给值** ⇒ 界面会显示「—」"
						+ $"（⭐ 厂商值={hw.GpuTemp} · ⭐ LHM={(lhmMonitor.GpuTemperature.HasValue ? lhmMonitor.GpuTemperature.Value.ToString("F0") : "无")}"
						+ $" · ⭐ NVML已隔离={NvmlHelper.Unavailable}"
						+ $" · ⭐ 句柄有效={nvmlAliveNoTemp}"
						+ $" · ⭐ 温度专属返回码={NvmlHelper.LastTempError}"
						+ $"（⭐ -1=没查过 · ⭐ -2=返回成功但值无效/常见于低功耗 · ⭐ 0=成功 · ⭐ 其他=NVML 原始码））"
						+ verdict
						+ "；⭐ 本条只在开始取不到的那一刻记一次 ✅");
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
			// 2026-10-22（机主：LM 显示的 CPU 转速其实是副扇、GPU 读不到）：
			// 无界机型**以 EC 为准**覆盖 GCU FanInfo：主扇 0x0464 = CPU、副扇 0x046C = GPU。
			// GCU FanInfo 未上报时（例如 GCU 服务预热/尚未 ready），按官方 EC 寄存器只读兜底。
			// 与 OpenRevo 的 EC 轮询同理：Fan RPM 直接走 EC，不依赖 GCU 是否已 ready。
			if (!hw.WujieSmuApu && !hw.UsesBaolongda
				&& (cpuFanRPM <= 0 || gpuFanRPM <= 0)
				&& hw.TryReadGcuEcFanSpeeds(out int gcuCpuFanRpm, out int gcuGpuFanRpm))
			{
				if (gcuCpuFanRpm > 0) cpuFanRPM = gcuCpuFanRpm;
				if (gcuGpuFanRpm > 0) gpuFanRPM = gcuGpuFanRpm;
				hw.ApplyGcuEcFanSpeeds(gcuCpuFanRpm, gcuGpuFanRpm);
				Logger.WriteLineIfChanged("gcu-ec-fan",
					"[fan] GCU FanInfo 缺失 ⇒ EC 只读兜底：CPU=" + gcuCpuFanRpm + "rpm · GPU=" + gcuGpuFanRpm + "rpm");
			}
			if (hw.WujieSmuApu)
			{
				if (hw.TryReadWujieEcFanSpeeds(out int mainFanRpm, out int secondaryFanRpm))
				{
					if (mainFanRpm >= 0)
					{
						cpuFanRPM = mainFanRpm;
					}
					if (secondaryFanRpm >= 0)
					{
						gpuFanRPM = secondaryFanRpm;
					}
					Logger.WriteLineIfChanged("wujie-ec-fan",
						"[fan] 无界 EC 风扇转速：主扇(CPU)=" + (mainFanRpm >= 0 ? mainFanRpm + "rpm" : "读不到")
						+ " · 副扇(GPU)=" + (secondaryFanRpm >= 0 ? secondaryFanRpm + "rpm" : "读不到")
						+ "（EC 为准，覆盖 GCU FanInfo）");
				}
				// 界面 RPM 行要求 FanInfo 新鲜 ⇒ 用 EC 值覆盖时也打一次时间戳（否则有值也不显示）
				hw.MarkFanInfoReceived();
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
			// 电池百分比以 Windows 当前值为准（GCU 推送可能滞后，任务栏与本程序要对得上）
			int winBattery = (int)Math.Round(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
			batteryCapacity = ((winBattery >= 0 && winBattery <= 100) ? winBattery : hw.BatteryPercent);
			batteryCharge = batteryCapacity;
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
	/// ⭐⭐ 2026-10-17（修「4070 GPU 温度已取到却又变回—」）：⭐ **与 LHM 闸门解耦**的 GPU 温度读取 ✅
	///
	/// <para>⚠️⚠️ <b>为什么要提前</b>：原来唯一会写 GPU 温度的是 `ApplySensorPriority`，
	/// 而它排在 `lhm-update` / `lhm-read` **两道闸门之后** ⚠️ ⇒ 闸门一失败就 `return`，
	/// 门面层从此不再刷新 ⇒ 只靠 `KeepAliveGpuTempOnSampleFailure()` 续 30 s ⇒ 之后界面变「—」❌
	/// （真机诊断包 `0.291.9.8-180311` 的时间线正是这样：13~28 s 有值 → 44 s 后「—」）。</para>
	///
	/// <para>⭐ <b>为什么安全</b>：
	/// <list type="bullet">
	/// <item>只读 <see cref="NvmlHelper.GetGpuTemperature" />（⭐ 加固层自带隔离/节流 ✅）；</item>
	/// <item>调用点排在 `_localSamplingQuarantined` **之后** ⇒ ⛔ 降级期间一次原生调用都不发 ✅；</item>
	/// <item>包在 `TryStep(..., countsAsHealthFailure:false)` 里 ⇒ 失败只跳过本步、⛔ 不影响健康判定 ✅；</item>
	/// <item>⭐ **厂商有值时（GCU 机型）直接返回** ⇒ ⛔ 不覆盖厂商值，行为逐位不变 ✅。</item>
	/// </list></para>
	/// </summary>
	private static void RefreshGpuTempFromNvmlWhenNoVendor()
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
		if (hw == null || NvmlHelper.Unavailable)
		{
			return;
		}
		// ⭐ 厂商给了值（GCU 机型）⇒ ⛔ 不碰；非宝龙达 + 门面值不是我们写的 ⇒ 老路 ✅
		if (hw.GpuTemp > 0 && !IsGpuTempFacadeWrittenByUs(hw) && !hw.UsesBaolongda)
		{
			return;
		}
		float? temp = NvmlHelper.GetGpuTemperature();
		if (!temp.HasValue || temp.Value <= 0f)
		{
			return; // ⚠️ 待机时 NVML 常返回 0 ⇒ 不写（⛔ 不谎报）✅
		}
		int celsius = (int)Math.Round(temp.Value);
		hw.MarkExternalGpuTemp(celsius);
		// ⭐ 记成"我们写的" ⇒ 下一轮 `ApplySensorPriority` 不会误判成厂商值 ✅
		Interlocked.Exchange(ref _gpuTempFacadeWritten, celsius);
		Interlocked.Exchange(ref _gpuTempExternalGoodTick, Environment.TickCount64);
		Interlocked.Exchange(ref _gpuTempKeepAliveState, 0);
		MarkGpuTempRealSourceReady();
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
	/// <summary>⭐⭐ 2026-10-16（修「待机时 GPU 温度只显示 —」）：⭐ GPU 温度**"在、但没值"**
	/// = 待机/低功耗态 ✅ —— 供界面把「—」显示成「待机」，⭐ 免得看着像故障 ⚠️。
	/// <para>⚠️ 判据来自 <see cref="NvmlHelper.TempUnavailableWhileDeviceAlive" />：
	/// ⭐ 句柄拿得到（⭐ 卡还在 ✅）＋ ⭐ 温度查询没给出有效值 ⇒ ⭐ 低功耗/待机 ✅；
	/// ⭐ 连句柄都拿不到 ⇒ ⭐ 真故障 ⇒ ⭐ 本属性为假 ✅（⭐ 那种情况界面该继续显示「—」✅）。</para>
	/// <para>⛔ <b>它只回答"为什么没有数字"，绝不提供数字</b> —— 界面必须显示"无读数"语义，
	/// ⛔ 不得据此编造温度 ✅（铁律：读不到就显示未知）。</para></summary>
	internal static bool GpuTempIdle => NvmlHelper.TempUnavailableWhileDeviceAlive;

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
			// 理据：0℃ 以下 / 150℃ 以上都不是真实 GPU 温度 ⇒ 当取不到（不猜）。
			// 注意：部分 nvidia-smi（如 621.01）的 temperature.gpu 输出是带两位小数的
			// 浮点（46.78 / 50.47），不能用 int.TryParse；否则会像 OpenRevo 那样
			// 解析失败后一直退回到旧值/空值。
			if (firstLine != null
				&& double.TryParse(firstLine, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double rawCelsius)
				&& rawCelsius > 0 && rawCelsius < 150)
			{
				return (int)Math.Round(rawCelsius);
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
	/// <summary>
	/// ⭐⭐ 2026-10-16（GPU 温度「完全不显示」BUGFIX · p2 实证）：
	/// ⭐ 一次性记录"本来源**为什么没跑**"✅ —— ⭐ **原因码变化时才打** ⇒ ⛔ 不刷屏 ✅。
	/// <para>⚠️ <b>为什么必须加</b>：p2（`…-225820`，日志级别 `All` ⇒ 能记全部级别）里
	/// ⭐ 从启动到导出共 7 分钟、`NVML Init` 与 `GPU 温度…取到` **一行都没有** ⚠️，
	/// 而原实现的判据 ⑤（`ResolveNvidiaSmiPath()` 找不到 exe）是**完全静默**的 ❌
	/// ⇒ ⭐ 诊断包**无法区分**"没装 nvidia-smi"／"厂商已给值"／"已让位"⚠️
	/// ⇒ ⭐ 下一份包据此可**一眼定位** ✅。</para>
	/// </summary>
	private static bool SkipEarlyNvidiaSmi(int code, string reason)
	{
		if (Interlocked.Exchange(ref _gpuTempSmiSkipLogged, code) != code)
		{
			Logger.WriteError($"GPU 温度：⭐ nvidia-smi 早期来源**本次未运行** ⇒ {reason}"
				+ "（⭐ 本行按原因变化只记一次 ✅；⭐ 该来源只在【厂商不给 GPU 温度】的机器上才需要 ⚠️）");
		}
		return false;
	}

	private static bool ShouldTryEarlyNvidiaSmi()
	{
		// ① 让位：LHM/NVML 已就绪并给过有效值 ⇒ 本来源功成身退
		if (Volatile.Read(ref _gpuTempRealSourceReady) == 1)
		{
			return SkipEarlyNvidiaSmi(1, "LHM/NVML 已给过有效值 ⇒ 已让位（✅ 正常，本来源不需要）");
		}
		int misses = Volatile.Read(ref _gpuTempSmiMisses);
		// ①b 彻底停用：连续失败太多次 ⇒ 推断本机没有可用的 NVIDIA 工具 ⚠️ ⇒ 不再 spawn ✅
		//   ⚠️ 这是"防无限起子进程"的兜底（⭐ 见 _gpuTempSmiMisses 的注释 ✅）
		if (misses >= NvidiaSmiGiveUpAfterMisses)
		{
			return SkipEarlyNvidiaSmi(2, $"连续 {misses} 次取不到值 ⇒ 已彻底停用"
				+ "（⚠️ 说明本机能找到 nvidia-smi 但**拿不到温度** ⚠️）");
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
			return false; // ⭐ 宽限期内（正常，⛔ 不记日志免刷屏）
		}
		// ③ TTL（⭐ 若已连续失败到退避阈值 ⇒ 用放大的 TTL ⚠️ —— 免得一直起子进程 ❌）
		int ttlMs = (misses >= NvidiaSmiBackoffAfterMisses) ? NvidiaSmiBackoffTtlMs : NvidiaSmiTtlMs;
		long lastAttemptTick = Interlocked.Read(in _gpuTempSmiLastAttemptTick);
		if (lastAttemptTick != 0L && now - lastAttemptTick < ttlMs)
		{
			return false; // ⭐ TTL 内（正常，⛔ 不记日志免刷屏）
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
			// ⭐ 厂商值有效 ⇒ ⭐ 永远不需要 nvidia-smi（⭐ GCU 机型就是这一支 ✅）
			return SkipEarlyNvidiaSmi(3, $"厂商已给 GPU 温度 {hw.GpuTemp}℃ ⇒ 不需要本来源（✅ 正常）");
		}
		// ⑤ 可执行文件存在
		if (ResolveNvidiaSmiPath() == null)
		{
			return SkipEarlyNvidiaSmi(4, "在本机三个候选路径里都**找不到 nvidia-smi.exe**"
				+ "（⭐ 已查 System32 ＋ 两个 Program Files 下的 `NVIDIA Corporation\\NVSMI` ✅）"
				+ " ⇒ ⚠️ 本来源在**本机不适用**；"
				+ "⚠️ 若本机确有 NVIDIA 独显，**这一行就是根因** ⚠️（⭐ 即 GPU 温度只能等 NVML，而那需要更久 ⚠️）");
		}
		return true;
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
	/// ⭐⭐ 2026-10-18（机主：遥测里 CPU 占用率显示不对）：⭐ <b>CPU 占用率本地采样</b> ✅
	/// —— ⭐ 来源：性能计数器 <c>Processor Information\_Total\% Processor Utility</c>（⭐ 任务管理器同源）
	/// ⇒ 不可用时退 <c>% Processor Time</c> ⇒ 再退 <c>GetSystemTimes</c> 差值 ✅。
	/// <para>⚠️ 旧链路只有 GCU/MQTT 推送（<c>hw.CpuUsage</c>）⇒ ⭐ 宝龙达（无 GCU）机型**根本没有来源** ❌。
	/// ⭐ 现在本地采；GCU 推送只作**最后兜底**（⭐ 且仅在 GCU 已连接时）✅。</para>
	/// <para>⚠️ 取不到 ⇒ **不更新时间戳**（⭐ 界面按 <see cref="UsageFreshAge"/> 判过期显示「—」，
	/// ⛔ 不显示 0% 冒充"占用 0%"）✅。</para>
	/// </summary>
	private static void SampleLocalCpuUsage()
	{
		int? usage = null;
		string source = "";
		int intervalMs = 0;
		CpuUsageSampler sampler = Volatile.Read(ref cpuUsageSampler);
		if (sampler != null)
		{
			int? sampled = sampler.Sample();
			if (sampled.HasValue && sampled.Value >= 0 && sampled.Value <= 100)
			{
				usage = sampled.Value;
				source = sampler.Source;
				intervalMs = sampler.LastIntervalMs;
			}
		}
		if (!usage.HasValue)
		{
			// ⭐ 兜底：GCU 推送（⭐ 只在 GCU 已连接时才有意义；⭐ 0% 也是合法值 ✅）
			MechrevoHw hw = TryGetHw();
			if (hw != null && hw.IsGcuConnected)
			{
				usage = Math.Clamp(hw.CpuUsage, 0, 100);
				source = "GCU 推送（兜底）";
				intervalMs = 0;
			}
		}
		if (!usage.HasValue)
		{
			return; // ⭐ 保持旧值、⛔ 不更新时间戳 ⇒ 上层判过期显示「—」✅
		}
		cpuUsage = usage;
		_cpuUsageSource = source;
		_cpuUsageIntervalMs = intervalMs;
		Interlocked.Exchange(ref _cpuUsageUpdatedTick, Environment.TickCount64);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：遥测里 GPU 占用率显示不对）：⭐ <b>GPU 占用率本地采样</b> ✅
	/// —— ⭐ 来源优先级：⭐ <b>NVML 核心利用率</b>（<c>nvmlDeviceGetUtilizationRates().gpu</c>，0–100，graphics 口径）
	/// ⇒ ⭐ <b>LHM GPU 核心负载</b>（⭐ 仅非 NVIDIA 机型；NVIDIA 机型 LHM GPU 那支被关 ⚠️）
	/// ⇒ ⭐ GCU 推送（⭐ 最后兜底）✅。
	/// <para>⚠️ **不是显存占用率**：显存占用仍由 <c>vramUsage = used/total</c> 单独提供；⛔ 两者不可混用 ✅。</para>
	/// <para>⚠️ NVML 是原生调用 ⇒ 调用点必须排在 <c>_localSamplingQuarantined</c> **之后**（⭐ 降级期一次不发 ✅）；
	/// ⚠️ 取不到 ⇒ **不更新时间戳** ⇒ 界面「—」（⛔ 不显示 0% 冒充）✅。</para>
	/// </summary>
	private static void SampleLocalGpuUsage()
	{
		int? usage = null;
		string source = "";
		string engine = "";
		int intervalMs = 0;
		if (!NvmlHelper.Unavailable)
		{
			int? nvml = NvmlHelper.GetGpuUtilization();
			if (nvml.HasValue && nvml.Value >= 0 && nvml.Value <= 100)
			{
				usage = nvml.Value;
				source = "NVML nvmlDeviceGetUtilizationRates";
				engine = "graphics(core)";
				// ⭐ 采样间隔 = NVML 快照 TTL（⭐ 同一快照复用，⛔ 不放大原生调用 ✅）
				intervalMs = NvmlHelper.SnapshotTtlMs;
			}
		}
		if (!usage.HasValue)
		{
			LhmMonitor lhmMonitor = lhm;
			if (lhmMonitor != null && !lhmMonitor.IsDisposed && lhmMonitor.GpuLoadPercent.HasValue)
			{
				usage = Math.Clamp(lhmMonitor.GpuLoadPercent.Value, 0, 100);
				source = "LHM Load 传感器";
				engine = (lhmMonitor.GpuLoadSensorName ?? "(未知)");
				intervalMs = 0;
			}
		}
		if (!usage.HasValue)
		{
			MechrevoHw hw = TryGetHw();
			if (hw != null && hw.IsGcuConnected)
			{
				usage = Math.Clamp(hw.GpuUsage, 0, 100);
				source = "GCU 推送（兜底）";
				engine = "(厂商口径)";
			}
		}
		if (!usage.HasValue)
		{
			return;
		}
		gpuUsage = usage;
		_gpuUsageSource = source;
		_gpuUsageEngine = engine;
		_gpuUsageIntervalMs = intervalMs;
		Interlocked.Exchange(ref _gpuUsageUpdatedTick, Environment.TickCount64);
	}

	/// <summary>⭐ 2026-10-18：⭐ 安全取 <see cref="Program.hw"/>（⭐ 启动早期可能为 null / 初始化中可能抛 ✅）。</summary>
	private static MechrevoHw TryGetHw()
	{
		try
		{
			return Program.hw;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>⭐ 2026-10-18：⭐ 占用率日志去重用的"上次已记录"状态 ✅。</summary>
	private static int _usageLastLoggedCpu = -1;

	private static int _usageLastLoggedGpu = -1;

	private static string _usageLastLoggedCpuSource = "";

	private static string _usageLastLoggedGpuSource = "";

	/// <summary>⭐ 2026-10-18：⭐ 采样间隔 → 人读文本（⭐ 诊断/日志共用 ✅）。</summary>
	private static string FormatUsageInterval(int intervalMs)
	{
		return (intervalMs <= 0) ? "未知" : (intervalMs + "ms");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：CPU/GPU 占用率显示不对）：⭐ <b>`[usage]` 诊断日志</b> ✅
	/// —— ⭐ 格式：<c>[usage] cpu=37%（来源=… · 采样间隔=1000ms）· gpu=12%（来源=… · engine=graphics(core) · 采样间隔=1500ms）</c> ✅
	/// <para>⭐ 去重：⭐ 值变化 ≥5 个百分点 / 来源变化 / 首次 ⇒ ⭐ 立即一行；否则 60 s 一条心跳（⛔ 不刷屏）✅。</para>
	/// <para>⚠️ 取不到新鲜值的那一项如实写 <c>—</c>（⭐ ⛔ 不写 0% 冒充 ✅）。</para>
	/// </summary>
	private static void LogUsageIfChanged()
	{
		bool cpuFresh = CpuUsageIsFresh(UsageFreshAge);
		bool gpuFresh = GpuUsageIsFresh(UsageFreshAge);
		int cpuVal = cpuFresh ? cpuUsage.GetValueOrDefault() : -1;
		int gpuVal = gpuFresh ? gpuUsage.GetValueOrDefault() : -1;
		string cpuPart = cpuFresh
			? ($"{cpuVal}%（来源={_cpuUsageSource} · 采样间隔={FormatUsageInterval(_cpuUsageIntervalMs)}）")
			: "—（没有新鲜值）";
		string gpuPart = gpuFresh
			? ($"{gpuVal}%（来源={_gpuUsageSource} · engine={_gpuUsageEngine} · 采样间隔={FormatUsageInterval(_gpuUsageIntervalMs)}）")
			: "—（没有新鲜值）";
		string line = $"[usage] cpu={cpuPart} · gpu={gpuPart}";
		bool significant = false;
		if (cpuFresh || gpuFresh)
		{
			significant = (cpuVal < 0 || gpuVal < 0)
				|| Math.Abs(cpuVal - _usageLastLoggedCpu) >= 5
				|| Math.Abs(gpuVal - _usageLastLoggedGpu) >= 5
				|| !string.Equals(_usageLastLoggedCpuSource, _cpuUsageSource, StringComparison.Ordinal)
				|| !string.Equals(_usageLastLoggedGpuSource, _gpuUsageSource, StringComparison.Ordinal);
		}
		_usageLastLoggedCpu = cpuVal;
		_usageLastLoggedGpu = gpuVal;
		_usageLastLoggedCpuSource = _cpuUsageSource;
		_usageLastLoggedGpuSource = _gpuUsageSource;
		if (significant)
		{
			Logger.WriteLine(line);
		}
		else
		{
			Logger.WriteLineThrottled("usage", line, 60000);
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
		// ⭐⭐ 2026-10-18（机主：CPU/GPU 占用率显示不对）：⭐ **CPU 占用率本地采样** ✅
		//   ⚠️ 放在 `_localSamplingQuarantined` / LHM/NVML 闸门**之前**：它只碰性能计数器 / GetSystemTimes
		//      （⭐ 不是 LHM/NVML 原生句柄 ✅）⇒ ⭐ 采样链降级时也能出值 ✅
		//   ⚠️ `countsAsHealthFailure:false` ⇒ ⛔ 性能计数器缺失不算"采样链坏了"✅
		if (_localMonitoringEnabled)
		{
			TryStep(SampleLocalCpuUsage, "cpu-usage-local", countsAsHealthFailure: false);
		}
		// ⭐⭐ 2026-10-18（机主：悬浮窗 CPU 温度停在开机时的温度）：⭐ **CPU 温度刷新** ✅
		//   ⚠️ 必须排在 `lhm == null` / `_localSamplingQuarantined` / LHM 两道闸门**之前** ——
		//      原因与上面 `cpu-freq-sample`、下面 `gpu-temp-nvml-early` 完全同款：⭐ LHM 一旦不可用/更新失败，
		//      闸门之后的 `ApplySensorPriority` 就**整段不跑** ⚠️ ⇒ ⭐ CPU 温度会永远停在启动值 ❌（本次机主现象）
		//   ⚠️ 三个来源（宝龙达功能号 22 / LHM / ACPI）里**没有一个是 LHM/NVML 的必需句柄**（LHM 只读缓存属性 ✅）
		//      ⇒ ⭐ 放在闸门之前**不削弱**原生层 AV 防护 ✅
		//   ⚠️ 只在监控已启用时做（⭐ 未启用/正在退出 ⇒ 一次原生调用都不发 ✅）；
		//      `countsAsHealthFailure:false` ⇒ ⛔ WMI/ACPI 抖一下不触发"采样链坏了 ⇒ 重建/降级"✅
		if (_localMonitoringEnabled)
		{
			TryStep(RefreshCpuTempFromLocalSources, "cpu-temp-local", countsAsHealthFailure: false);
		}
		// (d) 未启用/已释放：直接早退。
		//   ⭐⭐ 2026-10-16（GPU 温度「完全不显示」BUGFIX · p2 实证）：
		//     ⚠️ 本检查**必须排在 `_localSamplingQuarantined` 之前** —— 因为下面
		//     `RefreshEarlyGpuTempFromNvidiaSmi()` 用的是**独立子进程**
		//     （⛔ 不碰 LHM/NVML 原生句柄 ✅）⇒ ⭐ 它**不受"本地采样链降级"影响** ✅
		//     ⇒ ⭐ 隔离/降级期间也应该能补 GPU 温度 ✅（⭐ 与上方 `cpu-freq-sample` 同款处理 ——
		//        那条也刻意排在隔离检查之前，注释理由一模一样 ✅）。
		//   ⚠️ 而「LHM 被整体关掉」的配置下依然**逐位不变**（⭐ 这里就早退了 ✅）。
		LhmMonitor? lhmMonitor = lhm;
		if (lhmMonitor == null || lhmMonitor.IsDisposed)
		{
			// ⭐⭐ 2026-10-17（真机诊断包 0.291.9.9 定因）：⭐ LHM 正在后台重建 / 已释放时，
			//   GPU 温度**与 LHM 无关**（⭐ 宝龙达没有厂商 GPU 温度 ⇒ 只能来自 NVML ⚠️）
			//   ⇒ ⭐ 仍由 NVML 补一次 ✅（⭐ 否则重建那几秒里温度会闪成「—」❌）。
			//   ⚠️ 三条硬边界：① ⭐ 只有**监控仍处于启用状态**才做（⭐ 未启用/正在退出 ⇒ 一次原生调用都不发 ⚠️）；
			//     ② ⭐ 必须在 `_localSamplingQuarantined` 之外（⭐ 降级期一次原生调用都不发 ✅）；
			//     ③ ⭐ 包在 `countsAsHealthFailure:false` 里 ⇒ 失败只跳过本步、⛔ 不影响健康判定 ✅。
			//   ⭐ 分支内部的「厂商值优先」判据在 `RefreshGpuTempFromNvmlWhenNoVendor` 里，⛔ 未改 ✅
			//      （⭐ 厂商有值时它直接 return ✅）。
			if (_localMonitoringEnabled && !_localSamplingQuarantined)
			{
				TryStep(delegate
				{
					RefreshGpuTempFromNvmlWhenNoVendor();
				}, "gpu-temp-nvml-early", countsAsHealthFailure: false);
				// ⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ LHM 重建期间 GPU 占用率也照常刷（NVML，与 LHM 无关）✅
				TryStep(SampleLocalGpuUsage, "gpu-usage-nvml", countsAsHealthFailure: false);
			}
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
		//   ⭐⭐ 2026-10-16 二次修正：⭐ 同时**排在 `_localSamplingQuarantined` 之前** ✅
		//      （⭐ 见上方注释 —— 它不碰原生句柄，降级期间也该能取温度 ✅）。
		RefreshEarlyGpuTempFromNvidiaSmi();
		// (c) 降级中：一次原生调用都不发。
		if (_localSamplingQuarantined)
		{
			return;
		}
		// ⭐⭐ 2026-10-18（机主：GPU 占用率显示不对）：⭐ **GPU 占用率采样**（NVML 核心/graphics 利用率）✅
		//   ⚠️ 排在 `_localSamplingQuarantined` **之后**：NVML 是原生调用，降级期一次都不发 ✅
		//   ⚠️ `countsAsHealthFailure:false` ⇒ ⛔ NVML 单独查询失败不额外计入健康判定 ✅
		TryStep(SampleLocalGpuUsage, "gpu-usage-nvml", countsAsHealthFailure: false);
		// ⭐⭐ 2026-10-17（真机诊断包 `0.291.9.8-180311` 定因 · 修「4070 显卡温度又变回 —」）：
		//   证据：18:02:13~28 由 NVML 取到 41/42℃ 并写回门面层 ✅；之后 LHM 两道闸门失败 ⇒
		//     `ApplySensorPriority` 不再执行 ⇒ 门面层不再刷新 ⇒ `KeepAlive`(30 s) 到期 ⇒ 44 s 后界面又「—」❌
		//   ⇒ ⭐ 把「无厂商值时的 NVML 温度」提前到**两道 LHM 闸门之前** ✅
		//     ⚠️ 仍排在 `_localSamplingQuarantined` **之后** ⇒ ⛔ 不破坏"降级期间一次原生调用都不发"的保护 ✅
		//   ⚠️ 独立一步：失败只跳过本步（`countsAsHealthFailure:false` ⇒ ⛔ 不参与健康判定）✅
		TryStep(delegate
		{
			RefreshGpuTempFromNvmlWhenNoVendor();
		}, "gpu-temp-nvml-early", countsAsHealthFailure: false);
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
		// Intel iGPU（尤其星耀 PTL）：LHM 多数只给 D3D 负载；频率/功耗/温度缺失时
		// 尝试 Level Zero Sysman（只读），读到才覆盖，读不到保持「—」。
		if (CpuVendor.IsIntel)
		{
			try
			{
				if (IntelLevelZeroTelemetry.TryRead(out double? l0Freq, out double? l0Power, out double? l0Temp, out string l0Detail))
				{
					bool changed = false;
					if (l0Freq.HasValue && gpuCoreFreq <= 0)
					{
						gpuCoreFreq = (int)Math.Round(l0Freq.Value);
						changed = true;
					}
					if (l0Temp.HasValue && gpuTemp <= 0)
					{
						gpuTemp = (int)Math.Round(l0Temp.Value);
						changed = true;
					}
					if (l0Power.HasValue && !gpuPower.HasValue)
					{
						gpuPower = (float)l0Power.Value;
						changed = true;
					}
					if (Logger.TryMarkChanged("intel-l0-values", l0Detail))
					{
						Logger.WriteError("[intel-l0] " + l0Detail);
					}
					_ = changed;
				}
			}
			catch
			{
			}
		}
		// AMD 核显（无 NVIDIA 独显）⇒ **ADL PMLog 优先于 LHM**（只读；NVIDIA 机型一律不进）
		bool amdIgpu = CpuVendor.IsAmd && Program.hw != null && Program.hw.DgpuGeneration == DgpuGenerationKind.NoDgpu;
		// （机主更正：LHM 在 APU 上能读到核显功耗）：ADL 改为**仅当 LHM 没给值时的兜底**
		if (amdIgpu && !gpuPower.HasValue && AdlPowerReader.Available)
		{
			if (AdlPowerReader.TryRead(out float adlPower, out string adlSource))
			{
				// 部分 ADL 版本在 APU 上按 0.1W 上报（例如 400 实为 40.0W）；核显功耗不可能 >200W
				string adlUnitNote = "";
				if (adlPower > 200f)
				{
					float converted = adlPower / 10f;
					if (converted <= 200f)
					{
						adlUnitNote = "（原始值疑似 0.1W 单位 ⇒ 除以 10）";
						adlPower = converted;
					}
					else
					{
						Logger.WriteLine("[gpu-power] ⛔ ADL 核显功耗读数不可信（" + adlPower.ToString("F1") + "W，超出核显合理上限）⇒ 丢弃");
						adlPower = 0f;
					}
				}
				if (adlPower > 0f)
				{
					gpuPower = adlPower;
				}
				string adlSelected = "[gpu-power] AMD 核显功耗来源=" + adlSource + " ⇒ " + adlPower.ToString("F1") + "W" + adlUnitNote + "（优先于 LHM）";
				if (Logger.TryMarkChanged("gpu-power-adl-selected", adlSelected))
				{
					Logger.WriteError(adlSelected);
				}
			}
			else if (AdlPowerReader.Frozen)
			{
				// 冻结 ⇒ 悬浮窗显示「—」，⛔ 不回落到 LHM 的恒值 ✅
				gpuPower = null;
			}
		}
		// （机主：「4070 功耗也不对」）： 只读诊断 —— 把**原始来源**打出来（同值去重，不刷屏）
		Logger.WriteLineIfChanged("gpu-power-raw",
			$"[gpu-power] vendor={num2:F1}W · nvml={(gpuPowerViaNvml.HasValue ? gpuPowerViaNvml.Value.ToString("F1") : "null")}W · resolved={(gpuPower.HasValue ? gpuPower.Value.ToString("F1") : "null")}W");
		// ⭐⭐ 2026-10-17：⭐ 功耗字段**真的被刷新**了 ⇒ 打时间戳（⭐ 界面新鲜度判据只看这个 ✅）
		//   ⚠️ 必须放在这里（写值之后），⛔ 不能放方法入口 —— 入口到这儿之间有好几道早退，
		//      "来采过"≠"值刷新过" ⚠️（⭐ 见 `_lastPowerRefreshTick` 的注释 ✅）。
		Interlocked.Exchange(ref _lastPowerRefreshTick, Environment.TickCount64);
		// 核显（NoDgpu）：LHM 的 dedicated 字段通常只有一小段，真正用的是 D3D Shared Memory；
		// 有共享显存读数时优先它，避免无界/星耀核显显存占用一直是「—」。
		int lhmUsed = num6;
		int lhmTotal = num7;
		if (Program.hw != null && Program.hw.DgpuGeneration == DgpuGenerationKind.NoDgpu && lhmMonitor != null)
		{
			int sharedUsed = lhmMonitor.GpuSharedMemoryUsedMb;
			int sharedTotal = lhmMonitor.GpuSharedMemoryTotalMb;
			if (sharedUsed > 0 && sharedTotal > 0)
			{
				lhmUsed = sharedUsed;
				lhmTotal = sharedTotal;
				Logger.WriteLineIfChanged("gpu-vram-shared",
					"[gpu-vram] 核显共享显存 used=" + sharedUsed + "MB / total=" + sharedTotal + "MB");
			}
		}
		(long, long)? tuple = ((lhmUsed > 0 && lhmTotal > 0) ? ((long, long)?)null : memoryViaNvml);
		long num3 = ((lhmUsed > 0) ? lhmUsed : (tuple?.Item1 ?? 0));
		long num4 = ((lhmTotal > 0) ? lhmTotal : (tuple?.Item2 ?? 0));
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
		float upperBound = GpuPowerUpperBound();
		return SelectUsablePower(localPower, fallbackPower, upperBound);
	}

	/// <summary>GPU 功耗合理上限（NVML 功耗上限优先，其次 TGP 上限；笔记本兜底 350W）。</summary>
	private static float GpuPowerUpperBound()
	{
		if (NvmlHelper.TryGetPowerLimitBounds(out int _, out int nvmlMaxW, out int _) && nvmlMaxW > 0)
		{
			return nvmlMaxW + 50f;
		}
		int tgpMax = Program.hw?.GpuTgpMaximum ?? (-1);
		if (tgpMax > 0)
		{
			return tgpMax + 100f;
		}
		return 350f;
	}

	private static float? SelectUsablePower(float? localPower, float? fallbackPower, float upperBound = float.MaxValue)
	{
		if (!IsUsablePower(localPower, upperBound))
		{
			if (!IsUsablePower(fallbackPower, upperBound))
			{
				return null;
			}
			return fallbackPower;
		}
		return localPower;
	}

	private static bool IsUsablePower(float? power, float upperBound = float.MaxValue)
	{
		if (power.HasValue && power.GetValueOrDefault() > 0f && power.GetValueOrDefault() <= upperBound)
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
		SampleLocalPowerCore(includeAuxiliary: true);
	}

	/// <summary>
	/// ⭐ 2026-10-17：<see cref="SampleLocalPower"/> 的实体（⭐ 界面调用方走 <c>includeAuxiliary:true</c> ·
	/// ⭐ 常驻采样泵走 <c>false</c> ✅）。
	/// <para>⚠️ <paramref name="includeAuxiliary"/> = false 时**只跑采样链核心**（⭐ LHM/NVML/功耗/频率/显存 ✅
	/// ＋ ⭐ 健康判定/隔离到期重试 ✅），⛔ 不跑「功耗墙判定 / 供电方式 / 电池功率」——
	/// 那三项仍由主界面既有的 2 s 刷新（`SettingsForm.RefreshSensors`）负责，⛔ 不重复做 ⚠️。</para>
	/// </summary>
	private static void SampleLocalPowerCore(bool includeAuxiliary)
	{
		// (a) 异常防护：整条采样链在这里兜底（SampleLocalPower 会被 UI 线程的 2s Timer 直接调用，
		// 任何漏出来的异常都会穿过消息循环把进程带走）。
		try
		{
			// ⭐ 2026-10-17：⭐ 先记"有人来采过"（⭐ 采样泵据此让位，⛔ 不重复采样 ✅）。
			//   ⚠️ 与 `_lastPowerRefreshTick` 分工不同：那个在**值真的被写入**之后才打 ✅。
			Interlocked.Exchange(ref _lastLocalSampleTick, Environment.TickCount64);
			RefreshLocalMonitoring();
			// ⭐⭐ 2026-10-18（机主：CPU/GPU 占用率显示不对）：⭐ 占用率诊断日志
			//   （⭐ 值变化 ≥5 个百分点 / 来源变化 / 首次 ⇒ 立即一行；否则 60 s 心跳 ✅）
			TryStep(LogUsageIfChanged, "usage-log", countsAsHealthFailure: false);
			if (includeAuxiliary)
			{
				// 供电/电池刷新失败不影响「本地功耗采样链」的健康判定（见 TryStep 的 countsAsHealthFailure）。
				TryStep(ObservePowerWall, "powerwall", countsAsHealthFailure: false);
				TryStep(() => RefreshPowerInput(), "power-input", countsAsHealthFailure: false);
				TryStep(() => RefreshBatteryRate(), "battery-rate", countsAsHealthFailure: false);
			}
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
