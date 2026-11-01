# -*- coding: utf-8 -*-
# 升级逻辑：备份 GCU 用户数据 -> 卸载旧版 -> 安装新版 -> 恢复
def edit(path, old, new, tag):
    t = open(path, encoding='utf-8-sig').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8-sig', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\installer-src\install_script.iss'

# 1) const 加 LmAppIdKey（我们自己的卸载键）
old1 = '''const
  OfficialAppId = '{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1';'''
new1 = '''const
  OfficialAppId = '{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1';
  LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1';
  GcuUserRoot = '{app}\\GCU\\payload\\GCU\\AiStoneService\\MyControlCenter';'''
edit(p, old1, new1, 'const')

# 2) 在 PrepareToInstall 前插入升级流程函数
old2 = '''// Stop the bundled GCU processes and uninstall the prior GCU service BEFORE any
// file is overwritten, so the old service cannot hold file locks during the copy
// and two generations never coexist. Uses the OLD Uninstall-Gcu.ps1 still on disk;
// fresh installs (no existing {app}\\GCU\\Uninstall-Gcu.ps1) skip this step.
function PrepareToInstall(var NeedsRestart: Boolean): String;'''
new2 = '''// Read the previous L-Mechrevo uninstall string (same AppId). Empty on fresh installs.
function GetLmUninstallString(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\' + LmAppIdKey, 'UninstallString', S) then
    Result := S
  else if RegQueryStringValue(HKLM, 'SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\' + LmAppIdKey, 'UninstallString', S) then
    Result := S;
end;

// Copy a directory tree; a missing source counts as success (nothing to copy).
function CopyDirContents(Src, Dst: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if not DirExists(Src) then
  begin
    Result := True;
    Exit;
  end;
  ForceDirectories(Dst);
  if Exec(ExpandConstant('{cmd}'), '/c xcopy "' + Src + '\\*" "' + Dst + '" /E /I /Y /Q /H', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

// Upgrade path: back up the GCU user data (custom mode profiles + fan tables),
// uninstall the previous L-Mechrevo, then the new files are installed and the
// backup is restored afterwards (see CurStepChanged ssPostInstall).
procedure BackupAndUpgrade();
var
  GcuRoot, BackupRoot, Uninst, Q: String;
  ResultCode: Integer;
begin
  GcuRoot := ExpandConstant(GcuUserRoot);
  BackupRoot := ExpandConstant('{tmp}\\lm-upgrade-backup');
  // 1) Make sure L-Mechrevo exits so the uninstaller can delete {app}.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im L-Mechrevo.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 2) Back up GCU user data to the installer temp dir (survives the uninstall).
  if not CopyDirContents(GcuRoot + '\\UserPofiles', BackupRoot + '\\UserPofiles') then
  begin
    MsgBox('备份自定义模式档失败，已中止升级。', mbError, MB_OK);
    Abort();
  end;
  if not CopyDirContents(GcuRoot + '\\UserFanTables', BackupRoot + '\\UserFanTables') then
  begin
    MsgBox('备份风扇表失败，已中止升级。', mbError, MB_OK);
    Abort();
  end;
  // 3) Stop the bundled GCU service via the OLD script still on disk, then
  //    uninstall the previous L-Mechrevo so the new files start clean.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if FileExists(GcuRoot + '\\..\\..\\..\\..\\..\\Uninstall-Gcu.ps1') then
  begin
    Exec(ExpandConstant('{sys}\\WindowsPowerShell\\v1.0\\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + GcuRoot + '\\..\\..\\..\\..\\..\\Uninstall-Gcu.ps1" -TargetDir "' + ExpandConstant('{app}\\GCU') + '" -KeepDriver',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Uninst := GetLmUninstallString();
  if Uninst <> '' then
  begin
    Q := Trim(Uninst);
    if (Length(Q) > 0) and (Q[1] = '"') then
      Q := Copy(Q, 2, Length(Q) - 2);
    Q := '"' + Q + '"';
    if not Exec(ExpandConstant('{cmd}'), '/c ' + Q + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      MsgBox('卸载旧版本失败，已中止升级。', mbError, MB_OK);
      Abort();
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  GcuRoot, BackupRoot: String;
begin
  if CurStep = ssInstall then
  begin
    if GetLmUninstallString() <> '' then
      BackupAndUpgrade();
  end
  else if CurStep = ssPostInstall then
  begin
    // Restore the backed-up GCU user data over the freshly installed defaults.
    BackupRoot := ExpandConstant('{tmp}\\lm-upgrade-backup');
    if DirExists(BackupRoot) then
    begin
      GcuRoot := ExpandConstant(GcuUserRoot);
      CopyDirContents(BackupRoot + '\\UserPofiles', GcuRoot + '\\UserPofiles');
      CopyDirContents(BackupRoot + '\\UserFanTables', GcuRoot + '\\UserFanTables');
    end;
  end;
end;

// Stop the bundled GCU processes and uninstall the prior GCU service BEFORE any
// file is overwritten, so the old service cannot hold file locks during the copy
// and two generations never coexist. Uses the OLD Uninstall-Gcu.ps1 still on disk;
// fresh installs (no existing {app}\\GCU\\Uninstall-Gcu.ps1) skip this step.
function PrepareToInstall(var NeedsRestart: Boolean): String;'''
edit(p, old2, new2, 'upgrade-functions')

# 3) InitializeSetup 的升级注释更新
old3 = '''  // Upgrade path: the installer overwrites the existing L-Mechrevo in place (same AppId),
  // so there is no old-version uninstall step here anymore.'''
new3 = '''  // Upgrade path: the previous L-Mechrevo is uninstalled (after backing up GCU
  // user data) in CurStepChanged(ssInstall); the new files are installed and the
  // backup restored in ssPostInstall. Fresh installs skip the uninstall step.'''
edit(p, old3, new3, 'init-comment')

print('ALL OK')
