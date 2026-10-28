$ErrorActionPreference = 'Stop'
$log = 'D:\openLM\deploy_vblank.log'
$src='D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0'
$inst='C:\Program Files\L-Mechrevo'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
try {
    taskkill /F /T /IM L-Mechrevo.exe 2>$null | Out-Null
    Start-Sleep -Seconds 2
    Copy-Item 'D:\Downloads\NullSignal_win_amd64\usbmmidd_v2' "$inst\usbmmidd_v2" -Recurse -Force
    Copy-Item "$inst\L-Mechrevo.dll" "$inst\L-Mechrevo.dll.bak_vblank-$stamp" -Force
    Copy-Item "$src\L-Mechrevo.dll" "$inst\L-Mechrevo.dll" -Force
    $satSrc="$src\zh-CN\L-Mechrevo.Properties.Strings.resources.dll"
    if(Test-Path $satSrc){ New-Item "$inst\zh-CN" -ItemType Directory -Force | Out-Null; Copy-Item $satSrc "$inst\zh-CN\L-Mechrevo.Properties.Strings.resources.dll" -Force; 'satellite synced' | Add-Content $log }
    Start-Process "$inst\L-Mechrevo.exe"
    Start-Sleep -Seconds 3
    $proc = Get-Process -Name 'L-Mechrevo' -ErrorAction SilentlyContinue
    "DLL deployed: $((Get-Item "$inst\L-Mechrevo.dll").Length) bytes, LM pid=$($proc.Id), driver files: $((Get-ChildItem "$inst\usbmmidd_v2" -Recurse -File).Count)" | Add-Content $log
    "OK $(Get-Date -Format 'HH:mm:ss')" | Add-Content $log
} catch {
    "FAIL: $_" | Add-Content $log
}
