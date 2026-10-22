# Research artifacts and provenance

This repository is documentation-only. Binary firmware, vendor executables, extracted resources and private reports are not distributed. The registry records identities and derivation paths so a reader can distinguish a matching file from a compatibility, authenticity or safety claim.

## Artifact registry

### Registry schema and evidence boundary

Each record uses the following fields where they carry information: artifact ID, canonical filename, local upload alias, artifact type, size, SHA-256, parent/member path or extraction range, address-space relevance, evidence class, source coverage, availability and use. `Artifact-confirmed` describes a checked byte identity or structure; it does not mean that the artifact is public or that a vendor operation was executed.

Measured package records below come from the private audit record `inventory/mounted_artifacts.json` (`SRC-BINARIES`), and SFX member/extraction records come from the private audit record `inventory/sfx_inspection.json` (`SRC-SFX`). These identifiers describe retained audit inputs and are **not paths to files distributed in this public repository**. Both records contain offline byte measurements rather than hardware tests. A `historical` record preserves a baseline report identity; it is not presented as a new rehash.

### Recovered source identities

These are offline measurements of supplied source exports or recovered Project/Library records, not firmware compatibility claims. Their source text is curated into the linked technical pages; original private reports and shell transcripts are not distributed wholesale.

| Source ID | Representation and size | SHA-256 | Coverage / use |
|---|---|---|---|
| S2 · `P916F-STX_BIOS_1.15_full_option_audit.md` | Authorized raw file export; 140821 bytes, 859 lines | `525c46baa22a0d868b56c36d4094cc13f314bdfccba239f4332c3159983e5271` | Full static audit; every inventory row in [setup reference](bios-setup-options.md) |
| S3 | Project text extraction; 33532 bytes, 938 logical text lines (937 LF terminators; no final newline) | `fd33e0fc94a69c558156ae82e124a213f891627daa788fc65d84e03c0d73cc16` | Full extracted text, not original raw-byte identity or complete DSDT; complete [THMM](thermal-performance.md#thmm-conditional-alib-parameter-sequence) and selected technical excerpts |
| S6 · April text source | Recovered raw text file; 7465 bytes | `21d40ec647d5859a2b6feb19ec206dd296eaba45666d7d3e8bc4b17da5966d34` | Historical April `SPFM` map with `ECMD(0x91)` / `ECMD(0x92)` |
| S6 · `dsdt.dsl` | Recovered complete historical disassembly; 253103 bytes | `43f4b40e70ac867416b9137e3d41ae12ead76224038dcfb6a4943bfc41466296` | Complete historical DSDT text with the same `0x91` / `0x92` map; exact same-session firmware association remains unresolved |
| S11 | Authorized raw file export; 12706 bytes, 210 logical text lines (209 LF terminators; no final newline) | `994d5a0b7df61bccec35d1ef6bb7a7818aa6e9ec33dbef430a5b6307ac1ee0a0` | Full device/audio capture examined; curated [audio](audio.md#recovered-device-and-kernel-capture) and [Linux inventory](linux.md#audio-and-peripheral-inventory) |
| S12 | Project text representation; 15627 bytes | `08cbb63b25f81ad1c2e3523094c91800130e5106a22e6d612f978eb465616b65` | ACPI extraction command/output and DSDT header; not raw `dsdt.dat` bytes |
| S13 | Project text representation; 12085 bytes | `4492ee79f2f7eb142e50fa6f0f2c83d025a0ce31a205a7f6724777044c5e1842` | Live H2RAM/GFNS correlation capture; raw words are not independently calibrated RPM |
| S14 · `SREP_Config_P916F-STX_1.15_QuietBoot.cfg` | Project text representation; 152 bytes | `b7359ed2189796efbfff6be5e82c0d7ba7ba969fcef9156366b25949919297ef` | Candidate runtime SetupUtility suppression patch; not associated conclusively with the successful photographed session |
| S14 · `SREP_Config_P916F-STX_1.15_QuietBoot_v2.cfg` | Project text representation; 158 bytes | `8a56d7d2c89cb5fa48a49669e18f75797cfebef72f95e64fee40bf6ffafac022` | Second candidate using a different patch operation; not promoted to known-good |

Exact File IDs and classification limits are in the [source register](research-sources.md#project-sources). A checked source hash establishes byte identity of that representation only; it does not independently validate the source analyst's interpretation or certify hardware behavior. The `xingyao.fw` filename appears in S11 but its bytes, size, digest and origin are not recovered.

## Baseline machine-specific artifacts

The following records preserve the baseline identities and landmarks. Their source coverage is retained report or historical analysis unless explicitly marked as a new measurement below.

### Current raw 32 MiB ROM

The current image and preferred EC carve are the first two records in the baseline registry. Their exact private Library objects are now located, correcting the earlier wording that the source objects themselves were not retained. Raw-byte materialization was unavailable in this audit, so the historical digests below were not recomputed.

| Artifact ID | Canonical filename | Type / size | SHA-256 | Parent, member path or range | Address-space relevance | Evidence class / source coverage | Availability | Use or boundary |
|---|---|---|---|---|---|---|---|---|
| `ROM-P916F-CURRENT` | `P916F-STX-current-ROM.bin` | Raw 32 MiB firmware image; `33,554,432` bytes / `0x2000000` | `77043505b6f42e4a482110a7ba0c7e12ba6b1db28fdaed2743c28578bbf76cd7` | none | raw-ROM file offsets | Artifact-confirmed (historical); exact Library object located as S16, historical digest not rehashed in this audit | Private source object; not distributed; raw-byte materialization unavailable in current audit | Preferred parent for machine-specific static claims; EC carve at `0x081000` |
| `EC-P916F-IT5571-109` | `P916F-IT5571-EC-1.09.bin` | EC image; `0x20000` bytes / 128 KiB | `42c117f00c130c5e533be93ee1657401ac4d687255ed1b2250f74d3cc79397ea` | `ROM-P916F-CURRENT + 0x081000`, length `0x20000` | raw-ROM carve; child digest is distinct from parent | Artifact-confirmed (historical); exact Library object located as S16, historical digest not rehashed in this audit | Private source object; not distributed; raw-byte materialization unavailable in current audit | Preferred EC image for detailed battery analysis; second bank is sparse but real code/data |

### Other baseline identities

| Artifact ID | Canonical filename | Type / size | SHA-256 | Parent, member path or range | Address-space relevance | Evidence class / source coverage | Availability | Use or boundary |
|---|---|---|---|---|---|---|---|---|
| `UPDATER-OUTER-115` | `STX_SKU2_1.15.zip` | Outer vendor archive; size not retained | not retained | outer archive; contains `STX_SKU2_1.15.exe` | archive/container namespace | Not established; filename and nesting retained in baseline | Not retained | Do not substitute the EXE digest for this outer ZIP |
| `EC-UPDATER-CARVE-18000` | `STX_SKU2_1.15.exe -> isflash.bin` EC carve | Earlier updater EC-like extraction; `0x18000` bytes / 98,304 bytes | `030ec5da8b5f027d2461af98b92416eab4a526734bee4b3032e5d9042d016023` | `isflash.bin + 0x268E30`, length `0x18000` | updater-image offset inside `isflash.bin`, not raw-ROM | Artifact-confirmed (historical; independently repeated in measured SFX record below); retained report | Not distributed | Contains `ITE EC-V14.6`, `IT557x V1.09 E00 - 20230831`, `MECHREVO`, `VER:01.0F.00`; not interchangeable with the 128 KiB raw-ROM carve |
| `PKG-CC-GX-HISTORICAL` | `ControlCenter_5.56.1.13_Mechrevo_GX.zip` | GX Control Center package; size not retained in baseline | `d081d2b338068ca6fd1099be2f6762d522c1223796f20a800d47034842423449` | outer package; historical useful paths listed in baseline | archive/container namespace | Artifact-confirmed (historical report); package member sizes/digests were not retained in baseline | Not distributed | Generic OEM software evidence; not proof of P916F runtime support |
| `BUNDLE-CHARGE-REVERSE` | `P916F-charge-reverse.tar.gz` | Compact service/component bundle; exact located object size `10,092,171` bytes | not retained | outer bundle; members included `ACPIDriverDll.dll`, `GCUService.exe`, `service.ini` | archive/container namespace | Source-located at bundle level; individual historical child identities retained; parent digest not rehashed | Private source object; not distributed; raw-byte materialization unavailable in current audit | Bundle digest must not be replaced by a child digest; no `ACPIDriver.sys` was present in the retained extraction |
| `DLL-ACPIDRIVER-HISTORICAL` | `ACPIDriverDll.dll` | Native service component; size not retained | `97d7115943600c2a09951440859f9bd75fd0d8bff9db49c296c868b49df8c8c6` | `P916F-charge-reverse.tar.gz -> ACPIDriverDll.dll` (historical path) | extracted member namespace | Artifact-confirmed (historical); child hash retained, size/source bytes not retained | Not distributed | Historical interface landmarks: `\\.\ACPIDriver`, ReadEC IOCTL `0x9C40A488`, WriteEC IOCTL `0x9C40A48C`; not a published driver |
| `EXE-GCUSERVICE-HISTORICAL` | `GCUService.exe` | Native service component; size not retained | `01225ef470420d50e51bc541d63dd5ed321835d40c4209106da9908c8f277a9c` | `P916F-charge-reverse.tar.gz -> GCUService.exe` (historical path) | extracted member namespace | Artifact-confirmed (historical); child hash retained, size/source bytes not retained | Not distributed | Historical names/types only; important method bodies were not recovered as normal unobfuscated logic |
| `BOOT-GIF-115` | OEM boot animation resource | GIF; `800 x 600`, 60 frames, approximately 1.74 s; exact size/digest not retained | not retained | BIOS 1.15 resource associated with `OemBadgingSupportDxe`; no raw container offset promoted | firmware-resource namespace; do not treat duration as measured boot time | Artifact-confirmed (historical baseline metadata); exact resource extraction remains pending | Not distributed | GUID `931F77D1-10FE-48BF-AB72-773D389E3FAA`; exact size/hash and extraction offsets remain P11 |
| `BMOF-WQBA` | `WQBA.bmof` | Extracted BMOF buffer; 1,092 bytes / `0x444` | not retained | DSDT `BMOF` buffer; extraction origin not retained | extracted BMOF buffer namespace | Artifact-confirmed (historical metadata); decoded schema not exported | Not distributed | Prefix `46 4F 4D 42 01 00 00 00 34 04 00 00 5C 10 00 00` (`FOMB`); BMOF GUID `05901221-D566-11D1-B2F0-00A0C9062910`; schema remains P06 |

The earlier updater carve also contains the retained string `AMD Motherboard`. The compact charge-reverse extraction included AirplaneDriver-related files in addition to the listed native/service components. Both compact bundles are derived research collections, not independently authenticated vendor distributions.

## Newly recovered Project/Library artifacts

These records were recovered after the initial documentation consolidation. They are private research inputs and are not added to the public repository as binaries.

| Artifact ID | Canonical filename | Type / size | SHA-256 | Evidence / boundary | Availability |
|---|---|---|---|---|---|
| `WIN-NAHIMIC-EXPORT` | `NahimicExport.zip` | ZIP; 20,004,555 bytes; 112 entries | `4099d9631368deff8bc39ee07a948134097d56b75db39f6e8b0ff3b0a9de0906` | Artifact-confirmed raw bytes, S15. Contains Nahimic/A-Volute app state, APO/service/registry evidence and EQ presets; not a complete runtime DSP graph | Private source; not distributed |
| `SREP-REVEAL-PHOTO` | `image-1789545520742.jpg` | JPEG; 373,251 bytes | `38b11bb1a736a9373632c78b1949128164cfb7a842b0c67068196cc1fa5d52db` | Artifact-confirmed raw bytes, S14. Shows the expanded BIOS Boot page; does not identify exact successful patcher/config | Private source; not distributed |

The Nahimic archive's EQ preset selection and endpoint/APO evidence are documented in [`audio.md`](audio.md). The photograph/configuration evidence and remaining successful-session association gap are documented in [`srep-runtime-reveal.md`](srep-runtime-reveal.md).

## Uploaded package identities

The seven package rows are the complete measured set in `mounted_artifacts.json`. Canonical names remove only the local upload disambiguator `(1)` or `(2)` where the baseline or package identity supplies the unduplicated name; the local alias remains exact. Every parent was measured offline and is unavailable for distribution.

| Artifact ID | Canonical filename | Local upload alias | Type | Size (bytes) | SHA-256 | Parent/member path | Evidence / source coverage | Availability |
|---|---|---|---|---:|---|---|---|---|
| `PKG-01` | `ControlCenter_5.56.1.13_Mechrevo_GX.zip` | `ControlCenter_5.56.1.13_Mechrevo_GX(1).zip` | ZIP package | 281,753,965 | `d081d2b338068ca6fd1099be2f6762d522c1223796f20a800d47034842423449` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-02` | `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | ZIP package | 7,607,637 | `b6adb4a9cb84046cd342fe3b05fe6c16ab0e801040fa0f33d6b975d3b15768f9` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-03` | `OSD(SKU1&SKU2).zip` | `OSD(SKU1&SKU2).zip` | ZIP package | 1,520,070 | `9ecdcc7287f043268126cd468f9126b03319b723c1a006bd344d1c7e75a8f109` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-04` | `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15_bundle.zip` | ZIP package | 2,628,319 | `8982c65712910ea03e5e6336823b84c93802f37c60b6392ed22934a6130408ab` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-05` | `STX_SKU2_1.09.exe` | `STX_SKU2_1.09.exe` | standalone executable | 17,757,829 | `397841144f18ada42418993dbd37238b777a0f4126f3d53b4910908ac75ee5f1` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-06` | `STX_SKU2_1.15.exe` | `STX_SKU2_1.15.exe` | standalone executable | 18,419,646 | `11718b7f48a13ca08f627c1c3103cf4d1a3ee6857e15bcb165210c11e0ec446a` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |
| `PKG-07` | `jxgm_21911.zip` | `jxgm_21911(2).zip` | ZIP package | 33,456,863 | `b25bbac157abe916258d614afaabc4d39ee6be8dddd35229b998a9066acd9a2d` | outer upload (no parent) | Artifact-confirmed; recomputed mounted bytes, `SRC-BINARIES`; not a hardware test | Not distributed; mounted upload excluded from the public evidence set |

### Selected measured members

Selected member paths are copied from the structured inventory, including their package-root directory. The full parent/member path is intentional: a basename alone is not an identity.

| Member ID | Parent package / local alias | Full member path | Size (bytes) | SHA-256 | Evidence / source coverage | Availability |
|---|---|---|---:|---|---|---|
| `MEM-01` | `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip -> InsydeH2OEZE_x86_WINx64_100.00.03.11/H2OEZE-x64.exe` | 12,517,888 | `aadfb78a61cfb3862c3fea77eb662333334d35782c46344339e00d2d96554535` | Artifact-confirmed; selected member recomputed in `SRC-BINARIES` | Not distributed; parent upload not included in public evidence set |
| `MEM-02` | `OSD(SKU1&SKU2).zip` | `OSD(SKU1&SKU2).zip -> 21_OSD/Apps/MechrevoOSDInstaller013.exe` | 2,009,296 | `97635596c08214f615666ac391d3dfa6e6e39085dc1d857207436de4ab4eb678` | Artifact-confirmed; selected member recomputed in `SRC-BINARIES` | Not distributed; parent upload not included in public evidence set |
| `MEM-03` | `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15_bundle.zip -> P916F_STX_H2OFFT_1.15/platform.ini` | 59,202 | `ba7bbee754ec240fcba7ad8059261c4b417f1f2a3bd32f15b5d24354978a854d` | Artifact-confirmed; selected member recomputed in `SRC-BINARIES` | Not distributed; parent upload not included in public evidence set |
| `MEM-04` | `P916F_STX_H2OFFT_1.15_bundle.zip` | `P916F_STX_H2OFFT_1.15_bundle.zip -> P916F_STX_H2OFFT_1.15/H2OFFT-Wx64.exe` | 3,004,280 | `86f336d74c2ab951d04f35143c5efaabce94a4ebe9dd87fd62b018ad7103adb0` | Artifact-confirmed; selected member recomputed in `SRC-BINARIES` | Not distributed; parent upload not included in public evidence set |
| `MEM-05` | `jxgm_21911(2).zip` | `jxgm_21911(2).zip -> jxgm_21911/jxgmdjfwzx/OTA_setup.exe` | 34,178,800 | `6fa40846658906a3004114c0c06b2eb5a6e44202b2b436dd2e857e9b81ed8a3c` | Artifact-confirmed; selected member recomputed in `SRC-BINARIES` | Not distributed; parent upload not included in public evidence set |

### Package enumeration coverage

The inventory also records ZIP member counts even where no selected member digest was requested. A zero in the selected column means no nested digest was retained for that package, not that the archive was empty.

| Local upload alias | Enumerated members | Selected member digests |
|---|---:|---:|
| `ControlCenter_5.56.1.13_Mechrevo_GX(1).zip` | 193 | 0 |
| `InsydeH2OEZE_x86_WINx64_100.00.03.11.zip` | 247 | 1 |
| `OSD(SKU1&SKU2).zip` | 2 | 1 |
| `P916F_STX_H2OFFT_1.15_bundle.zip` | 14 | 2 |
| `STX_SKU2_1.09.exe` | 0 | 0 |
| `STX_SKU2_1.15.exe` | 0 | 0 |
| `jxgm_21911(2).zip` | 2 | 1 |

## Embedded H2OFFT SFX inspection

`SRC-SFX` inspected `STX_SKU2_1.15.exe` offline. The source EXE has measured SHA-256 `11718b7f48a13ca08f627c1c3103cf4d1a3ee6857e15bcb165210c11e0ec446a` and an embedded 7-Zip signature at `0x3946f`. The source EXE is also the measured `STX_SKU2_1.15.exe` package row above; this is one identity observed through two inventory records, not two independent downloads.

| SFX member ID | Full parent/member path | Size (bytes) | SHA-256 | Evidence / source coverage | Availability |
|---|---|---:|---|---|---|
| `SFX-MEM-01` | `STX_SKU2_1.15.exe -> Ding.wav` | 105,886 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-02` | `STX_SKU2_1.15.exe -> isflash.bin` | 35,626,768 | `4dd5ebb5fb5f23b0de8df0cbd60d6453cfe1f5a22f69ef34ceb24432d80094c1` | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-03` | `STX_SKU2_1.15.exe -> Microsoft.VC90.CRT.manifest` | 526 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-04` | `STX_SKU2_1.15.exe -> Microsoft.VC90.MFC.manifest` | 550 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-05` | `STX_SKU2_1.15.exe -> platform.ini` | 59,202 | `ba7bbee754ec240fcba7ad8059261c4b417f1f2a3bd32f15b5d24354978a854d` | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-06` | `STX_SKU2_1.15.exe -> H2OFFT.cat` | 10,574 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-07` | `STX_SKU2_1.15.exe -> InterToolx64.efi` | 1,355,232 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-08` | `STX_SKU2_1.15.exe -> H2OFFT.inf` | 6,668 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-09` | `STX_SKU2_1.15.exe -> FlsHook.exe` | 42,440 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-10` | `STX_SKU2_1.15.exe -> H2OFFT-Wx64.exe` | 3,004,280 | `86f336d74c2ab951d04f35143c5efaabce94a4ebe9dd87fd62b018ad7103adb0` | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-11` | `STX_SKU2_1.15.exe -> BiosImageProcx64.dll` | 286,664 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-12` | `STX_SKU2_1.15.exe -> mfc90u.dll` | 1,679,864 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-13` | `STX_SKU2_1.15.exe -> msvcp90.dll` | 851,456 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-14` | `STX_SKU2_1.15.exe -> msvcr90.dll` | 627,200 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-MEM-15` | `STX_SKU2_1.15.exe -> H2OFFT64.sys` | 48,008 | not retained | Artifact-confirmed; offline SFX member listing in `SRC-SFX` | Not distributed; source EXE not included |
| `SFX-EC-CARVE` | `STX_SKU2_1.15.exe -> isflash.bin + 0x268E30` (length `0x18000`) | 98,304 | `030ec5da8b5f027d2461af98b92416eab4a526734bee4b3032e5d9042d016023` | Artifact-confirmed; offline extraction measurement in `SRC-SFX`; digest matches the historical earlier-EC record | Not distributed |

The SFX-selected H2OFFT metadata has two version namespaces: textual `FileVersion`/`ProductVersion` `6.73`, and numeric `VS_FIXEDFILEINFO` `6.7.3.0` inspected at H2OFFT file offset `0x2D7660`. Neither establishes a build date or live IHISI versions; those require P09.

The embedded help strings `-g`, `-iv` and `-pq` describe intended utility syntax. They are static strings, not a retained invocation, dump, protection-map output or successful firmware operation.

## Preserved static landmarks

The artifact registry keeps the following baseline landmarks even when the source bytes are not distributed:

- BIOS 1.15 boot resource: `OemBadgingSupportDxe`; GUID `931F77D1-10FE-48BF-AB72-773D389E3FAA`; format GIF; `800 x 600`; 60 frames; approximately 1.74 s. Related `BootGraphicsResourceTableDxe` GUID: `B8E62775-BB0A-43F0-A843-5BE8B14F8CCD`.
- Type-54 / `-edt4f` callback in exact P916F BIOS: `ChipsetSvcSmm`, protocol slot `+0xA8`, callback RVA `0x221C`; compares requested type with `0x50`, and the examined nonmatching branch returns `EFI_UNSUPPORTED`.
- Type-6D target provisioning: GUID `DACFAB69-F977-4784-8AD8-7724A6F4B440`; raw-ROM FDM table at `0x1D7C000`, 47 entries scanned; target absent from that table and from the observed Windows ESRT. Raw-ROM and live ESRT absence are separate evidence types, not a universal statement about all logo paths.
- SetupUtility FFS GUID `FE3542FE-C1D3-4EF8-657C-8048606FF670`; Boot formset GUID `2D068309-12AC-45AB-9600-9187513CCDD8`; SystemConfig VarStore GUID `A04A27F4-DF00-4D42-B552-39511302113D`.
- Retained runtime setup observation: `Setup[0x6E] = 0x01` for Quiet Boot at the time of the test. The SREP runtime reveal and the untested permanent PE landmark are separate records.
- The compact bundle's native component used the historical Windows device path `\\.\ACPIDriver`, ReadEC IOCTL `0x9C40A488` and WriteEC IOCTL `0x9C40A48C`. These are provenance landmarks, not an executable interface supplied by this repository.
- Historical GX package paths retained from the baseline package context: `AiStoneService/GCUBridge.exe`, `AiStoneService/MyControlCenter/ACPIDriverDll.dll`, `AiStoneService/MyControlCenter/GCUService.exe`, `AiStoneService/MyControlCenter/GCUServicePlugin.dll` and `AiStoneService/MyControlCenter/GCUUtil.exe`. The measured package inventory has no selected member digests for these paths; they are not promoted to P916F runtime support.

## Missing identities and distribution policy

The following unresolved identities are deliberate and have a specific gate:

| Missing identity | Current evidence | Gate |
|---|---|---|
| Outer `STX_SKU2_1.15.zip` size/digest | The measured child EXE is nested inside it and cannot identify the outer ZIP | P17b: recover the exact outer archive bytes and hash |
| `P916F-charge-reverse.tar.gz` digest | Exact private parent object is located at 10,092,171 bytes, but raw-byte materialization was unavailable; a child hash still cannot identify the parent | P17a: rehash exact parent bytes when authorized/exportable |
| Current raw ROM and 128 KiB EC rehash | Exact private Library objects are located with expected sizes; historical digests remain retained but were not recomputed | P16: rehash exact source bytes when raw-byte export is available |
| Exact boot GIF size/digest/extraction offsets | Baseline geometry/GUID metadata does not establish a container offset | P11: recover the identified resource and extraction record |

No vendor download URL or upload timestamp is used as a substitute for a missing identity. The repository does not publish raw ROM, EC images, vendor EXE/SYS/DLL/GIF resources or private reports. A matching digest identifies bytes only; it does not certify vendor authenticity, P916F compatibility or safe execution.

## Provenance rules for new records

For each future low-level artifact, record the exact product/unit and BIOS/EC version, source filename and SHA-256 when available, parent chain, offset space, address class, evidence class, source coverage and availability. Keep raw-ROM, updater-image, extracted/decompressed, FFS-relative, PE file/RVA, EC CODE/XRAM, host physical/MMIO/I/O and VarStore offsets in separate namespaces.
