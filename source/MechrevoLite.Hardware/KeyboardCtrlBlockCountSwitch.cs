using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（kbdctrl②，用户更正原话「【7 分区】的蛟龙17也是单分区的」）：
/// 键盘灯 <c>Keyboard/Ctrl</c> 帧里 <c>ColorBlocks</c>/<c>ColorBuffer</c> 段数的**实验注入开关**。
///
/// <para><b>默认 = <c>official</c>（7 段）</b>：与 2026-10-09 官方 30 系控制台抓包的首个 SetEffectALL
/// **逐字节一致**。⛔ 默认<b>不会</b>因为"检测到单分区"而改变帧结构 —— 用户已明确
/// 「蛟龙17 也是单分区，而它的官方帧仍是 ColorBlocks:7」，即 7 段是官方对单分区键盘也照发的**固定格式**，
/// 不能拿它当分区数证据。</para>
///
/// <para><b>单分区的适配不靠改块数</b>：靠 <c>MonochromeIndex</c>（单色/预设色）、<c>SetLightingLevel</c>（亮度）、
/// <c>SetPower</c>（开关）—— 这三条已在 <c>MechrevoService.PublishKeyboardCtrl*</c> 里实现。</para>
///
/// <para><b>本开关是可选的诊断/实验项</b>（默认不生效）。打开方式：环境变量
/// <c>LMECHREVO_KB_BLOCKS</c>（进程级 → 用户级）：</para>
/// <list type="bullet">
/// <item>未设 / <c>official</c> ⇒ <c>ColorBlocks:7</c> + 7 段 ColorBuffer + <c>ManualIndex1..6</c> 照发（<b>默认</b>）</item>
/// <item><c>1</c> ⇒ <c>ColorBlocks:1</c> + ColorBuffer 只 1 项（<c>ID:0</c>）+ <c>ManualIndex1..6 = null</c></item>
/// <item><c>7</c> ⇒ 与 official 同值（显式写出用）</item>
/// <item>其它值 ⇒ 按默认 official，并把原值写进日志（写错不会意外改变行为）</item>
/// </list>
///
/// <para><b>⚠️ 尚待真机验证（本类不表结论）</b>：单分区键盘究竟该发 1 段还是仍发 7 段
/// （"前 1 段有效 + 其余黑/残留"）—— 现有证据**不足以判定**：
/// ① 官方对单分区（蛟龙17）抓到的就是 <c>ColorBlocks:7</c>；
/// ② 仓库里唯一一次"单分区实机验证成功"的修复（2026-10-04，<c>SetLightEffect</c> single-zone 分支，
/// 补 <c>MonochromeIndex</c> + 带 ID 的 ColorBuffer）用的也是 <b>7 段</b>，且用户确认灯恢复正常；
/// ③ 归档里**没有**任何 <c>ColorBlocks:1</c> 的真实帧样本。
/// ⇒ 故默认不动 7；要试 1 就在真机上设 <c>LMECHREVO_KB_BLOCKS=1</c> 做 A/B，并把
/// <c>[kbd] Keyboard/Ctrl 分区数=…（来源=…）</c> 那行日志一起回传核对。</para>
/// </summary>
internal static class KeyboardCtrlBlockCountSwitch
{
	/// <summary>环境变量名（进程级/用户级即可；进程启动时读一次并缓存）。</summary>
	internal const string EnvironmentVariable = "LMECHREVO_KB_BLOCKS";

	/// <summary>默认值字面量（日志与自测用）。</summary>
	internal const string OfficialValue = "official";

	/// <summary>默认段数 = 官方固定 7 槽格式。</summary>
	internal const int OfficialBlockCount = KeyboardCtrlPayload.KeyboardColorBlocks;

	private static (int BlockCount, string Source)? _resolved;

	private static int _startupLogged;

	/// <summary>本次实际要用的段数（默认 7）。</summary>
	internal static int BlockCount => Resolve().BlockCount;

	/// <summary>该段数的来源说明（写进日志，便于真机核对到底按几段发的）。</summary>
	internal static string Source => Resolve().Source;

	/// <summary>最终生效值（首次求值后缓存）。</summary>
	internal static (int BlockCount, string Source) Resolve()
	{
		(int BlockCount, string Source)? cached = _resolved;
		if (!cached.HasValue)
		{
			cached = Resolve(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process), ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User));
			_resolved = cached;
		}
		return cached.Value;
	}

	/// <summary>
	/// 纯函数内核（自测直接调它）：按"进程级优先、其次用户级"取值。
	/// 未设/空/<c>official</c>/无法识别 ⇒ 7（默认）；<c>1</c> ⇒ 1；<c>7</c> ⇒ 7。
	/// </summary>
	internal static (int BlockCount, string Source) Resolve(string processValue, string userValue)
	{
		string processText = (processValue == null) ? string.Empty : processValue.Trim();
		string userText = (userValue == null) ? string.Empty : userValue.Trim();
		if (processText.Length == 0 && userText.Length == 0)
		{
			return (OfficialBlockCount, "official（默认：未设 " + EnvironmentVariable + "）");
		}
		string scope = (processText.Length > 0) ? "进程级" : "用户级";
		string text = (processText.Length > 0) ? processText : userText;
		if (text.Equals(OfficialValue, StringComparison.OrdinalIgnoreCase))
		{
			return (OfficialBlockCount, "official（" + EnvironmentVariable + "=" + text + "，" + scope + "）");
		}
		if (text == "1")
		{
			return (KeyboardCtrlPayload.SingleZoneExperimentalColorBlocks, "实验注入 1 段（" + EnvironmentVariable + "=" + text + "，" + scope + "；⚠️ 待真机验证，非结论）");
		}
		if (text == "7")
		{
			return (OfficialBlockCount, "显式 7 段（" + EnvironmentVariable + "=" + text + "，" + scope + "，与 official 同值）");
		}
		return (OfficialBlockCount, "official（默认：" + EnvironmentVariable + "=" + text + " 无法识别，按默认 7 段）");
	}

	/// <summary>启动时打一行状态日志（默认也打）；只写日志、不改任何状态；本进程只打一次。</summary>
	internal static void LogStartupState()
	{
		if (System.Threading.Interlocked.Exchange(ref _startupLogged, 1) != 0)
		{
			return;
		}
		(int BlockCount, string Source) resolved = Resolve();
		Logger.WriteLine($"[kbd] Keyboard/Ctrl 块数注入 = {resolved.BlockCount} 段（来源={resolved.Source}）| 默认 official=7 段（与官方实测帧逐字节一致）；置 {EnvironmentVariable}=1 可试「单分区 1 段」，⚠️ 该取舍待真机验证，不是结论");
	}

	private static string ReadRaw(string name, EnvironmentVariableTarget target)
	{
		try
		{
			return Environment.GetEnvironmentVariable(name, target) ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
