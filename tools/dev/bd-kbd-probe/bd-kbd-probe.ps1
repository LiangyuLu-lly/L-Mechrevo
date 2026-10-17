# 宝龙达 WMI 键盘灯探针（只读为主；可选单次写）
#
# 目的：在 30 系（板号 MRID6）真机上确认
#   ① 有几个 MICommonInterface 实例，哪个实例的写入**真的生效**
#   ② 键盘灯 功能号 10/16/17/18 的**真实回读**（我们主程序读到的是 10=0 · 17=0,0,0 · 18=3）
#   ③ 是否需要"先开灯"（写 10=1）才写模式/颜色
#
# 用法（需管理员；LM 也是管理员，但 PowerShell 要单独提权）：
#   pwsh -File bd-kbd-probe.ps1                # 只读：列实例 + 读 10/16/17/18
#   pwsh -File bd-kbd-probe.ps1 -Watch 30      # 30 秒内每 1 s 读一次，只打印变化的行
#   pwsh -File bd-kbd-probe.ps1 -TestWrite     # ⚠️ 会写：16=2 → 17=红 → 18=1，每步都回读
#   pwsh -File bd-kbd-probe.ps1 -TestWrite -TurnOnFirst   # 先写 10=1 再走上面的写测试
#
# ⚠️ 写入只影响键盘灯（10/16/17/18），不碰风扇/功耗/模式。
# ⚠️ 与主程序同时运行会互相覆盖 ⇒ 测写入前**先退出 LM**。
#
# 帧格式（与主程序 BaolongdaWmi 一致）：
#   InData[1] = 250（读） / 251（写）
#   InData[3] = 功能号
#   InData[4..] = 载荷
#   OutData[3] = 回显功能号，OutData[4] = 值（多字节时 4.. 依次）

param(
    [int]$Watch = 0,
    [switch]$TestWrite,
    [switch]$TurnOnFirst,
    [int]$BufferLength = 64
)

$ErrorActionPreference = "Stop"
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$pr = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "❌ 需要管理员（WMI 方法调用会被拒）" -ForegroundColor Red
    exit 5
}

$Ns = "root\WMI"
$Class = "MICommonInterface"
# 方法名候选（不同批次固件有差异）：
$MethodCandidates = @("MICommonInterface", "GetSetULong", "GetSetLong", "MICommon")

function Get-Instances {
    $all = @(Get-CimInstance -Namespace $Ns -ClassName $Class -ErrorAction Stop)
    if ($all.Count -eq 0) { throw "没有 $Class 实例（本机不是宝龙达？）" }
    return $all
}

function Resolve-Method($inst) {
    foreach ($m in $MethodCandidates) {
        try {
            $null = Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments @{ InData = [byte[]]::new($BufferLength) } -ErrorAction Stop
            return $m
        } catch { }
    }
    return $null
}

function Invoke-Frame($inst, $method, [byte]$mode, [byte]$func, [byte[]]$payload) {
    $buf = [byte[]]::new($BufferLength)
    $buf[1] = $mode
    $buf[3] = $func
    if ($payload -and $payload.Length -gt 0) {
        [Array]::Copy($payload, 0, $buf, 4, [Math]::Min($payload.Length, $BufferLength - 4))
    }
    $r = Invoke-CimMethod -InputObject $inst -MethodName $method -Arguments @{ InData = $buf } -ErrorAction Stop
    return $r.OutData
}

function Read-Func($inst, $method, [byte]$func) {
    $o = Invoke-Frame $inst $method 250 $func $null
    if (-not $o -or $o.Length -le 4) { return $null }
    return [int]$o[4]
}

$funcs = [ordered]@{ 10 = "RGBKeyboardStatus"; 16 = "RGBKeyboardMode"; 17 = "RGBKeyboardColor(首字节)"; 18 = "RGBKeyboardBrightness" }

$insts = Get-Instances
Write-Host ("实例数：{0}" -f $insts.Count) -ForegroundColor Cyan
foreach ($i in $insts) {
    Write-Host ("  - {0}" -f $i.InstanceName)
}

# 解析方法名（每个实例都试一遍）
$resolved = @{}
foreach ($i in $insts) {
    $m = Resolve-Method $i
    $resolved[$i.InstanceName] = $m
    Write-Host ("  {0} ⇒ 方法名 {1}" -f $i.InstanceName, ($(if ($m) { $m } else { "❌ 未找到" }))) -ForegroundColor ($(if ($m) { "Green" } else { "Red" }))
}

function Snapshot {
    $rows = @()
    foreach ($i in $insts) {
        $m = $resolved[$i.InstanceName]
        if (-not $m) { continue }
        $vals = [ordered]@{}
        foreach ($f in $funcs.Keys) { $vals[$f] = (Read-Func $i $m ([byte]$f)) }
        $rows += [pscustomobject]@{ Instance = $i.InstanceName; V10 = $vals[10]; V16 = $vals[16]; V17 = $vals[17]; V18 = $vals[18] }
    }
    return $rows
}

Write-Host "`n=== 当前值（功能号 10 / 16 / 17首字节 / 18）===" -ForegroundColor Cyan
Snapshot | Format-Table -AutoSize

if ($Watch -gt 0) {
    Write-Host "`n=== 监视 $Watch 秒（每 1 s 一次，只打印变化）===" -ForegroundColor Cyan
    Write-Host "👉 现在请在官方控制台里点：开关灯 / 换颜色 / 调亮度，观察哪一列变化。" -ForegroundColor Yellow
    $prev = $null
    $deadline = (Get-Date).AddSeconds($Watch)
    while ((Get-Date) -lt $deadline) {
        $cur = Snapshot
        if ($prev) {
            foreach ($r in $cur) {
                $p = $prev | Where-Object { $_.Instance -eq $r.Instance }
                if ($p -and ($p.V10 -ne $r.V10 -or $p.V16 -ne $r.V16 -or $p.V17 -ne $r.V17 -or $p.V18 -ne $r.V18)) {
                    Write-Host ("[{0}] {1}: 10={2} 16={3} 17={4} 18={5}" -f (Get-Date -Format 'HH:mm:ss'), $r.Instance, $r.V10, $r.V16, $r.V17, $r.V18) -ForegroundColor Green
                }
            }
        } else {
            $cur | Format-Table -AutoSize
        }
        $prev = $cur
        Start-Sleep -Seconds 1
    }
    exit 0
}

if ($TestWrite) {
    Write-Host "`n=== 写测试（会改键盘灯；先退出 LM）===" -ForegroundColor Yellow
    foreach ($i in $insts) {
        $m = $resolved[$i.InstanceName]
        if (-not $m) { continue }
        Write-Host "`n--- 实例 $($i.InstanceName) ---" -ForegroundColor Cyan
        if ($TurnOnFirst) {
            $null = Invoke-Frame $i $m 251 10 ([byte[]]@(1))
            Start-Sleep -Milliseconds 200
            Write-Host ("  写 10=1 ⇒ 回读 10={0}" -f (Read-Func $i $m 10))
        }
        $null = Invoke-Frame $i $m 251 16 ([byte[]]@(2))
        Start-Sleep -Milliseconds 600
        Write-Host ("  写 16=2 ⇒ 回读 16={0}" -f (Read-Func $i $m 16))
        $null = Invoke-Frame $i $m 251 17 ([byte[]]@(255, 0, 0))
        Start-Sleep -Milliseconds 600
        Write-Host ("  写 17=红 ⇒ 回读 17={0}" -f (Read-Func $i $m 17))
        $null = Invoke-Frame $i $m 251 18 ([byte[]]@(1))
        Start-Sleep -Milliseconds 300
        Write-Host ("  写 18=1 ⇒ 回读 18={0}" -f (Read-Func $i $m 18))
        Write-Host "  👀 请目视：键盘灯变红了吗？亮度变了吗？" -ForegroundColor Yellow
    }
}

Write-Host "`n（结束）" -ForegroundColor DarkGray
