using System;
using MechrevoLite.Helpers;

namespace MechrevoLite.Gpu;

/// <summary>
/// 2026-10-09（igpugate）：「核显模式显示」的三态设置 —— 键名沿用 <c>igpu_only_ui</c>，值域扩展为
/// <c>auto</c> / <c>show</c> / <c>hide</c>（用户批准："显式 false 压过用户设置可用，但希望后续还是可以手动开"）：
///   * <c>auto</c>（新默认）—— 由**硬件判据**决定（<see cref="MechrevoLite.Hardware.MechrevoHw.IgpuOnlyHardwareVerdict"/>）：
///     能力位明确 true ⇒ 显示；明确 false ⇒ 隐藏；读不到（null）⇒ 退回旧行为，不得把能用的机器藏掉；
///   * <c>show</c> —— ⭐ **强制显示**（用户要的"判断出错时手动开回来"的后路）；
///   * <c>hide</c> —— 强制隐藏（老值，语义不变）。
///
/// 向后兼容与迁移：老装机只有 show / hide 两个值，其中 <c>show</c> 曾是**放行值**
/// （含首次引导的 fail-safe「不确定 → show」），它**不等于**"强制显示"。因此本类在首次读取时
/// 做一次迁移：<c>show → auto</c>（同时把标记键 <see cref="MarkerKey"/> 置 1），<c>hide</c> 保持不变。
/// 迁移标记缺失期间（老装机 / 审计模式）读到 <c>show</c> 也一律按 <c>auto</c> 解读 ——
/// 这样即使迁移没落盘，也不会退回"旧 show = 无条件放行"的旧行为。
///
/// 边界：本类只读写 config.json（在 <see cref="Set"/> 里写标记），不发任何显卡指令、不碰注册表 / NVRAM。
/// </summary>
internal static class IgpuOnlyUiMode
{
	/// <summary>设置键（沿用旧键名，值域扩展 —— 用户要求"键名可沿用"）。</summary>
	internal const string Key = "igpu_only_ui";

	/// <summary>迁移标记：1 = 这台机器上的 igpu_only_ui 已是新值域（此后 show 就是"强制显示"）。</summary>
	internal const string MarkerKey = "igpu_only_ui_v2";

	internal const string Auto = "auto";

	internal const string Show = "show";

	internal const string Hide = "hide";

	private static bool _legacyChecked;

	/// <summary>当前口径（已含老值迁移解读）：只会返回 <c>auto</c> / <c>show</c> / <c>hide</c>。</summary>
	internal static string Current()
	{
		EnsureLegacyMigrated();
		return Normalize(AppConfig.GetString(Key) ?? string.Empty);
	}

	internal static bool IsForcedShow => string.Equals(Current(), Show, StringComparison.OrdinalIgnoreCase);

	internal static bool IsForcedHide => string.Equals(Current(), Hide, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 用户在本项上做了明确选择（或引导落盘）—— 写值并置迁移标记。
	/// 直接用 <see cref="AppConfig.Set(string, string)"/> 写 show 会被 <see cref="Normalize"/> 当成老值，
	/// 所以本项的一切写入都必须走这里。
	/// </summary>
	internal static void Set(string mode)
	{
		AppConfig.Set(Key, NormalizeForWrite(mode));
		AppConfig.Set(MarkerKey, 1);
		AppConfig.Flush();
	}

	private static string NormalizeForWrite(string mode)
	{
		if (string.Equals(mode, Show, StringComparison.OrdinalIgnoreCase))
		{
			return Show;
		}
		if (string.Equals(mode, Hide, StringComparison.OrdinalIgnoreCase))
		{
			return Hide;
		}
		return Auto;
	}

	private static string Normalize(string raw)
	{
		string value = (raw ?? string.Empty).Trim();
		if (string.Equals(value, Hide, StringComparison.OrdinalIgnoreCase))
		{
			return Hide;
		}
		if (string.Equals(value, Auto, StringComparison.OrdinalIgnoreCase))
		{
			return Auto;
		}
		if (string.Equals(value, Show, StringComparison.OrdinalIgnoreCase))
		{
			// 标记已落盘 ⇒ 新值域，show 就是"强制显示"；标记缺失 ⇒ 老装机，show 是放行值 ⇒ 按 auto 解读。
			return (AppConfig.Get(MarkerKey, 0) == 1) ? Show : Auto;
		}
		// 空值 / 未知值（老装机只可能是 show/hide）：一律 auto（新默认，交给硬件判据）。
		return Auto;
	}

	/// <summary>
	/// 一次性迁移：老装机的 <c>show</c> → <c>auto</c>，<c>hide</c> 不动，并置标记。
	/// 只做一次（进程内静态位），写失败也不影响读取口径（<see cref="Normalize"/> 兜底）。
	/// 审计 / 截图模式（UiAuditRunner）不写配置，只按新口径读。
	/// </summary>
	private static void EnsureLegacyMigrated()
	{
		if (_legacyChecked)
		{
			return;
		}
		_legacyChecked = true;
		if (AppConfig.Get(MarkerKey, 0) == 1 || Program.UiAuditMode)
		{
			return;
		}
		string raw = AppConfig.GetString(Key) ?? string.Empty;
		bool migrated = string.Equals(raw.Trim(), Show, StringComparison.OrdinalIgnoreCase);
		if (migrated)
		{
			AppConfig.Set(Key, Auto);
		}
		AppConfig.Set(MarkerKey, 1);
		AppConfig.Flush();
		if (migrated)
		{
			Logger.WriteLine($"[igpu-ui] igpu_only_ui 迁移：show -> auto（机型={AppConfig.GetModel()}；旧的 show 是「放行值」，auto 才按硬件判据；hide 保持不变）");
		}
	}
}
