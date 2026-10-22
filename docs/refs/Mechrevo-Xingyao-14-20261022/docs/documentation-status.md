# Documentation status

The reference covers the identified P916F-STX investigation, not every firmware option or hardware behavior. Evidence classification and source availability are separate; see [methodology](reverse-engineering-methodology.md#evidence-classification). Unresolved behavior is recorded in [open questions](open-questions.md). This register identifies the source material needed to extend coverage without inventing results or repeating hardware experiments.

`NEEDS_SOURCE_EXPORT` means the source has been identified but its full text is unavailable here. `SOURCE_LOCATED` means the exact Project/Library object is known but its raw bytes could not be re-exported in the current audit. `SOURCE_RECOVERED` means an identified source or capture has been recovered sufficiently to support the bounded claim stated here. `NEEDS_EVIDENCE` means the required capture or artifact has not been supplied. `NEEDS_CONFIRMATION` means source association or a decision remains unresolved. `ADMIN_PENDING` concerns repository permissions or settings rather than technical evidence.

## Pending evidence

Detailed source locators, hashes and artifact identities are maintained in [research sources](research-sources.md) and the [artifact registry](research-artifacts.md). This page tracks only closure state and the evidence still needed.

| ID | Topic and status | Remaining gap / result | Reference |
|---|---|---|---|
| P01 | Full option audit — CLOSED | Full S2 audit imported: 8 formsets, 28 reachability rows, 151 SetupUtility questions/actions, 204 PBS controls and 416 CBS controls. Static presence/defaults do not establish live behavior. | [Setup options](bios-setup-options.md) |
| P02a | THMM source coverage — CLOSED | Complete S3 THMM and query bodies recovered and published. | [Thermal interfaces](thermal-performance.md) |
| P02b | Complete ACPI identity — PARTIAL | The September ACPI extraction/header capture is retained. Original `dsdt.dat` bytes/hash and exact BIOS association remain missing. | [ACPI/WMI](acpi-wmi.md), [sources](research-sources.md#project-sources) |
| P03 | Earlier SPFM mapping — NEEDS_CONFIRMATION | April source confirms `0x91`/`0x92`; exact same-session firmware identity remains unresolved. Do not merge it with the September `0x94`/`0x95` map. | [Thermal interfaces](thermal-performance.md) |
| P04a | Live GFNS/H2RAM correlation — CLOSED | Live GFNS results correlate with both H2RAM FNS fields. Raw values are not independently calibrated RPM. | [Thermal interfaces](thermal-performance.md) |
| P04b | Live Fn+X / GPFM transition — NEEDS_EVIDENCE | Original invocation, output and initial conditions for the reported profile transition are still missing. | [Thermal interfaces](thermal-performance.md) |
| P05 | EC tachometer/PWM/target tables — NEEDS_EVIDENCE | Need exact EC source context and decoding rationale for any tach/PWM/target table claim. ALIB parameters are not RPM tables. | [EC](embedded-controller.md), [thermal interfaces](thermal-performance.md) |
| P06 | Decoded BMOF schema — NEEDS_EVIDENCE | Need a complete decode with class/method declarations and tool context. AML framing alone is insufficient. | [ACPI/WMI](acpi-wmi.md) |
| P07 | Complete flashrom failure — NEEDS_EVIDENCE | Need the original command, terminal result, tool version and environment. | [Firmware access](firmware-access.md) |
| P08 | PSP attributes — NEEDS_EVIDENCE | Need the original multi-attribute capture for anti-rollback, RPMC and PSP versions. The retained ROM Armor result does not establish those fields. | [Firmware access](firmware-access.md) |
| P09 | H2OFFT execution and IHISI versions — NEEDS_EVIDENCE | Need live invocation/output and utility/onboard IHISI version readings associated with the acquired image. | [Tooling](reproduction-tooling.md) |
| P10 | Protected regions and DXE equality — NEEDS_EVIDENCE | Need original region output and an identified raw/updater slice comparison. | [Firmware access](firmware-access.md), [artifacts](research-artifacts.md) |
| P11 | Exact boot GIF extraction — NEEDS_EVIDENCE | Need the identified resource bytes or a complete extraction record. Existing geometry/frame metadata remains valid. | [Boot graphics](boot-logo-research.md) |
| P12 | Live setup overlay — NEEDS_EVIDENCE | Need original variable captures beyond Quiet Boot with VarStore/GUID/offset/width identity. | [Setup options](bios-setup-options.md) |
| P13 | Successful SREP configuration — NEEDS_CONFIRMATION | The revealed Boot-page photograph and candidate configs are recovered; exact patcher build, final config and session association remain unproven. | [Runtime visibility](srep-runtime-reveal.md) |
| P14 | Speaker layout and OEM tuning — PARTIAL | Nahimic/A-Volute application/APO evidence is recovered. Physical driver count, complete DSP graph, amplifier programming and wiring remain unresolved. | [Audio](audio.md) |
| P15a | Device/audio enumeration — CLOSED | S11 establishes V4L2 entries, microphone endpoints, ALSA/PCI bindings, loaded modules and PipeWire observations. Logical endpoints do not establish physical topology. | [Audio](audio.md), [Linux](linux.md) |
| P15b | Remaining platform inventory — PARTIAL | Installed SSD identity is recorded. Panel model/EDID/refresh and adapter identity/rating remain unrecorded. | [Hardware](hardware-platform.md), [Linux](linux.md) |
| P15c | Standard platform profile — NEEDS_EVIDENCE | Need an original `platform_profile` sysfs inspection associated with the documented unit. OEM GPFM does not establish standard ABI exposure. | [Linux](linux.md#standard-profile-portability) |
| P16 | Current raw ROM and EC source objects — REHASH_PENDING | Exact private source objects are located; raw-byte rehash remains pending. | [Artifacts](research-artifacts.md) |
| P17a | Charge-reverse parent bundle — REHASH_PENDING | Exact private parent object is located; raw-byte rehash remains pending. | [Artifacts](research-artifacts.md) |
| P17b | BIOS 1.15 outer archive — NEEDS_EVIDENCE | Need the original outer archive bytes or digest record; the measured nested executable does not identify the parent archive. | [Artifacts](research-artifacts.md) |


## Reference checks

P18 is resolved for the external references used in this revision: CC BY 4.0 legal terms, deed and licensing-scope guidance were compared; Linux power-supply unit definitions were checked against the sysfs ABI and the retained telemetry. These checks do not establish hardware compatibility, current kernel support or rights in third-party firmware. See [external references](research-sources.md).

## Administration

| ID | Topic | State |
|---|---|---|
| P19 | Description, topics and repository features | Description and 14 relevant topics are present. Issues remain enabled. Wiki was disabled after the public endpoint exposed no content, keeping repository documentation canonical under version control. Projects remains enabled because its usage was not established with the available project-inspection scope; no Projects setting was changed. |
| P20 | Optional branch protection | NEEDS_CONFIRMATION. `main` was observed unprotected. No protection, approval requirement or mandatory CI was introduced without an agreed owner workflow. |

Documentation commits and snapshot tags identify the text revision, not completion of the pending research. Publication identity is recorded in Git history and the delivery report rather than a self-referential commit hash in this file.
