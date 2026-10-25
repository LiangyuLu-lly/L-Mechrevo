using System.Collections.Generic;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

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
                foreach ((string label, int mins, int idleSeconds) in MechrevoLite.SettingsForm.LightingCloseTimerOptions)
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
                return ServiceResult.Success("自动关灯已写入键盘设置");
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail("自动关灯写入异常：" + ex.GetType().Name);
            }
        }
        AppConfig.Set(IdleSecondsKey, normalized);
        AppConfig.Flush();
        try
        {
            KeyboardRgb? rgb = Program.rgb;
            if (rgb != null)
            {
                rgb.CloseTimerMinutes = normalized / 60;
                rgb.SaveConfig();
            }
        }
        catch
        {
        }
        return ServiceResult.Success("自动关灯已保存");
    }

    public bool LightOffOnBattery => AppConfig.Is(OffOnBatteryKey);

    public ServiceResult SetLightOffOnBattery(bool on)
    {
        AppConfig.Set(OffOnBatteryKey, on ? 1 : 0);
        AppConfig.Flush();
        return ServiceResult.Success(on ? "已开启离电关灯（生效由灯效核对触发）" : "已关闭离电关灯");
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
        static (probe, effect) => probe.SetKeyboardEffectAsync(effect), effectId, "键盘灯效");

    public Task<ServiceResult> SetLightbarEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetLightbarEffectAsync(effect), effectId, "灯带灯效");

    public Task<ServiceResult> SetLogoEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetLogoEffectAsync(effect), effectId, "Logo灯效");

    public Task<ServiceResult> SetHingeEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetHingeEffectAsync(effect), effectId, "铰链灯效");

    public Task<ServiceResult> SetSyncEffectAsync(string effectId) => RunProbeAsync(
        static (probe, effect) => probe.SetSyncEffectAsync(effect), effectId, "同步灯效");

    public async Task<ServiceResult> SetKeyboardPowerAsync(bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            bool ok = await probe.SetKeyboardPowerAsync(on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? "键盘灯已开启" : "键盘灯已关闭") : ServiceResult.Fail("键盘灯电源下发失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("键盘灯电源异常：" + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetLightPowerAsync(string topic, bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            bool ok = await probe.SetLightPowerAsync(topic, on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? "灯光已开启" : "灯光已关闭") : ServiceResult.Fail("灯光电源下发失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("灯光电源异常：" + ex.GetType().Name);
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
            return ServiceResult.Fail("后端未连接");
        }
        if (string.IsNullOrWhiteSpace(effectId))
        {
            return ServiceResult.Fail(displayName + "为空");
        }
        try
        {
            bool ok = await apply(probe, effectId).ConfigureAwait(false);
            return ok ? ServiceResult.Success(displayName + "已下发") : ServiceResult.Fail(displayName + "下发失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(displayName + "异常：" + ex.GetType().Name);
        }
    }
}
