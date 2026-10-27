using System.Threading.Tasks;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 宽屏/迷你窗共享的命令入口（第二步：命令收口）。
/// 目前先迁移快捷开关；后续模式/显卡/灯光/水冷按同一入口逐步搬入。
/// </summary>
internal sealed class DashboardCommands
{
    private readonly AppServices? _services;
    private readonly BackendProbe? _probe;

    internal DashboardCommands(AppServices? services, BackendProbe? probe)
    {
        _services = services;
        _probe = probe;
    }

    internal async Task<bool> ToggleQuickAsync(string key)
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

    internal async Task<bool> SetQuickAsync(string key, bool on)
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
}