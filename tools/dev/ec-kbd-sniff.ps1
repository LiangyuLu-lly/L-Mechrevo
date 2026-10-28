# EC 寄存器变化嗅探（只读 · ACPIDriver）
#
# 用途：在「官方控制台」里操作键盘灯（开/关、亮度、灯效），实时抓出 EC 哪些地址在变。
#      只发读命令，不写任何寄存器；需要管理员权限（\\.\ACPIDriver）。
#
# 日志：默认写到「桌面\EC抓取_yyyyMMdd_HHmmss.txt」（任何情况下都会写，包括失败/无变化）。
#
# 用法（管理员 PowerShell）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File ec-kbd-sniff.ps1
#   powershell ... -File ec-kbd-sniff.ps1 -Start 0x830 -End 0x86F -IntervalMs 500 -Seconds 180
#   powershell ... -File ec-kbd-sniff.ps1 -DumpAll        # 额外把初始快照全部写进日志
#
# 建议操作：脚本开始后，等它打印「开始抓取」，再操作官方控制台键盘灯，一次一个动作、间隔 5 秒。

param(
    [int]$Start = 0x700,
    [int]$End = 0x8FF,
    [int]$IntervalMs = 500,
    [int]$Seconds = 180,
    [string]$LogPath = "",
    [switch]$DumpAll
)

$ErrorActionPreference = "Continue"

# ---- 日志路径：默认桌面，任何情况下都先建文件 ----
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path -LiteralPath $desktop)) {
        $desktop = Split-Path -Parent $PSCommandPath
    }
    $LogPath = Join-Path $desktop ("EC抓取_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")
}
try {
    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    Set-Content -LiteralPath $LogPath -Value ("EC 抓取日志 · " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
} catch {
    $LogPath = Join-Path $env:TEMP ("EC抓取_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")
    Set-Content -LiteralPath $LogPath -Value ("EC 抓取日志（桌面不可写，落 TEMP）· " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
}

function Log([string]$msg) {
    $line = ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $msg)
    Write-Host $line
    try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 } catch { }
}

Log ("=== EC 变化嗅探 · 0x{0:X4}~0x{1:X4} · 每 {2}ms 一轮 · 共 {3}s ===" -f $Start, $End, $IntervalMs, $Seconds)
Log ("脚本：{0}" -f $PSCommandPath)
Log ("日志：{0}" -f $LogPath)
Log "只读：不写任何寄存器。"

# ---- 管理员检查 ----
try {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Log "不是管理员 ⇒ 退出。请右键「以管理员身份运行」，或从管理员 PowerShell 再执行本脚本。"
        Write-Host ("日志已写入：" + $LogPath) -ForegroundColor Yellow
        exit 5
    }
} catch {
    Log ("管理员检查失败（继续尝试）：" + $_.Exception.Message)
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
    public static int LastError = 0;
    public static IntPtr Open()
    {
        IntPtr h = CreateFile(DevicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        LastError = Marshal.GetLastWin32Error();
        return h;
    }
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
    Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop | Out-Null
} catch {
    Log ("编译读 EC 代码失败：" + $_.Exception.Message)
    Write-Host ("日志已写入：" + $LogPath) -ForegroundColor Yellow
    exit 3
}

$handle = [AcpiEcSniff]::Open()
if ($handle -eq [IntPtr]::Zero -or $handle.ToInt64() -eq -1) {
    Log ("打不开 \\.\ACPIDriver（Win32 错误=" + [AcpiEcSniff]::LastError + "）⇒ 该机没装/没运行 ACPIDriver 驱动。")
    Write-Host ("日志已写入：" + $LogPath) -ForegroundColor Yellow
    exit 6
}
Log "已打开 \\\\.\\ACPIDriver，开始读取初始快照……"

$prev = @{}
$readable = 0
for ($a = $Start; $a -le $End; $a++) {
    $v = [AcpiEcSniff]::Read($handle, $a)
    if ($v -ge 0) {
        $prev[$a] = $v
        $readable++
        if ($DumpAll) { Log ("  初始 0x{0:X4} = 0x{1:X2}" -f $a, $v) }
    }
}
Log ("初始快照：{0} 个地址可读（范围 0x{1:X4}~0x{2:X4}）" -f $readable, $Start, $End)
if ($readable -eq 0) {
    Log "全部读不到：这条 EC 通道不通（该机可能不带 ACPIDriver 的 EC 映射），请把这一行反馈给维护者。"
}
Log "开始抓取：请现在操作官方控制台键盘灯（开/关、亮度、效果），一次一个动作、间隔 5 秒。"

$changes = 0
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    for ($a = $Start; $a -le $End; $a++) {
        $v = [AcpiEcSniff]::Read($handle, $a)
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

[AcpiEcSniff]::Close($handle)
Log ("=== 结束：共 {0} 处变化 ===" -f $changes)
if ($changes -eq 0) {
    Log "本次没有抓到任何 EC 变化：① 若操作确实生效，试着把范围放大（-Start 0x400 -End 0x8FF）或加快采样（-IntervalMs 200）；② 也可能键盘灯不走 EC（走 ACPI/WMI 或 HID）。"
}
Write-Host ("日志已写入：" + $LogPath) -ForegroundColor Green
