# Live validation report

This report preserves recorded P916F-STX EC and battery experiments. Historical baseline experiments are registered as [S1](research-sources.md#project-sources); the later battery-depletion reset capture is [S18](research-sources.md#project-sources); and the 85/90 threshold-semantics experiment is [S19](research-sources.md#project-sources). The behavioral counterpart to this report is [battery-charge-limit.md](battery-charge-limit.md), with the isolated three-region interpretation in [battery-threshold-semantics.md](battery-threshold-semantics.md).

Historical output is preserved verbatim where quoted. Threshold labels such as `F2 80` and `F3 100` use decimal threshold values: the corresponding wire data bytes are `0x50` and `0x64`. Linux battery units in the retained captures are `capacity` in percent, `voltage_now` in µV, `power_now` in µW and `energy_now` in µWh. Blank output is not interpreted as zero.

## 1. Live ITE Super-I/O identity

Observed configuration-space result:

```text
CFG 0x2E: chip=0xFFFF rev=0xFF
CFG 0x4E: chip=0x5571 rev=0x07
```

The valid identity response was obtained at I/O `0x4E`.

### PMC2 logical device

Logical device `0x12` returned:

```text
PMC2 active = 0x01
I/O #0      = 0x0068
I/O #1      = 0x006C
I/O #2      = 0x0000
IRQ         = 0x00
```

The working transport was therefore:

```text
PMC2 DATA            = 0x68
PMC2 COMMAND/STATUS  = 0x6C
```

The discovery was performed independently of the EC disassembly and subsequently correlated with the working command path.

## 2. Rejected candidate I2EC path

A candidate dedicated I2EC interface at base `0x380` was evaluated by querying state rather than writing threshold fields:

```text
I2EC control : EC[200D] = 0xFF
Battery %    : EC[0394] = 255
Threshold #1 : EC[0D13] = 255
Threshold #2 : EC[0D14] = 255
MMIO crosschk : FEEC2394 = 92
Cross-check   : MISMATCH
```

The known shared-memory SOC address simultaneously returned:

```text
0xFEEC2394 = 92
```

The candidate path was therefore rejected for the tested configuration.

## 3. PMC transaction behavior

The successful transactions used:

```text
status bit0 = OBF
status bit1 = IBF
```

The host waited for `IBF=0`, wrote the command byte to command/status I/O `0x6C`, waited again, and wrote the subcommand/data byte to data I/O `0x68`. When a response was expected, it waited for `OBF=1` and read `0x68`.

Commands were derived from static firmware analysis before the experiments. No brute-force command scan is part of this validation record. Bounded timeouts, stale-output handling, concurrent ownership and the complete error contract remain unestablished.

## 4. Initial charge-limit state

Before any setter was exercised in the baseline experiment:

```text
F1 12 enabled/state : 0x00 (0)
F1 13 threshold #1 : 0x00 (0%)
F1 14 threshold #2 : 0x00 (0%)
```

Baseline state:

```text
state = 0
T1    = 0
T2    = 0
```

The getter sequence returned coherent disabled/default values before mutation.

## 5. Threshold write/readback while disabled

The original transaction labels were:

```text
F2 80
F3 100
```

These labels mean T1=80 decimal and T2=100 decimal; their data bytes are `0x50` and `0x64`.

```text
Before: enabled=0
F2 80 response  : 80
F3 100 response : 100
After: enabled  = 0
0D13            = 80%
0D14            = 100%
```

This established that:

- `0xF2` reaches threshold field `XRAM[0x0D13]`;
- `0xF3` reaches threshold field `XRAM[0x0D14]`;
- getters read the written values back coherently;
- threshold programming does not implicitly enable the subsystem;
- the tested SET transactions returned response bytes corresponding to accepted values.

## 6. Enable transition

Recorded operation:

```text
F1 11
```

Observed state transition:

```text
Before enable: state=0, T1=80%, T2=100%
Sending F1 11 (ENABLE)...
After enable : state=1, T1=80%, T2=100%
```

The specified operation is `0xF1 0x11`. This is live confirmation of enable behavior while preserving the programmed threshold values.

## 7. Original 80/100 charge-stop behavior

The battery was approximately 89%. The adapter was physically disconnected for the first part of the trace and connected during the sequence. Initial `Discharging` samples therefore do not test AC-connected limiting.

```text
00s  status=Discharging  cap= 89% power_now=3906000 voltage_now=13003000
02s  status=Discharging  cap= 89% power_now=4609000 voltage_now=12984000
04s  status=Discharging  cap= 89% power_now=3648000 voltage_now=13002000
06s  status=Discharging  cap= 89% power_now=3612000 voltage_now=13003000
08s  status=Discharging  cap= 89% power_now=3953000 voltage_now=13000000
10s  status=Discharging  cap= 89% power_now=3601000 voltage_now=13004000
12s  status=Discharging  cap= 89% power_now=4140000 voltage_now=12999000
14s  status=Discharging  cap= 89% power_now=4140000 voltage_now=12999000
16s  status=Charging     cap= 89% power_now=2263000 voltage_now=13052000
18s  status=Discharging  cap= 89% power_now=246000  voltage_now=13053000
20s  status=Not charging cap= 89% power_now=0       voltage_now=13054000
22s  status=Not charging cap= 89% power_now=0       voltage_now=13054000
24s  status=Discharging  cap= 89% power_now=903000  voltage_now=13042000
26s  status=Not charging cap= 89% power_now=0       voltage_now=13054000
28s  status=Not charging cap= 89% power_now=0       voltage_now=13054000
```

The repeated settled observation was:

```text
SOC well above 80%
AC connected
status = Not charging
power_now = 0
```

After intentional discharge below the boundary, the recorded observation with AC connected was:

```text
displayed 77–78% -> charging continued
around displayed 79% / slightly above -> charging stopped
```

Representative capped sample:

```text
79% - Not charging
```

This established the practical T1 boundary for the 80/100 policy but did not isolate the role of T2 because a normal battery does not reach `SOC > 100`.

## 8. AC-online capped snapshot

```text
=== BATTERY ===
capacity:   79%
status:     Not charging
power_now:  0
energy_now: 63154000
charge_now:
voltage:    12764000

=== AC ===
ACAD: online=1
```

The snapshot establishes AC presence, `Not charging`, and reported battery `power_now=0` at that instant. It does not measure wall-side adapter power or every instantaneous battery current.

## 9. Normal-reboot persistence

After a normal Linux reboot without reapplying the configuration:

```text
state = 1
T1    = 80
T2    = 100
```

Recorded conclusion:

```text
configuration persists across a normal reboot
```

This is evidence for normal-reboot persistence only.

## 10. Battery-depletion full-power-loss observation

A later observation was recorded after the machine had fully lost system power because the battery was depleted. On the next powered session, the battery-limit query and current battery telemetry returned:

```text
=== P916F-STX Battery Limit ===
Enabled : 0 (OFF)
T1      : 0% (0x00)
T2      : 0% (0x00)

=== Current battery state ===
AC online: 1
SOC      : 89
Status   : Charging
Power    : 21137000
Energy   : 64350000
Voltage  : 13431000
```

The earlier `1 / 80 / 100` values were therefore not present after this recorded depletion event. This is **Live-confirmed** clearing for that event.

The capture cannot establish when or why clearing occurred. It does not distinguish depletion/brownout from an EC reset, firmware initialization on the next power-on, or another event-associated transition. It also does not establish behavior for every G3 transition, physical battery disconnect, CMOS/RTC-power removal, explicit EC reset, firmware update or other reset class.

The operational consequence is bounded: after battery depletion causes complete system power loss, the charge-limit state must not be assumed to remain programmed. See [battery-limit-power-loss-observation.md](battery-limit-power-loss-observation.md).

## 11. Earlier high-load battery-energy test

A separate baseline experiment applied approximately five minutes of all-CPU load while AC was connected.

### Before load

```text
20:47:14  cap=79%  status=Not charging  power=0  energy=63154000
```

### CPU stress execution

```text
stress-ng: info:  [21163] setting to a 5 mins run per stressor
stress-ng: info:  [21163] dispatching hogs: 20 cpu
stress-ng: info:  [21163] skipped: 0
stress-ng: info:  [21163] passed: 20: cpu (20)
stress-ng: info:  [21163] failed: 0
stress-ng: info:  [21163] metrics untrustworthy: 0
stress-ng: info:  [21163] successful run completed in 5 mins
```

### After load

```text
20:52:27  cap=78%  status=Charging  power=28128000  energy=62661000
```

The reported stored-energy decrease was:

```text
63.154 Wh -> 62.661 Wh
Delta     = 0.493 Wh
```

Calculation: `(63154000 - 62661000) µWh = 493000 µWh = 0.493 Wh`. The timestamped snapshots span 313 seconds, while the stress tool reports a 300-second configured run. These intervals are not interchangeable for precise average-power calculation.

This result supports net battery-energy contribution during the observation and is consistent with battery-assist behavior. It did not isolate T2 and is therefore distinct from the later 85/90 active-discharge experiment.

## 12. 85/90 programming and readback

To isolate both threshold semantics, the subsystem was later programmed as:

```text
Enabled = 1
T1      = 85%
T2      = 90%
```

The numeric wire data were:

```text
85 decimal = 0x55
90 decimal = 0x5A

T1: command 0xF2, data 0x55
T2: command 0xF3, data 0x5A
```

Immediate readback confirmed:

```text
Enabled : 1
T1      : 85% (0x55)
T2      : 90% (0x5A)
```

The policy therefore had three reachable regions before the behavioral trace was interpreted:

```text
SOC < 85%       candidate charge region
85%..90%        candidate hold region
SOC > 90%       candidate T2-specific region
```

## 13. T1 live validation

### Charging below T1

At displayed 82%, AC was initially disconnected:

```text
AC=0
SOC=82%
status=Discharging
```

When AC was connected at the same displayed SOC, the battery entered high-power charging:

```text
12:30:38  AC=1  SOC=82%  status=Charging  power=37852000
12:30:43  AC=1  SOC=82%  status=Charging  power=43811000
12:30:48  AC=1  SOC=82%  status=Charging  power=43752000
...
12:31:38  AC=1  SOC=83%  status=Charging
...
12:32:43  AC=1  SOC=84%  status=Charging
```

This directly establishes charging below T1 for the tested policy.

### Charge-stop transition near T1

The decisive transition occurred while Linux still displayed 84%:

```text
12:33:08  AC=1  SOC=84%  status=Charging      power=42486000
12:33:13  AC=1  SOC=84%  status=Discharging   power=23000
12:33:18  AC=1  SOC=84%  status=Not charging  power=0
12:33:23  AC=1  SOC=84%  status=Not charging  power=0
```

The subsequent state remained near displayed 84%, predominantly `Not charging`, with reported energy approximately stable.

The ~23 mW transient `Discharging` sample is not equivalent to the later multi-watt T2 active-discharge state.

T1 was programmed to 85% while Linux displayed 84% around the transition. This does not invalidate the threshold. The Linux `capacity` value and EC decision input are different sampled observations and are not established to update synchronously. The earlier T1=80 experiment likewise stopped near displayed 79–80%.

Supported conclusion:

```text
T1 = lower charge/hold boundary
Linux integer capacity = approximate external indication of that boundary
```

## 14. T2 live validation: active discharge above T2

The battery was charged above T2 and the validated 85/90 state was restored while AC remained online.

At the beginning of the recorded trace:

```text
14:56:53  AC=1  SOC=94%  status=Discharging  power=8867000  energy=68116000
14:56:58  AC=1  SOC=94%  status=Discharging  power=9055000  energy=68104000
14:57:03  AC=1  SOC=94%  status=Discharging  power=8234000  energy=68092000
...
```

The behavior continued through 93%, 92% and 91%. Representative 91% samples were:

```text
15:08:09  AC=1  SOC=91%  status=Discharging  power=20879000
15:08:14  AC=1  SOC=91%  status=Discharging  power=21982000
...
15:09:55  AC=1  SOC=91%  status=Discharging  power=7906000
...
15:11:35  AC=1  SOC=91%  status=Discharging  power=10674000
```

AC remained online while reported battery energy decreased. This is direct live evidence of net battery discharge above T2.

Supported conclusion:

```text
SOC > T2
    -> active battery discharge with AC online
```

## 15. T2 boundary release

High-power discharge continued during part of the interval while Linux displayed exactly 90%:

```text
15:11:40  SOC=90%  Discharging  15.788 W
15:11:45  SOC=90%  Discharging  20.515 W
15:12:00  SOC=90%  Discharging  25.383 W
15:12:10  SOC=90%  Discharging  31.166 W
15:12:30  SOC=90%  Discharging  30.005 W
```

Discharge power then fell sharply:

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

It then remained predominantly at:

```text
AC=1
SOC=90%
status=Not charging
power_now=0
energy_now=64972000
```

This establishes release of the sustained active-discharge state as the battery returns to the T2 boundary region. The Linux-visible 90% value during the transition does not identify the exact internal EC comparator instant.

## 16. Energy evidence for forced discharge

The T2 trace began near:

```text
14:56:53
energy_now = 68.116 Wh
```

By the stable T2 hold region:

```text
energy_now = 64.972 Wh
```

The reported decrease was:

```text
68.116 Wh - 64.972 Wh = 3.144 Wh
```

across approximately 18 minutes 57 seconds. Using those endpoints gives an approximate average net battery contribution of roughly 9.95 W.

This average is not presented as a calibrated electrical measurement: workload varied, `energy_now` is quantized and no wall-side adapter power was recorded. The qualitative conclusion is much stronger than the exact average: the battery lost several watt-hours of stored energy while AC remained continuously online. Therefore the T2 state is not merely `charging disabled`; it is genuine battery discharge.

## 17. Combined behavioral model

The live evidence now supports:

```text
SOC < T1
    -> Charging

T1 <= SOC <= T2
    -> Hold
    -> Not charging
    -> no intentionally sustained multi-watt discharge observed

SOC > T2
    -> Active discharge
    -> AC remains online
    -> battery supplies net stored energy
    -> SOC is driven back toward the T2 region
```

The 85/90 experiment directly demonstrated all three regions.

The earlier 80/100 behavior is now explained by the fact that `SOC > 100` is effectively unreachable during normal operation. With T2=100, the active-discharge region is effectively excluded, leaving T1 as the practical charge-cap boundary.

This explanation does not validate every arbitrary `T1=N,T2=100` configuration.

## 18. Static charger correlation

The high-level T2 behavior no longer depends on a charger-model inference because it has been independently observed live. Static reverse engineering nevertheless provides a lower-level correlation: the secondary branch modifies bit 5 of charger register `0x12`.

That register contract is compatible with a TI BQ25700A/BQ25710-family `EN_LEARN`-like discharge-oriented function. Exact charger silicon identity has not been established.

Evidence separation:

```text
T2 active-discharge behavior
    -> Live-confirmed

exact charger family / EN_LEARN register naming
    -> static-supported family-level inference
```

## 19. Validation boundaries

The following statements remain unestablished:

```text
all valid T1/T2 pairs behave identically
T1=N,T2=100 always creates an N% cap
behavior when T1 == T2
live behavior when T1 > T2
exact fractional/internal SOC at each comparator transition
exact Linux-visible percentage at the comparator instant
exact hysteresis width beyond the observed policy regions
exact charger IC and electrical mechanism of active discharge
every G3/battery-disconnect/EC-reset class clears the state identically
power_now=0 proves the adapter supplies every instantaneous system watt
```

The disable/reset path `0xF1 0x10` remains static-confirmed only. The post-depletion `0/0/0` state does not prove that this handler executed or that the fields are necessarily stored only in volatile SRAM.

The behavioral meaning of T2 is no longer unresolved: the 85/90 experiment live-confirms it as the upper boundary of the active-discharge region on the investigated P916F-STX.

## 20. Windows-side cross-OS PMC2 readback

After the `85/90` battery-limit policy had been configured under Linux and the machine was booted into Windows, the same PMC2 getter family was queried directly from Windows. The retained console output was:

```text
=== P916F-STX Battery Limit / Windows PMC2 ===
Enabled : 1 (ON)
T1      : 85% (0x55)
T2      : 90% (0x5A)
```

This is direct live evidence that the PMC2-visible charge-limit fields survived the tested Linux-to-Windows transition unchanged:

```text
Enabled = 1
T1      = 85
T2      = 90
```

The result is stronger than behavioral observation alone because the EC-facing getters returned the exact programmed fields while Windows was running. It establishes cross-OS persistence for this tested transition and rules out a Linux-userspace-only persistence mechanism.

The result does **not** identify the physical storage medium, prove that the fields reside only in volatile EC SRAM, or establish persistence across every shutdown, G3, battery-disconnect, EC-reset, firmware-update or other power-loss class. The separately recorded battery-depletion full-system-power-loss event still provides the observed counterexample in which the next powered session returned `0/0/0`.

## 21. Shutdown and AC-removal persistence

Two additional persistence tests were owner-reported after the `85/90` state had already been established.

Test C:

```text
normal shutdown
AC remains connected
next PMC2 readback -> Enabled=1 / T1=85 / T2=90
```

Test D:

```text
normal shutdown
AC removed after shutdown
internal battery remains connected
next PMC2 readback -> Enabled=1 / T1=85 / T2=90
```

These results establish that the programmed state survived both tested shutdown paths and that AC removal alone was not sufficient to clear the state while the internal battery remained connected.

No verbatim console transcript was supplied for these two runs, so this section records owner-reported live outcomes rather than full-capture evidence. The separately retained depletion capture remains the counterexample: after battery depletion caused complete system power loss, the next observed state was `0/0/0`.

The combined evidence supports a bounded persistence statement: the charge-limit state survives ordinary reboot/OS/shutdown transitions and AC removal in the tested cases, but it has been observed to clear after a deep battery-depletion power-loss event. The exact storage medium and exact clearing transition remain unestablished.

