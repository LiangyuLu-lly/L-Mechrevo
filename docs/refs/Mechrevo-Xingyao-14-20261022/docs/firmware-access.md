# Firmware access and protection observations

## Scope

This page distinguishes direct host-controller access, vendor firmware-service access and offline image analysis. It records only the available evidence; it does not provide a method for disabling platform protections or modifying the flash.

## Retained ROM Armor observation

The project terminal excerpt [S8](research-sources.md#project-sources) contains:

```text
/sys/bus/pci/devices/0000:c1:00.2/rom_armor_enforced:1
```

This establishes the reported value of the ROM Armor enforcement attribute at that observation. The PCI address is part of that capture, not a guaranteed address across firmware or kernel enumerations.

It does not by itself identify every blocked operation, authorize an alternative access method or establish the settings of anti-rollback and replay-protected monotonic-counter features. The additional PSP values retained as unverified leads are not reproduced as a terminal result.

## Direct SPI and vendor-service paths

The preceding investigation concerned a failed `flashrom` attempt. The complete command, tool version, diagnostic output and environment have not been recovered in the evidence set used for consolidation. Exact proposed error strings and a claimed `flashrom` version are therefore not presented as a verified transcript.

Separately, the baseline reference records a 32 MiB raw-ROM artifact and its digest. The actual uploaded BIOS package contains H2OFFT, whose embedded help defines `-g` as reading the current ROM and saving it to a file. These are distinct observations. A complete acquisition transcript is still needed to associate the documented raw image with a specific invocation, return status and live firmware-service version.

Accordingly, the evidence does not justify either of the broad statements that all Linux firmware reads are impossible or that a particular vendor path is universally available. Tool syntax, firmware provisioning and a successful live operation must be documented separately. See [BIOS structure](firmware-bios.md#h2offt-identity-and-embedded-help) for the static utility identity and [documentation status](documentation-status.md#pending-evidence) for source gates.

## Offline analysis

Archive enumeration, hashing and extraction of the uploaded BIOS/OEM files were performed without executing vendor programs. The 1.15 updater's `isflash.bin` and the earlier `0x18000` EC extraction were independently identified by digest. These are file-analysis results, not SPI-controller tests.

The raw 32 MiB image is a separate address space from the nested updater image. The full raw ROM was not available for a new comparison. No raw-ROM region map is reconstructed from a different container's offsets. See [firmware structure](firmware-bios.md#raw-rom-reference) and [artifact identities](research-artifacts.md).

## Protected-region and equality boundary

The H2OFFT `-pq` string is a static capability description, not a retained query result. A protected-region map requires the complete original query output; DXE, PEI, NV, EC and FTW ranges are not inferred from unrelated artifacts.

Likewise, equal filenames or GUIDs do not establish byte equality between an updater slice and a raw-ROM slice. A byte-for-byte claim requires both parent artifact identities, each exact slice start/length in its own address space, and comparison output or hashes. Pending gate **P10** covers both the protected-region map and the DXE comparison source.

## Evidence boundaries

| Claim | Evidence class | Source coverage | Boundary |
|---|---|---|---|
| `rom_armor_enforced:1` was reported at the captured PCI sysfs path | Live-confirmed | Exact one-line excerpt | Does not describe all PSP fields or all blocked operations |
| H2OFFT contains `-g`, `-iv` and `-pq` help strings | Artifact-confirmed | Static executable inspection | Does not prove invocation or success |
| A 32 MiB raw-ROM identity was retained | Artifact-confirmed | Baseline report and digest; bytes not redistributed | Does not link the file to a recovered live command |
| A complete direct-read diagnostic is available | Not established | Source identified but not exported | Requires the original command/output/environment, not a rerun for this page |
| A complete `-pq` region map is available | Not established | Source identified but not exported | Requires the original query output |
| Raw-ROM and updater DXE slices are byte-identical | Not established | Comparison source not exported | Requires parent identities, boundaries and comparison output |

## Pending source requirements

The following gates are consolidated here rather than repeated as synthetic diagnostics:

| Gate | Status | Required source | Question it would resolve |
|---|---|---|---|
| P07 | `NEEDS_EVIDENCE` | Complete `flashrom` command/output and version | Which direct access path failed and which diagnostic was actually returned |
| P08 | `NEEDS_EVIDENCE` | Full PSP sysfs capture | Values of anti-rollback, RPMC and firmware-version fields beyond the retained enforcement attribute |
| P09 | `NEEDS_EVIDENCE` | H2OFFT acquisition transcript | Actual dump command, return result and association with the recorded 32 MiB file; live `-iv` utility/onboard IHISI versions |
| P10 | `NEEDS_EVIDENCE` | Live `-pq` output plus both compared images/range hashes | Protected-region types, raw offsets and sizes; any claimed raw-ROM/updater correspondence |

These are source-recovery requirements, not a request to rerun a risky experiment. A value of zero in one protection attribute, if recovered, must not be treated as proof that unrelated flash-access restrictions are disabled.
