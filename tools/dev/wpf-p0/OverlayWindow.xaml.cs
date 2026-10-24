using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

public readonly record struct OverlaySnapshot(
    int CpuTemp,
    int GpuTemp,
    int FanRpm,
    double? PowerW,
    int BatteryPercent);

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
            OverlaySnapshot snapshot = _provider();
            CpuText.Text = snapshot.CpuTemp >= 0 ? snapshot.CpuTemp + " ℃" : "—";
            GpuText.Text = snapshot.GpuTemp >= 0 ? snapshot.GpuTemp + " ℃" : "—";
            FanText.Text = snapshot.FanRpm > 0 ? "风扇 " + snapshot.FanRpm + " rpm" : "风扇 —";
            PowerText.Text = snapshot.PowerW.HasValue ? "功耗 " + snapshot.PowerW.Value.ToString("0.0") + " W" : "功耗 —";
            BatteryText.Text = snapshot.BatteryPercent >= 0 ? "电池 " + snapshot.BatteryPercent + "%" : "电池 —";
        }
        catch
        {
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
