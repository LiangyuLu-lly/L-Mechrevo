using System;
using System.Runtime.InteropServices;

public static class WcsTest2
{
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InstallColorProfile(string pMachineName, string pProfileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssociateColorProfileWithDevice(string pMachineName, string pProfileName, string pDeviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DisassociateColorProfileFromDevice(string pMachineName, string pProfileName, string pDeviceName);
}
