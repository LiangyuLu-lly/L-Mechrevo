// ⭐⭐ 2026-10-17（宝龙达模具白名单 · **纯判据** · 零依赖）：
// 从 `BaolongdaBackend.IsBaolongdaModelWhitelisted` 里**抽出来的单一条判据**。
//
// <para>⭐⭐ <b>为什么单独一个文件、且一个依赖都不引</b>：
//   ⭐ 2026-10-17 的真机事故 = ⭐ **这条判据漏了裸 `MRID6`**（蛟龙16 · 6800H＋3070Ti）
//   ⇒ ⭐ 整台机退回 legacy 后端 ⇒ ⭐ 下游一连串「点了没反应」❌。
//   而**开发机是同方 `GM5HG0A`** ⚠️ ⇒ ⭐ 它永远走不到"命中"那一支
//   ⇒ ⭐ 只在开发机上看一眼**根本验不了判据本身** ⚠️。
//   ⇒ ⭐ 所以把判据抽成**无依赖的纯函数**，⭐ 由 `tools\dev\board-whitelist-probe` 直接 link 这个文件做断言 ✅
//     （⭐ 与 `fancurve-selftest` / `ecport-probe` 同一套"link 生产源文件"惯例 ✅）。</para>
//
// <para>⚠️ <b>改这里的唯一纪律</b>：⭐ 只认「**精确** `MRID6`」＋「**连字符前缀** `MRID6-`」，
//   ⛔ 不要图省事改成纯前缀 `MRID6` —— ⭐ 那样 `MRID60` / `MRID6A` 这类假想板号也会被放进来 ⚠️，
//   与本项目「⭐ 宁可不认宝龙达，⭐ 也绝不误伤同方」的原则相悖 ❌。</para>

using System;
using System.Diagnostics;
using System.Management;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 宝龙达模具**板号白名单**（⭐ 单一事实来源 · ⭐ 纯函数 · ⭐ 无依赖 ⇒ 可单测）。
///
/// <para>⭐ <b>接受</b>：<c>MRID6</c>（裸板号 · 2026-10-17 真机：蛟龙16 · 6800H＋3070Ti）·
/// <c>MRID6-23</c>（40 系 · 三台真机证据）· <c>MRID6-*</c>（后续变体，⚠️ 待逐台验证）。</para>
///
/// <para>⛔ <b>不接受</b>：同方全部已知板号
/// （<c>GM5HG0A</c> / <c>GM6IX8B</c> / <c>KUANGSHI-JIGUANG Series-*</c> / <c>YAOSHI Series-*</c> / 空值）
/// · ⚠️ 以及 <c>MRID60</c> / <c>MRID6A</c> 这类「以 <c>MRID6</c> 开头但后面不是连字符」的假想板号。</para>
///
/// <para>⚠️ <b>失败方向</b>：空值 / null ⇒ <c>false</c>（⭐ 按"不是宝龙达"处理 ✅ —— 落回老接口/GCU
/// ⭐ 总比误伤同方好 ✅）。</para>
/// </summary>
internal static class BoardWhitelist
{
	/// <summary>⭐ 白名单判据本体（⭐ 大小写不敏感 · ⭐ 两侧空白先裁掉）。
	/// ⚠️ 参数写成 <c>string</c>（⛔ 不用 <c>string?</c>）—— ⭐ 本工程的 <c>Nullable</c> 是**关**的，
	/// 写 <c>string?</c> 会多一条 `CS8632` 告警 ⚠️（⭐ 本树告警基线是 1455，⭐ 不增 ✅）。</summary>
	internal static bool Matches(string board)
	{
		string text = (board ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return false;
		}
		// ⭐ ① 40 系：⭐ `MRID6-23`（⭐ 三台真机证据 ✅）
		if (string.Equals(text, "MRID6-23", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		// ⭐ ② 30 系：⭐ **裸 `MRID6`**（⭐ 2026-10-17 真机 · 蛟龙16 · 6800H＋3070Ti ✅）
		//   ⚠️ **这条就是漏判修复的核心** —— ⛔ 去掉它立刻回到"整机退回 legacy"的事故状态 ❌
		if (string.Equals(text, "MRID6", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		// ⭐ ③ `MRID6-` 连字符前缀：⭐ 覆盖 `MRID6-XX` 后续变体 ✅
		if (text.StartsWith("MRID6-", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	/// <summary>⭐⭐ 2026-10-19（机主：宝龙达被判成同方）：⭐ 板号取证结果（四来源 + 最终采用值/来源）✅</summary>
	internal readonly record struct BoardProductEvidence(string RegBaseBoard, string WmiBaseBoard, string RegSystemProduct, string WmiModel, string Product, string Source);

	private static BoardProductEvidence _cachedEvidence;

	private static bool _cached;

	private static readonly object _cacheLock = new object();

	/// <summary>⭐ 统一入口：首个**命中白名单**的来源；③④ 仅在 ①② 都读不到时作兜底（且必须含 MRID6）✅</summary>
	internal static BoardProductEvidence GetBoardProductEvidence()
	{
		lock (_cacheLock)
		{
			if (_cached)
			{
				return _cachedEvidence;
			}
			string regBase = ReadRegistryValue("BaseBoardProduct");
			string wmiBase = ReadWmiValue("Win32_BaseBoard", "Product");
			string regSystem = ReadRegistryValue("SystemProductName");
			string wmiModel = ReadWmiValue("Win32_ComputerSystem", "Model");
			string product = string.Empty;
			string source = string.Empty;
			// ①②：按顺序找首个**白名单命中**（这样 reg 是 Standard、WMI 是 MRID6 时也能认对）
			if (Matches(regBase))
			{
				product = regBase;
				source = "reg.BaseBoardProduct";
			}
			else if (Matches(wmiBase))
			{
				product = wmiBase;
				source = "wmi.BaseBoard.Product";
			}
			else if (regBase.Length == 0 && wmiBase.Length == 0)
			{
				// ③④：仅当 ①② 都读不到时才用；且必须含 "MRID6" 才认（⛔ 不误伤同方）✅
				string fallback = regSystem.Length > 0 ? regSystem : wmiModel;
				if (fallback.IndexOf("MRID6", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					product = fallback;
					source = regSystem.Length > 0 ? "reg.SystemProductName" : "wmi.ComputerSystem.Model";
				}
			}
			if (product.Length == 0)
			{
				if (regBase.Length > 0)
				{
					product = regBase;
					source = "reg.BaseBoardProduct";
				}
				else if (wmiBase.Length > 0)
				{
					product = wmiBase;
					source = "wmi.BaseBoard.Product";
				}
			}
			_cachedEvidence = new BoardProductEvidence(regBase, wmiBase, regSystem, wmiModel, product, source);
			_cached = true;
			return _cachedEvidence;
		}
	}

	/// <summary>⭐ 最终采用的板号（空 = 读不到 ⇒ 安全方向按“不是宝龙达”）✅</summary>
	internal static string ReadBoardProduct()
	{
		return GetBoardProductEvidence().Product;
	}

	/// <summary>⭐ 取证日志：四来源 + 命中来源/判定（默认可见，去重）✅</summary>
	internal static void LogEvidence(BoardProductEvidence evidence, bool isBaolongda)
	{
		try
		{
			Logger.WriteLineIfChanged("board-evidence",
				"[机型] 板号取证：reg.BaseBoardProduct=" + Show(evidence.RegBaseBoard)
				+ " · wmi.BaseBoard.Product=" + Show(evidence.WmiBaseBoard)
				+ " · SystemProductName=" + Show(evidence.RegSystemProduct)
				+ " · Model=" + Show(evidence.WmiModel));
			Logger.WriteLineIfChanged("board-decision",
				"[机型] 判定：命中来源=" + (evidence.Source.Length == 0 ? "(无)" : evidence.Source)
				+ "(" + Show(evidence.Product) + ") ⇒ 宝龙达=" + isBaolongda);
		}
		catch
		{
		}
	}

	private static string Show(string value)
	{
		return value.Length == 0 ? "(空)" : "「" + value + "」";
	}

	private static string ReadRegistryValue(string name)
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue(name) as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string ReadWmiValue(string className, string property)
	{
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + className);
			foreach (ManagementObject item in searcher.Get())
			{
				object value = item[property];
				if (value != null)
				{
					return value.ToString().Trim();
				}
			}
		}
		catch
		{
			// WMI 不可用（服务损坏/权限）⇒ 空值 ⇒ 安全方向
		}
		return string.Empty;
	}
}
