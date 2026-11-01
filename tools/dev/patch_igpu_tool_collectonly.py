# -*- coding: utf-8 -*-
r"""给 collect.ps1 加 -CollectOnly 开关（只采集、不重启 LM、不交互），并生成 只采集.bat"""
import io
import os
import re
import sys

OUT = r'D:\openLM\tools\igpu-capture'
PS1 = os.path.join(OUT, 'collect.ps1')
GEN = r'D:\openLM\tools\gen_igpu_capture_tool.py'

BAT2 = r"""@echo off
rem ============================================================
rem  L-Mechrevo iGPU capture -- COLLECT ONLY (no LM restart, no prompts)
rem  Use this after you already rebooted Windows and want the 2nd sample.
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
"""


def main():
    # 1) 生成 只采集.bat
    with io.open(os.path.join(OUT, '只采集.bat'), 'w', encoding='ascii', newline='\r\n') as f:
        f.write(BAT2)
    print('已生成 只采集.bat')

    # 2) 给 collect.ps1 注入 param + CollectOnly 分支
    data = io.open(PS1, 'rb').read()
    text = data.decode('utf-8-sig')
    if '-CollectOnly' in text:
        print('collect.ps1 已含 -CollectOnly，跳过')
    else:
        # 在第一个 # ==... 注释块之后插入 param
        anchor = '$ErrorActionPreference = \'Stop\''
        if anchor not in text:
            print('!! 找不到锚点，未改 collect.ps1')
            return 1
        # param 必须位于脚本最前（注释之后、语句之前）
        marker = '$ErrorActionPreference = \'Stop\''
        inject = ("param(\n"
                  "    # 只采集：不重启 LM、不交互（用于重启系统后的第二次采集）\n"
                  "    [switch]$CollectOnly\n"
                  ")\n\n" + marker)
        text = text.replace(marker, inject, 1)

        # 在 $stamp 计算之后、步骤 1/3 之前插入 CollectOnly 分支
        anchor2 = "# --- 1/3 ---"
        if anchor2 not in text:
            print('!! 找不到步骤锚点')
            return 1
        branch = ("if ($CollectOnly) {\n"
                  "    Line '  [只采集模式] 不重启 LM、不交互，直接从现有 log.txt 生成报告'\n"
                  "    $out = Join-Path $desk ('LM-核显采集-只采集-' + $stamp + '.txt')\n"
                  "    $ok = Collect '只采集（未重启 LM，通常用于系统重启后的第二次采样）' $out\n"
                  "    Line ''\n"
                  "    Rule\n"
                  "    if ($ok) { Line (' 完成！请把报告发回开发者：' + $out) } else { Line ' 采集失败，请看上方提示' }\n"
                  "    Rule\n"
                  "    try { Start-Process explorer.exe $desk } catch { }\n"
                  "    exit 0\n"
                  "}\n\n" + anchor2)
        text = text.replace(anchor2, branch, 1)

        # $desk / $stamp 必须在分支之前定义 —— 检查顺序
        i_stamp = text.find('$stamp = Get-Date')
        i_branch = text.find('if ($CollectOnly)')
        if i_stamp < 0 or i_branch < 0 or i_stamp > i_branch:
            print('!! 变量定义顺序不对（$stamp=%d, branch=%d）' % (i_stamp, i_branch))
            return 1

        io.open(PS1, 'w', encoding='utf-8-sig', newline='\r\n').write(text)
        print('已给 collect.ps1 注入 -CollectOnly')

    # 3) 同步修改生成器，避免以后重生成覆盖掉
    g = io.open(GEN, encoding='utf-8').read()
    if '-CollectOnly' not in g:
        print('（提示：生成器未同步，重生成会覆盖。建议下次直接用 igpu-capture 目录里的文件）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
