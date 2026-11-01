<#
.SYNOPSIS
  GCU 安装前后快照采集（严格只读）。

.DESCRIPTION
  用途：为「手动全新安装 GCU（补全后的 40A）」做安装前基线 / 安装后对照，便于精确 diff。

  只读保证：
    * 不修改、不删除 C:\Program Files\L-Mechrevo\GCU\ 下任何文件；
    * 不写注册表、不启停服务、不启停进程、不重启；
    * 不向 13688 端口发起任何连接（只用 Get-NetTCPConnection / netstat 观察）。

  唯一写入位置：-OutDir 指定的快照目录（默认为脚本所在目录下的 .\gcu-<Label>-<时间戳>\）。
  采集项各自 try/catch：单项失败只记录，不影响其它项。可重复运行（幂等）。
  ⚠ 不依赖任何固定盘符/仓库目录：期望值（payload\40 的 13 件）已内嵌在脚本里。

.PARAMETER OutDir
  快照输出目录。默认：<脚本所在目录>\gcu-<Label>-<yyyyMMdd-HHmmss>

.PARAMETER Label
  快照标签，仅用于拼默认目录名：before（安装前）/ after（安装后）。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File ".\GCU快照.ps1" -Label before

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File ".\GCU快照.ps1" -Label after
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Label = 'before'
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$script:Stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:NowText = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$script:Errors  = New-Object System.Collections.ArrayList

# 默认输出目录：脚本所在目录（$PSScriptRoot），不假设任何固定盘符
$DiagRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DiagRoot)) { $DiagRoot = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $DiagRoot ("gcu-$Label-$script:Stamp")
}
$OutDirFull = [System.IO.Path]::GetFullPath($OutDir)
try {
    if (-not (Test-Path -LiteralPath $OutDirFull)) {
        New-Item -ItemType Directory -Path $OutDirFull -Force | Out-Null
    }
} catch {
    Write-Host "无法创建输出目录 $OutDirFull : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

function Save-Text {
    param([string]$Path, [string]$Text)
    # UTF-8 带 BOM：保证 PowerShell 5.1 能正确读中文
    $enc = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Save-Lines {
    param([string]$Path, [System.Collections.ArrayList]$Lines)
    Save-Text -Path $Path -Text (($Lines -join "`r`n") + "`r`n")
}

function Add-Err {
    param([string]$Item, [string]$Message)
    [void]$script:Errors.Add("[$Item] $Message")
}

# ============================================================
# 期望值常量表（payload\40 覆盖层，一次性内嵌）
# ⚠ 内嵌目的：脚本不再依赖任何固定盘符/仓库目录（原实现从 D 盘仓库现读，换台机器就废）。
# 结构：相对路径(AiStoneService 之下) | 字节大小 | SHA256(小写全串) | mtime
# ============================================================
$Exp40Payload = [ordered]@{
    'GCUBridge.exe'                                  = '2125552|5ebf6789441c63eb|2025-07-16 18:41:04'
    'UEFI_Firmware.dll'                              = '134312|10ccf9bcbfbb971d|2025-07-16 18:41:04'
    'MyControlCenter\GCUService.exe'                 = '14447864|a2b7d81ecf5c1dbdfc19738f17ba5e01e884d1b3688beb668858727307e2a318|2025-07-16 18:40:58'
    'MyControlCenter\UEFI_Firmware.dll'              = '134312|e4902f551de0a292|2025-07-16 18:41:04'
    'MyControlCenter\NVControlSetting.dll'           = '2775208|1e483d95ee7a5570|2025-07-16 18:41:02'
    'MyControlCenter\GPUInfoDLL.dll'                 = '2581672|f7ed3ffa383ec6aa|2025-07-16 18:41:02'
    'MyControlCenter\ACPIDriverDll.dll'              = '4116648|d8698164417a8416|2025-07-16 18:40:58'
    'MyControlCenter\OSDTpDetect.exe'                = '525480|c022268f3aa8554f|2025-07-16 18:41:02'
    'MyControlCenter\RegistryUtils.dll'              = '34984|30c8dd7cb89aac29|2025-07-16 18:41:02'
    'MyControlCenter\UWAirplane.dll'                 = '133872|4f55c21c15dcacce|2025-07-16 18:41:02'
    'MyControlCenter\UWDiskInfo.dll'                 = '2754728|935ea7d335e7dd86|2025-07-16 18:41:02'
    'MyControlCenter\DisplayInfo.dll'                = '2548976|6e4267cb01d50845|2025-07-16 18:41:02'
    'MyControlCenter\AppSettings\GamingMonitor.json' = '36|92bd428d4564e583|2025-02-12 15:25:14'
    'README.txt'                                     = '4732|86ed4437e8fe7706|2025-07-16 18:43:12'
}

function Get-ExpRow {
    param([string]$Rel)
    $k = ([string]$Rel).TrimStart('\')
    if (-not $Exp40Payload.Contains($k)) { return $null }
    $p = ([string]$Exp40Payload[$k]) -split '\|'
    return @{ Rel = $k; Size = [int]$p[0]; Hash = [string]$p[1]; Mtime = [string]$p[2] }
}

function Get-PathOrCim {
    param([int]$ProcId, $CimAll)
    $p = ''
    try { $p = (Get-Process -Id $ProcId -ErrorAction Stop).Path } catch { $p = '' }
    if ([string]::IsNullOrWhiteSpace($p)) {
        $c = $CimAll | Where-Object { $_.ProcessId -eq $ProcId } | Select-Object -First 1
        if ($c -and $c.ExecutablePath) { $p = $c.ExecutablePath }
    }
    if ([string]::IsNullOrWhiteSpace($p)) { $p = '（路径不可读：权限不足）' }
    return $p
}

function Get-StartOrCim {
    param([int]$ProcId, $CimAll)
    $s = ''
    try { $s = (Get-Process -Id $ProcId -ErrorAction Stop).StartTime.ToString('yyyy-MM-dd HH:mm:ss') } catch { $s = '' }
    if ([string]::IsNullOrWhiteSpace($s)) {
        $c = $CimAll | Where-Object { $_.ProcessId -eq $ProcId } | Select-Object -First 1
        if ($c -and $c.CreationDate) { $s = ([datetime]$c.CreationDate).ToString('yyyy-MM-dd HH:mm:ss') + '（来源：WMI）' }
    }
    if ([string]::IsNullOrWhiteSpace($s)) { $s = '（不可读：权限不足）' }
    return $s
}

$CommonHeader = @(
    "# 本文件由 GCU快照.ps1（$PSCommandPath）只读采集生成；以 # 开头的行是注释，diff 时请忽略",
    "# 采集时间: $script:NowText",
    "# 快照标签: $Label",
    "# 快照目录: $OutDirFull",
    "# 数据列之间用 <TAB> 分隔"
)

# ============================================================
# 01-文件清单.tsv  —— C:\Program Files\L-Mechrevo\GCU\ 递归清单
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 采集对象: C:\Program Files\L-Mechrevo\GCU\（递归，含隐藏文件；只读，未做任何修改）')
[void]$lines.Add("# 相对路径`t字节大小`tmtime`tSHA256")
try {
    $root = 'C:\Program Files\L-Mechrevo\GCU'
    if (-not (Test-Path -LiteralPath $root)) { throw "目录不存在: $root" }
    $rootFull = (Get-Item -LiteralPath $root).FullName.TrimEnd('\') + '\'
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue)
    $rows = New-Object System.Collections.ArrayList
    $badHash = 0
    $sumBytes = 0
    foreach ($f in $files) {
        $sumBytes += $f.Length
        $rel = $f.FullName.Substring($rootFull.Length)
        $h = ''
        try { $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLower() }
        catch { $h = 'HASH_FAILED'; $badHash++ }
        [void]$rows.Add([pscustomobject]@{
            Rel   = $rel
            Size  = $f.Length
            Mtime = $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
            Hash  = $h
        })
    }
    foreach ($r in ($rows | Sort-Object Rel)) {
        [void]$lines.Add("$($r.Rel)`t$($r.Size)`t$($r.Mtime)`t$($r.Hash)")
    }
    [void]$lines.Add("# 小计: 文件数=$($rows.Count) 目录总字节=$sumBytes SHA256失败=$badHash")
} catch {
    Add-Err '01-文件清单' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '01-文件清单.tsv') -Lines $lines

# ============================================================
# 02-服务与进程.txt
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 本项只做查询，未启停任何服务/进程。')
$svcNames = @('GCUBridge', 'GCUService')
$cimAll = @()
try { $cimAll = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue) } catch { Add-Err '02-Win32_Process' $_.Exception.Message }

try {
    [void]$lines.Add('')
    [void]$lines.Add('## 一、SCM 服务状态（Get-Service）')
    foreach ($n in $svcNames) {
        try {
            $s = Get-Service -Name $n -ErrorAction SilentlyContinue
            if ($null -eq $s) { [void]$lines.Add("服务 $n : 不存在（SCM 中查无此服务）"); continue }
            [void]$lines.Add("服务 $n : Name=$($s.Name) Status=$($s.Status) StartType=$($s.StartType) DisplayName=$($s.DisplayName)")
            try {
                $w = Get-CimInstance Win32_Service -Filter "Name='$n'" -ErrorAction Stop
                if ($w) { [void]$lines.Add("    Win32_Service: State=$($w.State) StartMode=$($w.StartMode) ProcessId=$($w.ProcessId) PathName=$($w.PathName)") }
            } catch { [void]$lines.Add("    Win32_Service 补充查询失败: $($_.Exception.Message)") }
        } catch { Add-Err "02-服务 $n" $_.Exception.Message; [void]$lines.Add("服务 $n 查询失败: $($_.Exception.Message)") }
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 二、sc.exe qc 原始输出（ImagePath / StartType / DisplayName）')
    foreach ($n in $svcNames) {
        [void]$lines.Add("--- sc.exe qc $n ---")
        try {
            $o = & sc.exe qc $n 2>&1
            foreach ($l in $o) { [void]$lines.Add([string]$l) }
        } catch { Add-Err "02-sc.exe qc $n" $_.Exception.Message; [void]$lines.Add("执行失败: $($_.Exception.Message)") }
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 三、GCU 相关进程')
    [void]$lines.Add('# 命中规则：进程名匹配 GCUBridge|GCUService|OSDTpDetect|GCUUtil，或可执行路径含 \GCU\')
    $namePat = 'GCUBridge|GCUService|OSDTpDetect|GCUUtil'
    $pidSet = New-Object System.Collections.Generic.HashSet[int]
    try {
        foreach ($p in (Get-Process -ErrorAction SilentlyContinue)) {
            if ($p.ProcessName -match $namePat) { [void]$pidSet.Add([int]$p.Id) }
        }
    } catch { Add-Err '02-Get-Process' $_.Exception.Message }
    try {
        foreach ($c in $cimAll) {
            if ($c.ExecutablePath -and $c.ExecutablePath -like '*\GCU\*') { [void]$pidSet.Add([int]$c.ProcessId) }
        }
    } catch { Add-Err '02-路径扫描' $_.Exception.Message }

    if ($pidSet.Count -eq 0) {
        [void]$lines.Add('（未发现 GCU 相关进程）')
    } else {
        [void]$lines.Add("进程名`tPID`t启动时间`t可执行路径")
        foreach ($procId in ($pidSet | Sort-Object)) {
            try {
                $nm = ''
                try { $nm = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { $nm = '' }
                if ([string]::IsNullOrWhiteSpace($nm)) {
                    $cc = $cimAll | Where-Object { $_.ProcessId -eq $procId } | Select-Object -First 1
                    if ($cc) { $nm = $cc.Name }
                }
                $st = Get-StartOrCim -ProcId $procId -CimAll $cimAll
                $pp = Get-PathOrCim -ProcId $procId -CimAll $cimAll
                [void]$lines.Add("$nm`t$procId`t$st`t$pp")
            } catch { Add-Err "02-进程 $procId" $_.Exception.Message }
        }
        [void]$lines.Add("# 小计: GCU 相关进程数=$($pidSet.Count)")
    }
} catch {
    Add-Err '02-服务与进程' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '02-服务与进程.txt') -Lines $lines

# ============================================================
# 03-端口与占用.txt
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 本项只观察端口，未向 13688 发起任何连接。')
try {
    $allTcp = @(Get-NetTCPConnection -ErrorAction SilentlyContinue)

    [void]$lines.Add('')
    [void]$lines.Add('## 一、Get-NetTCPConnection -LocalPort 13688')
    $local = @($allTcp | Where-Object { $_.LocalPort -eq 13688 })
    if ($local.Count -eq 0) {
        [void]$lines.Add('（无任何本地 13688 连接/监听）')
    } else {
        [void]$lines.Add("LocalAddress`tLocalPort`tRemoteAddress`tRemotePort`tState`tOwningProcess`t进程名`t进程路径")
        foreach ($c in $local) {
            $pname = ''; $ppath = ''
            try {
                $pr = Get-Process -Id $c.OwningProcess -ErrorAction Stop
                $pname = $pr.ProcessName
            } catch { $pname = '（不可读）' }
            $ppath = Get-PathOrCim -ProcId ([int]$c.OwningProcess) -CimAll $cimAll
            [void]$lines.Add("$($c.LocalAddress)`t$($c.LocalPort)`t$($c.RemoteAddress)`t$($c.RemotePort)`t$($c.State)`t$($c.OwningProcess)`t$pname`t$ppath")
        }
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 二、netstat -ano 中含 13688 的行（等价 netstat -ano ^| findstr 13688）')
    try {
        $ns = & netstat -ano 2>&1 | Select-String -Pattern '13688' -SimpleMatch
        if ($ns) { foreach ($l in $ns) { [void]$lines.Add(([string]$l).Trim()) } }
        else { [void]$lines.Add('（netstat 输出中无 13688）') }
    } catch { Add-Err '03-netstat' $_.Exception.Message; [void]$lines.Add("netstat 失败: $($_.Exception.Message)") }

    [void]$lines.Add('')
    [void]$lines.Add('## 三、结论（脚本根据同一时刻的连接表判定）')
    $est = @($local | Where-Object { $_.State -eq 'Established' })
    $listen = @($local | Where-Object { $_.State -eq 'Listen' })

    if ($listen.Count -gt 0) {
        foreach ($c in $listen) {
            $pname = ''
            try { $pname = (Get-Process -Id $c.OwningProcess -ErrorAction Stop).ProcessName } catch { $pname = '（不可读）' }
            $ppath = Get-PathOrCim -ProcId ([int]$c.OwningProcess) -CimAll $cimAll
            [void]$lines.Add("· 13688 监听者: $($c.LocalAddress):13688  进程=$pname  PID=$($c.OwningProcess)（$ppath）")
        }
    } else {
        [void]$lines.Add('· 13688 当前没有监听者。')
    }

    if ($est.Count -eq 0) {
        [void]$lines.Add('· 当前是否存在 ESTABLISHED 会话: 否')
    } else {
        [void]$lines.Add("· 当前是否存在 ESTABLISHED 会话: 是（共 $($est.Count) 条）")
        $peerIds = New-Object System.Collections.ArrayList
        foreach ($c in $est) {
            $peer = $allTcp | Where-Object {
                $_.LocalPort -eq $c.RemotePort -and $_.RemotePort -eq $c.LocalPort -and $_.LocalAddress -eq $c.RemoteAddress
            } | Select-Object -First 1
            if ($peer) {
                $pn = ''
                try { $pn = (Get-Process -Id $peer.OwningProcess -ErrorAction Stop).ProcessName } catch { $pn = '（不可读）' }
                $pp = Get-PathOrCim -ProcId ([int]$peer.OwningProcess) -CimAll $cimAll
                [void]$peerIds.Add("$pn(PID=$($peer.OwningProcess))")
                [void]$lines.Add("  - 本地 $($c.LocalAddress):$($c.LocalPort) <- 对端 $($c.RemoteAddress):$($c.RemotePort)  对端进程=$pn PID=$($peer.OwningProcess) 路径=$pp")
            } else {
                [void]$lines.Add("  - 本地 $($c.LocalAddress):$($c.LocalPort) <- 对端 $($c.RemoteAddress):$($c.RemotePort)  对端进程=未能解析（该连接可能刚被对端关闭或权限不足）")
            }
        }
        $uniq = @($peerIds | Sort-Object -Unique)
        if ($uniq.Count -gt 0) {
            [void]$lines.Add("· 对端 PID/进程: $($uniq -join '; ')")
            if (($uniq -join ';') -match 'L-Mechrevo') { [void]$lines.Add('· 对端是否为 L-Mechrevo: 是（对端进程名含 L-Mechrevo）') }
            else { [void]$lines.Add('· 对端是否为 L-Mechrevo: 否（对端进程名不含 L-Mechrevo，请见上表）') }
        } else {
            [void]$lines.Add('· 对端 PID/进程: 无法解析；是否为 L-Mechrevo: 未确定')
        }
    }
} catch {
    Add-Err '03-端口与占用' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '03-端口与占用.txt') -Lines $lines

# ============================================================
# 04-注册表.txt
# ============================================================
function Add-RegKey {
    param(
        [System.Collections.ArrayList]$Lines,
        [string]$Key,
        [string]$ValueNameRegex = ''
    )
    [void]$Lines.Add("--- $Key ---")
    try {
        if (-not (Test-Path -LiteralPath $Key)) { [void]$Lines.Add('(键不存在)'); return }
        $k = Get-Item -LiteralPath $Key -ErrorAction Stop
        $names = @($k.GetValueNames())
        if ($ValueNameRegex -ne '') { $names = @($names | Where-Object { $_ -match $ValueNameRegex }) }
        if ($names.Count -eq 0) { [void]$Lines.Add('(无匹配值)'); return }
        [void]$Lines.Add("值名`t类型`t值")
        $maskRe = 'pass|pwd|secret|token|credential|apikey|licen'
        foreach ($n in ($names | Sort-Object)) {
            $disp = $n
            if ([string]::IsNullOrEmpty($n)) { $disp = '(默认值)' }
            $kind = ''
            try { $kind = $k.GetValueKind($n).ToString() } catch { $kind = '?' }
            $val = ''
            try {
                $v = $k.GetValue($n)
                if ($v -is [array]) { $val = ($v -join ',') } else { $val = [string]$v }
            } catch { $val = '<读取失败>' }
            if ($disp -match $maskRe) { $val = '<已打码>' }
            [void]$Lines.Add("$disp`t$kind`t$val")
        }
    } catch {
        Add-Err "04-注册表 $Key" $_.Exception.Message
        [void]$Lines.Add("读取失败: $($_.Exception.Message)")
    }
}

$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 本项只读注册表；未写入/删除任何键值。凭据类值名（pass/pwd/secret/token/credential 等）已打码。')
try {
    [void]$lines.Add('')
    [void]$lines.Add('## 一、HKLM\SOFTWARE\L-Mechrevo（全部值）')
    Add-RegKey -Lines $lines -Key 'HKLM:\SOFTWARE\L-Mechrevo'
    [void]$lines.Add('（若上面缺少 GcuVariant / GcuPayloadSha256 / GcuServiceExe / GcuInstalledUtc，说明该键当前未写入这些值）')

    [void]$lines.Add('')
    [void]$lines.Add('## 二、GCU 服务注册表项（ImagePath / Start / Type / DisplayName）')
    Add-RegKey -Lines $lines -Key 'HKLM:\SYSTEM\CurrentControlSet\Services\GCUBridge'
    Add-RegKey -Lines $lines -Key 'HKLM:\SYSTEM\CurrentControlSet\Services\GCUService'

    [void]$lines.Add('')
    [void]$lines.Add('## 三、HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport（只列显卡/键盘/灯条/能力相关键）')
    [void]$lines.Add('# 过滤正则: Gpu|IGPU|Display|Keyboard|Lightbar|Project|IsOldType|Support|Service|Ready（不区分大小写）')
    Add-RegKey -Lines $lines -Key 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport' -ValueNameRegex 'Gpu|IGPU|Display|Keyboard|Lightbar|Project|IsOldType|Support|Service|Ready'

    [void]$lines.Add('')
    [void]$lines.Add('## 四、ItemSupport 完整值列表（便于前后 diff，确认有无新增/变化的能力键）')
    Add-RegKey -Lines $lines -Key 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport'
} catch {
    Add-Err '04-注册表' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '04-注册表.txt') -Lines $lines

# ============================================================
# 05-LM状态.txt
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 本项只读文件/进程/配置文件，不修改 LM 与 GCU。')
try {
    [void]$lines.Add('')
    [void]$lines.Add('## 一、安装态 C:\Program Files\L-Mechrevo\L-Mechrevo.dll')
    $dll = 'C:\Program Files\L-Mechrevo\L-Mechrevo.dll'
    if (Test-Path -LiteralPath $dll) {
        $fi = Get-Item -LiteralPath $dll
        [void]$lines.Add("大小(字节)`t$($fi.Length)")
        [void]$lines.Add("mtime`t$($fi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))")
        try { [void]$lines.Add("SHA256`t$((Get-FileHash -LiteralPath $dll -Algorithm SHA256 -ErrorAction Stop).Hash.ToLower())") }
        catch { Add-Err '05-dll SHA256' $_.Exception.Message; [void]$lines.Add("SHA256`t<失败: $($_.Exception.Message)>") }
        try {
            [void]$lines.Add("FileVersion`t$($fi.VersionInfo.FileVersion)")
            [void]$lines.Add("ProductVersion`t$($fi.VersionInfo.ProductVersion)")
        } catch { [void]$lines.Add("FileVersion`t<不可读>") }
    } else {
        [void]$lines.Add("(文件不存在: $dll)")
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 二、L-Mechrevo 进程（PID / 启动时间 / 路径）')
    $lmProcs = @()
    try { $lmProcs = @(Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue) } catch { Add-Err '05-LM进程' $_.Exception.Message }
    if ($lmProcs.Count -eq 0) {
        [void]$lines.Add('（当前没有 L-Mechrevo 进程在运行）')
    } else {
        [void]$lines.Add("PID`t启动时间`t可执行路径")
        foreach ($p in $lmProcs) {
            $st = Get-StartOrCim -ProcId ([int]$p.Id) -CimAll $cimAll
            $pp = Get-PathOrCim -ProcId ([int]$p.Id) -CimAll $cimAll
            [void]$lines.Add("$($p.Id)`t$st`t$pp")
        }
        [void]$lines.Add("# 小计: L-Mechrevo 进程数=$($lmProcs.Count)")
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 三、%APPDATA%\MechrevoLite\config.json 中 GCU/显卡模式相关键')
    [void]$lines.Add('# 只列键名匹配 gcu_* / gpu_* / igpu_only_* 的项（不贴全文）')
    $cfg = Join-Path $env:APPDATA 'MechrevoLite\config.json'
    [void]$lines.Add("配置文件路径`t$cfg")
    if (-not (Test-Path -LiteralPath $cfg)) {
        [void]$lines.Add('（配置文件不存在）')
    } else {
        $cfi = Get-Item -LiteralPath $cfg
        [void]$lines.Add("文件大小(字节)`t$($cfi.Length)   mtime`t$($cfi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))")
        try {
            $j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
            $hit = @($j.PSObject.Properties | Where-Object { $_.Name -match '^(?i)(gcu_|gpu_|igpu_only_)' })
            if ($hit.Count -eq 0) {
                [void]$lines.Add('（config.json 中无 gcu_/gpu_/igpu_only_ 前缀键；可能键名不同，需人工确认）')
            } else {
                [void]$lines.Add("键名`t值")
                foreach ($prop in ($hit | Sort-Object Name)) {
                    $v = $prop.Value
                    $vs = ''
                    if ($v -is [string] -or $v -is [int] -or $v -is [bool] -or $v -is [double] -or $v -is [long]) { $vs = [string]$v }
                    elseif ($null -eq $v) { $vs = 'null' }
                    else { try { $vs = ($v | ConvertTo-Json -Compress -Depth 6) } catch { $vs = '<复杂对象>' } }
                    [void]$lines.Add("$($prop.Name)`t$vs")
                }
            }
        } catch {
            Add-Err '05-config.json' $_.Exception.Message
            [void]$lines.Add("解析失败: $($_.Exception.Message)")
        }
    }
} catch {
    Add-Err '05-LM状态' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '05-LM状态.txt') -Lines $lines

# ============================================================
# 06-期望清单-40A.tsv  —— payload\40 覆盖层期望内容
# ============================================================
$lines = New-Object System.Collections.ArrayList
[void]$lines.Add('这是 40A 覆盖层的期望内容（13 个载荷文件 + README）')
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 期望值来源: 内嵌在脚本里的常量表（$Exp40Payload）—— 不再读仓库/payload\40，换台机器也能用')
[void]$lines.Add("# 相对路径`t字节大小`tmtime`tSHA256")
try {
    $rows = new-object System.Collections.ArrayList
    foreach ($rel in $Exp40Payload.Keys) {
        $p = ([string]$Exp40Payload[$rel]) -split '\|'
        [void]$rows.Add([pscustomobject]@{ Rel = $rel; Size = [int]$p[0]; Mtime = [string]$p[2]; Hash = [string]$p[1] })
    }
    foreach ($r in ($rows | Sort-Object Rel)) { [void]$lines.Add("$($r.Rel)`t$($r.Size)`t$($r.Mtime)`t$($r.Hash)") }
    $payloadCount = @($rows | Where-Object { $_.Rel -ne 'README.txt' }).Count
    [void]$lines.Add("# 小计: 文件数=$($rows.Count)（其中载荷文件 $payloadCount 个 + README.txt；.iss 只打包这 $payloadCount 个载荷文件）")
} catch {
    Add-Err '06-期望清单' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '06-期望清单-40A.tsv') -Lines $lines

# ============================================================
# 07-iss清单-40.txt（内嵌：不再读仓库里的 install_script.iss）
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 40A 进包清单（内嵌常量）：实际进包的 13 个文件（不含 README.txt）')
[void]$lines.Add('# 原实现是摘录仓库里的 install_script.iss；现改为内嵌，脚本不再依赖任何固定盘符。')
try {
    $idx = 0
    foreach ($rel in $Exp40Payload.Keys) {
        if ($rel -eq 'README.txt') { continue }
        $idx++
        $p = ([string]$Exp40Payload[$rel]) -split '\|'
        [void]$lines.Add(("{0,2}. payload\40\AiStoneService\{1}`t{2} 字节`tSHA256 {3}" -f $idx, $rel, $p[0], $p[1]))
    }
    [void]$lines.Add("# 小计: 进包载荷文件 $idx 个（另有 README.txt 不打包）")
} catch {
    Add-Err '07-iss清单' $_.Exception.Message
    [void]$lines.Add("# 生成失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '07-iss清单-40.txt') -Lines $lines

# ============================================================
# 08-说明.txt
# ============================================================
$explain = @'
GCU 安装前后快照说明
================================================================
一、本快照的身份
  这是【安装前】基线快照。用户将手动全新安装"补全后的 40A"版 GCU，装完后需要用
  同一套只读采集再生成一份 gcu-after-* 快照，然后做精确 diff。

二、怎么再跑一次（同一套采集）
  安装后执行（只改 -Label；在脚本所在目录下运行）：
    powershell -ExecutionPolicy Bypass -File ".\GCU快照.ps1" -Label after
  也可显式指定输出目录（换成任意可写目录即可）：
    powershell -ExecutionPolicy Bypass -File ".\GCU快照.ps1" -OutDir "$env:USERPROFILE\Desktop\gcu-after"

  注意：期望值（40A 的 13 件）已【内嵌在脚本里】，不需要仓库、不依赖任何固定盘符。

三、对比要点（按文件）
  1) 01-文件清单.tsv  vs  gcu-after\01-文件清单.tsv
     * 增 / 删 / 改三类：相对路径的新增与消失，以及同一路径的 SHA256 或大小变化。
     * 重点看【旧的 base 文件是否残留】：安装后目录文件集应当"等于"06-期望清单-40A.tsv
       的 13 个载荷文件（+README）【如果这次是全量覆盖式安装】，若仍存在大量旧文件，
       说明是"13 项 + 旧残留"的覆盖式安装。
     * 建议命令（忽略以 # 开头的注释行）：
         Compare-Object (Get-Content <before>\01-文件清单.tsv) (Get-Content <after>\01-文件清单.tsv)
  2) 02-服务与进程.txt
     * GCUBridge / GCUService 的 PID 与启动时间是否变化（PID 变化 = 服务被重启过）。
     * 是否新出现 GCUService 服务（当前 SCM 中 GCUService 不存在，只有 GCUBridge）。
     * 是否新出现 GCUUtil 或其它 \GCU\ 路径下的进程。
  3) 03-端口与占用.txt
     * 13688 的监听者是否仍是 GCUBridge，PID 是否变化。
     * 是否出现 ESTABLISHED 会话，以及对端进程是否为 L-Mechrevo（正常联动时应有）。
  4) 04-注册表.txt
     * 重点比对 HKLM\SOFTWARE\L-Mechrevo 下的：
       GcuVariant / GcuPayloadSha256 / GcuServiceExe / GcuInstalledUtc 是否变化。
       （GcuPayloadSha256 变化 = 载荷内容确实换了；GcuInstalledUtc 变化 = 本次安装时间。）
     * GCUBridge / GCUService 服务项的 ImagePath / Start / Type / DisplayName 是否变化。
     * ItemSupport 下显卡/键盘/灯条相关能力位是否变化（若安装程序也写这里）。
  5) 05-LM状态.txt
     * L-Mechrevo.dll 的大小/SHA256/FileVersion 是否被安装程序顺带改动（应当不变）。
     * L-Mechrevo 进程 PID/启动时间是否变化。
     * config.json 的 gcu_*/gpu_*/igpu_only_* 键是否变化。

四、判定口径（安装后是"13 项"还是"13 项 + 旧残留"）
  * 若 01 的文件集 == 06 的 13 个载荷文件（按相对路径），且哈希与 06 一致 → 干净的 40A 安装。
  * 若 01 的文件集 ⊃ 06，且旧文件（不在 06 中的相对路径）仍大量存在 → "13 项 + 旧残留"。
  * 若 01 中该有的 13 项哈希与 06 不一致 → 安装未覆盖成功或装的是别的版本。

五、安全声明
  本快照全程只读：没有修改/删除 C:\Program Files\L-Mechrevo\GCU\ 下任何文件，
  没有写注册表，没有启停服务或进程，没有连接 13688，没有重启，没有打包。
'@
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('')
foreach ($l in ($explain -split "`r?`n")) { [void]$lines.Add($l) }
[void]$lines.Add('')
[void]$lines.Add('================================================================')
[void]$lines.Add('本次采集的异常汇总（各采集项独立 try/catch，异常不影响其它项）')
if ($script:Errors.Count -eq 0) {
    [void]$lines.Add('（无异常）')
} else {
    foreach ($e in $script:Errors) { [void]$lines.Add("  - $e") }
}
Save-Lines -Path (Join-Path $OutDirFull '08-说明.txt') -Lines $lines

# ============================================================
# 09-40A落地核对.tsv  —— payload\40 的 13 个载荷文件 vs 实装文件
# ============================================================
$lines = New-Object System.Collections.ArrayList
foreach ($h in $CommonHeader) { [void]$lines.Add($h) }
[void]$lines.Add('# 目的：逐项核对 40A 的每个载荷文件是否已按内容落地到实际安装路径。')
[void]$lines.Add('# 期望来源: 【内嵌常量表】$Exp40Payload（不再读仓库/payload\40，换台机器也能用）')
[void]$lines.Add('# 实装位置: C:\Program Files\L-Mechrevo\GCU\AiStoneService\<相对路径>')
[void]$lines.Add('# 判定: OK=大小与SHA256都等于期望; 不一致=已装但内容不同; 缺失=实装路径没有该文件')
[void]$lines.Add('# 注意: MyControlCenter\UEFI_Firmware.dll(期望 E4902F55) 与 AiStoneService\UEFI_Firmware.dll(期望 10CCF9BC) 是两份不同的文件，勿混淆')
[void]$lines.Add("相对路径`t期望字节`t期望SHA256`t实装字节`t实装SHA256`t判定")
try {
    $liveRoot = 'C:\Program Files\L-Mechrevo\GCU\AiStoneService'
    $okCount = 0; $diffCount = 0; $missCount = 0
    $expCount = 0
    foreach ($rel in $Exp40Payload.Keys) {
        if ($rel -eq 'README.txt') { continue }   # README 不装到机器上，不参与落地核对
        $ep = ([string]$Exp40Payload[$rel]) -split '\|'
        $eh = [string]$ep[1]
        $elen = [int]$ep[0]
        $expCount++
        $live = Join-Path $liveRoot $rel
        $as = ''; $ah = ''; $verdict = ''
        if (Test-Path -LiteralPath $live) {
            $li = Get-Item -LiteralPath $live
            $as = [string]$li.Length
            try {
                $full = (Get-FileHash -LiteralPath $live -Algorithm SHA256 -ErrorAction Stop).Hash.ToLower()
                # 内嵌表只固化前 16 位，比较时取实装哈希前 16 位，够判定
                $ah = $full.Substring(0, [Math]::Min(16, $full.Length))
            } catch { $ah = 'HASH_FAILED' }
            if ($ah -eq $eh.Substring(0, [Math]::Min(16, $eh.Length)) -and $as -eq [string]$elen) { $verdict = 'OK'; $okCount++ }
            else { $verdict = '不一致'; $diffCount++ }
        } else {
            $as = '-'; $ah = '-'; $verdict = '缺失'; $missCount++
        }
        [void]$lines.Add("$rel`t$elen`t$($eh.Substring(0,[Math]::Min(16,$eh.Length)))`t$as`t$ah`t$verdict")
    }
    [void]$lines.Add("# 小计: 期望载荷文件=$expCount  OK=$okCount  不一致=$diffCount  缺失=$missCount")
    if ($expCount -eq 0) {
        [void]$lines.Add('# 结论: 内嵌期望表为空，无法判定。')
    } elseif ($okCount -eq $expCount) {
        [void]$lines.Add('# 结论: 补全后的 40A 载荷已全部按内容落地。')
    } else {
        [void]$lines.Add('# 结论: 40A 载荷未完全落地，请见上表"不一致/缺失"行。')
    }
} catch {
    Add-Err '09-40A落地核对' $_.Exception.Message
    [void]$lines.Add("# 采集失败: $($_.Exception.Message)")
}
Save-Lines -Path (Join-Path $OutDirFull '09-40A落地核对.tsv') -Lines $lines

# ============================================================
# 控制台汇总
# ============================================================
Write-Host ''
Write-Host "GCU 快照已生成: $OutDirFull" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutDirFull -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}  {1} 字节" -f $_.Name, $_.Length)
}
if ($script:Errors.Count -gt 0) {
    Write-Host '采集异常:' -ForegroundColor Yellow
    foreach ($e in $script:Errors) { Write-Host "  - $e" -ForegroundColor Yellow }
} else {
    Write-Host '采集异常: 无' -ForegroundColor Green
}
exit 0
