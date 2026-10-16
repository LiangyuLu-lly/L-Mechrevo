using System;

namespace MechrevoLite;

/// <summary>
/// 2026-10-08（QA 开关，用户原话「你能不能给我开放静音狂暴按钮 还有核显省电按钮 我检查一下UI」）：
/// 一个**默认关闭**的调试/QA 开关，唯一用途＝把平时被「能力位 / 机型条件」挡住的 UI 元素**显示出来**
/// 给人看样式。它**不是**产品功能、**不**出现在设置界面里、**不**参与任何打包/版本行为。
///
/// <para>打开方式（两条等价路径，都只读、都需重启 LM 生效）：</para>
/// <list type="number">
/// <item>环境变量 <c>LMECHREVO_UI_QA=1</c>（推荐；**用户级**即可，不要设机器级/系统级）。
/// 读取顺序：进程环境变量 → **持久化的用户级变量**（显式读一遍注册表值，因此"设完变量再重启 LM"一定生效，
/// 不依赖 explorer 是否刷新过它自己的环境块）→ config 键；任一为真即视为打开。
/// ⚠️ 机器级/系统级环境变量**故意不读**。</item>
/// <item>或 config.json 里的 <c>"ui_qa": 1</c>（不方便设环境变量时用；同文件其它键一概不动）。</item>
/// </list>
/// <para>关闭 / 撤销：删掉该环境变量（或改成 0 / 空），或把 <c>ui_qa</c> 改回 0 / 删掉该键，然后重启 LM。
/// 设成 0、false、空、或者两项都不设 —— 一律等于「关闭」。</para>
///
/// <para>⚠️ 严格边界（用户口径，务必保持）：</para>
/// <list type="bullet">
/// <item><b>默认关闭</b>：两项都不设时本属性恒为 false，所有调用点的判据与加这个类之前**逐项一致**，
/// 因此正式行为与已打包产物零变化。</item>
/// <item><b>仅影响「是否显示」</b>：它只参与 <c>Control.Visible</c> 与随之而来的等宽重排（布局），
/// **不**改任何取值（能力位 / 配置项 / 派生属性）、**不**改任何命令的下发条件、**不**代发任何命令。
/// 真正会下发命令的入口仍走各自原有的能力位守卫 —— 例如「核显省电」三颗按钮的
/// <c>SwitchIgpuSavingAsync</c> 里照旧有 <c>!hw.SupportsGpuHotSwap</c> 早退，本机（Gen40）打开了
/// 开关也只是「看得见」，点下去不会真的下发 <c>IGPU_ONLY_CONNECT_RB_*</c>。</item>
/// <item>它**不改变**任何既有定论：例如 2026-10-03「40 系三模不显示核显省电」这条结论仍然成立
/// （那是 <c>MechrevoHw.SupportsGpuHotSwap</c> 的判据），本开关只是在 QA 时把那一行显示出来看样式。</item>
/// </list>
///
/// <para>当前已接入本开关的元素（仅这两处，均为「纯是否显示」，理由见各自的调用点注释）：</para>
/// <list type="bullet">
/// <item>主界面「性能模式」行里的<b>「静音狂暴」按钮</b>（<c>SettingsForm.RefreshDeviceCapabilities</c> 的 flag16）；</item>
/// <item>主界面<b>「核显省电」行</b>（<c>SettingsForm.IgpuSavingRowVisible</c>，位置＝显卡模式按钮的下一行）。</item>
/// </list>
/// </summary>
internal static class UiQaShowAll
{
	/// <summary>主开关环境变量名（用户级即可；进程启动时读一次并缓存）。</summary>
	internal const string EnvironmentVariable = "LMECHREVO_UI_QA";

	/// <summary>等价的 config.json 键名（值为 1 视为打开；0 / 缺失 / 其它值视为关闭）。</summary>
	internal const string ConfigKey = "ui_qa";

	private static bool? _enabled;

	private static bool _startupLogged;

	/// <summary>
	/// QA 开关是否打开（默认 false）。首次求值时读一次环境变量与 config，之后缓存
	/// —— 与 <c>RuntimeCpuTuning.ForceEntry</c>（LMECHREVO_FORCE_CPU_TUNING）同一写法。
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
		Logger.WriteLine($"[qa] UI QA show-all = {(Enabled ? "ON" : "OFF")} | {EnvironmentVariable}: process={(processValue.Length == 0 ? "(unset)" : processValue)} user={(userValue.Length == 0 ? "(unset)" : userValue)} | config {ConfigKey}={AppConfig.Get(ConfigKey, 0)} | 仅影响显示，不改任何行为/取值/命令");
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
		// ① 进程环境变量：当前进程自己带上的值（命令行里 set 过、或由父进程继承来的）。
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process)))
		{
			return true;
		}
		// ② 持久化的**用户级**环境变量：用户用 setx / [Environment]::SetEnvironmentVariable(...,'User') 设的那个。
		//    这一条是必须的：Windows 不会把新设的用户级变量自动塞进已经在跑的进程（含 explorer），
		//    只认进程环境的话，「设完变量 → 重启 LM」在本机经常看不到效果；显式读一遍用户级值就稳了。
		//    ⚠️ 只读不写；机器级/系统级变量**故意不读**（本轮口径：不改机器级环境变量）。
		if (IsOn(ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User)))
		{
			return true;
		}
		// ③ 等价的 config.json 键（不方便设环境变量时的备用路径）。
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
