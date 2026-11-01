# 创建用户配置文件存储目录并放入 ICC（Win11 颜色管理页数据源验证）
$colorDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Color'
if (-not (Test-Path $colorDir)) {
  New-Item -ItemType Directory -Path $colorDir -Force | Out-Null
  Write-Host ('created: ' + $colorDir)
} else { Write-Host ('exists: ' + $colorDir) }

$src = 'C:\Program Files\L-Mechrevo\customICC\BOE0CBE_B025AA78094A.icm'
$dst = Join-Path $colorDir 'BOE0CBE_B025AA78094A.icm'
Copy-Item $src $dst -Force
Write-Host ('copied: ' + $dst)

Write-Host '=== result ==='
Get-ChildItem $colorDir | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize
Write-Host 'done - please refresh color management page'
