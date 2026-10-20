using System.Globalization;
using System.Text;

namespace MechrevoLite.Mode;

public sealed record PerfModeSettings
{
	public int? Pl1 { get; init; }

	public int? Pl2 { get; init; }

	public int? Pl4 { get; init; }

	public int? GpuTgp { get; init; }
	// （显卡功耗上限 · 本机字段）： 宝龙达没有 GCU 的 GpuConfigurableTGPTarget
	public int? GpuPowerLimitW { get; init; }

	public bool? GpuDynamicBoostOn { get; init; }

	public int? GpuDynamicBoost { get; init; }

	public int[]? CpuFanDuty { get; init; }

	public int[]? GpuFanDuty { get; init; }

	public bool? TccOn { get; init; }

	public int? TccTarget { get; init; }
	// <para> 显示/下发条件 = **本自定义档真的走 SMU**（PerfModeCapabilities.UsesSmuForCustomPower
	public int? StapmTimeSeconds { get; init; }

	// SMU 路径：慢速/快速各自独立（与 STAPM 拆开）
	public int? SmuSlowLimit { get; init; }

	public int? SmuSlowTimeSeconds { get; init; }

	public int? SmuFastTimeSeconds { get; init; }

	public bool? GpuOverclockOn { get; set; }

	public int? GpuCoreOffset { get; set; }

	public int? GpuMemoryOffset { get; set; }

	public bool? FanBoost { get; set; }

	public bool? FanSwitchSpeedOn { get; init; }

	public int? FanSwitchSpeedMs { get; init; }

	public int? WindowsPowerMode { get; init; }

	public int? CpuBoost { get; init; }

	public string? PowerPlanGuid { get; init; }

	public int? RefreshHz { get; set; }

	public bool IsUntouched
	{
		get
		{
			if (!NeedsFirmwareSlot && !HasBuiltInSafeOverride)
			{
				return !HasAppSideOverride;
			}
			return false;
		}
	}

	public bool NeedsFirmwareSlot
	{
		get
		{
			if (!Pl1.HasValue && !Pl2.HasValue && !Pl4.HasValue && !GpuTgp.HasValue && !GpuDynamicBoostOn.HasValue && !GpuDynamicBoost.HasValue && CpuFanDuty == null)
			{
				return GpuFanDuty != null;
			}
			return true;
		}
	}

	public bool HasBuiltInSafeOverride
	{
		get
		{
			if (!TccOn.HasValue && !TccTarget.HasValue && !GpuOverclockOn.HasValue && !GpuCoreOffset.HasValue && !GpuMemoryOffset.HasValue && !FanBoost.HasValue && !FanSwitchSpeedOn.HasValue)
			{
				return FanSwitchSpeedMs.HasValue;
			}
			return true;
		}
	}

	public bool HasAppSideOverride
	{
		get
		{
			if (!WindowsPowerMode.HasValue && !CpuBoost.HasValue && string.IsNullOrEmpty(PowerPlanGuid)
			&& !SmuSlowLimit.HasValue && !SmuSlowTimeSeconds.HasValue && !SmuFastTimeSeconds.HasValue)
			{
				return RefreshHz.HasValue;
			}
			return true;
		}
	}

	public static readonly PerfModeSettings Default = new PerfModeSettings();

	public string FirmwareSignature()
	{
		StringBuilder sb = new StringBuilder(96);
		N(Pl1);
		N(Pl2);
		N(Pl4);
		N(GpuTgp);
		B(GpuDynamicBoostOn);
		N(GpuDynamicBoost);
		A(CpuFanDuty);
		A(GpuFanDuty);
		B(TccOn);
		N(TccTarget);
		B(GpuOverclockOn);
		N(GpuCoreOffset);
		N(GpuMemoryOffset);
		B(FanSwitchSpeedOn);
		N(FanSwitchSpeedMs);
		return sb.ToString();
		void A(int[]? v)
		{
			sb.Append((v == null) ? "-" : string.Join(',', v)).Append('|');
		}
		void B(bool? v)
		{
			StringBuilder stringBuilder = sb;
			string value;
			if (v.HasValue)
			{
				value = (v.Value ? "1" : "0");
			}
			else
			{
				value = "-";
			}
			stringBuilder.Append(value).Append('|');
		}
		void N(int? v)
		{
			sb.Append(v?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
		}
	}
}
