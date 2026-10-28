#Requires -Version 5.1
<#
    SRGB诊断.ps1   （耀世 X6AR55xY / project IDY "调 SRGB 跳回默认" 只读诊断抓包）
    ==================================================================
    给机主用：切到 SRGB 档后跑一次，产出一个 zip，把"跳回默认"的关键证据一次性收齐。

    已查清的现象（本脚本只做取证，不做修复）：
      * 切 SRGB（档位 2）后 UI 跳回「默认」。
      * 日志签名：SetColorCalibration(mode 2) -> COLOR_CALIBRATION_ON_SRGB
        -> 241ms 后 confirmed=False expectedMode=2 actualMode=1 switch=True result=4
        -> 校色档位应用失败: sent=2, 已回退到原档位（两次都同签名）。
      * GCU 结果码：0=Ready 1=Finish 2=NetworkFail 3=FileIsNotFound 4=ColorCalibrationFail
        5=WebClientRequestFail 6=ConnectServerFail 7=WebClientUnknownFail。
        GCU 只在 result==1 时才写注册表 ColorCalibration 与上报档位。
      * 档位=2 的机型，sRGB 走 UserSetColorCalibration -> StartICCProfile(mode)，
        依赖 ICC 下载/应用 ⇒ 疑点在【ICC 那条链】。

    ------------------------------------------------------------------
    用法（复制粘贴即可；脚本不依赖任何固定盘符/固定目录，放哪都能跑）：
      powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\Desktop\SRGB诊断.ps1"
      指定输出目录（默认是桌面）：
        ... -OutDir "$env:USERPROFILE\Desktop\LM-SRGB诊断-测试"

    参数：
      -OutDir <path>   输出目录，默认 $env:USERPROFILE\Desktop\LM-SRGB诊断-<yyyyMMdd-HHmmss>
      -Zip / -Zip:$false   是否产出 zip，默认产出

    ------------------------------------------------------------------
    ⚠ 严格只读声明（重要）：
      * 本脚本【不】改注册表、【不】改颜色管理关联、【不】安装/删除任何 ICC、
        【不】写 C:\Windows\System32\spool\drivers\color、【不】写 ProgramData/AppData 配置、
        【不】启停服务、【不】连 13688、【不】切显卡/性能模式、【不】跑 powercfg、【不】写 EC。
      * 03 节会只读探测三个已知 ICC 服务器：DNS 解析 + TCP 连接 + 一次 HTTPS 请求
        （只取状态码/长度，不把响应体落盘；用完即弃，绝不进系统目录）。
      * 本诊断以只读为主，【不需要管理员】；非管理员下个别项读不到会在报告里写明"需管理员"。
      * 唯一写入：输出目录（桌面）+ 同名 .zip；跑完自动清理临时文件。

    产出：OutDir 目录 + 同名 .zip（默认）。zip 里含 00~06 各节证据，请把生成的 zip 发回来。
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [bool]$Zip = $true
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$SCRIPT_VERSION = '2026-10-10 r1'

# ================================================================ 运行状态
$script:Warnings = New-Object System.Collections.ArrayList
$script:Notes    = New-Object System.Collections.ArrayList
$script:StepNo   = 0

function Warn {
    param([string]$Text)
    $line = ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text)
    [void]$script:Warnings.Add($line)
    Write-Host ("  ! " + $Text) -ForegroundColor Yellow
}
function Note {
    param([string]$Text)
    [void]$script:Notes.Add($Text)
}
function Step {
    param([string]$Title)
    $script:StepNo++
    Write-Host ''
    Write-Host ("[{0}] {1}" -f $script:StepNo, $Title) -ForegroundColor Cyan
}
function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

# 写 UTF-8【带 BOM】：PS 5.1 读中文需要 BOM；PS7 的 utf8BOM 也带 BOM。两边行为一致。
function Write-Utf8 {
    param([string]$Path, [string]$Text)
    try {
        Set-Content -LiteralPath $Path -Value $Text -Encoding utf8BOM -ErrorAction Stop
    } catch {
        # 老版本 PS 没有 utf8BOM 名字：退回 UTF8（PS5.1 的 UTF8 自带 BOM）
        Set-Content -LiteralPath $Path -Value $Text -Encoding UTF8 -ErrorAction Stop
    }
}

function Get-PropText {
    # 把注册表值渲染成一行文本（Binary 只留长度+前 16 字节，避免报告里塞大 blob）
    param($Value, [string]$Kind)
    try {
        if ($null -eq $Value) { return '(空)' }
        if ($Value -is [byte[]]) {
            $n = $Value.Length
            $head = (($Value[0..([Math]::Min(15, $n - 1))] | ForEach-Object { $_.ToString('x2') }) -join ' ')
            return ("<Binary {0} 字节: {1} ...>" -f $n, $head)
        }
        if ($Value -is [array]) { return ('[' + (($Value | ForEach-Object { [string]$_ }) -join ', ') + ']') }
        $s = [string]$Value
        if ($s.Length -gt 300) { $s = $s.Substring(0, 300) + '...(截断)' }
        return $s
    } catch { return ('(渲染失败：' + $_.Exception.Message + ')') }
}

function Get-RegValues {
    # 健壮读注册表：返回 @{ Ok=$bool; Err=''; Values=@( @{Name;Type;Value} ) }
    param([string]$Path)
    $res = @{ Ok = $false; Err = ''; Values = @() }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { $res.Err = '路径不存在'; return $res }
        $k = Get-Item -LiteralPath $Path -ErrorAction Stop
        $names = @()
        try { $names = @($k.GetValueNames()) } catch { $names = @() }
        $vals = New-Object System.Collections.ArrayList
        foreach ($n in $names) {
            $disp = $n
            if (-not $disp) { $disp = '(默认值)' }
            $kind = ''
            $v = $null
            try { $kind = [string]$k.GetValueKind($n) } catch { $kind = '' }
            try { $v = $k.GetValue($n) } catch { $v = $null }
            [void]$vals.Add(@{ Name = $disp; Type = $kind; Value = (Get-PropText -Value $v -Kind $kind) })
        }
        $res.Values = $vals.ToArray()
        $res.Ok = $true
    } catch {
        $res.Err = $_.Exception.Message
    }
    return $res
}

function Get-RegSubKeys {
    param([string]$Path)
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return @() }
        return @(Get-ChildItem -LiteralPath $Path -ErrorAction Stop | ForEach-Object { $_.PSChildName })
    } catch { return @() }
}

function Format-RegSection {
    # 把某个注册表路径的值渲染进 StringBuilder
    param($Sb, [string]$Path, [string]$Title)
    [void]$Sb.AppendLine(('  --- ' + $Title + ' ---'))
    [void]$Sb.AppendLine(('  路径：' + $Path))
    $r = Get-RegValues -Path $Path
    if (-not $r.Ok) {
        [void]$Sb.AppendLine(('    （读不到：' + $(if ($r.Err) { $r.Err } else { '未知原因' }) + '）'))
        [void]$Sb.AppendLine('    ⚠ 该键非管理员也可能读不到（HKLM 受限）⇒ 需管理员才能读时属预期。')
        return
    }
    if ($r.Values.Count -eq 0) { [void]$Sb.AppendLine('    （该键存在，但没有任何值）'); return }
    foreach ($v in $r.Values) {
        [void]$Sb.AppendLine(("    {0,-30} [{1,-8}] = {2}" -f $v.Name, $v.Type, $v.Value))
    }
}

# ================================================================ 隐私打码
# 已知系统/产品路径分量与产品名（哨兵）：绝不能被当成"域名/长串"吞掉。
# ⚠ 表内顺序有意义：长词必须排在短词前面。
$script:SafeWords = @(
    'SystemProductName', 'BIOS_PROJECT_ID', 'BaseBoardSerialNumber',
    'HKLM:', 'HKCU:', 'Registry::',
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM',
    'Microsoft\Windows\CurrentVersion\Internet Settings',
    'spool\drivers\color', 'Windows\Color', 'Color System',
    'L-Mechrevo', 'MechrevoLite', 'Mechrevo', 'ControlCenter', 'GamingCenter2', 'GamingCenter',
    'GCUBridge', 'GCUService', 'OSDTpDetect', 'GCU',
    'MySetting', 'DisplayFeatures', 'ItemSupport', 'ProfileAssociations', 'RegisteredProfiles',
    'ColorCalibration', 'ColorCalibrationSupport', 'ColorCalibrationLevel', 'AssociateColorProfileResult',
    'DisplayFeatureStatus', 'DisplayMode', 'HDRSwitch', 'DGpuPowerManagement',
    'Get-IccProfile-fixed.ps1',
    'StartICCProfile', 'SetColorCalibration', 'UserSetColorCalibration', 'ICCProfile',
    'ColorCalibrationSupport', 'ColorCalibrationLevel', 'AssociateColorProfileResult',
    'expectedMode', 'actualMode', 'confirmed', 'actualProfile',
    'http://iccprofile.', 'http://iccprofile1.',
    'iccprofile.uniwill.com.tw', 'iccprofile.aistoneglobal.com', 'iccprofile1.aistoneglobal.com',
    'Windows Defender', 'AntiVirusProduct', 'SecurityCenter2',
    'Program Files', 'Program Files (x86)', 'ProgramData', 'Program',
    'Windows', 'System32', 'SysWOW64', 'AppData', 'Roaming', 'Local', 'Users',
    'Desktop', 'Documents', 'Downloads', 'Temp', 'SOFTWARE', 'SYSTEM', 'HARDWARE', 'DESCRIPTION',
    'CurrentControlSet', 'Services', 'wmi', 'root',
    'Administrator', 'LocalHost', 'localhost', '127.0.0.1', '::1', '0.0.0.0'
)

function Protect-Words {
    # 哨兵做法：先把已知系统路径/产品名换成 U+0001 哨兵，打码完再还原。
    # ★ 关键修正：哨兵里连同「匹配到的大小写」一起存（u=大写 l=小写 -=其它），
    #   否则还原时被 SafeWords 里的写法覆盖，真机实测会把
    #   http://iccprofile.uniwill.com.tw/api/iccprofile/ 变成 ...ICCProfile...。
    param([string]$Text)
    $t = [string]$Text
    if ([string]::IsNullOrEmpty($t)) { return $t }
    $s = [string][char]1
    for ($i = 0; $i -lt $script:SafeWords.Count; $i++) {
        $w = $script:SafeWords[$i]
        if (-not $w) { continue }
        try {
            $t = [regex]::Replace($t, [regex]::Escape($w), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase, {
                    param($m)
                    $v = [string]$m.Value
                    $fl = New-Object System.Text.StringBuilder
                    foreach ($ch in $v.ToCharArray()) {
                        if ([char]::IsUpper($ch)) { [void]$fl.Append('u') }
                        elseif ([char]::IsLower($ch)) { [void]$fl.Append('l') }
                        else { [void]$fl.Append('-') }
                    }
                    return ($s + 'W' + $i + ':' + $fl.ToString() + $s)
                })
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
            $pfx = $s + 'W' + $i + ':'
            if ($t.IndexOf($pfx) -lt 0) { continue }
            $word = [string]$script:SafeWords[$i]
            # ⚠ 按哨兵里存下的大小写逐字拼回；不能直接用 SafeWords 里的写法。
            $t = [regex]::Replace($t, [regex]::Escape($pfx) + '(u|l|-)*' + [regex]::Escape($s), {
                    param($m)
                    $v = [string]$m.Value
                    $fl = $v.Substring($pfx.Length, $v.Length - $pfx.Length - 1)
                    $b = New-Object System.Text.StringBuilder
                    for ($k = 0; $k -lt $word.Length; $k++) {
                        $c = $word[$k]
                        if ($k -lt $fl.Length) {
                            if ($fl[$k] -eq 'u') { $c = [char]::ToUpper($c) }
                            elseif ($fl[$k] -eq 'l') { $c = [char]::ToLower($c) }
                        }
                        [void]$b.Append($c)
                    }
                    return $b.ToString()
                })
        } catch { }
    }
    return $t
}
function Get-RedactMap {
    $m = New-Object System.Collections.ArrayList
    try {
        $cn = [string]$env:COMPUTERNAME
        if ($cn) { [void]$m.Add(@{ P = [regex]::Escape($cn); R = '<主机名>' }) }
        # ⚠ 用户名可能出现在 C:\Users\<名>\ 这种路径里，未必等于 $env:USERNAME ⇒ 收全部用户目录名
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
        # 序列号 / 机器 UUID
        $serials = @()
        foreach ($q in @('Win32_BIOS', 'Win32_ComputerSystemProduct', 'Win32_BaseBoard')) {
            try {
                $o = Get-CimInstance -ClassName $q -ErrorAction Stop
                if ($o) {
                    foreach ($prop in @('SerialNumber', 'IdentifyingNumber', 'UUID')) {
                        try {
                            $v = [string]$o.$prop
                            if ($v -and $v.Trim().Length -ge 6 -and $v -notmatch '^(Default string|To be filled|None|0+)$') { $serials += $v.Trim() }
                        } catch { }
                    }
                }
            } catch { }
        }
        foreach ($s in ($serials | Select-Object -Unique)) { [void]$m.Add(@{ P = [regex]::Escape($s); R = '<序列号>' }) }
        # MAC（只收物理网卡的，形状两种写法都覆盖）
        try {
            Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object {
                $mc = [string]$_.MacAddress
                if ($mc) { [void]$m.Add(@{ P = [regex]::Escape($mc); R = '<MAC>' }) }
            }
        } catch { }
    } catch { }
    return $m
}

function Redact-Text {
    # 顺序：a) 保护已知系统词 → b) 形状判定（长十六进制/MAC/IPv6）→ c) 整串替换标识 → d) 还原哨兵 → e) 补一刀 → f) 兜底
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $t = [string]$Text
    if ($t.Length -eq 0) { return $t }
    $t = Protect-Words $t
    # b1) 长十六进制串（>=32 位连续十六进制）：只保前 16 位
    try {
        $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])', {
                param($m)
                $v = $m.Value
                if ($v.Length -le 16) { return $v }
                return ($v.Substring(0, 16) + '<已打码>')
            })
    } catch { }
    # b2) 长 base64/哈希串（>=40 位且同时含大小写+数字），避免吞掉普通单词
    try {
        $t = [regex]::Replace($t, '(?<![0-9A-Za-z+/])[0-9A-Za-z+/]{40,}={0,2}(?![0-9A-Za-z+/=])', [System.Text.RegularExpressions.RegexOptions]::None, {
                param($m)
                $v = $m.Value
                if (-not (($v -cmatch '[a-z]') -and ($v -cmatch '[A-Z]') -and ($v -match '[0-9]'))) { return $v }
                return ($v.Substring(0, 16) + '<已打码>')
            })
    } catch { }
    # b3) 邮箱
    try { $t = [regex]::Replace($t, '[0-9A-Za-z._%+-]{2,}@[0-9A-Za-z.-]{2,}\.[A-Za-z]{2,}', '<邮箱>') } catch { }
    # b4) GUID（带花括号）
    try { $t = [regex]::Replace($t, '\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}', '<GUID>') } catch { }
    # b5) MAC（- 或 : 两种写法）
    try { $t = [regex]::Replace($t, '(?<![0-9A-Fa-f])([0-9A-Fa-f]{2}([-:]))([0-9A-Fa-f]{2}\2){4}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])', '<MAC>') } catch { }
    # b6) IPv6：只打码前缀，保留最后一段（末段常含端口/判断信息）
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
    # c) 主机名/用户名/用户目录/序列号/MAC 整串
    $skippedBySentinel = New-Object System.Collections.ArrayList
    foreach ($e in $script:RedactMap) {
        try {
            $before = $t
            $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase')
            # 没换成 ⇒ 多半被哨兵挡住（典型：C:\Users\<用户名>\ 里的用户名段）。记下来补一刀。
            if ($t -ceq $before -and $e.R -in @('<用户名>', '<主机名>', '<序列号>', '<用户目录>', '<MAC>')) {
                [void]$skippedBySentinel.Add($e)
            }
        } catch { }
    }
    $t = Unprotect-Words $t
    # e) 哨兵还原后【补一刀】—— 关键步骤，否则 C:\Users\<用户名>\... 会原样漏出去
    foreach ($e in $skippedBySentinel) {
        try { $t = [regex]::Replace($t, $e.P, $e.R, 'IgnoreCase') } catch { }
    }
    # f) 兜底裸名
    try {
        foreach ($bp in $script:BareNamePatterns) {
            try { $t = [regex]::Replace($t, $bp.P, $bp.R, 'IgnoreCase') } catch { }
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

# ================================================================ 工具集
function Test-Admin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $pr = New-Object Security.Principal.WindowsPrincipal($id)
        return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Invoke-Safe {
    # 每项独立 try/catch：单项失败只记告警，绝不整体中断
    param([string]$Label, [scriptblock]$Action, $Fallback = $null)
    try { return (& $Action) } catch {
        Warn ("{0}：该项失败（{1}）" -f $Label, $_.Exception.Message)
        return $Fallback
    }
}

function Read-LinesShared {
    # 允许别人同时写的方式读文件（LM 日志常被占用）；Tail=$true 只留最后 MaxLines 行
    param([string]$Path, [int]$MaxLines = 800, [bool]$Tail = $true)
    $fs = $null; $sr = $null
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
            if ($MaxLines -gt 0 -and $q.Count -ge $MaxLines) { [void]$q.Dequeue() }
            $q.Enqueue($l)
        }
        return $q.ToArray()
    } finally {
        if ($sr) { try { $sr.Dispose() } catch { } }
        if ($fs) { try { $fs.Dispose() } catch { } }
    }
}

function Select-Lines {
    # Keep='tail' 保留【最后 Max 行】（诊断要最新现场）
    param([string[]]$Lines, [string]$Pattern, [int]$Max = 250, [string]$Keep = 'tail')
    $re = $null
    try { $re = [regex]::new($Pattern, 'IgnoreCase') } catch { return @{ Lines = @(); Total = 0; Truncated = $false } }
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
        return @{ Lines = $q.ToArray(); Total = $total; Truncated = ($Max -gt 0 -and $total -gt $Max) }
    }
    $out = New-Object System.Collections.ArrayList
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        if ($re.IsMatch($l)) { $total++; if ($Max -le 0 -or $out.Count -lt $Max) { [void]$out.Add($l) } }
    }
    return @{ Lines = $out.ToArray(); Total = $total; Truncated = ($Max -gt 0 -and $total -gt $Max) }
}

function Get-TextOr {
    # 取值兜底：$null / 空白 一律渲染成显式文本，绝不留空白格
    param($Value, [string]$Fallback = '(未取到)')
    try {
        if ($null -eq $Value) { return $Fallback }
        $s = [string]$Value
        if ([string]::IsNullOrWhiteSpace($s)) { return $Fallback }
        return $s
    } catch { return $Fallback }
}

function Test-IccHeader {
    # ICC 头校验：前 4 字节 = 声明长度，第 37~40 字节 = 'acsp' 魔数
    param([string]$Path)
    $r = @{ Ok = $false; Size = 0; Declared = 0; Magic = ''; Sig = ''; Err = '' }
    try {
        $fi = Get-Item -LiteralPath $Path -ErrorAction Stop
        $r.Size = [int64]$fi.Length
    } catch { $r.Err = ('取文件信息失败：' + $_.Exception.Message); return $r }
    try {
        $buf = New-Object byte[] 128
        $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try { $n = $fs.Read($buf, 0, 128) } finally { $fs.Dispose() }
        if ($n -lt 40) { $r.Err = ('文件太短（{0} 字节 < 40），不可能是 ICC' -f $n); return $r }
        $r.Declared = ([int]$buf[0] -shl 24) + ([int]$buf[1] -shl 16) + ([int]$buf[2] -shl 8) + [int]$buf[3]
        $r.Magic = (([char]$buf[36]) + ([char]$buf[37]) + ([char]$buf[38]) + ([char]$buf[39]))
        $r.Sig = (([char]$buf[16]) + ([char]$buf[17]) + ([char]$buf[18]) + ([char]$buf[19])).Trim()
        $r.Ok = ($r.Magic -eq 'acsp')
    } catch { $r.Err = $_.Exception.Message }
    return $r
}

function Get-LastLogTime {
    # 从日志尾部行里抽出最后一条时间戳
    param([string[]]$Lines)
    if ($null -eq $Lines) { return $null }
    $arr = @($Lines)
    if ($arr.Count -eq 0) { return $null }
    for ($i = $arr.Count - 1; $i -ge 0; $i--) {
        $l = [string]$arr[$i]
        if (-not $l) { continue }
        $m = [regex]::Match($l, '(20\d{2})[-/](\d{1,2})[-/](\d{1,2})[ T](\d{1,2}):(\d{2}):(\d{2})')
        if ($m.Success) {
            try {
                return [datetime]::new([int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value,
                    [int]$m.Groups[4].Value, [int]$m.Groups[5].Value, [int]$m.Groups[6].Value)
            } catch { }
        }
    }
    return $null
}

# == ICC 关联名 <-> 磁盘文件的规范化匹配 ==
#    真机实证：关联写 Default.icm / sRGB.icm，磁盘上却叫 sRGB Color Space Profile.icm
#    ⇒ 只用精确匹配会误报"关联指向不存在的 ICC"，所以这里做四级匹配 + 常见别名表。
$script:AliasLooseKeys = @{
    'acm' = $true; 'adobe' = $true; 'colorspace' = $true; 'compatible' = $true; 'default' = $true
    'display' = $true; 'generic' = $true; 'iec' = $true; 'profile' = $true; 'standard' = $true
    'win' = $true; 'windows' = $true; 'wsc' = $true
}
$script:ProfileAliasExact = @{
    'srgb' = @('sRGB Color Space Profile.icm')
    'default' = @('sRGB Color Space Profile.icm')
    'adobergb' = @('AdobeRGB1998.icc')
    'adobe1998' = @('AdobeRGB1998.icc')
    'dcip3' = @('DCIP3.icm')
    'displayp3' = @('DisplayP3.icc')
    'widergb' = @('wscRGB.cdmp')
    'scrgb' = @('wscRGB.cdmp')
}
$script:ProfileDirCache = $null

function Get-ProfileDirList {
    # 与 02 节口径一致的只读目录表（缓存一次，避免每个名字都全盘枚举）
    if ($script:ProfileDirCache) { return $script:ProfileDirCache }
    $list = New-Object System.Collections.ArrayList
    [void]$list.Add(@{ Label = '系统颜色目录'; Path = (Join-Path $env:SystemRoot 'System32\spool\drivers\color') })
    [void]$list.Add(@{ Label = '用户颜色目录(Local)'; Path = (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Color') })
    [void]$list.Add(@{ Label = '用户颜色目录(Roaming)'; Path = (Join-Path $env:APPDATA 'Microsoft\Windows~Color') })
    [void]$list.Add(@{ Label = 'ProgramData 颜色目录'; Path = (Join-Path $env:ProgramData 'Microsoft\Windows\Color') })
    [void]$list.Add(@{ Label = 'LM 自定义 ICC'; Path = (Join-Path $env:ProgramFiles 'L-Mechrevo\customICC') })
    $script:ProfileDirCache = $list.ToArray()
    return $script:ProfileDirCache
}

function Get-AliasKey {
    # 规范化：小写 + 只留字母数字（去空格/点/连字符），再剥掉末尾 icc/icm
    param([string]$Name)
    $n = [System.IO.Path]::GetFileName([string]$Name)
    if (-not $n) { return '' }
    $k = [regex]::Replace($n.ToLowerInvariant(), '[^a-z0-9]', '')
    $k = [regex]::Replace($k, 'ic[cm]$', '')
    return $k
}

function Get-LooseAliasKey {
    # 更宽一层：丢掉 profile/colorspace/iec/standard/default/adobe 这类通用词后的主干
    param([string]$Name)
    $k = Get-AliasKey $Name
    if (-not $k) { return '' }
    $parts = New-Object System.Collections.ArrayList
    foreach ($mm in [regex]::Matches($k, 'srgb|p3|dcip3|displayp3|adobe[0-9]*|rgb|wide|ws|d50|d65|argb|[a-z]+')) {
        $tok = $mm.Value
        if (-not $script:AliasLooseKeys.ContainsKey($tok)) { [void]$parts.Add($tok) }
    }
    return ($parts -join '')
}

function Find-ProfileOnDisk {
    # 四级匹配：1) 绝对路径 2) 文件名精确（大小写不敏感） 3) 常见别名表 4) 规范化主干相同
    param([string]$Name, [bool]$NeedHeader = $false)
    $res = New-Object System.Collections.ArrayList
    if ([string]::IsNullOrWhiteSpace($Name)) { return @() }
    $raw = ([string]$Name).Trim()
    if ($raw -match '^[A-Za-z]:[\/]') {
        if (Test-Path -LiteralPath $raw) {
            $it = Get-Item -LiteralPath $raw -ErrorAction SilentlyContinue
            if ($it) {
                $h = $true
                if ($NeedHeader) { $h = (Test-IccHeader -Path $it.FullName).Ok }
                [void]$res.Add(@{ Path = $it.FullName; Len = $it.Length; Mtime = $it.LastWriteTime; DirLabel = '（名字本身就是绝对路径）'; How = '绝对路径精确命中'; Header = $h })
            }
        }
        return $res.ToArray()
    }
    $base = [System.IO.Path]::GetFileName($raw)
    if (-not $base) { return $res.ToArray() }
    $want = $base.ToLowerInvariant()
    $key = Get-AliasKey $base
    $loose = Get-LooseAliasKey $base
    $aliases = @()
    if ($script:ProfileAliasExact.ContainsKey($key)) { $aliases = @($script:ProfileAliasExact[$key]) }
    foreach ($d in (Get-ProfileDirList)) {
        if (-not (Test-Path -LiteralPath $d.Path)) { continue }
        $files = @(Get-ChildItem -LiteralPath $d.Path -File -ErrorAction SilentlyContinue)
        foreach ($f in $files) {
            $how = ''
            if ($f.Name.ToLowerInvariant() -eq $want) { $how = '文件名精确（大小写不敏感）' }
            elseif ($aliases -contains $f.Name) { $how = '常见别名表命中' }
            elseif ($key -and (Get-AliasKey $f.Name) -eq $key) { $how = '规范化主干相同（忽略空格/点/连字符/后缀）' }
            elseif ($loose -and $loose.Length -ge 3 -and $loose -eq (Get-LooseAliasKey $f.Name)) { $how = '宽松主干相同（去通用词）' }
            if ($how) {
                $h = $true
                if ($NeedHeader) { $h = (Test-IccHeader -Path $f.FullName).Ok }
                [void]$res.Add(@{ Path = $f.FullName; Len = $f.Length; Mtime = $f.LastWriteTime; DirLabel = $d.Label; How = $how; Header = $h })
            }
        }
    }
    return $res.ToArray()
}

function Get-SystemSerialValue {
    # 只读序列号取值链（顺序借 Get-IccProfile-fixed.ps1；只读借鉴，不调用它的下载/写盘逻辑）
    try {
        $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\SystemInformation')
        if ($k) { $x = [string]$k.GetValue('SystemSerialNumber'); if ($x -and $x.Trim()) { return $x.Trim() } }
    } catch { }
    try {
        $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('HARDWARE\DESCRIPTION\System\BIOS')
        if ($k) { $x = [string]$k.GetValue('BaseBoardSerialNumber'); if ($x -and $x.Trim()) { return $x.Trim() } }
    } catch { }
    foreach ($cls in @('Win32_BIOS', 'Win32_ComputerSystemProduct')) {
        try {
            $x = [string](Get-CimInstance -ClassName $cls -ErrorAction Stop).SerialNumber
            if ($x -and $x.Trim()) { return $x.Trim() }
        } catch { }
    }
    return ''
}

function Get-WiredMacValue {
    # 有线网卡 MAC（去分隔符、大写），与 GCU 的取法二命名规则一致
    $nics = @()
    try { $nics = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue) } catch { $nics = @() }
    $c1 = @($nics | Where-Object { $_.MediaType -like '*802.3*' -and $_.Status -eq 'Up' })
    $c2 = @($nics | Where-Object { $_.MediaType -like '*802.3*' })
    $nic = $null
    if ($c1.Count -gt 0) { $nic = $c1[0] } elseif ($c2.Count -gt 0) { $nic = $c2[0] }
    if (-not $nic) { return '' }
    $mac = [regex]::Replace([string]$nic.MacAddress, '[^0-9A-Fa-f]', '')
    if ($mac.Length -ne 12) { return '' }
    return $mac.ToUpperInvariant()
}

function Get-MonitorPnpValue {
    # 带 EDID 的第一个显示器 PnP ID（HKLM\SYSTEM\...\Enum\DISPLAY 下的硬件 ID）
    $base = 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY'
    try {
        foreach ($pnp in @(Get-ChildItem -LiteralPath $base -ErrorAction SilentlyContinue)) {
            foreach ($inst in @(Get-ChildItem -LiteralPath $pnp.PSPath -ErrorAction SilentlyContinue)) {
                try {
                    $edid = (Get-ItemProperty -Path (Join-Path $inst.PSPath 'Device Parameters') -Name EDID -ErrorAction SilentlyContinue).EDID
                    if ($edid -and $edid.Length -ge 128) { return [string]$pnp.PSChildName }
                } catch { }
            }
        }
    } catch { }
    return ''
}

function Invoke-HttpProbeHead {
    # 只读探测：只取 状态码 / Content-Length / 前 4 字节幻数。
    # ★ 响应体【绝不落盘】——直接读流的头 4 字节就关掉，所以不需要任何临时文件。
    param([string]$Url, [int]$TimeoutMs = 8000)
    $r = @{ Http = -1; Len = -1; Head = ''; Kind = 'n/a'; Ms = 0; Note = '' }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $resp = $null
    $st = $null
    try {
        $req = [System.Net.HttpWebRequest]::Create($Url)
        $req.Method = 'GET'
        $req.Timeout = $TimeoutMs
        $req.ReadWriteTimeout = $TimeoutMs
        $req.AllowAutoRedirect = $true
        $req.MaximumAutomaticRedirections = 2
        $req.UserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36'
        $req.Accept = 'application/zip,*/*'
        try {
            $resp = $req.GetResponse()
            $r.Http = [int]$resp.StatusCode
            try { $r.Len = [int64]$resp.ContentLength } catch { $r.Len = -1 }
        } catch [System.Net.WebException] {
            $wr = $_.Exception.Response
            if ($wr) { try { $r.Http = [int]([System.Net.HttpWebResponse]$wr).StatusCode } catch { $r.Http = -1 } }
            $r.Note = ($_.Exception.Message -replace '\s+', ' ')
        }
        if ($resp) {
            try {
                $st = $resp.GetResponseStream()
                $buf = New-Object byte[] 4
                $got = 0
                while ($got -lt 4) {
                    $n = $st.Read($buf, $got, 4 - $got)
                    if ($n -le 0) { break }
                    $got += $n
                }
                if ($got -gt 0) {
                    if ($got -eq 1) { $r.Head = $buf[0].ToString('x2') } else { $r.Head = (($buf[0..($got - 1)] | ForEach-Object { $_.ToString('x2') }) -join ' ') }
                    if ($got -ge 2 -and $buf[0] -eq 0x50 -and $buf[1] -eq 0x4B) { $r.Kind = 'ZIP（PK 幻数）' }
                    elseif ($buf[0] -eq 0x3C) { $r.Kind = 'HTML/XML（< 开头）' }
                    else { $r.Kind = 'binary/text' }
                } elseif ($r.Http -ge 200 -and $r.Http -lt 300) { $r.Kind = 'EMPTY（200 但正文为空）' }
            } catch { }
        }
    } catch {
        $r.Note = ($_.Exception.Message -replace '\s+', ' ')
    } finally {
        if ($st) { try { $st.Dispose() } catch { } }
        if ($resp) { try { $resp.Close() } catch { } }
        $sw.Stop()
    }
    $r.Ms = $sw.ElapsedMilliseconds
    return $r
}

function Get-SnapshotText {
    # 只读自证：把快照【内容本身】（注册表 4 键的值 + 颜色目录文件清单）落进 07 节，便于逐项复核
    $sb = New-Object System.Text.StringBuilder
    foreach ($k in @(
            'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures',
            'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting',
            'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport',
            'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display')) {
        $r = Get-RegValues -Path $k
        [void]$sb.AppendLine(('KEY ' + $k))
        [void]$sb.AppendLine(('    读取：' + $(if ($r.Ok) { '成功' } else { '失败（' + $(if ($r.Err) { $r.Err } else { '未知原因' }) + '）' }) + ('；值数：{0}' -f $r.Values.Count)))
        foreach ($v in $r.Values) { [void]$sb.AppendLine(('    {0,-32} [{1,-8}] = {2}' -f $v.Name, $v.Type, $v.Value)) }
    }
    foreach ($d in (Get-ProfileDirList)) {
        [void]$sb.AppendLine(('DIR ' + $d.Label + ' : ' + $d.Path))
        if (-not (Test-Path -LiteralPath $d.Path)) { [void]$sb.AppendLine('    （目录不存在）'); continue }
        $files = @(Get-ChildItem -LiteralPath $d.Path -File -ErrorAction SilentlyContinue | Sort-Object Name)
        [void]$sb.AppendLine(('    文件 {0} 个' -f $files.Count))
        foreach ($f in $files) {
            [void]$sb.AppendLine(('    {0}  {1} 字节  mtime {2}' -f $f.Name, $f.Length, $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
        }
    }
    return $sb.ToString()
}

# ================================================================ 参数与输出目录
if (-not $OutDir -or $OutDir.Trim().Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $desktop = ''
    try { $desktop = [Environment]::GetFolderPath('Desktop') } catch { }
    if (-not $desktop) { $desktop = Join-Path $env:USERPROFILE 'Desktop' }
    $OutDir = Join-Path $desktop ('LM-SRGB诊断-' + $stamp)
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
Ensure-Dir $OutDir

$isAdmin = Test-Admin

$P = [pscustomobject]@{
    OutDir        = $OutDir
    Admin         = $isAdmin
    Model         = ''
    BaseBoard     = ''
    Bios          = ''
    Project       = ''
    Gen           = ''
    LmVersion     = ''
    OsName        = ''
    OsBuild       = ''
    DispMode      = ''
    ColorCalib    = ''
    CcSupport     = ''
    AssocResult   = ''
    DefaultProf   = ''
    SpoolIccCount = 0
    LmLog         = ''
    LmKeyLines    = 0
    SrvOk         = 0
    SrvTotal      = 3
    Conclusion    = ''
    GcuUtilRaw    = ''
    GcuUtilPath   = ''
    GcuUtilValid  = ''
    GcuUtilNote   = ''
    AssocNames    = ''
    AssocMatch    = ''
    LogNewest     = ''
    LogTimeliness = ''
    ReqMatrix     = ''
}
# ⚠ 写 00-概要 只用这个独立引用：PowerShell 变量名大小写不敏感，
#   真机实测 5c 节的 `foreach ($p in $lm)` 会把 $P 这个“概要对象”整个顶掉
#   ⇒ 00-概要 全空却仍写“无告警”。该引用不依赖变量名，不会被同名变量覆盖。
$script:SummaryRef = $P
# ================================================================ 头部
Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' 耀世 / 调 SRGB 跳回默认 —— 只读诊断抓包' -ForegroundColor White
Write-Host (" 脚本版本 : " + $SCRIPT_VERSION)
Write-Host (" 运行时间 : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host (" 输出目录 : " + $OutDir)
Write-Host (" 权限     : " + $(if ($isAdmin) { '管理员' } else { '普通用户（本诊断只读，无需提权；个别 HKLM 项读不到会写明）' }))
Write-Host '============================================================' -ForegroundColor White
Write-Host ' ⚠ 本脚本严格只读：不改注册表 / 不动 ICC / 不写系统颜色目录 / 不启停服务。' -ForegroundColor Green

# ================================================================ 00 概要
$script:WarnCount = @($script:Warnings).Count
$script:SnapshotBefore = Get-SnapshotText
Step '00 概要 / 机型与校色状态结论' | Out-Null

try {
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
    $P.Model = ([string]$cs.Manufacturer + ' ' + [string]$cs.Model).Trim()
} catch { Warn ('机器型号读取失败：' + $_.Exception.Message) }
try {
    $bb = Get-CimInstance -ClassName Win32_BaseBoard -ErrorAction Stop
    $P.BaseBoard = ([string]$bb.Manufacturer + ' ' + [string]$bb.Product + ' ' + [string]$bb.Version).Trim()
} catch { Warn ('主板读取失败：' + $_.Exception.Message) }
try {
    $b = Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop
    $P.Bios = [string]$b.SMBIOSBIOSVersion
    if (-not $P.Bios) { $P.Bios = [string]$b.Version }
} catch { Warn ('BIOS 读取失败：' + $_.Exception.Message) }
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $P.OsName = [string]$os.Caption
    $P.OsBuild = [string]$os.BuildNumber
} catch { Warn ('OS 信息读取失败：' + $_.Exception.Message) }

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
    $pk = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\OEM\GamingCenter2\ItemSupport')
    if ($pk) {
        $v2 = [string]$pk.GetValue('BIOS_PROJECT_ID')
        if ($v2) { $P.Project += ('ItemSupport\BIOS_PROJECT_ID=' + $v2 + ' ') }
        $pk.Close()
    }
} catch { }
if (-not $P.Project) { $P.Project = '(未取到)' }

# 项目号 -> 世代判断（耀世 X6AR55xY / project IDY / 50 系）
$P.Gen = '(未判定)'
try {
    $projStr = $P.Project
    if ($projStr -match 'IDY' -or $P.Model -match 'X6AR55') { $P.Gen = '判为耀世 X6AR55xY / project IDY / 50 系' }
    elseif ($projStr -match 'IDO' -or $P.Model -match 'GM5AR0O') { $P.Gen = '判为极光Pro GM5AR0O / project IDO / Gen40' }
    elseif ($projStr -match 'IDA' -or $P.Model -match 'GM5HG0A') { $P.Gen = '判为翼龙15Pro GM5HG0A / project IDA / Gen40' }
    else { $P.Gen = '型号/项目号不在已知表里（本脚本只做取证，不强行归类）' }
} catch { }

$P.LmVersion = ''
$lmDll = Join-Path $env:ProgramFiles 'L-Mechrevo\L-Mechrevo.dll'
try {
    if (Test-Path -LiteralPath $lmDll) {
        $fi = Get-Item -LiteralPath $lmDll -ErrorAction Stop
        $fv = ''; $pv = ''
        try { $fv = [string]$fi.VersionInfo.FileVersion } catch { }
        try { $pv = [string]$fi.VersionInfo.ProductVersion } catch { }
        $P.LmVersion = ('FileVersion=' + $(if ($fv) { $fv } else { '(无)' }) + ' / ProductVersion=' + $(if ($pv) { $pv } else { '(无)' }) + '（' + $lmDll + '）')
    } else { $P.LmVersion = ('(未找到 ' + $lmDll + ')') }
} catch { $P.LmVersion = ('(读取失败：' + $_.Exception.Message + ')') }

# 一句话结论所需的三个关键值
$dfPath = 'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures'
$msPath = 'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting'
$itPath = 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'
try { $P.DispMode = [string](Get-ItemPropertyValue -LiteralPath $dfPath -Name 'DisplayMode' -ErrorAction Stop) } catch { $P.DispMode = '(读不到)' }
try { $P.ColorCalib = [string](Get-ItemPropertyValue -LiteralPath $dfPath -Name 'ColorCalibration' -ErrorAction Stop) } catch { $P.ColorCalib = '(不存在/读不到)' }
try { $P.CcSupport = [string](Get-ItemPropertyValue -LiteralPath $itPath -Name 'ColorCalibrationSupport' -ErrorAction Stop) } catch { $P.CcSupport = '(不存在/读不到)' }
try { $P.AssocResult = [string](Get-ItemPropertyValue -LiteralPath $msPath -Name 'AssociateColorProfileResult' -ErrorAction Stop) } catch { $P.AssocResult = '(不存在/读不到)' }

$P.Conclusion = ('注册表当前档位 ColorCalibration=' + $P.ColorCalib +
    '（1=默认/2=SRGB；GCU 只在 result==1 时才回写此值）、' +
    'ItemSupport\ColorCalibrationSupport=' + $P.CcSupport +
    '、MySetting\AssociateColorProfileResult=' + $P.AssocResult +
    '。若日志里出现 result=4(ColorCalibrationFail) 则说明 ICC 链失败 ⇒ 本次重点看 02/03/04。')

Write-Host ('  机器：' + $P.Model + '   ' + $P.Gen)
Write-Host ('  LM  ：' + $P.LmVersion)
Write-Host ('  结论：' + $P.Conclusion)

# ================================================================ 01 显示与校色状态
Step '01 显示与校色状态（只读注册表）' | Out-Null

$s01 = New-Object System.Text.StringBuilder
[void]$s01.AppendLine('01 显示与校色状态（只读采集；不改任何值）')
[void]$s01.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s01.AppendLine(('是否管理员：' + $(if ($isAdmin) { '是' } else { '否（只读诊断不需要；个别 HKLM 项可能读不到）' })))
[void]$s01.AppendLine('')

# 1a ItemSupport 下所有"校色相关"键值（以实际存在为准）
[void]$s01.AppendLine('== 1a HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport 里的校色相关键值 ==')
try {
    $r = Get-RegValues -Path $itPath
    if (-not $r.Ok) {
        [void]$s01.AppendLine(('  （读不到：' + $r.Err + '）'))
        Warn ('ItemSupport 读取失败：' + $r.Err)
    } else {
        $hit = @($r.Values | Where-Object { $_.Name -match 'Color|Calib|ICC|Display|HDR|sRGB|P3' })
        [void]$s01.AppendLine(('  该键共有 {0} 个值，其中校色/显示相关 {1} 个：' -f $r.Values.Count, $hit.Count))
        if ($hit.Count -eq 0) { [void]$s01.AppendLine('  （没有匹配 Color/Calib/ICC/Display/HDR 的值）') }
        foreach ($v in $hit) { [void]$s01.AppendLine(("    {0,-32} [{1,-8}] = {2}" -f $v.Name, $v.Type, $v.Value)) }
        [void]$s01.AppendLine('')
        [void]$s01.AppendLine('  --- ItemSupport 全部值（便于对表）---')
        foreach ($v in $r.Values) { [void]$s01.AppendLine(("    {0,-32} [{1,-8}] = {2}" -f $v.Name, $v.Type, $v.Value)) }
    }
} catch { Warn ('01a 处理失败：' + $_.Exception.Message) }
[void]$s01.AppendLine('')

# 1b DisplayFeatures
[void]$s01.AppendLine('== 1b HKLM\SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures ==')
Format-RegSection -Sb $s01 -Path $dfPath -Title 'DisplayFeatures（ColorCalibration / DisplayMode / DisplayFeatureStatus 等）'
[void]$s01.AppendLine('')

# 1c MySetting 全量（AssociateColorProfileResult 等）
[void]$s01.AppendLine('== 1c HKLM\SOFTWARE\OEM\GamingCenter2\MySetting ==')
Format-RegSection -Sb $s01 -Path $msPath -Title 'MySetting（含 AssociateColorProfileResult、HDRSwitch 等）'
[void]$s01.AppendLine('')

# 1d GamingCenter2 子树 + MySetting 子键（看还有没有别的校色节点）
[void]$s01.AppendLine('== 1d GamingCenter2 子树清单（找有没有漏掉的校色节点）==')
try {
    $subs = Get-RegSubKeys -Path 'HKLM:\SOFTWARE\OEM\GamingCenter2'
    foreach ($s in $subs) {
        [void]$s01.AppendLine(('    ' + $s))
        if ($s -match 'MySetting') {
            foreach ($s2 in (Get-RegSubKeys -Path ('HKLM:\SOFTWARE\OEM\GamingCenter2\' + $s))) {
                [void]$s01.AppendLine(('      \' + $s2))
            }
        }
    }
} catch { Warn ('01d 子树枚举失败：' + $_.Exception.Message) }
[void]$s01.AppendLine('')

# 1e 系统级显示状态：HDR / 分辨率 / 显示器
[void]$s01.AppendLine('== 1e HDR / 显示状态（只读）==')
try {
    $vk = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\VideoSettings'
    Format-RegSection -Sb $s01 -Path $vk -Title 'VideoSettings（EnableHDRForPlayback / VideoQualityOnBattery 等）'
} catch { Warn ('1e VideoSettings 失败：' + $_.Exception.Message) }
try {
    $ccd = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\VideoSettings'
    Format-RegSection -Sb $s01 -Path $ccd -Title 'HKLM VideoSettings'
} catch { }
[void]$s01.AppendLine('')
[void]$s01.AppendLine('  --- 当前显示器（WmiMonitorID，EDID 实例名）---')
try {
    $mons = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction Stop)
    if ($mons.Count -eq 0) { [void]$s01.AppendLine('    （未取到任何显示器）') }
    foreach ($m in $mons) {
        $name = ''
        $mfg = ''
        try {
            if ($m.UserFriendlyName) { $name = (($m.UserFriendlyName | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join '') }
        } catch { }
        try {
            if ($m.ManufacturerName) { $mfg = (($m.ManufacturerName | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join '') }
        } catch { }
        [void]$s01.AppendLine(("    实例 : " + $m.InstanceName))
        [void]$s01.AppendLine(("      名称(ID) : " + $name + "   制造商码:" + $mfg + "   年份:" + $m.YearOfManufacture))
    }
} catch { [void]$s01.AppendLine(('    （读取失败：' + $_.Exception.Message + '；非管理员属预期）')); Warn ('WmiMonitorID 读取失败：' + $_.Exception.Message) }
[void]$s01.AppendLine('')
[void]$s01.AppendLine('  --- 显示适配器（只读）---')
try {
    foreach ($v in @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop)) {
        [void]$s01.AppendLine(("    {0}  |  驱动 {1}  |  状态 {2}" -f $v.Name, $v.DriverVersion, $v.Status))
    }
} catch { Warn ('显示适配器读取失败：' + $_.Exception.Message) }
# 1f 三个 ItemSupport / 三个 RGBKeyboard 路径（2026-10-09 方案B 补：原来只读 GamingCenter2 一处）
# ⚠ 内置 MechrevoDeviceCapabilities.ItemSupportPaths / RgbKeyboardPaths 都是【三个路径】（GamingCenter2 /
#    GamingCenter / ControlCenter）。只读一处时，官方控制台装在另外两个路径的机型上会"看不到校色能力位"
#    —— 这是本批认定"确实缺"的显示/色彩项，所以这里补齐（只读，不做任何写入）。
[void]$s01.AppendLine('== 1f 校色/显示能力位的多路径读取（与内置 ItemSupportPaths / RgbKeyboardPaths 同口径）==')
$itPathsAll = @('HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport', 'HKLM:\SOFTWARE\OEM\GamingCenter\ItemSupport', 'HKLM:\SOFTWARE\OEM\ControlCenter\ItemSupport')
$rgbPathsAll = @('HKLM:\SOFTWARE\OEM\GamingCenter2\RGBKeyboard', 'HKLM:\SOFTWARE\OEM\GamingCenter\RGBKeyboard', 'HKLM:\SOFTWARE\OEM\ControlCenter\RGBKeyboard')
$ccKeysAll = @('ColorCalibrationSupport', 'ColorCalibrationLevel', 'ColorCalibration', 'DisplayFeatures', 'DispalyRefresh',
                'DisplayMode', 'LCDOverdriveSupport', 'LocalDimmingSupport', 'HDRSwitch', 'PanelType', 'IsNvGpu', 'IsAMDPlatform')
foreach ($p in $itPathsAll) {
    $r = Get-RegValues -Path $p
    [void]$s01.AppendLine(('  -- ' + $p + ' --'))
    if (-not $r.Ok) { [void]$s01.AppendLine(('    不存在/读不到（' + $r.Err + '）')); continue }
    $hit2 = @($r.Values | Where-Object { $_.Name -match 'Color|Calib|ICC|Display|HDR|sRGB|P3' })
    [void]$s01.AppendLine(('    该键共 {0} 个值，其中校色/显示相关 {1} 个' -f $r.Values.Count, $hit2.Count))
    foreach ($v in ($hit2 | Sort-Object { $_.Name })) { [void]$s01.AppendLine(("      {0,-32} [{1,-8}] = {2}" -f $v.Name, $v.Type, $v.Value)) }
}
[void]$s01.AppendLine('  -- ★ 校色/显示关键键逐项点名（三个路径都找不到就明确写"不存在"）--')
foreach ($k in $ccKeysAll) {
    $found = $false
    $lines = @()
    foreach ($p in $itPathsAll) {
        $r = Get-RegValues -Path $p
        if (-not $r.Ok) { continue }
        foreach ($v in $r.Values) { if ($v.Name -ieq $k) { $found = $true; $lines += ($p + ' → ' + $v.Value) } }
    }
    if ($found) { [void]$s01.AppendLine(('    ' + $k + ' = ' + ($lines -join '；'))) }
    else { [void]$s01.AppendLine(('    ' + $k + ' = 不存在（三个 ItemSupport 路径都没有这个值）')) }
}
foreach ($p in $rgbPathsAll) {
    $r = Get-RegValues -Path $p
    [void]$s01.AppendLine(('  -- ' + $p + '（灯效/面板相关，供校色旁证）--'))
    if (-not $r.Ok) { [void]$s01.AppendLine(('    不存在/读不到（' + $r.Err + '）')); continue }
    foreach ($v in ($r.Values | Sort-Object { $_.Name })) { [void]$s01.AppendLine(("      {0,-32} [{1,-8}] = {2}" -f $v.Name, $v.Type, $v.Value)) }
}
[void]$s01.AppendLine('')
Write-Utf8 -Path (Join-Path $OutDir '01-显示与校色状态.txt') -Text $s01.ToString()

# ================================================================ 02 ICC 关联现状
Step '02 ICC 关联现状（只读）' | Out-Null

$s02 = New-Object System.Text.StringBuilder
[void]$s02.AppendLine('02 ICC 关联现状（只读；不改关联、不装/删 ICC）')
[void]$s02.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s02.AppendLine('')
[void]$s02.AppendLine('  ⚠ 说明：档位=2 的机型 sRGB 走 UserSetColorCalibration -> StartICCProfile(mode)，')
[void]$s02.AppendLine('     依赖 ICC 下载/应用。本节回答"系统当前到底关联了哪个 ICC"。')
[void]$s02.AppendLine('')

# 2a 当前默认关联
[void]$s02.AppendLine('== 2a 当前默认关联 ==')
try {
    $gp = Get-Command -Name 'Get-ColorProfile' -ErrorAction SilentlyContinue
    if ($gp) {
        [void]$s02.AppendLine('  Get-ColorProfile 可用，输出：')
        try {
            foreach ($cp in @(Get-ColorProfile -ErrorAction Stop)) {
                [void]$s02.AppendLine(("    " + [string]$cp))
            }
        } catch { [void]$s02.AppendLine(('    （Get-ColorProfile 调用失败：' + $_.Exception.Message + '）')) }
    } else {
        [void]$s02.AppendLine('  Get-ColorProfile 在本机不可用（该 cmdlet 多见于 Windows 11 的 ColorManagement 模块）')
        [void]$s02.AppendLine('  ⇒ 退回注册表读取（等价信息）。')
    }
} catch { Warn ('2a Get-ColorProfile 探测失败：' + $_.Exception.Message) }
[void]$s02.AppendLine('')
[void]$s02.AppendLine('  --- HKCU ICM\ProfileAssociations\Display（★ 用户级"当前默认关联"就在这）---')
$assocRoot = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display'
$assocProfiles = New-Object System.Collections.ArrayList
try {
    $devKeys = @(Get-RegSubKeys -Path $assocRoot)
    if ($devKeys.Count -eq 0) { [void]$s02.AppendLine('    （该键不存在或没有子键）') }
    foreach ($d in $devKeys) {
        [void]$s02.AppendLine(('    [设备类/编号] ' + $d))
        $leafKeys = @(Get-RegSubKeys -Path ($assocRoot + '\' + $d))
        $targets = @()
        if ($leafKeys.Count -gt 0) { foreach ($l in $leafKeys) { $targets += ($assocRoot + '\' + $d + '\' + $l) } }
        else { $targets = @($assocRoot + '\' + $d) }
        foreach ($tk in $targets) {
            $leaf = $tk.Substring($assocRoot.Length).TrimStart('\')
            $r = Get-RegValues -Path $tk
            if (-not $r.Ok) { [void]$s02.AppendLine(('      [子键 ' + $leaf + '] ⇒ 读不到：' + $r.Err)); continue }
            [void]$s02.AppendLine(('      [子键 ' + $leaf + ']'))
            foreach ($v in $r.Values) {
                [void]$s02.AppendLine(("        {0} = {1}" -f $v.Name, $v.Value))
                # ⚠ 关联里的值是【数组形态】（显示成 [xxx.icm]），所以不能用 '\.ic[cm]$' 锚定行尾
                if (([string]$v.Value) -match '\.ic[cm]') {
                    foreach ($mm in ([regex]::Matches([string]$v.Value, '[^\[\],\s]+\.ic[cm]'))) {
                        [void]$assocProfiles.Add($mm.Value)
                    }
                }
            }
        }
    }
    $assocProfiles = @($assocProfiles | Select-Object -Unique)
    if ($assocProfiles.Count -gt 0) { $P.DefaultProf = ($assocProfiles -join ', ') } else { $P.DefaultProf = '(未在关联里解析到 .icc/.icm)' }
    [void]$s02.AppendLine('')
    [void]$s02.AppendLine(('  ⇒ 从关联里解析出的 ICC：' + $P.DefaultProf))
} catch {
    Warn ('2a 关联读取失败：' + $_.Exception.Message)
    [void]$s02.AppendLine(('    （该项失败：' + $_.Exception.Message + '）'))
}
[void]$s02.AppendLine('')
[void]$s02.AppendLine('  --- 旧式 ICMProfile 值（HKLM\...\ICM\RegisteredProfiles 与显示类键）---')
try {
    Format-RegSection -Sb $s02 -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles' -Title 'HKLM ICM\RegisteredProfiles'
    Format-RegSection -Sb $s02 -Path 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\RegisteredProfiles' -Title 'HKCU ICM\RegisteredProfiles'
    Format-RegSection -Sb $s02 -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\Calibration' -Title 'HKLM ICM\Calibration（WCS 校准状态）'
} catch { Warn ('2a 旧式 ICM 键读取失败：' + $_.Exception.Message) }
[void]$s02.AppendLine('')

# 2b 系统范围 SYSTEM_WIDE 关联
[void]$s02.AppendLine('== 2b 系统范围（SYSTEM_WIDE / 机器级）关联 ==')
[void]$s02.AppendLine('  说明：WCS 的 SYSTEM_WIDE 关联落在 HKLM 的 ICM\ProfileAssociations（若存在）。')
try {
    $sysRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations'
    if (Test-Path -LiteralPath $sysRoot) {
        $subs = @(Get-RegSubKeys -Path $sysRoot)
        if ($subs.Count -eq 0) { [void]$s02.AppendLine('    （键存在但没有子键）') }
        foreach ($d in $subs) {
            $tk = $sysRoot + '\' + $d
            $r = Get-RegValues -Path $tk
            [void]$s02.AppendLine(('    [SYSTEM_WIDE] ' + $d))
            if ($r.Ok) { foreach ($v in $r.Values) { [void]$s02.AppendLine(("      {0} = {1}" -f $v.Name, $v.Value)) } }
            else { [void]$s02.AppendLine(('      （读不到：' + $r.Err + '；HKLM 受限，可能要管理员）')) }
        }
    } else {
        [void]$s02.AppendLine('    HKLM\...\ICM\ProfileAssociations 不存在 ⇒ 本机没有机器级（SYSTEM_WIDE）关联记录。')
        [void]$s02.AppendLine('    （这与"只有用户级关联"的常见情况一致，不是故障。）')
    }
} catch { Warn ('2b SYSTEM_WIDE 读取失败：' + $_.Exception.Message) }
[void]$s02.AppendLine('')

# 2c 实际存在的 ICC 列表与时间戳
[void]$s02.AppendLine('== 2c ICC 文件实际存在情况（只列，不动）==')
$iccDirs = New-Object System.Collections.ArrayList
[void]$iccDirs.Add(@{ Label = '系统颜色目录(系统范围安装位置)'; Path = Join-Path $env:SystemRoot 'System32\spool\drivers\color' })
[void]$iccDirs.Add(@{ Label = '用户颜色目录(Local)'; Path = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Color' })
[void]$iccDirs.Add(@{ Label = '用户颜色目录(Roaming)'; Path = Join-Path $env:APPDATA 'Microsoft\Windows\Color' })
[void]$iccDirs.Add(@{ Label = 'ProgramData 颜色目录'; Path = Join-Path $env:ProgramData 'Microsoft\Windows\Color' })
[void]$iccDirs.Add(@{ Label = 'LM 自定义 ICC'; Path = Join-Path $env:ProgramFiles 'L-Mechrevo\customICC' })
[void]$iccDirs.Add(@{ Label = 'LM 安装目录'; Path = (Join-Path $env:ProgramFiles 'L-Mechrevo') })
$spoolList = @()
foreach ($d in $iccDirs) {
    [void]$s02.AppendLine(('  --- ' + $d.Label + ' ---'))
    [void]$s02.AppendLine(('  路径：' + $d.Path))
    if (-not (Test-Path -LiteralPath $d.Path)) { [void]$s02.AppendLine('    （不存在/本机没有这一项 ⇒ 优雅降级）'); continue }
    try {
        $files = @(Get-ChildItem -LiteralPath $d.Path -File -ErrorAction Stop | Where-Object { $_.Extension -match '^\.ic[cm]$' })
        if ($d.Label -eq '系统颜色目录(系统范围安装位置)') { $spoolList = $files }
        [void]$s02.AppendLine(('    共 {0} 个 .icc/.icm' -f $files.Count))
        foreach ($f in ($files | Sort-Object LastWriteTime -Descending)) {
            [void]$s02.AppendLine(("      {0,-40} {1,9} 字节   mtime {2}" -f $f.Name, $f.Length, $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))
        }
    } catch {
        [void]$s02.AppendLine(('    （枚举失败：' + $_.Exception.Message + '）'))
        [void]$s02.AppendLine('    ⚠ 非管理员下该目录可能只能看到部分文件 ⇒ 属优雅降级；需要完整清单时请以管理员重跑。')
        Warn ('ICC 目录枚举失败 ' + $d.Path + '：' + $_.Exception.Message)
    }
}
$P.SpoolIccCount = @($spoolList).Count
[void]$s02.AppendLine('')

# 2d 当前生效的那个（★ 规范化匹配：大小写不敏感 + 别名表，真机实测 Default.icm/sRGB.icm 会误报）
[void]$s02.AppendLine('== 2d 当前生效的 ICC 是谁（把关联名与磁盘文件对上）==')
[void]$s02.AppendLine('  ★ 匹配规则（四级，逐级放宽）：① 名字本身是绝对路径 ② 文件名精确（大小写不敏感）')
[void]$s02.AppendLine('     ③ 常见别名表（sRGB.icm / Default.icm ⇔ sRGB Color Space Profile.icm、AdobeRGB.icm ⇔ AdobeRGB1998.icc 等）')
[void]$s02.AppendLine('     ④ 规范化主干相同（去掉空格/点/连字符/扩展名后一致）。')
[void]$s02.AppendLine('  ⚠ 真机实证：关联里写 Default.icm / sRGB.icm，磁盘上只有 sRGB Color Space Profile.icm ⇒')
[void]$s02.AppendLine('     只用精确匹配会误报"关联指向不存在的 ICC"，所以这里必须做规范化匹配。')
try {
    $names = @()
    if ($P.DefaultProf -match '\.ic[cm]') { $names = @($P.DefaultProf -split ',\s*' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    if ($names.Count -eq 0) {
        [void]$s02.AppendLine('  关联里没有 ICC 名 ⇒ 无法判定"当前生效"。')
        [void]$s02.AppendLine('  ⚠ 这本身就是线索：档位=2 依赖 ICC 被关联，若关联为空，sRGB 很可能应用不上。')
    } else {
        foreach ($n in $names) {
            $found = @(Find-ProfileOnDisk -Name $n -NeedHeader $true)
            if ($found.Count -gt 0) {
                foreach ($f in $found) {
                    [void]$s02.AppendLine(('  ⇒ 匹配到：' + $f.Path))
                    [void]$s02.AppendLine(('     依据={0}；目录={1}；{2} 字节；mtime {3}；ICC 头校验={4}' -f $f.How, $f.DirLabel, $f.Len, $f.Mtime.ToString('yyyy-MM-dd HH:mm:ss'), $(if ($f.Header) { '通过（acsp 魔数在）' } else { '⚠ 未通过（不是合法 ICC）' })))
                }
            } else {
                [void]$s02.AppendLine(('  ⇒ 未匹配到：关联里写着 ' + $n + '，四个颜色目录里都没找到（含别名与规范化匹配）'))
                [void]$s02.AppendLine('     ⚠ "关联指向不存在的 ICC"是 ICC 应用失败的典型原因；但也可能是别名不在表里，')
                [void]$s02.AppendLine('       请人工在 2c 的文件清单里对一眼，不要只凭本行下结论。')
            }
        }
    }
} catch { Warn ('2d 生效判定失败：' + $_.Exception.Message) }
# == 关联名 <-> 磁盘 的匹配汇总（供 00 概要引用；带别名表）==
try {
    $an = @()
    if ($P.DefaultProf -match '\.ic[cm]') { $an = @($P.DefaultProf -split ',\s*' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    if ($an.Count -gt 0) {
        $okN = 0
        $badN = @()
        foreach ($n in $an) {
            if (@(Find-ProfileOnDisk -Name $n -NeedHeader $false).Count -gt 0) { $okN++ } else { $badN += $n }
        }
        $P.AssocNames = ($an -join ', ')
        $P.AssocMatch = ('{0}/{1} 匹配到磁盘' -f $okN, $an.Count)
        if ($badN.Count -gt 0) { $P.AssocMatch += ('；未匹配：' + ($badN -join ', ')) }
    } else {
        $P.AssocNames = '(关联里没有 ICC 名)'
        $P.AssocMatch = '无法匹配（关联为空）'
    }
} catch { }
[void]$s02.AppendLine('')
[void]$s02.AppendLine('  ⚠ 局限（只读约束下的诚实说明）：本脚本【不用】句柄类工具，所以判断"当前生效的 ICC"')
[void]$s02.AppendLine('     依据是 WCS 关联值（ICMProfile / ICMProfileSnapshot）而不是"哪个进程正打开着文件"。')
[void]$s02.AppendLine('     结论以 2a 的关联值 + 2c/2d 的文件实在性 为准即可。')
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== 2f MySetting\GCUUtil 里指定的 sRGB ICC（★ GCU 真正要应用的那个）==')
try {
    $gcuRaw = ''
    try { $gcuRaw = [string](Get-ItemPropertyValue -LiteralPath $msPath -Name 'GCUUtil' -ErrorAction Stop) } catch { $gcuRaw = '' }
    $P.GcuUtilRaw = $gcuRaw
    if (-not $gcuRaw) {
        $P.GcuUtilPath = '(未取到)'
        $P.GcuUtilValid = '(无法校验)'
        $P.GcuUtilNote = 'MySetting\GCUUtil 不存在或读不到'
        [void]$s02.AppendLine('  MySetting\GCUUtil：不存在 / 读不到（本机没有这一项 ⇒ 优雅降级，不是故障）')
    } else {
        [void]$s02.AppendLine(('  GCUUtil 原值：' + $gcuRaw))
        $gcuPath = ''
        try {
            $mm = [regex]::Match($gcuRaw, '\[([A-Za-z]:[^\]]+)\]')
            if ($mm.Success) { $gcuPath = $mm.Groups[1].Value.Trim() }
            if (-not $gcuPath) {
                $mm2 = [regex]::Match($gcuRaw, '[A-Za-z]:\\[^\[\]]+\.ic[cm]')
                if ($mm2.Success) { $gcuPath = $mm2.Value.Trim() }
            }
        } catch { }
        if ($gcuPath) {
            $P.GcuUtilPath = $gcuPath
            [void]$s02.AppendLine(('  ★ 解析出 GCU 要应用的 sRGB ICC 路径：' + $gcuPath))
            $gcuExists = $false
            try { $gcuExists = (Test-Path -LiteralPath $gcuPath) } catch { $gcuExists = $false }
            if ($gcuExists) {
                $hdr = Test-IccHeader -Path $gcuPath
                $fi = Get-Item -LiteralPath $gcuPath -ErrorAction SilentlyContinue
                [void]$s02.AppendLine('  ⇒ 磁盘存在性：存在')
                if ($fi) {
                    [void]$s02.AppendLine(('     {0} 字节；mtime {1}；ctime {2}' -f $fi.Length, $fi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $fi.CreationTime.ToString('yyyy-MM-dd HH:mm:ss')))
                }
                [void]$s02.AppendLine(('     ICC 头校验：' + $(if ($hdr.Ok) { '通过' } else { '⚠ 未通过' }) + ('（前 4 字节声明长度={0}，第 37~40 字节魔数="{1}"，期望 "acsp"；版本/标识签="{2}"）' -f $hdr.Declared, $hdr.Magic, $hdr.Sig)))
                if ($hdr.Declared -gt 0 -and $fi -and $hdr.Declared -ne $fi.Length) {
                    [void]$s02.AppendLine(('     ⚠ 头部声明长度({0}) 与文件实际大小({1}) 不一致 ⇒ 文件可能被截断/替换过。' -f $hdr.Declared, $fi.Length))
                }
                if ($hdr.Err) { [void]$s02.AppendLine(('     ⚠ 头校验备注：' + $hdr.Err)) }
                $P.GcuUtilValid = $(if ($hdr.Ok) { '通过（真 ICC）' } else { '未通过（不是合法 ICC 或读不到）' })
                $P.GcuUtilNote = '存在'
                $alt = @(Find-ProfileOnDisk -Name ([System.IO.Path]::GetFileName($gcuPath)) -NeedHeader $true)
                $altOther = @($alt | Where-Object { $_.Path -ine $gcuPath })
                if ($altOther.Count -gt 0) {
                    [void]$s02.AppendLine('     （同名/别名文件在其它颜色目录里也有，可能是系统实际用的那份：）')
                    foreach ($a in $altOther) { [void]$s02.AppendLine(('       ' + $a.Path + ('（{0}，{1} 字节，头校验={2}）' -f $a.How, $a.Len, $(if ($a.Header) { '通过' } else { '未通过' })))) }
                }
            } else {
                [void]$s02.AppendLine('  ⇒ 磁盘存在性：⚠ 不存在（该路径在本机找不到）')
                [void]$s02.AppendLine('     ⚠ 含义：GCU 记着要应用这个 sRGB ICC，但文件不在 ⇒ StartICCProfile 这类流程很可能因此失败。')
                [void]$s02.AppendLine('     ⚠ 注意：本机可能本来就没有 GCU 目录（非游戏本/未装 GCU），此时这行不是故障，属优雅降级。')
                $P.GcuUtilValid = '无法校验（文件不存在）'
                $P.GcuUtilNote = '路径不存在'
                $same = @(Find-ProfileOnDisk -Name ([System.IO.Path]::GetFileName($gcuPath)) -NeedHeader $true)
                if ($same.Count -gt 0) {
                    [void]$s02.AppendLine('     （但四个颜色目录里找到了同名/别名文件 ⇒ GCU 指的那条路径和系统里这份可能不是同一个：）')
                    foreach ($a in $same) { [void]$s02.AppendLine(('       ' + $a.Path + ('（{0}，{1} 字节，头校验={2}）' -f $a.How, $a.Len, $(if ($a.Header) { '通过' } else { '未通过' })))) }
                } else {
                    [void]$s02.AppendLine('     （四个颜色目录里也没有同名/别名文件。）')
                }
            }
        } else {
            $P.GcuUtilPath = '(未解析出路径)'
            $P.GcuUtilValid = '无法校验（未解析出路径）'
            $P.GcuUtilNote = '原值里没有 [盘符:\...] 形式的路径'
            [void]$s02.AppendLine('  ⚠ 未能从原值里解析出 [盘符:\...] 形式的路径 ⇒ 请人工看上面的原值。')
        }
    }
} catch {
    Warn ('2f GCUUtil 解析失败：' + $_.Exception.Message)
    [void]$s02.AppendLine(('    （该项失败：' + $_.Exception.Message + '）'))
    $P.GcuUtilPath = '(解析失败)'
    $P.GcuUtilValid = '(解析失败)'
    $P.GcuUtilNote = $_.Exception.Message
}
[void]$s02.AppendLine('')
[void]$s02.AppendLine('== 2e EDID 里的显示器标识（用来判断 ICC 文件名里的那串 ID 对应哪台屏）==')
try {
    $base = 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY'
    $found = 0
    foreach ($pnp in @(Get-ChildItem -LiteralPath $base -ErrorAction SilentlyContinue)) {
        foreach ($inst in @(Get-ChildItem -LiteralPath $pnp.PSPath -ErrorAction SilentlyContinue)) {
            try {
                $edid = (Get-ItemProperty -Path (Join-Path $inst.PSPath 'Device Parameters') -Name EDID -ErrorAction SilentlyContinue).EDID
                if ($edid -and $edid.Length -ge 128) {
                    [void]$s02.AppendLine(("    显示器硬件 ID: " + $pnp.PSChildName + "   EDID 长度 " + $edid.Length + " 字节"))
                    $found++
                }
            } catch { }
        }
    }
    if ($found -eq 0) { [void]$s02.AppendLine('    （没有读到任何带 EDID 的显示器实例）') }
} catch { [void]$s02.AppendLine(('    （读取失败：' + $_.Exception.Message + '）')) ; Warn ('2e EDID 枚举失败：' + $_.Exception.Message) }
Write-Utf8 -Path (Join-Path $OutDir '02-ICC关联现状.txt') -Text $s02.ToString()

# ================================================================ 03 ICC 服务器可达性
Step '03 ICC 服务器可达性（只读探测 + 真实请求矩阵）' | Out-Null

function Test-TcpOnce {
    param([string]$Target, [int]$Port, [int]$TimeoutMs = 5000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $c = $null
    try {
        $c = New-Object System.Net.Sockets.TcpClient
        $iar = $c.BeginConnect($Target, $Port, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)
        if (-not $ok) { $sw.Stop(); return @{ Ok = $false; Ms = $sw.ElapsedMilliseconds; Note = ('连接超时（>{0}ms）' -f $TimeoutMs) } }
        try { $c.EndConnect($iar); $sw.Stop(); return @{ Ok = $true; Ms = $sw.ElapsedMilliseconds; Note = 'TCP 已连接' } }
        catch { $sw.Stop(); return @{ Ok = $false; Ms = $sw.ElapsedMilliseconds; Note = ($_.Exception.Message -replace '\s+', ' ') } }
    } catch {
        $sw.Stop(); return @{ Ok = $false; Ms = $sw.ElapsedMilliseconds; Note = ($_.Exception.Message -replace '\s+', ' ') }
    } finally {
        if ($c) { try { $c.Close() } catch { } }
    }
}

$iccServers = @(
    @{ Url = 'http://iccprofile.uniwill.com.tw/api/iccprofile/'; Port = 80; Note = 'uniwill 官方' },
    @{ Url = 'http://iccprofile.aistoneglobal.com:8001/api/iccprofile/'; Port = 8001; Note = 'aistone 1 号（⚠ 带 e 才是真名，80 端口不通）' },
    @{ Url = 'http://iccprofile1.aistoneglobal.com:3081/api/iccprofile/'; Port = 3081; Note = 'aistone 2 号（⚠ 带 e 才是真名）' }
)

$s03 = New-Object System.Text.StringBuilder
[void]$s03.AppendLine('03 ICC 服务器可达性（★ 严格只读：DNS + TCP + 真实请求矩阵；只取状态码/长度/前 4 字节）')
[void]$s03.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s03.AppendLine('')
[void]$s03.AppendLine('  三个已知 ICC 服务器（命名规则与列表只读借鉴 tools\Get-IccProfile-fixed.ps1，')
[void]$s03.AppendLine('  本脚本【不】调用它的下载/解压/写盘逻辑，也【不】安装任何 ICC）：')
[void]$s03.AppendLine('  真实包名规则：<PnP ID>_<序列号或 MAC>.zip（一律大写、无分隔符）；逐个服务器地址见下面各节。')
[void]$s03.AppendLine('  ⚠ 本节【不写系统颜色目录、不解压、不安装】；HTTP 只读流的前 4 字节判幻数 ⇒ 响应体不落盘。')
[void]$s03.AppendLine('')

try {
    $proxy = Get-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
    if ($proxy) {
        [void]$s03.AppendLine('== 代理设置（HTTP 探测会受它影响）==')
        [void]$s03.AppendLine(('  ProxyEnable  = ' + [string]$proxy.ProxyEnable))
        [void]$s03.AppendLine(('  ProxyServer  = ' + [string]$proxy.ProxyServer))
        [void]$s03.AppendLine(('  AutoConfigURL= ' + [string]$proxy.AutoConfigURL))
        [void]$s03.AppendLine('  ⚠ ProxyEnable=1 时，下面"HTTP 请求"的结果是走代理的结果，不代表直连可达。')
        [void]$s03.AppendLine('')
    }
} catch { Warn ('代理设置读取失败：' + $_.Exception.Message) }

$srvOk = 0
foreach ($srv in $iccServers) {
    $hostName = ''
    try { $hostName = ([uri]$srv.Url).Host } catch { $hostName = $srv.Url }
    [void]$s03.AppendLine(('== ' + $hostName + '  (' + $srv.Note + ') =='))
    [void]$s03.AppendLine(('  URL : ' + $srv.Url))
    $ips = @()
    try {
        foreach ($a in [System.Net.Dns]::GetHostAddresses($hostName)) { $ips += $a.IPAddressToString }
        [void]$s03.AppendLine(('  DNS : 解析成功 -> ' + (($ips | Select-Object -Unique) -join ', ')))
    } catch {
        [void]$s03.AppendLine(('  DNS : 解析失败 -> ' + ($_.Exception.Message -replace '\s+', ' ')))
    }
    $t = Test-TcpOnce -Target $hostName -Port $srv.Port -TimeoutMs 5000
    [void]$s03.AppendLine(('  TCP : ' + $(if ($t.Ok) { '可达' } else { '不可达' }) + ('（端口 {0}，{1}ms，{2}）' -f $srv.Port, $t.Ms, $t.Note)))
    if ($ips.Count -gt 0) {
        $hr = Invoke-HttpProbeHead -Url $srv.Url -TimeoutMs 8000
        [void]$s03.AppendLine(('  HTTP: 根路径 状态=' + $hr.Http + ('（{0}ms，Content-Length={1}，前 4 字节={2}，类型={3}）' -f $hr.Ms, $hr.Len, $(if ($hr.Head) { $hr.Head } else { '(无正文)' }), $hr.Kind)))
        if ($hr.Http -eq 404) { $srvOk++ }
        elseif ($hr.Http -ge 200 -and $hr.Http -lt 400) { $srvOk++ }
    } else {
        [void]$s03.AppendLine('  HTTP: 跳过（DNS 都没解析成功）')
    }
    [void]$s03.AppendLine('')
}
$P.SrvOk = $srvOk
[void]$s03.AppendLine('  ⚠ 【口径校正】上面请求的是【API 根路径】（没带机型/显示器/序列号参数），')
[void]$s03.AppendLine('     根路径 404 是预期行为，**不作为结论**；真正回答"能不能取到 ICC"的是下面的请求矩阵。')
[void]$s03.AppendLine('')

# ---------------------------------------------------------------- 3b 真实请求矩阵
[void]$s03.AppendLine('== 3b 真实请求矩阵：PnP ID x {序列号, MAC}（3 服务器 x 2 取法 = 6 个请求）==')
$pnpId = ''
$serial = ''
$mac = ''
try { $pnpId = Get-MonitorPnpValue } catch { }
try { $serial = Get-SystemSerialValue } catch { }
try { $mac = Get-WiredMacValue } catch { }

$cands = New-Object System.Collections.ArrayList
$serialFrom = ''
$macFrom = ''
if ($pnpId -and $serial) {
    $serialFrom = '序列号来源：'
    try {
        $k1 = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\SystemInformation')
        $k2 = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('HARDWARE\DESCRIPTION\System\BIOS')
        if ($k1 -and $k1.GetValue('SystemSerialNumber')) { $serialFrom += 'UEFI 注册表' }
        elseif ($k2 -and $k2.GetValue('BaseBoardSerialNumber')) { $serialFrom += '旧系统注册表' }
        else { $serialFrom += 'CIM (Win32_BIOS / Win32_ComputerSystemProduct)' }
    } catch { $serialFrom += '未知' }
    [void]$cands.Add(@{ Scheme = 'pnp+serial'; Name = ([string]$pnpId + '_' + [string]$serial); From = $serialFrom })
}
if ($pnpId -and $mac) {
    $macFrom = '有线网卡 MAC（Get-NetAdapter 物理 802.3，已去分隔符大写）'
    [void]$cands.Add(@{ Scheme = 'pnp+mac'; Name = ([string]$pnpId + '_' + [string]$mac); From = $macFrom })
}
[void]$s03.AppendLine(('  本机取值：PnP ID=' + (Get-TextOr $pnpId '未取到') + '；序列号=' + (Get-TextOr $serial '未取到') + '；有线网卡 MAC=' + (Get-TextOr $mac '未取到')))
[void]$s03.AppendLine('  （序列号/MAC 在报告里的明文会被打码，不影响请求本身。）')
if ($cands.Count -eq 0) {
    [void]$s03.AppendLine('  ⚠ 取法组合为空（PnP ID 或序列号/MAC 未取到）⇒ 无法构造真实请求，本节只能给根路径结果。')
    $P.ReqMatrix = '未构造（PnP ID 或序列号/MAC 未取到）'
} else {
    [void]$s03.AppendLine('')
    [void]$s03.AppendLine('  --- 逐条结果（只取状态码/长度/前 4 字节；无响应体落盘、无临时文件）---')
    [void]$s03.AppendLine(('  {0,-34} {1,-11} {2,-6} {3,-14} {4,-20} {5}' -f '服务器', '取法', '状态', '长度', '前4字节', '判读'))
    [void]$s03.AppendLine(('  名字来源：' + (Get-TextOr $serialFrom '未取到') + '；' + (Get-TextOr $macFrom '未取到')))
    $zipHit = 0
    $noPkg = 0
    $netFail = 0
    foreach ($srv in $iccServers) {
        foreach ($cand in $cands) {
            $url = ([string]$srv.Url + [string]$cand.Name + '.zip')
            $rr = Invoke-HttpProbeHead -Url $url -TimeoutMs 8000
            $verdict = ''
            if ($rr.Http -ge 200 -and $rr.Http -lt 300 -and $rr.Kind -like 'ZIP*') { $verdict = '★ 可取到（HTTP 200 且是 ZIP）'; $zipHit++ }
            elseif ($rr.Http -eq 404) { $verdict = '该组合无包（404）'; $noPkg++ }
            elseif ($rr.Http -ge 200 -and $rr.Http -lt 300) { $verdict = ('服务器 200 但正文不是 ZIP（' + $rr.Kind + '）⇒ 像错误页'); $noPkg++ }
            elseif ($rr.Http -gt 0) { $verdict = ('HTTP ' + $rr.Http + ' ⇒ 服务器应答了但不是包'); $noPkg++ }
            else { $verdict = '连接/超时失败 ⇒ 网络侧问题（不是"无包"）'; $netFail++ }
            $hn = ''
            try { $hn = ([uri]$url).Host } catch { $hn = '?' }
            [void]$s03.AppendLine(('  {0,-34} {1,-11} {2,-6} {3,-14} {4,-20} {5}' -f $hn, $cand.Scheme, $rr.Http, $rr.Len, $(if ($rr.Head) { $rr.Head } else { '(空)' }), $verdict))
            if ($rr.Http -eq 200) {
                [void]$s03.AppendLine(('      ★ 该组合 HTTP 200 且长度像 ZIP ⇒ 本机【能取到 ICC】：' + $url))
                [void]$s03.AppendLine('      ⚠ 本脚本【不下载、不解压、不安装】；要取包请自行决定是否使用别的工具。')
            }
        }
    }
    [void]$s03.AppendLine('')
    [void]$s03.AppendLine('== 03 小结（按矩阵判定）==')
    [void]$s03.AppendLine(('  ★ 可取到（HTTP 200 且 ZIP 幻数）：{0} / {1} 个组合' -f $zipHit, ($iccServers.Count * $cands.Count)))
    [void]$s03.AppendLine(('  · 404 = 该组合服务器上确实没有包：{0} 个' -f $noPkg))
    [void]$s03.AppendLine(('  · 连接/超时失败 = 网络侧问题：{0} 个' -f $netFail))
    $P.ReqMatrix = ('{0} 个组合：可取到 {1} / 无包(404等) {2} / 网络失败 {3}' -f ($iccServers.Count * $cands.Count), $zipHit, $noPkg, $netFail)
    [void]$s03.AppendLine('  判读："HTTP 200 且长度像 ZIP = 可取到"；"404 = 该组合无包"；只有连接/超时失败才是网络问题。')
    if ($zipHit -eq 0 -and $netFail -eq 0) {
        [void]$s03.AppendLine('  ⇒ 服务器可达、路由存在，但本机这套 PnP/序列号/MAC 组合没有对应档案 ⇒')
        [void]$s03.AppendLine('     若日志里 sRGB 走 StartICCProfile 且失败，更该看 02（本地 ICC 关联/文件）而不是网络。')
    }
}
[void]$s03.AppendLine('  ⚠ 局限：本节请求未带 LM/GCU 的私有请求头；HTTP 200 只能证明"这个 URL 能取到东西"，')
[void]$s03.AppendLine('     不能证明 GCU 内部一定会用同一个 URL/参数。')
Write-Utf8 -Path (Join-Path $OutDir '03-ICC服务器可达性.txt') -Text $s03.ToString()

# ================================================================ 04 LM 日志
Step '04 LM 日志里的校色行（只读；贴原文行 + 三态判定）' | Out-Null

$logTargets = New-Object System.Collections.ArrayList
$mainLog = Join-Path $env:APPDATA 'MechrevoLite\log.txt'
if (Test-Path -LiteralPath $mainLog) { [void]$logTargets.Add($mainLog) }
# 备用位置（本机没有就优雅降级）
try {
    $alt = Join-Path $env:APPDATA 'MechrevoLite'
    if (Test-Path -LiteralPath $alt) {
        foreach ($f in @(Get-ChildItem -LiteralPath $alt -Filter '*.txt' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3)) {
            if ($logTargets -notcontains $f.FullName) { [void]$logTargets.Add($f.FullName) }
        }
        foreach ($f in @(Get-ChildItem -LiteralPath $alt -Filter '*.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3)) {
            if ($logTargets -notcontains $f.FullName) { [void]$logTargets.Add($f.FullName) }
        }
    }
} catch { Warn ('枚举 LM 日志目录失败：' + $_.Exception.Message) }
foreach ($alt2 in @('C:' + [char]92 + 'MyControlCenter.log', (Join-Path $env:ProgramData 'L-Mechrevo\logs'))) {
    try {
        if (Test-Path -LiteralPath $alt2 -PathType Container) {
            foreach ($f in @(Get-ChildItem -LiteralPath $alt2 -Filter '*.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2)) {
                [void]$logTargets.Add($f.FullName)
            }
        } elseif (Test-Path -LiteralPath $alt2) {
            [void]$logTargets.Add($alt2)
        }
    } catch { }
}

$s04 = New-Object System.Text.StringBuilder
[void]$s04.AppendLine('04 LM 日志里的校色/ICC 行（只读；共享读，取尾部；★ 命中即贴原文行）')
[void]$s04.AppendLine(('主目标：' + $mainLog))
[void]$s04.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s04.AppendLine('')
$calPattern = 'SetColorCalibration|ColorCalibration|校色|ICC|StartICCProfile|result=|COLOR_CALIBRATION|UserSetColorCalibration|confirmed=|expectedMode|actualMode|AssociateColorProfile|档位'
# 失败签名（★ 只有这些才算"命中了失败签名"；方法名本身命中只是"出现过该流程"）
$sigFail = @(
    @{ N = '校色档位应用失败 / 已回退到原档位'; P = '校色档位应用失败|已回退到原档位' },
    @{ N = 'confirmed=False 且 result 为失败码(2~7)'; P = 'confirmed=False[^\r\n]*result=[2-7]' },
    @{ N = 'result=4（ColorCalibrationFail）'; P = 'result=4' },
    @{ N = 'result=3（FileIsNotFound）'; P = 'result=3' },
    @{ N = 'result=2 或 5/6/7（网络类失败）'; P = 'result=[2567]' },
    @{ N = 'expectedMode=2 actualMode=1（跳回默认）'; P = 'expectedMode=2\s*actualMode=1' }
)
# 可疑/一般签名（方法被调用 ≠ 失败）
$sigSeen = @(
    @{ N = 'SetColorCalibration 调用'; P = 'SetColorCalibration' },
    @{ N = 'COLOR_CALIBRATION_ON_SRGB'; P = 'COLOR_CALIBRATION_ON_SRGB' },
    @{ N = 'UserSetColorCalibration'; P = 'UserSetColorCalibration' },
    @{ N = 'StartICCProfile'; P = 'StartICCProfile' },
    @{ N = 'AssociateColorProfile'; P = 'AssociateColorProfile' }
)
[void]$s04.AppendLine(('关键词：' + $calPattern))
[void]$s04.AppendLine('')
[void]$s04.AppendLine('  ⚠ 筛选说明：LM 目录下的 lhm_sensors*.txt 只是传感器名清单（不是运行日志），不整篇贴；')
[void]$s04.AppendLine('     某个文件零命中时只记一行摘要，不贴它的最后 40 行（避免噪声盖住现场）。')
[void]$s04.AppendLine('  ⚠ 三态区分：本节会把每个签名判成【没有命中任何已知签名】/【命中但内容正常】/【命中且是失败签名】。')
[void]$s04.AppendLine('')

$P.LmLog = '不存在'
if (-not (Test-Path -LiteralPath $mainLog)) {
    [void]$s04.AppendLine('  ⚠ 主目标不存在（LM 可能没跑过，或日志路径不同）⇒ 本节优雅降级。')
    [void]$s04.AppendLine('    请先启动 L-Mechrevo 并复现一次"切 SRGB"，再跑本脚本。')
} else {
    $P.LmLog = '存在'
}
[void]$s04.AppendLine('')

$totalHits = 0
$skipCount = 0
$readFails = New-Object System.Collections.ArrayList
$tailCache = @{}
$newestTime = $null
$newestFile = ''
foreach ($lt in $logTargets) {
    $tail = @()
    try {
        $isOwnMain = ($lt -ieq $mainLog)
        $leaf = [System.IO.Path]::GetFileName($lt)
        if ($leaf -match '^lhm_sensors') {
            $fi0 = Get-Item -LiteralPath $lt -ErrorAction Stop
            [void]$s04.AppendLine(('---- ' + $lt + ' ----'))
            [void]$s04.AppendLine(("  跳过（传感器名清单，不是运行日志）：{0} 字节   mtime {1}" -f $fi0.Length, $fi0.LastWriteTime))
            [void]$s04.AppendLine('')
            $skipCount++
            continue
        }
        $fi = Get-Item -LiteralPath $lt -ErrorAction Stop
        $tail = @(Read-LinesShared -Path $lt -MaxLines 20000 -Tail $true)
        $tailCache[$lt] = $tail
        $sel = Select-Lines -Lines $tail -Pattern $calPattern -Max 400 -Keep 'tail'
        if ($sel.Total -eq 0 -and -not $isOwnMain) {
            [void]$s04.AppendLine(('---- ' + $lt + ' ----'))
            [void]$s04.AppendLine(("  没有命中任何已知签名：{0} 字节   mtime {1}   尾部行数 {2}（不贴内容）" -f $fi.Length, $fi.LastWriteTime, $tail.Count))
            [void]$s04.AppendLine('     ⇒ 判定：本文件里【没有命中任何已知签名】（不等于"故障不存在"，只说明这份日志没写到）。')
            [void]$s04.AppendLine('')
            continue
        }
        [void]$s04.AppendLine(('---- ' + $lt + ' ----'))
        [void]$s04.AppendLine(("  大小：{0} 字节   mtime：{1}" -f $fi.Length, $fi.LastWriteTime))
        [void]$s04.AppendLine(("  读取尾部行数：{0}（共享读，容忍 LM 正在写）" -f $tail.Count))
        $totalHits += $sel.Total
        [void]$s04.AppendLine(("  ★ 命中校色/ICC 关键行：{0} 行（最多列最后 400 行{1}）" -f $sel.Total, $(if ($sel.Truncated) { '；已截断，只留最新 400' } else { '' })))
        if ($sel.Total -eq 0) {
            [void]$s04.AppendLine('    ⇒ 判定：本文件里【没有命中任何已知签名】。')
            [void]$s04.AppendLine('       ⚠ 这不等于"故障不存在"，只说明这份日志里没有相关行 —— 请确认复现时刻之后 LM 确实在写这份日志。')
        } else {
            [void]$s04.AppendLine('    ---- 命中原文行（保留时间戳；超出 400 行的部分只保留最新若干行）----')
            foreach ($x in $sel.Lines) { [void]$s04.AppendLine(('    ' + $x)) }
        }
        [void]$s04.AppendLine('')
        [void]$s04.AppendLine('  --- 该文件最后 40 行原文（上下文）---')
        $lastN = 40
        $start = [Math]::Max(0, $tail.Count - $lastN)
        for ($i = $start; $i -lt $tail.Count; $i++) { [void]$s04.AppendLine(('    ' + $tail[$i])) }
        # 日志最后一条时间戳（时间窗一致性校验用）
        $lt2 = Get-LastLogTime -Lines $tail
        if ($lt2 -and ((-not $newestTime) -or ($lt2 -gt $newestTime))) { $newestTime = $lt2; $newestFile = $lt }
    } catch {
        [void]$s04.AppendLine(('---- ' + $lt + ' ----'))
        [void]$s04.AppendLine(('  采集失败：' + $_.Exception.Message))
        [void]$s04.AppendLine('  ⚠ 该文件读不到 ⇒ 本节结论【不完整】，不要据此判"没有故障"。')
        [void]$readFails.Add($lt)
        Warn ('LM 日志读取失败 ' + $lt + '：' + $_.Exception.Message)
    }
    [void]$s04.AppendLine('')
}
$P.LmKeyLines = $totalHits

# ---------------------------------------------------------------- 4b 时间窗一致性
[void]$s04.AppendLine('== 4b 日志时间窗与本次运行时间的一致性 ==')
$nowT = Get-Date
if ($newestTime) {
    $age = $nowT - $newestTime
    $P.LogNewest = $newestTime.ToString('yyyy-MM-dd HH:mm:ss')
    [void]$s04.AppendLine(('  日志最后一条的时间：' + $newestTime.ToString('yyyy-MM-dd HH:mm:ss') + '（文件：' + (Split-Path -Leaf $newestFile) + '）'))
    [void]$s04.AppendLine(('  本次运行时间      ：' + $nowT.ToString('yyyy-MM-dd HH:mm:ss')))
    [void]$s04.AppendLine(('  两者相差          ：{0:N1} 小时' -f ($age.TotalHours)))
    if ($age.TotalHours -gt 24) {
        $P.LogTimeliness = ('⚠ 明显过旧（相差 {0:N1} 小时）：本次日志未覆盖你的复现时刻，请复现后立刻再跑' -f $age.TotalHours)
        [void]$s04.AppendLine('  ⚠⚠ 结论：本次日志【明显过旧】（>24h）⇒ 它【没有覆盖你的复现时刻】。')
        [void]$s04.AppendLine('      下面任何"命中/未命中"都只能当历史记录，不能当本次故障的证据。')
        [void]$s04.AppendLine('      ⇒ 请【复现一次"切 SRGB"后立刻再跑本脚本】。')
        Warn ('日志时间窗与本次运行不一致（>24h，差 {0:N1} 小时）：本次日志未覆盖复现时刻，请复现后立刻再跑' -f $age.TotalHours)
    } elseif ($age.TotalHours -gt 2) {
        $P.LogTimeliness = ('⚠ 偏旧（相差 {0:N1} 小时）：可能没覆盖复现时刻，建议复现后立刻重跑' -f $age.TotalHours)
        [void]$s04.AppendLine('  ⚠ 结论：日志偏旧（>2h），可能【没有覆盖本次复现时刻】；建议复现后立刻重跑。')
    } else {
        $P.LogTimeliness = ('正常（相差 {0:N1} 小时，覆盖本次运行附近）' -f $age.TotalHours)
        [void]$s04.AppendLine('  ⇒ 结论：日志时间与本次运行【基本一致】，可认为覆盖了复现时刻附近。')
    }
} else {
    $P.LogNewest = '(未取到)'
    if ($logTargets.Count -eq 0) {
        $P.LogTimeliness = '⚠ 无日志文件可读（LM 可能没跑过，或日志级别为 off）'
        [void]$s04.AppendLine('  ⚠ 没有任何可读日志文件 ⇒ 无法做时间窗一致性校验。')
    } else {
        $P.LogTimeliness = '⚠ 读到了日志但一行时间戳都抽不到（格式异常）'
        [void]$s04.AppendLine('  ⚠ 读到了文件，但尾部没有一行能抽出时间戳 ⇒ 无法判定日志是否覆盖本次复现时刻。')
    }
    [void]$s04.AppendLine('  ⚠ 结论：本次日志【无法证明】覆盖了你的复现时刻，请不要据此判"没有故障"。')
    Warn ('日志时间窗无法校验：' + $P.LogTimeliness)
}
if ($readFails.Count -gt 0) { Warn ('有 {0} 个日志文件读不到，04 节结论不完整' -f $readFails.Count) }
[void]$s04.AppendLine('')

# ---------------------------------------------------------------- 4c 三态判定
[void]$s04.AppendLine('== 4c 命中签名与三态判定（失败签名 / 命中但内容正常 / 没有命中）==')
$failRows = New-Object System.Collections.ArrayList
foreach ($lt in $logTargets) {
    if (-not $tailCache.ContainsKey($lt)) { continue }
    $tail2 = @($tailCache[$lt])
    foreach ($sig in $sigFail) {
        $m = Select-Lines -Lines $tail2 -Pattern $sig.P -Max 5 -Keep 'tail'
        if ($m.Total -gt 0) {
            [void]$failRows.Add(('{0}  ⇒ 命中 {1} 行（文件 {2}）' -f $sig.N, $m.Total, (Split-Path -Leaf $lt)))
            foreach ($ql in @($m.Lines)) { [void]$failRows.Add(('    原文：' + ($ql -replace '\s+', ' '))) }
        }
    }
}
$seenRows = New-Object System.Collections.ArrayList
foreach ($lt in $logTargets) {
    if (-not $tailCache.ContainsKey($lt)) { continue }
    $tail3 = @($tailCache[$lt])
    foreach ($sig in $sigSeen) {
        $m = Select-Lines -Lines $tail3 -Pattern $sig.P -Max 5 -Keep 'tail'
        if ($m.Total -gt 0) {
            [void]$seenRows.Add(('{0}  ⇒ 命中 {1} 行（文件 {2}）' -f $sig.N, $m.Total, (Split-Path -Leaf $lt)))
            foreach ($ql in @($m.Lines)) { [void]$seenRows.Add(('    原文：' + ($ql -replace '\s+', ' '))) }
        }
    }
}
if (($failRows.Count + $seenRows.Count) -eq 0) {
    [void]$s04.AppendLine('  ★ 三态判定：【没有命中任何已知签名】')
    [void]$s04.AppendLine('    ⚠ 这【不等于】"故障不存在"：也可能是 ① 日志没覆盖复现时刻（见 4b）② 日志级别为 off。')
    [void]$s04.AppendLine('      请结合 4b 的时间窗结论判断，必要时开日志后复现重跑。')
} else {
    if ($failRows.Count -gt 0) {
        [void]$s04.AppendLine('  ★★ 三态判定：【命中且是失败签名】（这一类才是故障证据）')
        foreach ($x in ($failRows | Select-Object -Unique)) { [void]$s04.AppendLine(('    ' + $x)) }
    } else {
        [void]$s04.AppendLine('  ★ 三态判定：【命中但内容正常】（只出现流程/方法名，没有失败签名；不是故障证据）')
    }
    if ($seenRows.Count -gt 0) {
        [void]$s04.AppendLine('')
        [void]$s04.AppendLine('  -- 一般签名（方法被调用，本身不代表失败）--')
        foreach ($x in ($seenRows | Select-Object -Unique)) { [void]$s04.AppendLine(('    ' + $x)) }
    }
    if ($totalHits -eq 0 -and $readFails.Count -eq 0) {
        [void]$s04.AppendLine('  ⚠ 一致性提示：关键行总命中为 0，但自动签名有命中 ⇒ 请人工对一眼上面的原文行。')
    }
}
if ($readFails.Count -eq 0 -and $logTargets.Count -eq 0) {
    [void]$s04.AppendLine('  ⚠ 一个日志文件都没找到 ⇒ 本节三态判定【不成立】（既不是"没有命中"也不是"命中"）。')
}
[void]$s04.AppendLine('')

# ---------------------------------------------------------------- 4d 档位数 vs mode 编号
[void]$s04.AppendLine('== 4d ColorCalibrationSupport（档位数）与日志里 mode 编号的关系 ==')
$modesSeen = New-Object System.Collections.ArrayList
foreach ($lt in $logTargets) {
    if (-not $tailCache.ContainsKey($lt)) { continue }
    try {
        $joined = (@($tailCache[$lt]) -join [char]10)
        foreach ($mm in [regex]::Matches($joined, 'SetColorCalibration\(mode\s*(\d+)\)|expectedMode=(\d+)|actualMode=(\d+)|SwitchCustomProfile\((\d+)\)')) {
            for ($gi = 1; $gi -le $mm.Groups.Count - 1; $gi++) {
                $g = $mm.Groups[$gi]
                if ($g.Success -and $g.Value -match '^\d+$') { [void]$modesSeen.Add([int]$g.Value) }
            }
        }
    } catch { }
}
$modeList = @(($modesSeen | Sort-Object -Unique) | ForEach-Object { [string]$_ })
[void]$s04.AppendLine(('  ColorCalibrationSupport（注册表/能力位）= ' + (Get-TextOr $P.CcSupport '未取到')))
[void]$s04.AppendLine(('  日志里实际出现过的 mode / 档位编号 = ' + $(if ($modeList.Count -gt 0) { ($modeList -join ', ') } else { '(未解析到)' })))
[void]$s04.AppendLine('  ⚠ 档位数与日志中出现的 mode 编号可能不一致，请以日志为准。')
[void]$s04.AppendLine('    （"Support=N"是能力位/档位数，不等于"当前生效档位"，也可能与 UI 的编号口径不同。）')
[void]$s04.AppendLine('')

[void]$s04.AppendLine('== 判读提醒 ==')
[void]$s04.AppendLine('  * result=4 ⇒ ColorCalibrationFail：GCU 只在 result==1 时才写注册表 ColorCalibration，')
[void]$s04.AppendLine('    所以 result=4 时"注册表档位没变"是必然结果，不是另一个故障。')
[void]$s04.AppendLine('  * 档位=2 的机型 sRGB 走 UserSetColorCalibration -> StartICCProfile(mode)，')
[void]$s04.AppendLine('    即依赖 ICC 下载/应用 ⇒ 重点看 02（关联/文件在不在）与 03（服务器/请求矩阵）。')
[void]$s04.AppendLine('  * 若 02 里"关联指向的 ICC 在磁盘上找不到（规范化匹配后仍找不到）"，优先怀疑本地 ICC 链而不是网络。')
[void]$s04.AppendLine('  * 若 4b 判定日志过旧，则本节所有命中都只能当历史记录，不能当本次故障证据。')
Write-Utf8 -Path (Join-Path $OutDir '04-LM日志校色行.txt') -Text $s04.ToString()

# ================================================================ 05 事件与其它线索
Step '05 事件日志与其它线索（只读、限量）' | Out-Null

$s05 = New-Object System.Text.StringBuilder
[void]$s05.AppendLine('05 事件日志与其它线索（只读、限量）')
[void]$s05.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s05.AppendLine('')

[void]$s05.AppendLine('== 5a 颜色管理 / WCS 相关事件（近 14 天，最多 30 条）==')
[void]$s05.AppendLine('  ⚠ 读法说明：$e.Message 对少数"消息资源缺失"的事件会抛错（本机实测会整项失败），')
[void]$s05.AppendLine('     所以这里逐条 try/catch 安全取值，读不到消息就只留提供程序/Id。')
try {
    $since = (Get-Date).AddDays(-14)
    $raw = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since } -MaxEvents 800 -ErrorAction SilentlyContinue)
    [void]$s05.AppendLine(('  近 14 天 Application 日志取到 {0} 条，逐条筛"颜色管理"相关：' -f $raw.Count))
    $hits = New-Object System.Collections.ArrayList
    foreach ($e in $raw) {
        if ($hits.Count -ge 30) { break }
        $t = ''
        try { $t = [string]$e.Message } catch { $t = '' }
        $prov = ''
        try { $prov = [string]$e.ProviderName } catch { }
        $isHit = $false
        if ($prov -match 'Color|icm|Wcs|Windows Color') { $isHit = $true }
        elseif ($t -and ($t -match '颜色|色彩|ICC|color profile|ColorProfile')) { $isHit = $true }
        if ($isHit) {
            $flat = ($t -replace '\s+', ' ')
            if ($flat.Length -gt 240) { $flat = $flat.Substring(0, 240) + '...' }
            $ts = ''
            try { $ts = $e.TimeCreated.ToString('MM-dd HH:mm:ss') } catch { }
            [void]$hits.Add(("  [{0}] {1}  Id={2}  {3}" -f $ts, $prov, $e.Id, $flat))
        }
    }
    if ($hits.Count -eq 0) { [void]$s05.AppendLine('  （近 14 天没有匹配的颜色管理事件）') }
    else { foreach ($h in $hits) { [void]$s05.AppendLine($h) } }
} catch {
    [void]$s05.AppendLine(('  （事件日志读取失败：' + ($_.Exception.Message -replace '\s+', ' ') + '）'))
    [void]$s05.AppendLine('  ⚠ 非管理员下部分日志通道读不到属预期；若确实是权限问题，需管理员才能读。')
    Warn ('事件日志读取失败：' + $_.Exception.Message)
}
[void]$s05.AppendLine('')

[void]$s05.AppendLine('== 5b 第三方安全软件 / 代理类线索 ==')
try {
    $av = @(Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop)
    if ($av.Count -eq 0) { [void]$s05.AppendLine('  （SecurityCenter2 未报告任何 AV 产品）') }
    foreach ($a in $av) { [void]$s05.AppendLine(("  AV: " + $a.displayName + "  productState=" + $a.productState + "  path=" + $a.pathToSignedProductExe)) }
} catch {
    [void]$s05.AppendLine(('  （SecurityCenter2 读取失败：' + ($_.Exception.Message -replace '\s+', ' ') + '）'))
}
$susProcPattern = '360|huorong|hips|kxe|tav|avp|mcafee|norton|kaspersky|eset|avast|avg|bitdefender|sophos|trend|clash|v2ray|xray|singbox|sing-box|surge|proxy|wireshark|fiddler|charles|proxifier'
try {
    $hitProcs = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match $susProcPattern })
    if ($hitProcs.Count -eq 0) { [void]$s05.AppendLine('  进程侧：未发现常见安全/代理类进程（匹配表见脚本文本）') }
    else { foreach ($p in $hitProcs) { [void]$s05.AppendLine(("  进程: " + $p.ProcessName + " (Id=" + $p.Id + ")")) } }
} catch { Warn ('进程枚举失败：' + $_.Exception.Message) }
try {
    $svcs = @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop | Where-Object { $_.Name -match $susProcPattern -or $_.DisplayName -match 'Proxy|VPN|Firewall' })
    if ($svcs.Count -eq 0) { [void]$s05.AppendLine('  服务侧：未发现可疑代理/VPN 服务') }
    else { foreach ($sv in $svcs) { [void]$s05.AppendLine(("  服务: " + $sv.Name + " [" + $sv.State + "/" + $sv.StartMode + "]")) } }
} catch { Warn ('服务枚举失败：' + $_.Exception.Message) }
[void]$s05.AppendLine('')

[void]$s05.AppendLine('== 5c LM / GCU 相关进程与网络（只读快照）==')
try {
    $lm = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(L-Mechrevo|GCUBridge|GCUService|OSDTpDetect)$' })
    if ($lm.Count -eq 0) { [void]$s05.AppendLine('  （没有 LM/GCU 系进程在跑）') }
    foreach ($lmProc in $lm) {
        $ws = ''
        try { $ws = ('{0:N0} MB' -f ($lmProc.WorkingSet64 / 1MB)) } catch { }
        $st = ''
        try { $st = [string]$lmProc.StartTime } catch { $st = '(读取失败)' }
        [void]$s05.AppendLine(("  {0} (Id={1})  内存 {2}  启动 {3}" -f $lmProc.ProcessName, $lmProc.Id, $ws, $st))
    }
} catch { Warn ('LM 进程快照失败：' + $_.Exception.Message) }
try {
    $est = @(Get-NetTCPConnection -State Established -ErrorAction Stop | Where-Object { $_.RemoteAddress -notmatch '^(127\.|::1)' } | Select-Object -First 25)
    if ($est.Count -eq 0) { [void]$s05.AppendLine('  （没有对外的 ESTABLISHED 连接）') }
    else {
        [void]$s05.AppendLine('  ⚠ 本地/远端地址只保留可判断性，进程用【名字】而不是 PID（PID 会随运行变化、且属个体信息）：')
        foreach ($c in $est) {
            $pn = '(未知/已退出)'
            try {
                $op = Get-Process -Id ([int]$c.OwningProcess) -ErrorAction Stop
                if ($op) { $pn = [string]$op.ProcessName }
            } catch { }
            [void]$s05.AppendLine(("  {0}:{1} -> {2}:{3}  进程={4}" -f $c.LocalAddress, $c.LocalPort, $c.RemoteAddress, $c.RemotePort, $pn))
        }
    }
} catch { Warn ('TCP 连接快照失败：' + $_.Exception.Message) }
[void]$s05.AppendLine('')
[void]$s05.AppendLine('⚠ 本节只做线索收集，不下结论。5b 命中的安全软件/代理会影响 ICC 下载，值得单独看一眼。')
Write-Utf8 -Path (Join-Path $OutDir '05-事件与其它线索.txt') -Text $s05.ToString()

# ================================================================ 06 手动步骤模板
Step '06 手动步骤模板（给机主填空）' | Out-Null

$s06 = New-Object System.Text.StringBuilder
[void]$s06.AppendLine('06 手动步骤模板（★ 请机主填空后连同 zip 一起回传）')
[void]$s06.AppendLine(('生成时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s06.AppendLine('')
[void]$s06.AppendLine('目的：抓"切到 SRGB 后 UI 跳回默认"的现场。脚本只取证，不做任何修复。')
[void]$s06.AppendLine('')
[void]$s06.AppendLine('【步骤 1】准备')
[void]$s06.AppendLine('  1.1 打开 L-Mechrevo，记下当前校色档位（默认 / SRGB / ...）      → 我记的是：______')
[void]$s06.AppendLine('  1.2 确认 L-Mechrevo 正在运行（日志才会写）                      → 是否运行：______')
[void]$s06.AppendLine('  1.3 不要关日志、不要退出 LM。')
[void]$s06.AppendLine('')
[void]$s06.AppendLine('【步骤 2】切 SRGB —— 切完【立刻】跑本脚本（越接近越好，最好 1 分钟内）')
[void]$s06.AppendLine('  2.1 在 UI 里点【SRGB】（档位 2）')
[void]$s06.AppendLine('  2.2 观察 UI 是否【跳回「默认」】：                                → 跳回了？是/否：______')
[void]$s06.AppendLine('      跳回大约几秒后发生：______ 秒')
[void]$s06.AppendLine('  2.3 【不要关 UI】，立刻双击桌面上的 SRGB诊断.ps1（或跑下面的命令）')
[void]$s06.AppendLine('      powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\Desktop\SRGB诊断.ps1"')
[void]$s06.AppendLine('  2.4 记录脚本输出的目录名：______')
[void]$s06.AppendLine('')
[void]$s06.AppendLine('【步骤 3】补一次对照（可选但很有用）')
[void]$s06.AppendLine('  3.1 先切回【默认】，再切【SRGB】，再跑一次本脚本 → 得到第二份报告')
[void]$s06.AppendLine('      两份对比可以确认"是不是每次都 result=4"。')
[void]$s06.AppendLine('  3.2 如果方便，再试一次"重启 LM 后再切 SRGB"，看是否同签名。')
[void]$s06.AppendLine('')
[void]$s06.AppendLine('【步骤 4】回传')
[void]$s06.AppendLine('  4.1 把桌面上的 LM-SRGB诊断-<时间戳>.zip 发回来。')
[void]$s06.AppendLine('  4.2 在下面写下 UI 的原文提示（如果 UI 有弹窗/提示文字）：')
[void]$s06.AppendLine('      ____________________________________________')
[void]$s06.AppendLine('      ____________________________________________')
[void]$s06.AppendLine('')
[void]$s06.AppendLine('【已经知道、不用重复调研的背景（供参考）】')
[void]$s06.AppendLine('  * 现象：耀世 X6AR55xY / project IDY / 0.291.1，切 SRGB（档位 2）后 UI 跳回「默认」。')
[void]$s06.AppendLine('  * 日志签名：SetColorCalibration(mode 2) -> COLOR_CALIBRATION_ON_SRGB')
[void]$s06.AppendLine('    -> 241ms 后 confirmed=False expectedMode=2 actualMode=1 switch=True result=4')
[void]$s06.AppendLine('    -> 校色档位应用失败: sent=2, 已回退到原档位（两次都同签名）。')
[void]$s06.AppendLine('  * GCU 结果码：0=Ready 1=Finish 2=NetworkFail 3=FileIsNotFound 4=ColorCalibrationFail')
[void]$s06.AppendLine('    5=WebClientRequestFail 6=ConnectServerFail 7=WebClientUnknownFail。')
[void]$s06.AppendLine('  * GCU 只在 result==1 时才写注册表 ColorCalibration 与上报档位。')
[void]$s06.AppendLine('  * 档位=2 的机型 sRGB 走 UserSetColorCalibration -> StartICCProfile(mode)，')
[void]$s06.AppendLine('    依赖 ICC 下载/应用 ⇒ 疑点在【ICC 那条链】。')
Write-Utf8 -Path (Join-Path $OutDir '06-手动步骤模板.txt') -Text $s06.ToString()

# ================================================================ 00-概要 成文
# ⚠ 本节只用 $script:SummaryRef 取数（不用 $P），因为 PowerShell 变量名大小写不敏感，
#   运行中可能有别的变量把 $P 顶掉（真机实测 5c 节的 foreach ($p in $lm) 就会）。
$script:ModelGuard = ''
if (-not $script:SummaryRef.Model) { $script:ModelGuard = '⚠ $P 在写 00 之前被其它变量覆盖（变量名冲突）⇒ 本次 00 取数已用独立引用兜底，下面各项以 $script:SummaryRef 为准。' }

function Get-CollectNote { param([string]$Key)
    $h = @()
    foreach ($w in $script:Warnings) { if ($w -match $Key) { $h += $w } }
    if ($h.Count -gt 0) { return ('采集失败：' + (($h | Select-Object -Unique) -join ' / ')) }
    return '采集失败或本机无此项'
}

$sum = New-Object System.Text.StringBuilder
[void]$sum.AppendLine('00 概要（只读诊断；本脚本不改注册表/不动 ICC/不写系统目录）')
[void]$sum.AppendLine('============================================================')
[void]$sum.AppendLine(('脚本版本   : ' + $SCRIPT_VERSION))
[void]$sum.AppendLine(('运行时间   : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$sum.AppendLine(('是否管理员 : ' + $(if ($isAdmin) { '是' } else { '否（只读诊断不需要提权；个别 HKLM 项读不到属预期，报告里已写明）' })))
$errCount = @($script:Warnings).Count
[void]$sum.AppendLine(('采集状态   : ' + $(if ($errCount -eq 0) { '全部采集项未报错（0 条告警）' } else { ('⚠ 有 {0} 条告警，见本节第 8 项' -f $errCount) })))
if ($script:ModelGuard) { [void]$sum.AppendLine(('自检       : ' + $script:ModelGuard)) }
[void]$sum.AppendLine('')
[void]$sum.AppendLine('1) 机型与系统')
[void]$sum.AppendLine('   机器      : ' + (Get-TextOr $script:SummaryRef.Model (Get-CollectNote 'Win32_ComputerSystem')))
[void]$sum.AppendLine('   主板      : ' + (Get-TextOr $script:SummaryRef.BaseBoard (Get-CollectNote 'Win32_BaseBoard')))
[void]$sum.AppendLine('   BIOS      : ' + (Get-TextOr $script:SummaryRef.Bios (Get-CollectNote 'Win32_BIOS')))
[void]$sum.AppendLine('   项目号    : ' + (Get-TextOr $script:SummaryRef.Project '采集失败或无此项'))
[void]$sum.AppendLine('   世代判断  : ' + (Get-TextOr $script:SummaryRef.Gen '未判定'))
[void]$sum.AppendLine('   系统      : ' + (Get-TextOr $script:SummaryRef.OsName (Get-CollectNote 'Win32_OperatingSystem')) + '  build ' + (Get-TextOr $script:SummaryRef.OsBuild '?'))
[void]$sum.AppendLine('   LM 版本   : ' + (Get-TextOr $script:SummaryRef.LmVersion '采集失败或无此项'))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('2) 一句话结论（ColorCalibration 相关）')
[void]$sum.AppendLine('   ' + (Get-TextOr $script:SummaryRef.Conclusion (Get-CollectNote 'Conclusion')))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('3) 关键读数')
[void]$sum.AppendLine('   DisplayMode                          = ' + (Get-TextOr $script:SummaryRef.DispMode '不存在/读不到'))
[void]$sum.AppendLine('   DisplayFeatures\ColorCalibration    = ' + (Get-TextOr $script:SummaryRef.ColorCalib '不存在/读不到'))
[void]$sum.AppendLine('   ItemSupport\ColorCalibrationSupport = ' + (Get-TextOr $script:SummaryRef.CcSupport '不存在/读不到'))
[void]$sum.AppendLine('   MySetting\AssociateColorProfileResult = ' + (Get-TextOr $script:SummaryRef.AssocResult '不存在/读不到'))
[void]$sum.AppendLine('   当前关联里的 ICC（HKCU ICM\ProfileAssociations\Display） = ' + (Get-TextOr $script:SummaryRef.DefaultProf '未解析到'))
[void]$sum.AppendLine('     ↳ 规范化匹配磁盘结果（大小写不敏感 + 别名表） = ' + (Get-TextOr $script:SummaryRef.AssocMatch '未做匹配'))
[void]$sum.AppendLine(('   系统颜色目录里的 ICC 个数            = ' + $script:SummaryRef.SpoolIccCount))
[void]$sum.AppendLine(('   LM 日志                              = ' + $script:SummaryRef.LmLog + ('（校色/ICC 关键行命中 ' + $script:SummaryRef.LmKeyLines + ' 行）')))
[void]$sum.AppendLine('   日志最后一条的时间                    = ' + (Get-TextOr $script:SummaryRef.LogNewest '未取到'))
[void]$sum.AppendLine('   日志时间窗一致性（对比本次运行）      = ' + (Get-TextOr $script:SummaryRef.LogTimeliness '无法校验'))
[void]$sum.AppendLine(('   ICC 服务器根路径应答                  = ' + $script:SummaryRef.SrvOk + ' / ' + $script:SummaryRef.SrvTotal + '（⚠ 根路径 404 属预期，不作为结论）'))
[void]$sum.AppendLine('   真实请求矩阵（PnP x {序列号,MAC}）    = ' + (Get-TextOr $script:SummaryRef.ReqMatrix '未构造'))
[void]$sum.AppendLine('')
[void]$sum.AppendLine('4) GCUUtil（MySetting\GCUUtil = GCU 要应用的 sRGB）')
[void]$sum.AppendLine('   GCUUtil 原值                 = ' + (Get-TextOr $script:SummaryRef.GcuUtilRaw '不存在/读不到'))
[void]$sum.AppendLine('   ↳ 解析出的 ICC 路径          = ' + (Get-TextOr $script:SummaryRef.GcuUtilPath '未解析出'))
[void]$sum.AppendLine('   ↳ 磁盘存在性 / 头校验        = ' + (Get-TextOr $script:SummaryRef.GcuUtilValid '未校验') + '（备注：' + (Get-TextOr $script:SummaryRef.GcuUtilNote '无') + '）')
[void]$sum.AppendLine('   ⚠ 档位数与日志里出现的 mode 编号可能不一致，请以日志为准（见 04 的 4d）。')
[void]$sum.AppendLine('')
[void]$sum.AppendLine('5) 本节文件清单')
[void]$sum.AppendLine('   01-显示与校色状态.txt    校色相关注册表值（ItemSupport / DisplayFeatures / MySetting / HDR / 显示器）；1f = 三路径 ItemSupport/RGBKeyboard（GamingCenter2/GamingCenter/ControlCenter）')
[void]$sum.AppendLine('   02-ICC关联现状.txt       默认关联、SYSTEM_WIDE、ICC 文件清单、规范化匹配、GCUUtil 解析与头校验、EDID')
[void]$sum.AppendLine('   03-ICC服务器可达性.txt   DNS / TCP / 真实请求矩阵（PnP x {序列号,MAC}；只读，响应体不落盘）')
[void]$sum.AppendLine('   04-LM日志校色行.txt      日志尾部 + 命中原行 + 三态判定 + 时间窗一致性 + mode 编号')
[void]$sum.AppendLine('   05-事件与其它线索.txt    颜色管理事件（限量）、安全软件/代理、LM 进程与连接快照')
[void]$sum.AppendLine('   06-手动步骤模板.txt      ★ 给机主填空的复现步骤')
[void]$sum.AppendLine('   07-只读自证.txt          ★ 跑前/跑后快照内容本身 + 只读结论 + 临时文件清理核对')
[void]$sum.AppendLine('')
[void]$sum.AppendLine('6) 判读提醒')
[void]$sum.AppendLine('   * result=4 = ColorCalibrationFail；GCU 只在 result==1 时才回写注册表 ColorCalibration。')
[void]$sum.AppendLine('   * 档位=2 的机型 sRGB 走 UserSetColorCalibration -> StartICCProfile ⇒ 02/03 是重点。')
[void]$sum.AppendLine('   * 本脚本全程只读；没有做任何修复动作（见 07 的自证快照内容）。')
[void]$sum.AppendLine('')
if ($errCount -gt 0) {
    [void]$sum.AppendLine('8) 本次告警（★ 有告警就说明有采集项失败，不要当成"无异常"读）')
    foreach ($w in $script:Warnings) { [void]$sum.AppendLine('   ' + $w) }
} else {
    [void]$sum.AppendLine('8) 本次告警：无（所有采集项都成功返回）')
    [void]$sum.AppendLine('   ⚠ 若上面 1)~4) 仍有"采集失败/未取到"字样，说明那一项是"本机没有这一项"而不是"脚本报错"。')
}
Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text $sum.ToString()

# ================================================================ 打码
Step '隐私打码 + 自检' | Out-Null
$redactedCount = 0
$redactFail = @()
foreach ($f in (Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name)) {
    try {
        $raw = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
        $red = Redact-Text -Text $raw
        if ($red -ne $raw) { Write-Utf8 -Path $f.FullName -Text $red; $redactedCount++ }
        # 哨兵自检：绝不能把 U+0001 留在报告里
        $leftSentinel = ([regex]::Matches($red, [regex]::Escape([string][char]1))).Count
        if ($leftSentinel -gt 0) { Warn ('打码哨兵未还原：{0}（残留 {1} 处，请人工看一眼）' -f $f.Name, $leftSentinel) }
    } catch {
        $redactFail += ($f.Name + '：' + $_.Exception.Message)
    }
}
foreach ($rf in $redactFail) { Warn ('打码失败（已保留原文）：' + $rf) }

$selfCheck = New-Object System.Collections.ArrayList
$leakVals = New-Object System.Collections.ArrayList
try {
    [void]$leakVals.Add(@{ V = [string]$env:USERNAME; L = '真实用户名（$env:USERNAME）' })
    [void]$leakVals.Add(@{ V = [string]$env:COMPUTERNAME; L = '真实主机名（$env:COMPUTERNAME）' })
    # ⚠ 2026-10-09（方案B）：新增内容（1f 多路径注册表导出）里会出现"主机名\用户名"形态，
    #    自检必须把这种形态也一起查，否则"零命中"名不副实。
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
        $sp = Get-CimInstance -ClassName Win32_ComputerSystemProduct -ErrorAction Stop
        if ($sp -and $sp.IdentifyingNumber -and ([string]$sp.IdentifyingNumber).Length -ge 6) { [void]$leakVals.Add(@{ V = [string]$sp.IdentifyingNumber; L = '序列号' }) }
    } catch { }
    try {
        foreach ($q in @('Win32_BIOS', 'Win32_BaseBoard')) {
            $o = Get-CimInstance -ClassName $q -ErrorAction SilentlyContinue
            if ($o -and $o.SerialNumber -and ([string]$o.SerialNumber).Length -ge 6) { [void]$leakVals.Add(@{ V = [string]$o.SerialNumber; L = '序列号(' + $q + ')' }) }
        }
    } catch { }
    try {
        Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.MacAddress) { [void]$leakVals.Add(@{ V = [string]$_.MacAddress; L = 'MAC 地址' }) }
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
                $v = [string]$lv.V
                if (-not $v -or $v.Length -lt 4) { continue }
                # ⚠ 只有"裸值"才算漏；带前缀的形态（如 Model=xxx）不算
                if ($txt -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape($v) + '(?![0-9A-Za-z])')) {
                    $leakFiles += ($f.Name + ' ← ' + $lv.L)
                }
            }
        } catch { }
    }
    # ⚠ 产物【文件名】本身也要查：文件名里夹序列号/长十六进制是最容易漏的痕迹（正文打了码、名字却漏出去）
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -Force | Sort-Object Name)) {
        $nm = [string]$f.Name
        foreach ($lv in $leakVals) {
            $v = [string]$lv.V
            if (-not $v -or $v.Length -lt 4) { continue }
            if ($nm -match ('(?i)(?<![0-9A-Za-z])' + [regex]::Escape($v) + '(?![0-9A-Za-z])')) {
                $leakFiles += ($nm + '（产物文件名） ← ' + $lv.L)
            }
        }
        if ($nm -match '(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])') {
            $leakFiles += ($nm + '（产物文件名） ← 文件名里夹着 ≥32 位连续十六进制（可能是哈希/序列号）')
        }
    }
} catch { }
if ($leakFiles.Count -gt 0) {
    Warn ('打码自检未通过，以下文件疑似仍有真实标识：' + (($leakFiles | Select-Object -Unique) -join '；'))
    [void]$selfCheck.Add('未通过：' + (($leakFiles | Select-Object -Unique) -join '；'))
} else {
    [void]$selfCheck.Add('通过：真实用户名/主机名/"主机名\用户名"/用户目录名/序列号/MAC（含产物文件名里夹的串）在全部产物里【零命中】')
}
# ⚠ 痕迹只允许占位符：统计占位符，并确认没有残留打码哨兵
try {
    $allTxt = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $OutDir -File -Force | Sort-Object Name)) {
        try { $allTxt += (Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop) } catch { }
    }
    $sentLeft = ([regex]::Matches($allTxt, [regex]::Escape([string][char]1))).Count
    [void]$selfCheck.Add(('占位符痕迹（只允许占位符）：<主机名> {0} 处、<用户名> {1} 处、<序列号> {2} 处、<已打码> {3} 处、<用户目录> {4} 处、<MAC> {5} 处' -f ([regex]::Matches($allTxt, [regex]::Escape('<主机名>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<用户名>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<序列号>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<已打码>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<用户目录>'))).Count, ([regex]::Matches($allTxt, [regex]::Escape('<MAC>'))).Count))
    if ($sentLeft -gt 0) { [void]$selfCheck.Add(('⚠ 未通过：产物里残留打码哨兵 U+0001 共 {0} 处（打码有 bug）' -f $sentLeft)) }
    else { [void]$selfCheck.Add('通过：产物里没有残留打码哨兵（U+0001 零命中）') }
} catch {
    [void]$selfCheck.Add('（占位符痕迹统计失败：' + $_.Exception.Message + '）')
}
[void]$selfCheck.Add(('已打码文件数：' + $redactedCount + ' / ' + @(Get-ChildItem -LiteralPath $OutDir -File).Count))
try {
    $cur = Get-Content -LiteralPath (Join-Path $OutDir '00-概要.txt') -Raw -Encoding UTF8
    $add = "`r`n-- 隐私打码自检 --`r`n   " + ($selfCheck -join "`r`n   ") + "`r`n"
    Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text ($cur + $add)
} catch { Warn ('自检结果写回 00-概要 失败：' + $_.Exception.Message) }
Write-Host ('  -> 打码：' + $redactedCount + ' 个文件被改写；自检：' + $selfCheck[0]) -ForegroundColor Green

# ================================================================ 只读自证（快照对比 + 快照内容留档）
Step '只读自证（跑前/跑后快照对比 + 快照内容留档）' | Out-Null
$stateBefore = $script:SnapshotBefore
$stateAfter = Get-SnapshotText
$readOnlyVerdict = ''
if ($null -eq $stateBefore) {
    $readOnlyVerdict = '⚠ 未通过（跑前快照没取到）：本次无法自证只读，请人工核验。'
    Warn ('只读自证失败：跑前快照缺失')
} elseif ($stateBefore -ceq $stateAfter) {
    $readOnlyVerdict = '通过：注册表（DisplayFeatures / MySetting / ItemSupport / ICM 关联）与各颜色目录的文件清单在本次运行前后【完全一致】⇒ 本脚本未改动它们。'
} else {
    $readOnlyVerdict = '⚠ 未通过：快照出现差异，请人工检查（可能是别的进程同时改了设置，不是本脚本写入的）。'
    Warn ('只读自证未通过：前后快照有差异（下面已贴出两份内容，可逐项比对）')
}
# 临时文件清理核对（本脚本不建临时请求体；这里如实列出 %TEMP% 里可疑残留）
$tempResidue = @()
try {
    $tempResidue = @(Get-ChildItem -LiteralPath $env:TEMP -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(LM-SRGB|iccprobe|icc_|srgbdiag|_snapshot_tmp)' })
} catch { }
$outDirResidue = @()
try {
    $outDirResidue = @(Get-ChildItem -LiteralPath $OutDir -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '_snapshot_tmp|\.tmp$' })
} catch { }

$s07 = New-Object System.Text.StringBuilder
[void]$s07.AppendLine('07 只读自证（★ 结论可复核：下面把快照内容本身也贴出来）')
[void]$s07.AppendLine(('采集时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$s07.AppendLine('')
[void]$s07.AppendLine('⚠ 说明：本脚本是只读诊断，不会有任何写注册表 / 装删 ICC / 改颜色关联 / 写 spool\drivers\color 的动作。')
[void]$s07.AppendLine('   跑前 / 跑后各取一次快照；两次快照的【内容本身】都贴在下面，便于逐项复核（不是只写一句"一致"）。')
[void]$s07.AppendLine('   快照范围 = 注册表 4 个键的全部值 + 5 个颜色目录的文件清单（名字/大小/时间）。')
[void]$s07.AppendLine('')
[void]$s07.AppendLine('== 只读结论 ==')
[void]$s07.AppendLine('   ' + $readOnlyVerdict)
[void]$s07.AppendLine(('   两次快照是否逐字符相同：' + $(if ($stateBefore -ceq $stateAfter) { '是' } else { '否' })))
[void]$s07.AppendLine(('   跑前快照长度：' + $(if ($null -eq $stateBefore) { '(未取到)' } else { ([string]$stateBefore).Length }) + ' 字符；跑后快照长度：' + ([string]$stateAfter).Length + ' 字符'))
[void]$s07.AppendLine('')
[void]$s07.AppendLine('== 临时文件清理核对 ==')
[void]$s07.AppendLine('   本脚本的 HTTP 探测只读响应流的前 4 字节，【不落任何响应体到磁盘】⇒ 全程无临时文件。')
[void]$s07.AppendLine(('   输出目录内 _snapshot_tmp* / *.tmp 残留：' + $(if ($outDirResidue.Count -eq 0) { '无' } else { ($outDirResidue.Name -join ', ') })))
[void]$s07.AppendLine(('   %TEMP% 内可疑残留（按名字匹配 LM-SRGB/iccprobe/icc_/srgbdiag/_snapshot_tmp）：' + $(if ($tempResidue.Count -eq 0) { '无' } else { (($tempResidue | ForEach-Object { $_.Name }) -join ', ') })))
[void]$s07.AppendLine('')
[void]$s07.AppendLine('== 跑前快照内容（脚本开始采集前）==')
[void]$s07.AppendLine($(if ($null -eq $stateBefore) { '（未取到）' } else { $stateBefore }))
[void]$s07.AppendLine('== 跑后快照内容（脚本全部采集与打码完成后）==')
[void]$s07.AppendLine($stateAfter)
Write-Utf8 -Path (Join-Path $OutDir '07-只读自证.txt') -Text $s07.ToString()
Write-Host ('  -> 只读自证：' + $readOnlyVerdict) -ForegroundColor Green
try {
    $cur2 = Get-Content -LiteralPath (Join-Path $OutDir '00-概要.txt') -Raw -Encoding UTF8
    $add2 = "`r`n-- 只读自证（快照内容见 07-只读自证.txt）--`r`n   " + $readOnlyVerdict +
    "`r`n   " + ('临时文件：输出目录残留=' + $(if ($outDirResidue.Count -eq 0) { '无' } else { ($outDirResidue.Name -join ', ') }) +
        '；%TEMP% 可疑残留=' + $(if ($tempResidue.Count -eq 0) { '无' } else { (($tempResidue | ForEach-Object { $_.Name }) -join ', ') })) + "`r`n"
    Write-Utf8 -Path (Join-Path $OutDir '00-概要.txt') -Text ($cur2 + $add2)
} catch { Warn ('只读自证写回 00-概要 失败：' + $_.Exception.Message) }

# ================================================================ 清理临时
Step '清理临时文件' | Out-Null
$cleaned = '无'
try {
    Get-ChildItem -LiteralPath $OutDir -Filter '_snapshot_tmp*' -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $OutDir -Filter '*.tmp' -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    $cleaned = '已在输出目录内清理 _snapshot_tmp*/*.tmp（脚本未在别处落任何临时文件）'
} catch { Warn ('临时清理失败：' + $_.Exception.Message) }
Write-Host ('  -> ' + $cleaned)

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
        $n = $za.Entries.Count
        $bad = @()
        foreach ($e in $za.Entries) { if ($e.FullName -match '_tmp_|\.tmp$') { $bad += $e.FullName } }
        $za.Dispose()
        Write-Host ('  -> zip 条目数：' + $n + $(if ($bad.Count -gt 0) { '（⚠ 混入临时文件：' + ($bad -join ',') + '）' } else { '（不含临时文件）' })) -ForegroundColor Green
    } catch { Warn ('zip 自检失败：' + $_.Exception.Message) }
    Write-Host ('  -> zip：' + $zipPath + '（' + (Get-Item -LiteralPath $zipPath).Length + ' 字节）') -ForegroundColor Green
} else {
    Write-Host ('  -> ' + $zipNote) -ForegroundColor Yellow
}

# ================================================================ 收尾
Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' SRGB 只读诊断结束' -ForegroundColor White
Write-Host (' 输出目录 : ' + $OutDir)
Write-Host (' zip      : ' + $(if ($zipOk) { $zipPath } else { '未产出（' + $zipNote + '）' }))
Write-Host (' 打码文件 : ' + $redactedCount + ' 个')
Write-Host (' 只读自证 : ' + $(if ($readOnlyVerdict -like '通过*') { '通过（未改注册表/ICC 关联/颜色目录）' } else { '未通过（见报告）' }))
Write-Host '============================================================' -ForegroundColor White
Write-Host '★ 请把生成的 zip 发回来。' -ForegroundColor Green
Write-Host '★ 别忘了在 06-手动步骤模板.txt 里填写 UI 是否跳回、几秒后跳的。' -ForegroundColor Green
Write-Host ''
foreach ($w in $script:Warnings) { Write-Host ('  告警：' + $w) -ForegroundColor Yellow }
