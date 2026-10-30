using System;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using L_Mechrevo.WpfP0.Resources;
using System.Windows.Threading;
using L_Mechrevo.WpfP0.Services;

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
    private bool _locked;

    public OverlayWindow(Func<OverlaySnapshot> provider)
    {
        InitializeComponent();
        _provider = provider;
        _locked = ReadSavedLocked();
        try
        {
            _displayMode = Math.Clamp(MechrevoLite.AppConfig.Get("overlay_wpf_mode", 1), 0, 2);
            ApplyOverlayBackground(Math.Clamp(MechrevoLite.AppConfig.Get("overlay_opacity", 72), 30, 100));
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(MechrevoLite.AppConfig.Get("overlay_interval_ms", 1000), 500, 10000));
        }
        catch
        {
            _displayMode = 1;
        }
        ApplyDisplayMode();
        SourceInitialized += (_, _) => ApplyLockState();
        Loaded += (_, _) =>
        {
            RestorePosition();
            if (double.IsNaN(Left) || double.IsNaN(Top) || Left <= 0 || Top <= 0)
            {
                ResetPosition();
            }
            ApplyLockState();
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
                ApplyLockState();
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
            bool showGpu = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.NoDgpu;
            GpuRow.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
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
            string state = s.Charging ? WpfStrings.OverlayCharging : (s.AcOnline ? (s.BatteryPercent >= 99 ? WpfStrings.OverlayBatteryFull : WpfStrings.OverlayNotCharging) : WpfStrings.OverlayDischarging);
            BatteryText.Text = s.BatteryPercent >= 0 ? WpfStrings.OverlayBatteryLabel + s.BatteryPercent + "%" : WpfStrings.OverlayBatteryDash;
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

    private static string CloseParen() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", System.StringComparison.OrdinalIgnoreCase) ? "）" : ")";

    private static string FormatPowerSource(bool acOnline)
    {
        if (!acOnline)
        {
            return WpfStrings.OverlayBatteryPower;
        }
        try
        {
            if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true)
            {
                return WpfStrings.OverlayAcPower;
            }
            L_Mechrevo.WpfP0.Services.PowerStatusSnapshot power = L_Mechrevo.WpfP0.Services.PowerStatusService.Read();
            MechrevoLite.Hardware.PowerInputSample? sample = power.Input;
            MechrevoLite.Hardware.PowerInputKind kind = MechrevoLite.Hardware.PowerInputDecoder.Reconcile(sample, acOnline);
            // task-38: map the enum on the WPF side so the power-source headline is localized
            // (Core only returns a pre-localized string; we no longer depend on its language).
            if (sample?.AdapterWatts is int watts)
            {
                string text = kind switch
                {
                    MechrevoLite.Hardware.PowerInputKind.Barrel => string.Format(WpfStrings.OverlayBarrelWattsFormat, watts),
                    MechrevoLite.Hardware.PowerInputKind.TypeC => WpfStrings.OverlayUsbCPower,
                    MechrevoLite.Hardware.PowerInputKind.BarrelAndTypeC => WpfStrings.OverlayUsbCPower,
                    MechrevoLite.Hardware.PowerInputKind.ExternalUnknown => WpfStrings.OverlayExternalPower,
                    _ => WpfStrings.OverlayAcPower
                };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            else if (kind == MechrevoLite.Hardware.PowerInputKind.Barrel)
            {
                return WpfStrings.OverlayBarrelPower;
            }
            int raw = sample?.BiosInfo3Raw ?? -1;
            if (raw >= 0)
            {
                return WpfStrings.OverlayAcPowerEcHex + raw.ToString("X2") + CloseParen();
            }
            decimal? rate = power.BatteryRateWatts;
            if (rate.HasValue)
            {
                return WpfStrings.OverlayAcPowerEcWatt + rate.Value.ToString("0.0") + "W" + CloseParen();
            }
        }
        catch
        {
        }
        return WpfStrings.OverlayAcPower;
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
        if (_locked || LockButton.IsMouseOver || e.ButtonState != MouseButtonState.Pressed)
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
        if (_locked || !_pressed || e.LeftButton != MouseButtonState.Pressed)
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
        if (_locked || !_pressed)
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
        if (_locked || LockButton.IsMouseOver)
        {
            return;
        }
        Hide();
    }

    // ── 锁定 + 鼠标穿透 ──────────────────────────────────────────────
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    internal static bool ReadSavedLocked()
    {
        try
        {
            return MechrevoLite.AppConfig.Is("overlay_locked");
        }
        catch
        {
            return false;
        }
    }

    internal bool IsLocked => _locked;

    internal void SetLocked(bool locked)
    {
        bool wasLocked = _locked;
        _locked = locked;
        try
        {
            MechrevoLite.AppConfig.Set("overlay_locked", locked ? 1 : 0);
            MechrevoLite.AppConfig.Flush();
        }
        catch
        {
        }
        ApplyLockState();
        if (locked && !wasLocked)
        {
            try
            {
                OsdService.Show("\uE72E", "悬浮窗已锁定", "可在托盘菜单或设置中解锁");
            }
            catch
            {
            }
        }
    }

    private void ApplyLockState()
    {
        try
        {
            OverlayRoot.IsHitTestVisible = !_locked;
            LockButton.Visibility = _locked ? Visibility.Collapsed : (IsMouseOver ? Visibility.Visible : Visibility.Collapsed);
            nint handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            long current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            long target = _locked
                ? (current | WsExTransparent | WsExLayered | WsExNoActivate)
                : (current & ~(WsExTransparent | WsExNoActivate));
            if (target != current)
            {
                SetWindowLongPtr(handle, GwlExStyle, (nint)target);
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
        }
        catch
        {
        }
    }

    private void OverlayRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_locked)
        {
            LockButton.Visibility = Visibility.Visible;
        }
    }

    private void OverlayRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_locked && !LockButton.IsMouseOver)
        {
            LockButton.Visibility = Visibility.Collapsed;
        }
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        SetLocked(true);
    }
}
