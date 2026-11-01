using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

/// <summary>P1 试接：只读创建 MechrevoHw 并读取公开传感器字段，不下发任何硬件命令。</summary>
internal sealed class BackendProbe : IDisposable
{
    private MechrevoHw? _hw;
    private MechrevoService? _service;
    private static readonly Type? ShellType = typeof(MechrevoHw).Assembly.GetType("MechrevoLite.Helpers.ShellPersonalization");
    private static readonly Type? SleepType = typeof(MechrevoHw).Assembly.GetType("MechrevoLite.Helpers.PowerSleepGuard");
    private bool _disposed;

    internal BackendProbe()
    {
    }

    /// <summary>UI 探针预览：直接挂合成硬件，不连接真实后端。</summary>
    internal BackendProbe(MechrevoHw hw, MechrevoService? service = null)
    {
        _hw = hw;
        _service = service;
        Connected = true;
    }

    internal bool Connected { get; private set; }

    internal string LastError { get; private set; } = "";

    internal async Task<bool> ConnectAsync()
    {
        try
        {
            MechrevoLite.Hardware.GcuIdentity.Prewarm();
            _hw = new MechrevoHw();
            bool ok = await _hw.ConnectAsync().ConfigureAwait(false);
            if (!ok && (_hw.IsIntelNucMachine || _hw.IsUmiMachine))
            {
                // NUC/UMI 的性能档、快捷键和大部分灯光/传感器走本地 ACPI/EC；
                // GCU 服务没起来时仍让窗口进入“已就绪”，否则性能键监听不会启动。
                ok = true;
                LastError = "";
                Logger.WriteLine("[backend] NUC/UMI GCU 未连接，使用本地 ACPI/EC 模式");
            }
            if (!ok && MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
            {
                // 星耀不再安装 GCU：性能档/风扇/快捷开关走 WMAA；窗口仍要进入已就绪。
                _hw.MarkXingyaoLocalReady();
                _service ??= new MechrevoService(_hw);
                ok = true;
                LastError = "";
                Logger.WriteLine("[backend] 星耀 P916F GCU 未连接，使用 WMAA 本地模式");
            }
            if (ok)
            {
                L_Mechrevo.WpfP0.Services.WpfBackend.acpi ??= new AsusACPI();
                _service = new MechrevoService(_hw);
            }
            Connected = ok;
            if (!ok)
            {
                LastError = WpfStrings.L("ConnectAsync 返回 false", "ConnectAsync returned false");
            }
            return ok;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Connected = false;
            return false;
        }
    }

    /// <summary>NUC/UMI 不依赖 GCU 时把探针标成本地就绪（仅当 Hw 已识别出机型）。</summary>
    internal bool TryEnableNucUmiLocalReady()
    {
        try
        {
            if (_hw == null || !(_hw.IsIntelNucMachine || _hw.IsUmiMachine))
            {
                return false;
            }
            _service ??= new MechrevoService(_hw);
            _hw.MarkNucLocalReady();
            Connected = true;
            LastError = "";
            Logger.WriteLine("[backend] NUC/UMI 本地 ACPI/EC 就绪（GCU 未连接）");
            return true;
        }
        catch
        {
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
                    return _hw.Capabilities.FanBoost || _hw.UsesBaolongda
                        || _hw.IsIntelNucMachine || _hw.IsUmiMachine;
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

    private int _nucBatteryMode = -1;

    internal int BatteryMode
    {
        get
        {
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                if (NucBatteryMode.TryReadMode(out int mode))
                {
                    _nucBatteryMode = mode;
                    return mode;
                }
                return _nucBatteryMode;
            }
            return _hw?.BatteryProtection ?? -1;
        }
    }

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

    /// <summary>当前后端是否提供了 GCU 色彩档位表；没有时改用本地软件色彩增强。</summary>
    private bool HasGcuDisplayProfiles
    {
        get
        {
            try
            {
                return _hw?.DisplayEffectProfiles.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    internal async Task<bool> ApplyDisplayEffectProfileAsync(string mode)
    {
        try
        {
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                return SoftwareColorEnhancement.Apply(mode);
            }
            if (_service == null || _hw == null || !_hw.DisplayEffectProfiles.TryGetValue(mode, out DisplayEffectProfile? profile))
            {
                // 40 系等部分 GCU 不上报 ProfileNameList；此时用本地 gamma/矩阵色彩增强，
                // 保证档位按钮实际有可见效果，而不是返回失败。
                Logger.WriteLine("[display-effect] GCU 无档位表，改用本地软件色彩增强 mode=" + mode);
                return SoftwareColorEnhancement.Apply(mode);
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
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true) return;
            if (_service != null)
            {
                await _service.RequestDisplayEffectStatus().ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    internal void SyncNucQuickSwitches()
    {
        if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
        {
            NucQuickSwitches.Sync(_hw);
        }
    }

    internal void MarkUserModeSwitch()
    {
        try
        {
            _service?.MarkUserModeSwitch();
        }
        catch
        {
        }
    }

    internal async Task<int> RefreshCurrentModeAsync()
    {
        try
        {
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                return MechrevoLite.Hardware.NucPerformanceMode.TryReadVisualMode(out int nucMode) ? nucMode : -1;
            }
            if (MechrevoLite.Hardware.XingyaoPerformanceMode.IsApplicable(_hw)
                && MechrevoLite.Hardware.XingyaoPerformanceMode.TryReadVisualMode(out int xyMode, out _))
            {
                return xyMode;
            }
            return _service == null ? -1 : await _service.RefreshCurrentModeAsync(_service.CurrentMode).ConfigureAwait(false);
        }
        catch
        {
            return -1;
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
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                bool ok = NucBatteryMode.TrySet(mode, out string detail);
                Logger.WriteLine("[nuc-battery] " + detail);
                if (ok) _nucBatteryMode = mode;
                return ok;
            }
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

    internal async Task<bool> SwitchLocalDimmingAsync(bool on)
    {
        try
        {
            return _service != null && await _service.SwitchLocalDimming(on).ConfigureAwait(false);
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
            // ⭐ task-24：息屏（monitoroff）由 Core VirtualDisplayBlank 接管（等价旧 SettingsForm.ApplyQuickMonitorOffAsync）。
            //   放在 _service 判空之前：息屏是纯显示层动作，不需要 GCU/MQTT 服务连接；
            //   原 taskbarautohide/transparency/darktheme/sleep* 分支保持不动。
            //   一次性语义：on=true 启动息屏（失败返回 false 供 UI 回弹并已记日志），on=false 无动作返回 false。
            if (L_Mechrevo.WpfP0.Services.VirtualDisplayBlankService.TryHandleQuickSwitch(key, on, out bool blankConfirmed))
            {
                return blankConfirmed;
            }
            if (string.Equals(key, "winupdate", StringComparison.OrdinalIgnoreCase))
            {
                WindowsUpdateGuard.Result result = WindowsUpdateGuard.SetDisabled(on);
                Logger.WriteLine("[winupdate] " + (on ? "禁用" : "恢复") + " · " + result.UserDetail());
                return result.Ok;
            }
            bool? systemResult = SetSystemQuick(key, on);
            if (systemResult.HasValue)
            {
                return systemResult.Value;
            }
            if (string.Equals(key, "gcuosd", StringComparison.OrdinalIgnoreCase))
            {
                // GCU 自己的 OSD：走 MechrevoService.SwitchQuick("osd")，
                // 不改 lm_osd_enabled（那是 LM 自身 OSD）。
                bool gcuOsdOk = _service != null && await _service.SwitchQuick("osd", on).ConfigureAwait(false);
                Logger.WriteError("[gcuosd] " + (on ? "显示" : "隐藏") + " ⇒ " + gcuOsdOk);
                return gcuOsdOk;
            }
            if (string.Equals(key, "osd", StringComparison.OrdinalIgnoreCase))
            {
                MechrevoLite.AppConfig.Set("lm_osd_enabled", on ? 1 : 0);
                MechrevoLite.AppConfig.Flush();
                if (on) OsdService.Show("", WpfStrings.QuickOsd, WpfStrings.StateOn);
                Logger.WriteError("[osd] 本地 OSD 开关=" + on);
                return true;
            }
            // 星耀 WMAA 快捷开关不依赖 GCU service。
            if (MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(_hw)
                && (key is "winkey" or "fnkey" or "copilot" or "touchpad")
                && MechrevoLite.Hardware.XingyaoQuickSwitch.TrySet(key, on, out string xingyaoDetail))
            {
                Logger.WriteLine("[xingyao-quick] " + xingyaoDetail);
                return true;
            }
            if (_service == null)
            {
                return false;
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
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                // NUC/UMI 没有 GCU 校色通道：原生/关闭只清本地 ICC，不做任何 MQTT。
                if (mode <= 1)
                {
                    await Task.Run(MechrevoLite.Display.IccHelper.ClearAllIcc).ConfigureAwait(false);
                    return true;
                }
                return false;
            }
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
            // 0.293.5.1 基线：只要 GCU 能力位承认超频就显示，不按载荷变体号硬屏蔽。
            return hw != null && hw.IsGcuConnected
                && (hw.SupportsGpuOverclock || hw.Capabilities?.OverclockSettings == true || hw.GcuGpuOverclockAvailable);
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

    /// <summary>色彩增强开关 + 档位下发：GCU 侧切换是异步的，先等一拍再下发档位，最后再补一次，避免首次开启被状态包盖掉。</summary>
    /// <summary>色彩增强开关 + 档位下发：GCU 档位表可能在启动后异步到达，失败时请求状态并重试。</summary>
    internal async Task<bool> ApplyDisplayEffectAsync(bool on, string? mode)
    {
        // NUC/UMI 没有 GCU Display/Control；色彩增强是本地软件 gamma/matrix，不能走 GCU 状态请求。
        if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
        {
            string target = string.IsNullOrWhiteSpace(mode)
                ? (MechrevoLite.AppConfig.GetString("display_effect_mode") ?? "")
                : mode;
            if (on)
            {
                if (string.IsNullOrWhiteSpace(target)) target = "VibrantMode";
                bool applied = MechrevoLite.Hardware.SoftwareColorEnhancement.Apply(target);
                Logger.WriteLine("[nuc-color] software color effect mode=" + target + " ok=" + applied);
                return applied;
            }
            bool reset = MechrevoLite.Hardware.SoftwareColorEnhancement.Reset();
            Logger.WriteLine("[nuc-color] software color reset ok=" + reset);
            return reset;
        }
        bool ok = await SetDisplayEffectSwitchAsync(on).ConfigureAwait(false);
        if (!ok)
        {
            return false;
        }
        if (!on)
        {
            await RequestDisplayEffectStatusAsync().ConfigureAwait(false);
            return true;
        }
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = _hw?.DisplayEffectMode;
        }
        if (string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(160).ConfigureAwait(false);
            bool applied = await ApplyDisplayEffectProfileAsync(mode).ConfigureAwait(false);
            await RequestDisplayEffectStatusAsync().ConfigureAwait(false);
            if (applied)
            {
                // 状态包可能稍后覆盖，最后一次档位在状态请求之后补发。
                await Task.Delay(120).ConfigureAwait(false);
                await ApplyDisplayEffectProfileAsync(mode).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    internal async Task<bool> SetDisplayEffectSwitchAsync(bool on)
    {
        try
        {
            if (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true)
            {
                return on
                    ? SoftwareColorEnhancement.Apply(MechrevoLite.AppConfig.GetString("display_effect_mode"))
                    : SoftwareColorEnhancement.Reset();
            }
            if (!HasGcuDisplayProfiles)
            {
                return on
                    ? SoftwareColorEnhancement.Apply(MechrevoLite.AppConfig.GetString("display_effect_mode"))
                    : SoftwareColorEnhancement.Reset();
            }
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
            if (_hw?.IsIntelNucMachine == true && string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal))
            {
                bool local = UmiLightbar.SetPower(on);
                bool? state = UmiLightbar.TryReadStatus(out bool readOn, out _, out _, out _) ? readOn : (bool?)null;
                if (state == on)
                {
                    return true;
                }
                // EC 写返回成功但回读没变：再走一次 GCU/服务通道，避免「点了关不掉」。
                bool remote = _service != null && await _service.SetLightPower(topic, on).ConfigureAwait(false);
                Logger.WriteError("[umi-lightbar] 电源回读不匹配 requested=" + on + " read=" + state?.ToString() + " local=" + local + " remote=" + remote);
                return local || remote;
            }
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
            (int light, int speed, Color? color) = ReadLightArgs("Keyboard/Ctrl", effect, keyboard: true);
            return _service != null && await _service.SetKeyboardEffect(effect, light, speed, "None", color, true).ConfigureAwait(false);
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

    private static (int Light, int Speed, Color? Color) ReadLightArgs(string topic, string effect, bool keyboard)
    {
        try
        {
            MechrevoLite.Hardware.LightChannelSettings settings = MechrevoLite.Hardware.LightingSettingsStore.Load(topic, effect);
            // 键盘：宝龙达用实际 ARGB；非宝龙达只有 Single 带颜色（原版 SettingsForm/RgbForm 逐字如此）。
            // 灯带/Logo/铰链/同步：原版 LightForm 对所有单色系效果带颜色。
            bool baolongda = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true;
            Color? color;
            if (keyboard)
            {
                color = baolongda
                    ? Color.FromArgb(settings.EffectiveColorArgb())
                    : (string.Equals(effect, "Single", StringComparison.Ordinal)
                        ? Color.FromArgb(settings.ColorArgb)
                        : null);
            }
            else
            {
                color = MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleColorEffect(effect)
                    ? Color.FromArgb(settings.ColorArgb)
                    : null;
            }
            return (settings.Light, settings.Speed, color);
        }
        catch
        {
            return (4, 1, null);
        }
    }

    internal async Task<bool> SetHingeEffectAsync(string effect)
    {
        try
        {
            (int light, int speed, Color? color) = ReadLightArgs("HidLightbar_Hinge/Ctrl", effect, keyboard: false);
            return _service != null && await _service.SetLightEffect("HidLightbar_Hinge/Ctrl", effect, light, speed, "None", color, true).ConfigureAwait(false);
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
            (int light, int speed, Color? color) = ReadLightArgs("HidLightbar_Sync/Ctrl", effect, keyboard: false);
            return _service != null && await _service.SetLightEffect("HidLightbar_Sync/Ctrl", effect, light, speed, "None", color, true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    internal bool ApplyIntelNucLightbar(string effect, int light, System.Drawing.Color? color)
    {
        return _hw?.IsIntelNucMachine == true && UmiLightbar.SetEffect(effect, color, light);
    }

    internal async Task<bool> SetLightbarEffectAsync(string effect)
    {
        try
        {
            (int light, int speed, Color? color) = ReadLightArgs("HidLightbar/Ctrl", effect, keyboard: false);
            if (_hw?.IsIntelNucMachine == true)
            {
                // 原厂灯带 Monocolor / Rainbow 都会写 RGB；不是单色效果也要带颜色，
                // 否则彩虹模式的亮度和混色不会变。
                if (color == null)
                {
                    try
                    {
                        color = System.Drawing.Color.FromArgb(
                            MechrevoLite.Hardware.LightingSettingsStore.Load("HidLightbar/Ctrl", effect).ColorArgb);
                    }
                    catch
                    {
                    }
                }
                return UmiLightbar.SetEffect(effect, color, light);
            }
            return _service != null && await _service.SetLightEffect("HidLightbar/Ctrl", effect, light, speed, "None", color, true).ConfigureAwait(false);
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
            (int light, int speed, Color? color) = ReadLightArgs("HidLightbar_Logo/Ctrl", effect, keyboard: false);
            return _service != null && await _service.SetLightEffect("HidLightbar_Logo/Ctrl", effect, light, speed, "None", color, true).ConfigureAwait(false);
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
        if (_hw == null && !string.Equals(key, "winupdate", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(key, "winupdate", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsUpdateGuard.IsDisabled() == true;
        }
        if (string.Equals(key, "touchpad", StringComparison.OrdinalIgnoreCase)
            && (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true))
        {
            object? ptp = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status",
                "Enabled", null);
            if (ptp != null)
            {
                return Convert.ToInt32(ptp) != 0;
            }
        }
        if (string.Equals(key, "touchpad", StringComparison.OrdinalIgnoreCase)
            && (_hw?.IsIntelNucMachine == true || _hw?.IsUmiMachine == true))
        {
            object? ptp = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status",
                "Enabled", null);
            if (ptp != null)
            {
                return Convert.ToInt32(ptp) != 0;
            }
        }
        if (string.Equals(key, "wifi", StringComparison.OrdinalIgnoreCase)
            && MechrevoLite.Helpers.RadioSwitch.Available(MechrevoLite.Helpers.RadioSwitchKind.Wifi)
            && MechrevoLite.Helpers.RadioSwitch.TryGet(MechrevoLite.Helpers.RadioSwitchKind.Wifi, out bool radioWifi, out _))
        {
            return radioWifi;
        }
        if (string.Equals(key, "bt", StringComparison.OrdinalIgnoreCase)
            && MechrevoLite.Helpers.RadioSwitch.Available(MechrevoLite.Helpers.RadioSwitchKind.Bluetooth)
            && MechrevoLite.Helpers.RadioSwitch.TryGet(MechrevoLite.Helpers.RadioSwitchKind.Bluetooth, out bool radioBt, out _))
        {
            return radioBt;
        }
        bool? system = ReadSystemQuick(key);
        if (system.HasValue)
        {
            return system.Value;
        }
        if (MechrevoLite.Hardware.XingyaoQuickSwitch.IsApplicable(_hw)
            && MechrevoLite.Hardware.XingyaoQuickSwitch.TryGet(key, out bool xingyaoQuick, out _))
        {
            return xingyaoQuick;
        }
        if (string.Equals(key, "logolight", StringComparison.OrdinalIgnoreCase)
            && _hw.UsesBaolongda && _hw.TryReadLogoLightViaBackend(out bool logoOn))
        {
            // 0.293.5.1：宝龙达 Logo 灯状态以硬件功能号 15 直读为准，不读 3 秒镜像。
            return logoOn;
        }
        if (string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase))
        {
            if (_hw.UsesBaolongda) return MechrevoHw.FanBoostActive;
            if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
            {
                // NUC/UMI 强冷状态来自 EC 0x751，由 NucQuickSwitches.Sync 回填。
                return _hw.QuickSwitches.TryGetValue("fanboost", out bool quickOn) && quickOn;
            }
            return _hw.FanBoost;
        }
        if (string.Equals(key, "highperf", StringComparison.OrdinalIgnoreCase))
        {
            // 50 系 GCU：状态来自 GCU 的 HighPerformancePowerModeSwitch 回包。
            if (_hw?.IsGcuConnected == true
                && !_hw.UsesBaolongda
                && !_hw.IsIntelNucMachine
                && !_hw.IsUmiMachine
                && _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen50
                && _hw.QuickSwitches.TryGetValue("highperf", out bool gcuHighPerf))
            {
                return gcuHighPerf;
            }
            return MechrevoLite.Mode.PerfModeService.Instance?.WindowsPowerOverlayEnabled == true;
        }
        if (string.Equals(key, "gcuosd", StringComparison.OrdinalIgnoreCase))
        {
            return _hw != null && _hw.QuickSwitches.TryGetValue("osd", out bool gcuOsd) && gcuOsd;
        }
        if (string.Equals(key, "osd", StringComparison.OrdinalIgnoreCase))
        {
            return MechrevoLite.AppConfig.Get("lm_osd_enabled", 1) != 0;
        }
        if (string.Equals(key, "apuboost", StringComparison.OrdinalIgnoreCase))
        {
            return MechrevoLite.Mode.PerfModeService.Instance?.ApuAntiDegradeEnabled == true;
        }
        return _hw.QuickSwitches.TryGetValue(key, out bool value) && value;
    }

    private static bool? ReadSystemQuick(string key)
    {
        try
        {
            switch (key)
            {
                case "capslock":
                    return MechrevoLite.Helpers.NativeLockKey.IsCapsLockOn();
                case "numpad":
                    return MechrevoLite.Helpers.NativeLockKey.IsNumLockOn();
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
            return LastError.Length == 0 ? WpfStrings.L("未连接", "Not connected") : LastError;
        }
        try
        {
            return "CPU " + hw.CpuTemp + "℃ · GPU " + hw.GpuTemp
                + WpfStrings.L("℃ · 风扇 ", "°C · fans ") + hw.CpuFanRpm + "/" + hw.GpuFanRpm
                + WpfStrings.L(" rpm · 电池 ", " rpm · battery ") + hw.BatteryPercent + "%"
                + " · " + (hw.IsConnected ? WpfStrings.L("已连接", "Connected") : WpfStrings.L("未连接", "Not connected"))
                + (Connected ? "" : WpfStrings.L(" · 连接未确认", " · connection unconfirmed"));
        }
        catch (Exception ex)
        {
            return WpfStrings.L("读取异常：", "Read error: ") + ex.GetType().Name;
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
