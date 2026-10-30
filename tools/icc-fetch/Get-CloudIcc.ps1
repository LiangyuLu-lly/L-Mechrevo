param(
    [string]$OutDir = $PSScriptRoot,
    # 抓取方式：1=<pnp>_<序列号>  2=<pnp>_<MAC>  3=两个都试(1+2)  auto=1失败再试2，仍失败再兜底
    [ValidateSet('auto', '1', '2', '3')]
    [string]$Method = 'auto',
    [switch]$NoElevate,
    [switch]$KeepOpen
)

# ============================================================
#  自动提权：读取 SMBIOS / 显示器 EDID 与后续安装 ICC 都需要管理员
#  - 已是管理员：直接继续
#  - 不是管理员：以管理员身份重新启动本脚本（会弹 UAC）
#  - 加 -NoElevate 可跳过提权（普通权限运行，部分信息可能读不到）
# ============================================================
$__isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $__isAdmin -and -not $NoElevate) {
    Write-Host '[提权] 正在请求管理员权限（UAC 请点【是】）…' -ForegroundColor Yellow
    $__argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
    if ($PSBoundParameters.ContainsKey('OutDir')) { $__argv += @('-OutDir', ('"' + $OutDir + '"')) }
    if ($PSBoundParameters.ContainsKey('Method') -and $Method -ne 'auto') { $__argv += @('-Method', $Method) }
    if ($KeepOpen) { $__argv += '-KeepOpen' }
    try {
        Start-Process -FilePath 'powershell.exe' -ArgumentList $__argv -Verb RunAs -ErrorAction Stop
        exit 0
    } catch {
        Write-Host '[提权] 已取消或失败：' -NoNewline -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        Write-Host '       改为普通权限继续运行（部分硬件信息可能为空）。' -ForegroundColor DarkGray
    }
}

$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$servers = @(
    'http://iccprofile.uniwill.com.tw/api/iccprofile/',
    'http://iccprofile.aistoneglobal.com:8001/api/iccprofile/'
)

function Get-SerialNumber {
    try { return (Get-CimInstance Win32_BIOS -ErrorAction Stop).SerialNumber.Trim() } catch { return '' }
}
function Get-MonitorIds {
    $ids = @()
    try {
        Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction Stop | ForEach-Object {
            $friendly = ($_.UserFriendlyName | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            $mfg      = ($_.ManufacturerName | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            $code     = ($_.ProductCodeID  | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            $monSn    = ($_.SerialNumberID | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            # PnP 名称 = 厂商代码 + 产品代码（如 BOE + 0CBE => BOE0CBE）；
            # 面板序列号常见返回 '0'，视为无效。
            $pnp = ($mfg + $code).Trim()
            if (-not $pnp -and $_.InstanceName -match 'DISPLAY\\([^\\]+)') { $pnp = $matches[1] }
            if ($monSn -eq '0') { $monSn = '' }
            $ids += [pscustomobject]@{ Pnp = $pnp; Friendly = $friendly; Serial = $monSn; Raw = ($_.InstanceName) }
        }
    } catch { }
    if ($ids.Count -eq 0) {
        try {
            Get-CimInstance Win32_DesktopMonitor | ForEach-Object {
                $pnp = ''
                if ($_.PNPDeviceID -match 'MONITOR\\([^\\]+)') { $pnp = $matches[1] }
                elseif ($_.PNPDeviceID -match 'DISPLAY\\([^\\]+)') { $pnp = $matches[1] }
                $ids += [pscustomobject]@{ Pnp = $pnp; Friendly = $_.Name; Serial = ''; Raw = $_.PNPDeviceID }
            }
        } catch { }
    }
    return $ids
}
function Get-MacAddresses {
    $list = @()
    try {
        Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' -or $_.MacAddress } | ForEach-Object {
            $mac = ($_.MacAddress -replace '[-:]','').ToUpperInvariant()
            if ($mac) { $list += $mac }
        }
    } catch { }
    return $list | Select-Object -Unique
}

$serial = Get-SerialNumber
$monitors = Get-MonitorIds
$macs = Get-MacAddresses
Write-Host "Serial: $serial"
Write-Host "Monitors: $($monitors.Count)"
$monitors | ForEach-Object { Write-Host ("  PnP=" + $_.Pnp + " / panelSN=" + $_.Serial + " / friendly=" + $_.Friendly + " / " + $_.Raw) }
Write-Host "MACs: $($macs.Count)"
$macs | ForEach-Object { Write-Host ("  " + $_) }

# ============================================================
#  候选名生成：三套规则，按顺序尝试（前一套失败才走下一套）
#   方法1  <pnp>_<序列号>    —— 50 系（aistoneglobal）约定 <pnp>_<sn>.zip
#   方法2  <pnp>_<MAC>       —— 40 系（uniwill）实测约定
#   兜底   <pnp> / <友好名>  —— 仅自动模式最后尝试
#  每个候选名都试三种后缀：''、'.zip'、'.icm'
# ============================================================
function Get-MethodCandidates {
    param([string]$Kind, $MonitorList, [string]$BiosSerial, $MacList)
    $out = @()
    foreach ($m in $MonitorList) {
        $id = $m.Pnp
        if (-not $id) { continue }
        switch ($Kind) {
            'serial' {
                if ($m.Serial)   { $out += ($id + '_' + $m.Serial) }
                if ($BiosSerial) { $out += ($id + '_' + $BiosSerial) }
            }
            'mac' {
                foreach ($mac in $MacList) { $out += ($id + '_' + $mac) }
            }
            'fallback' {
                $out += $id
                if ($m.Friendly -and $m.Friendly -ne $id) { $out += $m.Friendly }
            }
        }
    }
    return @($out | Select-Object -Unique)
}

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

function Invoke-FetchCandidates {
    param([string[]]$Candidates, [string[]]$TargetServers, [string]$Label)

    if (-not $Candidates -or $Candidates.Count -eq 0) {
        Write-Host ("[{0}] 没有候选名，跳过" -f $Label) -ForegroundColor DarkGray
        return 0
    }

    Write-Host ("`n===== {0}：{1} 个候选名 =====" -f $Label, $Candidates.Count) -ForegroundColor Cyan
    $Candidates | ForEach-Object { Write-Host ("   " + $_) -ForegroundColor DarkGray }

    $hits = 0
    foreach ($server in $TargetServers) {
        Write-Host ("--- {0}" -f $server) -ForegroundColor DarkCyan
        $serverHit = $false
        foreach ($name in $Candidates) {
            if ($serverHit) { break }
            foreach ($suffix in @('', '.zip', '.icm')) {
                $url = $server + $name + $suffix
                try {
                    $resp = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 6 -ErrorAction Stop
                    $bytes = $resp.RawContentStream.ToArray()
                    $sig = if ($bytes.Length -ge 40) { [Text.Encoding]::ASCII.GetString($bytes, 36, 4) } else { '' }
                    $isIcc = $sig -eq 'acsp'
                    $isZip = ($bytes.Length -ge 4 -and $bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B)
                    # 落盘命名：统一用「候选名」本体，去掉尝试用的 .zip/.icm 后缀，
                    # 避免出现 BOE0D55_xxx.zip.icm 这种双后缀。
                    $safeBase = ($name -replace '[^A-Za-z0-9._-]', '_')
                    $safeBase = $safeBase -replace '\.(zip|icm|icc)$', ''

                    if ($isZip) {
                        # 云端给的是 <pnp>_<sn>.zip，原样保存，不解包：
                        # 广色域机型一个包里常含多个 .icm（不同色域/不同面板批次），
                        # 解包只会丢掉其余的。这里只列出包内清单供后续选择。
                        $dest = Join-Path $OutDir ($safeBase + '.zip')
                        [IO.File]::WriteAllBytes($dest, $bytes)
                        $listText = ''
                        try {
                            $zip = [IO.Compression.ZipFile]::OpenRead($dest)
                            try {
                                $icms = @($zip.Entries | Where-Object { $_.FullName -match '\.(icm|icc)$' })
                                if ($icms.Count -gt 0) {
                                    $listText = '，内含 ' + $icms.Count + ' 个校色文件: ' + (($icms | ForEach-Object { $_.Name }) -join ', ')
                                } else {
                                    $listText = '，包内没有 .icm/.icc（仅保存原始文件）'
                                }
                            } finally { $zip.Dispose() }
                        } catch { $listText = '（无法列出包内清单）' }
                        Write-Host ("HIT " + $url + " -> ZIP " + $bytes.Length + " bytes" + $listText + " saved=" + $dest) -ForegroundColor Green
                        $hits++
                        $serverHit = $true
                        break
                    }
                    elseif ($isIcc) {
                        $dest = Join-Path $OutDir ($safeBase + '.icm')
                        [IO.File]::WriteAllBytes($dest, $bytes)
                        Write-Host ("HIT " + $url + " -> " + $bytes.Length + " bytes sig=acsp saved=" + $dest) -ForegroundColor Green
                        $hits++
                        $serverHit = $true
                        break
                    }
                    else {
                        $dest = Join-Path $OutDir ($safeBase + '.bin')
                        [IO.File]::WriteAllBytes($dest, $bytes)
                        Write-Host ("HIT(未知格式) " + $url + " -> " + $bytes.Length + " bytes saved=" + $dest) -ForegroundColor Yellow
                    }
                } catch {
                    $code = ''
                    try { $code = [int]$_.Exception.Response.StatusCode } catch { }
                    Write-Host ("MISS " + $url + " code=" + $code) -ForegroundColor DarkGray
                }
            }
        }
    }
    return $hits
}

# 服务器优先级：方法1 先试 50 系的 aistone，再试 uniwill；方法2 反过来
$serverAistone = @($servers | Where-Object { $_ -match 'aistoneglobal' })
$serverUniwill = @($servers | Where-Object { $_ -match 'uniwill' })
$orderMethod1 = @($serverAistone + $serverUniwill | Where-Object { $_ })
$orderMethod2 = @($serverUniwill + $serverAistone | Where-Object { $_ })

$found = 0

# ---- 交互式选择（双击运行时弹菜单；命令行给 -Method 则直接用）----
if ($Method -eq 'auto' -and $KeepOpen) {
    Write-Host ''
    Write-Host '请选择抓取方式（直接回车 = 自动）：' -ForegroundColor Cyan
    Write-Host '  [1] 方法1  <pnp>_<序列号>      50 系 aistone，文件名常见 .zip'
    Write-Host '  [2] 方法2  <pnp>_<MAC>         40 系 uniwill'
    Write-Host '  [3] 两个都试（方法1 + 方法2）'
    Write-Host '  [A] 自动：先 1，失败再 2，最后兜底'
    $ans = Read-Host '请输入 1 / 2 / 3 / A'
    switch ($ans.Trim().ToUpperInvariant()) {
        '1' { $Method = '1' }
        '2' { $Method = '2' }
        '3' { $Method = '3' }
        default { $Method = 'auto' }
    }
    $label = switch ($Method) {
        '1' { '方法1' }
        '2' { '方法2' }
        '3' { '两个都试（1 + 2）' }
        default { '自动（1 → 2 → 兜底）' }
    }
    Write-Host ("已选择：{0}" -f $label) -ForegroundColor Green
}

# ---- 方法1：<pnp>_<序列号> ----
if ($Method -eq '1' -or $Method -eq '3' -or $Method -eq 'auto') {
    $c1 = Get-MethodCandidates -Kind 'serial' -MonitorList $monitors -BiosSerial $serial -MacList $macs
    $found = Invoke-FetchCandidates -Candidates $c1 -TargetServers $orderMethod1 -Label '方法1 <pnp>_<序列号>'
}

# ---- 方法2：显式选 2/3 时都跑；自动模式下方法1失败才跑 ----
if ($Method -eq '2' -or $Method -eq '3' -or ($Method -eq 'auto' -and $found -eq 0)) {
    $c2 = Get-MethodCandidates -Kind 'mac' -MonitorList $monitors -BiosSerial $serial -MacList $macs
    $found += Invoke-FetchCandidates -Candidates $c2 -TargetServers $orderMethod2 -Label '方法2 <pnp>_<MAC>'
}
elseif ($Method -eq 'auto' -and $found -gt 0) {
    Write-Host "`n方法1 已命中，跳过方法2/兜底。" -ForegroundColor Green
}

# ---- 兜底：<pnp> / <友好名>（仅自动模式且前两套都失败时）----
if ($Method -eq 'auto' -and $found -eq 0) {
    $c3 = Get-MethodCandidates -Kind 'fallback' -MonitorList $monitors -BiosSerial $serial -MacList $macs
    $found += Invoke-FetchCandidates -Candidates $c3 -TargetServers $servers -Label '兜底 <pnp> / <友好名>'
}

if ($found -eq 0) {
    Write-Host "`n三套命名规则都没命中：该机型可能未上传云端校色文件。" -ForegroundColor Yellow
} else {
    Write-Host ("`n成功命中 {0} 个 ICC，输出目录：{1}" -f $found, $OutDir) -ForegroundColor Green
}
Write-Host "Done: $OutDir"

if ($KeepOpen) {
    Write-Host ''
    Write-Host '按回车键关闭窗口…' -ForegroundColor DarkGray
    [void](Read-Host)
}
