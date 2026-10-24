using System;
using System.Diagnostics;
using System.Windows;
using MechrevoLite;
using MechrevoLite.Update;

namespace L_Mechrevo.WpfP0;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;

    internal UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;
        Title = "检查更新 · " + (info.LatestVersion ?? Program.ReleaseVersion);
        VersionText.Text = "当前版本：" + (info.CurrentVersion ?? Program.ReleaseVersion)
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
            StatusText.Text = "没有可复制的下载链接";
            return;
        }
        try
        {
            Clipboard.SetText(link);
            StatusText.Text = "下载链接已复制";
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        string? link = Link;
        if (string.IsNullOrWhiteSpace(link))
        {
            StatusText.Text = "没有可打开的下载链接";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            StatusText.Text = "已在浏览器打开下载页";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开失败：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
