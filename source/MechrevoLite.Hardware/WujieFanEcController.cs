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

	/// <summary>2026-10-22（机主：45℃ 以下风扇会停转、写不进去）：低温区不写占空比，交还 EC 原生策略。</summary>
	internal const int MinimumOperatingTemperatureC = 45;

	private readonly Timer _timer;

	private readonly object _gate = new object();

	private static WujieFanEcController _active;

	private byte[] _curveTemps = Array.Empty<byte>();

	private int[] _curveDuties = Array.Empty<int>();

	private int _validPoints;

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

	/// <summary>启动曲线控制：温度数组与占空比数组（长度 16，0xFF 表示无效点）。</summary>
	internal bool Start(byte[] curveTemps, int[] curveDuties, out string detail)
	{
		if (!WujieFanEc.IsWujiePlatform())
		{
			detail = "不是无界机型";
			return false;
		}
		if (curveTemps == null || curveDuties == null || curveTemps.Length == 0 || curveDuties.Length == 0)
		{
			detail = "曲线数据为空";
			return false;
		}
		// 启动前先验通道：I2EC 可用（inpoutx64）、0x200D 读写模式、温度可读。
		if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterMode, out int mode, out string modeDetail))
		{
			detail = "I2EC 通道不可用（需要本机已安装 inpoutx64）：" + modeDetail;
			return false;
		}
		if ((mode & 0x03) != 0x03)
		{
			detail = "I2EC 不是读写模式（0x200D=0x" + mode.ToString("X2") + "）";
			return false;
		}
		if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterCpuTemp, out int temp, out string tempDetail) || temp < 0 || temp > 120)
		{
			detail = "读取 CPU 温度失败（0x043E）：" + tempDetail;
			return false;
		}
		StopActive("被新的曲线控制器替换");
		lock (_gate)
		{
			_curveTemps = (byte[])curveTemps.Clone();
			_curveDuties = (int[])curveDuties.Clone();
			int valid = 0;
			for (int i = 0; i < _curveTemps.Length; i++)
			{
				if (_curveTemps[i] == byte.MaxValue)
				{
					break;
				}
				valid++;
			}
			_validPoints = Math.Max(1, Math.Min(valid, Math.Min(_curveTemps.Length, _curveDuties.Length)));
			_fan2ZeroCount = 0;
			_tempFailTicks = 0;
			_writeFailTicks = 0;
			_readbackMismatchSamples = 0;
			_lastTempC = temp;
			_lastRaw = -1;
			_lastSampleTick = Environment.TickCount64;
			_lastTargetPercent = -1;
			_running = true;
			_active = this;
			_timer.Change(WriteIntervalMs, WriteIntervalMs);
		}
		detail = "已启动（I2EC 25Hz 双扇直写 · 曲线点数=" + _validPoints + " · 当前 " + temp + "℃）";
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
			bool tempOk = I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterCpuTemp, out int temp, out _) && temp >= 0 && temp <= 120;
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
			int target = (temp >= 85) ? 100 : ResolveCurveDuty(temp);
			if (target > 0 && target < WujieFanEc.FanFloorPercent)
			{
				target = WujieFanEc.FanFloorPercent;
			}
			int raw = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(target * 2.0)));
			bool ok1 = I2ecInpOut.TryWriteEcByte(WujieFanEc.RegisterDutyFan1, raw, out string writeDetail1);
			bool ok2 = I2ecInpOut.TryWriteEcByte(WujieFanEc.RegisterDutyFan2, raw, out string writeDetail2);
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
				if (target != _lastTargetPercent)
				{
					_lastTargetPercent = target;
					Logger.WriteLineIfChanged("wujie-fan-target", $"[fan] 无界 EC 曲线：{temp}℃ → 目标 {target}% (raw={raw}) · 主/副扇均写入（I2EC）");
				}
			}
			// ② 每 500ms 采样一次：回读一致性 + 副扇堵转看门狗。
			if (Environment.TickCount64 - _lastSampleTick >= SampleIntervalMs)
			{
				_lastSampleTick = Environment.TickCount64;
				if (I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterDutyFan1, out int readBack, out _) && readBack != raw)
				{
					_readbackMismatchSamples++;
					if (_readbackMismatchSamples >= MaxReadbackMismatchSamples)
					{
						Stop("占空比回读持续不一致（写 " + raw + " / 读 " + readBack + "），交还 EC");
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

	private int ResolveCurveDuty(int temp)
	{
		byte[] temps = _curveTemps;
		int[] duties = _curveDuties;
		int points = _validPoints;
		if (points <= 0)
		{
			return 100;
		}
		// 低温区的曲线点对无界 EC 无意义：统一按 45℃ 起算（第一个有效点若低于 45 也按 45 处理）。
		for (int i = 0; i < points; i++)
		{
			if (temps[i] != byte.MaxValue && temps[i] < MinimumOperatingTemperatureC)
			{
				temps[i] = MinimumOperatingTemperatureC;
			}
		}
		temp = Math.Max(temp, MinimumOperatingTemperatureC);
		if (temp <= temps[0])
		{
			return duties[0];
		}
		for (int i = 1; i < points; i++)
		{
			int current = temps[i];
			if (current == byte.MaxValue)
			{
				break;
			}
			if (temp <= current)
			{
				int previousTemp = temps[i - 1];
				int span = Math.Max(1, current - previousTemp);
				int deltaDuty = duties[i] - duties[i - 1];
				return duties[i - 1] + (int)Math.Round(deltaDuty * (double)(temp - previousTemp) / span);
			}
		}
		return duties[Math.Max(0, points - 1)];
	}

	private static bool TryReadFan2Rpm(out int rpm)
	{
		rpm = 0;
		if (!I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFan2RpmHigh, out int high, out _)
		|| !I2ecInpOut.TryReadEcByte(WujieFanEc.RegisterFan2RpmHigh + 1, out int low, out _))
		{
			return false;
		}
		rpm = ((high & 0xFF) << 8) | (low & 0xFF);
		return true;
	}
}
