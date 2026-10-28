# Battery threshold semantics and 85/90 live validation

## Scope

This page documents the behavioral meaning of the two programmable battery-control thresholds on the investigated MECHREVO Xingyao 14 / `P916F-STX`.

The source record identifies the following baseline:

```text
Platform:              P916F-STX
Embedded controller:   ITE IT5571, revision 0x07
System BIOS:           1.15
Linux test system:     CachyOS / Arch-family
Battery device:        /sys/class/power_supply/LCBT
AC adapter device:     /sys/class/power_supply/ACAD
```

The charge-limit feature is implemented by the EC and reached through the ITE PMC2 host interface. It is not exposed through the generic Linux `charge_control_start_threshold`, `charge_control_end_threshold` or `charge_behaviour` attributes in the recorded environment.

This page supplements the protocol description in [battery-charge-limit.md](battery-charge-limit.md), the earlier [live validation](validation.md), and the separate [battery-depletion power-loss observation](battery-limit-power-loss-observation.md).

## Result

The 85/90 experiment establishes a three-region policy:

```text
                    T1                         T2
                     |                          |
                     |                          |
        CHARGE       |          HOLD            |     ACTIVE DISCHARGE
---------------------+--------------------------+------------------------
        SOC < T1     |      T1 <= SOC <= T2     |       SOC > T2

        Charging            Not charging              Discharging
        battery energy ↑    battery held              battery energy ↓
```

The strongest supported interpretation is:

- **T1 is the lower charge/hold boundary.** Below T1, charging is permitted. At the T1 region, charging is stopped and the system enters the hold region.
- **T2 is the upper boundary of an active-discharge region.** Above T2, the EC selects a state in which the battery supplies net energy even while AC remains online.
- When SOC falls back into the T2 boundary region, the sustained high-power discharge is released and the machine settles into the hold state.

These are **Live-confirmed behavioral semantics** on the investigated unit. They do not establish the exact charger silicon, the exact electrical mechanism, or identical behavior for every possible threshold pair.

## EC fields and PMC2 protocol

Static reverse engineering identifies:

```text
XRAM[0x0D01].bit4   subsystem enable/state
XRAM[0x0D13]        threshold 1 / T1
XRAM[0x0D14]        threshold 2 / T2
XRAM[0x0394]        SOC value consumed by decision logic
```

Relevant EC code landmarks include:

```text
CODE:0xED7A   enable-state manipulation
CODE:0xED8E   enable-state query/test
CODE:0xEDBA   T1 validation/setter
CODE:0xEDDF   T2 validation/setter
CODE:0xF508   reset/disable path
CODE:0xF526   T1 getter
CODE:0xF621   T2 getter
CODE:0xC063   main SOC/threshold decision routine
```

The live PMC2 interface is:

```text
DATA            = I/O 0x68
COMMAND/STATUS  = I/O 0x6C
OBF             = status bit 0
IBF             = status bit 1
```

The recovered command family is:

| Operation | Command | Data / subcommand | Status |
|---|---:|---:|---|
| Disable/reset | `0xF1` | `0x10` | Static-confirmed only; not live-tested |
| Enable | `0xF1` | `0x11` | Live-confirmed |
| Read enabled state | `0xF1` | `0x12` | Live-confirmed |
| Read T1 | `0xF1` | `0x13` | Live-confirmed |
| Read T2 | `0xF1` | `0x14` | Live-confirmed |
| Set T1 | `0xF2` | numeric threshold byte | Live-confirmed |
| Set T2 | `0xF3` | numeric threshold byte | Live-confirmed |

Static range-check analysis establishes an accepted numeric range of decimal `0..100` inclusive. The encoded values are ordinary numeric bytes. For the 85/90 experiment:

```text
85 decimal = 0x55
90 decimal = 0x5A

T1 = 85: command 0xF2, data 0x55
T2 = 90: command 0xF3, data 0x5A
```

Immediate readback after programming returned:

```text
Enabled : 1
T1      : 85% (0x55)
T2      : 90% (0x5A)
```

This validates the programmed state before interpreting the subsequent charging behavior.

## T1: lower charge/hold boundary

### Charging below T1

The battery was first discharged below the configured T1 value. With AC disconnected, Linux reported:

```text
AC=0
SOC=82%
status=Discharging
```

After AC was connected at the same displayed SOC, the battery entered a high-power charging state:

```text
12:30:38  AC=1  SOC=82%  status=Charging  power=37852000
12:30:43  AC=1  SOC=82%  status=Charging  power=43811000
12:30:48  AC=1  SOC=82%  status=Charging  power=43752000
...
12:31:38  AC=1  SOC=83%  status=Charging
...
12:32:43  AC=1  SOC=84%  status=Charging
```

This directly establishes that the tested policy permits charging below T1.

### Transition near T1

The decisive transition occurred while Linux still displayed 84%:

```text
12:33:08  AC=1  SOC=84%  status=Charging      power=42486000
12:33:13  AC=1  SOC=84%  status=Discharging   power=23000
12:33:18  AC=1  SOC=84%  status=Not charging  power=0
12:33:23  AC=1  SOC=84%  status=Not charging  power=0
```

The machine then remained near the displayed 84% region with `Not charging`, `power_now=0` and essentially stable reported stored energy.

The brief approximately-23 mW `Discharging` sample at the transition is not equivalent to the sustained multi-watt active-discharge mode observed above T2.

T1 was programmed to 85%, while Linux displayed 84% around the transition. That one-percentage-point difference does not invalidate the threshold. Linux's integer `capacity` and the EC's decision input are sampled through different interfaces and are not established to update synchronously. Rounding, update cadence, fuel-gauge sampling and EC polling cadence remain possible explanations; none is selected as proven.

The earlier `T1=80` experiment likewise stopped charging around displayed 79–80%. The defensible conclusion is therefore:

> **T1 defines the lower charge/hold boundary. Linux `capacity` provides only an approximate externally visible indication of the EC comparator boundary.**

## T2: upper active-discharge boundary

### Sustained discharge above T2

The battery was charged above 90%, then the validated `85/90` state was restored while AC remained connected. The recorded trace began:

```text
14:56:53  AC=1  SOC=94%  status=Discharging  power=8867000  energy=68116000
14:56:58  AC=1  SOC=94%  status=Discharging  power=9055000  energy=68104000
14:57:03  AC=1  SOC=94%  status=Discharging  power=8234000  energy=68092000
...
```

The same behavior continued through displayed 93%, 92% and 91%. Representative 91% samples were:

```text
15:08:09  AC=1  SOC=91%  status=Discharging  power=20879000
15:08:14  AC=1  SOC=91%  status=Discharging  power=21982000
...
15:09:55  AC=1  SOC=91%  status=Discharging  power=7906000
...
15:11:35  AC=1  SOC=91%  status=Discharging  power=10674000
```

AC remained online while battery energy decreased. This is not adequately explained by a status-label transition: the battery was delivering net stored energy.

Therefore:

> **SOC above T2 enters an active-discharge state on the tested 85/90 policy.**

### Release at the T2 region

Linux had already begun displaying 90% while substantial discharge continued:

```text
15:11:40  SOC=90%  Discharging  15.788 W
15:11:45  SOC=90%  Discharging  20.515 W
15:12:00  SOC=90%  Discharging  25.383 W
15:12:10  SOC=90%  Discharging  31.166 W
15:12:30  SOC=90%  Discharging  30.005 W
```

The discharge then fell sharply:

```text
15:12:40  SOC=90%  Discharging  0.304 W
15:12:45  SOC=90%  Discharging  0.340 W
15:12:50  SOC=90%  Discharging  0.457 W
...
15:14:55  SOC=90%  Discharging  0.046 W
```

Finally the battery entered a stable hold state:

```text
15:15:50  AC=1  SOC=90%  status=Not charging  power=0
15:15:55  AC=1  SOC=90%  status=Not charging  power=0
```

The machine then remained predominantly at:

```text
AC=1
SOC=90%
status=Not charging
power_now=0
energy_now=64972000
```

The important transition is behavioral rather than tied to an exact Linux integer percentage:

```text
SOC above T2
    -> sustained active battery discharge

SOC returns to the T2 region
    -> sustained discharge is released
    -> hold / Not charging
```

The Linux-visible 90% display during part of the transition does not identify the exact instant at which the EC's internal comparator input crossed its boundary.

## Energy evidence for active discharge

The active-discharge trace begins near:

```text
14:56:53
energy_now = 68.116 Wh
```

The stable T2-boundary hold region was recorded near:

```text
energy_now = 64.972 Wh
```

The decrease was:

```text
68.116 Wh - 64.972 Wh = 3.144 Wh
```

across approximately 18 minutes 57 seconds. Treating those endpoints as the interval gives an approximate average net battery contribution of about 9.95 W.

That average is not a calibrated electrical measurement. Workload changed during the trace, `energy_now` is quantized, and wall-side adapter power was not measured. Its evidentiary purpose is narrower and stronger: **the battery lost several watt-hours of stored energy while AC remained continuously online**. T2 behavior therefore cannot be reduced to merely disabling charging.

## Why 80/100 behaves like a conventional charge cap

The earlier known-good policy was:

```text
T1 = 80
T2 = 100
```

For an ordinary valid battery SOC, `SOC > 100` is not a reachable operating region. The active-discharge branch is therefore effectively excluded during normal operation. The practical model becomes:

```text
SOC below T1
    -> charge

SOC at or above the T1 region, through normal full SOC
    -> hold / Not charging

active-discharge region above T2=100
    -> effectively unreachable
```

This explains why the original 80/100 experiment behaved like an approximately-80% conventional charge cap and why that experiment could not isolate the behavioral meaning of T2.

This explanation does **not** validate a generic rule that every arbitrary `T1=N, T2=100` pair has identical behavior. Only the tested policies and their observed regions are claimed.

## Static correlation to charger control

The main EC decision routine around `CODE:0xC063` consumes the SOC field and both thresholds. Earlier static reverse engineering also identified a downstream charger transaction in the secondary branch that modifies bit 5 of charger register `0x12`.

The register contract is compatible with the TI BQ25700A/BQ25710 family and an `EN_LEARN`-like discharge-oriented function. Exact charger silicon identity has not been established, so the repository separates the claims:

```text
High-level T2 behavior:
    active discharge above T2
    -> Live-confirmed

Exact charger silicon / register naming:
    BQ25700A/BQ25710-compatible EN_LEARN-like interpretation
    -> static-supported, family-level inference
```

The live behavioral conclusion does not depend on accepting the candidate charger-family interpretation.

## Reversed-threshold static special case

Recovered static control flow contains a special path when:

```text
T1 > T2
```

that takes a normal-current bypass branch. This condition has not been live-tested and is not a recommended configuration. Behavior for `T1 == T2` is also unresolved.

## Persistence and Linux implementation

Two persistence classes are now observed:

```text
ordinary reboot
    -> programmed charge-limit state retained

recorded complete battery depletion
    -> next powered session observed Enabled=0 / T1=0 / T2=0
```

The depletion result establishes the observed behavior but not the electrical reset mechanism. It does not prove that every G3 state, battery disconnect, CMOS/RTC-power removal, explicit EC reset, firmware update or other power-loss class clears the state.

A persistent Linux integration should therefore treat the configuration as **state to verify and restore**, not as a one-time permanent write. A robust idempotent flow is:

```text
boot
  |
  v
read Enabled / T1 / T2
  |
  +-- already matches desired policy --> no writes
  |
  `-- differs or has reset -----------> set T1
                                         set T2
                                         verify readback
                                         enable
                                         verify final state
```

The known `0xF1 0x10` reset path is not required for normal restoration and remains static-confirmed only.

## Evidence boundaries

The new tests establish the high-level three-region model, but they do not establish:

- identical behavior for every possible valid T1/T2 pair;
- behavior when `T1 == T2`;
- live behavior for reversed `T1 > T2` thresholds;
- exact fractional SOC or exact Linux-visible percentage at each EC comparator transition;
- the EC/fuel-gauge sampling and update cadence;
- the exact charger IC model;
- the exact electrical mechanism that creates active discharge;
- wall-side adapter power during the trace;
- the complete response/error/timeout and concurrency contract for PMC2 operations;
- persistence behavior under reset classes other than the specifically observed normal reboot and battery-depletion event.

The behavioral meaning of T2 is no longer in this unresolved list.

## Summary

For the investigated P916F-STX, the supported charge-control model is:

```text
SOC < T1
    -> Charging

T1 <= SOC <= T2
    -> Hold
    -> Not charging
    -> no intentionally sustained multi-watt battery discharge observed

SOC > T2
    -> Active discharge
    -> AC remains online
    -> battery supplies net stored energy
    -> SOC is driven back toward the T2 region
```

The 85/90 experiment directly demonstrated all three behavioral regions. The 80/100 configuration is now understood as a practical policy in which T1 supplies the charge cap and T2=100 leaves the active-discharge region outside the normally reachable SOC range.