param(
    [int]$Seconds = 120
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot ("perfkey-combined-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
$ecLog = Join-Path $PSScriptRoot ("perfkey-ec-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
Start-Transcript -Path $out -Force | Out-Null
Write-Host "输出: $out"
Write-Host "请在这 $Seconds 秒内按性能键 3 次，每次间隔约 3 秒。"
Write-Host ""
Add-Type -Path (Join-Path $PSScriptRoot 'AcpiEcProbe.cs')
$addresses = @(1870, 1864, 1865, 1866, 1867)
$last = @{}
$deadline = (Get-Date).AddSeconds($Seconds)
$start = Get-Date
while ((Get-Date) -lt $deadline) {
    foreach ($addr in $addresses) {
        try {
            $v = [AcpiEcProbe]::Read($addr)
            if ($v -ge 0 -and (-not $last.ContainsKey($addr) -or $last[$addr] -ne $v)) {
                $last[$addr] = $v
                Write-Host ("[{0}] EC 0x{1:X3} = 0x{2:X2} ({2})" -f (Get-Date -Format 'HH:mm:ss.fff'), $addr, $v)
            }
        } catch { }
    }
    Start-Sleep -Milliseconds 100
}
Stop-Transcript | Out-Null
Write-Host "完成: $out"
