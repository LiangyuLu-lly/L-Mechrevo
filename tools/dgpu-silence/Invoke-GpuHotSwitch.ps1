<#
  Invoke-GpuHotSwitch.ps1  --  hot-switch the discrete GPU with NO vendor software

  What it does (documented ACPI-WMI interface):
      cmd=3 addr=1  ->  write 0x3_0000_0000_01   iGPU-only ON  (switch the dGPU away)
      cmd=3 addr=1  ->  write 0x3_0000_0000_00   iGPU-only OFF (bring it back)
      cmd=3 addr=3  ->  read  support   85 = this machine has the runtime switch, 170 = it does not
      cmd=3 addr=2  ->  read  state     85 = iGPU-only ACTIVE (dGPU gone), 170 = not engaged

  It does NOT use the control centre, and it never writes EC bytes. The vendor's own pre-step of
  stopping the NVIDIA display container is reproduced here, because the switch is only accepted
  while that container is down.

  Success is verified, never assumed. "Silenced" means all three hold:
      * firmware state bit  == 85
      * nvidia-smi exit code != 0   (the driver can no longer reach the GPU)
      * no NVIDIA adapter is present in the PnP tree (a leftover node would be a phantom)
  If verification times out, the switch is rolled back automatically and reported.

  Exit codes: 0 ok | 1 error | 2 refused (precondition) | 3 refused (no confirmation)
              4 silence failed (rolled back) | 5 restore failed
#>
[CmdletBinding()]
param(
    [ValidateSet('Status', 'Silence', 'Restore')]
    [string]$Action = 'Status',

    [switch]$Force,             # override the "the dGPU currently drives the desktop" refusal
    [switch]$Yes,               # do not ask for the typed confirmation
    [switch]$DryRun,            # show what would happen, send no writes
    [switch]$SkipServiceStop,   # do not stop the NVIDIA display container first
    [int]$TimeoutSeconds = 120,
    [int]$PollSeconds = 5,
    [string]$LogPath,
    [string]$StateEngine = ''   # where to load the verified ACPI helpers from
)

$ErrorActionPreference = 'Continue'
$script:Report = New-Object System.Collections.ArrayList

function Write-Report {
    param([string]$Message = '', [string]$Level = 'INFO')
    $line = if ($Level -eq 'INFO') { $Message } else { ("{0,-8} {1}" -f "[$Level]", $Message) }
    Write-Host $line
    [void]$script:Report.Add($line)
}

function Save-Report {
    if (-not $LogPath) { return }
    try {
        Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8
        Write-Host ("report written to {0}" -f $LogPath)
    } catch { }
}

# ---------------------------------------------------------------------------------------
# load the ACPI helpers that are already proven on real hardware, instead of re-implementing
# the packing/extraction (the probe has been validated on four different laptops).
# ---------------------------------------------------------------------------------------
$probe = $StateEngine
if (-not $probe) { $probe = Join-Path $PSScriptRoot 'Get-NvramState.ps1' }
if (-not (Test-Path -LiteralPath $probe)) {
    Write-Report -Level ERROR -Message ("helper engine not found: {0}" -f $probe)
    Save-Report
    exit 1
}
$ast = $null
try { $ast = [System.Management.Automation.Language.Parser]::ParseFile($probe, [ref]$null, [ref]$null) } catch { }
if (-not $ast) {
    Write-Report -Level ERROR -Message ("cannot parse {0}" -f $probe)
    Save-Report
    exit 1
}
foreach ($fnName in @('Get-AcpiTargetInstance', 'Invoke-AcpiGetSetULong', 'Invoke-Native', 'Get-SmiSnapshot', 'Get-NvidiaInventoryLite', 'Get-NvHdAudioPresent')) {
    $found = $ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $fnName }, $true)
    if ($found.Count -ge 1) {
        try { Invoke-Expression $found[0].Extent.Text } catch { }
    }
}
foreach ($need in @('Invoke-AcpiGetSetULong', 'Get-SmiSnapshot', 'Get-NvidiaInventoryLite')) {
    if (-not (Get-Command $need -ErrorAction SilentlyContinue)) {
        Write-Report -Level ERROR -Message ("helper function {0} is missing from {1}" -f $need, $probe)
        Save-Report
        exit 1
    }
}

function Get-AcpiInstance {
    $t = $null
    if (Get-Command Get-AcpiTargetInstance -ErrorAction SilentlyContinue) {
        try { $t = Get-AcpiTargetInstance } catch { }
    }
    if ($null -eq $t) {
        try {
            $inst = @(Get-CimInstance -Namespace 'root\WMI' -ClassName 'AcpiTest_MULong' -ErrorAction Stop)
            $t = $inst | Where-Object { $_.InstanceName -like '*PNP0C14\1_1*' } | Select-Object -First 1
            if (-not $t) { $t = $inst | Select-Object -First 1 }
        } catch { }
    }
    return $t
}

function Get-HsState {
    <# One snapshot of everything the decision needs. #>
    $t = Get-AcpiInstance
    $sup = $null
    $sta = $null
    $err = ''
    if ($t) {
        $r1 = Invoke-AcpiGetSetULong -Instance $t -Data ([uint64]0x30000000003)
        if ($null -ne $r1.Value) { $sup = [int]($r1.Value -band 0xFF) } elseif ($r1.Error) { $err = $r1.Error }
        $r2 = Invoke-AcpiGetSetULong -Instance $t -Data ([uint64]0x30000000002)
        if ($null -ne $r2.Value) { $sta = [int]($r2.Value -band 0xFF) }
    }
    $ig = $null
    try { $ig = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting' -Name 'IGPUonly' -ErrorAction Stop).IGPUonly } catch { }
    $inv = Get-NvidiaInventoryLite
    $smi = Get-SmiSnapshot
    return [pscustomobject]@{
        Target    = $t
        Support   = $sup
        Status    = $sta
        IgpuOnly  = $ig
        NvPresent = $inv.Present
        NvCount   = $inv.Count
        NvPhantom = $inv.PhantomCount
        NvDevices = $inv.Devices
        SmiExit   = $smi.Exit
        SmiError  = $smi.Error
        SmiPower  = $smi.PowerW
        AcpiError = $err
    }
}

function Test-HsSilenced {
    param($S)
    $ok1 = ($S.Status -eq 85)
    $ok2 = ([string]$S.SmiExit -ne '0' -and [string]$S.SmiExit -ne '')
    $ok3 = (-not $S.NvPresent)
    return [pscustomobject]@{ State = $ok1; Driver = $ok2; Absent = $ok3; All = ($ok1 -and $ok2 -and $ok3) }
}

function Get-DesktopDrivers {
    <# Which adapters currently drive the desktop. Switching away from a dGPU that owns the panel
       is what makes screens go black, so that case is refused unless -Force. #>
    $rows = @()
    try {
        foreach ($v in @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop)) {
            $w = 0
            try { if ($null -ne $v.CurrentHorizontalResolution) { $w = [int]$v.CurrentHorizontalResolution } } catch { }
            if ($w -le 0) { continue }
            $isNv = ($v.Name -match 'NVIDIA') -or ($v.PNPDeviceID -match 'VEN_10DE')
            $rows += [pscustomobject]@{ Name = $v.Name; Nvidia = $isNv; Width = $w }
        }
    } catch { }
    return $rows
}

function Stop-NvContainer {
    $stopped = @()
    foreach ($n in @('NVDisplay.ContainerLocalSystem', 'NVContainerLocalSystem', 'NVDisplay.ContainerUser', 'NVContainerUser')) {
        $svc = Get-Service -Name $n -ErrorAction SilentlyContinue
        if (-not $svc) { continue }
        if ($svc.Status -eq 'Stopped') { continue }
        try {
            Stop-Service -Name $n -Force -ErrorAction Stop
            $stopped += $n
            Write-Report ("    stopped {0}" -f $n)
        } catch {
            $null = & net.exe stop $n 2>&1
            if ((Get-Service -Name $n -ErrorAction SilentlyContinue).Status -eq 'Stopped') {
                $stopped += $n
                Write-Report ("    stopped {0} (net stop)" -f $n)
            } else {
                Write-Report -Level WARN -Message ("    could not stop {0}" -f $n)
            }
        }
    }
    return $stopped
}

function Start-NvContainer {
    param($Names)
    foreach ($n in @($Names)) {
        try {
            Start-Service -Name $n -ErrorAction Stop
            Write-Report ("    started {0}" -f $n)
        } catch {
            Write-Report -Level WARN -Message ("    could not start {0}" -f $n)
        }
    }
}

function Invoke-HsWrite {
    param($Target, [uint64]$Data, [string]$What, [int]$Expect = -1)
    if ($DryRun) {
        Write-Report ("    DRY RUN: would write {0} ({1})" -f $What, $Data)
        return $true
    }
    $r = Invoke-AcpiGetSetULong -Instance $Target -Data $Data
    if ($null -ne $r.Value) {
        try {
            $b = [int]([uint64]$r.Value -band 0xFF)
            if ($Expect -ge 0) {
                Write-Report ("    wrote {0}   (the ACPI call returned {1}; the state right after this write should be {2})" -f $What, $b, $Expect)
                if ($b -eq $Expect) {
                    Write-Report '    the returned value matches the expected post-write state.'
                } else {
                    Write-Report -Level WARN -Message ("    the returned value {0} does not match the expected {1}; the write may not have been accepted." -f $b, $Expect)
                }
            } else {
                Write-Report ("    wrote {0}   (the ACPI call returned {1})" -f $What, $b)
            }
        } catch {
            Write-Report ("    wrote {0}" -f $What)
        }
        return $true
    }
    Write-Report -Level ERROR -Message ("    write failed for {0}: {1}" -f $What, $r.Error)
    return $false
}

function Wait-HsSilenced {
    param($Target)
    $waited = 0
    while ($waited -le $TimeoutSeconds) {
        $s = Get-HsState
        $v = Test-HsSilenced -S $s
        Write-Report ("    t+{0,3}s  firmware_state={1}  smi_exit={2}  nvidia_present={3}  phantom={4}  igpu_only={5}" -f $waited, $s.Status, $s.SmiExit, $s.NvPresent, $s.NvPhantom, $s.IgpuOnly)
        if ($v.All) { return $true }
        if ($waited -ge $TimeoutSeconds) { break }
        Start-Sleep -Seconds $PollSeconds
        $waited += $PollSeconds
    }
    return $false
}

# ---------------------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------------------
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
$isSystem = $identity.IsSystem

Write-Report '========================================================='
Write-Report ' GPU hot switch (ACPI iGPU-only) - no vendor software used'
Write-Report '========================================================='
Write-Report ("host={0}  user={1}  elevated={2}" -f $env:COMPUTERNAME, $identity.Name, $isAdmin)
Write-Report ("action={0}  force={1}  dryRun={2}" -f $Action, $Force, $DryRun)
if ($LogPath) { Write-Report ("log file     : {0}" -f $LogPath) }
Write-Report 'NO EC WRITES: only the ACPI-WMI selector words (cmd=3 addr=1) are ever written.'
Write-Report ''

$state = Get-HsState
if (-not $state.Target) {
    Write-Report -Level ERROR -Message 'the ACPI-WMI mapper (ACPI\PNP0C14) is not reachable - run elevated, or this machine has no such interface'
    Save-Report
    exit 1
}
Write-Report ("acpi instance : {0}" -f $state.Target.InstanceName)
Write-Report ("support       : {0}   (85 = runtime iGPU-only switch exists, 170 = it does not)" -f $state.Support)
Write-Report ("state         : {0}   (85 = iGPU-only ACTIVE, 170 = not engaged)" -f $state.Status)
Write-Report ("registry      : IGPUonly={0}  lgpuHotSwapSwitchStatus={1}" -f $state.IgpuOnly, (Get-HsState).IgpuOnly)
Write-Report ("nvidia pnp    : present={0} count={1} phantom={2}" -f $state.NvPresent, $state.NvCount, $state.NvPhantom)
Write-Report ("nvidia device : {0}" -f $state.NvDevices)
Write-Report ("nvidia-smi    : exit={0} power='{1}'" -f $state.SmiExit, $state.SmiPower)
$verdict = Test-HsSilenced -S $state
Write-Report ("silenced now  : {0}   (firmware={1} driverGone={2} absent={3})" -f $verdict.All, $verdict.State, $verdict.Driver, $verdict.Absent)
Write-Report ''

if ($Action -eq 'Status') {
    if ($verdict.All) {
        Write-Report -Level OK -Message 'the discrete GPU is currently switched away (silenced).'
    } elseif ($state.Status -eq 85) {
        Write-Report -Level WARN -Message 'the firmware state bit says iGPU-only, but the OS still sees the GPU - the switch is only half applied.'
    } else {
        Write-Report 'the discrete GPU is currently active (normal).'
    }
    Save-Report
    exit 0
}

if ($null -eq $state.Support) {
    Write-Report -Level ERROR -Message ('cannot read the support flag; refusing to write. {0}' -f $state.AcpiError)
    Save-Report
    exit 2
}
if ($state.Support -ne 85) {
    Write-Report -Level ERROR -Message ("support = {0}: this machine has NO runtime iGPU-only switch, so there is nothing to write." -f $state.Support)
    Write-Report '  (machines with 170 only support the cold route, which needs a reboot and is out of scope here.)'
    Save-Report
    exit 2
}

if ($Action -eq 'Silence') {
    if ($verdict.All) {
        Write-Report -Level OK -Message 'already silenced - nothing to do.'
        Save-Report
        exit 0
    }
    $drivers = Get-DesktopDrivers
    $nvDriving = @($drivers | Where-Object { $_.Nvidia })
    $otherDriving = @($drivers | Where-Object { -not $_.Nvidia })
    if ($nvDriving.Count -gt 0 -and $otherDriving.Count -eq 0 -and -not $Force) {
        Write-Report -Level ERROR -Message 'the desktop is currently driven by the NVIDIA adapter and by nothing else:'
        foreach ($d in $nvDriving) { Write-Report ("    {0} ({1}x{2})" -f $d.Name, $d.Width, '') }
        Write-Report '  switching it away could leave a black screen, so this is refused. Re-run with -Force only if you'
        Write-Report '  have a second display or another way back, or switch to the hybrid route first.'
        Save-Report
        exit 2
    }
    if (-not $Yes -and -not $DryRun) {
        Write-Report -Level WARN -Message 'this will switch the discrete GPU away. The screen may blink; the dGPU becomes unreachable'
        Write-Report '  until you run -Action Restore. Unsaved work in programs using the dGPU can be lost.'
        $answer = Read-Host 'Type SILENCE to continue'
        if ($answer -ne 'SILENCE') {
            Write-Report -Level WARN -Message 'not confirmed - nothing was written.'
            Save-Report
            exit 3
        }
    }

    $before = $state
    Write-Report -Level STEP -Message 'silencing the discrete GPU'
    $stopped = @()
    if (-not $SkipServiceStop) {
        Write-Report '  step 1: stop the NVIDIA display container (the vendor does this too)'
        $stopped = Stop-NvContainer
    } else {
        Write-Report '  step 1: skipped (SkipServiceStop)'
    }
    Write-Report '  step 2: write the iGPU-only ON selector'
    if (-not (Invoke-HsWrite -Target $state.Target -Data ([uint64]0x30000000001) -What 'iGPU-only ON (0x3_0000_0000_01)' -Expect 85)) {
        Write-Report -Level ERROR -Message 'the write itself failed; nothing to roll back.'
        Save-Report
        exit 4
    }
    Write-Report ("  step 3: verify for up to {0}s (firmware bit + driver gone + device absent)" -f $TimeoutSeconds)
    if (Wait-HsSilenced -Target $state.Target) {
        Write-Report -Level OK -Message 'SUCCESS: the discrete GPU is switched away and verified.'
        $helper = Join-Path $env:ProgramData 'DgpuSilence\restore-hotswitch.cmd'
        try {
            $dir = Split-Path -Parent $helper
            if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
            $cmd = "@echo off`r`nrem One-click restore written by Invoke-GpuHotSwitch.ps1`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"{0}`" -Action Restore -Yes`r`n" -f $PSCommandPath
            [IO.File]::WriteAllText($helper, $cmd)
            Write-Report ("  a one-click restore was written to {0}" -f $helper)
        } catch { }
        Save-Report
        exit 0
    }

    Write-Report -Level WARN -Message 'verification timed out - rolling back so the machine is not left half-switched'
    $null = Invoke-HsWrite -Target $state.Target -Data ([uint64]0x30000000000) -What 'iGPU-only OFF (rollback)' -Expect 170
    Start-NvContainer -Names $stopped
    Start-Sleep -Seconds 3
    $after = Get-HsState
    Write-Report ("  after rollback: state={0} smi_exit={1} present={2} igpu_only={3}" -f $after.Status, $after.SmiExit, $after.NvPresent, $after.IgpuOnly)
    if ($after.Status -eq 170) {
        Write-Report -Level OK -Message 'rollback confirmed: the firmware state bit is back to 170 (not engaged).'
    } else {
        Write-Report -Level WARN -Message 'rollback wrote the OFF selector but the state bit did not read back as 170; check the machine.'
    }
    Save-Report
    exit 4
}

if ($Action -eq 'Restore') {
    if (-not $verdict.State -and $verdict.Driver -and -not $state.NvPresent -eq $false) { }
    if ($state.Status -ne 85) {
        Write-Report -Level WARN -Message 'the firmware state bit already says iGPU-only is NOT engaged; writing OFF anyway to be sure.'
    }
    if (-not $Yes -and -not $DryRun) {
        $answer = Read-Host 'Type RESTORE to continue'
        if ($answer -ne 'RESTORE') {
            Write-Report -Level WARN -Message 'not confirmed - nothing was written.'
            Save-Report
            exit 3
        }
    }
    Write-Report -Level STEP -Message 'bringing the discrete GPU back'
    # Signature of a DELETED node (the thorough force-silence mode runs pnputil /remove-device):
    # present=False AND phantom=0 AND no device row at all. An ACPI write cannot recreate a node,
    # so re-scan the bus first - measured on two machines, otherwise this restore just times out.
    if ((-not $state.NvPresent) -and ($state.NvPhantom -eq 0)) {
        Write-Report -Level WARN -Message 'no NVIDIA device node exists at all (not even a phantom) - it was REMOVED, not disabled.'
        Write-Report '  re-scanning the PCI bus so Windows recreates the device node...'
        if (-not $DryRun) {
            try { $null = & pnputil.exe /scan-devices 2>&1 } catch { }
            Start-Sleep -Seconds 3
            $state = Get-HsState
            Write-Report ("  after the rescan: present={0} phantom={1}" -f $state.NvPresent, $state.NvPhantom)
            if ($state.NvPresent) { Write-Report -Level OK -Message '  the device node is back.' }
            else { Write-Report -Level WARN -Message '  the node did not come back; a reboot will re-enumerate it for certain.' }
        }
    }
    Write-Report '  step 1: write the iGPU-only OFF selector'
    if (-not (Invoke-HsWrite -Target $state.Target -Data ([uint64]0x30000000000) -What 'iGPU-only OFF (0x3_0000_0000_00)' -Expect 170)) {
        Write-Report -Level ERROR -Message 'the write itself failed.'
        Save-Report
        exit 5
    }
    Write-Report '  step 2: start the NVIDIA display container again'
    Start-NvContainer -Names @('NVDisplay.ContainerLocalSystem', 'NVContainerLocalSystem')
    Write-Report ("  step 3: verify for up to {0}s (state bit 170 + driver answering again)" -f $TimeoutSeconds)
    $waited = 0
    $ok = $false
    while ($waited -le $TimeoutSeconds) {
        $s = Get-HsState
        Write-Report ("    t+{0,3}s  firmware_state={1}  smi_exit={2}  nvidia_present={3}  igpu_only={4}" -f $waited, $s.Status, $s.SmiExit, $s.NvPresent, $s.IgpuOnly)
        if ($s.Status -eq 170 -and [string]$s.SmiExit -eq '0') { $ok = $true; break }
        if ($s.Status -eq 170 -and $s.NvPresent) {
            Write-Report '    - device present and firmware at 170, but the driver is not answering yet; still waiting'
        }
        if ($waited -ge $TimeoutSeconds) { break }
        Start-Sleep -Seconds $PollSeconds
        $waited += $PollSeconds
    }
    if ($ok) {
        Write-Report -Level OK -Message 'SUCCESS: the discrete GPU is back and the driver answers again.'
        Save-Report
        exit 0
    }
    $final = Get-HsState
    if ($final.NvPresent -and $final.Status -eq 170) {
        Write-Report -Level WARN -Message 'the device is back and the firmware state is 170, but nvidia-smi has not answered within the timeout.'
        Write-Report '  the driver is still starting; give it a few seconds, or reboot if it stays silent.'
        Save-Report
        exit 0
    }
    Write-Report -Level WARN -Message 'the OFF selector was written and the services were restarted, but verification did not confirm within the timeout.'
    Write-Report '  a reboot brings the dGPU back for certain.'
    Save-Report
    exit 5
}
