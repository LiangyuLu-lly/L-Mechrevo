using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（kbdctrl，用户原话「只搁置用户模式」⇒「给 LM 接通键盘灯」这一条可以开工）：
/// **默认关闭**的键盘灯（<c>Keyboard/Ctrl</c>）下发放行开关。
///
/// <para><b>它放宽的判定只有一条</b>：本机 <c>Keyboard/Status</c> 报 <c>solution=UNKNOWN</c>、
/// <c>powerStatus</c> 非 On 时，LM 侧「是否往 <c>Keyboard/Ctrl</c> 发帧」的判定会把键盘灯整条挡住
/// （真实抓包 29 条全是官方 S-&gt;C，LM 只 CONNECT/SUBSCRIBE/PINGREQ）。开关打开后，
/// <c>MechrevoService.PublishKeyboardCtrl*</c> 这一组新方法会**跳过能力/回读判定**直接组包下发。</para>
///
/// <para>⛔ 它<b>不影响任何其它行为</b>：灯带（<c>HidLightbar/Ctrl</c> 及 Logo/Hinge/Sync）、
/// 用户模式、模式/性能/显卡/风扇等全部路径一行未动 —— 本开关唯一的消费点是
/// <see cref="KeyboardCtrlPayload"/> 那条新链路（<c>MechrevoService.PublishKeyboardCtrl*</c> 与
/// <c>SetKeyboardEffect</c>/<c>SetKeyboardPower</c>/<c>SetKeyboardBrightnessPreservingEffect</c> 三个入口的
/// 「开关打开时改走新链路」这一句）。</para>
///
/// <para><b>为什么选环境变量而不是 <c>LightCompatibilitySwitches</c> 里那一套</b>：那一族的口径是
/// 「默认 on，设 0 才关」；本开关要的是**默认 off，设 1 才开**（= 关闭时行为与现在逐字一致、便于 A/B 对照），
/// 与 <c>Force30Ui</c>(<c>LMECHREVO_FORCE_30UI</c>) / <c>Force30Perf</c>(<c>LMECHREVO_FORCE_30PERF</c>) 同一风格。
/// 另起一个类还顺带避免了改动 <c>LightCompatibilitySwitches.LogStartupState</c> 那行既有的启动日志文本。</para>
///
/// <para>打开方式（两条等价路径；进程启动时读一次并缓存 ⇒ 改完要重启 LM）：</para>
/// <list type="number">
/// <item>环境变量 <c>LMECHREVO_KB_CTRL=1</c>（推荐；进程级或**用户级**即可，不要设机器级）。</item>
/// <item>或 config.json 的 <c>"实验_kb_ctrl": 1</c>。</item>
/// </list>
/// <para>一秒回退：删掉环境变量 / 改成 0，或把该 config 键改回 0，然后重启 LM。</para>
/// </summary>
internal static class KeyboardCtrlPublishSwitch
{
	/// <summary>环境变量名（进程级/用户级即可；进程启动时读一次并缓存）。</summary>
	internal const string EnvironmentVariable = "LMECHREVO_KB_CTRL";

	/// <summary>等价的 config.json 键名（值为 1 视为打开）。</summary>
	internal const string ConfigKey = "实验_kb_ctrl";

	/// <summary>默认值：<b>关</b>。关闭 ⇒ 不组包、不发 <c>Keyboard/Ctrl</c>，行为与加这个类之前逐字一致。</summary>
	internal const bool DefaultEnabled = false;

	private static bool? _enabled;

	private static int _startupLogged;

	/// <summary>开关是否打开（默认 false）。首次求值时读一次环境变量与 config，之后缓存。</summary>
	internal static bool Enabled
	{
		get
		{
			bool? enabled = _enabled;
			if (!enabled.HasValue)
			{
				enabled = Read() || IsMachineAutoEnabled();
				_enabled = enabled;
			}
			return enabled.Value;
		}
	}

	/// <summary>
	/// 启动时打一行状态日志（默认 OFF 也打），只写日志、不改任何状态；本进程只打一次。
	/// 关闭时这行只是"ON/OFF"不同，不含任何下发。
	/// </summary>
	internal static void LogStartupState()
	{
		if (System.Threading.Interlocked.Exchange(ref _startupLogged, 1) != 0)
		{
			return;
		}
		string processValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process);
		string userValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User);
		Logger.WriteLine($"[kbd] Keyboard/Ctrl 下发开关 = {(Enabled ? "ON" : "OFF")}（默认 OFF）| {EnvironmentVariable}: process={(processValue.Length == 0 ? "(unset)" : processValue)} user={(userValue.Length == 0 ? "(unset)" : userValue)} | config {ConfigKey}={AppConfig.Get(ConfigKey, 0)} | {(Enabled ? "键盘灯 SetEffectALL/SetPower/SetLightingLevel 将直接发往 Keyboard/Ctrl" : "不发任何 Keyboard/Ctrl 帧，行为与改前逐字一致")}");
	}

	/// <summary>无界（WujieSmuApu）单分区 EC 键盘：50 系 GCU 需要全字段 Keyboard/Ctrl 负载，本机自动启用新链路。</summary>
	private static bool IsMachineAutoEnabled()
	{
		try
		{
			return Program.hw != null && Program.hw.WujieSmuApu;
		}
		catch
		{
			return false;
		}
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

	private static bool Read()
	{
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process)))
		{
			return true;
		}
		// Windows 不会把新设的用户级变量塞进已在跑的进程，显式再读一遍用户级最稳。⚠️ 只读不写；机器级故意不读。
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User)))
		{
			return true;
		}
		try
		{
			return AppConfig.Get(ConfigKey, 0) == 1;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsOn(string raw)
	{
		string text = (raw == null) ? string.Empty : raw.Trim();
		if (text.Length == 0)
		{
			return DefaultEnabled;
		}
		if (text == "1")
		{
			return true;
		}
		return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase);
	}
}
