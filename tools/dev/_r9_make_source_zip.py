# -*- coding: utf-8 -*-
"""生成 r9 源码包：沿用 r8 的条目集合 + 新增两份 THIRD_PARTY_NOTICES,*.txt
   + 刷新 build.ps1 参考哈希（当前构建产物）与 SOURCE-README.md 到 r9。
用法：py -3 _r9_make_source_zip.py
"""
import sys, os, io, zipfile, hashlib
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"D:\openLM"
Z8 = os.path.join(ROOT, "dist", "LM-0.291.6-beta22.6-source-20261010-r8.zip")
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8"
TOP9 = "LM-0.291.6-beta22.6-source-20261010-r9"
OUT = os.path.join(ROOT, "dist", TOP9 + ".zip")

NEW_FILES = [
    r"installer-src\files\THIRD_PARTY_NOTICES,1.txt",
    r"installer-src\files\THIRD_PARTY_NOTICES,2.txt",
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
# (LM-0.291.6-beta22.6-setup.exe, archive revision r9, 2026-10-10 — verified against this build):
#   L-Mechrevo.dll                2,594,304 B  6A9EE40EC5DFBC5EC7B6DDBE8FBBB8040993AD941F0F0E75DC498CB30006C02E
#   zh-CN\L-Mechrevo.resources.dll   83,456 B  CBF5B3D5D519EFC463837F0955D80367D2A2F00BAC41F471F0CF826BBA0AB30A
#   L-Mechrevo.exe                  170,496 B  4AF6EFCB6986CCC45FB4524E476622E3032EC0347D6376C77BC71160BF7E0380
# (previous revision r8, superseded: L-Mechrevo.dll 2,580,480 B / 771EF2A4941A097D87ECA9E5646068C8F284AC4AE8B59B92FE147F7085C2DE27,
#  zh-CN satellite 83,456 B / 2D2CF5817E8F80BCEDF659CC4C3937FA92481EF6902C5CBA2E24D3FA7F33C0E4;
#  r7: 2,553,344 B / 128430E4F714362D6E49E91FB724BC09BB1AADEBA12D1D7D57D9B7692C88026B)
'''

SOURCE_README = r'''# L-Mechrevo 0.291.6 beta22.6 — Corresponding Source

This archive is the Corresponding Source for the binary release
`LM-0.291.6-beta22.6-setup.exe` (GPL-3.0-only, GPLv3 §6 / LGPL-2.1 §6).

- Version: **0.291.6** (product version string `0.291.6-beta22.6`). This revision of the archive (r9) is
  the Corresponding Source for the installer rebuilt on the same packaging baseline as r5–r8 (the older
  50-series GCU generation "50old", `payload/GCU` base plus `30` / `40` / `40B` / `50` overlays and
  `payload/50/.../GamdeMode.db`), with the following application increments over r8:
  1. **Per-core Curve-Optimizer command table opened up in full** — `source/PawnIO/RyzenSmuService.cs`
     (76,996 B -> 80,454 B in this revision): the per-core CO write path is no longer restricted to a
     single codename.
  2. **CPU tuning page reshaped** — `source/MechrevoLite/CpuTuningForm.cs` (96,034 B -> 114,440 B): the
     all-core CO (undervolt) input box is removed, the layout is re-tiled, and a dedicated frequency row
     is added. The new string `CpuTuneFreqNote` explains that FMax (the PBO boost ceiling) and the fixed
     all-core clock are two different SMU operations.
  3. **Per-core write availability / outcome handling** — `source/MechrevoLite.Hardware/RuntimeCpuTuning.cs`
     (75,551 B -> 86,123 B) and `source/MechrevoLite.Hardware/CpuTuningOption.cs` (1,297 B -> 1,594 B)
     follow the widened command table; the wording for "no per-core write path" now says the codename has
     no per-core write command in the SMU reference (`CpuTuneSummaryPerCoreOff`, `CpuTunePerCoreCodenameOnly`),
     and `CpuTuneValueCo` is marked legacy because its input box is gone.
  4. **UI audit updated** — `source/MechrevoLite.UI/UiAuditRunner.cs` (50,900 B -> 60,894 B) now covers the
     per-core write row / the re-tiled page.
  5. **Attribution gap closed** — both end-user notices
     (`installer-src/files/THIRD_PARTY_NOTICES,1.txt` English, `,2.txt` Chinese) now name the second
     embedded PawnIO.Modules module **`LMechrevo.IntelMSR.bin`** (5,324 B, SHA-256
     `D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F`, LGPL-2.1-or-later), matching
     `THIRD-PARTY-NOTICES.md` §1.2 B4 / §1.3 and the in-app "About — open source licences" window
     (`PawnIO.Modules IntelMSR.bin`). `THIRD-PARTY-NOTICES.md` itself is now listed in
     `installer-src/install_script.iss` and therefore ships as `{app}/THIRD-PARTY-NOTICES.md` — it is the
     file that the shipped root `LICENSE` and `LICENSES/README.md` both point at.
  6. `source/MechrevoLite.Properties.Strings.resx` / `Strings.zh-CN.resx` carry the wording above (one new
     key plus the revised CPU-tuning strings). The charge-limit, FMax/Intel and lighting increments
     described in r8 are unchanged and still present.
   It keeps the packaging that puts `LICENSES/` (`GPL-3.0.txt`, `LGPL-2.1.txt`, `README.md`), the root
   `LICENSE` and now also `THIRD-PARTY-NOTICES.md` inside the installer, as the in-app "About — open
   source licences" window promises. `installer-src/install_script.iss` refers to `..\LICENSE`,
   `..\LICENSES\*` and `..\THIRD-PARTY-NOTICES.md`, which resolve inside this archive.
- Packaged: 2026-10-10 (archive revision r9)

- Copyright (C) 2026 L-Mechrevo contributors

## What is inside

| Path | Contents |
|---|---|
| `source/` | Complete C# source tree of `L-Mechrevo.dll` (net10.0-windows10.0.19041.0, x64): all `.cs`, `.resx`, `.csproj`, embedded resources (`LMechrevo.RyzenSMU.bin`, `LMechrevo.IntelMSR.bin`, `LMechrevo.model-registry.json`, `LMechrevo.gcu-payload-baseline.txt`), fonts, icons and the in-repo PowerShell helpers. |
| `LICENSE` | L-Mechrevo's own licence file: short GPL-3.0-only notice **plus** the canonical GPL-3.0 text appended verbatim (byte-identical to G-Helper's `LICENSE`, git blob `f288702d…`). |
| `LICENSES/` | `GPL-3.0.txt`, `LGPL-2.1.txt` (both byte-identical to upstream), `README.md` explaining provenance. |
| `THIRD-PARTY-NOTICES.md` | Full third-party inventory, licence table, per-project derivation notes and the outstanding verification gaps. Also shipped inside the installer. |
| `ATTRIBUTION-PATCH-PLAN.md`, `ATTRIBUTION-CO-ADDENDUM.md` | The in-code / in-UI attribution plan and the AMD SMU / Curve-Optimizer derivation audit (which upstream file was compared byte-for-byte, at which commit). |
| `installer-src/install_script.iss`, `installer-src/install_script_nogcu.iss` | The Inno Setup 6 packaging scripts used to produce the binary release (the main one is the r9-aligned revision). |
| `installer-src/files/THIRD_PARTY_NOTICES,1.txt` / `,2.txt` | The end-user third-party notices (English / Chinese) exactly as the installer carries them; both name the embedded `RyzenSMU.bin` and `LMechrevo.IntelMSR.bin` modules with size, SHA-256, licence and upstream URL. |
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
  namazso) — embedded verbatim as managed resources; named in both end-user notices
  (`installer-src/files/THIRD_PARTY_NOTICES,1.txt` line 65 ff., `,2.txt` line 65 ff.) and covered by
  `LICENSES/LGPL-2.1.txt` (shipped as `COPYING.PawnIO.txt` as well).
- `irusanov/SMUDebugTool` is a **protocol / behaviour reference only** — no copied expression.
'''


def main():
    z8 = zipfile.ZipFile(Z8)
    names8 = z8.namelist()
    z8.close()

    dirs, files = [], []
    for n in names8:
        rel = n[len(TOP8) + 1:]
        if rel == "":
            dirs.append("")
        elif n.endswith("/"):
            dirs.append(rel[:-1])
        else:
            files.append(rel)
    for nf in NEW_FILES:
        nf = nf.replace("\\", "/")
        if nf not in files:
            files.append(nf)
    files = sorted(set(files), key=lambda s: s)
    dirs = sorted(set(dirs), key=lambda s: s)

    entries = [(TOP9 + "/" + d + "/") if d else (TOP9 + "/") for d in dirs]
    entries += [TOP9 + "/" + f for f in files]

    if os.path.exists(OUT):
        os.remove(OUT)
    n_gen = 0
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for e in entries:
            rel = e[len(TOP9) + 1:]
            if e.endswith("/"):
                zi = zipfile.ZipInfo(e, date_time=(2026, 10, 10, 23, 45, 0))
                zi.external_attr = 0x10
                z.writestr(zi, b"")
                continue
            if rel == "build.ps1":
                data = BUILD_PS1.encode("utf-8")
                n_gen += 1
            elif rel == "SOURCE-README.md":
                data = SOURCE_README.encode("utf-8")
                n_gen += 1
            else:
                p = os.path.join(ROOT, rel.replace("/", "\\"))
                if not os.path.isfile(p):
                    print("!! MISSING ON DISK:", rel)
                    continue
                data = open(p, "rb").read()
            zi = zipfile.ZipInfo(e, date_time=(2026, 10, 10, 23, 45, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED
            zi.external_attr = 0x20
            z.writestr(zi, data)

    h = hashlib.sha256(open(OUT, "rb").read()).hexdigest().upper()
    zf = zipfile.ZipFile(OUT)
    nfiles = sum(1 for n in zf.namelist() if not n.endswith("/"))
    ndirs = sum(1 for n in zf.namelist() if n.endswith("/"))
    # 二进制对应性：包内 source 与磁盘逐一比哈希
    diff = []
    for n in zf.namelist():
        if n.endswith("/"):
            continue
        rel = n[len(TOP9) + 1:]
        if rel in ("build.ps1", "SOURCE-README.md"):
            continue
        p = os.path.join(ROOT, rel.replace("/", "\\"))
        if not os.path.isfile(p) or open(p, "rb").read() != zf.read(n):
            diff.append(rel)
    zf.close()
    print("OUT      =", OUT)
    print("size     = %d B" % os.path.getsize(OUT))
    print("sha256   =", h)
    print("entries  = %d（文件 %d + 目录 %d）" % (len(entries), nfiles, ndirs))
    print("包内生成件 = %d（build.ps1 / SOURCE-README.md）" % n_gen)
    print("与磁盘内容不一致的条目 = %d %s" % (len(diff), diff[:5]))
    for probe in ("installer-src/files/THIRD_PARTY_NOTICES,1.txt",
                  "installer-src/files/THIRD_PARTY_NOTICES,2.txt",
                  "THIRD-PARTY-NOTICES.md",
                  "source/PawnIO/RyzenSmuService.cs",
                  "source/MechrevoLite/CpuTuningForm.cs",
                  "source/MechrevoLite.Hardware/RuntimeCpuTuning.cs",
                  "source/MechrevoLite.UI/UiAuditRunner.cs",
                  "source/PawnIO/IntelOcMailbox.cs",
                  "source/PawnIO/IntelOcDomain.cs",
                  "source/LMechrevo.IntelMSR.bin",
                  "installer-src/install_script.iss"):
        zf = zipfile.ZipFile(OUT)
        ok = probe in zf.namelist()
        n = zf.getinfo(probe).file_size if ok else 0
        zf.close()
        print("   含 %-52s %s %d B" % (probe, ok, n))
    return 0


if __name__ == "__main__":
    sys.exit(main())
