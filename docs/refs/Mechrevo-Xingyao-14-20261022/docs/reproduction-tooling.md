# Reproduction tooling and offline workflow

This page describes the retained tool identities and safe, offline reproduction boundaries. It is not a firmware flashing guide, hardware-control utility or execution recipe. Tool availability, version identity and source coverage are separate fields.

## Tool inventory

| Tool or interface | Role in the investigation | Version namespace / identity | Source | Retention state and publication boundary |
|---|---|---|---|---|
| `H2OFFT-Wx64.exe` | Embedded updater utility identified inside the P916F 1.15 SFX | Textual FileVersion/ProductVersion `6.73`; numeric VS_FIXEDFILEINFO `6.7.3.0`; member size `3,004,280`; SHA-256 `86f336d74c2ab951d04f35143c5efaabce94a4ebe9dd87fd62b018ad7103adb0` | `SRC-SFX`; [offline H2OFFT metadata](research-artifacts.md#embedded-h2offt-sfx-inspection) | Static artifact-confirmed only. Embedded `-g`, `-iv`, `-pq` strings do not prove invocation, dump, map output or safe operation. Proposed build date and live IHISI versions remain P09 |
| `7-Zip SFX / offline archive inspection` | Identify the embedded archive and enumerate members without invoking vendor code | Embedded signature offset `0x3946F`; extraction implementation/version not retained | `SRC-SFX`; [artifact inspection](research-sources.md#artifact-inspection-record) | Offline static inspection only; no vendor executable was run |
| `iasl` | Historical AML disassembly of exported `ssdt*.dat` inputs | Exact version not retained; source preserves `iasl -d` usage | `SRC-AML-D`; [ACPI/WMI](acpi-wmi.md) | Retained source command and locators; original complete inputs/version are not public |
| `SREP` | Historical runtime reveal of suppressed setup form | Exact executable build, configuration association and successful session receipt unresolved | `SRC-SREP`; [SREP runtime reveal](srep-runtime-reveal.md) | Historical report only; candidate configuration is not published as a known-good patch (P13) |
| `flashrom` | Firmware-access investigation context | Version and complete diagnostic output not retained | `SRC-ROMARMOR`; [firmware access](firmware-access.md) | Original version and diagnostics require P07; no synthetic transcript or runtime recipe |
| `UEFIExtract` / UEFI image tooling | Firmware-volume and module inspection context | Exact tool/version not retained | `SRC-BASELINE`; [firmware structure](firmware-bios.md) | Historical workflow landmarks only; no claim that an unretained version was rerun |
| `8051` / MCS-51 disassembly workflow | Follow EC reset flow, MOVX/XRAM accesses and dispatch landmarks | Specific disassembler and version not retained | `SRC-BASELINE`; [embedded controller](embedded-controller.md) | Static landmarks retained; no executable writer or direct-write contract |
| Local hash/extraction helpers | Package/member measurements used for this registry and deterministic offline checks | Local helper implementation/version is not a historical investigation tool identity | `SRC-BINARIES`, `SRC-SFX` | New offline provenance checks only; never describe them as original hardware experiments |

## Version and provenance rules

- Preserve version namespaces exactly. H2OFFT `6.73` is a textual resource field; `6.7.3.0` is the numeric fixed-file version. They are not interchangeable and neither supplies a build date.
- An embedded option string is evidence that a binary contains that syntax. It is not evidence that the option was executed, that a file was written or read, or that firmware accepted the operation.
- Historical commands are labeled as historical source text. New offline examples below are labeled reproduction examples and must not be backdated into the experiment chronology.
- A tool name in a report does not supply its version. Keep `not retained` when the exact version/build is absent.

## Retained command and string boundaries

The AML source excerpt retains this read-only disassembly command:

```text
for f in ssdt*.dat; do
    iasl -d "$f" >/dev/null 2>&1
done
```

This is a source locator from `SRC-AML-D`, not a claim that the same command was rerun during documentation consolidation. The source also retains method/search locators for `GFNS`, `GPFM`, `SPFM`, `WMAA` and the September `ECMD(0x94)` / `ECMD(0x95)` calls.

The H2OFFT offline metadata retains these embedded strings:

```text
-g     Read current ROM and save to file.
-iv    Show utility and onboard BIOS supported IHISI version.
-pq    Query BIOS protection region MAP in current ROM.
```

They remain static strings only. No complete `-g`, `-iv` or `-pq` invocation/output is retained, so this page intentionally does not provide a dump or protection-map runtime recipe.

## Offline reproduction examples

The examples in this section are newly authored, read-only provenance checks. They operate on a file already obtained by an authorized offline process; they do not invoke a vendor updater, access hardware, write firmware, alter NVRAM or issue EC setters.

### Example: hash a locally available package

```powershell
# Reproduction example only; not the historical acquisition command.
Get-FileHash -Algorithm SHA256 -LiteralPath .\STX_SKU2_1.15.exe
```

Expected digest when the input is the measured `STX_SKU2_1.15.exe` parent: `11718b7f48a13ca08f627c1c3103cf4d1a3ee6857e15bcb165210c11e0ec446a`. The expected value identifies the measured bytes; it does not establish vendor authenticity or compatibility.

### Example: record an offline archive member path

```text
parent: STX_SKU2_1.15.exe
member: isflash.bin
member: H2OFFT-Wx64.exe
member: platform.ini
```

The authoritative measured sizes and digests are in [the artifact registry](research-artifacts.md#embedded-h2offt-sfx-inspection). Do not flatten these paths to a basename when comparing a member from a different package.

### Example: verify the historical EC carve boundary

```text
parent: STX_SKU2_1.15.exe -> isflash.bin
offset space: isflash.bin file offset
offset: 0x268E30
length: 0x18000
expected SHA-256: 030ec5da8b5f027d2461af98b92416eab4a526734bee4b3032e5d9042d016023
```

This is an offline extraction/provenance example derived from `SRC-SFX`. It is not a raw-ROM offset and not evidence that an updater command ran. The same digest is retained historically for the earlier updater EC-like extraction; that does not establish equivalence to `ROM+0x081000` length `0x20000`.

## What remains gated

| Gate | Required material | Current boundary |
|---|---|---|
| P07 / flashrom | Original command, result, environment and exact version | No synthetic failure transcript or rerun |
| P09 / H2OFFT live identity | Actual invocation/output linked to dump, build date and IHISI versions | Static metadata only |
| P10 / `-pq` and byte equality | Complete region map or both exact byte slices plus comparison | Embedded `-pq` help is not output |
| P13 / SREP | Exact successful build/config/session association | Candidate text is not known-good |
| P16 / rehash | Exact raw-ROM/current EC bytes | Historical hashes retained; no new rehash |

The public workflow therefore stops at offline identity, structure and digest checks. It does not turn static evidence into an operating procedure.
