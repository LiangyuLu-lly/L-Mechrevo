using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

/// <summary>
/// UMI / Intel NUC X15 灯带：走 \\.\ACPIDriver 的 ACPI H2RAM 地址。
/// 地址与控制位沿用 30 系 NUC 控制台的灯带实现。
/// </summary>
internal static class UmiLightbar
{
    private const string DevicePath = "\\\\.\\ACPIDriver";
    private const uint IoctlEcRead = 2621482120u;
    private const uint IoctlEcWrite = 2621482124u;

    private const ushort AcCtrl = 1864;   // 0x748
    private const ushort AcR = 1865;      // 0x749
    private const ushort AcG = 1866;      // 0x74A
    private const ushort AcB = 1867;      // 0x74B
    private const ushort DcCtrl = 2018;   // 0x7E2
    private const ushort DcR = 2019;      // 0x7E3
    private const ushort DcG = 2020;      // 0x7E4
    private const ushort DcB = 2021;      // 0x7E5

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(nint handle, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    internal static bool TryReadStatus(out bool on, out int red, out int green, out int blue)
    {
        on = false;
        red = green = blue = -1;
        nint handle = Open();
        if (handle == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            byte ctrl = (byte)Math.Max(0, Read(handle, AcCtrl));
            red = Read(handle, AcR) / 2;
            green = Read(handle, AcG) / 2;
            blue = Read(handle, AcB) / 2;
            on = (ctrl & 1) == 1;
            return ctrl >= 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static bool SetPower(bool on)
    {
        nint handle = Open();
        if (handle == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            byte ac = (byte)Math.Max(0, Read(handle, AcCtrl));
            byte dc = (byte)Math.Max(0, Read(handle, DcCtrl));
            ac = (byte)((ac & 0xFA) | (on ? 1 : 0));
            dc = (byte)((dc & 0xFA) | (on ? 1 : 0));
            bool okAc = Write(handle, AcCtrl, ac);
            bool okDc = Write(handle, DcCtrl, dc);
            Logger.WriteLine("[umi-lightbar] 电源=" + (on ? "开" : "关") + " · AC=" + okAc + " DC=" + okDc);
            return okAc || okDc;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static bool SetEffect(string effect, System.Drawing.Color? color, int lightLevel)
    {
        nint handle = Open();
        if (handle == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            bool on = true;
            byte ac = (byte)Math.Max(0, Read(handle, AcCtrl));
            byte dc = (byte)Math.Max(0, Read(handle, DcCtrl));
            byte effectBits = effect == "Single" ? (byte)0 : (byte)4;
            ac = (byte)((ac & 0x89) | effectBits | 1);
            dc = (byte)((dc & 0x89) | effectBits | 1);
            Write(handle, AcCtrl, ac);
            Write(handle, DcCtrl, dc);

            if (color.HasValue)
            {
                int scale = Math.Clamp(lightLevel, 1, 4);
                int r = Math.Clamp(color.Value.R * scale / 4 * 2, 0, 255);
                int g = Math.Clamp(color.Value.G * scale / 4 * 2, 0, 255);
                int b = Math.Clamp(color.Value.B * scale / 4 * 2, 0, 255);
                Write(handle, AcR, (byte)r);
                Write(handle, AcG, (byte)g);
                Write(handle, AcB, (byte)b);
                Write(handle, DcR, (byte)r);
                Write(handle, DcG, (byte)g);
                Write(handle, DcB, (byte)b);
            }
            Logger.WriteLine("[umi-lightbar] 灯效=" + effect + " · on=" + on + " · 亮度档=" + lightLevel);
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static nint Open()
    {
        nint handle = CreateFile(DevicePath, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        return handle == new nint(-1) ? IntPtr.Zero : handle;
    }

    private static int Read(nint handle, int address)
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

    private static bool Write(nint handle, int address, byte value)
    {
        nint inBuffer = Marshal.AllocHGlobal(8);
        nint outBuffer = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(inBuffer, 0, address);
            Marshal.WriteByte(inBuffer, 4, value);
            return DeviceIoControl(handle, IoctlEcWrite, inBuffer, 5, outBuffer, 16, out int returned, IntPtr.Zero) && returned > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }
}
