# 系统级颜色配置注册 + 槽位写入（需管理员）
$ErrorActionPreference = 'Continue'

Write-Host '=== 1. 注册两个 ICC 到 HKLM RegisteredProfiles ==='
$regKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles'
$spool = 'C:\Windows\System32\spool\drivers\color'
foreach ($name in @('BOE0CBE_B025AA78094A.icm','BOE0CBE_B025AA781188.icm')) {
  $file = Join-Path $spool $name
  if (Test-Path $file) {
    New-ItemProperty -Path $regKey -Name $name -PropertyType String -Value $file -Force | Out-Null
    Write-Host ('  registered: ' + $name + ' = ' + $file)
  } else { Write-Host ('  FILE MISS: ' + $file) }
}

Write-Host '=== 2. 系统级显示器槽位写入我们的 ICC（全槽） ==='
$cls = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96e-e325-11ce-bfc1-08002be10318}'
Get-ChildItem $cls -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^000\d$' } | ForEach-Object {
  New-ItemProperty -Path $_.PSPath -Name 'ICMProfile' -PropertyType MultiString -Value @('BOE0CBE_B025AA78094A.icm') -Force | Out-Null
  Write-Host ('  slot ' + $_.PSChildName + ' <- BOE0CBE_B025AA78094A.icm')
}

Write-Host '=== 3. 结果 ==='
Write-Host '-- HKLM RegisteredProfiles --'
(Get-Item $regKey).Property | ForEach-Object { Write-Host ('  ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $regKey -Name $_)) }
Write-Host '-- 槽位 --'
Get-ChildItem $cls -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^000\d$' } | ForEach-Object {
  $v = ''
  if ((Get-Item $_.PSPath).Property -contains 'ICMProfile') { $v = [string](Get-ItemPropertyValue -Path $_.PSPath -Name 'ICMProfile') }
  Write-Host ('  ' + $_.PSChildName + ' = [' + $v + ']')
}
Write-Host 'done'
