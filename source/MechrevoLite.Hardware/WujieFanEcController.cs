using System;
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

	private WriteChannel _channel;

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
			_running = true;
			_channel = channel;
			_active = this;
			_timer.Change(WriteIntervalMs, WriteIntervalMs);
		}
		detail = "已启动（" + ChannelName(channel) + " 25Hz 双扇直写 · " + (independent ? "独立控制" : "同步控制") + " · CPU " + _validCpu + " 点 / GPU " + _validGpu + " 点 · 当前 " + temp + "℃）";
		Logger.WriteLine("[fan] 无界 EC 曲线启动：" + detail);
		return true;
	}

	/// <summary>停止写入，交还 EC（EC 会在 6~8 秒内接管；不做渐进降速）。</summary>
	internal void Stop(string reason)
	{
		bool wasRunning;
		lock (_gate)
		{
			wasRunning = _running;
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
		if (wasRunning)
		{
			Logger.WriteLine("[fan] 无界 EC 曲线停止：" + reason + "（交还 EC）");
		}
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
		try
		{
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
