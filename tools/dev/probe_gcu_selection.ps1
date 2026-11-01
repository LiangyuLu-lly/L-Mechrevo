# 真实硬件走一遍 GCU 选择逻辑（本机探测）
$ErrorActionPreference = 'Stop'
. 'D:\openLM\installer-src\files\GCU\Select-GcuPayload.ps1'

$r = Select-GcuPayload -Probe
Write-Host ('Generation  : ' + $r.Generation)
Write-Host ('Variant     : ' + $r.Variant)
Write-Host ('StagedDir   : ' + $r.StagedDir)
Write-Host ('RepoPayload : ' + $r.RepoPayload)
Write-Host ('Reason      : ' + $r.Reason)
Write-Host 'Evidence    :'
foreach ($e in @($r.Evidence)) { Write-Host ('   - ' + $e) }

Write-Host ''
Write-Host '--- 当前注册表里实际安装的变体 ---'
$gcu = Get-ItemProperty -Path 'HKLM:\SOFTWARE\L-Mechrevo' -ErrorAction SilentlyContinue
Write-Host ('GcuVariant = ' + $gcu.GcuVariant)
