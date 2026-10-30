using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

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
            // 原厂控制位：bit2=关闭；bit0=灯带使能。
            on = (ctrl & 0x04) == 0 && (ctrl & 0x01) != 0;
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
            // 原厂灯带电源：读改写 1864/2018，保留其它位，只动 bit0/bit2。
            // 开 = (ctrl & 0xFA) | 1；关 = (ctrl & 0xFA) | 5。
            // （旧实现直接写 0/8 会覆盖固件当前模式位，导致“关不掉”。）
            int acOld = Read(handle, AcCtrl);
            int dcOld = Read(handle, DcCtrl);
            byte ac = (byte)(((acOld < 0 ? 0 : acOld) & 0xFA) | (on ? 0x01 : 0x05));
            byte dc = (byte)(((dcOld < 0 ? 0 : dcOld) & 0xFA) | (on ? 0x01 : 0x05));
            bool okAc = Write(handle, AcCtrl, ac);
            bool okDc = Write(handle, DcCtrl, dc);
            int readAc = Read(handle, AcCtrl);
            int readDc = Read(handle, DcCtrl);
            Logger.WriteError("[umi-lightbar] 电源=" + (on ? "开" : "关") + " · AC=" + okAc + " DC=" + okDc
                + " · 回读AC=0x" + (readAc < 0 ? "??" : readAc.ToString("X2"))
                + " DC=0x" + (readDc < 0 ? "??" : readDc.ToString("X2")));
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
            int acOld = Read(handle, AcCtrl);
            int dcOld = Read(handle, DcCtrl);
            bool colorful = effect is "Colorful" or "Rainbow" or "ColorMarquee" or "Welcome" or "ColorfulMode" or "Mix";
            bool breathing = string.Equals(effect, "Breathing", StringComparison.OrdinalIgnoreCase);
            bool off = string.Equals(effect, "Off", StringComparison.OrdinalIgnoreCase);
            // 原厂 ApplySetting/ApplyOnOff 的控制字节语义：
            //   保留 0x72 的模式位；单色=bit0，呼吸=bit3，彩色/循环=bit7+bit3；
            //   关闭=bit2+bit0（0x05）。不再整字节覆盖。
            byte ctrlFor(int old)
            {
                if (off) return (byte)((old & 0xFA) | 0x05);
                if (colorful) return (byte)((old & 0x72) | 0x89);
                if (breathing) return (byte)((old & 0x72) | 0x09);
                return (byte)((old & 0x72) | 0x01);
            }
            byte ac = ctrlFor(acOld);
            byte dc = ctrlFor(dcOld);
            Write(handle, AcCtrl, ac);
            Write(handle, DcCtrl, dc);

            if (color.HasValue)
            {
                // 控制台的 R/G/B_level 是 0..100，固件再 *2；UI 的 ARGB 是 0..255。
                // 旧写法把 0..255 再乘亮度，实际写入严重偏色/偏亮，导致“设置和显示不一致”。
                float factor = lightLevel switch
                {
                    <= 0 => 0f,
                    <= 4 => lightLevel / 4f,
                    <= 100 => lightLevel / 100f,
                    _ => 1f
                };
                int r = Math.Clamp((int)Math.Round(color.Value.R * 100 / 255.0 * 2 * factor), 0, 255);
                int g = Math.Clamp((int)Math.Round(color.Value.G * 100 / 255.0 * 2 * factor), 0, 255);
                int b = Math.Clamp((int)Math.Round(color.Value.B * 100 / 255.0 * 2 * factor), 0, 255);
                Write(handle, AcR, (byte)r);
                Write(handle, AcG, (byte)g);
                Write(handle, AcB, (byte)b);
                Write(handle, DcR, (byte)r);
                Write(handle, DcG, (byte)g);
                Write(handle, DcB, (byte)b);
            }
            Logger.WriteLine("[umi-lightbar] 灯效=" + effect + " · AC控制=0x" + ac.ToString("X2") + " · DC控制=0x" + dc.ToString("X2") + " · 亮度档=" + lightLevel);
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
