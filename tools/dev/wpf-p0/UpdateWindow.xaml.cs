using System;
using System.Diagnostics;
using System.Windows;
using MechrevoLite;
using MechrevoLite.Update;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;

    internal UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;
        Title = WpfStrings.UpdateWindow_xaml_Title + (info.LatestVersion ?? L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion);
        VersionText.Text = WpfStrings.UpdateWindow_xaml_Text + (info.CurrentVersion ?? L_Mechrevo.WpfP0.Services.WpfBackend.ReleaseVersion)
            + "    最新版本：" + (info.LatestVersion ?? "未知")
            + (string.IsNullOrWhiteSpace(info.Channel) ? "" : "    通道：" + info.Channel);
        DateText.Text = string.IsNullOrWhiteSpace(info.ReleaseDate) ? "" : "发布日期：" + info.ReleaseDate;
        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes) ? "该版本没有提供更新说明。" : info.Notes;
    }

    private string? Link => string.IsNullOrWhiteSpace(_info.DownloadPage) ? _info.DownloadUrl : _info.DownloadPage;

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        string? link = Link;
        if (string.IsNullOrWhiteSpace(link))
        {
            StatusText.Text = WpfStrings.UpdateWindow_xaml_Text_2;
            return;
        }
        try
        {
            Clipboard.SetText(link);
            StatusText.Text = WpfStrings.UpdateWindow_xaml_Text_3;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.FeedbackWindow_xaml_Text_4 + ex.Message;
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        string? link = Link;
        if (string.IsNullOrWhiteSpace(link))
        {
            StatusText.Text = WpfStrings.UpdateWindow_xaml_Text_4;
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            StatusText.Text = WpfStrings.UpdateWindow_xaml_Text_5;
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
