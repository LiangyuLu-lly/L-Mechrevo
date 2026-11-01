@echo off
rem ============================================================
rem  L-Mechrevo iGPU capture -- STEP 2 of 2  (ASCII-only by design)
rem  Run this AFTER Windows rebooted: it only collects and writes
rem  a report to the Desktop. No restart, no prompts.
rem ============================================================
setlocal
set "PS1=%~dp0collect.ps1"
if not exist "%PS1%" (
  echo [ERROR] collect.ps1 not found next to this .bat
  pause
  exit /b 1
)
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -CollectOnly
echo.
pause
