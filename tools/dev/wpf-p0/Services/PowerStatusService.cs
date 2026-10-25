using System;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>电源/电池元数据快照（WPF 与迷你窗共用；⛔ 不含 UI 文案）。</summary>
internal sealed record PowerStatusSnapshot(
    PowerInputSample? Input,
    decimal? BatteryRateWatts,
    int Percent,
    bool AcOnline,
    bool Charging,
    bool LocalSamplingUnavailable);

/// <summary>
/// ⭐ 电源/电池静态采样的唯一 WPF 服务入口（P0-3 的补充）。
/// <para>旧 <c>HardwareControl</c> 的采样 API 仍属业务程序集；窗口不再直接散读静态字段。</para>
/// </summary>
internal static class PowerStatusService
{
    internal static PowerStatusSnapshot Read(bool sample = false)
    {
        try
        {
            if (sample) HardwareControl.ReadSensors();
            return new PowerStatusSnapshot(
                HardwareControl.PowerInput,
                HardwareControl.batteryRate,
                HardwareControl.GetBatteryChargePercentage(),
                System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online,
                System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging),
                HardwareControl.LocalPowerSamplingUnavailable);
        }
        catch
        {
            return new PowerStatusSnapshot(null, null, -1, false, false, true);
        }
    }

    internal static void ReadOverlay()
    {
        try { HardwareControl.ReadSensorsOverlay(); } catch { }
    }

    internal static void SampleLocalPower()
    {
        try { HardwareControl.SampleLocalPower(); } catch { }
    }

    internal static string ReadPowerWallText()
    {
        try
        {
            if (HardwareControl.LocalPowerSamplingUnavailable)
            {
                return PowerWallVerifier.UnavailableText;
            }
            PowerWallVerdict verdict = HardwareControl.powerWall.Evaluate(DateTime.Now);
            return verdict is PowerWallVerdict.Enforced or PowerWallVerdict.NotEnforced
                ? HardwareControl.powerWall.Describe(DateTime.Now)
                : "";
        }
        catch
        {
            return "";
        }
    }
}
