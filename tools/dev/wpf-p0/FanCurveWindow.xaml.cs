using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

namespace L_Mechrevo.WpfP0;

public partial class FanCurveWindow : Window
{
    private readonly string _modeId;
    private readonly MechrevoHw? _hw;
    private bool _dirty;

    public FanCurveWindow(string modeId)
    {
        InitializeComponent();
        _modeId = modeId;
        _hw = Program.hw;
        bool supported = _hw != null && _hw.IsGcuConnected && !_hw.UsesBaolongda && !_hw.WujieSmuApu;
        if (!supported)
        {
            HintText.Text = "当前平台请使用原版风扇曲线窗口。";
        }
        CpuCurve.Changed += MarkDirty;
        GpuCurve.Changed += MarkDirty;
        Closing += OnClosing;
        LoadFromHardware();
    }

    private void MarkDirty()
    {
        _dirty = true;
        StatusText.Text = "有未应用的修改";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty)
        {
            return;
        }
        MessageBoxResult result = MessageBox.Show(this, "风扇曲线还有未应用的修改，确定关闭？", "L-Mechrevo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void LoadFromHardware()
    {
        if (_hw == null)
        {
            return;
        }
        if (_hw.CpuCurveDuty.Length >= 16 && _hw.GpuCurveDuty.Length >= 16
            && _hw.CpuCurveUpT.Length >= 16 && _hw.GpuCurveUpT.Length >= 16)
        {
            CpuCurve.Load(_hw.CpuCurveUpT, _hw.CpuCurveDuty);
            GpuCurve.Load(_hw.GpuCurveUpT, _hw.GpuCurveDuty);
        }
        else if (_hw.TryGetBuiltInDefaultFanCurve(out byte[] cpuTemps, out byte[] cpuDuties,
                     out byte[] gpuTemps, out byte[] gpuDuties))
        {
            CpuCurve.Load(cpuTemps, cpuDuties);
            GpuCurve.Load(gpuTemps, gpuDuties);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_hw == null || !_hw.IsGcuConnected)
        {
            StatusText.Text = "GCU 未连接，无法下发风扇曲线";
            return;
        }
        try
        {
            int[] cpu = CpuCurve.Duties;
            int[] gpu = GpuCurve.Duties;
            await _hw.SetFanCurve(0, cpu).ConfigureAwait(true);
            await _hw.SetFanCurve(1, gpu).ConfigureAwait(true);
            PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
            PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, true);
            _dirty = false;
            StatusText.Text = "风扇曲线已下发，并已记录为当前档风扇曲线";
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_hw == null || !_hw.TryGetBuiltInDefaultFanCurve(out byte[] cpuTemps, out byte[] cpuDuties,
                out byte[] gpuTemps, out byte[] gpuDuties))
        {
            StatusText.Text = "读不到内置默认曲线";
            return;
        }
        CpuCurve.Load(cpuTemps, cpuDuties);
        GpuCurve.Load(gpuTemps, gpuDuties);
        _dirty = true;
        StatusText.Text = "已恢复默认曲线，点应用后下发";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
