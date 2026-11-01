using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using L_Mechrevo.WpfP0.Services;

namespace L_Mechrevo.WpfP0;

public partial class WaterCoolerCurveWindow : Window
{
    private readonly WaterCoolerRoutingService? _routing;

    internal WaterCoolerCurveWindow(WaterCoolerRoutingService? routing)
    {
        InitializeComponent();
        _routing = routing;
        CurveCanvas.Load(WaterCoolerCurve.Load());
        PumpStartSlider.Value = WaterCoolerCurve.PumpStartTemp;
        PumpTurboSlider.Value = WaterCoolerCurve.PumpTurboTemp;
        PumpStartValue.Text = WaterCoolerCurve.PumpStartTemp + "°C";
        PumpTurboValue.Text = WaterCoolerCurve.PumpTurboTemp + "°C";
        StatusText.Text = routing == null
            ? "水冷未连接或不是 BLE 直连；曲线不可下发"
            : (routing.IsBoundDevice ? "已绑定：" + routing.BoundDeviceName + " · " : "")
                + (routing.CurveEnabled ? "曲线控制中" : "固定风扇档位");
    }

    private void SetPreset(string name)
    {
        CurveCanvas.Load(WaterCoolerCurve.Preset(name));
        StatusText.Text = "已载入模板，点“应用”后生效";
    }

    private void PresetStandard_Click(object sender, RoutedEventArgs e) => SetPreset("standard");
    private void PresetQuiet_Click(object sender, RoutedEventArgs e) => SetPreset("quiet");
    private void PresetAggressive_Click(object sender, RoutedEventArgs e) => SetPreset("aggressive");
    private void PresetLinear_Click(object sender, RoutedEventArgs e) => SetPreset("linear");

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        CurveCanvas.Load(WaterCoolerCurve.DefaultPoints);
        StatusText.Text = "已恢复默认曲线，点“应用”后生效";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<WaterCoolerCurvePoint> points = CurveCanvas.Points;
        try
        {
            if (_routing == null)
            {
                StatusText.Text = "水冷未连接或不是 BLE 直连";
                return;
            }
            bool ok = await _routing.SetCurveAsync(points,
                (int)Math.Round(PumpStartSlider.Value),
                (int)Math.Round(PumpTurboSlider.Value));
            StatusText.Text = ok ? "已应用：曲线控制中" : "应用失败：当前水冷不是 BLE 直连或未连接";
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
        }
    }

    private void PumpStart_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PumpStartValue == null || PumpTurboSlider == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        PumpStartValue.Text = value + "°C";
        if ((int)Math.Round(PumpTurboSlider.Value) < value + 5)
        {
            PumpTurboSlider.Value = value + 5;
        }
    }

    private void PumpTurbo_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PumpTurboValue == null || PumpStartSlider == null)
        {
            return;
        }
        int value = (int)Math.Round(e.NewValue);
        PumpTurboValue.Text = value + "°C";
        if ((int)Math.Round(PumpStartSlider.Value) > value - 5)
        {
            PumpStartSlider.Value = Math.Max(30, value - 5);
        }
    }

    private void Unbind_Click(object sender, RoutedEventArgs e)
    {
        _routing?.UnbindDevice();
        StatusText.Text = "已解除绑定；重新连接后才会重新选择水冷";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
