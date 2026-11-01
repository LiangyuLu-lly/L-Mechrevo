# BIOS setup options and reachability

## Scope and source boundary

The complete static inventory from `P916F-STX_BIOS_1.15_full_option_audit.md` is reproduced below: P916F-STX BIOS 1.15, analysed package `STX_SKU2_1.15.zip` / InsydeH2O. Source S2 is the authorized raw-file export; its exact locator, digest and export identity are recorded in the [source register](research-sources.md#project-sources). All five source tables are preserved, including reference/action rows, duplicate and blank labels, VarStores, offsets, choices, defaults and analyst risk annotations. This is full coverage of the identified audit, not a claim that every firmware implementation or live behavior has been validated.

### Counts in this page

The importer counted the following data rows independently of the source's stated totals:

- **8** — Verified formset GUIDs.
- **28** — Static SetupUtility form reachability.
- **151** — All SetupUtility questions/actions.
- **204** — AMD PBS (`AmdPbsSetupDxe`).
- **416** — AMD CBS (`CbsSetupDxeSTX`).

The source reports 204 PBS and 416 CBS named non-reference controls; the imported table counts agree. SetupUtility questions/actions, formsets and reachability rows are separate categories, not added to those control totals. Source-import gate P01 is closed; live-value gate P12 remains open.

## Interpretation rules

The source legend uses these static categories:

| Source label | Meaning in this reference |
|---|---|
| `VISIBLE/UNSUPPRESSED` | Static IFR does not hide the item. A top-level tab can still depend on Insyde FrontPage/FormBrowser policy. |
| `HARD-HIDDEN` | A constant `SuppressIf TRUE`, or a parent menu reference hidden by that condition, statically hides the item. |
| `CONDITIONAL` | Visibility depends on another setting or hardware condition. |
| `ORPHAN/DYNAMIC` | A form exists, but no static root-form reference path was found; runtime injection remains possible. |
| `AMD PBS/CBS external formset` | A real HII formset that is not statically linked into the OEM SetupUtility tree. It must not be described as an ordinary hidden child of Advanced. |

Static visibility is not a live screenshot result. An IFR default is not a current variable value, and an option's presence does not confirm the associated hardware or behavior. The `Risk` column reproduces the source analyst's annotation; it is not a measured safety or reliability result.

Source decimal enum values and default notation are preserved as IFR metadata, not protocol bytes. Repeated labels at distinct offsets and the Wireless KVM Mouse Protocol anomaly (`Absolute = 0`, `Auto = 0`) are retained, not silently repaired. Empty labels and em dashes mean the source supplies no label or choice data; they are not inferred values.

## SetupUtility formsets

| Formset | GUID | Notes |
|---|---|---|
| Power | `A6712873-925F-46C6-90B4-A40F86A0917B` | Insyde SetupUtility formset |
| Advanced | `C6D4769E-7F48-4D2A-98E9-87ADCCF35CCC` | Insyde SetupUtility formset |
| Main | `C1E0B01A-607E-4B75-B8BB-0631ECFAACF2` | Insyde SetupUtility formset |
| Boot | `2D068309-12AC-45AB-9600-9187513CCDD8` | Insyde SetupUtility formset |
| Security | `5204F764-DF25-48A2-B337-9EC122B85E0D` | Insyde SetupUtility formset |
| Exit | `B6936426-FB04-4A7B-AA51-FD49397CDC01` | Insyde SetupUtility formset |
| AMD PBS | `B863B959-0EC6-4033-99C1-8FD89F040222` | Separate `AmdPbsSetupDxe` HII formset |
| AMD CBS | `B04535E3-3004-4946-9EB7-149428983053` | Separate `CbsSetupDxeSTX` HII formset |

## Static SetupUtility form reachability

| Tab | Form | Status |
|---|---|---|
| Advanced | Advanced | VISIBLE |
| Advanced | PCI Express Configurations | HARD-HIDDEN-PARENT |
| Advanced | Boot Configuration | HARD-HIDDEN-PARENT |
| Advanced | Peripheral Configuration | HARD-HIDDEN-PARENT |
| Advanced | SATA Configuration | HARD-HIDDEN-PARENT |
| Advanced | Video Configuration | HARD-HIDDEN-PARENT |
| Advanced | USB Configuration | VISIBLE |
| Advanced | Chipset Configuration | HARD-HIDDEN-PARENT |
| Advanced | ACPI Table/Features Control | HARD-HIDDEN-PARENT |
| Advanced | CPU Related setting | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #0 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #1 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #2 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #3 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #4 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #5 Features | HARD-HIDDEN-PARENT |
| Advanced | APU GPP #6 Features | HARD-HIDDEN-PARENT |
| Advanced | Sata Controller | ORPHAN/DYNAMIC |
| Advanced | USB Ports | VISIBLE |
| Boot | Boot | VISIBLE |
| Boot | Legacy | HARD-HIDDEN-PARENT |
| Boot | Network Boot Hot Key | ORPHAN/DYNAMIC |
| Exit | Exit | VISIBLE |
| Main | Main | VISIBLE |
| Power | Power | VISIBLE |
| Security | Security | VISIBLE |
| Security | Storage Password Setup Page | CONDITIONAL |
| Security | Storage Password Device Setup Page | ORPHAN/DYNAMIC |

## All SetupUtility questions/actions

| Tab | Form | Option | Type | Visibility | Var/offset | Choices/default | Risk |
|---|---|---|---|---|---|---|---|
| Power | Power | ACPI S1 | OneOf | **CONDITIONAL** | `SystemConfig+0x84` | `Disabled` = `0` **(default)**<br>`Enabled` = `1` | Medium |
| Power | Power | Thermal Fan Control | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xED` | `Auto` = `0`<br>`Disabled` = `1` **(default)** | Medium |
| Power | Power | Auto Wake on S5 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x88` | `Disabled` = `0` **(default)**<br>`By Every Day` = `1`<br>`By Day of Month` = `2` | Low/Medium |
| Power | Power |   Wake on S5 Time | Time | **CONDITIONAL** | `SystemConfig+0x89` | — | Low/Medium |
| Power | Power |   Day of Month | Numeric | **CONDITIONAL** | `SystemConfig+0x8C` | — | Medium |
| Power | Power | S5 Long Run Test | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x143` | `Disabled` = `0` **(default)**<br>`Enabled` = `1` | Medium |
| Advanced | Advanced | PCI Express Configurations | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | Boot Configuration | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | Peripheral Configuration | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | SATA Configuration | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | NVMe Configurations | Ref | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Advanced | Advanced | Video Configuration | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | USB Configuration | Ref | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Advanced | Advanced | UMA Frame buffer Size | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x214` | `2G` = `2048` **(default)**<br>`4G` = `4096`<br>`6G` = `6144`<br>`8G` = `8192` | Medium |
| Advanced | Advanced | System Performance Mode | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x212` | `Balance Mode` = `0` **(default)**<br>`Performance Mode` = `1` | Low/Medium |
| Advanced | Advanced | Chipset Configuration | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | ACPI Table/Features Control | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | Advanced | CPU Related setting | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | PSPP Policy | OneOf | **HARD-HIDDEN** | `SystemConfig+0x12B` | `Disabled` = `0`<br>`Performance` = `1`<br>`Balanced-High` = `2`<br>`Balanced-Low` = `3` **(default)**<br>`Power Saving` = `4`<br>`Auto` = `5` | Medium |
| Advanced | PCI Express Configurations | APU GPP #0 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #1 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #2 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #3 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #4 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #5 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | APU GPP #6 Features | Ref | **HARD-HIDDEN** | — | — | Medium |
| Advanced | PCI Express Configurations | PCIE Resizable Bar | OneOf | **HARD-HIDDEN** | `SystemConfig+0x163` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | APU GPP #0 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x12C` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #1 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x130` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #2 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x134` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #3 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x138` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #4 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x13C` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #5 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x140` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | APU GPP #6 Features | GPP Enabled | OneOf | **HARD-HIDDEN** | `SystemConfig+0x141` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | Boot Configuration | Numlock | OneOf | **HARD-HIDDEN** | `SystemConfig+0x8` | `Off` = `0` **(default)**<br>`On` = `1` | Low/Medium |
| Advanced | Peripheral Configuration | Erase fTPM NV for factory reset | OneOf | **HARD-HIDDEN** | `SystemConfig+0xF3` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | Peripheral Configuration | Azalia | OneOf | **HARD-HIDDEN** | `SystemConfig+0x33` | `Disabled` = `0`<br>`Auto` = `1` **(default)** | Medium |
| Advanced | Peripheral Configuration | Secure Biometrics Camera Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0x1DC` | `Disabled` = `0` **(default)**<br>`Enabled` = `1` | Medium |
| Advanced | SATA Configuration | SATA | OneOf | **HARD-HIDDEN** | `SystemConfig+0x111` | `Disabled` = `0`<br>`Auto` = `1` **(default)** | Medium |
| Advanced | SATA Configuration | SATA Configure as | OneOf | **HARD-HIDDEN** | `SystemConfig+0x39` | `AHCI` = `2` **(default)** | High |
| Advanced | SATA Configuration |   AHCI supporting as | OneOf | **HARD-HIDDEN** | `SystemConfig+0xEA` | `Legacy Mode` = `0` **(default)**<br>`UEFI Mode` = `1` | Medium |
| Advanced | SATA Configuration | Force Raid Mode | OneOf | **HARD-HIDDEN** | `SystemConfig+0x112` | `Disabled` = `0` **(default)**<br>`Enabled` = `1` | High |
| Advanced | Sata Controller | SATA Port 0 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0xFF` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 1 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x100` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 2 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x101` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 3 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x102` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 4 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x103` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 5 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x104` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 6 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x105` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 7 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x106` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 0 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x107` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 1 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x108` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 2 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x109` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 3 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x10A` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 4 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x10B` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 5 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x10C` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 6 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x10D` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Sata Controller | SATA Port 7 | OneOf | **ORPHAN/DYNAMIC** | `SystemConfig+0x110` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Video Configuration | HDMI Audio | OneOf | **HARD-HIDDEN** | `SystemConfig+0xF0` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Video Configuration | Brightness Control Method | OneOf | **HARD-HIDDEN** | `SystemConfig+0x145` | `Video BIOS` = `0`<br>`VGA driver` = `1` **(default)** | Medium |
| Advanced | Video Configuration | Enable Dual Vga Controllers | OneOf | **HARD-HIDDEN** | `SystemConfig+0xDF` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Configuration | USB BIOS Support | OneOf | **CONDITIONAL** | `SystemConfig+0x48` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Configuration | USB BIOS Support | OneOf | **CONDITIONAL** | `SystemConfig+0x48` | `Disabled` = `0`<br>`Enabled` = `1` **(default)**<br>`UEFI Only` = `2` | Medium |
| Advanced | USB Configuration | USB2.0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x47` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Configuration | USB Ports | Ref | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Advanced | USB Ports | USB Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x113` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 1 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x114` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 2 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x115` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 3 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x116` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x117` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB3 Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xF6` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB3 Port 1 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xF7` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 1 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x118` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB3 Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xF8` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 2 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x11B` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB3 Port 1 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xFA` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x11F` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | USB Ports | USB3 Port 0 | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xFC` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | Chipset Configuration | PCI Latency Timer | OneOf | **HARD-HIDDEN** | `SystemConfig+0x49` | `32` = `0`<br>`64` = `1` **(default)**<br>`96` = `2`<br>`128` = `3`<br>`160` = `4`<br>`192` = `5`<br>`224` = `6`<br>`248` = `7` | Medium |
| Advanced | Chipset Configuration | STIBP Status | CheckBox | **HARD-HIDDEN** | `SystemConfig+0xE7` | — | High |
| Advanced | ACPI Table/Features Control | FACP - C2 Latency Value | OneOf | **HARD-HIDDEN** | `SystemConfig+0x51` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | ACPI Table/Features Control | FACP - C3 Latency Value | OneOf | **HARD-HIDDEN** | `SystemConfig+0x52` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | ACPI Table/Features Control | FACP - RTC S4 Wakeup | OneOf | **HARD-HIDDEN** | `SystemConfig+0x53` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | ACPI Table/Features Control | APIC - IO APIC Mode | OneOf | **HARD-HIDDEN** | `SystemConfig+0x54` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | ACPI Table/Features Control | HPET - HPET Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0x55` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | ACPI Table/Features Control | _OSC Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0x10F` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Advanced | CPU Related setting | CPU P-State Setting | OneOf | **HARD-HIDDEN** | `SystemConfig+0xE8` | `Auto` = `0` **(default)**<br>`Lowest Speed` = `1` | High |
| Advanced | CPU Related setting | SMM Code Lock | OneOf | **HARD-HIDDEN** | `SystemConfig+0x122` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Advanced | CPU Related setting | SMM Protection | OneOf | **HARD-HIDDEN** | `SystemConfig+0x1DD` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| Main | Main | System Time | Time | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Main | Main | System Date | Date | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Boot | Boot | Quick Boot | OneOf | **HARD-HIDDEN** | `SystemConfig+0x6D` | `Enabled` = `1` **(default)**<br>`Disabled` = `0` | Low/Medium |
| Boot | Boot | Quiet Boot | OneOf | **HARD-HIDDEN** | `SystemConfig+0x6E` | `Enabled` = `1` **(default)**<br>`Disabled` = `0` | Low/Medium |
| Boot | Boot | Network Stack | OneOf | **HARD-HIDDEN** | `SystemConfig+0x6F` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Boot | Boot | PXE Boot to LAN | OneOf | **HARD-HIDDEN** | `SystemConfig+0x6F` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Boot | Boot | PXE Boot Capability | OneOf | **HARD-HIDDEN** | `SystemConfig+0x64` | `Disabled` = `4`<br>`UEFI PXE:IPv4` = `0`<br>`UEFI PXE:IPv6` = `1`<br>`UEFI PXE:IPv4/IPv6` = `2` **(default)**<br>`Legacy` = `3` | Medium |
| Boot | Boot | PXE / HTTP Boot Retry Policy | Numeric | **HARD-HIDDEN** | `NetworkConfig+0x0` | — | Medium |
| Boot | Boot | Power Up In Standby Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0xE2` | `Enabled` = `1`<br>`Disabled` = `0` **(default)** | Medium |
| Boot | Boot | Storage PCI Option Rom access right Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0xE4` | `Enabled` = `1` **(default)**<br>`Disabled` = `0` | Medium |
| Boot | Boot | ESATA drive boot access right Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0xCF` | `Enabled` = `1` **(default)**<br>`Disabled` = `0` | Medium |
| Boot | Boot | Add Boot Options | OneOf | **HARD-HIDDEN** | `SystemConfig+0x7D` | `First` = `0`<br>`Last` = `1`<br>`Auto` = `2` **(default)** | Medium |
| Boot | Boot | ACPI Selection | OneOf | **HARD-HIDDEN** | `SystemConfig+0x60` | `Acpi4.0` = `2`<br>`Acpi5.0` = `3`<br>`Acpi6.0` = `4`<br>`Acpi6.1` = `5`<br>`Acpi6.2` = `6`<br>`Acpi6.3` = `7`<br>`Acpi6.4` = `8`<br>`Acpi6.5` = `9` **(default)** | High |
| Boot | Boot | USB Boot | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0x5C` | `Enabled` = `0` **(default)**<br>`Disabled` = `1` | Low/Medium |
| Boot | Boot | EFI Device First | OneOf | **HARD-HIDDEN** | `SystemConfig+0x75` | `Enabled` = `0` **(default)** | Medium |
| Boot | Boot | UEFI OS Fast Boot | OneOf | **CONDITIONAL** | `SystemConfig+0x65` | `Enabled` = `0` **(default)**<br>`Disabled` = `1` | Medium |
| Boot | Boot |   USB Hot Key Support | OneOf | **HARD-HIDDEN** | `SystemConfig+0xE1` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Boot | Boot | Timeout | Numeric | **HARD-HIDDEN** | `SystemConfig+0x69` | — | Low/Medium |
| Boot | Boot | Automatic Failover | OneOf | **HARD-HIDDEN** | `SystemConfig+0xDE` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium |
| Boot | Boot | EFI | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Boot | Legacy | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | Normal Boot Menu | OneOf | **HARD-HIDDEN** | `SystemConfig+0x76` | `Normal` = `0` **(default)**<br>`Advance` = `1` | Medium |
| Boot | Legacy | Boot Type Order | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | Floppy Drive | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | Hard Disk Drive | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | CD/DVD-ROM Drive | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | PCMCIA | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | USB | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | EMBED NETWORK | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | BEV | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Legacy | Others | Ref | **HARD-HIDDEN** | — | — | Medium |
| Boot | Network Boot Hot Key | Network Hot Key Policy | OneOf | **ORPHAN/DYNAMIC** | `NetworkConfig+0x1` | `Disabled` = `0` **(default)**<br>`Network Boot First` = `1`<br>`Network Boot Only` = `2` | Medium |
| Boot | Network Boot Hot Key | Network Hot Key Capability | OneOf | **ORPHAN/DYNAMIC** | `NetworkConfig+0x2` | `PXE Only` = `1`<br>`HTTP Only` = `2`<br>`Both` = `3` **(default)** | Medium |
| Boot | Network Boot Hot Key | Network Hot Key Order | OneOf | **ORPHAN/DYNAMIC** | `NetworkConfig+0x3` | `Default` = `0` **(default)**<br>`PXE First` = `1`<br>`HTTP First` = `2` | Medium |
| Security | Security | Current TPM Device | OneOf | **VISIBLE/UNSUPPRESSED** | `SystemConfig+0xE3` | `Not Detected` = `0`<br>`TPM 1.2` = `1`<br>`TPM 2.0` = `2` **(default)** | High |
| Security | Security | TPM Availability | OneOf | **HARD-HIDDEN** | `SystemConfig+0x9D` | `Available` = `0` **(default)**<br>`Hidden` = `1` | High |
| Security | Security | TPM Operation | OneOf | **HARD-HIDDEN** | `SystemConfig+0x62` | `No Operation` = `0` **(default)**<br>`Disable and Deactivate` = `1`<br>`Enable and Activate` = `2` | High |
| Security | Security | Clear TPM | CheckBox | **HARD-HIDDEN** | `SystemConfig+0x63` | — | High |
| Security | Security | TrEE Protocol Version | OneOf | **HARD-HIDDEN** | `SystemConfig+0x9E` | `1.0` = `0`<br>`1.1` = `1` **(default)** | Medium |
| Security | Security | TPM Availability | OneOf | **HARD-HIDDEN** | `SystemConfig+0x9D` | `Available` = `0` **(default)**<br>`Hidden` = `1` | High |
| Security | Security |   PCR Bank: SHA1 | CheckBox | **HARD-HIDDEN** | `Tcg2ConfigInfo+0x5` | — | Medium |
| Security | Security |   PCR Bank: SHA256 | CheckBox | **HARD-HIDDEN** | `Tcg2ConfigInfo+0x6` | — | Medium |
| Security | Security |   PCR Bank: SHA384 | CheckBox | **HARD-HIDDEN** | `Tcg2ConfigInfo+0x7` | — | Medium |
| Security | Security |   PCR Bank: SHA512 | CheckBox | **HARD-HIDDEN** | `Tcg2ConfigInfo+0x8` | — | Medium |
| Security | Security |   PCR Bank: SM3_256 | CheckBox | **HARD-HIDDEN** | `Tcg2ConfigInfo+0x9` | — | Medium |
| Security | Security | Clear TPM | CheckBox | **HARD-HIDDEN** | `SystemConfig+0x68` | — | High |
| Security | Security |   | CheckBox | **HARD-HIDDEN** | `SystemConfig+0x67` | — | Medium |
| Security | Security |   | OneOf | **HARD-HIDDEN** | `SystemConfig+0x6B` | ` ` = `2` **(default)** | Medium |
| Security | Security | Set Supervisor Password | Password | **CONDITIONAL** | — | — | Medium |
| Security | Security | Power on Password | OneOf | **CONDITIONAL** | `SystemConfig+0x9F` | `Enabled` = `2`<br>`Disabled` = `1` **(default)** | Medium |
| Security | Security | Set All Hdd Password | Password | **CONDITIONAL** | — | — | Medium |
| Security | Security | Set All Master Hdd Password | Password | **CONDITIONAL** | — | — | Medium |
| Security | Security | Storage Password Setup Page | Ref | **CONDITIONAL** | — | — | Medium |
| Security | Security | Secure Erase | Ref | **CONDITIONAL** | — | — | Medium |
| Security | Storage Password Setup Page | TCG Storage Action | OneOf | **HARD-HIDDEN** | `PasswordConfig+0x4C` | `No Operation` = `0` **(default)**<br>`Enable_BlockSIDFunc` = `96`<br>`Disable_BlockSIDFunc` = `97`<br>`PPRequiredForEnableBlockSID_True` = `98`<br>`PPRequiredForEnableBlockSID_False` = `99`<br>`PPRequiredForDisableBlockSID_True` = `100`<br>`PPRequiredForDisableBlockSID_False` = `101` | Medium |
| Exit | Exit | Exit Saving Changes | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Exit | Exit | Save Change Without Exit | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Exit | Exit | Exit Discarding Changes | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Exit | Exit | Load Optimal Defaults | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Exit | Exit | Load Custom Defaults | Action | **CONDITIONAL** | — | — | Medium |
| Exit | Exit | Save Custom Defaults | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |
| Exit | Exit | Discard Changes | Action | **VISIBLE/UNSUPPRESSED** | — | — | Medium |

## AMD PBS (`AmdPbsSetupDxe`)

| Form | Option | Type | Internal suppression | Var/offset | Choices/default | Risk |
|---|---|---|---|---|---|---|
| AMD PBS Option | Enable SLT check | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x61` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)**<br>`Force SLT check halt` = `2` | Medium/High |
| AMD PBS Option | Above 4GB MMIO Limit | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x31` | `35bit (32GB)` = `35`<br>`36bit (64GB)` = `36`<br>`37bit (128GB)` = `37`<br>`38bit (256GB)` = `38`<br>`39bit (512GB)` = `39`<br>`40bit (1TB)` = `40` **(default)**<br>`41bit (2TB)` = `41`<br>`42bit (4TB)` = `42`<br>`43bit (8TB)` = `43`<br>`44bit (16TB)` = `44`<br>`45bit (32TB)` = `45`<br>`46bit (64TB)` = `46`<br>`47bit (128TB)` = `47`<br>`48bit (256TB)` = `48` | Medium/High |
| AMD PBS Option | Wireless LAN Recovery | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3E` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)**<br>`Dummy reset` = `2` | Medium/High |
| AMD PBS Option | Wireless LAN Country Code | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF4` | `Disabled` = `0`<br>`Country Code USA` = `1`<br>`Country Code China` = `2` **(default)** | Medium/High |
| AMD PBS Option | Bluetooth PLDR | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x73` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)**<br>`Dummy reset` = `2` | Medium/High |
| AMD PBS Option | Wireless Button | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x75` | `Disabled` = `0` **(default)**<br>`HID Based` = `3` | Medium/High |
| AMD PBS Option | Processor Aggregator Device | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x62` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| AMD PBS Option |  Core Count Control | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x63` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| AMD PBS Option |   Core Count SW_SCI_GPE_ID | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x74` | — | Medium/High |
| AMD PBS Option | APIC Software Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x85` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | High |
| AMD PBS Option | Dynamic P3T limit | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA9` | `Disabled` = `0`<br>`Enable for DC-only case (include fake DC)` = `1` **(default)**<br>`Enabled ` = `2` | Medium/High |
| AMD PBS Option | Dynamic LID | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xDF` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| AMD PBS Option | HDMI 3.0G Tx SLEW | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE4` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| AMD PBS Option | HDMI 3.0G Tx Slew Control Value | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE5` | — | Medium/High |
| AMD PBS Option | ACPI Power Button Method Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xED` | `Generic Button Device` = `0`<br>`ACPI Control Method` = `1` **(default)** | Medium/High |
| AMD PBS Option | Power Button Override | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x101` | `4 Seconds` = `0` **(default)**<br>`10 Seconds` = `1` | Medium/High |
| PCI Express Configurations | Pcie Dxio Timing ControlEnable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x9C` | `Auto` = `15` **(default)**<br>`Enabled ` = `1`<br>`Disabled` = `0` | High |
| PCI Express Configurations | PCIE Link Receiver Detection Polling | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x9D` | — | Medium/High |
| PCI Express Configurations | PCIE Link L0 Polling | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xA1` | — | Medium/High |
| PCI Express Configurations | Enable power sequence of WWAN support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA5` | `Disabled` = `0` **(default)**<br>`Support Device1(Fibocom)` = `1`<br>`Support Device2(Quectel RM520)` = `2`<br>`Support Device3(Quectel EM120/EM160)` = `3`<br>`Support Device4(Netprisma LCUK54-WWD/LCUK54-WRD)` = `4` | Medium/High |
| PCI Express Configurations | PCIE x4 DT Slot Power Enable | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xC` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| PCI Express Configurations | M.2 SSD1 Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x5C` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| PCI Express Configurations | WLAN Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD` | `Disabled` = `0`<br>`x1` = `1` **(default)**<br>`x2` = `2` | Medium/High |
| PCI Express Configurations | WWAN Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF` | `Disabled` = `0`<br>`x1` = `1` **(default)**<br>`x2` = `2` | Medium/High |
| PCI Express Configurations | SD Card Reader Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x5E` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| PCI Express Configurations |   Initial SD7 L1 Substates at OS runtime | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xF8` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| PCI Express Configurations | GbE Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| PCI Express Configurations | M.2 SSD0 Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x5B` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| PCI Express Configurations | NVMe RAID mode | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x45` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | High |
| PCI Express Configurations | Save Restore DXIO Feature | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x22` | `Auto` = `15` **(default)**<br>`Disabled` = `0`<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | NVME D3Cold | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x7E` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations |   M.2 NVME Tpvperl | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x6B` | — | Medium/High |
| Power Saving Configurations |   M.2 NVME Trst-cfg | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x6D` | — | Medium/High |
| Power Saving Configurations | NVME D3Hot Password | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF7` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | WLAN D3Cold | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xCF` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | LOM D3Cold | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF5` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | SD Card Reader D3Cold | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF6` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | USB4 D3 Eanble | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x84` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | Internal PCIe GPP 0 D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xAB` | `Disabled` = `0` **(default)**<br>`Enabled ` = `4` | Medium/High |
| Power Saving Configurations |   SOC GPU D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xAC` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC HD Audio D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xAD` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC USB3.1 D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xAE` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC ACP D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xAF` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC Azalia D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB0` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations | Internal PCIe GPP 2 D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB1` | `Disabled` = `0` **(default)**<br>`Enabled ` = `4` | Medium/High |
| Power Saving Configurations |   SOC USB3.1 for USB4 D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB2` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC USB4 D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB3` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations | Internal USB4 PCIe Tunneling D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB4` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations |   SOC USB4 PCIe Endpoint D3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB5` | `Disabled` = `0`<br>`Enabled ` = `4` **(default)** | Medium/High |
| Power Saving Configurations | Keep Wlan Power In S3/S4 state | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x6A` | `Disabled` = `0` **(default)**<br>`Enable Wlan Power In S3 And S4` = `3` | Medium/High |
| Power Saving Configurations | Unused GPP Clocks Off | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x15` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ0 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x16` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ1 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x17` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ2 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x18` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ3 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x19` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ4 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x1A` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ5 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x1B` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | Clock PM: CLK_REQ6 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x1C` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Power Saving Configurations | PCIe x4 Slot D3 Cold | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB8` | `Disabled` = `0` **(default)**<br>`Renesas XHCI` = `1`<br>`Realtek SD Card Reader` = `2` | Medium/High |
| USB/Thunderbolt Configurations | PD USB4 Control Enable/Disable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x80` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| USB/Thunderbolt Configurations | USB4 Bus Reserved | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x8F` | — | Medium/High |
| USB/Thunderbolt Configurations | USB4 IO Reserved (KB) | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xCD` | — | Medium/High |
| USB/Thunderbolt Configurations | USB4 Non-Prefetch Memory Reserved | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x90` | — | Medium/High |
| USB/Thunderbolt Configurations | USB4 non-Prefetch MMIO align (0 ~ 65534 MB) | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x96` | — | Medium/High |
| USB/Thunderbolt Configurations | USB4 Prefetch Memory Reserved | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x92` | `2MB` = `2`<br>`4MB` = `4`<br>`8MB` = `8`<br>`16MB` = `16`<br>`32MB` = `32`<br>`64MB` = `64`<br>`128MB` = `128`<br>`256MB` = `256`<br>`512MB` = `512`<br>`1GB` = `1024`<br>`2GB` = `2048`<br>`4GB` = `4096`<br>`8GB` = `8192`<br>`16GB` = `16384`<br>`32GB` = `32768`<br>`64GB` = `65536`<br>`128GB` = `131072` **(default)** | Medium/High |
| USB/Thunderbolt Configurations | USB4 Prefetch MMIO align | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x98` | `2MB` = `2`<br>`4MB` = `4`<br>`8MB` = `8`<br>`16MB` = `16`<br>`32MB` = `32`<br>`64MB` = `64`<br>`128MB` = `128`<br>`256MB` = `256`<br>`512MB` = `512`<br>`1GB` = `1024`<br>`2GB` = `2048`<br>`4GB` = `4096`<br>`8GB` = `8192`<br>`16GB` = `16384`<br>`32GB` = `32768` **(default)**<br>`64GB` = `65536` | Medium/High |
| USB/Thunderbolt Configurations | USB Camera Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3F` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| USB/Thunderbolt Configurations | USB Fingerprint or GBE MUX | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA8` | `Fingerprint` = `0`<br>`GBE` = `1` **(default)** | Medium/High |
| USB/Thunderbolt Configurations | USB Fingerprint Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x5D` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB/Thunderbolt Configurations | UCSI Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x11` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB/Thunderbolt Configurations |   UCSI tunnel location | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x12` | `UCSI tunnel at EC RAM` = `0`<br>`UCSI tunnel at MMIO 0xFEC20200` = `1` **(default)** | Medium/High |
| USB/Thunderbolt Configurations | USBC Port Harware Disable Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA6` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB/Thunderbolt Configurations | Reconfig Rebalance Resources by OS | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA7` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB/Thunderbolt Configurations | PD Thunderbolt3 Alt Mode | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3D` | `Enabled ` = `0` **(default)**<br>`Disabled` = `1` | Medium/High |
| USB/Thunderbolt Configurations | USB4 ACPI _DEP Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xBA` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB/Thunderbolt Configurations | USB4 LTR Snoop/NonSnoop Scale | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xEE` | — | Medium/High |
| USB/Thunderbolt Configurations | USB4 LTR Snoop/NonSnoop Value | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xEF` | — | Medium/High |
| USB/Thunderbolt Configurations | Modify _UPC/_PLD settings for USB3.2-A vertical port (Requires board rework) | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF3` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| USB TypeC Security Mode Configurations | USB Type-C Security | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xBB` | `Auto` = `15` **(default)**<br>`Mode 0` = `0`<br>`Mode 1` = `1`<br>`Mode 2` = `2` | Medium/High |
| Graphics Configurations | EVAL Slot Power Enable | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x10` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Graphics Configurations | EVAL CARD T-Diode Routing Select | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x42` | `APU SMBUS1` = `0`<br>`EC` = `1` **(default)** | Medium/High |
| Graphics Configurations | Special Display Features | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x4` | `Disabled` = `0`<br>`HybridGraphics` = `4` **(default)** | Medium/High |
| Graphics Configurations | D3Cold Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x32` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1`<br>`Dummy D3Cold` = `2` | Medium/High |
| Graphics Configurations |   Discrete GPU Hotplug Mode | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x29` | `Basic Mode` = `0`<br>`Enhanced Mode` = `1` **(default)**<br>`Non-Hotplug Mode` = `2` | Medium/High |
| Graphics Configurations |   Discrete GPU D3Cold HPD Support | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x46` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   PME Turn Off Support | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x47` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Graphics Configurations | D3Cold Force Gen1 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x50` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   SLOTPWR-PWREN Timing(ms) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x6F` | — | High |
| Graphics Configurations |   PWREN-RST Timing (ms) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x70` | — | High |
| Graphics Configurations |   PERST-WAKEL23 Timing (ms) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x71` | — | High |
| Graphics Configurations |   DLACT-CFGACC Timing (ms) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x72` | — | High |
| Graphics Configurations | NVIDIA DGPU Power Enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x4E` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | Discrete GPU _DSM Function A | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x51` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | Discrete GPU _DSM Function B | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x52` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | Non-Eval Discrete GPU Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x34` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Graphics Configurations | Discrete GPU HPD Circuitry | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x2A` | `OR Circuitry` = `0` **(default)**<br>`Pulse Circuitry` = `1` | Medium/High |
| Graphics Configurations | Discrete GPU's USB Port | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x4F` | `Keep Default Setting` = `0` **(default)**<br>`Disabled` = `1` | Medium/High |
| Graphics Configurations | Discrete GPU's SSID/SVID | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x53` | `Keep Default Setting` = `0`<br>`Program by Vendor` = `1` **(default)** | Medium/High |
| Graphics Configurations |   Discrete GPU's VGA SSID/SVID | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x87` | — | Medium/High |
| Graphics Configurations |   Discrete GPU's AUDIO SSID/SVID | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x8B` | — | Medium/High |
| Graphics Configurations | Discrete GPU BOMACO Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x2B` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   MACO-PWR Timing   (us) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x2C` | — | High |
| Graphics Configurations |   RST-MACO Timing   (ms) | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x2D` | — | High |
| Graphics Configurations | ATCS Function 9 Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB9` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | AC Maximum Performance Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x54` | — | Medium/High |
| Graphics Configurations | AC Better Performance Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x55` | — | Medium/High |
| Graphics Configurations | AC Better Battery Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x56` | — | Medium/High |
| Graphics Configurations | DC Maximum Performance Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x57` | — | Medium/High |
| Graphics Configurations | DC Better Performance Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x58` | — | Medium/High |
| Graphics Configurations | DC Better Battery Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x59` | — | Medium/High |
| Graphics Configurations | DC Battery Saver Limit | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x5A` | — | Medium/High |
| Graphics Configurations |   BLINK LED | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x2E` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)**<br>`GPIO 11 Output Low` = `2`<br>`GPIO 11 Output High` = `3` | Medium/High |
| Graphics Configurations | ATIF Notify Command Code | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xBC` | `Notify VGA 0x81` = `0` **(default)**<br>`Notify VGA 0xD0` = `208`<br>`Notify VGA 0xD1` = `209`<br>`Notify VGA 0xD2` = `210`<br>`Notify VGA 0xD3` = `211`<br>`Notify VGA 0xD4` = `212`<br>`Notify VGA 0xD5` = `213`<br>`Notify VGA 0xD6` = `214`<br>`Notify VGA 0xD7` = `215`<br>`Notify VGA 0xD8` = `216`<br>`Notify VGA 0xD9` = `217` | Medium/High |
| Graphics Configurations | ATIF Function 21 Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xBD` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   External Graphics Port | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xBE` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   Hide XConnect GUI | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xBF` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   Run Time PM and D3 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xC0` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   Support ATIF ATPX | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xC1` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | ATIF Function 22 Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xC2` | `Disabled` = `0` **(default)**<br>`Undefined` = `1`<br>`Integrated Graphics` = `2`<br>`Discrete Graphics` = `3` | Medium/High |
| Graphics Configurations |   GPU Package Power Limit Value | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xC3` | — | Medium/High |
| Graphics Configurations | ATIF Function 23 Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x64` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations |   Vari-Bright Maximum Performance | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x65` | — | Medium/High |
| Graphics Configurations |   Vari-Bright Better Performance | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x66` | — | Medium/High |
| Graphics Configurations |   Vari-Bright Better Battery | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x67` | — | Medium/High |
| Graphics Configurations |   Vari-Bright Battery Saver | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x68` | — | Medium/High |
| Graphics Configurations | ATIF Function 24 Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xEA` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Graphics Configurations |   ISP Device number | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xEB` | — | Medium/High |
| Graphics Configurations | Primary Video Adaptor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x5` | `Int Graphics (IGD)` = `1` **(default)**<br>`Ext Graphics (PEG)` = `2` | Medium/High |
| Graphics Configurations | Smart Mux Acpi Control | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xC7` | `Disabled` = `0` **(default)**<br>`Enabled without _DEP` = `1`<br>`Enabled ` = `3` | Medium/High |
| Graphics Configurations | Display Panel Multiplexer | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xC8` | — | Medium/High |
| Graphics Configurations | Smart Mux _HID Selection | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xC9` | `SMUX1234` = `0` **(default)**<br>`MSFT0007` = `1` | Medium/High |
| Graphics Configurations | Smart Mux MDM Support Level | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xCA` | `No Support` = `0` **(default)**<br>`Development Support` = `1`<br>`Experimental Support` = `2`<br>`Full Support` = `3` | Medium/High |
| Graphics Configurations | Smart Mux First Connected GPU | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xCB` | `Integrated Graphics` = `0` **(default)**<br>`Discrete Graphics` = `1` | Medium/High |
| Graphics Configurations | Smart Mux ACPI Method Location | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xCC` | `Under Mux` = `1`<br>`Under dGPU` = `2` **(default)** | Medium/High |
| Graphics Configurations | Verify PEI GOP | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x21` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Graphics Configurations | Ultra mode support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xFA` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Display Configurations | DP0 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x48` | `Default` = `0` **(default)**<br>`EDP display` = `1`<br>`DP display` = `2`<br>`HDMI display` = `3` | Medium/High |
| Display Configurations | DP1 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x49` | `Default` = `0` **(default)**<br>`EDP display` = `1`<br>`DP display` = `2`<br>`HDMI display` = `3` | Medium/High |
| Display Configurations |   Adjust DP Caps | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xD7` | `Soc Default` = `0` **(default)**<br>`Cap override` = `1` | Medium/High |
| Display Configurations | DP1 Display Caps | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x76` | — | Medium/High |
| Display Configurations | DP2 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x4A` | `Default` = `0` **(default)**<br>`EDP display` = `1`<br>`HDMI display` = `3`<br>`DP with TypeC display` = `4`<br>`DP without TypeC display` = `5` | Medium/High |
| Display Configurations |   Adjust DP Caps | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xD8` | `Soc Default` = `0` **(default)**<br>`Cap override` = `1` | Medium/High |
| Display Configurations | DP2 Display Caps | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x7A` | — | Medium/High |
| Display Configurations | DP3 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x4B` | `Default` = `0` **(default)**<br>`EDP display` = `1`<br>`HDMI display` = `3`<br>`DP with TypeC display` = `4`<br>`DP without TypeC display` = `5` | Medium/High |
| Display Configurations |   Adjust DP Caps | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xD9` | `Soc Default` = `0` **(default)**<br>`Cap override` = `1` | Medium/High |
| Display Configurations | DP3 Display Caps | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xE0` | — | Medium/High |
| Display Configurations | DP4 | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x4C` | `Default` = `0` **(default)**<br>`EDP display` = `1`<br>`HDMI display` = `3`<br>`DP with TypeC display` = `4`<br>`DP without TypeC display` = `5` | Medium/High |
| Display Configurations |   Adjust DP Caps | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xDA` | `Soc Default` = `0` **(default)**<br>`Cap override` = `1` | Medium/High |
| Display Configurations | DP4 Display Caps | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xDB` | — | Medium/High |
| Display Configurations | EDP priority | OneOf | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xCE` | `EDP0` = `0` **(default)**<br>`EDP1` = `1` | Medium/High |
| Audio Configurations | Wake On Voice | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x43` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Audio Configurations | ACP Power Gating | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x44` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Audio Configurations | ACP CLock Gating | OneOf | HARD-HIDDEN inside formset | `AMD_PBS_SETUP+0x4D` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Audio Configurations | Verb Table Select | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD0` | `ALC245 Crb Default: SVID/SSID: 1022/D997(Birman+)` = `0` **(default)**<br>`ALC245_05112020   : SVID/SSID: 10EC/1208` = `1`<br>`ALC256_06082020   : SVID/SSID: 10EC/1208` = `2`<br>`SN614X 08082022   : SVID/SSID: 14F1/0101` = `3`<br>`CX11970EVK        : SVID/SSID: D595/1022` = `4` | Medium/High |
| Audio Configurations | USB Sideband Audio | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE6` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Audio Configurations | BT VID PID for ACP BTLE Setting | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xFB` | — | Medium/High |
| I2C Configurations | Touch Panel Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x6` | `Under I2C 0 Bus` = `0`<br>`Under I2C 1 Bus` = `1`<br>`Under I2C 2 Bus` = `2`<br>`Under I2C 3 Bus` = `3`<br>`ELAN TSP Under I2C 0 Bus` = `16`<br>`ELAN TSP Under I2C 1 Bus` = `17`<br>`ELAN TSP Under I2C 2 Bus` = `18`<br>`ELAN TSP Under I2C 3 Bus` = `19`<br>`Under USB 6 Bus` = `4`<br>`Disabled` = `5` **(default)** | Medium/High |
| I2C Configurations | Touch Pad Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x7` | `Under I2C 0 Bus` = `0`<br>`Under I2C 1 Bus` = `1` **(default)**<br>`Under I2C 2 Bus` = `2`<br>`Under I2C 3 Bus` = `3`<br>`Disabled` = `4` | Medium/High |
| I2C Configurations |     Touchpad Slave address | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x81` | — | Medium/High |
| I2C Configurations |     Touchpad HID Descriptor address | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x82` | — | Medium/High |
| I2C Configurations | Nfc Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x8` | `Under I2C 0 Bus` = `0`<br>`Under I2C 1 Bus` = `1`<br>`Under I2C 2 Bus` = `2`<br>`Under I2C 3 Bus` = `3`<br>`Disabled` = `4` **(default)** | Medium/High |
| I2C Configurations | MITT/WITT Selection | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x14` | `MITT Only` = `0`<br>`WITT Only` = `1`<br>`Both disable` = `2` **(default)** | Medium/High |
| I2C Configurations | I2C MUX | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD4` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| I2C Configurations | SMBUS0 Buffer | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD5` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| I2C Configurations | SMBUS1 Buffer | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD6` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Thermal Configurations | APU PROCHOT# setting | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x83` | `Disable APU_PROCHOT#` = `0`<br>`Enable APU_PROCHOT# in pure-DC case` = `1`<br>`Enable APU_PROCHOT# in pure-AC case` = `2`<br>`Enable APU_PROCHOT# in either pure-DC or pure-AC case (not AC+DC)` = `3` **(default)** | Medium/High |
| Thermal Configurations | AMD DPTC interface | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x26` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| Thermal Configurations | STT sensor reporting | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x27` | `Disabled` = `0` **(default)**<br>`Report onboard sensors` = `1`<br>`Report onboard + eval card sensors` = `2` | Medium/High |
| MP2 Configurations | Power Sensors Routing Select | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x40` | `WALLE lite PDT` = `0` **(default)**<br>`WALLE lite PM log` = `1` | Medium/High |
| MP2 Configurations | MP2 FW Selection | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x41` | `MP2_SFH` = `0` **(default)**<br>`MP2_WalleLite` = `1` | Medium/High |
| MP2 Configurations | Turn off Xtal (S3/S5) | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x7F` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| MP2 Configurations | Sensor Fusion User Mode Driver | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x33` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| MP2 Configurations | Reserved Memory For MP2 | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x86` | `Disabled` = `255`<br>`128 KB` = `0` **(default)**<br>`256 KB` = `1`<br>`512 KB` = `2`<br>`1 MB` = `3`<br>`2 MB` = `4`<br>`4 MB` = `5`<br>`8 MB` = `6`<br>`16 MB` = `7`<br>`32 MB` = `8`<br>`64 MB` = `9` | Medium/High |
| MP2 Configurations | MP2 Privilege Mode | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x35` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Accelerator Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x36` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Magnet Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x37` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | SRA Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x38` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Light Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x39` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Proximity Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3A` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Wake On Human Presence detection WA | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB7` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | Gyroscope Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3B` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| MP2 Configurations | HPD-Lite Sensor | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x3C` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| EC/PD Configurations | Charger mode BYPASS | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x13` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| EC/PD Configurations | KBC Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x1D` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| EC/PD Configurations | AcDcSwitch | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x1E` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| EC/PD Configurations |   AmdDcTimer | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x1F` | — | Medium/High |
| EC/PD Configurations |   AmdAcTimer | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0x20` | — | Medium/High |
| EC/PD Configurations | Fake DC Level | Numeric | CONDITIONAL inside formset | `AMD_PBS_SETUP+0xAA` | — | Medium/High |
| EC/PD Configurations | VDD adjust for BoardDesign | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x69` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| EC/PD Configurations |   VDD MISC S5 voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x25` | `voltage (0.75V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (-10mv)` = `11`<br>`voltage (-20mv)` = `12`<br>`voltage (-30mv)` = `13`<br>`voltage (-40mv)` = `14`<br>`voltage (-50mv)` = `15`<br>`voltage (-60mv)` = `16`<br>`voltage (-70mv)` = `17`<br>`voltage (-80mv)` = `18`<br>`voltage (-90mv)` = `19`<br>`voltage (-100mv)` = `20` | High |
| EC/PD Configurations |   VDD_MISC voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x23` | `voltage (0.75V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (+110mv)` = `11`<br>`voltage (+120mv)` = `12`<br>`voltage (+130mv)` = `13`<br>`voltage (+140mv)` = `14`<br>`voltage (+150mv)` = `15`<br>`voltage (-10mv)` = `16`<br>`voltage (-20mv)` = `17`<br>`voltage (-30mv)` = `18`<br>`voltage (-40mv)` = `19`<br>`voltage (-50mv)` = `20`<br>`voltage (-60mv)` = `21`<br>`voltage (-70mv)` = `22`<br>`voltage (-80mv)` = `23`<br>`voltage (-90mv)` = `24`<br>`voltage (-100mv)` = `25`<br>`voltage (-110mv)` = `26`<br>`voltage (-120mv)` = `27`<br>`voltage (-130mv)` = `28`<br>`voltage (-140mv)` = `29`<br>`voltage (-150mv)` = `30` | High |
| EC/PD Configurations |   VDD11 voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0x24` | `voltage (1.1V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (-10mv)` = `11`<br>`voltage (-20mv)` = `12`<br>`voltage (-30mv)` = `13`<br>`voltage (-40mv)` = `14`<br>`voltage (-50mv)` = `15`<br>`voltage (-60mv)` = `16`<br>`voltage (-70mv)` = `17`<br>`voltage (-80mv)` = `18`<br>`voltage (-90mv)` = `19`<br>`voltage (-100mv)` = `20` | High |
| EC/PD Configurations |   1V8 ALW voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE7` | `voltage(1.8V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (+110mv)` = `11`<br>`voltage (+120mv)` = `12`<br>`voltage (+130mv)` = `13`<br>`voltage (+140mv)` = `14`<br>`voltage (+150mv)` = `15`<br>`voltage (-10mv)` = `16`<br>`voltage (-20mv)` = `17`<br>`voltage (-30mv)` = `18`<br>`voltage (-40mv)` = `19`<br>`voltage (-50mv)` = `20`<br>`voltage (-60mv)` = `21`<br>`voltage (-70mv)` = `22`<br>`voltage (-80mv)` = `23`<br>`voltage (-90mv)` = `24`<br>`voltage (-100mv)` = `25`<br>`voltage (-110mv)` = `26`<br>`voltage (-120mv)` = `27`<br>`voltage (-130mv)` = `28`<br>`voltage (-140mv)` = `29`<br>`voltage (-150mv)` = `30` | High |
| EC/PD Configurations |   VDD2L voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE8` | `voltage(0.9V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (-10mv)` = `11`<br>`voltage (-20mv)` = `12`<br>`voltage (-30mv)` = `13`<br>`voltage (-40mv)` = `14`<br>`voltage (-50mv)` = `15`<br>`voltage (-60mv)` = `16`<br>`voltage (-70mv)` = `17`<br>`voltage (-80mv)` = `18`<br>`voltage (-90mv)` = `19`<br>`voltage (-100mv)` = `20` | High |
| EC/PD Configurations |   VDDQ voltage | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xE9` | `voltage(0.5V)` = `0` **(default)**<br>`voltage (+10mv)` = `1`<br>`voltage (+20mv)` = `2`<br>`voltage (+30mv)` = `3`<br>`voltage (+40mv)` = `4`<br>`voltage (+50mv)` = `5`<br>`voltage (+60mv)` = `6`<br>`voltage (+70mv)` = `7`<br>`voltage (+80mv)` = `8`<br>`voltage (+90mv)` = `9`<br>`voltage (+100mv)` = `10`<br>`voltage (-10mv)` = `11`<br>`voltage (-20mv)` = `12`<br>`voltage (-30mv)` = `13`<br>`voltage (-40mv)` = `14`<br>`voltage (-50mv)` = `15`<br>`voltage (-60mv)` = `16`<br>`voltage (-70mv)` = `17`<br>`voltage (-80mv)` = `18`<br>`voltage (-90mv)` = `19`<br>`voltage (-100mv)` = `20` | High |
| EC/PD Configurations | POST LED on enable | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD1` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| EC/PD Configurations | Smart Mux Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB6` | `Disabled` = `0` **(default)**<br>`Hybrid Graphics Mode` = `1`<br>`Discrete Mode` = `2`<br>`Smart Mux 1.5` = `3`<br>`Smart Mux 2.0` = `4` | Medium/High |
| EC/PD Configurations | EC UDC Log Support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD2` | `Disabled` = `0`<br>`Enabled ` = `1` **(default)** | Medium/High |
| EC/PD Configurations | Retimer FMP driver support | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF9` | `Disabled` = `0` **(default)**<br>`I2c Retimer FMP driver without force power` = `1`<br>`I2c Retimer FMP driver with force power` = `2`<br>`SBU Retimer FMP driver without force power` = `3`<br>`SBU Retimer FMP driver with force power` = `4` | Medium/High |
| Debug Configurations | Serial Debug Message Under OS | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xA` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Debug Configurations | Debug Print In ASL | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xB` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |
| Debug Configurations | ISP Camera Module List | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xD3` | `Disabled` = `0` **(default)**<br>`Module 1` = `1`<br>`Module 2` = `2`<br>`Module 3` = `3`<br>`Module 4` = `4`<br>`Module 5` = `5`<br>`Module 6` = `6`<br>`Module 7` = `7`<br>`Module 8` = `8`<br>`Module 9` = `9`<br>`Module 10` = `10`<br>`Module 11` = `11` | Medium/High |
| Debug Configurations | Time for Blank Screen (10ms) | Numeric | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xFF` | — | Medium/High |
| Debug Configurations | Debug Data Preserve | OneOf | UNSUPPRESSED inside formset | `AMD_PBS_SETUP+0xF1` | `Disabled` = `0` **(default)**<br>`Enabled ` = `1` | Medium/High |

## AMD CBS (`CbsSetupDxeSTX`)

| Form | Option | Type | Internal suppression | Var/offset | Choices/default | Risk |
|---|---|---|---|---|---|---|
| AMD CBS | Combo CBS | Numeric | CONDITIONAL inside formset | `AmdSetup+0x20` | — | Medium/High |
| CPU Common Options | REP-MOV/STOS Streaming | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x21` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | Medium/High |
| CPU Common Options | RedirectForReturnDis | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x22` | `Auto` = `255` **(default)**<br>`1` = `1`<br>`0` = `0` | Medium/High |
| CPU Common Options | Platform First Error Handling | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x23` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `3` **(default)** | Medium/High |
| CPU Common Options | Core Performance Boost | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x24` | `Disabled` = `0`<br>`Auto` = `1` **(default)** | Medium/High |
| CPU Common Options | Global C-state Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x25` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| CPU Common Options | Opcache grayout flag | OneOf | CONDITIONAL inside formset | `AmdSetup+0x26` | `0` = `0` **(default)**<br>`1` = `1`<br>`Display` = `2` | Medium/High |
| CPU Common Options | Opcache Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x27` | `Disabled` = `1`<br>`Enabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | Streaming Stores Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x28` | `Disabled` = `1`<br>`Enabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | Local APIC Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x29` | `Compatibility` = `0`<br>`xAPIC` = `1`<br>`x2APIC` = `2`<br>`Auto` = `255` **(default)** | High |
| CPU Common Options | ACPI _CST C1 Declaration | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x2A` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| CPU Common Options | MCA error thresh enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x2B` | `False` = `0`<br>`True` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | MCA error thresh count | Numeric | CONDITIONAL inside formset | `AmdSetup+0x2C` | — | Medium/High |
| CPU Common Options | SMU and PSP Debug Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x2E` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| CPU Common Options | Enhanced REP MOVSB/STOSB (ERSM) | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x2F` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | Log Transparent Errors | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x30` | `Auto` = `3` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | Medium/High |
| CPU Common Options | AVX512 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x31` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | MONITOR and MWAIT disable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x32` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | SVM Lock | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x33` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `255` **(default)** | High |
| CPU Common Options | SVM Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x34` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| CPU Common Options | Workload classification | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x35` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | Medium/High |
| CPU Common Options | Latency Under Load (LUL) | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x36` | `Auto` = `255` **(default)**<br>`Enabled` = `0`<br>`Disabled` = `1` | Medium/High |
| Performance | OC Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x37` | `Normal Operation` = `0`<br>`OC1` = `1`<br>`OC2` = `2`<br>`OC3` = `3`<br>`Customized` = `5` **(default)** | Medium/High |
| Accept | Custom Pstate0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x3A` | `Custom` = `1`<br>`Auto` = `2` **(default)** | Medium/High |
| Accept | Pstate0 Freq (MHz) | Numeric | CONDITIONAL inside formset | `AmdSetup+0x3B` | — | Medium/High |
| Accept | Pstate0 VID | Numeric | CONDITIONAL inside formset | `AmdSetup+0x3F` | — | Medium/High |
| Prefetcher settings | L2 Stream HW Prefetcher | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x43` | `Disable` = `0`<br>`Enable` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| Prefetcher settings | L2 Up/Down Prefetcher | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x44` | `Disable` = `0`<br>`Enable` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| Core Watchdog | Core Watchdog Timer Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x45` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| Core Watchdog | Core Watchdog Timer Interval | OneOf | CONDITIONAL inside formset | `AmdSetup+0x46` | `21.461s` = `2304`<br>`10.730s` = `2048`<br>`5.364s` = `0`<br>`2.681s` = `256`<br>`1.340s` = `512`<br>`669.41ms` = `768`<br>`334.05ms` = `1024`<br>`166.37ms` = `1280`<br>`82.53ms` = `1536`<br>`40.61ms` = `1792`<br>`20.970ms` = `2305`<br>`10.484ms` = `2049`<br>`5.241ms` = `1`<br>`2.620ms` = `257`<br>`1.309ms` = `513`<br>`654.08us` = `769`<br>`326.4us` = `1025`<br>`162.56us` = `1281`<br>`80.64us` = `1537`<br>`39.68us` = `1793`<br>`Auto` = `65535` **(default)** | Medium/High |
| Core Watchdog | Core Watchdog Timer Severity | OneOf | CONDITIONAL inside formset | `AmdSetup+0x48` | `No Error` = `0`<br>`Transparent` = `1`<br>`Corrected` = `2`<br>`Deferred` = `3`<br>`Uncorrected` = `4`<br>`Fatal` = `5`<br>`Auto` = `255` **(default)** | Medium/High |
| DF Common Options | DF Watchdog Timer Interval | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x49` | `Auto` = `255` **(default)**<br>`41 ms` = `0`<br>`166 ms` = `1`<br>`334 ms` = `2`<br>`669 ms` = `3`<br>`1.34 seconds` = `4`<br>`2.68 seconds` = `5`<br>`5.36 seconds` = `6` | Medium/High |
| DF Common Options | Disable DF to external downstream IP SyncFloodPropagation | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4A` | `Sync flood disabled` = `1`<br>`Sync flood enabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| DF Common Options | Sync Flood Propagation to DF Components | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4B` | `Sync flood disabled` = `1`<br>`Sync flood enabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| DF Common Options | Freeze DF module queues on error | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4C` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| DF Common Options | CC6 memory region encryption | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4D` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| DF Common Options | System probe filter | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4E` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| DF Common Options | Disable DF sync flood propagation | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x4F` | `Sync flood disabled` = `1`<br>`Sync flood enabled` = `0`<br>`Auto` = `3` **(default)** | Medium/High |
| DF Common Options | DF Cstates | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x50` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| Memory Addressing | Memory interleaving | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x51` | `Disabled` = `0`<br>`Auto` = `7` **(default)** | Medium/High |
| Memory Addressing | Memory interleaving size | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x52` | `256 Bytes` = `0`<br>`512 Bytes` = `1`<br>`1 KB` = `2`<br>`2 KB` = `3`<br>`Auto` = `7` **(default)** | Medium/High |
| Memory Addressing | DRAM map inversion | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x53` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `3` **(default)** | Medium/High |
| Memory Addressing | Location of private memory regions | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x54` | `Distributed` = `0`<br>`Consolidated` = `1`<br>`Consolidated to 1st DRAM pair` = `2`<br>`Auto` = `255` **(default)** | Medium/High |
| Memory Addressing | 2 Channel Interleaving | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x55` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| Accept | Active Memory Timing Settings | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x58` | `Auto` = `255` **(default)**<br>`Enabled` = `1` | High |
| Accept | Memory Target Speed | Numeric | CONDITIONAL inside formset | `AmdSetup+0x59` | — | High |
| DDR SPD Timing | Tcl Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x5B` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Tcl | Numeric | CONDITIONAL inside formset | `AmdSetup+0x5C` | — | High |
| DDR SPD Timing | Trcd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x5E` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trcd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x5F` | — | High |
| DDR SPD Timing | Trp Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x61` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trp | Numeric | CONDITIONAL inside formset | `AmdSetup+0x62` | — | High |
| DDR SPD Timing | Tras Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x64` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Tras | Numeric | CONDITIONAL inside formset | `AmdSetup+0x65` | — | High |
| DDR SPD Timing | Trc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x67` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trc | Numeric | CONDITIONAL inside formset | `AmdSetup+0x68` | — | High |
| DDR SPD Timing | Twr Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x6A` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Twr | Numeric | CONDITIONAL inside formset | `AmdSetup+0x6B` | — | High |
| DDR SPD Timing | Trfc1 Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x6D` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trfc1 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x6E` | — | High |
| DDR SPD Timing | Trfc2 Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x70` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trfc2 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x71` | — | High |
| DDR SPD Timing | TrfcSb Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x73` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | TrfcSb | Numeric | CONDITIONAL inside formset | `AmdSetup+0x74` | — | High |
| DDR SPD Timing | Trtp Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x76` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Trtp | Numeric | CONDITIONAL inside formset | `AmdSetup+0x77` | — | High |
| DDR SPD Timing | TrrdL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x79` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | TrrdL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x7A` | — | High |
| DDR SPD Timing | TrrdS Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x7C` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | TrrdS | Numeric | CONDITIONAL inside formset | `AmdSetup+0x7D` | — | High |
| DDR SPD Timing | Tfaw Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x7F` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | Tfaw | Numeric | CONDITIONAL inside formset | `AmdSetup+0x80` | — | High |
| DDR SPD Timing | TwtrL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x82` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | TwtrL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x83` | — | High |
| DDR SPD Timing | TwtrS Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x85` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR SPD Timing | TwtrS | Numeric | CONDITIONAL inside formset | `AmdSetup+0x86` | — | High |
| DDR Non-SPD Timing | TrdrdScL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x88` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TrdrdScL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x89` | — | High |
| DDR Non-SPD Timing | TrdrdSc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x8B` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TrdrdSc | Numeric | CONDITIONAL inside formset | `AmdSetup+0x8C` | — | High |
| DDR Non-SPD Timing | TrdrdSd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x8E` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TrdrdSd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x8F` | — | High |
| DDR Non-SPD Timing | TrdrdDd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x91` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TrdrdDd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x92` | — | High |
| DDR Non-SPD Timing | TwrwrScL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x94` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TwrwrScL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x95` | — | High |
| DDR Non-SPD Timing | TwrwrSc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x97` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TwrwrSc | Numeric | CONDITIONAL inside formset | `AmdSetup+0x98` | — | High |
| DDR Non-SPD Timing | TwrwrSd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x9A` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TwrwrSd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x9B` | — | High |
| DDR Non-SPD Timing | TwrwrDd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x9D` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | TwrwrDd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x9E` | — | High |
| DDR Non-SPD Timing | Twrrd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xA0` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | Twrrd | Numeric | CONDITIONAL inside formset | `AmdSetup+0xA1` | — | High |
| DDR Non-SPD Timing | Trdwr Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xA3` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| DDR Non-SPD Timing | Trdwr | Numeric | CONDITIONAL inside formset | `AmdSetup+0xA4` | — | High |
| DDR Bus Configuration | Processor CA drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xA6` | `Auto` = `255` **(default)**<br>`High Impedance` = `0`<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30` | Medium/High |
| DDR Bus Configuration | Processor CK drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xA7` | `Auto` = `255` **(default)**<br>`High Impedance` = `0`<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30` | Medium/High |
| DDR Bus Configuration | Processor DQ drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xA8` | `Auto` = `255` **(default)**<br>`High Impedance` = `0`<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30` | Medium/High |
| DDR Bus Configuration | Processor DQS drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xA9` | `Auto` = `255` **(default)**<br>`High Impedance` = `0`<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30` | Medium/High |
| DDR Bus Configuration | Processor CA ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAA` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30`<br>`Disable` = `0` | Medium/High |
| DDR Bus Configuration | Processor CK ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAB` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30`<br>`Disable` = `0` | Medium/High |
| DDR Bus Configuration | Processor CS ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAC` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30`<br>`Disable` = `0` | Medium/High |
| DDR Bus Configuration | Processor DQ ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAD` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30`<br>`Disable` = `0` | Medium/High |
| DDR Bus Configuration | Processor DQS ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAE` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`30.0 Ohm` = `30`<br>`Disable` = `0` | Medium/High |
| DDR Bus Configuration | Dram DQ drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xAF` | `Auto` = `255` **(default)**<br>`34 ohm` = `0`<br>`40 ohm` = `1`<br>`48 ohm` = `2` | Medium/High |
| DDR Bus Configuration | Dram ODT impedance RTT_NOM_WR | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB0` | `Auto` = `255` **(default)**<br>`RTT_OFF` = `0`<br>`RZQ (240)` = `1`<br>`RZQ/2 (120)` = `2`<br>`RZQ/3 (80)` = `3`<br>`RZQ/4 (60)` = `4`<br>`RZQ/5 (48)` = `5`<br>`RZQ/6 (40)` = `6`<br>`RZQ/7 (34)` = `7` | Medium/High |
| DDR Bus Configuration | Dram ODT impedance RTT_NOM_RD | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB1` | `Auto` = `255` **(default)**<br>`RTT_OFF` = `0`<br>`RZQ (240)` = `1`<br>`RZQ/2 (120)` = `2`<br>`RZQ/3 (80)` = `3`<br>`RZQ/4 (60)` = `4`<br>`RZQ/5 (48)` = `5`<br>`RZQ/6 (40)` = `6`<br>`RZQ/7 (34)` = `7` | Medium/High |
| DDR Bus Configuration | Dram ODT impedance RTT_WR | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB2` | `Auto` = `255` **(default)**<br>`RTT_OFF` = `0`<br>`RZQ (240)` = `1`<br>`RZQ/2 (120)` = `2`<br>`RZQ/3 (80)` = `3`<br>`RZQ/4 (60)` = `4`<br>`RZQ/5 (48)` = `5`<br>`RZQ/6 (40)` = `6`<br>`RZQ/7 (34)` = `7` | Medium/High |
| DDR Bus Configuration | Dram ODT impedance RTT_PARK | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB3` | `Auto` = `255` **(default)**<br>`RTT_OFF` = `0`<br>`RZQ (240)` = `1`<br>`RZQ/2 (120)` = `2`<br>`RZQ/3 (80)` = `3`<br>`RZQ/4 (60)` = `4`<br>`RZQ/5 (48)` = `5`<br>`RZQ/6 (40)` = `6`<br>`RZQ/7 (34)` = `7` | Medium/High |
| DDR Bus Configuration | Dram ODT impedance DQS_RTT_PARK | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB4` | `Auto` = `255` **(default)**<br>`RTT_OFF` = `0`<br>`RZQ (240)` = `1`<br>`RZQ/2 (120)` = `2`<br>`RZQ/3 (80)` = `3`<br>`RZQ/4 (60)` = `4`<br>`RZQ/5 (48)` = `5`<br>`RZQ/6 (40)` = `6`<br>`RZQ/7 (34)` = `7` | Medium/High |
| DDR Controller Configuration | DDR RFM | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB5` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Power Options | Power Down Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB6` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Power Options | Phy Low Power Disable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x275` | `Auto` = `255` **(default)**<br>`0` = `0`<br>`1` = `1` | Medium/High |
| Thermal Throttling | On DIMM Temperature Sensor (ODTS) Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x276` | `Auto` = `255` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| Thermal Throttling | ODTS CMD Throttle Cycle Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x277` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| Thermal Throttling | ODTS CMD Throttle Cycle | Numeric | CONDITIONAL inside formset | `AmdSetup+0x278` | — | Medium/High |
| Thermal Throttling | Force Power Down Throttle | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x27A` | `Auto` = `255` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| DDR RAS | Disable Memory Error Injection | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB7` | `False` = `0`<br>`True` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR ECC Configuration | ECC | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB8` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR ECC Configuration | Memory Clear | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xB9` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| DDR Security | TSME | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBA` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| DDR Security | Data Scramble | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBB` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Addressing Options | Chipselect Interleaving | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBC` | `Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Addressing Options | Address Hash Bank | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBD` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Addressing Options | Address Hash CS | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBE` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| DDR Addressing Options | BankSwapMode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xBF` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`Swap APU` = `2` | Medium/High |
| DDR Training Options | DFE Read Training | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC0` | `Auto` = `255` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| DDR Training Options | DRAM PDA Enumerate ID Programming Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC1` | `Auto` = `255` **(default)**<br>`Toggling PDA enumeration mode` = `0`<br>`Legacy PDA enumeration mode` = `1` | Medium/High |
| DDR Training Options | PPT Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x274` | `Auto` = `255` **(default)**<br>`Disabled` = `0` | High |
| DDR Memory MBIST | MBIST Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC2` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Memory MBIST | MBIST Test Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC3` | `Interface Mode` = `0`<br>`Data Eye Mode` = `1`<br>`Both` = `2`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Memory MBIST | MBIST Aggressors | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC4` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Memory MBIST | MBIST Per Bit Slave Die Reporting | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC5` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| DDR Data Eye | Pattern Select | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC6` | `Auto` = `255` **(default)**<br>`PRBS` = `0`<br>`SSO` = `1`<br>`Both` = `2` | Medium/High |
| DDR Data Eye | Pattern Length Select | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC7` | `Auto` = `255` **(default)**<br>`Manual` = `1` | Medium/High |
| DDR Data Eye | Pattern Length | Numeric | CONDITIONAL inside formset | `AmdSetup+0xC8` | — | Medium/High |
| DDR Data Eye | Aggressor Channel | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xC9` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`1 Aggressor Channel` = `1`<br>`3 Aggressor Channels` = `3`<br>`7 Aggressor Channels` = `7` | Medium/High |
| Accept | Active Memory Timing Settings | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xCC` | `Auto` = `255` **(default)**<br>`Enabled` = `1` | High |
| Accept | Maximum Memory Data Clock Speed | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0xCD` | `Auto` = `65535` **(default)**<br>`1600MT/s` = `1600`<br>`3200MT/s` = `3200`<br>`4267MT/s` = `4267`<br>`5500MT/s` = `5500`<br>`6400MT/s` = `6400`<br>`7000MT/S` = `7000`<br>`7500MT/S` = `7500`<br>`8000MT/S` = `8000` | Medium/High |
| LPDDR SPD Timing | Trcpage Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xCF` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Trcpage | Numeric | CONDITIONAL inside formset | `AmdSetup+0xD0` | — | High |
| LPDDR SPD Timing | Tcwl Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xD2` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Tcwl | Numeric | CONDITIONAL inside formset | `AmdSetup+0xD4` | — | High |
| LPDDR SPD Timing | Tcl Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xD6` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Tcl | Numeric | CONDITIONAL inside formset | `AmdSetup+0xD8` | — | High |
| LPDDR SPD Timing | Trcdrd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xDA` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Trcdrd | Numeric | CONDITIONAL inside formset | `AmdSetup+0xDC` | — | High |
| LPDDR SPD Timing | Trcdwr Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xDE` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Trcdwr | Numeric | CONDITIONAL inside formset | `AmdSetup+0xE0` | — | High |
| LPDDR SPD Timing | Trp Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xE2` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Trp | Numeric | CONDITIONAL inside formset | `AmdSetup+0xE4` | — | High |
| LPDDR SPD Timing | TrpPb Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xE6` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | TrpPb | Numeric | CONDITIONAL inside formset | `AmdSetup+0xE8` | — | High |
| LPDDR SPD Timing | Trfc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xEA` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | Trfc | Numeric | CONDITIONAL inside formset | `AmdSetup+0xEB` | — | High |
| LPDDR SPD Timing | TrfcPb Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xED` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR SPD Timing | TrfcPb | Numeric | CONDITIONAL inside formset | `AmdSetup+0xEE` | — | High |
| LPDDR Non-SPD Timing | Tras Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xF0` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Tras | Numeric | CONDITIONAL inside formset | `AmdSetup+0xF1` | — | High |
| LPDDR Non-SPD Timing | Trc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xF3` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Trc | Numeric | CONDITIONAL inside formset | `AmdSetup+0xF4` | — | High |
| LPDDR Non-SPD Timing | TrcPb Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xF6` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrcPb | Numeric | CONDITIONAL inside formset | `AmdSetup+0xF7` | — | High |
| LPDDR Non-SPD Timing | TrrdS Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xF9` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrrdS | Numeric | CONDITIONAL inside formset | `AmdSetup+0xFA` | — | High |
| LPDDR Non-SPD Timing | TrrdL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xFC` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrrdL | Numeric | CONDITIONAL inside formset | `AmdSetup+0xFD` | — | High |
| LPDDR Non-SPD Timing | Tfaw Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0xFF` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Tfaw | Numeric | CONDITIONAL inside formset | `AmdSetup+0x100` | — | High |
| LPDDR Non-SPD Timing | TwtrS Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x102` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwtrS | Numeric | CONDITIONAL inside formset | `AmdSetup+0x103` | — | High |
| LPDDR Non-SPD Timing | TwtrL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x105` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwtrL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x106` | — | High |
| LPDDR Non-SPD Timing | Twr Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x108` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Twr | Numeric | CONDITIONAL inside formset | `AmdSetup+0x109` | — | High |
| LPDDR Non-SPD Timing | Trtp Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x10B` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Trtp | Numeric | CONDITIONAL inside formset | `AmdSetup+0x10C` | — | High |
| LPDDR Non-SPD Timing | Tcke Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x10E` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Tcke | Numeric | CONDITIONAL inside formset | `AmdSetup+0x10F` | — | High |
| LPDDR Non-SPD Timing | TrdrdScL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x111` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrdrdScL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x112` | — | High |
| LPDDR Non-SPD Timing | TrdrdSc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x114` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrdrdSc | Numeric | CONDITIONAL inside formset | `AmdSetup+0x115` | — | High |
| LPDDR Non-SPD Timing | TrdrdSd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x117` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrdrdSd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x118` | — | High |
| LPDDR Non-SPD Timing | TrdrdDd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x11A` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TrdrdDd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x11B` | — | High |
| LPDDR Non-SPD Timing | TwrwrScL Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x11D` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwrwrScL | Numeric | CONDITIONAL inside formset | `AmdSetup+0x11E` | — | High |
| LPDDR Non-SPD Timing | TwrwrSc Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x120` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwrwrSc | Numeric | CONDITIONAL inside formset | `AmdSetup+0x121` | — | High |
| LPDDR Non-SPD Timing | TwrwrSd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x123` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwrwrSd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x124` | — | High |
| LPDDR Non-SPD Timing | TwrwrDd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x126` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | TwrwrDd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x127` | — | High |
| LPDDR Non-SPD Timing | Twrrd Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x129` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Twrrd | Numeric | CONDITIONAL inside formset | `AmdSetup+0x12A` | — | High |
| LPDDR Non-SPD Timing | Trdwr Ctrl | OneOf | CONDITIONAL inside formset | `AmdSetup+0x12C` | `Auto` = `0` **(default)**<br>`Manual` = `1` | High |
| LPDDR Non-SPD Timing | Trdwr | Numeric | CONDITIONAL inside formset | `AmdSetup+0x12D` | — | High |
| LPDDR Controller Configuration | LPDDR Refresh Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x12F` | `Auto` = `255` **(default)**<br>`All Banks` = `1`<br>`Per Bank` = `2` | Medium/High |
| LPDDR Controller Configuration | LPDDR RFM | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x130` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Controller Configuration | WCK Always On | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x131` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Controller Configuration | DVFSC Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x132` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Controller Configuration | RRW Memory Test Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x133` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| LPDDR Power Options | Power Down Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x134` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Power Options | Phy Low Power Disable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x135` | `Auto` = `255` **(default)**<br>`0` = `0`<br>`1` = `1` | Medium/High |
| LPDDR Bus Configuration | CA drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x136` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40` | Medium/High |
| LPDDR Bus Configuration | CK drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x137` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40` | Medium/High |
| LPDDR Bus Configuration | DQ drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x138` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40` | Medium/High |
| LPDDR Bus Configuration | DQS drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x139` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40` | Medium/High |
| LPDDR Bus Configuration | WCK drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13A` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40` | Medium/High |
| LPDDR Bus Configuration | Processor CA ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13B` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Processor CK ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13C` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Processor CS ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13D` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Processor DQ ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13E` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Processor DQS ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x13F` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Processor WCK ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x140` | `Auto` = `255` **(default)**<br>`120.0 Ohm` = `120`<br>`60.0 Ohm` = `60`<br>`40.0 Ohm` = `40`<br>`Disable` = `0` | Medium/High |
| LPDDR Bus Configuration | Dram CA ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x141` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3`<br>`RZQ/4` = `4`<br>`RZQ/5` = `5`<br>`RZQ/6` = `6` | Medium/High |
| LPDDR Bus Configuration | Dram CS ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x142` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3` | Medium/High |
| LPDDR Bus Configuration | Dram DQ ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x143` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3`<br>`RZQ/4` = `4`<br>`RZQ/5` = `5`<br>`RZQ/6` = `6` | Medium/High |
| LPDDR Bus Configuration | Dram WCK ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x144` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3`<br>`RZQ/4` = `4`<br>`RZQ/5` = `5`<br>`RZQ/6` = `6` | Medium/High |
| LPDDR Bus Configuration | Dram Non-Target ODT impedance | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x145` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3`<br>`RZQ/4` = `4`<br>`RZQ/5` = `5`<br>`RZQ/6` = `6` | Medium/High |
| LPDDR Bus Configuration | Dram Pull-Down drive strengths | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x146` | `Auto` = `255` **(default)**<br>`Disable` = `0`<br>`RZQ/1` = `1`<br>`RZQ/2` = `2`<br>`RZQ/3` = `3`<br>`RZQ/4` = `4`<br>`RZQ/5` = `5`<br>`RZQ/6` = `6` | Medium/High |
| LPDDR RAS | DRAM Read Link ECC Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x147` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR RAS | DRAM Write Link ECC Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x148` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR RAS | Disable Memory Error Injection | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x149` | `False` = `0`<br>`True` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR RAS | Memory Clear | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14A` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| LPDDR Security | TSME | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14B` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| LPDDR Security | Data Scramble | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14C` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Addressing Options | Chip Select Interleaving | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14D` | `Disabled` = `0`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Addressing Options | Bank Swap | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14E` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Addressing Options | BankGroup Swap | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x14F` | `Disabled` = `0`<br>`Enabled` = `2`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Addressing Options | Address Hash Bank | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x150` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Addressing Options | Address Hash CS | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x151` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| LPDDR Training Options | DFE Read Training | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x152` | `Auto` = `255` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| LPDDR Training Options | DFE Write Training | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x153` | `Auto` = `255` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| LPDDR Training Options | HW scans above DDR8000 or above | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x273` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| LPDDR Memory MBIST | MBIST Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x154` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Memory MBIST | MBIST Test Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x155` | `Interface Mode` = `0`<br>`Data Eye Mode` = `1`<br>`Both` = `2`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Memory MBIST | MBIST Aggressors | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x156` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `255` **(default)** | Medium/High |
| LPDDR Data Eye | Pattern Select | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x157` | `Auto` = `255` **(default)**<br>`PRBS` = `0`<br>`SSO` = `1`<br>`Both` = `2` | Medium/High |
| LPDDR Data Eye | Pattern Length Select | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x158` | `Auto` = `255` **(default)**<br>`Manual` = `1` | Medium/High |
| LPDDR Data Eye | Pattern Length | Numeric | CONDITIONAL inside formset | `AmdSetup+0x159` | — | Medium/High |
| LPDDR Data Eye | Aggressor Channel | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15A` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`1 Aggressor Channel` = `1`<br>`3 Aggressor Channels` = `3`<br>`7 Aggressor Channels` = `7` | Medium/High |
| LPDDR Data Eye | Read Voltage Sweep Step Size | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15B` | `Auto` = `255` **(default)**<br>`1` = `0`<br>`2` = `1`<br>`4` = `4` | High |
| LPDDR Data Eye | Read Timing Sweep Step Size | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15C` | `Auto` = `255` **(default)**<br>`1` = `1`<br>`2` = `2`<br>`4` = `4` | High |
| LPDDR Data Eye | Write Voltage Sweep Step Size | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15D` | `Auto` = `255` **(default)**<br>`1` = `0`<br>`2` = `1`<br>`4` = `4` | High |
| LPDDR Data Eye | Write Timing Sweep Step Size | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15E` | `Auto` = `255` **(default)**<br>`1` = `1`<br>`2` = `2`<br>`4` = `4` | High |
| NBIO Common Options | IOMMU | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x15F` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | High |
| NBIO Common Options | Pre-boot DMA Protection | OneOf | CONDITIONAL inside formset | `AmdSetup+0x160` | `Auto` = `15` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| NBIO Common Options | Kernel DMA Protection Indicator | OneOf | CONDITIONAL inside formset | `AmdSetup+0x161` | `Auto` = `15` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| NBIO Common Options | SCPC attribute control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x162` | `0` = `0`<br>`1` = `1`<br>`2` = `2`<br>`3` = `3`<br>`Customized` = `255` **(default)** | Medium/High |
| NBIO Common Options | PCIe ARI Support | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x163` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| NBIO Common Options | PCIe loopback Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x164` | `Auto` = `15` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | Medium/High |
| NBIO Common Options | PSPP Policy | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x165` | `Auto` = `15` **(default)**<br>`Disabled` = `0`<br>`Balanced` = `2` | Medium/High |
| GFX Configuration | iGPU Configuration | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x166` | `Auto` = `15`<br>`iGPU Disabled` = `0`<br>`UMA_SPECIFIED` = `1` **(default)**<br>`UMA_AUTO` = `2`<br>`UMA_GAME_OPTIMIZED` = `3`<br>`UMA_WORKSTATION_OPTIMIZED` = `4`<br>`UMA_LEGACY_SPECIFIED` = `5` | Medium/High |
| GFX Configuration | iGPU Mem Bar Configuration | OneOf | CONDITIONAL inside formset | `AmdSetup+0x271` | `Auto` = `15` **(default)**<br>`LegacyGfxBAR` = `1`<br>`LargeBAR` = `2`<br>`ResizableBAR` = `3` | Medium/High |
| GFX Configuration | UMA Frame buffer Size | OneOf | CONDITIONAL inside formset | `AmdSetup+0x167` | `Auto` = `4294967295`<br>`512M` = `512`<br>`768M` = `768`<br>`1G` = `1024`<br>`2G` = `2048` **(default)**<br>`3G` = `3072`<br>`4G` = `4096`<br>`5G` = `5120`<br>`6G` = `6144`<br>`7G` = `7168`<br>`8G` = `8192`<br>`9G` = `9216`<br>`10G` = `10240`<br>`11G` = `11264`<br>`12G` = `12288`<br>`13G` = `13312`<br>`14G` = `14336`<br>`15G` = `15360`<br>`16G` = `16384`<br>`24G` = `24576`<br>`32G` = `32768`<br>`48G` = `49152`<br>`64G` = `65536`<br>`72G` = `73728`<br>`80G` = `81920`<br>`96G` = `98304`<br>`128G` = `131072` | Medium/High |
| GFX Configuration | Dedicated Graphics Memory Auto Level | OneOf | CONDITIONAL inside formset | `AmdSetup+0x16B` | `UmaSizeMinimum` = `0` **(default)**<br>`UmaSizeMedium` = `1`<br>`UmaSizeHigh` = `2` | Medium/High |
| GFX Configuration | Remaining System Memory | OneOf | CONDITIONAL inside formset | `AmdSetup+0x16C` | `RemainGB` = `0` **(default)** | Medium/High |
| GFX Configuration | GPU Host Translation Cache | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x16D` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| GFX Configuration | TCON INSTANT ON LOGO | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x16E` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Audio Configuration | NB Azalia | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x16F` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Audio Configuration | Audio IOs | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x170` | `Auto` = `255` **(default)**<br>`HDA(3SDI)` = `1`<br>`HDA(1SDI) + SW0(1MDATA)` = `2`<br>`SW0(4MDATA) + SW1(1MDATA)` = `3`<br>`HDA(3SDI) +  PDM(2CH)` = `4`<br>`HDA(1SDI) +  PDM(6CH)` = `5`<br>`HDA(1SDI) + SW0(1MDATA) + PDM(2CH)` = `6`<br>`SW0(4MDATA) + PDM(6CH)` = `7`<br>`SW0(4MDATA) + SW1(1MDATA) + PDM(2CH)` = `8`<br>`3I2S + 1 REFCLK + 1 INTR` = `9`<br>`HDA(3SDI) +  PDM(6CH) +I2S` = `10`<br>`HDA(3SDI) +  PDM(8CH) ` = `11`<br>`HDA(1SDI) + SW0(1MDATA) + PDM(6CH) + I2S` = `12`<br>`SW0(4MDATA)+ SW1(1MDATA) +PDM(6CH) + I2S ` = `13`<br>`SW0(4MDATA) + SW1(1MDATA)+ PDM(8CH) ` = `14`<br>`LPFLL Clock out ` = `15`<br>`SW0(3MDATA) + SW1(2MDATA)` = `16`<br>`SW0(3MDATA) + SW1(2MDATA) + PDM(2CH)` = `17`<br>`SW0(3MDATA) + SW1(2MDATA) + PDM(8CH)` = `18`<br>`SW0(3MDATA) + SW1(2MDATA) + PDM(6CH) + I2S` = `19`<br>`2I2S + 1 INTR + PDM(4CH)` = `20` | Medium/High |
| I3C/I2C Configuration Options | I3C/I2C 0 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x171` | `Both Disabled` = `3`<br>`I3C Enabled` = `0`<br>`I2C Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C 0 Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x172` | `I3C` = `0`<br>`I2C` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C/I2C 1 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x173` | `Both Disabled` = `3`<br>`I3C Enabled` = `0`<br>`I2C Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C 1 Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x174` | `I3C` = `0`<br>`I2C` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C/I2C 2 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x175` | `Both Disabled` = `3`<br>`I3C Enabled` = `0`<br>`I2C Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C 2 Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x176` | `I3C` = `0`<br>`I2C` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C/I2C 3 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x177` | `Both Disabled` = `3`<br>`I3C Enabled` = `0`<br>`I2C Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| I3C/I2C Configuration Options | I3C 3 Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x178` | `I3C` = `0`<br>`I2C` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB Configuration Options | USB0 controller enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x179` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `15` **(default)** | Medium/High |
| USB Configuration Options | USB1 controller enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x17A` | `Enabled` = `1`<br>`Disabled` = `0`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 2.0 port enable | USB0 2.0 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x17B` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 2.0 port enable | USB0 2.0 Port 1 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x17C` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 2.0 port enable | USB0 2.0 Port 2 | OneOf | CONDITIONAL inside formset | `AmdSetup+0x17D` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 2.0 port enable | USB0 2.0 Port 3 | OneOf | CONDITIONAL inside formset | `AmdSetup+0x17E` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 2.0 port enable | USB0 2.0 Port 4 | OneOf | CONDITIONAL inside formset | `AmdSetup+0x17F` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB1 2.0 port enable | USB1 2.0 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x180` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB2 2.0 port enable | USB2 2.0 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x181` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB3 2.0 port enable | USB3 2.0 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x182` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 3.1 port enable | USB0 3.1 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x183` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB0 3.1 port enable | USB0 3.1 Port 1 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x184` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB1 3.1 port enable | USB1 3.1 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x185` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB2 3.1 port enable | USB2 3.1 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x186` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB3 3.1 port enable | USB3 3.1 Port 0 | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x187` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Ac Power Loss Options | Ac Loss Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x188` | `Always Off` = `0`<br>`Always On` = `1`<br>`Reserved` = `2`<br>`Previous` = `3` **(default)**<br>`Auto` = `15` | Medium/High |
| Uart Configuration Options | Uart 0 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x189` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 0 Legacy Options | OneOf | CONDITIONAL inside formset | `AmdSetup+0x18A` | `Disabled` = `0`<br>`0x2E8` = `1`<br>`0x2F8` = `2`<br>`0x3E8` = `3`<br>`0x3F8` = `4`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 1 Enable (no HW FC) | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x18B` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 1 Legacy Options | OneOf | CONDITIONAL inside formset | `AmdSetup+0x18C` | `Disabled` = `0`<br>`0x2E8` = `1`<br>`0x2F8` = `2`<br>`0x3E8` = `3`<br>`0x3F8` = `4`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 2 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x18D` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 2 Legacy Options | OneOf | CONDITIONAL inside formset | `AmdSetup+0x18E` | `Disabled` = `0`<br>`0x2E8` = `1`<br>`0x2F8` = `2`<br>`0x3E8` = `3`<br>`0x3F8` = `4`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 3 Enable (no HW FC) | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x18F` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 3 Legacy Options | OneOf | CONDITIONAL inside formset | `AmdSetup+0x190` | `Disabled` = `0`<br>`0x2E8` = `1`<br>`0x2F8` = `2`<br>`0x3E8` = `3`<br>`0x3F8` = `4`<br>`Auto` = `15` **(default)** | Medium/High |
| Uart Configuration Options | Uart 4 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x191` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB4 Configuration Options | USB4 pre-CM Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x192` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB4 RT0 | RT0 Router Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x193` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB4 RT0 | RT0 PCIe Tunnel | OneOf | CONDITIONAL inside formset | `AmdSetup+0x194` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB4 RT1 | RT1 Router Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x195` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| USB4 RT1 | RT1 PCIe Tunnel | OneOf | CONDITIONAL inside formset | `AmdSetup+0x196` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| SPI Configuration Options | HFP Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x197` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| SPI Configuration Options | HID Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x198` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| SPI Configuration Options | HID2 Enable | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x199` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| SPI Configuration Options | HID Spi Read Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x19A` | `Quad IO 1-4-4` = `5` **(default)**<br>`Dual IO 1-2-2` = `4`<br>`Fast Read 1-1-1` = `7` | Medium/High |
| SPI Configuration Options | HID Spi Speed | OneOf | CONDITIONAL inside formset | `AmdSetup+0x19B` | `(1)33HMz` = `1` **(default)**<br>`(2)22NMz` = `2`<br>`(3)17.6Mhz` = `3`<br>`(5)800Khz` = `5`<br>`spi_spd6` = `6`<br>`spi_spd7` = `7` | Medium/High |
| SPI Configuration Options | HID Spi Speed6 N | Numeric | CONDITIONAL inside formset | `AmdSetup+0x19C` | — | Medium/High |
| SPI Configuration Options | HID Spi Speed7 N | Numeric | CONDITIONAL inside formset | `AmdSetup+0x19D` | — | Medium/High |
| SPI Configuration Options | HID2 Spi Read Mode | OneOf | CONDITIONAL inside formset | `AmdSetup+0x19E` | `Quad IO 1-4-4` = `5` **(default)**<br>`Dual IO 1-2-2` = `4`<br>`Fast Read 1-1-1` = `7` | Medium/High |
| SPI Configuration Options | HID2 Spi Speed | OneOf | CONDITIONAL inside formset | `AmdSetup+0x19F` | `(1)33HMz` = `1` **(default)**<br>`(2)22NMz` = `2`<br>`(3)17.6Mhz` = `3`<br>`(5)800Khz` = `5`<br>`spi_spd6` = `6`<br>`spi_spd7` = `7` | Medium/High |
| SPI Configuration Options | HID2 Spi Speed6 N | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1A0` | — | Medium/High |
| SPI Configuration Options | HID2 Spi Speed7 N | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1A1` | — | Medium/High |
| SMU Common Options | System Configuration | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1A2` | `15W` = `1`<br>`28W` = `2`<br>`30W` = `3`<br>`35W` = `4`<br>`45W` = `5`<br>`54W` = `6`<br>`65W` = `7`<br>`Auto` = `15` **(default)**<br>`20W` = `8` | Medium/High |
| SMU Common Options | SPL Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1A3` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | Sustained Power Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1A4` | — | Medium/High |
| SMU Common Options | PPT Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1A8` | `Manual` = `1`<br>`Auto` = `0` **(default)** | High |
| SMU Common Options | Fast PPT Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1A9` | — | High |
| SMU Common Options | Slow PPT Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1AD` | — | High |
| SMU Common Options | Slow PPT Time Constant | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1B1` | — | High |
| SMU Common Options | STAPM Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1B5` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | System Temperature Tracking | OneOf | CONDITIONAL inside formset | `AmdSetup+0x1B6` | `Auto` = `15` **(default)**<br>`1` = `1`<br>`0` = `0` | Medium/High |
| SMU Common Options | STAPM Boost Override | OneOf | CONDITIONAL inside formset | `AmdSetup+0x1B7` | `Auto` = `15` **(default)**<br>`0` = `1`<br>`1` = `0` | Medium/High |
| SMU Common Options | STAPM Boost | OneOf | CONDITIONAL inside formset | `AmdSetup+0x1B8` | `Auto` = `15` **(default)**<br>`1` = `1`<br>`0` = `0` | Medium/High |
| SMU Common Options | Tskin Time Constant (STAPM) | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1B9` | — | Medium/High |
| SMU Common Options | Thermal Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1BD` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | TjMax | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1BE` | — | Medium/High |
| SMU Common Options | TDC Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1C2` | `Manual` = `1`<br>`Auto` = `0` **(default)** | High |
| SMU Common Options | TDC_VDDCR_VDD | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1C3` | — | High |
| SMU Common Options | TDC_VDDCR_SOC | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1C7` | — | High |
| SMU Common Options | TDC_VDDCR_SR | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1CB` | — | High |
| SMU Common Options | EDC Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1CF` | `Manual` = `1`<br>`Auto` = `0` **(default)** | High |
| SMU Common Options | EDC_VDDCR_VDD | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1D0` | — | High |
| SMU Common Options | EDC_VDDCR_SOC | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1D4` | — | High |
| SMU Common Options | EDC_VDDCR_SR | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1D8` | — | High |
| SMU Common Options | PSI3 Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1DC` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | PSI3_VDDCR_VDD | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1DD` | — | Medium/High |
| SMU Common Options | PROCHOT Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x1E1` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | PROCHOT Deassertion Ramp Time | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1E2` | — | Medium/High |
| SMU Common Options | STT Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x1E6` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | STT_PCB_SENSOR_COUNT | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1E7` | — | Medium/High |
| SMU Common Options | STT_MIN_POWER_LIMIT | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1E8` | — | Medium/High |
| SMU Common Options | STT_M1 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1EA` | — | Medium/High |
| SMU Common Options | STT_M2 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1EC` | — | Medium/High |
| SMU Common Options | STT_M3 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1EE` | — | Medium/High |
| SMU Common Options | STT_M4 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1F0` | — | Medium/High |
| SMU Common Options | STT_M5 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1F2` | — | Medium/High |
| SMU Common Options | STT_M6 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1F4` | — | Medium/High |
| SMU Common Options | STT_C_APU | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1F6` | — | Medium/High |
| SMU Common Options | STT_C_HS2 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1F8` | — | Medium/High |
| SMU Common Options | STT_ALPHA_APU | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1FA` | — | Medium/High |
| SMU Common Options | STT_ALPHA_HS2 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1FC` | — | Medium/High |
| SMU Common Options | STT_SKIN_TEMPERATURE_LIMIT_APU | Numeric | CONDITIONAL inside formset | `AmdSetup+0x1FE` | — | Medium/High |
| SMU Common Options | STT_SKIN_TEMPERATURE_LIMIT_HS2 | Numeric | CONDITIONAL inside formset | `AmdSetup+0x200` | — | Medium/High |
| SMU Common Options | STT_ERROR_COEFF | Numeric | CONDITIONAL inside formset | `AmdSetup+0x202` | — | Medium/High |
| SMU Common Options | STT_ERROR_RATE_COEFF | Numeric | CONDITIONAL inside formset | `AmdSetup+0x204` | — | Medium/High |
| SMU Common Options | Fan Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x206` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | Force PWM Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x207` | `Force` = `1`<br>`Unforce` = `0` **(default)** | Medium/High |
| SMU Common Options | Force PWM | Numeric | CONDITIONAL inside formset | `AmdSetup+0x208` | — | Medium/High |
| SMU Common Options | Fan Table Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x209` | `Manual` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| SMU Common Options | Low Temperature | Numeric | CONDITIONAL inside formset | `AmdSetup+0x20A` | — | Medium/High |
| SMU Common Options | Medium Temperature | Numeric | CONDITIONAL inside formset | `AmdSetup+0x20E` | — | Medium/High |
| SMU Common Options | High Temperature | Numeric | CONDITIONAL inside formset | `AmdSetup+0x212` | — | Medium/High |
| SMU Common Options | Critical Temperature | Numeric | CONDITIONAL inside formset | `AmdSetup+0x216` | — | Medium/High |
| SMU Common Options | Low Pwm | Numeric | CONDITIONAL inside formset | `AmdSetup+0x21A` | — | Medium/High |
| SMU Common Options | Medium Pwm | Numeric | CONDITIONAL inside formset | `AmdSetup+0x21E` | — | Medium/High |
| SMU Common Options | High Pwm | Numeric | CONDITIONAL inside formset | `AmdSetup+0x222` | — | Medium/High |
| SMU Common Options | Temperature Hysteresis | Numeric | CONDITIONAL inside formset | `AmdSetup+0x226` | — | Medium/High |
| SMU Common Options | Pwm Frequency | OneOf | CONDITIONAL inside formset | `AmdSetup+0x22A` | `Auto` = `15` **(default)**<br>`1` = `1`<br>`0` = `0` | Medium/High |
| SMU Common Options | Fan polarity | OneOf | CONDITIONAL inside formset | `AmdSetup+0x22B` | `Auto` = `15` **(default)**<br>`1` = `1`<br>`0` = `0` | Medium/High |
| SmartShift Control | SmartShift Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x22C` | `Auto` = `15` **(default)**<br>`Manual` = `1` | Medium/High |
| SmartShift Control | SmartShift Enable | OneOf | CONDITIONAL inside formset | `AmdSetup+0x22D` | `Auto` = `15` **(default)**<br>`Disable` = `0`<br>`Enable` = `1` | Medium/High |
| SmartShift Control | APU Only sPPT Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x22E` | — | High |
| SmartShift Control | Sustained PowerLimit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x232` | — | Medium/High |
| SmartShift Control | Fast PPT Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x236` | — | High |
| SmartShift Control | Slow PPT Limit | Numeric | CONDITIONAL inside formset | `AmdSetup+0x23A` | — | High |
| SMU Feature | FAN CONTROLLER | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x27B` | `Disabled` = `0` **(default)**<br>`Enabled` = `1`<br>`Auto` = `15` | Medium/High |
| SMU Feature | CPUOFF | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x27C` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| SOC Miscellaneous Control | Pluton Attribute Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x23E` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| SOC Miscellaneous Control | Pluton fTPM Attribute Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x23F` | `Disabled` = `0`<br>`Enabled` = `1` **(default)** | High |
| SOC Miscellaneous Control | Trusted Platform Module | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x240` | `Auto` = `254` **(default)**<br>`Disabled` = `255`<br>`Enable dTPM` = `0`<br>`Enable ASP fTPM` = `1`<br>`Enable Pluton fTPM` = `2` | Medium/High |
| SOC Miscellaneous Control | Pluton Support | OneOf | CONDITIONAL inside formset | `AmdSetup+0x270` | `Auto` = `15` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | High |
| SOC Miscellaneous Control | Microsoft Security Levels | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x242` | `Customized` = `255` **(default)**<br>`dTPM Level 1 without Pluton Security Processor` = `1`<br>`dTPM Level 2 without Pluton Security Processor` = `2`<br>`dTPM Level 3 without Pluton Security Processor` = `3`<br>`dTPM Level 1 with Pluton Security Processor` = `11`<br>`dTPM Level 2 with Pluton Security Processor` = `12`<br>`dTPM Level 3 with Pluton Security Processor` = `13`<br>`Pluton fTPM Level 1 with Pluton Security Processor` = `31`<br>`Pluton fTPM Level 2 with Pluton Security Processor` = `32`<br>`Pluton fTPM Level 3 with Pluton Security Processor` = `33`<br>`ASP fTPM Level 0` = `40`<br>`ASP fTPM Level 1 without Pluton Security Processor` = `41`<br>`ASP fTPM Level 2 without Pluton Security Processor` = `42`<br>`ASP fTPM Level 1 with Pluton Security Processor` = `51`<br>`ASP fTPM Level 2 with Pluton Security Processor` = `52` | Medium/High |
| SOC Miscellaneous Control | Secured-core Auto enablement | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x243` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| SOC Miscellaneous Control | DRTM Support | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x244` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| SOC Miscellaneous Control | SMM Isolation Support | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x245` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | High |
| SOC Miscellaneous Control | Pro Part attribute control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x246` | `0` = `0` **(default)**<br>`1` = `1` | Medium/High |
| SOC Miscellaneous Control | ABL Console Out Control | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x247` | `Auto` = `2` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | Medium/High |
| SOC Miscellaneous Control | ABL Console Out Serial Port | OneOf | CONDITIONAL inside formset | `AmdSetup+0x248` | `Auto` = `255` **(default)**<br>`LPC UART` = `0`<br>`FCH UART0` = `1`<br>`FCH UART1` = `2`<br>`FCH UART2` = `3`<br>`FCH UART3` = `4` | Medium/High |
| SOC Miscellaneous Control | ABL Console Out Boot Mode Select | OneOf | CONDITIONAL inside formset | `AmdSetup+0x249` | `ALL` = `255` **(default)**<br>`S3/S0i3 only` = `2`<br>`Normal boot only` = `1` | Medium/High |
| SOC Miscellaneous Control | ABL PMU message Control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x24A` | `Auto` = `255` **(default)**<br>`Maximal debug messages` = `4`<br>`Detailed debug message` = `5`<br>`Coarse debug message` = `10`<br>`Stage completion` = `200`<br>`Assertion message` = `201`<br>`Firmware completion message only` = `254` | Medium/High |
| SOC Miscellaneous Control | PSP RPMC Switch | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x24B` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | Medium/High |
| SOC Miscellaneous Control | ABL Training Mode | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x24C` | `default` = `0` **(default)**<br>`serial` = `1`<br>`parallel` = `2` | Medium/High |
| Pluton Options | Pluton UART | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x24D` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | High |
| Pluton Options | Pluton UART Port | OneOf | CONDITIONAL inside formset | `AmdSetup+0x24E` | `UART 0` = `0`<br>`UART 1` = `1`<br>`UART 2` = `2`<br>`UART 3` = `3`<br>`UART 4` = `4`<br>`UART 5` = `5`<br>`UART 6` = `6`<br>`UART 7` = `7`<br>`Auto` = `15` **(default)** | High |
| Pluton Options | Pluton Power Gating | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x272` | `Auto` = `255` **(default)**<br>`Disabled` = `0`<br>`Enabled` = `1` | High |
| Firmware Anti-rollback (FAR) | FAR enforcement state | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x251` | `Enabled` = `1`<br>`Disabled` = `0` **(default)** | High |
| Firmware Anti-rollback (FAR) | SPL value in the CPU fuse | Numeric | UNSUPPRESSED inside formset | `AmdSetup+0x252` | — | High |
| Firmware Anti-rollback (FAR) | SPL value in the SPL table | Numeric | UNSUPPRESSED inside formset | `AmdSetup+0x256` | — | High |
| Firmware Anti-rollback (FAR) | Initial SPL value for FAR | Numeric | CONDITIONAL inside formset | `AmdSetup+0x25A` | — | High |
| Firmware Anti-rollback (FAR) | SPL update switch | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x25E` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | High |
| Intrusion Detection | Intrusion Detection Control | Numeric | UNSUPPRESSED inside formset | `AmdSetup+0x25F` | — | Medium/High |
| Intrusion Detection | Intrusion Detection Enable | OneOf | CONDITIONAL inside formset | `AmdSetup+0x263` | `Auto` = `255` **(default)**<br>`Enabled` = `1`<br>`Disabled` = `0` | Medium/High |
| Intrusion Detection | Log Intrusion Event | OneOf | CONDITIONAL inside formset | `AmdSetup+0x264` | `Enabled` = `1`<br>`Disabled` = `0` **(default)** | Medium/High |
| Intrusion Detection | Clear TPM | OneOf | CONDITIONAL inside formset | `AmdSetup+0x265` | `Enabled` = `1`<br>`Disabled` = `0` **(default)** | High |
| Intrusion Detection | Power Off System | OneOf | CONDITIONAL inside formset | `AmdSetup+0x266` | `Enabled` = `1`<br>`Disabled` = `0` **(default)** | Medium/High |
| SecureBio | SecureBio Support | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x267` | `Auto` = `15` **(default)**<br>`Enable` = `1`<br>`Disable` = `0` | High |
| SecureBio | SecureBio Camera Support | OneOf | CONDITIONAL inside formset | `AmdSetup+0x268` | `Disabled` = `0`<br>`XHCI Camera` = `1`<br>`MIPI Camera` = `2`<br>`Auto` = `15` **(default)** | High |
| AIM-T Options | AIM-T Support | OneOf | UNSUPPRESSED inside formset | `AmdSetup+0x269` | `AIM-T Disabled` = `0`<br>`AIM-T Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| AIM-T Options | MPM attribute control | OneOf | CONDITIONAL inside formset | `AmdSetup+0x26A` | `0` = `0` **(default)**<br>`1` = `1`<br>`2` = `2`<br>`3` = `3` | Medium/High |
| AIM-T Options | KVM for Wired Manageability | OneOf | CONDITIONAL inside formset | `AmdSetup+0x26B` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| AIM-T Options | Wireless Manageability | OneOf | CONDITIONAL inside formset | `AmdSetup+0x26C` | `Disabled` = `0`<br>`Enabled` = `1`<br>`Auto` = `15` **(default)** | Medium/High |
| AIM-T Options | Wireless KVM Mouse Protocol | OneOf | CONDITIONAL inside formset | `AmdSetup+0x26D` | `Absolute` = `0`<br>`Simple` = `1`<br>`Auto` = `0` **(default)** | Medium/High |
| AIM-T Options | WirelessKvmMouseSimpleResolutionX | Numeric | CONDITIONAL inside formset | `AmdSetup+0x26E` | — | Medium/High |
| AIM-T Options | WirelessKvmMouseSimpleResolutionY | Numeric | CONDITIONAL inside formset | `AmdSetup+0x26F` | — | Medium/High |

The SetupUtility FFS GUID is `FE3542FE-C1D3-4EF8-657C-8048606FF670`; it is distinct from the HII formset GUIDs above.

## Defaults, live values and behavior are separate

### IFR/source defaults

The full audit lists `Dynamic LID` with `Disabled = 0` marked as the source default and `Enabled = 1` as the alternative. This IFR default is static metadata, not a runtime read, and does not establish that opening the lid powers on the machine. ACPI lid-state methods and the setup option are separate layers.

### Retained runtime read

The baseline recorded one live value:

```text
QuestionId:      0x1064
VarStore:        SystemConfig
VarStore GUID:   A04A27F4-DF00-4D42-B552-39511302113D
VarStore offset: 0x6E
0x00:            Disabled
0x01:            Enabled
Runtime read:    Setup[0x6E] = 0x01
```

This confirms the reported Quiet Boot value at that observation only. It does not convert any IFR default into a live value, and it does not establish the result of saving a changed setting. Proposed live values for Dynamic LID, AC Loss, Auto Wake S5, Charger BYPASS and other settings remain unconfirmed until an identified variable capture is recovered (pending **P12**).

### Behavioral evidence

A setting's option/default and a runtime read do not establish what the firmware does after a change. The retained SREP record concerns runtime form visibility; it is not a behavioral test of each control. Any future overlay must identify the variable, GUID/VarStore, offset, width, initial value, operation and observed result separately.

## Visibility mechanisms and evidence limits

The source distinguishes variable editing from form display: a variable change does not reveal a menu. It identifies UMAF/UniversalAMDFormBrowser as candidates for the separate AMD formsets, SREP runtime FormBrowser visibility changes followed by SetupUtilityApp, and SREP-RUS suppression cancellation in loaded HII packages. These are source analysis descriptions, not newly validated procedures. Suppression cancellation can also bypass legitimate hardware-dependent conditions. The source reports matching standard SREP GUIDs for AMD PBS, AMD CBS, Power and Advanced; a match does not establish a successful session. Permanent SetupUtility modification remains unvalidated and subject to secure-capsule integrity/signature and recovery constraints. See [runtime visibility](srep-runtime-reveal.md).

The source also records a bounded negative search: `SetupUtility` contains `User Access Level`, but searches of `SetupUtility` and `H2OFormBrowserDxe` found no `Setup Menu Insyde Full Show`, `Hide Item Control`, `Developer Mode`, `Advanced Mode`, `Full Show`, or equivalent global show-all variable. This does not prove that every possible visibility mechanism is absent, and generic keyboard shortcuts are not verified for this build.

## Evidence coverage

| Record | Evidence class | Public source coverage |
|---|---|---|
| All five audit inventories | Static-confirmed | All source table rows included; underlying firmware not distributed |
| IFR choices, defaults and suppression | Static-confirmed | Audit metadata, not runtime values or demonstrated behavior |
| Quiet Boot runtime read | Live-confirmed | Retained S1 report, unchanged; original variable capture not included |
| Other live setup values | Not established | P12 requires identified variable captures |

The original private audit is not distributed as a separate report. The curated technical inventory above closes the source-import gap without promoting static options, source risk annotations or suggested tools into live validation.
