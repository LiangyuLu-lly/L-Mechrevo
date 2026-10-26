using System;
using Microsoft.Win32;
using MechrevoLite;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 已知会让 NVML 原生调用 0xc0000005 的机型：在 WPF 进程内永久停用 NVML，
/// GPU 温度/占用率/频率走 GCU，功耗/显存频率走 nvidia-smi 子进程。
/// </summary>
internal static class NvmlSafetyPolicy
{
    internal static void Apply()
    {
        try
        {
            string model = ReadBiosValue("SystemProductName");
            string board = ReadBiosValue("BaseBoardProduct");
            if (model.Contains("JIGUANG", StringComparison.OrdinalIgnoreCase)
                || board.Contains("JIGUANG", StringComparison.OrdinalIgnoreCase))
            {
                NvmlHelper.DisableNativeForProcess("JIGUANG NVML 0xc0000005 防护：进程内不再调用 NVML");
                Logger.WriteLine("[nvml] JIGUANG 机型已在 WPF 进程内停用 NVML；GPU 功耗/显存频率改用 nvidia-smi 子进程，温度/占用率/频率走 GCU");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[nvml] 机型安全策略检查失败（保持原行为）：" + ex.Message);
        }
    }

    private static string ReadBiosValue(string name)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return key?.GetValue(name) as string ?? "";
    }
}
