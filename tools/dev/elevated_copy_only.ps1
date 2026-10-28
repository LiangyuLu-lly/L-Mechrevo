$ErrorActionPreference = 'Continue'
$log = 'D:\openLM\replace_log.txt'
function Log($m) { Add-Content -Path $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) }
Log '=== elevated manual copy (layout-hotsap-delete) ==='
$oldDll = 'C:\Program Files\L-Mechrevo\L-Mechrevo.dll'
$src = 'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll'
try {
    Copy-Item $src $oldDll -Force -ErrorAction Stop
    Log ("copied dll: " + (Get-Item $oldDll).Length + " B, " + (Get-Item $oldDll).LastWriteTime)
} catch {
    Log ("COPY FAILED: " + $_.Exception.Message)
}
try {
    Start-Process 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe' -ErrorAction Stop
} catch {
    Log ("START FAILED: " + $_.Exception.Message)
}
Start-Sleep -Seconds 3
$q = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($q) { Log ('running pid: ' + ($q.Id -join ',')) } else { Log 'LM NOT RUNNING' }
Log '=== elevate end ==='
