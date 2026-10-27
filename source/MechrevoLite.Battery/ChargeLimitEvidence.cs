using System;
using MechrevoLite.Hardware;

namespace MechrevoLite.Battery;

internal sealed class ChargeLimitEvidence
{
	internal static readonly TimeSpan IneffectiveAfter = TimeSpan.FromMinutes(3L);

	internal static readonly TimeSpan HeldSampleSpacing = TimeSpan.FromSeconds(20L);

	internal const int HeldSamplesRequired = 3;

	internal const int FullBatteryPercent = 98;

	private DateTime? _chargingAboveSince;

	private int _heldSamples;

	private DateTime _lastHeldSample;

	public void Reset()
	{
		_chargingAboveSince = null;
		_heldSamples = 0;
	}

	public ChargeLimitVerdict? Observe(int limit, bool onAc, bool charging, int percent, int ecReadback, DateTime now)
	{
		bool flag = ((limit < 40 || limit >= 100) ? true : false);
		if (flag || !onAc)
		{
			Reset();
			return null;
		}
		// 2026-10-08（限充开关）：判据改成「EC 直读回读 == 命令上限」为主。
		// 实测（2026-10-08 23:29–23:31）：把 EC 0x7B9 写成 80（复充下限 0x7D0=75）后，
		// Win32_Battery.EstimatedChargeRemaining 与 root\wmi 的 FullChargedCapacity /
		// RemainingCapacity 一个字节都没变（始终 100% / 80080 mWh）。
		// 即 Windows 百分比**根本不会**跟随 EC 上限 —— 旧实现拿 Windows 百分比当判据
		// （充电中且 percent >= 上限+2 持续 3 分钟）会把真实生效的限充判成 ineffective
		// 并强制写回 100%，属于把用户设置撤销的功能性缺陷。
		if (ecReadback == limit)
		{
			Reset();
			return ChargeLimitVerdict.Verified;
		}
		if (ecReadback > 0)
		{
			// EC 能读但读到别的值：命令没落到实处。这里不判 ineffective（避免误撤销
			// 正在生效的限充），交给 ChargeLimitMonitor 记日志。
			Reset();
			return null;
		}
		// 只有 EC 完全读不到（驱动不可用）时才退回按 Windows 充电行为观察。
		if (charging && percent >= limit + 2)
		{
			_heldSamples = 0;
			DateTime valueOrDefault = _chargingAboveSince.GetValueOrDefault();
			if (!_chargingAboveSince.HasValue)
			{
				valueOrDefault = now;
				_chargingAboveSince = valueOrDefault;
			}
			if (!(now - _chargingAboveSince.Value >= IneffectiveAfter))
			{
				return null;
			}
			return ChargeLimitVerdict.Ineffective;
		}
		_chargingAboveSince = null;
		int num = limit - 5;
		if (!charging && percent >= num && percent < 98)
		{
			if (_heldSamples == 0 || now - _lastHeldSample >= HeldSampleSpacing)
			{
				_heldSamples++;
				_lastHeldSample = now;
			}
			if (_heldSamples < 3)
			{
				return null;
			}
			return ChargeLimitVerdict.Verified;
		}
		_heldSamples = 0;
		return null;
	}
}
