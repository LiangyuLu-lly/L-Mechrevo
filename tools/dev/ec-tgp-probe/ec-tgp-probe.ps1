# 只读探针：EC 里的 TGP / DB 字节 vs nvidia-smi 的 GPU 预算
#
# 目的（2026-10-17）：40 系 GCU 反编译里，TGP/DB 的 EC 地址是
#   0x743 (1859) ConfigurableTGP_DynamicBoost_CTRL_BYTE   控制字节
#   0x744 (1860) ConfigurableTGP_VALUE                    基础 TGP
#   0x745 (1861) DynamicBoost_TotalProcessingPowerTarget    DB 总处理功耗目标
#   0x746 (1862) DynamicBoost_MaxinumTGP                    DB 最大 TGP
# 但那四个常量只有声明、没有可见调用点 ⇒ "哪个字节 = 115W / 140W" 必须真机对照。
#
# ⛔ 本脚本**只读**：只调 GetSetULong 的"读"标志（0x10000000000 + 地址），绝不写入。
# ⚠️ 必须**以管理员身份**运行（AcpiTest_MULong 需要管理员；LM 自身也是 requireAdministrator）。
#
# 用法：
#   1) 单次快照：      pwsh -File ec-tgp-probe.ps1
#   2) 边切档边采样：  pwsh -File ec-tgp-probe.ps1 -Seconds 90
#      ⇒ 启动后请把性能模式在【平衡 ↔ 狂暴】之间切换几次，脚本只打印"有变化"的行。
#
# 结果怎么看：找出哪几个字节在切换后跟着 115 → 140 变，那就是该档的 TGP/DB 落点。

param(
    [int]$Seconds = 0,
    [int]$IntervalSec = 2,
    # 可选：同时把控制台输出录到文件（提权运行时用，便于事后比对）
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

# ── 管理员检查 ────────────────────────────────────────────────
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "❌ 需要管理员权限（AcpiTest_MULong 会被拒绝访问）。" -ForegroundColor Red
    Write-Host "   请右键 PowerShell → 以管理员身份运行，再执行本脚本。" -ForegroundColor Yellow
    exit 5
}

# 可选录制（Write-Host 也会被 transcript 收进文件）
if ($LogPath) {
    try { Start-Transcript -Path $LogPath -Force | Out-Null } catch { }
}

# ── nvidia-smi ───────────────────────────────────────────────
function Get-GpuPower {
    $smi = 'C:\Windows\System32\nvidia-smi.exe'
    if (-not (Test-Path $smi)) { return $null }
    $line = & $smi --query-gpu=power.default_limit,power.max_limit,enforced.power.limit,power.draw --format=csv,noheader 2>$null
    if (-not $line) { return $null }
    $parts = ($line -split ',') | ForEach-Object { $_.Trim() }
    return [pscustomobject]@{
        Default   = $parts[0]
        Max       = $parts[1]
        Enforced  = $parts[2]
        Draw      = $parts[3]
    }
}

# ── EC 只读（ACPI WMI GetSetULong）──────────────────────────────
# ⚠️ AcpiTest_MULong 可能有多条实例（对应不同 Scope）⇒ 必须取**一条**，
#    否则 Invoke-CimMethod -InputObject 会收到 Object[] 而报 ParameterBindingException。
#    GCU 用的是 `ACPI\PNP0C14\1_1`（见反编译 WMIEC.cs），这里优先选它。
$cimAll = @(Get-CimInstance -Namespace 'root\WMI' -ClassName 'AcpiTest_MULong' -ErrorAction Stop)
if ($cimAll.Count -eq 0) { Write-Host "❌ 没有 AcpiTest_MULong（本机不是同方 ACPI？）" -ForegroundColor Red; exit 6 }
$cim = $cimAll | Where-Object { $_.InstanceName -like '*PNP0C14\1_1*' } | Select-Object -First 1
if (-not $cim) { $cim = $cimAll | Select-Object -First 1 }
Write-Host ("使用实例：" + $cim.InstanceName + "（共 " + $cimAll.Count + " 条）") -ForegroundColor DarkGray

function Read-Ec([int]$addr) {
    $r = Invoke-CimMethod -InputObject $cim -MethodName GetSetULong -Arguments @{ Data = [uint64](0x10000000000 + $addr) }
    return [uint64]$r.Return
}

# 观察窗口：0x740..0x74F（含 0x743-0x746）＋ 0x78B（MYFAN3_GPU_SETTING）
$addrs = @()
$addrs += 1856..1871          # 0x740..0x74F
$addrs += 1931                # 0x78B

function Take-Snapshot {
    $ec = @{}
    foreach ($a in $addrs) { $ec[$a] = Read-Ec $a }
    return [pscustomobject]@{ Gpu = (Get-GpuPower); Ec = $ec }
}

function Format-Snapshot($snap) {
    $g = $snap.Gpu
    $gpuText = if ($g) { "default=$($g.Default) max=$($g.Max) enforced=$($g.Enforced) draw=$($g.Draw)" } else { "nvidia-smi 不可用" }
    Write-Host ""
    Write-Host "=== $(Get-Date -Format 'HH:mm:ss') · GPU: $gpuText ===" -ForegroundColor Cyan
    foreach ($a in $addrs) {
        $hex = '0x{0:X3}' -f $a
        $val = $snap.Ec[$a]
        $tag = ''
        if ($a -eq 1859) { $tag = '  ← CTRL(ConfigurableTGP/DB)' }
        elseif ($a -eq 1860) { $tag = '  ← ConfigurableTGP' }
        elseif ($a -eq 1861) { $tag = '  ← DB TotalProcessingPowerTarget' }
        elseif ($a -eq 1862) { $tag = '  ← DB MaxTGP' }
        elseif ($a -eq 1931) { $tag = '  ← MYFAN3_GPU_SETTING' }
        Write-Host ("  {0} ({1,4}) = 0x{2:X4}  → 低字节(该地址) = {3,3} / 0x{3:X2}{4}" -f $hex, $a, $val, ($val -band 0xFF), $tag)
    }
}

# ── 单次快照 ─────────────────────────────────────────────────
if ($Seconds -le 0) {
    $snap = Take-Snapshot
    Format-Snapshot $snap
    Write-Host ""
    Write-Host "（只读快照结束；未写入任何 EC 字节）" -ForegroundColor DarkGray
    if ($LogPath) { try { Stop-Transcript | Out-Null } catch { } }
    exit 0
}

# ── 连续采样：只打印"有变化"的行 ─────────────────────────────────
Write-Host ""
Write-Host "采样 $Seconds 秒，每 $IntervalSec 秒一次；**只打印有变化的行**。" -ForegroundColor Yellow
Write-Host "👉 现在请切换性能模式（平衡 ↔ 狂暴）几次，并留意哪几个字节跟着 115/140 变。" -ForegroundColor Yellow
Write-Host ""

$prev = $null
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    $snap = Take-Snapshot
    if ($null -eq $prev) {
        Format-Snapshot $snap
    }
    else {
        $changed = @()
        if ($prev.Gpu.Enforced -ne $snap.Gpu.Enforced) {
            $changed += ("enforced: {0} → {1}" -f $prev.Gpu.Enforced, $snap.Gpu.Enforced)
        }
        foreach ($a in $addrs) {
            if ($prev.Ec[$a] -ne $snap.Ec[$a]) {
                $changed += ("0x{0:X3}: {1} → {2}" -f $a, $prev.Ec[$a], $snap.Ec[$a])
            }
        }
        if ($changed.Count -gt 0) {
            Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), ($changed -join ' | ')) -ForegroundColor Green
        }
    }
    $prev = $snap
    Start-Sleep -Seconds $IntervalSec
}

Write-Host ""
Write-Host "（采样结束；全程只读，未写入任何 EC 字节）" -ForegroundColor DarkGray
if ($LogPath) { try { Stop-Transcript | Out-Null } catch { } }
