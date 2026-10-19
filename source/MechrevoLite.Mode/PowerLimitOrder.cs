using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MechrevoLite.Mode;
// **PL1 ≤ PL2 ≤ PL4 联动约束的唯一事实来源**（ 纯函数： 不读硬件、 不打日志、 无副作用）。
internal static class PowerLimitOrder
{
	/// <summary>⭐ 参与约束的三项（⭐ 名字与下发字段同名，便于日志/字段映射）✅。</summary>
	internal enum Field
	{
		Pl1,
		Pl2,
		Pl4
	}

	/// <summary>⭐ 硬件的可调范围（⭐ 只在抬高的方向上用于夹取）✅。</summary>
	internal readonly record struct Range(int Min, int Max)
	{
		internal bool Usable => Max > Min;

		internal int Clamp(int value)
		{
			return Math.Clamp(value, Min, Max);
		}

		/// <summary>⭐ 范围读不到时用"不夹"（⭐ 保留调用方传入值）✅。</summary>
		internal static Range Unbounded => new Range(int.MinValue, int.MaxValue);
	}

	/// <summary>⭐ 一次联动里"哪一项从多少变成了多少"（⭐ 仅用于日志/UI 显示）✅。</summary>
	internal readonly record struct Change(Field Field, int From, int To);

	/// <summary>
	/// ⭐ 把三元组规范成 **PL1 ≤ PL2 ≤ PL4**（⭐ 联动方向见类型注释）。
	/// <para>⚠️ 返回值 = 是否**最终**满足约束（⭐ 硬件范围互相冲突这种极端情形才会 false ⇒
	/// 调用方应放弃下发/保持原值，⛔ 不发违规组合）✅。</para>
	/// </summary>
	/// <param name="changed">⭐ 用户这一次改的是哪一项；<c>null</c> = "多字段同批/回放"（⭐ 优先往下压）✅。</param>
	/// <param name="pl1">⭐ 当前 PL1（<c>null</c> = 未知，⛔ 不参与联动）✅。</param>
	/// <param name="pl2">⭐ 当前 PL2（同上）✅。</param>
	/// <param name="pl4">⭐ 当前 PL4（同上；无 PL4 的机型传 <c>null</c>）✅。</param>
	/// <param name="pl1Range">⭐ PL1 硬件范围（抬高时夹取）✅。</param>
	/// <param name="pl2Range">⭐ PL2 硬件范围 ✅。</param>
	/// <param name="pl4Range">⭐ PL4 硬件范围 ✅。</param>
	/// <param name="linkedPl1">⭐ 联动后的 PL1（<c>null</c> = 未知/不参与）✅。</param>
	/// <param name="linkedPl2">⭐ 联动后的 PL2 ✅。</param>
	/// <param name="linkedPl4">⭐ 联动后的 PL4 ✅。</param>
	/// <param name="changes">⭐ 实际发生变化的项（⭐ 顺序 = PL1 → PL2 → PL4）✅。</param>
	/// <param name="reason">⭐ 日志用原因（例如「因 PL2=100」）✅。</param>
	internal static bool TryLink(
		Field? changed,
		int? pl1,
		int? pl2,
		int? pl4,
		Range pl1Range,
		Range pl2Range,
		Range pl4Range,
		out int? linkedPl1,
		out int? linkedPl2,
		out int? linkedPl4,
		out IReadOnlyList<Change> changes,
		out string reason)
	{
		int? a = Known(pl1);
		int? b = Known(pl2);
		int? c = Known(pl4);
		// ⭐ 先把"本次改的 / 已知的"值夹进各自硬件范围，再做联动 —— ⭐ 否则会拿一个"物理上不可能"的值
		//   去抬高 PL2（⭐ 例：PL1 上限 120，用户填 130 ⇒ 应把 PL2 抬到 120，⛔ 不是 130）✅
		a = a.HasValue ? pl1Range.Clamp(a.Value) : null;
		b = b.HasValue ? pl2Range.Clamp(b.Value) : null;
		c = c.HasValue ? pl4Range.Clamp(c.Value) : null;
		int? originalA = a;
		int? originalB = b;
		int? originalC = c;

		// ── ① 先按"用户改的是哪一项"决定联动方向 ──
		if (changed == Field.Pl2)
		{
			// 用户把 PL2 降／升到低于 PL1 ⇒ 把 PL1 一起降（⛔ 不替用户抬 PL2）。
			if (a.HasValue && b.HasValue && b.Value < a.Value)
			{
				a = b;
			}
		}
		else if (changed == Field.Pl1)
		{
			// 用户把 PL1 抬到高于 PL2 ⇒ 把 PL2（以及 PL4）一起抬（⭐ 受各自硬件上限约束）。
			if (a.HasValue && b.HasValue && a.Value > b.Value)
			{
				b = pl2Range.Clamp(a.Value);
				if (b.Value < a.Value)
				{
					// PL2 顶到上限仍低于 PL1 ⇒ 反过来把 PL1 压到 PL2 上限（⭐ 仍是合法组合）。
					a = b;
				}
				if (c.HasValue && c.Value < b.Value)
				{
					// ⭐ 只有 PL4 **低于** PL2 时才抬 PL4；PL4 本来更高就原样保留（⛔ 不把它压下来）✅
					c = pl4Range.Clamp(b.Value);
					if (c.Value < b.Value)
					{
						b = c;
						if (a.HasValue && a.Value > b.Value)
						{
							a = b;
						}
					}
				}
			}
		}
		else if (changed == Field.Pl4)
		{
			// 用户把 PL4 降到低于 PL2 ⇒ 把 PL2 一起降，再按关系把 PL1 一起降（⭐ 级联）。
			if (b.HasValue && c.HasValue && c.Value < b.Value)
			{
				b = pl2Range.Clamp(c.Value);
				if (a.HasValue && a.Value > b.Value)
				{
					a = b;
				}
			}
		}

		// ── ② 通用兜底（⭐ 多字段同批 / 历史非法存档回放）：优先往下压，直到关系成立 ──
		for (int pass = 0; pass < 3; pass++)
		{
			bool moved = false;
			if (a.HasValue && b.HasValue && a.Value > b.Value)
			{
				a = b;
				moved = true;
			}
			if (b.HasValue && c.HasValue && c.Value < b.Value)
			{
				b = pl2Range.Clamp(c.Value);
				moved = true;
			}
			if (!moved)
			{
				break;
			}
		}

		// ── ③ 夹进各自范围（⭐ 只夹已知值；⭐ 夹完再查一次关系）──
		a = a.HasValue ? pl1Range.Clamp(a.Value) : null;
		b = b.HasValue ? pl2Range.Clamp(b.Value) : null;
		c = c.HasValue ? pl4Range.Clamp(c.Value) : null;
		if (a.HasValue && b.HasValue && a.Value > b.Value)
		{
			a = b;
		}
		if (b.HasValue && c.HasValue && c.Value < b.Value)
		{
			b = c;
		}
		if (a.HasValue && b.HasValue && a.Value > b.Value)
		{
			a = b;
		}

		// ── ④ 结果 + 变化列表 + 原因 ──
		linkedPl1 = a;
		linkedPl2 = b;
		linkedPl4 = c;
		List<Change> list = new List<Change>(3);
		if (a.HasValue && originalA.HasValue && a.Value != originalA.Value)
		{
			list.Add(new Change(Field.Pl1, originalA.Value, a.Value));
		}
		if (b.HasValue && originalB.HasValue && b.Value != originalB.Value)
		{
			list.Add(new Change(Field.Pl2, originalB.Value, b.Value));
		}
		if (c.HasValue && originalC.HasValue && c.Value != originalC.Value)
		{
			list.Add(new Change(Field.Pl4, originalC.Value, c.Value));
		}
		changes = list;
		reason = changed switch
		{
			Field.Pl1 => a.HasValue ? "因 PL1=" + a.Value.ToString(CultureInfo.InvariantCulture) : "因 PL1",
			Field.Pl2 => b.HasValue ? "因 PL2=" + b.Value.ToString(CultureInfo.InvariantCulture) : "因 PL2",
			Field.Pl4 => c.HasValue ? "因 PL4=" + c.Value.ToString(CultureInfo.InvariantCulture) : "因 PL4",
			_ => "保持 PL1 ≤ PL2 ≤ PL4"
		};
		bool ok;
		if (a.HasValue && b.HasValue && c.HasValue)
		{
			ok = a.Value <= b.Value && b.Value <= c.Value;
		}
		else if (a.HasValue && b.HasValue)
		{
			ok = a.Value <= b.Value;
		}
		else if (b.HasValue && c.HasValue)
		{
			ok = b.Value <= c.Value;
		}
		else
		{
			ok = true;
		}
		return ok;
	}

	/// <summary>⭐ 拼出机主要求的那一行日志（⭐ 例：<c>[pl] 联动：PL1 120→100（因 PL2=100）</c>）✅。</summary>
	internal static string Describe(IReadOnlyList<Change> changes, string reason)
	{
		if (changes == null || changes.Count == 0)
		{
			return string.Empty;
		}
		StringBuilder sb = new StringBuilder("[pl] 联动：");
		for (int i = 0; i < changes.Count; i++)
		{
			if (i > 0)
			{
				sb.Append("、");
			}
			Change change = changes[i];
			sb.Append(Name(change.Field)).Append(' ').Append(change.From.ToString(CultureInfo.InvariantCulture))
				.Append('→').Append(change.To.ToString(CultureInfo.InvariantCulture));
		}
		if (!string.IsNullOrEmpty(reason))
		{
			sb.Append("（").Append(reason).Append('）');
		}
		return sb.ToString();
	}

	/// <summary>⭐ 字段名（⭐ 与 <c>SetCustomDetail</c> 的 wire key 同名）✅。</summary>
	internal static string Name(Field field)
	{
		return field switch
		{
			Field.Pl1 => "PL1",
			Field.Pl2 => "PL2",
			Field.Pl4 => "PL4",
			_ => field.ToString()
		};
	}

	/// <summary>⭐ 只有 <c>&gt; 0</c> 才是"已知值"（⭐ 0/负数 = 未设置/读不到 ⇒ 不参与联动）✅。</summary>
	private static int? Known(int? value)
	{
		return (value.HasValue && value.Value > 0) ? value : null;
	}
}
