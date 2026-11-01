using System;
using System.Collections.Generic;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（40B ≡ 40，用户原话「对了 LM里面好像有一些GCU判断条件 GCU40和40B应该等价」）：
/// GCU 变体（注册表 <c>HKLM\SOFTWARE\L-Mechrevo\GcuVariant</c>）的**家族**归一化 ——
/// 「40B 与 40 在行为判断上等价」这件事**只在这里定义一次**；凡按变体做分流判断的地方一律走本类，
/// 以后再加变体（例如 50B）只改 <see cref="Resolve"/> 一处。
/// <para><b>⚠️ 只归一「行为判断」，不归一「载荷身份」</b>：选哪个载荷、校验 <c>GcuPayloadSha256</c>、
/// 诊断按变体选落地基线、展示变体名，都必须仍用注册表原值区分 —— 40B 与 40 曾是**不同的字节集**
/// （payload\40 = 4 文件、payload\40B = 8 文件，2026-10-10 世代）。故意不走本类的那一处见
/// <c>DiagnosticGcuEvidenceInfo.BaselineForVariant</c>（那里按变体精确取表）。</para>
/// <para>2026-10-14：随包载荷整体换成「5 个独立 7z」（1020 / 30 / 40 / 50 / WJ），
/// 40B 覆盖层**整体不再存在**。本类保留 40B→40 归一，只是为了让**老机器上遗留的注册表值**
/// （上一版装过 40B 的机器写的就是 <c>GcuVariant=40B</c>）继续按 40 家族判断；新安装不会再产生它。</para>
/// <para>✅ 纯字符串运算：不读注册表、不碰硬件、不写任何状态；离线可跑自测见 <see cref="RunSelfTest"/>。
/// ⚠️ 参数类型不写 <c>string?</c> 注解：本工程未开 nullable（写了会新增 CS8632 警告），
/// 但运行时**允许**传 null，null / 空 / 全空白一律按"没有变体"处理。</para>
/// </summary>
internal static class GcuVariantFamily
{
	/// <summary>40 家族键 —— "40" 与 "40B" 归一后的值。</summary>
	internal const string Forty = "40";

	/// <summary>
	/// 40 系备用载荷变体（Uniwill 覆盖层）；行为上属 <see cref="Forty"/> 家族。
	/// ⚠️ 2026-10-14 起**已退役**：随包载荷换成 5 个 7z 后 40 系只有 40 这一份载荷，
	/// 常量与归一规则仅为兼容老注册表值而保留，⛔ 新代码不得再据此选载荷。
	/// </summary>
	internal const string FortyB = "40B";

	/// <summary>
	/// 变体 → 家族键：去首尾空白 + 转大写后，<c>"40B"</c> 归一到 <c>"40"</c>，其余原样返回
	/// （<c>"30"</c> / <c>"50"</c> / <c>"1020"</c> / <c>"WJ"</c> / <c>"GCU"</c> / 未知值各自成族）；
	/// <c>null</c> / 空 / 全空白 → <c>""</c>（＝注册表没有 GcuVariant，与任何家族都不相等）。
	/// </summary>
	internal static string Resolve(string variant)
	{
		if (string.IsNullOrWhiteSpace(variant))
		{
			return "";
		}
		string text = variant.Trim().ToUpperInvariant();
		if (text == FortyB)
		{
			return Forty;
		}
		return text;
	}

	/// <summary>
	/// 该变体是否属给定家族（"40" 与 "40B" 对家族 "40" 都判真；任一侧为空一律判假）。
	/// </summary>
	internal static bool IsFamily(string variant, string family)
	{
		string left = Resolve(variant);
		return left.Length != 0 && left == Resolve(family);
	}

	/// <summary>是否 40 家族（<c>"40"</c> / <c>"40B"</c>，大小写与首尾空白不敏感）。</summary>
	internal static bool Is40(string variant)
	{
		return Resolve(variant) == Forty;
	}

	/// <summary>
	/// 离线自测（<c>L-Mechrevo.exe --gcu-variant-family [输出.txt]</c>）：只做字符串断言，
	/// 不读注册表、不碰硬件、不联网、不发帧。报告首行含 <c>FAIL=n</c>（0 = 全通过）。
	/// </summary>
	internal static string RunSelfTest()
	{
		List<string> pass = new List<string>();
		List<string> fail = new List<string>();

		// ---- ① 40 家族：40 与 40B 等价（含大小写 / 空白；40B 仅为兼容老注册表值）----
		Check(Is40("40"), "Is40(\"40\") = true", pass, fail);
		Check(Is40(FortyB), "Is40(\"40B\") = true（40B 归一到 40；2026-10-14 起 40B 已退役，只为老注册表值保留）", pass, fail);
		Check(Is40("40b"), "Is40(\"40b\") = true（大小写不敏感）", pass, fail);
		Check(Is40("  40B  "), "Is40(\"  40B  \") = true（首尾空白不敏感）", pass, fail);
		Check(Resolve("40") == Resolve("40B"), "Resolve(\"40\") == Resolve(\"40B\")（同一家族键）", pass, fail);
		Check(IsFamily("40B", Forty) && IsFamily("40", Forty) && IsFamily("40b", " 40 "), "IsFamily：40 / 40B / 40b 对家族 \"40\" 全判真", pass, fail);

		// ---- ② 别的变体一律不落进 40 家族（不误判；2026-10-14 新增 1020 / WJ）----
		Check(!Is40("30"), "Is40(\"30\") = false", pass, fail);
		Check(!Is40("50"), "Is40(\"50\") = false", pass, fail);
		Check(!Is40("1020"), "Is40(\"1020\") = false（1020 不是 40 家族）", pass, fail);
		Check(!Is40("WJ"), "Is40(\"WJ\") = false（WJ 特别变体不是 40 家族）", pass, fail);
		Check(!Is40("GCU"), "Is40(\"GCU\") = false（base 底座不是 40 家族）", pass, fail);
		Check(!Is40("70"), "Is40(\"70\") = false（未知变体不落进 40）", pass, fail);
		Check(!Is40(null), "Is40(null) = false（注册表没有 GcuVariant）", pass, fail);
		Check(!Is40(""), "Is40(\"\") = false", pass, fail);
		Check(!Is40("   "), "Is40(\"   \") = false", pass, fail);
		Check(!IsFamily("40B", "50") && !IsFamily("50", Forty), "IsFamily：跨家族一律假（不把 40B 当 50）", pass, fail);
		Check(!IsFamily("40B", "WJ") && !IsFamily("WJ", Forty), "IsFamily：40B 与 WJ 跨家族，一律假", pass, fail);
		Check(!IsFamily(null, Forty) && !IsFamily("40B", null) && !IsFamily("40B", ""), "IsFamily：任一侧为空判假", pass, fail);

		// ---- ③ 非 40 变体的家族键原样保留（30 / 50 / 1020 / WJ 不被吞）----
		Check(Resolve("30") == "30" && Resolve("50") == "50" && Resolve("gcu") == "GCU", "Resolve：30 / 50 / gcu 原样成族", pass, fail);
		Check(Resolve("1020") == "1020" && Resolve("wj") == "WJ", "Resolve：1020 / wj 原样成族", pass, fail);

		// ---- ④ 载荷身份**不**被本类合并（40 ≠ 40B，供诊断按变体精确取表）----
		Check(!string.Equals("40", FortyB, StringComparison.OrdinalIgnoreCase), "载荷身份仍分开：\"40\" != \"40B\"（OrdinalIgnoreCase）—— 等价的是行为判断，不是载荷身份", pass, fail);
		Check(Resolve("40B") == Forty && "40B" != Forty, "Resolve 只改家族键，注册表原值 \"40B\" 一字未动", pass, fail);

		// ---- ⑤ 接线核对：MechrevoService 的公开判据与 40 家族助手同源 ----
		Check(MechrevoService.IsGcu40Family("40") && MechrevoService.IsGcu40Family("40B") && MechrevoService.IsGcu40Family("40b"), "MechrevoService.IsGcu40Family：40 / 40B / 40b 全判真（键盘灯 40 系协议分支的判据）", pass, fail);
		Check(!MechrevoService.IsGcu40Family("30") && !MechrevoService.IsGcu40Family("50") && !MechrevoService.IsGcu40Family(null), "MechrevoService.IsGcu40Family：30 / 50 / null 判假", pass, fail);
		Check(!MechrevoService.IsGcu40Family("1020") && !MechrevoService.IsGcu40Family("WJ"), "MechrevoService.IsGcu40Family：1020 / WJ 判假", pass, fail);

		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"GCU 变体家族自测（离线，不读注册表、不发帧）：OK={pass.Count} FAIL={fail.Count}");
		sb.AppendLine("语义：家族键 \"40\" = {\"40\", \"40B\"}；仅用于行为分流，载荷身份仍按注册表原值区分。2026-10-14 起 40B 载荷已退役（本节自测只校验行为归一是否仍在）。");
		foreach (string line in pass)
		{
			sb.AppendLine("[=] " + line);
		}
		foreach (string line in fail)
		{
			sb.AppendLine("[X] " + line);
		}
		return sb.ToString();
	}

	private static void Check(bool ok, string description, List<string> pass, List<string> fail)
	{
		if (ok)
		{
			pass.Add(description);
		}
		else
		{
			fail.Add(description);
		}
	}
}
