> [!] RETRACTION 2026-10-09 110:00 - READ THIS FIRST
> This plan contains one statement that was later PROVEN FALSE: the claim that our SMU command
> tables (CurveCommand / Stapm / Fast / Slow / Thm / CO) and GetPsmuAddrs are "not third-party code".
> They are derived from seerge/g-helper app/Pawn/RyzenSmu.cs (GPL-3.0), irusanov/ZenStates-Core (GPL-3.0)
> and FlyGoat/RyzenAdj (LGPL-3.0). The corrected text is already in the tree:
>   source/PawnIO/RyzenSmuService.cs:7-22  (derived-work notice, retracts the old wording)
>   source/PawnIO/RyzenSmuService.cs:192-284 (consolidated per-group provenance block)
>   THIRD-PARTY-NOTICES.md rows S2 (widened) / S6 / S7 / S8, and ATTRIBUTION-CO-ADDENDUM.md
> Treat any contrary sentence further down this file as superseded.

# ATTRIBUTION-PATCH-PLAN — in-code and in-UI marking (NOT APPLIED)

Companion to `THIRD-PARTY-NOTICES.md`. Everything below is a **plan**: no file listed here was
modified. Line numbers refer to the working tree at `D:\openLM` as of 2026-10-10 and will drift —
every edit is therefore given as *"anchor text → exact text to insert"* so it can be re-located after
the concurrent agents finish.

**Do not apply §1 to `source\PawnIO\RyzenSmuService.cs` and do not apply §2.3 to the `.resx` files
while the other agents own them** (see the edit-scope note at the end).

---

## 0. Anchor summary

| # | File | Anchor | Insert before/after | Blocked? |
|---|---|---|---|---|
| 1.1 | `source\PawnIO\RyzenSmuService.cs` | line 1 `using System;` | **before** line 1 | ⛔ another agent |
| 1.2 | `source\PawnIO\RyzenSmuService.cs` | line 936 `// 依据（用户给定的、已从参考实现 SMUDebugTool / ZenStates-Core 研究好的结论，本轮**不重复调研**）：` | **after** | ⛔ another agent |
| 1.3 | `source\PawnIO\FMaxCapability.cs` | line 1 `namespace PawnIO;` | **before** line 1 | ✅ free |
| 1.4 | `source\PawnIO\FMaxCapability.cs` | line 28 `// 地址是 SMN（PCI 配置空间间接访问）地址，与上游 ZenStates-Core 的 RSMU 邮箱是同一组，` | **after** (i.e. after line 29) | ✅ free |
| 1.5 | `source\PawnIO\CpuInfo.cs` | line 1 `using System;` | **before** line 1 | ✅ free |
| 1.6 | `source\L-Mechrevo.csproj` | line 25 `<EmbeddedResource Include="LMechrevo.RyzenSMU.bin" LogicalName="LMechrevo.RyzenSMU.bin" />` | **after** | ✅ free |
| 1.7 | `source\LMechrevo.RyzenSMU.bin.NOTICE.txt` | — | **new file** | ✅ free |
| 1.8 | `source\Install-PawnIO.ps1` | — | **restore file** | ✅ free |
| 2.3 | `source\MechrevoLite.Properties.Strings.resx` | line 5099 `</root>` | **before** | ⛔ another agent |
| 2.3 | `source\MechrevoLite.Properties.Strings.zh-CN.resx` | line 5006 `</root>` | **before** | ⛔ another agent |
| 2.3 | `source\MechrevoLite.Properties\Strings.cs` | line 1612 `}` (last property ends line 1611) | **before** | ✅ free |
| 2.4 | `source\MechrevoLite\AboutLicensesForm.cs` | — | **new file** | ✅ free |
| 2.5 | `source\MechrevoLite\SettingsForm.cs` | lines 7551-7554 `private void LabelVersion_Click(...)` body | **replace** | ✅ free-ish |
| 3.1 | `installer-src\files\THIRD_PARTY_NOTICES,1.txt` | line 64 end of file | **append** | ✅ free |
| 3.2 | `installer-src\files\THIRD_PARTY_NOTICES,2.txt` | same, Chinese copy | **append** | ✅ free |
| 3.3 | `installer-src\install_script.iss` | line 62 `Source: "files\THIRD_PARTY_NOTICES,2.txt"; …` | **after** | ✅ free |
| 4.1 | `LICENSE` (repo root) | — | ✅ **done — created** | — |

---

## 1. In-code: "derived from <project> (<licence>, <URL>)" headers

### 1.1 `source\PawnIO\RyzenSmuService.cs` — file header

**Anchor:** the file currently begins immediately with usings (line 1 = `using System;`, line 10 =
`namespace PawnIO;`). Insert **before line 1**:

```csharp
// ==============================================================================================
// L-Mechrevo — AMD SMU / PawnIO service layer.
//
// 本文件属于 L-Mechrevo（GPL-3.0-only，见仓库根 LICENSE）。
//
// 派生声明 / Derived-work notice:
//   * 本文件的 SMU 命令表（CurveCommand / Stapm / Fast / Slow / Thm / CO 等）与 PSMU 邮箱地址表
//     （GetPsmuAddrs）沿用 L-Mechrevo 自己的 0.290.6 实现，非第三方代码。
//     ⚠️ [已更正 2026-10-09 21:31] 上面这一句**已被推翻**，不要照抄：命令表实为派生作品，
//        见本文件顶部的 RETRACTED 说明、source\PawnIO\RyzenSmuService.cs:7-22 与
//        ATTRIBUTION-CO-ADDENDUM.md（第三方清单见 THIRD-PARTY-NOTICES.md S6/S7/S8）。
//   * 本文件的 **FMax（PBO boost limit）** 一部分（命令行 0x42/0x47/0x6E/0x70/0x12/0x86、
//     SMN 邮箱地址组、6 参数打包、事务时序）是在阅读下列 GPL-3.0 参考实现后重写的：
//         irusanov/ZenStates-Core  https://github.com/irusanov/ZenStates-Core  (GPL-3.0)
//         irusanov/SMUDebugTool    https://github.com/irusanov/SMUDebugTool    (GPL-3.0)
//     该部分按 GPL-3.0 分发；完整许可文本见仓库根 LICENSES\GPL-3.0.txt，
//     第三方清单见 THIRD-PARTY-NOTICES.md。
//   * 本文件**不含** namazso/PawnIO 的 C# 代码；命名空间 `PawnIO` 只是本项目的子系统名。
//     随程序分发的 PawnIO.Modules `RyzenSMU.bin` 是 LGPL-2.1-or-later，见 LICENSES\LGPL-2.1.txt。
// ==============================================================================================
```

### 1.2 `source\PawnIO\RyzenSmuService.cs` — the FMax section

**Anchor (line 936):**

```
	// 依据（用户给定的、已从参考实现 SMUDebugTool / ZenStates-Core 研究好的结论，本轮**不重复调研**）：
```

Insert **after** that line (it stays as-is; these lines go directly below it):

```
	//   来源与许可：irusanov/SMUDebugTool（GPL-3.0，https://github.com/irusanov/SMUDebugTool）与
	//   irusanov/ZenStates-Core（GPL-3.0，https://github.com/irusanov/ZenStates-Core）—— 下表的能力
	//   表、Get/Set 命令行、SMN 邮箱地址组与 6 参数打包均据其重写；L-Mechrevo 本身为 GPL-3.0-only，
	//   许可相容。许可文本：LICENSES\GPL-3.0.txt；第三方清单：THIRD-PARTY-NOTICES.md。
```

Also worth adding directly above the address constants (**anchor:** line 964
`// APU 组（= GetPsmuAddrs 里 Raven/Renoir/Mobile/StrixPoint/StrixHalo 那一组）。`), insert before it:

```
	// 地址组与 ZenStates-Core 的 RSMU 邮箱完全同源（GPL-3.0，见本段开头的来源声明）。
```

### 1.3 `source\PawnIO\FMaxCapability.cs` — file header

**Anchor:** line 1 is `namespace PawnIO;` (no usings). Insert **before line 1**:

```csharp
// ==============================================================================================
// L-Mechrevo — FMax（PBO 最大频率 / boost limit）能力模型。
//
// 派生声明 / Derived-work notice: 本文件的 `FMaxSpec` 表（Get/Set 命令行 + SMN 邮箱地址组）
// 与 `FMaxCapability` 分类规则，是在阅读下列 GPL-3.0 参考实现后重写的：
//     irusanov/ZenStates-Core  https://github.com/irusanov/ZenStates-Core  (GPL-3.0)
//     irusanov/SMUDebugTool    https://github.com/irusanov/SMUDebugTool    (GPL-3.0)
// 按 GPL-3.0 分发（L-Mechrevo 自身即为 GPL-3.0-only）。
// 许可文本：LICENSES\GPL-3.0.txt · 第三方清单：THIRD-PARTY-NOTICES.md
// ==============================================================================================
```

### 1.4 `source\PawnIO\FMaxCapability.cs` — inline, at the existing citation

**Anchor (lines 28-29):**

```
	// 地址是 SMN（PCI 配置空间间接访问）地址，与上游 ZenStates-Core 的 RSMU 邮箱是同一组，
	// 也就是本仓库**已有的 PSMU 邮箱通路**（SendPsmuRaw -> MailboxRaw 用的就是这些寄存器语义）：
```

Insert **after line 29**:

```
	//     ZenStates-Core 为 GPL-3.0（https://github.com/irusanov/ZenStates-Core），与本项目
	//     GPL-3.0-only 相容；许可文本见 LICENSES\GPL-3.0.txt。
```

### 1.5 `source\PawnIO\CpuInfo.cs` — file header (short)

**Anchor:** line 1 = `using System;`. Insert **before line 1**:

```csharp
// ==============================================================================================
// L-Mechrevo — CPU 代号/能力判据。本文件是 L-Mechrevo 自己的代码，不派生自第三方项目；
// 它调用的 SMU 命令表在 source\PawnIO\RyzenSmuService.cs，其 FMax 部分的第三方来源见该文件头部。
// ==============================================================================================
```

### 1.6 `source\L-Mechrevo.csproj` — mark the embedded module

**Anchor (line 25):**

```xml
    <EmbeddedResource Include="LMechrevo.RyzenSMU.bin" LogicalName="LMechrevo.RyzenSMU.bin" />
```

Insert **after** it:

```xml
    <!-- 2026-10-10 合规：LMechrevo.RyzenSMU.bin 是 namazso/PawnIO.Modules 的 RyzenSMU 模块
         （LGPL-2.1-or-later，Copyright (C) 2025 namazso），**原样未修改**地内嵌分发。
         许可文本：LICENSES\LGPL-2.1.txt（= 上游 COPYING）。
         第三方清单：THIRD-PARTY-NOTICES.md · 补丁计划：ATTRIBUTION-PATCH-PLAN.md
         ⚠️ 发布前必须确认本文件对应的上游 tag/版本（见 THIRD-PARTY-NOTICES.md §6.1）。 -->
```

### 1.7 `source\LMechrevo.RyzenSMU.bin.NOTICE.txt` — new file (binary can't hold a comment)

Create `source\LMechrevo.RyzenSMU.bin.NOTICE.txt`:

```
LMechrevo.RyzenSMU.bin
======================

This file is the PawnIO RyzenSMU module, embedded into L-Mechrevo as a managed resource
(L-Mechrevo.csproj) and therefore redistributed as part of L-Mechrevo.

Upstream : https://github.com/namazso/PawnIO.Modules
Licence  : GNU Lesser General Public License v2.1 or later (LGPL-2.1-or-later)
Copyright: (C) 2025 namazso <admin@namazso.eu>
Licence text: ../LICENSES/LGPL-2.1.txt  (= upstream COPYING, byte-identical)
Module source: see the upstream repository at the tag matching this binary.

This copy is unmodified. SHA-256 of this file:
  B84ECA7F32C63B3D8C14B2C6D45482706DF8683AA6F43EB8BEAD9DC62181D38F

FMax / SMU command tables in ../source/PawnIO/RyzenSmuService.cs additionally derive from
irusanov/ZenStates-Core and irusanov/SMUDebugTool (GPL-3.0). See ../THIRD-PARTY-NOTICES.md.
```

### 1.8 `source\Install-PawnIO.ps1` — restore the missing driver-installer script

Not a licence edit, but it is the file that keeps the GPL-2.0 PawnIO **driver** out of our bundle
(so we incur no driver redistribution duty). It is currently missing while
`source\MechrevoLite\CpuTuningForm.cs:1710` still launches it.

```powershell
Copy-Item -LiteralPath 'D:\openLM\_upstream\0.290.6\extract\{app}\Install-PawnIO.ps1' `
          -Destination 'D:\openLM\source\Install-PawnIO.ps1'
```

Then: (a) bump the script's `$expectedHash` (`:5`) after re-checking the current
`namazso/PawnIO.Setup` release, and update the URL on `:12` to match; (b) ship it via
`installer-src\install_script.iss` (see §3.3); (c) add a header note that the driver is downloaded,
not bundled, so it is deliberately **absent** from our redistribution.

---

## 2. In-UI: 「关于 / 致谢 / 开源许可」 surface

### 2.1 What exists today (survey result)

* **There is no about page, about box, credits tab or licence dialog anywhere in the app.**
  A repo-wide search for `关于` / `致谢` / `开源` / `AboutBox` in `source\` returns only two unrelated
  comments in `SettingsForm.cs` (lines 5581, 5627). `Strings.resx` has **no** key containing
  `Licen` / `Credit` / `Acknowledg` / `ThirdParty` / `About`.
* The only in-app "about-ish" affordances are:
  * `SettingsForm.labelVersion` — declared `SettingsForm.cs:706`, already styled as a clickable
    underlined link (`Cursor = Cursors.Hand` at `:10656`, underlined font at `:10658`), re-styled in
    the modern footer at `:12343-12346`, wired at `:3833` → `labelVersion.Click += LabelVersion_Click;`,
    text set at `:3602` from `Program.ReleaseLabel`/`Program.ReleaseVersion`. **Its handler currently
    only pops a QA message box** (`LabelVersion_Click`, `:7551-7554` → `Strings.TestFeedback`).
  * The **installer's Start-Menu shortcut** `开源许可 (GPL-3.0)` → `{app}\LICENSE.txt`
    (`installer-src\install_script.iss:547`). That is outside the app, so GPLv3 §0/§5d
    "Appropriate Legal Notices **in the interface**" is not yet satisfied inside the product.
* The footer layout that would host a new button (`BuildFooterV2`, `:12330-12406`) uses a
  7-column `BufferedTableLayoutPanel` whose **every column is already taken**:
  `ColumnCount = 7` (`:12350`), styles at `:12356-12360`, and
  `Controls.Add` for col 0 `labelVersion` (`:12362`) then cols 1-6 = `buttonOverlay`, settings gear,
  `buttonUpdates`, `buttonDonate`, `diagnosticButton`, `buttonQuit` (`:12398-12403`).

### 2.2 Recommended approach (Option A — one method body)

Reuse `labelVersion` as the entry point. It is **already** a hand-cursor link with a Click handler, so
this needs no layout change and cannot collide with the footer geometry work.

**Edit — `source\MechrevoLite\SettingsForm.cs`, replace lines 7551-7554:**

```csharp
	private void LabelVersion_Click(object? sender, EventArgs e)
	{
		ShowTestFeedback();
	}
```

with:

```csharp
	private void LabelVersion_Click(object? sender, EventArgs e)
	{
		// 2026-10-10 licence: 版本号改成「关于 / 开源许可」入口（GPLv3 §0/§5d 的 Appropriate Legal
		// Notices 必须在界面里可达）。原来的 QA「测试反馈」弹窗保留在 Ctrl+点击 上，功能不丢。
		if ((ModifierKeys & Keys.Control) == Keys.Control)
		{
			ShowTestFeedback();
			return;
		}
		using AboutLicensesForm aboutLicensesForm = new AboutLicensesForm();
		AddOwnedForm(aboutLicensesForm);
		aboutLicensesForm.ShowDialog(this);
	}
```

> ⚠️ **Decision for the user:** this changes what a plain click on the version number does. If any QA
> flow or audit script (`MechrevoLite.UI\UiAuditRunner.cs`) depends on the version label opening
> `Strings.TestFeedback`, pick Option B instead.

### 2.3 Required strings — insert into all three files

Add these `<data>` blocks immediately **before** the closing `</root>` of each `.resx`, and the matching
properties **before** the final `}` of `Strings.cs`:

* `source\MechrevoLite.Properties.Strings.resx` → `</root>` is at **line 5099** (last entry ends 5098)
* `source\MechrevoLite.Properties.Strings.zh-CN.resx` → `</root>` is at **line 5006** (last entry ends 5005)
* `source\MechrevoLite.Properties\Strings.cs` → file ends at **line 1612** `}` (last property on 1611)

**English — `source\MechrevoLite.Properties.Strings.resx`:**

```xml
    <data name="AboutLicensesTitle" xml:space="preserve">
    <value>About L-Mechrevo — open-source licences and credits</value>
  </data>
    <data name="AboutLicensesIntro" xml:space="preserve">
    <value>L-Mechrevo is free software, distributed under the GNU General Public License version 3 only. It contains code derived from the projects below, and it ships one third-party binary module. The full licence texts are in the LICENSES folder next to the program, and in LICENSE.txt / THIRD_PARTY_NOTICES.txt.</value>
  </data>
    <data name="AboutLicensesCopyright" xml:space="preserve">
    <value>Copyright (C) 2026 L-Mechrevo contributors</value>
  </data>
    <data name="AboutLicensesNoWarranty" xml:space="preserve">
    <value>This program comes with ABSOLUTELY NO WARRANTY. It is free software, and you are welcome to redistribute it under the terms of the GNU General Public License version 3 only. See LICENSE.txt for the full terms.</value>
  </data>
    <data name="AboutLicensesDerivedHeading" xml:space="preserve">
    <value>Code we derived from</value>
  </data>
    <data name="AboutLicensesReferenceHeading" xml:space="preserve">
    <value>Protocol references (no code copied)</value>
  </data>
    <data name="AboutLicensesShippedHeading" xml:space="preserve">
    <value>Shipped as a binary in this program</value>
  </data>
    <data name="AboutLicensesUseGHelper" xml:space="preserve">
    <value>The application as a whole is derived from G-Helper. That is why L-Mechrevo is licensed under the GNU General Public License version 3 only.</value>
  </data>
    <data name="AboutLicensesUseZenStatesCore" xml:space="preserve">
    <value>AMD SMU command IDs, the RSMU mailbox address groups, the 6-argument packing and the transaction sequence used by the FMax (PBO boost limit) feature.</value>
  </data>
    <data name="AboutLicensesUseSmudbg" xml:space="preserve">
    <value>Reference implementation used to research the FMax commands alongside ZenStates-Core.</value>
  </data>
    <data name="AboutLicensesUsePawnIOModules" xml:space="preserve">
    <value>The RyzenSMU module (RyzenSMU.bin) is embedded unmodified in L-Mechrevo.dll and provides the kernel-mediated SMU mailbox access used for CPU tuning.</value>
  </data>
    <data name="AboutLicensesUseRyzenSmu" xml:space="preserve">
    <value>Community RSMU command tables, used as a documented protocol reference.</value>
  </data>
    <data name="AboutLicensesUseRyzenAdj" xml:space="preserve">
    <value>AMD SMU command reference.</value>
  </data>
    <data name="AboutLicensesUseUxtu" xml:space="preserve">
    <value>Additional SMU route reference.</value>
  </data>
    <data name="AboutLicensesUseIntelUndervolt" xml:space="preserve">
    <value>Intel OC Mailbox (MSR 0x150) protocol reference for the Intel tuning path; no code copied.</value>
  </data>
    <data name="AboutLicensesUseWatercooler" xml:space="preserve">
    <value>Bluetooth LE liquid-cooler GATT service and command layout, used as a protocol reference for an independent implementation.</value>
  </data>
    <data name="AboutLicensesOpenFolder" xml:space="preserve">
    <value>Open the LICENSES folder</value>
  </data>
    <data name="AboutLicensesFolderMissing" xml:space="preserve">
    <value>The LICENSES folder was not found next to the program. Licence texts: {0}</value>
  </data>
    <data name="AboutLicensesClose" xml:space="preserve">
    <value>Close</value>
  </data>
```

**简体中文 — `source\MechrevoLite.Properties.Strings.zh-CN.resx`:**

```xml
    <data name="AboutLicensesTitle" xml:space="preserve">
    <value>关于 L-Mechrevo —— 开源许可与致谢</value>
  </data>
    <data name="AboutLicensesIntro" xml:space="preserve">
    <value>L-Mechrevo 是自由软件，以 GNU 通用公共许可证第 3 版（GPL-3.0-only）分发。本程序包含派生自下列项目的代码，并内嵌了一个第三方二进制模块。完整许可文本位于程序目录下的 LICENSES 文件夹，以及 LICENSE.txt / THIRD_PARTY_NOTICES.txt。</value>
  </data>
    <data name="AboutLicensesCopyright" xml:space="preserve">
    <value>版权所有 (C) 2026 L-Mechrevo contributors</value>
  </data>
    <data name="AboutLicensesNoWarranty" xml:space="preserve">
    <value>本程序绝对不提供任何担保。它是自由软件，欢迎你在 GNU 通用公共许可证第 3 版的条款下再分发。完整条款见 LICENSE.txt。</value>
  </data>
    <data name="AboutLicensesDerivedHeading" xml:space="preserve">
    <value>我们参考/派生自的代码</value>
  </data>
    <data name="AboutLicensesReferenceHeading" xml:space="preserve">
    <value>协议参考（未复制代码）</value>
  </data>
    <data name="AboutLicensesShippedHeading" xml:space="preserve">
    <value>以二进制形式随本程序分发</value>
  </data>
    <data name="AboutLicensesUseGHelper" xml:space="preserve">
    <value>本程序整体派生自 G-Helper，这也是 L-Mechrevo 采用 GNU 通用公共许可证第 3 版（仅此版本）的原因。</value>
  </data>
    <data name="AboutLicensesUseZenStatesCore" xml:space="preserve">
    <value>FMax（PBO 最大频率 / boost limit）功能所用的 AMD SMU 命令 ID、RSMU 邮箱地址组、6 参数打包方式与事务时序。</value>
  </data>
    <data name="AboutLicensesUseSmudbg" xml:space="preserve">
    <value>与 ZenStates-Core 一同用于调研 FMax 命令的参考实现。</value>
  </data>
    <data name="AboutLicensesUsePawnIOModules" xml:space="preserve">
    <value>RyzenSMU 模块（RyzenSMU.bin）以**未经修改**的形式内嵌在 L-Mechrevo.dll 中，为 CPU 调节提供经内核转发的 SMU 邮箱访问。</value>
  </data>
    <data name="AboutLicensesUseRyzenSmu" xml:space="preserve">
    <value>社区整理的 RSMU 命令表，作为有文档的协议参考。</value>
  </data>
    <data name="AboutLicensesUseRyzenAdj" xml:space="preserve">
    <value>AMD SMU 命令参考。</value>
  </data>
    <data name="AboutLicensesUseUxtu" xml:space="preserve">
    <value>额外的 SMU 通路参考。</value>
  </data>
    <data name="AboutLicensesUseIntelUndervolt" xml:space="preserve">
    <value>Intel 调节通路的 OC 邮箱（MSR 0x150）协议参考；未复制其代码。</value>
  </data>
    <data name="AboutLicensesUseWatercooler" xml:space="preserve">
    <value>蓝牙低功耗水冷器的 GATT 服务与命令布局，作为独立实现的协议参考。</value>
  </data>
    <data name="AboutLicensesOpenFolder" xml:space="preserve">
    <value>打开 LICENSES 文件夹</value>
  </data>
    <data name="AboutLicensesFolderMissing" xml:space="preserve">
    <value>没有在程序目录下找到 LICENSES 文件夹。许可文本：{0}</value>
  </data>
    <data name="AboutLicensesClose" xml:space="preserve">
    <value>关闭</value>
  </data>
```

**Accessors — `source\MechrevoLite.Properties\Strings.cs`, insert before the final `}` (line 1612),
matching the existing one-line pattern (e.g. `:466` `internal static string DonateThanks => ResourceManager.GetString("DonateThanks", resourceCulture);`):**

```csharp
	/// <summary>2026-10-10 licence: 「关于 / 开源许可」窗口标题。</summary>
	internal static string AboutLicensesTitle => ResourceManager.GetString("AboutLicensesTitle", resourceCulture);

	/// <summary>2026-10-10 licence: 「关于 / 开源许可」窗口开头说明。</summary>
	internal static string AboutLicensesIntro => ResourceManager.GetString("AboutLicensesIntro", resourceCulture);

	/// <summary>2026-10-10 licence: 著作权人行 —— 需用户确认后定稿。</summary>
	internal static string AboutLicensesCopyright => ResourceManager.GetString("AboutLicensesCopyright", resourceCulture);

	/// <summary>2026-10-10 licence: GPLv3 §0/§5d 要求的无担保声明。</summary>
	internal static string AboutLicensesNoWarranty => ResourceManager.GetString("AboutLicensesNoWarranty", resourceCulture);

	/// <summary>2026-10-10 licence: 分组标题「我们参考/派生自的代码」。</summary>
	internal static string AboutLicensesDerivedHeading => ResourceManager.GetString("AboutLicensesDerivedHeading", resourceCulture);

	/// <summary>2026-10-10 licence: 分组标题「协议参考（未复制代码）」。</summary>
	internal static string AboutLicensesReferenceHeading => ResourceManager.GetString("AboutLicensesReferenceHeading", resourceCulture);

	/// <summary>2026-10-10 licence: 分组标题「以二进制形式随本程序分发」。</summary>
	internal static string AboutLicensesShippedHeading => ResourceManager.GetString("AboutLicensesShippedHeading", resourceCulture);

	/// <summary>2026-10-10 licence: G-Helper 的用途说明。</summary>
	internal static string AboutLicensesUseGHelper => ResourceManager.GetString("AboutLicensesUseGHelper", resourceCulture);

	/// <summary>2026-10-10 licence: ZenStates-Core 的用途说明。</summary>
	internal static string AboutLicensesUseZenStatesCore => ResourceManager.GetString("AboutLicensesUseZenStatesCore", resourceCulture);

	/// <summary>2026-10-10 licence: SMUDebugTool 的用途说明。</summary>
	internal static string AboutLicensesUseSmudbg => ResourceManager.GetString("AboutLicensesUseSmudbg", resourceCulture);

	/// <summary>2026-10-10 licence: PawnIO.Modules RyzenSMU 模块的用途说明。</summary>
	internal static string AboutLicensesUsePawnIOModules => ResourceManager.GetString("AboutLicensesUsePawnIOModules", resourceCulture);

	/// <summary>2026-10-10 licence: ryzen_smu 的用途说明。</summary>
	internal static string AboutLicensesUseRyzenSmu => ResourceManager.GetString("AboutLicensesUseRyzenSmu", resourceCulture);

	/// <summary>2026-10-10 licence: RyzenAdj 的用途说明。</summary>
	internal static string AboutLicensesUseRyzenAdj => ResourceManager.GetString("AboutLicensesUseRyzenAdj", resourceCulture);

	/// <summary>2026-10-10 licence: UXTU 的用途说明。</summary>
	internal static string AboutLicensesUseUxtu => ResourceManager.GetString("AboutLicensesUseUxtu", resourceCulture);

	/// <summary>2026-10-10 licence: intel-undervolt 的用途说明。</summary>
	internal static string AboutLicensesUseIntelUndervolt => ResourceManager.GetString("AboutLicensesUseIntelUndervolt", resourceCulture);

	/// <summary>2026-10-10 licence: watercooler-manager 的用途说明。</summary>
	internal static string AboutLicensesUseWatercooler => ResourceManager.GetString("AboutLicensesUseWatercooler", resourceCulture);

	/// <summary>2026-10-10 licence: 「打开 LICENSES 文件夹」按钮。</summary>
	internal static string AboutLicensesOpenFolder => ResourceManager.GetString("AboutLicensesOpenFolder", resourceCulture);

	/// <summary>2026-10-10 licence: 找不到 LICENSES 文件夹时的提示，{0} = 备用说明。</summary>
	internal static string AboutLicensesFolderMissing => ResourceManager.GetString("AboutLicensesFolderMissing", resourceCulture);

	/// <summary>2026-10-10 licence: 关闭按钮。</summary>
	internal static string AboutLicensesClose => ResourceManager.GetString("AboutLicensesClose", resourceCulture);
```

### 2.4 New form — `source\MechrevoLite\AboutLicensesForm.cs`

Model it on `source\MechrevoLite\DonateForm.cs`, which is the closest existing pattern: `RForm` base,
`BackColor = UiVisualStyle.Window`, `Text = Strings.<Key>`, a `TableLayoutPanel` with
`Dock = DockStyle.Top` / `AutoSize`, `UiVisualStyle.ApplyWindow(this)`,
`ResponsiveLayout.PerformLayoutTree(this)` + `ResponsiveLayout.ScaleFrom96(this, this)`, and
`Strings.*` for every visible string. Add the usual `Logger.WriteLine` guard around the folder-open
call. Project names, licences and URLs are language-neutral, so they go in as literals; every sentence
comes from `Strings.*`.

The rows it must render (name — licence — what we use it for):

| Group | Rows | Licence column |
|---|---|---|
| Derived | G-Helper · `AboutLicensesUseGHelper` | GPL-3.0 |
| Derived | ZenStates-Core · `AboutLicensesUseZenStatesCore` | GPL-3.0 |
| Reference | SMUDebugTool · `AboutLicensesUseSmudbg` — Protocol / behaviour reference, no copied expression | GPL-3.0 |
| Shipped binary | PawnIO.Modules `RyzenSMU.bin` · `AboutLicensesUsePawnIOModules` | LGPL-2.1-or-later |
| Reference | leogx9r/ryzen_smu · `AboutLicensesUseRyzenSmu` | GPL-2.0 |
| Reference | FlyGoat/RyzenAdj · `AboutLicensesUseRyzenAdj` | LGPL-3.0 |
| Reference | JamesCJ60/Universal-x86-Tuning-Utility · `AboutLicensesUseUxtu` | GPL-3.0 |
| Reference | kitsunyan/intel-undervolt · `AboutLicensesUseIntelUndervolt` | GPL-3.0 |
| Reference | tomups/watercooler-manager · `AboutLicensesUseWatercooler` | GPL-3.0 |

URLs to render verbatim (they are the whole point of the exercise):
`https://github.com/seerge/g-helper`, `https://github.com/irusanov/ZenStates-Core`,
`https://github.com/irusanov/SMUDebugTool`, `https://github.com/namazso/PawnIO.Modules`,
`https://github.com/leogx9r/ryzen_smu`, `https://github.com/FlyGoat/RyzenAdj`,
`https://github.com/JamesCJ60/Universal-x86-Tuning-Utility`, `https://github.com/kitsunyan/intel-undervolt`,
`https://github.com/tomups/watercooler-manager`.

### 2.5 Optional (higher risk) — footer button instead of the version link

Only if the version-link change is rejected. `BuildFooterV2` has no spare column, so this needs:

1. `source\MechrevoLite\SettingsForm.cs:12350` — `ColumnCount = 7` → `8`.
2. `:12356-12360` — add one more `ColumnStyle(SizeType.Percent, 13f)` and re-balance the widths.
3. After `:12366` — `ResetLegacyFooterButton(buttonLicenses, Strings.AboutLicensesTitle, UiGlyph.Kind.Info, scale);`
   (requires a new `buttonLicenses` field near `:706`, and the button instance created/added like
   `diagnosticButton` at `:12382-12403`, e.g. `bufferedTableLayoutPanel.Controls.Add(buttonLicenses, 7, 0);`).
4. `:3632` area — `buttonLicenses.Text = Strings.AboutLicensesTitle;`

`UiGlyph.Kind.Info` already exists (`SettingsForm.cs:948` uses it for `labelVersion`), so no new glyph
assets are needed.

---

## 3. Packaging: make the notices and licence texts actually ship

### 3.1 `installer-src\files\THIRD_PARTY_NOTICES,1.txt` (English) — append after line 64

```
Runtime CPU tuning embeds unmodified signed RyzenSMU.bin from PawnIO.Modules,
Copyright (C) 2025 namazso, LGPL-2.1-or-later.
License: COPYING.PawnIO.txt (or LICENSES\LGPL-2.1.txt).
Corresponding module source and release:
https://github.com/namazso/PawnIO.Modules
The PawnIO kernel driver is GPL-2.0. It is downloaded by the user on explicit request
from the official release and is NOT bundled with L-Mechrevo.
AMD SMU command reference: https://github.com/FlyGoat/RyzenAdj (LGPL-3.0)
                           https://github.com/irusanov/ZenStates-Core (GPL-3.0)
                           https://github.com/irusanov/SMUDebugTool (GPL-3.0)
                           https://github.com/leogx9r/ryzen_smu (GPL-2.0)
Additional SMU route reference: https://github.com/JamesCJ60/Universal-x86-Tuning-Utility (GPL-3.0)
The Bluetooth LE liquid-cooler protocol reference is watercooler-manager
(https://github.com/tomups/watercooler-manager, GPL-3.0).
```

Also **fix line 29**: `(https://github.com/, see watercooler-manager-main/ in this tree)` →
`(https://github.com/tomups/watercooler-manager)`.

> ⚠️ Do **not** copy the previous release's line "unmodified signed … from PawnIO.Modules 0.2.11"
> without re-verifying it — the working tree embeds a *different* module file than 0.290.6 did
> (`THIRD-PARTY-NOTICES.md` §6.1). State the version only once it is confirmed, otherwise omit it.

### 3.2 `installer-src\files\THIRD_PARTY_NOTICES,2.txt` (Chinese) — same, translated

```
运行期 CPU 调节内嵌了来自 PawnIO.Modules 的**未经修改**的已签名 RyzenSMU.bin 模块，
版权所有 (C) 2025 namazso，许可：LGPL-2.1-or-later。
许可文本：COPYING.PawnIO.txt（或 LICENSES\LGPL-2.1.txt）。
模块对应源码与发布：https://github.com/namazso/PawnIO.Modules
PawnIO 内核驱动为 GPL-2.0，由用户显式请求后从官方发布自行下载，**不随本程序分发**。
AMD SMU 命令参考：https://github.com/FlyGoat/RyzenAdj (LGPL-3.0)
                  https://github.com/irusanov/ZenStates-Core (GPL-3.0)
                  https://github.com/irusanov/SMUDebugTool (GPL-3.0)
                  https://github.com/leogx9r/ryzen_smu (GPL-2.0)
其它 SMU 通路参考：https://github.com/JamesCJ60/Universal-x86-Tuning-Utility (GPL-3.0)
蓝牙水冷协议参考：https://github.com/tomups/watercooler-manager (GPL-3.0)
```

### 3.3 `installer-src\install_script.iss` — ship the texts and the restored script

Insert **after line 62** (`Source: "files\THIRD_PARTY_NOTICES,2.txt"; …`):

```ini
; 2026-10-10 合规：内嵌的 RyzenSMU.bin 为 LGPL-2.1-or-later，必须随二进制分发许可文本。
; 0.290.6 曾以 COPYING.PawnIO.txt 之名分发同一文件（见 _upstream\0.290.6\extract\install_script.iss:32），
; 当前 installer-src 漏掉了它，且我方的 PawnIO 归属段落也被删除 —— 两处一并恢复。
Source: "files\COPYING.PawnIO.txt"; DestDir: "{app}"; Flags: ignoreversion
; 2026-10-10：app 仍在调用 {app}\Install-PawnIO.ps1（source\MechrevoLite\CpuTuningForm.cs:1710），
; 但当前 installer-src\files 里没有它 —— 恢复，否则「下载 PawnIO 驱动」按钮静默失败。
Source: "files\Install-PawnIO.ps1"; DestDir: "{app}"; Flags: ignoreversion
```

And add a Start-Menu entry next to line 547 if the source-offer route is "publish/ship source":

```ini
Name: "{group}\第三方许可与开源组件 (THIRD-PARTY-NOTICES)"; Filename: "{app}\THIRD_PARTY_NOTICES.txt";
```

Also copy the two files into the payload directory:

```powershell
Copy-Item -LiteralPath 'D:\openLM\LICENSES\LGPL-2.1.txt' -Destination 'D:\openLM\installer-src\files\COPYING.PawnIO.txt' -Force
Copy-Item -LiteralPath 'D:\openLM\source\Install-PawnIO.ps1' -Destination 'D:\openLM\installer-src\files\Install-PawnIO.ps1' -Force   # after §1.8
```

### 3.4 Verify the packaging actually put them there

After the next build, confirm `{app}\COPYING.PawnIO.txt`, `{app}\THIRD_PARTY_NOTICES.txt`,
`{app}\LICENSE.txt` and `{app}\Install-PawnIO.ps1` all exist, and that
`installer-src\files\L-Mechrevo.dll` still contains the resource name `LMechrevo.RyzenSMU.bin`
(the check used for this audit:

```powershell
$s=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes('D:\openLM\installer-src\files\L-Mechrevo.dll'))
[regex]::Matches($s,'LMechrevo\.[A-Za-z0-9._-]+') | ForEach-Object Value | Sort-Object -Unique
```
→ currently `LMechrevo.gcu-payload-baseline.txt`, `LMechrevo.model-registry.json`,
`LMechrevo.RyzenSMU.bin`).

---

## 4. Repository-level licence files

### 4.1 `LICENSE` at the repo root — ✅ **CREATED** (was missing entirely)

`D:\openLM` had **no top-level `LICENSE`** (and no `.git` either). LM's own GPL-3.0 text existed only
as an installer payload: `installer-src\files\LICENSE,1.txt` / `,2.txt` (both 35149 B, both
byte-identical to G-Helper's `LICENSE` and to SMUDebugTool's `LICENSE.md` — git blob
`f288702d2fa16d3cdf0035b15a9fcbc552cd88e7`). The previous release had the same gap: its
`{app}\LICENSE,1.txt` came from the extracted payload, so the repo itself was not self-describing.

Now created as `D:\openLM\LICENSE` = **546-byte provenance banner + the 35149-byte canonical GPL-3.0
text**. The appended region was verified byte-for-byte against `LICENSES\GPL-3.0.txt`
(`appended body identical: True`, 0 non-ASCII bytes in the whole file).

If a different shape is preferred (e.g. `LICENSE` as the bare canonical text plus a separate
`NOTICE`), regenerate with:

```powershell
# bare canonical text, no banner
Copy-Item -LiteralPath 'D:\openLM\LICENSES\GPL-3.0.txt' -Destination 'D:\openLM\LICENSE' -Force

# or rebuild banner + body with guaranteed byte-exact body
$bannerLines = @(
 'L-Mechrevo is distributed under the GNU General Public License version 3 only (GPL-3.0-only).',
 'It contains code derived from G-Helper (https://github.com/seerge/g-helper), also GPL-3.0-only.',
 'See THIRD-PARTY-NOTICES.md for the complete third-party inventory, LICENSES\ for the upstream',
 'licence texts, and ATTRIBUTION-PATCH-PLAN.md for the in-code / in-UI marking work.','',
 'The canonical GPL-3.0 text follows verbatim (byte-identical to',
 'https://github.com/seerge/g-helper/blob/main/LICENSE, git blob f288702d2fa16d3cdf0035b15a9fcbc552cd88e7).','',''
)
$b = [Text.Encoding]::ASCII.GetBytes(($bannerLines -join "`r`n"))
$l = [IO.File]::ReadAllBytes('D:\openLM\LICENSES\GPL-3.0.txt')
$o = New-Object byte[] ($b.Length + $l.Length)
[Array]::Copy($b,0,$o,0,$b.Length); [Array]::Copy($l,0,$o,$b.Length,$l.Length)
[IO.File]::WriteAllBytes('D:\openLM\LICENSE', $o)
```

> The banner script above deliberately did **not** name a copyright holder. ⚠️[已更正 2026-10-09]
> 定稿版权行已落地：`Copyright (C) 2026 L-Mechrevo contributors` —— 根 `LICENSE`（:`1-2`）、两份 resx
> （`AboutLicensesCopyright`）、`L-Mechrevo.csproj` 内嵌模块注记与 `install_script.iss` 的 `AppCopyright`
> 口径一致；§2.3 里的 `TO BE CONFIRMED` 占位已全部清除（原文保留在上方代码块内，原作者邮箱一并移除）。

### 4.2 Top-level `README` cross-reference

If/when a `README.md` is added, it must link `LICENSE`, `THIRD-PARTY-NOTICES.md`, `LICENSES\` and
`ATTRIBUTION-PATCH-PLAN.md`, and must state where the Corresponding Source can be obtained
(GPLv3 §6 — see `THIRD-PARTY-NOTICES.md` §3).

---

## 5. Apply order and edit-scope

Safe to apply in any order, by anyone, right now (§ files are unowned):

1. §1.3, §1.4 (`FMaxCapability.cs`) — 2 comment insertions.
2. §1.5 (`CpuInfo.cs`), §1.6 (`L-Mechrevo.csproj`), §1.7 (`LMechrevo.RyzenSMU.bin.NOTICE.txt`).
3. §1.8 (`Install-PawnIO.ps1`) + §3.3 + §3.4 (packaging).
4. §3.1, §3.2 (notice texts), §4.1 (`LICENSE`).

**Must wait for the named agent to finish:**

| Edit | Wait because |
|---|---|
| §1.1, §1.2 (`source\PawnIO\RyzenSmuService.cs`) | another agent is completing `GetFamily` in this file |
| §2.3 `.resx` half (`Strings.resx`, `Strings.zh-CN.resx`) | another agent is adding Save/Load/auto-apply strings; both edits append at the same `</root>` anchor — resolve the conflict by concatenating both blocks, keeping each agent's keys |
| §2.2, §2.5 (`SettingsForm.cs`) | touches the footer/version area; verify against whatever lands in `CpuTuningForm.cs` / `RuntimeCpuTuning.cs` first |
| §2.4 (`AboutLicensesForm.cs`) | new file — safe, but its `Strings.*` calls only compile after §2.3 |

**Not to be touched at all:** `backup\`, `*.group_backup`, `*_backup*` (none of the above paths fall
under those).

## 6. Post-apply checks

1. `source\PawnIO\FMaxCapability.cs` and `RyzenSmuService.cs` mention *both* projects by name **with**
   licence and URL — grep for `ZenStates-Core` and confirm the same line/block carries `GPL-3.0` and a
   `github.com` URL.
2. Both `.resx` files parse as XML and contain the same key set (the app's language switch must not
   fall back to the key name); compare key lists, not file order.
3. `Strings.cs` compiles — every `AboutLicenses*` accessor has a matching `<data name=…>` in both resx.
4. The new window is reachable **inside** the app (click the version label) — that is the GPLv3
   "Appropriate Legal Notices" requirement, not the Start-Menu shortcut.
5. The licence-button/link is present in both UI languages.
6. Installer payload contains `COPYING.PawnIO.txt` (or `LICENSES\LGPL-2.1.txt`).
7. `THIRD-PARTY-NOTICES.md`, `installer-src\files\THIRD_PARTY_NOTICES,1.txt` and `,2.txt` describe the
   same component set — diff their component lists.
