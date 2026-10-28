# ACPI and WMI interfaces

## Source scope

This page combines the machine-specific baseline report (S1), the recovered S3 ACPI/EC text capture, and selected S4/S5 excerpts. S3 includes complete THMM and helper bodies, EC fields, query handlers and a Linux battery/sysfs capture. Its recorded identity applies to the recovered text representation rather than the original raw ACPI table bytes, and the capture contains repeated ranges and discontinuities rather than a complete DSDT. Original table bytes, their digest and exact firmware association remain P02b; complete THMM source recovery is closed as P02a. Exact source locators and export identities are maintained in the [source register](research-sources.md#project-sources), with unresolved material tracked in [documentation status](documentation-status.md#pending-evidence).

A static method body or BMOF metadata does not by itself establish a live setter, a complete WMI schema or a production-safe host transport. The battery limiter reached through ITE PMC2 is documented separately in [battery-charge-limit.md](battery-charge-limit.md).

## EC regions and devices

The investigated ACPI environment identifies an embedded controller with `_HID=PNP0C09` and GPE `0x0B`. Standard ACPI EC traffic uses the conventional I/O path at `0x62`/`0x66`.

S3 also shows `EC0._STA` returning `0x0F`, `OKEC` initialized to zero and mutexes `Z009`/`QEVT` initialized with sync level `0x00`. Its ERAM field declaration uses `ByteAcc, NoLock, Preserve`. Bytes `0x00..0x03` are `ECMV`, `ECSV`, `ECBV`, `ECTV`, and `0x04..0x07` are `FLT0..FLT3`, all 8-bit fields. These declarations do not supply measured values or independently establish their higher-level meaning. `LID._STA` and the captured `ACAD._STA` each return `0x0F`; the preceding adapter method fragment returns EC `ACIN`, but its complete body is not present at that source boundary.

A separate declaration exposes shared state through physical memory:

```asl
OperationRegion (ERAM, SystemMemory, 0xFEEC2300, 0x100)
```

The baseline firmware analysis maps that aperture to EC XRAM `0x0300..0x03FF`. This does not map all EC XRAM into host memory, and it is distinct from ITE PMC2 at I/O `0x68`/`0x6C`. The complete field map is in [embedded-controller architecture](embedded-controller.md#acpi-field-map).

An earlier ACPI-only inspection did not reveal a useful charge-limit path and initially treated `0x68`/`0x6C` as irrelevant. Live ITE Super-I/O configuration later showed PMC2 logical device `0x12` active at data I/O `0x68` and command/status I/O `0x6C`; that live result supersedes the earlier negative assumption.

## Battery object

The battery object is `LCBT`, exposed by Linux as:

```text
/sys/class/power_supply/LCBT
```

`ACAD` is the recorded AC adapter object. The retained battery model string is:

```text
588974-3S-G-A0
```

Relevant AML methods include:

```text
_BIX   extended battery information
_BST   dynamic battery status
_BTP   battery trip-point programming
```

`_BTP` writes `BTPL/BTPH` at window offset `0x90`. This is the ACPI battery trip-point/notification mechanism, not the charge limiter at `XRAM[0x0D13]` and `XRAM[0x0D14]`. Linux battery telemetry worked in the recorded environment, while generic charge-threshold attributes were absent. See [Linux power-supply exposure](linux.md#power-supply-exposure).

The recorded `LCBT` tree exposed `capacity`, `status`, `voltage_now`, `power_now` and `energy_now`; `current_now` was not present. It did not expose generic charge-limit attributes:

```text
charge_control_start_threshold
charge_control_end_threshold
charge_behaviour
```

## DSDT EC field map

The retained DSDT mapping includes:

```text
Offset 0x30:
  ACIN  bit6
  ACLW  bit7

Offset 0x31:
  PBST  bit0
  LIDS  bit1
  PBS2  bit6

Offset 0x32:
  TPST  bit0
  KBFN  bit1

Offset 0x33:
  KBBL  8 bits

Offset 0x34:
  WINS  bit0
  CPLS  bit1

Offset 0x35:
  KBST  16 bits

Thermal fields:
  0x3B  FNS0  16 bits
  0x3D  FNS1  16 bits
  0x3F  FTVL  8 bits
  0x70  APFL bit0
  0x70  MSFL bit1

Offset 0x80:
  BATI  bit0
  BAII  bit1
  BACG  bit2
  BAIC  bit3

Battery telemetry region:
  0x81  BFCL/BFCH
  0x83  BRML/BRMH
  0x85  BDCL/BDCH
  0x87  BMNF
  0x88  BVLL/BVLH
  0x8A  BCRL/BCRH
  0x8C  BDVL/BDVH
  0x8E  BACL/BACH

Other fields:
  0x90  BTPL/BTPH
  0x92  SRNM (16 bits)
  0x94  RSOL/RSOH
  0xA0  BDVN (128-bit/model-string region)
  0xCF  STAS
  0xFE  TEMP
```

The exact symbolic names above come from the AML field declarations. Their higher-level semantics should be established from their AML consumers rather than guessed solely from abbreviations. Thermal field behavior and the `GFNS`/`GPFM` layouts are detailed in [thermal/performance interfaces](thermal-performance.md#ec-visible-fields).

## Lid object

The lid device uses `_HID=PNP0C0D`. Its `_LID` method reads the EC-backed `LIDS` bit:

```text
LIDS == 0 -> _LID returns 0
otherwise -> _LID returns 1
```

The `_Q81` query handler calls `Notify (LID, 0x80)` to report a status change and also invokes the thermal-mode path described in [thermal/performance interfaces](thermal-performance.md#query-and-notification-order). This is the firmware lid-state path. The operating system's lid-close policy and the setup option named Dynamic LID are separate mechanisms; neither the field name nor the setup label establishes an open-lid-to-power-on feature.

## WMI device and transport layers

The recovered namespace contains `WMI1`, `_HID=PNP0C14` and `_UID="HWMI"`. The retained `_WDG` material identifies method/event GUIDs:

```text
ABBC0F5B-8EA1-11D1-A000-C90629100000
ABBC0F5C-8EA1-11D1-A000-C90629100000
```

The BMOF registration GUID is:

```text
05901221-D566-11D1-B2F0-00A0C9062910
```

Three layers must remain separate: WMI registration/schema, the ACPI `WMAA` call and the OEM method's response buffer. A wrapper may transform the ACPI return, so the direct AML return type is specified below rather than assumed to be a flat buffer.
The helper methods described below return a 256-byte result buffer unless their wrapper contract states otherwise; the first byte is used as the method status field. This helper-buffer convention is distinct from the direct `WMAA` package return.


### WMAA input and return contract

The dispatcher is `Method (WMAA, 3, Serialized)`. It acquires `MUTW`, treats `Arg2` as the request buffer and extracts MFID and SFID from bytes zero and one. The recovered beginning is:

```asl
Acquire (MUTW, 0xFFFF)
Name (WMRP, Package (0x02)
{
    Buffer (0x04){},
    Buffer (0x0100){}
})
Local0 = Arg2
CreateByteField (Local0, Zero, MFID)
CreateByteField (Local0, One, SFID)
Local1 = DerefOf (WMRP [One])
CreateByteField (Local1, Zero, STAT)
STAT = One
```

The separately recovered end is:

```asl
WMRP [One] = Local1
Release (MUTW)
Return (WMRP)
```

The direct ACPI return is a **two-element package**, initially containing a four-byte buffer and a 256-byte buffer. The method-specific result replaces element one. Within that result buffer, byte zero is the OEM method status field. WMAA itself must not be described as returning only a flat 256-byte buffer. The method is Serialized and uses `MUTW`; those AML synchronization properties do not establish ownership of raw PMC2 transactions issued outside this path.

### WMAA dispatch inspection

The retained dispatcher recognizes these finite MFID/SFID combinations:

| MFID | SFID | Target |
|---:|---:|---|
| `0x01` | `0x01` | `GVER` |
| `0x02` | `0x01` | `GFNS` |
| `0x03` | `0x01` | `GWLS` |
| `0x03` | `0x02` | `SWLS` |
| `0x03` | `0x03` | `GFLS` |
| `0x03` | `0x04` | `SFLS` |
| `0x03` | `0x05` | `GCLS` |
| `0x03` | `0x06` | `SCLS` |
| `0x03` | `0x07` | `GTPS` |
| `0x03` | `0x08` | `STPS` |
| `0x03` | `0x09` | `GPFM` |
| `0x03` | `0x0A` | `SPFM` |
| `0x03` | `0x0B` | `GKBT` |
| `0x03` | `0x0C` | `SKBT` |

The initial response status is `0x01`. Each helper defines its own success and failure behavior. The table is not evidence that unlisted MFID/SFID combinations are supported, and no brute-force request enumeration is part of this reference. The expected Huawei battery-threshold `0x10`/`0x11` family was not recovered as a usable path in this dispatcher.

## Method-specific behavior

### Interface version

`GVER` returns data DWORD `0x00020004` in response bytes beginning at offset one. This is retained as an OEM interface-version value; no compatibility policy is inferred from its numerical fields.

### Fan telemetry and profiles

The `GFNS` helper reads the selector at request byte `0x02`. Selectors `0x00` and `0x01` select `FNS0` and `FNS1`; the selected word is returned beginning at response byte one, and the response byte zero status becomes `0x00` on that path. Unsupported selectors retain the initial failure status. `GPFM` returns the current `FTVL` byte at response offset one and sets status `0x00`.

`SPFM` reads the requested profile from input byte `0x02`, calls `THMM`, then selects `ECMD(0x94)` for profile `0x01` or `ECMD(0x95)` for profile `0x02`. The recovered method calls `THMM` **before** checking those profile-specific EC-command cases. A failure status therefore does not establish that the earlier call had no effect. Full source-level thermal behavior, ALIB parameter values and the older-source discrepancy are in [thermal/performance interfaces](thermal-performance.md).

No direct fan-speed or profile setter was independently live-validated in the recovered evidence. Static fields and methods do not supply a tachometer calibration, RPM table or safe manual-PWM procedure.

### Fn-lock state

```text
GFLS -> reads EC field KBFN
SFLS -> accepts only 0 or 1 and writes EC field KBFN
```

`SFLS` contains explicit firmware debug strings:

```text
Fn lock
Fn unlock
```

The source therefore establishes the static polarity. It does not substitute for a retained live keyboard-behavior test.

### Windows-key lock state

```text
GWLS -> reads EC field WINS
SWLS -> accepts only 0 or 1 and writes EC field WINS
```

`SWLS` contains:

```text
WMI Request Win lock
WMI Request Win unlock
```

### Copilot-key lock state

```text
GCLS -> reads EC field CPLS
SCLS -> accepts only 0 or 1 and writes EC field CPLS
```

`SCLS` contains:

```text
WMI Request Copilot lock
WMI Request Copilot unlock
```

In the recovered `SCLS` body, input one selects the lock message and input zero selects unlock, followed by the masked field write.

### TPST state/control pair

```text
GTPS -> reads EC field TPST
STPS -> accepts only 0 or 1
```

The requested state selects these EC commands:

```text
requested state 1 -> ECMD(0x90)
requested state 0 -> ECMD(0x8F)
```

The field/method naming and event flow identify a two-state platform input-device control path. Exact end-user polarity and live behavior remain unverified until exercised; they are not inferred solely from names.

### KBST access

`GKBT` creates a response word at offset one and copies `KBST` into it. `SKBT` creates an input word at offset `0x02`, writes that value to `KBST` and returns success. The recovered source establishes the field and layout, not a validated range or a complete user-facing keyboard configuration model.

## EC query and WMI events

The handlers below write `WMEN` and then notify `WMI1` with `0xA0`:

| EC query | Condition or source field | WMEN |
|---|---|---:|
| `_Q0C` | `TPST=0` / `TPST=1` | `0x30` / `0x31` |
| `_Q11` | `KBBL=0` / `1` / `2` | `0x20` / `0x21` / `0x22` |
| `_Q16` | `FTVL=1`, Balance / `FTVL=2`, Performance | `0x41` / `0x42` |
| `_Q17` | `KBFN=0`, unlock / `KBFN=1`, lock | `0x50` / `0x51` |
| `_Q10` | User-facing meaning not established | `0xA1` |
| `_Q12` | User-facing meaning not established | `0xA0` |
| `_Q18` | User-facing meaning not established | `0xA2` |

The first three groups can be correlated directly with named EC state fields. `_Q16` contains debug strings naming Fn+X, Balance Mode and Performance Mode; the thermal page preserves its source-level call/order. The exact user-facing meanings of standalone `0xA0`/`0xA1`/`0xA2` events were not established.

`_Q0A` and `_Q0B` notify the LCD object with `0x87` and `0x86` respectively in S3. Those numeric observations are retained without assigning an unverified event meaning. `_Q40` and `_Q81` thermal dispatch, and the `_QA0`/`_QA1` battery/adapter notification order, are preserved in [thermal/performance interfaces](thermal-performance.md#query-and-notification-order) because that page is the canonical home for those related source excerpts.

## Binary MOF

The baseline investigation retained the following metadata for `WQBA.bmof`:

```text
Size:        1092 bytes / 0x444
Prefix:      46 4F 4D 42 01 00 00 00 34 04 00 00 5C 10 00 00
ASCII:       FOMB
BMOF GUID:   05901221-D566-11D1-B2F0-00A0C9062910
```

The original report describes generic OEM WMI classes and no established battery-charge-specific class. A complete decoded MOF listing was not recovered. Exact class/member declarations and proposed extended request sizes are therefore not reproduced as verified schema. The visible AML buffer layout remains independently documented above. This is the `SRC-BASELINE` artifact metadata; the full decoded BMOF schema remains pending source recovery.

## Huawei-compatible threshold test

A `huawei-wmi` platform device was present in the recorded Linux environment, but its expected battery-control attributes were absent. A Huawei-style threshold GET identified as `0x1103` returned a failure/unsupported result: **response buffer byte 0 was `0x01`** in the returned result used during the investigation. The corresponding SET `0x1003` was not attempted.

The presence of Huawei-compatible WMI plumbing does not establish the Huawei charge-threshold API. In the recovered WMAA dispatcher, the expected threshold family is not the working battery path. The validated P916F limiter is the separate [PMC2 protocol](battery-charge-limit.md).

## Classic ACPI EC versus ITE PMC2

There are two different host-facing interfaces relevant to the investigation:

```text
ACPI EC:  data/status path around 0x62/0x66
ITE PMC2: data 0x68, command/status 0x6C
```

The live ITE Super-I/O configuration result was:

```text
LDN 0x12 (PMC2)
active = 1
I/O #0 = 0x0068
I/O #1 = 0x006C
```

The later live PMC2 result supersedes the earlier ACPI-only negative assumption about the usefulness of `0x68`/`0x6C`; the battery command family worked through those ports. The classic ACPI EC path and the PMC2 path must not be treated as interchangeable transports.

## Interface boundaries

The battery limiter must not be conflated with `_BTP`, generic power-supply attributes, the failed Huawei threshold request, generic Uniwill `ECRR`/`ECRW` methods or the thermal-profile setters. Each has a different namespace, transport and validation status:

| Interface | What the retained evidence supports | Boundary |
|---|---|---|
| ACPI `_BTP` | Battery trip-point notification fields `BTPL/BTPH` | Not the charge-cap subsystem |
| Generic Linux power-supply threshold attributes | Absent from the observed `LCBT` tree | No generic sysfs setter is established |
| Huawei-compatible WMI | Registration and a failed read-only threshold GET | No Huawei threshold write was attempted |
| OEM thermal WMI methods | Static AML dispatch and field layout | No live fan/PWM behavior is established |
| ITE PMC2 | Live battery-limit command path at data I/O `0x68` and command/status I/O `0x6C` | Separate from the classic ACPI EC `0x62`/`0x66` path |

The machine's validated battery charge cap is therefore the P916F-specific ITE PMC2 command family, not a generic WMI or ACPI battery-threshold interface.
