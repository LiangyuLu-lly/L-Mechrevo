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
    # WUJIE 风扇 I2EC、XINGYAO P916F PMC2 电池限充、宝龙达 MRID6 EC 后备都需要 inpoutx64。
    if (($model -notmatch "WUJIE|XINGYAO|P916F|MRID6|JIAOLONG|蛟龙") -and ($board -notmatch "WUJIE|XINGYAO|P916F|MRID6|JIAOLONG|蛟龙")) {
        Log "not a WUJIE/XINGYAO model - skip inpoutx64 driver"
        exit 0
    }
    $svc = Get-Service inpoutx64 -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Log "inpoutx64 service already running - skip"
        exit 0
    }
    # sys 既可能在安装目录 {app}\inpoutx64\，也可能被单独拷到脚本旁；
    # 支持从更新目录直接运行本脚本（不用先复制到 Program Files）。
    $candidates = @(
        (Join-Path $PSScriptRoot "inpoutx64\inpoutx64.sys"),
        (Join-Path $PSScriptRoot "inpoutx64.sys"),
        (Join-Path $env:ProgramFiles "L-Mechrevo\inpoutx64\inpoutx64.sys"),
        (Join-Path $env:ProgramFiles "L-Mechrevo\inpoutx64.sys")
    )
    $sysSrc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $sysSrc) {
        Log ("inpoutx64.sys missing - checked: " + ($candidates -join " | "))
        exit 1
    }
    Log ("driver source: " + $sysSrc)
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