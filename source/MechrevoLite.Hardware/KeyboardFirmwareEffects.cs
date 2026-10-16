using System;
using MechrevoLite.Properties;
namespace MechrevoLite.Hardware;

/// <summary>
/// 键盘灯效表 + 键盘类型判据。
/// 2026-10-09（按作者 0.290.6 对齐单键 RGB）：判据改为作者的 ordinal→名称→kind 模型，
/// 只移植「单键 RGB 必需」的那一部分 —— Keymap 帧层、rgb.cfg、主题名不动；整套
/// LightingChannelSet/LightingEvidence/HID 接口分类（LightingChannelDetector 的其余部分）不移植
/// （我们分支没有这套模型，风险大且与本轮三处修复无关）。
/// 作者原文（0.290.6 LightingChannelDetector.cs）：
///   RgbkbTypeOrdinals[29] = Normal, SingleZone, FourZone, FourZoneSingleColor, MEZone_1st,
///     MEZone_2nd_101, MEZone_2nd_102, MEZone_3p1nd_101..104, MEZone_2p1nd_85..88, MEZone_2p2nd,
///     MEZone_Lighbar, MEZone_2p2nd_97..100, MEZone_3nd_98/99, MEZone_Lighbar2..4,
///     MEZone_Lighbar4_logo, MEZone_Lighbar4_hinge, MEZone_Lighbar4_sync
///   KindFromTypeName：PerKeyTypes(16 个 MEZone_*) ⇒ PerKey；FOURZONE ⇒ FourZone；
///     FOURZONESINGLECOLOR ⇒ FourZoneSingleColor；SINGLEZONE ⇒ SingleZone；MEZONE_1ST ⇒ PerKeyLegacy；
///     其余（含 ordinal 越界 / 空） ⇒ null ⇒ 作者兜底 <c>?? KeyboardLightKind.PerKey</c>。
/// 作者灯效表（LightingEffectCatalog）：PerKey 11 / FourZone 6 / FourZoneSingleColor 1 / SingleZone 2。
/// ⚠️ 我们的 SingleZone 仍是 4 项（Single/Breathing/Rainbow/Manual，含我们实机验证过的 Manual 与呼吸档）
/// —— 这是我们的分支特性，回退到作者的 2 项会让本机（KeyboardType=1）UI 缺 Breathing/Manual，故保留（见报告）。
/// </summary>
internal static class KeyboardFirmwareEffects
{
	internal static readonly (string Id, string Label)[] All = new (string, string)[11]
	{
		("Single", Strings.EffectSingle),
		("Breathing", Strings.EffectBreathing),
		("Wave", Strings.EffectWave),
		("Reactive", Strings.EffectReactive),
		("Rainbow", Strings.EffectRainbow),
		("Ripple", Strings.EffectRipple),
		("Raindrop", Strings.EffectRaindrop),
		("Marquee", Strings.EffectMarquee),
		("Spark", Strings.EffectSpark),
		("Aurora", Strings.EffectAurora),
		("Gaming", Strings.EffectGaming)
	};

	/// <summary>单分区：我们保留 4 项（作者 2 项：Single/Rainbow）。</summary>
	internal static readonly (string Id, string Label)[] SingleZone = new (string, string)[4]
	{
		("Single", Strings.EffectSingle),
		("Breathing", Strings.EffectBreathing),
		("Rainbow", Strings.EffectRainbow),
		("Manual", Strings.EffectManual)
	};

	/// <summary>四分区（ordinal=2）：与作者 FourZoneKeyboard() 逐项同名同序（Single/Breathing/Wave/Rainbow/Mix/Flash）。</summary>
	internal static readonly (string Id, string Label)[] FourZone = new (string, string)[6]
	{
		("Single", Strings.EffectSingle),
		("Breathing", Strings.EffectBreathing),
		("Wave", Strings.EffectWave),
		("Rainbow", Strings.EffectRainbow),
		("Mix", Strings.EffectMix),
		("Flash", Strings.LightEffectFlash)
	};

	/// <summary>四分区单色（ordinal=3）：与作者 FourZoneSingleColorKeyboard() 一致，只有 Single。</summary>
	internal static readonly (string Id, string Label)[] FourZoneSingleColor = new (string, string)[1]
	{
		("Single", Strings.EffectSingle)
	};

	/// <summary>作者 0.290.6 RgbkbTypeOrdinals：ordinal → 名称（0-based）。</summary>
	private static readonly string[] RgbkbTypeOrdinals = new string[29]
	{
		"Normal", "SingleZone", "FourZone", "FourZoneSingleColor", "MEZone_1st", "MEZone_2nd_101", "MEZone_2nd_102", "MEZone_3p1nd_101", "MEZone_3p1nd_102", "MEZone_3p1nd_103",
		"MEZone_3p1nd_104", "MEZone_2p1nd_85", "MEZone_2p1nd_86", "MEZone_2p1nd_87", "MEZone_2p1nd_88", "MEZone_2p2nd", "MEZone_Lighbar", "MEZone_2p2nd_97", "MEZone_2p2nd_98", "MEZone_2p2nd_99",
		"MEZone_2p2nd_100", "MEZone_3nd_98", "MEZone_3nd_99", "MEZone_Lighbar2", "MEZone_Lighbar3", "MEZone_Lighbar4", "MEZone_Lighbar4_logo", "MEZone_Lighbar4_hinge", "MEZone_Lighbar4_sync"
	};

	/// <summary>作者 0.290.6 的 PerKeyTypes（16 个 MEZone_*，逐字照抄）。</summary>
	private static readonly string[] PerKeyTypes = new string[16]
	{
		"MEZone_2nd_101", "MEZone_2nd_102", "MEZone_2p1nd_85", "MEZone_2p1nd_86", "MEZone_2p1nd_87", "MEZone_2p1nd_88", "MEZone_2p2nd_97", "MEZone_2p2nd_98", "MEZone_2p2nd_99", "MEZone_2p2nd_100",
		"MEZone_3nd_98", "MEZone_3nd_99", "MEZone_3p1nd_101", "MEZone_3p1nd_102", "MEZone_3p1nd_103", "MEZone_3p1nd_104"
	};

	private enum KeyboardKind
	{
		PerKey,
		SingleZone,
		FourZone,
		FourZoneSingleColor
	}

	/// <summary>
	/// 2026-10-16（0.291.8.1 · 修「耀世16Super 2024 logo 灯/灯带效果错乱」）：
	/// ⭐ **这个灯效该不该带"用户选的那个颜色"** —— 决定下发给灯带/Logo 时要 1 段用户色，还是交给固件自己变色。
	/// <para>背景（真机日志逐字证据）：三处调用点原来一律写 <c>(effect == "Single") ? 颜色 : null</c>，
	/// 于是除 Single 外**都不带颜色** ⇒ <c>SetLightEffect</c> 落到"无颜色"分支 ⇒ 发**硬编码七彩**
	/// （红/橙/黄/绿/青/蓝/品红）⇒ 硬件取第 1 段 ⇒ 用户看到的是**红色**（呼吸）；
	/// 「混合(Mix)」发同一份七彩 ⇒ 硬件干脆**把 logo 关掉**。</para>
	/// <para>⭐⭐ 2026-10-16 <b>按厂商去混淆反编译校正</b>（出处见
	/// <c>docs\notes\GCU灯效协议-按反编译校正.md</c>，源：<c>HIDLightbar4_logo.cs</c> 的 <c>RunEffct()</c>）：
	/// 厂商对 logo 通道是**逐效果**设 <c>isCircular</c>（该字段决定下发给 HID 的颜色模式字节 <c>b2</c>：
	/// <c>false ⇒ b2=1</c> 单色（且**只取 ColorBuffer[0]**）、<c>true ⇒ b2=8</c> 多段循环）：
	/// <list type="bullet">
	/// <item><c>1 STATIC(Single)</c> ⇒ <c>isCircular=false</c> ⇒ 单色</item>
	/// <item><c>2 BREATHING</c> ⇒ <c>isCircular=<b>true</b></c> ⇒ 多段循环</item>
	/// <item><c>4 REACTIVE</c> / <c>13 IMPACT</c> / <c>5 RAINBOW</c> / <c>33 THINKING</c> ⇒ 保持默认 <c>false</c></item>
	/// <item><c>6 RIPPLE</c> / <c>10 RAINDROP</c> ⇒ 显式 <c>false</c></item>
	/// <item>⭐ <c>19 MIX</c> ⇒ **不在任何分支** ⇒ 保持默认 <c>false</c> ⇒ ⭐ **单色**</item>
	/// </list></para>
	/// <para>⇒ 本判据的含义收窄为：**"我们要不要主动把用户颜色塞进 ColorBuffer[0]"**。
	/// ⭐ 对<b>单色系</b>（含 Mix）⇒ 要（发 1 段用户色 ✅，这正是厂商 <c>b2=1</c> 只吃第 0 段的行为）；
	/// ⭐ 对<b>固件自带配色</b>的（Rainbow / Aurora / Gaming —— 厂商让它们自己变色）⇒ 不主动塞，
	/// 保持既有行为（避免我们用单一颜色去覆盖固件的彩虹/极光效果）。</para>
	/// <para>⚠️ <c>Breathing</c> 厂商走的是 <c>isCircular=true</c>（多段循环）—— 我们这一版**仍按单色**处理
	/// （只给 1 段用户色 ⇒ 硬件用该段循环），因为"用用户颜色呼吸"才是用户要的效果；
	/// ⭐ 真正的多段循环留待下一轮按 §6 待办逐效果核对后再补。</para>
	/// <para>⚠️ 纯判据，⛔ 不读配置、不发帧、不碰控件。</para>
	/// </summary>
	internal static bool IsSingleColorEffect(string? effect)
	{
		if (string.IsNullOrWhiteSpace(effect))
		{
			return false;
		}
		return effect switch
		{
			// ⭐ 固件自带配色：不主动塞单一颜色（厂商对它们也是 isCircular=false，颜色交给固件）
			"Rainbow" => false,
			"Aurora" => false,
			"Gaming" => false,
			_ => true
		};
	}

	internal static bool Contains(string id)
	{
		if (Exists(All, id) || Exists(SingleZone, id))
		{
			return true;
		}
		return Exists(FourZone, id) || Exists(FourZoneSingleColor, id);
	}

	/// <summary>
	/// 判据（作者口径，见类注释）：ordinal=1(SingleZone) 才是单分区；2=FourZone、3=FourZoneSingleColor
	/// 都不是；越界/不认识 ⇒ 兜底 PerKey。开关 LightCompatibilitySwitches.KeyboardKindModelEnabled
	/// 置 0 时回到 0.291.2 旧判据（KeyboardType∈{1,2} 都算单分区）。
	/// </summary>
	internal static bool IsSingleZoneRgb(int keyboardType)
	{
		if (!LightCompatibilitySwitches.KeyboardKindModelEnabled)
		{
			return (uint)(keyboardType - 1) <= 1u;
		}
		return Classify(keyboardType) == KeyboardKind.SingleZone;
	}

	/// <summary>四分区（ordinal=2）：走它自己的分支与 6 项灯效表，不再被当单分区。</summary>
	internal static bool IsFourZoneRgb(int keyboardType)
	{
		if (!LightCompatibilitySwitches.KeyboardKindModelEnabled)
		{
			return false;
		}
		return Classify(keyboardType) == KeyboardKind.FourZone;
	}

	/// <summary>
	/// HID/GCU 通道归属（⚠️ 与灯效表判据无关）：保持 0.291.2 既有行为 —— KeyboardType∈{1,2}
	/// 一律硬判走 GCU/MQTT（RgbForm.IsGcuKeyboardFallback / SettingsForm.ShouldUseGcuKeyboardFallback /
	/// Program 50 系单分区恢复共用）。作者的通道归属只看 KeyboardLightPathPolicy，我们这里刻意不跟，
	/// 以免 4 分区机型在改动后被切到 per-key HID 帧流（回归风险）。
	/// </summary>
	internal static bool IsGcuZoneKeyboardPath(int keyboardType)
	{
		return (uint)(keyboardType - 1) <= 1u;
	}

	internal static (string Id, string Label)[] Visible(int keyboardType)
	{
		if (!LightCompatibilitySwitches.KeyboardKindModelEnabled)
		{
			return ((uint)(keyboardType - 1) <= 1u) ? SingleZone : All;
		}
		return Classify(keyboardType) switch
		{
			KeyboardKind.SingleZone => SingleZone,
			KeyboardKind.FourZone => FourZone,
			KeyboardKind.FourZoneSingleColor => FourZoneSingleColor,
			_ => All
		};
	}

	/// <summary>作者 0.290.6 KindFromTypeName 的键盘部分（作者对 PerKeyLegacy 也是 All(11) 项，故与 PerKey 等价）。</summary>
	private static KeyboardKind Classify(int keyboardType)
	{
		string name = (keyboardType >= 0 && keyboardType < RgbkbTypeOrdinals.Length) ? RgbkbTypeOrdinals[keyboardType] : null;
		if (string.IsNullOrWhiteSpace(name))
		{
			return KeyboardKind.PerKey;
		}
		if (Array.Exists(PerKeyTypes, (string t) => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
		{
			return KeyboardKind.PerKey;
		}
		return name.ToUpperInvariant() switch
		{
			"SINGLEZONE" => KeyboardKind.SingleZone,
			"FOURZONE" => KeyboardKind.FourZone,
			"FOURZONESINGLECOLOR" => KeyboardKind.FourZoneSingleColor,
			_ => KeyboardKind.PerKey
		};
	}

	private static bool Exists((string Id, string Label)[] table, string id)
	{
		return Array.Exists(table, ((string Id, string Label) e) => string.Equals(e.Id, id, StringComparison.Ordinal));
	}
}
