using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Mode;

public static class PerfModeRunner
{
	// F3（2026-10-09）：状态行文案改直白。写调用真的被拒（IssueAsync 返回 false）时用它，替换原来的"命令下发失败"。
	// ⚠️ 该文案不只用于 GPU 超频：WriteFirmwareField/开关步写失败都走同一分支，所以用通用短句；
	//    GPU 超频那一步渲染出来就是「未生效：GPU 超频（这次没写进去，可以再点一次『应用』）」。
	private const string WriteRejectedDetail = "这次没写进去，可以再点一次『应用』";

	// F3：下发过程抛异常时的状态行短句（异常原文只进日志，见下方 catch）。
	private const string StepErrorDetail = "下发时报错，详见日志";

	public static async Task<PerfApplyOutcome> RunAsync(string modeId, IReadOnlyList<PerfApplyStep> steps, IPerfModeBackend backend, CancellationToken ct = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrEmpty(modeId, "modeId");
		ArgumentNullException.ThrowIfNull(steps, "steps");
		ArgumentNullException.ThrowIfNull(backend, "backend");
		List<PerfApplyStepOutcome> outcomes = new List<PerfApplyStepOutcome>(steps.Count);
		bool previousWasFirmwareField = false;
		int stepIndex = 0;
		foreach (PerfApplyStep step in steps)
		{
			ct.ThrowIfCancellationRequested();
			if (step.Kind == PerfApplyStepKind.WriteFirmwareField)
			{
				// 2026-10-09（崩溃针对性改动，埋点 B）：字段下发【之前】先落一行，一眼看出"最后打出的那一行"＝死在哪个字段。
				// ⚠️ 必须走 Logger.WriteLine（不去重、不节流）：重复字段（如相邻档位 PL1 同值）连打也一字不少，
				//    若换成 WriteLineIfChanged / WriteLineThrottled 会被吞掉，这里就失去"最后一行"的定位能力。
				// ⚠️ 只在每次激活时每个字段各打一行（本 foreach 每步至多一行），不做周期性/逐帧刷屏。
				// 字段名与取值直接取自下发的 PerfApplyStep；index 与总步数对齐 PerfModeService 的 "steps=N" 与 PerfModeApplyPlanner 的构造顺序。
				Logger.WriteLine($"PerfModes: issuing WriteFirmwareField: {step.WireKey}={step.Value} (index {stepIndex}/{steps.Count})");
			}
			stepIndex++;
			if (previousWasFirmwareField && step.Kind == PerfApplyStepKind.WriteFirmwareField)
			{
				await backend.DelayAsync(120, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			previousWasFirmwareField = step.Kind == PerfApplyStepKind.WriteFirmwareField;
			bool flag;
			try
			{
				flag = await IssueAsync(step, backend, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				// F3（2026-10-09）：异常原文只进日志，状态行只放短句——原来把 ex2.Message 直接塞进状态行，
				// 会把 NVAPI/GCU 的长异常（甚至"no elevated helper"之类内部措辞）原样铺在界面上。
				Logger.WriteLine($"PerfModes: step {step.Kind} ({step.WireKey ?? "-"}) threw: {ex2}");
				outcomes.Add(new PerfApplyStepOutcome(step, PerfApplyResult.Failed, StepErrorDetail));
				if (IsSwitch(step))
				{
					break;
				}
				continue;
			}
			if (!flag)
			{
				// F3：写调用被拒。⚠️ 这行不只用于 GPU 超频（任何 WriteFirmwareField/开关步写失败都走这里），
				// 故用通用短句；界面会渲染成「未生效：GPU 超频（这次没写进去，可以再点一次『应用』）」。
				outcomes.Add(new PerfApplyStepOutcome(step, PerfApplyResult.Failed, WriteRejectedDetail));
				if (IsSwitch(step))
				{
					break;
				}
				continue;
			}
			bool? flag2 = await backend.VerifyAsync(step, ct).ConfigureAwait(continueOnCapturedContext: false);
			PerfApplyStepOutcome item;
			if (flag2.HasValue)
			{
				item = ((flag2 != true) ? new PerfApplyStepOutcome(step, PerfApplyResult.Failed, "设下去了，但读回来对不上") : new PerfApplyStepOutcome(step, PerfApplyResult.Confirmed));
			}
			else
			{
				// F3：写成功但没法即时确认（如 OverClockingSwitch 走 ServiceEcho、GpuClockOffset 后端缺失）⇒ 未确认（Sent），不是失败。
				item = new PerfApplyStepOutcome(step, PerfApplyResult.Sent, "已下发，暂时确认不了");
			}
			outcomes.Add(item);
			if (IsSwitch(step))
			{
				if (outcomes[outcomes.Count - 1].Result == PerfApplyResult.Failed)
				{
					break;
				}
			}
		}
		return new PerfApplyOutcome(modeId, outcomes);
	}

	private static bool IsSwitch(PerfApplyStep step)
	{
		PerfApplyStepKind kind = step.Kind;
		if (kind == PerfApplyStepKind.SwitchBuiltIn || kind == PerfApplyStepKind.SwitchFirmwareSlot)
		{
			return true;
		}
		return false;
	}

	private static Task<bool> IssueAsync(PerfApplyStep step, IPerfModeBackend backend, CancellationToken ct)
	{
		return step.Kind switch
		{
			PerfApplyStepKind.SwitchBuiltIn => backend.SwitchBuiltInAsync((PerfModeKind)step.Number, ct), 
			PerfApplyStepKind.SwitchTurboSubMode => backend.SwitchTurboSubModeAsync(step.Number == 1, ct), 
			PerfApplyStepKind.SwitchFirmwareSlot => backend.SwitchFirmwareSlotAsync(step.Number, ct), 
			PerfApplyStepKind.WriteFirmwareField => backend.WriteDetailFieldAsync(step.WireKey, step.Value, ct), 
			PerfApplyStepKind.WriteFanCurve => backend.WriteFanCurveAsync(step.IsCpuCurve, step.Curve, ct), 
			PerfApplyStepKind.ApplyGpuPowerLimit => backend.ApplyGpuPowerLimitAsync(step.Number, ct),
			PerfApplyStepKind.ApplyGpuOverclock => backend.ApplyGpuOverclockAsync(step.Number == 1, step.GpuOffsets?.Core ?? 0, step.GpuOffsets?.Memory ?? 0, ct), 
			PerfApplyStepKind.ApplyFanBoost => backend.SetFanBoostAsync(step.Number == 1, ct), 
			PerfApplyStepKind.ApplyRefreshRate => backend.SetRefreshRateAsync(step.Number, ct), 
			PerfApplyStepKind.ApplyPowerPlan => Task.FromResult(backend.SetPowerPlan(step.Value)), 
			PerfApplyStepKind.ApplyCpuBoost => Task.FromResult(backend.SetCpuBoost(step.Number)), 
			PerfApplyStepKind.ApplyPowerOverlay => Task.FromResult(backend.SetPowerOverlay(step.Number)), 
			_ => throw new ArgumentOutOfRangeException("step", step.Kind, "unknown apply step"), 
		};
	}
}
