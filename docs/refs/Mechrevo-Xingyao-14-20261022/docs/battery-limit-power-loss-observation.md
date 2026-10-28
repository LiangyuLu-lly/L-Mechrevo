# Battery-limit state after battery-depletion power loss

## Scope

This page records a live persistence observation from the investigated MECHREVO Xingyao 14 / `P916F-STX`. The capture was supplied after the machine had fully lost system power because the battery was depleted. The capture does not restate the BIOS or firmware-reported EC revision, so this page does not independently attach a firmware-version identity beyond the investigated unit and the battery-limit interface shown in the output.

The observation supplements the earlier validation in which `Enabled=1`, `T1=80%`, `T2=100%` survived a normal reboot. It establishes a different outcome after this later battery-depletion power-loss event.

This page is specifically about **persistence**. The later 85/90 experiment separately resolves the live behavioral roles of T1 and T2; see [battery threshold semantics](battery-threshold-semantics.md). The fact that the fields were later observed as `0/0/0` after depletion does not alter those threshold semantics.

## Raw capture

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

The battery telemetry units follow the Linux power-supply conventions already used by this repository: `Power` is recorded in µW, `Energy` in µWh and `Voltage` in µV. The capture therefore corresponds to 21.137 W reported battery charging power, 64.350 Wh reported stored energy and 13.431 V reported battery voltage at that instant.

## Established result

The previously configured battery-limit state was not present after the recorded battery-depletion power-loss event. The next observed state was:

```text
Enabled = 0
T1      = 0
T2      = 0
```

At the same time, AC was online, SOC was 89%, and the battery reported `Charging`. This is **Live-confirmed** evidence that the earlier `1 / 80 / 100` configuration did not survive this recorded complete-loss-of-system-power event.

The earlier normal-reboot result remains valid and distinct:

```text
normal reboot -> state 1 / T1 80 / T2 100 retained
battery-depletion full-power-loss event -> state 0 / T1 0 / T2 0 observed afterward
```


## Cross-OS retention observation

A later owner-observed transition adds a separate persistence case: after the charge limit had been configured under Linux, the machine was booted into Windows and the limit behavior remained active.

The state was then queried directly from Windows through the same ITE PMC2 battery-limit getter family. The retained Windows-side output was:

```text
=== P916F-STX Battery Limit / Windows PMC2 ===
Enabled : 1 (ON)
T1      : 85% (0x55)
T2      : 90% (0x5A)
```

This is **Live-confirmed** evidence that the exact PMC2-visible charge-limit fields remained programmed across the Linux-to-Windows transition. The result rules out a Linux-userspace-only persistence mechanism for this tested transition.

The observation still does not identify the physical storage medium, prove that the values are held only in volatile EC SRAM, or establish that every reboot/shutdown path with standby power will retain the state.

The combined observed persistence pattern is now:

```text
normal reboot -> programmed state retained
Linux -> Windows boot transition -> PMC2 readback retained 1 / 85 / 90
battery-depletion full-system-power-loss event -> state 0 / T1 0 / T2 0 observed afterward
```

This strengthens the distinction between ordinary OS/reboot transitions and the recorded deep power-loss event, while leaving the exact clearing transition and storage mechanism unresolved.

## Shutdown and AC-removal retention

Two additional owner-reported persistence tests were completed with the `85/90` policy. In both cases, the post-transition PMC2 readback still returned the programmed state:

```text
Enabled = 1
T1      = 85
T2      = 90
```

The tested transitions were:

```text
normal shutdown with AC still connected
    -> 1 / 85 / 90 retained

normal shutdown, AC removed while the internal battery remained connected
    -> 1 / 85 / 90 retained
```

No verbatim console capture was retained for these two runs, so they are recorded as owner-reported live results rather than full-capture evidence.

Together with the normal-reboot, Linux-to-Windows PMC2 readback and depletion result, the observed pattern is now:

```text
normal reboot                                      -> retained
Linux -> Windows boot transition                   -> retained
normal shutdown with AC connected                  -> retained
normal shutdown + AC removed, internal battery on  -> retained
battery-depletion full-system-power-loss event     -> cleared to 0 / 0 / 0
```

For the tested cases, removal of the AC adapter is therefore not sufficient to clear the battery-limit state while the internal battery remains connected. The exact storage mechanism and the exact event that clears the state during deep power loss remain unresolved.

## Boundaries

This observation establishes behavior, not the persistence mechanism. It does not determine when the values were cleared or whether clearing occurred during battery depletion, an EC brownout/reset, firmware initialization on the next power-on, or another transition associated with the event.

It also does not establish that every G3 transition, battery disconnect, CMOS/RTC-power removal, explicit EC reset, firmware update, or other power-loss class produces the same result. No claim is made that the threshold fields are necessarily stored only in volatile SRAM.

The observation is complete-system-power-loss evidence, not direct instrumentation of the EC power rail. Therefore it does not by itself prove a specific complete-EC-power-domain-loss mechanism.

Operationally, the result means the charge-limit state must not be assumed to remain enabled after a battery-depletion event that fully powers the machine off. A state readback is required before relying on the limit after such an event. A persistent Linux implementation should restore the desired policy only when readback shows that state has been cleared or changed; see [battery charge-limit protocol](battery-charge-limit.md#persistence).
