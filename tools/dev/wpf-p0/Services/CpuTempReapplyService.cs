using System;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Helpers;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 解锁后重放 CPU 温度墙（旧 ModeControl.AutoCPUTemp 的等价实现）。
/// 只在 AMD、已开启 UV、管理员且温度值有效时执行；失败只记日志，不阻塞会话恢复。
/// </summary>
internal static class CpuTempReapplyService
{
    internal static async Task ReapplyAfterUnlockAsync()
    {
        try
        {
            await Task.Delay(2000).ConfigureAwait(false);
            if (!PawnIO.CpuInfo.IsAMD || !AppConfig.IsApplyUV() || !ProcessHelper.IsUserAdministrator())
            {
                return;
            }
            int temperature = AppConfig.GetMode("cpu_temp");
            if (temperature < PawnIO.CpuInfo.MinTemp || temperature > PawnIO.CpuInfo.DefaultTemp)
            {
                return;
            }
            PawnIO.RyzenSmuService? smu = MechrevoLite.Mode.ModeControl.SharedSmu;
            if (smu == null)
            {
                return;
            }
            PawnIO.SmuStatus status = smu.SetThm(temperature);
            Logger.WriteLine("[session] 解锁后重放 CPU 温度墙=" + temperature + "℃ ⇒ " + status);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[session] 解锁后重放 CPU 温度墙失败（已吞掉）：" + ex.Message);
        }
    }
}
