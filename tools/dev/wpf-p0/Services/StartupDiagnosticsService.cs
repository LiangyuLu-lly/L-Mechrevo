using System;
using MechrevoLite;
using MechrevoLite.Diagnostics;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// task-20：旧 WinForms <c>Program.Main</c> 启动诊断日志的 WPF 等价接线。
///
/// <para>基线证据（<c>6faf99b^:source/MechrevoLite/Program.cs</c>，单实例判定之后、创建 SettingsForm 之前）：</para>
/// <code>
/// UiQaShowAll.LogStartupState();                                   // 920
/// Force30Ui.LogStartupState();                                     // 923（UI 专属，旧 UI 已删，不迁）
/// MechrevoLite.Hardware.Force30Perf.LogStartupState();             // 929
/// LightCompatibilitySwitches.LogStartupState();                    // 938
/// MechrevoLite.Hardware.KeyboardCtrlPublishSwitch.LogStartupState();   // 942
/// MechrevoLite.Hardware.KeyboardCtrlBlockCountSwitch.LogStartupState();// 945
/// MachineCodeProbe.LogStartupState();                              // 954
/// </code>
///
/// <para>现状：这些方法在 <c>MechrevoLite.Core</c> 里都还在（internal，WPF 有 InternalsVisibleTo），
/// 但 **WPF 启动路径 0 调用**（旧 Program 已删）⇒ 日志缺失。本服务把顺序原样补齐；每个调用独立 try/catch，
/// 诊断失败绝不影响启动。</para>
///
/// <para>⚠️ 接线（由 Lead/窗口任务执行，本任务不改 App.xaml.cs）：
/// 在 <c>tools/dev/wpf-p0/App.xaml.cs</c> 的 `OnStartup` 里、**取得单实例锁之后、创建 OriginalStyleWindow 之前**
/// （建议紧挨 `ApplyFont();` 之前）调用 <see cref="Run" />。注意：`Force30Ui` 是旧 UI 专属类，随
/// `source/MechrevoLite/Force30Ui.cs` 一起删除，不迁；WPF 侧对应逻辑见 `Force30Perf` + 自身能力位。</para>
///
/// <para>只写日志（Logger 自动分级：正常为 Info，默认「仅错误」级别不落盘；读不到时自动升级为 Error），
/// ⛔ 不写配置/注册表/EC/NVRAM，⛔ 不改变用户可见行为。</para>
/// </summary>
internal static class StartupDiagnosticsService
{
    /// <summary>
    /// 按旧 <c>Program.Main</c> 顺序补打启动诊断日志（幂等友好：各子项内部自身也有一次性缓存）。
    /// </summary>
    internal static void Run()
    {
        Try(UiQaShowAll.LogStartupState, "UiQaShowAll");
        Try(Force30Perf.LogStartupState, "Force30Perf");
        Try(LightCompatibilitySwitches.LogStartupState, "LightCompatibilitySwitches");
        Try(KeyboardCtrlPublishSwitch.LogStartupState, "KeyboardCtrlPublishSwitch");
        Try(KeyboardCtrlBlockCountSwitch.LogStartupState, "KeyboardCtrlBlockCountSwitch");
        Try(MachineCodeProbe.LogStartupState, "MachineCodeProbe");
        // ModelScope：基线里 MigrateToModelScope 全库 0 调用（见 ModelScopeDiagnosticsService 注释），
        // 这里只补一行只读诊断，不迁移、不写配置 —— 避免与将来 Core 化后的迁移重复。
        Try(ModelScopeDiagnosticsService.LogStatus, "ModelScope");
    }

    private static void Try(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[diag] 启动诊断 " + name + " 失败（不影响启动）：" + ex.GetType().Name + " " + ex.Message);
        }
    }
}
