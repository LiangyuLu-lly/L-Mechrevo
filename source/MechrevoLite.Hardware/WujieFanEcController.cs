using System;
using System.Collections.Generic;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界风扇曲线）：无界（WUJIE14XA）EC 风扇曲线控制器。
///
/// <para>通道：**I2EC 窗口**（端口 0x681/0x682/0x683，经本机已安装的 inpoutx64.dll）——
/// 这是无界 EC 风扇占空比实际可写的通道；占空比目标为主扇 0x1804、副扇 0x1809（0..200 = 0..100%），
/// 温度 0x043E，转速 0x0464/0x046C（大端）。</para>
///
/// <para>写入必须**25Hz 持续**（EC 热管理会周期性覆写；2Hz 被压制、10Hz 拉锯），并且两路必须同时写
/// （只写主扇会让副扇被 EC 斜坡挪到 0）。</para>
///
/// <para>安全：45℃ 以下不接管（EC 低温会停转）；副扇 20% 下限；≥85℃ 强制 100%；≥95℃ 停止交还 EC；
/// 副扇连续约 12 秒 0 RPM 停止；温度读失败约 2.4 秒、写失败约 1 秒、回读持续不一致均停止。
/// ⛔ 停止时不做渐进降速。</para>
/// </summary>
internal sealed class WujieFanEcController : IDisposable
{
	private const int WriteIntervalMs = 40;

	private const int SampleIntervalMs = 500;

	private const int Fan2ZeroSamples = 24;

	private const int MaxTempFailTicks = 60;

	private const int MaxWriteFailTicks = 25;

	private const int MaxReadbackMismatchSamples = 3;

	/// <summary>2026-10-22：写入通道选择 —— I2EC（inpoutx64，实测可写）优先；没有驱动时回退 ACPI-EC 0x93（实验）。</summary>
	private enum WriteChannel
	{
		None,
		I2ec,
		AcpiEc
	}

	/// <summary>
	/// ⭐⭐ 2026-10-27（task-43）：控制后端 —— <see cref="Table" />（曲线表+锁存，默认优先）/
	/// <see cref="Direct" />（25Hz 直写 0x1804/0x1809，回退/对照）。✅
	/// <para>⭐ table 优点：目标不变时稳态**零写入**（写表只在目标变化/10s 心跳漂移时），
	/// 撕裂写暴露窗口从 ~50 次/秒 降到“每次目标变化 21 次写”；缺点：受 EC 软着陆斜坡限制
	/// （目标变化时补写一次 0x1804/0x1809 跳坡）。</para>
	/// </summary>
	private enum ControlBackend
	{
		Direct,
		Table
	}

	/// <summary>table 后端温度采样周期（ms）：25Hz 定时器里只每 500ms 读一次温度，减少端口读压力。</summary>
	private const int TableTempSampleMs = 500;

	/// <summary>table 后端温度连续读失败样本数（6 × 500ms ≈ 3s）⇒ 交还 EC。</summary>
	private const int TableTempFailSamples = 6;

	/// <summary>table 写表连续失败次数 ⇒ 自动回退 direct 后端。</summary>
	private const int TableWriteFailLimit = 3;

	/// <summary>心跳连续偏离多少次判定“别的程序争用 EC”⇒ 放慢重锁间隔（不跟着加码狂写）。</summary>
	private const int TableContentionLimit = 5;

	/// <summary>判定争用后的心跳/重锁间隔（ms）。</summary>
	private const int TableContentionHeartbeatMs = 30000;

	private WriteChannel _channel;

	private ControlBackend _backend = ControlBackend.Direct;

	// ---- table 后端状态（task-43）----
	private int _tableLatchBase;
	private int _tableIsolateBase;
	private bool _tableActive;
	private int _tableLastRaw1 = -1;
	private int _tableLastRaw2 = -1;
	private long _tableTempSampleTick;
	private long _tableLastWriteTick;
	private int _tableHeartbeatMs = WujieFanEc.TableHeartbeatMs;
	private int _tableContentionCount;
	private int _tableWriteFailCount;
	private bool _lowTempHandoff;

	/// <summary>低温交还后重新接管的温度（℃，= 45 + 3）✅</summary>
	internal const int HandoffResumeTemperatureC = WujieFanEc.HandoffResumeTemperatureC;

	/// <summary>2026-10-22（机主：45℃ 以下风扇会停转、写不进去）：低温区不写占空比，交还 EC 原生策略。</summary>
	internal const int MinimumOperatingTemperatureC = 45;

	private readonly Timer _timer;

	private readonly object _gate = new object();

	private static WujieFanEcController _active;

	private byte[] _cpuTemps = Array.Empty<byte>();

	private int[] _cpuDuties = Array.Empty<int>();

	private int _validCpu;

	private byte[] _gpuTemps = Array.Empty<byte>();

	private int[] _gpuDuties = Array.Empty<int>();

	private int _validGpu;

	private bool _independent;

	private bool _running;

	private int _fan2ZeroCount;

	private int _tempFailTicks;

	private int _writeFailTicks;

	private int _readbackMismatchSamples;

	private int _lastTempC = -1;

	private int _lastRaw = -1;

	private long _lastSampleTick;

	private int _lastTargetPercent = -1;

	internal WujieFanEcController()
	{
		_timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
	}

	internal bool Running => _running;

	internal string LastDetail { get; private set; } = "";

	/// <summary>启动曲线控制：CPU/GPU 两套温度与占空比数组（长度 16，0xFF 表示无效点）；
	/// independent=true 时主扇按 CPU 曲线、副扇按 GPU 曲线分别输出。</summary>
	/// <summary>2026-10-22：无界保存的只有占空比点位（官方表可能读失败/无温度轴）⇒ 开机自启用固定温度阶梯 45..90℃（16 点）。</summary>
	internal static byte[] DefaultTemperatures()
	{
		byte[] temps = new byte[16];
		for (int i = 0; i < temps.Length; i++)
		{
			temps[i] = (byte)(45 + i * 3);
		}
		return temps;
	}

	internal bool Start(byte[] cpuTemps, int[] cpuDuties, bool independent, byte[] gpuTemps, int[] gpuDuties, out string detail)
	{
		if (!WujieFanEc.IsWujiePlatform())
		{
			detail = "不是无界机型";
			return false;
		}
		if (cpuTemps == null || cpuDuties == null || cpuTemps.Length == 0 || cpuDuties.Length == 0)
		{
			detail = "CPU 曲线数据为空";
			return false;
		}
		if (independent && (gpuTemps == null || gpuDuties == null || gpuTemps.Length == 0 || gpuDuties.Length == 0))
		{
			detail = "独立控制需要 GPU 曲线，但 GPU 曲线为空";
			return false;
		}
		// 2026-10-22（无界集成）：随包 DLL/sys 在，但驱动没装时先尝试自动安装一次，再验通道。
		if (!I2ecInpOut.IsAvailable)
		{
			InpOutDriverInstaller.TryEnsureInstalled(out string installDetail);
			Logger.WriteLine("[fan] 无界 EC 曲线：驱动自动安装结果=" + installDetail);
		}
		// 启动前先验通道：I2EC（inpoutx64）优先；没有驱动时回退 ACPI-EC 0x93（实验路径）。
		WriteChannel channel = WriteChannel.None;
		int temp = -1;
		string i2ecDetail = "";
		if (I2ecInpOut.IsAvailable
			&& I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterMode, out int mode, out string modeDetail))
		{
			if ((mode & 0x03) != 0x03)
			{
				detail = "I2EC 不是读写模式（0x200D=0x" + mode.ToString("X2") + "）";
				return false;
			}
			if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterCpuTemp, out temp, out string tempDetail) || temp < 0 || temp > 120)
			{
				detail = "读取 CPU 温度失败（0x043E，I2EC）：" + tempDetail;
				return false;
			}
			channel = WriteChannel.I2ec;
		}
		else
		{
			i2ecDetail = I2ecInpOut.IsAvailable ? "I2EC 读失败" : "inpoutx64.dll 未安装";
			if (!EcPortIo.TryReadByteStable(WujieFanEc.RegisterMode, out int modeAcpi, out string modeAcpiNote)
				|| (modeAcpi & 0x03) != 0x03)
			{
				detail = "无可用 EC 通道（I2EC：" + i2ecDetail + "；ACPI-EC：" + modeAcpiNote + "）";
				return false;
			}
			if (!EcPortIo.TryReadByte(WujieFanEc.RegisterCpuTemp, out temp) || temp < 0 || temp > 120)
			{
				detail = "无可用 EC 通道（I2EC：" + i2ecDetail + "；ACPI-EC 读温度失败）";
				return false;
			}
			channel = WriteChannel.AcpiEc;
		}
		// ⭐⭐ 2026-10-27（task-43）：选择后端 —— table 默认优先；显式配置 direct / table 初始化失败时回退 direct。
		ControlBackend backend = ControlBackend.Direct;
		int latchBase = 0;
		int isolateBase = 0;
		string backendDetail = "";
		SelectBackend(channel, out backend, out latchBase, out isolateBase, out backendDetail);
		if (backend == ControlBackend.Direct)
		{
			// direct 路径不依赖表；但要清理**上次崩溃/强杀遗留**的锁存/隔离位（否则 EC 可能仍在按旧表跑）。
			ClearStaleTableStateBestEffort();
		}
		StopActive("被新的曲线控制器替换");
		lock (_gate)
		{
			_independent = independent;
			_cpuTemps = (byte[])cpuTemps.Clone();
			_cpuDuties = (int[])cpuDuties.Clone();
			_validCpu = CountValidPoints(_cpuTemps, _cpuDuties);
			if (independent)
			{
				_gpuTemps = (byte[])gpuTemps.Clone();
				_gpuDuties = (int[])gpuDuties.Clone();
				_validGpu = CountValidPoints(_gpuTemps, _gpuDuties);
			}
			else
			{
				_gpuTemps = (byte[])_cpuTemps.Clone();
				_gpuDuties = (int[])_cpuDuties.Clone();
				_validGpu = _validCpu;
			}
			_fan2ZeroCount = 0;
			_tempFailTicks = 0;
			_writeFailTicks = 0;
			_readbackMismatchSamples = 0;
			_lastTempC = temp;
			_lastRaw = -1;
			_lastSampleTick = Environment.TickCount64;
			_lastTargetPercent = -1;
			_backend = backend;
			_tableLatchBase = latchBase;
			_tableIsolateBase = isolateBase;
			_tableActive = false;
			_tableLastRaw1 = -1;
			_tableLastRaw2 = -1;
			_tableTempSampleTick = Environment.TickCount64;
			_tableLastWriteTick = Environment.TickCount64;
			_tableHeartbeatMs = WujieFanEc.TableHeartbeatMs;
			_tableContentionCount = 0;
			_tableWriteFailCount = 0;
			_lowTempHandoff = false;
			_running = true;
			_channel = channel;
			_active = this;
			// 2026-10-23（机主：电池 2ms 下风扇掉写）：风扇运行期间给电池循环一个跨进程优先标记。
			I2ecInpOut.SetFanWritePriority(channel == WriteChannel.I2ec);
			_timer.Change(WriteIntervalMs, WriteIntervalMs);
		}
		detail = "已启动（" + ChannelName(channel) + " · " + backendDetail + " · " + (independent ? "独立控制" : "同步控制")
			+ " · CPU " + _validCpu + " 点 / GPU " + _validGpu + " 点 · 当前 " + temp + "℃）";
		Logger.WriteLine("[fan] 无界 EC 曲线启动：" + detail);
		return true;
	}

	/// <summary>
	/// ⭐ task-43：后端选择。默认 **table 优先**；`wujie_fan_backend=direct|duty` 显式强制旧 25Hz 直写；
	/// table 初始化失败（读不到 0x0727/0x07C5 或通道不是 I2EC）⇒ 自动回退 direct 并在日志说明原因。
	/// </summary>
	private static void SelectBackend(WriteChannel channel, out ControlBackend backend, out int latchBase, out int isolateBase, out string backendDetail)
	{
		backend = ControlBackend.Direct;
		latchBase = 0;
		isolateBase = 0;
		string configured = (MechrevoLite.AppConfig.GetString("wujie_fan_backend") ?? "").Trim();
		if (configured.Equals("direct", StringComparison.OrdinalIgnoreCase)
			|| configured.Equals("duty", StringComparison.OrdinalIgnoreCase))
		{
			backendDetail = "direct 后端（配置 wujie_fan_backend=" + configured + "）";
			Logger.WriteLine("[fan] 无界 EC：配置强制 direct 25Hz 直写（wujie_fan_backend=" + configured + "）");
			return;
		}
		if (channel != WriteChannel.I2ec)
		{
			backendDetail = "direct 后端（通道=" + ChannelName(channel) + "，table 需要 I2EC 批量写）";
			Logger.WriteLine("[fan] 无界 EC：table 不可用 ⇒ " + backendDetail);
			return;
		}
		if (configured.Length > 0
			&& !configured.Equals("table", StringComparison.OrdinalIgnoreCase)
			&& !configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			Logger.WriteError("[fan] 无界 EC：未知 wujie_fan_backend=" + configured + " ⇒ 按默认策略尝试 table");
		}
		if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFanTableLatch, out int latch, out string latchDetail))
		{
			backendDetail = "direct 后端（table 初始化失败：读 0x0727 失败 " + latchDetail + "）";
			Logger.WriteError("[fan] 无界 EC：table 初始化失败，自动回退 direct ⇒ " + latchDetail);
			return;
		}
		if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFanIsolate, out int isolate, out string isolateDetail))
		{
			backendDetail = "direct 后端（table 初始化失败：读 0x07C5 失败 " + isolateDetail + "）";
			Logger.WriteError("[fan] 无界 EC：table 初始化失败，自动回退 direct ⇒ " + isolateDetail);
			return;
		}
		latchBase = latch & ~0x40;
		isolateBase = isolate & ~0x80;
		backend = ControlBackend.Table;
		backendDetail = "table 后端（0x0F20/0x0F50 前 8 字节 + 0x0727 bit6 锁存 + 0x07C5 bit7 隔离；latch_base=0x"
			+ latchBase.ToString("X2") + " isolate_base=0x" + isolateBase.ToString("X2") + "）";
		bool tableSelfCheck = RunTableSelfCheck(out string tableSelfCheckDetail);
		Logger.WriteLine("[fan] 无界 EC：选择 table 后端优先（latch_base=0x" + latchBase.ToString("X2")
			+ " isolate_base=0x" + isolateBase.ToString("X2") + "；目标不变时稳态零写入，10s 心跳校验）"
			+ " · 计划自检=" + tableSelfCheck + "（" + tableSelfCheckDetail + "）");
	}

	/// <summary>
	/// direct 后端启动前：清理**上次崩溃/强杀**遗留的 table 锁存/隔离位（绝对写，read-modify-write 危险）。
	/// <para>⚠️ 只读一次 + 按基线绝对写；读不到就跳过（⛔ 不猜、⛔ 不伪造）。</para>
	/// </summary>
	private static void ClearStaleTableStateBestEffort()
	{
		try
		{
			if (!I2ecInpOut.IsAvailable)
			{
				return;
			}
			if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFanTableLatch, out int latch, out _)
				|| !I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFanIsolate, out int isolate, out _))
			{
				return;
			}
			if ((latch & 0x40) == 0 && (isolate & 0x80) == 0)
			{
				return;
			}
			int latchBase = latch & ~0x40;
			int isolateBase = isolate & ~0x80;
			List<I2ecInpOut.I2ecWriteStep> steps = new()
			{
				new(WujieFanEc.RegisterFanTableLatch, latchBase),
				new(WujieFanEc.RegisterFanIsolate, isolateBase)
			};
			List<I2ecInpOut.I2ecVerifyStep> verify = new()
			{
				new(WujieFanEc.RegisterFanTableLatch, latchBase),
				new(WujieFanEc.RegisterFanIsolate, isolateBase)
			};
			bool ok = I2ecInpOut.TryWriteEcSequence(steps, verify, out string detail);
			if (ok)
			{
				Logger.WriteError("[fan] 无界 EC：检测到遗留 table 锁存/隔离（0x" + latch.ToString("X2") + "/0x"
					+ isolate.ToString("X2") + "）⇒ 已清回基线并交还 EC");
			}
			else
			{
				Logger.WriteError("[fan] 无界 EC：⚠️ 遗留 table 锁存/隔离清理失败（" + detail + "）⇒ direct 直写仍会覆盖目标，但 EC 可能保留旧表设置");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[fan] 无界 EC：遗留 table 状态清理异常（已吞掉）：" + ex.Message);
		}
	}

	/// <summary>停止写入，交还 EC（EC 会在 6~8 秒内接管；不做渐进降速）。</summary>
	internal void Stop(string reason)
	{
		bool wasRunning;
		bool releaseTable;
		lock (_gate)
		{
			wasRunning = _running;
			releaseTable = _backend == ControlBackend.Table && _tableActive;
			_running = false;
			_timer.Change(Timeout.Infinite, Timeout.Infinite);
			_fan2ZeroCount = 0;
			_tempFailTicks = 0;
			_writeFailTicks = 0;
			_readbackMismatchSamples = 0;
			if (reason.Length > 0)
			{
				LastDetail = reason;
			}
		}
		// ⭐ task-43：table 后端停止/交还前必须**清锁存+清隔离**（绝对写）；否则 EC 会一直按最后一张表跑。
		if (releaseTable)
		{
			ReleaseTable("停止/" + reason);
		}
		if (wasRunning)
		{
			Logger.WriteLine("[fan] 无界 EC 曲线停止：" + reason + "（交还 EC）");
		}
		I2ecInpOut.SetFanWritePriority(false);
		if (ReferenceEquals(_active, this))
		{
			_active = null;
		}
	}

	public void Dispose()
	{
		Stop("窗口关闭");
		_timer.Dispose();
	}

	/// <summary>停掉当前活动的无界风扇曲线控制器（例如强冷开启时；强冷本身仍走 GCU）。</summary>
	internal static void StopActive(string reason)
	{
		WujieFanEcController active = _active;
		if (active != null)
		{
			active.Stop(reason);
		}
	}

	private void OnTick(object state)
	{
		lock (_gate)
		{
			if (!_running)
			{
				return;
			}
		}
		if (_channel == WriteChannel.I2ec)
		{
			// 心跳：让电池保持循环持续看到“风扇正在写”。
			I2ecInpOut.SetFanWritePriority(true);
		}
		try
		{
			// ⭐⭐ 2026-10-27（task-43）：table 后端 —— 25Hz 定时器只用于采样/看门狗，**写表只在目标变化/心跳漂移时**。
			if (_backend == ControlBackend.Table)
			{
				OnTickTable();
				return;
			}
			// ① 温度：读失败时短暂沿用上次值继续维持写入；超过约 2.4 秒仍失败则交还 EC。
			bool tempOk = ReadEc(WujieFanEc.RegisterCpuTemp, out int temp) && temp >= 0 && temp <= 120;
			if (!tempOk)
			{
				_tempFailTicks++;
				if (_tempFailTicks >= MaxTempFailTicks)
				{
					Stop("连续读 CPU 温度失败（I2EC），交还 EC");
					LastDetail = "I/O 失败已停止";
					return;
				}
				temp = _lastTempC;
			}
			else
			{
				_tempFailTicks = 0;
				_lastTempC = temp;
			}
			if (temp < 0)
			{
				return;
			}
			if (temp >= 95)
			{
				Stop("温度 " + temp + "℃ ≥ 95℃，交还 EC 自动控速");
				LastDetail = "温度过高已交还 EC";
				return;
			}
			if (temp < MinimumOperatingTemperatureC)
			{
				// 45℃ 以下 EC 本来就会让风扇停转/低转；暂停写入并交还 EC，不做看门狗计数。
				_lastRaw = -1;
				_fan2ZeroCount = 0;
				if (_lastTargetPercent != -1)
				{
					_lastTargetPercent = -1;
					Logger.WriteLine("[fan] 无界 EC 曲线：温度 " + temp + "℃ < 45℃，暂停写入并交还 EC");
				}
				return;
			}
			int cpuTarget = (temp >= 85) ? 100 : ResolveDuty(_cpuTemps, _cpuDuties, _validCpu, temp);
			int secondaryTemp = temp;
			if (_independent && ReadEc(0x044F, out int gpuTemp) && gpuTemp > 0 && gpuTemp <= 120)
			{
				secondaryTemp = gpuTemp;
			}
			int gpuTarget = (secondaryTemp >= 85) ? 100 : ResolveDuty(_gpuTemps, _gpuDuties, _validGpu, secondaryTemp);
			if (cpuTarget > 0 && cpuTarget < WujieFanEc.FanFloorPercent)
			{
				cpuTarget = WujieFanEc.FanFloorPercent;
			}
			if (gpuTarget > 0 && gpuTarget < WujieFanEc.FanFloorPercent)
			{
				gpuTarget = WujieFanEc.FanFloorPercent;
			}
			int raw = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(cpuTarget * 2.0)));
			int rawSecondary = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(gpuTarget * 2.0)));
			bool ok1 = WriteDuty(WujieFanEc.RegisterDutyFan1, raw, out string writeDetail1);
			bool ok2 = WriteDuty(WujieFanEc.RegisterDutyFan2, rawSecondary, out string writeDetail2);
			if (!ok1 || !ok2)
			{
				_writeFailTicks++;
				if (_writeFailTicks >= MaxWriteFailTicks)
				{
					Stop("连续写占空比失败（" + (ok1 ? writeDetail2 : writeDetail1) + "），交还 EC");
					LastDetail = "写入失败已停止";
					return;
				}
			}
			else
			{
				_writeFailTicks = 0;
				_lastRaw = raw;
				int combined = (_independent ? (cpuTarget * 1000 + gpuTarget) : cpuTarget);
				if (combined != _lastTargetPercent)
				{
					_lastTargetPercent = combined;
					Logger.WriteLineIfChanged("wujie-fan-target", $"[fan] 无界 EC 曲线：{temp}℃ → CPU {cpuTarget}% (raw={raw}) · 副扇 {gpuTarget}% (raw={rawSecondary}) · {(_independent ? "独立" : "同步")}（I2EC）");
				}
			}
			// ② 每 500ms 采样一次：回读一致性 + 副扇堵转看门狗。
			if (Environment.TickCount64 - _lastSampleTick >= SampleIntervalMs)
			{
				_lastSampleTick = Environment.TickCount64;
				if ((ReadEc(WujieFanEc.RegisterDutyFan1, out int readBack) && readBack != raw) || (ReadEc(WujieFanEc.RegisterDutyFan2, out int readBackSecondary) && readBackSecondary != rawSecondary))
				{
					_readbackMismatchSamples++;
					if (_readbackMismatchSamples >= MaxReadbackMismatchSamples)
					{
						Stop("占空比回读持续不一致（主扇写 " + raw + "·副扇写 " + rawSecondary + "），交还 EC");
						LastDetail = "回读不一致已停止";
						return;
					}
				}
				else
				{
					_readbackMismatchSamples = 0;
				}
				if (TryReadFan2Rpm(out int fan2Rpm) && fan2Rpm <= 0)
				{
					_fan2ZeroCount++;
					if (_fan2ZeroCount >= Fan2ZeroSamples)
					{
						Stop("副扇连续 0 RPM（疑似被 EC 判堵转），交还 EC");
						LastDetail = "副扇 0 RPM 看门狗触发";
					}
				}
				else
				{
					_fan2ZeroCount = 0;
				}
			}
		}
		catch (Exception ex)
		{
			Stop("控制器异常（" + ex.GetType().Name + "），交还 EC");
			LastDetail = "控制器异常已停止";
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-27（task-43）：table 后端的一步。只在目标变化时写表+锁存；稳态只做 10s 心跳校验，
	/// EC 偏离目标（&gt; 8 raw）才重锁。低温 &lt;45℃ 释放表交还 EC，≥48℃ 重新接管（3℃ 回差）。
	/// </summary>
	private void OnTickTable()
	{
		long now = Environment.TickCount64;
		int temp = _lastTempC;
		if (now - _tableTempSampleTick >= TableTempSampleMs)
		{
			_tableTempSampleTick = now;
			bool tempOk = ReadEc(WujieFanEc.RegisterCpuTemp, out int freshTemp)
				&& freshTemp >= 0 && freshTemp <= 120;
			if (!tempOk)
			{
				_tempFailTicks++;
				if (_tempFailTicks >= TableTempFailSamples)
				{
					Stop("连续读 CPU 温度失败（table，约 3 秒），交还 EC");
					LastDetail = "I/O 失败已停止";
					return;
				}
			}
			else
			{
				_tempFailTicks = 0;
				temp = freshTemp;
				_lastTempC = freshTemp;
			}
		}
		if (temp < 0)
		{
			return;
		}
		if (temp >= 95)
		{
			Stop("温度 " + temp + "℃ ≥ 95℃，交还 EC 自动控速");
			LastDetail = "温度过高已交还 EC";
			return;
		}
		// ⭐ 低温交还：<45℃ 释放表（清锁存+隔离）把控制权交还 EC —— 避免低温时我们的表压住 EC 的省电策略。
		if (temp < MinimumOperatingTemperatureC)
		{
			if (!_lowTempHandoff)
			{
				_lowTempHandoff = true;
				_lastTargetPercent = -1;
				Logger.WriteLine("[fan] 无界 table：温度 " + temp + "℃ < " + MinimumOperatingTemperatureC
					+ "℃ ⇒ 释放表并交还 EC（≥" + HandoffResumeTemperatureC + "℃ 重新接管，3℃ 回差）");
			}
			if (_tableActive)
			{
				ReleaseTable("低温交还 EC（" + temp + "℃）");
			}
			_tableLastRaw1 = -1;
			_tableLastRaw2 = -1;
			_fan2ZeroCount = 0;
			return;
		}
		if (_lowTempHandoff)
		{
			if (temp < HandoffResumeTemperatureC)
			{
				return;
			}
			_lowTempHandoff = false;
			Logger.WriteLine("[fan] 无界 table：温度 " + temp + "℃ ≥ " + HandoffResumeTemperatureC + "℃ ⇒ 重新接管（3℃ 回差）");
		}
		int cpuTarget = (temp >= 85) ? 100 : ResolveDuty(_cpuTemps, _cpuDuties, _validCpu, temp);
		int secondaryTemp = temp;
		if (_independent && ReadEc(0x044F, out int gpuTemp) && gpuTemp > 0 && gpuTemp <= 120)
		{
			secondaryTemp = gpuTemp;
		}
		int gpuTarget = (secondaryTemp >= 85) ? 100 : ResolveDuty(_gpuTemps, _gpuDuties, _validGpu, secondaryTemp);
		if (cpuTarget > 0 && cpuTarget < WujieFanEc.FanFloorPercent)
		{
			cpuTarget = WujieFanEc.FanFloorPercent;
		}
		if (gpuTarget > 0 && gpuTarget < WujieFanEc.FanFloorPercent)
		{
			gpuTarget = WujieFanEc.FanFloorPercent;
		}
		int raw1 = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(cpuTarget * 2.0)));
		int raw2 = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(gpuTarget * 2.0)));
		if (raw1 != _tableLastRaw1 || raw2 != _tableLastRaw2)
		{
			if (!ApplyTable(raw1, raw2, "目标变化"))
			{
				return;
			}
			_tableLastRaw1 = raw1;
			_tableLastRaw2 = raw2;
			_tableLastWriteTick = now;
			_tableContentionCount = 0;
			_tableWriteFailCount = 0;
			int combined = _independent ? (cpuTarget * 1000 + gpuTarget) : cpuTarget;
			if (combined != _lastTargetPercent)
			{
				_lastTargetPercent = combined;
				Logger.WriteLineIfChanged("wujie-fan-target", $"[fan] 无界 table：{temp}℃ → CPU {cpuTarget}% (raw={raw1}) · 副扇 {gpuTarget}% (raw={raw2}) · {(_independent ? "独立" : "同步")}（0x0F20/0x0F50 表 + 0x0727 锁存）");
			}
		}
		else if (now - _tableLastWriteTick >= _tableHeartbeatMs)
		{
			// ⭐ 10s 心跳：先读目标，只有 EC 真的偏离才重锁 —— 稳态零写入（防别的程序覆盖表/目标）。
			_tableLastWriteTick = now;
			int cur1 = -1;
			int cur2 = -1;
			bool drift = false;
			if (ReadEc(WujieFanEc.RegisterDutyFan1, out int read1))
			{
				cur1 = read1;
				drift |= Math.Abs(read1 - raw1) > WujieFanEc.TableDriftToleranceRaw;
			}
			if (ReadEc(WujieFanEc.RegisterDutyFan2, out int read2))
			{
				cur2 = read2;
				drift |= Math.Abs(read2 - raw2) > WujieFanEc.TableDriftToleranceRaw;
			}
			if (drift)
			{
				_tableContentionCount++;
				if (!ApplyTable(raw1, raw2, "心跳漂移重锁"))
				{
					return;
				}
				if (_tableContentionCount >= TableContentionLimit)
				{
					_tableHeartbeatMs = TableContentionHeartbeatMs;
					Logger.WriteError("[fan] 无界 table：心跳连续 " + _tableContentionCount + " 次发现目标被改回（0x1804="
						+ cur1 + " / 0x1809=" + cur2 + "）⇒ 疑似别的程序争用 EC；重锁间隔放慢到 30s（⛔ 不跟着加码狂写）");
				}
				else
				{
					Logger.WriteLine("[fan] 无界 table：心跳发现偏离（0x1804=" + cur1 + " / 0x1809=" + cur2 + "）⇒ 重锁一次");
				}
			}
			else
			{
				_tableContentionCount = 0;
			}
		}
		SampleFan2Watchdog();
	}

	/// <summary>
	/// ⭐ task-43：构造 table 写入计划（纯函数，便于离线自检）；顺序 = 表1 前 8 → 表2 前 8 → 隔离 → 清锁存 → 置锁存(50ms) → 补写占空比。
	/// </summary>
	private static void BuildTableWritePlan(int raw1, int raw2, int latchBase, int isolateBase,
		out List<I2ecInpOut.I2ecWriteStep> steps, out List<I2ecInpOut.I2ecVerifyStep> verify, out bool needIsolate)
	{
		needIsolate = raw1 != raw2;
		int isolateWant = needIsolate ? (isolateBase | 0x80) : (isolateBase & ~0x80);
		steps = new List<I2ecInpOut.I2ecWriteStep>(WujieFanEc.FanTableActiveLength * 2 + 5);
		for (int i = 0; i < WujieFanEc.FanTableActiveLength; i++)
		{
			steps.Add(new I2ecInpOut.I2ecWriteStep(WujieFanEc.RegisterFanTableFan1 + i, raw1));
		}
		for (int i = 0; i < WujieFanEc.FanTableActiveLength; i++)
		{
			steps.Add(new I2ecInpOut.I2ecWriteStep(WujieFanEc.RegisterFanTableFan2 + i, raw2));
		}
		// 锁存前先摆好隔离位（0→1 跳变时 EC 就看到最终状态），再清锁存 → 50ms → 置锁存。
		steps.Add(new I2ecInpOut.I2ecWriteStep(WujieFanEc.RegisterFanIsolate, isolateWant, 20));
		steps.Add(new I2ecInpOut.I2ecWriteStep(WujieFanEc.RegisterFanTableLatch, latchBase & ~0x40));
		steps.Add(new I2ecInpOut.I2ecWriteStep(WujieFanEc.RegisterFanTableLatch, latchBase | 0x40, WujieFanEc.TableLatchPulseMs));
		// 不补写 0x1804/0x1809：fanctl 的 table 语义是让 EC 自己沿软着陆斜坡走到表目标；
		// 直接写目标寄存器会和 EC 的斜坡/表状态互相争用，且回读可能永远不相等（导致误判写表失败）。
		verify = new List<I2ecInpOut.I2ecVerifyStep>
		{
			new(WujieFanEc.RegisterFanIsolate, isolateWant),
			new(WujieFanEc.RegisterFanTableLatch, latchBase | 0x40)
		};
	}

	/// <summary>
	/// ⭐ task-43 离线自检（⛔ 不访问 EC/不写任何端口）：校验常量与写入计划形状
	/// （8+8 表字节、隔离位、锁存脉冲 50ms、斜坡补写、同步时清隔离）。供启动日志/离线 harness 调用。
	/// </summary>
	internal static bool RunTableSelfCheck(out string detail)
	{
		try
		{
			// 用局部变量比较，避免编译器把“常量相等”折叠成恒假条件（CS0162 不可达代码）。
			int expectedTableLength = 8;
			int expectedHeartbeatMs = 10000;
			int expectedDriftRaw = 8;
			int expectedPulseMs = 50;
			int expectedResumeC = MinimumOperatingTemperatureC + 3;
			if (WujieFanEc.FanTableActiveLength != expectedTableLength
				|| WujieFanEc.TableHeartbeatMs != expectedHeartbeatMs
				|| WujieFanEc.TableDriftToleranceRaw != expectedDriftRaw
				|| WujieFanEc.TableLatchPulseMs != expectedPulseMs
				|| WujieFanEc.HandoffResumeTemperatureC != expectedResumeC)
			{
				detail = "常量与设计不一致（table 长度/心跳/漂移/脉冲/回差）";
				return false;
			}
			BuildTableWritePlan(60, 120, 0x00, 0x00, out List<I2ecInpOut.I2ecWriteStep> steps,
				out List<I2ecInpOut.I2ecVerifyStep> verify, out bool needIsolate);
			if (!needIsolate || steps.Count != 19 || verify.Count != 2)
			{
				detail = "计划形状不对：steps=" + steps.Count + " verify=" + verify.Count + " isolate=" + needIsolate;
				return false;
			}
			if (steps[0].Address != WujieFanEc.RegisterFanTableFan1 || steps[0].Value != 60
				|| steps[7].Address != WujieFanEc.RegisterFanTableFan1 + 7 || steps[7].Value != 60)
			{
				detail = "表1 前 8 字节计划不对";
				return false;
			}
			if (steps[8].Address != WujieFanEc.RegisterFanTableFan2 || steps[8].Value != 120
				|| steps[15].Address != WujieFanEc.RegisterFanTableFan2 + 7 || steps[15].Value != 120)
			{
				detail = "表2 前 8 字节计划不对";
				return false;
			}
			if (steps[16].Address != WujieFanEc.RegisterFanIsolate || steps[16].Value != 0x80)
			{
				detail = "副扇隔离位置位计划不对";
				return false;
			}
			if (steps[17].Address != WujieFanEc.RegisterFanTableLatch || steps[17].Value != 0x00
				|| steps[18].Address != WujieFanEc.RegisterFanTableLatch || steps[18].Value != 0x40
				|| steps[18].DelayAfterMs != WujieFanEc.TableLatchPulseMs)
			{
				detail = "锁存脉冲计划不对（应先 &~0x40 → 50ms → |0x40）";
				return false;
			}
			BuildTableWritePlan(80, 80, 0x02, 0x10, out steps, out _, out bool sameTargetIsolate);
			if (sameTargetIsolate || steps[16].Value != 0x10)
			{
				detail = "主副扇目标相同时应清 0x07C5 bit7（并保留基线其它位）";
				return false;
			}
			detail = "table 计划自检 OK（表1/2 各 8 字节 + 隔离 + 锁存 50ms 脉冲；同目标清隔离）";
			return true;
		}
		catch (Exception ex)
		{
			detail = "自检异常：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// ⭐ task-43：写 fan1/fan2 表前 8 字节 → 设/清隔离位 → 锁存脉冲（base&amp;~0x40 → 50ms → base|0x40）
	/// → 不写 0x1804/0x1809（由 EC 沿软着陆斜坡走到表目标）；整组序列在 I2EC 跨进程锁内一次完成（防撕裂写）。
	/// </summary>
	private bool ApplyTable(int raw1, int raw2, string reason)
	{
		lock (_gate)
		{
		if (!_running)
		{
			// Stop 已把控制器置停（另一个线程正在交还 EC）⇒ ⛔ 不再重锁表，避免 Stop 之后又被锁上。
			return false;
		}
		raw1 = Math.Clamp(raw1, 1, EcPortIo.WujieFanDutyMaxRaw);
		raw2 = Math.Clamp(raw2, 1, EcPortIo.WujieFanDutyMaxRaw);
		bool needIsolate = raw1 != raw2;
		BuildTableWritePlan(raw1, raw2, _tableLatchBase, _tableIsolateBase, out List<I2ecInpOut.I2ecWriteStep> steps,
			out List<I2ecInpOut.I2ecVerifyStep> verify, out _);
		bool ok = I2ecInpOut.TryWriteEcSequence(steps, verify, out string detail);
		if (!ok)
		{
			_tableWriteFailCount++;
			Logger.WriteError("[fan] 无界 table：写表失败（" + reason + "）：" + detail);
			if (_tableWriteFailCount >= TableWriteFailLimit)
			{
				bool released = ReleaseTable("table 连续写失败回退 direct", force: true);
				_backend = ControlBackend.Direct;
				_tableActive = false;
				_tableLastRaw1 = -1;
				_tableLastRaw2 = -1;
				Logger.WriteError("[fan] 无界 table：连续 " + _tableWriteFailCount + " 次写表失败 ⇒ 自动回退 25Hz direct 后端"
					+ (released ? "（旧表已释放）" : "（⚠️ 旧表释放失败，direct 直写仍会覆盖目标）"));
			}
			return false;
		}
		_tableActive = true;
		Logger.WriteLineIfChanged("wujie-fan-table", "[fan] 无界 table：已写表+锁存（" + reason + " · raw1=" + raw1
			+ " raw2=" + raw2 + " · 隔离=" + (needIsolate ? "置" : "清") + " · 回读确认 ok）");
		return true;
		}
	}
	/// <summary>
	/// ⭐ task-43：交还 EC —— 绝对写入清 0x0727 bit6 与 0x07C5 bit7（&amp; 基线，**绝不做 read-modify-write**），
	/// 带一次回读校验；失败重试一次并如实记日志（不假装成功）。
	/// </summary>
	private bool ReleaseTable(string reason, bool force = false)
	{
		if (!_tableActive && !force)
		{
			return true;
		}
		int latchWant = _tableLatchBase & ~0x40;
		int isolateWant = _tableIsolateBase & ~0x80;
		List<I2ecInpOut.I2ecWriteStep> steps = new()
		{
			new(WujieFanEc.RegisterFanTableLatch, latchWant),
			new(WujieFanEc.RegisterFanIsolate, isolateWant)
		};
		List<I2ecInpOut.I2ecVerifyStep> verify = new()
		{
			new(WujieFanEc.RegisterFanTableLatch, latchWant),
			new(WujieFanEc.RegisterFanIsolate, isolateWant)
		};
		bool ok = I2ecInpOut.TryWriteEcSequence(steps, verify, out string detail);
		if (!ok)
		{
			Thread.Sleep(20);
			ok = I2ecInpOut.TryWriteEcSequence(steps, verify, out detail);
		}
		_tableActive = false;
		_tableLastRaw1 = -1;
		_tableLastRaw2 = -1;
		if (ok)
		{
			Logger.WriteLine("[fan] 无界 table：已清锁存+隔离并交还 EC（" + reason + "）");
		}
		else
		{
			Logger.WriteError("[fan] 无界 table：⚠️ 交还 EC 失败（" + reason + "）：" + detail
				+ " ⇒ EC 可能仍按旧表运行；下次启动 direct 路径会尝试清理遗留位");
		}
		return ok;
	}

	/// <summary>table 后端的副扇堵转看门狗（500ms 采样；目标变化后的软着陆期由 12 秒计数窗口覆盖）。</summary>
	private void SampleFan2Watchdog()
	{
		if (Environment.TickCount64 - _lastSampleTick < SampleIntervalMs)
		{
			return;
		}
		_lastSampleTick = Environment.TickCount64;
		// 表写入后 EC 需要沿软着陆斜坡（满量程约 30s）走到目标；这段时间副扇 0 RPM 是正常的，
		// 不能按 12s 堵转处理。direct 路径不经过这里，保持旧行为。
		if (Environment.TickCount64 - _tableLastWriteTick < 30000)
		{
			_fan2ZeroCount = 0;
			return;
		}
		if (TryReadFan2Rpm(out int fan2Rpm) && fan2Rpm <= 0)
		{
			_fan2ZeroCount++;
			if (_fan2ZeroCount >= Fan2ZeroSamples)
			{
				Stop("副扇连续 0 RPM（疑似被 EC 判堵转），交还 EC");
				LastDetail = "副扇 0 RPM 看门狗触发";
			}
		}
		else
		{
			_fan2ZeroCount = 0;
		}
	}

	private static int CountValidPoints(byte[] temps, int[] duties)
	{
		int valid = 0;
		for (int i = 0; i < temps.Length && i < duties.Length; i++)
		{
			if (temps[i] == byte.MaxValue)
			{
				break;
			}
			valid++;
		}
		return Math.Max(1, valid);
	}

	private static int ResolveDuty(byte[] temps, int[] duties, int points, int temp)
	{
		if (points <= 0)
		{
			return 100;
		}
		byte[] effectiveTemps = new byte[points];
		Array.Copy(temps, effectiveTemps, points);
		for (int i = 0; i < points; i++)
		{
			if (effectiveTemps[i] < MinimumOperatingTemperatureC)
			{
				effectiveTemps[i] = (byte)MinimumOperatingTemperatureC;
			}
		}
		temp = Math.Max(temp, MinimumOperatingTemperatureC);
		if (temp <= effectiveTemps[0])
		{
			return duties[0];
		}
		for (int i = 1; i < points; i++)
		{
			int current = effectiveTemps[i];
			if (current == byte.MaxValue)
			{
				break;
			}
			if (temp <= current)
			{
				int previousTemp = effectiveTemps[i - 1];
				int span = Math.Max(1, current - previousTemp);
				int deltaDuty = duties[i] - duties[i - 1];
				return duties[i - 1] + (int)Math.Round(deltaDuty * (double)(temp - previousTemp) / span);
			}
		}
		return duties[Math.Max(0, points - 1)];
	}

	private bool TryReadFan2Rpm(out int rpm)
	{
		rpm = 0;
		if (!ReadEc(WujieFanEc.RegisterFan2RpmHigh, out int high)
		|| !ReadEc(WujieFanEc.RegisterFan2RpmHigh + 1, out int low))
		{
			return false;
		}
		rpm = ((high & 0xFF) << 8) | (low & 0xFF);
		return true;
	}

	private bool ReadEc(int address, out int value)
	{
		value = -1;
		if (_channel == WriteChannel.I2ec)
		{
			return I2ecInpOut.TryReadEcByte(address, out value, out _);
		}
		if (_channel == WriteChannel.AcpiEc)
		{
			return EcPortIo.TryReadByte(address, out value);
		}
		return false;
	}

	private bool WriteDuty(int address, int raw, out string detail)
	{
		if (_channel == WriteChannel.I2ec)
		{
			return I2ecInpOut.TryWriteEcByte(address, raw, out detail);
		}
		if (_channel == WriteChannel.AcpiEc)
		{
			bool ok = EcPortIo.TryWriteWujieFanDuty(address, raw, out int readBack);
			detail = ok ? "ok" : ("回读 0x" + readBack.ToString("X2") + " / " + EcPortIo.LastAcpiWriteTrace);
			return ok;
		}
		detail = "通道未选择";
		return false;
	}

	private static string ChannelName(WriteChannel channel)
	{
		return channel switch
		{
			WriteChannel.I2ec => "I2EC",
			WriteChannel.AcpiEc => "ACPI-EC（实验）",
			_ => "无",
		};
	}
}
