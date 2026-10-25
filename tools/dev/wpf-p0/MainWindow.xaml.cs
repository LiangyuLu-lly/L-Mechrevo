using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

public partial class MainWindow : Window
{
    private readonly OriginalStyleWindow? _owner;
    private readonly BackendProbe? _probe;
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
    private readonly List<(ToggleButton Button, int Hz)> _displayRefreshButtons = new();
    private readonly List<(string Topic, Button Toggle, TextBlock Effect, LightChannelSettings Settings)> _lightRows = new();

    internal MainWindow(OriginalStyleWindow? owner = null, BackendProbe? probe = null)
    {
        InitializeComponent();
        _owner = owner;
        _probe = probe;
        AppTheme.Apply(true);
        BackgroundImage.Visibility = Visibility.Collapsed;
        BackgroundScrim.Visibility = Visibility.Collapsed;
        BuildMoreSwitchTiles();
        BuildSensorBlocks();
        BuildHomeExtras();
        BuildLightingCards();
        ApplyPage("home");
        _sensorTimer.Tick += (_, _) => RefreshSensors();
        _sensorTimer.Start();
        Closed += (_, _) => _sensorTimer.Stop();
        RefreshSensors();
        StatusText.Text = "就绪 · 宽屏四页布局";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar(_dark);
    }

    private void RefreshSensors()
    {
        if (_owner == null)
        {
            return;
        }
        (string cpuTemp, string gpuTemp, string cpuFan, string gpuFan, string cpuUsage, string gpuUsage,
            string cpuFanDuty, string gpuFanDuty, string cpuFreq, string gpuFreq, string fanMaxText,
            string cpuPower, string gpuPower, string gpuMemFreq) = _owner.WideSensors();
        double fanMax = Math.Max(1, ParseNumber(fanMaxText));
        if (fanMax <= 1) fanMax = 5000;
        SetMetric(_cpuMetrics, 0, ParseNumber(cpuTemp), "℃");
        SetMetric(_cpuMetrics, 1, ParseNumber(cpuUsage), "%");
        SetMetric(_cpuMetrics, 2, ParseNumber(cpuPower), "W");
        SetMetric(_cpuMetrics, 3, ParseNumber(cpuFreq), "MHz");
        SetMetric(_gpuMetrics, 0, ParseNumber(gpuTemp), "℃");
        SetMetric(_gpuMetrics, 1, ParseNumber(gpuUsage), "%");
        SetMetric(_gpuMetrics, 2, ParseNumber(gpuPower), "W");
        SetMetric(_gpuMetrics, 3, ParseNumber(gpuFreq), "MHz");
        SetMetric(_gpuMetrics, 4, ParseNumber(gpuMemFreq), "MHz");
        _cpuFanRing?.SetValue(ParseNumber(cpuFan), fanMax, "rpm");
        _gpuFanRing?.SetValue(ParseNumber(gpuFan), fanMax, "rpm");
        if (_cpuDutyText != null) _cpuDutyText.Text = "占空比 " + cpuFanDuty;
        if (_gpuDutyText != null) _gpuDutyText.Text = "占空比 " + gpuFanDuty;
        RefreshHomeExtras();
    }

    private static void SetMetric(List<(RoundedBar Bar, TextBlock Value, string Unit)> metrics, int index, double value, string unit)
    {
        if (index < 0 || index >= metrics.Count) return;
        (RoundedBar bar, TextBlock text, string _) = metrics[index];
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

    private void BuildSensorBlocks()
    {
        if (SensorBlocksGrid == null) return;
        SensorBlocksGrid.Children.Add(BuildCpuGpuCard("CPU", cpu: true));
        SensorBlocksGrid.Children.Add(BuildCpuGpuCard("GPU", cpu: false));
    }

    private Border BuildCpuGpuCard(string title, bool cpu)
    {
        StackPanel left = new() { Width = 140 };
        left.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        RingGauge ring = new() { Width = 116, Height = 116, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        TextBlock duty = new()
        {
            Text = "占空比 --",
            Style = (Style)FindResource("MutedText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
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

        StackPanel metrics = new() { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        (string Label, double Maximum)[] rows = cpu
            ? new[] { ("温度", 100.0), ("使用率", 100.0), ("功耗", 100.0), ("频率", 6000.0) }
            : new[] { ("温度", 100.0), ("使用率", 100.0), ("功耗", 200.0), ("核心频率", 3000.0), ("显存频率", 12000.0) };
        List<(RoundedBar Bar, TextBlock Value, string Unit)> target = cpu ? _cpuMetrics : _gpuMetrics;
        foreach ((string label, double maximum) in rows)
        {
            StackPanel metric = new() { Margin = new Thickness(0, 0, 0, 8) };
            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new() { Text = label, Style = (Style)FindResource("MutedText") };
            TextBlock value = new()
            {
                Text = "--",
                Style = (Style)FindResource("MutedText"),
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(value, 1);
            header.Children.Add(name);
            header.Children.Add(value);
            RoundedBar bar = new()
            {
                Maximum = maximum,
                Height = 9,
                Margin = new Thickness(0, 5, 0, 0)
            };
            metric.Children.Add(header);
            metric.Children.Add(bar);
            metrics.Children.Add(metric);
            target.Add((bar, value, ""));
        }

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(metrics, 1);
        grid.Children.Add(left);
        grid.Children.Add(metrics);
        return new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(5),
            MinHeight = 220,
            Child = grid
        };
    }

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navCollapsed = !_navCollapsed;
        NavColumn.Width = new GridLength(_navCollapsed ? 58 : 196);
        NavPanel.Padding = new Thickness(_navCollapsed ? 6 : 18);
        NavTitle.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavQuickTitle.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavFooterPanel.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavCollapseButton.Content = _navCollapsed ? "»" : "«";
        FontFamily glyphFont = new("Segoe MDL2 Assets");
        FontFamily textFont = (FontFamily)Application.Current.Resources["AppFontFamily"];
        SetNavContent(NavHome, _navCollapsed, "\uE80F", "首页", glyphFont, textFont);
        SetNavContent(NavLighting, _navCollapsed, "\uE706", "灯光", glyphFont, textFont);
        SetNavContent(NavCustom, _navCollapsed, "\uE713", "自定义", glyphFont, textFont);
        SetNavContent(NavSwitches, _navCollapsed, "\uE7F4", "更多开关", glyphFont, textFont);
    }

    private static void SetNavContent(Button button, bool collapsed, string glyph, string text, FontFamily glyphFont, FontFamily textFont)
    {
        button.Content = collapsed ? glyph : text;
        button.FontFamily = collapsed ? glyphFont : textFont;
        button.FontSize = collapsed ? 16 : 13;
        button.Padding = collapsed ? new Thickness(10, 10, 10, 10) : new Thickness(12, 10, 12, 10);
        button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e) => new CompactWindow(_owner, _probe).Show();

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? "切换浅色" : "切换深色";
        StatusText.Text = "L-Mechrevo · " + (_dark ? "深色" : "浅色") + "主题";
    }

    private void AccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            Color color = (Color)ColorConverter.ConvertFromString(text);
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["AccentColor"] = color;
            StatusText.Text = "强调色已切换：" + text;
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
        {
            ApplyPage(page);
        }
    }

    private void ApplyPage(string page)
    {
        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        LightingPage.Visibility = page == "lighting" ? Visibility.Visible : Visibility.Collapsed;
        CustomPage.Visibility = page == "custom" ? Visibility.Visible : Visibility.Collapsed;
        SwitchesPage.Visibility = page == "switches" ? Visibility.Visible : Visibility.Collapsed;
        Button[] navs = { NavHome, NavLighting, NavCustom, NavSwitches };
        foreach (Button nav in navs)
        {
            bool active = string.Equals(nav.Tag as string, page, StringComparison.Ordinal);
            nav.Background = active ? (Brush)FindResource("ControlBrush") : Brushes.Transparent;
            nav.Foreground = active ? (Brush)FindResource("TextBrush") : (Brush)FindResource("MutedBrush");
        }
        StatusText.Text = page switch
        {
            "home" => "首页 · 传感器 / 性能 / 显卡 / 显示",
            "lighting" => "灯光 · 页面内直接编辑",
            "custom" => "自定义 · 自定义模式 / 风扇曲线",
            "switches" => "更多开关 · 磁贴布局",
            _ => "就绪"
        };
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string text && int.TryParse(text, out int mode))
        {
            _owner?.WideSetMode(mode);
            StatusText.Text = "性能模式已请求：" + button.Content;
        }
    }

    private void Gpu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string text && int.TryParse(text, out int mode))
        {
            _owner?.WideSetGpuMode(mode);
            StatusText.Text = "显卡模式已请求：" + button.Content;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string text && int.TryParse(text, out int hz))
        {
            _owner?.WideSetRefresh(hz);
            StatusText.Text = "刷新率已请求：" + hz + " Hz";
        }
    }

    private async void QuickTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string key)
        {
            bool ok = _owner != null && await _owner.WideToggleQuickAsync(key);
            StatusText.Text = ok ? "快捷开关已切换：" + key : "快捷开关失败：" + key;
        }
    }

    private async void MoreSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string key)
        {
            bool ok = _owner != null && await _owner.WideToggleQuickAsync(key);
            StatusText.Text = ok ? "更多开关已切换：" + key : "更多开关失败：" + key;
        }
    }

    private void BuildMoreSwitchTiles()
    {
        (string Key, string Label)[] switches =
        {
            ("touchpad", "触摸板"), ("wifi", "WiFi"), ("bt", "蓝牙"),
            ("webcam", "摄像头"), ("micmute", "麦克风静音"),
            ("numpad", "小键盘锁"), ("touchpadtoggle", "触摸板切换键")
        };
        foreach ((string key, string label) in switches)
        {
            Button tile = new()
            {
                Style = (Style)FindResource("TileButton"),
                Tag = key,
                Margin = new Thickness(5)
            };
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = key, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 6, 0, 0) });
            tile.Content = content;
            tile.Click += MoreSwitch_Click;
            MoreSwitchGrid.Children.Add(tile);
        }
    }

    private void BuildHomeExtras()
    {
        if (_owner == null || HomeExtrasPanel == null)
        {
            return;
        }
        (string info, int mode, string charge) = _owner.WideBattery();
        StackPanel battery = new();
        battery.Children.Add(new TextBlock { Text = "电池", Style = (Style)FindResource("CardTitle") });
        _batteryInfoText = new TextBlock { Text = info, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 4, 0, 8) };
        battery.Children.Add(_batteryInfoText);
        StackPanel modes = new() { Orientation = Orientation.Horizontal };
        string[] labels = { "长效模式", "平衡模式", "工作站模式" };
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
                _owner?.WideSetBatteryMode(target);
                StatusText.Text = "电池模式已请求：" + labels[target];
            };
            modes.Children.Add(button);
        }
        battery.Children.Add(modes);
        _chargeLimitText = new TextBlock { Text = charge, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        battery.Children.Add(_chargeLimitText);
        HomeExtrasPanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(5), Child = battery });

        (string displayInfo, int currentHz, int maxHz, bool auto, int brightness) = _owner.WideDisplay();
        DisplayInfoText.Text = displayInfo;
        DisplayRefreshPanel.Children.Clear();
        _displayRefreshButtons.Clear();
        foreach (int hz in new[] { 60, Math.Max(120, maxHz / 2), Math.Max(60, maxHz) }.Distinct())
        {
            int target = hz;
            ToggleButton button = new()
            {
                Content = hz + " Hz",
                Tag = hz.ToString(),
                Style = (Style)FindResource("SegmentButton"),
                IsChecked = hz == currentHz,
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.Click += (_, _) => _owner?.WideSetRefresh(target);
            DisplayRefreshPanel.Children.Add(button);
            _displayRefreshButtons.Add((button, hz));
        }
        StackPanel brightnessRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        brightnessRow.Children.Add(new TextBlock { Text = "亮度", Style = (Style)FindResource("MutedText"), Width = 54, VerticalAlignment = VerticalAlignment.Center });
        _displayBrightness = new Slider { Minimum = 0, Maximum = 100, Value = brightness, Width = 220, Style = (Style)FindResource("RoundSlider") };
        _displayBrightness.ValueChanged += (_, _) => _owner?.WideSetBrightness((int)Math.Round(_displayBrightness.Value));
        _displayBrightnessText = new TextBlock { Text = brightness + "%", Style = (Style)FindResource("MutedText"), Width = 46, TextAlignment = TextAlignment.Right };
        brightnessRow.Children.Add(_displayBrightness);
        brightnessRow.Children.Add(_displayBrightnessText);
        _autoRefresh = new CheckBox
        {
            Content = "自动刷新率",
            Style = (Style)FindResource("HeaderSwitch"),
            IsChecked = auto,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _autoRefresh.Click += (_, _) => _owner?.WideSetAutoRefresh(_autoRefresh.IsChecked == true);
        DisplayDetailsPanel.Children.Add(brightnessRow);
        DisplayDetailsPanel.Children.Add(_autoRefresh);

        (string topic, string label)[] lightChannels =
        {
            ("Keyboard/Ctrl", "键盘"),
            ("HidLightbar/Ctrl", "灯带"),
            ("HidLightbar_Logo/Ctrl", "Logo 灯"),
            ("HidLightbar_Hinge/Ctrl", "铰链灯"),
            ("HidLightbar_Sync/Ctrl", "同步灯")
        };
        StackPanel lights = new();
        lights.Children.Add(new TextBlock { Text = "灯光", Style = (Style)FindResource("CardTitle") });
        lights.Children.Add(new TextBlock { Text = "非编辑页：只显示状态与开关，详细参数到灯光页", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 3, 0, 8) });
        foreach ((string topic, string label) in lightChannels)
        {
            LightChannelSettings settings = LightingSettingsStore.Load(topic, "Single");
            Grid row = new() { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new() { Text = label, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center };
            TextBlock effect = new() { Text = settings.Effect, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center };
            Button toggle = new()
            {
                Content = settings.PowerOn ? "开" : "关",
                Style = (Style)FindResource("FlatButton"),
                Tag = topic
            };
            toggle.Click += async (_, _) =>
            {
                bool turnOn = toggle.Content as string != "开";
                LightChannelSettings current = LightingSettingsStore.Load(topic, "Single");
                if (_probe != null)
                {
                    if (topic == "Keyboard/Ctrl")
                    {
                        await _probe.SetKeyboardPowerAsync(turnOn);
                    }
                    else
                    {
                        await _probe.SetLightPowerAsync(topic, turnOn);
                    }
                }
                LightingSettingsStore.SavePower(topic, turnOn, "宽屏灯光摘要");
                toggle.Content = turnOn ? "开" : "关";
                StatusText.Text = label + (turnOn ? " 已打开" : " 已关闭");
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(effect, 1);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(name);
            row.Children.Add(effect);
            row.Children.Add(toggle);
            lights.Children.Add(row);
            _lightRows.Add((topic, toggle, effect, settings));
        }
        HomeExtrasPanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(5), Child = lights });
    }

    private void RefreshHomeExtras()
    {
        if (_owner == null)
        {
            return;
        }
        if (_batteryInfoText != null) _batteryInfoText.Text = _owner.WideBattery().Info;
        if (_chargeLimitText != null) _chargeLimitText.Text = _owner.WideBattery().Charge;
        (string info, int currentHz, int maxHz, bool auto, int brightness) = _owner.WideDisplay();
        DisplayInfoText.Text = info;
        if (_displayBrightnessText != null) _displayBrightnessText.Text = brightness + "%";
        if (_displayBrightness != null && Math.Abs(_displayBrightness.Value - brightness) > 1)
        {
            _displayBrightness.Value = brightness;
        }
        foreach ((ToggleButton button, int hz) in _displayRefreshButtons)
        {
            button.IsChecked = hz == currentHz;
        }
        if (_autoRefresh != null) _autoRefresh.IsChecked = auto;
        foreach ((string topic, Button toggle, TextBlock effect, LightChannelSettings _) in _lightRows)
        {
            LightChannelSettings settings = LightingSettingsStore.Load(topic, "Single");
            toggle.Content = settings.PowerOn ? "开" : "关";
            effect.Text = settings.Effect;
        }
    }

    private void BuildLightingCards()
    {
        if (LightingCardsGrid == null)
        {
            return;
        }
        MechrevoHw? hw = _probe?.Hw;
        int keyboardType = hw?.Capabilities.KeyboardType ?? 0;
        var channels = new List<(string Topic, string Title, (string Effect, string Name)[] Effects)>
        {
            ("Keyboard/Ctrl", "键盘灯光",
                KeyboardFirmwareEffects.Visible(keyboardType).Select(static item => (item.Id, item.Label)).ToArray()),
            ("HidLightbar/Ctrl", "灯带", LightForm.LightbarEffects),
            ("HidLightbar_Logo/Ctrl", "Logo 灯",
                hw is not null && (hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40
                    || hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30)
                    ? LightForm.LogoEffectsGen40 : LightForm.CurrentLogoEffects()),
            ("HidLightbar_Hinge/Ctrl", "铰链灯", LightForm.HingeEffects),
            ("HidLightbar_Sync/Ctrl", "同步灯", LightForm.SyncEffects)
        };
        foreach ((string topic, string title, (string Effect, string Name)[] effects) in channels)
        {
            if (effects.Length > 0)
            {
                LightingCardsGrid.Children.Add(BuildLightCard(topic, title, effects));
            }
        }
    }

    private Border BuildLightCard(string topic, string title, (string Effect, string Name)[] effects)
    {
        LightChannelSettings settings = LightingSettingsStore.Load(topic, effects[0].Effect);
        int maxLight = topic == "Keyboard/Ctrl" ? 4 : topic == "HidLightbar_Logo/Ctrl" ? 4 : 100;
        int light = Math.Clamp(settings.Light, 0, maxLight);
        StackPanel panel = new();
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        panel.Children.Add(new TextBlock
        {
            Text = "页面内直接编辑，修改后自动下发",
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 3, 0, 10)
        });
        ComboBox combo = new() { Style = (Style)FindResource("RoundCombo"), Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        foreach ((string effect, string name) in effects)
        {
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = effect });
        }
        int effectIndex = Array.FindIndex(effects, item => item.Effect == settings.Effect);
        combo.SelectedIndex = effectIndex >= 0 ? effectIndex : 0;
        panel.Children.Add(combo);
        Grid brightness = new() { Margin = new Thickness(0, 0, 0, 6) };
        brightness.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        brightness.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Slider slider = new() { Style = (Style)FindResource("RoundSlider"), Minimum = 0, Maximum = maxLight, Value = light };
        TextBlock value = new()
        {
            Text = light + "/" + maxLight,
            Style = (Style)FindResource("MutedText"),
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(value, 1);
        brightness.Children.Add(slider);
        brightness.Children.Add(value);
        panel.Children.Add(brightness);
        int colorArgb = settings.ColorArgb;
        CheckBox power = new()
        {
            Content = "电源",
            Style = (Style)FindResource("HeaderSwitch"),
            IsChecked = settings.PowerOn,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Slider speed = new()
        {
            Style = (Style)FindResource("RoundSlider"),
            Minimum = 0,
            Maximum = 4,
            Value = Math.Clamp(settings.Speed, 0, 4),
            Visibility = topic == "Keyboard/Ctrl" ? Visibility.Collapsed : Visibility.Visible
        };
        if (speed.Visibility == Visibility.Visible)
        {
            Grid speedRow = new() { Margin = new Thickness(0, 0, 0, 6) };
            speedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            speedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock speedText = new()
            {
                Text = (int)speed.Value + "/4",
                Style = (Style)FindResource("MutedText"),
                Width = 40,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(speed, 0);
            Grid.SetColumn(speedText, 1);
            speedRow.Children.Add(speed);
            speedRow.Children.Add(speedText);
            panel.Children.Add(speedRow);
            speed.ValueChanged += (_, _) =>
            {
                speedText.Text = (int)Math.Round(speed.Value) + "/4";
                _ = ApplyLightCardAsync(topic, combo, slider, speed, colorArgb, power.IsChecked == true);
            };
        }
        panel.Children.Add(power);
        power.Click += (_, _) => _ = ApplyLightCardAsync(topic, combo, slider, speed, colorArgb, power.IsChecked == true);
        Button color = new()
        {
            Content = "颜色",
            Style = (Style)FindResource("GhostButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 5, 12, 5)
        };
        color.Click += (_, _) =>
        {
            ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(colorArgb)) { Owner = this };
            if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
            {
                colorArgb = picked.ToArgb();
                _ = ApplyLightCardAsync(topic, combo, slider, speed, colorArgb, power.IsChecked == true);
            }
        };
        panel.Children.Add(color);
        slider.ValueChanged += (_, _) =>
        {
            value.Text = (int)Math.Round(slider.Value) + "/" + maxLight;
            _ = ApplyLightCardAsync(topic, combo, slider, speed, colorArgb, power.IsChecked == true);
        };
        combo.SelectionChanged += (_, _) => _ = ApplyLightCardAsync(topic, combo, slider, speed, colorArgb, power.IsChecked == true);
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(5), Child = panel };
    }

    private async Task ApplyLightCardAsync(string topic, ComboBox combo, Slider slider, Slider speed, int colorArgb, bool powerOn)
    {
        if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string effect)
        {
            return;
        }
        try
        {
            LightChannelSettings settings = LightingSettingsStore.Load(topic, effect) with
            {
                Effect = effect,
                Light = (int)Math.Round(slider.Value),
                Speed = (int)Math.Round(speed.Value),
                ColorArgb = colorArgb,
                UsePresetColor = false,
                PowerOn = powerOn
            };
            LightingSettingsStore.Save(topic, settings);
            if (_probe == null)
            {
                StatusText.Text = "灯光设置已保存（后端未连接）";
                return;
            }
            if (!powerOn)
            {
                bool off = topic == "Keyboard/Ctrl"
                    ? await _probe.SetKeyboardPowerAsync(false)
                    : await _probe.SetLightPowerAsync(topic, false);
                StatusText.Text = off ? "灯光已关闭：" + topic : "灯光关闭失败：" + topic;
                return;
            }
            if (topic == "Keyboard/Ctrl")
            {
                await _probe.SetKeyboardPowerAsync(true);
            }
            else
            {
                await _probe.SetLightPowerAsync(topic, true);
            }
            bool ok = topic switch
            {
                "Keyboard/Ctrl" => await _probe.SetKeyboardEffectAsync(effect),
                "HidLightbar/Ctrl" => await _probe.SetLightbarEffectAsync(effect),
                "HidLightbar_Logo/Ctrl" => await _probe.SetLogoEffectAsync(effect),
                "HidLightbar_Hinge/Ctrl" => await _probe.SetHingeEffectAsync(effect),
                _ => await _probe.SetSyncEffectAsync(effect)
            };
            StatusText.Text = ok ? "灯光已下发：" + topic + " / " + effect : "灯光下发失败：" + topic;
        }
        catch (Exception ex)
        {
            StatusText.Text = "灯光编辑失败：" + ex.Message;
        }
    }

    private void CustomMode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new CustomModeWindow().ShowDialog();
            StatusText.Text = "自定义模式已关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开自定义模式失败：" + ex.Message;
        }
    }

    private void FanCurve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FanCurveWindow("balanced").ShowDialog();
            StatusText.Text = "风扇曲线已关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开风扇曲线失败：" + ex.Message;
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
