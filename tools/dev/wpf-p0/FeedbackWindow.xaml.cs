using System;
using System.Diagnostics;
using System.Windows;
using MechrevoLite;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class FeedbackWindow : Window
{
    private readonly OriginalStyleWindow _owner;

    public FeedbackWindow(OriginalStyleWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        VersionText.Text = "L-Mechrevo " + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion;
        try
        {
            string model = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.Capabilities.Model ?? "";
            DeviceText.Text = WpfStrings.FeedbackWindow_xaml_Text + (string.IsNullOrWhiteSpace(model) ? WpfStrings.CommonUnknown : model)
                + "\n" + WpfStrings.FeedbackSystemLabel + Environment.OSVersion.VersionString;
        }
        catch
        {
            DeviceText.Text = WpfStrings.FeedbackWindow_xaml_Text_2 + Environment.OSVersion.VersionString;
        }
    }

    private string BuildInfo()
    {
        string info = "L-Mechrevo " + L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion + "\n" + DeviceText.Text;
        try
        {
            info += string.Format(WpfStrings.FeedbackGcuLineFormat,
                L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.IsGcuConnected == true ? WpfStrings.FeedbackConnected : WpfStrings.FeedbackDisconnected,
                L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true ? WpfStrings.FeedbackYes : WpfStrings.FeedbackNo,
                L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.WujieSmuApu == true ? WpfStrings.FeedbackYes : WpfStrings.FeedbackNo);
        }
        catch
        {
        }
        return info;
    }

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildInfo());
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_3;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_4 + ex.Message;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_5;
            await _owner.ExportDiagnosticPackForFeedbackAsync();
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_6;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_7 + ex.Message;
        }
    }

    private void Issues_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/LiangyuLu-lly/L-Mechrevo/issues") { UseShellExecute = true });
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_8;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_9 + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
