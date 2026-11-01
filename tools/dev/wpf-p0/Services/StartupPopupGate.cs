// ⭐ task-27：startup 参数（计划任务启动、静默驻留托盘）下的自动弹窗统一抑制。
// 正常启动/用户操作不受影响；只抑制“非用户触发的自动弹窗/通知”。
using System;

namespace L_Mechrevo.WpfP0.Services;

internal static class StartupPopupGate
{
    private static readonly Lazy<bool> _startupLaunch = new(() =>
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "startup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    });

    /// <summary>本次进程是否由计划任务以 <c>startup</c> 参数启动。</summary>
    internal static bool IsStartupLaunch => _startupLaunch.Value;

    /// <summary>静默启动时是否应抑制自动弹窗/通知（用户主动操作仍可弹）。</summary>
    internal static bool SuppressAutomaticPopup => IsStartupLaunch;
}
