using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using Microsoft.Win32;

using L_Mechrevo.WpfP0.Resources;
using L_Mechrevo.WpfP0.Services;
namespace L_Mechrevo.WpfP0;

public partial class SettingsWindow : Window
{
    private readonly OriginalStyleWindow _owner;
    private readonly PeripheralsService _peripheralsService = new();
    private bool _peripheralRefreshScheduled;
    private bool _syncing = true;
    private bool _overlaySyncing;
    private bool _isolated;

    public SettingsWindow(OriginalStyleWindow owner)
    {
        InitializeComponent();
        L_Mechrevo.WpfP0.Services.WindowWorkAreaPolicy.ClampOnLoaded(this);
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFontFamily"];
        _owner = owner;
        Owner = owner;
        try
        {
            LoadState();
            LoadOverlayState();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.SettingsLoadExceptionPrefix + ex.Message);
        }
        SourceInitialized += (_, _) =>
  {
      ApplyDarkTitleBar();
      RemoveMinMaxButtons();
  };
  Loaded += (_, _) =>
  {
      PositionAdjacentToOwner();
      SchedulePeripheralBatteryRefresh();
  };
    }

    /// <summary>
    /// ⭐ 外设电量：与旧版 <c>RefreshPeripheralsBattery</c> 同一时机（设置页可见后延迟 2.5s、后台读一次）。
    /// <para>⛔ 不新增定时器/高频轮询；服务内部 60s 缓存，失败或没有设备时卡片保持隐藏。</para>
    /// </summary>
    private void SchedulePeripheralBatteryRefresh()
    {
        if (_peripheralRefreshScheduled)
        {
            return;
        }
        _peripheralRefreshScheduled = true;
        _ = RefreshPeripheralBatteryAsync();
    }

    private async Task RefreshPeripheralBatteryAsync()
    {
        try
        {
            // 旧版 SettingsForm 在界面显示后 Task.Delay(2500) 再调 RefreshPeripheralsBattery，
            // 这里沿用同一延迟，避免和设置页自身的加载/主题/后端刷新抢资源。
            await Task.Delay(2500);
            if (!IsVisible)
            {
                _peripheralRefreshScheduled = false;
                return;
            }

            IReadOnlyList<PeripheralBatteryInfo> items = await _peripheralsService.GetAsync();
            ApplyPeripheralBatteries(items);
        }
        catch
        {
            // 读取失败（无蓝牙/HID 权限、无设备等）：保持卡片隐藏，⛔ 不弹错
            TryHidePeripheralCard();
        }
    }

    private void ApplyPeripheralBatteries(IReadOnlyList<PeripheralBatteryInfo> items)
    {
        try
        {
            PeripheralBatteryPanel.Children.Clear();
            if (items == null || items.Count == 0)
            {
                TryHidePeripheralCard();
                return;
            }

            foreach (PeripheralBatteryInfo item in items.Take(3))
            {
                Grid row = new() { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock name = new()
                {
                    Text = item.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock source = new()
                {
                    Text = item.Source,
                    Style = (Style)FindResource("Hint"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock percent = new()
                {
                    Text = item.Percent + "%",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(source, 1);
                Grid.SetColumn(percent, 2);
                row.Children.Add(name);
                row.Children.Add(source);
                row.Children.Add(percent);
                PeripheralBatteryPanel.Children.Add(row);
            }

            PeripheralBatteryStatus.Text = items.Count > 3 ? WpfStrings.PeripheralCountPrefix + items.Count + WpfStrings.UnitDevicesSuffix : "";
            PeripheralBatteryCard.Visibility = Visibility.Visible;
        }
        catch
        {
            TryHidePeripheralCard();
        }
    }

    private void TryHidePeripheralCard()
    {
        try
        {
            PeripheralBatteryCard.Visibility = Visibility.Collapsed;
            PeripheralBatteryStatus.Text = "";
        }
        catch
        {
        }
    }

    private void LoadOverlayState()
    {
        _overlaySyncing = true;
        try
        {
            int mode = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_wpf_mode", 1), 0, 2);
            int opacity = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_opacity", 72), 30, 100);
            int interval = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_interval_ms", 1000), 500, 10000);
            foreach (ComboBoxItem item in OverlayModeCombo.Items)
            {
                if (item.Tag as string == mode.ToString())
                {
                    OverlayModeCombo.SelectedItem = item;
                    break;
                }
            }
            OverlayOpacitySlider.Value = opacity;
            OverlayOpacityText.Text = opacity + "%";
            OverlayLockCheck.IsChecked = _owner?.WideOverlayLocked == true;
            foreach (ComboBoxItem item in OverlayIntervalCombo.Items)
            {
                if (item.Tag as string == interval.ToString())
                {
                    OverlayIntervalCombo.SelectedItem = item;
                    break;
                }
            }
        }
        catch
        {
        }
        finally
        {
            _overlaySyncing = false;
        }
    }

    private (int Mode, int Opacity, int Interval) OverlayValues()
    {
        int mode = 1;
        int interval = 1000;
        double opacity = 80;
        if (OverlayModeCombo?.SelectedItem is ComboBoxItem modeItem
            && modeItem.Tag is string modeText && int.TryParse(modeText, out int parsedMode))
        {
            mode = parsedMode;
        }
        if (OverlayIntervalCombo?.SelectedItem is ComboBoxItem intervalItem
            && intervalItem.Tag is string intervalText && int.TryParse(intervalText, out int parsedInterval))
        {
            interval = parsedInterval;
        }
        if (OverlayOpacitySlider != null)
        {
            opacity = OverlayOpacitySlider.Value;
        }
        return (mode, (int)Math.Round(opacity), interval);
    }

    private void ApplyOverlayValues(bool resetPosition = false)
    {
        if (_overlaySyncing || _owner == null || OverlayOpacitySlider == null)
        {
            return;
        }
        (int mode, int opacity, int interval) = OverlayValues();
        MechrevoLite.AppConfig.Set("overlay_wpf_mode", mode);
        MechrevoLite.AppConfig.Set("overlay_opacity", opacity);
        MechrevoLite.AppConfig.Set("overlay_interval_ms", interval);
        _owner.ApplyOverlayAppearance(opacity, mode, interval, resetPosition);
    }

    private void OverlayMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyOverlayValues();
    }

    private void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OverlayOpacityText != null)
        {
            OverlayOpacityText.Text = ((int)Math.Round(e.NewValue)) + "%";
        }
        ApplyOverlayValues();
    }

    private void OverlayInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyOverlayValues();
    }

    private void OverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        _owner.ToggleOverlayFromSettings();
        Status(WpfStrings.OverlayStateToggled);
    }

    private void OverlayLock_Click(object sender, RoutedEventArgs e)
    {
        bool locked = OverlayLockCheck.IsChecked == true;
        _owner?.SetOverlayLockedFromSettings(locked);
        Status(locked ? "悬浮窗已锁定，可在托盘菜单或设置中解锁" : "悬浮窗已解锁");
    }

    private void OverlayReset_Click(object sender, RoutedEventArgs e)
    {
        ApplyOverlayValues(resetPosition: true);
        Status(WpfStrings.OverlayPositionReset);
    }

    private void LoadState()
    {
        _syncing = true;
        DarkButton.IsChecked = _owner.IsDark;
        LightButton.IsChecked = !_owner.IsDark;
        FollowSystemCheck.IsChecked = AppConfig.Is("ui_theme_follow_system");
        StartupCheck.IsChecked = Startup.IsScheduled();
        WaterCoolingCheck.IsChecked = AppConfig.Is("force_liquid_cooling_menu");
        TraySensorTooltipCheck.IsChecked = _owner.WideTraySensorEnabled;
        DisplayCard.Visibility = _owner.SupportsLcdOverdrive || _owner.SupportsMiniled || _owner.SupportsFhd || _owner.SupportsHdrControl
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverdriveCheck.IsChecked = _owner.LcdOverdrive;
        MiniledRow.Visibility = _owner.SupportsMiniled ? Visibility.Visible : Visibility.Collapsed;
        MiniledCheck.IsChecked = _owner.MiniledOn;
        HdrControlRow.Visibility = _owner.SupportsHdrControl ? Visibility.Visible : Visibility.Collapsed;
        HdrControlCheck.IsChecked = _owner.HdrControlOn;
        FhdRow.Visibility = _owner.SupportsFhd ? Visibility.Visible : Visibility.Collapsed;
        FhdButton.Content = _owner.FhdOn ? "UHD" : "FHD";
        AboutVersionText.Text = "L-Mechrevo " + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseLabel;

        string language = AppConfig.GetString("language") ?? "zh-CN";
        LanguageCombo.SelectedIndex = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string level = AppConfig.GetString("log_level") ?? "error";
        LogLevelCombo.SelectedIndex = level switch
        {
            "off" => 0,
            "all" => 2,
            _ => 1
        };

        SetChoices(AppConfig.GetString("igpu_only_ui") ?? "auto", IgpuAutoButton, IgpuShowButton, IgpuHideButton);
        SetChoices(AppConfig.GetString("light_strip_ui") ?? "auto", StripAutoButton, StripShowButton, StripHideButton);
        SetChoices(AppConfig.GetString("light_logo_ui") ?? "auto", LogoAutoButton, LogoShowButton, LogoHideButton);

        string? accentText = AppConfig.GetString("ui_accent_color");
        if (!string.IsNullOrWhiteSpace(accentText))
        {
            try
            {
                ApplyAccent((Color)ColorConverter.ConvertFromString(accentText), save: false);
            }
            catch
            {
            }
        }

        RefreshOfficialConsole();
        RefreshPawnIO();
        BackgroundOpacitySlider.Value = Math.Clamp(AppConfig.Get("ui_bg_opacity", 65), 0, 100);
        BackgroundScrimSlider.Value = Math.Clamp(AppConfig.Get("ui_bg_scrim", 50), 0, 100);
        BackgroundBlurSlider.Value = Math.Clamp(AppConfig.Get("ui_bg_blur", 12), 0, 40);
        BackgroundOpacityText.Text = (int)BackgroundOpacitySlider.Value + "%";
        BackgroundScrimText.Text = (int)BackgroundScrimSlider.Value + "%";
        BackgroundBlurText.Text = ((int)BackgroundBlurSlider.Value).ToString();
        WideBackgroundOpacitySlider.Value = Math.Clamp(AppConfig.Get("wide_bg_opacity", 78), 0, 100);
        WideBackgroundOpacityText.Text = (int)WideBackgroundOpacitySlider.Value + "%";
        RefreshBackgroundPathText();
        RefreshWideBackgroundPathText();
        DockOpacitySlider.Value = Math.Clamp(AppConfig.Get("ui_dock_opacity", 70), 0, 100);
        DockOpacityText.Text = (int)DockOpacitySlider.Value + "%";
        CardOpacitySlider.Value = Math.Clamp(AppConfig.Get("ui_card_opacity", 100), 0, 100);
        CardOpacityText.Text = (int)CardOpacitySlider.Value + "%";
        ButtonOpacitySlider.Value = Math.Clamp(AppConfig.Get("ui_button_opacity", 60), 0, 100);
        ButtonOpacityText.Text = (int)ButtonOpacitySlider.Value + "%";
        CheckUpdateButton.Visibility = L_Mechrevo.WpfP0.Services.UpdatePolicy.TemporarilyHidden
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (FeedbackButtonsPanel != null)
        {
            FeedbackButtonsPanel.Visibility = AppConfig.Is("ui_show_privacy_feedback")
                ? Visibility.Visible : Visibility.Collapsed;
        }
        _syncing = false;
        BuildTrayMenuOptions();
    }

    private void BuildTrayMenuOptions()
    {
        if (TrayMenuPanel == null) return;
        TrayMenuPanel.Children.Clear();
        HashSet<string> enabled = _owner.Services != null ? _owner.Services.TrayMenu.LoadEnabled() : TrayMenuConfig.Load();
        foreach ((string key, string label) in TrayMenuConfig.Catalog)
        {
            CheckBox box = new()
            {
                Content = label,
                Style = (Style)FindResource("HeaderSwitch"),
                IsChecked = enabled.Contains(key),
                Tag = key,
                Margin = new Thickness(0, 0, 14, 8)
            };
            box.Click += (_, _) =>
            {
                List<string> selected = new();
                foreach (object child in TrayMenuPanel.Children)
                {
                    if (child is CheckBox item && item.IsChecked == true && item.Tag is string itemKey)
                    {
                        selected.Add(itemKey);
                    }
                }
                if (_owner.Services != null) { _owner.Services.TrayMenu.SaveEnabled(selected); } else { TrayMenuConfig.Save(selected); }
                _owner.WideRefreshTrayMenu();
                Status(WpfStrings.TraySaved);
            };
            TrayMenuPanel.Children.Add(box);
        }
    }

    private static void SetChoices(string value, params ToggleButton[] buttons)
    {
        foreach (ToggleButton button in buttons)
        {
            button.IsChecked = string.Equals(button.Tag as string, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void CheckOnly(ToggleButton selected)
    {
        if (selected.Parent is not StackPanel panel)
        {
            return;
        }
        foreach (object child in panel.Children)
        {
            if (child is ToggleButton button)
            {
                button.IsChecked = ReferenceEquals(button, selected);
            }
        }
    }

    private void Dark_Click(object sender, RoutedEventArgs e)
    {
        DarkButton.IsChecked = true;
        LightButton.IsChecked = false;
        AppConfig.Set("ui_dark_theme", 1);
        AppConfig.Flush();
        _owner.ApplyThemeFromSettings(true);
        ApplyDarkTitleBar();
    }

    private void Light_Click(object sender, RoutedEventArgs e)
    {
        LightButton.IsChecked = true;
        DarkButton.IsChecked = false;
        AppConfig.Set("ui_dark_theme", 0);
        AppConfig.Flush();
        _owner.ApplyThemeFromSettings(false);
        ApplyDarkTitleBar();
    }

    private void FollowSystem_Click(object sender, RoutedEventArgs e)
    {
        bool on = FollowSystemCheck.IsChecked == true;
        AppConfig.Set("ui_theme_follow_system", on ? 1 : 0);
        if (on)
        {
            bool systemDark = IsSystemDarkTheme();
            _owner.ApplyThemeFromSettings(systemDark);
            DarkButton.IsChecked = systemDark;
            LightButton.IsChecked = !systemDark;
            ApplyDarkTitleBar();
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }

    private void PickAccent_Click(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(
                (Application.Current.Resources["AccentColor"] as Color? ?? Color.FromRgb(59, 130, 246)).R,
                (Application.Current.Resources["AccentColor"] as Color? ?? Color.FromRgb(59, 130, 246)).G,
                (Application.Current.Resources["AccentColor"] as Color? ?? Color.FromRgb(59, 130, 246)).B)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        ApplyAccent(Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B), save: true);
        Status(WpfStrings.AccentSaved);
    }

    private void DefaultAccent_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("ui_accent_color", "");
        ApplyAccent(Color.FromRgb(59, 130, 246), save: false);
        Status(WpfStrings.AccentReset);
    }

    private void ApplyAccent(Color color, bool save)
    {
        Application.Current.Resources["AccentColor"] = color;
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
        AccentSwatch.Background = new SolidColorBrush(color);
        if (save)
        {
            AppConfig.Set("ui_accent_color", "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"));
        }
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LanguageCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        AppConfig.Set("language", item.Tag as string ?? "zh-CN");
        AppConfig.Flush();
        // 立即同步 GCU 悬浮窗语言；LM 自身界面仍按提示重启生效。
        _owner.SyncGcuLanguage(item.Tag as string ?? "zh-CN");
        MessageBoxResult restart = MessageBox.Show(
            this,
            WpfStrings.LanguageRestartPrompt,
            WpfStrings.LanguageRestartTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (restart == MessageBoxResult.Yes)
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    Process.Start(new ProcessStartInfo(
                        "cmd.exe",
                        "/c timeout /t 2 >nul & start \"\" \"" + exe + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
            }
            catch
            {
            }
            Application.Current.Shutdown();
        }
        else
        {
            Status(WpfStrings.LanguageSaved);
        }
    }

    private void LogLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LogLevelCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        AppConfig.Set("log_level", item.Tag as string ?? "error");
        Status(WpfStrings.LogLevelSaved);
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = StartupCheck.IsChecked == true;
        try
        {
            bool ok = on ? Startup.Schedule() : Startup.UnSchedule();
            if (!ok)
            {
                StartupCheck.IsChecked = !on;
                Status(WpfStrings.StartupSettingFailed);
                return;
            }
            AppConfig.Set("startup_enabled", on ? 1 : 0);
            Status(on ? WpfStrings.StartupEnabled : WpfStrings.StartupDisabled);
        }
        catch (Exception ex)
        {
            StartupCheck.IsChecked = !on;
            Status(WpfStrings.StartupSettingExceptionPrefix + ex.Message);
        }
    }

    private void WaterCooling_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("force_liquid_cooling_menu", WaterCoolingCheck.IsChecked == true ? 1 : 0);
        AppConfig.Flush();
        _owner.ApplyUiVisibilityFromConfig();
        Status(WpfStrings.LiquidMenuSaved);
    }

    private void IgpuRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("igpu_only_ui", value);
            AppConfig.Flush();
            _owner.ApplyUiVisibilityFromConfig();
            Status(WpfStrings.IgpuModeDisplayPrefix + value);
        }
    }

    private void LightStripRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("light_strip_ui", value);
            AppConfig.Flush();
            _owner.ApplyUiVisibilityFromConfig();
            Status(WpfStrings.LightStripRowPrefix + value);
        }
    }

    private void LightLogoRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("light_logo_ui", value);
            AppConfig.Flush();
            _owner.ApplyUiVisibilityFromConfig();
            Status(WpfStrings.LogoRowPrefix + value);
        }
    }

    private void RefreshBackgroundPathText()
    {
        string path = AppConfig.GetString("ui_bg_image") ?? "";
        BackgroundPathText.Text = string.IsNullOrWhiteSpace(path)
            ? WpfStrings.ImageNotSelected
            : System.IO.Path.GetFileName(path);
    }

    private void CardOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || CardOpacityText == null) return;
        int value = (int)Math.Round(e.NewValue);
        CardOpacityText.Text = value + "%";
        AppConfig.Set("ui_card_opacity", value);
        AppConfig.Flush();
        AppTheme.ApplyCardOpacity();
    }

    private void ButtonOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || ButtonOpacityText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        ButtonOpacityText.Text = value + "%";
        AppConfig.Set("ui_button_opacity", value);
        AppConfig.Flush();
        _owner.ApplyButtonOpacity();
    }

    private void DockOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || DockOpacityText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        DockOpacityText.Text = value + "%";
        AppConfig.Set("ui_dock_opacity", value);
        AppConfig.Flush();
        _owner.ApplyDockOpacity();
    }

    private void BackgroundOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || BackgroundOpacityText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        BackgroundOpacityText.Text = value + "%";
        AppConfig.Set("ui_bg_opacity", value);
        AppConfig.Flush();
        _owner.ApplyBackgroundAppearance();
    }

    private void BackgroundBlur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || BackgroundBlurText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        BackgroundBlurText.Text = value.ToString();
        AppConfig.Set("ui_bg_blur", value);
        AppConfig.Flush();
        _owner.ApplyBackgroundAppearance();
        _owner.RefreshWideBackground();
    }

    private void BackgroundScrim_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || BackgroundScrimText == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        BackgroundScrimText.Text = value + "%";
        AppConfig.Set("ui_bg_scrim", value);
        AppConfig.Flush();
        _owner.ApplyBackgroundAppearance();
    }

    private void RefreshWideBackgroundPathText()
    {
        string path = AppConfig.GetString("wide_bg_image") ?? "";
        WideBackgroundPathText.Text = string.IsNullOrWhiteSpace(path)
            ? WpfStrings.ImageNotSelected
            : System.IO.Path.GetFileName(path);
    }

    private void PickWideBackground_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = WpfStrings.ImageFileFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        AppConfig.Set("wide_bg_image", dialog.FileName);
        AppConfig.Flush();
        RefreshWideBackgroundPathText();
        _owner.RefreshWideBackground();
        Status(WpfStrings.WideBackgroundAppliedPrefix + System.IO.Path.GetFileName(dialog.FileName));
    }

    private void ClearWideBackground_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("wide_bg_image", "");
        AppConfig.Flush();
        RefreshWideBackgroundPathText();
        _owner.RefreshWideBackground();
        Status(WpfStrings.WideBackgroundCleared);
    }

    private void WideBackgroundOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || WideBackgroundOpacityText == null) return;
        int value = (int)Math.Round(e.NewValue);
        WideBackgroundOpacityText.Text = value + "%";
        AppConfig.Set("wide_bg_opacity", value);
        AppConfig.Flush();
        _owner.RefreshWideBackground();
    }

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Filter = WpfStrings.ImageFileFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        AppConfig.Set("ui_bg_image", dialog.FileName);
        AppConfig.Flush();
        RefreshBackgroundPathText();
        _owner.ReloadBackgroundImage();
        _owner.SetBackgroundFromSettings(true);
        Status(WpfStrings.BackgroundAppliedPrefix + System.IO.Path.GetFileName(dialog.FileName));
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("ui_bg_image", "");
        AppConfig.Flush();
        RefreshBackgroundPathText();
        _owner.ReloadBackgroundImage();
        _owner.SetBackgroundFromSettings(false);
        Status(WpfStrings.BackgroundCleared);
    }

    private void RefreshOfficialConsole()
    {
        var status = OfficialConsoleIsolation.GetStatus(_owner.BackendConnected);
        _isolated = status.Isolated;
        OfficialStatus.Text = status.OfficialUiInstalled
            ? (status.OfficialUiRunning ? WpfStrings.OfficialRunning : WpfStrings.OfficialInstalled)
            : WpfStrings.OfficialNotDetected;
        IsolationButton.Content = status.Isolated ? WpfStrings.CancelIsolation : WpfStrings.IsolateOfficial;
    }

    private async void Isolation_Click(object sender, RoutedEventArgs e)
    {
        IsolationButton.IsEnabled = false;
        try
        {
            var result = await OfficialConsoleIsolation.SetIsolationAsync(!_isolated);
            Status(result.Message);
            RefreshOfficialConsole();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.IsolationFailed + ex.Message);
        }
        finally
        {
            IsolationButton.IsEnabled = true;
        }
    }

    private void RefreshPawnIO()
    {
        PawnIOInstaller.Status status = PawnIOInstaller.GetStatus();
        PawnStatus.Text = status.Installed
            ? WpfStrings.PawnDriverInstalled + (string.IsNullOrWhiteSpace(status.Version) ? "" : " (" + status.Version + ")")
            : WpfStrings.PawnDriverNotInstalled;
    }

    private async void ReinstallPawnIO_Click(object sender, RoutedEventArgs e)
    {
        ReinstallPawnButton.IsEnabled = false;
        try
        {
            int code = await PawnIOInstaller.RunInstallerAsync();
            Status(code == 0 ? WpfStrings.PawnInstallDone : WpfStrings.PawnInstallReturn + code);
            RefreshPawnIO();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.PawnInstallFailed + ex.Message);
        }
        finally
        {
            ReinstallPawnButton.IsEnabled = true;
        }
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new SaveFileDialog
        {
            Title = WpfStrings.SettingsWindow_xaml_Title,
            Filter = WpfStrings.ZipFileFilter,
            DefaultExt = "zip",
            FileName = SettingsBackup.BuildFileName(DateTime.Now),
            InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            SettingsBackupExportResult result = SettingsBackup.Export(dialog.FileName, DateTime.Now);
            Status(WpfStrings.BackupExportedPrefix + result.Included.Count + WpfStrings.BackupExportedSuffix);
        }
        catch (Exception ex)
        {
            Status(WpfStrings.ExportFailedPrefix + ex.Message);
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = WpfStrings.SettingsWindow_xaml_Title_2,
            Filter = WpfStrings.ZipFileFilter,
            InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        if (MessageBox.Show(this, WpfStrings.SettingsWindow_xaml_MessageBoxShowthis, "L-Mechrevo",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            SettingsBackup.Import(dialog.FileName, DateTime.Now);
            Status(WpfStrings.SettingsImported);
        }
        catch (Exception ex)
        {
            Status(WpfStrings.ImportFailedPrefix + ex.Message);
        }
    }

    private void Miniled_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        _owner.ToggleMiniled();
        MiniledCheck.IsChecked = _owner.MiniledOn;
    }

    private void HdrControl_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        _owner.ToggleHdrControl();
        HdrControlCheck.IsChecked = _owner.HdrControlOn;
    }

    private void Fhd_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        _owner.ToggleFhd();
    }

    private async void Overdrive_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        bool requested = OverdriveCheck.IsChecked == true;
        OverdriveCheck.IsEnabled = false;
        try
        {
            bool ok = await _owner.SetLcdOverdriveAsync(requested);
            if (!ok)
            {
                _syncing = true;
                OverdriveCheck.IsChecked = _owner.LcdOverdrive;
                _syncing = false;
                Status(WpfStrings.DisplayResponseToggleFailed);
            }
            else
            {
                Status(requested ? WpfStrings.DisplayResponseOn : WpfStrings.DisplayResponseOff);
            }
        }
        catch (Exception ex)
        {
            Status(WpfStrings.DisplayResponseExceptionPrefix + ex.Message);
        }
        finally
        {
            OverdriveCheck.IsEnabled = true;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (L_Mechrevo.WpfP0.Services.UpdatePolicy.TemporarilyHidden)
        {
            Status(WpfStrings.OnlineUpdateDisabled);
            return;
        }
        Button? updateButton = sender as Button;
        if (updateButton != null)
        {
            updateButton.IsEnabled = false;
        }
        try
        {
            MechrevoLite.Update.UpdateInfo? info = await MechrevoLite.Update.UpdateChecker.CheckGitHubAsync(force: true);
            if (info == null)
            {
                Status(WpfStrings.UpdateCheckNoResult);
                return;
            }
            if (!info.UpdateAvailable)
            {
                Status(WpfStrings.UpdateLatestPrefix + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion);
                return;
            }
            new UpdateWindow(info) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.UpdateCheckFailedPrefix + ex.Message);
        }
        finally
        {
            if (updateButton != null)
            {
                updateButton.IsEnabled = true;
            }
        }
    }

    private void Hotkeys_Click(object sender, RoutedEventArgs e)
    {
        new HotkeySettingsWindow { Owner = this }.ShowDialog();
    }

    private void TraySensorTooltip_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        _owner.WideSetTraySensorEnabled(TraySensorTooltipCheck.IsChecked == true);
        Status(TraySensorTooltipCheck.IsChecked == true
            ? WpfStrings.TraySensorOn
            : WpfStrings.TraySensorOff);
    }

    private void OpenFeedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FeedbackWindow(_owner) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.FeedbackOpenFailedPrefix + ex.Message);
        }
    }

    private void OpenIssues_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/LiangyuLu-lly/L-Mechrevo/issues")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status(WpfStrings.LinkOpenFailedPrefix + ex.Message);
        }
    }

    private void OpenLicenses_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new LicensesWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Status(WpfStrings.LicenseOpenFailedPrefix + ex.Message);
        }
    }

    private void RemoveMinMaxButtons()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        const int GWL_STYLE = -16;
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        nint style = GetWindowLongPtr(hwnd, GWL_STYLE);
        long value = style.ToInt64();
        value &= ~(long)(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(hwnd, GWL_STYLE, new nint(value));
        SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, 0x0027);
    }

    private void PositionAdjacentToOwner()
    {
        // 宽屏设置窗统一居中显示，不再贴主窗右侧。
        Rect area = SystemParameters.WorkArea;
        double width = Width > 0 ? Width : ActualWidth;
        double height = Height > 0 ? Height : ActualHeight;
        Left = area.Left + Math.Max(0, (area.Width - width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - height) / 2);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void Status(string text)
    {
        StatusText.Text = "0.293.6 beta23.6 · " + text;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyDarkTitleBar()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        int enabled = _owner.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
