#Requires -Version 5.1
# ASCII-only by design (Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM).
# This is the GCU payload installer the Inno Setup package runs AFTER copying files.
# It installs the one shipped GCU payload, copies it under
# %ProgramFiles%\L-Mechrevo\GCU, verifies Authenticode, installs the UWACPI driver,
# registers + starts the GCUBridge service and adds the MQTT blocking firewall rule.
# The whole step is idempotent: re-running repairs instead of failing.
# 2026-10-09: the payload identity is now the manifest hash of the whole payload set the copy step
# lays down (see Get-GcuPayloadIdentity / Merge-GcuPayloadManifest), not the SHA256 of the single
# GCUService.exe - an overlay-only change used to leave the identity unchanged, so the VerifyOnly
# fast path skipped the copy and the new files never landed. The post-install self-check
# (Test-GcuPostInstall / Wait-GcuPostInstallFacts / Write-GcuInstallStatus plus a per-file payload
# landing check) is now wired into main's [post-install] step, which it never was before.
# 2026-10-14 (7z payload switch): the payload is now ONE 7z per variant (1020 / 30 / 40 / 50 /
# WJXY; no shared base tree, and 40B is gone - the 40-series has exactly one payload). This script
# selects a variant via Select-GcuPayload.ps1 and unpacks the matching 7z with 7za.exe into a temp
# dir. The payload identity is the manifest hash of that single complete tree (see
# Get-GcuPayloadIdentity / Merge-GcuPayloadManifest). The vendor AiStoneService\install.bat /
# uninstall.bat / uninstallReg.bat still ship inside the archives and are laid down with it;
# nothing calls them automatically (our own Install-Service / Uninstall-Gcu.ps1 stay authoritative).
#
# 2026-10-14 (owner correction - LANDING LAYOUT IS THE INSTALL TREE, NOT A TEMPLATE):
#   owner, verbatim: "the GCU unpacked into the install folder should go to
#   C:\Program Files\L-Mechrevo\GCU\AiStoneService\ and NOT under shard; and payload should be a
#   TEMPORARY file instead of being installed into the install folder", plus
#   "keep the ps1 (the uninstaller needs them), do NOT keep 7z, do NOT keep the GCU variants that
#   were not used".
#   => the unpacked tree now lands DIRECTLY at {app}\GCU\AiStoneService (no
#      payload\<Variant>\<ServiceDir> normalisation level at all), the archive's own service
#      directory name is NOT kept (1020/30 unpack to UniwillService inside the 7z but are laid
#      down as AiStoneService, see Get-GcuServiceLeafName), and the 5 archives + 7za.exe are
#      install-time TEMPORARY files: the setup copies them into {tmp} ([Files] Flags: dontcopy +
#      ExtractTemporaryFile) and this script deletes the archive plus 7za.exe as soon as the
#      unpack succeeded, so neither ever ends up under {app}\GCU.
#   => because the unpacked tree and the destination are now the SAME directory, the 7z is unpacked
#      to {tmp} first and the copy into {app}\GCU\AiStoneService happens only AFTER the vendor
#      files were signature-verified (see Expand-GcuPayloadArchive / Test-VendorPayloadSource and
#      the [4/8] landing in main), so a truncated/corrupt archive can never leave a half-landed
#      install tree behind - it aborts while the existing payload is still untouched.
#   => {app}\GCU\ holds exactly: AiStoneService\ (the payload) + logo\Language\ (shared) + the
#      three .ps1 that the uninstaller/selector need. UWACPIDriver\ lives in the APPLICATION ROOT
#      ({app}\UWACPIDriver). Nothing else. (README.txt / NOTICE.txt land in {app}\GCU as plain
#      [Files] entries, matching the old structure's base-level GCU\ root.)
<#
.SYNOPSIS
    Install the shipped GCU vendor payload bundled by L-Mechrevo.

.DESCRIPTION
    -StagingRoot is the directory holding the two install-time TEMPORARY inputs this script
    consumes and deletes: the selected archive under payload\*.7z and 7za.exe. The setup extracts
    both into {tmp} ([Files] Flags: dontcopy + ExtractTemporaryFile) and passes that {tmp} path, so
    neither file is ever installed into the application directory. It is NOT the payload source of
    the tree that gets laid down any more - since 2026-10-14 the only payload source is the archive
    itself.

.EXAMPLE
    # what the setup's [Run] entry does (archives already extracted into {tmp} by then)
    powershell -NoProfile -ExecutionPolicy Bypass -File Install-Gcu.ps1 `
        -StagingRoot "$env:TEMP\..." `
        -TargetDir  "C:\Program Files\L-Mechrevo\GCU"

.EXAMPLE
    # by hand, from a directory holding payload\<Variant>.7z and 7za.exe
    powershell -NoProfile -ExecutionPolicy Bypass -File Install-Gcu.ps1 `
        -StagingRoot "D:\staging" `
        -TargetDir  "C:\Program Files\L-Mechrevo\GCU" -Variant 40
#>
[CmdletBinding()]
param(
    # Install-time TEMPORARY staging directory: it must hold payload\<Variant>.7z and 7za.exe, and
    # both are deleted from it once the unpack succeeded (owner: "payload should be a TEMPORARY
    # file instead of being installed into the install folder"). The setup passes {tmp}.
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    # The GCU directory of the install tree ({app}\GCU). The unpacked payload lands in
    # $TargetDir\AiStoneService, the shared language files in $TargetDir\logo\Language.
    [Parameter(Mandatory = $true)][string]$TargetDir,
    # '30' is required by install_script_30gcu.iss ([Run] passes -Variant 30). Without it the
    # parameter binder rejects the call and the script dies before its first line (exit 1, no log).
    # The set must contain every value Select-GcuPayload.ps1 can hand back:
    # 2026-10-14 (7z payload switch): Auto / 1020 / 30 / 40 / 50 / WJXY.
    # 40B is GONE: the 40-series has exactly one payload (40.7z); -Variant 40B is accepted and
    # treated as '40' for compatibility, and the selector never hands it back either.
    [ValidateSet('Auto','1020','50','40','30','40B','WJ','WJXY')][string]$Variant = 'Auto',
    [string]$LogDir,
    [string]$InstallerVersion,
    [string]$StatusDir,
    [string]$AppExe,
    [string]$ConfigDir,
    [switch]$NoStart,
    [switch]$SinglePayload,
    # ⭐ 2026-10-15（owner 的解压失败兜底）：**载荷已由人工解压放好** ⇒ 跳过解包，
    # 在 $TargetDir 里找 AiStoneService（容忍多一层变体目录），校验 文件数/总字节/关键文件签名 后，
    # 继续走原有的落地与系统步骤（[3/8]~[8/8]）。`安装GCU.cmd`（解压失败时由本脚本写到 {app}\GCU\）就是用它调的。
    [switch]$PayloadReady,
    # ---------------------------------------------------------------------
    # Install-time removal of the vendor "official console" (2026-10-09, owner): OPTIONAL now.
    # ⭐ 2026-10-15 (owner, "并提示是否保留原版控制台"): **默认 = 保留**（原为移除）。
    # The Inno package's install-time question now asks whether to KEEP it (default button = keep)
    # and passes exactly one of these:
    #   -RemoveVendorConsole = remove (only when the user explicitly chose to remove)
    #   -KeepVendorConsole   = keep   (the default; also what a silent install gets, since it never asks)
    # Both switches given -> remove wins (logged). These switches carry no default of their own
    # ($false when absent); the ONE place the default is defined is the $VendorConsoleAction
    # assignment in main (search "SINGLE SOURCE OF TRUTH") - change that single word to flip it.
    [switch]$RemoveVendorConsole,
    [switch]$KeepVendorConsole,
    # ⭐ 2026-10-15：解包器选择（只影响"用哪个工具解压"）。auto = tar 优先/7za 兜底（发布默认）；
    #   tar = 只用系统 tar（失败不兜底）；7z = 只用随包 7za.exe（"纯 7za 测试包"用这个）。
    [ValidateSet('auto', 'tar', '7z')]
    [string]$UnpackTool = 'auto',
    [switch]$DryRun
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:RuleName = 'L-Mechrevo - Block remote GCU MQTT'
# Every bundled vendor payload registers the same service name, so the "legacy registration" is
# the same name installed by the vendor install.bat with a different binPath; the uninstall-first
# step removes it by name before the new payload is registered.
$script:ServiceName = 'GCUBridge'
$script:LegacyServiceNames = @('GCUBridge')
$script:LogFile = $null
# The single directory name the unpacked payload is laid down under inside {app}\GCU. Constant for
# every variant - see Get-GcuServiceLeafName for the reasoning (1020/30 unpack to UniwillService
# inside their archives and are laid down as AiStoneService).
$script:GcuServiceLeafName = 'AiStoneService'
# Install-time temporary staging files (filled in by main, deleted right after the landed copy).
$script:StagingArchive = $null
$script:StagingSevenZip = $null
# The private unpack directory (filled in by main, best-effort removed by the catch block).
$script:UnpackRoot = $null
# ⭐ 2026-10-15（-PayloadReady 人工解压兜底）：这几个**必须在这里初始化** —— 本脚本是 StrictMode 2.0，
# 未初始化的 `$script:` 变量一读就抛（实测：手工路径下 `$script:GcuWorkBase` 未设 ⇒ 用户数据备份那步直接 FATAL ❌）。
$script:ManualPayloadLanding = $false
$script:LandedPayloadDir = $null
$script:GcuWorkBase = ''
$script:ManualPayloadDir = $null

function Write-Log {
    param([string]$Message)
    $line = ('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $Message)
    if ($script:LogFile) {
        Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
    }
    Write-Host $line
}

function Get-GcuServiceLeafName {
    # The directory NAME the unpacked payload is laid down under, inside {app}\GCU.
    #
    # 2026-10-14 (owner correction): every variant lands as 'AiStoneService', including the two
    # archives whose INTERNAL service folder is called 'UniwillService' (1020.7z -> 20\
    # UniwillService, 30.7z -> 30\UniwillService). Reasons, all checked before deciding:
    #   * the vendor full layout is `<ServiceDir>\MyControlCenter\GCUService.exe` at a FIXED depth
    #     under the GCU root. If the leaf name were kept, the unpacked tree inside the 7z has the
    #     SAME shape as its destination and a copy into an existing 'UniwillService' could nest
    #     (…\UniwillService\UniwillService); a single, constant destination removes that entire
    #     class of failure.
    #   * not renaming means {app}\GCU\<X> depends on which archive was selected, so every
    #     downstream path - our app, the uninstaller, the support docs, the [UninstallDelete]
    #     entries - would need to know the variant. With one name there is ONE path to check.
    #   * the vendor binary does NOT read its own directory name: service.ini is only
    #     "[serviceinit]\nprocess=True" and it holds no path, and the only hard-coded product path
    #     inside the archives is 'C:\Program Files\OEM\Control Center\AiStoneService\...' (the
    #     vendor's own install location, already AiStoneService) in
    #     MyControlCenter\Command\ControlCenter.bat - so renaming cannot break a self-reference,
    #     while it makes the older UniwillService archives agree with the string the vendor itself
    #     uses everywhere else.
    return 'AiStoneService'
}

function Assert-RequiredFile {
    # A missing required file must abort BEFORE anything is copied into the install tree.
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("Missing required file ({0}): {1}" -f $Label, $Path)
    }
}

function Assert-SignedFile {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("Missing required file ({0}): {1}" -f $Label, $Path)
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ([string]$sig.Status -ne 'Valid') {
        throw ("Authenticode verification failed ({0}): {1} -> {2}" -f $Label, $Path, $sig.Status)
    }
    $subject = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '' }
    Write-Log ("  signature OK ({0}): {1} [{2}]" -f $Label, (Split-Path $Path -Leaf), $subject)
}

function Stop-GcuLockersForCopy {
    # 2026-10-08 (fix: upgrade copy blocked by a live prior generation): best-effort release of the
    # files a still-running GCU service/process may hold open. Idempotent, logged, never throws 鈥?
    # it is only a helper for the Copy-Tree retry loop below, not a substitute for the setup-side stop.
    try {
        $svc = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $svc -and $svc.Status -ne 'Stopped') {
            Write-Log ("  retry: stopping service {0} (status={1})" -f $script:ServiceName, $svc.Status)
            Stop-Service -Name $script:ServiceName -Force -ErrorAction SilentlyContinue
            $waited = 0
            while ($svc.Status -ne 'Stopped' -and $waited -lt 20) {
                Start-Sleep -Milliseconds 500
                $svc.Refresh()
                $waited++
            }
            Write-Log ("  retry: service {0} -> {1} after {2} ms" -f $script:ServiceName, $svc.Status, ($waited * 500))
        }
        foreach ($name in @('GCUService', 'GCUBridge')) {
            foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
                Write-Log ("  retry: stopping leftover process {0} (pid {1})" -f $name, $proc.Id)
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
    catch {
        Write-Log ("  retry: stop attempt failed (ignored): {0}" -f $_.Exception.Message)
    }
}

function Copy-Tree {
    param([string]$Source, [string]$Destination)
    if (-not (Test-Path -LiteralPath $Source)) {
        throw ("Payload source directory not found: {0}" -f $Source)
    }
    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }
    $robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
    if (Test-Path -LiteralPath $robocopy) {
        # 2026-10-08: robocopy >= 8 (failure class) is retried a few times before giving up.
        # Why: an upgrade can still have a previous-generation GCU process/service holding one of the
        # target files (observed: exit 11 copying payload\GCU\AiStoneService) 鈥?that lock is usually
        # transient (service being torn down), so a short bounded retry turns a fatal abort into a
        # successful install. Real errors still abort after the last attempt.
        $maxAttempts = 3
        $code = 0
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            Write-Log ("  robocopy `"{0}`" -> `"{1}`" (attempt {2}/{3})" -f $Source, $Destination, $attempt, $maxAttempts)
            & $robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            $code = $LASTEXITCODE
            if ($code -lt 8) { return }
            Write-Log ("  robocopy returned {0} (failure class) copying {1} -> {2}" -f $code, $Source, $Destination)
            if ($attempt -lt $maxAttempts) {
                Write-Log ("  retrying in 2s (attempt {0}/{1}); releasing possible file-lock holders first" -f ($attempt + 1), $maxAttempts)
                Stop-GcuLockersForCopy
                Start-Sleep -Seconds 2
            }
        }
        throw ("robocopy failed ({0}) copying {1} -> {2} after {3} attempts" -f $code, $Source, $Destination, $maxAttempts)
    }
    else {
        Write-Log ("  Copy-Item -Recurse `"{0}`" -> `"{1}`"" -f $Source, $Destination)
        Copy-Item -LiteralPath (Join-Path $Source '*') -Destination $Destination -Recurse -Force
    }
}

function Get-ServiceBinPath {
    $service = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $script:ServiceName) -ErrorAction SilentlyContinue
    if ($null -eq $service) { return $null }
    return [string]$service.PathName
}

function Assert-Port13688Free {
    # A single owner: after the prior service is gone, nothing may still be listening on 13688.
    $cmd = Get-Command -Name Get-NetTCPConnection -ErrorAction SilentlyContinue
    if (-not $cmd) { return }
    $owners = @(Get-NetTCPConnection -LocalPort 13688 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($owners.Count -gt 0) {
        $pid0 = $owners[0]
        $proc = Get-Process -Id $pid0 -ErrorAction SilentlyContinue
        throw ("TCP 13688 is still owned by PID {0} ({1}) after the prior-generation uninstall; aborting install" -f $pid0, $(if ($proc) { $proc.ProcessName } else { 'unknown' }))
    }
}

function Test-ExpectedBinPath {
    param([string]$PathName, [string]$ExpectedExe)
    if ([string]::IsNullOrWhiteSpace($PathName)) { return $false }
    $actual = $PathName.Trim().Trim('"')
    return ($actual.TrimEnd('"') -ieq $ExpectedExe)
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("file not found for hashing: {0}" -f $Path)
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StringSha256 {
    # SHA256 over the UTF-8 bytes of a canonical string; lowercase hex, same shape as Get-FileSha256.
    param([Parameter(Mandatory = $true)][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
        $hex = New-Object System.Text.StringBuilder
        foreach ($b in $bytes) { [void]$hex.Append($b.ToString('x2')) }
        return $hex.ToString()
    }
    finally { $sha.Dispose() }
}

function Get-ShortHash {
    param([string]$Hash)
    if ([string]::IsNullOrEmpty($Hash)) { return '' }
    if ($Hash.Length -le 12) { return $Hash }
    return $Hash.Substring(0, 12)
}

# Package documentation, never payload: excluded from the manifest so a README dropped next to a
# vendor tree cannot move the identity. In the shipped trees these files live OUTSIDE the copied
# AiStoneService directory anyway (payload\<variant>\README.txt, NOTICE.txt, the vendor .bat files),
# so today this list is defensive only; every hit is logged by New-GcuManifestObject.
$script:GcuManifestExcludedLeafNames = @('readme.txt', 'notice.txt', 'license.txt', 'readme.md')

function ConvertTo-GcuManifestPath {
    # Canonical relative path: lowercase, backslash separated, no leading separator.
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$FullName)
    $trimmed = $Root.TrimEnd('\')
    $rel = $FullName.Substring($trimmed.Length)
    return $rel.Replace('/', '\').TrimStart('\').ToLowerInvariant()
}

function New-GcuManifestObject {
    # Sort + canonicalize + hash. Deliberately deterministic across machines and runs: entry paths
    # are already lowercased and backslash-separated, the sort is ORDINAL on the canonical path (no
    # culture involved) and the join is "\n", so the same payload bytes always produce one value.
    param([object[]]$Entries, [string[]]$Roots)
    $map = @{}
    $excluded = New-Object System.Collections.ArrayList
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        $rel = [string]$entry.RelPath
        if ($script:GcuManifestExcludedLeafNames -contains (Split-Path $rel -Leaf)) {
            [void]$excluded.Add($rel)
            continue
        }
        $map[$rel] = $entry
    }
    if ($map.Count -eq 0) {
        throw ("payload manifest is empty (no files to lay down) under: {0}" -f (@($Roots) -join ', '))
    }
    $paths = [string[]]@($map.Keys)
    [array]::Sort($paths, [System.StringComparer]::Ordinal)
    $lines = New-Object System.Collections.ArrayList
    $total = [int64]0
    foreach ($p in $paths) {
        $e = $map[$p]
        [void]$lines.Add(('{0}|{1}|{2}' -f $e.RelPath, $e.Size, $e.Sha256))
        if ([int64]$e.Size -gt 0) { $total += [int64]$e.Size }
    }
    $canonical = ($lines -join "`n")
    if ($excluded.Count -gt 0) {
        Write-Log ("  payload manifest: skipped {0} documentation file(s): {1}" -f $excluded.Count, ($excluded -join ', '))
    }
    return [pscustomobject]@{
        Sha256     = (Get-StringSha256 -Text $canonical)
        Entries    = @($paths | ForEach-Object { $map[$_] })
        Canonical  = $canonical
        Roots      = @($Roots)
        FileCount  = $paths.Count
        TotalBytes = $total
    }
}

function Get-GcuPayloadIdentity {
    # Identity = manifest hash of the payload file set the copy step lays down: for every file the
    # canonical relative path plus its byte size plus its SHA256, one line per file, the lines sorted
    # by path, joined with "\n" and hashed once with SHA256.
    #
    # 2026-10-09 (fix: "overlay install never landed"): the identity used to be the SHA256 of
    # MyControlCenter\GCUService.exe alone. payload\40 grew from 3 to 13 files while GCUService.exe
    # stayed byte-identical, so the identity did not change and the VerifyOnly fast path in main
    # skipped robocopy entirely - the 10 added files never reached the target (measured on this
    # machine: the installed GCUBridge.exe stayed at 682,208 B).
    # The FileVersion is deliberately NOT part of identity: the payloads do not share a version line
    # (1.2.0.0 vs 1.0.2.70) and same-named files can share a version while differing by hash.
    # Cost: one SHA256 per file (payload\30 = 163 files, payload\GCU = 237, the 40 overlay = 5, the
    # 50 overlay = 4); a few seconds of one-off hashing per install, which is accepted for the
    # ability to see a file-set change at all.
    #
    # -ExpectEntries restricts the hash to a caller-supplied relative-path set (the installed-side
    # probe: pass the incoming manifest's entries and an identical value comes back only if every one
    # of those files is present with the same size and SHA256). A missing or unreadable file is
    # recorded with a sentinel size/hash so the value necessarily differs.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceDir,
        [object[]]$ExpectEntries
    )
    if (-not (Test-Path -LiteralPath $ServiceDir)) {
        throw ("payload service directory not found: {0}" -f $ServiceDir)
    }
    $root = (Get-Item -LiteralPath $ServiceDir).FullName
    $entries = New-Object System.Collections.ArrayList
    if ($null -ne $ExpectEntries) {
        foreach ($expected in @($ExpectEntries)) {
            $rel = [string]$expected.RelPath
            $full = Join-Path $root $rel
            $size = [int64](-1)
            $hash = '<missing>'
            if (Test-Path -LiteralPath $full -PathType Leaf) {
                $size = [int64](Get-Item -LiteralPath $full).Length
                try { $hash = Get-FileSha256 -Path $full }
                catch { $hash = '<unreadable>' }
            }
            [void]$entries.Add([pscustomobject]@{ RelPath = $rel; Size = $size; Sha256 = $hash })
        }
    }
    else {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
            $rel = ConvertTo-GcuManifestPath -Root $root -FullName $file.FullName
            $hash = '<unreadable>'
            try { $hash = Get-FileSha256 -Path $file.FullName } catch { }
            [void]$entries.Add([pscustomobject]@{ RelPath = $rel; Size = [int64]$file.Length; Sha256 = $hash })
        }
    }
    return (New-GcuManifestObject -Entries @($entries) -Roots @($root))
}

function Merge-GcuPayloadManifest {
    # The set the copy step really lays down. Since 2026-10-14 each variant is ONE complete tree
    # (the unpacked 7z) - there is no overlay merge any more - so a single manifest is the normal
    # case; the array parameter is kept so historical callers still work.
    # The merged manifest is what GcuPayloadSha256 records and what the landing check verifies.
    [CmdletBinding()]
    param([object[]]$Manifests)
    $map = @{}
    $roots = New-Object System.Collections.ArrayList
    foreach ($manifest in @($Manifests)) {
        if ($null -eq $manifest) { continue }
        foreach ($e in @($manifest.Entries)) { $map[[string]$e.RelPath] = $e }
        foreach ($r in @($manifest.Roots)) { [void]$roots.Add([string]$r) }
    }
    return (New-GcuManifestObject -Entries @($map.Values) -Roots @($roots))
}

function Test-GcuPayloadLanded {
    # Core post-install self-check item: every file of the expected manifest must exist under
    # TargetDir with the exact byte size and SHA256 the manifest recorded. The source trees are
    # byte-identical to what robocopy copies, so a mismatch means the copy step did not land.
    # This is a REAL failure, unlike "service not ready yet", which is only a warning.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Manifest,
        [Parameter(Mandatory = $true)][string]$TargetDir
    )
    $missing = New-Object System.Collections.ArrayList
    $mismatched = New-Object System.Collections.ArrayList
    $okCount = 0
    foreach ($e in @($Manifest.Entries)) {
        $full = Join-Path $TargetDir ([string]$e.RelPath)
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            [void]$missing.Add([string]$e.RelPath)
            continue
        }
        $size = [int64](-1)
        $hash = '<unreadable>'
        try {
            $size = [int64](Get-Item -LiteralPath $full).Length
            $hash = Get-FileSha256 -Path $full
        }
        catch { }
        if ($size -ne [int64]$e.Size -or $hash -ne [string]$e.Sha256) {
            [void]$mismatched.Add(('{0} (expected {1} B/{2}, found {3} B/{4})' -f $e.RelPath, $e.Size, (Get-ShortHash ([string]$e.Sha256)), $size, (Get-ShortHash $hash)))
            continue
        }
        $okCount++
    }
    $checked = @($Manifest.Entries).Count
    $ok = (($missing.Count -eq 0) -and ($mismatched.Count -eq 0))
    $detail = if ($ok) {
        ("all {0} payload file(s) present with the expected size and SHA256 under {1}" -f $okCount, $TargetDir)
    }
    else {
        ("{0}/{1} payload file(s) verified under {2}; missing={3} mismatched={4} (first problem: {5})" -f $okCount, $checked, $TargetDir, $missing.Count, $mismatched.Count, $(if ($missing.Count -gt 0) { 'missing ' + $missing[0] } else { 'differs ' + $mismatched[0] }))
    }
    return [pscustomobject]@{
        Ok         = $ok
        Checked    = $checked
        OkCount    = $okCount
        Missing    = @($missing)
        Mismatched = @($mismatched)
        Check      = [pscustomobject]@{ Name = 'payload-landing'; Ok = $ok; Detail = $detail }
    }
}

function Compare-PayloadIdentity {
    # Manifest-hash-only comparison (see Get-GcuPayloadIdentity). There is intentionally no version
    # parameter: version numbers cannot order or identify these payloads (unshared version line,
    # same-name/different-hash).
    # Compatibility: machines installed before 2026-10-09 recorded the single-file
    # (MyControlCenter\GCUService.exe) hash under the same value name, so the first upgrade to this
    # build necessarily compares 'Different' and re-lays the whole payload once - that is the wanted
    # repair, not a downgrade.
    [CmdletBinding()]
    param(
        [string]$InstalledSha256,
        [string]$IncomingSha256
    )
    if ([string]::IsNullOrWhiteSpace($InstalledSha256)) { return 'NotInstalled' }
    if ($InstalledSha256 -ieq $IncomingSha256) { return 'Same' }
    return 'Different'
}

function Remove-GcuStagingTemporaries {
    <#
    .SYNOPSIS
        Deletes the install-time TEMPORARY inputs: the selected archive and 7za.exe.

    .DESCRIPTION
        2026-10-14 (owner): "payload should be a TEMPORARY file instead of being installed into the
        install folder" + "do NOT keep 7z". The setup extracts payload\<Variant>.7z and 7za.exe out
        of the package into {tmp} (they have Flags: dontcopy, so they are NOT part of the installed
        file set); this removes them as soon as the unpack is done. Best effort and never fatal - a
        leftover file in {tmp} is harmless, the OS cleans {tmp} up, and a failure here must never
        turn a good install into a failed one. Logged either way so the field log says what is left.
    #>
    param([string]$ArchivePath, [string]$SevenZipPath)
    foreach ($path in @($ArchivePath, $SevenZipPath)) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        if (-not (Test-Path -LiteralPath $path)) {
            Write-Log ("  temporary staging file already gone: {0}" -f $path)
            continue
        }
        try {
            Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            Write-Log ("  temporary staging file deleted: {0}" -f $path)
        }
        catch {
            Write-Log ("  WARNING: could not delete temporary staging file {0} ({1}); it is not installed into the app directory, so this is harmless" -f $path, $_.Exception.Message)
        }
    }
}

function New-GcuUnpackDir {
    # ⭐ 2026-10-15（真机「50 系装不上，7za.exe failed with exit code 2」）：解包落点**带回退 + 真写探针**。
    # 动机：%TEMP% 在部分机器上不可用 —— 被安全软件/组策略限制、被「存储感知 / 磁盘清理」在解包过程中清掉、
    #   或临时卷满 / 当前账户无写权限。7za 对这种拒绝**只报** `Can not open output file` + 退出码 2，
    #   与「归档损坏」难以区分（真机日志里就只剩一行 exit code 2，无从定位 —— 见下方 Expand 里的观测补丁）。
    # 顺序：%TEMP% → %ProgramData%\L-Mechrevo\tmp → %SystemRoot%\Temp。
    #   ⚠️ 三个候选都在 {app} 之外，不违反「payload 不落在安装目录」的既定策略。
    #   ⚠️ **只有真能写文件才算可用**（"建得出目录" ≠ "写得进文件" —— 本机沙箱里出现的正是这种半通状态）。
    # ⚠️ 候选路径用**纯字符串拼接**构造，不要用 Join-Path —— Join-Path 会去解析驱动器提供程序，
    # 遇到不存在的盘（例如 TEMP 指向已拔掉的 U 盘/Z:）会在**进入 try 之前**抛 "Cannot find drive"，
    # 于是回退逻辑根本轮不到执行（本机实测踩过）。拼接出来的路径交给下面各自的 try/catch 处理。
    $bases = @()
    if ($env:TEMP)        { $bases += ($env:TEMP.TrimEnd('\') + '\lm-gcu-unpack') }
    if ($env:ProgramData) { $bases += ($env:ProgramData.TrimEnd('\') + '\L-Mechrevo\tmp\lm-gcu-unpack') }
    if ($env:SystemRoot)  { $bases += ($env:SystemRoot.TrimEnd('\') + '\Temp\lm-gcu-unpack') }
    # ⭐ 2026-10-15（真机 50 系极光X Pro：C 盘可能不够 56 MB 解压空间 / 安全软件拦 %TEMP%）：
    # **最后手段** = 安装目录所在盘（$TargetDir 里一个专用子目录）。它必然有余量（我们正要往那写几十 MB），
    # 且与 %TEMP% 分属不同卷、不受临时目录清理/拦截影响。
    # ⚠️ 取舍：这会在 {app}\GCU 下**短暂**出现一个 `_gcu_unpack`（解包结束即 Remove-Item）；
    #    仅在前面 3 个候选全不可用时才会走到 ⇒ 正常情况下**永不出现**，不违反「payload 不留安装目录」的既定策略。
    if ($TargetDir)       { $bases += ($TargetDir.TrimEnd('\') + '\_gcu_unpack') }
    $errors = @()
    foreach ($base in $bases) {
        $dir = $base + '-' + [guid]::NewGuid().ToString('N')
        try {
            New-Item -ItemType Directory -Path $dir -Force -ErrorAction Stop | Out-Null
            $probe = $dir + '\write-probe.tmp'
            Set-Content -LiteralPath $probe -Value 'ok' -Encoding Ascii -ErrorAction Stop
            Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
            # 记下**探针验证过的基目录**：后面所有"需要个可写临时位置"的地方（例如用户数据备份，
            # 见 $backupRoot）都复用它 —— ⛔ 别再去 Join-Path $env:TEMP（TEMP 不可用时那会直接抛
            # "Cannot find drive" 把整个安装打断，真机实测踩过）。
            $script:GcuWorkBase = $base
            return $dir
        } catch {
            Write-Log ("  unpack candidate unusable: {0} => {1}" -f $dir, $_.Exception.Message)
            $errors += ("{0} => {1}" -f $dir, $_.Exception.Message)
        }
    }
    throw ("no writable unpack directory (tried: " + ($errors -join ' | ') + ")")
}

# ===========================================================================
# ⭐⭐ 2026-10-15（owner：「如果实在是解压失败，直接把 7z 包放在 GCU 对应位置，弹出文件夹，
#     引导用户手动解压，然后运行『安装GCU』脚本」）：**最后一道兜底**。
#
# 触发条件（三条同时满足）：
#   ① 自动解包**彻底失败**（tar 与 7za 都不行）；
#   ② ⭐ 归档本身**完好**（大小 + SHA256 == 出厂值）—— 归档真坏了绝不让用户手工装 ❌；
#   ③ 目标目录里**还没有**合格的载荷（人工已放好的话就走 -PayloadReady 那条路 ✅）。
# 兜底动作：
#   ① 把该变体的 7z 复制到 {app}\GCU\<变体>.7z（⭐ 就放 GCU 对应位置，⛔ 不放桌面）✅
#   ② 写 `安装GCU.cmd`（⭐ 纯 ASCII，自提权后调用本脚本 -PayloadReady）✅
#   ③ 写 `手动安装说明.txt`（⭐ UTF-8 BOM，记事本可正常显示中文）✅
#   ④ ⭐ **弹出该文件夹**（explorer.exe）；-DryRun 下不弹窗、不弹框 ✅
#   ⑤ ⭐ 退出码 = **0**、状态 = **Warning**（⭐ 应用本体照常装上，不把整场安装判成失败）✅
# 用户手动解压 + 双击 `安装GCU.cmd`（或重跑安装程序）⇒ 走 `-PayloadReady`：
#   跳过解包/落地，只做 [5/8]~[8/8]（驱动 / 服务 / 防火墙 / 权限）✅
# ===========================================================================

# 出厂值：归档大小 + SHA256（判断"解包失败"到底是工具被拦还是归档坏了）
$script:PayloadArchiveFactory = @{
    '1020' = @{ Size = 9167136;  Sha256 = '3F8835ADC7E4140734A68FB33344818DB57836DA6FC687513CB0949090DB3163'; Name = '1020.7z' }
    '30'   = @{ Size = 8802015;  Sha256 = '199A5124B1B700856BB77BBDD6133713DBBA47307E0B90454387011BE3BD54DE'; Name = '30.7z' }
    '40'   = @{ Size = 17106392; Sha256 = '17826E53405C7B7C02FBB3DDF5828EA5B15292BA507956D885D337741F7BB3B0'; Name = '50.7z' }   # 2026-10-21: 40 系暂用 50 载荷
    '50'   = @{ Size = 17106392; Sha256 = '17826E53405C7B7C02FBB3DDF5828EA5B15292BA507956D885D337741F7BB3B0'; Name = '50.7z' }
    'WJ'   = @{ Size = 17484769; Sha256 = 'D71D3D975E8DB3A95F051E86CB6CB380011B16DD375AEB42EE66AF6001725638'; Name = 'WJ.7z' }
    'WJXY' = @{ Size = 17484769; Sha256 = 'D71D3D975E8DB3A95F051E86CB6CB380011B16DD375AEB42EE66AF6001725638'; Name = 'WJ.7z' }
}
# 落地后的期望值：AiStoneService 下的**文件数 + 总字节**（本机 5 变体实测，2026-10-15）
$script:PayloadLandedExpect = @{
    '1020' = @{ Files = 175; Bytes = 42456148 }
    '30'   = @{ Files = 173; Bytes = 46012624 }
    '40'   = @{ Files = 93;  Bytes = 55608072 }   # 2026-10-21: 40 系暂用 50 载荷 ⇒ 落地期望值同 50
    '50'   = @{ Files = 93;  Bytes = 55608072 }
    'WJ'   = @{ Files = 98;  Bytes = 57468032 }
    'WJXY' = @{ Files = 98;  Bytes = 57468032 }
}

function Test-GcuArchiveMatchesFactory {
    # 归档是否与出厂值逐位一致。返回 @{Ok;Detail}。⛔ 任何异常都算"无法确认" ⇒ Ok=$false（宁可不让用户手工装）。
    param([string]$Archive, [string]$Variant)
    if (-not $script:PayloadArchiveFactory.ContainsKey($Variant)) { return @{ Ok = $false; Detail = ("no factory record for variant '{0}'" -f $Variant) } }
    $exp = $script:PayloadArchiveFactory[$Variant]
    try {
        $fi = Get-Item -LiteralPath $Archive -ErrorAction Stop
        $sha = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
    } catch {
        return @{ Ok = $false; Detail = ('archive unreadable: ' + $_.Exception.Message) }
    }
    if ($fi.Length -ne $exp.Size) { return @{ Ok = $false; Detail = ("size {0} != factory {1}" -f $fi.Length, $exp.Size) } }
    if ($sha -ne $exp.Sha256) { return @{ Ok = $false; Detail = ("sha256 {0} != factory {1}" -f $sha, $exp.Sha256) } }
    return @{ Ok = $true; Detail = ('matches factory: {0} bytes, sha256={1}' -f $fi.Length, $sha) }
}

function Resolve-LandedServiceDir {
    # 找出"人工放好的"载荷服务目录。
    # ⭐⭐ 2026-10-15（owner 指出「UniwillService 呢」）：⭐ **必须同时认两个名字** ——
    #   1020 / 30 的归档里那个文件夹叫 **UniwillService**（我们落地时才改名成 AiStoneService），
    #   40 / 50 / WJXY 才叫 AiStoneService。⭐ 用户手工解压拿到的是**归档里的原名** ⇒
    #   若只认 AiStoneService，1020/30 的手工兜底会直接失败 ❌（本轮实测确认归档内确为 UniwillService）。
    # ⭐ 并容忍多套一层变体目录（"解压到当前文件夹"会得到 20\UniwillService 这种）。
    param([string]$TargetDir)
    if ([string]::IsNullOrWhiteSpace($TargetDir) -or -not (Test-Path -LiteralPath $TargetDir)) { return $null }
    $leafNames = @('AiStoneService', 'UniwillService')
    foreach ($leaf in $leafNames) {
        $direct = Join-Path $TargetDir $leaf
        if ((Test-Path -LiteralPath $direct) -and (Test-Path -LiteralPath (Join-Path $direct 'GCUBridge.exe'))) { return $direct }
    }
    foreach ($inner in @(Get-ChildItem -LiteralPath $TargetDir -Directory -ErrorAction SilentlyContinue)) {
        if ($inner.Name -like '_*' -or $inner.Name -eq 'logo' -or $inner.Name -eq 'payload' -or $inner.Name -eq 'logs') { continue }
        foreach ($leaf in $leafNames) {
            $cand = Join-Path $inner.FullName $leaf
            if ((Test-Path -LiteralPath $cand) -and (Test-Path -LiteralPath (Join-Path $cand 'GCUBridge.exe'))) { return $cand }
        }
    }
    return $null
}

function Test-LandedPayloadMatches {
    # 校验人工放好的载荷：文件数 + 总字节 == 该变体已知值，且关键文件验签通过。
    # ⛔ 这是"手工路径"唯一的把门人 —— 数量 / 字节 / 签名三项缺一不可。
    param([string]$ServiceDir, [string]$Variant)
    $result = @{ Ok = $false; Detail = ''; Files = 0; Bytes = 0 }
    if (-not $script:PayloadLandedExpect.ContainsKey($Variant)) {
        $result.Detail = ("no landed-expectation record for variant '{0}'" -f $Variant); return $result
    }
    if ([string]::IsNullOrWhiteSpace($ServiceDir) -or -not (Test-Path -LiteralPath $ServiceDir)) {
        $result.Detail = 'no AiStoneService directory found'; return $result
    }
    $exp = $script:PayloadLandedExpect[$Variant]
    $files = @(Get-ChildItem -LiteralPath $ServiceDir -Recurse -File -ErrorAction SilentlyContinue)
    $bytes = 0
    foreach ($f in $files) { $bytes += $f.Length }
    $result.Files = $files.Count
    $result.Bytes = $bytes
    if ($files.Count -ne $exp.Files) {
        $result.Detail = ("file count {0} != expected {1} (extracted incompletely, or the wrong service folder - the archive's inner 'AiStoneService' / 'UniwillService' must be the one that lands here)" -f $files.Count, $exp.Files)
        return $result
    }
    if ($bytes -ne $exp.Bytes) {
        $result.Detail = ("total bytes {0} != expected {1} (payload incomplete or mixed with another variant)" -f $bytes, $exp.Bytes)
        return $result
    }
    foreach ($rel in @('GCUBridge.exe', 'MyControlCenter\GCUService.exe')) {
        $p = Join-Path $ServiceDir $rel
        if (-not (Test-Path -LiteralPath $p)) { $result.Detail = ('missing key file: ' + $rel); return $result }
        $sig = Get-AuthenticodeSignature -LiteralPath $p
        if ($sig.Status -ne 'Valid') { $result.Detail = ('signature not valid for {0}: {1}' -f $rel, $sig.Status); return $result }
    }
    $result.Ok = $true
    $result.Detail = ('{0} file(s) / {1} B, signatures OK (hand-landed payload accepted)' -f $files.Count, $bytes)
    return $result
}

function Write-ManualPayloadFallback {
    # 放包 + 写脚本与说明 + 弹文件夹。返回目标文件夹路径。⛔ 不抛异常（调用方随后按 Warning 退出）。
    # ⭐ ServiceDirInArchive：⭐ 归档里那个服务文件夹的**原名** —— 1020/30 是 `UniwillService`，
    #   40/50/WJXY 是 `AiStoneService`（2026-10-15 实测）。说明文案必须写对，否则用户会找不到。
    param([string]$Archive, [string]$Variant, [string]$InnerRoot, [string]$ServiceDirInArchive, [string]$TargetDir, [bool]$DryRun)
    $script:ManualPayloadDir = $TargetDir
    try {
        if (-not (Test-Path -LiteralPath $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null }
        $archiveName = $script:PayloadArchiveFactory[$Variant].Name
        $destArchive = Join-Path $TargetDir $archiveName
        Copy-Item -LiteralPath $Archive -Destination $destArchive -Force
        Write-Log ('  LM-MANUAL: archive copied to ' + $destArchive)

        $cmdPath = Join-Path $TargetDir '安装GCU.cmd'
        $cmd = @"
@echo off
rem ===================================================================
rem  L-Mechrevo - install the GCU payload that was extracted by hand.
rem  1) extract $archiveName in THIS folder (a subfolder '$InnerRoot' appears)
rem  2) right-click this file -^> Run as administrator
rem  It verifies the extracted AiStoneService (file count + bytes +
rem  signatures), then registers the UWACPI driver, the GCUBridge service,
rem  the firewall rule and the ACLs. Nothing is downloaded.
rem ===================================================================
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath ''%~f0'' -Verb RunAs"
  exit /b
)
echo === L-Mechrevo: installing GCU from the hand-extracted payload ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Gcu.ps1" -StagingRoot "%~dp0" -TargetDir "%~dp0" -Variant $Variant -PayloadReady -NoStart -KeepVendorConsole -InstallerVersion 18.3 -AppExe "%~dp0..\L-Mechrevo.exe" -LogDir "%~dp0logs"
echo.
echo exit=%ERRORLEVEL%
echo Status/logs: %~dp0logs
pause
"@
        Set-Content -LiteralPath $cmdPath -Value $cmd -Encoding OEM
        Write-Log ('  LM-MANUAL: helper written: ' + $cmdPath)

        $txtPath = Join-Path $TargetDir '手动安装说明.txt'
        $txt = @"
L-Mechrevo：GCU 载荷自动解压失败，请手动完成（约 2 分钟）

【为什么会这样】
  安装程序解压 GCU 载荷时失败（最常见原因是安全软件拦截了解压程序）。
  压缩包本身已确认完好（大小与 SHA256 与出厂值完全一致），所以手动解压是安全的。

【怎么做】
  1) 在本文件夹里，用任意 7-Zip 工具解压 $archiveName
       · 装过 7-Zip：右键 $archiveName → 7-Zip → 解压到当前文件夹
       · 没装 7-Zip：Windows 自带的也行，在本文件夹打开命令行执行：
             tar -xf $archiveName
     解压后会多出一个 $InnerRoot 文件夹，里面是 $ServiceDirInArchive
     （不同型号这里的名字不一样：1020/30 是 UniwillService，40/50/WJXY 是 AiStoneService；
       两种名字脚本都认，不用改名、也不用挪位置）
  2) 在本文件夹右键 `安装GCU.cmd` → 以管理员身份运行
       · 它会校验 $ServiceDirInArchive（文件数、总字节、关键文件签名），
         然后注册 UWACPI 驱动、GCUBridge 服务、防火墙规则与权限。
       · 若提示"文件数/总字节不符"：多半是解压不完整或解压到了别处，请重做第 1 步。
  3) 完成后重新启动 L-Mechrevo，顶部状态应变成「GCU 已连接」。

【排查失败时请提供】
  本文件夹下 logs\gcu-install-*.log

归档：$archiveName
目标目录：$TargetDir
"@
        [System.IO.File]::WriteAllText($txtPath, ($txt -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($true)))
        Write-Log ('  LM-MANUAL: instructions written: ' + $txtPath)

        if (-not $DryRun) {
            try {
                Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $TargetDir + '"')
                Write-Log ('  LM-MANUAL: opened the folder: ' + $TargetDir)
            } catch {
                Write-Log ('  LM-MANUAL: could not open the folder (' + $_.Exception.Message + '); path = ' + $TargetDir)
            }
        } else {
            Write-Log '  LM-MANUAL: -DryRun: folder popup suppressed'
        }
    } catch {
        Write-Log ('  LM-MANUAL: fallback preparation failed: ' + $_.Exception.Message)
    }
    return $TargetDir
}

function Remove-ManualPayloadLeftovers {
    # ⭐⭐ 2026-10-15（owner：「脚本跑完了能不能清理手动安装导致的多余文件」）：
    # 清理"手工安装"留下的多余文件。⛔ 只在**载荷已成功落地之后**才允许调用。
    # 只删这三类**确定属于本次手工流程**的东西：
    #   ⭐ ① 归档 `{TargetDir}\<变体>.7z`（只认 5 个已知文件名）
    #   ⭐ ② 用户解压出来的副本 `{TargetDir}\<顶层>\…`（只认归档顶层名 20/30/40/50/WJXY）
    #   ⭐ ③ `安装GCU.cmd` / `手动安装说明.txt`（-IncludeHelper）
    # ⛔ 绝不动：`{TargetDir}\AiStoneService`（正式载荷）· `logo` · `logs` · `UWACPIDriver`
    # ⭐ 两道安全闸：副本目录里**必须**真的有服务目录 + 正式位置 `AiStoneService\GCUBridge.exe` **必须**已在
    #   —— 任一不满足就**保留不删**并记日志（宁可留垃圾，绝不能让用户没载荷可用）。
    param([string]$TargetDir, [string]$Variant, [bool]$IncludeHelper = $false)
    if ([string]::IsNullOrWhiteSpace($TargetDir) -or -not (Test-Path -LiteralPath $TargetDir)) { return }
    $removed = 0
    $landedExe = Join-Path (Join-Path $TargetDir 'AiStoneService') 'GCUBridge.exe'

    # ① 归档（⭐ 同样加闸：载荷没落到正式位置之前**不许删归档** —— 用户可能还要靠它重解压一遍）
    if ($script:PayloadArchiveFactory.ContainsKey($Variant)) {
        $archivePath = Join-Path $TargetDir $script:PayloadArchiveFactory[$Variant].Name
        if ((Test-Path -LiteralPath $archivePath) -and (-not (Test-Path -LiteralPath $landedExe))) {
            Write-Log ('  LM-MANUAL-CLEAN: kept archive ' + $archivePath + ' (the landed AiStoneService\GCUBridge.exe is not in place yet)')
        }
        elseif (Test-Path -LiteralPath $archivePath) {
            try {
                Remove-Item -LiteralPath $archivePath -Force -ErrorAction Stop
                $removed++
                Write-Log ('  LM-MANUAL-CLEAN: removed archive ' + $archivePath)
            } catch {
                Write-Log ('  LM-MANUAL-CLEAN: could not remove archive ' + $archivePath + ' (' + $_.Exception.Message + ')')
            }
        }
    }

    # ② 解压出来的副本（只认归档的 5 个顶层名；⛔ 不含 AiStoneService）
    foreach ($top in @('20', '30', '40', '50', 'WJXY')) {
        $copyDir = Join-Path $TargetDir $top
        if (-not (Test-Path -LiteralPath $copyDir)) { continue }
        $looksLikePayload = $false
        foreach ($leaf in @('AiStoneService', 'UniwillService')) {
            if (Test-Path -LiteralPath (Join-Path (Join-Path $copyDir $leaf) 'GCUBridge.exe')) { $looksLikePayload = $true }
        }
        if (-not $looksLikePayload) {
            Write-Log ('  LM-MANUAL-CLEAN: kept ' + $copyDir + ' (does not look like an extracted payload)')
            continue
        }
        if (-not (Test-Path -LiteralPath $landedExe)) {
            Write-Log ('  LM-MANUAL-CLEAN: kept ' + $copyDir + ' (the landed AiStoneService\GCUBridge.exe is not in place yet)')
            continue
        }
        try {
            Remove-Item -LiteralPath $copyDir -Recurse -Force -ErrorAction Stop
            $removed++
            Write-Log ('  LM-MANUAL-CLEAN: removed the extracted copy ' + $copyDir)
        } catch {
            Write-Log ('  LM-MANUAL-CLEAN: could not remove ' + $copyDir + ' (' + $_.Exception.Message + ')')
        }
    }

    # ③ 一键脚本与说明（载荷与系统步骤都成了才收）
    if ($IncludeHelper) {
        foreach ($name in @('安装GCU.cmd', '手动安装说明.txt')) {
            $helperPath = Join-Path $TargetDir $name
            if (Test-Path -LiteralPath $helperPath) {
                try {
                    Remove-Item -LiteralPath $helperPath -Force -ErrorAction Stop
                    $removed++
                    Write-Log ('  LM-MANUAL-CLEAN: removed helper ' + $helperPath)
                } catch {
                    Write-Log ('  LM-MANUAL-CLEAN: could not remove ' + $helperPath + ' (' + $_.Exception.Message + ')')
                }
            }
        }
    }
    Write-Log ('  LM-MANUAL-CLEAN: done, {0} item(s) removed' -f $removed)
}

function Expand-GcuPayloadArchive {
    <#
    .SYNOPSIS
        Unpacks the selected 7z into a private temp directory and returns the path of the
        unpacked SERVICE directory (…\<InnerRoot>\<ServiceDir>).

    .DESCRIPTION
        2026-10-14 (7z payload switch): each shipped payload is one self-contained 7z.
        The archive's INTERNAL top folder is not always the variant key (1020.7z holds '20',
        30.7z holds '30'), so the caller passes the expected InnerRoot and we assert on it;
        anything else means the archive and the catalog disagree and must not be installed.

        Tool: 7za.exe in $StagingRoot (an install-time temporary file). Refuses to fall back to
        tar.exe or Expand-Archive: 7z holds .sys/.cat files, and tar.exe cannot read 7z at all
        while Expand-Archive is zip-only.

        2026-10-14 (owner correction): the return value is the SERVICE directory itself, not the
        archive's inner root, because the destination is now {app}\GCU\AiStoneService - the copy
        step must copy the CONTENTS of the service directory, otherwise an existing
        {app}\GCU\AiStoneService would end up with a nested AiStoneService\ inside it. The caller
        also gets the inner root back through the object it already has (Get-GcuPayloadArchiveFor),
        and the 'logo' / 'UWACPIDriver' siblings some archives carry are deliberately NOT copied:
        the shared language files come from the package's shared\ copy and the ACPI driver lands in
        {app}\UWACPIDriver from its own [Files] entry + the shared\UWACPIDriver staging source.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$ArchiveRelPath,
        [Parameter(Mandatory = $true)][string]$InnerRoot,
        [Parameter(Mandatory = $true)][string]$ServiceDir,
        # ⭐ 2026-10-15（手工兜底用）：变体键与 GCU 目标目录。
        # 两者都只在"解包彻底失败 ⇒ 放包 + 引导手工解压"那条路上用到；调用方在 main 里传入。
        [string]$VariantKey = '',
        [string]$TargetDirForFallback = ''
    )

    $archive = Join-Path $StagingRoot $ArchiveRelPath
    if (-not (Test-Path -LiteralPath $archive)) {
        # The setup extracts the archives into {tmp} flat-ish: ExtractTemporaryFile recreates the
        # SOURCE sub-path, so the file really is at <staging>\payload\<name>.7z. Fall back to a bare
        # <staging>\<name>.7z so a hand-made staging dir (archive dropped directly in the root) works
        # too - the manual -StagingRoot path in the .EXAMPLE is exactly that case.
        $bare = Join-Path $StagingRoot (Split-Path -Leaf $ArchiveRelPath)
        if (Test-Path -LiteralPath $bare) {
            Write-Log ("  archive not at {0}; using the flat copy {1}" -f $archive, $bare)
            $archive = $bare
        }
        else {
            throw ("payload archive not found: {0} (also tried {1})" -f $archive, $bare)
        }
    }
    $sevenZip = Join-Path $StagingRoot '7za.exe'
    # ⭐ 2026-10-15：**7za.exe 缺失不再直接致命** —— 解包优先走系统自带的 `tar.exe`（微软签名，见下方），
    # 只有 tar 也不可用/失败时才会真正需要 7za。这里降级为"记一行、待会儿真要用时再判"。
    $sevenZipAvailable = Test-Path -LiteralPath $sevenZip
    if (-not $sevenZipAvailable) {
        Write-Log ("  bundled 7za.exe not present next to the payload ({0}); will rely on the system tar.exe" -f $sevenZip)
    }
    # Publish the two paths that were really used, so the caller's cleanup deletes THESE and not a
    # reconstruction of them (the archive may have been found at the flat fallback location above).
    $script:StagingArchive = $archive
    $script:StagingSevenZip = $sevenZip

    # ⭐ 2026-10-15：解包落点改为**带回退 + 真写探针**（%TEMP% 不可用的机器自动换到 ProgramData / SystemRoot\Temp）。
    $tmp = New-GcuUnpackDir
    Write-Log ("  unpack dir (writable probe passed) = {0}" -f $tmp)
    try {
        Write-Log ("  unpacking {0} -> {1}" -f $archive, $tmp)
        # ⭐ 2026-10-15（真机报「50 系装不上」，日志只留下 "7za.exe failed with exit code 2" 而无从定位）：
        # 先把**待解包归档本身**的大小 + SHA256 写进日志。用途：
        #   * 归档在暂存/落包时被截断或损坏 => 这里的 size/SHA 与出厂值不符（出厂值见下方 NOTE）；
        #   * 归档完好却仍失败 => 那就是**解压目标目录**的问题（%TEMP% 不可写 / 被安全软件拦 / 卷满）。
        # 这两类原因的处理完全不同，原日志无法区分。
        # NOTE（出厂值，2026-10-15 实测）：
        #   1020.7z   9167136 B  3F8835ADC7E4140734A68FB33344818DB57836DA6FC687513CB0949090DB3163
        #   30.7z     8802015 B  199A5124B1B700856BB77BBDD6133713DBBA47307E0B90454387011BE3BD54DE
        #   40.7z    14444731 B  1A8B0232B68A0F14ADA536A28F94FC34C045C46A177C453DD8714A5FDE9D2123
        #   50.7z    17106392 B  17826E53405C7B7C02FBB3DDF5828EA5B15292BA507956D885D337741F7BB3B0
        #   WJXY.7z  17484769 B  D71D3D975E8DB3A95F051E86CB6CB380011B16DD375AEB42EE66AF6001725638
        try {
            $archiveInfo = Get-Item -LiteralPath $archive
            Write-Log ("  archive on disk: {0} bytes, sha256={1}" -f $archiveInfo.Length, (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)
        } catch {
            Write-Log ("  archive stat failed (continuing): " + $_.Exception.Message)
        }
        # ⭐⭐ 2026-10-15（真机 50 系 22:47：**归档 SHA 与出厂一致、%TEMP% 写探针也通过，7za 仍然失败**）：
        # **优先用系统自带的 `tar.exe`（bsdtar / libarchive，微软签名）解包** —— 它原生支持读取 7z，
        # 而且**不需要往 %TEMP% 落地一个未签名的 `7za.exe`**（那正是最可能被安全软件 / ASR 规则拦掉的形态：
        # "阻止未满足流行度/年龄/受信任列表的可执行文件运行"）。
        # 本机逐字节验证（2026-10-15）：5 个载荷（1020/30/40/50/WJXY）用 tar 与 7za 解出的
        # **文件数 / 总字节 / 完整文件名单完全一致**（175/173/71/93/98，名称差异 0）。
        # tar 不存在（Windows < 1803）或解失败时，才回退到随包的 7za.exe。
        $systemTar = Join-Path $env:SystemRoot 'System32\tar.exe'
        # ⭐ 2026-10-15（用户「帮我打包一个纯7za的测试包」）：`-UnpackTool auto|tar|7z` ——
        # 只决定"用哪个解包器"，⛔ 不影响任何判据 / 验签 / 落地逻辑。
        #   auto（默认）= tar 优先、7za 兜底（正式发布的行为）
        #   tar         = **只用** system tar，失败即失败（不兜底，方便对照测试）
        #   7z          = **只用**随包 7za.exe（⛔ 完全不碰 tar；测试用）
        $toolChoice = 'auto'
        if ($UnpackTool) { $toolChoice = ([string]$UnpackTool).Trim().ToLowerInvariant() }
        Write-Log ("  unpack tool choice = {0}" -f $toolChoice)
        $allowTar = ($toolChoice -ne '7z')
        $allow7za = ($toolChoice -ne 'tar')
        $unpackTool = $null
        $unpackExit = $null
        $unpackOutput = @()
        $eapBefore = $ErrorActionPreference
        if (-not $allowTar) {
            Write-Log '  -UnpackTool 7z: skipping the system tar entirely; using the bundled 7za.exe'
        }
        elseif (Test-Path -LiteralPath $systemTar) {
            try {
                $ErrorActionPreference = 'Continue'
                $unpackOutput = @(& $systemTar -xf $archive -C $tmp 2>&1 | ForEach-Object { $_.ToString() })
                $unpackExit = $LASTEXITCODE
            } catch {
                # ⭐ 2026-10-15：`& tar.exe` 也可能抛（不是有效可执行程序/被拦）⇒ 一律折算成"失败"，
                # 交给下面的回退与手工兜底，⛔ 不许让异常直接冒到顶层。
                $unpackOutput = @('exec failed: ' + $_.Exception.Message)
                $unpackExit = -1
            } finally {
                $ErrorActionPreference = $eapBefore
            }
            if ($unpackExit -eq 0) {
                $unpackTool = 'tar.exe (system, signed)'
            } else {
                Write-Log ("  tar.exe failed (exit code {0}); output ({1} line(s); last 15 below):" -f $unpackExit, $unpackOutput.Count)
                @($unpackOutput | Select-Object -Last 15) | ForEach-Object { Write-Log ("    " + $_) }
                if ($allow7za) {
                    Write-Log '  falling back to the bundled 7za.exe'
                    # 清掉 tar 可能留下的半成品，免得与 7za 的结果混在一起
                    Get-ChildItem -LiteralPath $tmp -Force -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
                } else {
                    throw ("tar.exe failed with exit code {0} (-UnpackTool tar: no fallback by design)" -f $unpackExit)
                }
            }
        } else {
            if ($allow7za) {
                Write-Log '  system tar.exe not found (Windows older than 1803); using the bundled 7za.exe'
            } else {
                throw ("-UnpackTool tar but {0} is missing" -f $systemTar)
            }
        }
        if (-not $unpackTool) {
            if (-not $allow7za) { throw 'no unpack tool available' }
            # ⚠️ Argument shape matters: 7za needs '-o<dir>' as ONE argument. Binding it as separate
            # '-o', "$tmp" tokens works with `&`, but quoting it INSIDE the argument string
            # ('-o"$tmp"') makes PowerShell hand 7za the literal quotes on some hosts, which 7za then
            # treats as part of the path => every file fails with "Can not open output file".
            # Using the explicit form below keeps it one argument with no stray quotes.
            $outArg = '-o' + $tmp
            # ⭐ 2026-10-15：**不再 `| Out-Null`** —— 7za 的报错必须进日志（原实现把它整个吞掉，真机只剩
            # 一行 "7za.exe failed with exit code 2"）。⚠️ 也不能在 `$ErrorActionPreference='Stop'`（第 106 行）
            # 下直接 `2>&1`：那会把 7za 往 stderr 写的每一行当成**终止性错误**抛出，走不到下面的退出码判断，
            # 日志里只剩一个**空消息**的 FATAL（真机 22:47 就是这个形态）。
            $unpackOutput = @()
            $unpackExit = $null
            $eapBefore7 = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $unpackOutput = @(& $sevenZip x -y $outArg $archive 2>&1 | ForEach-Object { $_.ToString() })
                $unpackExit = $LASTEXITCODE
            } catch {
                # ⭐⭐ 2026-10-15（真机形态）：7za 被安全软件拦 / 文件不是有效可执行程序时，`& $sevenZip`
                # **抛异常**而不是返回退出码 ⇒ 原来会直接冒到顶层 FATAL，"手工兜底"根本没机会跑 ❌。
                # 这里折算成"失败"（exit=-1），让下面统一走：tar 回退 → 手工兜底 / 抛错。
                $unpackOutput = @('exec failed: ' + $_.Exception.Message)
                $unpackExit = -1
            } finally {
                $ErrorActionPreference = $eapBefore7
            }
            if ($unpackExit -ne 0) {
                Write-Log ("  7za exit code = {0}; output ({1} line(s); last 25 below):" -f $unpackExit, $unpackOutput.Count)
                @($unpackOutput | Select-Object -Last 25) | ForEach-Object { Write-Log ("    " + $_) }
                # ⭐⭐ 2026-10-15（owner 的最后一道兜底）：**归档完好但解包失败** ⇒ 不把整场安装判成失败，
                # 而是把包放到 {app}\GCU\、写好 `安装GCU.cmd` 与 `手动安装说明.txt`、弹出那个文件夹，
                # 然后按 Warning 退出（exit 0）。用户手动解压 + 跑那个脚本即可完成 GCU 安装。
                # ⚠️ 归档本身不完整/不符出厂值 ⇒ 绝不给这条路（继续抛错，让安装失败）✅
                if ($VariantKey -and $TargetDirForFallback) {
                    $factory = Test-GcuArchiveMatchesFactory -Archive $archive -Variant $VariantKey
                    Write-Log ('  LM-MANUAL: factory check -> Ok={0} ({1})' -f $factory.Ok, $factory.Detail)
                    if ($factory.Ok) {
                        Write-Log '  LM-MANUAL: the archive is intact, so this is a TOOL problem, not a payload problem'
                        Write-Log '  LM-MANUAL: switching to the manual route (archive + 安装GCU.cmd + 手动安装说明.txt in the GCU folder)'
                        $null = Write-ManualPayloadFallback -Archive $archive -Variant $VariantKey -InnerRoot $InnerRoot -ServiceDirInArchive $ServiceDir -TargetDir $TargetDirForFallback -DryRun ([bool]$DryRun)
                        $reason = ('GCU payload could not be unpacked automatically (tar and 7za both failed); manual route prepared in ' + $TargetDirForFallback)
                        try {
                            $statusPath = Write-GcuInstallStatus -StatusDir $StatusDir -Status 'Warning' -Reason $reason -Checks @()
                            Write-Log ('WARNING: ' + $reason)
                            Write-Log ('WARNING: the archive, 安装GCU.cmd and 手动安装说明.txt are in ' + $TargetDirForFallback + '; status written to ' + $statusPath)
                        } catch {
                            Write-Log ('WARNING: ' + $reason)
                            Write-Log ('  (could not write the status file: ' + $_.Exception.Message + ')')
                        }
                        exit 0
                    }
                }
                throw ("7za.exe failed with exit code {0}" -f $unpackExit)
            }
            $unpackTool = '7za.exe (bundled)'
        }
        Write-Log ("  unpack tool = {0}" -f $unpackTool)

        # The archive root must contain exactly one top folder, and it must be the one the
        # catalog promised. Both checks are cheap and both have already saved a build.
        $roots = @(Get-ChildItem -LiteralPath $tmp -Directory | Where-Object { $_.Name -notlike '__*' })
        if ($roots.Count -ne 1) {
            throw ("unexpected archive layout under {0}: expected exactly one top folder, found {1} ({2})" -f $tmp, $roots.Count, (($roots | ForEach-Object { $_.Name }) -join ', '))
        }
        if ($roots[0].Name -ne $InnerRoot) {
            throw ("archive inner root mismatch: expected '{0}', found '{1}'" -f $InnerRoot, $roots[0].Name)
        }
        Write-Log ("  archive inner root '{0}' matches the catalog" -f $roots[0].Name)

        $serviceSource = Join-Path $roots[0].FullName $ServiceDir
        if (-not (Test-Path -LiteralPath $serviceSource)) {
            throw ("archive '{0}' has no '{1}' directory under its inner root '{2}'" -f $archive, $ServiceDir, $InnerRoot)
        }

        # ---- anti-regression invariants on the UNPACKED tree (log-only criteria so a field
        #      diagnostics bundle can pinpoint which variant/archive went wrong) ----
        # 1) The two files EVERY generation ships must be inside the service directory we just
        #    located. GCUUtil.exe / OSDTpDetect.exe are deliberately NOT asserted: measured
        #    2026-10-14, GCUUtil.exe only exists in 50.7z / WJXY.7z and 1020/30/40 have none, so
        #    requiring it would fail three currently-correct variants.
        $svcRequired = @('GCUBridge.exe', 'MyControlCenter\GCUService.exe')
        foreach ($rel in $svcRequired) {
            $probe = Join-Path $serviceSource $rel
            if (-not (Test-Path -LiteralPath $probe)) {
                throw ("payload invariant violated: archive '{0}' (variant root '{1}', service dir '{2}') is missing '{3}' - the archive and the variant catalog disagree" -f $archive, $InnerRoot, $ServiceDir, $rel)
            }
        }
        Write-Log ("  payload invariant OK: {0}\{1} carries {2}" -f $InnerRoot, $ServiceDir, ($svcRequired -join ' + '))
        # 2) Legacy-naming sentinel: when the catalog says the archive-internal service dir is
        #    'UniwillService' (only 1020/30), that directory MUST still be there. If someone
        #    "helpfully" rewrites the catalog to AiStoneService for those two, the Join-Path above
        #    would already have thrown - this names the regression explicitly in the log.
        if ($ServiceDir -eq 'UniwillService') {
            Write-Log ("  legacy-naming invariant OK: archive kept its 'UniwillService' folder; it will be laid down as '{0}' (see Get-GcuServiceLeafName)" -f $script:GcuServiceLeafName)
            $unexpected = Join-Path $roots[0].FullName $script:GcuServiceLeafName
            if (Test-Path -LiteralPath $unexpected) {
                Write-Log ("  NOTE: archive also carries a '{0}' directory; the catalog-selected '{1}' is the one that lands" -f $script:GcuServiceLeafName, $ServiceDir)
            }
        }
        return $serviceSource
    }
    catch {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Get-GcuPayloadArchiveFor {
    <#
    .SYNOPSIS
        Maps a variant key to its archive file name (and the normalised target layout).
    #>
    param([Parameter(Mandatory = $true)][string]$Variant)

    $v = $Variant.Trim().ToUpperInvariant() -replace '\s+', ''
    $table = @{
        '1020' = @{ FileName = '1020.7z'; InnerRoot = '20';    ServiceDir = 'UniwillService' }
        '30'   = @{ FileName = '30.7z';   InnerRoot = '30';    ServiceDir = 'UniwillService' }
        '40'   = @{ FileName = '50.7z';   InnerRoot = '50';    ServiceDir = 'AiStoneService' }   # 2026-10-21: 40 系暂用 50 载荷
        '50'   = @{ FileName = '50.7z';   InnerRoot = '50';    ServiceDir = 'AiStoneService' }
        'WJ' = @{ FileName = 'WJ.7z'; InnerRoot = 'WJXY'; ServiceDir = 'AiStoneService' }
        'WJXY' = @{ FileName = 'WJ.7z'; InnerRoot = 'WJXY'; ServiceDir = 'AiStoneService' }
    }
    if (-not $table.ContainsKey($v)) { throw ("unknown GCU variant '$Variant' (known: 1020, 30, 40, 50, WJ, WJXY)") }
    $e = $table[$v]
    return [pscustomobject]@{
        Variant   = $v
        FileName  = $e.FileName
        InnerRoot = $e.InnerRoot
        ServiceDir = $e.ServiceDir
    }
}

function Get-GcuVariantFamily {
    # Family key for the "may the user's mode profiles be carried over?" decision (see the [3/8]
    # copy step in main). The 50 / 40 / 30 payload layouts share file names but not shapes
    # (Mode1..3 x5 vs Mode1..3 x2 + Mode4 x5), so carrying UserPofiles across generations would put
    # the previous variant's same-named files back = mixed generation.
    # 2026-10-09 (40B restored): '40B' maps to the SAME family as '40' - same generation, and neither
    # overlay ships UserPofiles of its own, so both lay down the base tree layout. A 40 <-> 40B
    # switch therefore keeps the user's mode profiles; every other pair still replaces them.
    # The helper is kept instead of comparing the raw strings so the comparison stays
    # case/whitespace-insensitive and there is a single place to extend.
    # Scope: this normalizes ONLY the preserve/overwrite decision. It never rewrites the stored
    # variant - Set-InstallMarker writes Selection.Variant verbatim.
    [CmdletBinding()]
    param([string]$Variant)
    if ([string]::IsNullOrWhiteSpace($Variant)) { return '' }
    $v = $Variant.Trim().ToUpperInvariant()
    if ($v -eq '40B') { return '40' }
    return $v
}
function Get-InstalledMarker {
    $key = 'HKLM:\SOFTWARE\L-Mechrevo'
    $marker = [pscustomobject]@{ GcuVariant = $null; GcuPayloadSha256 = $null; GcuInstallerVersion = $null }
    if (-not (Test-Path -LiteralPath $key)) { return $marker }
    foreach ($name in @('GcuVariant', 'GcuPayloadSha256', 'GcuInstallerVersion')) {
        try {
            $value = (Get-ItemProperty -LiteralPath $key -Name $name -ErrorAction Stop).$name
            if ($null -ne $value) { $marker.$name = [string]$value }
        }
        catch {
            # A missing value is normal on a first install; anything else (denied read) must be
            # visible, because a silently unreadable marker disables the anti-downgrade check.
            Write-Log ("  install marker value '{0}' unreadable: {1}" -f $name, $_.Exception.Message)
        }
    }
    return $marker
}

function Test-InstallerDowngrade {
    # Ordering uses the installer/app version line (shared and monotonic), never the GCU service
    # FileVersion, which the payloads do not share (1.2.0.0 vs 1.0.2.70).
    [CmdletBinding()]
    param(
        [string]$InstalledVersion,
        [string]$IncomingVersion
    )
    if ([string]::IsNullOrWhiteSpace($InstalledVersion)) { return 'NoInstalled' }
    if ([string]::IsNullOrWhiteSpace($IncomingVersion)) { return 'UnknownIncoming' }
    $installed = $null
    $incoming = $null
    if (-not [System.Version]::TryParse($InstalledVersion, [ref]$installed)) { return 'UnknownInstalled' }
    if (-not [System.Version]::TryParse($IncomingVersion, [ref]$incoming)) { return 'UnknownIncoming' }
    if ($installed -gt $incoming) { return 'Downgrade' }
    return 'NotOlder'
}

function Get-InstallAction {
    # Idempotency: an already-current install whose recorded payload identity matches the bundled
    # one needs only verification, never a re-copy or re-registration.
    [CmdletBinding()]
    param(
        [bool]$AlreadyCurrent,
        [string]$InstalledSha256,
        [string]$IncomingSha256
    )
    if (-not $AlreadyCurrent) { return 'Install' }
    if ((Compare-PayloadIdentity -InstalledSha256 $InstalledSha256 -IncomingSha256 $IncomingSha256) -eq 'Same') { return 'VerifyOnly' }
    return 'Install'
}

function Test-GcuPostInstall {
    # Four invariants. ItemSupport and ServiceReady are written by the vendor service, never by us;
    # we only read them. Failure is explicit because Inno [Run] has no ignoreerrors.
    [CmdletBinding()]
    param(
        [string[]]$ServiceNames,
        [int[]]$ListenerPids,
        [bool]$ItemSupportPresent,
        [int]$ServiceReady,
        [string]$ExpectedServiceName = 'GCUBridge'
    )
    $names = @($ServiceNames)
    $pids = @($ListenerPids)
    $observed = if ($names.Count -gt 0) { $names -join ', ' } else { '(none)' }
    $checks = @(
        [pscustomobject]@{
            Name = 'single-service'
            Ok = (($names.Count -eq 1) -and ($names -contains $ExpectedServiceName))
            Detail = ("expected exactly one GCU service ({0}); found {1}: {2}" -f $ExpectedServiceName, $names.Count, $observed)
        },
        [pscustomobject]@{
            Name = 'single-13688-owner'
            Ok = ($pids.Count -eq 1)
            Detail = ("expected exactly one listener on TCP 13688; found {0}" -f $pids.Count)
        },
        [pscustomobject]@{
            Name = 'item-support'
            Ok = [bool]$ItemSupportPresent
            # ⭐ 2026-10-15（真机日志暴露的文案 bug）：原来这里是一句**固定**的"missing"文案，于是当
            # ItemSupportPresent = True 时日志写成
            #   `self-check item-support: OK - ... ItemSupport missing - the vendor service did not write it`
            # —— Ok 与 Detail 自相矛盾（下一行 facts 里明明写着 ItemSupport=True），读日志的人会被误导。
            # 改成按真实值二选一。
            Detail = $(if ($ItemSupportPresent) {
                'HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport present (the vendor service wrote the capability snapshot)'
            } else {
                'HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport missing - the vendor service did not write it'
            })
        },
        [pscustomobject]@{
            Name = 'service-ready'
            Ok = ($ServiceReady -eq 1)
            Detail = ("ServiceReady == {0} (expected 1) - the vendor service did not become ready; see %ProgramData%\L-Mechrevo\logs\gcu-install-*.log" -f $ServiceReady)
        }
    )
    $failed = @($checks | Where-Object { -not $_.Ok })
    return [pscustomobject]@{ Ok = ($failed.Count -eq 0); Checks = $checks; Failed = $failed }
}

# ---------------------------------------------------------------------------
# N7 (owner correction): remove the vendor's official console as well as the
# existing GCU service, leaving only our console. The earlier "prompt the user,
# never delete" rule was the orchestrator's invention and is withdrawn.
#
# Removing a vendor application is destructive, so the scope is pinned by the
# predicate below: vendor-console + GCU artefacts ARE removable; unrelated
# software is NOT. Every step is announced and logged; a locked component is
# reported and skipped, never fatal.
#
# 2026-10-09 (owner, second correction - "make it optional, but the default button is REMOVE"):
# the history above is kept verbatim.
# ⭐⭐ 2026-10-15 (owner, THIRD correction - "并提示是否保留原版控制台"): **默认翻转为保留**，
#    并且卸载判定收敛为**纯 ID**（见下方 $script:VendorConsoleProductCodes）。触发原因：同一天真机
#    事故 —— 旧的名字匹配把「机械革命电子保修卡」当官方控制台卸掉了。
# What decides now: step [2/8] is no longer unconditional. The Inno package's install-time question
# ("是否保留原版控制台？", default button = KEEP) reaches this script as -KeepVendorConsole or
# -RemoveVendorConsole. The default when no switch is passed (manual run) and when nobody can be
# asked (silent install: /SILENT, /VERYSILENT) is now **KEEP**: Remove-VendorConsole /
# Remove-VendorConsoleCore are then never called, so every vendor artefact (packages, directories,
# autostart entries, shortcuts) stays exactly where it is. Only an explicit "否 / 移除" removes them.
# ---------------------------------------------------------------------------

# Concrete, verifiable vendor identifiers. Grounded in the app-side
# OfficialConsoleIsolation markers and the vendor packages documented in
# docs\hardware\README.md (GamingCenter3_Cross.UWP_5.17.*, CCU.WinUI).
$script:VendorPackageMarkers = @(
    'CCU.WinUI', 'GamingCenter3_Cross.UWP', 'GamingCenterU', 'ControlCenterU', 'GCUUI'
)
$script:VendorProcessNames = @(
    'CCUWinUI', 'SystrayComponent', 'ControlCenterU', 'GamingCenterU', 'GCUUI'
)
# ---------------------------------------------------------------------------
# ⭐⭐ 2026-10-15（真机事故 + 用户要求「根据ID判断」）：**卸载判定改为 ID 优先**。
#
# 事故：真机日志出现
#     found official console uninstall entry: 机械革命电子保修卡
#     running uninstall for 机械革命电子保修卡: MsiExec.exe /I{EB0CBF70-14A2-4607-92E5-44BC58334B61} /qn /norestart
# 即「移除官方控制台」把**同厂的电子保修卡**也卸了。原因：旧标记表里有 **厂商级** 关键字
#     'Mechrevo'、'机械革命'、'AISTONE'、'Uniwill'
# 任何名字里带这些词的同厂软件都会被匹配 ⇒ 必然误伤。
#
# 用户原话（判据方向）："根据ID判断 因为其他同方模具的机型命名不一样的 但是id一样"。
# 已实测依据（源文件 D:\搞电脑的东西\机械革命控制台\解包源文件\<版本>\install_script.iss）：
#   官方控制台 **5 个版本**（5.17.49.19 / 5.17.51.34 / 5.56.51.58 / 5.56.60.12 / 5.56.60.34）的
#   Inno AppId **完全相同**：`AppId={{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}`
#   而显示名来自各自的 setup.ini（`AppName={ini:...,APP,AppName|Control Center}` /
#   `UninstallDisplayName={ini:...,APP,ServiceName|Control Center Service}`）⇒ **每个模具都不一样**。
#   ⇒ 按名字判定必然出错；**按 AppId（ARP 键名 `<AppId>_is1`）判定才稳**。
#
# ⛔ 因此厂商级标记全部从卸载判定里删除；名称只作为**窄兜底**（仅控制台本体的名字），
#    并加一张**名称硬保留表**（保修/服务/驱动/电源/军规……），先判保留再判可移除。
# ---------------------------------------------------------------------------
$script:VendorConsoleProductCodes = @(
    # 官方控制台（Control Center / GamingCenter / 电竞控制台 …）：Inno AppId，跨 5 个版本实测一致。
    '{6EA3CE12-B991-4B65-9F8D-B148EAAECD87}'
)
$script:VendorNeverRemoveProductCodes = @(
    # 机械革命电子保修卡 —— 2026-10-15 被误删的那一个，⛔ 永不移除。
    '{EB0CBF70-14A2-4607-92E5-44BC58334B61}',
    # 我们自己（install_script.iss AppId，已固定，不会随构建变化）。
    '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}'
)
# ASCII-only file: the Chinese display names are built from code points, never typed literally.
# ⭐⭐ 2026-10-15（用户：「ID 优先判定直接改成纯ID判定」）：**名字表已整段删除** ——
# 卸载判定只看上面的 ID 表。下面两张表（VendorUninstallMarkers / VendorKeepNameMarkers）在
# "纯 ID" 方案下不再需要，且它们正是 2026-10-15 误删「机械革命电子保修卡」的根源，
# ⇒ 彻底移除，避免以后有人手滑又按名字匹配。
# ⚠️ 若将来遇到"ID 未知的老控制台"：往 $script:VendorConsoleProductCodes 里加 GUID，
#    ⛔ 不要恢复按名字匹配。找不到 ID 的一律 KEEP（宁可漏删，绝不误删）。
# Product folders under Program Files. Never L-Mechrevo\GCU (our payload).
$script:VendorDirectoryAllowList = @(
    'OEM\ControlCenter',
    'OEM\CCUWinUI',
    'OEM\GamingCenter',
    'ControlCenter',
    'CCUWinUI',
    'GamingCenter',
    'GamingCenter3',
    'AISTONE'
)

function Test-VendorArtefactRemovable {
    # The single decision point for "may this be removed?". Pure and testable: no I/O.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('package', 'directory', 'uninstall', 'shortcut', 'autostart', 'process')]
        [string]$Kind,
        # AllowEmptyString/AllowNull: an Uninstall subkey without a DisplayName (or a provisioned
        # package without a DisplayName) is normal on a real machine. A Mandatory [string] rejects
        # '' at BINDING time, i.e. before the IsNullOrWhiteSpace guard below can run, and the
        # exception used to abort the whole vendor-console removal on the first such key.
        # Field log (2026-10-09, owner machine):
        #   vendor-console removal failed (non-fatal): cannot bind to parameter "Value" ... empty string
        #   vendor-console removal done: 0 removed, 0 skipped
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value,
        # ⭐ 2026-10-15（用户「根据ID判断」）：卸载判定的**稳定标识** —— ARP 子键名，形如 "<AppId>_is1"。
        # 只有 -Kind 'uninstall' 会用到它；其它 kind 传空即可。默认值必须有，否则 StrictMode 下读它会报错。
        [AllowEmptyString()][AllowNull()][string]$KeyName = ''
    )
    if ([string]::IsNullOrWhiteSpace($Value)) { return 'KEEP' }

    switch ($Kind) {
        'package' {
            foreach ($marker in $script:VendorPackageMarkers) {
                if ($Value -like ('*' + $marker + '*')) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        'process' {
            foreach ($name in $script:VendorProcessNames) {
                if ($Value -ieq $name) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        'uninstall' {
            # ⭐⭐ 2026-10-15（用户：「ID 优先判定直接改成纯ID判定」）：**只看 ID，⛔ 完全不认名字**。
            # 为什么必须这样：官方控制台的显示名来自各自 setup.ini 的 AppName / ServiceName，
            # **每个模具、每个版本都不一样**（用户原话："其他同方模具的机型命名不一样的 但是id一样"）；
            # 而 AppId 恒定（5 个版本实测一致）。任何按名字的判定都必然误伤 —— 2026-10-15 真机就是
            # 因为旧表里有 'Mechrevo' / '机械革命'，把「机械革命电子保修卡」当控制台卸掉了。
            # 规则：ARP 子键名形如 "<AppId>_is1" ⇒ 取出花括号 GUID ⇒
            #   · 命中 VendorNeverRemoveProductCodes ⇒ KEEP
            #   · 命中 VendorConsoleProductCodes    ⇒ REMOVABLE
            #   · 其余一切（含没有 ID 的条目）      ⇒ KEEP
            # ⇒ **宁可漏删，绝不误删**；要覆盖更多控制台版本，就往 ID 表里加 GUID，⛔ 不要退回按名字。
            $key = ''
            if ($null -ne $KeyName) { $key = ([string]$KeyName).Trim() }
            if (-not $key) { return 'KEEP' }
            $bare = $key
            if ($bare -match '^(\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\})') { $bare = $Matches[1] }
            foreach ($code in $script:VendorNeverRemoveProductCodes) {
                if ($bare -eq $code) { return 'KEEP' }
            }
            foreach ($code in $script:VendorConsoleProductCodes) {
                if ($bare -eq $code) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        'shortcut' {
            # A shortcut is removable only when its own file name carries a vendor marker.
            $leaf = Split-Path -Leaf $Value
            foreach ($marker in @('GamingCenter', 'ControlCenter', 'CCUWinUI', 'SystrayComponent', 'GCUUI')) {
                if ($leaf -like ('*' + $marker + '*')) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        'autostart' {
            foreach ($marker in @('CCUWinUI.exe', 'SystrayComponent.exe', 'ControlCenterU.exe', 'GamingCenterU.exe', 'GCUUI.exe')) {
                if ($Value -like ('*' + $marker + '*')) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        'directory' {
            $normalized = $Value.TrimEnd('\')
            if ($normalized -like '*\L-Mechrevo\GCU*') { return 'KEEP' }
            foreach ($allowed in $script:VendorDirectoryAllowList) {
                if ($normalized -like ('*' + $allowed)) { return 'REMOVABLE' }
            }
            return 'KEEP'
        }
        default { return 'KEEP' }
    }
}

function Get-NotePropertyString {
    # StrictMode forbids $obj.DisplayName when the registry value is missing.
    param($Object, [string]$Name)
    if ($null -eq $Object) { return '' }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return '' }
    return [string]$prop.Value
}

function Invoke-VendorUninstallCommand {
    param([string]$DisplayName, [string]$Quiet, [string]$Normal)
    $line = $Quiet
    if ([string]::IsNullOrWhiteSpace($line)) { $line = $Normal }
    if ([string]::IsNullOrWhiteSpace($line)) { return $false }
    if ($line -match '(?i)msiexec' -and $line -notmatch '(?i)/qn') {
        $line = $line.Trim() + ' /qn /norestart'
    }
    Write-Log ('  running uninstall for {0}: {1}' -f $DisplayName, $line)
    try {
        $process = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $line) -PassThru -WindowStyle Hidden
        if ($null -eq $process) { return $false }
        if (-not $process.WaitForExit(180000)) {
            try { $process.Kill() } catch { }
            Write-Log ('  uninstall timed out for {0}' -f $DisplayName)
            return $false
        }
        Write-Log ('  uninstall exit code {0} for {1}' -f $process.ExitCode, $DisplayName)
        return ($process.ExitCode -eq 0)
    }
    catch {
        Write-Log ('  uninstall command failed for {0}: {1}' -f $DisplayName, $_.Exception.Message)
        return $false
    }
}

function Remove-VendorConsoleCore {
    # Idempotent, logged, non-fatal. Reports what was found, removed and skipped.
    # The result lists are owned by the caller (Remove-VendorConsole) so that a mid-way abort can
    # still report what had already been removed instead of a misleading "0 removed, 0 skipped".
    param(
        [System.Collections.Generic.List[string]]$Removed,
        [System.Collections.Generic.List[string]]$Skipped
    )
    $removed = $Removed
    $skipped = $Skipped
    if ($null -eq $removed) { $removed = New-Object System.Collections.Generic.List[string] }
    if ($null -eq $skipped) { $skipped = New-Object System.Collections.Generic.List[string] }

    foreach ($name in $script:VendorProcessNames) {
        foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            Write-Log ('  stopping official console process: {0} (pid {1})' -f $name, $process.Id)
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            # Counted as an artefact handled, otherwise a run that only stopped processes also
            # reports "0 removed" and looks like a no-op.
            $removed.Add(('stopped process {0} (pid {1})' -f $name, $process.Id))
        }
    }

    # 1. Add/Remove Programs uninstall (Win32 / MSI). This is the real "uninstall official console".
    $uninstallRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    $uninstallFound = 0
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($item in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
            $props = $null
            try { $props = Get-ItemProperty -LiteralPath $item.PSPath -ErrorAction Stop } catch { continue }
            $display = Get-NotePropertyString -Object $props -Name 'DisplayName'
            # ⭐ 2026-10-15：把 ARP 子键名（= AppId/ProductCode）一起交给判定 —— ID 优先，名字只兜底。
            if ((Test-VendorArtefactRemovable -Kind 'uninstall' -Value $display -KeyName $item.PSChildName) -ne 'REMOVABLE') { continue }
            $uninstallFound++
            Write-Log ('  found official console uninstall entry: {0}' -f $display)
            if (Invoke-VendorUninstallCommand -DisplayName $display -Quiet (Get-NotePropertyString -Object $props -Name 'QuietUninstallString') -Normal (Get-NotePropertyString -Object $props -Name 'UninstallString')) {
                $removed.Add($display)
                Write-Log ('  removed official console via uninstall: {0}' -f $display)
            }
            else {
                $skipped.Add($display)
                Write-Log ('  official console uninstall could not be removed ({0}); continuing' -f $display)
            }
        }
    }
    if ($uninstallFound -eq 0) {
        Write-Log '  no official console uninstall entry found'
    }

    # 2. UWP/MSIX: current user, all users, and provisioned image.
    $packages = @()
    try {
        $packages = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object {
            (Test-VendorArtefactRemovable -Kind 'package' -Value $_.Name) -eq 'REMOVABLE'
        })
    }
    catch {
        Write-Log ('  Get-AppxPackage -AllUsers failed, falling back to current user: ' + $_.Exception.Message)
        try {
            $packages = @(Get-AppxPackage -ErrorAction Stop | Where-Object {
                (Test-VendorArtefactRemovable -Kind 'package' -Value $_.Name) -eq 'REMOVABLE'
            })
        }
        catch { Write-Log ('  could not enumerate Appx packages: ' + $_.Exception.Message) }
    }

    if ($packages.Count -eq 0) {
        Write-Log '  no official console package found'
    }
    foreach ($package in $packages) {
        Write-Log ('  found official console package: {0}' -f $package.PackageFullName)
        try {
            Remove-AppxPackage -Package $package.PackageFullName -AllUsers -ErrorAction Stop
            $removed.Add($package.PackageFullName)
            Write-Log ('  removed official console package: {0}' -f $package.PackageFullName)
        }
        catch {
            try {
                Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
                $removed.Add($package.PackageFullName)
                Write-Log ('  removed official console package (current user): {0}' -f $package.PackageFullName)
            }
            catch {
                $skipped.Add($package.PackageFullName)
                Write-Log ('  official console package could not be removed ({0}); continuing: {1}' -f $package.PackageFullName, $_.Exception.Message)
            }
        }
    }

    try {
        foreach ($prov in @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)) {
            if ((Test-VendorArtefactRemovable -Kind 'package' -Value $prov.DisplayName) -ne 'REMOVABLE') { continue }
            Write-Log ('  found official console provisioned package: {0}' -f $prov.PackageName)
            try {
                Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName -ErrorAction Stop | Out-Null
                $removed.Add($prov.PackageName)
                Write-Log ('  removed official console provisioned package: {0}' -f $prov.PackageName)
            }
            catch {
                $skipped.Add($prov.PackageName)
                Write-Log ('  official console provisioned package could not be removed ({0}); continuing: {1}' -f $prov.PackageName, $_.Exception.Message)
            }
        }
    }
    catch { Write-Log ('  could not enumerate provisioned Appx packages: ' + $_.Exception.Message) }

    # 3. Desktop-exe folders on the allow-list.
    $programRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
    foreach ($root in $programRoots) {
        foreach ($allowed in $script:VendorDirectoryAllowList) {
            $candidate = Join-Path $root $allowed
            if (-not (Test-Path -LiteralPath $candidate)) { continue }
            if ((Test-VendorArtefactRemovable -Kind 'directory' -Value $candidate) -ne 'REMOVABLE') {
                Write-Log ('  refusing to remove non-allow-listed directory: {0}' -f $candidate)
                continue
            }
            Write-Log ('  found official console directory: {0}' -f $candidate)
            try {
                Remove-Item -LiteralPath $candidate -Recurse -Force -ErrorAction Stop
                $removed.Add($candidate)
                Write-Log ('  removed official console directory: {0}' -f $candidate)
            }
            catch {
                $skipped.Add($candidate)
                Write-Log ('  official console directory could not be removed ({0}); continuing: {1}' -f $candidate, $_.Exception.Message)
            }
        }
    }

    # 4. Autostart entries pointing at a vendor console executable.
    foreach ($hive in @('HKCU:', 'HKLM:')) {
        foreach ($sub in @('SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce')) {
            $key = Join-Path $hive $sub
            if (-not (Test-Path -LiteralPath $key)) { continue }
            foreach ($property in @((Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' })) {
                if ((Test-VendorArtefactRemovable -Kind 'autostart' -Value ([string]$property.Value)) -ne 'REMOVABLE') { continue }
                Write-Log ('  found official console autostart entry: {0} = {1}' -f $property.Name, $property.Value)
                try {
                    Remove-ItemProperty -LiteralPath $key -Name $property.Name -ErrorAction Stop
                    $removed.Add(('{0}\{1}' -f $key, $property.Name))
                    Write-Log ('  removed official console autostart entry: {0}' -f $property.Name)
                }
                catch {
                    $skipped.Add(('{0}\{1}' -f $key, $property.Name))
                    Write-Log ('  autostart entry could not be removed ({0}); continuing: {1}' -f $property.Name, $_.Exception.Message)
                }
            }
        }
    }

    # 5. Start Menu / desktop shortcuts.
    $shortcutRoots = @(
        (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:PUBLIC 'Desktop'),
        (Join-Path $env:USERPROFILE 'Desktop')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    foreach ($root in $shortcutRoots) {
        foreach ($lnk in @(Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue)) {
            if ((Test-VendorArtefactRemovable -Kind 'shortcut' -Value $lnk.FullName) -ne 'REMOVABLE') { continue }
            Write-Log ('  found official console shortcut: {0}' -f $lnk.FullName)
            try {
                Remove-Item -LiteralPath $lnk.FullName -Force -ErrorAction Stop
                $removed.Add($lnk.FullName)
                Write-Log ('  removed official console shortcut: {0}' -f $lnk.FullName)
            }
            catch {
                $skipped.Add($lnk.FullName)
                Write-Log ('  official console shortcut could not be removed ({0}); continuing: {1}' -f $lnk.FullName, $_.Exception.Message)
            }
        }
    }

    return [pscustomobject]@{ Removed = $removed; Skipped = $skipped }
    }

function Remove-VendorConsole {
    $removed = New-Object System.Collections.Generic.List[string]
    $skipped = New-Object System.Collections.Generic.List[string]
    try {
        $result = Remove-VendorConsoleCore -Removed $removed -Skipped $skipped
        return [pscustomobject]@{
            Removed = $result.Removed
            Skipped = $result.Skipped
            Failed = $false
            FailureReason = ''
        }
    }
    catch {
        Write-Log ('  vendor-console removal failed (non-fatal): ' + $_.Exception.Message)
        Write-Log ('  vendor-console removal ABORTED early: {0} removed, {1} skipped before the failure - ' -f $removed.Count, $skipped.Count +
                   'this is not the same as "no candidate matched"; everything not listed above is untouched')
        return [pscustomobject]@{
            Removed = $removed
            Skipped = $skipped
            Failed = $true
            FailureReason = $_.Exception.Message
        }
    }
}

function Get-GcuFallbackGuidance {
    # N6: the visible fallback that replaces the retired G0 gate. HONEST LIMITATION: this project
    # has no download server, so there is no one-click cloud download. The guidance tells the user
    # exactly which payload to fetch and where it lives, and how to install it by hand.
    $lines = @(
        'The newest GCU payload (release\GCU-only) did not come up on this machine.'
        'If this is a 40-series machine, fetch the matching 40-series payload and install it by hand:'
        '  1. release\GCU-40-51751  (AiStoneService variant; the 40-series default)'
        '  2. release\GCU-40-51749  (UniwillService variant; the older 40-series console)'
        'Both live in the L-Mechrevo source tree under release\ and are NOT shipped in this package.'
        'There is no download server for this project, so the payload cannot be fetched automatically.'
        'To install one by hand: copy its <ServiceDir>\* over {app}\GCU\AiStoneService\, then run'
        '  powershell -NoProfile -ExecutionPolicy Bypass -File "{app}\GCU\Install-Gcu.ps1" -StagingRoot "<dir holding payload\*.7z and 7za.exe>" -TargetDir "{app}\GCU"'
        'and re-run the self-check. Report the failure with %ProgramData%\L-Mechrevo\logs\gcu-install-*.log.'
    )
    return [pscustomobject]@{
        Reason   = 'the newest GCU payload did not become ready on this machine'
        Payloads = @('release\GCU-40-51751', 'release\GCU-40-51749')
        Lines    = $lines
    }
}

function Get-GcuPostInstallFacts {
    $services = @()
    try {
        $services = @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop |
            Where-Object { $_.PathName -and ($_.PathName -match '(?i)\\GCU\\') } |
            Select-Object -ExpandProperty Name)
    }
    catch { $services = @() }
    $pids = @()
    if (Get-Command -Name Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        $pids = @(Get-NetTCPConnection -LocalPort 13688 -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)
    }
    $itemSupportPresent = $false
    $serviceReady = -1
    try {
        $item = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\OEM\GamingCenter2\ItemSupport' -ErrorAction Stop
        $itemSupportPresent = (@($item.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' }).Count -gt 0)
        $ready = $item.PSObject.Properties['ServiceReady']
        if ($null -ne $ready) { $serviceReady = [int]$ready.Value }
    }
    catch {
        # Post-install verification reads this; a denied read must not look like "absent".
        Write-Log ('  ItemSupport unreadable during post-install verification: ' + $_.Exception.Message)
    }
    return [pscustomobject]@{
        ServiceNames = $services
        ListenerPids = $pids
        ItemSupportPresent = $itemSupportPresent
        ServiceReady = $serviceReady
    }
}

function Wait-GcuPostInstallFacts {
    # GCUBridge writes ItemSupport / ServiceReady and binds 13688 after Start-Service returns.
    # A single immediate probe is a field false-fail: setup then exits 1 while the service is
    # still coming up, and the app sits on "GCU connecting" forever.
# 2026-10-13 (owner): cap the "wait until the service reports ready" window at 5 s TOTAL
# (was 30 s). Retry semantics kept - poll every 1 s until the deadline. A timeout stays a
# WARNING only: the install always continues and is never marked failed by this wait.
    param([int]$TimeoutSeconds = 5)
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    $facts = Get-GcuPostInstallFacts
    $verdict = Test-GcuPostInstall -ServiceNames $facts.ServiceNames -ListenerPids $facts.ListenerPids `
        -ItemSupportPresent $facts.ItemSupportPresent -ServiceReady $facts.ServiceReady
    while (-not $verdict.Ok) {
        if ([datetime]::UtcNow -ge $deadline) { break }
        $reason = (@($verdict.Failed) | ForEach-Object { $_.Name }) -join ','
        Write-Log ("  post-install not ready yet ({0}); retrying" -f $reason)
        Start-Sleep -Seconds 1
        $facts = Get-GcuPostInstallFacts
        $verdict = Test-GcuPostInstall -ServiceNames $facts.ServiceNames -ListenerPids $facts.ListenerPids `
            -ItemSupportPresent $facts.ItemSupportPresent -ServiceReady $facts.ServiceReady
    }
    return [pscustomobject]@{ Facts = $facts; Verdict = $verdict }
}

function Write-GcuInstallStatus {
    [CmdletBinding()]
    param([string]$StatusDir, [string]$Status, [string]$Reason, [object]$Checks)
    if ([string]::IsNullOrWhiteSpace($StatusDir)) { return $null }
    if (-not (Test-Path -LiteralPath $StatusDir)) { New-Item -ItemType Directory -Path $StatusDir -Force | Out-Null }
    $path = Join-Path $StatusDir 'gcu-install-status.json'
    $payload = [ordered]@{
        Status     = $Status
        Reason     = $Reason
        UpdatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        Checks     = @($Checks)
    }
    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Install-UwacpiDriver {
    param([string]$DriverDir)
    $inf = Join-Path $DriverDir 'UWACPIDriver.inf'
    if (-not (Test-Path -LiteralPath $inf)) {
        throw ("UWACPIDriver.inf not found: {0}" -f $inf)
    }
    Write-Log ("  pnputil /add-driver `"{0}`" /install" -f $inf)
    $output = & pnputil.exe /add-driver $inf /install 2>&1
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Log ("    " + [string]$_) }
    if ($code -ne 0) {
        # Idempotency: an already-present driver can make pnputil return non-zero.
        $enum = & pnputil.exe /enum-drivers 2>&1
        $present = ($enum | Select-String -SimpleMatch 'uwacpidriver.inf' -Quiet)
        if ($present) {
            Write-Log ("  driver already present in the driver store (pnputil exit {0}); continuing" -f $code)
        }
        else {
            throw ("pnputil /add-driver failed with exit code {0}" -f $code)
        }
    }
    else {
        Write-Log '  driver installed/updated OK'
    }
}

function Install-Service {
    param([string]$ServiceExe)
    if (-not (Test-Path -LiteralPath $ServiceExe)) {
        throw ("GCUBridge.exe not found: {0}" -f $ServiceExe)
    }
    $current = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $script:ServiceName) -ErrorAction SilentlyContinue
    if ($null -ne $current) {
        Write-Log ("  existing service found (state={0}); stopping and recreating to pin binPath" -f $current.State)
        Stop-Service -Name $script:ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $script:ServiceName | Out-Null
        $waited = 0
        while ((Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $script:ServiceName) -ErrorAction SilentlyContinue) -and $waited -lt 20) {
            Start-Sleep -Milliseconds 500
            $waited++
        }
    }
    Write-Log ("  New-Service {0} -> `"{1}`"" -f $script:ServiceName, $ServiceExe)
    New-Service -Name $script:ServiceName -BinaryPathName ('"' + $ServiceExe + '"') `
        -DisplayName $script:ServiceName -StartupType Automatic `
        -Description 'L-Mechrevo GCU MQTT broker and hardware service host.' | Out-Null
}

function Ensure-FirewallRule {
    $cmd = Get-Command -Name New-NetFirewallRule -ErrorAction SilentlyContinue
    if ($cmd) {
        $existing = Get-NetFirewallRule -DisplayName $script:RuleName -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Log '  firewall block rule already present'
        }
        else {
            New-NetFirewallRule -DisplayName $script:RuleName -Direction Inbound -Action Block `
                -Protocol TCP -LocalPort 13688 -Profile Any -Enabled True | Out-Null
            Write-Log '  firewall inbound block rule created (TCP 13688)'
        }
        return
    }
    Write-Log '  WARNING: New-NetFirewallRule unavailable; trying netsh fallback'
    & netsh.exe advfirewall firewall delete rule ("name=" + $script:RuleName) | Out-Null
    & netsh.exe advfirewall firewall add rule ("name=" + $script:RuleName) dir=in action=block protocol=TCP localport=13688 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'netsh firewall rule creation failed' }
}

function Start-GcuService {
    Write-Log ("  Start-Service {0}" -f $script:ServiceName)
    Start-Service -Name $script:ServiceName
    $waited = 0
    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
        $waited++
    } while ($service -and $service.Status -ne 'Running' -and $waited -lt 40)
    if (-not $service -or $service.Status -ne 'Running') {
        throw ("{0} did not reach Running (status={1})" -f $script:ServiceName, $(if ($service) { $service.Status } else { 'missing' }))
    }
    Write-Log '  service is RUNNING'
}

# ---------------------------------------------------------------------------
# N5: privileges are acquired ONCE, here, while the installer is elevated.
# The app is permanently elevated by owner decision; the installer is the only
# place elevation is obtained, so the autostart task must exist before the app
# ever runs. An unelevated app cannot create a highest-privileges task, which is
# the same permission root cause as the original "reboot does not autostart" bug.
# ---------------------------------------------------------------------------

function Get-AutostartTaskName {
    # Per-user task name, matching the app's own naming so the app-side repair path
    # recognises the installer-created task instead of creating a second one.
    $sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    return ('LMechrevo_' + $sid)
}

function Register-AutostartTask {
    param([Parameter(Mandatory = $true)][string]$AppExe)
    if (-not (Test-Path -LiteralPath $AppExe)) {
        Write-Log ("  skipping autostart task: app exe not found ({0})" -f $AppExe)
        return $null
    }
    $taskName = Get-AutostartTaskName
    $userName = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).Name

    # N15 #14 regression fix: the action MUST carry the `startup` argument. The app's own contract
    # compares the action's arguments to "startup" (Helpers\Startup.cs:329) and registers it that
    # way (:360); a task without it never matches the app's plan, so the app rewrites it and the
    # launch path is wrong - which is exactly the "autostart does nothing" field report. The
    # argument is a fixed literal, not user input, so it is not an injectable surface.
    $action = New-ScheduledTaskAction -Execute $AppExe -Argument 'startup'
    $logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $userName
    $logonTrigger.Delay = 'PT10S'
    $consoleTrigger = New-ScheduledTaskTrigger -AtLogOn -User $userName
    $consoleTrigger.Delay = 'PT10S'
    $principal = New-ScheduledTaskPrincipal -UserId $userName -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

    # -Force overwrites the same-named task in place: no delete window, so a denied re-register
    # can never leave the machine without an autostart entry.
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($logonTrigger, $consoleTrigger) `
        -Principal $principal -Settings $settings -Force | Out-Null
    Write-Log ("  autostart task registered: {0} (RunLevel=Highest, action='{1}' Argument=startup)" -f $taskName, $AppExe)
    return $taskName
}

function Grant-AppDirectoryAcl {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
    $userName = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).Name
    # Modify = read/write/delete for this user only. Deliberately not a broad principal and not the
    # widest right: the app needs to write its own config/logs, nothing more.
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $userName, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
    Write-Log ("  ACL granted (Modify): {0} -> {1}" -f $userName, $Path)
}

function Grant-AcpiDriverAccess {
    # \\.\ACPIDriver is opened by the app for the read-only EC snapshot and the charge-limit write.
    # Running elevated already covers it; this only makes the grant explicit and idempotent so a
    # future non-elevated consumer is not silently denied. No EC/firmware write is added here.
    $driver = Join-Path $env:SystemRoot 'System32\drivers\UWACPIDriver.sys'
    if (-not (Test-Path -LiteralPath $driver)) {
        Write-Log '  UWACPIDriver.sys not present; skipping device ACL (driver install step owns it)'
        return
    }
    $userName = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).Name
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($userName, 'ReadAndExecute', 'Allow')
    $acl = Get-Acl -LiteralPath $driver
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $driver -AclObject $acl
    Write-Log ("  ACL granted (ReadAndExecute): {0} -> {1}" -f $userName, $driver)
}

function Set-InstallMarker {
    param([pscustomobject]$Selection, [string]$ServiceExe, [string]$PayloadSha256, [string]$InstallerVersion)
    try {
        $key = 'HKLM:\SOFTWARE\L-Mechrevo'
        if (-not (Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
        $markerVariant = $Selection.Variant
        if ($Selection.PSObject.Properties.Name -contains 'PayloadVariant' -and -not [string]::IsNullOrWhiteSpace([string]$Selection.PayloadVariant)) {
            $markerVariant = [string]$Selection.PayloadVariant
        }
        Set-ItemProperty -LiteralPath $key -Name 'GcuVariant' -Value $markerVariant
        Write-Log ("  GcuVariant marker = {0} (selection={1}, payload={2})" -f $markerVariant, $Selection.Variant, $markerVariant)
        # 2026-10-14: the service dir now comes from the unpacked archive's own layout
        # (UniwillService for 1020/30, AiStoneService for 40/50/WJXY).
        $serviceDirName = $null
        if ($Selection.PSObject.Properties.Name -contains 'ServiceDir') { $serviceDirName = $Selection.ServiceDir }
        if ([string]::IsNullOrWhiteSpace($serviceDirName)) { $serviceDirName = 'AiStoneService' }
        Set-ItemProperty -LiteralPath $key -Name 'GcuServiceDir' -Value $serviceDirName
        Set-ItemProperty -LiteralPath $key -Name 'GcuServiceExe' -Value $ServiceExe
        if (-not [string]::IsNullOrWhiteSpace($PayloadSha256)) {
            Set-ItemProperty -LiteralPath $key -Name 'GcuPayloadSha256' -Value $PayloadSha256
        }
        if (-not [string]::IsNullOrWhiteSpace($InstallerVersion)) {
            Set-ItemProperty -LiteralPath $key -Name 'GcuInstallerVersion' -Value $InstallerVersion
        }
        Set-ItemProperty -LiteralPath $key -Name 'GcuInstalledUtc' -Value ((Get-Date).ToUniversalTime().ToString('o'))
        Write-Log '  install marker written to HKLM\SOFTWARE\L-Mechrevo (GcuPayloadSha256 = manifest hash of the laid payload set: relative path + size + SHA256 of every file)'
    }
    catch {
        Write-Log ('  WARNING: could not write install marker: ' + $_.Exception.Message)
    }
}

function Restart-AppAfterGcuInstall {
    # 2026-10-09 (warmupreload fallback, installer side - the LAST resort for the warm-up window).
    # The app decides part of its device capability state once, while it is being constructed; an
    # instance that was already running before this GCU install therefore keeps its old "unsupported
    # / read-only" evaluation, and the app-side in-place soft reload cannot cover that construction-
    # time state (see source\MechrevoLite\GcuWarmupReload.cs). Reason, verbatim: GCU was just
    # installed, so restart L-Mechrevo once to load the full capability set.
    # Contract (deliberately narrow):
    #   * Only the full-install tail calls this. The VerifyOnly fast path (nothing changed) and every
    #     earlier install step are untouched, so this never fires for a repair/verify run.
    #   * At most ONE restart attempt per install run; no loop, no retry storm.
    #   * If the app is NOT running, do nothing at all - the installer never launches it on its own.
    #   * Non-fatal: any failure is logged and the install still finishes successfully.
    param([Parameter(Mandatory = $true)][string]$AppExe)
    try {
        $running = @(Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) {
            Write-Log '  app restart: L-Mechrevo is not running; nothing to do (not launching it)'
            return
        }
        if ([string]::IsNullOrWhiteSpace($AppExe) -or -not (Test-Path -LiteralPath $AppExe)) {
            Write-Log ('  app restart: app exe not found ({0}); skipping' -f $AppExe)
            return
        }
        $pids = (@($running) | ForEach-Object { $_.Id }) -join ','
        Write-Log ('  app restart: GCU was just installed and L-Mechrevo is running (pid {0}) -> restarting it ONCE so it loads the full capability set (its construction-time capability snapshot predates this install)' -f $pids)
        foreach ($proc in $running) {
            try {
                $proc.Kill()
                $proc.WaitForExit(10000) | Out-Null
            }
            catch {
                Write-Log ('  app restart: stop failed (continuing): ' + $_.Exception.Message)
            }
        }
        Start-Sleep -Seconds 3
        $left = @(Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue)
        if ($left.Count -gt 0) {
            Write-Log ('  app restart: WARNING {0} L-Mechrevo process(es) still alive after the stop request; not starting a second instance' -f $left.Count)
            return
        }
        Start-Process -FilePath $AppExe -WorkingDirectory (Split-Path -Parent $AppExe) -ErrorAction Stop
        Start-Sleep -Seconds 3
        $back = @(Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue)
        if ($back.Count -gt 0) {
            Write-Log ('  app restart: L-Mechrevo relaunched (pid {0})' -f ((@($back) | ForEach-Object { $_.Id }) -join ','))
        }
        else {
            Write-Log '  app restart: WARNING L-Mechrevo did not come back up; the autostart task will start it at next logon'
        }
    }
    catch {
        Write-Log ('  app restart: WARNING restart after GCU install failed (non-fatal): ' + $_.Exception.Message)
    }
}

# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
if ($MyInvocation.InvocationName -eq '.') {
    # Dot-sourced (tests / the build): expose the functions only, run nothing.
    return
}

try {
    if ([string]::IsNullOrWhiteSpace($LogDir)) {
        $LogDir = Join-Path $env:ProgramData 'L-Mechrevo\logs'
    }
    if (-not (Test-Path -LiteralPath $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
    $script:LogFile = Join-Path $LogDir ('gcu-install-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
    if ([string]::IsNullOrWhiteSpace($StatusDir)) {
        # The installer's [Run] entry does not pass -StatusDir; the self-check status file belongs
        # next to the install logs (both live under <ProgramData>\L-Mechrevo).
        $StatusDir = Join-Path $env:ProgramData 'L-Mechrevo'
    }

    Write-Log '=== L-Mechrevo GCU payload install ==='
    Write-Log ("StagingRoot = {0}" -f $StagingRoot)
    Write-Log ("TargetDir   = {0}" -f $TargetDir)
    Write-Log ("Variant     = {0}" -f $Variant)
    Write-Log ("SinglePayload = {0}" -f [bool]$SinglePayload)
    Write-Log ("DryRun      = {0}" -f [bool]$DryRun)

# ⭐⭐ 2026-10-15（用户「同样日志要收集完全」）：**环境头**。动机：排查真机 22:47 那份日志时，
# 我们花了很久才排除「归档损坏 / 临时目录不可写」，而 OS 版本、是否提权、TEMP 在哪、所在盘剩多少空间、
# 两个解包工具**到底能不能执行**——这些"环境类失败"最直接的证据，一条都没记。
# 现在每份安装日志自带这一段；尤其 `7za can run` / `tar can run` 两行，能立刻区分
# 「工具被安全软件拦住（起不来）」与「工具能跑但解包失败」。
Write-Log '--- environment ---'
try {
    $osKey = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
    $osBuild = ''
    try { $osBuild = ' (build {0}.{1})' -f $osKey.CurrentBuildNumber, $osKey.UBR } catch { }
    Write-Log ("OS          = {0} {1}{2}" -f $osKey.ProductName, $osKey.DisplayVersion, $osBuild)
} catch {
    Write-Log ("OS          = {0} (registry read failed: {1})" -f [Environment]::OSVersion.VersionString, $_.Exception.Message)
}
Write-Log ("PowerShell  = {0}" -f $PSVersionTable.PSVersion.ToString())
try {
    $isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Log ("Elevated    = {0}" -f $isElevated)
} catch {
    Write-Log ("Elevated    = (unknown: {0})" -f $_.Exception.Message)
}
Write-Log ("TEMP        = {0}" -f $env:TEMP)
$seenDrives = @()
foreach ($qualified in @($TargetDir, $env:TEMP)) {
    try {
        $drive = (Split-Path -Qualifier $qualified).TrimEnd(':')
        if ($seenDrives -contains $drive) { continue }
        $seenDrives += $drive
        $psd = Get-PSDrive -Name $drive -ErrorAction Stop
        Write-Log ("Drive {0}:     free = {1:N1} GB (of {2:N1} GB)" -f $drive, ($psd.Free / 1GB), (($psd.Free + $psd.Used) / 1GB))
    } catch {
        Write-Log ("Drive for {0}: free space unknown ({1})" -f $qualified, $_.Exception.Message)
    }
}
# 解包工具可用性（**能不能执行**本身就是关键证据）
$tarProbe = Join-Path $env:SystemRoot 'System32\tar.exe'
if (Test-Path -LiteralPath $tarProbe) {
    $tarEap = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $tarVer = @(& $tarProbe --version 2>&1 | Select-Object -First 1 | ForEach-Object { $_.ToString() })
        $tarExit = $LASTEXITCODE
    } catch {
        # ⭐ 探针绝不许终止脚本（同下面 7za 的道理）——只报告"跑不起来"。
        $tarVer = @('probe failed: ' + $_.Exception.Message)
        $tarExit = -1
    } finally {
        $ErrorActionPreference = $tarEap
    }
    Write-Log ("tar.exe     = {0} (can run exit {1}: {2})" -f $tarProbe, $tarExit, (($tarVer -join ' ').Trim()))
} else {
    Write-Log ("tar.exe     = NOT present at {0} (Windows older than 1803?)" -f $tarProbe)
}
$zaProbe = Join-Path $StagingRoot '7za.exe'
if (Test-Path -LiteralPath $zaProbe) {
    try {
        $zaInfo = Get-Item -LiteralPath $zaProbe
        Write-Log ("7za.exe     = {0} size={1} sha256={2}" -f $zaProbe, $zaInfo.Length, (Get-FileHash -LiteralPath $zaProbe -Algorithm SHA256).Hash)
    } catch {
        Write-Log ("7za.exe     = {0} (stat failed: {1})" -f $zaProbe, $_.Exception.Message)
    }
    $zaEap = $ErrorActionPreference
    $zaExit = -1
    $zaVer = @()
    try {
        $ErrorActionPreference = 'Continue'
        $zaVer = @(& $zaProbe 2>&1 | Select-Object -First 4 | ForEach-Object { $_.ToString() })
        $zaExit = $LASTEXITCODE
    } catch {
        # ⭐⭐ 2026-10-15（真机实测踩到）：`& 7za.exe` 在"文件不是有效的可执行程序 / 被安全软件拦"时
        # **抛异常**，而这里原来只有 try/finally ⇒ ⭐ **整个安装脚本会在解包之前就 FATAL 退出** ❌
        # —— 偏偏这就是我们要修的那种机器形态。⇒ 探针必须吞掉异常、只记一行，
        # 让后面的"tar 优先 / 手工兜底"有机会执行 ✅
        $zaVer = @('probe failed: ' + $_.Exception.Message)
        $zaExit = -1
    } finally {
        $ErrorActionPreference = $zaEap
    }
    $zaBanner = (@($zaVer) | Where-Object { $_ -match '7-Zip' } | Select-Object -First 1)
    Write-Log ("7za can run = {0} (exit {1}: {2})" -f ($zaExit -eq 0), $zaExit, $(if ($zaBanner) { $zaBanner.Trim() } else { (@($zaVer) -join ' | ').Trim() }))
} else {
    Write-Log ("7za.exe     = NOT present next to the payload ({0})" -f $zaProbe)
}

    # ---------------------------------------------------------------------
    # Install-time "official console" decision - SINGLE SOURCE OF TRUTH for the default.
    # 2026-10-09 (owner, first wording): optional, "make it optional, but the default button is REMOVE".
    # ⭐⭐ 2026-10-15 (owner, superseded - "并提示是否保留原版控制台"): **默认改为保留**。
    #   理由：同一天真机事故证明"默认移除"太危险 —— 移除判定曾按名字匹配，把同厂的
    #   「机械革命电子保修卡」也卸了。现在卸载判定已收敛为**纯 ID**（只认官方控制台的 AppId），
    #   且默认不动任何厂商物件：用户显式选"否/移除"才会执行 [2/8]。
    # The default is the one word 'Keep' below; flipping it back to 'Remove' changes the behaviour of
    # (a) a manual run without switches and (b) a silent install (which is never asked), while the
    # installer's answer (which passes one of the two switches) keeps winning over it.
    # -KeepVendorConsole turns step [2/8] into a no-op: no ARP uninstall, no Remove-AppxPackage,
    # no directory / autostart / shortcut deletion.
    # ---------------------------------------------------------------------
    $script:VendorConsoleAction = 'Keep'
    if ($KeepVendorConsole -and $RemoveVendorConsole) {
        Write-Log 'WARNING: -KeepVendorConsole and -RemoveVendorConsole were both passed; -RemoveVendorConsole wins'
        $script:VendorConsoleAction = 'Remove'
    }
    elseif ($RemoveVendorConsole) {
        $script:VendorConsoleAction = 'Remove'
    }
    elseif ($KeepVendorConsole) {
        $script:VendorConsoleAction = 'Keep'
    }
    Write-Log ("VendorConsole = {0} (RemoveVendorConsole={1}, KeepVendorConsole={2})" -f $script:VendorConsoleAction, [bool]$RemoveVendorConsole, [bool]$KeepVendorConsole)

    $selectorScript = Join-Path $PSScriptRoot 'Select-GcuPayload.ps1'
    $selectorExe = Join-Path $PSHOME 'powershell.exe'
    # ---------------------------------------------------------------------
    # 2026-10-16 (r30, owner: "hardware id takes priority over the displayed name
    # because the displayed name can be modified"): collect the GPU signals HERE,
    # on the caller side, and hand both to the selector via -GpuName / -GpuDeviceId.
    #
    # Why the caller must collect (verified 2026-10-16 on a 40-series machine):
    # the selector's own registry collector enumerates the display class key set
    # and that set contains a protected subkey (Properties) on which
    # Get-ChildItem throws SecurityException "requested registry access is not
    # allowed". With -ErrorAction Stop that first failure aborted the whole
    # enumeration, catch{} swallowed it, the judge saw no signal at all and fell
    # to UNKNOWN -> safety floor 50 (on a 40-series machine that unpacked 50.7z).
    # The caller-side collector below only enumerates the numeric subkeys and
    # reads MatchingDeviceId / HardwareID per key, so the protected subkey cannot
    # abort the walk, and the signals are passed to the child process explicitly,
    # so the judge no longer depends on the child's own registry walk at all.
    #
    # Route A (preferred): hardware id - MatchingDeviceId / HardwareID from the
    #   display class registry, full 'PCI\VEN_10DE&DEV_xxxx' strings.
    # Route B (fallback): display name from Win32_VideoController, with virtual
    #   display adapters filtered out (GameViewer Virtual Display Adapter /
    #   USB Mobile Monitor Virtual Display style devices must not feed the judge).
    # Either route may come up empty; the selector then applies its own safety
    # floor, and the log line below records exactly what was collected.
    # ---------------------------------------------------------------------
    $script:CollectedGpuNames = @()
    $script:CollectedGpuIds = @()
    $script:GpuNameSource = 'none'
    try {
        # .NET registry API (r30): Get-ChildItem on the class key throws on the protected
        # Properties subkey even with a numeric-name filter, so enumerate subkey NAMES only.
        $dispClass = 'SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Default').OpenSubKey($dispClass)
        if ($root) {
            foreach ($sub in $root.GetSubKeyNames()) {
                if ($sub -notmatch '^[0-9]{4}$') { continue }
                $k = $root.OpenSubKey($sub)
                if (-not $k) { continue }
                $desc = [string]$k.GetValue('DriverDesc')
                if (-not $desc -or $desc -notmatch 'NVIDIA') { continue }
                # NOTE (r30): @() BEFORE .Count - under StrictMode 2.0 a single-string
                # HardwareID has no Count property and the old pattern aborted the walk.
                $hwids = @(@($k.GetValue('HardwareID')) | Where-Object { $_ -and ("$_").ToUpperInvariant() -match 'PCI\\VEN_10DE&DEV_[0-9A-Fa-f]{4}' })
                if (@($hwids).Count -gt 0) { $script:CollectedGpuIds += @($hwids) }
                elseif (("$($k.GetValue('MatchingDeviceId'))").ToUpperInvariant() -match 'PCI\\VEN_10DE&DEV_[0-9A-Fa-f]{4}') {
                    $script:CollectedGpuIds += [string]$k.GetValue('MatchingDeviceId')
                }
                if ($desc) { $script:CollectedGpuNames += $desc }
            }
        }
        if ($script:CollectedGpuIds.Count -gt 0 -or $script:CollectedGpuNames.Count -gt 0) { $script:GpuNameSource = 'registry' }
    } catch {
        Write-Log ('  GPU signals: registry route failed (non-fatal): ' + $_.Exception.Message)
    }
    try {
        if ($script:CollectedGpuNames.Count -eq 0) {
            $vc = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop
            foreach ($v in $vc) {
                $n = [string]$v.Name
                if (-not $n) { continue }
                if ($n -match 'Virtual') { continue }   # virtual display adapters are not GPUs
                if ($n -match 'NVIDIA') { $script:CollectedGpuNames += $n }
            }
            if ($script:CollectedGpuNames.Count -gt 0 -and $script:GpuNameSource -eq 'none') { $script:GpuNameSource = 'cim' }
        }
    } catch {
        Write-Log ('  GPU signals: CIM route failed (non-fatal): ' + $_.Exception.Message)
    }
    $script:CollectedGpuNames = @($script:CollectedGpuNames | Select-Object -Unique)
    $script:CollectedGpuIds = @($script:CollectedGpuIds | Select-Object -Unique)
    Write-Log ("GPU signals collected: names=[{0}] ids=[{1}] (source={2})" -f ($script:CollectedGpuNames -join '; '), ($script:CollectedGpuIds -join '; '), $script:GpuNameSource)

    $selectorArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $selectorScript, '-Variant', $Variant, '-AsJson')
    if ($script:CollectedGpuNames.Count -gt 0) { $selectorArgs += @('-GpuName', $script:CollectedGpuNames) }
    if ($script:CollectedGpuIds.Count -gt 0) { $selectorArgs += @('-GpuDeviceId', $script:CollectedGpuIds) }
    if ($SinglePayload) { $selectorArgs += '-SinglePayload' }
    $selectionJson = & $selectorExe @selectorArgs
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($selectionJson | Out-String))) {
        throw ("GCU payload selection failed (exit {0})" -f $LASTEXITCODE)
    }
    $selection = ($selectionJson | Out-String) | ConvertFrom-Json
    # 2026-10-14 (7z payload switch): the selector returns Variant / Archive / ArchivePath /
    # DisplayName / Note. The judge fields (Generation / Reason / Evidence) no longer exist; the
    # single 'Note' line already carries the machine string, the keyword hit, the chosen variant
    # and the archive, so it is what the log must keep.
    Write-Log ("Selection   = {0} ({1})" -f $selection.Variant, $selection.DisplayName)
    Write-Log ("Archive     = {0}" -f $selection.ArchivePath)
    Write-Log ("Reason      = {0}" -f $selection.Note)
    # ---- judgement log: one line the field bundle can collect ----
    # Format (single line, ASCII): GcuVariantSelection | variant=<V> | archive=<file> | note=<...>
    Write-Log ("GcuVariantSelection | variant={0} | archive={1} | note={2}" -f $selection.Variant, $selection.Archive, $selection.Note)

    # Resolve the archive -> unpack into a temp staging dir -> (only after signature verification,
    # see [4/8] below) copy into {app}\GCU\AiStoneService. There is deliberately NO
    # payload\<Variant>\<ServiceDir> normalisation level any more.
    $archiveInfo = Get-GcuPayloadArchiveFor -Variant ([string]$selection.Variant)
    if ([string]::IsNullOrWhiteSpace($Variant)) { $Variant = 'Auto' }
    $variantKey = $archiveInfo.Variant

    # The setup extracts the archives into {tmp} and ExtractTemporaryFile recreates the SOURCE
    # sub-path, so they land at <staging>\payload\<name>.7z - NOT flat in <staging>. Build that path
    # here; the older code passed only the bare file name and re-joined it onto the staging root,
    # which only worked while the archives were installed under {app}\GCU\payload.
    $archiveRelPath = Join-Path 'payload' $archiveInfo.FileName
    if ($PayloadReady) {
        # ⭐⭐ 2026-10-15（owner 的手工兜底路线）：载荷已由人工解压放好 ⇒ **跳过解包**，
        # 直接在 $TargetDir 里找 AiStoneService（容忍多一层变体目录），校验 数量/字节/签名 后继续走
        # 原有的 [3/8]/[4/8] 落地与验签流程（它会把手放的载荷规整到 $TargetDir\AiStoneService）。
        Write-Log '  -PayloadReady: skipping the automatic unpack; looking for a hand-landed payload'
        $landed = Resolve-LandedServiceDir -TargetDir $TargetDir
        if (-not $landed) {
            throw ("-PayloadReady: neither AiStoneService nor UniwillService found under {0} - extract the payload there first" -f $TargetDir)
        }
        Write-Log ('  -PayloadReady: candidate = ' + $landed)
        $landedVerdict = Test-LandedPayloadMatches -ServiceDir $landed -Variant ([string]$archiveInfo.Variant)
        Write-Log ('  -PayloadReady: verification -> Ok={0}: {1}' -f $landedVerdict.Ok, $landedVerdict.Detail)
        if (-not $landedVerdict.Ok) {
            throw ("-PayloadReady: the hand-landed payload does not match variant {0}: {1}" -f $archiveInfo.Variant, $landedVerdict.Detail)
        }
        $unpackRoot = $landed
        # ⚠️ 作用域实测：主流程在块级作用域里跑，`if` 分支里赋的局部变量**不保证**传得到后面几行
        # （2026-10-15：分支里明明赋了值，2148 行的 Join-Path 仍收到 $null ❌）⇒ 用**脚本级**变量做载体。
        # ⛔ 并且**绝不**把人工目录登记进 `$script:UnpackRoot` —— 那是"失败清理要删的临时目录"，
        # 登记了就会把用户手工解压出来的东西删掉（实测踩过 ❌）。
        $script:LandedPayloadDir = $landed
        $script:ManualPayloadLanding = $true
        $script:UnpackRoot = $null
        Write-Log '  -PayloadReady: accepted; the normal landing + signature flow below will move it into place'
    } else {
        $unpackRoot = Expand-GcuPayloadArchive -StagingRoot $StagingRoot -ArchiveRelPath $archiveRelPath `
            -InnerRoot $archiveInfo.InnerRoot -ServiceDir $archiveInfo.ServiceDir `
            -VariantKey $variantKey -TargetDirForFallback $TargetDir
        $script:UnpackRoot = $unpackRoot
    }
    # ⭐ 兜底取回（块级作用域不保证可见）· 人工路径从 LandedPayloadDir 取，⛔ 不碰失败清理用的 UnpackRoot
    if (-not $unpackRoot) {
        if ($script:ManualPayloadLanding) { $unpackRoot = $script:LandedPayloadDir } else { $unpackRoot = $script:UnpackRoot }
    }
    if ($unpackRoot -and -not $script:ManualPayloadLanding) { $script:UnpackRoot = $unpackRoot }
    Assert-RequiredFile -Path (Join-Path $unpackRoot 'MyControlCenter\GCUService.exe') -Label 'unpacked GCUService.exe'
    Assert-RequiredFile -Path (Join-Path $unpackRoot 'GCUBridge.exe') -Label 'unpacked GCUBridge.exe'
    Write-Log ("  unpacked payload staging: {0} (archive service dir '{1}' -> laid down as '{2}')" -f $unpackRoot, $archiveInfo.ServiceDir, $script:GcuServiceLeafName)
    # The archive and 7za.exe are install-time temporary files: both are gone from {tmp} from here
    # on, so nothing of them can be copied into the app directory by a later step. Their exact paths
    # were published by Expand-GcuPayloadArchive (it may have resolved the archive to the flat
    # fallback location), so the cleanup below deletes the files that were really used.
    Write-Log ("  install-time temporary staging: archive={0} tool={1}" -f $script:StagingArchive, $script:StagingSevenZip)

    $selectionServiceDir = $script:GcuServiceLeafName
    # Contents of the unpacked service directory: copying '<source>\*' (Copy-Tree does exactly
    # that) into {app}\GCU\AiStoneService leaves no nested AiStoneService\ behind.
    $baseServiceSource = $unpackRoot
    $trioServiceSource = $baseServiceSource
    $serviceTarget = Join-Path $TargetDir $script:GcuServiceLeafName
    # 2026-10-13: UWACPIDriver 三件改为随包直接落 {app}\UWACPIDriver（shared\ 目录）；这个
    # staging 源现在指向 shared\UWACPIDriver（若存在则再复制一次，不存在则用已落盘的那份）。
    # ⚠️ 2026-10-14: shared\ is no longer laid down as a directory - the setup spreads its contents
    # out to their destinations ({app}\UWACPIDriver, {app}\GCU\logo\Language,
    # {app}\GCU\AiStoneService) via plain [Files] entries, so this staging source is only the
    # manual/by-hand path; the installed copy is what normally feeds Install-UwacpiDriver. The
    # logo source below is now {app}\GCU\logo (owner: "put logo in the GCU folder") and there is NO
    # copy step for it any more: the [Files] entry already put the two .json files there.
    $driverSource = Join-Path (Join-Path $StagingRoot 'shared') 'UWACPIDriver'
    # 2026-10-13 (owner: "UWACPIDriver 放根目录吧"): the bundled ACPI driver now lands in the
    # APPLICATION ROOT ({app}\UWACPIDriver) instead of {app}\GCU\UWACPIDriver. Root-first, GCU-legacy
    # fallback: derive {app} from -AppExe (the installer always passes it); if that is unusable, keep
    # the old <TargetDir>\UWACPIDriver so a manual/staged run still works. Nothing else about the
    # driver chain changes: same staging source, same Assert-SignedFile, same
    # `pnputil /add-driver <inf> /install` via Install-UwacpiDriver -DriverDir.
    $driverTarget = $null
    if (-not [string]::IsNullOrWhiteSpace($AppExe)) {
        $appRoot = Split-Path -Parent $AppExe
        if (-not [string]::IsNullOrWhiteSpace($appRoot)) { $driverTarget = Join-Path $appRoot 'UWACPIDriver' }
    }
    if ([string]::IsNullOrWhiteSpace($driverTarget)) {
        # Same convention the autostart step uses further down (:AppExe default): TargetDir is
        # {app}\GCU, so its parent is {app}.
        $appRoot = Split-Path -Parent $TargetDir
        if (-not [string]::IsNullOrWhiteSpace($appRoot)) { $driverTarget = Join-Path $appRoot 'UWACPIDriver' }
    }
    if ([string]::IsNullOrWhiteSpace($driverTarget)) { $driverTarget = Join-Path $TargetDir 'UWACPIDriver' }
    $legacyDriverTarget = Join-Path $TargetDir 'UWACPIDriver'
    if (($legacyDriverTarget -ne $driverTarget) -and `
        (-not (Test-Path -LiteralPath (Join-Path $driverTarget 'UWACPIDriver.inf'))) -and `
        (Test-Path -LiteralPath (Join-Path $legacyDriverTarget 'UWACPIDriver.inf'))) {
        Write-Log ("  UWACPIDriver: no .inf under {0}; falling back to legacy location {1}" -f $driverTarget, $legacyDriverTarget)
        $driverTarget = $legacyDriverTarget
    }
    Write-Log ("  UWACPIDriver directory = {0}" -f $driverTarget)
    $serviceExe = Join-Path $serviceTarget 'GCUBridge.exe'
    $gcuServiceExe = Join-Path (Join-Path $serviceTarget 'MyControlCenter') 'GCUService.exe'
    $driverSys = Join-Path $driverTarget 'UWACPIDriver.sys'

    if (-not (Test-Path -LiteralPath (Join-Path $trioServiceSource 'MyControlCenter\GCUService.exe'))) {
        throw ("selected payload is incomplete; missing GCUService.exe under {0}" -f $trioServiceSource)
    }
    if (-not (Test-Path -LiteralPath (Join-Path $baseServiceSource 'GCUBridge.exe'))) {
        throw ("selected payload is incomplete; missing GCUBridge.exe under {0}" -f $baseServiceSource)
    }
    # Identity is the manifest hash of the payload set this run lays down. Since 2026-10-14 there
    # is exactly ONE source tree per variant (the unpacked 7z), so the old base+overlay merge
    # collapses to a single manifest. The functions are kept because the registry value
    # GcuPayloadSha256 and the landing check both still read this manifest.
    $baseIdentity = Get-GcuPayloadIdentity -ServiceDir $baseServiceSource
    $overlayIdentity = $null
    $payloadManifest = Merge-GcuPayloadManifest -Manifests @($baseIdentity)
    Write-Log ("payload identity (manifest SHA256) = {0}" -f $payloadManifest.Sha256)
    Write-Log ("payload manifest = {0} file(s) / {1} B (single complete tree; one line per file: relative path|size|SHA256)" -f $payloadManifest.FileCount, $payloadManifest.TotalBytes)
    Write-Log ("payload layout = unpacked 7z '{0}' -> {1} (archive service dir '{2}' laid down as '{3}'; no payload\<variant>\ level)" -f $selection.Archive, $serviceTarget, $archiveInfo.ServiceDir, $selectionServiceDir)
    Write-Log ("payload staging = {0} (install-time temporary: the archive and 7za.exe are deleted from here once the payload is in place)" -f $StagingRoot)
    Write-Log 'NOTE: a machine installed before 2026-10-14 has the old base+overlay identity in HKLM\SOFTWARE\L-Mechrevo\GcuPayloadSha256, so its first install of this build compares Different and re-lays the whole payload once; that is the intended repair, not a downgrade.'
    Write-Log 'NOTE: machines installed by the 2026-10-14 package whose layout was {app}\GCU\payload\<variant>\<ServiceDir> have no {app}\GCU\AiStoneService yet, so the landing check below re-lays the whole payload once - the intended repair for that withdrawn layout.'

    # Anti-downgrade (installer/app version line, shared and monotonic): refuse before any change.
    $marker = Get-InstalledMarker
    $downgrade = Test-InstallerDowngrade -InstalledVersion $marker.GcuInstallerVersion -IncomingVersion $InstallerVersion
    if ($downgrade -eq 'Downgrade') {
        throw ("refusing to downgrade: installed L-Mechrevo {0} is newer than this installer {1}; no service/driver/firewall changes made - use a newer installer to upgrade." -f $marker.GcuInstallerVersion, $InstallerVersion)
    }
    Write-Log ("downgrade check = {0} (installed='{1}' incoming='{2}')" -f $downgrade, $marker.GcuInstallerVersion, $InstallerVersion)

    # Fast path: already installed, healthy and the same payload manifest -> verify only.
    $currentBinPath = Get-ServiceBinPath
    $alreadyCurrent = (Test-ExpectedBinPath -PathName $currentBinPath -ExpectedExe $serviceExe) -and `
        (Test-Path -LiteralPath $serviceExe) -and (Test-Path -LiteralPath $gcuServiceExe) -and (Test-Path -LiteralPath $driverSys)
    # Installed-side probe: hash the target files this manifest expects, so both sides describe the
    # same file set. An identical value means every expected file is in place byte for byte; an
    # added/removed/changed payload file anywhere in the manifest now shows up here.
    $currentIdentitySha = $null
    if ($alreadyCurrent) {
        $currentIdentitySha = (Get-GcuPayloadIdentity -ServiceDir $serviceTarget -ExpectEntries $payloadManifest.Entries).Sha256
    }
    $action = Get-InstallAction -AlreadyCurrent $alreadyCurrent -InstalledSha256 $currentIdentitySha -IncomingSha256 $payloadManifest.Sha256
    Write-Log ("install action = {0} (installed manifest identity = {1}, incoming = {2})" -f $action, $(if ($currentIdentitySha) { $currentIdentitySha } else { '(none)' }), $payloadManifest.Sha256)

    if ($action -eq 'VerifyOnly' -and -not $DryRun) {
        $service = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
        if ($service -and $service.Status -eq 'Running') {
            Write-Log 'already installed with the same payload manifest and RUNNING; verifying signatures, firewall and payload landing only'
            Assert-SignedFile -Path $serviceExe -Label 'GCUBridge'
            Assert-SignedFile -Path $gcuServiceExe -Label 'GCUService'
            Assert-SignedFile -Path $driverSys -Label 'UWACPIDriver'
            Ensure-FirewallRule
            Set-InstallMarker -Selection $selection -ServiceExe $serviceExe -PayloadSha256 $payloadManifest.Sha256 -InstallerVersion $InstallerVersion
            # Same self-check as the full path. No copy happened here, so a landing mismatch would
            # mean the installed tree was changed behind our back (still a real failure: the tree no
            # longer matches the shipped payload), while "not ready yet" stays a warning.
            $verifyLanding = Test-GcuPayloadLanded -Manifest $payloadManifest -TargetDir $serviceTarget
            $verifyFacts = Get-GcuPostInstallFacts
            $verifyVerdict = Test-GcuPostInstall -ServiceNames $verifyFacts.ServiceNames -ListenerPids $verifyFacts.ListenerPids `
                -ItemSupportPresent $verifyFacts.ItemSupportPresent -ServiceReady $verifyFacts.ServiceReady
            $verifyStatus = 'Ok'
            $verifyReason = 'payload manifest unchanged; every expected file verified in place (verify-only path)'
            if (-not $verifyLanding.Ok) {
                $verifyStatus = 'Failed'
                $verifyReason = 'payload landing check failed: ' + $verifyLanding.Check.Detail
            }
            elseif (-not $verifyVerdict.Ok) {
                $verifyStatus = 'Warning'
                $verifyReason = 'GCU service/backend not ready yet: ' + ((@($verifyVerdict.Failed) | ForEach-Object { $_.Name }) -join ',')
            }
            $verifyStatusPath = Write-GcuInstallStatus -StatusDir $StatusDir -Status $verifyStatus -Reason $verifyReason `
                -Checks (@($verifyLanding.Check) + @($verifyVerdict.Checks))
            Write-Log ("  post-install self-check: {0} -> {1} ({2})" -f $verifyStatus, $verifyStatusPath, $verifyReason)
            if (-not $verifyLanding.Ok) {
                Write-Log ('ERROR: ' + $verifyReason)
                exit 1
            }
            Write-Log 'GCU install OK (no changes needed)'
            exit 0
        }
    }

    # The installer's PrepareToInstall hook already stopped GCU processes and uninstalled
    # the prior-generation service BEFORE files were overwritten, so no uninstall-first
    # step is needed here. Only the port safety gate remains, so two generations can
    # never coexist even on a manual/staged run.
    Write-Log '[1/8] prior GCU already removed by setup (pre-overwrite); checking TCP 13688'
    if (-not $DryRun) { Assert-Port13688Free }

    # N7 (owner correction 2026-10-09, second pass): [2/8] is no longer unconditional. It runs ONLY
    # when this run was explicitly told to remove the vendor console:
    #   -RemoveVendorConsole -> Remove-VendorConsole: vendor ARP uninstallers (⭐ 纯 ID 匹配),
    #          Remove-AppxPackage / provisioned packages, the allow-listed ControlCenter /
    #          GamingCenter* / AISTONE directories, autostart entries and shortcuts.
    #   -KeepVendorConsole, **or no switch at all** (⭐ 2026-10-15 默认已翻转为保留）
    #          -> NOTHING here is called at all: every vendor artefact must stay exactly where it is.
    # The Inno package's install-time question ("是否保留原版控制台？", default button = KEEP) hands the
    # answer over as a parameter; a silent install is never asked and therefore lands on the default = KEEP.
    if ($script:VendorConsoleAction -ne 'Remove') {
        Write-Log ("[2/8] keeping the vendor official console (decision '{0}': the installer asked and the user chose to keep it, or nothing/-KeepVendorConsole was passed = default) - no vendor artefact touched" -f $script:VendorConsoleAction)
    }
    elseif ($DryRun) {
        Write-Log '[2/8] removing the vendor official console (packages, directories, autostart)'
        Write-Log '  DRY RUN: vendor-console removal skipped'
    }
    else {
        Write-Log '[2/8] removing the vendor official console (packages, directories, autostart)'
        $vendorResult = Remove-VendorConsole
        Write-Log ('  vendor-console removal done: {0} removed, {1} skipped' -f $vendorResult.Removed.Count, $vendorResult.Skipped.Count)
        if ($vendorResult.Skipped.Count -gt 0) {
            Write-Log '  the following vendor artefacts could not be removed and need a manual step or a reboot:'
            foreach ($item in $vendorResult.Skipped) { Write-Log ('    - ' + $item) }
        }
    }

    Write-Log ('[3/8] staging the unpacked {0} payload for the landing at {1}' -f $selection.Variant, $serviceTarget)
    # 2026-10-14 (owner correction + the "only the fan tables landed" field result): the copy into
    # the install tree happens NO EARLIER than after the signature check in [4/8] below. Reason: the
    # unpacked tree and the destination are now the same directory name (AiStoneService), so a
    # partial/corrupt archive copied straight in would leave the install directory in exactly the
    # broken state that was observed - a service directory holding whatever the vendor files
    # happened to be, with no runnable service in it. Verify first, land second.
    #
    # Preserve user data across the overwrite: mode profiles (UserPofiles) and fan curves
    # (UserFanTables) live under <serviceTarget>\MyControlCenter. Back them up before the
    # tree copy and restore them after, so an upgrade keeps the user's settings.
    # 2026-10-06 (fix B): UserPofiles are only carried over when the GCU variant is unchanged.
    # The 30-series payload's UserPofiles and the base (40/50) tree's UserPofiles share file
    # names but not layouts (Mode1..3 x5 vs Mode1..3 x2 + Mode4 x5), so carrying them across
    # variants puts the previous variant's same-named files back = mixed generation.
    # 2026-10-09: the comparison is on the variant FAMILY (see Get-GcuVariantFamily). The helper
    # still maps the retired '40B' to '40' so that a machine whose registry says GcuVariant=40B
    # (written by a pre-2026-10-14 build) keeps its mode profiles when it moves to the single 40
    # payload. No new install ever writes 40B again.
    # Only this decision is normalized: Set-InstallMarker keeps writing the real variant, so
    # HKLM\SOFTWARE\L-Mechrevo\GcuVariant is the variant that actually got installed.
    # $marker was read above (before this script changed anything). UserFanTables stays
    # untouched: fan tables live in the EC and are not moved around here.
    $installedVariant = [string]$marker.GcuVariant
    $installedFamily = Get-GcuVariantFamily -Variant $installedVariant
    $incomingFamily = Get-GcuVariantFamily -Variant ([string]$selection.Variant)
    $sameVariant = (-not [string]::IsNullOrWhiteSpace($installedFamily)) -and ($installedFamily -ieq $incomingFamily)
    $preserveDirs = @('UserFanTables')
    if ($sameVariant) {
        $preserveDirs = @('UserPofiles', 'UserFanTables')
        Write-Log ("  user data variant check: installed='{0}' (family '{1}') incoming='{2}' (family '{3}') -> same family, UserPofiles preserved" -f $installedVariant, $installedFamily, $selection.Variant, $incomingFamily)
    }
    else {
        $existingUserProfiles = Join-Path (Join-Path $serviceTarget 'MyControlCenter') 'UserPofiles'
        if (Test-Path -LiteralPath $existingUserProfiles) {
            Write-Log ("  user data variant check: installed='{0}' (family '{1}') incoming='{2}' (family '{3}') -> different family or unknown, NOT preserving UserPofiles (avoids mixed-generation profiles)" -f $installedVariant, $installedFamily, $selection.Variant, $incomingFamily)
        }
    }
    # The backup is staged in {tmp}, NOT under {app}: the install directory must keep nothing but
    # the payload, the shared language files and the three .ps1. It survives until the restore
    # below (same process, same run), so {tmp} is a safe place for it.
    # ⭐ 2026-10-15：**不再 Join-Path $env:TEMP** —— TEMP 不可用的机器上那一句会直接抛
    # "Cannot find drive / Cannot find path" 把整个安装打断（真机实测：解包已成功、却死在这一步）。
    # 复用 New-GcuUnpackDir 里**用写探针验证过**的基目录（同一次运行、同一个进程内），拿不到才退回旧行为。
    $backupBase = $script:GcuWorkBase
    if (-not $backupBase) { $backupBase = $env:TEMP }
    $backupRoot = Join-Path $backupBase ('.lm-gcu-userdata-' + (Get-Date).ToString('yyyyMMddHHmmss'))
    $preserved = @()
    foreach ($dirName in $preserveDirs) {
        $existing = Join-Path (Join-Path $serviceTarget 'MyControlCenter') $dirName
        if (Test-Path -LiteralPath $existing) {
            $dst = Join-Path $backupRoot $dirName
            New-Item -ItemType Directory -Path $dst -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $existing '*') -Destination $dst -Recurse -Force
            $preserved += $dirName
            Write-Log ("  preserving user data: {0} -> {1}" -f $existing, $dst)
        }
    }
    # 2026-10-13: the bundled driver is also shipped as a standard [Files] entry straight into
    # {app}\UWACPIDriver, so a missing staging copy must not abort the GCU step - the .inf/.cat/.sys
    # are already in place and the signature check + pnputil below still run on them.
    if (Test-Path -LiteralPath $driverSource) {
        Copy-Tree -Source $driverSource -Destination $driverTarget
    }
    else {
        Write-Log ("  UWACPIDriver staging source missing ({0}); using the copy already installed under {1}" -f $driverSource, $driverTarget)
    }
    # logo: no copy step. Since 2026-10-14 the two language files are plain [Files] entries
    # straight into {app}\GCU\logo\Language (owner: "put logo in the GCU folder"), exactly where the
    # old package's base tree kept them ({app}\GCU\payload\GCU\logo\Language before the 7z switch).
    $logoDir = Join-Path $TargetDir 'logo\Language'
    if (Test-Path -LiteralPath (Join-Path $logoDir 'zh-cn.json')) {
        Write-Log ("  logo language files already in place (shared [Files] entry): {0}" -f $logoDir)
    }
    else {
        Write-Log ("  WARNING: no logo language files under {0}; the package's shared\logo\Language entry did not land" -f $logoDir)
    }

    Write-Log '[4/8] verifying Authenticode signatures of the unpacked payload (staged copy, before it is landed)'
    # ⚠️ Verify the STAGED files, not the {app}-side ones: the whole point of this order is to prove
    # the archive is complete and signed BEFORE anything is written over the existing install tree.
    # Checking $serviceExe here would check the OUTPUT of the copy that has not happened yet (and on
    # a fresh install would fail outright - there is no GCUBridge.exe in {app} until the landing).
    Assert-SignedFile -Path (Join-Path $baseServiceSource 'GCUBridge.exe') -Label 'GCUBridge (staged)'
    Assert-SignedFile -Path (Join-Path $baseServiceSource 'MyControlCenter\GCUService.exe') -Label 'GCUService (staged)'
    # The driver is NOT part of the archive: it is a plain [Files] entry that already landed in
    # {app}\UWACPIDriver, so it is checked in place (unchanged from before).
    Assert-SignedFile -Path $driverSys -Label 'UWACPIDriver'

    # ---- the landing itself: only now, with the vendor files proven complete + signed ----
    # 2026-10-14 (owner correction): the unpacked payload goes DIRECTLY into
    # {app}\GCU\AiStoneService - no payload\<Variant>\<ServiceDir> level, no template tree, real
    # files in their real place. Copy-Tree copies the CONTENTS of the staged service directory, so
    # the result is {app}\GCU\AiStoneService\GCUBridge.exe / MyControlCenter\GCUService.exe ...
    #
    # Why it is here and not in [3/8]: if the copy happened before the signature check, a corrupt
    # or incomplete archive would overwrite an existing, working payload with a broken one and only
    # THEN abort - leaving the exact "no runnable service in the tree" state that was observed. The
    # backup of the user data (fan curves / mode profiles) was taken in [3/8] and is restored
    # immediately after the copy, so the overwrite cannot lose it.
    Write-Log ('[4/8] landing the verified payload: {0} -> {1}' -f $baseServiceSource, $serviceTarget)
    Copy-Tree -Source $baseServiceSource -Destination $serviceTarget
    # Re-assert on the LANDED files: the copy is what the service will actually run from, so a
    # landing that silently produced the wrong bytes must fail here rather than at first service start.
    Assert-SignedFile -Path $serviceExe -Label 'GCUBridge (landed)'
    Assert-SignedFile -Path $gcuServiceExe -Label 'GCUService (landed)'
    # ---- anti-regression: the rename actually happened, and the target tree is complete ----
    # 1020/30 unpack to a folder called 'UniwillService' inside their archive and are laid down as
    # 'AiStoneService'. If that rename ever silently stops working, the copy would land the tree
    # under the wrong leaf (or nest it) and the service would start from the wrong path - which is
    # exactly the regression the owner asked to be caught before a 1020/30 machine sees it.
    # Variant-aware: only the archives whose INTERNAL name is 'UniwillService' can leave such a
    # residue, and only they would show a stale sibling inside the target directory.
    if ($archiveInfo.ServiceDir -eq 'UniwillService') {
        $legacyLeaf = Join-Path $TargetDir 'UniwillService'
        if (Test-Path -LiteralPath $legacyLeaf) {
            throw ("payload landing invariant violated: variant '{0}' unpacked a 'UniwillService' tree, so {1} must have been laid down as '{2}' - but a '{3}' directory is present under {4}, which means the rename did NOT take effect" -f $variantKey, $serviceTarget, $script:GcuServiceLeafName, 'UniwillService', $TargetDir)
        }
        Write-Log ("  rename invariant OK: variant '{0}' came from 'UniwillService' and landed as '{1}'; no 'UniwillService' residue under {2}" -f $variantKey, $script:GcuServiceLeafName, $TargetDir)
        # A nested copy (…\AiStoneService\AiStoneService) is the other classic symptom of the
        # source and destination sharing a shape; assert it did not happen either.
        $nestedLeaf = Join-Path $serviceTarget $script:GcuServiceLeafName
        if (Test-Path -LiteralPath $nestedLeaf) {
            throw ("payload landing invariant violated: nested '{0}' inside {1} - the copy used the service directory itself as the source instead of its contents" -f $script:GcuServiceLeafName, $serviceTarget)
        }
        Write-Log ("  nesting invariant OK: no nested '{0}' inside {1}" -f $script:GcuServiceLeafName, $serviceTarget)
    }
    # Both files every generation ships must exist at the FIXED landing path (variant-independent).
    foreach ($probe in @($serviceExe, $gcuServiceExe)) {
        if (-not (Test-Path -LiteralPath $probe)) {
            throw ("payload landing invariant violated: '{0}' missing after the copy into {1}" -f $probe, $serviceTarget)
        }
    }
    Write-Log ("  landing invariant OK: {0} + {1} present under {2}" -f 'GCUBridge.exe', 'MyControlCenter\GCUService.exe', $serviceTarget)
    if ($preserved.Count -gt 0) {
        foreach ($dirName in $preserved) {
            $src = Join-Path $backupRoot $dirName
            $dst = Join-Path (Join-Path $serviceTarget 'MyControlCenter') $dirName
            if (-not (Test-Path -LiteralPath $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
            Copy-Item -LiteralPath (Join-Path $src '*') -Destination $dst -Recurse -Force
            Write-Log ("  restored user data: {0} -> {1}" -f $src, $dst)
        }
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
        Write-Log '  user data (modes + fan curves) preserved'
    }
    # The payload is in place -> the install-time temporary files have done their job. 7za.exe and
    # the archive are deleted from the staging directory ({tmp}) so no copy of either can ever end
    # up inside {app}\GCU (owner: "do NOT keep 7z", "payload should be a TEMPORARY file").
    Remove-GcuStagingTemporaries -ArchivePath $script:StagingArchive -SevenZipPath $script:StagingSevenZip
    Remove-Item -LiteralPath $unpackRoot -Recurse -Force -ErrorAction SilentlyContinue

    if ($DryRun) {
        Write-Log 'DRY RUN: copied + signature-verified only; skipping driver/service/firewall changes'
        Write-Log ("DRY RUN output: {0}" -f $serviceTarget)
        exit 0
    }

    Write-Log '[5/8] installing UWACPI kernel driver'
    Install-UwacpiDriver -DriverDir $driverTarget

    Write-Log '[6/8] registering GCUBridge service'
    Install-Service -ServiceExe $serviceExe



    Write-Log '[7/8] firewall rule; starting GCU service (readiness is checked in the post-install self-check below)'
    Ensure-FirewallRule
    Set-InstallMarker -Selection $selection -ServiceExe $serviceExe -PayloadSha256 $payloadManifest.Sha256 -InstallerVersion $InstallerVersion
    try {
        Start-Service -Name $script:ServiceName -ErrorAction Stop
        Write-Log '  GCU service start issued (async; not waiting for readiness)'
    }
    catch {
        Write-Log ('  WARNING: GCU service start failed (' + $_.Exception.Message + '); it will start on next boot via autostart task')
    }

    # Autostart must be registered even if GCU verification later fails. Previously this
    # ran after `exit 1`, so a ServiceReady miss left the machine with no boot task.
    Write-Log '[8/8] privileges: autostart task (highest), directory ACLs, device access'
    if ([string]::IsNullOrWhiteSpace($AppExe)) {
        $AppExe = Join-Path (Split-Path -Parent $TargetDir) 'L-Mechrevo.exe'
    }
    Register-AutostartTask -AppExe $AppExe | Out-Null
    Grant-AppDirectoryAcl -Path (Split-Path -Parent $TargetDir)
    Grant-AppDirectoryAcl -Path $TargetDir
    if ([string]::IsNullOrWhiteSpace($ConfigDir)) {
        $ConfigDir = Join-Path $env:APPDATA 'MechrevoLite'
    }
    Grant-AppDirectoryAcl -Path $ConfigDir
    Grant-AppDirectoryAcl -Path $LogDir
    Grant-AcpiDriverAccess

    # ---------------------------------------------------------------------------
    # Post-install self-check (2026-10-09): Test-GcuPostInstall / Wait-GcuPostInstallFacts /
    # Write-GcuInstallStatus existed but main never called them, so a payload that never landed and a
    # service that never became ready were both silent (no gcu-install-status.json at all).
    # Failure semantics:
    #   - payload landing mismatch (expected file missing / size / SHA256 differs) = REAL FAILURE ->
    #     status json + explicit ERROR log + exit 1; the Inno [Run] entry has no ignoreerrors, so
    #     setup reports the failure instead of ending on a green "finished".
    #   - service/backend not "ready" yet = WARNING only (the vendor service may come up on the next
    #     boot); the status json records it and the install still succeeds.
    # ---------------------------------------------------------------------------
    Write-Log '[post-install] self-check: payload landing + GCU service readiness'
    $landing = Test-GcuPayloadLanded -Manifest $payloadManifest -TargetDir $serviceTarget
    Write-Log ("  payload landing: {0}/{1} file(s) verified with size + SHA256 under {2}" -f $landing.OkCount, $landing.Checked, $serviceTarget)
    foreach ($m in @($landing.Missing)) { Write-Log ('  ERROR expected payload file missing after the copy: ' + $m) }
    foreach ($m in @($landing.Mismatched)) { Write-Log ('  ERROR payload file does not match the shipped bytes: ' + $m) }

# 2026-10-13 (owner): 30 s -> 5 s total. Deadline exceeded = log + continue, never a failure.
    $waited = Wait-GcuPostInstallFacts -TimeoutSeconds 5
    $facts = $waited.Facts
    $verdict = $waited.Verdict
    foreach ($check in @($verdict.Checks)) {
        Write-Log ("  self-check {0}: {1} - {2}" -f $check.Name, $(if ($check.Ok) { 'OK' } else { 'FAIL' }), $check.Detail)
    }
    Write-Log ("  facts: services=[{0}] 13688-listeners={1} ItemSupport={2} ServiceReady={3}" -f (@($facts.ServiceNames) -join ','), @($facts.ListenerPids).Count, $facts.ItemSupportPresent, $facts.ServiceReady)

    $allChecks = @($landing.Check) + @($verdict.Checks)
    if (-not $landing.Ok) {
        $reason = 'payload landing check FAILED: ' + $landing.Check.Detail
        $statusPath = Write-GcuInstallStatus -StatusDir $StatusDir -Status 'Failed' -Reason $reason -Checks $allChecks
        Write-Log ('ERROR: ' + $reason)
        Write-Log ('ERROR: the shipped payload did not land; status written to ' + $statusPath + ' - exiting 1 so setup reports it')
        exit 1
    }
    if (-not $verdict.Ok) {
        $reason = 'GCU service/backend not ready yet: ' + ((@($verdict.Failed) | ForEach-Object { $_.Name }) -join ',')
        $statusPath = Write-GcuInstallStatus -StatusDir $StatusDir -Status 'Warning' -Reason $reason -Checks $allChecks
        Write-Log ('WARNING: ' + $reason)
        Write-Log ('WARNING: every expected payload file IS correctly installed, so this is not fatal; the vendor service may become ready after a reboot (status: ' + $statusPath + ')')
    }
    else {
        $statusPath = Write-GcuInstallStatus -StatusDir $StatusDir -Status 'Ok' -Reason 'payload landed and GCU service reported ready' -Checks $allChecks
        Write-Log ('  post-install self-check OK (status: ' + $statusPath + ')')
    }

    # ---------------------------------------------------------------------------
    # Warm-up fallback (2026-10-09, warmupreload): last resort, and ONLY on this full-install path.
    # A running L-Mechrevo froze its capability/model identity while the GCU backend was still
    # starting, so it may stay in read-only mode until it is restarted. The app has an in-place
    # soft reload for the ItemSupport part, but not for state it read once at construction time,
    # so: GCU was just installed -> restart the app ONCE (stop + start), and log exactly why.
    # Not running = nothing happens. VerifyOnly (nothing was laid down) returns earlier and never
    # reaches here. Failures are logged only; this step can never fail the install.
    # ---------------------------------------------------------------------------
    # ⭐⭐ 2026-10-15（owner：「脚本跑完了能不能清理手动安装导致的多余文件」）：
    # 走到这里 = ⭐ 载荷已落好（landing 检查通过 ✅）+ ⭐ 系统步骤已做完 ⇒ ⭐ 手工那套可以收走了：
    #   ⭐ ① 归档 `{GCU}\<变体>.7z`（约 17 MB）
    #   ⭐ ② 用户解压出来的那份副本 `{GCU}\<顶层>\<服务目录>`（约 56 MB；⛔ 绝不碰 `{GCU}\AiStoneService`）
    #   ⭐ ③ `安装GCU.cmd` 与 `手动安装说明.txt`
    # ⚠️ 只在"载荷确实已就位"时才清 —— 上面 `if (-not $landing.Ok) { … exit 1 }` 已经把关 ✅
    # ⚠️ `{GCU}\logs`（手工那次运行的日志）**故意保留**：它是排障证据；卸载清单里已加对应项 ✅
    if ($script:ManualPayloadLanding -or @(Get-ChildItem -LiteralPath $TargetDir -Filter '*.7z' -File -ErrorAction SilentlyContinue).Count -gt 0) {
        Remove-ManualPayloadLeftovers -TargetDir $TargetDir -Variant ([string]$archiveInfo.Variant) -IncludeHelper $true
    }

    Restart-AppAfterGcuInstall -AppExe $AppExe

    Write-Log 'GCU install OK'
    exit 0
}
catch {
    # Best-effort tidy-up on the failure path: a fatal AFTER the unpack (e.g. the staged signature
    # check refusing a bad archive) would otherwise leave its lm-gcu-unpack-* directory behind. The
    # user-data backup under %TEMP% is deliberately NOT removed here - a failed landing must keep
    # the user's curves recoverable (the success path deletes it after the restore).
    if ($script:UnpackRoot -and (Test-Path -LiteralPath $script:UnpackRoot)) {
        try {
            Remove-Item -LiteralPath $script:UnpackRoot -Recurse -Force -ErrorAction Stop
            Write-Log ("  failure cleanup: unpack staging removed ({0})" -f $script:UnpackRoot)
        }
        catch {
            Write-Log ("  failure cleanup: could not remove unpack staging {0} ({1}); harmless, %TEMP% is cleaned by the OS" -f $script:UnpackRoot, $_.Exception.Message)
        }
    }
    if ($script:LogFile) {
        # ⚠️ 2026-10-15：`$_.Exception.Message` 在 PS 5.1 的 NativeCommandError 一类错误记录上**可能是空的**
        # （真机 22:47 的日志就是「FATAL: 」+ 什么都没有 ⇒ 唯一线索被白白丢掉）。
        # 因此这里补打**整个错误记录的 Out-String** 与 `Exception.ToString()`，保证任何情况下都留下可读文本。
        Write-Log ('FATAL: ' + $_.Exception.Message)
        try {
            $fatalDetail = ($_ | Out-String).Trim()
            if ($fatalDetail) { Write-Log ('FATAL detail: ' + $fatalDetail) }
            if ($_.Exception) { Write-Log ('FATAL exception: ' + $_.Exception.ToString()) }
            if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) { Write-Log ('FATAL at: ' + ($_.InvocationInfo.PositionMessage -replace "`r?`n", ' ')) }
        } catch {
            Write-Log 'FATAL detail unavailable'
        }
        Write-Log ($_.ScriptStackTrace)
    }
    else {
        Write-Error $_
    }
    exit 1
}
