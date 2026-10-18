# 焦点探针：LM 启动期间，前台窗口会不会被抢走？
# 用法：pwsh -File tools\dev\focus-probe\focus-probe.ps1 [-Seconds 16]
param([int]$Seconds = 16)

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Fg {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
'@

function Get-FgInfo {
  $h = [Fg]::GetForegroundWindow()
  $pid2 = 0
  [void][Fg]::GetWindowThreadProcessId($h, [ref]$pid2)
  $sb = New-Object System.Text.StringBuilder 512
  [void][Fg]::GetWindowText($h, $sb, $sb.Capacity)
  $name = '(unknown)'
  try { $name = (Get-Process -Id $pid2 -ErrorAction Stop).ProcessName } catch { }
  [pscustomobject]@{ Hwnd = $h; Pid = $pid2; Proc = $name; Title = $sb.ToString() }
}

# 1) 关掉正在跑的 LM（与 替换LM.ps1 同款：Process.Kill）
Get-Process -Name 'L-Mechrevo*' -ErrorAction SilentlyContinue | ForEach-Object {
  try { $_.Kill() } catch { }
}
Start-Sleep -Seconds 2

# 2) 起一个记事本当前台"靶子"
$np = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1200
$np.Refresh()
[void][Fg]::SetForegroundWindow($np.MainWindowHandle)
Start-Sleep -Milliseconds 400
$base = Get-FgInfo
Write-Output ("基线前台：{0} (PID {1})「{2}」" -f $base.Proc, $base.Pid, $base.Title)
if ($base.Pid -ne $np.Id) {
  Write-Output "⚠️ 基线不是记事本（可能被系统拒绝置前）—— 结果仅供参照"
}

# 3) 启动 LM，逐 100 ms 采样前台窗口
$exe = 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'
$lm = Start-Process $exe -PassThru
$t0 = [Environment]::TickCount64
$samples = New-Object System.Collections.Generic.List[object]
$last = $base.Hwnd
$changes = 0
for ($i = 0; $i -lt ($Seconds * 10); $i++) {
  Start-Sleep -Milliseconds 100
  $fg = Get-FgInfo
  if ($fg.Hwnd -ne $last) {
    $changes++
    $samples.Add([pscustomobject]@{
      Ms   = [Environment]::TickCount64 - $t0
      Proc = $fg.Proc
      Pid  = $fg.Pid
      Title = $fg.Title
    })
    $last = $fg.Hwnd
  }
}
$end = Get-FgInfo

Write-Output ""
Write-Output ("采了 {0} 秒，前台切换 {1} 次" -f $Seconds, $changes)
$samples | ForEach-Object { Write-Output ("  +{0,5} ms -> {1} (PID {2})「{3}」" -f $_.Ms, $_.Proc, $_.Pid, $_.Title) }
Write-Output ("结束时前台：{0} (PID {1})「{2}」" -f $end.Proc, $end.Pid, $end.Title)

if ($changes -eq 0) { Write-Output "✅ 启动期间前台没变（记事本一直是前台）" }
else {
  $stolen = $samples | Where-Object { $_.Proc -like 'L-Mechrevo*' }
  if ($stolen) { Write-Output "❌ 启动期间前台被 LM 抢走 $($stolen.Count) 次" } else { Write-Output "⚠️ 前台变了，但没变成 LM" }
}

# 4) 收尾：关记事本
try { $np.CloseMainWindow() | Out-Null } catch { }
