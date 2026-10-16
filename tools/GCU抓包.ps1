#Requires -Version 5.1
<#
    GCU抓包.ps1   （极光Pro / JiguangPro GM5AR0O "GCU 连不上" 一键诊断抓包）
    ==================================================================
    给机主用：管理员跑一次，产出一个 zip，把"GCU 连不上"的关键证据一次性收齐。

    失败形态（本脚本针对的现场）：
      MQTT CONNACK 全部 Success，但 "MQTT 订阅成功" 恒为 0；
      每次 ~4.5s 后客户端自己超时断开（MqttCommunicationTimedOutException / ReasonString=n/a）；
      回退链 _4→_3→_2 后卡在 _2；GCU 已连接: 否、设备数 0、能力位全 (未收到)；
      整机冷启后依旧；GCUBridge 在跑、13688 在 LISTEN。

    本机（翼龙15Pro，GCU 正常）用于验收本脚本本身，不是故障机。

    ------------------------------------------------------------------
    用法（复制粘贴即可；脚本不依赖任何固定盘符/固定目录，放哪都能跑）：
      默认抓 60 秒（把下面的 <脚本路径> 换成你实际存放的位置）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1"

      例如脚本放在桌面：
        powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\Desktop\GCU抓包.ps1"

      抓 120 秒：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1" -CaptureSeconds 120

      指定输出目录（默认是桌面，可换成任意可写目录）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1" -OutDir "$env:USERPROFILE\Desktop\LM-GCU抓包-测试"

      只收文本证据、不抓包：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1" -SkipCapture

      不产 zip（只留目录；默认是产 zip）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1" -Zip:$false
      强制产 zip：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\GCU抓包.ps1" -Zip

    参数：
      -CaptureSeconds <int>    回环抓包时长，默认 60 秒
      -OutDir <path>           输出目录，默认 $env:USERPROFILE\Desktop\LM-GCU抓包-<yyyyMMdd-HHmmss>
      -SkipCapture             跳过 09 回环抓包（只收文本证据）
      -Zip / -Zip:$false       是否产出 zip，默认产出

    ------------------------------------------------------------------
    ⚠ 权限与安全（重要）：
      * 抓包（pktmon / netsh trace）需要管理员；本脚本开头自检，
        非管理员时【明确提示"请以管理员身份重跑"】并自动降级为只收文本证据，
        不会静默失败。文本证据部分普通用户也能收。
      * 默认只读：不启停任何服务/进程、不改注册表/设置、
        不写 EC/NVRAM、不卸载不安装、不连 13688 做任何业务交互
        （抓包是旁路监听，不算连接）。
      * 脚本唯一会启动/停止的东西：它自己的抓包工具（pktmon / netsh trace），
        抓完一定会停，不留后台抓包。
      * 跑完自动清理中间文件（ETL 等），只保留报告目录与最终 zip。

    产出：OutDir 目录 + 同名 .zip（默认）。zip 里含 00~09、11、12~18 各节证据。
          2026-10-09（方案B）新增 12~18：把 LM 内置诊断有、而这个独立脚本缺的检查补上（GCU 日志"可发现"、
          KeyboardManager 两文件、载荷落地核对、灯光判据原文/指纹、UserPofiles 清单、install 日志、模式/风扇快照）。
          ⚠ 这些节全部只读、有上限、有打码；未打包、未改任何系统设置。
          请把生成的 zip 发回来。
#>
[CmdletBinding()]
param(
    [int]$CaptureSeconds = 60,
    [string]$OutDir,
    [switch]$SkipCapture,
    [bool]$Zip = $true,
    # 仅供开发自测：不真抓包，用内置样例报文走完 09 的解析/过滤/结论链路。
    # 机主正常使用不要加这个参数。
    [switch]$SelfTestCapture,
    # 配合 -SelfTestCapture：'zeropkt' = 样例里全是筛选器回显/组件/计数器行（零有效报文），
    # 用来证明"零有效报文 ⇒ 必须回退 netsh"这条分支会走到。
    [string]$SelfTestCaptureSample = '',
    # 跳过 11 独立 MQTT 探针（默认执行；探针只连本机回环、只 CONNECT+SUBSCRIBE，绝不 PUBLISH）
    [switch]$SkipProbe,
    # 扩展探针：把 UWPClient_1/_4 也试一遍（⚠ 必须在【LM 已退出】时用，否则会顶掉 LM 的会话）
    [switch]$ProbeExtended
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$SCRIPT_VERSION = '2026-10-10 r2（方案B：补 12~18 采集节）'

# ================================================================ 运行状态
$script:Warnings = New-Object System.Collections.ArrayList
$script:Steps    = New-Object System.Collections.ArrayList
$script:StepNo   = 0
$script:RunStart = Get-Date
$script:TraceWasActiveBefore = $false
$script:PktmonWasActiveBefore = $false

function Warn {
    param([string]$Text)
    $line = ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text)
    [void]$script:Warnings.Add($line)
    Write-Host ("  ! " + $Text) -ForegroundColor Yellow
}

function Step {
    param([string]$Title)
    $script:StepNo++
    Write-Host ''
    Write-Host ("[{0}] {1}" -f $script:StepNo, $Title) -ForegroundColor Cyan
    return $script:StepNo
}

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-Utf8 {
    # PS 5.1 的 Set-Content -Encoding UTF8 自带 BOM；中文需要 BOM 才不乱码
    param([string]$Path, [string]$Text)
    Set-Content -LiteralPath $Path -Value $Text -Encoding UTF8
}

function Get-Text {
    param([string]$Text, [int]$MaxLines = 0)
    if ($null -eq $Text) { return '' }
    $t = [string]$Text
    if ($MaxLines -gt 0) {
        $arr = $t -split "`r?`n"
        if ($arr.Count -gt $MaxLines) {
            $keep = $arr[0..($MaxLines - 1)]
            $t = ($keep -join "`r`n") + "`r`n...(已截断，原始 " + $arr.Count + " 行)"
        }
    }
    return $t
}

# ================================================================ 40A 期望值（内嵌常量）
# ⚠ 这些期望值【一次性固化在脚本里】，脚本运行时不需要任何外部目录
#   （不读仓库、不读 payload\40、不依赖任何固定盘符），换台机器照样能判"40A 是否落地"。
# 来源：40A 载荷（补全后的 GCU 底座）里那 13 个文件的真实值，一次性固化在这里。
# 结构：相对路径 | 大小(字节) | SHA256前16位(大写) | FileVersion
$Expected40A = [ordered]@{
    'GCUBridge.exe'                                    = '2125552|5EBF6789441C63EB|1.0.1.10'
    'UEFI_Firmware.dll'                                = '134312|10CCF9BCBFBB971D|1.0.0.1'
    'MyControlCenter\GCUService.exe'                   = '14447864|A2B7D81ECF5C1DBD|1.0.2.70'
    'MyControlCenter\UEFI_Firmware.dll'                = '134312|E4902F551DE0A292|1.0.0.1'
    'MyControlCenter\NVControlSetting.dll'             = '2775208|1E483D95EE7A5570|(无)'
    'MyControlCenter\GPUInfoDLL.dll'                   = '2581672|F7ED3FFA383EC6AA|1.0.0.11'
    'MyControlCenter\ACPIDriverDll.dll'                = '4116648|D8698164417A8416|1.0.0.1'
    'MyControlCenter\OSDTpDetect.exe'                  = '525480|C022268F3AA8554F|1.1.0.6'
    'MyControlCenter\RegistryUtils.dll'                = '34984|30C8DD7CB89AAC29|1.0.6467.28800'
    'MyControlCenter\UWAirplane.dll'                   = '133872|4F55C21C15DCACCE|(无)'
    'MyControlCenter\UWDiskInfo.dll'                   = '2754728|935EA7D335E7DD86|1.0.0.1'
    'MyControlCenter\DisplayInfo.dll'                  = '2548976|6E4267CB01D50845|1.0.0.1'
    'MyControlCenter\AppSettings\GamingMonitor.json'   = '36|92BD428D4564E583|(无)'
}
# 便于按"大小/SHA前16"两个关键判据取用
$ExpectedBridgeSize = 2125552
$ExpectedBridgeSha16 = '5EBF6789441C63EB'
$ExpectedGmSize = 36
$ExpectedGmSha16 = '92BD428D4564E583'

function Get-ExpectedRow {
    param([string]$Rel)
    if (-not $Rel) { return $null }
    $k = $Rel.TrimStart('\')
    if (-not $script:Expected40A.Contains($k)) { return $null }
    $parts = ([string]$script:Expected40A[$k]) -split '\|'
    return @{ Rel = $k; Size = [int]$parts[0]; Sha16 = [string]$parts[1]; Ver = [string]$parts[2] }
}

# ================================================================ 隐私打码
# 已知的系统/产品路径分量与产品名（哨兵）：绝不能被当成"域名/长串"打码。
# ⚠ 表里顺序有意义：先命中先保护，所以【长词必须排在短词前面】。
#   踩过的坑：'Program' 会先命中 SystemProductName 里的 "temPro"，还原时变成 SysTemproductName。
$script:SafeWords = @(
    'SystemProductName', 'BIOS_PROJECT_ID',
    'L-Mechrevo', 'MechrevoLite', 'Mechrevo', 'ControlCenter', 'GamingCenter',
    'GCUBridge', 'GCUService', 'GCUServicePlugin', 'OSDTpDetect', 'GCU',
    'AiStoneService', 'AiStageService', 'MyControlCenter', 'GamingMonitor',
    'UEFI_Firmware', 'NVControlSetting', 'GPUInfoDLL', 'ACPIDriverDll',
    'RegistryUtils', 'UWAirplane', 'UWDiskInfo', 'DisplayInfo', 'UWACPIDriver',
    'NPF', 'PktMon', 'Wireshark', 'OpenRevo',
    'Program', 'ProgramData', 'Program Files', 'Program Files (x86)',
    'Windows', 'System32', 'SysWOW64', 'AppData', 'Roaming', 'Local', 'Users',
    'Desktop', 'Documents', 'Downloads', 'Temp', 'SOFTWARE', 'SYSTEM',
    'CurrentControlSet', 'Services', 'ItemSupport', 'ServiceReady',
    'ServiceLoadingReady', 'PerformanceReady', 'Service',
    'Item', 'Support', 'Ready', 'Loading', 'Performance',
    'LocalHost', 'localhost', 'Administrator',
    # 回环地址：保留，否则会打断"对端 PID/端口"的可判断性（内网段前缀太宽，不放进词表）
    '127.0.0.1', '::1', '0.0.0.0', '::'
)

function Protect-Words {
    # 哨兵做法：先把"已知系统路径分量/产品名"换成 U+0001 哨兵，打码完再还原，
    # 这样 Files\L-Mechrevo、ProgramData\L-Mechrevo 这类系统路径不会被当成域名/长串吞掉。
    # ⚠ 必须用真实的 U+0001 字符；"\u0001" 字面量在 .NET 正则里不是转义，会导致哨兵还原不回来。
    param([string]$Text)
    $t = [string]$Text
    if ([string]::IsNullOrEmpty($t)) { return $t }
    $s = [string][char]1
    for ($i = 0; $i -lt $script:SafeWords.Count; $i++) {
        $w = $script:SafeWords[$i]
        try {
            $t = [regex]::Replace($t, [regex]::Escape($w), ($s + 'W' + $i + $s), 'IgnoreCase')
        } catch { }
    }
    return $t
}

function Unprotect-Words {
    param([string]$Text)
    $t = [string]$Text
    if ([string]::IsNullOrEmpty($t)) { return $t }
    $s = [string][char]1
    for ($i = 0; $i -lt $script:SafeWords.Count; $i++) {
        try {
            # ⚠ 替换串必须是"字面量"：这里绝对不能再套一层 [regex]::Escape，
            #   否则还原出来的 127.0.0.1 会变成 127\.0\.0\.1（替换串里的 \ 是转义符）。
            $t = [regex]::Replace($t, [regex]::Escape($s + 'W' + $i + $s), $script:SafeWords[$i])
        } catch { }
    }
    return $t
}

function Get-RedactMap {
    $m = New-Object System.Collections.ArrayList
    try {
        $cn = [string]$env:COMPUTERNAME
        if ($cn) { [void]$m.Add(@{ P = [regex]::Escape($cn); R = '<主机名>' }) }
        # ⚠ 用户名可能出现在 C:\Users\<名>\ 这种路径里，而 $env:USERNAME 在提权/换账户时未必等于
        #    路径里的那段。所以这里把【C:\Users 下所有实际的用户目录名】都收进打码表。
        $unames = New-Object System.Collections.ArrayList
        $un = [string]$env:USERNAME
        if ($un -and $un.Length -ge 2) { [void]$unames.Add($un) }
        try {
            Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
                $n = $_.Name
                if ($n -and $n.Length -ge 2 -and $n -notmatch '^(Public|Default|Default User|All Users|WDAGUtilityAccount|AppData)$') {
                    [void]$unames.Add($n)
                }
            }
        } catch { }
        foreach ($u in ($unames | Select-Object -Unique)) {
            if ($u -and $u.Length -ge 2) { [void]$m.Add(@{ P = [regex]::Escape($u); R = '<用户名>' }) }
        }
        # 整段用户目录（含盘符）也单独打掉，双保险
        $up = [string]$env:USERPROFILE
        if ($up) { [void]$m.Add(@{ P = [regex]::Escape($up); R = '<用户目录>' }) }
        try {
            Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
                [void]$m.Add(@{ P = [regex]::Escape($_.FullName); R = '<用户目录>' })
            }
        } catch { }
        $serials = @()
        foreach ($q in @('Win32_BIOS', 'Win32_ComputerSystemProduct', 'Win32_BaseBoard')) {
            try {
                $o = Get-CimInstance -ClassName $q -ErrorAction Stop
                if ($o) {
                    foreach ($prop in @('SerialNumber', 'IdentifyingNumber', 'UUID')) {
                        try {
                            $v = [string]$o.$prop
                            if ($v -and $v.Trim().Length -ge 4 -and $v -notmatch '^(Default string|To be filled|None|0+)$') {
                                $serials += $v.Trim()
                            }
                        } catch { }
                    }
                }
            } catch { }
        }
        foreach ($s in ($serials | Select-Object -Unique)) {
            [void]$m.Add(@{ P = [regex]::Escape($s); R = '<序列号>' })
        }
    } catch { }
    return $m
}

function Redact-Text {
    # 打码顺序很关键：
    #   a) 先做【形状判定】的长串/MAC（此时系统路径还没被哨兵替换，但形状规则本身
    #      要求 32+ 位连续十六进制或 40+ 位连续 base64 字符，不会命中普通路径）；
    #   b) 再做【整串替换】主机名/用户名/用户目录/序列号；
    #   c) 最后用"保护已知系统路径分量哨兵"还原 —— 这样 Files\L-Mechrevo、
    #      ProgramData\L-Mechrevo 之类的系统路径绝不会被当成域名/长串吞掉。
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $t = [string]$Text
    if ($t.Length -eq 0) { return $t }
    $t = Protect-Words $t
    # 1) 长十六进制串 / GUID / 长 base64 串：只保形状判定，且保留前 16 位可判断性
    try {
        $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])', {
            param($m)
            $v = $m.Value
            if ($v.Length -le 16) { return $v }
            return ($v.Substring(0, 16) + '<已打码>')
        })
    } catch { }
    try {
        # 只打码"确实像 base64/哈希"的长串：40 位以上，且同时含小写字母、大写字母、数字。
        # ⚠ 不能放宽成任意 [0-9A-Za-z+/]{40,}，否则 SystemProductName 这种普通单词也会被吞掉。
        $t = [regex]::Replace($t, '(?<![0-9A-Za-z+/])[0-9A-Za-z+/]{40,}={0,2}(?![0-9A-Za-z+/=])', {
            param($m)
            $v = $m.Value
            $hasLower = $v -cmatch '[a-z]'
            $hasUpper = $v -cmatch '[A-Z]'
            $hasDigit = $v -match '[0-9]'
            if (-not ($hasLower -and $hasUpper -and $hasDigit)) { return $v }
            return ($v.Substring(0, 16) + '<已打码>')
        })
    } catch { }
    # 2) MAC（含 - : 两种写法）。注意 GUID 形状以 { 开头，不会被这条吞掉
    try { $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])([0-9A-Fa-f]{2}([-:]))([0-9A-Fa-f]{2}\2){4}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])', '<MAC>') } catch { }
    # 2b) IPv6：只打码网络前缀，保留最后一段 —— 末段常含"Pid:端口"，是判断对端身份的关键，不能打掉
    try {
        $t = [regex]::Replace($t, '(?<![:0-9A-Fa-f])(?:[0-9A-Fa-f]{1,4}:){2,7}:[0-9A-Fa-f]{1,4}(?![:0-9A-Fa-f])', {
            param($m)
            $v = $m.Value
            $ip = $v
            $port = ''
            $mm = [regex]::Match($v, '^(.*?):(\d{1,5})$')
            if ($mm.Success) {
                $ip = $mm.Groups[1].Value
                $port = ':' + $mm.Groups[2].Value
            }
            $i = $ip.LastIndexOf(':')
            if ($i -le 0) { return $v }
            return ($ip.Substring(0, $i + 1) + 'xxxx' + $port)
        })
    } catch { }
    # 3) 主机名/用户名/用户目录/序列号（整串）
    #    ⚠ 回环地址 ::1 / 127.0.0.1 已在保护词表里，不会被上面的形状规则打死
    $skippedBySentinel = New-Object System.Collections.ArrayList
    foreach ($e in $script:RedactMap) {
        try {
            $before = $t
            $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase')
            # 没换成 ⇒ 多半是被哨兵保护挡住了（典型：C:\Users\<用户名> 里的用户名段
            # 变成了哨兵，导致整串匹配不上）。记下来，等哨兵还原后再补一刀。
            if ($t -ceq $before -and $e.R -in @('<用户名>', '<主机名>', '<序列号>', '<用户目录>')) {
                [void]$skippedBySentinel.Add($e)
            }
        } catch { }
    }
    $t = Unprotect-Words $t
    # 3b) 哨兵还原后【补一刀】：这一步是关键，否则 C:\Users\<用户名>\... 会原样漏出去
    #     （真机实测就是这样漏的用户名：主机名打了、用户名没打）
    foreach ($e in $skippedBySentinel) {
        try { $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase') } catch { }
    }
    # 3c) 最后再兜一次"裸用户名/裸主机名"：即使上面某条因为形状原因没命中，这一步也能兜住。
    try {
        if ($script:BareNamePatterns -and $script:BareNamePatterns.Count -gt 0) {
            foreach ($bp in $script:BareNamePatterns) {
                try { $t = [regex]::Replace($t, $bp.P, $bp.R, 'IgnoreCase') } catch { }
            }
        }
    } catch { }
    return $t
}

$script:RedactMap = Get-RedactMap

# 把 C:\Users 下的实际用户目录名也塞进保护词表：它们本身要被打码，
# 但"先保护后打码"的顺序保证它们不会被形状规则当成域名/长串吞掉。
$script:BareNamePatterns = New-Object System.Collections.ArrayList
try {
    $extraNames = New-Object System.Collections.ArrayList
    $u0 = [string]$env:USERNAME
    if ($u0 -and $u0.Length -ge 2) { [void]$extraNames.Add($u0) }
    try {
        Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $n = $_.Name
            if ($n -and $n.Length -ge 2 -and $n -notmatch '^(Public|Default|Default User|All Users|WDAGUtilityAccount|AppData)$') { [void]$extraNames.Add($n) }
        }
    } catch { }
    foreach ($n in ($extraNames | Select-Object -Unique)) {
        if ($n -and $n.Length -ge 2) {
            if ($script:SafeWords -notcontains $n) { $script:SafeWords += $n }
            # 3c 兜底用：裸用户名（含"主机名\用户名"里的那半段）
            [void]$script:BareNamePatterns.Add(@{ P = [regex]::Escape($n); R = '<用户名>' })
        }
    }
} catch { }

# ================================================================ 工具集
function Test-Admin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $pr = New-Object Security.Principal.WindowsPrincipal($id)
        return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Invoke-Safe {
    # 每项独立 try/catch：单项失败只写"该项失败（原因）"，绝不整体中断
    param(
        [string]$Label,
        [scriptblock]$Action,
        $Fallback = $null
    )
    try {
        $r = & $Action
        return $r
    } catch {
        Warn ("{0}：该项失败（{1}）" -f $Label, $_.Exception.Message)
        return $Fallback
    }
}

function Get-Cmd {
    param([string]$Name)
    try { return (Get-Command -Name $Name -ErrorAction Stop).Source } catch { return $null }
}

function Get-PidMap {
    # PID -> @{ Name; Path }。⚠ PowerShell 5.1 非管理员下 $proc.Path 会抛 Access denied，
    # 所以路径优先用 CIM（Win32_Process.ExecutablePath，非管理员也能读到自己的进程）。
    $map = @{}
    try {
        foreach ($p in @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)) {
            $n = ''
            $ep = ''
            try { $n = [string]$p.Name } catch { }
            try { $ep = [string]$p.ExecutablePath } catch { }
            if ($n -and $n.EndsWith('.exe')) { $n = $n.Substring(0, $n.Length - 4) }
            $map[[int]$p.ProcessId] = @{ Name = $n; Path = $ep; How = 'CIM' }
        }
    } catch {
        Warn ('CIM 进程表读取失败（改用 Get-Process）：' + $_.Exception.Message)
    }
    try {
        foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
            $id2 = [int]$p.Id
            $cur = $null
            if ($map.ContainsKey($id2)) { $cur = $map[$id2] }
            if ($null -eq $cur) {
                $n2 = ''
                try { $n2 = [string]$p.ProcessName } catch { }
                $map[$id2] = @{ Name = $n2; Path = ''; How = 'Get-Process(无路径)' }
            } elseif (-not $cur.Path) {
                # 自己拥有的进程，Get-Process.Path 也能读到；管理员下覆盖面更全
                $pp2 = ''
                try { $pp2 = [string]$p.Path } catch { $pp2 = '' }
                if ($pp2) { $map[$id2] = @{ Name = $cur.Name; Path = $pp2; How = 'Get-Process' } }
            }
        }
    } catch { }
    return $map
}

function Resolve-ProcFromMap {
    param($Map, [int]$ProcId)
    if ($Map -and $Map.ContainsKey($ProcId)) {
        $e = $Map[$ProcId]
        $pth = $e.Path
        if (-not $pth) { $pth = '(读取失败或需管理员)' }
        return @{ Name = $e.Name; Path = $pth }
    }
    return @{ Name = ('(PID {0} 已退出或读取失败)' -f $ProcId); Path = '(读取失败)' }
}

function Select-Lines {
    # 抽取匹配多个关键词的行，返回 @{ Lines=@(); Total=N }
    # ⚠ Keep='tail' 时保留【最后 Max 行】（诊断要的是最新现场）；Keep='head' 保留最前 Max 行。
    param([string[]]$Lines, [string]$Pattern, [int]$Max = 200, [string]$Keep = 'head')
    $re = $null
    try { $re = [regex]::new($Pattern, 'IgnoreCase') } catch { return @{ Lines = @(); Total = 0 } }
    $total = 0
    if ($Keep -eq 'tail') {
        $q = New-Object System.Collections.Queue
        foreach ($l in $Lines) {
            if ($null -eq $l) { continue }
            if ($re.IsMatch($l)) {
                $total++
                if ($Max -gt 0 -and $q.Count -ge $Max) { [void]$q.Dequeue() }
                $q.Enqueue($l)
            }
        }
        return @{ Lines = $q.ToArray(); Total = $total; Truncated = ($Max -gt 0 -and $total -gt $Max); Dropped = [Math]::Max(0, $total - $Max) }
    }
    $out = New-Object System.Collections.ArrayList
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        if ($re.IsMatch($l)) {
            $total++
            if ($Max -le 0 -or $out.Count -lt $Max) { [void]$out.Add($l) }
        }
    }
    return @{ Lines = $out.ToArray(); Total = $total; Truncated = ($Max -gt 0 -and $total -gt $Max); Dropped = [Math]::Max(0, $total - $Max) }
}

function Read-LinesShared {
    # 以"允许别人同时写"的方式读文件 —— LM / 底座日志经常被正在运行的进程占着，
    # 普通 ReadLines 会因"being used by another process"整项失败。
    param([string]$Path, [int]$MaxLines = 500, [bool]$Tail = $true)
    $fs = $null
    $sr = $null
    try {
        $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8, $true)
        if (-not $Tail) {
            $all = New-Object System.Collections.ArrayList
            while (-not $sr.EndOfStream) {
                [void]$all.Add($sr.ReadLine())
                if ($MaxLines -gt 0 -and $all.Count -ge $MaxLines) { break }
            }
            return $all.ToArray()
        }
        $q = New-Object System.Collections.Queue
        while (-not $sr.EndOfStream) {
            $l = $sr.ReadLine()
            if ($q.Count -ge $MaxLines) { [void]$q.Dequeue() }
            $q.Enqueue($l)
        }
        return $q.ToArray()
    } finally {
        if ($sr) { try { $sr.Dispose() } catch { } }
        if ($fs) { try { $fs.Dispose() } catch { } }
    }
}

function Get-TailLines {
    # 只读文件尾部若干行，不整文件载入内存（大日志友好）；共享读，容忍文件被占用
    param([string]$Path, [int]$MaxLines = 500)
    return (Read-LinesShared -Path $Path -MaxLines $MaxLines -Tail $true)
}

function Get-Sha16 {
    param([string]$Path)
    try {
        $h = (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($h.Length -ge 16) { return $h.Substring(0, 16) }
        return $h
    } catch {
        return ('(读取失败：{0})' -f $_.Exception.Message)
    }
}

function Get-SigBlock {
    # 单个文件签名块：存在性/大小/版本/哈希前16位
    param([string]$Label, [string]$Path)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine(("  " + $Label))
    [void]$sb.AppendLine(("    路径       : " + $Path))
    if (-not (Test-Path -LiteralPath $Path)) {
        [void]$sb.AppendLine("    状态       : 不存在")
        return $sb.ToString()
    }
    try {
        $fi = Get-Item -LiteralPath $Path -ErrorAction Stop
        [void]$sb.AppendLine(("    大小       : {0} 字节" -f $fi.Length))
        $fv = ''
        $pv = ''
        try { $fv = [string]$fi.VersionInfo.FileVersion } catch { }
        try { $pv = [string]$fi.VersionInfo.ProductVersion } catch { }
        if (-not $fv) { $fv = '(无)' }
        if (-not $pv) { $pv = '(无)' }
        [void]$sb.AppendLine(("    FileVersion: " + $fv))
        [void]$sb.AppendLine(("    ProductVer : " + $pv))
        [void]$sb.AppendLine(("    修改时间   : " + $fi.LastWriteTime))
        [void]$sb.AppendLine(("    SHA256前16 : " + (Get-Sha16 -Path $Path)))
    } catch {
        [void]$sb.AppendLine(("    状态       : 该项失败（{0}）" -f $_.Exception.Message))
    }
    return $sb.ToString()
}

# ================================================================ 参数准备
if (-not $OutDir -or $OutDir.Trim().Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $desktop = ''
    try { $desktop = [Environment]::GetFolderPath('Desktop') } catch { }
    if (-not $desktop) { $desktop = Join-Path $env:USERPROFILE 'Desktop' }
    $OutDir = Join-Path $desktop ("LM-GCU抓包-" + $stamp)
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
Ensure-Dir $OutDir

$isAdmin = Test-Admin
# ⚠ 事实袋：用 PSCustomObject 且【一次把键写全】——PSCustomObject 不允许中途新增属性
$P = [pscustomobject]@{
    OutDir             = $OutDir
    Admin              = $isAdmin
    SkipReasonNonAdmin = $false
    Model              = ''
    BaseBoard          = ''
    Bios               = ''
    OsName             = ''
    OsBuild            = ''
    Uptime             = ''
    Project            = ''
    LmVersion          = ''
    ProcCount          = 0
    ProcNames          = ''
    SvcRows            = @()
    AppxCount          = 0
    CcProcCount        = 0
    CcVerdict          = ''
    TcpRows            = @()
    ListenerName       = ''
    EstabCount         = 0
    PeerNames          = ''
    PeerPidList        = ''
    ReadyVals          = @{}
    ReadyLine          = ''
    BaseRoot           = ''
    BaseRootFound      = $false
    MccRoot            = ''
    MccHow             = ''
    GcuFileCount       = 0
    Landed40A          = '无法判定'
    BridgeSize         = $null
    GmSize             = $null
    Ok40A              = 0
    Mis40A             = 0
    Miss40A            = 0
    InstallStatus      = ''
    InstallStatusValue = ''
    LmLog              = ''
    KeyLines06         = 0
    KeyLines07         = 0
    CapMode            = '未执行'
    CapSeconds         = 0
    CapLines           = 0
    CapValid           = 0
    CapVerdicts        = $null
    Direction          = ''
    ProbeOverall       = ''
    ProbeConclusion    = ''
    ProbeAnySubAck     = $false
    ProbeConnAckOk     = $false
    ProbeRan           = $false
    # ---- 2026-10-09（方案B）12~18 节新增的事实袋字段（⚠ PSCustomObject 不许中途加键，一次写全）----
    LogDiscHits        = 0
    LogDiscRecent24h   = 0
    LogDiscGcuHits     = 0
    LogDiscTailFiles   = 0
    LogDiscKeyLines    = 0
    KmState            = ''
    PayloadSha         = ''
    PayloadShaVariant  = ''
    PayloadShaVerdict  = ''
    PayloadKeyChecked  = 0
    PayloadKeyOk       = 0
    UserPofilesState   = ''
    UserPofilesMissing = 0
    UserPofilesExtra   = 0
    LightPhantom       = ''
    LightLogoEvidence  = ''
    LightLogLines      = 0
    InstallLogState    = ''
    PerfActive         = ''
    PerfKeyCount       = 0
}

# ================================================================ 头部打印
Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' 极光Pro / GCU 连不上 —— 一键诊断抓包（默认只读）' -ForegroundColor White
Write-Host (" 脚本版本 : " + $SCRIPT_VERSION)
Write-Host (" 运行时间 : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host (" 输出目录 : " + $OutDir)
Write-Host (" 抓包时长 : {0} 秒（-SkipCapture 可跳过）" -f $CaptureSeconds)
Write-Host '============================================================' -ForegroundColor White

if (-not $isAdmin) {
    Write-Host ''
    Write-Host '############################################################' -ForegroundColor Red
    Write-Host '#  【请以管理员身份重跑】当前不是管理员！' -ForegroundColor Red
    Write-Host '#  抓包（pktmon / netsh trace）必须管理员权限。' -ForegroundColor Red
    if ($SkipCapture) {
        Write-Host '#  本次按 -SkipCapture 主动跳过 09 抓包，只收文本证据。' -ForegroundColor Red
    } else {
        Write-Host '#  本次将自动跳过 09 回环抓包，只收文本证据。' -ForegroundColor Red
    }
    Write-Host '#  重跑方法：开始菜单搜 PowerShell → 右键"以管理员身份运行" → 粘贴' -ForegroundColor Red
    Write-Host ('#    powershell -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"') -ForegroundColor Red
    Write-Host '############################################################' -ForegroundColor Red
    $P.SkipReasonNonAdmin = $true
} else {
    Write-Host ' 权限自检 : 已是管理员，可以抓包。' -ForegroundColor Green
}

# ================================================================ 机器/系统基本信息（供 00-概要.txt）
Step '机器与系统基本信息' | Out-Null

$P.Model = ''
$P.BaseBoard = ''
try {
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
    $P.Model = ([string]$cs.Manufacturer + ' ' + [string]$cs.Model).Trim()
} catch { Warn ('机器型号读取失败：' + $_.Exception.Message) }
try {
    $bb = Get-CimInstance -ClassName Win32_BaseBoard -ErrorAction Stop
    $P.BaseBoard = ([string]$bb.Manufacturer + ' ' + [string]$bb.Product + ' ' + [string]$bb.Version).Trim()
} catch { Warn ('主板读取失败：' + $_.Exception.Message) }
$P.Bios = ''
try {
    $b = Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop
    $P.Bios = [string]$b.SMBIOSBIOSVersion
    if (-not $P.Bios) { $P.Bios = [string]$b.Version }
} catch { Warn ('BIOS 读取失败：' + $_.Exception.Message) }
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $P.OsName = [string]$os.Caption
    $P.OsBuild = [string]$os.BuildNumber
    try {
        $up = (Get-Date) - $os.LastBootUpTime
        $P.Uptime = ('{0} 天 {1} 小时 {2} 分（开机于 {3}）' -f $up.Days, $up.Hours, $up.Minutes, $os.LastBootUpTime.ToString('yyyy-MM-dd HH:mm:ss'))
    } catch { $P.Uptime = '(开机时长计算失败)' }
} catch { Warn ('OS 信息读取失败：' + $_.Exception.Message) }
# 项目号：先注册表 BIOS_PROJECT_ID，再退回机型/BIOS 直判
$P.Project = ''
try {
    $rk = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('HARDWARE\DESCRIPTION\System\BIOS')
    if ($rk) {
        foreach ($n in @('BIOS_PROJECT_ID', 'ProjectId', 'SystemProductName')) {
            try { $v = [string]$rk.GetValue($n); if ($v) { $P.Project += ($n + '=' + $v + ' ') } } catch { }
        }
        $rk.Close()
    }
} catch { }
try {
    $pkey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\OEM\GamingCenter2\ItemSupport')
    if ($pkey) {
        $v2 = [string]$pkey.GetValue('BIOS_PROJECT_ID')
        if ($v2) { $P.Project += ('ItemSupport\BIOS_PROJECT_ID=' + $v2) }
        $pkey.Close()
    }
} catch { }
if (-not $P.Project) { $P.Project = '(未取到)' }
if ($P.Model -match 'GM5AR0O|JiguangPro|极光') {
    $P.Project += ' ⇒ 判为极光Pro (JiguangPro GM5AR0O / project IDO / Gen40)'
} elseif ($P.Model -match 'GM5HG0A') {
    $P.Project += ' ⇒ 判为翼龙15Pro (GM5HG0A / project IDA / Gen40)，即"可用机"基线'
}
# LM 版本：安装态 DLL 的 FileVersion + ProductVersion
$P.LmVersion = ''
$lmDll = Join-Path $env:ProgramFiles 'L-Mechrevo\L-Mechrevo.dll'
try {
    if (Test-Path -LiteralPath $lmDll) {
        $fi = Get-Item -LiteralPath $lmDll -ErrorAction Stop
        $fv = ''
        $pv = ''
        try { $fv = [string]$fi.VersionInfo.FileVersion } catch { }
        try { $pv = [string]$fi.VersionInfo.ProductVersion } catch { }
        $P.LmVersion = ('FileVersion=' + $(if ($fv) { $fv } else { '(无)' }) + ' / ProductVersion=' + $(if ($pv) { $pv } else { '(无)' }) + '（' + $lmDll + '）')
    } else {
        $P.LmVersion = ('(未找到 ' + $lmDll + ')')
    }
} catch { $P.LmVersion = ('(读取失败：' + $_.Exception.Message + ')') }
Write-Host ('  机器：' + $P.Model)
Write-Host ('  BIOS：' + $P.Bios + '   OS build ' + $P.OsBuild)
Write-Host ('  LM  ：' + $P.LmVersion)

# ================================================================ 01 进程与服务
Step '01 进程与服务 / 官方控制台' | Out-Null

function Get-ProcRows {
    # 返回 psobject 列表：Name/Id/Path/StartTime
    # 路径优先取 CIM 版进程表（PS 5.1 非管理员下 $proc.Path 会 Access denied）
    $rows = New-Object System.Collections.ArrayList
    $all = @()
    try { $all = @(Get-Process -ErrorAction SilentlyContinue) } catch { $all = @() }
    $hit = @()
    foreach ($pr in $all) {
        $pn = ''
        try { $pn = [string]$pr.ProcessName } catch { }
        $pp = ''
        if ($script:PidMap -and $script:PidMap.ContainsKey([int]$pr.Id)) { $pp = [string]$script:PidMap[[int]$pr.Id].Path }
        $isHit = $false
        if ($pn -match '^(GCUBridge|GCUService|OSDTpDetect)$') { $isHit = $true }
        elseif ($pp -and ($pp -match '\\GCU\\')) { $isHit = $true }
        if ($isHit) { $hit += $pr }
    }
    foreach ($pr in $hit) {
        $pn = ''
        try { $pn = [string]$pr.ProcessName } catch { }
        $pp = ''
        $how = ''
        if ($script:PidMap -and $script:PidMap.ContainsKey([int]$pr.Id)) {
            $pp = [string]$script:PidMap[[int]$pr.Id].Path
            $how = [string]$script:PidMap[[int]$pr.Id].How
        }
        if (-not $pp) { $pp = ('(读取失败或需管理员' + $(if ($how) { '：' + $how } else { '' }) + ')') }
        $st = ''
        try { $st = [string]$pr.StartTime } catch { $st = ('(读取失败：{0})' -f $_.Exception.Message) }
        [void]$rows.Add((New-Object psobject -Property @{
            Name = $pn; Id = $pr.Id; Path = $pp; StartTime = $st
        }))
    }
    return $rows
}

# 进程表（PID -> 名字/路径），02/01 都要用
$script:PidMap = Get-PidMap

$procRows = @(Invoke-Safe -Label '进程枚举' -Action { Get-ProcRows } -Fallback @())
$P.ProcCount = $procRows.Count
$P.ProcNames = ($procRows | ForEach-Object { $_.Name } | Select-Object -Unique) -join ','

$svcRows = @()
foreach ($sn in @('GCUBridge', 'GCUService')) {
    $one = Invoke-Safe -Label ("服务 " + $sn) -Action {
        $svc = Get-CimInstance -ClassName Win32_Service -Filter ("Name='" + $sn + "'") -ErrorAction Stop
        if (-not $svc) {
            return (New-Object psobject -Property @{ Name = $sn; Present = $false; State = ''; StartMode = ''; PathName = ''; ProcessId = '' })
        }
        return (New-Object psobject -Property @{
            Name = $sn; Present = $true; State = [string]$svc.State; StartMode = [string]$svc.StartMode
            PathName = [string]$svc.PathName; ProcessId = [string]$svc.ProcessId
        })
    } -Fallback (New-Object psobject -Property @{ Name = $sn; Present = $false; State = '(读取失败)'; StartMode = ''; PathName = ''; ProcessId = '' })
    $svcRows += $one
}
$P.SvcRows = $svcRows

# 官方控制台：三条线索都要查（只看 MSIX 名会漏）
#  ① Appx 注册：Get-AppxPackage *ControlCenter*
#  ② 进程：进程名含 ControlCenter，或进程路径含 WindowsApps\ControlCenter*
#  ③ 日志：C:\ProgramData\ControlCenter 下最近的 .log（还在写 => 可能还有组件在跑）
$appxRows = @(Invoke-Safe -Label 'MSIX 包枚举 *ControlCenter*' -Action {
    @(Get-AppxPackage -ErrorAction Stop | Where-Object { $_.Name -like '*ControlCenter*' -or $_.PackageFullName -like '*ControlCenter*' } |
        Select-Object Name, PackageFullName, Version, InstallLocation, Status)
} -Fallback @())
$P.AppxCount = $appxRows.Count
$ccProc = @()
try {
    $allP = @(Get-Process -ErrorAction SilentlyContinue)
    $ccProc = @($allP | Where-Object {
        try {
            $n = [string]$_.ProcessName
            $pp = ''
            if ($script:PidMap -and $script:PidMap.ContainsKey([int]$_.Id)) { $pp = [string]$script:PidMap[[int]$_.Id].Path }
            ($n -like 'ControlCenter*') -or ($pp -and $pp -match 'WindowsApps\\ControlCenter')
        } catch { $false }
    })
} catch { }
$P.CcProcCount = $ccProc.Count
# ③ ProgramData\ControlCenter 近期日志
$ccLogDir = Join-Path $env:ProgramData 'ControlCenter'
$ccRecentLogs = @()
$ccLatestLog = $null
try {
    if (Test-Path -LiteralPath $ccLogDir) {
        $ccRecentLogs = @(Get-ChildItem -LiteralPath $ccLogDir -Filter '*.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        if ($ccRecentLogs.Count -gt 0) { $ccLatestLog = $ccRecentLogs[0] }
    }
} catch { }
$ccLogFresh = $false
$ccLogAgeMin = -1
if ($ccLatestLog) {
    try {
        $ccLogAgeMin = [int]((Get-Date) - $ccLatestLog.LastWriteTime).TotalMinutes
        if ($ccLogAgeMin -le 60) { $ccLogFresh = $true }
    } catch { }
}
# 分项结论
$ccV1 = if ($ccProc.Count -gt 0) { '仍在运行' } else { '未在运行' }
$ccV2 = if ($appxRows.Count -gt 0) { ('仍注册（' + $appxRows.Count + ' 个包）') } else { '未注册' }
$ccV3 = if ($ccRecentLogs.Count -eq 0) { '无日志' }
        elseif ($ccLogFresh) { ('只有日志（最新 ' + $ccLatestLog.Name + '，' + $ccLogAgeMin + ' 分钟前，仍在写）') }
        else { ('只有日志（最新 ' + $ccLatestLog.Name + '，' + $ccLogAgeMin + ' 分钟前，已停写）') }
$P.CcVerdict = ('运行=' + $ccV1 + '；注册=' + $ccV2 + '；日志=' + $ccV3)

$s01 = New-Object System.Text.StringBuilder
[void]$s01.AppendLine('01 进程与服务（只读采集）')
[void]$s01.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s01.AppendLine('')
[void]$s01.AppendLine('== GCU 相关进程（GCUBridge / GCUService / OSDTpDetect / 路径含 \GCU\ 的进程）==')
if ($procRows.Count -eq 0) {
    [void]$s01.AppendLine('  （未发现任何 GCU 相关进程）')
} else {
    foreach ($r in $procRows) {
        [void]$s01.AppendLine(("  Name={0}  Id={1}" -f $r.Name, $r.Id))
        [void]$s01.AppendLine(("    Path      : " + $(if ($r.Path) { $r.Path } else { '(读取失败或为空)' })))
        [void]$s01.AppendLine(("    StartTime : " + $(if ($r.StartTime) { $r.StartTime } else { '(读取失败)' })))
    }
}
[void]$s01.AppendLine('')
[void]$s01.AppendLine('== SCM 服务状态（GCUBridge / GCUService）==')
foreach ($r in $svcRows) {
    if (-not $r.Present) {
        [void]$s01.AppendLine(("  {0}：服务不存在或该项读取失败" -f $r.Name))
        [void]$s01.AppendLine(("    Status/StartType : " + $r.State + ' / ' + $r.StartMode))
        continue
    }
    [void]$s01.AppendLine(("  {0}" -f $r.Name))
    [void]$s01.AppendLine(("    Status    : " + $r.State))
    [void]$s01.AppendLine(("    StartType : " + $r.StartMode))
    [void]$s01.AppendLine(("    ImagePath : " + $r.PathName))
    [void]$s01.AppendLine(("    ProcessId : " + $r.ProcessId))
}
[void]$s01.AppendLine('')
[void]$s01.AppendLine('== 官方控制台：三条线索分项结论 ==')
[void]$s01.AppendLine('  ⚠ 只看 MSIX 包名会漏（真机上曾出现"包名查不到、但 ControlCenter 日志还在写"的矛盾）。')
[void]$s01.AppendLine(('  ① 进程   ：' + $ccV1 + '（匹配 ControlCenter* 进程名或 WindowsApps\ControlCenter* 路径，共 ' + $ccProc.Count + ' 个）'))
[void]$s01.AppendLine(('  ② 注册   ：' + $ccV2 + '（Get-AppxPackage *ControlCenter*）'))
[void]$s01.AppendLine(('  ③ 日志   ：' + $ccV3 + '（目录：' + $ccLogDir + '）'))
[void]$s01.AppendLine(('  ⇒ 汇总   ：' + $P.CcVerdict))
[void]$s01.AppendLine('')
[void]$s01.AppendLine('  --- ① 进程明细 ---')
if ($ccProc.Count -eq 0) {
    [void]$s01.AppendLine('    （没有 ControlCenter 相关进程）')
} else {
    foreach ($cp in $ccProc) {
        $cpath = ''
        if ($script:PidMap -and $script:PidMap.ContainsKey([int]$cp.Id)) { $cpath = [string]$script:PidMap[[int]$cp.Id].Path }
        if (-not $cpath) { $cpath = '(读取失败或需管理员)' }
        [void]$s01.AppendLine(("    - {0} (Id={1}) {2}" -f $cp.ProcessName, $cp.Id, $cpath))
    }
}
[void]$s01.AppendLine('  --- ② Appx 包明细（*ControlCenter*）---')
if ($appxRows.Count -eq 0) {
    [void]$s01.AppendLine('    未发现 ControlCenter* 包（或该项读取失败，见脚本告警）')
} else {
    foreach ($a in $appxRows) {
        [void]$s01.AppendLine(("    Name         : " + $a.Name))
        [void]$s01.AppendLine(("    PackageFull  : " + $a.PackageFullName))
        [void]$s01.AppendLine(("    Version      : " + $a.Version))
        [void]$s01.AppendLine(("    InstallPath  : " + $a.InstallLocation))
    }
}
[void]$s01.AppendLine('  --- ③ ProgramData\ControlCenter 下的日志（最多列 6 个）---')
if ($ccRecentLogs.Count -eq 0) {
    [void]$s01.AppendLine('    （没有 .log，或目录不存在）')
} else {
    foreach ($lg in ($ccRecentLogs | Select-Object -First 6)) {
        [void]$s01.AppendLine(("    {0}  {1} 字节  mtime {2}" -f $lg.Name, $lg.Length, $lg.LastWriteTime))
    }
    [void]$s01.AppendLine(('    最新日志距今：{0} 分钟（<=60 分钟视为"仍在写"）' -f $ccLogAgeMin))
}
Write-Utf8 -Path (Join-Path $OutDir '01-进程与服务.txt') -Text $s01.ToString()

# ================================================================ 02 端口与连接
Step '02 端口与连接（13688）' | Out-Null

function Resolve-PidInfo {
    param([int]$ProcId)
    return (Resolve-ProcFromMap -Map $script:PidMap -ProcId $ProcId)
}

$tcpRows = @(Invoke-Safe -Label 'Get-NetTCPConnection 13688' -Action {
    @(Get-NetTCPConnection -LocalPort 13688 -ErrorAction Stop | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess)
} -Fallback @())
$netstatRaw = [string](Invoke-Safe -Label 'netstat 13688' -Action {
    (& netstat -ano | Select-String '13688' | ForEach-Object { $_.ToString() }) -join "`r`n"
} -Fallback '(该项失败)')

$P.TcpRows = $tcpRows
$listen = @($tcpRows | Where-Object { $_.State -eq 'Listen' })
$estab  = @($tcpRows | Where-Object { $_.State -eq 'Established' })

# ⚠ 重要更正：netstat -ano 每行是「本地地址:端口 / 远端地址:端口 / 状态 / PID」，
#   那一列 PID 是【持有该本地端口的本地进程】，不是对端！（真机实测：曾被误当成对端，
#   出现 "StartMenuExperienceHost(13688)" 这种"PID 恰好等于端口号"的荒谬结果。）
#   所以对端 PID 必须【独立反查】：拿 LocalPort=13688 的 ESTABLISHED 四元组，
#   再用 Get-NetTCPConnection -RemotePort 13688 找到"本地端口 == 对端端口"的那条连接，
#   它的 OwningProcess 才是对端；最后用四元组精确匹配。
$netstatLines = @()
try {
    foreach ($ln in ($netstatRaw -split "`r?`n")) {
        $t = $ln.Trim()
        if (-not $t) { continue }
        if ($t -match '^(TCP|UDP)\s') {
            # 只标注"这是本地 PID"，不做任何对端推断
            $cols = @($t -split '\s+' | Where-Object { $_ -ne '' })
            if ($cols.Count -ge 5 -and $cols[-1] -match '^\d+$') {
                $lp = [int]$cols[-1]
                $info = Resolve-ProcFromMap -Map $script:PidMap -ProcId $lp
                $netstatLines += ('  ' + $t + '   ← 该行末列 PID ' + $lp + ' 是【本地端】持有者：' + $info.Name)
            } else {
                $netstatLines += ('  ' + $t)
            }
        } else {
            $netstatLines += $ln
        }
    }
} catch {
    $netstatLines = @($netstatRaw)
    Warn ('netstat 行解析失败：' + $_.Exception.Message)
}
$netstatOut = ($netstatLines -join "`r`n")

# ---- 对端 PID 独立反查 ----
$revRows = @(Invoke-Safe -Label 'Get-NetTCPConnection -RemotePort 13688' -Action {
    @(Get-NetTCPConnection -RemotePort 13688 -ErrorAction Stop | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess)
} -Fallback @())
$peerPidInfo = @{}
$peerMethod = '无法判定'
try {
    if ($estab.Count -eq 0) {
        $peerMethod = '无 ESTABLISHED 会话，谈不上对端'
    } else {
        $matched = 0
        foreach ($r in $estab) {
            # 精确四元组反查：对端地址:端口 == 我们看到的 Remote；对端本地端口 == 我们的 LocalPort
            $hit = @($revRows | Where-Object {
                try {
                    ([string]$_.LocalAddress -eq [string]$r.RemoteAddress) -and
                    ([int]$_.LocalPort -eq [int]$r.RemotePort) -and
                    ([int]$_.RemotePort -eq [int]$r.LocalPort)
                } catch { $false }
            })
            if ($hit.Count -gt 0) {
                $tp = [int]$hit[0].OwningProcess
                if (-not $peerPidInfo.ContainsKey($tp)) {
                    $peerPidInfo[$tp] = Resolve-ProcFromMap -Map $script:PidMap -ProcId $tp
                }
                $matched++
            }
        }
        if ($matched -gt 0) {
            $peerMethod = ('独立反查成功：Get-NetTCPConnection -LocalPort 13688 拿四元组 → 用 -RemotePort 13688 反查同一条连接 → 取对端 OwningProcess（匹配 {0}/{1} 条）' -f $matched, $estab.Count)
        } else {
            $peerMethod = '独立反查未命中（对端可能已断开，或反查窗口内连接已消失）⇒ 对端 PID 无法判定'
        }
    }
} catch {
    $peerMethod = ('独立反查失败：' + $_.Exception.Message + ' ⇒ 对端 PID 无法判定')
}

$s02 = New-Object System.Text.StringBuilder
[void]$s02.AppendLine('02 端口与连接（TCP 13688，只读）')
[void]$s02.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== Get-NetTCPConnection -LocalPort 13688 ==')
[void]$s02.AppendLine('  ⚠ 注意：下面 OwningProcess 是【本地端】PID（13688 这一侧，正常应是 GCUBridge）；')
[void]$s02.AppendLine('     要判断"客户端侧是谁"，请看 netstat 那节的【对端 PID】。')
[void]$s02.AppendLine('     另：非管理员下，别的账户/系统进程的"路径"会读不到（显示"读取失败或需管理员"属预期）。')
if ($tcpRows.Count -eq 0) {
    [void]$s02.AppendLine('  （无任何结果：13688 可能无人监听，或该项读取失败）')
} else {
    foreach ($r in $tcpRows) {
        $pi = Resolve-PidInfo -ProcId ([int]$r.OwningProcess)
        [void]$s02.AppendLine(("  Local={0}:{1}  Remote={2}:{3}  State={4}  OwningPID={5} ({6})" -f `
            $r.LocalAddress, $r.LocalPort, $r.RemoteAddress, $r.RemotePort, $r.State, $r.OwningProcess, $pi.Name))
        [void]$s02.AppendLine(("    持有者路径 : " + $pi.Path))
    }
}
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== netstat -ano | findstr 13688 ==')
[void]$s02.AppendLine($netstatOut)
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== 结论 ==')
if ($listen.Count -gt 0) {
    $lname = @()
    foreach ($r in $listen) { $lname += (Resolve-PidInfo -ProcId ([int]$r.OwningProcess)).Name + '(' + $r.OwningProcess + ')' }
    [void]$s02.AppendLine(("  LISTEN 持有者：{0}" -f (($lname | Select-Object -Unique) -join ', ')))
    $P.ListenerName = (($lname | Select-Object -Unique) -join ', ')
} else {
    [void]$s02.AppendLine('  LISTEN 持有者：无（13688 没有 LISTEN）')
    $P.ListenerName = '无'
}
[void]$s02.AppendLine(("  ESTABLISHED 条数：{0}" -f $estab.Count))
$P.EstabCount = $estab.Count
if ($estab.Count -gt 0) {
    $peers = @()
    foreach ($r in $estab) {
        $pi = Resolve-PidInfo -ProcId ([int]$r.OwningProcess)
        $peers += ("{0}:{1} -> {2}  PID={3} 名={4}" -f $r.LocalAddress, $r.LocalPort, $r.RemoteAddress, $r.OwningProcess, $pi.Name)
    }
    foreach ($x in ($peers | Select-Object -Unique)) { [void]$s02.AppendLine(("    " + $x)) }
    $P.PeerNames = (($estab | ForEach-Object { (Resolve-PidInfo -ProcId ([int]$_.OwningProcess)).Name }) | Select-Object -Unique) -join ', '
    [void]$s02.AppendLine(("  本地端（13688 这一侧）进程：{0}" -f $P.PeerNames))
    [void]$s02.AppendLine('  （"客户端侧是谁"见下面 netstat 对端 PID 那节；这里是 13688 的持有者侧）')
} else {
    [void]$s02.AppendLine('  对端进程名：无（13688 上没有任何 ESTABLISHED 会话）')
    $P.PeerNames = '无'
}
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== ★ 对端（客户端侧）PID 是谁 —— 独立反查，不猜 ==')
[void]$s02.AppendLine('  ⚠ 更正：netstat -ano 每行末列 PID 是【本地端】持有者（不是对端！）。')
[void]$s02.AppendLine('     本节的"对端 PID"来自独立反查，方法与结果如下：')
[void]$s02.AppendLine(('  反查方法：' + $peerMethod))
[void]$s02.AppendLine(('  反查得到 Get-NetTCPConnection -RemotePort 13688 的行数：{0}' -f $revRows.Count))
if ($peerPidInfo.Count -eq 0) {
    [void]$s02.AppendLine('  ⇒ 对端 PID：无法判定（见上面的反查方法说明；绝不用"PID 恰好等于端口号"来猜）')
    $P.PeerPidList = '无法判定'
} else {
    $pl = @()
    foreach ($k in ($peerPidInfo.Keys | Sort-Object)) {
        $e = $peerPidInfo[$k]
        $pn2 = $e.Path
        if (-not $pn2) { $pn2 = '(读取失败或需管理员)' }
        $isLm = '否'
        if ($e.Name -match '^(L-Mechrevo|GCUService|OSDTpDetect|ControlCenter|GCUBridge|displayUtil|sysConUtil)') { $isLm = '是' }
        [void]$s02.AppendLine(("  PID {0,-7} 名={1,-18} 是否 LM/GCU 系（L-Mechrevo/GCUService/OSDTpDetect/ControlCenter3）：{2}" -f $k, $e.Name, $isLm))
        [void]$s02.AppendLine(("      路径 : " + $pn2))
        $pl += ($e.Name + '(' + $k + ')')
    }
    $P.PeerPidList = ($pl -join ', ')
}
Write-Utf8 -Path (Join-Path $OutDir '02-端口与连接.txt') -Text $s02.ToString()

# ================================================================ 03 注册表
Step '03 注册表（只读）' | Out-Null

function Get-RegValues {
    # 健壮读注册表：返回 @{ Ok=$bool; Err=''; Values=@( @{Name;Type;Value} ) }
    # 用 OpenSubKey + GetValueNames 枚举，保留原始类型，避免 - 前缀属性名丢失
    param([string]$Path, [string]$SubKey)
    $res = @{ Ok = $false; Err = ''; Values = @() }
    try {
        $base = [Microsoft.Win32.Registry]::LocalMachine
        if ($Path -eq 'HKCU') { $base = [Microsoft.Win32.Registry]::CurrentUser }
        $k = $base.OpenSubKey($SubKey)
        if ($null -eq $k) {
            $res.Err = '键不存在'
            return $res
        }
        $names = $k.GetValueNames()
        $vals = New-Object System.Collections.ArrayList
        foreach ($n in $names) {
            $disp = $n
            if ($disp -eq '') { $disp = '(默认值)' }
            $v = ''
            $ty = ''
            try {
                $ty = [string]$k.GetValueKind($n)
                $raw = $k.GetValue($n)
                if ($null -eq $raw) { $v = '(null)' }
                elseif ($raw -is [array]) { $v = ($raw -join ', ') }
                else { $v = [string]$raw }
            } catch { $v = ('(读取失败：{0})' -f $_.Exception.Message) }
            [void]$vals.Add(@{ Name = $disp; Type = $ty; Value = $v })
        }
        $k.Close()
        $res.Ok = $true
        $res.Values = $vals.ToArray()
    } catch {
        $res.Err = $_.Exception.Message
    }
    return $res
}

$regLm  = Get-RegValues -Path 'HKLM' -SubKey 'SOFTWARE\L-Mechrevo'
$regBr  = Get-RegValues -Path 'HKLM' -SubKey 'SYSTEM\CurrentControlSet\Services\GCUBridge'
$regSv  = Get-RegValues -Path 'HKLM' -SubKey 'SYSTEM\CurrentControlSet\Services\GCUService'
$regIts = Get-RegValues -Path 'HKLM' -SubKey 'SOFTWARE\OEM\GamingCenter2\ItemSupport'

function Get-RegValueByName {
    param($Res, [string]$Name)
    if (-not $Res.Ok) { return $null }
    foreach ($v in $Res.Values) {
        if ($v.Name -eq $Name) { return $v.Value }
    }
    return $null
}

$readyKeys = @('ServiceReady', 'ServiceLoadingReady', 'PerformanceReady')
$readyVals = @{}
foreach ($k in $readyKeys) { $readyVals[$k] = Get-RegValueByName -Res $regIts -Name $k }
$P.ReadyVals = $readyVals
$readyLineParts = @()
foreach ($k in $readyKeys) {
    if ($null -eq $readyVals[$k]) { $readyLineParts += ($k + '=不存在') }
    else { $readyLineParts += ($k + '=' + $readyVals[$k]) }
}
$P.ReadyLine = ($readyLineParts -join '  ')

$s03 = New-Object System.Text.StringBuilder
[void]$s03.AppendLine('03 注册表（HKEY_LOCAL_MACHINE，只读；未做任何写入）')
[void]$s03.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s03.AppendLine('')
$regSections = @(
    @{ T = 'HKLM\SOFTWARE\L-Mechrevo'; R = $regLm },
    @{ T = 'HKLM\SYSTEM\CurrentControlSet\Services\GCUBridge'; R = $regBr },
    @{ T = 'HKLM\SYSTEM\CurrentControlSet\Services\GCUService'; R = $regSv },
    @{ T = 'HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport'; R = $regIts }
)
foreach ($sec in $regSections) {
    [void]$s03.AppendLine(('== ' + $sec.T + ' =='))
    if (-not $sec.R.Ok) {
        [void]$s03.AppendLine(('  该项失败（' + $sec.R.Err + '）'))
        [void]$s03.AppendLine('')
        continue
    }
    if ($sec.R.Values.Count -eq 0) {
        [void]$s03.AppendLine('  （键存在，但没有任何值）')
    } else {
        foreach ($v in ($sec.R.Values | Sort-Object { $_.Name })) {
            [void]$s03.AppendLine(("  {0}  [{1}] = {2}" -f $v.Name, $v.Type, $v.Value))
        }
    }
    [void]$s03.AppendLine('')
}
[void]$s03.AppendLine('== ★ 三个就绪键（关键线索，单独一行结论）==')
[void]$s03.AppendLine('  ' + $P.ReadyLine)
[void]$s03.AppendLine('  说明：可用机（本机翼龙15Pro，GCU 正常）三者全 =1；')
[void]$s03.AppendLine('        故障机（极光Pro）已知 ServiceReady=1，但 ServiceLoadingReady / PerformanceReady 整个值不存在。')
[void]$s03.AppendLine('        值不存在 ⇒ 疑似 StartMainWork 没跑完。')
[void]$s03.AppendLine('')
[void]$s03.AppendLine('== ★ 版本基线口径（避免误判）==')
$gcuInstVer = Get-RegValueByName -Res $regLm -Name 'GcuInstallerVersion'
[void]$s03.AppendLine(('  本机 HKLM\SOFTWARE\L-Mechrevo\GcuInstallerVersion = ' + $(if ($gcuInstVer) { $gcuInstVer } else { '(无该值)' })))
[void]$s03.AppendLine('  说明：这个"安装器版本号"与"40A 载荷内容"是两件事。')
[void]$s03.AppendLine('        * 载荷是否正确落地，一律以 04 的"40A 落地核对表"（内嵌的大小/SHA256 期望值）为准；')
[void]$s03.AppendLine('        * GcuInstallerVersion 记 18.3 或 18.6 都【不算异常】——不同分发批次的安装器版本号不同，')
[void]$s03.AppendLine('          只要 13 件载荷逐项 OK，就说明 40A 已正确落地。')
[void]$s03.AppendLine('        * 本脚本不再把 18.6 当作硬性期望；请勿仅凭版本号判故障。')
Write-Utf8 -Path (Join-Path $OutDir '03-注册表.txt') -Text $s03.ToString()

# ================================================================ 04 GCU 文件
Step '04 GCU 文件与关键 13 件签名' | Out-Null

# ---- 实测探测底座根（两种目录布局，不许猜父目录）----
$probe = @()
function Add-Probe {
    param([string]$How, [string]$Path, [bool]$Hit, [string]$Err = '')
    $script:probe += [pscustomobject]@{ 方式 = $How; 路径 = $Path; 命中 = $Hit; 备注 = $Err }
}

# 候选 1：注册表登记的服务可执行文件所在目录
$cand = @()
try {
    $svcExe = Get-RegValueByName -Res $regLm -Name 'GcuServiceExe'
    if ($svcExe) {
        $d = Split-Path -Parent ([string]$svcExe)
        if ($d) { $cand += $d }
        Add-Probe -How '注册表 HKLM\SOFTWARE\L-Mechrevo\GcuServiceExe' -Path ([string]$svcExe) -Hit (Test-Path -LiteralPath ([string]$svcExe))
    }
} catch { Add-Probe -How '注册表 GcuServiceExe' -Path '' -Hit $false -Err $_.Exception.Message }
# 候选 2/3：两种已知布局
$cand += (Join-Path $env:ProgramFiles 'L-Mechrevo\GCU\AiStoneService')
$cand += (Join-Path $env:ProgramFiles 'L-Mechrevo\GCU')
foreach ($c in ($cand | Select-Object -Unique)) {
    if ($c) { Add-Probe -How '已知候选布局' -Path (Join-Path $c 'GCUBridge.exe') -Hit (Test-Path -LiteralPath (Join-Path $c 'GCUBridge.exe')) }
}
# 候选 4：实测全树找 GCUBridge.exe
$gcuRoot = Join-Path $env:ProgramFiles 'L-Mechrevo\GCU'
$found = @()
try {
    $found = @(Get-ChildItem -LiteralPath $gcuRoot -Filter 'GCUBridge.exe' -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object { $_.FullName.Length } | Select-Object -First 6)
    foreach ($f in $found) { Add-Probe -How '全树搜索（不猜父目录）' -Path $f.FullName -Hit $true }
} catch { Add-Probe -How '全树搜索' -Path $gcuRoot -Hit $false -Err $_.Exception.Message }

$baseRoot = ''
foreach ($f in $found) {
    if ($f.DirectoryName) { $baseRoot = $f.DirectoryName; break }
}
# 若全树没找到，退回候选里真正命中的那个
if (-not $baseRoot) {
    foreach ($c in ($cand | Select-Object -Unique)) {
        if ($c -and (Test-Path -LiteralPath (Join-Path $c 'GCUBridge.exe'))) { $baseRoot = $c; break }
    }
}
# 仍没有：用布局 A（AiStoneService）作为"期望路径"写进报告，不假装存在
$baseRootGuess = $baseRoot
if (-not $baseRootGuess) { $baseRootGuess = Join-Path $env:ProgramFiles 'L-Mechrevo\GCU\AiStoneService' }
$P.BaseRoot = $baseRootGuess
$P.BaseRootFound = [bool]$baseRoot

# MyControlCenter 位置（可能有两种）
$mcc = ''
$mccHow = ''
$tryMcc = @()
if ($baseRootGuess) { $tryMcc += (Join-Path $baseRootGuess 'MyControlCenter') }
$tryMcc += (Join-Path $env:ProgramFiles 'L-Mechrevo\GCU\MyControlCenter')
foreach ($t in ($tryMcc | Select-Object -Unique)) {
    if (Test-Path -LiteralPath (Join-Path $t 'GCUService.exe')) { $mcc = $t; $mccHow = '实测探测：该目录下有 GCUService.exe'; break }
}
if (-not $mcc) {
    # 全树找 GCUService.exe 的父目录
    $gs = @()
    try { $gs = @(Get-ChildItem -LiteralPath $gcuRoot -Filter 'GCUService.exe' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 4) } catch { }
    foreach ($f in $gs) { if ($f.DirectoryName) { $mcc = $f.DirectoryName; $mccHow = '全树搜索 GCUService.exe'; break } }
}
if (-not $mcc) { $mcc = (Join-Path $baseRootGuess 'MyControlCenter'); $mccHow = '未实测到，按底座根下的 MyControlCenter 作为期望路径' }
$P.MccRoot = $mcc
$P.MccHow = $mccHow

$s04 = New-Object System.Text.StringBuilder
[void]$s04.AppendLine('04 GCU 文件（只读；不修改、不删除任何文件）')
[void]$s04.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s04.AppendLine('')
[void]$s04.AppendLine('== 底座根探测过程（实测，不猜父目录）==')
foreach ($pr in $script:probe) {
    [void]$s04.AppendLine(("  [{0}] {1}  命中={2}  {3}" -f $pr.方式, $pr.路径, $pr.命中, $pr.备注))
}
[void]$s04.AppendLine(("  ⇒ 底座根采用：{0}" -f $baseRootGuess))
[void]$s04.AppendLine(("  ⇒ MyControlCenter 采用：{0}（依据：{1}）" -f $mcc, $mccHow))
[void]$s04.AppendLine('')
[void]$s04.AppendLine(('== 递归清单：' + $gcuRoot + ' （相对路径 / 大小 / mtime）=='))
$relRows = @()
try {
    $relRows = @(Get-ChildItem -LiteralPath $gcuRoot -Recurse -File -Force -ErrorAction SilentlyContinue)
} catch {
    [void]$s04.AppendLine(('  该项失败（' + $_.Exception.Message + '）'))
}
$P.GcuFileCount = $relRows.Count
[void]$s04.AppendLine(("  文件数：{0}" -f $relRows.Count))
foreach ($f in ($relRows | Sort-Object FullName)) {
    $rel = $f.FullName
    if ($rel.StartsWith($gcuRoot)) { $rel = $rel.Substring($gcuRoot.Length).TrimStart('\') }
    [void]$s04.AppendLine(("  {0,12}  {1}  {2}" -f $f.Length, $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $rel))
}
[void]$s04.AppendLine('')
[void]$s04.AppendLine('== ★ 关键 13 件：实测 大小/SHA256前16/FileVersion  vs  内嵌期望值 ==')
[void]$s04.AppendLine('  （期望值已【内嵌在脚本里】，不读仓库、不读 payload\40、不依赖任何盘符）')
$key13 = @(
    @{ L = 'GCUBridge.exe（底座）'; Rel = 'GCUBridge.exe'; P = (Join-Path $baseRootGuess 'GCUBridge.exe') },
    @{ L = 'UEFI_Firmware.dll（底座根那份）'; Rel = 'UEFI_Firmware.dll'; P = (Join-Path $baseRootGuess 'UEFI_Firmware.dll') },
    @{ L = 'MyControlCenter\GCUService.exe'; Rel = 'MyControlCenter\GCUService.exe'; P = (Join-Path $mcc 'GCUService.exe') },
    @{ L = 'MyControlCenter\UEFI_Firmware.dll'; Rel = 'MyControlCenter\UEFI_Firmware.dll'; P = (Join-Path $mcc 'UEFI_Firmware.dll') },
    @{ L = 'MyControlCenter\NVControlSetting.dll'; Rel = 'MyControlCenter\NVControlSetting.dll'; P = (Join-Path $mcc 'NVControlSetting.dll') },
    @{ L = 'MyControlCenter\GPUInfoDLL.dll'; Rel = 'MyControlCenter\GPUInfoDLL.dll'; P = (Join-Path $mcc 'GPUInfoDLL.dll') },
    @{ L = 'MyControlCenter\ACPIDriverDll.dll'; Rel = 'MyControlCenter\ACPIDriverDll.dll'; P = (Join-Path $mcc 'ACPIDriverDll.dll') },
    @{ L = 'MyControlCenter\OSDTpDetect.exe'; Rel = 'MyControlCenter\OSDTpDetect.exe'; P = (Join-Path $mcc 'OSDTpDetect.exe') },
    @{ L = 'MyControlCenter\RegistryUtils.dll'; Rel = 'MyControlCenter\RegistryUtils.dll'; P = (Join-Path $mcc 'RegistryUtils.dll') },
    @{ L = 'MyControlCenter\UWAirplane.dll'; Rel = 'MyControlCenter\UWAirplane.dll'; P = (Join-Path $mcc 'UWAirplane.dll') },
    @{ L = 'MyControlCenter\UWDiskInfo.dll'; Rel = 'MyControlCenter\UWDiskInfo.dll'; P = (Join-Path $mcc 'UWDiskInfo.dll') },
    @{ L = 'MyControlCenter\DisplayInfo.dll'; Rel = 'MyControlCenter\DisplayInfo.dll'; P = (Join-Path $mcc 'DisplayInfo.dll') },
    @{ L = 'MyControlCenter\AppSettings\GamingMonitor.json'; Rel = 'MyControlCenter\AppSettings\GamingMonitor.json'; P = (Join-Path $mcc 'AppSettings\GamingMonitor.json') }
)
$rows40A = New-Object System.Collections.ArrayList
$bridgeSize = $null
$gmSize = $null
$bridgeSha16 = ''
$gmSha16 = ''
foreach ($k in $key13) {
    # ⚠ 每一项都独立 try/catch；任何一项炸了也绝不影响后面出"40A 落地核对表 + 小计"
    try {
        $blk = Invoke-Safe -Label ('签名 ' + $k.L) -Action { Get-SigBlock -Label $k.L -Path $k.P } -Fallback ("  " + $k.L + "`r`n    状态       : 该项失败")
        [void]$s04.AppendLine($blk)
    } catch {
        [void]$s04.AppendLine(("  " + $k.L + "`r`n    状态       : 该项异常（" + $_.Exception.Message + "）"))
    }
    $exp = Get-ExpectedRow -Rel $k.Rel
    $aSize = $null
    $aSha = ''
    try {
        if (Test-Path -LiteralPath $k.P) {
            $aSize = [int](Get-Item -LiteralPath $k.P -ErrorAction Stop).Length
            $hs = Get-Sha16 -Path $k.P
            if ($hs -notlike '(*') { $aSha = ([string]$hs).ToUpper() }
        }
    } catch { }
    $verdict = ''
    if ($null -eq $aSize) { $verdict = '缺失' }
    elseif ($null -eq $exp) { $verdict = '(无内嵌期望值，未判)' }
    elseif ($aSize -eq $exp.Size -and $aSha -eq $exp.Sha16) { $verdict = 'OK（与 40A 期望一致）' }
    else { $verdict = '不一致（与 40A 期望不同）' }
    [void]$rows40A.Add([pscustomobject]@{
        Rel = $k.Rel; ExpSize = $(if ($exp) { $exp.Size } else { $null }); ExpSha = $(if ($exp) { $exp.Sha16 } else { '' })
        ExpVer = $(if ($exp) { $exp.Ver } else { '' }); ASize = $aSize; ASha = $aSha; Verdict = $verdict
    })
    if ($k.Rel -eq 'GCUBridge.exe') { $bridgeSize = $aSize; $bridgeSha16 = $aSha }
    if ($k.Rel -eq 'MyControlCenter\AppSettings\GamingMonitor.json') { $gmSize = $aSize; $gmSha16 = $aSha }
}
[void]$s04.AppendLine('')
[void]$s04.AppendLine('== ★ 40A 落地核对表（期望值内嵌；判定 = 大小与 SHA256 前16 都一致才算 OK）==')
[void]$s04.AppendLine('相对路径' + "`t" + '期望字节' + "`t" + '期望SHA16' + "`t" + '期望版本' + "`t" + '实测字节' + "`t" + '实测SHA16' + "`t" + '判定')
foreach ($r in $rows40A) {
    $fmtLine = "{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}"
    [void]$s04.AppendLine(($fmtLine -f $r.Rel, `
        $(if ($null -ne $r.ExpSize) { $r.ExpSize } else { '-' }), `
        $(if ($r.ExpSha) { $r.ExpSha } else { '-' }), `
        $(if ($r.ExpVer) { $r.ExpVer } else { '-' }), `
        $(if ($null -ne $r.ASize) { $r.ASize } else { '-' }), `
        $(if ($r.ASha) { $r.ASha } else { '-' }), `
        $r.Verdict))
}
$okCount = @($rows40A | Where-Object { $_.Verdict -like 'OK*' }).Count
$misCount = @($rows40A | Where-Object { $_.Verdict -like '不一致*' }).Count
$missCount = @($rows40A | Where-Object { $_.Verdict -eq '缺失' }).Count
[void]$s04.AppendLine(('小计：OK={0}  不一致={1}  缺失={2}  共 {3} 件' -f $okCount, $misCount, $missCount, $rows40A.Count))
[void]$s04.AppendLine('')
[void]$s04.AppendLine('== ★ "40A 是否落地" 结论 ==')
$partB = ''
$partG = ''
if ($null -eq $bridgeSize) { $partB = 'GCUBridge.exe 不存在 ⇒ 无法判定' }
elseif ($bridgeSize -eq $ExpectedBridgeSize -and $bridgeSha16 -eq $ExpectedBridgeSha16) { $partB = ('GCUBridge.exe = {0} 字节 / SHA16 {1} = 40A 预期值 ⇒ 命中' -f $bridgeSize, $bridgeSha16) }
elseif ($bridgeSize -eq $ExpectedBridgeSize) { $partB = ('GCUBridge.exe = {0} 字节 = 40A 预期大小，但 SHA16={1} ≠ 预期 {2} ⇒ 大小对、内容不同' -f $bridgeSize, $bridgeSha16, $ExpectedBridgeSha16) }
else { $partB = ('GCUBridge.exe = {0} 字节 ≠ 40A 预期 {1} 字节 ⇒ 不命中' -f $bridgeSize, $ExpectedBridgeSize) }
if ($null -eq $gmSize) { $partG = 'GamingMonitor.json 不存在 ⇒ 无法判定' }
elseif ($gmSize -eq $ExpectedGmSize -and $gmSha16 -eq $ExpectedGmSha16) { $partG = ('GamingMonitor.json = {0} 字节 / SHA16 {1} = 40A 预期值 ⇒ 命中' -f $gmSize, $gmSha16) }
elseif ($gmSize -eq $ExpectedGmSize) { $partG = ('GamingMonitor.json = {0} 字节 = 40A 预期大小，但 SHA16={1} ≠ 预期 {2} ⇒ 大小对、内容不同' -f $gmSize, $gmSha16, $ExpectedGmSha16) }
else { $partG = ('GamingMonitor.json = {0} 字节 ≠ 40A 预期 {1} 字节 ⇒ 不命中' -f $gmSize, $ExpectedGmSize) }
[void]$s04.AppendLine('  ' + $partB)
[void]$s04.AppendLine('  ' + $partG)
if ($null -eq $bridgeSize -or $null -eq $gmSize) {
    $P.Landed40A = '无法判定'
} elseif ($bridgeSize -eq $ExpectedBridgeSize -and $gmSize -eq $ExpectedGmSize) {
    $P.Landed40A = '是'
} else {
    $P.Landed40A = '否'
}
[void]$s04.AppendLine(('  ⇒ 40A 是否落地：{0}（13 件里 OK={1} / 不一致={2} / 缺失={3}）' -f $P.Landed40A, $okCount, $misCount, $missCount))
$P.BridgeSize = $bridgeSize
$P.GmSize = $gmSize
$P.Ok40A = $okCount
$P.Mis40A = $misCount
$P.Miss40A = $missCount
Write-Utf8 -Path (Join-Path $OutDir '04-GCU文件.txt') -Text $s04.ToString()

# ================================================================ 05 装后自检
Step '05 装后自检（gcu-install-status.json）' | Out-Null
$statusPath = Join-Path $env:ProgramData 'L-Mechrevo\gcu-install-status.json'
$s05 = New-Object System.Text.StringBuilder
[void]$s05.AppendLine('05 装后自检（只读）')
[void]$s05.AppendLine(('期望路径：' + $statusPath))
[void]$s05.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s05.AppendLine('')
if (-not (Test-Path -LiteralPath $statusPath)) {
    [void]$s05.AppendLine('  不存在')
    [void]$s05.AppendLine(('  （期望路径就是上面的：' + $statusPath + '）'))
    [void]$s05.AppendLine('  说明：该文件是"装后自检"产物，只有较新的安装器（已接入装后自检的那版）才会写。')
    [void]$s05.AppendLine('        本文件不存在 ⇒ 可能只是【旧版安装器 / 未接入装后自检】，不一定是安装失败；')
    [void]$s05.AppendLine('        请结合 04 的"40A 落地核对表"与 06 的安装日志一起判断，别单看这一条。')
    $P.InstallStatus = '文件不存在（可能为旧版安装器/未接入装后自检，非必然故障）'
} else {
    $P.InstallStatus = '存在'
    try {
        $fi = Get-Item -LiteralPath $statusPath -ErrorAction Stop
        [void]$s05.AppendLine(("  文件大小：{0} 字节  修改时间：{1}" -f $fi.Length, $fi.LastWriteTime))
        $raw = Get-Content -LiteralPath $statusPath -Raw -ErrorAction Stop
        [void]$s05.AppendLine('  --- 原始内容 ---')
        [void]$s05.AppendLine((Get-Text -Text $raw -MaxLines 400))
        [void]$s05.AppendLine('  --- 关键字段 ---')
        try {
            $j = $raw | ConvertFrom-Json -ErrorAction Stop
            foreach ($f in @('Status', 'Reason', 'UpdatedUtc')) {
                $v = ''
                try { $v = [string]$j.$f } catch { $v = '' }
                if (-not $v) { $v = '(无该字段)' }
                [void]$s05.AppendLine(("    {0} = {1}" -f $f, $v))
                if ($f -eq 'Status') { $P.InstallStatusValue = $v }
            }
            $chk = $null
            try { $chk = $j.Checks } catch { $chk = $null }
            if ($null -ne $chk) {
                [void]$s05.AppendLine('    Checks：')
                $names = @()
                try { $names = @($chk.PSObject.Properties.Name) } catch { }
                if ($names.Count -eq 0) { [void]$s05.AppendLine('      (无)') }
                foreach ($n in $names) {
                    $cv = ''
                    try { $cv = [string]$chk.$n } catch { $cv = '' }
                    [void]$s05.AppendLine(("      {0} = {1}" -f $n, $cv))
                }
            } else {
                [void]$s05.AppendLine('    Checks：(无该字段)')
            }
        } catch {
            [void]$s05.AppendLine(('    解析 JSON 失败（{0}）' -f $_.Exception.Message))
        }
    } catch {
        [void]$s05.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
    }
}
Write-Utf8 -Path (Join-Path $OutDir '05-装后自检.txt') -Text $s05.ToString()

# ================================================================ 06 底座日志
Step '06 GCU 底座日志' | Out-Null

function Get-LogDigest {
    # 取一个日志文件：路径/大小/mtime/取了多少行/是否截断 + 内容
    param([string]$Path, [int]$MaxLines = 300, [int]$MaxBytes = 65536)
    $hdr = New-Object System.Text.StringBuilder
    [void]$hdr.AppendLine(('---- ' + $Path + ' ----'))
    if (-not (Test-Path -LiteralPath $Path)) {
        [void]$hdr.AppendLine('  不存在')
        return $hdr.ToString()
    }
    try {
        $fi = Get-Item -LiteralPath $Path -ErrorAction Stop
        [void]$hdr.AppendLine(("  大小：{0} 字节   mtime：{1}" -f $fi.Length, $fi.LastWriteTime))
        $lines = @(Get-TailLines -Path $Path -MaxLines $MaxLines)
        $truncLine = ($fi.Length -gt $MaxBytes -or $lines.Count -ge $MaxLines)
        [void]$hdr.AppendLine(("  取行数：{0}（上限 {1} 行 / {2} 字节）  是否截断：{3}" -f $lines.Count, $MaxLines, $MaxBytes, $(if ($truncLine) { '是（取的是尾部）' } else { '否（已取全文）' })))
        $body = ($lines -join "`r`n")
        if ($body.Length -gt $MaxBytes) {
            $body = $body.Substring($body.Length - $MaxBytes)
            [void]$hdr.AppendLine('  （内容按字节截断到 64KB，保留尾部）')
        }
        [void]$hdr.AppendLine($body)
        return $hdr.ToString()
    } catch {
        [void]$hdr.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
        return $hdr.ToString()
    }
}

$logTargets = @()
try {
    $logTargets += @(Get-ChildItem -LiteralPath (Join-Path $env:ProgramData 'ControlCenter') -Filter '*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 2)
} catch { Warn ('枚举 ControlCenter 日志失败：' + $_.Exception.Message) }
try {
    $logTargets += @(Get-ChildItem -LiteralPath (Join-Path $env:ProgramData 'L-Mechrevo\logs') -Filter 'gcu-install-*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 2)
} catch { Warn ('枚举 gcu-install 日志失败：' + $_.Exception.Message) }
$mycc = 'C:\MyControlCenter.log'
if (Test-Path -LiteralPath $mycc) {
    try { $logTargets += (Get-Item -LiteralPath $mycc -ErrorAction Stop) } catch { }
}

$s06 = New-Object System.Text.StringBuilder
[void]$s06.AppendLine('06 GCU 底座日志（只读；每个文件 ≤300 行且 ≤64KB）')
[void]$s06.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s06.AppendLine(('目标：C:\ProgramData\ControlCenter\*.log（最近 2 个）、%ProgramData%\L-Mechrevo\logs\gcu-install-*.log（最近 2 个）、C:\MyControlCenter.log（若存在）'))
[void]$s06.AppendLine('')
if ($logTargets.Count -eq 0) {
    [void]$s06.AppendLine('  本次一个都没找到（三个位置都不存在或枚举失败）。')
}
$allKeyLines = New-Object System.Collections.ArrayList
$keyPattern = 'StartMainWork|ServiceReady|ServiceLoadingReady|PerformanceReady|Error|Exception|Fail|失败|超时|timeout|Subscribe|SUBACK|Supervise|ApplicationNames'
foreach ($lt in $logTargets) {
    $dig = Invoke-Safe -Label ('日志 ' + $lt.FullName) -Action { Get-LogDigest -Path $lt.FullName -MaxLines 300 -MaxBytes 65536 } -Fallback (('---- {0} ----' -f $lt.FullName) + "`r`n  该项失败")
    [void]$s06.AppendLine($dig)
    [void]$s06.AppendLine('')
    try {
        $ln = @(Get-TailLines -Path $lt.FullName -MaxLines 4000)
        $sel = Select-Lines -Lines $ln -Pattern $keyPattern -Max 200
        if ($sel.Total -gt 0) {
            [void]$allKeyLines.Add(('### ' + $lt.FullName + (' （命中 {0} 行）' -f $sel.Total)))
            foreach ($x in $sel.Lines) { [void]$allKeyLines.Add($x) }
        }
    } catch { Warn ('抽取关键行失败 ' + $lt.FullName + '：' + $_.Exception.Message) }
}
Write-Utf8 -Path (Join-Path $OutDir '06-GCU底座日志.txt') -Text $s06.ToString()

$s06b = New-Object System.Text.StringBuilder
[void]$s06b.AppendLine('06b 底座日志-关键行（最多 200 行）')
[void]$s06b.AppendLine(('关键词：' + $keyPattern))
[void]$s06b.AppendLine('')
if ($allKeyLines.Count -eq 0) {
    [void]$s06b.AppendLine('  （没有任何命中行；也可能是上面每个日志都读取失败/不存在，见 06）')
} else {
    foreach ($x in $allKeyLines) { [void]$s06b.AppendLine($x) }
}
Write-Utf8 -Path (Join-Path $OutDir '06b-底座日志-关键行.txt') -Text $s06b.ToString()
$P.KeyLines06 = $allKeyLines.Count

# ================================================================ 07 LM 日志
Step '07 LM 日志（MechrevoLite）' | Out-Null
$lmLog = Join-Path $env:APPDATA 'MechrevoLite\log.txt'
$s07 = New-Object System.Text.StringBuilder
[void]$s07.AppendLine('07 LM 日志（只读；尾部 ≤500 行）')
[void]$s07.AppendLine(('路径：' + $lmLog))
[void]$s07.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s07.AppendLine('')
$lmReadOk = $false
if (-not (Test-Path -LiteralPath $lmLog)) {
    [void]$s07.AppendLine('  不存在（LM 可能没跑过，或路径不同）')
    $P.LmLog = '不存在'
} else {
    $P.LmLog = '存在'
    try {
        $fi = Get-Item -LiteralPath $lmLog -ErrorAction Stop
        [void]$s07.AppendLine(("  大小：{0} 字节   mtime：{1}" -f $fi.Length, $fi.LastWriteTime))
        $tail = @(Get-TailLines -Path $lmLog -MaxLines 500)
        [void]$s07.AppendLine(("  取行数：{0}（上限 500 行，取尾部；共享读，容忍 LM 正在写）" -f $tail.Count))
        [void]$s07.AppendLine('')
        foreach ($l in $tail) { [void]$s07.AppendLine($l) }
        $lmReadOk = $true
    } catch {
        [void]$s07.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
        $P.LmLog = ('存在但读取失败：' + $_.Exception.Message)
    }
}
Write-Utf8 -Path (Join-Path $OutDir '07-LM日志.txt') -Text $s07.ToString()

$s07b = New-Object System.Text.StringBuilder
[void]$s07b.AppendLine('07b LM 日志-MQTT 关键行（最多 300 行）')
$lmKeyPattern = 'CONNACK|订阅|SUBACK|MQTT 断开|ReasonString|MqttCommunicationTimedOut|clientId|fallback|connected|GCU 已连接'
[void]$s07b.AppendLine(('关键词：' + $lmKeyPattern))
[void]$s07b.AppendLine('')
$lmHit = 0
if ($lmReadOk) {
    try {
        $tail2 = @(Get-TailLines -Path $lmLog -MaxLines 6000)
        # ⭐ 关键：命中超过 300 行时必须保留【最新的 300 行】（尾部），不是最早的 300 行。
        #    真机实测：取头部会丢掉最后约 25 分钟（抓包时刻的最新现场恰恰在那里）。
        $sel2 = Select-Lines -Lines $tail2 -Pattern $lmKeyPattern -Max 300 -Keep 'tail'
        $lmHit = $sel2.Total
        $truncMsg = if ($sel2.Truncated) { ('已按【尾部】截断：保留最新 300 行，丢弃更早的 {0} 行' -f $sel2.Dropped) } else { '未截断（命中行数 ≤ 300，全部保留）' }
        [void]$s07b.AppendLine(('（扫描最近 {0} 行，命中 {1} 行）' -f $tail2.Count, $sel2.Total))
        [void]$s07b.AppendLine(($truncMsg + '；命中行按原顺序，最后一行就是日志里最新的一条'))
        [void]$s07b.AppendLine('')
        if ($sel2.Total -eq 0) { [void]$s07b.AppendLine('  （无命中）') }
        foreach ($x in $sel2.Lines) { [void]$s07b.AppendLine($x) }
    } catch {
        [void]$s07b.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
    }
} else {
    [void]$s07b.AppendLine(('  本次没能读到 LM 日志，无关键行可抽。（07 里记了原因；期望路径：' + $lmLog + '）'))
}
Write-Utf8 -Path (Join-Path $OutDir '07b-LM日志-MQTT关键行.txt') -Text $s07b.ToString()
$P.KeyLines07 = $lmHit

# ================================================================ 08 网络环境
Step '08 网络环境' | Out-Null
$s08 = New-Object System.Text.StringBuilder
[void]$s08.AppendLine('08 网络环境（只读）')
[void]$s08.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s08.AppendLine('')
[void]$s08.AppendLine('== 网络适配器清单 ==')
$adps = @(Invoke-Safe -Label '适配器枚举' -Action {
    @(Get-NetAdapter -ErrorAction Stop | Select-Object Name, Status, LinkSpeed, InterfaceType, MacAddress, InterfaceDescription)
} -Fallback @())
if ($adps.Count -eq 0) { [void]$s08.AppendLine('  （无结果或该项失败）') }
foreach ($a in $adps) {
    [void]$s08.AppendLine(("  {0}  状态={1}  类型={2}  速率={3}" -f $a.Name, $a.Status, $a.InterfaceType, $a.LinkSpeed))
    [void]$s08.AppendLine(("    " + $a.InterfaceDescription))
}
[void]$s08.AppendLine('')
[void]$s08.AppendLine('== 回环 ping ==')
foreach ($t in @('127.0.0.1', '::1')) {
    $r = Invoke-Safe -Label ('ping ' + $t) -Action {
        $o = & ping -n 2 $t 2>&1 | Out-String
        $o
    } -Fallback '(该项失败)'
    [void]$s08.AppendLine(('  --- ping ' + $t + ' ---'))
    [void]$s08.AppendLine((Get-Text -Text ([string]$r) -MaxLines 12))
}
[void]$s08.AppendLine('')
[void]$s08.AppendLine('== 系统代理（netsh winhttp show proxy）==')
$px = Invoke-Safe -Label 'winhttp 代理' -Action { (& netsh winhttp show proxy 2>&1 | Out-String) } -Fallback '(该项失败)'
[void]$s08.AppendLine([string]$px)
[void]$s08.AppendLine('== 安全软件 / 网络过滤驱动线索 ==')
$av = @(Invoke-Safe -Label 'SecurityCenter2 AntiVirusProduct' -Action {
    @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction Stop |
        Select-Object displayName, productState, pathToSignedProductExe)
} -Fallback @())
if ($av.Count -eq 0) {
    [void]$s08.AppendLine('  已安装杀软：读取失败或为空（root\SecurityCenter2 不可用是常见情况，记事即可）')
} else {
    foreach ($a in $av) {
        [void]$s08.AppendLine(("  {0}  productState={1}  {2}" -f $a.displayName, $a.productState, $a.pathToSignedProductExe))
    }
}
[void]$s08.AppendLine('')
[void]$s08.AppendLine('== 文件系统过滤驱动（fltmc filters，需要管理员）==')
$flt = Invoke-Safe -Label 'fltmc filters' -Action { (& fltmc filters 2>&1 | Out-String) } -Fallback '(该项失败)'
[void]$s08.AppendLine([string]$flt)
if (-not $isAdmin) { [void]$s08.AppendLine('  （注意：非管理员下 fltmc 会 Access is denied，属预期）') }
Write-Utf8 -Path (Join-Path $OutDir '08-网络环境.txt') -Text $s08.ToString()

# ================================================================ 09 回环抓包
Step '09 回环抓包（核心新增）' | Out-Null

$LOOPBACK_GUID = '{5CBF81BD-5055-47CD-9055-A76B2B4E3694}'   # 固定 Loopback 接口

function Get-LoopbackIf {
    # 返回 @{ Name; Guid }，尽量实测；失败给固定值
    $res = @{ Name = 'Loopback'; Guid = $LOOPBACK_GUID; How = '使用系统固定 Loopback 接口 GUID（实测未取到）' }
    try {
        $out = (& netsh trace show interfaces 2>&1 | Out-String)
        $blocks = $out -split "(?m)^(?=Ethernet adapter|Wireless LAN adapter|Unknown adapter)"
        foreach ($b in $blocks) {
            if ($b -match '(?im)^\s*Description:\s*(.+)$' -and $b -match 'Loopback') {
                $g = ''
                $n = ''
                try { $m = [regex]::Match($b, '(?im)^\s*Interface GUID:\s*(\{[0-9A-Fa-f\-]+\})'); if ($m.Success) { $g = $m.Groups[1].Value } } catch { }
                try { $m2 = [regex]::Match($b, '(?im)^\s*(?:Ethernet|Wireless LAN|Unknown) adapter\s+(.+?):\s*$'); if ($m2.Success) { $n = $m2.Groups[1].Value.Trim() } } catch { }
                if ($g) { $res.Guid = $g }
                if ($n) { $res.Name = $n }
                $res.How = '实测：netsh trace show interfaces 命中 Loopback'
                return $res
            }
        }
    } catch { }
    return $res
}

function Get-CaptureToolState {
    $st = @{ PktmonRunning = $false; TraceRunning = $false }
    try {
        $o = (& pktmon status 2>&1 | Out-String)
        if ($o -notmatch '拒绝访问|Access is denied' -and $o -match '正在运行|Running|capture') { $st.PktmonRunning = $true }
    } catch { }
    try {
        $o2 = (& netsh trace show status 2>&1 | Out-String)
        if ($o2 -notmatch 'no trace session|没有') { $st.TraceRunning = $true }
    } catch { }
    return $st
}

function Test-RealPacketLine {
    # ⭐ 判定一行 ETL 转储是不是"真正的报文行"。
    # 背景（真机实测被骗过）：pktmon 的普通转储里，报文行本来就以 [组件号] + 时间戳 开头，
    #   例如：[00]140C.5530::2026-10-08 20:20:31.7 [PktMon] 数据包: TCP ...  ← 这是报文，必须算数；
    #   而下面这些"含端口号但不是报文"的行必须排除：
    #   * pktmon 筛选器注册回显：如 "数据包筛选器 1，名称 LM13688"（恰好含端口号 13688）
    #   * 组件/NIC 注册行（[组件] xxx 注册更改，没有时间戳）
    #   * 计数器/摘要行（组件、计数器和筛选器的摘要）
    #   * 纯元数据行（没有 TCP/UDP 与 地址:端口）
    # 判定口径：有 TCP/UDP 字样 + 有 源/目的 地址:端口 + 有 TCP 标志(SYN/PSH/ACK/FIN/RST)或 payload/长度字段。
    param([string]$Line, [int]$Port = 13688)
    if ([string]::IsNullOrWhiteSpace($Line)) { return $false }
    $l = $Line
    # 1) 排除筛选器注册/移除回显 与 组件/计数器摘要（这些都不带时间戳）
    if ($l -match '筛选器' -or $l -match '(?i)\bfilter\b') { return $false }
    if ($l -match '(?i)component|counter|计数器|摘要|Components\s*:') { return $false }
    # 2) 排除"像组件注册行"的：以 [组件] 之类开头且没有日期时间戳
    $hasTs = ($l -match '\d{4}-\d{2}-\d{2}' -or $l -match '\d{2}:\d{2}:\d{2}')
    if ($l -match '^\s*\[[^\]\d]' -and -not $hasTs) { return $false }
    # 3) 必须有 TCP/UDP 字样
    if ($l -notmatch '(?i)\bTCP\b|\bUDP\b') { return $false }
    # 4) 必须与端口相关
    if ($l -notmatch ([string]$Port)) { return $false }
    # 5) 必须有 地址:端口 形态
    if ($l -notmatch '\d+\.\d+\.\d+\.\d+:\d+' -and $l -notmatch '\[[0-9A-Fa-f:]+\]:\d+' -and $l -notmatch '[0-9A-Fa-f]{0,4}:[0-9A-Fa-f:]*:\d{2,5}') { return $false }
    # 6) 必须有 TCP 标志或载荷长度字段
    if ($l -notmatch '(?i)\bSYN\b|\bACK\b|\bPSH\b|\bFIN\b|\bRST\b|Payload|载荷|length\s*[:=]\s*\d+') { return $false }
    return $true
}

function Split-PacketLines {
    # 从 ETL 转储里分出 @{ Valid=真实报文行; PortOnly=只含端口但非报文的行(诊断用) }
    param([string[]]$Lines, [int]$Port = 13688)
    $valid = New-Object System.Collections.ArrayList
    $portOnly = New-Object System.Collections.ArrayList
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        if ($l -notmatch ([string]$Port)) { continue }
        [void]$portOnly.Add($l)
        if (Test-RealPacketLine -Line $l -Port $Port) { [void]$valid.Add($l) }
    }
    return @{ Valid = $valid.ToArray(); PortOnly = $portOnly.ToArray() }
}

function Convert-EtlToText {
    # 尝试多种自带转换器，返回 @{ Ok; Text; How; Err }
    param([string]$Etl, [string]$Txt)
    $r = @{ Ok = $false; Text = ''; How = ''; Err = '' }
    $pm = Get-Cmd 'pktmon'
    if ($pm) {
        try {
            & pktmon etl2txt $Etl --out $Txt 2>&1 | Out-Null
            if ((Test-Path -LiteralPath $Txt) -and ((Get-Item -LiteralPath $Txt).Length -gt 0)) {
                $r.Ok = $true; $r.How = 'pktmon etl2txt'
                return $r
            }
        } catch { $r.Err += ('pktmon etl2txt: ' + $_.Exception.Message + '; ') }
    }
    try {
        & netsh trace convert input=$Etl output=$Txt dump=TXT report=no overwrite=yes 2>&1 | Out-Null
        if ((Test-Path -LiteralPath $Txt) -and ((Get-Item -LiteralPath $Txt).Length -gt 0)) {
            $r.Ok = $true; $r.How = 'netsh trace convert'
            return $r
        }
    } catch { $r.Err += ('netsh trace convert: ' + $_.Exception.Message + '; ') }
    try {
        & tracerpt $Etl -o $Txt -of TEXT -y 2>&1 | Out-Null
        if ((Test-Path -LiteralPath $Txt) -and ((Get-Item -LiteralPath $Txt).Length -gt 0)) {
            $r.Ok = $true; $r.How = 'tracerpt -of TEXT'
            return $r
        }
    } catch { $r.Err += ('tracerpt: ' + $_.Exception.Message + '; ') }
    return $r
}

$capMode    = '未执行'
$capHow     = ''
$capSeconds = 0
$capEtl     = ''
$capFiltered = @()
$capFilteredTotal = 0
$capNote    = ''
$capErr     = ''
$tmpRoot    = ''
try {
    # 抓包中间件（ETL/文本转储）放系统临时目录，跑完必删；临时目录不可用就退回 OutDir 下的子目录
    if ($env:TEMP -and (Test-Path -LiteralPath $env:TEMP)) {
        $tmpRoot = Join-Path $env:TEMP ('LM-GCU抓包-tmp-' + (Get-Date -Format 'HHmmss') + '-' + $PID)
    }
} catch { }
if (-not $tmpRoot) { $tmpRoot = Join-Path $OutDir ('_tmp_' + (Get-Date -Format 'HHmmss')) }

$capVerdicts = [ordered]@{
    CONNECT  = '无法判断'
    CONNACK  = '无法判断'
    SUBSCRIBE = '无法判断'
    SUBACK   = '无法判断'
}

function Analyze-CaptureText {
    # 从已过滤的报文文本行里给结论
    param([string[]]$Lines)
    $v = [ordered]@{ CONNECT = '未看到'; CONNACK = '未看到'; SUBSCRIBE = '未看到'; SUBACK = '未看到' }
    $join = ''
    try { $join = ($Lines -join "`n") } catch { $join = '' }
    if ([string]::IsNullOrEmpty($join)) {
        return [ordered]@{ CONNECT = '无法判断'; CONNACK = '无法判断'; SUBSCRIBE = '无法判断'; SUBACK = '无法判断' }
    }
    $hasSub   = ($join -match '(?i)\bSUBSCRIBE\b|MQTT\s*Subscribe|Subscribe\s*\(|\x82')
    $hasSubA  = ($join -match '(?i)\bSUBACK\b|MQTT\s*SubAck|SubAck|SUBACK')
    $hasConn  = ($join -match '(?i)\bCONNECT\b|\bSYN\b|Flags.*SYN|\bSYN,')
    $hasConnA = ($join -match '(?i)\bCONNACK\b|MQTT\s*ConnAck|ConnAck')
    if ($hasConn) { $v.CONNECT = '看到（TCP 建连/或解码出 CONNECT）' }
    if ($hasConnA) { $v.CONNACK = '看到（解码出 CONNACK）' }
    if ($hasSub) { $v.SUBSCRIBE = '看到' }
    if ($hasSubA) { $v.SUBACK = '看到' }
    # 无法解码出 MQTT 名称时，用 TCP 层证据给出保守结论
    if ($v.CONNECT -eq '未看到' -and ($join -match '(?i)TCP')) { $v.CONNECT = '未看到（报文文本未出现 CONNECT/SYN 字样）' }
    return $v
}

$startPktmon = $false
$startTrace  = $false
$capProbeSec = [Math]::Min(8, [Math]::Max(3, [int]($CaptureSeconds / 4)))
# 各阶段记录（用于报告里写清"有效报文几行/为什么回退"）
$capNetshValid = -1
$capNetshPortOnly = -1
$capPktmonValid = -1
$capPktmonPortOnly = -1
$capFallbackNote = ''
$capValidCount = 0

try {
    if ($SkipCapture) {
        $capMode = '已跳过（-SkipCapture）'
        $capNote = '本次按参数 -SkipCapture 主动跳过 09 回环抓包。'
    } elseif ($SelfTestCapture) {
        # 自测模式：样例报文 → 证明"有效报文=0 时必须回退"这条分支会走到
        #   -SelfTestCapture          ：样例有真实 TCP 报文（含 CONNECT/CONNACK/SUBSCRIBE，故意缺 SUBACK）
        #   -SelfTestCaptureSample zeropkt ：样例只有筛选器回显+组件事件（模拟真机那种"假成功"）
        $capHow = '内建样例（-SelfTestCapture，仅用于验证判定/过滤/回退/结论链路）'
        $capEtl = '(未产生 ETL)'
        $sample = @(
            '数据包筛选器 1，名称 LM13688',                                   # ← 真机里骗过判定的那一行
            '[00]0000.0000::2026-01-01 00:00:00.900000100 [MSNT_SystemTrace] 事件: Header, BufferSize: 8192',
            '[组件] Microsoft-Windows-NDIS 注册更改',
            '[00]0000.0000::2026-01-01 00:00:00.950000100 [PktMon] 事件: 组件、计数器和筛选器的摘要',
            '[00]0000.0000::2026-01-01 00:00:01.000000100 [PktMon] 数据包: TCP 127.0.0.1:51234 -> 127.0.0.1:13688, Flags=SYN',
            '[00]0000.0000::2026-01-01 00:00:01.000200100 [PktMon] 数据包: TCP 127.0.0.1:13688 -> 127.0.0.1:51234, Flags=SYN,ACK',
            '[00]0000.0000::2026-01-01 00:00:01.001000100 [PktMon] 数据包: TCP 127.0.0.1:51234 -> 127.0.0.1:13688, PSH,ACK, Payload=MQTT CONNECT clientId=UWPClient_2',
            '[00]0000.0000::2026-01-01 00:00:01.002000100 [PktMon] 数据包: TCP 127.0.0.1:13688 -> 127.0.0.1:51234, PSH,ACK, Payload=MQTT CONNACK rc=0 (Success)',
            '[00]0000.0000::2026-01-01 00:00:01.003000100 [PktMon] 数据包: TCP 127.0.0.1:51234 -> 127.0.0.1:13688, PSH,ACK, Payload=MQTT SUBSCRIBE topic=...',
            '（样例故意缺 SUBACK：用于验证"看到 SUBSCRIBE 但没看到 SUBACK"的结论）',
            '无关行：UDP 127.0.0.1:53 不应被保留'
        )
        if ($SelfTestCaptureSample -eq 'zeropkt') {
            # 零有效报文样例：全部是筛选器回显/组件/计数器行，且都"含端口号"
            $sample = @(
                '数据包筛选器 1，名称 LM13688',
                '[00]0000.0000::2026-01-01 00:00:00.900000100 [MSNT_SystemTrace] 事件: Header, BufferSize: 8192, ProviderVersion: 26200',
                '[组件] Microsoft-Windows-NDP 注册更改（组件 ID 1，端口筛选 LM13688）',
                '[00]0000.0000::2026-01-01 00:00:00.950000100 [PktMon] 事件: 组件、计数器和筛选器的摘要（筛选器 LM13688）',
                '[00]0000.0000::2026-01-01 00:00:00.980000100 [PktMon] 事件: 数据包计数器（包含筛选条件 13688）'
            )
        }
        $split = Split-PacketLines -Lines $sample -Port 13688
        $capFiltered = $split.PortOnly
        $capFilteredTotal = $capFiltered.Count
        $capValidCount = $split.Valid.Count
        $capPktmonPortOnly = $capFilteredTotal
        $capPktmonValid = $capValidCount
        if ($capValidCount -gt 0) {
            $capMode = '自测样例（有真实报文，未真抓包）'
            $capVerdicts = Analyze-CaptureText -Lines $split.Valid
            $capNote = ('自测样例：含 13688 的行 {0} 条，其中【有效报文 {1} 条】；无效应排除的行 {2} 条（筛选器回显/组件/计数器）。' -f $capFilteredTotal, $capValidCount, ($capFilteredTotal - $capValidCount))
        } else {
            $capMode = '自测样例（零有效报文 → 触发回退分支）'
            $capVerdicts = [ordered]@{ CONNECT = '无法判断'; CONNACK = '无法判断'; SUBSCRIBE = '无法判断'; SUBACK = '无法判断' }
            $capFallbackNote = ('pktmon 阶段：含 13688 的行 {0} 条，其中【有效报文 0 条】⇒ 判定为未抓到，已回退 netsh trace（本自测样例到此为止，不真抓包）。' -f $capFilteredTotal)
            $capNote = $capFallbackNote + ' 说明：筛选器注册回显/组件/计数器行都会被排除，只有带 TCP 标志与地址:端口的行才算有效报文。'
        }
    } elseif (-not $isAdmin) {
        $capMode = '已跳过（非管理员）'
        $capNote = '抓包需要管理员权限：当前不是管理员，已跳过 09。请以管理员身份重跑以获取回环报文。'
        Warn '非管理员：跳过 09 回环抓包。'
    } else {
        Ensure-Dir $tmpRoot
        $capHow = ''
        # ⭐ 回环场景【先走 netsh trace】：pktmon 挂在 NDIS 组件上，127.0.0.1/::1 的回环流量
        #    不经过 NDIS，pktmon 基本抓不到（真机实测 pktmon 阶段有效报文 0 条）。
        #    所以 netsh trace(CaptureInterface=<Loopback GUID>) 才是主力，pktmon 只当兜底。
        $lb = Get-LoopbackIf
        $etl1 = Join-Path $tmpRoot 'netsh_loopback.etl'
        $txt1 = Join-Path $tmpRoot 'netsh_loopback.txt'
        try {
            Write-Host ("  -> 阶段1 netsh trace start capture=yes CaptureInterface={0} tracefile=<tmp> maxSize=256 fileMode=circular（{1} 秒）" -f $lb.Guid, $CaptureSeconds)
            & netsh trace start capture=yes CaptureInterface=$($lb.Guid) tracefile=$etl1 maxSize=256 fileMode=circular overwrite=yes 2>&1 | Out-Null
            $startTrace = $true
            Start-Sleep -Seconds $CaptureSeconds
            & netsh trace stop 2>&1 | Out-Null
            $startTrace = $false
            Start-Sleep -Milliseconds 800
            $capSeconds = $CaptureSeconds
            if ((Test-Path -LiteralPath $etl1) -and ((Get-Item -LiteralPath $etl1).Length -gt 1024)) {
                $cv1 = Convert-EtlToText -Etl $etl1 -Txt $txt1
                if ($cv1.Ok) {
                    $all1 = @(Get-Content -LiteralPath $txt1 -ErrorAction SilentlyContinue)
                    $sp1 = Split-PacketLines -Lines $all1 -Port 13688
                    $capNetshPortOnly = $sp1.PortOnly.Count
                    $capNetshValid = $sp1.Valid.Count
                    if ($capNetshValid -gt 0) {
                        $capMode = 'netsh trace（有效）'
                        $capHow = ('netsh trace start capture=yes CaptureInterface=' + $lb.Guid + ' tracefile=<tmp> maxSize=256 fileMode=circular + netsh trace stop；转换：' + $cv1.How + '；接口取法：' + $lb.How)
                        $capEtl = $etl1
                        $capFiltered = $sp1.PortOnly
                        $capFilteredTotal = $sp1.PortOnly.Count
                        $capValidCount = $capNetshValid
                        $capNote = ('netsh trace 抓了 {0} 秒（接口：{1}）；含 13688 的行 {2} 条，其中【有效报文 {3} 条】。' -f $CaptureSeconds, $lb.Name, $sp1.PortOnly.Count, $capNetshValid)
                    } else {
                        $capNote = ('netsh trace 抓了 {0} 秒：含 13688 的行 {1} 条，但其中【有效报文 0 条】（全是筛选器/组件/计数器行）⇒ 视为未抓到，进入阶段2 pktmon 兜底。' -f $CaptureSeconds, $sp1.PortOnly.Count)
                    }
                } else {
                    $capNote = ('netsh trace 抓到了 ETL 但三种自带转换器都失败（' + $cv1.Err + '）⇒ 进入阶段2 pktmon 兜底。')
                    $capErr = $cv1.Err
                }
            } else {
                $capNote = ('netsh trace 执行了 {0} 秒，但没有产出有效 ETL（Loopback 可能不在 capturetype 支持范围内）⇒ 进入阶段2 pktmon 兜底。' -f $CaptureSeconds)
                $capErr = 'netsh trace ETL 缺失或过小'
            }
        } catch {
            $capErr = $_.Exception.Message
            $capNote = ('netsh trace 失败（' + $capErr + '）⇒ 进入阶段2 pktmon 兜底。')
            Warn ('netsh trace 路径失败：' + $capErr)
        } finally {
            if ($startTrace) { & netsh trace stop 2>&1 | Out-Null; $startTrace = $false }
        }

        # ---- 阶段 2：pktmon 兜底（回环通常抓不到，但能拿到"驱动/组件在不在"的证据）----
        if ($capMode -notlike 'netsh trace（有效）*') {
            $pm = Get-Cmd 'pktmon'
            if ($pm) {
                try {
                    $etl2 = Join-Path $tmpRoot 'pktmon_probe.etl'
                    $txt2 = Join-Path $tmpRoot 'pktmon_probe.txt'
                    Write-Host ("  -> 阶段2 pktmon filter add LM13688 -t TCP -p 13688")
                    & pktmon filter remove 2>&1 | Out-Null
                    & pktmon filter add LM13688 -t TCP -p 13688 2>&1 | Out-Null
                    Write-Host ("  -> 阶段2 pktmon start --capture --pkt-size 0 --file-name <tmp>（{0} 秒兜底探测）" -f $capProbeSec)
                    & pktmon start --capture --pkt-size 0 -f $etl2 2>&1 | Out-Null
                    $startPktmon = $true
                    Start-Sleep -Seconds $capProbeSec
                    & pktmon stop 2>&1 | Out-Null
                    $startPktmon = $false
                    Start-Sleep -Milliseconds 500
                    if ((Test-Path -LiteralPath $etl2) -and ((Get-Item -LiteralPath $etl2).Length -gt 1024)) {
                        $cv2 = Convert-EtlToText -Etl $etl2 -Txt $txt2
                        if ($cv2.Ok) {
                            $all2 = @(Get-Content -LiteralPath $txt2 -ErrorAction SilentlyContinue)
                            $sp2 = Split-PacketLines -Lines $all2 -Port 13688
                            $capPktmonPortOnly = $sp2.PortOnly.Count
                            $capPktmonValid = $sp2.Valid.Count
                            if ($capPktmonValid -gt 0) {
                                $capMode = 'pktmon（有效）'
                                $capHow = ('pktmon filter add LM13688 -t TCP -p 13688 + pktmon start --capture --pkt-size 0 --file-name <tmp> + pktmon stop；转换：' + $cv2.How)
                                $capEtl = $etl2
                                $capFiltered = $sp2.PortOnly
                                $capFilteredTotal = $sp2.PortOnly.Count
                                $capValidCount = $capPktmonValid
                                if ($capNote) { $capNote += ' ' }
                                $capNote += ('pktmon 兜底 {0} 秒：含 13688 的行 {1} 条，其中【有效报文 {2} 条】。' -f $capProbeSec, $sp2.PortOnly.Count, $capPktmonValid)
                            } else {
                                $capMode = '两阶段都没抓到有效报文'
                                if ($capNote) { $capNote += ' ' }
                                $capNote += ('pktmon 兜底 {0} 秒：含 13688 的行 {1} 条，其中【有效报文 0 条】（多为筛选器回显/组件/计数器行）。' -f $capProbeSec, $sp2.PortOnly.Count)
                                $capFallbackNote = 'netsh trace 与 pktmon 两阶段都未抓到含 13688 的有效报文 ⇒ 本机无法用自带工具抓回环（已知限制）'
                            }
                        } else {
                            $capMode = '两阶段都没抓到有效报文'
                            if ($capNote) { $capNote += ' ' }
                            $capNote += ('pktmon 兜底 ETL 转文本失败（' + $cv2.Err + '）。')
                            $capFallbackNote = '两阶段都失败（netsh 无有效报文、pktmon 转文本失败）'
                        }
                    } else {
                        $capMode = '两阶段都没抓到有效报文'
                        if ($capNote) { $capNote += ' ' }
                        $capNote += ('pktmon 兜底 {0} 秒未产出有效 ETL。' -f $capProbeSec)
                        $capFallbackNote = '两阶段都没抓到（netsh 无有效报文、pktmon 无 ETL）'
                    }
                } catch {
                    $e2 = $_.Exception.Message
                    $capMode = '两阶段都没抓到有效报文'
                    if ($capNote) { $capNote += ' ' }
                    $capNote += ('pktmon 兜底失败（' + $e2 + '）。')
                    $capFallbackNote = '两阶段都失败（netsh 无有效报文、pktmon 报错 ' + $e2 + '）'
                    Warn ('pktmon 兜底失败：' + $e2)
                } finally {
                    if ($startPktmon) { & pktmon stop 2>&1 | Out-Null; $startPktmon = $false }
                    try { & pktmon filter remove 2>&1 | Out-Null } catch { }
                }
            } else {
                $capMode = '两阶段都没抓到有效报文'
                if ($capNote) { $capNote += ' ' }
                $capNote += '本机没有 pktmon，无法做兜底探测。'
                $capFallbackNote = 'netsh trace 未抓到有效报文，且本机没有 pktmon'
            }
        }

        if ($capMode -in @('netsh trace（有效）', 'pktmon（有效）')) {
            $capVerdicts = Analyze-CaptureText -Lines $capFiltered
        } else {
            $capVerdicts = [ordered]@{ CONNECT = '无法判断'; CONNACK = '无法判断'; SUBSCRIBE = '无法判断'; SUBACK = '无法判断' }
        }
    }
} catch {
    $capMode = '抓包阶段异常'
    $capErr = $_.Exception.Message
    Warn ('09 抓包异常：' + $capErr)
} finally {
    # ⚠ 兜底：无论怎么退出，都不留后台抓包
    if ($startPktmon) { try { & pktmon stop 2>&1 | Out-Null } catch { } }
    if ($startTrace) { try { & netsh trace stop 2>&1 | Out-Null } catch { } }
    # ⚠ 兜底：无论怎么退出，都不留后台抓包。
    #    这里不判管理员，让检查尽量跑（非管理员下 pktmon/trace 命令本身会失败，不影响流程）。
    if (-not $SkipCapture -and -not $SelfTestCapture) {
        try { & pktmon filter remove 2>&1 | Out-Null } catch { }
        $leftover = Get-CaptureToolState
        if ($leftover.PktmonRunning -or $leftover.TraceRunning) {
            Warn ('检测到抓包工具仍在运行（pktmon=' + $leftover.PktmonRunning + ' trace=' + $leftover.TraceRunning + '），再次强制停止。')
            try { & pktmon stop 2>&1 | Out-Null } catch { }
            try { & netsh trace stop 2>&1 | Out-Null } catch { }
        }
    }
}

# 写 09（含过滤后的报文文本，≤20000 行 / ≤2MB）
$MAXLINES09 = 20000
$MAXBYTES09 = 2 * 1024 * 1024
$keptLines = @()
$wasTruncated = '否'
if ($capFiltered.Count -gt $MAXLINES09) {
    $keptLines = $capFiltered[0..($MAXLINES09 - 1)]
    $wasTruncated = ('是（原始 {0} 行，只保留前 {1} 行）' -f $capFiltered.Count, $MAXLINES09)
} else {
    $keptLines = @($capFiltered)
}
$keptText = ($keptLines -join "`r`n")
if ($keptText.Length -gt $MAXBYTES09) {
    $keptText = $keptText.Substring(0, $MAXBYTES09)
    $wasTruncated = '是（按 2MB 字节上限截断）'
}

$s09 = New-Object System.Text.StringBuilder
[void]$s09.AppendLine('09 回环抓包（127.0.0.1:13688，系统自带工具，旁路监听，不做任何业务连接）')
[void]$s09.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s09.AppendLine(('抓包时长（本次实际）：{0} 秒' -f $capSeconds))
[void]$s09.AppendLine(('采用方式：' + $capMode))
[void]$s09.AppendLine(('调用语法：' + $(if ($capHow) { $capHow } else { '(未执行抓包)' })))
[void]$s09.AppendLine(('ETL 中间文件：' + $(if ($capEtl) { $capEtl } else { '(无)' }) + '（报告生成后会删除临时目录）'))
[void]$s09.AppendLine(('过滤：只保留含 13688 的行；行数上限 {0} 行 / 字节上限 2MB；本次是否截断：{1}' -f $MAXLINES09, $wasTruncated))
[void]$s09.AppendLine(('含 13688 的行数：{0}' -f $capFilteredTotal))
$pmLine = ''
try {
    $pmPath = Get-Cmd 'pktmon'
    if ($pmPath) {
        $pmLine = ('pktmon 可用（{0}）；但它挂在 NDIS 组件上，127.0.0.1/::1 的回环流量一般抓不到，所以本脚本【先】用 netsh trace(CaptureInterface=<Loopback GUID>) 抓，netsh 阶段有效报文为 0 时【才】用 pktmon 兜底探测。' -f $pmPath)
    } else {
        $pmLine = 'pktmon 不可用（需要 Win10 1809+ 自带；本机没找到）⇒ 只用 netsh trace(CaptureInterface=<Loopback GUID>)，不要求机主装 Wireshark 或任何第三方驱动。'
    }
} catch { $pmLine = '(pktmon 探测失败，已改走 netsh trace)' }
[void]$s09.AppendLine(('工具可用性：' + $pmLine))
if ($capNote) { [void]$s09.AppendLine(('说明：' + $capNote)) }
if ($capErr)  { [void]$s09.AppendLine(('错误：' + $capErr)) }
[void]$s09.AppendLine('')
[void]$s09.AppendLine('== ★ 抓包结论 ==')
[void]$s09.AppendLine(('  【有效性判定】含 13688 的行 {0} 条，其中真正算数的【有效报文 {1} 条】' -f $capFilteredTotal, $capValidCount))
[void]$s09.AppendLine('   有效报文 = 同时满足：①是 TCP/UDP 行 ②有 源/目的 地址:端口 ③有 TCP 标志(SYN/PSH/ACK/FIN/RST)或 payload/长度字段；')
[void]$s09.AppendLine('   已排除：pktmon 筛选器注册回显（如"数据包筛选器 1，名称 LM13688"）、组件/NIC 注册行、计数器/摘要行、纯元数据行。')
[void]$s09.AppendLine(('  阶段1 netsh trace：含端口行 {0}，有效报文 {1}' -f $capNetshPortOnly, $capNetshValid))
[void]$s09.AppendLine(('  阶段2 pktmon     ：含端口行 {0}，有效报文 {1}' -f $capPktmonPortOnly, $capPktmonValid))
if ($capFallbackNote) { [void]$s09.AppendLine(('  回退说明：' + $capFallbackNote)) }
[void]$s09.AppendLine(('  是否看到 CONNECT  ：' + $capVerdicts.CONNECT))
[void]$s09.AppendLine(('  是否看到 CONNACK  ：' + $capVerdicts.CONNACK))
[void]$s09.AppendLine(('  是否看到 SUBSCRIBE：' + $capVerdicts.SUBSCRIBE))
[void]$s09.AppendLine(('  是否看到 SUBACK  ：' + $capVerdicts.SUBACK))
if ($capValidCount -le 0) {
    [void]$s09.AppendLine('  ⚠ 有效报文为 0 ⇒ 上面的四问一律视为【无法判断】，不能当作"没订阅"的证据。')
}
[void]$s09.AppendLine('  说明：本结论基于"ETL 转文本后是否出现相应字样/协议名"的形状判定；')
[void]$s09.AppendLine('        pktmon etl2txt / netsh trace convert 只做报文头+载荷的文本转储，')
[void]$s09.AppendLine('        不一定把 MQTT 控制报文名解码出来。判断不了就写"无法判断/需人工看"。')
if ($capValidCount -le 0) {
    [void]$s09.AppendLine('')
    [void]$s09.AppendLine('  本机无法用自带工具抓到回环有效报文（原因见上）。替代判据：')
    [void]$s09.AppendLine('    (1) 见 02：13688 上有没有 ESTABLISHED、对端 PID 是谁（独立反查，不猜）；')
    [void]$s09.AppendLine('    (2) 打开【官方控制台】，看它能不能连上 GCU（手动结果写进 10-手动结果.txt）。')
}
[void]$s09.AppendLine('')
[void]$s09.AppendLine('== 过滤后的报文文本（原始转储，只保留含 13688 的行）==')
[void]$s09.AppendLine('（注意：本文件随后会统一走隐私打码，长串/主机名/用户名会被替换成占位符）')
[void]$s09.AppendLine($keptText)
Write-Utf8 -Path (Join-Path $OutDir '09-回环抓包.txt') -Text $s09.ToString()
$P.CapMode = $capMode
$P.CapSeconds = $capSeconds
$P.CapLines = $capFilteredTotal
$P.CapValid = $capValidCount
$P.CapVerdicts = $capVerdicts

# ================================================================ 11 独立 MQTT 探针
Step '11 独立 MQTT 探针（与 LM 无关的最小客户端）' | Out-Null

$script:MQTT_Funcs = {
    function Get-MqttRemLen {
        param([byte[]]$Data, [int]$Start)
        $mult = 1; $value = 0; $i = $Start
        do {
            if ($i -ge $Data.Length) { return -1 }
            $b = $Data[$i]
            $value += (($b -band 0x7F) * $mult)
            $mult = $mult * 128
            $i++
            if ($mult -gt (128 * 128 * 128)) { return -1 }
        } while (($b -band 0x80) -ne 0)
        return @{ Len = $value; Consumed = ($i - $Start) }
    }
    function Read-MqttPacket {
        # 读一个 MQTT 控制报文，返回 @{ Ok; Type; Body; Raw }；超时/断流则 Ok=$false
        param([System.IO.Stream]$Stream, [int]$TimeoutMs = 2500, [int]$MaxBytes = 1048576)
        $res = @{ Ok = $false; Type = -1; Body = @(); Raw = @(); Err = '' }
        try {
            $first = $Stream.ReadByte()
            if ($first -lt 0) { $res.Err = '对端已关闭连接（EOF）'; return $res }
            $res.Type = ($first -shr 4)
            $rl = @()
            $mult = 1; $value = 0; $n = 0
            while ($true) {
                $b = $Stream.ReadByte()
                if ($b -lt 0) { $res.Err = '读长度字段时断流'; return $res }
                $rl += [byte]$b
                $value += (($b -band 0x7F) * $mult)
                $mult = $mult * 128
                $n++
                if (($b -band 0x80) -eq 0) { break }
                if ($n -ge 4) { $res.Err = '长度字段非法（>4 字节）'; return $res }
            }
            if ($value -gt $MaxBytes) { $res.Err = ('报文体过大：' + $value); return $res }
            $body = New-Object byte[] $value
            $got = 0
            while ($got -lt $value) {
                $r = $Stream.Read($body, $got, $value - $got)
                if ($r -le 0) { $res.Err = '读报文体时断流'; return $res }
                $got += $r
            }
            $res.Body = $body
            $res.Raw = @([byte]$first) + $rl + $body
            $res.Ok = $true
        } catch {
            $res.Err = $_.Exception.Message
        }
        return $res
    }
    function New-MqttConnect {
        # MQTT 3.1.1 CONNECT（CleanSession=1，keepalive 60）。不设用户名/密码。
        param([string]$ClientId)
        $payload = New-Object System.Collections.Generic.List[byte]
        $payload.AddRange([System.Text.Encoding]::UTF8.GetBytes($ClientId))
        $idBytes = [System.Text.Encoding]::UTF8.GetBytes($ClientId)
        $vh = New-Object System.Collections.Generic.List[byte]
        $vh.AddRange([System.Text.Encoding]::ASCII.GetBytes('MQTT'))   # 协议名
        $vh.Add(4)          # 协议级别 3.1.1
        $vh.Add(0x02)       # 连接标志：CleanSession
        $vh.Add(0); $vh.Add(60)   # keepalive
        $pl = New-Object System.Collections.Generic.List[byte]
        $pl.Add([byte](($idBytes.Length -shr 8) -band 0xFF))
        $pl.Add([byte]($idBytes.Length -band 0xFF))
        $pl.AddRange($idBytes)
        $body = New-Object System.Collections.Generic.List[byte]
        $body.AddRange($vh); $body.AddRange($pl)
        $rl = @()
        $x = $body.Count
        do {
            $d = $x % 128
            $x = [int][Math]::Floor($x / 128)
            if ($x -gt 0) { $d = $d -bor 0x80 }
            $rl += [byte]$d
        } while ($x -gt 0)
        $pkt = New-Object System.Collections.Generic.List[byte]
        $pkt.Add([byte]0x10)   # CONNECT
        $pkt.AddRange($rl); $pkt.AddRange($body)
        return $pkt.ToArray()
    }
    function New-MqttSubscribe {
        # MQTT 3.1.1 SUBSCRIBE，QoS0，单个 topic filter
        param([string]$Topic, [int]$PacketId = 1)
        $tb = [System.Text.Encoding]::UTF8.GetBytes($Topic)
        $body = New-Object System.Collections.Generic.List[byte]
        $body.Add([byte](($PacketId -shr 8) -band 0xFF))
        $body.Add([byte]($PacketId -band 0xFF))
        $body.Add([byte](($tb.Length -shr 8) -band 0xFF))
        $body.Add([byte]($tb.Length -band 0xFF))
        $body.AddRange($tb)
        $body.Add([byte]0x00)   # 请求 QoS0
        $rl = @()
        $x = $body.Count
        do {
            $d = $x % 128
            $x = [int][Math]::Floor($x / 128)
            if ($x -gt 0) { $d = $d -bor 0x80 }
            $rl += [byte]$d
        } while ($x -gt 0)
        $pkt = New-Object System.Collections.Generic.List[byte]
        $pkt.Add([byte]0x82)   # SUBSCRIBE
        $pkt.AddRange($rl); $pkt.AddRange($body)
        return $pkt.ToArray()
    }
    function ConvertTo-HexShort {
        param([byte[]]$Bytes, [int]$Max = 24)
        if (-not $Bytes -or $Bytes.Length -eq 0) { return '(空)' }
        $n = [Math]::Min($Max, $Bytes.Length)
        $s = (($Bytes[0..($n - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' ')
        if ($Bytes.Length -gt $Max) { $s += (' …(共 ' + $Bytes.Length + ' 字节)') }
        return $s
    }
    function Invoke-MqttProbeOne {
        # 对单个 clientId 做：TCP 连接 → CONNECT/CONNACK → 逐个 topic SUBSCRIBE/等 SUBACK → 干净断开
        # ⚠ 只 CONNECT + SUBSCRIBE，绝不 PUBLISH（不碰任何控制主题，不动硬件）
        param([string]$ClientId, [string[]]$Topics, [int]$Port = 13688, [int]$RecvTimeoutMs = 2500)
        $r = [ordered]@{
            ClientId = $ClientId; TcpOk = $false; TcpErr = ''; ConnAck = '未收到'; ConnAckRaw = ''
            ReturnCode = ''; SubResults = @(); AnySubAck = $false; Note = ''
        }
        $cli = $null
        $st = $null
        try {
            $cli = New-Object System.Net.Sockets.TcpClient
            $cli.NoDelay = $true
            $iar = $cli.BeginConnect('127.0.0.1', $Port, $null, $null)
            if (-not $iar.AsyncWaitHandle.WaitOne(4000, $false)) {
                $r.TcpErr = 'TCP 连接超时（4 秒）'
                return $r
            }
            $cli.EndConnect($iar)
            $r.TcpOk = $true
            $st = $cli.GetStream()
            $st.ReadTimeout = $RecvTimeoutMs
            $st.WriteTimeout = $RecvTimeoutMs
            # --- CONNECT ---
            $c = New-MqttConnect -ClientId $ClientId
            $st.Write($c, 0, $c.Length)
            $st.Flush()
            $pk = Read-MqttPacket -Stream $st -TimeoutMs $RecvTimeoutMs
            if (-not $pk.Ok) {
                $r.ConnAck = ('读 CONNACK 失败/超时（' + $pk.Err + '）')
                $r.Note = 'CONNACK 都没拿到 ⇒ 白名单/占用/桥侧问题'
                return $r
            }
            $r.ConnAckRaw = ConvertTo-HexShort -Bytes $pk.Raw -Max 16
            if ($pk.Type -eq 2 -and $pk.Body.Count -ge 2) {
                $rc = [int]$pk.Body[1]
                $r.ReturnCode = ('0x{0:X2}' -f $rc)
                if ($rc -eq 0) { $r.ConnAck = 'CONNACK rc=0x00 (Accepted)' }
                elseif ($rc -eq 1) { $r.ConnAck = 'CONNACK rc=0x01 (拒绝：协议版本不支持)' }
                elseif ($rc -eq 2) { $r.ConnAck = 'CONNACK rc=0x02 (拒绝：clientId 不合法)' }
                elseif ($rc -eq 4) { $r.ConnAck = 'CONNACK rc=0x04 (拒绝：用户名/密码错)' }
                elseif ($rc -eq 5) { $r.ConnAck = 'CONNACK rc=0x05 (拒绝：未授权)' }
                else { $r.ConnAck = ('CONNACK rc=0x{0:X2} (拒绝)' -f $rc) }
                if ($rc -ne 0) { $r.Note = 'broker 明确拒绝了 CONNECT ⇒ 白名单/占用问题' ; return $r }
            } else {
                # 有些 broker 先回别的；把首个非 CONNACK 报文也记下来
                $r.ConnAck = ('收到非 CONNACK 报文（类型 {0}），原始：{1}' -f $pk.Type, (ConvertTo-HexShort -Bytes $pk.Raw -Max 16))
                $r.Note = 'CONNACK 形状异常 ⇒ 需人工看'
                return $r
            }
            # --- SUBSCRIBE 逐个主题 ---
            $pid = 1
            foreach ($t in $Topics) {
                $one = [ordered]@{ Topic = $t; Sent = $true; SubAck = '未收到'; Granted = ''; ElapsedMs = -1; Raw = ''; Err = '' }
                try {
                    $s = New-MqttSubscribe -Topic $t -PacketId $pid
                    $pid++
                    $sw2 = [Diagnostics.Stopwatch]::StartNew()
                    $st.Write($s, 0, $s.Length)
                    $st.Flush()
                    $sp = Read-MqttPacket -Stream $st -TimeoutMs $RecvTimeoutMs
                    $sw2.Stop()
                    $one.ElapsedMs = [int]$sw2.Elapsed.TotalMilliseconds
                    if ($sp.Ok -and $sp.Type -eq 9) {
                        $one.SubAck = '收到 SUBACK'
                        $r.AnySubAck = $true
                        if ($sp.Body.Count -ge 3) {
                            $codes = @()
                            for ($k = 2; $k -lt $sp.Body.Count; $k++) {
                                $cc = [int]$sp.Body[$k]
                                if ($cc -eq 0) { $codes += '0x00(成功/QoS0)' }
                                elseif ($cc -eq 1) { $codes += '0x01(成功/QoS1)' }
                                elseif ($cc -eq 2) { $codes += '0x02(成功/QoS2)' }
                                elseif ($cc -eq 0x80) { $codes += '0x80(订阅被拒)' }
                                else { $codes += ('0x{0:X2}' -f $cc) }
                            }
                            $one.Granted = ($codes -join ',')
                        }
                        $one.Raw = ConvertTo-HexShort -Bytes $sp.Raw -Max 16
                    } elseif ($sp.Ok) {
                        $one.SubAck = ('收到非 SUBACK 报文（类型 {0}）' -f $sp.Type)
                        $one.Raw = ConvertTo-HexShort -Bytes $sp.Raw -Max 16
                    } else {
                        $one.SubAck = ('超时/断开，未收到 SUBACK（' + $sp.Err + '）')
                        $one.Err = $sp.Err
                    }
                } catch {
                    $one.SubAck = ('发送/接收失败：' + $_.Exception.Message)
                    $one.Err = $_.Exception.Message
                }
                $r.SubResults += [pscustomobject]$one
                if (-not $sp.Ok -and $one.Err) { break }   # 连接已坏，后面的主题不必再试
            }
        } catch {
            $r.TcpErr = ('异常：' + $_.Exception.Message)
        } finally {
            # ⚠ 跑完即断开：先发 DISCONNECT(0xE0 0x00) 再关 socket，不留连接
            try { if ($st) { $d = [byte[]]@(0xE0, 0x00); $st.Write($d, 0, 2); $st.Flush() } } catch { }
            try { if ($st) { $st.Dispose() } } catch { }
            try { if ($cli) { $cli.Close() } } catch { }
        }
        return $r
    }
}

$probeResult = @{
    Ran = $false; SkipReason = ''; Overall = '未跑'; ChosenClientId = ''; Topics = @()
    Attempts = @(); AnySubAck = $false; ConnAckOk = $false; Conclusion = '证据不足'
    LmRunning = $false; TryIds = @(); TcpOnlySilence = $false
}

try {
    if ($SkipProbe) {
        $probeResult.SkipReason = '按参数 -SkipProbe 主动跳过'
        $probeResult.Overall = '已跳过'
    } else {
        . $script:MQTT_Funcs
        # 主题优先取 LM 实际订阅过的真实主题（LM 日志里能看到 xxx/Status 这类），QoS0，最后用 # 兜底。
        $probeTopics = @('GPUDevice/Status', 'HidLightbar_Sync/Status', 'Fan/Control', 'Setting/Status', '#')
        $probeResult.Topics = $probeTopics
        # LM 是否在跑：探针会占用一个白名单 clientId，所以先看清楚
        try {
            $probeResult.LmRunning = (@(Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue).Count -gt 0)
        } catch { }
        # 默认只试 UWPClient_3 → UWPClient_2（历史记录里较少被占用的两个）。
        # -ProbeExtended 才追加 UWPClient_1/_4（_4 是 LM 首选 id，必须在 LM 已退出时才用）。
        $tryIds = @('UWPClient_3', 'UWPClient_2')
        if ($ProbeExtended) { $tryIds += 'UWPClient_1'; $tryIds += 'UWPClient_4' }
        $probeResult.TryIds = $tryIds
        $attempts = New-Object System.Collections.ArrayList
        foreach ($cid in $tryIds) {
            Write-Host ('  -> 探针尝试 clientId=' + $cid + '（只 CONNECT + SUBSCRIBE，绝不 PUBLISH）')
            $one = Invoke-MqttProbeOne -ClientId $cid -Topics $probeTopics -Port 13688 -RecvTimeoutMs 2500
            [void]$attempts.Add($one)
            if ($one.TcpOk -and $one.ReturnCode -eq '0x00') {
                $probeResult.ChosenClientId = $cid
                $probeResult.ConnAckOk = $true
                if ($one.AnySubAck) { $probeResult.AnySubAck = $true }
                break   # 一次成功就够了，不再占用第二个白名单 id
            }
        }
        $probeResult.Attempts = $attempts.ToArray()
        $probeResult.Ran = $true
        $tcpOnly = @($attempts | Where-Object { $_.TcpOk -and $_.ReturnCode -ne '0x00' })
        $probeResult.TcpOnlySilence = ($tcpOnly.Count -gt 0 -and -not $probeResult.ConnAckOk)
        if ($probeResult.ConnAckOk -and $probeResult.AnySubAck) {
            $probeResult.Overall = 'CONNACK 成功 + 收到 SUBACK'
            $probeResult.Conclusion = '探针能收到 SUBACK ⇒ 问题在 LM 客户端侧（桥侧正常）'
        } elseif ($probeResult.ConnAckOk) {
            $probeResult.Overall = 'CONNACK 成功，但没收到 SUBACK'
            $probeResult.Conclusion = '探针也收不到 SUBACK ⇒ 桥侧问题（与 LM 无关）'
        } elseif ($probeResult.TcpOnlySilence) {
            $probeResult.Overall = 'TCP 能连上，但 broker 不给 CONNACK（静默）'
            $probeResult.Conclusion = '探针 TCP 连上了却收不到 CONNACK ⇒ 桥侧对新连接静默（clientId 占用/白名单/桥未就绪皆可能）。'
            if ($probeResult.LmRunning) {
                $probeResult.Conclusion += ' ⚠ 本次 LM 在跑，无法排除"同 id 被占"；请【退出 LM】后重跑 -ProbeExtended 再判。'
            } else {
                $probeResult.Conclusion += ' LM 当时没跑 ⇒ 更像白名单/桥侧问题，请结合 01/02 看。'
            }
        } elseif ($attempts.Count -gt 0) {
            $probeResult.Overall = 'CONNACK 都没成功'
            $probeResult.Conclusion = '探针连 CONNACK 都失败/被拒 ⇒ 白名单/占用问题（或桥侧未就绪），需结合 02/01 一起看'
        } else {
            $probeResult.Overall = '未产生结果'
            $probeResult.Conclusion = '证据不足（探针没有产生任何结果）'
        }
    }
} catch {
    $probeResult.Overall = ('探针异常：' + $_.Exception.Message)
    $probeResult.Conclusion = '证据不足（探针异常）'
    Warn ('11 探针异常：' + $_.Exception.Message)
} finally {
    $script:MQTT_Funcs = $null
}

# ---- 写 11-独立MQTT探针.txt ----
$s11 = New-Object System.Text.StringBuilder
[void]$s11.AppendLine('11 独立 MQTT 探针（与 LM 完全无关的最小客户端；只连本机回环 127.0.0.1:13688）')
[void]$s11.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s11.AppendLine('')
[void]$s11.AppendLine('== 做什么 / 不做什么 ==')
[void]$s11.AppendLine('  做    ：TCP 连 127.0.0.1:13688 → 手写 MQTT 3.1.1 CONNECT（CleanSession=1）→ 读 CONNACK →')
[void]$s11.AppendLine('          手写 SUBSCRIBE(QoS0) → 等 SUBACK（记录耗时）→ 发 DISCONNECT 并立即断开。')
[void]$s11.AppendLine('  不做  ：⚠ 绝不 PUBLISH（不碰任何控制主题、不动硬件、不改任何设置）；不装任何第三方库（纯 .NET 自带）。')
[void]$s11.AppendLine('  说明  ：MQTT 报文是用 System.Net.Sockets.TcpClient 手搓的，不依赖 MQTTnet 等任何库。')
[void]$s11.AppendLine('')
[void]$s11.AppendLine('== ⚠ 重要前提 ==')
[void]$s11.AppendLine('  探针会占用一个白名单 clientId（UWPClient_3，失败再试 UWPClient_2）。')
[void]$s11.AppendLine('  强烈建议【先退出 LM 再跑本探针】，避免和 LM 抢同一个 clientId 把它的会话顶掉。')
[void]$s11.AppendLine(('  本次运行前检测到 LM 进程：' + $(if ($probeResult.LmRunning) { '在跑 ⚠（探针结果可能受占用影响）' } else { '不在跑（干净 A/B）' })))
[void]$s11.AppendLine('  官方控制台若已删除，本条（以及 10 的说明）就是原来的"决定性测试"的替代。')
[void]$s11.AppendLine('')
[void]$s11.AppendLine('== 结果 ==')
[void]$s11.AppendLine(('  是否执行      ：' + $(if ($probeResult.Ran) { '是' } else { '否（' + $probeResult.SkipReason + '）' })))
[void]$s11.AppendLine(('  尝试的 clientId：' + $(if ($probeResult.ChosenClientId) { $probeResult.ChosenClientId + '（成功）' } else { (($probeResult.TryIds) -join ' → ') })))
[void]$s11.AppendLine(('  订阅的 topic  ：' + (($probeResult.Topics) -join ', ') + '（逐个试，QoS0；# 是通配符兜底）'))
[void]$s11.AppendLine(('  CONNACK       ：' + $(if ($probeResult.ConnAckOk) { '成功（rc=0x00）' } else { '未成功' })))
[void]$s11.AppendLine(('  是否收到 SUBACK：' + $(if ($probeResult.AnySubAck) { '是' } else { '否' })))
[void]$s11.AppendLine('')
if ($probeResult.Attempts.Count -eq 0) {
    [void]$s11.AppendLine('  （没有产生任何尝试记录）')
} else {
    foreach ($a in $probeResult.Attempts) {
        [void]$s11.AppendLine(('  --- clientId=' + $a.ClientId + ' ---'))
        [void]$s11.AppendLine(('    TCP 连接      ：' + $(if ($a.TcpOk) { '成功' } else { '失败/超时（' + $a.TcpErr + '）' })))
        [void]$s11.AppendLine(('    CONNACK       ：' + $a.ConnAck + $(if ($a.ConnAckRaw) { '  原始(hex)：' + $a.ConnAckRaw } else { '' })))
        if ($a.Note) { [void]$s11.AppendLine(('    备注          ：' + $a.Note)) }
        foreach ($sr in $a.SubResults) {
            [void]$s11.AppendLine(('    SUBSCRIBE ' + $sr.Topic + ' → ' + $sr.SubAck + $(if ($sr.Granted) { '，granted=' + $sr.Granted } else { '' }) + $(if ($sr.ElapsedMs -ge 0) { ('，耗时 ' + $sr.ElapsedMs + ' ms') } else { '' })))
            if ($sr.Raw) { [void]$s11.AppendLine(('        原始(hex)：' + $sr.Raw)) }
        }
    }
}
[void]$s11.AppendLine('')
[void]$s11.AppendLine('== ★ 结论（三选一）==')
[void]$s11.AppendLine('  ' + $probeResult.Conclusion)
[void]$s11.AppendLine('')
[void]$s11.AppendLine('  口径：')
[void]$s11.AppendLine('    * 探针也收不到 SUBACK      ⇒ 桥侧问题（与 LM 无关）')
[void]$s11.AppendLine('    * 探针能收到 SUBACK        ⇒ 问题在 LM 客户端侧')
[void]$s11.AppendLine('    * 探针连 CONNACK 都失败/被拒 ⇒ 白名单/占用问题（需结合 01/02 看 GCUBridge 是否在听、clientId 是否被占）')
[void]$s11.AppendLine('    * TCP 连得上但 broker 静默  ⇒ 桥侧对新连接不回 CONNACK；若此时 LM 正在跑，')
[void]$s11.AppendLine('      请【退出 LM】后重跑 `-ProbeExtended`（会把 UWPClient_1/_4 也试一遍）来区掉"同 id 被占"。')
Write-Utf8 -Path (Join-Path $OutDir '11-独立MQTT探针.txt') -Text $s11.ToString()
$P.ProbeOverall = $probeResult.Overall
$P.ProbeConclusion = $probeResult.Conclusion
$P.ProbeAnySubAck = $probeResult.AnySubAck
$P.ProbeConnAckOk = $probeResult.ConnAckOk
$P.ProbeRan = $probeResult.Ran

# ================================================================ 12 GCU 日志"可发现"
# 2026-10-09（方案B）：内置诊断新补的「GCU 日志路径发现」在独立脚本里没有 —— 30 系实测原来那 3 条硬编码
# 路径全不中，真日志到底在哪**包看不到**，这是本周期反复卡住我们的采集缺口。本节按"可发现"补：
# 底座根（注册表 GcuServiceExe 推导，兼容 AiStoneService / 直挂两种布局）+ 安装根（%ProgramFiles%\L-Mechrevo、
# D:\L-Mechrevo）+ %ProgramData% + %LOCALAPPDATA%/%APPDATA% 下与 GCU/ControlCenter 相关的目录，递归找 *.log。
# ⚠ 只读：只列"路径/大小/mtime"，只对近 24h 的取尾部（每文件与合计都有上限）；因上限跳过的项逐条写明，绝不静默。
Step '12 GCU 日志可发现（只读；底座根/安装根/%ProgramData%）' | Out-Null

$script:DiscMaxDepth   = 6        # 目录深度上限（与内置诊断 DiscoverMaxDepth 同口径）
$script:DiscMaxDirs    = 4000     # 访问目录数上限
$script:DiscMaxFiles   = 300      # 收集 *.log 数上限
$script:DiscMaxRelFiles = 100     # "GCU 相关" *.log 的【独立】额度（不被第三方日志挤掉；真机实测过这条坑）
# "GCU 相关"判据：路径里含这些词就算 —— 供"GCU 日志可发现"与尾部/关键行优先使用（口径只用一处，避免两套）
$script:DiscRelPat     = '(?i)mechrevo|gcu|controlcenter|gamingcenter|aistone|osdtpdetect|uwacpi|mycontrolcenter'
$script:DiscListCap    = 40       # 清单最多列 40 条（近 24h 优先，同内置口径）
$script:DiscRootCap    = 12       # 搜索根上限
$script:DiscTailFiles  = 8        # 最多给 8 个"近 24h"文件取尾部
$script:DiscTailLines  = 200      # 每文件 ≤200 行（沿用本脚本"每文件有限行"的风格）
$script:DiscTailBytes  = 32768    # 每文件 ≤32KB
$script:DiscTailBudget = 524288   # 尾部合计预算 ≤512KB
# ⚠ 命名：这些"上限常量"必须与下面的累加变量（$discKeyLines / $discKeyTotal）区分开 ——
#   PowerShell 变量名【不区分大小写】，撞名会把上限悄悄改成 ArrayList（真机实测已踩过这条）。
$script:DiscKeyPerFile  = 40      # 每文件关键行 ≤40 行
$script:DiscKeyAllTotal = 300     # 关键行合计 ≤300 行
$script:DiscKeyPattern = 'LM_Manager|LM_Init|RGBKB|solution|UNKNOWN|Keyboard|Subscribe|SUBACK|Mqtt|ACPIDriverInit|SingleColorKeyboardProjectIDs|IsCommercialPlatform|Error|Exception'

$script:DiscDirs    = 0
$script:DiscFiles   = 0
$script:DiscFilesRel = 0
$script:DiscFileCapNoted = $false
$script:DiscRelCapNoted = $false
$script:DiscNotes   = New-Object System.Collections.ArrayList
$script:DiscRootList = New-Object System.Collections.ArrayList

function Add-DiscRoot {
    param([string]$Path, [string]$Why)
    if (-not $Path) { return }
    $p = [string]$Path
    try { $p = [System.IO.Path]::GetFullPath($p) } catch { }
    foreach ($r in $script:DiscRootList) {
        if ([string]::Equals([string]$r.P, $p, [System.StringComparison]::OrdinalIgnoreCase)) { return }
    }
    if ($script:DiscRootList.Count -ge $script:DiscRootCap) {
        [void]$script:DiscNotes.Add(('搜索根上限 {0} 已达 ⇒ 未加入：{1}（来源：{2}）' -f $script:DiscRootCap, $p, $Why))
        return
    }
    [void]$script:DiscRootList.Add(@{ P = $p; Why = $Why })
}

function Find-LogsUnder {
    # 广度受限地递归找 *.log：深度/目录/文件三重上限，不跟随目录联接；超限都记进 $script:DiscNotes
    param([string]$Root)
    $hits = New-Object System.Collections.ArrayList
    if (-not (Test-Path -LiteralPath $Root)) { return $hits }
    $stack = New-Object System.Collections.Stack
    $stack.Push(@{ P = $Root; D = 0 })
    while ($stack.Count -gt 0) {
        if ($script:DiscDirs -ge $script:DiscMaxDirs) {
            [void]$script:DiscNotes.Add(('目录数上限 {0} 已达 ⇒ 停止遍历（未访问：{1}）' -f $script:DiscMaxDirs, $Root))
            break
        }
        $cur = $stack.Pop()
        $script:DiscDirs++
        try {
            foreach ($f in @(Get-ChildItem -LiteralPath $cur.P -File -Force -Filter '*.log' -ErrorAction SilentlyContinue)) {
                $isRel = ([string]$f.FullName -match $script:DiscRelPat)
                if ($script:DiscFiles -ge $script:DiscMaxFiles) {
                    # ⚠ 真机实测的坑：%ProgramData% 下第三方日志（NVIDIA/杀软/浏览器）会先把 300 个额度吃光，
                    #   结果 GCU 自己的日志（ControlCenter / L-Mechrevo\logs）反而一个都收不到 ⇒
                    #   GCU 相关的一律【额外放行】（独立额度），这才对得起"可发现"这三个字。
                    if (-not $isRel) {
                        if (-not $script:DiscFileCapNoted) {
                            [void]$script:DiscNotes.Add(('*.log 数上限 {0} 已达 ⇒ 非 GCU 相关的日志不再收集（GCU 相关的仍会收，另有独立额度 {1} 个）；首个撞上限目录：{2}' -f $script:DiscMaxFiles, $script:DiscMaxRelFiles, $cur.P))
                            $script:DiscFileCapNoted = $true
                        }
                        continue
                    }
                    if ($script:DiscFilesRel -ge $script:DiscMaxRelFiles) {
                        if (-not $script:DiscRelCapNoted) {
                            [void]$script:DiscNotes.Add(('GCU 相关 *.log 独立额度 {0} 个也已用尽 ⇒ 不再收集（目录：{1}）' -f $script:DiscMaxRelFiles, $cur.P))
                            $script:DiscRelCapNoted = $true
                        }
                        continue
                    }
                    $script:DiscFilesRel++
                    [void]$hits.Add($f)
                    continue
                }
                $script:DiscFiles++
                if ($isRel) { $script:DiscFilesRel++ }
                [void]$hits.Add($f)
            }
        } catch {
            [void]$script:DiscNotes.Add(('枚举文件失败：{0}：{1}' -f $cur.P, $_.Exception.Message))
        }
        if ([int]$cur.D -ge $script:DiscMaxDepth) {
            try {
                $sub = @(Get-ChildItem -LiteralPath $cur.P -Directory -Force -ErrorAction SilentlyContinue)
                if ($sub.Count -gt 0) {
                    [void]$script:DiscNotes.Add(('深度上限 {0} 层已达 ⇒ 未下钻：{1}（其下 {2} 个子目录未访问）' -f $script:DiscMaxDepth, $cur.P, $sub.Count))
                }
            } catch { }
            continue
        }
        try {
            foreach ($d in @(Get-ChildItem -LiteralPath $cur.P -Directory -Force -ErrorAction SilentlyContinue)) {
                if (([int]$d.Attributes -band [int][System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    [void]$script:DiscNotes.Add(('跳过目录联接/符号链接（不跟随）：{0}' -f $d.FullName))
                    continue
                }
                $stack.Push(@{ P = $d.FullName; D = ([int]$cur.D + 1) })
            }
        } catch {
            [void]$script:DiscNotes.Add(('枚举子目录失败：{0}：{1}' -f $cur.P, $_.Exception.Message))
        }
    }
    return $hits
}

# ---- 搜索根：① 底座根 ② 底座根的上级（安装根·底座侧）③ 注册表 GcuServiceExe 父目录
#      ④ 注册表 GcuServiceDir（可能是相对名）⑤ 两个安装根 ⑥ %ProgramData% ⑦ AppData 下相关目录 ----
Add-DiscRoot -Path $baseRootGuess -Why '底座根（04 实测探测所得，兼容两种布局）'
try { Add-DiscRoot -Path (Split-Path -Parent $baseRootGuess.TrimEnd('\')) -Why '底座根的上级（安装根·底座侧）' } catch { }
$svcExeReg = ''
try {
    $svcExeReg = [string](Get-RegValueByName -Res $regLm -Name 'GcuServiceExe')
    if ($svcExeReg) { Add-DiscRoot -Path (Split-Path -Parent $svcExeReg) -Why '注册表 HKLM\SOFTWARE\L-Mechrevo\GcuServiceExe 的父目录' }
} catch { [void]$script:DiscNotes.Add('读 GcuServiceExe 失败：' + $_.Exception.Message) }
try {
    $svcDirReg = [string](Get-RegValueByName -Res $regLm -Name 'GcuServiceDir')
    if ($svcDirReg) {
        if ([System.IO.Path]::IsPathRooted($svcDirReg)) {
            Add-DiscRoot -Path $svcDirReg -Why '注册表 GcuServiceDir（绝对路径）'
        } else {
            $pd = ''
            if ($svcExeReg) { $pd = Split-Path -Parent $svcExeReg }
            if (-not $pd) { $pd = Split-Path -Parent $baseRootGuess.TrimEnd('\') }
            if ($pd) {
                # ⚠ GcuServiceDir 常写成相对名（如 AiStoneService），而它的父目录往往【本身就是】那个目录
                #   （GcuServiceExe = …\GCU\AiStoneService\GCUBridge.exe）—— 直接拼会拼成 …\AiStoneService\AiStoneService。
                #   所以：若拼出来的路径不存在、而父目录的叶子名恰好就是它，就直接用父目录。
                $joined = Join-Path $pd $svcDirReg
                $leafPd = ''
                try { $leafPd = Split-Path -Leaf $pd.TrimEnd('\') } catch { }
                if ((-not (Test-Path -LiteralPath $joined)) -and $leafPd -and ($leafPd -ieq $svcDirReg)) {
                    Add-DiscRoot -Path $pd -Why ('注册表 GcuServiceDir（相对名 ' + $svcDirReg + '；父目录叶子名与它同名 ⇒ 直接取父目录）')
                } else {
                    Add-DiscRoot -Path $joined -Why ('注册表 GcuServiceDir（相对名 ' + $svcDirReg + '，拼在父目录下）')
                }
            }
        }
    }
} catch { [void]$script:DiscNotes.Add('读 GcuServiceDir 失败：' + $_.Exception.Message) }
Add-DiscRoot -Path (Join-Path $env:ProgramFiles 'L-Mechrevo') -Why '安装根（%ProgramFiles%\L-Mechrevo）'
Add-DiscRoot -Path 'D:\L-Mechrevo' -Why '安装根（D:\L-Mechrevo，存在才扫）'
Add-DiscRoot -Path $env:ProgramData -Why '%ProgramData%'
foreach ($base7 in @($env:LOCALAPPDATA, $env:APPDATA)) {
    if (-not $base7) { continue }
    $n7 = 0
    try {
        foreach ($d7 in @(Get-ChildItem -LiteralPath $base7 -Directory -Force -ErrorAction SilentlyContinue | Sort-Object Name)) {
            if ($d7.Name -match '(?i)mechrevo|controlcenter|gamingcenter|gcu|aistone') {
                if ($n7 -ge 8) {
                    [void]$script:DiscNotes.Add(('相关目录每根上限 8 已达 ⇒ 未加入：{0}\{1}' -f $base7, $d7.Name))
                    break
                }
                Add-DiscRoot -Path $d7.FullName -Why ('GCU/ControlCenter 相关目录（' + $base7 + ' 下）')
                $n7++
            }
        }
    } catch { }
}

$s12 = New-Object System.Text.StringBuilder
[void]$s12.AppendLine('12 GCU 日志"可发现"（⚠ 只读：只枚举 *.log、只对近 24h 取尾部；因上限跳过项逐条写明）')
[void]$s12.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s12.AppendLine('')
[void]$s12.AppendLine('== 口径与上限 ==')
[void]$s12.AppendLine(('  深度 ≤{0} 层 · 访问目录 ≤{1} 个 · *.log ≤{2} 个（⚠ GCU 相关的另有独立额度 {3} 个，不会被第三方日志挤掉）· 清单 ≤{4} 条（按 mtime 新→旧，近 24h 优先）· 不跟随目录联接（junction/symlink）' -f $script:DiscMaxDepth, $script:DiscMaxDirs, $script:DiscMaxFiles, $script:DiscMaxRelFiles, $script:DiscListCap))
[void]$s12.AppendLine(('  尾部：最多 {0} 个"近 24h"文件，每文件 ≤{1} 行且 ≤{2} 字节，合计预算 ≤{3} 字节；关键行每文件 ≤{4} 行、合计 ≤{5} 行' -f $script:DiscTailFiles, $script:DiscTailLines, $script:DiscTailBytes, $script:DiscTailBudget, $script:DiscKeyPerFile, $script:DiscKeyAllTotal))
[void]$s12.AppendLine('')
[void]$s12.AppendLine('== 搜索范围（逐个根：存在/不存在 + 命中数）==')
$discAll = New-Object System.Collections.ArrayList
$discRootOk = 0
foreach ($r in $script:DiscRootList) {
    if (-not (Test-Path -LiteralPath $r.P)) {
        [void]$s12.AppendLine(('  · 根: {0}  —— 不存在（来源：{1}）' -f $r.P, $r.Why))
        continue
    }
    $discRootOk++
    $dirsBefore = $script:DiscDirs
    $hits = @(Find-LogsUnder -Root $r.P)
    foreach ($h in $hits) { [void]$discAll.Add($h) }
    [void]$s12.AppendLine(('  · 根: {0}（含子目录）  命中 {1} 个 *.log；本根访问目录 {2} 个（来源：{3}）' -f $r.P, $hits.Count, ($script:DiscDirs - $dirsBefore), $r.Why))
}
[void]$s12.AppendLine(('  小结：加入搜索根 {0} 个（上限 {1}），其中实际存在 {2} 个' -f $script:DiscRootList.Count, $script:DiscRootCap, $discRootOk))
[void]$s12.AppendLine('')

# ---- 去重 + 分档（近 24h / 近 7d / 更早）----
$discMap = @{}
foreach ($h in $discAll) { if (-not $discMap.ContainsKey($h.FullName)) { $discMap[$h.FullName] = $h } }
$discFilesSorted = @($discMap.Values | Sort-Object LastWriteTime -Descending)
$nowTs = Get-Date
$t24h = $nowTs.AddHours(-24)
$t7d  = $nowTs.AddDays(-7)
$c24 = @($discFilesSorted | Where-Object { $_.LastWriteTime -ge $t24h }).Count
$c7d = @($discFilesSorted | Where-Object { $_.LastWriteTime -ge $t7d }).Count
$cOlder = $discFilesSorted.Count - $c7d
$sumKb = 0
if ($discFilesSorted.Count -gt 0) {
    $sumLen = ($discFilesSorted | Measure-Object -Property Length -Sum).Sum
    if ($sumLen) { $sumKb = [math]::Round(([double]$sumLen) / 1024, 0) }
}
$P.LogDiscHits = $discFilesSorted.Count
$P.LogDiscRecent24h = $c24

# ⚠ 关键：光按 mtime 排序的"前 40 条"会被 %ProgramData% 下的第三方日志（NVIDIA/杀软/浏览器）挤满，
#   真·GCU 日志反而看不见 —— 所以先单独给一块【GCU 相关命中】，再给整体的 mtime 清单。
$recent24All = @($discFilesSorted | Where-Object { $_.LastWriteTime -ge $t24h })
$discRelPat = $script:DiscRelPat
$gcuHitAll = @($discFilesSorted | Where-Object { $_.FullName -match $discRelPat })
$P.LogDiscGcuHits = $gcuHitAll.Count

[void]$s12.AppendLine('== ★★ GCU 相关命中（路径含 mechrevo/GCU/ControlCenter/GamingCenter/AiStone/OSDTpDetect/UWACPI；最多列 30 条）==')
[void]$s12.AppendLine(('  共 {0} 个 GCU 相关 *.log（近 24h {1} 个）—— 这一块才是"GCU 日志到底在哪"的答案' -f $gcuHitAll.Count, @($gcuHitAll | Where-Object { $_.LastWriteTime -ge $t24h }).Count))
if ($gcuHitAll.Count -eq 0) {
    [void]$s12.AppendLine('  ⚠ 一个 GCU 相关日志都没有（上面这些根下确实没有）—— 这本身就是结论：GCU 侧当前没在写 *.log，')
    [void]$s12.AppendLine('     或日志写到了别的扩展名/别的根（本节只找 *.log；也可能是上限先撞上，见末尾"因上限而跳过"）。')
} else {
    $gi = 0
    foreach ($f in $gcuHitAll) {
        if ($gi -ge 30) { [void]$s12.AppendLine(('  …另有 {0} 个 GCU 相关日志未列出（上限 30 条）' -f ($gcuHitAll.Count - 30))); break }
        $gi++
        $mark = ''
        if ($f.LastWriteTime -ge $t24h) { $mark = '[近24h] ' }
        elseif ($f.LastWriteTime -ge $t7d) { $mark = '[近7d]  ' }
        [void]$s12.AppendLine(('  {0,3}. {1}{2}  {3} 字节  mtime={4}' -f $gi, $mark, $f.FullName, $f.Length, $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
    }
}
[void]$s12.AppendLine('')
[void]$s12.AppendLine('== ★ 全部命中清单（按 mtime 新→旧；⭐ 近 24h 在最前；最多 40 条）==')
[void]$s12.AppendLine(('  命中：共 {0} 个 *.log（近 24h {1} 个 / 近 7d {2} 个 / 更早 {3} 个），体积合计 {4} KB' -f $discFilesSorted.Count, $c24, $c7d, $cOlder, $sumKb))
if ($discFilesSorted.Count -eq 0) {
    [void]$s12.AppendLine('  （一个都没有 —— 那是"上面这些根下确实没有 *.log"，不是"本机没有日志能力"；')
    [void]$s12.AppendLine('    也不排除是深度/目录/数量上限先撞上，请看本节末尾的"因上限而跳过"清单。）')
} else {
    $idx = 0
    foreach ($f in $discFilesSorted) {
        if ($idx -ge $script:DiscListCap) { break }
        $idx++
        $mark = ''
        if ($f.LastWriteTime -ge $t24h) { $mark = '[近24h] ' }
        elseif ($f.LastWriteTime -ge $t7d) { $mark = '[近7d]  ' }
        [void]$s12.AppendLine(('  {0,3}. {1}{2}  {3} 字节  mtime={4}' -f $idx, $mark, $f.FullName, $f.Length, $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
    }
    if ($discFilesSorted.Count -gt $script:DiscListCap) {
        [void]$s12.AppendLine(('  …另有 {0} 个未列出（清单上限 {1} 条；按 mtime 新→旧，前面的已是最新那批）' -f ($discFilesSorted.Count - $script:DiscListCap), $script:DiscListCap))
    }
}
[void]$s12.AppendLine('')

# ---- 近 24h 的取尾部（每文件 + 合计都有上限；⚠ GCU 相关的优先，避免把几百 KB 无关第三方日志塞进包）----
$recent24 = $recent24All
$recentRel = @($recent24 | Where-Object { $_.FullName -match $discRelPat })
$recentOther = @($recent24 | Where-Object { $_.FullName -notmatch $discRelPat })
if ($recentRel.Count -gt 0) { $tailTargets = $recentRel }
else { $tailTargets = @($recentOther | Select-Object -First 3) }
[void]$s12.AppendLine(('== ★ 近 24h 日志尾部（最多 {0} 个文件；每文件 ≤{1} 行且 ≤{2} 字节；合计预算 ≤{3} 字节）==' -f $script:DiscTailFiles, $script:DiscTailLines, $script:DiscTailBytes, $script:DiscTailBudget))
[void]$s12.AppendLine(('  选取口径：近 24h 里名字/路径含 mechrevo/GCU/ControlCenter/GamingCenter/AiStone/OSDTpDetect/UWACPI 的【GCU 相关】日志优先（' + $recentRel.Count + ' 个）；'))
[void]$s12.AppendLine(('            非 GCU 相关的近 24h 日志共 ' + $recentOther.Count + ' 个 —— 本节【不取它们的内容】（清单已列全），避免把无关第三方日志撑进诊断包。'))
if ($recentRel.Count -eq 0 -and $recentOther.Count -gt 0) {
    [void]$s12.AppendLine('  ⚠ 近 24h 里一个 GCU 相关日志都没有 ⇒ 退而取最新的 3 个非 GCU 日志作旁证（只为证明"日志确实在写"）。')
}
$tailBudget = $script:DiscTailBudget
$tailedCount = 0
if ($tailTargets.Count -eq 0) {
    [void]$s12.AppendLine('  （没有"近 24h"的 *.log ⇒ 无尾部可取；这不等于失败，命中情况见上面的清单）')
} else {
    foreach ($f in $tailTargets) {
        if ($tailedCount -ge $script:DiscTailFiles) {
            [void]$s12.AppendLine(('  ⚠ 因"尾部文件数上限 {0}"而跳过 {1} 个近 24h 文件（从 {2} 起，未取内容）' -f $script:DiscTailFiles, ($tailTargets.Count - $tailedCount), $f.FullName))
            break
        }
        if ($tailBudget -le 2048) {
            [void]$s12.AppendLine(('  ⚠ 因"尾部合计预算 {0} 字节"用尽而跳过其余近 24h 文件的尾部（从 {1} 起）' -f $script:DiscTailBudget, $f.FullName))
            break
        }
        $dig = Invoke-Safe -Label ('12 尾部 ' + $f.FullName) -Action { Get-LogDigest -Path $f.FullName -MaxLines $script:DiscTailLines -MaxBytes $script:DiscTailBytes } -Fallback (('---- ' + $f.FullName + ' ----') + "`r`n  该项失败")
        [void]$s12.AppendLine($dig)
        [void]$s12.AppendLine('')
        if ($dig) { $tailBudget -= $dig.Length }
        $tailedCount++
    }
}
$P.LogDiscTailFiles = $tailedCount

[void]$s12.AppendLine('== 规模与"因上限而跳过"（逐条写明）==')
[void]$s12.AppendLine(('  规模：访问目录 {0} 个（上限 {1}）· 收集 *.log {2} 个（普通额度上限 {3}；其中 GCU 相关 {4} 个，独立额度上限 {5}）· 取尾部 {6} 个文件' -f $script:DiscDirs, $script:DiscMaxDirs, $script:DiscFiles, $script:DiscMaxFiles, $script:DiscFilesRel, $script:DiscMaxRelFiles, $tailedCount))
if ($script:DiscNotes.Count -eq 0) {
    [void]$s12.AppendLine('  超限跳过：无（没有任何项因深度/目录数/文件数/大小上限而被跳过）')
} else {
    # ⚠ "绝不静默"要满足，但同类上限（如 %ProgramData% 下几十个 NVIDIA 深层目录）逐条刷屏没有信息量 ——
    #   所以按"同类"归并：给每类的条数 + 前 3 个示例路径，其余只报数（计数一条不少）。
    $noteGroups = New-Object System.Collections.ArrayList
    $noteIndex = @{}
    foreach ($n in $script:DiscNotes) {
        $kind = [string]$n
        $example = ''
        if ($n -match '^深度上限 (\d+) 层已达 ⇒ 未下钻：(.+?)（其下 \d+ 个子目录未访问）$') {
            $kind = ('深度上限 ' + $Matches[1] + ' 层已达 ⇒ 未下钻（该目录还有子目录，未访问）')
            $example = $Matches[2]
        } elseif ($n -match '^(跳过目录联接/符号链接（不跟随）)：(.+)$') {
            $kind = $Matches[1]
            $example = $Matches[2]
        }
        if ($noteIndex.ContainsKey($kind)) {
            $g = $noteGroups[$noteIndex[$kind]]
            $g.Count++
            if ($example -and $g.Examples.Count -lt 3) { [void]$g.Examples.Add($example) }
        } else {
            $g = @{ Kind = $kind; Count = 1; Examples = (New-Object System.Collections.ArrayList) }
            if ($example) { [void]$g.Examples.Add($example) }
            [void]$noteGroups.Add($g)
            $noteIndex[$kind] = $noteGroups.Count - 1
        }
    }
    [void]$s12.AppendLine(('  ⚠ 因上限或失败而跳过/未覆盖的项，共 {0} 条记录、归并为 {1} 类（每类给条数 + 前 3 个示例）：' -f $script:DiscNotes.Count, $noteGroups.Count))
    $gk = 0
    foreach ($g in $noteGroups) {
        if ($gk -ge 40) { [void]$s12.AppendLine(('    …另有 {0} 类未列出' -f ($noteGroups.Count - 40))); break }
        $gk++
        [void]$s12.AppendLine(('    - [{0} 条] {1}' -f $g.Count, $g.Kind))
        foreach ($ex in $g.Examples) { [void]$s12.AppendLine(('        例：' + $ex)) }
        if ($g.Count -gt $g.Examples.Count) { [void]$s12.AppendLine(('        其余 {0} 条同类（内容同型，已计数、不再刷屏）' -f ($g.Count - $g.Examples.Count))) }
    }
}
Write-Utf8 -Path (Join-Path $OutDir '12-GCU日志可发现.txt') -Text $s12.ToString()

# ---- 12b 关键行（贴原文行；⚠ 只扫"GCU 相关"那一批，避免被无关第三方日志把 300 行额度吃光）----
$discKeyLines = New-Object System.Collections.ArrayList
$discKeyTotal = 0
$discKeyDropped = 0
$discKeyScanned = 0
$discKeyScanList = $tailTargets
foreach ($f in $discKeyScanList) {
    try {
        $ln = @(Get-TailLines -Path $f.FullName -MaxLines 4000)
        $discKeyScanned++
        $sel = Select-Lines -Lines $ln -Pattern $script:DiscKeyPattern -Max $script:DiscKeyPerFile -Keep 'tail'
        if ($sel.Total -gt 0) {
            [void]$discKeyLines.Add(('### ' + $f.FullName + (' （扫描尾部 {0} 行，命中 {1} 行{2}）' -f $ln.Count, $sel.Total, $(if ($sel.Truncated) { ('，已按【尾部】只留最新 ' + $script:DiscKeyPerFile + ' 行（丢弃更早 ' + $sel.Dropped + ' 行）') } else { '，未截断' }))))
            foreach ($x in $sel.Lines) {
                if ($discKeyTotal -ge $script:DiscKeyAllTotal) { $discKeyDropped++; continue }
                [void]$discKeyLines.Add($x)
                $discKeyTotal++
            }
        }
    } catch {
        Warn ('12 抽取关键行失败 ' + $f.FullName + '：' + $_.Exception.Message)
    }
}
$s12b = New-Object System.Text.StringBuilder
[void]$s12b.AppendLine('12b GCU 日志-关键行（"可发现"范围内近 24h 的日志；贴原文行）')
[void]$s12b.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s12b.AppendLine(('关键词：' + $script:DiscKeyPattern))
[void]$s12b.AppendLine(('范围：与 12 节"近 24h 日志尾部"同一批（GCU 相关 {0} 个优先；若一个都没有则退取最新 3 个非 GCU 日志）；本文件里实际扫了 {1} 个，每个最多扫尾部 4000 行' -f $recentRel.Count, $discKeyScanned))
[void]$s12b.AppendLine(('上限：每文件关键行 ≤{0} 行，合计 ≤{1} 行' -f $script:DiscKeyPerFile, $script:DiscKeyAllTotal))
[void]$s12b.AppendLine('')
if ($discKeyTotal -eq 0 -and $discKeyLines.Count -eq 0) {
    [void]$s12b.AppendLine('  （没有任何命中行 —— 可能是：近 24h 没有日志 / 日志里确实没有这些关键词 / 每个日志都读取失败（原因见 12））')
} else {
    foreach ($x in $discKeyLines) { [void]$s12b.AppendLine($x) }
}
if ($discKeyDropped -gt 0) {
    [void]$s12b.AppendLine('')
    [void]$s12b.AppendLine(('  ⚠ 因"合计 {0} 行"上限而丢弃了 {1} 行命中（保留的是靠前的那些文件；要看更全请直接看 12 里对应日志的尾部）' -f $script:DiscKeyAllTotal, $discKeyDropped))
}
Write-Utf8 -Path (Join-Path $OutDir '12b-GCU日志可发现-关键行.txt') -Text $s12b.ToString()
$P.LogDiscKeyLines = $discKeyTotal

# ================================================================ 13 KeyboardManager 两文件
# 2026-10-09（方案B）：内置诊断新增的两个"灯效解"强候选小文件，独立脚本里没有 —— 这里补上（⚠ 全文贴 + mtime）。
# ⚠ 只读：只读这两个 json，绝不写。
Step '13 KeyboardManager 两文件（全文 + mtime，只读）' | Out-Null

function Get-FileTextShared {
    # 以"允许别人同时写"的方式读全文（底座/GCU 可能正在写这两个小文件），带字符上限
    param([string]$Path, [int]$MaxChars = 4096)
    try {
        $lines = @(Read-LinesShared -Path $Path -MaxLines 4000 -Tail $false)
        $t = ($lines -join "`r`n")
        if ($t.Length -gt $MaxChars) { $t = $t.Substring(0, $MaxChars) + ("`r`n…（超过 " + $MaxChars + " 字符上限，已截断）") }
        return $t
    } catch {
        return ('(读取失败：' + $_.Exception.Message + ')')
    }
}

$kmInstUtc = ''
try { $kmInstUtc = [string](Get-RegValueByName -Res $regLm -Name 'GcuInstalledUtc') } catch { }
$kmInstTime = $null
if ($kmInstUtc) { try { $kmInstTime = ([datetime]::Parse($kmInstUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)).ToUniversalTime() } catch { $kmInstTime = $null } }

$s13 = New-Object System.Text.StringBuilder
[void]$s13.AppendLine('13 GCU 键盘灯效文件（KeyboardManager 两文件；⚠ 只读 + 贴全文，有 4096 字符/文件上限）')
[void]$s13.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s13.AppendLine(('MyControlCenter 采用：' + $(if ($mcc) { $mcc } else { '(未实测到)' }) + '；底座根：' + $baseRootGuess))
[void]$s13.AppendLine(('安装时间（注册表 GcuInstalledUtc）：' + $(if ($kmInstUtc) { $kmInstUtc + '（UTC）' } else { '(注册表无该值 ⇒ 无法与 mtime 比"是否被运行期改写")' })))
[void]$s13.AppendLine('说明：这两个文件都很小，是"30 系灯效解"的强候选；⭐ mtime 晚于安装时间 ⇒ 是被 GCU【运行期改写】过的信号。')
[void]$s13.AppendLine('')
$kmFoundCount = 0
foreach ($rel in @('MyControlCenter\AppSettings\KeyboardManager.json', 'MyControlCenter\KeyboardManager\settings.json')) {
    $leaf = Split-Path -Leaf $rel
    $sub = Split-Path -Parent $rel
    $inner = $sub -replace '^MyControlCenter\\?', ''
    $innerPath = $leaf
    if ($inner) { $innerPath = Join-Path $inner $leaf }
    $cands = New-Object System.Collections.ArrayList
    # ⚠ 候选路径必须拼到【文件】这一层：$mcc 本身就是 MyControlCenter，不能只拼到子目录。
    if ($mcc) { [void]$cands.Add((Join-Path $mcc $innerPath)) }
    [void]$cands.Add((Join-Path $baseRootGuess $rel))
    $up13 = ''
    try { $up13 = Split-Path -Parent $baseRootGuess.TrimEnd('\') } catch { }
    if ($up13) { [void]$cands.Add((Join-Path $up13 ('AiStoneService\' + $rel))) }
    # 再兜一条：安装根（%ProgramFiles%\L-Mechrevo）下按相对路径找（有些布局底座根没有 AiStoneService 这一层）
    try { [void]$cands.Add((Join-Path (Join-Path $env:ProgramFiles 'L-Mechrevo') ('GCU\' + $rel))) } catch { }
    $hit13 = ''
    foreach ($c in ($cands | Select-Object -Unique)) {
        try { if ($c -and (Test-Path -LiteralPath $c)) { $hit13 = $c; break } } catch { }
    }
    [void]$s13.AppendLine(('== ' + $rel + ' =='))
    if (-not $hit13) {
        [void]$s13.AppendLine('  不存在')
        [void]$s13.AppendLine('  （试过的路径，逐个都没命中：）')
        foreach ($c in ($cands | Select-Object -Unique)) { [void]$s13.AppendLine('    - ' + $c) }
        [void]$s13.AppendLine('')
        continue
    }
    $kmFoundCount++
    [void]$s13.AppendLine(('  实际路径：' + $hit13))
    try {
        $fi13 = Get-Item -LiteralPath $hit13 -ErrorAction Stop
        [void]$s13.AppendLine(('  大小：{0} 字节   mtime：{1}（本地时间）' -f $fi13.Length, $fi13.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
        $verdict13 = '无法判定（注册表没有 GcuInstalledUtc）'
        if ($kmInstTime) {
            if ($fi13.LastWriteTimeUtc -gt $kmInstTime.AddMinutes(2)) {
                $verdict13 = ('是 ⇒ mtime 晚于安装时间 {0:yyyy-MM-dd HH:mm:ss}（UTC），说明装完之后被 GCU【运行期改写】过' -f $kmInstTime)
            } else {
                $verdict13 = ('否（mtime 不晚于安装时间 {0:yyyy-MM-dd HH:mm:ss} UTC）⇒ 与安装时一致，未见运行期改写' -f $kmInstTime)
            }
        }
        [void]$s13.AppendLine(('  ★ 是否被运行期改写：' + $verdict13))
    } catch {
        [void]$s13.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
    }
    [void]$s13.AppendLine('  --- 全文（只读；上限 4096 字符）---')
    [void]$s13.AppendLine((Get-FileTextShared -Path $hit13 -MaxChars 4096))
    [void]$s13.AppendLine('')
}
$P.KmState = ('命中 ' + $kmFoundCount + ' / 2')
Write-Utf8 -Path (Join-Path $OutDir '13-KeyboardManager.txt') -Text $s13.ToString()

# ================================================================ 14 载荷落地核对（按变体）
# ⚠ 判定口径：用【完整 64 位】清单哈希比对（值内嵌在脚本里）；
#   报告里按本脚本既有口径只印"SHA256 前 16 位" —— 因为最后的打码环节会把 32+ 位连续十六进制截成
#   "前16位 + <已打码>"，印全长反而会把值打坏、还会误导。
Step '14 载荷落地核对（GcuPayloadSha256 按变体，只读）' | Out-Null

$script:PayloadVariants = @(
    [pscustomobject]@{ Id = 'base'; Sha = 'c5197181079a8bc5f2a6c2eb7ece6d524aa45a88976005c9a67130d405af6754'; Entries = 87; Files = 97; Note = 'base = payload\GCU（2026-10-10 起为 50old 世代：Uniwill 完整桥 2,125,496 B + GCUUtil 30,888 B，无 GCUService/UEFI_Firmware/SQLite/rtknrai；iss [Files] 97 行 / 磁盘 97 文件；清单 87 条目 = 97 减去被排除的 readme/notice/license/.bat 类叶子名）'; Checks = @(
            @{ Rel = 'GCUBridge.exe'; Size = 2125496; Signer = 'AISTONE' },
            @{ Rel = 'MyControlCenter\OSDTpDetect.exe'; Size = 522480; Signer = 'AISTONE' },
            @{ Rel = 'MyControlCenter\GCUUtil.exe'; Size = 30888; Signer = 'AISTONE' }) },
    [pscustomobject]@{ Id = '40';   Sha = '41c3b8f326e72265ccbc9d3a0de4ed5d1b5eeda9cab9ba2b31f4ac14cbeafae1'; Entries = 90; Files = 4;  Note = '40 覆盖层 iss 4 行 / 磁盘 4 文件（2026-10-10：base 换成 50old 后去掉 AiStoneService\GCUBridge.exe 条目）'; Checks = @() },
    [pscustomobject]@{ Id = '40B';  Sha = '1afe15ba3822708e691db66c63e490b881faa00c158d427bb879303df22006bb'; Entries = 90; Files = 8;  Note = '40B 覆盖层 8 文件（2026-10-10：UWAirplane/UWDiskInfo 改由 base 50old 提供，条目 10→8）'; Checks = @(
            @{ Rel = 'GCUBridge.exe'; Size = 2122248; Signer = 'Uniwill' },
            @{ Rel = 'MyControlCenter\ACPIDriverDll.dll'; Size = 4113416; Signer = 'Uniwill' },
            @{ Rel = 'MyControlCenter\GPUInfoDLL.dll'; Size = 2578440; Signer = 'Uniwill' }) },
    [pscustomobject]@{ Id = '50';   Sha = 'e314d751c992a188db6a2c665031e873fe4ef3395a21a279a96c91ede49b3bae'; Entries = 93; Files = 9;   Note = '50 覆盖层 9 文件（2026-10-10：GCUBridge/UEFI_Firmware/OSDTpDetect 三件补入 iss；同批由另一会话代理补入 SQLiteDB\GamdeMode.db 20,480 B；磁盘另有 -shm/-wal 两个 SQLite 运行期临时件，故意不入清单）'; Checks = @(
            @{ Rel = 'MyControlCenter\GCUService.exe'; Size = 17110576; Signer = 'AISTONE' }) },
    [pscustomobject]@{ Id = '30';   Sha = '664011cabf99266500a7edadeac64f8389b36f02d1e33cb762e39c7e751960af'; Entries = 232; Files = 163; Note = '30 覆盖层 163 文件（base+30 合并随 50old 底座换代而变）'; Checks = @(
            @{ Rel = 'MyControlCenter\ACPIDriverDll.dll'; Size = 4113416; Signer = 'Uniwill' }) }
)
# ⚠ 旧值：安装器身份口径变更/补件/底座换代之前的历史清单哈希，只作"旧值"标注 —— 命中它也说明"装的是旧载荷批次"，不是未知值。
$script:PayloadOldValues = @(
    [pscustomobject]@{ Sha = '581f4d46f2020bed213d2957a6ab54cba528f5edd97014203752fb9a0338cedb'; Of = '50'; Why = '2026-10-10 补入 SQLiteDB\GamdeMode.db 之前的 base+50 合并清单值（91 条目 / 50 覆盖层 6 文件）' },
    [pscustomobject]@{ Sha = '8d5bc85f6528a5c235e29eccdba37b80bc3b76c2884deb66d4c7c1608eec8d49'; Of = 'base'; Why = '2026-10-10 底座换成 50old 世代之前的 base 清单值（50new 世代，237 条目 / 247 文件）' },
    [pscustomobject]@{ Sha = '5a4b5d743c7a53d7650957c927e211da8be54960980ef0804c6f8a5a6af25ca2'; Of = '40'; Why = '底座换代前的 base+40 合并清单值（240 条目 / 40 覆盖层 5 文件）' },
    [pscustomobject]@{ Sha = 'aba3aa75affaed8c3eeae94e555ff929ad49c06d11b10fa2ebc64892ccb3cd67'; Of = '40'; Why = '底座换代前的 40 覆盖层自身清单值（5 文件 / 19,617,248 B）' },
    [pscustomobject]@{ Sha = 'b3b7fbd94365631c98f34fbad7b4c504fc53e4e28cbe484a2793fdf97de9f94c'; Of = '40B'; Why = '底座换代前的 base+40B 合并清单值（240 条目 / 40B 覆盖层 10 文件）' },
    [pscustomobject]@{ Sha = 'f08d297fc864e70eb1b15b5951019dca6cb3cfa42c17a2cd7b1f841079238d6e'; Of = '40B'; Why = '底座换代前的 40B 覆盖层自身清单值（10 文件 / 26,740,424 B）' },
    [pscustomobject]@{ Sha = '73c7980822256c0825f507393eb3593553674c2c8e4545aefed9e9b37e339fb6'; Of = '50'; Why = '底座换代前的 base+50 合并清单值（241 条目 / 50 覆盖层 4 文件）' },
    [pscustomobject]@{ Sha = '292903b217292c8e3bd2dde2ea9fbe28edc8ce5fc08c8b2c88fd7975029053fd'; Of = '50'; Why = '底座换代前的 50 覆盖层自身清单值（4 文件 / 20,079,596 B）' },
    [pscustomobject]@{ Sha = '0fb5b357b056fac3f971c0b487e8701a4bae6a455208b58abdda08e10001b1a2'; Of = '30'; Why = '底座换代前的 base+30 合并清单值（383 条目）' },
    [pscustomobject]@{ Sha = 'a47c07b2b5da3a8c0fe5781daaa814972fd7d2844b23afb98892a67f411c8b63'; Of = '30';  Why = '30 补 ACPIDriverDll.dll（Uniwill）之前的旧清单值' },
    [pscustomobject]@{ Sha = '4c1094a8596af7d14845847140e4c8be30c872e462e4050ce066625688aa997f'; Of = '40'; Why = '40 覆盖层 13 文件（含 README 与 8 个后继改由 base 提供的文件）精简为 5 文件之前的旧清单值，base+40 合并口径' },
    [pscustomobject]@{ Sha = '2634caa0f3870e9d85d08bb9af08cfcb1d8c0c10847d2e87a7ce99c6d86d24d1'; Of = '40'; Why = 'g40full 时期 40 完整独立树 73 文件的清单值（该形态已撤销）' },
    [pscustomobject]@{ Sha = '0588d33794908f0fa7698cac41712e5c082ff7e52f8cb91643c39721fbfcea0d'; Of = '40B'; Why = '40B 补 ACPIDriverDll.dll + GPUInfoDLL.dll 之前的旧清单值' },
    [pscustomobject]@{ Sha = '77979b73924b520e7321cdfc9180cd26c67d2d50e6115af9d343ad4faa594239'; Of = '40B'; Why = '40B 补 GCUBridge.exe（Uniwill）之前的旧清单值' },
    [pscustomobject]@{ Sha = 'ef92fb3af700f74c836321e6f7cc02deae8866e4cd36bb437a567c9a0491154e'; Of = '40B'; Why = '40B 初版（6 文件）的旧清单值' },
    [pscustomobject]@{ Sha = '5512cf35d098c9eedceee3230765f647dd6dd2220c362c68a55a122582ca99ed'; Of = '50';  Why = '50 补 AppSettings\GamingMonitor.json 之前的旧清单值' }
)

function Get-Sha16Lower { param([string]$Sha) if (-not $Sha) { return '' } if ($Sha.Length -ge 16) { return $Sha.Substring(0, 16).ToLower() } return $Sha.ToLower() }

$regVariant = ''
$regSha = ''
try { $regVariant = [string](Get-RegValueByName -Res $regLm -Name 'GcuVariant') } catch { }
try { $regSha = [string](Get-RegValueByName -Res $regLm -Name 'GcuPayloadSha256') } catch { }
if ($regSha) { $regSha = $regSha.Trim().ToLower() }
$matchedVar = ''
$verdict14 = ''
if (-not $regSha) {
    $verdict14 = '无法判定（注册表 HKLM\SOFTWARE\L-Mechrevo 里没有 GcuPayloadSha256 ⇒ 旧版安装器未登记该值）'
} else {
    foreach ($v in $script:PayloadVariants) { if ($v.Sha -eq $regSha) { $matchedVar = $v.Id; break } }
    if ($matchedVar) {
        $verdict14 = ('命中【' + $matchedVar + '】现行清单哈希（' + (Get-Sha16Lower $regSha) + '…）⇒ 该变体的载荷已按当前批次落地')
    } else {
        foreach ($ov in $script:PayloadOldValues) {
            if ($ov.Sha -eq $regSha) { $verdict14 = ('命中【旧值】：' + $ov.Of + ' 的 ' + (Get-Sha16Lower $ov.Sha) + '…（' + $ov.Why + '）⇒ 装的是旧批次载荷'); break }
        }
        if (-not $verdict14) { $verdict14 = ('不在已知集合中（' + (Get-Sha16Lower $regSha) + '…）⇒ 无法判定（可能是新批次/自定义载荷，也可能是被改写）') }
    }
}
$P.PayloadSha = $regSha
$P.PayloadShaVariant = $matchedVar
$P.PayloadShaVerdict = $verdict14

$s14 = New-Object System.Text.StringBuilder
[void]$s14.AppendLine('14 载荷落地核对（按变体；⚠ 只读注册表 + 只读已装文件，不改任何东西）')
[void]$s14.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s14.AppendLine('')
[void]$s14.AppendLine('== 注册表登记（安装器写入的期望值）==')
[void]$s14.AppendLine(('  HKLM\SOFTWARE\L-Mechrevo\GcuVariant        = ' + $(if ($regVariant) { $regVariant } else { '(无该值)' })))
[void]$s14.AppendLine(('  HKLM\SOFTWARE\L-Mechrevo\GcuPayloadSha256  = ' + $(if ($regSha) { (Get-Sha16Lower $regSha) + '…（前16位；完整 64 位已实测并用于比对，此处按既有打码口径只印前16）' } else { '(无该值 ⇒ 旧版安装器未登记)' })))
$regGcuDir = ''
try { $regGcuDir = [string](Get-RegValueByName -Res $regLm -Name 'GcuServiceDir') } catch { }
$regGcuExe = ''
try { $regGcuExe = [string](Get-RegValueByName -Res $regLm -Name 'GcuServiceExe') } catch { }
[void]$s14.AppendLine(('  GcuServiceDir                              = ' + $(if ($regGcuDir) { $regGcuDir } else { '(无该值)' })))
[void]$s14.AppendLine(('  GcuServiceExe                              = ' + $(if ($regGcuExe) { $regGcuExe } else { '(无该值)' })))
[void]$s14.AppendLine('')
[void]$s14.AppendLine('== ★ 期望值集合比对（5 个现行值 + 16 个历史旧值；比对用完整 64 位，显示用前 16 位）==')
[void]$s14.AppendLine('  变体' + "`t" + '前16位' + "`t" + '清单条目数' + "`t" + '覆盖层文件数(参考)' + "`t" + '备注')
foreach ($v in $script:PayloadVariants) {
    $mk = ''
    if ($regSha -and $v.Sha -eq $regSha) { $mk = ' ★本机命中' }
    [void]$s14.AppendLine(('  {0}{1}`t{2}`t{3}`t{4}`t{5}' -f $v.Id, $mk, (Get-Sha16Lower $v.Sha), $v.Entries, $v.Files, $v.Note))
}
[void]$s14.AppendLine('  --- 历史旧值（只作标注，命中它 = 装的是旧批次载荷，不是"未知值"）---')
foreach ($ov in $script:PayloadOldValues) {
    $mk = ''
    if ($regSha -and $ov.Sha -eq $regSha) { $mk = ' ★本机命中' }
    [void]$s14.AppendLine(('  {0} 的旧值{1}`t{2}`t{3}' -f $ov.Of, $mk, (Get-Sha16Lower $ov.Sha), $ov.Why))
}
[void]$s14.AppendLine('')
[void]$s14.AppendLine('  ⚠ 上表"覆盖层文件数"是【安装器侧】的参考值（iss [Files] 行数 / payload 目录文件数），')
[void]$s14.AppendLine('     本脚本在已装机上【无法从装出来的合并树反推覆盖层文件数】（合并树 = base + 覆盖层），')
[void]$s14.AppendLine('     所以这里只做参考展示，不作为判定依据；判定依据只有"清单哈希命中"这一条。')
[void]$s14.AppendLine('')
[void]$s14.AppendLine('== ★ 关键文件抽查（大小 + 签名者；⚠ 查不到就写"无法判定"，不猜）==')
[void]$s14.AppendLine(('  已装布局：底座根 = ' + $baseRootGuess + '；MyControlCenter = ' + $(if ($mcc) { $mcc } else { '(未实测到)' })))
$chkOk = 0
$chkTotal = 0
foreach ($v in $script:PayloadVariants) {
    if (-not $v.Checks -or $v.Checks.Count -eq 0) {
        [void]$s14.AppendLine(('  [' + $v.Id + '] 本变体没有内嵌关键文件期望值 ⇒ 跳过判定（不是故障；判定仍看上面的清单哈希）'))
        continue
    }
    [void]$s14.AppendLine(('  [' + $v.Id + '] 该变体自带的关键文件（期望 大小/签名者）：'))
    foreach ($ck in $v.Checks) {
        $chkTotal++
        $rel14 = [string]$ck.Rel
        $cands14 = New-Object System.Collections.ArrayList
        [void]$cands14.Add((Join-Path $baseRootGuess $rel14))
        $leaf14 = Split-Path -Leaf $rel14
        $sub14 = Split-Path -Parent $rel14
        $inner14 = $sub14 -replace '^MyControlCenter\\?', ''
        $innerPath14 = $leaf14
        if ($inner14) { $innerPath14 = Join-Path $inner14 $leaf14 }
        if ($mcc) { [void]$cands14.Add((Join-Path $mcc $innerPath14)) }
        $up14 = ''
        try { $up14 = Split-Path -Parent $baseRootGuess.TrimEnd('\') } catch { }
        if ($up14) { [void]$cands14.Add((Join-Path $up14 ('AiStoneService\' + $rel14))) }
        $hit14 = ''
        foreach ($c in ($cands14 | Select-Object -Unique)) {
            try { if ($c -and (Test-Path -LiteralPath $c)) { $hit14 = $c; break } } catch { }
        }
        if (-not $hit14) {
            # ⚠ 这里绝不能用单引号串里的 `r`n（单引号不转义，会原样打印出 "`r`n"）—— 用数组 join 拼行。
            $miss14 = @(
                ('    ' + $rel14),
                ('      期望：' + $ck.Size + ' 字节 / 签名者含 "' + $ck.Signer + '"'),
                ('      实测：文件不存在（试过：' + (($cands14 | Select-Object -Unique) -join ' | ') + '）'),
                '      判定：无法判定（缺失）'
            )
            [void]$s14.AppendLine(($miss14 -join "`r`n"))
            continue
        }
        try {
            $fi14 = Get-Item -LiteralPath $hit14 -ErrorAction Stop
            $signer14 = '(未签名/读不到)'
            try {
                $sg14 = Get-AuthenticodeSignature -LiteralPath $hit14 -ErrorAction Stop
                $subj14 = ''
                if ($sg14 -and $sg14.SignerCertificate) { $subj14 = [string]$sg14.SignerCertificate.Subject }
                if ($subj14 -and $subj14 -match 'CN=([^,]+)') { $signer14 = $Matches[1].Trim() } elseif ($subj14) { $signer14 = $subj14 } else { $signer14 = '(无签名证书)' }
                if ($sg14 -and $sg14.Status) { $signer14 += ('（状态 ' + [string]$sg14.Status + '）') }
            } catch { $signer14 = '(签名读取失败)' }
            $sameSize = ([int64]$fi14.Length -eq [int64]$ck.Size)
            $sameSign = ($signer14 -like ('*' + $ck.Signer + '*'))
            $vd14 = ''
            if ($sameSize -and $sameSign) {
                if ($matchedVar -eq $v.Id) { $vd14 = 'OK（与本变体期望一致）'; $chkOk++ }
                elseif ($matchedVar) { $vd14 = '一致（但本机注册表命中的是 ' + $matchedVar + '，不是本变体）' }
                else { $vd14 = '一致（但本机清单哈希无法判定变体）' }
            } else {
                $why14 = New-Object System.Collections.ArrayList
                if (-not $sameSize) { [void]$why14.Add(('大小不符：实测 {0} ≠ 期望 {1}' -f $fi14.Length, $ck.Size)) }
                if (-not $sameSign) { [void]$why14.Add(('签名者不符：实测 "{0}" 不含 "{1}"' -f $signer14, $ck.Signer)) }
                if ($matchedVar -and $matchedVar -ne $v.Id) { $vd14 = '不一致（' + ($why14 -join '；') + '）—— 本机不是该变体，属预期' }
                else { $vd14 = '不一致（' + ($why14 -join '；') + '）' }
            }
            $ok14 = @(
                ('    ' + $rel14),
                ('      路径：' + $hit14),
                ('      期望：' + $ck.Size + ' 字节 / 签名者含 "' + $ck.Signer + '"'),
                ('      实测：' + $fi14.Length + ' 字节 / 签名者 "' + $signer14 + '"'),
                ('      判定：' + $vd14)
            )
            [void]$s14.AppendLine(($ok14 -join "`r`n"))
        } catch {
            $err14 = @(
                ('    ' + $rel14),
                ('      该项失败（' + $_.Exception.Message + '）⇒ 无法判定')
            )
            [void]$s14.AppendLine(($err14 -join "`r`n"))
        }
    }
}
$P.PayloadKeyChecked = $chkTotal
$P.PayloadKeyOk = $chkOk
[void]$s14.AppendLine('')
[void]$s14.AppendLine('== ★ 结论 ==')
[void]$s14.AppendLine(('  注册表 GcuVariant        = ' + $(if ($regVariant) { $regVariant } else { '(无)' }) + '；清单哈希判定 = ' + $verdict14))
if ($matchedVar -and $regVariant -and $regVariant -ne $matchedVar) {
    [void]$s14.AppendLine(('  ⚠ 注意：注册表 GcuVariant="' + $regVariant + '" 与清单哈希命中的变体 "' + $matchedVar + '" 不一致 —— 请两个值都记下来（以清单哈希为准，但变体号不一致本身是线索）。'))
}
[void]$s14.AppendLine(('  关键文件抽查：共 {0} 项，其中与本机命中变体一致的 {1} 项（其余是"别的变体的期望值"，不一致属预期）' -f $chkTotal, $chkOk))
Write-Utf8 -Path (Join-Path $OutDir '14-载荷落地核对.txt') -Text $s14.ToString()

# ================================================================ 15 灯光判据原样导出
# 2026-10-09（方案B）：内置诊断不打"灯光判据指纹原文"，独立脚本里补上。
# ⚠ 只读：注册表只读、LM 日志只读、config.json 只读；不做任何下发、不写 EC/注册表。
Step '15 灯光判据（能力位/GCU 上报/四条 Status 原文行/指纹/结论）' | Out-Null

function Get-JsonRawField {
    # 照 MechrevoHw.LightbarRawField 口径：字段名忽略大小写；缺失或 null/空 ⇒ "-"
    param($Obj, [string]$Name)
    if ($null -eq $Obj) { return '-' }
    try {
        foreach ($p in $Obj.PSObject.Properties) {
            if ($p.Name -ieq $Name) {
                if ($null -eq $p.Value) { return '-' }
                $v = [string]$p.Value
                if ($v.Length -eq 0) { return '-' }
                return $v
            }
        }
    } catch { }
    return '-'
}

function Get-LightbarFingerprintText {
    # 照 MechrevoHw.LightbarFingerprint 口径【逐字】拼：type=..|powerStatus=..|power=..|light=..|brightNess=..
    param($Obj)
    return ('type=' + (Get-JsonRawField -Obj $Obj -Name 'type') +
            '|powerStatus=' + (Get-JsonRawField -Obj $Obj -Name 'powerStatus') +
            '|power=' + (Get-JsonRawField -Obj $Obj -Name 'power') +
            '|light=' + (Get-JsonRawField -Obj $Obj -Name 'light') +
            '|brightNess=' + (Get-JsonRawField -Obj $Obj -Name 'brightNess'))
}

function Get-ConfigKeyValues {
    # 读 config.json，返回匹配 $Pattern 的键（同时匹配"平键"与"model.<机型>.<键>"两种形态）
    param([string]$Path, [string]$Pattern, [int]$MaxChars = 160)
    $out = New-Object System.Collections.ArrayList
    try {
        $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction Stop
        $j = $raw | ConvertFrom-Json -ErrorAction Stop
        foreach ($pr in $j.PSObject.Properties) {
            $k = [string]$pr.Name
            $short = $k
            if ($k -match '^model\..+?\.(.+)$') { $short = $Matches[1] }
            if ($short -notmatch $Pattern) { continue }
            $v = ''
            try {
                if ($null -eq $pr.Value) { $v = '(null)' } else { $v = [string]$pr.Value }
            } catch { $v = '(读取失败)' }
            if ($v.Length -gt $MaxChars) { $v = $v.Substring(0, $MaxChars) + '…' }
            [void]$out.Add(@{ Key = $k; Short = $short; Value = $v })
        }
    } catch { }
    return $out
}

$itPaths15 = @('SOFTWARE\OEM\GamingCenter2\ItemSupport', 'SOFTWARE\OEM\GamingCenter\ItemSupport', 'SOFTWARE\OEM\ControlCenter\ItemSupport')
$rgbPaths15 = @('SOFTWARE\OEM\GamingCenter2\RGBKeyboard', 'SOFTWARE\OEM\GamingCenter\RGBKeyboard', 'SOFTWARE\OEM\ControlCenter\RGBKeyboard')
$supPath15 = 'SOFTWARE\OEM\GamingCenter2\Support'
# 内置 MechrevoDeviceCapabilities 的两张名单（逐字照抄，便于"原样对表"）
$itLightKeys = @('KeyboardSupport', 'RGBKeyboardSupport', 'SingleColorKeyboardSupport', 'KeyboardLightSupport', 'KeyboardBacklightSupport',
                 'KeyboardType', 'RGBKeyboardType', 'KeyboardLightType',
                 'LightbarSupport', 'RGBLightbarSupport',
                 'LogoLightSupport', 'LogoLightbarSupport', 'LightbarLogoSupport', 'HidLightbarLogoSupport', 'RGBLogoLightSupport',
                 'LogoSupport', 'MBLogoSupport', 'MBlogoSupport', 'NewlogoSupport', 'NewLogoSupport',
                 'BatteryLogoSupport', 'IsCommercialPlatform', 'SingleColorKeyboardProjectIDs')
$rgbLightKeys = @('Forcenewlogosupport', 'MBlogoSupport', 'MBALogo', 'ForceLogo', 'ForceBase', 'ForceHinge',
                  'Lightbar_logo_PowerSwitch', 'Lightbar_logo_ACLight', 'Lightbar_logo_DCLight',
                  'PowerSwitch', 'ACLight', 'DCLight')

$s15 = New-Object System.Text.StringBuilder
[void]$s15.AppendLine('15 灯光判据（⚠ 只读；能力位/GCU 上报原文、四条 Status 原文行 + 指纹、结论）')
[void]$s15.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s15.AppendLine('')
[void]$s15.AppendLine('== 15a 能力位：三个 ItemSupport 路径（原样键值；与内置 MechrevoDeviceCapabilities.ItemSupportPaths 同口径）==')
foreach ($p15 in $itPaths15) {
    $r15 = Get-RegValues -Path 'HKLM' -SubKey $p15
    [void]$s15.AppendLine(('  -- HKLM\' + $p15 + ' --'))
    if (-not $r15.Ok) { [void]$s15.AppendLine(('    不存在/读不到（' + $r15.Err + '）')); continue }
    if ($r15.Values.Count -eq 0) { [void]$s15.AppendLine('    （键存在但没有任何值）'); continue }
    foreach ($v15 in ($r15.Values | Sort-Object { $_.Name })) {
        [void]$s15.AppendLine(('    {0} [{1}] = {2}' -f $v15.Name, $v15.Type, $v15.Value))
    }
}
[void]$s15.AppendLine('')
[void]$s15.AppendLine('  -- ★ 灯光相关键逐项点名（找不到就明确写"不存在"，不猜）--')
foreach ($k15 in $itLightKeys) {
    $found15 = $false
    $lines15 = @()
    foreach ($p15 in $itPaths15) {
        $r15 = Get-RegValues -Path 'HKLM' -SubKey $p15
        if (-not $r15.Ok) { continue }
        foreach ($v15 in $r15.Values) {
            if ($v15.Name -ieq $k15) { $found15 = $true; $lines15 += ($p15 + ' → ' + $v15.Value) }
        }
    }
    if ($found15) { [void]$s15.AppendLine(('    ' + $k15 + ' = ' + ($lines15 -join '；'))) }
    else { [void]$s15.AppendLine(('    ' + $k15 + ' = 不存在（三个 ItemSupport 路径都没有这个值）')) }
}
[void]$s15.AppendLine('')
[void]$s15.AppendLine('== 15b 注册表 RGBKeyboard 相关键（三个路径；原样导出 + 点名）==')
foreach ($p15 in $rgbPaths15) {
    $r15 = Get-RegValues -Path 'HKLM' -SubKey $p15
    [void]$s15.AppendLine(('  -- HKLM\' + $p15 + ' --'))
    if (-not $r15.Ok) { [void]$s15.AppendLine(('    不存在/读不到（' + $r15.Err + '）')); continue }
    if ($r15.Values.Count -eq 0) { [void]$s15.AppendLine('    （键存在但没有任何值）'); continue }
    foreach ($v15 in ($r15.Values | Sort-Object { $_.Name })) {
        [void]$s15.AppendLine(('    {0} [{1}] = {2}' -f $v15.Name, $v15.Type, $v15.Value))
    }
}
[void]$s15.AppendLine('')
[void]$s15.AppendLine('  -- ★ 灯光相关键逐项点名（找不到就明确写"不存在"）--')
foreach ($k15 in $rgbLightKeys) {
    $found15 = $false
    $lines15 = @()
    foreach ($p15 in $rgbPaths15) {
        $r15 = Get-RegValues -Path 'HKLM' -SubKey $p15
        if (-not $r15.Ok) { continue }
        foreach ($v15 in $r15.Values) {
            if ($v15.Name -ieq $k15) { $found15 = $true; $lines15 += ($p15 + ' → ' + $v15.Value) }
        }
    }
    if ($found15) { [void]$s15.AppendLine(('    ' + $k15 + ' = ' + ($lines15 -join '；'))) }
    else { [void]$s15.AppendLine(('    ' + $k15 + ' = 不存在（三个 RGBKeyboard 路径都没有这个值）')) }
}
$rSup15 = Get-RegValues -Path 'HKLM' -SubKey $supPath15
[void]$s15.AppendLine('')
[void]$s15.AppendLine(('  -- 旁证 HKLM\' + $supPath15 + '（GCU 写的支持位，含 LightBar / RGBLB / MBALogo）--'))
if (-not $rSup15.Ok) { [void]$s15.AppendLine(('    不存在/读不到（' + $rSup15.Err + '）')) }
else {
    foreach ($v15 in ($rSup15.Values | Sort-Object { $_.Name })) { [void]$s15.AppendLine(('    {0} [{1}] = {2}' -f $v15.Name, $v15.Type, $v15.Value)) }
}
[void]$s15.AppendLine('')

# ---- 15c GCU 上报（只能从 LM 日志的原文行里取；取不到就明确写证据不足）----
$lmLogCands = New-Object System.Collections.ArrayList
[void]$lmLogCands.Add((Join-Path $env:APPDATA 'MechrevoLite\log.txt'))
foreach ($d15 in @((Join-Path $env:ProgramData 'MechrevoLite'), (Join-Path $env:LOCALAPPDATA 'MechrevoLite'))) {
    try {
        foreach ($f15 in @(Get-ChildItem -LiteralPath $d15 -Filter '*.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2)) {
            [void]$lmLogCands.Add($f15.FullName)
        }
    } catch { }
}
$lmLogUsed = New-Object System.Collections.ArrayList
$lmLightLines = New-Object System.Collections.ArrayList
$lmCapsLine = ''
foreach ($lp in ($lmLogCands | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $lp)) { continue }
    [void]$lmLogUsed.Add($lp)
    try {
        $ln15 = @(Get-TailLines -Path $lp -MaxLines 6000)
        $pat15 = 'Resolved caps|LB HidLightbar|LightbarLogoSupport|LightbarBaseSupport|LightbarNewLogoSupport|LightbarMbLogoSupport|LightbarHingeSupport|SyncLight|lightmerge'
        $sel15 = Select-Lines -Lines $ln15 -Pattern $pat15 -Max 120 -Keep 'tail'
        if ($sel15.Total -gt 0) {
            [void]$lmLightLines.Add(('### ' + $lp + (' （扫描尾部 {0} 行，命中 {1} 行{2}）' -f $ln15.Count, $sel15.Total, $(if ($sel15.Truncated) { '，已按尾部只留最新 120 行' } else { '' }))))
            foreach ($x in $sel15.Lines) { [void]$lmLightLines.Add($x) }
        }
        if (-not $lmCapsLine) {
            $m15 = [regex]::Match(($ln15 -join "`n"), 'Resolved caps: kb=\S+ lightbar=\S+ logo=\S+ \(reported logo=\S+ base=\S+ newLogo=\S+ mbLogo=\S+; statusSeen lightbar=\S+ logo=\S+\) hinge=\S+ sync=\S+')
            if ($m15.Success) { $lmCapsLine = $m15.Value }
        }
    } catch { Warn ('15 读 LM 日志失败 ' + $lp + '：' + $_.Exception.Message) }
}
$P.LightLogLines = $lmLightLines.Count

[void]$s15.AppendLine('== 15c GCU 上报（GCU 支持位只能从 LM 日志的原文行取；本脚本不新增任何 MQTT GET/订阅）==')
[void]$s15.AppendLine(('  扫描的 LM 日志：' + $(if ($lmLogUsed.Count -gt 0) { ($lmLogUsed -join '；') } else { '（一个都没找到/都不存在）' })))
[void]$s15.AppendLine('  说明：字段名照内置口径（LightbarLogoSupport / LightbarBaseSupport / LightbarNewLogoSupport /')
[void]$s15.AppendLine('        LightbarMbLogoSupport / LightbarHingeSupport + Sync*）；LM 日志里的对应原文行是')
[void]$s15.AppendLine('        "Resolved caps: ..." 与 "LB <topic>: ..."。日志里没有 ⇒ 明确写证据不足，不猜。')
[void]$s15.AppendLine('')
[void]$s15.AppendLine('  -- ★ GCU 上报的三态（Y=是 / n=否 / -=未收到；取自 LM 日志的 Resolved caps 原文行）--')
if (-not $lmCapsLine) {
    [void]$s15.AppendLine('    （LM 日志里没有 Resolved caps 行 ⇒ 证据不足：无法给出 GCU 上报的三态。')
    [void]$s15.AppendLine('      可能原因：LM 没跑到"能力解析"那一步 / 日志被清或轮转 / log_level=off。）')
} else {
    [void]$s15.AppendLine(('    原文行：' + $lmCapsLine))
    $mm15 = [regex]::Match($lmCapsLine, 'Resolved caps: kb=(\S+) lightbar=(\S+) logo=(\S+) \(reported logo=(\S+) base=(\S+) newLogo=(\S+) mbLogo=(\S+); statusSeen lightbar=(\S+) logo=(\S+)\) hinge=(\S+) sync=(\S+)')
    if ($mm15.Success) {
        [void]$s15.AppendLine(('    解析：kb=' + $mm15.Groups[1].Value + ' lightbar=' + $mm15.Groups[2].Value + ' logo=' + $mm15.Groups[3].Value))
        [void]$s15.AppendLine(('          LightbarLogoSupport=' + $mm15.Groups[4].Value + ' LightbarBaseSupport=' + $mm15.Groups[5].Value + ' LightbarNewLogoSupport=' + $mm15.Groups[6].Value + ' LightbarMbLogoSupport=' + $mm15.Groups[7].Value))
        [void]$s15.AppendLine(('          LightbarStatusSeen=' + $mm15.Groups[8].Value + ' LogoLightStatusSeen=' + $mm15.Groups[9].Value + ' SupportsHingeLight=' + $mm15.Groups[10].Value + ' SupportsSyncLight=' + $mm15.Groups[11].Value))
    } else {
        [void]$s15.AppendLine('    ⚠ 该行存在但格式与预期不符（可能版本不同）⇒ 只贴原文，不做解析。')
    }
}
[void]$s15.AppendLine('')
[void]$s15.AppendLine('  -- LM 日志里所有灯光/能力相关原文行（最多 120 行，尾部优先）--')
if ($lmLightLines.Count -eq 0) { [void]$s15.AppendLine('    （无命中：LM 日志里没有这些关键词的行 ⇒ 证据不足）') }
else { foreach ($x in $lmLightLines) { [void]$s15.AppendLine('    ' + $x) } }
[void]$s15.AppendLine('')

# ---- 15d 四条 Status 原文行 + 指纹（照 LightbarFingerprint 逐字口径）----
[void]$s15.AppendLine('== 15d 四条灯条 Status 原文行 + 指纹（照 MechrevoHw.LightbarFingerprint 逐字口径）==')
[void]$s15.AppendLine('  口径（逐字）：type=<type>|powerStatus=<powerStatus>|power=<power>|light=<light>|brightNess=<brightNess>；')
[void]$s15.AppendLine('  字段缺失/为 null/为空 ⇒ 该段取 "-"（与 LightbarRawField 一致）。')
[void]$s15.AppendLine('  证据来源：LM 日志的 "LB <topic>: ..." 行；若该行带 raw={...} 原文 JSON，可逐字还原完整指纹；')
[void]$s15.AppendLine('  若只有摘要行（type=/power=/light=），则 power 与 brightNess 两个字段【取不到】⇒ 明确写证据不足，不编值。')
[void]$s15.AppendLine('')
$fpStore = @{}
foreach ($topic15 in @('HidLightbar/Status', 'HidLightbar_Logo/Status', 'HidLightbar_Hinge/Status', 'HidLightbar_Sync/Status')) {
    [void]$s15.AppendLine(('  -- ' + $topic15 + ' --'))
    $lastLine = ''
    $lastJson = ''
    foreach ($lp in ($lmLogUsed | Select-Object -Unique)) {
        try {
            $ln15 = @(Get-TailLines -Path $lp -MaxLines 6000)
            foreach ($l15 in $ln15) {
                if ($l15 -notmatch ('LB\s+' + [regex]::Escape($topic15) + ':')) { continue }
                $lastLine = $l15
                $i1 = $l15.IndexOf('{')
                $i2 = $l15.LastIndexOf('}')
                if ($i1 -ge 0 -and $i2 -gt $i1) { $lastJson = $l15.Substring($i1, $i2 - $i1 + 1) }
            }
        } catch { }
    }
    if (-not $lastLine) {
        [void]$s15.AppendLine('    原文行：未命中（LM 日志里没有该主题的行）')
        [void]$s15.AppendLine('    指纹  ：无法判定（证据不足：没有报文行）—— 不编值')
        $fpStore[$topic15] = ''
        [void]$s15.AppendLine('')
        continue
    }
    [void]$s15.AppendLine(('    原文行：' + $lastLine))
    $fp15 = ''
    if ($lastJson) {
        $obj15 = $null
        try { $obj15 = $lastJson | ConvertFrom-Json -ErrorAction Stop } catch { $obj15 = $null }
        if ($null -ne $obj15) {
            $fp15 = Get-LightbarFingerprintText -Obj $obj15
            [void]$s15.AppendLine(('    raw JSON 原文：' + $lastJson))
            [void]$s15.AppendLine(('    ★ 指纹（逐字，照 LightbarFingerprint 口径）：' + $fp15))
        } else {
            [void]$s15.AppendLine('    ⚠ 这行里的 {...} 解析失败 ⇒ 指纹无法逐字还原（证据不足）')
        }
    } else {
        $mp15 = [regex]::Match($lastLine, 'type=(\S*) power=(\S*) light=(\S*)')
        if ($mp15.Success) {
            [void]$s15.AppendLine('    ⚠ 该行是摘要行（不是 raw JSON）⇒ 拿不到 power / brightNess 两个字段的独立取值，')
            [void]$s15.AppendLine('       因此【无法逐字还原完整指纹】（证据不足）。可还原的三段如下（其余标"证据不足"）：')
            [void]$s15.AppendLine(('       type=' + $mp15.Groups[1].Value + '|powerStatus=' + $mp15.Groups[2].Value + '|power=（证据不足）|light=' + $mp15.Groups[3].Value + '|brightNess=（证据不足）'))
            [void]$s15.AppendLine('       ⚠ 注意：LM 这行里的 light= 实际打的是报文里 brightNess 的值（源码如此），所以上面把它放在 light 段、而不是 brightNess 段。')
        } else {
            [void]$s15.AppendLine('    指纹  ：无法判定（这行既不是 raw JSON，也不符合摘要行格式 ⇒ 证据不足）')
        }
    }
    $fpStore[$topic15] = $fp15
    [void]$s15.AppendLine('')
}

# ---- 15e 用户设置里的灯光键（config.json；平键与 model.<机型>. 形态都覆盖）----
[void]$s15.AppendLine('== 15e 用户设置里的灯光键（LM config.json，只读）==')
$cfgPaths15 = New-Object System.Collections.ArrayList
foreach ($cp in @((Join-Path $env:ProgramData 'MechrevoLite\config.json'), (Join-Path $env:APPDATA 'MechrevoLite\config.json'), (Join-Path $env:LOCALAPPDATA 'MechrevoLite\config.json'))) {
    if ($cp -and (Test-Path -LiteralPath $cp)) { [void]$cfgPaths15.Add($cp) }
}
if ($cfgPaths15.Count -eq 0) {
    [void]$s15.AppendLine('  （找不到 LM 的 config.json ⇒ 用户设置里的灯光键无法判定；试过：%ProgramData%/%APPDATA%/%LOCALAPPDATA%\MechrevoLite\config.json）')
} else {
    foreach ($cp in $cfgPaths15) {
        [void]$s15.AppendLine(('  -- ' + $cp + ' --'))
        $kv15 = @(Get-ConfigKeyValues -Path $cp -Pattern '^(light_|lighting_|light_strip_ui|light_logo_ui|light_extra_ui)')
        if ($kv15.Count -eq 0) { [void]$s15.AppendLine('    （没有 light_* / lighting_* 键）') }
        else { foreach ($e15 in $kv15) { [void]$s15.AppendLine(('    {0} = {1}' -f $e15.Key, $e15.Value)) } }
        foreach ($need15 in @('light_extra_ui', 'light_strip_ui', 'light_logo_ui', 'light_extra_ui_v2')) {
            $has15 = @($kv15 | Where-Object { $_.Short -ieq $need15 }).Count -gt 0
            if (-not $has15) { [void]$s15.AppendLine(('    ' + $need15 + ' = 不存在（该键在这次配置里没有出现）')) }
        }
    }
}
[void]$s15.AppendLine('')

# ---- 15f 结论：灯带行是否幻影 / Logo 行是否有依据（与内置判据同口径）----
[void]$s15.AppendLine('== ★ 15f 结论（按内置 LightExtraRows 同口径）==')
[void]$s15.AppendLine('  口径（逐字照内置）：灯带行是幻影 ⟺ LightbarBaseSupport==false 且 LightbarMbLogoSupport!=true')
[void]$s15.AppendLine('                        且（LightbarNewLogoSupport==true 或 LightbarLogoSupport==true）。')
[void]$s15.AppendLine('  Logo 行有依据 ⟺ 任一正向信号成立：MbLogo=是 / Logo=是 / NewLogo=是 / 能力位含 LogoLight')
[void]$s15.AppendLine('                        / 收到过 HidLightbar_Logo/Status 的实值报文 / 两主题指纹曾经分歧过 / 两指纹都在且不同。')
$repLogo15 = ''; $repBase15 = ''; $repNew15 = ''; $repMb15 = ''
$seenLb15 = ''; $seenLogo15 = ''; $capLightbar15 = ''; $capLogo15 = ''
$src15 = ''
if ($lmCapsLine) {
    $mm15b = [regex]::Match($lmCapsLine, 'Resolved caps: kb=(\S+) lightbar=(\S+) logo=(\S+) \(reported logo=(\S+) base=(\S+) newLogo=(\S+) mbLogo=(\S+); statusSeen lightbar=(\S+) logo=(\S+)\)')
    if ($mm15b.Success) {
        $capLightbar15 = $mm15b.Groups[2].Value; $capLogo15 = $mm15b.Groups[3].Value
        $repLogo15 = $mm15b.Groups[4].Value; $repBase15 = $mm15b.Groups[5].Value; $repNew15 = $mm15b.Groups[6].Value; $repMb15 = $mm15b.Groups[7].Value
        $seenLb15 = $mm15b.Groups[8].Value; $seenLogo15 = $mm15b.Groups[9].Value
        $src15 = 'LM 日志 Resolved caps 原文行'
    }
}
# 能力位侧的 LogoLight（照 MechrevoDeviceCapabilities.LogoLight 名单）
$capLogoFlag = $false
foreach ($p15 in $itPaths15) {
    $r15 = Get-RegValues -Path 'HKLM' -SubKey $p15
    if (-not $r15.Ok) { continue }
    foreach ($v15 in $r15.Values) {
        foreach ($lk in @('LogoLightSupport', 'LogoLightbarSupport', 'LightbarLogoSupport', 'HidLightbarLogoSupport', 'RGBLogoLightSupport', 'LogoSupport', 'MBLogoSupport', 'MBlogoSupport', 'NewlogoSupport', 'NewLogoSupport')) {
            if ($v15.Name -ieq $lk) {
                $vv = [string]$v15.Value
                if ($vv -and $vv -ne '0' -and $vv -notmatch '^(?i)false$') { $capLogoFlag = $true }
            }
        }
    }
}
$fpLb15 = [string]$fpStore['HidLightbar/Status']
$fpLogo15 = [string]$fpStore['HidLightbar_Logo/Status']
$emptyFp15 = 'type=-|powerStatus=-|power=|light=-|brightNess=-'
$fpDiffer15 = ($fpLb15 -and $fpLogo15 -and ($fpLb15 -cne $fpLogo15) -and ($fpLb15 -cne $emptyFp15))
$logoSeenEvidence = ($seenLogo15 -eq 'Y')

$phantom15 = $false
$phantomWhy15 = '证据不足 ⇒ 保持两行都留'
if ($repMb15 -eq 'Y') { $phantomWhy15 = '保护闸：MbLogo=是（分体 MBLogo）⇒ 永不判幻影，两行都真实 ⇒ 保持两行都留' }
elseif ($repBase15 -eq 'Y') { $phantomWhy15 = '保护闸：BaseSupport=是（GCU 明确报有灯带）⇒ 不判幻影 ⇒ 保持两行都留' }
elseif ($repBase15 -eq 'n' -and ($repNew15 -eq 'Y' -or $repLogo15 -eq 'Y')) { $phantom15 = $true; $phantomWhy15 = '三条同时成立（BaseSupport=否 且 MbLogo≠是 且 报过 Logo 版本）⇒ 灯带行是幻影行' }
elseif ($repBase15 -eq '' -and $repNew15 -eq '' -and $repLogo15 -eq '' -and $repMb15 -eq '') { $phantomWhy15 = '各字段皆"未收到/无证据" ⇒ 判据不成立 ⇒ 保持两行都留（零回归）' }
else { $phantomWhy15 = '证据不足（缺"GCU 明确报 BaseSupport=否"或"报过 Logo 版本"）⇒ 判据不成立 ⇒ 保持两行都留' }

$logoSupported15 = $false
$logoWhy15 = ''
if ($repMb15 -eq 'Y') { $logoSupported15 = $true; $logoWhy15 = 'gcu-mblogo-support（MbLogo=是）' }
elseif ($repLogo15 -eq 'Y') { $logoSupported15 = $true; $logoWhy15 = 'gcu-logo-support（LogoSupport=是）' }
elseif ($repNew15 -eq 'Y') { $logoSupported15 = $true; $logoWhy15 = 'gcu-newlogo-support（NewLogo=是）' }
elseif ($capLogoFlag) { $logoSupported15 = $true; $logoWhy15 = 'cap-logo-light（ItemSupport 里的 Logo 能力位为真）' }
elseif ($logoSeenEvidence) { $logoSupported15 = $true; $logoWhy15 = 'logo-status-seen（收到过 HidLightbar_Logo/Status 的实值报文）' }
elseif ($fpDiffer15) { $logoSupported15 = $true; $logoWhy15 = 'fingerprint-differs（两个主题指纹都在且不同）' }
else { $logoWhy15 = '证据不足（没有任何正向信号）⇒ 按"证据不足则保持两行都留"处理：不据此收掉 Logo 行' }

[void]$s15.AppendLine(('  证据来源：' + $(if ($src15) { $src15 } else { '无（LM 日志里没有 Resolved caps 行）' })))
[void]$s15.AppendLine(('  解析到的上报三态：LightbarLogoSupport=' + $(if ($repLogo15) { $repLogo15 } else { '(未收到/无证据)' }) + ' LightbarBaseSupport=' + $(if ($repBase15) { $repBase15 } else { '(未收到/无证据)' }) + ' LightbarNewLogoSupport=' + $(if ($repNew15) { $repNew15 } else { '(未收到/无证据)' }) + ' LightbarMbLogoSupport=' + $(if ($repMb15) { $repMb15 } else { '(未收到/无证据)' })))
[void]$s15.AppendLine(('  能力位侧：lightbar=' + $(if ($capLightbar15) { $capLightbar15 } else { '?' }) + ' logo=' + $(if ($capLogo15) { $capLogo15 } else { '?' }) + ' ItemSupport\Logo 系能力位为真=' + $(if ($capLogoFlag) { '是' } else { '否' })))
[void]$s15.AppendLine(('  两个 Status 指纹：HidLightbar/Status=' + $(if ($fpLb15) { $fpLb15 } else { '(取不到)' }) + '；HidLightbar_Logo/Status=' + $(if ($fpLogo15) { $fpLogo15 } else { '(取不到)' }) + '；两者不同=' + $(if ($fpDiffer15) { '是' } else { '否/无法判定' })))
[void]$s15.AppendLine('')
[void]$s15.AppendLine(('  ★ 灯带行是否为幻影：' + $(if ($phantom15) { '是（幻影）' } else { '否/未判' }) + ' —— ' + $phantomWhy15))
[void]$s15.AppendLine(('  ★ Logo 行是否有依据：' + $(if ($logoSupported15) { '是（有依据，应保留）' } else { '否/未判' }) + ' —— ' + $logoWhy15))
[void]$s15.AppendLine('  ⚠ 证据不足时的口径（与内置一致）：不据此收掉任何行 —— 保持"灯带行 + Logo 行"两行都留（零回归）。')
$P.LightPhantom = $(if ($phantom15) { '是（幻影）' } else { '否/未判 —— ' + $phantomWhy15 })
$P.LightLogoEvidence = $(if ($logoSupported15) { '是（' + $logoWhy15 + '）' } else { '否/未判 —— ' + $logoWhy15 })
Write-Utf8 -Path (Join-Path $OutDir '15-灯光判据.txt') -Text $s15.ToString()

# ================================================================ 16 UserPofiles 清单
# ⚠ 只列清单（名字/大小/mtime），**绝不复制或读取档案内容**（那是 GCU 运行期用户档案，属机主个人数据）。
Step '16 UserPofiles 清单（只列清单，不复制内容）' | Out-Null

$expectedUp = New-Object System.Collections.ArrayList
[void]$expectedUp.Add('MainOption.json')
foreach ($m16 in 1..3) { foreach ($p16 in 1..5) { [void]$expectedUp.Add(('Mode{0}_Profile{1}.json' -f $m16, $p16)) } }
$upCands = New-Object System.Collections.ArrayList
if ($mcc) { [void]$upCands.Add((Join-Path $mcc 'UserPofiles')) }
[void]$upCands.Add((Join-Path $baseRootGuess 'MyControlCenter\UserPofiles'))
[void]$upCands.Add((Join-Path $baseRootGuess 'AiStoneService\MyControlCenter\UserPofiles'))
$upDir = ''
foreach ($c in ($upCands | Select-Object -Unique)) {
    try { if ($c -and (Test-Path -LiteralPath $c)) { $upDir = $c; break } } catch { }
}
$s16 = New-Object System.Text.StringBuilder
[void]$s16.AppendLine('16 GCU 用户档案清单（UserPofiles；⚠ 只列 名字/大小/mtime，**不复制也不读取档案内容**）')
[void]$s16.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s16.AppendLine(('期望应含：MainOption + Mode1..3_Profile1..5（共 {0} 个）' -f $expectedUp.Count))
[void]$s16.AppendLine(('期望清单：' + ($expectedUp -join '、')))
[void]$s16.AppendLine('')
$missing16 = @()
$extra16 = @()
if (-not $upDir) {
    [void]$s16.AppendLine('  目录：不存在（该机器上 GCU 没有释放 UserPofiles，或布局与预期不同）')
    [void]$s16.AppendLine('  试过的路径：')
    foreach ($c in ($upCands | Select-Object -Unique)) { [void]$s16.AppendLine('    - ' + $c + '  → ' + $(if (Test-Path -LiteralPath $c) { '存在' } else { '不存在' })) }
    $missing16 = @($expectedUp)
    $P.UserPofilesState = '目录不存在'
} else {
    $P.UserPofilesState = '存在'
    [void]$s16.AppendLine(('  目录：' + $upDir))
    $files16 = @()
    try { $files16 = @(Get-ChildItem -LiteralPath $upDir -File -Force -ErrorAction SilentlyContinue | Sort-Object Name) } catch { Warn ('16 枚举 UserPofiles 失败：' + $_.Exception.Message) }
    [void]$s16.AppendLine(('  实际文件数：{0}（最多列 60 个）' -f $files16.Count))
    $shown16 = 0
    foreach ($f16 in $files16) {
        if ($shown16 -ge 60) { [void]$s16.AppendLine(('    …另有 {0} 个未列出' -f ($files16.Count - 60))); break }
        $shown16++
        [void]$s16.AppendLine(('    {0,12}  {1}  {2}' -f $f16.Length, $f16.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $f16.Name))
    }
    $names16 = @($files16 | ForEach-Object { $_.Name })
    $missing16 = @($expectedUp | Where-Object { $names16 -notcontains $_ })
    $extra16 = @($names16 | Where-Object { $expectedUp -notcontains $_ })
    [void]$s16.AppendLine('')
    [void]$s16.AppendLine(('  ★ 期望 16 项里【缺】的（{0} 个）：' -f $missing16.Count))
    if ($missing16.Count -eq 0) { [void]$s16.AppendLine('    （无 —— 16 项全在）') } else { foreach ($x in $missing16) { [void]$s16.AppendLine('    - ' + $x) } }
    [void]$s16.AppendLine(('  ★ 期望清单之外的【多】出来的（{0} 个；例如 Mode4_* 或自定义档位档案）：' -f $extra16.Count))
    if ($extra16.Count -eq 0) { [void]$s16.AppendLine('    （无）') } else { foreach ($x in $extra16) { [void]$s16.AppendLine('    - ' + $x) } }
}
$P.UserPofilesMissing = @($missing16).Count
$P.UserPofilesExtra = @($extra16).Count
Write-Utf8 -Path (Join-Path $OutDir '16-UserPofiles清单.txt') -Text $s16.ToString()

# ================================================================ 17 install 日志 + 装后自检状态
Step '17 install 日志与装后自检状态（只读）' | Out-Null

$instSearch = New-Object System.Collections.ArrayList
[void]$instSearch.Add((Join-Path $env:ProgramData 'L-Mechrevo'))
[void]$instSearch.Add((Join-Path $env:ProgramFiles 'L-Mechrevo'))
$instLogs = New-Object System.Collections.ArrayList
foreach ($d17 in $instSearch) {
    if (-not (Test-Path -LiteralPath $d17)) { continue }
    try {
        foreach ($f17 in @(Get-ChildItem -LiteralPath $d17 -Filter 'gcu-install-*.log' -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            [void]$instLogs.Add($f17)
            if ($instLogs.Count -ge 40) { break }
        }
    } catch { Warn ('17 枚举 gcu-install-*.log 失败 ' + $d17 + '：' + $_.Exception.Message) }
}
$instLogsSorted = @($instLogs | Sort-Object LastWriteTime -Descending)
$s17 = New-Object System.Text.StringBuilder
[void]$s17.AppendLine('17 install 日志 + 装后自检状态（⚠ 只读；gcu-install-*.log 给清单 + 尾部，gcu-install-status.json 给全文）')
[void]$s17.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s17.AppendLine(('搜索位置：' + ($instSearch -join '；') + '（各自递归，最多 40 个文件）'))
[void]$s17.AppendLine('')
[void]$s17.AppendLine('== 17a gcu-install-*.log 清单（名字/大小/mtime）==')
if ($instLogsSorted.Count -eq 0) {
    [void]$s17.AppendLine('  不存在（上面这些位置递归后一个 gcu-install-*.log 都没有）')
    $P.InstallLogState = '不存在'
} else {
    $P.InstallLogState = ('存在 ' + $instLogsSorted.Count + ' 个')
    foreach ($f17 in $instLogsSorted) {
        [void]$s17.AppendLine(('  {0,12}  {1}  {2}' -f $f17.Length, $f17.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $f17.FullName))
    }
    [void]$s17.AppendLine('')
    [void]$s17.AppendLine(('== 17b 最近 2 个 gcu-install-*.log 的尾部（每个 ≤300 行且 ≤64KB）=='))
    $n17 = 0
    foreach ($f17 in $instLogsSorted) {
        if ($n17 -ge 2) { [void]$s17.AppendLine(('  ⚠ 只给最近 2 个的尾部，其余 {0} 个未取内容（清单已列全）' -f ($instLogsSorted.Count - 2))); break }
        $dig17 = Invoke-Safe -Label ('17 尾部 ' + $f17.FullName) -Action { Get-LogDigest -Path $f17.FullName -MaxLines 300 -MaxBytes 65536 } -Fallback (('---- ' + $f17.FullName + ' ----') + "`r`n  该项失败")
        [void]$s17.AppendLine($dig17)
        [void]$s17.AppendLine('')
        $n17++
    }
}
[void]$s17.AppendLine('')
[void]$s17.AppendLine('== 17c 官方控制台安装日志（旁证，只列清单不贴内容）==')
$ccInst17 = Join-Path $env:ProgramData 'ControlCenter\installLog\install.log'
if (Test-Path -LiteralPath $ccInst17) {
    try {
        $fi17 = Get-Item -LiteralPath $ccInst17 -ErrorAction Stop
        [void]$s17.AppendLine(('  {0} 字节  mtime={1}  {2}' -f $fi17.Length, $fi17.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $ccInst17))
    } catch { [void]$s17.AppendLine('  该项失败（' + $_.Exception.Message + '）') }
} else {
    [void]$s17.AppendLine(('  不存在（' + $ccInst17 + '）'))
}
[void]$s17.AppendLine('')
[void]$s17.AppendLine('== 17d gcu-install-status.json（装后自检产物；全文）==')
$statusPath17 = Join-Path $env:ProgramData 'L-Mechrevo\gcu-install-status.json'
[void]$s17.AppendLine(('  期望路径：' + $statusPath17 + '（与 05 同源；这里给全文，字段解释见 05）'))
if (-not (Test-Path -LiteralPath $statusPath17)) {
    [void]$s17.AppendLine('  不存在')
    [void]$s17.AppendLine('  说明：该文件只有较新的安装器（已接入装后自检的那版）才写；不存在 ⇒ 可能只是旧版安装器，不必然是安装失败。')
} else {
    try {
        $fi17b = Get-Item -LiteralPath $statusPath17 -ErrorAction Stop
        [void]$s17.AppendLine(('  大小：{0} 字节   mtime：{1}' -f $fi17b.Length, $fi17b.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
        $raw17 = Get-Content -LiteralPath $statusPath17 -Raw -ErrorAction Stop
        [void]$s17.AppendLine('  --- 全文 ---')
        [void]$s17.AppendLine((Get-Text -Text $raw17 -MaxLines 400))
    } catch {
        [void]$s17.AppendLine(('  该项失败（{0}）' -f $_.Exception.Message))
    }
}
Write-Utf8 -Path (Join-Path $OutDir '17-安装日志与装后自检.txt') -Text $s17.ToString()

# ================================================================ 18 模式/风扇配置快照
Step '18 模式与风扇配置快照（只读）' | Out-Null

$s18 = New-Object System.Text.StringBuilder
[void]$s18.AppendLine('18 模式与风扇配置快照（⚠ 只读：config.json + 注册表 + 风扇表目录，不做任何写入/下发）')
[void]$s18.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s18.AppendLine('')
[void]$s18.AppendLine('== 18a config.json 里的档位/风扇键（perf_* / performance_* / fan* / 档位相关；含 model.<机型>. 形态）==')
$cfgPaths18 = New-Object System.Collections.ArrayList
foreach ($cp in @((Join-Path $env:ProgramData 'MechrevoLite\config.json'), (Join-Path $env:APPDATA 'MechrevoLite\config.json'), (Join-Path $env:LOCALAPPDATA 'MechrevoLite\config.json'))) {
    if ($cp -and (Test-Path -LiteralPath $cp)) { [void]$cfgPaths18.Add($cp) }
}
$perfKeyTotal = 0
if ($cfgPaths18.Count -eq 0) {
    [void]$s18.AppendLine('  （找不到 LM 的 config.json ⇒ 无法判定）')
} else {
    foreach ($cp in $cfgPaths18) {
        [void]$s18.AppendLine(('  -- ' + $cp + ' --'))
        $kv18 = @(Get-ConfigKeyValues -Path $cp -Pattern '^(perf_|performance_|fan|charge_limit|gpu_mode|calib_|screen_max|max_frequency|frequency|overdrive|igpu_only)')
        $perfKeyTotal += $kv18.Count
        if ($kv18.Count -eq 0) { [void]$s18.AppendLine('    （没有匹配的键）'); continue }
        $shown18 = 0
        foreach ($e18 in $kv18) {
            if ($shown18 -ge 120) { [void]$s18.AppendLine(('    …另有 {0} 个未列出（上限 120 个，同内置 MaxPerfKeys 口径）' -f ($kv18.Count - 120))); break }
            $shown18++
            [void]$s18.AppendLine(('    {0} = {1}' -f $e18.Key, $e18.Value))
        }
        foreach ($need18 in @('perf_active', 'perf_customs', 'perf_last_custom', 'perf_schema')) {
            $has18 = @($kv18 | Where-Object { $_.Short -ieq $need18 }).Count -gt 0
            if (-not $has18) { [void]$s18.AppendLine(('    ' + $need18 + ' = 不存在')) }
        }
        $kvFan18 = @($kv18 | Where-Object { $_.Short -match '(?i)fancpu|fangpu' })
        if ($kvFan18.Count -eq 0) { [void]$s18.AppendLine('    ⚠ perf_*_fancpu / perf_*_fangpu = 不存在（该配置文件里没有风扇曲线键）') }
        else { foreach ($e18 in $kvFan18) { [void]$s18.AppendLine(('    ★ 风扇曲线键：{0} = {1}' -f $e18.Key, $e18.Value)) } }
    }
}
$P.PerfKeyCount = $perfKeyTotal
try {
    $activeVals = @($cfgPaths18 | ForEach-Object { Get-ConfigKeyValues -Path $_ -Pattern '^(perf_active|performance_[0-9])' } | ForEach-Object { $_ })
    # ⚠ %ProgramData% 与 %APPDATA% 的 config.json 内容常常完全一样 ⇒ 去重，避免同一把键在摘要里印两遍。
    $P.PerfActive = ((@($activeVals | ForEach-Object { $_.Key + '=' + $_.Value }) | Select-Object -Unique) -join '；')
} catch { $P.PerfActive = '' }
[void]$s18.AppendLine('')
[void]$s18.AppendLine('== 18b 风扇表相关注册表（只读）==')
foreach ($rk18 in @('SOFTWARE\OEM\GamingCenter2\MyFanTable', 'SOFTWARE\OEM\GamingCenter2\Support', 'SOFTWARE\OEM\GamingCenter2\MySetting')) {
    $r18 = Get-RegValues -Path 'HKLM' -SubKey $rk18
    [void]$s18.AppendLine(('  -- HKLM\' + $rk18 + ' --'))
    if (-not $r18.Ok) { [void]$s18.AppendLine(('    不存在/读不到（' + $r18.Err + '）')); continue }
    if ($r18.Values.Count -eq 0) { [void]$s18.AppendLine('    （键存在但没有任何值）'); continue }
    foreach ($v18 in ($r18.Values | Sort-Object { $_.Name })) {
        $val18 = [string]$v18.Value
        if ($val18.Length -gt 160) { $val18 = $val18.Substring(0, 160) + '…' }
        [void]$s18.AppendLine(('    {0} [{1}] = {2}' -f $v18.Name, $v18.Type, $val18))
    }
}
[void]$s18.AppendLine('')
[void]$s18.AppendLine('== 18c 风扇表来源（UserFanTables 目录；只列名字/大小/mtime，最多列 40 项）==')
$uft18 = ''
foreach ($c18 in @((Join-Path $mcc 'UserFanTables'), (Join-Path $baseRootGuess 'MyControlCenter\UserFanTables'), (Join-Path $baseRootGuess 'AiStoneService\MyControlCenter\UserFanTables'))) {
    try { if ($c18 -and (Test-Path -LiteralPath $c18)) { $uft18 = $c18; break } } catch { }
}
if (-not $uft18) {
    [void]$s18.AppendLine('  目录不存在 ⇒ 风扇表来源无法判定（试过：' + ((@((Join-Path $mcc 'UserFanTables')) + (Join-Path $baseRootGuess 'MyControlCenter\UserFanTables')) -join '；') + '）')
} else {
    [void]$s18.AppendLine(('  目录：' + $uft18))
    $top18 = @()
    try { $top18 = @(Get-ChildItem -LiteralPath $uft18 -Force -ErrorAction SilentlyContinue | Sort-Object Name) } catch { }
    $dirs18 = @($top18 | Where-Object { $_.PSIsContainer })
    $files18 = @($top18 | Where-Object { -not $_.PSIsContainer })
    [void]$s18.AppendLine(('  顶层：{0} 个子目录 + {1} 个文件' -f $dirs18.Count, $files18.Count))
    $shown18b = 0
    foreach ($e18 in $top18) {
        if ($shown18b -ge 40) { [void]$s18.AppendLine(('    …另有 {0} 项未列出（上限 40）' -f ($top18.Count - 40))); break }
        $shown18b++
        if ($e18.PSIsContainer) {
            $sub18 = 0
            try { $sub18 = @(Get-ChildItem -LiteralPath $e18.FullName -File -Force -ErrorAction SilentlyContinue).Count } catch { }
            [void]$s18.AppendLine(('    [目录] {0}\  （内含 {1} 个文件）' -f $e18.Name, $sub18))
        } else {
            [void]$s18.AppendLine(('    [文件] {0,10} 字节  {1}  {2}' -f $e18.Length, $e18.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $e18.Name))
        }
    }
    [void]$s18.AppendLine('  ⚠ 本机型实际用哪张表，由 EC 机型码 + LM 的 ModelRegistry/FanTableResolver 映射决定；')
    [void]$s18.AppendLine('     本脚本不复刻那张映射表（避免猜错），所以只如实列出目录；"当前生效的表名"若 LM 没写进日志，就写无法判定。')
    $lmTableLine = ''
    foreach ($lp in ($lmLogCands | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $lp)) { continue }
        try {
            $ln18 = @(Get-TailLines -Path $lp -MaxLines 6000)
            $sel18 = Select-Lines -Lines $ln18 -Pattern 'FAN_TableName|TableName|风扇表' -Max 10 -Keep 'tail'
            if ($sel18.Total -gt 0) { $lmTableLine = ($sel18.Lines -join ' || ') }
        } catch { }
    }
    if ($lmTableLine) { [void]$s18.AppendLine(('  LM 日志里的表名线索（原文行）：' + $lmTableLine)) }
    else { [void]$s18.AppendLine('  当前生效的风扇表名：无法判定（LM 日志里没有 TableName/FAN_TableName 行）') }
}
Write-Utf8 -Path (Join-Path $OutDir '18-模式与风扇快照.txt') -Text $s18.ToString()


# ================================================================ 摘要与打码
Step '生成 00-概要.txt + 隐私打码' | Out-Null

# 最可能的方向（保守，不做过度断言；优先用 11 探针的结论）
$dir = '证据不足'
try {
    if ($probeResult.Ran -and $probeResult.ConnAckOk -and $probeResult.AnySubAck) {
        $dir = '指向客户端侧（11 独立探针能拿到 CONNACK 且收到 SUBACK ⇒ 桥侧正常，问题在 LM 客户端自身）'
    } elseif ($probeResult.Ran -and $probeResult.ConnAckOk -and -not $probeResult.AnySubAck) {
        $dir = '指向桥侧（11 独立探针能与 LM 无关地拿到 CONNACK，却同样收不到 SUBACK ⇒ 桥侧问题）'
    } elseif ($probeResult.Ran -and -not $probeResult.ConnAckOk) {
        $dir = '证据不足（11 探针连 CONNACK 都没成功 ⇒ 先看白名单/clientId 占用与 GCUBridge 是否在听，见 01/02）'
    } else {
        $subSeen   = ($capVerdicts.SUBSCRIBE -like '看到*')
        $subAckSeen = ($capVerdicts.SUBACK -like '看到*')
        if ($capValidCount -le 0) {
            $dir = '证据不足（抓包没有有效报文 ⇒ 四问无法判断；11 探针也没跑成。请先补跑探针）'
        } elseif ($subSeen -and -not $subAckSeen) {
            $dir = '指向桥侧（客户端确实发了 SUBSCRIBE，但抓包里没看到桥侧 SUBACK）'
        } elseif ($subSeen -and $subAckSeen) {
            $dir = '指向客户端侧（桥侧回了 SUBACK，问题更可能在客户端自身状态/后续处理）'
        } elseif ($P.EstabCount -eq 0) {
            $dir = '证据不足（13688 上没有 ESTABLISHED 会话 ⇒ 客户端可能根本没连上）'
        } else {
            $dir = '证据不足（抓包未见 SUBSCRIBE/SUBACK；请结合 02 的 ESTABLISHED 与 11 探针结论）'
        }
    }
} catch { $dir = '证据不足' }
$P.Direction = $dir

$sum = New-Object System.Text.StringBuilder
[void]$sum.AppendLine('00 概要 —— 极光Pro / GCU 连不上 一键诊断抓包')
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine(('脚本版本     : ' + $SCRIPT_VERSION))
[void]$sum.AppendLine(('生成时间     : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
$runCmdParts = New-Object System.Collections.ArrayList
[void]$runCmdParts.Add('powershell -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"')
if ($CaptureSeconds -ne 60) { [void]$runCmdParts.Add('-CaptureSeconds ' + $CaptureSeconds) }
if ($SkipCapture) { [void]$runCmdParts.Add('-SkipCapture') }
if (-not $Zip) { [void]$runCmdParts.Add('-Zip:$false') }
[void]$sum.AppendLine(('运行命令     : ' + ($runCmdParts -join ' ')))
[void]$sum.AppendLine(('输出目录     : ' + $OutDir))
[void]$sum.AppendLine(('是否管理员   : ' + $(if ($isAdmin) { '是' } else { '否（⚠ 抓包需要管理员；请以管理员身份重跑）' }) + $(if ($SelfTestCapture) { '；本次是 -SelfTestCapture 自测模式，未真抓包' } else { '' })))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('-- 机器与系统 --')
[void]$sum.AppendLine(('机器型号     : ' + $(if ($P.Model) { $P.Model } else { '(读取失败)' })))
[void]$sum.AppendLine(('主板         : ' + $(if ($P.BaseBoard) { $P.BaseBoard } else { '(读取失败)' })))
[void]$sum.AppendLine(('BIOS 版本    : ' + $(if ($P.Bios) { $P.Bios } else { '(读取失败)' })))
[void]$sum.AppendLine(('项目号       : ' + $(if ($P.Project) { $P.Project } else { '(未取到)' })))
[void]$sum.AppendLine(('OS           : ' + $(if ($P.OsName) { $P.OsName } else { '(读取失败)' }) + '  build ' + $(if ($P.OsBuild) { $P.OsBuild } else { '?' })))
[void]$sum.AppendLine(('开机时长     : ' + $(if ($P.Uptime) { $P.Uptime } else { '(读取失败)' })))
[void]$sum.AppendLine(('LM 版本      : ' + $(if ($P.LmVersion) { $P.LmVersion } else { '(读取失败)' })))
[void]$sum.AppendLine(('底座根（实测）: ' + $(if ($P.BaseRootFound) { $P.BaseRoot } else { $P.BaseRoot + '（未实测到，按期望路径写）' })))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('-- ★ 结论性摘要（一眼看问题）--')
[void]$sum.AppendLine(('1) 三个就绪键 : ' + $P.ReadyLine))
[void]$sum.AppendLine(('2) 40A 是否落地: ' + $P.Landed40A + '（13 件里 OK=' + $P.Ok40A + '/不一致=' + $P.Mis40A + '/缺失=' + $P.Miss40A + '；' + $(if ($P.BridgeSize -or $P.GmSize) { ('GCUBridge=' + $(if ($P.BridgeSize) { $P.BridgeSize } else { 'N/A' }) + ' 字节，GamingMonitor.json=' + $(if ($P.GmSize) { $P.GmSize } else { 'N/A' }) + ' 字节；内嵌预期 ' + $ExpectedBridgeSize + ' / ' + $ExpectedGmSize) } else { '两者都没读到' }) + '）'))
[void]$sum.AppendLine(('3) 13688 会话 : ESTABLISHED ' + $P.EstabCount + ' 条；LISTEN 持有者=' + $(if ($P.ListenerName) { $P.ListenerName } else { '(未取到)' }) + '；对端=' + $(if ($P.PeerNames) { $P.PeerNames } else { '无' })))
[void]$sum.AppendLine(('   对端 PID（netstat 侧）: ' + $(if ($P.PeerPidList) { $P.PeerPidList } else { '无/未解析到' })))
[void]$sum.AppendLine(('4) 抓包结论   : 方式=' + $P.CapMode + '；时长=' + $P.CapSeconds + ' 秒；含 13688 行=' + $P.CapLines + '；其中有效报文=' + $P.CapValid + $(if ($P.CapValid -le 0) { ' ⚠（有效报文 0 ⇒ 四问一律无法判断，不能当"没订阅"的证据）' } else { '' })))
[void]$sum.AppendLine(('     CONNECT=' + $capVerdicts.CONNECT + ' / CONNACK=' + $capVerdicts.CONNACK + ' / SUBSCRIBE=' + $capVerdicts.SUBSCRIBE + ' / SUBACK=' + $capVerdicts.SUBACK))
[void]$sum.AppendLine(('5) 进程       : GCU 相关进程 ' + $(if ($P.ProcCount) { $P.ProcCount } else { 0 }) + ' 个' + $(if ($P.ProcNames) { '（' + $P.ProcNames + '）' } else { '' }) + '；官方控制台包 ' + $P.AppxCount + ' 个，控制台进程 ' + $P.CcProcCount + ' 个' + $(if ($P.CcVerdict) { '（' + $P.CcVerdict + '）' } else { '' })))
[void]$sum.AppendLine(('6) 装后自检   : ' + $P.InstallStatus + $(if ($P.InstallStatusValue) { '（Status=' + $P.InstallStatusValue + '）' } else { '' })))
[void]$sum.AppendLine(('    LM 日志    : ' + $P.LmLog + '；MQTT 关键行命中 ' + $P.KeyLines07 + ' 行；底座日志关键行 ' + $P.KeyLines06 + ' 行'))
[void]$sum.AppendLine(('7) 独立探针   : ' + $P.ProbeOverall + '（' + $P.ProbeConclusion + '）'))
[void]$sum.AppendLine(('8) GCU 日志"可发现": 命中 *.log ' + $P.LogDiscHits + ' 个（近 24h ' + $P.LogDiscRecent24h + ' 个；其中 GCU 相关 ' + $P.LogDiscGcuHits + ' 个）；取尾部 ' + $P.LogDiscTailFiles + ' 个文件；' + $(if ($P.LogDiscKeyLines -gt 0) { '关键行命中 ' + $P.LogDiscKeyLines + ' 行（见 12b）' } else { '关键行 0 行（见 12/12b）' })))
[void]$sum.AppendLine(('9) 载荷落地核对: 注册表 GcuPayloadSha256 判定 = ' + $P.PayloadShaVerdict + $(if ($P.PayloadKeyChecked -gt 0) { '；关键文件抽查与本机变体一致 ' + $P.PayloadKeyOk + '/' + $P.PayloadKeyChecked + ' 项' } else { '' })))
[void]$sum.AppendLine(('10) 灯光判据   : 灯带行幻影=' + $P.LightPhantom + '；Logo 行有依据=' + $P.LightLogoEvidence))
[void]$sum.AppendLine(('11) 灯效/档案/模式: KeyboardManager ' + $P.KmState + '；UserPofiles ' + $P.UserPofilesState + '（缺 ' + $P.UserPofilesMissing + ' / 多 ' + $P.UserPofilesExtra + '）；install 日志 ' + $P.InstallLogState + '；档位键 ' + $P.PerfKeyCount + ' 个；perf_active 系 = ' + $(if ($P.PerfActive) { $P.PerfActive } else { '(无)' })))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('★ 最可能的方向（保守提示，不下过度断言）：')
[void]$sum.AppendLine('   ' + $P.Direction)
[void]$sum.AppendLine('')
[void]$sum.AppendLine('-- 采集清单 --')
[void]$sum.AppendLine('   01-进程与服务.txt           GCU 相关进程 / SCM 服务 / 官方控制台(MSIX ControlCenter3*)')
[void]$sum.AppendLine('   02-端口与连接.txt           13688 的 LISTEN/ESTABLISHED、持有者与对端 PID')
[void]$sum.AppendLine('   03-注册表.txt               HKLM\SOFTWARE\L-Mechrevo、两个服务的 ImagePath/Start/Type、ItemSupport 全值 + 三个就绪键')
[void]$sum.AppendLine('   04-GCU文件.txt              GCU 安装树清单 + 关键 13 件签名 + "40A 是否落地"')
[void]$sum.AppendLine('   05-装后自检.txt             %ProgramData%\L-Mechrevo\gcu-install-status.json')
[void]$sum.AppendLine('   06-GCU底座日志.txt          ControlCenter / gcu-install-* / C:\MyControlCenter.log')
[void]$sum.AppendLine('   06b-底座日志-关键行.txt     上面日志里含 StartMainWork/ServiceReady/Error/Subscribe 等关键词的行')
[void]$sum.AppendLine('   07-LM日志.txt               %APPDATA%\MechrevoLite\log.txt 尾部 500 行')
[void]$sum.AppendLine('   07b-LM日志-MQTT关键行.txt   含 CONNACK/订阅/ReasonString/MqttCommunicationTimedOut 等关键词的行')
[void]$sum.AppendLine('   08-网络环境.txt             适配器、回环 ping、winhttp 代理、杀软与过滤驱动线索')
[void]$sum.AppendLine('   09-回环抓包.txt             ★ 127.0.0.1:13688 回环抓包（只保留含 13688 的行）+ 抓包结论')
[void]$sum.AppendLine('   12-GCU日志可发现.txt        ★ 底座根/安装根/%ProgramData%/AppData 相关目录下递归找 *.log；命中清单（路径/大小/mtime + 近24h/近7d 分档）+ 近 24h 尾部')
[void]$sum.AppendLine('   12b-GCU日志可发现-关键行.txt 12 范围内近 24h 日志里含 LM_Manager/RGBKB/solution/UNKNOWN/Subscribe/SUBACK/Mqtt/ACPIDriverInit 等关键词的原文行')
[void]$sum.AppendLine('   13-KeyboardManager.txt      ★ 两个灯效小文件（AppSettings\KeyboardManager.json、KeyboardManager\settings.json）全文 + mtime（是否被运行期改写）')
[void]$sum.AppendLine('   14-载荷落地核对.txt         ★ 注册表 GcuPayloadSha256 与 5 个现行变体值 + 16 个历史旧值比对 + 关键文件（大小/签名者）抽查')
[void]$sum.AppendLine('   15-灯光判据.txt             ★ 能力位 / GCU 上报 / 四条灯条 Status 原文行 + 指纹（LightbarFingerprint 口径）/ 用户灯光键 / 幻影与 Logo 结论')
[void]$sum.AppendLine('   16-UserPofiles清单.txt      UserPofiles 文件名/大小/mtime 清单（不复制内容）+ 期望 16 项缺哪些/多哪些')
[void]$sum.AppendLine('   17-安装日志与装后自检.txt   gcu-install-*.log 清单 + 最近 2 个尾部；gcu-install-status.json 全文')
[void]$sum.AppendLine('   18-模式与风扇快照.txt       perf_*/performance_*/fan* 配置键、风扇表相关注册表（MyFanTable/Support/MySetting）、UserFanTables 目录')
[void]$sum.AppendLine('   00-概要.txt                 本文件（含结论性摘要与给机主的手动步骤）')
[void]$sum.AppendLine('')
if ($script:Warnings.Count -gt 0) {
    [void]$sum.AppendLine('-- 本次采集中的失败/告警（逐项独立，不影响其它项）--')
    foreach ($w in $script:Warnings) { [void]$sum.AppendLine('   ' + $w) }
    [void]$sum.AppendLine('')
}
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine('★ 给机主的三件事（请照做）')
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine('1) 把生成的 zip 发回来。')
[void]$sum.AppendLine('2) 【决定性测试已改由 11-独立MQTT探针.txt 承担】官方控制台若已删除，10-手动结果.txt 可跳过。')
[void]$sum.AppendLine('   ⚠ 但请注意：跑 11 探针前【建议先退出 LM】，避免和 LM 抢同一个白名单 clientId。')
[void]$sum.AppendLine('3) 抓包时请保持 LM 正在运行，并让它处在复现状态（GCU 显示"未连接"）。')
[void]$sum.AppendLine('   否则抓到的可能是正常连接，而不是故障现场。')
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine('⚠️ 待你填写：10-手动结果.txt（若官方控制台已删除，本条可跳过，直接在文件里写"控制台已删除"）')
[void]$sum.AppendLine('============================================================')
Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text $sum.ToString()

# 手动结果模板
$manualPath = Join-Path $OutDir '10-手动结果.txt'
if (-not (Test-Path -LiteralPath $manualPath)) {
    $mt = @'
10 手动结果（请机主填写）
============================================================
⚠️ 官方控制台已删除 => 本条【可跳过】。
   决定性测试已改由 11-独立MQTT探针.txt 承担（探针能收到 SUBACK ⇒ 问题在 LM 侧；
   探针也收不到 SUBACK ⇒ 桥侧问题）。
   若你手上还留着官方控制台，再照下面填；否则直接在本文件里写一行"控制台已删除"即可。

—— 如果你还能打开官方控制台，请按编号做 ——
步骤1：打开【官方控制台】，等它加载完。
步骤2：看它里面 GCU 是否显示"已连接"、设备数是不是 0、能力项是不是 (未收到)。
步骤3：把结果填到下面（把选中的那个留下，其余删掉），并补充现象。

[ ] 能连上      —— 官方控制台里 GCU 显示已连接/能读到设备
[ ] 不能连上    —— 官方控制台也显示未连接/设备数 0
[ ] 控制台打不开 —— 官方控制台本身起不来/闪退/报错
[ ] 控制台已删除 —— 本条跳过

补充现象（尽量具体）：
  官方控制台里看到的字：
  官方控制台启动时间：
  当时 LM 是否在运行：
  其它：

—— 口径 ——
   官方控制台能连上 13688 ⇒ 问题偏向客户端（LM）侧；
   官方控制台也连不上   ⇒ 问题偏向桥（GCUBridge/GCUService）侧。
'@
    Write-Utf8 -Path $manualPath -Text $mt
}

# ---- 打码：把报告文本逐文件脱敏 ----
$redactedCount = 0
$redactFail = @()
foreach ($f in (Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name)) {
    try {
        $raw = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
        $red = Redact-Text -Text $raw
        if ($red -ne $raw) {
            Write-Utf8 -Path $f.FullName -Text $red
            $redactedCount++
        }
        # 自检：哨兵是否全部还原（若没还原说明打码有 bug，绝不能让 U+0001 留在报告里）
        $leftSentinel = ([regex]::Matches($red, [regex]::Escape([string][char]1))).Count
        if ($leftSentinel -gt 0) {
            Warn ('打码哨兵未还原：{0}（残留 {1} 处，该文件请人工看一眼）' -f $f.Name, $leftSentinel)
        }
    } catch {
        $redactFail += ($f.Name + '：' + $_.Exception.Message)
    }
}
foreach ($rf in $redactFail) { Warn ('打码失败（已保留原文）：' + $rf) }

# ---- 打码自检：真实用户名/主机名/序列号必须"零命中"（含"主机名\用户名"形态与文件名里夹的串）----
$selfCheck = New-Object System.Collections.ArrayList
$leakVals = New-Object System.Collections.ArrayList
try {
    [void]$leakVals.Add(@{ V = [string]$env:USERNAME; L = '真实用户名（$env:USERNAME）' })
    [void]$leakVals.Add(@{ V = [string]$env:COMPUTERNAME; L = '真实主机名（$env:COMPUTERNAME）' })
    # ⚠ 2026-10-09（方案B）：新增内容里会出现"主机名\用户名"（WMI/网络/共享路径常见形态）与
    #    "文件名里夹的序列号/长十六进制"，所以自检必须把这两种形态也一起查（否则"零命中"名不副实）。
    if ($env:COMPUTERNAME -and $env:USERNAME) {
        [void]$leakVals.Add(@{ V = ([string]$env:COMPUTERNAME + '\' + [string]$env:USERNAME); L = '主机名\用户名 形态' })
    }
    try {
        Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $u = $_.Name
            if ($u -and $u -notmatch '^(Public|Default|Default User|All Users|WDAGUtilityAccount|AppData)$') {
                [void]$leakVals.Add(@{ V = $u; L = ('C:\Users 下的用户目录名：' + $u) })
                if ($env:COMPUTERNAME) { [void]$leakVals.Add(@{ V = ([string]$env:COMPUTERNAME + '\' + $u); L = ('主机名\用户名 形态：' + $u) }) }
            }
        }
    } catch { }
    try {
        # ⚠ 机型名(SystemProductName)属于"保留可判断性"的信息，允许出现在报告里，
        #    所以这里【不】把它当泄露值查；只查真正的敏感值：序列号。
        $sp = Get-CimInstance -ClassName Win32_ComputerSystemProduct -ErrorAction Stop
        if ($sp -and $sp.IdentifyingNumber -and ([string]$sp.IdentifyingNumber).Length -ge 6) {
            [void]$leakVals.Add(@{ V = [string]$sp.IdentifyingNumber; L = '序列号' })
        }
    } catch { }
    try {
        foreach ($q in @('Win32_BIOS', 'Win32_BaseBoard')) {
            $o = Get-CimInstance -ClassName $q -ErrorAction SilentlyContinue
            if ($o -and $o.SerialNumber -and ([string]$o.SerialNumber).Length -ge 6 -and ([string]$o.SerialNumber) -notmatch '^(?i)(Default string|To be filled|None|0+)$') {
                [void]$leakVals.Add(@{ V = [string]$o.SerialNumber; L = ('序列号(' + $q + ')') })
            }
        }
    } catch { }
} catch { }
$leakFiles = @()
    try {
        $outFiles = @(Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name)
        foreach ($f in $outFiles) {
            try {
                $txt = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
                foreach ($lv in $leakVals) {
                    if (-not $lv.V -or ([string]$lv.V).Length -lt 4) { continue }
                    # ⚠ 只有"裸值"才算漏：像 SystemProductName=xxx 这种带前缀的形态，
                    #    机型名本身保留可判断性是允许的，不算泄露。
                    if ($txt -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape([string]$lv.V) + '(?![0-9A-Za-z])')) {
                        $leakFiles += ($f.Name + ' ← ' + $lv.L)
                    }
                }
            } catch { }
        }
        # ⚠ 产物【文件名】本身也要查：文件名里夹序列号/长十六进制（例如 <序列号>.log 之类的日志名）
        #    是最容易漏的一种痕迹 —— 正文打了码、名字却漏出去。
        foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -Force | Sort-Object Name)) {
            $nm = [string]$f.Name
            foreach ($lv in $leakVals) {
                if (-not $lv.V -or ([string]$lv.V).Length -lt 4) { continue }
                if ($nm -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape([string]$lv.V) + '(?![0-9A-Za-z])')) {
                    $leakFiles += ($nm + '（产物文件名） ← ' + $lv.L)
                }
            }
            # 文件名里夹的长十六进制（≥32 位）：正文里会被打码，文件名也得查出来（只报，不自动改名）
            if ($nm -match '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])') {
                $leakFiles += ($nm + '（产物文件名） ← 文件名里夹着 ≥32 位连续十六进制（可能是哈希/序列号）')
            }
        }
    } catch { }
if ($leakFiles.Count -gt 0) {
    Warn ('打码自检未通过，以下文件疑似仍有真实标识：' + (($leakFiles | Select-Object -Unique) -join '；'))
    [void]$selfCheck.Add('未通过：' + (($leakFiles | Select-Object -Unique) -join '；'))
} else {
    [void]$selfCheck.Add('通过：真实用户名/主机名/"主机名\用户名"/用户目录名/序列号（含产物文件名里夹的串）在全部产物里【零命中】')
}
# ⚠ 痕迹只允许占位符：统计占位符出现次数，并确认没有任何真实值残留。
try {
    $allTxt = ''
    $phHost = 0; $phUser = 0; $phSn = 0; $sentLeft = 0
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -File -Force | Sort-Object Name)) {
        try { $allTxt += (Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop) } catch { }
    }
    $phHost = ([regex]::Matches($allTxt, [regex]::Escape('<主机名>'))).Count
    $phUser = ([regex]::Matches($allTxt, [regex]::Escape('<用户名>'))).Count
    $phSn   = ([regex]::Matches($allTxt, [regex]::Escape('<序列号>'))).Count
    $sentLeft = ([regex]::Matches($allTxt, [regex]::Escape([string][char]1))).Count
    [void]$selfCheck.Add(('占位符痕迹（只允许占位符）：<主机名> {0} 处、<用户名> {1} 处、<序列号> {2} 处、<已打码> {3} 处、<用户目录> {4} 处、<MAC> {5} 处' -f $phHost, $phUser, $phSn, ([regex]::Matches($allTxt, [regex]::Escape('<已打码>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<用户目录>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<MAC>'))).Count))
    if ($sentLeft -gt 0) {
        [void]$selfCheck.Add(('⚠ 未通过：产物里残留打码哨兵 U+0001 共 {0} 处（说明打码有 bug）' -f $sentLeft))
    } else {
        [void]$selfCheck.Add('通过：产物里没有残留打码哨兵（U+0001 零命中）')
    }
} catch {
    [void]$selfCheck.Add('（占位符痕迹统计失败：' + $_.Exception.Message + '）')
}
try {
    $cur = Get-Content -LiteralPath (Join-Path $OutDir '00-概要.txt') -Raw -Encoding UTF8
    $add = "`r`n-- 隐私打码自检 --`r`n   " + ($selfCheck -join "`r`n   ") + "`r`n"
    Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text ($cur + $add)
} catch { }

# ================================================================ 清理临时（⚠ 必须在打包之前）
Step '清理临时抓包文件' | Out-Null
$cleaned = '无'
try {
    if ($tmpRoot -and (Test-Path -LiteralPath $tmpRoot)) {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction Stop
        $cleaned = $tmpRoot
    }
} catch {
    Warn ('临时目录清理失败（可手动删）：' + $tmpRoot + '：' + $_.Exception.Message)
    $cleaned = '清理失败，保留：' + $tmpRoot
}
try {
    Get-ChildItem -LiteralPath $OutDir -Filter '*.etl' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $OutDir -Filter '_tmp_*' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} catch { }
Write-Host ('  -> 清理临时：' + $cleaned)

# ================================================================ 打包
Step '打包 zip' | Out-Null
$zipPath = $OutDir + '.zip'
$zipOk = $false
$zipNote = ''
if (-not $Zip) {
    $zipNote = '按参数 -Zip:$false 不产 zip，只保留目录。'
    Write-Host '  -> 已按 -Zip:$false 跳过打包。'
} else {
    try {
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force -ErrorAction Stop }
        Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -ErrorAction Stop
        if ((Test-Path -LiteralPath $zipPath) -and ((Get-Item -LiteralPath $zipPath).Length -gt 0)) {
            $zipOk = $true
        } else {
            $zipNote = 'zip 生成了但大小为 0。'
        }
    } catch {
        $zipNote = ('zip 打包失败（' + $_.Exception.Message + '）——目录已保留，请直接回传目录内容。')
        Warn ('zip 失败：' + $_.Exception.Message)
    }
}
# zip 自检：不得含 _tmp_* 或 *.etl
if ($zipOk) {
    $zipBad = @()
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $za = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        foreach ($e in $za.Entries) {
            if ($e.FullName -match '_tmp_' -or $e.FullName -like '*.etl') { $zipBad += $e.FullName }
        }
        $za.Dispose()
    } catch { Warn ('zip 内容自检失败：' + $_.Exception.Message) }
    if ($zipBad.Count -gt 0) { Warn ('zip 里混进了临时文件：' + (($zipBad | Select-Object -Unique) -join ', ')) }
    else { Write-Host '  -> zip 自检：不含 _tmp_* / *.etl（干净）' -ForegroundColor Green }
}
if ($zipOk) {
    Write-Host ('  -> zip：' + $zipPath + '（' + (Get-Item -LiteralPath $zipPath).Length + ' 字节）') -ForegroundColor Green
} else {
    Write-Host ('  -> ' + $zipNote) -ForegroundColor Yellow
}

# ================================================================ 收尾打印
Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' 抓包/采集结束' -ForegroundColor White
Write-Host (' 输出目录 : ' + $OutDir)
Write-Host (' zip      : ' + $(if ($zipOk) { $zipPath } else { '未产出（' + $zipNote + '）' }))
Write-Host (' 清理临时 : ' + $cleaned)
Write-Host (' 打码文件 : ' + $redactedCount + ' 个')
Write-Host ' 抓包是否已停止：是（pktmon stop / netsh trace stop 已执行，无后台抓包）'
Write-Host '============================================================' -ForegroundColor White
Write-Host ''
Write-Host '★ 请把生成的 zip 发回来。' -ForegroundColor Green
Write-Host '★ 决定性测试：见 11-独立MQTT探针.txt（官方控制台若已删除，10-手动结果.txt 可跳过）。' -ForegroundColor Green
Write-Host '★ 跑 11 探针前建议先退出 LM（探针会占用一个白名单 clientId），跑完再打开 LM。' -ForegroundColor Green
Write-Host '★ 请保持 LM 运行、并让它在复现状态（GCU 显示未连接）时再抓包。'
Write-Host ''
Write-Host '============================================================' -ForegroundColor Yellow
Write-Host ('⚠️  待你填写：' + (Join-Path $OutDir '10-手动结果.txt')) -ForegroundColor Yellow
Write-Host '    （官方控制台已删除的话，直接在该文件里写一行"控制台已删除"即可）' -ForegroundColor Yellow
Write-Host '============================================================' -ForegroundColor Yellow
Write-Host ''
foreach ($w in $script:Warnings) { Write-Host ('  告警：' + $w) -ForegroundColor Yellow }
exit 0
