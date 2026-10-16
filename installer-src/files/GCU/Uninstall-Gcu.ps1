#Requires -Version 5.1
# ASCII-only by design (Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM).
# Uninstall counterpart for Install-Gcu.ps1.
#
# 2026-10-09 (owner, verbatim: "uninstall the LM part only - do not touch the official one"): two
# modes, and our installer only ever used the first one.
# 2026-10-14 SUPERSEDES that for the uninstall path (owner, verbatim: "if it is an uninstall, then
# delete everything; if it is an upgrade, keep the custom performance settings and curves"). The
# installer now has THREE call sites and picks the mode by PATH, never by guessing:
#   * install / upgrade  -> never calls this script at all for the vendor parts (see the iss
#       [UninstallRun] gating note); the upgrade path only stops the service to release file locks.
#   * real uninstall     -> -PurgeVendor: LM-owned steps PLUS the vendor/shared service + console key.
#   * -LmOwnedOnly       -> retained (LM-owned only) and still the fail-safe that wins if both
#       switches are passed. Nothing calls it on the uninstall path any more.
#   * -LmOwnedOnly : remove ONLY what L-Mechrevo itself created:
#         - autostart tasks matching LMechrevo* (our boot-time elevation entry point)
#         - HKLM\SOFTWARE\L-Mechrevo (our install marker key)
#         - our firewall rule "L-Mechrevo - Block remote GCU MQTT"
#   * -PurgeVendor : real uninstall. Everything -LmOwnedOnly does, PLUS:
#         - stop + `sc delete` the GCUBridge service (and kill GCUService/GCUBridge processes)
#         - delete the vendor console key tree HKLM\SOFTWARE\OEM\GamingCenter2 (whole key; holds the
#           user's custom fan curves MyFan3\ and lighting RGBKeyboard\SingleZone\ - the owner
#           explicitly accepted losing those on a real uninstall)
#       It still does NOT touch the UWACPIDriver entry in the driver store (system-wide, shared,
#       needs a reboot) - only our own staged {app}\UWACPIDriver copy, which the uninstaller's
#       [UninstallDelete] removes.
#   * (no switch)  : legacy full uninstall, kept for manual / forensic use ONLY. Same vendor steps
#       as -PurgeVendor, but additionally removes the UWACPIDriver driver-store entry.
#       Those three are VENDOR/SHARED objects, not ours:
#         - the vendor payload's own install.bat does `sc.exe create GCUBridge` and installs into
#           %ProgramFiles%\OEM\<model>\AiStoneService (see payload\GCU\install.bat), and the
#           vendor's own cleanup script in payload\GCU (the "clean official console" .bat) states
#           the intent verbatim: "Keep: AiStoneService / UWACPIDriver / GCUBridge service /
#           UWACPI driver";
#         - the official console (GCU) talks to the same service name on port 13688;
#         - UWACPIDriver is the vendor ACPI driver pushed into the driver store by
#           `pnputil /add-driver` (Install-Gcu.ps1 -> Install-UwacpiDriver), system-wide and
#           possibly still needed by the official console / ACPI features.
#       Removing them can therefore break software we do not own, so our uninstaller must never
#       call those steps again. They stay in this file, gated behind -LmOwnedOnly = false.
# History (before 2026-10-09): the script was invoked with no switches and did both halves, and a
# short-lived follow-up gated the whole script behind an uninstall-time prompt. Both are gone:
# uninstall no longer asks anything about the vendor console.
# Safe to run when nothing is installed.
<#
.SYNOPSIS
    Remove L-Mechrevo's own GCU uninstall artifacts. With -LmOwnedOnly (the fail-safe mode) the
    vendor/shared GCUBridge service, its processes and the UWACPIDriver driver-store entry are left
    untouched. With -PurgeVendor (what our uninstaller passes on a real uninstall) the GCUBridge
    service and the vendor console key tree HKLM\SOFTWARE\OEM\GamingCenter2 are removed too. Without
    either switch the legacy full removal also runs the driver-store step.

.EXAMPLE
    # real uninstall (what [UninstallRun] passes)
    powershell -NoProfile -ExecutionPolicy Bypass -File Uninstall-Gcu.ps1 `
        -TargetDir "C:\Program Files\L-Mechrevo\GCU" -PurgeVendor

.EXAMPLE
    # LM-owned only / dry run
    powershell -NoProfile -ExecutionPolicy Bypass -File Uninstall-Gcu.ps1 `
        -TargetDir "C:\Program Files\L-Mechrevo\GCU" -LmOwnedOnly -DryRun
#>
# 2026-10-14 (owner correction): -TargetDir is still {app}\GCU. What changed is where the payload
# lives inside it: the unpacked GCU now lands DIRECTLY in {app}\GCU\AiStoneService for EVERY
# variant (1020/30 unpack to 'UniwillService' inside their archives and are laid down as
# AiStoneService - see Install-Gcu.ps1 Get-GcuServiceLeafName). So this script's directory handling
# is unchanged and the file/directory removal itself is done by the uninstaller's
# [UninstallDelete] entries ({app}\GCU\AiStoneService, {app}\GCU\UniwillService, {app}\GCU\logo,
# {app}\GCU\payload, {app}\GCU\shared, {app}\GCU\7za.exe, {app}\UWACPIDriver, ...) - those two
# paths are kept there precisely so an install made by the withdrawn 22:05 package (payload\<variant>\
# <ServiceDir> + shared\ + 7za.exe inside {app}\GCU) is cleaned up on uninstall as well.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TargetDir,
    [string]$LogDir,
    [switch]$KeepDriver,
    [switch]$LmOwnedOnly,
    # 2026-10-14 (owner, verbatim: "if it is an uninstall, then delete everything; if it is an
    # upgrade, keep the custom performance settings and curves"): TRUE full-removal mode, passed by
    # our uninstaller ONLY (never during install/upgrade). It additionally removes the two
    # VENDOR/SHARED artefacts that -LmOwnedOnly deliberately leaves alone:
    #   - the GCUBridge service (sc delete)
    #   - the vendor console key tree HKLM\SOFTWARE\OEM\GamingCenter2
    # -LmOwnedOnly wins if both switches are passed (fail-safe: we never purge by accident).
    [switch]$PurgeVendor,
    # 2026-10-15: passed by the Inno uninstaller only. Means "never create -LogDir", so this script
    # cannot resurrect <ProgramData>\L-Mechrevo (which the uninstaller deletes as its last step).
    # Logging degrades to "no -LogDir log file" when the directory is absent; all steps still run.
    [switch]$NoLogDir,
    [switch]$DryRun
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:RuleName = 'L-Mechrevo - Block remote GCU MQTT'
# Every bundled vendor payload (the 50-series base, the 40-series overlay, the 50-series overlay and
# the 30-series patch) registers the same
# GCUBridge name, so the legacy registration shares it; iterate the list so a name added by an
# older vendor install.bat is still removed.
# The vendor's own AiStoneService\uninstall.bat / uninstallReg.bat ship verbatim in the base payload
# and are deliberately NOT called from here: uninstallReg.bat deletes HKLM\SOFTWARE\OEM\GamingCenter2,
# which is the vendor console's key tree (see the marker-removal step below), not ours.
$script:ServiceName = 'GCUBridge'
$script:LegacyServiceNames = @('GCUBridge')
$script:LogFile = $null

function Write-Log {
    param([string]$Message)
    $line = ('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $Message)
    if ($script:LogFile) { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 }
    Write-Host $line
}

# VENDOR/SHARED STEP - reached only in the legacy full mode (-LmOwnedOnly NOT set), i.e. never from
# our installer any more. GCUBridge is the vendor console's own service name: the vendor payload's
# install.bat creates it too, Install-Gcu.ps1 re-pins it to our copy, and the official console uses
# the same name/port. Deleting it is therefore not ours to do.
function Remove-GcuService {
    foreach ($name in $script:LegacyServiceNames) {
        Remove-SingleGcuService -Name $name
    }
}

function Remove-SingleGcuService {
    param([string]$Name)
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Log ("  {0} service not present" -f $Name)
        return
    }
    Write-Log ("  stopping {0} (status={1})" -f $Name, $service.Status)
    Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
    foreach ($processName in @('GCUService', 'GCUBridge')) {
        foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
            Write-Log ("  stopping leftover process {0} (pid {1})" -f $processName, $process.Id)
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    & sc.exe delete $Name | Out-Null
    $waited = 0
    while ((Get-Service -Name $Name -ErrorAction SilentlyContinue) -and $waited -lt 20) {
        Start-Sleep -Milliseconds 500
        $waited++
    }
    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        Write-Log ("  WARNING: service {0} still exists after delete attempt (may clear after reboot)" -f $Name)
    }
    else {
        Write-Log ("  service {0} deleted" -f $Name)
    }
}

function Remove-FirewallRule {
    $cmd = Get-Command -Name Remove-NetFirewallRule -ErrorAction SilentlyContinue
    if ($cmd) {
        $existing = Get-NetFirewallRule -DisplayName $script:RuleName -ErrorAction SilentlyContinue
        if ($existing) {
            Remove-NetFirewallRule -DisplayName $script:RuleName -ErrorAction SilentlyContinue
            Write-Log '  firewall block rule removed'
        }
        else {
            Write-Log '  firewall block rule not present'
        }
        return
    }
    & netsh.exe advfirewall firewall delete rule ("name=" + $script:RuleName) | Out-Null
    Write-Log '  firewall rule removed via netsh (best effort)'
}

# VENDOR/SHARED STEP - reached only in the legacy full mode (-LmOwnedOnly NOT set), i.e. never from
# our installer any more. UWACPIDriver is the vendor ACPI driver that Install-Gcu.ps1's
# Install-UwacpiDriver pushes into the Windows driver store with `pnputil /add-driver`; that store
# entry is system-wide and the official console / ACPI features may still need it.
# Our own staged copy under {app}\UWACPIDriver (a plain file copy; {app}\GCU\UWACPIDriver before
# 2026-10-13) is our file and is deleted by
# the [UninstallDelete] entry - that is unrelated to the driver-store entry handled here.
function Remove-UwacpiDriver {
    $text = ((& pnputil.exe /enum-drivers 2>&1) | Out-String)
    $blocks = [regex]::Matches($text, '(?ms)Published Name:\s*(oem\d+\.inf).*?(?=Published Name:|\z)')
    $published = $null
    foreach ($block in $blocks) {
        if ($block.Value -match '(?i)Original Name:\s*uwacpidriver\.inf') {
            $published = $block.Groups[1].Value
            break
        }
    }
    if (-not $published) {
        Write-Log '  UWACPIDriver not found in the driver store (nothing to remove)'
        return
    }
    Write-Log ("  pnputil /delete-driver {0} /uninstall /force" -f $published)
    $output = & pnputil.exe /delete-driver $published /uninstall /force 2>&1
    $output | ForEach-Object { Write-Log ('    ' + [string]$_) }
    if ($LASTEXITCODE -ne 0) {
        Write-Log ("  WARNING: driver removal returned {0} (may require reboot)" -f $LASTEXITCODE)
    }
}

function Remove-VendorConsoleKey {
    # 2026-10-14 (owner: "if it is an uninstall, then delete everything"): delete the vendor
    # console's key tree HKLM\SOFTWARE\OEM\GamingCenter2 in full.
    #
    # Why a plain key delete and NOT "key + a loop over subkeys":
    #   The vendor's own AiStoneService\uninstallReg.bat does
    #       reg delete "HKLM\SOFTWARE\OEM\GamingCenter2" /f
    #   followed by a `reg query ... /s /f "*" /k` loop that deletes each subkey. That second loop is
    #   redundant: `reg delete <key> /f` on a key that still has subkeys returns exit code 1 and
    #   removes nothing, so the vendor added the loop to work around exactly that. Remove-Item
    #   -Recurse does not have that limitation - it deletes the key and its whole subtree in one
    #   call - so the loop would only add failure modes (a half-deleted tree if one subkey is denied)
    #   without removing anything the single recursive delete misses. One call is also atomic with
    #   respect to the ordering we log, which keeps the audit trail honest.
    #
    # What this tree holds (measured on the owner machine, read-only): the vendor service writes
    # ItemSupport / Support / TpDetect / SMAPCTable / MyFanTable\EcVersion (program capability data)
    # AND the user's own settings live there too - MyFan3\ (custom fan curves), RGBKeyboard\SingleZone\
    # (lighting), MySetting\, BatteryProtection2\. The owner explicitly accepted losing the custom
    # performance settings and curves on a real uninstall, so removing the whole key is intended.
    # NOTE: this function is reached ONLY via -PurgeVendor, i.e. only from the uninstaller. The
    # install/upgrade path never passes -PurgeVendor, so an upgrade keeps MyFan3\ etc. intact
    # (Install-Gcu.ps1 never writes this key tree; it only reads ItemSupport).
    $key = 'HKLM:\SOFTWARE\OEM\GamingCenter2'
    if (-not (Test-Path -LiteralPath $key)) {
        Write-Log '  vendor console key HKLM\SOFTWARE\OEM\GamingCenter2 not present (nothing to remove)'
        return
    }
    # Log what is actually there first, so the removal is auditable after the fact.
    $subkeys = @((Get-ChildItem -LiteralPath $key -ErrorAction SilentlyContinue) | ForEach-Object { $_.PSChildName })
    Write-Log ('  vendor console key present; subkeys: ' + $(if ($subkeys.Count) { $subkeys -join ', ' } else { '(none)' }))
    Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $key) {
        Write-Log '  WARNING: HKLM\SOFTWARE\OEM\GamingCenter2 still exists after removal (may need elevation or a reboot)'
    }
    else {
        Write-Log '  HKLM\SOFTWARE\OEM\GamingCenter2 removed (whole tree, incl. user settings - full uninstall)'
    }
}

function Remove-AutostartTask {
    # N5: the installer created a highest-privileges autostart task; uninstall must remove it so no
    # boot-time elevation entry point is left behind. Match the app's naming (LMechrevo_<SID>) and
    # also sweep the legacy names the app used to register.
    $sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    # 2026-10-09 fix (found while verifying the LM-owned-only mode): the literal used to be
    #   @('LMechrevo_' + $sid, 'LMechrevo', 'LMechrevoCharge', 'LMechrevo_' + $sid + 'Charge')
    # In PowerShell the comma binds tighter than '+', so that expression collapsed into ONE string:
    # "LMechrevo_<sid> LMechrevo LMechrevoCharge LMechrevo_<sid>Charge". The loop then looked up that
    # single nonsense name and never removed the task our installer really registers
    # (LMechrevo_<SID>, see Install-Gcu.ps1 Get-AutostartTaskName) - the step was a silent no-op.
    # Every concatenation is parenthesised now, so all four intended names are actually swept.
    $names = @(('LMechrevo_' + $sid), 'LMechrevo', 'LMechrevoCharge', ('LMechrevo_' + $sid + 'Charge'))
    foreach ($name in $names) {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if (-not $task) {
            Write-Log ("  autostart task not present: {0}" -f $name)
            continue
        }
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
        if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
            Write-Log ("  WARNING: autostart task {0} still exists after removal" -f $name)
        }
        else {
            Write-Log ("  autostart task removed: {0}" -f $name)
        }
    }
}

function Remove-InstallMarker {
    # 2026-10-09: remove the whole product key, not just the four values. The key
    # HKLM\SOFTWARE\L-Mechrevo is created by this product only (Install-Gcu.ps1) and holds
    # GcuVariant, GcuPayloadSha256, GcuInstalledUtc, GcuServiceDir, GcuServiceExe,
    # GcuInstallerVersion. The old code left GcuPayloadSha256 / GcuInstallerVersion and the
    # (now empty) key behind after every uninstall. Log what is actually present first so the
    # removal is auditable, then delete the key including any subkeys.
    # NOTE: HKLM\SOFTWARE\OEM\GamingCenter2 is the vendor console's own key tree and is
    # deliberately NOT touched here (the vendor's uninstallReg.bat deletes it; we must not).
    $key = 'HKLM:\SOFTWARE\L-Mechrevo'
    if (-not (Test-Path -LiteralPath $key)) {
        Write-Log '  install marker key HKLM\SOFTWARE\L-Mechrevo not present (nothing to remove)'
        return
    }
    $present = @()
    $item = Get-Item -LiteralPath $key -ErrorAction SilentlyContinue
    if ($item) {
        $present = @($item.Property | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        $present += @((Get-ChildItem -LiteralPath $key -ErrorAction SilentlyContinue) | ForEach-Object { 'subkey:' + $_.PSChildName })
    }
    if ($present.Count -eq 0) {
        Write-Log '  install marker key is present but empty'
    }
    else {
        Write-Log ('  install marker present: ' + ($present -join ', '))
    }
    Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $key) {
        Write-Log '  WARNING: HKLM\SOFTWARE\L-Mechrevo still exists after removal (may need elevation)'
    }
    else {
        Write-Log '  HKLM\SOFTWARE\L-Mechrevo removed (whole key: our own install marker)'
    }
}

# 2026-10-09: the one place that decides LM-owned vs vendor/shared, so the -LmOwnedOnly gate is
# readable and testable. Order kept from the pre-change script:
# autostart task -> [service: gated] -> firewall -> [driver: gated] -> install marker.
function Invoke-GcuUninstallCleanup {
    param([switch]$LmOwnedOnly, [switch]$KeepDriver, [switch]$PurgeVendor)

    # 2026-10-14: -LmOwnedOnly is the fail-safe and wins if both are passed, so a caller that
    # accidentally sets both can never trigger the vendor purge. Only the uninstaller passes
    # -PurgeVendor; the install/upgrade path never does.
    if ($LmOwnedOnly -and $PurgeVendor) {
        Write-Log '  both -LmOwnedOnly and -PurgeVendor given; -LmOwnedOnly wins (vendor steps skipped)'
        $PurgeVendor = $false
    }

    # N5: remove the installer-created highest-privileges autostart task first, so no boot-time
    # elevation entry point survives the uninstall. LM-owned: always runs, in both modes.
    Remove-AutostartTask

    if ($LmOwnedOnly) {
        Write-Log '  -LmOwnedOnly: GCUBridge service and the GCUService/GCUBridge processes are left alone (vendor/shared, also used by the official console)'
    }
    elseif ($PurgeVendor) {
        Write-Log '  -PurgeVendor (full uninstall): stopping and deleting the GCUBridge service'
        Remove-GcuService
    }
    else {
        Write-Log '  legacy full mode (not called by the installer): running the vendor/shared service step'
        Remove-GcuService
    }

    # LM-owned: the rule name is our own string, created by Install-Gcu.ps1's Ensure-FirewallRule.
    Remove-FirewallRule

    if ($LmOwnedOnly) {
        Write-Log '  -LmOwnedOnly: the UWACPIDriver entry in the Windows driver store is left alone (vendor/shared)'
    }
    elseif ($KeepDriver) {
        Write-Log '  -KeepDriver set; leaving UWACPIDriver installed'
    }
    else {
        # 2026-10-14 (owner: "delete everything" on a real uninstall): the driver-STORE entry is
        # still deliberately NOT removed, even in -PurgeVendor. Reason: uwacpidriver.inf is pushed
        # into the system-wide driver store by pnputil and is shared - the official ACPI path (and
        # the vendor console we are purging alongside) may still bind to it; pnputil /delete-driver
        # /uninstall /force also needs a reboot to fully settle and can leave the machine without a
        # working ACPI child device in between. So we only ever remove OUR OWN staged file copy
        # ({app}\UWACPIDriver, done by the uninstaller's [UninstallDelete]), and we say so loudly.
        Write-Log '  -PurgeVendor: driver-store entry for UWACPIDriver deliberately left alone (system-wide, shared, needs reboot); only our staged {app}\UWACPIDriver copy is removed'
    }

    # LM-owned: HKLM\SOFTWARE\L-Mechrevo is written only by our installer.
    Remove-InstallMarker

    if ($PurgeVendor) {
        Remove-VendorConsoleKey
    }
}

# 2026-10-15: -NoLogDir stops this script from CREATING the shared log dir just to write a log into it.
# Reason: the Inno uninstaller now removes <ProgramData>\L-Mechrevo as its very last step
# (install_script.iss, PurgeLmCommonAppData in usPostUninstall), so re-creating it here for an
# uninstall log would resurrect the directory the user just asked us to leave no trace of.
# The uninstaller's own log ({log} / /LOG) still records everything that matters.
try {
    if ([string]::IsNullOrWhiteSpace($LogDir)) {
        $LogDir = Join-Path $env:ProgramData 'L-Mechrevo\logs'
    }
    if (-not (Test-Path -LiteralPath $LogDir)) {
        if ($NoLogDir) {
            # Do not create it: the parent directory is the uninstaller's to delete, not ours to rebuild.
            $script:LogFile = $null
        } else {
            New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($LogDir) -and (Test-Path -LiteralPath $LogDir)) {
        $script:LogFile = Join-Path $LogDir ('gcu-uninstall-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
    }

    Write-Log '=== L-Mechrevo GCU payload uninstall ==='
    Write-Log ("TargetDir   = {0}" -f $TargetDir)
    Write-Log ("KeepDriver  = {0}" -f [bool]$KeepDriver)
    Write-Log ("LmOwnedOnly = {0}" -f [bool]$LmOwnedOnly)
    Write-Log ("PurgeVendor = {0}" -f [bool]$PurgeVendor)
    Write-Log ("DryRun      = {0}" -f [bool]$DryRun)

    if ($DryRun) {
        Write-Log 'DRY RUN: no system changes made'
        exit 0
    }

    Invoke-GcuUninstallCleanup -LmOwnedOnly:$LmOwnedOnly -KeepDriver:$KeepDriver -PurgeVendor:$PurgeVendor
    Write-Log 'GCU uninstall OK'
    exit 0
}
catch {
    if ($script:LogFile) {
        Write-Log ('FATAL: ' + $_.Exception.Message)
        Write-Log ($_.ScriptStackTrace)
    }
    else {
        Write-Error $_
    }
    exit 1
}
