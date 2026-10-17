using System.Globalization;
using System.Text;

namespace MechrevoLite.Mode;

public sealed record PerfModeSettings
{
	public int? Pl1 { get; init; }

	public int? Pl2 { get; init; }

	public int? Pl4 { get; init; }

	public int? GpuTgp { get; init; }

	/// <summary>
	/// ⭐⭐ 2026-10-19（显卡功耗上限 · 本机字段）：⭐ 宝龙达没有 GCU 的 `GpuConfigurableTGPTarget`，
	/// 但能通过 **NVML** 写 `nvmlDeviceSetPowerManagementLimit`（毫瓦）✅
	/// <para>⚠️ 与 <see cref="GpuTgp" /> 的区别：那个是**厂商固件字段**（GCU 语义），这个是**本机 NVML** 语义。</para>
	/// <para>⚠️ 只在能读到有效约束的机器上由 UI 显示；落盘仍是 `mode_tune_*`（结构不变）✅</para>
	/// </summary>
	public int? GpuPowerLimitW { get; init; }

	public bool? GpuDynamicBoostOn { get; init; }

	public int? GpuDynamicBoost { get; init; }

	public int[]? CpuFanDuty { get; init; }

	public int[]? GpuFanDuty { get; init; }

	public bool? TccOn { get; init; }

	public int? TccTarget { get; init; }

	/// <summary>
	/// ⭐⭐ 2026-10-19（30 系 · SMU 功耗时间窗）：STAPM（按机壳温度的持续功耗）的**平均时间窗**，单位**秒**。
	/// <para>⚠️ 只在「宝龙达 30 系」上由 UI 写入（30 系没有 WMI 功能号 23 ⇒ 自定义模式的 PL/温度墙走 SMU；
	/// 40 系不建这一行、本字段保持 null）。</para>
	/// <para>⚠️ 它不是 "PL2 生效时间"：PL2（fast limit）是瞬时限值、没有时间参数；
	/// SMU 侧只有 <c>stapm_time</c>（本字段）与 <c>slow_time</c>（平均功耗时间常数）两个时间窗。</para>
	/// </summary>
	public int? StapmTimeSeconds { get; init; }

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
			if (!WindowsPowerMode.HasValue && !CpuBoost.HasValue && string.IsNullOrEmpty(PowerPlanGuid))
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
