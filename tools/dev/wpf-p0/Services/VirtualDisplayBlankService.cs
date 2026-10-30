using System;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Display;
using Microsoft.Win32;
using System.Windows.Forms;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// task-20：<c>VirtualDisplayBlank</c>（「息屏（不睡眠）」执行体）的 WPF 生命周期接线。
///
/// <para>基线证据（`6faf99b^`）：类本身在 <c>source/MechrevoLite.Display/VirtualDisplayBlank.cs</c>
/// （693 行，WPF 侧 <c>source/MechrevoLite.Display/VirtualDisplayBlank.cs</c> 与之逐行一致）。旧调用点：</para>
/// <code>
/// Program.cs:979        Task.Run(() =&gt; VirtualDisplayBlank.CleanupResidualOnStartup());   // 启动清残留（崩溃/强杀后虚拟屏挂死）
/// Program.cs:3951       VirtualDisplayBlank.RestoreImmediate();                            // 唤醒/恢复路径
/// SettingsForm.cs:12255 VirtualDisplayBlank.AbortOnSuspend();                              // PBT_APMSUSPEND
/// SettingsForm.cs:13174 VirtualDisplayBlank.RestoreImmediate();                            // StopRuntimeTimers（退出）
/// SettingsForm.cs:13194 VirtualDisplayBlank.RestoreImmediate();                            // DisposeRuntimeResources（退出）
/// SettingsForm.cs:4811  VirtualDisplayBlank.Start();                                       // 「更多开关/磁贴」息屏点击
/// </code>
///
/// <para>WPF 现状（task-20 核对结论）：**类在 Core 里，但 WPF/Core 全库 0 调用**。快速开关 monitoroff 实际链路：
/// <c>OriginalStyleWindow.xaml.cs:2445 QuickTile_Click → BackendProbe.SwitchQuickAsync() → SetSystemQuick()
/// （无 monitoroff 分支）→ MechrevoService.SwitchQuick()（无 monitoroff 分支；MechrevoHw.HasQuickSwitchDispatch
/// 也没有）→ false</c> ⇒ 当前点击「息屏」不生效、启动也不清虚拟屏残留、睡眠/唤醒也不处理。
/// 本服务补齐**生命周期部分**（启动清残留 / 睡眠前 abort / 唤醒与退出 restore），
/// 并给出 <see cref="TryHandleQuickSwitch" /> 供窗口任务在 BackendProbe 里一行接入点击路径。</para>
///
/// <para>⚠️ 接线（本任务不改 App.xaml.cs / BackendProbe.cs；由 Lead 派给窗口任务执行）：</para>
/// <code>
/// // App.OnStartup：取得单实例锁、设置语言/字体之后，创建 OriginalStyleWindow 之前
/// VirtualDisplayBlankService.InitializeStartupCleanup();
/// VirtualDisplayBlankService.AttachPowerEvents();
/// // App.OnExit（DisableLocalMonitoring 附近）
/// VirtualDisplayBlankService.Shutdown();
///
/// // BackendProbe.SwitchQuickAsync() 开头（SetSystemQuick 之前）：
/// if (VirtualDisplayBlankService.TryHandleQuickSwitch(key, on, out bool blankConfirmed))
///     return blankConfirmed;
/// </code>
///
/// <para>⛔ 仅调用既有 Core 类；不改显示拓扑/驱动/配置，不持 ES_DISPLAY_REQUIRED；异常一律吞掉并记日志，不影响启动。</para>
/// </summary>
internal static class VirtualDisplayBlankService
{
    private static bool _powerEventsAttached;

    private static bool _shutdown;

    /// <summary>是否正处于「息屏（不睡眠）」会话中（等价旧版 <c>VirtualDisplayBlank.IsActive</c>）。</summary>
    internal static bool IsActive => VirtualDisplayBlank.IsActive;

    /// <summary>
    /// 启动后后台清一次虚拟显示屏残留（等价旧 <c>Program.cs:979</c> 的 Task.Run）：
    /// 崩溃/被强杀时 OnExit 不执行，usbmmidd 虚拟屏会一直挂在系统里；
    /// 只关本程序自己的 usbmmidd 虚拟屏，⛔ 不切显示拓扑、⛔ 不卸驱动/不删设备节点、⛔ 不阻塞启动。
    /// </summary>
    internal static void InitializeStartupCleanup()
    {
        _ = Task.Run(() =>
        {
            try
            {
                VirtualDisplayBlank.CleanupResidualOnStartup();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[blank] 启动残留清理失败（不影响启动）：" + ex.GetType().Name + " " + ex.Message);
            }
        });
    }

    /// <summary>
    /// 挂电源事件（等价旧 <c>SettingsForm</c> 的 PBT_APMSUSPEND/PBT_APMRESUMEAUTOMATIC 分支）：
    /// Suspend → <c>AbortOnSuspend()</c>（修「唤醒后屏幕反复被关闭」）；Resume → <c>RestoreImmediate()</c>。
    /// WPF 消息泵下 SystemEvents 可用；重复调用幂等。
    /// </summary>
    internal static void AttachPowerEvents()
    {
        if (_powerEventsAttached)
        {
            return;
        }
        _powerEventsAttached = true;
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Logger.WriteLine("[blank] 已挂电源事件（Suspend→AbortOnSuspend / Resume→RestoreImmediate）");
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[blank] 挂电源事件失败（不影响启动）：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            if (e.Mode == PowerModes.Suspend)
            {
                VirtualDisplayBlank.AbortOnSuspend();
                Logger.WriteLine("[blank] 睡眠前清理息屏状态（等价旧 PBT_APMSUSPEND → AbortOnSuspend）");
            }
            else if (e.Mode == PowerModes.Resume)
            {
                VirtualDisplayBlank.RestoreImmediate();
                Logger.WriteLine("[blank] 唤醒后恢复显示（等价旧 PBT_APMRESUMEAUTOMATIC → RestoreImmediate）");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[blank] 电源事件处理失败：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    /// <summary>最近一次息屏拒绝/失败原因（供 UI/OSD 使用）。</summary>
    internal static string LastError { get; private set; } = "";

    /// <summary>只有“单个内屏 + 没有外接屏”才允许息屏。</summary>
    internal static bool IsSingleInternalDisplay(out string reason)
    {
        reason = "";
        try
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length != 1)
            {
                reason = "外接显示器时“息屏（不睡眠）”不可用（仅内屏可用）";
                return false;
            }
            string? laptop = MechrevoLite.Display.ScreenNative.FindLaptopScreen(false);
            if (string.IsNullOrWhiteSpace(laptop))
            {
                reason = "未识别到内屏，已取消息屏";
                return false;
            }
            if (!string.Equals(screens[0].DeviceName, laptop, StringComparison.OrdinalIgnoreCase))
            {
                reason = "当前不是内屏，“息屏（不睡眠）”不可用（仅内屏可用）";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = "显示拓扑检测失败：" + ex.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 窗口任务在 <c>BackendProbe.SwitchQuickAsync</c> 里的一行接入点：处理「息屏（monitoroff）」一次性开关。
    /// 返回 true = 本服务已接管（<paramref name="confirmed"/> 为结果）；返回 false = 不是 monitoroff，走原逻辑。
    /// 「关闭方向」按旧版 CheckBox 一次性语义无动作（旧 <c>ApplyQuickMonitorOffAsync</c> 只在 Checked=true 时执行）。
    /// </summary>
    internal static bool TryHandleQuickSwitch(string key, bool on, out bool confirmed)
    {
        confirmed = false;
        if (!string.Equals(key, "monitoroff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!on)
        {
            return true;
        }
        if (!IsSingleInternalDisplay(out string reason))
        {
            LastError = reason;
            Logger.WriteError("[blank] " + reason);
            try
            {
                L_Mechrevo.WpfP0.ToastService.Show(reason, failure: true);
            }
            catch
            {
            }
            return true;
        }
        string? error = StartBlank();
        LastError = error ?? "";
        if (error != null)
        {
            try
            {
                L_Mechrevo.WpfP0.OsdService.Show("\uE7F4", "息屏", error);
            }
            catch
            {
            }
        }
        confirmed = error == null;
        return true;
    }

    /// <summary>
    /// 启动息屏序列（等价旧 <c>SettingsForm.ApplyQuickMonitorOffAsync</c> 里的 <c>VirtualDisplayBlank.Start()</c>）。
    /// 返回 null = 已启动；返回字符串 = 启动被拒绝的原因（应在 UI 提示，旧版用 MessageBox）。
    /// </summary>
    internal static string? StartBlank()
    {
        try
        {
            string? error = VirtualDisplayBlank.Start();
            if (error != null)
            {
                Logger.WriteLine("SCREEN_BLANK_FAILED: " + error);
            }
            return error;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Screen blank failed: " + ex.Message);
            return ex.Message;
        }
    }

    /// <summary>立即恢复显示（等价旧 <c>VirtualDisplayBlank.RestoreImmediate()</c>；幂等）。</summary>
    internal static void RestoreImmediate()
    {
        try
        {
            VirtualDisplayBlank.RestoreImmediate();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[blank] RestoreImmediate 失败：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    /// <summary>
    /// 退出清理（等价旧 <c>SettingsForm.DisposeRuntimeResources</c> 的 RestoreImmediate + 解挂电源事件）：
    /// 先恢复显示（若仍在息屏），再解挂事件，避免退出后仍持有静态事件引用。
    /// </summary>
    internal static void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }
        _shutdown = true;
        RestoreImmediate();
        if (_powerEventsAttached)
        {
            _powerEventsAttached = false;
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[blank] 解挂电源事件失败：" + ex.GetType().Name + " " + ex.Message);
            }
        }
    }
}
