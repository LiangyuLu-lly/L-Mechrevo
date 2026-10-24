using System;
using System.Diagnostics;
using System.Windows;
using MechrevoLite;

namespace L_Mechrevo.WpfP0;

public partial class FeedbackWindow : Window
{
    private readonly OriginalStyleWindow _owner;

    public FeedbackWindow(OriginalStyleWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        VersionText.Text = "L-Mechrevo " + Program.ReleaseVersion;
        try
        {
            string model = Program.hw?.Capabilities.Model ?? "";
            DeviceText.Text = "机型：" + (string.IsNullOrWhiteSpace(model) ? "未知" : model)
                + "\n系统：" + Environment.OSVersion.VersionString;
        }
        catch
        {
            DeviceText.Text = "系统：" + Environment.OSVersion.VersionString;
        }
    }

    private string BuildInfo()
    {
        string info = "L-Mechrevo " + Program.ReleaseVersion + "\n" + DeviceText.Text;
        try
        {
            info += "\nGCU：" + (Program.hw?.IsGcuConnected == true ? "已连接" : "未连接")
                + " · 宝龙达：" + (Program.hw?.UsesBaolongda == true ? "是" : "否")
                + " · 无界：" + (Program.hw?.WujieSmuApu == true ? "是" : "否");
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
            StatusText.Text = "信息已复制";
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "正在导出诊断包…";
            await _owner.ExportDiagnosticPackForFeedbackAsync();
            StatusText.Text = "诊断包导出流程已结束";
        }
        catch (Exception ex)
        {
            StatusText.Text = "导出失败：" + ex.Message;
        }
    }

    private void Issues_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/LiangyuLu-lly/L-Mechrevo/issues") { UseShellExecute = true });
            StatusText.Text = "已在浏览器打开 Issues";
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
