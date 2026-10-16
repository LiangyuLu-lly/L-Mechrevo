# ASCII-only (Windows PowerShell reads .ps1 as ANSI without a BOM).
# Restart L-Mechrevo (elevated context needed because LM runs as admin).
$ErrorActionPreference = 'Continue'
$exe = 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'
$log = Join-Path $env:APPDATA 'MechrevoLite\log.txt'
$before = 0
if (Test-Path -LiteralPath $log) { $before = (Get-Item -LiteralPath $log).Length }
Set-Content -LiteralPath 'D:\openLM\tools\_lm_restart_size.txt' -Value $before -Encoding ASCII

Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
Start-Process -FilePath $exe
Start-Sleep -Seconds 12

$after = 0
if (Test-Path -LiteralPath $log) { $after = (Get-Item -LiteralPath $log).Length }
Set-Content -LiteralPath 'D:\openLM\tools\_lm_restart_size_after.txt' -Value $after -Encoding ASCII
