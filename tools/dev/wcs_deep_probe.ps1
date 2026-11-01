# 深度探测：Win11 颜色管理实际存储（提权）
$out = 'D:\openLM\tools\wcs_deep_dump.txt'
$sb = New-Object System.Text.StringBuilder

function Dump-Sub([string]$path, [string]$indent) {
  if (-not (Test-Path $path)) { [void]$sb.AppendLine($indent + 'MISS: ' + $path); return }
  Get-ChildItem $path -ErrorAction SilentlyContinue | ForEach-Object {
    $k = $_.PSPath
    $props = (Get-Item $k).Property
    $interesting = @()
    foreach ($prop in $props) {
      $v = (Get-ItemPropertyValue -Path $k -Name $prop -ErrorAction SilentlyContinue)
      $vs = [string]$v
      if ($vs.Length -gt 80) { $vs = $vs.Substring(0, 80) + '...' }
      $interesting += ($prop + '=[' + $vs + ']')
    }
    [void]$sb.AppendLine($indent + $_.PSChildName + ' :: ' + ($interesting -join ' | '))
  }
}

[void]$sb.AppendLine('=== Monitor class {4d36e96e} ===')
Dump-Sub 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96e-e325-11ce-bfc1-08002be10318}' '  '

[void]$sb.AppendLine('=== Adapter class {4d36e968} ===')
Dump-Sub 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' '  '

[void]$sb.AppendLine('=== GraphicsDrivers Configuration ===')
Dump-Sub 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration' '  '

[IO.File]::WriteAllText($out, $sb.ToString(), [Text.Encoding]::UTF8)
Write-Host 'dumped to ' $out
