# 对比"添加用户配置"导入后的系统写入
Write-Host '=== 1. 用户颜色目录 ==='
$colorDir = 'C:\Users\xiaohan\AppData\Local\Microsoft\Windows\Color'
if (Test-Path $colorDir) { Get-ChildItem $colorDir | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize } else { Write-Host 'MISS' }

Write-Host '=== 2. 其他候选颜色目录 ==='
@('C:\Users\xiaohan\AppData\Roaming\Microsoft\Windows\Color','C:\ProgramData\Microsoft\Windows\Color','C:\Users\xiaohan\AppData\Local\Microsoft\Windows\Color System') | ForEach-Object {
  if (Test-Path $_) { Write-Host ('  EXISTS: ' + $_); Get-ChildItem $_ -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name } else { Write-Host ('  no: ' + $_) }
}

Write-Host '=== 3. HKCU ICM 全结构 ==='
$icm = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM'
if (Test-Path $icm) {
  Get-ChildItem $icm -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    $props = (Get-Item $_.PSPath -ErrorAction SilentlyContinue).Property
    $vs = @()
    foreach ($p in $props) { $v=[string](Get-ItemPropertyValue -Path $_.PSPath -Name $p -ErrorAction SilentlyContinue); if($v.Length -gt 90){$v=$v.Substring(0,90)+'...'}; $vs += ($p+'=['+$v+']') }
    Write-Host ('  ' + $_.Name.Replace('HKEY_CURRENT_USER\','') + ' :: ' + ($vs -join ' | '))
  }
}

Write-Host '=== 4. spool 颜色目录 ==='
Get-ChildItem 'C:\Windows\System32\spool\drivers\color' -ErrorAction SilentlyContinue | Select-Object Name,Length,LastWriteTime | Sort-Object LastWriteTime -Descending | Select-Object -First 6 | Format-Table -AutoSize
