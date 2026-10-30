; Created by "innounp" version 2.67.11
; Setup file: pkg-20260919-195709-a076c697.exe
; Inno Setup Version: 6.7.0 (Unicode)

[Setup]
AppName=L-Mechrevo
AppVerName=L-Mechrevo 0.293.6.3 beta23.6.3
AppId={{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}
AppVersion=0.293.6.3
AppPublisher=L-Mechrevo contributors
AppCopyright=Copyright (C) 2026 L-Mechrevo contributors. GPL-3.0-only.
DefaultDirName={autopf}\L-Mechrevo
DefaultGroupName=L-Mechrevo
#ifdef NOGCU
OutputBaseFilename=LM-0.293.6.3-beta23.6.3-baolongda-setup
#else
#ifdef NOGCU_PAYLOAD
OutputBaseFilename=LM-0.293.6.3-beta23.6.3-nogcupayload-setup
#else
OutputBaseFilename=LM-0.293.6.3-beta23.6.3-setup
#endif
#endif
UninstallDisplayIcon={app}\L-Mechrevo.exe
SetupIconFile=files\favicon.ico
VersionInfoVersion=0.293.6.3
VersionInfoProductVersion=0.293.6.3
UninstallDisplayName=L-Mechrevo 0.293.6.3 beta23.6.3
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
; 2026-10-22（无界风扇 I2EC）：InpOut32/64 用户态 DLL（MIT 式许可，见 LICENSES\LICENSE-InpOut32.txt）。
;   LM 从 {app}\inpoutx64.dll 直接加载；驱动 sys 放 {app}\inpoutx64\，由 Install-InpOut.ps1 在无界机型上注册服务。
Source: "files\inpoutx64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\inpoutx64\inpoutx64.sys"; DestDir: "{app}\inpoutx64"; Flags: ignoreversion
Source: "files\Install-InpOut.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\LICENSE-InpOut32.txt"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
Source: "files\L-Mechrevo.deps.json"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\MechrevoLite.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "files\L-Mechrevo.dll.config"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\L-Mechrevo.exe"; DestDir: "{app}"; Flags: ignoreversion 
; 2026-10-13（r25）：⛔ 【不再打包启动壳】。用户原话：
;   「然后我们启动壳删除吧,想想微软自带的那个下载已经有了 删了干净一点，也不会引入新的bug」
;   理由（⭐ 也供将来写更新日志）：
;     ① 缺 .NET 10 桌面运行时这件事，安装阶段已改成【安装器自己下载 + 静默安装】
;        （[Code] 的 NetDlTryInstallRuntime / NetDlDownloadToTmp / NetDlRunInstaller；
;         只有下载或安装失败才弹那个选择框）=> 壳不再是「唯一的提示来源」；
;     ② 万一装完运行时仍缺失（用户跳过了 / 下载失败又选了「稍后」），双击 L-Mechrevo.exe 时
;        apphost 会弹微软自带那句英文提示（焊在 exe 里，改不了）=> 覆盖面已经够了；
;     ③ 壳自身还带一类 bug（它那侧 explorer 的错误路径，r24 才修掉）=> 删掉更干净、少一类维护面。
;   ⚠️ 源码工程 tools\LmLauncherStub\【保留】（不再随包发；要恢复只改 [Files]/[Icons]/[Run] 三处指向）。
;   ⚠️ 已构建的 installer-src\files\L-Mechrevo-Launcher.exe 已移出 files\（备份在
;      backup\iss\r25_20261013_pre_netdl_launcher_removal\）=> check_iss_files.py 不再把它算冗余。
Source: "files\L-Mechrevo.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\LibreHardwareMonitorLib.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Microsoft.Win32.TaskScheduler.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Microsoft.Windows.SDK.NET.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\MQTTnet.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\NvAPIWrapper.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Probe.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\RAMSPDToolkit-NDD.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\System.Management.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\THIRD_PARTY_NOTICES,1.txt"; DestDir: "{app}\LICENSES"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion 
Source: "files\WinRT.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Gaming.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Office.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\DefaultCurve_Turbo.json"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode1.jpg"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode2.jpg"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode3.png"; DestDir: "{app}\Resources"; Flags: ignoreversion 
Source: "files\Resources\qrcode4.png"; DestDir: "{app}\Resources"; Flags: ignoreversion

Source: "files\Resources\qrcode1.jpg"; DestDir: "{app}\Assets\Resources"; Flags: ignoreversion
Source: "files\Resources\qrcode2.jpg"; DestDir: "{app}\Assets\Resources"; Flags: ignoreversion
Source: "files\Resources\qrcode3.png"; DestDir: "{app}\Assets\Resources"; Flags: ignoreversion
Source: "files\Resources\qrcode4.png"; DestDir: "{app}\Assets\Resources"; Flags: ignoreversion
;   ⭐⭐ 2026-10-17 修复（真机反馈「熄屏不睡眠点不动 · 报错缺失组件 usbmmidd_v2」）：
;   ⚠️ 根因：⭐ 驱动组件【一直在】`installer-src\files\usbmmidd_v2`（7 个文件 ✅），
;      但 ⛔ **本文件的 [Files] 从来没有引用过它**（`git log -S usbmmidd -- installer-src/install_script.iss` 空 ✅）
;      ⇒ ⭐ 装完 `{app}\usbmmidd_v2` 根本不存在 ⇒ ⭐ `VirtualDisplayBlank.VerifyDriverFiles` 判缺失
;      ⇒ ⭐ 点「熄屏」只回一句「组件缺失」❌（⭐ 与 UWACPIDriver 那次「文件在、但没被安装」同一类坑 ⚠️）
;   ✅ 修法：⭐ 用通配符整目录铺到 `{app}\usbmmidd_v2`（⭐ 与 `GetDriverDir()` 的路径逐字一致 ✅）
;   ⚠️ 许可：⭐ 该驱动按自带 `usbmmidd_v2\License.txt`（Amyuni）分发，⛔ 不另给许可文本（原样带上 ✅）
Source: "files\usbmmidd_v2\*"; DestDir: "{app}\usbmmidd_v2"; Flags: ignoreversion 
Source: "files\zh-CN\L-Mechrevo.resources.dll"; DestDir: "{app}\zh-CN"; Flags: ignoreversion 
Source: "files\zh-CN\MechrevoLite.Core.resources.dll"; DestDir: "{app}\zh-CN"; Flags: ignoreversion 
Source: "files\zh-CN\Microsoft.Win32.TaskScheduler.resources.dll"; DestDir: "{app}\zh-CN"; Flags: ignoreversion 
Source: "files\THIRD_PARTY_NOTICES,2.txt"; DestDir: "{app}\LICENSES"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion

; 2026-10-10 合规（包内补齐）：应用内「关于 L-Mechrevo —— 开源许可与致谢」窗口的说明文案承诺
;   「完整许可文本位于程序目录下的 LICENSES 文件夹」（Strings.AboutLicensesIntro / 英文版同义），
;   且「打开 LICENSES 文件夹」按 Path.Combine(AppContext.BaseDirectory, "LICENSES") 打开
;   （source\MechrevoLite\AboutLicensesForm.cs:310）—— AppContext.BaseDirectory 即 {app}，
;   故目标目录定为 {app}\LICENSES（README 与根 LICENSE 所描述的 "..\THIRD-PARTY-NOTICES.md" 亦指 {app}）。
;   此前包内只有 GPL-3.0 正文（{app}\LICENSE.txt），既无 LICENSES\ 也无根 LICENSE ⇒ 文案与现实不符。
;   ⚠️ 源取 installer-src 的上级（仓库根），与源码包里的 LICENSE / LICENSES\ 是同一份，不另做副本。
Source: "..\LICENSE"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
Source: "..\LICENSES\GPL-3.0.txt"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
Source: "..\LICENSES\LGPL-2.1.txt"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
Source: "..\LICENSES\README.md"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
; 2026-10-18 合规（MIT 归属）：内部笔记引用过 JiaolongControl 的代码片段 ⇒ 随包分发 MIT 全文（Copyright (C) 2025 GaoXanSheng）。
Source: "..\LICENSES\MIT-JiaolongControl.txt"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
; 2026-10-10 (repack9)：随包的两份文档都指向它（根 LICENSE 的横幅与
;   LICENSES\README.md 的 "..\THIRD-PARTY-NOTICES.md"）——不纳入则目标机上这两处引用悬空；
;   它也是包内内嵌模块 IntelMSR.bin 的权威清单（§1.2 B4）。ATTRIBUTION-PATCH-PLAN.md 属内部文档，不纳入。
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
; 2026-10-13（目录整理，owner 原话「整理一下程序目录显得没那么乱 我看很多许可协议直接放根目录的」）：
;   许可类文本此前有 5 份散在 {app} 根目录（LICENSE / LICENSE.txt / THIRD_PARTY_NOTICES.txt /
;   THIRD-PARTY-NOTICES.md / COPYING.PawnIO.txt）—— 本轮全部收进既有的 {app}\LICENSES\，
;   与 GPL-3.0.txt / LGPL-2.1.txt / README.md 同处一个文件夹。
;   ⚠️ 不新建第二个许可目录：应用内「开源许可」窗口读的就是 {app}\LICENSES
;     （source\MechrevoLite\AboutLicensesForm.cs:330 Path.Combine(AppContext.BaseDirectory, "LICENSES")）。
;   随包文档（更新日志.txt / 用前必看.txt）另收进 {app}\Docs\。
;   ⚠️ 文件名与内容一个字节都没改，只改 DestDir；[Icons] 按新路径同步（见该段）。
;   ⚠️ 上方 2026-10-10 那段注释（「目标目录定为 {app}\LICENSES」「根 LICENSE」「{app}\LICENSE.txt」）
;     描述的是当时的布局，已被本段取代 —— 历史注释不回改，以此段为准。

; 2026-10-10 合规：内嵌的 RyzenSMU.bin 为 LGPL-2.1-or-later，必须随二进制分发许可文本。
; 0.290.6 曾以 COPYING.PawnIO.txt 之名分发同一文件（见 _upstream\0.290.6\extract\install_script.iss:32），
; 当前 installer-src 漏掉了它，且我方的 PawnIO 归属段落也被删除 —— 两处一并恢复。
; 2026-10-27：PawnIO 模块改为外部文件，不再内嵌 MechrevoLite.Core.dll
Source: "files\PawnIO\LMechrevo.RyzenSMU.bin"; DestDir: "{app}\PawnIO"; Flags: ignoreversion
Source: "files\PawnIO\LMechrevo.IntelMSR.bin"; DestDir: "{app}\PawnIO"; Flags: ignoreversion
Source: "files\PawnIO\LMechrevo.LpcACPIEC.bin"; DestDir: "{app}\PawnIO"; Flags: ignoreversion
Source: "files\PawnIO\LMechrevo.LpcIO.bin"; DestDir: "{app}\PawnIO"; Flags: ignoreversion
Source: "files\COPYING.PawnIO.txt"; DestDir: "{app}\LICENSES"; Flags: ignoreversion
; 2026-10-10：app 仍在调用 {app}\Install-PawnIO.ps1（source\MechrevoLite\CpuTuningForm.cs:1710），
; 但当前 installer-src\files 里没有它 —— 恢复，否则「下载 PawnIO 驱动」按钮静默失败。
Source: "files\Install-PawnIO.ps1"; DestDir: "{app}"; Flags: ignoreversion 
Source: "files\更新日志.txt"; DestDir: "{app}\Docs"; Flags: ignoreversion 
Source: "files\用前必看.txt"; DestDir: "{app}\Docs"; Flags: ignoreversion 
Source: "files\GCU\Select-GcuPayload.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion; Check: ShouldInstallGcu 
Source: "files\GCU\Install-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion; Check: ShouldInstallGcu 
Source: "files\GCU\Uninstall-Gcu.ps1"; DestDir: "{app}\GCU"; Flags: ignoreversion; Check: ShouldInstallGcu
; ============================================================================
; 2026-10-13（owner 指示：「自带 ACPI 驱动」= 把 UWACPIDriver 变成标准自带）：
;   UWACPIDriver 三件此前只随 base payload 树（{app}\GCU\payload\GCU\UWACPIDriver）落盘，
;   再由 Install-Gcu.ps1 复制到 {app}\GCU\UWACPIDriver。本轮把这三件【直接】列入标准文件集，
;   于是不论 Select-GcuPayload.ps1 选中哪个变体（无 / 30 / 40 / 50），也不论 GCU 步骤
;   成功与否，目标目录都必定拿到 .inf / .cat / .sys 三件。
;   ⛔ 未把 legacy 10/20 系的 ACPIDriver 一起带进来（owner 说过先不放）。
; ----------------------------------------------------------------------------
; 2026-10-13 第二轮（owner 原话：「UWACPIDriver 放根目录吧 netsdk 这个干什么用的 …」）：
;   目标目录由 {app}\GCU\UWACPIDriver 改到【{app}\UWACPIDriver】（应用根目录）。
;   Install-Gcu.ps1 已同步：$driverTarget 改为「由 -AppExe 推出的应用根目录 \UWACPIDriver」，
;   取不到 AppExe 时回退 <TargetDir>\UWACPIDriver；若根目录缺 .inf 再回退这份旧 GCU 位置
;   （根优先、GCU 兜底，见 Install-Gcu.ps1 main 段）。
;   ⚠️ 下面 3 条 DestDir 必须与 Install-Gcu.ps1 解析出的 $driverTarget 一致，⛔ 不得改名。
;   ⚠️ 旧安装（<= 0.291.7.3 首轮布局）残留的 {app}\GCU\UWACPIDriver 由 [UninstallDelete] 清。
; ----------------------------------------------------------------------------
; ⭐ 2026-10-14（重复条目清理，owner 指出）：本段原本有【两个来源各 3 条】共 6 条 UWACPIDriver：
;   ① 下面这 3 条（shared\UWACPIDriver\<file> -> {app}\UWACPIDriver）—— 【保留】；
;   ② 曾把 shared\UWACPIDriver\* 再铺一份到 {app}\GCU\shared\UWACPIDriver —— 【已删】：
;      {app}\GCU\shared\ 这个层级整体取消（owner：「payload 应该作为临时文件而不是安装到安装目录里面」
;      「ps1 留 7z 不留 没用到的 GCU 也不留」），装完 {app}\GCU 下只有 AiStoneService\ + logo\ + 3 个 ps1；
;   ③ 曾用 shared\UWACPIDriver\{cat,inf,sys} 再写一遍 {app}\UWACPIDriver —— 【已删】：与下面 3 条
;      源和目标都完全相同，纯重复（ISCC 不会报错，但会让"实际装了哪些文件"无法从清单一眼看清）。
;   ⚠️ 因此 {app}\GCU\shared\UWACPIDriver 里的那份 staging 副本也不再存在；Install-Gcu.ps1 的
;      $driverSource（shared\UWACPIDriver）只对「手工 / 手工分发的 StagingRoot」有意义，
;      正常安装走的是 {app}\UWACPIDriver 里已落盘的那一份（脚本对缺失的 staging 源只记日志、不中止）。
; ============================================================================
; 2026-10-14（UWACPIDriver 来源与「星耀通用性」如实标注，owner 确认随包）：
;   这三件的**唯一来源**是 shared\UWACPIDriver\，DriverVer = 10/28/2025, 10.48.11.331（A 分支 / 更新分支）。
;   ⚠️ 2026-10-14 更正（本轮实测，⛔ 推翻本段原注释）：原注释写「⛔ 绝对不要从 50.7z 里抽，50.7z 里那份
;      DriverVer 是 08/27/2024, 11.2.18.347，更旧」—— 实测不成立：50.7z\50\UWACPIDriver\ 的三件与
;      shared\UWACPIDriver\ 【逐字节相同】（cat/inf/sys 的 SHA256 全等，DriverVer 同为 10/28/2025,
;      10.48.11.331），与 payload-old\GCU\UWACPIDriver\ 也逐字节相同。所以"从 7z 抽会降级"这个理由已作废；
;      ⚠️ 但**结论仍然有效**：本段照旧以 shared\UWACPIDriver\ 为唯一来源 —— 因为 50.7z 只有 50 系有，
;      而 1020/30/40/WJ 四个 7z 里的驱动并不是本驱动；从 shared\ 取才能保证「不论选中哪个变体，
;      {app}\UWACPIDriver\ 都拿到同一份」。
;   ⚠️ 星耀通用性**未证实**（如实记录，⛔ 不得写成"已确认星耀可用"）：
;      · inf 的 [Manufacturer] / [Models] 段里，A（星耀 / 无界）与 B（UX7）逐字节相同；
;      · 整份 inf **只有一个硬件 ID**：acpi\INOU0000；
;      · 未证实：ACPI\INOU0000 这个设备在星耀真机上是否真的存在（需要真机只读查询）；
;      · 用户口径：「目前只是不确定和星耀是否通用，其他应该是通用的」。
; ============================================================================
#ifndef NOGCU
Source: "files\GCU\shared\UWACPIDriver\uwacpidriver.cat"; DestDir: "{app}\UWACPIDriver"; Flags: ignoreversion; Check: ShouldInstallGcu
Source: "files\GCU\shared\UWACPIDriver\UWACPIDriver.inf"; DestDir: "{app}\UWACPIDriver"; Flags: ignoreversion; Check: ShouldInstallGcu
Source: "files\GCU\shared\UWACPIDriver\UWACPIDriver.sys"; DestDir: "{app}\UWACPIDriver"; Flags: ignoreversion; Check: ShouldInstallGcu
#endif
; ============================================================================
; 2026-10-10（repack5）：用户把 base GCU 有意换成"老一点的 50 系 GCU"（50old 世代），
; 本段 [Files] 据此重排为"恰好引用磁盘上存在的文件"——删除 157 条磁盘已无的旧条目
; （base GCU 的 SQLite/SystemHelper/rtknrai/snynrai 五件、UserFanTables 下 25 组 PH4*/PH6*
; 机型档共 139 条、UEFI_Firmware.dll，以及 40 的 GCUBridge.exe，以及已随 40B 整体移除而作废的
; 40B 覆盖层 UWAirplane/UWDiskInfo），
; 补入 6 条磁盘有而旧清单未引的新条目（base GCU 的 GCUUtil.exe / NVControlSetting.dll /
; AppSettings\GamingMonitor.json；50 覆盖层的 GCUBridge.exe / UEFI_Firmware.dll / OSDTpDetect.exe）。
; ⚠️ payload\** 的内容本轮一个字节都没改（只读），只对齐清单。
; ============================================================================

; ============================================================================
; ⭐⭐ 2026-10-14（owner 纠偏：载荷落地位置 + 临时化）—— 本段是 [Files] 里 GCU 部分的口径来源。
;
; owner 原话（准绳）：
;   ①「修改安装逻辑解压出来的 GCU 应该放在 安装目录的这个文件夹
;      C:\Program Files\L-Mechrevo\GCU\AiStoneService\ 而不是 shard，
;      而 payload 应该作为临时文件而不是安装到安装目录里面」
;   ②「ps1 留 卸载用到的 7z 不留 没用到的 GCU 也不留」
;   ③「logo 放在 GCU 文件夹」
;
; ⇒ 装完之后 {app}\GCU\ 下【只应有】：
;     · AiStoneService\            解压出来的那一份完整 GCU 本体（~90 MB 量级，
;                                  GCUService.exe / GCUBridge.exe / ACPIDriverDll.dll /
;                                  MyControlCenter\... 全在里面）
;     · logo\Language\             zh-cn.json / en-us.json（与 AiStoneService 同级 ——
;                                  旧结构 payload-old\GCU\ 下就是 AiStoneService\ / logo\ /
;                                  UWACPIDriver\ 三个同级目录，照旧结构对齐）
;     · README.txt · NOTICE.txt    厂商自带说明（同样照旧结构：旧 base 层 GCU\ 的根下）
;     · Install-Gcu.ps1 / Select-GcuPayload.ps1 / Uninstall-Gcu.ps1（卸载与判定要用）
;   ⛔ 不留 payload\（5 个 7z 是安装源，解压完就清）
;   ⛔ 不留 7za.exe（只在解压时用，用完清）
;   ⛔ 不留 shared\ 这个层级本身（内容按目的地铺开）
;   ⛔ 不留没用到的变体（只解压选中的那一个）
;   ⛔ 不留厂商的 install.bat / uninstall.bat / 清理官方控制台.bat（内容重复，owner 已确认删）
;   ✅ 保留 {app}\UWACPIDriver\（应用根目录，见上）
; ----------------------------------------------------------------------------
; 临时化怎么做（5 个 7z + 7za.exe）：
;   ⭐ 全部 6 条改用 `Flags: dontcopy` —— 它们【不属于安装文件集】：Inno 不复制、不登记进
;      unins 记录，用户装完在 {app} 里自然找不到它们。
;   ⭐ [Code] 的 PrepareToInstall 在【任何文件落盘之前】用 ExtractTemporaryFile() 把它们解到
;      {tmp}，[Run] 再把那个 {tmp} 目录作为 -StagingRoot 传给 Install-Gcu.ps1。
;   ⭐ Install-Gcu.ps1 解压完成后自己删掉 {tmp} 里的 7z 与 7za.exe（Remove-GcuStagingTemporaries），
;      所以连临时目录里也不长期留。
;   ⚠️ 为什么用 dontcopy + ExtractTemporaryFile，而不是「照旧装到 {app}\GCU 再在收尾删掉」：
;      · 后者在「解压失败 / 用户中途取消 / 收尾前崩溃」时会把 5 个 7z 永久留在安装目录里 ——
;        正是 owner 这次要求消除的形态；dontcopy 从根上不存在这个中间态（{app} 里从未出现过）；
;      · dontcopy 的 6 个文件也不进 unins 记录，卸载清单干净；
;      · 磁盘上不多占：{tmp} 与 {app} 在同一个卷时 Inno 直接搬运，且解压完即删。
;   ⚠️ 代价（如实记录）：文件只在 setup 进程存活期间可用 ⇒ Install-Gcu.ps1 不能再被单独手工重跑
;      （它现在需要 -StagingRoot 指向一个含 payload\*.7z + 7za.exe 的目录，见其 .EXAMPLE）。
;      这是刻意的取舍：校验（Test-VendorPayloadSource）与落地都在同一个 setup 运行里完成。
; ============================================================================
#ifndef NOGCU
#ifndef NOGCU_PAYLOAD
Source: "files\GCU\payload\1020.7z"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
#ifndef NOGCU_PAYLOAD
Source: "files\GCU\payload\30.7z"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
#ifndef NOGCU_PAYLOAD
; 2026-10-21（机主要求）：暂时不随包提供 40 载荷 —— 40 系改用 50 载荷。
; 2026-10-27：GcuVariant 改为记录**实际载荷版本**（40 系装 50 载荷 ⇒ 写 50），不再是机器代际 40。
; Source: "files\GCU\payload\40.7z"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
#ifndef NOGCU_PAYLOAD
Source: "files\GCU\payload\50.7z"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
#ifndef NOGCU_PAYLOAD
Source: "files\GCU\payload\WJ.7z"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
Source: "files\GCU\7za.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: ShouldInstallGcu
Source: "files\GCU\7za.exe"; DestDir: "{tmp}"; Flags: dontcopy; Check: ShouldInstallGcu
#endif
; --- shared\ 按【目的地】铺开（⛔ 不再整目录落 {app}\GCU\shared\；铺完 shared\ 这个层级就没有了）----
;   shared\UWACPIDriver\ 3 件 -> {app}\UWACPIDriver\（见上面那 3 条 Source）
;   shared\README.txt / NOTICE.txt -> {app}\GCU\（见本段末尾 2 条 Source）
;   logo\Language\ 2 件 -> {app}\GCU\logo\Language\（源在 files\GCU\logo\，不是 shared\logo\）
;   3 个厂商 .bat -> ⛔ 已删除（内容与我们的 ps1 重复，owner 拍板「如果bat确实内容重复就不要」）
; ⭐ 2026-10-14（owner「logo 放在 GCU 文件夹」+ 旧结构铁证）：logo 与 AiStoneService 同级。
;   旧结构（payload-old\GCU\）里就是 AiStoneService\ / logo\ / UWACPIDriver\ 三个同级目录，
;   旧的 [Files] 也把这两份 .json 铺在 base 层的 logo\Language\ 下 —— 本段照旧结构对齐，
;   落点定为 {app}\GCU\logo\Language\（与 {app}\GCU\AiStoneService\ 同级）。
;   ⚠️ 源目录已由 owner 从 shared\logo\ 移到 files\GCU\logo\（与 AiStoneService 同级的位置），
;      shared\logo\ 已不存在 —— 引用新路径，⛔ 不要再写 shared\logo\。
#ifndef NOGCU
Source: "files\GCU\logo\Language\en-us.json"; DestDir: "{app}\GCU\logo\Language"; Flags: ignoreversion; Check: ShouldInstallGcu
Source: "files\GCU\logo\Language\zh-cn.json"; DestDir: "{app}\GCU\logo\Language"; Flags: ignoreversion; Check: ShouldInstallGcu
#endif
; ⭐ 厂商自带的两个说明文件（README.txt / NOTICE.txt）—— 落 {app}\GCU\（与 AiStoneService\ 同级）。
;   ⚠️ 2026-10-14（旧结构铁证，owner 已确认「按你建议来」）：旧结构 base 层 payload-old\GCU\ 的
;      【根下】就是这 5 个文件（install.bat / uninstall.bat / 清理官方控制台.bat / README.txt /
;      NOTICE.txt），而 payload-old\GCU\AiStoneService\ 里只有厂商自带的同名但【不同】的内部脚本
;      （install.bat 535 B / uninstall.bat 459 B / uninstallReg.bat 366 B，与根目录那三个不是同一份）。
;      => 这两个 .txt 的正确落点是 GCU 根，即 {app}\GCU\（本版 = 与 AiStoneService\ / logo\ 同级）。
;   ⭐ 2026-10-14（owner 拍板「按你建议来 如果bat确实内容重复就不要」）：上面那 3 个 .bat 已核实
;      内容重复，源文件已从 files\GCU\shared\ 删除，对应的 3 条 Source 也已从本段移除：
;        · install.bat           —— robocopy 到硬编码 %ProgramFiles%\L-Mechrevo\GCU + 签名校验 +
;                                   pnputil + sc create；Install-Gcu.ps1 全都有（Assert-SignedFile /
;                                   Install-UwacpiDriver / Install-Service / Ensure-FirewallRule），
;                                   且它假设 UWACPIDriver\ 在 GCU 目录下，与我们 {app}\UWACPIDriver\ 冲突；
;        · uninstall.bat         —— sc stop/delete GCUBridge + 删防火墙规则；Uninstall-Gcu.ps1 更强
;                                   （杀进程 / sc delete / -PurgeVendor / 驱动库条目）；
;        · 清理官方控制台.bat    —— taskkill 5 个进程 + 删若干 OEM 目录；Install-Gcu.ps1 的
;                                   Remove-VendorConsole 覆盖同样名单且更全（还管 UWP/ARP/注册表）。
;   ⚠️ 三个 .bat 原本也从不被我们的流程调用（Install-Gcu.ps1 注释：nothing calls them automatically）。
;   ⚠️ 它们删的 "logo" 是官方控制台目录（C:\Program Files\OEM\*）里的，⛔ 不是 {app}\GCU\logo\，无冲突。
#ifndef NOGCU
Source: "files\GCU\shared\README.txt"; DestDir: "{app}\GCU"; Flags: ignoreversion; Check: ShouldInstallGcu
Source: "files\GCU\shared\NOTICE.txt"; DestDir: "{app}\GCU"; Flags: ignoreversion; Check: ShouldInstallGcu
#endif

; 2026-10-08：删除原 [Dirs] 段的三条空目录条目（{app}\ru\ / {app}\sv\ / {app}\zh-Hant\）。
; 原因：本包只带 zh-CN 资源，[Files] 从不向这三个目录写任何文件，而 [Dirs] 会无条件创建它们，
; 于是每次安装都在安装目录里留下 3 个空文件夹（同 install_script_30gcu.iss 的修复）。

[InstallDelete]
; 2026-10-13（目录整理）：老安装（<= 0.291.7.2）把许可与随包文档散在 {app} 根目录。
;   升级安装时按【旧的、写死的根路径】清掉旧副本，否则根目录会一直留着那批过期文件，
;   "不乱"这个目标就落空了（→ 选择"清理"，不是"接受"）。
;   ⚠️ 只删这些精确的 {app}\ 下【文件】，⛔ 不删任何目录、⛔ 绝不碰 {app} 之外的路径；
;   {app}\LICENSES 与 {app}\Docs 由 [Files] 覆盖写入，本段一概不动它们。
Type: files; Name: "{app}\LICENSE"
Type: files; Name: "{app}\LICENSE.txt"
Type: files; Name: "{app}\THIRD_PARTY_NOTICES.txt"
Type: files; Name: "{app}\THIRD-PARTY-NOTICES.md"
Type: files; Name: "{app}\PawnIO\*.bin"
Type: files; Name: "{app}\COPYING.PawnIO.txt"
Type: files; Name: "{app}\更新日志.txt"
Type: files; Name: "{app}\用前必看.txt"
; 2026-10-13（许可去重，owner 原话「合并」）：[Files] 里原本有两条把 GPL-3.0 正文写成 {app}\LICENSES\LICENSE.txt
;   （原 :43 / :63；源文件 files\LICENSE,1.txt 与 ,2.txt 逐字节相同，也与 ..\LICENSES\GPL-3.0.txt 相同，
;   即同一份 GPL 正文在安装目录里出现了两次）—— 已删除那两条，引用一律改指 {app}\LICENSES\LICENSE（横幅 + GPL 全文）。
;   由本版之前的 0.291.7.3 中间布局装出的机器，{app}\LICENSES\ 里会残留一份 LICENSE.txt；
;   [Files] 已不再写它 ⇒ 在此按【精确文件名】清掉，否则重复副本会永久留下（仍⛔不删任何目录、⛔不碰 {app} 之外）。
;   ⚠️ 上方 2026-10-13 那条「{app}\LICENSES 与 {app}\Docs 由 [Files] 覆盖写入，本段一概不动它们」
;     描述的是当时的布局，以此条为准（历史注释不回改）。
Type: files; Name: "{app}\LICENSES\LICENSE.txt"

[Run]
; 2026-10-27：不再在全新安装时静默删除 %APPDATA%\MechrevoLite。
;   原因：卸载项缺失/损坏时 GFreshInstall 会误判为全新安装，直接抹掉用户设置；
;   设置是否清理只由卸载器交互询问决定，安装器不再替用户做删除决定。
; 2026-10-09（owner 拍板：改成可选）：安装期「是否移除官方控制台」由 InitializeSetup 的询问决定，
;   答案经 {code:GetVendorConsoleParam} 透传给 Install-Gcu.ps1：-RemoveVendorConsole / -KeepVendorConsole。
;   ⭐⭐ 2026-10-15（owner 新口径「并提示是否保留原版控制台」）：**询问改为"是否【保留】"，默认按钮 = 保留**。
;      原因：同日真机事故 —— 移除判定曾按名字匹配，把同厂的「机械革命电子保修卡」也卸了。
;      现在卸载判定已收敛为**纯 ID**（只认官方控制台的 AppId {6ea3ce12-b991-4b65-9f8d-b148eaaecd87}），
;      且默认不动任何厂商物件；只有用户显式选「否 / 卸载」才会执行 [2/8]（N7）。
;   ⚠️ 回答「是」-> 传 -KeepVendorConsole -> 脚本的 [2/8]（N7）一个厂商物件都不碰。
;   ⚠️ 静默安装（/SILENT、/VERYSILENT）：⛔ 不弹窗（会挂住），取默认 = **保留**；
;      要卸载就显式加 -RemoveVendorConsole（安装界面上选「否」等价）。
;   ⚠️ 本行只加了 vendconsole 透传，-Variant {code:GetGcuVariantParam} 等一律未动。
;   ⭐ 2026-10-14（owner 纠偏）：-StagingRoot 由 "{app}\GCU" 改成 "{tmp}" —— 5 个 7z 与 7za.exe
;     现在是 dontcopy 的安装期临时文件，由 PrepareToInstall 的 ExtractGcuStagingTemporaries
;     解进 {tmp}（见该函数）。Install-Gcu.ps1 从这里取载荷、解压、然后把这两样删掉，
;     所以 {app}\GCU 下永远不会出现 payload\ 或 7za.exe。
;     ⚠️ -TargetDir 仍是 "{app}\GCU"，解压产物落 {app}\GCU\AiStoneService。
#ifndef NOGCU
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Check: ShouldRunGcuInstall; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\GCU\Install-Gcu.ps1"" -StagingRoot ""{tmp}"" -TargetDir ""{app}\GCU"" -Variant {code:GetGcuVariantParam} -InstallerVersion ""18.3"" -LogDir ""{commonappdata}\L-Mechrevo\logs"" -AppExe ""{app}\L-Mechrevo.exe"" {code:GetVendorConsoleParam}"; StatusMsg: "{cm:GcuStatus}"
#endif
; ⚠️ 2026-10-13（本轮回退 Check）：⛔ 这里【不再】用 Check: IsNet10RuntimeReady 门控。
;    理由：门控 = 缺运行时时连壳都不启动 = 用户装完什么都看不到（用户原话：「不静默安装用户跳过了.net
;    也不停止，只需要在 LM 启动的时候如果没有 .net10 再提示就行」）。
;    r25：直接启动程序本体（.NET 10 的 apphost）。缺运行时这件事现在由【安装阶段自动下载+静默安装】
;    解决（见 [Code] 的 NetDlTryInstallRuntime；失败才弹选择框）；万一装完仍缺运行时，apphost 会弹
;    微软自带那句英文提示 —— ⛔ 启动壳已在 r25 移除，不再有「壳的中文提示」这一层。
;    ⚠️ 静默安装（/SILENT、/VERYSILENT）本来就走不到这里：skipifsilent 已挡住。
; ** 2026-10-17：按「可选任务 pawnio」安装 PawnIO 驱动（读 CPU 温度所需）
;   只在该任务被勾选时执行；失败**不阻断安装**（命令末尾 exit 0 兜掉返回码）——
;     驱动装不上最坏只是 CPU 温度显示「—」，不该让整个安装失败
;   runhidden：脚本会下载 + 静默装，不需要黑窗；waituntilterminated 保证装完再启动 LM。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& '{app}\Install-PawnIO.ps1'; exit 0"""; StatusMsg: "正在安装 PawnIO 驱动（读取 CPU 温度所需）…"; Flags: runhidden nowait; Tasks: pawnio; Check: ShouldInstallPawnIo
; 2026-10-18（机主：安装完自动在后台拉起 LM，不显示主界面）：
;   ⭐ 传 "startup" 参数 ⇒ 程序按「开机自启」路径启动 ⇒ 只驻留托盘、不显示主窗口（IsStartupLaunch）。
;   ⛔ 不再是 finish 页的勾选项（postinstall 已去掉）⇒ 静默/普通安装都会自动后台启动。
; 2026-10-22：无界机型安装 inpoutx64 驱动（风扇 I2EC 写入需要）；非无界机器脚本自行跳过，失败不阻断安装。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-InpOut.ps1""; exit 0"; StatusMsg: "正在配置无界风扇驱动（inpoutx64）…"; Flags: runhidden waituntilterminated
Filename: "{app}\L-Mechrevo.exe"; Parameters: "startup"; StatusMsg: "正在后台启动 L-Mechrevo（托盘）…"; Flags: nowait

[UninstallRun]
; --- 历史（2026-10-09 上一轮，已被下面现行策略取代，保留备查）---
; 2026-10-09：本段从"无条件执行"改成"由 Check 门控" —— 只有用户在卸载询问里明确选「是，移除」
; 才跑 Uninstall-Gcu.ps1（厂商/共享那部分：GCUBridge 服务、驱动库里的 UWACPIDriver）。
; ⚠️ 为什么用 Check 而不是把整段删掉/改写：Inno 的 [UninstallRun] 支持 Check（本机 Inno Setup 6.7.1
;   实测编译通过），门控函数 ShouldRemoveVendorComponents 的值在 InitializeUninstall 里就已定好，
;   而 InitializeUninstall 一定早于 [UninstallRun]，所以不存在"先跑后问"。
; ⚠️ 静默卸载（/SILENT、/VERYSILENT）不弹窗，取明确默认 = 保留官方控制台/厂商组件（False）。
; ⚠️ L-Mechrevo 自己的那几样（HKLM\SOFTWARE\L-Mechrevo 标记键、LMechrevo* 自启任务、我方防火墙
;   规则）不看这个答案，在 [Code] CleanupLmOwnUninstallArtifacts 里无条件清理。
; --- 现行（2026-10-14，owner 原话：「如果是卸载的话,那就都删，如果升级的话那保留自定义性能设置和曲线」）---
; 上一轮"卸载期只管卸载LM自己 不管官方"**整体废除**，改成按【路径】二选一（⛔ 不靠猜、⛔ 不靠注册表存在性）：
;   ⭐ 真·卸载（本段 [UninstallRun]）：加 -PurgeVendor —— 除 LM 自有内容外，**额外**：
;      · stop + `sc delete` GCUBridge 服务（Uninstall-Gcu.ps1 Remove-GcuService；
;        ⛔ 驱动库那条 UWACPIDriver 仍然不碰：系统级共享 + 要重启，见该脚本内注释）；
;      · 删整棵厂商控制台键树 HKLM\SOFTWARE\OEM\GamingCenter2（Remove-VendorConsoleKey；
;        含用户自己的 MyFan3\ 自定义风扇曲线 / RGBKeyboard\SingleZone\ 灯效 —— 用户已明确接受）。
;   ⭐ 升级/覆盖安装：**根本不走本段**。[UninstallRun] 只存在于卸载器（unins000.exe）里，
;      而升级是"新的 setup.exe 去调旧版的 unins000.exe"——旧卸载器跑的是**旧版编译进去的**本段，
;      新版这次改的东西不会在升级时生效；新版自己的安装路径（[Run] Install-Gcu.ps1）⛔ 从不带
;      -PurgeVendor。这就是"只在卸载时全删"的机制保证，见下面 Check: IsRealUninstall 那段说明。
;   ⚠️ 保留 -LmOwnedOnly 语义与开关：Uninstall-Gcu.ps1 里它仍是 fail-safe（两个开关同时给 = 它赢），
;      便于以后需要时回退成"只清 LM 自有"。
; 历史（2026-10-09，已被上一行取代，保留备查）：本段无条件执行且固定 -LmOwnedOnly，让
;   Uninstall-Gcu.ps1 在卸载期只清 LM 自有内容：
;   ✅ 曾跑：LMechrevo* 自启任务、HKLM\SOFTWARE\L-Mechrevo 标记键、我方防火墙规则
;      "L-Mechrevo - Block remote GCU MQTT"（以及 [Code] 里对两处 MechrevoLite 用户数据的询问）。
;   ⛔ 曾不跑：GCUBridge 服务（sc delete）、GCUService/GCUBridge 进程（Stop-Process）、驱动库里的
;      UWACPIDriver（pnputil /delete-driver）、官方控制台目录/UWP/ARP 项。
;   依据（按安装期证据）：payload\GCU\install.bat 自己就 `sc.exe create GCUBridge` 并装到
;      %ProgramFiles%\OEM\<机型>\AiStoneService；payload\GCU 里厂商自带的"清理官方控制台"脚本
;      明写 "Keep: AiStoneService / UWACPIDriver / GCUBridge service / UWACPI driver"；
;      Install-Gcu.ps1 的 Install-Service / Install-UwacpiDriver 只是"用厂商件、按厂商名字再注册/再入驱动库"，
;      官方控制台同样用这个服务名（端口 13688）与这份系统级 ACPI 驱动 —— 删了会伤到不属于我们的软件。
;      ⭐ 用户现在已明确接受"真卸载就都删"，故上面那套保留改由 -PurgeVendor 覆盖。
; ⭐ 2026-10-15（残留修复）：本行加 -NoLogDir —— 卸载脚本**不再**新建 {commonappdata}\L-Mechrevo\logs。
;   理由：卸载的最后一步（[Code] PurgeLmCommonAppData / usPostUninstall）会整棵删掉
;   {commonappdata}\L-Mechrevo；如果脚本还先 New-Item 建目录写日志，就等于"删完又被重建"。
;   加了这个开关后：目录不存在时脚本只跳过 -LogDir 那份日志（Write-Log 对 $null 已短路，
;   见 Uninstall-Gcu.ps1 L111），清理步骤一步不少；目录存在时行为不变、照常写。
;   ⚠️ 该开关只影响"写日志"，⛔ 与 -PurgeVendor 的厂商清理范围无关。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\GCU\Uninstall-Gcu.ps1"" -TargetDir ""{app}\GCU"" -LogDir ""{commonappdata}\L-Mechrevo\logs"" -PurgeVendor -NoLogDir"; RunOnceId: "GcuUninstall"; Check: ShouldInstallGcu

[Icons]
; 2026-10-13（r25）：快捷方式指回程序本体 —— ⛔ 启动壳已移除；缺 .NET 10 运行时由【安装阶段
;   自动下载 + 静默安装】解决（失败才弹选择框）。
Name: "{group}\L-Mechrevo"; Filename: "{app}\L-Mechrevo.exe"; WorkingDir: "{app}"; 
Name: "{group}\使用前必看"; Filename: "{app}\Docs\用前必看.txt"; 
Name: "{group}\更新日志"; Filename: "{app}\Docs\更新日志.txt"; 
Name: "{group}\开源许可 (GPL-3.0)"; Filename: "{app}\LICENSES\LICENSE"; 
Name: "{group}\{cm:UninstallProgram,L-Mechrevo}"; Filename: "{uninstallexe}"; 
Name: "{autodesktop}\L-Mechrevo"; Filename: "{app}\L-Mechrevo.exe"; WorkingDir: "{app}"; Tasks: desktopicon; 

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";
; ** 2026-10-17（真机反馈「30 系 CPU 温度不识别」）：CPU 温度靠 LM 自带的 LHM，而 LHM 读 CPU 温度
;   需要 PawnIO 驱动（读 MSR）⇒ 没装驱动时 UI 只能显示「—」
;   合规：PawnIO 驱动本体是 GPL-2.0（https://github.com/namazso/PawnIO）⇒ 我们**不分发**它，
;     只在**用户勾选**本任务时，由 {app}\Install-PawnIO.ps1 从上游官方 release **现下载**，
;     校验 SHA-256 ＋ Authenticode 签名通过后才静默安装（脚本自带这两道校验）。
;   默认勾选（checkedonce = 升级时记住上次选择）；静默安装（/SILENT）时自动跳过。
Name: "pawnio"; Description: "安装 PawnIO 驱动（读取 CPU 温度所需，需联网下载约 1-2 MB）"; GroupDescription: "推荐组件";
; 2026-10-18（机主：强冷点不了，诊断包显示本机 PawnIO 未安装）：
;   ⚠️ `checkedonce` 会记住"用户上次取消过"⇒ 之后再装也永远不勾 ⇒ 驱动一直缺失 ⇒ EC 写入（风扇强冷/曲线/电池阈值）全失败。
;   ⇒ 去掉 checkedonce：默认总是勾选；已安装时由 CurPageChanged 自动取消勾选并改写说明（不重复安装）✅ 

[UninstallDelete]
; 2026-10-09：%APPDATA%\MechrevoLite 与 C:\ProgramData\MechrevoLite 故意不写在这里 ——
; 它们要支持"交互卸载时用户选择保留"，只能在 [Code] 里处理
; （见 HandleUninstallUserData / usUninstall）。本段只留路径无关的静态删除项。
; ⭐ 2026-10-09（卸载期只管 LM 自有）：下面 {app}\UWACPIDriver 删的是**我们自己暂存的驱动副本**
;   （Install-Gcu.ps1 把厂商 payload 复制到应用根目录 \UWACPIDriver），属 LM 自有文件，保留删除；
;   驱动库里那份（uwacpidriver.inf / oemN.inf，pnputil 入的）在 -LmOwnedOnly 模式下明确不碰（见 [UninstallRun]）。
;   ⭐ 2026-10-13 第二轮：路径由 {app}\GCU\UWACPIDriver 改到 {app}\UWACPIDriver；
;   旧的 GCU 位置保留一条（升级/回退安装的残留目录，属 LM 自有，一并清）。
; ⭐ 2026-10-14（owner 纠偏：载荷落地改 {app}\GCU\AiStoneService，payload/7za/shared 不再落盘）：
;   上一版（22:05 那个包）把 5 个 7z + 7za.exe + shared\ 都装进 {app}\GCU\，解压产物还规整到
;   {app}\GCU\payload\<变体>\<ServiceDir>（以及 1020/30 的 UniwillService 别名目录）。本版：
;     · 7z / 7za.exe 改 dontcopy + 解到 {tmp}，装完不留 ⇒ 下面那条 {app}\GCU\payload 只对
;       【上一版遗留的旧目录】有意义，保留它就是为了让升级安装能把那份残留清掉（⛔ 不是新装的产物）；
;     · 解压产物直接落 {app}\GCU\AiStoneService（固定名，1020/30 的 UniwillService 也统一成它），
;       所以 {app}\GCU\UniwillService 同样只针对旧包残留；
;     · {app}\GCU\shared 与 {app}\GCU\7za.exe 同样只针对旧包残留，显式补两条。
;   ⚠️ 本段只在【真卸载】时生效 —— [UninstallDelete] 是卸载器独有的段，安装/升级路径完全不读它。
; ⭐ 2026-10-15：{commonappdata}\L-Mechrevo\logs 这一条**不再是**父目录的兜底 —— 父目录（含
;   gcu-install-status.json 与 logs\）改由 [Code] PurgeLmCommonAppData() 在 usPostUninstall 里整棵删除
;   （原因见该函数上方注释：卸载器自己在写这个目录，静态段有"先删后写/被占用"的时序问题）。
;   这条留着只为覆盖一种情形：本次改动之前装下的旧版卸载器不会跑新 [Code]，但它跑的是旧版
;   编译进去的静态清单 —— 对本版之后的新卸载器而言此条已冗余（父目录整棵删掉时它必然一并消失）。
Type: filesandordirs; Name: "{commonappdata}\L-Mechrevo\logs"; 
Type: filesandordirs; Name: "{app}\GCU\AiStoneService"; 
Type: filesandordirs; Name: "{app}\GCU\UniwillService"; 
Type: filesandordirs; Name: "{app}\UWACPIDriver"; 
Type: filesandordirs; Name: "{app}\GCU\UWACPIDriver"; 
Type: filesandordirs; Name: "{app}\GCU\logo"; 
; ⭐ 2026-10-14（真卸载全删 / owner 原话「如果是卸载的话,那就都删」）：旧版安装器铺下的载荷模板。
;   {app}\GCU\payload 从本版起【不再是安装产物】（7z 走 {tmp}，见 [Files]），这里留着只为清
;   「上一版装出来的残留」（5 个 7z + 安装期解压出的变体目录）。万一将来要回退布局也能兜住。
Type: filesandordirs; Name: "{app}\GCU\payload"; 
; ⭐ 2026-10-14：同上一版残留 —— {app}\GCU\shared（shared\ 整目录铺开的旧形态）与
;   {app}\GCU\7za.exe（旧版把解压工具装进安装目录）。本版这两样都不再落盘，留条清残留。
Type: filesandordirs; Name: "{app}\GCU\shared"; 
Type: files; Name: "{app}\GCU\7za.exe";
; ⭐ 2026-10-15（手工兜底产物）：自动解压失败时，Install-Gcu.ps1 会把该变体的 7z、
;   `安装GCU.cmd`、`手动安装说明.txt` 放到 {app}\GCU\，引导用户手动解压后再跑那个脚本。
;   这三样都是本次安装放下的，卸载时一并清掉（逐个列名，不依赖通配符语义）。
Type: files; Name: "{app}\GCU\1020.7z";
Type: files; Name: "{app}\GCU\30.7z";
Type: files; Name: "{app}\GCU\40.7z";
Type: files; Name: "{app}\GCU\50.7z";
Type: files; Name: "{app}\GCU\WJ.7z";
Type: files; Name: "{app}\GCU\安装GCU.cmd";
Type: files; Name: "{app}\GCU\手动安装说明.txt";
; ⭐ 2026-10-15：手工兜底那次运行会把日志写到 {app}\GCU\logs（`安装GCU.cmd` 传的 -LogDir），卸载时一并清掉。
Type: filesandordirs; Name: "{app}\GCU\logs"; 
; 2026-10-13（卸载残留修复 / owner 原话「卸载的时候L-Mechrevo文件夹也就是安装文件夹不会被删去貌似？」）：
;   ⭐ 真因：{app}\install.log 是【安装最后一步】ssPostInstall 里才 CopyFile 出来的
;      （见本文件 CurStepChanged/ssPostInstall 的 if CopyFile(ExpandConstant('{log}'), ExpandConstant('{app}\install.log'), False)）。
;      那一刻 Inno 早已把 [Files] 清单登记进 unins 记录 —— 这份"事后新增"的文件不在卸载器的删除清单里，
;      于是卸载器删得掉清单内的一切，却删不掉这个孤儿 ⇒ {app} 因"非空"保留
;      ⇒ 上面那条 filesandordirs {app} 无从生效（本机同目录实测确有 135,511 B 的 install.log）。
;      ⭐ 这正是"卸载后 L-Mechrevo 安装文件夹仍在"的由来（⛔ 不是 [UninstallDelete] 缺 {app}，那条早就在）。
;   ⭐ 修法＝精确补这一条，让兜底那条 filesandordirs {app} 能真正把目录删干净；
;   ⛔ 只删这一个精确文件名，⛔ 不删任何目录、⛔ 不碰 {app} 之外；
;   ⚠️ 不担心丢证据：这是安装日志的副本（Inno 原始 {log} 在 %TEMP% 另有一份），
;      且 %ProgramData%\L-Mechrevo\logs 下的 GCU 安装/卸载日志不受影响。
;   ⚠️ 升级不受影响：升级走"旧卸载器 + 新安装"，[UninstallDelete] 不参与，且新版 ssPostInstall 会重新覆盖写它。
Type: files; Name: "{app}\install.log";


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
chinesesimplified.RuntimeReadyMemo=附加任务：%1 —— 安装程序将自动下载并静默安装（%2）。{break}若自动下载 / 安装失败，安装过程中会再问你是否改用浏览器下载；无论装没装上，安装都【不会】中止。
chinesesimplified.RuntimeRequired=本程序需要 .NET 10 桌面运行时（x64），当前未安装，安装程序已经尝试自动下载并静默安装，但没有成功。{break}{break}需要安装：%1{break}需要版本：%2{break}官方直链（短，不折行）：{break}%3{break}{break}· 这条直链已自动复制到剪贴板，可直接粘贴到浏览器，打开即开始下载；{break}· 「重试下载 / Retry」= 再自动下载并静默安装一次（网络抖动时先试这个）；{break}· 「用浏览器下载 / Open in browser」= 现在用浏览器打开上面的直链，【直接下载】运行时安装包（装好后回到本向导，安装自动继续）；{break}· 「稍后 / Later」= 不重试、不打开，直接继续安装。{break}{break}⚠️ 本提示框的文字没法用鼠标框选，地址请用剪贴板取（弹框前已写好）。{break}无论选哪个，安装都【不会】中止。
english.RuntimeReadyMemo=Additional task: %1 - Setup will download and silently install it for you (%2).{break}If that does not work, Setup will ask whether to download it in your browser instead; either way setup will NOT be aborted.
english.RuntimeRequired=This app needs the .NET 10 Desktop Runtime (x64), which is not installed; Setup already tried to download and silently install it, but that did not work.{break}{break}Install: %1{break}Version: %2{break}Official direct link (short, single line):{break}%3{break}{break}- This link has ALREADY been copied to your clipboard: paste it into your browser and the download starts;{break}- Retry = download and silently install it again (try this first if the network hiccuped);{break}- Open in browser = open the link above now and download the runtime installer right away (then come back; setup continues automatically);{break}- Later = do not retry and do not open it; continue the installation right away.{break}{break}NOTE: this dialog text cannot be selected with the mouse, so use the clipboard (already filled before this dialog).{break}Either way, setup will NOT be aborted.
chinesesimplified.RuntimeSelfCheckFailed=.NET 桌面运行时 10（x64）检查未通过，为免出现英文报错，本次【不会】启动 L-Mechrevo。{break}{break}请手动打开下面的地址，安装 x64 的 .NET 桌面运行时 10（Windows Desktop Runtime 10），装好后从开始菜单启动 L-Mechrevo：{break}{break}%1
english.RuntimeSelfCheckFailed=The .NET Desktop Runtime 10 (x64) check did not pass, so L-Mechrevo will NOT be launched (to avoid an English apphost error dialog).{break}{break}Open the address below, install the .NET Desktop Runtime 10 (x64), then start L-Mechrevo from the Start menu:{break}{break}%1
chinesesimplified.OfficialConsolePrompt=检测到已安装官方控制中心。是否【保留】它？{break}{break}「是」（默认按钮）：保留官方控制台，安装照常继续（两者可能功能冲突，可装完后再决定）；L-Mechrevo 不会动它的任何文件。{break}{break}「否」：卸载官方控制台，并由 L-Mechrevo 接管硬件控制。只按已知的程序 ID 精确卸载官方控制台本身，不会碰保修卡 / 服务 / 驱动等同厂其它软件；此操作不可逆，要恢复需重装官方控制台。
english.OfficialConsolePrompt=An official Control Center was detected. Keep it?{break}{break}Yes (the default button): keeps the official console untouched and setup continues (the two may conflict; you can decide again after setup). L-Mechrevo does not touch any of its files.{break}{break}No: uninstalls the official console and lets L-Mechrevo take over hardware control. It is matched strictly by its known product IDs, so the warranty card / services / drivers and other vendor apps are never touched. This is irreversible; reinstalling the official console is required to get it back.
chinesesimplified.OfficialConsoleUninstallFailed=无法卸载官方控制中心，安装已中止。请先手动卸载后再试。
chinesesimplified.GcuPreUninstallFailed=覆盖安装前卸载旧版 GCU 失败，安装已中止。请手动停止 GCU 服务后重试。
english.OfficialConsoleUninstallFailed=Could not uninstall the official Control Center. Setup aborted. Uninstall it manually and try again.
english.GcuPreUninstallFailed=Failed to uninstall the prior GCU before overwriting. Setup aborted. Stop the GCU service manually and retry.

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
  // 2026-10-14（7z 载荷化）：base 暂存树已不存在（payload 树整体改成 5 个 7z，安装期由
  //   Install-Gcu.ps1 按选中变体解压）。UserFanTables 等机型风扇表在各变体自己的目录里，
  //   ⭐ 而 2026-10-14 纠偏后【所有变体都落在同一个 {app}\GCU\AiStoneService】，
  //   故 GcuUserRoot 就是运行目录（= GcuLiveUserRoot）。
  //   升级备份时运行目录优先、这里兜底（见 BackupAndUpgrade）。
  GcuUserRoot = '{app}\GCU\AiStoneService\MyControlCenter';
  // 2026-10-06（三处修复 B）：运行中的 GCU 服务真正读写的用户数据目录 —— 自定义模式档 UserPofiles 实际在这里被改。
  GcuLiveUserRoot = '{app}\GCU\AiStoneService\MyControlCenter';
  GcuVariantRegKey = 'SOFTWARE\L-Mechrevo';
  GcuVariantRegValue = 'GcuVariant';
  // .NET 10 桌面运行时（x64）—— 提示里【显示 / 复制】的、点「下载」时用浏览器打开的，都是下面这条
  //   【官方直链】（⛔ 不是「下载页」，⛔ 不是我们下载文件 —— 只是把直链交给默认浏览器）。
  //   ⚠️ 2026-10-13（本轮第二轮）：用户原话「不是说点下载直接跳转 .net10 运行时的下载吗」
  //     + 第三轮「https://dotnet.microsoft.com/download/dotnet/10.0/runtime 这个是运行时的直选页
  //        可以替换掉原先模糊的 .net 总页」
  //     => 不再打开「列一堆运行时的落地页」，直接打开【核实过的官方直链】。
  //   ✅ 只读核实（2026-10-13 本机，只读 GET/HEAD，⛔ 未打开浏览器、⛔ 未下载任何正文）：
  //      · 本常量（aka.ms 短链）HEAD => 200 + Content-Type: application/octet-stream
  //        + Content-Length: 60,032,984；连测 2 次一致；302 终址见下一行。
  //      · 终址 = https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe
  //        （同一条终址单独 HEAD 也是 200 + application/octet-stream + 60,032,984，连测 2 次一致）
  //      · ⚠️ 用户 2026-10-13 也在自己的浏览器里【实测这条 aka.ms 能正常触发下载】=> 用它当主直链。
  //      · 运行时专页 zh-cn / en 两个都只读 GET => 均 200 text/html
  //        （⚠️ 页面是 JS 渲染的，HTML 原文里【没有】静态直链 => 直链只能靠 aka.ms 或已知版本 URL 取得）。
  Net10DownloadDirectUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  // 2026-10-13（r25）：安装器【自己下载】时用的【备用直链】= 上面那条 aka.ms 短链的 302 终址
  //   （r24 只读 HEAD 实测：200 + application/octet-stream + 60,032,984 B；版本 10.0.12）。
  //   用途：aka.ms 跳转失败（短链服务抖动 / 被墙 / 被安全软件拦）时自动改用这条重试一次。
  Net10DownloadFallbackUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe';
  // 下载落到 {tmp} 时用的文件名（⚠️ 同时也是超时 taskkill 的镜像名 => 取足够独特的名字，
  //   ⛔ 不照抄 LLT 的 dotnet8.exe —— 我们这套一律 .NET 10）。
  NetDlBaseName = 'lm-net10-windowsdesktop-runtime-x64.exe';
  // 运行时安装器的静默参数（与 LLT / 微软官方口径一致；⚠️ 与版本无关）。
  NetDlInstallArgs = '/install /repair /passive /norestart';
  // 等安装器结束的【上限】（秒）：运行时安装可能较久 => 取宽裕但有限的值（10 分钟）。
  NetDlMaxWaitSeconds = 600;
  // 轮询间隔（毫秒）：每 2 秒看一次「退出码标记文件」有没有出现。
  NetDlPollMs = 2000;
  // 标记文件名：cmd 包一层，把安装器真实退出码写进 {tmp}\<这个名字>。
  NetDlDoneFileName = 'lm-net10-install-rc.txt';
  // 写剪贴板用的临时文件（⛔ 不加换行；clip.exe 读它）
  NetDlClipFileName = 'lm-net10-clip-url.txt';
  // 供日志/兜底：运行时【专页】（用户指定的那条；只读 GET 200）。⛔ 不再用作「下载」目标。
  Net10DownloadPage = 'https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0/runtime';
  // 提示里【必须写清要装哪个运行时】（用户原话：「注意提示附带下载地址(运行时版本,因为有的人不知道装
  //   哪个版本的) 可复制的那种」）：版本串取自程序自带的 L-Mechrevo.runtimeconfig.json —— frameworks[] 里
  //   "Microsoft.WindowsDesktop.App" = "10.0.0"（tfm net10.0）=> 需要 10.0.0 及以上的 10.0.x；
  //   ⚠️ 必须是【x64 的 Windows Desktop Runtime】：⛔ 不是 ASP.NET Core Runtime，⛔ 不是只有 .NET Runtime。
  Net10RuntimeName = 'Microsoft Windows Desktop Runtime 10.0 (x64)';
  Net10RuntimeVerHint = '10.0.x（本程序 runtimeconfig 要求 Microsoft.WindowsDesktop.App 10.0.0 及以上；64 位）';
  // 2026-10-13 修 .NET 前置：官方安装器写的 sharedfx 键（版本号是【值名】）。
  NetSharedFxKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  // Inno 6 没有 RegEnumValue，只能按已知候选值名探（默认值 + 官方 installer 的版本值名）。
  // 2026-10-13 RUNTIME FIX (Inno runtime error 'Could not call proc'): the candidate value-name list
  // used to be a STRING constant handed to GetArrayLength()/[] -- Pascal Script ACCEPTS that at
  // COMPILE time (so 'compiles with 0 errors' proves nothing), then throws 'Could not call proc'
  // AT RUNTIME inside InitializeSetup, which has no try/except => setup dies on launch with a bare
  // error box and installs nothing. The list now lives in NetTenInSharedFxKey as a real array.
  // 2026-10-13（r25）：启动壳常量 LmLauncherExe 已随启动壳一起移除（⛔ 包内不再有壳；
  //   程序本体 = {app}\L-Mechrevo.exe）。

var
  // 外置 GCU 包使用标记（ssPostInstall 里登记到注册表，便于诊断包识别）
  GcuExternalUsed: Boolean;
  GcuExternalNote: String;
  // 本次安装是否已准备好可用的 GCU 载荷（精简包没外置包时为 False ⇒ 跳过 GCU 安装步骤）
  GcuPayloadReady: Boolean;

// .NET Desktop Runtime 10 (x64) presence check ---- 2026-10-13 修：版本敏感（必须真的是 10.x）。
// 旧实现只看注册表键/目录「存在与否」：装过 6/8 也会判为真 -> 静默跳过 -> 装完启动时 apphost
// 弹它自己的英文「需要 .NET 10」窗口（apphost 那段文字焊在 exe 里，改不了 => 所以必须在这里判准）。
// 命中顺序：① 10.x 共享框架目录（64 位布局 => {commonpf64}，普通布局 => {commonpf}）
//          ② sharedfx 注册表键里以 "10." 开头的【子键名】
//          ③ 该键里以 "10." 开头的【值名】（官方安装器写的形如 10.0.11；Inno 6 无 RegEnumValue，
//             故按已知候选值名探）
function NetMajorOf(const Name: String): Integer;
var
  I, N, Start: Integer;
  Digits: String;
begin
  Result := -1;
  N := Length(Name);
  Start := -1;
  for I := 1 to N do
  begin
    if (Name[I] >= '0') and (Name[I] <= '9') then
    begin
      if Start < 0 then
        Start := I;
    end
    else if Start > 0 then
      Break;
  end;
  if Start <= 0 then
    Exit;

  Digits := '';
  I := Start;
  while (I <= N) and (Name[I] >= '0') and (Name[I] <= '9') do
  begin
    Digits := Digits + Name[I];
    I := I + 1;
  end;
  if Digits = '' then
    Exit;
  if Length(Digits) > 4 then
    Digits := Copy(Digits, 1, 4);
  Result := StrToIntDef(Digits, -1);
end;

// 名字里的【大版本号】是否 >= 10。
//   ⚠️ 2026-10-13（本轮第二轮）用户口径：「如果用户装了能支持运行的其他版本也不提示，这是之前就有的逻辑」。
//   程序的 rollForward：L-Mechrevo.runtimeconfig.json 没写 rollForward => .NET 缺省值 = Minor
//     => 要求 Microsoft.WindowsDesktop.App >= 10.0.0 时，10.x 全可用；【更高的大版本】也可被
//        roll-forward 到（缺省 rollForward 允许向上扩到更高主版本）。
//   => 判据取「大版本 >= 10」：10.x（含 10.0.0+）/ 11.x / 12.x … 都算「能跑」-> ⛔ 不提示；
//      ⛔ 6.x / 7.x / 8.x / 9.x 仍【不算】满足 —— net10.0 的程序只装 8.x 是跑不起来的
//      （那正是我们之前修掉的「版本盲判」）。
//   ⚠️ ⛔ 不是「任何版本都算」：装 8.x 依旧提示（用户字面口径若真是「任何版本都不提示」，
//      需要用户显式确认，不自行放宽）。
function NetIsTenOrNewerVersion(const Name: String): Boolean;
begin
  Result := NetMajorOf(Name) >= 10;
end;

// 目录下有没有【大版本 >= 10】的子目录（10.x / 11.x / …）。目录不存在 / 一个都没有 => False。
function NetTenOrNewerSubDirExists(const Base: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Base) then
    Exit;
  if not FindFirst(Base + '\*', FindRec) then
    Exit;
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if NetIsTenOrNewerVersion(FindRec.Name) then
        begin
          Result := True;
          Exit;
        end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// 只在【子键名】和【值名】里找「大版本 >= 10」，绝不因为该键存在就判真。
function NetTenOrNewerInSharedFxKey(): Boolean;
var
  Names: TArrayOfString;
  Candidates: TArrayOfString;
  S: String;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, NetSharedFxKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if NetIsTenOrNewerVersion(Names[I]) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  // 2026-10-13 RUNTIME FIX: build the candidate list as a real array here.
  // NEVER a string constant + GetArrayLength: that imported call throws 'Could not call proc'.
  SetArrayLength(Candidates, 7);
  Candidates[0] := '10.0.11';
  Candidates[1] := '10.0.10';
  Candidates[2] := '10.0.9';
  Candidates[3] := '10.0.8';
  Candidates[4] := '10.0.7';
  Candidates[5] := '10.0.6';
  Candidates[6] := '10.0.5';
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    S := '';
    if RegQueryStringValue(HKLM, NetSharedFxKey, Candidates[I], S) and NetIsTenOrNewerVersion(S) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

// 「机器上有没有【能跑得起来】的桌面运行时」。
//   ⚠️ 2026-10-13（本轮第二轮）：判据由「名字以 10. 开头」放宽为【大版本 >= 10】
//      （理由与 rollForward 结论见上面 NetIsTenOrNewerVersion 的注释）。
function IsNetDesktop10Installed(): Boolean;
begin
  Result := NetTenOrNewerSubDirExists(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  if not Result then
    Result := NetTenOrNewerSubDirExists(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  if not Result then
    Result := NetTenOrNewerInSharedFxKey();
end;

// ---- r25：就绪页（Ready Memo）提前告知「安装程序会自己下载并静默安装运行时」----
//   用户口径（2026-10-13，参照 LLT 会往就绪页追加一行「附加任务：<运行时> <版本>」）：
//     仅在【检测到缺运行时】时追加一行，让用户在点「安装」之前就知道会发生什么，
//     ⛔ 不会被突然出现的下载进度页吓到。⚠️ 版本串一律 .NET 10，⛔ 不是 LLT 的 8：
//       %1 = Net10RuntimeName（Microsoft Windows Desktop Runtime 10.0 (x64)）
//       %2 = Net10RuntimeVerHint（10.0.x …，取自程序自带 L-Mechrevo.runtimeconfig.json）
//   ⚠️ 定义了 UpdateReadyMemo 就必须自己重建默认内容（下面这套拼接就是 Inno 官方文档给的写法，
//      ⛔ 少一段就会丢掉就绪页原有信息）。
//   ⚠️ 检测用 IsNetDesktop10Installed()：只读注册表/目录，⛔ 无 UI、⛔ 无下载、⛔ 无副作用；
//      任何异常 => 不追加那一行（⛔ 绝不让就绪页因为这一段打不开）。
//   ⚠️ 静默安装下本函数根本不会被调用（没有向导页）=> 无需额外判据。
function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S, Extra: String;
  Missing: Boolean;
begin
  S := '';
  if MemoUserInfoInfo <> '' then
    S := S + MemoUserInfoInfo + NewLine + NewLine;
  if MemoDirInfo <> '' then
    S := S + MemoDirInfo + NewLine + NewLine;
  if MemoTypeInfo <> '' then
    S := S + MemoTypeInfo + NewLine + NewLine;
  if MemoComponentsInfo <> '' then
    S := S + MemoComponentsInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then
    S := S + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    S := S + MemoTasksInfo + NewLine + NewLine;
  Missing := False;
  try
    Missing := not IsNetDesktop10Installed();
  except
    Missing := False;
    Log('LM-NET-DL: 就绪页运行时检测异常 -> 不追加「附加任务」那一行（继续显示默认内容）');
  end;
  if Missing then
  begin
    Extra := ExpandMsg('RuntimeReadyMemo');
    StringChange(Extra, '%1', Net10RuntimeName);
    StringChange(Extra, '%2', Net10RuntimeVerHint);
    S := S + Extra + NewLine;
    Log('LM-NET-DL: 就绪页追加「附加任务」行：' + Net10RuntimeName + ' / ' + Net10RuntimeVerHint);
  end;
  Result := S;
end;

// ================================================================================================
// 打开 .NET 10 桌面运行时（x64）的【官方直链】（用户点「下载 / Download」时唯一的动作）
// ================================================================================================
//   ⚠️⚠️ 2026-10-13（r24）：r23 的第 2 档把 explorer 的路径写成了 {sys}\explorer.exe。
//      本安装包的 [Setup] 是 ArchitecturesInstallIn64BitMode=x64compatible，实测（installer-src\_probe\probe_syspath.iss）：
//        {sys}       = C:\Windows\system32          => C:\Windows\system32\explorer.exe   【不存在】
//        {win}       = C:\Windows                    => C:\Windows\explorer.exe           【存在】<- 真身
//        {syswow64}  = C:\Windows\SysWOW64          => ...\SysWOW64\explorer.exe          【存在】
//      => 第 2 档必然失败，而且 Windows 自己会弹一个「找不到 C:\Windows\System32\explorer.exe」的
//         系统错误框盖在向导上（用户实测截图两次）。r24 起：
//           ① explorer 路径【先解析、再用 FileExists 校验】，候选取第一个真实存在的；
//           ② 候选全不存在 => ⛔ 连调用都不做，只写日志（绝不再把系统错误框弹给用户）；
//           ③ 新增第 3 档：显式读【用户默认浏览器】并启动它（多子档，逐档写日志 + 返回码）。
//   档位（每一档都写日志 + 检查返回值，全部失败才返回 False => 调用方给可见提示）：
//     第1档 ShellExec('open', 直链)                                    ★ 主档：系统 shell 关联直接开
//            ⚠️ 就是启动壳里【实测能拉起浏览器】的那条等价调用（用户实测：「壳倒是可以拉起浏览器诶」）；
//               r23 把这一档排在最后，前面还先弹了系统错误框 => 用户根本看不到浏览器。
//     第2档 ShellExecAsOriginalUser('open', 直链)                      以【原登录用户】身份（最理想）
//     第3档 ShellExec('open', <真实的 explorer.exe>, '"直链"')          explorer 代转降权（路径先校验）
//     第4档 4a ShellExecAsOriginalUser('open', 浏览器 exe, '"直链"')     显式默认浏览器（原用户身份）
//           4b ShellExec('open', 浏览器 exe, '"直链"')                 显式默认浏览器（提权，尽力而为）
//     第5档 剪贴板 + 可见提示（在 HandleNet10MissingAnswer 里：写清逐档结果 + errcode + 原因判断）
//   ⚠️ 一律 ewNoWait：⛔ 不等浏览器、⛔ 不阻塞安装；返回 False 时调用方补一条【可见提示】。
//   ⚠️ 怎么排查（给用户的说明）：日志文件 = %TEMP%\Setup Log YYYY-MM-DD #NNN.txt（SetupLogging=yes），
//      搜 `LM-NET-BROWSER:`；每一档都会留下 `第N档 [成功/失败/异常]` + errcode/rc。
//      ⛔ 一行都搜不到 => 这个包早于 r23（那段代码还不存在）。
var
  NetBrowserTierReport: String;   // 逐档结果摘要（全失败时原样进 MsgBox）

procedure NetTierNote(const S: String);
begin
  if NetBrowserTierReport <> '' then
    NetBrowserTierReport := NetBrowserTierReport + ' ; ';
  NetBrowserTierReport := NetBrowserTierReport + S;
  Log('LM-NET-BROWSER: ' + S);
end;

// explorer.exe 的【真实】位置：四个候选里挑第一个 FileExists 的；一个都没有 => 返回 ''（跳过该档）。
//   ⚠️ 顺序 = 真身优先（{win} = C:\Windows）；{sys}/{sysnative} 放在后面只为兜异常环境。
function NetResolveExplorerExe(): String;
var
  Candidates: TArrayOfString;
  I: Integer;
  S: String;
begin
  Result := '';
  SetArrayLength(Candidates, 4);
  Candidates[0] := ExpandConstant('{win}\explorer.exe');
  Candidates[1] := ExpandConstant('{syswow64}\explorer.exe');
  Candidates[2] := ExpandConstant('{sys}\explorer.exe');
  Candidates[3] := ExpandConstant('{sysnative}\explorer.exe');
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    S := Candidates[I];
    if FileExists(S) then
    begin
      Result := S;
      Log('LM-NET-BROWSER: 第2档 explorer 候选[' + IntToStr(I + 1) + ']=' + S + ' => 存在，采用');
      Exit;
    end;
    Log('LM-NET-BROWSER: 第2档 explorer 候选[' + IntToStr(I + 1) + ']=' + S + ' => 不存在，换下一个');
  end;
  Log('LM-NET-BROWSER: 第2档 explorer 四个候选都不存在 => 跳过这一档（⛔ 不调用，避免系统报错框）');
end;

// 从注册表读【用户默认浏览器】的 exe（第 3 档用）：
//   HKCU\...\UrlAssociations\http\UserChoice 的 ProgId -> HKCU\Software\Classes\<ProgId>\shell\open\command
//   （读不到就退到 HKCR\<ProgId>\...，再退到 http 协议本身的 shell\open\command；本机实测命令在 HKCR）
//   ⚠️ 只取命令行里的 exe 路径：带引号取引号内，不带引号取到第一个空格；解析不到/文件不存在 => ''。
function NetResolveDefaultBrowserExe(): String;
var
  ProgId, Cmd, S: String;
  P: Integer;
begin
  Result := '';
  ProgId := '';
  Cmd := '';
  if RegQueryStringValue(HKCU,
      'Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice', 'ProgId', ProgId) then
  begin
    if ProgId <> '' then
    begin
      if not RegQueryStringValue(HKCU, 'Software\Classes\' + ProgId + '\shell\open\command', '', Cmd) then
        RegQueryStringValue(HKCR, ProgId + '\shell\open\command', '', Cmd);
      Log('LM-NET-BROWSER: 第3档 默认浏览器 ProgId=' + ProgId + ' command=' + Cmd);
    end;
  end;
  if Cmd = '' then
  begin
    if not RegQueryStringValue(HKCU, 'Software\Classes\http\shell\open\command', '', Cmd) then
      RegQueryStringValue(HKCR, 'http\shell\open\command', '', Cmd);
    if Cmd <> '' then
      Log('LM-NET-BROWSER: 第3档 由 http 协议命令解析：' + Cmd);
  end;
  if Cmd = '' then
  begin
    Log('LM-NET-BROWSER: 第3档 读不到默认浏览器命令（UserChoice / Classes 都没有）=> 该档只用 cmd start 子档');
    Exit;
  end;
  S := Trim(Cmd);
  if (Length(S) > 0) and (S[1] = '"') then
  begin
    Delete(S, 1, 1);
    P := Pos('"', S);
    if P > 0 then
      S := Copy(S, 1, P - 1);
  end
  else
  begin
    P := Pos(' ', S);
    if P > 0 then
      S := Copy(S, 1, P - 1);
  end;
  // 命令里可能用 %ProgramFiles% / %LOCALAPPDATA% 之类的环境变量（少见，但兜住）
  StringChange(S, '%ProgramFiles(x86)%', GetEnv('ProgramFiles(x86)'));
  StringChange(S, '%ProgramFiles%', GetEnv('ProgramFiles'));
  StringChange(S, '%LOCALAPPDATA%', GetEnv('LOCALAPPDATA'));
  StringChange(S, '%APPDATA%', GetEnv('APPDATA'));
  StringChange(S, '%SystemRoot%', GetEnv('SystemRoot'));
  if (S <> '') and FileExists(S) then
    Result := S
  else
    Log('LM-NET-BROWSER: 第3档 解析出的浏览器 exe 为空或不存在（' + S + '）=> 跳过 exe 子档');
end;

function OpenNet10DownloadPage(): Boolean;
var
  ErrCode: Integer;
  ExplorerExe, BrowserExe: String;
begin
  Result := True;
  NetBrowserTierReport := '';
  // ---- 第 1 档【主档】：系统 shell 关联直接打开直链（= 启动壳里实测能用的那条等价调用）----
  Log('LM-NET-BROWSER: 第1档 ShellExec open 直链（主档）url=' + Net10DownloadDirectUrl);
  try
    ErrCode := 0;
    if ShellExec('open', Net10DownloadDirectUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
    begin
      NetTierNote('第1档[成功] ShellExec open 直链（errcode=' + IntToStr(ErrCode) + '）');
      Exit;
    end;
    NetTierNote('第1档[失败] ShellExec open 直链 返回 False（errcode=' + IntToStr(ErrCode) + '）');
  except
    NetTierNote('第1档[异常] ShellExec open 直链 抛异常');
  end;
  // ---- 第 2 档：以【原登录用户】身份用 shell 打开（最理想：完全用用户自己的浏览器关联）----
  Log('LM-NET-BROWSER: 第2档 ShellExecAsOriginalUser 开始 url=' + Net10DownloadDirectUrl);
  try
    ErrCode := 0;
    if ShellExecAsOriginalUser('open', Net10DownloadDirectUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
    begin
      NetTierNote('第2档[成功] ShellExecAsOriginalUser（errcode=' + IntToStr(ErrCode) + '）');
      Exit;
    end;
    NetTierNote('第2档[失败] ShellExecAsOriginalUser 返回 False（errcode=' + IntToStr(ErrCode) + '）');
  except
    // 以「管理员身份运行」启动安装包 / 初始 Setup 进程已退出 => 拿不到原始用户令牌，这一档会抛异常
    NetTierNote('第2档[异常] ShellExecAsOriginalUser 抛异常（原始用户不可用：多为「以管理员身份运行」）');
  end;
  // ---- 第 3 档：explorer.exe "<直链>"（★ r24 修正：路径先解析 + FileExists 校验，缺了就不调用）----
  ExplorerExe := NetResolveExplorerExe();
  if ExplorerExe = '' then
    NetTierNote('第3档[跳过] explorer.exe 候选都不存在（r23 用的 {sys}\explorer.exe = C:\Windows\system32\explorer.exe 本就不存在）')
  else
  begin
    Log('LM-NET-BROWSER: 第3档 explorer.exe 开始 path=' + ExplorerExe + ' args="' + Net10DownloadDirectUrl + '"');
    try
      ErrCode := 0;
      if ShellExec('open', ExplorerExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第3档[成功] explorer.exe ' + ExplorerExe + '（errcode=' + IntToStr(ErrCode) + '）');
        Exit;
      end;
      NetTierNote('第3档[失败] explorer.exe ' + ExplorerExe + ' 返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第3档[异常] explorer.exe 调用抛异常（path=' + ExplorerExe + '）');
    end;
  end;
  // ---- 第 4 档：显式读【用户默认浏览器】并启动它（4a 原用户身份 / 4b 提权兜底）----
  BrowserExe := NetResolveDefaultBrowserExe();
  if BrowserExe = '' then
    NetTierNote('第4档[跳过] 读不到可用的默认浏览器 exe')
  else
  begin
    Log('LM-NET-BROWSER: 第4a档 ShellExecAsOriginalUser 默认浏览器 exe=' + BrowserExe);
    try
      ErrCode := 0;
      if ShellExecAsOriginalUser('open', BrowserExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第4a档[成功] 原用户身份启动默认浏览器 ' + BrowserExe);
        Exit;
      end;
      NetTierNote('第4a档[失败] 原用户身份启动浏览器返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第4a档[异常] 原用户身份启动浏览器抛异常');
    end;
    Log('LM-NET-BROWSER: 第4b档 ShellExec（提权身份）默认浏览器 exe=' + BrowserExe);
    try
      ErrCode := 0;
      if ShellExec('open', BrowserExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第4b档[成功] 提权身份启动默认浏览器 ' + BrowserExe);
        Exit;
      end;
      NetTierNote('第4b档[失败] 提权身份启动浏览器返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第4b档[异常] 提权身份启动浏览器抛异常');
    end;
  end;
  // ---- 第 5 档（兜底）：直链已在剪贴板；调用方 HandleNet10MissingAnswer 会给【可见提示】----
  Result := False;
  NetTierNote('第5档[兜底] 自动打开全部失败 -> 直链已在剪贴板，改由【可见提示】让用户手动粘贴；安装照常继续');
  Log('LM-NET-BROWSER: 排查提示：以上每一档都有记录；日志文件 = %TEMP%\Setup Log*.txt（SetupLogging=yes）；'
    + '若一行都搜不到 => 这个包早于 r23（没有这段代码）');
end;

// 把官方【直链】写进剪贴板。返回 True = 真的写进去了；任何一步失败 => False。
//   ⚠️⚠️ 2026-10-13（r25）：r24 用 GlobalAlloc + GlobalLock + SetClipboardData 自己拼——【实测是坏的】：
//      · Inno 6.7.1 的 Pascal Script 里 GlobalLock 返回的 PAnsiChar 转成 AnsiString 是【拷贝】
//        => 逐字符写（P[I] := S[I]）和 lstrcpyA 都只写进拷贝、没写进那块 GlobalMem；实测后果：
//        ① 块里残留的垃圾串比直链短时抛 Inno 内部 Runtime error「Out of string range」（探针实测复现）；
//        ② 就算不抛，也是把【垃圾】塞进用户剪贴板（实测：lstrcpyA 版 / 逐字符版读回都是乱码）。
//      · StrPCopy 不存在（编译期实测 Unknown identifier）；PAnsiChar 不能下标赋值（r24 实测）。
//      ⚠️ 这个异常很致命：本轮把「写剪贴板」放在了弹框【之前】=> 一旦抛出，连选择框都弹不出来。
//   ✅ r25 改用 Windows 自带的 clip.exe：直链写进【不带换行】的临时文件，再 `cmd /c clip < 文件`。
//      实测：rc=0，读回与直链【逐字节相同】（无多余空格/换行）。⛔ 不再 import 任何剪贴板 API。
//   ⚠️ 一律 SW_HIDE + ewWaitUntilTerminated（clip.exe 毫秒级结束；⛔ 不留窗口、⛔ 不阻塞安装）；
//   ⚠️ 整段 try/except：⛔ 任何异常都只记日志 + 返回 False，⛔ 绝不影响安装、⛔ 绝不中止。
//   ⚠️ 剪贴板可能被别的进程临时占用 => 失败【只记日志】，由调用方的可见提示兜底。
function CopyNet10UrlToClipboard(const Where: String): Boolean;
var
  Rc: Integer;
  ClipFile, Cmd: String;
begin
  Result := False;
  if Net10DownloadDirectUrl = '' then
  begin
    Log('LM-NET-CLIP: 直链为空 -> 不写剪贴板（' + Where + '）');
    Exit;
  end;
  // clip.exe 读的是【文件】：写文件时⛔ 不加换行（加了会被一起粘进地址栏）
  ClipFile := ExpandConstant('{tmp}\' + NetDlClipFileName);
  try
    if not SaveStringToFile(ClipFile, Net10DownloadDirectUrl, False) then
    begin
      Log('LM-NET-CLIP: 写临时文件失败 -> 放弃本次复制（' + Where + '）：' + ClipFile);
      Exit;
    end;
    // ⚠️ 直链里没有 cmd 特殊字符（无 & | > < ^）；路径加引号兜住空格（{tmp} 可能带空格）
    Cmd := '/c clip < "' + ClipFile + '"';
    if Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, Rc) and (Rc = 0) then
    begin
      Result := True;
      Log('LM-NET-CLIP: 已把官方直链写入剪贴板（' + Where + '）url=' + Net10DownloadDirectUrl
        + ' len=' + IntToStr(Length(Net10DownloadDirectUrl)) + '（clip.exe rc=0）');
    end
    else
      Log('LM-NET-CLIP: clip.exe 失败 rc=' + IntToStr(Rc) + ' -> 放弃本次复制（' + Where + '）');
  except
    Result := False;
    Log('LM-NET-CLIP: 写剪贴板异常（已吞掉，不影响安装）：' + GetExceptionMessage + '（' + Where + '）');
  end;
end;

// ================================================================================================
// r25：安装器【自己】下载 + 静默安装 .NET 10 桌面运行时（x64）
// ================================================================================================
// 用户口径（2026-10-13）：「不过我觉得可以加,下载失败再弹选择」
//   => 点「安装」那一刻（PrepareToInstall -> PromptNet10Missing）里：
//      (a) 先【自己下载 + 静默安装】：可见进度页（CreateDownloadPage）+ `/install /repair /passive /norestart`；
//      (b) 只有【下载或安装失败】才弹 r24 那个选择框（直链 + 剪贴板 + 五档开浏览器链）；
//      (c) 任何情况 PrepareToInstall 的 Result 恒为 ''（⛔ 绝不中止安装）；
//      (d) 静默安装（/SILENT、/VERYSILENT）：⛔ 不下载、⛔ 不显示进度页、⛔ 不弹框，直接继续；
//      (e) 重试两层：① 自动 = 主直链失败后换【备用直链】再试一次；
//          ② 用户 = 选择框第 1 颗按钮「重试下载 / Retry」再走一遍 (a)。
//   ⚠️ 版本相关一律 .NET 10（⛔ 不是 LLT 的 8）：要装的运行时名字/版本 =
//      Net10RuntimeName / Net10RuntimeVerHint（取自程序自带 L-Mechrevo.runtimeconfig.json 的
//      Microsoft.WindowsDesktop.App = 10.0.0 => 对外显示 10.0.x）；主直链 = aka.ms/dotnet/10.0/…
//      （302 终址解析到 10.0.12）；备用直链 = 该终址（builds.…windowsdesktop-runtime-10.0.12-win-x64.exe）。
//   ⚠️ 不校验 SHA-256：aka.ms 是 evergreen 短链（版本随上游滚动）=> 写死哈希会误判失败；
//      这也是 Inno 官方例子对「下载运行时安装器」的常规做法（Add/DownloadTemporaryFile 的哈希参数传 ''）。
//   ⚠️ 本段【只】在检测到 .NET 10（>= 10）缺失时才会被调用。

// 探针钩子：WizardSilent() 在 Inno 6.7.1 里【不能被同名影子声明】（r24 实测），
//   所以把「是不是静默安装」包成一层普通函数 => 探针可以影子掉它，从而断言 (d) 静默分支。
function IsNetDlSilentMode(): Boolean;
begin
  Result := WizardSilent();
end;

// 下载页对象（可见进度页）：只在这里用；⚠️ 由 InitializeWizard 创建。
//   ⚠️ Inno 的进度页【不占向导页序】（官方文档原文：progress pages are not displayed as part of the
//      normal page sequence）=> 建了不显示也不影响正常安装流程，只有 Show 它时用户才看得到。
var
  NetDownloadPage: TDownloadWizardPage;
  NetDlMaxWaitSec: Integer;   // 等待安装器的上限（秒）；0 = 还没初始化（见 NetDlRunInstaller）

procedure InitializeWizard();
begin
  try
    NetDownloadPage := CreateDownloadPage('正在准备安装 / Preparing to Install',
      '正在下载附加文件（.NET 10 桌面运行时 x64）/ Downloading additional files', nil);
    NetDownloadPage.ShowBaseNameInsteadOfUrl := True;
    Log('LM-NET-DL: 下载页已创建（只有自动下载运行时的时候才会 Show 它）');
  except
    NetDownloadPage := nil;
    Log('LM-NET-DL: 下载页创建失败 -> 自动下载退化为无页面下载 errmsg=' + GetExceptionMessage);
  end;
end;

// (a-1) 把运行时安装包下载到 {tmp}。优先用【可见进度页】；⛔ 失败只返回 False（不把异常抛给调用方）。
//   ⚠️ 这是全脚本【唯一】动 TDownloadWizardPage 的地方 => 探针把整个函数换成自己的替身
//      （TDownloadWizardPage 是 Inno 内建对象：脚本里既构造不出替身对象，也影子不掉它的方法）。
//   ErrMsg 出参只给日志/失败分支用，⛔ 本函数不弹任何框。
function NetDlDownloadToTmp(const Url, BaseName: String; var ErrMsg: String): Boolean;
var
  Sz: Int64;
  TmpPath: String;
begin
  Result := False;
  ErrMsg := '';
  TmpPath := ExpandConstant('{tmp}\' + BaseName);
  if not IsNetDlSilentMode() then
  begin
    // ① 首选：可见下载页（标题「正在准备安装」+ 内容「正在下载附加文件…」+ 停止下载按钮，Inno 标准样式）
    if NetDownloadPage <> nil then
    begin
      try
        NetDownloadPage.ShowBaseNameInsteadOfUrl := True;
        NetDownloadPage.Clear;
        NetDownloadPage.Add(Url, BaseName, '');   // 第 3 参 = RequiredSHA256：'' = 不校验（evergreen 直链）
        NetDownloadPage.Show;
        try
          NetDownloadPage.Download;
          Result := FileExists(TmpPath);
        finally
          NetDownloadPage.Hide;
        end;
        if not Result then
          ErrMsg := '下载页返回后文件不存在：' + TmpPath;
      except
        Result := False;
        ErrMsg := '下载页异常：' + GetExceptionMessage;
        if NetDownloadPage.AbortedByUser then
          ErrMsg := ErrMsg + '（用户点了「停止下载」）';
      end;
      if Result then
        Log('LM-NET-DL: 下载成功（下载页）url=' + Url + ' -> ' + TmpPath)
      else
        Log('LM-NET-DL: 下载失败（下载页）url=' + Url + ' errmsg=' + ErrMsg);
      if Result then
        Exit;
    end;
  end;
  // ② 退化：没有下载页对象（静默模式理论上走不到这里；或 CreateDownloadPage / Add 抛异常）
  //    => 无界面下载。⚠️ DownloadTemporaryFile 失败会【抛异常】，这里吞掉并返回 False。
  try
    Sz := DownloadTemporaryFile(Url, BaseName, '', nil);
    Result := FileExists(TmpPath);
    if Result then
      Log('LM-NET-DL: 下载成功（无页面降级）url=' + Url + ' bytes=' + IntToStr(Sz) + ' -> ' + TmpPath)
    else
    begin
      ErrMsg := '下载后文件不存在：' + TmpPath;
      Log('LM-NET-DL: 下载失败（无页面降级）url=' + Url + ' errmsg=' + ErrMsg);
    end;
  except
    Result := False;
    ErrMsg := '下载异常：' + GetExceptionMessage;
    Log('LM-NET-DL: 下载失败（无页面降级）url=' + Url + ' errmsg=' + ErrMsg);
  end;
end;

// (a-2) 静默安装下载下来的运行时安装包，并且【等待有上限】。
//   为什么用 cmd.exe 包一层：Inno 6.7.1 的 Exec 没有超时参数（ewWaitUntilTerminated = 无限等），
//   而运行时安装可能要几分钟 => 用
//     cmd /v:on /c ""<exe>" <args> & echo !ERRORLEVEL! > <标记文件>"
//   + 轮询标记文件：① 拿到真实退出码；② 超过上限就 taskkill 掉安装器并判失败。
//   ⚠️ 必须 /v:on + !ERRORLEVEL!（延迟展开）：`cmd /c "app & echo %ERRORLEVEL%"` 里的 %ERRORLEVEL%
//      在解析整行时就展开了 => 拿到的是【上一行】的退出码，恒为错值。
//   返回 True = 退出码 0（成功）或 3010/1641（成功但需重启）；False = 启动不了 / 超时 / 非零。
function NetDlRunInstaller(const SetupExe: String; var Rc: Integer; var ErrMsg: String): Boolean;
var
  DoneFile, CmdLine, S: String;
  SA: AnsiString;   // ⚠️ LoadStringFromFile 只认 AnsiString（Inno 6.7.1 实测：String 报 Type mismatch）
  StartRc, Waited: Integer;
begin
  Result := False;
  Rc := -1;
  ErrMsg := '';
  // 上限的来源 NetDlMaxWaitSeconds（600s = 10 分钟）；这里是【惰性初始化】：
  //   ⚠️ 写成变量是为了让探针能把它调小（否则超时用例要真等 10 分钟）。
  if NetDlMaxWaitSec <= 0 then
    NetDlMaxWaitSec := NetDlMaxWaitSeconds;
  DoneFile := ExpandConstant('{tmp}\' + NetDlDoneFileName);
  DeleteFile(DoneFile);
  CmdLine := '/v:on /c ""' + SetupExe + '" ' + NetDlInstallArgs
    + ' & echo !ERRORLEVEL! > "' + DoneFile + '""';
  Log('LM-NET-DL: 安装开始 exe=' + SetupExe + ' args=' + NetDlInstallArgs
    + ' 等待上限=' + IntToStr(NetDlMaxWaitSec) + 's');
  if not Exec(ExpandConstant('{cmd}'), CmdLine, '', SW_SHOW, ewNoWait, StartRc) then
  begin
    ErrMsg := 'Exec 启动安装器失败 rc=' + IntToStr(StartRc);
    Log('LM-NET-DL: 安装失败（启动不了）errmsg=' + ErrMsg);
    Exit;
  end;
  Waited := 0;
  while (Waited < NetDlMaxWaitSec) and (not FileExists(DoneFile)) do
  begin
    Sleep(NetDlPollMs);
    Waited := Waited + (NetDlPollMs div 1000);
  end;
  if not FileExists(DoneFile) then
  begin
    ErrMsg := '安装超时（>' + IntToStr(NetDlMaxWaitSec) + 's）';
    Log('LM-NET-DL: 安装失败（超时）-> taskkill 安装器 image=' + NetDlBaseName);
    Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ' + NetDlBaseName + ' /F /T', '',
      SW_HIDE, ewWaitUntilTerminated, StartRc);
    Exit;
  end;
  // 读退出码（cmd 已经把 !ERRORLEVEL! 写进标记文件）
  SA := '';
  if LoadStringFromFile(DoneFile, SA) then
  begin
    S := SA;
    S := Trim(S);
    Rc := StrToIntDef(S, -1);
  end
  else
    ErrMsg := '读不到退出码文件：' + DoneFile;
  Log('LM-NET-DL: 安装结束 rc=' + IntToStr(Rc));
  if (Rc = 0) or (Rc = 3010) or (Rc = 1641) then
    Result := True
  else
    ErrMsg := '安装器退出码 ' + IntToStr(Rc);
end;

// (a) 走完「下载（主直链 -> 备用直链）+ 静默安装」，成功返回 True。
//   返回 False => 调用方去弹选择框（r24 那个）；⛔ 本函数不弹任何框、⛔ 不抛异常、⛔ 不中止安装。
function NetDlTryInstallRuntime(): Boolean;
var
  Attempt, Rc: Integer;
  Url, ErrMsg: String;
begin
  Result := False;
  if IsNetDlSilentMode() then
  begin
    Log('LM-NET-DL: 静默安装 -> 跳过自动下载/安装（⛔ 不下载、⛔ 不显示下载页、⛔ 不弹框），直接继续安装');
    Exit;
  end;
  Rc := -1;
  for Attempt := 1 to 2 do
  begin
    if Attempt = 1 then
      Url := Net10DownloadDirectUrl   // 主：aka.ms 短链（用户实测可下载）
    else
      Url := Net10DownloadFallbackUrl; // 备用：302 终址（builds.…10.0.12…）
    Log('LM-NET-DL: 开始第 ' + IntToStr(Attempt) + '/2 次下载 url=' + Url);
    DeleteFile(ExpandConstant('{tmp}\' + NetDlBaseName));
    if not NetDlDownloadToTmp(Url, NetDlBaseName, ErrMsg) then
      Log('LM-NET-DL: 第 ' + IntToStr(Attempt) + ' 次下载失败 errmsg=' + ErrMsg)
    else
    begin
      Log('LM-NET-DL: 第 ' + IntToStr(Attempt) + ' 次下载成功 -> 进入静默安装');
      if NetDlRunInstaller(ExpandConstant('{tmp}\' + NetDlBaseName), Rc, ErrMsg) then
      begin
        Result := True;
        Log('LM-NET-DL: 运行时静默安装成功 rc=' + IntToStr(Rc) + ' -> 不再弹选择框');
        Exit;
      end;
      Log('LM-NET-DL: 静默安装失败 errmsg=' + ErrMsg);
      // 安装失败 => 换备用直链重下一次也没意义（同一个包）=> 直接交给选择框（用户可点「重试下载」）
      Break;
    end;
  end;
  Log('LM-NET-DL: 自动下载/安装这条路没成功 -> 交给选择框（r24 五档链不变）');
end;

// (b) 失败时的选择框：只负责弹框 + 返回按钮答案（⛔ 不做别的）。
//   ⚠️ 单独拆一个函数：TaskDialogMsgBox 在 Inno 6.7.1 里不能被同名影子声明（r24 实测），
//      探针只能影子【这个包装函数】=> 才能断言「自动这条路失败后确实走到了选择框」。
//   三颗按钮（TaskDialogMsgBox 只认 MB_YESNOCANCEL 这组返回码）：
//     第1颗 = IDYES    「重试下载 / Retry」          -> 调用方再走一遍自动下载 + 静默安装（对齐 LLT 的重试）
//     第2颗 = IDNO     「用浏览器下载 / Open in browser」-> r24 五档链打开官方直链
//     第3颗 = IDCANCEL 「稍后 / Later」              -> 什么都不做，直接继续安装
//   ⚠️ 直链在弹框【之前】就写进剪贴板（见 PromptNet10Missing）=> 文案里「已复制」成立；
//      本框正文【不可框选】（r24 用户实测）=> 取地址就靠「弹框前那次剪贴板写入」+ 打开失败时的可见提示。
function AskNet10MissingDialog(): Integer;
var
  Labels: TArrayOfString;
  HelpText: String;
begin
  HelpText := ExpandMsg('RuntimeRequired');
  StringChange(HelpText, '%1', Net10RuntimeName);
  StringChange(HelpText, '%2', Net10RuntimeVerHint);
  StringChange(HelpText, '%3', Net10DownloadDirectUrl);
  SetArrayLength(Labels, 3);
  Labels[0] := '重试下载 / Retry';
  Labels[1] := '用浏览器下载 / Open in browser';
  Labels[2] := '稍后 / Later';
  Result := TaskDialogMsgBox('缺少 .NET 10 桌面运行时（x64） / .NET 10 Desktop Runtime (x64) required',
    HelpText, mbInformation, MB_YESNOCANCEL, Labels, IDYES);
end;
// 「弹框拿到答案之后干什么」拆成独立函数（⛔ 与弹框调用解耦，好在探针里逐条喂答案）：
//   IDYES    = 「重试下载 / Retry」=> ⛔ 不在本函数里执行（调用方 PromptNet10Missing 再走一遍自动下载+安装）
//   IDNO     = 「用浏览器下载 / Open in browser」=> 打开官方直链（第1档 系统 shell 关联（主档）-> 第2档 原用户身份 ->
//              第3档 explorer.exe 代转（路径先校验）-> 第4档 显式默认浏览器 -> 第5档 剪贴板 + 可见提示，ewNoWait）；
//              全部档位失败 => 补一条【可见提示】（原来只写日志 = 用户看到的就是"点了没反应"）
//   其它     = 「稍后 / Later」/ Esc / 关窗 => 什么都不做
//   返回：本次流程结束时「直链是否在剪贴板里」（= 弹框前那次复制的结果，除非中途又复制成功）
//   ⛔ 任何分支都【不返回错误、不抛异常、不中止安装】。
//   ⚠️ r25：答案码的含义变了（按钮从「下载/复制链接/稍后」改成「重试下载/用浏览器下载/稍后」）：
//      IDYES = 重试（由调用方执行）· IDNO = 用浏览器下载（五档链）· 其它 = 稍后。
//      ⚠️ 剪贴板那一次写入搬到调用方（弹框【之前】写，文案里的「已复制」才成立）=> 本函数只收结果。
function HandleNet10MissingAnswer(const Answer: Integer; const CopiedIn: Boolean): Boolean;
var
  Copied: Boolean;
begin
  Copied := CopiedIn;
  if Answer = IDNO then
  begin
    Log('LM-NET: 用户点了「用浏览器下载 / Open in browser」-> 打开官方直链，然后立即继续安装');
    if not OpenNet10DownloadPage() then
    begin
      Log('LM-NET: 直链没能在浏览器里打开 -> 仍【继续安装】（直链已在剪贴板，可粘贴到浏览器）');
      // ⚠️ 2026-10-13（本批）：原来是【静默失败】（只在日志里留一行，用户什么都看不到，表现就是「点了没反应」）
      //   => 现在补一条【可见提示】：告诉用户自动打开失败了、地址已复制、可手动粘贴。
      //   ⚠️ 静默安装（/SILENT、/VERYSILENT）⛔ 不弹任何窗：PromptNet10Missing() 在静默时根本不会走到这里，
      //      这里再加一道静默判据（IsNetDlSilentMode = 对 WizardSilent 的探针可影子包装），
      //      防止将来有人改调用顺序时把窗弹进静默流程。
      if not IsNetDlSilentMode() then
      begin
        // ⚠️ r24：文案必须【可见 + 可操作】—— 写清逐档结果（含 errcode/rc）+ 原因判断 + 地址。
        //   剪贴板若第一次没写进去，这里再试一次，并如实区分「已复制」/「复制也失败」。
        if not Copied then
          Copied := CopyNet10UrlToClipboard('自动打开失败后重试');
        if Copied then
          MsgBox('自动打开浏览器没有成功（已走完所有档位，逐档结果如下）。' + #13#10 + #13#10
            + '逐档结果：' + NetBrowserTierReport + #13#10 + #13#10
            + '原因判断：所有自动打开方式都没成功（第1档 系统 shell 关联 / 第2档 原用户身份 / '
            + '第3档 explorer 代转 / 第4档 显式默认浏览器）。若是用「以管理员身份运行」启动的安装程序，'
            + '第2档一定失败；其余档位失败通常说明系统里没有可用的浏览器关联（例如浏览器被安全软件拦下）。' + #13#10 + #13#10
            + '地址【已复制到剪贴板】，请手动粘贴到浏览器地址栏打开（打开即开始下载）：' + #13#10
            + Net10DownloadDirectUrl, mbInformation, MB_OK)
        else
          MsgBox('自动打开浏览器没有成功，复制到剪贴板也失败了。' + #13#10 + #13#10
            + '逐档结果：' + NetBrowserTierReport + #13#10 + #13#10
            + '请手动抄下这个地址，在浏览器里打开（打开即开始下载）：' + #13#10
            + Net10DownloadDirectUrl, mbInformation, MB_OK);
      end;
    end;
  end
  else if Answer = IDYES then
    // 「重试下载」的执行在调用方 PromptNet10Missing 里（要循环重试 + 需要知道成功与否）
    Log('LM-NET: 用户点了「重试下载 / Retry」-> 重试动作由调用方执行（再走一遍自动下载 + 静默安装）')
  else
    Log('LM-NET: 用户点了「稍后 / Later」-> 不开浏览器，直接【继续安装】');
  Result := Copied;
end;

// 缺 .NET 10 桌面运行时时的【中文提示】（非静默才弹；静默一律跳过）：
//   ⚠️ 用户口径（2026-10-13 本轮第二轮修正）：
//     ① 提示里【必须写明要装哪个运行时版本】—— 用户原话「注意提示附带下载地址(运行时版本,因为有的人
//        不知道装哪个版本的) 可复制的那种」；
//     ② ⛔⛔ 【实测纠正】TaskDialogMsgBox 的【正文根本框选不了】（用户实测截图：不可选中复制）
//        => 不能再宣称正文能框选；改成两条【真的能拿到地址】的路：
//          · 弹框【之前】就把直链写进剪贴板（Win32 剪贴板 API；Inno 6 没有 Clipboard 对象）
//            => 文案如实写「已复制到剪贴板」；
//          · 再给一颗「复制链接 / Copy link」按钮 => 点了再写一次剪贴板 + 一句确认。
//     ③ 点「下载 / Download」=> 依次用 ShellExec（主档）/ ShellExecAsOriginalUser / explorer.exe
//        （路径先校验存在）/ 显式默认浏览器 exe 打开【官方直链】
//        （浏览器直接开始下载），随后【立即继续安装】（⛔ 不等待下载、⛔ 不阻塞、⛔ 不中止）；
//        ⚠️ 2026-10-13（r24）：全部档位失败【不再静默】—— 补一条 MsgBox：逐档结果 + errcode + 原因判断
//          + 「地址已复制、请手动粘贴」；
//     ④ 点「复制链接 / Copy link」=> 只再写一次剪贴板 + 提示，⛔ 不开浏览器、⛔ 不中止；
//     ⑤ 点「稍后 / Later」=> 直接继续安装；
//     ⑥ 静默安装（/SILENT、/VERYSILENT）=> ⛔ 一个窗都不弹、⛔ 不开浏览器、⛔ 不碰剪贴板，直接继续。
//   三颗按钮用 MB_YESNOCANCEL 的三个既有返回码承载（Inno 的 TaskDialogMsgBox 只认这一组按钮）：
//     MB_YESNOCANCEL + Labels[0..2] => 第 1 颗 = IDYES(下载) / 第 2 颗 = IDNO(复制链接) / 第 3 颗 = IDCANCEL(稍后)。
//     ShieldButton 传 IDYES（= 第 1 颗「下载」，按 Inno 文档的 ShieldButton 取值法：第 n 颗 = 第 n 个 ID 常量）；
//     原来那个 DefaultButton 位置就是 now 的 ShieldButton 位置 —— 原来是 0 = 默认第 1 颗，行为不变。
//   ⚠️ 这里【仍用】TaskDialogMsgBox（⛔ 不用「可抑制」那个变体）：行为与用户已验收的现网版本一致；
//      ⚠️ 实测 TaskDialogMsgBox（含它的「可抑制」变体）/ WizardSilent / MsgBox 在 Inno 6.7.1 里
//      都【不能】被同名影子声明（Duplicate identifier）=> 探针没法替身掉弹框本身，
//      所以弹框调用与「拿到答案之后干什么」【拆成两个函数】：
//        · AskNet10MissingDialog()        —— 只负责弹框、返回按钮答案（探针只能证明它被调用 + 文案/直链正确）
//        · HandleNet10MissingAnswer(ID)   —— 只负责三条分支（探针用 4 个答案码逐一喂进去，逐条断言副作用）
//   ⚠️ 2026-10-13（r25）：本过程现在【有】下载文件 / 执行运行时安装器的动作了（用户口径
//      「不过我觉得可以加,下载失败再弹选择」）—— 顺序：
//        ① 静默安装（/SILENT、/VERYSILENT）=> ⛔ 不下载、⛔ 不显示下载页、⛔ 不弹框，直接继续；
//        ② 先【自己下载 + 静默安装】（NetDlTryInstallRuntime；成功 => ⛔ 不弹任何框）；
//        ③ 只有失败才弹选择框（AskNet10MissingDialog）+ 逐条执行按钮动作；
//           「重试下载」=> 回到 ②（用户可重复点；点「稍后」/「用浏览器下载」则离开循环）。
//        ④ ⛔ 任何分支都不中止安装（PrepareToInstall 的 Result 恒为 \'\'）。
procedure PromptNet10Missing();
var
  Answer: Integer;
  Ready, Copied: Boolean;
begin
  Log('LM-NET: 未检测到 .NET 10 桌面运行时（x64）-> 中文提示（含要装的版本 + 官方直链）；需要 '
    + Net10RuntimeName + ' / ' + Net10RuntimeVerHint + ' / ' + Net10DownloadDirectUrl);
  if IsNetDlSilentMode() then
  begin
    Log('LM-NET: 静默安装 -> 不下载、不显示下载页、不弹框，直接【继续安装】');
    Exit;
  end;
  // ② 先自己下载 + 静默安装（失败才会弹选择框）
  Log('LM-NET-DL: 自动下载 + 静默安装开始（失败才会弹选择框）');
  Ready := NetDlTryInstallRuntime();
  // ③ 失败才弹选择框；「重试下载」=> 回到 ②（循环由用户按键驱动，⛔ 不是死循环）
  while not Ready do
  begin
    Copied := CopyNet10UrlToClipboard('弹框之前（自动下载/安装失败）');
    if not Copied then
      Log('LM-NET-CLIP: 直链没能放进剪贴板（弹框前）-> 文案里的「已复制」对该用户不成立');
    Answer := AskNet10MissingDialog();
    if Answer = IDYES then
    begin
      Log('LM-NET: 用户点了「重试下载 / Retry」-> 再走一遍自动下载 + 静默安装');
      Ready := NetDlTryInstallRuntime();
      if not Ready then
        Log('LM-NET-DL: 用户重试后仍然失败 -> 再弹一次选择框（可继续重试，或改用浏览器/稍后）');
    end
    else
    begin
      HandleNet10MissingAnswer(Answer, Copied);
      Log('LM-NET: 选择框流程结束（结果一律不返回非空 = 安装继续）');
      Break;
    end;
  end;
  if Ready then
    Log('LM-NET-DL: 运行时已由安装器自动装好（或用户重试成功）-> 安装继续');
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
  // 静默卸载（升级触发）一律保留用户配置
  DoDelete := False;
  if not UninstallSilent() then
  begin
    Prompt := ExistList;
    StringChange(Prompt, '; ', #13#10);
    DoDelete := (TaskDialogMsgBox(
      '卸载时一并删除你的自定义设置？ / Delete your custom settings too?',
      '包括：性能模式自定义档（自定义 1/2 的参数与风扇曲线）、灯效与屏幕校色设置、' + #13#10 +
      '传感器与界面偏好。位置：' + #13#10 + Prompt + #13#10 + #13#10 +
      '「删除」= 直接删除，删除后无法找回。' + #13#10 + #13#10 +
      '「保留」= 不删除，设置原样留在机器上（默认，推荐）。' + #13#10 + #13#10 +
      'Includes custom mode profiles and fan curves, lighting and screen-calibration' + #13#10 +
      'settings, sensor/UI preferences. Delete removes them permanently; Keep leaves them untouched.',
      mbConfirmation, MB_YESNO, ['保留 / Keep', '删除 / Delete'], 0) = IDNO);
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

// ==== 历史（2026-10-09 上一轮，已被下面现行策略取代，保留备查）====
// 当时的做法：把"移除官方控制台 / 厂商组件"改成卸载期可选询问。
// 用户原话（当时）：「LM的卸载官方控制台应该是可选的 而不是强制」。
// 卸载期唯一对"厂商 / 官方控制台"动手的入口就是上面 [UninstallRun] 的 Uninstall-Gcu.ps1，它删的是
// 与官方控制台（GCU）共用、装在系统里的东西，不是 L-Mechrevo 自己的文件：
//   ① GCUBridge 服务：`sc delete GCUBridge`（厂商 GCU 载荷注册的服务；官方控制台也连它，端口 13688）
//   ② UWACPIDriver：`pnputil /delete-driver <oemN.inf> /uninstall /force`（驱动库里的厂商 ACPI 驱动，
//      全系统共享 —— 官方控制台 / ACPI 功能可能仍在用）
// 上一轮据此给用户一次选择：默认「否」= 保留，只有明确选「是」才跑那个脚本；实现符号为
// 常量 VendorRemovalDefault + 变量 GRemoveVendorComponents + 函数 ShouldRemoveVendorComponents
// （初始化在 InitializeUninstall 用 TaskDialogMsgBox 询问，再由 [UninstallRun] 的 Check 门控，
//  静默卸载不弹窗、取默认 = 保留）。
// ⚠️ 上一轮当时的取舍记录：没有加"静默时主动移除"的开关（卸载器侧能否展开 {param:...} 无法验证）。
// ==== 现行（2026-10-09，owner 原话：「卸载期只管卸载LM自己 不管官方」）====
// 卸载期询问**整体废除**：不再问，也没有"移除官方控制台"这个选项 ——
//   * 上面三个符号（VendorRemovalDefault / GRemoveVendorComponents / ShouldRemoveVendorComponents）
//     已删除，语义留在本注释里，不再保留死代码；
//   * [UninstallRun] 无条件执行且固定带 -LmOwnedOnly：卸载期只清 LM 自有内容，厂商/共享组件
//     （GCUBridge 服务、UWACPIDriver 驱动库条目、官方目录/UWP/ARP）一律不碰；
//   * InitializeUninstall 保留（卸载器启动时会调用它），但只写一行策略日志、不再弹窗；
//   * ⚠️ 安装期逻辑本次一行未动（三选项变体询问、[Files]、[Run] 的 -Variant、-RemoveVendorConsole /
//     -KeepVendorConsole 与 /KEEPVENDORCONSOLE 全部原样）。
// 说明：BoolText 仍被安装期日志使用（见下面的安装期询问），所以留在原处不动。

function BoolText(const B: Boolean): String;
begin
  if B then
    Result := 'True'
  else
    Result := 'False';
end;

// L-Mechrevo 自己造出来的东西 —— 与官方控制台无关，**无条件**清理：
//   ① HKLM\SOFTWARE\L-Mechrevo（我方安装标记：GcuVariant / GcuPayloadSha256 / GcuInstalledUtc / ...）
//   ② 开机自启计划任务 LMechrevo*（程序注册的最高权限任务）
//   ③ 防火墙规则 "L-Mechrevo - Block remote GCU MQTT"（名字就是我方的）
// 2026-10-09（卸载期只管 LM 自有）：以前这三样还和 [UninstallRun] 的 Check 答案"二选一并行"，
// 现在没有那个答案了 —— 它们就是卸载期被清理的全部系统级内容（加下面的用户数据一问）。
// 幂等：不存在就跳过；失败只写日志，不阻断卸载。这三样与 Uninstall-Gcu.ps1 -LmOwnedOnly 的同类
// 步骤重叠，重叠无害（两边都幂等）—— 这样即使脚本那一步失败，我方自己的东西也不会留下。
procedure CleanupLmOwnUninstallArtifacts();
var
  ResultCode: Integer;
begin
  if RegKeyExists(HKLM, 'SOFTWARE\L-Mechrevo') then
  begin
    if RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\L-Mechrevo') then
      Log('LM-UNINST: 已删除我方安装标记键 HKLM\SOFTWARE\L-Mechrevo')
    else
      Log('LM-UNINST: 删除 HKLM\SOFTWARE\L-Mechrevo 未完成（可能被占用；可手工删除）');
  end
  else
    Log('LM-UNINST: HKLM\SOFTWARE\L-Mechrevo 不存在（幂等跳过）');

  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
     '-NoProfile -ExecutionPolicy Bypass -Command "Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like ''LMechrevo*'' } | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue"',
     '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('LM-UNINST: 清理我方自启任务 LMechrevo* 完成 (exit=' + IntToStr(ResultCode) + ')')
  else
    Log('LM-UNINST: 无法启动 powershell 清理自启任务（跳过，不影响卸载）');

  if Exec(ExpandConstant('{sys}\netsh.exe'),
     'advfirewall firewall delete rule name="L-Mechrevo - Block remote GCU MQTT"',
     '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('LM-UNINST: 删除我方防火墙规则完成 (exit=' + IntToStr(ResultCode) + '；规则本来就不存在时 netsh 也返回 1，属正常)')
  else
    Log('LM-UNINST: netsh 调用失败（跳过，不影响卸载）');
end;

// 卸载入口：2026-10-09 起**不再询问**（owner 原话「卸载期只管卸载LM自己 不管官方」），只记一行策略日志。
// ⭐ 2026-10-14（owner 原话「如果是卸载的话,那就都删，如果升级的话那保留自定义性能设置和曲线」）：
//   本段日志口径已改成"真卸载 = 全删"。**关键：为什么这里不可能在升级时跑到**
//   ── 本文件会被编译进两个完全不同的可执行体，Inno 只在其中一个里放这些卸载函数：
//      · setup.exe（安装器）：⛔ 不含 [UninstallRun]/[UninstallDelete]，也**不会调用** InitializeUninstall
//        与 CurUninstallStepChanged —— 它们是卸载器（unins000.exe）的入口，这由 Inno 的编译模型保证，
//        不是我们靠 if 判断出来的。所以"升级"（= 新 setup.exe 调旧 unins000.exe）里，
//        跑起来的卸载逻辑永远是【旧版编译进去的那一份】；
//      · unins000.exe（卸载器）：才有 [UninstallRun]/[UninstallDelete]/InitializeUninstall/
//        CurUninstallStepChanged，且**只有在用户真的卸载时才被运行**。
//   ⇒ 升级路径永远不会执行本次新增的 -PurgeVendor / payload 删除：
//      新版 setup 的 [Run] 走的是 Install-Gcu.ps1（从不带 -PurgeVendor）；
//      而被它调起来的旧 unins000.exe 是旧版的、不知道 -PurgeVendor 是什么。
//      再叠加一层 fail-safe：即使有人手工把两个开关一起传给 Uninstall-Gcu.ps1，
//      -LmOwnedOnly 会赢（见该脚本 Invoke-GcuUninstallCleanup 开头），厂商步骤一律不跑。
// ⚠️ 保留这个事件函数是因为卸载器启动时会调用它（InitializeUninstall 是最早的时机）。
function InitializeUninstall(): Boolean;
begin
  Result := True;
  Log('LM-UNINST: 真卸载 = 全删（-PurgeVendor）：LMechrevo* 自启任务 / HKLM\SOFTWARE\L-Mechrevo / ' +
      '我方防火墙规则 / 用户数据另有一问 / GCUBridge 服务（stop+sc delete）/ ' +
      'HKLM\SOFTWARE\OEM\GamingCenter2（整键，含 MyFan3 自定义风扇曲线与灯效）/ ' +
      '{app}\GCU\payload 载荷模板（5 个 7z + 安装期解压出的变体目录）。' +
      '⛔ 驱动库里的 UWACPIDriver 条目仍不碰（系统级共享 + 需重启，只删我方 {app}\UWACPIDriver 副本）。');
end;

// ⭐ 2026-10-15（卸载残留修复：C:\ProgramData\L-Mechrevo 卸载后仍存在）：
//   现象（用户用"卸载后残留扫描"工具扫出）：真卸载完成后 {commonappdata}\L-Mechrevo 仍在。
//   成因（两处写入者，都在本文件里指明了 -LogDir）：
//     ① L293 [Run] 安装期：Install-Gcu.ps1 -LogDir "{commonappdata}\L-Mechrevo\logs"
//        —— 脚本 L1458 会 New-Item 建目录、L1459 写 gcu-install-<ts>.log；
//        且它没传 -StatusDir ⇒ Install-Gcu.ps1 L1463 把 StatusDir 默认成 {commonappdata}\L-Mechrevo，
//        L1190 再往那儿写 gcu-install-status.json（自检状态，与 logs\ 同级，是**父目录**下的文件）。
//     ② L338 [UninstallRun] 卸载期：Uninstall-Gcu.ps1 -LogDir "{commonappdata}\L-Mechrevo\logs"
//        —— Uninstall-Gcu.ps1 L362 同样 New-Item 建目录、L363 写 gcu-uninstall-<ts>.log。
//     ③ [Code] 升级期：GUpgradeBackupRoot = {commonappdata}\L-Mechrevo\upgrade-backup-<ts>（L1968）。
//   为什么原来删不掉 / 原来只删了 logs 也没用：
//     · 原 [UninstallDelete] 只有一条 {commonappdata}\L-Mechrevo\logs（现在的 L371），
//       ⛔ 删不掉父目录 {commonappdata}\L-Mechrevo，也 ⛔ 删不掉父目录下的 gcu-install-status.json；
//     · 更关键的是【时序】：[UninstallDelete] 在 usUninstall 阶段执行，而 [UninstallRun] 的
//       Uninstall-Gcu.ps1 也在同一阶段前后跑 —— 脚本自己还在往 logs\ 写日志。
//       "先删 logs、脚本后写" 或 "脚本占着句柄删不掉" 两种顺序都可能，所以静态段靠不住。
//   ⇒ 修法：挪到 usPostUninstall（卸载的最后一步，[UninstallRun] 早已跑完、日志已写完），
//     在 [Code] 里把 **整个 {commonappdata}\L-Mechrevo** 删掉。
//   ⛔ 安全边界：只删 L-Mechrevo 这一个子目录；{commonappdata} 是全用户共享目录，
//      【绝不】对 {commonappdata} 自身或其任何兄弟目录动手（见下面 PurgeLmCommonAppData 的实现，
//      路径是硬拼接的常量，没有任何通配/上级遍历）。
//   ⭐ 升级为什么永远不会走到这里：本函数是【卸载器 unins000.exe 的专属入口】——
//      ① setup.exe（安装器）不含 [UninstallDelete]、也不调用 CurUninstallStepChanged，
//         这是 Inno 的编译模型保证的（不是我们靠 if 判断出来的）；见上面 InitializeUninstall 的同类说明。
//      ② 升级 = 新 setup.exe 去调【旧版】unins000.exe，跑的是旧版编译进去的那份代码，
//         本次新增的这段在升级时并不存在；
//      ③ 即便某天新卸载器被升级路径调用，它删的也只是"装完才有、下一个安装动作会立刻重建"的
//         日志/状态目录；而升级真正需要保护的 upgrade-backup-<ts> 由 CurStepChanged/ssPostInstall
//         在**卸载器跑完之后**才读回（L2037-L2218），本段在卸载器进程内执行、早于回灌完成，
//         但那正是"新 setup 调旧 unins000"⇒ 旧卸载器【没有】本段 ⇒ 不存在误删。
//         叠加：升级调用旧卸载器时带的是 /VERYSILENT（L2091），而本段与静默与否无关，
//         所以"只在真卸载删"的保证来自①②，⛔ 不来自任何静默判断。
procedure PurgeLmCommonAppData();
var
  Root: String;
  Attempt: Integer;
begin
  // 只删 L-Mechrevo 这一个子目录（硬拼接常量，⛔ 不碰 {commonappdata} 自身与其它任何目录）。
  Root := ExpandConstant('{commonappdata}') + '\L-Mechrevo';
  if not DirExists(Root) then
  begin
    Log('LM-UNINST: {commonappdata}\L-Mechrevo 不存在（幂等跳过）');
    Exit;
  end;
  // 先删 logs 子目录再删根：两步都失败也无所谓，关键是最后 DelTree 整个根目录。
  // DelTree(Path, IsDir, DeleteFiles, DeleteSubdirsAlso) 本身递归且对失败返回 False（不抛异常）。
  for Attempt := 1 to 3 do
  begin
    if DelTree(Root, True, True, True) then
    begin
      Log('LM-UNINST: 已删除 {commonappdata}\L-Mechrevo（第 ' + IntToStr(Attempt) + ' 次尝试成功）');
      Exit;
    end;
    // 删不掉（句柄占用 / 权限）时的重试：⛔ 绝不报错中止卸载，只记日志。
    Log('LM-UNINST: 删除 {commonappdata}\L-Mechrevo 第 ' + IntToStr(Attempt) + ' 次失败（可能被占用；稍后重试）');
    Sleep(400);
  end;
  if DirExists(Root) then
    Log('LM-UNINST: {commonappdata}\L-Mechrevo 仍未能完全删除（可能被占用；不影响卸载完成，可手工删除）: ' + Root)
  else
    Log('LM-UNINST: {commonappdata}\L-Mechrevo 已在重试中删除');
end;

// ⭐⭐ 2026-10-15（owner 问「卸载是先弹窗 还是先停止程序再弹窗」+ 结论：改成**先停程序**）：
//   查证结果：旧版卸载路径**从不结束 L-Mechrevo** —— 全仓库唯一的
//   `taskkill /f /im L-Mechrevo.exe` 在 BackupAndUpgrade()（升级路径）里，注释写着
//   "Make sure L-Mechrevo exits so the uninstaller can delete {app}"。
//   卸载路径没有同样的处理，后果有三：
//     ① {app}\L-Mechrevo.exe / .dll 被占用 ⇒ 卸载删不净（Inno 提示"部分文件无法删除/重启后完成"）；
//     ② 用户在弹窗里选「删除自定义设置」后，**还在运行的 LM 退出时会把
//        %APPDATA%\MechrevoLite\config.json 又写回来** ⇒ 设置"复活"，观感极差；
//     ③ 观感上"卸载完了程序还在跑"。
//   现改成：【结束 LM】→【再弹"是否删除自定义设置"】→【清 LM 自有物件】→【再结束一次（防御）】。
//   为什么放在 usUninstall 而不是 InitializeUninstall：InitializeUninstall 在**用户确认卸载之前**就跑了，
//   那时结束进程会导致"用户点了取消，程序却已被杀掉"；usUninstall 只在用户确认之后触发，最安全。
procedure StopLmOwnProcesses(const Tag: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im L-Mechrevo.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('LM-UNINST: 已尝试结束 L-Mechrevo.exe（' + Tag + '；exit=' + IntToStr(ResultCode) + '；128=进程本来就没在跑）');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // ⭐ 1) 先结束 LM —— 必须在弹窗之前，否则用户选「删除」也会被运行中的程序写回来。
    StopLmOwnProcesses('before the user-data question');
    // ⭐ 2) 再问"是否一并删除自定义设置"。
    HandleUninstallUserData();
    // ⭐ 3) 清 LM 自有物件（含自启任务 —— 先杀掉再删任务，任务就不会把程序拉回来）。
    CleanupLmOwnUninstallArtifacts();
    // ⭐ 4) 防御：万一自启任务在 2)、3) 之间把 LM 拉起来了，这里再结束一次，
    //       好让接下来的文件删除阶段能删净 {app}。
    StopLmOwnProcesses('after cleanup, before file deletion');
  end
  // ⭐ 2026-10-15：残留修复 —— 放在最后一步，等 [UninstallRun] 的 Uninstall-Gcu.ps1 写完日志再删。
  else if CurUninstallStep = usPostUninstall then
    PurgeLmCommonAppData();
end;

// ===========================================================================
// 安装期：是否移除「官方控制台」—— 也就是 Install-Gcu.ps1 的 [2/8]（N7：厂商控制台 ARP 卸载程序、
// 应用包、ControlCenter / GamingCenter* / AISTONE 目录、自启项、快捷方式）。
// 2026-10-09（owner 拍板：改成可选，但默认【移除】）：
//   * 询问的**默认按钮 = 移除**（MB_DEFBUTTON1，默认落在「是」那一颗）；
//   * 这个答案会作为参数透传给 [Run] 的 Install-Gcu.ps1（-RemoveVendorConsole / -KeepVendorConsole）；
//   * 回答「否」= 完整保留：不跑官方控制台卸载，也不调用 N7（一个厂商物件都不碰）；
//   * 静默安装（/SILENT、/VERYSILENT => WizardSilent() = True）**绝不弹窗**（会挂住无人值守流程），
//     直接取默认 = **移除**，与询问的默认按钮一致；要保留请用命令行开关 /KEEPVENDORCONSOLE；
//   * 没检测到官方控制台时沿用原行为：不弹询问，也按默认 = 移除厂商残留。
// ⚠️ 默认值只有一处定义：下面的 InstallVendorConsoleRemovalDefault。
// ⚠️ 历史：安装期默认移除、卸载期默认保留，两边曾因两次改动而刻意相反；2026-10-09（owner
//    「卸载期只管卸载LM自己 不管官方」）卸载期那套"是否移除厂商组件"的默认值（VendorRemovalDefault）
//    已随询问一起废除 —— 卸载期永远不动厂商组件，所以"相反"不再存在。安装期默认移除本次一行未动。
// ===========================================================================
const
  InstallVendorConsoleRemovalDefault = False;         // ⭐ 2026-10-15：False = 保留（新默认）；以后翻转默认只改这一处
  InstallKeepVendorConsoleSwitch = '/KEEPVENDORCONSOLE';

var
  // 本次安装是否移除官方控制台/厂商残留。InitializeSetup 里定好，[Run] 的 {code:} 里读。
  GRemoveVendorConsoleOnInstall: Boolean;
  // .NET 10 桌面运行时自检结果（只在 PrepareToInstall 里赋值）。⚠️ 【只用于记日志】：
  // ⛔ 任何代码路径都不会因为它为 False 而中止安装 / 不启动程序 —— 缺运行时由启动壳（见 [Files]）
  //    在 LM 启动时再给一次中文提示。
  GNet10Ready: Boolean;

// 2026-10-13（本轮）：本函数【保留，但已不再是门控】—— [Run] 上的 Check 已摘掉。
//    保留原因：① 以后若要恢复门控，原型和函数体都在；② 它仍会记一行运行时状态的日志。
//    ⚠️ 原型约束（Inno [Run] 的 Check 要求无参 `function Name: Boolean;`）依旧适用。
function IsNet10RuntimeReady(): Boolean;
begin
  Result := GNet10Ready;
  if not Result then
    Log('LM-NET: [Run] 跳过启动 L-Mechrevo.exe（.NET 10 自检未通过）');
end;

// 静默安装下「保留」的显式开关：命令行（/SILENT、/VERYSILENT 都适用）里出现
// /KEEPVENDORCONSOLE 即为 True。默认（不写这个开关）= False = 移除。
function HasKeepVendorConsoleSwitch(): Boolean;
begin
  Result := Pos(InstallKeepVendorConsoleSwitch, Uppercase(GetCmdTail())) > 0;
end;

// [Run] 的 Parameters 通过 {code:GetVendorConsoleParam} 取用：返回 Install-Gcu.ps1 的开关字符串。
function GetVendorConsoleParam(Param: String): String;
begin
  if GRemoveVendorConsoleOnInstall then
    Result := '-RemoveVendorConsole'
  else
    Result := '-KeepVendorConsole';
end;

function InitializeSetup(): Boolean;
var
  OfficialUninst: String;
begin
  GcuPayloadReady := False;
  // 2026-10-05：旧版卸载项存在 = 覆盖升级 -> 保留用户数据；不存在 = 全新安装 -> 清残留使首次引导重现
  // 不能用 ExpandConstant('{app}...')：此刻 {app} 还没初始化，会抛内部错误。
  GFreshInstall := not IsLmAlreadyInstalled();
  Result := True;
  // ---- 2026-10-13（用户原话：「这个提示不要，先打开安装程序，在安装过程中引导提示」）----
  // ① 本函数【不做任何 .NET 工作】：⛔ 不查注册表、⛔ 不查目录、⛔ 不弹框、⛔ 不开浏览器、⛔ 不下载东西。
  // ② .NET 10 的「检测 + 中文提示（含要装的运行时版本 + 可复制的官网下载地址）+ 可选打开官网」
  //    整段在 PrepareToInstall（见该函数）里做：那时向导已打开、用户刚点过「安装」，而任何文件都还没落盘
  //    => 点『稍后』/ 浏览器打不开都不影响安装（仍是「零半装」）。
  // ③ r25：启动壳已移除 —— 缺运行时由安装阶段自己下载+静默安装；万一仍缺，
  //    apphost 会弹微软自带那句英文提示（⛔ 不再有壳的中文提示）。
  // Upgrade path: the previous L-Mechrevo is uninstalled (after backing up GCU
  // user data) in CurStepChanged(ssInstall); the new files are installed and the
  // backup restored in ssPostInstall. Fresh installs skip the uninstall step.
  // ---- 安装期「官方控制台」询问：答案现在真正决定 Install-Gcu.ps1 的 [2/8]（N7）做不做 ----
  // 优先级：命令行 /KEEPVENDORCONSOLE > 没检测到官方控制台（原行为：不问）> 静默安装（不问）
  //         > 询问（⭐ 2026-10-15 起：默认按钮 = **保留**）。
  OfficialUninst := GetOfficialUninstallString();
  GRemoveVendorConsoleOnInstall := InstallVendorConsoleRemovalDefault;
  if HasKeepVendorConsoleSwitch() then
  begin
    GRemoveVendorConsoleOnInstall := False;
    Log('LM-INSTALL: 命令行 ' + InstallKeepVendorConsoleSwitch + ' -> 保留官方控制台（不卸载、不清理残留），不弹询问');
  end
  else if OfficialUninst = '' then
  begin
    // 没有官方控制台的 ARP 卸载项就不问。⭐ 2026-10-15：默认已是"保留"⇒ 这里通常什么都不做
    // （目录 / 应用包 / 自启项 / 快捷方式一律不动）；想要清理厂商残留才需要显式 -RemoveVendorConsole。
    Log('LM-INSTALL: 未检测到官方控制台 -> 不弹询问，按默认 = 保留厂商物件（RemoveVendorConsole=' +
        BoolText(GRemoveVendorConsoleOnInstall) + '）');
  end
  else if WizardSilent() then
  begin
    // 静默安装（/SILENT、/VERYSILENT）：⛔ 绝不弹窗 —— MsgBox 在静默下照样会显示并挂住无人值守
    // 流程，所以这里显式跳过询问，直接取默认。⭐ 2026-10-15：默认已翻转为"保留"。
    // 官方控制台的卸载（若显式要求）交给 Install-Gcu.ps1 的 [2/8]（N7）统一做，此处不单独跑。
    Log('LM-INSTALL: 静默安装 -> 不弹询问，按默认 = 保留官方控制台（RemoveVendorConsole=' +
        BoolText(GRemoveVendorConsoleOnInstall) + '；要卸载请用 -RemoveVendorConsole 或安装界面上选「否」）');
  end
  else
  begin
    // ⭐ 2026-10-15（owner 口径「并提示是否保留原版控制台」）：询问改为"是否保留"，默认按钮 = 保留。
    //   IDYES（第一颗「是」）= 保留 · IDNO（第二颗「否」）= 卸载（此时才跑 UninstallOfficialConsole）。
    if MsgBox(ExpandMsg('OfficialConsolePrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then
    begin
      GRemoveVendorConsoleOnInstall := False;
      Log('LM-INSTALL: 用户选择保留官方控制台（RemoveVendorConsole=False）-> 官方控制台保留，N7 清理也不会执行');
    end
    else
    begin
      GRemoveVendorConsoleOnInstall := True;
      Log('LM-INSTALL: 用户选择卸载官方控制台（RemoveVendorConsole=True）');
      if not UninstallOfficialConsole() then
      begin
        MsgBox(ExpandConstant('{cm:OfficialConsoleUninstallFailed}'), mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;
  end;
  Log('LM-INSTALL: 安装期 RemoveVendorConsole=' + BoolText(GRemoveVendorConsoleOnInstall) +
      ' -> [Run] 透传 ' + GetVendorConsoleParam(''));
  // ---- ⛔ 本函数【不做任何 .NET 工作】（2026-10-13 本轮确认；用户口径：向导先出来，别一启动就弹）----
  //   ⛔ 不查注册表、⛔ 不查目录、⛔ 不弹框、⛔ 不开浏览器、⛔ 不下载任何东西。
  //   .NET 10 的「检测 -> 中文提示（版本 + 可复制的官网地址）-> 可选打开官网 -> 无论如何继续安装」
  //   整段在 PrepareToInstall（用户点「安装」那一刻、任何文件落盘之前）里做，见该函数。
  //   ⚠️ 本函数只定「安装期是否移除官方控制台」这一件事（GRemoveVendorConsoleOnInstall）。
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

// ==== 2026-10-14（7z 载荷化 + 40B 整体移除）：GCU 变体判定口径 ====
// ⛔ 40B 逻辑已整个删除（owner 指示「40B 逻辑【整个不要了】」）：40 系一律走 `40`。
//    随包载荷由「base + 40/30/50 覆盖层」改成 5 个独立 7z（1020 / 30 / 40 / 50 / WJ），
//    其中 40.7z 就是 40 系的唯一载荷；2026-10-21 起 40 载荷暂时不随包（40 系用 50 载荷，GcuVariant 仍写 40）；
//    不再有 40B 覆盖层、不再有「是否支持核显模式」的询问。
// 新增机型判据（owner 2026-10-14 定）：机型串含 `XINGYAO` 或 `LNL` => 走 WJ 特别变体；
//    其余照常走 30/40/50 判据；判不出来 => 记日志 + 走现有判据（⛔ 不静默乱选）。
//    ⭐ 判据实现与日志都在 Select-GcuPayload.ps1（同一份脚本、同一份口径），本段只保留
//      「把 Auto 结果回读给升级回灌逻辑用」的接线。
var
  // {code:} 常量理论上可能被重复展开，保证只问一次。
  GCuVariantAsked: Boolean;

// [Run] 实际传给 Install-Gcu.ps1 的 -Variant 值：一律 'Auto'（= 判据全部交给脚本）。
function GetEffectiveGcuVariant(): String;
begin
  Result := 'Auto';
end;

// 本次安装会选哪个变体：用刚复制好的 Select-GcuPayload.ps1，以与 [Run] 里 Install-Gcu.ps1
// 完全相同的入参（GetEffectiveGcuVariant：'-Variant Auto'）算一次，写进 OutFile 再读回。
// 只做硬件枚举（注册表 / PCI id 读取），不写任何硬件设置。
// ⭐ 同时把判据证据（机型串 / 命中关键字 / 选中变体 / 解压哪个 7z）写进安装日志，
//    便于装机主导包采集机型数据（见该脚本 Write-Log 侧的 GcuVariantSelection 行）。
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

// [Run] 的 Parameters 里用 {code:GetGcuVariantParam} 调用。40B 已移除 => 恒为 'Auto'。
function GetGcuVariantParam(Param: String): String;
begin
  if not GCuVariantAsked then
  begin
    GCuVariantAsked := True;
    Log('LM-GCU: 变体一律走 Auto 判据（40B 询问已于 2026-10-14 移除；40 系载荷实际为 50，注册表写 50；机型含 XINGYAO/LNL = WJ）');
  end;
  Result := GetEffectiveGcuVariant();
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
  GcuRoot, LiveRoot, BackupRoot, Uninst, Q, OldVariant, UserSource, UserFanSource, AppLocalFile: String;
  LmRoots, LmLeafs, AppLocalFiles: TArrayOfString;
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
  // 2.1/2.2) GCU 自定义模式档 / 风扇表不再备份：LM 本地存档 + 启动重放已覆盖
  // 2.5) 自定义 ICC 已改存用户数据目录（%APPDATA%\MechrevoLite\customICC），随 LM 用户数据一并备份
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
  // 旧版可能把 config.json / 灯效 cfg 放在安装目录；旧卸载器会删 {app}，必须一并备份。
  SetArrayLength(AppLocalFiles, 9);
  AppLocalFiles[0] := 'config.json';
  AppLocalFiles[1] := 'config.json.bak';
  AppLocalFiles[2] := 'rgb.cfg';
  AppLocalFiles[3] := 'lightbar.cfg';
  AppLocalFiles[4] := 'logolight.cfg';
  AppLocalFiles[5] := 'keyboard.cfg';
  AppLocalFiles[6] := 'lighting.user-configured';
  AppLocalFiles[7] := 'fan_curve_bd.cfg';
  AppLocalFiles[8] := 'lhm_sensors.txt';
  for I := 0 to GetArrayLength(AppLocalFiles) - 1 do
  begin
    AppLocalFile := ExpandConstant('{app}\') + AppLocalFiles[I];
    if FileExists(AppLocalFile) then
    begin
      ForceDirectories(BackupRoot + '\app-local');
      if FileCopy(AppLocalFile, BackupRoot + '\app-local\' + AppLocalFiles[I], False) then
        Log('LM-UPDATE: 安装目录设置已备份: ' + AppLocalFile)
      else
      begin
        LmBackupFail := LmBackupFail + 1;
        Log('LM-UPDATE: 安装目录设置备份失败: ' + AppLocalFile);
      end;
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
  //    ⚠️ 2026-10-09（卸载期只管 LM 自有）：这句只对【旧版安装包】成立 —— 新版卸载器
  //       （-LmOwnedOnly）**不再**删/停 GCUBridge 服务，所以升级时"停服解文件锁"完全依赖下面这行
  //       StopGcuProcessesForUpgrade（它真停服务 + 杀 GCUService/GCUBridge 并轮询到退出），别删。
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
  GcuRoot, BackupRoot, OldVariant, IncomingVariant: String;
  VariantSame: Boolean;
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
    if GcuExternalUsed then
    begin
      RegWriteStringValue(HKLM, GcuVariantRegKey, 'GcuExternalSource', GcuExternalNote);
      Log('LM-GCU-EXT: 外置 GCU 来源已登记 = ' + GcuExternalNote);
    end
    else
      Log('LM-GCU-EXT: 本次使用内置 GCU 载荷');
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
      // GCU 自定义模式档不再回灌（由 LM 本地存档 + 启动重放恢复）
  // GCU 风扇表不再回灌（同上）
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
      if DirExists(BackupRoot + '\app-local') then
      begin
        if CopyDirContentsLenient(BackupRoot + '\app-local', ExpandConstant('{app}')) then
          Log('LM-UPDATE: 安装目录设置已回灌: ' + BackupRoot + '\app-local -> {app}')
        else
        begin
          RestoreFail := RestoreFail + 1;
          Log('LM-UPDATE: 安装目录设置回灌失败: ' + BackupRoot + '\app-local -> {app}');
        end;
      end;
      // 清理备份的条件（2026-10-09 起）：回灌全部成功 且 变体一致；否则一律保留（含 LM 两处 + GCU 档 + ICC）。
      // 2026-10-09（#9 保守修法）：清备份的条件从「仅 RestoreFail = 0」改成「RestoreFail = 0 且变体一致」。
      //   原因：变体不一致时上面的 UserPofiles 回灌是按设计跳过的，若仍照旧删掉 upgrade-backup-*，
      //   旧 GCU 的自定义模式档就被静默永久丢失（不同变体布局不同，不能混灌，但也不能删）。
      if RestoreFail = 0 then
      begin
        if DelTree(BackupRoot, True, True, True) then
          Log('LM-UPDATE: 升级回灌全部成功 -> 升级备份目录已清理: ' + BackupRoot)
        else
          Log('LM-UPDATE: 升级回灌成功但备份目录清理失败（无害，可手工删除）: ' + BackupRoot);
      end
      else if RestoreFail > 0 then
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
      end
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
// ⚠️ 2026-10-09：新版卸载器（-LmOwnedOnly）**不再**删 GCUBridge 服务，"解文件锁"只靠下面这次
//    StopGcuProcessesForUpgrade；不要因为上面那句过时说明而删掉它。
// ============================================================================
// ⭐ 2026-10-14（owner 纠偏）：GCU 的安装期临时文件（5 个 7z + 7za.exe）。
//   [Files] 里这 6 条是 Flags: dontcopy ⇒ 它们【不属于安装文件集】，Inno 既不复制也不登记进
//   unins 记录 —— 所以「装完 {app}\GCU 里没有 payload/、没有 7za.exe」是由安装器机制保证的，
//   ⛔ 不是靠收尾时删。
//   它们只在 setup 存活期间存在：下面这个函数把它们解到 {tmp}，[Run] 再把 {tmp} 作为
//   -StagingRoot 交给 Install-Gcu.ps1；脚本解压完会自己删掉 {tmp} 里的这两样。
//   ⚠️ 必须在【任何文件落盘之前】解出（PrepareToInstall 正好满足），否则 [Run] 找不到它们。
//   ⚠️ 解不出任何一件 = 载荷不可能装上 ⇒ 返回非空字符串让 Inno 中止安装。这是刻意的：
//      宁可不装，也不要装出一个没有 GCU 本体的半成品（那正是上一版留下的坏状态）。
// ============================================================================
// ⭐⭐ 2026-10-16（0.291.9 · 宝龙达模具适配）：⭐ 宝龙达机型【不安装 GCU】的判据
//   ⭐ 用户原话：「对于蛟龙16Pro 30系 40系 不安装GCU 走宝龙达路」
//   ⚠️⚠️ 用户补充澄清（⭐ 关键，⭐ 它决定了判据形态）：
//      「注意不要改蛟龙其他机型已经50系的蛟龙 其他都是同方模具的 只有30 40系的蛟龙16pro不是」
//      ⇒ ⭐ **只有 蛟龙16Pro 的 30系 / 40系 是宝龙达模具** ✅
//      ⚠️ 其余蛟龙（⭐ **含 50 系蛟龙**）**都是同方模具** ⇒ ⭐ **照旧装 GCU** ✅
//   ⛔ 因此【绝不能】用「型号串含 Jiaolong」当判据 —— ⚠️ 50 系蛟龙也含它 ⇒ **会误伤** ❌
//   ⛔ 也【不能】用「有 MICommonInterface」当主判据 —— ⚠️ 同方机型**也有**这个 WMI 类 ❌
//   ⭐ 判据 = 【白名单式】（⭐ 原则：⭐ 宁可不跳过，⭐ 也绝不误伤同方 ✅）：
//      ⭐ ① 板号**精确命中** `MRID6-23` ⇒ 跳过 GCU
//         （⭐ 依据：⭐ 三台真机抓包一致 ✅，⭐ 配置 RTX 4060 / 4070 ⇒ 即"40 系" ✅）
//      ⭐ ② 扩展：板号以 `MRID6-` 开头 ⇒ 跳过
//         ⭐ **与后端 `BaolongdaBackend` 的判据【逐条一致】**（⭐ 父 agent 2026-10-16 明确要求对齐 ⚠️）：
//            ⭐ 后端也是「`MRID6-23` 精确 ＋ `MRID6-` 前缀放行 ＋ 其余不认 ＋ 读不到按不是宝龙达」✅
//            ⭐ ⇒ ⭐ 两路结论**永远一致**（⭐ 要么都走宝龙达、⭐ 要么都装 GCU ✅）
//         ⚠️⚠️ **未验证风险（⭐ 必须记档 ⚠️）**：⭐ 若**某个 `MRID6-*` 其实是同方模具** ⚠️
//            ⇒ ⭐ 会被误判 ⇒ ⭐ **不装 GCU ⇒ 功能缩水**（⭐ 静默、⭐ 难查 ⚠️）
//            ⭐ 依据：⭐ 用户判断 30/40 系都是 `MRID6-*`（⭐ 40 系已知 `MRID6-23` ✅；⭐ 30 系**未证实** ⚠️）
//            ⭐ ⇒ ⭐ 按用户判断走 ✅，⭐ 但**列为待真机确认项** ⚠️
//         ⭐ 要**收紧**成"只认精确命中"⇒ ⭐ 把下面那个 `if` 的条件最前面加上 `False and ` 即可（⭐ 一处 ✅）
//      ⭐ ③ **其余一切情况**（⭐ 含读取失败 / ⭐ 值缺失 / ⭐ 任何不确定）⇒ ⭐ **照旧装 GCU** ✅
//   ⭐ 注册表来源（⭐ 真机抓包实证 ✅）：`HKLM\HARDWARE\DESCRIPTION\System\BIOS`
//      ⭐ `BaseBoardProduct`（⭐ = `MRID6-23` ✅）· ⭐ `SystemProductName`（⭐ = `Jiaolong Series MRID6` ✅）
//      ⭐ 用注册表而**不用 WMI**：⭐ ① Inno Pascal 读注册表是一行、⭐ 无需 COM/WMI ✅
//         ⭐ ② 这两项与 WMI 的 `Win32_BaseBoard.Product` / `Win32_ComputerSystemProduct.Name` 同源 ✅
//   ⚠️ 刻意**不用全局变量缓存**：⭐ 一是避开"⭐ 全局 var 段该放哪"⭐ 的语法风险 ⚠️、
//      ⭐ 二是 Inno 的 `Check:` 逐条调用，⭐ 每次两次注册表读取**可以忽略** ✅
//   ⚠️ 只在**安装期**用它决定"装不装 GCU"；⛔ 那三个 ps1 仍**无条件安装**（⭐ 卸载要用 ✅）
function NoGcuPackage(): Boolean;
// ⭐ 2026-10-16（GCU 分离包）：编译期常量 —— 本包是否【不含 GCU】
//   用 ISCC /DNOGCU 编译 => True；默认（不带宏）=> False（含 GCU 的主包）
begin
#ifdef NOGCU
  Result := True;
#else
  Result := False;
#endif
end;

function GetBaseBoardProduct(): String;
// ⭐ 2026-10-16（GCU 分离包）：只读主板型号，供「同方装了会提示」用。
//   ⚠️ 与 IsBaolongdaMold 读同一个键，⛔ 判据不受本函数影响。
begin
  Result := '';
  RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'BaseBoardProduct', Result);
  if Result = '' then Result := '(读不到)';
end;
function ShouldInstallPawnIo(): Boolean; forward;
function IsWujieMold(): Boolean;
// 2026-10-18（机主：无界这类用 SMU 的机型也应默认安装 PawnIO）：
//   只读 BIOS 注册表 SystemProductName，含 WUJIE 即认定（⛔ 不写任何东西）。
var
  Model: String;
begin
  Result := False;
  Model := '';
  RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'SystemProductName', Model);
  Result := Pos('WUJIE', Uppercase(Model)) > 0;
  if Result then Log('LM-PAWNIO-FORCE: 检测到无界机型（SystemProductName=' + Model + '）');
end;

function IsBaolongdaMold(): Boolean; forward;


procedure CurPageChanged(CurPageID: Integer);
// 2026-10-18（机主：每次装 LM 都会看到 PawnIO 勾选）：已安装 PawnIO 时，任务页里自动取消并改写该项，
//   ⛔ 不重复安装；未安装时保持原样（默认勾选，可手动取消）。
var
  I: Integer;
begin
  if CurPageID = wpSelectTasks then
  begin
    if not ShouldInstallPawnIo() then
    begin
      try
        for I := 0 to WizardForm.TasksList.Items.Count - 1 do
          if Pos('PawnIO', WizardForm.TasksList.Items[I]) > 0 then
          begin
            WizardForm.TasksList.Checked[I] := False;
            WizardForm.TasksList.ItemCaption[I] := 'PawnIO 驱动（已安装，无需再次安装）';
          end;
      except
        Log('LM-PAWNIO-SKIP: 已安装 PawnIO，但任务页勾选改写失败（忽略）');
      end;
      Log('LM-PAWNIO-SKIP: 已安装 PawnIO ⇒ 任务页中该项已取消勾选（不再重复安装）');
    end
    else
    begin
      // 2026-10-18（机主：宝龙达应强制要求安装 PawnIO）：
      //   宝龙达的 风扇强冷 / 风扇曲线 / 电池充电阈值 全部依赖 PawnIO（EC 写入）⇒
      //   任务页里强制勾选并改写说明，避免用户取消后 EC 功能全废。
      if IsBaolongdaMold() or IsWujieMold() then
      begin
        try
          for I := 0 to WizardForm.TasksList.Items.Count - 1 do
            if Pos('PawnIO', WizardForm.TasksList.Items[I]) > 0 then
            begin
              WizardForm.TasksList.Checked[I] := True;
              if IsBaolongdaMold() then
                WizardForm.TasksList.ItemCaption[I] := '安装 PawnIO 驱动（宝龙达机型必需：风扇强冷/曲线/电池阈值）'
              else
                WizardForm.TasksList.ItemCaption[I] := '安装 PawnIO 驱动（无界机型必需：SMU 功耗自定义）';
            end;
        except
          Log('LM-PAWNIO-FORCE: 宝龙达机型强制勾选 PawnIO 失败（忽略）');
        end;
        Log('LM-PAWNIO-FORCE: 宝龙达/无界机型 ⇒ 已强制勾选 PawnIO 安装（EC/SMU 功能依赖它）');
      end;
    end;
  end;
end;

function ShouldInstallPawnIo(): Boolean;
// 2026-10-18（机主反馈「PawnIO 会重复安装」）：驱动服务键已存在 ⇒ 认为已安装，安装包不再重复跑安装脚本。
//   ⚠️ 只读注册表，⛔ 不探测设备；驱动坏了仍可从 LM「CPU 调节」里手动重装。
begin
  Result := not RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\PawnIO');
  if not Result then Log('LM-PAWNIO-SKIP: 已安装 PawnIO（服务键存在）=> 跳过安装包里的驱动安装步骤');
end;

function IsBaolongdaMold(): Boolean;
var
  Board: String;
  Model: String;
  BoardU: String;
  ModelU: String;
begin
  Result := False;
  Board := '';
  Model := '';
  try
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'BaseBoardProduct', Board);
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'SystemProductName', Model);
    BoardU := Uppercase(Board);
    ModelU := Uppercase(Model);
    // ⭐ ① 白名单：精确命中（真机证据 ✅）
    if BoardU = 'MRID6-23' then
    begin
      Log('LM-GCU-SKIP: 板号 = MRID6-23（宝龙达模具 · 蛟龙16Pro 40系）=> 本机【不安装 GCU】');
      Result := True;
      Exit;
    end;
    // ⭐ ①b 精确命中【裸】`MRID6`（⭐ 2026-10-17 真机：⭐ 蛟龙16 · R7-6800H ＋ RTX 3070Ti ✅）
    //    ⚠️ 旧判据只认「精确 `MRID6-23`」＋「前缀 `MRID6-`」⇒ ⭐ 裸 `MRID6` 两条都不命中 ❌
    //    ⇒ ⭐ 整台机照旧装 GCU、后端退回 legacy ⇒ ⭐ 一连串"点了没反应"⚠️（⭐ 与后端白名单逐条一致 ✅）
    if BoardU = 'MRID6' then
    begin
      Log('LM-GCU-SKIP: 板号 = MRID6（宝龙达模具 · 蛟龙16 30系 · 裸板号）=> 本机【不安装 GCU】');
      Result := True;
      Exit;
    end;
    // ⭐ ② 扩展：板号前缀 MRID6-（⭐ 与后端判据逐条一致 ✅）
    //    ⚠️ 待真机确认（30 系板号未证实）；⭐ 要收紧成"只认精确命中"就把这行条件加上 False and
    if Copy(BoardU, 1, 6) = 'MRID6-' then
    begin
      Log('LM-GCU-SKIP: 板号 = ' + Board + '（MRID6- 前缀 · 宝龙达模具 · 待真机确认）=> 本机【不安装 GCU】');
      Result := True;
      Exit;
    end;
    Log('LM-GCU-SKIP: 判据未命中 => 照旧安装 GCU；板号=' + Board + ' · 型号=' + Model);
  except
    // ⚠️ 任何异常 ⇒ ⭐ 不跳过（照旧装 GCU）—— ⭐ 保守优先，⛔ 绝不因判据异常而改变现有行为 ✅
    Log('LM-GCU-SKIP: 判据读取异常 => 按【非宝龙达】处理，照旧安装 GCU');
    Result := False;
  end;
end;

function IsXingyaoMold(): Boolean;
var
  Board: String;
  Model: String;
  BoardU: String;
  ModelU: String;
begin
  Result := False;
  Board := '';
  Model := '';
  try
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'BaseBoardProduct', Board);
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'HARDWARE\DESCRIPTION\System\BIOS', 'SystemProductName', Model);
    BoardU := Uppercase(Board);
    ModelU := Uppercase(Model);
    if (Pos('XINGYAO', BoardU) > 0) or (Pos('P916F', BoardU) > 0)
       or (Pos('XINGYAO', ModelU) > 0) or (Pos('P916F', ModelU) > 0) then
    begin
      Log('LM-GCU-SKIP: 星耀机型（板号=' + Board + ' · 型号=' + Model + '）=> 本机【不安装 GCU】');
      Result := True;
      Exit;
    end;
  except
    Log('LM-GCU-SKIP: 星耀判据读取异常 => 按【非星耀】处理，照旧安装 GCU');
    Result := False;
  end;
end;

// ⭐ [Files] / [Run] 的 `Check:` 参数用这个（⭐ 名字直说"该不该装 GCU" ✅）。
//   ⭐ 返回 False ⇒ ⭐ Inno 跳过该条目 ✅；⭐ 返回 True ⇒ ⭐ 照旧安装 ✅。
//   ⚠️ Inno 的 `Check:` 语义 = "返回 True 才处理这一条" ✅ ⇒ 所以这里取反 ✅。
//   ⚠️⚠️ **原型必须是【无参】的** `function Name: Boolean` —— ⚠️ 实测写成
//      `function Name(Param: String): Boolean` 会被 ISCC 拒绝：
//      「Invalid prototype for 'ShouldInstallGcu'」⇒ ⭐ 编译中止 ❌
//      （⭐ 带 `Param: String` 的那种只适用于 `{code:Name}` 常量展开，⛔ 不适用于 Check ✅）
function ShouldInstallGcu(): Boolean;
begin
  if IsBaolongdaMold() then
  begin
    Result := False;
    Exit;
  end;
  if IsXingyaoMold() then
  begin
    Result := False;
    Exit;
  end;
  Result := True;
end;

// 精简包（/DNOGCU_PAYLOAD）没有可用 GCU 载荷时跳过 [Run] 的 GCU 安装步骤，但 LM 照常安装
function ShouldRunGcuInstall(): Boolean;
begin
  if not ShouldInstallGcu() then
  begin
    Result := False;
    Exit;
  end;
#ifdef NOGCU_PAYLOAD
  Result := GcuPayloadReady;
#else
  Result := True;
#endif
end;

function GcuArchiveName(Index: Integer): String;
begin
  case Index of
    0: Result := '1020.7z';
    1: Result := '30.7z';
    2: Result := '40.7z';
    3: Result := '50.7z';
    4: Result := 'WJ.7z';
  else
    Result := '';
  end;
end;

// 外置 GCU 包：在下面三种位置找 payload（只需含本机需要的变体即可）
//   ① setup 同目录\GCU\<变体>.7z
//   ② setup 同目录\GCU.7z（内含任意子集，扁平/GCU\/payload\ 三层布局都认）
//   ③ setup 同目录直接放 <变体>.7z
function FindStagedGcuFile(Root, Name: String): String;
var
  Candidates: TArrayOfString;
  I: Integer;
begin
  Result := '';
  SetArrayLength(Candidates, 3);
  Candidates[0] := AddBackslash(Root) + Name;
  Candidates[1] := AddBackslash(Root) + 'payload\' + Name;
  Candidates[2] := AddBackslash(Root) + 'GCU\' + Name;
  for I := 0 to GetArrayLength(Candidates) - 1 do
    if FileExists(Candidates[I]) then
    begin
      Result := Candidates[I];
      Exit;
    end;
end;

function StageExternalGcuPayloads(): Boolean;
var
  BaseDir, ExtDir, ExtArchive, ExtractedDir, Src, Dest: String;
  I, Found, ResultCode: Integer;
begin
  Result := False;
  BaseDir := ExtractFileDir(ExpandConstant('{srcexe}'));
  ExtDir := AddBackslash(BaseDir) + 'GCU';
  ExtArchive := AddBackslash(BaseDir) + 'GCU.7z';
  ForceDirectories(ExpandConstant('{tmp}\payload'));
  Found := 0;
  // ① 同目录 GCU\ 文件夹
  if DirExists(ExtDir) then
  begin
    for I := 0 to 4 do
    begin
      Dest := ExpandConstant('{tmp}\payload\' + GcuArchiveName(I));
      Src := AddBackslash(ExtDir) + GcuArchiveName(I);
      if FileExists(Src) and CopyFile(Src, Dest, False) then
        Found := Found + 1;
    end;
    if Found > 0 then
    begin
      GcuExternalUsed := True;
      GcuExternalNote := 'sibling-GCU-folder';
      Log('LM-GCU-EXT: 同目录 GCU\ 文件夹载荷已就绪（' + IntToStr(Found) + ' 个变体）');
    end;
  end;
  // ② 同目录 GCU.7z（或 GCU\ 文件夹里再套一个 GCU.7z）——用内置 7za 解出
  if (Found = 0) and not FileExists(ExtArchive) then
  begin
    if FileExists(AddBackslash(ExtDir) + 'GCU.7z') then
      ExtArchive := AddBackslash(ExtDir) + 'GCU.7z';
  end;
  if (Found = 0) and FileExists(ExtArchive) and FileExists(ExpandConstant('{tmp}\7za.exe')) then
  begin
    ExtractedDir := ExpandConstant('{tmp}\gcu_ext');
    if Exec(ExpandConstant('{tmp}\7za.exe'), 'x -y -o"' + ExtractedDir + '" "' + ExtArchive + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      for I := 0 to 4 do
      begin
        Src := FindStagedGcuFile(ExtractedDir, GcuArchiveName(I));
        Dest := ExpandConstant('{tmp}\payload\' + GcuArchiveName(I));
        if (Src <> '') and CopyFile(Src, Dest, False) then
          Found := Found + 1;
      end;
      if Found > 0 then
      begin
        GcuExternalUsed := True;
        GcuExternalNote := 'sibling-GCU.7z';
        Log('LM-GCU-EXT: 同目录 GCU.7z 已解出外置载荷（' + IntToStr(Found) + ' 个变体）');
      end;
    end
    else
      Log('LM-GCU-EXT: GCU.7z 解压失败（7za exit=' + IntToStr(ResultCode) + '）=> 回退内置载荷');
  end;
  // ③ 同目录直接放 <变体>.7z（单变体包）
  if Found = 0 then
  begin
    for I := 0 to 4 do
    begin
      Src := AddBackslash(BaseDir) + GcuArchiveName(I);
      Dest := ExpandConstant('{tmp}\payload\' + GcuArchiveName(I));
      if FileExists(Src) and CopyFile(Src, Dest, False) then
        Found := Found + 1;
    end;
    if Found > 0 then
    begin
      GcuExternalUsed := True;
      GcuExternalNote := 'sibling-variant-7z';
      Log('LM-GCU-EXT: 同目录单变体载荷已就绪（' + IntToStr(Found) + ' 个变体）');
    end;
  end;
  if Found > 0 then
    Log('LM-GCU-EXT: 外置 GCU 载荷共 ' + IntToStr(Found) + ' 个变体；缺失变体稍后用内置载荷补齐');
  Result := Found > 0;
end;

function ExtractGcuStagingTemporaries(): String;
var
  I: Integer;
  Failed: String;
  GcuStagingFiles: TArrayOfString;
  Dest, Src: String;
begin
  Result := '';
  if NoGcuPackage() then begin Log('LM-GCU-SKIP: 本包不含 GCU（/DNOGCU）=> 不准备也不落地 GCU'); Exit; end;
  Failed := '';
  if IsBaolongdaMold() then
  begin
    Log('LM-GCU-SKIP: 宝龙达机型 => 跳过 GCU 载荷解压（本机不安装 GCU，无载荷需求）');
    Result := '';
    Exit;
  end;
  if IsXingyaoMold() then
  begin
    Log('LM-GCU-SKIP: 星耀机型 => 跳过 GCU 载荷解压（本机不安装 GCU，无载荷需求）');
    Result := '';
    Exit;
  end;
  SetArrayLength(GcuStagingFiles, 5);
  GcuStagingFiles[0] := '1020.7z';
  GcuStagingFiles[1] := '30.7z';
  GcuStagingFiles[2] := '50.7z';
  GcuStagingFiles[3] := 'WJ.7z';
  GcuStagingFiles[4] := '7za.exe';
  // 先解出 7za.exe（外置 GCU.7z 解压需要）
  try
    ExtractTemporaryFile('7za.exe');
  except
    Log('LM-GCU: 7za.exe 临时解出失败（若用外置 GCU.7z 将无法解压）');
  end;
  // 外置 GCU 包优先（单变体也可用）
  StageExternalGcuPayloads();
#ifndef NOGCU_PAYLOAD
  // 内置载荷补齐缺失变体
  for I := 0 to 3 do
  begin
    Dest := ExpandConstant('{tmp}\payload\' + GcuStagingFiles[I]);
    if not FileExists(Dest) then
    begin
      try
        ExtractTemporaryFile(GcuStagingFiles[I]);
        Src := ExpandConstant('{tmp}\' + GcuStagingFiles[I]);
        if not RenameFile(Src, Dest) then
        begin
          if Failed <> '' then Failed := Failed + ', ';
          Failed := Failed + GcuStagingFiles[I];
        end;
      except
        Log('LM-GCU: 临时文件解出失败（dontcopy）: ' + GcuStagingFiles[I]);
        if Failed <> '' then Failed := Failed + ', ';
        Failed := Failed + GcuStagingFiles[I];
      end;
    end;
  end;
#else
  for I := 0 to 3 do
  begin
    Dest := ExpandConstant('{tmp}\payload\' + GcuStagingFiles[I]);
    if not FileExists(Dest) then
    begin
      if Failed <> '' then Failed := Failed + ', ';
      Failed := Failed + GcuStagingFiles[I];
    end;
  end;
#endif
  if not FileExists(ExpandConstant('{tmp}\7za.exe')) then
  begin
    try
      ExtractTemporaryFile('7za.exe');
    except
      if Failed <> '' then Failed := Failed + ', ';
      Failed := Failed + '7za.exe';
    end;
  end;
  GcuPayloadReady := (Failed = '');
  if Failed <> '' then
#ifndef NOGCU_PAYLOAD
    Result := 'GCU 安装所需的载荷无法准备齐全（' + Failed + '）。' + #13#10 +
      '若使用外置 GCU 包，请确认包内含本机需要的变体；或改用含内置载荷的完整安装包。' + #13#10 + #13#10 +
      'Failed to prepare the GCU payload (' + Failed + ').';
#else
  begin
    // 精简包：只提示，不阻止 LM 安装（装完可在应用里点 GCU 状态导入）
    Log('LM-GCU-EXT: 精简包未找到可用 GCU 载荷（' + Failed + '）⇒ 继续安装 LM，GCU 可在应用内导入');
    if not WizardSilent() then
      MsgBox('本安装包不含内置 GCU 载荷（精简包）。' + #13#10 + #13#10 +
        '缺少的变体：' + Failed + #13#10 + #13#10 +
        '安装将继续：装好后打开 L-Mechrevo，点顶部的「GCU」状态即可导入外置 GCU 包并安装。' + #13#10 +
        '（也可以把 GCU\ 文件夹或 GCU.7z 放到安装包同目录后重新运行安装器）' + #13#10 + #13#10 +
        'This setup ships without embedded GCU payloads; installation will continue and GCU can be imported inside L-Mechrevo.',
        mbInformation, MB_OK);
    Result := '';
  end;
#endif
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  GcuTarget: String;
begin
  Result := '';
  // ⭐⭐ 2026-10-16（GCU 分离包）：本包【不含 GCU】=> 只适用于宝龙达模具（蛟龙16Pro 30/40 系）。
  //   若非宝龙达（同方模具）=> 必须【明确告知】并给选择，⛔ 不能静默继续。
  //   ⚠️ 注意：这里【不使用】Result（本文件既有铁律：Result 一律保持 ''），
  //     取消走 Abort（Inno 会干净地中止，且此刻任何文件都还没落盘 => 零半装）。
  if NoGcuPackage() and (not IsBaolongdaMold()) then
  begin
    if MsgBox('本安装包【未包含 GCU 组件】，仅适用于宝龙达模具机型（蛟龙16Pro 30/40 系）。' + #13#10 + #13#10 +
        '检测到本机主板型号为：' + GetBaseBoardProduct() + #13#10 +
        '该型号属同方模具 => 本包装不上 GCU，GCU 相关功能将不可用。' + #13#10 + #13#10 +
        '请改用正式版安装包（含 GCU）。' + #13#10 + #13#10 +
        '是否仍然继续安装（主程序可用，但 GCU 功能不可用）？', mbConfirmation, MB_YESNO) = IDNO then
      Abort;
  end;
  // ---- 2026-10-13（本轮）.NET 10 前置就落在这里 ----
  // 为什么是 PrepareToInstall（而不是 InitializeSetup / CurPageChanged）：
  //   ① InitializeSetup 在【向导出现之前】跑 —— 用户原话「这个提示不要，先打开安装程序，在安装过程中
  //      引导提示」=> ⛔ 那里一点 .NET 工作都不做；
  //   ② PrepareToInstall 是用户在「准备安装」页点下【安装】那一刻被 Inno 调用的，且在任何文件/注册表
  //      落盘之前 => 提示正好出现在「能点安装按钮」那一步，且拒绝/失败仍是「零半装」；
  //   ③ 它在【静默安装下也会被调用】，所以「静默 => 不弹框、直接继续」能和普通安装共用同一条代码路径
  //      （CurPageChanged(wpReady) 在静默下根本不触发，还得再写一条重复路径；且用户返回上一步再前进
  //      会重复弹）。
  // ⛔⛔ Result 一律保持 ''：返回非空 = Inno 判定失败 = 中止安装 —— 用户明确要求「任何情况都不中止」。
  //   ⛔ 本段不再有下载文件、不再有执行运行时安装器、不再有重试询问；只剩
  //      「检测 -> 中文提示（版本 + 可复制官网地址）-> 可选打开官网 -> 无论如何继续」。
  Log('LM-PROBE-MARK-PRE-NET-ENTRY: PrepareToInstall 进入 .NET 10 前置段');
  try
    GNet10Ready := IsNetDesktop10Installed();
    if GNet10Ready then
      Log('LM-NET: 已检测到 .NET 10 桌面运行时（x64）-> 不提示、不打开任何页面')
    else
      PromptNet10Missing();
    // ⚠️ 写死 True：[Run] 执行的是程序本体；若运行时最终没装上（下载失败又选了「稍后」），
    //    启动时由 apphost 自己弹微软那句英文提示（r25 起不再有启动壳）。
    GNet10Ready := True;
  except
    // ⛔ 兜底：本段任何异常都吞掉（绝不冒出 PrepareToInstall），照常安装。
    GNet10Ready := True;
    Log('LM-NET: 写入前 .NET 10 检测/提示/自动下载段异常 -> 已吞掉，照常安装；启动时由 apphost 自己提示');
  end;
  GcuTarget := ExpandConstant('{app}\GCU');
  // ⭐ 2026-10-14：先解出 GCU 的安装期临时文件（dontcopy 的 5 个 7z + 7za.exe -> {tmp}），
  //    并据此决定要不要中止。放在最前面：这 6 件解不出来，后面的 [Run] 一定会失败，
  //    而那时文件已经落盘了 —— 早失败才不会留下半装状态（见 ExtractGcuStagingTemporaries）。
  Result := ExtractGcuStagingTemporaries();
  if Result <> '' then
    Exit;
  // 1) 先可靠停服 + 杀进程 + 等它们真的退出，解除 {app} 下的文件占用（失败不致命，只记日志）。
  //    2026-10-08：原来只发一次 taskkill，服务/进程可能仍在（或被 SCM 拉起），
  //    会把 {app}\GCU\AiStoneService 的文件锁到 Install-Gcu.ps1 复制阶段。
  StopGcuProcessesForUpgrade('写入前(PrepareToInstall)', GcuTarget + '\AiStoneService');
end;

function NeedRestart(): Boolean;
begin
  Result := True;
end;
