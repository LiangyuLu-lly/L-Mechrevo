@echo off
rem ---------------------------------------------------------------------------
rem Double-click entry point for Invoke-DgpuSilence.ps1
rem
rem Windows does not run a .ps1 on double-click (it opens an editor), so this
rem .cmd is the "click me" door: it starts the script without arguments, which
rem makes the script ask for administrator rights (UAC) and then show the
rem interactive menu (status / disable dGPU / enable dGPU).
rem
rem Keep this file ASCII-only: cmd.exe renders non-ASCII text using the OEM code
rem page, and the .ps1 next to it must stay ASCII too (see its header).
rem ---------------------------------------------------------------------------
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Invoke-DgpuSilence.ps1"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo.
    echo exit code = %RC%
    pause
)
endlocal
