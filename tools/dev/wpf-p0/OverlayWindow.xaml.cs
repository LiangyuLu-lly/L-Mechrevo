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
    private int _displayMode = 1;
    private Point _pressPoint;
    private bool _pressed;
    private bool _dragging;

    public OverlayWindow(Func<OverlaySnapshot> provider)
    {
        InitializeComponent();
        _provider = provider;
        try
        {
            _displayMode = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_wpf_mode", 1), 0, 2);
            Opacity = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_opacity", 100), 30, 100) / 100.0;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(MechrevoLite.AppConfig.Get("overlay_interval_ms", 1000), 500, 10000));
        }
        catch
        {
            _displayMode = 1;
        }
        ApplyDisplayMode();
        Loaded += (_, _) =>
        {
            RestorePosition();
            if (double.IsNaN(Left) || double.IsNaN(Top) || Left <= 0 || Top <= 0)
            {
                ResetPosition();
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
            BatteryText.Text = s.BatteryPercent >= 0 ? "电池 " + s.BatteryPercent + "%" : "电池 —";
            BatteryDetailText.Text = s.BatteryPercent >= 0 ? source + " · " + state : "";
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

    internal void ApplyAppearance(double opacity, int mode, int intervalMs, bool resetPosition)
    {
        Opacity = Math.Clamp(opacity, 30, 100) / 100.0;
        _displayMode = Math.Clamp(mode, 0, 2);
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 500, 10000));
        ApplyDisplayMode();
        if (resetPosition)
        {
            ResetPosition();
            SavePosition();
        }
        try
        {
            MechrevoLite.AppConfig.Set("overlay_opacity", (int)Math.Round(opacity));
            MechrevoLite.AppConfig.Set("overlay_interval_ms", intervalMs);
            MechrevoLite.AppConfig.Set("overlay_wpf_mode", _displayMode);
        }
        catch
        {
        }
    }

    internal int DisplayMode => _displayMode;

    private void ResetPosition()
    {
        Rect area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 12, area.Right - Width - 24);
        Top = area.Top + 24;
    }

    private void RestorePosition()
    {
        try
        {
            if (MechrevoLite.AppConfig.Exists("overlay_wpf_left") && MechrevoLite.AppConfig.Exists("overlay_wpf_top"))
            {
                Left = MechrevoLite.AppConfig.Get("overlay_wpf_left", (int)Math.Round(Left));
                Top = MechrevoLite.AppConfig.Get("overlay_wpf_top", (int)Math.Round(Top));
            }
        }
        catch
        {
        }
    }

    private void SavePosition()
    {
        try
        {
            MechrevoLite.AppConfig.Set("overlay_wpf_left", (int)Math.Round(Left));
            MechrevoLite.AppConfig.Set("overlay_wpf_top", (int)Math.Round(Top));
        }
        catch
        {
        }
    }

    private void ApplyDisplayMode()
    {
        bool standard = _displayMode >= 1;
        bool complete = _displayMode >= 2;
        CpuDetailText.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        CpuFanText.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        GpuDetailText.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        GpuFanText.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
        RamText.Visibility = complete ? Visibility.Visible : Visibility.Collapsed;
        BatteryText.Visibility = Visibility.Visible;
    }

    private void CycleDisplayMode()
    {
        _displayMode = (_displayMode + 1) % 3;
        try
        {
            MechrevoLite.AppConfig.Set("overlay_wpf_mode", _displayMode);
        }
        catch
        {
        }
        ApplyDisplayMode();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }
        _pressed = true;
        _dragging = false;
        _pressPoint = e.GetPosition(this);
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _pressPoint.X) > 5 || Math.Abs(now.Y - _pressPoint.Y) > 5)
        {
            _dragging = true;
            _pressed = false;
            ReleaseMouseCapture();
            try
            {
                DragMove();
                SavePosition();
            }
            catch
            {
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }
        _pressed = false;
        ReleaseMouseCapture();
        Point now = e.GetPosition(this);
        if (!_dragging && Math.Abs(now.X - _pressPoint.X) <= 5 && Math.Abs(now.Y - _pressPoint.Y) <= 5)
        {
            CycleDisplayMode();
        }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Hide();
    }
}
