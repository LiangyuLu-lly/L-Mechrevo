. 'D:\openLM\installer-src\files\GCU\Select-GcuPayload.ps1'
foreach ($v in '30','40','50') {
  $r = Select-GcuPayload -Variant $v -DeviceId @('PCI\VEN_10DE&DEV_28A0') -GpuName @('NVIDIA GeForce RTX 4060 Laptop GPU')
  Write-Host ("  -Variant {0} -> Variant={1}  StagedDir={2}" -f $v, $r.Variant, $r.StagedDir)
  Write-Host ("      Reason: {0}" -f $r.Reason)
}
