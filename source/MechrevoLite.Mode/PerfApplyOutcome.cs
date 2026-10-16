using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Mode;

public sealed record PerfApplyOutcome(string ModeId, IReadOnlyList<PerfApplyStepOutcome> Steps)
{
	/// <summary>
	/// (b)（2026-10-09，用户原话「OK 按你说的」）：这条结果不是一次新的下发，而是懒验证（PerfLazyVerifier）
	/// 事后**原地升级**（Sent→Confirmed，或期限到 Sent→Stale）重建出来的。界面据此避免用这条旧结果
	/// 覆盖掉用户此刻新改出来的"有未应用改动"提示。
	/// </summary>
	public bool LazyUpgrade { get; init; }

	public bool ModeSwitched
	{
		get
		{
			if (Steps.Any((PerfApplyStepOutcome s) => IsSwitch(s.Step)))
			{
				return Steps.Where((PerfApplyStepOutcome s) => IsSwitch(s.Step)).All((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Confirmed);
			}
			return false;
		}
	}

	public bool FirmwareParametersIssued => Steps.Where((PerfApplyStepOutcome s) =>
	{
		PerfApplyStepKind kind = s.Step.Kind;
		return ((uint)(kind - 3) <= 1u || kind == PerfApplyStepKind.ApplyGpuOverclock) ? true : false;
	}).All((PerfApplyStepOutcome s) => s.Result != PerfApplyResult.Failed);

	public bool AnyFailed => Steps.Any((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Failed);

	public IReadOnlyList<PerfApplyStepOutcome> Failed => Steps.Where((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Failed).ToList();

	public IReadOnlyList<PerfApplyStepOutcome> Unverified => Steps.Where((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Sent).ToList();

	/// <summary>(b)：已下发、期限到仍读到"对不上"的步骤（文案「可能没生效」；不算失败）。</summary>
	public IReadOnlyList<PerfApplyStepOutcome> Stale => Steps.Where((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Stale).ToList();

	internal static bool IsSwitch(PerfApplyStep step)
	{
		PerfApplyStepKind kind = step.Kind;
		if ((uint)kind <= 2u)
		{
			return true;
		}
		return false;
	}

	public string Describe()
	{
		if (Steps.Count != 0)
		{
			return string.Join("  ", Steps.Select((PerfApplyStepOutcome s) => s.ToString()));
		}
		return "(no steps)";
	}
}
