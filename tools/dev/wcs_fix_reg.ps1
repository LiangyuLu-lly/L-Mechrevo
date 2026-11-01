# 验证：补齐注册表自洽性（注册项键名=槽位值），让设置页能解析
$icmRoot = 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM'
$spoolFile = 'C:\Windows\System32\spool\drivers\color\BOE0CBE_B025AA78094A.icm'
$profileName = 'BOE0CBE_B025AA78094A.icm'

Write-Host '=== before ==='
$rp = Join-Path $icmRoot 'RegisteredProfiles'
if (Test-Path $rp) { (Get-Item $rp).Property | ForEach-Object { Write-Host ('  RP: ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp -Name $_)) } }
$disp = Join-Path $icmRoot 'ProfileAssociations\Display\DISPLAY'
if (Test-Path $disp) { Write-Host ('  DISPLAY kind=' + (Get-Item $disp).GetValueKind('ICMProfile') + ' val=[' + (Get-ItemPropertyValue -Path $disp -Name 'ICMProfile') + ']') }

Write-Host '=== write: RegisteredProfiles\profileName = spool path ==='
New-Item -Path $rp -Name $profileName -Force | Out-Null
New-ItemProperty -Path $rp -Name $profileName -PropertyType String -Value $spoolFile -Force | Out-Null

Write-Host '=== write: DISPLAY.ICMProfile = profileName (REG_SZ) ==='
New-ItemProperty -Path $disp -Name 'ICMProfile' -PropertyType String -Value $profileName -Force | Out-Null

Write-Host '=== after ==='
(Get-Item $rp).Property | ForEach-Object { Write-Host ('  RP: ' + $_ + ' = ' + (Get-ItemPropertyValue -Path $rp -Name $_)) }
Write-Host ('  DISPLAY kind=' + (Get-Item $disp).GetValueKind('ICMProfile') + ' val=[' + (Get-ItemPropertyValue -Path $disp -Name 'ICMProfile') + ']')
Write-Host 'done - please refresh Windows Settings > System > Display > Color profile'
