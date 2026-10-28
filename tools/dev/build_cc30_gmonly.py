# -*- coding: utf-8 -*-
# 重建 30 系官方控制台精简版 v2（GMOnly）：
#   1) 复制 cc_30\app 全树到 rebuild\cc30_lite\files
#   2) 只删 GCU 内的 GamingMonitor 目录（保留 MyControlCenter 根目录 SharpDX/NAudio——UI 依赖）
#   3) 补包内 UserFanTables（外置机型风扇表）
#   4) 生成 install_script.iss（同完整版，OutputBaseFilename=GMOnly，AppId 固定）
import io, os, shutil

SRC = r'D:\openLM\archive\unpacks\cc_30\app'
DST = r'D:\openLM\rebuild\cc30_lite\files'
ISS = r'D:\openLM\rebuild\cc30_lite\install_script_gmonly.iss'
EXT = r'D:\Downloads\ControlCenter_4.17.47.13_Mechrevo'

# --- 1) 复制整树 ---
if os.path.exists(DST):
    shutil.rmtree(DST)
shutil.copytree(SRC, DST)

# --- 2) 只删 GamingMonitor 目录 ---
gcu_mc = os.path.join(DST, 'UniwillService', 'MyControlCenter')
shutil.rmtree(os.path.join(gcu_mc, 'GamingMonitor'), ignore_errors=True)
print('deleted GamingMonitor dir only')

# --- 3) 补包内 UserFanTables ---
uft_src = os.path.join(EXT, 'UserFanTables')
uft_dst = os.path.join(gcu_mc, 'UserFanTables')
if os.path.isdir(uft_src) and not os.path.exists(uft_dst):
    shutil.copytree(uft_src, uft_dst)
    print('copied UserFanTables (%d dirs)' % len(os.listdir(uft_src)))

# --- 4) 生成 iss ---
appid = '{{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}'

files_lines = []
for root, dirs, fnames in os.walk(DST):
    dirs.sort()
    for f in sorted(fnames):
        full = os.path.join(root, f)
        rel = os.path.relpath(full, DST)
        d = os.path.dirname(rel)
        dest = '{app}'
        if d:
            dest += '\\' + d.replace('/', '\\')
        files_lines.append('Source: "files\\%s"; DestDir: "%s"; Flags: ignoreversion'
                           % (rel.replace('/', '\\'), dest))
files_block = '\n'.join(files_lines)

code_block = r'''[Code]
// 从安装源根（exe 所在目录）的 logo 目录复制到 app 目录的 logo（logo 为外置文件）
procedure CurStepChanged(CurStep: TSetupStep);
var
  ExeParentPath, ExternalLogoPath, InstallAppLogoPath: string;
  BatchPath, CmdLine: string;
  ExitCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    ExeParentPath := ExtractFilePath(ExpandConstant('{srcexe}'));
    ExternalLogoPath := ExeParentPath + 'logo\';
    InstallAppLogoPath := ExpandConstant('{app}\logo\');

    if DirExists(ExternalLogoPath) then
    begin
      CreateDir(InstallAppLogoPath);

      // 生成临时批处理，调用系统xcopy命令复制整个目录
      BatchPath := ExpandConstant('{tmp}\copy_logo.bat');
      CmdLine := Format('xcopy "%s*" "%s" /E /I /Y /Q', [ExternalLogoPath, InstallAppLogoPath]);

      if SaveStringToFile(BatchPath, CmdLine, False) then
      begin
        Exec(BatchPath, '', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
        DeleteFile(BatchPath);
      end;
    end;
  end;
end;
'''

iss = r'''; 30 系官方控制台精简版 GMOnly（仅删 GamingMonitor，保留 SharpDX/NAudio）
; 基于官方 ControlCenter_4.17.47.13_Mechrevo 解包重建，安装行为参考官方 5.56.60.34
; 外置依赖（exe 同目录）：PreinstallKit（主程序 UI 侧载）、logo、RGBKeyboard.reg、setup.ini

[Setup]
AppName=Control Center Lite
AppId=%APPID%
AppVersion=4.17.47.13
AppPublisher=OEM
AppCopyright=Copyright (C) AISTONE GLOBAL (SUZHOU) LIMITED. All rights reserved.
VersionInfoVersion=4.17.47.13
VersionInfoProductVersion=4.17.47.13
VersionInfoProductName=机械革命控制中心（精简版）
VersionInfoDescription=机械革命控制中心安装程序（精简版）
VersionInfoCompany=AISTONE GLOBAL (SUZHOU) LIMITED
VersionInfoOriginalFileName=ControlCenter_4.17.47.13_Mechrevo_Lite_GMOnly.exe
DefaultDirName={commonpf}\OEM\ControlCenterLite
DefaultGroupName=ControlCenterLite
OutputBaseFilename=ControlCenter_4.17.47.13_Mechrevo_Lite_GMOnly
Compression=lzma
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=yes
DisableProgramGroupPage=yes
WizardStyle=classic
PrivilegesRequired=admin
UninstallDisplayName=Control Center Lite (GCU)

[Files]
%FILES_BLOCK%

[Registry]
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AppType"; ValueType: Dword; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AppIcon"; ValueType: String; ValueData: "controlcenter_MR.ico"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "APPTarget"; ValueType: String; ValueData: "Customize"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "Language"; ValueType: String; ValueData: "zh-cn"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "CustomizeTarget"; ValueType: Dword; ValueData: "17"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "BridgeType"; ValueType: Dword; ValueData: "0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "LanguageList"; ValueType: String; ValueData: "en-us,zh-cn"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AutoDetectSystemLang"; ValueType: String; ValueData: "DetectOff"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AppVersion"; ValueType: String; ValueData: "4.17.47.13"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AppSubVersion"; ValueType: String; ValueData: "4.17.47.13_Mechrevo"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "AppName"; ValueType: String; ValueData: "机械革命电竞控制台"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "ColorCalibration"; ValueType: Dword; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "OsdOnly"; ValueType: Dword; ValueData: "0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "MonitorSupport"; ValueType: Dword; ValueData: "0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueName: "DebugMode"; ValueType: Dword; ValueData: "0"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ZipFiles\7za.exe"; Parameters: "x ""{app}\ZipFiles\UWACPIDriver.zip"" -o""{app}\"" * -r -aoa";
Filename: "{app}\UniwillService\install.bat";
Filename: "reg.exe"; Parameters: "import ""{src}\RGBKeyboard.reg"" /reg:64";
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{src}\PreinstallKit\Install.ps1"""; WorkingDir: "{src}\PreinstallKit"; Flags: waituntilterminated
Filename: "{app}\DefaultTool\DefaultTool.exe"; Parameters: """1"" -i"; StatusMsg: "Load default configuration...";
Filename: "{app}\CreateShortcut\CreateShortcut.exe";
Filename: "pnputil.exe"; Parameters: "-a ""{app}\UWACPIDriver\UWACPIDriver.inf""";
Filename: "{app}\AirplaneDriver\devcon.exe"; Parameters: "remove acpi\INOU0000";
Filename: "{app}\AirplaneDriver\devcon.exe"; Parameters: "/r update ""{app}\UWACPIDriver\UWACPIDriver.inf"""" acpi\INOU0000";
Filename: "{app}\AirplaneDriver\devcon.exe"; Parameters: "rescan";
Filename: "{app}\ZipFiles\7za.exe"; Parameters: "x ""{app}\ZipFiles\BT_DFU_Tool.zip"" -o""{commonappdata}\"" * -r -aoa";
Filename: "{app}\ZipFiles\7za.exe"; Parameters: "x ""{app}\ZipFiles\BT_DFU_FW.zip"" -o""{sd}\Temp\"" * -r -aoa";

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im SystrayComponent.exe"; Flags: skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im OSDTpDetect.exe"; Flags: skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im GCUBridge.exe"; Flags: skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im GCUService.exe"; Flags: skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im DefaultTool.exe"; Flags: skipifdoesntexist
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage -AllUsers *GamingCenter3* | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; Get-AppxPackage *GamingCenter3* | Remove-AppxPackage -ErrorAction SilentlyContinue; exit 0""";
Filename: "{app}\UniwillService\uninstall.bat";
Filename: "{app}\DefaultTool\DefaultTool.exe"; Parameters: """1"" -u";
Filename: "{app}\AirplaneDriver\devcon.exe"; Parameters: "remove acpi\INOU0000";

[UninstallDelete]
Type: filesandordirs; Name: "{app}\UniwillService\MyControlCenter\GamingMonitor";
Type: dirifempty; Name: "{app}";
Type: filesandordirs; Name: "{app}";
Type: filesandordirs; Name: "{commonappdata}\BT_DFU_Tool";
Type: filesandordirs; Name: "{sd}\Temp\BT_DFU_FW";

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

''' + code_block

iss = iss.replace('%APPID%', appid).replace('%FILES_BLOCK%', files_block)

io.open(ISS, 'w', encoding='utf-8-sig').write(iss)
print('iss written:', ISS)
print('files:', len(files_lines))
sz = 0
for root, _, fnames in os.walk(DST):
    for f in fnames:
        sz += os.path.getsize(os.path.join(root, f))
print('files total:', round(sz/1048576, 2), 'MB')
