using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MechrevoLite.Hardware;
using System.Windows.Interop;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

public partial class OriginalStyleWindow : Window
{
    private bool _dark = true;
    private BackendProbe? _backendProbe;
    private readonly DispatcherTimer _backendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    public OriginalStyleWindow()
    {
        InitializeComponent();
        ApplyDarkTitleBar(_dark);
        _backendTimer.Tick += (_, _) =>
        {
            if (_backendProbe != null)
            {
                RefreshFromBackend();
            }
        };
        Closed += (_, _) =>
        {
            _backendTimer.Stop();
            _backendProbe?.Dispose();
            _backendProbe = null;
        };
        if (ModeGrid != null && ModeGrid.Children.Count > 1 && ModeGrid.Children[1] is ToggleButton defaultMode)
        {
            defaultMode.IsChecked = true;
            UpdateModeBorders(defaultMode);
        }
        MainScroll.SizeChanged += delegate
        {
            UpdateContentWidth();
        };
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
        };
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
            StatusText.Text = "性能模式：" + selected.Content;
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

    private async Task SwitchModeAsync(int mode)
    {
        bool ok = await _backendProbe!.SwitchModeAsync(mode);
        BackendStatus.Text = ok ? "模式已下发" : "模式下发失败";
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
        bool ok = await _backendProbe.SwitchQuickAsync(key, tile.IsChecked == true);
        StatusText.Text = ok ? "快捷开关已下发：" + key : "快捷开关下发失败：" + key;
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
        CpuStat.Text = "CPU " + hw.CpuTemp + "℃   " + hw.CpuFanRpm + "rpm";
        GpuStat.Text = "GPU " + hw.GpuTemp + "℃   " + hw.GpuFanRpm + "rpm";
        BatteryInfo.Text = "电池 " + hw.BatteryPercent + "% · " + hw.BatteryCapacityText;
        int mode = probe.CurrentMode;
        GpuOverclockSwitch.Visibility = (mode == 1 && probe.GpuOcSupported) ? Visibility.Visible : Visibility.Collapsed;
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
    }

    private static void UpdateModeBorders(ToggleButton selected)
    {
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        foreach (object child in ((UniformGrid)selected.Parent).Children)
        {
            if (child is ToggleButton button)
            {
                button.BorderBrush = gray;
                button.BorderThickness = new Thickness(1);
            }
        }
        if (selected.Tag is string hex)
        {
            selected.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            selected.BorderThickness = new Thickness(3);
        }
    }

    private void BackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool on = BackgroundToggle.IsChecked == true;
        BackgroundImage.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ConnectBackend_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe != null)
        {
            BackendLine.Text = _backendProbe.Snapshot();
            return;
        }
        ConnectBackendButton.IsEnabled = false;
        BackendStatus.Text = "后端连接中…";
        BackendStatus.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
        BackendLine.Text = "正在创建 MechrevoHw 并 ConnectAsync（最长 15 秒）…";
        BackendProbe probe = new BackendProbe();
        _backendProbe = probe;
        Task<bool> connect = probe.ConnectAsync();
        Task completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(15)));
        if (completed != connect)
        {
            BackendStatus.Text = "后端连接超时";
            BackendStatus.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
            BackendLine.Text = "15 秒内未完成；MechrevoHw 可能在没有 GCU 服务的机器上等待/无权限。";
            probe.Dispose();
            _backendProbe = null;
            ConnectBackendButton.IsEnabled = true;
            return;
        }
        if (await connect)
        {
            BackendStatus.Text = "后端已连接";
            BackendStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
            BackendLine.Text = probe.Snapshot();
            ApplyCapabilities();
            _backendTimer.Start();
        }
        else
        {
            BackendStatus.Text = "后端连接失败";
            BackendStatus.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
            BackendLine.Text = probe.LastError;
            probe.Dispose();
            _backendProbe = null;
        }
        ConnectBackendButton.IsEnabled = true;
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
            LightingSection.Visibility = (caps.Keyboard || caps.Lightbar || caps.LogoLight) ? Visibility.Visible : Visibility.Collapsed;
            bool customAllowed = (caps.CpuPerformanceTuning || caps.FanSettings) && !(_backendProbe?.Hw?.SupportsModeProfileConfig ?? false);
            CustomModeButton.Visibility = customAllowed ? Visibility.Visible : Visibility.Collapsed;
            FanTile.Visibility = _backendProbe.SupportsQuick("fanboost") ? Visibility.Visible : Visibility.Collapsed;
            CameraTile.Visibility = _backendProbe.SupportsQuick("webcam") ? Visibility.Visible : Visibility.Collapsed;
            MicTile.Visibility = _backendProbe.SupportsQuick("micmute") ? Visibility.Visible : Visibility.Collapsed;
            WinKeyTile.Visibility = _backendProbe.SupportsQuick("winkey") ? Visibility.Visible : Visibility.Collapsed;
            ScreenOffTile.Visibility = _backendProbe.SupportsQuick("monitoroff") ? Visibility.Visible : Visibility.Collapsed;
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

    private async void CalibrationSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool on = CalibrationSwitch.IsChecked == true;
        int mode = on ? Math.Max(0, CalibrationCombo.SelectedIndex) : 0;
        bool ok = await _backendProbe.SetColorCalibrationAsync(mode);
        StatusText.Text = ok ? "屏幕校色已下发" : "屏幕校色下发失败";
    }

    private async void CalibrationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_backendProbe != null && CalibrationSwitch.IsChecked == true)
        {
            await _backendProbe.SetColorCalibrationAsync(Math.Max(0, CalibrationCombo.SelectedIndex));
        }
    }

    private async void DisplayEffectSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe == null)
        {
            return;
        }
        bool ok = await _backendProbe.SetDisplayEffectSwitchAsync(DisplayEffectSwitch.IsChecked == true);
        StatusText.Text = ok ? "色彩增强已下发" : "色彩增强下发失败";
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

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? "浅色" : "深色";
        StatusText.Text = "0.293.5 / WPF P0 · " + (_dark ? "深色" : "浅色");
    }

    private void WideButton_Click(object sender, RoutedEventArgs e)
    {
        new MainWindow().Show();
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string name)
        {
            StatusText.Text = "P0 · " + name;
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
