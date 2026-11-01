# Reverse-engineering methodology

This document describes how the P916F-STX findings in this repository were established. It preserves the technical methods and the limits of the retained evidence so that another engineer can follow the reasoning without treating a historical workflow as a new hardware procedure.

## 1. Scope and evidence hierarchy

The subject is the documented MECHREVO Xingyao 14 / `P916F-STX`, primarily with system BIOS `1.15`, firmware-reported EC `1.15`, and the retained IT557x internal image string `V1.09`. A result is machine-specific only when its platform, firmware scope and source support are stated.

The investigation uses this order of evidentiary preference:

1. Recorded live behavior on the identified P916F-STX unit.
2. The unit's identified raw 32 MiB ROM artifact.
3. ACPI tables exported from that unit.
4. The P916F-STX BIOS 1.15 updater and its identified members.
5. Exact MECHREVO packages inspected during the investigation.
6. Generic Insyde, ITE, Uniwill, Tongfang or Linux material used only for architectural or interface context.

A generic register, method, option, package label or driver is never promoted to a P916F fact without machine-specific static evidence or a live cross-check. This is why generic `0x07B9` / `0x07D0` offsets remain comparative evidence while the `0x0D13` / `0x0D14` subsystem is treated as machine-specific.

The stable project-source crosswalk is maintained in [`research-sources.md`](research-sources.md#project-sources). The [evidence matrix](evidence-matrix.md) gives every retained baseline claim a stable ID, source coverage and a public detail link.

## Evidence classification

Validation class and availability of the supporting source are independent properties. A historical result is not downgraded merely because this editorial pass did not rerun the machine, and a source excerpt is not promoted to a live result merely because it contains a plausible command.

### Evidence classes

| Class | Definition |
|---|---|
| **Live-confirmed** | A recorded observation or experiment on the identified unit supports the stated behavior or value. |
| **Platform-confirmed** | An identified platform or model specification establishes the stated identity or specification for the documented platform without implying a separate live measurement. |
| **Static-confirmed** | An identified firmware, ACPI, disassembly or source excerpt establishes the stated static property. |
| **Artifact-confirmed** | Size, bytes, structure or digest was checked on an identified artifact. |
| **Inferred** | An interpretation is supported by observations but is not fully established by a direct implementation or behavioral proof. |
| **Comparative only** | Evidence comes from a generic implementation, another platform, or a source whose machine association is incomplete. |
| **Rejected** | The stated hypothesis or path failed under the documented conditions and scope. |
| **Not established** | The retained evidence does not establish the proposition. A scoped absence is recorded as a result, not as a universal claim. |
| **Not tested** | The specified operation or experiment was not performed in the retained record. |

`Platform-confirmed` is retained as a canonical class for identified platform/model specifications. Historical compound descriptions such as `Platform/live-confirmed` and `Static-confirmed + live-correlated` are normalized to the closest canonical class, with the additional support and its boundary recorded in the interpretation column. `Absent` is not a separate class: the matrix states what was absent, where, and what broader conclusion does not follow.

### Source coverage

The matrix uses one of these source-availability descriptions:

- **Full bytes/capture included** — the relevant byte material or complete retained capture is available in the public reference or its included evidence record.
- **Selected excerpt included** — the relevant source lines are included, but the parent file/table or surrounding context is not complete.
- **Retained report only** — a prior report records the result, but the original byte dump, terminal capture or photograph is not included.
- **Source located privately** — the exact source object is known but raw-byte export or public redistribution is unavailable.
- **Source identified but not exported** — the source has a stable identity or File Library locator, but the needed complete content is not in the package.
- **Unverified lead** — a proposed value or locator is retained only to guide later recovery and is not evidence for the claim.

Source coverage never changes the evidence class. For example, a historical live observation can remain `Live-confirmed` with `Retained report only`; an embedded `-g` help string remains `Artifact-confirmed` or `Static-confirmed`, not a live dump.

## 3. Numeric notation

New explanatory prose uses the following convention:

- Hexadecimal addresses, register values, command bytes and byte-stream values use a `0x` prefix and uppercase hex digits where readability matters.
- Decimal quantities carry a unit or context. User-facing thresholds are decimal percentages such as `80%`; they are not hexadecimal byte values.
- A byte stream is written as `0xNN`. Historical labels, source strings, disassembly and captured output are preserved verbatim even when their notation is mixed.
- HII enum values retain the source spelling and base. The table heading or surrounding prose identifies the convention instead of rewriting source values.
- A date string such as `05/07/2026` remains raw when its format is not proven. Collection timestamps identify source files; they are not measurement timestamps.

The battery protocol layout below is explanatory notation, not a command script:

```text
Set T1 = 80%:
  command byte -> I/O 0x6C: 0xF2
  data byte    -> I/O 0x68: 0x50

Set T2 = 100%:
  command byte -> I/O 0x6C: 0xF3
  data byte    -> I/O 0x68: 0x64

Set T1 = 85%:
  command byte -> I/O 0x6C: 0xF2
  data byte    -> I/O 0x68: 0x55

Set T2 = 90%:
  command byte -> I/O 0x6C: 0xF3
  data byte    -> I/O 0x68: 0x5A
```

Historical raw validation labels remain unchanged in [`validation.md`](validation.md#5-threshold-writereadback-while-disabled); the explanatory notation distinguishes decimal arguments from encoded data bytes. This rule prevents decimal `100` from becoming `0x100`, and prevents two bytes written to one port from being presented as the actual transport.

For Linux telemetry, units are explained where used. The earlier `energy_now` values `63154000` and `62661000` are micro-watt-hours, corresponding to a reported decrease of approximately `0.493 Wh`. The later T2-isolation trace moved from approximately `68.116 Wh` to `64.972 Wh`, a reported decrease of `3.144 Wh` while AC remained online. Endpoint averages are explicitly labeled approximate because workload, telemetry quantization and wall-side power were not controlled as calibrated measurements.

## 4. Address-space taxonomy

Every offset or address has a namespace. A literal is retained with an uncertainty note when the source does not establish its address class; it is not assigned a more convenient mapping.

| Address class | Required context |
|---|---|
| Raw-ROM file offset | Parent raw-image identity/digest and byte offset. |
| Updater-image offset | Exact nested image identity/digest and offset within that file. |
| Extracted/decompressed-region offset | Extraction result identity and region-relative offset. |
| FFS-relative offset | Identified FFS file/section and its offset origin. |
| PE file offset | Extracted PE bytes; never label this an RVA. |
| PE RVA | PE image identity and relative virtual address. |
| EC `CODE:` address | Logical instruction address and bank information where required. A banked 16-bit logical address is not automatically a file offset. |
| EC XRAM address | EC memory-space address, distinct from host physical memory. |
| Host physical MMIO | Physical address and the mapping justification. |
| Host I/O port | Port number plus command, data or status role. |
| VarStore offset | Variable/VarStore name, GUID, width and offset origin when retained. |

For example, the DSDT `SystemMemory` aperture `0xFEEC2300..0xFEEC23FF` is host physical MMIO, while `XRAM[0x0300..0x03FF]` is EC XRAM. The candidate I2EC base `0x380` is an I/O-port hypothesis and is not interchangeable with host address `0xFEEC2300`. The SetupUtility landmark near PE file offset `0x2636A0` is not a raw-ROM offset. `CODE:0xDD50` and `CODE:0xEDBA` require the EC image and bank context used by the disassembly.

## 5. Artifact provenance and package interpretation

The primary raw firmware identity retained by the baseline is:

```text
P916F-STX-current-ROM.bin
size:    0x2000000 bytes / 32 MiB
SHA-256: 77043505b6f42e4a482110a7ba0c7e12ba6b1db28fdaed2743c28578bbf76cd7
```

The preferred embedded-controller carve is:

```text
P916F-IT5571-EC-1.09.bin
source:  raw-ROM + 0x081000
length:  0x20000 bytes
SHA-256: 42c117f00c130c5e533be93ee1657401ac4d687255ed1b2250f74d3cc79397ea
```

The BIOS 1.15 package has nested address spaces:

```text
STX_SKU2_1.15.zip
  -> STX_SKU2_1.15.exe       (7-Zip self-extracting archive)
      -> isflash.bin
      -> H2OFFT-Wx64.exe
      -> platform.ini
      -> H2OFFT64.sys
      -> BiosImageProcx64.dll
      -> ...
```

An `isflash.bin` offset is therefore not a raw-ROM offset unless an explicit source identity, range and comparison establish that mapping. Package member sizes and hashes in the local inventory are offline artifact measurements; they do not prove that a vendor executable ran successfully, that a package is compatible with every P916F variant, or that a digest proves vendor authenticity or safe flashing.

The package's embedded help and version resources are static facts. In particular, H2OFFT textual version `6.73` and PE fixed version `6.7.3.0` are separate representations, and embedded `-g`, `-iv` and `-pq` strings do not constitute a live invocation. The reported build date, utility IHISI version and onboard IHISI version remain pending until their original capture is recovered. See [`research-artifacts.md`](research-artifacts.md#artifact-registry), [`firmware-bios.md`](firmware-bios.md#h2offt-identity-and-embedded-help) and [`reproduction-tooling.md`](reproduction-tooling.md#offline-reproduction-examples) when those pages are present.

## 6. Firmware-image analysis

The BIOS image was inspected at firmware-volume and FFS-module level to identify components relevant to SetupUtility and IFR forms, OEM badging and boot graphics, BGRT support, Insyde H2OFFT/IHISI services, chipset SMM callbacks, firmware data-map regions and the embedded ITE EC image. Exact GUIDs are retained in the detailed pages rather than replaced by descriptive names.

Representative identifiers are:

```text
SetupUtility:
FE3542FE-C1D3-4EF8-657C-8048606FF670

Boot formset:
2D068309-12AC-45AB-9600-9187513CCDD8

Boot animation resource:
931F77D1-10FE-48BF-AB72-773D389E3FAA

BootGraphicsResourceTableDxe:
B8E62775-BB0A-43F0-A843-5BE8B14F8CCD
```

The embedded boot resource remains a static artifact claim: animated GIF, `800 × 600`, 60 frames and approximately 1.74 seconds, associated with `OemBadgingSupportDxe`. Duration is resource metadata, not a measured boot-time duration.

### 6.1 SetupUtility and IFR

Setup questions were recovered from the exact SetupUtility image rather than inferred from screenshots. The retained Quiet Boot record is:

```text
QuestionId:      0x1064
VarStore:        SystemConfig
VarStore GUID:   A04A27F4-DF00-4D42-B552-39511302113D
VarStore offset: 0x6E
0x00: Disabled
0x01: Enabled
```

The form containing that setting was statically found under a suppression condition. Separately, a historical SREP session reportedly exposed the hidden Boot form at runtime. Static structure, runtime visibility and a saved setup-variable change are separate evidence classes.

The recovered S2 audit is imported with all five inventories, counted independently: 8 formsets, 28 reachability records, 151 SetupUtility questions/actions, 204 PBS and 416 CBS controls. Coverage means completeness relative to this identified audit, not every possible firmware feature. Source choices, defaults, duplicate/blank labels and risk annotations remain intact. IFR defaults, live variable reads and behavior are separate evidence; a default never becomes a live value by inference.

### 6.2 Logo paths and negative evidence

Two generic Insyde mechanisms were traced into the exact image:

- Type `0x54` / H2OFFT `-edt4f`, whose examined `ChipsetSvcSmm` callback rejects the requested type.
- Type `0x6D` / H2OFFT `-logoupdate`, whose generic target GUID was not found in the retained Windows ESRT or the 47-entry raw-ROM HFDM table at raw-ROM offset `0x1D7C000`.

These observations reject those two standard paths for the tested BIOS 1.15 scope. ESRT absence alone is not a full flash-region absence, and the two negative findings do not prove that every possible OEM-specific logo mechanism is impossible. The detailed result is in [`boot-logo-research.md`](boot-logo-research.md#scope-and-conclusion).

## 7. ACPI and WMI analysis

The machine's ACPI tables were inspected for the EC device, `OperationRegion` declarations, battery methods, WMI methods and BMOF buffers, lid methods, keyboard/Fn/Windows/Copilot fields, EC query events and OEM control methods. One critical declaration is:

```asl
OperationRegion (ERAM, SystemMemory, 0xFEEC2300, 0x100)
```

This declaration establishes only a 256-byte system-memory region. The EC-side meaning was established separately from the EC firmware's H2RAM initialization code. The cross-layer method prevents assigning semantics from a field name or address alone.

The WMAA method's direct AML return is `Package(2)`, containing a `Buffer(4)` element and the helper result `Buffer(0x100)` expected by the dispatcher. It is not a flat 256-byte return. Helper status/data layout is documented separately from the wrapper/package shape. A decoded extended BMOF schema remains pending; GUID registration and visible AML wrapper contracts do not prove class/member definitions or 4128-byte semantics.

## 8. EC firmware reverse engineering

The preferred carved IT557x image is MCS-51 / 8051-family firmware. The analysis followed reset/vector flow, `MOVX` XRAM accesses, constant DPTR loads, command dispatchers, battery-state reads, charger-control working values, threshold setters/getters and PMC data-in/data-out paths. Cross-references to XRAM addresses were treated as stronger semantic anchors than isolated strings.

### 8.1 H2RAM mapping

An initialization sequence around `CODE:0xDD50` programs the H2RAM base and size. Together with the DSDT declaration, it establishes:

The retained instruction sequence is:

```asm
MOV  DPTR,#0x105B
MOV  A,#0x30
MOVX @DPTR,A

MOV  DPTR,#0x105D
MOV  A,#0x04
MOVX @DPTR,A

MOV  DPTR,#0x105A
MOV  A,#0x01
MOVX @DPTR,A
RET
```

The logical `CODE:` address and the XRAM data addresses are separate namespaces; this sequence is not a raw-ROM patch recipe.

```text
host physical 0xFEEC2300 + N  <->  EC XRAM 0x0300 + N
```

for the exposed 256-byte window. The battery SOC field used by charge logic is `EC XRAM[0x0394]`, corresponding to host physical `0xFEEC2394`. Live MMIO reads were used as a correlation, not as a substitute for the static mapping.

### 8.2 Charge-limit subsystem

Static analysis identified:

```text
XRAM[0x0D01].bit4   enable/state
XRAM[0x0D13]        threshold #1
XRAM[0x0D14]        threshold #2
XRAM[0x0394]        SOC used by control logic
```

Relevant code landmarks include `CODE:0xED7A` (set enable bit), `CODE:0xED8E` (test enable bit), `CODE:0xEDBA` (validate/write `0x0D13`), `CODE:0xEDDF` (validate/write `0x0D14`), `CODE:0xF508` (reset/disable path), `CODE:0xF526` (read `0x0D13`), `CODE:0xF621` (read `0x0D14`) and `CODE:0xC063` (SOC/threshold decision path).

The range-check code was interpreted instruction by instruction. The `SETB C` before `SUBB A,#0x64` makes exactly decimal 100 valid and 101 invalid, establishing an inclusive `0..100` stored range. That static range check alone does not establish behavioral semantics. The later S19 `85/90` experiment independently establishes T1 as the lower charge/hold boundary and T2 as the upper active-discharge boundary on the live unit. Behavior for every arbitrary pair, exact comparator timing and exact electrical implementation remain open.

The relevant comparison pattern is:

```asm
MOV  A,value
CLR  C
SUBB A,#0
JC   invalid

MOV  A,value
SETB C
SUBB A,#0x64
JNC  invalid
```

The decision routine feeds working words around `XRAM[0x0D54..0x0D57]` and `XRAM[0x0D65..0x0D68]`. A later worker stages charger transactions associated with command numbers `0x14` and `0x15`; naming them as complete Smart Battery charger semantics remains an inference until the full path is decoded.

A secondary charger-control branch also modifies bit 5 of charger register `0x12`. The register contract is compatible with a BQ25700A/BQ25710-family `EN_LEARN`-like function, but exact charger silicon identity is not established. This family-level interpretation is not needed to establish the high-level active-discharge behavior because the latter is live-confirmed by the S19 energy trace.

## 9. Live host I/O discovery and recorded transport

ITE configuration space was checked at common candidate ports. The retained result was:

```text
CFG 0x2E: chip=0xFFFF rev=0xFF
CFG 0x4E: chip=0x5571 rev=0x07
```

Logical device `0x12` was active with data port `0x68` and command/status port `0x6C`. The successful battery transactions used status bit 0 as OBF and bit 1 as IBF.

The historical validation record describes the sequence as waiting for IBF clear, placing the command byte on `0x6C`, waiting again, placing the data/subcommand byte on `0x68`, then waiting for OBF and reading `0x68` when a response was expected. This is a description of the retained observation and its transport contract, not a new command recipe or a recommendation to issue writes. The raw observations are preserved in [`validation.md`](validation.md#3-pmc-transaction-behavior).

## 10. Thermal and performance analysis

The thermal evidence currently establishes AML field layout and dispatch, not a complete live fan controller. Recovered fields include `FNS0` at EC-window offset `0x3B` (16 bits), `FNS1` at `0x3D` (16 bits) and `FTVL` at `0x3F` (8 bits). With the H2RAM mapping, these correlate to EC XRAM `0x033B..0x033F` and host addresses `0xFEEC233B..0xFEEC233F`.

The static `GFNS` method selects FNS0 or FNS1 for selector `0x00` or `0x01`; `GPFM` returns FTVL; `SPFM` reads the requested profile and calls `THMM` before its profile-specific branches. The September source maps profile `0x01` to `ECMD(0x94)` and `0x02` to `ECMD(0x95)`. An earlier source mentions `0x91` / `0x92` but lacks complete firmware identity, so the maps are not merged.

The complete S3 `THMM` body builds a seven-byte DPTI buffer and supplies three ALIB selector/value pairs for each of Balance, Performance and LID, guarded by `DPTC == One`. Balance/LID pairs are equal; that does not establish identical EC fan tables. Parameters are not RPM, watts or fan-curve thresholds. `_Q16` labels Balance and Performance event paths; `_Q40` and `_Q81` call `THMM(FTVL)`, with `_Q81` also notifying the lid object. `_QA0` and `_QA1` notify `LCBT` of information and status changes, then notify `ACAD`, with the recorded sleeps between calls. This is static dispatch, not a live transition. The full recovered text extraction is not a complete DSDT; its digest identifies export text, not original raw File Library bytes.

The ordering of `THMM` before the `ECMD(0x94)` / `ECMD(0x95)` branches is material: a nonzero returned status does not prove that the earlier call had no side effect. This is a static control-flow correction, not an instruction to exercise an unvalidated setter. See [`thermal-performance.md`](thermal-performance.md#spfm-profile-request) and [`open-questions.md`](open-questions.md#thermal-and-performance-control).

## 11. Progressive validation strategy

The battery work deliberately separated protocol validation from behavioral semantics and persistence.

The first phase progressed from lower to higher state impact: read initial state; write threshold values while disabled; read them back; enable only after readback matched; observe charging around the 80% boundary; reboot and read the values again; then observe battery-energy telemetry under CPU load.

The retained first-phase sequence was:

```text
Read initial state:       0xF1 0x12, 0xF1 0x13, 0xF1 0x14
Set thresholds disabled:  0xF2 0x50, 0xF3 0x64
Read thresholds again
Enable after readback:    0xF1 0x11
Observe 80/100 cap behavior
Read back after reboot
Observe energy telemetry under CPU load
```

A later depletion event added a persistence counterexample: the state that survived an ordinary reboot was observed as `0/0/0` after complete battery depletion caused system power loss. The exact clearing mechanism remains unestablished.

The semantic-isolation phase then used `T1=85`, `T2=90`, verified immediate readback, and deliberately observed all three reachable regions:

```text
SOC below T1      -> Charging
T1..T2 region     -> Hold / Not charging
SOC above T2      -> sustained active battery discharge with AC online
return to T2      -> active discharge released; hold restored
```

The T2 experiment additionally retained a reported `energy_now` decrease from approximately `68.116 Wh` to `64.972 Wh`, which distinguishes genuine net battery discharge from a status-label-only transition.

These lines identify historical validation order and evidence. They are not a general-purpose command script. The repository still does not promote arbitrary threshold pairs, exact comparator timing, a production transport, or the static `0xF1 0x10` reset path to live-tested behavior.

The retained rejected-I2EC cross-check was:

```text
I2EC control : EC[200D] = 0xFF
Battery %    : EC[0394] = 255
Threshold #1 : EC[0D13] = 255
Threshold #2 : EC[0D14] = 255
MMIO crosschk : FEEC2394 = 92
Cross-check   : MISMATCH
```

## 12. Rejected paths and their scope

A rejected path is retained when its result rules out a plausible implementation under a documented condition. Rejection is scoped to the tested machine, image and method; it is not a universal statement about another model.

| Path or hypothesis | Recorded result and boundary | Detail |
|---|---|---|
| Generic Uniwill offsets `0x07B9` / `0x07D0` | Generic multi-model software evidence only; not a P916F register definition. | [`battery-charge-limit.md`](battery-charge-limit.md#paths-tested-and-rejected) |
| `INOU0000` / `ECRR` / `ECRW` | Not present in the retained P916F ACPI tables; this is a scoped absence, not a claim about every firmware. | [`acpi-wmi.md`](acpi-wmi.md#classic-acpi-ec-versus-ite-pmc2) |
| Dedicated I2EC at base `0x380` | Read-only candidate reads returned `0xFF` while H2RAM/MMIO returned a plausible SOC; rejected as a usable stock path. | [`validation.md`](validation.md#2-rejected-candidate-i2ec-path) |
| Huawei threshold GET `0x1103` | Live failure/unsupported result; the corresponding SET `0x1003` was not attempted. | [`battery-charge-limit.md`](battery-charge-limit.md#paths-tested-and-rejected) |
| H2OFFT `-edt4f` / IHISI type `0x54` | Exact P916F callback rejects the requested type. | [`boot-logo-research.md`](boot-logo-research.md#examined-type-0x54-extra-data-path) |
| H2OFFT `-logoupdate` / type `0x6D` | Required target was absent from the retained ESRT and raw-ROM HFDM scope; no working standard path established. | [`boot-logo-research.md`](boot-logo-research.md#result-matrix) |
| ACPI `_BTP` as charge cap | `_BTP` is the ACPI battery trip-point mechanism, not the dedicated threshold subsystem. | [`acpi-wmi.md`](acpi-wmi.md#dsdt-ec-field-map) |
| Direct SPI diagnostic conclusion | A complete flashrom command/result capture is not retained; only the exact ROM Armor line is supported. | [`firmware-access.md`](firmware-access.md#retained-rom-armor-observation) |

The Huawei SET is `Not tested`, not rejected. A getter result does not promote an unexecuted setter to failed or successful. Likewise, a ROM Armor enforcement line does not establish anti-rollback, RPMC, universal Linux dump failure or universal vendor-service availability.

## 13. External research three-check rule

External material is used only when it answers a real unresolved question. Every new external conclusion must pass three meaningful checks:

1. Read the primary source for the exact version and scope, and record the statement it supports.
2. Compare an independent primary implementation/specification or an independent section of the primary source for units, semantics, version and conditions.
3. Compare the result with the P916F evidence, look for a counterexample or exception, and state what the external source cannot prove about this machine.

Three search snippets or three readings of one secondary summary are not three checks. If all checks cannot be completed, the result remains pending or is written only as a bounded external fact. The public citation and qualification belong in [`research-sources.md`](research-sources.md#external-interface-and-licensing-references); a source register must not claim that checks were completed when no receipt exists. E1 Linux power-supply references define units and generic ABI semantics but do not prove this machine exposes a particular attribute. E2 Creative Commons references define licensing terms but do not grant rights in vendor binaries or extracted proprietary material.

## 14. Static facts, behavioral semantics and package claims

The repository keeps implementation facts separate from behavioral semantics and lower-level mechanism claims.

The battery subsystem is a useful example. Static firmware analysis establishes `XRAM[0x0D13]` and `XRAM[0x0D14]` as range-checked threshold fields and shows both are consumed by the charge-control path. That static fact alone did not establish what T2 meant. The later S19 `85/90` experiment separately **Live-confirmed** the high-level behavior: T1 is the lower charge/hold boundary and T2 is the upper boundary of sustained active discharge with AC online. The exact charger silicon, exact register naming, fractional comparator point, electrical power-path mechanism and behavior of every arbitrary threshold pair remain separate unresolved questions.

This distinction prevents a common error: a behavioral result can be established even when the exact lower-level mechanism remains inferred, while a plausible register-family interpretation does not become a machine identity claim merely because it fits the behavior.

The same boundary applies to setup, SREP and packages. An IFR control can be statically present while hidden at runtime; an SREP candidate can describe intended formset operations while its known-good session remains unconfirmed; an archive can contain H2OFFT help while no live `-g`, `-iv` or `-pq` invocation is established. A package hash confirms the measured bytes, not vendor authenticity, compatibility or safe execution.

CPU model attribution and topology use separate evidence: the documented processor is platform-confirmed as AMD Ryzen AI 9 365, while the 10-core / 20-thread topology comes from the identified processor's specification. The retained 20-worker stress invocation is workload configuration and is not used as topology evidence. Audio attribution is likewise separate: ALC256 and logical FL/FR are recorded observations; four physical drivers remain a reported layout without an independent product or inspection source. AC-online battery telemetry is an electrical observation at the battery interface, not by itself a complete charger-topology characterization.

## 15. Safety and publication boundaries

The investigation intentionally did not brute-force EC commands, write unknown H2RAM/MMIO values, enable generic EC write support merely to test offsets, issue Huawei threshold SET after the GET failed, copy sibling-model offsets, run vendor executables as a substitute for source recovery, or flash a permanent SetupUtility/SREP landmark. These boundaries explain why some command space and hardware behavior remain unresolved.

No document should present the following as a tested operation: a direct PWM writer, a firmware flash procedure, a raw-ROM dump command, a permanent PE patch, a known-good SREP configuration, an arbitrary threshold pair, or a generic Linux interface that the machine did not expose. The 85/90 battery policy is a recorded historical experiment, not a blanket recommendation for arbitrary values. Public pages retain technical detail, rejected hypotheses, source identifiers and failure observations while excluding vendor binaries, raw ROM/EC images and private transcripts.
