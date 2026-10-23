using System;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

/// <summary>P1 试接：只读创建 MechrevoHw 并读取公开传感器字段，不下发任何硬件命令。</summary>
internal sealed class BackendProbe : IDisposable
{
    private MechrevoHw? _hw;
    private bool _disposed;

    internal bool Connected { get; private set; }

    internal string LastError { get; private set; } = "";

    internal async Task<bool> ConnectAsync()
    {
        try
        {
            _hw = new MechrevoHw();
            bool ok = await _hw.ConnectAsync().ConfigureAwait(false);
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
        Connected = false;
    }
}
