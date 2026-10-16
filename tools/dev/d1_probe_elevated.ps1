# 2026-10-06 D-1 acceptance: --cpu-tuning-probe regression (read-only getters, elevated).
# ASCII only on purpose (powershell 5.1 reads BOM-less files as ANSI).
$ErrorActionPreference = 'Continue'
$dir = 'D:\openLM\diagnostics\d1-20261006-igpuhint'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$cfg = Join-Path $dir 'tmp-cfg-probe.json'
Set-Content -Path $cfg -Value '{"performance_mode":0}' -Encoding UTF8
$env:LMECHREVO_CONFIG_FILE = $cfg
$env:LMECHREVO_LOG_DIR = (Join-Path $dir 'logs-probe')
$env:LMECHREVO_LOG_LEVEL = 'All'
New-Item -ItemType Directory -Force -Path $env:LMECHREVO_LOG_DIR | Out-Null
$json = Join-Path $dir 'cpu-tuning-probe-d1.json'
Remove-Item $json -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe' -ArgumentList @('--cpu-tuning-probe', $json) -Wait -PassThru
"exit=$($p.ExitCode) jsonExists=$(Test-Path $json)" | Out-File -Encoding UTF8 (Join-Path $dir 'probe-exit.txt')
'=== done ===' | Out-File -Encoding UTF8 -Append (Join-Path $dir 'probe-exit.txt')
