using System;
using System.Collections.Generic;
using System.Linq;
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
    private WujieFanEcController? _wujieController;

    public FanCurveWindow(string modeId, bool showCpu = true, bool showGpu = true)
    {
        InitializeComponent();
        _modeId = modeId;
        CpuSection.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
        GpuSection.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
        _hw = Program.hw;
        bool supported = _hw != null && (_hw.IsGcuConnected || _hw.UsesBaolongda || _hw.WujieSmuApu);
        if (!supported)
        {
            HintText.Text = "后端未连接，无法读取风扇曲线。";
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
        if (_hw.UsesBaolongda)
        {
            BaolongdaFanCurveSettings curve = _hw.LoadBaolongdaFanCurve(_modeId);
            CpuCurve.Load(TempsToBytes(curve.Channel1Temperatures),
                RpmsToPercent(curve.Channel1Rpms, curve.MaxRpm));
            GpuCurve.Load(TempsToBytes(curve.Channel0Temperatures),
                RpmsToPercent(curve.Channel0Rpms, curve.MaxRpm));
            return;
        }
        if (_hw.WujieSmuApu && _hw.CpuCurveDuty.Length >= 16 && _hw.GpuCurveDuty.Length >= 16
            && _hw.CpuCurveUpT.Length >= 16 && _hw.GpuCurveUpT.Length >= 16)
        {
            CpuCurve.Load(_hw.CpuCurveUpT, _hw.CpuCurveDuty);
            GpuCurve.Load(_hw.GpuCurveUpT, _hw.GpuCurveDuty);
            return;
        }
        MechrevoLite.Mode.PerfModeSettings saved = MechrevoLite.Mode.PerfModeStore.LoadSettings(_modeId);
        int[]? cpuSaved = saved.CpuFanDuty is { Length: >= 2 } cpuDuty ? cpuDuty : null;
        int[]? gpuSaved = saved.GpuFanDuty is { Length: >= 2 } gpuDuty ? gpuDuty : null;
        if (cpuSaved != null && gpuSaved != null)
        {
            CpuCurve.Load(_hw.CpuCurveUpT, cpuSaved.Select(v => (byte)Math.Clamp(v, 0, 100)).ToArray());
            GpuCurve.Load(_hw.GpuCurveUpT, gpuSaved.Select(v => (byte)Math.Clamp(v, 0, 100)).ToArray());
        }
        else if (_modeId.StartsWith("custom", StringComparison.Ordinal)
            || _modeId.StartsWith("fw", StringComparison.Ordinal))
        {
            CpuCurve.Load(FanCurveDefaults.CpuTemperatures, FanCurveDefaults.CpuDutyBytes);
            GpuCurve.Load(FanCurveDefaults.GpuTemperatures, FanCurveDefaults.GpuDutyBytes);
            PersistHardwareCurveIfMissing(cpuSaved, gpuSaved);
        }
        else if (_hw.CpuCurveDuty.Length >= 16 && _hw.GpuCurveDuty.Length >= 16
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
        else
        {
            CpuCurve.Load(FanCurveDefaults.CpuTemperatures, FanCurveDefaults.CpuDutyBytes);
            GpuCurve.Load(FanCurveDefaults.GpuTemperatures, FanCurveDefaults.GpuDutyBytes);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_hw == null || !_hw.IsGcuConnected)
        {
            StatusText.Text = "GCU 未连接，无法下发风扇曲线";
            return;
        }
        if (_hw.UsesBaolongda)
        {
            try
            {
                int[] cpu = CpuCurve.Duties;
                int[] gpu = GpuCurve.Duties;
                int maxRpm = Math.Max(1, _hw.FanRpmMaxForPercent);
                BaolongdaFanCurveSettings curve = new()
                {
                    Enabled = true,
                    FormatVersion = BaolongdaFanCurveSettings.CurrentFormatVersion,
                    SyncEnabled = true,
                    HysteresisC = FanCurveEngine.DefaultHysteresisC,
                    IntervalMs = BaolongdaFanCurveSettings.DefaultIntervalMs,
                    MinRpm = BaolongdaFanCurveSettings.DefaultMinRpm,
                    MaxRpm = maxRpm,
                    Channel1Temperatures = TempsToInts(CpuCurve.Temperatures),
                    Channel1Rpms = PercentToRpms(cpu, maxRpm),
                    Channel0Temperatures = TempsToInts(GpuCurve.Temperatures),
                    Channel0Rpms = PercentToRpms(gpu, maxRpm)
                };
                bool ok = _hw.SaveBaolongdaFanCurve(_modeId, curve);
                _dirty = false;
                StatusText.Text = ok ? "宝龙达风扇曲线已保存并应用" : "宝龙达风扇曲线保存失败";
            }
            catch (Exception ex)
            {
                StatusText.Text = "应用失败：" + ex.Message;
            }
            return;
        }
        if (_hw.WujieSmuApu)
        {
            try
            {
                int[] cpu = CpuCurve.Duties;
                int[] gpu = GpuCurve.Duties;
                _wujieController ??= new WujieFanEcController();
                byte[] cpuTemps = CpuCurve.Temperatures;
                byte[] gpuTemps = GpuCurve.Temperatures;
                bool ok = _wujieController.Start(cpuTemps, cpu, independent: false, gpuTemps, gpu, out string detail);
                PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                SaveWujieTemps(cpuTemps, gpuTemps);
                _dirty = false;
                StatusText.Text = ok ? "无界 EC 曲线已启动：" + detail : "无界 EC 曲线启动失败：" + detail;
            }
            catch (Exception ex)
            {
                StatusText.Text = "应用失败：" + ex.Message;
            }
            return;
        }
        try
        {
            int[] cpu = CpuCurve.Duties;
            int[] gpu = GpuCurve.Duties;
            await _hw.SetFanCurve(0, cpu).ConfigureAwait(true);
            await _hw.SetFanCurve(1, gpu).ConfigureAwait(true);
            await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" }).ConfigureAwait(true);
            await Task.Delay(700).ConfigureAwait(true);
            bool confirmed = CurveMatches(cpu, _hw.CpuCurveDuty, _hw.CpuCurveUpT)
                && CurveMatches(gpu, _hw.GpuCurveDuty, _hw.GpuCurveUpT);
            if (confirmed)
            {
                if (_modeId.StartsWith("fw", StringComparison.Ordinal)
                    || _modeId.StartsWith("custom", StringComparison.Ordinal))
                {
                    MechrevoLite.Mode.PerfModeSettings settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(_modeId) with
                    {
                        CpuFanDuty = cpu,
                        GpuFanDuty = gpu,
                        FanCurveOn = true
                    };
                    MechrevoLite.Mode.PerfModeStore.SaveSettings(_modeId, settings);
                }
                else
                {
                    PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                    PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, true);
                }
                _dirty = false;
                StatusText.Text = "风扇曲线已下发并回读确认，已写入当前配置档";
            }
            else
            {
                StatusText.Text = "风扇曲线回读未确认，未写入当前配置档";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
        }
    }

    private void PersistHardwareCurveIfMissing(int[]? cpuSaved, int[]? gpuSaved)
    {
        if (_hw == null || (!_modeId.StartsWith("custom", StringComparison.Ordinal)
            && !_modeId.StartsWith("fw", StringComparison.Ordinal)))
        {
            return;
        }
        if (cpuSaved is { Length: >= 2 } && gpuSaved is { Length: >= 2 })
        {
            return;
        }
        int[] cpu = CpuCurve.Duties.ToArray();
        int[] gpu = GpuCurve.Duties.ToArray();
        MechrevoLite.Mode.PerfModeSettings settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(_modeId) with
        {
            CpuFanDuty = cpu,
            GpuFanDuty = gpu
        };
        MechrevoLite.Mode.PerfModeStore.SaveSettings(_modeId, settings);
    }

    private static bool CurveMatches(int[] request, byte[] actual, byte[] temps)
    {
        int valid = Array.IndexOf(temps, (byte)255);
        valid = valid < 2 ? Math.Min(11, Math.Max(2, request.Length)) : valid;
        for (int i = 0; i < valid; i++)
        {
            if (i < actual.Length && actual[i] != 255 && Math.Clamp((int)actual[i], 0, 100) != request[i])
            {
                return false;
            }
        }
        return true;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_hw != null && _hw.UsesBaolongda)
        {
            BaolongdaFanCurveSettings curve = _hw.LoadBaolongdaFanCurve(_modeId);
            CpuCurve.Load(TempsToBytes(BaolongdaFanCurveSettings.DefaultTemperatures()),
                RpmsToPercent(BaolongdaFanCurveSettings.DefaultRpms(_hw.FanRpmMaxForPercent), _hw.FanRpmMaxForPercent));
            GpuCurve.Load(TempsToBytes(BaolongdaFanCurveSettings.DefaultTemperatures()),
                RpmsToPercent(BaolongdaFanCurveSettings.DefaultRpms(_hw.FanRpmMaxForPercent), _hw.FanRpmMaxForPercent));
        }
        else if (_hw != null && _hw.WujieSmuApu)
        {
            CpuCurve.Load(FanCurveDefaults.CpuTemperatures, FanCurveDefaults.CpuDutyBytes);
            GpuCurve.Load(FanCurveDefaults.GpuTemperatures, FanCurveDefaults.GpuDutyBytes);
        }
        else if (_modeId.StartsWith("custom", StringComparison.Ordinal)
            || _modeId.StartsWith("fw", StringComparison.Ordinal))
        {
            CpuCurve.Load(FanCurveDefaults.CpuTemperatures, FanCurveDefaults.CpuDutyBytes);
            GpuCurve.Load(FanCurveDefaults.GpuTemperatures, FanCurveDefaults.GpuDutyBytes);
        }
        else if (_hw != null && _hw.TryGetBuiltInDefaultFanCurve(out byte[] cpuTemps, out byte[] cpuDuties,
                     out byte[] gpuTemps, out byte[] gpuDuties))
        {
            CpuCurve.Load(cpuTemps, cpuDuties);
            GpuCurve.Load(gpuTemps, gpuDuties);
        }
        else
        {
            CpuCurve.Load(FanCurveDefaults.CpuTemperatures, FanCurveDefaults.CpuDutyBytes);
            GpuCurve.Load(FanCurveDefaults.GpuTemperatures, FanCurveDefaults.GpuDutyBytes);
        }
        _dirty = true;
        StatusText.Text = "已恢复默认曲线，点应用后下发";
    }

    private void SaveWujieTemps(byte[] cpuTemps, byte[] gpuTemps)
    {
        try
        {
            string key = string.IsNullOrWhiteSpace(_modeId) ? "active" : _modeId;
            MechrevoLite.AppConfig.Set("wujie_fan_cputemps_" + key, string.Join(",", cpuTemps));
            MechrevoLite.AppConfig.Set("wujie_fan_gputemps_" + key, string.Join(",", gpuTemps));
        }
        catch
        {
        }
    }

    private static byte[] TempsToBytes(int[] temps)
    {
        byte[] bytes = new byte[16];
        for (int i = 0; i < bytes.Length && i < temps.Length; i++)
        {
            bytes[i] = (byte)Math.Clamp(temps[i], 0, 255);
        }
        return bytes;
    }

    private static int[] TempsToInts(byte[] temps)
    {
        int[] ints = new int[16];
        for (int i = 0; i < ints.Length && i < temps.Length; i++)
        {
            ints[i] = temps[i];
        }
        return ints;
    }

    private static byte[] RpmsToPercent(int[] rpms, int maxRpm)
    {
        byte[] duties = new byte[16];
        int max = Math.Max(1, maxRpm);
        for (int i = 0; i < duties.Length && i < rpms.Length; i++)
        {
            duties[i] = (byte)Math.Clamp((int)Math.Round(rpms[i] * 100.0 / max), 0, 100);
        }
        return duties;
    }

    private static int[] PercentToRpms(int[] duties, int maxRpm)
    {
        int[] rpms = new int[16];
        for (int i = 0; i < rpms.Length && i < duties.Length; i++)
        {
            rpms[i] = (int)Math.Round(duties[i] * maxRpm / 100.0);
        }
        return rpms;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _wujieController?.Stop("窗口关闭");
        _wujieController?.Dispose();
        _wujieController = null;
        Close();
    }
}
