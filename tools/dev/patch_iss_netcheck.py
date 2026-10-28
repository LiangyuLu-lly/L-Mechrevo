# -*- coding: utf-8 -*-
p = r'D:\openLM\installer-src\install_script.iss'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''// .NET Desktop Runtime 10 (x64) presence check. The sharedfx key only exists
// for .NET Desktop Runtime 10.x (x64), so key presence is a reliable signal.
function IsNetDesktop10Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.WindowsDesktop.App');
end;'''
new = '''// .NET Desktop Runtime 10 (x64) presence check. Accept either the sharedfx
// registry key (written by the official installer) OR the shared framework
// directory on disk (manually deployed / zip-extracted runtimes do not write
// the key but still run the app).
function IsNetDesktop10Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.WindowsDesktop.App');
  if not Result then
    Result := DirExists(ExpandConstant('{commonpf64}\\dotnet\\shared\\Microsoft.WindowsDesktop.App'));
  if not Result then
    Result := DirExists(ExpandConstant('{commonpf}\\dotnet\\shared\\Microsoft.WindowsDesktop.App'));
end;'''
assert t.count(old) == 1, 'IsNetDesktop10Installed anchor not found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('install_script.iss: IsNetDesktop10Installed now checks key + dirs')
