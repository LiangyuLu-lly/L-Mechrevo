# -*- coding: utf-8 -*-
# 安装器日志：SetupLogging=yes + 升级流程关键步骤 Log() + 装完复制到 {app}\install.log
def edit(path, old, new, tag):
    t = open(path, encoding='utf-8-sig').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8-sig', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\installer-src\install_script.iss'

# 1) [Setup] 加 SetupLogging=yes
old1 = '''WizardStyle=modern light
[Files]'''
new1 = '''WizardStyle=modern light
SetupLogging=yes
[Files]'''
edit(p, old1, new1, 'setup-logging')

# 2) BackupAndUpgrade 加日志
old2 = '''begin
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
end;'''
new2 = '''begin
  GcuRoot := ExpandConstant(GcuUserRoot);
  BackupRoot := ExpandConstant('{tmp}\\lm-upgrade-backup');
  Log('LM-UPDATE: 检测到旧版，开始升级流程（备份->卸载->安装->恢复）');
  // 1) Make sure L-Mechrevo exits so the uninstaller can delete {app}.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im L-Mechrevo.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('LM-UPDATE: L-Mechrevo.exe 已尝试结束 (exit=' + IntToStr(ResultCode) + ')');
  // 2) Back up GCU user data to the installer temp dir (survives the uninstall).
  if not CopyDirContents(GcuRoot + '\\UserPofiles', BackupRoot + '\\UserPofiles') then
  begin
    Log('LM-UPDATE: 备份自定义模式档失败: ' + GcuRoot + '\\UserPofiles');
    MsgBox('备份自定义模式档失败，已中止升级。', mbError, MB_OK);
    Abort();
  end;
  Log('LM-UPDATE: 自定义模式档已备份');
  if not CopyDirContents(GcuRoot + '\\UserFanTables', BackupRoot + '\\UserFanTables') then
  begin
    Log('LM-UPDATE: 备份风扇表失败: ' + GcuRoot + '\\UserFanTables');
    MsgBox('备份风扇表失败，已中止升级。', mbError, MB_OK);
    Abort();
  end;
  Log('LM-UPDATE: 风扇表已备份');
  // 3) Stop the bundled GCU service via the OLD script still on disk, then
  //    uninstall the previous L-Mechrevo so the new files start clean.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if FileExists(GcuRoot + '\\..\\..\\..\\..\\..\\Uninstall-Gcu.ps1') then
  begin
    Log('LM-UPDATE: 开始卸载旧 GCU 服务（Uninstall-Gcu.ps1，可能耗时数秒）');
    Exec(ExpandConstant('{sys}\\WindowsPowerShell\\v1.0\\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + GcuRoot + '\\..\\..\\..\\..\\..\\Uninstall-Gcu.ps1" -TargetDir "' + ExpandConstant('{app}\\GCU') + '" -KeepDriver',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('LM-UPDATE: 旧 GCU 服务卸载脚本结束 (exit=' + IntToStr(ResultCode) + ')');
  end
  else
    Log('LM-UPDATE: 未发现旧 Uninstall-Gcu.ps1，跳过 GCU 服务卸载');
  Uninst := GetLmUninstallString();
  if Uninst <> '' then
  begin
    Q := Trim(Uninst);
    if (Length(Q) > 0) and (Q[1] = '"') then
      Q := Copy(Q, 2, Length(Q) - 2);
    Q := '"' + Q + '"';
    Log('LM-UPDATE: 开始卸载旧版 L-Mechrevo: ' + Q);
    if not Exec(ExpandConstant('{cmd}'), '/c ' + Q + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Log('LM-UPDATE: 卸载旧版本执行失败');
      MsgBox('卸载旧版本失败，已中止升级。', mbError, MB_OK);
      Abort();
    end;
    Log('LM-UPDATE: 旧版卸载完成 (exit=' + IntToStr(ResultCode) + ')');
  end
  else
    Log('LM-UPDATE: 未找到旧版卸载键，跳过卸载');
end;'''
edit(p, old2, new2, 'backup-log')

# 3) CurStepChanged 加日志 + 装完复制 {log} 到 {app}\install.log
old3 = '''begin
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
end;'''
new3 = '''begin
  if CurStep = ssInstall then
  begin
    if GetLmUninstallString() <> '' then
      BackupAndUpgrade()
    else
      Log('LM-UPDATE: 全新安装，跳过升级流程');
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
      Log('LM-UPDATE: GCU 用户数据已恢复');
    end
    else
      Log('LM-UPDATE: 无备份可恢复（全新安装）');
    // Keep the full setup log next to the app for troubleshooting.
    if FileCopy(ExpandConstant('{log}'), ExpandConstant('{app}\\install.log'), False) then
      Log('LM-UPDATE: 安装日志已保存到 {app}\\install.log')
    else
      Log('LM-UPDATE: 安装日志复制失败');
  end;
end;'''
edit(p, old3, new3, 'curstep-log')

print('ALL OK')
