$ErrorActionPreference = 'Continue'
$log = 'D:\openLM\replace_log.txt'
function Log($m) { Add-Content -Path $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) }
Log '=== elevated kill+copy (with zh-CN satellite) ==='
taskkill /IM L-Mechrevo.exe /F /T 2>$null | Out-Null
Start-Sleep -Seconds 3
Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$inst = 'C:\Program Files\L-Mechrevo'
$srcDir = 'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0'
try {
    Copy-Item "$srcDir\L-Mechrevo.dll" "$inst\L-Mechrevo.dll" -Force -ErrorAction Stop
    Log ("main dll: " + (Get-Item "$inst\L-Mechrevo.dll").LastWriteTime)
} catch {
    Log ("MAIN COPY FAILED: " + $_.Exception.Message)
}
try {
    New-Item -ItemType Directory -Path "$inst\zh-CN" -Force | Out-Null
    Copy-Item "$srcDir\zh-CN\L-Mechrevo.resources.dll" "$inst\zh-CN\L-Mechrevo.resources.dll" -Force -ErrorAction Stop
    Log ("zh-CN satellite: " + (Get-Item "$inst\zh-CN\L-Mechrevo.resources.dll").LastWriteTime)
} catch {
    Log ("SATELLITE COPY FAILED: " + $_.Exception.Message)
}
try {
    Start-Process "$inst\L-Mechrevo.exe" -ErrorAction Stop
} catch {
    Log ("START FAILED: " + $_.Exception.Message)
}
Start-Sleep -Seconds 3
$q = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
if ($q) { Log ('running pid: ' + ($q.Id -join ',')) } else { Log 'LM NOT RUNNING' }
Log '=== elevate end ==='
