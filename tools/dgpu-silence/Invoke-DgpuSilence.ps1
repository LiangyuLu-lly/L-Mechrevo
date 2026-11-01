<#
.SYNOPSIS
    Standalone NVIDIA dGPU "silence" script: disable / enable the discrete GPU device.
    Does NOT use any vendor control-center service or background agent.

.DESCRIPTION
    Talks to the PnP device directly (the standard Windows PnP command set /
    laptop dGPU disable), but adds everything a one-liner lacks: elevation check,
    idempotency, display-topology gate, occupancy detection and cleanup, fallback
    paths, result verification, automatic rollback on a broken display path, logging,
    exit codes and a recovery entry point.

    Three different things are often confused; this script only does the first:
      1. Disable the device      - PnP level, the device disappears.  <- this script
      2. Silence the dGPU        - hybrid mode, let the dGPU sleep (vendor power-saving
                                   mode, executed by the embedded controller).
      3. Change the display route- MUX / firmware level, usually needs a reboot.
                                   This script neither does it nor can do it.

    Key fact: Disable-PnpDevice is PERSISTENT (still disabled after a reboot), while
    pnputil /remove-device is NOT (the device comes back after a rescan or reboot).

    NOTE ON ENCODING - keep this file ASCII-only, do not add non-ASCII text:
    Windows PowerShell 5.1 reads a BOM-less .ps1 using the ANSI code page (GBK on a
    zh-CN system). Non-ASCII characters then decode as mojibake, which at best makes
    messages unreadable and at worst breaks parsing outright. Verified: this same
    script with Chinese text and no BOM yielded 42 parse errors under PS 5.1.
    The original one-line scripts are ASCII-only for the same reason.

.PARAMETER Action
    Status (read-only) | Disable | Enable.
    When -Action is omitted the script opens an interactive menu instead, and asks for
    administrator rights first (the menu can toggle the device). Pass -Action Status
    explicitly for the non-interactive, read-only default.

.PARAMETER Interactive
    Force the interactive menu even when -Action is given.

.PARAMETER NoElevate
    Never relaunch through UAC. The menu then runs unelevated and its Disable/Enable
    choices fail with exit code 2 ("requires administrator rights"). For unattended runs.

.PARAMETER Elevated
    Internal marker set on the instance relaunched after the UAC prompt; prevents an
    endless elevation loop. Not needed for manual use.

.PARAMETER Force
    On Disable, allow clearing occupancy: request a graceful window close first, then
    kill the process tree. Processes on the protected list are never touched.
    Without -Force, any "closable" occupant makes the script refuse (exit code 3).

.PARAMETER AllowSurpriseRemove
    Permit pnputil /remove-device as a LAST RESORT when the plain disable fails (surprise
    removal; programs using the dGPU are interrupted at once). Requires -Force as well.
    For an unconditional removal, use -RemoveDevice instead.

.PARAMETER RemoveDevice
    After disabling, remove the device node unconditionally (pnputil /remove-device), so the
    device is gone from Device Manager instead of sitting there with a down arrow. Disabling
    first is deliberate: the disabled state is what survives a reboot, while a removed node
    is re-enumerated by the PCI bus. Works on already-disabled devices too.

.PARAMETER SkipDisplayCheck
    Skip the "is a monitor attached to the dGPU" gate. Dangerous: on a dGPU-direct /
    MUX machine, with an external monitor wired to the dGPU, or on a model without an
    iGPU, this blacks the screen out. Note that when the display topology cannot be read
    at all, Disable refuses by default (unknown is never treated as safe); this switch
    also overrides that refusal.

.PARAMETER IncludeOtherSessions
    Occupancy detection and cleanup normally only look at the current session; this
    switch widens them to all sessions.

.PARAMETER DryRun
    Evaluate the gates and print what would happen, changing nothing. Recommended for
    the first real-machine run of Disable.

.PARAMETER SkipServiceStop
    Do not stop the NVIDIA container services (NVDisplay.ContainerLocalSystem /
    NVContainerLocalSystem) before disabling, and do not (re)start them after enabling.
    Stopping them mirrors the vendor's own sequence before it changes the dGPU state:
    the containers release their dGPU handles, which reduces handle contention on
    Disable-PnpDevice / remove-device. It is best-effort - a failure is only logged and
    the run continues. Enable starts them again either way.

.PARAMETER LogPath
    Log file path. Default: %ProgramData%\DgpuSilence\logs\dgpu-silence_<timestamp>.log

.EXAMPLE
    # No arguments: UAC prompt, then an interactive menu (status / disable / enable)
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-DgpuSilence.ps1

.EXAMPLE
    # Read-only health check: devices + display topology + occupancy + gate verdict
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-DgpuSilence.ps1 -Action Status

.EXAMPLE
    # Rehearsal of the disable path (changes nothing)
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-DgpuSilence.ps1 -Action Disable -DryRun

.EXAMPLE
    # Real disable: clear occupancy, then disable
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-DgpuSilence.ps1 -Action Disable -Force

.EXAMPLE
    # Bring it back
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-DgpuSilence.ps1 -Action Enable

.NOTES
    Exit codes: 0 success / already in target state; 1 unexpected error;
                2 prerequisite gate refused; 3 occupants present without -Force;
                4 disable failed; 5 enable failed.
    Elevation is required for Disable/Enable, not for Status.
    After a successful disable a restore-dgpu.cmd is written next to the log; keep it.
#>
[CmdletBinding()]
param(
    [ValidateSet('Status', 'Disable', 'Enable')]
    [string]$Action = 'Status',

    [switch]$Force,
    [switch]$AllowSurpriseRemove,
    [switch]$RemoveDevice,
    [switch]$SkipDisplayCheck,
    [switch]$IncludeOtherSessions,
    [switch]$DryRun,
    [string]$LogPath,

    [switch]$Interactive,
    [switch]$NoElevate,
    [switch]$SkipServiceStop,
    [switch]$Elevated
)

# Exit codes
$script:ExitOk = 0
$script:ExitError = 1
$script:ExitGate = 2
$script:ExitOccupied = 3
$script:ExitDisableFailed = 4
$script:ExitEnableFailed = 5

# Never closed / killed. Counts as "held by the system": reported, but not blocking.
$script:ProtectedProcessNames = @(
    'system', 'idle', 'registry', 'memory compression', 'secure system', 'smss', 'csrss', 'wininit',
    'services', 'lsass', 'winlogon', 'dwm', 'fontdrvhost', 'svchost', 'explorer', 'taskhostw', 'sihost',
    'runtimebroker', 'shellexperiencehost', 'searchhost', 'startmenuexperiencehost', 'textinputhost',
    'applicationframehost', 'systemsettings', 'dllhost', 'conhost', 'openconsole', 'audiodg', 'spoolsv',
    'wlanext', 'msdtc', 'ctfloader', 'igfxem', 'igfxhk', 'igfxext', 'nvcontainer', 'nvdisplay.container',
    'nvsettings', 'nvspcaps64', 'nvsphelper64', 'nvwmi64', 'nvcplui', 'nvidia-smi', 'nvidia-smi.exe',
    'atieclxx', 'atiesrxx',
    'powershell', 'pwsh', 'windowsterminal', 'cmd', 'taskkill', 'reg', 'wmiprvse', 'msmpeng',
    'lockapp', 'shellhost', 'shellexperiencehost', 'startmenuexperiencehost', 'searchhost',
    'textinputhost', 'applicationframehost', 'explorer', 'dwm', 'winlogon', 'logonui', 'userinit'
)
$script:ProtectedNamePrefixes = @()   # service-owned processes are protected dynamically

# NVIDIA user-mode driver modules: a process that loaded one of these has the dGPU open.
$script:NvidiaUmdPattern = '^(nvwgf2umx?|nvd3dumx?|nvcuda|nvml|nvapi64|nvoglv64|nvopencl64|nvfbc|nvencodeapi64)\.dll$'

$script:PnpAvailable = $true
$script:NvidiaVendorIdPattern = 'VEN_10DE'

# The vendor's own service stops exactly these two containers before it touches the dGPU
# state (MySettingManager.SetNVServiceOnOff -> "net stop NVDisplay.ContainerLocalSystem" /
# "NVContainerLocalSystem"), so that the NVIDIA containers release their dGPU handles.
$script:NvidiaServiceNames = @('NVDisplay.ContainerLocalSystem', 'NVContainerLocalSystem')

# =====================================================================================
# Logging
# =====================================================================================
if (-not $LogPath) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $base = Join-Path $env:ProgramData 'DgpuSilence\logs'
    try {
        if (-not (Test-Path $base)) { New-Item -ItemType Directory -Path $base -Force | Out-Null }
    } catch {
        $base = Join-Path $env:TEMP 'DgpuSilence\logs'
        if (-not (Test-Path $base)) { New-Item -ItemType Directory -Path $base -Force | Out-Null }
    }
    $LogPath = Join-Path $base ("dgpu-silence_{0}.log" -f $stamp)
}
$script:LogFile = $LogPath
$script:LogDir = Split-Path -Parent $LogPath
if ($script:LogDir -and -not (Test-Path $script:LogDir)) {
    try { New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null } catch { }
}

function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'CRITICAL', 'STEP')][string]$Level = 'INFO'
    )
    $line = "{0} [{1,-8}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'), $Level, $Message
    switch ($Level) {
        'WARN'     { Write-Host $line -ForegroundColor Yellow }
        'ERROR'    { Write-Host $line -ForegroundColor Red }
        'CRITICAL' { Write-Host $line -ForegroundColor Red }
        'STEP'     { Write-Host $line -ForegroundColor Cyan }
        default    { Write-Host $line }
    }
    try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

function Fail {
    param([int]$Code, [string]$Message)
    Write-Log -Level ERROR -Message $Message
    Write-Log -Level ERROR -Message ("exit code {0}; log: {1}" -f $Code, $script:LogFile)
    exit $Code
}

# =====================================================================================
# Generic helpers
# =====================================================================================
function Test-Elevated {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @()
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if (-not (Test-Path -LiteralPath $FilePath)) {
            return [pscustomobject]@{ ExitCode = -1; Output = ("not found: {0}" -f $FilePath) }
        }
        $out = & $FilePath @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
        return [pscustomobject]@{ ExitCode = $code; Output = $out.Trim() }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Test-ProtectedProcess {
    <#
      Decides whether a process must never be closed or killed, and therefore counts as
      "held by the system" instead of a closable occupant.
      Two independent rules, on purpose:
        1. executable path under %SystemRoot% -> never touch it. This catches explorer,
           LockApp, SearchHost, ShellExperienceHost, StartMenuExperienceHost, ... WITHOUT
           depending on a name list staying up to date.
        2. a name list/prefix list, for the NVIDIA/OEM helpers that live elsewhere.
      The name is normalised first: nvidia-smi reports full paths, so names arrive as
      "explorer.exe" while the list holds "explorer" - that mismatch is exactly how a
      -Force run could have killed the user's shell (found on a real machine).
    #>
    param([string]$Name, [string]$Path = '', [int]$ProcessId = 0)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $true }

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $root = $env:SystemRoot
        if ($root -and $Path.ToLowerInvariant().StartsWith($root.ToLowerInvariant())) { return $true }
    }

    $lower = $Name.ToLowerInvariant()
    if ($lower.EndsWith('.exe')) { $lower = $lower.Substring(0, $lower.Length - 4) }
    if ($script:ProtectedProcessNames -contains $lower) { return $true }
    foreach ($prefix in $script:ProtectedNamePrefixes) {
        if ($lower.StartsWith($prefix)) { return $true }
    }
    # Vendor-neutral: any process owned by a registered Windows service is left alone.
    if ($ProcessId -gt 0) {
        try {
            $svc = Get-CimInstance Win32_Service -Filter ('ProcessId=' + $ProcessId) -ErrorAction Stop
            if ($svc) { return $true }
        } catch { }
    }
    return $false
}

# =====================================================================================
# PnP devices (single dependency: Get-PnpDevice, a CIM/WMI-backed cmdlet)
# =====================================================================================
function Get-DisplayClassDevice {
    <#
      Returns every Display-class device, or $null when enumeration failed.
      The comma wrapping matters: an empty array must stay an empty array instead of
      being collapsed to $null by PowerShell, because $null here means "enumeration
      failed" and the two cases lead to opposite decisions.
    #>
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        return ,@(Get-PnpDevice -Class Display -ErrorAction Stop)
    } catch {
        $script:PnpAvailable = $false
        Write-Log -Level WARN -Message ("Get-PnpDevice unavailable (WMI/CIM denied or broken): {0}" -f $_.Exception.Message)
        return $null
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Test-NvidiaInstance {
    param([string]$InstanceId)
    return ($InstanceId -match $script:NvidiaVendorIdPattern)
}

function Get-NvidiaDisplayDevice {
    <# $State: All / Enabled / NotEnabled #>
    param([ValidateSet('All', 'Enabled', 'NotEnabled')][string]$State = 'All')

    $all = Get-DisplayClassDevice
    if ($null -eq $all) { return $null }

    $nvidia = @($all | Where-Object {
        (Test-NvidiaInstance -InstanceId $_.InstanceId) -or ($_.FriendlyName -match 'NVIDIA')
    })
    switch ($State) {
        'Enabled'    { return ,@($nvidia | Where-Object { $_.Status -eq 'OK' }) }
        'NotEnabled' { return ,@($nvidia | Where-Object { $_.Status -ne 'OK' }) }
        default      { return ,$nvidia }
    }
}

function Get-NvidiaProblemCode {
    param([string]$InstanceId)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        $prop = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop
        if ($null -ne $prop -and $null -ne $prop.Data) { return [int]$prop.Data }
        return $null
    } catch {
        return $null
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Test-AnyNonNvidiaDisplayAdapter {
    <# Fallback adapter: at least one present + enabled non-NVIDIA display adapter.
       Without it, disabling the dGPU could leave the machine with no display at all. #>
    $all = Get-DisplayClassDevice
    if ($null -eq $all) { return $null }
    $others = @($all | Where-Object {
        -not (Test-NvidiaInstance -InstanceId $_.InstanceId) -and ($_.Status -eq 'OK')
    })
    return ($others.Count -gt 0)
}

# =====================================================================================
# Display topology (EnumDisplayDevices; no WMI)
# =====================================================================================
if (-not ('DgpuSilenceDisplayApi' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DgpuSilenceDisplayDevice
{
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]  public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
    public int StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
}

public static class DgpuSilenceDisplayApi
{
    // lpDevice is IntPtr on purpose. Declaring it as string and passing $null does NOT
    // work from PowerShell: $null is coerced to "" for a string parameter, and
    // EnumDisplayDevices("") fails immediately with no adapter at all (verified).
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevices")]
    public static extern bool ByIntPtr(IntPtr lpDevice, uint iDevNum,
        ref DgpuSilenceDisplayDevice lpDisplayDevice, uint dwFlags);

    // Used for the per-adapter monitor enumeration, where a real device name is passed.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevices")]
    public static extern bool ByName(string lpDevice, uint iDevNum,
        ref DgpuSilenceDisplayDevice lpDisplayDevice, uint dwFlags);
}
'@
}

function New-DisplayDeviceStruct {
    $d = New-Object DgpuSilenceDisplayDevice
    $d.cb = [System.Runtime.InteropServices.Marshal]::SizeOf($d)
    return $d
}

function Get-DisplayTopology {
    <#
      Per display adapter: AttachedToDesktop / Primary / Mirrored / IsNvidia / Monitors[].
      Monitors are enumerated with EDD_GET_DEVICE_INTERFACE_NAME, so it is visible which
      adapter each monitor hangs off. Vendor-neutral equivalent of the firmware's
      screen-count / HDMI-monitor-count / signal-source guard.
    #>
    $adapters = New-Object System.Collections.ArrayList
    $index = 0
    while ($true) {
        $adapter = New-DisplayDeviceStruct
        if (-not [DgpuSilenceDisplayApi]::ByIntPtr([IntPtr]::Zero, [uint32]$index, [ref]$adapter, [uint32]0)) { break }

        $monitors = New-Object System.Collections.ArrayList
        $monitorIndex = 0
        while ($true) {
            $monitor = New-DisplayDeviceStruct
            if (-not [DgpuSilenceDisplayApi]::ByName($adapter.DeviceName, [uint32]$monitorIndex, [ref]$monitor, [uint32]1)) { break }
            [void]$monitors.Add([pscustomobject]@{
                Name   = $monitor.DeviceString
                Active = (($monitor.StateFlags -band 0x1) -ne 0)
                Device = $monitor.DeviceID
            })
            $monitorIndex++
        }

        [void]$adapters.Add([pscustomobject]@{
            DeviceName        = $adapter.DeviceName
            Description       = $adapter.DeviceString
            DeviceId          = $adapter.DeviceID
            IsNvidia          = ($adapter.DeviceID -match 'VEN_10DE') -or ($adapter.DeviceString -match 'NVIDIA')
            AttachedToDesktop = (($adapter.StateFlags -band 0x1) -ne 0)
            Primary           = (($adapter.StateFlags -band 0x4) -ne 0)
            Mirrored          = (($adapter.StateFlags -band 0x8) -ne 0)
            Monitors          = $monitors.ToArray()
        })
        $index++
    }
    return ,$adapters.ToArray()
}

function Show-DisplayTopology {
    <# One physical GPU shows up as several \\.\DISPLAYn entries, so entries are grouped
       by device ID; only attached entries list their monitors. #>
    param($Topology)
    if (-not $Topology -or $Topology.Count -eq 0) {
        Write-Log -Level WARN -Message 'Display topology: EnumDisplayDevices returned no adapter at all (abnormal).'
        return
    }
    foreach ($group in ($Topology | Group-Object -Property DeviceId)) {
        $entries = @($group.Group)
        $first = $entries[0]
        $names = ($entries | ForEach-Object { $_.DeviceName }) -join ', '
        $tags = @()
        if ($first.IsNvidia) { $tags += 'NVIDIA' }
        if (@($entries | Where-Object { $_.AttachedToDesktop }).Count -gt 0) { $tags += 'attached-to-desktop' }
        if (@($entries | Where-Object { $_.Primary }).Count -gt 0) { $tags += 'primary' }
        if (@($entries | Where-Object { $_.Mirrored }).Count -gt 0) { $tags += 'mirroring' }
        $tagText = ''
        if ($tags.Count -gt 0) { $tagText = ' (' + ($tags -join ', ') + ')' }
        Write-Log -Message ("  {0}{1}`n      entries: {2}" -f $first.Description, $tagText, $names)
        foreach ($m in @($entries | ForEach-Object { $_.Monitors })) {
            Write-Log -Message ("      monitor: {0} active={1}" -f $m.Name, $m.Active)
        }
    }
}

function Test-DgpuDrivesDisplay {
    <#
      Is the dGPU currently driving any monitor (= would black out when disabled)?
      A single physical GPU shows up as several \\.\DISPLAYn entries, so results are
      grouped by device ID to keep the reasons readable.
      Known=$false means the topology could not be read at all - callers must treat that
      as "unknown", never as "safe".
    #>
    param($Topology)
    if (-not $Topology -or $Topology.Count -eq 0) {
        return [pscustomobject]@{
            Known   = $false
            Drives  = $false
            Reasons = @('display topology is unavailable (EnumDisplayDevices returned no adapter)')
        }
    }
    $reasons = @()
    $nvidia = @($Topology | Where-Object { $_.IsNvidia })
    foreach ($group in ($nvidia | Group-Object -Property DeviceId)) {
        $entries = @($group.Group)
        $desc = ($entries | Select-Object -First 1).Description
        $attached = @($entries | Where-Object { $_.AttachedToDesktop })
        $activeMonitors = @($entries | ForEach-Object { $_.Monitors } | Where-Object { $_.Active })
        if ($attached.Count -gt 0) { $reasons += ("NVIDIA adapter '{0}' is flagged attached-to-desktop" -f $desc) }
        if ($activeMonitors.Count -gt 0) { $reasons += ("NVIDIA adapter '{0}' has {1} active monitor(s) attached" -f $desc, $activeMonitors.Count) }
    }
    return [pscustomobject]@{ Known = $true; Drives = ($reasons.Count -gt 0); Reasons = $reasons }
}

function Test-DesktopHasAdapter {
    <# $null means "cannot tell" (no topology data) - not "no display". #>
    param($Topology)
    if (-not $Topology -or $Topology.Count -eq 0) { return $null }
    $attached = @($Topology | Where-Object { $_.AttachedToDesktop -and -not $_.Mirrored })
    return ($attached.Count -gt 0)
}

# =====================================================================================
# Occupancy detection / cleanup
# =====================================================================================
function Get-DgpuOccupant {
    <#
      Two merged sources:
        1) nvidia-smi --query-compute-apps  (CUDA / compute; authoritative)
        2) process module scan: any process with nvwgf2umx.dll / nvd3dumx.dll / nvcuda.dll
           / nvapi64.dll loaded (D3D / OpenGL / video decode and so on)
      Returns Closable (not on the protected list) / SystemHeld (reported only) /
      Uninspected (non-protected processes whose module list could not be read).
    #>
    param([switch]$IncludeOtherSessions)

    $ownProcess = Get-Process -Id $PID -ErrorAction SilentlyContinue
    $ownSession = $null
    if ($null -ne $ownProcess) { $ownSession = $ownProcess.SessionId }

    $closable = New-Object System.Collections.ArrayList
    $systemHeld = New-Object System.Collections.ArrayList
    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $uninspected = 0

    function Add-Occupant {
        # The parameter must NOT be named $Pid: $PID is a read-only automatic variable
        # and binding to it throws. -Path matters: nvidia-smi reports full paths and the
        # path-based protection rule is what keeps the Windows shell out of the kill list.
        param($OccupantPid, $Name, $Source, $HasWindow, $Path = '')
        if (-not $seen.Add([int]$OccupantPid)) { return }
        $item = [pscustomobject]@{
            ProcessId = [int]$OccupantPid
            Name      = $Name
            Source    = $Source
            HasWindow = [bool]$HasWindow
            Path      = $Path
        }
        if (Test-ProtectedProcess -Name $Name -Path $Path) { [void]$systemHeld.Add($item) } else { [void]$closable.Add($item) }
    }

    # 1) nvidia-smi compute processes
    $smiPath = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
    if (Test-Path -LiteralPath $smiPath) {
        $r = Invoke-Native -FilePath $smiPath -Arguments @('--query-compute-apps=pid,process_name', '--format=csv,noheader')
        if ($r.ExitCode -eq 0) {
            # exit 0 with empty output means "no compute process is using the dGPU", which is
            # a perfectly normal result, not a failure to warn about.
            foreach ($line in ($r.Output -split "`r?`n")) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                $parts = $line.Split(',')
                if ($parts.Count -lt 2) { continue }
                $pidText = $parts[0].Trim()
                $name = $parts[1].Trim()
                if ($pidText -match '^\d+$') {
                    Add-Occupant -OccupantPid ([int]$pidText) -Name ([System.IO.Path]::GetFileName($name)) -Source 'nvidia-smi:compute' -HasWindow $false -Path $name
                }
            }
        } else {
            Write-Log -Level WARN -Message ("nvidia-smi compute-app query failed (exit {0}): {1}" -f $r.ExitCode, $r.Output)
            Write-Log -Message '  (exit 9 = "could not communicate with the NVIDIA driver" is expected while the dGPU is disabled.)'
        }
    }

    # 2) module scan
    $processes = @(Get-Process -ErrorAction SilentlyContinue)
    foreach ($p in $processes) {
        if ($p.Id -le 4 -or $p.Id -eq $PID) { continue }
        if ($seen.Contains($p.Id)) { continue }
        if (-not $IncludeOtherSessions) {
            if ($null -ne $ownSession -and $p.SessionId -ne $ownSession) { continue }
        }
        $isProtected = Test-ProtectedProcess -Name $p.ProcessName -ProcessId $p.Id
        $modules = $null
        try { $modules = $p.Modules } catch { $modules = $null }
        if ($null -eq $modules) {
            if (-not $isProtected) { $uninspected++ }
            continue
        }
        $hit = $null
        foreach ($m in $modules) {
            if ($m.ModuleName -match $script:NvidiaUmdPattern) { $hit = $m.ModuleName; break }
        }
        if (-not $hit) { continue }
        $hasWindow = $false
        try { $hasWindow = ($p.MainWindowHandle -ne [IntPtr]::Zero) } catch { $hasWindow = $false }
        $procPath = ''
        try { $procPath = $p.Path } catch { $procPath = '' }
        Add-Occupant -OccupantPid $p.Id -Name $p.ProcessName -Source ("module:{0}" -f $hit) -HasWindow $hasWindow -Path $procPath
    }

    return [pscustomobject]@{
        Closable    = $closable.ToArray()
        SystemHeld  = $systemHeld.ToArray()
        Uninspected = $uninspected
    }
}

function Show-DgpuOccupant {
    param($Occupants)
    if ($Occupants.Closable.Count -eq 0) {
        Write-Log -Message '  closable occupants: none'
    } else {
        Write-Log -Level WARN -Message ("  closable occupants ({0}):" -f $Occupants.Closable.Count)
        $shown = 0
        foreach ($o in $Occupants.Closable) {
            if ($shown -ge 15) {
                Write-Log -Level WARN -Message ("      ... and {0} more" -f ($Occupants.Closable.Count - $shown))
                break
            }
            $window = ''
            if ($o.HasWindow) { $window = ' has-main-window' }
            Write-Log -Level WARN -Message ("      {0} (PID {1}) source={2}{3}" -f $o.Name, $o.ProcessId, $o.Source, $window)
            $shown++
        }
    }
    if ($Occupants.SystemHeld.Count -gt 0) {
        $heldNames = @($Occupants.SystemHeld | ForEach-Object { $_.Name } | Select-Object -Unique)
        Write-Log -Message ("  held by the system (not closable, {0}): {1}" -f $Occupants.SystemHeld.Count, ($heldNames -join ', '))
        Write-Log -Level WARN -Message '  Note: while these hold a dGPU handle, Disable-PnpDevice may simply fail (commonly "Generic failure"); the fallback path is reported when that happens.'
    }
    if ($Occupants.Uninspected -gt 0) {
        Write-Log -Level WARN -Message ("  {0} non-protected process(es) had an unreadable module list (protected/denied); a user of the dGPU may still hide there." -f $Occupants.Uninspected)
    }
}

function Clear-DgpuOccupant {
    <# Graceful window close first, then kill the process tree via taskkill /T /F.
       Process.Kill(bool) does not exist on .NET Framework / PS 5.1, so taskkill is used. #>
    param($Occupants)

    if ($Occupants.Closable.Count -eq 0) { return }

    Write-Log -Level STEP -Message 'Clearing occupancy: step 1, request a graceful close (data can be saved)'
    foreach ($o in $Occupants.Closable) {
        try {
            $p = Get-Process -Id $o.ProcessId -ErrorAction Stop
            if ($o.HasWindow -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
                [void]$p.CloseMainWindow()
                Write-Log -Message ("  CloseMainWindow -> {0} (PID {1})" -f $o.Name, $o.ProcessId)
            }
        } catch {
            Write-Log -Message ("  process already gone: {0} (PID {1})" -f $o.Name, $o.ProcessId)
        }
    }
    Start-Sleep -Seconds 1
    $after = Get-DgpuOccupant -IncludeOtherSessions:$IncludeOtherSessions
    if ($after.Closable.Count -eq 0) {
        Write-Log -Message '  no occupants left after the graceful close.'
        return
    }

    Write-Log -Level WARN -Message ("Clearing occupancy: step 2, force-kill the process tree ({0})" -f $after.Closable.Count)
    foreach ($o in $after.Closable) {
        $r = Invoke-Native -FilePath (Join-Path $env:SystemRoot 'System32\taskkill.exe') -Arguments @('/T', '/F', '/PID', [string]$o.ProcessId)
        $note = ''
        if ($r.ExitCode -eq 128) { $note = ' (already gone)' }
        Write-Log -Message ("  taskkill /T /F /PID {0} ({1}) -> exit {2}{3}" -f $o.ProcessId, $o.Name, $r.ExitCode, $note)
    }
    Start-Sleep -Seconds 1
    $final = Get-DgpuOccupant -IncludeOtherSessions:$IncludeOtherSessions
    if ($final.Closable.Count -gt 0) {
        Write-Log -Level WARN -Message ("  {0} occupant(s) survived: {1}" -f $final.Closable.Count, (($final.Closable | ForEach-Object { "$($_.Name)#$($_.ProcessId)" }) -join ', '))
    } else {
        Write-Log -Message '  occupancy cleared.'
    }
}

# =====================================================================================
# Disable / Enable
# =====================================================================================
function Invoke-DeviceDisable {
    param([string]$InstanceId, [switch]$AllowForce)

    Write-Log -Message ("  [1/3] Disable-PnpDevice -InstanceId {0}" -f $InstanceId)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        Disable-PnpDevice -InstanceId $InstanceId -Confirm:$false -ErrorAction Stop
        $ErrorActionPreference = $previous
        return [pscustomobject]@{ Ok = $true; How = 'Disable-PnpDevice' }
    } catch {
        $ErrorActionPreference = $previous
        Write-Log -Level WARN -Message ("      Disable-PnpDevice failed: {0}" -f $_.Exception.Message)
    }

    $pnputil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
    Write-Log -Message '  [2/3] fallback: pnputil /disable-device'
    $r = Invoke-Native -FilePath $pnputil -Arguments @('/disable-device', $InstanceId)
    Write-Log -Message ("      exit {0}: {1}" -f $r.ExitCode, ($r.Output -replace "`r?`n", ' | '))
    if ($r.ExitCode -eq 0) { return [pscustomobject]@{ Ok = $true; How = 'pnputil /disable-device' } }

    if ($AllowForce) {
        Write-Log -Message '  [2b] fallback: pnputil /disable-device /force (needs Windows 11 22H2 or newer)'
        $rf = Invoke-Native -FilePath $pnputil -Arguments @('/disable-device', $InstanceId, '/force')
        Write-Log -Message ("      exit {0}: {1}" -f $rf.ExitCode, ($rf.Output -replace "`r?`n", ' | '))
        if ($rf.ExitCode -eq 0) { return [pscustomobject]@{ Ok = $true; How = 'pnputil /disable-device /force' } }
    }
    return [pscustomobject]@{ Ok = $false; How = $null }
}

function Invoke-DeviceNodeRemove {
    <#
      pnputil /remove-device = DIF_REMOVE, a surprise removal: the device node is deleted
      from the PnP tree, so it no longer shows up in Device Manager at all (unlike a plain
      disable, which keeps the row with a down arrow). Occupants are interrupted at once,
      and the PCI bus re-enumerates the device on the next rescan/reboot unless it was
      disabled first (disable is what persists).
    #>
    param([string]$InstanceId, [string]$Why = 'removing the device node')
    $pnputil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
    Write-Log -Message ("  pnputil /remove-device /force ({0})" -f $Why)
    $r = Invoke-Native -FilePath $pnputil -Arguments @('/remove-device', $InstanceId, '/force')
    Write-Log -Message ("      exit {0}: {1}" -f $r.ExitCode, ($r.Output -replace "`r?`n", ' | '))
    if ($r.ExitCode -ne 0) {
        $r2 = Invoke-Native -FilePath $pnputil -Arguments @('/remove-device', $InstanceId)
        Write-Log -Message ("      retry without /force, exit {0}: {1}" -f $r2.ExitCode, ($r2.Output -replace "`r?`n", ' | '))
        return ($r2.ExitCode -eq 0)
    }
    return $true
}

function Test-DeviceDisabled {
    param([string]$InstanceId)
    $all = Get-DisplayClassDevice
    if ($null -eq $all) { return $null }
    $dev = @($all | Where-Object { $_.InstanceId -eq $InstanceId })
    if ($dev.Count -eq 0) { return $true }   # already removed: no longer usable, counts as done
    return ($dev[0].Status -ne 'OK')
}

function Test-DeviceNodeRemoved {
    <#
      $true  = the devnode is gone from the enumeration (or lingers only as a non-present
               ghost, Status Unknown), i.e. Device Manager no longer lists it;
      $false = still enumerated as a live device;
      $null  = cannot tell (Get-PnpDevice unavailable).
    #>
    param([string]$InstanceId)
    $all = Get-DisplayClassDevice
    if ($null -eq $all) { return $null }
    $dev = @($all | Where-Object { $_.InstanceId -eq $InstanceId })
    if ($dev.Count -eq 0) { return $true }
    Write-Log -Message ("      still enumerated: Status={0}" -f $dev[0].Status)
    if ($dev[0].Status -eq 'Unknown') { return $true }
    return $false
}

function Write-RestoreHelper {
    <# Disabling is persistent, so always leave a one-click recovery entry point behind. #>
    $scriptPath = $PSCommandPath
    if (-not $scriptPath) { return }
    $cmdPath = Join-Path $script:LogDir 'restore-dgpu.cmd'
    $content = @(
        '@echo off',
        'rem Generated by Invoke-DgpuSilence.ps1: one-click restore of the dGPU (needs administrator).',
        'rem If the screen is black / no display, read this file from another machine, or call the',
        'rem same command from a logon scheduled task.',
        'setlocal',
        ('powershell -NoProfile -ExecutionPolicy Bypass -File "{0}" -Action Enable' -f $scriptPath),
        'echo.',
        'echo exit code = %ERRORLEVEL%',
        'pause'
    )
    try {
        Set-Content -LiteralPath $cmdPath -Value $content -Encoding ASCII
        Write-Log -Message ("recovery entry point written: {0}" -f $cmdPath)
    } catch {
        Write-Log -Level WARN -Message ("could not write the recovery entry point: {0}" -f $_.Exception.Message)
    }
}

# =====================================================================================
# NVIDIA container services (the vendor's own pre-step)
# =====================================================================================
function Stop-NvidiaService {
    <#
      Best-effort, mirrors what the vendor's service does before it changes the dGPU state:
      stop NVDisplay.ContainerLocalSystem / NVContainerLocalSystem so the containers release
      their dGPU handles. Never fatal: a failure is logged and the run continues, because a
      plain Disable-PnpDevice often succeeds even with the containers running.
    #>
    $stopped = @()
    foreach ($name in $script:NvidiaServiceNames) {
        try {
            $svc = Get-Service -Name $name -ErrorAction Stop
        } catch {
            Write-Log -Message ("  service {0}: not installed" -f $name)
            continue
        }
        if ($svc.Status -eq 'Stopped') {
            Write-Log -Message ("  service {0}: already stopped" -f $name)
            continue
        }
        Write-Log -Message ("  service {0}: {1} -> stopping" -f $name, $svc.Status)
        try {
            Stop-Service -Name $name -Force -ErrorAction Stop
            $stopped += $name
        } catch {
            Write-Log -Level WARN -Message ("  service {0}: Stop-Service failed ({1}); falling back to net stop" -f $name, $_.Exception.Message)
            $r = Invoke-Native -FilePath (Join-Path $env:SystemRoot 'System32\net.exe') -Arguments @('stop', $name)
            Write-Log -Message ("  net stop {0} -> exit {1}: {2}" -f $name, $r.ExitCode, ($r.Output -replace "`r?`n", ' | '))
            if ($r.ExitCode -eq 0) { $stopped += $name }
        }
    }
    if ($stopped.Count -gt 0) {
        # give the display driver a moment to drop its references
        Start-Sleep -Seconds 2
        Write-Log -Message ("  stopped: {0}" -f ($stopped -join ', '))
    } else {
        Write-Log -Message '  no NVIDIA service needed stopping.'
    }
    return ,$stopped
}

function Start-NvidiaService {
    <#
      Counterpart of Stop-NvidiaService: bring the containers back after an enable (or after
      a failed disable) so the machine is left in a working state. Best-effort as well.
    #>
    foreach ($name in $script:NvidiaServiceNames) {
        try {
            $svc = Get-Service -Name $name -ErrorAction Stop
        } catch {
            continue
        }
        if ($svc.Status -eq 'Running') {
            Write-Log -Message ("  service {0}: already running" -f $name)
            continue
        }
        try {
            Start-Service -Name $name -ErrorAction Stop
            Write-Log -Message ("  service {0}: started" -f $name)
        } catch {
            Write-Log -Level WARN -Message ("  service {0}: Start-Service failed ({1}); falling back to net start" -f $name, $_.Exception.Message)
            $r = Invoke-Native -FilePath (Join-Path $env:SystemRoot 'System32\net.exe') -Arguments @('start', $name)
            Write-Log -Message ("  net start {0} -> exit {1}: {2}" -f $name, $r.ExitCode, ($r.Output -replace "`r?`n", ' | '))
        }
    }
}

function Invoke-DisableAction {
    Write-Log -Level STEP -Message 'Action: disable the NVIDIA dGPU device'

    if (-not (Test-Elevated)) {
        Fail -Code $script:ExitGate -Message 'Disabling requires administrator rights (start PowerShell elevated).'
    }

    $targets = Get-NvidiaDisplayDevice -State Enabled
    if ($null -eq $targets) {
        Fail -Code $script:ExitGate -Message 'Cannot enumerate Display-class devices (Get-PnpDevice unavailable); refusing to disable while blind.'
    }
    if ($RemoveDevice) {
        # Removing the node must also work when the device is already disabled: a plain
        # "Disable" run would report "target state reached" and never touch the node.
        $present = Get-NvidiaDisplayDevice -State All
        if ($null -eq $present) {
            Fail -Code $script:ExitGate -Message 'Cannot enumerate Display-class devices (Get-PnpDevice unavailable); refusing to remove while blind.'
        }
        if ($present.Count -gt 0) {
            Write-Log -Message ("-RemoveDevice: taking all {0} NVIDIA display device(s), enabled or not." -f $present.Count)
            $targets = $present
        }
    }
    if ($targets.Count -eq 0) {
        $all = Get-NvidiaDisplayDevice -State All
        if ($null -ne $all -and $all.Count -gt 0) {
            Write-Log -Message 'No enabled NVIDIA display device (already disabled): target state reached.'
        } else {
            Write-Log -Message 'No NVIDIA display device found: nothing to disable, target state reached.'
        }
        exit $script:ExitOk
    }
    Write-Log -Message ("target device(s): {0}" -f $targets.Count)
    foreach ($t in $targets) {
        $problem = Get-NvidiaProblemCode -InstanceId $t.InstanceId
        $problemText = 'n/a'
        if ($null -ne $problem) { $problemText = $problem }
        Write-Log -Message ("  {0}`n      InstanceId = {1}`n      Status = {2}  ProblemCode = {3}" -f $t.FriendlyName, $t.InstanceId, $t.Status, $problemText)
    }
    if ($targets.Count -gt 1) {
        Write-Log -Level WARN -Message 'Note: more than one NVIDIA display device matched; all of them will be disabled (a one-liner would disable all as well).'
    }

    # ---- gate 1: fallback display adapter ----
    $hasFallback = Test-AnyNonNvidiaDisplayAdapter
    if ($null -eq $hasFallback) {
        Fail -Code $script:ExitGate -Message 'Cannot confirm that a non-NVIDIA display adapter exists; refusing.'
    }
    if (-not $hasFallback) {
        Fail -Code $script:ExitGate -Message 'No enabled non-NVIDIA display adapter: disabling the dGPU could leave no display device at all; refusing.'
    }
    Write-Log -Message 'gate 1 passed: a non-NVIDIA display adapter (iGPU) is available as fallback.'

    # ---- gate 2: the dGPU drives no monitor ----
    $topology = Get-DisplayTopology
    Write-Log -Message 'display topology:'
    Show-DisplayTopology -Topology $topology
    $drives = Test-DgpuDrivesDisplay -Topology $topology
    if ($SkipDisplayCheck) {
        if ($drives.Drives -or (-not $drives.Known)) {
            Write-Log -Level WARN -Message ('gate 2 skipped by -SkipDisplayCheck, but the risk is real: ' + ($drives.Reasons -join '; '))
        } else {
            Write-Log -Level WARN -Message 'gate 2 skipped by -SkipDisplayCheck (topology showed no monitor on the dGPU anyway).'
        }
    } elseif (-not $drives.Known) {
        Fail -Code $script:ExitGate -Message 'Cannot determine the display topology (EnumDisplayDevices returned no adapter); refusing to disable while blind. Use -SkipDisplayCheck to override (not recommended).'
    } elseif ($drives.Drives) {
        $msg = 'The dGPU is driving a display (disabling it blacks the screen / drops the output): ' + ($drives.Reasons -join '; ') + '. To force it anyway use -SkipDisplayCheck (not recommended).'
        Fail -Code $script:ExitGate -Message $msg
    } else {
        Write-Log -Message 'gate 2 passed: no monitor is attached to the dGPU (not dGPU-direct, no external display on the dGPU).'
    }

    # ---- gate 1b: something non-NVIDIA actually drives the desktop ----
    # Gate 1 only proves that a non-NVIDIA display *device* is present and enabled, which a
    # virtual display driver (remote-play / IDD adapters) can also satisfy. The topology is
    # what proves that a real, non-NVIDIA adapter is driving the screen right now.
    if ($topology.Count -gt 0) {
        $attachedOthers = @($topology | Where-Object { -not $_.IsNvidia -and $_.AttachedToDesktop -and -not $_.Mirrored })
        if ($attachedOthers.Count -eq 0) {
            Fail -Code $script:ExitGate -Message 'No non-NVIDIA adapter is attached to the desktop: after disabling the dGPU nothing would drive the screen. Switch to hybrid mode first.'
        }
        $otherNames = (($attachedOthers | ForEach-Object { $_.Description }) | Select-Object -Unique) -join ', '
        Write-Log -Message ("gate 1b passed: the desktop is also driven by {0}." -f $otherNames)
    }

    # ---- gate 3: occupancy ----
    Write-Log -Level STEP -Message 'occupancy detection (nvidia-smi compute apps + NVIDIA user-mode driver module scan)'
    $occupants = Get-DgpuOccupant -IncludeOtherSessions:$IncludeOtherSessions
    Show-DgpuOccupant -Occupants $occupants
    if ($occupants.Closable.Count -gt 0 -and -not $Force) {
        Fail -Code $script:ExitOccupied -Message ("{0} program(s) are using the dGPU; add -Force to close them gracefully and then kill them." -f $occupants.Closable.Count)
    }
    Write-Log -Message 'gate 3 passed.'

    if ($DryRun) {
        Write-Log -Level STEP -Message 'DryRun: stopping here, nothing was changed. A real run would now clear the occupants (if -Force), stop the NVIDIA container services, disable the device and verify it.'
        exit $script:ExitOk
    }

    # ---- clear occupancy ----
    if ($Force -and $occupants.Closable.Count -gt 0) {
        Clear-DgpuOccupant -Occupants $occupants
    }

    # ---- stop the NVIDIA containers (vendor pre-step) ----
    if ($SkipServiceStop) {
        Write-Log -Message '-SkipServiceStop: leaving the NVIDIA container services alone.'
    } else {
        Write-Log -Level STEP -Message 'stopping the NVIDIA container services (the vendor does this before it changes the dGPU state)'
        [void](Stop-NvidiaService)
    }

    # ---- disable (and optionally remove the device node) ----
    Write-RestoreHelper
    Write-Log -Level STEP -Message 'disabling device(s)'
    $results = @()
    foreach ($t in $targets) {
        Write-Log -Message ("processing {0}" -f $t.InstanceId)

        # An already-disabled device still needs its node removed when -RemoveDevice was
        # asked for, so the disable step is skipped instead of aborting the whole loop.
        $r = $null
        if ($t.Status -eq 'OK') {
            $r = Invoke-DeviceDisable -InstanceId $t.InstanceId -AllowForce:$Force
            if (-not $r.Ok -and $AllowSurpriseRemove -and $Force) {
                $removedNode = Invoke-DeviceNodeRemove -InstanceId $t.InstanceId -Why 'last resort after a failed disable'
                if ($removedNode) { $r = [pscustomobject]@{ Ok = $true; How = 'pnputil /remove-device (last resort)' } }
            }
        } else {
            Write-Log -Message ("  already not enabled (Status={0}); skipping the disable step." -f $t.Status)
            $r = [pscustomobject]@{ Ok = $true; How = 'already disabled' }
        }

        if (-not $r.Ok) {
            Write-Log -Level ERROR -Message ("{0} could not be disabled (no -AllowSurpriseRemove given, or that path failed too)." -f $t.InstanceId)
            Write-Log -Level ERROR -Message 'Common causes: a process still holds a dGPU handle (DWM/CUDA/game), the device drives a display, or the driver refused to stop.'
            $results += [pscustomobject]@{ InstanceId = $t.InstanceId; Ok = $false; How = $null; Removed = $false }
            continue
        }

        Start-Sleep -Milliseconds 800
        $disabled = Test-DeviceDisabled -InstanceId $t.InstanceId
        if ($null -eq $disabled) {
            Write-Log -Level WARN -Message '  verification unavailable (Get-PnpDevice query failed); cannot confirm the device is disabled.'
        }
        Write-Log -Message ("  verify: Status is no longer OK = {0}; method = {1}" -f $disabled, $r.How)

        $removedNow = $false
        if ($RemoveDevice) {
            $removedNow = Invoke-DeviceNodeRemove -InstanceId $t.InstanceId -Why 'explicit -RemoveDevice'
            if ($removedNow) {
                Start-Sleep -Milliseconds 800
                $gone = Test-DeviceNodeRemoved -InstanceId $t.InstanceId
                if ($null -eq $gone) {
                    Write-Log -Level WARN -Message '  cannot verify the removal (Get-PnpDevice query failed).'
                } elseif ($gone) {
                    Write-Log -Message '  verify: the device node is no longer enumerated (gone from Device Manager).'
                } else {
                    Write-Log -Level WARN -Message '  the node is still enumerated; check Device Manager.'
                }
            }
        }
        $results += [pscustomobject]@{ InstanceId = $t.InstanceId; Ok = [bool]$disabled; How = $r.How; Removed = $removedNow }
    }

    # ---- verify + auto-rollback on a broken display path ----
    $okCount = @($results | Where-Object { $_.Ok }).Count
    Write-Log -Level STEP -Message ("result: {0}/{1} succeeded" -f $okCount, $results.Count)

    $postTopology = Get-DisplayTopology
    $desktopOk = Test-DesktopHasAdapter -Topology $postTopology
    if ($null -eq $desktopOk) {
        Write-Log -Level WARN -Message 'cannot verify the display path after disabling (topology unavailable); check the screen manually.'
    } elseif (-not $desktopOk) {
        Write-Log -Level CRITICAL -Message 'The desktop has no attached adapter left (display path broken) - rolling back now: re-enabling the dGPU.'
        Invoke-EnableAction -AutoRollback | Out-Null
        Fail -Code $script:ExitError -Message 'Disabling broke the display path; an automatic restore was attempted. Check the monitor and keep the log.'
    } else {
        Write-Log -Message 'read back: the desktop still has a usable display adapter.'
    }

    if ($okCount -eq 0) {
        if (-not $SkipServiceStop) {
            Write-Log -Message 'restoring the NVIDIA container services after the failed disable.'
            [void](Start-NvidiaService)
        }
        Fail -Code $script:ExitDisableFailed -Message 'Disable did not take effect.'
    }
    if ($okCount -lt $results.Count) {
        if (-not $SkipServiceStop) {
            Write-Log -Message 'restoring the NVIDIA container services after the partial disable.'
            [void](Start-NvidiaService)
        }
        Fail -Code $script:ExitDisableFailed -Message 'Some devices were not disabled (see above).'
    }

    $removedCount = @($results | Where-Object { $_.Removed }).Count
    if ($removedCount -gt 0) {
        Write-Log -Level STEP -Message ("disable + node removal succeeded for {0} device(s): the row is gone from Device Manager." -f $removedCount)
        Write-Log -Message 'The disable part is what is expected to survive a reboot (the bus re-enumerates a removed device and Windows should re-apply its disabled state); worth verifying once after a reboot. Restore with -Action Enable (it rescans first).'
    } else {
        Write-Log -Level STEP -Message 'disable succeeded. Note: the state is persistent (still disabled after a reboot); restore with -Action Enable.'
    }
    exit $script:ExitOk
}

function Invoke-EnableAction {
    param([switch]$AutoRollback)

    Write-Log -Level STEP -Message 'Action: enable the NVIDIA dGPU device'

    if (-not (Test-Elevated)) {
        Fail -Code $script:ExitGate -Message 'Enabling requires administrator rights.'
    }

    $targets = Get-NvidiaDisplayDevice -State NotEnabled
    if ($null -eq $targets) {
        Fail -Code $script:ExitGate -Message 'Cannot enumerate Display-class devices (Get-PnpDevice unavailable).'
    }

    if ($targets.Count -eq 0) {
        # Possibly surprise-removed before (the device is not in the enumeration at all)
        # -> trigger a hardware rescan so it re-enumerates.
        Write-Log -Message 'No non-enabled NVIDIA device found; running pnputil /scan-devices so a removed device re-enumerates.'
        $pnputil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
        $r = Invoke-Native -FilePath $pnputil -Arguments @('/scan-devices')
        Write-Log -Message ("  /scan-devices exit {0}" -f $r.ExitCode)
        Start-Sleep -Seconds 3
        $targets = Get-NvidiaDisplayDevice -State NotEnabled
        if ($null -eq $targets) { Fail -Code $script:ExitGate -Message 'Re-enumeration failed (Get-PnpDevice unavailable).' }
        if ($targets.Count -eq 0) {
            Write-Log -Message 'Nothing to enable (either already enabled, or this machine has no NVIDIA device).'
            exit $script:ExitOk
        }
    }

    Write-Log -Message ("target device(s): {0}" -f $targets.Count)
    foreach ($t in $targets) {
        $problem = Get-NvidiaProblemCode -InstanceId $t.InstanceId
        $problemText = 'n/a'
        $hint = ''
        if ($null -ne $problem) {
            $problemText = $problem
            if ($problem -eq 22) { $hint = '(ProblemCode 22 = disabled, exactly what we are recovering from)' }
            else { $hint = ("(ProblemCode {0} = real fault; enabling may not fix it)" -f $problem) }
        }
        Write-Log -Message ("  {0}`n      InstanceId = {1}`n      Status = {2}  ProblemCode = {3} {4}" -f $t.FriendlyName, $t.InstanceId, $t.Status, $problemText, $hint)
    }

    if ($DryRun) {
        Write-Log -Level STEP -Message 'DryRun: stopping here, nothing was changed.'
        exit $script:ExitOk
    }

    $pnputilPath = Join-Path $env:SystemRoot 'System32\pnputil.exe'
    $results = @()
    foreach ($t in $targets) {
        $ok = $false
        $how = $null
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Stop'
        try {
            Enable-PnpDevice -InstanceId $t.InstanceId -Confirm:$false -ErrorAction Stop
            $ok = $true
            $how = 'Enable-PnpDevice'
        } catch {
            Write-Log -Level WARN -Message ("  Enable-PnpDevice failed: {0}" -f $_.Exception.Message)
        } finally {
            $ErrorActionPreference = $previous
        }
        if (-not $ok) {
            $r = Invoke-Native -FilePath $pnputilPath -Arguments @('/enable-device', $t.InstanceId)
            Write-Log -Message ("  pnputil /enable-device exit {0}: {1}" -f $r.ExitCode, ($r.Output -replace "`r?`n", ' | '))
            if ($r.ExitCode -eq 0) { $ok = $true; $how = 'pnputil /enable-device' }
        }

        Start-Sleep -Milliseconds 1500
        $all = Get-DisplayClassDevice
        $dev = @()
        if ($null -ne $all) { $dev = @($all | Where-Object { $_.InstanceId -eq $t.InstanceId }) }
        $status = 'unknown'
        if ($dev.Count -gt 0) { $status = $dev[0].Status }
        if ($status -ne 'OK') {
            Write-Log -Level WARN -Message ("  status is still {0} after enabling; trying pnputil /restart-device" -f $status)
            $rr = Invoke-Native -FilePath $pnputilPath -Arguments @('/restart-device', $t.InstanceId)
            Write-Log -Message ("  /restart-device exit {0}" -f $rr.ExitCode)
            Start-Sleep -Seconds 2
            $all = Get-DisplayClassDevice
            $dev = @()
            if ($null -ne $all) { $dev = @($all | Where-Object { $_.InstanceId -eq $t.InstanceId }) }
            if ($dev.Count -gt 0) { $status = $dev[0].Status }
        }
        Write-Log -Message ("  verify: Status = {0} (method = {1})" -f $status, $how)
        $results += [pscustomobject]@{ InstanceId = $t.InstanceId; Ok = ($status -eq 'OK'); Status = $status }
    }

    $r2 = Invoke-Native -FilePath $pnputilPath -Arguments @('/scan-devices')
    Write-Log -Message ("final /scan-devices exit {0}" -f $r2.ExitCode)

    # Bring the NVIDIA containers back: Enable means "the dGPU works again", and a disable
    # run leaves them stopped. Best-effort, and also run on partial failure on purpose.
    if (-not $SkipServiceStop) {
        Write-Log -Level STEP -Message 'restarting the NVIDIA container services'
        [void](Start-NvidiaService)
    }

    $okCount = @($results | Where-Object { $_.Ok }).Count
    Write-Log -Level STEP -Message ("result: {0}/{1} enabled and OK" -f $okCount, $results.Count)
    if ($okCount -eq 0) { Fail -Code $script:ExitEnableFailed -Message 'Enable did not take effect.' }
    if ($okCount -lt $results.Count) { Fail -Code $script:ExitEnableFailed -Message 'Some devices were not enabled (possibly a real hardware fault, see ProblemCode).' }
    if ($AutoRollback) { return $true }
    exit $script:ExitOk
}

# =====================================================================================
# Status
# =====================================================================================
function Invoke-StatusAction {
    Write-Log -Level STEP -Message 'Action: read-only health check (Status)'

    Write-Log -Message ("elevated: {0}" -f (Test-Elevated))

    $all = Get-NvidiaDisplayDevice -State All
    if ($null -eq $all) {
        Write-Log -Level WARN -Message 'NVIDIA device list: unavailable (Get-PnpDevice denied; normal inside this sandbox, should work elevated on a real machine)'
    } elseif ($all.Count -eq 0) {
        Write-Log -Message 'NVIDIA device list: none (this machine has no NVIDIA display device)'
    } else {
        Write-Log -Message ("NVIDIA device list ({0}):" -f $all.Count)
        foreach ($d in $all) {
            $problem = Get-NvidiaProblemCode -InstanceId $d.InstanceId
            $problemText = 'n/a'
            if ($null -ne $problem) { $problemText = $problem }
            Write-Log -Message ("  {0}`n      Status = {1}  ProblemCode = {2}  Class = {3}`n      InstanceId = {4}" -f $d.FriendlyName, $d.Status, $problemText, $d.Class, $d.InstanceId)
        }
    }
    Write-Log -Message ("Get-PnpDevice available: {0}" -f $script:PnpAvailable)

    $fallback = Test-AnyNonNvidiaDisplayAdapter
    $fallbackText = $fallback
    if ($null -eq $fallback) { $fallbackText = 'unknown' }
    Write-Log -Message ("fallback adapter (non-NVIDIA and enabled): {0}" -f $fallbackText)

    $topology = Get-DisplayTopology
    Write-Log -Message 'display topology:'
    Show-DisplayTopology -Topology $topology
    $drives = Test-DgpuDrivesDisplay -Topology $topology
    $driveText = ''
    if (-not $drives.Known) { $driveText = ' -- unknown (topology unavailable)' }
    elseif ($drives.Drives) { $driveText = ' -- ' + ($drives.Reasons -join '; ') }
    Write-Log -Message ("dGPU is driving a display: {0}{1}" -f $drives.Drives, $driveText)
    $desktopOk = Test-DesktopHasAdapter -Topology $topology
    $desktopText = $desktopOk
    if ($null -eq $desktopOk) { $desktopText = 'unknown' }
    Write-Log -Message ("desktop has an attached adapter: {0}" -f $desktopText)

    Write-Log -Level STEP -Message 'occupancy detection'
    $occupants = Get-DgpuOccupant -IncludeOtherSessions:$IncludeOtherSessions
    Show-DgpuOccupant -Occupants $occupants

    Write-Log -Level STEP -Message 'gate verdict (each gate is checked again when Action=Disable)'
    $elevated = Test-Elevated
    $gate2Text = (-not $drives.Drives)
    if (-not $drives.Known) { $gate2Text = 'unknown (topology unavailable)' }
    $attachedOthers = @($topology | Where-Object { -not $_.IsNvidia -and $_.AttachedToDesktop -and -not $_.Mirrored })
    $gate1bText = ($attachedOthers.Count -gt 0)
    if ($topology.Count -eq 0) { $gate1bText = 'unknown' }
    $closableText = ''
    if ($occupants.Closable.Count -gt 0) { $closableText = ' (-Force can clear them)' }
    Write-Log -Message ("  gate0  elevated                    : {0}" -f $elevated)
    Write-Log -Message ("  gate1  non-NVIDIA device present   : {0}" -f $fallbackText)
    Write-Log -Message ("  gate1b non-NVIDIA drives desktop   : {0}" -f $gate1bText)
    Write-Log -Message ("  gate2  dGPU drives no display      : {0}" -f $gate2Text)
    Write-Log -Message ("  gate3  no closable occupants       : {0}{1}" -f ($occupants.Closable.Count -eq 0), $closableText)

    $blockers = @()
    if (-not $elevated) { $blockers += 'gate0: not elevated - Disable/Enable need an administrator shell' }
    if ($null -eq $fallback) { $blockers += 'gate1: cannot confirm a non-NVIDIA display device exists' }
    elseif ($fallback -eq $false) { $blockers += 'gate1: no enabled non-NVIDIA display device' }
    if ($topology.Count -eq 0) { $blockers += 'gate1b/gate2: display topology unreadable (Disable refuses while blind)' }
    elseif ($attachedOthers.Count -eq 0) { $blockers += 'gate1b: no non-NVIDIA adapter is attached to the desktop - disabling would leave nothing driving the screen' }
    if ($drives.Drives) { $blockers += ('gate2: ' + ($drives.Reasons -join '; ')) }
    if ($occupants.Closable.Count -gt 0) { $blockers += ("gate3: {0} closable occupant(s) - add -Force to clear them" -f $occupants.Closable.Count) }

    if ($blockers.Count -eq 0) {
        Write-Log -Message '  verdict                      : ALL GATES PASS - Action=Disable is allowed right now'
    } else {
        Write-Log -Level WARN -Message ("  verdict                      : NOT ready to disable, {0} blocker(s):" -f $blockers.Count)
        foreach ($b in $blockers) { Write-Log -Level WARN -Message ("      - {0}" -f $b) }
    }
    exit $script:ExitOk
}

# =====================================================================================
# Self-elevation + interactive menu
# =====================================================================================
function Get-SelfExecutable {
    <# Path of the running PowerShell host, with a safe fallback. #>
    $exe = $null
    try { $exe = (Get-Process -Id $PID -ErrorAction Stop).Path } catch { $exe = $null }
    if (-not $exe) { $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' }
    return $exe
}

function Restart-SelfElevated {
    <#
      Relaunch this script in a new window with administrator rights (one UAC prompt),
      then the caller exits. Only the interactive menu elevates implicitly: an -Action run
      never does, so scripted callers keep getting a predictable exit code 2 instead of an
      interactive UAC prompt.
    #>
    $exe = Get-SelfExecutable
    # Start-Process does not quote arguments, so paths are quoted by hand.
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-Interactive', '-Elevated',
        # share the parent's log file, so every action lands in one place
        '-LogPath', ('"{0}"' -f $script:LogFile)
    )
    if ($IncludeOtherSessions) { $arguments += '-IncludeOtherSessions' }

    Write-Log -Level STEP -Message 'Requesting administrator rights (UAC prompt)...'
    try {
        Start-Process -FilePath $exe -Verb RunAs -ArgumentList $arguments -ErrorAction Stop | Out-Null
        Write-Log -Message 'The elevated window was launched; this window can be closed.'
        return $true
    } catch {
        Write-Log -Level ERROR -Message ("Elevation failed or was refused: {0}" -f $_.Exception.Message)
        Write-Log -Level ERROR -Message 'Re-run from an administrator shell, or use -NoElevate together with -Action Status.'
        return $false
    }
}

function Show-InteractiveSummary {
    <# Compact in-process state view for the menu: no child process, no exit. #>
    $all = Get-NvidiaDisplayDevice -State All
    if ($null -eq $all) {
        Write-Host '  dGPU        : unavailable (Get-PnpDevice denied - are you elevated?)' -ForegroundColor Yellow
    } elseif ($all.Count -eq 0) {
        Write-Host '  dGPU        : none found (if the node was removed earlier this is expected - option 5 rescans)' -ForegroundColor Yellow
    } else {
        foreach ($d in $all) {
            $problem = Get-NvidiaProblemCode -InstanceId $d.InstanceId
            $problemText = 'n/a'
            if ($null -ne $problem) { $problemText = $problem }
            $state = 'DISABLED / unusable'
            if ($d.Status -eq 'OK') { $state = 'enabled' }
            Write-Host ("  dGPU        : {0}  [{1}]  Status={2} ProblemCode={3}" -f $d.FriendlyName, $state, $d.Status, $problemText)
        }
    }

    $topology = Get-DisplayTopology
    $drives = Test-DgpuDrivesDisplay -Topology $topology
    if (-not $drives.Known) {
        Write-Host '  display     : topology unreadable - Disable will refuse' -ForegroundColor Yellow
    } else {
        $attachedOthers = @($topology | Where-Object { -not $_.IsNvidia -and $_.AttachedToDesktop -and -not $_.Mirrored })
        if ($drives.Drives) {
            Write-Host '  display     : dGPU is DRIVING A DISPLAY - disabling it would black the screen' -ForegroundColor Yellow
        } else {
            Write-Host '  display     : dGPU is not driving any monitor' -ForegroundColor Gray
        }
        if ($attachedOthers.Count -gt 0) {
            $names = (($attachedOthers | ForEach-Object { $_.Description }) | Select-Object -Unique) -join ', '
            Write-Host ("  fallback    : {0}" -f $names) -ForegroundColor Gray
        } else {
            Write-Host '  fallback    : NONE attached - disabling would leave no display' -ForegroundColor Yellow
        }
    }

    $occupants = Get-DgpuOccupant -IncludeOtherSessions:$IncludeOtherSessions
    Write-Host ("  occupants   : {0} closable / {1} held by the system" -f $occupants.Closable.Count, $occupants.SystemHeld.Count)
    if ($occupants.Closable.Count -gt 0) {
        $list = ($occupants.Closable | Select-Object -First 6 | ForEach-Object { "$($_.Name)#$($_.ProcessId)" }) -join ', '
        Write-Host ("                {0}" -f $list) -ForegroundColor Gray
    }
}

function Invoke-ChildAction {
    <#
      Run one action as a child process of this script. The child reuses all the internal
      gates/verification/exit-code logic verbatim, and only the child ends; children inherit
      our elevation.
      Out-Host sends the child's stdout straight to the console without putting it on this
      function's output stream (the caller only wants the exit code), and it streams live.
      Trade-off: because the child's stdout is a pipe, its Write-Host colours are lost here.
      Start-Process -Wait -PassThru would keep the colours but needs a process handle, which
      restricted environments can deny.
    #>
    param([string[]]$ChildArguments)

    $exe = Get-SelfExecutable
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-LogPath', $script:LogFile
    )
    if ($IncludeOtherSessions) { $arguments += '-IncludeOtherSessions' }
    foreach ($a in $ChildArguments) { $arguments += $a }

    Write-Host ''
    & $exe @arguments 2>&1 | Out-Host
    $code = $LASTEXITCODE
    Write-Host ''
    $colour = 'Green'
    if ($code -ne 0) { $colour = 'Yellow' }
    Write-Host ("  -> action finished, exit code {0}" -f $code) -ForegroundColor $colour
    return $code
}

function Invoke-InteractiveMenu {
    Write-Log -Level STEP -Message 'Interactive menu started'
    while ($true) {
        Write-Host ''
        Write-Host '=================== dGPU silence / restore ===================' -ForegroundColor Cyan
        Show-InteractiveSummary
        Write-Host ''
        Write-Host '  1) Status                 read-only health check, changes nothing'
        Write-Host '  2) Disable dGPU           make the discrete GPU unusable (persistent, row stays)'
        Write-Host '  3) Disable, force         close/kill the programs using the dGPU first'
        Write-Host '  4) Disable, force+remove  clear occupancy AND remove the node (gone from the list)'
        Write-Host '  5) Enable dGPU            bring the discrete GPU back (rescans if the node was removed)'
        Write-Host '  6) Remove node only       disable + remove; for an already-disabled device'
        Write-Host '  0) Exit                   (pressing Enter also exits)'
        Write-Host ''
        $choice = Read-Host 'Select'

        switch ($choice) {
            '1' { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Status')) }
            '2' { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Disable')) }
            '3' {
                $confirm = Read-Host 'This closes/kills programs using the dGPU; unsaved work can be lost. Type YES'
                if ($confirm -eq 'YES') { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Disable', '-Force')) }
                else { Write-Host '  cancelled.' -ForegroundColor Yellow }
            }
            '4' {
                $confirm = Read-Host 'This closes/kills programs using the dGPU AND removes the device node. Type YES'
                if ($confirm -eq 'YES') { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Disable', '-Force', '-RemoveDevice')) }
                else { Write-Host '  cancelled.' -ForegroundColor Yellow }
            }
            '5' { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Enable')) }
            '6' { [void](Invoke-ChildAction -ChildArguments @('-Action', 'Disable', '-RemoveDevice')) }
            '0' { return $script:ExitOk }
            ''  { return $script:ExitOk }
            default { Write-Host '  unknown choice.' -ForegroundColor Yellow }
        }
    }
}

# =====================================================================================
# Main
# =====================================================================================
$actionBound = $PSBoundParameters.ContainsKey('Action')
$interactiveMode = $Interactive -or (-not $actionBound)

if ($interactiveMode -and ((-not [Environment]::UserInteractive) -or ($Host.Name -ne 'ConsoleHost'))) {
    Write-Log -Level WARN -Message 'No interactive console detected; falling back to -Action Status.'
    $interactiveMode = $false
}

Write-Log -Level STEP -Message ('Invoke-DgpuSilence start: Action={0} interactive={1} Force={2} AllowSurpriseRemove={3} RemoveDevice={4} DryRun={5} SkipDisplayCheck={6} SkipServiceStop={7}' -f $Action, $interactiveMode, [bool]$Force, [bool]$AllowSurpriseRemove, [bool]$RemoveDevice, [bool]$DryRun, [bool]$SkipDisplayCheck, [bool]$SkipServiceStop)
Write-Log -Message ("host = {0} / PID = {1} / user = {2}" -f $env:COMPUTERNAME, $PID, [System.Security.Principal.WindowsIdentity]::GetCurrent().Name)

if ($AllowSurpriseRemove -and -not $Force) {
    Write-Log -Level WARN -Message '-AllowSurpriseRemove only takes effect together with -Force (the surprise-removal path will not be used in this run).'
}

# The menu can toggle the device, so it asks for administrator rights up front (one UAC
# prompt, one window). -Action runs never elevate implicitly.
if ($interactiveMode -and -not (Test-Elevated) -and -not $NoElevate -and -not $Elevated) {
    if (Restart-SelfElevated) { exit $script:ExitOk }
    exit $script:ExitGate
}

try {
    if ($interactiveMode) {
        $code = Invoke-InteractiveMenu
        Write-Host ''
        try { [void](Read-Host 'Press Enter to close this window') } catch { }
        exit $code
    }
    switch ($Action) {
        'Status'  { Invoke-StatusAction }
        'Disable' { Invoke-DisableAction }
        'Enable'  { Invoke-EnableAction }
    }
} catch {
    Write-Log -Level ERROR -Message ("unexpected error: {0}`n{1}" -f $_.Exception.Message, $_.ScriptStackTrace)
    exit $script:ExitError
}
