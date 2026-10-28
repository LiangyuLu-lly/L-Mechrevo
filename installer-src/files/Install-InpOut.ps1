# 2026-10-22: install inpoutx64 driver for WUJIE fan I2EC access.
# Runs elevated from the installer. Idempotent; exits 0 when not WUJIE or already running.
$ErrorActionPreference = "Stop"
$logDir = Join-Path $env:ProgramData "L-Mechrevo\logs"
$logFile = Join-Path $logDir "inpout-install.log"
function Log([string]$msg) {
    try {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        Add-Content -Path $logFile -Value ("[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg) -Encoding UTF8
    } catch { }
}
try {
    $model = ""
    $board = ""
    try { $model = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).Model } catch { }
    try { $board = (Get-CimInstance Win32_BaseBoard -ErrorAction Stop).Product } catch { }
    Log ("machine check: model={0} board={1}" -f $model, $board)
    if (($model -notmatch "WUJIE") -and ($board -notmatch "WUJIE")) {
        Log "not a WUJIE model - skip inpoutx64 driver"
        exit 0
    }
    $svc = Get-Service inpoutx64 -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Log "inpoutx64 service already running - skip"
        exit 0
    }
    $sysSrc = Join-Path $PSScriptRoot "inpoutx64.sys"
    if (-not (Test-Path $sysSrc)) {
        Log "inpoutx64.sys missing - skip"
        exit 1
    }
    $sysDir = Join-Path $env:SystemRoot "System32\drivers"
    $target = Join-Path $sysDir "inpoutx64.sys"
    Copy-Item $sysSrc $target -Force
    Log ("driver copied: {0}" -f $target)
    if ($svc) {
        & sc.exe config inpoutx64 binPath= $target | Out-Null
    } else {
        & sc.exe create inpoutx64 type= kernel start= auto binPath= $target | Out-Null
    }
    & sc.exe start inpoutx64 | Out-Null
    Start-Sleep -Milliseconds 500
    $after = Get-Service inpoutx64 -ErrorAction SilentlyContinue
    Log ("service status after install: {0}" -f ($(if ($after) { $after.Status } else { "missing" })))
    exit 0
} catch {
    Log ("install error: " + $_.Exception.Message)
    exit 1
}