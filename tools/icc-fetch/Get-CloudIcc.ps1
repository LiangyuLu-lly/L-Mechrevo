param(
    [string]$OutDir = $PSScriptRoot
)
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
            $name = ($_.UserFriendlyName | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            $serial = ($_.SerialNumberID | Where-Object { $_ -gt 0 } | ForEach-Object { [char]$_ }) -join ''
            $ids += [pscustomobject]@{ Name = $name; Serial = $serial; Raw = ($_.InstanceName) }
        }
    } catch { }
    if ($ids.Count -eq 0) {
        try {
            Get-CimInstance Win32_DesktopMonitor | ForEach-Object { $ids += [pscustomobject]@{ Name = $_.Name; Serial = ''; Raw = $_.PNPDeviceID } }
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
$monitors | ForEach-Object { Write-Host ("  " + $_.Name + " / " + $_.Serial + " / " + $_.Raw) }
Write-Host "MACs: $($macs.Count)"
$macs | ForEach-Object { Write-Host ("  " + $_) }

$names = @()
foreach ($m in $monitors) {
    $id = $m.Name
    if (-not $id -and $m.Raw -match 'DISPLAY\\([^\\]+)') { $id = $matches[1] }
    if (-not $id) { continue }
    if ($serial) { $names += ($id + '_' + $serial) }
    foreach ($mac in $macs) { $names += ($id + '_' + $mac) }
    $names += $id
}
$names = $names | Select-Object -Unique
Write-Host "Candidate names: $($names.Count)"
$names | ForEach-Object { Write-Host ("  " + $_) }

foreach ($server in $servers) {
    Write-Host "`n=== $server ==="
    foreach ($name in $names) {
        foreach ($suffix in @('', '.icm')) {
            $url = $server + $name + $suffix
            try {
                $resp = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 6 -ErrorAction Stop
                $bytes = $resp.RawContentStream.ToArray()
                $sig = if ($bytes.Length -ge 40) { [Text.Encoding]::ASCII.GetString($bytes, 36, 4) } else { '' }
                $isIcc = $sig -eq 'acsp'
                $isZip = ($bytes.Length -ge 2 -and $bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B)
                $ext = if ($isIcc) { '.icm' } elseif ($isZip) { '.zip' } else { '.bin' }
                $safe = ($name + $suffix + $ext) -replace '[^A-Za-z0-9._-]','_'
                $path = Join-Path $OutDir $safe
                [IO.File]::WriteAllBytes($path, $bytes)
                Write-Host ("HIT " + $url + " -> " + $bytes.Length + " bytes sig=" + $sig + " saved=" + $path)
                if ($isIcc) { break }
            } catch {
                $code = ''
                try { $code = [int]$_.Exception.Response.StatusCode } catch { }
                Write-Host ("MISS " + $url + " code=" + $code + " " + $_.Exception.Message)
            }
        }
    }
}
Write-Host "`nDone: $OutDir"
