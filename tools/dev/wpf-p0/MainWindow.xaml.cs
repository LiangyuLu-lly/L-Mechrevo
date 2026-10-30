using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Display;
using L_Mechrevo.WpfP0.Services;
using L_Mechrevo.WpfP0.Resources;
using Microsoft.Win32;

namespace L_Mechrevo.WpfP0;

public partial class MainWindow : Window
{
    private readonly OriginalStyleWindow? _owner;
    private BackendProbe? _probe;
    private readonly DispatcherTimer _sensorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _dark = true;
    private bool _navCollapsed;
    private readonly List<(RoundedBar Bar, TextBlock Value, string Unit)> _cpuMetrics = new();
    private readonly List<(RoundedBar Bar, TextBlock Value, string Unit)> _gpuMetrics = new();
    private RingGauge? _cpuFanRing;
    private RingGauge? _gpuFanRing;
    private TextBlock? _cpuDutyText;
    private TextBlock? _gpuDutyText;
    private TextBlock? _batteryInfoText;
    private TextBlock? _chargeLimitText;
    private TextBlock? _displayBrightnessText;
    private Slider? _displayBrightness;
    private CheckBox? _autoRefresh;
    private readonly List<ToggleButton> _calibrationOptionButtons = new();
    private readonly List<ToggleButton> _effectOptionButtons = new();
    private readonly List<ToggleButton> _lightingIdleButtons = new();
    private readonly Dictionary<string, Slider> _effectSliders = new();
    private Border? _liquidCard;
    private Button? _liquidStatusButton;
    private ComboBox? _liquidPumpCombo;
    private ComboBox? _liquidFanCombo;
    private WaterCoolerCurveCanvas? _liquidCurveCanvas;
    private Slider? _liquidPumpStartSlider;
    private Slider? _liquidPumpTurboSlider;
    private TextBlock? _liquidPumpStartValue;
    private TextBlock? _liquidPumpTurboValue;
    private Button? _liquidCurveApplyButton;
    private bool _liquidSyncing;
    private Grid? _liquidLightRow;
    private ComboBox? _liquidLightCombo;
    private Button? _liquidLightColorButton;
    private TextBlock? _liquidLightStatus;
    private bool _liquidLightSyncing;
    private readonly Dictionary<string, TextBlock> _effectValues = new();
    private DispatcherTimer? _effectCommitTimer;
    private bool _syncingCalibrationUi;
    private bool _syncingScreenEffect;
    private bool? _screenBackendConnected;
    private string _currentEffectMode = "";
    private readonly List<(ToggleButton Button, Func<bool> Get, Action<bool> Set)> _screenFeatures = new();
    private Window? _inlineWindow;
    private bool _wideMaximized;
    private bool _syncingWideBackgroundToggle;
    private bool _switchingToMini;
    private string _activePage = "home";
    private readonly Dictionary<string, long> _pageLastAccess = new() { ["home"] = Environment.TickCount64 };
    private readonly DispatcherTimer _pageEvictionTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _animatingWindow;
    private L_Mechrevo.WpfP0.Pages.SwitchesPageView? _switchesView;
    private ScrollViewer? SwitchesPage;
    private StackPanel? MoreSwitchGrid;
    private Rect _wideRestoreBounds;
    private CheckBox? _chargeLimitSwitch;
    private TextBox? _chargeLow;
    private TextBox? _chargeHigh;
    private TextBlock? _chargeStatus;
    private CheckBox? _chargeExperiment;
    private Slider? _chargeSlider;
    private TextBlock? _chargeSliderText;
    private readonly List<ToggleButton> _igpuSavingButtons = new();
    private TextBlock? _igpuSavingState;
    private CheckBox? _ecDirect;
    private bool _syncingChargeSlider;
    private readonly List<(ToggleButton Button, int Hz)> _displayRefreshButtons = new();
    private string _refreshRatesSignature = "";
    private readonly List<ToggleButton> _batteryModeButtons = new();
    private StackPanel? _batteryModePanel;
    private readonly List<(string Key, Button Button, TextBlock State, string Label)> _quickTileButtons = new();
    private readonly Dictionary<string, bool> _quickTilePending = new();
    private readonly Dictionary<string, long> _quickTilePendingTick = new();
    /// <summary>⭐ task-26：每张灯光卡的 150 ms 合并计时器；重建卡片时统一 Stop（避免对已拆掉的卡补发旧值）✅。</summary>
    private readonly List<DispatcherTimer> _lightCardApplyTimers = new();
    private Window? _inlineLightEditorWindow;
    private readonly List<LightEditorWindow> _inlineLightEditors = new();

    internal MainWindow(OriginalStyleWindow? owner = null, BackendProbe? probe = null)
    {
        InitializeComponent();
        L_Mechrevo.WpfP0.Services.WindowWorkAreaPolicy.ClampOnLoaded(this);
        EnsureHomeView();
        _owner = owner;
        _probe = probe;
        SourceInitialized += (_, _) =>
        {
            ApplyRoundedWindowChrome();
            WindowBackdrop.Apply(this);
            ReloadWideBackground();
        };
        RootContent.Opacity = 1;
        AppTheme.Apply(true);
        ReloadWideBackground();
        BuildSensorBlocks();
        BuildQuickTiles();
        BuildHomeExtras();
        BuildLiquidCard();
        BuildIgpuSavingControls();
        if (_owner != null)
        {
            _owner.BackendReady += OnBackendReady;
            _owner.WideUiVisibilityChanged += ApplyWideUiVisibilityFromOwner;
        }
        ApplyPage("home");
        _sensorTimer.Tick += (_, _) => RefreshSensors();
        _sensorTimer.Start();
        StateChanged += (_, _) => OnWideStateChanged();
        Closed += (_, _) =>
        {
            _sensorTimer.Stop();
            _pageEvictionTimer.Stop();
            if (_switchingToMini || _owner == null)
            {
                return;
            }
            try
            {
                AppConfig.Set("last_window_mode", "wide");
                AppConfig.Flush();
                _owner.Opacity = 0;
                _owner.ShowInTaskbar = false;
                _owner.Hide();
            }
            catch
            {
            }
        };
        ApplyHomeCapabilities();
        RefreshSensors();
        UpdateWideBackendStatus();
        StatusText.Text = WpfStrings.MainWindow_xaml_Text;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar(_dark);
    }

    private void UpdateWideBackendStatus()
    {
        if (WideBackendStatus == null)
        {
            return;
        }
        BackendProbe? probe = _probe ?? _owner?.CurrentBackend;
        bool gcu = probe?.IsGcuConnected == true;
        bool connected = probe?.Connected == true;
        bool reconnecting = probe?.IsReconnecting == true;
        bool nonGcu = probe?.IsNonGcuBackend == true;
        string text;
        string color;
        string tip;
        if (gcu)
        {
            text = WpfStrings.BackendGcuConnected;
            color = "#4ADE80";
            tip = WpfStrings.BackendGcuConnectedTip;
        }
        else if (reconnecting)
        {
            text = WpfStrings.BackendGcuConnecting;
            color = "#F59E0B";
            tip = WpfStrings.BackendGcuConnectingTip;
        }
        else if (connected || nonGcu)
        {
            text = WpfStrings.BackendNonGcuReady;
            color = "#4ADE80";
            tip = WpfStrings.L("本机后端已就绪（GCU 未连接）", "Local backend ready (GCU not connected)");
        }
        else
        {
            text = WpfStrings.BackendGcuDisconnected;
            color = "#EF4444";
            tip = WpfStrings.BackendGcuDisconnectedTip;
        }
        WideBackendStatus.Text = text;
        WideBackendStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        WideBackendStatus.ToolTip = tip;
    }

    private void RefreshSensors()
    {
        if (_owner == null)
        {
            return;
        }
        // ⭐ P0-3：宽屏也直接消费共享 ISensorService 快照（⛔ 不再经 WideSensors 读静态字段）；
        //   未连接/异常 ⇒ Empty ⇒ 0，占位显示与原来的 ParseNumber("--") 一致 ✅
        SensorSnapshot snapshot = _owner.Services?.Sensors.Read() ?? SensorSnapshot.Empty;

        double fanMax = Math.Max(1, snapshot.FanRpmMaxForPercent);
        if (fanMax <= 1) fanMax = 5000;
        SetMetric(_cpuMetrics, 0, snapshot.CpuTempC, "℃");
        SetMetric(_cpuMetrics, 1, snapshot.CpuUsagePercent, "%");
        SetMetric(_cpuMetrics, 2, snapshot.CpuPowerWatts ?? double.NaN, "W");
        SetMetric(_cpuMetrics, 3, snapshot.CpuFrequencyMhz, "MHz");
        SetMetric(_gpuMetrics, 0, snapshot.GpuTempC, "℃");
        SetMetric(_gpuMetrics, 1, snapshot.GpuUsagePercent, "%");
        SetMetric(_gpuMetrics, 2, snapshot.GpuPowerWatts ?? double.NaN, "W");
        SetMetric(_gpuMetrics, 3, snapshot.GpuCoreFrequencyMhz, "MHz");
        SetMetric(_gpuMetrics, 4, snapshot.GpuMemoryClockMhz ?? 0, "MHz");
        _cpuFanRing?.SetValue(snapshot.CpuFanRpm, fanMax, "rpm");
        _gpuFanRing?.SetValue(snapshot.GpuFanRpm, fanMax, "rpm");
        if (_cpuDutyText != null) _cpuDutyText.Text = WpfStrings.MainWindow_xaml_Text_2 + snapshot.CpuFanDutyPercent + "%";
        if (_gpuDutyText != null) _gpuDutyText.Text = WpfStrings.MainWindow_xaml_Text_2 + snapshot.GpuFanDutyPercent + "%";
        // ⭐ task-26：连接状态可能后变（GCU 服务重连/掉线）⇒ 每次传感器 tick 顺带校正一次屏幕页门控（有缓存，只在变化时改 UI）
        UpdateScreenBackendGating();
        UpdateWideBackendStatus();
        RefreshHomeExtras();
    }

    private static void SetMetric(List<(RoundedBar Bar, TextBlock Value, string Unit)> metrics, int index, double value, string unit)
    {
        if (index < 0 || index >= metrics.Count) return;
        (RoundedBar bar, TextBlock text, string _) = metrics[index];
        if (double.IsNaN(value))
        {
            bar.SetValue(0, bar.Maximum);
            text.Text = "--";
            return;
        }
        bar.SetValue(value, bar.Maximum);
        text.Text = unit switch
        {
            "W" => value.ToString("0.0") + " W",
            _ => Math.Round(value) + " " + unit
        };
    }

    private static double ParseNumber(string text)
    {
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(text, @"[-+]?\d+(?:\.\d+)?");
        return match.Success && double.TryParse(match.Value, out double value) ? value : 0;
    }

    private static string ResolveCpuModel()
    {
        try
        {
            string name = PawnIO.CpuInfo.Name?.Trim() ?? "";
            return name.Length == 0 ? "CPU" : name;
        }
        catch
        {
            return "CPU";
        }
    }

    private static string ResolveGpuModel()
    {
        try
        {
            MechrevoLite.Gpu.DgpuIdentity identity = MechrevoLite.Gpu.GpuGenerationProvider.Current();
            string name = identity.MarketingName?.Trim() ?? "";
            if (name.Length > 0)
            {
                return name;
            }
            // 核显/无独显机型：DgpuIdentity 没有 MarketingName，从显示适配器注册表补。
            IReadOnlyList<MechrevoLite.Gpu.GpuAdapter> adapters = MechrevoLite.Gpu.GpuAdapterReader.ReadFromRegistry(out _);
            bool Physical(MechrevoLite.Gpu.GpuAdapter a)
                => !string.IsNullOrWhiteSpace(a.Name)
                    && !IsVirtualDisplayName(a.Name)
                    && !a.Name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
            MechrevoLite.Gpu.GpuAdapter? adapter = adapters.FirstOrDefault(a => a.IsNvidia && Physical(a))
                ?? adapters.FirstOrDefault(a => Physical(a) && a.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                ?? adapters.FirstOrDefault(a => Physical(a) && a.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                // 最后只接受有 PCI VendorId 的物理适配器，⛔ 不把虚拟屏/串流屏当 GPU。
                ?? adapters.FirstOrDefault(a => Physical(a) && !string.IsNullOrWhiteSpace(a.VendorId));
            string adapterName = adapter?.Name?.Trim() ?? "";
            return adapterName.Length == 0 ? "GPU" : adapterName;
        }
        catch
        {
            return "GPU";
        }
    }

    private static bool IsVirtualDisplayName(string name)
    {
        string value = name.ToLowerInvariant();
        string[] markers =
        {
            "virtual", "usbmmidd", "indirect", "displaylink", "idd",
            "parsec", "sunshine", "spacedesk", "duet", "twomon",
            "air display", "remote", "rdp", "vmware", "virtualbox",
            "hyper-v", "citrix", "splashtop", "basic display"
        };
        foreach (string marker in markers)
        {
            if (value.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void BuildSensorBlocks()
    {
        if (SensorBlocksGrid == null) return;
        SensorBlocksGrid.Children.Add(BuildCpuGpuCard("CPU", cpu: true));
        SensorBlocksGrid.Children.Add(BuildCpuGpuCard("GPU", cpu: false));
    }

    private Border BuildCpuGpuCard(string title, bool cpu)
    {
        string model = cpu ? ResolveCpuModel() : ResolveGpuModel();
        TextBlock titleText = new()
        {
            Text = title,
            Style = (Style)FindResource("CardTitle"),
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock modelText = new()
        {
            Text = model,
            Style = (Style)FindResource("MutedText"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = model
        };
        Grid header = new() { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(titleText, 0);
        Grid.SetColumn(modelText, 1);
        header.Children.Add(titleText);
        header.Children.Add(modelText);

        StackPanel left = new() { Width = 140, VerticalAlignment = VerticalAlignment.Center };
        RingGauge ring = new()
        {
            Width = 116,
            Height = 116,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        TextBlock duty = new()
        {
            Text = WpfStrings.MainWindow_xaml_Text_3,
            Style = (Style)FindResource("MutedText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        left.Children.Add(ring);
        left.Children.Add(duty);
        if (cpu)
        {
            _cpuFanRing = ring;
            _cpuDutyText = duty;
            _cpuMetrics.Clear();
        }
        else
        {
            _gpuFanRing = ring;
            _gpuDutyText = duty;
            _gpuMetrics.Clear();
        }

        StackPanel metrics = new() { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        (string Label, double Maximum)[] rows = cpu
            ? new[] { (WpfStrings.SensorTemp, 100.0), (WpfStrings.SensorUsage, 100.0), (WpfStrings.SensorPower, 100.0), (WpfStrings.SensorFrequency, 6000.0) }
            : new[] { (WpfStrings.SensorTemp, 100.0), (WpfStrings.SensorUsage, 100.0), (WpfStrings.SensorPower, 200.0), (WpfStrings.SensorCoreFrequency, 3000.0), (WpfStrings.SensorVramFrequency, 12000.0) };
        List<(RoundedBar Bar, TextBlock Value, string Unit)> target = cpu ? _cpuMetrics : _gpuMetrics;
        foreach ((string label, double maximum) in rows)
        {
            StackPanel metric = new() { Margin = new Thickness(0, 0, 0, 8) };
            Grid rowHeader = new();
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new() { Text = label, Style = (Style)FindResource("MutedText") };
            TextBlock value = new()
            {
                Text = "--",
                Style = (Style)FindResource("MutedText"),
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(value, 1);
            rowHeader.Children.Add(name);
            rowHeader.Children.Add(value);
            RoundedBar bar = new()
            {
                Maximum = maximum,
                Height = 9,
                Margin = new Thickness(0, 5, 0, 0)
            };
            metric.Children.Add(rowHeader);
            metric.Children.Add(bar);
            metrics.Children.Add(metric);
            target.Add((bar, value, ""));
        }

        Grid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(metrics, 1);
        body.Children.Add(left);
        body.Children.Add(metrics);

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(header);
        root.Children.Add(body);
        return new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(5, 0, 5, 14),
            MinHeight = 260,
            Child = root
        };
    }

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navCollapsed = !_navCollapsed;
        NavColumn.Width = new GridLength(_navCollapsed ? 58 : 196);
        NavPanel.Padding = new Thickness(_navCollapsed ? 6 : 18);
        NavTitle.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavCollapseButton.Content = _navCollapsed ? "»" : "«";
        FontFamily glyphFont = new("Segoe MDL2 Assets");
        FontFamily textFont = (FontFamily)Application.Current.Resources["AppFontFamily"];
        SetNavContent(NavHome, _navCollapsed, "\uE80F", WpfStrings.NavHome_Content, glyphFont, textFont);
        SetNavContent(NavScreen, _navCollapsed, "\uE7F4", WpfStrings.NavScreen_Content, glyphFont, textFont);
        SetNavContent(NavLighting, _navCollapsed, "\uE706", WpfStrings.TitleText_Text_2, glyphFont, textFont);
        SetNavContent(NavCustom, _navCollapsed, "\uE70F", WpfStrings.NavCustom_Content, glyphFont, textFont);
        SetNavContent(NavSwitches, _navCollapsed, "\uE712", WpfStrings.NavMoreSwitches, glyphFont, textFont);
        SetDockNavContent(NavDockOverlay, _navCollapsed, "\uE8A7", WpfStrings.MainWindow_Text_4, glyphFont, textFont);
        SetDockNavContent(NavDockSettings, _navCollapsed, "\uE713", WpfStrings.InlineOverlayTitle_Text, glyphFont, textFont);
        SetDockNavContent(NavDockSponsor, _navCollapsed, "\uEB51", WpfStrings.MainWindow_Text_3, glyphFont, textFont);
        SetDockNavContent(NavDockDiag, _navCollapsed, "\uE9D9", WpfStrings.MainWindow_Text_2, glyphFont, textFont);
        SetDockNavContent(NavDockExit, _navCollapsed, "\uE7E8", WpfStrings.MainWindow_Text, glyphFont, textFont);
    }

    private static void SetDockNavContent(Button button, bool collapsed, string glyph, string text, FontFamily glyphFont, FontFamily textFont)
    {
        if (collapsed)
        {
            button.Content = new TextBlock
            {
                Text = glyph,
                FontFamily = glyphFont,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = button.Foreground
            };
            button.FontFamily = textFont;
            button.FontSize = 13;
            button.Padding = new Thickness(0);
            button.Height = 40;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            return;
        }
        StackPanel content = new() { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = glyphFont,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = textFont,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = content;
        button.FontFamily = textFont;
        button.FontSize = 13;
        button.Padding = new Thickness(12, 10, 12, 10);
        button.Height = double.NaN;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
    }

    private static void SetNavContent(Button button, bool collapsed, string glyph, string text, FontFamily glyphFont, FontFamily textFont)
    {
        if (collapsed)
        {
            button.Content = new TextBlock
            {
                Text = glyph,
                FontFamily = glyphFont,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = button.Foreground
            };
            button.FontFamily = textFont;
            button.FontSize = 13;
            button.Padding = new Thickness(0);
            button.Height = 40;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            button.Content = text;
            button.FontFamily = textFont;
            button.FontSize = 13;
            button.Padding = new Thickness(12, 10, 12, 10);
            button.Height = double.NaN;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e)
    {
        if (_owner != null)
        {
            _switchingToMini = true;
            AppConfig.Set("last_window_mode", "mini");
            AppConfig.Flush();
            _owner.ShowCompactWindow();
            // 先隐藏，若 TTL 内切回宽屏可秒开；超时后再真正销毁渲染。
            Hide();
            _owner.ScheduleWideWindowRelease();
            return;
        }
        StatusText.Text = WpfStrings.MainWindow_xaml_Text_4;
    }

    private void ApplyRoundedWindowChrome()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == 0) return;
            int corner = 2;
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
            int transitionsDisabled = 0;
            DwmSetWindowAttribute(hwnd, 3, ref transitionsDisabled, sizeof(int));
        }
        catch
        {
        }
    }

    private void UpdateMonochromeIcon()
    {
        try
        {
            const int size = 256;
            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                Brush background = new SolidColorBrush(_dark ? Colors.White : Colors.Black);
                dc.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 56, 56);
                Brush foreground = new SolidColorBrush(_dark ? Colors.Black : Colors.White);
                FormattedText text = new("L", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 176, foreground, 1.0);
                dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            }
            RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            Icon = bitmap;
        }
        catch
        {
        }
    }

    internal void ReloadWideBackground()
    {
        try
        {
            SyncWideBackgroundToggle();
            string path = AppConfig.GetString("wide_bg_image") ?? "";
            bool enabled = AppConfig.Get("wide_bg_enabled", 0) != 0;
            bool hasImage = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
            if (!enabled || !hasImage)
            {
                BackgroundImage.Visibility = Visibility.Collapsed;
                BackgroundScrim.Visibility = Visibility.Collapsed;
                // 背景关闭：恢复云母（默认开；用户显式选 solid 时 Apply 内部会尊重配置）。
                WindowBackdrop.Apply(this);
                return;
            }
            // 背景图优先：先撤掉本窗口的云母，再显示背景图和遮罩。
            WindowBackdrop.Disable(this);
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            BackgroundImage.Source = bitmap;
            BackgroundImage.Opacity = Math.Clamp(AppConfig.Get("wide_bg_opacity", 78), 0, 100) / 100.0;
            OriginalStyleWindow.ApplyBackgroundBlur(BackgroundImage);
            BackgroundImage.Visibility = Visibility.Visible;
            BackgroundScrim.Fill = new SolidColorBrush(Colors.Black);
            BackgroundScrim.Opacity = Math.Clamp(AppConfig.Get("wide_bg_scrim", 35), 0, 100) / 100.0;
            BackgroundScrim.Visibility = Visibility.Visible;
        }
        catch
        {
        }
    }

    private void SyncWideBackgroundToggle()
    {
        if (WideBackgroundToggle == null)
        {
            return;
        }
        string path = AppConfig.GetString("wide_bg_image") ?? "";
        bool hasImage = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
        bool enabled = AppConfig.Get("wide_bg_enabled", 0) != 0;
        _syncingWideBackgroundToggle = true;
        WideBackgroundToggle.IsEnabled = hasImage;
        WideBackgroundToggle.IsChecked = hasImage && enabled;
        WideBackgroundToggle.ToolTip = hasImage
            ? "显示/隐藏大窗口自定义背景图"
            : "先在设置 → 宽屏背景图里选择图片";
        _syncingWideBackgroundToggle = false;
    }

    private void WideBackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingWideBackgroundToggle || WideBackgroundToggle == null)
        {
            return;
        }
        AppConfig.Set("wide_bg_enabled", WideBackgroundToggle.IsChecked == true ? 1 : 0);
        AppConfig.Flush();
        ReloadWideBackground();
    }

    private void OnBackendReady()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _probe = _owner?.CurrentBackend;
            BuildMoreSwitchTiles();
            if (_lightingView != null) BuildLightingCards();
            if (_screenView != null) BuildScreenFeatures();
            UpdateScreenBackendGating();
            ApplyHomeCapabilities();
            // ⭐ 2026-10-26（task-18）：宽屏若在“后端就绪前”打开，充电阈值卡片（含实验行/EC 直写）
            //   当时因能力位读不到而**未建**；后端就绪后补建一次（不支持机型 Visible=false，不会建）。
            //   ⚠️ 只在“卡片还没建且现在支持”时补建，避免每次重连都重建整张电池卡。
            if (_owner != null
                && ((_chargeLimitText == null && _owner.WideChargeLimitVisible)
                    || (_chargeSlider == null && _owner.WideChargeExperimentSupported)
                    || (_ecDirect == null && _owner.WideEcDirectSupported)))
            {
                BuildHomeExtras();
            }
            UpdateWideBackendStatus();
            RefreshHomeExtras();
        }));
    }

    private void WideTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
        else
        {
            ToggleWideMaximize();
        }
    }

    private async void WideMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (_animatingWindow) return;
        _animatingWindow = true;
        ScaleTransform scale = RootContent.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        RootContent.RenderTransformOrigin = new Point(0.5, 0.5);
        RootContent.RenderTransform = scale;
        CubicEase ease = new() { EasingMode = EasingMode.EaseIn };
        DoubleAnimation fade = new(0, 0.35, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        DoubleAnimation shrink = new(1, 0.94, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        RootContent.BeginAnimation(OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        await Task.Delay(140);
        SystemCommands.MinimizeWindow(this);
        _animatingWindow = false;
    }

    private void WideMaximize_Click(object sender, RoutedEventArgs e) => ToggleWideMaximize();

    private void WideClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleWideMaximize()
    {
        if (_animatingWindow) return;
        Rect target;
        if (!_wideMaximized)
        {
            _wideRestoreBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            target = SystemParameters.WorkArea;
            _wideMaximized = true;
        }
        else
        {
            target = _wideRestoreBounds;
            _wideMaximized = false;
        }
        AnimateWindowBounds(target);
        if (WideMaxButton != null) WideMaxButton.Content = _wideMaximized ? "❐" : "□";
    }

    private void AnimateWindowBounds(Rect target)
    {
        _animatingWindow = true;
        TimeSpan duration = TimeSpan.FromMilliseconds(200);
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };
        DoubleAnimation left = new(target.Left, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        DoubleAnimation top = new(target.Top, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        DoubleAnimation width = new(target.Width, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        DoubleAnimation height = new(target.Height, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        left.Completed += (_, _) =>
        {
            Left = target.Left;
            Top = target.Top;
            Width = target.Width;
            Height = target.Height;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            _animatingWindow = false;
        };
        BeginAnimation(LeftProperty, left);
        BeginAnimation(TopProperty, top);
        BeginAnimation(WidthProperty, width);
        BeginAnimation(HeightProperty, height);
    }

    private void OnWideStateChanged()
    {
        if (WindowState == WindowState.Normal)
        {
            RootContent.Opacity = 1;
            if (RootContent.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? WpfStrings.ToggleLightTheme : WpfStrings.ToggleDarkTheme;
        StatusText.Text = "L-Mechrevo · " + (_dark ? WpfStrings.DarkTheme : WpfStrings.LightTheme) + WpfStrings.ThemeWord;
    }

    private void AccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            Color color = (Color)ColorConverter.ConvertFromString(text);
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["AccentColor"] = color;
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_5 + text;
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
        {
            ApplyPage(page);
        }
    }

    private void EnsureSwitchesView()
    {
        if (_switchesView != null)
        {
            return;
        }
        _switchesView = new L_Mechrevo.WpfP0.Pages.SwitchesPageView();
        SwitchesPage = _switchesView.SwitchesPage;
        MoreSwitchGrid = _switchesView.MoreSwitchGrid;
        SwitchesHost.Content = _switchesView;
        BuildMoreSwitchTiles();
    }

    private void ApplyPage(string page)
    {
        _activePage = page;
        _pageLastAccess[page] = Environment.TickCount64;
        if (page == "home") EnsureHomeView();
        else if (page == "screen") EnsureScreenView();
        else if (page == "lighting") EnsureLightingView();
        else if (page == "custom") EnsureCustomView();
        else if (page == "switches") EnsureSwitchesView();
        // ⭐ task-26：切到屏幕页时立即按连接状态门控一次（页面按需创建时 BuildScreenControls 也会做）
        if (page == "screen") UpdateScreenBackendGating();
        if (page == "custom") RefreshCustomModeEditor();
        HomeHost.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        ScreenHost.Visibility = page == "screen" ? Visibility.Visible : Visibility.Collapsed;
        LightingHost.Visibility = page == "lighting" ? Visibility.Visible : Visibility.Collapsed;
        CustomHost.Visibility = page == "custom" ? Visibility.Visible : Visibility.Collapsed;
        SwitchesHost.Visibility = page == "switches" ? Visibility.Visible : Visibility.Collapsed;
        Button[] navs = { NavHome, NavScreen, NavLighting, NavCustom, NavSwitches };
        foreach (Button nav in navs)
        {
            bool active = string.Equals(nav.Tag as string, page, StringComparison.Ordinal);
            nav.Background = active ? (Brush)FindResource("ControlBrush") : Brushes.Transparent;
            nav.Foreground = active ? (Brush)FindResource("TextBrush") : (Brush)FindResource("MutedBrush");
        }
        StatusText.Text = page switch
        {
            "home" => WpfStrings.NavHome_Content,
            "screen" => WpfStrings.NavScreen_Content,
            "lighting" => WpfStrings.TitleText_Text_2,
            "custom" => WpfStrings.NavCustom_Content,
            "switches" => WpfStrings.NavSwitches_Content,
            _ => WpfStrings.PageStatusReady
        };
    }

    private void EnsureWideSilentTurboButton(bool visible)
    {
        if (visible && HomePerf静音狂暴 == null && HomePerf狂暴.Parent is Panel panel)
        {
            ToggleButton button = new()
            {
                Content = WpfStrings.SilentTurbo,
                Tag = "silentturbo",
                Style = (_homeView?.TryFindResource("HomePerfSegment") as Style) ?? (Style)FindResource("SegmentButton")
            };
            button.Click += Mode_Click;
            int index = panel.Children.IndexOf(HomePerf狂暴);
            panel.Children.Insert(index < 0 ? panel.Children.Count : index, button);
            HomePerf静音狂暴 = button;
        }
        else if (!visible && HomePerf静音狂暴 != null)
        {
            if (HomePerf静音狂暴.Parent is Panel parent) parent.Children.Remove(HomePerf静音狂暴);
            HomePerf静音狂暴 = null;
        }
    }

    private void EnsureWideExtremeButton(bool visible)
    {
        if (visible && HomePerf极致 == null && HomePerf狂暴.Parent is Panel panel)
        {
            ToggleButton button = new()
            {
                Content = WpfStrings.NucExtremeTurbo,
                Tag = "5",
                Style = (_homeView?.TryFindResource("HomePerfSegment") as Style) ?? (Style)FindResource("SegmentButton")
            };
            button.Click += Mode_Click;
            int index = panel.Children.IndexOf(HomePerf狂暴);
            panel.Children.Insert(index < 0 ? panel.Children.Count : index + 1, button);
            HomePerf极致 = button;
        }
        else if (!visible && HomePerf极致 != null)
        {
            if (HomePerf极致.Parent is Panel parent) parent.Children.Remove(HomePerf极致);
            HomePerf极致 = null;
        }
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button)
        {
            return;
        }
        List<ToggleButton> perfButtons = new() { HomePerf静音, HomePerf平衡, HomePerf狂暴, HomePerf自定义 };
        if (HomePerf静音狂暴 != null) perfButtons.Add(HomePerf静音狂暴);
        if (HomePerf极致 != null) perfButtons.Add(HomePerf极致);
        foreach (ToggleButton perfButton in perfButtons)
        {
            perfButton.IsChecked = ReferenceEquals(perfButton, button);
        }
        if (HomePerf静音狂暴 != null && ReferenceEquals(button, HomePerf静音狂暴))
        {
            _owner?.WideSetSilentTurbo();
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_6 + button.Content;
            return;
        }
        if (HomePerf极致 != null && ReferenceEquals(button, HomePerf极致))
        {
            _owner?.WideSetMode(MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode);
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_6 + button.Content;
            return;
        }
        if (ReferenceEquals(button, HomePerf自定义))
        {
            _owner?.WideActivateCustomMode();
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_6 + button.Content;
            return;
        }
        if (button.Tag is string text && int.TryParse(text, out int mode))
        {
            _owner?.WideSetMode(mode);
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_6 + button.Content;
        }
    }

    private void Gpu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string text && int.TryParse(text, out int mode))
        {
            // 和小窗走同一个 GpuMode_Click：能力判断、宝龙达/SMU/重启确认由同一后端路径处理。
            _owner?.WideSetGpuMode(mode);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string text && int.TryParse(text, out int hz))
        {
            foreach ((ToggleButton refreshButton, int refreshHz) in _displayRefreshButtons)
            {
                refreshButton.IsChecked = refreshHz == hz;
            }
            _owner?.WideSetRefresh(hz);
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_8 + hz + " Hz";
        }
    }

    private async void QuickTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string key)
        {
            bool ok = _owner != null && await _owner.DashboardCommands.ToggleQuickAsync(key);
            // task-33：状态串走资源；key 的显示名也按当前语言取（英文模式不再拼中文 name）。
            string label = QuickSwitchService.Label(key);
            StatusText.Text = (ok ? WpfStrings.QuickTileToggled : WpfStrings.QuickTileToggleFailed) + label;
        }
    }

    private async void MoreSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string key)
        {
            bool ok = _owner != null && await _owner.DashboardCommands.ToggleQuickAsync(key);
            string label = QuickSwitchService.Label(key);
            StatusText.Text = (ok ? WpfStrings.MoreSwitchToggled : WpfStrings.MoreSwitchToggleFailed) + label;
        }
    }

    private void BuildMoreSwitchTiles()
    {
        if (MoreSwitchGrid == null || _owner == null) return;
        MoreSwitchGrid.Children.Clear();
        foreach ((string group, (string Key, string Label, bool On)[] items) in _owner.WideMoreSwitches())
        {
            MoreSwitchGrid.Children.Add(new TextBlock
            {
                Text = QuickSwitchService.GroupTitle(group),
                Style = (Style)FindResource("MutedText"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 10, 0, 6)
            });
            WrapPanel row = new();
            foreach ((string key, string label, bool on) in items)
            {
                string switchKey = key;
                // task-33：迷你窗回退目录里的 label 是硬编码中文；按 key 重新取当前语言显示名。
                string displayLabel = QuickSwitchService.Label(key, label);
                ToggleButton tile = new()
                {
                    Content = displayLabel,
                    Style = (Style)FindResource("SegmentButton"),
                    IsChecked = on,
                    Margin = new Thickness(0, 0, 8, 8),
                    MinWidth = 110
                };
                tile.Click += async (_, _) =>
                {
                    bool target = tile.IsChecked == true;
                    await _owner.DashboardCommands.ToggleQuickAsync(switchKey);
                    bool actual = _owner.WideMoreSwitches()
                        .SelectMany(groupItems => groupItems.Items)
                        .FirstOrDefault(item => item.Key == switchKey).On;
                    tile.IsChecked = actual;
                    StatusText.Text = (actual ? WpfStrings.QuickSwitchedOnPrefix : WpfStrings.QuickSwitchedOffPrefix) + displayLabel;
                };
                row.Children.Add(tile);
            }
            MoreSwitchGrid.Children.Add(row);
        }
    }

    private FrameworkElement CreateWideQuickIcon(string key, string glyph)
    {
        // 强冷图标与迷你窗完全一致：复用其四叶风扇 Path，而不是另一枚字体图标。
        if (string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase))
        {
            Path path = new()
            {
                Data = Geometry.Parse("M8,8 C6,5 6,2 8,1 C10,2 10,5 8,8 Z M8,8 C11,6 14,7 15,9 C13,11 10,10 8,8 Z M8,8 C10,11 10,14 8,15 C6,14 6,11 8,8 Z M8,8 C5,10 2,9 1,7 C3,5 6,6 8,8 Z M8,7 A1,1 0 1 1 8,9 A1,1 0 1 1 8,7 Z"),
                Fill = (Brush)FindResource("TextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas canvas = new() { Width = 16, Height = 16 };
            canvas.Children.Add(path);
            return new Viewbox { Width = 16, Height = 16, Child = canvas, HorizontalAlignment = HorizontalAlignment.Center };
        }
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    private void BuildQuickTiles()
    {
        if (_owner == null || QuickTilesGrid == null)
        {
            return;
        }
        QuickTilesGrid.Children.Clear();
        _quickTileButtons.Clear();
        foreach ((string key, string label, string glyph, bool on) in _owner.WideQuickTiles())
        {
            // task-33：迷你窗目录 label 是硬编码中文；按 key 取当前语言显示名（en-US 下为英文）。
            string displayLabel = QuickSwitchService.Label(key, label);
            FrameworkElement icon = CreateWideQuickIcon(key, glyph);
            TextBlock state = new()
            {
                Text = displayLabel + (on ? " · " + WpfStrings.StateOn : " · " + WpfStrings.StateOff),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                // task-33：英文标签更长（如 Turn off display · Off）⇒ 允许两行，避免底部磁贴被裁字。
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Style = (Style)FindResource("MutedText")
            };
            StackPanel content = new();
            content.Children.Add(icon);
            content.Children.Add(state);
            Button tile = new()
            {
                Style = (Style)FindResource("TileButton"),
                Tag = key,
                Content = content,
                Width = 72,
                Height = 52,
                Margin = new Thickness(3),
                Padding = new Thickness(4, 3, 4, 3)
            };
            tile.Click += async (_, _) =>
            {
                if (_owner != null)
                {
                    bool previous = _owner.WideQuickTiles().FirstOrDefault(item => item.Key == key).On;
                    bool ok = await _owner.WideToggleQuickAsync(key);
                    if (ok)
                    {
                        _quickTilePending[key] = !previous;
                        _quickTilePendingTick[key] = Environment.TickCount64;
                    }
                    RefreshQuickTiles();
                    RefreshIgpuSaving();
                }
            };
            QuickTilesGrid.Children.Add(tile);
            _quickTileButtons.Add((key, tile, state, displayLabel));
        }
        StackPanel customContent = new();
        customContent.Children.Add(new TextBlock
        {
            Text = "\uE710",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        customContent.Children.Add(new TextBlock
        {
            Text = WpfStrings.MainWindow_xaml_Text_9,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Style = (Style)FindResource("MutedText")
        });
        Button customTile = new()
        {
            Style = (Style)FindResource("TileButton"),
            Content = customContent,
            Width = 72,
            Height = 52,
            Margin = new Thickness(3),
            Padding = new Thickness(4, 3, 4, 3)
        };
        customTile.Click += (_, _) =>
        {
            _owner?.WideEditQuickTiles();
            BuildQuickTiles();
        };
        QuickTilesGrid.Children.Add(customTile);
        QuickTilesGrid.Columns = Math.Max(1, QuickTilesGrid.Children.Count);
        RefreshQuickTiles();
    }

    private void RefreshQuickTiles()
    {
        if (_owner == null)
        {
            return;
        }
        foreach ((string key, Button button, TextBlock state, string label) in _quickTileButtons)
        {
            bool on = _owner.WideQuickTiles().FirstOrDefault(item => item.Key == key).On;
            if (_quickTilePending.TryGetValue(key, out bool pending))
            {
                if (on == pending)
                {
                    _quickTilePending.Remove(key);
                    _quickTilePendingTick.Remove(key);
                }
                else if (Environment.TickCount64 - _quickTilePendingTick.GetValueOrDefault(key) < 6000)
                {
                    on = pending;
                }
                else
                {
                    _quickTilePending.Remove(key);
                    _quickTilePendingTick.Remove(key);
                }
            }
            state.Text = label + (on ? " · " + WpfStrings.StateOn : " · " + WpfStrings.StateOff);
            button.BorderBrush = on ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("CardBorderBrush");
            button.BorderThickness = new Thickness(on ? 2 : 1);
            button.Background = on
                ? new SolidColorBrush(Color.FromArgb(0x26, 0x4A, 0x9E, 0xFF))
                : Brushes.Transparent;
        }
    }

    private void BuildHomeExtras()
    {
        if (_owner == null)
        {
            return;
        }
        (string info, int mode, string charge) = _owner.WideBattery();
        StackPanel battery = new();
        battery.Children.Add(new TextBlock { Text = WpfStrings.MainWindow_xaml_Text_10, Style = (Style)FindResource("CardTitle") });
        _batteryInfoText = new TextBlock { Text = info, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 4, 0, 8) };
        battery.Children.Add(_batteryInfoText);
        // ⭐ task-18：本方法可能在后端就绪后**补建**一次（见 OnBackendReady）⇒ 先清空档位按钮引用，
        //   避免旧的按钮对象留在列表里（RefreshHomeExtras 会按列表回填 IsChecked）。
        _batteryModeButtons.Clear();
        StackPanel modes = new() { Orientation = Orientation.Horizontal };
        string[] labels = { WpfStrings.BatteryLong, WpfStrings.BatteryBalanced, WpfStrings.BatteryWorkstation };
        for (int i = 0; i < labels.Length; i++)
        {
            int target = i;
            ToggleButton button = new()
            {
                Content = labels[i],
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = mode == i,
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.Click += (_, _) =>
            {
                foreach (ToggleButton other in _batteryModeButtons)
                {
                    other.IsChecked = ReferenceEquals(other, button);
                }
                _owner?.WideSetBatteryMode(target);
                StatusText.Text = WpfStrings.MainWindow_xaml_Text_11 + labels[target];
            };
            modes.Children.Add(button);
            _batteryModeButtons.Add(button);
        }
        modes.Visibility = _owner.WideBatteryModeVisible ? Visibility.Visible : Visibility.Collapsed;
        _batteryModePanel = modes;
        battery.Children.Add(modes);
        if (_owner.WideChargeLimitVisible)
        {
            _chargeLimitText = new TextBlock { Text = charge, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            battery.Children.Add(_chargeLimitText);

            (bool limitEnabled, string lowText, string highText, string limitStatus, bool experiment, bool ecDirect) = _owner.WideChargeLimit();
            _chargeLimitSwitch = new CheckBox { Content = WpfStrings.MainWindow_xaml_Content, Style = (Style)FindResource("HeaderSwitch"), IsChecked = limitEnabled, Margin = new Thickness(0, 8, 8, 0) };
            _chargeLimitSwitch.Click += (_, _) => _owner.WideSetChargeLimitEnabled(_chargeLimitSwitch.IsChecked == true);
            _chargeLow = new TextBox { Text = lowText, Width = 46, Height = 26, Margin = new Thickness(0, 8, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _chargeHigh = new TextBox { Text = highText, Width = 46, Height = 26, Margin = new Thickness(0, 8, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Button applyLimit = new() { Content = WpfStrings.MainWindow_xaml_Content_2, Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 8, 0, 0) };
            applyLimit.Click += (_, _) => _owner.WideApplyChargeLimit(_chargeLow.Text, _chargeHigh.Text);
            StackPanel limitRow = new() { Orientation = Orientation.Horizontal };
            limitRow.Children.Add(_chargeLimitSwitch);
            limitRow.Children.Add(new TextBlock { Text = WpfStrings.MainWindow_xaml_Text_12, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 4, 0) });
            limitRow.Children.Add(_chargeLow);
            limitRow.Children.Add(new TextBlock { Text = WpfStrings.MainWindow_xaml_Text_13, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 4, 0) });
            limitRow.Children.Add(_chargeHigh);
            limitRow.Children.Add(applyLimit);
            battery.Children.Add(limitRow);
            _chargeStatus = new TextBlock { Text = limitStatus, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            battery.Children.Add(_chargeStatus);

            // ⭐⭐ 2026-10-26（task-18 · 电池实验行能力位门控）：这两行旧实现放在
            //   `if (_owner.WideChargeLimitVisible)` **之外** ⇒ 不支持的机型（非宝龙达白名单 /
            //   10/16/20/30 系 / 无界非 8040 / Gen30）也会出现「可点但无下发」的控件 ❌
            //   ⇒ 现在按各自能力位**分别**创建：不支持就一个控件都不建（不是建好再隐藏）。
            bool experimentSupported = _owner.WideChargeExperimentSupported;
            bool ecDirectSupported = _owner.WideEcDirectSupported;
            if (experimentSupported || ecDirectSupported)
            {
                StackPanel extraLimitRow = new() { Orientation = Orientation.Horizontal };
                if (experimentSupported)
                {
                    CheckBox experimentBox = new() { Content = WpfStrings.MainWindow_xaml_Content_3, Style = (Style)FindResource("HeaderSwitch"), IsChecked = experiment, Margin = new Thickness(0, 8, 16, 0) };
                    experimentBox.Click += (_, _) =>
                    {
                        _owner.WideSetChargeExperiment(experimentBox.IsChecked == true);
                        if (experimentBox.IsChecked == true && _ecDirect != null) _ecDirect.IsChecked = false;
                        UpdateChargeSliderVisibility();
                    };
                    _chargeExperiment = experimentBox;
                    extraLimitRow.Children.Add(experimentBox);
                }
                if (ecDirectSupported)
                {
                    CheckBox ecDirectBox = new() { Content = WpfStrings.MainWindow_xaml_Content_4, Style = (Style)FindResource("HeaderSwitch"), IsChecked = ecDirect, Margin = new Thickness(0, 8, 0, 0) };
                    ecDirectBox.Click += (_, _) =>
                    {
                        _owner.WideSetEcDirect(ecDirectBox.IsChecked == true);
                        if (ecDirectBox.IsChecked == true && _chargeExperiment != null)
                        {
                            _chargeExperiment.IsChecked = false;
                            UpdateChargeSliderVisibility();
                        }
                    };
                    _ecDirect = ecDirectBox;
                    extraLimitRow.Children.Add(ecDirectBox);
                }
                battery.Children.Add(extraLimitRow);
            }

            // ⭐ task-18：「无极限充」滑块只在该能力支持时创建（旧 WinForms 不支持时 RowCount=3、不建滑块行）。
            if (experimentSupported)
            {
                (bool sliderOn, int sliderValue) = _owner.WideBatteryLimitSlider();
                _chargeSlider = new Slider
                {
                    Minimum = 40,
                    Maximum = 100,
                    Value = sliderValue,
                    Width = 190,
                    Margin = new Thickness(0, 8, 6, 0),
                    Style = (Style)FindResource("RoundSlider"),
                    Visibility = sliderOn ? Visibility.Visible : Visibility.Collapsed
                };
                _chargeSlider.ValueChanged += (_, _) =>
                {
                    if (_syncingChargeSlider)
                    {
                        return;
                    }
                    _owner.WideSetBatteryLimitSlider((int)Math.Round(_chargeSlider.Value));
                    if (_chargeSliderText != null) _chargeSliderText.Text = (int)Math.Round(_chargeSlider.Value) + "%";
                };
                _chargeSliderText = new TextBlock
                {
                    Text = sliderValue + "%",
                    Style = (Style)FindResource("MutedText"),
                    Width = 46,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = sliderOn ? Visibility.Visible : Visibility.Collapsed
                };
                StackPanel sliderRow = new() { Orientation = Orientation.Horizontal };
                sliderRow.Children.Add(_chargeSlider);
                sliderRow.Children.Add(_chargeSliderText);
                battery.Children.Add(sliderRow);
            }
        }
        // GCU 没有宝龙达阈值面板，但「无极限充 / 关闭 EC 直写」属于 EC 传输，必须单独显示。
        if (!_owner.WideChargeLimitVisible)
        {
            bool gcuExperiment = _owner.WideChargeExperimentSupported;
            bool gcuEcDirect = _owner.WideEcDirectSupported;
            if (gcuExperiment || gcuEcDirect)
            {
                StackPanel gcuExtraRow = new() { Orientation = Orientation.Horizontal };
                if (gcuExperiment)
                {
                    CheckBox gcuExperimentBox = new() { Content = WpfStrings.MainWindow_xaml_Content_3, Style = (Style)FindResource("HeaderSwitch"), IsChecked = _owner.WideBatteryLimitSlider().Toggle, Margin = new Thickness(0, 8, 16, 0) };
                    gcuExperimentBox.Click += (_, _) =>
                    {
                        _owner.WideSetChargeExperiment(gcuExperimentBox.IsChecked == true);
                        if (gcuExperimentBox.IsChecked == true && _ecDirect != null) _ecDirect.IsChecked = false;
                        UpdateChargeSliderVisibility();
                    };
                    _chargeExperiment = gcuExperimentBox;
                    gcuExtraRow.Children.Add(gcuExperimentBox);
                }
                if (gcuEcDirect)
                {
                    CheckBox gcuEcDirectBox = new() { Content = WpfStrings.MainWindow_xaml_Content_4, Style = (Style)FindResource("HeaderSwitch"), IsChecked = _owner.WideEcDirectSupported && !AppConfig.Is("ec_charge_limit"), Margin = new Thickness(0, 8, 0, 0) };
                    gcuEcDirectBox.Click += (_, _) =>
                    {
                        _owner.WideSetEcDirect(gcuEcDirectBox.IsChecked == true);
                        if (gcuEcDirectBox.IsChecked == true && _chargeExperiment != null)
                        {
                            _chargeExperiment.IsChecked = false;
                            UpdateChargeSliderVisibility();
                        }
                    };
                    _ecDirect = gcuEcDirectBox;
                    gcuExtraRow.Children.Add(gcuEcDirectBox);
                }
                battery.Children.Add(gcuExtraRow);
            }
            if (gcuExperiment)
            {
                (bool gcuSliderOn, int gcuSliderValue) = _owner.WideBatteryLimitSlider();
                _chargeSlider = new Slider { Minimum = 40, Maximum = 100, Value = gcuSliderValue, Width = 190, Margin = new Thickness(0, 8, 6, 0), Style = (Style)FindResource("RoundSlider"), Visibility = gcuSliderOn ? Visibility.Visible : Visibility.Collapsed };
                _chargeSlider.ValueChanged += (_, _) =>
                {
                    if (_syncingChargeSlider)
                    {
                        return;
                    }
                    _owner.WideSetBatteryLimitSlider((int)Math.Round(_chargeSlider.Value));
                    if (_chargeSliderText != null) _chargeSliderText.Text = (int)Math.Round(_chargeSlider.Value) + "%";
                };
                _chargeSliderText = new TextBlock { Text = gcuSliderValue + "%", Style = (Style)FindResource("MutedText"), Width = 46, VerticalAlignment = VerticalAlignment.Center, Visibility = gcuSliderOn ? Visibility.Visible : Visibility.Collapsed };
                StackPanel gcuSliderRow = new() { Orientation = Orientation.Horizontal };
                gcuSliderRow.Children.Add(_chargeSlider);
                gcuSliderRow.Children.Add(_chargeSliderText);
                battery.Children.Add(gcuSliderRow);
            }
        }

        Grid batteryLayout = new();
        batteryLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        batteryLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid batteryGlyph = new() { Width = 48, Height = 48 };
        Border batteryBody = new()
        {
            Width = 32,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            BorderBrush = (Brush)FindResource("MutedBrush"),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Child = new Border
            {
                Width = 18,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0)
            }
        };
        Border batteryTip = new()
        {
            Width = 4,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Background = (Brush)FindResource("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            // 电池尾部小凸起紧贴电池体，原 4px 间距看起来断开
            Margin = new Thickness(0, 0, 8, 0)
        };
        batteryGlyph.Children.Add(batteryBody);
        batteryGlyph.Children.Add(batteryTip);
        Viewbox batteryIcon = new()
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = batteryGlyph
        };
        Grid.SetColumn(battery, 1);
        batteryLayout.Children.Add(batteryIcon);
        batteryLayout.Children.Add(battery);
        BatteryCardHost.Content = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0), Child = batteryLayout };

        (string displayInfo, int currentHz, int maxHz, bool auto, int brightness) = _owner.WideDisplay();
        DisplayInfoText.Text = displayInfo;
        // BuildHomeExtras 会在后端就绪时重建；先清掉上一次的亮度行/自动刷新，避免出现两排亮度。
        DisplayDetailsPanel.Children.Clear();
        for (int i = DisplayInfoRow.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(DisplayInfoRow.Children[i], DisplayInfoText))
            {
                DisplayInfoRow.Children.RemoveAt(i);
            }
        }
        BuildRefreshRateButtons(_owner.WideRefreshRates(), currentHz);
        StackPanel brightnessRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        brightnessRow.Children.Add(new TextBlock { Text = WpfStrings.Brightness, Style = (Style)FindResource("MutedText"), Width = 54, VerticalAlignment = VerticalAlignment.Center });
        _displayBrightness = new Slider { Minimum = 0, Maximum = 100, Value = brightness, Width = 220, Style = (Style)FindResource("RoundSlider") };
        _displayBrightness.ValueChanged += (_, _) =>
        {
            int value = (int)Math.Round(_displayBrightness.Value);
            if (_displayBrightnessText != null)
            {
                _displayBrightnessText.Text = value + "%";
            }
            _owner?.WideSetBrightness(value);
        };
        _displayBrightnessText = new TextBlock { Text = brightness + "%", Style = (Style)FindResource("MutedText"), Width = 46, TextAlignment = TextAlignment.Right };
        brightnessRow.Children.Add(_displayBrightness);
        brightnessRow.Children.Add(_displayBrightnessText);
        _autoRefresh = new CheckBox
        {
            Content = WpfStrings.MainWindow_xaml_Content_5,
            Style = (Style)FindResource("HeaderSwitch"),
            IsChecked = auto,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_autoRefresh, 1);
        DisplayInfoRow.Children.Add(_autoRefresh);
        _autoRefresh.Click += (_, _) => _owner?.WideSetAutoRefresh(_autoRefresh.IsChecked == true);
        DisplayDetailsPanel.Children.Add(brightnessRow);
        if (_owner.SupportsMiniled)
        {
            StackPanel miniledRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            miniledRow.Children.Add(new TextBlock
            {
                Text = WpfStrings.L("MiniLED 分区控光", "MiniLED local dimming"),
                Style = (Style)FindResource("MutedText"),
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center
            });
            CheckBox miniled = new()
            {
                Content = WpfStrings.L("开启", "On"),
                Style = (Style)FindResource("HeaderSwitch"),
                IsChecked = _owner.MiniledOn,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            miniled.Click += (_, _) => _owner.ToggleMiniled();
            miniledRow.Children.Add(miniled);
            DisplayDetailsPanel.Children.Add(miniledRow);
        }
    }

    private void BuildRefreshRateButtons(int[] rates, int currentHz)
    {
        DisplayRefreshPanel.Children.Clear();
        _displayRefreshButtons.Clear();
        _refreshRatesSignature = string.Join(",", rates);
        foreach (int hz in rates)
        {
            int target = hz;
            ToggleButton button = new()
            {
                Content = hz + " Hz",
                Tag = hz.ToString(),
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = Math.Abs(hz - currentHz) <= 2,
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.Click += (_, _) => _owner?.WideSetRefresh(target);
            DisplayRefreshPanel.Children.Add(button);
            _displayRefreshButtons.Add((button, hz));
        }
    }

    private void BuildIgpuSavingControls()
    {
        if (_owner == null || HomeIgpuSavingPanel == null)
        {
            return;
        }
        StackPanel row = new() { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = WpfStrings.MainWindow_xaml_Text_14, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        string[] labels = { WpfStrings.StateOff, WpfStrings.StateOn, WpfStrings.StateAuto };
        string[] modes = { "OFF", "ON", "AUTO" };
        for (int i = 0; i < labels.Length; i++)
        {
            string mode = modes[i];
            ToggleButton button = new() { Content = labels[i], Style = (Style)FindResource("SegmentButton"), Margin = new Thickness(0, 0, 6, 0) };
            button.Click += async (_, _) =>
            {
                foreach (ToggleButton other in _igpuSavingButtons) other.IsChecked = ReferenceEquals(other, button);
                await _owner.WideSetIgpuSavingAsync(mode);
                RefreshIgpuSaving();
            };
            row.Children.Add(button);
            _igpuSavingButtons.Add(button);
        }
        _igpuSavingState = new TextBlock { Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        row.Children.Add(_igpuSavingState);
        HomeIgpuSavingPanel.Children.Add(row);
        HomeIgpuSavingPanel.Visibility = (_owner.WideIgpuSavingSupported || UiQaShowAll.Enabled)
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshIgpuSaving();
    }

    private void RefreshIgpuSaving()
    {
        if (_owner == null || HomeIgpuSavingPanel == null) return;
        bool supported = _owner.WideIgpuSavingSupported || UiQaShowAll.Enabled;
        HomeIgpuSavingPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        int mode = _owner.WideIgpuSavingMode;
        foreach (ToggleButton button in _igpuSavingButtons)
        {
            button.IsEnabled = supported;
        }
        if (_igpuSavingButtons.Count >= 3)
        {
            _igpuSavingButtons[0].IsChecked = mode == 1;
            _igpuSavingButtons[1].IsChecked = mode == 0;
            _igpuSavingButtons[2].IsChecked = mode == 3;
        }
        if (_igpuSavingState != null)
        {
            // task-33：动态状态串（开/关/自动/未知）走资源，中文保持原文。
            _igpuSavingState.Text = supported
                ? mode switch
                {
                    0 => WpfStrings.IgpuSavingCurrentOn,
                    1 => WpfStrings.IgpuSavingCurrentOff,
                    3 => WpfStrings.IgpuSavingCurrentAuto,
                    _ => WpfStrings.IgpuSavingCurrentUnknown
                }
                : WpfStrings.IgpuSavingCurrentUnsupported;
        }
    }

    private void BuildScreenControls()
    {
        if (_owner == null)
        {
            return;
        }
        (bool calibEnabled, _, _) = _owner.WideCalibration();
        ScreenCalibrationToggle.IsChecked = calibEnabled;
        (bool effectEnabled, _, _) = _owner.WideDisplayEffect();
        ScreenEffectToggle.IsChecked = effectEnabled;
        BuildCalibrationOptions();
        BuildEffectOptions();
        BuildScreenFeatures();
        LoadCalibrationSettings();
        _effectCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _effectCommitTimer.Tick += async (_, _) =>
        {
            _effectCommitTimer.Stop();
            await CommitEffectProfileAsync();
        };
        // ⭐ task-26：屏幕页创建后立即按当前后端连接状态做一次门控（未连接 ⇒ 编辑/开关禁用并提示）
        UpdateScreenBackendGating();
    }

    private void BuildCalibrationOptions()
    {
        if (_owner == null) return;
        ScreenCalibrationOptionsPanel.Children.Clear();
        _calibrationOptionButtons.Clear();
        (_, (string Label, object? Tag)[] options, int selected) = _owner.WideCalibration();
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            ToggleButton button = new()
            {
                Content = options[i].Label,
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = i == selected,
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Click += (_, _) =>
            {
                _owner.WideSetCalibrationIndex(index);
                foreach (ToggleButton item in _calibrationOptionButtons) item.IsChecked = ReferenceEquals(item, button);
            };
            _calibrationOptionButtons.Add(button);
            ScreenCalibrationOptionsPanel.Children.Add(button);
        }
    }

    private void BuildEffectOptions()
    {
        if (_owner == null) return;
        ScreenEffectOptionsPanel.Children.Clear();
        _effectOptionButtons.Clear();
        (_, (string Label, object? Tag)[] options, int selected) = _owner.WideDisplayEffect();
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            ToggleButton button = new()
            {
                Content = options[i].Label,
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = i == selected,
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Click += (_, _) =>
            {
                _owner.WideSetDisplayEffectIndex(index);
                foreach (ToggleButton item in _effectOptionButtons) item.IsChecked = ReferenceEquals(item, button);
                if (ScreenEffectEditorPanel.Visibility == Visibility.Visible) BuildEffectEditor();
            };
            _effectOptionButtons.Add(button);
            ScreenEffectOptionsPanel.Children.Add(button);
        }
    }

    private void BuildScreenFeatures()
    {
        if (_owner == null || ScreenFeaturesPanel == null) return;
        ScreenFeaturesPanel.Children.Clear();
        _screenFeatures.Clear();
        ScreenFeaturesCard.Visibility = Visibility.Collapsed;
        AddScreenFeature(WpfStrings.L("显示响应加速", "Display overdrive"), () => _owner.SupportsLcdOverdrive, () => _owner.LcdOverdrive,
            on => _ = _owner.SetLcdOverdriveAsync(on));
        AddScreenFeature(WpfStrings.L("MiniLED 分区控光", "MiniLED local dimming"), () => _owner.SupportsMiniled, () => _owner.MiniledOn,
            on => { if (_owner.MiniledOn != on) _owner.ToggleMiniled(); });
        AddScreenFeature("HDR Color Control", () => _owner.SupportsHdrControl, () => _owner.HdrControlOn,
            on => { if (_owner.HdrControlOn != on) _owner.ToggleHdrControl(); });
        if (_owner.SupportsFhd)
        {
            Button fhd = new()
            {
                Content = WpfStrings.MainWindow_xaml_Content_6,
                Style = (Style)FindResource("FlatButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 8, 8)
            };
            fhd.Click += (_, _) => _owner.ToggleFhd();
            ScreenFeaturesPanel.Children.Add(fhd);
        }
        ScreenFeaturesCard.Visibility = ScreenFeaturesPanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddScreenFeature(string label, Func<bool> supported, Func<bool> get, Action<bool> set)
    {
        bool isSupported = false;
        try { isSupported = supported(); } catch { }
        if (!isSupported) return;
        ToggleButton button = new()
        {
            Content = label,
            Style = (Style)FindResource("SegmentButton"),
            IsChecked = get(),
            Margin = new Thickness(0, 0, 8, 8)
        };
        button.Click += (_, _) =>
        {
            try { set(button.IsChecked == true); } catch { }
        };
        ScreenFeaturesPanel.Children.Add(button);
        _screenFeatures.Add((button, get, set));
    }

    private void LoadCalibrationSettings()
    {
        _syncingCalibrationUi = true;
        try
        {
            CalibrationStartupSwitch.IsChecked = AppConfig.Is("calib_apply_on_startup");
            CalibrationCustomSwitch.IsChecked = AppConfig.Is("calib_custom_on");
            RefreshIccList();
        }
        finally
        {
            _syncingCalibrationUi = false;
        }
    }

    private void RefreshIccList()
    {
        CalibrationIccCombo.Items.Clear();
        foreach (string name in IccHelper.ListCustomIccFiles())
        {
            CalibrationIccCombo.Items.Add(name);
        }
        string saved = AppConfig.GetString("calib_custom_icc") ?? "";
        int selected = CalibrationIccCombo.Items.IndexOf(saved);
        if (selected >= 0) CalibrationIccCombo.SelectedIndex = selected;
    }

    private void ScreenCalibrationToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWideBackendConnected)
        {
            // task-26：未连接时点了也不下发（开关已禁用，这里是键盘/并发路径的兜底）
            ScreenCalibrationToggle.IsChecked = !(ScreenCalibrationToggle.IsChecked == true);
            StatusText.Text = WpfStrings.StatusCalibrationNeedsBackend;
            return;
        }
        _owner?.WideSetCalibrationEnabled(ScreenCalibrationToggle.IsChecked == true);
        BuildCalibrationOptions();
    }

    private void ScreenEffectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWideBackendConnected)
        {
            ScreenEffectToggle.IsChecked = !(ScreenEffectToggle.IsChecked == true);
            StatusText.Text = WpfStrings.StatusEffectNeedsBackend;
            return;
        }
        _owner?.WideSetDisplayEffectEnabled(ScreenEffectToggle.IsChecked == true);
        BuildEffectOptions();
    }

    private void ScreenCalibrationSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = ScreenCalibrationSettingsPanel.Visibility != Visibility.Visible;
        ScreenCalibrationSettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) LoadCalibrationSettings();
        if (!IsWideBackendConnected && show)
        {
            // task-26：本地 ICC 设置仍可用；只提示需要后端的开关/档位
            StatusText.Text = WpfStrings.StatusCalibrationSettingsNeedsBackend;
        }
    }

    private void CalibrationStartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingCalibrationUi) return;
        AppConfig.Set("calib_apply_on_startup", CalibrationStartupSwitch.IsChecked == true ? 1 : 0);
        AppConfig.Flush();
    }

    private void CalibrationCustomSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingCalibrationUi) return;
        bool on = CalibrationCustomSwitch.IsChecked == true;
        AppConfig.Set("calib_custom_on", on ? 1 : 0);
        AppConfig.Flush();
        CalibrationIccCombo.IsEnabled = on;
        if (!on)
        {
            _ = Task.Run(() => IccHelper.ClearAllIcc());
            if (AppConfig.Get("calib_mode") == 999)
            {
                AppConfig.Set("calib_mode", 1);
                AppConfig.Flush();
            }
        }
        BuildCalibrationOptions();
    }

    private void CalibrationIccCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCalibrationUi || CalibrationIccCombo.SelectedItem is not string name) return;
        AppConfig.Set("calib_custom_icc", name);
        AppConfig.Flush();
    }

    private void CalibrationImportIcc_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = WpfStrings.L("ICC 配置文件|*.icc;*.icm|所有文件|*.*", "ICC profiles|*.icc;*.icm|All files|*.*"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        string? imported = IccHelper.ImportIcc(dialog.FileName);
        if (string.IsNullOrWhiteSpace(imported))
        {
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_15;
            return;
        }
        AppConfig.Set("calib_custom_icc", imported);
        AppConfig.Flush();
        _syncingCalibrationUi = true;
        try
        {
            RefreshIccList();
            int index = CalibrationIccCombo.Items.IndexOf(imported);
            if (index >= 0) CalibrationIccCombo.SelectedIndex = index;
        }
        finally
        {
            _syncingCalibrationUi = false;
        }
    }

    private void ScreenEffectEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWideBackendConnected)
        {
            // task-26：未连接时编辑面板打不开（按钮本身已被门控禁用；这里是兜底）
            StatusText.Text = WpfStrings.StatusScreenEffectEditorNeedsBackend;
            return;
        }
        bool show = ScreenEffectEditorPanel.Visibility != Visibility.Visible;
        ScreenEffectEditorPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) BuildEffectEditor();
    }

    private void BuildEffectEditor()
    {
        ScreenEffectSliderPanel.Children.Clear();
        _effectSliders.Clear();
        _effectValues.Clear();
        if (_owner == null) return;
        _currentEffectMode = _owner.WideSelectedDisplayEffectMode();
        DisplayEffectProfile? profile = null;
        if (_probe?.Hw != null && _probe.Hw.DisplayEffectProfiles.TryGetValue(_currentEffectMode, out DisplayEffectProfile? found))
        {
            profile = found;
        }
        profile ??= MechrevoLite.Hardware.SoftwareColorEnhancement.DefaultProfile(_currentEffectMode);
        AddEffectRow("brightness", WpfStrings.L("亮度", "Brightness"), 0, 100, profile.Brightness, v => v + "%");
        AddEffectRow("colortemp", WpfStrings.L("色温", "Color temp"), 30, 100, profile.ColorTemp / 100, v => (v * 100) + "K");
        AddEffectRow("saturation", WpfStrings.L("饱和度", "Saturation"), 5, 25, (int)Math.Round(profile.VibrantValue * 10), v => (v * 0.1).ToString("0.0"));
        AddEffectRow("gamma", WpfStrings.L("伽玛", "Gamma"), 50, 200, (int)Math.Round(profile.Gamma * 100), v => (v * 0.01).ToString("0.00"));
        AddEffectRow("r", WpfStrings.L("红", "Red"), 0, 255, (int)profile.ColorR, v => v.ToString());
        AddEffectRow("g", WpfStrings.L("绿", "Green"), 0, 255, (int)profile.ColorG, v => v.ToString());
        AddEffectRow("b", WpfStrings.L("蓝", "Blue"), 0, 255, (int)profile.ColorB, v => v.ToString());
        AddEffectRow("contrast", WpfStrings.L("对比度", "Contrast"), 5, 20, (int)Math.Round(profile.Constrast * 10), v => (v * 0.1).ToString("0.0"));
    }

    private void AddEffectRow(string key, string label, int min, int max, int value, Func<int, string> format)
    {
        Grid row = new() { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center });
        Slider slider = new()
        {
            Style = (Style)FindResource("RoundSlider"),
            Minimum = min,
            Maximum = max,
            Value = value,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        TextBlock valueText = new()
        {
            Text = format(value),
            TextAlignment = TextAlignment.Right,
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = format((int)Math.Round(e.NewValue));
            if (_syncingScreenEffect || _effectCommitTimer == null) return;
            _effectCommitTimer.Stop();
            _effectCommitTimer.Start();
        };
        _effectSliders[key] = slider;
        _effectValues[key] = valueText;
        ScreenEffectSliderPanel.Children.Add(row);
    }

    private int EffectValue(string key) => _effectSliders.TryGetValue(key, out Slider? slider) ? (int)Math.Round(slider.Value) : 0;

    private async Task CommitEffectProfileAsync()
    {
        if (_probe?.Service == null || string.IsNullOrWhiteSpace(_currentEffectMode)) return;
        DisplayEffectProfile profile = new()
        {
            Name = _currentEffectMode,
            Brightness = EffectValue("brightness"),
            ColorTemp = EffectValue("colortemp") * 100,
            ColorR = EffectValue("r"),
            ColorG = EffectValue("g"),
            ColorB = EffectValue("b"),
            VibrantValue = EffectValue("saturation") * 0.1f,
            Constrast = EffectValue("contrast") * 0.1f,
            Gamma = EffectValue("gamma") * 0.01f
        };
        bool hasGcuProfile = _probe.Hw != null && _probe.Hw.DisplayEffectProfiles.ContainsKey(_currentEffectMode);
        if (hasGcuProfile)
        {
            await _probe.Service.ApplyDisplayEffectProfile(profile);
        }
        else
        {
            MechrevoLite.Hardware.SoftwareColorEnhancement.ApplyProfile(profile.Name, profile.Brightness,
                profile.ColorTemp, profile.ColorR, profile.ColorG, profile.ColorB,
                profile.VibrantValue, profile.Constrast, profile.Gamma);
        }
    }

    private async void ScreenEffectReset_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWideBackendConnected)
        {
            StatusText.Text = WpfStrings.StatusEffectResetNeedsBackend;
            return;
        }
        if (string.IsNullOrWhiteSpace(_currentEffectMode)) return;
        bool hasGcuProfile = _probe?.Hw != null && _probe.Hw.DisplayEffectProfiles.ContainsKey(_currentEffectMode);
        if (hasGcuProfile && _probe?.Service != null)
        {
            if (await _probe.Service.ResetDisplayEffectProfile(_currentEffectMode))
            {
                await _probe.Service.RequestDisplayEffectStatus();
                await Task.Delay(500);
            }
        }
        else
        {
            MechrevoLite.Hardware.SoftwareColorEnhancement.Apply(_currentEffectMode);
        }
        BuildEffectEditor();
    }

    private void RefreshScreenControls()
    {
        if (_owner == null || ScreenCalibrationToggle == null || ScreenEffectToggle == null) return;
        (bool calibEnabled, (string Label, object? Tag)[] calibOptions, int calibSelected) = _owner.WideCalibration();
        ScreenCalibrationToggle.IsChecked = calibEnabled;
        if (_calibrationOptionButtons.Count != calibOptions.Length) BuildCalibrationOptions();
        for (int i = 0; i < _calibrationOptionButtons.Count; i++) _calibrationOptionButtons[i].IsChecked = i == calibSelected;
        foreach ((ToggleButton button, Func<bool> get, Action<bool> _) in _screenFeatures)
        {
            try { button.IsChecked = get(); } catch { }
        }
        (bool effectEnabled, (string Label, object? Tag)[] effectOptions, int effectSelected) = _owner.WideDisplayEffect();
        ScreenEffectToggle.IsChecked = effectEnabled;
        if (_effectOptionButtons.Count != effectOptions.Length) BuildEffectOptions();
        for (int i = 0; i < _effectOptionButtons.Count; i++) _effectOptionButtons[i].IsChecked = i == effectSelected;
    }

    /// <summary>⭐ task-26：宽屏后端是否已连接（BackendProbe 或迷你窗当前后端，任一在线即视为已连接）✅。</summary>
    private bool IsWideBackendConnected =>
        _probe?.Connected == true || _owner?.CurrentBackend?.Connected == true;

    /// <summary>
    /// ⭐ task-26（P2 真机前收口）：屏幕页「后端未连接门控」——
    /// 需要后端的编辑/开关/特性在未连接时**禁用 + tooltip/状态栏提示**，避免"可点但无任何效果"；
    /// ⚠️ 校色「设置」里含**本地 ICC**（导入/选中/开机应用，与 GCU 无关）⇒ 不整体禁用，只提示需要后端的部分。
    /// ⭐ 只改 IsEnabled/可见性/提示文本，⛔ 不改任何能力位、⛔ 不伪造可用性。
    /// </summary>
    private void UpdateScreenBackendGating()
    {
        // 屏幕页是按需创建的：未创建时本方法只做无操作（等 EnsureScreenView→BuildScreenControls 再调用）
        if (ScreenEffectEditButton is null || ScreenEffectToggle is null || ScreenCalibrationToggle is null)
        {
            return;
        }
        bool connected = IsWideBackendConnected;
        if (_screenBackendConnected == connected)
        {
            return;   // 状态未变化：不重复改 UI（传感器 2 s tick 会周期调用）
        }
        _screenBackendConnected = connected;
        try
        {
            string? tip = connected ? null : WpfStrings.L("后端未连接：需要 GCU/厂商服务在线后才可编辑", "Backend not connected: GCU/vendor service must be online to edit");
            ScreenEffectEditButton.IsEnabled = connected;
            ScreenEffectEditButton.ToolTip = tip;
            ScreenEffectToggle.IsEnabled = connected;
            ScreenEffectToggle.ToolTip = tip;
            ScreenEffectOptionsPanel.IsEnabled = connected;
            ScreenCalibrationToggle.IsEnabled = connected;
            ScreenCalibrationToggle.ToolTip = tip;
            ScreenCalibrationOptionsPanel.IsEnabled = connected;
            ScreenFeaturesPanel.IsEnabled = connected;
            if (ScreenCalibrationSettingsButton is not null)
            {
                ScreenCalibrationSettingsButton.ToolTip = connected
                    ? null
                    : WpfStrings.L("后端未连接：校色开关/档位需连接后生效；本地 ICC 设置仍可用", "Backend not connected: calibration switch/profile need a connection; local ICC settings still work");
            }
            if (!connected && ScreenEffectEditorPanel is not null)
            {
                ScreenEffectEditorPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // UI 门控失败不影响功能（只影响是否可点）；异常吞掉，避免影响 2 s 刷新
        }
        Logger.WriteLineIfChanged("wide-screen-backend-gate",
            connected
                ? "[wide-screen] 后端已连接 ⇒ 启用屏幕效果编辑/校色开关/档位/显示特性 ✅"
                : "[wide-screen] 后端未连接 ⇒ 禁用屏幕效果编辑/校色开关/档位/显示特性（本地 ICC 设置保留）✅");
    }

    private void UpdateChargeSliderVisibility()
    {
        if (_chargeSlider != null && _chargeExperiment != null)
        {
            _chargeSlider.Visibility = _chargeExperiment.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void HomeGpuOcToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_owner == null) return;
        bool requested = HomeGpuOcToggle.IsChecked == true;
        HomeGpuOcToggle.IsEnabled = false;
        try
        {
            bool ok = await _owner.WideSetGpuOverclockAsync(requested);
            if (!ok)
            {
                HomeGpuOcToggle.IsChecked = !requested;
                StatusText.Text = WpfStrings.GpuOverclockFailed;
            }
            else
            {
                StatusText.Text = WpfStrings.GpuOverclockPrefix + (requested ? WpfStrings.IgpuSavingOnPreview_Content : WpfStrings.OriginalStyleWindow_Content_5);
            }
        }
        finally
        {
            HomeGpuOcToggle.IsEnabled = true;
        }
    }

    private void RefreshHomeExtras()
    {
        if (_owner == null)
        {
            return;
        }
        if (_batteryModePanel != null)
        {
            _batteryModePanel.Visibility = _owner.WideBatteryModeVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        ApplyHomeCapabilities();
        if (MoreSwitchGrid != null && MoreSwitchGrid.Children.Count == 0) BuildMoreSwitchTiles();
        PerfCurrentText.Text = _owner.WidePerformanceText;
        GpuCurrentText.Text = _owner.WideGpuModeText;
        HomeGpuOcToggle.Visibility = _owner.WideGpuOverclockVisible ? Visibility.Visible : Visibility.Collapsed;
        HomeGpuOcToggle.IsChecked = _owner.WideGpuOverclockEnabled;
        bool silentTurboVisible = _owner.WideSilentTurboVisible;
        HomePerf狂暴.Visibility = _owner.WideTurboModeVisible ? Visibility.Visible : Visibility.Collapsed;
        EnsureWideSilentTurboButton(silentTurboVisible);
        EnsureWideExtremeButton(_owner.WideExtremeModeVisible);
        HomePerf狂暴.Content = silentTurboVisible ? WpfStrings.TurboHighEnergy : WpfStrings.ModeTurbo;
        if (!_owner.WideModeSwitchPending)
        {
            int currentMode = _owner.WideCurrentMode;
            bool silentTurboActive = _owner.WideSilentTurboActive;
            HomePerf静音.IsChecked = currentMode == 2;
            HomePerf平衡.IsChecked = currentMode == 0;
            if (HomePerf静音狂暴 != null) HomePerf静音狂暴.IsChecked = currentMode == 1 && silentTurboActive;
            HomePerf狂暴.IsChecked = currentMode == 1 && !silentTurboActive;
            if (HomePerf极致 != null) HomePerf极致.IsChecked = currentMode == MechrevoLite.Hardware.NucPerformanceMode.ExtremeVisualMode;
            HomePerf自定义.IsChecked = currentMode is 3 or 4;
        }
        // 按实际 GPU 模式枚举高亮，不再依赖本地化文案匹配（语言/文案变化不会失配）。
        int gpuMode = _probe?.CurrentGpuMode ?? -1;
        int pendingGpu = _probe?.PendingGpuTarget ?? -1;
        if (_probe?.HasPendingGpuRestart == true && pendingGpu >= 0)
        {
            gpuMode = pendingGpu;
        }
        if (gpuMode >= 0)
        {
            HomeGpu混合.IsChecked = gpuMode == 1;
            HomeGpu独显.IsChecked = gpuMode == 2;
            HomeGpu集显.IsChecked = gpuMode == 0;
        }
        else
        {
            string gpu = _owner.WideGpuModeText;
            HomeGpu混合.IsChecked = gpu.Contains(WpfStrings.GpuModeHybrid) || gpu.Contains(WpfStrings.GpuHybrid);
            HomeGpu独显.IsChecked = gpu.Contains(WpfStrings.GpuModeDedicated) || gpu.Contains(WpfStrings.GpuDedicated);
            HomeGpu集显.IsChecked = gpu.Contains(WpfStrings.GpuModeIntegrated) || gpu.Contains(WpfStrings.GpuIntegrated);
        }
        RefreshQuickTiles();
        (string batteryInfo, int batteryMode, string batteryCharge) = _owner.WideBattery();
        if (_batteryInfoText != null) _batteryInfoText.Text = batteryInfo;
        for (int i = 0; i < _batteryModeButtons.Count; i++)
        {
            _batteryModeButtons[i].IsChecked = i == batteryMode;
        }
        if (_chargeLimitText != null)
        {
            string chargeText = _owner.WideBattery().Charge;
            _chargeLimitText.Text = chargeText;
            _chargeLimitText.Visibility = !string.IsNullOrWhiteSpace(chargeText)
                && !string.Equals(chargeText, _chargeStatus?.Text, StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        (string info, int currentHz, int maxHz, bool auto, int brightness) = _owner.WideDisplay();
        DisplayInfoText.Text = info;
        if (_displayBrightnessText != null) _displayBrightnessText.Text = brightness + "%";
        if (_displayBrightness != null
            && !_displayBrightness.IsMouseCaptureWithin
            && Math.Abs(_displayBrightness.Value - brightness) > 1)
        {
            _displayBrightness.Value = brightness;
        }
        int[] refreshRates = _owner.WideRefreshRates();
        string refreshSignature = string.Join(",", refreshRates);
        if (refreshSignature != _refreshRatesSignature)
        {
            BuildRefreshRateButtons(refreshRates, currentHz);
        }
        else
        {
            foreach ((ToggleButton button, int hz) in _displayRefreshButtons)
            {
                button.IsChecked = Math.Abs(hz - currentHz) <= 2;
            }
        }
        if (_autoRefresh != null) _autoRefresh.IsChecked = auto;
        RefreshLiquidCooling();
        RefreshScreenControls();
        (bool limitEnabled2, string lowText2, string highText2, string limitStatus2, bool experiment2, bool ecDirect2) = _owner.WideChargeLimit();
        if (_chargeLimitSwitch != null) _chargeLimitSwitch.IsChecked = limitEnabled2;
        if (_chargeLow != null && !_chargeLow.IsKeyboardFocusWithin) _chargeLow.Text = lowText2;
        if (_chargeHigh != null && !_chargeHigh.IsKeyboardFocusWithin) _chargeHigh.Text = highText2;
        if (_chargeStatus != null) _chargeStatus.Text = limitStatus2;
        if (_chargeExperiment != null) _chargeExperiment.IsChecked = experiment2;
        if (_ecDirect != null) _ecDirect.IsChecked = ecDirect2;
        (bool sliderOn, int sliderValue) = _owner.WideBatteryLimitSlider();
        if (_chargeSlider != null)
        {
            _chargeSlider.Visibility = sliderOn ? Visibility.Visible : Visibility.Collapsed;
            if (_chargeSliderText != null)
            {
                _chargeSliderText.Visibility = sliderOn ? Visibility.Visible : Visibility.Collapsed;
                _chargeSliderText.Text = sliderValue + "%";
            }
            if (!_chargeSlider.IsMouseCaptureWithin && Math.Abs(_chargeSlider.Value - sliderValue) > 1)
            {
                _syncingChargeSlider = true;
                _chargeSlider.Value = sliderValue;
                _syncingChargeSlider = false;
            }
        }
    }

    private static bool HasAnyCustomRange(MechrevoHw? hw, MechrevoDeviceCapabilities caps)
    {
        return (hw?.Pl1Adjustable ?? false) || (hw?.Pl2Adjustable ?? false) || (hw?.Pl4Adjustable ?? false)
            || (hw?.TccAdjustable ?? false) || (hw?.GpuTgpAdjustable ?? false) || (hw?.GpuDynamicBoostAdjustable ?? false)
            || caps.CpuPerformanceTuning || caps.FanSettings;
    }

    private void ApplyHomeCapabilities()
    {
        MechrevoDeviceCapabilities? caps = _probe?.Capabilities;
        if (caps == null)
        {
            return;
        }
        bool nucUmi = _probe?.Hw?.IsIntelNucMachine == true || _probe?.Hw?.IsUmiMachine == true;
        bool nucNoGpu = _probe?.Hw?.IsIntelNucMachine == true;
        // Intel 星耀暂时屏蔽自定义；AMD 星耀/无界走本机 SMU 自定义。
        bool smuCustom = _probe?.Hw?.SmuCustomApu == true;
        bool customAllowed = nucUmi || smuCustom
            || ((caps.CpuPerformanceTuning || caps.FanSettings || HasAnyCustomRange(_probe?.Hw, caps))
                && !(_probe?.Hw?.SupportsModeProfileConfig ?? false))
            || _probe?.Hw?.UsesBaolongda == true;
        HomePerf自定义.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
        bool gpuRowHidden = _probe?.Hw?.GpuRowHiddenByGcuReports == true;
        bool directSupported = _probe?.Hw?.SupportsDgpuDirect ?? caps.DgpuDirect;
        bool gpuSwitchSupported = _probe?.Hw?.CanOfferGpuModeSwitch ?? (caps.DgpuDirect || caps.IgpuOnly || caps.GpuHotSwap);
        bool gpuSupported = !nucNoGpu && !gpuRowHidden
            && (gpuSwitchSupported || directSupported || caps.GpuHotSwap);
        HomeGpuCard.Visibility = gpuSupported ? Visibility.Visible : Visibility.Collapsed;
        HomeGpu独显.Visibility = directSupported ? Visibility.Visible : Visibility.Collapsed;
        HomeGpu集显.Visibility = (_probe?.Hw?.CanOfferIgpuOnly ?? caps.IgpuOnly)
            ? Visibility.Visible : Visibility.Collapsed;
        HomeGpuOcToggle.Visibility = _owner?.WideGpuOverclockVisible == true
            ? Visibility.Visible : Visibility.Collapsed;
        HomeGpuColumn.Width = gpuSupported ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        HomeMidColumn.Width = gpuSupported ? new GridLength(12) : new GridLength(0);
        Grid.SetColumn(HomePerfCard, 0);
        Grid.SetColumnSpan(HomePerfCard, gpuSupported ? 1 : 3);
        bool displaySupported = caps.DisplayRefresh || caps.ColorCalibration || caps.LcdOverdrive || caps.LocalDimming;
        HomeDisplayCard.Visibility = displaySupported ? Visibility.Visible : Visibility.Collapsed;
        HomeDisplayColumn.Width = displaySupported ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        HomeDisplayGap.Width = displaySupported ? new GridLength(10) : new GridLength(0);
        Grid.SetColumn(BatteryCardHost, displaySupported ? 2 : 0);
        Grid.SetColumnSpan(BatteryCardHost, displaySupported ? 1 : 3);
        bool hasLighting = caps.Keyboard || caps.Lightbar || caps.LogoLight
            || _probe?.Hw?.SupportsKeyboard == true || _probe?.Hw?.SupportsLightbar == true
            || _probe?.Hw?.SupportsLogoLight == true;
        NavLighting.Visibility = hasLighting ? Visibility.Visible : Visibility.Collapsed;
        NavCustom.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
        NavScreen.Visibility = displaySupported ? Visibility.Visible : Visibility.Collapsed;
        if (ScreenCalibrationCard != null)
        {
            ScreenCalibrationCard.Visibility = caps.ColorCalibration ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ScreenEffectCard != null)
        {
            ScreenEffectCard.Visibility = _owner?.WideDisplayEffectSupported == true ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ScreenCalibrationCard != null && ScreenEffectCard != null
            && ScreenCalibrationCard.Visibility != Visibility.Visible && ScreenEffectCard.Visibility != Visibility.Visible)
        {
            NavScreen.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildLiquidCard()
    {
        if (_owner == null || HomePage == null || _liquidCard != null)
        {
            return;
        }
        if (HomePage.Content is not StackPanel root)
        {
            return;
        }
        Border card = new()
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = _owner.WideLiquidVisible ? Visibility.Visible : Visibility.Collapsed
        };
        StackPanel panel = new();
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Viewbox icon = new()
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M8,1 C8,1 3,7 3,10.5 A5,5 0 0 0 13,10.5 C13,7 8,1 8,1 Z"),
                Fill = (Brush)FindResource("MutedBrush"),
                Stretch = Stretch.Uniform
            }
        };
        StackPanel titleRow = new() { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(icon);
        titleRow.Children.Add(new TextBlock { Text = WpfStrings.LiquidSystem, Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(titleRow, 0);
        header.Children.Add(titleRow);
        _liquidStatusButton = new Button { Content = WpfStrings.NotConnected, Style = (Style)FindResource("FlatButton"), HorizontalAlignment = HorizontalAlignment.Right };
        _liquidStatusButton.Click += async (_, _) =>
        {
            bool ok = await _owner.WideLiquidStatusActionAsync();
            if (_liquidStatusButton != null)
            {
                _liquidStatusButton.Content = _owner.WideLiquidConnected ? WpfStrings.LiquidConnected : WpfStrings.LiquidDisconnected;
            }
            StatusText.Text = ok ? WpfStrings.L("水冷状态已更新", "Liquid cooling status updated") : WpfStrings.L("刷新水冷状态失败", "Failed to refresh liquid cooling status");
        };
        Button pairButton = new()
        {
            Content = "配对",
            Style = (Style)FindResource("FlatButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        pairButton.Click += (_, _) => _owner?.WideOpenLiquidPairing();
        Grid.SetColumn(pairButton, 2);
        header.Children.Add(pairButton);
        Grid.SetColumn(_liquidStatusButton, 3);
        header.Children.Add(_liquidStatusButton);
        panel.Children.Add(header);
        Grid combos = new() { Margin = new Thickness(0, 8, 0, 0) };
        combos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        combos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        combos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        combos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock pumpLabel = new() { Text = WpfStrings.Pump, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        combos.Children.Add(pumpLabel);
        _liquidPumpCombo = CreateLiquidCombo(new[] { (WpfStrings.AutoOption, "-1"), ("45%", "0"), ("60%", "1"), ("90%", "2") });
        _liquidPumpCombo.SelectionChanged += async (_, _) => await SetLiquidComboAsync(pump: true);
        Grid.SetColumn(_liquidPumpCombo, 1);
        combos.Children.Add(_liquidPumpCombo);
        TextBlock fanLabel = new() { Text = WpfStrings.Fan, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        Grid.SetColumn(fanLabel, 2);
        combos.Children.Add(fanLabel);
        _liquidFanCombo = CreateLiquidCombo(new[] { (WpfStrings.AutoOption, "-1"), ("40%", "0"), ("50%", "1"), ("60%", "2"), ("90%", "3"), ("自定义曲线", "curve") });
        _liquidFanCombo.SelectionChanged += async (_, _) => await SetLiquidComboAsync(pump: false);
        Grid.SetColumn(_liquidFanCombo, 3);
        combos.Children.Add(_liquidFanCombo);
        panel.Children.Add(combos);
        _liquidCurveCanvas = new WaterCoolerCurveCanvas
        {
            Height = 190,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(_liquidCurveCanvas);
        Grid pumpThresholds = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pumpThresholds.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock startLabel = new() { Text = "启泵", Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        pumpThresholds.Children.Add(startLabel);
        _liquidPumpStartSlider = new Slider { Style = (Style)FindResource("RoundSlider"), Minimum = 30, Maximum = 90, Value = 55, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_liquidPumpStartSlider, 1);
        pumpThresholds.Children.Add(_liquidPumpStartSlider);
        _liquidPumpStartValue = new TextBlock { Text = "55°C", Width = 46, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(_liquidPumpStartValue, 2);
        pumpThresholds.Children.Add(_liquidPumpStartValue);
        TextBlock turboLabel = new() { Text = "狂暴", Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        Grid.SetColumn(turboLabel, 3);
        pumpThresholds.Children.Add(turboLabel);
        _liquidPumpTurboSlider = new Slider { Style = (Style)FindResource("RoundSlider"), Minimum = 35, Maximum = 100, Value = 90, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_liquidPumpTurboSlider, 4);
        pumpThresholds.Children.Add(_liquidPumpTurboSlider);
        _liquidPumpTurboValue = new TextBlock { Text = "90°C", Width = 46, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(_liquidPumpTurboValue, 5);
        pumpThresholds.Children.Add(_liquidPumpTurboValue);
        _liquidPumpStartSlider.ValueChanged += (_, _) =>
        {
            if (_liquidPumpStartValue != null) _liquidPumpStartValue.Text = (int)Math.Round(_liquidPumpStartSlider.Value) + "°C";
            if (_liquidPumpTurboSlider != null && _liquidPumpTurboSlider.Value < _liquidPumpStartSlider.Value + 5)
            {
                _liquidPumpTurboSlider.Value = _liquidPumpStartSlider.Value + 5;
            }
        };
        _liquidPumpTurboSlider.ValueChanged += (_, _) =>
        {
            if (_liquidPumpTurboValue != null) _liquidPumpTurboValue.Text = (int)Math.Round(_liquidPumpTurboSlider.Value) + "°C";
            if (_liquidPumpStartSlider != null && _liquidPumpStartSlider.Value > _liquidPumpTurboSlider.Value - 5)
            {
                _liquidPumpStartSlider.Value = Math.Max(30, _liquidPumpTurboSlider.Value - 5);
            }
        };
        panel.Children.Add(pumpThresholds);
        Grid curveActions = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        curveActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        curveActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        curveActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _liquidCurveApplyButton = new Button { Content = "应用曲线", Style = (Style)FindResource("FlatButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        _liquidCurveApplyButton.Click += async (_, _) => await ApplyWideLiquidCurveAsync();
        Grid.SetColumn(_liquidCurveApplyButton, 1);
        curveActions.Children.Add(_liquidCurveApplyButton);
        Button restore = new() { Content = "恢复默认", Style = (Style)FindResource("FlatButton"), Padding = new Thickness(12, 4, 12, 4) };
        restore.Click += (_, _) =>
        {
            _liquidCurveCanvas?.Load(WaterCoolerCurve.DefaultPoints);
            if (_liquidPumpStartSlider != null) _liquidPumpStartSlider.Value = 55;
            if (_liquidPumpTurboSlider != null) _liquidPumpTurboSlider.Value = 90;
        };
        Grid.SetColumn(restore, 2);
        curveActions.Children.Add(restore);
        panel.Children.Add(curveActions);
        _liquidCurveCanvas.Tag = curveActions;
        _liquidPumpStartSlider.Tag = pumpThresholds;
        Grid lightRow = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        lightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lightRow.Children.Add(new TextBlock
        {
            Text = WpfStrings.L("灯光", "Lighting"),
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        _liquidLightCombo = new ComboBox
        {
            Style = (Style)FindResource("RoundCombo"),
            Margin = new Thickness(0, 0, 12, 0)
        };
        _liquidLightCombo.SelectionChanged += async (_, _) => await SetLiquidLightComboAsync();
        Grid.SetColumn(_liquidLightCombo, 1);
        lightRow.Children.Add(_liquidLightCombo);
        _liquidLightColorButton = new Button
        {
            Content = WpfStrings.L("颜色", "Color"),
            // ⚠️ task-26 收口：宽屏根资源里没有 "MiniButton"（各子窗口有自己的局部同名样式）⇒
            //   原来直接 FindResource 会在宽窗构造时抛 ResourceReferenceKeyNotFoundException（启动崩溃）。
            //   ⭐ 有就用，没有回退到既有 FlatButton（⛔ 不新增/不伪造样式资源；等 MainWindow.xaml 补定义后自动恢复）。
            Style = TryFindResource("MiniButton") as Style ?? (Style)FindResource("FlatButton"),
            MinWidth = 52,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _liquidLightColorButton.Click += async (_, _) => await PickLiquidLightColorAsync();
        Grid.SetColumn(_liquidLightColorButton, 2);
        lightRow.Children.Add(_liquidLightColorButton);
        _liquidLightStatus = new TextBlock
        {
            Style = (Style)FindResource("MutedText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_liquidLightStatus, 3);
        lightRow.Children.Add(_liquidLightStatus);
        panel.Children.Add(lightRow);
        _liquidLightRow = lightRow;
        card.Child = panel;
        root.Children.Add(card);
        _liquidCard = card;
        RefreshLiquidCooling();
    }

    private static ComboBox CreateLiquidCombo((string Label, string Tag)[] items)
    {
        ComboBox combo = new() { Style = (Style)Application.Current.FindResource("RoundCombo"), Width = 110 };
        foreach ((string label, string tag) in items)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        }
        return combo;
    }

    private async Task SetLiquidComboAsync(bool pump)
    {
        if (_liquidSyncing || _owner == null)
        {
            return;
        }
        ComboBox? combo = pump ? _liquidPumpCombo : _liquidFanCombo;
        if (combo?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string tag = item.Tag as string ?? "";
        if (!pump && string.Equals(tag, "curve", StringComparison.OrdinalIgnoreCase))
        {
            _owner?.WideOpenLiquidCurve();
            return;
        }
        int profile = int.TryParse(tag, out int parsed) ? parsed : -1;
        bool ok = pump
            ? await _owner.WideSetLiquidPumpAsync(profile)
            : await _owner.WideSetLiquidFanAsync(profile);
        StatusText.Text = ok ? (pump ? WpfStrings.L("水泵档位已下发", "Pump profile sent") : WpfStrings.L("风扇档位已下发", "Fan profile sent")) : WpfStrings.L("水冷档位下发失败", "Failed to send liquid cooling profile");
    }

    private async Task SetLiquidLightComboAsync()
    {
        if (_liquidLightSyncing || _owner == null || _liquidLightCombo?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string profile = item.Tag as string ?? "";
        if (profile.Length == 0)
        {
            return;
        }
        bool ok = await _owner.WideApplyLiquidLightAsync(profile, _owner.WideLiquidLightColor);
        if (_liquidLightStatus != null)
        {
            _liquidLightStatus.Text = ok ? WpfStrings.L("灯光已下发", "Lighting sent") : WpfStrings.L("灯光下发失败", "Failed to send lighting");
        }
        StatusText.Text = ok ? WpfStrings.L("水冷灯光已下发", "Liquid cooling lighting sent") : WpfStrings.L("水冷灯光下发失败", "Failed to send liquid cooling lighting");
    }

    private async Task PickLiquidLightColorAsync()
    {
        if (_owner == null)
        {
            return;
        }
        ColorPickerWindow picker = new(_owner.WideLiquidLightColor);
        if (picker.ShowDialog() != true || picker.Result is not System.Drawing.Color color)
        {
            return;
        }
        LiquidCoolingLightService.SaveColor(color);
        string profile = _owner.WideLiquidLightProfile;
        if (!LiquidCoolingLightService.IsCustom(profile))
        {
            profile = "custom_static";
        }
        bool ok = await _owner.WideApplyLiquidLightAsync(profile, color);
        if (_liquidLightStatus != null)
        {
            _liquidLightStatus.Text = ok ? WpfStrings.L("颜色已下发", "Color sent") : WpfStrings.L("颜色下发失败", "Failed to send color");
        }
        StatusText.Text = ok ? WpfStrings.L("水冷颜色已下发", "Liquid cooling color sent") : WpfStrings.L("水冷颜色下发失败", "Failed to send liquid cooling color");
        if (_liquidLightColorButton != null)
        {
            _liquidLightColorButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
        }
    }

    internal void RefreshFromOwner()
    {
        RefreshHomeExtras();
    }

    internal void RefreshLiquidCooling()
    {
        if (_liquidCard == null || _owner == null)
        {
            return;
        }
        bool visible = _owner.WideLiquidVisible;
        _liquidCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }
        bool supported = _owner.WideLiquidSupported;
        if (_liquidStatusButton != null)
        {
            _liquidStatusButton.Content = supported
                ? (_owner.WideLiquidConnected ? WpfStrings.LiquidConnected : WpfStrings.LiquidDisconnected)
                : _owner.WideLiquidStatus;
        }
        if (_liquidPumpCombo != null) _liquidPumpCombo.IsEnabled = supported;
        if (_liquidFanCombo != null) _liquidFanCombo.IsEnabled = supported;
        _liquidSyncing = true;
        SelectLiquidCombo(_liquidPumpCombo, _owner.WideLiquidPump);
        SelectLiquidCombo(_liquidFanCombo, _owner.WideLiquidFan);
        _liquidSyncing = false;
        if (_liquidCurveCanvas != null)
        {
            bool curveSupported = _owner.WideLiquidCurveSupported;
            _liquidCurveCanvas.Visibility = Visibility.Visible;
            _liquidCurveCanvas.Load(_owner.WideLiquidCurvePoints);
            if (_liquidCurveCanvas.Tag is Grid actions)
            {
                actions.Visibility = Visibility.Visible;
            }
            if (_liquidCurveApplyButton != null)
            {
                _liquidCurveApplyButton.IsEnabled = curveSupported;
            }
            if (_liquidPumpStartSlider != null && _liquidPumpStartSlider.Tag is Grid thresholds)
            {
                thresholds.Visibility = Visibility.Visible;
                _liquidPumpStartSlider.IsEnabled = curveSupported;
            }
            if (_liquidPumpTurboSlider != null)
            {
                _liquidPumpTurboSlider.IsEnabled = curveSupported;
            }
            if (_liquidPumpStartSlider != null)
            {
                _liquidPumpStartSlider.Value = WaterCoolerCurve.PumpStartTemp;
            }
            if (_liquidPumpTurboSlider != null)
            {
                _liquidPumpTurboSlider.Value = WaterCoolerCurve.PumpTurboTemp;
            }
        }
        bool lightSupported = _owner.WideLiquidLightSupported;
        if (_liquidLightRow != null)
        {
            // 宽屏水冷卡可见时，灯光行始终可见；未连接时置灰。
            _liquidLightRow.Visibility = Visibility.Visible;
        }
        if (!lightSupported)
        {
            _liquidLightSyncing = true;
            try
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
            }
            finally
            {
                _liquidLightSyncing = false;
            }
        }
        if (lightSupported)
        {
            IReadOnlyList<LiquidLightOption> options = _owner.WideLiquidLightOptions;
            string saved = _owner.WideLiquidLightProfile;
            int selected = 0;
            _liquidLightSyncing = true;
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
                    System.Drawing.Color color = _owner.WideLiquidLightColor;
                    _liquidLightColorButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
                    _liquidLightColorButton.IsEnabled = true;
                }
                if (_liquidLightStatus != null)
                {
                    _liquidLightStatus.Text = string.IsNullOrWhiteSpace(saved) ? "" : WpfStrings.L("已保存：", "Saved: ") + saved;
                }
            }
            finally
            {
                _liquidLightSyncing = false;
            }
        }
    }

    private async System.Threading.Tasks.Task ApplyWideLiquidCurveAsync()
    {
        if (_owner == null || _liquidCurveCanvas == null)
        {
            return;
        }
        int start = (int)Math.Round(_liquidPumpStartSlider?.Value ?? 55);
        int turbo = (int)Math.Round(_liquidPumpTurboSlider?.Value ?? 90);
        bool ok = await _owner.WideSetLiquidCurveAsync(_liquidCurveCanvas.Points, start, turbo);
        StatusText.Text = ok ? "水冷曲线已应用" : "水冷曲线应用失败：请确认 BLE 水冷已连接";
    }

    private static void SelectLiquidCombo(ComboBox? combo, int value)
    {
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item
                && int.TryParse(item.Tag as string, out int tag)
                && tag == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // -2/未知档位时回退到第一个“自动”，不要留下空下拉
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static bool ResolveWideLightVisible(string key, bool supported)
    {
        string mode = AppConfig.GetString(key) ?? "auto";
        if (string.Equals(mode, "show", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(mode, "hide", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return supported;
    }

    internal void RefreshCustomModeEditor()
    {
        if (_customModeInlineWindow is CustomModeWindow editor)
        {
            try
            {
                string id = _owner?.WideCustomModeId ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    editor.BindMode(id);
                    if (_cpuFanInlineWindow is FanCurveWindow cpuFan) cpuFan.SetModeId(id);
                    if (_gpuFanInlineWindow is FanCurveWindow gpuFan) gpuFan.SetModeId(id);
                }
            }
            catch
            {
            }
        }
    }

    private void ApplyWideUiVisibilityFromOwner()
    {
        if (Dispatcher.CheckAccess())
        {
            BuildLightingCards();
            RefreshIgpuSaving();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BuildLightingCards();
                RefreshIgpuSaving();
            }));
        }
    }

    private void BuildLightingCards()
    {
        if (LightingCardsGrid == null)
        {
            return;
        }
        // ⭐ task-17：重建前先清空（OnBackendReady 会再次调用本方法；原来不清空导致灯光卡整组重复 ❌）
        // ⭐ task-26：先把旧卡的合并计时器停掉再拆 UI，避免旧卡 pending 的 slider 值在重建后补发 ✅
        foreach (DispatcherTimer timer in _lightCardApplyTimers)
        {
            timer.Stop();
        }
        _lightCardApplyTimers.Clear();
        foreach (LightEditorWindow oldEditor in _inlineLightEditors)
        {
            oldEditor.Close();
        }
        _inlineLightEditors.Clear();
        LightingCardsGrid.Children.Clear();
        BuildLightingGlobalControls();
        MechrevoHw? hw = _probe?.Hw ?? WpfBackend.Hw;
        // ⭐⭐ task-17/task-26（宽屏 HID / 白色背光入口）：能力判定与迷你窗同判据；
        //    判定异常/无设备一律按「不支持」处理 ⇒ 不建对应入口、⛔ 不伪造能力 ✅
        bool hidKeyboard = false;
        bool whiteKeyboard = false;
        try
        {
            hidKeyboard = WideIsHidKeyboard(hw);
            whiteKeyboard = WideIsWhiteBacklightKeyboard(hw);
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 键盘能力判定异常（按不支持处理，⛔ 不伪造能力）：" + ex);
        }
        LogKeyboardCapabilityDecision(hw, hidKeyboard, whiteKeyboard);
        (string Effect, string Name)[] keyboardEffects;
        try
        {
            keyboardEffects = _owner?.WideKeyboardEffects() ?? Array.Empty<(string, string)>();
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 读取键盘效果表失败（本次不建键盘卡，⛔ 不伪造能力）：" + ex);
            keyboardEffects = Array.Empty<(string, string)>();
        }
        if (hw?.UsesBaolongda == true && keyboardEffects.Length > 0)
        {
            // ⭐ task-17：与迷你窗 EffectsForCurrentBackend 同口径（宝龙达固件只支持 单色/彩虹；
            //    能力位驱动、⛔ 不硬编码机型）⇒ 宽屏内联卡与完整编辑器都不再露出无效效果 ✅
            keyboardEffects = new[]
            {
                ("Single", MechrevoLite.Properties.Strings.EffectSingle),
                ("Rainbow", MechrevoLite.Properties.Strings.EffectRainbow)
            };
        }
        var channels = new List<(string Topic, string Title, (string Effect, string Name)[] Effects)>
        {
            ("Keyboard/Ctrl", WpfStrings.LightingKeyboard, keyboardEffects),
            ("HidLightbar/Ctrl", WpfStrings.LightingStrip, hw?.IsIntelNucMachine == true
                ? new[] { ("Single", MechrevoLite.Properties.Strings.EffectSingle), ("Rainbow", MechrevoLite.Properties.Strings.EffectRainbow) }
                : LightingCatalog.LightbarEffects),
            ("HidLightbar_Logo/Ctrl", WpfStrings.LightingLogo,
                hw is not null && (hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40
                    || hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30)
                    ? LightingCatalog.LogoEffectsGen40 : LightingCatalog.CurrentLogoEffects()),
            ("HidLightbar_Hinge/Ctrl", WpfStrings.LightingHinge, LightingCatalog.HingeEffects),
            ("HidLightbar_Sync/Ctrl", WpfStrings.LightingSync, LightingCatalog.SyncEffects)
        };
        bool hasKeyboard = hw?.SupportsKeyboard ?? false;
        bool hasLightbar = hw?.SupportsLightbar ?? false;
        bool hasLogo = hw?.SupportsLogoLight ?? false;
        bool hasHinge = hw?.SupportsHingeLight ?? false;
        bool hasSync = hw?.SupportsSyncLight ?? false;
        foreach ((string topic, string title, (string Effect, string Name)[] effects) in channels)
        {
            bool supported = topic switch
            {
                "Keyboard/Ctrl" => hasKeyboard,
                "HidLightbar/Ctrl" => ResolveWideLightVisible("light_strip_ui", hasLightbar),
                "HidLightbar_Logo/Ctrl" => ResolveWideLightVisible("light_logo_ui", hasLogo),
                "HidLightbar_Hinge/Ctrl" => hasHinge,
                _ => hasSync
            };
            if (effects.Length > 0 && supported)
            {
                bool isKeyboard = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
                LightingCardsGrid.Children.Add(BuildLightCard(
                    topic,
                    title,
                    effects,
                    hidKeyboard: isKeyboard && hidKeyboard,
                    whiteKeyboard: isKeyboard && whiteKeyboard));
            }
        }
    }

    private void BuildLightingGlobalControls()
    {
        if (_owner == null || LightingIdlePanel == null) return;
        LightingIdlePanel.Children.Clear();
        _lightingIdleButtons.Clear();
        int current = _owner.WideLightIdleSeconds();
        foreach ((string label, int seconds) in _owner.WideLightIdleOptions())
        {
            int target = seconds;
            ToggleButton button = new()
            {
                Content = label,
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = seconds == current,
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Click += (_, _) =>
            {
                _owner.WideSetLightIdleSeconds(target);
                foreach (ToggleButton item in _lightingIdleButtons) item.IsChecked = ReferenceEquals(item, button);
            };
            _lightingIdleButtons.Add(button);
            LightingIdlePanel.Children.Add(button);
        }
        LightingOffBatteryToggle.IsChecked = _owner.WideLightOffOnBattery;
    }

    private void LightingOffBatteryToggle_Click(object sender, RoutedEventArgs e)
    {
        _owner?.WideSetLightOffOnBattery(LightingOffBatteryToggle.IsChecked == true);
    }

    private Border BuildInlineEditorCard(string topic, string title, (string Effect, string Name)[] effects, bool whiteBacklightOnly)
    {
        LightEditorWindow editor = new(topic, title, effects, whiteBacklightOnly);
        editor.PrepareWideInline();
        UIElement? editorContent = editor.Content as UIElement;
        editor.Content = null;
        ToggleButton power = new()
        {
            Content = WpfStrings.PowerOn,
            Style = (Style)FindResource("SegmentButton"),
            IsChecked = editor.PowerOn,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        power.Click += (_, _) => editor.SetPower(power.IsChecked == true);
        StackPanel left = new();
        StackPanel header = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(power);
        left.Children.Add(header);
        left.Children.Add(new TextBlock
        {
            Text = WpfStrings.LightEditorWindow_xaml_Text,
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid layout = new();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        layout.Children.Add(left);
        if (editorContent != null)
        {
            Grid.SetColumn(editorContent, 2);
            layout.Children.Add(editorContent);
        }
        _inlineLightEditors.Add(editor);
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10), Child = layout };
    }

    private Border BuildLightCard(string topic, string title, (string Effect, string Name)[] effects, bool hidKeyboard = false, bool whiteKeyboard = false)
    {
        // 非 HID 通道直接内联完整编辑器（原二级菜单已验证可下发），不再由宽屏自绘一套控件。
        if (!hidKeyboard)
        {
            return BuildInlineEditorCard(topic, title, effects, whiteKeyboard);
        }
        LightChannelSettings settings = LightingSettingsStore.Load(topic, effects[0].Effect);
        bool keyboardChannel = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
        bool gradeLightChannel = IsGradeLightChannel(topic, _probe?.Hw ?? WpfBackend.Hw);
        int maxLight = whiteKeyboard ? 2 : (keyboardChannel || gradeLightChannel ? 4 : 100);
        int light = whiteKeyboard
            ? (settings.Light >= 3 ? 2 : Math.Clamp(settings.Light, 1, 2))
            : gradeLightChannel || keyboardChannel
                ? Math.Clamp(settings.Light, 0, 4)
                : Math.Clamp(settings.Light * 25, 0, 100);
        int colorArgb = settings.ColorArgb;
        int breathingIndex = Math.Clamp(settings.BreathingIndex, 1, 4);
        bool singleZoneKeyboard = topic == "Keyboard/Ctrl"
            && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is { } keyboardHw
            && MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleZoneRgb(keyboardHw.Capabilities.KeyboardType);
        Slider? sliderRef = null;
        Slider? speedRef = null;
        bool powerOn = settings.PowerOn;
        string selectedEffect = Array.Exists(effects, item => item.Effect == settings.Effect) ? settings.Effect : effects[0].Effect;
        if (hidKeyboard && WpfBackend.rgb is { } hidState)
        {
            // ⭐ task-17：HID 键盘卡初始状态以 KeyboardRgb 配置为准（与迷你窗 FillKeyboardEffects 同口径）✅
            string hidEffect = "hid:" + hidState.KbHidMode;
            if (Array.Exists(effects, item => item.Effect == hidEffect))
            {
                selectedEffect = hidEffect;
            }
            powerOn = hidState.KbPowerOn;
            light = Math.Clamp(MechrevoLite.Hardware.KeyboardRgb.MapSoftwareBrightnessToHardwareLevel(hidState.Brightness), 0, maxLight);
        }
        ToggleButton? powerRef = null;
        Action<string>? updateEffectUi = null;
        bool lightUiSyncing = false;
        StackPanel colorSection = new();
        StackPanel mixSection = new();
        WrapPanel mixPanel = new();
        StackPanel breathSection = new();
        WrapPanel breathPanel = new();
        List<Button> breathButtons = new();
        List<Button> mixButtons = new();
        StackPanel manualSection = new();
        WrapPanel manualPanel = new();
        List<Button> manualButtons = new();
        int[] manualIndexes = (settings.ManualIndexes ?? LightChannelSettings.DefaultManualIndexes).ToArray();
        int[] mixColors = (LightingSettingsStore.Load(topic, "ColorMarquee").MixColors ?? LightChannelSettings.DefaultMixColors).ToArray();
        void RefreshMixButtons()
        {
            for (int i = 0; i < mixColors.Length && i < mixButtons.Count; i++)
            {
                System.Drawing.Color c = System.Drawing.Color.FromArgb(mixColors[i]);
                mixButtons[i].Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                mixButtons[i].ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }
        void SaveMixColors()
        {
            try
            {
                LightChannelSettings current = LightingSettingsStore.Load(topic, selectedEffect) with { MixColors = (int[])mixColors.Clone() };
                LightingSettingsStore.Save(topic, current);
                LightChannelSettings marquee = LightingSettingsStore.Load(topic, "ColorMarquee") with { MixColors = (int[])mixColors.Clone() };
                LightingSettingsStore.Save(topic, marquee);
            }
            catch
            {
            }
        }
        DispatcherTimer lightApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        lightApplyTimer.Tick += (_, _) =>
        {
            lightApplyTimer.Stop();
            _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, powerRef?.IsChecked == true, channelPowerOsd: true);
        };
        _lightCardApplyTimers.Add(lightApplyTimer);

        Grid layout = new();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

        StackPanel left = new();
        ToggleButton power = new()
        {
            Content = WpfStrings.PowerOn,
            Style = (Style)FindResource("SegmentButton"),
            IsChecked = powerOn,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel header = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(power);
        powerRef = power;
        left.Children.Add(header);
        TextBlock hint = new()
        {
            Text = WpfStrings.AdjustHint,
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 4, 0, 10)
        };
        left.Children.Add(hint);
        WrapPanel effectPanel = new();
        List<ToggleButton> effectButtons = new();
        foreach ((string effect, string name) in effects)
        {
            string effectId = effect;
            ToggleButton button = new()
            {
                Content = name,
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = effect == selectedEffect,
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Click += (_, _) =>
            {
                selectedEffect = effectId;
                foreach (ToggleButton item in effectButtons) item.IsChecked = ReferenceEquals(item, button);
                updateEffectUi?.Invoke(effectId);
                _ = ApplyLightCardAsync(topic, effectId, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
            };
            effectButtons.Add(button);
            effectPanel.Children.Add(button);
        }
        left.Children.Add(effectPanel);
        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        StackPanel right = new() { VerticalAlignment = VerticalAlignment.Center };
        Border swatch = new()
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            BorderBrush = (Brush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255,
                (byte)((colorArgb >> 16) & 0xFF), (byte)((colorArgb >> 8) & 0xFF), (byte)(colorArgb & 0xFF)))
        };
        TextBlock colorLabel = new()
        {
            Text = "#" + colorArgb.ToString("X8"),
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel colorContent = new() { Orientation = Orientation.Horizontal };
        colorContent.Children.Add(swatch);
        colorContent.Children.Add(colorLabel);
        Button color = new()
        {
            Content = colorContent,
            Style = (Style)FindResource("GhostButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 8)
        };
        color.Click += (_, _) =>
        {
            ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(colorArgb)) { Owner = this };
            if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
            {
                colorArgb = picked.ToArgb();
                swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, picked.R, picked.G, picked.B));
                colorLabel.Text = "#" + colorArgb.ToString("X8");
                _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
            }
        };
        WrapPanel presets = new() { Margin = new Thickness(0, 0, 0, 8) };
        (string Name, int Argb)[] palette =
        {
            (WpfStrings.ColorRed, unchecked((int)0xFFFF3B30)), (WpfStrings.ColorOrange, unchecked((int)0xFFFF9500)),
            (WpfStrings.ColorYellow, unchecked((int)0xFFFFD60A)), (WpfStrings.ColorGreen, unchecked((int)0xFF34C759)),
            (WpfStrings.ColorCyan, unchecked((int)0xFF32ADE6)), (WpfStrings.ColorBlue, unchecked((int)0xFF007AFF)),
            (WpfStrings.ColorPurple, unchecked((int)0xFFAF52DE)), (WpfStrings.ColorWhite, unchecked((int)0xFFFFFFFF))
        };
        foreach ((string name, int argbValue) in palette)
        {
            int presetArgb = argbValue;
            Button preset = new()
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255,
                    (byte)((presetArgb >> 16) & 0xFF), (byte)((presetArgb >> 8) & 0xFF), (byte)(presetArgb & 0xFF))),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = name
            };
            preset.Click += (_, _) =>
            {
                colorArgb = presetArgb;
                swatch.Background = preset.Background;
                colorLabel.Text = "#" + colorArgb.ToString("X8");
                _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
            };
            presets.Children.Add(preset);
        }
        colorSection.Children.Add(color);
        colorSection.Children.Add(presets);
        for (int i = 0; i < 7; i++)
        {
            int index = i;
            System.Drawing.Color c = System.Drawing.Color.FromArgb(mixColors[index]);
            Button mixSwatch = new()
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            };
            mixSwatch.Click += (_, _) =>
            {
                ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(mixColors[index])) { Owner = this };
                if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
                {
                    mixColors[index] = picked.ToArgb();
                    RefreshMixButtons();
                    SaveMixColors();
                    _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
                }
            };
            mixButtons.Add(mixSwatch);
            mixPanel.Children.Add(mixSwatch);
        }
        mixSection.Children.Add(new TextBlock { Text = WpfStrings.LightEditorWindow_Text_5, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 4) });
        mixSection.Children.Add(mixPanel);
        for (int i = 0; i < 4; i++)
        {
            int index = i + 1;
            System.Drawing.Color c = System.Drawing.Color.FromArgb(MechrevoLite.Hardware.LightChannelSettings.BreathingPalette[i]);
            Button breathSwatch = new()
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(index == breathingIndex ? 2 : 0)
            };
            breathSwatch.Click += (_, _) =>
            {
                breathingIndex = index;
                for (int j = 0; j < breathButtons.Count; j++)
                {
                    breathButtons[j].BorderThickness = new Thickness(j + 1 == breathingIndex ? 2 : 0);
                }
                _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
            };
            breathButtons.Add(breathSwatch);
            breathPanel.Children.Add(breathSwatch);
        }
        breathSection.Children.Add(new TextBlock { Text = WpfStrings.LightBreathingColor, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 4) });
        breathSection.Children.Add(breathPanel);
        void RefreshManualButtons()
        {
            for (int i = 0; i < manualButtons.Count && i < manualIndexes.Length; i++)
            {
                System.Drawing.Color c = System.Drawing.Color.FromArgb(
                    LightChannelSettings.ManualPalette[Math.Clamp(manualIndexes[i], 1, 29) - 1]);
                manualButtons[i].Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                manualButtons[i].ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            manualIndexes[index] = Math.Clamp(manualIndexes[index], 1, 29);
            System.Drawing.Color c = System.Drawing.Color.FromArgb(
                LightChannelSettings.ManualPalette[manualIndexes[index] - 1]);
            Button manualSwatch = new()
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            };
            manualSwatch.Click += (_, _) =>
            {
                ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(
                    LightChannelSettings.ManualPalette[Math.Clamp(manualIndexes[index], 1, 29) - 1])) { Owner = this };
                if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
                {
                    manualIndexes[index] = NearestManualPaletteIndex(picked);
                    RefreshManualButtons();
                    _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
                }
            };
            manualButtons.Add(manualSwatch);
            manualPanel.Children.Add(manualSwatch);
        }
        manualSection.Children.Add(new TextBlock { Text = WpfStrings.L("手动分区颜色", "Manual zone colors"), Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 4) });
        manualSection.Children.Add(manualPanel);
        right.Children.Add(colorSection);
        right.Children.Add(mixSection);
        right.Children.Add(breathSection);
        right.Children.Add(manualSection);
        Slider slider = new() { Style = (Style)FindResource("RoundSlider"), Minimum = whiteKeyboard ? 1 : 0, Maximum = maxLight, Value = light };
        sliderRef = slider;
        TextBlock value = new()
        {
            Text = light + "/" + maxLight,
            Style = (Style)FindResource("MutedText"),
            Width = 48,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid brightness = new() { Margin = new Thickness(0, 2, 0, 8) };
        brightness.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        brightness.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        brightness.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock brightnessLabel = new() { Text = WpfStrings.Brightness, Style = (Style)FindResource("MutedText"), MinWidth = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)) { brightnessLabel.Width = 42; }
        brightness.Children.Add(brightnessLabel);
        Grid.SetColumn(slider, 1);
        brightness.Children.Add(slider);
        Grid.SetColumn(value, 2);
        brightness.Children.Add(value);
        right.Children.Add(brightness);
        Slider speed = new()
        {
            Style = (Style)FindResource("RoundSlider"),
            Minimum = 0,
            Maximum = 4,
            Value = Math.Clamp(settings.Speed, 0, 4)
        };
        speedRef = speed;
        Grid speedRow = new() { Margin = new Thickness(0, 0, 0, 0) };
        speedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        speedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        speedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock speedLabel = new() { Text = WpfStrings.Speed, Style = (Style)FindResource("MutedText"), MinWidth = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)) { speedLabel.Width = 42; }
        TextBlock speedText = new()
        {
            Text = (int)speed.Value + "/4",
            Style = (Style)FindResource("MutedText"),
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        speedRow.Children.Add(speedLabel);
        Grid.SetColumn(speed, 1);
        speedRow.Children.Add(speed);
        Grid.SetColumn(speedText, 2);
        speedRow.Children.Add(speedText);
        right.Children.Add(speedRow);
        speed.ValueChanged += (_, _) =>
        {
            speedText.Text = (int)Math.Round(speed.Value) + "/" + (int)speed.Maximum;
            if (lightUiSyncing) return;
            lightApplyTimer.Stop();
            lightApplyTimer.Start();
        };
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);
        power.Click += (_, _) =>
        {
            _ = ApplyLightCardAsync(topic, selectedEffect, sliderRef!, speedRef!, colorArgb, mixColors, breathingIndex, manualIndexes, power.IsChecked == true);
        };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = (int)Math.Round(slider.Value) + "/" + maxLight;
            if (lightUiSyncing) return;
            lightApplyTimer.Stop();
            lightApplyTimer.Start();
        };
        updateEffectUi = effect =>
        {
            lightUiSyncing = true;
            try
            {
                bool hidEffect = effect.StartsWith("hid:", StringComparison.Ordinal);
                int hidColorMode = -1;
                bool hidColorEffect = hidEffect
                    && int.TryParse(effect.AsSpan(4), out hidColorMode) && hidColorMode is 0 or 9;
                bool manual = keyboardChannel && !hidEffect && string.Equals(effect, "Manual", StringComparison.Ordinal);
                bool breathPalette = keyboardChannel && !hidEffect && singleZoneKeyboard && string.Equals(effect, "Breathing", StringComparison.Ordinal);
                bool singleColor;
                bool mixColor;
                if (hidEffect)
                {
                    singleColor = hidColorEffect;
                    mixColor = false;
                }
                else if (gradeLightChannel)
                {
                    int colorMode = LightingCatalog.LightEffectColorMode(effect);
                    singleColor = colorMode == 1;
                    mixColor = colorMode == 7;
                }
                else if (keyboardChannel)
                {
                    singleColor = !manual && !breathPalette
                        && !string.Equals(effect, "Rainbow", StringComparison.Ordinal)
                        && MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleColorEffect(effect);
                    mixColor = false;
                }
                else
                {
                    // 老 LightForm：非 Gen40 灯条/Logo/铰链/同步没有七色混色；
                    // 单色系灯效（含呼吸/波浪/冲击等）走一个单色选择器。
                    singleColor = !breathPalette && MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleColorEffect(effect);
                    mixColor = false;
                }
                bool speedApplicable = EffectSupportsSpeed(topic, effect, hidEffect);
                (int speedMin, int speedMax, string speedLabelText) = EffectSpeedInfo(topic, effect);
                colorSection.Visibility = singleColor ? Visibility.Visible : Visibility.Collapsed;
                mixSection.Visibility = mixColor ? Visibility.Visible : Visibility.Collapsed;
                breathSection.Visibility = breathPalette ? Visibility.Visible : Visibility.Collapsed;
                manualSection.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
                speedRow.Visibility = speedApplicable ? Visibility.Visible : Visibility.Collapsed;
                speedLabel.Text = speedLabelText;
                LightChannelSettings effectSettings = LightingSettingsStore.Load(topic, effect);
                if (breathPalette)
                {
                    breathingIndex = Math.Clamp(effectSettings.BreathingIndex, 1, 4);
                    for (int j = 0; j < breathButtons.Count; j++)
                    {
                        breathButtons[j].BorderThickness = new Thickness(j + 1 == breathingIndex ? 2 : 0);
                    }
                }
                if (manual)
                {
                    manualIndexes = (effectSettings.ManualIndexes ?? LightChannelSettings.DefaultManualIndexes).ToArray();
                    for (int j = 0; j < manualIndexes.Length; j++)
                    {
                        manualIndexes[j] = Math.Clamp(manualIndexes[j], 1, 29);
                    }
                    RefreshManualButtons();
                }
                if (mixColor && effectSettings.MixColors is { Length: 7 } savedMix)
                {
                    mixColors = (int[])savedMix.Clone();
                    RefreshMixButtons();
                }
                int effectLight = gradeLightChannel || keyboardChannel
                    ? effectSettings.Light
                    : Math.Clamp(effectSettings.Light * 25, 0, 100);
                slider.Value = Math.Clamp(effectLight, 0, maxLight);
                speed.Minimum = speedMin;
                speed.Maximum = speedMax;
                speed.Value = Math.Clamp(effectSettings.Speed, speedMin, speedMax);
                colorArgb = effectSettings.EffectiveColorArgb();
                swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255,
                    (byte)((colorArgb >> 16) & 0xFF), (byte)((colorArgb >> 8) & 0xFF), (byte)(colorArgb & 0xFF)));
                colorLabel.Text = "#" + colorArgb.ToString("X8");
                value.Text = (int)Math.Round(slider.Value) + "/" + maxLight;
                speedText.Text = (int)Math.Round(speed.Value) + "/" + speedMax;
            }
            finally
            {
                lightUiSyncing = false;
            }
        };
        updateEffectUi(selectedEffect);
        if (whiteKeyboard)
        {
            // task-17：白色背光键盘按能力只保留「电源 + 2 档亮度」，⛔ 不显示不支持的 RGB 效果/颜色/速度
            hint.Text = WpfStrings.LightWhiteKeyboardHint;
            effectPanel.Visibility = Visibility.Collapsed;
            colorSection.Visibility = Visibility.Collapsed;
            mixSection.Visibility = Visibility.Collapsed;
            breathSection.Visibility = Visibility.Collapsed;
            manualSection.Visibility = Visibility.Collapsed;
            speedRow.Visibility = Visibility.Collapsed;
        }
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10), Child = layout };
    }

    /// <summary>
    /// ⭐⭐ task-17（宽屏 HID / 完整灯光编辑器入口）：与迷你窗 <c>KeyboardLightEdit_Click</c> 同判据 —
    /// HID 可达（<see cref="KeyboardLightPathPolicy" />）⇒ HID 编辑器；非 HID 固件键盘 ⇒ 完整 <see cref="LightEditorWindow" />。
    /// </summary>
    private static bool WideIsHidKeyboard(MechrevoHw? hw)
    {
        try
        {
            if (hw == null
                || hw.UsesBaolongda
                || hw.WujieSmuApu
                || KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(hw.Capabilities.KeyboardType))
            {
                return false;
            }
            KeyboardRgb? rgb = WpfBackend.rgb;
            if (rgb != null
                && KeyboardLightPathPolicy.ShouldUseGcuKeyboardFallback(
                    rgb.ControllerAvailability,
                    rgb.IsConnected,
                    WpfBackend.Service != null && hw.IsConnected,
                    hidBrightnessTookEffect: true))
            {
                return false;
            }
            return true;
        }
        catch
        {
            // task-26：能力读取异常 ⇒ 按「非 HID」处理（⛔ 不伪造 HID 能力；入口不出现，页面不崩）
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ task-17：白色背光键盘判据（能力位驱动，不硬编码机型）：
    /// 非 HID、非宝龙达；无界 APU 白键盘，或固件效果表为空（<see cref="KeyboardFirmwareEffects.Visible" />）。
    /// 这类键盘只有「电源 + 4 档亮度」，⛔ 没有 RGB 效果/颜色 ✅。
    /// </summary>
    private static bool WideIsWhiteBacklightKeyboard(MechrevoHw? hw)
    {
        try
        {
            if (hw == null || !hw.SupportsKeyboard || WideIsHidKeyboard(hw) || hw.UsesBaolongda)
            {
                return false;
            }
            if (hw.WujieSmuApu)
            {
                return true;
            }
            return KeyboardFirmwareEffects.Visible(hw.Capabilities.KeyboardType).Length == 0;
        }
        catch
        {
            // task-26：能力读取异常 ⇒ 按「非白色背光」处理（⛔ 不伪造能力；不隐藏入口，页面不崩）
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ task-26：补齐键盘能力位判定日志，供验收/真机从日志确认「HID/白色背光入口为什么出现或不出现」。
    /// ⚠️ 只写日志（WriteLineIfChanged 去重），⛔ 不改任何可见性/能力值 ✅。
    /// </summary>
    private void LogKeyboardCapabilityDecision(MechrevoHw? hw, bool hidKeyboard, bool whiteKeyboard)
    {
        try
        {
            int keyboardType = -1;
            int firmwareVisible = -1;
            string solution = "";
            bool supportsKeyboard = false;
            bool usesBaolongda = false;
            bool wujie = false;
            bool gcuZonePath = false;
            if (hw != null)
            {
                keyboardType = hw.Capabilities.KeyboardType;
                solution = hw.KeyboardSolution;
                supportsKeyboard = hw.SupportsKeyboard;
                usesBaolongda = hw.UsesBaolongda;
                wujie = hw.WujieSmuApu;
                gcuZonePath = KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(keyboardType);
                firmwareVisible = KeyboardFirmwareEffects.Visible(keyboardType).Length;
            }
            string reason = hw == null
                ? "hw=null（后端尚未就绪）"
                : !supportsKeyboard
                    ? "SupportsKeyboard=false（无键盘灯能力）"
                    : usesBaolongda
                        ? "UsesBaolongda=true（固件键盘，走单色/彩虹）"
                        : wujie
                            ? "WujieSmuApu=true（白色背光：仅电源+亮度）"
                            : gcuZonePath
                                ? "IsGcuZoneKeyboardPath=true（固件/HID 回退路径）"
                                : hidKeyboard ? "HID 可达（KeyboardRgb 已连接且未回退）" : "非 HID 固件键盘";
            Logger.WriteLineIfChanged("wide-light-keyboard-cap",
                $"[wide-light] 键盘能力判定：hid={hidKeyboard} white={whiteKeyboard} hw={(hw == null ? "null" : "ok")}"
                + $" connected={IsWideBackendConnected} KeyboardType={keyboardType} solution={solution}"
                + $" firmwareVisible={firmwareVisible} rgb={(WpfBackend.rgb == null ? "null" : "ok")} 原因={reason}");
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 键盘能力判定日志失败（已吞掉，不影响入口）：" + ex.GetType().Name);
        }
    }

    /// <summary>⭐ task-17：通道能力判据与 <c>OriginalStyleWindow.WideApplyLightAsync</c> 同一套（不硬编码）✅。</summary>
    private bool WideLightTopicSupported(string topic)
    {
        MechrevoHw? hw = _probe?.Hw ?? WpfBackend.Hw;
        BackendProbe? probe = _probe;
        return topic switch
        {
            "Keyboard/Ctrl" => hw?.SupportsKeyboard ?? false,
            "HidLightbar/Ctrl" => probe?.SupportsLightbar ?? false,
            "HidLightbar_Logo/Ctrl" => hw?.SupportsLogoLight == true,
            "HidLightbar_Hinge/Ctrl" => probe?.SupportsHingeLight ?? false,
            "HidLightbar_Sync/Ctrl" => probe?.SupportsSyncLight ?? false,
            _ => false
        };
    }

    /// <summary>⭐ task-17：灯光卡「编辑 / HID 编辑」入口；不支持该通道时不创建（⛔ 不做可点但无通路控件）✅。</summary>
    private Button? BuildLightEditorButton(string topic, string title, (string Effect, string Name)[] effects, bool hidKeyboard, bool whiteKeyboard)
    {
        if (!WideLightTopicSupported(topic))
        {
            return null;
        }
        MechrevoHw? hw = _probe?.Hw ?? WpfBackend.Hw;
        if (hw?.UsesBaolongda == true && !string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
        {
            // ⭐ task-17：与迷你窗等价 —— 宝龙达下非键盘通道只有开/关（效果列/编辑入口本来就不给）✅
            return null;
        }
        Button button = new()
        {
            Content = hidKeyboard ? WpfStrings.L("HID 编辑", "HID editor") : WpfStrings.LightSyncEditButton_Content,
            Style = (Style)FindResource("GhostButton"),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            if (hidKeyboard)
            {
                OpenWideHidKeyboardEditor(title);
            }
            else if (whiteKeyboard)
            {
                OpenWideLightEditor(topic, title, Array.Empty<(string, string)>(), whiteBacklightOnly: true);
            }
            else
            {
                OpenWideLightEditor(topic, title, effects);
            }
        };
        return button;
    }

    /// <summary>⭐ task-17：打开与迷你窗等价的完整灯光编辑器（颜色/亮度/速度/电源/七色/单分区四色全部保留）✅。</summary>
    private void OpenWideLightEditor(string topic, string title, (string Effect, string Name)[] effects, bool whiteBacklightOnly = false)
    {
        try
        {
            Logger.WriteLine("[wide-light] 内联完整灯光编辑器：" + topic + " white=" + whiteBacklightOnly);
            WpfBackend.Attach(_probe?.Hw, _probe?.Service);
            MountInlineLightEditor(new LightEditorWindow(topic, title, effects, whiteBacklightOnly));
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 打开完整灯光编辑器失败：" + ex);
            StatusText.Text = string.Format(WpfStrings.StatusLightEditorOpenFailed, ex.Message);
        }
    }

    /// <summary>⭐ task-17：打开与迷你窗共用的 HID 键盘编辑器（模式/亮度/速度/颜色，按 HID 支持范围）✅。</summary>
    private void OpenWideHidKeyboardEditor(string title)
    {
        // task-26：无设备/后端未连接 ⇒ 只提示不打开（⛔ 不伪造能力；HidKeyboardWindow 需要真实 KeyboardRgb）
        if (!IsWideBackendConnected || (_probe?.Hw ?? WpfBackend.Hw) is null)
        {
            Logger.WriteLineIfChanged("wide-light-hid-no-device",
                $"[wide-light] HID 编辑入口：无设备或后端未连接（connected={IsWideBackendConnected} hw={((_probe?.Hw ?? WpfBackend.Hw) == null ? "null" : "ok")}）⇒ 只提示，不打开 ✅");
            StatusText.Text = WpfStrings.StatusHidEditorNeedsBackend;
            return;
        }
        try
        {
            Logger.WriteLine("[wide-light] 打开 HID 键盘编辑器");
            WpfBackend.Attach(_probe?.Hw, _probe?.Service);
            KeyboardRgb? rgb = WpfBackend.rgb;
            if (rgb == null)
            {
                rgb = new KeyboardRgb();
                rgb.LoadConfig();
                WpfBackend.rgb = rgb;
            }
            MountInlineLightEditor(new HidKeyboardWindow(rgb));
            Logger.WriteLineIfChanged("wide-light-hid-editor", $"[wide-light] HID 键盘编辑器已内联：{title}");
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 打开 HID 键盘编辑器失败：" + ex);
            StatusText.Text = string.Format(WpfStrings.StatusHidEditorOpenFailed, ex.Message);
        }
    }

    /// <summary>把二级编辑器内容直接挂到灯光页，而不是另开窗口。</summary>
    private void MountInlineLightEditor(Window editor)
    {
        try
        {
            if (LightingCardsGrid == null || editor.Content is not UIElement content)
            {
                editor.Owner = this;
                editor.ShowDialog();
                return;
            }
            _inlineLightEditorWindow?.Close();
            _inlineLightEditorWindow = editor;
            editor.Content = null;
            StackPanel host = new();
            Button back = new()
            {
                Content = WpfStrings.LightInlineBack,
                Style = (Style)FindResource("GhostButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            back.Click += (_, _) => CloseInlineLightEditor();
            host.Children.Add(back);
            host.Children.Add(content);
            LightingCardsGrid.Children.Clear();
            LightingCardsGrid.Children.Add(host);
        }
        catch (Exception ex)
        {
            Logger.WriteError("[wide-light] 内联灯光编辑器失败：" + ex);
            RebuildLightingCards();
        }
    }

    private void CloseInlineLightEditor()
    {
        _inlineLightEditorWindow?.Close();
        _inlineLightEditorWindow = null;
        RebuildLightingCards();
    }

    private void RebuildLightingCards()
    {
        // BuildLightingCards 内部会先清空，重复调用安全 ✅
        BuildLightingCards();
    }

    private static bool IsGradeLightChannel(string topic, MechrevoHw? hw)
    {
        return (string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
                || string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal))
            && hw != null
            && !hw.UsesBaolongda
            && hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40;
    }

    private static bool EffectSupportsSpeed(string topic, string effect, bool hidKeyboard)
    {
        if (hidKeyboard || effect.StartsWith("hid:", StringComparison.Ordinal))
        {
            return false;
        }
        if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
        {
            return string.Equals(effect, "Manual", StringComparison.Ordinal);
        }
        return effect is not ("Single" or "Static" or "Off" or "TurnedOff");
    }

    private static (int Min, int Max, string Label) EffectSpeedInfo(string topic, string effect)
    {
        if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal)
            && string.Equals(effect, "Manual", StringComparison.Ordinal))
        {
            return (2, 10, WpfStrings.L("色彩间隔", "Color interval"));
        }
        return (0, 4, WpfStrings.Speed);
    }

    private static int NearestManualPaletteIndex(System.Drawing.Color color)
    {
        int best = 1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < LightChannelSettings.ManualPalette.Length; i++)
        {
            System.Drawing.Color candidate = System.Drawing.Color.FromArgb(LightChannelSettings.ManualPalette[i]);
            int distance = (candidate.R - color.R) * (candidate.R - color.R)
                + (candidate.G - color.G) * (candidate.G - color.G)
                + (candidate.B - color.B) * (candidate.B - color.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i + 1;
            }
        }
        return best;
    }

    private async Task ApplyLightCardAsync(string topic, string effect, Slider slider, Slider speed, int colorArgb, int[] mixColors, int breathingIndex, int[] manualIndexes, bool powerOn, bool channelPowerOsd = false)
    {
        try
        {
            bool keyboardChannel = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
            bool gradeLightChannel = IsGradeLightChannel(topic, _probe?.Hw ?? WpfBackend.Hw);
            int sliderLight = (int)Math.Round(slider.Value);
            int light = keyboardChannel || gradeLightChannel
                ? sliderLight
                : Math.Clamp(sliderLight * 4 / 100, 0, 4);
            bool speedApplicable = EffectSupportsSpeed(topic, effect, effect.StartsWith("hid:", StringComparison.Ordinal));
            int speedValue = speedApplicable ? (int)Math.Round(speed.Value) : 0;
            if (topic == "Keyboard/Ctrl" && WideIsWhiteBacklightKeyboard(_probe?.Hw))
            {
                if (_owner != null)
                {
                    bool whiteOk = await _owner.WideApplyWhiteBacklightAsync(light, powerOn);
                    StatusText.Text = whiteOk ? WpfStrings.L("白色背光已下发", "White backlight sent") : WpfStrings.L("白色背光下发失败", "Failed to send white backlight");
                }
                return;
            }
            LightChannelSettings settings = LightingSettingsStore.Load(topic, effect) with
            {
                Effect = effect,
                Light = light,
                Speed = speedValue,
                ColorArgb = colorArgb,
                MixColors = (int[])mixColors.Clone(),
                ManualIndexes = (int[])manualIndexes.Clone(),
                BreathingIndex = breathingIndex,
                UsePresetColor = false,
                PowerOn = powerOn
            };
            LightingSettingsStore.Save(topic, settings);
            if (_owner == null)
            {
                StatusText.Text = WpfStrings.StatusLightSavedOffline;
                return;
            }
            bool ok = await _owner.WideApplyLightAsync(topic, effect, light, speedValue, colorArgb, powerOn, channelPowerOsd);
            StatusText.Text = ok
                ? WpfStrings.L("灯光已下发：", "Lighting sent: ") + topic + " / " + effect
                : WpfStrings.L("灯光下发失败：", "Failed to send lighting: ") + topic + " / " + effect;
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(WpfStrings.StatusLightEditFailed, ex.Message);
        }
    }

    private async void WideDock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string name) return;
        switch (name)
        {
            case "退出":
                Application.Current.Shutdown();
                return;
            case "设置":
                if (_owner != null)
                {
                    SettingsWindow settings = new(_owner);
                    settings.Owner = this;
                    settings.ShowDialog();
                }
                return;
            case "赞助":
                ShowInlineWindow(new SponsorWindow(), "赞助");
                return;
            case "悬浮窗":
            case "诊断":
                _ = RunDockActionAsync(name);
                return;
            case "检查更新":
                if (_owner != null)
                {
                    StatusText.Text = WpfStrings.UpdateChecking;
                    string message = await _owner.WideCheckForUpdatesAsync();
                    StatusText.Text = message;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        ToastService.Show(message);
                    }
                }
                return;
        }
    }

    private async Task RunDockActionAsync(string name)
    {
        if (_owner == null) return;
        await _owner.WideDockActionAsync(name);
        StatusText.Text = name + WpfStrings.L(" 已执行", " executed");
    }

    private void ShowInlineWindow(Window window, string title)
    {
        if (window.Content is not UIElement content)
        {
            window.Close();
            return;
        }
        window.Content = null;
        InlineOverlayTitle.Text = title;
        InlineOverlayContent.Content = content;
        InlineOverlay.Visibility = Visibility.Visible;
        _inlineWindow = window;
    }

    private void InlineOverlayClose_Click(object sender, RoutedEventArgs e)
    {
        InlineOverlay.Visibility = Visibility.Collapsed;
        InlineOverlayContent.Content = null;
        if (_inlineWindow != null)
        {
            _inlineWindow.Close();
            _inlineWindow = null;
        }
    }

    private void CustomMode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new CustomModeWindow { Owner = this }.ShowDialog();
                StatusText.Text = WpfStrings.MainWindow_xaml_Text_19;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_20 + ex.Message;
        }
    }

    private void FanCurveCpu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FanCurveWindow("balanced", showCpu: true, showGpu: false) { Owner = this }.ShowDialog();
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_21;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_22 + ex.Message;
        }
    }

    private void FanCurveGpu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FanCurveWindow("balanced", showCpu: false, showGpu: true) { Owner = this }.ShowDialog();
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_23;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.MainWindow_xaml_Text_24 + ex.Message;
        }
    }

    private void ApplyDarkTitleBar(bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int flag = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref flag, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
