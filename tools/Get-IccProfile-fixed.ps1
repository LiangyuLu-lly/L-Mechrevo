<#
.SYNOPSIS
    拉取本机显示器 ICC 校色包（复刻 GCUService 的取法）。
    相比 Downloads 原版的改动：多地址 × 双取法（PnP+SN / PnP+MAC），带超时与遮蔽诊断。
.NOTES
    - 只下载、只解压到本地目录；不安装、不写任何硬件/系统设置。
    - 诊断输出遮蔽序列号与 MAC（只显示前后 2 位）。PnP ID（面板型号）不遮蔽。
    - 本文件必须保存为 UTF-8 **带 BOM**：Windows PowerShell 5.1 对无 BOM 的 .ps1
      按 ANSI(GBK) 读取，中文串会被破坏并直接解析失败（2026-10-06 实测）。
      某些编辑器/工具改写此文件会丢 BOM，改完必须复查前 3 字节 = EF BB BF。
    - 命名规则（2026-10-06 由安装包内 ICCProfile\ 目录实证）：
      <PnP>_<后缀>.zip，后缀为 12 位有线网卡 MAC（如 B025AA7FCFA5）或长序列号（如 425032C32531090339），
      一律大写、无分隔符。服务器路由 /api/iccprofile/<name>.zip 真实存在（404 为自定义 9 字节 "Not found"）。
    - 服务器端口固定：aistone 两台的 80 端口不通，只有 8001 / 3081 通。
.PARAMETER Out
    输出目录，默认 .\ICCProfile
.PARAMETER Server
    覆盖服务器列表（可传多个；默认三个已知地址，必须以 / 结尾）
.PARAMETER MonitorId
    手动指定 PnP ID（默认自动探测，取第一个带 EDID 的显示器）
.PARAMETER Serial
    手动指定序列号（默认 SMBIOS type 1，读取顺序：UEFI 注册表 → 旧系统注册表 → CIM）
.PARAMETER Mac
    手动指定有线网卡 MAC（默认取已连接的 802.3 物理网卡，去分隔符、大写）
.PARAMETER TimeoutSec
    单次请求超时秒数（默认 10）
.PARAMETER Dry
    只打印将要请求的 URL 矩阵，不联网
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Get-IccProfile-fixed.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Get-IccProfile-fixed.ps1 -Dry
.NOTES
    应用方式（自行确认后再执行，本脚本不会替你安装）：
      系统设置 → 显示 → 高级显示器设置 → 显示器配置文件 → 添加解压出的 .icc/.icm
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Out = (Join-Path (Get-Location).Path 'ICCProfile'),
    [string[]]$Server,
    [string]$MonitorId,
    [string]$Serial,
    [string]$Mac,
    [int]$TimeoutSec = 10,
    [switch]$Dry
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

# 三个已知 ICC 服务器。注意：aistoneglobal（带 e）才是能解析的真名，
# 原脚本里的 aistonglobal（无 e）是拼写错误，DNS 直接失败（2026-10-06 实测复核）。
# 端口不能改：8001 / 3081 是 aistone 两台的实际监听端口，80 端口不通（超时）。
$DefaultServers = @(
    'http://iccprofile.uniwill.com.tw/api/iccprofile/',
    'http://iccprofile.aistoneglobal.com:8001/api/iccprofile/',
    'http://iccprofile1.aistoneglobal.com:3081/api/iccprofile/'
)
if ($PSBoundParameters.ContainsKey('Server') -and $Server) {
    # -Server 传单个字符串时会被绑成 String[]，这里统一成标量字符串数组，
    # 否则后面的 $server + $name 会退化成 Object[]（2026-10-06 实测踩过）。
    $Servers = @($Server | ForEach-Object { [string]$_ })
} else {
    $Servers = @($DefaultServers)
}
foreach ($s in $Servers) {
    if ($s -notmatch '/$') { throw "server url must end with '/': $s" }
}

# 部分服务器对 UA 敏感，显式给定 UA，行为贴近厂商客户端。
$UserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36'
$BaseParams = @{
    Method             = 'GET'
    TimeoutSec         = $TimeoutSec
    UseBasicParsing    = $true
    MaximumRedirection = 2
    UserAgent          = $UserAgent
    Headers            = @{ 'Accept' = 'application/zip,*/*' }
}

function Mask-Value {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '(none)' }
    $v = $Value.Trim()
    if ($v.Length -le 4) { return ('x' + $v.Substring($v.Length - 1)) }
    return $v.Substring(0, 2) + '***' + $v.Substring($v.Length - 2)
}

function Get-MonitorIdWithEdid {
    $base  = 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY'
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($pnp in (Get-ChildItem $base -ErrorAction SilentlyContinue)) {
        foreach ($inst in (Get-ChildItem $pnp.PSPath -ErrorAction SilentlyContinue)) {
            $dp   = Join-Path $inst.PSPath 'Device Parameters'
            $edid = (Get-ItemProperty -Path $dp -Name EDID -ErrorAction SilentlyContinue).EDID
            if ($edid -and $edid.Length -ge 128 -and -not $found.Contains($pnp.PSChildName)) {
                $found.Add($pnp.PSChildName)
            }
        }
    }
    if ($found.Count -eq 0) { return $null }
    return $found[0]
}

function Get-WiredMac {
    $nics = Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
            Where-Object { $_.MediaType -like '*802.3*' -and $_.Status -eq 'Up' }
    $nic = $nics | Select-Object -First 1
    if (-not $nic) {
        $nics = Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.MediaType -like '*802.3*' }
        $nic  = $nics | Select-Object -First 1
    }
    if (-not $nic) { return $null }
    return ($nic.MacAddress -replace '[^0-9A-Fa-f]', '').ToUpper()
}

$smbiosSrc = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SmbiosInfo
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetSystemFirmwareTable(uint provider, uint tableId, byte[] buffer, uint size);
    static string GetString(byte[] t, int start, int len, byte index)
    {
        if (index == 0) return "";
        int p = start + len;
        for (byte i = 1; p < t.Length; i++)
        {
            int e = p;
            while (e < t.Length && t[e] != 0) e++;
            if (i == index) return Encoding.ASCII.GetString(t, p, e - p).Trim();
            p = e + 1;
            if (p < t.Length && t[p] == 0) return "";
        }
        return "";
    }
    public static string SystemSerial()
    {
        uint size = GetSystemFirmwareTable(0x52534D42, 0, null, 0);
        if (size == 0) return null;
        byte[] buf = new byte[size];
        if (GetSystemFirmwareTable(0x52534D42, 0, buf, size) != size) return null;
        int p = 8;
        while (p + 4 <= buf.Length)
        {
            byte type = buf[p];
            int len = buf[p + 1];
            if (len < 4 || p + len > buf.Length) break;
            if (type == 1 && len > 7)
            {
                string s = GetString(buf, p, len, buf[p + 7]);
                if (s.Length > 0) return s;
            }
            int q = p + len;
            while (q + 1 < buf.Length && !(buf[q] == 0 && buf[q + 1] == 0)) q++;
            p = q + 2;
            if (type == 127) break;
        }
        return null;
    }
}
'@

if (-not ('SmbiosInfo' -as [type])) { Add-Type -TypeDefinition $smbiosSrc -Language CSharp }

# 序列号取值链（只读）：
#  1) UEFI 注册表映射的 SMBIOS type 1（受限环境下也可读，比 CIM 稳；本仓库受限 shell 实测可用）
#  2) GetSystemFirmwareTable 直接解析 SMBIOS
#  3) 旧系统注册表映射（部分机型只有 BaseBoardSerialNumber）
#  4) CIM Win32_BIOS / Win32_ComputerSystemProduct（无权限时不再抛错，只记一条 Verbose）
function Get-SystemSerial {
    $v = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\SystemInformation' -ErrorAction SilentlyContinue).SystemSerialNumber
    if ($v -and $v.Trim()) { return $v.Trim() }
    try { $v = [SmbiosInfo]::SystemSerial(); if ($v -and $v.Trim()) { return $v.Trim() } } catch { }
    $v = (Get-ItemProperty -Path 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS' -ErrorAction SilentlyContinue).BaseBoardSerialNumber
    if ($v -and $v.Trim()) { return $v.Trim() }
    foreach ($cls in @('Win32_BIOS', 'Win32_ComputerSystemProduct')) {
        try {
            $v = (Get-CimInstance -ClassName $cls -ErrorAction Stop).SerialNumber
            if ($v -and $v.Trim()) { return $v.Trim() }
        } catch {
            Write-Verbose ("{0} serial read failed: {1}" -f $cls, $_.Exception.Message)
        }
    }
    return $null
}

# 单次请求：返回 Http / Length / Kind / Head / Bytes / Ms / Note
function Invoke-IccProbe {
    param([string]$Url)
    $sw    = [System.Diagnostics.Stopwatch]::StartNew()
    $code  = -1
    $note  = ''
    $bytes = $null
    try {
        # 注意：splat 必须写在左边，否则 -Uri 与 @BaseParams 会串成 Object[]
        $resp  = Invoke-WebRequest @BaseParams -Uri $Url -ErrorAction Stop
        $code  = [int]$resp.StatusCode
        $ct    = [string]$resp.Headers['Content-Type']
        if ($ct) { $note = 'ct=' + $ct }
        $bytes = $resp.RawContentStream.ToArray()
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        $note = ($_.Exception.Message -replace '\s+', ' ')
    } catch {
        $note = ($_.Exception.Message -replace '\s+', ' ')
    }
    $sw.Stop()

    $head = ''
    $kind = 'n/a'
    $len  = 0
    if ($bytes -and $bytes.Length -gt 0) {
        $len  = $bytes.Length
        $head = (($bytes[0..([Math]::Min(7, $len - 1))] | ForEach-Object { $_.ToString('x2') }) -join ' ')
        if ($len -ge 2 -and $bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B) { $kind = 'ZIP' }
        elseif ($bytes[0] -eq 0x3C)                                     { $kind = 'HTML/XML' }
        else                                                            { $kind = 'binary/text' }
    } elseif ($code -ge 200 -and $code -lt 300) {
        $kind = 'EMPTY'
    }

    [PSCustomObject]@{
        Http = $code; Length = $len; Kind = $kind; Head = $head
        Ms = $sw.ElapsedMilliseconds; Note = $note; Bytes = $bytes
    }
}

# 校验：ZIP 幻数 + 真能被 .NET 当 zip 打开（防 200 + HTML 错误页被当成功）
function Test-ZipPayload {
    param([byte[]]$Bytes)
    if (-not $Bytes -or $Bytes.Length -lt 4) { return $false }
    if (-not ($Bytes[0] -eq 0x50 -and $Bytes[1] -eq 0x4B)) { return $false }
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        [System.IO.File]::WriteAllBytes($tmp, $Bytes)
        $z = [System.IO.Compression.ZipFile]::OpenRead($tmp)
        try { return ($z.Entries.Count -gt 0) } finally { $z.Dispose() }
    } catch {
        Write-Verbose ('zip open failed: ' + $_.Exception.Message)
        return $false
    } finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

if (-not $MonitorId) { $MonitorId = Get-MonitorIdWithEdid }
if (-not $MonitorId) { throw 'no monitor with EDID found (use -MonitorId)' }

if (-not $Serial) { $Serial = Get-SystemSerial }
if (-not $Serial) { throw 'no SMBIOS serial number (use -Serial)' }
$Serial = [string]$Serial.Trim()

if (-not $Mac) { $Mac = Get-WiredMac }

# 双取法：PnP+SN 与 PnP+MAC
$Candidates = New-Object System.Collections.Generic.List[object]
$Candidates.Add([PSCustomObject]@{ Scheme = 'pnp+sn'; Name = [string]($MonitorId + '_' + $Serial) })
if ($Mac) {
    $Candidates.Add([PSCustomObject]@{ Scheme = 'pnp+mac'; Name = [string]($MonitorId + '_' + $Mac) })
} else {
    Write-Warning '未取到有线网卡 MAC（Get-NetAdapter 无权限或无机型网卡），pnp+mac 取法已跳过；可用 -Mac 手动指定'
}

Write-Host ''
Write-Host '=== ICC fetch (diagnostic) ===' -ForegroundColor Cyan
Write-Host ("PnP ID : {0}" -f $MonitorId)
Write-Host ("Serial : {0}  (masked)" -f (Mask-Value $Serial))
Write-Host ("MAC    : {0}  (masked)" -f (Mask-Value $Mac))
Write-Host ("Cells  : {0} servers x {1} schemes, timeout {2}s" -f $Servers.Count, $Candidates.Count, $TimeoutSec)
Write-Host ''

if ($Dry) {
    foreach ($s in $Servers) {
        foreach ($c in $Candidates) { Write-Host ('[dry] ' + [string]$s + [string]$c.Name + '.zip') }
    }
    Write-Host '(dry run: nothing downloaded)' -ForegroundColor Yellow
    return
}

$results  = New-Object System.Collections.Generic.List[object]
$hit      = $null
$hitBytes = $null

foreach ($server in $Servers) {
    foreach ($cand in $Candidates) {
        $url = [string]$server + [string]$cand.Name + '.zip'
        $r   = Invoke-IccProbe -Url $url

        $ok = ($r.Kind -eq 'ZIP') -and (Test-ZipPayload -Bytes $r.Bytes)
        $line = "[{0,-7}] {1,-38} HTTP {2,-4} len={3,-8} kind={4,-12} {5}ms {6}" -f `
                $cand.Scheme, ([uri]$url).Host, $r.Http, $r.Length, $r.Kind, $r.Ms, $r.Note
        if ($ok) { Write-Host $line -ForegroundColor Green }
        elseif ($r.Http -ge 200 -and $r.Http -lt 300) { Write-Host $line -ForegroundColor Yellow }
        else { Write-Host $line }

        $item = [PSCustomObject]@{
            Scheme = $cand.Scheme; Host = ([uri]$url).Host; Url = $url
            Http = $r.Http; Length = $r.Length; Kind = $r.Kind; Head = $r.Head
            Ms = $r.Ms; Note = $r.Note; Success = $ok
        }
        $results.Add($item)

        if ($ok -and -not $hit) { $hit = $item; $hitBytes = $r.Bytes; break }
    }
    if ($hit) { break }
}

Write-Host ''
if (-not $hit) {
    Write-Host '=== 结果：所有组合均未命中 ===' -ForegroundColor Red
    Write-Host '判定要点：'
    Write-Host '  - kind=HTML/XML 或 HTTP 404 -> 服务端没有本机这台显示器/机型的档案（路由本身存在）。'
    Write-Host '  - 连接超时 / 名称解析失败 -> 网络问题（aistone 两台的 80 端口不通，必须用 8001 / 3081）。'
    Write-Host '可尝试：-MonitorId 换显示器型号、-Serial 手工指定、-Mac 手工指定有线网卡 MAC（取法二）。'
    $results | Format-Table Scheme, Host, Http, Length, Kind, Head, Ms -AutoSize | Out-String -Width 220 | Write-Host
    exit 2
}

Write-Host '=== 命中 ===' -ForegroundColor Green
Write-Host ("取法   : {0}" -f $hit.Scheme)
Write-Host ("服务器 : http://{0}" -f $hit.Host)
Write-Host ("URL    : {0}" -f $hit.Url)
Write-Host ("大小   : {0} bytes" -f $hit.Length)

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$zipPath = Join-Path $Out ($hit.Scheme + '_' + $MonitorId + '.zip')

# 命中时已在内存里校验过 zip；直接落盘，避免二次网络请求又失败。
[System.IO.File]::WriteAllBytes($zipPath, $hitBytes)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = New-Object System.Collections.Generic.List[string]
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $zip.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        $dst = Join-Path $Out $entry.Name
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dst, $true)
        $files.Add($dst)
    }
} finally { $zip.Dispose() }

Write-Host ''
Write-Host '=== 已保存（未安装） ===' -ForegroundColor Cyan
[PSCustomObject]@{ Zip = $zipPath; Size = (Get-Item $zipPath).Length; Files = ($files -join '; ') } | Format-List

Write-Host '应用方式（自行确认后再执行，本脚本不会替你安装）：'
Write-Host '  1) 系统设置 - 系统 - 显示 - 高级显示器设置 - 显示器配置文件 - 添加刚解压的 .icc/.icm'
Write-Host '  2) 或双击 .icc 文件 - 关联到设备（Windows 颜色管理）'
Write-Host '  3) 本仓库 LM 的自定义 ICC：把 .icc 放进程序目录 customICC\，在校色弹窗里选自定义'
Write-Host ''
Write-Host '注意：这一步会真的改动系统颜色管理关联，请先确认文件来源可信。' -ForegroundColor Yellow
