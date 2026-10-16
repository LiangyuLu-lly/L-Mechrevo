# -*- coding: utf-8 -*-
"""追加 2026-10-05 18:30 的修改日志（UTF-8 追加，不覆盖）。"""
import io
import os

LOG = r'D:\openLM\修改日志.txt'

ENTRY = r"""[2026-10-05 18:30] 修复打包后运行时报错：InitializeSetup 里不能展开 {app}
================================================================================
（标题时间按用户指定写 18:30；产物文件时间戳为本机时钟 2026-10-05 17:02:57 / 17:03:04）

【问题（用户实测）】
运行 installer-src\Output\LM-0.290.7-beta21.7-setup.exe（16:52:27 那一版）弹窗：
    Runtime error (at 24:81):
    内部错误：An attempt was made to expand the "app" constant before it was initialized.
用户侧 setup 日志（%TEMP%\Setup Log 2026-10-05 #003.txt）实测记录：
    16:57:00.889  Install mode root key: HKEY_LOCAL_MACHINE / 64-bit install mode: Yes /
                  User privileges: Administrative
    16:57:00.898  InitializeSetup raised an exception (fatal).
    16:57:00.898  Message box (OK): Runtime error (at 24:81):
                  内部错误：An attempt was made to expand the "app" constant before it was initialized.

【根因】
今天 15:35~16:52 之间加的「全新安装清用户数据」逻辑，在 InitializeSetup() 第一行用了
    GFreshInstall := not FileExists(ExpandConstant('{app}\L-Mechrevo.exe'));
{app} 是「向导选择目录」派生出来的常量：InitializeSetup 跑在向导之前、用户还没选目录，
WizardDirValue 为空 -> Inno 抛内部错误（Setup.e32 字符串表里 'app' 常量旁边就是那条
 '" constant before it was initialized'）。所以这不是逻辑分支问题，而是「过早展开路径常量」
的硬错误，任何机器、任何状态都必现（用户 2 秒内就撞上）。

【修复 1/2：installer-src\install_script.iss —— 判定改为读卸载注册项，完全不依赖路径常量】
新增函数（放在已有常量 LmAppIdKey 之后、InitializeSetup 之前；Pascal 先声明后使用）：
  // 判定"是否已安装过 L-Mechrevo"：读卸载注册项（不依赖任何路径常量）。
  function IsLmAlreadyInstalled(): Boolean;
  begin
    Result :=
      RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey) or
      RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey) or
      RegKeyExists(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + LmAppIdKey);
  end;
InitializeSetup 开头改为：
  // 2026-10-05：旧版卸载项存在 = 覆盖升级 -> 保留用户数据；不存在 = 全新安装 -> 清残留使首次引导重现
  // 不能用 ExpandConstant('{app}...')：此刻 {app} 还没初始化，会抛内部错误。
  GFreshInstall := not IsLmAlreadyInstalled();
AppId 用法：**复用文件里原有的常量** LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1'
（与 [Setup] AppId={{8F4E2C71-...} 对应），没有硬编码重复字符串，写法与既有 GetLmUninstallString() 一致。

【修复 2/2：installer-src\install_script_nogcu.iss】
该文件原本没有 LmAppIdKey。先读它的 [Setup] 段：AppId={{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}
（Inno 里 {{ 是转义，实际 AppId = {8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}）—— 与主 iss **完全相同**，
所以卸载项键也完全相同。为免硬编码，在它自己的 const 段加了同名常量：
    LmAppIdKey = '{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}_is1';   // 与主 iss 相同
新函数与 InitializeSetup 开头两行与主 iss 逐字一致。

【顺手修掉一个 NoGCU 的编译错误（今天就存在，只是从没打过包所以没暴露）】
对比 backup\install_script_nogcu.iss_20261005_pre_userdata.iss L182 可见，今天更早的用户数据补丁
把 InitializeSetup 的**函数头吃掉了**：
    改前：function InitializeSetup(): Boolean;  begin ... end;
    改后：begin ... end;                        <- 裸块，里面却引用 Result
ISCC 报：Error on line 224 Column 3: Unknown identifier 'Result'
（Output 里从来没有 LM-0.290.7-beta21.7-NoGCU-setup.exe，正是因为它一直编译不过。）
本次把 `function InitializeSetup(): Boolean;` 补回、结构对齐主 iss -> 编译通过（见下）。

【[Code] 早期路径常量隐患排查（要求②：会不会有第二处同样异常）】
做法：写了一个**只读探针** setup（installer-src\_probe\probe_const.iss）：在真实的 InitializeSetup
时刻逐个展开候选常量并把结果写盘，InitializeSetup 直接 return False
（**不安装 / 不写注册表 / 不建卸载项 / 不复制文件**）。实测（_probe\probe_result.txt）全部成功：
    commonpf    = C:\Program Files
    commonpf64  = C:\Program Files
    commonpf32  = C:\Program Files (x86)
    autopf      = C:\Users\xiaohan\AppData\Local\Programs  （探针用 lowest 权限免 UAC；
                                                            实机 admin 模式下 autopf = commonpf）
    tmp         = ...\Temp\is-XXXX.tmp                     （Setup 启动时就创建，安全）
    userappdata = C:\Users\xiaohan\AppData\Roaming
    sys         = C:\Windows\system32
    cmd         = C:\Windows\system32\cmd.exe
    log         = []                                       （未开 SetupLogging 时为空串，正常）
    srcexe      = <探针自身路径>
    cm:         = Launch L-Mechrevo                        （{cm:...} 可用）
逐条结论：
 1) IsNetDesktop10Installed() 的 {commonpf64} / {commonpf} —— **可用，不是隐患**（实测展开成功）
 2) InstallNet10() 的 {tmp} —— 可用
 3) UninstallOfficialConsole() 的 {cmd} —— 可用
 4) ExpandMsg() 的 {cm:...} —— 可用
 5) 其余 {app} 使用点全在向导之后，安全：GcuUserRoot 常量、BackupAndUpgrade()（ssInstall，
    含 '{app}\customICC'）、CurStepChanged ssPostInstall（{tmp}、{app}\install.log、{log}）、
    PrepareToInstall（{app}\GCU）—— 本轮未改动
 6) CurUninstallStepChanged(usPostUninstall) 的 {userappdata}\MechrevoLite —— 按约定保持不动
    （usPostUninstall 时该常量可用）
 7) [Run] 那条 PowerShell 的 $env:APPDATA 由 PowerShell 运行时自己求值，不经过 Inno 常量展开，未改
=> 两个 iss 的 [Code] 里**没有第二处**会抛同样异常的地方。

【关键正确性验证：新判定在本机（已装 0.290.6）真的判成"升级"（只读，不跑安装包）】
注册表事实（reg query，只读）：卸载项**只在 64 位视图**存在：
    HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4E...}_is1
      DisplayName     = L-Mechrevo 0.290.6
      DisplayVersion  = 0.290.6
      UninstallString = "C:\Program Files\L-Mechrevo\unins000.exe"
    WOW6432Node 视图、HKCU —— 均不存在。
于是第二个只读探针（_probe\probe_reg.iss）在 InitializeSetup 时刻跑**新函数本体**
（结果 _probe\probe_reg_result.txt）：
    term1 RegKeyExists(HKLM, …\Uninstall\<key>)          = True   <- 64 位安装模式下 HKLM = 64 位视图
    term2 RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\…')   = False
    term3 RegKeyExists(HKCU, …\Uninstall\<key>)          = False
    IsLmAlreadyInstalled()                               = True
    => GFreshInstall = False   （判为覆盖升级 -> **保留用户数据**）✅
    对照：HKLM64=True / HKLM32=False / HKCU64=False / HKCU32=False；
          RegQueryStringValue(HKLM, …\Uninstall\<key>, 'UninstallString') 取到 unins000.exe
          （即既有 GetLmUninstallString() 的升级路径也正常）
这一步不安装、不写盘，只用真实注册表状态证明「升级不会被误判成全新安装」，
即**不会误清 %APPDATA%\MechrevoLite**。

【打包与产物】
1) 源码：& 'D:\openLM\dotnet-sdk\dotnet.exe' build source\L-Mechrevo.csproj -c Release
   -> 已成功生成 / 0 个警告 / 0 个错误（源码未改，与上版一致）
2) 打包（本次**未跑** merge_and_clean.py：源码未变，installer-src\files 无需重合并；ISCC 也未报缺文件）：
   & ISCC install_script.iss        -> Compiling [Code] section / Successful compile (14.765 sec)
   & ISCC install_script_nogcu.iss  -> Compiling [Code] section / Successful compile (6.875 sec)
   两个日志（_probe\iscc_main.txt / iscc_nogcu.txt）逐行扫描：**无任何 Warning / Error**
   ⚠️ 第一次打主包时 ISCC 报 Error 32（Could not copy SetupLdr.e32 -> Output\…exe，文件被占用）：
      原因是用户 16:57 那次失败的安装程序还停在错误对话框上（4 个进程，含
      %TEMP%\is-*.tmp\LM-0.290.7-beta21.7-setup.tmp，窗口标题「错误」），原 exe 被锁。
      等我去处理时这些进程已自行退出（用户点了确定）-> **我没有杀任何进程**；随后重打正常。
3) 产物（新时间戳；同名旧产物已被覆盖删除，日志里有 "Deleting … from output directory"）：
   a. installer-src\Output\LM-0.290.7-beta21.7-setup.exe
      52,111,152 B (49.70 MB)   2026-10-05 17:02:57   （上一版 52,111,094 B / 16:52:27，+58 B）
      SHA256 = 32269E7D7A3FFE29FA7D3C8831E14D34B91F95B253EC0D1A7733806483B8F4EF
      FileVersionInfo = 0.290.7 / ProductVersion 0.290.7 / "L-Mechrevo Setup"
   b. installer-src\Output\LM-0.290.7-beta21.7-NoGCU-setup.exe（本次顺带补打，以前一直缺）
      18,121,789 B (17.28 MB)   2026-10-05 17:03:04
      SHA256 = FC08FA2789E842C1055595D7DF08DC496174C776500D517FD56A8A9BAA81AB32
      FileVersionInfo = 0.290.7

【只读验证「修复确实进了包」（innounp 2.67.11，未运行安装包）】
 1) innounp -t：405 个条目全部 "checked"（含 embedded\CompiledCode.bin、install_script.iss）
 2) innounp -x -m 解出 embedded\CompiledCode.bin（14,529 B；Inno 编译后的 Pascal 代码 + UTF-16 字符串表）：
      * 含 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4E…}_is1'
      * 含 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{8F4E…}_is1'
        （字节级命中 "WOW6432Node" 3 处 -> 新函数第 2 项确实编进去了）
      * 含 '{userappdata}\MechrevoLite'
      * **'{app}\L-Mechrevo.exe' 命中 0 次** —— 出问题的那条字面量已从编译产物中彻底消失 ✅
        （唯一出现 'L-Mechrevo.exe' 的 2 处是升级路径的 '/c taskkill /f /im L-Mechrevo.exe'
          和日志文案 'LM-UPDATE: L-Mechrevo.exe 已尝试结束 (exit='，都只在 ssInstall 升级分支执行）
      * 另有 '{app}\GCU'（3 处）、'{app}\install.log'、'{log}'、'{cm:'（均在向导之后执行）
        —— 这些同时充当"扫描方法的阳性对照"：同样的方式能命中 {app}\ 前缀字面量
 3) 解出的内部 install_script.iss 的 [Run] 原样保留：
      Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe";
      Parameters: "… Join-Path $env:APPDATA MechrevoLite …"; Check: "IsFreshInstallFlag";
    （Check 引用仍在 -> IsFreshInstallFlag 也被编进包；$env:APPDATA 未动）
 4) NoGCU 产物同样解包比对（_probe\unp_nogcu）：含两个 Uninstall\{8F4E…}_is1 字面量、
    含 '{userappdata}\MechrevoLite'、0 处 '{app}\L-Mechrevo.exe'、[Run] 的
    Check: "IsFreshInstallFlag" 与 $env:APPDATA 原样保留。

【未做 / 未确定项】
 1) **没有运行、没有安装**新产出的 setup.exe；**没有触碰** %APPDATA%\MechrevoLite\；
    **没有动** C:\Program Files\L-Mechrevo\。本轮只有读取动作：reg query、innounp 解包、
    两个 InitializeSetup 里 return False 的只读探针（自身不安装）。
 2) 只读探针证明的是「新判定在本机注册表状态下 = True（升级）」。**全新安装分支**
    （卸载项不存在 -> GFreshInstall=True -> 清 %APPDATA%\MechrevoLite -> 首次引导重现）
    仍未实机验证；该分支一旦命中就会清用户数据，按约束不能实测，等实机升级/装机时观察。
 3) 探针用 PrivilegesRequired=lowest（免 UAC）。已验证 64 位安装模式下 RegKeyExists(HKLM, …)
    读的是 64 位视图（HKLM64=True / HKLM32=False），与用户侧日志
    "64-bit install mode: Yes / root key HKEY_LOCAL_MACHINE" 一致。
 4) 旧产物（16:52 那版）已被同名覆盖删除，未留备份；出问题的源码状态（15:35~16:52 之间）
    也没有留 before/after 对照 exe。
 5) 证据与探针都留在 installer-src\_probe\（probe_*.iss、probe_*_result.txt、iscc_*.txt、
    unp_main\、unp_nogcu\），未删；两个探针 exe（const-probe.exe / reg-probe.exe）已删除，
    避免在工作区留下多余安装器。
 6) 用户那次失败运行留下的 %TEMP%\is-*.tmp（16:57 三个）与 Setup Log 2026-10-05 #003.txt
    **未删除**（保留现场）。
================================================================================
"""


def main():
    with open(LOG, 'rb') as f:
        before = f.read()
    assert b'\r' not in before, '意外：日志里存在 CR，行尾约定需要复核'
    with io.open(LOG, 'a', encoding='utf-8', newline='\n') as f:
        f.write(ENTRY)
    with open(LOG, 'rb') as f:
        after = f.read()
    added = len(after) - len(before)
    assert after[:len(before)] == before, '失败：既有内容被改动！'
    assert after[len(before):] == ENTRY.encode('utf-8'), '失败：追加内容与预期不一致！'
    print('OK  文件: %s' % LOG)
    print('追加前 %d B -> 追加后 %d B (+%d B, %d 字符)'
          % (len(before), len(after), added, len(ENTRY)))
    print('既有字节逐字节未变: 通过')


if __name__ == '__main__':
    main()
