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
    private bool _syncingDisplayEffect;
    private bool _syncingBatteryLimit;
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
            _backendProbe?.Dispose();
            _backendProbe = null;
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
        Close();
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
        ChargeSliderToggle.IsChecked = AppConfig.Is("charge_limit_slider");
        EcDirectOffToggle.IsChecked = string.Equals(AppConfig.GetString("ec_charge_limit"), "0", StringComparison.Ordinal);
        BatteryLimitSlider.Visibility = ChargeSliderToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _syncingBatteryLimit = true;
        BatteryLimitSlider.Value = BatteryControl.ResolveDisplayLimitPercent();
        _syncingBatteryLimit = false;
    }

    private void ChargeSliderToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChargeSliderToggle.IsChecked == true;
        AppConfig.Set("charge_limit_slider", on ? 1 : 0);
        BatteryLimitSlider.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = on ? "已启用无极限充滑块" : "已关闭无极限充滑块";
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

    private void ModeButton_Click(object sender, RoutedEventArgs e)
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
            if (mode >= 0 && _backendProbe != null)
            {
                _ = SwitchModeAsync(mode);
            }
        }
    }

    private static void HighlightSegment(ToggleButton selected, Color? overrideColor = null)
    {
        if (selected.Parent is not UniformGrid grid)
        {
            return;
        }
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        Brush normal = (Brush)Application.Current.Resources["ControlBrush"];
        FontFamily regular = (FontFamily)Application.Current.Resources["AppFontFamily"];
        FontFamily bold = (FontFamily)Application.Current.Resources["AppFontBoldFamily"];
        foreach (object child in grid.Children)
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
        Color color = overrideColor ?? ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        selected.BorderBrush = new SolidColorBrush(color);
        selected.BorderThickness = new Thickness(2);
        selected.Background = normal;
        selected.FontFamily = bold;
        selected.FontWeight = FontWeights.Bold;
    }

    private async Task SwitchModeAsync(int mode)
    {
        bool ok = await _backendProbe!.SwitchModeAsync(mode);
        BackendStatus.Text = ok ? "模式已下发" : "模式下发失败";
        RefreshFromBackend();
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
        if (sender is not ToggleButton button || button.Tag is not string text || _backendProbe == null)
        {
            return;
        }
        foreach (object child in ((UniformGrid)button.Parent).Children)
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
        Refresh180Button.IsChecked = text == "180";
        Refresh60Button.IsChecked = text == "60";
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
        string capacity = string.IsNullOrWhiteSpace(hw.BatteryCapacityText) ? "" : " · " + hw.BatteryCapacityText;
        BatteryInfo.Text = "电池 " + hw.BatteryPercent + "%" + capacity;
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
        SilentModeButton.IsChecked = mode == 2;
        BalancedModeButton.IsChecked = mode == 0;
        TurboModeButton.IsChecked = mode == 1;
        CustomModeButton.IsChecked = mode == 3 || mode == 4;
        ToggleButton[] batteryButtons = { BatteryLongButton, BatteryBalancedButton, BatteryWorkButton };
        int batteryMode = probe.BatteryMode;
        for (int i = 0; i < batteryButtons.Length; i++)
        {
            batteryButtons[i].IsChecked = i == batteryMode;
        }
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
        foreach (object child in ((UniformGrid)selected.Parent).Children)
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

    private async void ConnectBackend_Click(object sender, RoutedEventArgs e)
    {
        await ConnectBackendAsync();
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
        ConnectBackendButton.IsEnabled = false;
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
                BackendLine.Text = "15 秒内未完成；稍后自动重试，也可手动点「连接后端」。";
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
                _backendRetryTimer.Stop();
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
            ConnectBackendButton.IsEnabled = true;
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
        }
        catch
        {
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
        string effect = KeyboardLightSwitch.IsChecked == true ? (item.Tag as string ?? "Single") : "Off";
        bool ok = await _backendProbe.SetKeyboardEffectAsync(effect);
        StatusText.Text = ok ? "键盘灯效已下发：" + effect : "键盘灯效下发失败";
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
        string effect = LogoLightSwitch.IsChecked == true ? (item.Tag as string ?? "Single") : "Off";
        bool ok = await _backendProbe.SetLogoEffectAsync(effect);
        StatusText.Text = ok ? "Logo 灯效已下发：" + effect : "Logo 灯效下发失败";
    }

    internal bool IsDark => _dark;

    internal bool BackendConnected => _backendProbe?.Connected == true;

    internal bool IsBackgroundEnabled => BackgroundToggle.IsChecked == true;

    internal void ApplyThemeFromSettings(bool dark)
    {
        _dark = dark;
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
                _overlayWindow = new OverlayWindow(GetOverlaySnapshot);
                _overlayWindow.Closed += (_, _) => _overlayWindow = null;
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
        MechrevoHw? hw = _backendProbe?.Hw;
        double? power = null;
        return new OverlaySnapshot(
            hw?.CpuTemp ?? -1,
            hw?.GpuTemp ?? -1,
            hw?.CpuFanRpm ?? -1,
            power,
            hw?.BatteryPercent ?? -1);
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
