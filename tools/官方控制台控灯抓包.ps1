<#
.SYNOPSIS
    官方控制台控灯抓包.ps1 —— "官方控制台怎么控键盘灯" 一次性只读取证
    （MQTT 全帧 / HID 只读 / 注册表快照 / EC 只读 / GCU 日志；全程只读）
.DESCRIPTION
    为什么要这个脚本（背景）：
      30 系机器（蛟龙 GM5ZG0O / project IDW / GcuVariant=30）上，
      L-Mechrevo 侧 Keyboard/Status 恒为 solution=UNKNOWN、power=否，LM 从未发出过灯效帧。
      怀疑：30 系官方控制台对灯光用的接口和我们走的不是同一条。
      所以要让机主在一台【装好官方控制台】的机器上，把控制台"怎么控灯"这件事原样抓下来。

    两个 MQTT 抓包窗（时长可分别指定，默认已改短，机主不用干等；⭐ 全程零阻塞、自动计时）：
      第 1 窗（控灯前基线）默认 12 秒（10~15 秒都行）：这 10~15 秒是基线，
        你可以先去把【官方控制台打开、翻到键盘灯页面】，但【先不要改灯】；
        基线只要证明"不改灯时没有灯效帧"，背景流量几秒就够，所以这窗很短。
        倒计时期间会显示"剩余 N 秒"；不按任何键也会自己跑满 --
        允许按回车提前结束（可选，纯省时间，基线早结束无害）；开始后前 5 秒内的回车会被忽略（防误触）。
      ⭐ 第 1 窗一结束【立刻自动进入第 2 窗】，中间【不等待任何按键】、没有 Read-Host，
        所以第 1 窗快结束时就要先切到官方控制台，改灯动作才落在窗口【里面】。
      第 2 窗（控灯中）默认 40 秒（30~45 秒都行）：这 30~45 秒里请【全程持续改灯】
        （关→开、换颜色、换模式），每次停 2~3 秒；本窗【自动计时、到点自动收窗】，
        也允许按回车提前结束（若已改完不想干等）——但提前结束会少抓后面的帧；
        为避免误触/连按把窗口瞬间收掉，开始后前 5 秒内的回车一律忽略。

    本脚本要抓的通道（全部只读）：
      01  注册表快照（控灯前）：HKLM\SOFTWARE\OEM\GamingCenter2（含 ItemSupport 全值）+ HKLM\SOFTWARE\L-Mechrevo
      02  HID 设备枚举（全部 HIDClass / PnP，含 VID/PID/FriendlyName/InstanceId/Status）+ 可疑灯效设备标注
      03  HID 只读探测：枚举 HID 接口 -> 读 UsagePage/Usage/上报长度（判定键盘/Consumer/Vendor 集合），
          并尝试用 OVERLAPPED 超时读一次输入报告。绝不向 HID 写任何数据。
      04  MQTT 全帧抓包 · 第 1 窗（控灯前基线，默认 12 秒）：连 127.0.0.1:13688，SUBSCRIBE 全部主题，
          逐条打印收到的 PUBLISH 的 topic + payload 原文 + 时间戳 + 方向。绝不 PUBLISH。
          clientId 按 GCU 白名单顺序 UWPClient_4 -> _3 -> _2 串行回退（每个只试一次，连完即关）；
          CONNECT 参数与 LM 对齐（MQTT 3.1.1 / CleanSession=1 / keepalive 3 秒），并按真实成功抓包带上
          username/password（UWPClient_N 配 UWPClient_User_N / UWPClient_Pwd888881772688_N，flags 0xC2），并把
          【我方发出的 CONNECT 原始字节 hex】写进产物，便于与 LM 抓包逐字节对照。
          握手失败时：屏幕红字告警，并把"本窗无有效数据 + 失败原因（TCP 连不上 / 读 CONNACK 超时 /
          收到 CONNACK 但 rc≠0，三种分开写）+ 排查提示"写进 04 节顶部与 00-概要.txt。
      00b EC 只读采样（控灯前，在 MQTT 第 1 窗【之前】）：0x740 / 0x741 / 0x766 / 0x767；
          只读、绝不写 EC；非管理员读不到就如实写"做不到"，不伪造。
          这份"控灯前"读数与 07 的"控灯后"读数在报告里成对出现（可做前后对比）。
      05  [衔接] 第 1 窗结束立刻自动开始第 2 窗（不等按键）；"现在去改灯"的提示在第 2 窗开始后打印
      06  MQTT 全帧抓包 · 第 2 窗（控灯时，默认 40 秒）：同上（这一窗才是重点；失败告警同 04）
      07  EC 只读：\\.\ACPIDriver + IOCTL_GPD_ACPI_ECREAD(2621482120) 读 0x740(1856) / 0x766(1894)
          （另附 0x741 / 0x767 相邻寄存器对照）。绝不写 EC（脚本里没有写 IOCTL 2621482124）
      08  GCU 日志：只读 C:\ProgramData\ControlCenter\*.log（列清单 + 尾部 + 关键行），
          并打印 Debug\enable=1 的确切开启步骤
      09  注册表快照（控灯后）+ 前后差异对比；单独列出三个关键值
      00  00-概要.txt：结论摘要 + 逐字的给机主步骤 + 隐私打码自检

    用法（把 <脚本路径> 换成实际位置；更全的示例看 -? / Get-Help .\官方控制台控灯抓包.ps1 -Full 的 .EXAMPLE 节）：
      默认（推荐，双击/一条命令即可）= 第 1 窗 12 秒 + 第 2 窗 40 秒：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1"
      两窗【分别】指定（第 1 窗 15 秒、第 2 窗 45 秒）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -BaselineSeconds 15 -ControlSeconds 45
      旧参数 -CaptureSeconds N 仍兼容（= 两窗都是 N 秒，老命令行为不变）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -CaptureSeconds 90
      只抓 MQTT（跳过 HID/EC）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -SkipHid -SkipEc
      自测（不连网、不读 EC、不动系统，只验证 MQTT 解码链路）：
        powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -SelfTest

    权限与安全：
      * EC 读取必须管理员；HID 原始报告读取建议管理员。非管理员时脚本【明确提示并降级】，
        其余通道（注册表/HID 枚举/MQTT/日志）普通用户也能收。
      * 全程只读：不 PUBLISH 任何 MQTT 帧、不写 HID、不写 EC/NVRAM、不改注册表、
        不启停任何服务/进程、不安装/运行任何包。
      * 唯一碰网络的：本机回环 127.0.0.1:13688 的 CONNECT + SUBSCRIBE + PINGREQ + DISCONNECT。
      * 跑完自动清理临时文件，只保留报告目录与最终 zip。
.PARAMETER CaptureSeconds
    [旧参数·仍兼容] 一次把两个 MQTT 抓包窗设成同一秒数（默认 0 = 未指定，走下面两个独立默认值）；
    传了它就等于 -BaselineSeconds N -ControlSeconds N（老命令 -CaptureSeconds 90 的行为不变）。
.PARAMETER BaselineSeconds
    第 1 窗（控灯前基线）时长（秒），默认 12（10~15 秒都行；背景流量几秒就够）。
    这 10~15 秒是基线：可以先去把官方控制台打开、翻到键盘灯页面，但【先不要改灯】。
.PARAMETER ControlSeconds
    第 2 窗（控灯中）时长（秒），默认 40（30~45 秒都行；要覆盖多次改灯，所以刻意不给提前结束）。
.PARAMETER OutDir
    输出目录，默认桌面 $env:USERPROFILE\Desktop\LM-官方控灯抓包-<yyyyMMdd-HHmmss>。
.PARAMETER Zip
    是否产 zip，默认产出；不产用 -Zip:$false。
.PARAMETER NoPrompt
    不等回车（自动化/自测用；机主正常使用不要加）。
.PARAMETER SkipMqtt
    跳过 MQTT 抓包（只收别的通道）。
.PARAMETER SkipHid
    跳过 HID 枚举与只读探测。
.PARAMETER SkipEc
    跳过 EC 只读。
.PARAMETER Port
    MQTT 端口，默认 13688。
.PARAMETER Topics
    要订阅的主题，默认 '#' 通配 + LM 已知主题。
.PARAMETER ClientIds
    clientId 尝试顺序，默认按 GCU 白名单 UWPClient_4 -> UWPClient_3 -> UWPClient_2
    （串行尝试，每个只试一次；每次都先关掉上一条连接再连，绝不并发占用多个）。
    三个白名单 clientId 都没拿到 CONNACK 时会红字明确报告，并提示"官方控制台可能占满了会话"这个两难。
.PARAMETER HidProbeSeconds
    HID 只读探测总预算（秒），默认 6。
.PARAMETER SelfTest
    只跑 MQTT 解码自测：不连网、不读 EC、不枚举 HID、不读注册表。
.EXAMPLE
    默认（推荐；第 1 窗 12 秒 + 第 2 窗 40 秒）：
      powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1"
.EXAMPLE
    两窗分别指定（第 1 窗 15 秒、第 2 窗 45 秒）：
      powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -BaselineSeconds 15 -ControlSeconds 45
.EXAMPLE
    旧参数仍兼容（两窗都 90 秒）：
      powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -CaptureSeconds 90
.EXAMPLE
    只抓 MQTT（跳过 HID/EC）：
      powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -SkipHid -SkipEc
.EXAMPLE
    自测（不连网、不读 EC、不动系统，只验证 MQTT 解码链路）：
      powershell -ExecutionPolicy Bypass -File "<脚本路径>\官方控制台控灯抓包.ps1" -SelfTest
#>
[CmdletBinding()]
param(
    # [旧参数·仍兼容] 一次把两个 MQTT 抓包窗设成同一秒数（默认 0 = 未指定，走下面两个独立默认值）。
    #   传了它 = -BaselineSeconds N -ControlSeconds N（老命令 -CaptureSeconds 90 行为不变）。
    [int]$CaptureSeconds = 0,
    # 第 1 窗（控灯前基线）时长（秒）。默认 12（10~15 秒都行）。
    #   这 10~15 秒是基线：可以先去把官方控制台打开、翻到键盘灯页面，但【先不要改灯】。
    [int]$BaselineSeconds = 12,
    # 第 2 窗（控灯中）时长（秒）。默认 40（30~45 秒都行）。
    #   这 30~45 秒里请【持续改灯】（换颜色/换模式），每次停 2~3 秒；必须覆盖多次改灯。
    [int]$ControlSeconds = 40,
    # 输出目录，默认桌面 $env:USERPROFILE\Desktop\LM-官方控灯抓包-<yyyyMMdd-HHmmss>
    [string]$OutDir,
    # 是否产 zip，默认产出
    [bool]$Zip = $true,
    # 不等回车（自动化/自测用；机主正常使用不要加）
    [switch]$NoPrompt,
    # 跳过 MQTT 抓包（只收别的通道）
    [switch]$SkipMqtt,
    # 跳过 HID 枚举与只读探测
    [switch]$SkipHid,
    # 跳过 EC 只读
    [switch]$SkipEc,
    # MQTT 端口
    [int]$Port = 13688,
    # 要订阅的主题（默认 # 通配 + LM 已知主题）
    [string[]]$Topics = @(
        '#',
        'Keyboard/Ctrl', 'Keyboard/Status',
        'HidLightbar/Ctrl', 'HidLightbar/Status', 'HidLightbar_Sync/Status',
        'Setting/Control', 'Setting/Status',
        'GPUDevice/Status', 'System/BatteryProtection', 'BatteryProtection/Control', 'Fan/Control'
    ),
    # clientId 尝试顺序：按 GCU 白名单 UWPClient_4 -> _3 -> _2。
    #   现场教训：用白名单外的 UWPClient_1 时 TCP 能连上、但读 CONNACK 永远超时
    #   （桥很可能直接忽略白名单外的 clientId），=> 默认改成按白名单顺序回退。
    #   串行尝试：每次 Invoke-MqttWindow 在自己的 finally 里关掉上一条连接后才试下一个，
    #   绝不并发占用多个 clientId。
    [string[]]$ClientIds = @('UWPClient_4', 'UWPClient_3', 'UWPClient_2'),
    # HID 只读探测总预算（秒）
    [int]$HidProbeSeconds = 6,
    # 只跑 MQTT 解码自测：不连网、不读 EC、不枚举 HID、不读注册表
    [switch]$SelfTest
)
#Requires -Version 5.1

# --- 两窗时长：默认值 + 旧参数兼容 + 兜底夹取（纯参数归一，不动任何采集逻辑）---
# 旧参数 -CaptureSeconds 显式传了（且 >= 5）就同时管两窗 => 老命令行为不变。
if ($PSBoundParameters.ContainsKey('CaptureSeconds') -and $CaptureSeconds -ge 5) {
    $BaselineSeconds = $CaptureSeconds
    $ControlSeconds  = $CaptureSeconds
}
# 误传 0/负数/太小的值会让窗口短到抓不全，统一抬到 5 秒下限（默认值不受影响）。
if ($BaselineSeconds -lt 5) { $BaselineSeconds = 5 }
if ($ControlSeconds  -lt 5) { $ControlSeconds  = 5 }

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$SCRIPT_VERSION = '2026-10-09 r3（官方控制台控灯抓包 · MQTT 全帧/HID/注册表/EC 只读；两窗时长默认 12s/40s 可分别指定；⭐ r3 修 CONNECT 拼包：补协议名长度 00 04 + 按 clientId 编号配套 username/password）'

# --- MQTT 通信参数（与 LM 对齐；只影响我们发出去的 CONNECT / PINGREQ，不影响收到的报文与解码）---
#   keepalive：LM 用 WithKeepAlivePeriod(3 秒)，本脚本原来发 60 => 改成 3 对齐。
#   保活间隔：= keepalive/2（下限 2 秒）。取舍见 Invoke-MqttWindow 里的注释。
$MQTT_KEEPALIVE     = 3
$MQTT_PING_SECONDS  = [Math]::Max(2, [int][Math]::Ceiling($MQTT_KEEPALIVE / 2.0))

# ================================================================ 运行状态
$script:Warnings = New-Object System.Collections.ArrayList
$script:StepNo   = 0
$script:RunStart = Get-Date

# 零写 / 零发布自检用的"针"：故意用拼接方式构造，避免自检字符串本身命中自己。
# 只在【代码行】上匹配（注释/说明里会出现这两个数字，那是文档不是调用）。
$script:EcWriteNeedle    = '2621482' + '124u'          # EC 写 IOCTL 的 C# 字面量形态
$script:PubBuilderNeedle = 'New-Mqtt' + 'Publish'      # PUBLISH 组包函数名

function Get-CodeText {
    # 去掉 PowerShell(#) / C#(//) / iss(;) 注释行，只留代码行，用于"零写/零发布"自检
    param([string]$Text)
    $keep = New-Object System.Collections.ArrayList
    foreach ($ln in ([string]$Text -split "`r?`n")) {
        $t2 = $ln.TrimStart()
        if ($t2.StartsWith('#')) { continue }
        if ($t2.StartsWith(';')) { continue }
        if ($t2.StartsWith('//')) { continue }
        [void]$keep.Add($ln)
    }
    return ($keep -join "`n")
}

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
    # 带 BOM 的 UTF-8：机主用记事本打开中文不乱码
    param([string]$Path, [string]$Text)
    [System.IO.File]::WriteAllText($Path, [string]$Text, (New-Object System.Text.UTF8Encoding($true)))
}

function Get-Stamp { return (Get-Date -Format 'HH:mm:ss.fff') }

function Test-Admin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $pr = New-Object Security.Principal.WindowsPrincipal($id)
        return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Format-HexBytes {
    param([byte[]]$Bytes, [int]$Max = 256)
    if (-not $Bytes -or $Bytes.Length -eq 0) { return '(空)' }
    $n = [Math]::Min($Max, $Bytes.Length)
    $s = (($Bytes[0..($n - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' ')
    if ($Bytes.Length -gt $Max) { $s += (' ...(共 ' + $Bytes.Length + ' 字节，只列前 ' + $Max + ')') }
    return $s
}

# ================================================================ 隐私打码
# 与 tools\GCU抓包.ps1 同源同口径，便于两份产物互相对照。
# 表里顺序有意义：先命中先保护，所以【长词必须排在短词前面】。
$script:SafeWords = @(
    'SystemProductName', 'BIOS_PROJECT_ID', 'KeyboardManager', 'KeyboardType',
    'HidLightbar_Sync', 'HidLightbar', 'Lightbar', 'Keyboard',
    'L-Mechrevo', 'MechrevoLite', 'Mechrevo', 'ControlCenter', 'GamingCenterService', 'GamingCenter',
    'GCUBridge', 'GCUService', 'GCUServicePlugin', 'OSDTpDetect', 'GCU',
    'AiStoneService', 'AiStageService', 'MyControlCenter', 'GamingMonitor',
    'UEFI_Firmware', 'NVControlSetting', 'GPUInfoDLL', 'ACPIDriverDll', 'ACPIDriver',
    'RegistryUtils', 'UWAirplane', 'UWDiskInfo', 'DisplayInfo', 'UWACPIDriver',
    'UsagePage', 'InputReport', 'OutputReport', 'VendorID', 'ProductID', 'FriendlyName',
    'InstanceId', 'Provider', 'ProgramData', 'Program Files (x86)', 'Program Files', 'Program',
    'Windows', 'System32', 'SysWOW64', 'AppData', 'Roaming', 'Local', 'Users',
    'Desktop', 'Documents', 'Downloads', 'Temp', 'SOFTWARE', 'SYSTEM',
    'CurrentControlSet', 'Services', 'ItemSupport', 'ServiceReady', 'Debug', 'enable',
    'ServiceLoadingReady', 'PerformanceReady', 'Service',
    'Item', 'Support', 'Ready', 'Loading', 'Performance',
    'LocalHost', 'localhost', 'Administrator',
    '127.0.0.1', '::1', '0.0.0.0', '::'
)

function Protect-Words {
    # 哨兵做法：先把"已知系统路径分量/产品名"换成 U+0001 哨兵，打码完再还原。
    # 必须用真实的 U+0001 字符；"\u0001" 字面量在 .NET 正则里不是转义。
    param([string]$Text)
    $t = [string]$Text
    if ([string]::IsNullOrEmpty($t)) { return $t }
    $s = [string][char]1
    for ($i = 0; $i -lt $script:SafeWords.Count; $i++) {
        $w = $script:SafeWords[$i]
        try { $t = [regex]::Replace($t, [regex]::Escape($w), ($s + 'W' + $i + $s), 'IgnoreCase') } catch { }
    }
    return $t
}

function Unprotect-Words {
    param([string]$Text)
    $t = [string]$Text
    if ([string]::IsNullOrEmpty($t)) { return $t }
    $s = [string][char]1
    for ($i = 0; $i -lt $script:SafeWords.Count; $i++) {
        try { $t = [regex]::Replace($t, [regex]::Escape($s + 'W' + $i + $s), $script:SafeWords[$i]) } catch { }
    }
    return $t
}

function Get-RedactMap {
    $m = New-Object System.Collections.ArrayList
    try {
        $cn = [string]$env:COMPUTERNAME
        if ($cn) { [void]$m.Add(@{ P = [regex]::Escape($cn); R = '<主机名>' }) }
        $unames = New-Object System.Collections.ArrayList
        $un = [string]$env:USERNAME
        if ($un -and $un.Length -ge 2) { [void]$unames.Add($un) }
        try {
            Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
                $n = $_.Name
                if ($n -and $n.Length -ge 2 -and $n -notmatch '^(Public|Default|Default User|All Users|WDAGUtilityAccount|AppData)$') { [void]$unames.Add($n) }
            }
        } catch { }
        foreach ($u in ($unames | Select-Object -Unique)) {
            if ($u -and $u.Length -ge 2) { [void]$m.Add(@{ P = [regex]::Escape($u); R = '<用户名>' }) }
        }
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
                            if ($v -and $v.Trim().Length -ge 4 -and $v -notmatch '^(Default string|To be filled|None|0+)$') { $serials += $v.Trim() }
                        } catch { }
                    }
                }
            } catch { }
        }
        foreach ($s in ($serials | Select-Object -Unique)) { [void]$m.Add(@{ P = [regex]::Escape($s); R = '<序列号>' }) }
    } catch { }
    return $m
}

function Redact-Text {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $t = [string]$Text
    if ($t.Length -eq 0) { return $t }
    $t = Protect-Words $t
    # 1) 长十六进制串 / 长 base64 串：只保形状判定，保留前 16 位可判断性
    try {
        $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])', {
            param($m)
            $v = $m.Value
            if ($v.Length -le 16) { return $v }
            return ($v.Substring(0, 16) + '<已打码>')
        })
    } catch { }
    try {
        $t = [regex]::Replace($t, '(?<![0-9A-Za-z+/])[0-9A-Za-z+/]{40,}={0,2}(?![0-9A-Za-z+/=])', {
            param($m)
            $v = $m.Value
            if (-not (($v -cmatch '[a-z]') -and ($v -cmatch '[A-Z]') -and ($v -match '[0-9]'))) { return $v }
            return ($v.Substring(0, 16) + '<已打码>')
        })
    } catch { }
    # 2) MAC（含 - : 两种写法）
    try { $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])([0-9A-Fa-f]{2}([-:]))([0-9A-Fa-f]{2}\2){4}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])', '<MAC>') } catch { }
    # 2b) IPv6：只打码网络前缀，保留最后一段
    try {
        $t = [regex]::Replace($t, '(?<![:0-9A-Fa-f])(?:[0-9A-Fa-f]{1,4}:){2,7}:[0-9A-Fa-f]{1,4}(?![:0-9A-Fa-f])', {
            param($m)
            $v = $m.Value
            $ip = $v; $port = ''
            $mm = [regex]::Match($v, '^(.*?):(\d{1,5})$')
            if ($mm.Success) { $ip = $mm.Groups[1].Value; $port = ':' + $mm.Groups[2].Value }
            $i = $ip.LastIndexOf(':')
            if ($i -le 0) { return $v }
            return ($ip.Substring(0, $i + 1) + 'xxxx' + $port)
        })
    } catch { }
    # 3) 主机名/用户名/用户目录/序列号（整串）
    $skippedBySentinel = New-Object System.Collections.ArrayList
    foreach ($e in $script:RedactMap) {
        try {
            $before = $t
            $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase')
            if ($t -ceq $before -and $e.R -in @('<用户名>', '<主机名>', '<序列号>', '<用户目录>')) { [void]$skippedBySentinel.Add($e) }
        } catch { }
    }
    $t = Unprotect-Words $t
    # 3b) 哨兵还原后补一刀（C:\Users\<用户名>\... 这种）
    foreach ($e in $skippedBySentinel) {
        try { $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase') } catch { }
    }
    # 3c) 裸用户名/裸主机名兜底
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
            [void]$script:BareNamePatterns.Add(@{ P = [regex]::Escape($n); R = '<用户名>' })
        }
    }
} catch { }

# ================================================================ MQTT 手搓客户端
# 只 CONNECT / SUBSCRIBE / PINGREQ / DISCONNECT；【绝对没有 PUBLISH 的代码路径】。
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

function Resolve-MqttReadErrorKind {
    # ⭐⭐ 修窗口 bug 的核心判定：把"这次读失败"分成两类 ——
    #   TimedOut（RecvTimeoutMs 内没数据 => 正常读超时，本窗【继续轮询】，绝不结束窗口）
    #   其它      （ConnectionReset / Aborted / Closed / EOF… => 真断线，如实结束窗口）
    # ⚠️ 为什么必须沿 InnerException 链找：PowerShell 会把 .NET 异常包成
    #   MethodInvocationException / RuntimeException（报错前缀就是
    #   `Exception calling "ReadByte" with "0" argument(s): "…did not properly respond after a period of time…"`），
    #   所以对 $_.Exception 做 `-is [System.IO.IOException]` 判定恒为 false —— 这正是第 3 次真实抓包
    #   两窗被 1 秒空闲提前掐断的根因（旧代码 471-481 行）。
    # 返回：@{ TimedOut = <bool>; Kind = 'TimedOut' | 'SocketError.<码>' | 'Disconnected' }
    param($Exception)
    $isTimeout = $false
    $sockEx = $null
    $msgChain = ''
    $cur = $Exception
    for ($guard = 0; $guard -lt 8 -and $cur; $guard++) {
        if (-not $sockEx -and ($cur -is [System.Net.Sockets.SocketException])) { $sockEx = $cur }
        $msgChain += [string]$cur.Message + ' | '
        $cur = $cur.InnerException
    }
    if ($sockEx -and $sockEx.SocketErrorCode -eq [System.Net.Sockets.SocketError]::TimedOut) {
        $isTimeout = $true
    }
    # 兜底：只在链上【根本没有】SocketException 时按消息文本判（覆盖包装形态差异）。
    #   真断线的文本是 forcibly closed / aborted / 已关闭 —— 不含下面这些词，不会被误判成超时。
    if (-not $isTimeout -and (-not $sockEx) -and $msgChain -match '(?i)did not properly respond|timed\s*out|timedout') {
        $isTimeout = $true
    }
    $kind = 'Disconnected'
    if ($isTimeout) { $kind = 'TimedOut' }
    elseif ($sockEx) { $kind = ('SocketError.' + [string]$sockEx.SocketErrorCode) }
    return @{ TimedOut = $isTimeout; Kind = $kind }
}

function Read-MqttPacket {
    # 读一个 MQTT 控制报文 -> @{ Ok; Type; Flags; Body; Raw; Err; TimedOut; ErrKind }
    param([System.IO.Stream]$Stream, [int]$MaxBytes = 1048576)
    $res = @{ Ok = $false; Type = -1; Flags = 0; Body = @(); Raw = @(); Err = ''; TimedOut = $false; ErrKind = '' }
    try {
        $first = $Stream.ReadByte()
        if ($first -lt 0) { $res.Err = '对端已关闭连接（EOF）'; return $res }
        $res.Type = ($first -shr 4)
        $res.Flags = ($first -band 0x0F)
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
        $ex = $_.Exception
        # ⭐⭐ 旧写法（错）：`if ($ex -is [System.IO.IOException]) { … }` —— $ex 其实是
        #   MethodInvocationException，恒为 false，连里面的消息兜底也一起被跳过。
        # 现写法：交给 Resolve-MqttReadErrorKind 沿 InnerException 链判定（见该函数注释）。
        $cls = Resolve-MqttReadErrorKind -Exception $ex
        $res.TimedOut = $cls.TimedOut
        $res.ErrKind = $cls.Kind
        $res.Err = $ex.Message
    }
    return $res
}

function New-MqttFixedPacket {
    # 按首字节 + 报文体拼一个完整报文（自动算 Remaining Length）
    param([byte]$First, [byte[]]$Body)
    $rl = @()
    $x = $Body.Length
    do {
        $d = $x % 128
        $x = [int][Math]::Floor($x / 128)
        if ($x -gt 0) { $d = $d -bor 0x80 }
        $rl += [byte]$d
    } while ($x -gt 0)
    $pkt = New-Object System.Collections.Generic.List[byte]
    $pkt.Add($First)
    $pkt.AddRange([byte[]]$rl)
    $pkt.AddRange($Body)
    return $pkt.ToArray()
}

function Get-MqttCredentials {
    # ⭐ 凭据与 clientId 的编号严格配套：UWPClient_N -> 用户名 UWPClient_User_N / 密码 UWPClient_Pwd888881772688_N。
    #   依据（真实成功抓包，逐字照抄）：archive\核显省电抓包\gpu_saving_capture.py:77-88 —— 那张表里
    #     ("UWPClient_1","UWPClient_User_1","UWPClient_Pwd888881772688_1") … 一直到 _4，四组都能对上号。
    #   另有成功抓包记录：diagnostics\30系键盘灯GCU协议_抓包分析.md:18-29（用 UWPClient_2 抓到了官方控灯帧）。
    #   ⚠️ 仓库里 _1/_2/_3/_4 四组凭据齐全，没有缺失的组，所以不需要（也不许）编造凭据。
    #   clientId 不是 UWPClient_<数字> 形态时返回 $null => 该次不带凭据（flags 只留 0x02），并由调用方如实记录。
    param([string]$ClientId)
    $m = [regex]::Match([string]$ClientId, '^UWPClient_([0-9]+)$')
    if (-not $m.Success) { return $null }
    $n = $m.Groups[1].Value
    return @{
        User = ('UWPClient_User_' + $n)
        Pwd  = ('UWPClient_Pwd888881772688_' + $n)
    }
}

function New-MqttConnect {
    # MQTT 3.1.1 CONNECT（CleanSession=1，keepalive 默认 3 秒，与 LM 的 WithKeepAlivePeriod(3s) 对齐）。
    # ⭐ 2026-10-09 r3 修两处缺陷（原来两次真实抓包都因此白跑）：
    #   ① 【漏了"协议名长度"两字节 00 04】：原来发出的是 10 15 4D 51 54 54 04 02 …，broker 会把
    #      4D 51 当成协议名长度（0x4D51）=> 不回 CONNACK => 现象"TCP 连上、读 CONNACK 超时"。
    #      合法应为 10 17 00 04 4D 51 54 54 04 <flags> 00 03 <clientId…>（remlen 也跟着 +2）。
    #   ② 【没带用户名/密码】：真实成功的抓包都带凭据（见 Get-MqttCredentials）；
    #      带 username + password 时连接标志 = 0xC2（username 0x80 | password 0x40 | cleanSession 0x02）。
    #   合法字节序：10 <remlen> | 00 04 'M' 'Q' 'T' 'T' | 04（级别） | <flags> | <keepalive 2B>
    #              | 00 <len> <clientId> | 00 <len> <username> | 00 <len> <password>
    #   不传 -User/-Password 就不带凭据（flags 只留 0x02）—— 只有白名单外的自定义 clientId 会走到这条。
    param([string]$ClientId, [int]$KeepAlive = 3, [string]$User = '', [string]$Password = '')
    $idBytes = [System.Text.Encoding]::UTF8.GetBytes($ClientId)
    $uBytes = New-Object byte[] 0
    if ($User)     { $uBytes = [System.Text.Encoding]::UTF8.GetBytes($User) }
    $pBytes = New-Object byte[] 0
    if ($Password) { $pBytes = [System.Text.Encoding]::UTF8.GetBytes($Password) }
    $flags = 0x02                                              # CleanSession
    if ($uBytes.Length -gt 0) { $flags = $flags -bor 0x80 }    # User Name Flag
    if ($pBytes.Length -gt 0) { $flags = $flags -bor 0x40 }    # Password Flag
    $vh = New-Object System.Collections.Generic.List[byte]
    $vh.Add(0x00); $vh.Add(0x04)   # ⭐ 协议名长度（就是本次修的根因：原来这两字节漏了）
    $vh.AddRange([System.Text.Encoding]::ASCII.GetBytes('MQTT'))
    $vh.Add(4)          # 协议级别 3.1.1
    $vh.Add([byte]$flags)
    $vh.Add([byte](([int]$KeepAlive -shr 8) -band 0xFF)); $vh.Add([byte]([int]$KeepAlive -band 0xFF))   # keepalive
    $pl = New-Object System.Collections.Generic.List[byte]
    $pl.Add([byte](($idBytes.Length -shr 8) -band 0xFF))
    $pl.Add([byte]($idBytes.Length -band 0xFF))
    $pl.AddRange($idBytes)
    # 按 MQTT 3.1.1 规范：clientId 之后依次是 username、password（各为 2 字节长度 + UTF-8 字节）。
    if ($uBytes.Length -gt 0) {
        $pl.Add([byte](($uBytes.Length -shr 8) -band 0xFF))
        $pl.Add([byte]($uBytes.Length -band 0xFF))
        $pl.AddRange($uBytes)
    }
    if ($pBytes.Length -gt 0) {
        $pl.Add([byte](($pBytes.Length -shr 8) -band 0xFF))
        $pl.Add([byte]($pBytes.Length -band 0xFF))
        $pl.AddRange($pBytes)
    }
    $body = New-Object System.Collections.Generic.List[byte]
    $body.AddRange($vh); $body.AddRange($pl)
    return (New-MqttFixedPacket -First ([byte]0x10) -Body $body.ToArray())
}

function Get-MqttConnectFields {
    # ⭐ 字节级反解一个 CONNECT 报文（只解析、不联网）：把拼好的字节按字段解回来，做长度自洽校验。
    #   返回 ordered dict；Ok=$false 时 Err 写明哪一段越界/对不上。
    param([byte[]]$Packet)
    $r = [ordered]@{ Ok = $false; Err = ''; Total = 0; RemLen = -1; RemLenBytes = 0; ProtoNameLen = -1
                     ProtoName = ''; Level = -1; Flags = -1; KeepAlive = -1; ClientId = ''; User = ''; Pwd = '' }
    if (-not $Packet -or $Packet.Length -lt 2) { $r.Err = '报文太短'; return $r }
    $r.Total = $Packet.Length
    if ($Packet[0] -ne 0x10) { $r.Err = ('首字节不是 0x10（CONNECT）：0x{0:X2}' -f $Packet[0]); return $r }
    $rl = Get-MqttRemLen -Data $Packet -Start 1
    if ($rl -eq -1) { $r.Err = '变长头（剩余长度）非法'; return $r }
    $r.RemLen = $rl.Len; $r.RemLenBytes = $rl.Consumed
    $plEnd = 1 + $rl.Consumed + $rl.Len
    if ($plEnd -ne $Packet.Length) { $r.Err = ('剩余长度 ' + $rl.Len + ' 与总长 ' + $Packet.Length + ' 对不上'); return $r }
    $i = 1 + $rl.Consumed
    if (($i + 10) -gt $plEnd) { $r.Err = '变长头不足（<10 字节）'; return $r }
    $r.ProtoNameLen = ([int]$Packet[$i] * 256) + [int]$Packet[$i + 1]
    if (($i + 2 + $r.ProtoNameLen) -gt $plEnd) { $r.Err = '协议名长度越界'; return $r }
    $r.ProtoName = [System.Text.Encoding]::ASCII.GetString($Packet, $i + 2, $r.ProtoNameLen)
    $i += 2 + $r.ProtoNameLen
    $r.Level = [int]$Packet[$i]; $i++
    $r.Flags = [int]$Packet[$i]; $i++
    $r.KeepAlive = ([int]$Packet[$i] * 256) + [int]$Packet[$i + 1]; $i += 2
    foreach ($slot in @('ClientId', 'User', 'Pwd')) {
        if ($slot -eq 'User' -and (($r.Flags -band 0x80) -eq 0)) { continue }
        if ($slot -eq 'Pwd'  -and (($r.Flags -band 0x40) -eq 0)) { continue }
        if (($i + 2) -gt $plEnd) { $r.Err = ($slot + '：长度字段越界'); return $r }
        $len = ([int]$Packet[$i] * 256) + [int]$Packet[$i + 1]; $i += 2
        if (($i + $len) -gt $plEnd) { $r.Err = ($slot + '：内容越界（声明 ' + $len + ' 字节）'); return $r }
        $r[$slot] = [System.Text.Encoding]::UTF8.GetString($Packet, $i, $len)
        $i += $len
    }
    if ($i -ne $plEnd) { $r.Err = ('尾部还有 ' + ($plEnd - $i) + ' 字节没被字段消化'); return $r }
    $r.Ok = $true
    return $r
}

function New-MqttSubscribe {
    # 一个 SUBSCRIBE 报文里带【全部】主题过滤器（QoS0）
    param([string[]]$TopicFilters, [int]$PacketId = 1)
    $body = New-Object System.Collections.Generic.List[byte]
    $body.Add([byte](($PacketId -shr 8) -band 0xFF))
    $body.Add([byte]($PacketId -band 0xFF))
    foreach ($t in $TopicFilters) {
        $tb = [System.Text.Encoding]::UTF8.GetBytes($t)
        $body.Add([byte](($tb.Length -shr 8) -band 0xFF))
        $body.Add([byte]($tb.Length -band 0xFF))
        $body.AddRange($tb)
        $body.Add([byte]0x00)   # 请求 QoS0
    }
    return (New-MqttFixedPacket -First ([byte]0x82) -Body $body.ToArray())
}

function ConvertTo-MqttPayloadText {
    param([byte[]]$Bytes)
    if (-not $Bytes -or $Bytes.Length -eq 0) { return '(空)' }
    $cap = 8192
    $useBytes = $Bytes
    $trunc = ''
    if ($Bytes.Length -gt $cap) {
        $useBytes = New-Object byte[] $cap
        [Array]::Copy($Bytes, 0, $useBytes, 0, $cap)
        $trunc = (' ...(原 ' + $Bytes.Length + ' 字节，只列前 ' + $cap + ')')
    }
    $printable = $true
    foreach ($b in $useBytes) { if ($b -lt 32 -and $b -ne 9 -and $b -ne 10 -and $b -ne 13) { $printable = $false; break } }
    if ($printable) {
        $txt = [System.Text.Encoding]::UTF8.GetString($useBytes)
        return ($txt + $trunc)
    }
    return ('(二进制) ' + (Format-HexBytes -Bytes $useBytes -Max $cap) + $trunc)
}

function Decode-MqttPacket {
    # 把 (Type, Flags, Body) 解成人能看的字段。返回 ordered dict。
    param([int]$Type, [int]$Flags, [byte[]]$Body)
    $d = [ordered]@{
        Kind = ''; Topic = ''; Payload = ''; PayloadHex = ''; PacketId = ''
        Dup = $false; QoS = -1; Retain = $false; Extra = ''
    }
    switch ($Type) {
        1  { $d.Kind = 'CONNECT' }
        2  {
            $d.Kind = 'CONNACK'
            if ($Body.Count -ge 2) { $d.Extra = ('sessionPresent={0} returnCode=0x{1:X2}' -f $Body[0], $Body[1]) }
        }
        3  {
            $d.Kind = 'PUBLISH'
            $d.Dup    = (($Flags -band 0x08) -ne 0)
            $d.QoS    = (($Flags -band 0x06) -shr 1)
            $d.Retain = (($Flags -band 0x01) -ne 0)
            $i = 0
            if ($Body.Count -ge 2) {
                $tl = ([int]$Body[0] * 256) + [int]$Body[1]
                $i = 2
                if (($i + $tl) -le $Body.Count) {
                    $d.Topic = [System.Text.Encoding]::UTF8.GetString($Body, $i, $tl)
                    $i += $tl
                } else {
                    $d.Topic = '(topic 长度越界)'
                    $i = $Body.Count
                }
            }
            if ($d.QoS -gt 0 -and ($i + 1) -lt $Body.Count) {
                $d.PacketId = (([int]$Body[$i] * 256) + [int]$Body[$i + 1])
                $i += 2
            }
            if ($i -le $Body.Count) {
                $pl = New-Object byte[] ($Body.Count - $i)
                if ($pl.Length -gt 0) { [Array]::Copy($Body, $i, $pl, 0, $pl.Length) }
                $d.PayloadHex = Format-HexBytes -Bytes $pl -Max 2048
                $d.Payload = ConvertTo-MqttPayloadText -Bytes $pl
            }
            $d.Extra = ('qos={0} retain={1} dup={2}' -f $d.QoS, $(if ($d.Retain) { '1' } else { '0' }), $(if ($d.Dup) { '1' } else { '0' }))
        }
        4  { $d.Kind = 'PUBACK' }
        8  {
            $d.Kind = 'SUBSCRIBE'
            if ($Body.Count -ge 2) { $d.PacketId = (([int]$Body[0] * 256) + [int]$Body[1]) }
        }
        9  {
            $d.Kind = 'SUBACK'
            if ($Body.Count -ge 2) {
                $d.PacketId = (([int]$Body[0] * 256) + [int]$Body[1])
                $codes = @()
                for ($k = 2; $k -lt $Body.Count; $k++) {
                    $cc = [int]$Body[$k]
                    if ($cc -eq 0x00) { $codes += '0x00(QoS0 OK)' }
                    elseif ($cc -eq 0x01) { $codes += '0x01(QoS1 OK)' }
                    elseif ($cc -eq 0x02) { $codes += '0x02(QoS2 OK)' }
                    elseif ($cc -eq 0x80) { $codes += '0x80(被拒)' }
                    else { $codes += ('0x{0:X2}' -f $cc) }
                }
                $d.Extra = ('granted=' + ($codes -join ','))
            }
        }
        12 { $d.Kind = 'PINGREQ' }
        13 { $d.Kind = 'PINGRESP' }
        14 { $d.Kind = 'DISCONNECT' }
        default { $d.Kind = ('类型' + $Type + '(未解析)') }
    }
    return $d
}

function Invoke-MqttWindow {
    # 一个抓包窗口：TCP -> CONNECT/CONNACK -> SUBSCRIBE(全部主题)/SUBACK -> 监听 Seconds 秒 -> DISCONNECT
    # 只订阅、只收包；【从不 PUBLISH】。
    param(
        [string]$ClientId,
        [string[]]$TopicFilters,
        [int]$Seconds,
        [int]$Port = 13688,
        [int]$RecvTimeoutMs = 1000,
        # MQTT keepalive（秒），与 LM 对齐用 3 秒。只影响我们发的 CONNECT / PINGREQ。
        [int]$KeepAlive = 3,
        # 显示用的窗口名（如 '第 1 窗'、'第 2 窗'），只影响倒计时那行的文字
        [string]$Label = '',
        # 是否允许机主在本窗监听期间按【回车】提前结束（纯 UI 开关，默认关）
        [switch]$AllowEnterStop,
        # ⭐ 监听窗【已经开始之后】才打印的一句提示（例如第 2 窗的"现在就去改灯"）。
        #   纯显示、不阻塞：绝不等待按键，窗口计时从进循环那一刻就已经在走。
        [string]$StartNotice = '',
        # ⭐ 允许"按回车提前结束"时，至少先跑满这么多秒才接受回车（防止误触/连按把窗口瞬间收掉）。
        #   只对显式开了 -AllowEnterStop 的窗口生效；第 1 窗与第 2 窗都用 5 秒。
        [int]$MinSecondsBeforeEnterStop = 5,
        # ⭐ 倒计时那行的"按回车"提示原文（可各窗不同；第 2 窗用带后果说明的那句）
        [string]$EnterStopTip = '按回车可提前结束本窗'
    )
    $r = [ordered]@{
        Ran = $true; ClientId = $ClientId; TcpOk = $false; Err = ''
        ConnAck = '未收到'; ConnAckCode = ''; SubAck = '未收到'; Granted = ''
        Events = (New-Object System.Collections.ArrayList)
        PublishCount = 0; PublishTopics = (New-Object System.Collections.ArrayList)
        SentCount = 0; RecvCount = 0; OtherCount = 0; StartedAt = (Get-Date)
        EndedAt = $null; PlannedSeconds = $Seconds; StoppedEarly = $false
        # ⭐ 诊断用：本窗因"正常读超时"继续轮询了多少次；以及本窗到底为什么结束（超时续轮询 vs 真断线）
        TimeoutPolls = 0; EndReason = ''; RecvTimeoutMs = $RecvTimeoutMs
        # 门槛内被忽略的回车次数（纯诊断）
        EnterIgnored = 0
    }
    $cli = $null; $st = $null
    try {
        $cli = New-Object System.Net.Sockets.TcpClient
        $cli.NoDelay = $true
        $iar = $cli.BeginConnect('127.0.0.1', $Port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne(4000, $false)) {
            $r.TcpOk = $false; $r.Err = 'TCP 连接 127.0.0.1:' + $Port + ' 超时（4 秒）——GCU/桥没在听？'
            return $r
        }
        $cli.EndConnect($iar)
        $r.TcpOk = $true
        $st = $cli.GetStream()
        $st.ReadTimeout = $RecvTimeoutMs
        $st.WriteTimeout = 3000

        # --- CONNECT（发出） ---
        # 本窗通信参数（与 LM 对齐）：MQTT 3.1.1、CleanSession=1、keepalive 见 -KeepAlive（默认 3 秒）。
        # ⭐ 凭据与 clientId 编号配套（UWPClient_N -> UWPClient_User_N / UWPClient_Pwd888881772688_N），
        #   依据 archive\核显省电抓包\gpu_saving_capture.py:77-88 的真实成功抓包；带凭据 => 连接标志 0xC2。
        # 现场教训：clientId 若不在 GCU 白名单（例如 _1），桥很可能直接忽略 => TCP 通但读 CONNACK 超时。
        $cred = Get-MqttCredentials -ClientId $ClientId
        $credUser = ''; $credPwd = ''
        if ($cred) { $credUser = [string]$cred.User; $credPwd = [string]$cred.Pwd }
        $c = New-MqttConnect -ClientId $ClientId -KeepAlive $KeepAlive -User $credUser -Password $credPwd
        # 产物里写 clientId + 用户名，密码只写长度（不把密码明文落到报告里；报文 hex 本来就含密码字节）。
        $credNote = $(if ($credUser) { '凭据=username:' + $credUser + '，password 长度 ' + $credPwd.Length + ' 字符（与 clientId 同一个编号）' } else { '无凭据（clientId 不是 UWPClient_<数字> 形态）' })
        [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '发 C->S'; Kind = 'CONNECT'; Topic = ''; Payload = ('clientId=' + $ClientId + ' keepalive=' + $KeepAlive + ' CleanSession=1 ' + $credNote); Extra = ''; Hex = (Format-HexBytes -Bytes $c -Max 512) })
        $r.SentCount++
        $st.Write($c, 0, $c.Length); $st.Flush()

        # --- CONNACK（收到） ---
        $pk = Read-MqttPacket -Stream $st
        if (-not $pk.Ok) {
            $r.ConnAck = ('读 CONNACK 失败/超时（' + $pk.Err + '）')
            $r.Err = 'CONNACK 未拿到 => 白名单 / clientId 被占 / 桥未就绪'
            [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = '(无报文)'; Topic = ''; Payload = $r.ConnAck; Extra = ''; Hex = '' })
            return $r
        }
        $r.RecvCount++
        $dd = Decode-MqttPacket -Type $pk.Type -Flags $pk.Flags -Body $pk.Body
        [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = $dd.Kind; Topic = $dd.Topic; Payload = (($dd.Extra + ' ' + $dd.Payload).Trim()); Extra = $dd.Extra; Hex = (Format-HexBytes -Bytes $pk.Raw -Max 512) })
        if ($pk.Type -ne 2) {
            $r.ConnAck = ('收到非 CONNACK 报文（' + $dd.Kind + '）')
            $r.Err = 'CONNACK 形状异常 => 需人工看'
            return $r
        }
        $rc = 0
        if ($pk.Body.Count -ge 2) { $rc = [int]$pk.Body[1] }
        $r.ConnAckCode = ('0x{0:X2}' -f $rc)
        if ($rc -eq 0) { $r.ConnAck = 'CONNACK rc=0x00 (Accepted)' }
        elseif ($rc -eq 1) { $r.ConnAck = 'CONNACK rc=0x01 (协议版本不支持)' }
        elseif ($rc -eq 2) { $r.ConnAck = 'CONNACK rc=0x02 (clientId 不合法)' }
        elseif ($rc -eq 4) { $r.ConnAck = 'CONNACK rc=0x04 (用户名/密码错)' }
        elseif ($rc -eq 5) { $r.ConnAck = 'CONNACK rc=0x05 (未授权)' }
        else { $r.ConnAck = ('CONNACK rc=0x{0:X2} (拒绝)' -f $rc) }
        if ($rc -ne 0) { $r.Err = 'broker 明确拒绝 CONNECT => 白名单/占用问题'; return $r }

        # --- SUBSCRIBE（发出，一个包里带全部主题） ---
        $s = New-MqttSubscribe -TopicFilters $TopicFilters -PacketId 1
        [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '发 C->S'; Kind = 'SUBSCRIBE'; Topic = (($TopicFilters) -join ', '); Payload = ('共 ' + $TopicFilters.Count + ' 个过滤器，QoS0'); Extra = ''; Hex = (Format-HexBytes -Bytes $s -Max 1024) })
        $r.SentCount++
        $st.Write($s, 0, $s.Length); $st.Flush()

        $sp = Read-MqttPacket -Stream $st
        if ($sp.Ok) {
            $r.RecvCount++
            $sd = Decode-MqttPacket -Type $sp.Type -Flags $sp.Flags -Body $sp.Body
            if ($sp.Type -eq 9) { $r.SubAck = '收到 SUBACK'; $r.Granted = $sd.Extra }
            else { $r.SubAck = ('收到非 SUBACK 报文（' + $sd.Kind + '）') }
            [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = $sd.Kind; Topic = $sd.Topic; Payload = (($sd.Extra + ' ' + $sd.Payload).Trim()); Extra = $sd.Extra; Hex = (Format-HexBytes -Bytes $sp.Raw -Max 1024) })
        } else {
            $r.SubAck = ('超时/断开，未收到 SUBACK（' + $sp.Err + '）')
            $r.Err = 'SUBACK 未拿到'
            [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = '(无报文)'; Topic = ''; Payload = $r.SubAck; Extra = ''; Hex = '' })
            return $r
        }

        # --- 监听窗 ---
        $listenStart = (Get-Date)
        $deadline = $listenStart.AddSeconds($Seconds)
        # ⭐ "按回车提前结束"的最短门槛：监听窗开始后至少 MinSecondsBeforeEnterStop 秒才接受回车
        $enterStopFrom = $listenStart.AddSeconds($MinSecondsBeforeEnterStop)
        # 保活间隔 = keepalive/2（下限 2 秒；keepalive=3 秒 => 每 2 秒一次 PINGREQ）。
        #   取舍：keepalive 按 LM 用 3 秒；若仍按原来 20 秒才发 PINGREQ，桥会按 1.5×keepalive 判本连接超时断开，
        #   12/40 秒的窗口里就收不到帧了。PINGREQ 只有 2 字节，只增加"保活"帧，不影响收到的报文集。
        $pingEverySec = [Math]::Max(2, [int][Math]::Ceiling($KeepAlive / 2.0))
        $nextPing = (Get-Date).AddSeconds($pingEverySec)
        $lastLeft = -1; $cdShown = $false; $noticeShown = $false
        while ((Get-Date) -lt $deadline) {
            # ⭐ 【窗口已经开始之后】才提示（纯显示、不阻塞、不等按键）——第 2 窗靠它告诉机主"现在就去改灯"，
            #   这样改灯动作落在窗口【里面】，不会再掉进两窗之间的空档。
            if (-not $noticeShown -and $StartNotice) {
                $noticeShown = $true
                Write-Host ''
                Write-Host ('  ⭐ ' + $StartNotice) -ForegroundColor Yellow
            }
            # --- 可见倒计时（纯 UI：只在秒数变化时刷一行，不碰收包/解码） ---
            $leftSec = [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
            if ($leftSec -lt 0) { $leftSec = 0 }
            if ($leftSec -ne $lastLeft) {
                $lastLeft = $leftSec
                $cdShown = $true
                # ⭐ 提示语：跑满最短门槛前明说"还不能回车收窗"，之后才给出该窗的正式提示原文。
                $tip = ''
                if ($AllowEnterStop) {
                    $gateLeft = [int][Math]::Ceiling(($enterStopFrom - (Get-Date)).TotalSeconds)
                    if ($gateLeft -gt 0) { $tip = ('  —— 还差 ' + $gateLeft + ' 秒才接受回车提前结束（防误触）') }
                    else { $tip = ('  —— ' + $EnterStopTip) }
                }
                Write-Host ("`r  {0}剩余 {1,3} 秒{2}      " -f $Label, $leftSec, $tip) -NoNewline -ForegroundColor DarkCyan
            }
            # --- 可选：机主按回车提前结束（只在显式开了 -AllowEnterStop 的窗口生效；
            #     ⭐ 且必须先跑满 MinSecondsBeforeEnterStop 秒，避免误触/连按把窗口瞬间收掉） ---
            if ($AllowEnterStop) {
                try {
                    if ([Console]::KeyAvailable) {
                        $k = [Console]::ReadKey($true)
                        if ($k.Key -eq [ConsoleKey]::Enter) {
                            if ((Get-Date) -ge $enterStopFrom) {
                                $r.StoppedEarly = $true
                                $r.EndReason = ('机主按回车提前结束本窗（已跑满最短门槛 ' + $MinSecondsBeforeEnterStop + ' 秒）')
                                break
                            } else {
                                # 门槛内按回车：忽略，但如实记一笔（不改计时、不收窗）
                                $r.EnterIgnored++
                            }
                        }
                    }
                } catch { }
            }
            if ((Get-Date) -ge $nextPing) {
                $pq = [byte[]]@(0xC0, 0x00)
                try {
                    $st.Write($pq, 0, 2); $st.Flush()
                    $r.SentCount++
                    [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '发 C->S'; Kind = 'PINGREQ'; Topic = ''; Payload = ('保活（keepalive ' + $KeepAlive + 's，每 ' + $pingEverySec + 's 一次，与 LM 的 3s keepalive 对齐）'); Extra = ''; Hex = 'C0 00' })
                } catch {
                    [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '发 C->S'; Kind = 'PINGREQ'; Topic = ''; Payload = ('发送失败：' + $_.Exception.Message); Extra = ''; Hex = 'C0 00' })
                    $r.Err = 'PINGREQ 发送失败 => 连接已坏'
                    $r.EndReason = '真断线：PINGREQ 发送失败（连接已坏）'
                    break
                }
                $nextPing = (Get-Date).AddSeconds($pingEverySec)
            }
            $pk2 = Read-MqttPacket -Stream $st
            if ($pk2.Ok) {
                $r.RecvCount++
                $d2 = Decode-MqttPacket -Type $pk2.Type -Flags $pk2.Flags -Body $pk2.Body
                if ($d2.Kind -eq 'PUBLISH') {
                    $r.PublishCount++
                    if ($d2.Topic) { [void]$r.PublishTopics.Add($d2.Topic) }
                } elseif ($d2.Kind -eq 'SUBACK' -or $d2.Kind -eq 'PINGRESP' -or $d2.Kind -eq 'CONNACK') {
                    # 正常控制报文
                } else {
                    $r.OtherCount++
                }
                [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = $d2.Kind; Topic = $d2.Topic; Payload = $d2.Payload; Extra = $d2.Extra; Hex = (Format-HexBytes -Bytes $pk2.Raw -Max 2048) })
                continue
            }
            if ($pk2.TimedOut) {
                # ⭐⭐ 本窗因"正常读超时"继续轮询 —— 这里【只计数 + continue】，绝不 break、绝不结束窗口。
                #   RecvTimeoutMs=1000 的空闲读超时是期望行为（两窗都要等满计划秒数），
                #   改前因为异常包装判定失效走到下面的"连接中断"分支，窗口被 1 秒空闲提前掐断。
                $r.TimeoutPolls++
                continue
            }
            [void]$r.Events.Add([ordered]@{ T = (Get-Stamp); Dir = '收 S->C'; Kind = '(连接中断)'; Topic = ''; Payload = ($pk2.Err + '  [ErrKind=' + $pk2.ErrKind + ']'); Extra = ''; Hex = '' })
            $r.Err = ('连接中断：' + $pk2.Err)
            $r.EndReason = ('真断线/对端关闭（ErrKind=' + $pk2.ErrKind + '）—— 如实结束本窗')
            break
        }
        if (-not $r.EndReason) { $r.EndReason = '窗口时间到（正常结束：期间读超时全部按 continue 轮询）' }
        if ($cdShown) { Write-Host '' }   # 给倒计时那一行收尾换行（纯显示）
    } catch {
        $r.Err = ('异常：' + $_.Exception.Message)
    } finally {
        try { if ($st) { $d = [byte[]]@(0xE0, 0x00); $st.Write($d, 0, 2); $st.Flush(); $r.SentCount++ } } catch { }
        try { if ($st) { $st.Dispose() } } catch { }
        try { if ($cli) { $cli.Close() } } catch { }
        $r.EndedAt = (Get-Date)
    }
    return $r
}

# ================================================================ 本机 P/Invoke 助手（HID + EC）
# 这一段是本脚本唯一的"接触硬件"的代码：只开 GENERIC_READ / 只发读 IOCTL。
#   HID：HidD_GetAttributes / HidD_GetPreparsedData / HidP_GetCaps（全只读）+ 一次 OVERLAPPED 超时读
#   EC ：IOCTL 2621482120（读）。写 IOCTL 2621482124 【故意不写进本脚本】。
$script:HidAvailable = $false
$script:EcAvailable  = $false
$script:PinvokeError = ''

$pinvokeCode = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LmCapLight
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead, IntPtr lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle hFile, ref OVERLAPPED lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIo(SafeFileHandle hFile);

        // ---- HID 接口路径枚举（GUID_DEVINTERFACE_HID = 4D1E55B2-F16F-11CF-88CB-001111000030） ----
        public static string[] EnumHidPaths()
        {
            Guid hidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) { return list.ToArray(); }
            try
            {
                SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                int idx = 0;
                while (SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, idx, ref did))
                {
                    idx++;
                    int req = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) { continue; }
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86
                        Marshal.WriteInt32(buf, (IntPtr.Size == 8) ? 8 : 6);
                        int req2 = 0;
                        if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req2, IntPtr.Zero))
                        {
                            string p = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                            if (!string.IsNullOrEmpty(p)) { list.Add(p); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list.ToArray();
        }

        private static string BytesToHex(byte[] b, int n)
        {
            if (b == null) { return ""; }
            if (n > b.Length) { n = b.Length; }
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) { s.Append(' '); }
                s.Append(b[i].ToString("X2"));
            }
            return s.ToString();
        }

        // ---- 打开一个 HID 接口，读属性 + caps + 试读一次输入报告（只读） ----
        public static string ProbeHid(string path, int timeoutMs)
        {
            SafeFileHandle h = CreateFileW(path, 0x80000000u /*GENERIC_READ*/, 3u /*share r|w*/, IntPtr.Zero, 3u /*OPEN_EXISTING*/, 0x40000000u /*FILE_FLAG_OVERLAPPED*/, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int e = Marshal.GetLastWin32Error();
                h.Dispose();
                return "openfail:" + e;
            }
            StringBuilder sb = new StringBuilder();
            try
            {
                HIDD_ATTRIBUTES a = new HIDD_ATTRIBUTES();
                a.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (HidD_GetAttributes(h, ref a))
                {
                    sb.AppendFormat("vid=0x{0:X4} pid=0x{1:X4} ver=0x{2:X4} ", a.VendorID, a.ProductID, a.VersionNumber);
                }
                else
                {
                    sb.Append("attrs=fail(" + Marshal.GetLastWin32Error() + ") ");
                }
                IntPtr pp = IntPtr.Zero;
                if (HidD_GetPreparsedData(h, out pp))
                {
                    try
                    {
                        HIDP_CAPS caps = new HIDP_CAPS();
                        caps.Reserved = new ushort[17];
                        int rc = HidP_GetCaps(pp, ref caps);
                        if (rc == 0x00110000) // HIDP_STATUS_SUCCESS
                        {
                            sb.AppendFormat("usagePage=0x{0:X4} usage=0x{1:X4} inLen={2} outLen={3} featLen={4} ", caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
                        }
                        else
                        {
                            sb.AppendFormat("caps=fail(0x{0:X8}) ", rc);
                        }
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                else
                {
                    sb.Append("preparsed=fail(" + Marshal.GetLastWin32Error() + ") ");
                }

                // 试读一次输入报告（OVERLAPPED + 超时；超时即 CancelIo，绝不阻塞）
                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                OVERLAPPED ov = new OVERLAPPED();
                ov.hEvent = ev;
                byte[] buf = new byte[64];
                bool ok = ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 997) // ERROR_IO_PENDING
                    {
                        CancelIo(h);
                        sb.Append("read=fail(" + err + ")");
                        return sb.ToString();
                    }
                    uint w = WaitForSingleObject(ev, (uint)timeoutMs);
                    if (w != 0)
                    {
                        CancelIo(h);
                        WaitForSingleObject(ev, 200);
                        sb.Append("read=timeout(" + timeoutMs + "ms 内没有输入报告)");
                        return sb.ToString();
                    }
                    int tr = 0;
                    if (!GetOverlappedResult(h, ref ov, out tr, false))
                    {
                        sb.Append("read=fail2(" + Marshal.GetLastWin32Error() + ")");
                        return sb.ToString();
                    }
                    sb.Append("read=ok:" + tr + ":" + BytesToHex(buf, tr));
                    return sb.ToString();
                }
                int got = 0;
                GetOverlappedResult(h, ref ov, out got, true);
                if (got <= 0) { got = buf.Length; }
                sb.Append("read=ok:" + got + ":" + BytesToHex(buf, got));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                sb.Append("exception:" + ex.GetType().Name + ":" + ex.Message);
                return sb.ToString();
            }
            finally
            {
                try { h.Dispose(); } catch { }
            }
        }

        // ---- EC 只读：\\.\ACPIDriver + IOCTL 2621482120 ----
        // res[0] = "ok" 或 "openfail:<win32err>"；res[i+1] = "ok:<byte0>:<16字节hex>" 或 "ioctlfail:<err>"
        public static string[] ReadEc(int[] addrs)
        {
            string[] res = new string[addrs.Length + 1];
            SafeFileHandle h = CreateFileW("\\\\.\\ACPIDriver", 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int e = Marshal.GetLastWin32Error();
                h.Dispose();
                res[0] = "openfail:" + e;
                for (int i = 0; i < addrs.Length; i++) { res[i + 1] = ""; }
                return res;
            }
            res[0] = "ok";
            try
            {
                for (int i = 0; i < addrs.Length; i++)
                {
                    byte[] inb = BitConverter.GetBytes((uint)addrs[i]);
                    byte[] outb = new byte[16];
                    int ret = 0;
                    bool ok = DeviceIoControl(h, 2621482120u, inb, 4, outb, 16, out ret, IntPtr.Zero);
                    if (!ok)
                    {
                        res[i + 1] = "ioctlfail:" + Marshal.GetLastWin32Error();
                    }
                    else
                    {
                        res[i + 1] = "ok:" + outb[0] + ":" + BytesToHex(outb, 16);
                    }
                }
            }
            catch (Exception ex)
            {
                res[0] = "exception:" + ex.Message;
            }
            finally
            {
                try { h.Dispose(); } catch { }
            }
            return res;
        }
    }
}
'@

try {
    Add-Type -TypeDefinition $pinvokeCode -Language CSharp -ErrorAction Stop
    $script:HidAvailable = $true
    $script:EcAvailable  = $true
} catch {
    $script:PinvokeError = $_.Exception.Message
    Warn ('P/Invoke 助手编译失败（HID 与 EC 两节都会写"做不到"，其余通道不受影响）：' + $script:PinvokeError)
}

# ================================================================ 自测模式（不连网/不读 EC/不动系统）
if ($SelfTest) {
    Write-Host ''
    Write-Host '==== 自测模式（-SelfTest）：只验证 MQTT 解码链路，不连网、不读 EC、不枚举 HID ====' -ForegroundColor Yellow
    $fails = New-Object System.Collections.ArrayList
    $ok = New-Object System.Collections.ArrayList

    # 1) 造一个 QoS0 PUBLISH：topic=Keyboard/Ctrl
    $topic = 'Keyboard/Ctrl'
    $pl = [System.Text.Encoding]::UTF8.GetBytes('{"Action":"SET_LED","Color":"FF0000"}')
    $tb = [System.Text.Encoding]::UTF8.GetBytes($topic)
    $b1 = New-Object System.Collections.Generic.List[byte]
    $b1.Add([byte](($tb.Length -shr 8) -band 0xFF)); $b1.Add([byte]($tb.Length -band 0xFF)); $b1.AddRange($tb); $b1.AddRange($pl)
    $p1 = New-MqttFixedPacket -First ([byte]0x30) -Body $b1.ToArray()
    $d1 = Decode-MqttPacket -Type 3 -Flags 0x00 -Body ($p1[2..($p1.Length - 1)])
    if ($d1.Topic -eq $topic -and $d1.Payload -eq '{"Action":"SET_LED","Color":"FF0000"}' -and $d1.QoS -eq 0) {
        [void]$ok.Add(('QoS0 PUBLISH 解出 topic=' + $d1.Topic + ' payload=' + $d1.Payload))
    } else {
        [void]$fails.Add(('QoS0 PUBLISH 解码错误：topic=' + $d1.Topic + ' payload=' + $d1.Payload))
    }

    # 2) 造一个 QoS1 PUBLISH（带 packetId）
    $b2 = New-Object System.Collections.Generic.List[byte]
    $b2.Add([byte](($tb.Length -shr 8) -band 0xFF)); $b2.Add([byte]($tb.Length -band 0xFF)); $b2.AddRange($tb)
    $b2.Add(0x00); $b2.Add(0x2A)   # packetId = 42
    $b2.AddRange([System.Text.Encoding]::UTF8.GetBytes('PING'))
    $d2 = Decode-MqttPacket -Type 3 -Flags 0x02 -Body $b2.ToArray()
    if ($d2.QoS -eq 1 -and $d2.PacketId -eq 42 -and $d2.Payload -eq 'PING') {
        [void]$ok.Add('QoS1 PUBLISH 解出 packetId=42 payload=PING')
    } else {
        [void]$fails.Add(('QoS1 PUBLISH 解码错误：qos=' + $d2.QoS + ' pid=' + $d2.PacketId + ' payload=' + $d2.Payload))
    }

    # 3) retain 位 + 二进制载荷
    $b3 = New-Object System.Collections.Generic.List[byte]
    $b3.Add([byte](($tb.Length -shr 8) -band 0xFF)); $b3.Add([byte]($tb.Length -band 0xFF)); $b3.AddRange($tb)
    $b3.AddRange([byte[]]@(0x01, 0x02, 0xFF))
    $d3 = Decode-MqttPacket -Type 3 -Flags 0x01 -Body $b3.ToArray()
    if ($d3.Retain -and $d3.Payload -like '(二进制)*') {
        [void]$ok.Add('retain 位与二进制载荷识别正确')
    } else {
        [void]$fails.Add(('retain/二进制判定错误：retain=' + $d3.Retain + ' payload=' + $d3.Payload))
    }

    # 4) SUBACK 解码
    $d4 = Decode-MqttPacket -Type 9 -Flags 0 -Body ([byte[]]@(0x00, 0x01, 0x00, 0x80))
    if ($d4.Kind -eq 'SUBACK' -and $d4.PacketId -eq 1 -and $d4.Extra -match '0x80') {
        [void]$ok.Add('SUBACK 解出 granted 列表：' + $d4.Extra)
    } else {
        [void]$fails.Add('SUBACK 解码错误：' + $d4.Extra)
    }

    # 5) ⭐ CONNECT 报文【字节级自检】（本次修的核心：协议名长度 00 04 + 配套凭据）：
    #    拼包 -> 打印 hex -> 用 Get-MqttConnectFields 按字段解回来逐段验证长度自洽。
    $credU3 = Get-MqttCredentials -ClientId 'UWPClient_3'
    $credWanted = ($null -ne $credU3)
    $expUser = $(if ($credWanted) { [string]$credU3.User } else { '' })
    $expPwd  = $(if ($credWanted) { [string]$credU3.Pwd } else { '' })
    $c = New-MqttConnect -ClientId 'UWPClient_3' -KeepAlive 3 -User $expUser -Password $expPwd
    $f = Get-MqttConnectFields -Packet $c
    $expFlags = $(if ($expUser -and $expPwd) { 0xC2 } else { 0x02 })
    Write-Host ''
    Write-Host ('  CONNECT hex（clientId=UWPClient_3 keepalive=3）：' + (Format-HexBytes -Bytes $c -Max 512)) -ForegroundColor DarkGray
    Write-Host ('  按字段解回：协议名长度=' + $f.ProtoNameLen + ' 协议名=' + $f.ProtoName + ' 级别=' + $f.Level + ' flags=0x' + ('{0:X2}' -f $f.Flags) + ' keepalive=' + $f.KeepAlive + ' clientId=' + $f.ClientId + ' username=' + $f.User + ' password=' + $f.Pwd + ' 剩余长度=' + $f.RemLen + '（' + $f.RemLenBytes + ' 字节编码）总长=' + $f.Total) -ForegroundColor DarkGray
    $connOk = ($f.Ok -and $f.ProtoNameLen -eq 4 -and $f.ProtoName -eq 'MQTT' -and $f.Level -eq 4 -and
               $f.Flags -eq $expFlags -and $f.KeepAlive -eq 3 -and $f.ClientId -eq 'UWPClient_3' -and
               $f.User -eq $expUser -and $f.Pwd -eq $expPwd -and
               $c[2] -eq 0x00 -and $c[3] -eq 0x04 -and
               $f.RemLen -eq ($c.Length - 1 - $f.RemLenBytes))
    if ($connOk) {
        [void]$ok.Add(('CONNECT 字节级自检通过：协议名长度 00 04 在位、协议名 MQTT、级别 4、flags 0x{0:X2}、keepalive 3、clientId=UWPClient_3、username/password 与 _3 配套、剩余长度 {1} 与总长 {2} 自洽' -f $f.Flags, $f.RemLen, $c.Length))
    } else {
        [void]$fails.Add('CONNECT 字节级自检未通过：' + $(if ($f.Err) { $f.Err } else { '字段与期望不符' }) + '  hex=' + (Format-HexBytes -Bytes $c -Max 96))
    }
    # 5b) 不带凭据时的兜底形态（自定义 clientId）：协议名长度仍在、flags=0x02
    $c2 = New-MqttConnect -ClientId 'UWPClient_1' -KeepAlive 3
    $f2 = Get-MqttConnectFields -Packet $c2
    if ($f2.Ok -and $f2.Flags -eq 0x02 -and $f2.ClientId -eq 'UWPClient_1' -and $c2[2] -eq 0x00 -and $c2[3] -eq 0x04) {
        [void]$ok.Add('CONNECT 无凭据形态自检通过（flags=0x02、协议名长度 00 04 仍在）')
    } else {
        [void]$fails.Add('CONNECT 无凭据形态自检未通过：' + $(if ($f2.Err) { $f2.Err } else { 'flags 或协议名长度不对' }))
    }
    # 5c) 凭据配套关系自检：UWPClient_N 必须配 UWPClient_User_N / UWPClient_Pwd888881772688_N
    $credMapOk = $true
    foreach ($n in @(1, 2, 3, 4)) {
        $cr = Get-MqttCredentials -ClientId ('UWPClient_' + $n)
        if (-not $cr -or [string]$cr.User -ne ('UWPClient_User_' + $n) -or [string]$cr.Pwd -ne ('UWPClient_Pwd888881772688_' + $n)) { $credMapOk = $false }
    }
    if ($credMapOk -and ($null -eq (Get-MqttCredentials -ClientId 'KeyboardProbe'))) {
        [void]$ok.Add('凭据配套自检通过：_1/_2/_3/_4 四组都与 clientId 同编号（依据 gpu_saving_capture.py:77-88），非白名单形态不带凭据')
    } else {
        [void]$fails.Add('凭据配套自检未通过：四组凭据或非白名单形态判定不对')
    }

    # 6) SUBSCRIBE 报文：多过滤器一个包
    $s = New-MqttSubscribe -TopicFilters @('Keyboard/Ctrl', 'HidLightbar/Ctrl') -PacketId 7
    $rl2 = Get-MqttRemLen -Data $s -Start 1
    if ($s[0] -eq 0x82 -and $rl2.Len -eq ($s.Length - 2)) {
        [void]$ok.Add('SUBSCRIBE 报文构造正确（一个包里带多个过滤器）')
    } else {
        [void]$fails.Add('SUBSCRIBE 构造异常')
    }

    # 7) 零写自检：脚本【代码行】里不得出现 EC 写 IOCTL 字面量（注释/说明里出现不算）
    $selfText = [System.IO.File]::ReadAllText($PSCommandPath, [System.Text.UTF8Encoding]::new($false))
    $codeText = Get-CodeText -Text $selfText
    $readNeedle = '2621482' + '120u'
    if ($codeText -match [regex]::Escape($script:EcWriteNeedle)) {
        [void]$fails.Add('零写自检未通过：代码里出现了 EC 写 IOCTL 2621482124')
    } else {
        [void]$ok.Add('零写自检通过：代码行里不含 EC 写 IOCTL 2621482124（只有读 ' + ($readNeedle -replace 'u$','') + '）')
    }
    # 8) 零发布自检：不得有 PUBLISH 组包函数；且 socket 写入只能有 4 处（CONNECT/SUBSCRIBE/PINGREQ/DISCONNECT）
    if ($codeText -match [regex]::Escape($script:PubBuilderNeedle)) {
        [void]$fails.Add('零发布自检未通过：代码里出现了 PUBLISH 组包函数')
    } else {
        $writeLines = @()
        $ln = 0
        foreach ($line in ($selfText -split "`r?`n")) {
            $ln++
            if ($line -match '\$st\.Write\(') { $writeLines += ('L' + $ln) }
        }
        if ($writeLines.Count -eq 4) {
            [void]$ok.Add('零发布自检通过：没有 PUBLISH 组包函数；socket 写入恰好 4 处（' + ($writeLines -join '/') + '）= CONNECT/SUBSCRIBE/PINGREQ/DISCONNECT')
        } else {
            [void]$fails.Add('零发布自检需人工看：socket 写入处为 ' + $writeLines.Count + ' 处（期望 4）：' + ($writeLines -join ', '))
        }
    }

    Write-Host ''
    foreach ($o in $ok)    { Write-Host ('  [OK]   ' + $o) -ForegroundColor Green }
    foreach ($f in $fails) { Write-Host ('  [FAIL] ' + $f) -ForegroundColor Red }
    Write-Host ''
    Write-Host ('  自测结果：OK=' + $ok.Count + ' FAIL=' + $fails.Count + '（P/Invoke 助手编译：' + $(if ($script:HidAvailable) { '成功' } else { '失败：' + $script:PinvokeError }) + '）')
    Write-Host ''
    if ($fails.Count -gt 0) { exit 1 } else { exit 0 }
}

# ================================================================ 00 准备（输出目录 / 管理员）
Step '准备：输出目录 / 权限 / 环境' | Out-Null
$isAdmin = Test-Admin

if (-not $OutDir -or $OutDir.Trim().Length -eq 0) {
    $desk = Join-Path $env:USERPROFILE 'Desktop'
    if (-not (Test-Path -LiteralPath $desk)) { $desk = $env:USERPROFILE }
    $OutDir = Join-Path $desk ('LM-官方控灯抓包-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
try { Ensure-Dir -Path $OutDir } catch {
    Write-Host ('  ! 输出目录建不出来：' + $OutDir + '（' + $_.Exception.Message + '）') -ForegroundColor Red
    exit 1
}

Write-Host ('  脚本版本 : ' + $SCRIPT_VERSION)
Write-Host ('  输出目录 : ' + $OutDir)
Write-Host ('  管理员   : ' + $(if ($isAdmin) { '是' } else { '否（EC 读取会失败；HID 原始报告可能读不到）' })) -ForegroundColor $(if ($isAdmin) { 'Gray' } else { 'Yellow' })
Write-Host ('  P/Invoke : ' + $(if ($script:HidAvailable) { 'HID/EC 助手编译成功' } else { '编译失败：' + $script:PinvokeError }))

$P = [ordered]@{
    Model = ''; BaseBoard = ''; Bios = ''; OsName = ''; OsBuild = ''; Uptime = ''
    ProjectID = ''; BioProjectId = ''; KeyboardType = ''; KeyboardSupport = ''; RgbLightbarSupport = ''
    HidTotal = 0; HidSuspect = 0; HidProbeTried = 0; HidProbeOk = 0
    Mqtt1 = '未跑'; Mqtt2 = '未跑'; Mqtt1Pub = 0; Mqtt2Pub = 0
    EcVerdict = '未跑'; Ec0740 = ''; Ec0766 = ''
    EcPreVerdict = '未跑'; EcPre0740 = ''; EcPre0766 = ''
    LogCount = 0; LogKeyLines = 0; DebugEnable = '(键不存在)'
    RegDiffLines = 0; Direction = '证据不足'
}

try { $P.Model = [string](Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).Model } catch { }
try { $P.BaseBoard = [string](Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue).Product } catch { }
try { $P.Bios = [string](Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue).SMBIOSBIOSVersion } catch { }
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
    if ($os) {
        $P.OsName = [string]$os.Caption
        $P.OsBuild = [string]$os.BuildNumber
        try {
            $up = (Get-Date) - $os.LastBootUpTime
            $P.Uptime = ('{0}天{1}小时{2}分' -f $up.Days, $up.Hours, $up.Minutes)
        } catch { }
    }
} catch { }

# ================================================================ 01 注册表快照（控灯前）
Step '01 注册表快照（控灯前，只读全树）' | Out-Null

function Get-RegDump {
    param([string]$Root)
    $lines = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $stack.Push($Root)
    while ($stack.Count -gt 0) {
        $k = [string]$stack.Pop()
        try { $key = Get-Item -LiteralPath $k -ErrorAction Stop } catch { continue }
        [void]$lines.Add(('[' + $k + ']'))
        try {
            foreach ($n in ($key.GetValueNames() | Sort-Object)) {
                $show = $n
                if ($show -eq '') { $show = '(默认)' }
                $kind = ''
                try { $kind = [string]$key.GetValueKind($n) } catch { $kind = '?' }
                $v = $null
                try { $v = $key.GetValue($n, $null, 'DoNotExpandEnvironmentNames') } catch { $v = $null }
                $vs = ''
                if ($v -is [byte[]]) { $vs = ('HEX(' + (Format-HexBytes -Bytes $v -Max 512) + ')') }
                elseif ($v -is [string[]]) { $vs = ('MULTI[' + ($v -join ' | ') + ']') }
                elseif ($null -eq $v) { $vs = '(null)' }
                else { $vs = [string]$v }
                [void]$lines.Add(('  ' + $show + ' : ' + $kind + ' = ' + $vs))
            }
        } catch { [void]$lines.Add('  (值枚举失败：' + $_.Exception.Message + ')') }
        try {
            foreach ($sk in ($key.GetSubKeyNames() | Sort-Object)) { $stack.Push(($k.TrimEnd('\') + '\' + $sk)) }
        } catch { }
    }
    return $lines
}

$regRoots = @(
    @{ Path = 'HKLM:\SOFTWARE\OEM\GamingCenter2'; Label = 'HKLM\SOFTWARE\OEM\GamingCenter2' },
    @{ Path = 'HKLM:\SOFTWARE\L-Mechrevo';       Label = 'HKLM\SOFTWARE\L-Mechrevo' }
)
$regA = [ordered]@{}
$snapA = New-Object System.Text.StringBuilder
[void]$snapA.AppendLine('01 注册表快照（控灯前 / 只读全树）')
[void]$snapA.AppendLine('============================================================')
[void]$snapA.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
[void]$snapA.AppendLine('本文件是只读快照，脚本没有写任何注册表值。')
[void]$snapA.AppendLine('')
foreach ($rr in $regRoots) {
    $exists = Test-Path -LiteralPath $rr.Path
    [void]$snapA.AppendLine('== ' + $rr.Label + ' ==')
    if (-not $exists) {
        [void]$snapA.AppendLine('  (整个键不存在)')
        $regA[$rr.Label] = @()
        [void]$snapA.AppendLine('')
        continue
    }
    $dump = Get-RegDump -Root $rr.Path
    $regA[$rr.Label] = @($dump)
    foreach ($l in $dump) { [void]$snapA.AppendLine(('  ' + $l)) }
    [void]$snapA.AppendLine('')
}
# 三个关键值单独醒目列出（判定 30 系键盘类型的判据）
[void]$snapA.AppendLine('== 三个关键值（判定 30 系键盘类型用）==')
foreach ($kv in @(
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2'; N = 'ProjectID'; L = 'GamingCenter2\ProjectID' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'BIOS_PROJECT_ID'; L = 'ItemSupport\BIOS_PROJECT_ID' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'KeyboardType'; L = 'ItemSupport\KeyboardType' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'KeyboardSupport'; L = 'ItemSupport\KeyboardSupport' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'RGBLightbarSupport'; L = 'ItemSupport\RGBLightbarSupport' }
)) {
    $val = '(读不到)'
    try {
        if (Test-Path -LiteralPath $kv.P) {
            $o = Get-ItemProperty -LiteralPath $kv.P -Name $kv.N -ErrorAction Stop
            $val = [string]$o.($kv.N)
        }
    } catch { $val = '(读不到)' }
    [void]$snapA.AppendLine(('  ' + $kv.L + ' = ' + $val))
    if ($kv.L -eq 'GamingCenter2\ProjectID') { $P.ProjectID = $val }
    if ($kv.L -eq 'ItemSupport\BIOS_PROJECT_ID') { $P.BioProjectId = $val }
    if ($kv.L -eq 'ItemSupport\KeyboardType') { $P.KeyboardType = $val }
    if ($kv.L -eq 'ItemSupport\KeyboardSupport') { $P.KeyboardSupport = $val }
    if ($kv.L -eq 'ItemSupport\RGBLightbarSupport') { $P.RgbLightbarSupport = $val }
}
# GCU 日志开关当前状态（只读）
[void]$snapA.AppendLine('')
[void]$snapA.AppendLine('== HKLM\SOFTWARE\OEM\GamingCenterService\Debug（日志开关，只读）==')
try {
    $dbgPath = 'HKLM:\SOFTWARE\OEM\GamingCenterService\Debug'
    if (Test-Path -LiteralPath $dbgPath) {
        $o = Get-ItemProperty -LiteralPath $dbgPath -ErrorAction SilentlyContinue
        foreach ($n in ($o.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })) {
            [void]$snapA.AppendLine(('  ' + $n.Name + ' = ' + [string]$n.Value))
            if ($n.Name -eq 'enable') { $P.DebugEnable = [string]$n.Value }
        }
        if ($P.DebugEnable -eq '(键不存在)') { $P.DebugEnable = '(enable 值不存在)' }
    } else {
        [void]$snapA.AppendLine('  (整个键不存在) => 日志门控默认关闭')
        $P.DebugEnable = '(整个键不存在)'
    }
} catch { [void]$snapA.AppendLine('  (读取失败：' + $_.Exception.Message + ')') }
Write-Utf8 -Path (Join-Path $OutDir '01-注册表快照-控灯前.txt') -Text $snapA.ToString()
Write-Host ('  -> 注册表（控灯前）已写入；ProjectID=' + $P.ProjectID + ' BIOS_PROJECT_ID=' + $P.BioProjectId + ' KeyboardType=' + $P.KeyboardType + ' Debug.enable=' + $P.DebugEnable) -ForegroundColor Green

# ================================================================ 02 HID 设备枚举
Step '02 HID 设备枚举（全部 HID/PnP，含 VID/PID，只读）' | Out-Null

# VID -> 厂商（只列与"键盘/灯效"有关的常见值，够标注用）
$script:VidTable = @{
    '3554' = 'ITE Tech（机械革命/Uniwill 机型上极常见：键盘/灯效 HID 控制器）'
    '048D' = 'ITE Tech'
    '04F2' = 'Chicony（键盘/摄像头）'
    '04D9' = 'Holtek'
    '258A' = 'SINO WEALTH（键盘）'
    '1A2C' = 'China Resource Semico（键盘）'
    '3151' = 'PixArt'
    '0B05' = 'ASUS'
    '1532' = 'Razer'
    '046D' = 'Logitech'
    '05AC' = 'Apple'
    '045E' = 'Microsoft'
    '0C45' = 'Sonix'
    '0BDA' = 'Realtek'
    '8087' = 'Intel'
    '17EF' = 'Lenovo'
    '413C' = 'Dell'
    '27C6' = 'Goodix'
    '06CB' = 'Synaptics'
    '04CA' = 'Lite-On'
    '0955' = 'NVIDIA'
    '1D6B' = 'Linux Foundation'
    '05E3' = 'Genesys Logic'
    '0424' = 'Microchip'
}

$hidRows = New-Object System.Collections.ArrayList
try {
    $devs = @()
    try {
        $devs = @(Get-PnpDevice -ErrorAction Stop | Where-Object {
            $_.Class -eq 'HIDClass' -or ($_.InstanceId -match '^HID\\') -or ($_.InstanceId -match '^USB\\.*MI_')
        })
    } catch {
        $devs = @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object {
            $_.PNPClass -eq 'HIDClass' -or ($_.DeviceID -match '^HID\\') -or ($_.DeviceID -match '^USB\\.*MI_')
        } | ForEach-Object {
            [pscustomobject]@{ Status = $_.Status; Class = $_.PNPClass; FriendlyName = $_.Name; InstanceId = $_.DeviceID }
        })
    }
    foreach ($d in $devs) {
        $iid = [string]$d.InstanceId
        $vid = ''; $pidv = ''
        $m = [regex]::Match($iid, '(?i)VID_([0-9A-F]{4})')
        if ($m.Success) { $vid = $m.Groups[1].Value.ToUpper() }
        $m2 = [regex]::Match($iid, '(?i)PID_([0-9A-F]{4})')
        if ($m2.Success) { $pidv = $m2.Groups[1].Value.ToUpper() }
        $fn = [string]$d.FriendlyName
        $reasons = New-Object System.Collections.ArrayList
        if ($vid -eq '3554') { [void]$reasons.Add('VID=3554(ITE) 重点') }
        elseif ($script:VidTable.ContainsKey($vid)) { [void]$reasons.Add('厂商=' + $script:VidTable[$vid]) }
        if ($fn -match '(?i)键盘|keyboard') { [void]$reasons.Add('名称含键盘') }
        if ($fn -match '(?i)RGB|灯|lightbar|背光') { [void]$reasons.Add('名称含灯效') }
        if ($fn -match '(?i)供应商定义|vendor') { [void]$reasons.Add('供应商定义集合(常见于 RGB/灯控)') }
        if ($fn -match '(?i)consumer|用户控制') { [void]$reasons.Add('Consumer Control') }
        if ($iid -match '(?i)^HID\\VID_3554') { [void]$reasons.Add('HID\VID_3554(ITE 顶层集合)') }
        [void]$hidRows.Add([pscustomobject]@{
            Status = [string]$d.Status; Class = [string]$d.Class; Vid = $vid; Pid = $pidv
            Name = $fn; InstanceId = $iid; Why = ($reasons -join '；')
            Suspect = ($reasons.Count -gt 0)
        })
    }
} catch {
    Warn ('HID 枚举失败：' + $_.Exception.Message)
}

$P.HidTotal = $hidRows.Count
$P.HidSuspect = @($hidRows | Where-Object { $_.Suspect }).Count
$hsb = New-Object System.Text.StringBuilder
[void]$hsb.AppendLine('02 HID 设备枚举（只读；枚举用 Get-PnpDevice，失败回退 Win32_PnPEntity）')
[void]$hsb.AppendLine('============================================================')
[void]$hsb.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
[void]$hsb.AppendLine('本节只枚举，不打开、不写入。打开与试读在 03。')
[void]$hsb.AppendLine('')
[void]$hsb.AppendLine('== 可能是键盘 / 灯效设备的（高嫌疑，先看这些）==')
$susp = @($hidRows | Where-Object { $_.Suspect })
if ($susp.Count -eq 0) { [void]$hsb.AppendLine('  (没有命中任何关键词)') }
foreach ($r in $susp) {
    [void]$hsb.AppendLine(('  [' + $r.Status + '] ' + $r.Name))
    [void]$hsb.AppendLine(('      VID=' + $r.Vid + ' PID=' + $r.Pid + '  Class=' + $r.Class))
    [void]$hsb.AppendLine(('      为什么可疑：' + $r.Why))
    [void]$hsb.AppendLine(('      InstanceId：' + $r.InstanceId))
}
[void]$hsb.AppendLine('')
[void]$hsb.AppendLine('== 其余 HID 设备（全量，共 ' + $P.HidTotal + ' 个）==')
[void]$hsb.AppendLine('  Status | VID | PID | Class | FriendlyName | InstanceId')
foreach ($r in ($hidRows | Sort-Object Vid, Pid, Name)) {
    [void]$hsb.AppendLine(('  ' + $r.Status + ' | ' + $r.Vid + ' | ' + $r.Pid + ' | ' + $r.Class + ' | ' + $r.Name + ' | ' + $r.InstanceId))
}
[void]$hsb.AppendLine('')
[void]$hsb.AppendLine('== 关键词命中统计 ==')
foreach ($kw in @('键盘', 'Keyboard', 'RGB', '供应商定义', 'Consumer', '用户控制', '系统控制器', '鼠标', 'Mouse', '触摸板')) {
    $n = @($hidRows | Where-Object { $_.Name -match ('(?i)' + [regex]::Escape($kw)) }).Count
    [void]$hsb.AppendLine(('  ' + $kw + ' : ' + $n))
}
Write-Utf8 -Path (Join-Path $OutDir '02-HID设备枚举.txt') -Text $hsb.ToString()
Write-Host ('  -> HID 设备 ' + $P.HidTotal + ' 个，其中高嫌疑（键盘/灯效）' + $P.HidSuspect + ' 个') -ForegroundColor Green
if ($P.HidSuspect -gt 0) {
    foreach ($r in ($susp | Select-Object -First 8)) { Write-Host ('     * ' + $r.Vid + ':' + $r.Pid + '  ' + $r.Name + '   <- ' + $r.Why) }
}

# ================================================================ 03 HID 只读探测（UsagePage/Usage + 一次输入报告）
$hidProbeText = New-Object System.Text.StringBuilder
[void]$hidProbeText.AppendLine('03 HID 只读探测（打开 GENERIC_READ + 读 caps + 试读一次输入报告）')
[void]$hidProbeText.AppendLine('============================================================')
[void]$hidProbeText.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
[void]$hidProbeText.AppendLine('只做三件事：① 打开接口（只请求 GENERIC_READ）② 读 HID 描述能力（只读 IOCTL）')
[void]$hidProbeText.AppendLine('   ③ 用 OVERLAPPED + 超时试读【一次】输入报告。绝不向任何 HID 设备写一个字节。')
[void]$hidProbeText.AppendLine('')

if ($SkipHid) {
    Step '03 HID 只读探测（按参数 -SkipHid 跳过）' | Out-Null
    [void]$hidProbeText.AppendLine('按参数 -SkipHid 跳过。')
    Write-Host '  -> 已按 -SkipHid 跳过。'
} elseif (-not $script:HidAvailable) {
    Step '03 HID 只读探测（做不到：P/Invoke 助手未编译）' | Out-Null
    [void]$hidProbeText.AppendLine('做不到：本机 P/Invoke 助手（hid.dll / setupapi.dll）编译失败，无法枚举 HID 接口与读 caps。')
    [void]$hidProbeText.AppendLine('  失败原因：' + $script:PinvokeError)
    [void]$hidProbeText.AppendLine('  替代方案：见本文件末尾"做不到的点与替代方案"。')
    Write-Host '  ! HID 只读探测做不到（P/Invoke 编译失败）' -ForegroundColor Yellow
} else {
    Step '03 HID 只读探测（UsagePage/Usage/上报长度 + 试读一次）' | Out-Null
    $paths = @()
    try { $paths = @([LmCapLight.Native]::EnumHidPaths()) } catch { Warn ('HID 接口枚举失败：' + $_.Exception.Message) }
    [void]$hidProbeText.AppendLine(('HID 接口（GUID_DEVINTERFACE_HID）共 ' + $paths.Count + ' 个。'))
    [void]$hidProbeText.AppendLine('')
    $limit = 24
    $perTimeout = 300
    $deadline = (Get-Date).AddSeconds([Math]::Max(2, $HidProbeSeconds))
    $idx = 0
    $records = New-Object System.Collections.ArrayList
    foreach ($pth in $paths) {
        if ($idx -ge $limit) { [void]$hidProbeText.AppendLine(('(已达单次上限 ' + $limit + ' 个接口，其余略)')); break }
        if ((Get-Date) -ge $deadline) { [void]$hidProbeText.AppendLine(('(已达本节时间预算 ' + $HidProbeSeconds + ' 秒，其余略)')); break }
        $idx++
        $P.HidProbeTried++
        $res = ''
        try { $res = [string]([LmCapLight.Native]::ProbeHid($pth, $perTimeout)) } catch { $res = ('exception:' + $_.Exception.Message) }
        $up = [regex]::Match($res, 'usagePage=0x([0-9A-F]{4}) usage=0x([0-9A-F]{4})')
        $tag = ''
        if ($up.Success) {
            switch ($up.Groups[1].Value) {
                '0006' { $tag = '键盘顶层集合（Generic Desktop / Keyboard）' }
                '000C' { $tag = 'Consumer Control（多媒体键）' }
                '0001' { $tag = 'Generic Desktop（鼠标/其他）' }
                'FF00' { $tag = '重点：供应商定义集合（UsagePage=0xFF00，RGB/灯控常在这里）' }
                default { if ($up.Groups[1].Value -match '^FF') { $tag = '重点：供应商定义集合' } }
            }
        }
        if ($res -match 'read=ok') { $P.HidProbeOk++ }
        [void]$records.Add([pscustomobject]@{ Path = $pth; Res = $res; Tag = $tag })
    }
    [void]$hidProbeText.AppendLine('== 逐个接口结果 ==')
    foreach ($r in $records) {
        [void]$hidProbeText.AppendLine(('  ' + $r.Path))
        [void]$hidProbeText.AppendLine(('      ' + $r.Res + $(if ($r.Tag) { '   ' + $r.Tag } else { '' })))
    }
    [void]$hidProbeText.AppendLine('')
    [void]$hidProbeText.AppendLine('== 小结 ==')
    [void]$hidProbeText.AppendLine(('  尝试接口 ' + $P.HidProbeTried + ' 个；其中能成功读到一次输入报告的 ' + $P.HidProbeOk + ' 个。'))
    $vendor = @($records | Where-Object { $_.Tag -match 'FF|供应商' })
    [void]$hidProbeText.AppendLine(('  UsagePage=0xFF00 这类"供应商定义集合"命中 ' + $vendor.Count + ' 个 —— ' + $(if ($vendor.Count -gt 0) { '这些是最像灯控通道的接口。' } else { '没命中（可能灯控不走 HID，或需要 USBPcap 才能在总线层看到）。' })))
    Write-Host ('  -> HID 接口 ' + $paths.Count + ' 个；试读 ' + $P.HidProbeTried + ' 个，成功 ' + $P.HidProbeOk + ' 个；供应商定义集合 ' + $vendor.Count + ' 个') -ForegroundColor Green
}
[void]$hidProbeText.AppendLine('')
[void]$hidProbeText.AppendLine('== 做不到的点与替代方案（必须如实说明）==')
[void]$hidProbeText.AppendLine('  1) Windows 用户态【没有】"抓 HID 输出报告（Output Report）"的公开只读接口。')
[void]$hidProbeText.AppendLine('     键位灯/灯带的写命令通常走 HID SET_REPORT(Output) 或 Feature Report —— 那是"主机->设备"，')
[void]$hidProbeText.AppendLine('     本脚本（以及任何用户态程序）都只能写、不能旁听；所以【做不到】，必须用总线层抓包。')
[void]$hidProbeText.AppendLine('  2) 本脚本尝试读的是"输入报告（Input Report）"，只能看到设备->主机方向：')
[void]$hidProbeText.AppendLine('     如果官方控制台改了灯、设备回了个报告，本节可能看得到；但控制台"发出去的"命令看不到。')
[void]$hidProbeText.AppendLine('  3) 替代方案（优先级从高到低）：')
[void]$hidProbeText.AppendLine('     a) USBPcap + Wireshark：装 USBPcap，Wireshark 选 USBPcap1/2 抓，')
[void]$hidProbeText.AppendLine('        过滤 usb.control / usbhid；它能同时看到 SET_REPORT(Output) 与 Feature，')
[void]$hidProbeText.AppendLine('        是唯一能看到"写"的办法。')
[void]$hidProbeText.AppendLine('     b) Windows 内置 pktmon 只能抓网络，抓不了 USB；Process Monitor 只能看到驱动调用，')
[void]$hidProbeText.AppendLine('        看不到帧内容。')
[void]$hidProbeText.AppendLine('     c) 若厂商把灯控放在 HID 之外的通道（例如走 ACPI/EC 或 MQTT），那 USBPcap 也抓不到 ——')
[void]$hidProbeText.AppendLine('        这时要看 04~09：MQTT 帧、EC 0x740/0x766、GCU 日志、注册表前后差异。')
Write-Utf8 -Path (Join-Path $OutDir '03-HID只读探测.txt') -Text $hidProbeText.ToString()

# ================================================================ MQTT 握手失败的分类 / 告警（屏幕红字 + 产物）
# 现场教训：原来失败后约 1 秒就默默结束，使用者会误以为"抓到了"。所以失败必须【讲清楚】：
#   屏幕红字告警 + 写进 04/06 节顶部与 00-概要.txt + 给出排查提示。仍然【快速失败，不做长时间重试】。
function Get-MqttHandshakeClass {
    # 把一窗的结果分成互斥的几类（三种失败【分开写】，不再混成一句"失败"）：
    #   ''                => 握手成功（TCP 通 + CONNACK rc=0x00 + SUBACK 到手）
    #   'tcp'             => ① TCP 连不上（端口不可达 / 桥没在听）
    #   'connack-timeout' => ② TCP 已连上，但读 CONNACK 超时（一条报文都没收到）
    #   'connack-rc'      => ③ 收到 CONNACK 但 return code != 0
    #   'suback'          => CONNACK 成功但没拿到 SUBACK（订阅没建立）
    #   'other'           => 其它（收到非 CONNACK 报文 / 本窗未运行）
    param($Win)
    if ($null -eq $Win) { return 'other' }
    if (-not $Win.TcpOk) { return 'tcp' }
    if ($Win.ConnAckCode -eq '0x00') {
        if ([string]$Win.SubAck -like '超时/断开*') { return 'suback' }
        return ''
    }
    if ($Win.ConnAckCode) { return 'connack-rc' }
    if ([string]$Win.ConnAck -like '读 CONNACK 失败*') { return 'connack-timeout' }
    return 'other'
}

function Get-MqttFailShort {
    # 告警标题里的短原因（成功返回 ''）。'读 CONNACK 超时' 就是真实现场那一类。
    param($Win)
    switch (Get-MqttHandshakeClass -Win $Win) {
        ''                { return '' }
        'tcp'             { return 'TCP 连接失败' }
        'connack-timeout' { return '读 CONNACK 超时' }
        'connack-rc'      { return 'CONNACK return code≠0' }
        'suback'          { return '未收到 SUBACK' }
        default           { return '握手未完成' }
    }
}

function Get-MqttFailReason {
    # 人话版原因（成功返回 ''）：三种情况分开写清。
    param($Win)
    switch (Get-MqttHandshakeClass -Win $Win) {
        ''                { return '' }
        'tcp'             { return ('① TCP 连不上 127.0.0.1:' + $Port + '（端口不可达 / GCU 桥没在听）') }
        'connack-timeout' { return '② TCP 已连上，但读 CONNACK 超时（未收到任何 CONNACK 报文）' }
        'connack-rc'      { return ('③ 收到 CONNACK 但 return code≠0（' + [string]$Win.ConnAck + '）=> broker 拒绝：白名单 / 占用问题') }
        'suback'          { return ('CONNACK 已成功但未收到 SUBACK（' + [string]$Win.SubAck + '）=> 订阅没建立，本窗同样没有报文') }
        default           { return ('握手未完成：CONNACK=' + [string]$Win.ConnAck) }
    }
}

function Get-MqttTryHintBlock {
    # 排查提示（屏幕与产物共用同一份文字，避免两处说法不一致）。
    param()
    $t = New-Object System.Text.StringBuilder
    [void]$t.AppendLine('  排查提示（逐条照做）：')
    [void]$t.AppendLine('    ① 确认 GCU / 官方控制台正在运行：GCUBridge / GCUService 在跑，官方控制台能打开、能改灯；')
    [void]$t.AppendLine('    ② ⚠️ 先关闭 L-Mechrevo：它会占用 clientId 白名单 UWPClient_4 / UWPClient_3 / UWPClient_2，')
    [void]$t.AppendLine('       被占就会连不上或读不到 CONNACK（本脚本已按 _4 -> _3 -> _2 串行回退逐个试过）；')
    [void]$t.AppendLine('    ③ 查 127.0.0.1:13688 是否在监听：netstat -ano | findstr 13688；')
    [void]$t.AppendLine('    ④ 保证 CONNECT 用的 clientId 在白名单里（_4 / _3 / _2）：白名单外的名字（例如 _1）')
    [void]$t.AppendLine('       会出现"TCP 连上但永远读不到 CONNACK"，与本次现场完全吻合；')
    [void]$t.AppendLine('    ⑤ 本脚本【不做长时间重试】：每个 clientId 只试一次、失败即停；处理完上面几点后重跑。')
    return $t.ToString()
}

function Get-MqttFailText {
    # 写进产物的整块文字："本窗无有效数据 + 失败原因（分类）+ 排查提示"。
    # 握手成功时返回 ''（产物理由：成功就不需要这段告警）。
    param($Win, [string]$Label = '本窗')
    $short = Get-MqttFailShort -Win $Win
    if (-not $short) { return '' }
    $cls = Get-MqttHandshakeClass -Win $Win
    $t = New-Object System.Text.StringBuilder
    [void]$t.AppendLine('################################################################')
    [void]$t.AppendLine('[警告] 未能与 127.0.0.1:' + $Port + ' 完成 MQTT 握手（' + $short + '）——本次没有抓到任何报文')
    [void]$t.AppendLine('  ' + $Label + '【无有效数据】：' + (Get-MqttFailReason -Win $Win))
    [void]$t.AppendLine('  失败分类（三种情况分开写，不再混成一句"失败"）：')
    [void]$t.AppendLine('    ① TCP 连不上（端口不可达 / 桥没在听）：' + $(if ($cls -eq 'tcp') { '★ 本窗就是这一类' } else { '否' }))
    [void]$t.AppendLine('    ② TCP 已连上，但读 CONNACK 超时（未收到任何 CONNACK）：' + $(if ($cls -eq 'connack-timeout') { '★ 本窗就是这一类（TCP 通 => 端口在听，桥收到 CONNECT 却不回 CONNACK）' } else { '否' }))
    [void]$t.AppendLine('    ③ 收到 CONNACK 但 return code≠0（broker 明确拒绝）：' + $(if ($cls -eq 'connack-rc') { '★ 本窗就是这一类' } else { '否' }))
    [void]$t.AppendLine('  TCP 连接   ：' + $(if ($Win.TcpOk) { '成功（127.0.0.1:' + $Port + '）' } else { '失败/超时' }))
    [void]$t.AppendLine('  CONNACK    ：' + [string]$Win.ConnAck + $(if ($Win.ConnAckCode) { '  码=' + $Win.ConnAckCode } else { '' }))
    [void]$t.AppendLine('  SUBACK     ：' + [string]$Win.SubAck)
    [void]$t.AppendLine('  收到的 PUBLISH：' + $Win.PublishCount + ' 条（本窗没有可分析的数据）')
    if ($Win.AttemptedIds) { [void]$t.AppendLine('  尝试过的 clientId（串行、每个只试一次）：' + [string]$Win.AttemptedIds + '；最后使用 = ' + [string]$Win.ClientId) }
    [void]$t.AppendLine('  白名单两难提示：若官方控制台自己把 _4/_3/_2 全占了，可以【先关掉官方控制台】再抓，')
    [void]$t.AppendLine('                 但那样就没法"边改灯边抓"——要不要这么做由机主决定。')
    [void]$t.Append((Get-MqttTryHintBlock))
    [void]$t.AppendLine('################################################################')
    [void]$t.AppendLine('')
    return $t.ToString()
}

function Write-MqttFailAlert {
    # 屏幕醒目告警（红字）+ 记入 $script:Warnings（最终会写进 00-概要.txt 的失败/告警清单）。
    param($Win, [string]$Label = '本窗')
    $short = Get-MqttFailShort -Win $Win
    if (-not $short) { return }
    Write-Host ''
    Write-Host '  ################################################################' -ForegroundColor Red
    Write-Host ('  [警告] 未能与 127.0.0.1:' + $Port + ' 完成 MQTT 握手（' + $short + '）——本次没有抓到任何报文') -ForegroundColor Red
    Write-Host ('  ' + $Label + '【无有效数据】：' + (Get-MqttFailReason -Win $Win)) -ForegroundColor Red
    Write-Host ('  窗口结果：TCP=' + $(if ($Win.TcpOk) { '成功' } else { '失败/超时' }) + ' / CONNACK=' + [string]$Win.ConnAck + ' / SUBACK=' + [string]$Win.SubAck + ' / PUBLISH=' + $Win.PublishCount + ' 条') -ForegroundColor Red
    if ($Win.AttemptedIds) { Write-Host ('  尝试过的 clientId（串行、每个只试一次）：' + [string]$Win.AttemptedIds) -ForegroundColor Red }
    foreach ($ln in ((Get-MqttTryHintBlock) -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Write-Host $ln -ForegroundColor Yellow } }
    Write-Host '  ################################################################' -ForegroundColor Red
    Write-Host ''
    Warn ($Label + '：未与 127.0.0.1:' + $Port + ' 完成 MQTT 握手（' + $short + '）——本窗没有抓到任何报文，详见 04/06 节顶部与 00-概要.txt')
}

# ================================================================ 04/06 MQTT 两个窗口
function Write-MqttSection {
    param([string]$Title, [string]$Path, $Win, [string]$Note)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine($Title)
    [void]$sb.AppendLine('============================================================')
    # 握手失败：先把"本窗无有效数据 + 失败原因 + 排查提示"放在本节【顶部】（红字告警的产物版）。
    $failBlock = Get-MqttFailText -Win $Win -Label '本窗'
    if ($failBlock) { [void]$sb.Append($failBlock) }
    [void]$sb.AppendLine('本脚本【只 SUBSCRIBE、从不 PUBLISH】。下面每一条都带时间戳与方向（发 C->S / 收 S->C）。')
    [void]$sb.AppendLine('方向标"收 S->C"的 PUBLISH 就是 GCU 桥（或官方控制台经桥）发出来的帧 —— 这才是我们要找的东西。')
    [void]$sb.AppendLine('')
    if ($Note) { [void]$sb.AppendLine($Note); [void]$sb.AppendLine('') }
    [void]$sb.AppendLine(('使用的 clientId ：' + $Win.ClientId))
    $credW = Get-MqttCredentials -ClientId ([string]$Win.ClientId)
    $credTxt = $(if ($credW) { 'username=' + [string]$credW.User + ' + password（与 clientId 同编号；依据 archive\核显省电抓包\gpu_saving_capture.py:77-88 的真实成功抓包）' } else { '无凭据（clientId 不是 UWPClient_<数字> 形态）' })
    [void]$sb.AppendLine(('CONNECT 参数    ：MQTT 3.1.1（协议级别 4）、CleanSession=1、keepalive=' + $MQTT_KEEPALIVE + ' 秒（与 LM 的 WithKeepAlivePeriod(3s) 对齐）、' + $credTxt))
    [void]$sb.AppendLine('CONNECT 拼包    ：10 <remlen> | 00 04 M Q T T | 04 | <flags> | <keepalive 2B> | 00 <len> clientId [00 <len> username] [00 <len> password]（2026-10-09 已修：原来漏了协议名长度 00 04，broker 把 4D 51 当长度 => 不回 CONNACK）')
    [void]$sb.AppendLine(('保活间隔        ：每 ' + $MQTT_PING_SECONDS + ' 秒一个 PINGREQ（keepalive/2；只发保活帧，不影响收到的报文集）'))
    $connEv = @($Win.Events | Where-Object { [string]$_.Kind -eq 'CONNECT' } | Select-Object -First 1)
    if ($connEv -and $connEv.Hex) {
        [void]$sb.AppendLine('我方发出的 CONNECT 原始字节（hex，便于与 LM 抓包逐字节对照）：')
        [void]$sb.AppendLine('  ' + [string]$connEv.Hex)
        [void]$sb.AppendLine('  提示：变长头是 00 04 M Q T T 04 <flags>；载荷里 00 0B + 后面 11 字节 ASCII 就是 clientId（UWPClient_4 -> 55 57 50 43 6C 69 65 6E 74 5F 34；_3 结尾 5F 33；_2 结尾 5F 32），')
        [void]$sb.AppendLine('       其后紧跟 00 <len> + username（UWPClient_User_N）与 00 <len> + password（UWPClient_Pwd888881772688_N），三段长度各自与总长自洽。')
    }
    [void]$sb.AppendLine(('订阅的 topic    ：' + (($Topics) -join ', ')))
    [void]$sb.AppendLine(('TCP 连接        ：' + $(if ($Win.TcpOk) { '成功（127.0.0.1:' + $Port + '）' } else { '失败/超时' })))
    [void]$sb.AppendLine(('CONNACK         ：' + $Win.ConnAck + $(if ($Win.ConnAckCode) { '  码=' + $Win.ConnAckCode } else { '' })))
    [void]$sb.AppendLine(('SUBACK          ：' + $Win.SubAck + $(if ($Win.Granted) { '  ' + $Win.Granted } else { '' })))
    if ($Win.Err) { [void]$sb.AppendLine(('错误/说明       ：' + $Win.Err)) }
    if ($Win.StartedAt -and $Win.EndedAt) { [void]$sb.AppendLine(('窗口起止        ：' + $Win.StartedAt.ToString('HH:mm:ss.fff') + ' -> ' + $Win.EndedAt.ToString('HH:mm:ss.fff'))) }
    if ($Win.PlannedSeconds) {
        $realSec = 0
        if ($Win.StartedAt -and $Win.EndedAt) { $realSec = [Math]::Round(($Win.EndedAt - $Win.StartedAt).TotalSeconds, 1) }
        [void]$sb.AppendLine(('本窗时长        ：计划 ' + $Win.PlannedSeconds + ' 秒，实际 ' + $realSec + ' 秒' + $(if ($Win.StoppedEarly) { '（机主按回车提前结束）' } else { '' }) + $(if ([int]$Win.EnterIgnored -gt 0) { '（另有 ' + [int]$Win.EnterIgnored + ' 次回车落在最短门槛内、已忽略）' } else { '' })))
    }
    # ⭐ 窗口健康度诊断（改窗口 bug 时加的）：把"读超时继续轮询"和"因断线结束"分开写清楚，
    #   以后一眼就能看出窗口是被计划时长正常收掉的，还是被真断线掐断的。
    [void]$sb.AppendLine(('读超时续轮询   ：' + [int]$Win.TimeoutPolls + ' 次（每次 = 空闲 ' + $(if ($Win.RecvTimeoutMs) { [int]$Win.RecvTimeoutMs } else { 1000 }) + 'ms 无数据；⭐ 这是正常的空窗等待，不会结束窗口）'))
    [void]$sb.AppendLine(('本窗结束原因   ：' + $(if ($Win.EndReason) { $Win.EndReason } else { '（未记录；本窗在拿到 CONNACK/SUBACK 前就退出了）' })))
    [void]$sb.AppendLine(('计数            ：发包 ' + $Win.SentCount + ' / 收包 ' + $Win.RecvCount + '；其中 PUBLISH ' + $Win.PublishCount + ' 条；其它 ' + $Win.OtherCount + ' 条'))
    if ($Win.PublishTopics.Count -gt 0) {
        $uniq = @($Win.PublishTopics | Select-Object -Unique)
        [void]$sb.AppendLine(('PUBLISH 出现过的 topic（去重）：' + ($uniq -join ' | ')))
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('== 全帧时序（时间戳 | 方向 | 类型 | topic | payload / 说明）==')
    if ($Win.Events.Count -eq 0) {
        [void]$sb.AppendLine('  (一条报文都没有)')
    } else {
        foreach ($e in $Win.Events) {
            [void]$sb.AppendLine(('  ' + $e.T + ' | ' + ([string]$e.Dir).PadRight(7) + ' | ' + ([string]$e.Kind).PadRight(12) + ' | ' + $(if ($e.Topic) { $e.Topic } else { '-' }) + ' | ' + [string]$e.Payload))
        }
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('== 只看 PUBLISH（含 payload 原文，便于与我们发的帧逐字比对）==')
    $pubs = @($Win.Events | Where-Object { $_.Kind -eq 'PUBLISH' })
    if ($pubs.Count -eq 0) {
        [void]$sb.AppendLine('  本次窗口没有收到任何 PUBLISH。')
        [void]$sb.AppendLine('  => 若官方控制台此时确实在控灯，说明它【不是通过 13688 上的 MQTT 控灯的】，')
        [void]$sb.AppendLine('     那就重点看 03（HID/USB 总线）、07（EC）、08（GCU 日志）。')
    } else {
        $n = 0
        foreach ($e in $pubs) {
            $n++
            [void]$sb.AppendLine(('  #' + $n + '  ' + $e.T))
            [void]$sb.AppendLine(('      topic  ：' + $e.Topic))
            [void]$sb.AppendLine(('      payload：' + [string]$e.Payload))
            if ($e.Extra) { [void]$sb.AppendLine(('      标志   ：' + $e.Extra)) }
            if ($e.Hex) { [void]$sb.AppendLine(('      原始   ：' + $e.Hex)) }
        }
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('== 原始字节（全部收发报文，hex，供事后逐字节比对）==')
    if ($Win.Events.Count -eq 0) {
        [void]$sb.AppendLine('  (无)')
    } else {
        foreach ($e in $Win.Events) {
            [void]$sb.AppendLine(('  ' + $e.T + ' ' + $e.Dir + ' ' + [string]$e.Kind + ' ' + $(if ($e.Topic) { $e.Topic } else { '-' })))
            [void]$sb.AppendLine(('      ' + [string]$e.Hex))
        }
    }
    Write-Utf8 -Path $Path -Text $sb.ToString()
}

# ================================================================ EC 只读采样（只读；绝不写 EC）
# 提取成函数，是为了在【MQTT 第 1 窗之前】先采一次"控灯前"读数（00b 节），
# 第 2 窗之后再采"控灯后"读数（07 节）=> 报告里 EC 前后两次都在，能做对比。
# ⛔ 只读：只调用 Native::ReadEc（内部只发读 IOCTL 2621482120）；写 IOCTL 2621482124 在本脚本里根本不存在。
function Format-EcPair {
    # 把 (0x740, 0x766, verdict) 拼成一行"读数对"；读不到就如实写"做不到"，绝不伪造数值。
    param([string]$V740, [string]$V766, [string]$Verdict)
    if ($V740 -or $V766) {
        return ('0x740=' + $(if ($V740) { $V740 } else { '(未读到)' }) + '  0x766=' + $(if ($V766) { $V766 } else { '(未读到)' }))
    }
    return ('未读到（' + $Verdict + '）')
}

function Read-EcSample {
    # 一次 EC 只读采样：\\.\ACPIDriver + 读 IOCTL 2621482120，读 0x740 / 0x741 / 0x766 / 0x767。
    # 返回 ordered：Text（整节文本）/ Title / Phase / Ok / Verdict / V0740 / V0766
    param([string]$Title, [string]$Phase = '')
    $ec = New-Object System.Text.StringBuilder
    $verdict = '未跑'; $v740 = ''; $v766 = ''; $okRead = $false
    [void]$ec.AppendLine($Title)
    [void]$ec.AppendLine('============================================================')
    [void]$ec.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
    [void]$ec.AppendLine(('采样阶段：' + $Phase + $(if ($Phase -eq '控灯前') { '（MQTT 第 1 窗【之前】采的，用于和 07 的"控灯后"做前后对比）' } else { '（MQTT 两窗都结束之后采的）' })))
    [void]$ec.AppendLine('这条采样【只读】：只发读 IOCTL 2621482120；脚本里没有写 IOCTL 2621482124。非管理员读不到就如实写"做不到"。')
    [void]$ec.AppendLine('')
    [void]$ec.AppendLine('== 这条通道是什么 ==')
    [void]$ec.AppendLine('  设备路径 ：\\.\ACPIDriver')
    [void]$ec.AppendLine('  打开方式 ：CreateFile(GENERIC_READ|GENERIC_WRITE, share=READ|WRITE, OPEN_EXISTING)')
    [void]$ec.AppendLine('  读 IOCTL ：2621482120（0x9C07D2D0，IOCTL_GPD_ACPI_ECREAD），输入 4 字节地址，输出 16 字节')
    [void]$ec.AppendLine('  写 IOCTL 2621482124（0x9C07D2D4）【本脚本里根本没有】，绝不写 EC。')
    [void]$ec.AppendLine('')
    [void]$ec.AppendLine('== 为什么读这两个寄存器 ==')
    [void]$ec.AppendLine('  * 0x740(1856) = ProjectID。GCU 侧 LM_Init() 的 EC 路径就是：')
    [void]$ec.AppendLine('      读 0x740 取 ProjectID -> 命中"单色键盘白名单"则硬性 return false -> 否则再读 0x766。')
    [void]$ec.AppendLine('  * 0x766(1894) = 键盘灯判据寄存器。判据是 (值 & 4) == 4：满足才是"支持 RGB 键盘灯"。')
    [void]$ec.AppendLine('  => 这两个值就是我们要的答案：0x740 决定机型走哪条路，0x766&4 决定灯效能不能初始化。')
    [void]$ec.AppendLine('  * 相邻的 0x741 / 0x767 一并读，用于排除"16 位小端/相邻字节"布局的误判（只读对照）。')
    [void]$ec.AppendLine('')

    if ($SkipEc) {
        [void]$ec.AppendLine('按参数 -SkipEc 跳过。')
        $verdict = '已跳过'
        Write-Host ('  -> EC ' + $Phase + '：已按 -SkipEc 跳过。')
    } elseif (-not $script:EcAvailable) {
        [void]$ec.AppendLine('做不到：P/Invoke 助手编译失败，无法调用 DeviceIoControl。')
        [void]$ec.AppendLine('  失败原因：' + $script:PinvokeError)
        [void]$ec.AppendLine('  替代方案：用仓库里现成的 tools\read_power_input_ec.py 思路（同样走 2621482120），')
        [void]$ec.AppendLine('            或直接用 L-Mechrevo 自己的 EC 读取通道（Probe.dll 的 AcpiDriverReadTransport）。')
        $verdict = '做不到（P/Invoke 编译失败）'
        Write-Host ('  ! EC ' + $Phase + ' 读取做不到（P/Invoke 编译失败）') -ForegroundColor Yellow
    } else {
        $addrs = @(0x740, 0x741, 0x766, 0x767)
        $names = @('0x740 (1856) ProjectID', '0x741 (1857) 相邻对照', '0x766 (1894) 键盘灯判据', '0x767 (1895) 相邻对照')
        $res = @()
        try { $res = @([LmCapLight.Native]::ReadEc([int[]]$addrs)) } catch { $res = @('exception:' + $_.Exception.Message) }
        if ($res.Count -lt 1) { $res = @('exception:未返回结果') }
        if ($res[0] -ne 'ok') {
            $why = '未知'
            if ($res[0] -match '^openfail:(\d+)$') {
                $code = [int]$Matches[1]
                switch ($code) {
                    2    { $why = 'ERROR_FILE_NOT_FOUND(2)：\\.\ACPIDriver 设备不存在 => 驱动（UWACPIDriver/ACPIDriver）没装上或没起来' }
                    5    { $why = 'ERROR_ACCESS_DENIED(5)：权限不足 => 【请以管理员身份重跑本脚本】' }
                    32   { $why = 'ERROR_SHARING_VIOLATION(32)：被别的进程独占 => 稍后再试' }
                    default { $why = ('Win32 错误码 ' + $code) }
                }
            } elseif ($res[0] -match '^exception:(.+)$') { $why = '异常：' + $Matches[1] }
            [void]$ec.AppendLine('打开 \\.\ACPIDriver 失败，本次 EC 读取【做不到】：')
            [void]$ec.AppendLine('  原始返回：' + $res[0])
            [void]$ec.AppendLine('  判因    ：' + $why)
            [void]$ec.AppendLine('')
            [void]$ec.AppendLine('  替代方案：')
            [void]$ec.AppendLine('   a) 以【管理员身份】重跑本脚本（权限不足是最常见原因）；')
            [void]$ec.AppendLine('   b) 确认设备存在：Get-PnpDevice | ? InstanceId -like "*ACPI\INOU0000*"（应显示 ACPIDriver / Status=OK）；')
            [void]$ec.AppendLine('   c) 确认驱动服务：类型为内核驱动，且 System32\drivers 下有 UWACPIDriver.sys 或 ACPIDriver.sys；')
            [void]$ec.AppendLine('   d) 本机（40 系）与目标机（30 系）都有这个设备 —— 只要 GCU 装过就会绑 ACPI\INOU0000。')
            $verdict = '做不到：' + $res[0]
            Write-Host ('  ! EC ' + $Phase + ' 读取做不到：' + $res[0] + '  ' + $why) -ForegroundColor Yellow
        } else {
            [void]$ec.AppendLine('== 读取结果 ==')
            for ($i = 0; $i -lt $addrs.Count; $i++) {
                $v = $res[$i + 1]
                [void]$ec.AppendLine(('  ' + $names[$i]))
                if ($v -match '^ok:(\d+):(.+)$') {
                    $b0 = [int]$Matches[1]
                    $b0hex = ('0x{0:X2}' -f $b0)
                    [void]$ec.AppendLine(('      值 = ' + $b0 + '  (' + $b0hex + ')'))
                    [void]$ec.AppendLine(('      16 字节原始输出：' + $Matches[2]))
                    if ($addrs[$i] -eq 0x740) { $v740 = $b0hex }
                    if ($addrs[$i] -eq 0x766) { $v766 = $b0hex }
                } else {
                    [void]$ec.AppendLine(('      读取失败：' + $v))
                }
            }
            [void]$ec.AppendLine('')
            [void]$ec.AppendLine('== 判读 ==')
            if ($v766 -match '^0x([0-9A-F]{2})$') {
                $b = [Convert]::ToInt32($Matches[1], 16)
                $bit = ($b -band 4)
                [void]$ec.AppendLine(('  0x766 = ' + $v766 + ' => (值 & 4) = ' + $bit + ' => ' + $(if ($bit -eq 4) { '满足判据（value & 4 == 4）：EC 认为本机【支持 RGB 键盘灯】' } else { '【不满足】判据（value & 4 != 4）：EC 认为本机不支持 RGB 键盘灯 => 与 LM 侧 solution=UNKNOWN 的现象一致！' })))
            }
            [void]$ec.AppendLine(('  0x740 = ' + $v740 + '（ProjectID，交给研发比对"单色键盘白名单"）'))
            [void]$ec.AppendLine('')
            [void]$ec.AppendLine('  口径：0x766&4 == 4 且 0x740 未命中单色白名单 => EC 这条路应该能初始化；')
            [void]$ec.AppendLine('        任一条不满足 => 30 系灯效识别失败的根因可能就在 EC 这条路上。')
            $verdict = ('成功：0x740=' + $v740 + ' 0x766=' + $v766)
            $okRead = $true
            Write-Host ('  -> EC ' + $Phase + ' 读取成功：0x740=' + $v740 + '  0x766=' + $v766) -ForegroundColor Green
        }
    }
    return [ordered]@{ Text = $ec.ToString(); Title = $Title; Phase = $Phase; Ok = $okRead; Verdict = $verdict; V0740 = $v740; V0766 = $v766 }
}

# ================================================================ 00b EC 只读采样（控灯前）——必须在 MQTT 第 1 窗之前
Step '00b EC 只读采样（控灯前：MQTT 第 1 窗之前；0x740 / 0x741 / 0x766 / 0x767）' | Out-Null
$ecPre = Read-EcSample -Title '00b EC 读数 · 控灯前（MQTT 第 1 窗【之前】采的）' -Phase '控灯前'
Write-Utf8 -Path (Join-Path $OutDir '00b-EC读数-控灯前.txt') -Text $ecPre.Text
$P.EcPreVerdict = $ecPre.Verdict; $P.EcPre0740 = $ecPre.V0740; $P.EcPre0766 = $ecPre.V0766
Write-Host '  -> 00b-EC读数-控灯前.txt 已写入（这是"控灯前"读数；"控灯后"读数在 07 节，00-概要里两次都在）。' -ForegroundColor Green

# ⭐ 00-概要 要标注"某一窗是否被机主按回车提前结束"，所以先给两个窗变量兜底（-SkipMqtt 时保持 $null）
$win1 = $null; $win2 = $null
if ($SkipMqtt) {
    Step '04/06 MQTT 全帧抓包（按参数 -SkipMqtt 跳过）' | Out-Null
    Write-Host '  -> 已按 -SkipMqtt 跳过。'
    $P.Mqtt1 = '已跳过'; $P.Mqtt2 = '已跳过'
} else {
    # ---- 04 第 1 窗（基线） ----
    Step ('04 MQTT 全帧抓包 · 第 1 窗（控灯前基线，' + $BaselineSeconds + ' 秒）') | Out-Null
    Write-Host ('  这 ' + $BaselineSeconds + ' 秒是【基线】：你可以先去把官方控制台打开、翻到键盘灯页面，') -ForegroundColor Yellow
    Write-Host '  ⚠️ 但【先不要改灯】（现在改灯就把基线弄脏了）。' -ForegroundColor Yellow
    Write-Host '  基线只要证明"不改灯时没有灯效帧"，背景流量几秒就够，所以这窗很短，不用干等。' -ForegroundColor DarkGray
    Write-Host '  下面会显示"剩余 N 秒"；如果想马上进入下一窗，按一下【回车】即可提前结束本窗。' -ForegroundColor DarkGray
    $win1 = $null
    $tried1 = @()
    foreach ($cid in $ClientIds) {
        Write-Host ('  -> 尝试 clientId=' + $cid + '（白名单 _4 -> _3 -> _2 串行回退；只 CONNECT + SUBSCRIBE，绝不 PUBLISH）')
        # 串行：上一次 Invoke-MqttWindow 在自己的 finally 里已经把连接关掉了，这里才连下一个（绝不并发占用多个）。
        $try = Invoke-MqttWindow -ClientId $cid -TopicFilters $Topics -Seconds $BaselineSeconds -Port $Port -Label '第 1 窗' -AllowEnterStop -KeepAlive $MQTT_KEEPALIVE
        $win1 = $try
        $tried1 += $cid
        if ($try.TcpOk -and $try.ConnAckCode -eq '0x00') { break }
        # 连不上/被拒：换下一个白名单 id 再试一次，但【每个 id 只试一次，不做死循环】
        Start-Sleep -Milliseconds 150
    }
    if ($null -eq $win1) {
        $win1 = [ordered]@{ Ran = $false; ClientId = ''; TcpOk = $false; Err = 'clientId 列表为空'; ConnAck = '未跑'; ConnAckCode = ''; SubAck = '未跑'; Granted = ''; Events = (New-Object System.Collections.ArrayList); PublishCount = 0; PublishTopics = (New-Object System.Collections.ArrayList); SentCount = 0; RecvCount = 0; OtherCount = 0; StartedAt = (Get-Date); EndedAt = (Get-Date) }
    }
    $win1.AttemptedIds = ($tried1 -join ' -> ')
    $P.Mqtt1 = $(if ($win1.TcpOk -and $win1.ConnAckCode -eq '0x00') { 'CONNACK 成功' } else { '连接失败/' + $win1.ConnAck })
    $P.Mqtt1Pub = $win1.PublishCount
    Write-MqttSection -Title '04 MQTT 全帧抓包 · 第 1 窗（控灯前基线）' -Path (Join-Path $OutDir '04-MQTT全帧-第1窗-控灯前.txt') -Win $win1 -Note ('本窗是"没有改灯"的基线（默认 ' + $BaselineSeconds + ' 秒：只开控制台翻页面可以，改灯不行）：它用来证明"不改灯时没有灯效帧"。')
    Write-Host ('  -> 第 1 窗结束：收包 ' + $win1.RecvCount + '，其中 PUBLISH ' + $win1.PublishCount + ' 条') -ForegroundColor Green
    Write-MqttFailAlert -Win $win1 -Label '第 1 窗（控灯前）'
    if (Get-MqttFailShort -Win $win1) {
        Write-Host ('  [警告] 白名单 clientId（' + ($ClientIds -join ' / ') + '）都没拿到 CONNACK。') -ForegroundColor Red
        Write-Host '  ⚠️ 两难提示：官方控制台自己可能把 _4/_3/_2 全占了 => 可以【先关掉官方控制台】再抓，' -ForegroundColor Yellow
        Write-Host '     但那样就没法"边改灯边抓"了 —— 要不要这么做由你（机主）决定。' -ForegroundColor Yellow
    }

    # ---- 05 第 1 窗 -> 第 2 窗的【无缝衔接】（⭐ 修交互：不再等回车，改灯动作必须落在窗口里面） ----
    # 旧流程是"第 1 窗 -> 按回车 -> 切去控制台改灯 -> 再按回车 -> 第 2 窗"，
    # 结果机主的改灯动作全落在两窗之间的空档里（第 3 次真实抓包就是这么漏掉全部控灯帧的）。
    # 现在：第 1 窗一结束【立刻】进第 2 窗，不阻塞、不等任何按键；提示改到窗口开始之后打印。
    Step '05 [衔接] 第 1 窗已结束 -> 立刻进入第 2 窗（不再等回车）' | Out-Null
    Write-Host ('  ⭐ 现在【马上切到官方控制台】，第 2 窗的 ' + $ControlSeconds + ' 秒马上开始 —— 不再等你按回车。') -ForegroundColor Yellow
    Write-Host ('  ⭐ 第 2 窗这 ' + $ControlSeconds + ' 秒里请【全程持续改灯】（关→开、换颜色、换模式，每次停 2~3 秒）；本窗结束前不要停。') -ForegroundColor Yellow
    $guide = @(
        '  ① 切到【官方控制台】-> 找到键盘灯/灯效页面；',
        '  ② 把键盘灯从【关】调到【开】；',
        '  ③ 依次换 3~5 个不同颜色，每换一次停 2~3 秒；',
        '  ④ 再换 2~3 个不同灯效/模式（呼吸/波浪/单色…），每次停 2~3 秒；',
        '  ⑤ 如果你还会改"灯带/氛围灯/Logo 灯"，也顺手各改一次；',
        '  ⑥ 一直改到本窗倒计时走完（自动收窗）；如果已经改完了不想干等，可以按【回车】提前结束，',
        ('     ⚠️ 但回车提前结束会【少抓后面的帧】（建议改满 ' + $ControlSeconds + ' 秒）；开始后前 5 秒内的回车会被忽略（防误触）。')
    )
    foreach ($g in $guide) { Write-Host ('  ' + $g) -ForegroundColor Yellow }
    if ($NoPrompt) { Write-Host '  (-NoPrompt：同上，仍是不等回车直接开始第 2 窗)' -ForegroundColor DarkGray }
    # 这句会由 Invoke-MqttWindow 在【第 2 窗已经开始之后】打印（见 -StartNotice），保证不占用窗口时间。
    $win2Notice = ('现在就去改灯！本窗 ' + $ControlSeconds + ' 秒请【全程持续改灯】（关→开、换颜色、换模式，每次停 2~3 秒），本窗结束前不要停。')

    # ---- 06 第 2 窗（操作窗） ----
    Step ('06 MQTT 全帧抓包 · 第 2 窗（控灯时，' + $ControlSeconds + ' 秒）') | Out-Null
    Write-Host ('  这 ' + $ControlSeconds + ' 秒里请【持续改灯】：换颜色 / 换模式，每次停 2~3 秒，让帧落在窗口里。') -ForegroundColor Yellow
    Write-Host ('  （本窗自动计时、到点自动收窗；若你已改完不想干等，可按【回车】提前结束 —— ' + '但会【少抓后面的帧】，建议改满 ' + $ControlSeconds + ' 秒。）') -ForegroundColor DarkGray
    Write-Host '  ⚠️ 开始后前 5 秒内的回车会被忽略（防误触，避免窗口被瞬间收掉等于没抓）。' -ForegroundColor DarkGray
    Write-Host '  下面会一直显示"剩余 N 秒"；读到空闲超时只会继续等，不会提前收窗。' -ForegroundColor DarkGray
    $win2 = $null
    $tried2 = @()
    foreach ($cid in $ClientIds) {
        Write-Host ('  -> 尝试 clientId=' + $cid + '（白名单 _4 -> _3 -> _2 串行回退；只 CONNECT + SUBSCRIBE，绝不 PUBLISH）')
        # 串行：上一次 Invoke-MqttWindow 在自己的 finally 里已经把连接关掉了，这里才连下一个（绝不并发占用多个）。
        $try2 = Invoke-MqttWindow -ClientId $cid -TopicFilters $Topics -Seconds $ControlSeconds -Port $Port -Label '第 2 窗' -KeepAlive $MQTT_KEEPALIVE -StartNotice $win2Notice -AllowEnterStop -EnterStopTip ('按回车可提前结束本窗：会【少抓后面的帧】，建议改满 ' + $ControlSeconds + ' 秒；若已改完可回车')
        $win2 = $try2
        $tried2 += $cid
        if ($try2.TcpOk -and $try2.ConnAckCode -eq '0x00') { break }
        Start-Sleep -Milliseconds 150
    }
    if ($null -eq $win2) {
        $win2 = [ordered]@{ Ran = $false; ClientId = ''; TcpOk = $false; Err = 'clientId 列表为空'; ConnAck = '未跑'; ConnAckCode = ''; SubAck = '未跑'; Granted = ''; Events = (New-Object System.Collections.ArrayList); PublishCount = 0; PublishTopics = (New-Object System.Collections.ArrayList); SentCount = 0; RecvCount = 0; OtherCount = 0; StartedAt = (Get-Date); EndedAt = (Get-Date) }
    }
    $win2.AttemptedIds = ($tried2 -join ' -> ')
    $P.Mqtt2 = $(if ($win2.TcpOk -and $win2.ConnAckCode -eq '0x00') { 'CONNACK 成功' } else { '连接失败/' + $win2.ConnAck })
    $P.Mqtt2Pub = $win2.PublishCount
    Write-MqttSection -Title '06 MQTT 全帧抓包 · 第 2 窗（控灯时 本窗才是重点）' -Path (Join-Path $OutDir '06-MQTT全帧-第2窗-控灯时.txt') -Win $win2 -Note '本窗是"正在控灯"的窗口：新出现的 PUBLISH（第 1 窗没有、第 2 窗有）就是控灯帧。'
    Write-Host ('  -> 第 2 窗结束：收包 ' + $win2.RecvCount + '，其中 PUBLISH ' + $win2.PublishCount + ' 条') -ForegroundColor Green
    Write-MqttFailAlert -Win $win2 -Label '第 2 窗（控灯时）'
    if (Get-MqttFailShort -Win $win2) {
        Write-Host ('  [警告] 白名单 clientId（' + ($ClientIds -join ' / ') + '）都没拿到 CONNACK。') -ForegroundColor Red
        Write-Host '  ⚠️ 两难提示：官方控制台自己可能把 _4/_3/_2 全占了 => 可以【先关掉官方控制台】再抓，' -ForegroundColor Yellow
        Write-Host '     但那样就没法"边改灯边抓"了 —— 要不要这么做由你（机主）决定。' -ForegroundColor Yellow
    }
}

# ================================================================ 07 EC 只读（0x740 / 0x766）· 控灯后
Step '07 EC 只读（\\.\ACPIDriver + IOCTL 2621482120；0x740 / 0x766）· 控灯后' | Out-Null
$ecPost = Read-EcSample -Title '07 EC 读数 · 控灯后（0x740=1856 ProjectID / 0x766=1894 键盘灯判据）' -Phase '控灯后'
# 报告里 EC 一节现在【两次读数都在】：把 00b 的"控灯前"读数附在本节尾部做前后对比。
$ecOut = New-Object System.Text.StringBuilder
[void]$ecOut.Append($ecPost.Text)
[void]$ecOut.AppendLine('')
[void]$ecOut.AppendLine('== 控灯前 / 控灯后 两次读数对比（本报告 EC 节两次读数都在）==')
[void]$ecOut.AppendLine(('  控灯前（00b-EC读数-控灯前.txt）：' + (Format-EcPair $ecPre.V0740 $ecPre.V0766 $ecPre.Verdict)))
[void]$ecOut.AppendLine(('  控灯后（本文件 07）            ：' + (Format-EcPair $ecPost.V0740 $ecPost.V0766 $ecPost.Verdict)))
[void]$ecOut.AppendLine('  口径：两次相同 => 控灯没有改这两个 EC 寄存器；两次不同 => 控灯会写 EC（重点线索）。')
[void]$ecOut.AppendLine('  注：两次都是只读采样（只发读 IOCTL 2621482120）；非管理员读不到时各自如实写"做不到"，不伪造。')
Write-Utf8 -Path (Join-Path $OutDir '07-EC只读-0x740-0x766.txt') -Text $ecOut.ToString()
$P.EcVerdict = $ecPost.Verdict; $P.Ec0740 = $ecPost.V0740; $P.Ec0766 = $ecPost.V0766
if (-not $ecPost.Ok) { Warn ('EC 控灯后读数做不到：' + $ecPost.Verdict) }

# ================================================================ 08 GCU 日志（只读）+ 开关指引
Step '08 GCU 日志（只读 C:\ProgramData\ControlCenter\*.log）+ Debug 开关指引' | Out-Null
$gl = New-Object System.Text.StringBuilder
[void]$gl.AppendLine('08 GCU 日志（只读）+ 日志开关的开启步骤')
[void]$gl.AppendLine('============================================================')
[void]$gl.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
[void]$gl.AppendLine('本节【只读】文件，脚本没有改注册表、没有启停服务。')
[void]$gl.AppendLine('')
[void]$gl.AppendLine('== 如何让 GCU 写日志（这一步由机主自己决定做不做）==')
[void]$gl.AppendLine('  现状：GCUCtrl.LogCtrl.Write / TraceMessage 被注册表门控，【默认关闭】，且服务启动时只读一次。')
[void]$gl.AppendLine('  开启步骤（确切）：')
[void]$gl.AppendLine('    ① 打开 regedit，定位：')
[void]$gl.AppendLine('         HKEY_LOCAL_MACHINE\SOFTWARE\OEM\GamingCenterService\Debug')
[void]$gl.AppendLine('       （若 Debug 键不存在：右键 GamingCenterService -> 新建 -> 项，名字填 Debug）')
[void]$gl.AppendLine('    ② 在 Debug 下新建 DWORD(32 位)值，名字 enable，数值数据填 1（十六进制 1 也行）。')
[void]$gl.AppendLine('    ③ create 保持 0（不要动它；它是另一个开关，改它行为不同）。')
[void]$gl.AppendLine('    ④ 必须【重启 GCU 应用进程】才生效（服务启动时只读一次这个值）：')
[void]$gl.AppendLine('         任务管理器里结束 GCUService.exe / GCUBridge.exe，然后重启 GCUBridge 服务；')
[void]$gl.AppendLine('         或者更简单：直接重启电脑。')
[void]$gl.AppendLine('    ⑤ 日志会写到：C:\ProgramData\ControlCenter\yyyy-MM-dd.log（硬编码、没有子目录）。')
[void]$gl.AppendLine('    ⑥ 回滚：把 enable 改回 0，或直接删掉 enable 这个值 / 删掉 Debug 键。')
[void]$gl.AppendLine('  本脚本【不代做】这一步；本节只把当前门控状态读出来给你看。')
[void]$gl.AppendLine('')
[void]$gl.AppendLine('== 当前门控状态（只读）==')
[void]$gl.AppendLine('  HKLM\SOFTWARE\OEM\GamingCenterService\Debug\enable = ' + $P.DebugEnable)
[void]$gl.AppendLine('  => ' + $(if ($P.DebugEnable -match '^\s*[1-9]') { '已开启（>0）：GCU 应该在写日志了（若没写，检查 create 与是否重启过 GCU 进程）' } else { '未开启（0 或不存在）：GCU 大概率不写日志，看下面列出的历史日志即可' }))
[void]$gl.AppendLine('')
[void]$gl.AppendLine('== 日志文件清单（C:\ProgramData\ControlCenter\*.log）==')
$logDir = 'C:\ProgramData\ControlCenter'
$logFiles = @()
try {
    if (Test-Path -LiteralPath $logDir) {
        $logFiles = @(Get-ChildItem -LiteralPath $logDir -Filter '*.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    } else {
        [void]$gl.AppendLine('  (目录不存在：' + $logDir + ')')
    }
} catch { [void]$gl.AppendLine('  (枚举失败：' + $_.Exception.Message + ')') }
$P.LogCount = $logFiles.Count
if ($logFiles.Count -eq 0 -and (Test-Path -LiteralPath $logDir)) { [void]$gl.AppendLine('  (没有 *.log 文件)') }
foreach ($lf in $logFiles) {
    [void]$gl.AppendLine(('  ' + $lf.Name + '  ' + $lf.Length + ' 字节  最后写入 ' + $lf.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
}
[void]$gl.AppendLine('')
[void]$gl.AppendLine('== 最近 3 个日志的尾部（每文件最多 300 行 / 64KB）==')
foreach ($lf in ($logFiles | Select-Object -First 3)) {
    [void]$gl.AppendLine('')
    [void]$gl.AppendLine('---- ' + $lf.Name + ' ----')
    try {
        $txt = ''
        if ($lf.Length -gt 65536) {
            $fs = [System.IO.File]::Open($lf.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                [void]$fs.Seek(-65536, [System.IO.SeekOrigin]::End)
                $buf = New-Object byte[] 65536
                $n = $fs.Read($buf, 0, 65536)
                $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
            } finally { $fs.Dispose() }
        } else {
            $txt = [System.IO.File]::ReadAllText($lf.FullName, [System.Text.Encoding]::UTF8)
        }
        $arr = $txt -split "`r?`n"
        if ($arr.Count -gt 300) { $arr = $arr[($arr.Count - 300)..($arr.Count - 1)] }
        foreach ($l in $arr) { [void]$gl.AppendLine($l) }
    } catch { [void]$gl.AppendLine('(读取失败：' + $_.Exception.Message + ')') }
}
[void]$gl.AppendLine('')
[void]$gl.AppendLine('== 关键行（含 LM_Manager / LM_Init / RGBKB / solution / UNKNOWN / Keyboard / Subscribe / Mqtt / ACPIDriverInit 等）==')
$keyPat = 'LM_Manager|LM_Init|RGBKB|solution|UNKNOWN|Keyboard|Subscribe|SUBACK|Mqtt|ACPIDriverInit|Error|Exception|Lightbar|RGB'
$keyHits = 0
foreach ($lf in ($logFiles | Select-Object -First 5)) {
    try {
        $txt = [System.IO.File]::ReadAllText($lf.FullName, [System.Text.Encoding]::UTF8)
        $ii = 0
        foreach ($l in ($txt -split "`r?`n")) {
            $ii++
            if ($l -match $keyPat) {
                $keyHits++
                if ($keyHits -le 300) { [void]$gl.AppendLine(('  ' + $lf.Name + ':' + $ii + '  ' + $l.Trim())) }
            }
        }
    } catch { }
}
if ($keyHits -eq 0) { [void]$gl.AppendLine('  (零命中 —— 若 Debug\enable 未开，这是正常的，不是脚本出错)') }
elseif ($keyHits -gt 300) { [void]$gl.AppendLine(('  ...(共 ' + $keyHits + ' 行命中，只列前 300 行)')) }
$P.LogKeyLines = $keyHits
Write-Utf8 -Path (Join-Path $OutDir '08-GCU日志.txt') -Text $gl.ToString()
Write-Host ('  -> GCU 日志 ' + $P.LogCount + ' 个；关键行命中 ' + $keyHits + ' 行；Debug.enable=' + $P.DebugEnable) -ForegroundColor Green

# ================================================================ 09 注册表快照（控灯后）+ 差异
Step '09 注册表快照（控灯后）+ 前后差异对比' | Out-Null
$regB = [ordered]@{}
$snapB = New-Object System.Text.StringBuilder
[void]$snapB.AppendLine('09 注册表快照（控灯后 / 只读全树）')
[void]$snapB.AppendLine('============================================================')
[void]$snapB.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')))
[void]$snapB.AppendLine('')
foreach ($rr in $regRoots) {
    $exists = Test-Path -LiteralPath $rr.Path
    [void]$snapB.AppendLine('== ' + $rr.Label + ' ==')
    if (-not $exists) {
        [void]$snapB.AppendLine('  (整个键不存在)')
        $regB[$rr.Label] = @()
        [void]$snapB.AppendLine('')
        continue
    }
    $dump = Get-RegDump -Root $rr.Path
    $regB[$rr.Label] = @($dump)
    foreach ($l in $dump) { [void]$snapB.AppendLine(('  ' + $l)) }
    [void]$snapB.AppendLine('')
}
[void]$snapB.AppendLine('== 三个关键值（控灯后，应与控灯前一致；变了就是控制台写了它们）==')
foreach ($kv in @(
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2'; N = 'ProjectID'; L = 'GamingCenter2\ProjectID' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'BIOS_PROJECT_ID'; L = 'ItemSupport\BIOS_PROJECT_ID' },
    @{ P = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'; N = 'KeyboardType'; L = 'ItemSupport\KeyboardType' }
)) {
    $val = '(读不到)'
    try {
        if (Test-Path -LiteralPath $kv.P) {
            $o = Get-ItemProperty -LiteralPath $kv.P -Name $kv.N -ErrorAction Stop
            $val = [string]$o.($kv.N)
        }
    } catch { $val = '(读不到)' }
    [void]$snapB.AppendLine(('  ' + $kv.L + ' = ' + $val))
}
Write-Utf8 -Path (Join-Path $OutDir '09-注册表快照-控灯后.txt') -Text $snapB.ToString()

$diff = New-Object System.Text.StringBuilder
[void]$diff.AppendLine('10 注册表前后差异（只读对比：控灯前 vs 控灯后）')
[void]$diff.AppendLine('============================================================')
[void]$diff.AppendLine('口径：把两次快照的每一行（"路径 + 值名 + 类型 + 值"）做集合比较，逐行列出"新增 / 消失 / 变化"。')
[void]$diff.AppendLine('只读对比，脚本没有写任何注册表值。')
[void]$diff.AppendLine('')
$diffCount = 0
foreach ($label in @($regA.Keys)) {
    $a = @($regA[$label])
    $b = @($regB[$label])
    [void]$diff.AppendLine('== ' + $label + ' ==')
    [void]$diff.AppendLine(('  控灯前 ' + $a.Count + ' 行 / 控灯后 ' + $b.Count + ' 行'))
    $cmp = @(Compare-Object -ReferenceObject $a -DifferenceObject $b)
    $onlyA = @($cmp | Where-Object { $_.SideIndicator -eq '<=' } | ForEach-Object { [string]$_.InputObject })
    $onlyB = @($cmp | Where-Object { $_.SideIndicator -eq '=>' } | ForEach-Object { [string]$_.InputObject })
    if ($onlyA.Count -eq 0 -and $onlyB.Count -eq 0) {
        [void]$diff.AppendLine('  [无差异] 控制台控灯没有改这两个键下的任何值')
    } else {
        $diffCount += ($onlyA.Count + $onlyB.Count)
        [void]$diff.AppendLine('  有差异：')
        foreach ($l in $onlyA) { [void]$diff.AppendLine(('    [仅控灯前有 / 控灯后消失] ' + $l)) }
        foreach ($l in $onlyB) { [void]$diff.AppendLine(('    [仅控灯后有 / 新增或变化] ' + $l)) }
    }
    [void]$diff.AppendLine('')
}
[void]$diff.AppendLine('== 判读 ==')
[void]$diff.AppendLine(('  差异行数：' + $diffCount))
if ($diffCount -eq 0) {
    [void]$diff.AppendLine('  => 控灯没有走注册表（配置不落在这两个键下）。')
    [void]$diff.AppendLine('     那么控灯通道要么是 MQTT（看 04/06）、要么是 HID/USB（看 03）、要么是 EC（看 07）。')
} else {
    [void]$diff.AppendLine('  => 控灯会写注册表：上面标 [仅控灯后有 / 新增或变化] 的就是它落地的地方，优先研究这些键。')
}
$P.RegDiffLines = $diffCount
Write-Utf8 -Path (Join-Path $OutDir '10-注册表差异-前后.txt') -Text $diff.ToString()
Write-Host ('  -> 注册表前后差异行数：' + $diffCount) -ForegroundColor Green

# ================================================================ 00 概要 + 给机主步骤
Step '00 生成 00-概要.txt（结论摘要 + 逐字给机主步骤）' | Out-Null

# 结论方向（保守，不做过度断言）
try {
    if ($P.Mqtt2Pub -gt 0) {
        $P.Direction = 'MQTT 上确实有 PUBLISH（第 2 窗 ' + $P.Mqtt2Pub + ' 条）=> 控灯很可能走 13688 上的 MQTT，' + '请把 06 里"第 1 窗没有、第 2 窗新出现"的 topic + payload 抽出来与我们发的帧逐字比对。'
    } elseif ($P.RegDiffLines -gt 0) {
        $P.Direction = 'MQTT 没有新帧，但控灯改了注册表（差异 ' + $P.RegDiffLines + ' 行）=> 控灯走注册表/本地配置，优先看 10。'
    } elseif ($P.HidProbeOk -gt 0) {
        $P.Direction = 'MQTT 没有新帧，但 HID 输入报告可读（' + $P.HidProbeOk + ' 个接口）=> 灯控可能在 HID/USB 层；' + '要看到"控制台发出去的写命令"必须上 USBPcap + Wireshark（见 03 末尾替代方案）。'
    } elseif (($P.Mqtt1 -like '连接失败*') -and ($P.Mqtt2 -like '连接失败*')) {
        $P.Direction = 'MQTT 两窗都没完成握手（TCP 连不上 / 读 CONNACK 超时 / CONNACK rc≠0 三类分类见 04、06 节顶部）=> 先按 04/06 节顶部的排查提示处理（官方控制台/GCU 是否在跑、白名单 clientId 是否被占、13688 是否在听），再重抓一次。'
    } else {
        $P.Direction = '证据不足：本次没有抓到明显的控灯帧/注册表差异。请确认①官方控制台真的改了灯 ②Debug\enable 是否要开 ③必要时上 USBPcap。'
    }
} catch { $P.Direction = '证据不足（摘要计算异常）' }

# ---- MQTT 握手失败分类（供 00-概要 顶部"本次抓包无效"与 2)/3) 行使用）----
$mqttShort1 = ''; $mqttShort2 = ''; $mqttReason1 = ''; $mqttReason2 = ''
if (-not $SkipMqtt) {
    $mqttShort1 = Get-MqttFailShort -Win $win1; $mqttReason1 = Get-MqttFailReason -Win $win1
    $mqttShort2 = Get-MqttFailShort -Win $win2; $mqttReason2 = Get-MqttFailReason -Win $win2
}
$mqttBothFail = [bool]($mqttShort1 -and $mqttShort2)

$sum = New-Object System.Text.StringBuilder
[void]$sum.AppendLine('00 概要 —— 官方控制台"怎么控键盘灯" 一次性取证')
[void]$sum.AppendLine('============================================================')
if ($mqttBothFail) {
    [void]$sum.AppendLine('')
    [void]$sum.AppendLine('!! 本次抓包无效：MQTT 未握手成功（' + $mqttShort1 + '）——两个窗（04 / 06）都没有抓到任何报文。')
    [void]$sum.AppendLine('   第 1 窗（控灯前）原因：' + $mqttReason1)
    [void]$sum.AppendLine('   第 2 窗（控灯时）原因：' + $mqttReason2)
    [void]$sum.AppendLine('   请先按 04 / 06 节顶部的"排查提示"处理，然后【重抓一次】；')
    [void]$sum.AppendLine('   ⚠️ 不要把这份无效包当成有效包发回。')
    [void]$sum.AppendLine('')
}
[void]$sum.AppendLine(('脚本版本     : ' + $SCRIPT_VERSION))
[void]$sum.AppendLine(('生成时间     : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$sum.AppendLine(('运行命令     : powershell -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'))
[void]$sum.AppendLine(('输出目录     : ' + $OutDir))
[void]$sum.AppendLine(('是否管理员   : ' + $(if ($isAdmin) { '是' } else { '否（EC 读取会失败，请以管理员身份重跑）' })))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('-- 机器与系统 --')
[void]$sum.AppendLine(('机器型号     : ' + $(if ($P.Model) { $P.Model } else { '(读取失败)' })))
[void]$sum.AppendLine(('主板         : ' + $(if ($P.BaseBoard) { $P.BaseBoard } else { '(读取失败)' })))
[void]$sum.AppendLine(('BIOS 版本    : ' + $(if ($P.Bios) { $P.Bios } else { '(读取失败)' })))
[void]$sum.AppendLine(('OS           : ' + $(if ($P.OsName) { $P.OsName } else { '(读取失败)' }) + '  build ' + $(if ($P.OsBuild) { $P.OsBuild } else { '?' })))
[void]$sum.AppendLine(('开机时长     : ' + $(if ($P.Uptime) { $P.Uptime } else { '(读取失败)' })))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('-- 结论性摘要（一眼看这次抓到了什么）--')
[void]$sum.AppendLine('1) 三个关键值（判定 30 系键盘类型）：')
[void]$sum.AppendLine(('     GamingCenter2\ProjectID            = ' + $P.ProjectID))
[void]$sum.AppendLine(('     ItemSupport\BIOS_PROJECT_ID        = ' + $P.BioProjectId))
[void]$sum.AppendLine(('     ItemSupport\KeyboardType           = ' + $P.KeyboardType))
[void]$sum.AppendLine(('     （附带）KeyboardSupport=' + $P.KeyboardSupport + '  RGBLightbarSupport=' + $P.RgbLightbarSupport))
[void]$sum.AppendLine(('2) MQTT 第 1 窗（控灯前）：' + $P.Mqtt1 + '；收到 PUBLISH ' + $P.Mqtt1Pub + ' 条' + $(if ($mqttShort1) { '   <- 本窗无有效数据：未完成 MQTT 握手（' + $mqttShort1 + '），失败原因见 04 节顶部' } else { '' })))
[void]$sum.AppendLine(('3) MQTT 第 2 窗（控灯时）：' + $P.Mqtt2 + '；收到 PUBLISH ' + $P.Mqtt2Pub + ' 条  <- 这一数字是关键' + $(if ($mqttShort2) { '；但本窗无有效数据：未完成 MQTT 握手（' + $mqttShort2 + '），失败原因见 06 节顶部' } else { '' })))
# ⭐ 窗口时长/是否被提前结束，如实记录 —— 判断"没抓到帧"是不是只因窗口太短，就看下面两行
$winNote = {
    param($w, $name)
    if (-not $w) { return ('    ' + $name + '：未跑') }
    $rs = 0
    if ($w.StartedAt -and $w.EndedAt) { $rs = [Math]::Round(($w.EndedAt - $w.StartedAt).TotalSeconds, 1) }
    $plan = $(if ($w.PlannedSeconds) { [int]$w.PlannedSeconds } else { 0 })
    $flag = $(if ($w.StoppedEarly) { '⭐ 被机主按回车提前结束（窗口偏短 —— 没抓到帧可能只是这个原因）' } else { '跑满计划时长（自动收窗，未提前结束）' })
    return ('    ' + $name + '：计划 ' + $plan + ' 秒 / 实际 ' + $rs + ' 秒 —— ' + $flag + $(if ($w.EndReason) { '；结束原因：' + $w.EndReason } else { '' }))
}
[void]$sum.AppendLine('   窗口时长如实记录（⭐ 本表用于判断"没抓到控灯帧"是否只是因为窗口太短）：')
[void]$sum.AppendLine((& $winNote $win1 '第 1 窗（控灯前基线）'))
[void]$sum.AppendLine((& $winNote $win2 '第 2 窗（控灯时）'))
[void]$sum.AppendLine('   （两窗都【不按任何键】也会自己跑满；第 1、2 窗都允许按回车提前结束，但前 5 秒内的回车会被忽略。）')
[void]$sum.AppendLine(('4) HID 设备   : 共 ' + $P.HidTotal + ' 个，其中高嫌疑（键盘/灯效）' + $P.HidSuspect + ' 个；只读试读接口 ' + $P.HidProbeTried + ' 个、成功 ' + $P.HidProbeOk + ' 个'))
[void]$sum.AppendLine(('5) EC 只读    : 控灯前（00b）= ' + (Format-EcPair $P.EcPre0740 $P.EcPre0766 $P.EcPreVerdict) + ' ；控灯后（07）= ' + (Format-EcPair $P.Ec0740 $P.Ec0766 $P.EcVerdict)))
[void]$sum.AppendLine(('     EC 前后两次都在：控灯前在 00b-EC读数-控灯前.txt，控灯后在 07-EC只读-0x740-0x766.txt；'))
[void]$sum.AppendLine('     两次相同 => 控灯没改这两个 EC 寄存器；不同 => 控灯会写 EC。两次都是只读，非管理员读不到会如实写"做不到"。')
[void]$sum.AppendLine(('6) GCU 日志   : ' + $P.LogCount + ' 个；关键行 ' + $P.LogKeyLines + ' 行；Debug\enable 当前 = ' + $P.DebugEnable))
[void]$sum.AppendLine(('7) 注册表差异 : ' + $P.RegDiffLines + ' 行（控灯前 vs 控灯后）'))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('方向判断（保守提示，不下过度断言）：')
[void]$sum.AppendLine('   ' + $P.Direction)
[void]$sum.AppendLine('')
if ($mqttShort1 -or $mqttShort2) {
    [void]$sum.AppendLine('-- 本次 MQTT 握手失败的嫌疑与处理结果（两条；脚本已按此改好）--')
    [void]$sum.AppendLine('   嫌疑 1（最可能）：clientId 不在 GCU 白名单里。')
    [void]$sum.AppendLine('       现场证据：那次抓包的 CONNECT 里 clientId = UWPClient_1（报文尾部 ... 5F 31），')
    [void]$sum.AppendLine('       而 GCU 白名单是 UWPClient_4 -> _3 -> _2；用白名单外的 _1 很可能被桥直接忽略：')
    [void]$sum.AppendLine('       TCP 能连上，但永远收不到 CONNACK —— 与"TCP 通 / 读 CONNACK 超时 / PUBLISH 0 条"完全吻合。')
    [void]$sum.AppendLine('       处理结果：默认 clientId 已改成按白名单回退 UWPClient_4 -> _3 -> _2（串行、每个只试一次；')
    [void]$sum.AppendLine('                 每次都先关掉上一条连接再连，绝不并发占用多个）。')
    [void]$sum.AppendLine('   嫌疑 2：CONNECT 参数与 LM 不一致（我们发 keepalive=60，LM 用 WithKeepAlivePeriod(3 秒)）。')
    [void]$sum.AppendLine(('       处理结果：keepalive 已改成 ' + $MQTT_KEEPALIVE + ' 秒（与 LM 一致），协议级别 4 / CleanSession=1 不变；'))
    [void]$sum.AppendLine('                 并把【我方发出的 CONNECT 原始字节 hex】写进 04/06 节顶部，便于与 LM 抓包逐字节对照。')
    [void]$sum.AppendLine('   嫌疑 3（2026-10-09 定位到的真根因）：CONNECT 报文本身拼错了 ——')
    [void]$sum.AppendLine('       原来发出 10 15 4D 51 54 54 04 02 …，漏了"协议名长度"两字节 00 04；broker 会把 4D 51 当成')
    [void]$sum.AppendLine('       协议名长度（0x4D51）=> 直接不回 CONNACK => 现象就是"TCP 连上、读 CONNACK 超时"。')
    [void]$sum.AppendLine('       处理结果：拼包改成合法的 10 17 00 04 4D 51 54 54 04 <flags> 00 03 <clientId…>（剩余长度跟着 +2），')
    [void]$sum.AppendLine('                 并按真实成功抓包（archive\核显省电抓包\gpu_saving_capture.py:77-88）补上 username/password：')
    [void]$sum.AppendLine('                 UWPClient_N 配 UWPClient_User_N / UWPClient_Pwd888881772688_N，连接标志 0x02 -> 0xC2；')
    [void]$sum.AppendLine('                 -SelfTest 已加字节级反解校验（协议名长度/级别/flags/keepalive/clientId/username/password 逐段自洽）。')
    [void]$sum.AppendLine(('       取舍：keepalive=3 秒很短，若仍按原来 20 秒才 PINGREQ，桥会按 1.5×keepalive 判超时断连；'))
    [void]$sum.AppendLine(('             所以 PINGREQ 间隔改为每 ' + $MQTT_PING_SECONDS + ' 秒一次（keepalive/2；每帧仅 2 字节，只增加保活帧，不影响收到的报文集）。'))
    [void]$sum.AppendLine('')
}
[void]$sum.AppendLine('-- 采集清单 --')
[void]$sum.AppendLine('   01-注册表快照-控灯前.txt          HKLM\SOFTWARE\OEM\GamingCenter2（全树 + ItemSupport 全值）+ HKLM\SOFTWARE\L-Mechrevo')
[void]$sum.AppendLine('   02-HID设备枚举.txt               全部 HID 设备（VID/PID/FriendlyName/InstanceId/Status）+ 可疑灯效设备标注')
[void]$sum.AppendLine('   03-HID只读探测.txt               HID 接口的 UsagePage/Usage/上报长度 + 一次输入报告试读 + "做不到的点与替代方案"')
[void]$sum.AppendLine('   04-MQTT全帧-第1窗-控灯前.txt     MQTT 全帧（时间戳 + 方向 + topic + payload 原文 + 原始 hex），基线窗（默认 ' + $BaselineSeconds + ' 秒）；握手失败时顶部有告警+排查提示')
[void]$sum.AppendLine('   06-MQTT全帧-第2窗-控灯时.txt     同上，控灯时窗（重点看新出现的 PUBLISH）（默认 ' + $ControlSeconds + ' 秒）；握手失败时顶部有告警+排查提示')
[void]$sum.AppendLine('   07-EC只读-0x740-0x766.txt        EC 只读【控灯后】（0x740 ProjectID / 0x766 键盘灯判据 & 4）+ 与 00b 的控灯前读数对比')
[void]$sum.AppendLine('   00b-EC读数-控灯前.txt            EC 只读【控灯前】（在 MQTT 第 1 窗之前采；只读，读不到如实写"做不到"）')
[void]$sum.AppendLine('   08-GCU日志.txt                   C:\ProgramData\ControlCenter\*.log 清单 + 尾部 + 关键行 + Debug\enable 开启步骤')
[void]$sum.AppendLine('   09-注册表快照-控灯后.txt         控灯后的同口径快照')
[void]$sum.AppendLine('   10-注册表差异-前后.txt           前后逐行差异（新增/消失/变化）')
[void]$sum.AppendLine('   00-概要.txt                      本文件')
[void]$sum.AppendLine('')
if ($script:Warnings.Count -gt 0) {
    [void]$sum.AppendLine('-- 本次采集中的失败/告警（逐项独立，不影响其它项）--')
    foreach ($w in $script:Warnings) { [void]$sum.AppendLine('   ' + $w) }
    [void]$sum.AppendLine('')
}
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine('给机主的中文步骤（照做即可）')
[void]$sum.AppendLine('============================================================')
$ownerSteps = @(
    '第 0 步  先确认：这台机器上【官方控制台】装好并且能打开、能改键盘灯。',
    '         如果你装的官方控制台是 GamingCenterU / ControlCenter，',
    '         请先确认它里面"键盘灯"页面能正常改颜色。',
    '第 1 步  双击本脚本（或右键 -> 以管理员身份运行）。',
    '         必须【以管理员身份】运行：EC 读取需要管理员权限；',
    '           非管理员时脚本会明确写"EC 读取做不到"并降级，其余通道照常。',
    '         脚本会先抓一遍"控灯前"的快照（注册表 + HID + EC 0x740/0x766 + MQTT 第 1 窗）。',
    ('第 2 步  第 1 窗 MQTT 抓包（默认 ' + $BaselineSeconds + ' 秒，⭐ 自动计时，不用按任何键）：'),
    ('         这 ' + $BaselineSeconds + ' 秒是【基线】：你可以先去把【官方控制台打开、翻到键盘灯页面】，'),
    '         ⚠️ 但【先不要改灯】（现在改灯就把基线弄脏了）。',
    '         屏幕上会一直显示"剩余 N 秒"；不按键也会自己跑满，到点自动进第 3 步。',
    '         只有第 1 窗允许按【回车】提前结束 —— 这是可选的，纯为省时间。',
    '第 3 步  ⭐ 第 1 窗一结束，第 2 窗【立刻自动开始】（不等按键、不用按回车）——',
    '         所以第 1 窗快走完时就要【先切到官方控制台】-> 打开键盘灯页面 ->',
    '         把键盘灯从【关】调到【开】，再换 3~5 个不同颜色，',
    '         再换 2~3 个不同灯效/模式（呼吸/波浪/单色…），每换一次停 2~3 秒。',
    ('第 4 步  第 2 窗 MQTT 抓包（默认 ' + $ControlSeconds + ' 秒，⭐ 自动计时、到点自动结束）：'),
    ('         这 ' + $ControlSeconds + ' 秒里请【全程持续改灯】（关→开 / 换颜色 / 换模式，每次停 2~3 秒）；'),
    '         ⚠️ 本窗【不需要】按任何键：40 秒满脚本自己收窗；',
    '            若你已改完不想干等，可按【回车】提前结束 —— 但会【少抓后面的帧】，建议改满 40 秒；',
    '            开始后前 5 秒内的回车会被忽略（防误触，避免窗口被瞬间收掉等于没抓）。',
    '         本窗是否被提前结束、实际跑了多少秒，都会写进 06 节顶部和本概要（用于判断"没抓到帧"是不是窗口太短）。',
    '         然后自动再采一次 EC 0x740/0x766（控灯后，与 00b 的控灯前读数对比）、GCU 日志、注册表"控灯后"快照，并做前后差异对比。',
    '第 5 步  桌面会生成一个 zip：LM-官方控灯抓包-<时间>.zip。',
    '         把这个 zip【原样】发回给我们（不要解压、不要改动里面的文件）。',
    '',
    '脚本只读、绝不写任何东西：',
    '   - 不 PUBLISH 任何 MQTT 帧（只 SUBSCRIBE 收包）',
    '   - 不向 HID 写任何数据（只尝试读）',
    '   - 不写 EC（只用读 IOCTL 2621482120，脚本里根本没有写 IOCTL 2621482124）',
    '   - 不改注册表（只读快照）',
    '   - 不启停任何服务/进程',
    '',
    '如果希望拿到 GCU 自己的日志，请【你自己决定】是否开启 Debug 开关（步骤见 08-GCU日志.txt）：',
    '   注册表 HKEY_LOCAL_MACHINE\SOFTWARE\OEM\GamingCenterService\Debug 新建 DWORD enable=1，',
    '   create 保持 0，然后重启 GCU 进程（或重启电脑）才生效；删掉 enable 即回滚。',
    '   日志落在 C:\ProgramData\ControlCenter\yyyy-MM-dd.log。'
)
foreach ($l in $ownerSteps) { [void]$sum.AppendLine('  ' + $l) }
[void]$sum.AppendLine('')
[void]$sum.AppendLine('============================================================')
Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text $sum.ToString()
Write-Host '  -> 00-概要.txt 已写入。' -ForegroundColor Green

# ================================================================ 隐私打码 + 零命中自检
Step '隐私打码 + 零命中自检' | Out-Null
$redactedCount = 0
$redactFail = @()
foreach ($f in (Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name)) {
    try {
        $raw = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
        $red = Redact-Text -Text $raw
        if ($red -ne $raw) { Write-Utf8 -Path $f.FullName -Text $red; $redactedCount++ }
        $leftSentinel = ([regex]::Matches($red, [regex]::Escape([string][char]1))).Count
        if ($leftSentinel -gt 0) { Warn ('打码哨兵未还原：' + $f.Name + '（残留 ' + $leftSentinel + ' 处）') }
    } catch { $redactFail += ($f.Name + '：' + $_.Exception.Message) }
}
foreach ($rf in $redactFail) { Warn ('打码失败（已保留原文）：' + $rf) }

$selfCheck = New-Object System.Collections.ArrayList
$leakVals = New-Object System.Collections.ArrayList
try {
    # 这里的 Label 一律【不带真实值】，否则自检文本本身会把真值写进 00-概要
    [void]$leakVals.Add(@{ V = [string]$env:USERNAME; L = '真实用户名' })
    [void]$leakVals.Add(@{ V = [string]$env:COMPUTERNAME; L = '真实主机名' })
    if ($env:COMPUTERNAME -and $env:USERNAME) { [void]$leakVals.Add(@{ V = ([string]$env:COMPUTERNAME + '\' + [string]$env:USERNAME); L = '主机名\用户名 形态' }) }
    try {
        Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $u = $_.Name
            if ($u -and $u -notmatch '^(Public|Default|Default User|All Users|WDAGUtilityAccount|AppData)$') {
                [void]$leakVals.Add(@{ V = $u; L = 'C:\Users 下的用户目录名' })
                if ($env:COMPUTERNAME) { [void]$leakVals.Add(@{ V = ([string]$env:COMPUTERNAME + '\' + $u); L = '主机名\用户名 形态' }) }
            }
        }
    } catch { }
    try {
        $sp = Get-CimInstance -ClassName Win32_ComputerSystemProduct -ErrorAction Stop
        if ($sp -and $sp.IdentifyingNumber -and ([string]$sp.IdentifyingNumber).Length -ge 6) { [void]$leakVals.Add(@{ V = [string]$sp.IdentifyingNumber; L = '序列号' }) }
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
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name)) {
        try {
            $txt = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
            foreach ($lv in $leakVals) {
                if (-not $lv.V -or ([string]$lv.V).Length -lt 4) { continue }
                if ($txt -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape([string]$lv.V) + '(?![0-9A-Za-z])')) {
                    $leakFiles += ($f.Name + ' <- ' + $lv.L)
                }
            }
        } catch { }
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -Force | Sort-Object Name)) {
        $nm = [string]$f.Name
        foreach ($lv in $leakVals) {
            if (-not $lv.V -or ([string]$lv.V).Length -lt 4) { continue }
            if ($nm -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape([string]$lv.V) + '(?![0-9A-Za-z])')) { $leakFiles += ($nm + '（产物文件名） <- ' + $lv.L) }
        }
        if ($nm -match '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])') { $leakFiles += ($nm + '（产物文件名） <- 文件名里夹着 >=32 位连续十六进制') }
    }
} catch { }
if ($leakFiles.Count -gt 0) {
    $uniq = @($leakFiles | Select-Object -Unique)
    Warn ('打码自检未通过：' + ($uniq -join '；'))
    [void]$selfCheck.Add('未通过：' + ($uniq -join '；'))
} else {
    [void]$selfCheck.Add('通过：真实用户名 / 主机名 / "主机名\用户名" / 用户目录名 / 序列号（含产物文件名里夹的串）在全部产物里【零命中】')
}
try {
    $allTxt = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -File -Force | Sort-Object Name)) {
        try { $allTxt += (Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop) } catch { }
    }
    $sentLeft = ([regex]::Matches($allTxt, [regex]::Escape([string][char]1))).Count
    $ph = @()
    foreach ($phv in @('<主机名>', '<用户名>', '<序列号>', '<已打码>', '<用户目录>', '<MAC>')) {
        $ph += ($phv + ' ' + ([regex]::Matches($allTxt, [regex]::Escape($phv))).Count + ' 处')
    }
    [void]$selfCheck.Add('占位符痕迹（只允许占位符）：' + ($ph -join '、'))
    if ($sentLeft -gt 0) { [void]$selfCheck.Add('未通过：产物里残留打码哨兵 U+0001 共 ' + $sentLeft + ' 处（说明打码有 bug）') }
    else { [void]$selfCheck.Add('通过：产物里没有残留打码哨兵（U+0001 零命中）') }
} catch { [void]$selfCheck.Add('（占位符痕迹统计失败：' + $_.Exception.Message + '）') }
# 零写自检：脚本【代码行】里不得含 EC 写 IOCTL（注释/说明里出现不算）
try {
    $selfText = [System.IO.File]::ReadAllText($PSCommandPath, [System.Text.UTF8Encoding]::new($false))
    $codeText = Get-CodeText -Text $selfText
    if ($codeText -match [regex]::Escape($script:EcWriteNeedle)) {
        [void]$selfCheck.Add('未通过：代码里出现了 EC 写 IOCTL 2621482124（不该有）')
        Warn '零写自检未通过：代码里出现 EC 写 IOCTL 2621482124'
    } else {
        [void]$selfCheck.Add('通过：代码行里不含 EC 写 IOCTL 2621482124（零写 EC 自检）')
    }
} catch { }
try {
    $cur = Get-Content -LiteralPath (Join-Path $OutDir '00-概要.txt') -Raw -Encoding UTF8
    $add = "`r`n-- 隐私打码与零命中自检 --`r`n   " + ($selfCheck -join "`r`n   ") + "`r`n"
    Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text ($cur + $add)
} catch { }
foreach ($sc in $selfCheck) { Write-Host ('  ' + $sc) -ForegroundColor $(if ($sc -like '通过*') { 'Green' } else { 'Yellow' }) }

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
        if ((Test-Path -LiteralPath $zipPath) -and ((Get-Item -LiteralPath $zipPath).Length -gt 0)) { $zipOk = $true }
        else { $zipNote = 'zip 生成了但大小为 0。' }
    } catch {
        $zipNote = ('zip 打包失败（' + $_.Exception.Message + '）——目录已保留，请直接回传目录内容。')
        Warn ('zip 失败：' + $_.Exception.Message)
    }
}
if ($zipOk) {
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $za = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        $bad = @()
        foreach ($e in $za.Entries) { if ($e.FullName -match '_tmp_') { $bad += $e.FullName } }
        $za.Dispose()
        if ($bad.Count -gt 0) { Warn ('zip 里混进了临时文件：' + (($bad | Select-Object -Unique) -join ', ')) }
        else { Write-Host '  -> zip 自检：不含 _tmp_*（干净）' -ForegroundColor Green }
    } catch { Warn ('zip 内容自检失败：' + $_.Exception.Message) }
    Write-Host ('  -> zip：' + $zipPath + '（' + (Get-Item -LiteralPath $zipPath).Length + ' 字节）') -ForegroundColor Green
} else {
    Write-Host ('  -> ' + $zipNote) -ForegroundColor Yellow
}

# ================================================================ 收尾
Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' 官方控制台控灯抓包结束' -ForegroundColor White
Write-Host (' 输出目录 : ' + $OutDir)
Write-Host (' zip      : ' + $(if ($zipOk) { $zipPath } else { '未产出（' + $zipNote + '）' }))
Write-Host (' 打码文件 : ' + $redactedCount + ' 个')
Write-Host ' 本次【没有】做这些：没有 PUBLISH 任何 MQTT 帧、没有写 HID、没有写 EC/NVRAM、没有改注册表、没有启停服务/进程。'
Write-Host '============================================================' -ForegroundColor White
Write-Host ''
Write-Host '请把生成的 zip 发回来（不要解压、不要改动里面的文件）。' -ForegroundColor Green
Write-Host '如果 EC 那一节写了"做不到"，请【以管理员身份】再跑一次。' -ForegroundColor Green
Write-Host '如果 MQTT 有窗报"[警告] 未能与 127.0.0.1:13688 完成 MQTT 握手"，请先：① 确认官方控制台/GCU 在运行；' -ForegroundColor Green
Write-Host '  ② ⚠️ 关闭 L-Mechrevo（它占用 clientId 白名单 UWPClient_4/_3/_2）；③ netstat -ano | findstr 13688 看端口在不在听。' -ForegroundColor Green
Write-Host ''
foreach ($w in $script:Warnings) { Write-Host ('  告警：' + $w) -ForegroundColor Yellow }
if (-not $NoPrompt) {
    Write-Host ''
    # ⭐ 这里【不要求按任何键】：抓包与产物早已全部写完，本窗口 120 秒后自动关闭，
    #   想早点走就按一下回车（纯阅读时间，绝不参与任何计时/流程推进）。
    Write-Host '（全部抓包与产物都已写完；本窗口 120 秒后自动关闭 —— 也可以按回车立即关闭，不按也行）' -ForegroundColor Green
    $closeDeadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $closeDeadline) {
        try {
            if ([Console]::KeyAvailable) {
                $k = [Console]::ReadKey($true)
                if ($k.Key -eq [ConsoleKey]::Enter) { break }
            }
        } catch { break }
        Start-Sleep -Milliseconds 200
    }
}
exit 0
