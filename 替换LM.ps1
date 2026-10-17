# ============================================================
# 升级替换脚本：强杀 LM（.NET Kill，已验证有效）-> 备份 -> 替换 DLL -> 重启
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File "D:\openLM\替换LM.ps1"
#      加 -Show 才把主窗弹出来；默认走 `startup` 参数 = 最小化到托盘
# 说明：LM 受进程保护，taskkill /F 无效；Process.Kill() 可正常终止
#
# ⚠️⚠️ 2026-10-17（焦点问题定案）：**不带参数启动 = 弹主窗 + 抢焦点**
#   链：Program.cs:997 `SettingsToggle(checkForFocus:false)` -> SettingsForm.ShowAll()
#       -> 末尾 `Activate()`（SettingsForm.cs:11047）= SetForegroundWindow
#   ⇒ 上一轮会话反复"替换/重启"时，每跑一次就把焦点从用户正在用的程序抢走一次。
#   ⇒ 现在默认 `-ArgumentList "startup"`（与计划任务完全同一路径 ⇒ 最小化到托盘、不抢焦点）。
#   ⚠️ 若旧的 LM 没被彻底杀掉就起新的：新实例会敲 ShowMain 让**旧实例**弹窗（同样抢焦点）
#      ⇒ 所以下面杀进程后要确认真的没了再启动。
# ============================================================
param([switch]$Show)
$ErrorActionPreference = "Stop"
$target = "C:\Program Files\L-Mechrevo\L-Mechrevo.dll"
$new = "D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll"
$exe = "C:\Program Files\L-Mechrevo\L-Mechrevo.exe"

# 1. 强杀 LM（.NET Kill，逐个尝试，失败不中断）
Get-Process -Name "L-Mechrevo*" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); Write-Host "已杀 PID=$($_.Id)" } catch { Write-Host "杀 PID=$($_.Id) 失败: $($_.Exception.Message)" }
}
Start-Sleep -Seconds 3
if (Get-Process -Name "L-Mechrevo*" -ErrorAction SilentlyContinue) {
    Write-Host "警告：仍有 LM 进程存活，再试一轮"
    Get-Process -Name "L-Mechrevo*" -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill() } catch { }
    }
    Start-Sleep -Seconds 2
}

# 2. 备份 + 替换
if (Test-Path $target) {
    try {
        $bak = "$target.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Copy-Item $target $bak -Force
        Write-Host "已备份: $bak"
    } catch {
        Write-Host "备份失败（继续替换）: $($_.Exception.Message)"
    }
}
Copy-Item $new $target -Force
Write-Host "已替换: $target ($((Get-Item $target).Length) 字节)"
# 2.5 同步 zh-CN 卫星资源（新增资源 key 时旧卫星缺失会导致中文回退英文）
$satSrc = "D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0\zh-CN\L-Mechrevo.resources.dll"
$satDst = "C:\Program Files\L-Mechrevo\zh-CN\L-Mechrevo.resources.dll"
if (Test-Path $satSrc) {
    Copy-Item $satDst "$satDst.bak-old" -Force -ErrorAction SilentlyContinue
    Copy-Item $satSrc $satDst -Force
    Write-Host "已同步 zh-CN 卫星资源"
}

# 3. 重启 LM
#   默认：`startup` = 计划任务同一路径 ⇒ 最小化到托盘，不弹窗、不抢焦点
#   只有显式 -Show 才按"手动双击 exe"启动（会弹窗并 Activate）—— 仅当你确实要立刻看界面时才用
if ($Show) {
    Start-Process $exe
    Write-Host "已启动（-Show：主窗弹出并取得焦点）"
} else {
    Start-Process $exe -ArgumentList "startup"
    Write-Host "已启动（startup：最小化到托盘，不抢焦点）"
}
Start-Sleep -Seconds 5
$p = Get-Process -Name "L-Mechrevo*" -ErrorAction SilentlyContinue
if ($p) { Write-Host "LM 已重启: PID=$($p.Id -join ',')" } else { Write-Host "警告：LM 未能启动" }
