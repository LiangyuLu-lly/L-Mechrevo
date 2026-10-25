using System.Threading.Tasks;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 显卡模式（两窗共享；恢复重启、核显省电只暴露**只读能力位**）✅
/// <para>⭐ 解决的原问题：迷你窗调 <c>WideSetGpuMode</c> ⇒ 实际触发宽屏窗口的
/// <c>GpuMode_Click</c>（里面含“重启提示对话框/宝龙达不支持核显”等 UI 决策）。</para>
/// <para>⚠️ 本服务**刻意不含对话框**：重启确认对话框、DGPU 占用确认仍留在窗口
/// （迁移点见 <c>docs/notes/WPF共享业务服务层设计-20261026.md</c> §4）。</para>
/// </summary>
internal interface IGpuService
{
    /// <summary>当前显卡模式（0=核显 / 1=混合 / 2=独显直连；-1=未连接）。</summary>
    int CurrentMode { get; }

    /// <summary>本机能否切到该模式（能力位判据，无 UI）。</summary>
    bool CanSwitch(int mode);

    /// <summary>宝龙达本机通道切显卡模式（同步 + 回读确认；调用方负责写“重启后生效”提示与 pending 标记）。</summary>
    ServiceResult SwitchOnBaolongda(int mode, out bool confirmed);

    /// <summary>GCU 路径切显卡模式（原 <c>SwitchGpuMode</c> MQTT）。</summary>
    Task<ServiceResult> SwitchAsync(int mode);

    /// <summary>请求“切换并重启”（GCU 路径）。</summary>
    Task<ServiceResult> RequestRestartAsync(int mode);

    /// <summary>刷新 GPU 状态（只读）。</summary>
    Task RefreshAsync();

    /// <summary>是否有“重启后生效”的待处理切换。</summary>
    bool HasPendingRestart { get; }

    /// <summary>待处理切换的目标模式（-1 = 无）。</summary>
    int PendingTargetMode { get; }

    /// <summary>标记“重启后生效”（原 <c>MarkGpuRestartPending</c>）。</summary>
    void MarkRestartPending(int mode);

    /// <summary>清除待处理标记。</summary>
    void ClearRestartPending();

    /// <summary>是否支持核显省电热切换（只读能力位）。</summary>
    bool SupportsIgpuSaving { get; }

    /// <summary>当前核显省电模式（-1 = 读不到）。</summary>
    int IgpuSavingMode { get; }
}

/// <inheritdoc />
internal sealed class GpuService : IGpuService
{
    private readonly BackendAccessor _backend;

    internal GpuService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public int CurrentMode => _backend.Probe?.CurrentGpuMode ?? -1;

    public bool CanSwitch(int mode) => _backend.Probe?.CanSwitchGpuMode(mode) == true;

    public ServiceResult SwitchOnBaolongda(int mode, out bool confirmed)
    {
        confirmed = false;
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        bool ok = probe.SwitchGpuModeBaolongda(mode, out confirmed);
        return ok
            ? ServiceResult.Success(confirmed ? "已切到目标模式，重启后生效" : "已请求切换，回读未确认，重启后确认")
            : ServiceResult.Fail("宝龙达显卡模式写入失败");
    }

    public async Task<ServiceResult> SwitchAsync(int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            bool ok = await probe.SwitchGpuModeAsync(mode).ConfigureAwait(false);
            return ok ? ServiceResult.Success("显卡模式已下发") : ServiceResult.Fail("显卡模式下发失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("显卡模式下发异常：" + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> RequestRestartAsync(int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        try
        {
            bool ok = await probe.RequestGpuModeRestartAsync(mode).ConfigureAwait(false);
            return ok ? ServiceResult.Success("已请求切换并重启，重启后生效") : ServiceResult.Fail("重启切换请求失败");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("重启切换请求异常：" + ex.GetType().Name);
        }
    }

    public async Task RefreshAsync()
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return;
        }
        try
        {
            await probe.RefreshGpuModeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public bool HasPendingRestart => _backend.Probe?.HasPendingGpuRestart == true;

    public int PendingTargetMode => _backend.Probe?.PendingGpuTarget ?? -1;

    public void MarkRestartPending(int mode)
    {
        _backend.Probe?.MarkGpuRestartPending(mode);
    }

    public void ClearRestartPending()
    {
        _backend.Probe?.ClearGpuRestartPending();
    }

    public bool SupportsIgpuSaving => _backend.Hw?.SupportsGpuHotSwap == true;

    public int IgpuSavingMode => _backend.Hw?.IgpuOnlySaveMode ?? -1;
}
