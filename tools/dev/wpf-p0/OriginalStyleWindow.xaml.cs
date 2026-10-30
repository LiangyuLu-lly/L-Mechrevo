using System.Diagnostics;
﻿using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MechrevoLite;
using MechrevoLite.Battery;
using MechrevoLite.Diagnostics;
using MechrevoLite.Display;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Services;
using System.Windows.Interop;
using System.Windows.Threading;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

internal sealed class DragGhostAdorner : Adorner
{
    private readonly VisualBrush _brush;
    private readonly Size _size;
    private Point _position;

    public DragGhostAdorner(UIElement adorned, FrameworkElement source)
        : base(adorned)
    {
        _brush = new VisualBrush(source);
        _size = new Size(Math.Max(1, source.ActualWidth), Math.Max(1, source.ActualHeight));
        IsHitTestVisible = false;
    }

    public void SetPosition(Point point)
    {
        _position = new Point(point.X - _size.Width / 2, point.Y - _size.Height / 2);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.PushOpacity(0.82);
        drawingContext.DrawRectangle(_brush, null, new Rect(_position, _size));
        drawingContext.Pop();
    }
}

public partial class OriginalStyleWindow : Window
{
    private bool _dark = true;
    private bool _syncingBrightness;
    private bool _syncingCalibration;
    private OverlayWindow? _overlayWindow;
    private readonly L_Mechrevo.WpfP0.Services.BackendHost _backendHost = new();
    private MainWindow? _wideWindow;
    private readonly DispatcherTimer _wideWindowCacheTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private readonly DispatcherTimer _miniRenderCacheTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private object? _miniCachedContent;
    private AppServices? _services;
    private IDashboardCommands? _dashboardCommands;
    internal event Action? BackendReady;
    internal BackendProbe? CurrentBackend => _backendProbe;
    private bool _overlayPreloaded;
    private bool _gpuBootChecksStarted;
    private bool _wideMode;
    private TrayIconService? _trayIcon;
    private HotkeyService? _hotkeyService;
    private bool _firstRunChecked;
    private bool _updateCheckStarted;
    private bool _perfRestoreStarted;
    private CustomModeWindow? _customModeWindow;
    private readonly DispatcherTimer _lightingIdleTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _lightingIdleSuspended;
    private long _lastLightingConnectionGeneration;
    private long _lastLightingRestoreTick;
    private readonly long _bootTick = Environment.TickCount64;
    private bool _hidStartupDeferred;
    private readonly bool?[] _pendingChannelPower = new bool?[4];
    private readonly long[] _pendingChannelTick = new long[4];
    private System.Windows.Interop.HwndSource? _powerBroadcastSource;
    // ⭐ task-30：盖子开关监听（独立 message-only 窗口；不挂主窗 WndProc）
    private L_Mechrevo.WpfP0.Services.LidSwitchService? _lidSwitch;
    private bool[]? _lightingIdleSwitchSnapshot;
    private bool _lightingPowerSuspended;
    private bool _sleepKeyboardLightWasOn;
    private bool _lightingPowerBusy;
    private bool _syncingLiquid;
    private WaterCoolerRoutingService? _waterCoolerRouting;
    private bool _syncingLiquidLight;
    private Grid? _liquidLightRow;
    private ComboBox? _liquidLightCombo;
    private Button? _liquidLightColorButton;
    private TextBlock? _liquidLightStatus;
    private bool _syncingLighting;
    private readonly System.Threading.SemaphoreSlim _keyboardLightApplyLock = new(1, 1);
    private readonly System.Threading.SemaphoreSlim _hidApplyLock = new(1, 1);
    private int _hidRetryGeneration;
    private int _lightingReassertGeneration;
    private int _capabilityWarmupGeneration;
    private int _hidApplyGeneration;
    private MechrevoHw? _stateHookedHw;
    private bool? _pendingKeyboardPower;
    private long _pendingKeyboardPowerTick;
    private long _keyboardLightUiLockUntil;
    private bool _syncingPerfConfig;
    private int _pendingMode = -1;
    private int _lastNucEcMode = -2;
    // GCU 性能键：每次按键递增，旧的取样循环（还在等后端回读）看到序号变了就退出，
    // 避免连按时多条 OSD 叠着弹。
    private int _perfKeyWatchSeq;
    private int _lastObservedGcuMode = -2;
    private long _lastPerfKeyOsdTick;
    private long _lastSwitchModeOsdTick;
    private int _lastKeyboardBrightnessOsd = -2;
    private int _lastTouchpadOsd = -2;
    private int _lastFnLockOsd = -2;
    private int _lastMicOsd = -2;
    private long _lastMicHotkeyTick;
    private long _lastAirplaneHotkeyTick;
    private long _lastNumpadHotkeyTick;
    private long _lastBrightnessHotkeyTick;
    private int _lastKbdHotkeyLevel = -1;
    private readonly HashSet<MechrevoLite.Helpers.RadioSwitchKind> _radioOsdPrimed = new();
    private bool _modeSwitchInFlight;
    private int _modeRequestId;
    private long _pendingModeTick;
    private int _lastDbVisualMode = int.MinValue;
    private bool _powerSessionEventsHooked;
    private bool _sessionLocked;
    private bool _lastPowerLineOnline;
    private int _powerSettleGeneration;
    private long _resumeShowSuppressUntil;
    private int _cleanExitMarked;
    private string _customWindowModeId = "";
    private bool _perfConfigBuilt;
    private bool _gpuPromptOpen;
    private bool _syncingDisplayEffect;
    private bool _syncingBatteryLimit;
    private bool _batteryLimitLoaded;
    private bool _xingyaoChargeLoaded;
    private bool? _chargeLimitChannelProbe;
    private long _chargeLimitChannelProbeTick;
    private BackendProbe? _backendProbe;
    private PerformanceKeyService? _performanceKey;
    // 托盘/后台默认 2s；悬浮窗打开时临时提到 1s。
    private readonly DispatcherTimer _backendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    // GCU 性能键兜底：不依赖界面可见性/ACPI 事件，700ms 观察实际档位。
    private readonly DispatcherTimer _gcuModeOsdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _miniHeightSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    private const string MiniHeightConfigKey = "mini_window_height";
    private bool _restoringMiniHeight;
    private bool _syncingMainScrollBar;
    private readonly DispatcherTimer _backendRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    private bool _sensorDemandSuspended;
    private int _sensorDemandBusy;
    private readonly DispatcherTimer _statusResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    private readonly string _statusResetText = L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion.Split('-')[0];
    private bool _connectAttempting;
    private const string QuickTilesConfigKey = "perf_quick_tiles";
    private const string QuickTilesEmptySentinel = "-";
    private static readonly string[] DefaultQuickTileKeys = { "fanboost", "webcam", "micmute", "winkey", "monitoroff" };
    private static readonly (string Key, string Label, string Glyph)[] QuickTileCatalog =
    {
        ("touchpad", QuickSwitchService.Label("touchpad", "触摸板"), "\uE7C9"), ("touchpadtoggle", QuickSwitchService.Label("touchpadtoggle", "触摸板切换键"), "\uE7C9"),
        ("wifi", "WiFi", "\uE701"), ("bt", QuickSwitchService.Label("bt", "蓝牙"), "\uE702"),
        ("webcam", QuickSwitchService.Label("webcam", "摄像头"), "\uE722"), ("micmute", QuickSwitchService.Label("micmute", "麦克风"), "\uE720"),
        ("numpad", QuickSwitchService.Label("numpad", "小键盘锁"), "\uE765"), ("winkey", QuickSwitchService.Label("winkey", "Win键锁"), "\uE765"),
        ("fnkey", QuickSwitchService.Label("fnkey", "Fn键锁"), "\uE765"), ("capslock", QuickSwitchService.Label("capslock", "大小写锁定"), "\uE8AB"),
        ("osd", QuickSwitchService.Label("osd", "OSD提示"), "\uE7F4"), ("gcuosd", "GCU-OSD", "\uE7F4"), ("copilot", QuickSwitchService.Label("copilot", "Copilot键锁"), "\uE8B7"),
        ("usb", QuickSwitchService.Label("usb", "关机USB供电"), "\uE88E"), ("acrecovery", QuickSwitchService.Label("acrecovery", "来电自启"), "\uE7E8"),
        ("highperf", QuickSwitchService.Label("highperf", "高性能电源"), "\uE945"), ("apuboost", QuickSwitchService.Label("apuboost", "APU 防缩缸"), "\uE9D9"),
        ("fanboost", QuickSwitchService.Label("fanboost", "强冷"), "\uE9CA"), ("deepsleep", QuickSwitchService.Label("deepsleep", "深度睡眠"), "\uE708"),
        ("gamewhitelist", QuickSwitchService.Label("gamewhitelist", "游戏白名单"), "\uE7FC"), ("cpuadvperf", QuickSwitchService.Label("cpuadvperf", "解锁BIOS菜单"), "\uE7EF"),
        ("transparency", QuickSwitchService.Label("transparency", "系统透明效果"), "\uE790"),
        ("darktheme", QuickSwitchService.Label("darktheme", "系统深色主题"), "\uE793"), ("monitoroff", QuickSwitchService.Label("monitoroff", "息屏"), "\uE7F4"),
        ("winupdate", QuickSwitchService.Label("winupdate", "禁用Win更新"), "\uE895")
    };

    /// <summary>复用快捷磁贴目录里的图标，供 OSD 等复用，避免另造一套图标。</summary>
    internal static string QuickGlyph(string key)
    {
        foreach ((string Key, string Label, string Glyph) item in QuickTileCatalog)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return item.Glyph;
            }
        }
        return "\uE7F4";
    }

    public OriginalStyleWindow()
    {
        InitializeComponent();
        L_Mechrevo.WpfP0.Services.WindowWorkAreaPolicy.ClampOnLoaded(this);
        RestoreSavedMiniHeight();
        _miniHeightSaveTimer.Tick += (_, _) =>
        {
            _miniHeightSaveTimer.Stop();
            SaveMiniHeight();
        };
        SizeChanged += (_, _) => ScheduleMiniHeightSave();
        _statusResetTimer.Tick += (_, _) =>
        {
            _statusResetTimer.Stop();
            if (StatusText != null)
            {
                StatusText.Text = _statusResetText;
            }
        };
        System.ComponentModel.DependencyPropertyDescriptor? statusTextDescriptor =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        statusTextDescriptor?.AddValueChanged(StatusText, (_, _) => ScheduleStatusReset());
        Loaded += (_, _) => ScheduleStatusReset();
        Closed += (_, _) =>
        {
            _statusResetTimer.Stop();
            SaveMiniHeight();
        };
        _dark = ResolveStartupDarkTheme();
        AppTheme.Apply(_dark);
        ApplySavedAccent();
        ThemeButton.Content = _dark ? WpfStrings.LightTheme : WpfStrings.DarkTheme;
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFontFamily"];
        InitializeQuickTileDrag();
        HookPowerSessionEvents();
        InitializeLidSwitchMonitor();
        Closed += (_, _) =>
        {
            _powerBroadcastSource?.RemoveHook(PowerBroadcastHook);
            _lidSwitch?.Dispose();
            _lidSwitch = null;
            UnhookPowerSessionEvents();
            MarkCleanExitForShutdown("窗口 Closed");
            if (_stateHookedHw != null)
            {
                _stateHookedHw.StateChanged -= OnHardwareLightingStateChanged;
                _stateHookedHw = null;
            }
        };
        SourceInitialized += (_, _) =>
        {
            HideMinMaxButtons();
            ApplyDarkTitleBar(_dark);
            ApplyRoundedCorners();
        };
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        MigrateButtonOpacityDefault();
        ApplyButtonOpacity();
        _gcuModeOsdTimer.Tick += (_, _) => PollGcuModeOsd();
        _gcuModeOsdTimer.Start();
        LockKeyOsdService.Changed += OnLockKeyChanged;
        MechrevoLite.Helpers.RadioSwitch.StateChanged += OnRadioStateChanged;
        _backendTimer.Tick += (_, _) =>
        {
            if (_backendProbe == null)
            {
                return;
            }
            bool consumer = HasVisibleSensorConsumer();
            ApplySensorDemandState(consumer);
            if (!consumer)
            {
                _trayIcon?.SetTooltip("L-Mechrevo");
                L_Mechrevo.WpfP0.Services.MemoryTrimService.TrimIfIdle(idle: true);
                return;
            }
            RefreshFromBackend();
        };
        _lightingIdleTimer.Tick += (_, _) => CheckLightingIdle();
        _lightingIdleTimer.Start();
        _backendRetryTimer.Tick += async (_, _) =>
        {
            if (_backendProbe == null)
            {
                await ConnectBackendAsync();
            }
            else
            {
                _backendRetryTimer.Stop();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool consumer = HasVisibleSensorConsumer();
                ApplySensorDemandState(consumer);
                if (!consumer)
                {
                    L_Mechrevo.WpfP0.Services.MemoryTrimService.TrimIfIdle(idle: true);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
        Closed += (_, _) =>
        {
            _backendTimer.Stop();
            ApplySensorDemandState(consumer: false);
            _backendRetryTimer.Stop();
            _lightingIdleTimer.Stop();
            _waterCoolerRouting?.Dispose();
            _waterCoolerRouting = null;
            _backendProbe?.Dispose();
            _backendProbe = null;
            _backendHost.Dispose();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _hotkeyService?.Dispose();
            _hotkeyService = null;
        };
        MechrevoLite.Helpers.ToastBridge.FailureCallback = message =>
        {
            if (StartupPopupGate.SuppressAutomaticPopup)
            {
                Logger.WriteLine("[startup] startup 参数：抑制错误 Toast：" + message);
                return;
            }
            ToastService.Show(message, failure: true);
        };
        MechrevoLite.Helpers.ToastBridge.NoticeCallback = message =>
        {
            if (StartupPopupGate.SuppressAutomaticPopup)
            {
                Logger.WriteLine("[startup] startup 参数：抑制通知 Toast：" + message);
                return;
            }
            ToastService.Show(message);
        };
        _trayIcon = new TrayIconService();
        _trayIcon.SetTheme(_dark);
        _trayIcon.ShowRequested += ShowFromTray;
        _trayIcon.SettingsRequested += () => new SettingsWindow(this).ShowDialog();
        _trayIcon.OverlayRequested += ToggleTrayOverlay;
        _trayIcon.OverlayLockRequested += ToggleTrayOverlayLock;
        _trayIcon.ExitRequested += () =>
        {
            Close();
            Application.Current.Shutdown();
        };
        _trayIcon.ModeRequested += RequestMode;
        _trayIcon.StateProvider = BuildTrayMenuState;
        _trayIcon.FanBoostRequested += () =>
        {
            if (_backendProbe?.Hw != null)
            {
                _ = _services != null
                    ? _services.Performance.SwitchFanBoostRawAsync(!(_backendProbe.Hw.FanBoost))
                    : _backendProbe.SwitchQuickAsync("fanboost", !(_backendProbe.Hw.FanBoost));
            }
        };
        _trayIcon.KeyboardLightingRequested += () => KeyboardLightEdit_Click(this, new RoutedEventArgs());
        _trayIcon.DiagnosticsRequested += () => _ = ExportDiagnosticPackAsync();
        _trayIcon.GpuModeRequested += mode =>
        {
            ToggleButton? target = mode switch
            {
                0 => GpuEcoButton,
                1 => GpuHybridButton,
                2 => GpuDirectButton,
                _ => null
            };
            if (target != null)
            {
                GpuMode_Click(target, new RoutedEventArgs());
            }
        };
        _trayIcon.CustomModeRequested += id => _ = ActivateTrayCustomAsync(id);
        _trayIcon.SilentTurboRequested += () => _ = TraySilentTurboAsync();
        Loaded += (_, _) => RestoreOverlayVisible();
        // 悬浮窗改为按需创建：启动期不再预加载，降低常驻内存。
        SourceInitialized += (_, _) =>
        {
            try
            {
                _hotkeyService = new HotkeyService(this);
                _hotkeyService.ModeRequested += RequestMode;
                _hotkeyService.OverlayRequested += ToggleTrayOverlay;
                _hotkeyService.SettingsRequested += () => new SettingsWindow(this).ShowDialog();
                _hotkeyService.ShowRequested += ShowFromTray;
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text + ex.Message;
            }
        };
        if (ModeGrid != null && ModeGrid.Children.Count > 1 && ModeGrid.Children[1] is ToggleButton defaultMode)
        {
            defaultMode.IsChecked = true;
            UpdateModeBorders(defaultMode);
            HighlightSegment(GpuDirectButton);
            HighlightSegment(BatteryLongButton);
        }
        MainScroll.SizeChanged += delegate
        {
            UpdateContentWidth();
            SyncMainScrollBar();
        };
        MainScroll.ScrollChanged += (_, _) => SyncMainScrollBar();
        SourceInitialized += (_, _) =>
        {
            WindowBackdrop.Apply(this);
            ApplyBackdropBackgroundState();
            _powerBroadcastSource = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            _powerBroadcastSource?.AddHook(PowerBroadcastHook);
        };
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
            ApplySavedQuickTiles();
            ApplyMoreSectionState();
            ApplySectionCollapseStates();
            RefreshBrightness();
            InitBatteryControls();
            InitLightingControls();
            NormalizeSegmentDefaults();
            ApplyDarkTitleBar(_dark);
            ApplyRoundedCorners();
            bool backgroundEnabled = MechrevoLite.AppConfig.Is("ui_bg_enabled");
            BackgroundToggle.IsChecked = backgroundEnabled;
            UpdateBackgroundVisibility();
            if (backgroundEnabled)
            {
                ReloadBackgroundImage();
            }
            _ = Dispatcher.BeginInvoke(new Action(() => _ = ConnectBackendAsync()), System.Windows.Threading.DispatcherPriority.Background);
            // 静默开机启动只驻留托盘，不在这里弹宽屏；否则会出现“宽屏一闪又消失”。
            if (!L_Mechrevo.WpfP0.Services.StartupPopupGate.IsStartupLaunch
                && string.Equals(AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
            {
                _ = Dispatcher.BeginInvoke(new Action(ShowWideWindow), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        };
    }

    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    private void TitleClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private bool _customModeCycleBusy;

    /// <summary>
    /// Ctrl+Alt+4：连续按依次循环「自定义 1/2/3...」，每次都按保存的配置重新下发一次，
    /// OSD 标题固定为「性能模式」，内容显示该自定义档的名字（不是笼统的“自定义”）。
    /// </summary>
    private async Task CycleCustomModeFromHotkeyAsync()
    {
        if (_customModeCycleBusy)
        {
            return;
        }
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        if (perf == null)
        {
            return;
        }
        List<MechrevoLite.Mode.PerfModeDefinition> customs = perf.Modes
            .Where(mode => mode.IsCustom)
            .ToList();
        if (customs.Count == 0)
        {
            RequestMode(3);
            return;
        }
        string currentId = perf.DisplayedModeId;
        int currentIndex = customs.FindIndex(mode => string.Equals(mode.Id, currentId, StringComparison.Ordinal));
        MechrevoLite.Mode.PerfModeDefinition target = customs[(currentIndex + 1) % customs.Count];
        _customModeCycleBusy = true;
        try
        {
            MechrevoLite.Mode.PerfApplyOutcome? outcome = await perf.ActivateAsync(
                target.Id, "hotkey custom cycle", forceParameters: true);
            if (outcome != null)
            {
                _pendingMode = 3;
                _pendingModeTick = Environment.TickCount64;
                RefreshFromBackend();
                OsdService.Show(string.Empty, WpfStrings.OsdPerformance, MechrevoLite.Mode.PerfModeText.Name(target));
            }
            else
            {
                StatusText.Text = WpfStrings.MainWindow_xaml_Text_20 + target.Id;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_20 + ex.Message;
        }
        finally
        {
            _customModeCycleBusy = false;
        }
    }

    private void RequestMode(int mode)
    {
        if (_backendProbe == null)
        {
            return;
        }
        _pendingMode = mode;
        _pendingModeTick = Environment.TickCount64;
        _ = SwitchModeAsync(mode);
    }

    internal bool WideSilentTurboVisible => ShouldShowSilentTurbo(_backendProbe?.Capabilities, _backendProbe?.Hw);

    internal bool WideTurboVisible => ShouldShowTurboMode(_backendProbe?.Capabilities, _backendProbe?.Hw);

    /// <summary>
    /// 50 系默认显示静音狂暴/高能狂暴；其他机型默认显示狂暴。
    /// 只有 GCU 明确上报不支持（ProfileAvailable=true 且对应位=0）才隐藏，
    /// 避免首次装 GCU 服务未完全启动时把能力位误判为“不支持”。
    /// </summary>
    private static bool ShouldShowTurboMode(MechrevoDeviceCapabilities? caps, MechrevoHw? hw)
    {
        if (UiQaShowAll.Enabled) return true;
        // 星耀 PTL 的三档由 WMAA 提供；GCU WJXY 的 TurboModeSupport=0 不能拿来做 UI 判据。
        if (MechrevoLite.Hardware.XingyaoPerformanceMode.IsApplicable(hw)) return true;
        if (caps == null) return true;
        if (!caps.ProfileAvailable) return true;
        if (caps.TurboMode) return true;
        // 宝龙达/特殊机型仍按已有能力位处理
        return hw?.UsesBaolongda == true;
    }

    private static bool ShouldShowSilentTurbo(MechrevoDeviceCapabilities? caps, MechrevoHw? hw)
    {
        if (UiQaShowAll.Enabled) return true;
        if (caps == null) return hw?.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen50;
        bool gen50 = hw?.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen50;
        return caps.SilentTurboAvailability switch
        {
            MechrevoLite.Hardware.FeatureAvailability.Supported => true,
            MechrevoLite.Hardware.FeatureAvailability.Unsupported => false,
            _ => gen50
        };
    }

    internal int WideCurrentMode => _backendProbe != null ? ResolveDisplayMode(_backendProbe) : VisualModeForId(MechrevoLite.Mode.PerfModeStore.ActiveModeId);

    internal bool WideSilentTurboActive => WideCurrentMode == 1 && WideSilentTurboVisible
        && MechrevoService.IsSilentTurboActive;

    internal void WideSetSilentTurbo()
    {
        _ = SwitchModeAsync(1, true);
    }

    internal string WideCustomModeId => ResolveCustomModeId();

    internal void WideActivateCustomMode()
    {
        _ = WideActivateCustomModeAsync();
    }

    private async Task WideActivateCustomModeAsync()
    {
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        string id = ResolveCustomModeId();
        if (perf != null && !string.IsNullOrWhiteSpace(id)
            && id.StartsWith("custom", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                MechrevoLite.Mode.PerfApplyOutcome? outcome = await perf.ActivateAsync(id, "wide-home", forceParameters: true);
                if (outcome?.ModeSwitched == true)
                {
                    RefreshFromBackend();
                    RefreshWideCustomEditor();
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[perfmode] 宽屏切自定义失败，回退 ChangeMode(3): " + ex.Message);
            }
        }
        await SwitchModeAsync(3);
        RefreshWideCustomEditor();
    }

    internal void RefreshWideCustomEditor()
    {
        _wideWindow?.RefreshCustomModeEditor();
    }

    internal void WideSetMode(int mode)
    {
        RequestMode(mode);
    }

    internal void WideSetGpuMode(int mode)
    {
        ToggleButton? target = mode switch
        {
            0 => GpuEcoButton,
            1 => GpuHybridButton,
            2 => GpuDirectButton,
            _ => null
        };
        if (target != null)
        {
            GpuMode_Click(target, new RoutedEventArgs());
        }
    }

    internal async void WideSetRefresh(int hertz)
    {
        if (_backendProbe == null)
        {
            return;
        }
        // 旧版定稿：刷新率唯一动作走 Win32（ChangeDisplaySettingsEx），GCU 仅 best-effort 镜像；
        // 手选档位不关闭“自动刷新率”，只提示可能被插拔电源覆盖。
        bool autoOn = AppConfig.Is("screen_auto");
        bool ok;
        try
        {
            // 对当前活动显示设备直接调用 Win32 ChangeDisplaySettingsEx；
            // 不再先 FindLaptopScreen 把外接屏场景导到错误目标。
            ScreenNative.SetRefreshRate(null, hertz);
            ok = true;
        }
        catch (Exception ex)
        {
            ok = false;
            Logger.WriteLine("[refresh] Win32 切换失败：" + ex.Message);
        }
        if (ok)
        {
            // 旧版：ChangeDisplaySettingsEx 后模式异步落地，短重试回读；对不上目标时仍高亮用户点的那档。
            int readback = -1;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(150);
                }
                readback = MechrevoLite.Display.ScreenNative.GetRefreshRate(null);
                if (readback > 0 && Math.Abs(readback - hertz) <= 2)
                {
                    break;
                }
            }
            int highlight = readback > 0 && Math.Abs(readback - hertz) <= 2 ? readback : hertz;
            SelectMiniRefreshButton(highlight);
        }
        if (_backendProbe.Hw?.IsGcuConnected == true)
        {
            _ = _backendProbe.SwitchRefreshRateAsync(hertz);
        }
        if (autoOn && ok)
        {
            ToastService.Show(WpfStrings.DisplayRefreshAutoWarn);
        }
        StatusText.Text = ok
            ? (autoOn ? WpfStrings.DisplayRefreshSentPrefix + hertz + " Hz · " + WpfStrings.DisplayRefreshAutoWarn : WpfStrings.DisplayRefreshSentPrefix + hertz + " Hz")
            : WpfStrings.DisplayRefreshFailedPrefix + hertz + " Hz";
        SyncRefreshRateState();
    }

    internal (string Effect, string Name)[] WideKeyboardEffects()
    {
        try
        {
            MechrevoHw? hw = WpfBackend.Hw;
            if (hw != null && IsHidKeyboard(hw))
            {
                return LightingCatalog.VisibleHidEffects(hw.Capabilities.KeyboardType)
                    .Select(item => ("hid:" + item.Mode, item.Name))
                    .ToArray();
            }
            int type = hw?.Capabilities.KeyboardType ?? 0;
            (string Id, string Label)[] firmware = MechrevoLite.Hardware.KeyboardFirmwareEffects.Visible(type)
                .Select(item => (item.Id, item.Label))
                .ToArray();
            if (firmware.Length == 0)
            {
                // 白色背光键盘没有固件灯效，仍保留电源/亮度入口。
                return new[] { ("Single", WpfStrings.LightSingleWhiteBacklight) };
            }
            return firmware;
        }
        catch
        {
            return Array.Empty<(string, string)>();
        }
    }

    internal async Task<bool> WideApplyWhiteBacklightAsync(int level, bool on)
    {
        MechrevoService? service = _backendProbe?.Service ?? WpfBackend.Service;
        if (service == null)
        {
            return false;
        }
        try
        {
            if (!on)
            {
                return _backendProbe != null
                    ? await _backendProbe.SetKeyboardPowerAsync(false)
                    : await service.SetKeyboardPower(false);
            }
            if (_backendProbe != null)
            {
                await _backendProbe.SetKeyboardPowerAsync(true);
            }
            int hardwareLevel = level >= 2 ? 4 : 2;
            return await service.SetKeyboardBrightnessPreservingEffect(hardwareLevel);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWhiteBacklightForOsd(MechrevoHw? hw)
    {
        try
        {
            if (hw == null || !hw.SupportsKeyboard || hw.UsesBaolongda || IsHidKeyboard(hw))
            {
                return false;
            }
            if (hw.WujieSmuApu)
            {
                return true;
            }
            return MechrevoLite.Hardware.KeyboardFirmwareEffects.Visible(hw.Capabilities.KeyboardType).Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGradeLightTopicForOsd(string topic, MechrevoHw? hw)
    {
        return (string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
                || string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal))
            && hw != null
            && !hw.UsesBaolongda
            && hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40;
    }

    internal static string LightChannelTitle(string topic)
    {
        return topic switch
        {
            "Keyboard/Ctrl" => WpfStrings.LightingKeyboard,
            "HidLightbar/Ctrl" => WpfStrings.LightingStrip,
            "HidLightbar_Logo/Ctrl" => WpfStrings.LightingLogo,
            "HidLightbar_Hinge/Ctrl" => WpfStrings.LightingHinge,
            "HidLightbar_Sync/Ctrl" => WpfStrings.LightingSync,
            _ => WpfStrings.OsdLighting
        };
    }

    internal static string LightChannelGlyph(string topic)
    {
        // 键盘 / 灯带 / Logo / 铰链 / 同步使用各自图标，避免全部套显示器图标。
        return topic switch
        {
            "Keyboard/Ctrl" => "\uE765",
            "HidLightbar/Ctrl" => "\uE706",
            "HidLightbar_Logo/Ctrl" => "\uE82F",
            "HidLightbar_Hinge/Ctrl" => "\uE781",
            "HidLightbar_Sync/Ctrl" => "\uE895",
            _ => "\uE706"
        };
    }

    internal static string LightLevelText(string topic, int light, bool white)
    {
        if (white)
        {
            return Math.Clamp(light, 1, 2) + "/2";
        }
        if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal)
            || IsGradeLightTopicForOsd(topic, WpfBackend.Hw))
        {
            return Math.Clamp(light, 0, 4) + "/4";
        }
        return Math.Clamp(light * 25, 0, 100) + "%";
    }

    internal async Task<bool> WideApplyLightAsync(string topic, string effect, int light, int speed, int colorArgb, bool on, bool channelPowerOsd = false)
    {
        MechrevoHw? hw = _backendProbe?.Hw;
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return false;
        }
        try
        {
            // 开关只报通道名+开/关；亮度/颜色/速度才报档位，避免开关也弹“亮度 1/4”。
            if (channelPowerOsd)
            {
                OsdService.Show(LightChannelGlyph(topic), LightChannelTitle(topic),
                    on ? WpfStrings.StateOn : WpfStrings.StateOff);
            }
            else
            {
                bool osdWhite = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal) && IsWhiteBacklightForOsd(hw);
                OsdService.Show(LightChannelGlyph(topic), WpfStrings.OsdLighting,
                    on ? LightLevelText(topic, light, osdWhite) : WpfStrings.StateOff);
            }
            if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
            {
                if (IsHidKeyboard(hw) && effect.StartsWith("hid:", StringComparison.Ordinal))
                {
                    int hidMode = int.TryParse(effect.AsSpan(4), out int parsedMode) ? parsedMode : 1;
                    KeyboardRgb? rgb = EnsureKeyboardRgb();
                    if (rgb == null)
                    {
                        return false;
                    }
                    rgb.Brightness = Math.Clamp(light <= 4 ? light * 25 : light, 1, 100);
                    System.Drawing.Color hidColor = System.Drawing.Color.FromArgb(colorArgb);
                    // HID 每模式的速度/颜色由 HID 编辑器维护；宽屏卡只切模式，⛔ 不用固件速度覆盖。
                    switch (hidMode)
                    {
                        case 0:
                            rgb.BreathColor = hidColor;
                            break;
                        case 3:
                            rgb.ReactiveColor = hidColor;
                            break;
                        case 9:
                            rgb.StaticColor = hidColor;
                            break;
                    }
                    return await ApplyHidKeyboardAsync(hidMode, on);
                }
                if (!on)
                {
                    return await probe.SetKeyboardPowerAsync(false);
                }
                await probe.SetKeyboardPowerAsync(true);
                System.Drawing.Color? keyboardColor = (hw?.UsesBaolongda == true
                    || string.Equals(effect, "Single", StringComparison.Ordinal))
                    ? System.Drawing.Color.FromArgb(colorArgb)
                    : null;
                MechrevoService? keyboardService = probe.Service;
                if (keyboardService == null)
                {
                    return false;
                }
                bool effectOk = await keyboardService.SetKeyboardEffect(effect, light, speed, "None", keyboardColor, true);
                // 键盘亮度是独立命令：只发 SetEffectALL 时部分固件/GCU 不更新亮度。
                if (effectOk && on && light > 0)
                {
                    await keyboardService.SetKeyboardBrightnessPreservingEffect(light);
                }
                return effectOk;
            }

            bool supported = topic switch
            {
                "HidLightbar/Ctrl" => probe.SupportsLightbar,
                "HidLightbar_Logo/Ctrl" => hw?.SupportsLogoLight == true,
                "HidLightbar_Hinge/Ctrl" => probe.SupportsHingeLight,
                "HidLightbar_Sync/Ctrl" => probe.SupportsSyncLight,
                _ => false
            };
            if (!supported)
            {
                return false;
            }
            if (!on)
            {
                return await probe.SetLightPowerAsync(topic, false);
            }
            await probe.SetLightPowerAsync(topic, true);
            MechrevoService? lightService = probe.Service;
            if (lightService == null)
            {
                return false;
            }
            System.Drawing.Color? lightColor = MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleColorEffect(effect)
                ? System.Drawing.Color.FromArgb(colorArgb)
                : null;
            if (string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
                && hw?.IsIntelNucMachine == true)
            {
                // NUC 原厂 ApplySetting 对 Monocolor / Rainbow 都会写 R/G/B；
                // 彩虹/混色的亮度、混色也必须把当前颜色带下去。
                if (effect is not "Off" and not "None" and not "")
                {
                    lightColor = System.Drawing.Color.FromArgb(colorArgb);
                }
                bool rawOk = probe.ApplyIntelNucLightbar(effect, light, lightColor);
                Logger.WriteError("[umi-lightbar] apply topic=" + topic + " effect=" + effect + " light=" + light + " raw=" + rawOk);
                return rawOk;
            }
            return await lightService.SetLightEffect(topic, effect, light, speed, "None", lightColor, true);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> WideApplyKeyboardEffectAsync(string effect, bool on)
    {
        try
        {
            if (effect.StartsWith("hid:", StringComparison.Ordinal))
            {
                int mode = int.TryParse(effect.AsSpan(4), out int parsed) ? parsed : 1;
                return await ApplyHidKeyboardAsync(mode, on);
            }
            if (_backendProbe == null)
            {
                return false;
            }
            _keyboardLightUiLockUntil = Environment.TickCount64 + 5000;
            if (on)
            {
                await _backendProbe.SetKeyboardPowerAsync(true);
            }
            return await SetKeyboardEffectServiceAsync(effect);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>宽屏/迷你窗共享命令入口（第二步命令收口）。</summary>
    internal IDashboardCommands DashboardCommands =>
        _dashboardCommands ??= new DashboardCommands(_services, _backendProbe, () => EnsureWaterCoolerRouting());

    internal async Task<bool> WideToggleQuickAsync(string key)
    {
        return await DashboardCommands.ToggleQuickAsync(key);
    }

    internal string WidePerformanceText
    {
        get
        {
            try
            {
                return CurrentModeText.Text;
            }
            catch
            {
                return WpfStrings.CurrentPrefix + "—";
            }
        }
    }

    internal string WideGpuModeText
    {
        get
        {
            try
            {
                if (GpuEcoButton.IsChecked == true) return WpfStrings.CurrentPrefix + WpfStrings.GpuEcoMode;
                if (GpuDirectButton.IsChecked == true) return WpfStrings.CurrentPrefix + WpfStrings.GpuDirectMode;
                if (GpuHybridButton.IsChecked == true) return WpfStrings.CurrentPrefix + WpfStrings.GpuHybridMode;
                return WpfStrings.CurrentPrefix + "—";
            }
            catch
            {
                return WpfStrings.CurrentPrefix + "—";
            }
        }
    }

    internal (bool Enabled, (string Label, object? Tag)[] Options, int Selected) WideCalibration()
    {
        List<(string, object?)> options = new();
        foreach (object item in CalibrationCombo.Items)
        {
            if (item is ComboBoxItem comboItem)
            {
                options.Add((comboItem.Content?.ToString() ?? "", comboItem.Tag));
            }
        }
        return (CalibrationSwitch.IsChecked == true, options.ToArray(), CalibrationCombo.SelectedIndex);
    }

    internal void WideSetCalibrationEnabled(bool enabled)
    {
        CalibrationSwitch.IsChecked = enabled;
        CalibrationSwitch_Click(CalibrationSwitch, new RoutedEventArgs());
    }

    internal void WideSetCalibrationIndex(int index)
    {
        if (index >= 0 && index < CalibrationCombo.Items.Count)
        {
            CalibrationCombo.SelectedIndex = index;
        }
    }

    internal void WideOpenCalibrationSettings()
    {
        CalibrationSettings_Click(CalibrationSettingsButton, new RoutedEventArgs());
    }

    internal (bool Enabled, (string Label, object? Tag)[] Options, int Selected) WideDisplayEffect()
    {
        List<(string, object?)> options = new();
        foreach (object item in DisplayEffectCombo.Items)
        {
            if (item is ComboBoxItem comboItem)
            {
                options.Add((comboItem.Content?.ToString() ?? "", comboItem.Tag));
            }
        }
        return (DisplayEffectSwitch.IsChecked == true, options.ToArray(), DisplayEffectCombo.SelectedIndex);
    }

    internal void WideSetDisplayEffectEnabled(bool enabled)
    {
        DisplayEffectSwitch.IsChecked = enabled;
        DisplayEffectSwitch_Click(DisplayEffectSwitch, new RoutedEventArgs());
    }

    internal string WideSelectedDisplayEffectMode()
    {
        try
        {
            return SelectedDisplayEffectMode();
        }
        catch
        {
            return "";
        }
    }

    internal void WideSetDisplayEffectIndex(int index)
    {
        if (index >= 0 && index < DisplayEffectCombo.Items.Count)
        {
            DisplayEffectCombo.SelectedIndex = index;
        }
    }

    /// <summary>宽屏点选色彩增强档位：立即下发，不依赖 ComboBox 的 SelectionChanged 是否被抑制。</summary>
    internal async Task WideApplyDisplayEffectModeAsync(int index)
    {
        if (_backendProbe == null || index < 0 || index >= DisplayEffectCombo.Items.Count)
        {
            return;
        }
        _syncingDisplayEffect = true;
        DisplayEffectCombo.SelectedIndex = index;
        _syncingDisplayEffect = false;
        await ApplySelectedDisplayEffectModeAsync();
    }

    internal void WideOpenDisplayEffectEditor()
    {
        DisplayEffectEdit_Click(DisplayEffectEditButton, new RoutedEventArgs());
    }

    internal (bool Toggle, int Value) WideBatteryLimitSlider()
    {
        return (ChargeSliderToggle.IsChecked == true, (int)Math.Round(BatteryLimitSlider.Value));
    }

    internal void WideSetBatteryLimitSlider(int value)
    {
        BatteryLimitSlider.Value = Math.Clamp(value, 40, 100);
    }

    internal event Action? WideUiVisibilityChanged;

    internal bool WideIgpuSavingSupported => _backendProbe?.Hw?.SupportsGpuHotSwap == true;

    internal int WideIgpuSavingMode => _backendProbe?.Hw?.IgpuOnlySaveMode ?? -1;

    internal async Task WideSetIgpuSavingAsync(string mode)
    {
        await SwitchIgpuSavingAsync(mode);
    }

    internal bool WideDisplayEffectSupported
    {
        get
        {
            try
            {
                return _backendProbe?.Hw?.IsIntelNucMachine == true
                    || _backendProbe?.Hw?.IsUmiMachine == true
                    || _backendProbe?.Hw?.DisplayEffectProfiles.Count > 0
                    || _backendProbe?.Capabilities?.ColorCalibration == true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 充电阈值 UI 分流：宝龙达白名单直接可用；其它机型只有 EC 通道**真实可读**时才显示，
    /// 避免出现“能点但读取失败：需要 PawnIO/本机型不支持”的控件。
    /// </summary>
    private bool IsChargeLimitChannelAvailable(MechrevoHw? hw)
    {
        if (hw == null)
        {
            return false;
        }
        try
        {
            if (MechrevoLite.BatteryLimitPanel.IsSupportedBoard(hw))
            {
                return true;
            }
            if (!MechrevoLite.Hardware.EcChargeLimit.IsAvailableOnThisMachine())
            {
                return false;
            }
            long now = Environment.TickCount64;
            if (_chargeLimitChannelProbe is bool cached && now - _chargeLimitChannelProbeTick < 10000)
            {
                return cached;
            }
            bool readable = MechrevoLite.Hardware.BatteryLimitWriter.TryReadState(out _, out _, out _, out _);
            _chargeLimitChannelProbe = readable;
            _chargeLimitChannelProbeTick = now;
            return readable;
        }
        catch
        {
            return false;
        }
    }

    internal bool WideChargeLimitVisible
    {
        get
        {
            try
            {
                // 宝龙达阈值面板（BatteryLimitWriter）与 GCU 的 EC 限充是两套：这里只回答宝龙达面板。
                return _backendProbe?.Hw != null && MechrevoLite.BatteryLimitPanel.IsSupportedBoard(_backendProbe.Hw);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ⭐⭐ 2026-10-26（task-18）：电池「无极限充（实验行 ＋ 滑块）」是否应显示/创建 ✅
    /// <para>⭐ 判据与旧 WinForms <c>SettingsForm.ChargeSliderSupported()</c> 逐条对齐：</para>
    /// <list type="number">
    /// <item>⭐ 10/16/20/30 系（GcuVariant 家族 <c>1020</c> / <c>30</c>）写 EC 可能损坏硬件 ⇒ 不建行
    /// （<see cref="EcChargeLimit.IsLegacyGenerationBlocked" />，与 EC 侧同一判据）；</item>
    /// <item>⭐ 无界只有 **8040 系**开放（其它无界如 7040 系不建此开关/滑块）；</item>
    /// <item>⭐ 显卡代际 **Gen30 / Unknown** ⇒ 不建行；Gen40 / Gen50 / NoDgpu ⇒ 开放。</item>
    /// </list>
    /// <para>⚠️ 这里**不读** <c>ec_charge_limit</c> 当前值：那是「关闭 EC 直写」开关自己的状态，
    /// 不能反过来决定本行是否存在（否则勾上就丢掉撤销入口）。</para>
    /// <para>⚠️ P0 首页充电阈值卡片本身只对宝龙达白名单板开放（<see cref="WideChargeLimitVisible" />）⇒
    /// 非宝龙达 GCU 机型连卡片都不建，实验行自然一并隐藏（保留 GCU 三档按钮，行为不变）。</para>
    /// </summary>
    internal bool WideChargeExperimentSupported
    {
        get
        {
            try
            {
                if (!WideChargeLimitVisible && !WideEcTransportSupported)
                {
                    return false;
                }
                if (MechrevoLite.Hardware.EcChargeLimit.IsLegacyGenerationBlocked())
                {
                    return false;
                }
                if (_backendProbe?.Hw?.WujieSmuApu == true && !IsWujie8040Series())
                {
                    return false;
                }
                // 宝龙达使用专属「限制充电」面板（限制充电开关 + 充/停充阈值），
                // 不再叠加 GCU 的三档/EC 滑块。
                if (_backendProbe?.Hw?.UsesBaolongda == true)
                {
                    return false;
                }
                MechrevoLite.Gpu.DgpuGenerationKind generation = MechrevoLite.Gpu.GpuGenerationProvider.Current().Generation;
                return generation != MechrevoLite.Gpu.DgpuGenerationKind.Gen30
                    && generation != MechrevoLite.Gpu.DgpuGenerationKind.Unknown;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ⭐⭐ 2026-10-26（task-18）：电池「关闭 EC 直写」逃生口是否应显示/创建 ✅
    /// <para>⭐ 判据：⭐ 充电阈值卡片可见（宝龙达白名单板，见 <see cref="WideChargeLimitVisible" />）**且**
    /// 不是 10/16/20/30 系（<see cref="EcChargeLimit.IsLegacyGenerationBlocked" />）—— 老代际 EC 直写整条被禁，
    /// 留着开关只会给出「可点但无下发」的控件。</para>
    /// <para>⚠️ 刻意**不读** <c>ec_charge_limit</c> 当前值：用户勾上“关闭 EC 直写”后该行若消失，就失去撤销入口
    /// （旧 WinForms 明确要求这一行永不参与收放）；判据只回答「这台机器该不该有 EC 直写这件事」。</para>
    /// </summary>
    internal bool WideEcDirectSupported
    {
        get
        {
            try
            {
                return (WideChargeLimitVisible || WideEcTransportSupported)
                    && !MechrevoLite.Hardware.EcChargeLimit.IsLegacyGenerationBlocked();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>⭐ task-18：无界 8040 系判定（与旧 WinForms IsWujie8040Series 同一判据：WujieSmuApu + CPU 名 8xxx）。</summary>
    private static bool IsWujie8040Series()
    {
        try
        {
            string cpuName = PawnIO.CpuInfo.Name ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(cpuName, @"\b8[0-9]{3}[A-Z]*\b");
        }
        catch
        {
            return false;
        }
    }

    internal (bool LimitEnabled, string Low, string High, string Status, bool Experiment, bool EcDirect) WideChargeLimit()
    {
        return (ChargeLimitSwitch.IsChecked == true, ChargeLimitLowBox.Text, ChargeLimitHighBox.Text,
            ChargeLimitStatus.Text, ChargeSliderToggle.IsChecked == true, EcDirectOffToggle.IsChecked == true);
    }

    internal void WideSetChargeLimitEnabled(bool enabled)
    {
        ChargeLimitSwitch.IsChecked = enabled;
        ChargeLimitSwitch_Click(ChargeLimitSwitch, new RoutedEventArgs());
    }

    internal void WideApplyChargeLimit(string low, string high)
    {
        ChargeLimitLowBox.Text = low;
        ChargeLimitHighBox.Text = high;
        ChargeLimitApply_Click(ChargeLimitApplyButton, new RoutedEventArgs());
    }

    internal void WideSetChargeExperiment(bool enabled)
    {
        ChargeSliderToggle.IsChecked = enabled;
        ChargeSliderToggle_Click(ChargeSliderToggle, new RoutedEventArgs());
    }

    internal void WideSetEcDirect(bool enabled)
    {
        EcDirectOffToggle.IsChecked = enabled;
        EcDirectOffToggle_Click(EcDirectOffToggle, new RoutedEventArgs());
    }

    internal void WideEditQuickTiles()
    {
        CustomTile_Click(CustomTile, new RoutedEventArgs());
    }

    internal async Task WideDockActionAsync(string name)
    {
        switch (name)
        {
            case "悬浮窗":
                ToggleTrayOverlay();
                break;
            case "诊断":
                await ExportDiagnosticPackAsync();
                break;
            case "检查更新":
                await CheckForUpdatesAsync();
                break;
        }
    }

    internal async Task<string> WideCheckForUpdatesAsync()
    {
        return await CheckForUpdatesAsync();
    }

    private async Task<string> CheckForUpdatesAsync()
    {
        try
        {
            StatusText.Text = WpfStrings.UpdateChecking;
            MechrevoLite.Update.UpdateInfo? info = await MechrevoLite.Update.UpdateChecker.CheckGitHubAsync(force: true);
            if (info == null)
            {
                string noResult = WpfStrings.UpdateCheckFailedPrefix + WpfStrings.UpdateCheckNoResult;
                StatusText.Text = noResult;
                return noResult;
            }
            if (!info.UpdateAvailable)
            {
                string latest = WpfStrings.UpdateLatestPrefix + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion;
                StatusText.Text = latest;
                return latest;
            }
            new UpdateWindow(info) { Owner = this }.ShowDialog();
            return WpfStrings.L("发现新版本", "New version available");
        }
        catch (Exception ex)
        {
            string failed = WpfStrings.UpdateCheckFailedPrefix + ex.Message;
            StatusText.Text = failed;
            return failed;
        }
    }

    internal (string Group, (string Key, string Label, bool On)[] Items)[] WideMoreSwitches()
    {
        if (_services != null)
        {
            return _services.QuickSwitches.Groups
                .Select(group => (group.Title, group.Items
                    .Where(item => _services.QuickSwitches.Supports(item.Key))
                    .Select(item => (item.Key, item.Label, _services.QuickSwitches.GetState(item.Key)))
                    .ToArray()))
                .ToArray();
        }
        (string Title, (string Key, string Label)[] Items)[] groups =
        {
            (WpfStrings.QuickGroupInput, new (string, string)[]
            {
                ("touchpad", QuickSwitchService.Label("touchpad", "触摸板")), ("wifi", "WiFi"), ("bt", QuickSwitchService.Label("bt", "蓝牙")), ("webcam", QuickSwitchService.Label("webcam", "摄像头")),
                ("micmute", QuickSwitchService.Label("micmute", "麦克风静音")), ("numpad", QuickSwitchService.Label("numpad", "小键盘锁")), ("touchpadtoggle", QuickSwitchService.Label("touchpadtoggle", "触摸板切换键"))
            }),
            (WpfStrings.QuickGroupKeyboard, new (string, string)[]
            {
                ("winkey", QuickSwitchService.Label("winkey", "Win键锁")), ("fnkey", QuickSwitchService.Label("fnkey", "Fn键锁")), ("capslock", QuickSwitchService.Label("capslock", "大小写锁定")),
                ("osd", QuickSwitchService.Label("osd", "OSD提示")), ("copilot", QuickSwitchService.Label("copilot", "Copilot键锁"))
            }),
            (WpfStrings.QuickGroupPower, new (string, string)[]
            {
                ("usb", QuickSwitchService.Label("usb", "关机USB供电")), ("acrecovery", QuickSwitchService.Label("acrecovery", "来电自启")), ("highperf", QuickSwitchService.Label("highperf", "高性能电源")),
                ("apuboost", QuickSwitchService.Label("apuboost", "APU 防缩缸")), ("fanboost", QuickSwitchService.Label("fanboost", "风扇强冷")), ("deepsleep", QuickSwitchService.Label("deepsleep", "深度睡眠")),
                ("gamewhitelist", QuickSwitchService.Label("gamewhitelist", "游戏白名单")), ("cpuadvperf", QuickSwitchService.Label("cpuadvperf", "解锁BIOS菜单")),
                ("transparency", QuickSwitchService.Label("transparency", "系统透明效果")),
                ("darktheme", QuickSwitchService.Label("darktheme", "系统深色主题")), ("monitoroff", QuickSwitchService.Label("monitoroff", "息屏（不睡眠）")),
                ("winupdate", QuickSwitchService.Label("winupdate", "禁用Win更新"))
            }),
            (WpfStrings.QuickGroupSleep, new (string, string)[]
            {
                ("sleepnetoff", QuickSwitchService.Label("sleepnetoff", "待机断开网络")), ("sleepaway", QuickSwitchService.Label("sleepaway", "封杀离开模式")),
                ("sleepsysreq", QuickSwitchService.Label("sleepsysreq", "忽略系统策略")), ("sleeprtcwake", QuickSwitchService.Label("sleeprtcwake", "禁用RTC唤醒")),
                ("sleepdeep", QuickSwitchService.Label("sleepdeep", "ACPI深度睡眠")), ("sleephibernate", QuickSwitchService.Label("sleephibernate", "30分钟转休眠"))
            })
        };
        List<(string, (string, string, bool)[])> result = new();
        foreach ((string title, (string Key, string Label)[] items) in groups)
        {
            List<(string, string, bool)> visible = new();
            foreach ((string key, string label) in items)
            {
                if (_backendProbe != null && !_backendProbe.SupportsQuick(key)) continue;
                visible.Add((key, label, _backendProbe?.QuickState(key) == true));
            }
            if (visible.Count > 0)
            {
                result.Add((title, visible.ToArray()));
            }
        }
        return result.ToArray();
    }

    internal (string Label, int Seconds)[] WideLightIdleOptions()
    {
        List<(string, int)> options = new();
        foreach ((string Label, int Mins, int IdleSeconds) option in LightingCatalog.LightingCloseTimerOptions)
        {
            options.Add((option.Label, option.IdleSeconds));
        }
        return options.ToArray();
    }

    internal int WideLightIdleSeconds()
    {
        if (_services != null)
        {
            return _services.Lighting.LightIdleSeconds;
        }
        return _backendProbe?.Hw?.UsesBaolongda == true
            ? MechrevoHw.GetKeyboardAutoOffSeconds()
            : AppConfig.Get("lighting_idle_seconds", 0);
    }

    internal void WideSetLightIdleSeconds(int seconds)
    {
        if (_services != null) { DashboardCommands.SetLightIdleSeconds(seconds); return; }
        if (_backendProbe?.Hw?.UsesBaolongda == true)
        {
            MechrevoHw.SetKeyboardAutoOffSeconds(seconds);
            _backendProbe.Hw.ApplyKeyboardAutoOffSetting();
            return;
        }
        AppConfig.Set("lighting_idle_seconds", seconds);
        AppConfig.Flush();
        if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.CloseTimerMinutes = seconds / 60;
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.SaveConfig();
        }
        _ = ReconcileLightingPowerAsync();
    }

    internal bool WideLightOffOnBattery => AppConfig.Is("lighting_off_on_battery");

    internal void WideSetLightOffOnBattery(bool on)
    {
        if (_services != null)
        {
            DashboardCommands.SetLightOffOnBattery(on);
        }
        else
        {
            AppConfig.Set("lighting_off_on_battery", on ? 1 : 0);
            AppConfig.Flush();
        }
        _ = ReconcileLightingPowerAsync();
    }

    internal (string Key, string Label, string Glyph, bool On)[] WideQuickTiles()
    {
        List<(string, string, string, bool)> result = new();
        List<string> keys = new();
        if (QuickTileGrid != null)
        {
            foreach (object child in QuickTileGrid.Children)
            {
                if (child is ToggleButton tile && tile.Tag is string tileKey)
                {
                    keys.Add(tileKey);
                }
            }
        }
        if (keys.Count == 0)
        {
            keys.AddRange(new[] { "wifi", "bt", "webcam", "micmute" });
        }
        foreach (string key in keys)
        {
            if (_backendProbe != null && !_backendProbe.SupportsQuick(key))
            {
                continue;
            }
            (string Key, string Label, string Glyph) info = QuickTileCatalog.FirstOrDefault(item => item.Key == key);
            if (info.Key == null)
            {
                continue;
            }
            bool on = _backendProbe?.QuickState(key) == true;
            result.Add((key, QuickSwitchService.Label(key, info.Label), info.Glyph, on));
        }
        return result.ToArray();
    }

    /// <summary>宝龙达的充电上限是专属「限制充电」三档面板；通用三档/EC 滑块只给 GCU 机型。</summary>
    internal bool WideBatteryModeVisible => _backendProbe?.Hw != null;

    internal (string Info, int Mode, string Charge) WideBattery()
    {
        try
        {
            int mode = _services?.Battery.CurrentMode ?? _backendProbe?.BatteryMode ?? -1;
            MechrevoHw? hw = _backendProbe?.Hw;
            bool thresholdBackend = hw?.UsesBaolongda == true;
            // 宝龙达三档是“写阈值”语义：高亮由上下限文本框反推；GCU 保持原固件三档逻辑。
            if (thresholdBackend && ChargeLimitInputRow.Visibility == Visibility.Visible
                && int.TryParse(ChargeLimitHighBox.Text.Trim(), out int high))
            {
                mode = ChargeLimitSwitch.IsChecked == true ? ModeForPercent(high) : 0;
            }
            // 星耀三档同样按自身 PMC2 文本框反推。
            if (MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform()
                && XingyaoChargeHighBox.Visibility == Visibility.Visible
                && int.TryParse(XingyaoChargeHighBox.Text.Trim(), out int xyHigh))
            {
                mode = XingyaoChargeEnable.IsChecked == true ? ModeForPercent(xyHigh) : 0;
            }
            // 无极限充/EC 限充生效时，三档是 EC 上限值来源，高亮必须跟选定百分比走。
            if (ChargeSliderToggle.IsChecked == true)
            {
                mode = ModeForPercent(MechrevoLite.Battery.BatteryControl.SelectedModePercent());
            }
            else
            {
                // GCU 的 BatteryProtection 回包可能滞后/不刷新；
                // 三档高亮以用户最后选中的记忆档为准，避免点完立刻被旧回包弹回去。
                bool gcuMode = hw != null
                    && hw.IsGcuConnected
                    && !hw.UsesBaolongda
                    && !hw.IsIntelNucMachine
                    && !hw.IsUmiMachine
                    && !MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform();
                if (gcuMode)
                {
                    mode = ModeForPercent(MechrevoLite.Battery.BatteryControl.SelectedModePercent());
                }
            }
            return (BatteryInfo.Text, mode, ChargeLimitStatus.Text);
        }
        catch
        {
            return (WpfStrings.BatteryInfoDash, -1, "");
        }
    }

    private static int ModeForPercent(int percent)
    {
        if (percent >= 90) return 0;
        if (percent >= 70) return 1;
        return 2;
    }

    private void SetBatteryLimitTexts(int low, int high, bool enabled)
    {
        try
        {
            _syncingBatteryLimit = true;
            if (ChargeLimitLowBox != null)
            {
                ChargeLimitLowBox.Text = Math.Clamp(low, 0, 99).ToString();
            }
            if (ChargeLimitHighBox != null)
            {
                ChargeLimitHighBox.Text = Math.Clamp(high, 2, 100).ToString();
            }
            if (ChargeLimitSwitch != null)
            {
                ChargeLimitSwitch.IsChecked = enabled;
            }
            if (BatteryLimitSlider != null)
            {
                BatteryLimitSlider.Value = Math.Clamp(high, 40, 100);
            }
        }
        catch
        {
        }
        finally
        {
            _syncingBatteryLimit = false;
        }
    }

    private void ApplyBatteryModeHighlight(int mode)
    {
        BatteryLongButton.IsChecked = mode == 0;
        BatteryBalancedButton.IsChecked = mode == 1;
        BatteryWorkButton.IsChecked = mode == 2;
        ToggleButton? selected = mode switch
        {
            0 => BatteryLongButton,
            1 => BatteryBalancedButton,
            2 => BatteryWorkButton,
            _ => null
        };
        if (selected != null)
        {
            HighlightSegment(selected);
        }
    }

    internal void WideSetBatteryMode(int mode)
    {
        ToggleButton? target = mode switch
        {
            0 => BatteryLongButton,
            1 => BatteryBalancedButton,
            2 => BatteryWorkButton,
            _ => null
        };
        if (target != null)
        {
            BatteryMode_Click(target, new RoutedEventArgs());
        }
    }

    internal (string Info, int CurrentHz, int MaxHz, bool Auto, int Brightness) WideDisplay()
    {
        try
        {
            string? laptopScreen = MechrevoLite.Display.ScreenNative.FindLaptopScreen(false);
            System.Windows.Forms.Screen? screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(
                s => string.Equals(s.DeviceName, laptopScreen, StringComparison.OrdinalIgnoreCase))
                ?? System.Windows.Forms.Screen.PrimaryScreen;
            System.Drawing.Rectangle bounds = screen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 0, 0);
            int current = MechrevoLite.Display.ScreenNative.GetRefreshRate(laptopScreen);
            if (current < 0)
            {
                current = MechrevoLite.Display.ScreenNative.GetRefreshRate(null);
            }
            if (current < 0)
            {
                current = _backendProbe?.Hw?.CurrentHz ?? -1;
            }
            string info = bounds.Width + " × " + bounds.Height + " · " + Math.Max(0, current) + " Hz";
            return (info, current, _refreshHighHz, AutoRefreshSwitch.IsChecked == true, (int)Math.Round(BrightnessSlider.Value));
        }
        catch
        {
            return (WpfStrings.PlaceholderDisplayDash, -1, -1, false, 100);
        }
    }

    internal int[] WideRefreshRates()
    {
        try
        {
            // 23.5.1：当前显示设备（内屏或外接屏）的 Windows API 档位。
            IReadOnlyList<int> rates = MechrevoLite.Display.ScreenNative.GetAvailableRefreshRates(null, allowCurrentDeviceFallback: true);
            return rates.Distinct().OrderBy(v => v).ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    internal void WideSetBrightness(int brightness)
    {
        int value = Math.Clamp(brightness, 0, 100);
        _syncingBrightness = true;
        BrightnessSlider.Value = value;
        _syncingBrightness = false;
        // 程序化回写滑块时上面的 ValueChanged 会被 _syncingBrightness 跳过；宽屏必须在这里
        // 真正下发亮度，否则两个滑块都只是改 UI、硬件不变。
        try
        {
            if (_services != null) { DashboardCommands.SetBrightness(value); } else { ScreenBrightness.Set(value); }
            OsdService.Show(QuickGlyph("monitoroff"), WpfStrings.OsdBrightness, value + "%");
        }
        catch
        {
        }
    }

    internal void WideSetAutoRefresh(bool enabled)
    {
        AutoRefreshSwitch.IsChecked = enabled;
        AutoRefresh_Click(AutoRefreshSwitch, new RoutedEventArgs());
    }

    internal (string CpuTemp, string GpuTemp, string CpuFan, string GpuFan, string CpuUsage, string GpuUsage, string CpuFanDuty, string GpuFanDuty, string CpuFreq, string GpuFreq, string FanMax, string CpuPower, string GpuPower, string GpuMemFreq) WideSensors()
    {
        // ⭐ P0-3：统一走共享 ISensorService 快照（⛔ 不再直接读 HardwareControl 静态传感器）；
        //   后端未连接/异常 ⇒ Empty ⇒ 全 "--"（与原行为一致）。UI 文本格式保持不变 ✅
        SensorSnapshot snapshot = _services?.Sensors.Read() ?? SensorSnapshot.Empty;
        if (!snapshot.HardwareAvailable)
        {
            return ("--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--");
        }
        try
        {
            double? cpuPower = snapshot.CpuPowerWatts;
            double? gpuPower = snapshot.GpuPowerWatts;
            int? memClock = snapshot.GpuMemoryClockMhz;
            return (snapshot.CpuTempC + " ℃", snapshot.GpuTempC + " ℃",
                snapshot.CpuFanRpm + " RPM", snapshot.GpuFanRpm + " RPM",
                snapshot.CpuUsagePercent + "%", snapshot.GpuUsagePercent + "%",
                snapshot.CpuFanDutyPercent + "%", snapshot.GpuFanDutyPercent + "%",
                snapshot.CpuFrequencyMhz + " MHz", snapshot.GpuCoreFrequencyMhz + " MHz",
                snapshot.FanRpmMaxForPercent.ToString(),
                (cpuPower.HasValue ? cpuPower.Value.ToString("0.0") : "--") + " W",
                (gpuPower.HasValue ? gpuPower.Value.ToString("0.0") : "--") + " W",
                (memClock.HasValue && memClock.Value > 0 ? memClock.Value.ToString("0") : "--") + " MHz");
        }
        catch
        {
            return ("--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", "--");
        }
    }

    /// <summary>GCU 机型的 EC 限充传输开关（与宝龙达阈值面板是两套）。</summary>
    internal bool WideEcTransportSupported
    {
        get
        {
            try
            {
                MechrevoHw? hw = _backendProbe?.Hw;
                return hw != null && !hw.UsesBaolongda
                    && MechrevoLite.Hardware.EcChargeLimit.IsAvailableOnThisMachine();
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool SupportsMiniled => _backendProbe?.Hw?.SupportsLocalDimming == true;

    internal bool MiniledOn => _backendProbe?.Hw?.LocalDimming == true;

    internal bool SupportsFhd => L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327708u) >= 0;

    internal bool FhdOn => L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327708u) > 0;

    internal bool SupportsHdrControl => L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327793u) >= 0;

    internal bool HdrControlOn => L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327793u) > 0;

    internal async void ToggleMiniled()
    {
        try
        {
            BackendProbe? probe = _backendProbe;
            if (probe?.Hw == null || !probe.Hw.SupportsLocalDimming)
            {
                return;
            }
            bool target = !probe.Hw.LocalDimming;
            bool ok = await probe.SwitchLocalDimmingAsync(target);
            StatusText.Text = ok
                ? (target ? WpfStrings.OriginalStyleWindow_xaml_Text_2 : WpfStrings.OriginalStyleWindow_xaml_Text_2)
                : WpfStrings.OriginalStyleWindow_xaml_Text_3;
            RefreshFromBackend();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_3 + ex.Message;
        }
    }

    internal void ToggleHdrControl()
    {
        try
        {
            if (L_Mechrevo.WpfP0.Services.WpfBackend.acpi == null)
            {
                return;
            }
            int current = L_Mechrevo.WpfP0.Services.WpfBackend.acpi.DeviceGet(327793u);
            AppConfig.Set("hdr_control", current == 1 ? 0 : 1);
            AppConfig.Flush();
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_4;
            RefreshFromBackend();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_5 + ex.Message;
        }
    }

    internal void ToggleFhd()
    {
        try
        {
            MechrevoLite.Display.ScreenControl.ToogleFHD();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_6 + ex.Message;
        }
    }

    private void ShowFromTray()
    {
        if (string.Equals(AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
        {
            ShowWideWindow();
            return;
        }
        ShowCompactWindow();
    }

    internal void ShowMainFromExternal()
    {
        if (Environment.TickCount64 < _resumeShowSuppressUntil)
        {
            Logger.WriteLine("[power] 唤醒/会话恢复抑制窗口内收到唤起请求 ⇒ 不显示主界面");
            return;
        }
        if (string.Equals(AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
        {
            ShowWideWindow();
            return;
        }
        ShowCompactWindow();
    }

    private void OpenLightEditor(string topic, string title, (string Effect, string Name)[] effects)
    {
        try
        {
            _ = L_Mechrevo.WpfP0.Services.WpfBackend.Service?.RequestLightStatus(topic);
            new LightEditorWindow(topic, title, effects) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_7 + ex.Message;
        }
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow != null)
        {
            return;
        }
        _overlayWindow = new OverlayWindow(GetOverlaySnapshot);
        _overlayWindow.Closed += (_, _) =>
        {
            _overlayWindow = null;
            SaveOverlayVisible(false);
            UpdateBackendTimerInterval();
        };
        _overlayWindow.IsVisibleChanged += (_, _) =>
        {
            UpdateBackendTimerInterval();
            // 右键隐藏也走这里；必须持久化，否则下次启动会错误地自动显示。
            SaveOverlayVisible(_overlayWindow?.IsVisible == true);
        };
    }

    private void PreloadOverlayWindow()
    {
        if (_overlayPreloaded)
        {
            return;
        }
        _overlayPreloaded = true;
        try
        {
            EnsureOverlayWindow();
        }
        catch
        {
        }
    }

    private TrayMenuState BuildTrayMenuState()
    {
        BackendProbe? probe = _backendProbe;
        int mode = probe != null ? ResolveDisplayMode(probe) : VisualModeForId(MechrevoLite.Mode.PerfModeStore.ActiveModeId);
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        List<TrayCustomItem> customs = new();
        string customRoot = WpfStrings.TrayCustom;
        try
        {
            if (perf != null)
            {
                foreach (MechrevoLite.Mode.PerfModeDefinition custom in perf.Modes.Where(m => m.IsCustom))
                {
                    bool active = string.Equals(perf.DisplayedModeId, custom.Id, StringComparison.Ordinal);
                    string label = string.IsNullOrWhiteSpace(custom.UserName)
                        ? MechrevoLite.Mode.PerfModeText.Name(custom)
                        : custom.UserName;
                    customs.Add(new TrayCustomItem(custom.Id, label, active));
                    if (active)
                    {
                        customRoot = WpfStrings.TrayCustom + ": " + label;
                    }
                }
            }
        }
        catch
        {
        }
        return new TrayMenuState(
            mode,
            customRoot,
            customs,
            probe?.SupportsQuick("fanboost") == true,
            probe?.Hw?.FanBoost == true,
            GpuSection.Visibility == Visibility.Visible,
            probe?.CurrentGpuMode ?? -1,
            KeyboardLightRow.Visibility == Visibility.Visible,
            _overlayWindow?.IsVisible == true,
            WideSilentTurboVisible,
            MechrevoLite.Hardware.MechrevoService.IsSilentTurboActive,
            WideTurboVisible,
            probe?.Hw?.SupportsDgpuDirect ?? probe?.Capabilities?.DgpuDirect ?? false,
            probe?.Hw?.CanOfferIgpuOnly ?? probe?.Capabilities?.IgpuOnly ?? false,
            _overlayWindow?.IsLocked ?? OverlayWindow.ReadSavedLocked());
    }

    private async Task TraySilentTurboAsync()
    {
        if (_backendProbe?.Service == null)
        {
            return;
        }
        await SwitchModeAsync(1, silentTurbo: true);
        RefreshFromBackend();
    }

    private async Task ActivateTrayCustomAsync(string id)
    {
        try
        {
            MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
            if (perf == null)
            {
                return;
            }
            MechrevoLite.Mode.PerfApplyOutcome? outcome = await perf.ActivateAsync(id, "tray", forceParameters: true);
            if (outcome?.AnyFailed == true)
            {
                ToastService.Show(WpfStrings.PerfCustomSwitchFailed, failure: true);
            }
            else
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_8;
            }
        }
        catch (Exception ex)
        {
            ToastService.Show(WpfStrings.PerfCustomSwitchFailedPrefix + ex.Message, failure: true);
        }
        RefreshFromBackend();
    }

    internal void ToggleOverlayFromSettings()
    {
        ToggleTrayOverlay();
    }

    internal void ApplyOverlayAppearance(double opacity, int mode, int intervalMs, bool resetPosition)
    {
        EnsureOverlayWindow();
        _overlayWindow?.ApplyAppearance(opacity, mode, intervalMs, resetPosition);
    }

    private void ToggleTrayOverlay()
    {
        EnsureOverlayWindow();
        if (_overlayWindow == null)
        {
            return;
        }
        if (_overlayWindow.IsVisible)
        {
            _overlayWindow.Hide();
            SaveOverlayVisible(false);
            return;
        }
        _overlayWindow.Show();
        if (!_overlayWindow.IsLocked)
        {
            _overlayWindow.Activate();
        }
        SaveOverlayVisible(true);
    }

    internal bool WideOverlayLocked => _overlayWindow?.IsLocked ?? OverlayWindow.ReadSavedLocked();

    internal void SetOverlayLockedFromSettings(bool locked)
    {
        try
        {
            EnsureOverlayWindow();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[overlay] 创建悬浮窗失败（设置锁定状态仅保存配置）：" + ex.Message);
        }
        if (_overlayWindow != null)
        {
            _overlayWindow.SetLocked(locked);
        }
        else
        {
            MechrevoLite.AppConfig.Set("overlay_locked", locked ? 1 : 0);
            MechrevoLite.AppConfig.Flush();
        }
        _trayIcon?.RefreshMenu();
    }

    private void ToggleTrayOverlayLock()
    {
        bool locked = !(_overlayWindow?.IsLocked ?? OverlayWindow.ReadSavedLocked());
        try
        {
            EnsureOverlayWindow();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[overlay] 创建悬浮窗失败（锁定状态仅保存配置）：" + ex.Message);
        }
        if (_overlayWindow != null)
        {
            _overlayWindow.SetLocked(locked);
        }
        else
        {
            try
            {
                MechrevoLite.AppConfig.Set("overlay_locked", locked ? 1 : 0);
                MechrevoLite.AppConfig.Flush();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[overlay] overlay_locked 写入失败：" + ex.Message);
            }
        }
        _trayIcon?.RefreshMenu();
    }

    private static void SaveOverlayVisible(bool visible)
    {
        try
        {
            MechrevoLite.AppConfig.Set("overlay_wpf_visible", visible ? 1 : 0);
            MechrevoLite.AppConfig.Flush();
        }
        catch
        {
        }
    }

    private void RestoreOverlayVisible()
    {
        try
        {
            // 0.293.5.1 旧键迁移：旧 config 用 overlay=1 表示上次悬浮窗打开。
            if (!MechrevoLite.AppConfig.Exists("overlay_wpf_visible")
                && MechrevoLite.AppConfig.Is("overlay"))
            {
                SaveOverlayVisible(true);
            }
            if (MechrevoLite.AppConfig.Get("overlay_wpf_visible", 0) == 0)
            {
                return;
            }
            EnsureOverlayWindow();
            _overlayWindow?.Show();
        }
        catch
        {
        }
    }

    private IntPtr PowerBroadcastHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_APMRESUMEAUTOMATIC)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(1500);
                await RestoreLightingOnStartupAsync();
            }));
        }
        return IntPtr.Zero;
    }

    // ⭐⭐ task-30：盖子开关监听（旧 Program.cs:1175 注册 LIDSWITCH_STATE_CHANGE 的 WPF 等价）✅
    //   旧版把通知注册在 settingsForm.Handle 上、由窗体的 WndProc 处理；WPF 这里用**独立 message-only 窗口**
    //   （LidSwitchService 内部 HWND_MESSAGE）⇒ ⛔ 不占用/不改本窗 WndProc，生命周期随窗口创建/Closed 释放。
    private void InitializeLidSwitchMonitor()
    {
        try
        {
            _lidSwitch = new L_Mechrevo.WpfP0.Services.LidSwitchService();
            _lidSwitch.LidStateChanged += OnLidStateChanged;
            bool registered = _lidSwitch.Start();
            bool selfCheck = L_Mechrevo.WpfP0.Services.LidSwitchService.RunSelfCheck(out string selfCheckDetail);
            Logger.WriteLine("[lid] 服务：" + (registered ? "已注册 LIDSWITCH_STATE_CHANGE" : "未注册（安全 no-op，不伪造）")
                + " · 自检=" + selfCheck + "（" + selfCheckDetail + WpfStrings.ClosingParen
                + (registered ? "" : " · 原因=" + _lidSwitch.LastError));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[lid] 启动失败（已吞掉，不影响启动）：" + ex.Message);
        }
    }

    private void OnLidStateChanged(object? sender, L_Mechrevo.WpfP0.Services.LidStateChangedEventArgs e)
    {
        // 钩子运行在 UI 线程；再排一次 Dispatcher，避免在消息处理里长时间 await 硬件下发。
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await ApplyLidStateAsync(e.Closed);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[lid] 处理异常（已吞掉）：" + ex.Message);
            }
        });
    }

    /// <summary>
    /// 旧 <c>SettingsForm.cs:12273</c> LIDSWITCH 处理的 WPF 等价：
    /// 合盖 = <c>BatteryControl.AutoBattery()</c> + 关键盘背光（旧 <c>Aura.ApplyBrightness(0,"Lid")</c>）；
    /// 开盖 = 恢复键盘背光（旧 <c>InputDispatcher.GetBacklight()</c>）+ 旧 <c>InputDispatcher.InitFNLock()</c>
    /// （Core 无同源 API ⇒ 安全跳过并记录，⛔ 不伪造）。
    /// <para>⚠️ 旧合盖分支**不**改性能模式/刷新率/电源计划；电源来源变化的模式重放已由 task-25
    /// <c>OnPowerSettledAsync → PerfModeService.ReplayAsync</c> 覆盖 ⇒ 本路径不重复做。</para>
    /// </summary>
    private async Task ApplyLidStateAsync(bool closed)
    {
        try
        {
            // 旧合盖分支第一句即 BatteryControl.AutoBattery()（与 SetAutoModes 同源；不涉及模式/刷新率）。
            BatteryControl.AutoBattery();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[lid] AutoBattery 失败（已吞掉）：" + ex.Message);
        }
        if (closed)
        {
            Logger.WriteLine("[lid] 合盖 ⇒ 关闭键盘背光（旧 Aura.ApplyBrightness(0,\"Lid\") 的 WPF 等价）");
            await SetKeyboardBacklightForLidAsync(on: false);
            return;
        }
        Logger.WriteLine("[lid] 开盖 ⇒ 恢复键盘背光（旧 InputDispatcher.GetBacklight() 的 WPF 等价：按当前开关重放）");
        Logger.WriteLine("[lid] 开盖：旧 InputDispatcher.InitFNLock 无 Core 等价 API ⇒ 跳过（不伪造）");
        await SetKeyboardBacklightForLidAsync(on: true);
    }

    /// <summary>盖子场景的键盘背光开关：HID/EC 分支均复用既有窗口实现，不新增硬件协议。</summary>
    private async Task SetKeyboardBacklightForLidAsync(bool on)
    {
        if (_backendProbe == null)
        {
            Logger.WriteLine("[lid] 后端未连接 ⇒ 键盘背光" + (on ? "恢复" : "关闭") + "跳过");
            return;
        }
        try
        {
            if (on)
            {
                if (KeyboardLightSwitch.IsChecked != true)
                {
                    Logger.WriteLine("[lid] 键盘灯开关是关的 ⇒ 开盖不擅自点亮");
                    return;
                }
                await ApplyKeyboardLightAsync(showStatus: false);
                return;
            }
            if (IsHidKeyboard(_backendProbe.Hw))
            {
                KeyboardRgb? rgb = EnsureKeyboardRgb();
                if (rgb == null)
                {
                    Logger.WriteLine("[lid] HID 键盘实例不可用 ⇒ 合盖关灯安全跳过（不伪造）");
                    return;
                }
                rgb.StopCurrentEffect();
                rgb.KbPowerSyncFromHardware(false);
                return;
            }
            await _backendProbe.SetKeyboardPowerAsync(false);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[lid] 键盘背光" + (on ? "恢复" : "关闭") + "异常（已吞掉）：" + ex.Message);
        }
    }

    private async Task RestoreKeyboardLightAfterWakeAsync()
    {
        if (!_sleepKeyboardLightWasOn || _backendProbe == null)
        {
            return;
        }
        try
        {
            _syncingLighting = true;
            KeyboardLightSwitch.IsChecked = true;
            _syncingLighting = false;
            await ApplyKeyboardLightAsync(showStatus: false);
        }
        catch
        {
        }
    }

    // ⭐⭐ task-25：电源/会话事件（旧 Program.SystemEvents_PowerModeChanged/OnPowerSettled/
    // SystemEvents_SessionSwitch/SystemEvents_SessionEnding 的 WPF 等价）✅
    private void HookPowerSessionEvents()
    {
        if (_powerSessionEventsHooked)
        {
            return;
        }
        _powerSessionEventsHooked = true;
        _lastPowerLineOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
            == System.Windows.Forms.PowerLineStatus.Online;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        if (Application.Current != null)
        {
            Application.Current.SessionEnding += OnAppSessionEnding;
        }
        Logger.WriteLine("[power] 已挂载 PowerModeChanged/SessionSwitch/SessionEnding");
    }

    private void UnhookPowerSessionEvents()
    {
        if (!_powerSessionEventsHooked)
        {
            return;
        }
        _powerSessionEventsHooked = false;
        try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
        try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        try
        {
            if (Application.Current != null)
            {
                Application.Current.SessionEnding -= OnAppSessionEnding;
            }
        }
        catch { }
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            _sleepKeyboardLightWasOn = KeyboardLightSwitch.IsChecked == true;
            Logger.WriteLine("[power] Suspend：记录键盘灯=" + (_sleepKeyboardLightWasOn ? WpfStrings.IgpuSavingOnPreview_Content : WpfStrings.OriginalStyleWindow_Content_5));
            return;
        }
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            MarkResumeShowSuppression("PowerModes.Resume");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500).ConfigureAwait(false);
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await RestoreLightingOnStartupAsync();
                    await RestoreKeyboardLightAfterWakeAsync();
                }));
            });
        }
        // StatusChange / Resume 都走同一套 2s 电源稳定检查（旧 Program.SchedulePowerCheck）
        SchedulePowerSettle();
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLogon
            || e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock
            || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
        {
            MarkResumeShowSuppression("SessionSwitch:" + e.Reason);
            // ⭐ task-27：旧 Program.SystemEvents_SessionSwitch 的 SmartDisplayControl kill（Core 已有 API）
            try
            {
                MechrevoLite.Helpers.ProcessHelper.KillSmartDisplayControl();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[power] KillSmartDisplayControl 失败（已吞掉）：" + ex.Message);
            }
            // Aura.ApplyAura 属旧 UI；WPF 灯光恢复由 ReconcileLightingPowerAsync 承担。
            _ = ReconcileLightingPowerAsync();
            if (_sessionLocked)
            {
                _sessionLocked = false;
                _ = CpuTempReapplyService.ReapplyAfterUnlockAsync();
                Logger.WriteLine("[power] 解锁后重放 CPU 温度墙（旧 AutoCPUTemp 等价）");
            }
            Logger.WriteLine("[power] SessionSwitch 恢复：" + e.Reason);
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            _sessionLocked = true;
            Logger.WriteLine("[power] SessionSwitch 锁定");
        }
    }

    private void OnAppSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        MarkCleanExitForShutdown("SessionEnding");
        try
        {
            // 旧 Program.SystemEvents_SessionEnding：会话结束时把自动电池策略交回
            BatteryControl.AutoBattery();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] SessionEnding AutoBattery 失败（已吞掉）：" + ex.Message);
        }
    }

    private void MarkResumeShowSuppression(string reason)
    {
        _resumeShowSuppressUntil = Environment.TickCount64 + 90_000L;
        Logger.WriteLine("[power] 唤醒后 90 秒内抑制「第二实例唤起主界面」（来源=" + reason + WpfStrings.ClosingParen);
    }

    private void SchedulePowerSettle()
    {
        int generation = System.Threading.Interlocked.Increment(ref _powerSettleGeneration);
        _ = Task.Run(async () =>
        {
            await Task.Delay(2200).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation == _powerSettleGeneration)
                {
                    _ = OnPowerSettledAsync();
                }
            });
        });
    }

    private async Task OnPowerSettledAsync()
    {
        bool online = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
            == System.Windows.Forms.PowerLineStatus.Online;
        if (online == _lastPowerLineOnline)
        {
            return;
        }
        _lastPowerLineOnline = online;
        Logger.WriteLine("[power] 电源来源变化 ⇒ " + (online ? WpfStrings.OverlayExternalPower : "电池"));
        try
        {
            BatteryControl.AutoBattery();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] AutoBattery 失败（已吞掉）：" + ex.Message);
        }
        try
        {
            // 旧 SetAutoModes 的等价：只按“按电源来源记住的档位”重放，不覆盖用户手选的当前档
            MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
            if (perf != null)
            {
                await perf.ReplayAsync(WpfStrings.PowerSourceChangedReason, powerSourceChanged: true, appSideOnly: false);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] 自动性能模式失败（已吞掉）：" + ex.Message);
        }
        try
        {
            await ReconcileLightingPowerAsync();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] 灯光电源跟随失败（已吞掉）：" + ex.Message);
        }
        try
        {
            // 旧 SetAutoModes 仅在“真·插拔电源”时应用自动刷新率（screen_auto 未开时内部 no-op）
            ScreenControl.AutoScreen();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] 自动刷新率失败（已吞掉）：" + ex.Message);
        }
    }

    private void MarkCleanExitForShutdown(string reason)
    {
        if (System.Threading.Interlocked.Exchange(ref _cleanExitMarked, 1) != 0)
        {
            return;
        }
        try
        {
            CpuCoAutoApplyGuard.MarkCleanExit(reason);
            GpuOcShutdownGuard.MarkCleanExit(reason);
            Logger.WriteLine("[power] 已写 clean-exit marker（" + reason + WpfStrings.ClosingParen);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[power] clean-exit marker 失败（已吞掉）：" + ex.Message);
        }
    }

    // ⭐ task-25：startup 参数（计划任务）下不弹首启引导/更新窗口，仅正常启动弹 ✅
    private static bool IsStartupLaunch()
    {
        return StartupPopupGate.IsStartupLaunch;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void HideMinMaxButtons()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        nint style = GetWindowLongPtr(hwnd, GWL_STYLE);
        long value = style.ToInt64();
        value &= ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)value);
    }

    private ToggleButton? _dragTile;
    private Point _dragStart;
    private AdornerLayer? _ghostLayer;
    private DragGhostAdorner? _ghostAdorner;

    private void InitializeQuickTileDrag()
    {
        QuickTileGrid.PreviewMouseLeftButtonDown += QuickTileGrid_PreviewMouseLeftButtonDown;
        QuickTileGrid.PreviewMouseMove += QuickTileGrid_PreviewMouseMove;
        QuickTileGrid.DragOver += (_, e) =>
        {
            _ghostAdorner?.SetPosition(e.GetPosition(QuickTileGrid));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        QuickTileGrid.Drop += QuickTileGrid_Drop;
    }

    private void QuickTileGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragTile = FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject);
        if (_dragTile == CustomTile)
        {
            _dragTile = null;
            return;
        }
        _dragStart = e.GetPosition(QuickTileGrid);
    }

    private void QuickTileGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTile == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        Point now = e.GetPosition(QuickTileGrid);
        if (Math.Abs(now.X - _dragStart.X) < 8 && Math.Abs(now.Y - _dragStart.Y) < 8)
        {
            return;
        }
        ToggleButton source = _dragTile;
        _dragTile = null;
        _ghostLayer = AdornerLayer.GetAdornerLayer(QuickTileGrid);
        _ghostAdorner = new DragGhostAdorner(QuickTileGrid, source);
        _ghostLayer?.Add(_ghostAdorner);
        _ghostAdorner.SetPosition(Mouse.GetPosition(QuickTileGrid));
        try
        {
            DragDrop.DoDragDrop(source, source, DragDropEffects.Move);
        }
        finally
        {
            _ghostLayer?.Remove(_ghostAdorner);
            _ghostAdorner = null;
            _ghostLayer = null;
        }
    }

    private void QuickTileGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ToggleButton)) is not ToggleButton source || source == CustomTile)
        {
            return;
        }
        ToggleButton? target = FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject);
        if (target == null || target == CustomTile || target == source)
        {
            return;
        }
        QuickTileGrid.Children.Remove(source);
        int index = QuickTileGrid.Children.IndexOf(target);
        QuickTileGrid.Children.Insert(index < 0 ? 0 : index, source);
        QuickTileGrid.Columns = Math.Max(1, QuickTileGrid.Children.Count);
        SaveCurrentQuickTileOrder();
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_9;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T typed)
            {
                return typed;
            }
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void NormalizeSegmentDefaults()
    {
        GpuEcoButton.IsChecked = false;
        GpuHybridButton.IsChecked = false;
        GpuDirectButton.IsChecked = true;
        HighlightSegment(GpuDirectButton);
        BatteryLongButton.IsChecked = true;
        BatteryBalancedButton.IsChecked = false;
        BatteryWorkButton.IsChecked = false;
        HighlightSegment(BatteryLongButton);
    }

    private void InitBatteryControls()
    {
        ApplyBatteryLimitVisibility();
        ChargeSliderToggle.IsChecked = AppConfig.Is("charge_limit_slider");
        EcDirectOffToggle.IsChecked = string.Equals(AppConfig.GetString("ec_charge_limit"), "0", StringComparison.Ordinal);
        BatteryLimitSlider.Visibility = ChargeSliderToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BatteryLimitSliderText != null) BatteryLimitSliderText.Visibility = ChargeSliderToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _syncingBatteryLimit = true;
        BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
        _syncingBatteryLimit = false;
        ApplyBatterySliderStage();
    }

    private void ApplyBatterySliderStage()
    {
        bool on = ChargeSliderToggle.IsChecked == true;
        // “关闭 EC 直写”必须始终可点：即使“无极限充”开着，用户也要能切换；
        // 点击时 EcDirectOffToggle_Click 会先把无极限充关掉。
        EcDirectOffToggle.IsEnabled = true;
        ToggleButton[] buttons = { BatteryLongButton, BatteryBalancedButton, BatteryWorkButton };
        foreach (ToggleButton button in buttons)
        {
            // 旧版：无极限充/EC 路生效时三档不置灰，点档位直接走 EC 并联动滑块。
            button.IsEnabled = true;
            button.Opacity = 1.0;
        }
    }

    private async Task WriteBackFullChargeAfterSliderOffAsync()
    {
        try
        {
            MechrevoHw? nucHw = _backendProbe?.Hw;
            if (nucHw?.IsIntelNucMachine == true || nucHw?.IsUmiMachine == true)
            {
                bool nucOk = await Task.Run(() => MechrevoLite.Hardware.NucBatteryMode.TrySetPercent(100, out _));
                if (nucOk)
                {
                    AppConfig.Set("charge_limit", 100);
                    AppConfig.Flush();
                    BatteryControl.chargeFull = true;
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_30;
                }
                else
                {
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_31;
                }
                return;
            }
            bool ecAllowed = !string.Equals(AppConfig.GetString("ec_charge_limit"), "0", StringComparison.Ordinal)
                && EcChargeLimit.IsEcDirectWriteAllowed();
            if (ecAllowed)
            {
                int current = await Task.Run(() => EcChargeLimit.ReadPercent());
                if (current == 100)
                {
                    AppConfig.Set("charge_limit", 100);
                    AppConfig.Flush();
                    BatteryControl.chargeFull = true;
                    return;
                }
                (bool ok, int readback) = await Task.Run(() =>
                {
                    bool written = EcChargeLimit.TrySet(100, out _, out _);
                    return (written, EcChargeLimit.ReadPercent());
                });
                if (readback == 100)
                {
                    AppConfig.Set("charge_limit", 100);
                    AppConfig.Flush();
                    BatteryControl.chargeFull = true;
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_10;
                    return;
                }
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_11 + ok + WpfStrings.StatusReadbackSuffix + readback + "%" + WpfStrings.ClosingParen;
                return;
            }
            MechrevoHw? hw = _backendProbe?.Hw;
            if (hw != null && hw.IsConnected)
            {
                if (hw.BatteryProtection == 0)
                {
                    AppConfig.Set("charge_limit", 100);
                    AppConfig.Flush();
                    BatteryControl.chargeFull = true;
                    return;
                }
                bool confirmed = await hw.SetBatteryProtection(0);
                if (confirmed && hw.BatteryProtection == 0)
                {
                    AppConfig.Set("charge_limit", 100);
                    AppConfig.Flush();
                    BatteryControl.chargeFull = true;
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_12;
                    return;
                }
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_13;
                return;
            }
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_14;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_15 + ex.Message;
        }
    }

    private void ApplyBatteryLimitVisibility()
    {
        bool supported = false;
        try
        {
            MechrevoHw? hw = _backendProbe?.Hw;
            supported = hw != null
                && (BatteryLimitPanel.IsSupportedBoard(hw)
                    || hw.IsIntelNucMachine || hw.IsUmiMachine
                    || MechrevoLite.Hardware.EcChargeLimit.IsEcDirectWriteAllowed());
        }
        catch
        {
            supported = false;
        }
        Visibility visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        MechrevoHw? batteryHw = _backendProbe?.Hw;
        bool baolongda = batteryHw?.UsesBaolongda == true;
        BatteryModePanel.Visibility = Visibility.Visible;
        // 只有宝龙达的阈值行/文本框属于宝龙达；GCU 保持原来的固件三档逻辑。
        Visibility thresholdRows = (visibility == Visibility.Visible && baolongda)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChargeLimitRow.Visibility = thresholdRows;
        ChargeLimitInputRow.Visibility = thresholdRows;
        ChargeLimitStatus.Visibility = visibility;
        ChargeSliderToggle.Visibility = (visibility == Visibility.Visible && !baolongda) ? Visibility.Visible : Visibility.Collapsed;
        EcDirectOffToggle.Visibility = (visibility == Visibility.Visible && !baolongda) ? Visibility.Visible : Visibility.Collapsed;
        RefreshXingyaoChargeLimitUi();
        if (!supported)
        {
            return;
        }
        if (!_batteryLimitLoaded)
        {
            _batteryLimitLoaded = true;
            _ = InitBatteryLimitControlsAsync();
        }
    }

    private void RefreshXingyaoChargeLimitUi()
    {
        try
        {
            if (!MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
            {
                XingyaoChargeRow.Visibility = Visibility.Collapsed;
                XingyaoChargeButtonRow.Visibility = Visibility.Collapsed;
                XingyaoChargeStatus.Visibility = Visibility.Collapsed;
                return;
            }
            // PTL 也开放：读不到也显示，允许现场尝试；非星耀一律不显示。
            XingyaoChargeRow.Visibility = Visibility.Visible;
            XingyaoChargeButtonRow.Visibility = Visibility.Visible;
            XingyaoChargeStatus.Visibility = Visibility.Visible;
            if (MechrevoLite.Hardware.XingyaoPmc2.TryReadChargeLimit(out bool xyEnabled, out int xyT1, out int xyT2, out string xyDetail))
            {
                if (!_xingyaoChargeLoaded)
                {
                    _xingyaoChargeLoaded = true;
                    XingyaoChargeEnable.IsChecked = xyEnabled;
                    XingyaoChargeLowBox.Text = xyT1.ToString();
                    XingyaoChargeHighBox.Text = xyT2.ToString();
                }
                XingyaoChargeStatus.Text = xyDetail;
            }
            else
            {
                XingyaoChargeStatus.Text = "PMC2 读取失败：" + xyDetail + "（可点「保存阈值」尝试写入）";
            }
        }
        catch (Exception ex)
        {
            if (MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
            {
                XingyaoChargeRow.Visibility = Visibility.Visible;
                XingyaoChargeStatus.Visibility = Visibility.Visible;
                XingyaoChargeStatus.Text = "PMC2 状态读取异常：" + ex.GetType().Name;
            }
        }
    }

    private async void XingyaoChargeApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(XingyaoChargeLowBox.Text.Trim(), out int t1)
                || !int.TryParse(XingyaoChargeHighBox.Text.Trim(), out int t2)
                || t1 < 0 || t1 > 100 || t2 < 0 || t2 > 100)
            {
                XingyaoChargeStatus.Text = "星耀限充参数必须是 0–100 的整数";
                return;
            }
            bool enabled = XingyaoChargeEnable.IsChecked == true;
            (bool ok, string detail) = await Task.Run(() =>
            {
                bool applied = MechrevoLite.Hardware.XingyaoPmc2.TryApplyChargeLimit(enabled, t1, t2, out string d);
                return (applied, d);
            });
            XingyaoChargeStatus.Text = detail;
            StatusText.Text = ok ? "星耀充电上限已下发" : "星耀充电上限下发失败";
            Logger.WriteError("[xingyao-pmc2] " + detail);
        }
        catch (Exception ex)
        {
            XingyaoChargeStatus.Text = "星耀充电上限异常：" + ex.GetType().Name + " " + ex.Message;
        }
    }

    private async void XingyaoChargeRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            (bool ok, string detail) = await Task.Run(() =>
            {
                bool applied = MechrevoLite.Hardware.XingyaoPmc2.TryRestoreChargeLimit(out string d);
                return (applied, d);
            });
            XingyaoChargeStatus.Text = detail;
            if (ok)
            {
                XingyaoChargeEnable.IsChecked = false;
                StatusText.Text = "星耀充电限充已恢复默认";
            }
            else
            {
                StatusText.Text = "星耀充电限充恢复失败";
            }
            Logger.WriteError("[xingyao-pmc2] " + detail);
        }
        catch (Exception ex)
        {
            XingyaoChargeStatus.Text = "星耀恢复默认异常：" + ex.GetType().Name + " " + ex.Message;
        }
    }

    private async Task InitBatteryLimitControlsAsync()
    {
        try
        {
            MechrevoHw? limitHw = _backendProbe?.Hw;
            if (limitHw == null || !BatteryLimitPanel.IsSupportedBoard(limitHw))
            {
                return;
            }
            ChargeLimitState? state = await Task.Run(() => _services?.ChargeLimit.Read());
            if (state?.Supported == true)
            {
                int low = state.LowPercent;
                int high = state.HighPercent;
                int mode = state.Mode;
                ChargeLimitLowBox.Text = Math.Clamp(low, 1, 99).ToString();
                ChargeLimitHighBox.Text = Math.Clamp(high, 2, 100).ToString();
                ChargeLimitSwitch.IsChecked = state.Enabled;
                string verdictText = "";
                if (mode == BatteryLimitWriter.ModeActive)
                {
                    verdictText = MechrevoLite.Hardware.EcChargeLimit.Verdict switch
                    {
                        MechrevoLite.Hardware.ChargeLimitVerdict.Verified => " · " + MechrevoLite.Hardware.EcChargeLimit.VerifiedNotice,
                        MechrevoLite.Hardware.ChargeLimitVerdict.Ineffective => " · " + MechrevoLite.Hardware.EcChargeLimit.IneffectiveNotice,
                        _ => " · " + MechrevoLite.Hardware.EcChargeLimit.PendingNotice
                    };
                }
                ChargeLimitStatus.Text = (mode == BatteryLimitWriter.ModeActive
                    ? WpfStrings.ChargeAppliedPrefix + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen
                    : WpfStrings.ChargeNotEnabled) + verdictText;
            }
            else
            {
                ChargeLimitSwitch.IsEnabled = false;
                ChargeLimitApplyButton.IsEnabled = false;
                ChargeLimitLowBox.IsEnabled = false;
                ChargeLimitHighBox.IsEnabled = false;
                ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_16;
            }
        }
        catch (Exception ex)
        {
            ChargeLimitSwitch.IsEnabled = false;
            ChargeLimitApplyButton.IsEnabled = false;
            ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_17 + ex.Message;
        }
    }

    private async Task RefreshBatteryLimitStateAsync()
    {
        try
        {
            ChargeLimitState? state = await Task.Run(() => _services?.ChargeLimit.Read());
            if (state?.Supported == true)
            {
                ChargeLimitSwitch.IsChecked = state.Enabled;
                ChargeLimitStatus.Text = state.Enabled
                    ? WpfStrings.ChargeAppliedPrefix + state.HighPercent + WpfStrings.ChargeStartSeparator + state.LowPercent + "%" + WpfStrings.ClosingParen
                    : WpfStrings.ChargeNotEnabled;
            }
        }
        catch
        {
        }
    }

    private async void ChargeLimitSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChargeLimitSwitch.IsChecked == true;
        int low = 0;
        int high = 0;
        if (enable && !TryReadChargeThreshold(out low, out high, out string error))
        {
            ChargeLimitStatus.Text = error;
            ChargeLimitSwitch.IsChecked = false;
            return;
        }
        ChargeLimitSwitch.IsEnabled = false;
        ChargeLimitApplyButton.IsEnabled = false;
        try
        {
            ServiceResult result = await Task.Run(() => enable
                ? (_services != null ? DashboardCommands.EnableChargeLimitRaw(low, high) : EnableChargeLimitDirect(low, high))
                : (_services != null ? DashboardCommands.DisableChargeLimitRaw() : DisableChargeLimitDirect()));
            bool ok = result.Ok;
            string detail = result.Detail;
            if (ok)
            {
                if (enable)
                {
                    BatteryLimitRestore.SaveConfig(true, low, high);
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_18 + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen;
                }
                else
                {
                    BatteryLimitRestore.SaveConfig(false, 0, 0);
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_20;
                }
            }
            else
            {
                ChargeLimitStatus.Text = (enable ? WpfStrings.OriginalStyleWindow_xaml_Text_19 : WpfStrings.OriginalStyleWindow_xaml_Text_21) + detail;
                await RefreshBatteryLimitStateAsync();
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_22 + ex.Message;
            await RefreshBatteryLimitStateAsync();
        }
        finally
        {
            ChargeLimitSwitch.IsEnabled = true;
            ChargeLimitApplyButton.IsEnabled = true;
        }
    }

    private async void ChargeLimitApply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadChargeThreshold(out int low, out int high, out string error))
        {
            ChargeLimitStatus.Text = error;
            return;
        }
        ChargeLimitSwitch.IsEnabled = false;
        ChargeLimitApplyButton.IsEnabled = false;
        try
        {
            ServiceResult chargeApply = await Task.Run(() => _services != null
                ? DashboardCommands.EnableChargeLimitRaw(low, high)
                : EnableChargeLimitDirect(low, high));
            bool ok = chargeApply.Ok;
            string detail = chargeApply.Detail;
            if (ok)
            {
                BatteryLimitRestore.SaveConfig(true, low, high);
                ChargeLimitSwitch.IsChecked = true;
                ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_23 + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen;
            }
            else
            {
                ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_24 + detail;
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_25 + ex.Message;
        }
        finally
        {
            ChargeLimitSwitch.IsEnabled = true;
            ChargeLimitApplyButton.IsEnabled = true;
        }
    }

    private async void ChargeLimitRestore_Click(object sender, RoutedEventArgs e)
    {
        ChargeLimitSwitch.IsEnabled = false;
        ChargeLimitApplyButton.IsEnabled = false;
        ChargeLimitSwitch.IsChecked = false;
        try
        {
            ServiceResult result = await Task.Run(() => _services != null
                ? DashboardCommands.DisableChargeLimitRaw()
                : DisableChargeLimitDirect());
            ChargeLimitStatus.Text = result.Ok ? WpfStrings.OriginalStyleWindow_xaml_Text_20 : WpfStrings.OriginalStyleWindow_xaml_Text_21 + result.Detail;
            if (result.Ok)
            {
                BatteryLimitRestore.SaveConfig(false, 0, 0);
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = "恢复默认失败：" + ex.GetType().Name + " " + ex.Message;
        }
        finally
        {
            ChargeLimitSwitch.IsEnabled = true;
            ChargeLimitApplyButton.IsEnabled = true;
        }
    }

    private static ServiceResult EnableChargeLimitDirect(int low, int high)
    {
        if (MechrevoLite.Hardware.EcChargeLimit.IsEcDirectWriteAllowed())
        {
            bool ecOk = MechrevoLite.Hardware.EcChargeLimit.TrySet(high, out int applied);
            string ecDetail = "EC 限充 high=" + high + "%" + (applied > 0 ? " 回读=" + applied + "%" : "");
            return ecOk ? ServiceResult.Success(ecDetail) : ServiceResult.Fail(ecDetail);
        }
        bool ok = BatteryLimitWriter.TryEnable(low, high, allowInitialDischarge: true, out string detail);
        return ok ? ServiceResult.Success(detail) : ServiceResult.Fail(detail);
    }

    private static ServiceResult DisableChargeLimitDirect()
    {
        if (MechrevoLite.Hardware.EcChargeLimit.IsEcDirectWriteAllowed())
        {
            bool ecOk = MechrevoLite.Hardware.EcChargeLimit.TrySet(100, out int applied);
            string ecDetail = "EC 限充恢复 100%" + (applied > 0 ? " 回读=" + applied + "%" : "");
            return ecOk ? ServiceResult.Success(ecDetail) : ServiceResult.Fail(ecDetail);
        }
        bool ok = BatteryLimitWriter.TryDisable(out string detail);
        return ok ? ServiceResult.Success(detail) : ServiceResult.Fail(detail);
    }

    private async Task<bool> SetKeyboardEffectServiceAsync(string effect)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetKeyboardEffectAsync(effect)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetKeyboardEffectAsync(effect);
    }

    private async Task<bool> SetLightbarEffectServiceAsync(string effect)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetLightbarEffectAsync(effect)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetLightbarEffectAsync(effect);
    }

    private async Task<bool> SetLogoEffectServiceAsync(string effect)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetLogoEffectAsync(effect)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetLogoEffectAsync(effect);
    }

    private async Task<bool> SetHingeEffectServiceAsync(string effect)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetHingeEffectAsync(effect)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetHingeEffectAsync(effect);
    }

    private async Task<bool> SetSyncEffectServiceAsync(string effect)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetSyncEffectAsync(effect)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetSyncEffectAsync(effect);
    }

    private async Task<bool> SetLightPowerServiceAsync(string topic, bool on)
    {
        if (_services != null)
        {
            return (await _services.Lighting.SetLightPowerAsync(topic, on)).Ok;
        }
        return _backendProbe != null && await _backendProbe.SetLightPowerAsync(topic, on);
    }

    private bool TryReadChargeThreshold(out int low, out int high, out string error)
    {
        low = 60;
        high = 80;
        error = "";
        if (!int.TryParse(ChargeLimitLowBox.Text.Trim(), out low) || low < 1 || low > 99)
        {
            error = WpfStrings.ChargeStartRangeError;
            return false;
        }
        if (!int.TryParse(ChargeLimitHighBox.Text.Trim(), out high) || high < 2 || high > 100)
        {
            error = WpfStrings.ChargeStopRangeError;
            return false;
        }
        if (low >= high)
        {
            error = WpfStrings.ChargeOrderInvalid;
            return false;
        }
        return true;
    }

    private async void ChargeSliderToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChargeSliderToggle.IsChecked == true;
        if (on && EcDirectOffToggle.IsChecked == true)
        {
            EcDirectOffToggle.IsChecked = false;
            if (_services != null) { _services.ChargeLimit.SetEcDirectWrite(false); } else { AppConfig.Remove("ec_charge_limit"); }
        }
        if (_services != null) { _services.ChargeLimit.SetSliderEnabled(on); } else { AppConfig.Set("charge_limit_slider", on ? 1 : 0); }
        BatteryLimitSlider.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (BatteryLimitSliderText != null) BatteryLimitSliderText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ApplyBatterySliderStage();
        if (on)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_26;
            return;
        }
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_27;
        await WriteBackFullChargeAfterSliderOffAsync();
        _syncingBatteryLimit = true;
        BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
        _syncingBatteryLimit = false;
    }

    private async void EcDirectOffToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = EcDirectOffToggle.IsChecked == true;
        if (on && ChargeSliderToggle.IsChecked == true)
        {
            ChargeSliderToggle.IsChecked = false;
            if (_services != null) { _services.ChargeLimit.SetSliderEnabled(false); } else { AppConfig.Set("charge_limit_slider", 0); }
            BatteryLimitSlider.Visibility = Visibility.Collapsed;
            if (BatteryLimitSliderText != null) BatteryLimitSliderText.Visibility = Visibility.Collapsed;
            ApplyBatterySliderStage();
            await WriteBackFullChargeAfterSliderOffAsync();
        }
        if (on)
        {
            if (_services != null) { _services.ChargeLimit.SetEcDirectWrite(true); } else { AppConfig.Set("ec_charge_limit", "0"); }
        }
        else
        {
            if (_services != null) { _services.ChargeLimit.SetEcDirectWrite(false); } else { AppConfig.Remove("ec_charge_limit"); }
        }
        ChargeSliderToggle.IsEnabled = !on;
        StatusText.Text = on ? WpfStrings.ChargeEcDirectOff : WpfStrings.ChargeEcAuto;
    }


    private void BatteryLimitSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingBatteryLimit || BatteryLimitSlider == null || !IsLoaded)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        if (BatteryLimitSliderText != null) BatteryLimitSliderText.Text = value + "%";
        MechrevoHw? hw = _backendProbe?.Hw;
        if (hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true)
        {
            if (!MechrevoLite.Hardware.NucBatteryMode.TrySetPercent(value, out string nucDetail))
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_28;
                _syncingBatteryLimit = true;
                if (MechrevoLite.Hardware.NucBatteryMode.TryReadPercent(out int nucNow))
                {
                    BatteryLimitSlider.Value = nucNow;
                }
                _syncingBatteryLimit = false;
                Logger.WriteLine("[nuc-battery] 滑块 EC 0x07B9 失败：" + nucDetail);
                return;
            }
            BatteryControl.SetSelectedModePercent(value);
            ApplyBatteryModeHighlight(ModeForPercent(value));
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_29 + value + "%";
            Logger.WriteLine("[nuc-battery] 滑块 EC 0x07B9 = " + value + "%");
            return;
        }
        if (!BatteryControl.CaptureChargeLimitGesture() || !BatteryControl.ApplyChargeLimitFromUserGesture(value))
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_28;
            _syncingBatteryLimit = true;
            BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
            _syncingBatteryLimit = false;
            return;
        }
        BatteryControl.SetSelectedModePercent(value);
        ApplyBatteryModeHighlight(ModeForPercent(value));
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_29 + value + "%";
    }

    private void RefreshBrightness()
    {
        try
        {
            if (ScreenBrightness.TryGet(out int brightness))
            {
                _syncingBrightness = true;
                BrightnessSlider.Value = Math.Clamp(brightness, 0, 100);
                BrightnessText.Text = brightness + "%";
                _syncingBrightness = false;
            }
        }
        catch
        {
            _syncingBrightness = false;
        }
    }

    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        BrightnessText.Text = value + "%";
        if (_syncingBrightness)
        {
            return;
        }
        try
        {
            if (_services != null) { _services.Display.SetBrightness(value); } else { ScreenBrightness.Set(value); }
            OsdService.Show(QuickGlyph("monitoroff"), WpfStrings.OsdBrightness, value + "%");
        }
        catch
        {
        }
    }

    private void UpdateContentWidth()
    {
        double width = MainScroll.ViewportWidth - 24;
        if (width <= 100)
        {
            width = MainScroll.ActualWidth - 30;
        }
        if (width <= 100)
        {
            return;
        }
        // 从宽屏切回迷你时 ViewportWidth 可能还是旧值；用当前窗口宽度兜底，避免子项按 1100 宽布局后被裁切。
        double windowCap = ActualWidth - 24;
        if (windowCap > 100 && width > windowCap)
        {
            width = windowCap;
        }
        ContentPanel.Width = width;
        if (!_wideMode)
        {
            // 迷你窗：显式约束两条 AdaptivePanel，避免 WrapPanel 子项按内容宽度撑开后被窗口右缘裁切。
            double inner = Math.Max(120, width);
            if (ModeGrid != null) ModeGrid.Width = inner;
            if (BatteryModePanel != null) BatteryModePanel.Width = inner;
        }
        else
        {
            // 宽屏必须清掉迷你窗留下的固定宽度，否则两模式机器只有 3 个可见按钮时
            // 仍按旧宽度等分，按钮贴左、右侧空出一格。
            if (ModeGrid != null) ModeGrid.Width = double.NaN;
            if (BatteryModePanel != null) BatteryModePanel.Width = double.NaN;
        }
        double childWidth = _wideMode ? Math.Max(340, (width - 8) / 2) : width;
        foreach (object child in ContentPanel.Children)
        {
            if (child is FrameworkElement element)
            {
                element.Width = childWidth;
            }
        }
    }

    private void ApplyWideMode(bool wide)
    {
        _wideMode = wide;
        if (wide)
        {
            MaxWidth = 1600;
            MinWidth = 920;
            Width = Math.Min(1100, SystemParameters.WorkArea.Width - 40);
            WideButton.Content = WpfStrings.OriginalStyleWindow_xaml_Content;
        }
        else
        {
            MaxWidth = 440;
            MinWidth = 440;
            Width = 440;
            WideButton.Content = WpfStrings.OriginalStyleWindow_xaml_Content_2;
        }
        Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        bool nucDirect = _backendProbe?.Hw?.IsIntelNucMachine == true
            || _backendProbe?.Hw?.IsUmiMachine == true;
        if (nucDirect)
        {
            // 官方控制台以 CurrentProfile 事件/回读为准，不在点击瞬间改按钮。
            if (sender is ToggleButton clicked)
            {
                clicked.IsChecked = false;
            }
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_30
                + (sender as ToggleButton)?.Content?.ToString();
            int pendingMode = sender == SilentModeButton ? 2
                : sender == BalancedModeButton ? 0
                : sender == SilentTurboModeButton ? 1
                : sender == TurboModeButton ? 1
                : sender == ExtremeModeButton ? MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode
                : sender == CustomModeButton ? 3
                : -1;
            if (pendingMode >= 0 && _backendProbe != null)
            {
                if (pendingMode == 3)
                {
                    _pendingMode = 3;
                    _pendingModeTick = Environment.TickCount64;
                    await OpenCustomModeFormAsync(switchMode: true);
                }
                else
                {
                    _pendingMode = pendingMode;
                    _pendingModeTick = Environment.TickCount64;
                    _ = SwitchModeAsync(pendingMode, null);
                }
            }
            return;
        }
        foreach (object child in ModeGrid.Children)
        {
            if (child is ToggleButton button)
            {
                button.IsChecked = false;
            }
        }
        if (sender is ToggleButton selected)
        {
            selected.IsChecked = true;
            UpdateModeBorders(selected);
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_30 + (selected.Content as TextBlock)?.Text;
            int mode = selected == SilentModeButton ? 2
                : selected == BalancedModeButton ? 0
                : SilentTurboModeButton != null && selected == SilentTurboModeButton ? 1
                : selected == TurboModeButton ? 1
                : selected == ExtremeModeButton ? MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode
                : selected == CustomModeButton ? 3
                : -1;
            bool? silentTurbo = SilentTurboModeButton != null && ReferenceEquals(selected, SilentTurboModeButton) ? true
                : ReferenceEquals(selected, TurboModeButton) ? false
                : null;
            if (ReferenceEquals(selected, CustomModeButton))
            {
                _pendingMode = 3;
                _pendingModeTick = Environment.TickCount64;
                await OpenCustomModeFormAsync(switchMode: true);
                return;
            }
            if (mode >= 0 && _backendProbe != null)
            {
                _pendingMode = mode;
                _pendingModeTick = Environment.TickCount64;
                _ = SwitchModeAsync(mode, silentTurbo);
            }
        }
    }

    private static void ClearSegmentBorders(Panel panel)
    {
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        Brush normal = (Brush)Application.Current.Resources["ControlBrush"];
        FontFamily regular = (FontFamily)Application.Current.Resources["AppFontFamily"];
        foreach (object child in panel.Children)
        {
            if (child is ToggleButton button)
            {
                button.BorderBrush = gray;
                button.BorderThickness = new Thickness(1);
                button.Background = normal;
                button.FontFamily = regular;
                button.FontWeight = FontWeights.Normal;
            }
        }
    }

    private static void HighlightSegment(ToggleButton selected, Color? overrideColor = null)
    {
        if (selected.Parent is not Panel grid)
        {
            return;
        }
        ClearSegmentBorders(grid);
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        Brush normal = (Brush)Application.Current.Resources["ControlBrush"];
        FontFamily regular = (FontFamily)Application.Current.Resources["AppFontFamily"];
        FontFamily bold = (FontFamily)Application.Current.Resources["AppFontBoldFamily"];
        Color color = overrideColor ?? ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        selected.BorderBrush = new SolidColorBrush(color);
        selected.BorderThickness = new Thickness(2);
        selected.Background = normal;
        selected.FontFamily = bold;
        selected.FontWeight = FontWeights.Bold;
    }

    private void SyncCustomWindowMode(int mode)
    {
        if (_customModeWindow == null || !_customModeWindow.IsVisible)
        {
            return;
        }
        string id = mode switch
        {
            2 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Silent),
            1 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Turbo),
            0 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Balanced),
            3 or 4 => ResolveCustomModeId(),
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(id) || string.Equals(id, _customWindowModeId, StringComparison.Ordinal))
        {
            return;
        }
        _customWindowModeId = id;
        _customModeWindow.BindMode(id);
    }

    private void SyncGpuDbForMode(int mode, string reason)
    {
        try
        {
            // 原版只在视觉档 0/1/2（平衡/狂暴/静音）联动 DB；自定义档 3/4 不碰。
            if (mode < 0 || mode > 2 || _backendProbe?.Hw == null || !_backendProbe.Hw.IsGcuConnected)
            {
                return;
            }
            L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
            if (!MechrevoLite.Hardware.GpuDbControl.Supported())
            {
                Logger.WriteLineIfChanged("db-skip",
                    "[db] 跳过：gen=" + (_backendProbe?.Hw?.DgpuGeneration.ToString() ?? "?")
                    + " · gcuVariant=" + MechrevoLite.Hardware.GcuIdentity.Variant
                    + " · gcu=" + (_backendProbe?.Hw?.IsGcuConnected == true));
                return;
            }
            if (mode == _lastDbVisualMode)
            {
                return;
            }
            _lastDbVisualMode = mode;
            MechrevoLite.Hardware.GpuDbControl.SyncForVisualMode(mode, reason);
        }
        catch
        {
        }
    }

    private async Task SwitchModeAsync(int mode, bool? silentTurbo = null)
    {
        int request = ++_modeRequestId;
        _pendingMode = mode;
        _pendingModeTick = Environment.TickCount64;
        _modeSwitchInFlight = true;
        bool turboSubModeSupported = _backendProbe?.Capabilities?.SilentTurboAvailability
            == MechrevoLite.Hardware.FeatureAvailability.Supported;
        string osdMode = mode switch
        {
            2 => WpfStrings.ModeSilent,
            1 => silentTurbo == true
                ? WpfStrings.SilentTurbo
                : silentTurbo == false && turboSubModeSupported ? WpfStrings.TurboHighEnergy : WpfStrings.ModeTurbo,
            0 => WpfStrings.ModeBalanced,
            MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode => WpfStrings.NucExtremeTurbo,
            _ => WpfStrings.ModeCustom
        };
        OsdService.Show(string.Empty, WpfStrings.OsdPerformance, osdMode);
        _lastSwitchModeOsdTick = Environment.TickCount64;
        _backendProbe?.MarkUserModeSwitch();
        try
        {
            bool nucDirect = IsNucLike(_backendProbe?.Hw);
            bool xingyaoDirect = mode != 3
                && MechrevoLite.Hardware.XingyaoPerformanceMode.IsApplicable(_backendProbe?.Hw);
            MechrevoService.ModeSwitchResult result;
            if (xingyaoDirect)
            {
                bool applied = MechrevoLite.Hardware.XingyaoPerformanceMode.TryApplyVisualMode(mode, out string xyDetail);
                Logger.WriteError("[xingyao-mode] " + (applied ? "OK" : "FAIL") + "：" + xyDetail);
                if (!applied)
                {
                    StatusText.Text = "[星耀] " + xyDetail;
                }
                result = applied ? MechrevoService.ModeSwitchResult.Ok : MechrevoService.ModeSwitchResult.Failed;
            }
            else if (nucDirect)
            {
                int previousMode = ResolveDisplayMode(_backendProbe);
                bool applied = MechrevoLite.Hardware.NucPerformanceMode.TryApply(mode, out string detail);
                if (mode != MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode
                    && applied && MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int reportedMode))
                {
                    if (reportedMode != mode)
                    {
                        Logger.WriteLine("[nuc-mode] 回读模式=" + reportedMode + " 与请求=" + mode + " 不一致，按回读为准");
                        mode = reportedMode;
                        _pendingMode = reportedMode;
                    }
                }
                if (previousMode == 3 && mode != 3)
                {
                    bool fanRestored = MechrevoLite.Hardware.NucFanTable.TryRestoreDefault(out string fanDetail);
                    detail += " 退出自定义风扇恢复=" + fanRestored + " " + fanDetail;
                }
                Logger.WriteLine("[nuc-mode] 直写 EC " + (applied ? "OK" : "FAIL") + "：" + detail);
                result = applied ? MechrevoService.ModeSwitchResult.Ok : MechrevoService.ModeSwitchResult.Failed;
            }
            else
            {
                result = await DashboardCommands.SetPerformanceModeResultAsync(mode);
            }
            if (request != _modeRequestId)
            {
                return;
            }
            if (result == MechrevoService.ModeSwitchResult.Superseded)
            {
                _pendingMode = -1;
                return;
            }
            bool ok = result == MechrevoService.ModeSwitchResult.Ok;
            if (!nucDirect && !xingyaoDirect && ok && mode == 1 && silentTurbo == true && !turboSubModeSupported)
            {
                ok = false;
            }
            else if (!nucDirect && !xingyaoDirect && ok && mode == 1 && silentTurbo.HasValue && turboSubModeSupported)
            {
                ok = _backendProbe?.Service != null
                    && await _backendProbe.Service.SwitchTurboSubMode(silentTurbo.Value);
            }
            BackendStatus.Text = ok ? WpfStrings.OriginalStyleWindow_xaml_Text_8 : WpfStrings.PerfModeFailed;
            if (!ok)
            {
                // 旧版失败后会 RefreshCurrentModeAsync 再回填；不能拿缓存的 CurrentMode 直接回弹。
                if (!nucDirect)
                {
                    int actualMode = await _backendProbe!.RefreshCurrentModeAsync();
                    if (actualMode >= 0)
                    {
                        Logger.WriteLine("[perfmode] 切档失败，回读实际档=" + actualMode);
                    }
                }
                _pendingMode = -1;
            }
            if (ok)
            {
                MechrevoLite.Mode.PerfModeKind? kind = mode switch
                {
                    2 => MechrevoLite.Mode.PerfModeKind.Silent,
                    1 => silentTurbo == true
                        ? MechrevoLite.Mode.PerfModeKind.SilentTurbo
                        : MechrevoLite.Mode.PerfModeKind.Turbo,
                    0 => MechrevoLite.Mode.PerfModeKind.Balanced,
                    _ => null
                };
                if (kind.HasValue)
                {
                    string activeId = MechrevoLite.Mode.PerfModeDefinition.BuiltInId(kind.Value);
                    MechrevoLite.Mode.PerfModeStore.ActiveModeId = activeId;
                    MechrevoLite.Mode.PerfModeService.Instance?.NotifyExternalSwitch(activeId);
                    // 先更新 ActiveModeId，再按当前档位完整配置应用 Windows 电源模式；
                    // 自定义配置里的 WindowsPowerMode 会覆盖高性能电源的默认映射。
                    MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForActiveMode();
                }
                // 非 GCU 机型（NUC/UMI/星耀/宝龙达）也按 UI 档位直接兜底一次；
                // 不依赖 PerfModeStore 里是否能 Find 到 active id。
                if (mode is 0 or 1 or 2)
                {
                    MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForUiMode(mode, silentTurbo == true);
                }
                if (!nucDirect)
                {
                    SyncGpuDbForMode(mode, "WPF 切档");
                }
                SyncCustomWindowMode(mode);
                RefreshWideCustomEditor();
            }
        }
        finally
        {
            if (request == _modeRequestId)
            {
                _modeSwitchInFlight = false;
                RefreshPerfConfigSelections();
                RefreshFromBackend();
                _wideWindow?.RefreshFromOwner();
            }
        }
    }

    internal bool WideModeSwitchPending => _modeSwitchInFlight || _pendingMode >= 0;

    /// <summary>宽屏/迷你三档区域是否显示狂暴档；办公本不支持时保持隐藏。</summary>
    internal bool WideTurboModeVisible => TurboModeButton.Visibility == Visibility.Visible;

    internal bool WideExtremeModeVisible => ExtremeModeButton.Visibility == Visibility.Visible;

    private static int VisualModeForId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }
        if (id.StartsWith("custom", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        return id switch
        {
            "silent" => 2,
            "balanced" => 0,
            "silentturbo" => 1,
            "turbo" => 1,
            _ => -1
        };
    }

    private static bool IsNucLike(MechrevoHw? hw)
    {
        return hw != null && (hw.IsIntelNucMachine || hw.IsUmiMachine);
    }

    private void PollGcuModeOsd()
    {
        try
        {
            BackendProbe? probe = _backendProbe;
            MechrevoHw? hw = probe?.Hw;
            if (hw != null)
            {
                PollKeyboardBrightnessOsd(hw);
                PollTouchpadOsd(hw);
                PollFnLockOsd(hw);
                PollMicOsd(hw);
            }
            bool nucPerf = hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
            bool gcuPerf = hw != null
                && hw.IsGcuConnected
                && !hw.UsesBaolongda
                && !hw.IsIntelNucMachine
                && !hw.IsUmiMachine;
            if (probe == null || hw == null || (!gcuPerf && !nucPerf))
            {
                return;
            }
            int mode = ResolveDisplayMode(probe);
            if (_lastObservedGcuMode == -2)
            {
                _lastObservedGcuMode = mode;
                return;
            }
            if (mode < 0 || mode == _lastObservedGcuMode)
            {
                return;
            }
            int previous = _lastObservedGcuMode;
            _lastObservedGcuMode = mode;
            if (_modeSwitchInFlight || _pendingMode >= 0
                || Environment.TickCount64 - _lastPerfKeyOsdTick <= 1200)
            {
                return;
            }
            _lastPerfKeyOsdTick = Environment.TickCount64;
            OsdService.Show(string.Empty, WpfStrings.OsdPerformance, ModeOsdName(mode));
            Logger.WriteError("[perfkey] GCU 轮询档位变化 " + previous + " -> " + mode + " ⇒ OSD");
        }
        catch
        {
        }
    }

    private void OnAcpiBrightnessCode(int code)
    {
        try
        {
            long now = Environment.TickCount64;
            if (code >= 59 && code <= 63)
            {
                if (now - _lastBrightnessHotkeyTick < 120)
                {
                    return;
                }
                _lastBrightnessHotkeyTick = now;
                int direct = code - 59;
                ApplyKeyboardBrightnessHotkey(direct);
                return;
            }
            if (code == 183)
            {
                if (now - _lastMicHotkeyTick < 600)
                {
                    return;
                }
                _lastMicHotkeyTick = now;
                ToggleMicHotkey();
                return;
            }
            if (code == 164)
            {
                if (now - _lastAirplaneHotkeyTick < 800)
                {
                    return;
                }
                _lastAirplaneHotkeyTick = now;
                ToggleAirplaneHotkey();
                return;
            }
            // Fn+SCRLK 实际产生 VK_SCROLL，由 LockKeyOsdService 监听 + NumpadOverlayService 映射；
            // 不再把 ACPI 1/3 当成 NumLock，否则 CapsLock 也会连带弹一次小键盘 OSD。
            if (code != 177 && code != 178 && code != 185)
            {
                Logger.WriteError("[acpikey] 未处理 ACPI ULong=" + code);
                return;
            }
            if (now - _lastBrightnessHotkeyTick < 120)
            {
                return;
            }
            _lastBrightnessHotkeyTick = now;
            MechrevoHw? hw = _backendProbe?.Hw;
            bool nucCycle = hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
            int current = hw?.KeyboardBrightness ?? -1;
            int hwLevel = current <= 4 ? current : (current < 0 ? 1 : Math.Clamp((int)Math.Round(current / 25.0), 0, 4));
            // ⚠️ HID 写入后 hw.KeyboardBrightness 不一定会立刻回读；
            // 若每次都从硬件值重算，会 100%→0 反复横跳，表现为永远 0/4。
            // 用 LM 上一次实际下发的档位作为循环基准。
            int level = _lastKbdHotkeyLevel >= 0 ? _lastKbdHotkeyLevel : hwLevel;
            if (nucCycle)
            {
                // NUC/UMI 的 Fn+F8 是单键循环，固件可能固定发 177；
                // 若再按“177=减一”处理，亮度到 0 后会永远卡在 0。
                int max = IsWhiteBacklightForOsd(hw) ? 2 : 4;
                level = (level + 1) % (max + 1);
                Logger.WriteError("[kbd-osd] ACPI code=" + code + " NUC 循环 " + level + "/" + max);
            }
            else if (code == 177)
            {
                level = Math.Max(0, level - 1);
            }
            else if (code == 178)
            {
                level = Math.Min(4, level + 1);
            }
            else
            {
                level = (level + 1) % 5;
            }
            ApplyKeyboardBrightnessHotkey(level);
        }
        catch (Exception ex)
        {
            Logger.WriteError("[acpikey] 处理失败：" + ex.Message);
        }
    }

    private void ApplyKeyboardBrightnessHotkey(int level)
    {
        bool white = IsWhiteBacklightForOsd(_backendProbe?.Hw);
        int clamped = Math.Clamp(level, 0, white ? 2 : 4);
        _lastKbdHotkeyLevel = clamped;
        ShowKeyboardBrightnessOsd(clamped);
        try
        {
            KeyboardRgb? rgb = EnsureKeyboardRgb();
            if (rgb != null)
            {
                rgb.Brightness = clamped * 25;
                rgb.ApplyBrightnessToDevice();
                // ApplyBrightnessToDevice 只发 Step3；真正的亮度缩放发生在效果帧里。
                // 不重启当前效果的话，关闭后再开只会发 Step3，灯仍然不亮。
                if (rgb.ActiveMode >= 0)
                {
                    rgb.StartModeForced(rgb.ActiveMode);
                }
                rgb.SaveConfig();
                Logger.WriteError("[kbd-osd] ACPI 亮度实际下发 " + clamped + "/4");
                return;
            }
            MechrevoService? service = _backendProbe?.Service;
            if (service != null)
            {
                _ = service.SetKeyboardBrightnessPreservingEffect(clamped);
                Logger.WriteError("[kbd-osd] ACPI 亮度走 GCU " + clamped + "/4");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteError("[kbd-osd] 亮度下发失败：" + ex.Message);
        }
    }

    private void ToggleMicHotkey()
    {
        try
        {
            if (!MechrevoLite.Helpers.AudioEndpointMic.Available)
            {
                Logger.WriteError("[acpikey] 麦克风端点不可用，忽略 183");
                return;
            }
            bool target = !MechrevoLite.Helpers.AudioEndpointMic.IsMuted;
            if (MechrevoLite.Helpers.AudioEndpointMic.TrySetMute(target, out string note))
            {
                if (_backendProbe?.Hw?.QuickSwitches != null)
                {
                    _backendProbe.Hw.QuickSwitches["micmute"] = target;
                }
                _lastMicOsd = target ? 1 : 0;
                OsdService.Show(QuickGlyph("micmute"), QuickSwitchService.DisplayLabel("micmute"),
                    target ? WpfStrings.StateOn : WpfStrings.StateOff);
                UpdateQuickTileStates();
                _wideWindow?.RefreshFromOwner();
                Logger.WriteError("[acpikey] 183 麦克风静音=" + target + " " + note);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteError("[acpikey] 麦克风切换失败：" + ex.Message);
        }
    }

    private void ToggleAirplaneHotkey()
    {
        try
        {
            bool wifiOn = MechrevoLite.Helpers.RadioSwitch.TryGet(
                MechrevoLite.Helpers.RadioSwitchKind.Wifi, out bool wifi, out _) && wifi;
            bool btOn = MechrevoLite.Helpers.RadioSwitch.TryGet(
                MechrevoLite.Helpers.RadioSwitchKind.Bluetooth, out bool bt, out _) && bt;
            bool turnOff = wifiOn || btOn;
            _ = Task.Run(async () =>
            {
                await MechrevoLite.Helpers.RadioSwitch.SetAsync(MechrevoLite.Helpers.RadioSwitchKind.Wifi, !turnOff);
                await MechrevoLite.Helpers.RadioSwitch.SetAsync(MechrevoLite.Helpers.RadioSwitchKind.Bluetooth, !turnOff);
            });
            OsdService.Show(QuickGlyph("wifi"), "飞行模式", turnOff ? WpfStrings.StateOn : WpfStrings.StateOff);
            Logger.WriteError("[acpikey] 164 飞行模式=" + turnOff);
        }
        catch (Exception ex)
        {
            Logger.WriteError("[acpikey] 飞行模式切换失败：" + ex.Message);
        }
    }

    private void ToggleNumLockHotkey(int code)
    {
        try
        {
            bool target = !MechrevoLite.Helpers.NativeLockKey.IsNumLockOn();
            if (MechrevoLite.Helpers.NativeLockKey.TrySetNumLock(target, out string note))
            {
                _lastNumpadHotkeyTick = Environment.TickCount64;
                OsdService.Show(QuickGlyph("numpad"), QuickSwitchService.DisplayLabel("numpad"),
                    target ? WpfStrings.StateOn : WpfStrings.StateOff);
                Logger.WriteError("[acpikey] " + code + " NumLock=" + target + " " + note);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteError("[acpikey] NumLock 切换失败：" + ex.Message);
        }
    }

    private void ShowKeyboardBrightnessOsd(int level)
    {
        int clamped = Math.Clamp(level, 0, 4);
        _lastKeyboardBrightnessOsd = clamped;
        bool white = IsWhiteBacklightForOsd(_backendProbe?.Hw);
        OsdService.Show(LightChannelGlyph("Keyboard/Ctrl"), WpfStrings.OsdLighting,
            white ? Math.Clamp(clamped, 0, 2) + "/2" : clamped + "/4");
        Logger.WriteError("[kbd-osd] ACPI 亮度 → " + clamped + "/4");
    }

    // OSD 交互参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。
    private void PollTouchpadOsd(MechrevoHw hw)
    {
        try
        {
            object? raw = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status",
                "Enabled", null);
            if (raw == null)
            {
                return;
            }
            bool on = Convert.ToInt32(raw) != 0;
            if (_lastTouchpadOsd == -2)
            {
                _lastTouchpadOsd = on ? 1 : 0;
                return;
            }
            if ((_lastTouchpadOsd == 1) == on)
            {
                return;
            }
            _lastTouchpadOsd = on ? 1 : 0;
            if (hw.QuickSwitches != null)
            {
                hw.QuickSwitches["touchpad"] = on;
            }
            OsdService.ShowTouchpad(QuickSwitchService.DisplayLabel("touchpad"),
                on ? WpfStrings.StateOn : WpfStrings.StateOff);
            UpdateQuickTileStates();
            _wideWindow?.RefreshFromOwner();
            Logger.WriteError("[touchpad-osd] PTP Enabled=" + on + " ⇒ OSD");
        }
        catch
        {
        }
    }

    private void PollFnLockOsd(MechrevoHw hw)
    {
        try
        {
            bool fnKnown;
            bool fnOn = false;
            if (hw.IsIntelNucMachine || hw.IsUmiMachine)
            {
                fnKnown = MechrevoLite.Hardware.NucQuickSwitches.TryReadFnLock(out fnOn);
            }
            else
            {
                fnKnown = hw.QuickSwitches != null && hw.QuickSwitches.TryGetValue("fnkey", out fnOn);
            }
            if (fnKnown)
            {
                if (_lastFnLockOsd == -2)
                {
                    _lastFnLockOsd = fnOn ? 1 : 0;
                    return;
                }
                if ((_lastFnLockOsd == 1) == fnOn)
                {
                    return;
                }
                _lastFnLockOsd = fnOn ? 1 : 0;
                OsdService.Show(QuickGlyph("fnkey"), QuickSwitchService.DisplayLabel("fnkey"),
                    fnOn ? WpfStrings.StateOn : WpfStrings.StateOff);
                UpdateQuickTileStates();
                _wideWindow?.RefreshFromOwner();
                Logger.WriteError("[fnkey-osd] FnLock=" + fnOn + " => OSD");
            }
        }
        catch
        {
        }
    }

    private void PollMicOsd(MechrevoHw hw)
    {
        try
        {
            if (!MechrevoLite.Helpers.AudioEndpointMic.Available)
            {
                return;
            }
            bool muted = MechrevoLite.Helpers.AudioEndpointMic.IsMuted;
            if (_lastMicOsd == -2)
            {
                _lastMicOsd = muted ? 1 : 0;
                return;
            }
            if ((_lastMicOsd == 1) == muted)
            {
                return;
            }
            _lastMicOsd = muted ? 1 : 0;
            if (hw.QuickSwitches != null)
            {
                hw.QuickSwitches["micmute"] = muted;
            }
            OsdService.Show(QuickGlyph("micmute"), QuickSwitchService.DisplayLabel("micmute"),
                muted ? WpfStrings.StateOn : WpfStrings.StateOff);
            UpdateQuickTileStates();
            _wideWindow?.RefreshFromOwner();
            Logger.WriteError("[mic-osd] mic mute=" + muted + " => OSD");
        }
        catch
        {
        }
    }

    private void PollKeyboardBrightnessOsd(MechrevoHw hw)
    {
        int brightness = hw.KeyboardBrightness;
        if (_lastKeyboardBrightnessOsd == -2)
        {
            _lastKeyboardBrightnessOsd = brightness;
            return;
        }
        if (brightness < 0 || brightness == _lastKeyboardBrightnessOsd)
        {
            _lastKeyboardBrightnessOsd = brightness;
            return;
        }
        int previous = _lastKeyboardBrightnessOsd;
        _lastKeyboardBrightnessOsd = brightness;
        // 亮度 0 也是有效状态：键盘灯被 Fn 键关掉。
        // hw.KeyboardBrightness 在不同后端可能是 0..4，也可能是 0..100；
        // 先统一折算为灯效档位，否则 25/50/75 会被 LightLevelText 压成 4。
        int level = brightness <= 4
            ? brightness
            : Math.Clamp((int)Math.Round(brightness / 25.0), 0, 4);
        bool white = IsWhiteBacklightForOsd(hw);
        OsdService.Show(LightChannelGlyph("Keyboard/Ctrl"), WpfStrings.OsdLighting,
            LightLevelText("Keyboard/Ctrl", level, white));
        Logger.WriteError("[kbd-osd] Fn 亮度变化 " + previous + " -> " + brightness + " ⇒ OSD");
    }

    private static string ModeOsdName(int mode)
    {
        if (mode == 3 || mode == 4)
        {
            try
            {
                MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
                string? id = perf?.DisplayedModeId;
                MechrevoLite.Mode.PerfModeDefinition? custom = perf?.Modes
                    .FirstOrDefault(m => m.IsCustom && string.Equals(m.Id, id, StringComparison.Ordinal));
                if (custom != null)
                {
                    return MechrevoLite.Mode.PerfModeText.Name(custom);
                }
            }
            catch
            {
            }
            return WpfStrings.ModeCustom;
        }
        return mode switch
        {
            0 => WpfStrings.ModeBalanced,
            1 => WpfStrings.ModeTurbo,
            2 => WpfStrings.ModeSilent,
            MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode => WpfStrings.NucExtremeTurbo,
            _ => "—"
        };
    }

    /// <summary>NUC/UMI 外部性能键改变 EC 模式后，把 LM 的活动档同步回 EC 真值。</summary>
    private void SyncNucExternalMode(BackendProbe probe)
    {
        try
        {
            MechrevoHw? hw = probe.Hw;
            if (!IsNucLike(hw) || _modeSwitchInFlight || _pendingMode >= 0)
            {
                return;
            }
            if (!MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int ecMode))
            {
                return;
            }
            bool firstObservation = _lastNucEcMode == -2;
            bool ecChanged = _lastNucEcMode != ecMode;
            _lastNucEcMode = ecMode;
            // 首次只记基线；但启动时 EC 已是某档，也要把 Windows 电源模式跟上。
            if (firstObservation)
            {
                int initialUiMode = ecMode == 1 ? 1 : (ecMode == 2 ? 2 : 0);
                MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForUiMode(initialUiMode, false);
                return;
            }
            if (!ecChanged)
            {
                return;
            }
            string id = ecMode switch
            {
                1 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Turbo),
                2 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Silent),
                0 => MechrevoLite.Mode.PerfModeDefinition.BuiltInId(MechrevoLite.Mode.PerfModeKind.Balanced),
                _ => ""
            };
            if (id.Length == 0)
            {
                return;
            }
            string? active = MechrevoLite.Mode.PerfModeStore.ActiveModeId;
            bool customActive = active?.StartsWith("custom", StringComparison.OrdinalIgnoreCase) == true;
            bool builtInMismatch = !customActive && !string.Equals(active, id, StringComparison.Ordinal);
            if (customActive || builtInMismatch)
            {
                Logger.WriteLine("[nuc-mode] 外部按键/EC 模式=" + ecMode + " ⇒ 同步活动档 " + active + " -> " + id);
                MechrevoLite.Mode.PerfModeStore.ActiveModeId = id;
                MechrevoLite.Mode.PerfModeService.Instance?.NotifyExternalSwitch(id);
                MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForActiveMode();
                int uiMode = ecMode == 1 ? 1 : (ecMode == 2 ? 2 : 0);
                MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForUiMode(uiMode, false);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[nuc-mode] 外部模式同步失败：" + ex.Message);
        }
    }

    private static int ResolveDisplayMode(BackendProbe probe)
    {
        // 自定义档在大多数 GCU 上不会回读成一个独立视觉档；以 PerfModeService
        // 的当前显示档为准，避免切到自定义后主界面又回落到内置档高亮。
        string displayedId = MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId
            ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId;
        bool customDisplayed = displayedId?.StartsWith("custom", StringComparison.OrdinalIgnoreCase) == true;
        MechrevoHw? hw = probe.Hw;
        // 极致狂暴没有独立 EC 字节（原厂 Benchmark 与性能风扇同为 1），只能靠本进程标志；
        // 自定义编辑态优先显示自定义，避免打开自定义时误亮极致狂暴。
        if (!customDisplayed
            && IsNucLike(hw)
            && MechrevoLite.Hardware.NucPerformanceMode.IsExtremeActive
            && MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int extremeEc)
            && extremeEc == 1)
        {
            return MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode;
        }
        if (customDisplayed)
        {
            return 3;
        }
        // 宝龙达 / 无界没有原生自定义固件档：ActiveModeId 才是权威。
        if (hw?.UsesBaolongda == true || hw?.SmuCustomApu == true)
        {
            if (string.Equals(MechrevoLite.Mode.PerfModeStore.ActiveModeId, "custom", StringComparison.OrdinalIgnoreCase)
                || (MechrevoLite.Mode.PerfModeStore.ActiveModeId?.StartsWith("custom", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return 3;
            }
        }
        if (MechrevoLite.Hardware.XingyaoPerformanceMode.IsApplicable(hw)
            && MechrevoLite.Hardware.XingyaoPerformanceMode.TryReadVisualMode(out int xingyaoMode, out _))
        {
            return xingyaoMode;
        }
        if (IsNucLike(hw))
        {
            // 等同于官方 CurrentProfile 事件：直接读 EC 1963 的模式字节。
            if (MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int ecMode))
            {
                if (ecMode != 1)
                {
                    // 外部/Fn 键切到其它档：极致狂暴标志失效。
                    MechrevoLite.Hardware.NucPerformanceMode.ClearExtremeMarker();
                }
                if (ecMode == 1 && MechrevoLite.Hardware.NucPerformanceMode.IsExtremeActive)
                {
                    return MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode;
                }
                return ecMode;
            }
            // 读不到时退回本地 ActiveModeId。
            string? localId = MechrevoLite.Mode.PerfModeStore.ActiveModeId;
            if (string.IsNullOrWhiteSpace(localId))
            {
                localId = MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId;
            }
            int local = VisualModeForId(localId);
            if (local >= 0)
            {
                return local;
            }
        }
        int mode = probe.CurrentMode;
        if (mode >= 0)
        {
            return mode;
        }
        int stored = VisualModeForId(MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId
            ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId);
        return stored;
    }

    private void Normalize30BuiltInProfiles()
    {
        try
        {
            MechrevoHw? hw = _backendProbe?.Hw;
            if (hw == null || !hw.IsIntelNucMachine || hw.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.Gen30)
            {
                return;
            }
            if (AppConfig.Get("perf_30_builtin_reset_v1", 0) == 1)
            {
                return;
            }
            foreach (string id in new[] { "silent", "balanced", "turbo" })
            {
                MechrevoLite.Mode.PerfModeSettings settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(id);
                if (settings.NeedsFirmwareSlot)
                {
                    MechrevoLite.Mode.PerfModeStore.ResetSettings(id);
                    Logger.WriteLine("[perfmode] 30 系内置档被配置编辑器改写成固件槽：恢复内置默认 " + id);
                }
            }
            MechrevoLite.Mode.PerfModeStore.SaveSlots(Array.Empty<MechrevoLite.Mode.FirmwareSlotState>());
            AppConfig.Set("perf_30_builtin_reset_v1", 1);
            AppConfig.Flush();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[perfmode] 30 内置档清理失败：" + ex.Message);
        }
    }

    private void EnsurePerformanceKeyService()
    {
        if (_performanceKey != null || _backendProbe?.Hw == null)
        {
            return;
        }
        MechrevoHw keyHw = _backendProbe.Hw;
        bool hasPerformanceKey = IsNucLike(keyHw)
            || (keyHw.IsGcuConnected && !keyHw.UsesBaolongda && !keyHw.IsIntelNucMachine && !keyHw.IsUmiMachine);
        if (!hasPerformanceKey)
        {
            return;
        }
        _performanceKey = new PerformanceKeyService();
        _performanceKey.Pressed += () => Dispatcher.BeginInvoke(new Action(OnPerformanceKeyPressed));
        _performanceKey.AcpiCode += code => Dispatcher.BeginInvoke(new Action(() => OnAcpiBrightnessCode(code)));
        _performanceKey.Start();
        // 性能键的 OSD 走 OsdService，而 OsdService 受 lm_osd_enabled 总开关控制；
        // 这里把开关状态记一行，免得下次只看到“按键无 OSD”却不知道是总开关关着。
        Logger.WriteLine("[perfkey] OSD 总开关 lm_osd_enabled="
            + MechrevoLite.AppConfig.Get("lm_osd_enabled", 1)
            + "（0=LM 所有 OSD 不弹，快捷磁贴「OSD提示」可改回）");
    }

    private async void OnPerformanceKeyPressed()
    {
        try
        {
            BackendProbe? probe = _backendProbe;
            if (probe == null)
            {
                return;
            }
            MechrevoHw? hw = probe.Hw;
            bool nuc = IsNucLike(hw);

            if (nuc)
            {
                int ecBefore = -1;
                bool ecBeforeOk = MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out ecBefore);
                int before = ResolveDisplayMode(probe);
                if (before < 0 && ecBeforeOk)
                {
                    before = ecBefore;
                }
                // 原厂 NUC 的顺序：Performance -> BatterySaver -> Balanced -> Performance
                int next = before switch
                {
                    1 => 2,
                    2 => 0,
                    _ => 1
                };
                await Task.Delay(400);
                probe = _backendProbe;
                if (probe == null)
                {
                    return;
                }
                int after = ResolveDisplayMode(probe);
                bool ecChanged = MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int ecAfter)
                    && ecBeforeOk && ecAfter != ecBefore;
                // 固件只要真的切了档（不一定是预测的 next，也可能是自定义档），
                // 就跟随实际档位出 OSD，不再主动覆盖固件结果。
                if ((after >= 0 && after != before) || ecChanged)
                {
                    SyncNucExternalMode(probe);
                    int visual = after >= 0 ? after : (ecAfter == 1 ? 1 : (ecAfter == 2 ? 2 : 0));
                    OsdService.Show(string.Empty, WpfStrings.OsdPerformance, ModeOsdName(visual));
                    RefreshFromBackend();
                    _wideWindow?.RefreshFromOwner();
                    Logger.WriteLine("[perfkey] NUC 固件已切档 " + before + " -> " + visual + " ⇒ 只同步界面 + OSD");
                    return;
                }
                // 情况 B：按键只发出 ACPI 事件、EC 模式没变 ⇒ LM 主动切一档。
                Logger.WriteLine("[perfkey] 按键后 EC 模式未变（before=" + before + " next=" + next + "）⇒ 主动切档");
                await SwitchModeAsync(next);
                return;
            }

            // GCU 游戏本：按键由固件/GCU 自己处理，LM 只观察。
            // 不再在这里开 5s 取样循环——ModeChanged 和 300ms 轮询会负责 OSD，
            // 避免按键后长时间占用后台任务、也避免连按时多个循环抢 ResolveDisplayMode。
            _lastObservedGcuMode = ResolveDisplayMode(probe);
            Logger.WriteError("[perfkey] GCU 按键交给 GCU/ModeChanged+轮询观察（当前=" + _lastObservedGcuMode + "）");

        }
        catch (Exception ex)
        {
            Logger.WriteLine("[perfkey] 切档失败：" + ex.Message);
        }
    }

    private async Task RestorePerfModeOnStartupAsync()
    {
        if (_perfRestoreStarted)
        {
            return;
        }
        _perfRestoreStarted = true;
        try
        {
            MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
            if (perf != null)
            {
                Normalize30BuiltInProfiles();
                await perf.ReconcileForStartupAsync().ConfigureAwait(true);
                await perf.ReplayLocalProfileOnStartupAsync().ConfigureAwait(true);
                try
                {
                    perf.ApplyApuAntiDegradeOnStartup();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            Dispatcher.BeginInvoke(new Action(RefreshFromBackend));
        }
    }

    /// <summary>5 个模式按钮（50 系双狂暴 / NUC 极致狂暴）时缩小内边距，避免最后“自定义”被卡片边缘裁切。</summary>
    private void ApplyModeSegmentDensity()
    {
        int visible = 0;
        foreach (object child in ModeGrid.Children)
        {
            if (child is ToggleButton { Visibility: Visibility.Visible }) visible++;
        }
        bool dense = visible >= 5;
        foreach (object child in ModeGrid.Children)
        {
            if (child is not ToggleButton button) continue;
            button.Padding = dense ? new Thickness(6, 8, 6, 8) : new Thickness(10, 8, 10, 8);
            button.FontSize = 14;
        }
        ModeGrid.Margin = dense ? new Thickness(0, 2, 6, 0) : new Thickness(0, 2, 0, 0);
    }

    private void RefreshModeHighlights()
    {
        foreach (object child in ModeGrid.Children)
        {
            if (child is ToggleButton button && button.IsChecked == true)
            {
                UpdateModeBorders(button);
                return;
            }
        }
        ClearSegmentBorders(ModeGrid);
    }

    private int _refreshHighHz;
    private readonly List<ToggleButton> _miniRefreshButtons = new();
    private string _miniRefreshRateSignature = "";

    private void SyncRefreshRateState()
    {
        try
        {
            AutoRefreshSwitch.IsChecked = AppConfig.Is("screen_auto");
            int min = ScreenControl.MIN_RATE > 0 ? ScreenControl.MIN_RATE : 60;
            // 23.5.1 口径：直接查询当前活动显示设备（内屏或外接屏）。
            int current = ScreenNative.GetRefreshRate(null);
            IReadOnlyList<int> rates = ScreenNative.GetAvailableRefreshRates(null, allowCurrentDeviceFallback: true);
            List<int> list = rates.Where(r => r >= min).Distinct().OrderBy(r => r).ToList();
            if (list.Count == 0)
            {
                list.Add(min);
                if (current > min) list.Add(current);
                list = list.Distinct().OrderBy(r => r).ToList();
            }
            int max = list[^1];
            _refreshHighHz = max;
            string signature = string.Join(",", list);
            if (!string.Equals(signature, _miniRefreshRateSignature, StringComparison.Ordinal))
            {
                _miniRefreshRateSignature = signature;
                RebuildMiniRefreshButtons(list);
            }
            SelectMiniRefreshButton(current > 0 ? current : max);
        }
        catch
        {
        }
    }

    private void RebuildMiniRefreshButtons(IReadOnlyList<int> rates)
    {
        if (RefreshRatePanel == null)
        {
            return;
        }
        RefreshRatePanel.Children.Clear();
        _miniRefreshButtons.Clear();
        int count = Math.Max(1, rates.Count);
        foreach (int hz in rates)
        {
            ToggleButton button = new()
            {
                Content = hz + "Hz",
                Tag = hz.ToString(),
                Style = (Style)FindResource("RefreshSegment"),
                Margin = new Thickness(count > 1 ? 2 : 0, 0, count > 1 ? 2 : 0, 0)
            };
            button.Click += RefreshRate_Click;
            RefreshRatePanel.Children.Add(button);
            _miniRefreshButtons.Add(button);
        }
    }

    private void SelectMiniRefreshButton(int hz)
    {
        ToggleButton? selected = null;
        foreach (ToggleButton button in _miniRefreshButtons)
        {
            int rate = int.TryParse(button.Tag as string, out int parsed) ? parsed : -1;
            bool on = rate > 0 && Math.Abs(rate - hz) <= 2;
            button.IsChecked = on;
            if (on) selected = button;
        }
        HighlightMiniRefresh();
        if (selected != null)
        {
            HighlightSegment(selected);
        }
    }

    private void HighlightMiniRefresh()
    {
        ToggleButton? selected = _miniRefreshButtons.FirstOrDefault(b => b.IsChecked == true);
        if (selected != null)
        {
            HighlightSegment(selected);
        }
    }

    private void UpdateGpuButtons(int mode, bool pending)
    {
        ToggleButton[] buttons = { GpuEcoButton, GpuHybridButton, GpuDirectButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].IsChecked = i == mode;
        }
        if (mode >= 0 && mode < buttons.Length)
        {
            HighlightSegment(buttons[mode], pending ? Color.FromRgb(250, 204, 21) : null);
        }
        else if (buttons[0].Parent is Panel panel)
        {
            ClearSegmentBorders(panel);
        }
    }

    private bool _igpuSavingBusy;
    private ToggleButton? SilentTurboModeButton;
    private Grid? IgpuSavingPreviewRow;
    private ToggleButton? IgpuSavingOffPreview;
    private ToggleButton? IgpuSavingOnPreview;
    private ToggleButton? IgpuSavingAutoPreview;
    private TextBlock? IgpuSavingPreviewStatus;

    private async void IgpuSavingPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string mode)
        {
            await SwitchIgpuSavingAsync(mode);
        }
    }

    private void BuildIgpuSavingRow()
    {
        if (IgpuSavingHost == null || IgpuSavingPreviewRow != null)
        {
            return;
        }
        Grid row = new() { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock label = new()
        {
            Text = WpfStrings.OriginalStyleWindow_Text_14,
            Style = (Style)FindResource("RowLabel"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        row.Children.Add(label);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 1);
        ToggleButton CreateButton(string content, string tag, string? width = "76")
        {
            ToggleButton button = new()
            {
                Content = content,
                Tag = tag,
                Style = (Style)FindResource("GpuSegment"),
                Width = 76,
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.Click += IgpuSavingPreview_Click;
            buttons.Children.Add(button);
            return button;
        }
        IgpuSavingOffPreview = CreateButton(WpfStrings.OriginalStyleWindow_Content_5, "OFF");
        IgpuSavingOnPreview = CreateButton(WpfStrings.IgpuSavingOnPreview_Content, "ON");
        IgpuSavingAutoPreview = CreateButton(WpfStrings.OriginalStyleWindow_Content_6, "AUTO");
        IgpuSavingAutoPreview.IsChecked = true;
        IgpuSavingAutoPreview.Margin = new Thickness(0);
        row.Children.Add(buttons);
        IgpuSavingPreviewStatus = new TextBlock
        {
            Text = WpfStrings.IgpuSavingPreviewStatus_Text,
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(IgpuSavingPreviewStatus, 2);
        row.Children.Add(IgpuSavingPreviewStatus);
        IgpuSavingPreviewRow = row;
        IgpuSavingHost.Children.Clear();
        IgpuSavingHost.Children.Add(row);
    }

    private void VisualiseIgpuSaving()
    {
        MechrevoHw? hardware = _backendProbe?.Hw;
        if (hardware == null)
        {
            return;
        }
        bool igpuSavingVisible = hardware.SupportsGpuHotSwap || UiQaShowAll.Enabled;
        if (igpuSavingVisible && IgpuSavingPreviewRow == null)
        {
            BuildIgpuSavingRow();
        }
        if (IgpuSavingPreviewRow == null)
        {
            return;
        }
        IgpuSavingPreviewRow.Visibility = igpuSavingVisible ? Visibility.Visible : Visibility.Collapsed;
        int mode = hardware.IgpuOnlySaveMode;
        if (IgpuSavingOffPreview != null) IgpuSavingOffPreview.IsChecked = mode == 1;
        if (IgpuSavingOnPreview != null) IgpuSavingOnPreview.IsChecked = mode == 0;
        if (IgpuSavingAutoPreview != null) IgpuSavingAutoPreview.IsChecked = mode == 3;
        if (IgpuSavingPreviewStatus != null)
        {
            IgpuSavingPreviewStatus.Text = _igpuSavingBusy
                ? WpfStrings.SwitchingEllipsis
                : mode switch { 0 => WpfStrings.IgpuSavingCurrentOn, 1 => WpfStrings.IgpuSavingCurrentOff, 3 => WpfStrings.IgpuSavingCurrentAuto, _ => WpfStrings.IgpuSavingCurrentUnknown };
        }
    }

    private async Task<bool> TryForceIgpuSavingAsync(MechrevoService service, string mode, bool occupancyAlreadyConfirmed)
    {
        if (!NvidiaGpuControl.TryGetActiveDgpuApplications(out IReadOnlyList<DgpuApplication> applications))
        {
            MessageBox.Show(WpfStrings.IgpuListReadFailed, WpfStrings.OriginalStyleWindow_Text_14,
                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            return false;
        }
        IReadOnlyList<DgpuApplication> closable = DgpuApplicationSafety.FilterClosable(
            applications, Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().SessionId);
        DisplayRouteSnapshot display = await WmiDisplayRouteProbe.ProbeAsync();
        GpuScenarioVerdict verdict = GpuScenarioGuard.Evaluate(display, occupancySnapshotAvailable: true,
            occupied: closable.Count > 0, forceConfirmed: false, userOverrideDgpuOwnsDisplay: false);
        if (verdict.Refused)
        {
            string reason = verdict.RefusalReason switch
            {
                "dgpu-owns-only-active-display" => WpfStrings.IgpuBlockOnlyDgpu,
                "occupancy-snapshot-unavailable" => WpfStrings.IgpuBlockSnapshotUnavailable,
                "display-route-unavailable" => WpfStrings.IgpuBlockRouteUnavailable,
                _ => WpfStrings.IgpuBlockSafetyFailed
            };
            MessageBox.Show(reason, WpfStrings.MainWindow_xaml_Text_14, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!verdict.NeedsForceConfirmation || closable.Count == 0)
        {
            return false;
        }
        if (!occupancyAlreadyConfirmed)
        {
            string names = string.Join("\n", closable.Select(static app => app.ProcessName));
            string body = verdict.DgpuOwnsOnlyActiveDisplay
                ? WpfStrings.IgpuWarningDgpuOnlyPrefix + names
                : WpfStrings.IgpuWarningOccupiedPrefix + names;
            if (MessageBox.Show(body, WpfStrings.MainWindow_xaml_Text_14, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return false;
            }
        }
        bool displayOverride = false;
        if (verdict.DgpuOwnsOnlyActiveDisplay)
        {
            if (MessageBox.Show(WpfStrings.IgpuForceConfirm, WpfStrings.OriginalStyleWindow_Text_14,
                MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            {
                return false;
            }
            displayOverride = true;
        }
        GpuScenarioVerdict confirmedVerdict = GpuScenarioGuard.Evaluate(display,
            occupancySnapshotAvailable: true, occupied: true, forceConfirmed: true,
            userOverrideDgpuOwnsDisplay: displayOverride);
        if (confirmedVerdict.Refused || !confirmedVerdict.ForceAllowed)
        {
            return false;
        }
        GpuForceKillPlan plan = DgpuForceKillPlanBuilder.TryPlan(closable, Environment.ProcessId,
            System.Diagnostics.Process.GetCurrentProcess().SessionId, forceConfirmed: true, ReadProcessImagePath);
        if (plan.Refused || plan.SafeToKill.Count == 0)
        {
            MessageBox.Show(WpfStrings.IgpuNoSafeProcesses, WpfStrings.OriginalStyleWindow_Text_14,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        string safeNames = string.Join("\n", plan.SafeToKill.Select(static app => app.ProcessName));
        if (MessageBox.Show(WpfStrings.IgpuKillListPrefix + safeNames + WpfStrings.ContinuePromptSuffix, WpfStrings.OriginalStyleWindow_Text_14,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return false;
        }
        (bool clean, string message) = await ElevatedProcessKiller.KillWithElevationAsync(plan.SafeToKill);
        if (!clean)
        {
            MessageBox.Show(WpfStrings.IgpuKillFailedPrefix + message, WpfStrings.OriginalStyleWindow_Text_14, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return await service.SetIGpuOnlySaveMode(mode);
    }

    private static string? ReadProcessImagePath(DgpuApplication application)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(application.ProcessId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private async Task SwitchIgpuSavingAsync(string mode)
    {
        if (_igpuSavingBusy)
        {
            return;
        }
        MechrevoService? service = _backendProbe?.Service;
        MechrevoHw? hardware = _backendProbe?.Hw;
        if (service == null || hardware == null || !hardware.IsConnected || !hardware.SupportsGpuHotSwap)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_31;
            return;
        }
        int target = mode switch { "ON" => 0, "OFF" => 1, "AUTO" => 3, _ => -1 };
        if (target < 0)
        {
            return;
        }
        if (hardware.IgpuOnlySaveMode == target)
        {
            VisualiseIgpuSaving();
            return;
        }
        _igpuSavingBusy = true;
        VisualiseIgpuSaving();
        int previous = hardware.IgpuOnlySaveMode;
        bool occupancyConfirmed = false;
        try
        {
            if (NvidiaGpuControl.TryGetActiveDgpuApplications(out IReadOnlyList<DgpuApplication> applications))
            {
                IReadOnlyList<DgpuApplication> closable = DgpuApplicationSafety.FilterClosable(
                    applications, Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().SessionId);
                if (closable.Count > 0)
                {
                    string names = string.Join("\n", closable.Select(static app => app.ProcessName));
                    if (MessageBox.Show(WpfStrings.IgpuOccupiedPrefix + names +
                        WpfStrings.IgpuConfirmContinueSuffix,
                        WpfStrings.OriginalStyleWindow_Text_14, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_32;
                        return;
                    }
                    occupancyConfirmed = true;
                }
            }
            bool ok = await service.SetIGpuOnlySaveMode(mode);
            await service.RecheckIgpuOnlySaveModeAsync();
            VisualiseIgpuSaving();
            if (ok)
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_33 + IgpuSavingPreviewStatus.Text;
                return;
            }
            bool forced = await TryForceIgpuSavingAsync(service, mode, occupancyConfirmed);
            if (forced)
            {
                await service.RecheckIgpuOnlySaveModeAsync();
                VisualiseIgpuSaving();
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_34;
                return;
            }
            if (previous != -1)
            {
                string previousMode = previous switch { 0 => "ON", 1 => "OFF", 3 => "AUTO", _ => "OFF" };
                await service.SetIGpuOnlySaveMode(previousMode);
                await service.RecheckIgpuOnlySaveModeAsync();
                VisualiseIgpuSaving();
            }
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_35;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_36 + ex.Message;
            VisualiseIgpuSaving();
        }
        finally
        {
            _igpuSavingBusy = false;
            VisualiseIgpuSaving();
        }
    }

    private async void GpuMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        int mode = int.Parse(text);
        if (!(_services?.Gpu.CanSwitch(mode) ?? _backendProbe.CanSwitchGpuMode(mode)))
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_37;
            RefreshFromBackend();
            return;
        }
        string modeName = button.Content?.ToString() ?? (WpfStrings.ModeNamePrefix + mode);
        // 当前已经是目标显卡模式：不再弹「切换并重启」确认框，直接回显当前态。
        int currentGpuMode = _services?.Gpu.CurrentMode ?? _backendProbe.CurrentGpuMode;
        if (currentGpuMode == mode)
        {
            if (_services != null) { _services.Gpu.ClearRestartPending(); } else { _backendProbe.ClearGpuRestartPending(); }
            StatusText.Text = WpfStrings.CurrentPrefix + modeName;
            RefreshFromBackend();
            return;
        }
        if (_backendProbe.Hw?.IsUmiMachine == true)
        {
            await SwitchUmiGpuColdAsync(mode, button);
            return;
        }
        if (_backendProbe.IsBaolongda)
        {
            if (mode == 0)
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_38;
                RefreshFromBackend();
                return;
            }
            if ((_services?.Gpu.CurrentMode ?? _backendProbe.CurrentGpuMode) == mode)
            {
                if (_services != null) { _services.Gpu.ClearRestartPending(); } else { _backendProbe.ClearGpuRestartPending(); }
                RefreshFromBackend();
                return;
            }
            bool confirmed;
            bool bdOk = _services != null
                ? _services.Gpu.SwitchOnBaolongda(mode, out confirmed).Ok
                : _backendProbe.SwitchGpuModeBaolongda(mode, out confirmed);
            if (bdOk)
            {
                if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
                StatusText.Text = confirmed ? WpfStrings.GpuTargetModeRestart : WpfStrings.GpuSwitchUnconfirmed;
            }
            else
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_39;
            }
            RefreshFromBackend();
            return;
        }
        if (!_backendProbe.IsBaolongda)
        {
            // 对话框打开期间冻结 GPU 按钮刷新，否则 2s 后端 tick 会把刚点的按钮弹回当前模式。
            _gpuPromptOpen = true;
            foreach (ToggleButton item in new[] { GpuEcoButton, GpuHybridButton, GpuDirectButton })
            {
                item.IsChecked = ReferenceEquals(item, button);
            }
            HighlightSegment(button);
            GpuRestartPromptWindow prompt = new GpuRestartPromptWindow(modeName) { Owner = this };
            try
            {
                prompt.ShowDialog();
            }
            finally
            {
                _gpuPromptOpen = false;
            }
            if (prompt.Result == GpuPromptResult.Cancel)
            {
                RefreshFromBackend();
                return;
            }
            if (prompt.Result == GpuPromptResult.SwitchAndRestart)
            {
                bool restartOk = _services != null
                    ? (await DashboardCommands.RequestGpuRestartAsync(mode)).Ok
                    : await _backendProbe.RequestGpuModeRestartAsync(mode);
                if (restartOk)
                {
                    if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_40;
                    // 立刻显示黄色待重启态；后续后端刷新会按 pending 保持。
                    UpdateGpuButtons(mode, pending: true);
                }
                else
                {
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_41;
                }
                RefreshFromBackend();
                return;
            }
        }
        foreach (ToggleButton item in new[] { GpuEcoButton, GpuHybridButton, GpuDirectButton })
        {
            item.IsChecked = ReferenceEquals(item, button);
        }
        HighlightSegment(button);
        bool ok = _services != null
            ? (await DashboardCommands.SetGpuModeAsync(mode)).Ok
            : await _backendProbe.SwitchGpuModeAsync(mode);
        if (_services != null) { await _services.Gpu.RefreshAsync(); } else { await _backendProbe.RefreshGpuModeAsync(); }
        int current = _backendProbe.CurrentGpuMode;
        if (ok && current != mode)
        {
            if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
            UpdateGpuButtons(mode, pending: true);
        }
        else if (ok)
        {
            if (_services != null) { _services.Gpu.ClearRestartPending(); } else { _backendProbe.ClearGpuRestartPending(); }
        }
        StatusText.Text = ok ? WpfStrings.GpuModeSentRestart : WpfStrings.GpuModeFailed;
        RefreshFromBackend();
    }

    private async Task SwitchUmiGpuColdAsync(int mode, ToggleButton button)
    {
        if (_backendProbe == null || (mode != 1 && mode != 2))
        {
            return;
        }
        string modeName = button.Content?.ToString() ?? (WpfStrings.ModeNamePrefix + mode);
        _gpuPromptOpen = true;
        foreach (ToggleButton item in new[] { GpuEcoButton, GpuHybridButton, GpuDirectButton })
        {
            item.IsChecked = ReferenceEquals(item, button);
        }
        HighlightSegment(button);
        GpuRestartPromptWindow prompt = new GpuRestartPromptWindow(modeName) { Owner = this };
        try
        {
            prompt.ShowDialog();
        }
        finally
        {
            _gpuPromptOpen = false;
        }
        if (prompt.Result == GpuPromptResult.Cancel)
        {
            RefreshFromBackend();
            return;
        }
        bool ok = MechrevoLite.Hardware.UefiGpuMux.TryWriteColdMode(mode, out string detail);
        Logger.WriteLine("[gpu-mux] UMI cold switch " + (ok ? "OK" : "FAIL") + "：" + detail);
        if (!ok)
        {
            StatusText.Text = WpfStrings.L("UEFI 显卡模式写入失败：", "Failed to write UEFI GPU mode: ") + detail;
            RefreshFromBackend();
            return;
        }
        if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
        UpdateGpuButtons(mode, pending: true);
        if (prompt.Result == GpuPromptResult.SwitchAndRestart)
        {
            StatusText.Text = WpfStrings.L("已写入 NVRAM，即将重启切换显卡模式", "NVRAM written; restarting to switch GPU mode");
            try
            {
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /f /t 5")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.L("NVRAM 已写入，但重启失败：", "NVRAM written, but restart failed: ") + ex.Message;
            }
        }
        else
        {
            StatusText.Text = WpfStrings.L("已写入 NVRAM，重启后切换显卡模式：", "NVRAM written; GPU mode will switch after restart: ") + detail;
        }
        RefreshFromBackend();
    }

    private async void BatteryMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        foreach (object child in ((Panel)button.Parent).Children)
        {
            if (child is ToggleButton other)
            {
                other.IsChecked = ReferenceEquals(other, button);
            }
        }
        HighlightSegment(button);
        int mode = int.Parse(text);
        int percentForMode = mode switch { 0 => 100, 1 => 80, 2 => 60, _ => 80 };

        // 宝龙达三档：直接写充电上下限，并把文本框同步到实际阈值。
        // GCU 保持原来的固件三档逻辑（不在这里改阈值）。
        MechrevoHw? modeHw = _backendProbe.Hw;
        bool modeBaolongda = modeHw?.UsesBaolongda == true;
        bool modeGcu = !modeBaolongda
            && modeHw?.IsGcuConnected == true
            && !MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform();
        if (modeBaolongda && _services != null)
        {
            bool enable = mode != 0;
            int low = mode switch { 1 => 75, 2 => 55, _ => 0 };
            int high = mode switch { 1 => 80, 2 => 60, _ => 100 };
            SetBatteryLimitTexts(low, high, enabled: enable);
            ServiceResult result = await Task.Run(() => enable
                ? DashboardCommands.EnableChargeLimitRaw(low, high)
                : DashboardCommands.DisableChargeLimitRaw());
            if (result.Ok)
            {
                if (enable)
                {
                    BatteryLimitRestore.SaveConfig(true, low, high);
                }
                else
                {
                    BatteryLimitRestore.SaveConfig(false, 0, 0);
                }
                ApplyBatteryModeHighlight(mode);
                StatusText.Text = mode switch
                {
                    1 => "平衡模式：充电 75~80%",
                    2 => "工作站模式：充电 55~60%",
                    _ => "长效模式：不限制充电"
                };
            }
            else
            {
                StatusText.Text = WpfStrings.BatteryModeFailed + " " + result.Detail;
                RefreshFromBackend();
            }
            return;
        }

        // 星耀三档：直接写 PMC2 的 T1/T2，并同步星耀自己的文本框。
        if (MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
        {
            bool enable = mode != 0;
            int low = mode switch { 1 => 75, 2 => 55, _ => 0 };
            int high = mode switch { 1 => 80, 2 => 60, _ => 100 };
            (bool xyOk, string xyDetail) = await Task.Run(() =>
            {
                bool applied = enable
                    ? MechrevoLite.Hardware.XingyaoPmc2.TryApplyChargeLimit(true, low, high, out string d)
                    : MechrevoLite.Hardware.XingyaoPmc2.TryRestoreChargeLimit(out d);
                return (applied, d);
            });
            if (xyOk)
            {
                _xingyaoChargeLoaded = true;
                _syncingBatteryLimit = true;
                try
                {
                    XingyaoChargeEnable.IsChecked = enable;
                    XingyaoChargeLowBox.Text = low.ToString();
                    XingyaoChargeHighBox.Text = high.ToString();
                }
                finally
                {
                    _syncingBatteryLimit = false;
                }
                XingyaoChargeStatus.Text = xyDetail;
                ApplyBatteryModeHighlight(mode);
                StatusText.Text = mode switch
                {
                    1 => "平衡模式：充电 75~80%",
                    2 => "工作站模式：充电 55~60%",
                    _ => "长效模式：不限制充电"
                };
            }
            else
            {
                XingyaoChargeStatus.Text = xyDetail;
                StatusText.Text = WpfStrings.BatteryModeFailed + " " + xyDetail;
                RefreshFromBackend();
            }
            Logger.WriteError("[xingyao-pmc2] " + xyDetail);
            return;
        }

        // 路径只由「无极限充」开关决定：
        //   ON  → 三档作为 EC 上限，走 EC 直写；
        //   OFF → 走原来的传统/GCU 路（GCU BatteryProtection / NUC 本地 / 其它后端）。
        if (ChargeSliderToggle.IsChecked == true)
        {
            bool sliderOk;
            if (_backendProbe.Hw?.IsIntelNucMachine == true || _backendProbe.Hw?.IsUmiMachine == true)
            {
                sliderOk = MechrevoLite.Hardware.NucBatteryMode.TrySetPercent(percentForMode, out string nucDetail);
                Logger.WriteLine("[nuc-battery] 三档→EC 0x07B9 " + (sliderOk ? "OK" : "FAIL") + "：" + nucDetail);
            }
            else
            {
                sliderOk = MechrevoLite.Battery.BatteryControl.CaptureChargeLimitGesture()
                    && MechrevoLite.Battery.BatteryControl.ApplyChargeLimitFromUserGesture(percentForMode);
            }
            if (sliderOk)
            {
                _syncingBatteryLimit = true;
                BatteryLimitSlider.Value = Math.Clamp(percentForMode, 40, 100);
                _syncingBatteryLimit = false;
                if (modeGcu)
                {
                    // GCU 原逻辑不变，只把用户选中的三档记忆下来；
                    // 否则 SelectedModePercent 仍回退到旧的 80，长效档会被高亮成平衡。
                    MechrevoLite.Battery.BatteryControl.SetSelectedModePercent(percentForMode);
                }
                ApplyBatteryModeHighlight(mode);
                StatusText.Text = WpfStrings.BatteryModeSent;
            }
            else
            {
                StatusText.Text = WpfStrings.BatteryModeFailed;
                RefreshFromBackend();
            }
            Logger.WriteLine($"[charge-route] 无极限充=ON ⇒ mode={mode} 走 EC 路 ok={sliderOk}");
            return;
        }
        bool ok = _services != null
            ? (await DashboardCommands.SetBatteryModeAsync(mode)).Ok
            : await _backendProbe.SetBatteryModeAsync(mode);
        if (ok && modeGcu)
        {
            MechrevoLite.Battery.BatteryControl.SetSelectedModePercent(percentForMode);
        }
        StatusText.Text = ok ? WpfStrings.BatteryModeSent : WpfStrings.BatteryModeFailed;
        Logger.WriteLine($"[charge-route] 无极限充=OFF ⇒ mode={mode} 走传统/GCU 路 ok={ok}");
        if (!ok)
        {
            RefreshFromBackend();
        }
    }

    private void RefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        int requested = int.Parse(text);
        SelectMiniRefreshButton(requested);
        HighlightSegment(button);
        WideSetRefresh(requested);
    }

    private async void AutoRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = AutoRefreshSwitch.IsChecked == true;
        bool ok = _services != null
            ? (await DashboardCommands.SetAutoRefreshAsync(on)).Ok
            : await _backendProbe.SwitchAutoRefreshAsync(on);
        AppConfig.Set("screen_auto", on ? 1 : 0);
        AppConfig.Flush();
        SyncRefreshRateState();
        StatusText.Text = ok ? WpfStrings.DisplayAutoRefreshSent : WpfStrings.DisplayAutoRefreshFailed;
    }

    private async void QuickTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tile || tile.Tag is not string key || _backendProbe == null)
        {
            return;
        }
        bool on = tile.IsChecked == true;
        if (key == "micmute")
        {
            on = !on;
        }
        bool ok = await DashboardCommands.SetQuickAsync(key, on);
        StatusText.Text = ok ? WpfStrings.QuickSentPrefix + key : WpfStrings.QuickFailedPrefix + key;
    }

    private void OnRadioStateChanged(MechrevoLite.Helpers.RadioSwitchKind kind, bool on)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                string key = kind == MechrevoLite.Helpers.RadioSwitchKind.Wifi ? "wifi" : "bt";
                // 首次快照只用于基线，避免 LM 启动时弹“Wi-Fi/蓝牙已开”的 OSD。
                if (!_radioOsdPrimed.Add(kind))
                {
                    OsdService.ShowQuickSwitch(key, on);
                }
                if (_backendProbe != null)
                {
                    UpdateQuickTileStates();
                    if (MoreGroupsPanel != null)
                    {
                        foreach (CheckBox box in EnumerateCheckBoxes(MoreGroupsPanel))
                        {
                            if (box.Tag is string switchKey)
                            {
                                box.IsChecked = _backendProbe.QuickState(switchKey);
                            }
                        }
                        UpdateMoreSwitchCount();
                    }
                }
                _wideWindow?.RefreshFromOwner();
            }
            catch
            {
            }
        }));
    }

    private void OnLockKeyChanged(string key, bool on)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_backendProbe == null)
                {
                    return;
                }
                UpdateQuickTileStates();
                if (MoreGroupsPanel != null)
                {
                    foreach (CheckBox box in EnumerateCheckBoxes(MoreGroupsPanel))
                    {
                        if (box.Tag is string switchKey)
                        {
                            box.IsChecked = _backendProbe.QuickState(switchKey);
                        }
                    }
                    UpdateMoreSwitchCount();
                }
                _wideWindow?.RefreshFromOwner();
            }
            catch
            {
            }
        }));
    }

    private void UpdateQuickTileStates()
    {
        BackendProbe? probe = _backendProbe;
        if (probe == null || QuickTileGrid == null)
        {
            return;
        }
        foreach (object child in QuickTileGrid.Children)
        {
            if (child is ToggleButton tile && tile.Tag is string key)
            {
                UpdateQuickTile(tile, key, probe);
            }
        }
    }

    private static void UpdateQuickTile(ToggleButton tile, string key, BackendProbe probe)
    {
        bool switchOn = probe.QuickState(key);
        tile.IsChecked = key == "micmute" ? !switchOn : switchOn;
    }

    private void CustomTile_Click(object sender, RoutedEventArgs e)
    {
        List<string> currentOrder = new List<string>();
        foreach (object child in QuickTileGrid.Children)
        {
            if (child is ToggleButton tile && tile.Tag is string currentKey)
            {
                currentOrder.Add(currentKey);
            }
        }
        List<QuickChoice> choices = new List<QuickChoice>();
        foreach ((string key, string label, string glyph) in QuickTileCatalog)
        {
            if (_backendProbe != null && !_backendProbe.SupportsQuick(key))
            {
                continue;
            }
            choices.Add(new QuickChoice(key, QuickSwitchService.Label(key, label), glyph, HasQuickTile(key)));
        }
        choices = choices
            .OrderBy(c => currentOrder.IndexOf(c.Key) is int i && i >= 0 ? i : int.MaxValue)
            .ToList();
        if (choices.Count == 0)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_43;
            return;
        }
        QuickTilePickerWindow picker = new QuickTilePickerWindow(choices, 5)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true)
        {
            RebuildQuickTiles(picker.SelectedChoices);
        }
    }

    private bool HasQuickTile(string key)
    {
        foreach (object child in QuickTileGrid.Children)
        {
            if (child is ToggleButton tile && tile.Tag is string existing
                && string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private void RebuildQuickTiles(IReadOnlyList<QuickChoice> selected, bool persist = true)
    {
        for (int i = QuickTileGrid.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(QuickTileGrid.Children[i], CustomTile))
            {
                QuickTileGrid.Children.RemoveAt(i);
            }
        }
        foreach (QuickChoice choice in selected)
        {
            ToggleButton tile = new ToggleButton
            {
                Style = (Style)FindResource("Tile"),
                Tag = choice.Key,
                IsChecked = _backendProbe?.QuickState(choice.Key) == true
            };
            tile.Click += QuickTile_Click;
            StackPanel content = new StackPanel();
            FrameworkElement icon = CreateQuickTileIcon(choice.Key, choice.Glyph);
            TextBlock label = new TextBlock
            {
                Text = choice.Label,
                FontSize = selected.Count >= 5 || choice.Label.Length > 4 ? 10 : 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = choice.Label,
                Margin = new Thickness(0, 2, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            content.Children.Add(icon);
            content.Children.Add(label);
            tile.Content = content;
            int customIndex = QuickTileGrid.Children.IndexOf(CustomTile);
            QuickTileGrid.Children.Insert(customIndex, tile);
        }
        QuickTileGrid.Columns = Math.Max(1, QuickTileGrid.Children.Count);
        UpdateQuickTileStates();
        if (persist)
        {
            SaveQuickTileKeys(selected.Select(static c => c.Key).ToList());
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_44;
        }
    }

    private FrameworkElement CreateQuickTileIcon(string key, string glyph)
    {
        if (string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M8,8 C6,5 6,2 8,1 C10,2 10,5 8,8 Z M8,8 C11,6 14,7 15,9 C13,11 10,10 8,8 Z M8,8 C10,11 10,14 8,15 C6,14 6,11 8,8 Z M8,8 C5,10 2,9 1,7 C3,5 6,6 8,8 Z M8,7 A1,1 0 1 1 8,9 A1,1 0 1 1 8,7 Z")
            };
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1)
            });
            Canvas canvas = new Canvas { Width = 16, Height = 16 };
            canvas.Children.Add(path);
            return new Viewbox { Width = 16, Height = 16, Child = canvas, HorizontalAlignment = HorizontalAlignment.Center };
        }
        TextBlock text = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return text;
    }

    private const string MoreOpenConfigKey = "group_quick_open";

    private void RefreshSegmentHighlights()
    {
        foreach (object child in ModeGrid.Children)
        {
            if (child is ToggleButton mode && mode.IsChecked == true)
            {
                UpdateModeBorders(mode);
                break;
            }
        }
        HighlightChecked(GpuEcoButton, GpuHybridButton, GpuDirectButton);
        HighlightMiniRefresh();
        HighlightChecked(BatteryLongButton, BatteryBalancedButton, BatteryWorkButton);
    }

    private static void HighlightChecked(params ToggleButton[] buttons)
    {
        if (buttons.Length > 0 && buttons[0].Parent is Panel panel)
        {
            ClearSegmentBorders(panel);
        }
        foreach (ToggleButton button in buttons)
        {
            if (button.IsChecked == true)
            {
                HighlightSegment(button);
                break;
            }
        }
    }

    private void ApplyMoreSectionState()
    {
        bool expanded = AppConfig.Is(MoreOpenConfigKey);
        MoreGroupsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MoreArrow.Text = expanded ? "▾" : "▸";
    }

    private const string LightingOpenConfigKey = "group_lighting_open";
    private const string LiquidOpenConfigKey = "group_lc_open";

    private void ApplySectionCollapseStates()
    {
        bool lighting = !AppConfig.Exists(LightingOpenConfigKey) || AppConfig.Is(LightingOpenConfigKey);
        bool liquid = !AppConfig.Exists(LiquidOpenConfigKey) || AppConfig.Is(LiquidOpenConfigKey);
        SetSectionExpanded(LightingBody, LightingArrow, lighting);
        SetSectionExpanded(LiquidBody, LiquidArrow, liquid);
    }

    private static void SetSectionExpanded(StackPanel body, TextBlock arrow, bool expanded)
    {
        body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        arrow.Text = expanded ? "▾" : "▸";
    }

    private void ToggleSection(StackPanel body, TextBlock arrow, string key)
    {
        bool expanded = body.Visibility != Visibility.Visible;
        SetSectionExpanded(body, arrow, expanded);
        AppConfig.Set(key, expanded ? 1 : 0);
        AppConfig.Flush();
    }

    private void LightingHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }
        ToggleSection(LightingBody, LightingArrow, LightingOpenConfigKey);
    }

    private void LiquidHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }
        ToggleSection(LiquidBody, LiquidArrow, LiquidOpenConfigKey);
    }

    private void MoreHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool expanded = MoreGroupsPanel.Visibility != Visibility.Visible;
        MoreGroupsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MoreArrow.Text = expanded ? "▾" : "▸";
        AppConfig.Set(MoreOpenConfigKey, expanded ? 1 : 0);
    }

    private void ApplySavedQuickTiles()
    {
        string? raw = AppConfig.GetString(QuickTilesConfigKey);
        List<string> keys;
        if (raw == null)
        {
            keys = DefaultQuickTileKeys.ToList();
        }
        else if (raw.Trim() == QuickTilesEmptySentinel)
        {
            keys = new List<string>();
        }
        else
        {
            keys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        List<QuickChoice> selected = new List<QuickChoice>();
        foreach (string key in keys)
        {
            if (selected.Count >= 5)
            {
                break;
            }
            foreach ((string catalogKey, string label, string glyph) in QuickTileCatalog)
            {
                if (string.Equals(catalogKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(new QuickChoice(catalogKey, QuickSwitchService.Label(catalogKey, label), glyph, true));
                    break;
                }
            }
        }
        RebuildQuickTiles(selected, persist: false);
    }

    private static void SaveQuickTileKeys(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            AppConfig.Set(QuickTilesConfigKey, QuickTilesEmptySentinel);
        }
        else
        {
            AppConfig.Set(QuickTilesConfigKey, string.Join(",", keys));
        }
    }

    private void SaveCurrentQuickTileOrder()
    {
        List<string> keys = new List<string>();
        foreach (object child in QuickTileGrid.Children)
        {
            if (child is ToggleButton tile && tile.Tag is string key)
            {
                keys.Add(key);
            }
        }
        SaveQuickTileKeys(keys);
    }

    private void BuildMoreSwitches()
    {
        if (MoreGroupsPanel == null || MoreGroupsPanel.Children.Count > 0 || _backendProbe == null)
        {
            return;
        }
        (string Title, (string Key, string Label)[] Items)[] groups =
        {
            (WpfStrings.QuickGroupInput, new (string, string)[]
            {
                ("touchpad", QuickSwitchService.Label("touchpad", "触摸板")), ("wifi", "WiFi"), ("bt", QuickSwitchService.Label("bt", "蓝牙")), ("webcam", QuickSwitchService.Label("webcam", "摄像头")),
                ("micmute", QuickSwitchService.Label("micmute", "麦克风静音")), ("numpad", QuickSwitchService.Label("numpad", "小键盘锁")), ("touchpadtoggle", QuickSwitchService.Label("touchpadtoggle", "触摸板切换键"))
            }),
            (WpfStrings.QuickGroupKeyboard, new (string, string)[]
            {
                ("winkey", QuickSwitchService.Label("winkey", "Win键锁")), ("fnkey", QuickSwitchService.Label("fnkey", "Fn键锁")), ("capslock", QuickSwitchService.Label("capslock", "大小写锁定")),
                ("osd", QuickSwitchService.Label("osd", "OSD提示")), ("copilot", QuickSwitchService.Label("copilot", "Copilot键锁"))
            }),
            (WpfStrings.QuickGroupPower, new (string, string)[]
            {
                ("usb", QuickSwitchService.Label("usb", "关机USB供电")), ("acrecovery", QuickSwitchService.Label("acrecovery", "来电自启")), ("highperf", QuickSwitchService.Label("highperf", "高性能电源")),
                ("apuboost", QuickSwitchService.Label("apuboost", "APU 防缩缸")), ("fanboost", QuickSwitchService.Label("fanboost", "风扇强冷")), ("deepsleep", QuickSwitchService.Label("deepsleep", "深度睡眠")),
                ("gamewhitelist", QuickSwitchService.Label("gamewhitelist", "游戏白名单")), ("cpuadvperf", QuickSwitchService.Label("cpuadvperf", "解锁BIOS菜单")),
                ("transparency", QuickSwitchService.Label("transparency", "系统透明效果")),
                ("darktheme", QuickSwitchService.Label("darktheme", "系统深色主题")), ("monitoroff", QuickSwitchService.Label("monitoroff", "息屏（不睡眠）")),
                ("winupdate", QuickSwitchService.Label("winupdate", "禁用Win更新"))
            }),
            (WpfStrings.QuickGroupSleep, new (string, string)[]
            {
                ("sleepnetoff", QuickSwitchService.Label("sleepnetoff", "待机断开网络")), ("sleepaway", QuickSwitchService.Label("sleepaway", "封杀离开模式")),
                ("sleepsysreq", QuickSwitchService.Label("sleepsysreq", "忽略系统策略")), ("sleeprtcwake", QuickSwitchService.Label("sleeprtcwake", "禁用RTC唤醒")),
                ("sleepdeep", QuickSwitchService.Label("sleepdeep", "ACPI深度睡眠")), ("sleephibernate", QuickSwitchService.Label("sleephibernate", "30分钟转休眠"))
            })
        };
        foreach ((string title, (string Key, string Label)[] items) in groups)
        {
            List<CheckBox> visible = new List<CheckBox>();
            foreach ((string key, string label) in items)
            {
                if (!_backendProbe.SupportsQuick(key))
                {
                    continue;
                }
                visible.Add(new CheckBox
                {
                    Content = QuickSwitchService.Label(key, label),
                    Style = (Style)FindResource("RowSwitch"),
                    Tag = key,
                    IsChecked = _backendProbe.QuickState(key),
                    Margin = new Thickness(0, 1, 4, 1),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (visible.Count == 0)
            {
                continue;
            }
            if (title == WpfStrings.QuickGroupSleep)
            {
                Grid headerRow = new Grid { Margin = new Thickness(0, 8, 0, 2) };
                headerRow.Children.Add(new TextBlock { Text = QuickSwitchService.GroupTitle(title), Style = (Style)FindResource("MutedText"), FontSize = 10 });
                StackPanel scope = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                ToggleButton ac = new ToggleButton { Content = WpfStrings.OriginalStyleWindow_xaml_Content, Style = (Style)FindResource("SmallSegment"), IsChecked = true, Tag = "ac", Margin = new Thickness(0, 0, 4, 0), Height = 24, FontSize = 11 };
                ToggleButton dc = new ToggleButton { Content = WpfStrings.OriginalStyleWindow_xaml_Content_2, Style = (Style)FindResource("SmallSegment"), Tag = "dc", Height = 24, FontSize = 11 };
                ac.Click += SleepScope_Click;
                dc.Click += SleepScope_Click;
                scope.Children.Add(ac);
                scope.Children.Add(dc);
                headerRow.Children.Add(scope);
                MoreGroupsPanel.Children.Add(headerRow);
            }
            else
            {
                MoreGroupsPanel.Children.Add(new TextBlock
                {
                    Text = QuickSwitchService.GroupTitle(title),
                    Style = (Style)FindResource("MutedText"),
                    FontSize = 10,
                    Margin = new Thickness(0, 8, 0, 2)
                });
            }
            UniformGrid grid = new UniformGrid { Columns = 3, Margin = new Thickness(0) };
            foreach (CheckBox box in visible)
            {
                box.Click += MoreSwitch_Click;
                grid.Children.Add(box);
            }
            MoreGroupsPanel.Children.Add(grid);
        }
        UpdateMoreSwitchCount();
    }

    private void SleepScope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Parent is not StackPanel parent)
        {
            return;
        }
        foreach (object child in parent.Children)
        {
            if (child is ToggleButton button)
            {
                button.IsChecked = ReferenceEquals(button, selected);
            }
        }
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_45 + selected.Content;
    }

    private async void MoreSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.Tag is string key && _backendProbe != null)
        {
            bool ok = await DashboardCommands.SetQuickAsync(key, box.IsChecked == true);
            if (!ok)
            {
                box.IsChecked = !(box.IsChecked == true);
            }
            StatusText.Text = ok ? WpfStrings.SwitchedPrefix + key : WpfStrings.SwitchFailedPrefix + key;
            UpdateMoreSwitchCount();
        }
    }

    private void UpdateMoreSwitchCount()
    {
        if (MoreSwitchCount == null || MoreGroupsPanel == null)
        {
            return;
        }
        int on = 0;
        int total = 0;
        foreach (object child in EnumerateCheckBoxes(MoreGroupsPanel))
        {
            if (child is CheckBox box)
            {
                total++;
                if (box.IsChecked == true)
                {
                    on++;
                }
            }
        }
        MoreSwitchCount.Text = string.Format(WpfStrings.QuickMoreSummaryFormat, on, total);
    }

    private static IEnumerable<CheckBox> EnumerateCheckBoxes(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is CheckBox box)
            {
                yield return box;
            }
            foreach (CheckBox nested in EnumerateCheckBoxes(child))
            {
                yield return nested;
            }
        }
    }

    private static string FormatPowerSource(bool acOnline)
    {
        if (!acOnline)
        {
            return WpfStrings.OverlayBatteryPower;
        }
        try
        {
            if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true)
            {
                // 宝龙达没有适配器功率表，保持原版「外接电源」，不显示“未识别”。
                return WpfStrings.OverlayExternalPower;
            }
            MechrevoLite.Hardware.PowerInputSample? sample = L_Mechrevo.WpfP0.Services.PowerStatusService.Read().Input;
            MechrevoLite.Hardware.PowerInputKind kind = MechrevoLite.Hardware.PowerInputDecoder.Reconcile(
                sample, acOnline);
            if (sample?.AdapterWatts is int watts)
            {
                string text = SettingsText.PowerSourceHeadline(kind, watts);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            int raw = sample?.BiosInfo3Raw ?? -1;
            if (raw >= 0)
            {
                // 同方机型不在功率表里时，直接显示 EC 原始读数。
                return string.Format(WpfStrings.DisplayPowerEcHexFormat, raw.ToString("X2"));
            }
            decimal? rate = L_Mechrevo.WpfP0.Services.PowerStatusService.Read().BatteryRateWatts;
            if (rate.HasValue)
            {
                return string.Format(WpfStrings.DisplayPowerEcWattFormat, rate.Value.ToString("0.0"));
            }
        }
        catch
        {
        }
        return WpfStrings.OverlayExternalPower;
    }

    internal void WideRefreshTrayMenu()
    {
        try
        {
            _trayIcon?.RefreshMenu();
        }
        catch
        {
        }
    }

    /// <summary>
    /// 把 LM 当前语言同步给 GCU 悬浮窗/OSD（Languages/Control SET）。
    /// 仅 GCU 机型；宝龙达没有该通道。
    /// </summary>
    internal void SyncGcuLanguage(string? language)
    {
        try
        {
            MechrevoHw? hw = _backendProbe?.Hw;
            if (hw == null || !hw.IsGcuConnected || hw.UsesBaolongda)
            {
                return;
            }
            string code = language ?? MechrevoLite.AppConfig.GetString("language") ?? "zh-CN";
            string gcuLang = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-cn" : "en-us";
            SyncOemRegistryLanguage(gcuLang);
            _ = Task.Run(async () =>
            {
                try
                {
                    await hw.Publish("Languages/Control", new Dictionary<string, object>
                    {
                        ["Action"] = "SET",
                        ["Lang"] = gcuLang
                    }).ConfigureAwait(false);
                    Logger.WriteLine("[gcu-lang] 已同步 GCU 悬浮窗语言：" + gcuLang);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("[gcu-lang] 同步失败：" + ex.GetType().Name + " " + ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[gcu-lang] 同步异常：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void SyncOemRegistryLanguage(string gcuLang)
    {
        try
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\OEM\GamingCenter2", writable: true);
            key?.SetValue("Language", gcuLang);
            using Microsoft.Win32.RegistryKey tp = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\OEM\GamingCenter2\TpDetect", writable: true);
            tp?.SetValue("Language", gcuLang);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[gcu-lang] OEM 注册表语言写入失败：" + ex.Message);
        }
    }

    internal bool WideTraySensorEnabled => MechrevoLite.AppConfig.Get("tray_sensor_tooltip", 0) == 1;

    internal void WideSetTraySensorEnabled(bool enabled)
    {
        MechrevoLite.AppConfig.Set("tray_sensor_tooltip", enabled ? 1 : 0);
        MechrevoLite.AppConfig.Flush();
        if (!enabled)
        {
            _trayIcon?.SetTooltip("L-Mechrevo");
        }
        bool consumer = HasVisibleSensorConsumer();
        ApplySensorDemandState(consumer);
        if (consumer)
        {
            RefreshFromBackend();
        }
    }

    private bool HasVisibleSensorConsumer()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            return true;
        }
        if (_wideWindow?.IsVisible == true && _wideWindow.WindowState != WindowState.Minimized)
        {
            return true;
        }
        if (_overlayWindow?.IsVisible == true)
        {
            return true;
        }
        // 设置/自定义/曲线等任何可见窗口也算消费者；OSD 瞬时窗口不单独常驻。
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (!ReferenceEquals(window, this)
                    && !ReferenceEquals(window, _wideWindow)
                    && !ReferenceEquals(window, _overlayWindow)
                    && window.IsVisible
                    && window.WindowState != WindowState.Minimized)
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return WideTraySensorEnabled;
    }

    private void ApplySensorDemandState(bool consumer)
    {
        if (Interlocked.Exchange(ref _sensorDemandBusy, 1) != 0)
        {
            return;
        }
        try
        {
            if (!consumer)
            {
                if (!_sensorDemandSuspended)
                {
                    _sensorDemandSuspended = true;
                    MechrevoLite.HardwareControl.DisableLocalMonitoring();
                    Logger.WriteLine("[sensor-demand] 无可见 UI/托盘传感器/悬浮窗 ⇒ 停止本地传感器采样");
                }
                return;
            }
            if (_sensorDemandSuspended)
            {
                _sensorDemandSuspended = false;
                Logger.WriteLine("[sensor-demand] 有可见消费者 ⇒ 重新启用本地传感器采样");
                _ = Task.Run(() =>
                {
                    try
                    {
                        MechrevoLite.HardwareControl.EnableLocalMonitoring();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("[sensor-demand] 重新启用失败：" + ex.Message);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[sensor-demand] 状态切换失败：" + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _sensorDemandBusy, 0);
        }
    }

    private void UpdateBackendTimerInterval()
    {
        TimeSpan interval = _overlayWindow?.IsVisible == true
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(2);
        if (_backendTimer.Interval != interval)
        {
            _backendTimer.Interval = interval;
        }
    }

    private void UpdateTraySensorTooltip(BackendProbe probe)
    {
        if (!WideTraySensorEnabled || _trayIcon == null || probe.Hw == null)
        {
            _trayIcon?.SetTooltip("L-Mechrevo");
            return;
        }
        try
        {
            MechrevoHw hw = probe.Hw;
            L_Mechrevo.WpfP0.Services.PowerStatusSnapshot power = L_Mechrevo.WpfP0.Services.PowerStatusService.Read(sample: true);
            L_Mechrevo.WpfP0.Services.SensorSnapshot sensor = _services?.Sensors.Read() ?? L_Mechrevo.WpfP0.Services.SensorSnapshot.Empty;
            int battery = power.Percent;
            string cpuFan = hw.CpuFanRpm > 0
                ? " " + hw.CpuFanRpm + "rpm"
                : " " + hw.CpuFanDuty + "%";
            string gpuFan = hw.GpuFanRpm > 0
                ? " " + hw.GpuFanRpm + "rpm"
                : " " + hw.GpuFanDuty + "%";
            string cpuPower = sensor.CpuPowerWatts.HasValue ? " " + sensor.CpuPowerWatts.Value.ToString("0") + "W" : "";
            string gpuPower = sensor.GpuPowerWatts.HasValue ? " " + sensor.GpuPowerWatts.Value.ToString("0") + "W" : "";
            string text = "CPU" + hw.CpuTemp + "°" + cpuFan + cpuPower
                + " | GPU" + hw.GpuTemp + "°" + gpuFan + gpuPower
                + (battery > 0 ? " | " + battery + "%" : "");
            _trayIcon.SetTooltip(text);
        }
        catch
        {
            _trayIcon.SetTooltip("L-Mechrevo");
        }
    }

    private void RefreshFromBackend()
    {
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return;
        }
        // 0.293.5.1 限充效果判定：复用界面 2s 心跳，内部 10s 限频；只读/有界重确立。
        try { MechrevoLite.Battery.ChargeLimitMonitor.Tick(DateTime.Now); } catch { }
        probe.SyncNucQuickSwitches();
        BackendLine.Text = probe.Snapshot();
        UpdateTraySensorTooltip(probe);
        HookHardwareState(probe.Hw);
        if (probe.Hw.IsIntelNucMachine || probe.Hw.IsUmiMachine)
        {
            MechrevoLite.Hardware.NucQuickSwitches.Sync(probe.Hw);
        }
        EnsurePerformanceKeyService();
        try
        {
            MechrevoHw? liveHw = probe.Hw;
            long connectionGeneration = liveHw?.ConnectionGeneration ?? 0;
            bool live = probe.Connected && liveHw != null && liveHw.IsConnected;
            if (live && (connectionGeneration != _lastLightingConnectionGeneration || _lastLightingConnectionGeneration == 0))
            {
                _lastLightingConnectionGeneration = connectionGeneration;
                _ = RestoreLightingOnStartupAsync();
                ScheduleLightingReassert();
            }
        }
        catch
        {
        }
        try
        {
            L_Mechrevo.WpfP0.Services.PowerStatusService.Read(sample: true);
        }
        catch
        {
        }
        if (probe.Hw is not MechrevoHw hw)
        {
            return;
        }
        RefreshXingyaoFanIfNeeded(hw);
        bool noDgpu = hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.NoDgpu;
        GpuStat.Visibility = noDgpu ? Visibility.Collapsed : Visibility.Visible;
        CpuStat.Text = SensorLine(hw, cpu: true);
        GpuStat.Text = SensorLine(hw, cpu: false);
        VisualiseIgpuSaving();
        if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            string? selectedHidTag = (KeyboardEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (IsHidKeyboard(hw))
            {
                string expectedHidTag = "hid:" + L_Mechrevo.WpfP0.Services.WpfBackend.rgb.KbHidMode;
                if (!string.Equals(selectedHidTag, expectedHidTag, StringComparison.Ordinal))
                {
                    FillKeyboardEffects();
                }
            }
            else if (selectedHidTag?.StartsWith("hid:", StringComparison.Ordinal) == true)
            {
                FillKeyboardEffects();
            }
        }
        string health = SettingsText.BatteryHealthText(hw);
        string abnormal = hw.BatteryAbnormal ? " · " + WpfStrings.BatteryAbnormal : "";
        int batteryPercent = WindowsBattery.ReadPercent();
        if (batteryPercent < 0)
        {
            batteryPercent = hw.BatteryPercent;
        }
        if (batteryPercent < 0)
        {
            batteryPercent = 0;
        }
        string powerState = "";
        try
        {
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            bool charging = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
            string source = FormatPowerSource(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
            string state = charging
                ? WpfStrings.BatteryCharging
                : (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online
                    ? (batteryPercent >= 99 ? WpfStrings.BatteryFull : WpfStrings.BatteryNotCharging)
                    : WpfStrings.BatteryDischarging);
            powerState = " · " + source + " · " + state;
        }
        catch
        {
        }
        BatteryInfo.Text = WpfStrings.OriginalStyleWindow_xaml_Text_46 + batteryPercent + "%" + powerState + (string.IsNullOrWhiteSpace(health) ? "" : " · " + health) + abnormal;
        BatteryInfo.ToolTip = health;
        SyncNucExternalMode(probe);
        int mode = ResolveDisplayMode(probe);
        GpuOverclockSwitch.Visibility = (mode == 1 && probe.GpuOcSupported) ? Visibility.Visible : Visibility.Collapsed;
        GpuOverclockSwitch.IsChecked = MechrevoService.GpuOverclockEnabled || AppConfig.Is("gpu_oc");
        int currentGpu = probe.CurrentGpuMode;
        int pendingGpu = probe.PendingGpuTarget;
        bool pending = probe.HasPendingGpuRestart && pendingGpu >= 0;
        int displayGpu = pending ? pendingGpu : currentGpu;
        if (currentGpu >= 0 && currentGpu == pendingGpu)
        {
            probe.ClearGpuRestartPending();
            pending = false;
            displayGpu = currentGpu;
        }
        if (!_gpuPromptOpen)
        {
            UpdateGpuButtons(displayGpu, pending);
        }
        bool skipModeUi = _modeSwitchInFlight;
        bool silentTurboSupported = WideSilentTurboVisible;
        bool silentTurboActive = mode == 1 && silentTurboSupported && MechrevoService.IsSilentTurboActive;
        CurrentModeText.Text = WpfStrings.CurrentPrefix + (mode switch
        {
            2 => WpfStrings.ModeSilent,
            1 => silentTurboActive
                ? WpfStrings.SilentTurbo
                : silentTurboSupported ? WpfStrings.TurboHighEnergy : WpfStrings.ModeTurbo,
            0 => WpfStrings.ModeBalanced,
            MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode => WpfStrings.NucExtremeTurbo,
            3 or 4 => WpfStrings.ModeCustom,
            _ => "—"
        });
        if (!_modeSwitchInFlight && _pendingMode >= 0)
        {
            if (mode == _pendingMode || Environment.TickCount64 - _pendingModeTick > 15000)
            {
                _pendingMode = -1;
            }
            else
            {
                skipModeUi = true;
            }
        }
        if (!skipModeUi)
        {
            SilentModeButton.IsChecked = mode == 2;
            BalancedModeButton.IsChecked = mode == 0;
            if (SilentTurboModeButton != null) SilentTurboModeButton.IsChecked = silentTurboActive;
            TurboModeButton.IsChecked = mode == 1 && !silentTurboActive;
            ExtremeModeButton.IsChecked = mode == MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode;
            CustomModeButton.IsChecked = mode is 3 or 4;
            RefreshModeHighlights();
        }
        SyncLightSwitchesFromHardware();
        SyncRefreshRateState();
        RefreshPerfConfigVisibility(mode);
        RefreshPerfConfigSelections();
        // 能力位是异步到达的；每次刷新都重新确认 NUC 第三档可见，避免只显示静音/平衡。
        if (hw.IsIntelNucMachine)
        {
            if (!_modeSwitchInFlight && TurboModeButton.Visibility != Visibility.Visible)
            {
                TurboModeButton.Visibility = Visibility.Visible;
                RefreshPerfConfigVisibility(mode);
            }
            if (ExtremeModeButton.Visibility != Visibility.Visible)
            {
                ExtremeModeButton.Visibility = Visibility.Visible;
            }
            ApplyModeSegmentDensity();
        }
        // 能力位晚到/首次构建时 backend 未就绪会让 MoreGroupsPanel 为空；
        // 这里每次刷新补建一次（已构建时 BuildMoreSwitches 会立即返回）。
        ApplyModeSegmentDensity();
        BuildMoreSwitches();
        UpdateContentWidth();
        SyncGpuDbForMode(mode, "WPF 后端模式同步");
        if (!skipModeUi)
        {
            SyncCustomWindowMode(mode);
        }
        ToggleButton[] batteryButtons = { BatteryLongButton, BatteryBalancedButton, BatteryWorkButton };
        // 无极限充开启时三档高亮必须跟滑块走；否则 2s 刷新会用后端旧 BatteryMode
        // 覆盖滑块刚设置的高亮，表现为拖滑块时三个按钮来回跳。
        int batteryMode = ChargeSliderToggle.IsChecked == true
            ? ModeForPercent((int)Math.Round(BatteryLimitSlider.Value))
            : probe.BatteryMode;
        for (int i = 0; i < batteryButtons.Length; i++)
        {
            batteryButtons[i].IsChecked = i == batteryMode;
        }
        HighlightChecked(batteryButtons);
        UpdateQuickTileStates();
        if (MoreGroupsPanel != null)
        {
            foreach (CheckBox box in EnumerateCheckBoxes(MoreGroupsPanel))
            {
                if (box.Tag is string key)
                {
                    box.IsChecked = probe.QuickState(key);
                }
            }
            UpdateMoreSwitchCount();
        }
    }

    private static void RefreshXingyaoFanIfNeeded(MechrevoHw hw)
    {
        try
        {
            if (hw.CpuFanRpm > 0 && hw.GpuFanRpm > 0)
            {
                return;
            }
            if (!MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
            {
                return;
            }
            if (MechrevoLite.Hardware.XingyaoFanReader.TryReadFanRpm(out int main, out int second))
            {
                hw.ApplyXingyaoFanSpeeds(main, second);
            }
        }
        catch
        {
        }
    }

    /// <summary>CPU / GPU 那行读数：温度 + 风扇转速 + 风扇百分比。</summary>
    private static string SensorLine(MechrevoHw hw, bool cpu)
    {
        string line = (cpu ? "CPU " : "GPU ")
            + (cpu ? hw.CpuTemp : hw.GpuTemp) + "℃   "
            + (cpu ? hw.CpuFanRpm : hw.GpuFanRpm) + "rpm";
        string duty = FanDutyText(hw, cpu);
        return duty.Length == 0 ? line : line + " " + duty;
    }

    /// <summary>
    /// 风扇百分比（占空比）。读不到风扇信息、或该后端给不出百分比（只报转速）时不显示。
    /// </summary>
    private static string FanDutyText(MechrevoHw hw, bool cpu)
    {
        if (!hw.IsFanInfoFresh(MechrevoHw.FanInfoMaximumAge))
        {
            return "";
        }
        int? duty;
        int rpmFullPercent = hw.FanRpmMaxForPercent;
        if (rpmFullPercent > 0)
        {
            int rpm = cpu ? hw.CpuFanRpm : hw.GpuFanRpm;
            duty = rpm > 0 ? (int)Math.Round(rpm * 100.0 / rpmFullPercent) : null;
        }
        else
        {
            duty = cpu ? hw.CpuFanDuty : hw.GpuFanDuty;
        }
        if (!duty.HasValue || duty.GetValueOrDefault() < (hw.FanZeroIsMeaningful ? 0 : 1))
        {
            return "";
        }
        return duty.GetValueOrDefault() + "%";
    }

    private static void UpdateModeBorders(ToggleButton selected)
    {
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        Brush normalBackground = (Brush)Application.Current.Resources["ControlBrush"];
        FontFamily regular = (FontFamily)Application.Current.Resources["AppFontFamily"];
        FontFamily bold = (FontFamily)Application.Current.Resources["AppFontBoldFamily"];
        foreach (object child in ((Panel)selected.Parent).Children)
        {
            if (child is ToggleButton button)
            {
                button.BorderBrush = gray;
                button.BorderThickness = new Thickness(1);
                button.Background = normalBackground;
                button.FontFamily = regular;
                button.FontWeight = FontWeights.Normal;
            }
        }
        if (selected.Tag is string hex)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            selected.BorderBrush = new SolidColorBrush(color);
            selected.BorderThickness = new Thickness(3);
            selected.Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
            selected.FontFamily = bold;
            selected.FontWeight = FontWeights.Bold;
        }
    }

    private void BackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && BackgroundToggle.IsChecked == true && BackgroundImage.Source == null)
        {
            ReloadBackgroundImage();
        }
        UpdateBackgroundVisibility();
        if (IsLoaded)
        {
            AppConfig.Set("ui_bg_enabled", BackgroundToggle.IsChecked == true ? 1 : 0);
            AppConfig.Flush();
        }
    }

    internal void ReloadBackgroundImage()
    {
        try
        {
            string path = AppConfig.GetString("ui_bg_image") ?? "";
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                BackgroundImage.Source = bitmap;
            }
            else
            {
                BackgroundImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/background.jpg", UriKind.Absolute));
            }
            ApplyBackgroundAppearance();
        }
        catch
        {
            BackgroundImage.Source = null;
        }
    }

    internal void ApplyBackgroundAppearance()
    {
        int opacity = Math.Clamp(AppConfig.Get("ui_bg_opacity", 65), 0, 100);
        int scrim = Math.Clamp(AppConfig.Get("ui_bg_scrim", 50), 0, 100);
        BackgroundImage.Opacity = opacity / 100.0;
        BackgroundScrim.Opacity = scrim / 100.0;
        ApplyBackgroundBlur(BackgroundImage);
    }

    /// <summary>背景图高斯模糊：ui_bg_blur 0–40，0 = 不模糊。</summary>
    internal static void ApplyBackgroundBlur(System.Windows.Controls.Image? image)
    {
        if (image == null)
        {
            return;
        }
        try
        {
            int radius = Math.Clamp(AppConfig.Get("ui_bg_blur", 12), 0, 40);
            // 固定 overscan：开/关模糊时变换不变，只换 Effect，避免背景缩放造成“动一下”。
            // 必须以中心为原点；旧写法 new ScaleTransform(...,0.5,0.5) 是绝对坐标，会向左上/右下偏。
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            image.RenderTransform = new ScaleTransform(1.08, 1.08);
            if (radius <= 0)
            {
                image.Effect = null;
                return;
            }
            image.Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = radius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[ui] 背景模糊应用失败（已忽略）：" + ex.GetType().Name + " " + ex.Message);
            image.Effect = null;
        }
    }

    private void UpdateBackgroundVisibility()
    {
        bool show = BackgroundToggle.IsChecked == true;
        if (show)
        {
            // 迷你窗：背景开关优先于云母；先撤掉本窗口 Mica，再显示背景图。
            WindowBackdrop.Disable(this);
            BackgroundScrim.Fill = _dark
                ? new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255));
            BackgroundImage.Visibility = Visibility.Visible;
            BackgroundScrim.Visibility = Visibility.Visible;
            return;
        }
        BackgroundImage.Visibility = Visibility.Collapsed;
        BackgroundScrim.Visibility = Visibility.Collapsed;
        // 背景关闭：恢复云母（默认开；用户显式 solid 时 Apply 内部尊重配置）。
        WindowBackdrop.Apply(this);
    }

    internal void ApplyBackdropBackgroundState()
    {
        UpdateBackgroundVisibility();
    }


    private async void BackendStatus_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (GcuPackageImport.ShouldOffer())
            {
                using GcuOwnerWindow owner = new(this);
                await GcuPackageImport.OfferAsync(owner);
            }
            if (_backendProbe == null)
            {
                await ConnectBackendAsync();
                return;
            }
            UpdateBackendStatus();
            RefreshFromBackend();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_48 + ex.Message;
        }
    }

    private sealed class GcuOwnerWindow : System.Windows.Forms.IWin32Window, IDisposable
    {
        private readonly WindowInteropHelper _helper;

        public GcuOwnerWindow(Window window)
        {
            _helper = new WindowInteropHelper(window);
        }

        public nint Handle => _helper.Handle;

        public void Dispose()
        {
        }
    }

    private async void StartSilentUpdateCheckIfAllowed()
    {
        if (IsStartupLaunch())
        {
            Logger.WriteLine("[startup] startup 参数：跳过静默更新检查");
            return;
        }
        if (_updateCheckStarted
            || !MechrevoLite.Update.UpdateChecker.AutoCheckEnabled
            || L_Mechrevo.WpfP0.Services.UpdatePolicy.TemporarilyHidden)
        {
            return;
        }
        _updateCheckStarted = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            MechrevoLite.Update.UpdateInfo? info = await MechrevoLite.Update.UpdateChecker.CheckGitHubAsync();
            if (info?.UpdateAvailable == true)
            {
                Dispatcher.BeginInvoke(() => ToastService.Show(WpfStrings.UpdateAvailablePrefix + (info.LatestVersion ?? "") + WpfStrings.UpdateAvailableSuffix));
            }
        }
        catch
        {
        }
    }

    private void ShowFirstRunGuidesIfNeeded()
    {
        if (IsStartupLaunch())
        {
            Logger.WriteLine("[startup] startup 参数：跳过首次引导（留待正常启动）");
            return;
        }
        if (_firstRunChecked)
        {
            return;
        }
        _firstRunChecked = true;
        try
        {
            if (AppConfig.Get("onboarding_version", 0) < 1)
            {
                MechrevoHw? hw = _backendProbe?.Hw;
                if (!(hw?.UsesBaolongda ?? false))
                {
                    AppConfig.Set("igpu_only_asked", 1);
                    AppConfig.Flush();
                }
                // P0-4：首次使用引导已迁到 WPF（FirstRunGuideWindow），不再经 OriginalFormHostWindow
                // 托管 WinForms FirstRunGuideForm；步骤/配置键/完成动作与旧窗口逐项一致（见新窗口文件头）。
                FirstRunGuideWindow guide = new(igpuOnlyStepOnly: false)
                {
                    Owner = this
                };
                guide.ShowDialog();
                AppConfig.Set("onboarding_version", 1);
                AppConfig.Flush();
                return;
            }
            if (AppConfig.Get("igpu_only_asked", 0) != 1)
            {
                MechrevoHw? hw = _backendProbe?.Hw;
                bool baolongda = hw?.UsesBaolongda ?? false;
                bool needAsk = !baolongda && (hw == null || (hw.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.Gen30 && !hw.HasReliableIgpuOnlyCapability));
                if (needAsk)
                {
                    AppConfig.Set("igpu_only_asked", 1);
                    AppConfig.Flush();
                    // P0-4：单问模式同样改走 WPF（紧凑窗口、只问核显模式那一题、落盘逻辑不变）。
                    FirstRunGuideWindow guide = new(igpuOnlyStepOnly: true)
                    {
                        Owner = this
                    };
                    guide.ShowDialog();
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_49 + ex.Message;
        }
    }

    private void StartGpuBootChecksOnce()
    {
        if (_gpuBootChecksStarted)
        {
            return;
        }
        _gpuBootChecksStarted = true;
        MechrevoLite.Gpu.GpuRestartVerifier.NotifyOverride = (verdict, text) =>
        {
            void Show()
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                ToastService.Show(text, verdict == MechrevoLite.Gpu.GpuRestartVerdict.NotApplied);
            }
            if (Dispatcher.CheckAccess()) Show();
            else Dispatcher.BeginInvoke(new Action(Show));
        };
        _ = VerifyGpuRestartAfterBootAsync();
        _ = ApplyGpuOcGuardAfterBootAsync();
    }

    private async Task VerifyGpuRestartAfterBootAsync()
    {
        try
        {
            MechrevoLite.Gpu.GpuRestartVerdict verdict =
                await MechrevoLite.Gpu.GpuRestartVerifier.RunAfterBootAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(true);
            if (verdict == MechrevoLite.Gpu.GpuRestartVerdict.NotApplied)
            {
                RefreshFromBackend();
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[gpu-restart] 启动校验失败：" + ex.Message);
        }
    }

    private async Task ApplyGpuOcGuardAfterBootAsync()
    {
        try
        {
            MechrevoService? service = _backendProbe?.Service;
            if (service != null)
            {
                await MechrevoLite.Hardware.GpuOcShutdownGuard.ApplyIfUncleanShutdownAsync(service).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[gpu-oc] 开机保护失败：" + ex.Message);
        }
    }

    private async Task ConnectBackendAsync()
    {
        if (_connectAttempting)
        {
            return;
        }
        if (_backendProbe != null)
        {
            BackendLine.Text = _backendProbe.Snapshot();
            return;
        }
        _connectAttempting = true;
        // UI 探针预览：合成 Hw 已挂在 WpfBackend，不要再连真实后端覆盖能力位。
        if (MechrevoLite.Program.UiAuditMode
            && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is MechrevoHw previewHw)
        {
            try
            {
                _backendProbe = new BackendProbe(previewHw, null);
                _services = new AppServices(_backendProbe);
                BackendLine.Text = _backendProbe.Snapshot();
                UpdateBackendStatus();
                ApplyCapabilities();
                BackendReady?.Invoke();
                SyncGcuLanguage(MechrevoLite.AppConfig.GetString("language"));
                Logger.WriteLine("[ui-probe] 使用合成 Hw 预览，已跳过真实后端连接");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("[ui-probe] 合成 Hw 预览失败：" + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                _connectAttempting = false;
            }
            return;
        }
        SetBackendStatus(WpfStrings.BackendGcuConnecting, "#F59E0B", WpfStrings.BackendGcuConnectingTip);
        BackendLine.Text = WpfStrings.OriginalStyleWindow_xaml_Text_50;
        try
        {
            bool connected = await _backendHost.ConnectAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            BackendProbe? probe = _backendHost.Probe;
            if (!connected || probe == null)
            {
                string reason = _backendHost.TimedOut
                    ? WpfStrings.UpdateTimeoutStatus
                    : _backendHost.LastError;
                SetBackendStatus(WpfStrings.BackendGcuDisconnected, "#EF4444", WpfStrings.BackendGcuDisconnectedTip);
                BackendLine.Text = reason;
                _backendProbe = null;
                _backendRetryTimer.Start();
                return;
            }
            _backendProbe = probe;
            UpdateBackendStatus();
            BackendLine.Text = probe.Snapshot();
            _services = new AppServices(probe);
            try
            {
                if (MechrevoLite.Mode.PerfModeService.Instance == null)
                {
                    MechrevoLite.Mode.PerfModeService.Create(probe.Service!, probe.Hw);
                }
                _ = RestorePerfModeOnStartupAsync();
            }
            catch
            {
            }
            _backendRetryTimer.Stop();
            SyncGpuDbForMode(probe.CurrentMode, "WPF 启动同步");
            await probe.RefreshGpuModeAsync();
            ApplyCapabilities();
            BackendReady?.Invoke();
            SyncGcuLanguage(MechrevoLite.AppConfig.GetString("language"));
            StartGpuBootChecksOnce();
            ScheduleCapabilityWarmup();
            ShowFirstRunGuidesIfNeeded();
            StartSilentUpdateCheckIfAllowed();
            _backendTimer.Start();
        }
        finally
        {
            _connectAttempting = false;
        }
    }

    private void UpdateBackendStatus()
    {
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return;
        }
        // NUC/UMI 的多数功能走本地 ACPI/EC，不依赖 GCU；GCU 未连接也按“已就绪”显示。
        if (!probe.IsGcuConnected
            && (probe.Hw?.IsIntelNucMachine == true || probe.Hw?.IsUmiMachine == true))
        {
            SetBackendStatus(WpfStrings.BackendNonGcuReady, "#4ADE80", WpfStrings.L("NUC/UMI 直接 ACPI/EC 模式已就绪（无需 GCU）", "NUC/UMI direct ACPI/EC mode ready (GCU not required)"));
            return;
        }
        if (!probe.IsGcuConnected
            && probe.Connected
            && MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
        {
            SetBackendStatus(WpfStrings.BackendNonGcuReady, "#4ADE80",
                WpfStrings.L("星耀 WMAA 本地模式已就绪（无需 GCU）", "Xingyao WMAA local mode ready (GCU not required)"));
            return;
        }
        if (_services?.BackendStatus.IsNonGcuBackend ?? probe.IsNonGcuBackend)
        {
            bool ready = _services?.BackendStatus.Connected ?? probe.Connected;
            SetBackendStatus(
                ready ? WpfStrings.BackendNonGcuReady : WpfStrings.BackendNonGcuNotReady,
                ready ? "#4ADE80" : "#F59E0B",
                WpfStrings.BackendNonGcuTip);
            return;
        }
        if (_services?.BackendStatus.IsGcuConnected ?? probe.IsGcuConnected)
        {
            SetBackendStatus(WpfStrings.BackendGcuConnected, "#4ADE80", WpfStrings.BackendGcuConnectedTip);
            return;
        }
        if ((_services?.BackendStatus.IsReconnecting ?? probe.IsReconnecting) || probe.IsSessionAwaitingSubscribe)
        {
            SetBackendStatus(WpfStrings.BackendGcuConnecting, "#F59E0B", WpfStrings.BackendGcuConnectingTip);
            return;
        }
        SetBackendStatus(WpfStrings.BackendGcuDisconnected, "#EF4444", WpfStrings.BackendGcuDisconnectedTip);
    }

    private void SetBackendStatus(string text, string hex, string tooltip)
    {
        BackendStatus.Text = text;
        BackendStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        BackendStatus.ToolTip = tooltip;
    }

    private void EnsureSilentTurboButton(bool allowed)
    {
        if (allowed && SilentTurboModeButton == null)
        {
            ToggleButton button = new()
            {
                Tag = "#FB7185",
                Style = (Style)FindResource("ModeSegment"),
                Content = WpfStrings.SilentTurbo
            };
            button.Click += ModeButton_Click;
            int index = ModeGrid.Children.IndexOf(TurboModeButton);
            if (index < 0) index = ModeGrid.Children.Count;
            ModeGrid.Children.Insert(index, button);
            SilentTurboModeButton = button;
        }
        if (SilentTurboModeButton == null)
        {
            return;
        }
        if (!allowed)
        {
            ModeGrid.Children.Remove(SilentTurboModeButton);
            SilentTurboModeButton = null;
        }
    }

    /// <summary>迷你窗高度完全由用户拖动决定；这里只负责记住/恢复，不再按内容自动收放。</summary>
    private void RestoreSavedMiniHeight()
    {
        try
        {
            int saved = AppConfig.Get(MiniHeightConfigKey, 0);
            if (saved <= 0) return;
            _restoringMiniHeight = true;
            double max = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
            Height = Math.Clamp(saved, MinHeight, max);
        }
        catch
        {
        }
        finally
        {
            _restoringMiniHeight = false;
        }
    }

    private void ScheduleMiniHeightSave()
    {
        if (_restoringMiniHeight || _wideMode || !IsLoaded) return;
        _miniHeightSaveTimer.Stop();
        _miniHeightSaveTimer.Start();
    }

    private void SaveMiniHeight()
    {
        if (_restoringMiniHeight || _wideMode || !IsLoaded) return;
        try
        {
            int height = (int)Math.Round(ActualHeight);
            if (height >= MinHeight)
            {
                AppConfig.Set(MiniHeightConfigKey, height);
                AppConfig.Flush();
            }
        }
        catch
        {
        }
    }

    private void SyncMainScrollBar()
    {
        try
        {
            double scrollable = MainScroll.ScrollableHeight;
            bool visible = scrollable > 0.5;
            if (MainScrollBar.Visibility != (visible ? Visibility.Visible : Visibility.Collapsed))
            {
                MainScrollBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!visible) return;
            MainScrollBar.Maximum = scrollable;
            MainScrollBar.ViewportSize = MainScroll.ViewportHeight;
            MainScrollBar.SmallChange = 32;
            MainScrollBar.LargeChange = Math.Max(32, MainScroll.ViewportHeight * 0.8);
            _syncingMainScrollBar = true;
            MainScrollBar.Value = MainScroll.VerticalOffset;
        }
        catch
        {
        }
        finally
        {
            _syncingMainScrollBar = false;
        }
    }

    private void MainScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingMainScrollBar) return;
        try
        {
            MainScroll.ScrollToVerticalOffset(e.NewValue);
        }
        catch
        {
        }
    }

    private void ApplyCapabilities()
    {
        if (_backendProbe?.Capabilities is not MechrevoDeviceCapabilities caps)
        {
            return;
        }
        try
        {
            MechrevoHw? hw = _backendProbe?.Hw;
            bool umiGpuMode = hw?.IsUmiMachine == true;
            bool nucNoGpuSwitch = hw?.IsIntelNucMachine == true;
            bool gpuRowHidden = hw?.GpuRowHiddenByGcuReports == true;
            bool directSupported = hw?.SupportsDgpuDirect ?? caps.DgpuDirect;
            bool gpuSwitchSupported = hw?.CanOfferGpuModeSwitch ?? (caps.DgpuDirect || caps.IgpuOnly || caps.GpuHotSwap);
            GpuDirectButton.Visibility = directSupported ? Visibility.Visible : Visibility.Collapsed;
            GpuSection.Visibility = (!nucNoGpuSwitch && !gpuRowHidden
                && (gpuSwitchSupported || directSupported || caps.GpuHotSwap || umiGpuMode))
                ? Visibility.Visible
                : Visibility.Collapsed;
            ScreenSection.Visibility = (caps.DisplayRefresh || caps.ColorCalibration || caps.LcdOverdrive || caps.LocalDimming) ? Visibility.Visible : Visibility.Collapsed;
            bool hasKeyboard = caps.Keyboard || (hw?.SupportsKeyboard ?? false)
                || hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
            bool hasLightbar = caps.Lightbar || (hw?.SupportsLightbar ?? false)
                || hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
            bool hasLogo = caps.LogoLight || (hw?.SupportsLogoLight ?? false);
            bool xingyaoLightIdle = MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(hw);
            LightingSection.Visibility = (hasKeyboard || hasLightbar || hasLogo || xingyaoLightIdle) ? Visibility.Visible : Visibility.Collapsed;
            KeyboardLightRow.Visibility = hasKeyboard ? Visibility.Visible : Visibility.Collapsed;
            LogoLightRow.Visibility = hasLogo ? Visibility.Visible : Visibility.Collapsed;
            FillKeyboardEffects();
  FillLightChannelEffects();
  FillLightIdleOptions();
  _services?.Lighting.ApplyHardwareLightIdleTimer();
  L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.ApplyKeyboardAutoOffSetting();
  SyncLightSwitchesFromHardware();
  bool nucUmiModeUi = _backendProbe?.Hw?.IsIntelNucMachine == true || _backendProbe?.Hw?.IsUmiMachine == true;
            // Intel 星耀暂时屏蔽自定义模式；AMD 星耀/无界走本机 SMU 自定义。
            bool smuCustom = hw?.SmuCustomApu == true;
            bool customAllowed = nucUmiModeUi || smuCustom
                || ((caps.CpuPerformanceTuning || caps.FanSettings)
                    && !(_backendProbe?.Hw?.SupportsModeProfileConfig ?? false));
            CustomModeButton.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
            ExtremeModeButton.Visibility = hw?.IsIntelNucMachine == true ? Visibility.Visible : Visibility.Collapsed;
            ApplyModeSegmentDensity();
            // 原版只保证静音/平衡；部分 30 系机型没有狂暴档 ⇒ 隐藏狂暴按钮，30 系配置行随之隐藏。
            // 仅 Intel NUC/UMI 的原厂控制台在 TurboModeSupport=0 时仍提供三档；
        // LNL 无界/星耀等办公本如果上报不支持狂暴，必须继续隐藏，不能强制显示。
        bool vendorNucModes = hw?.IsIntelNucMachine == true
            && hw.WujieSmuApu != true
            && hw.SmuCustomApu != true;
        TurboModeButton.Visibility = (ShouldShowTurboMode(caps, hw) || vendorNucModes)
            ? Visibility.Visible
            : Visibility.Collapsed;
            // 50 系默认两个狂暴；其它机型默认只显示狂暴。GCU 明确不支持才隐藏。
            bool silentTurboAllowed = ShouldShowSilentTurbo(caps, hw);
            EnsureSilentTurboButton(silentTurboAllowed);
            // 50 系的静音狂暴是在这里动态插入的；插入后必须重新按 5 按钮密度排版。
            ApplyModeSegmentDensity();
            if (TurboModeButton.Content is TextBlock turboLabel)
            {
                turboLabel.Text = silentTurboAllowed ? WpfStrings.TurboHighEnergy : WpfStrings.OriginalStyleWindow_Text_22;
            }
            BuildMoreSwitches();
            // 动态插入/显示第 5 个模式按钮后，重新按当前窗口宽度分配各 section，
            // 否则 WrapPanel 子项会按内容宽度撑开，右侧被窗口裁掉。
            UpdateContentWidth();
            BackendProbe probe = _backendProbe;
            FanTile.Visibility = probe.SupportsQuick("fanboost") ? Visibility.Visible : Visibility.Collapsed;
            CameraTile.Visibility = probe.SupportsQuick("webcam") ? Visibility.Visible : Visibility.Collapsed;
            MicTile.Visibility = probe.SupportsQuick("micmute") ? Visibility.Visible : Visibility.Collapsed;
            WinKeyTile.Visibility = probe.SupportsQuick("winkey") ? Visibility.Visible : Visibility.Collapsed;
            ScreenOffTile.Visibility = probe.SupportsQuick("monitoroff") ? Visibility.Visible : Visibility.Collapsed;
            InitScreenColorControls();
            ApplyUiVisibilityFromConfig();
            ApplyBatteryLimitVisibility();
        }
        catch
        {
        }
    }

    internal void ApplyUiVisibilityFromConfig()
    {
        if (_backendProbe == null)
        {
            return;
        }
        MechrevoHw? hw = _backendProbe.Hw;
        MechrevoDeviceCapabilities? caps = _backendProbe.Capabilities;
        string igpuMode = AppConfig.GetString("igpu_only_ui") ?? "auto";
        bool igpuSupported = hw?.CanOfferIgpuOnly ?? ((caps?.IgpuOnly ?? false) || (hw?.SupportsIgpuOnly ?? false));
        if (hw?.GpuRowHiddenByGcuReports == true)
        {
            GpuEcoButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            GpuEcoButton.Visibility = ResolveRowVisibility(igpuMode, igpuSupported);
        }
        GpuEcoColumn.Width = GpuEcoButton.Visibility == Visibility.Visible
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (GpuEcoButton.Visibility == Visibility.Visible && hw?.IsIntelNucMachine != true)
        {
            GpuSection.Visibility = Visibility.Visible;
        }
        bool keyboard = (caps?.Keyboard ?? false) || (hw?.SupportsKeyboard ?? false)
            || hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
        bool hasStrip = (caps?.Lightbar ?? false) || (hw?.SupportsLightbar ?? false);
        bool hasLogo = (caps?.LogoLight ?? false) || (hw?.SupportsLogoLight ?? false);
        bool hasHinge = _backendProbe?.SupportsHingeLight == true;
        bool hasSync = _backendProbe?.SupportsSyncLight == true;
        KeyboardLightRow.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        LightStripRow.Visibility = ResolveRowVisibility(AppConfig.GetString("light_strip_ui") ?? "auto", hasStrip);
        LogoLightRow.Visibility = ResolveRowVisibility(AppConfig.GetString("light_logo_ui") ?? "auto", hasLogo);
        LightHingeRow.Visibility = hasHinge ? Visibility.Visible : Visibility.Collapsed;
        LightSyncRow.Visibility = hasSync ? Visibility.Visible : Visibility.Collapsed;
        InitPerfConfigRow();
        RefreshPerfConfigVisibility(_services?.Performance.CurrentMode ?? _backendProbe?.CurrentMode ?? -1);
        // ⭐⭐ 2026-10-26（task-18）：实验行/EC 直写按**能力位**显隐（Gen30 / 宝龙达 / 无界 / GCU），
        //   不再只看“显卡是不是 Gen30”。不支持 ⇒ 连行都不留（与旧 WinForms ChargeSliderSupported / IsEcDirectWriteAllowed 同源）。
        bool chargeExperimentSupported = WideChargeExperimentSupported;
        bool ecDirectSupported = WideEcDirectSupported;
        ChargeSliderToggle.Visibility = chargeExperimentSupported ? Visibility.Visible : Visibility.Collapsed;
        EcDirectOffToggle.Visibility = ecDirectSupported ? Visibility.Visible : Visibility.Collapsed;
        if (!chargeExperimentSupported)
        {
            BatteryLimitSlider.Visibility = Visibility.Collapsed;
            BatteryLimitSliderText.Visibility = Visibility.Collapsed;
        }
        bool liquidSupported = (caps?.LiquidCooling ?? false) || (hw?.SupportsLiquidCooling ?? false);
        LiquidSection.Visibility = (AppConfig.Is("force_liquid_cooling_menu") || liquidSupported) ? Visibility.Visible : Visibility.Collapsed;
        RefreshLiquidCoolingUi();
        _wideWindow?.RefreshLiquidCooling();
        bool xingyaoLightIdle = MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(hw);
        LightingSection.Visibility = (KeyboardLightRow.Visibility == Visibility.Visible
            || LightStripRow.Visibility == Visibility.Visible
            || LogoLightRow.Visibility == Visibility.Visible
            || LightHingeRow.Visibility == Visibility.Visible
            || LightSyncRow.Visibility == Visibility.Visible
            || xingyaoLightIdle) ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            WideUiVisibilityChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[wide-ui] 通知宽屏刷新可见性失败（已吞掉）：" + ex.Message);
        }
    }

    private static Visibility ResolveRowVisibility(string mode, bool supported)
    {
        if (string.Equals(mode, "show", StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Visible;
        }
        if (string.Equals(mode, "hide", StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Collapsed;
        }
        return supported ? Visibility.Visible : Visibility.Collapsed;
    }

    private WaterCoolerRoutingService? EnsureWaterCoolerRouting()
    {
        if (_waterCoolerRouting != null)
        {
            return _waterCoolerRouting;
        }
        if (_backendProbe == null)
        {
            return null;
        }
        try
        {
            _waterCoolerRouting = new WaterCoolerRoutingService(_backendProbe.Hw, _backendProbe.Service);
            _waterCoolerRouting.RouteChanged += () =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(RefreshLiquidCoolingUi));
                }
                catch
                {
                }
            };
            _ = _waterCoolerRouting.InitializeAsync();
            return _waterCoolerRouting;
        }
        catch
        {
            return null;
        }
    }

    internal bool WideLiquidVisible =>
        MechrevoLite.AppConfig.Is("force_liquid_cooling_menu")
        || EnsureWaterCoolerRouting()?.Route != LiquidCoolingControlRoute.None
        || _backendProbe?.Hw?.SupportsLiquidCooling == true
        || _backendProbe?.Capabilities?.LiquidCooling == true;

    internal bool WideLiquidSupported => EnsureWaterCoolerRouting()?.Route is LiquidCoolingControlRoute.Gcu or LiquidCoolingControlRoute.DirectBle;

    internal bool WideLiquidConnected
    {
        get
        {
            WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
            if (routing == null)
            {
                return false;
            }
            return routing.IsGcuRoute ? _backendProbe?.Hw?.LcReportedConnected == true : routing.IsBleConnected;
        }
    }

    internal int WideLiquidPump => EnsureWaterCoolerRouting()?.PumpProfile ?? -1;

    internal int WideLiquidFan => EnsureWaterCoolerRouting()?.FanProfile ?? -1;

    internal string WideLiquidStatus => EnsureWaterCoolerRouting()?.StatusText ?? WpfStrings.OriginalStyleWindow_xaml_Content_3;

    internal async Task<bool> WideSetLiquidPumpAsync(int profile)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || !routing.IsGcuRoute && !routing.IsBleRoute)
        {
            return false;
        }
        bool ok = await routing.SetPumpAsync(profile);
        if (ok)
        {
            RefreshLiquidCoolingUi();
            RefreshFromBackend();
        }
        return ok;
    }

    internal async Task<bool> WideSetLiquidFanAsync(int profile)
    {
        bool ok = await DashboardCommands.SetLiquidFanAsync(profile);
        if (ok)
        {
            RefreshLiquidCoolingUi();
            RefreshFromBackend();
        }
        return ok;
    }

    internal async Task<bool> WideRefreshLiquidAsync()
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null)
        {
            return false;
        }
        try
        {
            bool ok = await routing.RefreshAsync();
            RefreshLiquidCoolingUi();
            RefreshFromBackend();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    internal Task<bool> WideLiquidStatusActionAsync()
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing?.IsBleRoute == true)
        {
            routing.Disconnect();
            RefreshLiquidCoolingUi();
            return Task.FromResult(true);
        }
        return WideRefreshLiquidAsync();
    }

    internal WaterCoolerRoutingService? WideLiquidRouting => EnsureWaterCoolerRouting();

    internal bool WideLiquidCurveSupported => EnsureWaterCoolerRouting()?.IsBleRoute == true;

    internal IReadOnlyList<WaterCoolerCurvePoint> WideLiquidCurvePoints =>
        EnsureWaterCoolerRouting()?.CurvePoints ?? WaterCoolerCurve.DefaultPoints;

    internal bool WideLiquidCurveEnabled => EnsureWaterCoolerRouting()?.CurveEnabled == true;

    internal void WideOpenLiquidCurve()
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || !routing.IsBleRoute)
        {
            return;
        }
        new WaterCoolerCurveWindow(routing) { Owner = this }.ShowDialog();
        RefreshLiquidCoolingUi();
        _wideWindow?.RefreshLiquidCooling();
    }

    internal async Task<bool> WideSetLiquidCurveAsync(IReadOnlyList<WaterCoolerCurvePoint> points, int pumpStart = 55, int pumpTurbo = 90)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null)
        {
            return false;
        }
        bool ok = await routing.SetCurveAsync(points, pumpStart, pumpTurbo);
        if (ok)
        {
            RefreshLiquidCoolingUi();
            _wideWindow?.RefreshLiquidCooling();
        }
        return ok;
    }

    internal bool WideLiquidLightSupported => EnsureWaterCoolerRouting()?.SupportsLight == true;

    internal System.Drawing.Color WideLiquidLightColor => EnsureWaterCoolerRouting()?.LightColor ?? LiquidCoolingLightService.SavedColor;

    internal string WideLiquidLightProfile => EnsureWaterCoolerRouting()?.LightProfile ?? LiquidCoolingLightService.SavedProfile;

    internal IReadOnlyList<LiquidLightOption> WideLiquidLightOptions =>
        EnsureWaterCoolerRouting()?.LightOptions ?? LiquidCoolingLightService.Options(_backendProbe?.Hw);

    internal async Task<bool> WideApplyLiquidLightAsync(string profile, System.Drawing.Color color)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || !routing.SupportsLight)
        {
            return false;
        }
        bool ok = await routing.ApplyLightAsync(profile, color);
        if (ok)
        {
            RefreshLiquidCoolingUi();
            RefreshFromBackend();
        }
        return ok;
    }

    private void RefreshLiquidCoolingUi()
    {
        EnsureLiquidLightRow();
        try
        {
            WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
            bool force = MechrevoLite.AppConfig.Is("force_liquid_cooling_menu");
            bool visible = force
                || routing?.Route != LiquidCoolingControlRoute.None
                || _backendProbe?.Hw?.SupportsLiquidCooling == true
                || _backendProbe?.Capabilities?.LiquidCooling == true;
            LiquidSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }
            bool gcuSupported = routing?.IsGcuRoute == true
                || (routing == null && _backendProbe?.Hw?.SupportsLiquidCooling == true);
            bool bleSupported = routing?.IsBleRoute == true;
            if (!gcuSupported && !bleSupported)
            {
                LiquidStatusButton.Content = WpfStrings.OriginalStyleWindow_xaml_Content_3;
                LiquidPumpCombo.IsEnabled = false;
                LiquidFanCombo.IsEnabled = false;
                LiquidCurveButton.Visibility = Visibility.Visible;
            LiquidPairButton.Visibility = Visibility.Visible;
                LiquidPairButton.Visibility = Visibility.Visible;
                RefreshLiquidLightUi();
                return;
            }
            LiquidStatusButton.Content = WideLiquidConnected ? "已连接" : "未连接";
            LiquidPumpCombo.IsEnabled = true;
            LiquidFanCombo.IsEnabled = true;
            LiquidCurveButton.Visibility = Visibility.Visible;
            _syncingLiquid = true;
            SelectComboByTag(LiquidPumpCombo, routing?.PumpProfile ?? _backendProbe?.Hw?.LcPumpControl ?? -1);
            if (routing?.CurveEnabled == true)
            {
                SelectComboByString(LiquidFanCombo, "curve");
            }
            else
            {
                SelectComboByTag(LiquidFanCombo, routing?.FanProfile ?? _backendProbe?.Hw?.LcFanControl ?? -1);
            }
            _syncingLiquid = false;
            RefreshLiquidLightUi();
        }
        catch
        {
        }
    }

    private void EnsureLiquidLightRow()
    {
        if (_liquidLightRow != null)
        {
            return;
        }
        Grid row = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = WpfStrings.TitleText_Text_2,
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        _liquidLightCombo = new ComboBox
        {
            Style = (Style)FindResource("RoundCombo"),
            Margin = new Thickness(0, 0, 12, 0)
        };
        _liquidLightCombo.SelectionChanged += async (_, _) => await LiquidLight_SelectionChangedAsync();
        Grid.SetColumn(_liquidLightCombo, 1);
        row.Children.Add(_liquidLightCombo);
        _liquidLightColorButton = new Button
        {
            Content = WpfStrings.MultiColorLabel_Text,
            Style = (Style)FindResource("MiniButton"),
            MinWidth = 52,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _liquidLightColorButton.Click += async (_, _) => await LiquidColor_ClickAsync();
        Grid.SetColumn(_liquidLightColorButton, 2);
        row.Children.Add(_liquidLightColorButton);
        _liquidLightStatus = new TextBlock
        {
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_liquidLightStatus, 3);
        row.Children.Add(_liquidLightStatus);
        // 水冷灯光行必须放在 LiquidBody 内部：
        // 放到 LiquidSection.Children 会在水冷折叠后留在标题下面，
        // 表现为“水冷控制叠在一起 / 灯光行悬空”。
        LiquidBody.Children.Add(row);
        _liquidLightRow = row;
    }

    private void RefreshLiquidLightUi()
    {
        if (_liquidLightRow == null)
        {
            return;
        }
        bool supported = WideLiquidLightSupported;
        // 水冷区可见时，灯光行也要可见；没有水冷/没有灯光能力时置灰并说明，
        // 不再整行隐藏（机主原话：看不到水冷灯光控制 UI）。
        _liquidLightRow.Visibility = LiquidSection.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!supported)
        {
            if (_liquidLightCombo != null)
            {
                _liquidLightCombo.Items.Clear();
                _liquidLightCombo.Items.Add(new ComboBoxItem { Content = "未连接/不支持", Tag = "" });
                _liquidLightCombo.SelectedIndex = 0;
                _liquidLightCombo.IsEnabled = false;
            }
            if (_liquidLightColorButton != null)
            {
                _liquidLightColorButton.IsEnabled = false;
            }
            if (_liquidLightStatus != null)
            {
                _liquidLightStatus.Text = "水冷未连接";
            }
            return;
        }
        IReadOnlyList<LiquidLightOption> options = WideLiquidLightOptions;
        string saved = WideLiquidLightProfile;
        int selected = 0;
        _syncingLiquidLight = true;
        try
        {
            if (_liquidLightCombo != null)
            {
                _liquidLightCombo.Items.Clear();
                for (int i = 0; i < options.Count; i++)
                {
                    LiquidLightOption option = options[i];
                    _liquidLightCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Profile });
                    if (string.Equals(option.Profile, saved, StringComparison.Ordinal))
                    {
                        selected = i;
                    }
                }
                _liquidLightCombo.SelectedIndex = selected;
                _liquidLightCombo.IsEnabled = true;
            }
            if (_liquidLightColorButton != null)
            {
                _liquidLightColorButton.Background = new SolidColorBrush(ToMediaColor(WideLiquidLightColor));
                _liquidLightColorButton.IsEnabled = true;
            }
            if (_liquidLightStatus != null)
            {
                _liquidLightStatus.Text = string.IsNullOrWhiteSpace(saved) ? "" : WpfStrings.SavedPrefix + saved;
            }
        }
        finally
        {
            _syncingLiquidLight = false;
        }
    }

    private async Task LiquidLight_SelectionChangedAsync()
    {
        if (_syncingLiquidLight || _liquidLightCombo?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string profile = item.Tag as string ?? "";
        if (profile.Length == 0)
        {
            return;
        }
        bool ok = await WideApplyLiquidLightAsync(profile, WideLiquidLightColor);
        if (_liquidLightStatus != null)
        {
            _liquidLightStatus.Text = ok ? WpfStrings.LightLiquidSent : WpfStrings.LightLiquidFailed;
        }
    }

    private async Task LiquidColor_ClickAsync()
    {
        ColorPickerWindow picker = new(WideLiquidLightColor);
        if (picker.ShowDialog() != true || picker.Result is not System.Drawing.Color color)
        {
            return;
        }
        EnsureWaterCoolerRouting()?.SaveLightColor(color);
        string profile = WideLiquidLightProfile;
        if (!LiquidCoolingLightService.IsCustom(profile))
        {
            profile = "custom_static";
        }
        bool ok = await WideApplyLiquidLightAsync(profile, color);
        if (_liquidLightStatus != null)
        {
            _liquidLightStatus.Text = ok ? WpfStrings.LiquidColorSent : WpfStrings.LiquidColorFailed;
        }
    }

    private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color color) =>
        System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static void SelectComboByString(ComboBox combo, string value)
    {
        if (combo == null)
        {
            return;
        }
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem comboItem
                && string.Equals(comboItem.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                int index = combo.Items.IndexOf(comboItem);
                if (combo.SelectedIndex != index)
                {
                    combo.SelectedIndex = index;
                }
                return;
            }
        }
    }

    private static void SelectComboByTag(ComboBox combo, int value)
    {
        if (combo == null)
        {
            return;
        }
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem comboItem
                && int.TryParse(comboItem.Tag as string, out int tag)
                && tag == value)
            {
                int index = combo.Items.IndexOf(comboItem);
                if (combo.SelectedIndex != index)
                {
                    combo.SelectedIndex = index;
                }
                return;
            }
        }
    }

    private async void LiquidStatus_Click(object sender, RoutedEventArgs e)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing?.IsBleRoute == true)
        {
            routing.Disconnect();
            RefreshLiquidCoolingUi();
            StatusText.Text = WpfStrings.LiquidBleDisconnected;
            return;
        }
        if (_backendProbe?.Service == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_51;
            return;
        }
        try
        {
            LiquidStatusButton.IsEnabled = false;
            await _backendProbe.Service.RefreshLiquidCoolingStatus();
            RefreshLiquidCoolingUi();
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_52;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_53 + ex.Message;
        }
        finally
        {
            LiquidStatusButton.IsEnabled = true;
        }
    }

    private async void LiquidPump_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLiquid || LiquidPumpCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || (!routing.IsGcuRoute && !routing.IsBleRoute))
        {
            return;
        }
        int profile = int.TryParse(item.Tag as string, out int parsed) ? parsed : -1;
        bool ok = await routing.SetPumpAsync(profile);
        StatusText.Text = ok ? WpfStrings.LiquidPumpSent : WpfStrings.LiquidPumpFailed;
    }

    private async void LiquidFan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLiquid || LiquidFanCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string tag = item.Tag as string ?? "";
        if (string.Equals(tag, "curve", StringComparison.OrdinalIgnoreCase))
        {
            LiquidCurve_Click(sender, e);
            return;
        }
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || (!routing.IsGcuRoute && !routing.IsBleRoute))
        {
            return;
        }
        int profile = int.TryParse(tag, out int parsed) ? parsed : -1;
        bool ok = await routing.SetFanAsync(profile);
        StatusText.Text = ok ? WpfStrings.LiquidFanSent : WpfStrings.LiquidFanFailed;
    }

    private void LiquidCurve_Click(object sender, RoutedEventArgs e)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        new WaterCoolerCurveWindow(routing) { Owner = this }.ShowDialog();
        RefreshLiquidCoolingUi();
    }

    private void LiquidPair_Click(object sender, RoutedEventArgs e)
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        new WaterCoolerPairingWindow(routing) { Owner = this }.ShowDialog();
        RefreshLiquidCoolingUi();
    }

    internal void WideOpenLiquidPairing()
    {
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        new WaterCoolerPairingWindow(routing) { Owner = this }.ShowDialog();
        RefreshLiquidCoolingUi();
        _wideWindow?.RefreshLiquidCooling();
    }



    private void InitPerfConfigRow()
    {
        if (_perfConfigBuilt)
        {
            return;
        }
        _perfConfigBuilt = true;
        int count = Math.Max(1, MechrevoLite.Mode.ModeProfileConfig.Count);
        ComboBox[] combos = { PerfConfigSilentCombo, PerfConfigBalancedCombo, PerfConfigTurboCombo };
        _syncingPerfConfig = true;
        foreach (ComboBox combo in combos)
        {
            combo.Items.Clear();
            for (int i = 0; i < count; i++)
            {
                combo.Items.Add(new ComboBoxItem { Content = WpfStrings.OriginalStyleWindow_xaml_Content_3 + (i + 1), Tag = i.ToString() });
            }
        }
        _syncingPerfConfig = false;
    }

    private void RefreshPerfConfigVisibility(int currentMode)
    {
        MechrevoHw? hw = _backendProbe?.Hw;
        bool profileConfig = hw?.SupportsModeProfileConfig == true
                && !(hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true);
        bool show = profileConfig && currentMode >= 0 && currentMode <= 2;
        // 30 系配置下拉必须和对应性能模式按钮一起显隐；部分 30 系只有静音/平衡两档。
        PerfConfigSilentGroup.Visibility = SilentModeButton.Visibility;
        PerfConfigBalancedGroup.Visibility = BalancedModeButton.Visibility;
        PerfConfigTurboGroup.Visibility = TurboModeButton.Visibility;
        bool anyModeVisible = PerfConfigSilentGroup.Visibility == Visibility.Visible
            || PerfConfigBalancedGroup.Visibility == Visibility.Visible
            || PerfConfigTurboGroup.Visibility == Visibility.Visible;
        PerfConfigRow.Visibility = (show && anyModeVisible) ? Visibility.Visible : Visibility.Collapsed;
        PerfConfigPanel.Visibility = (show && anyModeVisible) ? Visibility.Visible : Visibility.Collapsed;
        PerfConfigEditButton.Visibility = (show && anyModeVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPerfConfigSelections()
    {
        try
        {
            // 显式绑定三个视觉模式，⛔ 不再依赖 ComboBox.Tag 反查；
            // 避免切模式/切配置时把另一个模式的配置号读到当前下拉。
            (ComboBox Combo, int VisualMode)[] combos =
            {
                (PerfConfigSilentCombo, 2),
                (PerfConfigBalancedCombo, 0),
                (PerfConfigTurboCombo, 1)
            };
            _syncingPerfConfig = true;
            foreach ((ComboBox combo, int visualMode) in combos)
            {
                combo.SelectedIndex = Math.Clamp(
                    MechrevoLite.Mode.ModeProfileConfig.Get(visualMode),
                    0,
                    Math.Max(0, combo.Items.Count - 1));
            }
            _syncingPerfConfig = false;
        }
        catch
        {
            _syncingPerfConfig = false;
        }
    }

    private async void PerfConfig_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPerfConfig || sender is not ComboBox combo || combo.Tag is not string tag
            || !int.TryParse(tag, out int visualMode) || combo.SelectedIndex < 0)
        {
            return;
        }
        MechrevoLite.Mode.ModeProfileConfig.Set(visualMode, combo.SelectedIndex);
        // 先立即回填三行，保证当前下拉/其它模式下拉不读旧值。
        RefreshPerfConfigSelections();
        if (_backendProbe != null && _backendProbe.Service?.CurrentMode == visualMode)
        {
            // 走统一切档入口：会标记 pending、等 backend 确认，finally 再刷新 UI。
            await SwitchModeAsync(visualMode);
        }
        RefreshPerfConfigSelections();
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_54 + combo.SelectedIndex + WpfStrings.ModeSeparatorSuffix + visualMode + WpfStrings.ClosingParen;
    }

    private void PerfConfigEdit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_backendProbe == null)
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_55;
                return;
            }
            L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
            int visualMode = _backendProbe.Service?.CurrentMode ?? 0;
            int profileIndex = MechrevoLite.Mode.ModeProfileConfig.Get(visualMode);
            if (_customModeWindow == null || !_customModeWindow.IsVisible)
            {
                _customModeWindow = new CustomModeWindow { Owner = this };
            }
            _customModeWindow.SetDark(_dark);
            _customModeWindow.BindFirmwareProfile(visualMode, profileIndex);
            _customModeWindow.Show();
            _customModeWindow.Activate();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_56 + ex.Message;
        }
    }

    private async void CustomModeButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await OpenCustomModeFormAsync(switchMode: false);
    }

    private string ResolveCustomModeId()
    {
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        if (perf != null)
        {
            string active = perf.DisplayedModeId;
            foreach (MechrevoLite.Mode.PerfModeDefinition mode in perf.Modes)
            {
                if (mode.IsCustom && string.Equals(mode.Id, active, StringComparison.Ordinal))
                {
                    return mode.Id;
                }
            }
            foreach (MechrevoLite.Mode.PerfModeDefinition mode in perf.Modes)
            {
                if (mode.IsCustom)
                {
                    return mode.Id;
                }
            }
        }
        return MechrevoLite.Mode.PerfModeStore.ActiveModeId;
    }

    private async Task OpenCustomModeFormAsync(bool switchMode)
    {
        try
        {
            if (_backendProbe == null)
            {
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_57;
                return;
            }
            L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
            string customId = ResolveCustomModeId();
            if (_customModeWindow == null)
            {
                _customModeWindow = new CustomModeWindow(customId) { Owner = this };
                _customModeWindow.SetDark(_dark);
                _customModeWindow.Closed += (_, _) => _customModeWindow = null;
            }
            if (!_customModeWindow.IsVisible)
            {
                _customModeWindow.Show();
            }
            _customModeWindow.Activate();
            _customWindowModeId = customId;
            if (switchMode)
            {
                if (!await _customModeWindow.SelectModeAsync(customId))
                {
                    _customModeWindow.BindMode(MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId);
                }
            }
            else
            {
                _customModeWindow.BindMode(customId);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteError("[custom-open] 打开自定义模式失败：" + ex);
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_20 + ex.Message;
        }
    }

    private async void GpuOverclock_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = GpuOverclockSwitch.IsChecked == true;
        StatusText.Text = on ? WpfStrings.GpuOverclockOnPending : WpfStrings.GpuOverclockOffPending;
        bool ok = await ApplyGpuOverclockSettingAsync(on);
        StatusText.Text = ok ? WpfStrings.GpuOverclockPrefix + (on ? WpfStrings.IgpuSavingOnPreview_Content : WpfStrings.OriginalStyleWindow_Content_5) : WpfStrings.GpuOverclockFailed;
    }

    /// <summary>迷你窗/宽屏共用：50 GCU 显卡超频下发。</summary>
    internal async Task<bool> WideSetGpuOverclockAsync(bool on)
    {
        if (_backendProbe == null)
        {
            return false;
        }
        return await ApplyGpuOverclockSettingAsync(on);
    }

    private async Task<bool> ApplyGpuOverclockSettingAsync(bool on)
    {
        if (_backendProbe == null)
        {
            return false;
        }
        MechrevoService.GpuOverclockEnabled = on;
        int modeBefore = _backendProbe.CurrentMode;
        bool ok = await _backendProbe.ApplyGpuOverclockAsync(on);
        // 部分 50 GCU 在只写 OC 字段后会把 operating mode 落回平衡；超频开关本身不应切模式，
        // 原来在狂暴档时把档位恢复回去（不改变 OC 字段）。
        if (ok && modeBefore == 1)
        {
            int actual = await _backendProbe.RefreshCurrentModeAsync();
            if (actual >= 0 && actual != 1)
            {
                await SwitchModeAsync(1);
            }
        }
        if (ok)
        {
            AppConfig.Set("gpu_oc", on ? 1 : 0);
            AppConfig.Flush();
        }
        return ok;
    }

    internal bool WideGpuOverclockVisible
    {
        get
        {
            try
            {
                MechrevoHw? hw = _backendProbe?.Hw;
                // 与迷你窗 GpuOverclockSwitch 同一条件：只在狂暴档且后端支持超频时显示；
                // 不额外按 DgpuGeneration 硬屏蔽（50 系 GCU 支持时即可用）。
                return hw != null
                    && _backendProbe?.GpuOcSupported == true
                    && WideCurrentMode == 1;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool WideGpuOverclockEnabled
    {
        get
        {
            try
            {
                return MechrevoService.GpuOverclockEnabled || AppConfig.Is("gpu_oc");
            }
            catch
            {
                return false;
            }
        }
    }

    private void InitScreenColorControls()
    {
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return;
        }
        _syncingCalibration = true;
        try
        {
            int level = probe.ColorCalibrationLevel;
            int selected = AppConfig.Exists("calib_mode") ? AppConfig.Get("calib_mode") : 1;
            CalibrationCombo.Items.Clear();
            CalibrationCombo.Items.Add(new ComboBoxItem { Content = WpfStrings.OriginalStyleWindow_xaml_Content_4, Tag = 1 });
            if (level >= 2)
            {
                CalibrationCombo.Items.Add(new ComboBoxItem { Content = "sRGB", Tag = 2 });
            }
            if (level >= 3)
            {
                CalibrationCombo.Items.Add(new ComboBoxItem { Content = "P3", Tag = 3 });
            }
            if (level >= 4)
            {
                CalibrationCombo.Items.Add(new ComboBoxItem { Content = "AdobeRGB", Tag = 4 });
            }
            bool customAvailable = AppConfig.Is("calib_custom_on") || IccHelper.ListCustomIccFiles().Count > 0;
            if (customAvailable)
            {
                CalibrationCombo.Items.Add(new ComboBoxItem { Content = WpfStrings.OriginalStyleWindow_xaml_Content_5, Tag = 999 });
            }
            int index = 0;
            for (int i = 0; i < CalibrationCombo.Items.Count; i++)
            {
                if (CalibrationCombo.Items[i] is ComboBoxItem item && (int)item.Tag == selected)
                {
                    index = i;
                    break;
                }
            }
            CalibrationCombo.SelectedIndex = index;
            bool calibrationOn = AppConfig.Exists("calib_enabled") && AppConfig.Is("calib_enabled");
            CalibrationSwitch.IsChecked = calibrationOn;
            CalibrationCombo.IsEnabled = calibrationOn;
            CalibrationSettingsButton.IsEnabled = calibrationOn;
            CalibrationSwitch.IsEnabled = probe.IsGcuConnected || !probe.IsNonGcuBackend;
        }
        finally
        {
            _syncingCalibration = false;
        }

        _syncingDisplayEffect = true;
        try
        {
            DisplayEffectCombo.Items.Clear();
            string[] modes = { "VibrantMode", "InternetMode", "VideoMode", "LowBlueMode", "CinemaMode", "PhotoMode" };
            string[] labels = { WpfStrings.DisplayVibrant, WpfStrings.DisplayInternet, WpfStrings.DisplayVideo, WpfStrings.DisplayLowBlue, WpfStrings.DisplayCinema, WpfStrings.DisplayPhoto };
            for (int i = 0; i < modes.Length; i++)
            {
                DisplayEffectCombo.Items.Add(new ComboBoxItem { Content = labels[i], Tag = modes[i] });
            }
            string selectedMode = AppConfig.GetString("display_effect_mode") ?? "";
            int index = 0;
            for (int i = 0; i < DisplayEffectCombo.Items.Count; i++)
            {
                if (DisplayEffectCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, selectedMode, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            DisplayEffectCombo.SelectedIndex = index;
            bool effectOn = AppConfig.Is("display_effect");
            DisplayEffectSwitch.IsChecked = effectOn;
            DisplayEffectCombo.IsEnabled = effectOn;
            DisplayEffectEditButton.IsEnabled = effectOn;
        }
        finally
        {
            _syncingDisplayEffect = false;
        }
    }

    private int SelectedCalibrationMode()
    {
        return CalibrationCombo.SelectedItem is ComboBoxItem item && item.Tag is int mode ? mode : 1;
    }

    private string SelectedDisplayEffectMode()
    {
        return DisplayEffectCombo.SelectedItem is ComboBoxItem item ? item.Tag as string ?? "" : "";
    }

    private async void CalibrationSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingCalibration || _backendProbe == null)
        {
            return;
        }
        bool on = CalibrationSwitch.IsChecked == true;
        int mode = SelectedCalibrationMode();
        CalibrationSwitch.IsEnabled = false;
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_58;
        bool ok;
        if (on && mode == 999)
        {
            string iccName = AppConfig.GetString("calib_custom_icc") ?? "";
            string iccPath = string.IsNullOrWhiteSpace(iccName) ? "" : System.IO.Path.Combine(IccHelper.CustomIccDir, iccName);
            if (!string.IsNullOrWhiteSpace(iccPath) && File.Exists(iccPath))
            {
                ok = await Task.Run(() => IccHelper.ApplyIcc(iccPath));
            }
            else
            {
                ok = false;
            }
        }
        else if (!on && AppConfig.Get("calib_mode") == 999)
        {
            await Task.Run(IccHelper.ClearAllIcc);
            ok = true;
        }
        else
        {
            ok = await _backendProbe.SetColorCalibrationAsync(on ? mode : 0);
        }
        if (ok)
        {
            AppConfig.Set("calib_mode", mode);
            AppConfig.Set("calib_enabled", on ? 1 : 0);
            AppConfig.Flush();
            StatusText.Text = on ? WpfStrings.DisplayCalibrationOn : WpfStrings.DisplayCalibrationOff;
        }
        else
        {
            _syncingCalibration = true;
            CalibrationSwitch.IsChecked = !on;
            _syncingCalibration = false;
            StatusText.Text = mode == 999 ? WpfStrings.DisplayIccMissing : WpfStrings.DisplayCalibrationFailed;
        }
        CalibrationCombo.IsEnabled = CalibrationSwitch.IsChecked == true;
        CalibrationSettingsButton.IsEnabled = CalibrationSwitch.IsChecked == true;
        CalibrationSwitch.IsEnabled = true;
    }

    private async void CalibrationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCalibration || _backendProbe == null || CalibrationSwitch.IsChecked != true)
        {
            return;
        }
        int mode = SelectedCalibrationMode();
        bool ok;
        if (mode == 999)
        {
            string iccName = AppConfig.GetString("calib_custom_icc") ?? "";
            string iccPath = string.IsNullOrWhiteSpace(iccName) ? "" : System.IO.Path.Combine(IccHelper.CustomIccDir, iccName);
            ok = !string.IsNullOrWhiteSpace(iccPath) && File.Exists(iccPath)
                && await Task.Run(() => IccHelper.ApplyIcc(iccPath));
        }
        else
        {
            if (AppConfig.Get("calib_mode") == 999)
            {
                await Task.Run(IccHelper.ClearAllIcc);
            }
            ok = await _backendProbe.SetColorCalibrationAsync(mode);
        }
        if (ok)
        {
            AppConfig.Set("calib_mode", mode);
            AppConfig.Set("calib_enabled", 1);
            AppConfig.Flush();
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_59 + (CalibrationCombo.SelectedItem as ComboBoxItem)?.Content;
        }
        else
        {
            StatusText.Text = mode == 999 ? WpfStrings.DisplayIccMissing : WpfStrings.DisplayCalibrationModeFailed;
            InitScreenColorControls();
        }
    }

    private void CalibrationSettings_Click(object sender, RoutedEventArgs e)
    {
        CalibrationSettingsWindow dialog = new CalibrationSettingsWindow { Owner = this };
        dialog.ShowDialog();
        InitScreenColorControls();
    }

    private async void DisplayEffectSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingDisplayEffect || _backendProbe == null)
        {
            return;
        }
        bool on = DisplayEffectSwitch.IsChecked == true;
        DisplayEffectSwitch.IsEnabled = false;
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_60;
        bool ok = await _backendProbe.ApplyDisplayEffectAsync(on, SelectedDisplayEffectMode());
        if (ok)
        {
            AppConfig.Set("display_effect", on ? 1 : 0);
            AppConfig.Flush();
            StatusText.Text = on ? WpfStrings.DisplayColorEnhancementOn : WpfStrings.DisplayColorEnhancementOff;
        }
        else
        {
            _syncingDisplayEffect = true;
            DisplayEffectSwitch.IsChecked = !on;
            _syncingDisplayEffect = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_61;
        }
        DisplayEffectCombo.IsEnabled = DisplayEffectSwitch.IsChecked == true;
        DisplayEffectEditButton.IsEnabled = DisplayEffectSwitch.IsChecked == true;
        DisplayEffectSwitch.IsEnabled = true;
    }

    private async void DisplayEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDisplayEffect || _backendProbe == null)
        {
            return;
        }
        await ApplySelectedDisplayEffectModeAsync();
    }

    private async Task ApplySelectedDisplayEffectModeAsync()
    {
        if (_backendProbe == null)
        {
            return;
        }
        string mode = SelectedDisplayEffectMode();
        // 用户的档位选择必须先记忆：即使当前后端档位表缺失/下发失败，
        // 下一次刷新也不能把下拉重置回“多彩”。
        if (!string.IsNullOrWhiteSpace(mode))
        {
            AppConfig.Set("display_effect_mode", mode);
            AppConfig.Flush();
        }
        if (DisplayEffectSwitch.IsChecked != true)
        {
            return;
        }
        bool ok = await _backendProbe.ApplyDisplayEffectProfileAsync(mode);
        if (ok)
        {
            await _backendProbe.RequestDisplayEffectStatusAsync();
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_62 + (DisplayEffectCombo.SelectedItem as ComboBoxItem)?.Content;
        }
        else
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_63;
        }
    }

    private void DisplayEffectEdit_Click(object sender, RoutedEventArgs e)
    {
        BackendProbe? probe = _backendProbe;
        if (probe?.Service == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_64;
            return;
        }
        DisplayEffectEditorWindow dialog = new DisplayEffectEditorWindow(SelectedDisplayEffectMode()) { Owner = this };
        dialog.ShowDialog();
        _ = probe.RequestDisplayEffectStatusAsync();
        InitScreenColorControls();
    }

    private static (string Effect, string Name)[] CurrentLogoEffectsForBackend()
    {
        MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        if (hw != null && !hw.UsesBaolongda
            && (hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30
                || hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40))
        {
            return EffectsForCurrentBackend(LightingCatalog.LogoEffectsGen40);
        }
        return EffectsForCurrentBackend(LightingCatalog.CurrentLogoEffects());
    }

    private static (string Effect, string Name)[] EffectsForCurrentBackend((string Effect, string Name)[] effects)
    {
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true)
        {
            return new[]
            {
                ("Single", MechrevoLite.Properties.Strings.EffectSingle),
                ("Rainbow", MechrevoLite.Properties.Strings.EffectRainbow)
            };
        }
        return effects;
    }

    private static (string Effect, string Name)[] KeyboardEffectsForCurrent()
    {
        MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        if (hw?.UsesBaolongda == true)
        {
            return EffectsForCurrentBackend(Array.Empty<(string, string)>());
        }
        if (hw?.WujieSmuApu == true)
        {
            return new[] { ("Single", MechrevoLite.Properties.Strings.EffectSingle) };
        }
        return MechrevoLite.Hardware.KeyboardFirmwareEffects.Visible(hw?.Capabilities.KeyboardType ?? 0);
    }

    private static void FillEffects(ComboBox combo, (string Effect, string Name)[] effects, string topic)
    {
        combo.Items.Clear();
        string selected = MechrevoLite.Hardware.LightingSettingsStore.Load(topic, effects[0].Effect).Effect;
        int selectedIndex = 0;
        bool effectFound = false;
        for (int i = 0; i < effects.Length; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = effects[i].Name, Tag = effects[i].Effect });
            if (string.Equals(effects[i].Effect, selected, StringComparison.Ordinal))
            {
                selectedIndex = i;
                effectFound = true;
            }
        }
        if (!effectFound && L_Mechrevo.WpfP0.Services.WpfBackend.Hw != null && !L_Mechrevo.WpfP0.Services.WpfBackend.Hw.UsesBaolongda
            && L_Mechrevo.WpfP0.Services.WpfBackend.Hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40)
        {
            try
            {
                MechrevoLite.Hardware.LightChannelSettings migrated =
                    MechrevoLite.Hardware.LightingSettingsStore.Load(topic, effects[0].Effect);
                MechrevoLite.Hardware.LightingSettingsStore.Save(topic,
                    migrated with { Effect = effects[0].Effect });
            }
            catch
            {
            }
        }
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = selectedIndex;
        }
    }

    private static bool LoadChannelPower(string topic, string defaultEffect)
    {
        try
        {
            return MechrevoLite.Hardware.LightingSettingsStore.Load(topic, defaultEffect).PowerOn;
        }
        catch
        {
            return false;
        }
    }

    private static (string Effect, string Name)[] CurrentLightbarEffectsForBackend()
    {
        MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        if (hw?.IsIntelNucMachine == true)
        {
            // NUC 灯带只有单色/彩虹两档，其余名称下发无效。
            return new[]
            {
                ("Single", MechrevoLite.Properties.Strings.EffectSingle),
                ("Rainbow", MechrevoLite.Properties.Strings.EffectRainbow)
            };
        }
        return LightingCatalog.LightbarEffects;
    }

    private void FillLightChannelEffects()
    {
        _syncingLighting = true;
        try
        {
            FillEffects(LightStripEffectCombo, EffectsForCurrentBackend(CurrentLightbarEffectsForBackend()), "HidLightbar/Ctrl");
            FillEffects(LogoEffectCombo, CurrentLogoEffectsForBackend(), "HidLightbar_Logo/Ctrl");
            FillEffects(LightHingeEffectCombo, EffectsForCurrentBackend(LightingCatalog.HingeEffects), "HidLightbar_Hinge/Ctrl");
            FillEffects(LightSyncEffectCombo, EffectsForCurrentBackend(LightingCatalog.SyncEffects), "HidLightbar_Sync/Ctrl");
            ApplyLightChannelBackendVisibility();
        }
        finally
        {
            _syncingLighting = false;
        }
    }

    private static bool IsHidKeyboard(MechrevoHw? hw)
    {
        if (hw == null
            || hw.UsesBaolongda
            || hw.WujieSmuApu
            || MechrevoLite.Hardware.KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(hw.Capabilities.KeyboardType))
        {
            return false;
        }
        // 与原版 ShouldUseGcuKeyboardFallback 一致：HID 不可用/亮度未生效时回 GCU 固件面板。
        KeyboardRgb? rgb = L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
        if (rgb != null
            && MechrevoLite.Hardware.KeyboardLightPathPolicy.ShouldUseGcuKeyboardFallback(
                rgb.ControllerAvailability,
                rgb.IsConnected,
                L_Mechrevo.WpfP0.Services.WpfBackend.Service != null && hw.IsConnected,
                hidBrightnessTookEffect: true))
        {
            return false;
        }
        return true;
    }

    private void ApplyLightChannelBackendVisibility()
    {
        bool baolongda = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true;
        Visibility visibility = baolongda ? Visibility.Collapsed : Visibility.Visible;
        LightStripEffectCombo.Visibility = visibility;
        LightStripEditButton.Visibility = visibility;
        LogoEffectCombo.Visibility = visibility;
        LogoLightEditButton.Visibility = visibility;
        LightHingeEffectCombo.Visibility = visibility;
        LightHingeEditButton.Visibility = visibility;
        LightSyncEffectCombo.Visibility = visibility;
        LightSyncEditButton.Visibility = visibility;
    }

    private static KeyboardRgb? EnsureKeyboardRgbConfig()
    {
        if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            return L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
        }
        try
        {
            KeyboardRgb rgb = new();
            rgb.LoadConfig();
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb = rgb;
            return rgb;
        }
        catch
        {
            return null;
        }
    }

    private void FillKeyboardEffects()
    {
        _syncingLighting = true;
        try
        {
            KeyboardEffectCombo.Items.Clear();
            MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
            if (IsHidKeyboard(hw))
            {
                EnsureKeyboardRgbConfig();
                (int Mode, string Name)[] hidEffects = LightingCatalog.VisibleHidEffects(
                    hw?.Capabilities.KeyboardType ?? 0);
                int selectedMode = L_Mechrevo.WpfP0.Services.WpfBackend.rgb?.KbHidMode ?? (hidEffects.Length > 0 ? hidEffects[0].Mode : 1);
                int hidIndex = 0;
                for (int i = 0; i < hidEffects.Length; i++)
                {
                    KeyboardEffectCombo.Items.Add(new ComboBoxItem
                    {
                        Content = hidEffects[i].Name,
                        Tag = "hid:" + hidEffects[i].Mode
                    });
                    if (hidEffects[i].Mode == selectedMode)
                    {
                        hidIndex = i;
                    }
                }
                if (KeyboardEffectCombo.Items.Count > 0)
                {
                    KeyboardEffectCombo.SelectedIndex = hidIndex;
                }
                return;
            }
            (string Effect, string Name)[] effects = KeyboardEffectsForCurrent();
            string selected = MechrevoLite.Hardware.LightingSettingsStore.Load("Keyboard/Ctrl", effects.Length > 0 ? effects[0].Effect : "Single").Effect;
            int selectedIndex = 0;
            for (int i = 0; i < effects.Length; i++)
            {
                KeyboardEffectCombo.Items.Add(new ComboBoxItem { Content = effects[i].Name, Tag = effects[i].Effect });
                if (string.Equals(effects[i].Effect, selected, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                }
            }
            if (KeyboardEffectCombo.Items.Count > 0)
            {
                KeyboardEffectCombo.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _syncingLighting = false;
        }
    }

    private void SyncLightSwitchesFromHardware()
    {
        MechrevoHw? hw = _backendProbe?.Hw;
        if (hw == null || !hw.IsConnected)
        {
            return;
        }
        bool suspended = L_Mechrevo.WpfP0.Services.WpfBackend.IsLightingTemporarilySuspended;
        _syncingLighting = true;
        try
        {
            // 这里必须区分“硬件回读值”和“用户刚点的 pending 值”：
            // 旧逻辑先把 pending 覆盖进 effective，再拿 effective 和 pending 比较 ⇒ 永远相等、
            // pending 在第一条状态包就被清掉，下一拍又按仍然为 false 的硬件回读把开关压回去。
            bool hardwareKeyboardPower = IsHidKeyboard(hw)
                ? (L_Mechrevo.WpfP0.Services.WpfBackend.rgb?.KbPowerOn ?? hw.KeyboardPower)
                : hw.KeyboardPower;
            if (_pendingKeyboardPower is bool pendingPower)
            {
                // 只有硬件回读真正追上用户请求才清 pending。
                if (hardwareKeyboardPower != pendingPower)
                {
                    // 保持按钮为用户刚点的状态，等硬件回读追上；不再被旧硬件值弹回。
                    KeyboardLightSwitch.IsChecked = pendingPower;
                }
                else
                {
                    _pendingKeyboardPower = null;
                    KeyboardLightSwitch.IsChecked = MechrevoLite.Hardware.LightingState.IsLightSwitchOn(
                        MechrevoLite.KeyboardLightingDecision.ShouldWrite(hw.IsConnected, hardwareKeyboardPower), suspended);
                }
            }
            else
            {
                bool keyboardOn = hw.UsesBaolongda
                    ? hardwareKeyboardPower
                    : MechrevoLite.KeyboardLightingDecision.ShouldWrite(hw.IsConnected, hardwareKeyboardPower);
                KeyboardLightSwitch.IsChecked = MechrevoLite.Hardware.LightingState.IsLightSwitchOn(keyboardOn, suspended);
            }
            SetSwitchFromQuick(hw, "lightbar", LightStripSwitch, suspended, 0);
            SetSwitchFromQuick(hw, "logolight", LogoLightSwitch, suspended, 1);
            SetSwitchFromQuick(hw, "hingelight", LightHingeSwitch, suspended, 2);
            SetSwitchFromQuick(hw, "synclight", LightSyncSwitch, suspended, 3);
        }
        finally
        {
            _syncingLighting = false;
        }
    }

    private void SetSwitchFromQuick(MechrevoHw hw, string key, CheckBox target, bool suspended, int index)
    {
        if (hw.QuickSwitches == null || !hw.QuickSwitches.TryGetValue(key, out bool power))
        {
            return;
        }
        bool shown = MechrevoLite.Hardware.LightingState.IsLightSwitchOn(power, suspended);
        if (_pendingChannelPower[index] is bool pending)
        {
            // 用户刚点的状态优先；回读未追上则保持按钮，不用超时强制回填。
            if (shown != pending)
            {
                return;
            }
            _pendingChannelPower[index] = null;
        }
        target.IsChecked = shown;
    }

    private void InitLightingControls()
    {
        LightOffOnBatteryCheck.IsChecked = AppConfig.Is("lighting_off_on_battery");
        FillKeyboardEffects();
        FillLightChannelEffects();
        try
        {
            KeyboardLightSwitch.IsChecked = _backendProbe?.Hw?.KeyboardPower == true;
        }
        catch
        {
            KeyboardLightSwitch.IsChecked = false;
        }
        LightStripSwitch.IsChecked = LoadChannelPower("HidLightbar/Ctrl", LightingCatalog.LightbarEffects[0].Effect);
        LogoLightSwitch.IsChecked = LoadChannelPower("HidLightbar_Logo/Ctrl", CurrentLogoEffectsForBackend()[0].Effect);
        LightHingeSwitch.IsChecked = LoadChannelPower("HidLightbar_Hinge/Ctrl", LightingCatalog.HingeEffects[0].Effect);
        LightSyncSwitch.IsChecked = LoadChannelPower("HidLightbar_Sync/Ctrl", LightingCatalog.SyncEffects[0].Effect);
        FillLightIdleOptions();
    }

    private void FillLightIdleOptions()
    {
        _syncingLighting = true;
        try
        {
            LightIdleCombo.Items.Clear();
            bool baolongda = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true;
            bool xingyao = MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(L_Mechrevo.WpfP0.Services.WpfBackend.Hw);
            int current;
            if (baolongda)
            {
                current = MechrevoHw.GetKeyboardAutoOffSeconds();
                foreach ((string Label, int Seconds) option in LightingCatalog.KeyboardAutoOffOptions)
                {
                    LightIdleCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Seconds.ToString() });
                }
            }
            else if (xingyao)
            {
                // 星耀 WMAA 只有两档：[3,12,1,0] 常亮 / [3,12,0,0] 10 秒。
                current = AppConfig.Get("lighting_idle_seconds", 0);
                foreach ((string Label, int Seconds) option in LightingCatalog.KeyboardAutoOffOptions)
                {
                    if (option.Seconds == 0 || option.Seconds == 10)
                    {
                        LightIdleCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Seconds.ToString() });
                    }
                }
            }
            else
            {
                current = AppConfig.Get("lighting_idle_seconds",
                    Math.Max(0, L_Mechrevo.WpfP0.Services.WpfBackend.rgb?.CloseTimerMinutes ?? 0) * 60);
                foreach ((string Label, int Mins, int IdleSeconds) option in LightingCatalog.LightingCloseTimerOptions)
                {
                    LightIdleCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.IdleSeconds.ToString() });
                }
            }
            LightIdleCombo.SelectedIndex = 0;
            foreach (object item in LightIdleCombo.Items)
            {
                if (item is ComboBoxItem comboItem && int.TryParse(comboItem.Tag as string, out int value) && value == current)
                {
                    LightIdleCombo.SelectedItem = comboItem;
                    break;
                }
            }
        }
        finally
        {
            _syncingLighting = false;
        }
    }

    private void LightIdleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        if (LightIdleCombo.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Tag as string, out int seconds))
        {
            return;
        }
        if (MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(L_Mechrevo.WpfP0.Services.WpfBackend.Hw))
        {
            bool alwaysOn = seconds <= 0;
            if (!MechrevoLite.Hardware.XingyaoQuickSwitch.TrySetKeyboardSleepTimeout(alwaysOn, out string xyDetail))
            {
                StatusText.Text = "星耀键盘睡眠设置失败：" + xyDetail;
                return;
            }
            AppConfig.Set("lighting_idle_seconds", seconds);
            AppConfig.Flush();
            StatusText.Text = alwaysOn
                ? "键盘灯：常亮（星耀 WMAA）"
                : "键盘灯：10 秒后关闭（星耀 WMAA）";
            Logger.WriteError("[xingyao-quick] " + xyDetail);
            return;
        }
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true)
        {
            MechrevoHw.SetKeyboardAutoOffSeconds(seconds);
            L_Mechrevo.WpfP0.Services.WpfBackend.Hw.ApplyKeyboardAutoOffSetting();
            StatusText.Text = seconds <= 0 ? WpfStrings.LightBdKeyboardAutoOffOff : WpfStrings.LightBdKeyboardAutoOffPrefix + seconds + WpfStrings.UnitSecondsSuffix;
            return;
        }
        AppConfig.Set("lighting_idle_seconds", seconds);
        AppConfig.Flush();
        if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.CloseTimerMinutes = seconds / 60;
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.SaveConfig();
        }
        _ = ReconcileLightingPowerAsync();
        StatusText.Text = seconds <= 0 ? WpfStrings.LightAutoOffOff : WpfStrings.LightAutoOffPrefix + (seconds >= 60 ? (seconds / 60) + WpfStrings.UnitMinutesSuffix : seconds + WpfStrings.UnitSecondsSuffix);
    }

    private void LightOffOnBattery_Click(object sender, RoutedEventArgs e)
    {
        bool on = LightOffOnBatteryCheck.IsChecked == true;
        AppConfig.Set("lighting_off_on_battery", on ? 1 : 0);
        AppConfig.Flush();
        StatusText.Text = on ? WpfStrings.LightOffOnBatteryOn : WpfStrings.LightOffOnBatteryOff;
    }

    private void KeyboardLightEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_65;
            return;
        }
        L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
        MechrevoHw? hw = _backendProbe.Hw;
        bool firmwareKeyboard = hw != null && !IsHidKeyboard(hw);
        if (firmwareKeyboard)
        {
            OpenLightEditor("Keyboard/Ctrl", WpfStrings.HidKeyboardWindow_Text_9, EffectsForCurrentBackend(KeyboardEffectsForCurrent()));
            return;
        }
        KeyboardRgb? rgb = EnsureKeyboardRgb();
        if (rgb == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_66;
            return;
        }
        new HidKeyboardWindow(rgb) { Owner = this }.ShowDialog();
        FillKeyboardEffects();
    }

    private void LogoLightEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_67;
            return;
        }
        L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
        OpenLightEditor("HidLightbar_Logo/Ctrl", WpfStrings.LightTitleLogo, CurrentLogoEffectsForBackend());
    }

    private void CheckLightingIdle()
    {
        _ = ReconcileLightingPowerAsync();
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true)
        {
            return;
        }
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return;
        }
        // 不要求 GCU/后端已连接：同方机器 GCU 断开时仍走本地空闲计时，
        // 关灯优先使用 HID/legacy 路径；完全没有后端时后续写入会失败，但计时器仍保留。
        int timeoutSeconds = AppConfig.Get("lighting_idle_seconds", 0);
        // 亚分钟选项由 LM 软件计时；1s tick 保证“20秒”实际落在 20~21s，而不是 2s tick 拖到 22s+。
        TimeSpan desiredTick = timeoutSeconds > 0 && timeoutSeconds < 60
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(2);
        if (_lightingIdleTimer.Interval != desiredTick)
        {
            _lightingIdleTimer.Interval = desiredTick;
        }
        if (timeoutSeconds <= 0)
        {
            if (_lightingIdleSuspended)
            {
                _lightingIdleSuspended = false;
                _ = RestoreAllLightsAsync();
            }
            return;
        }
        double idleSeconds = NativeMethods.GetIdleTime().TotalSeconds;
        if (!_lightingIdleSuspended && idleSeconds >= timeoutSeconds)
        {
            _lightingIdleSuspended = true;
            L_Mechrevo.WpfP0.Services.WpfBackend.LightingIdleSuspended = true;
            _lightingIdleSwitchSnapshot = new[]
            {
                KeyboardLightSwitch.IsChecked == true,
                LightStripSwitch.IsChecked == true,
                LogoLightSwitch.IsChecked == true,
                LightHingeSwitch.IsChecked == true,
                LightSyncSwitch.IsChecked == true
            };
            _ = TurnOffAllLightsAsync();
            return;
        }
        if (_lightingIdleSuspended && idleSeconds < Math.Max(5, timeoutSeconds / 2))
        {
            _lightingIdleSuspended = false;
            L_Mechrevo.WpfP0.Services.WpfBackend.LightingIdleSuspended = false;
            _ = RestoreAllLightsAsync();
        }
    }

    private void HookHardwareState(MechrevoHw? hw)
    {
        if (ReferenceEquals(_stateHookedHw, hw))
        {
            return;
        }
        if (_stateHookedHw != null)
        {
            try
            {
                _stateHookedHw.StateChanged -= OnHardwareLightingStateChanged;
                _stateHookedHw.ModeChanged -= OnHardwareModeChanged;
            }
            catch
            {
            }
        }
        _stateHookedHw = hw;
        if (hw != null)
        {
            hw.StateChanged += OnHardwareLightingStateChanged;
            hw.ModeChanged += OnHardwareModeChanged;
        }
    }

    private void OnHardwareLightingStateChanged(string topic)
    {
        if (string.IsNullOrEmpty(topic)
            || (!topic.Contains("Keyboard", StringComparison.OrdinalIgnoreCase)
                && !topic.Contains("Lightbar", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        // ⛔ 不能在每次 GCU 灯光状态回包时重放保存的灯效：
        //    应用灯效 → GCU 回包 → 5s 后再次重放 ⇒ 无限循环（NUC 上表现为每 6s 一次的
        //    UmiLightbar 电源写入 + HID 状态栏反复出现，并会覆盖用户刚调的彩虹亮度/颜色）。
        //    这里只把开关状态同步到界面；真正的恢复只在连接/启动/定时重试时做。
        Dispatcher.BeginInvoke(new Action(SyncLightSwitchesFromHardware));
    }

    /// <summary>
    /// GCU 自己处理性能键后会上报 OperatingMode；这里只跟随上报值出 OSD，
    /// 不主动下发任何模式。比轮询快，也能正确区分自定义档。
    /// </summary>
    private void OnHardwareModeChanged(int operatingMode)
    {
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Task.Delay(120);
                BackendProbe? probe = _backendProbe;
                MechrevoHw? hw = probe?.Hw;
                if (probe == null || hw == null
                    || !hw.IsGcuConnected
                    || hw.UsesBaolongda
                    || hw.IsIntelNucMachine
                    || hw.IsUmiMachine)
                {
                    return;
                }
                if (_modeSwitchInFlight || _pendingMode >= 0)
                {
                    return;
                }
                if (Environment.TickCount64 - _lastSwitchModeOsdTick < 1800
                    || Environment.TickCount64 - _lastPerfKeyOsdTick < 600)
                {
                    return;
                }
                int mode = ResolveDisplayMode(probe);
                if (mode < 0)
                {
                    return;
                }
                _lastPerfKeyOsdTick = Environment.TickCount64;
                _lastObservedGcuMode = mode;
                OsdService.Show(string.Empty, WpfStrings.OsdPerformance, ModeOsdName(mode));
                Logger.WriteError("[perfkey] GCU ModeChanged=" + operatingMode + " ⇒ OSD=" + mode);
                RefreshFromBackend();
            }
            catch
            {
            }
        }));
    }

    /// <summary>
    /// 近期修复：首次装 GCU / 后端预热期能力位可能晚到，启动后分阶梯重新评估一次。
    /// 只刷新只读能力与 UI，不重复连接、不写硬件。
    /// </summary>
    private void ScheduleCapabilityWarmup()
    {
        int generation = ++_capabilityWarmupGeneration;
        _ = Task.Run(async () =>
        {
            foreach (int delaySeconds in new[] { 5, 15, 30, 60 })
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != _capabilityWarmupGeneration || _backendProbe == null)
                    {
                        return;
                    }
                    try
                    {
                        // 0.293.5.1 GcuWarmupReload：预热期重读一次 ItemSupport/能力快照，
                        // 不能只重画 UI，否则首次装 GCU 漏报的能力位不会回来。
                        _backendProbe?.Hw?.ReloadCapabilities();
                        ApplyCapabilities();
                        BackendReady?.Invoke();
                    }
                    catch
                    {
                    }
                }));
            }
        });
    }

    private void ScheduleLightingReassert()
    {
        int generation = ++_lightingReassertGeneration;
        _ = Task.Run(async () =>
        {
            foreach (int delaySeconds in new[] { 20, 45 })
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (generation == _lightingReassertGeneration)
                    {
                        await RestoreLightingOnStartupAsync();
                    }
                }));
            }
        });
    }

    private async Task RestoreLightingOnStartupAsync()
    {
        long now = Environment.TickCount64;
        if (now - _lastLightingRestoreTick < 5000)
        {
            return;
        }
        _lastLightingRestoreTick = now;
        if (_backendProbe == null
            || !MechrevoLite.Hardware.LightingSettingsStore.HasUserLightingIntent()
            || L_Mechrevo.WpfP0.Services.WpfBackend.IsLightingTemporarilySuspended
            || _lightingPowerSuspended
            || _lightingIdleSuspended)
        {
            return;
        }
        // r78：同方单键 RGB 开机闪 —— HID 帧流不要抢在固件/GCU 稳定前接管。
        if (IsHidKeyboard(_backendProbe.Hw))
        {
            const long hidStartupDelayMs = 8000;
            long elapsed = Environment.TickCount64 - _bootTick;
            if (elapsed < hidStartupDelayMs)
            {
                if (!_hidStartupDeferred)
                {
                    _hidStartupDeferred = true;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay((int)(hidStartupDelayMs - elapsed));
                        Dispatcher.BeginInvoke(new Action(() => _ = RestoreLightingOnStartupAsync()));
                    });
                }
                return;
            }
        }
        bool onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
            != System.Windows.Forms.PowerLineStatus.Online;
        if (AppConfig.Is("lighting_off_on_battery") && onBattery)
        {
            return;
        }
        try
        {
            LightChannelSettings keyboard = MechrevoLite.Hardware.LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
            if (keyboard.PowerExplicit)
            {
                _syncingLighting = true;
                KeyboardLightSwitch.IsChecked = keyboard.PowerOn;
                _syncingLighting = false;
                if (keyboard.PowerOn)
                {
                    await ApplyKeyboardLightAsync(showStatus: false);
                }
                else if (IsHidKeyboard(_backendProbe.Hw))
                {
                    KeyboardRgb? rgb = EnsureKeyboardRgb();
                    if (rgb != null && rgb.IsConnected)
                    {
                        rgb.StopCurrentEffect();
                        rgb.KbPowerSyncFromHardware(false);
                    }
                }
                else
                {
                    await _backendProbe.SetKeyboardPowerAsync(false);
                }
            }
            await RestoreSavedChannelAsync("HidLightbar/Ctrl", LightStripSwitch);
            if (!IsSamePhysicalLightbar(_backendProbe.Hw))
            {
                await RestoreSavedChannelAsync("HidLightbar_Logo/Ctrl", LogoLightSwitch);
            }
            await RestoreSavedChannelAsync("HidLightbar_Hinge/Ctrl", LightHingeSwitch);
            await RestoreSavedChannelAsync("HidLightbar_Sync/Ctrl", LightSyncSwitch);
            TryResumeBaolongdaRainbow("启动/恢复");
        }
        catch
        {
        }
    }

    /// <summary>0.293.5.1：宝龙达软件彩虹只在硬件亮着且保存效果=彩虹时恢复刷色，不重新点亮。</summary>
    private void TryResumeBaolongdaRainbow(string reason)
    {
        MechrevoHw? hw = _backendProbe?.Hw;
        if (hw == null || !hw.UsesBaolongda || hw.IsKeyboardRainbowRunning) return;
        if (!MechrevoLite.Hardware.LightingSettingsStore.TryLoad("Keyboard/Ctrl", out LightChannelSettings settings)) return;
        if (!string.Equals(settings.Effect, "Rainbow", StringComparison.OrdinalIgnoreCase)) return;
        KeyboardRgb? rgb = EnsureKeyboardRgb();
        if (rgb != null && rgb.KbPowerExplicitOff) return;
        if (!hw.TryReadKeyboardLightingViaBackend(out bool hardwareOn, out int _, out int brightness, out string _) || !hardwareOn) return;
        if (brightness <= 0 && hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30)
        {
            MechrevoLite.Hardware.KeyboardEcBrightness.TryReadCurrentLevel(out brightness);
        }
        if (brightness <= 0) return;
        int max = Math.Max(1, MechrevoHw.EffectiveKeyboardBrightnessMax());
        hw.StartKeyboardRainbow((byte)Math.Clamp(brightness, 1, max), CancellationToken.None);
        Logger.WriteError("[light] 宝龙达启动：" + reason + " 软件彩虹恢复（硬件亮着 · 亮度=" + brightness + "）");
    }

    private async Task RestoreSavedChannelAsync(string topic, CheckBox powerSwitch)
    {
        if (_backendProbe == null)
        {
            return;
        }
        if (topic == "HidLightbar_Logo/Ctrl" && IsSamePhysicalLightbar(_backendProbe.Hw))
        {
            return;
        }
        string defaultEffect = topic switch
        {
            "HidLightbar/Ctrl" => CurrentLightbarEffectsForBackend()[0].Effect,
            "HidLightbar_Logo/Ctrl" => CurrentLogoEffectsForBackend()[0].Effect,
            "HidLightbar_Hinge/Ctrl" => LightingCatalog.HingeEffects[0].Effect,
            _ => LightingCatalog.SyncEffects[0].Effect
        };
        LightChannelSettings saved = MechrevoLite.Hardware.LightingSettingsStore.Load(topic, defaultEffect);
        if (!saved.PowerExplicit)
        {
            return;
        }
        _syncingLighting = true;
        powerSwitch.IsChecked = saved.PowerOn;
        _syncingLighting = false;
        if (!saved.PowerOn)
        {
            await SetLightPowerServiceAsync(topic, false);
            return;
        }
        await SetLightPowerServiceAsync(topic, true);
        switch (topic)
        {
            case "HidLightbar/Ctrl":
                await SetLightbarEffectServiceAsync(saved.Effect);
                break;
            case "HidLightbar_Logo/Ctrl":
                await SetLogoEffectServiceAsync(saved.Effect);
                break;
            case "HidLightbar_Hinge/Ctrl":
                await SetHingeEffectServiceAsync(saved.Effect);
                break;
            default:
                await SetSyncEffectServiceAsync(saved.Effect);
                break;
        }
    }

    private static bool IsSamePhysicalLightbar(MechrevoHw? hw)
    {
        try
        {
            if (hw == null || !MechrevoLite.Hardware.LightCompatibilitySwitches.LightbarDedupEnabled)
            {
                return false;
            }
            if (hw.SupportsLogoLight || hw.LightbarMbLogoSupport == true)
            {
                return false;
            }
            string? lightbar = hw.LightbarStatusFingerprint("HidLightbar/Status");
            string? logo = hw.LightbarStatusFingerprint("HidLightbar_Logo/Status");
            return lightbar != null && logo != null
                && string.Equals(lightbar, logo, StringComparison.Ordinal)
                && !string.Equals(lightbar, "type=-|powerStatus=-|power=-|light=-|brightNess=-", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task RestoreAllLightsAsync()
    {
        if (_lightingIdleSwitchSnapshot is { Length: 5 } snapshot)
        {
            _syncingLighting = true;
            KeyboardLightSwitch.IsChecked = snapshot[0];
            LightStripSwitch.IsChecked = snapshot[1];
            LogoLightSwitch.IsChecked = snapshot[2];
            LightHingeSwitch.IsChecked = snapshot[3];
            LightSyncSwitch.IsChecked = snapshot[4];
            _syncingLighting = false;
            _lightingIdleSwitchSnapshot = null;
        }
        await ApplyKeyboardLightAsync(showStatus: false);
        if (!IsSamePhysicalLightbar(_backendProbe?.Hw))
        {
            await ApplyLogoLightAsync(restoreEffect: true);
        }
        await ApplyLightStripLightAsync(restoreEffect: true);
        await ApplyHingeLightAsync(restoreEffect: true);
        await ApplySyncLightAsync(restoreEffect: true);
    }

    private async Task ReconcileLightingPowerAsync()
    {
        if (_lightingPowerBusy || _backendProbe == null)
        {
            return;
        }
        _lightingPowerBusy = true;
        try
        {
            bool onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
                != System.Windows.Forms.PowerLineStatus.Online;
            bool shouldSuspend = AppConfig.Is("lighting_off_on_battery") && onBattery;
            if (shouldSuspend == _lightingPowerSuspended)
            {
                return;
            }
            _lightingPowerSuspended = shouldSuspend;
            if (shouldSuspend)
            {
                await TurnOffAllLightsAsync();
            }
            else
            {
                await ApplyKeyboardLightAsync(showStatus: false);
                await ApplyLogoLightAsync();
                await ApplyLightStripLightAsync();
                await ApplyHingeLightAsync();
                await ApplySyncLightAsync();
            }
        }
        catch
        {
        }
        finally
        {
            _lightingPowerBusy = false;
        }
    }

    private async Task TurnOffAllLightsAsync()
    {
        if (_backendProbe == null)
        {
            return;
        }
        if (IsHidKeyboard(_backendProbe.Hw) && L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.StopCurrentEffect();
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb.KbPowerSyncFromHardware(false);
        }
        else
        {
            await _backendProbe.SetKeyboardPowerAsync(false);
        }
        await SetLightPowerServiceAsync("HidLightbar/Ctrl", false);
        await SetLightPowerServiceAsync("HidLightbar_Logo/Ctrl", false);
        await SetLightPowerServiceAsync("HidLightbar_Hinge/Ctrl", false);
        await SetLightPowerServiceAsync("HidLightbar_Sync/Ctrl", false);
    }

    private async Task<bool> ApplyLightStripLightAsync(bool restoreEffect = false)
    {
        if (_backendProbe == null)
        {
            return false;
        }
        bool on = LightStripSwitch.IsChecked == true;
        string effect = (LightStripEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        bool ok;
        if (on)
        {
            ok = await SetLightPowerServiceAsync("HidLightbar/Ctrl", true);
            if (ok && restoreEffect)
            {
                ok = await SetLightbarEffectServiceAsync(effect);
            }
        }
        else
        {
            ok = await SetLightPowerServiceAsync("HidLightbar/Ctrl", false);
        }
        return ok;
    }

    private async Task<bool> ApplyHingeLightAsync(bool restoreEffect = false)
    {
        if (_backendProbe == null)
        {
            return false;
        }
        bool on = LightHingeSwitch.IsChecked == true;
        string effect = (LightHingeEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        bool ok;
        if (on)
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Hinge/Ctrl", true);
            if (ok && restoreEffect)
            {
                ok = await SetHingeEffectServiceAsync(effect);
            }
        }
        else
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Hinge/Ctrl", false);
        }
        return ok;
    }

    private async Task<bool> ApplySyncLightAsync(bool restoreEffect = false)
    {
        if (_backendProbe == null)
        {
            return false;
        }
        bool on = LightSyncSwitch.IsChecked == true;
        string effect = (LightSyncEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        bool ok;
        if (on)
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Sync/Ctrl", true);
            if (ok && restoreEffect)
            {
                ok = await SetSyncEffectServiceAsync(effect);
            }
        }
        else
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Sync/Ctrl", false);
        }
        return ok;
    }

    private async void LightStripSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingLighting || _backendProbe == null)
        {
            return;
        }
        bool requested = LightStripSwitch.IsChecked == true;
        _pendingChannelPower[0] = requested;
        _pendingChannelTick[0] = Environment.TickCount64;
        LightStripSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplyLightStripLightAsync();
        }
        finally
        {
            LightStripSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _syncingLighting = true;
            LightStripSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_68;
            return;
        }
        if (_backendProbe.Hw?.IsConnected == true)
        {
            MechrevoLite.Hardware.LightingSettingsStore.SavePower("HidLightbar/Ctrl", requested, "用户点击「灯光」开关");
            OsdService.Show(LightChannelGlyph("HidLightbar/Ctrl"), LightChannelTitle("HidLightbar/Ctrl"), requested ? WpfStrings.StateOn : WpfStrings.StateOff);
        }
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_69;
    }

    private async void LightStripEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        if (LightStripEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        if (_backendProbe == null || LightStripSwitch.IsChecked != true)
        {
            SaveChannelEffect("HidLightbar/Ctrl", effect);
            return;
        }
        bool ok = await SetLightbarEffectServiceAsync(effect);
        if (ok)
        {
            SaveChannelEffect("HidLightbar/Ctrl", effect);
        }
        StatusText.Text = ok ? WpfStrings.LightLightbarSentPrefix + effect : WpfStrings.LightLightbarFailed;
    }

    private void LightStripEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_70;
            return;
        }
        L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
        OpenLightEditor("HidLightbar/Ctrl", WpfStrings.LightTitleLightbar, EffectsForCurrentBackend(CurrentLightbarEffectsForBackend()));
    }

    private KeyboardRgb? EnsureKeyboardRgb()
    {
        if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
        {
            if (!L_Mechrevo.WpfP0.Services.WpfBackend.rgb.IsConnected)
            {
                try
                {
                    L_Mechrevo.WpfP0.Services.WpfBackend.rgb.Connect();
                }
                catch
                {
                }
            }
            return L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
        }
        try
        {
            KeyboardRgb rgb = new();
            rgb.LoadConfig();
            rgb.Connect();
            L_Mechrevo.WpfP0.Services.WpfBackend.rgb = rgb;
            return rgb;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_71 + ex.Message;
            return null;
        }
    }

    private async void LightHingeSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingLighting || _backendProbe == null)
        {
            return;
        }
        bool requested = LightHingeSwitch.IsChecked == true;
        _pendingChannelPower[2] = requested;
        _pendingChannelTick[2] = Environment.TickCount64;
        LightHingeSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplyHingeLightAsync();
        }
        finally
        {
            LightHingeSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _syncingLighting = true;
            LightHingeSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_72;
            return;
        }
        if (_backendProbe.Hw?.IsConnected == true)
        {
            MechrevoLite.Hardware.LightingSettingsStore.SavePower("HidLightbar_Hinge/Ctrl", requested, "用户点击「灯光」开关");
            OsdService.Show(LightChannelGlyph("HidLightbar_Hinge/Ctrl"), LightChannelTitle("HidLightbar_Hinge/Ctrl"), requested ? WpfStrings.StateOn : WpfStrings.StateOff);
        }
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_73;
    }

    private async void LightHingeEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        if (LightHingeEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        if (_backendProbe == null || LightHingeSwitch.IsChecked != true)
        {
            SaveChannelEffect("HidLightbar_Hinge/Ctrl", effect);
            return;
        }
        bool ok = await SetHingeEffectServiceAsync(effect);
        if (ok)
        {
            SaveChannelEffect("HidLightbar_Hinge/Ctrl", effect);
        }
        StatusText.Text = ok ? WpfStrings.LightHingeSent : WpfStrings.LightHingeFailed;
    }

    private void LightHingeEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_74;
            return;
        }
        L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
        OpenLightEditor("HidLightbar_Hinge/Ctrl", WpfStrings.LightTitleHinge, EffectsForCurrentBackend(LightingCatalog.HingeEffects));
    }

    private async void LightSyncSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingLighting || _backendProbe == null)
        {
            return;
        }
        bool requested = LightSyncSwitch.IsChecked == true;
        _pendingChannelPower[3] = requested;
        _pendingChannelTick[3] = Environment.TickCount64;
        LightSyncSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplySyncLightAsync();
        }
        finally
        {
            LightSyncSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _syncingLighting = true;
            LightSyncSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_75;
            return;
        }
        if (_backendProbe.Hw?.IsConnected == true)
        {
            MechrevoLite.Hardware.LightingSettingsStore.SavePower("HidLightbar_Sync/Ctrl", requested, "用户点击「灯光」开关");
            OsdService.Show(LightChannelGlyph("HidLightbar_Sync/Ctrl"), LightChannelTitle("HidLightbar_Sync/Ctrl"), requested ? WpfStrings.StateOn : WpfStrings.StateOff);
        }
        StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_76;
    }

    private async void LightSyncEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        if (LightSyncEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        if (_backendProbe == null || LightSyncSwitch.IsChecked != true)
        {
            SaveChannelEffect("HidLightbar_Sync/Ctrl", effect);
            return;
        }
        bool ok = await SetSyncEffectServiceAsync(effect);
        if (ok)
        {
            SaveChannelEffect("HidLightbar_Sync/Ctrl", effect);
        }
        StatusText.Text = ok ? WpfStrings.LightSyncSent : WpfStrings.LightSyncFailed;
    }

    private void LightSyncEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_77;
            return;
        }
        L_Mechrevo.WpfP0.Services.WpfBackend.Attach(_backendProbe.Hw, _backendProbe.Service);
        OpenLightEditor("HidLightbar_Sync/Ctrl", WpfStrings.LightTitleSync, EffectsForCurrentBackend(LightingCatalog.SyncEffects));
    }

    private async void KeyboardLightSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        bool requested = KeyboardLightSwitch.IsChecked == true;
        _pendingKeyboardPower = requested;
        _pendingKeyboardPowerTick = Environment.TickCount64;
        // 与 0.293.5.1 同口径：不管最终走 HID 还是 GCU 固件键盘，先落下用户意图位。
        // 固件键盘的电源状态包可能滞后/无回读，同步时 KeyboardLightingDecision 需要这份意图，
        // 否则“命令已下发但回读仍为 false”会把开关压回去。
        KeyboardRgb? intentRgb = L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
        if (intentRgb != null)
        {
            try { intentRgb.KbPowerOn = requested; } catch { }
        }
        // 必须在 await 之前锁住 UI：GCU 状态包常在命令回写过程中到达，
        // 旧代码等下发完才加锁，锁之前已经被 SyncLightSwitchesFromHardware 压回去了。
        _keyboardLightUiLockUntil = Environment.TickCount64 + 5000;
        KeyboardLightSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplyKeyboardLightAsync(powerOnly: true);
        }
        finally
        {
            KeyboardLightSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _pendingKeyboardPower = null;
            // 与 0.293.5.1 一致：下发失败时用户意图也回滚，否则下一拍同步又会被旧意图点亮。
            KeyboardRgb? rollbackRgb = L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
            if (rollbackRgb != null)
            {
                try { rollbackRgb.KbPowerOn = !requested; } catch { }
            }
            _syncingLighting = true;
            KeyboardLightSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_78;
            return;
        }
        _keyboardLightUiLockUntil = Environment.TickCount64 + 2500;
        if (_backendProbe?.Hw?.IsConnected == true)
        {
            MechrevoLite.Hardware.LightingSettingsStore.SavePower("Keyboard/Ctrl", requested, "用户点击键盘灯光开关");
        }
        OsdService.Show(LightChannelGlyph("Keyboard/Ctrl"), LightChannelTitle("Keyboard/Ctrl"),
            requested ? WpfStrings.StateOn : WpfStrings.StateOff);
    }

    private static void SaveChannelEffect(string topic, string effect)
    {
        try
        {
            LightChannelSettings current = MechrevoLite.Hardware.LightingSettingsStore.Load(topic, effect);
            MechrevoLite.Hardware.LightingSettingsStore.Save(topic, current with { Effect = effect });
        }
        catch
        {
        }
    }

    private async void KeyboardEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting || KeyboardEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        if (effect.StartsWith("hid:", StringComparison.Ordinal))
        {
            int hidMode = int.TryParse(effect.AsSpan(4), out int parsedHidMode) ? parsedHidMode : 1;
            if (KeyboardLightSwitch.IsChecked != true)
            {
                if (L_Mechrevo.WpfP0.Services.WpfBackend.rgb != null)
                {
                    L_Mechrevo.WpfP0.Services.WpfBackend.rgb.KbHidMode = hidMode;
                    L_Mechrevo.WpfP0.Services.WpfBackend.rgb.QueueSaveConfig();
                }
                return;
            }
            await ApplyKeyboardLightAsync();
            return;
        }
        if (KeyboardLightSwitch.IsChecked != true)
        {
            // 与 0.293.5.1 一致：灯关着时只记住用户选的效果，不自动把灯点亮；
            // 等用户下次打开开关再下发该效果。
            SaveChannelEffect("Keyboard/Ctrl", effect);
            return;
        }
        await ApplyKeyboardLightAsync();
    }

    private async Task<bool> ApplyHidKeyboardAsync(int mode, bool on, bool allowRetry = true, bool showStatus = true)
    {
        await _hidApplyLock.WaitAsync();
        try
        {
            int generation = ++_hidApplyGeneration;
            bool ok = await ApplyHidKeyboardCoreAsync(mode, on, showStatus);
            if (!ok && on && allowRetry)
            {
                ScheduleHidRetry(mode, generation);
            }
            return ok;
        }
        finally
        {
            _hidApplyLock.Release();
        }
    }

    private void ScheduleHidRetry(int mode, int generation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                KeyboardRgb? rgb = L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
                if (rgb == null || rgb.IsConnected
                    || rgb.ControllerAvailability == MechrevoLite.Hardware.FeatureAvailability.Unsupported)
                {
                    return;
                }
                await Task.Delay(1500);
                if (generation != _hidApplyGeneration || rgb.IsConnected
                    || rgb.ControllerAvailability == MechrevoLite.Hardware.FeatureAvailability.Unsupported)
                {
                    return;
                }
                if (rgb.ControllerAvailability == MechrevoLite.Hardware.FeatureAvailability.Unknown)
                {
                    await rgb.EnsureHidReadyAsync();
                }
                if (!rgb.IsConnected && !rgb.Connect())
                {
                    return;
                }
                if (generation != _hidApplyGeneration)
                {
                    return;
                }
                rgb.ReInitCustomMode();
                rgb.StartMode(mode);
                L_Mechrevo.WpfP0.Services.WpfBackend.MarkKeyboardCustomStatusBaseline();
                rgb.SaveConfig();
            }
            catch
            {
            }
        });
    }

    private async Task<bool> ApplyHidKeyboardCoreAsync(int mode, bool on, bool showStatus = true)
    {
        KeyboardRgb? rgb = EnsureKeyboardRgb();
        if (rgb == null)
        {
            if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_79;
            return false;
        }
        try
        {
            rgb.KbPowerOn = on;
            if (!on)
            {
                bool offReady = rgb.IsConnected || await rgb.EnsureHidReadyAsync() || rgb.Connect();
                if (!offReady)
                {
                    rgb.KbPowerOn = true;
                    if (L_Mechrevo.WpfP0.Services.StartupPopupGate.SuppressAutomaticPopup)
                    {
                        Logger.WriteLine("[startup] HID 关灯暂不可用，静默重试：" + rgb.LastError);
                    }
                    else
                    {
                        if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_80 + rgb.LastError;
                    }
                    return false;
                }
                rgb.StopCurrentEffect();
                // 0.293.5.1：HID 关灯后同时走固件键盘电源关（NUC/UMI 自带通道，不走 GCU）。
                MechrevoHw? offHw = _backendProbe?.Hw;
                if (_backendProbe?.Service != null && offHw is { IsConnected: true }
                    && offHw.IsIntelNucMachine != true && offHw.IsUmiMachine != true)
                {
                    await _backendProbe.SetKeyboardPowerAsync(false).ConfigureAwait(true);
                }
                rgb.SaveConfig();
                if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_81;
                return true;
            }
            bool ready = rgb.IsConnected || await rgb.EnsureHidReadyAsync() || rgb.Connect();
            if (!ready)
            {
                if (!L_Mechrevo.WpfP0.Services.StartupPopupGate.SuppressAutomaticPopup)
                {
                    rgb.KbPowerOn = !on;
                    if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_82 + rgb.LastError;
                }
                else
                {
                    // 静默启动期间 HID 设备可能还没枚举完成：不弹错误、不回滚用户配置，
                    // 由 ApplyHidKeyboardAsync 的 ScheduleHidRetry 后台重试。
                    Logger.WriteLine("[startup] HID 暂不可用，静默重试：" + rgb.LastError);
                }
                return false;
            }
            // 0.293.5.1：HID 帧流前，如果固件键盘电源还是关的，先补一次电源；
            // 否则部分机型 HID 帧在线但背光仍不亮（NUC/UMI 不走这条 GCU 分流）。
            MechrevoHw? onHw = _backendProbe?.Hw;
            if (_backendProbe?.Service != null && onHw is { IsConnected: true, KeyboardPower: false }
                && onHw.IsIntelNucMachine != true && onHw.IsUmiMachine != true)
            {
                await _backendProbe.SetKeyboardPowerAsync(true).ConfigureAwait(true);
            }
            rgb.Brightness = Math.Clamp(rgb.Brightness, 1, 100);
            L_Mechrevo.WpfP0.Services.WpfBackend.NotifyLightingUserIntent();
            rgb.ReInitCustomMode();
            L_Mechrevo.WpfP0.Services.WpfBackend.MarkKeyboardCustomStatusBaseline();
            rgb.KbHidMode = mode;
            rgb.StartMode(mode);
            rgb.ApplyBrightnessToDevice();
            rgb.SaveConfig();
            if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_83 + mode;
            return true;
        }
        catch (Exception ex)
        {
            rgb.KbPowerOn = !on;
            if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_84 + ex.Message;
            return false;
        }
    }

    private async Task<bool> ApplyKeyboardLightAsync(bool powerOnly = false, bool showStatus = true)
    {
        await _keyboardLightApplyLock.WaitAsync();
        try
        {
            return await ApplyKeyboardLightCoreAsync(powerOnly, showStatus);
        }
        finally
        {
            _keyboardLightApplyLock.Release();
        }
    }

    private void EnsureKeyboardBrightnessOnPowerOn()
    {
        if (_backendProbe?.Hw == null || IsHidKeyboard(_backendProbe.Hw))
        {
            return;
        }
        try
        {
            string currentEffect = (KeyboardEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
            LightChannelSettings settings = MechrevoLite.Hardware.LightingSettingsStore.Load("Keyboard/Ctrl", currentEffect);
            if (settings.Light <= 0)
            {
                MechrevoLite.Hardware.LightingSettingsStore.Save("Keyboard/Ctrl", settings with { Light = 1 });
            }
        }
        catch
        {
        }
    }

    private async Task<bool> ApplyKeyboardLightCoreAsync(bool powerOnly = false, bool showStatus = true)
    {
        if (_backendProbe == null || KeyboardEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return false;
        }
        bool on = KeyboardLightSwitch.IsChecked == true;
        string effect = item.Tag as string ?? "Single";
        if (effect.StartsWith("hid:", StringComparison.Ordinal))
        {
            int hidMode = int.TryParse(effect.AsSpan(4), out int parsedHidMode) ? parsedHidMode : 1;
            return await ApplyHidKeyboardAsync(hidMode, on, showStatus: showStatus);
        }
        if (!on)
        {
            bool offOk = await _backendProbe.SetKeyboardPowerAsync(false);
            if (showStatus) StatusText.Text = offOk ? WpfStrings.OriginalStyleWindow_xaml_Text_81 : WpfStrings.LightKeyboardFailed;
            return offOk;
        }
        EnsureKeyboardBrightnessOnPowerOn();
        if (_backendProbe.Hw?.KeyboardPower != true)
        {
            bool powerOk = await _backendProbe.SetKeyboardPowerAsync(true);
            if (!powerOk)
            {
                if (showStatus) StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_85;
                return false;
            }
        }
        if (powerOnly)
        {
            // 按 0.293.5.1：开关只补电源/亮度，不重发 SetEffectALL。
            // 重发效果帧会被部分 GCU 固件当成新灯效并复位 KeyboardPower。
            MechrevoService? keyboardService = _backendProbe.Service;
            if (keyboardService != null)
            {
                int brightness = Math.Clamp(_backendProbe.Hw?.KeyboardBrightness ?? 100, 1, 100);
                int hardwareLevel = Math.Clamp((brightness + 12) / 25, 1, 4);
                // 必须等亮度/效果命令完成再返回：如果这里 fire-and-forget，
                // 用户紧接着关灯时，延迟的 SetLightingLevel(light=1) 会在关灯之后
                // 把键盘重新点亮（“灭了又亮”）。ApplyKeyboardLightAsync 已有锁，
                // 等它完成即可保证关灯命令后发、最终生效。
                await keyboardService.SetKeyboardBrightnessPreservingEffect(hardwareLevel).ConfigureAwait(true);
            }
            if (showStatus) StatusText.Text = WpfStrings.LightKeyboardOn;
            return true;
        }
        bool ok = await SetKeyboardEffectServiceAsync(effect);
        if (ok)
        {
            MechrevoLite.Hardware.LightingSettingsStore.Save("Keyboard/Ctrl",
                MechrevoLite.Hardware.LightingSettingsStore.Load("Keyboard/Ctrl", effect) with { Effect = effect });
        }
        if (showStatus) StatusText.Text = ok ? WpfStrings.LightKeyboardSentPrefix + effect : WpfStrings.LightKeyboardFailed;
        return ok;
    }

    private async void LogoLightSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingLighting)
        {
            return;
        }
        bool requested = LogoLightSwitch.IsChecked == true;
        _pendingChannelPower[1] = requested;
        _pendingChannelTick[1] = Environment.TickCount64;
        LogoLightSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplyLogoLightAsync();
        }
        finally
        {
            LogoLightSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _syncingLighting = true;
            LogoLightSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_86;
            return;
        }
        if (_backendProbe?.Hw?.IsConnected == true)
        {
            MechrevoLite.Hardware.LightingSettingsStore.SavePower("HidLightbar_Logo/Ctrl", requested, "用户点击「灯光」开关");
            OsdService.Show(LightChannelGlyph("HidLightbar_Logo/Ctrl"), LightChannelTitle("HidLightbar_Logo/Ctrl"), requested ? WpfStrings.StateOn : WpfStrings.StateOff);
        }
    }

    private async void LogoEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLighting || LogoEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        if (LogoLightSwitch.IsChecked != true)
        {
            SaveChannelEffect("HidLightbar_Logo/Ctrl", effect);
            return;
        }
        if (_backendProbe == null)
        {
            SaveChannelEffect("HidLightbar_Logo/Ctrl", effect);
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_87;
            return;
        }
        bool logoOk = await SetLogoEffectServiceAsync(effect);
        if (logoOk)
        {
            SaveChannelEffect("HidLightbar_Logo/Ctrl", effect);
        }
        StatusText.Text = logoOk ? WpfStrings.LightLogoSentPrefix + effect : WpfStrings.LightLogoFailed;
    }

    private async Task<bool> ApplyLogoLightAsync(bool restoreEffect = false)
    {
        if (_backendProbe == null || LogoEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return false;
        }
        bool on = LogoLightSwitch.IsChecked == true;
        string effect = item.Tag as string ?? "Single";
        bool ok;
        if (on)
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Logo/Ctrl", true);
            if (ok && restoreEffect)
            {
                ok = await SetLogoEffectServiceAsync(effect);
            }
        }
        else
        {
            ok = await SetLightPowerServiceAsync("HidLightbar_Logo/Ctrl", false);
        }
        StatusText.Text = ok ? (on ? WpfStrings.LightLogoSentPrefix + effect : WpfStrings.LightLogoOff) : WpfStrings.LightLogoFailed;
        return ok;
    }

    internal bool SupportsLcdOverdrive => _backendProbe?.SupportsLcdOverdrive == true;

    internal bool LcdOverdrive => _backendProbe?.LcdOverdrive == true;

    internal async Task<bool> SetLcdOverdriveAsync(bool on)
    {
        return _backendProbe != null && await _backendProbe.SetLcdOverdriveAsync(on);
    }

    internal bool IsDark => _dark;

    internal bool BackendConnected => _backendProbe?.Connected == true;

    internal bool IsBackgroundEnabled => BackgroundToggle.IsChecked == true;

    private void ScheduleStatusReset()
    {
        if (!IsLoaded || StatusText == null)
        {
            return;
        }
        _statusResetTimer.Stop();
        if (string.Equals(StatusText.Text, _statusResetText, StringComparison.Ordinal))
        {
            return;
        }
        _statusResetTimer.Start();
    }

    internal void ApplyThemeFromSettings(bool dark)
    {
        _dark = dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        ApplyButtonOpacity();
        _trayIcon?.SetTheme(_dark);
        ThemeButton.Content = _dark ? WpfStrings.LightTheme : WpfStrings.DarkTheme;
        AppConfig.Set("ui_dark_theme", _dark ? 1 : 0);
        AppConfig.Flush();
        UpdateBackgroundVisibility();
        RefreshSegmentHighlights();
        StatusText.Text = "0.293.6 / WPF P0 · " + (_dark ? WpfStrings.DarkTheme : WpfStrings.LightTheme);
    }

    internal void SetBackgroundFromSettings(bool on)
    {
        BackgroundToggle.IsChecked = on;
        AppConfig.Set("ui_bg_enabled", on ? 1 : 0);
        AppConfig.Flush();
        if (on)
        {
            ReloadBackgroundImage();
        }
        UpdateBackgroundVisibility();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        ApplyButtonOpacity();
        _trayIcon?.SetTheme(_dark);
        ThemeButton.Content = _dark ? WpfStrings.LightTheme : WpfStrings.DarkTheme;
        AppConfig.Set("ui_dark_theme", _dark ? 1 : 0);
        AppConfig.Flush();
        UpdateBackgroundVisibility();
        RefreshSegmentHighlights();
        StatusText.Text = "0.293.6 / WPF P0 · " + (_dark ? WpfStrings.DarkTheme : WpfStrings.LightTheme);
    }

    private void WideButton_Click(object sender, RoutedEventArgs e) => ShowWideWindow();

    /// <summary>宽屏切迷你后延迟释放宽屏窗口；TTL 内切回直接复用隐藏窗口。</summary>
    internal void ScheduleWideWindowRelease()
    {
        int seconds = AppConfig.Get("wide_window_cache_seconds", 120);
        string? env = Environment.GetEnvironmentVariable("LMECHREVO_WIDE_WINDOW_CACHE_SECONDS");
        if (int.TryParse(env, out int overrideSeconds))
        {
            seconds = overrideSeconds;
        }
        seconds = Math.Clamp(seconds, 5, 1800);
        _wideWindowCacheTimer.Stop();
        _wideWindowCacheTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, seconds * 1000L));
        _wideWindowCacheTimer.Tick -= WideWindowCacheTimer_Tick;
        _wideWindowCacheTimer.Tick += WideWindowCacheTimer_Tick;
        _wideWindowCacheTimer.Start();
        Logger.WriteLine("[wide-mem] 宽屏窗口已隐藏，将在 " + seconds + "s 后释放渲染");
    }

    private void WideWindowCacheTimer_Tick(object? sender, EventArgs e)
    {
        _wideWindowCacheTimer.Stop();
        MainWindow? window = _wideWindow;
        if (window == null || window.IsVisible)
        {
            return;
        }
        Logger.WriteLine("[wide-mem] 宽屏窗口缓存到期，释放窗口渲染");
        _wideWindow = null;
        try
        {
            window.Close();
        }
        catch
        {
        }
    }

    /// <summary>迷你窗切宽屏后延迟释放迷你窗视觉树渲染；TTL 内切回复用原 Content。</summary>
    internal void ScheduleMiniRenderRelease()
    {
        int seconds = AppConfig.Get("mini_window_render_cache_seconds", 120);
        string? env = Environment.GetEnvironmentVariable("LMECHREVO_MINI_RENDER_CACHE_SECONDS");
        if (int.TryParse(env, out int overrideSeconds))
        {
            seconds = overrideSeconds;
        }
        seconds = Math.Clamp(seconds, 5, 1800);
        _miniRenderCacheTimer.Stop();
        _miniRenderCacheTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, seconds * 1000L));
        _miniRenderCacheTimer.Tick -= MiniRenderCacheTimer_Tick;
        _miniRenderCacheTimer.Tick += MiniRenderCacheTimer_Tick;
        _miniRenderCacheTimer.Start();
        Logger.WriteLine("[wide-mem] 迷你窗已隐藏，将在 " + seconds + "s 后释放渲染");
    }

    private void MiniRenderCacheTimer_Tick(object? sender, EventArgs e)
    {
        _miniRenderCacheTimer.Stop();
        if (IsVisible || Content == null)
        {
            return;
        }
        Logger.WriteLine("[wide-mem] 迷你窗缓存到期，释放视觉树渲染");
        _miniCachedContent = Content;
        Content = null;
    }

    private void RestoreMiniRendering()
    {
        _miniRenderCacheTimer.Stop();
        if (_miniCachedContent == null)
        {
            return;
        }
        Content = _miniCachedContent;
        _miniCachedContent = null;
    }

    internal void ShowWideWindow()
    {
        _wideWindowCacheTimer.Stop();
        if (_wideWindow == null)
        {
            MainWindow window = new(this, _backendProbe);
            window.Closed += (_, _) => _wideWindow = null;
            _wideWindow = window;
        }
        AppConfig.Set("last_window_mode", "wide");
        AppConfig.Flush();
        Opacity = 0;
        ShowInTaskbar = false;
        Hide();
        ScheduleMiniRenderRelease();
        if (_wideWindow.WindowState == WindowState.Minimized)
        {
            _wideWindow.WindowState = WindowState.Normal;
        }
        _wideWindow.Show();
        _wideWindow.Activate();
    }

    internal void RefreshWideBackground()
    {
        _wideWindow?.ReloadWideBackground();
    }

    internal AppServices? Services => _services;

    internal void PositionCompactBottomRight()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Rect area = SystemParameters.WorkArea;
        double compactWidth = double.IsNaN(Width) ? ActualWidth : Width;
        double compactHeight = double.IsNaN(Height) ? ActualHeight : Height;
        Left = area.Right - compactWidth - 16;
        Top = area.Bottom - compactHeight - 16;
    }

    internal void ShowCompactWindow()
    {
        RestoreMiniRendering();
        Opacity = 1;
        ShowInTaskbar = true;
        PositionCompactBottomRight();
        Show();
        Activate();
    }

    private async void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string name)
        {
            return;
        }
        switch (name)
        {
            case "设置":
                new SettingsWindow(this).ShowDialog();
                return;

            case "检查更新":
                _ = CheckForUpdatesAsync();
                return;

            case "悬浮窗":
                ToggleTrayOverlay();
                StatusText.Text = _overlayWindow?.IsVisible == true ? WpfStrings.OverlayOpened : WpfStrings.OverlayClosed;
                return;

            case "赞助":
                new SponsorWindow { Owner = this }.ShowDialog();
                return;

            case "诊断":
                await ExportDiagnosticPackAsync();
                return;

            case "退出":
                Close();
                Application.Current.Shutdown();
                return;
        }
    }

    private OverlaySnapshot GetOverlaySnapshot()
    {
        try
        {
            // 保留既有采样触发（2 s 遥测/悬浮窗节奏不变）；⛔ 不在此处直接读静态传感器字段
            L_Mechrevo.WpfP0.Services.PowerStatusService.ReadOverlay();
        }
        catch
        {
        }
        // ⭐ P0-3：温度/占用/频率/风扇/功耗统一由共享 ISensorService 快照提供；
        //   VRAM/RAM（SensorSnapshot 暂无字段）仍从后端对象取，未连接回退 -1 ✅
        SensorSnapshot snapshot = _services?.Sensors.Read() ?? SensorSnapshot.Empty;
        MechrevoHw? hw = _backendProbe?.Hw;
        int battery = hw?.BatteryPercent ?? -1;
        try
        {
            double pct = L_Mechrevo.WpfP0.Services.PowerStatusService.Read().Percent;
            if (battery < 0 && pct >= 0)
            {
                battery = (int)Math.Round(pct);
            }
        }
        catch
        {
        }
        bool acOnline = false;
        bool charging = false;
        try
        {
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            acOnline = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            charging = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
        }
        catch
        {
        }
        return new OverlaySnapshot(
            // 悬浮窗必须和主界面同一口径：快照没有值时回退到本地优先源
            // （Wujie/星耀 APU 的 hw.CpuTemp 是 ACPI 热区，可能比真实 Tctl 高 20℃+）。
            snapshot.CpuTempC > 0 ? snapshot.CpuTempC : (MechrevoLite.HardwareControl.cpuTemp > 0
                ? MechrevoLite.HardwareControl.cpuTemp : (hw?.CpuTemp ?? -1)),
            snapshot.CpuUsagePercent,
            snapshot.CpuFrequencyMhz,
            snapshot.GpuTempC > 0 ? snapshot.GpuTempC : (MechrevoLite.HardwareControl.gpuTemp > 0
                ? MechrevoLite.HardwareControl.gpuTemp : (hw?.GpuTemp ?? -1)),
            snapshot.GpuUsagePercent,
            snapshot.GpuCoreFrequencyMhz,
            MechrevoLite.HardwareControl.vramUsedMb ?? (hw != null && hw.VramUsedMb > 0 ? hw.VramUsedMb : -1),
            MechrevoLite.HardwareControl.vramUsage ?? -1,
            snapshot.CpuFanRpm > 0 ? snapshot.CpuFanRpm : (hw?.CpuFanRpm ?? -1),
            snapshot.GpuFanRpm > 0 ? snapshot.GpuFanRpm : (hw?.GpuFanRpm ?? -1),
            snapshot.CpuFanDutyPercent,
            snapshot.GpuFanDutyPercent,
            hw?.RamUsage ?? -1,
            hw?.RamUsedGb ?? -1,
            battery,
            acOnline,
            charging,
            (float)(snapshot.CpuPowerWatts ?? 0),
            (float)(snapshot.GpuPowerWatts ?? 0));
    }

    internal Task ExportDiagnosticPackForFeedbackAsync()
    {
        return ExportDiagnosticPackAsync();
    }

    private async Task ExportDiagnosticPackAsync()
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = WpfStrings.OriginalStyleWindow_xaml_Title,
            Filter = WpfStrings.ZipFileFilter,
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = DiagnosticPackExporter.BuildFileName(L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion, DateTime.Now)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_5;
        try
        {
            DiagnosticPackInputs inputs = DiagnosticPackCapture.Capture();
            DiagnosticPackResult result = await DiagnosticPackRunner.ExportAsync(dialog.FileName, inputs);
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_88 + System.IO.Path.GetFileName(result.ZipPath);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + result.ZipPath + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_89 + ex.Message;
        }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int preference = 2;
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
        }
    }

    private static bool ResolveStartupDarkTheme()
    {
        try
        {
            if (AppConfig.Is("ui_theme_follow_system"))
            {
                using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                return value is int number ? number == 0 : true;
            }
            return AppConfig.Get("ui_dark_theme", 1) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void ApplySavedAccent()
    {
        try
        {
            string? text = AppConfig.GetString("ui_accent_color");
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            Color color = (Color)ColorConverter.ConvertFromString(text);
            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
        }
        catch
        {
        }
    }

    private static void MigrateButtonOpacityDefault()
    {
        if (AppConfig.Is("ui_button_opacity_v2"))
        {
            return;
        }
        int previous = AppConfig.Get("ui_button_opacity", 100);
        if (previous >= 100)
        {
            AppConfig.Set("ui_button_opacity", 60);
            AppConfig.Set("ui_button_opacity_v2", 1);
            AppConfig.Flush();
        }
    }

    internal void ApplyButtonOpacity()
    {
        int opacity = Math.Clamp(AppConfig.Get("ui_button_opacity", 60), 0, 100);
        byte alpha = (byte)(opacity * 255 / 100);
        Color normal = _dark
            ? Color.FromArgb(alpha, 0x2A, 0x30, 0x38)
            : Color.FromArgb(alpha, 0xE8, 0xEC, 0xF2);
        Color hover = _dark
            ? Color.FromArgb(alpha, 0x36, 0x3D, 0x47)
            : Color.FromArgb(alpha, 0xDD, 0xE2, 0xEA);
        Application.Current.Resources["ControlBrush"] = new SolidColorBrush(normal);
        Application.Current.Resources["ControlHoverBrush"] = new SolidColorBrush(hover);
    }

    internal void ApplyDockOpacity()
    {
        int opacity = Math.Clamp(AppConfig.Get("ui_dock_opacity", 70), 0, 100);
        byte alpha = (byte)(opacity * 255 / 100);
        Color baseColor = _dark ? Color.FromRgb(0x16, 0x1A, 0x20) : Colors.White;
        Application.Current.Resources["DockBrush"] =
            new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    private void UpdateTitleIcon()
    {
        if (TitleIconImage != null)
        {
            TitleIconImage.Opacity = _dark ? 1.0 : 0.88;
        }
    }

    private void ApplyDarkTitleBar(bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int flag = dark ? 1 : 0;
            int result = DwmSetWindowAttribute(hwnd, 20, ref flag, sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref flag, sizeof(int));
            }
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
