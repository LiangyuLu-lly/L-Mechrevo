using System;
using System.Linq;
using MechrevoLite.Properties;

namespace MechrevoLite.Mode;

internal static class PerfModeText
{
	public static string Name(PerfModeDefinition mode)
	{
		ArgumentNullException.ThrowIfNull(mode, "mode");
		if (!string.IsNullOrEmpty(mode.UserName))
		{
			return mode.UserName;
		}
		int ordinal;
		return mode.Kind switch
		{
			PerfModeKind.Silent => Strings.Silent, 
			PerfModeKind.Balanced => Strings.Balanced, 
			PerfModeKind.SilentTurbo => Strings.SilentTurbo, 
			PerfModeKind.Turbo => Strings.Turbo, 
			_ => string.Format(Strings.CustomProfileN, (!PerfModeCollection.TryParseCustomOrdinal(mode.Id, out ordinal)) ? 1 : ordinal), 
		};
	}

	public static string ComboText(PerfModeDefinition mode)
	{
		if (!mode.IsEmulatedBuiltIn)
		{
			return Name(mode);
		}
		return string.Format(Strings.PerfModeCustomized, Name(mode));
	}

	public static string StepName(PerfApplyStep step, bool amd)
	{
		ArgumentNullException.ThrowIfNull(step, "step");
		switch (step.Kind)
		{
		case PerfApplyStepKind.SwitchBuiltIn:
		case PerfApplyStepKind.SwitchTurboSubMode:
		case PerfApplyStepKind.SwitchFirmwareSlot:
			return Strings.PerfStepSwitch;
		case PerfApplyStepKind.WriteFanCurve:
			return Strings.FanCurve + (step.IsCpuCurve ? " CPU" : " GPU");
		case PerfApplyStepKind.ApplyGpuOverclock:
			return Strings.GpuOverclock;
		case PerfApplyStepKind.ApplyFanBoost:
			return Strings.ModeTuneFanBoost;
		case PerfApplyStepKind.ApplyPowerPlan:
			return Strings.PowerPlan;
		case PerfApplyStepKind.ApplyCpuBoost:
			return Strings.BoostMode;
		case PerfApplyStepKind.ApplyPowerOverlay:
			return Strings.ModeTunePowerMode;
		case PerfApplyStepKind.ApplyRefreshRate:
			return Strings.ModeTuneRefresh;
		case PerfApplyStepKind.WriteFirmwareField:
			return step.WireKey switch
			{
				"PL1" => Strings.CpuPl1, 
				"PL2" => Strings.CpuPl2, 
				"PL4" => amd ? Strings.CpuFppt : Strings.CpuPl4, 
				"CpuTccOffsetSwitch" => Strings.CpuTempWall, 
				"CpuTccOffset" => Strings.TempWallValue, 
				"GpuConfigurableTGPTarget" => Strings.GpuTgp, 
				"GpuDynamicBoostSwitch" => Strings.GpuDynamicBoost, 
				"GpuDynamicBoost" => Strings.DynamicBoostValue, 
				"OverClockingSwitch" => Strings.GpuOverclock, 
				"GpuCoreClockOffsetOC" => Strings.CoreOffset, 
				"GpuMemoryClockOffsetOC" => Strings.MemoryOffset, 
				"FanSwitchSpeedEnabled" => Strings.FanSwitchSensitivity, 
				"FanSwitchSpeed" => Strings.FanSwitchDelay, 
				_ => step.WireKey ?? "?", 
			};
		default:
			return step.Kind.ToString();
		}
	}

	public static string Summary(PerfApplyOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome, "outcome");
		int num = outcome.Steps.Count((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Confirmed);
		// (b)（2026-10-09）：期限到仍没读到的 Stale 一并计入「待确认」——它**不是失败**，口径与 Sent 相同；
		// 区别只体现在 tooltip 的逐项文案与状态行末尾那句「可能没生效」（见 StaleNote）。
		int num2 = outcome.Steps.Count((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Sent || s.Result == PerfApplyResult.Stale);
		int num3 = outcome.Steps.Count((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Failed);
		return string.Format(Strings.PerfModeOutcome, num, num2, num3);
	}

	/// <summary>
	/// (b)（2026-10-09）：期限（30s）到仍未读到一致的步骤 ⇒ 状态行末尾追加一句诚实的「可能没生效」+ 重试口径；
	/// 没有这种步骤时返回空串（状态行一字不变）。⚠️ 不用红色：它不是失败。
	/// </summary>
	public static string StaleNote(PerfApplyOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome, "outcome");
		int num = outcome.Steps.Count((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Stale);
		return (num == 0) ? "" : string.Format(Strings.PerfModeOutcomeStale, num);
	}

	public static string FailedItems(PerfApplyOutcome outcome, bool amd)
	{
		ArgumentNullException.ThrowIfNull(outcome, "outcome");
		return string.Join("、", outcome.Failed.Select((PerfApplyStepOutcome s) => StepName(s.Step, amd) + (string.IsNullOrEmpty(s.Detail) ? "" : ("（" + s.Detail + "）"))).Distinct());
	}

	public static string Details(PerfApplyOutcome outcome, bool amd)
	{
		ArgumentNullException.ThrowIfNull(outcome, "outcome");
		return string.Join(Environment.NewLine, outcome.Steps.Select((PerfApplyStepOutcome s) => s.Result switch
		{
			PerfApplyResult.Confirmed => "✓ ", 
			PerfApplyResult.Sent => "… ", 
			// (b)：期限到仍没读到 ⇒ 用"?"而不是"✗"，一眼看出它不是失败（文案里已写明"可能没生效"）。
			PerfApplyResult.Stale => "? ", 
			_ => "✗ ", 
		} + StepName(s.Step, amd) + (string.IsNullOrEmpty(s.Detail) ? "" : ("：" + s.Detail))));
	}
}
