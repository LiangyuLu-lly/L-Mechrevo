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
            + "    " + WpfStrings.UpdateLatestVersionLabel + (info.LatestVersion ?? WpfStrings.CommonUnknown)
            + (string.IsNullOrWhiteSpace(info.Channel) ? "" : "    " + WpfStrings.UpdateChannelLabel + info.Channel);
        DateText.Text = string.IsNullOrWhiteSpace(info.ReleaseDate) ? "" : WpfStrings.UpdateReleaseDateLabel + info.ReleaseDate;
        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes) ? WpfStrings.UpdateNoNotes : info.Notes;
    }

    // 有更新包时直接给更新包；只有安装包时才回退到发布页/安装包。
    private string? Link => !string.IsNullOrWhiteSpace(_info.DownloadUrl) ? _info.DownloadUrl : _info.DownloadPage;

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

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        DownloadUpdateButton.IsEnabled = false;
        try
        {
            if (!MechrevoLite.Update.UpdateInstaller.IsDllUpdatePackage(_info))
            {
                // setup 包更新：先下载 NoGCU 安装器；如果更新源是 exe，再拉 GitHub 外置 GCU.7z，
                // 放到 setup 同目录，让 NoGCU 安装器安装 GCU 组件。
                StatusText.Text = WpfStrings.L("正在下载安装包…", "Downloading installer...");
                var setupProgress = new Progress<int>(p => StatusText.Text = WpfStrings.L("正在下载安装包 ", "Downloading installer ") + p + "%");
                MechrevoLite.Update.DownloadResult setupResult = await MechrevoLite.Update.UpdateInstaller.DownloadAsync(_info, setupProgress);
                if (string.IsNullOrWhiteSpace(setupResult.Path))
                {
                    StatusText.Text = WpfStrings.L("下载失败：", "Download failed: ") + (setupResult.Reason ?? WpfStrings.L("未知错误", "Unknown error"));
                    return;
                }
                MechrevoLite.Update.PackageVerification verify = MechrevoLite.Update.UpdateInstaller.Verify(setupResult.Path, _info);
                if (!verify.Ok)
                {
                    StatusText.Text = verify.Reason;
                    return;
                }
                if (!string.IsNullOrWhiteSpace(_info.ExternalGcuUrl))
                {
                    StatusText.Text = WpfStrings.L("正在下载外置 GCU 包…", "Downloading external GCU package...");
                    var gcuInfo = _info with
                    {
                        FileName = string.IsNullOrWhiteSpace(_info.ExternalGcuName) ? "GCU.7z" : _info.ExternalGcuName,
                        Size = _info.ExternalGcuSize,
                        Sha256 = _info.ExternalGcuSha256,
                        DownloadUrl = _info.ExternalGcuUrl
                    };
                    var gcuProgress = new Progress<int>(p => StatusText.Text = WpfStrings.L("正在下载外置 GCU 包 ", "Downloading external GCU package ") + p + "%");
                    MechrevoLite.Update.DownloadResult gcuResult = await MechrevoLite.Update.UpdateInstaller.DownloadAsync(gcuInfo, gcuProgress);
                    if (string.IsNullOrWhiteSpace(gcuResult.Path))
                    {
                        StatusText.Text = WpfStrings.L("外置 GCU 包下载失败：", "External GCU package download failed: ") + (gcuResult.Reason ?? WpfStrings.L("未知错误", "Unknown error"));
                        return;
                    }
                    if (!MechrevoLite.Update.UpdateInstaller.VerifyExternalArchive(gcuResult.Path, _info.ExternalGcuSha256, _info.ExternalGcuSize, out string gcuVerifyReason))
                    {
                        StatusText.Text = gcuVerifyReason;
                        return;
                    }
                    string setupDir = System.IO.Path.GetDirectoryName(setupResult.Path) ?? "";
                    if (!string.IsNullOrWhiteSpace(setupDir))
                    {
                        System.IO.File.Copy(gcuResult.Path, System.IO.Path.Combine(setupDir, "GCU.7z"), overwrite: true);
                    }
                }
                if (!MechrevoLite.Update.UpdateInstaller.StartInstaller(setupResult.Path))
                {
                    StatusText.Text = WpfStrings.L("安装器启动失败", "Failed to start installer");
                    return;
                }
                StatusText.Text = WpfStrings.L("安装器已启动，主程序即将退出", "Installer started; the app will exit");
                Application.Current.Shutdown();
                return;
            }
            StatusText.Text = WpfStrings.L("正在下载更新包…", "Downloading update package...");
            var progress = new Progress<int>(p => StatusText.Text = WpfStrings.L("正在下载更新包 ", "Downloading update package ") + p + "%");
            MechrevoLite.Update.DownloadResult result = await MechrevoLite.Update.UpdateInstaller.DownloadAsync(_info, progress);
            if (string.IsNullOrWhiteSpace(result.Path))
            {
                StatusText.Text = WpfStrings.L("下载失败：", "Download failed: ") + (result.Reason ?? WpfStrings.L("未知错误", "Unknown error"));
                return;
            }
            if (!MechrevoLite.Update.UpdateInstaller.VerifyDllPackage(result.Path, out string verifyReason))
            {
                StatusText.Text = verifyReason;
                return;
            }
            if (!MechrevoLite.Update.UpdateInstaller.PrepareDllUpdater(result.Path, AppContext.BaseDirectory, out string startReason))
            {
                StatusText.Text = startReason;
                return;
            }
            StatusText.Text = WpfStrings.L("更新器已启动，主程序即将退出", "Updater started; the app will exit");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.L("更新失败：", "Update failed: ") + ex.Message;
        }
        finally
        {
            DownloadUpdateButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
