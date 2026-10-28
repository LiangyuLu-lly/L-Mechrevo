<#
  GCU 显卡工具箱（中文界面）
  ---------------------------------------------------------------------------
  给"不熟悉电脑的人"用的入口：双击 -> 自动请求管理员 -> 中文菜单 -> 一键出报告。
  它只做只读的事（读 EC / 固件变量 / 注册表 / 设备状态），绝不会改动硬件。

  底下调用的是同目录的 Get-GcuNvramState.ps1（纯 ASCII、只读）。
  本文件是 UTF-8 带 BOM —— 千万不要用会去掉 BOM 的编辑器另存，否则
  Windows PowerShell 5.1 会按 GBK 读取，中文会变乱码甚至语法报错。
#>
[CmdletBinding()]
param(
    [switch]$NoElevate,          # 测试用：已经是管理员、或不想弹 UAC 时加这个
    [switch]$ShowReport          # 只生成报告不显示菜单（给别的脚本调用）
)

$ErrorActionPreference = 'Continue'
$script:Root      = $PSScriptRoot
$script:Engine    = Join-Path $PSScriptRoot 'Get-GcuNvramState.ps1'
$script:PSExe     = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$script:LastLog   = ''

function Write-Cn {
    param([string]$Text, [string]$Color = 'Gray')
    Write-Host $Text -ForegroundColor $Color
}

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object System.Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-DesktopPath {
    $d = [Environment]::GetFolderPath('Desktop')
    if (-not $d -or -not (Test-Path -LiteralPath $d)) { $d = $env:USERPROFILE }
    if (-not $d -or -not (Test-Path -LiteralPath $d)) { $d = $env:TEMP }
    return $d
}

function Get-ReportSettingFile {
    $dir = Join-Path $env:ProgramData 'DgpuSilence'
    try { if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null } } catch { }
    return (Join-Path $dir 'toolbox-report.txt')
}

function Test-ReportEnabled {
    <# Master switch for writing report files. Default ON: a missing/empty file means enabled.
       With it OFF nothing is written to the Desktop; the engines then print to the console only,
       and -Measure still works because snapshots use their own path. #>
    try {
        $f = Get-ReportSettingFile
        if (Test-Path -LiteralPath $f) {
            $v = (Get-Content -LiteralPath $f -Raw).Trim().ToLowerInvariant()
            if ($v -eq 'off' -or $v -eq '0' -or $v -eq 'false') { return $false }
        }
    } catch { }
    return $true
}

function Set-ReportEnabled {
    param([bool]$Enabled)
    try {
        [IO.File]::WriteAllText((Get-ReportSettingFile), $(if ($Enabled) { 'on' } else { 'off' }))
        return $true
    } catch { return $false }
}

function New-ReportPath {
    if (-not (Test-ReportEnabled)) { return '' }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $name = 'GCU显卡报告_{0}_{1}.txt' -f $env:COMPUTERNAME, $stamp
    return (Join-Path (Get-DesktopPath) $name)
}

function Show-Header {
    Write-Host ''
    Write-Cn '=================================================' Cyan
    Write-Cn '            GCU 显卡工具箱  (只读体检)' Cyan
    Write-Cn '=================================================' Cyan
    Write-Cn (" 电脑名称 : {0}" -f $env:COMPUTERNAME)
    Write-Cn (" 当前用户 : {0}" -f [System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
    Write-Cn (" 管理员   : {0}" -f (Test-Admin))
    Write-Cn (" 报告位置 : {0}" -f (Get-DesktopPath))
    Write-Host ''
}

function Show-Usage {
    Write-Host ''
    Write-Cn '【怎么用】' Yellow
    Write-Cn '  1) 双击「双击运行-显卡工具箱.cmd」，弹出"用户账户控制"时点【是】。'
    Write-Cn '  2) 只想看看这台机器的情况  ->  选 1（会在桌面生成一份报告）。'
    Write-Cn '  3) 要试"切到核显档"前后有什么变化  ->  选 2，然后按屏幕提示操作。'
    Write-Cn '  4) 把桌面上生成的"GCU显卡报告_....txt"发给开发者就行。'
    Write-Host ''
    Write-Cn '【注意】' Yellow
    Write-Cn '  * 本工具箱只读取信息，不会改动任何设置。'
    Write-Cn '  * 切换显示模式是由"控制中心/GCU"这个官方软件来做的，本工具箱只负责"前后拍照"。'
    Write-Cn '  * 如果切换后屏幕黑了：等 30 秒；仍黑就长按电源键 10 秒强制关机再开机（不会损坏硬件）。'
    Write-Host ''
}

function Get-ChineseSummary {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return @('（没有拿到输出，请把报告文件发给开发者）') }
    $lines = New-Object System.Collections.ArrayList

    # 判断"这次到底有没有拿到管理员权限"：日志里第一次读固件变量必然先失败再重试成功，
    # 所以不能只看 'privilege not held' 字样（那会产生假警报），要看 elevated 与最终结果。
    if ($Raw -notmatch 'elevated=True') {
        [void]$lines.Add('提示：这次不是以管理员身份运行，很多信息读不到 —— 请重新双击，并在弹窗里点【是】')
    } elseif ($Raw -match 'no usable struct blob') {
        [void]$lines.Add('提示：固件变量没读到（权限或该机型不支持），部分信息缺失')
    }

    $m = [regex]::Match($Raw, 'support \(cmd=3 addr=3\)\s*->\s*(\d+)')
    if ($m.Success) {
        $v = $m.Groups[1].Value
        if ($v -eq '85') { [void]$lines.Add('热切换（不用重启就能切走独显）：【支持】') }
        elseif ($v -eq '170') { [void]$lines.Add('热切换（不用重启就能切走独显）：【不支持】—— 这台只能重启后才生效') }
        else { [void]$lines.Add(('热切换支持标志：{0}（看不懂就交给开发者）' -f $v)) }
    } else {
        [void]$lines.Add('没读到"热切换支持标志"（这台机器可能不提供该接口，或读取被拦截）')
    }

    $m = [regex]::Match($Raw, 'IGPUonly\s*=\s*(\d+)')
    if ($m.Success) {
        $v = $m.Groups[1].Value
        $t = '未知'
        if ($v -eq '0') { $t = '关闭（正常状态）' } elseif ($v -eq '1') { $t = '开启（独显已被切走）' } elseif ($v -eq '2') { $t = '自动' }
        [void]$lines.Add(('当前"核显省电/独显切走"状态：{0}' -f $t))
    }

    $m = [regex]::Match($Raw, 'EC\[2002\]\s*=\s*0x([0-9A-F]{2})')
    if ($m.Success) { [void]$lines.Add(('固件里"本机有独显"的记录：0x{0}（非 00 就是有）' -f $m.Groups[1].Value)) }

    # 显示模式：AMD 是 1/0/2，Intel 是 2/4/1，两套编码不同 —— 所以先认平台再翻译，
    # 翻译不出来就用引擎自己给出的英文原文（引擎已经按平台翻译过一遍）。
    $m = [regex]::Match($Raw, '(?m)OemDisplayMode@\d+\s*=\s*(\d+)\s*->\s*(.+?)\s*$')
    if ($m.Success) {
        $v = $m.Groups[1].Value
        $engineText = $m.Groups[2].Value
        $plat = 'unknown'
        $pm = [regex]::Match($Raw, 'platform \(non-NVIDIA display adapter\):\s*(\w+)')
        if ($pm.Success) { $plat = $pm.Groups[1].Value }
        $t = ''
        if ($plat -eq 'Intel') {
            if ($v -eq '2') { $t = '独显直连（内屏由独显驱动）' }
            elseif ($v -eq '4') { $t = '关闭直连（混合，内屏由核显驱动）' }
            elseif ($v -eq '1') { $t = '核显' }
        } else {
            if ($v -eq '1') { $t = '独显直连（内屏由独显驱动）' }
            elseif ($v -eq '0') { $t = '关闭直连（混合，内屏由核显驱动）' }
            elseif ($v -eq '2') { $t = '核显' }
        }
        if (-not $t) { $t = $engineText }
        [void]$lines.Add(('开机后生效的显示模式（固件里的持久设置）：{0}（{1} 平台，值={2}）' -f $t, $plat, $v))
    }

    # 对比表里那一行：必须按行锚定，否则会跨行吃到下一个字段名（真报告里出现过 True -> nvidia_count）
    $m = [regex]::Match($Raw, '(?m)^\s*nvidia_present\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|')
    if ($m.Success) {
        $b = $m.Groups[1].Value.Trim()
        $a = $m.Groups[2].Value.Trim()
        if ($b -eq $a) {
            if ($a -match 'True') { [void]$lines.Add('切换前后独显都还在系统里（没有被摘掉、也没有被禁用）') }
            else { [void]$lines.Add('切换前后独显都不在系统里') }
        } elseif ($a -match 'False') {
            [void]$lines.Add('切换后独显从系统里消失了（热切换生效）')
        } else {
            [void]$lines.Add(('切换前后独显状态变了：{0} -> {1}' -f $b, $a))
        }
    } else {
        $m2 = [regex]::Match($Raw, '(?m)^\s*nvidia_present\s*=\s*(\w+)')
        if ($m2.Success) { [void]$lines.Add(('现在系统里能不能看到独显（真的在场才算）：{0}' -f $m2.Groups[1].Value)) }
    }
    # 固件侧的"核显档是否真的生效"：85 = 生效，170 = 未生效
    # （实测：设备真正下线那一刻它从 170 变 85）
    $m3 = [regex]::Match($Raw, '(?m)^\s+acpi_status\s+=\s*(\d+)')
    if ($m3.Success) {
        if ($m3.Groups[1].Value -eq '85') { [void]$lines.Add('固件侧的核显档状态：【已生效】（独显已被切走）') }
        elseif ($m3.Groups[1].Value -eq '170') { [void]$lines.Add('固件侧的核显档状态：未生效') }
    }

    if ($lines.Count -eq 0) { [void]$lines.Add('（上面没有识别到关键结论，请把报告文件发给开发者）') }
    return $lines
}

function Invoke-Engine {
    <# 调用只读引擎；报告同时由引擎写入 $ReportPath（即使这里捕获失败，报告也在） #>
    param([string[]]$EngineArgs, [string]$Title, [string]$ReportPath, [string]$EnginePath = '', [string]$Hint = '')

    if (-not $EnginePath) { $EnginePath = $script:Engine }
    if (-not (Test-Path -LiteralPath $EnginePath)) {
        Write-Cn ('找不到引擎文件：{0}' -f $EnginePath) Red
        Write-Cn '请确认工具箱和它依赖的脚本在同一个文件夹里。' Red
        return $null
    }
    Write-Host ''
    Write-Cn ('开始：{0}' -f $Title) Cyan
    if ($Hint) { Write-Cn $Hint DarkGray }
    else { Write-Cn '请稍等 10~30 秒，不要关闭窗口 ...' DarkGray }

    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $EnginePath) + $EngineArgs
    if ($ReportPath) { $all += @('-LogPath', $ReportPath) }
    $script:LastExitCode = $null
    try {
        $out = (& $script:PSExe @all 2>&1 | Out-String)
        $script:LastExitCode = $LASTEXITCODE
        return $out
    } catch {
        Write-Cn ('执行出错：{0}' -f $_.Exception.Message) Red
        return $null
    }
}

function Show-ReportResult {
    param([string]$ReportPath)
    if ($ReportPath -and (Test-Path -LiteralPath $ReportPath)) {
        $size = (Get-Item -LiteralPath $ReportPath).Length
        $script:LastLog = $ReportPath
        Write-Host ''
        Write-Cn '---------------- 报告已生成 ----------------' Green
        Write-Cn ('文件：{0}' -f $ReportPath) Green
        Write-Cn ('大小：{0} 字节' -f $size)
        Write-Cn '把这一份文件发给开发者就行。' Green
        try { Start-Process -FilePath 'explorer.exe' -ArgumentList ('/select,"{0}"' -f $ReportPath) | Out-Null } catch { }
    } else {
        if (-not (Test-ReportEnabled)) {
            Write-Cn '（报告功能已关闭：本次没有生成文件。需要报告就回主菜单选 12 打开。）' Yellow
        } else {
            Write-Cn '没有生成报告文件（可能是权限被拒）。请把屏幕上的内容截图发给开发者。' Yellow
        }
    }
}

function Add-ReportHeader {
    <# 在引擎写出的英文日志前面加中文表头 + 中文摘要。
       写成 UTF-8 带 BOM，记事本打开才不会乱码。 #>
    param([string]$ReportPath, [string]$Action, [string[]]$Summary)
    if (-not $ReportPath -or -not (Test-Path -LiteralPath $ReportPath)) { return }
    try {
        $body = Get-Content -LiteralPath $ReportPath -Raw -Encoding UTF8
        $head = New-Object System.Collections.ArrayList
        [void]$head.Add('==================== GCU 显卡报告 ====================')
        [void]$head.Add(('机器名称 : {0}' -f $env:COMPUTERNAME))
        [void]$head.Add(('当前用户 : {0}' -f [System.Security.Principal.WindowsIdentity]::GetCurrent().Name))
        [void]$head.Add(('生成时间 : {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
        [void]$head.Add(('本次操作 : {0}' -f $Action))
        [void]$head.Add('')
        [void]$head.Add('---- 中文摘要（给使用者看）----')
        foreach ($l in $Summary) { [void]$head.Add('  * ' + $l) }
        [void]$head.Add('')
        [void]$head.Add('---- 原始记录（给开发者看，请不要删）----')
        $all = ($head -join "`r`n") + "`r`n" + $body
        [IO.File]::WriteAllText($ReportPath, $all, (New-Object System.Text.UTF8Encoding($true)))
    } catch {
        Write-Cn ('给报告加中文表头失败（不影响报告内容）：{0}' -f $_.Exception.Message) Yellow
    }
}

function Invoke-HealthCheck {
    $report = New-ReportPath
    $raw = Invoke-Engine -EngineArgs @('-Measure') -Title '体检（读取硬件与固件信息）' -ReportPath $report
    Write-Host ''
    Write-Cn '---------------- 结论摘要 ----------------' Cyan
    $sum = Get-ChineseSummary -Raw $raw
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) }
    Add-ReportHeader -ReportPath $report -Action '只读体检' -Summary $sum
    Show-ReportResult -ReportPath $report
}

function Invoke-CaptureSwitch {
    Write-Host ''
    Write-Cn '这个功能会做三件事：' Yellow
    Write-Cn '  1) 先记录"切换前"的状态；'
    Write-Cn '  2) 等你去官方控制中心把显示模式切到【核显 / iGPU 省电】；'
    Write-Cn '  3) 再记录"切换后"的状态，并打印前后对比。'
    Write-Host ''
    Write-Cn '操作提示：' Yellow
    Write-Cn '  * 打开"控制中心 / Control Center"（GCU），找到显卡模式 / dGPU 省电那一项，选【核显】。'
    Write-Cn '  * 如果找不到那个选项，或者它提示"需要重启"，那这台就不支持免重启热切换。'
    Write-Cn '  * 切换时屏幕可能会黑 1~5 秒，属于正常现象。'
    Write-Host ''
    # 让使用者先声明意图：只靠机器信号分不清"切换没生效"和"使用者其实没切"
    Write-Cn '开始之前，先说一下你等下要做什么：' Yellow
    Write-Cn '  1 = 我要切到【核显 / 独显省电】档'
    Write-Cn '  2 = 我要切回【混合 / 独显直连】档'
    Write-Cn '  3 = 我找不到那个选项'
    $intent = Read-Host '输入 1、2 或 3 再回车'
    $intentText = '未说明'
    if ($intent -eq '1') { $intentText = '切到核显/独显省电档' }
    elseif ($intent -eq '2') { $intentText = '切回混合/独显直连档' }
    elseif ($intent -eq '3') { $intentText = '找不到该选项' }

    $ok = Read-Host '准备好了就按回车开始（输入 q 再回车可取消）'
    if ($ok -eq 'q' -or $ok -eq 'Q') { Write-Cn '已取消。' Yellow; return }

    $report = New-ReportPath
    Write-Cn '提示：切换之后程序会继续观察约 2 分钟（厂商自己的等待上限也是 2 分钟），' DarkGray
    Write-Cn '      这样才能看出"独显是不是真的被切走了"，请耐心等一会儿。' DarkGray
    $raw = Invoke-Engine -EngineArgs @('-CaptureSwitch', '-AutoWait', '-WaitSeconds', '240', '-FollowSeconds', '120') -Title '抓取"切换前后对比"（最多等 4 分钟 + 观察 2 分钟）' -ReportPath $report

    Write-Host ''
    Write-Cn '最后确认一下实际发生了什么（会写进报告，开发者要靠它判断）：' Yellow
    Write-Cn '  1 = 我点了【核显 / 独显省电】，软件没有报错'
    Write-Cn '  2 = 我点了，但软件提示"需要重启"或报错'
    Write-Cn '  3 = 控制中心里找不到该选项'
    Write-Cn '  4 = 我没动过，只是跑了一遍'
    $done = Read-Host '输入 1、2、3 或 4 再回车'
    $doneText = '未说明'
    if ($done -eq '1') { $doneText = '点了核显档，软件未报错' }
    elseif ($done -eq '2') { $doneText = '点了核显档，但提示需要重启或报错' }
    elseif ($done -eq '3') { $doneText = '控制中心里没有该选项' }
    elseif ($done -eq '4') { $doneText = '未做任何切换' }
    Write-Cn ('  已记录：{0} / 实际：{1}' -f $intentText, $doneText) Green

    Write-Host ''
    Write-Cn '---------------- 结论摘要 ----------------' Cyan
    $sumList = New-Object System.Collections.ArrayList
    [void]$sumList.Add(('你刚才的操作：{0}；实际结果：{1}' -f $intentText, $doneText))
    foreach ($l in (Get-ChineseSummary -Raw $raw)) { [void]$sumList.Add($l) }
    foreach ($l in $sumList) { Write-Cn ('  * ' + $l) }
    Add-ReportHeader -ReportPath $report -Action ('切换核显档前后对比；用户操作：{0} -> {1}' -f $intentText, $doneText) -Summary $sumList
    Show-ReportResult -ReportPath $report
}

function Invoke-QuickStatus {
    # 也带 -Measure，摘要里才有"独显还在不在"这类信息；快照写到临时目录，不弄脏桌面
    $tmp = Join-Path $env:TEMP ('gcu_quick_{0}.json' -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
    $raw = Invoke-Engine -EngineArgs @('-Measure', '-SnapshotPath', $tmp) -Title '快速状态（不生成桌面报告）' -ReportPath $null
    Write-Host ''
    Write-Cn '---------------- 结论摘要 ----------------' Cyan
    foreach ($l in (Get-ChineseSummary -Raw $raw)) { Write-Cn ('  * ' + $l) }
    try { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch { }
}

# =====================================================================================
# 启动
# =====================================================================================
if (-not (Test-Admin) -and -not $NoElevate) {
    Write-Host ''
    Write-Cn '需要管理员权限才能读取硬件信息。' Yellow
    Write-Cn '马上会弹出"用户账户控制"窗口，请点【是】。' Yellow
    Write-Host ''
    try {
        Start-Process -FilePath $script:PSExe -Verb RunAs -ErrorAction Stop -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath)
        )
        exit 0
    } catch {
        Write-Cn '没有获得管理员权限（可能点了"否"）。' Red
        Write-Cn '请重新双击，并在弹窗里点【是】。' Red
        [void](Read-Host '按回车键关闭')
        exit 2
    }
}

if ($ShowReport) {
    Invoke-HealthCheck
    exit 0
}

function Invoke-FreeGpu {
    Write-Host ''
    Write-Cn '用途：当控制中心提示「显卡使用中，暂不支持切换」时，用它先把占用独显的程序关掉。' Yellow
    Write-Cn '它会先列出谁在占用，问你确认后才动手；桌面、开始菜单、控制中心本身等关键进程绝不会被关。' Yellow
    Write-Host ''
    $engine = Join-Path $script:Root 'Clear-DgpuOccupants.ps1'
    if (-not (Test-Path -LiteralPath $engine)) {
        Write-Cn ('找不到文件：{0}' -f $engine) Red
        Write-Cn '请确认它和工具箱在同一个文件夹里（压缩包里应该一起有）。' Red
        return
    }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $report = ''   # empty unless the report switch is on
    if (Test-ReportEnabled) { $report = Join-Path (Get-DesktopPath) ('GCU释放占用_{0}_{1}.txt' -f $env:COMPUTERNAME, $stamp) }

    Write-Cn '第一步：查看当前是谁在占用独显（只读，不会关任何东西）...' Cyan
    $raw = Invoke-Engine -EngineArgs @() -Title '查看独显占用情况' -ReportPath $report -EnginePath $engine
    $raw

    Write-Host ''
    Write-Cn '注意：下面这些程序会被强制关闭，程序里没保存的内容会丢失。' Yellow
    Write-Cn '  1 = 关掉"主要占用"的那几个（推荐：≥100MB 或正在用独显的）'
    Write-Cn '  2 = 把能关的全关掉（更彻底，但会关掉更多小程序）'
    Write-Cn '  0 = 先不关，我自己处理'
    $pick = Read-Host '输入 1、2 或 0 再回车'
    if ($pick -eq '0' -or $pick -eq '') {
        Write-Cn '已跳过，没有关闭任何程序。' Yellow
        Show-ReportResult -ReportPath $report
        return
    }

    $killArgs = @('-Kill', '-Yes')
    $tier = '主要占用'
    if ($pick -eq '2') { $killArgs += '-All'; $tier = '全部可关闭的' }
    Write-Host ''
    Write-Cn ('正在关闭【{0}】程序，请稍等...' -f $tier) Cyan
    $raw2 = Invoke-Engine -EngineArgs $killArgs -Title ('关闭占用独显的程序（{0}）' -f $tier) -ReportPath $report -EnginePath $engine
    $raw2

    $sum = New-Object System.Collections.ArrayList
    [void]$sum.Add(('本次操作：释放独显占用（{0}档）' -f $tier))
    if ($raw2 -match 'none - go back') {
        [void]$sum.Add('独显已经空出来了：现在回控制中心点【开启】试一下。')
    } elseif ($raw2 -match 'still hold the dGPU') {
        [void]$sum.Add('还有程序占着独显（详见报告）；可以再选一次 2，或者重启一次再试。')
    } else {
        [void]$sum.Add('详见报告文件。')
    }
    [void]$sum.Add('如果释放占用后控制中心仍然拒绝，重启电脑一定能清空所有占用。')
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) Green }
    Add-ReportHeader -ReportPath $report -Action ('释放独显占用（{0}档）' -f $tier) -Summary $sum
    Show-ReportResult -ReportPath $report
}

function Invoke-BeforeSnapshot {
    Write-Host ''
    Write-Cn '这一步会在"切换前"拍一张快照并记住它，然后请你把本工具关掉再去切换。' Cyan
    Write-Cn '为什么要关掉：本工具运行时会不停读取显卡/EC 状态，而控制中心切换时也要读同一处，' Yellow
    Write-Cn '两边抢会让控制中心误判"没切成功"，于是它自己把设置回滚（机主实测过）。' Yellow
    Write-Host ''
    $report = New-ReportPath
    $raw = Invoke-Engine -EngineArgs @('-Measure','-MarkBaseline') -Title '拍"切换前"快照' -ReportPath $report
    Write-Host ''
    Write-Cn '---------------- 当前状态摘要 ----------------' Cyan
    $sum = Get-ChineseSummary -Raw $raw
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) }
    Add-ReportHeader -ReportPath $report -Action '切换前快照' -Summary $sum
    Write-Host ''
    Write-Cn '========================================================' Green
    Write-Cn ' 接下来请这样做：' Green
    Write-Cn '   1) 关掉本工具窗口（按 0 退出，或直接关掉）' Green
    Write-Cn '   2) 打开控制中心 -> 显卡模式 -> 核显省电模式 -> 选【开启】' Green
    Write-Cn '   3) 等它提示成功（或提示需要重启）' Green
    Write-Cn '   4) 重新双击本工具，选 3 看对比结果' Green
    Write-Cn '========================================================' Green
    Show-ReportResult -ReportPath $report
}

function Invoke-AfterCompare {
    Write-Host ''
    Write-Cn '这一步会把"现在"和上次拍的快照做对比（不用你输入路径）。' Cyan
    $report = New-ReportPath
    $raw = Invoke-Engine -EngineArgs @('-Measure', '-CompareTo', 'last') -Title '切换后对比' -ReportPath $report
    Write-Host ''
    Write-Cn '---------------- 结论摘要 ----------------' Cyan
    $sum = Get-ChineseSummary -Raw $raw
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) }
    Add-ReportHeader -ReportPath $report -Action '切换后对比' -Summary $sum
    Show-ReportResult -ReportPath $report
}

function Invoke-SilenceDgpu {
    <# 软件层静默：直接 PnP 禁用（更彻底时移除）NVIDIA 设备。
       这一层不经过控制中心，所以不受"显卡使用中，暂不支持切换"的限制，任何机型都能用。
       引擎自带门控（例如当前内屏由独显驱动时会拒绝，避免黑屏）与自动回滚。 #>
    param([switch]$Restore)
    Write-Host ''
    $engine = Join-Path $script:Root 'Invoke-DgpuSilence.ps1'
    if (-not (Test-Path -LiteralPath $engine)) {
        Write-Cn ('找不到文件：{0}' -f $engine) Red
        Write-Cn '请确认它和工具箱在同一个文件夹里。' Red
        return
    }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    if ($Restore) {
        Write-Cn '这一步会把独显恢复回来（重新启用设备）。' Cyan
    } else {
        Write-Cn '这一步会把独显"禁用"掉：程序看不到它、也不能用它。' Cyan
        Write-Cn '它的好处是不经过控制中心，所以不会被"显卡使用中"挡住。' Cyan
        Write-Cn '随时可以用菜单里的【还原独显】恢复到现在的样子。' Cyan
    }
    Write-Host ''
    $report = ''   # empty unless the report switch is on
    if (Test-ReportEnabled) { $report = Join-Path (Get-DesktopPath) ('GCU{0}_{1}_{2}.txt' -f $(if ($Restore) { '还原独显' } else { '静默独显' }), $env:COMPUTERNAME, $stamp) }

    Write-Cn '第一步：先看看现在的状态（只读，不改任何东西）...' Cyan
    $st = Invoke-Engine -EngineArgs @('-Action', 'Status') -Title '查看独显与显示拓扑' -ReportPath $report -EnginePath $engine
    $st

    if (-not $Restore) {
        Write-Host ''
        Write-Cn '注意：如果上面显示"内屏当前由独显驱动"，禁用会导致黑屏，程序会自动拒绝（这是保护）。' Yellow
        Write-Cn '确认要继续禁用独显吗？会强制关闭正在使用独显的程序。' Yellow
        $ans = Read-Host '输入 YES 再回车继续（其它任何输入都会取消）'
        if ($ans -ne 'YES') { Write-Cn '已取消，没有做任何改动。' Yellow; Show-ReportResult -ReportPath $report; return }
    }

    $args = @('-Action', $(if ($Restore) { 'Enable' } else { 'Disable' }))
    if (-not $Restore) { $args += @('-Force') }
    $out = Invoke-Engine -EngineArgs $args -Title $(if ($Restore) { '还原独显' } else { '静默独显（禁用设备）' }) -ReportPath $report -EnginePath $engine
    $out
    $code = $script:LastExitCode

    $sum = New-Object System.Collections.ArrayList
    if ($Restore) { [void]$sum.Add('本次操作：还原独显') } else { [void]$sum.Add('本次操作：静默独显（软件层禁用）') }
    switch ($code) {
        0 { [void]$sum.Add($(if ($Restore) { '已恢复：独显重新可用。' } else { '成功：独显已被禁用，程序看不到它了。想恢复就点菜单里的【还原独显】。' })) }
        2 { [void]$sum.Add('被安全门控拒绝（最常见原因：当前内屏由独显驱动，禁用会黑屏）。没有改动任何东西。') }
        3 { [void]$sum.Add('还有程序正在使用独显，没有强制执行。可以用菜单【释放独显占用】先关掉它们再试。') }
        4 { [void]$sum.Add('禁用失败（详见报告）。') }
        5 { [void]$sum.Add('还原失败（详见报告）。') }
        default { [void]$sum.Add(('引擎退出码 {0}，详见报告。' -f $code)) }
    }
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) Green }
    Add-ReportHeader -ReportPath $report -Action $(if ($Restore) { '还原独显' } else { '静默独显（软件层禁用）' }) -Summary $sum
    Show-ReportResult -ReportPath $report
}

function Invoke-HotSwitch {
    <# 硬件层热切：直接写 ACPI 选择子把独显切走/切回，完全不经过控制中心。
       只在 support=85 的机型上有效；成功与否由三项硬判据验证，失败会自动回滚。 #>
    param([switch]$Restore)
    Write-Host ''
    $engine = Join-Path $script:Root 'Invoke-GpuHotSwitch.ps1'
    if (-not (Test-Path -LiteralPath $engine)) {
        Write-Cn ('找不到文件：{0}' -f $engine) Red
        Write-Cn '请确认它和工具箱在同一个文件夹里（压缩包里应该一起有）。' Red
        return
    }
    if ($Restore) {
        Write-Cn '这一步会把刚才切走的独显切回来（写入 OFF 选择子并重启 NVIDIA 服务）。' Cyan
    } else {
        Write-Cn '这一步直接写固件接口把独显切走，不需要控制中心，也不需要重启。' Cyan
        Write-Cn '只在该机型支持时有效（下面先做只读检查）；成功与否会自动验证。' Cyan
    }
    Write-Host ''
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $report = ''   # empty unless the report switch is on
    if (Test-ReportEnabled) { $report = Join-Path (Get-DesktopPath) ('GCU{0}_{1}_{2}.txt' -f $(if ($Restore) { '热切还原' } else { '热切静默' }), $env:COMPUTERNAME, $stamp) }

    Write-Cn '第一步：只读检查（不会写任何东西）...' Cyan
    $st = Invoke-Engine -EngineArgs @('-Action', 'Status') -Title '检查热切换能力与当前状态' -ReportPath $report -EnginePath $engine
    $st
    if ($script:LastExitCode -ne 0) {
        Write-Host ''
        Write-Cn '读不到固件接口（多数是没以管理员运行）。请重新双击工具箱并在弹窗里点【是】。' Yellow
        Write-Cn '若确认是管理员仍读不到，说明这台机器的固件不提供该接口，请改用菜单 7（软件层静默）。' Yellow
        Show-ReportResult -ReportPath $report
        return
    }

    if (-not $Restore) {
        Write-Host ''
        Write-Cn '确认要继续吗？独显会被切走，期间屏幕可能闪一下；想恢复就用菜单 10。' Yellow
        Write-Cn '正在使用独显的程序（游戏等）请先保存，否则会丢进度。' Yellow
        $ans = Read-Host '输入 YES 再回车继续（其它输入取消）'
        if ($ans -ne 'YES') { Write-Cn '已取消，没有做任何写入。' Yellow; Show-ReportResult -ReportPath $report; return }
    }

    if ($Restore) {
        # A device node DELETED by menu 11's thorough mode (pnputil /remove-device /force) cannot be
        # brought back by an ACPI write: the PCI bus must be re-scanned. Measured failure without
        # this: present=False, phantom=0, nvidia device empty, firmware already 170, restore times out.
        $silEngine = Join-Path $script:Root 'Invoke-DgpuSilence.ps1'
        if (Test-Path -LiteralPath $silEngine) {
            Write-Cn '先重新扫描硬件并恢复设备（若之前用过"强制静默·彻底模式"，这一步是必须的）...' Cyan
            $pre = Invoke-Engine -EngineArgs @('-Action', 'Enable') -Title '第一步：重新扫描并恢复独显设备' -ReportPath $report -EnginePath $silEngine
            $pre
        }
    }

    $hsArgs = @('-Action', $(if ($Restore) { 'Restore' } else { 'Silence' }), '-Yes')
    $out = Invoke-Engine -EngineArgs $hsArgs -Title $(if ($Restore) { '热切还原' } else { '热切静默（写入固件接口）' }) -ReportPath $report -EnginePath $engine -Hint '正在写入：成功会在 1~2 秒内立刻提示；只有在等固件生效时才会逐秒显示进度（上限 120 秒）。请不要关闭窗口 ...'
    $out
    $code = $script:LastExitCode

    $sum = New-Object System.Collections.ArrayList
    [void]$sum.Add($(if ($Restore) { '本次操作：热切还原（切回独显）' } else { '本次操作：热切静默（免重启切走独显）' }))
    switch ($code) {
        0 { [void]$sum.Add($(if ($Restore) { '成功：独显已经回来了。' } else { '成功：独显已被切走（固件状态 85 + 驱动失联 + 设备不在场，三项都确认）。想恢复用菜单 10。' })) }
        2 { [void]$sum.Add('被拒绝：这台机器不支持免重启热切（support 不是 85），或当前内屏由独显驱动（切走会黑屏）。没有写入任何东西。') }
        3 { [void]$sum.Add('没有确认，未做任何写入。') }
        4 { [void]$sum.Add('写入后等待验证超时，程序已自动回滚（把独显切回来了）。详见报告。') }
        5 { [void]$sum.Add('还原已写入但未在超时内确认；重启一次电脑一定能恢复。') }
        default { [void]$sum.Add(('引擎退出码 {0}，详见报告。' -f $code)) }
    }
    foreach ($l in $sum) { Write-Cn ('  * ' + $l) Green }
    Add-ReportHeader -ReportPath $report -Action $(if ($Restore) { '热切还原' } else { '热切静默' }) -Summary $sum
    Show-ReportResult -ReportPath $report
}

function Invoke-ForceSilence {
    <# 强制静默（彻底）：一次点击完成三层。
         1) 清占用        —— 结束占用独显的进程（系统/桌面等保护名单除外）
         2) 硬件层切走    —— 仅 support=85 的机型；写 ACPI 选择子把独显断电/摘除（三项验证，失败自动回滚）
         3) 软件层禁用    —— PnP 禁用（可选彻底模式：再把设备节点移除，程序连枚举都看不到）
       顺序按厂商的做法：先清占用 → 再固件切走 → 最后设备层兜底。 #>
    Write-Host ''
    Write-Cn '这一步是"强制静默"：即使有程序正在使用独显，也会先把它们关掉，再让独显消失。' Yellow
    Write-Cn '  * 会强制关闭占用独显的程序，未保存的内容会丢失；' Yellow
    Write-Cn '  * 系统关键进程（桌面、开始菜单、安全软件、本工具）绝不会被关；' Yellow
    Write-Cn '  * 想恢复：先按菜单 8（恢复设备），必要时再按菜单 10（热切还原）。' Yellow
    Write-Host ''
    Write-Cn '彻底模式会在禁用之后把设备节点也移除（程序连枚举都看不到）；' Cyan
    Write-Cn '代价是它更依赖重启恢复（重启后设备会自己回来）。' Cyan
    $th = Read-Host '输入 YES 使用彻底模式；直接回车 = 标准模式（只禁用，不移除）'
    $thorough = ($th -eq 'YES')
    $ans = Read-Host '确认开始强制静默？输入 YES 再回车（其它输入取消）'
    if ($ans -ne 'YES') { Write-Cn '已取消，没有做任何改动。' Yellow; return }

    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $report = ''   # empty unless the report switch is on
    if (Test-ReportEnabled) { $report = Join-Path (Get-DesktopPath) ('GCU强制静默_{0}_{1}.txt' -f $env:COMPUTERNAME, $stamp) }
    $sum = New-Object System.Collections.ArrayList
    [void]$sum.Add(('本次操作：强制静默（{0}模式）' -f $(if ($thorough) { '彻底' } else { '标准' })))

    # ---- step 1: clear the occupants -------------------------------------------------
    $occEngine = Join-Path $script:Root 'Clear-DgpuOccupants.ps1'
    $silEngine = Join-Path $script:Root 'Invoke-DgpuSilence.ps1'
    $hsEngine = Join-Path $script:Root 'Invoke-GpuHotSwitch.ps1'
    if (-not (Test-Path -LiteralPath $silEngine)) {
        Write-Cn ('找不到文件：{0}' -f $silEngine) Red
        return
    }
    Write-Host ''
    $o1 = Invoke-Engine -EngineArgs @('-Kill', '-Yes') -Title '第 1/3 步：释放独显占用（主要占用档）' -ReportPath $report -EnginePath $occEngine
    $o1
    $occCode = $script:LastExitCode
    [void]$sum.Add($(if ($occCode -eq 0) { '第 1 步：主要占用已清空。' } else { '第 1 步：仍有占用（稍后会视情况再清一次）。' }))

    # ---- step 2: hardware layer, when the firmware supports it ------------------------
    $hwNote = '第 2 步：本机固件不支持免重启热切（support 不是 85），跳过硬件层。'
    if ((Test-Path -LiteralPath $hsEngine) -and (Test-Path -LiteralPath (Join-Path $script:Root 'Get-GcuNvramState.ps1'))) {
        Write-Host ''
        $o2 = Invoke-Engine -EngineArgs @('-Action', 'Status') -Title '第 2/3 步：检查固件是否支持热切' -ReportPath $report -EnginePath $hsEngine
        $o2
        if ($o2 -match '(?m)^\s*support\s+:\s*85') {
            Write-Cn '本机支持硬件层热切，开始把独显切走（最多等 120 秒，失败会自动回滚）...' Cyan
            $o3 = Invoke-Engine -EngineArgs @('-Action', 'Silence', '-Yes') -Title '第 2/3 步：硬件层切走独显（写入固件接口）' -ReportPath $report -EnginePath $hsEngine
            $o3
            switch ($script:LastExitCode) {
                0 { $hwNote = '第 2 步：硬件层成功 —— 独显已被固件切走（三项判据都确认）。' }
                4 { $hwNote = '第 2 步：硬件层验证超时，已自动回滚（未做断电）。' }
                default { $hwNote = ('第 2 步：硬件层退出码 {0}。' -f $script:LastExitCode) }
            }
        }
    } else {
        $hwNote = '第 2 步：缺少热切引擎文件，跳过硬件层。'
    }
    [void]$sum.Add($hwNote)

    # ---- step 3: software layer as the guarantee -------------------------------------
    Write-Host ''
    $a3 = @('-Action', 'Disable', '-Force')
    if ($thorough) { $a3 += '-RemoveDevice' }
    $o4 = Invoke-Engine -EngineArgs $a3 -Title ('第 3/3 步：软件层禁用独显{0}' -f $(if ($thorough) { '（彻底：含移除设备节点）' } else { '' })) -ReportPath $report -EnginePath $silEngine
    $o4
    $code3 = $script:LastExitCode

    if ($code3 -eq 3 -and -not $thorough) {
        # occupied again -> clear everything and retry once
        Write-Cn '还有占用挡住了禁用，先把能关的全关掉再试一次...' Yellow
        $o5 = Invoke-Engine -EngineArgs @('-Kill', '-All', '-Yes') -Title '补清占用（全部可关闭的）' -ReportPath $report -EnginePath $occEngine
        $o5
        $o4 = Invoke-Engine -EngineArgs $a3 -Title '重试：软件层禁用独显' -ReportPath $report -EnginePath $silEngine
        $o4
        $code3 = $script:LastExitCode
    }

    switch ($code3) {
        0 { [void]$sum.Add($(if ($thorough) { '第 3 步：成功 —— 设备已禁用并移除，程序看不到也调不动它。' } else { '第 3 步：成功 —— 设备已禁用，程序看不到也调不动它。' })) }
        2 { [void]$sum.Add('第 3 步：被安全门控拒绝（最常见：当前内屏由独显驱动，禁用会黑屏）。没有改动。') }
        3 { [void]$sum.Add('第 3 步：仍有程序占用独显，禁用没能完成。建议重启后再用菜单 11。') }
        4 { [void]$sum.Add('第 3 步：禁用失败，详见报告。') }
        default { [void]$sum.Add(('第 3 步：退出码 {0}，详见报告。' -f $code3)) }
    }
    [void]$sum.Add('恢复顺序：菜单 8（恢复设备）→ 需要时菜单 10（热切还原）。')

    foreach ($l in $sum) { Write-Cn ('  * ' + $l) Green }
    Add-ReportHeader -ReportPath $report -Action ('强制静默（{0}模式）' -f $(if ($thorough) { '彻底' } else { '标准' })) -Summary $sum
    Show-ReportResult -ReportPath $report
}

Show-Header
while ($true) {
    Write-Cn '---------------- 请选择 ----------------' Cyan
    Write-Cn '  1) 体检并生成报告到桌面（推荐）'
    Write-Cn '  2) 【推荐】切换前拍快照（然后关掉本工具，去控制中心切换）'
    Write-Cn '  3) 切换后对比（自动和上次拍的快照比较）'
    Write-Cn '  4) 实时抓取切换过程（可能干扰控制中心，成功率低）'
    Write-Cn '  5) 释放独显占用（控制中心提示"显卡使用中"时用）'
    Write-Cn '  6) 只看一下状态（不生成报告）'
    Write-Cn '  7) 静默独显：禁用设备（立即生效，不经过控制中心）'
    Write-Cn '  8) 还原独显：恢复被禁用的设备'
    Write-Cn '  9) 热切静默：免重启切走独显（不依赖控制中心，仅部分机型）'
    Write-Cn ' 10) 热切还原：把切走的独显切回来'
    Write-Cn ' 11) 强制静默（彻底）：清占用 + 软硬双层，就算有程序在用也做'
    Write-Cn (' 12) 报告开关：当前【{0}】（切换"是否在桌面生成报告文件"）' -f $(if (Test-ReportEnabled) { '开' } else { '关' }))
    Write-Cn '  ?) 使用说明'
    Write-Cn '  0) 退出'
    $c = Read-Host '输入数字后按回车'
    switch ($c) {
        '1' { Invoke-HealthCheck }
        '2' { Invoke-BeforeSnapshot }
        '3' { Invoke-AfterCompare }
        '4' { Invoke-CaptureSwitch }
        '5' { Invoke-FreeGpu }
        '6' { Invoke-QuickStatus }
        '7' { Invoke-SilenceDgpu }
        '8' { Invoke-SilenceDgpu -Restore }
        '9' { Invoke-HotSwitch }
        '10' { Invoke-HotSwitch -Restore }
        '11' { Invoke-ForceSilence }
        '12' {
            $now = Test-ReportEnabled
            $new = -not $now
            if (Set-ReportEnabled -Enabled $new) {
                Write-Cn ('报告功能已{0}。（{1}）' -f $(if ($new) { '打开' } else { '关闭' }), $(if ($new) { '以后每次操作都会在桌面生成报告文件' } else { '以后不再生成文件，只显示在屏幕上；体检/快照对比仍然可用' })) Green
            } else {
                Write-Cn '设置保存失败（权限问题），本次状态未改变。' Red
            }
        }
        '?' { Show-Usage }
        '0' { Write-Cn '已退出。'; exit 0 }
        $null { Write-Cn '（没有输入，自动退出）' Yellow; exit 0 }
        default { Write-Cn '请输入 1~12、? 或 0。' Yellow }
    }
    Write-Host ''
    Write-Cn ('（最近的报告：{0}）' -f $(if ($script:LastLog) { $script:LastLog } else { '还没生成' }))
    Write-Host ''
}
