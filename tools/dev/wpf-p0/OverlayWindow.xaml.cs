using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

public readonly record struct OverlaySnapshot(
    int CpuTemp,
    int CpuUsage,
    int CpuFrequency,
    int GpuTemp,
    int GpuUsage,
    int GpuCoreFreq,
    int GpuVramUsedMb,
    int GpuVramUsage,
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
    private bool _acOnline;

    public OverlayWindow(Func<OverlaySnapshot> provider)
    {
        InitializeComponent();
        _provider = provider;
        try
        {
            _displayMode = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_wpf_mode", 1), 0, 2);
            ApplyOverlayBackground(Math.Clamp(MechrevoLite.AppConfig.Get("overlay_opacity", 92), 30, 100));
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
            _acOnline = s.AcOnline;
            GpuTempText.Text = FormatTemp(s.GpuTemp);
            GpuFreqText.Text = FormatFreq(s.GpuCoreFreq);
            GpuFanText.Text = FormatFan(s.GpuFanRpm, s.GpuFanDuty);
            GpuPowerText.Text = FormatPower(s.GpuPower);
            GpuUsageText.Text = FormatPercent(s.GpuUsage);
            GpuUsageBar.Value = Math.Clamp(s.GpuUsage, 0, 100);
            GpuMemoryText.Text = s.GpuVramUsedMb > 0 ? (s.GpuVramUsedMb / 1024.0).ToString("0.0") + "GB" : "—";
            GpuMemoryBar.Value = Math.Clamp(s.GpuVramUsage, 0, 100);

            CpuTempText.Text = FormatTemp(s.CpuTemp);
            CpuFreqText.Text = FormatFreq(s.CpuFrequency);
            CpuFanText.Text = FormatFan(s.CpuFanRpm, s.CpuFanDuty);
            CpuPowerText.Text = FormatPower(s.CpuPower);
            CpuUsageText.Text = FormatPercent(s.CpuUsage);
            CpuUsageBar.Value = Math.Clamp(s.CpuUsage, 0, 100);
            CpuMemoryText.Text = s.RamUsage >= 0
                ? (s.RamUsedGb > 0 ? s.RamUsedGb.ToString("0.0") + "GB" : s.RamUsage + "%")
                : "—";
            CpuMemoryBar.Value = Math.Clamp(s.RamUsage, 0, 100);

            string source = FormatPowerSource(s.AcOnline);
            string state = s.Charging ? "充电中" : (s.AcOnline ? (s.BatteryPercent >= 99 ? "已充满" : "未充电") : "放电中");
            BatteryText.Text = s.BatteryPercent >= 0 ? "电池 " + s.BatteryPercent + "%" : "电池 —";
            BatteryDetailText.Text = s.BatteryPercent >= 0 ? source + " · " + state : "";
            UpdateBatteryVisibility();
        }
        catch
        {
        }
    }

    private static string FormatTemp(int temp) => temp > 0 ? temp + "°" : "—";

    private static string FormatFreq(int mhz) => mhz > 0 ? (mhz / 1000.0).ToString("0.0") + "G" : "—";

    private static string FormatFan(int rpm, int duty)
    {
        string speed = rpm > 0 ? rpm + " RPM" : "— RPM";
        if (duty < 0)
        {
            return speed;
        }
        return speed + " · " + duty + "%";
    }

    private static string FormatPower(float watts) => watts > 0 ? watts.ToString("0.0") + "W" : "—";

    private static string FormatPercent(int value) => value >= 0 ? value + "%" : "—";

    private void UpdateBatteryVisibility()
    {
        bool showBattery = !_acOnline;
        BatteryPanel.Visibility = showBattery ? Visibility.Visible : Visibility.Collapsed;
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
                return "外接电源";
            }
            MechrevoLite.Hardware.PowerInputSample? sample = MechrevoLite.HardwareControl.PowerInput;
            MechrevoLite.Hardware.PowerInputKind kind = MechrevoLite.Hardware.PowerInputDecoder.Reconcile(sample, acOnline);
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

    private void ApplyOverlayBackground(double opacity)
    {
        Opacity = 1.0;
        try
        {
            Color color = Color.FromRgb(0x0B, 0x0D, 0x10);
            color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 100) * 255.0 / 100.0);
            OverlayCard.Background = new SolidColorBrush(color);
        }
        catch
        {
        }
    }

    internal void ApplyAppearance(double opacity, int mode, int intervalMs, bool resetPosition)
    {
        ApplyOverlayBackground(Math.Clamp(opacity, 30, 100));
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
        Visibility standardVisibility = standard ? Visibility.Visible : Visibility.Collapsed;
        Visibility completeVisibility = complete ? Visibility.Visible : Visibility.Collapsed;

        GpuPowerText.Visibility = standardVisibility;
        GpuUsageText.Visibility = standardVisibility;
        GpuUsageBar.Visibility = standardVisibility;
        GpuMemoryText.Visibility = completeVisibility;
        GpuMemoryBar.Visibility = completeVisibility;
        CpuPowerText.Visibility = standardVisibility;
        CpuUsageText.Visibility = standardVisibility;
        CpuUsageBar.Visibility = standardVisibility;
        CpuMemoryText.Visibility = completeVisibility;
        CpuMemoryBar.Visibility = completeVisibility;
        UpdateBatteryVisibility();
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
