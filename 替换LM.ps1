# ============================================================
# 升级替换脚本：强杀 LM（.NET Kill，已验证有效）-> 备份 -> 替换 DLL -> 重启
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File "D:\openLM\替换LM.ps1"
# 说明：LM 受进程保护，taskkill /F 无效；Process.Kill() 可正常终止
# ============================================================
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
Start-Process $exe
Start-Sleep -Seconds 5
$p = Get-Process -Name "L-Mechrevo*" -ErrorAction SilentlyContinue
if ($p) { Write-Host "LM 已重启: PID=$($p.Id -join ',')" } else { Write-Host "警告：LM 未能启动" }
