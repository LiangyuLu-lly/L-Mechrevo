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

namespace L_Mechrevo.WpfP0;

public partial class SettingsWindow : Window
{
    private readonly OriginalStyleWindow _owner;
    private bool _syncing;
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
        }
        catch (Exception ex)
        {
            Status("设置加载异常：" + ex.Message);
        }
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void LoadState()
    {
        _syncing = true;
        DarkButton.IsChecked = _owner.IsDark;
        LightButton.IsChecked = !_owner.IsDark;
        FollowSystemCheck.IsChecked = AppConfig.Is("ui_theme_follow_system");
        StartupCheck.IsChecked = Startup.IsScheduled();
        WaterCoolingCheck.IsChecked = AppConfig.Is("force_liquid_cooling_menu");

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
        _syncing = false;
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
        _owner.ApplyThemeFromSettings(true);
        ApplyDarkTitleBar();
    }

    private void Light_Click(object sender, RoutedEventArgs e)
    {
        LightButton.IsChecked = true;
        DarkButton.IsChecked = false;
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
        Status("语言已保存，重启后生效");
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
        Status("水冷菜单设置已保存");
    }

    private void IgpuRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("igpu_only_ui", value);
            Status("核显模式显示：" + value);
        }
    }

    private void LightStripRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("light_strip_ui", value);
            Status("灯带行显示：" + value);
        }
    }

    private void LightLogoRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string value)
        {
            CheckOnly(button);
            AppConfig.Set("light_logo_ui", value);
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

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
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
            ? (status.OfficialUiRunning ? "官方控制台运行中" : "官方控制台已安装")
            : "未检测到官方控制台";
        IsolationButton.Content = status.Isolated ? "取消隔离" : "隔离官方界面与托盘";
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
            Status("隔离操作失败：" + ex.Message);
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
            ? "PawnIO 驱动：已安装" + (string.IsNullOrWhiteSpace(status.Version) ? "" : "（" + status.Version + "）")
            : "PawnIO 驱动：未安装";
    }

    private async void ReinstallPawnIO_Click(object sender, RoutedEventArgs e)
    {
        ReinstallPawnButton.IsEnabled = false;
        try
        {
            int code = await PawnIOInstaller.RunInstallerAsync();
            Status(code == 0 ? "PawnIO 安装完成" : "PawnIO 安装返回 " + code);
            RefreshPawnIO();
        }
        catch (Exception ex)
        {
            Status("PawnIO 安装失败：" + ex.Message);
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
            Title = "导出设置备份",
            Filter = "Zip 压缩包 (*.zip)|*.zip",
            DefaultExt = "zip",
            FileName = SettingsBackup.BuildFileName(DateTime.Now),
            InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
        };
        if (dialog.ShowDialog(this) != true)
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
            Title = "导入设置备份",
            Filter = "Zip 压缩包 (*.zip)|*.zip",
            InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        if (MessageBox.Show(this, "导入会覆盖现有设置，并自动备份当前设置。继续？", "L-Mechrevo",
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
}
