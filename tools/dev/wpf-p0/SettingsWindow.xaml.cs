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
namespace L_Mechrevo.WpfP0;

public partial class SettingsWindow : Window
{
    private readonly OriginalStyleWindow _owner;
    private bool _syncing = true;
    private bool _overlaySyncing;
    private bool _isolated;

    public SettingsWindow(OriginalStyleWindow owner)
    {
        InitializeComponent();
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
            Status("设置加载异常：" + ex.Message);
        }
        SourceInitialized += (_, _) =>
  {
      ApplyDarkTitleBar();
      RemoveMinMaxButtons();
  };
  Loaded += (_, _) => PositionAdjacentToOwner();
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
        Status("悬浮窗状态已切换");
    }

    private void OverlayReset_Click(object sender, RoutedEventArgs e)
    {
        ApplyOverlayValues(resetPosition: true);
        Status("悬浮窗位置已重置");
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
        AboutVersionText.Text = "L-Mechrevo " + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion + " " + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseLabel;

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
        BackgroundOpacityText.Text = (int)BackgroundOpacitySlider.Value + "%";
        BackgroundScrimText.Text = (int)BackgroundScrimSlider.Value + "%";
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
        HashSet<string> enabled = TrayMenuConfig.Load();
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
                TrayMenuConfig.Save(selected);
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
        Status("主题色已保存");
    }

    private void DefaultAccent_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("ui_accent_color", "");
        ApplyAccent(Color.FromRgb(59, 130, 246), save: false);
        Status("主题色已恢复默认");
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
            Status("语言已保存，重启后生效");
        }
    }

    private void LogLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LogLevelCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        AppConfig.Set("log_level", item.Tag as string ?? "error");
        Status("日志级别已保存");
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
                Status("开机自启设置失败");
                return;
            }
            AppConfig.Set("startup_enabled", on ? 1 : 0);
            Status(on ? "已启用开机自启" : "已关闭开机自启");
        }
        catch (Exception ex)
        {
            StartupCheck.IsChecked = !on;
            Status("开机自启异常：" + ex.Message);
        }
    }

    private void WaterCooling_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("force_liquid_cooling_menu", WaterCoolingCheck.IsChecked == true ? 1 : 0);
        AppConfig.Flush();
        _owner.ApplyUiVisibilityFromConfig();
        Status("水冷菜单设置已保存");
    }

    private void IgpuRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("igpu_only_ui", value);
            AppConfig.Flush();
            _owner.ApplyUiVisibilityFromConfig();
            Status("核显模式显示：" + value);
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
            Status("灯带行显示：" + value);
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
            Status("Logo 灯行显示：" + value);
        }
    }

    private void RefreshBackgroundPathText()
    {
        string path = AppConfig.GetString("ui_bg_image") ?? "";
        BackgroundPathText.Text = string.IsNullOrWhiteSpace(path)
            ? "未选择"
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
            ? "未选择"
            : System.IO.Path.GetFileName(path);
    }

    private void PickWideBackground_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        AppConfig.Set("wide_bg_image", dialog.FileName);
        AppConfig.Flush();
        RefreshWideBackgroundPathText();
        _owner.RefreshWideBackground();
        Status("已应用宽屏背景图：" + System.IO.Path.GetFileName(dialog.FileName));
    }

    private void ClearWideBackground_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("wide_bg_image", "");
        AppConfig.Flush();
        RefreshWideBackgroundPathText();
        _owner.RefreshWideBackground();
        Status("已清除宽屏背景图");
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
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
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
        Status("已应用背景图：" + System.IO.Path.GetFileName(dialog.FileName));
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("ui_bg_image", "");
        AppConfig.Flush();
        RefreshBackgroundPathText();
        _owner.ReloadBackgroundImage();
        _owner.SetBackgroundFromSettings(false);
        Status("已清除背景图");
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
            Filter = "Zip 压缩包 (*.zip)|*.zip",
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
            Status("已导出 " + result.Included.Count + " 个文件");
        }
        catch (Exception ex)
        {
            Status("导出失败：" + ex.Message);
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = WpfStrings.SettingsWindow_xaml_Title_2,
            Filter = "Zip 压缩包 (*.zip)|*.zip",
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
            Status("设置已导入，重启 L-Mechrevo 生效");
        }
        catch (Exception ex)
        {
            Status("导入失败：" + ex.Message);
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
                Status("显示响应加速切换失败");
            }
            else
            {
                Status(requested ? "显示响应加速已开启" : "显示响应加速已关闭");
            }
        }
        catch (Exception ex)
        {
            Status("显示响应加速异常：" + ex.Message);
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
            Status("当前版本已按原版策略关闭在线更新");
            return;
        }
        Button? updateButton = sender as Button;
        if (updateButton != null)
        {
            updateButton.IsEnabled = false;
        }
        try
        {
            MechrevoLite.Update.UpdateInfo? info = await MechrevoLite.Update.UpdateChecker.CheckAsync(force: true);
            if (info == null)
            {
                Status("检查更新失败：没有返回结果");
                return;
            }
            if (!info.UpdateAvailable)
            {
                Status("已是最新版本：" + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion);
                return;
            }
            new UpdateWindow(info) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Status("检查更新失败：" + ex.Message);
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
            ? "托盘图标显示传感器参数已开启"
            : "托盘图标显示传感器参数已关闭；后台无悬浮窗时暂停传感器读取");
    }

    private void OpenFeedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FeedbackWindow(_owner) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Status("打开反馈失败：" + ex.Message);
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
            Status("打开链接失败：" + ex.Message);
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
            Status("打开许可证失败：" + ex.Message);
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
        if (Owner is not Window owner || owner.WindowState == WindowState.Minimized)
        {
            return;
        }
        Rect area = SystemParameters.WorkArea;
        if (!owner.IsVisible || owner.Left < area.Left - Width || owner.Top < area.Top - Height)
        {
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
            return;
        }
        double targetLeft = owner.Left - Width - 10;
        if (targetLeft < area.Left)
        {
            targetLeft = owner.Left + owner.Width + 10;
        }
        if (targetLeft + Width > area.Right)
        {
            targetLeft = Math.Max(area.Left, area.Right - Width - 8);
        }
        double targetTop = Math.Min(Math.Max(area.Top, owner.Top), area.Bottom - Height - 8);
        Left = targetLeft;
        Top = targetTop;
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
        StatusText.Text = "0.293.5 beta23.5 · " + text;
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
