using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MechrevoLite;
using Microsoft.Win32;

namespace L_Mechrevo.WpfP0;

public partial class SettingsWindow : Window
{
    private readonly OriginalStyleWindow _owner;

    public SettingsWindow(OriginalStyleWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        Owner = owner;
        BackgroundCheck.IsChecked = owner.IsBackgroundEnabled;
        FollowSystemCheck.IsChecked = AppConfig.Is("ui_theme_follow_system");
        StartupCheck.IsChecked = Startup.IsScheduled();
        WaterCoolingCheck.IsChecked = AppConfig.Is("force_liquid_cooling_menu");
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void Dark_Click(object sender, RoutedEventArgs e)
    {
        _owner.ApplyThemeFromSettings(true);
        ApplyDarkTitleBar();
    }

    private void Light_Click(object sender, RoutedEventArgs e)
    {
        _owner.ApplyThemeFromSettings(false);
        ApplyDarkTitleBar();
    }

    private void FollowSystem_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("ui_theme_follow_system", FollowSystemCheck.IsChecked == true ? 1 : 0);
    }

    private void Background_Click(object sender, RoutedEventArgs e)
    {
        _owner.SetBackgroundFromSettings(BackgroundCheck.IsChecked == true);
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = StartupCheck.IsChecked == true;
        try
        {
            bool ok = on ? Startup.Schedule() : Startup.UnSchedule();
            if (!ok)
            {
                Status("开机自启设置失败");
                StartupCheck.IsChecked = !on;
                return;
            }
            AppConfig.Set("startup_enabled", on ? 1 : 0);
            Status(on ? "已启用开机自启" : "已关闭开机自启");
        }
        catch (Exception ex)
        {
            Status("开机自启设置异常：" + ex.Message);
            StartupCheck.IsChecked = !on;
        }
    }

    private void WaterCooling_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("force_liquid_cooling_menu", WaterCoolingCheck.IsChecked == true ? 1 : 0);
        Status("水冷菜单设置已保存");
    }

    private void IgpuRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string value)
        {
            AppConfig.Set("igpu_only_ui", value);
            Status("核显模式显示：" + value);
        }
    }

    private void LightStripRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string value)
        {
            AppConfig.Set("light_strip_ui", value);
            Status("灯带行显示：" + value);
        }
    }

    private void LightLogoRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string value)
        {
            AppConfig.Set("light_logo_ui", value);
            Status("Logo 灯行显示：" + value);
        }
    }

    private void Chinese_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("language", "zh-CN");
        Status("语言：中文，重启后生效");
    }

    private void English_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("language", "en-US");
        Status("Language: English, restart to apply");
    }

    private void LogOff_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "off");
        Status("日志：关");
    }

    private void LogError_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "error");
        Status("日志：仅错误");
    }

    private void LogAll_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "all");
        Status("日志：全部");
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
            Status("设置已导入，重启 LM 生效");
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
        VersionText.Text = "0.293.5 beta23.5 · " + text;
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
