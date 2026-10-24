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
using MechrevoLite.Hardware;
using System.Windows.Interop;
using System.Windows.Threading;

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
    private TrayIconService? _trayIcon;
    private HotkeyService? _hotkeyService;
    private CustomModeWindow? _customModeForm;
    private MechrevoLite.CustomModeForm? _legacyCustomModeForm;
    private readonly DispatcherTimer _lightingIdleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _lightingIdleSuspended;
    private bool _lightingPowerSuspended;
    private bool _lightingPowerBusy;
    private bool _syncingLiquid;
    private bool _syncingPerfConfig;
    private int _pendingMode = -1;
    private int _modeRequestId;
    private long _pendingModeTick;
    private int _lastDbVisualMode = int.MinValue;
    private string _customWindowModeId = "";
    private bool _perfConfigBuilt;
    private bool _syncingDisplayEffect;
    private bool _syncingBatteryLimit;
    private bool _batteryLimitLoaded;
    private BackendProbe? _backendProbe;
    private readonly DispatcherTimer _backendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _backendRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    private bool _connectAttempting;
    private const string QuickTilesConfigKey = "perf_quick_tiles";
    private const string QuickTilesEmptySentinel = "-";
    private static readonly string[] DefaultQuickTileKeys = { "fanboost", "webcam", "micmute", "winkey", "monitoroff" };
    private static readonly (string Key, string Label, string Glyph)[] QuickTileCatalog =
    {
        ("touchpad", "触摸板", "\uE7C9"), ("touchpadtoggle", "触摸板切换键", "\uE7C9"),
        ("wifi", "WiFi", "\uE701"), ("bt", "蓝牙", "\uE702"),
        ("webcam", "摄像头", "\uE722"), ("micmute", "麦克风", "\uE720"),
        ("numpad", "小键盘锁", "\uE765"), ("winkey", "Win键锁", "\uE765"),
        ("fnkey", "Fn键锁", "\uE765"), ("capslock", "大小写锁定", "\uE8AB"),
        ("osd", "OSD提示", "\uE7F4"), ("copilot", "Copilot键锁", "\uE8B7"),
        ("usb", "关机USB供电", "\uE88E"), ("acrecovery", "来电自启", "\uE7E8"),
        ("highperf", "高性能电源", "\uE945"), ("apuboost", "APU 防缩缸", "\uE9D9"),
        ("fanboost", "强冷", "\uE9CA"), ("deepsleep", "深度睡眠", "\uE708"),
        ("gamewhitelist", "游戏白名单", "\uE7FC"), ("cpuadvperf", "解锁BIOS菜单", "\uE7EF"),
        ("taskbarautohide", "任务栏自动隐藏", "\uE7F4"), ("transparency", "系统透明效果", "\uE790"),
        ("darktheme", "系统深色主题", "\uE793"), ("monitoroff", "息屏", "\uE7F4"),
        ("winupdate", "禁用Win更新", "\uE895")
    };

    public OriginalStyleWindow()
    {
        InitializeComponent();
        MechrevoLite.UI.UiVisualStyle.SetNightMode(_dark);
        MechrevoLite.UI.UiVisualStyle.SetAuditNightMode(_dark);
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFontFamily"];
        InitializeQuickTileDrag();
        SourceInitialized += (_, _) =>
        {
            HideMinMaxButtons();
            ApplyDarkTitleBar(_dark);
            ApplyRoundedCorners();
        };
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        ApplyButtonOpacity();
        _backendTimer.Tick += (_, _) =>
        {
            if (_backendProbe != null)
            {
                RefreshFromBackend();
            }
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
        Closed += (_, _) =>
        {
            _backendTimer.Stop();
            _backendRetryTimer.Stop();
            _lightingIdleTimer.Stop();
            _backendProbe?.Dispose();
            _backendProbe = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            _hotkeyService?.Dispose();
            _hotkeyService = null;
        };
        _trayIcon = new TrayIconService();
        _trayIcon.ShowRequested += ShowFromTray;
        _trayIcon.SettingsRequested += () => new SettingsWindow(this).ShowDialog();
        _trayIcon.OverlayRequested += ToggleTrayOverlay;
        _trayIcon.ExitRequested += () =>
        {
            Close();
            Application.Current.Shutdown();
        };
        _trayIcon.ModeRequested += RequestMode;
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
                StatusText.Text = "全局热键注册失败：" + ex.Message;
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
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
            ApplySavedQuickTiles();
            ApplyMoreSectionState();
            RefreshBrightness();
            InitBatteryControls();
            InitLightingControls();
            NormalizeSegmentDefaults();
            ApplyDarkTitleBar(_dark);
            ApplyRoundedCorners();
            ReloadBackgroundImage();
            _ = Dispatcher.BeginInvoke(new Action(() => _ = ConnectBackendAsync()), System.Windows.Threading.DispatcherPriority.Background);
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

    internal bool SupportsMiniled => _backendProbe?.Hw?.SupportsLocalDimming == true;

    internal bool MiniledOn => _backendProbe?.Hw?.LocalDimming == true;

    internal bool SupportsFhd => Program.acpi?.DeviceGet(327708u) >= 0;

    internal bool FhdOn => Program.acpi?.DeviceGet(327708u) > 0;

    internal bool SupportsHdrControl => Program.acpi?.DeviceGet(327793u) >= 0;

    internal bool HdrControlOn => Program.acpi?.DeviceGet(327793u) > 0;

    internal void ToggleMiniled()
    {
        try
        {
            if (Program.acpi == null)
            {
                return;
            }
            int primary = Program.acpi.DeviceGet(327710u);
            int secondary = Program.acpi.DeviceGet(327726u);
            int value = primary == 1 ? 0 : (primary >= 0 ? 1 : (secondary == 1 ? 2 : (secondary == 2 ? 0 : 1)));
            AppConfig.Set("miniled", value);
            AppConfig.Flush();
            StatusText.Text = "MiniLED 分区控光已切换";
            RefreshFromBackend();
        }
        catch (Exception ex)
        {
            StatusText.Text = "MiniLED 切换失败：" + ex.Message;
        }
    }

    internal void ToggleHdrControl()
    {
        try
        {
            if (Program.acpi == null)
            {
                return;
            }
            int current = Program.acpi.DeviceGet(327793u);
            AppConfig.Set("hdr_control", current == 1 ? 0 : 1);
            AppConfig.Flush();
            StatusText.Text = "HDR Color Control 已切换";
            RefreshFromBackend();
        }
        catch (Exception ex)
        {
            StatusText.Text = "HDR Color Control 切换失败：" + ex.Message;
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
            StatusText.Text = "FHD 切换失败：" + ex.Message;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void ToggleTrayOverlay()
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
            return;
        }
        _overlayWindow = new OverlayWindow(GetOverlaySnapshot);
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();
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
        StatusText.Text = "快捷磁贴顺序已保存";
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
        _syncingBatteryLimit = true;
        BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
        _syncingBatteryLimit = false;
        ApplyBatterySliderStage();
    }

    private void ApplyBatterySliderStage()
    {
        bool on = ChargeSliderToggle.IsChecked == true;
        ToggleButton[] buttons = { BatteryLongButton, BatteryBalancedButton, BatteryWorkButton };
        foreach (ToggleButton button in buttons)
        {
            button.IsEnabled = !on;
            button.Opacity = on ? 0.45 : 1.0;
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
                    StatusText.Text = "已关闭无极限充，充电上限恢复 100%";
                    return;
                }
                StatusText.Text = "关闭无极限充后写回 100% 失败（写入=" + ok + " · 回读=" + readback + "%）";
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
                    StatusText.Text = "已关闭无极限充，GCU 充电上限恢复 100%";
                    return;
                }
                StatusText.Text = "关闭无极限充后 GCU 写回 100% 失败";
                return;
            }
            StatusText.Text = "关闭无极限充：没有可用的 EC/GCU 回写通道";
        }
        catch (Exception ex)
        {
            StatusText.Text = "关闭无极限充失败：" + ex.Message;
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
        ChargeLimitRow.Visibility = visibility;
        ChargeLimitInputRow.Visibility = visibility;
        ChargeLimitStatus.Visibility = visibility;
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
                    ? "限制充电已生效（停充 " + high + "% / 起充 " + low + "%）"
                    : "限制充电未启用";
            }
            else
            {
                ChargeLimitSwitch.IsEnabled = false;
                ChargeLimitApplyButton.IsEnabled = false;
                ChargeLimitLowBox.IsEnabled = false;
                ChargeLimitHighBox.IsEnabled = false;
                ChargeLimitStatus.Text = "读取充电阈值失败：需要 PawnIO 驱动或本机型不支持";
            }
        }
        catch (Exception ex)
        {
            ChargeLimitSwitch.IsEnabled = false;
            ChargeLimitApplyButton.IsEnabled = false;
            ChargeLimitStatus.Text = "读取充电阈值异常：" + ex.Message;
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
                    ? "限制充电已生效（停充 " + high + "% / 起充 " + low + "%）"
                    : "限制充电未启用";
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
                ok = BatteryLimitWriter.TryEnable(low, high, allowInitialDischarge: true, out string detail);
                if (ok)
                {
                    BatteryLimitRestore.SaveConfig(true, low, high);
                    ChargeLimitStatus.Text = "限制充电已启用（停充 " + high + "% / 起充 " + low + "%）";
                }
                else
                {
                    ChargeLimitStatus.Text = "启用失败：" + detail;
                }
            }
            else
            {
                ok = BatteryLimitWriter.TryDisable(out string detail);
                if (ok)
                {
                    BatteryLimitRestore.SaveConfig(false, 0, 0);
                    ChargeLimitStatus.Text = "限制充电已关闭";
                }
                else
                {
                    ChargeLimitStatus.Text = "关闭失败：" + detail;
                }
            }
            if (!ok)
            {
                RefreshBatteryLimitState();
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = "操作异常：" + ex.Message;
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
            bool ok = BatteryLimitWriter.TryEnable(low, high, allowInitialDischarge: true, out string detail);
            if (ok)
            {
                BatteryLimitRestore.SaveConfig(true, low, high);
                ChargeLimitSwitch.IsChecked = true;
                ChargeLimitStatus.Text = "阈值已保存（停充 " + high + "% / 起充 " + low + "%）";
            }
            else
            {
                ChargeLimitStatus.Text = "保存失败：" + detail;
            }
        }
        catch (Exception ex)
        {
            ChargeLimitStatus.Text = "保存异常：" + ex.Message;
        }
    }

    private bool TryReadChargeThreshold(out int low, out int high, out string error)
    {
        low = 60;
        high = 80;
        error = "";
        if (!int.TryParse(ChargeLimitLowBox.Text.Trim(), out low) || low < 1 || low > 99)
        {
            error = "起充阈值需为 1~99";
            return false;
        }
        if (!int.TryParse(ChargeLimitHighBox.Text.Trim(), out high) || high < 2 || high > 100)
        {
            error = "停充阈值需为 2~100";
            return false;
        }
        if (low >= high)
        {
            error = "起充阈值必须小于停充阈值";
            return false;
        }
        return true;
    }

    private async void ChargeSliderToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChargeSliderToggle.IsChecked == true;
        AppConfig.Set("charge_limit_slider", on ? 1 : 0);
        BatteryLimitSlider.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ApplyBatterySliderStage();
        if (on)
        {
            StatusText.Text = "已启用无极限充滑块（三档按钮暂时置灰）";
            return;
        }
        StatusText.Text = "正在关闭无极限充并恢复满充…";
        await WriteBackFullChargeAfterSliderOffAsync();
        _syncingBatteryLimit = true;
        BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
        _syncingBatteryLimit = false;
    }

    private void EcDirectOffToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = EcDirectOffToggle.IsChecked == true;
        if (on)
        {
            AppConfig.Set("ec_charge_limit", "0");
        }
        else
        {
            AppConfig.Remove("ec_charge_limit");
        }
        StatusText.Text = on ? "已关闭 EC 直写充电上限" : "EC 直写恢复自动判定";
    }


    private void BatteryLimitSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingBatteryLimit || BatteryLimitSlider == null || !IsLoaded)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        if (!BatteryControl.CaptureChargeLimitGesture() || !BatteryControl.ApplyChargeLimitFromUserGesture(value))
        {
            StatusText.Text = "充电上限写入未确认";
            _syncingBatteryLimit = true;
            BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
            _syncingBatteryLimit = false;
            return;
        }
        BatteryControl.SetSelectedModePercent(value);
        StatusText.Text = "充电上限：" + value + "%";
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
            ScreenBrightness.Set(value);
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
        if (width > 100)
        {
            ContentPanel.Width = width;
        }
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
            StatusText.Text = "性能模式：" + (selected.Content as TextBlock)?.Text;
            int mode = selected == SilentModeButton ? 2
                : selected == BalancedModeButton ? 0
                : selected == TurboModeButton ? 1
                : selected == CustomModeButton ? 3
                : -1;
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
                _ = SwitchModeAsync(mode);
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
        if (_customModeForm == null || !_customModeForm.IsVisible)
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
        _customModeForm.BindMode(id);
    }

    private void SyncGpuDbForMode(int mode, string reason)
    {
        try
        {
            if (mode < 0 || _backendProbe?.Hw == null || !_backendProbe.Hw.IsGcuConnected)
            {
                return;
            }
            Program.hw = _backendProbe.Hw;
            Program.service = _backendProbe.Service!;
            if (!MechrevoLite.Hardware.GpuDbControl.Supported())
            {
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

    private async Task SwitchModeAsync(int mode)
    {
        int request = ++_modeRequestId;
        _pendingMode = mode;
        _pendingModeTick = Environment.TickCount64;
        MechrevoService.ModeSwitchResult result = await _backendProbe!.SwitchModeResultAsync(mode);
        if (request != _modeRequestId)
        {
            return;
        }
        if (result == MechrevoService.ModeSwitchResult.Superseded)
        {
            return;
        }
        bool ok = result == MechrevoService.ModeSwitchResult.Ok;
        BackendStatus.Text = ok ? "模式已下发" : "模式下发失败";
        if (!ok)
        {
            _pendingMode = -1;
        }
        if (ok)
        {
            SyncGpuDbForMode(mode, "WPF 切档");
            SyncCustomWindowMode(mode);
        }
        RefreshFromBackend();
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

    private async void GpuMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        int mode = int.Parse(text);
        if (!_backendProbe.CanSwitchGpuMode(mode))
        {
            StatusText.Text = "当前机型不支持该显卡模式";
            RefreshFromBackend();
            return;
        }
        if (_backendProbe.IsBaolongda)
        {
            if (mode == 0)
            {
                StatusText.Text = "宝龙达不支持核显模式";
                RefreshFromBackend();
                return;
            }
            if (_backendProbe.CurrentGpuMode == mode)
            {
                _backendProbe.ClearGpuRestartPending();
                RefreshFromBackend();
                return;
            }
            bool bdOk = _backendProbe.SwitchGpuModeBaolongda(mode, out bool confirmed);
            if (bdOk)
            {
                _backendProbe.MarkGpuRestartPending(mode);
                StatusText.Text = confirmed ? "已切到目标模式，重启后生效" : "已请求切换，回读未确认，重启后确认";
            }
            else
            {
                StatusText.Text = "宝龙达显卡模式写入失败";
            }
            RefreshFromBackend();
            return;
        }
        string modeName = button.Content?.ToString() ?? ("模式 " + mode);
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
                bool restartOk = await _backendProbe.RequestGpuModeRestartAsync(mode);
                if (restartOk)
                {
                    _backendProbe.MarkGpuRestartPending(mode);
                    StatusText.Text = "已请求切换并重启，重启后生效";
                }
                else
                {
                    StatusText.Text = "重启切换请求失败";
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
        bool ok = await _backendProbe.SwitchGpuModeAsync(mode);
        await _backendProbe.RefreshGpuModeAsync();
        int current = _backendProbe.CurrentGpuMode;
        if (ok && current != mode)
        {
            _backendProbe.MarkGpuRestartPending(mode);
        }
        else if (ok)
        {
            _backendProbe.ClearGpuRestartPending();
        }
        StatusText.Text = ok ? "显卡模式已下发，重启后生效" : "显卡模式下发失败";
        RefreshFromBackend();
    }

    private async void BatteryMode_Click(object sender, RoutedEventArgs e)
    {
        if (ChargeSliderToggle.IsChecked == true)
        {
            StatusText.Text = "无极限充开启时三档按钮不可用";
            return;
        }
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
        bool ok = await _backendProbe.SetBatteryModeAsync(int.Parse(text));
        StatusText.Text = ok ? "电池档位已下发" : "电池档位下发失败";
    }

    private async void RefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        AutoRefreshSwitch.IsChecked = false;
        AppConfig.Set("screen_auto", 0);
        ScreenControl.SetAutoRefresh(0);
        int requested = int.Parse(text);
        Refresh180Button.IsChecked = requested >= _refreshHighHz;
        Refresh60Button.IsChecked = requested < _refreshHighHz;
        HighlightSegment(button);
        bool ok = await _backendProbe.SwitchRefreshRateAsync(int.Parse(text));
        StatusText.Text = ok ? "刷新率已下发" : "刷新率下发失败";
    }

    private async void AutoRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = AutoRefreshSwitch.IsChecked == true;
        bool ok = await _backendProbe.SwitchAutoRefreshAsync(on);
        AppConfig.Set("screen_auto", on ? 1 : 0);
        AppConfig.Flush();
        SyncRefreshRateState();
        StatusText.Text = ok ? "自动刷新率已下发" : "自动刷新率下发失败";
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
        StatusText.Text = ok ? "快捷开关已下发：" + key : "快捷开关下发失败：" + key;
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
            choices.Add(new QuickChoice(key, label, glyph, HasQuickTile(key)));
        }
        choices = choices
            .OrderBy(c => currentOrder.IndexOf(c.Key) is int i && i >= 0 ? i : int.MaxValue)
            .ToList();
        if (choices.Count == 0)
        {
            StatusText.Text = "没有可用的快捷开关";
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
            StatusText.Text = "快捷磁贴已更新";
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
                    selected.Add(new QuickChoice(catalogKey, label, glyph, true));
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
            ("输入设备", new (string, string)[]
            {
                ("touchpad", "触摸板"), ("wifi", "WiFi"), ("bt", "蓝牙"), ("webcam", "摄像头"),
                ("micmute", "麦克风静音"), ("numpad", "小键盘锁"), ("touchpadtoggle", "触摸板切换键")
            }),
            ("键盘与热键", new (string, string)[]
            {
                ("winkey", "Win键锁"), ("fnkey", "Fn键锁"), ("capslock", "大小写锁定"),
                ("osd", "OSD提示"), ("copilot", "Copilot键锁")
            }),
            ("电源与系统", new (string, string)[]
            {
                ("usb", "关机USB供电"), ("acrecovery", "来电自启"), ("highperf", "高性能电源"),
                ("apuboost", "APU 防缩缸"), ("fanboost", "风扇强冷"), ("deepsleep", "深度睡眠"),
                ("gamewhitelist", "游戏白名单"), ("cpuadvperf", "解锁BIOS菜单"),
                ("taskbarautohide", "任务栏自动隐藏"), ("transparency", "系统透明效果"),
                ("darktheme", "系统深色主题"), ("monitoroff", "息屏（不睡眠）"),
                ("winupdate", "禁用Win更新")
            }),
            ("睡眠省电", new (string, string)[]
            {
                ("sleepnetoff", "待机断开网络"), ("sleepaway", "封杀离开模式"),
                ("sleepsysreq", "忽略系统策略"), ("sleeprtcwake", "禁用RTC唤醒"),
                ("sleepdeep", "ACPI深度睡眠"), ("sleephibernate", "30分钟转休眠")
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
                    Content = label,
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
            if (title == "睡眠省电")
            {
                Grid headerRow = new Grid { Margin = new Thickness(0, 8, 0, 2) };
                headerRow.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("MutedText"), FontSize = 10 });
                StackPanel scope = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                ToggleButton ac = new ToggleButton { Content = "通电时", Style = (Style)FindResource("SmallSegment"), IsChecked = true, Tag = "ac", Margin = new Thickness(0, 0, 4, 0), Height = 24, FontSize = 11 };
                ToggleButton dc = new ToggleButton { Content = "离电时", Style = (Style)FindResource("SmallSegment"), Tag = "dc", Height = 24, FontSize = 11 };
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
                    Text = title,
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
        StatusText.Text = "睡眠省电范围：" + selected.Content;
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
            StatusText.Text = ok ? "已切换：" + key : "切换失败：" + key;
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
        MoreSwitchCount.Text = on + "/" + total + " 开启";
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

    private void RefreshFromBackend()
    {
        BackendProbe? probe = _backendProbe;
        if (probe == null)
        {
            return;
        }
        BackendLine.Text = probe.Snapshot();
        if (probe.Hw is not MechrevoHw hw)
        {
            return;
        }
        CpuStat.Text = SensorLine(hw, cpu: true);
        GpuStat.Text = SensorLine(hw, cpu: false);
        string health = MechrevoLite.SettingsForm.BatteryHealthText(hw);
        string abnormal = hw.BatteryAbnormal ? " · 电池异常" : "";
        string powerState = "";
        try
        {
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online)
            {
                powerState = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging) ? " · 充电中" : " · 外接电源";
            }
            else
            {
                powerState = " · 电池供电";
            }
        }
        catch
        {
        }
        BatteryInfo.Text = "电池 " + hw.BatteryPercent + "%" + powerState + (string.IsNullOrWhiteSpace(health) ? "" : " · " + health) + abnormal;
        BatteryInfo.ToolTip = health;
        int mode = probe.CurrentMode;
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
        bool skipModeUi = false;
        if (_pendingMode >= 0)
        {
            if (mode == _pendingMode || Environment.TickCount64 - _pendingModeTick > 4000)
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
            TurboModeButton.IsChecked = mode == 1;
            CustomModeButton.IsChecked = mode == 3 || mode == 4;
            RefreshModeHighlights();
        }
        _trayIcon?.SetMode(mode);
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
        UpdateBackgroundVisibility();
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
            StatusText.Text = "GCU 状态检查失败：" + ex.Message;
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
        SetBackendStatus("● GCU 连接中", "#F59E0B", "正在连接 GCU 服务（127.0.0.1:13688）——服务通常在登录后数秒内就绪，应用会自动重试");
        BackendLine.Text = "正在创建 MechrevoHw 并 ConnectAsync（最长 15 秒）…";
        BackendProbe probe = new BackendProbe();
        _backendProbe = probe;
        try
        {
            Task<bool> connect = probe.ConnectAsync();
            Task completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completed != connect)
            {
                SetBackendStatus("● GCU 未连接", "#EF4444", "GCU 服务未运行或连接失败（127.0.0.1:13688）；应用正在自动重连，恢复后此指示会变绿");
                BackendLine.Text = "15 秒内未完成；应用会自动重试，点上方状态可重新检查。";
                probe.Dispose();
                _backendProbe = null;
                _backendRetryTimer.Start();
                return;
            }
            if (await connect)
            {
                UpdateBackendStatus();
                BackendLine.Text = probe.Snapshot();
                Program.hw = probe.Hw;
                Program.service = probe.Service!;
                try
                {
                    if (MechrevoLite.Mode.PerfModeService.Instance == null)
                    {
                        MechrevoLite.Mode.PerfModeService.Create(probe.Service!, probe.Hw);
                    }
                }
                catch
                {
                }
                _backendRetryTimer.Stop();
                SyncGpuDbForMode(probe.CurrentMode, "WPF 启动同步");
                await probe.RefreshGpuModeAsync();
                ApplyCapabilities();
                _backendTimer.Start();
            }
            else
            {
                SetBackendStatus("● GCU 未连接", "#EF4444", "GCU 服务未运行或连接失败（127.0.0.1:13688）；应用正在自动重连，恢复后此指示会变绿");
                BackendLine.Text = probe.LastError;
                probe.Dispose();
                _backendProbe = null;
                _backendRetryTimer.Start();
            }
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
        if (probe.IsNonGcuBackend)
        {
            bool ready = probe.Connected;
            SetBackendStatus(
                ready ? "● 已就绪" : "● 未就绪",
                ready ? "#4ADE80" : "#F59E0B",
                "本机使用老机型接口，仅支持三个内置性能档");
            return;
        }
        if (probe.IsGcuConnected)
        {
            SetBackendStatus("● GCU 已连接", "#4ADE80", "GCU 服务连接正常（GCUBridge，MQTT 127.0.0.1:13688）");
            return;
        }
        if (probe.IsReconnecting || probe.IsSessionAwaitingSubscribe)
        {
            SetBackendStatus("● GCU 连接中", "#F59E0B", "正在连接 GCU 服务（127.0.0.1:13688）——服务通常在登录后数秒内就绪，应用会自动重试");
            return;
        }
        SetBackendStatus("● GCU 未连接", "#EF4444", "GCU 服务未运行或连接失败（127.0.0.1:13688）；应用正在自动重连，恢复后此指示会变绿");
    }

    private void SetBackendStatus(string text, string hex, string tooltip)
    {
        BackendStatus.Text = text;
        BackendStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        BackendStatus.ToolTip = tooltip;
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
            bool customAllowed = (caps.CpuPerformanceTuning || caps.FanSettings) && !(_backendProbe?.Hw?.SupportsModeProfileConfig ?? false);
            CustomModeButton.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
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
        bool gen30 = hw?.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30;
        RefreshPerfConfigVisibility(_backendProbe?.CurrentMode ?? -1);
        ChargeSliderToggle.Visibility = gen30 ? Visibility.Collapsed : Visibility.Visible;
        EcDirectOffToggle.Visibility = gen30 ? Visibility.Collapsed : Visibility.Visible;
        if (gen30)
        {
            BatteryLimitSlider.Visibility = Visibility.Collapsed;
        }
        bool liquidSupported = (caps?.LiquidCooling ?? false) || (hw?.SupportsLiquidCooling ?? false);
        LiquidSection.Visibility = (AppConfig.Is("force_liquid_cooling_menu") || liquidSupported) ? Visibility.Visible : Visibility.Collapsed;
        RefreshLiquidCoolingUi();
        LightingSection.Visibility = (KeyboardLightRow.Visibility == Visibility.Visible
            || LightStripRow.Visibility == Visibility.Visible
            || LogoLightRow.Visibility == Visibility.Visible
            || LightHingeRow.Visibility == Visibility.Visible
            || LightSyncRow.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
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

    private void RefreshLiquidCoolingUi()
    {
        try
        {
            MechrevoHw? hw = _backendProbe?.Hw;
            bool connected = hw?.LcReportedConnected == true;
            bool supported = hw?.SupportsLiquidCooling == true;
            if (!supported)
            {
                LiquidStatusButton.Content = "未连接";
                LiquidPumpCombo.IsEnabled = false;
                LiquidFanCombo.IsEnabled = false;
                return;
            }
            LiquidStatusButton.Content = connected ? "水冷已连接" : "水冷未连接";
            LiquidPumpCombo.IsEnabled = true;
            LiquidFanCombo.IsEnabled = true;
            _syncingLiquid = true;
            SelectComboByTag(LiquidPumpCombo, hw!.LcPumpControl);
            SelectComboByTag(LiquidFanCombo, hw.LcFanControl);
            _syncingLiquid = false;
        }
        catch
        {
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
        if (_backendProbe?.Service == null)
        {
            StatusText.Text = "后端未连接";
            return;
        }
        try
        {
            LiquidStatusButton.IsEnabled = false;
            await _backendProbe.Service.RefreshLiquidCoolingStatus();
            RefreshLiquidCoolingUi();
            StatusText.Text = "水冷状态已刷新";
        }
        catch (Exception ex)
        {
            StatusText.Text = "刷新水冷状态失败：" + ex.Message;
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
        StatusText.Text = ok ? "水泵档位已下发" : "水泵档位下发失败";
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
        StatusText.Text = ok ? "水冷风扇档位已下发" : "水冷风扇档位下发失败";
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
                combo.Items.Add(new ComboBoxItem { Content = "配置 " + (i + 1), Tag = i.ToString() });
            }
        }
        _syncingPerfConfig = false;
    }

    private void RefreshPerfConfigVisibility(int currentMode)
    {
        MechrevoHw? hw = _backendProbe?.Hw;
        bool profileConfig = hw?.SupportsModeProfileConfig == true;
        bool show = profileConfig && currentMode >= 0 && currentMode <= 2;
        PerfConfigRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PerfConfigEditButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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
        StatusText.Text = "配置已保存：" + combo.SelectedIndex + "（模式 " + visualMode + "）";
    }

    private void PerfConfigEdit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_backendProbe == null)
            {
                StatusText.Text = "后端未连接，无法打开配置编辑";
                return;
            }
            Program.hw = _backendProbe.Hw;
            Program.service = _backendProbe.Service!;
            int visualMode = _backendProbe.Service?.CurrentMode ?? 0;
            if (_legacyCustomModeForm == null || _legacyCustomModeForm.IsDisposed)
            {
                _legacyCustomModeForm = new CustomModeForm();
            }
            _legacyCustomModeForm.BindFirmwareProfile(visualMode, MechrevoLite.Mode.ModeProfileConfig.Get(visualMode));
            if (!_legacyCustomModeForm.Visible)
            {
                _legacyCustomModeForm.Show();
            }
            _legacyCustomModeForm.Activate();
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开配置编辑失败：" + ex.Message;
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
                StatusText.Text = "后端未连接，无法打开自定义模式";
                return;
            }
            Program.hw = _backendProbe.Hw;
            Program.service = _backendProbe.Service!;
            string customId = ResolveCustomModeId();
            if (_customModeForm == null)
            {
                _customModeForm = new CustomModeWindow(customId) { Owner = this };
                _customModeForm.SetDark(_dark);
                _customModeForm.Closed += (_, _) => _customModeForm = null;
            }
            if (!_customModeForm.IsVisible)
            {
                _customModeForm.Show();
            }
            _customModeForm.Activate();
            _customWindowModeId = customId;
            if (switchMode)
            {
                if (!await _customModeForm.SelectModeAsync(customId))
                {
                    _customModeForm.BindMode(MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId);
                }
            }
            else
            {
                _customModeForm.BindMode(customId);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开自定义模式失败：" + ex.Message;
        }
    }

    private async void GpuOverclock_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = GpuOverclockSwitch.IsChecked == true;
        bool ok = await _backendProbe.ApplyGpuOverclockAsync(on);
        if (!ok)
        {
            GpuOverclockSwitch.IsChecked = !on;
        }
        StatusText.Text = ok ? "显卡超频：" + (on ? "开" : "关") : "显卡超频下发失败";
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
            CalibrationCombo.Items.Add(new ComboBoxItem { Content = "默认", Tag = 1 });
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
                CalibrationCombo.Items.Add(new ComboBoxItem { Content = "自定义校色", Tag = 999 });
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
            string[] labels = { "多彩", "上网", "视频", "低蓝光", "影院", "照片" };
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
        StatusText.Text = "屏幕校色下发中…";
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
            StatusText.Text = on ? "屏幕校色已开启" : "屏幕校色已关闭";
        }
        else
        {
            _syncingCalibration = true;
            CalibrationSwitch.IsChecked = !on;
            _syncingCalibration = false;
            StatusText.Text = mode == 999 ? "自定义 ICC 文件缺失或应用失败" : "屏幕校色下发失败";
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
            StatusText.Text = "屏幕校色档位：" + (CalibrationCombo.SelectedItem as ComboBoxItem)?.Content;
        }
        else
        {
            StatusText.Text = mode == 999 ? "自定义 ICC 文件缺失或应用失败" : "屏幕校色档位下发失败";
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
        StatusText.Text = "色彩增强下发中…";
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
            StatusText.Text = on ? "色彩增强已开启" : "色彩增强已关闭";
        }
        else
        {
            _syncingDisplayEffect = true;
            DisplayEffectSwitch.IsChecked = !on;
            _syncingDisplayEffect = false;
            StatusText.Text = "色彩增强下发失败";
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
            StatusText.Text = "色彩增强档位：" + (DisplayEffectCombo.SelectedItem as ComboBoxItem)?.Content;
        }
        else
        {
            StatusText.Text = "色彩增强档位下发失败";
        }
    }

    private void DisplayEffectEdit_Click(object sender, RoutedEventArgs e)
    {
        BackendProbe? probe = _backendProbe;
        if (probe?.Service == null)
        {
            StatusText.Text = "后端未连接，无法编辑色彩增强";
            return;
        }
        DisplayEffectEditorWindow dialog = new DisplayEffectEditorWindow(SelectedDisplayEffectMode()) { Owner = this };
        dialog.ShowDialog();
        _ = probe.RequestDisplayEffectStatusAsync();
        InitScreenColorControls();
    }

    private void InitLightingControls()
    {
        LightOffOnBatteryCheck.IsChecked = AppConfig.Is("lighting_off_on_battery");
        int seconds = AppConfig.Get("lighting_idle_seconds", 0);
        foreach (object item in LightIdleCombo.Items)
        {
            if (item is ComboBoxItem comboItem && int.TryParse(comboItem.Tag as string, out int value) && value == seconds)
            {
                LightIdleCombo.SelectedItem = comboItem;
                return;
            }
        }
        LightIdleCombo.SelectedIndex = 0;
    }

    private void LightIdleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LightIdleCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag as string, out int seconds))
        {
            AppConfig.Set("lighting_idle_seconds", seconds);
            AppConfig.Flush();
            StatusText.Text = seconds <= 0 ? "灯光自动关闭：关" : "灯光自动关闭：" + (seconds / 60) + " 分钟";
        }
    }

    private void LightOffOnBattery_Click(object sender, RoutedEventArgs e)
    {
        bool on = LightOffOnBatteryCheck.IsChecked == true;
        AppConfig.Set("lighting_off_on_battery", on ? 1 : 0);
        AppConfig.Flush();
        StatusText.Text = on ? "离电自动关闭灯效：开" : "离电自动关闭灯效：关";
    }

    private void KeyboardLightEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = "后端未连接，无法编辑键盘灯光";
            return;
        }
        MechrevoLite.Program.hw = _backendProbe.Hw;
        MechrevoLite.Program.service = _backendProbe.Service!;
        KeyboardRgb? rgb = EnsureKeyboardRgb();
        if (rgb == null)
        {
            StatusText.Text = "键盘 RGB 设备未连接，无法打开键盘灯光编辑";
            return;
        }
        new RgbForm(rgb).ShowDialog();
    }

    private void LogoLightEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = "后端未连接，无法编辑 Logo 灯光";
            return;
        }
        MechrevoLite.Program.hw = _backendProbe.Hw;
        MechrevoLite.Program.service = _backendProbe.Service!;
        new MechrevoLite.LightForm("HidLightbar_Logo/Ctrl", "Logo 灯光", MechrevoLite.LightForm.CurrentLogoEffects()).ShowDialog();
    }

    private void CheckLightingIdle()
    {
        _ = ReconcileLightingPowerAsync();
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
                _ = ApplyKeyboardLightAsync();
                _ = ApplyLogoLightAsync();
            }
            return;
        }
        double idleSeconds = NativeMethods.GetIdleTime().TotalSeconds;
        if (!_lightingIdleSuspended && idleSeconds >= timeoutSeconds)
        {
            _lightingIdleSuspended = true;
            _ = probe.SetKeyboardEffectAsync("Off");
            _ = probe.SetLogoEffectAsync("Off");
            return;
        }
        if (_lightingIdleSuspended && idleSeconds < Math.Max(5, timeoutSeconds / 2))
        {
            _lightingIdleSuspended = false;
            _ = ApplyKeyboardLightAsync();
            _ = ApplyLogoLightAsync();
        }
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
        await _backendProbe.SetKeyboardPowerAsync(false);
        await _backendProbe.SetLightPowerAsync("HidLightbar/Ctrl", false);
        await _backendProbe.SetLightPowerAsync("HidLightbar_Logo/Ctrl", false);
        await _backendProbe.SetLightPowerAsync("HidLightbar_Hinge/Ctrl", false);
        await _backendProbe.SetLightPowerAsync("HidLightbar_Sync/Ctrl", false);
    }

    private async Task ApplyLightStripLightAsync()
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = LightStripSwitch.IsChecked == true;
        string effect = (LightStripEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        if (on)
        {
            await _backendProbe.SetLightbarEffectAsync(effect);
        }
        else
        {
            await _backendProbe.SetLightPowerAsync("HidLightbar/Ctrl", false);
        }
    }

    private async Task ApplyHingeLightAsync()
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = LightHingeSwitch.IsChecked == true;
        string effect = (LightHingeEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        if (on)
        {
            await _backendProbe.SetHingeEffectAsync(effect);
        }
        else
        {
            await _backendProbe.SetLightPowerAsync("HidLightbar_Hinge/Ctrl", false);
        }
    }

    private async Task ApplySyncLightAsync()
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = LightSyncSwitch.IsChecked == true;
        string effect = (LightSyncEffectCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Single";
        if (on)
        {
            await _backendProbe.SetSyncEffectAsync(effect);
        }
        else
        {
            await _backendProbe.SetLightPowerAsync("HidLightbar_Sync/Ctrl", false);
        }
    }

    private async void LightStripSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        await ApplyLightStripLightAsync();
        StatusText.Text = "灯带状态已下发";
    }

    private async void LightStripEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_backendProbe == null || LightStripSwitch.IsChecked != true || LightStripEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string effect = item.Tag as string ?? "Single";
        bool ok = await _backendProbe.SetLightbarEffectAsync(effect);
        StatusText.Text = ok ? "灯带效果已下发：" + effect : "灯带效果下发失败";
    }

    private void LightStripEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = "后端未连接，无法编辑灯带";
            return;
        }
        Program.hw = _backendProbe.Hw;
        Program.service = _backendProbe.Service!;
        new LightForm("HidLightbar/Ctrl", "灯带灯光").ShowDialog();
    }

    private KeyboardRgb? EnsureKeyboardRgb()
    {
        if (Program.rgb != null)
        {
            return Program.rgb;
        }
        try
        {
            KeyboardRgb rgb = new();
            rgb.LoadConfig();
            rgb.Connect();
            Program.rgb = rgb;
            return rgb;
        }
        catch (Exception ex)
        {
            StatusText.Text = "键盘 RGB 初始化失败：" + ex.Message;
            return null;
        }
    }

    private async void LightHingeSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        await ApplyHingeLightAsync();
        StatusText.Text = "铰链灯状态已下发";
    }

    private async void LightHingeEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_backendProbe == null || LightHingeSwitch.IsChecked != true || LightHingeEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        bool ok = await _backendProbe.SetHingeEffectAsync(item.Tag as string ?? "Single");
        StatusText.Text = ok ? "铰链灯效果已下发" : "铰链灯效果下发失败";
    }

    private void LightHingeEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = "后端未连接，无法编辑铰链灯";
            return;
        }
        Program.hw = _backendProbe.Hw;
        Program.service = _backendProbe.Service!;
        new LightForm("HidLightbar_Hinge/Ctrl", "铰链灯光", LightForm.HingeEffects).ShowDialog();
    }

    private async void LightSyncSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        await ApplySyncLightAsync();
        StatusText.Text = "同步灯状态已下发";
    }

    private async void LightSyncEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_backendProbe == null || LightSyncSwitch.IsChecked != true || LightSyncEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        bool ok = await _backendProbe.SetSyncEffectAsync(item.Tag as string ?? "Single");
        StatusText.Text = ok ? "同步灯效果已下发" : "同步灯效果下发失败";
    }

    private void LightSyncEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            StatusText.Text = "后端未连接，无法编辑同步灯";
            return;
        }
        Program.hw = _backendProbe.Hw;
        Program.service = _backendProbe.Service!;
        new LightForm("HidLightbar_Sync/Ctrl", "同步灯光", LightForm.SyncEffects).ShowDialog();
    }

    private async void KeyboardLightSwitch_Click(object sender, RoutedEventArgs e)
    {
        await ApplyKeyboardLightAsync();
    }

    private async void KeyboardEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyboardLightSwitch.IsChecked == true)
        {
            await ApplyKeyboardLightAsync();
        }
    }

    private async Task ApplyKeyboardLightAsync()
    {
        if (_backendProbe == null || KeyboardEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        bool on = KeyboardLightSwitch.IsChecked == true;
        string effect = item.Tag as string ?? "Single";
        bool ok = on
            ? await _backendProbe.SetKeyboardEffectAsync(effect)
            : await _backendProbe.SetKeyboardPowerAsync(false);
        StatusText.Text = ok ? (on ? "键盘灯效已下发：" + effect : "键盘灯光已关闭") : "键盘灯效下发失败";
    }

    private async void LogoLightSwitch_Click(object sender, RoutedEventArgs e)
    {
        await ApplyLogoLightAsync();
    }

    private async void LogoEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogoLightSwitch.IsChecked == true)
        {
            await ApplyLogoLightAsync();
        }
    }

    private async Task ApplyLogoLightAsync()
    {
        if (_backendProbe == null || LogoEffectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        bool on = LogoLightSwitch.IsChecked == true;
        string effect = item.Tag as string ?? "Single";
        bool ok = on
            ? await _backendProbe.SetLogoEffectAsync(effect)
            : await _backendProbe.SetLightPowerAsync("HidLightbar_Logo/Ctrl", false);
        StatusText.Text = ok ? (on ? "Logo 灯效已下发：" + effect : "Logo 灯已关闭") : "Logo 灯效下发失败";
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

    internal void ApplyThemeFromSettings(bool dark)
    {
        _dark = dark;
        MechrevoLite.UI.UiVisualStyle.SetNightMode(_dark);
        MechrevoLite.UI.UiVisualStyle.SetAuditNightMode(_dark);
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        ApplyButtonOpacity();
        ThemeButton.Content = _dark ? "浅色" : "深色";
        UpdateBackgroundVisibility();
        RefreshSegmentHighlights();
        StatusText.Text = "0.293.5 / WPF P0 · " + (_dark ? "深色" : "浅色");
    }

    internal void SetBackgroundFromSettings(bool on)
    {
        BackgroundToggle.IsChecked = on;
        UpdateBackgroundVisibility();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        MechrevoLite.UI.UiVisualStyle.SetNightMode(_dark);
        MechrevoLite.UI.UiVisualStyle.SetAuditNightMode(_dark);
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        UpdateTitleIcon();
        ApplyDockOpacity();
        ApplyButtonOpacity();
        ThemeButton.Content = _dark ? "浅色" : "深色";
        UpdateBackgroundVisibility();
        RefreshSegmentHighlights();
        StatusText.Text = "0.293.5 / WPF P0 · " + (_dark ? "深色" : "浅色");
    }

    private void WideButton_Click(object sender, RoutedEventArgs e)
    {
        new MainWindow().Show();
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
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow = null;
                    StatusText.Text = "悬浮窗已关闭";
                    return;
                }
                try
                {
                    HardwareControl.EnableLocalMonitoring();
                }
                catch
                {
                }
                _overlayWindow = new OverlayWindow(GetOverlaySnapshot);
                _overlayWindow.Closed += (_, _) =>
                {
                    _overlayWindow = null;
                    try
                    {
                        HardwareControl.DisableLocalMonitoring();
                    }
                    catch
                    {
                    }
                };
                _overlayWindow.Show();
                StatusText.Text = "悬浮窗已打开";
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
            HardwareControl.ReadSensorsOverlay();
        }
        catch
        {
        }
        MechrevoHw? hw = _backendProbe?.Hw;
        int battery = hw?.BatteryPercent ?? -1;
        try
        {
            double pct = HardwareControl.GetBatteryChargePercentage();
            if (pct >= 0)
            {
                battery = (int)Math.Round(pct);
            }
        }
        catch
        {
        }
        return new OverlaySnapshot(
            HardwareControl.cpuTemp > 0 ? HardwareControl.cpuTemp : (hw?.CpuTemp ?? -1),
            HardwareControl.cpuUsage ?? hw?.CpuUsage ?? -1,
            HardwareControl.cpuFrequency > 0 ? HardwareControl.cpuFrequency : (hw?.CpuFrequency ?? -1),
            HardwareControl.gpuTemp > 0 ? HardwareControl.gpuTemp : (hw?.GpuTemp ?? -1),
            HardwareControl.gpuUsage ?? hw?.GpuUsage ?? -1,
            HardwareControl.gpuCoreFreq > 0 ? HardwareControl.gpuCoreFreq : (hw?.GpuCoreFreq ?? -1),
            HardwareControl.cpuFanRPM > 0 ? HardwareControl.cpuFanRPM : (hw?.CpuFanRpm ?? -1),
            HardwareControl.gpuFanRPM > 0 ? HardwareControl.gpuFanRPM : (hw?.GpuFanRpm ?? -1),
            HardwareControl.ramUsage ?? hw?.RamUsage ?? -1,
            HardwareControl.ramUsedMb.HasValue ? HardwareControl.ramUsedMb.Value / 1024.0 : (hw?.RamUsedGb ?? -1),
            battery,
            HardwareControl.cpuPower ?? 0f,
            HardwareControl.gpuPower ?? 0f);
    }

    private async Task ExportDiagnosticPackAsync()
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "导出诊断包",
            Filter = "Zip 压缩包 (*.zip)|*.zip",
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = DiagnosticPackExporter.BuildFileName(Program.ReleaseVersion, DateTime.Now)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        StatusText.Text = "正在导出诊断包…";
        try
        {
            DiagnosticPackInputs inputs = DiagnosticPackCapture.Capture();
            DiagnosticPackResult result = await DiagnosticPackRunner.ExportAsync(dialog.FileName, inputs);
            StatusText.Text = "诊断包已导出：" + System.IO.Path.GetFileName(result.ZipPath);
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
            StatusText.Text = "诊断包导出失败：" + ex.Message;
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

    internal void ApplyButtonOpacity()
    {
        int opacity = Math.Clamp(AppConfig.Get("ui_button_opacity", 100), 0, 100);
        Color normal = _dark
            ? Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x26, 0x00, 0x00, 0x00);
        Color hover = _dark
            ? Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
        Application.Current.Resources["ControlBrush"] =
            new SolidColorBrush(Color.FromArgb((byte)(normal.A * opacity / 100), normal.R, normal.G, normal.B));
        Application.Current.Resources["ControlHoverBrush"] =
            new SolidColorBrush(Color.FromArgb((byte)(hover.A * opacity / 100), hover.R, hover.G, hover.B));
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
        TitleIconBorder.Background = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(17, 17, 17));
        TitleIconText.Foreground = _dark ? Brushes.Black : Brushes.White;
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
