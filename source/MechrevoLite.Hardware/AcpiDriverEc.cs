using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MechrevoLite.Hardware;

/// <summary>
/// UWACPIDriver 的 EC IOCTL 通道。NUC/UMI 原厂地址通过 \\.\ACPIDriver 读写，
/// 比 LPC mailbox 更接近原厂控制台的行为。
/// </summary>
internal static class AcpiDriverEc
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWrite = 3;
    private const uint IoctlEcRead = 0x9C40A488;
    private const uint IoctlEcWrite = 0x9C40A48C;
    private const string DevicePath = "\\\\.\\ACPIDriver";
    private static readonly object Gate = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] inBuffer, uint inSize, byte[] outBuffer, uint outSize, out uint bytesReturned, IntPtr overlapped);

    internal static bool TryReadByte(int address, out byte value)
    {
        value = 0;
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }
        lock (Gate)
        {
            using SafeFileHandle handle = CreateFileW(DevicePath, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return false;
            }
            byte[] input = BitConverter.GetBytes((uint)address);
            byte[] output = new byte[4];
            if (!DeviceIoControl(handle, IoctlEcRead, input, 4, output, 4, out _, IntPtr.Zero))
            {
                return false;
            }
            value = (byte)BitConverter.ToUInt32(output, 0);
            return true;
        }
    }

    internal static bool TryWriteByte(int address, int value)
    {
        if (address < 0 || address > ushort.MaxValue || value < 0 || value > 255)
        {
            return false;
        }
        lock (Gate)
        {
            using SafeFileHandle handle = CreateFileW(DevicePath, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return false;
            }
            byte[] input = new byte[8];
            BitConverter.GetBytes((uint)address).CopyTo(input, 0);
            BitConverter.GetBytes((uint)value).CopyTo(input, 4);
            byte[] output = new byte[4];
            return DeviceIoControl(handle, IoctlEcWrite, input, 8, output, 4, out _, IntPtr.Zero);
        }
    }
}
