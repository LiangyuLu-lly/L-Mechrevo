# OEM Control Center and software-package analysis

This reference records what the Windows MECHREVO Control Center packages contributed to the investigation of the Xingyao 14 / `P916F-STX`. Package names, class names and generic constants are useful comparative evidence; they are not machine-specific proof without correlation to the P916F-STX firmware, ACPI tables or a recorded live result. Machine-specific charge-control findings are documented in [`battery-charge-limit.md`](battery-charge-limit.md).

The baseline software analysis is [S1 / `SRC-BASELINE`](research-sources.md#project-sources). Archive and selected-member measurements are [S9 / `SRC-BINARIES`](research-sources.md#project-sources) and [S9 / `SRC-SFX`](research-sources.md#project-sources), and are indexed in the [artifact registry](research-artifacts.md#uploaded-package-identities). Package hashing and archive enumeration were performed offline; they did not execute vendor programs.

## Package identities

The following are the seven measured uploaded package files. A local upload suffix such as `(1)` or `(2)` is not part of the canonical filename.

| Canonical filename | Measured upload alias | Size | SHA-256 | Investigation role | Evidence class | Source coverage |
|---|---|---:|---|---|---|---|
| `ControlCenter_5.56.1.13_Mechrevo_GX.zip` | `ControlCenter_5.56.1.13_Mechrevo_GX(1).zip` | 281,753,965 | `d081d2b338068ca6fd1099be2f6762d522c1223796f20a800d47034842423449` | Generic GX service/backend inspection | Artifact-confirmed | Full measured upload metadata |
| `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | 7,607,637 | `b6adb4a9cb84046cd342fe3b05fe6c16ab0e801040fa0f33d6b975d3b15768f9` | Insyde firmware-analysis tool package | Artifact-confirmed | Full measured upload metadata |
| `OSD(SKU1&SKU2).zip` | `OSD(SKU1&SKU2).zip` | 1,520,070 | `9ecdcc7287f043268126cd468f9126b03319b723c1a006bd344d1c7e75a8f109` | MECHREVO OSD support package | Artifact-confirmed | Full measured upload metadata |
| `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15_bundle.zip` | 2,628,319 | `8982c65712910ea03e5e6336823b84c93802f37c60b6392ed22934a6130408ab` | H2OFFT/platform metadata bundle | Artifact-confirmed | Full measured upload metadata |
| `STX_SKU2_1.09.exe` | `STX_SKU2_1.09.exe` | 17,757,829 | `397841144f18ada42418993dbd37238b777a0f4126f3d53b4910908ac75ee5f1` | Historical BIOS package identity | Artifact-confirmed | Full measured upload metadata |
| `STX_SKU2_1.15.exe` | `STX_SKU2_1.15.exe` | 18,419,646 | `11718b7f48a13ca08f627c1c3103cf4d1a3ee6857e15bcb165210c11e0ec446a` | BIOS 1.15 package identity | Artifact-confirmed | Full measured upload metadata |
| `jxgm_21911.zip` | `jxgm_21911(2).zip` | 33,456,863 | `b25bbac157abe916258d614afaabc4d39ee6be8dddd35229b998a9066acd9a2d` | 机械革命电竞服务中心 3.0.2.4 package | Artifact-confirmed | Full measured upload metadata |

Each selected-member row is also Artifact-confirmed; source coverage is the measured member identity and parent relationship, not a runtime execution result.

Selected nested-member measurements include:

| Parent package | Member path | Size | SHA-256 |
|---|---|---:|---|
| `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | `InsydeH2OEZE_x86_WINx64_100.00.03.11/H2OEZE-x64.exe` | 12,517,888 | `aadfb78a61cfb3862c3fea77eb662333334d35782c46344339e00d2d96554535` |
| `OSD(SKU1&SKU2).zip` | `21_OSD/Apps/MechrevoOSDInstaller013.exe` | 2,009,296 | `97635596c08214f615666ac391d3dfa6e6e39085dc1d857207436de4ab4eb678` |
| `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15/platform.ini` | 59,202 | `ba7bbee754ec240fcba7ad8059261c4b417f1f2a3bd32f15b5d24354978a854d` |
| `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15/H2OFFT-Wx64.exe` | 3,004,280 | `86f336d74c2ab951d04f35143c5efaabce94a4ebe9dd87fd62b018ad7103adb0` |
| `jxgm_21911.zip` | `jxgm_21911/jxgmdjfwzx/OTA_setup.exe` | 34,178,800 | `6fa40846658906a3004114c0c06b2eb5a6e44202b2b436dd2e857e9b81ed8a3c` |

The registry is canonical for parent/member relationships and availability. These measurements establish artifact identity only; they do not revalidate runtime behavior, backend applicability or P916F support.

## GX package structure

The GX package contained, among other files:

```text
AiStoneService/GCUBridge.exe
AiStoneService/MyControlCenter/ACPIDriverDll.dll
AiStoneService/MyControlCenter/GCUService.exe
AiStoneService/MyControlCenter/GCUServicePlugin.dll
AiStoneService/MyControlCenter/GCUUtil.exe
```

The paths indicate separate frontend, service, native-interface and utility layers. Their presence in a multi-model package does not establish that every layer applies to the investigated laptop.

## Battery-protection frontend

Retained frontend identifiers include:

```text
BatteryProtection_Command
Protection_Status
HighCapacityMode
BalancedMode
HealthyMode
IsBatteryHealthSupport
BatteryProtection/Control
```

The observed profile-selection IDs were:

```text
High-capacity / performance mode -> 0
Balanced mode                    -> 1
Healthy mode                     -> 2
```

These are profile IDs, not direct charge percentages. No retained frontend evidence establishes a P916F-specific mapping such as healthy mode equaling exactly 80%.

## Managed service findings

The exact GX `GCUService.exe` analysis identified types/classes such as:

```text
GCUService.MySystem.BatteryProtection2
MyECIO.MyEcCtrl
MyECIO.IOdriverEC
MyControlCenter.WMIEC
```

Battery-related enum names and members included:

```text
BatteryHealthProtection_Status:
  PERFORMANCE
  BALANCED
  HEALTHY

Battery_Commands:
  GET
  CHARGING_UP_LIMIT
  CHARGING_DOWN_LIMIT
  RECOVERY
  TYPE_C
  ...
```

These names establish that the broader Control Center codebase models battery-protection states and upper/lower charging limits. The relevant managed method bodies were described as anti-tampered or obfuscated, or decompiled as runtime/empty stubs. Names alone therefore do not recover the P916F transaction sequence.

## Generic EC constants

The service metadata contained:

```text
ADDR_BATTERY_CHARGE_LIMIT_UP   = 1977 decimal = 0x07B9
ADDR_BATTERY_CHARGE_LIMIT_DOWN = 2000 decimal = 0x07D0
```

These offsets are **Comparative only**. They are plausible generic Uniwill/Tongfang-oriented software constants for some supported machines, but they are not established as P916F-STX addresses.

The exact P916F firmware instead exposes a separate charge-limit subsystem using:

```text
XRAM[0x0D13]
XRAM[0x0D14]
XRAM[0x0D01].bit4
```

The associated machine-specific work used PMC2 commands in the `F1`/`F2`/`F3` family. It is documented with its own static and live evidence in [`battery-charge-limit.md`](battery-charge-limit.md). Generic constants must not replace that firmware-specific map.

## Native `ACPIDriverDll.dll` bridge

One exact native DLL extracted for analysis has this digest:

```text
SHA-256: 97d7115943600c2a09951440859f9bd75fd0d8bff9db49c296c868b49df8c8c6
```

Retained interface identifiers are:

```text
Device path: \\.\ACPIDriver
ReadEC      = 0x9C40A488
WriteEC     = 0x9C40A48C
```

The generic driver family associated with this DLL expects ACPI methods or interfaces such as:

```text
INOU0000
INOU
ECRR
ECRW
```

The P916F-STX ACPI tables were searched and these interfaces were not present. Thus the generic `ACPIDriver -> ECRR/ECRW -> old EC offset` route is not the native P916F battery-limit path. The IOCTL values are interface-identification evidence, not a validated low-level control procedure for this laptop.

## `GCUService.exe` and compact bundle

One exact service binary used in the analysis has this digest:

```text
SHA-256: 01225ef470420d50e51bc541d63dd5ed321835d40c4209106da9908c8f277a9c
```

The compact reverse-engineering bundle named `P916F-charge-reverse.tar.gz` reportedly contained:

```text
ACPIDriverDll.dll
GCUService.exe
service.ini
AirplaneDriver-related files
```

No `ACPIDriver.sys` corresponding to the generic DLL path was present in that compact extraction. The bundle-level SHA-256 was not retained; individual member hashes are the available provenance anchors. The missing outer-bundle identity is tracked under [P17 — outer package/bundle provenance](documentation-status.md#pending-evidence).

## Unsupported and unvalidated package behavior

The `jxgm_21911.zip` package was identified as 机械革命电竞服务中心 3.0.2.4. Its baseline launch on the documented machine reported that the system was not supported. This is a recorded compatibility result for that package and machine, not a conclusion that every OEM package is unsupported on P916F-STX.

The OSD archive contains `Apps/MechrevoOSDInstaller013.exe` (the measured member path is listed in the registry). No recovered experiment establishes that this package implements the battery protocol, thermal-profile control or another specific P916F service. Its presence in the source collection is recorded without assigning unverified functionality.

The Insyde H2OEZE and H2OFFT package identities likewise establish tools and metadata available for offline inspection; they do not establish a supported P916F modification workflow or a successful live invocation. Tool execution and live acquisition claims remain bounded by [P09 — live H2OFFT invocation/IHISI versions](documentation-status.md#pending-evidence).

## Interpretation boundary

The OEM packages establish:

- explicit battery-protection UI concepts;
- a broader backend with multiple EC/control mechanisms;
- upper/lower threshold concepts and generic offsets such as `0x07B9`/`0x07D0` for some supported systems;
- useful class, enum, native-device and IOCTL analysis targets.

They do not independently establish:

- that `0x07B9`/`0x07D0` belong to P916F-STX;
- that profile IDs `0`/`1`/`2` are direct percentages;
- that the generic `ACPIDriver` ACPI interface exists on this laptop;
- the exact P916F PMC2 command bytes;
- that OSD or H2OEZE packages implement any unrecorded P916F control function.

Registry metadata is Artifact-confirmed package identity, not revalidated runtime or backend evidence. Related-model configuration and generic software findings remain Comparative only until their platform applicability is demonstrated.
