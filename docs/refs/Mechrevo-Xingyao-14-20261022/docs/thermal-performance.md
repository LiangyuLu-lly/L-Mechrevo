# Fan telemetry and thermal-performance interfaces

## Evidence scope

The interface definitions on this page are derived from the recovered S3 ACPI/EC text capture and selected S4/S5 DSDT excerpts. S13 adds a recovered live H2RAM/GFNS capture, while S6 provides the historical April source that uses the older `0x91` / `0x92` SPFM command pair. Stable source IDs and exact provenance are maintained in the [source register](research-sources.md#project-sources), and unresolved source-association questions remain in the [pending-evidence register](documentation-status.md#pending-evidence).

The S3 extraction includes the complete THMM body and query handlers; it is a concatenated source capture with discontinuities elsewhere, not a compilable complete DSDT. Its digest identifies the extracted text, not original raw File Library bytes. These sources establish AML field layout, dispatch and control flow. S13 additionally establishes that the two live GFNS result words track the two live H2RAM FNS fields. It does **not** establish that the numerical unit is RPM, provide an independent tachometer calibration, validate a profile write, or recover raw PWM control. The September excerpts use `ECMD(0x94)` and `ECMD(0x95)` in `SPFM`; the recovered April source uses `0x91` and `0x92`, but its exact same-session firmware identity remains unresolved. The two maps must not be merged.

## EC-visible fields

The recovered field declaration contains:

```asl
Offset (0x3B),
FNS0,   16,
Offset (0x3D),
FNS1,   16,
Offset (0x3F),
FTVL,   8,
Offset (0x70),
APFL,   1,
MSFL,   1,
```

Using the baseline H2RAM mapping gives these address relationships:

| Field | Window offset | Width | Corresponding EC XRAM | Corresponding host physical address |
|---|---:|---:|---|---|
| `FNS0` | `0x3B` | 16 bits | `0x033B..0x033C` | `0xFEEC233B..0xFEEC233C` |
| `FNS1` | `0x3D` | 16 bits | `0x033D..0x033E` | `0xFEEC233D..0xFEEC233E` |
| `FTVL` | `0x3F` | 8 bits | `0x033F` | `0xFEEC233F` |
| `APFL` | `0x70` | 1 bit | `0x0370` bit 0 | `0xFEEC2370` bit 0 |
| `MSFL` | `0x70` | 1 bit | `0x0370` bit 1 | `0xFEEC2370` bit 1 |

These are field/address correlations, not a recommendation for direct MMIO access. Atomicity, update rate and invalid-value encoding have not been established. S13 now provides a live cross-interface correlation for `FNS0`/`FNS1`, but no independent tachometer establishes that the raw word equals RPM. `FNS0` and `FNS1` establish two firmware-visible telemetry fields; they do not independently establish mechanical fan count.

## GFNS: fan telemetry

`GFNS` receives the request buffer passed by `WMAA`. Its input selector is the byte at offset `0x02`. Only selector values `0x00` and `0x01` take the successful path. The method allocates a 256-byte response, sets status to `0x01` initially, selects `FNS0` or `FNS1`, then sets status to `0x00`.

| Request or response location | Meaning |
|---|---|
| Request byte `0x00` | WMAA MFID `0x02` |
| Request byte `0x01` | WMAA SFID `0x01` |
| Request byte `0x02` | Fan selector `0x00` or `0x01` |
| Response byte `0x00` | Method status; `0x00` on the successful path |
| Response bytes `0x01..0x02` | `FSPD` word copied from the selected fan field |

Relevant AML from `SRC-AML-A` is:

```asl
CreateByteField (BUFF, Zero, STAT)
CreateWordField (BUFF, One, FSPD)
CreateByteField (Arg0, 0x02, FNUM)
STAT = One
If (((FNUM != One) && (FNUM != Zero)))
{
    Return (BUFF)
}

If ((FNUM == Zero))
{
    FSPD = ^PCI0.LPC0.EC0.FNS0
}
Else
{
    FSPD = ^PCI0.LPC0.EC0.FNS1
}

STAT = Zero
Return (BUFF)
```

The word occupies two response bytes beginning at offset one; the expected byte interpretation is low byte followed by high byte.

### Recovered live H2RAM/GFNS correlation

S13 contains a direct host-memory read followed by live GFNS calls. The relevant H2RAM bytes were:

```text
host 0xFEEC233B..0xFEEC233E: 91 0f cd 0e
```

Interpreting the two 16-bit little-endian fields gives the snapshots:

```text
FNS0 snapshot: 0x0F91 = 3985 decimal
FNS1 snapshot: 0x0ECD = 3789 decimal
```

The immediately following live WMI calls were issued through the available `huawei-wmi` debug interface:

```text
selector 0x00 -> status 0x00, result bytes 99 0f -> word 0x0F99 = 3993 decimal
selector 0x01 -> status 0x00, result bytes e1 0e -> word 0x0EE1 = 3809 decimal
```

The MMIO/H2RAM read and WMI calls were sequential rather than atomic, so byte-for-byte equality is not expected for a changing field. Both channels remain close to their corresponding field values and the returned layout matches the static GFNS implementation. This is **Live-confirmed cross-interface correlation** between GFNS and the two dynamic FNS fields.

The raw decimal values are deliberately not labelled RPM. `FSPD`, fan-related method naming and dynamic values establish fan telemetry semantics, but no independent tachometer, scaling contract or calibrated unit source was recovered. Earlier prose that quoted specific RPM values remains outside the validated set until its original capture and unit evidence are recovered.

## GPFM: profile-state query

`WMAA` dispatches MFID `0x03`, SFID `0x09` to `GPFM`. The method returns the current `FTVL` byte at response offset one and sets status to `0x00`:

```asl
Name (BUFF, Buffer (0x0100){})
CreateByteField (BUFF, Zero, STAT)
CreateByteField (BUFF, One, PFMS)
STAT = One
PFMS = ^PCI0.LPC0.EC0.FTVL
STAT = Zero
Return (BUFF)
```

This defines the query layout. It does not establish which values are reachable under every power, lid or firmware state.

## SPFM: profile request

`WMAA` dispatches MFID `0x03`, SFID `0x0A` to `SPFM`. The requested profile is read from input byte `0x02`:

```asl
Name (BUFF, Buffer (0x0100){})
CreateByteField (BUFF, Zero, STAT)
CreateByteField (Arg0, 0x02, PFMR)
STAT = One
THMM (PFMR)
If ((PFMR == One))
{
    STAT = ^PCI0.LPC0.EC0.ECMD (0x94)
}
ElseIf ((PFMR == 0x02))
{
    STAT = ^PCI0.LPC0.EC0.ECMD (0x95)
}

Return (BUFF)
```

| Input profile | Thermal call | EC command in the recovered September source |
|---:|---|---:|
| `0x01` | `THMM(0x01)` | `0x94` |
| `0x02` | `THMM(0x02)` | `0x95` |
| Other values | `THMM` is still invoked | No branch-specific EC command in this method |

`THMM` is called before the two EC-command branches are checked. A nonzero returned status therefore does not prove that no earlier state-changing action occurred. The recovered `THMM` body also contains an ALIB branch for `0x03`; this does not make that input a supported or side-effect-free `SPFM` request.

No direct `SPFM` write was independently live-validated in the recovered evidence set. The presence of a host-callable method is not a production safety or compatibility guarantee.

## THMM: conditional ALIB parameter sequence

`THMM` constructs a seven-byte DPTI buffer:

| Buffer field | Offset | Width | Recorded content |
|---|---:|---:|---|
| `SSZE` | `0x00` | 16 bits | `0x0007` |
| `MSID` | `0x02` | 8 bits | Parameter selector |
| `MSDV` | `0x03` | 32 bits | Parameter value |

The complete S3 text extraction contains all three branches, each guarded by `DPTC == One`. Every assignment pair is followed by `ALIB(0x0C, DPTI)`:

| THMM argument | Static label | MSID | MSDV | Following call |
|---|---|---|---|---|
| `0x01` | Balance | `0x05` | `0x3A98` | `ALIB(0x0C, DPTI)` |
| `0x01` | Balance | `0x06` | `0x7530` | `ALIB(0x0C, DPTI)` |
| `0x01` | Balance | `0x07` | `0x61A8` | `ALIB(0x0C, DPTI)` |
| `0x02` | Performance | `0x05` | `0x6D60` | `ALIB(0x0C, DPTI)` |
| `0x02` | Performance | `0x06` | `0xAFC8` | `ALIB(0x0C, DPTI)` |
| `0x02` | Performance | `0x07` | `0x88B8` | `ALIB(0x0C, DPTI)` |
| `0x03` | LID | `0x05` | `0x3A98` | `ALIB(0x0C, DPTI)` |
| `0x03` | LID | `0x06` | `0x7530` | `ALIB(0x0C, DPTI)` |
| `0x03` | LID | `0x07` | `0x61A8` | `ALIB(0x0C, DPTI)` |

The full recovered method body follows; indentation alone is normalized:

```asl
Method (THMM, 1, Serialized)
{
    Name (DPTI, Buffer (0x07){})
    M460 ("LCT-ASL- Change thermal mode = %d \n", Arg0, Zero, Zero, Zero, Zero, Zero)
    CreateWordField (DPTI, Zero, SSZE)
    CreateByteField (DPTI, 0x02, MSID)
    CreateDWordField (DPTI, 0x03, MSDV)
    SSZE = 0x07
    If ((Arg0 == Zero)){}
    ElseIf ((Arg0 == One))
    {
        If ((DPTC == One))
        {
            M460 ("LCT-ASL- Change to Balance Mode proc DPTC ALIB Call\n", Zero, Zero, Zero, Zero, Zero, Zero)
            MSID = 0x05
            MSDV = 0x3A98
            ALIB (0x0C, DPTI)
            MSID = 0x06
            MSDV = 0x7530
            ALIB (0x0C, DPTI)
            MSID = 0x07
            MSDV = 0x61A8
            ALIB (0x0C, DPTI)
        }
    }
    ElseIf ((Arg0 == 0x02))
    {
        If ((DPTC == One))
        {
            M460 ("LCT-ASL- Change to Performance Mode proc DPTC ALIB Call\n", Zero, Zero, Zero, Zero, Zero, Zero)
            MSID = 0x05
            MSDV = 0x6D60
            ALIB (0x0C, DPTI)
            MSID = 0x06
            MSDV = 0xAFC8
            ALIB (0x0C, DPTI)
            MSID = 0x07
            MSDV = 0x88B8
            ALIB (0x0C, DPTI)
        }
    }
    ElseIf ((Arg0 == 0x03))
    {
        If ((DPTC == One))
        {
            M460 ("LCT-ASL- Change to LID Mode proc DPTC ALIB Call\n", Zero, Zero, Zero, Zero, Zero, Zero)
            MSID = 0x05
            MSDV = 0x3A98
            ALIB (0x0C, DPTI)
            MSID = 0x06
            MSDV = 0x7530
            ALIB (0x0C, DPTI)
            MSID = 0x07
            MSDV = 0x61A8
            ALIB (0x0C, DPTI)
        }
    }

    M460 ("LCT-ASL- THMM End \n", Zero, Zero, Zero, Zero, Zero, Zero)
}
```

`Arg0 == Zero` has an empty branch. Arguments outside `0x01..0x03`, or `DPTC != One`, do not enter these ALIB sequences; framing and debug calls still occur. This bounds this method only, not all effects of its callers. There is no explicit return in the recovered body. The `0x03` branch does not establish a supported or live-validated `SPFM` setter: that caller has EC-command branches only for `0x01` and `0x02`.

Balance and LID have equal ALIB selector/value sequences. This does **not** establish identical EC fan tables. MSDV values are raw ALIB parameters, not RPM, watts, temperature thresholds or fan-curve points. Their units and effective platform behavior require a separately identified ALIB contract and supporting evidence.

## Fn+X event path

The `_Q16` query handler includes firmware debug strings explicitly naming Fn+X, Balance Mode and Performance Mode. For `FTVL` equal to `0x01` or `0x02`, it calls `THMM` and raises a WMI event:

| FTVL | Static label | Event payload `WMEN` | Notify value |
|---:|---|---:|---:|
| `0x00` | Empty branch in this handler | No event in the shown branch | — |
| `0x01` | Balance Mode | `0x41` | `0xA0` |
| `0x02` | Performance Mode | `0x42` | `0xA0` |

The retained source is:

```asl
Method (_Q16, 0, NotSerialized)  // _Qxx: EC Query, xx=0x00-0xFF
{
    M460 ("PLA-ASL-\\_SB.PCI0.LPC0.EC0.Q16 Fn + x Start\n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
    If ((FTVL == Zero)){}
    ElseIf ((FTVL == One))
    {
        M460 ("PLA-ASL- Fn + x (Balance Mode) proc DPTC ALIB Call\n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
        THMM (FTVL)
        ^^^^WMI1.WMEN = 0x41
        Notify (WMI1, 0xA0) // Device-Specific
    }
    ElseIf ((FTVL == 0x02))
    {
        M460 ("PLA-ASL- Fn + x (Performance Mode) proc DPTC ALIB Call\n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
        THMM (FTVL)
        ^^^^WMI1.WMEN = 0x42
        Notify (WMI1, 0xA0) // Device-Specific
    }

    M460 ("PLA-ASL-\\_SB.PCI0.LPC0.EC0.Q16 Fn + x End\n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
}
```

The labels support the static profile names. This code is not a raw capture of a physical Fn+X press or a measured `0x02`-to-`0x01` transition. The previously reported Fn+X/GPFM transition remains pending as P04b and is not reconstructed here.

## Query and notification order

The recovered AML also contains initialization, lid-related thermal dispatch and battery/adapter notification order. These statements are source-level evidence; they do not establish the value selected by Dynamic LID or a live LID profile.

### `_Q40`: initialization dispatch

```asl
Method (_Q40, 0, NotSerialized)  // _Qxx: EC Query, xx=0x00-0xFF
{
    M460 ("PLA-ASL- Initial THM Mode \n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
    THMM (FTVL)
}
```

`_Q40` calls `THMM` with the current `FTVL` value.

### `_Q81`: LID-related thermal dispatch

```asl
Method (_Q81, 0, NotSerialized)  // _Qxx: EC Query, xx=0x00-0xFF
{
    M460 ("PLA-ASL- Change THM Mode when LID Mode change \n", Zero, Zero, Zero, Zero, Zero, Zero, Zero)
    THMM (FTVL)
    P80H = 0x81
    Notify (LID, 0x80) // Status Change
}
```

`_Q81` calls `THMM(FTVL)` before writing `P80H = 0x81` and notifying `LID` with `0x80`. It does not prove that Dynamic LID selects a particular profile, that the laptop powers on when opened or that a specific LID profile was observed live.

### `_QA0` and `_QA1`: battery/adapter notification order

Both query handlers retain the same notification sequence:

```asl
Method (_QA0, 0, NotSerialized)  // _Qxx: EC Query, xx=0x00-0xFF
{
    Notify (LCBT, 0x81) // Information Change
    Sleep (0x64)
    Notify (LCBT, 0x80) // Status Change
    Sleep (0x64)
    Notify (ACAD, 0x80) // Status Change
}

Method (_QA1, 0, NotSerialized)  // _Qxx: EC Query, xx=0x00-0xFF
{
    Notify (LCBT, 0x81) // Information Change
    Sleep (0x64)
    Notify (LCBT, 0x80) // Status Change
    Sleep (0x64)
    Notify (ACAD, 0x80) // Status Change
}
```

The sequence is an AML notification-order observation. It is not a timing guarantee for host software and does not add a live fan or battery experiment.

## Source-version discrepancy

The historical source discrepancy is no longer merely a source locator. S6 now contains two recovered records:

```text
S6 April text source
  size:      7465 bytes
  SHA-256:   21d40ec647d5859a2b6feb19ec206dd296eaba45666d7d3e8bc4b17da5966d34

dsdt.dsl
  size:      253103 bytes
  SHA-256:   43f4b40e70ac867416b9137e3d41ae12ead76224038dcfb6a4943bfc41466296
  DSDT len:  0x836B / 33643 bytes
```

Both recovered April records contain the same general `SPFM` structure and use `ECMD(0x91)` and `ECMD(0x92)`. The September sources `SRC-AML-B`/`SRC-AML-C` (`S5`/`S3`) use `0x94` and `0x95`.

The April source identity is therefore recovered, but exact same-session firmware association is still unresolved. Separate historical kernel captures identify P916F-STX BIOS 1.09 in the broader April investigation, which is corroborating context rather than proof that this exact DSDT was exported under that BIOS. The repository therefore retains the two source-scoped maps without declaring why they differ.

The September map is the one specified above for the recovered September source. The April map remains historical and must not be used as an interchangeable P916F-STX 1.15 command recipe.

## Source locators that are not fan tables

`SRC-AML-D` retains these SSDT line locators:

```text
ssdt10.dsl:190:            M460 ("  ECFanTableIndex: 0x%x\n", M52A, Zero, Zero, Zero, Zero, Zero, Zero)
ssdt10.dsl:191:            M460 ("  ECFanRPM: 0x%x\n", M52B, Zero, Zero, Zero, Zero, Zero, Zero)
```

These strings are source locators only. They are not a recovered fan-table definition, a tachometer register map or a measured RPM dataset. Debug strings alone cannot establish the missing implementation.

## Unresolved EC-level control

The recovered AML and live query capture do not establish:

- a safe generic fan setter;
- complete tachometer/PWM register semantics;
- the meaning of manual/automatic mode bytes;
- target-RPM tables;
- temperature-to-row thresholds;
- profile persistence;
- the runtime condition that selects LID mode;
- mechanical fan count or an independent RPM calibration;
- the previously reported live Fn+X/GPFM transition.

A future EC analysis would need the exact image digest, bank/address context and disassembly or data extraction for the direct control claims. Until then, no manual PWM writes, reconstructed fan curves or unsupported RPM values are published. The missing tach/PWM/table evidence is tracked as `P05 NEEDS_EVIDENCE`; live GFNS/H2RAM correlation is closed as P04a, while the missing Fn+X/GPFM transition remains P04b in [documentation status](documentation-status.md#pending-evidence).
