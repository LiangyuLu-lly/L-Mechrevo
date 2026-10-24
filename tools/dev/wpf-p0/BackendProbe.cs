using System;
using System.Reflection;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

/// <summary>P1 试接：只读创建 MechrevoHw 并读取公开传感器字段，不下发任何硬件命令。</summary>
internal sealed class BackendProbe : IDisposable
{
    private MechrevoHw? _hw;
    private MechrevoService? _service;
    private static readonly Type? ShellType = typeof(MechrevoHw).Assembly.GetType("MechrevoLite.Helpers.ShellPersonalization");
    private static readonly Type? SleepType = typeof(MechrevoHw).Assembly.GetType("MechrevoLite.Helpers.PowerSleepGuard");
    private bool _disposed;

    internal bool Connected { get; private set; }

    internal string LastError { get; private set; } = "";

    internal async Task<bool> ConnectAsync()
    {
        try
        {
            MechrevoLite.Hardware.GcuIdentity.Prewarm();
            _hw = new MechrevoHw();
            bool ok = await _hw.ConnectAsync().ConfigureAwait(false);
            if (ok)
            {
                Program.acpi ??= new AsusACPI();
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

    internal MechrevoService? Service => _service;

    internal MechrevoDeviceCapabilities? Capabilities => _hw?.Capabilities;

    internal bool SupportsQuick(string key)
    {
        try
        {
            if (_hw == null)
            {
                return false;
            }
            if (_hw.SupportsQuickSwitch(key))
            {
                return true;
            }
            // 与 LM StaticQuick 对齐：这些项要么是纯 Windows 策略，要么有本机回退通道。
            switch (key)
            {
                case "apuboost":
                case "capslock":
                case "highperf":
                case "monitoroff":
                case "taskbarautohide":
                case "transparency":
                case "darktheme":
                case "sleepnetoff":
                case "sleepaway":
                case "sleepsysreq":
                case "sleeprtcwake":
                case "sleepdeep":
                case "sleephibernate":
                case "winupdate":
                    return true;
                case "numpad":
                    return _hw.Capabilities.Numpad || _hw.Capabilities.Keyboard;
                case "acrecovery":
                    return _hw.Capabilities.AcRecovery;
                case "fanboost":
                    return _hw.Capabilities.FanBoost;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    internal int CurrentMode => _service?.CurrentMode ?? -1;

    internal int BatteryMode => _hw?.BatteryProtection ?? -1;

    internal int GpuMode => _hw?.GpuMode ?? -1;

    internal bool SupportsLcdOverdrive
    {
        get
        {
            try
            {
                return _hw?.SupportsLcdOverdrive ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool LcdOverdrive
    {
        get
        {
            try
            {
                return _hw?.LcdOverdrive ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal async Task<bool> SetLcdOverdriveAsync(bool on)
    {
        try
        {
            return _service != null && await _service.SwitchLcdOverdrive(on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal bool SupportsColorCalibration
    {
        get
        {
            try
            {
                return _hw?.SupportsColorCalibration ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal int ColorCalibrationLevel
    {
        get
        {
            try
            {
                int level = _hw?.MqttColorCalibrationLevel ?? _hw?.Capabilities?.ColorCalibrationLevel ?? 1;
                return Math.Clamp(level, 1, 4);
            }
            catch
            {
                return 1;
            }
        }
    }

    internal bool HasDisplayEffectProfile(string mode)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(mode) && _hw != null && _hw.DisplayEffectProfiles.ContainsKey(mode);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> ApplyDisplayEffectProfileAsync(string mode)
    {
        try
        {
            if (_service == null || _hw == null || !_hw.DisplayEffectProfiles.TryGetValue(mode, out DisplayEffectProfile? profile))
            {
                return false;
            }
            return await _service.ApplyDisplayEffectProfile(profile).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task RequestDisplayEffectStatusAsync()
    {
        try
        {
            if (_service != null)
            {
                await _service.RequestDisplayEffectStatus().ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    internal async Task<MechrevoService.ModeSwitchResult> SwitchModeResultAsync(int mode)
    {
        try
        {
            if (_service == null)
            {
                return MechrevoService.ModeSwitchResult.Failed;
            }
            return await _service.SwitchMode(mode).ConfigureAwait(false);
        }
        catch
        {
            return MechrevoService.ModeSwitchResult.Failed;
        }
    }

    internal async Task<bool> SwitchModeAsync(int mode)
    {
        return await SwitchModeResultAsync(mode).ConfigureAwait(false) == MechrevoService.ModeSwitchResult.Ok;
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
            if (_service == null)
            {
                return false;
            }
            bool? systemResult = SetSystemQuick(key, on);
            if (systemResult.HasValue)
            {
                return systemResult.Value;
            }
            if (string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase))
            {
                return await _service.SwitchFanBoost(on).ConfigureAwait(false);
            }
            return await _service.SwitchQuick(key, on).ConfigureAwait(false);
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

    internal async Task<bool> SetKeyboardPowerAsync(bool on)
    {
        try
        {
            return _service != null && await _service.SetKeyboardPower(on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetLightPowerAsync(string topic, bool on)
    {
        try
        {
            return _service != null && await _service.SetLightPower(topic, on).ConfigureAwait(false);
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

    internal bool SupportsLightbar
    {
        get
        {
            try
            {
                return _hw?.SupportsLightbar ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool SupportsHingeLight => _hw?.SupportsHingeLight == true;

    internal bool SupportsSyncLight => _hw?.SupportsSyncLight == true;

    internal async Task<bool> SetHingeEffectAsync(string effect)
    {
        try
        {
            return _service != null && await _service.SetLightEffect("HidLightbar_Hinge/Ctrl", effect, 4, 1, "None", null, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetSyncEffectAsync(string effect)
    {
        try
        {
            return _service != null && await _service.SetLightEffect("HidLightbar_Sync/Ctrl", effect, 4, 1, "None", null, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SetLightbarEffectAsync(string effect)
    {
        try
        {
            return _service != null && await _service.SetLightEffect("HidLightbar/Ctrl", effect, 4, 1, "None", null, true).ConfigureAwait(false);
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
            return _service != null && await _service.SetLightEffect("HidLightbar_Logo/Ctrl", effect, 4, 1, "None", null, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal bool HasPendingGpuRestart => AppConfig.Is("gpu_restart_pending");

    internal int PendingGpuTarget
    {
        get
        {
            string? text = AppConfig.GetString("gpu_restart_target");
            return int.TryParse(text, out int value) && value >= 0 && value <= 2 ? value : -1;
        }
    }

    internal void MarkGpuRestartPending(int targetMode)
    {
        AppConfig.Set("gpu_restart_pending", 1);
        AppConfig.Set("gpu_restart_tick", Environment.TickCount64.ToString());
        AppConfig.Set("gpu_restart_target", targetMode);
    }

    internal void ClearGpuRestartPending()
    {
        AppConfig.Set("gpu_restart_pending", 0);
        AppConfig.Remove("gpu_restart_tick");
    }

    internal int CurrentGpuMode => _service?.CurrentGpuMode ?? -1;

    internal bool CanSwitchGpuMode(int mode)
    {
        try
        {
            return _hw?.CanSwitchGpuMode(mode) ?? false;
        }
        catch
        {
            return false;
        }
    }

    internal MechrevoBackendKind ActiveBackend
    {
        get
        {
            try
            {
                return _hw?.ActiveBackend ?? MechrevoBackendKind.None;
            }
            catch
            {
                return MechrevoBackendKind.None;
            }
        }
    }

    internal bool IsNonGcuBackend
    {
        get
        {
            MechrevoBackendKind backend = ActiveBackend;
            return backend != MechrevoBackendKind.None && backend != MechrevoBackendKind.Gcu;
        }
    }

    internal bool IsGcuConnected
    {
        get
        {
            try
            {
                return _hw?.IsGcuConnected ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool IsReconnecting
    {
        get
        {
            try
            {
                return _hw?.IsReconnecting ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool IsSessionAwaitingSubscribe
    {
        get
        {
            try
            {
                return _hw?.IsSessionAwaitingSubscribe ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool EverConnected
    {
        get
        {
            try
            {
                return (_hw?.ConnectionGeneration ?? 0) > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool IsBaolongda
    {
        get
        {
            try
            {
                return _hw?.UsesBaolongda ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool SwitchGpuModeBaolongda(int mode, out bool confirmed)
    {
        confirmed = false;
        try
        {
            if (_hw == null)
            {
                return false;
            }
            int backendMode = mode == 2 ? BaolongdaWmi.GpuModeDiscrete : BaolongdaWmi.GpuModeHybrid;
            return _hw.TrySetGpuModeViaBackend(backendMode, out confirmed);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> SwitchGpuModeAsync(int mode)
    {
        try
        {
            return _service != null && await _service.SwitchGpuMode(mode).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<bool> RequestGpuModeRestartAsync(int mode)
    {
        try
        {
            return _service != null && await _service.RequestGpuModeRestartAsync(mode).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal async Task RefreshGpuModeAsync()
    {
        try
        {
            if (_service != null)
            {
                await _service.RefreshGpuModeStatus().ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    internal bool QuickState(string key)
    {
        if (_hw == null)
        {
            return false;
        }
        bool? system = ReadSystemQuick(key);
        if (system.HasValue)
        {
            return system.Value;
        }
        if (string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase))
        {
            return _hw.FanBoost;
        }
        return _hw.QuickSwitches.TryGetValue(key, out bool value) && value;
    }

    private static bool? ReadSystemQuick(string key)
    {
        try
        {
            switch (key)
            {
                case "taskbarautohide":
                    return InvokeBool(ShellType, "IsTaskbarAutoHide");
                case "transparency":
                    return InvokeBool(ShellType, "IsTransparencyEnabled");
                case "darktheme":
                    return InvokeBool(ShellType, "IsDarkTheme");
            }
            if (key.StartsWith("sleep", StringComparison.OrdinalIgnoreCase))
            {
                return InvokeSleepRead(key, true);
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool? SetSystemQuick(string key, bool on)
    {
        try
        {
            switch (key)
            {
                case "taskbarautohide":
                    return InvokeBool(ShellType, "SetTaskbarAutoHide", on);
                case "transparency":
                    return InvokeBool(ShellType, "SetTransparencyEnabled", on);
                case "darktheme":
                    return InvokeBool(ShellType, "SetDarkTheme", on);
            }
            if (key.StartsWith("sleep", StringComparison.OrdinalIgnoreCase))
            {
                MethodInfo? method = SleepType?.GetMethod("TrySetEnabled", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    return null;
                }
                object?[] args = { key, on, true, null };
                object? result = method.Invoke(null, args);
                return result is bool ok && ok;
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool? InvokeBool(Type? type, string name, params object?[] args)
    {
        MethodInfo? method = type?.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        object? result = method?.Invoke(null, args);
        return result is bool value ? value : null;
    }

    private static bool? InvokeSleepRead(string key, bool ac)
    {
        MethodInfo? method = SleepType?.GetMethod("ReadEnabled", BindingFlags.Public | BindingFlags.Static);
        object? result = method?.Invoke(null, new object?[] { key, ac });
        return result is bool value ? value : null;
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
