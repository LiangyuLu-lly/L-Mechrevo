$ErrorActionPreference = 'Continue'
$log = 'D:\openLM\replace_log.txt'
function Log($m) { Add-Content -Path $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) }
Log '=== elevated kill+restart ==='
taskkill /IM L-Mechrevo.exe /F /T 2>$null | Out-Null
Start-Sleep -Seconds 3
Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$p = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($p) { Log ('STILL ALIVE: ' + ($p.Id -join ',')) } else { Log 'killed ok' }
Start-Process 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'
Start-Sleep -Seconds 2
Log 'LM restarted'
$q = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($q) { Log ('running pid: ' + ($q.Id -join ',')) } else { Log 'LM NOT RUNNING' }
Log '=== elevate end ==='
