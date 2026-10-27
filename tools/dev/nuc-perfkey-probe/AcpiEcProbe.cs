using System;
using System.Runtime.InteropServices;

public static class AcpiEcProbe
{
    const string Path = "\\\\.\\ACPIDriver";
    const uint IoctlRead = 2621482120u;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    public static int Read(int address)
    {
        IntPtr h = CreateFile(Path, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        if (h == new IntPtr(-1)) return -1;
        IntPtr inBuf = Marshal.AllocHGlobal(4);
        IntPtr outBuf = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(inBuf, 0, address);
            int returned;
            if (!DeviceIoControl(h, IoctlRead, inBuf, 4, outBuf, 16, out returned, IntPtr.Zero) || returned <= 0) return -1;
            return Marshal.ReadByte(outBuf);
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
            CloseHandle(h);
        }
    }
}
