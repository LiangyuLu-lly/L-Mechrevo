# GCU MQTT 抓包工具（无需 Python / 无第三方依赖）
# ------------------------------------------------------------
# 用途：抓取 GCU 服务（127.0.0.1:13688）上的全部 MQTT 消息，
#       用来确认官方控制台/固件到底上报/下发了什么字段。
#
# 用法：双击「运行-GCU抓包.cmd」，或：
#   powershell -NoProfile -ExecutionPolicy Bypass -File gcu-topic-sniff.ps1 -Seconds 180
#
# 输出：
#   桌面 GCU抓包_yyyyMMdd_HHmmss.log（每行：时间 | 方向 | topic | payload）
#   抓包结束后按回车退出（抓包中按 Ctrl+C 可提前结束）
#
# 说明：
# - 纯被动订阅 '#'，不向 GCU 发任何控制指令（不轮询、不写设置）；
# - 凭据使用 LM/官方控制台同一套白名单 client id，自动从 _2/_3/_4 里挑能连上的；
# - 抓包期间请操作一次「官方 GCU 控制台」的温度墙页面（或 LM 自定义窗口），
#   方便定位 CPU_TccOffset / TjMax 等字段。
param(
    [int]$Seconds = 180,
    [string]$BrokerHost = '127.0.0.1',
    [int]$BrokerPort = 13688
)

$ErrorActionPreference = 'Stop'
$script:LogPath = Join-Path ([Environment]::GetFolderPath('Desktop')) ('GCU抓包_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log')
$script:Stop = $false

function Log([string]$line) {
    $ts = (Get-Date -Format 'HH:mm:ss.fff')
    $text = "$ts | $line"
    Write-Host $text
    try { Add-Content -LiteralPath $script:LogPath -Value $text -Encoding UTF8 } catch { }
}

# ---- MQTT 3.1.1 最小实现 ----
function Write-VarInt([byte[]]$buf, [int]$value) {
    $out = New-Object System.Collections.Generic.List[byte]
    do {
        $b = [byte]($value % 128)
        $value = [int][math]::Floor($value / 128)
        if ($value -gt 0) { $b = $b -bor 0x80 }
        $out.Add($b)
    } while ($value -gt 0)
    return [byte[]]$out.ToArray()
}
function New-MqttString([string]$s) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($s)
    $out = New-Object System.Collections.Generic.List[byte]
    $out.Add([byte](($bytes.Length -shr 8) -band 0xFF))
    $out.Add([byte]($bytes.Length -band 0xFF))
    [void]$out.AddRange([byte[]]$bytes)
    return [byte[]]$out.ToArray()
}
function Send-Packet([System.IO.Stream]$stream, [byte]$type, [byte[]]$body) {
    $len = Write-VarInt $null $body.Length
    $header = New-Object System.Collections.Generic.List[byte]
    $header.Add($type)
    [void]$header.AddRange([byte[]]$len)
    [void]$header.AddRange([byte[]]$body)
    $arr = [byte[]]$header.ToArray()
    $stream.Write($arr, 0, $arr.Length)
    $stream.Flush()
}
function Read-Exact([System.IO.Stream]$stream, [int]$count) {
    $buf = New-Object byte[] $count
    $off = 0
    while ($off -lt $count) {
        $n = $stream.Read($buf, $off, $count - $off)
        if ($n -le 0) { throw '连接已断开' }
        $off += $n
    }
    return $buf
}
function Read-Packet([System.IO.Stream]$stream) {
    $first = (Read-Exact $stream 1)[0]
    $multiplier = 1; $remaining = 0
    for ($i = 0; $i -lt 4; $i++) {
        $b = (Read-Exact $stream 1)[0]
        $remaining += ($b -band 0x7F) * $multiplier
        $multiplier *= 128
        if (($b -band 0x80) -eq 0) { break }
    }
    $body = if ($remaining -gt 0) { Read-Exact $stream $remaining } else { New-Object byte[] 0 }
    return @{ Type = [int](($first -shr 4) -band 0x0F); Body = $body }
}

$credentials = @(
    @{ Id = 'UWPClient_2'; User = 'UWPClient_User_2'; Pass = 'UWPClient_Pwd888881772688_2' },
    @{ Id = 'UWPClient_3'; User = 'UWPClient_User_3'; Pass = 'UWPClient_Pwd888881772688_3' },
    @{ Id = 'UWPClient_4'; User = 'UWPClient_User_4'; Pass = 'UWPClient_Pwd888881772688_4' }
)

$client = $null; $stream = $null; $connected = $false
foreach ($cred in $credentials) {
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.Connect($BrokerHost, $BrokerPort)
        $stream = $client.GetStream()
        $stream.ReadTimeout = 2000
        # CONNECT
        $varHeader = New-Object System.Collections.Generic.List[byte]
        [void]$varHeader.AddRange([byte[]](New-MqttString 'MQTT'))
        $varHeader.Add([byte]4)      # level 4 = 3.1.1
        $varHeader.Add([byte]0xC2)   # clean session + username + password
        $varHeader.Add([byte]0); $varHeader.Add([byte]60)  # keepalive 60s
        [void]$varHeader.AddRange([byte[]](New-MqttString $cred.Id))
        [void]$varHeader.AddRange([byte[]](New-MqttString $cred.User))
        [void]$varHeader.AddRange([byte[]](New-MqttString $cred.Pass))
        Send-Packet $stream 0x10 $varHeader.ToArray()
        $connack = Read-Packet $stream
        if ($connack.Type -ne 2 -or $connack.Body.Length -lt 2 -or $connack.Body[1] -ne 0) {
            $rc = if ($connack.Body.Length -ge 2) { $connack.Body[1] } else { -1 }
            throw ("CONNACK 拒绝，返回码=$rc（client id=$($cred.Id)）")
        }
        # SUBSCRIBE '#' qos0
        $sub = New-Object System.Collections.Generic.List[byte]
        $sub.Add([byte]0); $sub.Add([byte]1)          # packet id = 1
        [void]$sub.AddRange([byte[]](New-MqttString '#'))
        $sub.Add([byte]0)
        Send-Packet $stream 0x82 $sub.ToArray()
        $suback = Read-Packet $stream
        if ($suback.Type -ne 9) { throw 'SUBACK 异常' }
        $connected = $true
        Log ("### CONNECTED broker=$BrokerHost`:$BrokerPort as $($cred.Id)，订阅 '#'，抓 $Seconds 秒")
        break
    } catch {
        Log ("[warn] 连接失败（$($cred.Id)）：" + $_.Exception.Message)
        if ($client) { try { $client.Close() } catch { } }
        $client = $null; $stream = $null
    }
}
if (-not $connected) {
    Log '[error] 全部凭据都连不上 GCU broker。请确认官方控制台/GCU 服务在运行。'
    Read-Host '按回车退出' | Out-Null
    exit 2
}

$deadline = (Get-Date).AddSeconds($Seconds)
$lastPing = Get-Date
$count = 0
try {
    while ((Get-Date) -lt $deadline) {
        if ($stream.DataAvailable) {
            $pkt = Read-Packet $stream
            switch ($pkt.Type) {
                3 {
                    # PUBLISH qos0
                    $b = $pkt.Body
                    if ($b.Length -ge 2) {
                        $tl = ($b[0] -shl 8) -bor $b[1]
                        if ($b.Length -ge 2 + $tl) {
                            $topic = [Text.Encoding]::UTF8.GetString($b, 2, $tl)
                            $payload = if ($b.Length -gt 2 + $tl) { [Text.Encoding]::UTF8.GetString($b, 2 + $tl, $b.Length - 2 - $tl) } else { '' }
                            $count++
                            Log ("<<< $topic | " + $payload)
                        }
                    }
                }
                4 { Send-Packet $stream 0x40 $pkt.Body }  # PUBACK（qos1 消息，回执避免重发）
                13 { Send-Packet $stream 0xD0 (New-Object byte[] 0) } # PINGREQ
            }
        } else {
            if (((Get-Date) - $lastPing).TotalSeconds -ge 20) {
                Send-Packet $stream 0xD0 (New-Object byte[] 0)
                $lastPing = Get-Date
            }
            Start-Sleep -Milliseconds 20
        }
    }
    Log ("### 抓包结束：共 $count 条消息；日志：$script:LogPath")
} catch {
    Log ('[error] 抓包中断：' + $_.Exception.Message)
} finally {
    if ($stream) { try { Send-Packet $stream 0xE0 (New-Object byte[] 0) } catch { } }
    if ($client) { try { $client.Close() } catch { } }
}
Write-Host ''
Write-Host ('日志已保存：' + $script:LogPath)
Read-Host '按回车退出' | Out-Null
