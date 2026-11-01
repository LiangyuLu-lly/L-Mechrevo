// ⭐⭐ 2026-10-17 · 宝龙达板号白名单**断言探针**
//
// 为什么要它：白名单漏判（裸 `MRID6`）是 2026-10-17 那台蛟龙16「点了没反应」的**根因**，
// 而开发机是同方 `GM5HG0A` ⇒ 永远走不到"命中"那一支 ⇒ **只能靠断言证明判据本身对**。
//
// 跑法：dotnet run --project tools\dev\board-whitelist-probe -c Release
// 退出码：0 = 全过；1 = 有失败。

using System;
using System.Collections.Generic;
using MechrevoLite.Hardware;

internal static class Program
{
	private static int _pass;
	private static int _fail;

	private static void Case(string board, bool expected, string why)
	{
		bool actual = BoardWhitelist.Matches(board);
		bool ok = actual == expected;
		if (ok) { _pass++; }
		else { _fail++; }
		Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] 板号={(board == null ? "<null>" : "「" + board + "」")} 期望={expected} 实际={actual} —— {why}");
	}

	private static void Main()
	{
		Console.WriteLine("=== 宝龙达板号白名单断言 ===");
		Console.WriteLine();
		Console.WriteLine("--- ① 必须命中（宝龙达）---");
		// ⭐ 本次事故的板号 —— 少了这条就等于没修
		Case("MRID6", true, "⭐ 2026-10-17 真机：蛟龙16 · 6800H＋3070Ti（本次漏判的那个）");
		Case("MRID6-23", true, "40 系 · 蛟龙16Pro · 7945HX＋4070（三台真机）");
		Case("MRID6-40", true, "假想后续变体 —— 连字符前缀应放行");
		Case("MRID6-50", true, "假想后续变体 —— 连字符前缀应放行");
		Case("mrid6", true, "小写 —— 大小写不敏感");
		Case("  MRID6  ", true, "两侧空白 —— 先 Trim 再判");
		Case("Mrid6-23", true, "混合大小写 —— 大小写不敏感");

		Console.WriteLine();
		Console.WriteLine("--- ② 必须不命中（同方 / 其它 / 异常输入）---");
		Case("GM5HG0A", false, "⭐ 同方翼龙15Pro（开发机）—— 绝不能误伤");
		Case("GM6IX8B", false, "同方机型");
		Case("KUANGSHI-JIGUANG Series-X6AR55xW", false, "同方 5070 机型");
		Case("YAOSHI Series-X6AR55xY", false, "同方 5070 机型");
		Case("Standard", false, "板号读不到时的兜底字面量");
		Case("MRID6X", false, "⚠️ 以 MRID6 开头但**后面不是连字符** —— 这正是不用纯前缀的原因");
		Case("MRID60", false, "⚠️ 同上（纯前缀会误放行）");
		Case("MRID6A", false, "⚠️ 同上");
		Case("MRID", false, "更短的前缀");
		Case("MRID5-23", false, "不同家族");
		Case("", false, "空串 —— 安全方向");
		Case("   ", false, "全空白 —— 安全方向");
		Case(null, false, "null —— 安全方向");

		Console.WriteLine();
		Console.WriteLine($"=== PASS={_pass} FAIL={_fail} ===");
		Environment.Exit(_fail == 0 ? 0 : 1);
	}
}
