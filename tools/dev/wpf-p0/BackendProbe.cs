using System;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

/// <summary>P1 试接：只读创建 MechrevoHw 并读取公开传感器字段，不下发任何硬件命令。</summary>
internal sealed class BackendProbe : IDisposable
{
    private MechrevoHw? _hw;
    private MechrevoService? _service;
    private bool _disposed;

    internal bool Connected { get; private set; }

    internal string LastError { get; private set; } = "";

    internal async Task<bool> ConnectAsync()
    {
        try
        {
            _hw = new MechrevoHw();
            bool ok = await _hw.ConnectAsync().ConfigureAwait(false);
            if (ok)
            {
                _service = new MechrevoService(_hw);
            }
            Connected = ok;
            LastError = ok ? "" : "ConnectAsync 返回 false";
            return ok;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Connected = false;
            return false;
        }
    }

    internal MechrevoHw? Hw => _hw;

    internal MechrevoDeviceCapabilities? Capabilities => _hw?.Capabilities;

    internal bool SupportsQuick(string key)
    {
        try
        {
            return _hw?.SupportsQuickSwitch(key) ?? false;
        }
        catch
        {
            return false;
        }
    }

    internal int CurrentMode => _service?.CurrentMode ?? -1;

    internal int BatteryMode => _hw?.BatteryProtection ?? -1;

    internal int GpuMode => _hw?.GpuMode ?? -1;

    internal async Task<bool> SwitchModeAsync(int mode)
    {
        try
        {
            if (_service == null)
            {
                return false;
            }
            await _service.SwitchMode(mode).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetBatteryModeAsync(int mode)
    {
        try
        {
            return _hw != null && await _hw.SetBatteryProtection(mode).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SwitchAutoRefreshAsync(bool on)
    {
        try
        {
            return _service != null && await _service.SwitchAutoRefreshRate(on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SwitchRefreshRateAsync(int hz)
    {
        try
        {
            return _service != null && await _service.SwitchRefreshRate(hz).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SwitchQuickAsync(string key, bool on)
    {
        try
        {
            return _service != null && await _service.SwitchQuick(key, on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetColorCalibrationAsync(int mode)
    {
        try
        {
            return _service != null && await _service.SetColorCalibration(mode).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal bool GpuOcSupported
    {
        get
        {
            MechrevoHw? hw = _hw;
            return hw != null && (hw.SupportsGpuOverclock || hw.Capabilities?.OverclockSettings == true || hw.GcuGpuOverclockAvailable);
        }
    }

    internal async Task<bool> ApplyGpuOverclockAsync(bool on)
    {
        try
        {
            return _service != null && await _service.ApplyGpuOverclock(on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetDisplayEffectSwitchAsync(bool on)
    {
        try
        {
            return _service != null && await _service.SetDisplayEffectSwitch(on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetKeyboardEffectAsync(string effect)
    {
        try
        {
            return _service != null && await _service.SetKeyboardEffect(effect, 4, 1, "None", null, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetLogoEffectAsync(string effect)
    {
        try
        {
            return _service != null && await _service.SetLightEffect("Logo", effect, 4, 1, "None", null, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal string Snapshot()
    {
        MechrevoHw? hw = _hw;
        if (hw == null)
        {
            return LastError.Length == 0 ? "未连接" : LastError;
        }
        try
        {
            return "CPU " + hw.CpuTemp + "℃ · GPU " + hw.GpuTemp
                + "℃ · 风扇 " + hw.CpuFanRpm + "/" + hw.GpuFanRpm
                + " rpm · 电池 " + hw.BatteryPercent + "%"
                + " · " + (hw.IsConnected ? "已连接" : "未连接")
                + (Connected ? "" : " · 连接未确认");
        }
        catch (Exception ex)
        {
            return "读取异常：" + ex.GetType().Name;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _hw?.Dispose();
        }
        catch
        {
        }
        _hw = null;
        _service = null;
        Connected = false;
    }
}
