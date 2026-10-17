@echo off
rem ============================================================
rem  宝龙达键盘灯探针 · 自提权启动器（窗口不会一闪而过）
rem
rem  以前的问题：
rem   · 用「右键 → 使用 PowerShell 运行」= 非管理员 + 脚本结束即关窗；
rem   · 用 Start-Process 拼参数时引号被 cmd 吃掉 ⇒ 直接报错退出。
rem
rem  现在：
rem   1) 先检查是否管理员，不是就**用管理员身份重新运行本 .cmd**（UAC）；
rem   2) 然后在本窗口里跑 PowerShell（5.1），加 -NoExit ⇒ 跑完窗口留着；
rem   3) 最后 pause，保证能看到错误。
rem
rem  用法：双击即可。带参数示例（管理员已确认时可跳过 UAC）：
rem    bd-kbd-probe.cmd -TestWrite
rem ============================================================

setlocal
net session >nul 2>&1
if %errorlevel% equ 0 goto :run

echo 正在请求管理员权限...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b

:run
set "SCRIPT=%~dp0bd-kbd-probe.ps1"
if not exist "%SCRIPT%" (
    echo [X] 找不到 %SCRIPT%
    pause
    exit /b 1
)
echo 以管理员身份运行：%SCRIPT%
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -File "%SCRIPT%" %*
echo.
echo （如果上面有红色错误，请把窗口内容或同目录的 bd-kbd-probe-*.log 发给我们）
pause
endlocal