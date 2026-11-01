# 验证：Win11 新颜色管理数据源位置
Write-Host '=== 1. 用户配置文件存储 ==='
$colorDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Color'
if (Test-Path $colorDir) { Write-Host ('  EXISTS: ' + $colorDir); Get-ChildItem $colorDir | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize } else { Write-Host ('  MISS: ' + $colorDir) }

Write-Host '=== 2. 系统级显示器类设备槽 ==='
$cls = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96e-e325-11ce-bfc1-08002be10318}'
if (Test-Path $cls) {
  Get-ChildItem $cls | ForEach-Object {
    $k = $_.PSChildName
    $p = $_.PSPath
    $vals = @()
    if ((Get-Item $p).Property -contains 'ICMProfile') { $vals += ('ICMProfile=[' + (Get-ItemPropertyValue -Path $p -Name 'ICMProfile') + ']') }
    if ((Get-Item $p).Property -contains 'MDP') { $vals += ('MDP=[' + (Get-ItemPropertyValue -Path $p -Name 'MDP') + ']') }
    Write-Host ('  ' + $k + ' -> ' + ($vals -join ' '))
  }
} else { Write-Host ('  MISS: ' + $cls) }

Write-Host '=== 3. spool 颜色目录内容 ==='
Get-ChildItem 'C:\Windows\System32\spool\drivers\color' -ErrorAction SilentlyContinue | Select-Object -First 15 -ExpandProperty Name

Write-Host '=== 4. 用户级 RegisteredProfiles（当前） ==='
$rp = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles'
if (Test-Path $rp) { (Get-Item $rp).Property | ForEach-Object { Write-Host ('  ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp -Name $_)) } }
