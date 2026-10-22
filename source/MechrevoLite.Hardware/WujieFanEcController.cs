using System;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界风扇曲线）：无界（WUJIE14XA）EC 风扇曲线控制器。
///
/// <para>无界 EC 的占空比目标（主扇 0x1804 / 副扇 0x1809，0..200 = 0..100%）会被 EC 热管理任务
/// 周期性覆写，所以必须**持续高频写**（实测 25Hz 才能压住；2Hz 被压制、10Hz 拉锯）。
/// 两路必须同时写，只写主扇会让副扇被 EC 斜坡挪到 0。</para>
///
/// <para>安全：副扇 20% 下限；≥85℃ 强制 100%；≥95℃ 停止写入交还 EC；
/// 副扇连续约 12 秒 0 RPM 视为被闭锁，停止写入；写失败/读温度失败连续多次也停止。
/// ⛔ 停止时不做渐进降速（实测渐进释放会把副扇锁在 0）。</para>
/// </summary>
internal sealed class WujieFanEcController : IDisposable
{
	private const int WriteIntervalMs = 40;

	private const int SampleIntervalMs = 500;

	private const int Fan2ZeroSamples = 24;

	private const int MaxIoFailures = 5;

	private readonly Timer _timer;

	private readonly object _gate = new object();

	private byte[] _curveTemps = Array.Empty<byte>();

	private int[] _curveDuties = Array.Empty<int>();

	private int _validPoints;

	private bool _running;

	private int _fan2ZeroCount;

	private int _ioFailures;

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
			_ioFailures = 0;
			_lastSampleTick = Environment.TickCount64;
			_lastTargetPercent = -1;
			_running = true;
			_timer.Change(WriteIntervalMs, WriteIntervalMs);
		}
		detail = "已启动（25Hz 双扇直写 · 曲线点数=" + _validPoints + "）";
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
			_ioFailures = 0;
			if (reason.Length > 0)
			{
				LastDetail = reason;
			}
		}
		if (wasRunning)
		{
			Logger.WriteLine("[fan] 无界 EC 曲线停止：" + reason + "（交还 EC）");
		}
	}

	public void Dispose()
	{
		Stop("窗口关闭");
		_timer.Dispose();
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
			if (!EcPortIo.TryReadByte(WujieFanEc.RegisterCpuTemp, out int temp) || temp < 0 || temp > 120)
			{
				NoteIoFailure("读 CPU 温度失败");
				return;
			}
			if (temp >= 95)
			{
				Stop("温度 " + temp + "℃ ≥ 95℃，交还 EC 自动控速");
				LastDetail = "温度过高已交还 EC";
				return;
			}
			int target = (temp >= 85) ? 100 : ResolveCurveDuty(temp);
			if (target > 0 && target < WujieFanEc.FanFloorPercent)
			{
				target = WujieFanEc.FanFloorPercent;
			}
			int raw = Math.Max(1, Math.Min(EcPortIo.WujieFanDutyMaxRaw, (int)Math.Round(target * 2.0)));
			bool ok1 = EcPortIo.TryWriteWujieFanDutyFast(WujieFanEc.RegisterDutyFan1, raw);
			bool ok2 = EcPortIo.TryWriteWujieFanDutyFast(WujieFanEc.RegisterDutyFan2, raw);
			if (!ok1 || !ok2)
			{
				NoteIoFailure("写占空比失败");
				return;
			}
			_ioFailures = 0;
			if (target != _lastTargetPercent)
			{
				_lastTargetPercent = target;
				Logger.WriteLineIfChanged("wujie-fan-target", $"[fan] 无界 EC 曲线：{temp}℃ → 目标 {target}% (raw={raw}) · 主/副扇均写入");
			}
			if (Environment.TickCount64 - _lastSampleTick >= SampleIntervalMs)
			{
				_lastSampleTick = Environment.TickCount64;
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
			NoteIoFailure(ex.GetType().Name + " " + ex.Message);
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
		if (!EcPortIo.TryReadBytes(WujieFanEc.RegisterFan2RpmHigh, 2, out int[] values) || values.Length != 2)
		{
			return false;
		}
		rpm = ((values[0] & 0xFF) << 8) | (values[1] & 0xFF);
		return true;
	}

	private void NoteIoFailure(string reason)
	{
		_ioFailures++;
		if (_ioFailures >= MaxIoFailures)
		{
			Stop("连续 I/O 失败（" + reason + "），交还 EC");
			LastDetail = "I/O 失败已停止";
		}
	}
}
