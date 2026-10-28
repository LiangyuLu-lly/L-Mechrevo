using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MechrevoLite.Hardware;

/// <summary>
/// UEFI NVRAM 冷切换显卡模式：OemDisplayMode (offset 98)。
/// 只用于有 MUX 硬件的 UMI/同方机型；NUC 不调用。
/// </summary>
internal static class UefiGpuMux
{
    private const string Guid = "{9F33F85C-13CA-4FD1-9C4A-96217722C593}";
    private const string OemMagic = "OemMagicVariable";
    private const string UniWill = "UniWillVariable";
    private const int DisplayModeOffset = 98;
    private const int BufferSize = 512;

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFirmwareEnvironmentVariable(string name, string guid, byte[] buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetFirmwareEnvironmentVariable(string name, string guid, byte[] buffer, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    internal static bool TryRead(out int raw, out string variable, out string error)
    {
        raw = -1;
        variable = "";
        error = "";
        if (!EnablePrivilege(out error))
        {
            return false;
        }
        foreach (string name in new[] { OemMagic, UniWill })
        {
            byte[] buffer = new byte[BufferSize];
            uint size = GetFirmwareEnvironmentVariable(name, Guid, buffer, BufferSize);
            if (size > DisplayModeOffset)
            {
                raw = buffer[DisplayModeOffset];
                variable = name;
                return true;
            }
        }
        error = "UEFI NVRAM 变量不存在";
        return false;
    }

    /// <summary>visualMode: 1=混合, 2=独显直连。</summary>
    internal static bool TryWriteColdMode(int visualMode, out string detail)
    {
        detail = "";
        if (visualMode != 1 && visualMode != 2)
        {
            detail = "只支持混合/独显直连";
            return false;
        }
        if (!TryRead(out int current, out string variable, out string error))
        {
            detail = error;
            return false;
        }
        int target = CpuVendor.IsAmd
            ? (visualMode == 2 ? 1 : 0)
            : (visualMode == 2 ? 2 : 4);
        if (current == target)
        {
            detail = "NVRAM 已是目标模式";
            return true;
        }
        if (!EnablePrivilege(out error))
        {
            detail = error;
            return false;
        }

        byte[] original = new byte[BufferSize];
        uint length = GetFirmwareEnvironmentVariable(variable, Guid, original, BufferSize);
        if (length <= DisplayModeOffset)
        {
            detail = "NVRAM 内容长度不足";
            return false;
        }
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo", "logs");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "uefi-gpu-nvram-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin"), original.AsSpan(0, (int)length).ToArray());
        }
        catch
        {
        }
        byte[] data = new byte[length];
        Array.Copy(original, data, length);
        data[DisplayModeOffset] = (byte)target;
        if (!SetFirmwareEnvironmentVariable(variable, Guid, data, length))
        {
            detail = "写入 NVRAM 失败 Win32=" + Marshal.GetLastWin32Error();
            return false;
        }
        Thread.Sleep(800);
        if (!TryRead(out int after, out _, out error) || after != target)
        {
            detail = "写入后回读失败：" + error + " raw=" + after;
            return false;
        }
        detail = $"NVRAM {variable} offset98 {current} -> {target} (visual {visualMode})";
        return true;
    }

    private static bool EnablePrivilege(out string error)
    {
        error = "";
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token))
        {
            error = "OpenProcessToken 失败";
            return false;
        }
        try
        {
            if (!LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out Luid luid))
            {
                error = "LookupPrivilegeValue 失败";
                return false;
            }
            TokenPrivileges tp = new()
            {
                Count = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                error = "AdjustTokenPrivileges 失败";
                return false;
            }
            return true;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
