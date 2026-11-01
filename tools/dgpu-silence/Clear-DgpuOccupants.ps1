<#
  Clear-DgpuOccupants.ps1  --  READ-ONLY BY DEFAULT

  Why this exists: the vendor console refuses the "iGPU power-saving" switch while the discrete
  GPU is in use ("GPU in use, switching not supported right now"). Its own usage number comes from
  the Windows GPU performance counters (GPUTypeClass)
  / GetGpuUsage_PerfCounter), so the occupants are exactly the processes that hold GPU engine time
  or GPU memory. This script lists them and, only with -Kill, ends them.

  How the discrete GPU is identified without any vendor API: per-process GPU memory counters
  (Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory) are keyed per LUID. Only a
  discrete GPU has a large amount of DEDICATED memory, so the LUID with the biggest dedicated total
  is the dGPU. Measured on a hybrid machine: dGPU 8,023,904,256 B vs iGPU 24,236,032 B vs virtual 0.

  Nothing here is undocumented: it uses CIM performance counters and taskkill only. It never
  touches EC, firmware variables or the ACPI mapper.

  Exit codes: 0 = nothing to do / cleared, 1 = occupants remain, 2 = error, 3 = refused (protected set)
#>
[CmdletBinding()]
param(
    [switch]$List,                 # default: only report
    [switch]$Kill,                 # end the reported occupant processes
    [switch]$IncludeIdle,          # also kill processes that hold dGPU memory but 0% engine time
    [switch]$IncludeDllHolders,    # also kill processes that merely loaded an NVIDIA user-mode DLL
    [switch]$StopNvidiaServices,   # stop NVDisplay.ContainerLocalSystem / NvContainerLocalSystem first
    [switch]$Yes,                  # do not ask for confirmation (for the one-click wrapper)
    [switch]$Wait,                 # poll until no closable occupant is left
    [int]$WaitSeconds = 60,
    [int]$PollSeconds = 3,
    [string]$LogPath,
    [int]$MinDedicatedMB = 1,      # listing threshold
    [int]$MinKillMB = 100,         # -Kill only ends occupants at or above this (use -All to ignore)
    [switch]$All
)

$ErrorActionPreference = 'Continue'
$script:Report = New-Object System.Collections.ArrayList

function Write-Report {
    param([string]$Message = '', [string]$Level = 'INFO')
    $line = if ($Level -eq 'INFO') { $Message } else { "[$Level] $Message" }
    Write-Host $line
    [void]$script:Report.Add($line)
}

# Processes we never end: system/security, the shell and desktop compositor, the vendor console
# itself, our own process tree, and anything running out of %SystemRoot%.
$script:ProtectedNames = @(
    'system', 'idle', 'registry', 'memory compression', 'secure system',
    'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'smss', 'dwm', 'explorer',
    'svchost', 'fontdrvhost', 'sihost', 'taskhostw', 'ctfmon', 'logonui', 'lockapp',
    'searchhost', 'startmenuexperiencehost', 'shellexperiencehost', 'textinputhost',
    'applicationframehost', 'runtimebroker', 'wmiPrvSE', 'audiodg', 'spoolsv',
    'msmpeng', 'nissrv', 'securityhealthservice', 'securityhealthsystray',
    
    'nvdisplay.container', 'nvcontainer', 'nvsphelper64', 'nvsmartmaxcontainer'
)

function Test-Protected {
    param($Process)
    if (-not $Process) { return $true }
    $name = $Process.ProcessName.ToLowerInvariant()
    if ($script:ProtectedNames -contains $name) { return $true }
    if ($Process.Id -eq $PID) { return $true }
    try {
        $parent = (Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $Process.Id) -ErrorAction Stop).ParentProcessId
        if ($parent -eq $PID) { return $true }
    } catch { }
    try {
        $path = $Process.Path
        if ($path -and $path.ToLowerInvariant().StartsWith($env:SystemRoot.ToLowerInvariant())) { return $true }
    } catch { }
    return $false
}

function Get-GpuMemoryRows {
    <# Per-PID, per-LUID GPU memory. Used both to find the dGPU LUID and to score occupants. #>
    $rows = New-Object System.Collections.ArrayList
    try {
        $pm = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -ErrorAction Stop)
    } catch {
        return $rows
    }
    foreach ($x in $pm) {
        if ($x.Name -match '^pid_(\d+)_luid_0x[0-9A-Fa-f]{8}_0x([0-9A-Fa-f]{8})') {
            [void]$rows.Add([pscustomobject]@{
                Pid    = [int]$Matches[1]
                Luid   = $Matches[2].ToUpperInvariant()
                Ded    = [int64]$x.DedicatedUsage
                Shared = [int64]$x.SharedUsage
            })
        }
    }
    return $rows
}

function Get-GpuEngineRows {
    <# Per-PID, per-LUID engine utilisation (the vendor's own source of "is the GPU in use"). #>
    $rows = New-Object System.Collections.ArrayList
    try {
        $eng = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop)
    } catch {
        return $rows
    }
    foreach ($e in $eng) {
        if ($e.Name -match '^pid_(\d+)_luid_0x[0-9A-Fa-f]{8}_0x([0-9A-Fa-f]{8})') {
            [void]$rows.Add([pscustomobject]@{
                Pid  = [int]$Matches[1]
                Luid = $Matches[2].ToUpperInvariant()
                Util = [int]$e.UtilizationPercentage
            })
        }
    }
    return $rows
}

function Get-DgpuLuid {
    param($MemoryRows)
    $totals = @{}
    foreach ($r in $MemoryRows) {
        if (-not $totals.ContainsKey($r.Luid)) { $totals[$r.Luid] = [int64]0 }
        $totals[$r.Luid] += $r.Ded
    }
    $byDed = $totals.GetEnumerator() | Sort-Object Value -Descending
    foreach ($t in $byDed) {
        # first LUID with a non-trivial dedicated pool is the discrete GPU
        if ($t.Value -ge (1MB)) { return [pscustomobject]@{ Luid = $t.Key; Dedicated = $t.Value; All = $byDed } }
    }
    return [pscustomobject]@{ Luid = $null; Dedicated = 0; All = $byDed }
}

function Get-Occupants {
    param([string]$Luid, $MemoryRows, $EngineRows, [switch]$WithDllHolders, [int]$MinDedMB = 1)
    $map = @{}
    foreach ($r in $MemoryRows) {
        if ($r.Luid -ne $Luid) { continue }
        if (-not $map.ContainsKey($r.Pid)) {
            $map[$r.Pid] = [pscustomobject]@{ Pid = $r.Pid; Ded = [int64]0; Shared = [int64]0; Util = 0; Dll = $false }
        }
        $map[$r.Pid].Ded += $r.Ded
        $map[$r.Pid].Shared += $r.Shared
    }
    foreach ($r in $EngineRows) {
        if ($r.Luid -ne $Luid) { continue }
        if (-not $map.ContainsKey($r.Pid)) {
            $map[$r.Pid] = [pscustomobject]@{ Pid = $r.Pid; Ded = [int64]0; Shared = [int64]0; Util = 0; Dll = $false }
        }
        $map[$r.Pid].Util += $r.Util
    }
    $list = New-Object System.Collections.ArrayList
    foreach ($pidKey in $map.Keys) {
        $o = $map[$pidKey]
        $proc = Get-Process -Id $o.Pid -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        $o | Add-Member -NotePropertyName Name -NotePropertyValue $proc.ProcessName -Force
        try { $o | Add-Member -NotePropertyName Path -NotePropertyValue $proc.Path -Force } catch { }
        try { $o | Add-Member -NotePropertyName Started -NotePropertyValue $proc.StartTime -Force } catch { }
        if ($WithDllHolders) {
            $nvDlls = @('nvwgf2umx', 'nvwgf2um', 'nvcuda', 'nvapi64', 'nvoglv64', 'nvldumdx')
            try {
                $hit = $proc.Modules | Where-Object { $m = $_; $nvDlls | Where-Object { $m.ModuleName -like "$_*" } }
                if ($hit) { $o.Dll = $true }
            } catch { }
        }
        $o | Add-Member -NotePropertyName Protected -NotePropertyValue (Test-Protected -Process $proc) -Force
        [void]$list.Add($o)
    }
    return $list
}

function Get-NvidiaDllHolders {
    $nvDlls = @('nvwgf2umx', 'nvwgf2um', 'nvcuda', 'nvapi64', 'nvoglv64', 'nvldumdx')
    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        try {
            $hit = $p.Modules | Where-Object { $m = $_; $nvDlls | Where-Object { $m.ModuleName -like "$_*" } }
            if ($hit) { $p }
        } catch { }
    }
}

function Stop-NvidiaService {
    <# Same pre-step the vendor performs before switching: stop the display container. #>
    $names = @('NVDisplay.ContainerLocalSystem', 'NvContainerLocalSystem', 'NVDisplay.ContainerUser', 'NvContainerUser')
    foreach ($n in $names) {
        $svc = Get-Service -Name $n -ErrorAction SilentlyContinue
        if (-not $svc) { continue }
        if ($svc.Status -eq 'Stopped') { Write-Report ("    {0,-32} already stopped" -f $n); continue }
        try {
            Stop-Service -Name $n -Force -ErrorAction Stop
            Write-Report -Level OK -Message ("    {0,-32} stopped" -f $n)
        } catch {
            $null = & net.exe stop $n 2>&1
            $now = (Get-Service -Name $n -ErrorAction SilentlyContinue).Status
            if ($now -eq 'Stopped') { Write-Report -Level OK -Message ("    {0,-32} stopped (net stop)" -f $n) }
            else { Write-Report -Level WARN -Message ("    {0,-32} could not stop ({1})" -f $n, $_.Exception.Message) }
        }
    }
}

function Kill-Process {
    param($Occupant)
    $label = ("{0} (pid {1}, {2:N0} MB)" -f $Occupant.Name, $Occupant.Pid, ($Occupant.Ded / 1MB))
    if ($Occupant.Protected) {
        Write-Report ("    SKIP {0} - protected" -f $label)
        return $false
    }
    $out = & taskkill.exe /T /F /PID $Occupant.Pid 2>&1
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        Write-Report -Level OK -Message ("    ended {0}" -f $label)
        return $true
    }
    Write-Report -Level WARN -Message ("    could not end {0}: {1}" -f $label, (($out | Out-String).Trim()))
    return $false
}

# ---------------------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------------------
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Report '========================================================='
Write-Report ' clear dGPU occupants (read-only unless -Kill is given)'
Write-Report '========================================================='
Write-Report ("host={0}  user={1}  elevated={2}" -f $env:COMPUTERNAME, $identity.Name, $isAdmin)
Write-Report 'source: Win32_PerfFormattedData_GPUPerformanceCounters_* (the same Windows counters reads)'
if ($LogPath) { Write-Report ("log file     : {0}" -f $LogPath) }
Write-Report ''

$memRows = Get-GpuMemoryRows
$engRows = Get-GpuEngineRows
if ($memRows.Count -eq 0) {
    Write-Report -Level WARN -Message 'GPU performance counters are unavailable - cannot identify occupants.'
    Write-Report 'If the GPU was just disabled or the driver is not loaded, this can be normal.'
    if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
    exit 2
}
$dgpu = Get-DgpuLuid -MemoryRows $memRows
Write-Report 'adapters seen (per-LUID dedicated memory; the discrete GPU is the largest pool):'
foreach ($t in $dgpu.All) {
    $mark = ''
    if ($t.Key -eq $dgpu.Luid) { $mark = '   <== treating this as the discrete GPU' }
    Write-Report ("    luid 0x{0}  dedicated={1,15:N0} B{2}" -f $t.Key, $t.Value, $mark)
}
if (-not $dgpu.Luid) {
    Write-Report -Level WARN -Message 'no adapter exposes a dedicated memory pool; cannot tell the dGPU from the iGPU.'
    if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
    exit 2
}
Write-Report ''

$occupants = Get-Occupants -Luid $dgpu.Luid -MemoryRows $memRows -EngineRows $engRows -WithDllHolders -MinDedMB $MinDedicatedMB
$scored = @($occupants | Where-Object { ($_.Ded -ge ($MinDedicatedMB * 1MB)) -or ($_.Util -gt 0) } | Sort-Object Ded -Descending)
# A real function, not a scriptblock: Where-Object passes the item as $_ while an explicit call
# passes it as the first argument, and one scriptblock cannot serve both shapes correctly.
function Test-HeavyOccupant { param($o) return (($o.Ded -ge ($MinKillMB * 1MB)) -or ($o.Util -gt 0)) }
$closable = @($scored | Where-Object { -not $_.Protected })
$heavy = @($closable | Where-Object { Test-HeavyOccupant $_ })
$light = @($closable | Where-Object { -not (Test-HeavyOccupant $_) })
$held = @($scored | Where-Object { $_.Protected })

Write-Report ("processes holding the discrete GPU: {0}  (closable {1} = heavy {2} + light {3} ; protected {4})" -f $scored.Count, $closable.Count, $heavy.Count, $light.Count, $held.Count)
Write-Report ''
Write-Report ("  {0,-8} {1,-26} {2,9} {3,6} {4,7}  {5}" -f 'pid', 'process', 'ded(MB)', 'util%', 'status', 'path')
foreach ($o in $scored) {
    $status = 'light'
    if ($o.Protected) { $status = 'PROTECTED' }
    elseif (Test-HeavyOccupant $o) { $status = 'HEAVY' }
    $path = ''
    if ($o.PSObject.Properties.Name -contains 'Path') { $path = [string]$o.Path }
    Write-Report ("  {0,-8} {1,-26} {2,9:N0} {3,6} {4,7}  {5}" -f $o.Pid, $o.Name, ($o.Ded / 1MB), $o.Util, $status, $path)
}
if ($held.Count -gt 0) {
    Write-Report ''
    Write-Report 'protected entries are the desktop/shell/security stack or the vendor console: do not kill them.'
}
if ($light.Count -gt 0) {
    Write-Report ''
    Write-Report ("light entries (<{0} MB and 0% engine time) usually do NOT block the vendor switch; -Kill leaves them alone unless -All is given." -f $MinKillMB)
}
Write-Report ''

if (-not $Kill) {
    $dllHolders = @()
    if ($IncludeDllHolders) {
        $dllHolders = @(Get-NvidiaDllHolders | Where-Object { -not (Test-Protected -Process $_) })
    }
    Write-Report 'read-only run: nothing was ended. Re-run with -Kill to end the closable processes above.'
    if ($closable.Count -eq 0) {
        Write-Report -Level OK -Message 'no closable occupant found - the vendor switch should be allowed now.'
        if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
        exit 0
    }
    if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
    exit 1
}

if ($StopNvidiaServices) {
    Write-Report 'stopping the NVIDIA display container (the vendor does this too before switching):'
    Stop-NvidiaService
    Write-Report ''
    Start-Sleep -Seconds 2
    $memRows = Get-GpuMemoryRows
    $engRows = Get-GpuEngineRows
    $occupants = Get-Occupants -Luid $dgpu.Luid -MemoryRows $memRows -EngineRows $engRows -WithDllHolders -MinDedMB $MinDedicatedMB
    $scored = @($occupants | Where-Object { ($_.Ded -ge ($MinDedicatedMB * 1MB)) -or ($_.Util -gt 0) } | Sort-Object Ded -Descending)
    $closable = @($scored | Where-Object { -not $_.Protected })
    $heavy = @($closable | Where-Object { Test-HeavyOccupant $_ })
    $light = @($closable | Where-Object { -not (Test-HeavyOccupant $_) })
}

# Which tier do we act on? By default only the heavy occupants, because the light ones (a few MB,
# no engine time) normally do not block the vendor switch - and ending 19 processes to free the GPU
# is far more collateral than a non-technical operator expects.
$target = $heavy
$tierName = ("heavy (>= {0} MB or using the GPU)" -f $MinKillMB)
if ($All) {
    $target = $closable
    $tierName = 'ALL closable'
}
if ($closable.Count -eq 0) {
    Write-Report -Level OK -Message 'nothing closable left to end.'
} else {
    Write-Report ("about to force-end the {0} tier: {1} process(es) - unsaved work in them will be lost:" -f $tierName, $target.Count)
    foreach ($o in $target) { Write-Report ("    {0} (pid {1}, {2:N0} MB)" -f $o.Name, $o.Pid, ($o.Ded / 1MB)) }
    if ($light.Count -gt 0 -and -not $All) {
        Write-Report ("    (leaving {0} light occupant(s) alone; add -All to end them too)" -f $light.Count)
    }
    if ($target.Count -eq 0) {
        Write-Report 'nothing in the heavy tier; use -All if the vendor still refuses.'
    } else {
        if (-not $Yes) {
            $answer = Read-Host 'Type Y to end them, anything else to abort'
            if ($answer -notmatch '^[Yy]') {
                Write-Report -Level WARN -Message 'aborted by the operator; nothing was ended.'
                if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
                exit 1
            }
        }
        Write-Report ''
        $ended = 0
        foreach ($o in $target) { if (Kill-Process -Occupant $o) { $ended++ } }
        Write-Report ''
        Write-Report ("ended {0} of {1} process(es)." -f $ended, $target.Count)
    }
}

if ($IncludeDllHolders -and $Kill) {
    $holders = @(Get-NvidiaDllHolders | Where-Object { -not (Test-Protected -Process $_) })
    if ($holders.Count -gt 0) {
        Write-Report ''
        Write-Report ("also ending {0} process(es) that merely hold an NVIDIA driver DLL:" -f $holders.Count)
        foreach ($h in $holders) {
            $lbl = ("{0} (pid {1})" -f $h.ProcessName, $h.Id)
            $out = & taskkill.exe /T /F /PID $h.Id 2>&1
            if ($LASTEXITCODE -eq 0) { Write-Report -Level OK -Message ("    ended {0}" -f $lbl) }
            else { Write-Report -Level WARN -Message ("    could not end {0}" -f $lbl) }
        }
    }
}

if ($Wait) {
    Write-Report ''
    Write-Report ("waiting up to {0}s for the discrete GPU to go quiet..." -f $WaitSeconds)
    $waited = 0
    $remaining = 1
    while ($waited -lt $WaitSeconds) {
        Start-Sleep -Seconds $PollSeconds
        $waited += $PollSeconds
        $memRows = Get-GpuMemoryRows
        $engRows = Get-GpuEngineRows
        $occ = Get-Occupants -Luid $dgpu.Luid -MemoryRows $memRows -EngineRows $engRows -MinDedMB $MinDedicatedMB
        $remaining = @($occ | Where-Object {
            if ($_.Protected) { return $false }
            if ($All) { return (($_.Ded -ge ($MinDedicatedMB * 1MB)) -or ($_.Util -gt 0)) }
            return (($_.Ded -ge ($MinKillMB * 1MB)) -or ($_.Util -gt 0))
        }).Count
        if ($remaining -eq 0) { break }
    }
    if ($remaining -eq 0) {
        Write-Report -Level OK -Message ("discrete GPU is quiet after {0}s - the vendor switch should be allowed now." -f $waited)
    } else {
        Write-Report -Level WARN -Message ("{0} closable occupant(s) still hold the dGPU after {1}s." -f $remaining, $waited)
    }
}

Write-Report ''
$final = Get-Occupants -Luid $dgpu.Luid -MemoryRows (Get-GpuMemoryRows) -EngineRows (Get-GpuEngineRows) -MinDedMB $MinDedicatedMB
$finalClosable = @($final | Where-Object {
    if ($_.Protected) { return $false }
    if ($All) { return (($_.Ded -ge ($MinDedicatedMB * 1MB)) -or ($_.Util -gt 0)) }
    return (($_.Ded -ge ($MinKillMB * 1MB)) -or ($_.Util -gt 0))
})
$finalLight = @($final | Where-Object {
    (-not $_.Protected) -and ($_.Ded -lt ($MinKillMB * 1MB)) -and ($_.Util -le 0) -and ($_.Ded -ge ($MinDedicatedMB * 1MB))
})
Write-Report ("remaining on the discrete GPU (heavy tier, >= {0} MB or using it):" -f $MinKillMB)
if ($finalClosable.Count -eq 0) {
    Write-Report -Level OK -Message '    none - go back to the control centre and switch to the iGPU mode now.'
} else {
    foreach ($o in ($finalClosable | Sort-Object Ded -Descending)) {
        Write-Report ("    {0} (pid {1}, {2:N0} MB, util {3}%)" -f $o.Name, $o.Pid, ($o.Ded / 1MB), $o.Util)
    }
}
if ($finalLight.Count -gt 0) {
    Write-Report ("    plus {0} light occupant(s) below {1} MB with no engine time - normally harmless." -f $finalLight.Count, $MinKillMB)
}
Write-Report 'note: if the vendor still refuses, add -All, and if it still refuses, a reboot clears every GPU user for certain.'

if ($LogPath) {
    try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { }
    Write-Host ("report written to {0}" -f $LogPath)
}
if ($finalClosable.Count -eq 0) { exit 0 }
exit 1
