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

	/// <summary>
	/// ⭐⭐ 2026-10-17（宝龙达 · 自定义档**本地化**）：⭐ 本结果来自"**没有固件槽**"的后端
	/// （`PerfModeCapabilities.SupportsFirmwareSlot == false` ⇒ 宝龙达）⚠️
	/// <para>⭐ 那种后端下，自定义档**不会**产生 `SwitchFirmwareSlot` 步骤（规划器整块跳过），
	/// 但它的参数（PL1/PL2/温度墙）会走 **WMI 功能号 23** 真下发 ✅
	/// ⇒ ⭐ 所以"没有切换步骤"**不能**被当成"没切换成功" ❌（那会让界面一直报"未切换成功"）。</para>
	/// <para>⚠️ 默认 false ⇒ 所有既有后端（GCU/老接口）行为**逐位不变** ✅。</para>
	/// </summary>
	public bool LocalApply { get; init; }

	public bool ModeSwitched
	{
		get
		{
			if (Steps.Any((PerfApplyStepOutcome s) => IsSwitch(s.Step)))
			{
				return Steps.Where((PerfApplyStepOutcome s) => IsSwitch(s.Step)).All((PerfApplyStepOutcome s) => s.Result == PerfApplyResult.Confirmed);
			}
			// ⭐ 宝龙达（无固件槽）：没有切换步骤是**预期**的 ⇒ 只要有下发步骤且都没失败，就算已生效 ✅
			return LocalApply && Steps.Count > 0 && !AnyFailed;
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
