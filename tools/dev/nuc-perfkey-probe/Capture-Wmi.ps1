param(
    [int]$Seconds = 120
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot ("perfkey-wmi-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
Start-Transcript -Path $out -Force | Out-Null
Write-Host "输出: $out"
Write-Host "请在这 $Seconds 秒内按性能键 3 次，每次间隔约 3 秒。"
Write-Host ""

$namespaces = @('root\WMI','root\Uniwill','root\Nuc','root\Intel')
$registrations = @()
$patterns = 'OSD|MODE|EC|UNIWILL|NUC|EVENT|PERF|ACPI|OEM'
foreach ($ns in $namespaces) {
    try {
        $classes = Get-CimClass -Namespace $ns -ErrorAction Stop | Where-Object {
            $_.CimClassName -match $patterns -or $_.CimSuperClassName -eq '__ExtrinsicEvent'
        }
        foreach ($c in $classes) {
            $name = $c.CimClassName
            $id = "perfkey-$ns-$name"
            try {
                $null = Register-CimIndicationEvent -Namespace $ns -Query "SELECT * FROM $name" -SourceIdentifier $id -Action {
                    $ev = $event.SourceEventArgs.NewEvent
                    $time = Get-Date -Format 'HH:mm:ss.fff'
                    Write-Host "[$time] WMI $($event.SourceIdentifier)"
                    foreach ($p in $ev.CimInstanceProperties) {
                        if ($p.Name -in @('__PATH','__NAMESPACE','__CLASS','__GENUS','__SERVER','__PROPERTY_COUNT','__DERIVATION','__RELPATH','__DYNASTY')) { continue }
                        try { Write-Host ("  {0} = {1}" -f $p.Name, $p.Value) } catch { }
                    }
                } -ErrorAction Stop
                $registrations += $id
                Write-Host "订阅: $ns :: $name"
            } catch {
                Write-Host "跳过: $ns :: $name ($($_.Exception.Message))"
            }
        }
    } catch {
        Write-Host "命名空间不可用: $ns"
    }
}

Write-Host ""
Write-Host "开始监听 $Seconds 秒 ..."
Start-Sleep -Seconds $Seconds

foreach ($id in $registrations) {
    try { Unregister-Event -SourceIdentifier $id -ErrorAction SilentlyContinue } catch { }
}
Stop-Transcript | Out-Null
Write-Host "完成: $out"
