# -*- coding: utf-8 -*-
"""生成 r8 源码包：沿用 r7 的条目集合 + r8 新增 3 个源文件 + 更新 build.ps1 / SOURCE-README.md"""
import sys, os, io, zipfile, hashlib
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"D:\openLM"
Z7 = os.path.join(ROOT, "dist", "LM-0.291.6-beta22.6-source-20261010-r7.zip")
TOP7 = "LM-0.291.6-beta22.6-source-20261010-r7"
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8"
OUT = os.path.join(ROOT, "dist", TOP8 + ".zip")

NEW_FILES = [
    r"source\LMechrevo.IntelMSR.bin",
    r"source\PawnIO\IntelOcDomain.cs",
    r"source\PawnIO\IntelOcMailbox.cs",
]

BUILD_PS1 = r'''# Builds the exact L-Mechrevo.dll that shipped in LM-0.291.6-beta22.6-setup.exe.
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# The reference DLLs are not part of this source archive (they are third-party binaries).
$refDir = Join-Path $root 'lm_pkg_files\{app}'
if (-not (Test-Path $refDir)) {
    Write-Host "[!] $refDir not found."
    Write-Host "    Copy the third-party reference DLLs listed in SOURCE-README.md there first."
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw "dotnet SDK not found on PATH (needs .NET SDK 10.0.4xx)." }

& $dotnet build (Join-Path $root 'source\L-Mechrevo.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }

$out = Join-Path $root 'source\bin\Release\net10.0-windows10.0.19041.0'
foreach ($f in @('L-Mechrevo.dll', 'zh-CN\L-Mechrevo.resources.dll')) {
    $p = Join-Path $out $f
    if (Test-Path $p) {
        "{0}  {1} B  {2}" -f $f, (Get-Item $p).Length, (Get-FileHash $p -Algorithm SHA256).Hash
    } else {
        "MISSING: $p"
    }
}
# Reference hashes of the shipped release
# (LM-0.291.6-beta22.6-setup.exe, archive revision r8, 2026-10-10 — verified against this build):
#   L-Mechrevo.dll                2,580,480 B  771EF2A4941A097D87ECA9E5646068C8F284AC4AE8B59B92FE147F7085C2DE27
#   zh-CN\L-Mechrevo.resources.dll   83,456 B  2D2CF5817E8F80BCEDF659CC4C3937FA92481EF6902C5CBA2E24D3FA7F33C0E4
#   L-Mechrevo.exe                  170,496 B  4AF6EFCB6986CCC45FB4524E476622E3032EC0347D6376C77BC71160BF7E0380
# (previous revision r7, superseded: L-Mechrevo.dll 2,553,344 B / 128430E4F714362D6E49E91FB724BC09BB1AADEBA12D1D7D57D9B7692C88026B,
#  zh-CN satellite 82,944 B / 2EC7FA9DBA0C24B72C4752747CEA2D5423FC317813B4732190CC3C0A71F41D3F)
'''

SOURCE_README = r'''# L-Mechrevo 0.291.6 beta22.6 — Corresponding Source

This archive is the Corresponding Source for the binary release
`LM-0.291.6-beta22.6-setup.exe` (GPL-3.0-only, GPLv3 §6 / LGPL-2.1 §6).

- Version: **0.291.6** (product version string `0.291.6-beta22.6`). This revision of the archive (r8) is
  the Corresponding Source for the installer rebuilt on the same packaging baseline as r5/r6/r7 (the older
  50-series GCU generation "50old", `payload/GCU` base plus `30` / `40` / `40B` / `50` overlays and
  `payload/50/.../GamdeMode.db`), with the following application increments over r7:
  1. **Charge-limit read-back, bit 7 masked** — `source/MechrevoLite.Hardware/EcChargeLimit.cs`: the
     `0x07B9` read-back value is a firmware "limit enabled" flag in bit 7 plus a percentage in the low
     7 bits, so `PercentFor` masks before clamping; the upstream recharge floor register `0x07D0` is no
     longer written (read-only observation only).
  2. **Charge-limit re-assertion protection** — an event entry point plus a 10 s heartbeat fallback
     (writes throttled to 60 s) in `ChargeLimitMonitor.cs` / the `EnsureChargeLimitHeld` re-assert path,
     with the authoritative value following the slider / three-preset switch; two interface-side
     overwrite holes found by that work are closed as well.
  3. **The three charge presets are no longer hidden on the 50 series** — the three presets are a shared
     *value source* while the EC limit is a *transport*; the three write paths (no-limit-percent slider,
     EC limit, GCU official charge limit) are now mutually exclusive in `SettingsForm.cs`.
  4. **Boot / session-end gating** — the EC is only written at start-up and at session end when the EC
     write path is actually in effect, and the diagnostics "function verification" no longer writes the
     GCU charge-tier register while the EC transport is active
     (`source/MechrevoLite.Diagnostics/FunctionVerifier.cs`).
  5. **Intel CPU tuning merged from the upstream 0.290.6 tree (the "⑦B" merge)** — new
     `source/PawnIO/IntelOcMailbox.cs` (MSR 0x150 OC mailbox over the PawnIO kernel wrapper, write
     verification and offset encoding) and `source/PawnIO/IntelOcDomain.cs`; the embedded
     `source/LMechrevo.IntelMSR.bin` resource declared in `source/L-Mechrevo.csproj`
     (`<EmbeddedResource>`); the Intel branch of `source/MechrevoLite.Hardware/RuntimeCpuTuning.cs`
     (probe / apply / restore, `IsIntel()` via CPUID.07H:EDX bit 15) with a three-state outcome
     (verified / accepted / failed); and the Intel row plus per-platform heading in
     `source/MechrevoLite/CpuTuningForm.cs`, which also stops showing the AMD-only FMax row on
     non-AMD CPUs. Attribution follows: `THIRD-PARTY-NOTICES.md` §1.2 B4 / §1.3, the new
     `AboutLicensesUsePawnIOIntelMsr` string (`Strings.resx` + `Strings.zh-CN.resx` + `Strings.cs`)
     and the `PawnIO.Modules IntelMSR.bin` entry of `AboutLicensesForm.cs`, mirrored in
     `ATTRIBUTION-PATCH-PLAN.md`.
  6. **Lighting is not pushed at start-up / resume without a saved profile**
     (`source/MechrevoLite/Program.cs` and the UI audit runner), so a fresh install no longer turns the
     keyboard lighting off.
  It keeps the packaging that puts `LICENSES/` (`GPL-3.0.txt`, `LGPL-2.1.txt`, `README.md`) and the
  root `LICENSE` inside the installer, as the in-app "About — open source licences" window promises.
  `installer-src/install_script.iss` refers to `..\LICENSE` and `..\LICENSES\*`, which resolve inside
  this archive.
- Packaged: 2026-10-10 (archive revision r8)

- Copyright (C) 2026 L-Mechrevo contributors

## What is inside

| Path | Contents |
|---|---|
| `source/` | Complete C# source tree of `L-Mechrevo.dll` (net10.0-windows10.0.19041.0, x64): all `.cs`, `.resx`, `.csproj`, embedded resources (`LMechrevo.RyzenSMU.bin`, `LMechrevo.IntelMSR.bin`, `LMechrevo.model-registry.json`, `LMechrevo.gcu-payload-baseline.txt`), fonts, icons and the in-repo PowerShell helpers. |
| `LICENSE` | L-Mechrevo's own licence file: short GPL-3.0-only notice **plus** the canonical GPL-3.0 text appended verbatim (byte-identical to G-Helper's `LICENSE`, git blob `f288702d…`). |
| `LICENSES/` | `GPL-3.0.txt`, `LGPL-2.1.txt` (both byte-identical to upstream), `README.md` explaining provenance. |
| `THIRD-PARTY-NOTICES.md` | Full third-party inventory, licence table, per-project derivation notes and the outstanding verification gaps. |
| `ATTRIBUTION-PATCH-PLAN.md`, `ATTRIBUTION-CO-ADDENDUM.md` | The in-code / in-UI attribution plan and the AMD SMU / Curve-Optimizer derivation audit (which upstream file was compared byte-for-byte, at which commit). |
| `installer-src/install_script.iss`, `installer-src/install_script_nogcu.iss` | The Inno Setup 6 packaging scripts used to produce the binary release (the main one is the r8-aligned revision). |
| `installer-src/打包须知.txt` | The packaging conventions sheet: GCU payload layering rules, per-variant file counts / byte totals and the canonical payload manifest hashes for this release. |
| `installer-src/files/GCU/gcu-variants.json` | The GCU payload variant manifest (per variant: on-disk files, self / merged / whole-tree hashes, diff vs base). It is metadata only — no vendor binary. |
| `50系GCU两版对比.md` | Contributor note comparing the two 50-series GCU generations (50old vs 50new) that motivated the base change in this release. |
| `替换LM.ps1` | The in-repo deployment script (SHA-256 compare -> back up -> replace `L-Mechrevo.dll` + `zh-CN` satellite -> single restart). |

## What is NOT inside (and why)

- **Vendor GCU payload binaries** (`installer-src/files/GCU/payload/**`: AiStoneService, MyControlCenter,
  UWACPIDriver, ...). These are the OEM's own redistributable artefacts, not L-Mechrevo source; they are
  only *carried* by the installer. The packaging script lists every one of them, so the full file list is
  auditable from `install_script.iss` alone. The two packaging-metadata files that *are* included
  (`installer-src/打包须知.txt`, `installer-src/files/GCU/gcu-variants.json`) contain no vendor binary.
- **Third-party reference DLLs** (`lm_pkg_files/{app}/*.dll`: Newtonsoft.Json, MQTTnet, HidSharp,
  LibreHardwareMonitorLib, Microsoft.Windows.SDK.NET, NvAPIWrapper, Probe, RAMSPDToolkit-NDD,
  BlackSharp.Core, DiskInfoToolkit, WinRT.Runtime, Microsoft.Win32.TaskScheduler, System.Management).
  `source/L-Mechrevo.csproj` references them by `<HintPath>` only; obtain them from their own upstream
  releases (see `THIRD-PARTY-NOTICES.md` for licences). To build, put them in `lm_pkg_files/{app}/`
  next to `source/`.
- **Build outputs** (`source/bin`, `source/obj`), **backups**, **diagnostics / packet captures**,
  **user configuration, logs and crash dumps**, **local build tooling** (bundled .NET SDK, innounp,
  innoextract) and the developer change log. One stray non-compiled leftover of the working tree
  (`source/MechrevoLite/CustomModeForm.cs.group_backup`) is omitted as well.

## How to build

Needs the .NET SDK 10.0.4xx (or a later 10.0.x) with the Windows Desktop workload, on Windows x64.
The third-party reference DLLs listed above must be present in `lm_pkg_files/{app}/`.

    # from the extracted archive root
    dotnet build source\L-Mechrevo.csproj -c Release
    # -> source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll
    #    source\bin\Release\net10.0-windows10.0.19041.0\zh-CN\L-Mechrevo.resources.dll

`build.ps1` in this archive runs exactly that command and prints the output hashes.

Packaging (needs Inno Setup 6; the vendor payload must be restored first):

    ISCC.exe installer-src\install_script.iss
    # -> installer-src\Output\LM-0.291.6-beta22.6-setup.exe

## Third-party components that are separately licensed

- Code derived from **G-Helper** (GPL-3.0-only), **irusanov/ZenStates-Core** (GPL-3.0-only) and the
  G-Helper AMD SMU layer — marked in the source headers and listed in the in-app "About" window.
- **PawnIO.Modules `RyzenSMU.bin`** and **`IntelMSR.bin`** (both LGPL-2.1-or-later, Copyright (C) 2025
  namazso) — embedded verbatim as managed resources; see `source/LMechrevo.RyzenSMU.bin.NOTICE.txt` and
  `LICENSES/LGPL-2.1.txt`.
- `irusanov/SMUDebugTool` is a **protocol / behaviour reference only** — no copied expression.
'''


def main():
    z7 = zipfile.ZipFile(Z7)
    names7 = z7.namelist()
    z7.close()

    dirs, files = [], []
    for n in names7:
        rel = n[len(TOP7) + 1:]
        if rel == "":
            dirs.append("")
        elif n.endswith("/"):
            dirs.append(rel[:-1])
        else:
            files.append(rel)
    for nf in NEW_FILES:
        if nf not in files:
            files.append(nf)
    files = sorted(set(files), key=lambda s: s)
    dirs = sorted(set(dirs), key=lambda s: s)

    # 与 r7 相同的排序口径：目录（有序）在前，文件（有序）在后
    entries = [(TOP8 + "/" + d + "/") if d else (TOP8 + "/") for d in dirs]
    entries += [TOP8 + "/" + f.replace("\\", "/") for f in files]

    if os.path.exists(OUT):
        os.remove(OUT)
    n_add = 0
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for e in entries:
            rel = e[len(TOP8) + 1:].replace("/", "\\")
            if e.endswith("/"):
                zi = zipfile.ZipInfo(e, date_time=(2026, 10, 10, 22, 50, 0))
                zi.external_attr = 0x10
                z.writestr(zi, b"")
                continue
            if rel == "build.ps1":
                data = BUILD_PS1.encode("utf-8")
                n_add += 1
            elif rel == "SOURCE-README.md":
                data = SOURCE_README.encode("utf-8")
                n_add += 1
            else:
                p = os.path.join(ROOT, rel)
                if not os.path.isfile(p):
                    print("!! MISSING ON DISK:", rel)
                    continue
                data = open(p, "rb").read()
            zi = zipfile.ZipInfo(e, date_time=(2026, 10, 10, 22, 50, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED
            zi.external_attr = 0x20
            z.writestr(zi, data)

    h = hashlib.sha256(open(OUT, "rb").read()).hexdigest()
    zf = zipfile.ZipFile(OUT)
    nfiles = sum(1 for n in zf.namelist() if not n.endswith("/"))
    ndirs = sum(1 for n in zf.namelist() if n.endswith("/"))
    zf.close()
    print("OUT =", OUT)
    print("size =", os.path.getsize(OUT))
    print("sha256 =", h)
    print("entries = %d (文件 %d + 目录 %d)" % (len(entries), nfiles, ndirs))
    print("包内独有生成件 =", n_add)


if __name__ == "__main__":
    main()
