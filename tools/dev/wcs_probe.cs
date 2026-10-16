using System;
using System.Runtime.InteropServices;

public static class WcsTest
{
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WcsAssociateColorProfileWithDevice(uint scope, string profileName, string deviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InstallColorProfile(string deviceName, string profileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssociateColorProfileWithDevice(string deviceName, string profileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DisassociateColorProfileFromDevice(string deviceName, string profileName);
}
