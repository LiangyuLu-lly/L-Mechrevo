# -*- coding: utf-8 -*-
"""
Task B: rewrite ONLY the header comment block of installer-src\files\GCU\Select-GcuPayload.ps1
so it describes the CURRENT implementation (payload\{GCU,50,40,30} + the real switches).

Hard guarantees enforced by this script:
  * only lines 1..30 (the '#Requires' line + the '<# ... #>' comment block) are touched;
  * every line after the '#'> terminator is compared byte-for-byte before/after and must be
    identical, otherwise the script refuses to write;
  * the non-comment code lines are extracted before/after and must be identical;
  * output is ASCII, CRLF, no BOM (same signature as the input file).

Backup is written to D:\\openLM\\backup\\pkgs\\Select-GcuPayload.ps1_pre_v2907finish (done separately).
"""
import hashlib
import io
import sys

TARGET = r'D:\openLM\installer-src\files\GCU\Select-GcuPayload.ps1'
BACKUP = r'D:\openLM\backup\pkgs\Select-GcuPayload.ps1_pre_v2907finish'
CODE_BEFORE = r'D:\openLM\backup\pkgs\selectgcu_code_pre_v2907finish.txt'

NEW_HEADER = """#Requires -Version 5.1
# ASCII-only by design (no BOM): Windows PowerShell 5.1 reads a .ps1 as ANSI unless it has a
# BOM, and this file runs both from the installer and by hand on GBK systems.
<#
.SYNOPSIS
    Maps the local hardware to the GCU payload directory bundled by the L-Mechrevo installer.

.DESCRIPTION
    CURRENT DESIGN (payload split, matches $script:Bundle / $script:PayloadEntries below):
    four payload trees are staged and three of them are overlays:

      payload\\GCU - the full GCU base tree (247 files / 34.35 MB): GCUBridge.exe, OSDTpDetect.exe,
                     ACPIDriverDll.dll, UWACPIDriver, logo/, configs. It does NOT contain the
                     service trio (GCUService.exe / UEFI_Firmware.dll / NVControlSetting.dll).
      payload\\50  - the 50-series service trio (3 files / 19.15 MB)   -> overlay for 50-series.
      payload\\40  - the 40-series service trio (4 files / 16.55 MB)   -> overlay for 40-series
                     (the mixed-GCU fix; our 40-series three-piece set).
      payload\\30  - the 30-series service patch (130 files / 14.83 MB): 30-series GCUService +
                     .NET Core runtime + GamingMonitor etc., whose EC does not match the 40 trio.
                     -> overlay for 30-series.

    The base tree payload\\GCU is always installed first, then the selected overlay is copied
    over it. All three overlays stage ServiceDir = 'AiStoneService'; the difference between them
    is *which bytes* land in that directory, so a selection is identified by payload identity
    (SHA256 of the service binary), never by a version number.

    SELECTION RULES (as implemented):
      - Variant '50' / '40' / '30' (case-insensitive) forces that overlay and bypasses hardware
        detection; Reason is reported as 'forced variant N (hardware detection bypassed)'.
      - Any OTHER -Variant value (including the default 'Auto', and also unknown values such as
        '70', which are NOT refused) falls through to auto-detection.
      - Auto-detection order:
          1. GPU marketing name, English/ASCII vendor strings only: \\bRTX\\s*N[5-9]\\d\\b for
             N = 50 / 40 / 30. A Chinese-localised GPU name therefore does NOT match here --
             the PCI device-id pass below is what resolves such a machine.
          2. NVIDIA PCI device-id high byte (DEV_xxxx & 0xFF00): 50 = 2B/2C/2D/2E/2F,
             40 = 26/27/28, 30 = 22/24/25, pre-30 'legacy' = 1B/1C/1D/1E/1F/20/21.
          3. Nothing found -> 'unknown', then the fallbacks below.
      - Falls back to the installed LM config (%APPDATA%\\MechrevoLite\\config.json, then
        %ProgramData%\\MechrevoLite\\config.json) dgpu_generation Gen50 / Gen40 / Gen30.
      - With no NVIDIA signal at all (iGPU-only / office machine) -> 50-series overlay.
      - Still unresolved (an NVIDIA adapter whose generation cannot be resolved) -> 50-series
        overlay as the safety floor.
      - Resulting mapping: 50 -> payload\\50, 40 -> payload\\40, 30 -> payload\\30,
        pre-30 NVIDIA ('legacy') -> payload\\30 patch, unresolved / no dGPU -> payload\\50.
        The 'legacy' pre-30 path exists only as a key inside Get-GpuGeneration; it is not a
        fourth overlay -- it resolves to the 30-series patch.

    -BiosProjectId is accepted and threaded through but is deliberately NOT a detection input:
    the platform code must not decide the dGPU generation.

.PARAMETER Variant
    'Auto' (default) or a force value. Only 50 / 40 / 30 actually force; anything else is treated
    as auto-detection.

.PARAMETER SelfTest
    Runs the built-in case table and exits 0 (all pass) / 1 (any failure). Its expectations are a
    function of live external state: the 'Intel-only office' and 'unresolved NVIDIA id' cases
    expect Read-LmConfigGeneration's value when an LM config is present, otherwise '50'. So on a
    machine whose config says Gen40 those two cases read '-> 40', which is correct, not a failure.

.PARAMETER AsJson
    Emits the selection object as JSON (Depth 4) instead of the human-readable Default block.
    Fields: Generation, Variant, ServiceDir, RepoPayload, StagedDir, Reason, Evidence.

.PARAMETER OutputFile
    Writes the chosen Variant to that path as ASCII (one value, no newline).

.PARAMETER DeviceId / GpuName
    Explicit inputs for auto-detection; when omitted (or empty) Get-LiveProbe enumerates the
    display-class registry, then Win32_VideoController -> Win32_PnPEntity.

.NOTES
    NOT PRESENT IN THIS FILE (do not document them as working switches):
      * -SinglePayload / /GCUVARIANT do not exist here. Passing -SinglePayload fails with
        "A parameter cannot be found that matches parameter name 'SinglePayload'". An older
        beta21 build had that switch; this payload-split build has no such second payload.
      * -Probe is a parameter of the internal Select-GcuPayload function, not of this script.

.OUTPUTS
    [pscustomobject] with Generation, Variant, ServiceDir, RepoPayload, StagedDir, Reason, Evidence.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File installer\\Select-GcuPayload.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File installer\\Select-GcuPayload.ps1 -SelfTest
    powershell -NoProfile -ExecutionPolicy Bypass -File installer\\Select-GcuPayload.ps1 -Variant 40 -AsJson

.HISTORICAL (kept for context; NONE of it describes this build)
      * beta21 two-master design: release\\GCU-only (newest AiStoneService + UWACPIDriver) served
        30/40/50 and every undeterminable generation, while release\\GCU-1020 (GamingCenterU
        legacy UniwillService + ACPIDriver) served GTX 10 / GTX 16 / RTX 20 -- the machines whose
        official console is GamingCenterU. Detection there was logged, not a gate, because a
        single payload installed regardless. Those release\\* trees are NOT this design; here the
        overlay decision IS decisive and picks among payload\\50 / payload\\40 / payload\\30.
      * N6 single-payload era: until beta20 only release\\GCU-only shipped and it served every
        generation; -Variant / /GCUVARIANT was refused because no second payload existed.
      * The retired 40-series trees and the old G0 gate are gone. The broken platform-code ->
        generation heuristic was removed because axis 1 (platform code) must not decide axis 2
        (dGPU generation). The G0 safety net's replacement remains the post-install / first-run
        self-check plus the visible fallback described in Install-Gcu.ps1.
#>"""


def code_lines(text):
    """Non-comment, non-blank lines, trimmed -- the behavioural fingerprint."""
    out = []
    in_block = False
    for line in text.split('\n'):
        line = line.rstrip('\r')
        if not in_block:
            if '<#' in line:
                in_block = True
                continue
            if line.lstrip().startswith('#'):
                continue
            if not line.strip():
                continue
            out.append(line.strip())
        else:
            if '#>' in line:
                in_block = False
    return out


def main():
    raw = open(TARGET, 'rb').read()
    if raw[:3] == b'\xef\xbb\xbf':
        sys.exit('REFUSING: file has a BOM (expected none)')

    text = raw.decode('ascii')          # raises if any non-ASCII sneaks in
    if '\t' in text:
        sys.exit('REFUSING: file contains tabs (expected none)')

    lines = text.split('\r\n')
    if lines[-1] != '':
        sys.exit('REFUSING: file does not end with CRLF')

    # --- locate the header terminator -------------------------------------
    if lines[3] != '<#':
        sys.exit('REFUSING: line 4 is not "<#" (header layout changed): %r' % lines[3])
    term = None
    for idx in range(4, len(lines)):
        if lines[idx].rstrip() == '#>':
            term = idx
            break
    if term is None:
        sys.exit('REFUSING: no "#>" terminator found')
    print('header block = lines 1..%d (terminator at index %d)' % (term + 1, term))

    new_lines = NEW_HEADER.split('\n') + lines[term + 1:]
    new_text = '\r\n'.join(new_lines)

    # --- guarantee 1: everything after the terminator is byte-identical ----
    old_tail = '\r\n'.join(lines[term + 1:])
    new_tail = '\r\n'.join(new_lines[len(NEW_HEADER.split('\n')):])
    assert old_tail == new_tail, 'tail changed - aborting'
    print('OK  tail after header is byte-identical (%d bytes)' % len(old_tail))

    # --- guarantee 2: behavioural fingerprint identical --------------------
    before = code_lines(text)
    after = code_lines(new_text)
    if before != after:
        import difflib
        for d in difflib.unified_diff(before, after, 'code-before', 'code-after', lineterm=''):
            print(d)
        sys.exit('REFUSING: non-comment code lines changed')
    print('OK  non-comment code lines identical (%d lines)' % len(after))

    # --- guarantee 3: the recorded pre-edit fingerprint still matches ------
    try:
        recorded = [l.rstrip('\r') for l in
                    io.open(CODE_BEFORE, encoding='utf-8-sig').read().split('\n') if l.strip()]
        if recorded != before:
            sys.exit('REFUSING: live code lines differ from the recorded pre-edit baseline')
        print('OK  matches recorded pre-edit code baseline (%d lines)' % len(recorded))
    except IOError:
        print('WARN: no recorded baseline found, skipped that check')

    # --- guarantee 4: new header itself is ASCII ---------------------------
    NEW_HEADER.encode('ascii')
    print('OK  new header is pure ASCII')

    data = new_text.encode('ascii')
    with open(TARGET, 'wb') as fh:
        fh.write(data)

    print('')
    print('bytes  %d -> %d' % (len(raw), len(data)))
    print('sha256 before %s' % hashlib.sha256(raw).hexdigest().upper())
    print('sha256 after  %s' % hashlib.sha256(data).hexdigest().upper())


if __name__ == '__main__':
    main()
