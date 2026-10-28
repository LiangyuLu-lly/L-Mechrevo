# -*- coding: utf-8 -*-
# 三个修复：
# 1) 安装器删除 Uninstall-Gcu 逻辑（卸载旧版 LM 时其 [UninstallRun] 会自动卸载 GCU 服务）
# 2) 40 系校色状态初始化优先读 LM 自己记忆（AppConfig calib_mode），无记忆才读官方注册表
# 3) 托盘核显模式菜单项仅对真三模（CanOfferIgpuOnly）显示，去掉误伤双显双模的兜底分支
def edit(path, old, new, tag, enc='utf-8-sig'):
    t = open(path, encoding=enc).read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding=enc, newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# ---------- 1) 安装器删 Uninstall-Gcu ----------
p = r'D:\openLM\installer-src\install_script.iss'

old1a = '''  // 3) Stop the bundled GCU service via the OLD script still on disk, then
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
    Log('LM-UPDATE: 未发现旧 Uninstall-Gcu.ps1，跳过 GCU 服务卸载');'''
new1a = '''  // 3) Stop GCU processes to release file locks. No separate GCU uninstall call:
  //    the old version's own uninstaller (unins000) removes the GCU service via
  //    its [UninstallRun] Uninstall-Gcu.ps1, so uninstalling L-Mechrevo is enough.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('LM-UPDATE: GCU 进程已尝试结束 (exit=' + IntToStr(ResultCode) + ')');'''
edit(p, old1a, new1a, 'iss-backupandupgrade')

old1b = '''// Stop the bundled GCU processes and uninstall the prior GCU service BEFORE any
// file is overwritten, so the old service cannot hold file locks during the copy
// and two generations never coexist. Uses the OLD Uninstall-Gcu.ps1 still on disk;
// fresh installs (no existing {app}\\GCU\\Uninstall-Gcu.ps1) skip this step.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  GcuUninstall: String;
  GcuTarget: String;
  ResultCode: Integer;
begin
  Result := '';
  GcuTarget := ExpandConstant('{app}\\GCU');
  GcuUninstall := GcuTarget + '\\Uninstall-Gcu.ps1';
  // 1) 先杀停 GCU 进程，解除文件占用（失败不致命，卸载脚本会再处理）。
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 2) 覆盖前用磁盘上现有的卸载脚本移除旧 GCU 服务/注册。
  if FileExists(GcuUninstall) then
  begin
    if Exec(ExpandConstant('{sys}\\WindowsPowerShell\\v1.0\\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + GcuUninstall + '" -TargetDir "' + GcuTarget + '" -KeepDriver',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode <> 0 then
        Result := ExpandConstant('{cm:GcuPreUninstallFailed}') + ' (' + IntToStr(ResultCode) + ')';
    end;
  end;
end;'''
new1b = '''// Stop the bundled GCU processes BEFORE any file is overwritten, so the old
// service cannot hold file locks during the copy. The GCU service itself is
// removed by the old version's uninstaller ([UninstallRun] Uninstall-Gcu.ps1)
// during the upgrade path, so no separate uninstall call is needed here.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  GcuTarget: String;
  ResultCode: Integer;
begin
  Result := '';
  GcuTarget := ExpandConstant('{app}\\GCU');
  // 1) 先杀停 GCU 进程，解除文件占用（失败不致命）。
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;'''
edit(p, old1b, new1b, 'iss-preparetoinstall')

# ---------- 2) 40 系校色记忆 ----------
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
old2 = '''	internal static int ResolveColorCalibrationMode()
	{
		MechrevoHw? hw = Program.hw;
		if (hw == null || !hw.ColorCalibrationModeSeen)
		{
			return MechrevoService.GetColorCalibrationMode();
		}
		return Program.hw.ColorCalibrationMode;
	}'''
new2 = '''	internal static int ResolveColorCalibrationMode()
	{
		MechrevoHw? hw = Program.hw;
		if (hw == null || !hw.ColorCalibrationModeSeen)
		{
			// 40 系 GCU 无校色档位回读：优先用 LM 自己的记忆（设置校色时写入 calib_mode），
			// 保证在 LM 内关闭后重启仍保持关闭；从未用 LM 设置过才读官方控制台注册表。
			if (AppConfig.Exists("calib_mode"))
			{
				return Math.Clamp(AppConfig.Get("calib_mode"), 0, 4);
			}
			return MechrevoService.GetColorCalibrationMode();
		}
		return Program.hw.ColorCalibrationMode;
	}'''
edit(p2, old2, new2, 'calib-memory', enc='utf-8')

# ---------- 3) 托盘核显按钮 ----------
old3 = '''		num5 = (TrayShow((trayHw?.CanOfferIgpuOnly ?? false) || ((trayHw?.CanOfferGpuModeSwitch ?? false) && (trayHw?.SupportsDgpuDirect ?? mechrevoDeviceCapabilities.DgpuDirect))) ? 1 : 0);'''
new3 = '''		num5 = (TrayShow((trayHw?.CanOfferIgpuOnly ?? false)) ? 1 : 0);'''
edit(p2, old3, new3, 'tray-igpu', enc='utf-8')

print('ALL OK')
