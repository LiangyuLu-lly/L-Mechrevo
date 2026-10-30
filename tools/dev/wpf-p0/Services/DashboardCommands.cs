using System.Drawing;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 宽屏/迷你窗共享的命令入口（第二步：命令收口）。
/// 窗口只负责对话框/OSD/布局；硬件动作统一从这里进。
/// </summary>
internal interface IDashboardCommands
{
    Task<bool> ToggleQuickAsync(string key);
    Task<bool> SetQuickAsync(string key, bool on);
    Task<ServiceResult> SetPerformanceModeAsync(int visualMode);
    Task<MechrevoLite.Hardware.MechrevoService.ModeSwitchResult> SetPerformanceModeResultAsync(int visualMode);
    Task<ServiceResult> SetFanBoostAsync(bool on);
    Task<ServiceResult> ToggleFanBoostAsync();
    Task<ServiceResult> SetGpuModeAsync(int mode);
    Task<ServiceResult> RequestGpuRestartAsync(int mode);
    Task<ServiceResult> SetBatteryModeAsync(int mode);
    Task<ServiceResult> SaveChargeLimitAsync(int lowPercent, int highPercent);
    ServiceResult EnableChargeLimitRaw(int lowPercent, int highPercent);
    ServiceResult DisableChargeLimitRaw();
    Task<ServiceResult> SetChargeSliderEnabledAsync(bool on);
    Task<ServiceResult> SetEcDirectWriteAsync(bool on);
    Task<ServiceResult> SetLightPowerAsync(string topic, bool on);
    Task<ServiceResult> SetKeyboardEffectAsync(string effect);
    Task<ServiceResult> SetLightbarEffectAsync(string effect);
    Task<ServiceResult> SetLogoEffectAsync(string effect);
    Task<ServiceResult> SetHingeEffectAsync(string effect);
    Task<ServiceResult> SetSyncEffectAsync(string effect);
    ServiceResult SetLightIdleSeconds(int seconds);
    ServiceResult SetLightOffOnBattery(bool on);
    ServiceResult SetBrightness(int percent);
    Task<ServiceResult> SetRefreshRateAsync(int hertz);
    Task<ServiceResult> SetAutoRefreshAsync(bool on);
    Task<bool> SetLiquidFanAsync(int profile);
    Task<bool> ApplyLiquidLightAsync(string profile, Color color);
}

internal sealed class DashboardCommands : IDashboardCommands
{
    private readonly AppServices? _services;
    private readonly BackendProbe? _probe;
    private readonly Func<WaterCoolerRoutingService?>? _water;

    internal DashboardCommands(AppServices? services, BackendProbe? probe, Func<WaterCoolerRoutingService?>? water = null)
    {
        _services = services;
        _probe = probe;
        _water = water;
    }

    public async Task<bool> ToggleQuickAsync(string key)
    {
        if (_services != null)
        {
            return (await _services.QuickSwitches.ToggleAsync(key).ConfigureAwait(false)).Ok;
        }
        if (_probe == null)
        {
            return false;
        }
        bool current = _probe.QuickState(key);
        return await _probe.SwitchQuickAsync(key, !current).ConfigureAwait(false);
    }

    public async Task<bool> SetQuickAsync(string key, bool on)
    {
        if (_services != null)
        {
            return (await _services.QuickSwitches.SetAsync(key, on).ConfigureAwait(false)).Ok;
        }
        if (_probe == null)
        {
            return false;
        }
        return await _probe.SwitchQuickAsync(key, on).ConfigureAwait(false);
    }

    public Task<ServiceResult> SetPerformanceModeAsync(int visualMode) =>
        _services != null ? _services.Performance.SetModeAsync(visualMode) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<MechrevoLite.Hardware.MechrevoService.ModeSwitchResult> SetPerformanceModeResultAsync(int visualMode) =>
        _services != null
            ? _services.Performance.SetModeResultAsync(visualMode)
            : Task.FromResult(MechrevoLite.Hardware.MechrevoService.ModeSwitchResult.Failed);

    public Task<ServiceResult> SetFanBoostAsync(bool on) =>
        _services != null ? _services.Performance.SetFanBoostAsync(on) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> ToggleFanBoostAsync() =>
        _services != null ? _services.Performance.ToggleFanBoostAsync() : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetGpuModeAsync(int mode) =>
        _services != null ? _services.Gpu.SwitchAsync(mode) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> RequestGpuRestartAsync(int mode) =>
        _services != null ? _services.Gpu.RequestRestartAsync(mode) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetBatteryModeAsync(int mode) =>
        _services != null ? _services.Battery.SetModeAsync(mode) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SaveChargeLimitAsync(int lowPercent, int highPercent) =>
        Task.FromResult(_services != null ? _services.ChargeLimit.SaveThreshold(lowPercent, highPercent) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public ServiceResult EnableChargeLimitRaw(int lowPercent, int highPercent) =>
        _services != null ? _services.ChargeLimit.EnableThresholdRaw(lowPercent, highPercent) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready"));

    public ServiceResult DisableChargeLimitRaw() =>
        _services != null ? _services.ChargeLimit.DisableThresholdRaw() : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready"));

    public Task<ServiceResult> SetChargeSliderEnabledAsync(bool on) =>
        Task.FromResult(_services != null ? _services.ChargeLimit.SetSliderEnabled(on) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetEcDirectWriteAsync(bool on) =>
        Task.FromResult(_services != null ? _services.ChargeLimit.SetEcDirectWrite(on) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetLightPowerAsync(string topic, bool on) =>
        _services != null ? _services.Lighting.SetLightPowerAsync(topic, on) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetKeyboardEffectAsync(string effect) =>
        _services != null ? _services.Lighting.SetKeyboardEffectAsync(effect) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetLightbarEffectAsync(string effect) =>
        _services != null ? _services.Lighting.SetLightbarEffectAsync(effect) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetLogoEffectAsync(string effect) =>
        _services != null ? _services.Lighting.SetLogoEffectAsync(effect) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetHingeEffectAsync(string effect) =>
        _services != null ? _services.Lighting.SetHingeEffectAsync(effect) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetSyncEffectAsync(string effect) =>
        _services != null ? _services.Lighting.SetSyncEffectAsync(effect) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public ServiceResult SetLightIdleSeconds(int seconds) =>
        _services != null ? _services.Lighting.SetLightIdleSeconds(seconds) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready"));

    public ServiceResult SetLightOffOnBattery(bool on) =>
        _services != null ? _services.Lighting.SetLightOffOnBattery(on) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready"));

    public ServiceResult SetBrightness(int percent) =>
        _services != null ? _services.Display.SetBrightness(percent) : ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready"));

    public Task<ServiceResult> SetRefreshRateAsync(int hertz) =>
        _services != null ? _services.Display.SetRefreshRateAsync(hertz) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public Task<ServiceResult> SetAutoRefreshAsync(bool on) =>
        _services != null ? _services.Display.SetAutoRefreshAsync(on) : Task.FromResult(ServiceResult.Fail(WpfStrings.L("服务未就绪", "Service not ready")));

    public async Task<bool> SetLiquidFanAsync(int profile)
    {
        WaterCoolerRoutingService? routing = _water?.Invoke();
        return routing != null && await routing.SetFanAsync(profile).ConfigureAwait(false);
    }

    public async Task<bool> ApplyLiquidLightAsync(string profile, Color color)
    {
        WaterCoolerRoutingService? routing = _water?.Invoke();
        return routing != null && await routing.ApplyLightAsync(profile, color).ConfigureAwait(false);
    }
}