using System.Threading.Tasks;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 电池域（电量/健康/三档保护模式；两窗共享）✅
/// <para>⭐ 解决的原问题：<c>WideBattery()</c> 返回的是 <c>BatteryInfo.Text</c> / <c>ChargeLimitStatus.Text</c>
/// 这类**控件文本**，迷你窗只能间接读标签。</para>
/// <para>⛔ 本服务不拼“充电中/已充满”等展示文案：只给硬件字段 + 交直流状态，由 View 格式化
/// （原窗口 2698-2703 行的文案仍留在窗口，等 ViewModel 化时再抽）。</para>
/// </summary>
internal interface IBatteryService
{
    /// <summary>电池快照（硬件字段 + 系统供电状态）。</summary>
    BatterySnapshot Read();

    /// <summary>当前电池保护档位（0/1/2；-1 = 未连接）。</summary>
    int CurrentMode { get; }

    /// <summary>设置电池保护档位（0=长效 / 1=平衡 / 2=工作站）。</summary>
    Task<ServiceResult> SetModeAsync(int mode);
}

/// <inheritdoc />
internal sealed class BatteryService : IBatteryService
{
    private readonly BackendAccessor _backend;

    internal BatteryService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public BatterySnapshot Read()
    {
        MechrevoHw? hw = _backend.Hw;
        if (hw == null)
        {
            return BatterySnapshot.Empty;
        }
        bool acOnline = false;
        bool charging = false;
        try
        {
            // 与窗口 RefreshFromBackend 同一来源（WinForms PowerStatus，纯系统 API，非窗口依赖）。
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            acOnline = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            charging = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
        }
        catch
        {
        }
        int percent = WindowsBattery.ReadPercent();
        if (percent < 0)
        {
            percent = hw.BatteryPercent;
        }
        if (percent < 0)
        {
            percent = 0;
        }
        return new BatterySnapshot(
            HardwareAvailable: true,
            Percent: percent,
            ProtectionMode: hw.BatteryProtection,
            CycleCount: hw.BatteryCycleCount,
            CapacityText: hw.BatteryCapacityText,
            Abnormal: hw.BatteryAbnormal,
            AcOnline: acOnline,
            Charging: charging);
    }

    public int CurrentMode => _backend.Probe?.BatteryMode ?? -1;

    public async Task<ServiceResult> SetModeAsync(int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.L("后端未连接", "Backend not connected"));
        }
        try
        {
            bool ok = await probe.SetBatteryModeAsync(mode).ConfigureAwait(false);
            return ok ? ServiceResult.Success(WpfStrings.L("电池档位已下发", "Battery profile sent")) : ServiceResult.Fail(WpfStrings.L("电池档位下发失败", "Failed to send battery profile"));
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.L("电池档位下发异常：", "Battery profile error: ") + ex.GetType().Name);
        }
    }
}
