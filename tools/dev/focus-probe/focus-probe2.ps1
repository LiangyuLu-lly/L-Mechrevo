# 焦点探针 ②：分场景测"LM 什么时候把前台抢走"
#   launch-away : 启动 LM → 2.2s 后主动切到记事本 → 再盯 12s（测预热"隐形 Show"会不会把焦点抢回去）
#   idle        : LM 已在跑（主窗已开）→ 切到记事本 → 盯 30s（测周期刷新/toast 会不会抢）
#   tray        : LM 已在跑 → 切到记事本 → 用 ShowMain 事件唤起主窗（模拟托盘双击）
param(
  [ValidateSet('launch-away','idle','tray')]
  [string]$Scenario = 'launch-away',
  [int]$Seconds = 12
)

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Fg2 {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

function Get-FgInfo2 {
  $h = [Fg2]::GetForegroundWindow()
  $pid2 = 0
  [void][Fg2]::GetWindowThreadProcessId($h, [ref]$pid2)
  $sb = New-Object System.Text.StringBuilder 512
  [void][Fg2]::GetWindowText($h, $sb, $sb.Capacity)
  $name = '(unknown)'
  try { $name = (Get-Process -Id $pid2 -ErrorAction Stop).ProcessName } catch { }
  [pscustomobject]@{ Hwnd = $h; Pid = $pid2; Proc = $name; Title = $sb.ToString() }
}

function Open-Target {
  $np = Start-Process notepad -PassThru
  Start-Sleep -Milliseconds 1200
  $np.Refresh()
  [void][Fg2]::SetForegroundWindow($np.MainWindowHandle)
  Start-Sleep -Milliseconds 400
  return $np
}

function Watch($seconds, $tag) {
  $t0 = [Environment]::TickCount64
  $last = (Get-FgInfo2).Hwnd
  $events = New-Object System.Collections.Generic.List[object]
  for ($i = 0; $i -lt ($seconds * 10); $i++) {
    Start-Sleep -Milliseconds 100
    $fg = Get-FgInfo2
    if ($fg.Hwnd -ne $last) {
      $events.Add([pscustomobject]@{
        Ms = [Environment]::TickCount64 - $t0; At = (Get-Date -Format 'HH:mm:ss.fff'); Proc = $fg.Proc; Pid = $fg.Pid; Title = $fg.Title
      })
      $last = $fg.Hwnd
    }
  }
  Write-Output ""
  Write-Output ("[{0}] 盯了 {1}s，前台变化 {2} 次" -f $tag, $seconds, $events.Count)
  $events | ForEach-Object { Write-Output ("  +{0,5} ms [{1}] -> {2} (PID {3})「{4}」" -f $_.Ms, $_.At, $_.Proc, $_.Pid, $_.Title) }
  $endInfo = Get-FgInfo2
  Write-Output ("  结束前台：{0}「{1}」" -f $endInfo.Proc, $endInfo.Title)
  $stolen = @($events | Where-Object { $_.Proc -like 'L-Mechrevo*' })
  if ($stolen.Count -gt 0) { Write-Output "  ❌ 被 LM 抢走 $($stolen.Count) 次" }
  elseif ($events.Count -eq 0) { Write-Output "  ✅ 没变化" }
  else { Write-Output "  ⚠️ 变了但不是 LM" }
}

$exe = 'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'

switch ($Scenario) {
  'launch-away' {
    Get-Process -Name 'L-Mechrevo*' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    Start-Sleep -Seconds 2
    $lm = Start-Process $exe -PassThru
    Start-Sleep -Milliseconds 2200
    $np = Open-Target
    Write-Output ("基线（启动 2.2s 后切走）：{0}" -f (Get-FgInfo2).Proc)
    Watch $Seconds 'launch-away'
    try { $np.CloseMainWindow() | Out-Null } catch { }
  }
  'idle' {
    if (-not (Get-Process -Name 'L-Mechrevo*' -ErrorAction SilentlyContinue)) {
      Write-Output "LM 没在跑，先启动一次"
      Start-Process $exe | Out-Null
      Start-Sleep -Seconds 6
    }
    $np = Open-Target
    Write-Output ("基线（切走）：{0}" -f (Get-FgInfo2).Proc)
    Watch $Seconds 'idle'
    try { $np.CloseMainWindow() | Out-Null } catch { }
  }
  'tray' {
    $np = Open-Target
    Write-Output ("基线（切走）：{0}" -f (Get-FgInfo2).Proc)
    $ev = $null
    try {
      $ev = [System.Threading.EventWaitHandle]::OpenExisting('Global\L-Mechrevo-ShowMain')
      [void]$ev.Set()
      Write-Output "已敲 ShowMain 事件"
    } catch { Write-Output "打开 ShowMain 事件失败：$($_.Exception.Message)" }
    Start-Sleep -Milliseconds 800
    $fg = Get-FgInfo2
    Write-Output ("敲完后前台：{0}「{1}」" -f $fg.Proc, $fg.Title)
    if ($ev) { $ev.Dispose() }
    try { $np.CloseMainWindow() | Out-Null } catch { }
  }
}
