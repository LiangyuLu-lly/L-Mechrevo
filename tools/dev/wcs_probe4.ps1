# Experiment 2: correct signatures
Add-Type -Path 'D:\openLM\tools\wcs_probe2.cs'

$icc = 'C:\Program Files\L-Mechrevo\customICC\BOE0CBE_B025AA78094A.icm'
$profileName = 'BOE0CBE_B025AA78094A.icm'

Write-Host '=== 1. InstallColorProfile(NULL, path) ==='
$r1 = [WcsTest2]::InstallColorProfile($null, $icc)
Write-Host ("ret=" + $r1 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host '=== 2. Associate(NULL, name, \\.\DISPLAY1) ==='
$r2 = [WcsTest2]::AssociateColorProfileWithDevice($null, $profileName, '\\.\DISPLAY1')
Write-Host ("ret=" + $r2 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Write-Host '=== 3. Associate(NULL, name, monitorPath) ==='
$mp = '\\?\DISPLAY#BOE0CBE#5&1551b5bc&1a&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'
$r3 = [WcsTest2]::AssociateColorProfileWithDevice($null, $profileName, $mp)
Write-Host ("ret=" + $r3 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Start-Sleep -Seconds 1
Write-Host '=== result ==='
$rp2 = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles'
if (Test-Path $rp2) { (Get-Item $rp2).Property | ForEach-Object { Write-Host ('  RP: ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp2 -Name $_)) } }
$sd2 = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\DISPLAY'
if (Test-Path $sd2) { Write-Host ('  DISPLAY.ICMProfile=[' + (Get-ItemPropertyValue -Path $sd2 -Name 'ICMProfile') + ']') }
$spool = 'C:\Windows\System32\spool\drivers\color\BOE0CBE_B025AA78094A.icm'
Write-Host ('  spool file exists: ' + (Test-Path $spool))
$base = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\{4d36e96e-e325-11ce-bfc1-08002be10318}'
if (Test-Path $base) { Get-ChildItem $base | ForEach-Object { $icm = ''; if ((Get-Item $_.PSPath).Property -contains 'ICMProfile') { $icm = [string](Get-ItemPropertyValue -Path $_.PSPath -Name 'ICMProfile') }; Write-Host ('  slot ' + $_.PSChildName + ' = [' + $icm + ']') } }

Write-Host '=== cleanup (disassociate both) ==='
[WcsTest2]::DisassociateColorProfileFromDevice($null, $profileName, '\\.\DISPLAY1') | Out-Null
[WcsTest2]::DisassociateColorProfileFromDevice($null, $profileName, $mp) | Out-Null
Write-Host 'done'
