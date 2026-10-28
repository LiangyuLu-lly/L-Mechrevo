; ============================================================================
; ⛔ 已废弃（DEPRECATED）—— Gen30-on-40B 实验脚本自 2026-10-09 起不再维护
; ============================================================================
; 用户决定（2026-10-09）：「LMEXP_gen30on40b这玩意可以废弃了 以后非必要就只改正常包的」
;   * 本文件 = Gen30-on-40B 实验包（把 40 系机器当 30 系装 30 覆盖层）的打包脚本，
;     自 2026-10-09 起**废弃、不再维护**，保留在此**仅为历史参考**。
;   * ⭐ 今后只维护正常包：installer-src\install_script.iss；
;     所有新改动一律只做在正常包上，非必要不要修改本文件。
;   * 已知落后项（有意保留，不是待修的缺陷）：正常包卸载期已改为"只清 LM 自有"、
;     [UninstallRun] 固定带 -LmOwnedOnly；本文件仍是旧的"无条件、无开关"写法。
;   * 若要重建实验包，请以当时的正常包 install_script.iss 为基准重新做一份副本，
;     不要直接改本文件。
; ============================================================================

; Created by "innounp" version 2.67.11
; Setup file: pkg-20260919-195709-a076c697.exe
; Inno Setup Version: 6.7.0 (Unicode)
;
; ============================================================================
; [EXP] 实验包 gen30on40b：把本机当 30 系装 30 覆盖层
;    产出：installer-src\Output\LM-EXP-gen30on40b-setup.exe
;    用途：让 40 系 / 40B 机器用上 30 系的 UI 与「模式 x 配置」性能模式下发
;          （LM 侧唯一判据 = HKLM\SOFTWARE\L-Mechrevo\GcuVariant == "30"
;            -> MechrevoService.cs:3049 IsGcu30 -> MechrevoHw.cs:4448 SupportsModeProfileConfig）。
;    本 iss = install_script.iss 的副本 + 仅 5 处实验改动：
;      (1) [Setup] AppVerName / UninstallDisplayName 带 EXP 字样（版本号一律不动）
;      (2) [Setup] OutputBaseFilename = LM-EXP-gen30on40b-setup
;      (3) [Run] -Variant 固定为常量 30（不展开 {code:GetGcuVariantParam}）
;      (4) [Code] 新增 GExpForcedGcuVariant = '30'；GetEffectiveGcuVariant 恒返回它；
;              GetGcuVariantParam 恒返回它；40 系「核显模式」询问整段不再被调用（不弹窗）
;      (5) [Messages] + [Code] 新增安装前的实验警告（ExperimentalNotice）
;    [Files] 载荷构成一行未改（payload\30 = 163 条、payload\40B = 10 条、base = 247 条原样随包）。
;    AppId 与正式包同一个 GUID，所以能就地覆盖升级；回退 = 重跑正式安装包。
;    已知风险（用户须知）：30 版 GCUService.exe 是框架依赖构建，40B 机器上可能起不来。
; ============================================================================

[Setup]
AppName=L-Mechrevo
AppVerName=L-Mechrevo 0.291.4 beta22.4 EXP (30-on-40B)
AppId={{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}
AppVersion=0.291.4
AppPublisher=L-Mechrevo contributors
AppCopyright=Copyright (C) 2026 L-Mechrevo contributors. GPL-3.0-only.
DefaultDirName={autopf}\L-Mechrevo
DefaultGroupName=L-Mechrevo
OutputBaseFilename=LM-EXP-gen30on40b-setup
UninstallDisplayIcon={app}\L-Mechrevo.exe
SetupIconFile=files\favicon.ico
VersionInfoVersion=0.291.4
VersionInfoProductVersion=0.291.4
UninstallDisplayName=L-Mechrevo 0.291.4 beta22.4 EXP (30-on-40B)
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
Source: "files\GCU\Select-GcuPayload.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion 
Source: "files\GCU\Install-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion 
Source: "files\GCU\Uninstall-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\install.bat"; DestDir: "{app}\GCU\payload\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\NOTICE.txt"; DestDir: "{app}\GCU\payload\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\README.txt"; DestDir: "{app}\GCU\payload\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\uninstall.bat"; DestDir: "{app}\GCU\payload\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\清理官方控制台.bat"; DestDir: "{app}\GCU\payload\GCU"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\GCUBridge.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\install.bat"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\service.ini"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\uninstall.bat"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\uninstallReg.bat"; DestDir: "{app}\GCU\payload\GCU\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\ACPIDriverDll.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\DisplayInfo.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\displayUtil.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\EntityFramework.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\EntityFramework.SqlServer.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\EntityFramework.SqlServer.xml"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\EntityFramework.xml"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\GCUServicePlugin.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\GPUInfoDLL.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\IntelOverclockingSDK.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Microsoft.Toolkit.Uwp.Notifications.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Newtonsoft.Json.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\NLog.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\OSDTpDetect.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\logo\Language\en-us.json"; DestDir: "{app}\GCU\payload\GCU\logo\Language"; Flags: ignoreversion
Source: "files\GCU\payload\GCU\logo\Language\zh-cn.json"; DestDir: "{app}\GCU\payload\GCU\logo\Language"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\RegistryUtils.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\RestSharp.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\rtknrai.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SensorsData.Analytics.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\snynrai.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLite.Interop.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\sysConUtil.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\System.Data.SQLite.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\System.Management.Automation.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SystemHelper.exe"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UWAirplane.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UWDiskInfo.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings\AppProfileBinding.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings\AppProfileBindingPowerSaving.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings\KeyboardManager.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\BTSavingSettings\BTSavingSettings.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\BTSavingSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\CC.xml"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\ControlCenter.bat"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\disableAMDAudioCoProcessor.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\disableDGpu.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\disableWebcam.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\enableAMDAudioCoProcessor.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\enableDGpu.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\enableWebcam.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\GetGpuUsage.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\LogonUserName.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\ScanforHardwareChange.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Command\SearchCamStatus.ps1"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Command"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\Config\DeviceBase64.dll"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\Config"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\DisplayProfile\DisplayTable.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\DisplayProfile"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\GPUPowerSavingSettings\GPUPowerSavingSettings.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\GPUPowerSavingSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\KeyboardManager\settings.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\KeyboardManager"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\LCSavingSettings\LCSavingSettings.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\LCSavingSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\LiquidHWOC\LiquidHWOC.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\LiquidHWOC"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB\GamdeMode.db"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB\GamdeMode.db-shm"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB\GamdeMode.db-wal"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\SQLiteDB"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\DefaultFanTable_Gaming.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\DefaultFanTable_Office.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\DefaultFanTable_Turbo.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M1T4.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M1T5.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M2T4.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M2T5.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M3T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M3T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M3T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M3T4.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M3T5.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M4T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M4T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M4T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M4T4.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\M4T5.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQE3"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxf"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4AXxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PGx2"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4PUxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TQx1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH4TUX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6AQxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6ARxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG0x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG3x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PG7x150W"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PGEx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6PRxx"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M1T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M1T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M1T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M2T1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M2T2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1\M2T3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserFanTables\PH6TRX1"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\MainOption.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode4_Profile1.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode4_Profile2.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode4_Profile3.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode4_Profile4.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles\Mode4_Profile5.json"; DestDir: "{app}\GCU\payload\GCU\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\UWACPIDriver\uwacpidriver.cat"; DestDir: "{app}\GCU\payload\GCU\UWACPIDriver"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\UWACPIDriver\UWACPIDriver.inf"; DestDir: "{app}\GCU\payload\GCU\UWACPIDriver"; Flags: ignoreversion 
Source: "files\GCU\payload\GCU\UWACPIDriver\UWACPIDriver.sys"; DestDir: "{app}\GCU\payload\GCU\UWACPIDriver"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\GCUBridge.exe"; DestDir: "{app}\GCU\payload\40\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\GCUService.exe"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\NVControlSetting.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\GPUInfoDLL.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\ACPIDriverDll.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\OSDTpDetect.exe"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\RegistryUtils.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\UWAirplane.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\UWDiskInfo.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\DisplayInfo.dll"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40\AiStoneService\MyControlCenter\AppSettings\GamingMonitor.json"; DestDir: "{app}\GCU\payload\40\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion 
; 2026-10-09 载荷批次：40B 也要带上自己的桥，不能继承 base（50 系）那份 682,208 B 的旧 GCUBridge.exe。
; 该桥 2,122,248 B / SHA256 45B2C1D9…，取自来源 D:\innounp-2\tools\{app}\UniwillService\ 根目录，
; 落盘位置照 payload\40 的真实布局：AiStoneService\ 根下（与同目录那份 UEFI_Firmware.dll 成"桥+固件接口"一对）。
Source: "files\GCU\payload\40B\AiStoneService\GCUBridge.exe"; DestDir: "{app}\GCU\payload\40B\AiStoneService"; Flags: ignoreversion 
; 2026-10-09 载荷批次：40B 也要和 payload\40 一样，在 AiStoneService\ 根下放一份 UEFI_Firmware.dll。
; 该文件 131,080 B / SHA256 DED0F626…，与来源 D:\innounp-2\tools\{app}\UniwillService\ 逐字节一致
; （根目录与 MyControlCenter 两处同哈希）。不补的话，base（50 系）那份 193,760 B 会留在根目录。
Source: "files\GCU\payload\40B\AiStoneService\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\GCUService.exe"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\NVControlSetting.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\RegistryUtils.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\UWAirplane.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\UWDiskInfo.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\ACPIDriverDll.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\40B\AiStoneService\MyControlCenter\GPUInfoDLL.dll"; DestDir: "{app}\GCU\payload\40B\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\50\AiStoneService\MyControlCenter\GCUService.exe"; DestDir: "{app}\GCU\payload\50\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\50\AiStoneService\MyControlCenter\NVControlSetting.dll"; DestDir: "{app}\GCU\payload\50\AiStoneService\MyControlCenter"; Flags: ignoreversion 
Source: "files\GCU\payload\50\AiStoneService\MyControlCenter\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\50\AiStoneService\MyControlCenter"; Flags: ignoreversion 
; 2026-10-09 载荷批次：官方 50 系整包（ControlCenter_5.56.60.34_Mechrevo_GX.exe）带
; MyControlCenter\AppSettings\GamingMonitor.json（36 B / SHA256 92BD428D…），而 payload\50 与 base
; 都没有 —— 这是相对官方整包唯一的功能性缺口。文件取自该官方整包的只读解包，
; 落盘路径与 payload\40 覆盖层的真实布局一致。
Source: "files\GCU\payload\50\AiStoneService\MyControlCenter\AppSettings\GamingMonitor.json"; DestDir: "{app}\GCU\payload\50\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion 
Source: "files\GCU\payload\30\AiStoneService\GCUBridge.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MQTTnet.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\Microsoft.Win32.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\NLog.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\Newtonsoft.Json.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.AppContext.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Collections.Concurrent.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Collections.NonGeneric.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Collections.Specialized.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Collections.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ComponentModel.EventBasedAsync.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ComponentModel.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ComponentModel.TypeConverter.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ComponentModel.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Console.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Data.Common.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.Contracts.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.Debug.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.FileVersionInfo.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.Process.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.StackTrace.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.TextWriterTraceListener.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.Tools.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.TraceSource.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Diagnostics.Tracing.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Drawing.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Dynamic.Runtime.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Globalization.Calendars.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Globalization.Extensions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Globalization.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.Compression.ZipFile.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.Compression.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.FileSystem.DriveInfo.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.FileSystem.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.FileSystem.Watcher.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.FileSystem.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.IsolatedStorage.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.MemoryMappedFiles.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.Pipes.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.UnmanagedMemoryStream.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.IO.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Linq.Expressions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Linq.Parallel.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Linq.Queryable.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Linq.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Http.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.NameResolution.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.NetworkInformation.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Ping.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Requests.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Security.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.Sockets.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.WebHeaderCollection.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.WebSockets.Client.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Net.WebSockets.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ObjectModel.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Reflection.Extensions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Reflection.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Reflection.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Resources.Reader.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Resources.ResourceManager.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Resources.Writer.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.CompilerServices.VisualC.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Extensions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Handles.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.InteropServices.RuntimeInformation.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.InteropServices.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Numerics.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Serialization.Formatters.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Serialization.Json.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Serialization.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.Serialization.Xml.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Runtime.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Claims.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Cryptography.Algorithms.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Cryptography.Csp.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Cryptography.Encoding.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Cryptography.Primitives.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Cryptography.X509Certificates.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.Principal.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Security.SecureString.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Text.Encoding.Extensions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Text.Encoding.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Text.RegularExpressions.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.Overlapped.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.Tasks.Parallel.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.Tasks.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.Thread.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.ThreadPool.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.Timer.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Threading.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.ValueTuple.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.ReaderWriter.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.XDocument.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.XPath.XDocument.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.XPath.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.XmlDocument.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\System.Xml.XmlSerializer.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\netstandard.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\ACPIDriverDll.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\Colourful.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GCUService.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GPUInfoDLL.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\Gma.UserActivityMonitor.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\M2Mqtt.Net.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\NVControlSetting.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\Newtonsoft.Json.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\OSDTpDetect.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\OutputDiskInfo.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\RegistryUtils.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UEFI_Firmware.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UWAirplane.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UWDiskInfo.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
; 2026-10-09：补齐 30 系覆盖层的逐文件清单（此前常规包 payload\30 只列了 114/162 条）。
; 本次补入 32 条：MyControlCenter\ 根 3 个运行库（NAudio / SharpDX / SharpDX.RawInput）、
; AppSettings\ 2 个（GamingMonitor.json / AppProfileBindingPowerSaving.json）、
; GamingMonitor\ 整个目录 27 文件（GCUService 会加载 GCUService.GamingMonitor 插件）。
; 至此与 install_script_30gcu.iss 的 payload\30 集合一致（各 146 条）。
; 2026-10-09（本批修订）：上述排除已取消，UserPofiles\ 的 16 个 json 现在补入本 iss。
; 依据：base payload\GCU 本身就随包分发 UserPofiles（其 MainOption 亦带厂商构建机的
; BsodTimestampRestored 时间戳），故 30 系同样随包给默认档是与 base 一致的做法；且此前
; 146/162 的缺口使 30 系的 5 档/模式布局（Mode1..3 x5）在常规包里永远装不进去。
; 注意：专用包 install_script_30gcu.iss 按其自身设计仍不含这 16 条，两个 iss 在本项上不再一致。
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\NAudio.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\SharpDX.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\SharpDX.RawInput.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\AppSettings\GamingMonitor.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\AppSettings\AppProfileBindingPowerSaving.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\AppSettings"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\Capture.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\Config\configs.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\Config"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyHook.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyHook32.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyHook32Svc.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyHook64.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyHook64Svc.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyLoad32.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\EasyLoad64.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\GameTesing.exe"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\GameTesing.exe.config"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\GameTesing.pdb"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\GPUInfoDLL.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\Newtonsoft.Json.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.D3DCompiler.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Desktop.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Direct2D1.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Direct3D10.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Direct3D11.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Direct3D11.Effects.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Direct3D9.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.DXGI.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\SharpDX.Mathematics.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\sharpdx_direct3d11_1_effects_x64.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\sharpdx_direct3d11_1_effects_x86.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\System.Diagnostics.DiagnosticSource.dll"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor"; Flags: ignoreversion
; 2026-10-09 载荷批次：payload\30 的 UserPofiles\ 16 个 json（MainOption + Mode1..3 x5），
; 补上后本 iss 的 payload\30 条目数 146 -> 162，与磁盘文件数一一对应。
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\MainOption.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile1.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile2.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile3.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile4.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode1_Profile5.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile1.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile2.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile3.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile4.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode2_Profile5.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile1.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile2.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile3.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile4.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles\Mode3_Profile5.json"; DestDir: "{app}\GCU\payload\30\AiStoneService\MyControlCenter\UserPofiles"; Flags: ignoreversion
Source: "files\usbmmidd_v2\*"; DestDir: "{app}\usbmmidd_v2"; Flags: ignoreversion recursesubdirs createallsubdirs 

; 2026-10-08：删除原 [Dirs] 段的三条空目录条目（{app}\ru\ / {app}\sv\ / {app}\zh-Hant\）。
; 原因：本包只带 zh-CN 资源，[Files] 从不向这三个目录写任何文件，而 [Dirs] 会无条件创建它们，
; 于是每次安装都在安装目录里留下 3 个空文件夹（同 install_script_30gcu.iss 的修复）。

[Run]
; 2026-10-05：全新安装前清空用户数据（升级时不执行，见 Check）。卸载全删、升级保留。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath (Join-Path $env:APPDATA MechrevoLite) -Recurse -Force -ErrorAction SilentlyContinue"""; Flags: runhidden; Check: IsFreshInstallFlag
; [EXP] 实验包：-Variant 直接写常量 30，绝不展开 {code:GetGcuVariantParam}，
;        所以 40 系「核显模式 -> 40 / 40B」询问在本包里一定不会被触发（静默/交互都不弹）。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\GCU\Install-Gcu.ps1"" -StagingRoot ""{app}\GCU"" -TargetDir ""{app}\GCU"" -Variant 30 -InstallerVersion ""18.3"" -LogDir ""{commonappdata}\L-Mechrevo\logs"" -AppExe ""{app}\L-Mechrevo.exe"""; StatusMsg: "{cm:GcuStatus}"; 
Filename: "{app}\L-Mechrevo.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall skipifsilent nowait

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\GCU\Uninstall-Gcu.ps1"" -TargetDir ""{app}\GCU"" -LogDir ""{commonappdata}\L-Mechrevo\logs"""; RunOnceId: "GcuUninstall"; 

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
; 2026-10-09：%APPDATA%\MechrevoLite 与 C:\ProgramData\MechrevoLite 故意不写在这里 ——
; 它们要支持"交互卸载时用户选择保留"，只能在 [Code] 里处理
; （见 HandleUninstallUserData / usUninstall）。本段只留路径无关的静态删除项。
Type: filesandordirs; Name: "{commonappdata}\L-Mechrevo\logs"; 
Type: filesandordirs; Name: "{app}\GCU\AiStoneService"; 
Type: filesandordirs; Name: "{app}\GCU\UniwillService"; 
Type: filesandordirs; Name: "{app}\GCU\UWACPIDriver"; 
Type: filesandordirs; Name: "{app}\GCU\logo"; 
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
chinesesimplified.GcuStatus=正在安装 GCU...
english.GcuStatus=Installing GCU...
chinesesimplified.LaunchApp=运行 L-Mechrevo
english.LaunchApp=Launch L-Mechrevo
chinesesimplified.RuntimeRequired=本程序需要 .NET 桌面运行时 10（x64），当前未安装。{break}{break}是否现在从 Microsoft 官方地址自动下载并安装？
english.RuntimeRequired=This app needs the .NET Desktop Runtime 10 (x64), which is not installed.{break}{break}Download and install it now from Microsoft?
chinesesimplified.RuntimeFailed=自动下载或安装 .NET 桌面运行时失败，无法继续。{break}{break}将打开官方下载页面，请手动安装后重新运行本安装程序。
english.RuntimeFailed=Automatic download/install of the .NET Desktop Runtime failed.{break}{break}The official download page will open; install it manually and run setup again.
chinesesimplified.RuntimeDeclined=缺少 .NET 桌面运行时 10（x64），安装无法继续。{break}{break}将打开官方下载页面。
english.RuntimeDeclined=.NET Desktop Runtime 10 (x64) is missing; setup cannot continue.{break}{break}The official download page will open.
chinesesimplified.OfficialConsolePrompt=检测到已安装官方控制中心，是否立即卸载它以继续安装？卸载后由 L-Mechrevo 接管硬件控制。{break}{break}选择「否」将继续安装（可能与官方控制台功能冲突）。
english.OfficialConsolePrompt=An official Control Center was detected. Uninstall it now to continue? L-Mechrevo will take over hardware control after removal.{break}{break}Choose No to continue anyway (may conflict with the official console).
chinesesimplified.OfficialConsoleUninstallFailed=无法卸载官方控制中心，安装已中止。请先手动卸载后再试。
chinesesimplified.GcuPreUninstallFailed=覆盖安装前卸载旧版 GCU 失败，安装已中止。请手动停止 GCU 服务后重试。
english.OfficialConsoleUninstallFailed=Could not uninstall the official Control Center. Setup aborted. Uninstall it manually and try again.
english.GcuPreUninstallFailed=Failed to uninstall the prior GCU before overwriting. Setup aborted. Stop the GCU service manually and retry.

; [EXP] 实验包专用提示文案（只存在于本 iss，正式 install_script.iss 不含）。
; ⚠️ 展开时把 {break} 换成真实换行（见 ExpandMsg），理由同 RuntimeRequired。
chinesesimplified.ExperimentalNotice=⚠️ 实验版安装包（EXP）—— 30 覆盖层装到 40 系机器上{break}{break}本包不是正式发布版，只用于实验：{break}{break}1) 安装后本机会被当成【30 系】使用：LM 会显示 30 系的「模式 x 配置」配置号下拉，并随模式切换下发 ProfileIndex（性能模式下发链路）。{break}{break}2) 本机原 GCU 的用户自定义模式档 UserPofiles 会被换成 30 系布局，旧的 Mode4_Profile* 会消失（脚本按变体不同不回灌，这是设计如此）。UserFanTables 会保留。{break}{break}3) 30 版 GCUService.exe 是框架依赖构建，40B 机器上可能起不来（表现为 GCU 服务启动失败/无响应）；这不是安装失败。{break}{break}4) 一秒回退：重新运行正式安装包（同一个 AppId，直接覆盖回来 = 变体回到 Auto/40）。{break}{break}5) 建议安装前先备份：{app}\GCU 整个目录，以及注册表 HKLM\SOFTWARE\L-Mechrevo 下的 GcuVariant / GcuPayloadSha256 等值。{break}{break}确定要继续安装吗？
english.ExperimentalNotice=EXPERIMENTAL build (EXP) - installing the 30-series GCU overlay on a 40-series machine.{break}{break}1) This machine will be treated as 30-series: LM shows the 30-series "mode x profile" selector and pushes ProfileIndex on mode changes.{break}{break}2) Your GCU UserPofiles (custom mode profiles) will be replaced by the 30-series layout; Mode4_Profile* disappears. UserFanTables is kept.{break}{break}3) The 30-series GCUService.exe is framework-dependent and may fail to start on a 40B machine.{break}{break}4) One-second rollback: just run the official installer again (same AppId) to overwrite it back.{break}{break}5) Back up {app}\GCU and HKLM\SOFTWARE\L-Mechrevo first.{break}{break}Continue?

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
  OfficialAppId = '{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1';
  LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1';
  // 变体无关的 base 暂存树（Install-Gcu.ps1 从这里把 payload 拷进运行目录；UserFanTables 等机型风扇表都在这里）。
  GcuUserRoot = '{app}\GCU\payload\GCU\AiStoneService\MyControlCenter';
  // 2026-10-06（三处修复 B）：运行中的 GCU 服务真正读写的用户数据目录 —— 自定义模式档 UserPofiles 实际在这里被改。
  GcuLiveUserRoot = '{app}\GCU\AiStoneService\MyControlCenter';
  GcuVariantRegKey = 'SOFTWARE\L-Mechrevo';
  GcuVariantRegValue = 'GcuVariant';
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

function GetOfficialUninstallString(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + OfficialAppId, 'UninstallString', S) then
    Result := S
  else if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\' + OfficialAppId, 'UninstallString', S) then
    Result := S;
end;

// ==== 2026-10-08 修复：升级前必须把 GCU 停干净（服务真停 + 进程真退 + 全过程有日志） ====
// 旧实现只发一次 taskkill 就往下走（日志只有一句 exit=…，而 exit=128 恰恰表示"没找到进程"），
// 随后旧版卸载与新版文件复制期间，GCU 服务/进程仍可能持有 AiStoneService 里的文件句柄，
// 使 Install-Gcu.ps1 的 robocopy 以 11（失败类）退出、GCU 铺不上去（2026-10-07 20:11 实测）。
// 新流程：① 停 GCUBridge 服务并轮询到 Stopped（超时 20s）② 强制结束残留的
// GCUService.exe / GCUBridge.exe 并按进程名轮询到真正消失 ③ 按 GCU 运行目录兜底清一遍
// ④ 结果（停了哪些 / 等了多久 / 最终是否干净）逐行写进 install.log；超时也继续安装，
// 但明确记一行"未能在 N 秒内停干净，可能影响复制"。幂等：服务/进程不存在时只记一行跳过，不报错。
// GcuDir 由调用方传：InitializeSetup 阶段 {app} 还没初始化，那里必须传空串。
function StopGcuProcessesForUpgrade(const Tag, GcuDir: String): Boolean;
var
  TmpDir, Helper, OutFile, Exe, Args: String;
  ResultCode, I, N: Integer;
  ScriptLines, ResultLines: TArrayOfString;
begin
  Result := False;
  TmpDir := ExpandConstant('{tmp}');
  Helper := TmpDir + '\lm-stop-gcu.ps1';
  OutFile := TmpDir + '\lm-stop-gcu.log';
  DeleteFile(OutFile);
  SetArrayLength(ScriptLines, 64);
  N := 0;
  ScriptLines[N] := 'param([string]$OutFile, [string]$GcuDir, [string]$Tag)'; N := N + 1;
  ScriptLines[N] := '$ErrorActionPreference = "SilentlyContinue"'; N := N + 1;
  ScriptLines[N] := '$log = New-Object System.Collections.Generic.List[string]'; N := N + 1;
  ScriptLines[N] := 'function W($m) { $log.Add((Get-Date).ToString("HH:mm:ss") + " " + $m) }'; N := N + 1;
  ScriptLines[N] := '$svcName = "GCUBridge"'; N := N + 1;
  ScriptLines[N] := '$procNames = @("GCUService", "GCUBridge")'; N := N + 1;
  ScriptLines[N] := '$timeoutSec = 20'; N := N + 1;
  ScriptLines[N] := 'W ("[" + $Tag + "] 开始停 GCU：服务=" + $svcName + " 进程=" + ($procNames -join ",") + " 运行目录=" + $GcuDir)'; N := N + 1;
  ScriptLines[N] := '$svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue'; N := N + 1;
  ScriptLines[N] := 'if ($null -eq $svc) { W ("服务 " + $svcName + " 不存在（幂等跳过停服）") }'; N := N + 1;
  ScriptLines[N] := 'else {'; N := N + 1;
  ScriptLines[N] := '  W ("服务 " + $svcName + " 当前状态=" + $svc.Status)'; N := N + 1;
  ScriptLines[N] := '  if ($svc.Status -ne "Stopped") {'; N := N + 1;
  ScriptLines[N] := '    $sw = [Diagnostics.Stopwatch]::StartNew()'; N := N + 1;
  ScriptLines[N] := '    Stop-Service -Name $svcName -Force -ErrorAction SilentlyContinue'; N := N + 1;
  ScriptLines[N] := '    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) { $svc.Refresh(); if ($svc.Status -eq "Stopped") { break }; Start-Sleep -Milliseconds 300 }'; N := N + 1;
  ScriptLines[N] := '    if ($svc.Status -eq "Stopped") { W ("停服成功：" + $svcName + " -> Stopped（等待 " + $sw.Elapsed.TotalSeconds.ToString("0.0") + "s）") }'; N := N + 1;
  ScriptLines[N] := '    else { W ("停服未完成：" + $svcName + " 仍为 " + $svc.Status + "（等待 " + $sw.Elapsed.TotalSeconds.ToString("0.0") + "s 超时）") }'; N := N + 1;
  ScriptLines[N] := '  }'; N := N + 1;
  ScriptLines[N] := '  else { W ("服务 " + $svcName + " 已是 Stopped（幂等跳过）") }'; N := N + 1;
  ScriptLines[N] := '}'; N := N + 1;
  ScriptLines[N] := 'foreach ($name in $procNames) {'; N := N + 1;
  ScriptLines[N] := '  $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)'; N := N + 1;
  ScriptLines[N] := '  if ($procs.Count -eq 0) { W ("进程 " + $name + "：未运行（幂等跳过）"); continue }'; N := N + 1;
  ScriptLines[N] := '  W ("进程 " + $name + "：发现 " + $procs.Count + " 个 pid=" + (($procs | ForEach-Object { $_.Id }) -join ",") + " -> 强制结束")'; N := N + 1;
  ScriptLines[N] := '  foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }'; N := N + 1;
  ScriptLines[N] := '  $sw = [Diagnostics.Stopwatch]::StartNew()'; N := N + 1;
  ScriptLines[N] := '  $left = @(Get-Process -Name $name -ErrorAction SilentlyContinue)'; N := N + 1;
  ScriptLines[N] := '  while ($left.Count -gt 0 -and $sw.Elapsed.TotalSeconds -lt $timeoutSec) { Start-Sleep -Milliseconds 300; $left = @(Get-Process -Name $name -ErrorAction SilentlyContinue) }'; N := N + 1;
  ScriptLines[N] := '  if ($left.Count -eq 0) { W ("进程 " + $name + "：已全部退出（等待 " + $sw.Elapsed.TotalSeconds.ToString("0.0") + "s）") }'; N := N + 1;
  ScriptLines[N] := '  else { W ("进程 " + $name + "：仍有 " + $left.Count + " 个未退出（等待 " + $sw.Elapsed.TotalSeconds.ToString("0.0") + "s 超时）") }'; N := N + 1;
  ScriptLines[N] := '}'; N := N + 1;
  ScriptLines[N] := 'if (-not [string]::IsNullOrWhiteSpace($GcuDir)) {'; N := N + 1;
  ScriptLines[N] := '  foreach ($p in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)) {'; N := N + 1;
  ScriptLines[N] := '    if ([string]::IsNullOrWhiteSpace([string]$p.ExecutablePath)) { continue }'; N := N + 1;
  ScriptLines[N] := '    if (-not ([string]$p.ExecutablePath).ToLower().StartsWith($GcuDir.ToLower())) { continue }'; N := N + 1;
  ScriptLines[N] := '    W ("兜底：运行目录内的进程 " + $p.Name + " pid=" + $p.ProcessId + " -> 强制结束")'; N := N + 1;
  ScriptLines[N] := '    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue'; N := N + 1;
  ScriptLines[N] := '  }'; N := N + 1;
  ScriptLines[N] := '  Start-Sleep -Milliseconds 500'; N := N + 1;
  ScriptLines[N] := '}'; N := N + 1;
  ScriptLines[N] := '$leftSvc = Get-Service -Name $svcName -ErrorAction SilentlyContinue'; N := N + 1;
  ScriptLines[N] := '$leftProcs = @(Get-Process -Name $procNames -ErrorAction SilentlyContinue)'; N := N + 1;
  ScriptLines[N] := '$svcOk = ($null -eq $leftSvc) -or ($leftSvc.Status -eq "Stopped")'; N := N + 1;
  ScriptLines[N] := 'if ($svcOk -and $leftProcs.Count -eq 0) { W "结果：GCU 已停干净（服务不存在或已 Stopped；GCU 进程 0 个）" }'; N := N + 1;
  ScriptLines[N] := 'else { W ("结果：未能在 " + $timeoutSec + " 秒内把 GCU 停干净，可能影响后续文件复制（服务状态=" + $leftSvc.Status + " 残留进程=" + $leftProcs.Count + "）") }'; N := N + 1;
  ScriptLines[N] := 'Set-Content -LiteralPath $OutFile -Value $log -Encoding UTF8'; N := N + 1;
  ScriptLines[N] := 'if ($svcOk -and $leftProcs.Count -eq 0) { exit 0 }'; N := N + 1;
  ScriptLines[N] := 'exit 1'; N := N + 1;
  SetArrayLength(ScriptLines, N);
  if not SaveStringsToFile(Helper, ScriptLines, False) then
  begin
    Log('LM-UPDATE: 停 GCU[' + Tag + ']：临时脚本写入失败，回退到单次 taskkill');
    Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('LM-UPDATE: 停 GCU[' + Tag + ']：回退 taskkill 完成 (exit=' + IntToStr(ResultCode) + ')');
    Exit;
  end;
  Exe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Args := '-NoProfile -ExecutionPolicy Bypass -File "' + Helper + '" -OutFile "' + OutFile +
          '" -GcuDir "' + GcuDir + '" -Tag "' + Tag + '"';
  if not Exec(Exe, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('LM-UPDATE: 停 GCU[' + Tag + ']：无法启动 powershell，回退到单次 taskkill');
    Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im GCUBridge.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('LM-UPDATE: 停 GCU[' + Tag + ']：回退 taskkill 完成 (exit=' + IntToStr(ResultCode) + ')');
    Exit;
  end;
  if LoadStringsFromFile(OutFile, ResultLines) then
    for I := 0 to GetArrayLength(ResultLines) - 1 do
      Log('LM-UPDATE: 停 GCU[' + Tag + '] ' + ResultLines[I]);
  Result := (ResultCode = 0);
  if Result then
    Log('LM-UPDATE: 停 GCU[' + Tag + '] 完成：退出码 0（服务已停止/不存在，GCU 进程已清空）')
  else
    Log('LM-UPDATE: 停 GCU[' + Tag + '] 完成：退出码 ' + IntToStr(ResultCode) +
        '（超时后仍有残留，按设计继续安装；可能影响后续复制，详见上面的时间线）');
end;

function UninstallOfficialConsole(): Boolean;
var
  Uninst: String;
  Q: String;
  ResultCode: Integer;
begin
  Result := True;
  // 先停掉官方 GCU 相关进程，避免文件占用导致卸载失败。
  // 2026-10-08：改为可靠停服（停 GCUBridge 并等 Stopped -> 强制结束残留进程并等它们消失）。
  // 此处仍在 InitializeSetup 里，{app} 还没初始化，所以运行目录兜底那一步传空串跳过。
  StopGcuProcessesForUpgrade('官方控制台卸载前', '');
  Uninst := GetOfficialUninstallString();
  if Uninst = '' then
  begin
    Result := True;
    Exit;
  end;
  Q := Trim(Uninst);
  if (Length(Q) > 0) and (Q[1] = '"') then
    Q := Copy(Q, 2, Length(Q) - 2);
  Q := '"' + Q + '"';
  if Exec(ExpandConstant('{cmd}'), '/c ' + Q + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
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
  // 2026-10-09（升级保留修复）：本次"覆盖升级"的备份目录 —— ssInstall 里写入、ssPostInstall 里读回。
  // 放 {commonappdata}\L-Mechrevo\upgrade-backup-<时间戳> 而不是 {tmp}：即使 setup 在
  // "旧卸载器已删 → 新版还没回灌"之间崩溃/被杀，用户数据仍在磁盘上，不会随 {tmp} 一起被清掉。
  GUpgradeBackupRoot: String;

function IsFreshInstallFlag(): Boolean;
begin
  Result := GFreshInstall;
end;

// ==== 2026-10-09：卸载时清理用户数据（交互卸载可选「保留」）====
// 两处 LM 自己写的用户数据（都不在 {app} 下，与 [UninstallDelete] 里 {app} /
// {commonappdata}\L-Mechrevo\logs 那几条互不相干）：
//   ① {userappdata}\MechrevoLite    = %APPDATA%\MechrevoLite（config.json / rgb.cfg / lhm_sensors.txt / log.txt）
//   ② {commonappdata}\MechrevoLite  = C:\ProgramData\MechrevoLite（LM 日志 "Config loaded from ..." 指向的那份权威副本）
// 为什么放 [Code] 而不是 [UninstallDelete]：
//   ① [UninstallDelete] 没有 Check 参数，做不出"交互卸载时让用户选择保留"；
//   ② 本段挂在 usUninstall（用户已确认卸载、程序文件被删之前）：用户在向导里取消 -> 一行都不动。
// 2026-10-09（uninstnobak）：用户选「删除」= 直接删除，不做任何备份、不生成 README-restore.txt；
//   理由：设置里本来就有「导出设置 / 导入设置」两颗按钮，不需要卸载时再偷偷备份一份。
// 静默卸载（/SILENT、/VERYSILENT => UninstallSilent() = True）：不弹任何窗、不阻塞，直接删除
//   （= 交互卸载的默认项，两者结果一致）。
// 交互卸载：Inno 的卸载器没有自定义向导页（CreateCustomPage 只在安装侧可用），所以退化成一条任务对话框，
//   按钮 =「删除」/「保留」，默认按钮是第一个（= 删除）。文案在这里内联（清单中英双语），
//   不走 [CustomMessages]：{cm:} 在本脚本里只用于安装阶段，卸载阶段能否展开无法在本机编译验证。
// 幂等：目录不存在就跳过。

// 目录内容整体拷贝：升级前「备份」与升级后「回灌」两个方向共用（xcopy /Y 覆盖同名文件，
// 所以回灌会盖掉新装/程序写入的默认值）。缺源目录视作成功（无需拷贝）；xcopy 退出码
// 0=已复制、1=源里没有文件可复制，都算成功 —— 1 不是失败：源里本来就没有文件，不存在
// "该备份的没备份"。真实错误（2 用户中断 / 4 初始化失败 / 5 写盘失败）仍然报失败。
function CopyDirContentsLenient(const Src, Dst: String): Boolean;
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
  if Exec(ExpandConstant('{cmd}'), '/c xcopy "' + Src + '\*" "' + Dst + '" /E /I /Y /Q /H', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0) or (ResultCode = 1);
end;

procedure HandleUninstallUserData();
var
  Roots: TArrayOfString;
  I: Integer;
  ExistList, Prompt: String;
  DoDelete: Boolean;
begin
  SetArrayLength(Roots, 2);
  Roots[0] := ExpandConstant('{userappdata}\MechrevoLite');
  Roots[1] := ExpandConstant('{commonappdata}\MechrevoLite');
  ExistList := '';
  for I := 0 to GetArrayLength(Roots) - 1 do
  begin
    if DirExists(Roots[I]) then
    begin
      if ExistList <> '' then
        ExistList := ExistList + '; ';
      ExistList := ExistList + Roots[I];
    end
    else
      Log('LM-UNINST: 用户数据目录不存在，跳过: ' + Roots[I]);
  end;
  if ExistList = '' then
  begin
    Log('LM-UNINST: 两处用户数据目录都不存在，无需删除（幂等跳过）');
    Exit;
  end;
  Log('LM-UNINST: 发现用户数据: ' + ExistList);
  DoDelete := True;
  if not UninstallSilent() then
  begin
    Prompt := ExistList;
    StringChange(Prompt, '; ', #13#10);
    DoDelete := (TaskDialogMsgBox(
      '卸载时一并删除你的自定义设置？ / Delete your custom settings too?',
      '包括：性能模式自定义档（自定义 1/2 的参数与风扇曲线）、灯效与屏幕校色设置、' + #13#10 +
      '传感器与界面偏好。位置：' + #13#10 + Prompt + #13#10 + #13#10 +
      '「删除」= 直接删除，删除后无法找回（默认，推荐）。' + #13#10 + #13#10 +
      '「保留」= 不删除，设置原样留在机器上。' + #13#10 + #13#10 +
      'Includes custom mode profiles and fan curves, lighting and screen-calibration' + #13#10 +
      'settings, sensor/UI preferences. Delete removes them permanently; Keep leaves them untouched.',
      mbConfirmation, MB_YESNO, ['删除 / Delete', '保留 / Keep'], 0) = IDYES);
  end;
  if not DoDelete then
  begin
    Log('LM-UNINST: 用户选择保留自定义文件：未删除（' + ExistList + '）');
    Exit;
  end;
  // DelTree(Path, IsDir, DeleteFiles, DeleteSubdirsAlso)
  for I := 0 to GetArrayLength(Roots) - 1 do
  begin
    if DirExists(Roots[I]) then
    begin
      if DelTree(Roots[I], True, True, True) then
        Log('LM-UNINST: 已删除 ' + Roots[I])
      else
        Log('LM-UNINST: 删除未完成（可能被占用），目录仍在: ' + Roots[I]);
    end;
  end;
  Log('LM-UNINST: 自定义文件清理完成（直接删除，未做任何备份）');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    HandleUninstallUserData();
end;

// 2026-10-09 [EXP] 实验包专用：安装前的醒目实验警告 + 显式确认。
// 只在本 iss 里存在；正式 install_script.iss 不含此段。
// 文案走 [CustomMessages]（中英各一条），用 ExpandMsg 把 {break} 换成真实换行。
function AskExperimentalNotice(): Boolean;
begin
  Result := (MsgBox(ExpandMsg('ExperimentalNotice'), mbConfirmation, MB_YESNO) = IDYES);
  if Result then
    Log('LM-EXP: 用户已确认实验包风险（强制 -Variant 30 / 30 覆盖层铺到非 30 系机器）')
  else
    Log('LM-EXP: 用户在实验警告处选择取消，安装未开始（本机未做任何改动）');
end;

function InitializeSetup(): Boolean;
var
  OfficialUninst: String;
begin
  // 2026-10-05：旧版卸载项存在 = 覆盖升级 -> 保留用户数据；不存在 = 全新安装 -> 清残留使首次引导重现
  // 不能用 ExpandConstant('{app}...')：此刻 {app} 还没初始化，会抛内部错误。
  GFreshInstall := not IsLmAlreadyInstalled();
  Result := True;
  // [EXP] 先弹实验包警告并要用户明确确认（取消 = 一行都不改，直接退出安装）。
  if not AskExperimentalNotice() then
  begin
    Result := False;
    Exit;
  end;
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
  // Upgrade path: the previous L-Mechrevo is uninstalled (after backing up GCU
  // user data) in CurStepChanged(ssInstall); the new files are installed and the
  // backup restored in ssPostInstall. Fresh installs skip the uninstall step.
  OfficialUninst := GetOfficialUninstallString();
  if OfficialUninst <> '' then
  begin
    if MsgBox(ExpandMsg('OfficialConsolePrompt'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not UninstallOfficialConsole() then
      begin
        MsgBox(ExpandConstant('{cm:OfficialConsoleUninstallFailed}'), mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;
  end;
end;

// Read the previous L-Mechrevo uninstall string (same AppId). Empty on fresh installs.
function GetLmUninstallString(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey, 'UninstallString', S) then
    Result := S
  else if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey, 'UninstallString', S) then
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
  if Exec(ExpandConstant('{cmd}'), '/c xcopy "' + Src + '\*" "' + Dst + '" /E /I /Y /Q /H', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

// 2026-10-06（三处修复 B）：GCU 用户数据按 variant 打标，只在同 variant 时回灌 UserPofiles。
// 背景：30 系 payload 的 UserPofiles 与 base（40/50 用）同名但布局不同
//      （payload\30 = Mode1..3 各 5 档；base = Mode1..3 各 2 档 + Mode4 x5），
//      跨变体回灌会把"上一次别的 variant 的同名文件"盖回来造成混档。
// 变体标记由 Install-Gcu.ps1 写在 HKLM\SOFTWARE\L-Mechrevo\GcuVariant，
// 但 Uninstall-Gcu.ps1 卸载时会删掉该值 —— 所以必须在卸载前读出来存进备份目录。
function ReadInstalledGcuVariant(): String;
var
  V: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, GcuVariantRegKey, GcuVariantRegValue, V) then
    Result := Trim(V);
end;

// ==== 2026-10-09（40B）：40 系 GCU 变体询问（40 默认 / 40B 备选）====
// 用户裁定：40B 是 40 系的可选覆盖层（只换 GCU 侧 6 个组件），由「安装时询问」选中；
// 静默安装不弹窗，直接按默认 40（= 沿用改动前的 -Variant Auto，在 40 系机器上判定为 40 那条路）。
// 判据与 [Run] 里 Install-Gcu.ps1 完全同源：同一个 Select-GcuPayload.ps1、同一个 Auto 口径，
// 只做硬件枚举（注册表 / PCI id 读取），不写任何硬件设置、不启停服务、不碰 EC/NVRAM。
var
  // '' = 还没问 / 问过但选了默认（两种情况下 [Run] 都照旧传 Auto）；'40B' = 用户选了备选。
  GCuVariantAnswer: String;
  // {code:} 常量理论上可能被重复展开，保证只问一次。
  GCuVariantAsked: Boolean;

// 2026-10-09 [EXP] 实验包 gen30on40b = install_script.iss 的唯一实验分支：
// 强制 GCU 变体常量。恒为 '30' 时，GetEffectiveGcuVariant / GetGcuVariantParam 都返回它，
// 于是：(1) [Run] 以 -Variant 30 铺 30 覆盖层 -> Install-Gcu.ps1 写 HKLM\SOFTWARE\L-Mechrevo\GcuVariant="30"
//       -> LM 侧 IsGcu30=真 -> 30 系 UI 与性能模式下发链路自然生效（LM 一行代码都不用改）；
//       (2) AskGcuVariantFromUser() 里的"40 系才问"判据（Detected='40'）恒不成立 -> 永不弹 40/40B 询问；
//       (3) ssPostInstall 的变体对照（IncomingVariant=30）与旧变体不一致时按既有设计跳过 UserPofiles 回灌。
// 想恢复"按机器自动判定"只需把这一行删除或改回 'Auto'。
const
  GExpForcedGcuVariant = '30';

// [Run] 实际传给 Install-Gcu.ps1 的 -Variant 值。
function GetEffectiveGcuVariant(): String;
begin
  Result := GExpForcedGcuVariant;
end;

// 本次安装会选哪个变体：用刚复制好的 Select-GcuPayload.ps1，以与 [Run] 里 Install-Gcu.ps1
// 完全相同的入参（GetEffectiveGcuVariant：未作答时 = '-Variant Auto'）算一次，写进 OutFile 再读回。
// 只做硬件枚举（注册表 / PCI id 读取），不写任何硬件设置。
function ResolveIncomingGcuVariant(const OutFile: String): String;
var
  Script, Args: String;
  S: AnsiString;
  ResultCode: Integer;
begin
  Result := '';
  Script := ExpandConstant('{app}\GCU\Select-GcuPayload.ps1');
  if not FileExists(Script) then
  begin
    Log('LM-UPDATE: 变体判定跳过：找不到 ' + Script);
    Exit;
  end;
  DeleteFile(OutFile);
  Args := '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" -Variant ' + GetEffectiveGcuVariant() + ' -OutputFile "' + OutFile + '"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('LM-UPDATE: 变体判定跳过：无法启动 powershell');
    Exit;
  end;
  if (ResultCode <> 0) or (not FileExists(OutFile)) then
  begin
    Log('LM-UPDATE: 变体判定跳过：选择脚本退出码 ' + IntToStr(ResultCode));
    Exit;
  end;
  if LoadStringFromFile(OutFile, S) then
    Result := Trim(S);
end;

// 2026-10-09：只读探测本机是否明确上报「支持核显模式」，用于在询问正文里追加一句推荐。
// ⚠️ 纯提示、不参与任何判定：读不到就返回空串（不猜），默认值与按钮语义一律不变。
// 口径与 MechrevoDeviceCapabilities 一致：认 ItemSupport 的 iGPUModeOnlySupport / IGpuOnlyModeSupport /
// IntegratedGpuOnlySupport（50 系另有 IGpuOnlyConnectionSwitch_Support，那套在 40 系机器上通常缺失）。
function DetectIgpuOnlySupportHint(): String;
var
  S1, S2, S3: Cardinal;
  Has1, Has2, Has3, Supported: Boolean;
begin
  Result := '';
  // ⚠️ Inno 的 Pascal Script 没有 `or else`（短链求值会报 Syntax error），且 `or` 两边都会求值 ——
  //    所以先各自取值、记下"键是否在"，再统一判等，别写成一条长 if。
  Has1 := RegQueryDWordValue(HKLM, 'SOFTWARE\OEM\GamingCenter2\ItemSupport', 'iGPUModeOnlySupport', S1);
  Has2 := RegQueryDWordValue(HKLM, 'SOFTWARE\OEM\GamingCenter2\ItemSupport', 'IGpuOnlyModeSupport', S2);
  Has3 := RegQueryDWordValue(HKLM, 'SOFTWARE\OEM\GamingCenter2\ItemSupport', 'IntegratedGpuOnlySupport', S3);
  Supported := False;
  if Has1 and (S1 = 1) then
  begin
    Supported := True;
    Log('LM-GCU: 只读能力位 iGPUModeOnlySupport=1');
  end;
  if Has2 and (S2 = 1) then
  begin
    Supported := True;
    Log('LM-GCU: 只读能力位 IGpuOnlyModeSupport=1');
  end;
  if Has3 and (S3 = 1) then
  begin
    Supported := True;
    Log('LM-GCU: 只读能力位 IntegratedGpuOnlySupport=1');
  end;
  if Supported then
    Result := '提示：只读检测到本机能力位支持核显模式，建议选「支持核显模式」。' + #13#10 +
              'Tip: a read-only check found this machine supports iGPU-only mode; "Supports iGPU-only" is recommended.';
end;

// 40 系才问；问出来的结果进 GCuVariantAnswer，[Run] 通过 {code:GetGcuVariantParam} 取用。
// 为什么问在这里（[Run] 参数展开时），而不是 CurStepChanged(ssPostInstall) 或向导页：
//   · [Run] 的语义保证它"在程序安装完成之后"才执行 ⇒ 展开 Parameters 时 [Files] 早已铺完，
//     {app}\GCU\Select-GcuPayload.ps1 一定存在（这也是 Install-Gcu.ps1 自己能跑的前提）；
//   · 而 ssPostInstall 与 [Run] 的先后由 Inno 内部步骤决定（历来是 [Run] 先、
//     CurStepChanged(ssPostInstall) 后，见 Inno Main.pas: ProcessRunEntries -> SetStep(ssPostInstall)），
//     把询问放那里赶不上 [Run] 的 -Variant；本设计不依赖这个先后次序 ——
//     {code:} 展开必然发生在 Install-Gcu.ps1 启动之前，无论 Inno 怎么排。
//   · 向导页（wpReady 等）时判据脚本还没铺到 {app}\GCU，除非再加一条 dontcopy 临时解包条目。
//   此刻正好是唯一"先问、再按答案装"的点（向导显示的是 [Run] 的 StatusMsg「正在安装 GCU...」）。
procedure AskGcuVariantFromUser();
var
  Detected, Text, SupportHint: String;
  Buttons: array of String;
begin
  Detected := ResolveIncomingGcuVariant(ExpandConstant('{tmp}\lm-gcu-variant-probe.txt'));
  if CompareText(Detected, '40') <> 0 then
  begin
    Log('LM-GCU: 40 系询问跳过：本次 Auto 判定 = ' + Detected + '（非 40 系，保持原行为）');
    Exit;
  end;
  if WizardSilent() then
  begin
    Log('LM-GCU: 本次 Auto 判定 = 40；静默安装 -> 不弹窗，使用默认 40');
    Exit;
  end;
  // 2026-10-09：问法改成「能力询问」——照首次引导 FirstRunStep4 的口吻问「有没有核显模式这一档」，
  //   因为 40 / 40B 两套 GCU 的实际差别就在此：40 支持核显模式（双显三模档 IGPU_ONLY_CONNECT_RB_*），
  //   40B 是同一代但不带该档的备选套件。
  // ⭐ 回答 -> 变体映射：有（支持核显模式）=> 40；没有（不支持）=> 40B。
  // ⚠️ 2026-10-09：续行的**行首不能是 `#`** —— 否则 Inno 预处理器当成指令报
  //    "Unknown preprocessor directive"。所以 `#13#10` 一律跟在字符串后面，不单独起行。
  Text := '你的电脑有「核显模式」这一档吗？（仅核显输出 / iGPU Only、BIOS 或官方控制台里可查）' + #13#10 + #13#10 +
          '选「支持核显模式 / 有」——装【40 常规套件】（支持核显模式）' + #13#10 +
          '选「不支持 / 没有」——装【40B 备选套件】（不带核显模式这一档）' + #13#10 +
          '不确定的话先用默认（40）。两套只是 GCU 侧组件不同，装完随时可用本安装器换成另一套。' + #13#10 + #13#10 +
          'Does your PC have an "iGPU-only" option (integrated-graphics-only output; check BIOS or the official console)?' + #13#10 +
          'Choose "Supports iGPU-only" -> installs the 40 standard set (iGPU-only supported).' + #13#10 +
          'Choose "Does not support" -> installs the 40B alternate set (no iGPU-only option).' + #13#10 +
          'Not sure? Keep the default (40). Only the GCU-side components differ; you can switch with this installer any time.';
  SupportHint := DetectIgpuOnlySupportHint();
  if SupportHint <> '' then
  begin
    Text := SupportHint + #13#10 + #13#10 + Text;
    Log('LM-GCU: 只读读到本机能力位 iGPUModeOnlySupport=1 -> 正文追加"建议选支持"提示（不影响默认值）');
  end;
  // 只有明确点第二个按钮（TaskDialog 的 IDNO =「不支持」=> 用 40B）才切 40B；
  // 其余任何返回值（含直接关掉对话框）都按默认 40 处理，绝不静默切到备选。
  // ⚠️ 2026-10-09 打包 0.291.3：按钮数组必须先用变量拼好再传 —— Inno Setup 的
  //    预处理器把 [Code] 里任何"以 [ 开头的行"当成段标记（实测 "Invalid section tag"），
  //    所以 ['…','…'] 不能作为独立的续行出现。IDNO 判定与按钮语义映射保持改动前一致（第二颗 = 40B）。
  SetArrayLength(Buttons, 2);
  Buttons[0] := '支持核显模式（装 40）/ Supports iGPU-only (40)';
  Buttons[1] := '不支持（装 40B）/ Does not support (40B)';
  if TaskDialogMsgBox('这台机器支持核显模式吗？ / Does this machine support iGPU-only mode?',
                      Text, mbConfirmation, MB_YESNO,
                      Buttons, 0) = IDNO then
  begin
    GCuVariantAnswer := '40B';
    Log('LM-GCU: 用户选择 40B -> [Run] 以 -Variant 40B 安装 GCU 侧覆盖层');
  end
  else
    Log('LM-GCU: 用户选择 40（默认）-> [Run] 继续以 -Variant Auto（本次判定 40）安装');
end;

// [Run] 的 Parameters 里用 {code:GetGcuVariantParam} 调用（脚本常量，Inno 允许在 [Run]
// Parameters 这种支持常量的位置使用）。
// ⚠️ [EXP] 实验包：本包 [Run] 已改成直接写常量 30，正常路径不会走到这里；
//    此函数仍恒返回 GExpForcedGcuVariant，且刻意不调用 AskGcuVariantFromUser()
//    —— 就算有人把 [Run] 改回 {code:GetGcuVariantParam}，40 系询问也不会弹、变体仍是 30。
function GetGcuVariantParam(Param: String): String;
begin
  Result := GExpForcedGcuVariant;
end;

function ReadVariantFile(const Path: String): String;
var
  S: AnsiString;
begin
  Result := '';
  if FileExists(Path) and LoadStringFromFile(Path, S) then
    Result := Trim(S);
end;

// Upgrade path: back up the GCU user data (custom mode profiles), uninstall the previous
// L-Mechrevo, then the new files are installed and the backup is restored afterwards
// (see CurStepChanged ssPostInstall) — but only when the GCU variant is unchanged.
procedure BackupAndUpgrade();
var
  GcuRoot, LiveRoot, BackupRoot, Uninst, Q, OldVariant, UserSource: String;
  LmRoots, LmLeafs: TArrayOfString;
  I, LmBackupFail: Integer;
  ResultCode: Integer;
begin
  GcuRoot := ExpandConstant(GcuUserRoot);
  LiveRoot := ExpandConstant(GcuLiveUserRoot);
  // 2026-10-09（升级保留修复）：备份目录从 {tmp}\lm-upgrade-backup 改为持久目录
  //   {commonappdata}\L-Mechrevo\upgrade-backup-<时间戳>。原因：{tmp} 在 setup 退出时被清掉，
  //   一旦"旧卸载器已删、新版回灌失败/崩溃"，备份就随 {tmp} 消失；持久目录让数据永远可手工找回。
  //   时间戳到秒，重复升级各留一个目录；同秒重复进入（几乎不可能）则复用并覆盖同名文件（幂等）。
  //   本目录与卸载侧的 userdata-backup-* 同级但前缀不同，两者互不干扰；旧卸载器的
  //   [UninstallDelete] 只删 {commonappdata}\L-Mechrevo\logs，不会碰这里。
  BackupRoot := ExpandConstant('{commonappdata}\L-Mechrevo\upgrade-backup-' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':'));
  GUpgradeBackupRoot := BackupRoot;
  if DirExists(BackupRoot) then
    Log('LM-UPDATE: 备份目录已存在（同一秒内重复进入升级）-> 复用并覆盖同名文件: ' + BackupRoot)
  else
    ForceDirectories(BackupRoot);
  if not DirExists(BackupRoot) then
  begin
    Log('LM-UPDATE: 备份目录创建失败: ' + BackupRoot + ' -> 中止升级（旧卸载器尚未运行，未做任何删除）');
    MsgBox('无法创建自定义设置的备份目录，升级已中止。' + #13#10 +
           '本次没有删除任何数据，旧版仍可正常使用。' + #13#10 + #13#10 +
           'Failed to create the backup folder; the upgrade was aborted before the previous' + #13#10 +
           'version was uninstalled, so nothing has been deleted.', mbError, MB_OK);
    Abort();
  end;
  Log('LM-UPDATE: 检测到旧版，开始升级流程（备份->卸载->安装->恢复）；备份目录 = ' + BackupRoot);
  // 1) Make sure L-Mechrevo exits so the uninstaller can delete {app}.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im L-Mechrevo.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('LM-UPDATE: L-Mechrevo.exe 已尝试结束 (exit=' + IntToStr(ResultCode) + ')');
  // 2) 变体打标：先记下旧版装的是哪个 GCU 变体（卸载会删掉 HKLM 里的 GcuVariant）。
  OldVariant := ReadInstalledGcuVariant();
  SaveStringToFile(BackupRoot + '\previous-variant.txt', OldVariant, False);
  if OldVariant = '' then
    Log('LM-UPDATE: 旧版 GCU 变体标记缺失（HKLM\SOFTWARE\L-Mechrevo\GcuVariant）→ 本次不回灌自定义模式档')
  else
    Log('LM-UPDATE: 旧版 GCU 变体 = ' + OldVariant);
  // 2.1) 备份自定义模式档：优先取运行目录（GCU 服务实际读写的那份，2026-10-06 本机实测
  //      该目录里的文件是当天被服务改写的，而 base 暂存树里那份还是 2025-09-12 的出厂文件），
  //      运行目录不存在时回退到 base 暂存树（保持改动前的行为）。
  if DirExists(LiveRoot + '\UserPofiles') then
    UserSource := LiveRoot + '\UserPofiles'
  else
    UserSource := GcuRoot + '\UserPofiles';
  if not CopyDirContents(UserSource, BackupRoot + '\UserPofiles') then
  begin
    Log('LM-UPDATE: 备份自定义模式档失败: ' + UserSource);
    MsgBox('备份自定义模式档失败，已中止升级。', mbError, MB_OK);
    Abort();
  end;
  Log('LM-UPDATE: 自定义模式档已备份: ' + UserSource);
  // 2.2) 按用户说明（2026-10-06）：风扇表一般写在 EC 里，这里不再搬运/合并
  //      payload\...\UserFanTables 机型风扇表（改动前会把它备份并回灌）。
  //      LM 自己配置里的风扇曲线/模式配置（%APPDATA%\MechrevoLite）不在这条流程里，
  //      改由下面的 2.6 单独备份（2026-10-09 起）—— 那才是升级时真正会被旧卸载器删掉的一份。
  Log('LM-UPDATE: 按用户说明跳过 UserFanTables 备份（风扇表由 EC 管理，不跨变体搬运）');
  // 2.5) Back up custom ICC profiles (old version is still in {app} at ssInstall).
  if CopyDirContents(ExpandConstant('{app}\customICC'), BackupRoot + '\customICC') then
    Log('LM-UPDATE: 自定义校色 ICC 已备份')
  else
    Log('LM-UPDATE: 备份自定义校色 ICC 失败（继续升级）');
  // 2.6) 2026-10-09（升级保留修复）：LM 自己的两处用户数据也必须在调用旧卸载器【之前】备份。
  //      背景：升级正是用 `/VERYSILENT` 调【旧卸载器】，而本版卸载器在静默时 = 先备份再删除
  //      %APPDATA%\MechrevoLite 与 C:\ProgramData\MechrevoLite（见 HandleUninstallUserData）；
  //      上一批的 ssPostInstall 只回灌 GCU UserPofiles/customICC，不含这两处 ⇒ 每次覆盖升级都会
  //      静默清掉自定义档/风扇曲线/灯效/校色且不恢复。这里备份进同一个升级备份目录，
  //      由 ssPostInstall 回灌（xcopy /Y 覆盖新装/程序写入的默认值，用户旧设置胜出）。
  //      "原目录 -> 备份叶子名"与卸载侧 HandleUninstallUserData 完全同一对口径，备份目录可直接
  //      按同名手工拷回，不需要额外说明文档。
  //      失败语义：任一处备份失败 = 立即 Abort 升级 —— 此刻旧卸载器还没运行，两处用户数据
  //      一行未删，机器保持旧版完整安装 ⇒ 绝不会出现"删了没备份"。
  SetArrayLength(LmRoots, 2);
  LmRoots[0] := ExpandConstant('{userappdata}\MechrevoLite');
  LmRoots[1] := ExpandConstant('{commonappdata}\MechrevoLite');
  SetArrayLength(LmLeafs, 2);
  LmLeafs[0] := 'roaming-MechrevoLite';
  LmLeafs[1] := 'programdata-MechrevoLite';
  LmBackupFail := 0;
  for I := 0 to GetArrayLength(LmRoots) - 1 do
  begin
    if not DirExists(LmRoots[I]) then
      Log('LM-UPDATE: LM 用户数据目录不存在（无需备份；回灌时同样跳过，保持新装状态）: ' + LmRoots[I])
    else if CopyDirContentsLenient(LmRoots[I], BackupRoot + '\' + LmLeafs[I]) then
      Log('LM-UPDATE: LM 用户数据已备份: ' + LmRoots[I] + ' -> ' + BackupRoot + '\' + LmLeafs[I])
    else
    begin
      LmBackupFail := LmBackupFail + 1;
      Log('LM-UPDATE: LM 用户数据备份失败（第 ' + IntToStr(LmBackupFail) + ' 处）: ' + LmRoots[I]);
    end;
  end;
  if LmBackupFail > 0 then
  begin
    Log('LM-UPDATE: 用户数据备份失败 ' + IntToStr(LmBackupFail) + ' 处 -> 中止升级；旧卸载器未运行，用户数据未被删除');
    MsgBox('备份你的自定义设置（自定义模式档/风扇曲线/灯效/屏幕校色）失败，升级已中止。' + #13#10 +
           '你的设置原样保留：本次没有删除任何数据，旧版仍可正常使用。' + #13#10 +
           '请确认磁盘空间与权限后重试升级。' + #13#10 + #13#10 +
           'Failed to back up your custom settings; the upgrade was aborted BEFORE the previous' + #13#10 +
           'version was uninstalled, so nothing has been deleted.', mbError, MB_OK);
    Abort();
  end;
  // 3) Stop GCU for real to release file locks (2026-10-08 fix): the previous single taskkill was
  //    not enough — the service could still be up (and its process respawned) and hold
  //    {app}\GCU\AiStoneService files while the overlay copy ran, which made robocopy exit 11.
  //    No separate GCU uninstall call: the old version's own uninstaller (unins000) removes the
  //    GCU service via its [UninstallRun] Uninstall-Gcu.ps1, so uninstalling L-Mechrevo is enough.
  StopGcuProcessesForUpgrade('升级前(ssInstall)', ExpandConstant('{app}\GCU\AiStoneService'));
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
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  GcuRoot, BackupRoot, OldVariant, IncomingVariant, VariantUserRoot: String;
  LmRoots, LmLeafs: TArrayOfString;
  J, RestoreFail: Integer;
begin
  if CurStep = ssInstall then
  begin
    if GetLmUninstallString() <> '' then
      BackupAndUpgrade()
    else
      Log('LM-UPDATE: 全新安装，跳过升级流程');
  end
  else if CurStep = ssPostInstall then
  begin
    // 2026-10-06（三处修复 B）：回灌 GCU 用户数据，但只在 variant 未变时。
    // 旧变体从卸载前存下的 previous-variant.txt 读，新变体用 Select-GcuPayload.ps1 现算。
    // 2026-10-09（升级保留修复）：同一步里回灌 LM 自己的两处用户数据（%APPDATA%\MechrevoLite、
    //   C:\ProgramData\MechrevoLite），xcopy /Y 覆盖新装写入的默认值 —— 用户旧设置胜出。
    //   本步在 [Run]（Install-Gcu.ps1 铺载荷 + 启动 L-Mechrevo）之前，所以程序启动时读到的
    //   就是用户升级前的设置，而不是默认值。
    if GUpgradeBackupRoot = '' then
      GUpgradeBackupRoot := ExpandConstant('{tmp}\lm-upgrade-backup');   // 兜底：保持改动前的位置
    BackupRoot := GUpgradeBackupRoot;
    if DirExists(BackupRoot) then
    begin
      RestoreFail := 0;
      GcuRoot := ExpandConstant(GcuUserRoot);
      OldVariant := ReadVariantFile(BackupRoot + '\previous-variant.txt');
      IncomingVariant := ResolveIncomingGcuVariant(BackupRoot + '\incoming-variant.txt');
      Log('LM-UPDATE: GCU 变体对照 旧=' + OldVariant + ' 新=' + IncomingVariant);
      if (OldVariant <> '') and (IncomingVariant <> '') and (CompareText(OldVariant, IncomingVariant) = 0) then
      begin
        if not CopyDirContentsLenient(BackupRoot + '\UserPofiles', GcuRoot + '\UserPofiles') then
          RestoreFail := RestoreFail + 1;
        // 30 系在 payload\30 里有自己的 UserPofiles 覆盖 base（40/50 没有那份），
        // 必须同时回灌到该变体自己的目录，否则会被 overlay 的默认值盖掉。
        VariantUserRoot := ExpandConstant('{app}\GCU\payload\' + IncomingVariant + '\AiStoneService\MyControlCenter\UserPofiles');
        if DirExists(VariantUserRoot) then
        begin
          if not CopyDirContentsLenient(BackupRoot + '\UserPofiles', VariantUserRoot) then
            RestoreFail := RestoreFail + 1;
          Log('LM-UPDATE: 自定义模式档同时回灌到变体目录 ' + VariantUserRoot);
        end;
        Log('LM-UPDATE: GCU 变体一致（' + IncomingVariant + '），自定义模式档已回灌');
      end
      else
        Log('LM-UPDATE: GCU 变体不一致或未知（旧=' + OldVariant + '，新=' + IncomingVariant + '），跳过自定义模式档回灌以避免混档');
      // 校色 ICC 与 GCU 变体无关，始终恢复。
      if not CopyDirContentsLenient(BackupRoot + '\customICC', ExpandConstant('{app}\customICC')) then
        RestoreFail := RestoreFail + 1;
      // UserFanTables 按用户说明不搬运：既不再备份也不再回灌（机型风扇表由 EC 管理）。
      Log('LM-UPDATE: GCU 用户数据恢复步骤完成（ICC 已恢复；UserFanTables 未搬运）');
      // LM 自己的两处用户数据回灌。与卸载侧同一对"原目录 <- 备份叶子名"口径：
      //   roaming-MechrevoLite     -> %APPDATA%\MechrevoLite
      //   programdata-MechrevoLite -> C:\ProgramData\MechrevoLite
      // 覆盖语义：xcopy /Y 让备份里的文件胜出（升级不重置用户设置）。升级前不存在的目录
      // （未备份）跳过 = 保持新装状态，由程序首次运行自行创建，不算失败。
      SetArrayLength(LmRoots, 2);
      LmRoots[0] := ExpandConstant('{userappdata}\MechrevoLite');
      LmRoots[1] := ExpandConstant('{commonappdata}\MechrevoLite');
      SetArrayLength(LmLeafs, 2);
      LmLeafs[0] := 'roaming-MechrevoLite';
      LmLeafs[1] := 'programdata-MechrevoLite';
      for J := 0 to GetArrayLength(LmRoots) - 1 do
      begin
        if not DirExists(BackupRoot + '\' + LmLeafs[J]) then
          Log('LM-UPDATE: LM 用户数据升级前不存在（未备份）-> 保持新装状态: ' + LmRoots[J])
        else if CopyDirContentsLenient(BackupRoot + '\' + LmLeafs[J], LmRoots[J]) then
          Log('LM-UPDATE: LM 用户数据已回灌（覆盖新装默认值）: ' + BackupRoot + '\' + LmLeafs[J] + ' -> ' + LmRoots[J])
        else
        begin
          RestoreFail := RestoreFail + 1;
          Log('LM-UPDATE: LM 用户数据回灌失败: ' + BackupRoot + '\' + LmLeafs[J] + ' -> ' + LmRoots[J]);
        end;
      end;
      // 全部回灌成功才清理备份目录；有失败就保留（含 LM 两处 + GCU 档 + ICC），并给出路径。
      if RestoreFail = 0 then
      begin
        if DelTree(BackupRoot, True, True, True) then
          Log('LM-UPDATE: 升级回灌全部成功 -> 升级备份目录已清理: ' + BackupRoot)
        else
          Log('LM-UPDATE: 升级回灌成功但备份目录清理失败（无害，可手工删除）: ' + BackupRoot);
      end
      else
      begin
        Log('LM-UPDATE: 升级回灌有 ' + IntToStr(RestoreFail) + ' 处失败 -> 保留升级备份目录供手工恢复: ' + BackupRoot);
        MsgBox('升级时恢复你的自定义设置有 ' + IntToStr(RestoreFail) + ' 处失败。' + #13#10 +
               '升级前的完整备份已保留在下面这个目录（先完全退出程序再手工拷回）：' + #13#10 +
               '    roaming-MechrevoLite     -> %APPDATA%\MechrevoLite' + #13#10 +
               '    programdata-MechrevoLite -> C:\ProgramData\MechrevoLite' + #13#10 + #13#10 +
               BackupRoot + #13#10 + #13#10 +
               'Some custom settings could not be restored; a full backup is kept in the folder' + #13#10 +
               'above (copy roaming-/programdata-MechrevoLite back to their original locations).',
               mbError, MB_OK);
      end;
    end
    else
      Log('LM-UPDATE: 无备份可恢复（全新安装）');
    // Keep the full setup log next to the app for troubleshooting.
    if CopyFile(ExpandConstant('{log}'), ExpandConstant('{app}\install.log'), False) then
      Log('LM-UPDATE: 安装日志已保存到 {app}\install.log')
    else
      Log('LM-UPDATE: 安装日志复制失败');
  end;
end;

// Stop the bundled GCU processes BEFORE any file is overwritten, so the old
// service cannot hold file locks during the copy. The GCU service itself is
// removed by the old version's uninstaller ([UninstallRun] Uninstall-Gcu.ps1)
// during the upgrade path, so no separate uninstall call is needed here.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  GcuTarget: String;
begin
  Result := '';
  GcuTarget := ExpandConstant('{app}\GCU');
  // 1) 先可靠停服 + 杀进程 + 等它们真的退出，解除 {app} 下的文件占用（失败不致命，只记日志）。
  //    2026-10-08：原来只发一次 taskkill，服务/进程可能仍在（或被 SCM 拉起），
  //    会把 {app}\GCU\AiStoneService 的文件锁到 Install-Gcu.ps1 复制阶段。
  StopGcuProcessesForUpgrade('写入前(PrepareToInstall)', GcuTarget + '\AiStoneService');
end;

function NeedRestart(): Boolean;
begin
  Result := True;
end;
