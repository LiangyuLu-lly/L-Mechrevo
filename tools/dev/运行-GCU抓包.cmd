@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gcu-topic-sniff.ps1" -Seconds 180
