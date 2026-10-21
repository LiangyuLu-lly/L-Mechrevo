@echo off
chcp 936 >nul
setlocal
title GCU 键盘灯抓包
set "PY=%~dp0gcu_kbd_capture.py"

echo ============================================================
echo  GCU 键盘灯专项抓包（被动，不向 GCU 发控制指令）
echo  1) 本窗口启动后，再打开官方 GCU 控制台操作键盘灯
echo  2) 一次一个动作、间隔 5 秒：开灯 / 亮度+ / 亮度- / 换效果 / 关灯
echo  3) 结束后把 D:\openLM\diagnostics\GCU_kbd_capture_*.log 发回
echo ============================================================
echo.

set "ARGS=%*"
if "%ARGS%"=="" set "ARGS=240"

where py >nul 2>&1
if %errorlevel%==0 (
  py -3 "%PY%" %ARGS%
) else (
  python "%PY%" %ARGS%
)
set "RC=%errorlevel%"
echo.
if not "%RC%"=="0" (
  echo 运行失败（退出码 %RC%）：请确认已安装 Python，并执行 py -3 -m pip install paho-mqtt
)
pause
