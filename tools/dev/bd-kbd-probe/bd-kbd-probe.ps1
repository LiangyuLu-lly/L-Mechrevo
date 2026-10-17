# 宝龙达 WMI 键盘灯探针 v2（System.Management，与主程序 C# 同一套调用）
#
# 为什么改：v1 用 CIM 的 Invoke-CimMethod，机主 23:12 日志报
#   「MiInterface ... 无效的对象」——方法名这次是对的（从"找不到方法"变成"无效的对象"），
#   是 PowerShell 5.1 的 CIM 对 byte[] 参数封送不兼容。
#   ✅ 主程序用的是 System.Management（ManagementObject.InvokeMethod）⇒ 探针改成同一套。
#
# 用法（双击 bd-kbd-probe.cmd，或管理员 PowerShell 里跑；需管理员）：
#   .\bd-kbd-probe.cmd                  # 快照 → 倒计时 10 s → 采样 30 s
#   .\bd-kbd-probe.cmd -Countdown 5 -Watch 60
#   .\bd-kbd-probe.cmd -TestWrite       # 写测试（先退出 LM）
#   .\bd-kbd-probe.cmd -TestWrite -TurnOnFirst
#
# 全程日志写到脚本同目录：bd-kbd-probe-yyyyMMdd-HHmmss.log

param(
    [int]$Watch = 0,
    [switch]$TestWrite,
    [switch]$TurnOnFirst,
    [int]$BufferLength = 32,
    [int]$Countdown = 10
)

$ErrorActionPreference = "Stop"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$pr = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "❌ 需要管理员（WMI 方法调用会被拒）" -ForegroundColor Red
    Read-Host "按回车关闭窗口" | Out-Null
    return
}

$LogPath = Join-Path $PSScriptRoot ("bd-kbd-probe-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
try {
    Start-Transcript -Path $LogPath -Force | Out-Null
    Write-Host ("📝 日志：{0}" -f $LogPath) -ForegroundColor DarkGray
} catch {
    Write-Host ("⚠️ 写日志失败（继续运行）：{0}" -f $_.Exception.Message) -ForegroundColor Yellow
    $LogPath = $null
}

$Ns = "root\WMI"
$Class = "MICommonInterface"
$Method = "MiInterface"

function Get-Instances {
    $all = @(Get-CimInstance -Namespace $Ns -ClassName $Class -ErrorAction Stop)
    if ($all.Count -eq 0) { throw "没有 $Class 实例（本机不是宝龙达？）" }
    return $all
}

function Invoke-Frame($instanceName, [byte]$mode, [byte]$func, [byte[]]$payload) {
    $buf = New-Object 'byte[]' $BufferLength
    $buf[1] = $mode
    $buf[3] = $func
    if ($payload -and $payload.Length -gt 0) {
        [Array]::Copy($payload, 0, $buf, 4, [Math]::Min($payload.Length, $BufferLength - 4))
    }
    $mo = New-Object System.Management.ManagementObject($Ns, $instanceName, $null)
    try {
        $in = $mo.GetMethodParameters($Method)
        $in["InData"] = $buf
        $out = $mo.InvokeMethod($Method, $in, $null)
        if (-not $out) { return $null }
        return [byte[]]$out["OutData"]
    } finally {
        $mo.Dispose()
    }
}

function Read-Func($instanceName, [byte]$func) {
    $o = Invoke-Frame $instanceName 250 $func $null
    if (-not $o -or $o.Length -le 4) { return $null }
    return [int]$o[4]
}

function Snapshot($insts) {
    $rows = @()
    foreach ($i in $insts) {
        $rows += [pscustomobject]@{
            Instance = $i.InstanceName
            V10 = (Read-Func $i.InstanceName 10)
            V16 = (Read-Func $i.InstanceName 16)
            V17 = (Read-Func $i.InstanceName 17)
            V18 = (Read-Func $i.InstanceName 18)
        }
    }
    return $rows
}

$insts = Get-Instances
Write-Host ("实例数：{0}" -f $insts.Count) -ForegroundColor Cyan
foreach ($i in $insts) { Write-Host ("  - {0}" -f $i.InstanceName) }

Write-Host "`n=== 当前值（功能号 10 / 16 / 17首字节 / 18）===" -ForegroundColor Cyan
try {
    Snapshot $insts | Format-Table -AutoSize
} catch {
    Write-Host ("❌ 读取失败：{0}" -f $_.Exception.Message) -ForegroundColor Red
    Write-Host "   （若仍报 '无效的对象'/'未找到'，请把本日志发给我们）" -ForegroundColor Yellow
}

$watchSec = if ($Watch -gt 0) { $Watch } else { 30 }

if ($TestWrite) {
    Write-Host "`n=== 写测试（会改键盘灯；请先退出 LM）===" -ForegroundColor Yellow
    foreach ($i in $insts) {
        Write-Host "`n--- 实例 $($i.InstanceName) ---" -ForegroundColor Cyan
        if ($TurnOnFirst) {
            $null = Invoke-Frame $i.InstanceName 251 10 ([byte[]]@(1))
            Start-Sleep -Milliseconds 200
            Write-Host ("  写 10=1 ⇒ 回读 10={0}" -f (Read-Func $i.InstanceName 10))
        }
        $null = Invoke-Frame $i.InstanceName 251 16 ([byte[]]@(2))
        Start-Sleep -Milliseconds 600
        Write-Host ("  写 16=2 ⇒ 回读 16={0}" -f (Read-Func $i.InstanceName 16))
        $null = Invoke-Frame $i.InstanceName 251 17 ([byte[]]@(255, 0, 0))
        Start-Sleep -Milliseconds 600
        Write-Host ("  写 17=红 ⇒ 回读 17={0}" -f (Read-Func $i.InstanceName 17))
        $null = Invoke-Frame $i.InstanceName 251 18 ([byte[]]@(1))
        Start-Sleep -Milliseconds 300
        Write-Host ("  写 18=1 ⇒ 回读 18={0}" -f (Read-Func $i.InstanceName 18))
        Write-Host "  👀 请目视：键盘灯变红了吗？亮度变了吗？" -ForegroundColor Yellow
    }
}
else {
    if ($Countdown -gt 0) {
        Write-Host "`n=== 倒计时 $Countdown 秒：请切到官方控制台，准备点「关灯 / 开灯 / 换颜色 / 调亮度」===" -ForegroundColor Yellow
        for ($c = $Countdown; $c -gt 0; $c--) {
            Write-Host ("   {0,2} 秒..." -f $c) -ForegroundColor Yellow
            Start-Sleep -Seconds 1
        }
    }
    Write-Host "`n=== 开始采样 $watchSec 秒（每 1 s 一次，只打印变化）===" -ForegroundColor Cyan
    Write-Host "👉 现在点官方控制台：关灯 → 开灯 → 换颜色 → 调亮度" -ForegroundColor Yellow
    $prev = $null
    $deadline = (Get-Date).AddSeconds($watchSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $cur = Snapshot $insts
        } catch {
            Write-Host ("[{0}] 读取失败：{1}" -f (Get-Date -Format 'HH:mm:ss'), $_.Exception.Message) -ForegroundColor Red
            Start-Sleep -Seconds 1
            continue
        }
        if ($prev) {
            foreach ($r in $cur) {
                $p = $prev | Where-Object { $_.Instance -eq $r.Instance }
                if ($p -and ($p.V10 -ne $r.V10 -or $p.V16 -ne $r.V16 -or $p.V17 -ne $r.V17 -or $p.V18 -ne $r.V18)) {
                    Write-Host ("[{0}] {1}: 10={2} 16={3} 17={4} 18={5}" -f (Get-Date -Format 'HH:mm:ss'), $r.Instance, $r.V10, $r.V16, $r.V17, $r.V18) -ForegroundColor Green
                }
            }
        }
        $prev = $cur
        Start-Sleep -Seconds 1
    }
}

if ($LogPath) { try { Stop-Transcript | Out-Null } catch { } }
Write-Host "`n（结束）" -ForegroundColor DarkGray
if ($LogPath) { Write-Host ("📝 日志已保存：{0}" -f $LogPath) -ForegroundColor DarkGray }
Read-Host "按回车关闭窗口" | Out-Null
