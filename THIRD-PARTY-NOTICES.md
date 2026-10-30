# L-Mechrevo — Third-party notices / 第三方组件与开源许可清单

Scope: the C# WinForms application **L-Mechrevo** (`MechrevoLite`, assembly name `L-Mechrevo`,
version `0.291.5` per `source\Properties\AssemblyInfo.cs:19`) and the files it ships.

This document is the **repo-root master copy**. The installer ships a different, end-user-facing
variant at `installer-src\files\THIRD_PARTY_NOTICES,1.txt` (English) and
`installer-src\files\THIRD_PARTY_NOTICES,2.txt` (Chinese), installed as `{app}\THIRD_PARTY_NOTICES.txt`.
Keep the two in sync — see `docs/archive/attribution-20261010/ATTRIBUTION-PATCH-PLAN.md` for the exact edits.

Date of this inventory: 2026-10-10. All URLs and licences were fetched from the projects themselves
(GitHub API / raw file), not from memory; the raw evidence is quoted per row.

---

## 0. How to read this / method

* **"Derived source"** = our `.cs` contains material taken from that project (a table, an algorithm,
  a command sequence). Such a component triggers licence obligations.
* **"Protocol reference"** = we learned a wire format from it but copied no expression. Attribution
  is a courtesy and a defensive record; it normally triggers no licence obligation.
* **"Vendored binary"** = a compiled artifact from the project is embedded in or shipped beside
  `L-Mechrevo.dll`. This is the case that needs the most care.
* Line numbers are 1-based and refer to the working tree at `D:\openLM` as of the date above.

### ⚠️ Two things that look like third parties but are NOT

| Path | What it actually is | Consequence |
|---|---|---|
| `_upstream\0.290.6\` | **Our own previous release**, decompiled, plus the extracted installer payload and inter-version diffs. It contains `L-Mechrevo.dll` / `L-Mechrevo.exe`. | Do **not** list it as a third party. It is the reference baseline for "what did we change". |
| `beta21\`, `unpack18.*\`, `archive\`, `rebuild\`, `dist\`, `交付\`, `L-Mechrevo-18.6-source\`, `_re_decomp_924\` | Our own earlier builds / decompilations / delivery snapshots. | Same — not third parties. |

Evidence that `_upstream\0.290.6` is our own prior release: its `decomp\` tree mirrors our own
namespace layout 1:1, and our own source cites it as "上游 0.290.6" (e.g. `source\PawnIO\CpuInfo.cs:231`,
`CpuInfo.cs:295`).

### ⚠️ The `PawnIO` C# namespace is **our own code**, not namazso's

`source\PawnIO\*.cs` uses `namespace PawnIO;`, which misleads. Proof it is ours, carried forward
unchanged from our own 0.290.6:

| File | Working tree | `_upstream\0.290.6\decomp\PawnIO\` |
|---|---|---|
| `PawnIOWrapper.cs` | 4220 B | 4220 B |
| `CpuFamily.cs` | 168 B | 168 B |
| `CpuCodeName.cs` | 788 B | 788 B |
| `PowerLimits.cs` | 132 B | 132 B |
| `SmuStatus.cs` | 159 B | 159 B |

Only `RyzenSmuService.cs` (17914 → 49090 B), `CpuInfo.cs` (4893 → 22855 B) and the new
`FMaxCapability.cs` differ. So: **no attribution to namazso/PawnIO is required for the C# layer.**
Attribution *is* required for the embedded `LMechrevo.RyzenSMU.bin` module — that is PawnIO.Modules.

---

## 1. Inventory

### 1.1 Derived / reimplemented source — the SMU work

| # | Component | What we use it for | Where it landed in our tree (evidence) | Copy or reimplementation? |
|---|---|---|---|---|
| S1 | **irusanov/SMUDebugTool**<br>https://github.com/irusanov/SMUDebugTool<br>GPL-3.0 | Named by our own code as one of the two reference implementations the FMax feature was researched from. | `source\PawnIO\RyzenSmuService.cs:936` — `// 依据（用户给定的、已从参考实现 SMUDebugTool / ZenStates-Core 研究好的结论…）`. Also the retained reference set `diagnostics\smudbg-reference-20261005\src\SMUDebugTool-master\` (screenshot + icon PNGs + `Prebuilt\InpOut.LICENSE.txt`). | **Protocol / behaviour reference — no copied expression.** SMUDebugTool's own tree contains no SMU command table (its SMU engine is the `ZenStates-Core` submodule = S2), so nothing of its expression was copied; the in-UI list (`AboutLicensesForm`) therefore renders this row under *Protocol references (no code copied)*. No SMUDebugTool `.cs` source exists in this repo, so byte-identity could not be checked. |
| S2 | **irusanov/ZenStates-Core**<br>https://github.com/irusanov/ZenStates-Core<br>GPL-3.0 (submodule of SMUDebugTool) | (a) the **per-codename command tables** — the **FMax capability table** (Get/Set command IDs) **and the CO / iGPU-CO / OC-mode / power-limit / thermal tables** (all-core CO ids, per-core CO write + read-back, `EnableOcMode`/`DisableOcMode`/`SetOverclockFrequencyAllCores`, `SetStapmLimit`/`SetFastLimit`/`SetSlowLimit`/`SetTctlMax`) — the CO/OC/power/thermal part is inventoried in detail as **S7** and audited in `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md`; (b) the **RSMU mailbox SMN address groups**; (c) the 6-argument packing `arg0 = mhz & 0xFFFFF, arg1..5 = 0`; (d) the **transaction sequence** (clear RSP → write ARGs → write MSG → poll RSP → require `0x01`); (e) per-codename quirks (`RavenRidge` needs `arg0 = 3` before the read). | Table: `source\PawnIO\RyzenSmuService.cs:938-946` (comment) and `:987-1047` (`ResolveFMax`). Addresses: `:965,967,969` (APU `0x3B10A20/80/88`), `:972,974,976` (desktop `0x3B10524/70`, `0x3B10A40`), `:979,981,983` (ShimadaPeak `0x3B10924/70`). Command IDs: `:1003` (`0x42`/`0x47`), `:1014` (`0x6E`/`0x70`), `:1017` (`0x70` write-only), `:1024` (`0x6E` read-only), `:1031` (`0x42` read-only), `:1035` (`0x12`, `ReadArgument = 3`), `:1039` (`0x86`). Packing: `:1109-1112` `EncodeFMaxArg` → `mhz & 0xFFFFF`, used at `:1126`. Capability derivation: `source\PawnIO\FMaxCapability.cs:1-64` (its comment at `:29` explicitly says "与上游 ZenStates-Core 的 RSMU 邮箱是同一组"). ⚠️ The line numbers in this row are the 2026-10-10 snapshot and have drifted since (a co-attribution comment block was inserted above `CurveCommand`, and `GetFamily` was completed) — re-locate by symbol name; `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md` §5 has the current mapping. | **Derived table** (facts/IDs, re-expressed as a C# `switch`/record — not a verbatim file copy). Our own `RyzenSmuService.cs` even documents where it *deliberately deviates* from the reference (`:1000-1002` `KrackanPoint2`, `:1043-1044` `RavenRidge2`), which is consistent with reimplementation rather than copy-paste. **The reference `.cs` sources are not in this repo**, so a byte-diff against ZenStates-Core is impossible here — see §5 and `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md` §1. (A byte-exact comparison *is* available for the G-Helper SMU file — see S6.) |
| S3 | **leogx9r/ryzen_smu**<br>https://github.com/leogx9r/ryzen_smu<br>GPL-2.0 (its `LICENSE` is the GPLv2 text) | Community RSMU command tables; retained verbatim as a reference document. | `diagnostics\smudbg-reference-20261005\key-files\rsmu_commands.md` (e.g. `GetMaxFrequency 0x6E`, `SetPPTLimit 0x53`, the `SetOverclockFreqPerCore` value mask). | **Protocol reference.** The file appears to be ryzen_smu's `rsmu_commands.md`; I could not re-fetch the upstream copy to confirm byte-identity (§5). |
| S4 | **coreboot** (`0x…smu.h` headers) | APU SMU command IDs for Cezanne / Phoenix / Glinda. | `diagnostics\smudbg-reference-20261005\key-files\coreboot_cezanne_smu.h`, `coreboot_phoenix_smu.h`, `coreboot_glinda_smu.h`. coreboot is GPL-2.0. | Protocol reference, retained as-is under `diagnostics\` (not shipped). |
| S5 | **seerge/g-helper** (G-Helper)<br>https://github.com/seerge/g-helper<br>GPL-3.0 | The whole application contains code derived from G-Helper — this is stated by L-Mechrevo itself and is why L-Mechrevo is GPL-3.0-only. | `installer-src\files\THIRD_PARTY_NOTICES,1.txt:3-4` and `:22-24`. **Hard evidence:** `installer-src\files\LICENSE,1.txt` is **byte-identical** to G-Helper's `LICENSE` — git blob SHA-1 `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` (computed locally, matches `namazso`-style blob hash reported by the GitHub API for `seerge/g-helper@main:LICENSE`). Reference copy of one G-Helper SMU file: `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs`. | **Derived source** (pre-existing, already noticed). |
| S6 | **seerge/g-helper — `app/Pawn/RyzenSmu.cs`**<br>https://github.com/seerge/g-helper/blob/b2b7bae52cd39095002078af98349e6f6d57b960/app/Pawn/RyzenSmu.cs<br>GPL-3.0 | The **AMD SMU command layer as a whole** — not just the app shell: all-core Curve Optimizer (`SetCoAll`), iGPU curve (`SetCoGfx`), thermal limit (`SetThm`), the power-limit setters (`SetStapm`/`SetFast`/`SetSlow`), the PM-table read (`GetPowerLimits`/`GetTctlIndex`/`HasApuSlowField`), the mailbox transaction (`MailboxRaw`) and the SMN address groups. Audited command-by-command in `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md` §2. | `source\PawnIO\RyzenSmuService.cs` — CO `:288-314`, iGPU curve `:595-613`, thermal `:615-637`, power `:1017-1104`, PM table `:675-777`, transport `:1479-1573`, addresses `:1576-1655`. Same ids/channels/order as upstream `:160-175`, `:192-205`, `:207-223`, `:249-331`, `:379-432`, `:434-589`. Reference copy: `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs`. | **Derived table** (facts/IDs re-expressed as C# `switch`es — not a verbatim file copy). **Hard evidence:** the retained copy is **byte-identical** to upstream `seerge/g-helper@b2b7bae5:app/Pawn/RyzenSmu.cs` — 22 317 B, SHA-256 `07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35`, git blob SHA-1 `b063297f0ac394fce4a2aede49488a4120193377`. G-Helper's file in turn credits **RyzenAdj** and **UXTU** in its own comments (`:165-172`, `:197-201`, `:212-220`, `:295`, `:324`) — see S8. |
| S7 | **irusanov/ZenStates-Core** — CO / OC / power / thermal tables and command classes<br>https://github.com/irusanov/ZenStates-Core<br>GPL-3.0 | In addition to the FMax material listed under S2: the **per-core CO write** (`Mp1Smu.SMU_MSG_SetDldoPsmMargin = 0x35`, `SetPsmMarginSingleCore`, `Utils.MakePsmMarginArg`, `Cpu.MakeCoreMask`), the **CO read-back** table (`Rsmu.SMU_MSG_GetDldoPsmMargin` per codename: 0xE1/0xC3/0x2F/0xAF/0xD5/0xA3/0x7C), the **all-core CO ids** (`SetAllDldoPsmMargin` 0x55/0x4C/0x07/0x0B), the **OC-mode ids** (`EnableOcMode`/`DisableOcMode`/`SetOverclockFrequencyAllCores`), the **power-limit ids** (`SetStapmLimit`/`SetFastLimit`/`SetSlowLimit`) and `SetTctlMax`, plus every `SMU_ADDR_MSG/RSP/ARG` triple. | `source\PawnIO\RyzenSmuService.cs`: per-core `:171-190`, `:338-360`; read-back `:439-479`, `:1195-1249`; all-core `:288-314`; OC `:487-593`; power `:1017-1104`; thermal `:615-637`; addresses `:1576-1655`. Upstream: `Hardware/Smu/Settings/{APUSettings0,APUSettings1,APUSettings1_Cezanne,APUSettings1_Phoenix,APUSettings1_Rembrandt,APUSettings1_Strix,APUSettings1_VanGogh,Zen2Settings,Zen3Settings,Zen4Settings,Zen5Settings_ShimadaPeak,ZenSettings}.cs`, `Hardware/Smu/Commands/{SetPsmMarginSingleCore,GetPsmMarginSingleCore,SetPsmMarginAllCores,SetSmuLimit,SetTctlMax,SetOcMode}.cs`, `Utils.cs:180-184`, `Cpu.cs:932-955`. | **Derived table** (id-for-id transcription re-expressed as `switch`es, with documented deviations: our 20-bit CO packing vs upstream's 16-bit `MakePsmMarginArg`; the Vermeer all-core row using the upstream Zen2 `0x0B` `// Not sure` value; the per-core row's `ccd`/`core` coming from an 8-cores-per-CCD assumption instead of the APOB core map). The `.cs` sources are not vendored; the comparison against upstream at commit `869b7e82572337e5a673e92230edc92bddf0e44b` is recorded in `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md`. |
| S8 | **FlyGoat/RyzenAdj**<br>https://github.com/FlyGoat/RyzenAdj<br>**LGPL-3.0** | The values and channel-fallback order for: the iGPU curve (`set_cogfx`: Renoir `_do_adjust(0x64)`, Mobile `_do_adjust_psmu(0xB7)`, StrixHalo deliberately unsupported), the fixed-frequency OC sequence (`set_enable_oc` `0x2F` / `psmu 0x17`, `set_oc_clk` `0x31` → `psmu 0x19`, `set_disable_oc` `0x30` → `psmu 0x1d`), the APU power-limit and thermal tables (`set_stapm_limit`/`set_fast_limit`/`set_slow_limit`/`set_tctl_temp` with their "Retry with PSMU" branches) and the PM-table `get_tctl_temp`/`get_apu_slow_limit` offsets. | `source\PawnIO\RyzenSmuService.cs` — iGPU curve `:595-613`, OC `:487-593`, power `:1017-1104`, thermal `:615-637`; the G-Helper reference copy in this repo carries the explicit citations (`diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs:197-201` `// RyzenAdj: _do_adjust(0x64) — MP1 only`, `:212-220`, `:295` `// matching the switch in RyzenAdj api.c get_tctl_temp()`, `:324`). | **Values / channel-order reference.** The expression we actually hold came through G-Helper's GPL-3.0 file; the ids and the MP1→PSMU order are corroborated in RyzenAdj `lib/api.c:444-653`, `:1281-1467`, `:1478+`, `:1852+`. Kept in the *protocol reference* group in §1.3 today — ⚠️ if the maintainer reads the copied expression as RyzenAdj's, promote this to the derived group; LGPL-3.0 and our GPL-3.0-only are compatible either way (LGPL-3.0 §3). **Decision for the user** (`docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md` §8 b). |

### 1.2 Shipped third-party binaries / resources

| # | Component | How it enters the build | Evidence | Shipped? |
|---|---|---|---|---|
| B1 | **PawnIO.Modules — `RyzenSMU.bin`**<br>https://github.com/namazso/PawnIO.Modules<br>LGPL-2.1-or-later | Embedded as a managed resource via MSBuild. | `source\L-Mechrevo.csproj:22` (`<None Remove="LMechrevo.RyzenSMU.bin" />`) and `:25` (`<EmbeddedResource Include="LMechrevo.RyzenSMU.bin" LogicalName="LMechrevo.RyzenSMU.bin" />`). The module file is `source\LMechrevo.RyzenSMU.bin`, 38196 B, SHA-256 `B84ECA7F32C63B3D8C14B2C6D45482706DF8683AA6F43EB8BEAD9DC62181D38F`. The resource name is present in the shipped assembly `installer-src\files\L-Mechrevo.dll`. Its upstream source is retained at `diagnostics\smudbg-reference-20261005\key-files\RyzenSMU.p`, whose header carries the upstream copyright and `SPDX-License-Identifier: LGPL-2.1-or-later` (`RyzenSMU.p:1-15`). | **YES — embedded in `L-Mechrevo.dll`.** |
| B2 | **`Install-PawnIO.ps1`** (our own script, downloads namazso's **PawnIO driver**, GPL-2.0) | ⚠️ **Referenced by the app but MISSING from the repo and from the installer.** | Our code launches it: `source\MechrevoLite\CpuTuningForm.cs:1710` → `Path.Combine(AppContext.BaseDirectory, "Install-PawnIO.ps1")` (spawned at `:1698-1716` with `runas`). The file exists **only** in the previous release: `_upstream\0.290.6\extract\{app}\Install-PawnIO.ps1` (2339 B), which pins the download to `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`, verifies SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032` (`:5`, `:12`) and requires a valid Authenticode signature (`:18-19`). `Get-ChildItem -Recurse -Filter Install-PawnIO*` over `D:\openLM` finds it nowhere else; `installer-src\files\` contains no `*.ps1` at the app root; `installer-src\install_script.iss` never references it. | **NO** — and the "下载 PawnIO 驱动" button therefore currently starts PowerShell against a non-existent script. |
| B3 | **PawnIO kernel driver** (GPL-2.0) | Downloaded at the user's explicit request; **not** bundled. | Per B2, and per the previous release's notice (`_upstream…\THIRD_PARTY_NOTICES,1.txt:72,76-77`). Confirmed **not** present in `installer-src\files\`. | **NO** — good for compliance; no GPL-2.0 driver redistribution. |
| B4 | **`LMechrevo.IntelMSR.bin`** (PawnIO.Modules) | Embedded as a managed resource via MSBuild — **restored 2026-10-10** with the Intel tuning merge (⑦B). | `source\LMechrevo.IntelMSR.bin`, 5324 B, SHA-256 `D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F` — byte-identical to the author's copy at `_upstream\0.290.6\decomp\LMechrevo.IntelMSR.bin`. Declared at `source\L-Mechrevo.csproj` (`<None Remove="LMechrevo.IntelMSR.bin" />` + `<EmbeddedResource Include="LMechrevo.IntelMSR.bin" LogicalName="LMechrevo.IntelMSR.bin" />`); the resource name is present in the built assembly (verified by enumerating `Assembly.GetManifestResourceNames()`). Consumed by `source\PawnIO\IntelOcMailbox.cs` (via `RuntimeCpuTuning.Probe`'s Intel branch). | **YES — embedded in `L-Mechrevo.dll`.** Same upstream and the same LGPL-2.1-or-later licence text as B1 (`LICENSES\LGPL-2.1.txt`); the in-app 关于/开源许可 window lists it under *shipped binaries* as `PawnIO.Modules IntelMSR.bin` (`source\MechrevoLite\AboutLicensesForm.cs`). |
| B5 | **AISTONE / Mechrevo GCU service binaries + a Microsoft-signed UWACPI driver** | Shipped in the GCU-only bundle under `installer-src\files\GCU\`. | `installer-src\files\GCU\Install-Gcu.ps1`, `Select-GcuPayload.ps1`, `Uninstall-Gcu.ps1`, `GCU\payload\…\AiStoneService\…` (incl. `MyControlCenter\License\protocol_{zh,en}.{html,rtf}`), referenced throughout `installer-src\install_script.iss` (`:65-67`, `:114-123`, `:537-541`). | **YES** — proprietary third-party works, already covered by the GCU paragraph of the shipped notice. |
| B6 | **Managed DLLs shipped beside the app** | Copied from `lm_pkg_files\{app}\` and referenced by `source\L-Mechrevo.csproj` (`<Reference Include=…><HintPath>..\lm_pkg_files\{app}\…`). | `installer-src\files\`: `LibreHardwareMonitorLib.dll`, `Newtonsoft.Json.dll`, `MQTTnet.dll`, `NvAPIWrapper.dll`, `RAMSPDToolkit-NDD.dll`, `HidSharp.dll`, `Microsoft.Win32.TaskScheduler.dll`, `Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`, `System.Management.dll`, `DiskInfoToolkit.dll`, `BlackSharp.Core.dll`, `Probe.dll`. | **YES** — not SMU-related; licences are **not** collected in this pass. See §5. |
| B7 | **usbmmidd_v2 virtual display driver** (Amyuni Technologies Inc., © 2014-2021) | Shipped beside the app under `<app>\usbmmidd_v2\` for the **screen-blank** feature (`source\MechrevoLite.Display\VirtualDisplayBlank.cs`): mounts the virtual display via `deviceinstaller64.exe install usbmmidd.inf usbmmidd` / `enableidd 1`, then `displayswitch.exe /external` force-switches the video signal for true blank-without-sleep. Copied verbatim from `D:\Downloads\NullSignal_win_amd64\usbmmidd_v2\` (9 files: `deviceinstaller.exe` / `deviceinstaller64.exe`, `usbmmIdd.inf` / `usbmmidd.cat`, `Win32\usbmmIdd.dll`, `x64\usbmmIdd.dll`, `usbmmidd.bat`, `idd_instructions.txt`, `License.txt`; 9 files / 458,770 B — re-measured on disk 2026-10-12). | Installed copy verified 2026-10-11 at `C:\Program Files\L-Mechrevo\usbmmidd_v2` and shipped in `installer-src\files\usbmmidd_v2` (9 files / 458,770 B). | **YES** — third-party proprietary driver, **not** MIT. Distributed under the Amyuni licence in `usbmmidd_v2\License.txt`, whose clause 2 requires an acknowledgment in the product documentation — done in the in-app 关于/开源许可 window (`AboutLicensesForm`, Shipped group) and here. |

### 1.3 Protocol references named in the previous release's notice (no code here)

These were listed by `_upstream\0.290.6\extract\{app}\THIRD_PARTY_NOTICES,1.txt:73-79`. None of them
has source in this repo; they are credited as protocol references only.

| Component | URL | Licence | Note |
|---|---|---|---|
| kitsunyan/intel-undervolt | https://github.com/kitsunyan/intel-undervolt | GPL-3.0 | Intel OC mailbox (MSR 0x150) protocol reference for the Intel tuning path added by the 2026-10-10 merge (⑦B). **No code copied** — our `source\PawnIO\IntelOcMailbox.cs` / `IntelOcDomain.cs` are the author's reimplementation (via the upstream L-Mechrevo 0.290.6 tree), expressed as a C# class over `PawnIOWrapper.Execute`. The credit stays in the *protocol reference* group both in this file and in the in-app window. |
| FlyGoat/RyzenAdj | https://github.com/FlyGoat/RyzenAdj | **LGPL-3.0** (its `LICENSE` is the LGPLv3 text; GitHub reports `lgpl-3.0`) | AMD SMU command reference. Note the licence was **never stated** in the shipped notice. |
| JamesCJ60/Universal-x86-Tuning-Utility | https://github.com/JamesCJ60/Universal-x86-Tuning-Utility | GPL-3.0 | Additional SMU route reference. |
| torvalds/linux `bitland-mifs-wmi.c` | https://github.com/torvalds/linux/blob/master/drivers/platform/x86/bitland-mifs-wmi.c | GPL-2.0 | Legacy MICommonInterface protocol reference. |
| tomups/watercooler-manager | https://github.com/tomups/watercooler-manager | GPL-3.0 | BLE liquid-cooler GATT layout. The shipped notice prints a **broken URL** (`https://github.com/` — no repo path). |
| BetterRGB ("Copyright (c) 2026 TheYamo", MIT per the notice) | — | MIT per our own notice | ⚠️ **Unverifiable.** A GitHub search for `BetterRGB in:name` returns `ZavierChen/BetterRGB-ITE8291-Adaptive` (MIT), `imsudip/betterrgb` (no licence) and `Erdletheterdle/BetterRGB-Profiles` (no licence) — **no repository owned by "TheYamo"**. The MIT text is reproduced in our notice, so the obligation is met if the claim is accurate; the URL/provenance needs a human to confirm. |
| IYATT-yx/NullSignal | https://github.com/IYATT-yx/NullSignal | MIT | 2026-10-11 (screen-blank rework): the virtual-display blank **approach/flow** (usbmmidd driver + `displayswitch /external` + mouse-move restore) was re-implemented in `source\MechrevoLite.Display\VirtualDisplayBlank.cs` — reference only, **no Python code copied**. Listed in the in-app window under *References*. |
| Zcodeoooo/JiaoLong16Pro-Battery-threshold | https://github.com/Zcodeoooo/JiaoLong16Pro-Battery-threshold | No LICENSE file in the repo | 2026-10-18 charge threshold: EC register semantics (`0x83A1`/`0x83A2`/`0x83C0`, mailbox `0x8150`-`0x8154`) and enable/disable sequence. **Protocol facts only, no code copied.** |
| GaoXanSheng/JiaolongControl (`windows/X86-64`) | https://github.com/GaoXanSheng/JiaolongControl | **MIT** - Copyright (C) 2025 GaoXanSheng (full text in `LICENSES\MIT-JiaolongControl.txt`) | 2026-10 authoritative source for the Baolongda protocol facts (WMI `MiInterface` call shape, function numbers 8-23, EC channel). Internal notes quote code fragments; **the MIT notice ships with the package**. Product code contains no copied implementation. |
| KanzakiK/DOUZHANZHE-Control | https://github.com/KanzakiK/DOUZHANZHE-Control | GPL-3.0 (as shown on GitHub; no licence file obtained locally) | 2026-10 engineering practice and NVAPI/EC fact cross-check. **Facts/approach only, no code copied.** |

---

## 2. Licences and what each one obliges

### 2.1 Licence table

| Component | Licence (SPDX) | Where the licence text lives upstream | Key clause that applies to us |
|---|---|---|---|
| G-Helper | **GPL-3.0** (GitHub `gpl-3.0`) | https://github.com/seerge/g-helper/blob/main/LICENSE | GPLv3 §5a-c: mark that we modified it, keep it under GPLv3, license the whole work under GPLv3. §6: convey Corresponding Source. §5d/§0: interactive UIs must show "Appropriate Legal Notices". |
| seerge/g-helper — `app/Pawn/RyzenSmu.cs` (the AMD SMU command layer, **S6**) | **GPL-3.0** (same canonical `LICENSE` blob `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` as G-Helper itself) | https://github.com/seerge/g-helper/blob/main/app/Pawn/RyzenSmu.cs | GPLv3 §5a/§5b: state that the material is derived and carry the notices (done in `RyzenSmuService.cs`'s header + the co-attribution block, and in the in-app 关于/开源许可 window). §5c/§6: the whole covered work is conveyed under GPL-3.0-only — already the case for L-Mechrevo, so no new reach. |
| irusanov/SMUDebugTool | **GPL-3.0** (`gpl-3.0` via GitHub API) | https://github.com/irusanov/SMUDebugTool/blob/master/LICENSE.md | Same as GPLv3 above. The file is named `LICENSE.md`, not `LICENSE`. Its blob SHA-1 is `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` — **the same canonical GPL-3.0 blob as G-Helper's `LICENSE` and as our own `installer-src\files\LICENSE,1.txt`**, i.e. all three are byte-identical. `ZenStates-Core` is confirmed to be a git submodule of this repo (`.gitmodules` + a gitlink entry at `master`). |
| irusanov/ZenStates-Core | **GPL-3.0** (`gpl-3.0`; `LICENSE` = the GPLv3 text, fetched OK) | https://github.com/irusanov/ZenStates-Core/blob/master/LICENSE | Same. Note: the `LICENSE` file is the plain GPLv3 text — whether it is "only" or "or later" depends on the per-file headers; treat as GPL-3.0. |
| leogx9r/ryzen_smu | **GPL-2.0** (`LICENSE` = GPLv2 text) | https://github.com/leogx9r/ryzen_smu/blob/master/LICENSE | GPLv2 §2/§3 if derived. GPLv2-only is **incompatible** with GPLv3-only — see the risk note in §2.3. |
| namazso/PawnIO (driver) | **GPL-2.0** (`COPYING` = GPLv2 text, 18092 B) | https://github.com/namazso/PawnIO/blob/master/COPYING | Only if we convey the driver. **We do not** (B3) — the user downloads it from upstream. No obligation. |
| namazso/PawnIO.Modules | **LGPL-2.1-or-later** (`COPYING` = LGPL-2.1 text, 26530 B; module sources carry `SPDX-License-Identifier: LGPL-2.1-or-later`) | https://github.com/namazso/PawnIO.Modules/blob/main/COPYING<br>sources: https://github.com/namazso/PawnIO.Modules/tree/main | This is the one that bites: we **convey the module in binary form** (B1). LGPL-2.1 §6 requires (a) prominent notice that the library is used and covered by this licence, (b) a copy of the licence, (c) the corresponding source of the library + our application code in a form that lets the user relink — or (d) the "shared library mechanism" alternative, which **is not available to us** because the module is embedded as a resource, and (e) a written offer. Because the module is a byte-for-byte upstream artifact and LM is GPL-3.0, LGPL-2.1 §3 ("opt to apply the ordinary GPL instead") is also open to us. |
| FlyGoat/RyzenAdj | **LGPL-3.0** (`lgpl-3.0`) | https://github.com/FlyGoat/RyzenAdj/blob/master/LICENSE | Only if derived. Used as a protocol/values reference today: the iGPU-curve values, the fixed-clock values and the MP1→PSMU fallback order we send came through G-Helper's file, which credits RyzenAdj (**S8**). ⚠️ Promote to "derived" if the maintainer reads the copied expression as RyzenAdj's — LGPL-3.0 and GPL-3.0-only are compatible either way (LGPL-3.0 §3); see `docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md` §8 b. |
| kitsunyan/intel-undervolt | **GPL-3.0** (`gpl-3.0`) | https://github.com/kitsunyan/intel-undervolt | Protocol reference only. |
| JamesCJ60/Universal-x86-Tuning-Utility | **GPL-3.0** (`gpl-3.0`) | https://github.com/JamesCJ60/Universal-x86-Tuning-Utility | Protocol reference only. |
| tomups/watercooler-manager | **GPL-3.0** (`gpl-3.0`) | https://github.com/tomups/watercooler-manager | Protocol reference only. |
| coreboot headers | **GPL-2.0** | https://github.com/coreboot/coreboot/blob/master/COPYING | Reference files under `diagnostics\` (not shipped). |
| coreboot / Linux `bitland-mifs-wmi.c` | **GPL-2.0** | https://github.com/torvalds/linux/blob/master/COPYING | Protocol reference only. |

### 2.2 Obligations that follow, plainly

Because L-Mechrevo is itself open-source and distributed under **GPL-3.0-only**, every GPL-family
upstream above is **compatible**. There is no licensing blocker — only a to-do list.

1. **Attribution / NOTICE.** GPLv3 §5a/§5b and LGPL-2.1 §6(a)/(c) both require prominent notices and
   the licence texts. → this file, `LICENSES\`, and the end-user notice inside the installer.
2. **Source disclosure of the whole covered work.** GPLv3 §5c/§6: whoever receives the binary must be
   able to obtain the **Corresponding Source**, including our modifications. → **currently unmet**,
   see §3.
3. **Copy of the licences travels with the binary.** GPLv3 §4/§6, LGPL-2.1 §6(b). → `LICENSE.txt`
   ships; the LGPL text for the embedded module **does not** (§4).
4. **Copyleft reach into the app.** Because we already ship GPL-3.0-only and derive from GPL-3.0
   G-Helper, the app as a whole is GPL-3.0-only anyway. The new GPL-3.0 derived material (S1/S2)
   adds no new reach. → *No change to the distribution model is required.*
5. **Redistribution of the driver/module binary.** The **module** is embedded and conveyed → LGPL-2.1
   duties apply (B1/§2.1). The **driver** is not conveyed → no duty (B3). The **GCU** binaries are
   proprietary, already flagged in the notice as "only redistribute with permission".
6. **"Appropriate Legal Notices" in the UI.** GPLv3 §0/§5d: an interactive UI must display a copyright
   notice, the no-warranty statement, and how to view the licence. → currently the only in-app surface
   is the Start-Menu shortcut `开源许可 (GPL-3.0)` → `{app}\LICENSE.txt`
   (`installer-src\install_script.iss:547`), i.e. **outside** the app. The 关于/致谢/开源许可 surface
   in `docs/archive/attribution-20261010/ATTRIBUTION-PATCH-PLAN.md` closes this.

### 2.3 Risk notes (stated as what the licences say, not as legal advice)

* **GPL-2.0-only vs GPL-3.0-only.** `leogx9r/ryzen_smu` and `namazso/PawnIO` are GPL-2.0. If any
  *expression* from them were ever merged into our GPL-3.0-only source, the two licences cannot both
  be satisfied (GPLv2-only and GPLv3-only are mutually incompatible in the direction GPLv2 → GPLv3).
  Today we treat both as **protocol references with no copied expression**, which keeps this clean.
  **Keep it that way**: if anyone later pastes ryzen_smu or PawnIO driver code into our tree, that is a
  decision for the maintainer, not a mechanical edit. (The GPL-2.0 coreboot headers under
  `diagnostics\` are reference-only and must **not** be copied into the app either.)
* **LGPL-2.1 on the embedded module.** Embedding a binary as a managed resource is *static* inclusion.
  LGPL-2.1 §6 gives four routes; the two realistic ones for us are (a) ship the module's corresponding
  source + our app source so the user can rebuild/relink, or (c) rely on LGPL-2.1 §3 and relicense the
  copied module under the ordinary GPLv2 — which is itself awkward because our tree is GPLv3-only.
  **Do not guess.** The safe, minimal, zero-risk option is: ship the LGPL text, state prominently that
  the module is LGPL-2.1-or-later and unmodified, and give the exact upstream source URL +
  version/tag matching the shipped binary — which is what `LICENSES\` + the notice now do. The
  residual question ("should we instead vendor the module as a separate file, or move to the driver's
  own module store?") is a **decision for the user**.
* **Two upstreams share one licence text.** GPL-3.0 covers G-Helper, ZenStates-Core, SMUDebugTool,
  intel-undervolt, UXTU and watercooler-manager; one `LICENSES\GPL-3.0.txt` satisfies all of them.
* **RyzenAdj is LGPL-3.0, not LGPL-2.1.** The previous notice never said. LGPL-3.0 is compatible with
  GPLv3, so no problem — but the credit should state the right licence.
* **Broken URL in a shipped notice.** `installer-src\files\THIRD_PARTY_NOTICES,1.txt:29` says the
  watercooler-manager project is at `https://github.com/` — a URL that identifies nothing. Fix to
  `https://github.com/tomups/watercooler-manager`.

---

## 3. ⚠️ The one real, currently-unmet obligation: Corresponding Source

LM is distributed as a binary (`L-Mechrevo.dll` / `L-Mechrevo.exe` inside
`LM-<version>-setup.exe`) while the source tree has **not** been published. The shipped notice
promises a GPLv3 §6(b) written offer (`installer-src\files\THIRD_PARTY_NOTICES,1.txt:1-17`), which is
a legitimate route — but note what it commits us to:

| Licence | What it requires of a binary distributor | Is it met today? |
|---|---|---|
| GPL-3.0 (G-Helper, ZenStates-Core, SMUDebugTool, UXTU, intel-undervolt, watercooler-manager) | §6(b): accompany the object code with a **written offer, valid ≥3 years**, to give anyone the Corresponding Source of the covered work (which, for a GPLv3-only work, is *all* of it incl. our modifications) | **Partly.** The offer text exists in the shipped notice and is valid in form; but it names an email address only, and the source is not yet obtainable. |
| LGPL-2.1 (PawnIO.Modules `RyzenSMU.bin`) | §6(b) or §6(a): the module's Complete Corresponding Source, **plus** the application code needed to relink; §6(b) prominent notice + a copy of the licence | **No.** The licence text is not shipped at all (§4). |
| GPL-2.0 (coreboot/linux references; PawnIO driver not conveyed) | nothing, since nothing is conveyed/derived | n/a |

**Concrete options (pick one; all are compatible with staying closed-source-in-the-repo until launch):**

1. **Publish the repo** (or a tarball per release) at a stable public URL, and add a "Corresponding
   Source" line to the notice + the in-app 关于 surface pointing at it. GPLv3 §6(d) — cleanest.
2. **Ship a source archive alongside each release** (`LM-<version>-source.zip`, which the repo already
   produces in places — e.g. `archive\L-Mechrevo-0.289.6-beta18.6-source.zip`,
   `交付\LM-源码-提交作者-20260910\`), and say so in the notice.
3. **Keep the written offer** that is already in the notice, and make sure the named contact can
   actually deliver a complete, buildable tree for the exact version (GPLv3 §6(b) covers ≥3 years
   *and* for as long as spare parts/support are offered).

Whichever route is chosen, the offer must name the **exact shipped version** and cover **all** GPL
components — including the GPL-3.0 material newly derived for FMax.

**Not legal advice.** The above is what the licences say, read against this repo's actual artifacts.
The publish/ship-source decision is the user's.

---

## 4. ⚠️ Regression found in the current packaging

The previous release shipped more attribution than the working tree does. Three concrete deletions:

| Item | Previous release `0.290.6` | Current `installer-src\` | Consequence |
|---|---|---|---|
| PawnIO.Modules / RyzenSMU.bin / LGPL-2.1 notice block | present — `_upstream\0.290.6\extract\{app}\THIRD_PARTY_NOTICES,1.txt:66-77` (4521 B file) | **deleted** — `installer-src\files\THIRD_PARTY_NOTICES,1.txt` is 3434 B; a line-by-line compare shows all 18 lines about PawnIO.Modules, IntelMSR, intel-undervolt, RyzenAdj, UXTU, PawnIO 2.2.0 and bitland-mifs-wmi are gone | We ship an LGPL-2.1 binary with **no notice and no licence text**. |
| `COPYING.PawnIO.txt` (LGPL-2.1 text) | shipped — `_upstream\0.290.6\extract\install_script.iss:32` | **not shipped** — file absent from `installer-src\files\`, no reference in `installer-src\install_script.iss` | LGPL-2.1 §6(b) violation. |
| `Install-PawnIO.ps1` | shipped — `_upstream\0.290.6\extract\install_script.iss:36` | **not shipped, and not in the tree** — but `source\MechrevoLite\CpuTuningForm.cs:1710` still calls it | The CPU-tuning "install driver" button silently fails; also the SHA-256 + Authenticode pinning that made the download safe is gone. |

The good news is that this is a **restore**, not new work: the exact upstream license text is still
available in the previous release's payload, and it is byte-identical to upstream (see §5).

---

## 5. `LICENSES\` layout (created) and what must still be added

```
LICENSES\GPL-3.0.txt       35149 B  SHA-256 3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986
LICENSES\LGPL-2.1.txt      26530 B  SHA-256 DC626520DCD53A22F727AF3EE42C770E56C97A64FE3ADB063799D8AB032FE551
LICENSES\README.md
```

Provenance and **byte-identity proof** (git blob SHA-1 = `sha1("blob <len>\0" + content)`, computed
locally and compared against the value the GitHub API reports for the upstream blob):

| Created file | Copied from | Local git blob SHA-1 | Upstream blob SHA-1 | Match |
|---|---|---|---|---|
| `LICENSES\LGPL-2.1.txt` | `_upstream\0.290.6\extract\{app}\COPYING.PawnIO.txt` | `4362b49151d7b34ef83b3067a8f9c9f877d72a0e` | `namazso/PawnIO.Modules@main:COPYING` = `4362b49151d7b34ef83b3067a8f9c9f877d72a0e` | ✅ **byte-identical** |
| `LICENSES\GPL-3.0.txt` | `installer-src\files\LICENSE,1.txt` | `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` | `seerge/g-helper@main:LICENSE` = `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` | ✅ **byte-identical** |

Still to add (see `docs/archive/attribution-20261010/ATTRIBUTION-PATCH-PLAN.md` §6 for the exact commands, and note these are only
needed if the corresponding code/binary is actually conveyed):

| File to add | Needed when | Source |
|---|---|---|
| `LGPL-3.0.txt` | if RyzenAdj code is ever copied in | `https://raw.githubusercontent.com/FlyGoat/RyzenAdj/master/LICENSE` |
| `GPL-2.0.txt` | if ryzen_smu / coreboot / PawnIO-driver code is ever copied in | `https://raw.githubusercontent.com/namazso/PawnIO/master/COPYING` |
| `RyzenSMU-module-source/` (or a note with a URL + tag) | always, for the embedded module (LGPL-2.1 §6(a) — "corresponding source of the library") | `https://github.com/namazso/PawnIO.Modules` at the exact tag matching the shipped `LMechrevo.RyzenSMU.bin` |
| `NOTICE-nuget.md` | always, for B6 | Collect from the NuGet/upstream metadata of each shipped DLL |

### Must ship with the binary — the short list

**Must** be in `{app}\` for a compliant release:

1. `LICENSE.txt` — GPL-3.0 (LM's own; already shipped, `install_script.iss:43`/`:61`).
2. `THIRD_PARTY_NOTICES.txt` — the merged notice (already shipped; **must be extended** with the
   PawnIO.Modules block, and the watercooler-manager URL fixed).
3. **`COPYING.PawnIO.txt`** (or `LICENSES\LGPL-2.1.txt`) — **currently missing; the single most
   important fix**, because `RyzenSMU.bin` is embedded and conveyed.
4. `Install-PawnIO.ps1` — not a licence item, but the app calls it; restore it and add it to the
   installer (and re-check its pinned SHA-256 against the current upstream release).

---

## 6. Verification gaps (flagging honestly)

These I could **not** confirm from inside this repo; each is a cheap check for the next pass.

1. **Is the embedded module really "unmodified PawnIO.Modules 0.2.11"?** The previous notice claimed
   0.2.11 with release-ZIP SHA-256 `43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4`.
   The working tree's `LMechrevo.RyzenSMU.bin` is **38196 B / SHA-256 `B84ECA7F…`**, whereas `0.290.6`
   shipped a **50764 B / SHA-256 `301D9CA3…`** module — *different files*. Both export the same eight
   `ioctl_*` entry points, so both are RyzenSMU modules, but the **version claim in the notice must be
   re-derived** from the file we actually embed before publishing it. Do not copy "0.2.11" forward on
   faith.
2. ~~**Byte-diff against ZenStates-Core is impossible here.**~~ **Partially closed on 2026-10-10 by the
   co-attribution audit** (`docs/archive/attribution-20261010/ATTRIBUTION-CO-ADDENDUM.md`): upstream `ZenStates-Core@869b7e82`,
   `g-helper@b2b7bae5`, `RyzenAdj@5775fc3e` and `SMUDebugTool@3c1cd0f4` were fetched, and **69/69 files
   were byte-verified** against the blob SHA-1 the GitHub tree API reports for the same commit
   (0 mismatches) — so the id/packing/order comparison in that addendum §2 is made against verified
   upstream bytes. Two gaps remain: (i) ZenStates-Core's `.cs` is still not vendored in this repo, so
   our file cannot be *diffed* token-by-token — the verdict "derived table" rests on the
   id/channel/packing/order identity; (ii) the G-Helper SMU file **is** available byte-exactly (**S6**,
   SHA-256 `07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35`), so that one
   comparison is exact. To go further: diff our `CurveCommand`/`ClockCommand` blocks against the
   retained `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs` line by line.
3. **`rsmu_commands.md` provenance.** It matches ryzen_smu's document by title and content, but the
   raw upstream fetch failed (network error) so byte-identity was not proven.
4. **`BetterRGB` / "TheYamo".** No such GitHub repository was found — see §1.3.
5. **B6 DLL licences** were not collected in this pass (out of scope: none are SMU-related). The most
   consequential one is `LibreHardwareMonitorLib.dll` (MPL-2.0 upstream) — file-level copyleft, so if
   we ever patch it we owe the modified file's source. **Not verified here.**

---

## 7. Files affected by this inventory

Created (this pass):

* `THIRD-PARTY-NOTICES.md` (this file)
* `LICENSES\GPL-3.0.txt`, `LICENSES\LGPL-2.1.txt`, `LICENSES\README.md`
* `docs/archive/attribution-20261010/ATTRIBUTION-PATCH-PLAN.md`
* `LICENSE` (repo root) — did not exist before; created as a 546-byte provenance banner followed by the
  35149-byte canonical GPL-3.0 text copied byte-exactly from `LICENSES\GPL-3.0.txt` (verified by a
  byte-for-byte comparison of the appended region).

To be edited (see `docs/archive/attribution-20261010/ATTRIBUTION-PATCH-PLAN.md` for exact anchors; nothing below was touched):

* `source\PawnIO\RyzenSmuService.cs`, `source\PawnIO\FMaxCapability.cs`, `source\PawnIO\CpuInfo.cs`
* `source\MechrevoLite\SettingsForm.cs`, `source\MechrevoLite\SettingsDialog.cs`, new `AboutForm`-style form
* `source\MechrevoLite.Properties.Strings.resx`, `Strings.zh-CN.resx`
* `installer-src\files\THIRD_PARTY_NOTICES,1.txt` / `,2.txt`, `installer-src\install_script.iss`
* repo root `LICENSE` (does not exist yet — LM has no top-level licence file)

---

## InpOut32/64（Highresolution Enterprises）— 随包组件

- 组件：`inpoutx64.dll`（用户态，v1.5.0.0）、`inpoutx64.sys`（内核驱动，v1.2 x64，已签名）、`Install-InpOut.ps1`
- 上游：https://www.highrez.co.uk/Downloads/InpOut32
- 版权：Copyright (c) 2003-2015 Phil Gibbons <www.highrez.co.uk>；Portions Copyright (c) 2000 logix4u.net
- 许可：MIT 式许可（允许使用/复制/修改/合并/发布/分发/再许可/销售，须保留版权声明与许可文本）
- 完整许可文本：`LICENSES\LICENSE-InpOut32.txt`
- 用途：无界（WUJIE14XA）风扇 I2EC 写入与电池电压修复的端口 I/O；驱动仅在无界机型安装
