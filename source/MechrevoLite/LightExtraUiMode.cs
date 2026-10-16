using System;
using System.Threading;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;

namespace MechrevoLite;

/// <summary>
/// ⚠️ 2026-10-09（lightsplit，用户原话「额外灯光项应该细分」）：本类是**旧键** <c>light_extra_ui</c> 的解读器，
/// 现在只剩两个用途：
///   ① **一次性迁移的数据源**（见 <see cref="LightExtraRows.MigrateLegacyOnce"/>）；
///   ② 迁移尚未跑过时的兜底读取（两个新键都缺失时，UI/判据仍按旧值走，不会出现"没读到"的空窗）。
/// 新口径已经把**一个三态控件**拆成**按行两个**：<see cref="LightStripUiMode"/>（灯带行）与
/// <see cref="LightLogoUiMode"/>（Logo 灯行）—— 旧的三态加"hide 只收灯带行"的改义凑法（一个控件表达不了
/// "隐灯带、留 Logo"）到此为止。
/// 值域仍是 <c>auto</c> / <c>show</c> / <c>hide</c>；空值 / 未知值一律 <c>auto</c>。
/// 只读 config.json 里这一个键；不写硬件、不发指令。
/// </summary>
internal static class LightExtraUiMode
{
	/// <summary>旧键（只读，保留在用户 config 里不删）。</summary>
	internal const string Key = "light_extra_ui";

	internal const string Auto = "auto";

	internal const string Show = "show";

	internal const string Hide = "hide";

	/// <summary>当前旧键取值：只会返回 <c>auto</c> / <c>show</c> / <c>hide</c>；空值 / 未知值一律 <c>auto</c>。</summary>
	internal static string Current()
	{
		return Normalize(AppConfig.GetString(Key) ?? string.Empty);
	}

	/// <summary>旧键是否**在 config 里存在**（用于迁移日志区分"没这个键"与"值是 auto"）。</summary>
	internal static bool IsPresent => AppConfig.Exists(Key);

	internal static bool IsForcedShow => string.Equals(Current(), Show, StringComparison.OrdinalIgnoreCase);

	internal static bool IsForcedHide => string.Equals(Current(), Hide, StringComparison.OrdinalIgnoreCase);

	/// <summary>规范化（迁移时复用）：<c>show</c> / <c>hide</c> 之外的任何值都算 <c>auto</c>。</summary>
	internal static string NormalizeForWrite(string mode)
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
		return NormalizeForWrite((raw ?? string.Empty).Trim());
	}
}

/// <summary>
/// 2026-10-09（lightsplit）：**灯带行**（<c>HidLightbar/Ctrl</c> 那一条逻辑行）的三态设置 ——
/// 键 <c>light_strip_ui</c>，值域 <c>auto</c> / <c>show</c> / <c>hide</c>（默认 <c>auto</c>）。
/// 与 <see cref="LightLogoUiMode"/> 是**同构的两把键**（做法照抄 <c>Gpu\IgpuOnlyUiMode.cs</c>），
/// 每个键只管自己那一行 ⇒ "隐灯带、留 Logo"（耀世 16Pro 2024）与"两行都藏"都重新可达。
///   * <c>auto</c> —— 该行的自动判据：灯带行见 <see cref="LightExtraRows.BaseRowPhantom"/>（证据不足一律留）；
///   * <c>show</c> —— **强制显示**这一行（判据判错时的后路）；
///   * <c>hide</c> —— **强制隐藏**这一行。
/// 只读写 config.json 里这一个键；不改灯光下发 / 主题 / 去重，不写 EC / NVRAM，不碰 payload。
/// </summary>
internal static class LightStripUiMode
{
	internal const string Key = "light_strip_ui";

	internal const string Auto = "auto";

	internal const string Show = "show";

	internal const string Hide = "hide";

	internal static string Current()
	{
		return LightRowUiValue.Normalize(AppConfig.GetString(Key) ?? string.Empty);
	}

	internal static void Set(string mode)
	{
		AppConfig.Set(Key, LightRowUiValue.Normalize(mode));
		AppConfig.Flush();
	}
}

/// <summary>
/// 2026-10-09（lightsplit）：**Logo 灯行**（<c>HidLightbar_Logo/Ctrl</c> 那一条逻辑行）的三态设置 ——
/// 键 <c>light_logo_ui</c>，值域与语义与 <see cref="LightStripUiMode"/> 完全同构，
/// 只是自动判据换成 <see cref="LightExtraRows.LogoRowSupported"/>（拿不到任何 Logo 灯证据才算"该收"，
/// 拿不准一律留）。两个键互不影响 ⇒ 旧口径里"一个三态控件同时管两行"表达不了的组合现在都能表达。
/// 只读写 config.json 里这一个键；不改灯光下发 / 主题 / 去重，不写 EC / NVRAM，不碰 payload。
/// </summary>
internal static class LightLogoUiMode
{
	internal const string Key = "light_logo_ui";

	internal const string Auto = "auto";

	internal const string Show = "show";

	internal const string Hide = "hide";

	internal static string Current()
	{
		return LightRowUiValue.Normalize(AppConfig.GetString(Key) ?? string.Empty);
	}

	internal static void Set(string mode)
	{
		AppConfig.Set(Key, LightRowUiValue.Normalize(mode));
		AppConfig.Flush();
	}
}

/// <summary>2026-10-09（lightsplit）：两个按行三态键共用的取值规范化（一处出处，避免两把键判法漂移）。</summary>
internal static class LightRowUiValue
{
	/// <summary>规范化为 <c>auto</c> / <c>show</c> / <c>hide</c>（空 / 未知 / 大小写混写都收敛）。</summary>
	internal static string Normalize(string? raw)
	{
		string value = (raw ?? string.Empty).Trim();
		if (string.Equals(value, LightStripUiMode.Hide, StringComparison.OrdinalIgnoreCase))
		{
			return LightStripUiMode.Hide;
		}
		if (string.Equals(value, LightStripUiMode.Show, StringComparison.OrdinalIgnoreCase))
		{
			return LightStripUiMode.Show;
		}
		return LightStripUiMode.Auto;
	}

	internal static bool IsShow(string? raw)
	{
		return string.Equals(Normalize(raw), LightStripUiMode.Show, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsHide(string? raw)
	{
		return string.Equals(Normalize(raw), LightStripUiMode.Hide, StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// 2026-10-09（lightfix2 / lightsplit）：灯光区「灯带行 / Logo 灯行」**逐行**该不该显示。
/// ⚠️ 方向（lightfix2 起）：耀世 16Pro 2024（<c>GM6IX0B</c> / project <c>IDB</c> / <c>GcuVariant=40</c>）只有
/// 键盘灯 + **Logo 灯**、**没有灯带**：ItemSupport 逐字 <c>KeyboardSupport=1 LightbarSupport=1
/// RGBLightbarSupport=1 KeyboardType=1</c>（**无任何 Logo/MBLogo/Newlogo 键**），GCU Status 报
/// <c>LightbarBaseSupport=否</c> / <c>LightbarNewLogoSupport=是</c> / <c>LightbarMbLogoSupport</c> 未收到，
/// 且解析出的能力行是 <c>lightbar=Y logo=Y (reported logo=n base=n newLogo=Y mbLogo=-)</c>
/// ⇒ 真判据是"**灯带行**是幻影行"。
/// ⚠️ 方向（lightsplit 起，用户原话「额外灯光项应该细分」）：收行不再是**一个**三态键的事 ——
/// 灯带行看 <see cref="LightStripUiMode"/>、Logo 行看 <see cref="LightLogoUiMode"/>，两条判据各管各的行。
/// 两条纯函数各判一行，语义与排除项都按新方向重排：
///   * <see cref="BaseRowPhantom"/> —— 灯带行是不是幻影行：须"GCU 明确报 <c>BaseSupport=否</c>"**且**
///     "报过 Logo 版本（<c>NewLogoSupport=是</c> 或 <c>LogoSupport=是</c>）"**且**"没有 <c>MbLogoSupport=是</c>"
///     三条同时成立。旧的排除项 <c>gcu-mblogo-support</c> **保留**，改作"两行都真实"的**保护闸**
///     （mbLogo=是 ⇒ 永不判幻影；耀世 5090 / 苍龙 / 5070Ti+ 走这条 ⇒ 两行都留）。
///   * <see cref="LogoRowSupported"/> —— Logo 行是不是真的（该保留）：<c>gcu-logo-support</c> /
///     <c>gcu-newlogo-support</c> / <c>cap-logo-light</c> 是正向信号；
///     <c>fingerprint-diverged-ever</c> **只用于禁止隐藏 Logo 行**（latched，进程内失忆）；
///     "两主题指纹逐字相同"只是可选加强条件（指纹都在且**不同** ⇒ 额外判 Logo 行为真）；
///     灯带行非幻影 ⇒ 一律不碰 Logo 行（保护闸，零回归）。
///   * 全"未收到"（各字段皆 null）⇒ 两条判据都为 false ⇒ 保持现状 = 两行都留（零回归）。
/// 只读 MechrevoHw 已有字段（<c>LightbarBaseSupport</c> / <c>LightbarMbLogoSupport</c> /
/// <c>LightbarNewLogoSupport</c> / <c>LightbarLogoSupport</c> / 两个 Status 指纹 / <c>Capabilities.LogoLight</c> /
/// <c>LogoLightStatusSeen</c> / <c>LightbarStatusDivergedEver</c>）—— **零新管道**；本类除一次性迁移外不写 config、
/// 不写注册表 / EC，也不发任何指令。
/// </summary>
internal static class LightExtraRows
{
	internal const string LightbarTopic = "HidLightbar/Status";

	internal const string LogoTopic = "HidLightbar_Logo/Status";

	/// <summary>两个新键落地后写下的"迁移已做过"标记（按机型作用域，见 ModelScopedConfig）。</summary>
	internal const string MigrationMarkerKey = "light_extra_ui_v2";

	/// <summary>空指纹护栏（五个字段全缺失），与灯效去重同一口径 —— 全空不算"同值"证据，也不算"分歧"证据。</summary>
	private const string EmptyFingerprint = "type=-|powerStatus=-|power=|light=-|brightNess=-";

	private static int _legacyMigrationRan;

	/// <summary>
	/// **灯带行是幻影行**吗（= 本机没有独立的灯带，那条 <c>HidLightbar/Ctrl</c> 行是同一块 Logo 灯/键盘灯的
	/// 重复投影）。返回 true 才允许收掉**灯带行**；返回 false 时 <paramref name="evidence"/> 说明是哪条
	/// 保护闸 / 缺哪条证据（供日志与代入用）。
	/// 判据（三条同时成立）：<c>BaseSupport == false</c>（GCU 明确报否，**未收到不算否**）
	/// 且 <c>MbLogoSupport != true</c>（保护闸：分体 MBLogo = 两行都真实）
	/// 且（<c>NewLogoSupport == true</c> 或 <c>LogoSupport == true</c>，即明确报过 Logo 版本）。
	/// </summary>
	internal static bool BaseRowPhantom(MechrevoHw? hw, out string evidence)
	{
		evidence = "hw-null-keep";
		if (hw == null)
		{
			return false;
		}
		if (hw.LightbarMbLogoSupport == true)
		{
			evidence = "mblogo-real-keep-both";
			return false;
		}
		if (hw.LightbarBaseSupport == true)
		{
			evidence = "base-real-keep-both";
			return false;
		}
		if (hw.LightbarNewLogoSupport == true || hw.LightbarLogoSupport == true)
		{
			evidence = "base-denied-logo-only";
			return true;
		}
		evidence = "no-evidence-keep";
		return false;
	}

	/// <summary>
	/// **Logo 行是真的**吗（= 应当保留）。任一正向信号成立即 true；全部不成立且灯带行本身是幻影行
	/// （即"这台机器只有灯带那一条投影、没有 Logo 灯"）才 false。返回 false 意味着连 Logo 灯证据都没有，
	/// 此时 <c>auto</c>/<c>hide</c> 都可以把它收掉。
	/// </summary>
	internal static bool LogoRowSupported(MechrevoHw? hw, out string evidence)
	{
		if (hw == null)
		{
			evidence = "hw-null-keep";
			return true;
		}
		// ① GCU / ItemSupport 报 Logo 支持（含 New logo / MB logo 别名）。
		if (hw.LightbarMbLogoSupport == true)
		{
			evidence = "gcu-mblogo-support";
			return true;
		}
		if (hw.LightbarLogoSupport == true)
		{
			evidence = "gcu-logo-support";
			return true;
		}
		if (hw.LightbarNewLogoSupport == true)
		{
			evidence = "gcu-newlogo-support";
			return true;
		}
		if (hw.Capabilities.LogoLight)
		{
			evidence = "cap-logo-light";
			return true;
		}
		// ② 收到过 HidLightbar_Logo/Status 的**实值**报文（HasLogoLightEvidence 口径）。
		if (hw.LogoLightStatusSeen)
		{
			evidence = "logo-status-seen";
			return true;
		}
		// ③ ⚠️ 只用于**禁止隐藏 Logo 行**：两主题指纹曾经分开变化过（sticky 观测）。
		if (hw.LightbarStatusDivergedEver)
		{
			evidence = "fingerprint-diverged-ever";
			return true;
		}
		// ④ 可选加强条件（已从必要条件降级）：两个指纹都在且**不同** ⇒ Logo 行是独立的第二条。
		string? lightbar = hw.LightbarStatusFingerprint(LightbarTopic);
		string? logo = hw.LightbarStatusFingerprint(LogoTopic);
		if (!string.IsNullOrEmpty(lightbar) && !string.IsNullOrEmpty(logo)
			&& !string.Equals(lightbar, logo, StringComparison.Ordinal)
			&& !string.Equals(lightbar, EmptyFingerprint, StringComparison.Ordinal))
		{
			evidence = "fingerprint-differs";
			return true;
		}
		// ⑤ 保护闸：灯带行本身不是幻影行 ⇒ 这台机器有真实灯带，不碰 Logo 行（零回归）。
		if (!BaseRowPhantom(hw, out _))
		{
			evidence = "base-row-real";
			return true;
		}
		// ⚠️ 走到这里在当前证据集下**不可达**（按设计）：能让灯带行成为幻影行的两个信号
		// （NewLogoSupport / LogoSupport = 是）本身就是上面的正向信号 ⇒ auto 永远不会收掉 Logo 行；
		// 只有用户显式把 light_logo_ui 设成 hide 才收。
		evidence = "logo-phantom";
		return false;
	}

	/// <summary>
	/// 灯光组最终可见性：返回 (灯带行是否显示, Logo 行是否显示)。**按行**决策（每行一把自己的键）：
	///   * 该行三态 = <c>show</c> ⇒ **强制显示这一行**（★ 2026-10-09 lightspeed 修正：原口径是"强制留着这一行"，
	///     即 `该行原可见性 && ...`，对**能力位本身判否**的机型（本机翼龙15Pro 画像「灯条:否」）
	///     点「强制显示」= 把已经为 false 的原可见性再算一遍 ⇒ 界面毫无变化，用户报「点了没反应」）；
	///     现在与「核显模式显示」三选行的「强制显示」同义：**不看判据，把这一行开回来**；
	///   * 该行三态 = <c>hide</c> ⇒ 强制收掉这一行；
	///   * 该行三态 = <c>auto</c> ⇒ 灯带行 = 原可见性 &amp;&amp; !<see cref="BaseRowPhantom"/>；
	///                           Logo 行 = 原可见性 &amp;&amp; <see cref="LogoRowSupported"/>（拿不准一律留）。
	/// ⚠️ 只决定这两行的 Visible 与随之而来的通道数，不改任何下发。
	/// </summary>
	internal static (bool Lightbar, bool Logo) Resolve(MechrevoHw? hw, bool lightbarRow, bool logoRow, string stripMode, string logoMode, out string reason)
	{
		bool phantom = BaseRowPhantom(hw, out string baseEvidence);
		bool logoReal = LogoRowSupported(hw, out string logoEvidence);
		bool lightbarOut;
		string stripReason;
		if (LightRowUiValue.IsShow(stripMode))
		{
			// 2026-10-09（lightspeed，用户原话「灯带按钮点了没反应」）：⚠️ 这里原来写的是
			// `lightbarOut = lightbarRow`（"强制留着这一行"），于是**能力位本身就判否**的机型
			//（caps.Lightbar / caps.RgbLightbar / hw.SupportsLightbar 全为假 ⇒ 传入的 lightbarRow 已经是 false）
			// 上点「强制显示」= 把 false 原样再算一遍 ⇒ 选中态与 config 都变了，灯光组一行不动
			//（本机实测就是这条：config 已是 light_strip_ui=show，灯光组仍只有「键盘」一行）。
			// 语义修正为与「核显模式显示」那行完全一致：**强制显示 = 不看判据，把这一行开回来**
			//（它承载的就是"判据判错时的后路"；用户显式表态优先于能力位推断）。
			// ⚠️ 仅影响该行的 Visible 与灯光区通道数（见 SettingsForm.RefreshDeviceCapabilities 的 flag5/flag6），
			// 不碰任何下发：点击灯带行里的开关仍要过 MechrevoService.SupportsLightTopic / hw.SupportsLightbar 那道门，
			// 本机没有灯带的机型上开关会被硬件侧拒绝并回弹，绝不会凭空向不存在的通道发帧。
			// ⚠️ 2026-10-16（baolongda 死控件闸）：宝龙达后端上 **HidLightbar/Ctrl 这条主题没有任何下发通路** ——
			//   * MechrevoHw.TrySetLightPowerViaBackend 对「非 Keyboard、非 Logo」的主题一律 return false
			//     ⇒ 交回原路 ⇒ 原路是 MechrevoService.PublishLightPower（MQTT）⇒ 宝龙达没有 GCU ⇒ 必然失败；
			//   * 且宝龙达的 Caps.Lightbar / Caps.RgbLightbar 恒为 false（见 BaolongdaBackend.BuildCapabilities 的
			//     「只开 Keyboard，不开 Lightbar / RgbLightbar」），LightbarStatusSeen 只由 GCU Status 赋真
			//     （MechrevoHw.OnLightbarOrLogoLightStatus）⇒ auto 判据本就把它收起。
			//   ⇒ 此时「强制显示」只可能产出一个点了没反应的死控件：SupportsLightTopic("HidLightbar/Ctrl")
			//     走 "lightbar" => _hw.SupportsLightbar = false ⇒ 开关被拒并回弹 ⇒ 违反「能点的就必须有反应」。
			//   ⇒ 因此宝龙达上**忽略 force-show**，退回 auto 判据（lightbarRow && !phantom）。
			//   ⚠️ 语义范围：只动「宝龙达 + 灯带行 + 用户显式 show」这一个组合；非宝龙达（GCU / 老同方）的
			//      「判据判错时的后路」语义逐位不变。Logo 行的 force-show 不受影响 —— 宝龙达确实支持 Logo 灯
			//      （功能号 15，TrySetLogoLightViaBackend 已接），那一行的 show 是真的能用的。
			//   ⚠️ 撤除条件：**将来若给宝龙达接了灯带后端**（TrySetLightPowerViaBackend 认了 HidLightbar/Ctrl，
			//      且 BuildCapabilities 开了 Lightbar / RgbLightbar），这个前提必须一并撤掉，否则它会变成新的
			//      死约束（把已支持的行也压住）。注意这里退化成 auto 而非写死 false，就是为了那种情况下能自动放行。
			if (hw != null && hw.UsesBaolongda)
			{
				lightbarOut = lightbarRow && !phantom;
				stripReason = "force-show-ignored-baolongda(" + baseEvidence + ")->" + (lightbarOut ? "keep" : "hide");
			}
			else
			{
				lightbarOut = true;
				stripReason = "force-show";
			}
		}
		else if (LightRowUiValue.IsHide(stripMode))
		{
			lightbarOut = false;
			stripReason = "force-hide";
		}
		else
		{
			lightbarOut = lightbarRow && !phantom;
			stripReason = "auto(" + baseEvidence + ")->" + (lightbarOut ? "keep" : "hide");
		}
		bool logoOut;
		string logoReason;
		if (LightRowUiValue.IsShow(logoMode))
		{
			// 2026-10-09（lightspeed）：与灯带行同一条修正（见上面 force-show 的注释）——
			// 强制显示 = 不看判据把 Logo 行开回来；auto / hide 两条一字未动。
			logoOut = true;
			logoReason = "force-show";
		}
		else if (LightRowUiValue.IsHide(logoMode))
		{
			logoOut = false;
			logoReason = "force-hide";
		}
		else
		{
			logoOut = logoRow && logoReal;
			logoReason = "auto(" + logoEvidence + ")->" + (logoOut ? "keep" : "hide");
		}
		reason = "base:" + stripReason + "; logo:" + logoReason;
		return (lightbarOut, logoOut);
	}

	/// <summary>
	/// 2026-10-09（lightsplit）：**旧键 <c>light_extra_ui</c> 的一次性迁移**（向后兼容，必须）。
	/// 规则（两个新键**都缺失**时才做）：
	///   * 旧值 <c>show</c> ⇒ <c>light_strip_ui=show</c> + <c>light_logo_ui=show</c>（两行都强制显示）；
	///   * 旧值 <c>auto</c>（含缺失 / 未知值）⇒ 两个新键都写 <c>auto</c>（各自按行判据）；
	///   * 旧值 <c>hide</c> ⇒ <c>light_strip_ui=hide</c> + <c>light_logo_ui=auto</c>
	///     —— 旧 <c>hide</c> 的意图是"藏掉那个幻影额外汇"，而 16Pro 2024 的正确结论正是
	///     "**隐灯带、留 Logo**"（旧口径只能靠改义硬凑，本批细分后终于能直接表达）。
	/// ⚠️ **不动旧键**（不删用户数据，留着可回溯），只写两个新键 + 打迁移标记
	/// <see cref="MigrationMarkerKey"/>=1；本进程内用静态 latch 保证只跑一次，
	/// 且两个新键一旦落地，下次启动就再也进不来 ⇒ 不会每次启动都写 config。
	/// 失败不影响启动（内部 try/catch 兜住，只记一行日志）。
	/// </summary>
	internal static void MigrateLegacyOnce()
	{
		try
		{
			if (Interlocked.Exchange(ref _legacyMigrationRan, 1) != 0)
			{
				return;
			}
			if (AppConfig.Exists(LightStripUiMode.Key) || AppConfig.Exists(LightLogoUiMode.Key))
			{
				// 已经是新口径（迁移做过了，或用户在新版上选过）⇒ 旧键彻底只读，不再参与任何解读。
				return;
			}
			string legacy = LightExtraUiMode.Current();
			string strip = LightRowUiValue.Normalize(legacy);
			string logo;
			if (string.Equals(strip, LightStripUiMode.Hide, StringComparison.OrdinalIgnoreCase))
			{
				// 旧 hide 的意图 = 只收那一条幻影额外汇；16Pro 的正确结论 = 隐灯带、留 Logo。
				logo = LightLogoUiMode.Auto;
			}
			else
			{
				logo = strip;
			}
			AppConfig.Set(LightStripUiMode.Key, strip);
			AppConfig.Set(LightLogoUiMode.Key, logo);
			AppConfig.Set(MigrationMarkerKey, 1);
			AppConfig.Flush();
			Logger.WriteLine($"[lightmerge] 灯光额外汇细分迁移：旧键 light_extra_ui{(LightExtraUiMode.IsPresent ? "=" + legacy : " 缺失/视为 auto")} ⇒ light_strip_ui={strip}、light_logo_ui={logo}（{MigrationMarkerKey}=1；旧键保留不删，只做一次）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[lightmerge] 灯光额外汇细分迁移失败（按未迁移继续，下次启动再试）：" + ex.Message);
		}
	}

	/// <summary>迁移是否已经跑过（同进程内），供测试/诊断复用；只读。</summary>
	internal static bool LegacyMigrationRan => Interlocked.CompareExchange(ref _legacyMigrationRan, 0, 0) != 0;
}
