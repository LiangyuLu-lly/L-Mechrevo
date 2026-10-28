# 实验2：正确签名 InstallColorProfile(NULL,...) + Associate(机器名, profile, 设备)
Add-Type -Path 'D:\openLM\tools\wcs_probe.cs'

$icc = 'C:\Program Files\L-Mechrevo\customICC\BOE0CBE_B025AA78094A.icm'
$profileName = 'BOE0CBE_B025AA78094A.icm'

Write-Host '=== 0. 前置状态 ==='
$rp = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles'
if (Test-Path $rp) { (Get-Item $rp).Property | ForEach-Object { Write-Host ('  RP: ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp -Name $_)) } }
$sd = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\DISPLAY'
if (Test-Path $sd) { Write-Host ('  DISPLAY.ICMProfile=[' + (Get-ItemPropertyValue -Path $sd -Name 'ICMProfile') + ']') }

Write-Host '=== 1. InstallColorProfile(NULL, path) ==='
$r1 = [WcsTest]::InstallColorProfile($null, $icc)
Write-Host ("ret=" + $r1 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host '=== 2. Associate(\\.\DISPLAY1) ==='
$r2 = [WcsTest]::AssociateColorProfileWithDevice($null, $profileName, '\\.\DISPLAY1')
Write-Host ("ret=" + $r2 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host '=== 3. Associate(monitorPath) ==='
$r3 = [WcsTest]::AssociateColorProfileWithDevice($null, $profileName, '\\?\DISPLAY#BOE0CBE#5&1551b5bc&1a&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}')
Write-Host ("ret=" + $r3 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Start-Sleep -Seconds 1
Write-Host '=== 结果状态 ==='
$rp2 = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles'
if (Test-Path $rp2) { (Get-Item $rp2).Property | ForEach-Object { Write-Host ('  RP: ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp2 -Name $_)) } }
$sd2 = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\DISPLAY'
if (Test-Path $sd2) { Write-Host ('  DISPLAY.ICMProfile=[' + (Get-ItemPropertyValue -Path $sd2 -Name 'ICMProfile') + ']') }
$spool = 'C:\Windows\System32\spool\drivers\color\BOE0CBE_B025AA78094A.icm'
Write-Host ('  spool 文件存在: ' + (Test-Path $spool))

Write-Host '=== 清理（仅解除 DISPLAY1/monitor 关联，保留注册） ==='
[WcsTest]::DisassociateColorProfileFromDevice($null, $profileName, '\\.\DISPLAY1') | Out-Null
[WcsTest]::DisassociateColorProfileFromDevice($null, $profileName, '\\?\DISPLAY#BOE0CBE#5&1551b5bc&1a&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}') | Out-Null
Write-Host 'done'
