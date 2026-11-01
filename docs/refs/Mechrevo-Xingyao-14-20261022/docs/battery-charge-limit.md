# Battery charge-limit protocol

## Scope and validated behavior

This page documents the firmware-level battery charge-limit subsystem identified in the `P916F-STX` IT5571 EC and exercised through the live ITE PMC2 host interface. Historical baseline observations are registered as [S1](research-sources.md#project-sources), the later battery-depletion reset observation as [S18](research-sources.md#project-sources), and the 85/90 semantic-isolation experiment as [S19](research-sources.md#project-sources). The detailed three-region behavior is documented in [battery threshold semantics](battery-threshold-semantics.md), while raw and representative live captures are retained in [validation](validation.md).

The important distinction is now:

```text
transport semantics    -> PMC2 commands and EC fields
behavioral semantics   -> what T1 and T2 do on the live machine
persistence semantics  -> which reset/power-loss classes retain or clear state
```

These layers are documented separately because a working setter does not by itself establish the meaning of a threshold, and a normal reboot does not establish deep-power-loss persistence.

## Three-region policy

The 85/90 experiment establishes the high-level policy on the investigated unit:

```text
                    T1                         T2
                     |                          |
        CHARGE       |          HOLD            |     ACTIVE DISCHARGE
---------------------+--------------------------+------------------------
        SOC < T1     |      T1 <= SOC <= T2     |       SOC > T2

        Charging            Not charging              Discharging
        battery energy ↑    battery held              battery energy ↓
```

The evidence supports the following behavioral meanings:

- **T1 is the lower charge/hold boundary.** Below T1, charging is permitted. Around the T1 region, charging is stopped and the machine settles into hold.
- **T2 is the upper boundary of an active-discharge region.** Above T2, the EC selects a state in which the battery supplies net stored energy even with AC continuously online.
- Returning toward the T2 region releases the sustained high-power discharge and returns the machine to hold.

This resolves the earlier open question about the behavioral purpose of T2. It does **not** establish identical behavior for every possible valid threshold pair, exact fractional comparator timing, or the exact electrical implementation inside the charger/power path.

## Linux power-supply devices and units

The recorded Linux devices are:

```text
Battery:    /sys/class/power_supply/LCBT
AC adapter: /sys/class/power_supply/ACAD
```

The retained battery model string is:

```text
588974-3S-G-A0
```

Useful observed attributes include `capacity`, `status`, `voltage_now`, `power_now` and `energy_now`. In the recorded environment:

- `capacity` is integer percent;
- `voltage_now` is µV;
- `power_now` is µW;
- `energy_now` is µWh;
- `current_now` was absent from the inspected `LCBT` tree.

The generic Linux threshold attributes were also absent:

```text
charge_control_start_threshold
charge_control_end_threshold
charge_behaviour
```

The firmware feature nevertheless worked through PMC2. Absence of the generic sysfs ABI therefore means only that this machine did not expose the feature through that standard Linux interface in the recorded environment.

## EC state and firmware landmarks

Static reverse engineering of the exact P916F IT5571 firmware identified:

| EC XRAM | Role | Evidence |
|---|---|---|
| `XRAM[0x0D01].bit4` | Enable/state bit | Static-confirmed; live-correlated through getter |
| `XRAM[0x0D13]` | Threshold 1 / T1 | Static-confirmed plus live set/readback |
| `XRAM[0x0D14]` | Threshold 2 / T2 | Static-confirmed plus live set/readback |
| `XRAM[0x0394]` | SOC input consumed by charge-control logic | Static-confirmed plus host-MMIO corroboration |

Relevant logical EC code locations are:

```text
CODE:0xED60...   enable/status handler family
CODE:0xED7A      sets XRAM[0x0D01].bit4
CODE:0xED8E      checks XRAM[0x0D01].bit4
CODE:0xEDBA      validates and writes XRAM[0x0D13]
CODE:0xEDDF      validates and writes XRAM[0x0D14]
CODE:0xF508      disable/reset path
CODE:0xF526      reads XRAM[0x0D13]
CODE:0xF621      reads XRAM[0x0D14]
CODE:0xC063      main SOC/threshold decision logic
CODE:0xF4D8      recorded PMC2 parser landmark
CODE:0xE65D      recorded PMC2 data-out helper landmark
```

These are logical EC `CODE:` addresses. The EC image is banked; these values are not automatically raw-ROM or carve-file offsets.

## Inclusive threshold range

Both setters contain the same 8051 range-check pattern. In simplified form:

```asm
MOV  A,value
CLR  C
SUBB A,#0
JC   invalid

MOV  A,value
SETB C
SUBB A,#0x64
JNC invalid
```

Because the second comparison sets carry before `SUBB`, decimal 100 remains accepted while 101 is rejected. The stored numeric range is therefore:

```text
0 .. 100 inclusive
0x00 .. 0x64 as a numeric byte
```

Examples:

```text
80 decimal  = 0x50
85 decimal  = 0x55
90 decimal  = 0x5A
100 decimal = 0x64
```

This establishes the accepted field range. It does not validate every possible pair as a safe or equivalent policy.

## PMC2 host protocol

The live ITE PMC2 interface is:

```text
DATA            = I/O 0x68
COMMAND/STATUS  = I/O 0x6C
OBF             = status bit 0
IBF             = status bit 1
```

The recovered command family is:

| Operation | Command to I/O `0x6C` | Data/subcommand to I/O `0x68` | Validation |
|---|---:|---:|---|
| Disable/reset | `0xF1` | `0x10` | Static-confirmed only; not live-tested |
| Enable | `0xF1` | `0x11` | Live-confirmed |
| Read state | `0xF1` | `0x12` | Live-confirmed |
| Read T1 | `0xF1` | `0x13` | Live-confirmed |
| Read T2 | `0xF1` | `0x14` | Live-confirmed |
| Set T1 | `0xF2` | threshold byte | Live-confirmed at 80 and 85 |
| Set T2 | `0xF3` | threshold byte | Live-confirmed at 100 and 90 |

Historical raw labels such as `F2 80` and `F3 100` use decimal threshold values. The actual wire pairs for the earlier policy are `0xF2 0x50` and `0xF3 0x64`; the 85/90 policy uses `0xF2 0x55` and `0xF3 0x5A`.

The successful transaction flow was:

1. Wait until PMC2 `IBF` is clear.
2. Write command byte to command/status I/O `0x6C`.
3. Wait for `IBF` to clear again.
4. Write subcommand or data byte to I/O `0x68`.
5. For a returning command, wait for `OBF` and read data from I/O `0x68`.

The complete production contract remains incomplete: bounded timeouts, stale-output handling, response framing for every command, concurrent ownership with platform firmware/software, and error behavior are not established.

## Earlier 80/100 validation

The first behaviorally validated configuration was:

```text
enabled = 1
T1      = 80
T2      = 100
```

The initial getter state before mutation was:

```text
F1 12 enabled/state : 0x00 (0)
F1 13 threshold #1 : 0x00 (0%)
F1 14 threshold #2 : 0x00 (0%)
```

Threshold writes while still disabled returned coherent readback:

```text
Before: enabled=0
F2 80 response  : 80
F3 100 response : 100
After: enabled  = 0
0D13            = 80%
0D14            = 100%
```

After enable:

```text
Before enable: state=0, T1=80%, T2=100%
Sending F1 11 (ENABLE)...
After enable : state=1, T1=80%, T2=100%
```

With AC connected, the policy produced repeated settled samples above the T1 region:

```text
20s  status=Not charging  cap=89%  power_now=0
22s  status=Not charging  cap=89%  power_now=0
26s  status=Not charging  cap=89%  power_now=0
28s  status=Not charging  cap=89%  power_now=0
```

After intentional discharge below the boundary:

```text
displayed 77–78% -> charging continued
around displayed 79% / slightly above -> charging stopped
```

The configuration survived a normal Linux reboot:

```text
state = 1
T1    = 80
T2    = 100
```

At the time, this proved T1-like cap behavior but could not isolate T2 because `SOC > 100` is not a normally reachable state.

## 85/90 semantic-isolation experiment

The later experiment deliberately made all three policy regions reachable:

```text
Enabled = 1
T1      = 85% (0x55)
T2      = 90% (0x5A)
```

Immediate PMC2 readback confirmed the programmed state before behavioral interpretation.

### Below T1: charging

With AC connected below T1, the machine charged normally:

```text
12:30:38  AC=1  SOC=82%  status=Charging  power=37852000
12:30:43  AC=1  SOC=82%  status=Charging  power=43811000
12:30:48  AC=1  SOC=82%  status=Charging  power=43752000
...
12:31:38  AC=1  SOC=83%  status=Charging
...
12:32:43  AC=1  SOC=84%  status=Charging
```

### T1 boundary: hold

The transition occurred while Linux still displayed 84%:

```text
12:33:08  AC=1  SOC=84%  status=Charging      power=42486000
12:33:13  AC=1  SOC=84%  status=Discharging   power=23000
12:33:18  AC=1  SOC=84%  status=Not charging  power=0
12:33:23  AC=1  SOC=84%  status=Not charging  power=0
```

The subsequent state was predominantly `Not charging`, with reported energy approximately stable. The transient ~23 mW discharge sample is several orders of magnitude smaller than the sustained multi-watt T2 discharge and must not be treated as the same mode.

Linux showed 84% around a programmed T1=85 boundary. The earlier T1=80 test likewise stopped around displayed 79–80%. The exact one-percentage-point relationship is not assigned a cause: Linux `capacity` and EC SOC decision timing are not established to be synchronous.

### Above T2: active discharge

With AC continuously online and SOC above 90%, the machine showed sustained battery discharge:

```text
14:56:53  AC=1  SOC=94%  status=Discharging  power=8867000  energy=68116000
14:56:58  AC=1  SOC=94%  status=Discharging  power=9055000  energy=68104000
14:57:03  AC=1  SOC=94%  status=Discharging  power=8234000  energy=68092000
...
15:08:09  AC=1  SOC=91%  status=Discharging  power=20879000
15:08:14  AC=1  SOC=91%  status=Discharging  power=21982000
...
15:11:35  AC=1  SOC=91%  status=Discharging  power=10674000
```

The battery therefore supplied net stored energy while the AC adapter remained online.

### T2 boundary: active discharge released

Substantial discharge continued for part of the interval while Linux displayed 90%:

```text
15:11:40  SOC=90%  Discharging  15.788 W
15:11:45  SOC=90%  Discharging  20.515 W
15:12:00  SOC=90%  Discharging  25.383 W
15:12:10  SOC=90%  Discharging  31.166 W
15:12:30  SOC=90%  Discharging  30.005 W
```

It then fell sharply:

```text
15:12:40  SOC=90%  Discharging  0.304 W
15:12:45  SOC=90%  Discharging  0.340 W
15:12:50  SOC=90%  Discharging  0.457 W
...
15:14:55  SOC=90%  Discharging  0.046 W
```

and finally settled to hold:

```text
15:15:50  AC=1  SOC=90%  status=Not charging  power=0
15:15:55  AC=1  SOC=90%  status=Not charging  power=0
```

This is direct behavioral evidence that sustained active discharge is released as the battery returns to the T2 region.

## Energy evidence for active discharge

The T2 trace moved from approximately:

```text
68.116 Wh
```

to:

```text
64.972 Wh
```

for a reported decrease of:

```text
3.144 Wh
```

over approximately 18 minutes 57 seconds. That endpoint calculation corresponds to roughly 9.95 W average net battery contribution over the interval.

The calculated average is not a calibrated electrical measurement: workload changed, `energy_now` is quantized, and wall-side adapter input was not measured. The key evidentiary point is independent of that exact average: several watt-hours of stored battery energy disappeared while AC remained online, so the T2 state is genuine battery discharge rather than only a Linux status-label change or simple charge inhibition.

## Final T1/T2 semantics

The combined static and live evidence supports:

```text
if charge_limit_disabled:
    normal platform charging policy
else:
    if SOC < T1:
        charging permitted
    elif T1 <= SOC <= T2:
        charging held / Not charging
    else:  # SOC > T2
        active battery discharge despite AC online
```

This pseudocode is a behavioral model. It does not claim exact instruction-for-instruction equivalence with the EC implementation or exact comparator inclusivity at fractional SOC values.

### Why 80/100 looks like a conventional cap

For `T1=80`, `T2=100`, the active-discharge condition `SOC > 100` is effectively unreachable under ordinary valid SOC. The practical policy therefore becomes charge-below-T1 and hold-above-T1, explaining the earlier approximately-80% cap behavior.

That explanation does not promote arbitrary `T1=N,T2=100` pairs to validated configurations.

## Static charger-control correlation

The charge-control decision routine around `CODE:0xC063` consumes `XRAM[0x0394]`, `XRAM[0x0D13]` and `XRAM[0x0D14]`. Earlier static reverse engineering also identified a downstream charger transaction in the secondary branch that modifies bit 5 of charger register `0x12`.

The register contract is compatible with the TI BQ25700A/BQ25710 family and an `EN_LEARN`-like discharge-oriented control function. Exact charger silicon identity is not established. The repository therefore separates:

```text
T2 active-discharge behavior
    -> Live-confirmed

exact BQ25700A/BQ25710 / EN_LEARN naming
    -> static-supported family-level inference
```

The live T2 conclusion does not depend on the candidate charger-family identification.

## Reversed-threshold special case

Static control flow contains a special path when:

```text
T1 > T2
```

that takes a normal-current bypass branch. This has not been live-tested and is not a recommended configuration. Behavior for `T1 == T2` is also unresolved.

## Persistence

The battery-limit state survived a normal reboot in the earlier validation:

```text
state = 1
T1    = 80
T2    = 100
```

A later OS-transition test adds another retained-state case: after the `85/90` policy had been configured under Linux, the machine was booted into Windows and queried directly through the same PMC2 getter family. Windows-side readback returned `Enabled=1`, `T1=85 (0x55)`, `T2=90 (0x5A)`. This is direct live confirmation that the PMC2-visible fields persisted across the tested Linux-to-Windows transition, not merely behavioral inference from charging status. It rules out a Linux-userspace-only persistence mechanism for this transition, but it does not establish the exact EC storage medium or behavior for every reset/power-loss class.

Two additional owner-reported tests further narrow the persistence boundary. With the `85/90` policy programmed, a normal shutdown with AC still connected retained `1/85/90`; a separate normal shutdown followed by AC removal while the internal battery remained connected also retained `1/85/90` on the next PMC2 readback. These runs were reported without verbatim console capture, so they establish the tested outcomes but do not provide independently inspectable raw transcripts. In the tested cases, AC removal alone was therefore insufficient to clear the state while the internal battery remained connected.

A later battery-depletion event caused complete system power loss. On the next powered session:

```text
Enabled : 0 (OFF)
T1      : 0% (0x00)
T2      : 0% (0x00)

AC online: 1
SOC      : 89
Status   : Charging
Power    : 21137000
Energy   : 64350000
Voltage  : 13431000
```

This is Live-confirmed clearing for that recorded event. It does not establish when the fields were cleared, whether the EC rail itself went fully unpowered, whether firmware initialization performed the clearing, whether `0xF1 0x10` executed, or whether every other power-loss/reset class behaves identically.

A persistent Linux implementation should therefore read and compare the EC state before relying on it. If the desired policy is absent, an idempotent restoration flow can set T1, set T2, verify readback, enable, and verify final state. If the state already matches, unnecessary EC writes should be avoided. Production use still requires a safe transaction-ownership and concurrency design.

## Charger-control working values

Static analysis also identified working words around:

```text
XRAM[0x0D65]/XRAM[0x0D66]
XRAM[0x0D67]/XRAM[0x0D68]
```

with source/default words around:

```text
XRAM[0x0D54..0x0D57]
```

Helpers around `CODE:0xC249` and `CODE:0xC27A` clear or copy these words depending on the selected control branch. A later worker stages transactions using command numbers `0x14` and `0x15`, which are consistent with conventional Smart Battery charger `ChargingCurrent` and `ChargingVoltage` naming. That exact semantic naming remains inferred until the complete bus transaction path and charger identity are independently established.

## Earlier high-load battery-assist observation

A separate five-minute `stress-ng --cpu 0` run under the 80/100 cap retained:

```text
start:
79%  Not charging  power=0         energy=63154000

end:
78%  Charging      power=28128000  energy=62661000
```

The stored-energy decrease was:

```text
63.154 Wh -> 62.661 Wh
Delta     = 0.493 Wh
```

The two timestamped snapshots were 313 seconds apart while the stress tool reported a 300-second configured run. Those durations are not interchangeable for precise power calculation. This older test supports battery-energy contribution under load but does not replace the stronger T2 experiment, which explicitly isolated the active-discharge region with AC online.

## Paths tested and rejected

| Alternative | Recorded result and boundary |
|---|---|
| Generic Linux threshold sysfs | `charge_control_start_threshold`, `charge_control_end_threshold` and `charge_behaviour` were absent in the observed battery device |
| Huawei-compatible WMI threshold API | Read-only GET `0x1103` returned failure/unsupported; corresponding SET `0x1003` was not attempted |
| Generic Uniwill/Tongfang offsets | `0x07B9` and `0x07D0` are comparative software constants, not established P916F control addresses |
| `INOU0000` / `ECRR` / `ECRW` | Not found in the examined ACPI tables |
| Dedicated I2EC base `0x380` | Rejected by the recorded `0xFF` candidate-read / valid-MMIO cross-check |
| ACPI `_BTP` | Battery trip-point notification, not the dedicated charge-limit subsystem |

## Disable/reset path

Static analysis maps command `0xF1` with data `0x10` to the reset/disable handler around `CODE:0xF508`. The handler statically clears:

```text
XRAM[0x0D01].bit4
XRAM[0x0D13]
XRAM[0x0D14]
```

This path remains **Static-confirmed only**. The post-depletion `0/0/0` observation is not evidence that this command ran. It must not be described as a live-tested rollback command or as a verified restoration of every factory charging parameter.

## Remaining boundaries

The following remain unestablished:

- exact behavior of every possible valid T1/T2 pair;
- behavior for `T1 == T2`;
- live behavior for `T1 > T2`;
- exact fractional/internal SOC at each comparator transition;
- exact Linux-visible percentage at the decision instant;
- exact hysteresis width beyond the observed policy regions;
- exact charger IC and electrical implementation of active discharge;
- complete wall-side power behavior;
- complete PMC2 timeout/error/concurrency contract;
- persistence under reset/power-loss classes other than the recorded normal reboot and battery-depletion event.

The behavioral meaning of T2 is **not** an open question anymore. It is live-confirmed as the upper boundary of the active-discharge region on the investigated P916F-STX.