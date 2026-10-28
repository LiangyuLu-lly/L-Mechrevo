$ErrorActionPreference = 'Continue'
$log = 'D:\openLM\replace_log.txt'
function Log($m) { Add-Content -Path $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) }
Log '=== elevated hotswap-delete replace ==='
# 1. 杀 LM
taskkill /IM L-Mechrevo.exe /F /T 2>$null | Out-Null
Start-Sleep -Seconds 3
Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$p = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($p) { Log ('STILL ALIVE: ' + ($p.Id -join ',')) } else { Log 'killed ok' }
# 2. 备份旧 DLL
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$oldDll = 'C:\Program Files\L-Mechrevo\L-Mechrevo.dll'
$bak = "D:\openLM\backup\L-Mechrevo_$stamp.dll"
Copy-Item $oldDll $bak -Force
Log "backup -> $bak ($((Get-Item $bak).Length) B)"
# 3. 替换（先改名旧文件绕占用，再复制新的）
$renamed = 'C:\Program Files\L-Mechrevo\L-Mechrevo.dll.old'
if (Test-Path $renamed) { Remove-Item $renamed -Force }
Move-Item $oldDll $renamed -Force
Copy-Item 'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll' $oldDll -Force
Remove-Item $renamed -Force
$newInfo = Get-Item $oldDll
Log "replaced dll: $($newInfo.Length) B, $($newInfo.LastWriteTime)"
# 4. 重启
Start-Process 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'
Start-Sleep -Seconds 2
$q = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($q) { Log ('running pid: ' + ($q.Id -join ',')) } else { Log 'LM NOT RUNNING' }
Log '=== elevate end ==='
