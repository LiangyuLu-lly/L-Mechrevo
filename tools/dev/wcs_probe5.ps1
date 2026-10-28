# 实验3：WCS SYSTEM_WIDE scope 关联，看 HKLM 是否创建 ProfileAssociations
Add-Type -Path 'D:\openLM\tools\wcs_probe.cs'

$icc = 'C:\Program Files\L-Mechrevo\customICC\BOE0CBE_B025AA78094A.icm'
$spoolIcc = 'C:\Windows\System32\spool\drivers\color\BOE0CBE_B025AA78094A.icm'

Write-Host '=== WCS SYSTEM_WIDE + \\.\DISPLAY1 ==='
$r1 = [WcsTest]::WcsAssociateColorProfileWithDevice(0, $spoolIcc, '\\.\DISPLAY1')
Write-Host ("ret=" + $r1 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())

Start-Sleep -Seconds 1
Write-Host '=== HKLM ICM 全结构 ==='
$hklmIcm = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM'
if (Test-Path $hklmIcm) { Get-ChildItem $hklmIcm -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
  Write-Host ('  ' + $_.Name)
  (Get-Item $_.PSPath).Property | ForEach-Object { Write-Host ('    ' + $_ + ' = [' + (Get-ItemPropertyValue -Path $_.PSPath -Name $_) + ']') }
} }

Write-Host '=== 文件系统候选 ==='
@('C:\Windows\System32\spool\drivers\color','%LOCALAPPDATA%\Microsoft\Windows\Color','%APPDATA%\Microsoft\Windows\Color') | ForEach-Object {
  $p = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($_)
  if (Test-Path $p) { Write-Host ('  ' + $p); Get-ChildItem $p -ErrorAction SilentlyContinue | Select-Object -First 10 -ExpandProperty Name } else { Write-Host ('  MISS ' + $p) }
}

Write-Host '=== cleanup ==='
[WcsTest]::WcsDisassociateColorProfileFromDevice(0, $spoolIcc, '\\.\DISPLAY1') | Out-Null
Write-Host 'done'
