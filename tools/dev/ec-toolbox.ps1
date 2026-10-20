# EC 水冷 / 功耗 实验工具箱（菜单式）
#   1) 只读体检   2) 水冷高性能 开   3) 水冷高性能 关/还原   4) 监视   0) 退出
#   自动提权；原值记录在 %TEMP%\ec-lc-hwoc-state.json；日志 %TEMP%\ec-toolbox.txt
$ErrorActionPreference = 'Continue'
$script:Log = Join-Path $env:TEMP 'ec-toolbox.txt'
$script:State = Join-Path $env:TEMP 'ec-lc-hwoc-state.json'
$ADDR_LC = 1991   # 0x7C7 bit1 = 水冷已连接
$ADDR_DB = 0x746
$ADDR_DB2 = 0x7D5
$ADDR_CTRL = 0x743

$me = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($me)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
            -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"') -Verb RunAs | Out-Null
        Write-Host '已请求管理员权限，请在新窗口继续…'
    } catch { Write-Host ('提权失败：' + $_.Exception.Message) }
    exit 0
}

$src = @'
using System;
using System.Runtime.InteropServices;
public static class TbEc {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr CreateFile(string n,uint a,uint s,IntPtr sec,uint d,uint f,IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(IntPtr h,uint c,IntPtr i,int isz,IntPtr o,int osz,out int r,IntPtr ov);
  const string P = "\\\\.\\ACPIDriver";
  public static IntPtr Open(){ return CreateFile(P,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero); }
  public static void Close(IntPtr h){ CloseHandle(h); }
  public static int Read(IntPtr h,int a){ IntPtr i=Marshal.AllocHGlobal(4), o=Marshal.AllocHGlobal(16);
    try{ Marshal.WriteInt32(i,0,a); int r; if(!DeviceIoControl(h,2621482120,i,4,o,16,out r,IntPtr.Zero)||r<=0) return -1; return Marshal.ReadByte(o);} finally{Marshal.FreeHGlobal(i);Marshal.FreeHGlobal(o);} }
  public static bool Write(IntPtr h,int a,byte v){ IntPtr i=Marshal.AllocHGlobal(8), o=Marshal.AllocHGlobal(16);
    try{ Marshal.WriteInt32(i,0,a); Marshal.WriteByte(i,4,v); int r; return DeviceIoControl(h,2621482124,i,5,o,16,out r,IntPtr.Zero);} finally{Marshal.FreeHGlobal(i);Marshal.FreeHGlobal(o);} }
}
'@
Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop

function WLog([string]$m) {
    $line = ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss.fff'), $m)
    Write-Host $line
    try { Add-Content -LiteralPath $script:Log -Value $line -Encoding UTF8 } catch { }
}
function GpuLine {
    $smi = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
    $l = & $smi --query-gpu=enforced.power.limit,power.draw,temperature.gpu --format=csv,noheader 2>$null
    if ($l) { return $l.Trim() } else { return 'n/a' }
}
function ReadEc($h, $addr) { return [TbEc]::Read($h, [int]$addr) }
function Health($h) {
    $v = ReadEc $h $ADDR_LC
    WLog ("EC[0x7C7]=0x{0:X2}  bit1(水冷已连接)={1}" -f $v, (($v -band 2) -ne 0))
    WLog ('邻近：' + ((0x7C0..0x7CF | ForEach-Object { $x=ReadEc $h $_; '0x{0:X3}=0x{1:X2}' -f $_, $x }) -join ' '))
    WLog ("DB：0x743=0x{0:X2} 0x746=0x{1:X2} 0x7D5=0x{2:X2}" -f (ReadEc $h $ADDR_CTRL), (ReadEc $h $ADDR_DB), (ReadEc $h $ADDR_DB2))
    WLog ("GPU: " + (GpuLine))
    WLog 'PL1/PL2 不在 EC 0x7xx（由 GCU 经 MQTT Fan/Status 发布；LM 界面显示的即为该值）'
}
function EnsureState($h) {
    if (-not (Test-Path $script:State)) {
        $v = ReadEc $h $ADDR_LC
        @{ Original = $v; At = (Get-Date).ToString('s') } | ConvertTo-Json | Set-Content -LiteralPath $script:State -Encoding UTF8
        WLog ("记录原值 0x{0:X2} ⇒ {1}" -f $v, $script:State)
    }
}
function LcOn($h) {
    EnsureState $h
    $v = ReadEc $h $ADDR_LC
    $t = [byte]($v -bor 0x02)
    $ok = [TbEc]::Write($h, $ADDR_LC, $t)
    Start-Sleep -Milliseconds 500
    $rb = ReadEc $h $ADDR_LC
    WLog ("写 0x7C7=0x{0:X2}（调用={1}）回读=0x{2:X2} bit1={3}" -f $t, $ok, $rb, (($rb -band 2) -ne 0))
    WLog ("写后 GPU: " + (GpuLine))
}
function LcOff($h) {
    if (Test-Path $script:State) {
        $st = Get-Content -LiteralPath $script:State -Raw -Encoding UTF8 | ConvertFrom-Json
        $ok = [TbEc]::Write($h, $ADDR_LC, [byte]$st.Original)
        Start-Sleep -Milliseconds 400
        WLog ("还原 0x7C7=0x{0:X2}（写={1}）回读=0x{2:X2}" -f $st.Original, $ok, (ReadEc $h $ADDR_LC))
        Remove-Item $script:State -ErrorAction SilentlyContinue
    } else {
        $v = ReadEc $h $ADDR_LC
        $ok = [TbEc]::Write($h, $ADDR_LC, [byte]($v -band 0xFD))
        Start-Sleep -Milliseconds 400
        WLog ("清 bit1 0x7C7=0x{0:X2} 回读=0x{1:X2}" -f $v, (ReadEc $h $ADDR_LC))
    }
}
function Watch($h, [int]$sec) {
    Write-Host '（监视中：按回车可跳过）'
    $deadline = (Get-Date).AddSeconds($sec)
    $prev = ''
    $nextSample = Get-Date
    while ((Get-Date) -lt $deadline) {
        # 回车跳过：每 150ms 检查一次键盘，不必等满整段监视
        try {
            while ([Console]::KeyAvailable) {
                $k = [Console]::ReadKey($true)
                if ($k.Key -eq [ConsoleKey]::Enter) {
                    WLog '用户按回车 ⇒ 跳过监视'
                    return
                }
            }
        } catch { }
        if ((Get-Date) -ge $nextSample) {
            $cur = (GpuLine) + (' · 0x7C7=0x{0:X2}' -f (ReadEc $h $ADDR_LC))
            if ($cur -ne $prev) { WLog ('  ' + $cur); $prev = $cur }
            $nextSample = (Get-Date).AddSeconds(1)
        }
        Start-Sleep -Milliseconds 150
    }
}

$h = [TbEc]::Open()
if ($h -eq [IntPtr]::Zero -or $h.ToInt64() -eq -1) { WLog '打不开 ACPIDriver（需要管理员 + 已装 UWACPIDriver）'; Read-Host '回车退出' | Out-Null; exit 6 }
try {
    while ($true) {
        Write-Host ''
        Write-Host '===== EC 水冷 / 功耗 工具箱 ====='
        Write-Host ' 1) 只读体检（0x7C7 + 邻近 + DB + GPU）'
        Write-Host ' 2) 水冷高性能：开（置 0x7C7 bit1=1，20s 监视）'
        Write-Host ' 3) 水冷高性能：关 / 还原原值'
        Write-Host ' 4) 只监视 30s（功耗/温度/0x7C7）'
        Write-Host ' 0) 退出'
        $c = Read-Host '输入数字后按回车'
        switch ($c) {
            '1' { Health $h }
            '2' { LcOn $h; Watch $h 20; WLog '完成；如需恢复选 3（或重启自动还原）' }
            '3' { LcOff $h }
            '4' { Watch $h 30 }
            '0' { break }
            default { Write-Host '无效选项' }
        }
        if ($c -eq '0') { break }
    }
}
finally { [TbEc]::Close($h) }
