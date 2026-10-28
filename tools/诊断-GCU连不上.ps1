#Requires -Version 5.1
<#
    诊断-GCU连不上.ps1
    ------------------------------------------------------------------
    用途：极光Pro23（Gen40 / JiguangPro GM5AR0O）"GCU 连不上"现场取证。
          失败形态：MechrevoHw 订阅失败: The MQTT client is disconnected.
          （= 13688 上有进程回了 CONNACK 成功、随后立刻掐断会话）
          已有只读节：②B GCU 侧进程/启动时间/子进程树；
                      ③B 13688 的 ESTABLISHED 对端与对端 PID 详情。
          2026-10-09 新增两节（同样只读）：
                      ⑦B 显示枚举 —— QueryDisplayConfig 活动路径（输出接口类型+友好名）、
                          root\wmi EDID 标识/输出技术、Win32_VideoController 刷新率、
                          Screen.AllScreens 活动屏，并给出"内屏/eDP vs 外接/虚拟屏"分类结论；
                          用途：判断极光X"多显示器刷新率"与"GCU 连不上"时到底挂了几块屏。
                      ⑦C GCU/broker 侧日志 —— 只取
                          %ProgramFiles%\L-Mechrevo\GCU\<实际子目录>（AiStoneService/AiStageService）
                          及其子目录、以及 %ProgramData%\L-Mechrevo\logs 下 *.log 的末尾若干行
                          （最多 6 个文件 / 每文件最多 80 行），看 broker 侧是否持有 UWPClient_4、为何断。

    默认行为：只读采集。不加参数运行时，不启动/停止任何服务、
              不写注册表、不写系统目录、不删除任何文件。

    2026-10-09 再升级（本次）：
                      ⑨ 后端健康判定 —— 放在 ⑧ 自动结论之前，只读，把
                          「服务状态/StartType → 后端进程（GCUService 是否在跑）
                            → 13688 监听者是不是 GCUBridge → 13688 会话持有者
                            → 运行目录 exe 大小/版本/哈希」归纳成【一句结论 + 该做什么】。
                          针对的故障形态：broker 能回 CONNACK 成功、却断在 Subscribe
                          （⇒ 问题在后端，不是 clientId 被占）。

    可选参数 -Fix：仅在显式指定且当前进程是管理员时，做且只做以下两类操作：
              (1) sc.exe config GCUBridge start= auto
              (2) 重启后端服务：对【实际存在】的 GCUBridge / GCUService 执行
                  Restart-Service（服务为 Stopped 时改 Start-Service），
                  并打印修复前/后的 Status/StartType 与关键进程。
          不做别的：不重装 GCU、不拷文件、不改注册表、不动 OpenRevo、
          不删文件、不自动提权、不弹 UAC。

    用法：
      powershell -NoProfile -ExecutionPolicy Bypass -File .\诊断-GCU连不上.ps1
      powershell -NoProfile -ExecutionPolicy Bypass -File .\诊断-GCU连不上.ps1 -Fix   (需管理员)

    输出：屏幕打印 + 同内容写入桌面 GCU诊断-<时间戳>.txt，请回传该文件（隐私自行打码）。

    另见：tools\GCU抓包.ps1（一键诊断抓包：回环抓包 + GCU 底座日志 + 装后自检 + 打包成 zip）。
#>
[CmdletBinding()]
param(
    [switch]$Fix,
    [string]$OutFile
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

# ---------------------------------------------------------------- 基础设施
$script:Report = New-Object System.Text.StringBuilder
$script:Hints  = New-Object System.Collections.ArrayList

# 变体基线（仓库实测值：大小 / SHA256 / 文件版本）
$GcuBaselines = @(
    (New-Object psobject -Property @{ Name = '30 系 (Gen30)'; Size = 1769216;  Hash = '8C490C54A15977F7421AC819A54370CC40E5C9E1DCAC94177FBFE1821DE94A2F'; Ver = '1.0.2.70' }),
    (New-Object psobject -Property @{ Name = '40 系 (Gen40)'; Size = 14447864; Hash = 'A2B7D81ECF5C1DBDFC19738F17BA5E01E884D1B3688BEB668858727307E2A318'; Ver = '1.0.2.70' }),
    (New-Object psobject -Property @{ Name = '50 系 (Gen50)'; Size = 17110576; Hash = '4F0F74E60D65D1810F74AAA89C41EC349C932CFF0B56DBB903887206929AA5D1'; Ver = '1.2.0.0' })
)
$BridgeBaseSize = 682208   # 底座那份 GCUBridge.exe（随 LM 0.290.8 安装器）
$Bridge30Size   = 77320    # 30 系 payload 里那份 GCUBridge.exe（会把底座顶掉）

# 采集到的关键事实（供 ⑧ 结论提示使用）
$script:F = @{
    IsAdmin        = $false
    BridgeStatus   = ''
    BridgeStart    = ''
    SvcPresent     = $false
    SvcBridgePresent = $false
    SvcServicePresent = $false
    SvcServiceStatus  = ''
    SvcServiceStart   = ''
    GcuServiceRunning = $false
    GcuBridgeRunning  = $false
    ListenerIsBridge  = $false
    EstHolderNames    = ''
    EstHolderCount    = 0
    BackendVerdict    = ''
    PortListening  = $null
    ListenerName   = ''
    ListenerSize   = 0
    ListenerPath   = ''
    SvcExeFacts    = $null
    BrgExeFacts    = $null
    PortReadFail   = $false
    RegVariant     = ''
    VariantMatch   = ''
    VariantMismatch= $false
    BridgeIs30     = $false
    OpenRevoDir    = $false
    OpenRevoProc   = $false
    OpenRevoTakeover = $false
    RunRenamed     = $false
    LogMissing     = $false
    ListenerPathUnknown = $false
    FwBlock        = ''
    FixApplied     = ''
}

function Say {
    param([string]$Text = '')
    Write-Host $Text
    [void]$script:Report.AppendLine($Text)
}

function Section {
    param([string]$Title)
    Say ''
    Say ('=' * 70)
    Say $Title
    Say ('=' * 70)
}

function Get-FileFacts {
    # 读取单个文件的大小/版本/时间/SHA256，失败不中断整体流程
    param([string]$Label, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Say ("  {0}：不存在" -f $Label)
        Say ("    预期路径：{0}" -f $Path)
        return $null
    }
    $fi = $null
    try { $fi = Get-Item -LiteralPath $Path -ErrorAction Stop }
    catch { Say ("  [该项读取失败] {0}：{1}" -f $Label, $_.Exception.Message); return $null }

    $hash = '(读取失败)'
    try { $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash }
    catch { Say ("    [SHA256 读取失败] {0}" -f $_.Exception.Message) }

    $ver = '(未知)'
    try { if ($fi.VersionInfo.FileVersion) { $ver = $fi.VersionInfo.FileVersion } } catch { }

    Say ("  {0}：" -f $Label)
    Say ("    路径   ：{0}" -f $fi.FullName)
    Say ("    大小   ：{0} 字节" -f $fi.Length)
    Say ("    版本   ：{0}" -f $ver)
    Say ("    时间   ：{0}" -f $fi.LastWriteTime)
    Say ("    SHA256 ：{0}" -f $hash)
    if ($Label -like 'GCUService.exe*')      { $script:F.SvcExeFacts = New-Object psobject -Property @{ Size = $fi.Length; Hash = [string]$hash; Version = [string]$ver; Path = $fi.FullName } }
    elseif ($Label -like 'GCUBridge.exe*')   { $script:F.BrgExeFacts = New-Object psobject -Property @{ Size = $fi.Length; Hash = [string]$hash; Version = [string]$ver; Path = $fi.FullName } }
    return (New-Object psobject -Property @{
        Size = $fi.Length; Hash = $hash; Version = $ver; Time = $fi.LastWriteTime; Path = $fi.FullName
    })
}

# ================================================================ 抬头
Say '极光Pro23（Gen40 / JiguangPro GM5AR0O）"GCU 连不上"现场诊断报告'
Say '失败形态参考：MechrevoHw 订阅失败: The MQTT client is disconnected.'
if ($Fix) {
    Say '运行模式：只读采集 + 最小修复(-Fix)'
} else {
    Say '运行模式：只读采集（未加 -Fix：不会启动/停止服务、不写注册表、不改任何文件）'
}

# ================================================================ ① 环境
Section '① 环境'
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    Say ("  操作系统 ：{0}" -f $os.Caption)
    Say ("  版本构建 ：{0} (Build {1}) / {2}" -f $os.Version, $os.BuildNumber, $os.OSArchitecture)
} catch {
    Say ("  [该项读取失败] 操作系统：{0}" -f $_.Exception.Message)
    try {
        $os2 = Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
        Say ("  操作系统 ：{0} {1}" -f $os2.Caption, $os2.Version)
    } catch { Say ("  [该项读取失败] 操作系统(WMI 兜底)：{0}" -f $_.Exception.Message) }
}
try {
    Say ("  计算机名 ：{0}" -f $env:COMPUTERNAME)
    Say ("  当前用户 ：{0}" -f $env:USERNAME)
    Say ("  采集时间 ：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    Say ("  PowerShell：{0} ({1})" -f $PSVersionTable.PSVersion, $PSVersionTable.PSEdition)
} catch { Say ("  [该项读取失败] 计算机/用户：{0}" -f $_.Exception.Message) }

try {
    $wid = [Security.Principal.WindowsIdentity]::GetCurrent()
    $wpr = New-Object Security.Principal.WindowsPrincipal($wid)
    $script:F.IsAdmin = $wpr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Say ("  管理员   ：{0}" -f $(if ($script:F.IsAdmin) { '是' } else { '否（只读采集不受影响；-Fix 需要管理员）' }))
} catch { Say ("  [该项读取失败] 管理员判定：{0}" -f $_.Exception.Message) }

try {
    $lmExe = Join-Path $env:ProgramFiles 'L-Mechrevo\L-Mechrevo.exe'
    if (Test-Path -LiteralPath $lmExe) {
        $lmInfo = Get-Item -LiteralPath $lmExe
        Say ("  LM 主程序：{0}" -f $lmInfo.FullName)
        Say ("    FileVersion={0}  大小={1} 字节  时间={2}" -f $lmInfo.VersionInfo.FileVersion, $lmInfo.Length, $lmInfo.LastWriteTime)
    } else { Say ("  LM 主程序：不存在（{0}）" -f $lmExe) }
} catch { Say ("  [该项读取失败] LM 主程序：{0}" -f $_.Exception.Message) }

foreach ($rk in 'HKLM:\SOFTWARE\L-Mechrevo', 'HKLM:\SOFTWARE\WOW6432Node\L-Mechrevo') {
    try {
        if (-not (Test-Path -LiteralPath $rk)) { Say ("  注册表 {0}：不存在" -f $rk); continue }
        $rp = Get-ItemProperty -LiteralPath $rk -ErrorAction Stop
        $names = @($rp.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { $_.Name })
        Say ("  注册表 {0}：含值 {1}" -f $rk, ($names -join ', '))
        foreach ($k in 'Version', 'InstallVersion', 'ProductVersion', 'DisplayVersion') {
            if ($names -contains $k) { Say ("    {0} = {1}" -f $k, $rp.$k) }
        }
    } catch { Say ("  [该项读取失败] 注册表 {0}：{1}" -f $rk, $_.Exception.Message) }
}

# ================================================================ ② 服务
Section '② 服务状态（GCUBridge / GCUService）'
foreach ($svcName in 'GCUBridge', 'GCUService') {
    try {
        $s = Get-Service -Name $svcName -ErrorAction Stop
        Say ("  {0}：Status={1}  StartType={2}" -f $svcName, $s.Status, $s.StartType)
        $script:F.SvcPresent = $true
        if ($svcName -eq 'GCUBridge') {
            $script:F.SvcBridgePresent = $true
            $script:F.BridgeStatus = [string]$s.Status
            $script:F.BridgeStart  = [string]$s.StartType
        } elseif ($svcName -eq 'GCUService') {
            $script:F.SvcServicePresent = $true
            $script:F.SvcServiceStatus  = [string]$s.Status
            $script:F.SvcServiceStart   = [string]$s.StartType
        }
    } catch {
        Say ("  {0}：未安装或读取失败（{1}）" -f $svcName, $_.Exception.Message)
    }
    Say ("  --- sc.exe qc {0} （服务配置；不存在时下方为错误信息，属正常）---" -f $svcName)
    try {
        $qc = & sc.exe qc $svcName 2>&1
        $qcLines = @($qc | ForEach-Object { ([string]$_).TrimEnd() } | Where-Object { $_ -ne '' })
        if ($qcLines.Count -eq 0) { Say ("    （无输出，exit={0}）" -f $LASTEXITCODE) }
        foreach ($l in $qcLines) { Say ("    " + $l) }
    } catch { Say ("    [该项读取失败] sc.exe qc {0}：{1}" -f $svcName, $_.Exception.Message) }
}

# ================================================================ ②B GCU 侧进程与启动时间
Section '②B GCU 侧进程与启动时间（回答"故障当时后端在不在"）'
$gcuProcNames = @('GCUBridge', 'GCUService')
foreach ($pn in $gcuProcNames) {
    $procs = @()
    try { $procs = @(Get-Process -Name $pn -ErrorAction SilentlyContinue) }
    catch { Say ("  [该项读取失败] Get-Process {0}：{1}" -f $pn, $_.Exception.Message) }
    if ($procs.Count -eq 0) { Say ("  {0}：无此进程（当前未运行）" -f $pn); continue }
    if ($pn -eq 'GCUService') { $script:F.GcuServiceRunning = $true }
    if ($pn -eq 'GCUBridge')  { $script:F.GcuBridgeRunning  = $true }
    foreach ($p in $procs) {
        Say ("  ★ {0}：PID={1}" -f $pn, $p.Id)
        # 启动时间：优先 .NET StartTime；读不到（SYSTEM 进程 / 非管理员）退到 Win32_Process.CreationDate
        $pc = $null
        try { $pc = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $p.Id) -ErrorAction Stop } catch { }
        $startTxt = ''
        try { $st = $p.StartTime; if ($st) { $startTxt = [string]$st } } catch { }
        if (-not $startTxt -and $pc -and $pc.CreationDate) { $startTxt = [string]$pc.CreationDate }
        if ($startTxt) { Say ("    启动时间 ：{0}" -f $startTxt) }
        else { Say '    启动时间 ：[该项读取失败，非管理员常见；建议以管理员再跑一次只读采集]' }
        $pp = ''
        try { $pp = [string]$p.Path } catch { $pp = '' }
        if (-not $pp -and $pc -and $pc.ExecutablePath) { $pp = [string]$pc.ExecutablePath }
        if ($pp) { Say ("    进程路径 ：{0}" -f $pp) }
        else { Say '    进程路径 ：[该项读取失败，非管理员常见；建议以管理员再跑一次只读采集]' }
        if ($pp -and (Test-Path -LiteralPath $pp)) {
            try {
                $pfi = Get-Item -LiteralPath $pp -ErrorAction Stop
                Say ("    FileVersion：{0}" -f $pfi.VersionInfo.FileVersion)
                Say ("    文件大小 ：{0} 字节" -f $pfi.Length)
            } catch { Say ("    [该项读取失败] 文件版本：{0}" -f $_.Exception.Message) }
        }
    }
}

Say '  --- GCUBridge / GCUService 子进程树（尽力而为，读不到即写"该项读取失败"）---'
try {
    $allProc = @(Get-CimInstance Win32_Process -ErrorAction Stop | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath)
    $roots = @($allProc | Where-Object { $_.Name -match '^GCU(Bridge|Service)(\.exe)?$' })
    if ($roots.Count -eq 0) { Say '    未找到 GCUBridge/GCUService 进程，无进程树可列' }
    # 只从最顶层那个 GCU 进程展开，避免父子都当根、把同一条链打印两遍
    $rootIds  = @($roots | ForEach-Object { $_.ProcessId })
    $topRoots = @($roots | Where-Object { $rootIds -notcontains $_.ParentProcessId })
    foreach ($r in $topRoots) {
        $kids = @($allProc | Where-Object { $_.ParentProcessId -eq $r.ProcessId })
        if ($kids.Count -eq 0) { Say ("    {0} (PID {1})：无子进程" -f $r.Name, $r.ProcessId); continue }
        foreach ($k in $kids) {
            Say ("    {0} (PID {1}) -> {2} (PID {3})  路径={4}" -f $r.Name, $r.ProcessId, $k.Name, $k.ProcessId, $(if ($k.ExecutablePath) { $k.ExecutablePath } else { '[该项读取失败]' }))
            $gk = @($allProc | Where-Object { $_.ParentProcessId -eq $k.ProcessId })
            foreach ($g in $gk) {
                Say ("      {0} (PID {1}) -> {2} (PID {3})  路径={4}" -f $k.Name, $k.ProcessId, $g.Name, $g.ProcessId, $(if ($g.ExecutablePath) { $g.ExecutablePath } else { '[该项读取失败]' }))
            }
        }
    }
} catch { Say ("    [该项读取失败] GCU 进程树（Win32_Process）：{0}" -f $_.Exception.Message) }

# ================================================================ ③ 13688
Section '③ 13688 端口占用（最能定性的一条）'
try {
    $conns  = @(Get-NetTCPConnection -LocalPort 13688 -ErrorAction Stop)
    $listen = @($conns | Where-Object { $_.State -eq 'Listen' })
    if ($listen.Count -eq 0) {
        $script:F.PortListening = $false
        Say '  ★ 13688：无人监听（当前没有任何进程在 LISTEN 13688）'
        Say '     ⇒ 这台机上不像"有东西回了 CONNACK 后掐断"，更接近端口/服务没起来。'
    } else {
        $script:F.PortListening = $true
        $script:F.ListenerName  = ''
        foreach ($c in ($listen | Sort-Object OwningProcess -Unique)) {
            Say ("  ★ 监听地址 ：{0}" -f $c.LocalAddress)
            Say ("    PID      ：{0}" -f $c.OwningProcess)
            try {
                $p = Get-Process -Id $c.OwningProcess -ErrorAction Stop
                $script:F.ListenerName = [string]$p.ProcessName
                if ($script:F.ListenerName -match '^GCUBridge') { $script:F.ListenerIsBridge = $true }
                Say ("    进程名   ：{0}" -f $p.ProcessName)
                $ppath = ''
                try { $ppath = [string]$p.Path } catch { $ppath = '' }
                if (-not $ppath) {
                    try {
                        $cim = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $c.Id) -ErrorAction Stop
                        if ($cim) {
                            if ($cim.ExecutablePath) { $ppath = [string]$cim.ExecutablePath }
                            if ($cim.CommandLine) { Say ("    命令行   ：{0}" -f $cim.CommandLine) }
                        }
                    } catch { }
                }
                if (-not $ppath) {
                    $script:F.ListenerPathUnknown = $true
                    $ppath = '(读取失败：非管理员通常读不到别的账户进程的路径，建议以管理员再跑一次只读采集)'
                }
                Say ("    进程路径 ：{0}" -f $ppath)
                $script:F.ListenerPath = [string]$ppath
                if ($ppath -and (Test-Path -LiteralPath $ppath)) {
                    try {
                        $pfi = Get-Item -LiteralPath $ppath -ErrorAction Stop
                        Say ("    文件大小 ：{0} 字节" -f $pfi.Length)
                        Say ("    文件版本 ：{0}" -f $pfi.VersionInfo.FileVersion)
                        Say ("    文件时间 ：{0}" -f $pfi.LastWriteTime)
                        $script:F.ListenerSize = [int64]$pfi.Length
                    } catch { Say ("    文件信息读取失败：{0}" -f $_.Exception.Message) }
                    try { Say ("    文件SHA256：{0}" -f (Get-FileHash -LiteralPath $ppath -Algorithm SHA256 -ErrorAction Stop).Hash) }
                    catch { Say ("    文件SHA256：读取失败（{0}）" -f $_.Exception.Message) }
                }
            } catch { Say ("    进程详情读取失败：{0}" -f $_.Exception.Message) }
        }
    }
    try {
        $est = @($conns | Where-Object { $_.State -eq 'Established' })
        Say ("  13688 其它状态连接（Established）数量：{0}" -f $est.Count)
    } catch { }
} catch {
    $script:F.PortReadFail = $true
    Say ("  [该项读取失败] Get-NetTCPConnection：{0}" -f $_.Exception.Message)
    Say '  改用 netstat 兜底：'
    try {
        $ns = @(& netstat.exe -ano 2>&1 | Select-String -SimpleMatch ':13688')
        if ($ns.Count -gt 0) {
            foreach ($l in ($ns | Select-Object -First 20)) { Say ("    " + ([string]$l.Line).Trim()) }
        } else { Say '    netstat 也没有发现 13688 相关行' }
    } catch { Say ("    [该项读取失败] netstat：{0}" -f $_.Exception.Message) }
}

# ================================================================ ③B 13688 的 ESTABLISHED 对端
Section '③B 13688 的 ESTABLISHED 对端（回答"谁占着 UWPClient_4"）'
$peerPids = New-Object System.Collections.Generic.List[int]
$c13688   = @()
try {
    $c13688 = @(Get-NetTCPConnection -LocalPort 13688 -ErrorAction Stop)
} catch {
    Say ("  [该项读取失败] Get-NetTCPConnection：{0}" -f $_.Exception.Message)
    Say '  改用 netstat -ano 兜底（先列原文，下面的 PID 详情仍会尽力打印）：'
    try {
        $nsLines = @(& netstat.exe -ano 2>&1 | Select-String -SimpleMatch ':13688')
        foreach ($l in $nsLines) {
            $t = ([string]$l.Line).Trim()
            Say ("    " + $t)
            $parts = @($t -split '\s+')
            if ($parts.Count -ge 5 -and $parts[-1] -match '^\d+$') { [void]$peerPids.Add([int]$parts[-1]) }
        }
        if ($nsLines.Count -eq 0) { Say '    netstat：未发现 13688 相关行' }
    } catch { Say ("    [该项读取失败] netstat：{0}" -f $_.Exception.Message) }
}
$estCount = 0
foreach ($c in @($c13688 | Sort-Object State, OwningProcess)) {
    if ($c.State -eq 'Established') { $estCount++ }
    Say ("  [{0}] {1}:{2} -> {3}:{4}  PID={5}" -f $c.State, $c.LocalAddress, $c.LocalPort, $c.RemoteAddress, $c.RemotePort, $c.OwningProcess)
    [void]$peerPids.Add([int]$c.OwningProcess)
}
Say ("  13688 连接总数={0}，其中 ESTABLISHED={1}（监听者见 ③）" -f @($c13688).Count, $estCount)
# 反向视角：本机进程"作为客户端"连到 13688 的套接字（RemotePort=13688）——持有 UWPClient_4 会话的一方在这里
$cPeer = @()
try { $cPeer = @(Get-NetTCPConnection -RemotePort 13688 -ErrorAction Stop) }
catch { Say ("  [该项读取失败] Get-NetTCPConnection -RemotePort 13688：{0}" -f $_.Exception.Message) }
Say ("  --- 反向视角：谁在连 13688（RemotePort=13688，共 {0} 条，即 MQTT 会话持有方）---" -f @($cPeer).Count)
if (@($cPeer).Count -eq 0) { Say '    没有查到本机进程连到 13688（或该项读取失败）' }
foreach ($c in @($cPeer | Sort-Object State, OwningProcess)) {
    Say ("  [{0}] {1}:{2} -> {3}:{4}  PID={5}" -f $c.State, $c.LocalAddress, $c.LocalPort, $c.RemoteAddress, $c.RemotePort, $c.OwningProcess)
    [void]$peerPids.Add([int]$c.OwningProcess)
}
if (@($c13688).Count -eq 0 -and $peerPids.Count -eq 0) {
    Say '  ⇒ 当前 13688 上没有任何连接：若故障当时也是这样，对话方可能已退场（更接近"后端没起来/没接上"）。'
} elseif ($estCount -eq 0) {
    Say '  ⇒ 当前 13688 上没有 ESTABLISHED 对端：可能 bridge 后端当时未就绪，或对端已断开。'
} else {
    Say ("  ⇒ 当前有 {0} 条 ESTABLISHED：持有 UWPClient_4 会话的进程见上面【反向视角】与 PID 详情。" -f $estCount)
}

# 会话持有者名单（供 ⑨ 后端健康判定直接引用；逐项 try/catch，读不到不影响整体）
try {
    $holderPids = New-Object System.Collections.Generic.List[int]
    foreach ($c in @($c13688)) { if ($c.State -eq 'Established') { [void]$holderPids.Add([int]$c.OwningProcess) } }
    foreach ($c in @($cPeer))   { if ($c.State -eq 'Established') { [void]$holderPids.Add([int]$c.OwningProcess) } }
    $holderPids = @($holderPids | Sort-Object -Unique)
    $script:F.EstHolderCount = $holderPids.Count
    if ($holderPids.Count -gt 0) {
        $hnames = New-Object System.Collections.Generic.List[string]
        foreach ($hp in $holderPids) {
            $nm = ''
            try { $nm = (Get-Process -Id $hp -ErrorAction Stop).ProcessName } catch { $nm = '' }
            if (-not $nm) { $nm = '(进程名读取失败)' }
            [void]$hnames.Add(("{0}(PID {1})" -f $nm, $hp))
        }
        $script:F.EstHolderNames = ($hnames -join ', ')
    }
    Say ("  --- 会话持有者汇总（ESTABLISHED，去重）：{0} 个" -f $script:F.EstHolderCount)
    if ($script:F.EstHolderNames) { Say ("      {0}" -f $script:F.EstHolderNames) }
    else { Say '      （当前没有 ESTABLISHED 会话持有者，或该项读取失败）' }
} catch { Say ("  [该项读取失败] 会话持有者汇总：{0}" -f $_.Exception.Message) }

Say '  --- 每个对端 PID 的进程名 / 路径 / FileVersion ---'
$seenPid = @{}
foreach ($opid in @($peerPids | Sort-Object -Unique)) {
    if ($seenPid.ContainsKey($opid)) { continue }
    $seenPid[$opid] = $true
    Say ("  --- PID {0} ---" -f $opid)
    $pr = $null
    try { $pr = Get-Process -Id $opid -ErrorAction Stop }
    catch { Say ("    [该项读取失败] 进程名：{0}" -f $_.Exception.Message); continue }
    Say ("    进程名   ：{0}" -f $pr.ProcessName)
    $op = ''
    try { $op = [string]$pr.Path } catch { $op = '' }
    if (-not $op) {
        try {
            $oc = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $opid) -ErrorAction Stop
            if ($oc -and $oc.ExecutablePath) { $op = [string]$oc.ExecutablePath }
        } catch { }
    }
    if (-not $op) { Say '    进程路径 ：[该项读取失败，非管理员常见；建议以管理员再跑一次只读采集]'; continue }
    Say ("    进程路径 ：{0}" -f $op)
    try {
        $ofi = Get-Item -LiteralPath $op -ErrorAction Stop
        Say ("    FileVersion：{0}" -f $ofi.VersionInfo.FileVersion)
        Say ("    文件大小 ：{0} 字节" -f $ofi.Length)
    } catch { Say ("    [该项读取失败] 文件信息：{0}" -f $_.Exception.Message) }
}

# ================================================================ ④ 注册表
Section '④ 注册表（GCU 变体登记）'
foreach ($rk in 'HKLM:\SOFTWARE\L-Mechrevo', 'HKLM:\SOFTWARE\WOW6432Node\L-Mechrevo') {
    Say ("  --- {0} ---" -f $rk)
    try {
        if (-not (Test-Path -LiteralPath $rk)) { Say '    （该键不存在）'; continue }
        $rp = Get-ItemProperty -LiteralPath $rk -ErrorAction Stop
        foreach ($want in 'GcuVariant', 'GcuServiceExe', 'GcuPayloadSha256', 'GcuInstallerVersion') {
            $val = $null
            try { $val = $rp.$want } catch { }
            if ($null -ne $val -and "$val" -ne '') {
                Say ("    {0} = {1}" -f $want, $val)
                if ($want -eq 'GcuVariant') { $script:F.RegVariant = [string]$val }
            } else { Say ("    {0} = （无此值）" -f $want) }
        }
        $others = @($rp.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' -and $_.Name -notin @('GcuVariant', 'GcuServiceExe', 'GcuPayloadSha256', 'GcuInstallerVersion') } | ForEach-Object { "$($_.Name)=$($_.Value)" })
        if ($others.Count -gt 0) { Say ("    其它值：{0}" -f ($others -join '; ')) }
    } catch { Say ("    [该项读取失败] {0}" -f $_.Exception.Message) }
}

# ================================================================ ⑤ GCU 目录
Section '⑤ GCU 运行目录（是否装错变体）'
$gcuRoot = Join-Path $env:ProgramFiles 'L-Mechrevo\GCU\AiStoneService'
$svcExe  = Join-Path $gcuRoot 'MyControlCenter\GCUService.exe'
$brgExe  = Join-Path $gcuRoot 'GCUBridge.exe'
Say ("  根目录：{0}" -f $gcuRoot)
Say ("  是否存在：{0}" -f (Test-Path -LiteralPath $gcuRoot))
Say ("  脚本内置基线（对照用）：30 系={0} 字节 / 40 系={1} 字节 / 50 系={2} 字节" -f $GcuBaselines[0].Size, $GcuBaselines[1].Size, $GcuBaselines[2].Size)

try {
    $svcInfo = Get-FileFacts -Label 'GCUService.exe（运行目录里实际在用的那份）' -Path $svcExe
    if ($svcInfo) {
        $matched = $null
        if ($svcInfo.Hash -and $svcInfo.Hash -notlike '(*') {
            foreach ($b in $GcuBaselines) { if ($svcInfo.Hash -eq $b.Hash) { $matched = $b; break } }
        }
        $bySize = $false
        if (-not $matched) {
            foreach ($b in $GcuBaselines) { if ($svcInfo.Size -eq $b.Size) { $matched = $b; $bySize = $true; break } }
        }
        if ($matched) {
            $script:F.VariantMatch = $matched.Name
            if ($bySize) { Say ("  ★ 变体对照：匹配【{0}】（按大小匹配；该机 Gen40 预期应为 40 系）" -f $matched.Name) }
            else         { Say ("  ★ 变体对照：匹配【{0}】（按 SHA256 精确匹配）" -f $matched.Name) }
            if ($matched.Name -notlike '40*') {
                $script:F.VariantMismatch = $true
                Say '     ⇒ 与 Gen40 预期变体不一致，建议用官方安装器重跑并选 40 系。'
            }
        } else {
            $script:F.VariantMatch = '三套基线都不匹配'
            Say '  ★ 变体对照：大小与 SHA256 都与 30/40/50 三套基线不符（可能是别的版本，或文件被替换/损坏）'
        }
        if ($script:F.RegVariant -and $matched) {
            $fileVariant = $matched.Name.Substring(0, 2)
            if ($script:F.RegVariant -notmatch '^(30|40|50)$' -or $script:F.RegVariant -ne $fileVariant) {
                $script:F.VariantMismatch = $true
                Say ("     ⇒ 注意：注册表 GcuVariant={0} 与磁盘上这份文件（{1}）看起来不一致。" -f $script:F.RegVariant, $script:F.VariantMatch)
            }
        }
    }
    $brgInfo = Get-FileFacts -Label 'GCUBridge.exe（底座 / 可被 30 系顶掉）' -Path $brgExe
    if ($brgInfo) {
        if ($brgInfo.Size -eq $Bridge30Size) {
            $script:F.BridgeIs30 = $true
            Say ("  ★ 大小 {0} 字节 = 30 系那份 GCUBridge.exe ⇒ 底座那份（682,208 字节）疑似被 30 系顶掉。" -f $Bridge30Size)
        } elseif ($brgInfo.Size -eq $BridgeBaseSize) {
            Say ("  ★ 大小 {0} 字节 = 底座那份 GCUBridge.exe（正常形态）。" -f $BridgeBaseSize)
        } else {
            Say ("  ★ 大小 {0} 字节：既不是底座那份（682,208）也不是 30 系那份（77,320）。" -f $brgInfo.Size)
        }
    }
    try {
        $cnt = @(Get-ChildItem -LiteralPath $gcuRoot -Recurse -File -Force -ErrorAction SilentlyContinue).Count
        Say ("  运行目录文件总数（递归）：{0}" -f $cnt)
    } catch { Say ("  [该项读取失败] 文件计数：{0}" -f $_.Exception.Message) }
} catch { Say ("  [该项读取失败] GCU 运行目录：{0}" -f $_.Exception.Message) }

# ================================================================ ⑥ OpenRevo
Section '⑥ OpenRevo 痕迹（它会禁用 GCUBridge、改 Run 键名）'
$orDir = Join-Path $env:APPDATA 'OpenRevo'
try {
    if (Test-Path -LiteralPath $orDir) {
        $script:F.OpenRevoDir = $true
        Say ("  ★ 存在目录：{0}" -f $orDir)
        Get-ChildItem -LiteralPath $orDir -Force -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 15 |
            ForEach-Object { Say ("    {0}  {1}  {2} 字节" -f $_.LastWriteTime, $_.Name, $_.Length) }
        $takelog = Join-Path $orDir 'oem_takeover.log'
        if (Test-Path -LiteralPath $takelog) {
            $script:F.OpenRevoTakeover = $true
            Say '  --- oem_takeover.log 末 40 行 ---'
            try {
                Get-Content -LiteralPath $takelog -Tail 40 -ErrorAction Stop | ForEach-Object { Say ("    " + ([string]$_).TrimEnd()) }
            } catch { Say ("    [该项读取失败] oem_takeover.log：{0}" -f $_.Exception.Message) }
        } else { Say '  oem_takeover.log：不存在' }
    } else {
        Say '  未发现 %APPDATA%\OpenRevo（大概率没装过 OpenRevo）'
    }
} catch { Say ("  [该项读取失败] OpenRevo 目录：{0}" -f $_.Exception.Message) }

try {
    $orp = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'OpenRevo' })
    if ($orp.Count -gt 0) {
        $script:F.OpenRevoProc = $true
        foreach ($p in $orp) { Say ("  ★ OpenRevo 进程：{0} (PID {1})" -f $p.ProcessName, $p.Id) }
    } else { Say '  未发现 OpenRevo 相关进程' }
} catch { Say ("  [该项读取失败] OpenRevo 进程：{0}" -f $_.Exception.Message) }

try {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $run = Get-ItemProperty -LiteralPath $runKey -ErrorAction Stop
    $runNames = @($run.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { $_.Name })
    Say ("  HKCU Run 键值名：{0}" -f ($runNames -join ', '))
    $renamed = @($runNames | Where-Object { $_ -match 'OpenRevoBackup_' })
    if ($renamed.Count -gt 0) {
        $script:F.RunRenamed = $true
        Say ("  ★ 发现 OpenRevo 改名痕迹（OpenRevoBackup_*）：{0}" -f ($renamed -join ', '))
    } else { Say '  未发现 OpenRevoBackup_* 改名痕迹' }
} catch { Say ("  [该项读取失败] HKCU Run 键：{0}" -f $_.Exception.Message) }

# ================================================================ ⑦ 日志与防火墙
Section '⑦ LM 日志与防火墙'
$lmLog = Join-Path $env:APPDATA 'MechrevoLite\log.txt'
try {
    if (-not (Test-Path -LiteralPath $lmLog)) {
        $script:F.LogMissing = $true
        Say ("  ★ 未发现 LM 日志：{0}" -f $lmLog)
        Say '     ⇒ 日志级别可能是 off/未开启。建议把日志级别改成"全部"，复现一次"GCU 连不上"后回传。'
    } else {
        $li = Get-Item -LiteralPath $lmLog
        Say ("  LM 日志：{0}" -f $li.FullName)
        Say ("    大小={0} 字节  最后写入={1}" -f $li.Length, $li.LastWriteTime)
        $lines = @()
        try {
            if ($li.Length -gt 8MB) {
                Say '    （日志较大，只取末尾 20000 行做关键字检索）'
                $lines = @(Get-Content -LiteralPath $lmLog -Tail 20000 -ErrorAction Stop)
            } else {
                $lines = @(Get-Content -LiteralPath $lmLog -ErrorAction Stop)
            }
            Say ("    参与检索行数：{0}" -f $lines.Count)
        } catch { Say ("    [该项读取失败] 读取日志：{0}" -f $_.Exception.Message) }
        foreach ($kw in '订阅失败', 'MechrevoHw', 'MQTT', 'CONNACK', '13688', '连接尚未就绪', 'disconnected', 'connected', 'clientId', 'Hz screen identify', 'screen metrics') {
            $hit = @($lines | Select-String -SimpleMatch -Pattern $kw)
            Say ("    --- 关键字「{0}」命中 {1} 行，下面是最末 5 行 ---" -f $kw, $hit.Count)
            if ($hit.Count -gt 0) {
                foreach ($h in ($hit | Select-Object -Last 5)) { Say ("      " + ([string]$h.Line).Trim()) }
            }
        }
    }
} catch { Say ("  [该项读取失败] LM 日志：{0}" -f $_.Exception.Message) }

# 防火墙（只读）
try {
    Say '  --- 防火墙规则（名字含 13688 / Mechrevo / GCU）---'
    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match '13688|Mechrevo|GCU' })
    if ($rules.Count -gt 0) {
        foreach ($r in $rules) {
            Say ("    规则：{0} | 启用={1} | 方向={2} | 动作={3} | 配置={4}" -f $r.DisplayName, $r.Enabled, $r.Direction, $r.Action, $r.Profile)
            if ($r.DisplayName -match '13688' -or ($r.Direction -eq 'Inbound' -and $r.Action -eq 'Block')) { $script:F.FwBlock = [string]$r.DisplayName }
        }
    } else { Say '    Get-NetFirewallRule：未发现名字含 13688/Mechrevo/GCU 的规则' }
    try {
        $pf = @(Get-NetFirewallPortFilter -ErrorAction Stop | Where-Object { "$($_.LocalPort)" -match '13688' })
        if ($pf.Count -gt 0) { Say ("    端口过滤中命中 LocalPort=13688 的规则数：{0}" -f $pf.Count) }
        else { Say '    端口过滤中未命中 LocalPort=13688' }
    } catch { Say ("    端口过滤读取失败（非管理员时常见，属正常）：{0}" -f $_.Exception.Message) }
} catch { Say ("  [该项读取失败] Get-NetFirewallRule：{0}" -f $_.Exception.Message) }

try {
    Say '  --- netsh 兜底检索（只读；规则多时略慢）---'
    $nsAll = @(netsh advfirewall firewall show rule name=all 2>&1)
    $found = 0
    for ($i = 0; $i -lt $nsAll.Count -and $found -lt 3; $i++) {
        if ([string]$nsAll[$i] -match '13688') {
            $found++
            $from = [Math]::Max(0, $i - 12)
            Say '    --- netsh 片段 ---'
            for ($j = $from; $j -le $i; $j++) { Say ("      " + ([string]$nsAll[$j]).TrimEnd()) }
        }
    }
    if ($found -eq 0) { Say '    netsh：未发现 13688 相关规则' }
} catch { Say ("  [该项读取失败] netsh：{0}" -f $_.Exception.Message) }

# ================================================================ ⑦B 显示枚举
Section '⑦B 显示枚举（回答"当时有几块屏、哪块是内屏/eDP、各是多少 Hz"）'
Say '  用途：极光X"多显示器刷新率"与极光Pro23"GCU 连不上"都要先确定"这台机当时挂了哪几块屏"。'
Say '  只读：只查配置，不点刷新率、不改分辨率、不切显示模式、不插拔屏。'

# QueryDisplayConfig（活动路径：输出接口类型 + 友好名）—— 需要现场编译一小段 C#。
# 源码刻意保持纯 ASCII，避免 Add-Type 在中文系统上因代码页导致编译/输出乱码。
$DspCfgSrc = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DshDispCfg
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    public struct REGION2D { public uint cx; public uint cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public RATIONAL hSyncFreq;
        public RATIONAL vSyncFreq;
        public REGION2D activeSize;
        public REGION2D totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TARGET_MODE { public VIDEO_SIGNAL_INFO targetVideoSignalInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public int posX; public int posY; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public TARGET_MODE targetMode;
        [FieldOffset(16)] public SOURCE_MODE sourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetTargetName(ref TARGET_DEVICE_NAME name);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetSourceName(ref SOURCE_DEVICE_NAME name);

    public static string TechName(uint tech)
    {
        if (tech == 2147483648u) { return "INTERNAL"; }
        if (tech == 11u) { return "DISPLAYPORT_EMBEDDED"; }
        if (tech == 10u) { return "DISPLAYPORT_EXTERNAL"; }
        if (tech == 5u) { return "HDMI"; }
        if (tech == 4u) { return "DVI"; }
        if (tech == 0u) { return "HD15"; }
        if (tech == 6u) { return "LVDS"; }
        if (tech == 15u) { return "MIRACAST"; }
        if (tech == 16u) { return "INDIRECT_WIRED"; }
        if (tech == 17u) { return "INDIRECT_VIRTUAL"; }
        if (tech == 4294967295u) { return "OTHER"; }
        return "UNKNOWN";
    }

    private static bool SameLuid(LUID a, LUID b)
    {
        return a.LowPart == b.LowPart && a.HighPart == b.HighPart;
    }

    public static string Dump()
    {
        StringBuilder sb = new StringBuilder();
        uint numPath = 0;
        uint numMode = 0;
        int rc = GetDisplayConfigBufferSizes(2u, out numPath, out numMode);
        if (rc != 0) { return "GetDisplayConfigBufferSizes rc=" + rc; }
        PATH_INFO[] paths = new PATH_INFO[numPath];
        MODE_INFO[] modes = new MODE_INFO[numMode];
        rc = QueryDisplayConfig(2u, ref numPath, paths, ref numMode, modes, IntPtr.Zero);
        if (rc != 0) { return "QueryDisplayConfig rc=" + rc; }
        for (int i = 0; i < (int)numPath; i++)
        {
            TARGET_DEVICE_NAME tn = new TARGET_DEVICE_NAME();
            tn.header.type = 2u;
            tn.header.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
            tn.header.adapterId = paths[i].targetInfo.adapterId;
            tn.header.id = paths[i].targetInfo.id;
            string friendly = "(read-failed)";
            string devicePath = "";
            uint tech = paths[i].targetInfo.outputTechnology;
            if (GetTargetName(ref tn) == 0)
            {
                friendly = string.IsNullOrEmpty(tn.monitorFriendlyDeviceName) ? "(no-friendly-name)" : tn.monitorFriendlyDeviceName;
                devicePath = tn.monitorDevicePath;
                tech = tn.outputTechnology;
            }
            SOURCE_DEVICE_NAME sn = new SOURCE_DEVICE_NAME();
            sn.header.type = 1u;
            sn.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_DEVICE_NAME));
            sn.header.adapterId = paths[i].sourceInfo.adapterId;
            sn.header.id = paths[i].sourceInfo.id;
            string gdi = (GetSourceName(ref sn) == 0) ? sn.viewGdiDeviceName : "(read-failed)";
            uint w = 0u;
            uint h = 0u;
            uint hz = 0u;
            for (int j = 0; j < (int)numMode; j++)
            {
                if (modes[j].infoType == 1u && modes[j].id == paths[i].sourceInfo.id && SameLuid(modes[j].adapterId, paths[i].sourceInfo.adapterId))
                {
                    w = modes[j].sourceMode.width;
                    h = modes[j].sourceMode.height;
                }
                if (modes[j].infoType == 2u && modes[j].id == paths[i].targetInfo.id && SameLuid(modes[j].adapterId, paths[i].targetInfo.adapterId))
                {
                    uint den = modes[j].targetMode.targetVideoSignalInfo.vSyncFreq.Denominator;
                    if (den != 0u)
                    {
                        hz = (uint)Math.Round((double)modes[j].targetMode.targetVideoSignalInfo.vSyncFreq.Numerator / (double)den);
                    }
                }
            }
            bool embedded = (tech == 11u || tech == 2147483648u);
            sb.AppendLine(string.Format("    #{0} friendly={1} output={2}(0x{3:X8}) gdi={4} resolution={5}x{6} refresh={7}Hz internal={8}",
                i + 1, friendly, TechName(tech), tech, gdi, w, h, hz, embedded ? "yes" : "no"));
            sb.AppendLine(string.Format("         devicePath={0} adapterLuid={1:X8}:{2:X8}", devicePath, paths[i].targetInfo.adapterId.HighPart, paths[i].targetInfo.adapterId.LowPart));
        }
        if (numPath == 0u) { sb.AppendLine("    (no active path)"); }
        return sb.ToString().TrimEnd();
    }
}
'@

$dspReady = $false
try {
    if (-not ('DshDispCfg' -as [type])) {
        Add-Type -TypeDefinition $DspCfgSrc -Language CSharp -ErrorAction Stop
    }
    $dspReady = $true
} catch {
    Say ("  [该项读取失败] QueryDisplayConfig（现场编译 C# 失败，改用 WMI 兜底）：{0}" -f $_.Exception.Message)
}

Say '  --- QueryDisplayConfig 活动路径（QDC_ONLY_ACTIVE_PATHS；输出接口类型 + 友好名）---'
if ($dspReady) {
    try {
        foreach ($l in @(([DshDispCfg]::Dump()) -split "`r?`n")) { Say $l }
    } catch { Say ("  [该项读取失败] QueryDisplayConfig：{0}" -f $_.Exception.Message) }
} else {
    Say '  （Add-Type 不可用，跳过；下方 root\wmi 兜底仍会给出输出技术与友好名）'
}

Say '  --- 显示器 EDID 标识 / 输出技术（root\wmi：WmiMonitorID + WmiMonitorConnectionParams）---'
# WMI 里 UserFriendlyName / ManufacturerName / SerialNumberID 是 uint16[]，ProductCodeID 也是数组；
# 直接打印只会得到 "System.UInt16[]"，故统一解码（数组→字符；ProductCodeID→十六进制）。
function Convert-EdidText {
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [System.Array]) {
        return (-join @($Value | Where-Object { $_ -ne 0 } | ForEach-Object { [char][int]$_ }))
    }
    return ([string]$Value)
}
function Convert-EdidHex {
    param($Value)
    if ($null -eq $Value) { return '(无)' }
    if ($Value -is [System.Array]) {
        return (@($Value | ForEach-Object { ('0x{0:X4}' -f [int]$_) }) -join ',')
    }
    return ('0x{0:X4}' -f [int]$Value)
}
function Fmt-Wmi {
    param($Value)
    if ($null -eq $Value -or "$Value" -eq '') { return '(无)' }
    return ([string]$Value)
}
$edidTotal = 0; $edidInternal = 0; $edidExternal = 0
$connTech = @{}
try {
    foreach ($c in @(Get-CimInstance -Namespace 'root\wmi' -ClassName 'WmiMonitorConnectionParams' -ErrorAction Stop)) {
        try { $connTech[[string]$c.InstanceName] = [int64]$c.VideoOutputTechnology } catch { }
    }
} catch { Say ("    [该项读取失败] WmiMonitorConnectionParams：{0}" -f $_.Exception.Message) }

try {
    foreach ($m in @(Get-CimInstance -Namespace 'root\wmi' -ClassName 'WmiMonitorID' -ErrorAction Stop)) {
        $edidTotal++
        $inst = [string]$m.InstanceName
        $fn = Convert-EdidText $m.UserFriendlyName
        $mf = Convert-EdidText $m.ManufacturerName
        $sn = Convert-EdidText $m.SerialNumberID
        $pc = Convert-EdidText $m.ProductCodeID
        if ($pc) { $pc = ($pc + ' (' + (Convert-EdidHex $m.ProductCodeID) + ')') } else { $pc = '(空)' }
        if ($sn -eq '0') { $sn = '' }
        $techTxt = '(无)'
        $techVal = $null
        if ($connTech.ContainsKey($inst)) { $techVal = $connTech[$inst]; $techTxt = ('0x{0:X8}' -f $techVal) }
        $isInternal = ($techVal -ne $null -and ($techVal -eq 2147483648 -or $techVal -eq 11))
        if ($techVal -eq $null) { $cls = '未判定' } elseif ($isInternal) { $cls = '内屏/eDP'; $edidInternal++ } else { $cls = '外接/虚拟屏'; $edidExternal++ }
        Say ("    #{0} 友好名={1} 厂商={2} ProductCodeID={3} Serial={4} 年份={5} 周={6}" -f $edidTotal, $(if ($fn) { $fn } else { '(空)' }), $(if ($mf) { $mf } else { '(空)' }), $pc, $(if ($sn) { $sn } else { '(空)' }), $m.YearOfManufacture, $m.WeekOfManufacture)
        Say ("        输出技术={0} 分类={1} InstanceName={2}" -f $techTxt, $cls, $inst)
    }
    if ($edidTotal -eq 0) { Say '    （WmiMonitorID 未返回任何条目；该项读取失败或非管理员受限）' }
} catch { Say ("    [该项读取失败] WmiMonitorID：{0}" -f $_.Exception.Message) }

Say '  --- 显示适配器（Win32_VideoController：CurrentRefreshRate / MaxRefreshRate）---'
try {
    $vcs = @(Get-CimInstance -ClassName 'Win32_VideoController' -ErrorAction Stop)
    if ($vcs.Count -eq 0) { Say '    （未枚举到显示适配器）' }
    $vi = 0
    foreach ($v in $vcs) {
        $vi++
        Say ("    #{0} {1}" -f $vi, $v.Name)
        Say ("        CurrentRefreshRate={0} MaxRefreshRate={1} MinRefreshRate={2}" -f (Fmt-Wmi $v.CurrentRefreshRate), (Fmt-Wmi $v.MaxRefreshRate), (Fmt-Wmi $v.MinRefreshRate))
        Say ("        CurrentResolution={0}x{1}  VideoModeDescription={2}" -f (Fmt-Wmi $v.CurrentHorizontalResolution), (Fmt-Wmi $v.CurrentVerticalResolution), (Fmt-Wmi $v.VideoModeDescription))
        Say ("        DriverVersion={0}  Status={1}  PNPDeviceID={2}" -f (Fmt-Wmi $v.DriverVersion), (Fmt-Wmi $v.Status), (Fmt-Wmi $v.PNPDeviceID))
    }
} catch { Say ("    [该项读取失败] Win32_VideoController：{0}" -f $_.Exception.Message) }

Say '  --- 活动屏（System.Windows.Forms.Screen）---'
Say '    注：非 DPI-aware 进程（例如本脚本）读到的 Bounds 会被 DPI 虚拟化（会小于真实分辨率）；'
Say '        真实分辨率请以上面 QueryDisplayConfig 的 resolution= 或 Win32_VideoController 为准。'
$gdiCount = 0
try {
    Add-Type -AssemblyName 'System.Windows.Forms' -ErrorAction Stop
    $scr = @([System.Windows.Forms.Screen]::AllScreens)
    $gdiCount = $scr.Count
    foreach ($s in $scr) {
        Say ("    {0} 主屏={1} 逻辑坐标分辨率={2}x{3} 位置=({4},{5})" -f $s.DeviceName, $s.Primary, $s.Bounds.Width, $s.Bounds.Height, $s.Bounds.X, $s.Bounds.Y)
    }
    if ($gdiCount -eq 0) { Say '    （未枚举到活动屏）' }
} catch { Say ("    [该项读取失败] Screen.AllScreens：{0}" -f $_.Exception.Message) }

Say '  --- 分类结论（保守措辞）---'
Say ("    活动 GDI 屏={0} 块；EDID 条目={1}（判为内屏/eDP={2}，判为外接/虚拟屏={3}）" -f $gdiCount, $edidTotal, $edidInternal, $edidExternal)
if ($gdiCount -ge 2) {
    Say "    ⇒ 采集时是【多屏】状态：若故障只在多屏时出现（例如刷新率档位错、GCU 连不上），这一份就是关键对照。"
} elseif ($gdiCount -eq 1) {
    Say '    ⇒ 采集时是【单屏】状态：只有一块屏，刷新率/GCU 问题若存在，就不是「外接屏档位污染」造成的。'
} else {
    Say '    ⇒ 未能确定屏数（该项读取失败），不能据此下结论。'
}
Say "    说明：本节的 Win32_VideoController.MaxRefreshRate 与活动路径 refresh= 都是【系统侧】读数；"
Say "          包内/日志里的 HzList/CurrentHz 是【GCU 侧】上报值 —— 两者不一致才是问题证据。"

# ================================================================ ⑦C GCU / broker 侧日志
Section '⑦C GCU / broker 侧日志（只读：看 broker 是否持有 UWPClient_4、为何断）'
Say '  只读：只取既有 *.log 的末尾若干行，不复制整棵目录、不新建文件、不改任何内容。'
$GcuLogTailLines  = 60   # 每个文件读末尾多少行
$GcuLogMaxFiles   = 6    # 最多摘几个文件
$GcuLogMaxLines   = 80   # 每个文件最多输出多少行（只取尾部）
$GcuLogScanLines  = 400  # 关键字检索时每个文件读多少行

$gcuLogRoots = New-Object System.Collections.Generic.List[string]
$gcuBase = Join-Path $env:ProgramFiles 'L-Mechrevo\GCU'
try {
    if (Test-Path -LiteralPath $gcuBase) {
        Say ("  扫描根：{0}（按实际子目录名枚举，AiStoneService / AiStageService 都收）" -f $gcuBase)
        $subDirs = @(Get-ChildItem -LiteralPath $gcuBase -Directory -Force -ErrorAction SilentlyContinue)
        foreach ($d in $subDirs) {
            [void]$gcuLogRoots.Add($d.FullName)
            Say ("    子目录：{0}" -f $d.Name)
        }
        if ($subDirs.Count -eq 0) { Say '    （GCU 目录下没有子目录）' }
    } else {
        Say ("  GCU 目录不存在：{0}" -f $gcuBase)
    }
} catch { Say ("  [该项读取失败] 枚举 GCU 目录：{0}" -f $_.Exception.Message) }

$pdLogs = Join-Path $env:ProgramData 'L-Mechrevo\logs'
if (Test-Path -LiteralPath $pdLogs) {
    [void]$gcuLogRoots.Add($pdLogs)
    Say ("  扫描根：{0}（存在）" -f $pdLogs)
} else {
    Say ("  ProgramData 日志目录不存在（跳过）：{0}" -f $pdLogs)
}

# 注意：此处循环变量刻意用 $lgf/$lf，不要用单字母 $f ——
# PowerShell 变量名大小写不敏感，而脚本作用域里的 $script:F（采集事实表）会被
# 同名的 $f 覆盖成 FileInfo，导致 ⑧/⑨ 读到空值（本脚本 2026-10-09 修的就是这个坑）。
$gcuLogFiles = New-Object System.Collections.Generic.List[object]
foreach ($root in $gcuLogRoots) {
    try {
        foreach ($lgf in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.log' -Force -ErrorAction SilentlyContinue)) {
            [void]$gcuLogFiles.Add($lgf)
        }
    } catch { Say ("    [该项读取失败] 枚举 {0} 下的 *.log：{1}" -f $root, $_.Exception.Message) }
}
Say ("  共发现 *.log：{0} 个（只摘最新 {1} 个、每个最多 {2} 行）" -f $gcuLogFiles.Count, $GcuLogMaxFiles, $GcuLogMaxLines)

if ($gcuLogFiles.Count -eq 0) {
    Say '  ★ 没有发现任何 GCU/broker 侧 *.log ⇒ 底座侧"是否持有 UWPClient_4、为何断"仍无直接证据。'
    Say '    这是当前证据链上唯一还缺的一块，建议在故障复现后立刻再跑一次本脚本。'
} else {
    $topFiles = @($gcuLogFiles | Sort-Object LastWriteTime -Descending | Select-Object -First $GcuLogMaxFiles)
    $gcuLogCache = @{}
    $idx = 0
    foreach ($lf in $topFiles) {
        $idx++
        Say ("  --- [{0}/{1}] {2} ---" -f $idx, $topFiles.Count, $lf.FullName)
        Say ("      大小={0} 字节  最后写入={1}" -f $lf.Length, $lf.LastWriteTime)
        try {
            $cache = @(Get-Content -LiteralPath $lf.FullName -Tail $GcuLogScanLines -ErrorAction Stop)
            $gcuLogCache[$lf.FullName] = $cache
            $tail = @($cache | Select-Object -Last ([Math]::Min($GcuLogTailLines, $GcuLogMaxLines)))
            foreach ($l in $tail) { Say ("      " + ([string]$l).TrimEnd()) }
        } catch { Say ("      [该项读取失败] 读取尾部：{0}" -f $_.Exception.Message) }
    }
    Say '  --- 关键字命中（只在上面已读取的尾部缓存里检索，避免重复读盘）---'
    foreach ($kw in 'UWPClient', 'disconnect', 'CONNACK', 'identifier', '13688', 'subscribe', 'Subscribe') {
        $hits = @()
        foreach ($k in $gcuLogCache.Keys) {
            foreach ($line in $gcuLogCache[$k]) {
                if ([string]$line -like ('*' + $kw + '*')) { $hits += [string]$line }
            }
        }
        Say ("    「{0}」命中 {1} 行" -f $kw, $hits.Count)
        foreach ($h in ($hits | Select-Object -Last 3)) { Say ("      " + $h.Trim()) }
    }
}

# ================================================================ -Fix
if ($Fix) {
    Section '★ -Fix：最小修复（只做两类操作；需管理员）'
    Say '  (1) sc.exe config GCUBridge start= auto'
    Say '  (2) 重启后端服务：对【实际存在】的 GCUBridge / GCUService 执行 Restart-Service'
    Say '      （服务为 Stopped 时改 Start-Service）；并打印修复前/后的 Status/StartType 与关键进程。'
    Say '  不会重装 GCU、不拷贝/替换任何文件、不改注册表、不动 OpenRevo、不删除任何文件。'
    Say '  不会自动提权、不会弹 UAC；不结束任何其它进程、不动显示/性能模式。'
    if (-not $script:F.IsAdmin) {
        $script:F.FixApplied = '已跳过（当前不是管理员）'
        Say '  [跳过] 当前不是管理员。请右键"以管理员身份运行 PowerShell"后，再加 -Fix 重跑本脚本。'
        Say '         本脚本不会自动提权、不会弹 UAC。'
    } else {
        # ---- 修复前的服务状态 + 关键进程（先把基线留证）----
        Say '  --- 修复前 ---'
        $svcNames = @('GCUBridge', 'GCUService')
        $svcPre   = @{}   # 名称 -> 服务对象（仅存在的）
        foreach ($sn in $svcNames) {
            try {
                $s = Get-Service -Name $sn -ErrorAction Stop
                $svcPre[$sn] = $s
                Say ("    {0}：Status={1}  StartType={2}" -f $sn, $s.Status, $s.StartType)
            } catch {
                Say ("    {0}：服务不存在或读取失败（{1}）—— 本脚本不会为它创建服务" -f $sn, $_.Exception.Message)
            }
        }
        foreach ($pn in @('GCUBridge', 'GCUService')) {
            try {
                $pp = @(Get-Process -Name $pn -ErrorAction SilentlyContinue)
                if ($pp.Count -eq 0) { Say ("    进程 {0}：未运行" -f $pn) }
                else { foreach ($p in $pp) { Say ("    进程 {0}：PID={1}  启动={2}" -f $pn, $p.Id, $(try { [string]$p.StartTime } catch { '(读取失败)' })) } }
            } catch { Say ("    [读取失败] 进程 {0}：{1}" -f $pn, $_.Exception.Message) }
        }

        # ---- (1) sc.exe config GCUBridge start= auto（仅当该服务存在）----
        if ($svcPre.ContainsKey('GCUBridge')) {
            Say '  --- 执行：sc.exe config GCUBridge start= auto ---'
            try {
                $a1 = @('config', 'GCUBridge', 'start=', 'auto')
                $o1 = & sc.exe @a1 2>&1
                foreach ($l in @($o1)) { Say ("    " + ([string]$l).TrimEnd()) }
                Say ("    exit={0}" -f $LASTEXITCODE)
            } catch { Say ("    [执行失败] sc.exe config：{0}" -f $_.Exception.Message) }
        } else {
            Say '  --- 跳过：sc.exe config GCUBridge start= auto（GCUBridge 服务不存在，不创建）---'
        }

        # ---- (2) 重启/启动后端服务（只动存在的服务；跨停止/启动失败都要捕获并报告）----
        Say '  --- 执行：重启后端服务（Restart-Service；Stopped 则 Start-Service）---'
        $didAny = $false
        foreach ($sn in $svcNames) {
            if (-not $svcPre.ContainsKey($sn)) {
                Say ("    {0}：服务不存在 ⇒ 跳过（不创建、不猜测）" -f $sn)
                continue
            }
            $didAny = $true
            $cur = [string]$svcPre[$sn].Status
            try {
                if ($cur -eq 'Stopped') {
                    Say ("    {0}：当前 Stopped ⇒ Start-Service {0}" -f $sn)
                    Start-Service -Name $sn -ErrorAction Stop
                    Say ("    {0}：Start-Service 已返回" -f $sn)
                } else {
                    Say ("    {0}：当前 {1} ⇒ Restart-Service {0}" -f $sn, $cur)
                    Restart-Service -Name $sn -ErrorAction Stop
                    Say ("    {0}：Restart-Service 已返回" -f $sn)
                }
            } catch {
                Say ("    [执行失败] {0}：{1}" -f $sn, $_.Exception.Message)
                try {
                    $mid = Get-Service -Name $sn -ErrorAction Stop
                    Say ("      {0} 当前状态：Status={1}" -f $sn, $mid.Status)
                } catch { Say ("      {0} 状态复读失败：{1}" -f $sn, $_.Exception.Message) }
            }
            try {
                $w = Get-Service -Name $sn -ErrorAction Stop
                $w.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
                Say ("    {0}：已到 Running" -f $sn)
            } catch {
                Say ("    {0}：等待 Running 未成功（{1}）" -f $sn, $_.Exception.Message)
            }
        }
        if (-not $didAny) {
            Say '    说明：本机 GCUBridge / GCUService 两个服务都不存在 ⇒ 没有任何服务被启动。'
            Say '          这不属于本脚本的处理范围（本脚本不安装/不注册服务），请走官方安装器。'
        }

        # ---- 修复后 ----
        Say '  --- 修复后 ---'
        foreach ($sn in $svcNames) {
            try {
                $s2 = Get-Service -Name $sn -ErrorAction Stop
                $preTxt = '（修复前读取失败）'
                if ($svcPre.ContainsKey($sn)) { $preTxt = ('{0}/{1}' -f $svcPre[$sn].Status, $svcPre[$sn].StartType) }
                Say ("    {0}：Status={1}  StartType={2}   （修复前：{3}）" -f $sn, $s2.Status, $s2.StartType, $preTxt)
                if ($sn -eq 'GCUBridge') {
                    $script:F.BridgeStatus = [string]$s2.Status
                    $script:F.BridgeStart  = [string]$s2.StartType
                } else {
                    $script:F.SvcServiceStatus = [string]$s2.Status
                    $script:F.SvcServiceStart  = [string]$s2.StartType
                }
            } catch { Say ("    {0}：修复后读取失败（{1}）" -f $sn, $_.Exception.Message) }
        }
        foreach ($pn in @('GCUBridge', 'GCUService')) {
            try {
                $pp2 = @(Get-Process -Name $pn -ErrorAction SilentlyContinue)
                if ($pp2.Count -eq 0) { Say ("    进程 {0}：仍未运行" -f $pn); if ($pn -eq 'GCUService') { $script:F.GcuServiceRunning = $false } }
                else {
                    foreach ($p in $pp2) { Say ("    进程 {0}：PID={1}" -f $pn, $p.Id) }
                    if ($pn -eq 'GCUService') { $script:F.GcuServiceRunning = $true }
                    if ($pn -eq 'GCUBridge')  { $script:F.GcuBridgeRunning  = $true }
                }
            } catch { Say ("    [读取失败] 进程 {0}：{1}" -f $pn, $_.Exception.Message) }
        }
        try {
            $l2 = @(Get-NetTCPConnection -LocalPort 13688 -State Listen -ErrorAction Stop)
            Say ("    13688 监听：{0} 条（0 条表示重启后后端还没把端口拉起来，建议稍等 10 秒再跑一次只读采集）" -f $l2.Count)
        } catch { Say ("    13688 监听复读失败（{0}）" -f $_.Exception.Message) }

        $script:F.FixApplied = '已执行（config start=auto + 重启已存在的 GCU 后端服务）'
        Say ''
        Say '  ★ 请现在复测 LM 是否连上：'
        Say '      - 若已连上 ⇒ 问题确在"后端服务未就绪/卡死"，把本报告留档即可。'
        Say '      - 若仍显示 GCU 未连接（尤其仍是「CONNACK 成功、断在 Subscribe」）⇒ 请【重新导出 LM 诊断包】，'
        Say '        连同本脚本输出一起回传；脚本已排除"修复动作"以外的变量，便于对比复现前后。'
    }
}

# ================================================================ ⑨ 后端健康判定（只读）
Section '⑨ 后端健康判定（只读；针对"CONNACK 成功、断在 Subscribe"这一形态）'
Say '  本节只读：一句结论 + 该做什么。原始数据仍见 ②/②B/③/③B/⑤，这里只做归纳，不再重复采集。'
Say '  判定对象：后端 = GCUService（后端核心）+ GCUBridge（13688 broker 侧）。'

try {
    # 优先用本次进程扫描的实时结果；若该扫描没命中则以 Get-Process 现查一次兜底
    $serviceLive = $false
    try { $serviceLive = @(Get-Process -Name 'GCUService' -ErrorAction SilentlyContinue).Count -gt 0 } catch { $serviceLive = $false }
    if ($serviceLive) { $script:F.GcuServiceRunning = $true }
    $bridgeLive = $false
    try { $bridgeLive = @(Get-Process -Name 'GCUBridge' -ErrorAction SilentlyContinue).Count -gt 0 } catch { $bridgeLive = $false }
    if ($bridgeLive) { $script:F.GcuBridgeRunning = $true }

    # ---- 事实汇总表 ----
    Say '  --- 事实汇总（服务 / 进程 / 13688）---'
    foreach ($sn in @('GCUBridge', 'GCUService')) {
        if ($sn -eq 'GCUBridge') { $present = $script:F.SvcBridgePresent; $st = $script:F.BridgeStatus; $sty = $script:F.BridgeStart; $run = $script:F.GcuBridgeRunning }
        else                     { $present = $script:F.SvcServicePresent; $st = $script:F.SvcServiceStatus; $sty = $script:F.SvcServiceStart; $run = $script:F.GcuServiceRunning }
        if (-not $present) {
            Say ("    {0}：服务【不存在】  Status=(无)  StartType=(无)  进程={1}" -f $sn, $(if ($run) { '在运行（服务未注册但有进程）' } else { '未运行' }))
        } else {
            Say ("    {0}：服务存在  Status={1}  StartType={2}  进程={3}" -f $sn, $(if ($st) { $st } else { '(读取失败)' }), $(if ($sty) { $sty } else { '(读取失败)' }), $(if ($run) { '在运行' } else { '未运行' }))
        }
    }
    if ($script:F.PortListening -eq $true) {
        $lp = $script:F.ListenerPath; if (-not $lp) { $lp = '(路径读取失败)' }
        Say ("    13688 监听者：{0}  PID 与路径详见 ③" -f $script:F.ListenerName)
        Say ("      路径     ：{0}" -f $lp)
        if ($script:F.ListenerSize -gt 0) { Say ("      大小     ：{0} 字节" -f $script:F.ListenerSize) }
        Say ("      是否为 GCUBridge：{0}" -f $(if ($script:F.ListenerIsBridge) { '是' } else { '否 ★' }))
    } elseif ($script:F.PortListening -eq $false) {
        Say '    13688 监听者：无（当前无人 LISTEN 13688）'
    } else {
        Say '    13688 监听者：读取失败（见 ③ 的 netstat 兜底输出）'
    }
    if ($script:F.EstHolderCount -gt 0) { Say ("    13688 ESTABLISHED 持有者：{0} 个 — {1}" -f $script:F.EstHolderCount, $script:F.EstHolderNames) }
    else { Say '    13688 ESTABLISHED 持有者：0 个（当前没有活动会话；故障当时若也是这样，说明对端压根没建立起会话）' }

    # ---- 运行目录 exe 事实（复用 ⑤ 的结果，不重复读盘）----
    Say '  --- 运行目录 exe 事实（复用 ⑤，不重复读取）---'
    if ($script:F.SvcExeFacts) {
        Say ("    GCUService.exe：{0} 字节 / 版本 {1} / SHA256 {2}" -f $script:F.SvcExeFacts.Size, $script:F.SvcExeFacts.Version, $script:F.SvcExeFacts.Hash)
    } else { Say '    GCUService.exe：⑤ 未取到（文件不存在或该项读取失败）' }
    if ($script:F.BrgExeFacts) {
        Say ("    GCUBridge.exe ：{0} 字节 / 版本 {1} / SHA256 {2}" -f $script:F.BrgExeFacts.Size, $script:F.BrgExeFacts.Version, $script:F.BrgExeFacts.Hash)
    } else { Say '    GCUBridge.exe ：⑤ 未取到（文件不存在或该项读取失败）' }
    Say ("    变体对照：{0}{1}" -f $(if ($script:F.VariantMatch) { $script:F.VariantMatch } else { '(未判定)' }), $(if ($script:F.BridgeIs30) { '；且 GCUBridge.exe 疑似被 30 系那份顶掉' } else { '' }))
    Say ("    Gen40 预期：40 系 GCUService.exe = {0} 字节；底座 GCUBridge.exe = {1} 字节" -f $GcuBaselines[1].Size, $BridgeBaseSize)

    # ---- 一句结论 ----
    Say '  --- 判定（按实际值择一）---'
    if (-not $script:F.IsAdmin) {
        Say '    提示：本进程不是管理员，进程路径/启动时间等个别项可能读不到；要拿到完整证据请用管理员再跑一次只读采集。'
    }
    # 判定顺序有讲究（本机实测：Gen40 上 GCUService 常常【只以后台进程形态跟着 GCUBridge 起】，
    # 并没有注册成 NT 服务）。所以先判"后端进程在不在 + 13688 是不是 GCUBridge 在听"，
    # 最后才把"GCUService 服务未注册"当作附注 —— 否则会把"后端明明正常"误报成"后端未就绪"。
    if ($script:F.GcuServiceRunning -and $script:F.ListenerIsBridge) {
        $script:F.BackendVerdict = '后端看起来正常（GCUService 在跑 + GCUBridge 在听 13688）'
        Say '    ⇒ 【后端在跑但订阅被掐（CONNACK 成功、断在 Subscribe 这一形态）】'
        Say '       结论：问题在 GCU 后端进程本身（不是 clientId 被占，也不是端口没人听）。'
        Say '       建议：管理员下 -Fix 重启后端服务（GCUService / GCUBridge）后立即复测 LM；'
        Say '             若仍连不上，请回传本次输出 + 一份【新的 LM 诊断包】。'
    } elseif ($script:F.GcuServiceRunning -and -not $script:F.ListenerIsBridge) {
        $script:F.BackendVerdict = '13688 被非 GCUBridge 进程占用：{0}' -f $(if ($script:F.ListenerName) { $script:F.ListenerName } else { '(进程名读取失败)' })
        Say ("    ⇒ ★【13688 被非 GCU 进程占用：{0}】★" -f $(if ($script:F.ListenerName) { $script:F.ListenerName } else { '(进程名读取失败，见 ③)' }))
        Say ("       路径：{0}" -f $(if ($script:F.ListenerPath) { $script:F.ListenerPath } else { '(读取失败)' }))
        Say '       建议：先人工确认这个进程是什么（本脚本不会替你结束进程），确认后可结束它，再管理员下 -Fix 重启 GCUBridge / GCUService。'
        Say '             —— 它可能正是"回了 CONNACK 却撑不起订阅"的那一方。'
    } elseif (-not $script:F.GcuServiceRunning -and $script:F.PortListening -eq $true) {
        $script:F.BackendVerdict = '后端疑似半开：13688 有监听、但后端核心 GCUService 进程不在'
        Say ("    ⇒ 【后端疑似半开：13688 有监听（{0}），但后端核心 GCUService 进程不在】" -f $script:F.ListenerName)
        Say '       建议：用管理员 -Fix 重启 GCUBridge / GCUService（或管理员下 Restart-Service 这两个服务），再复测 LM。'
    } elseif (-not $script:F.GcuServiceRunning -and $script:F.PortListening -eq $false) {
        $script:F.BackendVerdict = '后端疑似未就绪：GCUService 进程不在、13688 无人监听'
        Say '    ⇒ 【后端疑似未就绪：GCUService 进程不在，且 13688 无人监听】'
        if ($script:F.SvcServicePresent) {
            Say '       建议：用管理员 PowerShell 运行本脚本加 -Fix（只重启 GCUBridge / GCUService 这两个已存在的服务），'
            Say '             或管理员下 Restart-Service GCUBridge / GCUService；随后复测 LM。'
        } else {
            Say '       建议：GCUService 既没注册成服务、也没在跑 ⇒ 本脚本的 -Fix 无法把它拉起来（-Fix 只重启已存在的服务，不安装服务）。'
            Say '             请用官方安装器按 Gen40（40 系）重跑 GCU 安装/修复，然后再跑一次本脚本只读采集确认。'
        }
    } elseif ($script:F.PortListening -eq $false) {
        $script:F.BackendVerdict = 'GCUService 在跑但 13688 无人监听'
        Say '    ⇒ 【后端在跑但没把 13688 监听拉起来（端口未就绪）】'
        Say '       建议：管理员下 -Fix 重启 GCUBridge / GCUService；若重启后 13688 仍无监听，请回传本报告。'
    } else {
        $script:F.BackendVerdict = '后端状态未能判定（13688 读取失败）'
        Say '    ⇒ 【后端状态未能判定：13688 的监听者读不到（见 ③ 的 netstat 兜底）】'
        Say '       建议：以管理员再跑一次只读采集；或按 ③ 的 netstat 原文人工确认监听者，再决定是否 -Fix。'
    }
    if (-not $script:F.SvcServicePresent -and $script:F.GcuServiceRunning) {
        Say '    附注：GCUService 未注册成 NT 服务（本机常见形态），它当前是作为后台进程在跑；因此 GCUService 未注册本身不等于"后端未就绪"。'
    }
    if ($script:F.VariantMismatch) {
        Say ("    ⚠ 附注：运行目录变体=【{0}】，与 Gen40 预期（40 系）不一致；重启服务不能修变体问题，需用官方安装器按 40 系重跑。" -f $script:F.VariantMatch)
    }
    if ($script:F.BridgeIs30) {
        Say '    ⚠ 附注：GCUBridge.exe = 30 系那份（77,320 字节），底座那份（682,208 字节）疑似被顶掉；同样需安装器修正，不是重启能解决的。'
    }
    if ($script:F.OpenRevoTakeover -or $script:F.RunRenamed) {
        Say '    ⚠ 附注：发现 OpenRevo 接管痕迹；若 -Fix 后服务又被自动停掉，请先关闭 OpenRevo 的接管再复测。'
    }
    Say ''
    Say '  ★ 该做什么（一句话）：'
    switch -Wildcard ($script:F.BackendVerdict) {
        '13688 被非 GCU*' { Say '     先确认/结束占用 13688 的那个非 GCU 进程，再管理员下 -Fix 重启 GCUBridge / GCUService，然后复测 LM。' }
        '*GCUService 在跑 + GCUBridge 在听 13688*' { Say '     用管理员 -Fix 重启后端服务后复测 LM；若仍显示 GCU 未连接，回传本文件 + 【新的 LM 诊断包】。' }
        '*后端状态未能判定*' { Say '     以管理员再跑一次只读采集（或按 ③ 的 netstat 原文人工确认监听者），拿到监听者身份后再决定是否 -Fix。' }
        default { Say '     管理员下用 -Fix 重启 GCU 后端服务（只动已存在的 GCUBridge / GCUService）后复测 LM；若仍不行，回传本文件 + LM 诊断包。' }
    }
} catch {
    Say ("  [该项读取失败] 后端健康判定：{0}" -f $_.Exception.Message)
    Say ("    位置：第 {0} 行 —— {1}" -f $_.InvocationInfo.ScriptLineNumber, ([string]$_.InvocationInfo.Line).Trim())
}

# ================================================================ ⑧ 结论提示
Section '⑧ 结论提示（自动生成，措辞保守，仅供参考）'

if ($script:F.PortListening -eq $false) {
    [void]$script:Hints.Add('13688 当前无人监听：可能 GCUBridge/GCUService 没有起来。建议先看 ② 的 StartType 是否为 Disabled；若被禁用，可在管理员 PowerShell 里加 -Fix 只把 GCUBridge 设为自动并启动。')
}
if ($script:F.PortListening -eq $true) {
    $who = $script:F.ListenerName
    if (-not $who) { $who = '(进程名读取失败)' }
    [void]$script:Hints.Add(("13688 的监听者可能是 {0}（PID 见 ③）：请对照它的大小/版本落在哪一套变体；若落在 30 系那一列，可能是变体装错（Gen40 机器装了 30 系）。" -f $who))
}
if ($script:F.PortReadFail) {
    [void]$script:Hints.Add('无法用 Get-NetTCPConnection 读取 13688：可能权限不足，建议以管理员重跑一次只读采集，或直接用 netstat 结果判断。')
}
if ($script:F.ListenerPathUnknown) {
    [void]$script:Hints.Add('13688 监听者的可执行文件路径读不到（非管理员常见）：要拿到"路径/大小/版本/哈希"这条最关键的证据，建议以管理员身份再跑一次只读采集。')
}
if ($script:F.BridgeIs30) {
    [void]$script:Hints.Add('GCUBridge.exe 大小 77,320 字节 = 30 系那份：底座那份（682,208）可能被顶掉，疑似装成了 30 系变体，建议用官方安装器重跑并选 40 系。')
}
if ($script:F.VariantMismatch) {
    [void]$script:Hints.Add(("运行目录里的 GCUService.exe 可能是【{0}】，与 Gen40 预期（40 系）不一致：建议用安装器重跑并确认选择 40 系，而不是手工替换文件。" -f $script:F.VariantMatch))
}
if ($script:F.BridgeStart -match 'Disabled') {
    [void]$script:Hints.Add('GCUBridge 的 StartType 是 Disabled：服务可能被别的工具（例如 OpenRevo）禁用过，可考虑在管理员下用 -Fix 恢复为自动并启动。')
}
if ($script:F.OpenRevoDir -or $script:F.OpenRevoProc -or $script:F.OpenRevoTakeover -or $script:F.RunRenamed) {
    [void]$script:Hints.Add('发现 OpenRevo 痕迹（目录/进程/接管日志/Run 键改名）：它可能禁用了 GCUBridge 或改过启动项。建议先关闭 OpenRevo 的接管、重启后再看是否恢复。')
}
if ($script:F.LogMissing) {
    [void]$script:Hints.Add('未找到 LM 日志：日志级别可能是 off。建议改成"全部"后复现一次"GCU 连不上"，再回传日志。')
}
if ($script:F.FwBlock) {
    [void]$script:Hints.Add(("存在 13688 相关防火墙规则（{0}）：这是 LM 自建的入站阻断，通常只影响远程访问、不影响本机回环；若机主是远程/多机形态，可确认它是否误伤。" -f $script:F.FwBlock))
}
if ($script:Hints.Count -eq 0) {
    [void]$script:Hints.Add('本次采集没有触发任何已知形态：建议把日志级别改成"全部"后复现一次，再连同本报告一起回传。')
}
$shown = 0
foreach ($h in $script:Hints) {
    $shown++
    if ($shown -gt 4) { break }
    Say ("  {0}. {1}" -f $shown, $h)
}
if ($shown -lt $script:Hints.Count) { Say ("  （另有 {0} 条次要提示未展开，可对照 ③⑤⑥⑦ 的原文。）" -f ($script:Hints.Count - $shown)) }
Say ''
Say '  以上均为基于本次采集的保守推测（"可能/建议"），不是定论；请结合各节原文判断。'
if ($script:F.BackendVerdict) { Say ("  ⑨ 后端健康判定（一句话）：{0}" -f $script:F.BackendVerdict) }
if ($Fix) { Say ("  本次 -Fix 状态：{0}" -f $script:F.FixApplied) }
else { Say '  本次未加 -Fix：脚本没有做任何修改（未启停服务、未写注册表、未改文件）。' }

# ================================================================ 落盘
if (-not $OutFile) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $desk = ''
    try { $desk = [Environment]::GetFolderPath('Desktop') } catch { $desk = '' }
    if (-not $desk) { $desk = Join-Path $env:USERPROFILE 'Desktop' }
    $OutFile = Join-Path $desk ("GCU诊断-{0}.txt" -f $stamp)
}
Say ''
Say '请把该文件回传（隐私自行打码）：'
Say ("  {0}" -f $OutFile)

$text = $script:Report.ToString()
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
try {
    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($OutFile, $text, $utf8Bom)
    Write-Host ''
    Write-Host ("[完成] 诊断报告已写入：{0}" -f $OutFile) -ForegroundColor Green
    Write-Host '请把该 txt 回传给技术支持（敏感信息可自行打码）。' -ForegroundColor Green
} catch {
    $alt = Join-Path $env:TEMP ("GCU诊断-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    try {
        [System.IO.File]::WriteAllText($alt, $text, $utf8Bom)
        Write-Host ''
        Write-Host ("[提示] 原路径写入失败（{0}），已改写到：{1}" -f $_.Exception.Message, $alt) -ForegroundColor Yellow
    } catch {
        Write-Host ''
        Write-Host ("[警告] 报告落盘失败：{0}" -f $_.Exception.Message) -ForegroundColor Red
        Write-Host '可手动复制上面的屏幕输出。' -ForegroundColor Red
    }
}
