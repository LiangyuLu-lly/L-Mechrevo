<#
.SYNOPSIS
    Read-only probe of the vendor UEFI NVRAM state for the dGPU / display route (GCU-unpacked clue #1).
    Does NOT use GCU, MQTT or any vendor DLL - it calls the documented Win32 firmware API directly.

.DESCRIPTION
    The vendor service (GCUService) stores the persistent display route ("dGPU direct connect" vs
    "iGPU only" / MUX) in UEFI NVRAM variables handled by its UEFI_Firmware.dll, which internally
    calls Get/SetFirmwareEnvironmentVariableW with the GUID below:

        GUID  {9f33f85c-13ca-4fd1-9c4a-96217722c593}
        50-gen DLL variable names: UniWillVariable, OemMagicVariable, OemMagicDoor, OemDgpuPresent
        30/40-gen DLL variable names: UniWillVariable only

    Evidence (verified by scanning the shipped DLL bytes, see GCUService_unpacked analysis):
      - installer-src\files\GCU\payload\GCU\AiStoneService\UEFI_Firmware.dll   (193760 B)
      - installer-src\files\GCU\payload\{30,40,50}\...\UEFI_Firmware.dll
      - NVRAM_STRUCT layout: MyControlCenter\NVRAM_STRUCT.cs (OemDisplayMode is byte offset 98)
      - OemDisplayMode writers: MySettingManager.cs:2023/2033/2043 (AMD 1/0/2), 2056/2066/2076 (Intel 2/4/1)

    This script only READS. It never calls SetFirmwareEnvironmentVariable, and the ACPI-WMI section
    only sends the vendor's two read queries (cmd=3 addr=2 status, addr=3 support) - never the
    write values (addr=0/1).

    Why the offsets are trustworthy: everything before OemDisplayMode is naturally aligned, so the
    packed and aligned layouts agree up to that field. The script also self-checks the layout by
    comparing the decoded ProjectID / APVersionCheck against what GCU itself wrote into the registry
    (HKLM\SOFTWARE\OEM\GamingCenter2\...), so you can see whether the offsets hold on this machine.

.PARAMETER LogPath
    Where to write the report. Default: %ProgramData%\DgpuSilence\logs\gcu-nvram_<timestamp>.log
    (the report is appended, so repeated runs accumulate in one file).

.PARAMETER NoLog
    Do not write any file; print to the console only.

.PARAMETER RunAsSystem
    Reading UEFI variables needs SeSystemEnvironmentPrivilege, which an ELEVATED ADMINISTRATOR token
    does not hold (observed on a real machine: the read returns win32=1314, then
    AdjustTokenPrivileges returns win32=1300 = "not assigned to this token"). The vendor's service
    can write those variables only because GCU runs as SYSTEM (its scheduled task CC.xml uses
    S-1-5-18 / HighestAvailable). With this switch the probe re-runs itself as SYSTEM through a
    one-shot scheduled task (create, run, delete) and prints the log that instance produced.
    Creating the task needs an elevated shell. Opt-in on purpose: it creates a temporary SYSTEM task.

.PARAMETER SystemRun
    Internal marker set on the SYSTEM instance so it does not re-launch itself. Not for manual use.

.PARAMETER Measure
    Take a read-only snapshot of everything that matters for "what does switching the display route
    actually do to the dGPU" - PnP inventory, nvidia-smi (incl. power draw), EC bytes, ACPI
    support/status, NVRAM display route - and print it. Combine with -CompareTo to get a before/after
    table. Nothing is written to hardware: this mode only issues the vendor's ec-read (cmd=1) and
    iGPU-only read queries (cmd=3 addr=2/3).

.PARAMETER SnapshotPath
    Where to save the snapshot as JSON. Default: %ProgramData%\DgpuSilence\snapshots\snapshot_<stamp>.json

.PARAMETER CompareTo
    Path of an earlier snapshot; the current state is compared against it and printed as a
    before/after table. Implies -Measure.

.PARAMETER CaptureSwitch
    One run instead of two: take a "before" snapshot, wait for you to perform the switch (the
    vendor console, or your own tool), then take an "after" snapshot and print the comparison,
    the verdict and the transition timeline. Needs an interactive elevated console, so it cannot
    be combined with -RunAsSystem.

.PARAMETER AutoWait
    With -CaptureSwitch: instead of waiting for Enter, poll every -PollSeconds until one of the
    tracked signals changes (iGPU-only registry value, NVIDIA presence, EC[1989] bit 5,
    nvidia-smi exit code) or -WaitSeconds elapses, and print the transition timeline.

.PARAMETER WaitSeconds
    AutoWait timeout. Default 180.

.PARAMETER PollSeconds
    AutoWait polling interval. Default 3.

.PARAMETER FollowSeconds
    After the first change is detected, keep sampling this long before the "after" snapshot is
    taken, so the comparison reflects the settled state rather than ~6 seconds after the flip.
    0 disables the follow-up window. Default 120 (the vendor's own poll loop also allows 120s).

.EXAMPLE
    # On the machine that supports the hot switch (support = 85): snapshot, switch, snapshot, diff
    .\Get-GcuNvramState.ps1 -RunAsSystem -Measure                      # before
    .\Get-GcuNvramState.ps1 -RunAsSystem -Measure -CompareTo <before.json>   # after + table

.EXAMPLE
    # Elevated PowerShell: read the firmware state and the hot-switch capability
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Get-GcuNvramState.ps1

.NOTES
    Exit codes: 0 = at least one variable was read; 1 = unexpected error; 2 = nothing could be read
    (not elevated, or the machine did not boot in UEFI mode).
    Keep this file ASCII-only: Windows PowerShell 5.1 reads a BOM-less .ps1 with the ANSI code page.
#>
[CmdletBinding()]
param(
    [string]$LogPath,
    [switch]$NoLog,
    [switch]$RunAsSystem,
    [switch]$SystemRun,
    [switch]$Measure,
    [string]$SnapshotPath,
    [string]$CompareTo,
    [switch]$CaptureSwitch,
    [switch]$AutoWait,
    [int]$WaitSeconds = 180,
    [int]$PollSeconds = 3,
    [int]$FollowSeconds = 120,
    [int]$EcEvery = 10,
    [switch]$MarkBaseline
)

$ErrorActionPreference = 'Continue'

# Arguments forwarded to the SYSTEM instance, so the "before" and "after" snapshots are taken by
# the same identity (otherwise the NVRAM fields would be missing from one of them).
$script:SystemPassThrough = ''
if ($Measure -or $CompareTo) { $script:SystemPassThrough += ' -Measure' }
if ($SnapshotPath) { $script:SystemPassThrough += (' -SnapshotPath "{0}"' -f $SnapshotPath) }
if ($CompareTo) { $script:SystemPassThrough += (' -CompareTo "{0}"' -f $CompareTo) }

# ---------------------------------------------------------------------------------------
# Constants recovered from the vendor binaries / decompiled source
# ---------------------------------------------------------------------------------------
$script:VendorGuid = '{9f33f85c-13ca-4fd1-9c4a-96217722c593}'
$script:VariableNames = @('UniWillVariable', 'OemDgpuPresent', 'OemMagicVariable', 'OemMagicDoor')
$script:ErrorEnvVarNotFound = 203
$script:ErrorPrivilegeNotHeld = 1314
$script:ErrorInsufficientBuffer = 122

# NVRAM_STRUCT field offsets (NVRAM_STRUCT.cs).
# Up to and including OemDisplayMode@98 packed and natural alignment agree, so those are safe
# either way. BEYOND that the struct is NATURALLY ALIGNED - verified on a 50-gen machine: the
# uint AudioCodecID lands on 104 (pad byte at 103) and decodes to a Realtek id 0x10EC0274.
$script:FieldOffsets = [ordered]@{
    OemBoardSsid                          = @{ Offset = 0;   Size = 4; Kind = 'uint' }
    SupportByte                           = @{ Offset = 4;   Size = 2; Kind = 'ushort' }
    ProjectID                             = @{ Offset = 6;   Size = 1; Kind = 'byte' }
    KeyboardType                          = @{ Offset = 7;   Size = 1; Kind = 'byte' }
    CustomerList                          = @{ Offset = 8;   Size = 1; Kind = 'byte' }
    PowerMode                             = @{ Offset = 47;  Size = 1; Kind = 'byte' }
    APVersionCheck                        = @{ Offset = 67;  Size = 1; Kind = 'byte' }
    OverClockRecoveryFlag                 = @{ Offset = 92;  Size = 1; Kind = 'byte' }
    ApExistFlag                           = @{ Offset = 93;  Size = 1; Kind = 'byte' }
    ACRecoverySupport                     = @{ Offset = 94;  Size = 1; Kind = 'byte' }
    ACRecoveryStatus                      = @{ Offset = 95;  Size = 1; Kind = 'byte' }
    MemoryOverClockSupport                = @{ Offset = 96;  Size = 1; Kind = 'byte' }
    ApUseFlag                             = @{ Offset = 97;  Size = 1; Kind = 'byte' }
    OemDisplayMode                        = @{ Offset = 98;  Size = 1; Kind = 'byte' }
    FnKeyStatus                           = @{ Offset = 102; Size = 1; Kind = 'byte' }
    AudioCodecID                          = @{ Offset = 104; Size = 4; Kind = 'uint' }
    CPUPerformanceAndOverClockMenuSwitch  = @{ Offset = 111; Size = 1; Kind = 'byte' }
}

$script:Report = New-Object System.Collections.ArrayList

function Write-Report {
    param([string]$Message, [string]$Level = 'INFO')
    $line = $Message
    switch ($Level) {
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
        'OK'    { Write-Host $line -ForegroundColor Green }
        'HEAD'  { Write-Host $line -ForegroundColor Cyan }
        default { Write-Host $line }
    }
    [void]$script:Report.Add($line)
}

function Test-Elevated {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsSystem {
    <# IsInRole(Administrator) is FALSE for a SYSTEM token, so SYSTEM needs its own test. #>
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    if ($id.User -and $id.User.Value -eq 'S-1-5-18') { return $true }
    return ($id.Name -match '\\SYSTEM$')
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @())
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $FilePath @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out.Trim() }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Invoke-ProbeAsSystem {
    <#
      Runs this same script as SYSTEM through a one-shot scheduled task, then prints the log that
      instance wrote. This mirrors the vendor: GCU's bridge runs as S-1-5-18 (Command\CC.xml,
      HighestAvailable), which is exactly why it can touch the firmware variables at all.
    #>
    param([string]$LogFile)

    $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $baseDir = Join-Path $env:ProgramData 'DgpuSilence'
    if (-not (Test-Path -LiteralPath $baseDir)) { New-Item -ItemType Directory -Path $baseDir -Force | Out-Null }
    $cmdFile = Join-Path $baseDir 'run-probe-as-system.cmd'
    $taskName = 'DgpuSilenceNvramProbe'

    if (Test-Path -LiteralPath $LogFile) { Remove-Item -LiteralPath $LogFile -Force -ErrorAction SilentlyContinue }

    $content = @(
        '@echo off',
        ('"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}" -SystemRun -LogPath "{2}"{3}' -f $psExe, $PSCommandPath, $LogFile, $script:SystemPassThrough)
    )
    Set-Content -LiteralPath $cmdFile -Value $content -Encoding ASCII

    $tr = 'cmd.exe /c {0}' -f $cmdFile
    Write-Report ("SYSTEM task  : {0}" -f $taskName)
    Write-Report ("task action  : {0}" -f $tr)
    Write-Report ("log          : {0}" -f $LogFile)

    $create = Invoke-Native -FilePath 'schtasks.exe' -Arguments @('/create', '/f', '/tn', $taskName, '/tr', $tr, '/sc', 'once', '/st', '23:59', '/ru', 'SYSTEM', '/rl', 'HIGHEST')
    Write-Report ("schtasks /create -> exit {0}" -f $create.ExitCode)
    if ($create.ExitCode -ne 0) {
        $reason = ($create.Output -split "`r?`n" | Where-Object { $_ -match 'ERROR|denied' } | Select-Object -First 1)
        if (-not $reason) { $reason = $create.Output }
        Write-Report -Level WARN -Message ("  {0}" -f $reason)
        Write-Report -Level WARN -Message 'Could not create the SYSTEM task (needs an elevated shell). Run this by hand instead:'
        Write-Report ("  schtasks /create /f /tn {0} /tr ""{1}"" /sc once /st 23:59 /ru SYSTEM /rl HIGHEST" -f $taskName, $tr)
        Write-Report ("  schtasks /run /tn {0}    then read {1}" -f $taskName, $LogFile)
        Remove-Item -LiteralPath $cmdFile -Force -ErrorAction SilentlyContinue
        return 3
    }

    try {
        $run = Invoke-Native -FilePath 'schtasks.exe' -Arguments @('/run', '/tn', $taskName)
        Write-Report ("schtasks /run -> exit {0}" -f $run.ExitCode)
        $waited = 0
        while (-not (Test-Path -LiteralPath $LogFile) -and $waited -lt 30) {
            Start-Sleep -Seconds 1
            $waited++
        }
        if (Test-Path -LiteralPath $LogFile) {
            Write-Report ("SYSTEM instance finished after ~{0}s; its report follows:" -f $waited)
            Write-Report '---------------------------------------------------------------'
            foreach ($line in (Get-Content -LiteralPath $LogFile)) { Write-Report $line }
            Write-Report '---------------------------------------------------------------'
            return 0
        }
        Write-Report -Level WARN -Message 'The SYSTEM instance produced no log within 30s.'
        return 3
    } finally {
        $delete = Invoke-Native -FilePath 'schtasks.exe' -Arguments @('/delete', '/f', '/tn', $taskName)
        Write-Report ("schtasks /delete -> exit {0}" -f $delete.ExitCode)
        Remove-Item -LiteralPath $cmdFile -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------
# Interop: firmware variables + the privilege the vendor DLL enables
# ---------------------------------------------------------------------------------------
if (-not ('GcuNvramProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ProbeLuid { public uint LowPart; public int HighPart; }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ProbeLuidAndAttributes { public uint LowPart; public int HighPart; public uint Attributes; }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ProbeTokenPrivileges { public uint PrivilegeCount; public ProbeLuidAndAttributes Privileges; }

public static class GcuNvramProbe
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetFirmwareEnvironmentVariableW(string lpName, string lpGuid, byte[] pBuffer, uint nSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out ProbeLuid lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        IntPtr NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeNameW(string lpSystemName, ref ProbeLuid lpLuid,
        System.Text.StringBuilder lpName, ref uint cchName);
}
'@
}

function Enable-SystemEnvironmentPrivilege {
    <# The vendor DLL enables this before touching firmware variables. Reading may work
       without it, so the probe tries a plain read first and only calls this if needed. #>
    $TOKEN_ADJUST_PRIVILEGES = 0x0020
    $TOKEN_QUERY = 0x0008
    $SE_PRIVILEGE_ENABLED = 0x00000002

    $token = [IntPtr]::Zero
    $buf = [IntPtr]::Zero
    try {
        if (-not [GcuNvramProbe]::OpenProcessToken([GcuNvramProbe]::GetCurrentProcess(),
                ($TOKEN_ADJUST_PRIVILEGES -bor $TOKEN_QUERY), [ref]$token)) {
            return [pscustomobject]@{ Ok = $false; Why = ("OpenProcessToken failed, win32={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
        }
        $luid = New-Object ProbeLuid
        if (-not [GcuNvramProbe]::LookupPrivilegeValueW($null, 'SeSystemEnvironmentPrivilege', [ref]$luid)) {
            return [pscustomobject]@{ Ok = $false; Why = ("LookupPrivilegeValue failed, win32={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
        }

        # Build TOKEN_PRIVILEGES by hand: DWORD count + LUID(8) + DWORD attributes = 16 bytes (Pack=4).
        # Doing this with PowerShell struct properties silently produced a broken LUID: the call then
        # returned TRUE with ERROR_NOT_ALL_ASSIGNED and the privilege stayed disabled (verified with
        # the token diagnostic on a real machine). Raw memory removes the marshalling question.
        $buf = [Runtime.InteropServices.Marshal]::AllocHGlobal(16)
        [Runtime.InteropServices.Marshal]::WriteInt32($buf, 0, 1)
        [Runtime.InteropServices.Marshal]::WriteInt32($buf, 4, [int]$luid.LowPart)
        [Runtime.InteropServices.Marshal]::WriteInt32($buf, 8, [int]$luid.HighPart)
        [Runtime.InteropServices.Marshal]::WriteInt32($buf, 12, [int]$SE_PRIVILEGE_ENABLED)

        if (-not [GcuNvramProbe]::AdjustTokenPrivileges($token, $false, $buf, [uint32]16, [IntPtr]::Zero, [IntPtr]::Zero)) {
            return [pscustomobject]@{ Ok = $false; Why = ("AdjustTokenPrivileges failed, win32={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
        }
        # A TRUE return only means "the call went through"; whether it took effect is checked by the
        # caller (token diagnostic + an actual retry of the read), never by GetLastWin32Error alone.
        return [pscustomobject]@{ Ok = $true; Why = ("AdjustTokenPrivileges returned TRUE for LUID 0x{0:X8}{1:X8} (caller verifies)" -f $luid.HighPart, $luid.LowPart) }
    } catch {
        return [pscustomobject]@{ Ok = $false; Why = $_.Exception.Message }
    } finally {
        if ($buf -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::FreeHGlobal($buf) }
        if ($token -ne [IntPtr]::Zero) { [void][GcuNvramProbe]::CloseHandle($token) }
    }
}

function Show-TokenPrivilegeDiagnostic {
    <#
      Explains WHY the firmware read failed, which the error code alone cannot:
        - "NOT in this token"          -> the right is not granted to this identity at all
                                          (LSA policy; grant it with secpol.msc to Administrators/SYSTEM,
                                          or read the variables through a process that already has it).
        - "PRESENT, enabled=False"     -> enabling it is the fix, so a failed enable is the real bug.
      Enumerates the token privileges via GetTokenInformation(TokenPrivileges) and matches the LUID
      of SeSystemEnvironmentPrivilege.
    #>
    $TOKEN_QUERY = 0x0008
    $SE_PRIVILEGE_ENABLED = 0x00000002
    $token = [IntPtr]::Zero
    $buf = [IntPtr]::Zero
    try {
        if (-not [GcuNvramProbe]::OpenProcessToken([GcuNvramProbe]::GetCurrentProcess(), [uint32]$TOKEN_QUERY, [ref]$token)) {
            Write-Report -Level WARN -Message ("    token query: OpenProcessToken failed, win32={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error())
            return
        }
        $len = [uint32]0
        [void][GcuNvramProbe]::GetTokenInformation($token, 3, [IntPtr]::Zero, 0, [ref]$len)
        if ($len -eq 0) {
            Write-Report -Level WARN -Message '    token query: no privilege information returned'
            return
        }
        $buf = [Runtime.InteropServices.Marshal]::AllocHGlobal([int]$len)
        if (-not [GcuNvramProbe]::GetTokenInformation($token, 3, $buf, $len, [ref]$len)) {
            Write-Report -Level WARN -Message ("    token query: GetTokenInformation failed, win32={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error())
            return
        }
        $count = [Runtime.InteropServices.Marshal]::ReadInt32($buf, 0)
        $found = $false
        for ($i = 0; $i -lt $count; $i++) {
            $base = 4 + ($i * 12)   # TOKEN_PRIVILEGES: DWORD count, then LUID(8)+DWORD(4) per entry, Pack=4
            $luid = New-Object ProbeLuid
            $luid.LowPart = [uint32][Runtime.InteropServices.Marshal]::ReadInt32($buf, $base)
            $luid.HighPart = [Runtime.InteropServices.Marshal]::ReadInt32($buf, $base + 4)
            $attr = [Runtime.InteropServices.Marshal]::ReadInt32($buf, $base + 8)
            $sb = New-Object System.Text.StringBuilder 128
            $cch = [uint32]128
            if ([GcuNvramProbe]::LookupPrivilegeNameW($null, [ref]$luid, $sb, [ref]$cch)) {
                if ($sb.ToString() -eq 'SeSystemEnvironmentPrivilege') {
                    $found = $true
                    $enabled = (($attr -band $SE_PRIVILEGE_ENABLED) -ne 0)
                    Write-Report ("    token privilege: SeSystemEnvironmentPrivilege PRESENT, enabled={0} (attributes=0x{1:X}, LUID=0x{2:X8}{3:X8})" -f $enabled, $attr, $luid.HighPart, $luid.LowPart)
                }
            }
        }
        if (-not $found) {
            Write-Report -Level WARN -Message ("    token privilege: SeSystemEnvironmentPrivilege is NOT in this token (checked {0} privileges)" -f $count)
        }
    } finally {
        if ($buf -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::FreeHGlobal($buf) }
        if ($token -ne [IntPtr]::Zero) { [void][GcuNvramProbe]::CloseHandle($token) }
    }
}

function Read-FirmwareVariable {
    <# Fixed generous buffer first; retry bigger on ERROR_INSUFFICIENT_BUFFER. Never writes. #>
    param([string]$Name, [int]$InitialSize = 4096)

    foreach ($size in @($InitialSize, 65536)) {
        $buffer = New-Object byte[] $size
        $read = [GcuNvramProbe]::GetFirmwareEnvironmentVariableW($Name, $script:VendorGuid, $buffer, [uint32]$size)
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($read -gt 0) {
            $data = New-Object byte[] $read
            [Array]::Copy($buffer, $data, [int]$read)
            return [pscustomobject]@{ Status = 'OK'; Name = $Name; Size = [int]$read; Data = $data; Error = 0 }
        }
        if ($err -eq $script:ErrorInsufficientBuffer -and $size -lt 65536) { continue }
        $status = 'FAILED'
        if ($err -eq $script:ErrorEnvVarNotFound) { $status = 'NOT_FOUND' }
        elseif ($err -eq $script:ErrorPrivilegeNotHeld) { $status = 'NO_PRIVILEGE' }
        return [pscustomobject]@{ Status = $status; Name = $Name; Size = 0; Data = $null; Error = $err }
    }
    return [pscustomobject]@{ Status = 'FAILED'; Name = $Name; Size = 0; Data = $null; Error = -1 }
}

function Get-HexDump {
    <# Dump the whole blob when it is small: OemDisplayMode lives at offset 98, which a 96-byte
       cap used to hide (found on a real run - the one byte that matters was not visible). #>
    param([byte[]]$Data, [int]$Max = 256)
    if (-not $Data) { return @() }
    $lines = @()
    $count = [Math]::Min($Max, $Data.Length)
    for ($i = 0; $i -lt $count; $i += 16) {
        $slice = $Data[$i..([Math]::Min($i + 15, $count - 1))]
        $hex = ($slice | ForEach-Object { $_.ToString('X2') }) -join ' '
        $ascii = -join ($slice | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
        $lines += ("    {0,4}: {1,-48} {2}" -f $i, $hex, $ascii)
    }
    if ($Data.Length -gt $Max) { $lines += ("    ... ({0} bytes total)" -f $Data.Length) }
    return $lines
}

function Get-FieldValue {
    param([byte[]]$Data, [string]$Field)
    $info = $script:FieldOffsets[$Field]
    if (-not $info) { return $null }
    if ($Data.Length -lt ($info.Offset + $info.Size)) { return $null }
    switch ($info.Kind) {
        'uint'   { return [uint32]([BitConverter]::ToUInt32($Data, $info.Offset)) }
        'ushort' { return [uint16]([BitConverter]::ToUInt16($Data, $info.Offset)) }
        default  { return [byte]$Data[$info.Offset] }
    }
}

function Get-PciSubsysId {
    <# SUBSYS_xxxxxxxx from the NVIDIA adapter's instance ID, as a uint32 - on the AMD models this
       equals the firmware blob's OemBoardSsid (offset 0), which is what made it a self-check. #>
    try {
        $dev = @(Get-PnpDevice -Class Display -ErrorAction Stop | Where-Object { $_.InstanceId -match 'VEN_10DE' } | Select-Object -First 1)
    } catch {
        return $null
    }
    if ($dev.Count -eq 0) { return $null }
    if ($dev[0].InstanceId -match 'SUBSYS_([0-9A-Fa-f]{8})') {
        try { return [uint32]('0x' + $Matches[1]) } catch { return $null }
    }
    return $null
}

function Get-AllPciSubsysIds {
    <# Every SUBSYS_xxxxxxxx on the PCI bus -> device name. The blob's OemBoardSsid is the BOARD
       subsystem id: on AMD models it happens to equal the dGPU's, but on a measured Intel model it
       does NOT (blob 0x50001D05 vs dGPU 0x60421D05). So validate against the whole set instead of
       only the NVIDIA adapter, otherwise a correct layout looks like a failed check. #>
    $map = @{}
    try {
        $devs = @(Get-PnpDevice -ErrorAction Stop)
    } catch {
        return $map
    }
    foreach ($d in $devs) {
        if ($d.InstanceId -notmatch '^PCI\\') { continue }
        if ($d.InstanceId -match 'SUBSYS_([0-9A-Fa-f]{8})') {
            $val = $Matches[1].ToUpperInvariant()
            if (-not $map.ContainsKey($val)) { $map[$val] = $d.FriendlyName }
        }
    }
    return $map
}

function Get-PlatformKind {
    <# Distinguishes the two OemDisplayMode encodings: AMD (1/0/2) vs Intel (2/4/1). #>
    try {
        $gpus = @(Get-PnpDevice -Class Display -ErrorAction Stop | Where-Object { $_.Status -eq 'OK' })
    } catch {
        return 'unknown'
    }
    $others = @($gpus | Where-Object { $_.InstanceId -notmatch 'VEN_10DE' -and $_.FriendlyName -notmatch 'NVIDIA' })
    foreach ($g in $others) {
        if ($g.FriendlyName -match 'AMD|Radeon') { return 'AMD' }
        if ($g.FriendlyName -match 'Intel|Iris|UHD') { return 'Intel' }
    }
    return 'unknown'
}

function Get-OemDisplayModeText {
    param([int]$Value, [string]$Platform)
    if ($Value -eq 255) { return '255 = NotSupport (this platform does not offer the route switch)' }
    if ($Platform -eq 'AMD') {
        switch ($Value) {
            1 { return '1 = DGPU_DIRECT_CONNECT_TOGGLE_ON  (dGPU direct connect / MUX to dGPU)' }
            0 { return '0 = DGPU_DIRECT_CONNECT_TOGGLE_OFF (hybrid: iGPU drives the panel)' }
            2 { return '2 = DGPU_DIRECT_CONNECT_TOGGLE_IGPU (iGPU only)' }
        }
    } elseif ($Platform -eq 'Intel') {
        switch ($Value) {
            2 { return '2 = DGPU_DIRECT_CONNECT_TOGGLE_ON  (Intel encoding)' }
            4 { return '4 = DGPU_DIRECT_CONNECT_TOGGLE_OFF (Intel encoding)' }
            1 { return '1 = DGPU_DIRECT_CONNECT_TOGGLE_IGPU (Intel encoding)' }
        }
    }
    return ("{0} = (no mapping for platform '{1}'; raw value shown)" -f $Value, $Platform)
}

function Show-RegistryMirror {
    <# Values GCU itself writes. Two groups:
         - support/capability flags under ...\Support and ...\ItemSupport
         - the LIVE display-route state under ...\MySetting ("IGPUonly": 1 = iGPU-only ON,
           0 = OFF, 2 = AUTO; written at MySettingManager.cs:2198/2215/2222, read at :2266)
           plus ...\MySetting\GpuConfig\lgpuHotSwapSwitchStatus.
       The MySetting values are semantic and model-independent, so they cross-check the
       NVRAM blob decode without relying on any struct offset.
       -Quiet suppresses the report lines but still returns the values. #>
    param([switch]$Quiet)
    $paths = @(
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\Support', 'APVersionCheck'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport', 'IsNvGpu'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport', 'DGpuDirectConnectionSupport'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport', 'IDR2Check'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport', 'DispalyRefresh'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting', 'IGPUonly'),
        @('HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting\GpuConfig', 'lgpuHotSwapSwitchStatus')
    )
    $result = [ordered]@{}
    foreach ($pair in $paths) {
        try {
            $v = (Get-ItemProperty -Path $pair[0] -Name $pair[1] -ErrorAction Stop).($pair[1])
            $result[$pair[1]] = $v
            if (-not $Quiet) {
                Write-Report ("    {0,-28} = {1}" -f $pair[1], $v)
                if ($pair[1] -eq 'IGPUonly') {
                    $ig = [int]$v
                    $igText = ("{0} = (unmapped value)" -f $ig)
                    if ($ig -eq 1) { $igText = '1 = iGPU-only ON (dGPU switched away)' }
                    elseif ($ig -eq 0) { $igText = '0 = iGPU-only OFF' }
                    elseif ($ig -eq 2) { $igText = '2 = AUTO' }
                    Write-Report ("    {0,-28}   -> live iGPU-only state: {1}" -f '', $igText)
                }
            }
        } catch {
            if (-not $Quiet) { Write-Report ("    {0,-28} = <absent>" -f $pair[1]) }
        }
    }
    return $result
}

function Get-AcpiTargetInstance {
    <# The CIM instance of the OS ACPI-WMI mapper the vendor talks to, or $null.
       GCU uses ACPI\PNP0C14\1_1; fall back to the first instance present. #>
    try {
        $instances = @(Get-CimInstance -Namespace 'root\WMI' -ClassName 'AcpiTest_MULong' -ErrorAction Stop)
    } catch {
        return $null
    }
    if ($instances.Count -eq 0) { return $null }
    $target = $instances | Where-Object { $_.InstanceName -like '*PNP0C14\1_1*' } | Select-Object -First 1
    if (-not $target) { $target = $instances[0] }
    return $target
}

function Invoke-AcpiGetSetULong {
    <# Returns @{ Value; Error }. Tries the CIM cmdlet first, then the older WMI cmdlet: they take
       different code paths into the mapper, and a failure must be REPORTED, not swallowed (an
       earlier version returned $null and the caller then blamed "mapper not reachable" while the
       cmd=3 queries had just worked). #>
    param($Instance, [uint64]$Data)
    $errors = @()
    try {
        $r = Invoke-CimMethod -InputObject $Instance -MethodName 'GetSetULong' -Arguments @{ Data = $Data } -ErrorAction Stop
        if ($null -ne $r -and $null -ne $r.Return) { return [pscustomobject]@{ Value = [uint64]$r.Return; Error = '' } }
        $errors += 'Invoke-CimMethod returned no Return value'
    } catch {
        $errors += ('Invoke-CimMethod: ' + $_.Exception.Message)
    }
    try {
        $rw = Invoke-WmiMethod -Namespace 'root\WMI' -Class 'AcpiTest_MULong' -Name 'GetSetULong' -ArgumentList @($Data) -ErrorAction Stop
        if ($null -ne $rw -and $null -ne $rw.Return) { return [pscustomobject]@{ Value = [uint64]$rw.Return; Error = '' } }
        $errors += 'Invoke-WmiMethod returned no Return value'
    } catch {
        $errors += ('Invoke-WmiMethod: ' + $_.Exception.Message)
    }
    return [pscustomobject]@{ Value = $null; Error = ($errors -join ' | ') }
}

function Get-EcByte {
    <# EC read through the same mapper. WMIEC.WMIReadECRAM packs Data = 0x1_0000_0000_00 + Addr
       (verified verbatim at WMIEC.cs:538) and WMIEC.WMIWriteECRAM packs (value << 16) + Addr
       (WMIEC.cs:572-573). This function only ever uses the READ form.
       Returns @{ Value; Error }: Value is the low byte, or $null with Error explaining why. #>
    param($Instance, [int]$Address)
    $data = [uint64]1099511627776 + [uint64]$Address
    $r = Invoke-AcpiGetSetULong -Instance $Instance -Data $data
    if ($null -eq $r.Value) { return [pscustomobject]@{ Value = $null; Error = $r.Error } }
    return [pscustomobject]@{ Value = [int]($r.Value -band 0xFF); Error = '' }
}

function Get-IgpuOnlyAcpiState {
    <# READ-ONLY ACPI-WMI queries on the OS ACPI-WMI mapper (ACPI\PNP0C14):
         cmd=3 addr=3 -> WMIGetECIGPUonlySupport     (85 = supported)
         cmd=3 addr=2 -> WMIGetECDGPUStatusForIGPUonly
       The write values (addr=0/1) are deliberately NOT sent by this script.
       -Quiet suppresses the report lines but still returns the state. #>
    param([switch]$Quiet)
    $result = [ordered]@{ Available = $false; Instances = @(); Support = $null; Status = $null; Note = ''; Target = $null }
    try {
        $instances = @(Get-CimInstance -Namespace 'root\WMI' -ClassName 'AcpiTest_MULong' -ErrorAction Stop)
    } catch {
        $result.Note = ("ACPI-WMI mapper not reachable: {0}" -f $_.Exception.Message)
        return $result
    }
    if ($instances.Count -eq 0) {
        $result.Note = 'AcpiTest_MULong has no instance on this machine (no ACPI-WMI mapper / not UEFI)'
        return $result
    }
    $result.Available = $true
    $result.Instances = @($instances | ForEach-Object { $_.InstanceName })

    # Prefer the vendor's instance ACPI\PNP0C14\1_1, else the first one
    $target = $instances | Where-Object { $_.InstanceName -like '*PNP0C14\1_1*' } | Select-Object -First 1
    if (-not $target) { $target = $instances[0] }
    $result.Target = $target
    if (-not $Quiet) { Write-Report ("    using instance: {0}" -f $target.InstanceName) }

    foreach ($probe in @(@('support (cmd=3 addr=3)', [uint64]0x30000000003, 'Support'),
                         @('dGPU status (cmd=3 addr=2)', [uint64]0x30000000002, 'Status'),
                         @('battery logo support (cmd=3 addr=4)', [uint64]0x30000000004, 'BatteryLogo'))) {
        $r = Invoke-AcpiGetSetULong -Instance $target -Data $probe[1]
        $val = $null
        if ($null -ne $r.Value) { $val = [int]($r.Value -band 0xFF) }
        $result[$probe[2]] = $val
        if (-not $Quiet) {
            if ($null -eq $val) {
                Write-Report -Level WARN -Message ("    {0,-36} -> failed: {1}" -f $probe[0], $r.Error)
            } else {
                Write-Report ("    {0,-36} -> {1}" -f $probe[0], $val)
            }
        }
    }
    # Measured meaning of the iGPU-only STATUS query (cmd=3 addr=2): 170 = iGPU-only NOT engaged,
    # 85 = iGPU-only ACTIVE. A real report showed it flip 170 -> 85 exactly when the dGPU went away
    # (nvidia-smi exit 0 -> 9, device no longer present), so this is the firmware-side success bit.
    if ($probe[2] -eq 'Status' -and $null -ne $result.Status) {
        if (-not $Quiet) {
            if ($result.Status -eq 85) { Write-Report -Level OK -Message '    -> 85 means the iGPU-only state is ACTIVE (the dGPU has been switched away)' }
            elseif ($result.Status -eq 170) { Write-Report '    -> 170 means the iGPU-only state is NOT engaged' }
        }
    }
    return $result
}

function Get-EcSnapshot {
    <# EC bytes that matter for this investigation. 1989 bit5 is the vendor's own panel-signal /
       readiness check (GPUDeviceItem.GetMonitorSignalfromEC, polled after an iGPU-only switch);
       2002 bits0-4 is the legacy "does this SKU have a dGPU" read used when APVersionCheck < 25;
       1834 is ADDR_GPU_STATUS (Define\ECSpec.cs:462). #>
    param($Instance)
    $result = [ordered]@{ '1989' = $null; '1989_bit5' = $null; '2002' = $null; '2002_low5' = $null; '1834' = $null; 'error' = '' }
    if (-not $Instance) { $result['error'] = 'no ACPI-WMI instance'; return $result }
    $errors = @()
    foreach ($pair in @(@(1989, '1989', '1989_bit5', 0x20, $false), @(2002, '2002', '2002_low5', 0x1F, $true), @(1834, '1834', $null, $null, $false))) {
        $r = Get-EcByte -Instance $Instance -Address $pair[0]
        if ($null -eq $r.Value) {
            $errors += ("EC[{0}]: {1}" -f $pair[0], $r.Error)
            continue
        }
        $result[$pair[1]] = $r.Value
        if ($pair[2]) {
            if ($pair[4]) {
                # a 5-bit field (module id / dGPU presence), not a flag
                $result[$pair[2]] = ($r.Value -band $pair[3])
            } else {
                $result[$pair[2]] = (($r.Value -band $pair[3]) -ne 0)
            }
        }
    }
    $result['error'] = ($errors -join ' | ')
    return $result
}

function Show-EcSnapshot {
    param($Ec, [int]$ApVersionCheck)
    $any = ($null -ne $Ec['1989']) -or ($null -ne $Ec['2002']) -or ($null -ne $Ec['1834'])
    if (-not $any) {
        $why = $Ec['error']
        if (-not $why) { $why = 'no error reported' }
        Write-Report -Level WARN -Message ("    EC reads returned nothing ({0})" -f $why)
        return
    }
    # Report whatever worked: one failing address must not hide the others.
    if ($null -ne $Ec['1989']) {
        Write-Report ("    EC[1989] = 0x{0:X2}  bit5(panel signal)={1}" -f [int]$Ec['1989'], $Ec['1989_bit5'])
    } else {
        Write-Report -Level WARN -Message '    EC[1989] unavailable (panel signal / vendor readiness bit)'
    }
    if ($null -ne $Ec['2002']) {
        Write-Report ("    EC[2002] = 0x{0:X2}  bits0-4(module/dGPU id)={1}" -f [int]$Ec['2002'], $Ec['2002_low5'])
    } else {
        Write-Report -Level WARN -Message '    EC[2002] unavailable (legacy dGPU-presence read)'
    }
    if ($null -ne $Ec['1834']) {
        Write-Report ("    EC[1834] = 0x{0:X2}  (ADDR_GPU_STATUS)" -f [int]$Ec['1834'])
    } else {
        Write-Report -Message '    EC[1834] unavailable (ADDR_GPU_STATUS)'
    }
    if ($Ec['error']) {
        Write-Report -Level WARN -Message ("    partial failures: {0}" -f $Ec['error'])
    }
    # Anchor plausibility. 0xFF is the vendor's own "unsupported / uninitialised" convention
    # (e.g. IsSupportLiquidCoolingHWOC requires != 255 && > 0), so 0xFF on an anchor is a real red
    # flag. A single 0x00 is NOT: EC[1989] legitimately reads 0x00 on a machine whose EC[2002]
    # cross-check passes, so only "all anchors zero" indicates a broken read path.
    $anchorKeys = @('1989', '2002', '1834')
    $zeroCount = 0
    foreach ($key in $anchorKeys) {
        if ($null -eq $Ec[$key]) { continue }
        if ($Ec[$key] -eq 0xFF) {
            Write-Report -Level WARN -Message ("    anchor EC[{0}] = 0xFF - the vendor treats 255 as 'unsupported/uninitialised'; the read path may be invalid on this model" -f $key)
        }
        if ($Ec[$key] -eq 0x00) { $zeroCount++ }
    }
    if ($zeroCount -eq 3) {
        Write-Report -Level WARN -Message '    all three anchors read 0x00 - the EC read path is probably not valid on this model (ignore the values below)'
    }
    if ($null -ne $ApVersionCheck) {
        if ($ApVersionCheck -lt 25 -and $null -ne $Ec['2002_low5']) {
            # MyEcCtrl.GetNvGpu(): APVersionCheck < 25 -> reads EC 2002 bits0-4 -> "has NV GPU"
            $byEc = ($Ec['2002_low5'] -gt 0)
            Write-Report ("    cross-check: APVersionCheck={0} (<25) so GetNvGpu() uses EC[2002] -> 'dGPU present' = {1} (must agree with registry IsNvGpu)" -f $ApVersionCheck, $byEc)
        } else {
            # >= 25: GCU itself stops trusting EC 2002/1834 and reads the NVRAM OemDgpuPresent flag
            # instead (MyEcCtrl.cs:386-431), so EC[2002] is NOT a valid cross-check on this machine.
            Write-Report ("    note: APVersionCheck={0} (>=25) so GetNvGpu() uses the NVRAM OemDgpuPresent flag, NOT EC[2002] - do not use EC[2002] as a cross-check here" -f $ApVersionCheck)
        }
    }
}

# =======================================================================================
# Measure mode: read-only snapshot + before/after comparison
# =======================================================================================
function Get-NvidiaInventoryLite {
    <# Present vs phantom matters more than anything else here: an iGPU-only switch or a PnP
       disable can leave the device NODE in the PnP tree while the device itself is no longer
       present. Get-PnpDevice without -PresentOnly returns those phantoms (measured on a dev
       machine: 539 devices, 271 of them non-present), and a real report then claimed
       "nvidia_present=True" for a GPU that the owner could no longer see in Device Manager. #>
    $all = @()
    try { $all = @(Get-PnpDevice -Class Display -ErrorAction Stop) } catch { $all = @() }
    $nvidia = @($all | Where-Object { $_.InstanceId -match 'VEN_10DE' -or $_.FriendlyName -match 'NVIDIA' })
    $present = @($nvidia | Where-Object { $_.Present })
    $phantom = @($nvidia | Where-Object { -not $_.Present })
    $parts = @()
    foreach ($d in $nvidia) {
        $code = ''
        try {
            $p = Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop
            if ($null -ne $p -and $null -ne $p.Data) { $code = [string]$p.Data }
        } catch { }
        $where = 'present'
        if (-not $d.Present) { $where = 'PHANTOM/not present' }
        $parts += ("{0}[{1}/code {2}/{3}]" -f $d.FriendlyName, $d.Status, $code, $where)
    }
    return [pscustomobject]@{
        Present           = ($present.Count -gt 0)
        Count             = $present.Count
        PhantomCount      = $phantom.Count
        Devices           = ($parts -join '; ')
        AllDisplayDevices = @($all | Where-Object { $_.Present }).Count
        AllDevices        = ((@($all | Where-Object { $_.Present } | ForEach-Object { $_.FriendlyName }) -join '; '))
    }
}

function Get-SmiSnapshot {
    $result = [ordered]@{ Exit = $null; Name = ''; PowerW = ''; PState = ''; TempC = ''; MemUsed = ''; Error = '' }
    $smi = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
    if (-not (Test-Path -LiteralPath $smi)) { $result.Error = 'nvidia-smi.exe not found'; return [pscustomobject]$result }
    $r = Invoke-Native -FilePath $smi -Arguments @('--query-gpu=name,power.draw,pstate,temperature.gpu,memory.used', '--format=csv,noheader')
    $result.Exit = $r.ExitCode
    if ($r.ExitCode -eq 0 -and $r.Output) {
        $parts = ($r.Output -split "`r?`n")[0].Split(',')
        if ($parts.Count -ge 5) {
            $result.Name = $parts[0].Trim()
            $result.PowerW = $parts[1].Trim()
            $result.PState = $parts[2].Trim()
            $result.TempC = $parts[3].Trim()
            $result.MemUsed = $parts[4].Trim()
        }
    } else {
        $err = ($r.Output -replace "`r?`n", ' ').Trim()
        if ($err.Length -gt 180) { $err = $err.Substring(0, 180) }
        $result.Error = $err
    }
    return [pscustomobject]$result
}

function Get-ServiceStateLite {
    param([string]$Name)
    try { return (Get-Service -Name $Name -ErrorAction Stop).Status.ToString() } catch { return 'absent' }
}

function Get-NvHdAudioPresent {
    <# The vendor refuses to switch to iGPU-only unless this is present (WMICheckNVHADstatus).
       Both sources are reported: Win32_SoundDevice (CIM) and the present-only PnP view. They can
       disagree - the same phantom-vs-present trap the dGPU inventory fell into (an Intel machine
       whose display function had gone reported nvidia_hd_audio=True, so which view said so
       matters). Returns a boolean when they agree, otherwise "cim=.. pnp_present=..". #>
    $cim = $null
    try { $cim = (@(Get-CimInstance -ClassName Win32_SoundDevice -Filter "Name like '%NVIDIA%'" -ErrorAction Stop).Count -gt 0) } catch { }
    $pnp = $null
    try {
        $p = @(Get-PnpDevice -Class MEDIA -ErrorAction Stop | Where-Object { $_.FriendlyName -match 'NVIDIA' -and $_.Present })
        $pnp = ($p.Count -gt 0)
    } catch { }
    if ($null -eq $cim -and $null -eq $pnp) { return $null }
    if ($cim -eq $pnp) { return $cim }
    return ("cim={0} pnp_present={1}" -f $cim, $pnp)
}

function Get-BlobSource {
    <# Re-read the NVRAM struct blob, choosing by generation exactly like the main flow:
       the 50-gen firmware keeps it in OemMagicVariable, the 40-gen in UniWillVariable. #>
    $best = $null
    foreach ($candidate in @('OemMagicVariable', 'UniWillVariable')) {
        $c = Read-FirmwareVariable -Name $candidate
        if ($c.Status -eq 'OK' -and $c.Size -ge 99) { return [pscustomobject]@{ Name = $candidate; Blob = $c } }
        if (-not $best) { $best = [pscustomobject]@{ Name = $candidate; Blob = $c } }
    }
    return $best
}

function Get-TransitionSignals {
    <# Signals sampled while waiting for the switch.

       IMPORTANT - keep the default sample non-invasive. The owner of a measured machine reported
       that running the capture tool makes the vendor switch fail, and the mechanism is contention:
       the vendor confirms its own switch by polling EC[1989] through its driver, while an invasive
       sample would read EC through the ACPI-WMI mapper (a single serialised interpreter) and spawn
       nvidia-smi (about 122 such spawns per capture run). So by default this touches nothing on the
       GPU or the ACPI/EC path: one registry read plus one PnP query. Pass -WithEc / -WithSmi only
       when you accept that the observation itself can break the thing being observed. #>
    param($AcpiInstance, [switch]$WithEc, [switch]$WithSmi)
    $ig = $null
    try { $ig = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\OEM\GamingCenter2\MySetting' -Name 'IGPUonly' -ErrorAction Stop).IGPUonly } catch { }
    $present = $null
    try {
        $dev = @(Get-PnpDevice -Class Display -ErrorAction Stop | Where-Object { ($_.InstanceId -match 'VEN_10DE' -or $_.FriendlyName -match 'NVIDIA') -and $_.Present })
        $present = ($dev.Count -gt 0)
    } catch { }
    $ec = $null
    if ($WithEc) { $ec = Get-EcSnapshot -Instance $AcpiInstance }
    $smi = $null
    if ($WithSmi) { $smi = Get-SmiSnapshot }
    return [ordered]@{
        t           = (Get-Date).ToString('HH:mm:ss')
        igpu_only   = $ig
        nvidia_pnp  = $present
        ec_1989_b5  = $(if ($ec) { $ec['1989_bit5'] } else { $null })
        ec_2002_low = $(if ($ec) { $ec['2002_low5'] } else { $null })
        smi_exit    = $(if ($smi) { $smi.Exit } else { $null })
    }
}

function Show-TransitionTimeline {
    param($Rows, [bool]$Detected = $false)
    Write-Report -Level HEAD -Message 'transition timeline (rows where a tracked signal changed, plus first/last):'
    Write-Report ("  {0,-10} | {1,-9} | {2,-10} | {3,-9} | {4,-11} | {5}" -f 'time', 'IGPUonly', 'nvidia_pnp', 'ec1989b5', 'ec2002_low5', 'smi_exit')
    $prev = $null
    $printed = 0
    for ($i = 0; $i -lt $Rows.Count; $i++) {
        $r = $Rows[$i]
        $changed = $true
        if ($null -ne $prev) {
            $changed = ($r.igpu_only -ne $prev.igpu_only) -or ($r.nvidia_pnp -ne $prev.nvidia_pnp) -or
                       ($r.ec_1989_b5 -ne $prev.ec_1989_b5) -or ($r.ec_2002_low -ne $prev.ec_2002_low) -or
                       ($r.smi_exit -ne $prev.smi_exit)
        }
        if ($changed -or $i -eq 0 -or $i -eq ($Rows.Count - 1)) {
            Write-Report ("  {0,-10} | {1,-9} | {2,-10} | {3,-9} | {4,-11} | {5}" -f $r.t, $r.igpu_only, $r.nvidia_pnp, $r.ec_1989_b5, $r.ec_2002_low, $r.smi_exit)
            $printed++
        }
        $prev = $r
    }
    if (-not $Detected) {
        Write-Report '  (no tracked signal changed during the wait: the switch may need a reboot, or it did nothing)'
    } elseif ($Rows.Count -gt 2) {
        Write-Report '  (sampling continued briefly after the first change; read the rows top-down to see what moved first)'
    }
}

function Get-MeasureSnapshot {
    param($Acpi, $Ec, $Blob, $Reg, [string]$Platform, [string]$BlobSource = '')

    $smi = Get-SmiSnapshot
    $inv = Get-NvidiaInventoryLite
    $mode = $null
    $blobSize = 0
    if ($Blob -and $Blob.Status -eq 'OK') {
        $blobSize = $Blob.Size
        if ($Blob.Size -ge 99) { $mode = Get-FieldValue -Data $Blob.Data -Field 'OemDisplayMode' }
    }

    return [ordered]@{
        captured_at             = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        host                    = $env:COMPUTERNAME
        identity                = ("{0}{1}" -f [System.Security.Principal.WindowsIdentity]::GetCurrent().Name, $(if (Test-IsSystem) { ' (SYSTEM)' } else { '' }))
        platform                = $Platform
        nvidia_present          = $inv.Present
        nvidia_count            = $inv.Count
        nvidia_phantom_count    = $inv.PhantomCount
        nvidia_device           = $inv.Devices
        display_device_count    = $inv.AllDisplayDevices
        display_devices         = $inv.AllDevices
        nvidia_hd_audio         = (Get-NvHdAudioPresent)
        nvdisplay_container     = (Get-ServiceStateLite 'NVDisplay.ContainerLocalSystem')
        nvcontainer             = (Get-ServiceStateLite 'NVContainerLocalSystem')
        smi_exit                = $smi.Exit
        smi_name                = $smi.Name
        smi_power_draw          = $smi.PowerW
        smi_pstate              = $smi.PState
        smi_temp_c              = $smi.TempC
        smi_mem_used            = $smi.MemUsed
        smi_error               = $smi.Error
        acpi_available          = $Acpi.Available
        acpi_support            = $Acpi.Support
        acpi_status             = $Acpi.Status
        ec_1989                 = $Ec['1989']
        ec_1989_bit5            = $Ec['1989_bit5']
        ec_2002                 = $Ec['2002']
        ec_2002_low5            = $Ec['2002_low5']
        ec_1834                 = $Ec['1834']
        ec_error                = $Ec['error']
        oem_display_mode        = $mode
        nvram_blob_size         = $blobSize
        nvram_blob_source       = $BlobSource
        reg_ap_version_check    = $Reg['APVersionCheck']
        reg_is_nv_gpu           = $Reg['IsNvGpu']
        reg_dgpu_direct_support = $Reg['DGpuDirectConnectionSupport']
        reg_igpu_only           = $Reg['IGPUonly']
        reg_hotswap_status      = $Reg['lgpuHotSwapSwitchStatus']
    }
}

function Save-MeasureSnapshot {
    param($Snapshot, [string]$Path)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        try { New-Item -ItemType Directory -Path $dir -Force | Out-Null } catch { }
    }
    try {
        ($Snapshot | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $Path -Encoding UTF8
        return $true
    } catch {
        Write-Report -Level WARN -Message ("  could not save the snapshot: {0}" -f $_.Exception.Message)
        return $false
    }
}

function Import-MeasureSnapshot {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    } catch {
        Write-Report -Level WARN -Message ("  could not parse {0}: {1}" -f $Path, $_.Exception.Message)
        return $null
    }
}

function Show-MeasureSnapshot {
    param($Snapshot)
    # Must go through the helpers: PSObject.Properties on an OrderedDictionary returns the
    # dictionary's own CLR properties (Count/Keys/Values/...), which is exactly what a real
    # operator report showed.
    foreach ($k in (Get-SnapshotKeys -Snapshot $Snapshot)) {
        Write-Report ("    {0,-24} = {1}" -f $k, (Get-SnapshotValue -Snapshot $Snapshot -Key $k))
    }
}

function Get-SnapshotKeys {
    <# Works for both shapes involved: the live snapshot is an ordered dictionary, the one loaded
       from JSON is a PSCustomObject. PSObject.Properties on an OrderedDictionary returns the
       dictionary's own CLR properties (Count/Keys/Values/...), which produced 7 bogus rows. #>
    param($Snapshot)
    if ($Snapshot -is [System.Collections.IDictionary]) { return @($Snapshot.Keys) }
    return @($Snapshot.PSObject.Properties.Name)
}

function Get-SnapshotValue {
    param($Snapshot, [string]$Key)
    if ($Snapshot -is [System.Collections.IDictionary]) {
        if ($Snapshot.Contains($Key)) { return $Snapshot[$Key] }
        return $null
    }
    return $Snapshot.$Key
}

function Show-MeasureCompare {
    <# Table of before/after for every key of either snapshot; returns the number of differences. #>
    param($Before, $After)
    $keys = @(Get-SnapshotKeys -Snapshot $After)
    foreach ($k in (Get-SnapshotKeys -Snapshot $Before)) { if ($keys -notcontains $k) { $keys += $k } }

    Write-Report ("  {0,-24} | {1,-30} | {2,-30} | {3}" -f 'item', 'before', 'after', 'flag')
    Write-Report ('  ' + ('-' * 100))
    $changed = 0
    foreach ($k in $keys) {
        $bRaw = Get-SnapshotValue -Snapshot $Before -Key $k
        $aRaw = Get-SnapshotValue -Snapshot $After -Key $k
        $b = [string]$bRaw
        $a = [string]$aRaw
        if ($b.Length -gt 30) { $b = $b.Substring(0, 27) + '...' }
        if ($a.Length -gt 30) { $a = $a.Substring(0, 27) + '...' }
        $flag = ''
        if ($bRaw -ne $aRaw) {
            if ($k -eq 'captured_at') { $flag = '(timestamp)' }
            elseif ($k -eq 'identity') { $flag = '(identity differs - not comparable)' }
            else { $flag = '*** CHANGED'; $changed++ }
        }
        Write-Report ("  {0,-24} | {1,-30} | {2,-30} | {3}" -f $k, $b, $a, $flag)
    }
    return $changed
}

function Show-MeasureVerdict {
    param($Before, $After)
    Write-Report -Level HEAD -Message 'interpretation (software-only evidence):'

    if ($Before.identity -ne $After.identity) {
        Write-Report -Level WARN -Message ("  ! the two snapshots were taken by different identities ({0} vs {1}); some rows are not comparable" -f $Before.identity, $After.identity)
    }
    if ([string]$Before.nvidia_present -ne [string]$After.nvidia_present) {
        $verb = 'LEFT'
        if ([string]$After.nvidia_present -eq 'True') { $verb = 're-entered' }
        Write-Report ("  * PnP inventory: nvidia_present {0} -> {1}  => the device {2} the device tree" -f $Before.nvidia_present, $After.nvidia_present, $verb)
    } else {
        Write-Report ("  * PnP inventory unchanged (nvidia_present={0}) => no enumeration-level change" -f $After.nvidia_present)
    }
    # A phantom node is what "gone but still listed" looks like: the device is no longer present,
    # so Device Manager (and any present-only query) no longer sees it, while the PnP node remains.
    if ([string]$After.nvidia_present -eq 'False' -and [string]$After.nvidia_phantom_count -ne '0' -and $null -ne $After.nvidia_phantom_count) {
        Write-Report ("  * the dGPU node still exists but is NOT present ({0} phantom entr(y/ies)) => truly removed/depowered, not merely disabled" -f $After.nvidia_phantom_count)
    }
    if ([string]$Before.acpi_status -ne [string]$After.acpi_status) {
        Write-Report ("  * firmware iGPU-only STATUS (ACPI cmd=3 addr=2): {0} -> {1}  (85 = the iGPU-only state is ACTIVE)" -f $Before.acpi_status, $After.acpi_status)
    }
    if ([string]$Before.smi_exit -ne [string]$After.smi_exit) {
        Write-Report ("  * nvidia-smi exit {0} -> {1} => the display driver {2}" -f $Before.smi_exit, $After.smi_exit, $(if ([string]$After.smi_exit -ne '0') { 'can no longer talk to the GPU' } else { 'talks to the GPU again' }))
    }
    if ([string]$Before.smi_power_draw -ne [string]$After.smi_power_draw) {
        Write-Report ("  * power draw: '{0}' -> '{1}'" -f $Before.smi_power_draw, $After.smi_power_draw)
    }
    if ([string]$Before.ec_2002_low5 -ne [string]$After.ec_2002_low5) {
        Write-Report ("  * EC[2002] bits0-4 (firmware-side dGPU presence): {0} -> {1}" -f $Before.ec_2002_low5, $After.ec_2002_low5)
    }
    if ([string]$Before.ec_1989_bit5 -ne [string]$After.ec_1989_bit5) {
        Write-Report ("  * EC[1989] bit5 (panel signal; the vendor polls this after a switch): {0} -> {1}" -f $Before.ec_1989_bit5, $After.ec_1989_bit5)
    }
    if ([string]$Before.acpi_status -ne [string]$After.acpi_status) {
        Write-Report ("  * ACPI iGPU-only status query (cmd=3 addr=2): {0} -> {1}" -f $Before.acpi_status, $After.acpi_status)
    }
    if ([string]$Before.oem_display_mode -ne [string]$After.oem_display_mode) {
        Write-Report ("  * NVRAM OemDisplayMode: {0} -> {1} (a change here is the persistent/reboot-applied route)" -f $Before.oem_display_mode, $After.oem_display_mode)
    }
    Write-Report '  limits: a power-rail cut cannot be distinguished from power gating by software alone;'
    Write-Report '          that needs a wattmeter, or a still-readable power draw that drops to ~0.'
    Write-Report '          "nvidia-smi reports the device missing" vs "reports a driver-communication error" is the strongest software signal.'
}

# =======================================================================================
# Main
# =======================================================================================
Write-Report -Level HEAD -Message '========================================================='
Write-Report -Level HEAD -Message ' GCU UEFI NVRAM / display-route probe  (READ-ONLY)'
Write-Report -Level HEAD -Message '========================================================='
$isSystem = Test-IsSystem
$isElevated = (Test-Elevated) -or $isSystem
Write-Report ("host={0}  user={1}  elevated={2}{3}" -f $env:COMPUTERNAME,
    [System.Security.Principal.WindowsIdentity]::GetCurrent().Name, $isElevated,
    $(if ($isSystem) { ' (running as SYSTEM)' } else { '' }))
$uefiHint = Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State'
Write-Report ("UEFI hint (SecureBoot state key present): {0}" -f $uefiHint)
Write-Report ("GUID = {0}" -f $script:VendorGuid)
Write-Report 'NO WRITES: this script never calls SetFirmwareEnvironmentVariable and never sends ACPI write values.'

# ---- where the report goes (also what -RunAsSystem passes to the SYSTEM instance) --------
if ($RunAsSystem -and $NoLog) {
    # The SYSTEM instance can only report back through a file, so a log is mandatory here.
    $NoLog = $false
    Write-Report -Level WARN -Message '-RunAsSystem needs a log file (the SYSTEM instance can only report through it); ignoring -NoLog.'
}
if (-not $LogPath -and -not $NoLog) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $logDir = Join-Path $env:ProgramData 'DgpuSilence\logs'
    try {
        if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        $LogPath = Join-Path $logDir ("gcu-nvram_{0}.log" -f $stamp)
    } catch {
        $LogPath = Join-Path $env:TEMP ("gcu-nvram_{0}.log" -f $stamp)
    }
    Write-Report ("log file     : {0}" -f $LogPath)
} elseif ($NoLog) {
    Write-Report 'log file     : disabled by -NoLog'
} else {
    Write-Report ("log file     : {0}" -f $LogPath)
}

# ---- capture mode preconditions ---------------------------------------------------------
if ($CaptureSwitch) {
    if ($RunAsSystem -and -not $SystemRun) {
        Write-Report -Level ERROR -Message '-CaptureSwitch needs an interactive console, so it cannot be combined with -RunAsSystem (a SYSTEM scheduled task has no console to pause on).'
        Write-Report -Level ERROR -Message 'Run it from an elevated PowerShell window instead: on the measured machines a plain Administrator token is enough.'
        if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
        exit 2
    }
    if ((-not [Environment]::UserInteractive) -or ($Host.Name -ne 'ConsoleHost')) {
        Write-Report -Level ERROR -Message '-CaptureSwitch needs an interactive console (none detected).'
        if ($LogPath) { try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { } }
        exit 2
    }
}

# ---- optional: re-run as SYSTEM (the only identity that holds SeSystemEnvironmentPrivilege) ----
if ($RunAsSystem -and -not $SystemRun -and -not $isSystem) {
    Write-Report -Level HEAD -Message 're-running as SYSTEM via a one-shot scheduled task...'
    $code = Invoke-ProbeAsSystem -LogFile $LogPath
    exit $code
}

$platform = Get-PlatformKind
Write-Report -Level HEAD -Message ("platform (non-NVIDIA display adapter): {0}" -f $platform)

# ---- firmware variables -----------------------------------------------------------------
Write-Report -Level HEAD -Message 'NVRAM variables:'
$read = [ordered]@{}
foreach ($name in $script:VariableNames) {
    $r = Read-FirmwareVariable -Name $name
    $read[$name] = $r
    switch ($r.Status) {
        'OK'           { Write-Report -Level OK    -Message ("  {0,-18} OK, {1} bytes" -f $name, $r.Size) }
        'NOT_FOUND'    { Write-Report              -Message ("  {0,-18} not present (win32={1})" -f $name, $r.Error) }
        'NO_PRIVILEGE' { Write-Report -Level WARN  -Message ("  {0,-18} access denied - privilege not held (win32={1})" -f $name, $r.Error) }
        default        { Write-Report -Level WARN  -Message ("  {0,-18} read failed (win32={1})" -f $name, $r.Error) }
    }
}

# If everything failed with the privilege error, enable SeSystemEnvironmentPrivilege and retry once.
$privErrors = @($read.Values | Where-Object { $_.Status -eq 'NO_PRIVILEGE' }).Count
if ($privErrors -gt 0) {
    Write-Report 'retrying after enabling SeSystemEnvironmentPrivilege...'
    $priv = Enable-SystemEnvironmentPrivilege
    Write-Report ("  privilege: {0}" -f $priv.Why)
    Write-Report '  token state after the enable attempt:'
    Show-TokenPrivilegeDiagnostic
    if ($priv.Ok) {
        foreach ($name in $script:VariableNames) {
            $r = Read-FirmwareVariable -Name $name
            $read[$name] = $r
            if ($r.Status -eq 'OK') { Write-Report -Level OK -Message ("  {0,-18} OK, {1} bytes (after privilege enable)" -f $name, $r.Size) }
        }
    }
}

# ---- hex dumps --------------------------------------------------------------------------
foreach ($name in $script:VariableNames) {
    $r = $read[$name]
    if ($r.Status -ne 'OK') { continue }
    Write-Report -Level HEAD -Message ("raw dump of {0} ({1} bytes):" -f $name, $r.Size)
    foreach ($line in (Get-HexDump -Data $r.Data)) { Write-Report $line }
}

# ---- decode the blob --------------------------------------------------------------------
# Which variable holds the struct blob is GENERATION DEPENDENT: the 40-gen firmware uses
# UniWillVariable, the 50-gen firmware uses OemMagicVariable (measured on both machines).
# Pick the first readable candidate that is big enough, and remember which one it was.
$blob = $null
$blobSource = ''
foreach ($candidate in @('OemMagicVariable', 'UniWillVariable')) {
    $c = $read[$candidate]
    if ($c.Status -eq 'OK' -and $c.Size -ge 99) { $blob = $c; $blobSource = $candidate; break }
}
if (-not $blob) {
    foreach ($candidate in @('OemMagicVariable', 'UniWillVariable')) {
        if ($read[$candidate].Status -eq 'OK') { $blob = $read[$candidate]; $blobSource = $candidate; break }
    }
}

if ($blob -and $blob.Status -eq 'OK' -and $blob.Size -ge 99) {
    Write-Report -Level HEAD -Message ("decoded NVRAM_STRUCT fields from {0} (offsets from NVRAM_STRUCT.cs):" -f $blobSource)
    foreach ($field in $script:FieldOffsets.Keys) {
        $v = Get-FieldValue -Data $blob.Data -Field $field
        if ($null -eq $v) { continue }
        Write-Report ("    {0,-38} @{1,-4} = {2}" -f $field, $script:FieldOffsets[$field].Offset, $v)
    }
} elseif ($blob -and $blob.Status -eq 'OK') {
    Write-Report -Level WARN -Message ("{0} is only {1} bytes; cannot decode OemDisplayMode (needs >= 99)" -f $blobSource, $blob.Size)
} else {
    Write-Report -Level WARN -Message 'no usable struct blob was readable; cannot decode the display route from NVRAM.'
}

$dgpu = $read['OemDgpuPresent']
if ($dgpu.Status -eq 'OK') {
    $val = $null
    if ($dgpu.Size -ge 1) { $val = $dgpu.Data[0] }
    Write-Report -Level HEAD -Message 'dGPU presence flag (50-gen DLL only):'
    Write-Report ("    OemDgpuPresent[{0} bytes] first byte = {1}" -f $dgpu.Size, $val)
    if ($dgpu.Size -ge 4) { Write-Report ("    as uint32 = {0}" -f [BitConverter]::ToUInt32($dgpu.Data, 0)) }
    Write-Report '    note: the vendor DLL only exposes a reader for this variable (ReadUefiGPU); there is no WriteUefiGPU.'
} else {
    Write-Report ("OemDgpuPresent: {0} - the 50-gen DLL references this name, but none of the three measured machines' firmware defines it" -f $dgpu.Status)
    Write-Report '                   (irrelevant while the registry APVersionCheck is < 25: GetNvGpu() then uses EC[2002] instead)'
}

# ---- registry mirror (self-check of the offsets) ----------------------------------------
Write-Report -Level HEAD -Message 'self-check - registry mirror written by GCU:'
$reg = Show-RegistryMirror

$layoutOk = $false
if ($blob -and $blob.Status -eq 'OK' -and $blob.Size -ge 99) {
    # check 1: OemBoardSsid@0 must be the SUBSYS id of SOME PCI device on this machine. Comparing
    # only against the dGPU is not enough: on a measured Intel model the dGPU carries a different
    # subsystem id (0x60421D05) than the board (0x50001D05), which is not a layout failure.
    $ssid = Get-FieldValue -Data $blob.Data -Field 'OemBoardSsid'
    if ($null -ne $ssid) {
        $ssidHex = ('{0:X8}' -f [uint32]$ssid)
        $subsysMap = Get-AllPciSubsysIds
        if ($subsysMap.Count -gt 0) {
            if ($subsysMap.ContainsKey($ssidHex)) {
                Write-Report -Level OK -Message ("    check 1: NVRAM OemBoardSsid (0x{0}) == the PCI SUBSYS of '{1}' - struct offsets hold" -f $ssidHex, $subsysMap[$ssidHex])
                $layoutOk = $true
            } else {
                $dgpuSsid = Get-PciSubsysId
                Write-Report -Level WARN -Message ("    check 1 FAILED: NVRAM OemBoardSsid=0x{0} matches no PCI SUBSYS on this machine (dGPU is 0x{1:X8})" -f $ssidHex, [uint32]$dgpuSsid)
            }
        } else {
            Write-Report -Level WARN -Message '    check 1 skipped: could not enumerate PCI subsystem ids'
        }
    }

    # check 2: AudioCodecID@104 should decode to a known audio-codec vendor id if the
    # natural-alignment offsets hold beyond byte 98. A non-listed vendor only warns.
    $codec = Get-FieldValue -Data $blob.Data -Field 'AudioCodecID'
    if ($null -ne $codec) {
        $codecVendor = [uint32]($codec -shr 16)
        $codecVendors = @{ 0x10EC = 'Realtek'; 0x14F1 = 'Conexant'; 0x1013 = 'Cirrus Logic';
                           0x11D4 = 'Analog Devices'; 0x111D = 'IDT'; 0x1102 = 'Creative';
                           0x8086 = 'Intel'; 0x1AF4 = 'Virtio' }
        if ($codecVendors.ContainsKey([int]$codecVendor)) {
            Write-Report -Level OK -Message ("    check 2: AudioCodecID@104 = 0x{0:X8} ({1} 0x{2:X4}) - offsets beyond 98 hold too" -f $codec, $codecVendors[[int]$codecVendor], $codecVendor)
        } elseif ($codecVendor -ne 0 -and $codecVendor -ne 0xFFFF) {
            Write-Report ("    check 2: AudioCodecID@104 = 0x{0:X8} looks like a vendor:device pair (vendor 0x{1:X4} is not in the known codec list, plausibly fine)" -f $codec, $codecVendor)
        } else {
            Write-Report -Level WARN -Message ("    check 2: AudioCodecID@104 = 0x{0:X8} does not decode to a vendor id - offsets beyond 98 unconfirmed on this model" -f $codec)
        }
    }

    Write-Report ("    blob source: {0}, {1} bytes (a Reserved[76] struct is 187/188 bytes; the difference sits in the trailing array and moves no field used here)" -f $blobSource, $blob.Size)

    # Route: NVRAM byte vs GCU's own live registry state. The registry value is semantic and
    # model-independent, so a disagreement means the NVRAM offset/mapping does NOT transfer to
    # this generation - which is exactly what the 40-gen and 50-gen machines showed.
    $mode = Get-FieldValue -Data $blob.Data -Field 'OemDisplayMode'
    Write-Report -Level HEAD -Message 'display route:'
    Write-Report ("    NVRAM OemDisplayMode@{0} = {1} -> {2}" -f $script:FieldOffsets['OemDisplayMode'].Offset, $mode, (Get-OemDisplayModeText -Value $mode -Platform $platform))
    Write-Report '    caveat: this NVRAM byte is the PERSISTED route the firmware applies at boot (the vendor''s'
    Write-Report '            DGPU_DIRECT_CONNECT_* actions are documented as reboot-required). It is NOT'
    Write-Report '            necessarily the live route - measured: a 50-gen machine reads 1 (dGPU direct) while'
    Write-Report '            its desktop is actually driven by the iGPU. Judge the live route from the registry'
    Write-Report '            IGPUonly below plus the display topology.'
    if ($null -ne $reg['IGPUonly']) {
        $ig = [int]$reg['IGPUonly']
        $igText = ("{0} = (unmapped value)" -f $ig)
        if ($ig -eq 1) { $igText = '1 = iGPU-only ON (the dGPU is switched away)' }
        elseif ($ig -eq 0) { $igText = '0 = iGPU-only OFF (normal route)' }
        elseif ($ig -eq 2) { $igText = '2 = AUTO' }
        Write-Report ("    registry IGPUonly (live, written by GCU) = {0}" -f $igText)
    } else {
        Write-Report '    registry IGPUonly: absent (GCU has not written it on this machine)'
    }
    if (-not $layoutOk) {
        Write-Report -Level WARN -Message '    NOTE: check 1 did not pass, so treat the NVRAM route byte as a HINT and prefer the registry / display-topology evidence.'
    }
}

# ---- hot-switch capability (read-only ACPI-WMI) -----------------------------------------
Write-Report -Level HEAD -Message 'iGPU-only / hot-switch capability (read-only ACPI-WMI, cmd=3 read addresses only):'
$acpi = Get-IgpuOnlyAcpiState
if (-not $acpi.Available) {
    Write-Report -Level WARN -Message ("    {0}" -f $acpi.Note)
} else {
    Write-Report ("    instances: {0}" -f ($acpi.Instances -join ', '))
    if ($null -ne $acpi.Support) {
        if ($acpi.Support -eq 85) {
            Write-Report -Level OK -Message '    support = 85 (0x55) -> the runtime ACPI iGPU-only switch IS supported (hot, no reboot needed)'
        } elseif ($acpi.Support -eq 170) {
            Write-Report -Level WARN -Message '    support = 170 (0xAA) -> NOT supported: no runtime ACPI iGPU-only switch on this machine (cold switch via OemDisplayMode + reboot only)'
        } else {
            Write-Report ("    support = {0} (unexpected; 85=supported, 170=not supported)" -f $acpi.Support)
        }
    }
}

# ---- EC bytes (read-only, cmd=1) --------------------------------------------------------
Write-Report -Level HEAD -Message 'EC bytes (read-only cmd=1; the EC-write form is never used by this script):'
$liveEc = Get-EcSnapshot -Instance $acpi.Target
Show-EcSnapshot -Ec $liveEc -ApVersionCheck $reg['APVersionCheck']

# ---- capture mode: before snapshot -> you switch -> after snapshot + comparison ----------
if ($CaptureSwitch) {
    Write-Report -Level HEAD -Message 'capture mode: before snapshot -> perform the switch -> after snapshot + comparison'

    $before = Get-MeasureSnapshot -Acpi $acpi -Ec $liveEc -Blob $blob -Reg $reg -Platform $platform -BlobSource $blobSource
    if (-not $SnapshotPath) {
        $snapDir = Join-Path $env:ProgramData 'DgpuSilence\snapshots'
        try { if (-not (Test-Path -LiteralPath $snapDir)) { New-Item -ItemType Directory -Path $snapDir -Force | Out-Null } } catch { }
        $SnapshotPath = Join-Path $snapDir ("before_{0}.json" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
    }
    if (Save-MeasureSnapshot -Snapshot $before -Path $SnapshotPath) {
        Write-Report ("  before snapshot: {0}" -f $SnapshotPath)
        try {
            [IO.File]::WriteAllText((Join-Path $env:ProgramData 'DgpuSilence\last-snapshot.txt'), $SnapshotPath)
            [IO.File]::WriteAllText((Join-Path $env:ProgramData 'DgpuSilence\baseline.txt'), $SnapshotPath)
        } catch { }
    }
    Write-Report '  before values:'
    Show-MeasureSnapshot -Snapshot $before

    if ($null -ne $acpi.Support -and $acpi.Support -ne 85) {
        Write-Report -Level WARN -Message ("  support = {0} (not 85): this machine has no runtime iGPU-only switch, so the hot switch should fail or do nothing." -f $acpi.Support)
    }

    $rows = New-Object System.Collections.ArrayList
    if ($AutoWait) {
        Write-Report -Level STEP -Message ("perform the switch now (vendor console -> iGPU only / 'iGPU' power-saving mode). Auto-wait up to {0}s, polling every {1}s..." -f $WaitSeconds, $PollSeconds)
        Write-Report -Level WARN -Message '  this live capture CAN make the vendor switch fail: it contends with the vendor''s own EC'
        Write-Report '  polling. If the switch fails while this runs, close this window, switch, then measure with'
        Write-Report '  -Measure before / -Measure -CompareTo last instead (the two-step workflow).'
        $base = Get-TransitionSignals -AcpiInstance $acpi.Target
        [void]$rows.Add($base)
        $waited = 0
        $polls = 0
        $changed = $false
        while ($waited -lt $WaitSeconds) {
            Start-Sleep -Seconds $PollSeconds
            $waited += $PollSeconds
            $polls++
            # EC only every EcEvery polls (0 = never): each EC read is 3 ACPI-WMI calls and is the
            # most likely source of interference with the vendor's confirmation poll.
            if ($EcEvery -gt 0 -and ($polls % $EcEvery) -eq 0) {
                $s = Get-TransitionSignals -AcpiInstance $acpi.Target -WithEc
            } else {
                $s = Get-TransitionSignals -AcpiInstance $acpi.Target
            }
            [void]$rows.Add($s)
            if (($s.igpu_only -ne $base.igpu_only) -or ($s.nvidia_pnp -ne $base.nvidia_pnp) -or
                ($s.ec_1989_b5 -ne $base.ec_1989_b5) -or ($s.ec_2002_low -ne $base.ec_2002_low) -or
                ($s.smi_exit -ne $base.smi_exit)) {
                $changed = $true
                $detectAt = $waited     # report the detection time, not detection+follow-up
                # Keep observing after the first change. A real capture showed the after-snapshot
                # landing only ~6s after the flip, while the vendor's own poll loop waits up to
                # 2 minutes - far too short to claim "nothing else changed". So keep sampling for
                # FollowSeconds and let the AFTER snapshot reflect the settled state; a switch that
                # changes again (e.g. reverts) is visible in the timeline.
                $follow = 0
                while ($follow -lt $FollowSeconds) {
                    Start-Sleep -Seconds $PollSeconds
                    $follow += $PollSeconds
                    $waited += $PollSeconds
                    $polls++
                    if ($EcEvery -gt 0 -and ($polls % $EcEvery) -eq 0) {
                        [void]$rows.Add((Get-TransitionSignals -AcpiInstance $acpi.Target -WithEc))
                    } else {
                        [void]$rows.Add((Get-TransitionSignals -AcpiInstance $acpi.Target))
                    }
                }
                if ($FollowSeconds -gt 0) {
                    Write-Report ("  kept observing for {0}s after the change (vendor waits up to 120s)" -f $follow)
                }
                break
            }
        }
        if ($changed) {
            Write-Report -Level OK -Message ("  first change detected after ~{0}s" -f $detectAt)
            if ($waited -gt $detectAt) {
                Write-Report ("  after-snapshot taken {0}s after the wait started (the follow-up window moved it past the flip)" -f $waited)
            }
        }
        else { Write-Report -Level WARN -Message ("  no tracked signal changed within {0}s" -f $waited) }
        Start-Sleep -Seconds 5   # let the transition settle before the after snapshot
    } else {
        Write-Report -Level STEP -Message 'perform the switch in the vendor console (iGPU only / iGPU power-saving), then return here.'
        try { [void](Read-Host 'Press Enter once the switch has been attempted (accepted or refused)') } catch { }
        [void]$rows.Add((Get-TransitionSignals -AcpiInstance $acpi.Target))
    }

    # Re-read everything for the after snapshot: EC/ACPI/NVRAM all can change during the switch.
    $acpi2 = Get-IgpuOnlyAcpiState -Quiet
    $ec2 = Get-EcSnapshot -Instance $acpi2.Target
    $blobPick = Get-BlobSource
    $reg2 = Show-RegistryMirror -Quiet
    $after = Get-MeasureSnapshot -Acpi $acpi2 -Ec $ec2 -Blob $blobPick.Blob -Reg $reg2 -Platform $platform -BlobSource $blobPick.Name

    Write-Report -Level HEAD -Message 'after values:'
    Show-MeasureSnapshot -Snapshot $after

    $afterPath = $SnapshotPath
    if ($afterPath -match 'before_') {
        $afterPath = $afterPath -replace 'before_', 'after_'
    } else {
        $dir = Split-Path -Parent $afterPath
        if (-not $dir) { $dir = '.' }
        $afterPath = Join-Path $dir ([System.IO.Path]::GetFileNameWithoutExtension($afterPath) + '_after.json')
    }
    if (Save-MeasureSnapshot -Snapshot $after -Path $afterPath) { Write-Report ("  after snapshot: {0}" -f $afterPath) }

    Write-Report -Level HEAD -Message 'before/after comparison:'
    $changedCount = Show-MeasureCompare -Before $before -After $after
    Write-Report ("  {0} item(s) differ" -f $changedCount)
    Show-MeasureVerdict -Before $before -After $after
    if ($rows.Count -gt 1) { Show-TransitionTimeline -Rows $rows -Detected $changed }

    if ($LogPath) {
        try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { }
        Write-Host ("report written to {0}" -f $LogPath) -ForegroundColor Cyan
    }
    exit 0
}

# ---- measure mode: snapshot / before-after table ----------------------------------------
if ($Measure -or $CompareTo) {
    Write-Report -Level HEAD -Message 'measure mode (read-only snapshot + comparison):'
    # "-CompareTo last" -> the snapshot this script saved most recently, so the recommended
    # two-step workflow (snapshot -> CLOSE the tool -> switch in the vendor console -> reopen and
    # compare) needs no path typing and no live capture running during the switch.
    #
    # It prefers a MARKED BASELINE over the newest snapshot. A real operator run proved why: an
    # intervening -Measure (e.g. a plain health check) overwrote "newest snapshot", so the compare
    # ended up diffing two post-switch states and reported "0 items differ" while nvidia-smi had
    # actually gone from exit 0 to exit 9. Only -MarkBaseline writes the baseline, so option 1/3
    # can no longer destroy the reference that option 2 took.
    if ($CompareTo -eq 'last') {
        $stateDir = Join-Path $env:ProgramData 'DgpuSilence'
        $baselineFile = Join-Path $stateDir 'baseline.txt'
        $lastFile = Join-Path $stateDir 'last-snapshot.txt'
        $resolved = $null
        $source = ''
        try {
            if (Test-Path -LiteralPath $baselineFile) {
                $cand = (Get-Content -LiteralPath $baselineFile -Raw).Trim()
                if ($cand -and (Test-Path -LiteralPath $cand)) { $resolved = $cand; $source = 'marked baseline' }
            }
            if (-not $resolved -and (Test-Path -LiteralPath $lastFile)) {
                $cand = (Get-Content -LiteralPath $lastFile -Raw).Trim()
                if ($cand -and (Test-Path -LiteralPath $cand)) { $resolved = $cand; $source = 'newest snapshot' }
            }
        } catch { }
        if ($resolved) {
            $CompareTo = $resolved
            Write-Report ("  -CompareTo last -> {0} ({1})" -f $CompareTo, $source)
            if ($source -eq 'newest snapshot') {
                Write-Report -Level WARN -Message '    no marked baseline exists; if other snapshot runs happened since the switch, this comparison may be meaningless. Use -MarkBaseline on the "before" run.'
            }
        } else {
            Write-Report -Level WARN -Message '-CompareTo last: no snapshot has been recorded yet; run -Measure -MarkBaseline first.'
            $CompareTo = $null
        }
    }
    $snapshot = Get-MeasureSnapshot -Acpi $acpi -Ec $liveEc -Blob $blob -Reg $reg -Platform $platform -BlobSource $blobSource
    if (-not $SnapshotPath) {
        $snapDir = Join-Path $env:ProgramData 'DgpuSilence\snapshots'
        try { if (-not (Test-Path -LiteralPath $snapDir)) { New-Item -ItemType Directory -Path $snapDir -Force | Out-Null } } catch { }
        $SnapshotPath = Join-Path $snapDir ("snapshot_{0}.json" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
    }
    if (Save-MeasureSnapshot -Snapshot $snapshot -Path $SnapshotPath) {
        Write-Report ("  snapshot saved: {0}" -f $SnapshotPath)
        # remember it so a later -Measure -CompareTo last needs no typing
        try {
            [IO.File]::WriteAllText((Join-Path $env:ProgramData 'DgpuSilence\last-snapshot.txt'), $SnapshotPath)
            if ($MarkBaseline) {
                [IO.File]::WriteAllText((Join-Path $env:ProgramData 'DgpuSilence\baseline.txt'), $SnapshotPath)
                Write-Report '  marked as the BASELINE for a later -Measure -CompareTo last'
            }
        } catch { }
    }
    Write-Report '  current values:'
    Show-MeasureSnapshot -Snapshot $snapshot

    if ($CompareTo) {
        $before = Import-MeasureSnapshot -Path $CompareTo
        if ($null -eq $before) {
            Write-Report -Level WARN -Message ("  cannot compare: {0} is missing or unreadable" -f $CompareTo)
        } else {
            Write-Report -Level HEAD -Message ("before/after comparison ({0}  ->  now):" -f $CompareTo)
            $changed = Show-MeasureCompare -Before $before -After $snapshot
            Write-Report ("  {0} item(s) differ (timestamp excluded; power draw / temperature / memory used drift on their own)" -f $changed)
            Show-MeasureVerdict -Before $before -After $snapshot
        }
    } else {
        Write-Report ("  next step: after the switch, run the same command with -CompareTo ""{0}""" -f $SnapshotPath)
    }

    if ($LogPath) {
        try { Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8 } catch { }
    }
    exit 0
}

# ---- verdict ----------------------------------------------------------------------------
Write-Report -Level HEAD -Message 'verdict:'
if ($blob -and $blob.Status -eq 'OK' -and $blob.Size -ge 99) {
    $mode = Get-FieldValue -Data $blob.Data -Field 'OemDisplayMode'
    Write-Report ("    persisted route (applied at boot), from {0}: {1}" -f $blobSource, (Get-OemDisplayModeText -Value $mode -Platform $platform))
    if (-not $layoutOk) { Write-Report -Level WARN -Message '    (unverified layout on this model: prefer the registry IGPUonly / topology reading above)' }
} else {
    Write-Report '    persisted route: not decodable (no usable blob)'
}
if ($null -ne $acpi.Support) {
    if ($acpi.Support -eq 85) { Write-Report '    hot iGPU-only switch: YES (ACPI cmd=3 supported)' }
    else { Write-Report '    hot iGPU-only switch: NO  (this machine switches the route only at reboot)' }
}

# ---- write the log ----------------------------------------------------------------------
if ($LogPath) {
    try {
        Add-Content -LiteralPath $LogPath -Value $script:Report -Encoding UTF8
        Write-Host ("report written to {0}" -f $LogPath) -ForegroundColor Cyan
    } catch {
        Write-Host ("could not write the log ({0}): {1}" -f $LogPath, $_.Exception.Message) -ForegroundColor Yellow
    }
}

# ---- exit code --------------------------------------------------------------------------
$okCount = @($read.Values | Where-Object { $_.Status -eq 'OK' }).Count
if ($okCount -gt 0) { exit 0 }
if ($acpi.Available -and -not $isElevated) { exit 0 }
if (-not $isElevated) {
    Write-Report -Level WARN -Message 'HINT: the firmware variables need SeSystemEnvironmentPrivilege, which an elevated administrator token does not hold.'
    Write-Report -Level WARN -Message '      Re-run from an elevated shell with -RunAsSystem (creates a temporary SYSTEM task and deletes it again).'
} elseif (-not $isSystem) {
    Write-Report -Level WARN -Message 'HINT: you are elevated, yet the token still lacks SeSystemEnvironmentPrivilege (win32=1300).'
    Write-Report -Level WARN -Message '      Re-run with -RunAsSystem, or grant "Modify firmware environment values" to Administrators (secpol.msc).'
}
exit 2
