using System;

namespace MechrevoLite;

/// <summary>
/// 2026-10-09（实验开关，用户原话「c」＝先看 30 系 UI，再决定要不要铺 30 载荷）：
/// 一个**默认关闭**的实验开关，唯一用途＝让本机（40B / Gen40）的**主界面按 30 系机型的样式渲染**
/// 「模式 × 配置」那一行，供用户肉眼确认「30 系 UI 到底多出什么」。它**不是**产品功能、
/// **不**出现在设置界面、**不**参与任何打包/版本行为。
///
/// <para>打开方式（两条等价路径；进程启动时读一次并缓存 ⇒ 改完要重启 LM）：</para>
/// <list type="number">
/// <item>环境变量 <c>LMECHREVO_FORCE_30UI=1</c>（推荐；**用户级或进程级**即可，不要设机器级）。
/// 读取顺序：进程环境变量 → 持久化的**用户级**变量（显式读一遍，因此"设完变量再重启 LM"一定生效）
/// → config 键。任一为真即视为打开。</item>
/// <item>或 config.json 里的 <c>"实验_force_30ui": 1</c>（同文件其它键一概不动）。</item>
/// </list>
/// <para>关闭 / 一秒回退：删掉该环境变量（或改成 0 / 空），或把该 config 键改回 0 / 删掉，然后重启 LM。
/// 设成 0、false、空、或两项都不设 —— 一律等于「关闭」。</para>
///
/// <para>⚠️ 严格边界（用户口径，务必保持；这是本开关能随正式版一起发的全部依据）：</para>
/// <list type="bullet">
/// <item><b>默认关闭</b>：两项都不设时 <c>Force30Ui.Enabled</c> 恒为 false，
/// 两个调用点的判据与加这个类之前**逐项一致** ⇒ 对正常用户零影响。</item>
/// <item><b>只影响「是否显示」</b>：它只喂给 <c>SettingsForm.ApplyPerfConfigRowVisibility</c>
/// 与 <c>SettingsForm.RefreshDeviceCapabilities</c> 里那个「隐藏自定义按钮」的布尔量，
/// 也就是只参与 <c>Control.Visible</c> 与随之而来的性能面板高度/按钮重排（布局）。
/// <b>不</b>改任何取值、<b>不</b>改任何下发条件、<b>不</b>代发任何命令。</item>
/// <item><b>绝不影响 <c>MechrevoHw.SupportsModeProfileConfig</c></b>：那个属性是
/// <c>MechrevoHw.ResolveModeProfileIndex</c>（切模式时决定是否给 GCU 下发 <c>ProfileIndex</c>）的判据，
/// 一旦被本开关污染就会变成**行为改变**（真的下发 ProfileIndex）。因此本类
/// **故意**住在 UI 程序集（<c>MechrevoLite</c>）里，而 <c>SupportsModeProfileConfig</c> /
/// <c>ResolveModeProfileIndex</c> 住在 <c>MechrevoLite.Hardware</c> 里 ⇒
/// <b>硬件程序集无法引用本类</b>（编译期即不可能），开关的调用链与 ProfileIndex 的下发链在结构上不相交。
/// 打开本开关后 <c>SupportsModeProfileConfig</c> 返回值照旧为 false，
/// 切模式时<b>仍然不下发</b> <c>ProfileIndex</c>（保持 40B 的既有行为）。</item>
/// <item>它**不改变**任何既有定论；30 系专属 UI 的**唯一**判据仍是
/// <c>MechrevoHw.SupportsModeProfileConfig</c>（= Gen30 或 <c>MechrevoService.IsGcu30</c>），该判据一字未动。</item>
/// </list>
/// </summary>
internal static class Force30Ui
{
	/// <summary>主开关环境变量名（用户级/进程级即可；进程启动时读一次并缓存）。</summary>
	internal const string EnvironmentVariable = "LMECHREVO_FORCE_30UI";

	/// <summary>等价的 config.json 键名（值为 1 视为打开；0 / 缺失 / 其它值视为关闭）。</summary>
	internal const string ConfigKey = "实验_force_30ui";

	private static bool? _enabled;

	private static bool _startupLogged;

	/// <summary>
	/// 实验开关是否打开（默认 false）。首次求值时读一次环境变量与 config，之后缓存
	/// —— 与 <c>UiQaShowAll.Enabled</c> / <c>RuntimeCpuTuning.ForceEntry</c> 同一写法。
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
	/// 启动时打一行状态日志，供验收时从日志确认「开关当前是否生效」。
	/// 只写日志，不改任何状态；关闭时也照打 OFF（这样"没打开"同样有据可查）。
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
		Logger.WriteLine($"[exp30ui] force 30-series UI = {(Enabled ? "ON" : "OFF")} | {EnvironmentVariable}: process={(processValue.Length == 0 ? "(unset)" : processValue)} user={(userValue.Length == 0 ? "(unset)" : userValue)} | config {ConfigKey}={AppConfig.Get(ConfigKey, 0)} | 仅影响 UI 显示，不下发 ProfileIndex");
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
