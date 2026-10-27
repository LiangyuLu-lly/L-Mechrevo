using System;
using System.Runtime.InteropServices;
using MechrevoLite.Hardware;

namespace MechrevoLite.Hardware;

/// <summary>
/// UMI / Intel NUC X15 风扇转速读取：走 \\.\ACPIDriver 的 ACPI H2RAM 地址。
/// 与 EC 0x62/0x66 的 0xC834/0xC835 不是同一地址空间。
/// </summary>
internal static class UmiFanReader
{
    private const string DevicePath = "\\\\.\\ACPIDriver";
    private const uint IoctlEcRead = 2621482120u; // IOCTL_GPD_ACPI_ECREAD
    private const ushort MainFanHigh = 1124; // 0x464
    private const ushort MainFanLow = 1125;  // 0x465
    private const ushort SecondFanHigh = 1132; // 0x46C
    private const ushort SecondFanLow = 1133;  // 0x46D
    private static long _lastLogTick;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(nint handle, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    internal static bool TryReadFanRpm(out int mainFanRpm, out int secondFanRpm)
    {
        mainFanRpm = -1;
        secondFanRpm = -1;
        nint handle = CreateFile(DevicePath, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new nint(-1))
        {
            return false;
        }
        try
        {
            bool mainOk = TryReadWord(handle, MainFanHigh, MainFanLow, out mainFanRpm);
            bool secondOk = TryReadWord(handle, SecondFanHigh, SecondFanLow, out secondFanRpm);
            if (mainOk || secondOk)
            {
                if (Environment.TickCount64 - _lastLogTick > 10000)
                {
                    _lastLogTick = Environment.TickCount64;
                    Logger.WriteLine("[umi-fan] ACPI H2RAM 读取：主扇=" + (mainOk ? mainFanRpm + "rpm" : "读不到")
                        + " · 副扇=" + (secondOk ? secondFanRpm + "rpm" : "读不到"));
                }
            }
            return mainOk || secondOk;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool TryReadWord(nint handle, ushort highAddress, ushort lowAddress, out int value)
    {
        value = -1;
        int high = ReadByte(handle, highAddress);
        int low = ReadByte(handle, lowAddress);
        if (high < 0 || low < 0)
        {
            return false;
        }
        value = (high << 8) | low;
        return true;
    }

    private static int ReadByte(nint handle, int address)
    {
        nint inBuffer = Marshal.AllocHGlobal(4);
        nint outBuffer = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(inBuffer, 0, address);
            return (DeviceIoControl(handle, IoctlEcRead, inBuffer, 4, outBuffer, 16, out int returned, IntPtr.Zero) && returned > 0)
                ? Marshal.ReadByte(outBuffer)
                : -1;
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }
}
