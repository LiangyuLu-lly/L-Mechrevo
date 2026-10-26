using System.Collections.Generic;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 性能模式 + 风扇强冷（两窗共享）✅
/// <para>⭐ 解决的原问题：迷你窗原来要调宽屏窗口的 <c>WideSetMode</c>/<c>WideToggleQuickAsync("fanboost")</c>；
/// 现在两窗都只依赖本接口。</para>
/// </summary>
internal interface IPerformanceService
{
    /// <summary>当前性能模式（0=平衡 / 1=狂暴 / 2=静音 / 3/4=自定义；-1=未连接）。</summary>
    int CurrentMode { get; }

    /// <summary>模式显示名（与原窗口同一张表，见 <see cref="PerformanceModeLabel" />）。</summary>
    string ModeLabel(int mode);

    /// <summary>切换性能模式（经 <see cref="BackendProbe.SwitchModeResultAsync" />）。</summary>
    Task<ServiceResult> SetModeAsync(int mode);

    /// <summary>Set mode while preserving the raw Superseded semantics (window orchestration; 1:1 with BackendProbe).</summary>
    Task<MechrevoLite.Hardware.MechrevoService.ModeSwitchResult> SetModeResultAsync(int mode);

    /// <summary>Raw fan-boost path (GCU/Quick only; 1:1 with the window's previous direct call).</summary>
    Task<bool> SwitchFanBoostRawAsync(bool on);

    /// <summary>全部模式定义（内置 + 自定义；只读快照）。</summary>
    IReadOnlyList<PerfModeDefinition> Modes { get; }

    /// <summary>本机是否有风扇强冷通道（宝龙达本机通道也计入）。</summary>
    bool FanBoostSupported { get; }

    /// <summary>强冷当前是否开启。</summary>
    bool FanBoostOn { get; }

    /// <summary>开/关强冷；宝龙达走本机通道（功能号 21/13 + 回读），其它走 <c>SwitchQuick</c>。</summary>
    Task<ServiceResult> SetFanBoostAsync(bool on);

    /// <summary>翻转强冷（内部先读当前状态，⛔ 不猜）。</summary>
    Task<ServiceResult> ToggleFanBoostAsync();
}

/// <inheritdoc />
internal sealed class PerformanceService : IPerformanceService
{
    private readonly BackendAccessor _backend;

    internal PerformanceService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public int CurrentMode => _backend.Probe?.CurrentMode ?? -1;

    public string ModeLabel(int mode) => PerformanceModeLabel.Of(mode);

    public async Task<ServiceResult> SetModeAsync(int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            MechrevoService.ModeSwitchResult result = await probe.SwitchModeResultAsync(mode).ConfigureAwait(false);
            return result switch
            {
                MechrevoService.ModeSwitchResult.Ok => ServiceResult.Success(WpfStrings.OriginalStyleWindow_xaml_Text_8),
                MechrevoService.ModeSwitchResult.Superseded => ServiceResult.Fail(WpfStrings.PerfModeSuperseded),
                _ => ServiceResult.Fail(WpfStrings.PerfModeFailed)
            };
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.PerfModeExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<MechrevoLite.Hardware.MechrevoService.ModeSwitchResult> SetModeResultAsync(int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return MechrevoLite.Hardware.MechrevoService.ModeSwitchResult.Failed;
        }
        try
        {
            return await probe.SwitchModeResultAsync(mode).ConfigureAwait(false);
        }
        catch
        {
            return MechrevoLite.Hardware.MechrevoService.ModeSwitchResult.Failed;
        }
    }

    public async Task<bool> SwitchFanBoostRawAsync(bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return false;
        }
        try
        {
            return await probe.SwitchQuickAsync("fanboost", on).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<PerfModeDefinition> Modes
    {
        get
        {
            try
            {
                return PerfModeStore.Load();
            }
            catch
            {
                return Array.Empty<PerfModeDefinition>();
            }
        }
    }

    public bool FanBoostSupported
    {
        get
        {
            BackendProbe? probe = _backend.Probe;
            // ⚠️ 迁移点：BackendProbe.SupportsQuick("fanboost") 只认 GCU 能力位，宝龙达会判 false；
            //   服务层补上 UsesBaolongda（与 WinForms 侧 2026-10-19 的修复同一口径）。
            return _backend.IsBaolongda || probe?.SupportsQuick("fanboost") == true;
        }
    }

    public bool FanBoostOn
    {
        get
        {
            if (_backend.IsBaolongda)
            {
                return MechrevoHw.FanBoostActive;
            }
            return _backend.Probe?.QuickState("fanboost") == true;
        }
    }

    public async Task<ServiceResult> SetFanBoostAsync(bool on)
    {
        if (_backend.IsBaolongda)
        {
            MechrevoHw? hw = _backend.Hw;
            if (hw == null)
            {
                return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
            }
            try
            {
                // 宝龙达：暂停曲线闭环 → 写满转 → 功能号 13 回读（≥1500 rpm 才算确认）。
                bool handled = hw.TrySetFanBoostViaBackend(on, out bool confirmed);
                if (!handled)
                {
                    return ServiceResult.Fail(WpfStrings.QuickFanBoostBackendUnavailable);
                }
                return confirmed
                    ? ServiceResult.Success(on ? WpfStrings.QuickFanBoostOnConfirmed : WpfStrings.QuickFanBoostOffConfirmed)
                    : ServiceResult.Fail(on ? WpfStrings.QuickFanBoostOnUnconfirmed : WpfStrings.QuickFanBoostOffUnconfirmed);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail(WpfStrings.FanBoostExceptionPrefix + ex.GetType().Name);
            }
        }

        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SwitchQuickAsync("fanboost", on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? WpfStrings.FanBoostOn : WpfStrings.FanBoostOff) : ServiceResult.Fail(WpfStrings.FanBoostFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.FanBoostExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> ToggleFanBoostAsync()
    {
        return await SetFanBoostAsync(!FanBoostOn).ConfigureAwait(false);
    }
}
