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
        // 现在 NVIDIA 遥测已改为独立进程 nvidia-smi，不再在 WPF 进程内加载 nvml.dll。
        // 因此不需要再按机型/显示拓扑停用 NVIDIA 遥测。
    }

    private static string ReadBiosValue(string name)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return key?.GetValue(name) as string ?? "";
    }
}
