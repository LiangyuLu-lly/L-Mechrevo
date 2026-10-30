using System;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class App : Application
{
    private static readonly string InstanceMutexName =
        Environment.GetEnvironmentVariable("L_MECHREVO_WPF_INSTANCE") is { Length: > 0 } customInstance
            ? @"Local\" + customInstance
            : @"Local\L-Mechrevo";
    private static readonly string ShowMainEventName = InstanceMutexName + ".ShowMain";
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _showMainEvent;
    private RegisteredWaitHandle? _showMainWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 独立更新器必须先于单实例/UI/硬件初始化处理，避免更新器加载安装目录里的旧 DLL。
        if (L_Mechrevo.WpfP0.Services.DllUpdateHelper.TryHandle(e.Args, out int updateExitCode))
        {
            Environment.ExitCode = updateExitCode;
            Shutdown();
            return;
        }
        // ⭐ task-24：`--ui-probe` 优先无界面处理（等价旧 Program.cs:340-343：必须在单实例互斥体之前，
        //   预览/帮助不建主窗、不连硬件。list=退出 0；未知能力档/窗体=退出 2；正常启动不受影响）。
        if (L_Mechrevo.WpfP0.Services.UiProbeRunnerService.TryHandleHeadless(e.Args, out int uiProbeExitCode))
        {
            Environment.ExitCode = uiProbeExitCode;
            Shutdown();
            return;
        }
        // task-40：JIGUANG 机型已知 NVML 原生 AV（0xc0000005），在采样泵启动前先停用进程内 NVML。
        L_Mechrevo.WpfP0.Services.NvmlSafetyPolicy.Apply();

        bool startupLaunch = false;
        foreach (string arg in e.Args)
        {
            if (string.Equals(arg, "startup", StringComparison.OrdinalIgnoreCase))
            {
                startupLaunch = true;
                break;
            }
        }
        // ⭐ task-22：兼容旧 Program.Main 的无界面启动参数（charge / --register-autostart）。
        //   必须在单实例互斥体之前处理：计划任务/提权注册不应被已有 UI 实例挡掉。
        if (L_Mechrevo.WpfP0.Services.HeadlessStartupService.TryHandle(e.Args, out int headlessExitCode))
        {
            Environment.ExitCode = headlessExitCode;
            Shutdown();
            return;
        }
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        _ownsInstanceMutex = createdNew;
        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowMainEventName, out EventWaitHandle? existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
            }
            Shutdown();
            return;
        }
        _showMainEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMainEventName);
        _showMainWait = ThreadPool.RegisterWaitForSingleObject(
            _showMainEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is OriginalStyleWindow owner)
                {
                    owner.ShowMainFromExternal();
                }
                else if (MainWindow is Window window)
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                }
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        try
        {
            string language = MechrevoLite.AppConfig.GetString("language") ?? "zh-CN";
            CultureInfo uiCulture = CultureInfo.GetCultureInfo(
                language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN");
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
        catch
        {
        }
        // ⭐ task-24：启动诊断日志（等价旧 Program.cs:920-954 顺序：UiQa/Force30Perf/LightCompat/KeyboardCtrl*/
        //   MachineCodeProbe；全部只读，子项内部各自 try/catch，失败绝不影响启动）。
        //   单实例判定之后调用 ⇒ 第二个实例不会再重复打一遍（旧 Program 的同一时序语义）。
        try
        {
            L_Mechrevo.WpfP0.Services.StartupDiagnosticsService.Run();
            // ModelScope：基线 MigrateToModelScope 全库 0 调用（task-20 核对）⇒ 这里只补只读 marker 日志，不迁移。
            L_Mechrevo.WpfP0.Services.ModelScopeDiagnosticsService.LogStatus();
            // UiQa：初始化/缓存开关 + 启动状态行（默认 OFF 也打），只影响显隐（窗口钩子后续任务做）。
            _ = L_Mechrevo.WpfP0.Services.UiQaShowAll.Enabled;
            L_Mechrevo.WpfP0.Services.UiQaShowAll.LogStartupState();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[diag] 启动诊断接线失败（不影响启动）：" + ex.GetType().Name + " " + ex.Message);
        }
        // ⭐ task-24：合法 `--ui-probe <profile>:<form>` 时应用合成能力位预览（旧 UiProbeRunner 语义；无参数时无操作）。
        L_Mechrevo.WpfP0.Services.UiProbeRunnerService.TryApplyPreview(e.Args);
        // ⭐ task-24：息屏生命周期（等价旧 Program.cs:979 / SettingsForm PBT_APMSUSPEND、APMRESUMEAUTOMATIC）：
        //   启动后台清 usbmmidd 虚拟屏残留 + 挂电源事件；退出恢复在 OnExit（不阻塞启动）。
        L_Mechrevo.WpfP0.Services.VirtualDisplayBlankService.InitializeStartupCleanup();
        L_Mechrevo.WpfP0.Services.VirtualDisplayBlankService.AttachPowerEvents();
        ApplyFont();
        // ⭐ task-22：与旧 Program 一致，后台核对/修复启动计划任务（失败只记日志，不阻塞 UI）✅
        _ = System.Threading.Tasks.Task.Run(() => global::Startup.StartupCheck());
        _ = Task.Run(() => MechrevoLite.HardwareControl.EnableLocalMonitoring());
        base.OnStartup(e);
        // 系统 CapsLock / NumLock 变化时弹 LM OSD，并让快捷开关跟随真实状态。
        LockKeyOsdService.Start();
        // 暂不补充九键映射：Fn+SCRLK 只显示 ScrollLock OSD，不改键码。
        // Wi-Fi / 蓝牙：和 OpenRevo 一样用 Windows Radio 状态轮询，
        // 系统里切换也能同步 OSD / 开关状态；只读探测，不改变硬件策略。
        MechrevoLite.Helpers.RadioSwitch.Start();
        OriginalStyleWindow window = new();
        MainWindow = window;
        if (string.Equals(MechrevoLite.AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
        {
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
        }
        else
        {
            window.PositionCompactBottomRight();
        }
        window.Show();
        // ⭐ task-23：后端连接后的 P0 启动恢复（限充核对/风扇启动清理/CPU 自动应用/无界曲线）
        L_Mechrevo.WpfP0.Services.StartupRecoveryService.Schedule();
        if (startupLaunch)
        {
            // ⭐ task-23：旧 startMinimized 语义——启动任务仅驻留托盘，不显示主界面。
            // 在 SystemIdle 执行，确保 OriginalStyleWindow 的 wide 页也已按 last_window_mode 打开后再隐藏。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (Window openWindow in Application.Current.Windows)
                    {
                        openWindow.ShowInTaskbar = false;
                        openWindow.Hide();
                    }
                    Logger.WriteLine("[startup] startup 参数：已静默驻留托盘（启动恢复继续后台执行）");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("[startup] 静默驻留失败（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
                }
            }), System.Windows.Threading.DispatcherPriority.SystemIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ⭐ task-24：退出恢复显示 + 解挂电源事件（等价旧 SettingsForm.DisposeRuntimeResources 的 RestoreImmediate；
        //   幂等：未息屏时内部直接返回，不改变用户可见行为）。
        try
        {
            L_Mechrevo.WpfP0.Services.VirtualDisplayBlankService.Shutdown();
        }
        catch
        {
        }
        try
        {
            MechrevoLite.HardwareControl.DisableLocalMonitoring();
        }
        catch
        {
        }
        _showMainWait?.Unregister(null);
        _showMainEvent?.Dispose();
        if (_ownsInstanceMutex)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch
            {
            }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ApplyFont()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            string regular = Path.Combine(dir, "HarmonyOS_Sans_SC_Regular.ttf");
            string bold = Path.Combine(dir, "HarmonyOS_Sans_SC_Bold.ttf");
            bool installed = UserFontInstaller.EnsureInstalled(regular, bold);
            FontFamily regularFamily;
            FontFamily boldFamily;
            if (installed)
            {
                regularFamily = new FontFamily("HarmonyOS Sans SC");
                boldFamily = new FontFamily("HarmonyOS Sans SC");
            }
            else
            {
                regularFamily = File.Exists(regular)
                    ? new FontFamily(regular + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
                boldFamily = File.Exists(bold)
                    ? new FontFamily(bold + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
            }
            Application.Current.Resources["AppFontFamily"] = regularFamily;
            Application.Current.Resources["AppFontBoldFamily"] = boldFamily;
        }
        catch
        {
        }
    }
}
