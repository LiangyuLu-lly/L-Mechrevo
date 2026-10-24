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
    int CpuFanDuty,
    int GpuFanDuty,
    int RamUsage,
    double RamUsedGb,
    int BatteryPercent,
    bool AcOnline,
    bool Charging,
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
            if (Left <= 0 || Top <= 0)
            {
                Rect area = SystemParameters.WorkArea;
                Left = Math.Max(area.Left + 12, area.Right - Width - 24);
                Top = area.Top + 24;
            }
            Refresh();
            _timer.Tick += (_, _) => Refresh();
            if (IsVisible)
            {
                _timer.Start();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
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
            CpuFanText.Text = FormatFan(s.CpuFanRpm, s.CpuFanDuty);
            GpuFanText.Text = FormatFan(s.GpuFanRpm, s.GpuFanDuty);
            RamText.Text = s.RamUsage >= 0
                ? "内存 " + s.RamUsage + "%" + (s.RamUsedGb > 0 ? " · " + s.RamUsedGb.ToString("0.0") + "GB" : "")
                : "内存 —";
            string source = FormatPowerSource(s.AcOnline);
            string state = s.Charging ? "充电中" : (s.AcOnline ? (s.BatteryPercent >= 99 ? "已充满" : "未充电") : "放电中");
            BatteryText.Text = s.BatteryPercent >= 0
                ? "电池 " + s.BatteryPercent + "% · " + source + " · " + state
                : "电池 —";
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
        return power + " · " + freq + " · " + usage;
    }

    private static string FormatGpuDetail(OverlaySnapshot s)
    {
        string usage = s.GpuUsage >= 0 ? s.GpuUsage + "%" : "—";
        string freq = s.GpuCoreFreq > 0 ? (s.GpuCoreFreq / 1000.0).ToString("0.0") + "GHz" : "—";
        string power = s.GpuPower > 0 ? s.GpuPower.ToString("0.0") + "W" : "—";
        return power + " · " + freq + " · " + usage;
    }

    private static string FormatPowerSource(bool acOnline)
    {
        if (!acOnline)
        {
            return "电池供电";
        }
        try
        {
            if (MechrevoLite.Program.hw?.UsesBaolongda == true)
            {
                // 宝龙达没有适配器功率表，保持原版「外接电源」，不显示“未识别”。
                return "外接电源";
            }
            MechrevoLite.Hardware.PowerInputSample? sample = MechrevoLite.HardwareControl.PowerInput;
            MechrevoLite.Hardware.PowerInputKind kind = MechrevoLite.Hardware.PowerInputDecoder.Reconcile(
                sample, acOnline);
            if (sample?.AdapterWatts is int watts)
            {
                string text = MechrevoLite.SettingsForm.PowerSourceHeadline(kind, watts);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            int raw = sample?.BiosInfo3Raw ?? -1;
            if (raw >= 0)
            {
                // 同方机型不在功率表里时，直接显示 EC 原始读数。
                return "外接电源（EC 0x" + raw.ToString("X2") + "）";
            }
            decimal? rate = MechrevoLite.HardwareControl.batteryRate;
            if (rate.HasValue)
            {
                return "外接电源（EC " + rate.Value.ToString("0.0") + "W）";
            }
        }
        catch
        {
        }
        return "外接电源";
    }

    private static string FormatFan(int rpm, int duty)
    {
        string speed = rpm > 0 ? rpm + "rpm" : "—";
        string percent = duty >= 0 ? duty + "%" : "—";
        return speed + " " + percent;
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
        Hide();
    }
}
