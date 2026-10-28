# EC 水冷高性能（LC / HWOC）独立实验脚本 —— 读取 + 写入一步完成
#   EC 0x7C7 (1991)：bit1 = 水冷已连接（GCU 的 LiquidCooling.SetECSupport 写的就是这一位）
#   用法（管理员）：
#     powershell -NoProfile -ExecutionPolicy Bypass -File ec-lc-hwoc.ps1              # 只读
#     ... -Enable                                                                     # 置 bit1=1 并回读 + 监视
#     ... -Disable                                                                    # 清 bit1
#     ... -Restore                                                                    # 恢复进入本脚本后第一次记录的原值
#   写前记录原值到 %TEMP%\ec-lc-hwoc-state.json；退出/重启/断电后 EC 自动回原值。
param(
    [switch]$Enable,
    [switch]$Disable,
    [switch]$Restore,
    [int]$MonitorSeconds = 20,
    [string]$StatePath = (Join-Path $env:TEMP 'ec-lc-hwoc-state.json')
)

$ErrorActionPreference = 'Continue'
# 自动提权：非管理员时用 RunAs 重新启动自己（保留全部参数），原进程退出
$__me = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($__me)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argLine = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    foreach ($k in $PSBoundParameters.Keys) {
        $val = $PSBoundParameters[$k]
        if ($val -is [switch]) { if ($val.IsPresent) { $argLine += ' -' + $k } }
        else { $argLine += ' -' + $k + ' "' + $val + '"' }
    }
    try {
        Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $argLine -Verb RunAs | Out-Null
        Write-Host '已请求管理员权限（新窗口继续）…'
    } catch {
        Write-Host ('提权被拒绝：' + $_.Exception.Message)
    }
    exit 0
}
$script:LogFile = Join-Path $env:TEMP 'ec-lc-hwoc.txt'
Remove-Item $script:LogFile -ErrorAction SilentlyContinue
function Log([string]$m) {
    $line = ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss.fff'), $m)
    Write-Host $line
    try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

$src = @'
using System;
using System.Runtime.InteropServices;
public static class LcEcRw {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr CreateFile(string n,uint a,uint s,IntPtr sec,uint d,uint f,IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(IntPtr h,uint c,IntPtr i,int isz,IntPtr o,int osz,out int r,IntPtr ov);
  const string P = "\\\\.\\ACPIDriver";
  public static IntPtr Open(){ return CreateFile(P,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero); }
  public static void Close(IntPtr h){ CloseHandle(h); }
  public static int Read(IntPtr h,int a){
    IntPtr i=Marshal.AllocHGlobal(4), o=Marshal.AllocHGlobal(16);
    try{ Marshal.WriteInt32(i,0,a); int r; if(!DeviceIoControl(h,2621482120,i,4,o,16,out r,IntPtr.Zero)||r<=0) return -1; return Marshal.ReadByte(o);} finally{Marshal.FreeHGlobal(i);Marshal.FreeHGlobal(o);} }
  public static bool Write(IntPtr h,int a,byte v){
    IntPtr i=Marshal.AllocHGlobal(8), o=Marshal.AllocHGlobal(16);
    try{ Marshal.WriteInt32(i,0,a); Marshal.WriteByte(i,4,v); int r; return DeviceIoControl(h,2621482124,i,5,o,16,out r,IntPtr.Zero);} finally{Marshal.FreeHGlobal(i);Marshal.FreeHGlobal(o);} }
}
'@
Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop

$ADDR = 1991  # 0x7C7
function GpuLine {
    $smi = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
    $l = & $smi --query-gpu=enforced.power.limit,power.draw,temperature.gpu --format=csv,noheader 2>$null
    if ($l) { return $l.Trim() } else { return 'n/a' }
}
function WmiCondProbe {
    try {
        $inst = Get-CimInstance -Namespace root\WMI -ClassName AcpiTest_MULong -ErrorAction Stop | Select-Object -First 1
        $data = ([uint64]0x0100 -shl 32) -bor [uint64]0xD6BF
        $r = Invoke-CimMethod -InputObject $inst -MethodName GetSetULong -Arguments @{ Data = $data } -ErrorAction Stop
        return ('WMI mailbox[0xD6BF] = 0x{0:X8}' -f [uint32]$r.Return)
    } catch { return ('WMI mailbox 不可用：' + $_.Exception.Message) }
}

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Log '不是管理员 ⇒ 退出（请用管理员 PowerShell 运行）'; exit 5
}
$h = [LcEcRw]::Open()
if ($h -eq [IntPtr]::Zero -or $h.ToInt64() -eq -1) { Log '打不开 ACPIDriver ⇒ 退出'; exit 6 }
try {
    $v = [LcEcRw]::Read($h, $ADDR)
    if ($v -lt 0) { Log ("读 0x7C7 失败") ; exit 7 }
    Log ("EC[0x7C7]=0x{0:X2}  bit1(水冷已连接)={1}  GPU[{2}]" -f $v, (($v -band 2) -ne 0), (GpuLine))
    Log ('邻近值：' + (0x7C0..0x7CF | ForEach-Object { $x=[LcEcRw]::Read($h,$_); ('0x{0:X3}=0x{1:X2}' -f $_, $x) }) -join ' ')
    Log (WmiCondProbe)

    if ($Restore) {
        if (-not (Test-Path $StatePath)) { Log '没有原值记录，无法 Restore'; exit 8 }
        $st = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $ok = [LcEcRw]::Write($h, $ADDR, [byte]$st.Original)
        Start-Sleep -Milliseconds 400
        Log ("Restore 0x7C7 => 0x{0:X2}（写调用={1}，回读=0x{2:X2}）" -f $st.Original, $ok, ([LcEcRw]::Read($h,$ADDR)))
        exit 0
    }
    if (-not $Enable -and -not $Disable) {
        Log '只读完成（要写入请加 -Enable / -Disable）'; exit 0
    }

    if (-not (Test-Path $StatePath)) {
        @{ Original = $v; At = (Get-Date).ToString('s') } | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding UTF8
        Log ("已记录原值 0x{0:X2} 到 {1}" -f $v, $StatePath)
    }
    $target = if ($Enable) { [byte]($v -bor 0x02) } else { [byte]($v -band 0xFD) }
    $w = [LcEcRw]::Write($h, $ADDR, $target)
    Start-Sleep -Milliseconds 500
    $rb = [LcEcRw]::Read($h, $ADDR)
    Log ("写 0x7C7=0x{0:X2}（调用={1}）回读=0x{2:X2} bit1={3}" -f $target, $w, $rb, (($rb -band 2) -ne 0))
    Log ("写后 GPU[{0}]" -f (GpuLine))
    $deadline = (Get-Date).AddSeconds($MonitorSeconds)
    $prev = ''
    while ((Get-Date) -lt $deadline) {
        $cur = (GpuLine) + (' · 0x7C7=0x{0:X2}' -f ([LcEcRw]::Read($h,$ADDR)))
        if ($cur -ne $prev) { Log ('  ' + $cur); $prev = $cur }
        Start-Sleep -Seconds 1
    }
    Log '完成。恢复：本脚本 -Restore（或重启/断电自动还原）'
    exit 0
}
finally { [LcEcRw]::Close($h) }
