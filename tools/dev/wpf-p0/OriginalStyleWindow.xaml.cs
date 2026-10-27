using System.ComponentModel;
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
    internal event Action? BackendReady;
    internal BackendProbe? CurrentBackend => _backendProbe;
    private bool _overlayPreloaded;
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
    private bool _syncingPerfConfig;
    private int _pendingMode = -1;
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
    private bool _syncingDisplayEffect;
    private bool _syncingBatteryLimit;
    private bool _batteryLimitLoaded;
    private bool? _chargeLimitChannelProbe;
    private long _chargeLimitChannelProbeTick;
    private BackendProbe? _backendProbe;
    // 托盘/后台默认 2s；悬浮窗打开时临时提到 1s。
    private readonly DispatcherTimer _backendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _backendRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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
        ("taskbarautohide", QuickSwitchService.Label("taskbarautohide", "任务栏自动隐藏"), "\uE7F4"), ("transparency", QuickSwitchService.Label("transparency", "系统透明效果"), "\uE790"),
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
        Closed += (_, _) => _statusResetTimer.Stop();
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
        _backendTimer.Tick += (_, _) =>
        {
            if (_backendProbe == null)
            {
                return;
            }
            if (!HasVisibleSensorConsumer())
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
            if (!IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!HasVisibleSensorConsumer())
                    {
                        L_Mechrevo.WpfP0.Services.MemoryTrimService.TrimIfIdle(idle: true);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        Closed += (_, _) =>
        {
            _backendTimer.Stop();
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
            HighlightSegment(Refresh180Button);
            HighlightSegment(BatteryLongButton);
        }
        MainScroll.SizeChanged += delegate
        {
            UpdateContentWidth();
        };
        SourceInitialized += (_, _) =>
        {
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
            if (string.Equals(AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
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

    internal bool WideSilentTurboVisible => _backendProbe?.Capabilities?.SilentTurboAvailability
        == MechrevoLite.Hardware.FeatureAvailability.Supported || UiQaShowAll.Enabled;

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
            ScreenControl.SetScreen(hertz);
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
            string? laptop = MechrevoLite.Display.ScreenNative.FindLaptopScreen(false);
            int readback = -1;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(150);
                }
                readback = MechrevoLite.Display.ScreenNative.GetRefreshRate(laptop);
                if (readback > 0 && Math.Abs(readback - hertz) <= 2)
                {
                    break;
                }
            }
            int highlight = readback > 0 && Math.Abs(readback - hertz) <= 2 ? readback : hertz;
            Refresh60Button.IsChecked = highlight < _refreshHighHz;
            Refresh180Button.IsChecked = highlight >= _refreshHighHz;
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
                    || string.Equals(effect, "Single", StringComparison.Ordinal)
                    || string.Equals(effect, "Breathing", StringComparison.Ordinal))
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
                return probe.ApplyIntelNucLightbar(effect, light, lightColor);
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

    internal async Task<bool> WideToggleQuickAsync(string key)
    {
        if (_services != null)
        {
            return (await _services.QuickSwitches.ToggleAsync(key)).Ok;
        }
        if (_backendProbe == null)
        {
            return false;
        }
        bool current = _backendProbe.QuickState(key);
        return await _backendProbe.SwitchQuickAsync(key, !current);
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
                return _backendProbe?.Hw?.DisplayEffectProfiles.Count > 0
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
                ("taskbarautohide", QuickSwitchService.Label("taskbarautohide", "任务栏自动隐藏")), ("transparency", QuickSwitchService.Label("transparency", "系统透明效果")),
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
        if (_services != null) { _services.Lighting.SetLightIdleSeconds(seconds); return; }
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
        AppConfig.Set("lighting_off_on_battery", on ? 1 : 0);
        AppConfig.Flush();
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
    internal bool WideBatteryModeVisible => _backendProbe?.Hw?.UsesBaolongda != true;

    internal (string Info, int Mode, string Charge) WideBattery()
    {
        try
        {
            int mode = _services?.Battery.CurrentMode ?? _backendProbe?.BatteryMode ?? -1;
            // 无极限充/EC 限充生效时，三档是 EC 上限值来源，高亮必须跟选定百分比走。
            if (ChargeSliderToggle.IsChecked == true || MechrevoLite.Battery.BatteryControl.LockEnabled())
            {
                mode = ModeForPercent(MechrevoLite.Battery.BatteryControl.SelectedModePercent());
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
            string? laptopScreen = MechrevoLite.Display.ScreenNative.FindLaptopScreen(false);
            bool fallback = _backendProbe?.Hw?.UsesBaolongda == true;
            IReadOnlyList<int> rates = MechrevoLite.Display.ScreenNative.GetAvailableRefreshRates(laptopScreen, fallback);
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
            if (_services != null) { _services.Display.SetBrightness(value); } else { ScreenBrightness.Set(value); }
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
            UpdateBackendTimerInterval();
        };
        _overlayWindow.IsVisibleChanged += (_, _) => UpdateBackendTimerInterval();
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
            probe?.Capabilities?.SilentTurboAvailability == MechrevoLite.Hardware.FeatureAvailability.Supported
                || UiQaShowAll.Enabled,
            MechrevoLite.Hardware.MechrevoService.IsSilentTurboActive,
            probe?.Capabilities?.TurboMode == true
                || probe?.Hw?.UsesBaolongda == true
                || (probe?.Hw != null && !probe.Hw.IsConnected && MechrevoHw.IsGcuServiceInstalled && !probe.Hw.UsesBaolongda));
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
            return;
        }
        _overlayWindow.Show();
        _overlayWindow.Activate();
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
    /// （后者在可得反编译源里是空 stub，Core 无同源 API ⇒ 安全跳过并记录，⛔ 不伪造）。
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
                await ApplyKeyboardLightAsync();
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
            await ApplyKeyboardLightAsync();
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
        Refresh180Button.IsChecked = true;
        Refresh60Button.IsChecked = false;
        HighlightSegment(Refresh180Button);
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
        EcDirectOffToggle.IsEnabled = !on;
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
            supported = hw != null && BatteryLimitPanel.IsSupportedBoard(hw);
        }
        catch
        {
            supported = false;
        }
        Visibility visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        bool baolongda = _backendProbe?.Hw?.UsesBaolongda == true;
        BatteryModePanel.Visibility = baolongda ? Visibility.Collapsed : Visibility.Visible;
        ChargeLimitRow.Visibility = visibility;
        ChargeLimitInputRow.Visibility = visibility;
        ChargeLimitStatus.Visibility = visibility;
        ChargeSliderToggle.Visibility = (visibility == Visibility.Visible && !baolongda) ? Visibility.Visible : Visibility.Collapsed;
        EcDirectOffToggle.Visibility = (visibility == Visibility.Visible && !baolongda) ? Visibility.Visible : Visibility.Collapsed;
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

    private async Task InitBatteryLimitControlsAsync()
    {
        try
        {
            if (_backendProbe?.Hw == null || !BatteryLimitPanel.IsSupportedBoard(_backendProbe.Hw))
            {
                return;
            }
            (bool ok, int high, int low, int mode) = await Task.Run(() =>
            {
                bool read = BatteryLimitWriter.TryReadState(out int h, out int l, out int m, out _);
                return (read, h, l, m);
            });
            if (ok)
            {
                ChargeLimitLowBox.Text = Math.Clamp(low, 1, 99).ToString();
                ChargeLimitHighBox.Text = Math.Clamp(high, 2, 100).ToString();
                ChargeLimitSwitch.IsChecked = mode == BatteryLimitWriter.ModeActive;
                ChargeLimitStatus.Text = mode == BatteryLimitWriter.ModeActive
                    ? WpfStrings.ChargeAppliedPrefix + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen
                    : WpfStrings.ChargeNotEnabled;
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

    private void RefreshBatteryLimitState()
    {
        try
        {
            if (BatteryLimitWriter.TryReadState(out int high, out int low, out int mode, out _))
            {
                ChargeLimitSwitch.IsChecked = mode == BatteryLimitWriter.ModeActive;
                ChargeLimitStatus.Text = mode == BatteryLimitWriter.ModeActive
                    ? WpfStrings.ChargeAppliedPrefix + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen
                    : WpfStrings.ChargeNotEnabled;
            }
        }
        catch
        {
        }
    }

    private void ChargeLimitSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChargeLimitSwitch.IsChecked == true;
        try
        {
            bool ok;
            if (enable)
            {
                if (!TryReadChargeThreshold(out int low, out int high, out string error))
                {
                    ChargeLimitStatus.Text = error;
                    ChargeLimitSwitch.IsChecked = false;
                    return;
                }
                ServiceResult chargeEnable = _services != null
                    ? _services.ChargeLimit.EnableThresholdRaw(low, high)
                    : EnableChargeLimitDirect(low, high);
                ok = chargeEnable.Ok;
                string detail = chargeEnable.Detail;
                if (ok)
                {
                    BatteryLimitRestore.SaveConfig(true, low, high);
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_18 + high + WpfStrings.ChargeStartSeparator + low + "%" + WpfStrings.ClosingParen;
                }
                else
                {
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_19 + detail;
                }
            }
            else
            {
                ServiceResult chargeDisable = _services != null
                    ? _services.ChargeLimit.DisableThresholdRaw()
                    : DisableChargeLimitDirect();
                ok = chargeDisable.Ok;
                string detail = chargeDisable.Detail;
                if (ok)
                {
                    BatteryLimitRestore.SaveConfig(false, 0, 0);
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_20;
                }
                else
                {
                    ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_21 + detail;
                }
            }
            if (!ok)
            {
                RefreshBatteryLimitState();
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = WpfStrings.OriginalStyleWindow_xaml_Text_22 + ex.Message;
            RefreshBatteryLimitState();
        }
    }

    private void ChargeLimitApply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadChargeThreshold(out int low, out int high, out string error))
        {
            ChargeLimitStatus.Text = error;
            return;
        }
        try
        {
            ServiceResult chargeApply = _services != null
                ? _services.ChargeLimit.EnableThresholdRaw(low, high)
                : EnableChargeLimitDirect(low, high);
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
    }

    private static ServiceResult EnableChargeLimitDirect(int low, int high)
    {
        bool ok = BatteryLimitWriter.TryEnable(low, high, allowInitialDischarge: true, out string detail);
        return ok ? ServiceResult.Success(detail) : ServiceResult.Fail(detail);
    }

    private static ServiceResult DisableChargeLimitDirect()
    {
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
        double width = MainScroll.ViewportWidth - 22;
        if (width <= 100)
        {
            width = MainScroll.ActualWidth - 30;
        }
        if (width <= 100)
        {
            return;
        }
        ContentPanel.Width = width;
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
            MaxWidth = 420;
            MinWidth = 420;
            Width = 420;
            WideButton.Content = WpfStrings.OriginalStyleWindow_xaml_Content_2;
        }
        Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void ModeButton_Click(object sender, RoutedEventArgs e)
    {
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
            _ => WpfStrings.ModeCustom
        };
        OsdService.Show(string.Empty, WpfStrings.OsdPerformance, osdMode);
        _backendProbe?.MarkUserModeSwitch();
        try
        {
            MechrevoService.ModeSwitchResult result = _services != null
                ? await _services.Performance.SetModeResultAsync(mode)
                : await _backendProbe!.SwitchModeResultAsync(mode);
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
            if (ok && mode == 1 && silentTurbo == true && !turboSubModeSupported)
            {
                ok = false;
            }
            else if (ok && mode == 1 && silentTurbo.HasValue && turboSubModeSupported)
            {
                ok = _backendProbe?.Service != null
                    && await _backendProbe.Service.SwitchTurboSubMode(silentTurbo.Value);
            }
            BackendStatus.Text = ok ? WpfStrings.OriginalStyleWindow_xaml_Text_8 : WpfStrings.PerfModeFailed;
            if (!ok)
            {
                // 旧版失败后会 RefreshCurrentModeAsync 再回填；不能拿缓存的 CurrentMode 直接回弹。
                int actualMode = await _backendProbe!.RefreshCurrentModeAsync();
                if (actualMode >= 0)
                {
                    Logger.WriteLine("[perfmode] 切档失败，回读实际档=" + actualMode);
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
                }
                SyncGpuDbForMode(mode, "WPF 切档");
                SyncCustomWindowMode(mode);
                RefreshWideCustomEditor();
            }
        }
        finally
        {
            if (request == _modeRequestId)
            {
                _modeSwitchInFlight = false;
                RefreshFromBackend();
            }
        }
    }

    internal bool WideModeSwitchPending => _modeSwitchInFlight || _pendingMode >= 0;

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

    private static int ResolveDisplayMode(BackendProbe probe)
    {
        // 自定义档在大多数 GCU 上不会回读成一个独立视觉档；以 PerfModeService
        // 的当前显示档为准，避免切到自定义后主界面又回落到内置档高亮。
        string displayedId = MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId
            ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId;
        if (displayedId?.StartsWith("custom", StringComparison.OrdinalIgnoreCase) == true)
        {
            return 3;
        }
        MechrevoHw? hw = probe.Hw;
        // 宝龙达 / 无界没有原生自定义固件档：ActiveModeId 才是权威。
        if (hw?.UsesBaolongda == true || hw?.SmuCustomApu == true)
        {
            if (string.Equals(MechrevoLite.Mode.PerfModeStore.ActiveModeId, "custom", StringComparison.OrdinalIgnoreCase)
                || (MechrevoLite.Mode.PerfModeStore.ActiveModeId?.StartsWith("custom", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return 3;
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

    private int _refreshHighHz = 180;

    private void SyncRefreshRateState()
    {
        try
        {
            AutoRefreshSwitch.IsChecked = AppConfig.Is("screen_auto");
            int min = ScreenControl.MIN_RATE > 0 ? ScreenControl.MIN_RATE : 60;
            int max = 0;
            try
            {
                max = ScreenNative.GetMaxRefreshRate(ScreenNative.FindLaptopScreen(false));
            }
            catch
            {
            }
            if (max <= min)
            {
                max = 180;
            }
            _refreshHighHz = max;
            Refresh180Button.Tag = max.ToString();
            Refresh180Button.Content = max + "Hz";
            Refresh60Button.Tag = min.ToString();
            Refresh60Button.Content = min + "Hz";
            int current = ScreenNative.GetRefreshRate(ScreenNative.FindLaptopScreen(false));
            bool high = current > min;
            Refresh180Button.IsChecked = high;
            Refresh60Button.IsChecked = !high;
            HighlightSegment(high ? Refresh180Button : Refresh60Button);
        }
        catch
        {
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
            GpuRestartPromptWindow prompt = new GpuRestartPromptWindow(modeName) { Owner = this };
            prompt.ShowDialog();
            if (prompt.Result == GpuPromptResult.Cancel)
            {
                RefreshFromBackend();
                return;
            }
            if (prompt.Result == GpuPromptResult.SwitchAndRestart)
            {
                bool restartOk = _services != null
                    ? (await _services.Gpu.RequestRestartAsync(mode)).Ok
                    : await _backendProbe.RequestGpuModeRestartAsync(mode);
                if (restartOk)
                {
                    if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_40;
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
            ? (await _services.Gpu.SwitchAsync(mode)).Ok
            : await _backendProbe.SwitchGpuModeAsync(mode);
        if (_services != null) { await _services.Gpu.RefreshAsync(); } else { await _backendProbe.RefreshGpuModeAsync(); }
        int current = _backendProbe.CurrentGpuMode;
        if (ok && current != mode)
        {
            if (_services != null) { _services.Gpu.MarkRestartPending(mode); } else { _backendProbe.MarkGpuRestartPending(mode); }
        }
        else if (ok)
        {
            if (_services != null) { _services.Gpu.ClearRestartPending(); } else { _backendProbe.ClearGpuRestartPending(); }
        }
        StatusText.Text = ok ? WpfStrings.GpuModeSentRestart : WpfStrings.GpuModeFailed;
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
        // 无极限充 / EC 限充生效时，三档只作为 EC 上限值来源，写 EC 0x7B9，不发 GCU 档位。
        if (ChargeSliderToggle.IsChecked == true)
        {
            bool sliderOk = MechrevoLite.Battery.BatteryControl.CaptureChargeLimitGesture()
                && MechrevoLite.Battery.BatteryControl.ApplyChargeLimitFromUserGesture(percentForMode);
            if (sliderOk)
            {
                _syncingBatteryLimit = true;
                BatteryLimitSlider.Value = Math.Clamp(percentForMode, 40, 100);
                _syncingBatteryLimit = false;
                ApplyBatteryModeHighlight(mode);
                StatusText.Text = WpfStrings.BatteryModeSent;
            }
            else
            {
                StatusText.Text = WpfStrings.BatteryModeFailed;
            }
            return;
        }
        // GCU 与宝龙达是两套：宝龙达走自身阈值面板；GCU 若已打开 EC 限充，三档改走 EC 直写，
        // 否则仍走 GCU 官方 BatteryProtection。
        if (_backendProbe.Hw?.UsesBaolongda != true
            && MechrevoLite.Battery.BatteryControl.LockEnabled())
        {
            bool ecOk = MechrevoLite.Battery.BatteryControl.CaptureChargeLimitGesture()
                && MechrevoLite.Battery.BatteryControl.ApplyChargeLimitFromUserGesture(percentForMode);
            StatusText.Text = ecOk ? WpfStrings.BatteryModeSent : WpfStrings.BatteryModeFailed;
            return;
        }
        bool ok = _services != null
            ? (await _services.Battery.SetModeAsync(mode)).Ok
            : await _backendProbe.SetBatteryModeAsync(mode);
        StatusText.Text = ok ? WpfStrings.BatteryModeSent : WpfStrings.BatteryModeFailed;
    }

    private void RefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        int requested = int.Parse(text);
        Refresh180Button.IsChecked = requested >= _refreshHighHz;
        Refresh60Button.IsChecked = requested < _refreshHighHz;
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
            ? (await _services.Display.SetAutoRefreshAsync(on)).Ok
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
        bool ok = await _backendProbe.SwitchQuickAsync(key, on);
        StatusText.Text = ok ? WpfStrings.QuickSentPrefix + key : WpfStrings.QuickFailedPrefix + key;
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
        QuickTilePickerWindow picker = new QuickTilePickerWindow(choices, 6)
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
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
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
        HighlightChecked(Refresh180Button, Refresh60Button);
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
                ("taskbarautohide", QuickSwitchService.Label("taskbarautohide", "任务栏自动隐藏")), ("transparency", QuickSwitchService.Label("transparency", "系统透明效果")),
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
            bool ok = await _backendProbe.SwitchQuickAsync(key, box.IsChecked == true);
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

    internal bool WideTraySensorEnabled => MechrevoLite.AppConfig.Get("tray_sensor_tooltip", 1) == 1;

    internal void WideSetTraySensorEnabled(bool enabled)
    {
        MechrevoLite.AppConfig.Set("tray_sensor_tooltip", enabled ? 1 : 0);
        MechrevoLite.AppConfig.Flush();
        if (!enabled)
        {
            _trayIcon?.SetTooltip("L-Mechrevo");
        }
        RefreshFromBackend();
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
        return WideTraySensorEnabled;
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
        BackendLine.Text = probe.Snapshot();
        UpdateTraySensorTooltip(probe);
        HookHardwareState(probe.Hw);
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
        string powerState = "";
        try
        {
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            bool charging = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
            string source = FormatPowerSource(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
            string state = charging
                ? WpfStrings.BatteryCharging
                : (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online
                    ? (hw.BatteryPercent >= 99 ? WpfStrings.BatteryFull : WpfStrings.BatteryNotCharging)
                    : WpfStrings.BatteryDischarging);
            powerState = " · " + source + " · " + state;
        }
        catch
        {
        }
        int batteryPercent = hw.BatteryPercent;
        if (batteryPercent < 0)
        {
            try
            {
                float life = System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent;
                if (life >= 0f && life <= 1f)
                {
                    batteryPercent = (int)Math.Round(life * 100f);
                }
            }
            catch
            {
            }
        }
        BatteryInfo.Text = WpfStrings.OriginalStyleWindow_xaml_Text_46 + batteryPercent + "%" + powerState + (string.IsNullOrWhiteSpace(health) ? "" : " · " + health) + abnormal;
        BatteryInfo.ToolTip = health;
        int mode = ResolveDisplayMode(probe);
        GpuOverclockSwitch.Visibility = (mode == 1 && probe.GpuOcSupported) ? Visibility.Visible : Visibility.Collapsed;
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
        UpdateGpuButtons(displayGpu, pending);
        bool skipModeUi = _modeSwitchInFlight;
        bool silentTurboSupported = probe.Capabilities?.SilentTurboAvailability
            == MechrevoLite.Hardware.FeatureAvailability.Supported;
        bool silentTurboActive = mode == 1 && silentTurboSupported && MechrevoService.IsSilentTurboActive;
        CurrentModeText.Text = WpfStrings.CurrentPrefix + (mode switch
        {
            2 => WpfStrings.ModeSilent,
            1 => silentTurboActive
                ? WpfStrings.SilentTurbo
                : silentTurboSupported ? WpfStrings.TurboHighEnergy : WpfStrings.ModeTurbo,
            0 => WpfStrings.ModeBalanced,
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
            CustomModeButton.IsChecked = mode is 3 or 4;
            RefreshModeHighlights();
        }
        SyncLightSwitchesFromHardware();
        SyncRefreshRateState();
        RefreshPerfConfigVisibility(mode);
        RefreshPerfConfigSelections();
        SyncGpuDbForMode(mode, "WPF 后端模式同步");
        if (!skipModeUi)
        {
            SyncCustomWindowMode(mode);
        }
        ToggleButton[] batteryButtons = { BatteryLongButton, BatteryBalancedButton, BatteryWorkButton };
        int batteryMode = probe.BatteryMode;
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
    }

    private void UpdateBackgroundVisibility()
    {
        bool show = BackgroundToggle.IsChecked == true;
        BackgroundImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Fill = _dark
            ? new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255));
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
            MechrevoLite.Update.UpdateInfo? info = await MechrevoLite.Update.UpdateChecker.CheckAsync();
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

    private void ApplyCapabilities()
    {
        if (_backendProbe?.Capabilities is not MechrevoDeviceCapabilities caps)
        {
            return;
        }
        try
        {
            GpuSection.Visibility = (caps.DgpuDirect || caps.IgpuOnly || caps.GpuHotSwap) ? Visibility.Visible : Visibility.Collapsed;
            ScreenSection.Visibility = (caps.DisplayRefresh || caps.ColorCalibration || caps.LcdOverdrive || caps.LocalDimming) ? Visibility.Visible : Visibility.Collapsed;
            MechrevoHw? hw = _backendProbe?.Hw;
            bool hasKeyboard = caps.Keyboard || (hw?.SupportsKeyboard ?? false);
            bool hasLightbar = caps.Lightbar || (hw?.SupportsLightbar ?? false);
            bool hasLogo = caps.LogoLight || (hw?.SupportsLogoLight ?? false);
            LightingSection.Visibility = (hasKeyboard || hasLightbar || hasLogo) ? Visibility.Visible : Visibility.Collapsed;
            KeyboardLightRow.Visibility = hasKeyboard ? Visibility.Visible : Visibility.Collapsed;
            LogoLightRow.Visibility = hasLogo ? Visibility.Visible : Visibility.Collapsed;
            FillKeyboardEffects();
  FillLightChannelEffects();
  FillLightIdleOptions();
  L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.ApplyKeyboardAutoOffSetting();
  SyncLightSwitchesFromHardware();
  bool customAllowed = (caps.CpuPerformanceTuning || caps.FanSettings) && !(_backendProbe?.Hw?.SupportsModeProfileConfig ?? false);
            CustomModeButton.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
            // 原版只保证静音/平衡；部分 30 系机型没有狂暴档 ⇒ 隐藏狂暴按钮，30 系配置行随之隐藏。
            TurboModeButton.Visibility = (caps.TurboMode || UiQaShowAll.Enabled) ? Visibility.Visible : Visibility.Collapsed;
            bool silentTurboAllowed = caps.SilentTurboAvailability == MechrevoLite.Hardware.FeatureAvailability.Supported
                || UiQaShowAll.Enabled;
            EnsureSilentTurboButton(silentTurboAllowed);
            if (TurboModeButton.Content is TextBlock turboLabel)
            {
                turboLabel.Text = silentTurboAllowed ? WpfStrings.TurboHighEnergy : WpfStrings.OriginalStyleWindow_Text_22;
            }
            BuildMoreSwitches();
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
        bool igpuSupported = (caps?.IgpuOnly ?? false) || (hw?.SupportsIgpuOnly ?? false);
        GpuEcoButton.Visibility = ResolveRowVisibility(igpuMode, igpuSupported);
        GpuEcoColumn.Width = GpuEcoButton.Visibility == Visibility.Visible
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (GpuEcoButton.Visibility == Visibility.Visible)
        {
            GpuSection.Visibility = Visibility.Visible;
        }
        bool keyboard = (caps?.Keyboard ?? false) || (hw?.SupportsKeyboard ?? false);
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
        LightingSection.Visibility = (KeyboardLightRow.Visibility == Visibility.Visible
            || LightStripRow.Visibility == Visibility.Visible
            || LogoLightRow.Visibility == Visibility.Visible
            || LightHingeRow.Visibility == Visibility.Visible
            || LightSyncRow.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
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
        WaterCoolerRoutingService? routing = EnsureWaterCoolerRouting();
        if (routing == null || !routing.IsGcuRoute && !routing.IsBleRoute)
        {
            return false;
        }
        bool ok = await routing.SetFanAsync(profile);
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
                LiquidStatusButton.Content = routing?.StatusText ?? WpfStrings.OriginalStyleWindow_xaml_Content_3;
                LiquidPumpCombo.IsEnabled = false;
                LiquidFanCombo.IsEnabled = false;
                RefreshLiquidLightUi();
                return;
            }
            LiquidStatusButton.Content = routing?.StatusText ?? (WideLiquidConnected ? WpfStrings.LiquidConnected : WpfStrings.LiquidDisconnected);
            LiquidPumpCombo.IsEnabled = true;
            LiquidFanCombo.IsEnabled = true;
            _syncingLiquid = true;
            SelectComboByTag(LiquidPumpCombo, routing?.PumpProfile ?? _backendProbe?.Hw?.LcPumpControl ?? -1);
            SelectComboByTag(LiquidFanCombo, routing?.FanProfile ?? _backendProbe?.Hw?.LcFanControl ?? -1);
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
        LiquidSection.Children.Add(row);
        _liquidLightRow = row;
    }

    private void RefreshLiquidLightUi()
    {
        if (_liquidLightRow == null)
        {
            return;
        }
        bool supported = WideLiquidLightSupported;
        _liquidLightRow.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (!supported)
        {
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
        if (_syncingLiquid || _backendProbe?.Service == null || LiquidPumpCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        int profile = int.TryParse(item.Tag as string, out int parsed) ? parsed : -1;
        bool ok = profile < 0
            ? await _backendProbe.Service.SwitchLcFanAuto()
            : await _backendProbe.Service.SwitchLcPump(profile);
        StatusText.Text = ok ? WpfStrings.LiquidPumpSent : WpfStrings.LiquidPumpFailed;
    }

    private async void LiquidFan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLiquid || _backendProbe?.Service == null || LiquidFanCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        int profile = int.TryParse(item.Tag as string, out int parsed) ? parsed : -1;
        bool ok = profile < 0
            ? await _backendProbe.Service.SwitchLcFanAuto()
            : await _backendProbe.Service.SwitchLcFan(profile);
        StatusText.Text = ok ? WpfStrings.LiquidFanSent : WpfStrings.LiquidFanFailed;
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
        bool profileConfig = hw?.SupportsModeProfileConfig == true;
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
            ComboBox[] combos = { PerfConfigSilentCombo, PerfConfigBalancedCombo, PerfConfigTurboCombo };
            _syncingPerfConfig = true;
            foreach (ComboBox combo in combos)
            {
                if (combo.Tag is string tag && int.TryParse(tag, out int visualMode))
                {
                    combo.SelectedIndex = Math.Clamp(MechrevoLite.Mode.ModeProfileConfig.Get(visualMode), 0, Math.Max(0, combo.Items.Count - 1));
                }
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
        if (_backendProbe != null && _backendProbe.Service?.CurrentMode == visualMode)
        {
            await _backendProbe.SwitchModeAsync(visualMode);
        }
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
        MechrevoService.GpuOverclockEnabled = on;
        int modeBefore = _backendProbe.CurrentMode;
        StatusText.Text = on ? WpfStrings.GpuOverclockOnPending : WpfStrings.GpuOverclockOffPending;
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
        StatusText.Text = ok ? WpfStrings.GpuOverclockPrefix + (on ? WpfStrings.IgpuSavingOnPreview_Content : WpfStrings.OriginalStyleWindow_Content_5) : WpfStrings.GpuOverclockFailed;
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
        bool ok = await _backendProbe.SetDisplayEffectSwitchAsync(on);
        if (ok)
        {
            AppConfig.Set("display_effect", on ? 1 : 0);
            AppConfig.Flush();
            if (on)
            {
                await _backendProbe.ApplyDisplayEffectProfileAsync(SelectedDisplayEffectMode());
            }
            await _backendProbe.RequestDisplayEffectStatusAsync();
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
        if (_syncingDisplayEffect || _backendProbe == null || DisplayEffectSwitch.IsChecked != true)
        {
            return;
        }
        string mode = SelectedDisplayEffectMode();
        bool ok = await _backendProbe.ApplyDisplayEffectProfileAsync(mode);
        if (ok)
        {
            AppConfig.Set("display_effect_mode", mode);
            AppConfig.Flush();
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

    private void FillLightChannelEffects()
    {
        _syncingLighting = true;
        try
        {
            FillEffects(LightStripEffectCombo, EffectsForCurrentBackend(LightingCatalog.LightbarEffects), "HidLightbar/Ctrl");
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
            bool effectiveKeyboardPower = IsHidKeyboard(hw)
                ? (L_Mechrevo.WpfP0.Services.WpfBackend.rgb?.KbPowerOn ?? hw.KeyboardPower)
                : hw.KeyboardPower;
            if (_pendingKeyboardPower is bool pendingPower)
            {
                // 用户刚点的状态优先；只有回读真正追上才清 pending。
                // ⛔ 不用 5s 超时强制回填，否则下发成功但回读慢时会“回弹”。
                if (effectiveKeyboardPower != pendingPower)
                {
                    // 保持按钮状态，等下一次回读
                }
                else
                {
                    _pendingKeyboardPower = null;
                    KeyboardLightSwitch.IsChecked = MechrevoLite.Hardware.LightingState.IsLightSwitchOn(
                        MechrevoLite.KeyboardLightingDecision.ShouldWrite(hw.IsConnected, effectiveKeyboardPower), suspended);
                }
            }
            else
            {
                bool keyboardOn = hw.UsesBaolongda
                    ? effectiveKeyboardPower
                    : MechrevoLite.KeyboardLightingDecision.ShouldWrite(hw.IsConnected, effectiveKeyboardPower);
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
            int current;
            if (baolongda)
            {
                current = MechrevoHw.GetKeyboardAutoOffSeconds();
                foreach ((string Label, int Seconds) option in LightingCatalog.KeyboardAutoOffOptions)
                {
                    LightIdleCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Seconds.ToString() });
                }
            }
            else
            {
                current = AppConfig.Get("lighting_idle_seconds", 0);
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
        if (probe?.Connected != true)
        {
            return;
        }
        int timeoutSeconds = AppConfig.Get("lighting_idle_seconds", 0);
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
            }
            catch
            {
            }
        }
        _stateHookedHw = hw;
        if (hw != null)
        {
            hw.StateChanged += OnHardwareLightingStateChanged;
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
        Dispatcher.BeginInvoke(new Action(() => _ = RestoreLightingOnStartupAsync()));
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
                    await ApplyKeyboardLightAsync();
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
        }
        catch
        {
        }
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
            "HidLightbar/Ctrl" => LightingCatalog.LightbarEffects[0].Effect,
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
        await ApplyKeyboardLightAsync();
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
                await ApplyKeyboardLightAsync();
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
        OpenLightEditor("HidLightbar/Ctrl", WpfStrings.LightTitleLightbar, EffectsForCurrentBackend(LightingCatalog.LightbarEffects));
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
        KeyboardLightSwitch.IsEnabled = false;
        bool ok;
        try
        {
            ok = await ApplyKeyboardLightAsync();
        }
        finally
        {
            KeyboardLightSwitch.IsEnabled = true;
        }
        if (!ok)
        {
            _pendingKeyboardPower = null;
            _syncingLighting = true;
            KeyboardLightSwitch.IsChecked = !requested;
            _syncingLighting = false;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_78;
            return;
        }
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
            SaveChannelEffect("Keyboard/Ctrl", effect);
            _syncingLighting = true;
            KeyboardLightSwitch.IsChecked = true;
            _syncingLighting = false;
        }
        await ApplyKeyboardLightAsync();
    }

    private async Task<bool> ApplyHidKeyboardAsync(int mode, bool on, bool allowRetry = true)
    {
        await _hidApplyLock.WaitAsync();
        try
        {
            int generation = ++_hidApplyGeneration;
            bool ok = await ApplyHidKeyboardCoreAsync(mode, on);
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

    private async Task<bool> ApplyHidKeyboardCoreAsync(int mode, bool on)
    {
        KeyboardRgb? rgb = EnsureKeyboardRgb();
        if (rgb == null)
        {
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_79;
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
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_80 + rgb.LastError;
                    return false;
                }
                rgb.StopCurrentEffect();
                rgb.SaveConfig();
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_81;
                return true;
            }
            bool ready = rgb.IsConnected || await rgb.EnsureHidReadyAsync() || rgb.Connect();
            if (!ready)
            {
                rgb.KbPowerOn = !on;
                StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_82 + rgb.LastError;
                return false;
            }
            rgb.Brightness = Math.Clamp(rgb.Brightness, 1, 100);
            L_Mechrevo.WpfP0.Services.WpfBackend.NotifyLightingUserIntent();
            rgb.ReInitCustomMode();
            L_Mechrevo.WpfP0.Services.WpfBackend.MarkKeyboardCustomStatusBaseline();
            rgb.KbHidMode = mode;
            rgb.StartMode(mode);
            rgb.ApplyBrightnessToDevice();
            rgb.SaveConfig();
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_83 + mode;
            return true;
        }
        catch (Exception ex)
        {
            rgb.KbPowerOn = !on;
            StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_84 + ex.Message;
            return false;
        }
    }

    private async Task<bool> ApplyKeyboardLightAsync()
    {
        await _keyboardLightApplyLock.WaitAsync();
        try
        {
            return await ApplyKeyboardLightCoreAsync();
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

    private async Task<bool> ApplyKeyboardLightCoreAsync()
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
            return await ApplyHidKeyboardAsync(hidMode, on);
        }
        bool ok;
        if (on)
        {
            EnsureKeyboardBrightnessOnPowerOn();
            if (_backendProbe.Hw?.KeyboardPower != true)
            {
                bool powerOk = await _backendProbe.SetKeyboardPowerAsync(true);
                if (!powerOk)
                {
                    StatusText.Text = WpfStrings.OriginalStyleWindow_xaml_Text_85;
                    return false;
                }
            }
            ok = await SetKeyboardEffectServiceAsync(effect);
            if (ok)
            {
                MechrevoLite.Hardware.LightingSettingsStore.Save("Keyboard/Ctrl",
                    MechrevoLite.Hardware.LightingSettingsStore.Load("Keyboard/Ctrl", effect) with { Effect = effect });
            }
        }
        else
        {
            ok = await _backendProbe.SetKeyboardPowerAsync(false);
        }
        StatusText.Text = ok ? (on ? WpfStrings.LightKeyboardSentPrefix + effect : WpfStrings.OriginalStyleWindow_xaml_Text_81) : WpfStrings.LightKeyboardFailed;
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
            snapshot.CpuTempC > 0 ? snapshot.CpuTempC : (hw?.CpuTemp ?? -1),
            snapshot.CpuUsagePercent,
            snapshot.CpuFrequencyMhz,
            snapshot.GpuTempC > 0 ? snapshot.GpuTempC : (hw?.GpuTemp ?? -1),
            snapshot.GpuUsagePercent,
            snapshot.GpuCoreFrequencyMhz,
            hw != null && hw.VramUsedMb > 0 ? hw.VramUsedMb : -1,
            -1, // ⚠️ SensorSnapshot 暂无 VramUsage 字段；保留占位 -1（原静态字段未迁移）
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
