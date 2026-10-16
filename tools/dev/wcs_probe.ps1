# -*- coding: utf-8 -*-
# 实验：验证 WCS/ICM 关联的正确 device 名格式，观察注册表写入
$src = @'
using System;
using System.Runtime.InteropServices;
public static class WcsTest
{
    [DllImport("mscms.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WcsAssociateColorProfileWithDevice(uint scope, string profileName, string deviceName);

    [DllImport("mscms.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InstallColorProfile(string deviceName, string profileName);

    [DllImport("mscms.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssociateColorProfileWithDevice(string deviceName, string profileName);

    [DllImport("mscms.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DisassociateColorProfileFromDevice(string deviceName, string profileName);
}
'@
Add-Type -TypeDefinition $src

$icc = 'C:\Program Files\L-Mechrevo\customICC\BOE0CBE_B025AA78094A.icm'
$monitorPath = '\\?\DISPLAY#BOE0CBE#5&1551b5bc&1a&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'

Write-Host "=== 1. WCS + monitorDevicePath ==="
$r1 = [WcsTest]::WcsAssociateColorProfileWithDevice(1, $icc, $monitorPath)
Write-Host ("ret=" + $r1 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host "=== 2. 旧API Install+Associate + DISPLAY1 ==="
$r2 = [WcsTest]::InstallColorProfile('DISPLAY1', $icc)
Write-Host ("install ret=" + $r2 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
$r3 = [WcsTest]::AssociateColorProfileWithDevice('DISPLAY1', $icc)
Write-Host ("assoc ret=" + $r3 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host "=== 3. 旧API Install+Associate + monitorDevicePath ==="
$r4 = [WcsTest]::InstallColorProfile($monitorPath, $icc)
Write-Host ("install ret=" + $r4 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
$r5 = [WcsTest]::AssociateColorProfileWithDevice($monitorPath, $icc)
Write-Host ("assoc ret=" + $r5 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Start-Sleep -Seconds 1
Write-Host "=== 注册表检查 ==="
$paths = @(
  'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles',
  'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\DeviceAssociation',
  'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles',
  'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\DeviceAssociation'
)
foreach ($p in $paths) {
  if (Test-Path $p) {
    Write-Host ("=== " + $p + " ===")
    Get-ChildItem $p -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
      Write-Host ("  " + $_.Name)
      (Get-Item $_.PSPath).Property | ForEach-Object { Write-Host ("    " + $_ + " = " + (Get-ItemProperty -Path $_.PSPath).$_) }
    }
  } else { Write-Host ("MISS: " + $p) }
}

# 清理实验关联（避免污染）
Write-Host "=== 清理 ==="
[WcsTest]::DisassociateColorProfileFromDevice('DISPLAY1', $icc) | Out-Null
[WcsTest]::DisassociateColorProfileFromDevice($monitorPath, $icc) | Out-Null
Write-Host "cleaned"
