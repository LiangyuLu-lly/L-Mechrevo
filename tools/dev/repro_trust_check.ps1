# ASCII-only: Windows PowerShell reads .ps1 as ANSI without a BOM.
# Reproduce ExecutableTrust.GrantsImageReplacementToNonAdministrators
$Trusted = @('S-1-5-32-544','S-1-5-18','S-1-5-19','S-1-5-20','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
$dangerNames = @('Write','DeleteSubdirectoriesAndFiles','Delete','ChangePermissions','TakeOwnership')

function Test-Rules([string]$label, [string]$path) {
  Write-Host ("===== {0} : {1} =====" -f $label, $path)
  try { $acl = Get-Acl -LiteralPath $path } catch { Write-Host ("  READ FAILED: " + $_.Exception.Message); return }
  $hits = 0
  foreach ($r in $acl.Access) {
    $sid = ''
    try { $sid = $r.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { }
    $rights = [string]$r.FileSystemRights
    $hasDanger = $false
    foreach ($n in $dangerNames) { if ($rights -match $n) { $hasDanger = $true } }
    $trusted = $Trusted -contains $sid
    $inheritOnly = ($rights -match 'InheritOnly')
    $flag = ''
    if ($r.AccessControlType -eq 'Allow' -and -not $inheritOnly -and $hasDanger -and -not $trusted) {
      $flag = '   <== TRIGGERS WARNING'
      $hits++
    }
    Write-Host ("  {0,-46} sid={1,-46} rights={2,-38} inherited={3,-6} trusted={4}{5}" -f $r.IdentityReference, $sid, $rights, $r.IsInherited, $trusted, $flag)
  }
  if ($hits -gt 0) { Write-Host ("  => RESULT: UNSAFE (warning shown), hits=" + $hits) } else { Write-Host "  => RESULT: SAFE (no warning)" }
  Write-Host ''
}

Test-Rules 'DIR'  'C:\Program Files\L-Mechrevo'
Test-Rules 'EXE'  'C:\Program Files\L-Mechrevo\L-Mechrevo.exe'
Test-Rules 'DLL'  'C:\Program Files\L-Mechrevo\L-Mechrevo.dll'

Write-Host "===== running LM processes ====="
Get-Process -Name 'L-Mechrevo*' -ErrorAction SilentlyContinue | Select-Object Id, Path | Format-Table -AutoSize
