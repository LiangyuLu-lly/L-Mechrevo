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
        LoadFromHardware();
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
            StatusText.Text = "风扇曲线已下发";
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
        StatusText.Text = "已恢复默认曲线，点应用后下发";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
