; Created by "innounp" version 2.67.11
; Setup file: pkg-20260919-195709-a076c697.exe
; Inno Setup Version: 6.7.0 (Unicode)

[Setup]
AppName=L-Mechrevo
AppVerName=L-Mechrevo 0.293.6.3 beta23.6.3 (NoGCU)
AppId={{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}
AppVersion=0.293.6.3
AppPublisher=L-Mechrevo contributors
AppCopyright=Copyright (C) 2026 L-Mechrevo contributors. GPL-3.0-only.
DefaultDirName={autopf}\L-Mechrevo
DefaultGroupName=L-Mechrevo
OutputBaseFilename=LM-0.293.6.3-beta23.6.3-NoGCU-setup
UninstallDisplayIcon={app}\L-Mechrevo.exe
SetupIconFile=files\favicon.ico
VersionInfoVersion=0.293.6.3
VersionInfoProductVersion=0.293.6.3
UninstallDisplayName=L-Mechrevo 0.293.6.3 beta23.6.3 (NoGCU)
Compression=lzma2
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=auto
DisableProgramGroupPage=yes
ChangesAssociations=no
ShowLanguageDialog=auto
AllowNoIcons=yes
WizardStyle=modern light
SetupLogging=yes
[Files]
Source: "files\Fonts\HarmonyOS_Sans_SC_Regular.ttf"; DestDir: "{app}\Fonts"; Flags: ignoreversion
Source: "files\Fonts\HarmonyOS_Sans_SC_Bold.ttf"; DestDir: "{app}\Fonts"; Flags: ignoreversion
Source: "files\BlackSharp.Core.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\DiskInfoToolkit.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\favicon.ico"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\HidSharp.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\MechrevoLite.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\MechrevoLite.Core.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\PawnIO\*"; DestDir: "{app}\PawnIO"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "files\UWACPIDriver\*"; DestDir: "{app}\UWACPIDriver"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "files\Install-PawnIO.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\GCU\Install-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\GCU\Select-GcuPayload.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\GCU\Uninstall-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\GCU\7za.exe"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\GCU\logo\Language\*.json"; DestDir: "{app}\GCU\logo\Language"; Flags: ignoreversion
Source: "files\GCU\shared\README.txt"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\GCU\shared\NOTICE.txt"; DestDir: "{app}\GCU"; Flags: ignoreversion
Source: "files\L-Mechrevo.deps.json"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.dll.config"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.exe"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\LibreHardwareMonitorLib.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\LICENSE,1.txt"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion 
Source: "files\Microsoft.Win32.TaskScheduler.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Microsoft.Windows.SDK.NET.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\MQTTnet.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\NvAPIWrapper.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Probe.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\RAMSPDToolkit-NDD.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\System.Management.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\THIRD_PARTY_NOTICES,1.txt"; DestDir: "{app}"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion 
Source: "files\WinRT.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Gaming.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Office.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Turbo.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode1.jpg"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode2.jpg"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode3.png"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode4.png"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\zh-CN\L-Mechrevo.resources.dll"; DestDir: "{app}\zh-CN"; Flags: ignoreversion 
Source: "files\zh-CN\Microsoft.Win32.TaskScheduler.resources.dll"; DestDir: "{app}\zh-CN"; Flags: ignoreversion 
Source: "files\LICENSE,2.txt"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion 
Source: "files\THIRD_PARTY_NOTICES,2.txt"; DestDir: "{app}"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion 
Source: "files\更新日志.txt"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\用前必看.txt"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\usbmmidd_v2\*"; DestDir: "{app}\usbmmidd_v2"; Flags: ignoreversion recursesubdirs createallsubdirs 

; 2026-10-08：删除原 [Dirs] 段的三条空目录条目（{app}\ru\ / {app}\sv\ / {app}\zh-Hant\）。
; 原因同上（本包只带 zh-CN 资源，这三条只会产生空文件夹）。

[Run]
; 2026-10-05：全新安装前清空用户数据（升级时不执行，见 Check）。卸载全删、升级保留。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath (Join-Path $env:APPDATA MechrevoLite) -Recurse -Force -ErrorAction SilentlyContinue"""; Flags: runhidden; Check: IsFreshInstallFlag
Filename: "{app}\L-Mechrevo.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall skipifsilent nowait

[UninstallRun]

[Icons]
Name: "{group}\L-Mechrevo"; Filename: "{app}\L-Mechrevo.exe"; WorkingDir: "{app}"; 
Name: "{group}\使用前必看"; Filename: "{app}\用前必看.txt"; 
Name: "{group}\更新日志"; Filename: "{app}\更新日志.txt"; 
Name: "{group}\开源许可 (GPL-3.0)"; Filename: "{app}\LICENSE.txt"; 
Name: "{group}\{cm:UninstallProgram,L-Mechrevo}"; Filename: "{uninstallexe}"; 
Name: "{autodesktop}\L-Mechrevo"; Filename: "{app}\L-Mechrevo.exe"; WorkingDir: "{app}"; Tasks: desktopicon; 

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; 

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\L-Mechrevo\logs"; 
Type: dirifempty; Name: "{app}"; 
Type: filesandordirs; Name: "{app}"; 

[CustomMessages]
chinesesimplified.NameAndVersion=%1 版本 %2
chinesesimplified.AdditionalIcons=附加快捷方式：
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
chinesesimplified.CreateQuickLaunchIcon=创建快速启动栏快捷方式(&Q)
chinesesimplified.ProgramOnTheWeb=%1 网站
chinesesimplified.UninstallProgram=卸载 %1
chinesesimplified.LaunchProgram=运行 %1
chinesesimplified.AssocFileExtension=将 %2 文件扩展名与 %1 建立关联(&A)
chinesesimplified.AssocingFileExtension=正在将 %2 文件扩展名与 %1 建立关联...
chinesesimplified.AutoStartProgramGroupDescription=启动：
chinesesimplified.AutoStartProgram=自动启动 %1
chinesesimplified.AddonHostProgramNotFound=您选择的文件夹中无法找到 %1。%n%n您要继续吗？
english.NameAndVersion=%1 version %2
english.AdditionalIcons=Additional shortcuts:
english.CreateDesktopIcon=Create a &desktop shortcut
english.CreateQuickLaunchIcon=Create a &Quick Launch shortcut
english.ProgramOnTheWeb=%1 on the Web
english.UninstallProgram=Uninstall %1
english.LaunchProgram=Launch %1
english.AssocFileExtension=&Associate %1 with the %2 file extension
english.AssocingFileExtension=Associating %1 with the %2 file extension...
english.AutoStartProgramGroupDescription=Startup:
english.AutoStartProgram=Automatically start %1
english.AddonHostProgramNotFound=%1 could not be located in the folder you selected.%n%nDo you want to continue anyway?
chinesesimplified.LaunchApp=运行 L-Mechrevo
english.LaunchApp=Launch L-Mechrevo
chinesesimplified.RuntimeRequired=本程序需要 .NET 桌面运行时 10（x64），当前未安装。{break}{break}是否现在从 Microsoft 官方地址自动下载并安装？
english.RuntimeRequired=This app needs the .NET Desktop Runtime 10 (x64), which is not installed.{break}{break}Download and install it now from Microsoft?
chinesesimplified.RuntimeFailed=自动下载或安装 .NET 桌面运行时失败，无法继续。{break}{break}将打开官方下载页面，请手动安装后重新运行本安装程序。
english.RuntimeFailed=Automatic download/install of the .NET Desktop Runtime failed.{break}{break}The official download page will open; install it manually and run setup again.
chinesesimplified.RuntimeDeclined=缺少 .NET 桌面运行时 10（x64），安装无法继续。{break}{break}将打开官方下载页面。
english.RuntimeDeclined=.NET Desktop Runtime 10 (x64) is missing; setup cannot continue.{break}{break}The official download page will open.

[Languages]
; These files are stubs
; To achieve better results after recompilation, use the real language files
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"; 
Name: "english"; MessagesFile: "compiler:Default.isl";
[Code]
// {cm:} 展开不递归处理 {break}，这里统一替换为真实换行。
function ExpandMsg(Const Name: String): String;
var
  S: String;
begin
  S := ExpandConstant('{cm:' + Name + '}');
  StringChange(S, '{break}', #13#10);
  Result := S;
end;

const
  // 与 [Setup] 的 AppId={{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64} 对应（Inno 的 {{ 转义为单个 {），
  // 卸载项键名 = <AppId>_is1。与主 iss install_script.iss 相同。
  LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1';
  Net10RuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  Net10DownloadPage = 'https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0';

// .NET Desktop Runtime 10 (x64) presence check. Accept either the sharedfx
// registry key (written by the official installer) OR the shared framework
// directory on disk (manually deployed / zip-extracted runtimes do not write
// the key but still run the app).
function IsNetDesktop10Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  if not Result then
    Result := DirExists(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  if not Result then
    Result := DirExists(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App'));
end;

procedure OpenNet10DownloadPage();
var
  ErrCode: Integer;
begin
  ShellExec('open', Net10DownloadPage, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
end;

// Download the latest 10.0.x desktop runtime (aka.ms short link) and install quietly.
function InstallNet10(): Boolean;
var
  TmpPath: String;
  ResultCode: Integer;
begin
  Result := False;
  DownloadTemporaryFile(Net10RuntimeUrl, 'net10-desktop-runtime.exe', '', nil);
  TmpPath := ExpandConstant('{tmp}\net10-desktop-runtime.exe');
  if not FileExists(TmpPath) then
    Exit;
  if not Exec(TmpPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;
  Result := (ResultCode = 0) and IsNetDesktop10Installed();
end;

// 判定"是否已安装过 L-Mechrevo"：读卸载注册项（不依赖任何路径常量）。
// 必须这样判：InitializeSetup 运行时安装目录还没确定，{app} 尚未初始化，
// 展开它会抛 "An attempt was made to expand the "app" constant before it was initialized."
// （2026-10-05 beta21.7 实测报错即由此而来）。AppId 复用上面的 LmAppIdKey。
function IsLmAlreadyInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey) or
    RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey) or
    RegKeyExists(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey);
end;

// 2026-10-05：全新安装（旧版卸载项不存在）时清空用户数据目录，使「首次运行引导」重新出现；
// 覆盖升级时保留。
var
  GFreshInstall: Boolean;

function IsFreshInstallFlag(): Boolean;
begin
  Result := GFreshInstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 升级时 Setup 会以静默方式调用旧卸载程序 —— 那种情况必须保留用户数据。
    // 只有用户手动卸载（非静默）才全删。
    if not UninstallSilent() then
      DelTree(ExpandConstant('{userappdata}\MechrevoLite'), True, True, True);
  end;
end;

function InitializeSetup(): Boolean;
begin
  // 2026-10-05：旧版卸载项存在 = 覆盖升级 -> 保留用户数据；不存在 = 全新安装 -> 清残留使首次引导重现
  // 不能用 ExpandConstant('{app}...')：此刻 {app} 还没初始化，会抛内部错误。
  GFreshInstall := not IsLmAlreadyInstalled();
  Result := True;
  // .NET Desktop Runtime 10 (x64) is required; offer automatic install or open the download page.
  if not IsNetDesktop10Installed() then
  begin
    if MsgBox(ExpandMsg('RuntimeRequired'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not InstallNet10() then
      begin
        MsgBox(ExpandMsg('RuntimeFailed'), mbError, MB_OK);
        OpenNet10DownloadPage();
        Result := False;
        Exit;
      end;
    end
    else
    begin
      MsgBox(ExpandMsg('RuntimeDeclined'), mbInformation, MB_OK);
      OpenNet10DownloadPage();
      Result := False;
      Exit;
    end;
  end;
end;

procedure InstallExternalGcuIfPresent();
var
  SourceArchive, SourceFolder, Staging, SevenZip, Script, Params: String;
  ResultCode: Integer;
begin
  SourceArchive := ExpandConstant('{src}\GCU.7z');
  SourceFolder := ExpandConstant('{src}\GCU');
  if not FileExists(SourceArchive) and not DirExists(SourceFolder) then
    Exit;

  Staging := ExpandConstant('{tmp}\lm-external-gcu');
  ForceDirectories(Staging);
  SevenZip := ExpandConstant('{app}\GCU\7za.exe');
  if FileExists(SourceArchive) then
  begin
    if not FileExists(SevenZip) then
    begin
      Log('LM-GCU: 7za.exe 缺失，跳过外置 GCU 包');
      Exit;
    end;
    if not Exec(SevenZip, 'x -y -o"' + Staging + '" "' + SourceArchive + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('LM-GCU: 外置 GCU.7z 解压失败，跳过');
      Exit;
    end;
  end
  else
    Staging := SourceFolder;

  // Install-Gcu.ps1 需要 StagingRoot 下有 payload 和 7za.exe。
  if not DirExists(Staging + '\payload') then
  begin
    Log('LM-GCU: 外置目录没有 payload，跳过');
    Exit;
  end;
  if FileExists(SevenZip) then
    CopyFile(SevenZip, Staging + '\7za.exe', False);

  Script := ExpandConstant('{app}\GCU\Install-Gcu.ps1');
  if not FileExists(Script) then
  begin
    Log('LM-GCU: Install-Gcu.ps1 缺失，跳过');
    Exit;
  end;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '"' +
    ' -StagingRoot "' + Staging + '"' +
    ' -TargetDir "' + ExpandConstant('{app}\GCU') + '"' +
    ' -Variant Auto -InstallerVersion "18.3"' +
    ' -LogDir "' + ExpandConstant('{commonappdata}\L-Mechrevo\logs') + '"' +
    ' -AppExe "' + ExpandConstant('{app}\L-Mechrevo.exe') + '"';
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('LM-GCU: 外置 GCU 安装退出码=' + IntToStr(ResultCode))
  else
    Log('LM-GCU: 外置 GCU 安装启动失败');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Keep the full setup log next to the app for troubleshooting.
    if CopyFile(ExpandConstant('{log}'), ExpandConstant('{app}\install.log'), False) then
      Log('LM-UPDATE: 安装日志已保存到 {app}\install.log')
    else
      Log('LM-UPDATE: 安装日志复制失败');
    InstallExternalGcuIfPresent();
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := True;
end;