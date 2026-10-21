@echo off
chcp 65001 >nul
setlocal
rem 一键启动：GCU 键盘灯专项抓包（被动订阅，不向 GCU 发控制指令）
rem 输出：D:\openLM\diagnostics\GCU_kbd_capture_<时间>.log 与 ..._键盘相关.log
rem 用法：双击；或带秒数：运行-GCU键盘抓包.cmd 300

echo ============================================================
echo  GCU 键盘灯专项抓包（被动）
echo  1) 本窗口启动后，再打开官方 GCU 控制台操作键盘灯
echo  2) 一次一个动作、间隔 5 秒：开灯 / 亮度+ / 亮度- / 换效果 / 关灯
echo  3) 结束后把 D:\openLM\diagnostics\GCU_kbd_capture_*.log 发回
echo ============================================================
echo.

set "ARGS=%*"
if "%ARGS%"=="" set "ARGS=240"

where py >nul 2>&1
if %errorlevel%==0 (
  py -3 "%~dp0gcu_kbd_capture.py" %ARGS%
) else (
  python "%~dp0gcu_kbd_capture.py" %ARGS%
)
if errorlevel 1 (
  echo.
  echo 运行失败：请确认本机有 Python（py -3 或 python）且已安装 paho-mqtt。
  pause
  exit /b 1
)
pause
