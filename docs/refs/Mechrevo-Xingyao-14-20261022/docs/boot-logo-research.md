# Boot-logo and OEM-badging investigation

## Scope and conclusion

The investigation examined whether the P916F-STX BIOS 1.15 boot animation could be replaced through a dedicated logo-update service rather than rebuilding and reflashing firmware regions. The retained evidence identifies blockers in two generic Insyde paths. **No working logo-only update mechanism has been established for the examined build.** This is a bounded result for those paths, not a proof that every possible OEM-specific mechanism is absent.

Sources are the baseline report S1 and the retained logo analysis S10 in the [project source register](research-sources.md#project-sources). Module landmarks and provisioning results are preserved from those reports; they were not reproduced from a newly available raw ROM during consolidation.

## Embedded resource and BGRT observation

```text
Resource GUID: 931F77D1-10FE-48BF-AB72-773D389E3FAA
Format:        animated GIF
Dimensions:    800 x 600
Frame count:   60
Duration:      approximately 1.74 s
Association:   OemBadgingSupportDxe
```

The related `BootGraphicsResourceTableDxe` GUID is `B8E62775-BB0A-43F0-A843-5BE8B14F8CCD`.

Recorded Linux BGRT values:

```text
status  = 0
type    = 0
version = 1
xoffset = 1040
yoffset = 387
```

For the recorded 2880-pixel-wide panel, `1040 + 800 + 1040 = 2880`. The geometry is consistent with horizontal centering of an 800-pixel resource. It does not establish that the BGRT object is itself the animation or that BGRT offers a persistent logo-update mechanism. The status value is retained without treating it as an unconditional displayed-image assertion.

The resource's exact byte size, SHA-256 and decompressed-container offsets require the identified resource/container and extraction record (P11, [pending evidence](documentation-status.md#pending-evidence)). A decompressed-container offset must not be relabeled as a raw-ROM offset.

## Examined Type-0x54 extra-data path

The retained generic Insyde analysis maps H2OFFT extra-data type 4, represented by the option name `-edt4f`, to OEM extra-data type `0x54`. Support for an option in a utility does not establish a corresponding writer in the target firmware.

The examined P916F `ChipsetSvcSmm` callback was identified as:

```text
Module:       ChipsetSvcSmm
Protocol slot: +0xA8
Callback RVA:  0x221C
```

The decisive disassembly recorded in the report is:

```asm
221C  cmp cl, 50h
221F  je  222C
2221  mov rax, EFI_UNSUPPORTED
222B  ret
```

The examined callback handles type `0x50`, associated in the report with the OA3 path, and rejects other values through `EFI_UNSUPPORTED`. Type `0x54` is therefore unsupported at this callback. Type `0x6D` also falls through this callback, but the separate authenticated mechanism must be evaluated on its own path rather than inferred solely from this comparison.

This is the basis for rejecting the examined Type-0x54 raw-logo route on the tested build. It does not justify attempting the utility option against the machine.

## Examined authenticated Type-0x6D path

The retained analysis describes a different generic route associated with `-logoupdate`: an authenticated image, PKCS#7 verification and a dedicated destination identified by GUID:

```text
DACFAB69-F977-4784-8AD8-7724A6F4B440
```

The machine-specific results recorded by the investigation were:

| Check | Recorded result | Evidence type / address context |
|---|---|---|
| Windows ESRT inspection | No entry for the target GUID | Live exposed-resource observation |
| Raw-ROM firmware device map | No matching entry among 47 entries | Static provisioning analysis |
| Raw-ROM FDM location | `ROM+0x1D7C000` | Raw-image file offset |

The absence of the target in ESRT is not, in isolation, proof that a raw region does not exist. The FDM check supplies the separate static evidence used by the report. Taken together, the examined Type-0x6D path lacks its expected provisioned target in this build.

A complete parsed FDM listing and original ESRT capture remain desirable for independently reproducing the negative result. The retained report's 47-entry count must not be relabeled as a newly performed scan. Absence of this target also does not establish absence of every other logo-related type or region.

## Result matrix

| Mechanism | Finding on the examined build | Evidence class | Source coverage | Limitation |
|---|---|---|---|---|
| Type `0x54`, raw/project-specific logo data | Examined chipset callback returns `EFI_UNSUPPORTED` | Static-confirmed | Retained analysis report; underlying module bytes were not redistributed | Applies to the traced callback path |
| Type `0x6D`, authenticated logo component (ESRT) | Expected target GUID absent from the recorded ESRT result | Live-confirmed | Retained report of the machine observation; original capture not included | Applies to the exposed-resource check |
| Type `0x6D`, authenticated logo component (FDM) | Expected target GUID absent among the recorded 47 FDM entries | Static-confirmed | Retained report of raw-image analysis; raw bytes not available for a new scan | Applies to the identified raw-ROM FDM scan |
| Another OEM-specific mechanism | Not established | Not established | No identified source | Not excluded by the two negative results |
| Persistent replacement inside firmware structures | Not tested | Not tested | No retained write experiment | Requires separate analysis of container layout and update integrity |

Locating a GIF within firmware is not evidence of a safe replacement procedure. FFS/FV layout, compression, alignment, authentication and the actual write path are distinct concerns. `finding the GIF` is therefore not equivalent to having a safe logo replacement method.

## Quiet Boot is a separate control

The recorded Quiet Boot setting is QuestionId `0x1064`, `SystemConfig` offset `0x6E`, VarStore GUID `A04A27F4-DF00-4D42-B552-39511302113D`, with zero Disabled and one Enabled. The baseline runtime read reported `Setup[0x6E]=0x01`.

SREP runtime visibility and the appearance of the Boot page were reported separately. Visibility does not prove the result of saving Quiet Boot, eliminate variable-write effects or establish that disabling it suppresses every stage of OEM presentation. No verified custom-logo replacement follows from this setting.

The [runtime-visibility report](srep-runtime-reveal.md) distinguishes source presence, page reveal and persistent modification. The [setup inventory](bios-setup-options.md) keeps defaults separate from live values.

## Research boundary

The evidence supports retaining both negative paths and avoiding speculative logo writes. In particular:

- H2OFFT help syntax is not evidence of a P916F Type-0x54 writer;
- the expected Type-0x6D target must not be assumed when the recorded target is absent;
- a sibling P916F firmware's logo layout is not interchangeable;
- an embedded GIF does not make direct replacement safe.

No turnkey H2OFFT invocation, cross-flash recommendation or known-good permanent SetupUtility modification is published here. Exact missing artifacts and captures are listed in [documentation status](documentation-status.md#pending-evidence).
