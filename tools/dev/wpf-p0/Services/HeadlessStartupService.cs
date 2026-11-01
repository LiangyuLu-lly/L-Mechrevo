// ⭐ task-22：WPF 无界面启动参数兼容层（旧 Program.Main 的 charge / --register-autostart 分支）
// 只负责无 UI 的启动任务动作；不创建窗口、不改变正常启动流程。
using System;
using System.Security.Principal;
using MechrevoLite;
using MechrevoLite.Battery;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

internal static class HeadlessStartupService
{
    /// <summary>旧 <c>Program.Main</c> 的 <c>charge</c> 分支：开机/内核启动计划任务恢复充电上限。</summary>
    internal static bool RunChargeTask()
    {
        try
        {
            // 宝龙达：先走既有启动核对（非宝龙达会在内部 0 读 0 写直接返回）
            if (BatteryLimitRestore.HasSavedConfig())
            {
                BatteryLimitRestore.RunOnce("charge-task");
            }

            // 非宝龙达/EC 直写：与旧 ApplyBatteryLimitAtBootAsync 逐条同口径
            if (!BatteryControl.ReestablishArmed())
            {
                Logger.WriteLine("[charge-task] EC 限充开关与无极限制充电滑块都关 ⇒ 本次不写 EC，退出 0");
                return true;
            }

            int limit = AppConfig.Get("charge_limit", 100);
            if (!EcChargeLimit.IsSupportedLimit(limit))
            {
                Logger.WriteLine("[charge-task] charge_limit=" + limit + " 超出 40-100，退出 1");
                return false;
            }
            if (!EcChargeLimit.IsAvailableOnThisMachine())
            {
                Logger.WriteLine("[charge-task] 本机 EC 限充通道不可用，退出 1");
                return false;
            }
            if (!EcChargeLimit.TrySet(limit, out int appliedPercent))
            {
                Logger.WriteLine("[charge-task] 写入未确认 request=" + limit + "% ecReadback=" + EcChargeLimit.ReadPercent() + "%");
                return false;
            }
            Logger.WriteLine("[charge-task] 已恢复 request=" + limit + "% applied=" + appliedPercent
                + "% ec=0x" + EcChargeLimit.UpperRegister.ToString("X3"));
            AppConfig.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[charge-task] 失败（不重试）：" + ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    /// <summary>旧 <c>--register-autostart</c> 分支：提权后只注册用户启动任务，不打开 UI。</summary>
    internal static int RunRegisterAutostart()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Logger.WriteLine("[autostart] 注册需要管理员权限，退出 5");
                return 5;
            }
            bool ok = global::Startup.ScheduleUserTaskOnly();
            Logger.WriteLine("[autostart] 注册结果=" + ok);
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[autostart] 注册失败：" + ex.GetType().Name + " " + ex.Message);
            return 1;
        }
    }

    /// <summary>识别需要无界面处理的旧启动参数；返回 true 时调用方应设置 ExitCode 并退出。</summary>
    internal static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        string action = args.Length > 0 ? (args[0] ?? string.Empty).Trim() : string.Empty;
        if (string.Equals(action, "charge", StringComparison.OrdinalIgnoreCase))
        {
            exitCode = RunChargeTask() ? 0 : 1;
            return true;
        }
        if (string.Equals(action, "--register-autostart", StringComparison.OrdinalIgnoreCase))
        {
            exitCode = RunRegisterAutostart();
            return true;
        }
        return false;
    }
}
