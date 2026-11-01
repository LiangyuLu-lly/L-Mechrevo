# -*- coding: utf-8 -*-
import subprocess, os
dll = r'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll'
sat = r'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\zh-CN\L-Mechrevo.resources.dll'
print('主 DLL:', os.path.getsize(dll), 'bytes')
import datetime
print('时间:', datetime.datetime.fromtimestamp(os.path.getmtime(dll)))
# 用 dotnet 读程序集属性不方便，直接用 powershell 读版本资源
out = subprocess.run(['powershell', '-NoProfile', '-Command',
    f'$a=[System.Diagnostics.FileVersionInfo]::GetVersionInfo(r"{dll}"); $a.FileVersion; $a.ProductVersion'],
    capture_output=True, text=True)
print('FileVersion/ProductVersion:', out.stdout.strip())
print('卫星:', os.path.exists(sat), os.path.getsize(sat) if os.path.exists(sat) else '')
