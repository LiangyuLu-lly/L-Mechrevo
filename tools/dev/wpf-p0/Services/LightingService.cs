using System.Collections.Generic;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Services;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>⭐ 灯光通道 topic 常量（与生产代码 <c>MqttTopics</c> 同源；铰链/同步目前只有字面量）。</summary>
internal static class LightingTopics
{
    internal const string Keyboard = MqttTopics.KeyboardCtrl;
    internal const string Lightbar = MqttTopics.LightbarCtrl;
    internal const string Logo = MqttTopics.LogoLightCtrl;

    /// <summary>⚠️ 生产侧尚无 <c>MqttTopics</c> 常量（BackendProbe 里也是字面量）⇒ 先在此集中。</summary>
    internal const string Hinge = "HidLightbar_Hinge/Ctrl";

    /// <summary>⚠️ 同 <see cref="Hinge" />。</summary>
    internal const string Sync = "HidLightbar_Sync/Ctrl";
}

/// <summary>
/// ⭐ 灯光域（通道设置持久化 + 自动关灯 + 灯效下发；两窗共享）✅
/// <para>⭐ 解决的原问题：<c>WideLightIdleOptions()/WideLightIdleSeconds()/WideSetLightIdleSeconds()</c>
/// /<c>WideLightOffOnBattery()</c> 与 <c>WideMoreSwitches()</c> 混在宽屏窗口里；迷你窗无法独立取用。</para>
/// <para>⭐ 通道设置仍复用生产 <see cref="LightingSettingsStore" />（⛔ 不复制解析/写盘逻辑）；
/// 灯效下发复用 <see cref="BackendProbe" /> 的既有封装（它内部已处理 ARGB/单色判定）。</para>
/// <para>⚠️ 迁移点：<c>ReconcileLightingPowerAsync()</c>（离电/空闲熄灭的实际下发与定时器）仍在窗口；
/// 本服务只负责配置落盘，避免把灯效电源状态机搬进共享层。</para>
/// </summary>
internal interface ILightingService
{
    /// <summary>读通道设置（不存在时返回默认值，与 <see cref="LightingSettingsStore.Load" /> 同语义）。</summary>
    LightChannelSettings LoadChannel(string topic, string defaultEffect);

    /// <summary>尝试读通道设置（不存在/读失败 ⇒ false）。</summary>
    bool TryLoadChannel(string topic, out LightChannelSettings settings);

    /// <summary>保存通道设置（用户操作路径；会记“用户明确设置过灯光”标记）。</summary>
    void SaveChannel(string topic, LightChannelSettings settings);

    /// <summary>保存通道电源开关（会写 powerexplicit=1，仅用户点击时调用）。</summary>
    void SaveChannelPower(string topic, bool powerOn, string trigger);

    /// <summary>用户是否明确设置过灯光（安装/升级后不覆盖硬件状态的前提）。</summary>
    bool HasUserLightingIntent { get; }

    /// <summary>自动关灯可选项（与 WinForms SettingsForm 的表同源）。</summary>
    IReadOnlyList<LightIdleOption> LightIdleOptions { get; }

    /// <summary>当前自动关灯秒数（宝龙达读硬件设置，其它读 config）。</summary>
    int LightIdleSeconds { get; }

    /// <summary>设置自动关灯秒数（只落盘/写硬件设置；电源状态核对仍由窗口触发）。</summary>
    ServiceResult SetLightIdleSeconds(int seconds);

    /// <summary>把当前自动关灯配置同步给 GCU 固件计时器（0 = 关闭）。</summary>
    void ApplyHardwareLightIdleTimer();

    /// <summary>离电时是否关灯（config）。</summary>
    bool LightOffOnBattery { get; }

    /// <summary>设置“离电关灯”（只落盘；实际熄灭由窗口的 Reconcile 触发）。</summary>
    ServiceResult SetLightOffOnBattery(bool on);

    /// <summary>当前键盘可见灯效（按键盘类型裁剪；与原窗口 FillKeyboardEffects 同源）。</summary>
    IReadOnlyList<(string Id, string Label)> VisibleKeyboardEffects { get; }

    /// <summary>下发键盘灯效。</summary>
    Task<ServiceResult> SetKeyboardEffectAsync(string effectId);

    /// <summary>下发灯带灯效。</summary>
    Task<ServiceResult> SetLightbarEffectAsync(string effectId);

    /// <summary>下发 Logo 灯效。</summary>
    Task<ServiceResult> SetLogoEffectAsync(string effectId);

    /// <summary>下发铰链灯效。</summary>
    Task<ServiceResult> SetHingeEffectAsync(string effectId);

    /// <summary>下发同步灯效。</summary>
    Task<ServiceResult> SetSyncEffectAsync(string effectId);

    /// <summary>键盘灯电源。</summary>
    Task<ServiceResult> SetKeyboardPowerAsync(bool on);

    /// <summary>指定灯光通道电源（topic 见 <see cref="LightingTopics" />）。</summary>
    Task<ServiceResult> SetLightPowerAsync(string topic, bool on);
}

/// <inheritdoc />
internal sealed class LightingService : ILightingService
{
    private const string IdleSecondsKey = "lighting_idle_seconds";
    private const string OffOnBatteryKey = "lighting_off_on_battery";

    private readonly BackendAccessor _backend;

    internal LightingService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public LightChannelSettings LoadChannel(string topic, string defaultEffect)
    {
        try
        {
            return LightingSettingsStore.Load(topic, defaultEffect);
        }
        catch
        {
            return new LightChannelSettings(defaultEffect, 4, 1, -1, PowerOn: false);
        }
    }

    public bool TryLoadChannel(string topic, out LightChannelSettings settings)
    {
        settings = default;
        try
        {
            return LightingSettingsStore.TryLoad(topic, out settings);
        }
        catch
        {
            return false;
        }
    }

    public void SaveChannel(string topic, LightChannelSettings settings)
    {
        try
        {
            LightingSettingsStore.Save(topic, settings);
        }
        catch
        {
        }
    }

    public void SaveChannelPower(string topic, bool powerOn, string trigger)
    {
        try
        {
            LightingSettingsStore.SavePower(topic, powerOn, trigger);
        }
        catch
        {
        }
    }

    public bool HasUserLightingIntent
    {
        get
        {
            try
            {
                return LightingSettingsStore.HasUserLightingIntent();
            }
            catch
            {
                return false;
            }
        }
    }

    public IReadOnlyList<LightIdleOption> LightIdleOptions
    {
        get
        {
            List<LightIdleOption> options = new();
            try
            {
                foreach ((string label, int mins, int idleSeconds) in LightingCatalog.LightingCloseTimerOptions)
                {
                    options.Add(new LightIdleOption(label, mins, idleSeconds));
                }
            }
            catch
            {
            }
            return options;
        }
    }

    public int LightIdleSeconds
    {
        get
        {
            if (_backend.IsBaolongda)
            {
                try
                {
                    return MechrevoHw.GetKeyboardAutoOffSeconds();
                }
                catch
                {
                    return 0;
                }
            }
            return AppConfig.Get(IdleSecondsKey, 0);
        }
    }

    public ServiceResult SetLightIdleSeconds(int seconds)
    {
        int normalized = Math.Max(0, seconds);
        if (_backend.IsBaolongda)
        {
            try
            {
                MechrevoHw.SetKeyboardAutoOffSeconds(normalized);
                _backend.Hw?.ApplyKeyboardAutoOffSetting();
                return ServiceResult.Success(WpfStrings.LightAutoOffWritten);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail(WpfStrings.LightAutoOffWriteExceptionPrefix + ex.GetType().Name);
            }
        }
        AppConfig.Set(IdleSecondsKey, normalized);
        AppConfig.Flush();
        // 非宝龙达 GCU 的自动关灯在固件里；仅改 config 不会关掉固件计时器。
        ApplyHardwareLightIdleTimer();
        try
        {
            KeyboardRgb? rgb = L_Mechrevo.WpfP0.Services.WpfBackend.rgb;
            if (rgb != null)
            {
                rgb.CloseTimerMinutes = normalized / 60;
                rgb.SaveConfig();
            }
        }
        catch
        {
        }
        return ServiceResult.Success(WpfStrings.LightAutoOffSaved);
    }

    public void ApplyHardwareLightIdleTimer()
    {
        try
        {
            if (_backend.IsBaolongda)
            {
                return;
            }
            MechrevoService? service = _backend.Service;
            if (service == null)
            {
                return;
            }
            int seconds = AppConfig.Get(IdleSecondsKey, 0);
            int minutes = seconds <= 0 ? 0 : Math.Max(1, seconds / 60);
            _ = service.PublishKeyboardCloseTimer(minutes);
            Logger.WriteLine("[light] 已同步 GCU 自动关灯计时器：" + (minutes <= 0 ? "关闭" : minutes + " 分钟"));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[light] 同步 GCU 自动关灯计时器失败：" + ex.Message);
        }
    }

    public bool LightOffOnBattery => AppConfig.Is(OffOnBatteryKey);

    public ServiceResult SetLightOffOnBattery(bool on)
    {
        AppConfig.Set(OffOnBatteryKey, on ? 1 : 0);
        AppConfig.Flush();
        return ServiceResult.Success(on ? WpfStrings.LightOffOnBatteryOnLong : WpfStrings.LightOffOnBatteryOffShort);
    }

    public IReadOnlyList<(string Id, string Label)> VisibleKeyboardEffects
    {
        get
        {
            List<(string, string)> effects = new();
            try
            {
                int keyboardType = _backend.Hw?.Capabilities.KeyboardType ?? 0;
                foreach ((string id, string label) in KeyboardFirmwareEffects.Visible(keyboardType))
                {
                    effects.Add((id, label));
                }
            }
            catch
            {
            }
            return effects;
        }
    }

    public Task<ServiceResult> SetKeyboardEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetKeyboardEffectAsync(effect), effectId, WpfStrings.HidKeyboardWindow_Text_9);

    public Task<ServiceResult> SetLightbarEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetLightbarEffectAsync(effect), effectId, WpfStrings.LightChannelLightbar);

    public Task<ServiceResult> SetLogoEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetLogoEffectAsync(effect), effectId, WpfStrings.LightChannelLogo);

    public Task<ServiceResult> SetHingeEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetHingeEffectAsync(effect), effectId, WpfStrings.LightChannelHinge);

    public Task<ServiceResult> SetSyncEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetSyncEffectAsync(effect), effectId, WpfStrings.LightChannelSync);

    public async Task<ServiceResult> SetKeyboardPowerAsync(bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SetKeyboardPowerAsync(on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? WpfStrings.LightKeyboardOn : WpfStrings.LightKeyboardOff) : ServiceResult.Fail(WpfStrings.LightKeyboardPowerFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.LightKeyboardPowerExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetLightPowerAsync(string topic, bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SetLightPowerAsync(topic, on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? WpfStrings.LightPowerOn : WpfStrings.LightPowerOff) : ServiceResult.Fail(WpfStrings.LightPowerFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.LightPowerExceptionPrefix + ex.GetType().Name);
        }
    }

    private async Task<ServiceResult> RunProbeAsync(
        Func<BackendProbe, string, Task<bool>> apply,
        string effectId,
        string displayName)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        if (string.IsNullOrWhiteSpace(effectId))
        {
            return ServiceResult.Fail(displayName + WpfStrings.IsEmptySuffix);
        }
        try
        {
            bool ok = await apply(probe, effectId).ConfigureAwait(false);
            return ok ? ServiceResult.Success(displayName + WpfStrings.SentSuffix) : ServiceResult.Fail(displayName + WpfStrings.ApplyFailedSuffix);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(displayName + WpfStrings.ExceptionPrefix + ex.GetType().Name);
        }
    }
}
