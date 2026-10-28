# -*- coding: utf-8 -*-
# 生成 EC 供电编码抓取 bat（单文件，内嵌 base64 ps1，UTF-8 BOM 保中文）
import base64
import os

PS1 = r'''# L-Mechrevo EC 供电编码抓取
param()
$ErrorActionPreference = 'Stop'
$dir = $env:LM_LOG_DIR
if (-not $dir) { $dir = (Get-Location).Path }
$log = Join-Path $dir 'LM-EC-Power-Input.log'

$cs = @'
using System;
using System.Runtime.InteropServices;
public static class EcReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr handle, uint code, IntPtr inBuffer, int inSize, IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);
    public static int ReadByte(int address)
    {
        IntPtr handle = CreateFile("\\\\.\\ACPIDriver", 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        if (handle == new IntPtr(-1)) { return -1; }
        try
        {
            IntPtr input = Marshal.AllocHGlobal(4);
            IntPtr output = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(input, 0, address);
                int returned;
                bool ok = DeviceIoControl(handle, 2621482120u, input, 4, output, 16, out returned, IntPtr.Zero);
                return (ok && returned > 0) ? Marshal.ReadByte(output) : -1;
            }
            finally
            {
                Marshal.FreeHGlobal(input);
                Marshal.FreeHGlobal(output);
            }
        }
        finally { CloseHandle(handle); }
    }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp

$c7cc = [EcReader]::ReadByte(1996)
$c49f = [EcReader]::ReadByte(1183)

$kind = '外部供电（方式未知）'
if ($c7cc -ge 0) {
    $round = ($c7cc -band 1) -ne 0
    $typeC = ($c7cc -band 6) -ne 0
    if ($round -and $typeC) { $kind = '圆口 + Type-C' }
    elseif ($round) { $kind = '圆口' }
    elseif ($typeC) { $kind = 'Type-C' }
}

$table = @{ 0=330; 8=230; 16=180; 24=150; 32=120; 40=90; 48=65; 56=40; 64=280; 88=420 }
    # ⚠️ 空位编码（合法取值 = (0x49F & 0x78)，即 8 的倍数）：72 / 80 / 96 / 104 / 112 / 120
    #    抓到这些值会显示「未知（不在表内）」—— 那正是我们要的新编码（例如 210W 可能是其中之一）
$code = -1
$watts = $null
if ($c49f -ge 0) {
    $code = $c49f -band 0x78
    if ($table.ContainsKey($code)) { $watts = $table[$code] }
}

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
$lines = @()
$lines += "=== L-Mechrevo EC 供电编码抓取 $stamp ==="
if ($c7cc -ge 0) {
    $lines += ('0x7CC(1996) = 0x{0:X2} -> 供电方式: {1}' -f $c7cc, $kind)
} else {
    $lines += ('0x7CC(1996) = 读取失败（ACPIDriver 不可用） -> 供电方式: {0}' -f $kind)
}
if ($c49f -ge 0) {
    if ($watts -ne $null) {
        $lines += ('0x49F(1183) = 0x{0:X2} -> 编码 0x{1:X2} -> {2}W' -f $c49f, $code, $watts)
    } else {
        $lines += ('0x49F(1183) = 0x{0:X2} -> 编码 0x{1:X2} -> 未知（不在表内）' -f $c49f, $code)
    }
} else {
    $lines += '0x49F(1183) = 读取失败（ACPIDriver 不可用）'
}
$lines += ''
$text = ($lines -join "`r`n") + "`r`n"
[System.IO.File]::AppendAllText($log, $text, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ($lines -join [Environment]::NewLine)
Write-Host ''
Write-Host ('日志已写入: ' + $log)
Write-Host '（可直接关闭本窗口）'
'''

# UTF-8 BOM 编码（PowerShell 5.1 正确识别中文）
ps1_bytes = b'\xef\xbb\xbf' + PS1.encode('utf-8')
b64 = base64.b64encode(ps1_bytes).decode('ascii')

bat = '''@echo off
title L-Mechrevo EC Power-Input Capture
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
set "LM_LOG_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$b64='__B64__'; $p=[IO.Path]::Combine($env:TEMP,'lm_ec_power_capture.ps1'); [IO.File]::WriteAllBytes($p,[Convert]::FromBase64String($b64)); & $p"
pause
'''.replace('__B64__', b64)

desktop = r'C:\Users\xiaohan\Desktop'
out_bat = os.path.join(desktop, 'EC供电编码抓取.bat')
with open(out_bat, 'w', encoding='ascii', newline='\r\n') as f:
    f.write(bat)

print('生成完成:', out_bat)
print('大小:', os.path.getsize(out_bat), 'bytes')
