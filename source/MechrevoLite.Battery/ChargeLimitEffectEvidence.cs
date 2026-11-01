using System;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

namespace MechrevoLite.Battery;

/// <summary>
/// 2026-10-10（限充真效果判据 · 按上游 Leo-stro/mechrevo-charge-limit 口径）：
/// 与 <see cref="ChargeLimitEvidence"/> **同层**的第二份证据 —— 上游口径的"真效果"判据。
///
/// 背景（上游做法，实测机 = 无界 15X，非 50 系）：
/// ① 写 0x07B9=上限、0x07C3 原值先存后置 4，保持约 2.5s 让固件 store_limit() 把上限抄进存储区 0x087F；
/// ② 还原 0x07C3 并**回读确认**（失败重试一次）；
/// ③ 之后再等约 7s，读 **0x0742 的 bit2 = 1** 才算"真的生效"。
/// 我们的旧判据只看"0x07B9 读回 == 我写的值"，那只能证明**写进去了**，不能证明**生效了**。
///
/// 本类的两种判定与区分：
/// <list type="bullet">
/// <item><see cref="ChargeLimitEffect.Verified"/> = 0x0742 读得到且 bit2 = 1 ⇒ ✅ 生效；</item>
/// <item><see cref="ChargeLimitEffect.Ineffective"/> = 0x0742 读得到但 bit2 ≠ 1 ⇒ ❌ 未生效（报原始值 + bit2）；</item>
/// <item><see cref="ChargeLimitEffect.Undetermined"/> = 0x0742 **读不到** ⇒ ⚠️ 无法判定（⛔ 既不当成功也不当失败）。</item>
/// </list>
/// ⛔ 本类全程**只读**：不写任何寄存器、不改 config、不改 <see cref="EcChargeLimit.Verdict"/>、不撤销限充。
/// 旧判据（0x07B9 回读）作为辅助信息在同一行日志里一起打。
/// </summary>
internal enum ChargeLimitEffect
{
	/// <summary>无法判定：0x0742 读不到（通道不可用 / 机型差异）。</summary>
	Undetermined,
	/// <summary>生效：0x0742 bit2 = 1。</summary>
	Verified,
	/// <summary>未生效：0x0742 读得到但 bit2 ≠ 1。</summary>
	Ineffective
}

/// <summary>一次真效果判定的完整结果（供日志、诊断包、以后按机型判断用）。</summary>
internal sealed class ChargeLimitEffectRecord
{
	internal ChargeLimitEffectRecord(int limit, ChargeLimitEffect effect, ChargeLimitEcProbe probe, DateTime checkedAt, bool superseded)
	{
		Limit = limit;
		Effect = effect;
		Probe = probe;
		CheckedAt = checkedAt;
		Superseded = superseded;
	}

	internal static readonly ChargeLimitEffectRecord None = new(-1, ChargeLimitEffect.Undetermined, ChargeLimitEcProbe.Empty, DateTime.MinValue, false);

	/// <summary>本次判定对应的命令上限（%）。</summary>
	internal int Limit { get; }

	internal ChargeLimitEffect Effect { get; }

	/// <summary>判定当时只读探测到的全部原始值。</summary>
	internal ChargeLimitEcProbe Probe { get; }

	internal DateTime CheckedAt { get; }

	/// <summary>结果产生时已被更新的一次写入取代（仅留档，不采纳为"最近一次"）。</summary>
	internal bool Superseded { get; }

	internal string DescribeVerdict()
	{
		return Effect switch
		{
			ChargeLimitEffect.Verified => "生效",
			ChargeLimitEffect.Ineffective => "未生效",
			_ => "无法判定"
		};
	}
}

internal static class ChargeLimitEffectEvidence
{
	private static readonly object Gate = new object();

	private static int _generation;

	private static ChargeLimitEffectRecord _latest = ChargeLimitEffectRecord.None;

	/// <summary>最近一次已完成的真效果判定（诊断包 / 以后 UI 读取）。</summary>
	internal static ChargeLimitEffectRecord Latest
	{
		get
		{
			lock (Gate)
			{
				return _latest;
			}
		}
	}

	/// <summary>
	/// 一次寄存器写入**已回读确认**之后调用：异步等门控还原完成、再等 <see cref="EcChargeLimit.EffectCheckDelayMs"/>ms，
	/// 然后只读探测并给出上游口径的判定。
	/// ⚠️ 全程异步（<see cref="Task.Run"/> + await），**不阻塞 UI 线程**，也不持任何锁等待。
	/// </summary>
	internal static void Begin(int limit)
	{
		if (limit < EcChargeLimit.MinimumPercent || limit > EcChargeLimit.MaximumPercent)
		{
			return;
		}
		if (limit >= 100)
		{
			// 上限 100% ＝ 不限充：0x0742 的 bit2 只对 <100% 的限充有意义，
			// 此时判"未生效"会在日志里造成"限充不能用"的误导（实测本机启动时就是这条）。
			// 所以只做一次**只读**探测留档（能证明 0x0742 在不在、通道通不通），⛔ 不记录任何判定。
			_ = Task.Run(delegate
			{
				try
				{
					ChargeLimitEcProbe probe = EcChargeLimit.ProbeRegisters();
					Logger.WriteLine($"限充真效果判据：命令上限 100%（＝不限充），跳过生效判定（0x{EcChargeLimit.EffectRegister:X3} bit2 只对 <100% 的限充有意义）；只读探测 {probe.DescribeAll()}；通道 {probe.ChannelNote}");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("限充只读探测失败（不影响限充本身）：" + ex.Message);
				}
			});
			return;
		}
		int generation;
		lock (Gate)
		{
			generation = ++_generation;
		}
		// 先取"这次写入安排的门控还原任务"的快照：判据必须等门控**还原完成**之后再计时（上游顺序）。
		Task gateRestore = EcChargeLimit.GateRestoreCompleted;
		_ = Task.Run(async delegate
		{
			try
			{
				await gateRestore.ConfigureAwait(continueOnCapturedContext: false);
				await Task.Delay(EcChargeLimit.EffectCheckDelayMs).ConfigureAwait(continueOnCapturedContext: false);
				Observe(generation, limit);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("限充真效果判据执行失败（不影响限充本身）：" + ex.Message);
			}
		});
	}

	/// <summary>
	/// 同步执行一次真效果判定（诊断包 / 自检可直接调用）。**只读**。
	/// </summary>
	internal static ChargeLimitEffectRecord Observe(int generation, int limit)
	{
		ChargeLimitEcProbe probe = EcChargeLimit.ProbeRegisters();
		bool? bitSet = probe.EffectBitSet;
		ChargeLimitEffect effect = ((!bitSet.HasValue) ? ChargeLimitEffect.Undetermined : (bitSet.Value ? ChargeLimitEffect.Verified : ChargeLimitEffect.Ineffective));
		bool superseded;
		lock (Gate)
		{
			superseded = generation != _generation;
			if (!superseded)
			{
				_latest = new ChargeLimitEffectRecord(limit, effect, probe, DateTime.Now, superseded: false);
			}
		}
		string verdictText = effect switch
		{
			ChargeLimitEffect.Verified => "✅ 生效", 
			ChargeLimitEffect.Ineffective => "❌ 未生效", 
			_ => "⚠️ 无法判定", 
		};
		string effectRaw = ChargeLimitEcProbe.FormatRaw(probe.Effect);
		string bitText = (bitSet.HasValue ? (bitSet.Value ? "1" : "0") : "?");
		string auxUpper = (probe.Upper < 0) ? "不可读" : (EcChargeLimit.PercentFor(probe.Upper) + "%（已掩 bit7「已启用」标志，低 7 位才是百分数；原始值见上面只读探测）");
		Logger.WriteLine(
			$"限充真效果判据（上游口径 0x{EcChargeLimit.EffectRegister:X3} bit2）{verdictText}：命令上限 {limit}%；" +
			$"0x{EcChargeLimit.EffectRegister:X3}={effectRaw}（bit2={bitText}）{(superseded ? "［已被更新的一次写入取代，仅留档］" : "")}；" +
			$"只读探测 {probe.DescribeAll()}；通道 {probe.ChannelNote}；" +
			$"辅助信息（旧判据）：0x{EcChargeLimit.UpperRegister:X3} 回读 {auxUpper}");
		return new ChargeLimitEffectRecord(limit, effect, probe, DateTime.Now, superseded);
	}

	/// <summary>测试/自检用：清空代次与最近一次结果。</summary>
	internal static void ResetForTests()
	{
		lock (Gate)
		{
			_generation = 0;
			_latest = ChargeLimitEffectRecord.None;
		}
	}
}
