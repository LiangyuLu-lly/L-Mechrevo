using System.Collections.Generic;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

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
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            MechrevoService.ModeSwitchResult result = await probe.SwitchModeResultAsync(mode).ConfigureAwait(false);
            return result switch
            {
                MechrevoService.ModeSwitchResult.Ok => ServiceResult.Success("模式已下发"),
                MechrevoService.ModeSwitchResult.Superseded => ServiceResult.Fail("模式请求被更新的一次请求取代"),
                _ => ServiceResult.Fail("模式下发失败")
            };
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("模式下发异常：" + ex.GetType().Name);
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
                return ServiceResult.Fail("后端未连接");
            }
            try
            {
                // 宝龙达：暂停曲线闭环 → 写满转 → 功能号 13 回读（≥1500 rpm 才算确认）。
                bool handled = hw.TrySetFanBoostViaBackend(on, out bool confirmed);
                if (!handled)
                {
                    return ServiceResult.Fail("本机强冷通道不可用");
                }
                return confirmed
                    ? ServiceResult.Success(on ? "强冷已开启（回读确认）" : "强冷已关闭（已恢复原曲线）")
                    : ServiceResult.Fail(on ? "强冷已下发但回读未确认" : "强冷关闭未确认");
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail("强冷异常：" + ex.GetType().Name);
            }
        }

        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            bool ok = await probe.SwitchQuickAsync("fanboost", on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? "强冷已开启" : "强冷已关闭") : ServiceResult.Fail("强冷下发失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("强冷异常：" + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> ToggleFanBoostAsync()
    {
        return await SetFanBoostAsync(!FanBoostOn).ConfigureAwait(false);
    }
}
