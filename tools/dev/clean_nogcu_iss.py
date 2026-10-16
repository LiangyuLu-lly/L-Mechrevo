# -*- coding: utf-8 -*-
import io
p = r'D:\openLM\installer-src\install_script_nogcu.iss'
s = io.open(p, encoding='utf-8-sig').read()

# 1) const: 删 OfficialAppId / LmAppIdKey
s = s.replace("""const
  OfficialAppId = '{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1';
  LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1';
  Net10RuntimeUrl""", """const
  Net10RuntimeUrl""")

# 2) InitializeSetup: 删 var OfficialUninst
s = s.replace("""function InitializeSetup(): Boolean;
var
  OfficialUninst: String;
begin
  Result := True;""", """function InitializeSetup(): Boolean;
begin
  Result := True;""")

# 3) 删多余 end; + GetLmUninstallString 注释
s = s.replace("""  end;
    end;
  end;
end;

// Read the previous L-Mechrevo uninstall string (same AppId). Empty on fresh installs.
procedure CurStepChanged(CurStep: TSetupStep);""", """  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);""")

# 4) 删 PrepareToInstall 注释
s = s.replace("""end;
// Stop the bundled GCU processes BEFORE any file is overwritten, so the old
// service cannot hold file locks during the copy. The GCU service itself is
// removed by the old version's uninstaller ([UninstallRun] Uninstall-Gcu.ps1)
// during the upgrade path, so no separate uninstall call is needed here.
function NeedRestart(): Boolean;""", """end;

function NeedRestart(): Boolean;""")

io.open(p, 'w', encoding='utf-8-sig').write(s)
# 校验
left = [kw for kw in ('OfficialAppId', 'LmAppIdKey', 'OfficialUninst', 'Uninstall-Gcu',
                      'Install-Gcu', 'Select-GcuPayload', 'GcuUserRoot', 'files\\GCU\\') if kw in s]
print('leftover:', left if left else 'NONE')
print('lines:', s.count('\n') + 1)
