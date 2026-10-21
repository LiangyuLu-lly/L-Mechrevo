# EC 寄存器变化嗅探（只读 · ACPIDriver）
#
# 用途：在「官方控制台」里操作键盘灯（开/关、亮度、灯效），实时抓出 EC 哪些地址在变。
#      只发读命令，不写任何寄存器；需要管理员权限（\\.\ACPIDriver）。
#
# 用法（管理员 PowerShell）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File ec-kbd-sniff.ps1
#   powershell ... -File ec-kbd-sniff.ps1 -Start 0x830 -End 0x86F -IntervalMs 1000 -Seconds 180
#
# 建议操作：脚本开始后，等 5 秒再打开官方控制台，然后【一次只做一个动作，间隔 5 秒】：
#   1. 打开键盘灯效页   2. 开灯   3. 调亮度 +   4. 调亮度 -   5. 换灯效   6. 关灯
# 结束后看日志里 0x???? 的 旧值 -> 新值，那几个地址就是键盘灯相关寄存器。

param(
    [int]$Start = 0x830,
    [int]$End = 0x8FF,
    [int]$IntervalMs = 1000,
    [int]$Seconds = 150,
    [string]$LogPath = ""
)

$ErrorActionPreference = "Continue"
$script:LogFile = if ($LogPath) { $LogPath } else { Join-Path $env:TEMP 'ec-kbd-sniff.txt' }
Remove-Item $script:LogFile -ErrorAction SilentlyContinue

function Log([string]$msg) {
    $line = ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $msg)
    Write-Host $line
    try { Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

$src = @'
using System;
using System.Runtime.InteropServices;
public static class AcpiEcSniff
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int ret, IntPtr ov);
    const string DevicePath = "\\\\.\\ACPIDriver";
    public static IntPtr Open() { return CreateFile(DevicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); }
    public static void Close(IntPtr h) { CloseHandle(h); }
    public static int Read(IntPtr h, int address)
    {
        IntPtr inBuf = Marshal.AllocHGlobal(4);
        IntPtr outBuf = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(inBuf, 0, address);
            int ret;
            if (!DeviceIoControl(h, 2621482120, inBuf, 4, outBuf, 16, out ret, IntPtr.Zero) || ret <= 0) return -1;
            return Marshal.ReadByte(outBuf);
        }
        finally { Marshal.FreeHGlobal(inBuf); Marshal.FreeHGlobal(outBuf); }
    }
}
'@

try {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "不是管理员 ⇒ 请右键「以管理员身份运行 PowerShell」再执行本脚本" -ForegroundColor Yellow
        exit 5
    }

    Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop | Out-Null
    $h = [AcpiEcSniff]::Open()
    if ($h -eq [IntPtr]::Zero -or $h.ToInt64() -eq -1) {
        Write-Host "打不开 \\.\ACPIDriver ⇒ 该机没装/没运行 ACPIDriver 驱动" -ForegroundColor Yellow
        exit 6
    }

    Log ("=== EC 变化嗅探 · 0x{0:X4}~0x{1:X4} · 每 {2}ms 一轮 · 共 {3}s ===" -f $Start, $End, $IntervalMs, $Seconds)
    Log "只读：不写任何寄存器。请现在打开官方控制台，按“一次一个动作、间隔 5 秒”操作键盘灯。"

    $prev = @{}
    $readable = 0
    for ($a = $Start; $a -le $End; $a++) {
        $v = [AcpiEcSniff]::Read($h, $a)
        if ($v -ge 0) { $prev[$a] = $v; $readable++ }
    }
    Log ("初始快照：{0} 个地址可读" -f $readable)
    if ($readable -eq 0) {
        Log "全部读不到 ⇒ 这条通道不通（可能该机 EC 范围不同/驱动不支持），先反馈这一行"
    }

    $changes = 0
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        for ($a = $Start; $a -le $End; $a++) {
            $v = [AcpiEcSniff]::Read($h, $a)
            if ($v -lt 0) { continue }
            if ($prev.ContainsKey($a)) {
                if ($prev[$a] -ne $v) {
                    Log ("0x{0:X4}: 0x{1:X2} -> 0x{2:X2}" -f $a, $prev[$a], $v)
                    $prev[$a] = $v
                    $changes++
                }
            } else {
                $prev[$a] = $v
            }
        }
        Start-Sleep -Milliseconds $IntervalMs
    }

    Log ("=== 结束：共 {0} 处变化 · 日志 = {1} ===" -f $changes, $script:LogFile)
    [AcpiEcSniff]::Close($h)
}
catch {
    Write-Host ("执行失败：" + $_.Exception.Message) -ForegroundColor Red
    exit 7
}
