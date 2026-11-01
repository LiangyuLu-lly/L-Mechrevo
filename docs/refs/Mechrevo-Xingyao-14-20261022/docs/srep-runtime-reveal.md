# Runtime visibility of setup forms

## Scope

The firmware investigation distinguishes static form presence, runtime visibility changes and persistent firmware modification. Sources are the baseline report S1, setup audit S2, retained candidate configuration S7 and the newly recovered photograph/configuration records S14 in the [project source register](research-sources.md#project-sources). This page deliberately does not publish a runnable bypass configuration.

## Static form structure

The setup audit identifies OEM SetupUtility formsets and separate AMD PBS/CBS HII formsets. Some menu references or child blocks are suppressed by constant conditions; other controls depend on settings or hardware conditions. A form without a static reference path can still be installed or reached dynamically.

These categories are not interchangeable. Exposing a top-level formset does not necessarily remove child suppression, and removing suppression can reveal controls for absent or unsupported hardware. The [option inventory](bios-setup-options.md) retains the distinct visibility classes and the source's selected row counts.

## Retained candidate configurations

The project source collection identifies `P916F_STX_115_SREP_full_reveal.cfg` (S7). Its complete displayed text is retained privately as an inert source excerpt, but its directives are not reproduced here because they would form a reusable visibility-bypass configuration. The source names H2OFormBrowserDxe visibility records for three identified formsets:

| Formset | GUID |
|---|---|
| AMD PBS | `B863B959-0EC6-4033-99C1-8FD89F040222` |
| AMD CBS | `B04535E3-3004-4946-9EB7-149428983053` |
| Power | `A6712873-925F-46C6-90B4-A40F86A0917B` |

It also names `SuppressIfPatcher` and a subsequent load/execute operation for `SetupUtilityApp`. This establishes the configuration's intended operations, not their successful execution on the investigated machine.

Two additional Quiet-Boot candidate records were recovered as S14:

| Candidate | Recovered text representation | Boundary |
|---|---|---|
| `SREP_Config_P916F-STX_1.15_QuietBoot.cfg` | 152 bytes; SHA-256 `b7359ed2189796efbfff6be5e82c0d7ba7ba969fcef9156366b25949919297ef` | Targets the SetupUtility suppression-pattern change with a normal patch operation |
| `SREP_Config_P916F-STX_1.15_QuietBoot_v2.cfg` | 158 bytes; SHA-256 `8a56d7d2c89cb5fa48a49669e18f75797cfebef72f95e64fee40bf6ffafac022` | Targets the same pattern with a fast-patch operation |

The exact directive bodies remain private evidence rather than a published turnkey patch recipe. Their existence and hashes establish the candidate records, not which one produced the successful session.

## Recovered runtime photograph

The post-reveal BIOS photograph is retained as source S14; its exact private locator is recorded in the [source register](research-sources.md#project-sources):

```text
filename:  image-1789545520742.jpg
size:      373251 bytes
SHA-256:   38b11bb1a736a9373632c78b1949128164cfb7a842b0c67068196cc1fa5d52db
```

The photographed Boot page visibly contains at least:

```text
Quick Boot
Quiet Boot
Network Stack
PXE Boot Capability
PXE / HTTP Boot Retry Policy
Power Up In Standby Support
Storage PCI Option ROM Access
ESATA drive boot access right
Add Boot Options
ACPI Selection
USB Boot
UEFI OS Fast Boot
```

This is direct photographic evidence that an expanded Boot page was visible on the investigated MECHREVO system. It is stronger than the earlier retained prose-only report for the page contents.

The photograph does **not** by itself identify:

- the SREP executable build or digest;
- the exact configuration file loaded in that boot;
- whether the candidate v1, v2 or another configuration produced the page;
- the exact order of patcher messages before entering SetupUtility;
- whether changing any newly visible option is safe or functional.

Accordingly, the page reveal is a recovered runtime observation, while the successful-session configuration association remains pending.

## Quiet Boot value and behavior

The baseline records QuestionId `0x1064`, `SystemConfig` offset `0x6E`, Disabled=`0x00`, Enabled=`0x01`, and the runtime read `Setup[0x6E]=0x01`. These observations do not establish a completed save/change experiment or the resulting boot presentation.

A runtime form-visibility change and a subsequent setup-variable save are different operations. The form patch being applied in memory does not establish that later variable writes had no persistent effects. Conversely, the retained `Quiet Boot` read does not establish a successful change or the behavior of every boot stage.

## Permanent SetupUtility candidate

The baseline analysis records a suppression expression near **extracted SetupUtility PE file offset** `0x2636A0` and a proposed `0x46` to `0x47` byte change. This is an image-specific, extracted-PE file-offset landmark, not a raw-ROM offset or a cross-version patch specification. The modification was not flashed or live-tested and is not equivalent to the reported runtime reveal.

The candidate byte change is retained as a reverse-engineering landmark only. No patch recipe, write instruction or permanent firmware modification is supplied.

## Reproduction and pending gates

A fully reproducible account still requires the exact patcher build, the final configuration digest used in the successful run, the firmware/module identity and the console result associated with the recovered photograph. Pending gate **P13** is therefore `PHOTO_RECOVERED / NEEDS_CONFIRMATION`: the photographic result is no longer missing, but no candidate configuration is promoted to known-good without its session linkage.

Unknown hardware behavior and missing source material are tracked in [open questions](open-questions.md) and [documentation status](documentation-status.md#pending-evidence).
