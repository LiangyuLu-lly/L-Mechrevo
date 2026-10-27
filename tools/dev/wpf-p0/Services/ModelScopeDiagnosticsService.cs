using System;
using MechrevoLite;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// task-20：<c>ModelScopeMigration</c> 的 WPF/Core 等价性核对结论 —— **只读诊断，不迁移**。
///
/// <para>基线证据（`6faf99b^`，注意是旧 WinForms 删除前最后一个提交的父提交）：</para>
/// <list type="bullet">
/// <item>实现：<c>source/MechrevoLite/ModelScopedConfig.cs</c> 提供
/// <c>Plan()</c> / <c>MigrateToModelScope(model)</c> / <c>ScopeMarkerKey="model_config_scope"</c>；
/// <c>ModelScopeMigration</c> 只是 5 行的 record 结果类型。</item>
/// <item>调用点：`git grep -n 'MigrateToModelScope\|ModelScopedConfig\.' 6faf99b^ -- source` 的结果**只有
/// 定义本身 + 几处文档注释**（CpuCoAutoApplyGuard/RuntimeCpuTuning 的注释提到 `IsMachineScopedKey`）——
/// 即：**旧版启动流程从未调用过迁移**，`ModelScopedConfig` 是当时先落实现、尚未接线的死代码。</item>
/// <item>当前 Core：`source/` 下没有 `ModelScopedConfig.cs` / `ModelScopeMigration.cs`（`git grep` 只剩
/// SettingsBackup/CpuCoAutoApplyGuard/RuntimeCpuTuning 的注释），也没有任何 `model_config_scope` 读写，
/// ⇒ 当前**不存在**“已由 Core 启动迁移”，也**不存在**需要避免的重复迁移。</item>
/// </list>
///
/// <para>结论：**不需要补迁移**。若将来真的要启用机型作用域（把 A 机器的限充/核显/灯光/CO/FMax 值搬到 B 机器时
/// 不串味），必须把 <c>ModelScopedConfig</c> + <c>ModelScopeMigration</c> 迁进 Core，并在
/// **App/Core 启动最早期、任何配置读取之前**调用一次 <c>MigrateToModelScope(AppConfig.GetModel())</c>；
/// 那是一个独立功能任务，不应混进本诊断补齐（否则会创建一个基线从未有过的行为）。</para>
///
/// <para>本服务只做只读状态日志（便于将来核对“是否已经迁过”），⛔ 不写配置、不删键、不迁移。</para>
/// </summary>
internal static class ModelScopeDiagnosticsService
{
    private static bool _logged;

    /// <summary>只读打印机型作用域状态（marker + 当前机型 + 结论），供启动诊断/验收核对。</summary>
    internal static void LogStatus()
    {
        if (_logged)
        {
            return;
        }
        _logged = true;
        string marker;
        string model;
        try
        {
            marker = AppConfig.GetString("model_config_scope") ?? "(unset)";
        }
        catch
        {
            marker = "(read-failed)";
        }
        try
        {
            model = AppConfig.GetModel();
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "(unknown)";
            }
        }
        catch
        {
            model = "(read-failed)";
        }
        Logger.WriteLine(
            "[model-scope] marker model_config_scope=" + marker + " | current model=" + model +
            " | 基线 6faf99b^ 中 MigrateToModelScope 调用点=0，当前 Core 无迁移实现 ⇒ 本次不迁移（无重复迁移风险）");
    }
}
