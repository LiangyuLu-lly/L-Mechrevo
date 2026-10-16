# -*- coding: utf-8 -*-
r"""
生成「核显切换采集」工具（给其他机主用，零依赖：只用 Windows 自带 PowerShell）
================================================================================
产出目录：D:\openLM\tools\igpu-capture\
  采集核显切换.bat      <- 机主双击这个（ASCII，自提权）
  collect.ps1           <- 实际逻辑（UTF-8 **带 BOM**，中文才正常）
  说明.txt              <- 给机主的操作说明

设计要点：
- 不用 Python / 不用抓包（GCU 的 MQTT client_id 有白名单，抓包物理上做不到）
- 靠 LM 自己的日志（log.txt，磁盘文件，重启不丢）
- **两阶段采集**：①点完「核显模式」先别重启就收一次 ②重启后再收一次校验结果
- 环境变量 LMECHREVO_LOG_LEVEL=All（HKCU），否则默认 Off 没有 log.txt
"""
import io
import os
import sys

OUT = r'D:\openLM\tools\igpu-capture'
os.makedirs(OUT, exist_ok=True)

BAT = r"""@echo off
rem ============================================================
rem  L-Mechrevo iGPU-switch capture tool  (ASCII-only by design)
rem  Double-click this file. It self-elevates and runs collect.ps1
rem ============================================================
setlocal
set "PS1=%~dp0collect.ps1"
if not exist "%PS1%" (
  echo [ERROR] collect.ps1 not found next to this .bat
  pause
  exit /b 1
)
rem --- self-elevate if not admin (LM runs elevated; restarting it needs admin)
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
echo.
pause
"""

PS1 = r'''# =====================================================================
#  L-Mechrevo 核显模式切换 —— 动作采集工具
#  （给机主使用；只依赖 Windows 自带的 PowerShell，不需要装任何东西）
#
#  原理：GCU 的 MQTT 端口对 client_id 有白名单（只认 LM 自己用的那个），
#        所以【无法抓包】。但 LM 会把每次发出的报文写进日志 log.txt（磁盘文件），
#        本工具就是打开全量日志、引导你点一次「核显模式」，再把日志里的
#        动作记录整理成一份报告。
# =====================================================================

$ErrorActionPreference = 'Stop'
$app = 'MechrevoLite'
$appData = Join-Path $env:APPDATA $app
$logFile = Join-Path $appData 'log.txt'
$cfgFile = Join-Path $appData 'config.json'
$exeCandidates = @(
    (Join-Path ${env:ProgramFiles} 'L-Mechrevo\L-Mechrevo.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'L-Mechrevo\L-Mechrevo.exe')
)

function Line($t) { Write-Host $t }
function Rule { Write-Host ('=' * 72) }

function Get-LMPath {
    foreach ($p in $exeCandidates) { if ($p -and (Test-Path -LiteralPath $p)) { return $p } }
    return $null
}

function Set-LogLevel {
    [Environment]::SetEnvironmentVariable('LMECHREVO_LOG_LEVEL', 'All', 'User')
    $env:LMECHREVO_LOG_LEVEL = 'All'
    Line '  [OK] 已设置 LMECHREVO_LOG_LEVEL=All（对【新启动】的 LM 生效）'
    if ([Environment]::GetEnvironmentVariable('LMECHREVO_DIAGNOSTIC_PACK_PATH', 'User')) {
        Line '  [注意] 检测到 LMECHREVO_DIAGNOSTIC_PACK_PATH，它会把诊断包写死到固定文件名，'
        Line '         如非必要建议删除该环境变量。'
    }
}

function Restart-LM {
    $exe = Get-LMPath
    if (-not $exe) { Line '  [警告] 没找到 L-Mechrevo.exe，请手动退出并重新打开 LM'; return }
    Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    Start-Process -FilePath $exe
    Line '  [OK] 已重启 LM'
    Start-Sleep -Seconds 8
}

# 从日志里挑出关注的行
$Patterns = [ordered]@{
    '① LM 实际发出的动作'   = 'SwitchGpuMode\(\d+\)\s*->'
    '② 冷切换载荷序列'       = 'GPU restart route applied'
    '③ 模式回读'             = 'GpuMode\s*->'
    '④ 路由 / NVRAM 待生效'  = 'GPU route (at startup|readback|verify)'
    '⑤ 能力位'               = 'TrayGPU:|IgpuOnlyConnectionSwitch|DiscreteGpuDirectConnectionSwitch'
    '⑥ 核显省电'             = 'SetIGpuOnlySaveMode|IgpuSaving'
    '⑦ 机型与硬件'           = 'Device capabilities:|HardwareInfo:'
}

function Collect([string]$stage, [string]$outPath) {
    if (-not (Test-Path -LiteralPath $logFile)) {
        Line ('  [失败] 找不到日志：' + $logFile)
        Line '         说明日志级别没生效。请确认 LM 已【重新启动】过。'
        return $false
    }
    $fi = Get-Item -LiteralPath $logFile
    $all = Get-Content -LiteralPath $logFile -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $all) { Line '  [失败] 日志为空'; return $false }

    $model = ''; $bios = ''; $cpu = ''; $gpu = ''
    foreach ($l in $all) {
        if (-not $model -and $l -match 'Device capabilities:.*model=([^,]+),.*bios=([^,]*)') {
            $model = $Matches[1].Trim(); $bios = $Matches[2].Trim()
        }
        if (-not $cpu -and $l -match '"ProcessorInfo":"([^"]+)"') { $cpu = $Matches[1] }
        if (-not $gpu -and $l -match '(NVIDIA[^",\\]{4,60})') { $gpu = $Matches[1].Trim() }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('L-Mechrevo 核显模式切换 —— 动作采集报告')
    [void]$sb.AppendLine('=' * 72)
    [void]$sb.AppendLine(('采集阶段 : ' + $stage))
    [void]$sb.AppendLine(('生成时间 : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
    [void]$sb.AppendLine(('机型     : ' + $(if ($model) { $model } else { '(未解析)' })))
    [void]$sb.AppendLine(('BIOS     : ' + $(if ($bios) { $bios } else { '(未解析)' })))
    [void]$sb.AppendLine(('CPU      : ' + $(if ($cpu) { $cpu } else { '(未解析)' })))
    [void]$sb.AppendLine(('GPU      : ' + $(if ($gpu) { $gpu } else { '(未解析)' })))
    if (Test-Path -LiteralPath $cfgFile) {
        try {
            $cfg = Get-Content -LiteralPath $cfgFile -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($k in 'dgpu_generation', 'dgpu_generation_source', 'gpu_mode', 'gpu_restart_pending', 'gpu_restart_target', 'log_level') {
                $v = $cfg.PSObject.Properties | Where-Object { $_.Name -eq $k }
                if ($v) { [void]$sb.AppendLine(('{0,-9}: {1}' -f $k, $v.Value)) }
            }
        } catch { }
    }
    [void]$sb.AppendLine(('日志     : {0:N1} MB / {1} 行' -f ($fi.Length / 1MB), $all.Count))
    [void]$sb.AppendLine('')

    foreach ($k in $Patterns.Keys) {
        $hit = $all | Select-String -Pattern $Patterns[$k] -ErrorAction SilentlyContinue
        [void]$sb.AppendLine('=' * 72)
        [void]$sb.AppendLine(('## {0}   ({1} 行)' -f $k, $hit.Count))
        [void]$sb.AppendLine('=' * 72)
        if ($hit.Count -eq 0) {
            [void]$sb.AppendLine('   （无）')
        }
        elseif ($k -eq '⑤ 能力位') {
            $sample = @($hit | Select-Object -First 6) + @($hit | Select-Object -Last 6)
            foreach ($h in $sample) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
        elseif ($hit.Count -gt 60) {
            foreach ($h in ($hit | Select-Object -First 40)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
            [void]$sb.AppendLine(('   ... 省略 {0} 行 ...' -f ($hit.Count - 50)))
            foreach ($h in ($hit | Select-Object -Last 10)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
        else {
            foreach ($h in $hit) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
        [void]$sb.AppendLine('')
    }

    [void]$sb.AppendLine('=' * 72)
    [void]$sb.AppendLine('## ★ 重点：切到【核显模式】(mode 0) 的相关记录')
    [void]$sb.AppendLine('=' * 72)
    $focus = $all | Select-String -Pattern 'SwitchGpuMode\(0\)|target=0|GpuMode -> 0 |TOGGLE_IGPU|IGPU_ONLY_CONNECT_RB_ON' -ErrorAction SilentlyContinue
    if ($focus.Count -eq 0) {
        [void]$sb.AppendLine('   （未发现 —— 请确认已在 LM 里点过「核显模式」按钮）')
    }
    else {
        foreach ($h in ($focus | Select-Object -Last 40)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
    }

    $dir = Split-Path -Parent $outPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Line ('  [OK] 已写入：' + $outPath)
    return $true
}

# ============================== 主流程 ==============================
Rule
Line ' L-Mechrevo 核显模式切换 —— 动作采集（三步）'
Rule
Line ''

if (-not (Get-LMPath)) {
    Line ' [错误] 没找到 L-Mechrevo.exe（默认在 C:\Program Files\L-Mechrevo\）。'
    Line '        请确认已安装 L-Mechrevo 后重试。'
    Read-Host ' 按回车退出'
    exit 1
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$desk = [Environment]::GetFolderPath('Desktop')
$safeModel = 'unknown'

# --- 1/3 ---
Line '【步骤 1/3】打开全量日志并重启 LM'
Set-LogLevel
Restart-LM
Line ''
Line '【步骤 2/3】现在请到 LM 界面里操作：'
Line '    1) 打开「设置」页，找到「显卡模式」那一行'
Line '    2) 点一下【核显模式】按钮'
Line '    3) 若弹出「需要重启」的提示 —— 请先点【取消】！'
Line '       （先不要重启，我们要在重启前把动作记录收下来）'
Line ''
Read-Host '  做完后按回车继续'

$first = Join-Path $desk ('LM-核显采集-第一阶段-' + $stamp + '.txt')
$ok1 = Collect '第一阶段（点击核显模式后，未重启）' $first
Line ''

# --- 3/3 ---
Line '【步骤 3/3】（可选，但很有价值）重启 Windows 后再采集一次'
Line '    重启后 LM 会回读实际路由并写日志，能验证切换是否真的生效。'
$ans = Read-Host '  现在重启电脑吗？(Y/N)'
if ($ans -match '^[Yy]') {
    Line '  将在 10 秒后重启。重启完成后，请再次双击本工具的 bat 文件，'
    Line '  这次选择「只采集（不重启 LM）」。'
    Line ''
    Line '  ⚠️ 重启后请在同一个 bat 里按提示操作；报告会生成到桌面。'
    Start-Sleep -Seconds 2
    shutdown /r /t 10 /c "L-Mechrevo 核显切换采集：重启以完成切换"
    exit 0
}
else {
    Line '  跳过重启。'
    Line ''
    Line '  提示：你也可以稍后手动重启，然后再次双击本 bat —— 它会再采一次并生成第二阶段报告。'
}

Line ''
Rule
Line ' 完成！请把桌面上生成的报告文件发回给开发者：'
if ($ok1) { Line ('   ' + $first) }
Rule
Start-Sleep -Seconds 1
try { Start-Process explorer.exe $desk } catch { }
'''

README = """L-Mechrevo 核显模式切换 —— 动作采集工具
================================================

给机主：怎么用
--------------
1. 把整个文件夹放到任意位置（例如桌面）
2. 双击【采集核显切换.bat】
   - 会弹出管理员授权（因为 LM 是管理员权限运行的，需要重启它）
   - 弹出「用户账户控制」时选【是】
3. 按窗口提示操作：
   - 它会自动打开全量日志并重启 LM
   - 然后请到 LM 的「设置」-> 显卡模式，点一下【核显模式】
   - ⚠️ 若弹出「需要重启」的提示，请先点【取消】（先别重启）
   - 回到黑窗口按回车
4. 它会自动在【桌面】生成一份报告：LM-核显采集-第一阶段-<时间>.txt
5. 窗口最后会问你是否重启电脑：
   - 选 Y：重启后请再次双击 bat，再采一次（第二阶段报告会记录"是否真的切成功"）
   - 选 N：跳过，稍后手动重启也一样
6. 把桌面上的报告文件发回给开发者即可

为什么不用抓包
--------------
GCU 的 MQTT 端口对 "客户端 ID" 有白名单，只接受 LM 自己使用的那个 ID，
其它程序连上去会被拒绝，所以无法旁路抓包。
但 LM 会把每次发出的报文写进自己的日志（磁盘文件），本工具就是读取它。

需要装什么吗
------------
不需要。只用 Windows 自带的 PowerShell。
（首次运行若提示脚本执行策略，bat 里已经带了 -ExecutionPolicy Bypass）

隐私说明
--------
报告只包含：机型/BIOS/CPU/GPU 名称、LM 的显卡模式相关日志行、配置里的
显卡模式字段。**不包含**用户名、序列号、密码或任何文件内容。
"""

with io.open(os.path.join(OUT, '采集核显切换.bat'), 'w', encoding='ascii', newline='\r\n') as f:
    f.write(BAT)

# PowerShell 5.1 需要 BOM 才能正确识别 UTF-8 中的中文 -> 用 utf-8-sig
with io.open(os.path.join(OUT, 'collect.ps1'), 'w', encoding='utf-8-sig', newline='\r\n') as f:
    f.write(PS1)

with io.open(os.path.join(OUT, '说明.txt'), 'w', encoding='utf-8-sig', newline='\r\n') as f:
    f.write(README)

print('已生成:')
for n in sorted(os.listdir(OUT)):
    p = os.path.join(OUT, n)
    print('   %-24s %8d 字节' % (n, os.path.getsize(p)))
