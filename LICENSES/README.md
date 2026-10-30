# LICENSES\ — upstream licence texts shipped with L-Mechrevo

This folder holds **verbatim, unmodified** licence texts of third-party components that L-Mechrevo
derives from or conveys in binary form. See `THIRD-PARTY-NOTICES.md` (in the source tree: `..\THIRD-PARTY-NOTICES.md`) for the full inventory and
`..\..\docs\archive\attribution-20261010\ATTRIBUTION-PATCH-PLAN.md` for how these files get into the installer.

## Contents

| File | Covers | Size | SHA-256 | Origin |
|---|---|---|---|---|
| `GPL-3.0.txt` | G-Helper, irusanov/ZenStates-Core, irusanov/SMUDebugTool, kitsunyan/intel-undervolt, JamesCJ60/Universal-x86-Tuning-Utility, tomups/watercooler-manager — **and L-Mechrevo itself** | 35149 B | `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986` | copied byte-for-byte from `..\installer-src\files\LICENSE,1.txt`, which is byte-identical to `seerge/g-helper@main:LICENSE` (git blob SHA-1 `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7`) |
| `LGPL-2.1.txt` | namazso/PawnIO.Modules — the `RyzenSMU.bin` module embedded in `L-Mechrevo.dll` | 26530 B | `DC626520DCD53A22F727AF3EE42C770E56C97A64FE3ADB063799D8AB032FE551` | copied byte-for-byte from `..\_upstream\0.290.6\extract\{app}\COPYING.PawnIO.txt`, which is byte-identical to `namazso/PawnIO.Modules@main:COPYING` (git blob SHA-1 `4362b49151d7b34ef83b3067a8f9c9f877d72a0e`) |
| `MIT-JiaolongControl.txt` | GaoXanSheng/JiaolongControl (`windows/X86-64`) — internal notes quote code fragments; the Baolongda protocol facts were read from it | 1078 B | `89FA9E1553B3FF96B9B51F18FCE7E5A74580FAC3029BF44902F87996D6696C17` | written for this repo from the upstream MIT text, **Copyright (C) 2025 GaoXanSheng**; MIT requires this notice to ship with the binary |

`LGPL-2.1.txt` is also the file referred to, under its upstream name, as `COPYING.PawnIO.txt` by the
previous release's installer (`_upstream\0.290.6\extract\install_script.iss:32`). When restoring that
installer line, either keep the upstream file name or point the notice at `LICENSES\LGPL-2.1.txt` —
but ship **one** of them, because the module is embedded and therefore conveyed.

## Not yet present (add only when the corresponding code or binary is actually conveyed)

| File | Add when | Fetch from |
|---|---|---|
| `LGPL-3.0.txt` | any expression from FlyGoat/RyzenAdj is copied into our source | `https://raw.githubusercontent.com/FlyGoat/RyzenAdj/master/LICENSE` |
| `GPL-2.0.txt` | any expression from leogx9r/ryzen_smu, coreboot, or namazso/PawnIO (driver) is copied in — **all three are GPL-2.0-only and must not be merged into our GPL-3.0-only tree** | `https://raw.githubusercontent.com/namazso/PawnIO/master/COPYING` (canonical GPLv2 text) |
| `RyzenSMU-module-source\` (or a `MODULE-SOURCE.md` with an exact URL + tag) | always, for the embedded module: LGPL-2.1 §6(a) wants the library's Corresponding Source, and the notice currently cannot state which upstream version we ship (see `..\THIRD-PARTY-NOTICES.md` §6.1) | `https://github.com/namazso/PawnIO.Modules` — pick the tag whose `RyzenSMU.bin` matches `source\LMechrevo.RyzenSMU.bin`, SHA-256 `B84ECA7F32C63B3D8C14B2C6D45482706DF8683AA6F43EB8BEAD9DC62181D38F` |
| `NOTICE-nuget.md` | before the next release | NuGet / upstream metadata for each DLL listed in `..\THIRD-PARTY-NOTICES.md` §1.2 B6 |

## Rules for this folder

1. **Never edit these files.** Additions and corrections mean replacing the file with a fresh copy
   from upstream, then updating the SHA-256 above.
2. **Never add a licence here for something we do not derive from or convey.** A gratuitous licence
   file is a false statement about provenance.
3. **Verify by hash, not by size.** The two files above were accepted only after their git blob SHA-1
   matched the upstream blob hash reported by the GitHub API.
4. Anything added here must also be listed in `..\THIRD-PARTY-NOTICES.md` **and** in the end-user
   notice at `..\installer-src\files\THIRD_PARTY_NOTICES,1.txt` / `,2.txt`, **and** added to
   `..\installer-src\install_script.iss` so it actually reaches `{app}\`.
