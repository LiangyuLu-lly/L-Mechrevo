@echo off
rem ---------------------------------------------------------------------------
rem Double-click entry point for the GCU GPU toolbox (Chinese UI).
rem This file is deliberately ASCII-ONLY with NO BOM:
rem   * cmd.exe is strictest about encoding, so no non-ASCII text and no BOM here;
rem   * all Chinese lives in GcuGpuToolbox.ps1, which is UTF-8 WITH a BOM
rem     (Windows PowerShell 5.1 needs the BOM to read UTF-8 correctly).
rem Do not "save as" these files from Notepad: it can drop the BOM and break
rem the Chinese text.
rem ---------------------------------------------------------------------------
cd /d "%~dp0"
if not exist "%~dp0GcuGpuToolbox.ps1" (
    echo.
    echo Missing file: GcuGpuToolbox.ps1
    echo Keep this .cmd and GcuGpuToolbox.ps1 in the SAME folder.
    echo.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0GcuGpuToolbox.ps1"
if errorlevel 1 pause
