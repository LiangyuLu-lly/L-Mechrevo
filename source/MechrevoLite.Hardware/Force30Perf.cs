using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（实验开关，用户原话「先在我这台机器上无条件显示30系性能模式 我看看40A GCU能不能正确切30系的性能方式」）：
/// **完整 30 系性能模式**开关 —— 与只改 UI 的 <c>MechrevoLite.Force30Ui</c>（<c>LMECHREVO_FORCE_30UI</c>，
/// 语义保持「只看界面、不下发」一字不变）**分开**的第二把开关。
///
/// <para>它与 Force30Ui 的**本质区别**：本开关喂给
/// <c>MechrevoService.IsGcu30</c> —— 也就是 <c>MechrevoHw.SupportsModeProfileConfig</c> 的唯一非显卡代际判据。
/// 因此打开后是**行为改变**：不仅「模式 × 配置」行出现、自定义按钮隐藏，
/// <c>MechrevoHw.ResolveModeProfileIndex</c> 还会真的把 <c>ModeProfileConfig.Get(visualMode)</c>
/// 作为 <c>ProfileIndex</c> 塞进 <c>Fan/Control</c> 一起下发（<c>MechrevoHw.cs:4474</c> /
/// <c>MechrevoService.cs:432</c>），并让 <c>VerifyProfileIndexAsync</c> 跑「设备认不认这个字段」的只读回读判定。</para>
///
/// <para><b>为什么必须是环境变量而不是改注册表</b>：本机 <c>HKLM\SOFTWARE\L-Mechrevo\GcuVariant</c> 现值
/// 正好是 <c>"40"</c>，而 <c>MechrevoService.IsGcu40Keyboard</c> 认 40 家族（<c>"40"</c> 或 <c>"40B"</c>；
/// 2026-10-09 起 40B≡40，见 <c>GcuVariantFamily</c>）。
/// 一旦把 <c>GcuVariant</c> 改成 <c>"30"</c> 去做这个实验，键盘灯指令会从 40 系协议分支切走
/// （丢掉 <c>powerstatus</c> / <c>MonochromeIndex</c> / 7 格 <c>ColorBlocks</c>，
/// <c>MechrevoService.cs:3442/3454</c>）⇒ 本开关**只读注册表、绝不写注册表**，
/// <c>IsGcu40Keyboard</c> 的判据不被本开关改变（它读到的仍是本机 <c>"40"</c> ⇒ 键盘灯照旧走 40 系协议）。</para>
///
/// <para>打开方式（两条等价路径；进程启动时读一次并缓存 ⇒ 改完要重启 LM）：</para>
/// <list type="number">
/// <item>环境变量 <c>LMECHREVO_FORCE_30PERF=1</c>（推荐；进程级或**用户级**即可，不要设机器级）。
/// 读取顺序：进程环境变量 → 持久化的用户级变量 → config 键。任一为真即视为打开。</item>
/// <item>或 config.json 里的 <c>"实验_force_30perf": 1</c>（其它键一概不动）。</item>
/// </list>
/// <para>关闭 / 一秒回退：删掉该环境变量（或改成 0 / 空），或把该 config 键改回 0 / 删掉，然后重启 LM。
/// 设成 0、false、空、或两项都不设 —— 一律等于「关闭」。</para>
///
/// <para><b>默认关闭的证明口径</b>：两项都不设时 <see cref="Enabled"/> 恒为 false ⇒
/// <c>MechrevoService.IsGcu30</c> 的返回值与加这个类之前**逐字一致**（仍然只看注册表
/// <c>GcuVariant=="30"</c>），于是 <c>SupportsModeProfileConfig</c> / <c>ResolveModeProfileIndex</c> /
/// <c>VerifyProfileIndexAsync</c> 三条链路全部保持原样（不再下发 ProfileIndex、不再跑回读判定）。
/// 见 <c>MechrevoService.IsGcu30</c> 里那句「开关关闭时逐字等价」的注释。</para>
/// </summary>
internal static class Force30Perf
{
	/// <summary>主开关环境变量名（进程级/用户级即可；进程启动时读一次并缓存）。</summary>
	internal const string EnvironmentVariable = "LMECHREVO_FORCE_30PERF";

	/// <summary>等价的 config.json 键名（值为 1 视为打开；0 / 缺失 / 其它值视为关闭）。</summary>
	internal const string ConfigKey = "实验_force_30perf";

	/// <summary>日志/提示里用来区分「真 30 系机型」与「本实验开关」的来源标记。</summary>
	internal const string LogSource = "source=force30perf";

	private static bool? _enabled;

	private static bool _startupLogged;

	/// <summary>
	/// 实验开关是否打开（默认 false）。首次求值时读一次环境变量与 config，之后缓存
	/// —— 与 <c>MechrevoLite.Force30Ui.Enabled</c> / <c>UiQaShowAll.Enabled</c> 同一写法。
	/// </summary>
	internal static bool Enabled
	{
		get
		{
			bool? enabled = _enabled;
			if (!enabled.HasValue)
			{
				enabled = Read();
				_enabled = enabled;
			}
			return enabled.Value;
		}
	}

	/// <summary>
	/// 给 <c>[perf-profile]</c> 日志/提示用的来源后缀：开关打开时为 <c> source=force30perf</c>，
	/// 关闭时为空串 ⇒ 关闭状态下日志文本与加这个类之前**逐字一致**。
	/// </summary>
	internal static string LogSourceSuffix => Enabled ? (" " + LogSource) : string.Empty;

	/// <summary>
	/// 启动时打一行状态日志（默认 OFF 也打），供验收时从日志确认开关当前是否生效、
	/// 以及随后 <c>SupportsModeProfileConfig</c> 会取到什么值。只写日志，不改任何状态。
	/// </summary>
	internal static void LogStartupState()
	{
		if (_startupLogged)
		{
			return;
		}
		_startupLogged = true;
		string processValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process);
		string userValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User);
		Logger.WriteLine($"[exp30perf] force full 30-series perf = {(Enabled ? "ON" : "OFF")} | {EnvironmentVariable}: process={(processValue.Length == 0 ? "(unset)" : processValue)} user={(userValue.Length == 0 ? "(unset)" : userValue)} | config {ConfigKey}={AppConfig.Get(ConfigKey, 0)} | {(Enabled ? "SupportsModeProfileConfig 将被强制判真 ⇒ 切模式真的下发 ProfileIndex（只读注册表，不改 GcuVariant）" : "判据逐字不变：IsGcu30 仍只看注册表 GcuVariant==\"30\"，不下发 ProfileIndex")}");
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
		// ① 进程环境变量：当前进程自己带上的值（一行 .cmd 里 set 过、或由父进程继承来的）。
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process)))
		{
			return true;
		}
		// ② 持久化的**用户级**环境变量：Windows 不会把新设的用户级变量自动塞进已经在跑的进程，
		//    显式读一遍用户级值，「设完变量 → 重启 LM」才一定生效。
		//    ⚠️ 只读不写；机器级/系统级变量**故意不读**。
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User)))
		{
			return true;
		}
		// ③ 等价的 config.json 键（键名带「实验」前缀，和正常用户的配置项天然隔开）。
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
			return false;
		}
		if (text == "1")
		{
			return true;
		}
		return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase);
	}
}
