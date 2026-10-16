# ATTRIBUTION-CO-ADDENDUM — Curve Optimizer / 负压 (CO) · OC · power · thermal

**Derivation audit + attribution addendum. Companion to `THIRD-PARTY-NOTICES.md` and
`ATTRIBUTION-PATCH-PLAN.md`.**

* Created: 2026-10-10 (this pass). This file is the **only** file this pass wrote.
* Scope: the AMD SMU code that makes **Curve Optimizer (CO / 负压)**, the **iGPU curve**, the
  **fixed-frequency / OC** path and the **power / thermal** setters, i.e. everything the user's
  instruction covers: *「那个负压的代码我是让另一个对话参考SMU 如果很像的话那就是引用了 也要加」*
  ("the CO code was written in another session by referencing the SMU project(s); if it is
  substantially similar then it is referenced — attribute it too").
* **Nothing under `source\` was edited by this pass** — the CO/OC/power paths are owned by another
  agent that is applying `ATTRIBUTION-PATCH-PLAN.md` right now. Every code change below is a
  **proposal with an exact anchor**, not an applied edit. See §7 for the conflicts this audit found
  with the patch that is already in the tree.

---

## 0. The answer in one line

**Yes — the CO code must be attributed, and not only to ZenStates-Core: the *whole* AMD SMU command
layer that carries CO (all-core `0x55/0x4C/0x07/0x0B`, per-core `MP1 0x35`, read-back
`0xE1/0xC3/0x2F/0xAF/0xD5/0xA3/0x7C`), iGPU-CO (`0x64/0xB7`), fixed-frequency OC
(`0x24/0x26/0x25`, `0x5D/0x5F/0x5E`, `0x2F/0x31/0x30`, `0x17/0x19/0x18`), power limits
(`0x14/0x15/0x16`, `0x4F/0x3E/0x5F`, `0x1A/0x1B/0x1C`) and thermal (`0x19/0x1F/0x3E/0x3F`) is
**structurally identical to `seerge/g-helper`'s `app/Pawn/RyzenSmu.cs`** (GPL-3.0) — a file whose
byte-identical copy the repo already keeps at
`diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs` — with the per-core CO
write/read rows additionally **identical to `irusanov/ZenStates-Core`** (GPL-3.0) and the
iGPU-CO / fixed-clock values and channel-fallback order **traceable to `FlyGoat/RyzenAdj`**
(LGPL-3.0, credited in G-Helper's own comments as `_do_adjust(0x64)` / `_do_adjust_psmu(0xB7)` /
`set_oc_clk` PSMU retry), plus one row (`UXTU`'s `set-cogfx 0xB7`) that G-Helper names and RyzenAdj
confirms.**

Consequences: (a) `THIRD-PARTY-NOTICES.md`'s derived-source inventory must gain the
G-Helper-SMU-layer rows and must stop scoping ZenStates-Core to "FMax only"; (b) the in-code notice
that the other agent has **already applied** at `source\PawnIO\RyzenSmuService.cs:7-8`
("…沿用 L-Mechrevo 自己的 0.290.6 实现，**非第三方代码**") is **wrong for CO/Stapm/Fast/Slow/Thm/OC**
and must be corrected; (c) the in-UI 关于/开源许可 list needs **one extra row** (G-Helper AMD SMU
layer) plus re-wording of two existing strings (§6).

---

## 1. Method and evidence (so the claims below can be re-checked)

1. **Our side** was read from the working tree (read-only):
   `source\PawnIO\RyzenSmuService.cs` (66 787 B / 1 555 lines at the time of this snapshot),
   `source\PawnIO\CpuInfo.cs`, `source\MechrevoLite.Hardware\RuntimeCpuTuning.cs`,
   `source\MechrevoLite\CpuTuningForm.cs`, `source\MechrevoLite\SmuCoRoundtrip.cs`,
   `source\MechrevoLite.Mode\ModeControl.cs`, plus our own previous release
   `_upstream\0.290.6\decomp\PawnIO\RyzenSmuService.cs` (17 914 B).
   **⚠️ `RyzenSmuService.cs` is being edited concurrently** (its line count went 1 531 → 1 555 while
   this audit ran). Every anchor below is given as **text** so it survives the drift; the line
   numbers are the 2026-10-10 snapshot.
2. **Upstream side** was fetched from GitHub **raw** and **verified byte-for-byte** against the
   blob SHA-1 the GitHub tree API reports for the same commit
   (`sha1("blob <len>\0" + content)` computed locally; 69/69 files matched, 0 mismatches).
   Pinned commits used:
   * `irusanov/ZenStates-Core` @ `869b7e82572337e5a673e92230edc92bddf0e44b` (master, 2026-10-05)
   * `seerge/g-helper` @ `b2b7bae52cd39095002078af98349e6f6d57b960` (main, 2026-10-08)
   * `FlyGoat/RyzenAdj` @ `5775fc3e6dbb25c7030ee2d100a1bdd6e8bf2d0a` (master, 2026-05-13)
   * `irusanov/SMUDebugTool` @ `3c1cd0f4ffeb69e245c1b86a580d163f6092afc9` (master, 2026-10-02)
   Local copies used for the diff live only in `%TEMP%\co-audit\` (not in the repo).
3. **The decisive artefact**: `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs`
   (22 317 B) is **byte-identical** (SHA-256 `07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35`)
   to upstream `seerge/g-helper@app/Pawn/RyzenSmu.cs` (git blob SHA-1
   `b063297f0ac394fce4a2aede49488a4120193377`, size 22 317 B). So the repo holds the **exact
   upstream file** our SMU layer was written from — this is the strongest available form of
   "no vendored `.cs` locally, therefore compare structurally" evidence. `THIRD-PARTY-NOTICES.md`
   S5 calls it "Reference copy of one G-Helper SMU file", which this now **proves**.
4. Every verdict token is one of the five the task fixed:
   `identical (id+packing+order)` · `same id, different packing` · `similar but rewritten` ·
   `different` · `not found upstream`.

### 1.0 Case map (who is who)

| Our label | Upstream label | Evidence of identity |
|---|---|---|
| `SendPsmu` / `GetPsmuAddrs` | ZenStates-Core **`Rsmu`** mailbox | APU group `0x3B10A20/0x3B10A80/0x3B10A88` (`APUSettings1.cs:11-13`), desktop group `0x03B10524/0x03B10570/0x3B10A40` (`Zen2Settings.cs:11-13`, `Zen4Settings.cs:11-13`) = our `GetPsmuAddrs` |
| `SendMp1` / `GetMp1Addrs` | ZenStates-Core **`Mp1Smu`** mailbox | `0x3B10530/0x3B1057C/0x3B109C4` (`Zen2Settings.cs:45-47`) = our Matisse/Raphael row; `0x03B10528/0x03B10564/0x03B10998` (`APUSettings1.cs:64-66`) = our Raven/Renoir row; `0x03B10928/0x03B10978/0x03B10998` (`APUSettings1_Strix.cs:14-16`) = our Strix row |

---

## 2. Per-command verdict table

Legend: **ours** = `source\PawnIO\RyzenSmuService.cs` (snapshot line numbers); **upstream** = the file
named, pinned to the commit in §1.2. All upstream URLs are
`https://github.com/<repo>/blob/<commit>/<path>#L<line>`.

### 2.1 CO write — all-core (`SetCoAll` + `CurveCommand`)

| # | ours (id · channel · packing · order) | upstream counterpart | verdict | likely source file |
|---|---|---|---|---|
| 1 | `CurveCommand` :181-207, `SetCoAll` :111-124, `EncodeCurve` :901 `return (uint)(steps & 0xFFFFF);`<br>Renoir/Cezanne/Lucienne → **MP1 0x55** | RyzenAdj `set_coall` `FAM_CEZANNE/FAM_RENOIR/FAM_LUCIENNE → _do_adjust(0x55)` (api.c:1392-1395); ZenStates `Mp1Smu.SMU_MSG_SetAllDldoPsmMargin = 0x55` (APUSettings1.cs:106); G-Helper `CpuFamily.Renoir => SendMp1(0x55, v)` (RyzenSmu.cs:166) | **identical (id+channel+order)**; **same id, different packing** vs ZenStates/RyzenAdj (16-bit `Utils.MakePsmMarginArg` vs our 20-bit `& 0xFFFFF`) | G-Helper `app/Pawn/RyzenSmu.cs`; ZenStates-Core `Hardware/Smu/Settings/APUSettings1.cs`; RyzenAdj `lib/api.c` |
| 2 | Rembrandt/Vangogh/Phoenix/Phoenix2/HawkPoint/StrixPoint/StrixHalo/KrackanPoint → **MP1 0x4C** | RyzenAdj `set_coall` `FAM_REMBRANDT/VANGOGH/PHOENIX/HAWKPOINT/KRACKANPOINT/STRIXPOINT/STRIXHALO → _do_adjust(0x4C)` (api.c:1397-1404); ZenStates `Mp1Smu … = 0x4C` (APUSettings1_Phoenix.cs:60, APUSettings1_VanGogh.cs:25); G-Helper `CpuFamily.Mobile or CpuFamily.StrixPoint => SendMp1(0x4C, v)` (RyzenSmu.cs:168) | **identical (id+channel+order)**; same-id/different-packing note as #1. One deliberate difference: G-Helper's StrixHalo row is `SendMp1(0x4C, v) … ? s : SendPsmu(0x5D, v)` (RyzenSmu.cs:170) — **our row drops the 0x5D fallback** | as #1 + `Hardware/Smu/Settings/APUSettings1_Phoenix.cs` |
| 3 | Raphael/GraniteRidge/DragonRange → **PSMU 0x07** | RyzenAdj `set_coall` `FAM_DRAGONRANGE/FAM_FIRERANGE → _do_adjust_psmu(0x7)` (api.c:1406-1408); G-Helper `CpuFamily.Raphael => SendPsmu(0x07, v)` (RyzenSmu.cs:172); ZenStates `Rsmu.SMU_MSG_SetAllDldoPsmMargin = 0x7` (Zen4Settings.cs:37) | **identical (id+channel)** vs G-Helper/RyzenAdj and same id as the ZenStates Rsmu entry; against **ZenStates' runtime chooser** it is `same id, different channel` — `SetPsmMarginAllCores` sends `Mp1Smu.SMU_MSG_SetAllDldoPsmMargin (=0x36)` first when it is > 0 (SetPsmMarginAllCores.cs:92-94) | G-Helper / RyzenAdj as above; ZenStates-Core `Hardware/Smu/Settings/Zen4Settings.cs`, `Hardware/Smu/Commands/SetPsmMarginAllCores.cs` |
| 4 | **Vermeer → PSMU 0x0B (11)** | value exists only as ZenStates `Rsmu.SMU_MSG_SetAllDldoPsmMargin = 0xB; // Not sure` (Zen2Settings.cs:34), inherited by `class Zen3Settings : Zen2Settings` (Zen3Settings.cs:4) whose own write is `Mp1Smu.SMU_MSG_SetAllDldoPsmMargin = 0x36` (Zen3Settings.cs:23). **Not present in G-Helper or RyzenAdj at all** | **same id, different packing + different channel choice**: the value is the upstream Zen2 RSMU table entry, upstream's own Zen3 preference is MP1 0x36. Our code already documents exactly this (`RyzenSmuService.cs:285-288`; `修改日志.txt:6702`) | ZenStates-Core `Hardware/Smu/Settings/Zen2Settings.cs:34`, `Zen3Settings.cs:22-23` |

### 2.2 CO write — per-core (全核之外的那条 per-core 通路)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `CurvePerCoreCommand` :231-246 → `if (cpu == CpuCodeName.Vermeer) return (false, 53u);` (**MP1 0x35**); `EncodePerCoreCurveArg` :248-252 `(uint)((ccd << 28) \| (localCore << 20) \| (margin & 0xFFFF))` with `ccd = core/8`, `localCore = core%8`; `SetCoPerCore` :157-176 | `SetPsmMarginSingleCore.cs:20-29`:<br>`uint m = Utils.MakePsmMarginArg(margin);`<br>`result.args[0] = (coreMask & 0xfff00000) \| m;`<br>`if (smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin > 0) … SendMp1Command(smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin, …)`;<br>`Utils.MakePsmMarginArg` = `(uint)(offset + margin) & 0xffff` (Utils.cs:180-184);<br>`Cpu.MakeCoreMask` (17H+) = `(ccd << 28) \| ((core % 8) << 20)` (Cpu.cs:932-940);<br>`Zen3Settings.cs:22` `Mp1Smu.SMU_MSG_SetDldoPsmMargin = 0x35` | **identical (id+packing+order)** — same command, same mailbox, same 16-bit two's-complement margin, same `(ccd<<28)\|((core%8)<<20)\|margin` word. Only the *source* of `ccd/core` differs (upstream reads the APOB core map via `MakeCoreMaskForLogicalCore`, Cpu.cs:948-955; we assume 8 cores/CCD, which the code states at :122-127) | ZenStates-Core `Hardware/Smu/Commands/SetPsmMarginSingleCore.cs`, `Utils.cs:180-184`, `Cpu.cs:932-955`, `Hardware/Smu/Settings/Zen3Settings.cs:22` |
| (G-Helper also has a per-core CO: `SetCoPer` RyzenSmu.cs:177-190 → Renoir `SendMp1(0x54)`, Mobile/Strix* `SendMp1(0x4B)`, Raphael `SendPsmu(0x06)`, packing `sel << 20 \| (value & 0xFFFF)` with `sel = core/8 << 8 \| core%8` on Raphael, commented *"UXTU: ((ccd << 8 \| core) << 20) \| (offset & 0xFFFF)"*) | | **different** — our per-core table is **not** G-Helper's; it is the ZenStates desktop (Vermeer) path. Worth recording because it shows the CO work was re-sourced from ZenStates, not copied from G-Helper | G-Helper `app/Pawn/RyzenSmu.cs:177-190` (for contrast only) |

### 2.3 CO read-back (只读)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `CurveReadCommand` :332-372: Phoenix/Phoenix2/HawkPoint **0xE1**, Cezanne **0xC3**, Rembrandt/Mendocino **0x2F**, StrixPoint/StrixHalo/KrackanPoint **0xAF**, Raphael/GraniteRidge/DragonRange **0xD5**, ShimadaPeak **0xA3**, Vermeer **0x7C**; channel = PSMU (= Rsmu); `ReadCurveMargin` :1088; `EncodeCurveReadArg` :1112 (bare core, `core << 20` on Vermeer); `ReadCurveMarginRaw` :1119; `DecodeCurveMargin` :1142 | ZenStates `Rsmu.SMU_MSG_GetDldoPsmMargin`: Phoenix `0xE1` (APUSettings1_Phoenix.cs:19), Cezanne `0xC3` (APUSettings1_Cezanne.cs:8), Rembrandt `0x2F` (APUSettings1_Rembrandt.cs:8), Strix `0xAF` (APUSettings1_Strix.cs:11), Zen4 `0xD5` (Zen4Settings.cs:39), ShimadaPeak `0xA3` (Zen5Settings_ShimadaPeak.cs:35), Zen2 `0x7C; // Not sure` (Zen2Settings.cs:35); `GetPsmMarginSingleCore.cs:9-16` `result.args[0] = coreMask; … SendRsmuCommand(smu.Rsmu.SMU_MSG_GetDldoPsmMargin, …)` | **identical (id+channel+arg0 semantics = coreMask)** for every row — our table is a per-codename transcription of the upstream `Rsmu.SMU_MSG_GetDldoPsmMargin` values. `DecodeCurveMargin` (32→20→16-bit sign-extension ladder) has **no upstream counterpart**: `not found upstream` (upstream never decodes; SMUDebugTool shows the raw value). G-Helper has **no** CO read path, so this row is ZenStates-only | ZenStates-Core `Hardware/Smu/Settings/APUSettings1_{Phoenix,Cezanne,Rembrandt,Strix}.cs`, `Zen2Settings.cs`, `Zen4Settings.cs`, `Zen5Settings_ShimadaPeak.cs`, `Hardware/Smu/Commands/GetPsmMarginSingleCore.cs`, `Cpu.cs:1828-1840` |

### 2.4 iGPU curve (`SetCoGfx`)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `SetCoGfx` :488-506 — Renoir family → **MP1 0x64 (100)**; Mobile/StrixHalo → **PSMU 0xB7 (183)**; else `Failed`; packing `EncodeCurve` | G-Helper `SetCoGfx` (RyzenSmu.cs:192-205) is **line-for-line the same two rows** including the `StrixHalo → 0xB7` choice and the `default: Failed`; its own comments say<br>`// RyzenAdj: _do_adjust(0x64) — MP1 only` (RyzenSmu.cs:197)<br>`// UXTU Socket_FT6_FP7_FP8: set-cogfx false 0xB7 — PSMU` (RyzenSmu.cs:199).<br>RyzenAdj `set_cogfx`: Renoir/Cezanne/Lucienne `_do_adjust(0x64)`, Rembrandt/Phoenix/HawkPoint/Vangogh `_do_adjust_psmu(0xB7)`, StrixHalo → *"0xB7 is rejected on this architecture"* (api.c:1445-1467).<br>ZenStates: `Rsmu.SMU_MSG_SetGpuPsmMargin`: APUSettings1 `0x53`, `_Rembrandt` **0xB7**, `_Phoenix` `0x1F`, APUSettings0 `0x59`, Zen4 `0xA7` | **identical (id+channel)** vs G-Helper and vs RyzenAdj for the Mobile row; **same id as the ZenStates `_Rembrandt` entry only**; on Phoenix/Strix the 0xB7 disagrees with ZenStates' own table (0x1F) and the Renoir `0x64` exists **only** in RyzenAdj (no ZenStates table has 0x64 for iGPU CO). StrixHalo: we follow G-Helper (send 0xB7), RyzenAdj refuses it → `similar but rewritten` at that one point | FlyGoat/RyzenAdj `lib/api.c:1445-1467` (values + reason), surfaced through G-Helper `app/Pawn/RyzenSmu.cs:192-205`; ZenStates-Core `Hardware/Smu/Settings/APUSettings1_Rembrandt.cs:9` (the name/id) |

### 2.5 Fixed-frequency / OC mode (`ClockCommand`, `SetOcClock`, `DisableOc`)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `ClockCommand` :380-408 rows `<Psmu, Enable, Clock, Disable>`:<br>SummitRidge/PinnacleRidge → MP1 `0x23 / 0x39 / 0x24`<br>Matisse/Vermeer → MP1 `0x24 / 0x26 / 0x25`<br>Raphael族 → PSMU `0x5D / 0x5F / 0x5E`<br>Renoir/Cezanne/Lucienne → MP1 `0x2F / 0x31 / 0x30`<br>Rembrandt/Phoenix/Phoenix2/HawkPoint → PSMU `0x17 / 0x19 / 0x18` | ZenStates `Zen2Settings.cs:60-63` `Mp1Smu EnableOcMode 0x24 / DisableOcMode 0x25 / SetOverclockFrequencyAllCores 0x26`; `Zen4Settings.cs:23-25` `Rsmu 0x5D / 0x5E / 0x5F`; `APUSettings1.cs:96-98` `Mp1Smu 0x2F / 0x30 / 0x31`; `APUSettings1.cs:34-36` `Rsmu 0x17 / 0x18 / 0x19`; `ZenSettings.cs:51-53` (Zen1) `Mp1Smu EnableOcMode 0x37 / DisableOcMode 0x37 / SetOverclockFrequencyAllCores 0x39` and `:57-59` `AcBtcStartCal 0x23 / AcBtcStopCal 0x24`.<br>RyzenAdj `set_enable_oc` (api.c:1367-1385: Renoir `0x2F`, Rembrandt `psmu 0x17`), `set_oc_clk` (api.c:1281-1300: `0x31`, retry `_do_adjust_psmu(0x19)`), `set_disable_oc` (api.c:1343-1365: `0x30`, retry `_do_adjust_psmu(0x1d)`) | Matisse/Vermeer, Raphael, Renoir, Mobile rows: **identical (id+channel)** to ZenStates' tables. Zen1 row: **not found upstream** — ZenStates master has `0x37/0x37` for enable/disable and uses `0x23/0x24` for **AcBtcStartCal/AcBtcStopCal**, i.e. our two ids coincide with *different* upstream commands (`different`). G-Helper has no clock code at all, so this whole section is RyzenAdj + ZenStates | ZenStates-Core `Hardware/Smu/Settings/{Zen2,Zen4,APUSettings1,ZenSettings}.cs`; FlyGoat/RyzenAdj `lib/api.c:1367-1385, 1281-1300, 1343-1365` |
| `SetOcClock` :430-471 sequence: `Enable(arg=0)` → `Clock(mhz)`; if the clock call returns `UnknownCmd` and `Family == Renoir` → `SendPsmu(25 = 0x19)`; on non-OK → `DisableOc()` + throw. `DisableOc` :473-486: `Disable(arg=0)`, and for Renoir `UnknownCmd` → `SendPsmu(29 = 0x1D)` | RyzenAdj models exactly these three steps as three API calls whose ids and PSMU retries match ours one-for-one (`set_enable_oc`, `set_oc_clk` → `psmu 0x19`, `set_disable_oc` → `psmu 0x1d`); ZenStates `SetOcMode`/`SetFrequencyAllCore` are separate commands with an Rsmu→Mp1 failover instead (SetOcMode.cs) | enable/clock/disable **ids**: `identical`; the PSMU fallbacks `0x19`/`0x1D` and the "try MP1, otherwise PSMU" pattern: `identical` to RyzenAdj; the *orchestration* (enable→set→auto-disable+throw on failure) is our own composition of RyzenAdj's three functions → `similar but rewritten` | FlyGoat/RyzenAdj `lib/api.c:1281-1385` (+ ZenStates-Core settings tables for the ids) |

### 2.6 Power limits (`SetStapm` / `SetFast` / `SetSlow`)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `SetStapm` :910-937, `SetFast` :939-966, `SetSlow` :968-997; `arg = (uint)(watts * 1000)`:<br>Raven → MP1 `0x1A / 0x1B / 0x1C`<br>Renoir → MP1 `0x14 / 0x15 / 0x16` **then** PSMU `0x31 / 0x32 / 0x33 (+0x34 for slow)`, returning the MP1 status<br>Mobile/StrixPoint/StrixHalo → MP1 `0x14 / 0x15 / 0x16`<br>Raphael → MP1 `0x4F / 0x3E / 0x5F` | G-Helper `SetStapm/SetFast/SetSlow` (RyzenSmu.cs:379-432) = **the same ids, the same channels, the same double-send order** (Raven `0x1A/0x1B/0x1C`; Renoir `0x14 + psmu 0x31`, `0x15 + 0x32`, `0x16 + 0x33 + 0x34`; Mobile/Strix MP1; Raphael `0x4F/0x3E/0x5F`).<br>RyzenAdj `set_stapm_limit` (api.c:444-…, Raven `0x1a`; Renoir-family `0x14` + *"Retry with PSMU"* `_do_adjust_psmu(0x31)`; Raphael `0x4f`), `set_fast_limit` (`0x1b` / `0x15+0x32` / `0x3e`), `set_slow_limit` (`0x1c` / `0x16` / `0x5f`).<br>ZenStates `Mp1Smu.SMU_MSG_SetStapmLimit/SetFastLimit/SetSlowLimit`: APUSettings0.cs:65-68 `0x1A/0x1B/0x1C`, APUSettings1.cs:73-76 `0x14/0x15/0x16`, Zen4Settings.cs:66-69 `0x4F/0x3E/0x5F` (Zen2: `0x3C`, Zen3: `0x3D`) | **identical (id+channel+packing+order)** vs G-Helper and RyzenAdj, including the Renoir "MP1 first, then mirror to Rsmu/ApuSlow" behaviour and the `watts × 1000`→mW packing (`SetSmuLimit` uses `arg * 1000` too, SetSmuLimit.cs:11). **Same ids** as ZenStates' tables but ZenStates' helper tries **Rsmu first** and only falls back to Mp1 — a channel-order difference (`similar but rewritten` for that one detail) | G-Helper `app/Pawn/RyzenSmu.cs:379-432`; FlyGoat/RyzenAdj `lib/api.c`; ZenStates-Core `Hardware/Smu/Settings/{APUSettings0,APUSettings1,Zen4Settings}.cs` |

### 2.7 Thermal (`SetThm`)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `SetThm` :508-530, `arg = (uint)celsius`: Raven → MP1 `0x1F`; Renoir/Mobile/StrixPoint/StrixHalo → MP1 `0x19`; Matisse (family, incl. Vermeer) → MP1 `0x3E`; Raphael → MP1 `0x3F` | G-Helper `SetThm` (RyzenSmu.cs:207-223) = **identical rows**, with its own comments `// RyzenAdj: _do_adjust(0x1F / 0x19 / 0x3E / 0x3F) — MP1 only`.<br>RyzenAdj `set_tctl_temp` (api.c:620-653): Raven `0x1f`, Renoir…StrixHalo `0x19`, DragonRange/FireRange `0x3f`.<br>ZenStates `Mp1Smu.SMU_MSG_SetTctlMax`: APUSettings0.cs:70 `0x1F`, APUSettings1.cs:78 `0x19`, Zen2Settings.cs:55 `0x3D`, Zen3Settings.cs:17 `0x3E`, Zen4Settings.cs:72 `0x3F` | ids/channel/packing: **identical** vs G-Helper and RyzenAdj; **same ids** as ZenStates' APU0/APU1/Zen3/Zen4 entries. Two honest deviations: (a) our `CpuFamily.Matisse` row uses Zen3's `0x3E` where genuine Zen2 Matisse is `0x3D` upstream (family label covers Matisse **and** Vermeer); (b) ZenStates' `SetTctlMax` tries **Rsmu first** for non-APU and falls back to Mp1, we send Mp1 only → `similar but rewritten` for the channel logic, `identical` for the ids | G-Helper `app/Pawn/RyzenSmu.cs:207-223`; FlyGoat/RyzenAdj `lib/api.c:620-653`; ZenStates-Core `Hardware/Smu/Settings/*`: `SetTctlMax` entries |

### 2.8 PM-table read (power/thermal *read* path — neither FMax nor CO, but SMU-derived)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `GetPowerLimits` :568-630 (`ioctl_resolve_pm_table` → `ioctl_update_pm_table` + `Thread.Sleep(100)` → `ioctl_update_pm_table` + `Sleep(200)` → 64 words → floats `[0],[2],[4],[tctlIndex]` + optional `[6]`); `GetTctlIndex` :632-653 (30/100→22; 55/63/64/69/76/93/101→16; 84/98→10; else **-1**); `HasApuSlowField` :655-670 (55/63/64/69/76/93/100/101) | G-Helper `GetPowerLimits`/`GetTctlIndex`/`HasApuSlowField` (RyzenSmu.cs:253-331) = **same ioctl sequence, same sleeps, same float indices**, with its own credit `// matching the switch in RyzenAdj api.c get_tctl_temp()` (RyzenSmu.cs:295) and `// Per RyzenAdj api.c get_apu_slow_limit()` (:324).<br>RyzenAdj `get_tctl_temp` (api.c:1852+), `get_apu_slow_limit` (api.c:1478+), `_read_float_value(0x0/0x8/0x10)` offsets | **identical (sequence + offsets + version grouping)**; the table *form* differs (our explicit `case 30/100/55/…` list vs G-Helper's `0x1E or 0x64 => 22`), and we tighten the unknown case to `-1`→"no read" where G-Helper defaults to 16 → `similar but rewritten` for that one line, `identical` otherwise | G-Helper `app/Pawn/RyzenSmu.cs:249-331`; FlyGoat/RyzenAdj `lib/api.c` (`get_tctl_temp`, `get_apu_slow_limit`, `get_stapm_limit` offsets) |

### 2.9 Transport + addresses (used by *every* row above)

| ours | upstream | verdict | likely source |
|---|---|---|---|
| `MailboxRaw` :1372-1466 (wait idle → clear RSP → write 6 ARGs → write MSG → poll RSP ≤ 200 ms → read 6 responses); `SendMp1`/`SendPsmu`; `SendPsmuRaw` :1063; `GetMp1Addrs` :1469; `GetPsmuAddrs` :1509 (all 12 address groups) | G-Helper `MailboxRaw`/`SendMp1`/`SendPsmu`/`GetMp1Addrs`/`GetPsmuAddrs` (RyzenSmu.cs:434-589) — the same algorithm **and the same literal constants** (`0x03B10528/0x03B10564/0x03B10998`, `0x03B10A20/0x03B10A80/0x03B10A88`, `0x03B10530/0x03B1057C/0x03B109C4`, `0x03B10928/0x03B10978`, `0x03B10924/0x03B10970`); ZenStates `Hardware/Smu/Mailboxes/{MP1,RSMU}Mailbox.cs` implements the same transaction, and every `Settings/*.cs` declares the same `SMU_ADDR_MSG/RSP/ARG` triplets | **identical (algorithm + constants)**. `THIRD-PARTY-NOTICES.md` S2(d) already attributes this transaction **but only under "the FMax feature"** — it is equally the CO/OC/power transport and must be named there too | G-Helper `app/Pawn/RyzenSmu.cs:434-589`; ZenStates-Core `Hardware/Smu/Mailboxes/*.cs` + `Hardware/Smu/Settings/*.cs` |

### 2.10 Verdict roll-up

| verdict | rows |
|---|---|
| `identical (id+packing+order)` | CO all-core APU rows (2), per-core write, CO read-back (7 ids), iGPU-CO in its G-Helper/RyzenAdj form, OC rows Matisse/Vermeer+Raphael+Renoir+Mobile, power limits ×3, thermal, PM-table read, transport+addresses |
| `same id, different packing` | CO all-core argument packing (ours 20-bit `& 0xFFFFF` vs upstream 16-bit `MakePsmMarginArg`) — **already admitted in our own code comment** `RyzenSmuService.cs:1136-1140` |
| `same id, different channel/choice` | Raphael all-core (`PSMU 0x07` vs ZenStates' Mp1 `0x36`); Vermeer all-core (`PSMU 0x0B` vs ZenStates' Mp1 `0x36`); Renoir power limits (MP1-first vs ZenStates' Rsmu-first) |
| `similar but rewritten` | `SetOcClock` orchestration; `DecodeCurveMargin` (no upstream decode); PM-table table form; iGPU-CO StrixHalo choice |
| `different` | our per-core CO table vs **G-Helper's** `SetCoPer` (different ids) — i.e. this row is ZenStates-derived, not G-Helper-derived |
| `not found upstream` | Zen1/Zen+ `ClockCommand` row (`0x23/0x39/0x24`) — ZenStates tags `0x23/0x24` as `AcBtcStartCal/AcBtcStopCal`; `0x64` iGPU-CO for Renoir exists only in RyzenAdj; UXTU's `set-cogfx 0xB7` (G-Helper's comment is the only trace) |

---

## 3. In-repo traces that already admit derivation

These are the "smoking gun" texts; they show the CO/OC/power work was *deliberately* sourced from the
SMU projects, which is exactly the user's premise.

| Where | What it admits |
|---|---|
| `source\PawnIO\RyzenSmuService.cs:132` (comment above `SetCoPerCore`) | `// 命令 / 位宽 / 编码（全部来自上游 ZenStates-Core，且台式机实测确认）：` — "command / bit width / encoding **all come from upstream ZenStates-Core**" |
| same, `:133-138` | names `Mp1Smu.SMU_MSG_SetDldoPsmMargin (Zen3Settings.cs:22)`, `Utils.cs:178-184 MakePsmMarginArg`, `Cpu.cs:929-940 MakeCoreMask + SetPsmMarginSingleCore.cs:20-27` |
| same, `:259-268` | names the read command's per-codename origin (`APUSettings1_Phoenix.cs` 0xE1, `APUSettings1_Cezanne.cs` 0xC3, `APUSettings1_Rembrandt.cs` 0x2F, `APUSettings1_Strix.cs` 0xAF, `Zen4Settings.cs` 0xD5, `Zen5Settings_ShimadaPeak.cs` 0xA3) |
| same, `:283-288`, `:344-348` | `// 我们的 Vermeer 写命令 PSMU 0x0B 正是同一块 Zen2 RSMU 表里的 SetAllDldoPsmMargin = 0xB（同样 "// Not sure"）` and `Rsmu.SMU_MSG_GetDldoPsmMargin = 0x7C; // Not sure` |
| same, `:1136-1140` | the 16-bit vs 20-bit packing difference (`Utils.MakePsmMarginArg` vs our `EncodeCurve`) — i.e. deliberate re-use with a documented deviation |
| same, `:1165-1170` (the "来源与许可" block) | the block that names **both** projects with licence + URL — but scoped to **FMax only**: `irusanov/SMUDebugTool（GPL-3.0…）与 irusanov/ZenStates-Core（GPL-3.0…）` |
| `source\PawnIO\FMaxCapability.cs:4-9, 38-41` | same dual-project derived notice (FMax scope) |
| `source\MechrevoLite\SmuCoRoundtrip.cs:30-45` | `**不新增任何写命令**：写入只用现有路径 RyzenSmuService.SetCoAll（= CurveCommand 那套）` + `上游 CO 允许 -50..50` |
| `修改日志.txt:6055-6181` | the whole investigation entry: reference implementation = `https://github.com/irusanov/SMUDebugTool` (engine = ZenStates-Core), the per-codename read-command table with source file + line, the mailbox addresses "= `APUSettings1.cs` 的 `Rsmu.SMU_ADDR_*`" |
| `修改日志.txt:6150-6151` | `注意 g-helper/UXTU 的编码是 (core<<20)|margin，而上游 ZenStates 在 APU 上 …（上游这条 APU 每核写存疑）` |
| `修改日志.txt:6609-6735` | Vermeer read-command audit quoting `Zen3Settings.cs:22-23`, `Zen2Settings.cs:35`, `SMU.cs:282`, `RyzenSMU.p:195,214`, `SettingsForm.cs` |
| `修改日志.txt:6702`, `:7017-7105`, `:7297-7341`, `:7350-7365`, `:7420-7430` | `Vermeer：我们 = PSMU 0x0B（= 上游 Zen2 Rsmu SetAll 0xB…）`; the per-core-write candidate table with upstream citations (`Zen4Settings.cs:86-87`, `MP1 0x35` vs `PSMU 0x0A`); the desktop `CORE-WRITE-OK` verdict; the productisation entry (`CurvePerCoreCommand` / `SetCoPerCore`) |
| `_upstream\0.290.6\decomp\PawnIO\RyzenSmuService.cs:92-269, 459-539` | the **pre-existing** table (our own previous release) is *the same table*: `CurveCommand` 85/76/7/11, `ClockCommand` 35/57/36, 36/38/37, 93/95/94, 47/49/48, 23/25/24, `SetCoGfx` 100/183, `SetThm` 31/25/62/63, `SetStapm/Fast/Slow` Raven 26/27/28, Renoir 20+49 / 21+50 / 22+51+52, Raphael 79/62/95 — ids, channels and call order all match G-Helper's file 1:1 |

---

## 4. `merge target: derived-source inventory` — ready-to-paste block for `THIRD-PARTY-NOTICES.md`

> **Merge target: `THIRD-PARTY-NOTICES.md` §1.1 "Derived / reimplemented source — the SMU work"
> (append as new rows S6–S8 after S5) and the notes in §4/§6 that follow.** Everything between the
> `<<<` / `>>>` markers is the literal text to paste; `…` placeholders that this audit could not pin
> are marked ⚠️ and listed in §8.

<<<BEGIN MERGE BLOCK — THIRD-PARTY-NOTICES.md §1.1

| # | Component | What we use it for | Where it landed in our tree (evidence) | Copy or reimplementation? |
|---|---|---|---|---|
| S6 | **seerge/g-helper — `app/Pawn/RyzenSmu.cs`**<br>https://github.com/seerge/g-helper/blob/b2b7bae52cd39095002078af98349e6f6d57b960/app/Pawn/RyzenSmu.cs<br>GPL-3.0 | The **AMD SMU command layer as a whole**, not just the app shell: all-core Curve Optimizer (`SetCoAll`), iGPU curve (`SetCoGfx`), thermal limit (`SetThm`), the power-limit setters (`SetStapm`/`SetFast`/`SetSlow`), the PM-table read (`GetPowerLimits`/`GetTctlIndex`/`HasApuSlowField`), the mailbox transaction (`MailboxRaw`) and the SMN address groups. | `source\PawnIO\RyzenSmuService.cs` — CO `:181-207`, iGPU curve `:488-506`, thermal `:508-530`, power `:910-997`, PM table `:568-670`, transport `:1372-1466`, addresses `:1469-1540`. Same ids/channels/order as upstream lines 160-223 and 379-432 and 253-331 and 434-589. | **Derived table** (facts/IDs re-expressed as C# `switch`es — not a verbatim file copy), and the repo keeps the **byte-identical upstream file itself** at `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs` (22 317 B, SHA-256 `07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35` = git blob `b063297f0ac394fce4a2aede49488a4120193377`). G-Helper's file in turn credits **RyzenAdj** and **UXTU** in its own comments (`:165-172`, `:197-201`, `:212-220`). |
| S7 | **irusanov/ZenStates-Core** — CO/OC/power/thermal tables and command classes<br>https://github.com/irusanov/ZenStates-Core<br>GPL-3.0 | In addition to the FMax material already listed under S2: the **per-core CO write** (`Mp1Smu.SMU_MSG_SetDldoPsmMargin = 0x35`, `SetPsmMarginSingleCore`, `Utils.MakePsmMarginArg`, `Cpu.MakeCoreMask`), the **CO read-back** table (`Rsmu.SMU_MSG_GetDldoPsmMargin` per codename), the **all-core CO ids** (`SetAllDldoPsmMargin` 0x55/0x4C/0x07/0x0B), the **OC-mode ids** (`EnableOcMode`/`DisableOcMode`/`SetOverclockFrequencyAllCores`), the **power-limit ids** (`SetStapmLimit`/`SetFastLimit`/`SetSlowLimit`) and `SetTctlMax`, plus every `SMU_ADDR_MSG/RSP/ARG` triple. | `source\PawnIO\RyzenSmuService.cs`: per-core `:157-176`, `:231-252`; read-back `:332-372`, `:1088-1160`; all-core `:181-207`; OC `:380-486`; power `:910-997`; thermal `:508-530`; addresses `:1469-1540`. Upstream: `Hardware/Smu/Settings/{APUSettings0,APUSettings1,APUSettings1_Cezanne,APUSettings1_Phoenix,APUSettings1_Rembrandt,APUSettings1_Strix,APUSettings1_VanGogh,Zen2Settings,Zen3Settings,Zen4Settings,Zen5Settings_ShimadaPeak,ZenSettings}.cs`, `Hardware/Smu/Commands/{SetPsmMarginSingleCore,GetPsmMarginSingleCore,SetPsmMarginAllCores,SetSmuLimit,SetTctlMax,SetOcMode}.cs`, `Utils.cs:180-184`, `Cpu.cs:932-955`. | **Derived table** (id-for-id transcription re-expressed as `switch`es, with documented deviations: our 20-bit CO packing vs upstream 16-bit; our per-codename Vermeer row using the upstream Zen2 `0x0B`). The reference `.cs` sources are **not** vendored in this repo — the comparison was made against upstream at commit `869b7e82572337e5a673e92230edc92bddf0e44b` and is recorded in `ATTRIBUTION-CO-ADDENDUM.md`. |
| S8 | **FlyGoat/RyzenAdj**<br>https://github.com/FlyGoat/RyzenAdj<br>**LGPL-3.0** | The values and channel-fallback order for: iGPU curve (`set_cogfx`: Renoir `_do_adjust(0x64)`, Mobile `_do_adjust_psmu(0xB7)`, StrixHalo deliberately unsupported), the fixed-frequency OC sequence (`set_enable_oc` `0x2F`/`psmu 0x17`, `set_oc_clk` `0x31` → `psmu 0x19`, `set_disable_oc` `0x30` → `psmu 0x1d`), the APU power-limit/lüfter thermal tables (`set_stapm_limit`/`set_fast_limit`/`set_slow_limit`/`set_tctl_temp` with their PSMU retries) and the PM-table `get_tctl_temp`/`get_apu_slow_limit` offsets. | `source\PawnIO\RyzenSmuService.cs` — iGPU curve `:488-506`, OC `:430-486`, power `:910-997`, thermal `:508-530`; the G-Helper reference copy in this repo carries the explicit citations (`diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs:197-201` `// RyzenAdj: _do_adjust(0x64) — MP1 only`, `:212-220`, `:295` `// matching the switch in RyzenAdj api.c get_tctl_temp()`, `:324`). | **Values/order reference** (the expression we hold came through G-Helper's GPL-3.0 file; the numeric ids and the fallback order are corroborated in RyzenAdj `lib/api.c:444-653, 1281-1467, 1478+, 1852+`). Kept in the **protocol-reference** group today — ⚠️ see §8 note (b): if the maintainer prefers, promote it to the derived group; LGPL-3.0 is compatible with our GPL-3.0-only either way. |

Also update, in the same file:

* §1.1 **S2** — replace "the **FMax capability table**" scope wording so it reads "the FMax capability
  table **and the CO/OC/power/thermal command tables** (see S7)".
* §1.2 **B1** table note — no change.
* §2.1 licence table — add a row for **G-Helper `app/Pawn/RyzenSmu.cs`** (GPL-3.0, same `LICENSE`
  blob `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` already recorded for G-Helper) with the key clause
  "GPLv3 §5a/§5b attribution + §5c whole-work source".
* §6 **Verification gaps** — replace item 2 ("Byte-diff against ZenStates-Core is impossible here")
  with a pointer to `ATTRIBUTION-CO-ADDENDUM.md` §1.2/§8, which now contains the verified blob-SHA
  table for the 69 upstream files actually compared.

<<<END MERGE BLOCK

---

## 5. Proposed in-code anchors (DO NOT APPLY — `source\` is owned by another agent)

Line numbers = 2026-10-10 snapshot (`RyzenSmuService.cs` = 66 787 B / 1 555 lines). Anchors are given
as text so they survive drift.

### 5.1 `source\PawnIO\RyzenSmuService.cs` — correct the file header (line 7-8 today)

**Anchor (must be replaced — it is currently false):**

```csharp
//   * 本文件的 SMU 命令表（CurveCommand / Stapm / Fast / Slow / Thm / CO 等）与 PSMU 邮箱地址表
//     （GetPsmuAddrs）沿用 L-Mechrevo 自己的 0.290.6 实现，非第三方代码。
```

**Replace with:**

```csharp
//   * 本文件的 SMU 命令表（CurveCommand / CurvePerCoreCommand / CurveReadCommand / ClockCommand /
//     Stapm / Fast / Slow / Thm / CoGfx）与邮箱地址表（GetMp1Addrs / GetPsmuAddrs）是**派生作品**：
//     它们与下列上游逐条同源（命令 ID / 通道 / 参数打包 / 调用顺序），
//     L-Mechrevo 只是把它们重写成按代号的 C# 表并补了守卫（见 ATTRIBUTION-CO-ADDENDUM.md）：
//         seerge/g-helper   app/Pawn/RyzenSmu.cs  https://github.com/seerge/g-helper           (GPL-3.0)
//         irusanov/ZenStates-Core                 https://github.com/irusanov/ZenStates-Core    (GPL-3.0)
//         FlyGoat/RyzenAdj  lib/api.c             https://github.com/FlyGoat/RyzenAdj           (LGPL-3.0)
//     注：本仓库**保留了一份与 g-helper 上游逐字节相同**的参考副本
//         diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs
//         （SHA-256 07925B97…DDE35），它就是这张表的来源文件。
//     ⚠️ 上述三段**不是**"沿用 L-Mechrevo 自己的 0.290.6 实现"：0.290.6 的那张表本身就是这张表的
//        早期重排（逐条比对见 ATTRIBUTION-CO-ADDENDUM.md §3 末行），不能因为"我们自己以前也有"
//        就当成原创。
//     该部分按 GPL-3.0 分发（LGPL-3.0 与 GPL-3.0-only 相容）；许可文本见 LICENSES\，
//     第三方清单见 THIRD-PARTY-NOTICES.md 的 S6/S7/S8。
```

### 5.2 CO write (all-core) — insert **before** `internal static (bool Psmu, uint Command)? CurveCommand(CpuCodeName cpu)` (now line 181)

```csharp
// 派生声明（CO 全核写）：下表与上游逐条同源 —— 不是自创的命令 ID。
//   * Renoir/Cezanne/Lucienne -> MP1 0x55、Rembrandt/Vangogh/Phoenix/…/Strix* -> MP1 0x4C、
//     Raphael 族 -> PSMU 0x07：seerge/g-helper `app/Pawn/RyzenSmu.cs:160-175`（GPL-3.0）
//     == FlyGoat/RyzenAdj `lib/api.c:1387-1414` `set_coall`（LGPL-3.0）
//     == irusanov/ZenStates-Core `Hardware/Smu/Settings/APUSettings1.cs:106`（0x55）、
//        `APUSettings1_Phoenix.cs:60`（0x4C）、`Zen4Settings.cs:37`（0x7）（GPL-3.0）。
//   * Vermeer -> PSMU 0x0B：ZenStates-Core `Zen2Settings.cs:34`（`// Not sure`，Zen3Settings 继承）。
//   * 打包差异（**有意保留**）：我们的 EncodeCurve 编低 20 位，上游 `Utils.MakePsmMarginArg`
//     编低 16 位（Utils.cs:180-184）—— 见下方 DecodeCurveMargin 处的说明。
```

### 5.3 CO per-core write — `CurvePerCoreCommand` / `EncodePerCoreCurveArg` / `SetCoPerCore` (now :231 / :248 / :157)

Insert **after** the existing provenance block (anchor: the line
`//   * arg0     = (ccd << 28) | ((core % 8) << 20) | margin`, now ~:139):

```csharp
//   来源与许可（补充）：命令与编码来自 irusanov/ZenStates-Core（GPL-3.0，
//   https://github.com/irusanov/ZenStates-Core）—— `Hardware/Smu/Commands/SetPsmMarginSingleCore.cs`
//   （`result.args[0] = (coreMask & 0xfff00000) | Utils.MakePsmMarginArg(margin)`）、
//   `Utils.cs:180-184`、`Cpu.cs:932-940 MakeCoreMask`、`Hardware/Smu/Settings/Zen3Settings.cs:22`。
//   我们的 arg0 与上游逐位相同；只有 ccd/core 的取值来源不同（上游读 APOB，本仓库假设每 CCD 8 核）。
```

### 5.4 CO read-back — insert **before** `internal static uint? CurveReadCommand(CpuCodeName cpu)` (now :332)

```csharp
// 来源与许可（补充）：本表的每一个命令 ID 都是上游 ZenStates-Core 的
//   `Rsmu.SMU_MSG_GetDldoPsmMargin`（GPL-3.0，https://github.com/irusanov/ZenStates-Core）：
//   APUSettings1_Phoenix.cs:19 / APUSettings1_Cezanne.cs:8 / APUSettings1_Rembrandt.cs:8 /
//   APUSettings1_Strix.cs:11 / Zen4Settings.cs:39 / Zen5Settings_ShimadaPeak.cs:35 /
//   Zen2Settings.cs:35（0x7C，上游自注 "// Not sure"）。
//   调用形态同 `Hardware/Smu/Commands/GetPsmMarginSingleCore.cs`（arg0 = coreMask，走 Rsmu 邮箱）。
//   注：本表的"解码阶梯"（DecodeCurveMargin）**没有**上游对应物，是本仓库自己的判定，
//   如实记在这里，避免日后被误当成上游定义。
```

### 5.5 iGPU curve — insert **before** `public SmuStatus SetCoGfx(int value)` (now :488)

```csharp
// 派生声明（iGPU 曲线）：两行取值来自 FlyGoat/RyzenAdj `lib/api.c:1445-1467` `set_cogfx`
//   （LGPL-3.0，https://github.com/FlyGoat/RyzenAdj）：Renoir 族 `_do_adjust(0x64)`、
//   Mobile 族 `_do_adjust_psmu(0xB7)`，并经 seerge/g-helper `app/Pawn/RyzenSmu.cs:192-205`
//   原样带出（含它给 StrixHalo 也发 0xB7、而 RyzenAdj 明确拒绝这一点）。
//   0xB7 这个名字同时出现在 ZenStates-Core `APUSettings1_Rembrandt.cs:9`
//   （`Rsmu.SMU_MSG_SetGpuPsmMargin = 0xB7`，GPL-3.0）；但**Phoenix/Strix 上游写的是 0x1F**，
//   本表在这两代上与 ZenStates 不一致 —— 有意的"照 g-helper/RyzenAdj"，不是抄错，记录在此。
```

### 5.6 Fixed-frequency / OC — insert **before** `internal static (bool Psmu, uint Enable, uint Clock, uint Disable)? ClockCommand(CpuCodeName cpu)` (now :380)

```csharp
// 派生声明（固定频率 / OC 模式）：三段 ID 全部来自上游，逐条可核：
//   * Matisse/Vermeer MP1 0x24/0x26/0x25 = ZenStates-Core `Zen2Settings.cs:60-63`（GPL-3.0）
//   * Raphael 族 PSMU 0x5D/0x5F/0x5E     = ZenStates-Core `Zen4Settings.cs:23-25`
//   * Renoir 族 MP1 0x2F/0x31/0x30       = ZenStates-Core `APUSettings1.cs:96-98`
//   * Mobile 族 PSMU 0x17/0x19/0x18      = ZenStates-Core `APUSettings1.cs:34-36`
//   而"使能 -> 设频 -> 失败即关"的调用顺序，以及 Renoir 的 PSMU 兜底 ID（设频 0x19 / 关闭 0x1D），
//   与 FlyGoat/RyzenAdj `lib/api.c:1281-1385`（`set_oc_clk` / `set_disable_oc` / `set_enable_oc`，
//   LGPL-3.0）一模一样。
//   ⚠️ **Zen1/Zen+ 那一行（0x23/0x39/0x24）在上游 master 里找不到**：ZenStates-Core 的
//   ZenSettings.cs:51-53 是 0x37/0x39/0x37，而 0x23/0x24 在它表里是 AcBtcStartCal/AcBtcStopCal
//   （ZenSettings.cs:57-59）。本行疑似老版本转录或误抄，**不改**，只登记（见 addendum §8）。
```

### 5.7 Power limits + thermal + PM table — insert **before** `private SmuStatus SetStapm(int watts)` (now :910)

```csharp
// 派生声明（功率/温度）：SetStapm/SetFast/SetSlow/SetThm 的 ID、通道与**调用顺序**（含 Renoir
// 先发 MP1 再镜像 PSMU、Slow 额外补一条 ApuSlow 0x34）与上游逐条相同：
//   * seerge/g-helper `app/Pawn/RyzenSmu.cs:379-432`（GPL-3.0）—— 同一张表、同一次序；
//   * FlyGoat/RyzenAdj `lib/api.c:444-653`（set_stapm_limit / set_fast_limit / set_slow_limit /
//     set_tctl_temp）—— 同 ID + "MP1 失败再试 PSMU" 的原始写法（LGPL-3.0）；
//   * irusanov/ZenStates-Core `Hardware/Smu/Settings/{APUSettings0,APUSettings1,Zen4Settings}.cs`
//     与 `Commands/{SetSmuLimit,SetTctlMax}.cs`（GPL-3.0）—— 同 ID；
//     ⚠️ 但 ZenStates 的助手是"先 Rsmu 后 Mp1"，与我们/G-Helper/RyzenAdj 的"先 Mp1"相反。
//     ⚠️ Matisse 族那一行的温度命令用 0x3E（= 上游 Zen3），真 Zen2 上游是 0x3D（Zen2Settings.cs:55）。
//   * GetPowerLimits/GetTctlIndex/HasApuSlowField 的 ioctl 时序、sleep 值与 float 下标同
//     g-helper `:249-331`（其自身注释把表归给 RyzenAdj 的 get_tctl_temp/get_apu_slow_limit）；
//     唯一的自研改动是"未知表版本 -> -1（不可读）"，g-helper 是 default 16。
```

### 5.8 Transport + addresses — insert **before** `private SmuStatus MailboxRaw(` (now :1372)

```csharp
// 派生声明（邮箱事务与 SMN 地址）：WaitForMailboxIdle -> 清 RSP -> 写 6 个 ARG -> 写 MSG ->
// 轮询 RSP -> 读回 6 个 response，与 seerge/g-helper `app/Pawn/RyzenSmu.cs:434-589`（GPL-3.0）
// 逐句相同；地址常量（GetMp1Addrs / GetPsmuAddrs 的 12 组值）也相同，并且等于
// irusanov/ZenStates-Core 每张 `Hardware/Smu/Settings/*.cs` 里的 `SMU_ADDR_MSG/RSP/ARG`
// （GPL-3.0，邮箱类见 `Hardware/Smu/Mailboxes/{MP1,RSMU}Mailbox.cs`）。
```

### 5.9 The small files that only *use* these paths

| File | Anchor | Text to add |
|---|---|---|
| `source\PawnIO\CpuInfo.cs` | file header (line 1) | `// 本文件是 L-Mechrevo 自己的判据代码；它读的 RyzenSmuService.CurveCommand / CurvePerCoreCommand / SetCoGfx 命令表是派生作品（来源与许可见 RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。` |
| `source\PawnIO\CpuInfo.cs` | above `private static bool HasIgpuCurveMailbox(CpuCodeName cpu)` (now :512) | `// 白名单与 RyzenSmuService.SetCoGfx 的 switch 一致；那两行取值的来源见 SetCoGfx 上方的派生声明。` |
| `source\MechrevoLite.Hardware\RuntimeCpuTuning.cs` | file header, after the existing `// 2026-10-05 step7A: ported from upstream 0.290.6 …` (line 16-18) | `// CO/OC/power 通路调用的是派生自 g-helper / ZenStates-Core / RyzenAdj 的 SMU 命令表（来源见 source\PawnIO\RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。本文件本身不新增任何 SMU 命令 ID。` |
| `source\MechrevoLite\SmuCoRoundtrip.cs` | file header, after line 31 (`// 本文件不含任何新的 SMU 命令 ID。`) | `// 它调用的 SetCoAll / SetCoPerCore / CurveReadCommand / DisableOc 是派生自 g-helper / ZenStates-Core 的表（见 RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。` |
| `source\MechrevoLite\CpuTuningForm.cs` | file header (line 1) | `// 本窗口的 CO / 固定频率 / FMax 通路走 RuntimeCpuTuning -> RyzenSmuService 的派生命令表；来源与许可见 source\PawnIO\RyzenSmuService.cs 头部、THIRD-PARTY-NOTICES.md 与 ATTRIBUTION-CO-ADDENDUM.md。` |

---

## 6. In-UI 「关于 / 开源许可」 — does the list need an extra row?

**Current state** (applied by the other agent, read-only inspection):
`source\MechrevoLite\AboutLicensesForm.cs:32-51` renders exactly three groups —
*Derived*: G-Helper, irusanov/ZenStates-Core, irusanov/SMUDebugTool;
*Shipped binary*: PawnIO.Modules `RyzenSMU.bin`;
*Reference (no code copied)*: leogx9r/ryzen_smu, FlyGoat/RyzenAdj,
JamesCJ60/Universal-x86-Tuning-Utility, kitsunyan/intel-undervolt, tomups/watercooler-manager.
String keys `AboutLicenses*` exist in both `.resx` at
`source\MechrevoLite.Properties.Strings.resx:5111-5165` (en) and
`…Strings.zh-CN.resx:5089-5143` (zh-CN).

**Answer: yes — exactly one extra row is warranted, plus two re-wordings.**

1. **New row (recommended).** *Derived* group, between G-Helper and ZenStates-Core:

   | field | value |
   |---|---|
   | Name | `G-Helper — AMD SMU layer (app/Pawn/RyzenSmu.cs)` |
   | Licence | `GPL-3.0` |
   | URL | `https://github.com/seerge/g-helper` |
   | Use string (new key) | `AboutLicensesUseGHelperSmu` — wording below |

   The existing G-Helper row only says "the application as a whole", which does not tell the reader
   that the **CPU undervolt / OC / power / thermal** code specifically is G-Helper's SMU layer.
   With this row the CO attribution is visible in-product, which is what the user asked for.

2. **Re-word `AboutLicensesUseZenStatesCore`** (it currently claims ZenStates-Core only for
   "AMD SMU command IDs, the RSMU mailbox address groups, the 6-argument packing and the transaction
   sequence used by the **FMax** feature" — an understatement after this audit).

3. **Promote the RyzenAdj row out of "Reference (no code copied)"** — ⚠️ this one is a *decision for
   the user* (see §8 note b). Minimal alternative if the maintainer prefers not to move it: keep the
   row where it is and fix its wording to say the iGPU-curve and OC values/fallback order originate
   there.

### 6.1 Exact English wording (for `source\MechrevoLite.Properties.Strings.resx`)

New key `AboutLicensesUseGHelperSmu`:

```xml
    <data name="AboutLicensesUseGHelperSmu" xml:space="preserve">
    <value>The AMD SMU command layer used by CPU undervolting (Curve Optimizer), the iGPU curve, the fixed-frequency overclock mode, the power limits and the thermal limit. Our table uses the same command IDs, the same channels and the same call order as G-Helper's RyzenSmu.cs.</value>
  </data>
```

Reworded `AboutLicensesUseZenStatesCore`:

```xml
    <value>AMD SMU command IDs and mailbox address groups, the FMax (PBO boost limit) tables, the per-core Curve Optimizer write and read-back command tables, the OC-mode and power/thermal limit tables, and the 6-argument mailbox transaction sequence.</value>
```

Reworded `AboutLicensesUseRyzenAdj` (keeps the reference group wording honest; if the row is moved to
the derived group, prefix "Values and channel-fallback order for the iGPU curve and the fixed-frequency
overclock mode, cross-checked against RyzenAdj; " — otherwise use:)

```xml
    <value>AMD SMU reference: the Curve Optimizer / iGPU-curve / overclock / power / thermal command values and their MP1-to-PSMU fallback order were cross-checked against this project.</value>
```

### 6.2 Exact 简体中文 wording (for `source\MechrevoLite.Properties.Strings.zh-CN.resx`)

New key `AboutLicensesUseGHelperSmu`:

```xml
    <data name="AboutLicensesUseGHelperSmu" xml:space="preserve">
    <value>CPU 降压（Curve Optimizer / 负压）、核显曲线、固定全核频率超频、功耗墙与温度墙所用的 AMD SMU 命令层。我们的命令 ID、通道与调用顺序与 G-Helper 的 RyzenSmu.cs 相同。</value>
  </data>
```

Reworded `AboutLicensesUseZenStatesCore`:

```xml
    <value>AMD SMU 命令 ID 与邮箱地址组、FMax（PBO 最大频率）表、每核 Curve Optimizer 的写入与回读命令表、超频模式与功耗/温度墙命令表，以及 6 参数邮箱事务时序。</value>
```

Reworded `AboutLicensesUseRyzenAdj`:

```xml
    <value>AMD SMU 参考：Curve Optimizer / 核显曲线 / 超频 / 功耗 / 温度的命令取值与"MP1 失败再试 PSMU"的兜底顺序对照过本项目。</value>
```

> ⚠️ Adding the new key requires a matching `internal static string AboutLicensesUseGHelperSmu => …`
> accessor in `source\MechrevoLite.Properties\Strings.cs` (pattern: the existing
> `AboutLicensesUseGHelper` accessor) and one entry in the `Derived` array of
> `AboutLicensesForm.cs:32-37`. **All three files are owned by the other agent** — this section is a
> proposal, not an edit.

---

## 7. Conflicts found with the patch that is already in the tree

| # | What is in the tree now | Why it conflicts | Owner action |
|---|---|---|---|
| C1 | `source\PawnIO\RyzenSmuService.cs:7-8` says the command tables (`CurveCommand / Stapm / Fast / Slow / Thm / CO`) and `GetPsmuAddrs` "沿用 L-Mechrevo 自己的 0.290.6 实现，**非第三方代码**" | This audit shows those tables are id-for-id the same as G-Helper's `app/Pawn/RyzenSmu.cs` (byte-identical copy in-repo) and, for CO per-core/read-back, ZenStates-Core's. `0.290.6`'s file is the same table again (`_upstream\0.290.6\decomp\PawnIO\RyzenSmuService.cs:92-269, 459-539`). Under the user's rule ("如果很像的话那就是引用了 也要加") the denial must go | replace with §5.1 text |
| C2 | `THIRD-PARTY-NOTICES.md` S2 scopes ZenStates-Core to the "FMax capability table" only, and S5's G-Helper row does not mention `app/Pawn/RyzenSmu.cs` | CO/OC/power/thermal are absent from the derived inventory — the exact gap this task exists to close | merge §4 block |
| C3 | `source\PawnIO\AboutLicensesForm.cs:34-36` + the two `AboutLicensesUse*` strings | "derived" list describes ZenStates-Core as FMax-only; no in-product statement that the CO layer is G-Helper's | §6 |
| C4 | `ATTRIBUTION-PATCH-PLAN.md` §1.1 (the plan text) carries the same "非第三方代码" sentence | the plan is the source of C1; if the plan is kept as a historical document, add a forward pointer to this addendum rather than silently diverging | informational — **this pass did not edit it** |

Nothing else in the tree needs to change: `LICENSE`, `LICENSES\` and the app-wide GPL-3.0-only
position already cover every licence named here (GPL-3.0 ×3, LGPL-3.0 ×1 — all compatible; the
Copyleft reach does not grow, only the attribution list does).

---

## 8. `could not verify` — honest gaps

| # | Gap | Why it matters / what would close it |
|---|---|---|
| a | **No vendored upstream `.cs` to byte-diff our files against.** The comparison is structural: my quotes are from upstream files whose **git blob SHA-1 I verified** (§1.2), but our file is a re-expression, so "how many tokens were copied" is not measurable. A human decision ("derived table" vs "protocol reference") rests on the id/packing/order identity shown in §2. | To close: diff our `CurveCommand`/`ClockCommand` blocks against `ghelper_RyzenSmu.cs` (the byte-identical upstream copy is already in the repo) line by line. |
| b | **RyzenAdj (LGPL-3.0) status is a judgement call, not a fact I can settle.** The values `0x64`, `0xB7`, `0x2F/0x31/0x30 → 0x19/0x1D`, `0x4F/0x3E/0x5F`, `0x14/0x15/0x16` are all present in RyzenAdj `lib/api.c` **and** in G-Helper's file (which cites RyzenAdj). If the maintainer reads the copied *expression* as G-Helper's, RyzenAdj stays a reference; if as RyzenAdj's, the row should move to `derived`. Either way there is **no licence blocker** (LGPL-3.0 → GPL-3.0-only is compatible), and LGPL-3.0 §3 would allow relicensing the copied part under GPLv3. **This is the user's call**, flagged in §4/§6. |
| c | **UXTU (`JamesCJ60/Universal-x86-Tuning-Utility`, GPL-3.0) is cited only inside G-Helper's comment** (`// UXTU Socket_FT6_FP7_FP8: set-cogfx false 0xB7 — PSMU`, `ghelper_RyzenSmu.cs:199`). I did **not** locate the corresponding UXTU source line (the string "Socket_FT6_FP7_FP8" is UXTU's naming, and searching UXTU's tree for the `set-cogfx` table was out of this pass's budget). Treat UXTU as a courtesy credit; do not claim a verified id match. |
| d | **Zen1/Zen+ OC row `0x23/0x39/0x24` is unexplained** (§2.5). It is `not found upstream` at master and it coincides with ZenStates' `AcBtcStartCal/AcBtcStopCal`. Possible explanations I could not distinguish: an older ZenStates/SMUDebugTool revision, a RyzenAdj-era table, or a transcription error. **Do not "fix" it in this pass** — only the Zen1 Desktop family reaches it, and changing an unmeasured id is out of scope. |
| e | **Matisse vs Vermeer thermal id.** `CpuFamily.Matisse` in our code covers Matisse **and** Vermeer, and the row uses `0x3E` (upstream Zen3). Upstream Zen2 (genuine Matisse) is `0x3D` (`Zen2Settings.cs:55`). Which one a real 3000-series desktop needs was not measured here. |
| f | **`SetCoGfx` on Phoenix/StrixHalo sends `0xB7`, upstream ZenStates writes `0x1F` there.** We are following G-Helper/RyzenAdj (RyzenAdj refused `0xB7` on StrixHalo). Whether `0xB7` actually works on Phoenix/Strix was not verifiable from the repo (the code's own comment at `CpuInfo.cs:484-514` says only "keep the whitelist in sync"). Only a hardware test would settle it — **not run in this pass** (no builds, no hardware). |
| g | **`SMUDebugTool` (GPL-3.0) is not the proximate source of any CO value I could find.** Its CO page calls ZenStates-Core's `GetPsmMarginSingleCore` (per `修改日志.txt:6086-6088`). It stays credited as the tool that *demonstrated* the read path; if the maintainer wants it named under CO (not only under FMax), say "reference implementation used to verify the CO read-back route". |
| h | **`ryzen_smu` / coreboot headers: no copied expression found for CO.** They remain protocol references. I did not find any CO/OC command value that appears *only* in `rsmu_commands.md` (that document has no `GetDldoPsmMargin`/`SetAllDldoPsmMargin` rows at all) — so the §2.3 GPL-2.0 risk note in `THIRD-PARTY-NOTICES.md` is unaffected by this addendum. |
| i | **Line numbers drift while another agent edits `RyzenSmuService.cs`.** The numbers here were correct at snapshot time (66 787 B / 1 555 lines). Anchors are text; re-locate by the quoted line, not the number. |
| j | Our own `Rule` for the CI/AI-generated `--z__ReadOnlyArray.cs` / `--y__InlineArray19.cs` files in `source\` was **not** examined (not SMU-related). |

### 8.1 What I fetched but did **not** use (available if a second pass is wanted)

All under `%TEMP%\co-audit\` (outside the repo, deletable): the 69 verified ZenStates-Core files, the
G-Helper `app/Pawn/RyzenSmu.cs`, RyzenAdj `lib/api.c` + `lib/nb_smu_ops.c`, and the GitHub tree JSONs
for g-helper / RyzenAdj / ZenStates-Core. Notably unused: `Hardware/Smu/Commands/{SetCurveShaperMargin,
GetCurveShaperMargin,SetGpuPsmMargin,GetGpuPsmMargin,SetOcMode,SetFrequencyAllCore}.cs` and
`Hardware/Smu/Settings/BristolRidgeSettings.cs` (Bristol/Curve-Shaper paths we do **not** implement —
their absence from our tree is itself evidence we did not copy those parts).

---

## 9. Files this pass wrote / touched

* **Created**: `D:\openLM\ATTRIBUTION-CO-ADDENDUM.md` (this file) — the only write.
* **Read only** (no modification): `source\PawnIO\RyzenSmuService.cs`, `source\PawnIO\CpuInfo.cs`,
  `source\PawnIO\FMaxCapability.cs`, `source\MechrevoLite.Hardware\RuntimeCpuTuning.cs`,
  `source\MechrevoLite\CpuTuningForm.cs`, `source\MechrevoLite\SmuCoRoundtrip.cs`,
  `source\MechrevoLite\AboutLicensesForm.cs`, `source\MechrevoLite.Mode\ModeControl.cs`,
  `source\MechrevoLite.Properties.Strings.resx` / `.zh-CN.resx`,
  `THIRD-PARTY-NOTICES.md`, `ATTRIBUTION-PATCH-PLAN.md`, `修改日志.txt`,
  `diagnostics\smudbg-reference-20261005\key-files\*`, `_upstream\0.290.6\decomp\PawnIO\RyzenSmuService.cs`.
* **Not touched** (as instructed): everything under `source\` (no source file created or edited),
  the two `Strings*.resx`, `Strings.cs`, `SettingsForm.cs`, `installer-src\**`, `修改日志.txt`,
  `backup\`, `archive\`, `_upstream\`, `diagnostics\`. No build, no hardware access, no network
  write to the repo (temp downloads went to `%TEMP%\co-audit\` only).
