@echo off
setlocal
rem 双击运行：自动提权后启动 EC 寄存器变化嗅探（只读）
rem 用法：直接双击；或 cmd 里带参数运行（会原样传给 ec-kbd-sniff.ps1）
rem   ec-kbd-sniff.cmd -Start 0x740 -End 0x8FF -IntervalMs 500 -Seconds 180

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo 需要管理员权限，正在提权...
  powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0' -ArgumentList '%*'"
  exit /b
)

set "PS1=%~dp0ec-kbd-sniff.ps1"
set "ARGS=%*"
if "%ARGS%"=="" set "ARGS=-Start 0x700 -End 0x8FF -IntervalMs 500 -Seconds 180"

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%
echo.
echo 日志见 %TEMP%\ec-kbd-sniff.txt
pause
