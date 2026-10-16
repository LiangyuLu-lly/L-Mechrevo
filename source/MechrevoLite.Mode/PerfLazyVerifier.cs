using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Properties;

namespace MechrevoLite.Mode;

/// <summary>
/// (b) 懒验证（2026-10-09，用户原话「OK 按你说的」）：
/// 把状态为 <see cref="PerfApplyResult.Sent"/>（已下发·待确认）的步骤**连同它的期望值一起留着**，
/// 然后在 ① <b>多拍重读</b>（<see cref="ProbeDelaysMs"/>，默认 300ms / 2s / 4s —— 就是 (a) 要求的那串多拍，
/// 替换原来"只阻塞 300ms 重读一次"）与 ② <b>每一次 GCU 状态刷新</b>（<see cref="ProbeNow"/>，由
/// MechrevoHw.CustomModeChanged 触发）上再读一次回读值：
/// ① 读到与目标一致 ⇒ 该步骤**原地升级为 Confirmed**，重抛 Applied，状态行/汇总/tooltip 一起刷新（红字消失）；
/// ② 超过期限（<see cref="DeadlineMs"/>，默认 30s）仍读到"对不上" ⇒ 升级为
///    <see cref="PerfApplyResult.Stale"/>（文案「可能没生效，可以再点一次『应用』」）——**不算失败**，也不谎报已生效；
/// ③ 期限到了但**根本读不到**（回读通道不可用）⇒ 不打"可能没生效"的标签，维持既有
///    「已下发，暂时确认不了」并记一行日志（诚实：读不到 ≠ 没生效）。
/// ⚠️ 全程异步：<see cref="Register"/> 登记后立刻返回，**绝不阻塞下发链**（下发完就返回，读回结果回来再更新状态）。
/// ⚠️ 只登记"有回读通道"的步骤：GCU 页面字段（与 reconcile 同一份 <see cref="PerfLiveSnapshot"/>）与
/// GPU 超频步骤；风扇曲线这类**没有回读**的步骤一律不登记，绝不误报"可能没生效"。
/// </summary>
internal sealed class PerfLazyVerifier
{
	/// <summary>(a) 多拍重读序列：300ms / 2s / 4s（首拍就是原来那一拍 300ms，只是不再阻塞下发）。</summary>
	internal static int[] ProbeDelaysMs { get; set; } = new int[3] { 300, 2000, 4000 };

	/// <summary>多拍跑完后，改成跟着 GCU 状态刷新 + 每秒一拍，直到期限。</summary>
	internal static int RefreshProbeIntervalMs { get; set; } = 1000;

	/// <summary>(b) 期限：默认 30s（或这期间的任何一次状态刷新）。到点仍"读得到却对不上"才标「可能没生效」。</summary>
	internal static int DeadlineMs { get; set; } = 30000;

	/// <summary>后台循环的节拍（只在有登记项时才运行）。</summary>
	internal static int LoopIntervalMs { get; set; } = 250;

	private sealed class Pending
	{
		public string ModeId = "";

		public PerfApplyStep Step = null!;

		public long StartedTicks;

		public long DeadlineTicks;

		public int Stage;

		public long NextProbeTicks;

		public int ProbeCount;
	}

	private readonly Func<PerfApplyStep, Task<bool?>> _probe;

	private readonly Action<PerfApplyOutcome> _onUpgraded;

	private readonly Action _onGpuOverclockConfirmed;

	private readonly object _lock = new object();

	private readonly List<Pending> _pending = new List<Pending>();

	private readonly Dictionary<string, PerfApplyOutcome> _lastOutcome = new Dictionary<string, PerfApplyOutcome>(StringComparer.Ordinal);

	private int _loopRunning;

	internal PerfLazyVerifier(Func<PerfApplyStep, Task<bool?>> probe, Action<PerfApplyOutcome> onUpgraded, Action onGpuOverclockConfirmed)
	{
		_probe = probe ?? throw new ArgumentNullException("probe");
		_onUpgraded = onUpgraded ?? throw new ArgumentNullException("onUpgraded");
		_onGpuOverclockConfirmed = onGpuOverclockConfirmed ?? throw new ArgumentNullException("onGpuOverclockConfirmed");
	}

	/// <summary>当前还挂着的"已下发·待确认"步骤数（给日志/自检用）。</summary>
	internal int PendingCount
	{
		get
		{
			lock (_lock)
			{
				return _pending.Count;
			}
		}
	}

	/// <summary>
	/// 登记一次下发结果里**仍待确认**的步骤（只登记有回读通道的）。
	/// ⚠️ 同一档位的新结果会先把该档位旧的待办清掉（避免用旧期望值升级新结果）；
	///    结果里若一个可回读步骤都没有（例如只下发 app-side 设置），**不动**既有待办。
	/// </summary>
	internal void Register(PerfApplyOutcome outcome, bool gpuOverclockReadbackAvailable)
	{
		ArgumentNullException.ThrowIfNull(outcome, "outcome");
		List<PerfApplyStep> list = outcome.Steps.Select((PerfApplyStepOutcome s) => s.Step).Where((PerfApplyStep step) => IsVerifiable(step, gpuOverclockReadbackAvailable)).ToList();
		if (list.Count == 0)
		{
			return;
		}
		long now = Environment.TickCount64;
		List<PerfApplyStep> list2 = new List<PerfApplyStep>();
		lock (_lock)
		{
			_lastOutcome[outcome.ModeId] = outcome;
			_pending.RemoveAll((Pending p) => string.Equals(p.ModeId, outcome.ModeId, StringComparison.Ordinal));
			foreach (PerfApplyStepOutcome item in outcome.Steps)
			{
				if (item.Result == PerfApplyResult.Sent && IsVerifiable(item.Step, gpuOverclockReadbackAvailable))
				{
					list2.Add(item.Step);
					_pending.Add(new Pending
					{
						ModeId = outcome.ModeId,
						Step = item.Step,
						StartedTicks = now,
						DeadlineTicks = now + DeadlineMs,
						Stage = 0,
						NextProbeTicks = now + ((ProbeDelaysMs.Length != 0) ? ProbeDelaysMs[0] : RefreshProbeIntervalMs)
					});
				}
			}
			if (_pending.Count != 0 && _loopRunning == 0)
			{
				_loopRunning = 1;
				_ = Task.Run((Func<Task>)LoopAsync);
			}
		}
		if (list2.Count != 0)
		{
			Logger.WriteLine($"PerfModes: lazy-verify 登记 {list2.Count} 个待确认步骤（多拍重读 {DescribeDelays()} + 每次 GCU 状态刷新，期限 {(DeadlineMs / 1000)}s）: {string.Join(", ", list2.Select((PerfApplyStep s) => s.ToString()))}");
		}
	}

	/// <summary>
	/// (b) "下一次 GCU 状态刷新"入口：由 MechrevoHw.CustomModeChanged 触发，只把待办标成"立刻读"，
	/// 真正读回读在后台循环里做 —— 事件处理器里绝不读硬件、不做慢活。
	/// </summary>
	internal void ProbeNow()
	{
		lock (_lock)
		{
			if (_pending.Count == 0)
			{
				return;
			}
			foreach (Pending item in _pending)
			{
				item.NextProbeTicks = 0L;
			}
			if (_loopRunning == 0)
			{
				_loopRunning = 1;
				_ = Task.Run((Func<Task>)LoopAsync);
			}
		}
	}

	private static string DescribeDelays()
	{
		return string.Join("/", ProbeDelaysMs.Select((int ms) => (ms >= 1000) ? ((ms / 1000) + "s") : (ms + "ms")));
	}

	private async Task LoopAsync()
	{
		try
		{
			while (true)
			{
				await Task.Delay(Math.Max(50, LoopIntervalMs)).ConfigureAwait(continueOnCapturedContext: false);
				List<Pending> due = TakeDue();
				if (due.Count == 0)
				{
					lock (_lock)
					{
						if (_pending.Count == 0)
						{
							_loopRunning = 0;
							return;
						}
					}
					continue;
				}
				foreach (Pending item in due)
				{
					await EvaluateAsync(item).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes: lazy-verify loop failed: " + ex.Message);
			lock (_lock)
			{
				_loopRunning = 0;
			}
		}
	}

	private List<Pending> TakeDue()
	{
		long now = Environment.TickCount64;
		List<Pending> list = new List<Pending>();
		lock (_lock)
		{
			foreach (Pending item in _pending)
			{
				if (now >= item.NextProbeTicks)
				{
					item.NextProbeTicks = now + 100000L;
					list.Add(item);
				}
			}
		}
		return list;
	}

	private async Task EvaluateAsync(Pending item)
	{
		bool? flag;
		try
		{
			flag = await _probe(item.Step).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes: lazy-verify probe failed (" + item.Step.ToString() + "): " + ex.Message);
			flag = null;
		}
		item.ProbeCount++;
		long now = Environment.TickCount64;
		long num = now - item.StartedTicks;
		if (flag == true)
		{
			// 读到与目标一致 ⇒ 原地升级为已确认（状态行/汇总/tooltip 一起刷新，红字消失）。
			Upgrade(item, PerfApplyResult.Confirmed, "", num);
			return;
		}
		if (now >= item.DeadlineTicks)
		{
			if (flag == false)
			{
				// 期限到、仍"读得到但对不上" ⇒ 诚实地标「可能没生效」（不是失败），并给重试口径。
				Upgrade(item, PerfApplyResult.Stale, Strings.PerfModeStaleDetail, num);
			}
			else
			{
				// 期限到但根本读不到（无回读通道）⇒ 不打「可能没生效」，维持既有「已下发，暂时确认不了」。
				Logger.WriteLine($"PerfModes: lazy-verify {item.Step} 期限({DeadlineMs / 1000}s)到，但一直读不到回读值（无可回读通道，共 {item.ProbeCount} 拍）⇒ 维持「已下发，暂时确认不了」：不当失败，也不标「可能没生效」");
				Remove(item);
			}
			return;
		}
		lock (_lock)
		{
			item.Stage++;
			int num2 = ((item.Stage < ProbeDelaysMs.Length) ? ProbeDelaysMs[item.Stage] : RefreshProbeIntervalMs);
			item.NextProbeTicks = now + num2;
		}
	}

	/// <summary>把该步骤在**最近一次结果**里原地换成新状态，然后重抛一次 Applied 让界面跟着刷新。</summary>
	private void Upgrade(Pending item, PerfApplyResult result, string detail, long elapsedMs)
	{
		PerfApplyOutcome outcome = null;
		lock (_lock)
		{
			_pending.Remove(item);
			if (_lastOutcome.TryGetValue(item.ModeId, out PerfApplyOutcome value))
			{
				List<PerfApplyStepOutcome> list = new List<PerfApplyStepOutcome>(value.Steps.Count);
				bool flag = false;
				foreach (PerfApplyStepOutcome step in value.Steps)
				{
					if (!flag && step.Result == PerfApplyResult.Sent && IsSameStep(step.Step, item.Step))
					{
						flag = true;
						list.Add(step with
						{
							Result = result,
							Detail = detail
						});
					}
					else
					{
						list.Add(step);
					}
				}
				if (flag)
				{
					outcome = value with
					{
						Steps = list,
						LazyUpgrade = true
					};
					_lastOutcome[item.ModeId] = outcome;
				}
			}
		}
		if (result == PerfApplyResult.Confirmed)
		{
			Logger.WriteLine($"PerfModes: lazy-verify {item.Step} -> 已确认（第 {item.ProbeCount} 拍读到与目标一致，下发后 {elapsedMs}ms）");
			if (IsGpuOverclockStep(item.Step))
			{
				try
				{
					_onGpuOverclockConfirmed();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("PerfModes: lazy-verify GPU OC confirm callback failed: " + ex.Message);
				}
			}
		}
		else
		{
			Logger.WriteLine($"PerfModes: lazy-verify {item.Step} 期限({DeadlineMs / 1000}s)到仍未读到一致 ⇒ 标记「可能没生效」（仍不算失败，可以再点一次『应用』），共读 {item.ProbeCount} 拍 / {elapsedMs}ms");
		}
		if (outcome != null)
		{
			_onUpgraded(outcome);
		}
	}

	private void Remove(Pending item)
	{
		lock (_lock)
		{
			_pending.Remove(item);
		}
	}

	/// <summary>
	/// 是否有**可用的回读通道**：
	/// ① GCU 页面字段（PL/温度墙/GPU 功耗/超频开关与偏移…）—— 与 reconcile 同一份 PerfLiveSnapshot；
	/// ② GPU 超频步骤在 NVAPI 直连后端存在时走驱动回读（多拍重读的主用场）。
	/// ⚠️ 风扇曲线（WriteFanCurve）等**没有回读**的步骤一律不登记，避免"其实写好了却报可能没生效"。
	/// </summary>
	private static bool IsVerifiable(PerfApplyStep step, bool gpuOverclockReadbackAvailable)
	{
		if (step.Kind == PerfApplyStepKind.ApplyGpuOverclock)
		{
			return gpuOverclockReadbackAvailable;
		}
		if (step.Kind != PerfApplyStepKind.WriteFirmwareField)
		{
			return false;
		}
		switch (step.WireKey)
		{
		case "PL1":
		case "PL2":
		case "PL4":
		case "CpuTccOffsetSwitch":
		case "CpuTccOffset":
		case "GpuConfigurableTGPTarget":
		case "GpuDynamicBoostSwitch":
		case "GpuDynamicBoost":
		case "FanSwitchSpeedEnabled":
		case "FanSwitchSpeed":
		// GPU 超频开关/偏移：直连后端可用时走驱动回读；走 GCU 时 PerfLiveSnapshot 里的 Effective* 也是同一路读数。
		case "OverClockingSwitch":
		case "GpuCoreClockOffsetOC":
		case "GpuMemoryClockOffsetOC":
			return true;
		default:
			return false;
		}
	}

	internal static bool IsGpuOverclockStep(PerfApplyStep step)
	{
		if (step.Kind != PerfApplyStepKind.ApplyGpuOverclock)
		{
			switch (step.WireKey)
			{
			case "OverClockingSwitch":
			case "GpuCoreClockOffsetOC":
			case "GpuMemoryClockOffsetOC":
				return true;
			default:
				return false;
			}
		}
		return true;
	}

	private static bool IsSameStep(PerfApplyStep a, PerfApplyStep b)
	{
		if (a.Kind == b.Kind && string.Equals(a.WireKey, b.WireKey, StringComparison.Ordinal) && string.Equals(a.Value, b.Value, StringComparison.Ordinal) && a.Number == b.Number)
		{
			return a.GpuOffsets.Equals(b.GpuOffsets);
		}
		return false;
	}

	/// <summary>
	/// 与 reconcile 同一份快照比对：只有"快照里真有这个字段"时才给结论，否则返回 null（读不到 ≠ 不一致）。
	/// </summary>
	internal static bool? CompareLive(PerfLiveSnapshot live, PerfApplyStep step)
	{
		if (live == null || step.Kind != PerfApplyStepKind.WriteFirmwareField || !int.TryParse(step.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
		{
			return null;
		}
		return step.WireKey switch
		{
			"PL1" => Compare(live.Pl1, value), 
			"PL2" => Compare(live.Pl2, value), 
			"PL4" => Compare(live.Pl4, value), 
			"CpuTccOffsetSwitch" => CompareSwitch(live.TccOn, value), 
			"CpuTccOffset" => Compare(live.TccTarget, value), 
			"GpuConfigurableTGPTarget" => Compare(live.GpuTgp, value), 
			"GpuDynamicBoostSwitch" => CompareSwitch(live.GpuDynamicBoostOn, value), 
			"GpuDynamicBoost" => Compare(live.GpuDynamicBoost, value), 
			"FanSwitchSpeedEnabled" => CompareSwitch(live.FanSwitchSpeedOn, value), 
			"FanSwitchSpeed" => Compare(live.FanSwitchSpeedMs, value), 
			"OverClockingSwitch" => CompareSwitch(live.GpuOverclockOn, value), 
			"GpuCoreClockOffsetOC" => Compare(live.GpuCoreOffset, value), 
			"GpuMemoryClockOffsetOC" => Compare(live.GpuMemoryOffset, value), 
			_ => null, 
		};
		static bool? Compare(int? actual, int expected)
		{
			return actual.HasValue ? new bool?(actual.GetValueOrDefault() == expected) : ((bool?)null);
		}
		static bool? CompareSwitch(bool? actual, int expected)
		{
			return actual.HasValue ? new bool?(actual.GetValueOrDefault() == (expected == 1)) : ((bool?)null);
		}
	}
}
