using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MechrevoLite;

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

    private void Background_Click(object sender, RoutedEventArgs e)
    {
        _owner.SetBackgroundFromSettings(BackgroundCheck.IsChecked == true);
    }

    private void Chinese_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("language", "zh-CN");
        VersionText.Text = "0.293.5 beta23.5 · 中文（重启后生效）";
    }

    private void English_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("language", "en-US");
        VersionText.Text = "0.293.5 beta23.5 · English（重启后生效）";
    }

    private void LogOff_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "off");
        VersionText.Text = "日志：关";
    }

    private void LogError_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "error");
        VersionText.Text = "日志：仅错误";
    }

    private void LogAll_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Set("log_level", "all");
        VersionText.Text = "日志：全部";
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
