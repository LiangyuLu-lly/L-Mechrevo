using System;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（灯带 bisect 开关）：把 0.291.1 里新加的两条灯带修复（① 给灯带/Logo 的 ColorBuffer 补 ID、
/// ② 灯带与 Logo 的 Status 指纹同值 ⇒ 只下发一次）各做成一个「可一键关闭」的开关，用途是让机主在真机上
/// **逐条关掉**，定位「beta18 正常 / beta22 不正常」到底是哪一条引入的。
/// </summary>
/// <remarks>
/// 口径（只读、不改任何状态）：
/// ① 两个默认值（见下面的具名常量）**都是 true** ⇒ 本机（Gen40）与正常用户行为与改前逐字一致；
/// ② 只有显式设了对应环境变量才可能被关掉；关掉后**逐字回到 18.6 的行为**（见各调用点注释）；
/// ③ 两条开关互不影响，可单独关、可同时关；
/// ④ 只读环境变量，**绝不写**环境变量；不动 config、不点/不改任何灯效设置。
///
/// 取值表（判定大小写不敏感，首尾空白忽略）：
///   未设 / 空串            ⇒ 默认 on（＝保持修复开启）
///   1 / true / on / yes    ⇒ on
///   0 / false / off / no   ⇒ off（＝关闭该条修复，回到 18.6）
///   其它任意值             ⇒ 保持默认 on，并把原值打进启动日志（写错不会意外改变行为，但日志里看得见）
///
/// 读取顺序：进程环境变量 → 用户级环境变量。理由：Windows 不会把新设的用户级变量塞进已在跑的进程，
/// 只认进程环境的话「setx 后重启 LM」经常看不到效果，显式再读一遍用户级最稳。⚠️ 刻意不读机器级
/// （与 UiQaShowAll 同口径：不改、也不依赖机器级环境变量）。
/// </remarks>
internal static class LightCompatibilitySwitches
{
	/// <summary>「给灯带 / Logo 的 SetEffectALL 补 ColorBuffer 每项 ID」这条修复的总开关（默认值）。</summary>
	internal const bool EnableLightbarIdFix = true;

	/// <summary>「灯带行与 Logo 行 Status 指纹逐字同值 ⇒ 只下发一次」这条去重修复的总开关（默认值）。</summary>
	internal const bool EnableLightbarDedup = true;

	/// <summary>
	/// 2026-10-09（① 通用 per-key 分支补 ID）：通用 per-key 分支（非 40 系、非单分区的键盘）
	/// 的 ColorBuffer 之前不带 ID ⇒ 按我们自己的诊断口径「无 ID ⇒ 整条 SetEffectALL 不生效」，
	/// 单键键盘 HID 掉线回落到 MQTT 时灯效/颜色会静默失效。本开关默认 on ⇒ 该分支复用
	/// BuildColorBufferWithIds（作者 0.290.6 的口径：每个主题每一项都带 ID=i，顺序 ID,R,G,B）。
	/// </summary>
	internal const bool EnablePerKeyColorBufferIdFix = true;

	/// <summary>
	/// 2026-10-09（② 键盘类型判据 对齐）：ordinal→名称→kind。
	/// on（默认）= ordinal=1 才是单分区，FourZone(2) 走它自己的 6 项灯效表与通用分支；
	/// off = 回到 0.291.2 旧判据（KeyboardType∈{1,2} 都当单分区）。
	/// </summary>
	internal const bool EnableKeyboardKindModel = true;

	/// <summary>
	/// 2026-10-29（A5 · Mix 关灯真 bug）：灯带 / Logo / 铰链 / 同步四个通道的 color.isCircular
	/// 改按「逐通道 × 逐效果」表取值（LightbarColorPolicy），并把"没颜色就发 7 段七彩"的兜底
	/// 限制到 Rainbow 专用。默认 on。
	/// 关掉（环境变量 LMECHREVO_LIGHTBAR_MIXFIX=0）⇒ 回到旧口径（isCircular = Breathing || Mix，Mix 发 7 段七彩）。
	/// </summary>
	internal const bool EnableLightbarMixFix = true;

	/// <summary>环境变量名：置 0 关闭「补 ID」（恢复 18.6 的通用 7 段无 ID 帧）。</summary>
	internal const string LightbarIdFixEnvironmentVariable = "LMECHREVO_LIGHTBAR_IDFIX";

	/// <summary>环境变量名：置 0 关闭「灯带去重」（四个通道照常各发一次）。</summary>
	internal const string LightbarDedupEnvironmentVariable = "LMECHREVO_LIGHTBAR_DEDUP";

	/// <summary>环境变量名：置 0 关闭「通用 per-key 分支补 ID」（回到无 ID 的通用帧）。</summary>
	internal const string PerKeyIdFixEnvironmentVariable = "LMECHREVO_PERKEY_IDFIX";

	/// <summary>环境变量名：置 0 关闭「ordinal→kind 键盘判据」（回到 0.291.2 的 1/2 都算单分区）。</summary>
	internal const string KeyboardKindModelEnvironmentVariable = "LMECHREVO_KB_KIND_MODEL";

	/// <summary>环境变量名：置 0 关闭「灯带/Logo 逐效果 isCircular + Rainbow 专用七色板」修复（回到 Mix 关灯那一版）。</summary>
	internal const string LightbarMixFixEnvironmentVariable = "LMECHREVO_LIGHTBAR_MIXFIX";

	private static (bool Value, string Source)? _lightbarIdFix;

	private static (bool Value, string Source)? _lightbarDedup;

	private static (bool Value, string Source)? _perKeyIdFix;

	private static (bool Value, string Source)? _keyboardKindModel;

	private static (bool Value, string Source)? _lightbarMixFix;

	private static int _startupLogged;

	/// <summary>最终生效值：true=按修复后的行为（补 ID）；false=逐字回到 18.6。</summary>
	internal static bool LightbarIdFixEnabled => ResolveCached(ref _lightbarIdFix, LightbarIdFixEnvironmentVariable, EnableLightbarIdFix).Value;

	/// <summary>最终生效值：true=按修复后的行为（指纹同值时去重）；false=逐字回到 18.6（各通道各发一次）。</summary>
	internal static bool LightbarDedupEnabled => ResolveCached(ref _lightbarDedup, LightbarDedupEnvironmentVariable, EnableLightbarDedup).Value;

	/// <summary>最终生效值：true=通用 per-key 分支的 ColorBuffer 带 ID（默认）；false=不带 ID（0.291.2 旧行为）。</summary>
	internal static bool PerKeyColorBufferIdFixEnabled => ResolveCached(ref _perKeyIdFix, PerKeyIdFixEnvironmentVariable, EnablePerKeyColorBufferIdFix).Value;

	/// <summary>最终生效值：true=ordinal→kind 判据（默认，作者 0.290.6 口径）；false=0.291.2 旧判据。</summary>
	internal static bool KeyboardKindModelEnabled => ResolveCached(ref _keyboardKindModel, KeyboardKindModelEnvironmentVariable, EnableKeyboardKindModel).Value;

	/// <summary>
	/// 最终生效值：true=灯带/Logo/铰链/同步按逐效果 isCircular 表取值且七彩兜底只留给 Rainbow；
	/// false=旧口径（isCircular = Breathing || Mix，Mix 发 7 段七彩）。
	/// </summary>
	internal static bool LightbarMixFixEnabled => ResolveCached(ref _lightbarMixFix, LightbarMixFixEnvironmentVariable, EnableLightbarMixFix).Value;

	/// <summary>
	/// 启动时打**一条**独立日志（Logger.WriteLine，不走去重），把两个开关的最终生效值与来源写清楚，
	/// 便于机主导包/替换后一眼确认。只写日志，不改任何状态；本进程只打一次。
	/// </summary>
	internal static void LogStartupState()
	{
		if (Interlocked.Exchange(ref _startupLogged, 1) != 0)
		{
			return;
		}
		(bool Value, string Source) idFix = ResolveCached(ref _lightbarIdFix, LightbarIdFixEnvironmentVariable, EnableLightbarIdFix);
		(bool Value, string Source) dedup = ResolveCached(ref _lightbarDedup, LightbarDedupEnvironmentVariable, EnableLightbarDedup);
		(bool Value, string Source) perKeyId = ResolveCached(ref _perKeyIdFix, PerKeyIdFixEnvironmentVariable, EnablePerKeyColorBufferIdFix);
		(bool Value, string Source) kbKind = ResolveCached(ref _keyboardKindModel, KeyboardKindModelEnvironmentVariable, EnableKeyboardKindModel);
		(bool Value, string Source) lbMix = ResolveCached(ref _lightbarMixFix, LightbarMixFixEnvironmentVariable, EnableLightbarMixFix);
		Logger.WriteLine("[light] 灯带/键盘修复开关: lightbarIdFix=" + (idFix.Value ? "on" : "off") + " dedup=" + (dedup.Value ? "on" : "off")
			+ " perKeyIdFix=" + (perKeyId.Value ? "on" : "off") + " kbKindModel=" + (kbKind.Value ? "on" : "off")
			+ " lightbarMixFix=" + (lbMix.Value ? "on" : "off")
			+ " (来源: idFix=" + idFix.Source + "; dedup=" + dedup.Source + "; perKeyIdFix=" + perKeyId.Source + "; kbKindModel=" + kbKind.Source
			+ "; lightbarMixFix=" + lbMix.Source + ")"
			+ " | " + LightbarIdFixEnvironmentVariable + " / " + LightbarDedupEnvironmentVariable + " / " + PerKeyIdFixEnvironmentVariable + " / " + KeyboardKindModelEnvironmentVariable
			+ " / " + LightbarMixFixEnvironmentVariable + " 取值 1/true/on/yes=开, 0/false/off/no=关; 改完重启 LM 生效; 删掉变量即恢复默认(on)");
	}

	private static (bool Value, string Source) ResolveCached(ref (bool Value, string Source)? cache, string variableName, bool defaultValue)
	{
		(bool Value, string Source)? cached = cache;
		if (!cached.HasValue)
		{
			cached = Resolve(variableName, defaultValue);
			cache = cached;
		}
		return cached.Value;
	}

	private static (bool Value, string Source) Resolve(string variableName, bool defaultValue)
	{
		string raw = ReadRaw(variableName);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return (defaultValue, "默认(未设 " + variableName + ")");
		}
		string text = raw.Trim();
		if (IsOn(text))
		{
			return (true, "环境变量 " + variableName + "=" + text);
		}
		if (IsOff(text))
		{
			return (false, "环境变量 " + variableName + "=" + text);
		}
		return (defaultValue, "默认(" + variableName + "=" + text + " 无法识别, 按默认 on)");
	}

	private static bool IsOn(string text)
	{
		return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsOff(string text)
	{
		return text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase) || text.Equals("no", StringComparison.OrdinalIgnoreCase);
	}

	private static string ReadRaw(string variableName)
	{
		try
		{
			string processValue = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Process);
			if (!string.IsNullOrWhiteSpace(processValue))
			{
				return processValue;
			}
			return Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User) ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
