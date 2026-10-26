// ⭐ task-23：P0 启动恢复补齐（与旧 Program deferredInitialization 对齐）。
// 只做后端就绪后的启动恢复调度；不创建/不修改任何窗口，不改 MainWindow/OriginalStyleWindow。
using System;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

internal static class StartupRecoveryService
{
    private static int _scheduled;

    /// <summary>
    /// 启动后等待后端就绪，再依次执行旧 Program 的启动恢复项。
    /// ⚠️ 全部 try/catch 独立处理：某一项失败不阻塞其它项；每项都有日志。
    /// </summary>
    internal static void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0)
        {
            return;
        }
        _ = Task.Run(RunAsync);
    }

    private static async Task RunAsync()
    {
        MechrevoHw? hw = await WaitForConnectedHwAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (hw == null)
        {
            Logger.WriteLine("[startup-recovery] 后端 60s 内未连接 ⇒ 跳过启动恢复（不重试）");
            return;
        }
        Logger.WriteLine("[startup-recovery] 后端已连接 ⇒ 开始启动恢复");

        Try("BatteryLimitRestore.RunOnce", () =>
        {
            // 宝龙达：正常启动时的限充核对（旧 Program.ScheduleIdleBackendWarmup 路径）；
            // 非宝龙达在 BatteryLimitPlatform 内部 0 读 0 写直接返回。
            BatteryLimitRestore.RunOnce("WPF 启动恢复");
        });

        Try("RunDeferredFanStartupCleanup", () =>
        {
            // 旧 Program 启动 deferred 段的同一调用：宝龙达首次初始化风扇闸门/交回固件。
            hw.RunDeferredFanStartupCleanup();
        });

        Try("RuntimeCpuTuning.ApplySavedOnStartup", () =>
        {
            // 旧 Program.ScheduleCpuCoAutoApply 的启动入口；内部有 clean-exit/开关/能力门控。
            RuntimeCpuTuning.ApplySavedOnStartup();
        });

        Try("WujieFanCurveAutoApply.Schedule", () =>
        {
            // 无界 APU 自定义档开机自动曲线（内部等 4s 并判 WujieSmuApu/自定义档）。
            WujieFanCurveAutoApply.Schedule();
        });

        Logger.WriteLine("[startup-recovery] 启动恢复调度结束");
    }

    private static async Task<MechrevoHw?> WaitForConnectedHwAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            MechrevoHw? hw = WpfBackend.Hw;
            if (hw != null && hw.IsConnected)
            {
                return hw;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return null;
    }

    private static void Try(string name, Action action)
    {
        try
        {
            action();
            Logger.WriteLine("[startup-recovery] " + name + " ⇒ 已执行（成功/由内部判据跳过）");
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[startup-recovery] " + name + " 失败（已吞掉，不重试）："
                + ex.GetType().Name + " " + ex.Message);
        }
    }
}
