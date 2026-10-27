param(
    [int]$Seconds = 120,
    [int[]]$Addresses = @(1870, 1864, 1865, 1866, 1867)
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot ("perfkey-capture-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
Start-Transcript -Path $out -Force | Out-Null

Add-Type -Path (Join-Path $PSScriptRoot 'AcpiEcProbe.cs')
Write-Host "输出: $out"
Write-Host "请在这 $Seconds 秒内按性能键 3 次，每次间隔 3 秒。"
Write-Host "同时记录 ACPI EC 地址 $($Addresses -join ', ') 的变化和 WMI 事件。"
Write-Host ""

# 1) ACPI EC polling
$last = @{}
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
    foreach ($addr in $Addresses) {
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
