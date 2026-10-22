# 2026-10-22：无界（WUJIE14XA）风扇 I2EC 需要的 inpoutx64 驱动安装脚本。
# 只在无界机型上执行；已安装/已运行则直接成功。安装程序以管理员运行，本脚本幂等。
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
    Log ("机型检查：model={0} board={1}" -f $model, $board)
    if (($model -notmatch "WUJIE") -and ($board -notmatch "WUJIE")) {
        Log "非无界机型 ⇒ 不安装 inpoutx64 驱动"
        exit 0
    }
    $svc = Get-Service inpoutx64 -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Log "inpoutx64 服务已在运行 ⇒ 跳过"
        exit 0
    }
    $sysSrc = Join-Path $PSScriptRoot "inpoutx64.sys"
    if (-not (Test-Path $sysSrc)) {
        Log "缺少 inpoutx64.sys ⇒ 跳过"
        exit 1
    }
    $sysDir = Join-Path $env:SystemRoot "System32\drivers"
    $target = Join-Path $sysDir "inpoutx64.sys"
    Copy-Item $sysSrc $target -Force
    Log ("已复制驱动：{0}" -f $target)
    if ($svc) {
        & sc.exe config inpoutx64 binPath= ("\"" + $target + "\"") | Out-Null
    } else {
        & sc.exe create inpoutx64 type= kernel start= auto binPath= ("\"" + $target + "\"") | Out-Null
    }
    & sc.exe start inpoutx64 | Out-Null
    Start-Sleep -Milliseconds 500
    $after = Get-Service inpoutx64 -ErrorAction SilentlyContinue
    Log ("安装后服务状态：{0}" -f ($(if ($after) { $after.Status } else { "不存在" })))
    exit 0
} catch {
    Log ("安装异常：" + $_.Exception.Message)
    exit 1
}