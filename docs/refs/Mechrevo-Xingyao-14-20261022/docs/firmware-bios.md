# BIOS and firmware structure

## Research baseline

The investigated machine was reported to have moved from BIOS 1.09 to BIOS 1.15. The retained firmware display values were:

```text
BIOS Version: 1.15
EC Version:   1.15
Build Date:   05/07/2026
```

The build-date string is preserved without assuming a date convention. Internal EC strings use separate build namespaces; the reported `EC Version: 1.15` and any internal EC build label are not silently merged. The reported update changed the boot graphics and introduced an animation. This is a baseline observation, not a complete vendor release history. Sources: [S1 and S9](research-sources.md#project-sources).

## BIOS package structure

The baseline report identifies the outer archive as `STX_SKU2_1.15.zip`, containing `STX_SKU2_1.15.exe`. The outer ZIP was not available for rehashing. The uploaded EXE was independently inspected as a 7-Zip self-extracting archive, with the embedded 7-Zip signature at EXE file offset `0x3946F`.

```text
STX_SKU2_1.15.zip                 reported outer archive
  STX_SKU2_1.15.exe              independently inspected SFX
    isflash.bin
    H2OFFT-Wx64.exe
    platform.ini
    H2OFFT64.sys
    BiosImageProcx64.dll
    InterToolx64.efi
    H2OFFT.inf
    FlsHook.exe
    supporting runtime libraries
```

`isflash.bin` is 35,626,768 bytes. It is not the same artifact as the 33,554,432-byte raw ROM. Offsets in these two representations are not interchangeable.

The inspected 1.15 updater has SHA-256 `11718b7f48a13ca08f627c1c3103cf4d1a3ee6857e15bcb165210c11e0ec446a`; the nested image has SHA-256 `4dd5ebb5fb5f23b0de8df0cbd60d6453cfe1f5a22f69ef34ceb24432d80094c1`. Complete file identities are recorded in the [artifact registry](research-artifacts.md). These are offline artifact measurements, not evidence that the updater was executed.

### Completion configuration

The inspected configuration contains the following values, with the OEM inline comment omitted from this excerpt:

```ini
[FlashComplete]
Action=1,1
Dialog=0
Counter=15
ActionOverride=0
```

The adjacent configuration comments define action value one as Shutdown and two as Reboot. This is configuration evidence; no updater was executed during consolidation.

## H2OFFT identity and embedded help

The 1.15 package's `H2OFFT-Wx64.exe` has SHA-256 `86f336d74c2ab951d04f35143c5efaabce94a4ebe9dd87fd62b018ad7103adb0` and size 3,004,280 bytes. Its PE string resources report `FileVersion=6.73` and `ProductVersion=6.73`; its fixed numeric file-version tuple is `6.7.3.0`. These representations are recorded separately.

The executable's retained help strings include:

```text
-g                Read current ROM and save to file.
-iv               Show utility and onboard BIOS supported IHISI
                   version.
-pq               Query BIOS protection region MAP in current ROM.
```

Static presence of these options does not prove successful execution against a particular firmware. Exact live IHISI versions, a complete `-pq` region map and a fully retained acquisition transcript remain pending source recovery. A PE version resource is not evidence of the previously proposed build date. See the consolidated gates in [documentation status](documentation-status.md#pending-evidence).

## Raw-ROM reference

The baseline investigation recorded:

```text
Artifact: P916F-STX-current-ROM.bin
Size:     0x2000000 bytes / 33,554,432 bytes / 32 MiB
SHA-256:  77043505b6f42e4a482110a7ba0c7e12ba6b1db28fdaed2743c28578bbf76cd7
```

This is the reference parent for the baseline raw-ROM EC carve and raw-ROM FDM offsets. The exact private Library object is now located as S16, with the expected 33,554,432-byte size. Raw-byte materialization was unavailable in the recovery audit, so the historical SHA-256 above was not independently recomputed. The earlier claim that a raw-ROM DXE slice matched an updater slice byte for byte is not promoted to a verified comparison without the source ranges and comparison output.

## EC extraction representations

| Representation | Parent address space | Start | Length | Evidence |
|---|---|---:|---:|---|
| Preferred baseline EC image | Raw ROM | `0x081000` | `0x20000` | Retained analysis report and digest; exact private source object located |
| Earlier updater EC extraction | Nested `isflash.bin` | `0x268E30` | `0x18000` | Independently repeated extraction; digest matched |

The different lengths are significant. The 98,304-byte updater carve must not silently replace the 128 KiB image used for the baseline banked-EC analysis. The [EC reference](embedded-controller.md) specifies the code and XRAM landmarks associated with the latter.

## Boot graphics

The baseline analysis records an animated GIF associated with `OemBadgingSupportDxe`:

| Property | Recorded value |
|---|---|
| Resource GUID | `931F77D1-10FE-48BF-AB72-773D389E3FAA` |
| Dimensions | 800 × 600 |
| Frames | 60 |
| Duration | Approximately 1.74 seconds |
| Related module | BootGraphicsResourceTableDxe |
| Related module GUID | `B8E62775-BB0A-43F0-A843-5BE8B14F8CCD` |

The Linux BGRT capture reported `status=0`, `type=0`, `version=1`, `xoffset=1040` and `yoffset=387`. The horizontal relationship `1040 + 800 + 1040 = 2880` is consistent with the recorded panel width. It is a geometric correlation, not an image replacement interface or proof of runtime animation timing.

The [boot-logo investigation](boot-logo-research.md) documents the two examined update mechanisms and their limitations. An exact extracted-GIF digest, byte size and decompressed-image offsets remain pending source recovery (**P11 = NEEDS_EVIDENCE**); the proposed values in the private lead register are not promoted here.

## SetupUtility and HII formsets

The recorded SetupUtility FFS GUID is `FE3542FE-C1D3-4EF8-657C-8048606FF670`. The Boot formset GUID is `2D068309-12AC-45AB-9600-9187513CCDD8`.

The full [S2 audit](research-sources.md#project-sources) identifies Power, Advanced, Main, Boot, Security and Exit formsets plus separate AMD PBS/CBS HII formsets. They are not automatically ordinary children of OEM Advanced. [BIOS setup options](bios-setup-options.md) preserves all 8 formset, 28 reachability, 151 SetupUtility question/action, 204 PBS and 416 CBS rows. These are independently counted source-table categories. P01 source import is complete; IFR defaults and static presence do not establish live values, hardware support or behavior.

### Quiet Boot

```text
QuestionId:       0x1064
VarStore:         SystemConfig
VarStore GUID:    A04A27F4-DF00-4D42-B552-39511302113D
VarStore offset:  0x6E
Disabled:         0x00
Enabled:          0x01
```

The baseline live read was reported as `Setup[0x6E] = 0x01`. The source's runtime variable label and IFR VarStore name are retained distinctly; a complete variable-export record is required before generalizing variable-name aliases. This observation establishes the read value, not the result of changing it. Other proposed setup live overlays remain pending (**P12 = NEEDS_EVIDENCE**).

### Dynamic LID

The audit identifies `Dynamic LID` / `AmdDynamicLid` at `AMD_PBS_SETUP+0xDF`, with an IFR default of zero. Its runtime effect has not been established. The ACPI lid-state path is a separate fact and does not establish open-lid power-on behavior.

## Runtime visibility and permanent modification

The baseline report records a successful SREP runtime reveal of the suppressed Boot page. S14 now adds the recovered post-reveal photograph, whose raw bytes are identified in the [source register](research-sources.md#project-sources); the photograph directly shows the expanded Boot page. A separate permanent SetupUtility suppression candidate was noted near extracted-PE file offset `0x2636A0`, with a proposed `0x46` to `0x47` change. That permanent modification was not flashed or live-tested.

These are different experiments. Two Quiet-Boot candidate configuration records are recovered, but neither is automatically the configuration used by the successful photographed session. [Runtime setup visibility](srep-runtime-reveal.md) records the available evidence without publishing an unverified patch recipe. The photograph itself is no longer pending; the exact successful SREP executable build, final configuration and session association remain **P13 = NEEDS_CONFIRMATION**.

## Logo paths and firmware access

The [boot-logo investigation](boot-logo-research.md) keeps the Type-0x54 callback and Type-0x6D provisioning checks separate. The Type-0x54 callback rejects the examined raw-logo request; the Type-0x6D target GUID was absent from the recorded ESRT and 47-entry raw-ROM FDM result. These bounded results do not establish that every OEM-specific logo mechanism is impossible.

The retained PSP sysfs observation reports ROM Armor enforced. The available evidence does not justify a complete map of permitted flash operations or a universal claim about Linux dumping support. See [firmware access](firmware-access.md), which consolidates acquisition, PSP and region-map source gates rather than presenting proposed diagnostics as a live transcript.

## Firmware portability boundary

`P916F-HPT-R` and `P916F-ARL` are related names encountered during research, not validated compatible firmware targets. Board power sequencing, EC firmware, GPIO assignments, flash layout and setup defaults must be established separately before any cross-platform inference.
