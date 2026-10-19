# GCU 灯效抓包（无需 Python）：连本地 MQTT（127.0.0.1:13688），订阅 #，落盘灯效相关报文。
# 用法（普通 PowerShell 即可，不需要管理员）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File D:\openLM\tools\gcu_light_capture.ps1
#   powershell ... -Seconds 300
# 输出（D:\openLM\diagnostics\）：
#   GCU_light_capture_<时间>.jsonl
#   GCU_light_capture_<时间>_灯效.log
#   GCU_light_capture_<时间>_摘要.txt

param(
    [int]$Seconds = 180,
    [string]$BrokerHost = "127.0.0.1",
    [int]$BrokerPort = 13688,
    [string]$OutDir = "D:\openLM\diagnostics"
)

$ErrorActionPreference = "Continue"
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
if (-not (Test-Path $OutDir)) { $OutDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$base = Join-Path $OutDir ("GCU_light_capture_" + $stamp)
$jsonlPath = $base + ".jsonl"
$logPath = $base + "_灯效.log"
$sumPath = $base + "_摘要.txt"

function Log([string]$msg) {
    $line = ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $msg)
    Write-Host $line
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

# ── MQTT 3.1.1 最小客户端 ─────────────────────────────────────
function Get-Utf8Bytes([string]$s) { return [Text.Encoding]::UTF8.GetBytes($s) }

function New-RemainingLength([int]$len) {
    $bytes = New-Object System.Collections.Generic.List[byte]
    do {
        $d = [byte]($len % 128)
        $len = [math]::Floor($len / 128)
        if ($len -gt 0) { $d = $d -bor 0x80 }
        $bytes.Add($d)
    } while ($len -gt 0)
    return $bytes.ToArray()
}

function Send-Packet([System.IO.Stream]$stream, [byte]$header, [byte[]]$body) {
    $rl = New-RemainingLength $body.Length
    $buf = New-Object System.Collections.Generic.List[byte]
    $buf.Add($header)
    foreach ($b in $rl) { $buf.Add($b) }
    if ($body.Length -gt 0) { foreach ($b in $body) { $buf.Add($b) } }
    $arr = $buf.ToArray()
    $stream.Write($arr, 0, $arr.Length)
    $stream.Flush()
}

function Read-Packet([System.IO.Stream]$stream) {
    $one = New-Object byte[] 1
    if ($stream.Read($one, 0, 1) -ne 1) { return $null }
    $header = $one[0]
    $mult = 1; $len = 0
    do {
        if ($stream.Read($one, 0, 1) -ne 1) { return $null }
        $len += ($one[0] -band 0x7F) * $mult
        $mult *= 128
    } while (($one[0] -band 0x80) -ne 0)
    $body = New-Object byte[] $len
    $off = 0
    while ($off -lt $len) {
        $n = $stream.Read($body, $off, $len - $off)
        if ($n -le 0) { return $null }
        $off += $n
    }
    return [pscustomobject]@{ Header = $header; Body = $body }
}

function Connect-MqttClient([string]$clientId, [string]$user, [string]$pwd) {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect($BrokerHost, $BrokerPort)
    $stream = $tcp.GetStream()
    $stream.ReadTimeout = 3000

    $body = New-Object System.Collections.Generic.List[byte]
    $proto = Get-Utf8Bytes "MQTT"
    $body.Add([byte](($proto.Length -shr 8) -band 0xFF)); $body.Add([byte]($proto.Length -band 0xFF))
    foreach ($b in $proto) { $body.Add($b) }
    $body.Add(4)          # protocol level
    $body.Add(0xC2)       # username + password + clean session
    $body.Add(0); $body.Add(60)   # keepalive 60s
    $cid = Get-Utf8Bytes $clientId
    $body.Add([byte](($cid.Length -shr 8) -band 0xFF)); $body.Add([byte]($cid.Length -band 0xFF))
    foreach ($b in $cid) { $body.Add($b) }
    $u = Get-Utf8Bytes $user
    $body.Add([byte](($u.Length -shr 8) -band 0xFF)); $body.Add([byte]($u.Length -band 0xFF))
    foreach ($b in $u) { $body.Add($b) }
    $p = Get-Utf8Bytes $pwd
    $body.Add([byte](($p.Length -shr 8) -band 0xFF)); $body.Add([byte]($p.Length -band 0xFF))
    foreach ($b in $p) { $body.Add($b) }

    Send-Packet $stream 0x10 $body.ToArray()
    $ack = Read-Packet $stream
    if ($null -eq $ack -or $ack.Header -ne 0x20 -or $ack.Body.Length -lt 2 -or $ack.Body[1] -ne 0) {
        try { $tcp.Close() } catch { }
        return $null
    }

    $sub = New-Object System.Collections.Generic.List[byte]
    $sub.Add(0); $sub.Add(1)                 # packet id
    $filter = Get-Utf8Bytes "#"
    $sub.Add([byte](($filter.Length -shr 8) -band 0xFF)); $sub.Add([byte]($filter.Length -band 0xFF))
    foreach ($b in $filter) { $sub.Add($b) }
    $sub.Add(0)                              # QoS 0
    Send-Packet $stream 0x82 $sub.ToArray()

    return [pscustomobject]@{ Tcp = $tcp; Stream = $stream; LastIo = [DateTime]::Now }
}

$creds = @(
    @{ Id = "UWPClient_2"; User = "UWPClient_User_2"; Pwd = "UWPClient_Pwd888881772688_2" },
    @{ Id = "UWPClient_3"; User = "UWPClient_User_3"; Pwd = "UWPClient_Pwd888881772688_3" },
    @{ Id = "GCUClient"; User = "GCUClient_User"; Pwd = "GCUClient_Pwd888881772688" }
)

Set-Content -Path $logPath -Value "# 灯效抓包 开始=$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) 时长=${Seconds}s" -Encoding UTF8
Set-Content -Path $jsonlPath -Value "" -Encoding UTF8

$conn = $null
$used = ""
foreach ($c in $creds) {
    try {
        $conn = Connect-MqttClient $c.Id $c.User $c.Pwd
    } catch { $conn = $null }
    if ($null -ne $conn) { $used = $c.Id; break }
}
if ($null -eq $conn) {
    Write-Host "[X] 连不上 $BrokerHost`:$BrokerPort（MSG 服务未运行？）"
    exit 1
}
Log "已连接（$used），订阅 #；现在去官方控制台切换 Logo 灯效（每种停 3~5 秒）"

$lightParts = @("hidlightbar", "keyboard/", "lighting", "/ctrl")
function Test-LightTopic([string]$topic) {
    $t = $topic.ToLowerInvariant()
    foreach ($p in $lightParts) { if ($t.Contains($p)) { return $true } }
    return $false
}

$stream = $conn.Stream
$deadline = [DateTime]::Now.AddSeconds($Seconds)
$count = 0
$lightCount = 0
$records = New-Object System.Collections.Generic.List[object]

while ([DateTime]::Now -lt $deadline) {
    # 20 秒没消息发一次 PINGREQ
    if ((([DateTime]::Now - $conn.LastIo).TotalSeconds) -gt 20) {
        Send-Packet $stream 0xC0 (New-Object byte[] 0)
        $conn.LastIo = [DateTime]::Now
    }
    if (-not $stream.DataAvailable) { Start-Sleep -Milliseconds 50; continue }
    $pkt = Read-Packet $stream
    if ($null -eq $pkt) { break }
    $conn.LastIo = [DateTime]::Now
    $type = [int]($pkt.Header -shr 4)
    if ($type -eq 12) { Send-Packet $stream 0xD0 (New-Object byte[] 0); continue }   # PINGRESP
    if ($type -ne 3) { continue }                                                    # 只处理 PUBLISH

    $qos = [int](($pkt.Header -shr 1) -band 0x03)
    $b = $pkt.Body
    $tlen = ([int]$b[0] -shl 8) -bor [int]$b[1]
    if ($tlen -le 0 -or 2 + $tlen -gt $b.Length) { continue }
    $topic = [Text.Encoding]::UTF8.GetString($b, 2, $tlen)
    $payloadStart = 2 + $tlen
    if ($qos -gt 0) { $payloadStart += 2 }
    if ($payloadStart -gt $b.Length) { continue }
    $payload = [Text.Encoding]::UTF8.GetString($b, $payloadStart, $b.Length - $payloadStart)

    $count++
    $rec = [pscustomobject]@{ t = (Get-Date -Format "HH:mm:ss.fff"); topic = $topic; payload = $payload }
    $records.Add($rec)
    Add-Content -Path $jsonlPath -Value ($rec | ConvertTo-Json -Compress) -Encoding UTF8

    if (Test-LightTopic $topic) {
        $lightCount++
        Log ("{0} | {1}" -f $topic, $payload.Substring(0, [Math]::Min(500, $payload.Length)))
    }
}

try { $conn.Tcp.Close() } catch { }
Log "抓包结束：报文 $count 条，灯效相关 $lightCount 条"

# 摘要：按 topic+effect 去重，保留最后一条完整 payload
$summary = @{}
foreach ($r in $records) {
    if (-not (Test-LightTopic $r.topic)) { continue }
    try { $obj = $r.payload | ConvertFrom-Json } catch { continue }
    $effect = ""
    if ($obj.PSObject.Properties.Name -contains "effect") { $effect = [string]$obj.effect }
    $key = $r.topic + "|" + $effect
    $summary[$key] = $r.payload
}
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# 按 topic+effect 去重（最后一条完整 payload）")
$lines.Add("")
foreach ($k in ($summary.Keys | Sort-Object)) {
    $lines.Add("## $k")
    $lines.Add([string]$summary[$k])
    $lines.Add("")
}
Set-Content -Path $sumPath -Value $lines -Encoding UTF8

Write-Host ""
Write-Host "抓包结束：报文 $count 条，灯效相关 $lightCount 条"
Write-Host "  全部: $jsonlPath"
Write-Host "  灯效: $logPath"
Write-Host "  摘要: $sumPath"
