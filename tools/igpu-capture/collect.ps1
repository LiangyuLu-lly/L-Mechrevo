# =====================================================================
#  L-Mechrevo 核显模式切换 —— 动作采集（v2，两阶段）
#  只依赖 Windows 自带 PowerShell，不需要装任何东西。
#
#  为什么不用抓包：GCU 的 MQTT 端口对「客户端 ID」有白名单，只接受 LM 自己
#  使用的那个 ID，其它程序会被拒绝 —— 旁路抓包物理上做不到。
#  但 LM 会把每次发出的报文写进自己的日志（磁盘文件）—— 本工具读它。
#
#  ⚠️⚠️ 重要：在 LM 的切换提示框里必须点【重启切换】！
#      点【取消】= 整个切换被取消 = 一条指令都不会发出 = 日志里什么都没有。
# =====================================================================

param(
    [switch]$Step1,        # 阶段一：开日志 + 重启 LM + 指引（不采集）
    [switch]$CollectOnly   # 阶段二：只采集（系统重启后跑这个）
)

$ErrorActionPreference = 'Continue'

function Line($t) { Write-Host $t }
function Rule { Write-Host ('=' * 72) }

# ---------------- 找 L-Mechrevo.exe ----------------
function Find-LMExe {
    # 1) 正在运行的进程（最可靠）
    $p = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue |
         Where-Object { $_.Path } | Select-Object -First 1
    if ($p -and (Test-Path -LiteralPath $p.Path)) { return $p.Path }
    # 2) 注册表卸载项 / App Paths
    foreach ($k in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
        try {
            Get-ItemProperty $k -ErrorAction SilentlyContinue |
              Where-Object { $_.DisplayName -like '*Mechrevo*' -and $_.InstallLocation } |
              ForEach-Object {
                  $c = Join-Path $_.InstallLocation 'L-Mechrevo.exe'
                  if (Test-Path -LiteralPath $c) { return $c }
              }
        } catch { }
    }
    # 3) 常见位置
    $cands = @(
        (Join-Path $env:ProgramFiles 'L-Mechrevo\L-Mechrevo.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'L-Mechrevo\L-Mechrevo.exe'),
        (Join-Path $env:LOCALAPPDATA 'L-Mechrevo\L-Mechrevo.exe'),
        'D:\Desktop\系统工具\控制台\L-Mechrevo\L-Mechrevo.exe'
    )
    foreach ($c in $cands) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
    # 4) 手动输入
    Line '  [提示] 没自动找到 L-Mechrevo.exe。'
    Line '         请把它的完整路径粘贴进来（可拖拽文件到本窗口）后回车；'
    Line '         直接回车则跳过"自动重启 LM"这一步（你可手动重启 LM）。'
    $manual = Read-Host '  路径'
    if ($manual) {
        $manual = $manual.Trim('"').Trim()
        if (Test-Path -LiteralPath $manual) { return $manual }
        Line '        该路径不存在，已跳过自动重启。'
    }
    return $null
}

# ---------------- 找 log.txt ----------------
function Find-LogFile {
    $cands = New-Object System.Collections.Generic.List[string]
    if ($env:LMECHREVO_LOG_DIR) { $cands.Add((Join-Path $env:LMECHREVO_LOG_DIR 'log.txt')) }
    if ($env:APPDATA)    { $cands.Add((Join-Path (Join-Path $env:APPDATA 'MechrevoLite') 'log.txt')) }
    if ($env:ProgramData){ $cands.Add((Join-Path (Join-Path $env:ProgramData 'MechrevoLite') 'log.txt')) }
    $best = $null; $bestTime = [datetime]::MinValue
    foreach ($c in $cands) {
        if (Test-Path -LiteralPath $c) {
            $t = (Get-Item -LiteralPath $c).LastWriteTime
            if ($t -gt $bestTime) { $bestTime = $t; $best = $c }
        }
    }
    return $best
}

function Find-CfgFile {
    if ($env:APPDATA)     { $c = Join-Path (Join-Path $env:APPDATA 'MechrevoLite') 'config.json'; if (Test-Path -LiteralPath $c) { return $c } }
    if ($env:ProgramData) { $c = Join-Path (Join-Path $env:ProgramData 'MechrevoLite') 'config.json'; if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}

# ---------------- 采集 ----------------
$Patterns = [ordered]@{
    '① LM 实际发出的动作'  = 'SwitchGpuMode|CreateGpuSwitchPayload|Publish.*Setting/Control'
    '② 冷切换载荷序列'      = 'GPU restart route applied|payload\(s\)'
    '③ 模式回读'            = 'GpuMode\s*->|GpuMode\b.*dgpu='
    '④ 路由 / NVRAM 待生效' = 'GPU route|NVRAM display byte|OemDisplayMode'
    '⑤ 能力位'              = 'TrayGPU|IgpuOnlyConnectionSwitch|DiscreteGpuDirectConnectionSwitch|iGPUModeOnlySupport|IGpuAutoSupportSwitch'
    '⑥ 核显省电'            = 'SetIGpuOnlySaveMode|IgpuSaving|IGPU_ONLY_CONNECT_RB_AUTO'
    '⑦ 机型与硬件'          = 'Device capabilities|HardwareInfo|SystemProductName'
    '⑧ 核显模式相关（宽匹配）' = 'IGPU_ONLY|IgpuOnly|TOGGLE_IGPU|核显|集显|iGPU|Igpu'
}

function Collect([string]$stage, [string]$outPath) {
    $logFile = Find-LogFile
    if (-not $logFile) {
        Line '  [失败] 找不到 log.txt。可能原因：'
        Line '         1) 日志级别没生效 —— 请确认执行步骤一后 LM 确实被【重新启动】过'
        Line '         2) LM 以 SYSTEM 身份运行 —— 日志会在 C:\ProgramData\MechrevoLite\'
        Line '         3) 设过 LMECHREVO_LOG_DIR 环境变量 —— 日志在那个目录'
        return $false
    }
    $fi = Get-Item -LiteralPath $logFile
    $all = Get-Content -LiteralPath $logFile -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $all) { Line '  [失败] 日志为空'; return $false }

    $model = ''; $bios = ''; $cpu = ''; $gpu = ''
    foreach ($l in $all) {
        if (-not $model -and $l -match 'model=([^,]+),.*bios=([^,]*)') { $model = $Matches[1].Trim(); $bios = $Matches[2].Trim() }
        if (-not $cpu -and $l -match '"ProcessorInfo":"([^"]+)"') { $cpu = $Matches[1] }
        if (-not $gpu -and $l -match '(NVIDIA[^",\\]{4,60})') { $gpu = $Matches[1].Trim() }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('L-Mechrevo 核显模式切换 —— 动作采集报告')
    [void]$sb.AppendLine('=' * 72)
    [void]$sb.AppendLine(('采集阶段 : ' + $stage))
    [void]$sb.AppendLine(('生成时间 : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
    [void]$sb.AppendLine(('日志文件 : ' + $logFile))
    [void]$sb.AppendLine(('机型     : ' + $(if ($model) { $model } else { '(未解析)' })))
    [void]$sb.AppendLine(('BIOS     : ' + $(if ($bios) { $bios } else { '(未解析)' })))
    [void]$sb.AppendLine(('CPU      : ' + $(if ($cpu) { $cpu } else { '(未解析)' })))
    [void]$sb.AppendLine(('GPU      : ' + $(if ($gpu) { $gpu } else { '(未解析)' })))
    $cfg = Find-CfgFile
    if ($cfg) {
        [void]$sb.AppendLine(('配置文件 : ' + $cfg))
        try {
            $j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($k in 'dgpu_generation', 'dgpu_generation_source', 'gpu_mode', 'gpu_restart_pending', 'gpu_restart_target', 'log_level', 'gcu_variant') {
                $v = $j.PSObject.Properties | Where-Object { $_.Name -eq $k }
                if ($v) { [void]$sb.AppendLine(('{0,-22}: {1}' -f $k, $v.Value)) }
            }
        } catch { }
    }
    [void]$sb.AppendLine(('环境变量 : LMECHREVO_LOG_LEVEL=' + $(if ($env:LMECHREVO_LOG_LEVEL) { $env:LMECHREVO_LOG_LEVEL } else { '(未设置)' }) +
                          '  LMECHREVO_LOG_DIR=' + $(if ($env:LMECHREVO_LOG_DIR) { $env:LMECHREVO_LOG_DIR } else { '(未设置)' })))
    [void]$sb.AppendLine(('日志规模 : {0:N1} MB / {1} 行' -f ($fi.Length / 1MB), $all.Count))
    [void]$sb.AppendLine('')

    foreach ($k in $Patterns.Keys) {
        $hit = @($all | Select-String -Pattern $Patterns[$k] -ErrorAction SilentlyContinue)
        [void]$sb.AppendLine('=' * 72)
        [void]$sb.AppendLine(('## {0}   ({1} 行)' -f $k, $hit.Count))
        [void]$sb.AppendLine('=' * 72)
        if ($hit.Count -eq 0) { [void]$sb.AppendLine('   （无）') }
        elseif ($hit.Count -gt 60) {
            foreach ($h in ($hit | Select-Object -First 40)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
            [void]$sb.AppendLine(('   ... 省略 {0} 行 ...' -f ($hit.Count - 50)))
            foreach ($h in ($hit | Select-Object -Last 10)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
        else { foreach ($h in $hit) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) } }
        [void]$sb.AppendLine('')
    }

    [void]$sb.AppendLine('=' * 72)
    [void]$sb.AppendLine('## ★ 重点：切到【核显模式】的关键动作（已排除 TrayGPU 周期性刷屏）')
    [void]$sb.AppendLine('=' * 72)
    $focus = @($all | Select-String -Pattern 'SwitchGpuMode|target=0|GpuMode -> 0|IGPU_ONLY|TOGGLE_IGPU|IgpuOnly|核显|集显' -ErrorAction SilentlyContinue |
               Where-Object { $_.Line -notmatch 'TrayGPU' })
    if ($focus.Count -eq 0) {
        [void]$sb.AppendLine('   （未发现 —— 请确认在 LM 里点了【核显模式】并选了【重启切换】）')
    }
    else {
        $key = '(SwitchGpuMode|TOGGLE_IGPU|IGPU_ONLY_CONNECT_RB_ON|payload\(s\)|target=0|route verify|NVRAM display byte|OemDisplayMode|Setting/Control)'
        $prio = @($focus | Where-Object { $_.Line -match $key })
        $rest = @($focus | Where-Object { $_.Line -notmatch $key })
        [void]$sb.AppendLine(('   --- 关键动作行（{0} 条，显示全部/最多 60）---' -f $prio.Count))
        foreach ($h in ($prio | Select-Object -Last 60)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        if ($rest.Count -gt 0) {
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine(('   --- 其它核显相关行（共 {0} 条，仅显示最后 20 条）---' -f $rest.Count))
            foreach ($h in ($rest | Select-Object -Last 20)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
    }

    $dir = Split-Path -Parent $outPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Line ('  [OK] 已写入：' + $outPath)
    return $true
}

# ============================== 主流程 ==============================
$desk = [Environment]::GetFolderPath('Desktop')
if (-not $desk) { $desk = $env:USERPROFILE }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'

if ($CollectOnly) {
    Rule
    Line ' 第二步：只采集（不重启 LM、不交互）'
    Rule
    $out = Join-Path $desk ('LM-核显采集-报告-' + $stamp + '.txt')
    $ok = Collect '第二步（系统重启后采集）' $out
    Line ''
    Rule
    if ($ok) { Line (' 完成！请把这个文件发回给开发者：' + $out) } else { Line ' 采集失败，请看上方提示' }
    Rule
    try { Start-Process explorer.exe $desk } catch { }
    exit 0
}

# --- 阶段一 ---
Rule
Line ' 第一步：打开全量日志 + 重启 LM（本步【不采集】）'
Rule
Line ''
[Environment]::SetEnvironmentVariable('LMECHREVO_LOG_LEVEL', 'All', 'User')
$env:LMECHREVO_LOG_LEVEL = 'All'
Line '  [OK] 已设置 LMECHREVO_LOG_LEVEL=All（对新启动的 LM 生效）'
if ([Environment]::GetEnvironmentVariable('LMECHREVO_DIAGNOSTIC_PACK_PATH', 'User')) {
    Line '  [注意] 检测到 LMECHREVO_DIAGNOSTIC_PACK_PATH，它会把诊断包写死到固定文件名，'
    Line '         如非必要建议删除该环境变量。'
}
Line ''

$exe = Find-LMExe
if ($exe) {
    Line ('  找到 L-Mechrevo：' + $exe)
    Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    Start-Process -FilePath $exe
    Line '  [OK] 已重启 LM'
    Start-Sleep -Seconds 8
}
else {
    Line '  [跳过] 未自动重启 LM —— 请【手动完全退出并重新打开】L-Mechrevo。'
}
Line ''
Rule
Line ' ⚠️⚠️ 接下来这一步非常关键 ⚠️⚠️'
Rule
Line ''
Line '  1) 打开 L-Mechrevo 的「设置」页，找到「显卡模式」那一行'
Line '  2) 点一下【核显模式】按钮'
Line '  3) 弹出提示框时 —— 请点【重启切换】！'
Line ''
Line '      ⚠️ 千万不要点【取消】！'
Line '         点【取消】= 整个切换被取消 = 一条指令都不会发出 = 采集不到任何东西'
Line ''
Line '  4) 电脑会自动重启。重启完成后：'
Line '     → 回到这个文件夹，双击【第二步-采集.bat】'
Line '     → 它会在桌面生成报告，把报告发回给开发者即可'
Line ''
Rule
Line ' 说明：日志写在磁盘上（%APPDATA%\MechrevoLite\log.txt），重启不会丢。'
Rule
