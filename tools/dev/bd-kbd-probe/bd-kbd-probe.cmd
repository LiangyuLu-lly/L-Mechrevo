@echo off
rem ============================================================
rem  宝龙达键盘灯探针 · 启动器（解决「右键运行 → 窗口一闪而过」）
rem
rem  为什么会一闪而过：
rem    · 「右键 → 使用 PowerShell 运行」用的是 **Windows PowerShell 5.1**，
rem      脚本一结束（包括我们的"需要管理员"直接 exit 5）窗口立刻关闭；
rem    · 而且它不是管理员，WMI 调用会被拒。
rem
rem  这个启动器做两件事：
rem    1) 提权（弹 UAC）→ 新开一个管理员 PowerShell 窗口；
rem    2) 加 -NoExit ⇒ 脚本跑完窗口**停住**，能看到输出。
rem
rem  用法：双击本文件即可。想带参数就在命令行里跑，例如：
rem    bd-kbd-probe.cmd -Watch 30
rem    bd-kbd-probe.cmd -TestWrite
rem ============================================================

setlocal
set "SCRIPT=%~dp0bd-kbd-probe.ps1"
set "ARGS=%*"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process -FilePath 'powershell' -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%SCRIPT%') %ARGS%"

if errorlevel 1 (
    echo.
    echo [!] 提权失败（可能点了"否"）。请右键本文件 -^> 以管理员身份运行。
    pause
)
endlocal
