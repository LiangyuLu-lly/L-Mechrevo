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

    internal MainWindow(OriginalStyleWindow? owner = null, BackendProbe? probe = null)
    {
        InitializeComponent();
        _owner = owner;
        _probe = probe;
        AppTheme.Apply(true);
        BackgroundImage.Visibility = Visibility.Collapsed;
        BackgroundScrim.Visibility = Visibility.Collapsed;
        BuildMoreSwitchTiles();
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
        (string cpuTemp, string gpuTemp, string cpuFan, string gpuFan, string cpuUsage, string gpuUsage) = _owner.WideSensors();
        CpuTempText.Text = cpuTemp.Replace(" ℃", "");
        GpuTempText.Text = gpuTemp.Replace(" ℃", "");
        CpuPowerText.Text = cpuUsage.Replace("%", "");
        GpuPowerText.Text = gpuUsage.Replace("%", "");
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e) => new CompactWindow().Show();

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
                _ = ApplyLightCardAsync(topic, combo, slider, colorArgb);
            }
        };
        panel.Children.Add(color);
        slider.ValueChanged += (_, _) =>
        {
            value.Text = (int)Math.Round(slider.Value) + "/" + maxLight;
            _ = ApplyLightCardAsync(topic, combo, slider, colorArgb);
        };
        combo.SelectionChanged += (_, _) => _ = ApplyLightCardAsync(topic, combo, slider, colorArgb);
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(5), Child = panel };
    }

    private async Task ApplyLightCardAsync(string topic, ComboBox combo, Slider slider, int colorArgb)
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
                ColorArgb = colorArgb,
                UsePresetColor = false,
                PowerOn = true
            };
            LightingSettingsStore.Save(topic, settings);
            if (_probe == null)
            {
                StatusText.Text = "灯光设置已保存（后端未连接）";
                return;
            }
            await _probe.SetLightPowerAsync(topic, true);
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
