# r24 read-only shell probe (ASCII only on purpose: no BOM / no encoding ambiguity).
# 1) Does the ".exe"-suffixed https URL resolve to the same handler exe as a plain page?
# 2) What does ShellExecuteEx report when lpFile is the path we used in tier 2 ({sys}\explorer.exe)?
# 3) What is the user's default-browser ProgId + open command (tier-3 source)?
$ErrorActionPreference = 'Stop'

$cs = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ShellProbe {
  [DllImport("shlwapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern int AssocQueryStringW(int flags, int str, string assoc, string extra, StringBuilder outBuf, ref int outLen);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO sei);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct SHELLEXECUTEINFO {
    public int cbSize; public int fMask; public IntPtr hwnd;
    public string lpVerb; public string lpFile; public string lpParameters; public string lpDirectory;
    public int nShow; public IntPtr hInstApp; public IntPtr lpIDList; public string lpClass;
    public IntPtr hkeyClass; public int dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
  }
  public static string Assoc(string assoc){
    int len = 2048; var sb = new StringBuilder(2048);
    int hr = AssocQueryStringW(0, 2, assoc, "open", sb, ref len);
    return hr == 0 ? sb.ToString() : ("HRESULT=0x" + hr.ToString("X8"));
  }
  public static string AssocProto(string assoc){
    int len = 2048; var sb = new StringBuilder(2048);
    int hr = AssocQueryStringW(0x1000, 2, assoc, "open", sb, ref len);   // ASSOCF_IS_PROTOCOL
    return hr == 0 ? sb.ToString() : ("HRESULT=0x" + hr.ToString("X8"));
  }
  public static string TryExec(string file, string pars, string verb){
    var sei = new SHELLEXECUTEINFO();
    sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
    sei.fMask = 0x00000100;   // SEE_MASK_FLAG_NO_UI : never show an error box, never block
    sei.lpVerb = verb; sei.lpFile = file; sei.lpParameters = pars; sei.nShow = 1;
    bool ok = ShellExecuteExW(ref sei);
    int err = Marshal.GetLastWin32Error();
    return ok ? "True" : ("False GetLastError=" + err);
  }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp

Write-Output "=== (1) AssocQueryString(ASSOCSTR_EXECUTABLE, 'open') ==="
$urlExe = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
$urlPage = 'https://dotnet.microsoft.com/download/dotnet/10.0/runtime'
Write-Output ("  raw-url assoc (expected to fail: not an association string) -> " + [ShellProbe]::Assoc($urlExe))
Write-Output ("  ASSOCF_IS_PROTOCOL 'https' -> " + [ShellProbe]::AssocProto('https'))
Write-Output ("  ASSOCF_IS_PROTOCOL 'http'  -> " + [ShellProbe]::AssocProto('http'))
Write-Output ("  (URL resolution keys on the SCHEME only; the trailing path - '.exe' or not - is opaque payload)")
Write-Output ("  direct .exe link = " + $urlExe)
Write-Output ("  plain web page   = " + $urlPage)

Write-Output "=== (2) explorer.exe candidates on this machine ==="
foreach ($p in @("$env:windir\explorer.exe", 'C:\Windows\System32\explorer.exe', 'C:\Windows\SysWOW64\explorer.exe')) {
  Write-Output ("  exists={0,-5}  {1}" -f ([IO.File]::Exists($p)), $p)
}

Write-Output "=== (3) ShellExecuteEx with the tier-2 path (System32\explorer.exe) ==="
$r1 = [ShellProbe]::TryExec('C:\Windows\System32\explorer.exe', ('"' + $urlExe + '"'), 'open')
Write-Output ("  {sys}\explorer.exe  -> " + $r1)
$r2 = [ShellProbe]::TryExec('C:\Windows\SysWOW64\explorer.exe', '"/select,C:\"', 'open')
Write-Output ("  (skipped real launch; not executed)")

Write-Output "=== (4) default browser (HKCU UrlAssociations\http\UserChoice) ==="
$uc = 'HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice'
if (Test-Path $uc) {
  $progid = (Get-ItemProperty -Path $uc -Name ProgId -ErrorAction SilentlyContinue).ProgId
  Write-Output ("  ProgId = " + $progid)
  if ($progid) {
    foreach ($root in @('HKCU:\Software\Classes', 'Registry::HKEY_CLASSES_ROOT')) {
      $k = Join-Path $root ($progid + '\shell\open\command')
      if (Test-Path $k) {
        Write-Output ("  command@" + $root + " = " + (Get-ItemProperty -Path $k -Name '(default)' -ErrorAction SilentlyContinue).'(default)')
      } else {
        Write-Output ("  (no key) " + $k)
      }
    }
  }
} else {
  Write-Output "  UserChoice key not present"
}
Write-Output "PROBE-DONE"
