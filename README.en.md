# L-Mechrevo

[中文](README.md) | **English**

L-Mechrevo is an open-source control center for Mechrevo laptops (Mechrevo / Uniwill-built models), living in the Windows system tray. It talks to the GCU hardware service already present on the machine to read sensor state and send control commands, gathering performance modes, custom power limits, keyboard lighting, display and battery controls into one lightweight window. Entries the hardware doesn't support hide themselves automatically.

## Interface overview

The main window is a scrollable dashboard. Top to bottom: performance modes, live telemetry, GPU mode, display, battery, liquid cooling, lighting, and extra switches. The footer holds the hardware overlay toggle, settings, update check, donate, and exit. Each section header has an arrow that collapses or expands it, and the collapsed state is remembered.

![Main dashboard](docs/images/1-dashboard.png)

## Features

### Performance modes

The performance section on the dashboard shows segmented buttons. Tap to switch; the active mode is highlighted:

| Mode | What it does | Availability |
|---|---|---|
| Silent | Caps power and fan speed for the lowest noise | All models |
| Balanced | Default mode, balances performance and noise | All models |
| Silent Turbo | Turbo performance with a quieter fan strategy | Shown only when the model's confirmed capability supports it |
| Turbo | Unlocks higher power limits | All models |
| Custom | Opens the custom performance mode window | All models |

The tray right-click menu can switch modes too, with entries "Silent (Office)", "Balanced (Game)", and "Turbo (Enhanced)". The active mode gets a check mark.

### Custom performance mode (4 slots)

Tap "Custom" on the dashboard to open a separate window. Four slot buttons (Custom 1 to 4) sit at the top; each slot stores a complete parameter set and selecting a slot applies it. The active slot shows "Custom N active".

![Custom performance mode](docs/images/2-custom-mode.png)

Each slot can hold:

- **Windows power plan**: a dropdown, saved and applied per slot.
- **Turbo mode**: Disabled / Enabled / Aggressive / Efficient, saved per slot.
- **CPU power limits PL1 / PL2 / PL4**: slider + value box + stepper buttons. PL1 and PL2 are the sustained and short-term limits, PL4 is the instantaneous limit. Ranges come from GCU per model. Not every model reports PL4; the row hides itself when the range is invalid.
- **GPU TGP target**: caps total GPU power in watts, range read back per model.
- **GPU dynamic boost**: a toggle plus a boost value in watts (GPU Dynamic Boost).
- **Fan shift sensitivity**: a toggle plus a shift delay in milliseconds, controlling how fast fan speed steps change.
- **GPU overclock**: a toggle plus core clock offset (MHz) and memory clock offset (MHz). Uses the NVIDIA driver directly or the GCU channel, whichever is available; ranges are read back from the driver live.
- **Reset slot to defaults**: restores every parameter of the current slot.
- **Fan curve**: opens the curve editor, see below.

The bottom of the window reads "Parameters are saved to the selected custom slot." Changes land in the selected slot immediately.

#### Fan curve

Tap "Fan curve" in the custom mode window to open the editor. CPU and GPU each get a curve: temperature (°C) on the X axis, fan duty (%) on the Y axis, 16 fixed temperature points. Drag a point and it saves in real time. A "Independent fan control" toggle sits in the window: off = both fans follow the GPU curve (default), on = CPU and GPU fans run their own curves. Tap "Save" when done.

![Fan curve editor](docs/images/5-fan-curve.png)

### Lighting

The lighting section lists one row per channel: keyboard, lightbar, logo. Each row has a power switch, an effect dropdown, and an "Edit" button. The dropdown switches effects directly; "Edit" opens a window with that effect's parameters. The lightbar and logo rows only appear when the machine has that hardware. The section header shows a live summary such as "3 channels · 1 on".

![Keyboard lighting](docs/images/3-keyboard.png)

- **Keyboard lighting**: 10 software-rendered effects: smooth rainbow, static, breathing, sparkle, reactive, rainbow wheel, lightning, flame, rain, matrix. Each effect carries its own parameters (brightness, frame rate, angle, speed, colors, depending on the effect). Frames are rendered in-app and written to the keyboard over HID, independent of the firmware effects.
- **Lightbar lighting**: single color, breathing, wave, impact, meteor, with brightness (4 levels), speed (slow / medium / fast), and a single color.
- **Logo lighting**: single color, breathing, mix, same parameters as the lightbar.

![Lightbar lighting](docs/images/4-lightbar.png)

Two global settings sit at the top of the lighting section:

- **Turn all lights off on battery**: unplugging the charger extinguishes every light; plugging back in restores them.
- **Sleep timer**: off / 10 s (software) / 10 min to 2 h. After the machine sits idle for the set time all lights go out, and any input brings them back.

### Liquid cooling (external water cooler)

With an external water cooler connected, the liquid cooling section header shows a status summary such as "Connected · pump max · fan max". Inside the section:

- **Pump speed**: auto / 45% / 60% / max.
- **Fan**: auto plus several manual steps.
- **Water cooler lighting**: opens the liquid cooling light menu (pump head / fan lighting, requires Mk2 fan light capability).

When disconnected the section shows "Not connected" and can retry; if the GCU channel fails it can fall back to a direct Bluetooth connection. The whole section only appears when a water cooler is detected.

![Liquid cooling](docs/images/6-liquid.png)

### Display

The display section gathers screen controls:

- **Refresh rate**: segmented buttons built from the model's supported list (for example 240 / 60); tap to switch.
- **Auto refresh rate**: a toggle that switches between high refresh and power-saving refresh based on load.
- **Color calibration**: a dropdown with Default / sRGB gamut modes.
- **Brightness**: the standard Windows brightness slider.
- **Overdrive**: lives in the "Settings" dialog on the footer, shown when the model supports it.

![Display](docs/images/7-screen.png)

There is also a "Screen off (no sleep)" switch under "Extra switches → Power & system": it dims the panel once and holds off standby. Move the mouse or press a key to wake the screen; the system does not sleep.

### GPU mode

The GPU section offers segmented buttons: iGPU mode (eco), standard, and discrete-only, with modes such as automatic on some models, all gated by model capability. Modes that need a reboot (discrete-only, iGPU) prompt for a restart before taking effect.

### Battery

The battery section shows the battery's cycle count and provides:

- **Charge limit slider**: a continuous slider that sets the charging cap in percent (1% steps). The slider disables briefly until the command is confirmed.
- **Full charge**: temporarily raises the cap back to 100%, handy before travel.

### Extra switches

The extra switches section holds 22 toggles in three subgroups; the header shows "N/22 on":

- **Input devices**: touchpad, touchpad toggle key, WiFi, Bluetooth, camera, numpad lock.
- **Keyboard & hotkeys**: Win key lock, Fn lock, Copilot key lock, OSD hints.
- **Power & system**: USB charging, power-on auto start, high performance power, fan boost, deep sleep, game whitelist, advanced CPU performance, taskbar auto-hide, transparency effects, dark theme, screen off (no sleep), launch at login.

![Extra switches: input devices and keyboard hotkeys](docs/images/8-more-switches-1.png)

![Extra switches: power and system](docs/images/8-more-switches-2.png)

Every toggle sends its command on tap and disables briefly until the command is confirmed. Deep sleep needs a reboot to take effect and the app says so in a dialog. All switches are gated by model capability: items the service doesn't report hide entirely, and when a whole subgroup has nothing to show its heading collapses too.

### More

- **Live telemetry**: a row under the performance section shows CPU / GPU temperature, power draw, fan speed, and duty cycle.
- **Hardware overlay**: a desktop overlay with four layouts you can click through and drag anywhere (footer "Overlay" toggle).
- **Day / night theme**: switched in the footer "Settings" dialog.
- **Official console**: detects whether the vendor console is running and can isolate its UI and tray with one click, keeping only the GCU background service. Reversible at any time.
- **In-app updates**: silently checks for new versions; the "Check updates" button grows a badge when one exists. Downloads are verified by SHA-256 before the updater launches.
- **Launch at login**: implemented as a per-user scheduled task, no administrator rights needed ("Extra switches → Power & system → Launch at login").

## Download

Two packages are available on the [Releases](https://github.com/LiangyuLu-lly/L-Mechrevo/releases) page:

- `L-Mechrevo-<version>-setup.exe`: full installer, bundles the .NET runtime and the GCU service payload. Grab this for a first install.
- `L-Mechrevo-<version>-unsigned.zip`: the auto-update package for existing installs. The app prompts when a new version is detected; you can also download the zip manually and replace the program files yourself.

## System requirements

- Windows 10 / 11 64-bit.
- A Mechrevo laptop (Mechrevo / Uniwill-built models).
- The GCU hardware service (the installer deploys the right payload for your GPU generation automatically).
- Feature availability varies by model hardware; unsupported entries hide themselves.

## Installation and first run

1. Download `L-Mechrevo-<version>-setup.exe` from Releases.
2. Run the installer and follow the wizard.
3. The installer detects your GPU generation (RTX 40 / 50 series) and deploys the matching GCU service payload and driver.
4. Start the app for the first time after installation.
5. If the installer asks for a reboot, reboot once before use.
6. The app lives in the system tray; double-click the tray icon to open the main window.
7. To start with Windows, enable "Launch at login" under "Extra switches → Power & system".

On first launch the app shows a short guide. If the official console is running, the app offers to isolate its UI and tray (nothing is uninstalled; the GCU background service stays).

## Usage

### Opening and closing the main window

The app lives in the tray. Double-click the tray icon to show or hide the main window; the × in the corner only hides it and the app keeps running. To exit fully, use the "Exit" button on the footer or the tray menu.

### Switching performance modes

Tap a segment in the performance panel; the active mode highlights. Silent Turbo only appears on models that support it. The tray right-click menu switches modes too, with a check mark on the active one.

### Custom mode slots

Tap "Custom" to open the custom mode window and pick slot Custom 1 to 4 at the top. Each slot holds the Windows power plan, turbo mode, CPU power limits (PL1 / PL2, plus PL4 on some models), GPU TGP target, GPU dynamic boost, fan shift sensitivity, GPU overclock, and a "Fan curve" button. Changes save to the selected slot immediately; "Reset slot to defaults" restores the current slot.

### Keyboard, lightbar, and logo lighting

The lighting section has three rows: keyboard, lightbar, logo. Each row has a power switch, an effect dropdown, and an "Edit" button. Pick an effect from the dropdown, or open "Edit" to tune parameters (brightness / frame rate / angle / speed / colors for the keyboard; brightness / speed / single color for the lightbar and logo). The top of the section holds the "Turn all lights off on battery" toggle and the sleep timer dropdown. The lightbar and logo rows only appear on machines with that hardware.

### Liquid cooling gears

The liquid cooling section header shows the connection status. Once connected you can set the pump speed (auto / 45% / 60% / max) and the fan step, and open the water cooler light menu with the "Water cooler lighting" button.

### Refresh rate and color gamut

The display row shows segmented buttons for the model's supported refresh rates; tap to switch. The row header carries the color calibration dropdown (Default / sRGB) and the auto refresh rate toggle. The brightness slider sits on the same row.

### GPU mode

Tap a segment in the GPU panel to switch. Modes that need a reboot (discrete-only, iGPU) prompt for a restart.

### Extra switches

The extra switches card has three subgroups: input devices, keyboard & hotkeys, power & system. Each item is a toggle; tapping sends the command and the toggle disables briefly until the command is confirmed. Deep sleep needs a reboot and the app says so in a dialog.

### In-app updates

The app checks for new versions silently at startup; the "Check updates" button grows a badge when one exists. Open the update window for release notes, then tap "Download and install" to download, verify, and launch the updater. Versions published on a file host show an "Open download page" button instead.

![Update window](docs/images/9-update.png)

## FAQ

**It says GCU is not connected. What now?**
When the GCU hardware service isn't connected, hardware commands report "GCU hardware service is not connected, please try again later". Make sure the official console is installed and the GCU service is running, wait a moment, and retry.

**Why can't I see some feature entries?**
Entries the model doesn't support hide themselves, for example Silent Turbo, the lightbar, the logo light, the PL4 power limit, or the liquid cooling section. That's capability-based design, not a bug.

**I never get update prompts?**
Update checks need the current version to be outdated and the network to be reachable. Tap "Check updates" on the footer manually, or download from the Releases page.

**After using the "privacy screen" of a third-party remote / streaming tool (such as UU Remote), my local screen went black?**
That's the remote tool's privacy screen doing its job, not a fault of this app. Turn the privacy screen off on the remote side to restore the display.

**The screen went black after "Screen off (no sleep)". How do I get it back?**
Move the mouse or press any key and the screen lights up again.

**Are custom power limits / overclocking risky?**
Yes. Power limits, overclocking, and fan curves can affect stability and hardware lifespan. Evaluate for yourself; you do this at your own risk.

**The driver or antivirus blocked the install or update?**
The installer deploys GCU service payloads and drivers, which some antivirus products or system policies may block. Allow the installer and the program folder in your antivirus and retry; if a driver was blocked and never deployed, the related features won't work.

## License and corresponding source code

This program includes code derived from [G-Helper](https://github.com/seerge/g-helper) (GPL-3.0-only), so the whole work is released under the GNU GPL v3.0. See `THIRD_PARTY_NOTICES.txt` in the release package for full third-party notices.
Under GPLv3 §6(b): for three years from the distribution of this version, any third party may request the complete, machine-readable corresponding source code of the version you received via liangyulu781@gmail.com (charged only for media and shipping cost). Please state the exact version number (for example `0.289.0-beta14`) when requesting.

## Feedback

Issues are welcome at [Issues](https://github.com/LiangyuLu-lly/L-Mechrevo/issues). Please include: the model, the app version (visible at the bottom of the main window), and reproduction steps.

## Disclaimer

This is a personal project, unaffiliated with Mechrevo. Power limit and overclocking operations carry risk; evaluate the consequences yourself. The app is provided "as is", without any warranty.
