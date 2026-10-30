using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class FanCurveWindow : Window
{
    private string _modeId;
    private readonly MechrevoHw? _hw;
    private bool _dirty;
    private WujieFanEcController? _wujieController;
    private readonly DispatcherTimer _activeModeRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public FanCurveWindow(string modeId, bool showCpu = true, bool showGpu = true)
    {
        InitializeComponent();
        _modeId = modeId;
        try
        {
            PushFanCurveCheck.IsChecked = MechrevoLite.Mode.PerfModeStore.LoadSettings(modeId).FanCurveOn ?? true;
            bool wujie = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.WujieSmuApu == true;
            WujieIndependentCheck.Visibility = wujie ? Visibility.Visible : Visibility.Collapsed;
            WujieIndependentCheck.IsChecked = MechrevoLite.AppConfig.Is("wujie_fan_independent");
        }
        catch
        {
        }
        CpuSection.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
        GpuSection.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
        _hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        if (_hw?.UsesBaolongda == true)
        {
            BaolongdaLoopCheck.Visibility = Visibility.Visible;
            BaolongdaSyncCheck.Visibility = Visibility.Visible;
        }
        bool supported = _hw != null && (_hw.IsGcuConnected || _hw.UsesBaolongda || _hw.WujieSmuApu
            || _hw.IsIntelNucMachine || _hw.IsUmiMachine);
        if (!supported)
        {
            HintText.Text = WpfStrings.FanCurveWindow_xaml_Text;
        }
        CpuCurve.Changed += MarkDirty;
        GpuCurve.Changed += MarkDirty;
        Closing += OnClosing;
        Closed += OnClosed;
        _activeModeRefreshTimer.Tick += (_, _) =>
        {
            _activeModeRefreshTimer.Stop();
            OnActiveModeChanged(force: true);
        };
        MechrevoLite.Mode.PerfModeService.Instance?.ActiveChanged += OnActiveModeChanged;
        MechrevoLite.Mode.PerfModeService.Instance?.Applied += OnAppliedMode;
        LoadFromHardware();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        if (perf != null)
        {
            perf.ActiveChanged -= OnActiveModeChanged;
            perf.Applied -= OnAppliedMode;
        }
        _activeModeRefreshTimer.Stop();
    }

    private void OnAppliedMode(MechrevoLite.Mode.PerfApplyOutcome outcome)
    {
        OnActiveModeChanged();
    }

    private void OnActiveModeChanged()
    {
        OnActiveModeChanged(force: false);
    }

    /// <summary>切换自定义/性能档后，已打开的风扇曲线窗口立即切到新档并重载曲线。</summary>
    private void OnActiveModeChanged(bool force)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnActiveModeChanged(force)));
            return;
        }
        string? id = MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        SetModeId(id, force);
        // 档位切换的硬件回读/保存可能晚于 ActiveChanged 一瞬；再补一次强制刷新。
        _activeModeRefreshTimer.Stop();
        _activeModeRefreshTimer.Start();
    }

    internal void SetModeId(string modeId, bool force = false)
    {
        if (!force && string.Equals(_modeId, modeId, StringComparison.Ordinal))
        {
            return;
        }
        _modeId = modeId;
        LoadFromHardware();
    }

    internal void PrepareInline()
    {
        TitleText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        if (CpuSection.Children.Count > 0 && CpuSection.Children[0] is TextBlock cpuTitle)
        {
            cpuTitle.Visibility = Visibility.Collapsed;
        }
        if (GpuSection.Children.Count > 0 && GpuSection.Children[0] is TextBlock gpuTitle)
        {
            gpuTitle.Visibility = Visibility.Collapsed;
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_2;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty)
        {
            return;
        }
        MessageBoxResult result = MessageBox.Show(this, WpfStrings.FanCurveWindow_xaml_MessageBoxShowthis, "L-Mechrevo",
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
        // 宽屏内联时模式事件可能在拖动过程中到达；拖动期间不重载，避免点位回弹。
        if (CpuCurve.IsDragging || GpuCurve.IsDragging)
        {
            return;
        }
        if (_hw.UsesBaolongda)
        {
            BaolongdaFanCurveSettings curve = _hw.LoadBaolongdaFanCurve(_modeId);
            BaolongdaLoopCheck.IsChecked = curve.Enabled;
            BaolongdaSyncCheck.IsChecked = curve.SyncEnabled;
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
            // LegacyMifs 等无 GCU 机型不把默认曲线落盘，避免污染存档。
            if (_hw.IsGcuConnected)
            {
                PersistHardwareCurveIfMissing(cpuSaved, gpuSaved);
            }
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
        if (_hw == null)
        {
            StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_3;
            return;
        }
        if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
        {
            try
            {
                int[] cpu = CpuCurve.Duties;
                int[] gpu = GpuCurve.Duties;
                if (PushFanCurveCheck?.IsChecked == false)
                {
                    PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                    PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, false);
                    _dirty = false;
                    StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_4;
                    return;
                }
                bool ok = MechrevoLite.Hardware.NucFanTable.TryApply(
                    CpuCurve.Temperatures, cpu, GpuCurve.Temperatures, gpu, out string detail);
                bool rampOk = MechrevoLite.Hardware.NucFanTable.TrySetFanRamp(true, 300, out string rampDetail);
                bool isolatedOk = MechrevoLite.Hardware.NucFanTable.TrySetFanIsolated(true, out string isolatedDetail);
                ok = ok && rampOk && isolatedOk;
                detail += " " + rampDetail + " " + isolatedDetail;
                Logger.WriteLine("[nuc-fan] 自定义风扇应用 ok=" + ok + " detail=" + detail);
                PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, true);
                _dirty = false;
                StatusText.Text = ok ? WpfStrings.L("NUC/UMI 风扇曲线已写入：", "NUC/UMI fan curve written: ") + detail : WpfStrings.L("NUC/UMI 风扇曲线写入失败：", "Failed to write NUC/UMI fan curve: ") + detail;
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
            }
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
                    Enabled = BaolongdaLoopCheck.IsChecked == true,
                    FormatVersion = BaolongdaFanCurveSettings.CurrentFormatVersion,
                    SyncEnabled = BaolongdaSyncCheck.IsChecked == true,
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
                StatusText.Text = ok ? WpfStrings.L("宝龙达风扇曲线已保存并应用", "Baolongda fan curve saved and applied") : WpfStrings.L("宝龙达风扇曲线保存失败", "Failed to save Baolongda fan curve");
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
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
                bool independent = WujieIndependentCheck?.IsChecked == true;
                try
                {
                    MechrevoLite.AppConfig.Set("wujie_fan_independent", independent ? 1 : 0);
                }
                catch
                {
                }
                bool ok = _wujieController.Start(cpuTemps, cpu, independent, gpuTemps, gpu, out string detail);
                PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                SaveWujieTemps(cpuTemps, gpuTemps);
                _dirty = false;
                StatusText.Text = ok ? WpfStrings.L("无界 EC 曲线已启动：", "Wujie EC curve started: ") + detail : WpfStrings.L("无界 EC 曲线启动失败：", "Failed to start Wujie EC curve: ") + detail;
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
            }
            return;
        }
        if (!_hw.IsGcuConnected)
        {
            StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_3;
            return;
        }
        try
        {
            int[] cpu = CpuCurve.Duties;
            int[] gpu = GpuCurve.Duties;
            bool push = PushFanCurveCheck?.IsChecked != false;
            if (!push)
            {
                PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, false);
                _dirty = false;
                StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_4;
                return;
            }
            PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, true);
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
                        FanCurveOn = PushFanCurveCheck?.IsChecked != false
                    };
                    MechrevoLite.Mode.PerfModeStore.SaveSettings(_modeId, settings);
                }
                else
                {
                    PerfModeService.Instance?.RecordFanCurves(_modeId, cpu, gpu);
                    PerfModeService.Instance?.RecordFanCurveEnabled(_modeId, true);
                }
                _dirty = false;
                StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_5;
            }
            else
            {
                StatusText.Text = WpfStrings.FanCurveWindow_xaml_Text_6;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
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

    private async void RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_hw == null) return;
        MessageBoxResult confirm = MessageBox.Show(this,
            WpfStrings.L("将停止自定义风扇曲线、交还固件/EC 自动控制，并清除该档已保存的曲线点位。继续？", "This stops the custom fan curve, returns control to firmware/EC, and clears saved curve points for this mode. Continue?"),
            WpfStrings.L("恢复默认风扇曲线", "Restore default fan curve"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            if (_hw.UsesBaolongda)
            {
                PerfModeService.Instance?.ResetBaolongdaFanToFirmwareDefault();
                ClearSavedFanCurve();
                LoadFromHardware();
                _dirty = false;
                StatusText.Text = WpfStrings.L("已恢复默认：宝龙达软件闭环已关闭，曲线回到固件默认", "Restored: Baolongda software loop disabled; curve returned to firmware default");
            }
            else if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
            {
                bool restored = MechrevoLite.Hardware.NucFanTable.TryRestoreDefault(out string nucDetail);
                bool isolatedRestore = MechrevoLite.Hardware.NucFanTable.TrySetFanIsolated(false, out string isolatedDetail);
                restored &= isolatedRestore;
                nucDetail += " " + isolatedDetail;
                LoadFromHardware();
                _dirty = false;
                StatusText.Text = restored
                    ? WpfStrings.L("已恢复默认：NUC/UMI 风扇表已还原", "Restored: NUC/UMI fan table restored")
                    : WpfStrings.L("恢复默认失败：", "Restore default failed: ") + nucDetail;
            }
            else if (_hw.IsGcuConnected)
            {
                if (!_hw.TryGetBuiltInDefaultFanCurve(out byte[] cpuTemps, out byte[] cpuDuties,
                        out byte[] gpuTemps, out byte[] gpuDuties))
                {
                    throw new InvalidOperationException(WpfStrings.L("找不到内置默认曲线资源", "Built-in default curve resource not found"));
                }
                await _hw.SetFanCurve(0, cpuDuties.Select(v => (int)v).ToArray());
                await _hw.SetFanCurve(1, gpuDuties.Select(v => (int)v).ToArray());
                ClearSavedFanCurve();
                CpuCurve.Load(cpuTemps, cpuDuties);
                GpuCurve.Load(gpuTemps, gpuDuties);
                _dirty = false;
                StatusText.Text = WpfStrings.L("已恢复默认：已写回内置默认风扇曲线", "Restored: built-in default fan curve written");
            }
            else
            {
                StatusText.Text = WpfStrings.L("本机不支持恢复默认曲线", "Restoring the default curve is not supported on this machine");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.L("恢复默认失败：", "Restore default failed: ") + ex.Message;
            Logger.WriteLine("[fan] 恢复默认曲线失败：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private void ClearSavedFanCurve()
    {
        try
        {
            PerfModeSettings settings = PerfModeStore.LoadSettings(_modeId);
            PerfModeStore.SaveSettings(_modeId, settings with { CpuFanDuty = null, GpuFanDuty = null });
        }
        catch
        {
        }
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
