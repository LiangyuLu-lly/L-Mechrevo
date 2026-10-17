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
    [int]$Countdown = 3   # ⭐ 机主要求：倒计时默认 3 秒（够切到官方控制台）
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


# ⭐⭐ 2026-10-19（最后一招）：PS 5.1 对 WMI 返回值的封送（PSObject）反复出问题
#   ⇒ ⭐ 直接用 Add-Type 内联一段 C#，**与主程序逐字同构**（同一 API、同一帧格式）✅
#   ⭐ Win10 自带 .NET Framework 4.x + System.Management ⇒ 不需要装任何东西 ✅
if (-not ("BdProbe" -as [type])) {
    Add-Type -ReferencedAssemblies System.Management -TypeDefinition @"
using System;
using System.Management;
public static class BdProbe
{
    public static byte[] Invoke(string instanceName, byte mode, byte func, byte[] payload, int len)
    {
        byte[] buf = new byte[len];
        buf[1] = mode;
        buf[3] = func;
        if (payload != null && payload.Length > 0)
        {
            Array.Copy(payload, 0, buf, 4, Math.Min(payload.Length, len - 4));
        }
        string rel = "MICommonInterface.InstanceName=\"" + instanceName.Replace("\\", "\\\\") + "\"";
        using (ManagementObject mo = new ManagementObject("root\\\\WMI", rel, null))
        {
            ManagementBaseObject inP = mo.GetMethodParameters("MiInterface");
            inP["InData"] = buf;
            using (ManagementBaseObject outP = mo.InvokeMethod("MiInterface", inP, null))
            {
                return outP["OutData"] as byte[];
            }
        }
    }
}
"@
}

$Ns = "root\WMI"
$Class = "MICommonInterface"
$Method = "MiInterface"

function Get-Instances {
    $all = @(Get-CimInstance -Namespace $Ns -ClassName $Class -ErrorAction Stop)
    if ($all.Count -eq 0) { throw "没有 $Class 实例（本机不是宝龙达？）" }
    return $all
}

function Get-RelPath($instanceName) {
    # WMI 相对路径：MICommonInterface.InstanceName="ACPI\PNP0C14\MIFS_0"
    # ⚠️ 反斜杠在路径字符串里必须成对，否则 ManagementObject 解析失败（就报"对象的当前状态使该操作无效"）
    $escaped = $instanceName.Replace("\", "\\")
    return ('{0}.InstanceName="{1}"' -f $Class, $escaped)
}

function Invoke-Frame($instanceName, [byte]$mode, [byte]$func, [byte[]]$payload) {
    # ⭐ 全部交给内联 C#（BdProbe.Invoke），PowerShell 只负责传参/打印 ⇒ 不再碰 PSObject 封送 ✅
    return [BdProbe]::Invoke($instanceName, $mode, $func, $payload, $BufferLength)
}

function Read-Func($instanceName, [byte]$func) {
    $o = Invoke-Frame $instanceName 250 $func $null
    if ($null -eq $o) { return $null }
    if (@($o).Count -le 4) { return $null }
    return [int](@($o)[4])
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
            Write-Host ("        实例={0}" -f ($insts[0].InstanceName)) -ForegroundColor DarkYellow
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
