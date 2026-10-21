@echo off
chcp 936 >nul
setlocal
title EC 抓取（只读）
set "PS1=%~dp0ec-kbd-sniff.ps1"

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo 正在请求管理员权限...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'powershell' -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%PS1%'"
  if errorlevel 1 (
    echo.
    echo 提权失败：请右键本文件，选择“以管理员身份运行”。
  )
  echo.
  pause
  exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "RC=%errorlevel%"
echo.
echo ============================================================
echo  抓取结束（退出码 %RC%）
echo  日志文件：%TEMP%\ec-kbd-sniff.txt
echo ============================================================
pause
