using System;
using MechrevoLite;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// task-20：旧 WinForms 版 <c>MechrevoLite.UiQaShowAll</c>（`6faf99b^:source/MechrevoLite/UiQaShowAll.cs`）
/// 的 WPF 等价物。
///
/// <para>语义与旧版**逐项一致**（唯一用途＝把平时被能力位/机型条件挡住的 UI 元素显示出来给人看样式，
/// 默认关闭、只影响显示、不改任何行为/取值/命令）：</para>
/// <list type="number">
/// <item>环境变量 <c>LMECHREVO_UI_QA</c>：进程级 → **持久化用户级**（显式读注册表值，设完变量再重启一定生效；
/// ⚠️ 机器级/系统级故意不读）→ config 键 <c>ui_qa</c>；任一为真即打开。</item>
/// <item>值判定：<c>1</c> / <c>true</c> / <c>yes</c> / <c>on</c>（忽略大小写）为真；空/0/false 关闭。</item>
/// <item>结果缓存一次；<see cref="LogStartupState" /> 启动时打一行（默认 OFF 也打，含 process/user/config 三个来源），
/// 供验收从日志确认开关是否生效；只写日志、不改状态。</item>
/// </list>
///
/// <para>⚠️ 接线边界：旧版是在 WinForms <c>SettingsForm</c> 里把「静音狂暴」按钮与「核显省电」行
/// 的可见性 <c>|| UiQaShowAll.Enabled</c>。WPF 的对应显隐在 <c>OriginalStyleWindow</c>/<c>MainWindow</c> 里，
/// 本任务写作用域禁止改那两个文件 ⇒ 本服务先提供**完整开关与启动日志**，UI 显隐接线留到窗口任务
/// （见 docs/notes/基准诊断与兼容功能接入-20261026.md §4 的接线片段）。</para>
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

    /// <summary>启动时打一行状态日志（默认 OFF 也打），供验收从日志确认「开关当前是否生效」。</summary>
    internal static void LogStartupState()
    {
        if (_startupLogged)
        {
            return;
        }
        _startupLogged = true;
        string processValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.Process);
        string userValue = ReadRaw(EnvironmentVariable, EnvironmentVariableTarget.User);
        Logger.WriteLine(
            $"[qa] UI QA show-all = {(Enabled ? "ON" : "OFF")} | {EnvironmentVariable}: " +
            $"process={(processValue.Length == 0 ? "(unset)" : processValue)} " +
            $"user={(userValue.Length == 0 ? "(unset)" : userValue)} | " +
            $"config {ConfigKey}={AppConfig.Get(ConfigKey, 0)} | 仅影响显示，不改任何行为/取值/命令");
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
        //    Windows 不会把新设的用户级变量自动塞进已经在跑的进程（含 explorer），只认进程环境的话，
        //    「设完变量 → 重启 LM」经常看不到效果；显式读一遍用户级值就稳了。
        //    ⚠️ 只读不写；机器级/系统级变量**故意不读**。
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
        string text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }
        if (text == "1")
        {
            return true;
        }
        return text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
