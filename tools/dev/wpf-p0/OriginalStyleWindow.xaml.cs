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
        SourceInitialized += (_, _) => HideMinMaxButtons();
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

    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

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
        (string Key, string Label, string Glyph)[] choices =
        {
            ("fanboost", "风扇强冷", "\uE9CA"), ("webcam", "摄像头", "\uE722"),
            ("micmute", "麦克风", "\uE720"), ("winkey", "Win键", "\uE765"),
            ("monitoroff", "息屏", "\uE7F4"), ("touchpad", "触摸板", "\uE7C9"),
            ("wifi", "WiFi", "\uE701"), ("bt", "蓝牙", "\uE702")
        };
        List<QuickChoice> list = new List<QuickChoice>();
        foreach ((string key, string label, string glyph) in choices)
        {
            if ((_backendProbe?.SupportsQuick(key) ?? true) && !HasQuickTile(key))
            {
                list.Add(new QuickChoice(key, label, glyph));
            }
        }
        if (list.Count == 0)
        {
            StatusText.Text = "没有可添加的快捷开关";
            return;
        }
        QuickTilePickerWindow picker = new QuickTilePickerWindow(list)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true && picker.SelectedChoice is QuickChoice choice)
        {
            AddQuickTile(choice);
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

    private void AddQuickTile(QuickChoice choice)
    {
        ToggleButton tile = new ToggleButton
        {
            Style = (Style)FindResource("Tile"),
            Tag = choice.Key,
            IsChecked = _backendProbe?.QuickState(choice.Key) == true
        };
        tile.Click += QuickTile_Click;
        StackPanel content = new StackPanel();
        TextBlock icon = new TextBlock
        {
            Text = choice.Glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        TextBlock label = new TextBlock
        {
            Text = choice.Label,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(icon);
        content.Children.Add(label);
        tile.Content = content;
        int customIndex = QuickTileGrid.Children.IndexOf(CustomTile);
        if (customIndex < 0)
        {
            QuickTileGrid.Children.Add(tile);
        }
        else
        {
            QuickTileGrid.Children.Insert(customIndex, tile);
        }
        QuickTileGrid.Columns = Math.Max(6, QuickTileGrid.Children.Count);
        StatusText.Text = "已添加快捷开关：" + choice.Label;
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
                    FontSize = 9,
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
                ToggleButton ac = new ToggleButton { Content = "通电时", Style = (Style)FindResource("SmallSegment"), IsChecked = true, Tag = "ac", Margin = new Thickness(0, 0, 4, 0), Height = 24, FontSize = 9 };
                ToggleButton dc = new ToggleButton { Content = "离电时", Style = (Style)FindResource("SmallSegment"), Tag = "dc", Height = 24, FontSize = 9 };
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
