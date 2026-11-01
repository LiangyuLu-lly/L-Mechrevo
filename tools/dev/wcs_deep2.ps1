# 精简探测：只读显示器类+适配器类槽位（不递归 Configuration）
$out = 'D:\openLM\tools\wcs_deep2.txt'
$sb = New-Object System.Text.StringBuilder

function Dump-Sub([string]$path) {
  if (-not (Test-Path $path)) { [void]$sb.AppendLine('MISS: ' + $path); return }
  Get-ChildItem $path -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^000\d+$' } | ForEach-Object {
    $k = $_.PSPath
    $props = (Get-Item $k).Property
    $interesting = @()
    foreach ($prop in $props) {
      if ($prop -match 'MDP|ICM|Profile|Color') {
        $v = [string](Get-ItemPropertyValue -Path $k -Name $prop -ErrorAction SilentlyContinue)
        if ($v.Length -gt 100) { $v = $v.Substring(0, 100) + '...' }
        $interesting += ($prop + '=[' + $v + ']')
      }
    }
    [void]$sb.AppendLine('  ' + $_.PSChildName + ' :: ' + ($interesting -join ' | '))
  }
}

[void]$sb.AppendLine('=== Monitor class {4d36e96e} ===')
Dump-Sub 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96e-e325-11ce-bfc1-08002be10318}'
[void]$sb.AppendLine('=== Adapter class {4d36e968} ===')
Dump-Sub 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
[void]$sb.AppendLine('=== ICM HKLM tree (top) ===')
Dump-Sub 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM'

[IO.File]::WriteAllText($out, $sb.ToString(), [Text.Encoding]::UTF8)
Write-Host 'done'
