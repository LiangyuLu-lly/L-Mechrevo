@echo off
rem ============================================================
rem  L-Mechrevo iGPU capture -- STEP 1 of 2  (ASCII-only by design)
rem  Opens full logging, restarts LM, then guides you.
rem  Double-click this file; it self-elevates.
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
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -Step1
echo.
pause
