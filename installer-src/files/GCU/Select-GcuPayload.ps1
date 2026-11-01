#Requires -Version 5.1
# 变体判定 + 7z 载荷映射（2026-10-14 重写；同日按 owner 纠偏更新落地口径）：随包载荷已从
# 「base + 覆盖层树」改成「5 个独立完整 7z」，安装期由 Install-Gcu.ps1 按选中变体解压并【直接】
# 落到
#   {app}\GCU\AiStoneService\...
# ⭐ 2026-10-14（owner 原话：「修改安装逻辑解压出来的 GCU 应该放在 安装目录的这个文件夹
#    C:\Program Files\L-Mechrevo\GCU\AiStoneService\ 而不是 shard，而 payload 应该作为临时文件
#    而不是安装到安装目录里面」）：⛔ 不再有 {app}\GCU\payload\<变体名>\ 这一层；5 个 7z 与
#    7za.exe 是安装期临时文件（[Files] Flags: dontcopy -> {tmp} -> 解压完即删），装完不留。
# ⭐ 所有变体统一落成 AiStoneService（1020/30 在包内叫 UniwillService，落地时改名，
#    见 Install-Gcu.ps1 的 Get-GcuServiceLeafName 及其理由注释）。
# ASCII-only by design (no BOM): Windows PowerShell 5.1 reads a .ps1 as ANSI unless it has a
# BOM, and this file runs both from the installer and by hand on GBK systems.
#
# ===========================================================================
# Naming conventions produced by Get-GcuPayloadArchive / Resolve-GcuVariant
# ===========================================================================
# Variant keys (authoritative value written to the registry):
#   '1020'  - legacy 10/16/20-series machines (GamingCenterU / UniwillService)
#   '30'    - 30-series
#   '40'    - 40-series (40B removed: 40-series is ONLY '40' now)
#   '50'    - 50-series
#   'WJXY'  - the special variant for XINGYAO / LNL machines
# Guest-facing strings: never emit raw keys; use the GcuVariantDisplay table.

param(
    [string]$Variant = "auto",
    [AllowEmptyString()][string[]]$GpuName,
    [AllowEmptyString()][string[]]$GpuDeviceId,
    [AllowEmptyString()][string[]]$MachineModel,
    [string]$LogFile,
    [string]$OutputFile,
    [switch]$AsJson,
    [switch]$SelfTest,
    # 2026-10-16 (r30): -SelfTest -Offline skips the local registry/CIM collection inside
    # Resolve-GcuVariant so the "no signal -> safety floor" cases stay deterministic on a
    # machine that HAS a real GPU (the collector is fixed now and would self-collect it).
    [switch]$Offline,
    [ValidateSet("SilentlyContinue","Continue")][string]$ErrorActionPreference = "Continue"
)

$ErrorActionPreference = $ErrorActionPreference

Set-StrictMode -Version 2.0

# ===========================================================================
# Machine-model judge: XINGYAO / LNL => the special WJXY variant.
# Owner rule (2026-10-14): if the machine string contains XINGYAO or LNL, go WJXY;
# otherwise fall through to the normal 30/40/50 judge. Never silently pick a variant:
# an unparseable model is logged and judged by the legacy rules.
# ===========================================================================
$script:WJXY_KEYS = "XINGYAO", "LNL"
$script:MACHINE_MODEL_KEYS = "Win32_ComputerSystem.Model", "Win32_BaseBoard.Product", "Win32_ComputerSystemProduct.Name", "SystemProductName"
$script:DISPLAY_NAMES = "MECHREVO", "MECHREVO_LITE", "GM5", "UMI", "YAOSHI", "YILONG", "LONGXIN", "WUJING", "XINGYAO", "SHINN", "ZHICHENG"
$script:MATCH_MODE = "Substring"
$script:CANON_CASE = "IgnoreCase"

function Test-WjxyMachine {
    <#
    .SYNOPSIS
        True when any of the machine-model strings contains XINGYAO or LNL.
    .DESCRIPTION
        Owner rule (2026-10-14): XINGYAO / LNL => WJXY special variant. Case-insensitive.
        The model strings are NOT sent anywhere; they only feed the local judge.
    #>
    param([Parameter(Mandatory=$false)][AllowEmptyString()][string[]]$Models)

    if ($null -eq $Models) { return $false }
    foreach ($m in $Models) {
        if ([string]::IsNullOrWhiteSpace($m)) { continue }
        foreach ($k in $script:WJXY_KEYS) {
            if ($m.IndexOf($k, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        }
    }
    return $false
}

function Get-MachineModelString {
    <#
    .SYNOPSIS
        Best-effort local read of the machine model. Returns '' when unknown (never throws).
    #>
    try { $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop } catch { $cs = $null }
    $candidates = @()
    if ($cs) { $candidates += $cs.Model }
    try { $bb = Get-CimInstance Win32_BaseBoard -ErrorAction Stop } catch { $bb = $null }
    if ($bb) { $candidates += $bb.Product }
    try { $p = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop } catch { $p = $null }
    if ($p) { $candidates += $p.Name }
    $envNames = @("SystemProductName", "PRODUCT_NAME")
    foreach ($n in $envNames) { $candidates += [Environment]::GetEnvironmentVariable($n) }
    foreach ($c in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($c)) { return $c.Trim() }
    }
    return ''
}

function Get-XingyaoEvidence {
    <#
    .SYNOPSIS
        One log line describing why WJXY was or was not chosen. Contains the machine string;
        that is already present in the diagnostics log (never uploaded).
    #>
    param([string]$Model, [bool]$Wjxy)

    $key = ''
    if ($Wjxy) {
        foreach ($k in $script:WJXY_KEYS) {
            if ($Model -and $Model.IndexOf($k, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $key = $k; break }
        }
    }
    return [pscustomobject]@{
        Variant = $(if ($Wjxy) { "WJ" } else { "" })
        Model   = $Model
        Key     = $key
        Reason  = $(if ($Wjxy) { "machine string contains '$key' -> WJXY special variant" } else { "machine string has no WJXY keyword -> legacy judge" })
    }
}

function Resolve-GcuVariant {
    <#
    .SYNOPSIS
        Decides the variant key. Priority: forced value, then XINGYAO/LNL, then the
        30/40/50/legacy judge, then the safety floor.
    .DESCRIPTION
        Forced values '1020' / '30' / '40' / '50' / 'WJXY' bypass hardware detection.
        Auto never returns '40B' - that variant is gone; 40-series is '40' only.
    #>
    param(
        [string]$Variant = "auto",
        [AllowEmptyString()][string[]]$GpuName = @(),
        [AllowEmptyString()][string[]]$GpuDeviceId = @(),
        [AllowEmptyString()][string[]]$MachineModel = @(),
        [switch]$Offline
    )

    $notes = New-Object System.Collections.ArrayList

    $forced = $null
    if ($Variant -and $Variant.Trim() -ne "" -and $Variant.Trim().ToLowerInvariant() -ne "auto") {
        $v = $Variant.Trim().ToUpperInvariant()
        $v = $v -replace "\s+", ""
        if ($v -eq "40B") {
            $notes.Add("variant '40B' is retired; treated as '40'") | Out-Null
            $v = "40"
        }
        if ($script:VariantKeys -contains $v) { $forced = $v } else {
            $notes.Add("unknown -Variant '$Variant' ignored; falling back to auto") | Out-Null
        }
    }

    $model = ''
    if ($MachineModel -and $MachineModel.Count -gt 0) {
        foreach ($m in $MachineModel) { if (-not [string]::IsNullOrWhiteSpace($m)) { $model = $m.Trim(); break } }
    }
    if ($model -eq '' -and -not $Offline) { $model = Get-MachineModelString }

    $wjxy = Test-WjxyMachine -Models @($model)

    if ($forced -eq "WJXY") {
        # WJXY compatibility alias: same LNL payload as WJ.
        $forced = "WJ"
        $notes.Add("variant forced to WJXY (alias of WJ)") | Out-Null
    }
    elseif ($forced) {
        $notes.Add("variant forced to $forced (hardware detection bypassed)") | Out-Null
    }
    elseif ($wjxy) {
        $forced = "WJ"
        $notes.Add("machine string '$model' contains a WJ keyword -> WJ special variant") | Out-Null
    }
    else {
        $gpuGen = Get-GpuGeneration -GpuName $GpuName -GpuDeviceId $GpuDeviceId -Offline:$Offline
        $notes.Add("GPU generation judge: $($gpuGen.Generation) ($($gpuGen.Evidence -join '; '))") | Out-Null
        switch ($gpuGen.Generation) {
            "GEN1020" { $forced = "1020"; $notes.Add("legacy 10/16/20-series machine -> 1020 payload") | Out-Null }
            "GEN30"   { $forced = "30";   $notes.Add("30-series -> 30 payload") | Out-Null }
            "GEN40"   { $forced = "40";   $notes.Add("40-series -> variant 40 (payload temporarily 50.7z; 40 archive not shipped, no prompt)") | Out-Null }
            "GEN50"   { $forced = "50";   $notes.Add("50-series -> 50 payload") | Out-Null }
            default   { $forced = "50";   $notes.Add("GPU generation unresolved -> safety floor 50 payload (no silent guessing)") | Out-Null }
        }
    }

    $archive = $script:Archives[$forced]
    if ($null -eq $archive) { throw "internal error: archive for variant '$forced' is not in the catalog" }

    return [pscustomobject]@{
        Variant        = $forced
        PayloadVariant = $archive.PayloadVariant
        Archive        = $archive.FileName
        ArchivePath    = $archive.RelPath
        DisplayName    = $archive.DisplayName
        Note           = ($notes -join " | ")
    }
}

# ===========================================================================
# Payload archives staged as install-time TEMPORARY files (2026-10-14, owner):
#   the setup extracts payload\*.7z + 7za.exe out of the package into {tmp}
#   ([Files] Flags: dontcopy + ExtractTemporaryFile) and Install-Gcu.ps1
#   deletes them again once the unpack succeeded. They are NOT installed into
#   {app}\GCU - after the install, {app}\GCU holds only AiStoneService\,
#   logo\Language\, README.txt / NOTICE.txt and the three .ps1 files.
# Install-Gcu.ps1 unpacks the chosen archive and lands its service directory
# directly as
#   {app}\GCU\AiStoneService\...        (EVERY variant, incl. 1020/30)
# ===========================================================================
$script:VariantKeys = @("1020", "30", "40", "50", "WJ", "WJXY")
$script:Archives = [ordered]@{
    # inner root -> variant name mapping (the 7z internal top folder differs from the
    # variant key for two entries and must be normalised during the unpack):
    #   1020.7z -> 20\UniwillService   (old UniwillService naming -> laid down as AiStoneService)
    #   30.7z   -> 30\UniwillService   (ditto)
    #   40.7z / 50.7z / WJXY.7z -> <name>\AiStoneService
    # ServiceDir is the name INSIDE the archive (used to locate the tree to copy);
    # it is NOT the name on disk - see Install-Gcu.ps1 Get-GcuServiceLeafName.
    "1020" = [pscustomobject]@{ Variant = "1020"; PayloadVariant = "1020"; FileName = "1020.7z"; RelPath = "payload\1020.7z"; InnerRoot = "20";    ServiceDir = "UniwillService"; DisplayName = "1020 series (legacy GamingCenterU)" }
    "30"   = [pscustomobject]@{ Variant = "30";   PayloadVariant = "30";   FileName = "30.7z";   RelPath = "payload\30.7z";   InnerRoot = "30";    ServiceDir = "UniwillService"; DisplayName = "30 series" }
    # 40 系当前实际安装的是 50 系 GCU 载荷；注册表必须记录实际载荷版本 50，而不是机器代际 40。
    "40"   = [pscustomobject]@{ Variant = "40";   PayloadVariant = "50";   FileName = "50.7z";   RelPath = "payload\50.7z";   InnerRoot = "50";    ServiceDir = "AiStoneService"; DisplayName = "40 series (temporary: 50 payload; 40 archive not shipped)" }
    "50"   = [pscustomobject]@{ Variant = "50";   PayloadVariant = "50";   FileName = "50.7z";   RelPath = "payload\50.7z";   InnerRoot = "50";    ServiceDir = "AiStoneService"; DisplayName = "50 series" }
    "WJ"   = [pscustomobject]@{ Variant = "WJ";   PayloadVariant = "WJ";   FileName = "WJ.7z";   RelPath = "payload\WJ.7z";   InnerRoot = "WJXY"; ServiceDir = "AiStoneService"; DisplayName = "WJ special variant (LNL Wujie; default)" }
    "WJXY" = [pscustomobject]@{ Variant = "WJXY"; PayloadVariant = "WJ";   FileName = "WJ.7z";   RelPath = "payload\WJ.7z";   InnerRoot = "WJXY"; ServiceDir = "AiStoneService"; DisplayName = "WJXY alias (compatibility)" }
}

# The archives are staged flat, one per variant, under the -StagingRoot the setup passes ({tmp}).
$script:StagedPayloadRel = "payload"

function Get-GcuPayloadArchive {
    <#
    .SYNOPSIS
        Maps a variant key to its archive file name (and the name its service directory has
        INSIDE the archive; every variant is laid down as AiStoneService, see
        Install-Gcu.ps1 Get-GcuServiceLeafName).
    #>
    param([Parameter(Mandatory=$true)][string]$Variant)

    $v = $Variant.Trim().ToUpperInvariant() -replace "\s+", ""
    if ($null -eq $script:Archives[$v]) { throw "unknown GCU variant '$Variant' (known: $($script:VariantKeys -join ', '))" }
    return $script:Archives[$v]
}

function Get-GpuGeneration {
    # GPU-generation classifier: display-class registry feeds both routes, then the
    # PCI device id judge decides FIRST, the GPU display name is only a fallback.
    # NO model-name guessing, NO SMBIOS parsing. Order matters for a stable judge.
    # -Offline (r30, SelfTest only): skip the local registry walk so the "no signal"
    # cases stay deterministic on a machine that HAS a real GPU.
    param(
        [AllowEmptyString()][string[]]$GpuName = @(),
        [AllowEmptyString()][string[]]$GpuDeviceId = @(),
        [switch]$Offline
    )

    $names = @(); $ids = @()
    foreach ($g in $GpuName) { if (-not [string]::IsNullOrWhiteSpace($g)) { $names += $g.Trim() } }
    foreach ($d in $GpuDeviceId) { if (-not [string]::IsNullOrWhiteSpace($d)) { $ids += $d.Trim() } }

    # 2026-10-16 (r30): the old collector walked "$class\*" with Get-ItemProperty
    # -ErrorAction Stop. The class key set contains a protected subkey (Properties)
    # that throws SecurityException on enumeration on real machines, so the whole
    # walk aborted and catch{} left BOTH routes empty -> UNKNOWN -> safety floor 50
    # (the exact r29 field failure on a 40-series machine). The collector below only
    # enumerates the numeric subkeys and reads each one with -ErrorAction
    # SilentlyContinue, so one unreadable key can no longer blank the signal set.
    # 2026-10-16 (r30, second fix): Get-ChildItem on the class key itself ALSO throws
    # SecurityException on real machines - the protected Properties subkey is touched
    # during enumeration. The collector therefore uses the .NET registry API directly:
    # OpenSubKey(...).GetSubKeyNames() lists every subkey name WITHOUT opening the
    # protected one, and only the numeric subkeys are then opened for values.
    $class = "SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
    try {
        if ($Offline) { throw "offline: local registry walk skipped" }
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Default').OpenSubKey($class)
        if ($root) {
            foreach ($sub in $root.GetSubKeyNames()) {
                if ($sub -notmatch '^[0-9]{4}$') { continue }
                $k = $root.OpenSubKey($sub)
                if (-not $k) { continue }
                $desc = [string]$k.GetValue('DriverDesc')
                if (-not $desc -or $desc -notmatch 'NVIDIA') { continue }
                # NOTE: under StrictMode 2.0 a single-string HardwareID value has no
                # Count property - the old code died on that (caught by catch{}) and the
                # whole collector came back empty on real machines. @() BEFORE .Count.
                $hwids = @(@($k.GetValue('HardwareID')) | Where-Object { $_ -and ("$_").ToUpperInvariant() -match 'DEV_[0-9A-Fa-f]{4}' })
                if (@($hwids).Count -gt 0) { $ids += @($hwids) }
                elseif (("$($k.GetValue('MatchingDeviceId'))").ToUpperInvariant() -match 'DEV_[0-9A-Fa-f]{4}') {
                    $ids += [string]$k.GetValue('MatchingDeviceId')
                }
                if ($desc) { $names += $desc }
            }
        }
    } catch { }

    # 2026-10-16 (r30, owner: "hardware id takes priority over the displayed name
    # because the displayed name can be modified"): the PCI device id judge runs
    # FIRST and is final; the display name is only a fallback used when the device
    # id route resolved nothing. If both routes give an answer, the device id wins
    # and the evidence line says so (name/device-id conflict resolved by device id).
    # Device id -> generation bands (NVIDIA):
    #   DEV_26xx-2Axx  Ada / 40-series          -> GEN40
    #   DEV_2Bxx+      Blackwell / 50-series    -> GEN50
    #   DEV_24xx-25xx  Ampere / 30-series       -> GEN30
    #   DEV_1Fxx-23xx  Turing/Ampere legacy     -> GEN30
    #   below 1F                                -> GEN1020 (pre-30 legacy)
    $devGen = $null
    $devEvidence = ''
    foreach ($id in $ids) {
        # registry MatchingDeviceId strings are typically lowercase - normalize first
        $m = [regex]::Match($id.ToUpperInvariant(), "DEV_([0-9A-Fa-f]{4})")
        if (-not $m.Success) { continue }
        $hex = $m.Groups[1].Value
        $mid = [Convert]::ToInt32($hex.Substring(0,2), 16)
        if ($mid -ge 0x26 -and $mid -le 0x2A) { $devGen = "GEN40"; $devEvidence = "PCI device id DEV_$hex maps to a 40-series-class device (Ada)"; break }
        if ($mid -ge 0x2B) { $devGen = "GEN50"; $devEvidence = "PCI device id DEV_$hex maps to a 50-series-class device (Blackwell)"; break }
        if ($mid -ge 0x24 -and $mid -le 0x25) { $devGen = "GEN30"; $devEvidence = "PCI device id DEV_$hex maps to a 30-series-class device (Ampere)"; break }
        if ($mid -ge 0x1F -and $mid -le 0x23) { $devGen = "GEN30"; $devEvidence = "PCI device id DEV_$hex maps to a 30-series-class device (late legacy)"; break }
        if ($mid -le 0x1E) { $devGen = "GEN1020"; $devEvidence = "PCI device id DEV_$hex maps to a pre-30 legacy device"; break }
    }

    $nameGen = $null
    $nameEvidence = ''
    foreach ($n in $names) {
        if ($n -match "RTX\s*50\d\d|RTX\s*5090|RTX\s*5080") { $nameGen = "GEN50";   $nameEvidence = "GPU name '$n' is a 50-series part"; break }
        if ($n -match "RTX\s*40\d\d|RTX\s*4090|RTX\s*4080|RTX\s*4070") { $nameGen = "GEN40";   $nameEvidence = "GPU name '$n' is a 40-series part"; break }
        if ($n -match "RTX\s*30\d\d|RTX\s*3080|RTX\s*3070|RTX\s*3060|MX\d{3}") { $nameGen = "GEN30";   $nameEvidence = "GPU name '$n' is a 30-series part"; break }
        if ($n -match "GTX\s*10\d\d|GTX\s*16\d\d|GTX\s*16[0-9]{2}") { $nameGen = "GEN1020"; $nameEvidence = "GPU name '$n' is a legacy 10/16-series part"; break }
    }

    if ($devGen) {
        if ($nameGen -and $nameGen -ne $devGen) {
            return [pscustomobject]@{ Generation = $devGen; Evidence = "$devEvidence; name/device-id conflict resolved by device id" }
        }
        return [pscustomobject]@{ Generation = $devGen; Evidence = $devEvidence }
    }
    if ($nameGen) {
        return [pscustomobject]@{ Generation = $nameGen; Evidence = "$nameEvidence; name-based fallback (device id did not resolve)" }
    }

    return [pscustomobject]@{ Generation = "UNKNOWN"; Evidence = "no usable NVIDIA signal (name/id/registry)" }
}

# ---------------------------------------------------------------------------
# Legacy aliases: keep the old function names working for callers that dot-source
# this file (Install-Gcu.ps1 is the main one).
# ---------------------------------------------------------------------------
function Select-GcuPayload {
    param(
        [string]$Variant = "auto",
        [AllowEmptyString()][string[]]$GpuName = @(),
        [AllowEmptyString()][string[]]$GpuDeviceId = @(),
        [AllowEmptyString()][string[]]$MachineModel = @()
    )
    return Resolve-GcuVariant -Variant $Variant -GpuName $GpuName -GpuDeviceId $GpuDeviceId -MachineModel $MachineModel
}

# ---------------------------------------------------------------------------
# CLI entry (kept from the previous shape so the installer's [Run] and manual
# invocations keep working): -Variant / -GpuName / -GpuDeviceId / -MachineModel /
# -AsJson / -SelfTest / -OutputFile. Dot-sourcing (Install-Gcu.ps1) still exposes
# the functions only, exactly as before.
# ---------------------------------------------------------------------------
function Invoke-SelfTest {
    $cases = @(
        @{ Name = 'XINGYAO -> WJXY';              Args = @{ MachineModel = @('MECHREVO XINGYAO Series') };                          Expect = 'WJXY' },
        @{ Name = 'LNL -> WJXY';                  Args = @{ MachineModel = @('EM_LNL326_V1.0.23') };                               Expect = 'WJXY' },
        @{ Name = 'DEV_2C19 -> 50 (Blackwell)';   Args = @{ MachineModel = @(''); GpuDeviceId = @('PCI\VEN_10DE&DEV_2C19') };       Expect = '50' },
        @{ Name = 'DEV_2820 -> 40 (Ada)';         Args = @{ MachineModel = @(''); GpuDeviceId = @('PCI\VEN_10DE&DEV_2820') };       Expect = '40' },
        @{ Name = 'DEV_2860 -> 40 (Ada)';         Args = @{ MachineModel = @(''); GpuDeviceId = @('PCI\VEN_10DE&DEV_2860') };       Expect = '40' },
        @{ Name = 'DEV_25xx -> 30 (Ampere)';      Args = @{ MachineModel = @(''); GpuDeviceId = @('PCI\VEN_10DE&DEV_2504') };       Expect = '30' },
        @{ Name = 'name-only RTX 4060 -> 40';     Args = @{ MachineModel = @(''); GpuName = @('NVIDIA GeForce RTX 4060 Laptop GPU') }; Expect = '40' },
        @{ Name = 'conflict: 40 id + 50 name';    Args = @{ MachineModel = @(''); GpuName = @('NVIDIA GeForce RTX 5060 Laptop GPU'); GpuDeviceId = @('PCI\VEN_10DE&DEV_2860') }; Expect = '40' },
        @{ Name = 'conflict: 50 id + 40 name';    Args = @{ MachineModel = @(''); GpuName = @('NVIDIA GeForce RTX 4060 Laptop GPU'); GpuDeviceId = @('PCI\VEN_10DE&DEV_2C19') }; Expect = '50' },
        @{ Name = 'no signal -> 50 safety floor'; Args = @{ MachineModel = @('') };                                                 Expect = '50' },
        @{ Name = 'forced 40B -> 40 (retired)';   Args = @{ Variant = '40B' };                                                     Expect = '40' },
        @{ Name = 'forced 30';                    Args = @{ Variant = '30' };                                                      Expect = '30' },
        @{ Name = 'forced 1020';                  Args = @{ Variant = '1020' };                                                    Expect = '1020' },
        @{ Name = 'forced WJXY';                  Args = @{ Variant = 'WJXY' };                                                    Expect = 'WJXY' },
        @{ Name = 'unknown -Variant -> judge';    Args = @{ Variant = '99'; MachineModel = @('') };                                 Expect = '50' }
    )
    $failed = 0
    foreach ($c in $cases) {
        $p2 = @{ Variant = 'auto'; GpuName = @(); GpuDeviceId = @(); MachineModel = @(); Offline = $true }
        foreach ($k in $c.Args.Keys) { $p2[$k] = $c.Args[$k] }
        $actual = ''
        try { $actual = (Resolve-GcuVariant @p2).Variant } catch { $actual = 'THROW' }
        $ok = ($actual -eq $c.Expect)
        if (-not $ok) { $failed++ }
        Write-Host ("  [{0}] {1,-34} -> {2} (expected {3})" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $c.Name, $actual, $c.Expect)
    }
    Write-Host ("SelfTest: {0} case(s), {1} failed" -f $cases.Count, $failed)
    return ($failed -eq 0)
}

if ($SelfTest) {
    if (-not (Invoke-SelfTest)) { exit 1 }
    exit 0
}

if ($MyInvocation.InvocationName -eq '.') {
    # Dot-sourced (e.g. by Install-Gcu.ps1): expose the functions only.
    return
}

$sel = Resolve-GcuVariant -Variant $Variant -GpuName @($GpuName) -GpuDeviceId @($GpuDeviceId) -MachineModel @($MachineModel)

# ---- judgement log: one line the field bundle can collect ----
# Format (single line, ASCII): GcuVariantSelection | variant=<V> | archive=<file> | note=<...>
$logLine = ("GcuVariantSelection | variant={0} | payload={1} | archive={2} | note={3}" -f $sel.Variant, $sel.PayloadVariant, $sel.Archive, $sel.Note)
if ($LogFile) {
    try { Add-Content -LiteralPath $LogFile -Value $logLine -Encoding ASCII -ErrorAction Stop } catch { }
}
# ⚠️ 2026-10-14（修 -AsJson 被污染）：这一行【只在不是 -AsJson 时】往 stdout 写。
#   原因：Install-Gcu.ps1 用 `& $selectorExe ... -AsJson` 捕获本脚本的 stdout 再 ConvertFrom-Json，
#   而 Write-Host 的输出在「被调用方捕获管道」的场景下会并进同一条 stdout 流 —— 于是前面多了
#   这一行人看的日志，ConvertFrom-Json 直接抛 "Invalid JSON primitive: GcuVariantSelection"，
#   整个 GCU 步骤 FATAL。日志不会丢：上面 $LogFile 那条照写，Install-Gcu.ps1 自己也会把
#   selection.Variant / Archive / Note 重新打成同格式的一行（见其 GcuVariantSelection Write-Log）。
if (-not $AsJson) { Write-Host $logLine }
if (-not [string]::IsNullOrWhiteSpace($OutputFile)) {
    # 升级回灌比较用的是实际载荷版本，不是机器代际选择键；40 系载荷实际为 50。
    Set-Content -LiteralPath $OutputFile -Value $sel.PayloadVariant -Encoding ASCII -Force
}

if ($AsJson) {
    $sel | ConvertTo-Json -Depth 4
}
else {
    Write-Host ("Variant    : {0} ({1})" -f $sel.Variant, $sel.DisplayName)
    Write-Host ("Archive    : {0}" -f $sel.ArchivePath)
    Write-Host ("Reason     : {0}" -f $sel.Note)
}