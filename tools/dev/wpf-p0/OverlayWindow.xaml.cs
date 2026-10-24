using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

public readonly record struct OverlaySnapshot(
    int CpuTemp,
    int CpuUsage,
    int CpuFrequency,
    int GpuTemp,
    int GpuUsage,
    int GpuCoreFreq,
    int CpuFanRpm,
    int GpuFanRpm,
    int RamUsage,
    double RamUsedGb,
    int BatteryPercent,
    float CpuPower,
    float GpuPower);

public partial class OverlayWindow : Window
{
    private readonly Func<OverlaySnapshot> _provider;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public OverlayWindow(Func<OverlaySnapshot> provider)
    {
        InitializeComponent();
        _provider = provider;
        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left + 12, area.Right - Width - 24);
            Top = area.Top + 24;
            Refresh();
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        try
        {
            OverlaySnapshot s = _provider();
            CpuText.Text = s.CpuTemp >= 0 ? "CPU " + s.CpuTemp + "℃" : "CPU —";
            CpuDetailText.Text = FormatCpuDetail(s);
            GpuText.Text = s.GpuTemp >= 0 ? "GPU " + s.GpuTemp + "℃" : "GPU —";
            GpuDetailText.Text = FormatGpuDetail(s);
            FanText.Text = FormatFans(s.CpuFanRpm, s.GpuFanRpm);
            RamText.Text = s.RamUsage >= 0
                ? "内存 " + s.RamUsage + "%" + (s.RamUsedGb > 0 ? " · " + s.RamUsedGb.ToString("0.0") + "GB" : "")
                : "内存 —";
            BatteryText.Text = s.BatteryPercent >= 0 ? "电池 " + s.BatteryPercent + "%" : "电池 —";
        }
        catch
        {
        }
    }

    private static string FormatCpuDetail(OverlaySnapshot s)
    {
        string usage = s.CpuUsage >= 0 ? s.CpuUsage + "%" : "—";
        string freq = s.CpuFrequency > 0 ? (s.CpuFrequency / 1000.0).ToString("0.0") + "GHz" : "—";
        string power = s.CpuPower > 0 ? s.CpuPower.ToString("0.0") + "W" : "—";
        return usage + " · " + freq + " · " + power;
    }

    private static string FormatGpuDetail(OverlaySnapshot s)
    {
        string usage = s.GpuUsage >= 0 ? s.GpuUsage + "%" : "—";
        string freq = s.GpuCoreFreq > 0 ? (s.GpuCoreFreq / 1000.0).ToString("0.0") + "GHz" : "—";
        string power = s.GpuPower > 0 ? s.GpuPower.ToString("0.0") + "W" : "—";
        return usage + " · " + freq + " · " + power;
    }

    private static string FormatFans(int cpu, int gpu)
    {
        if (cpu < 0 && gpu < 0)
        {
            return "风扇 —";
        }
        if (gpu >= 0 && gpu != cpu)
        {
            return "风扇 " + Math.Max(0, cpu) + "/" + gpu + " rpm";
        }
        return "风扇 " + Math.Max(0, cpu) + " rpm";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Close();
    }
}
