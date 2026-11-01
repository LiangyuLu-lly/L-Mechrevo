# -*- coding: utf-8 -*-
r"""
在 D:\openLM\修改日志.txt 末尾**追加**本次记录（utf-8 / LF / 不覆盖）。
先备份到 backup\修改日志_20261005_pre_02907.txt，并断言追加后前缀逐字节未变。
"""
import io
import os
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LOG = r'D:\openLM\修改日志.txt'
BAK = r'D:\openLM\backup\修改日志_20261005_pre_02907.txt'

ENTRY = u"""[2026-10-05 18:00] 升版本 0.290.7 beta21.7 并打包
================================================================================
用户两项任务：①版本号 0.290.3 beta21.3 -> 0.290.7 beta21.7 ②打包出安装程序 exe

【① 版本号改了哪些文件（全仓库检索 0.290.3 / beta21.3 后确定）】
改前先备份到 backup\\：
  AssemblyInfo_20261005_pre_02907.cs
  install_script.iss.bak-20261005_1800
  install_script_nogcu.iss.bak-20261005_1800
  更新日志_20261005_pre_02907.txt

1) source\\Properties\\AssemblyInfo.cs（utf-8 无 BOM / LF）—— 3 处
   [assembly: AssemblyFileVersion("0.290.3")]                  -> ("0.290.7")
   [assembly: AssemblyInformationalVersion("0.290.3-beta21.3")] -> ("0.290.7-beta21.7")
   [assembly: AssemblyVersion("0.290.3")]                      -> ("0.290.7")
2) installer-src\\install_script.iss（utf-8 BOM / CRLF）—— 6 处
   L7  AppVerName=L-Mechrevo 0.290.3 beta21.3            -> 0.290.7 beta21.7
   L9  AppVersion=0.290.3                                -> 0.290.7
   L14 OutputBaseFilename=LM-0.290.3-beta21.3-setup      -> LM-0.290.7-beta21.7-setup
   L17 VersionInfoVersion=0.290.3                        -> 0.290.7
   L18 VersionInfoProductVersion=0.290.3                 -> 0.290.7
   L19 UninstallDisplayName=L-Mechrevo 0.290.3 beta21.3  -> 0.290.7 beta21.7
3) installer-src\\install_script_nogcu.iss（utf-8 BOM / CRLF）—— 6 处（同上，带 (NoGCU) 后缀）
合计 22 处替换。脚本：tools\\bump_version_2907.py（沿用 tools\\bump_version_2903.py 的做法：
先改 beta 标签再改数字版本、按原编码/newline 原样写回、改后复读计数核验）
diff 核验：两个 iss 与各自备份的差异**恰好各 6 行**（Compare-Object 各 12 条记录），
          除版本行外无任何其他改动 ✅
（复核：两个 iss 里的用户数据逻辑标记各 8 处，改前改后一致，未被破坏 ✅）

【未改（有意保留）】
- source\\obj\\**\\L-Mechrevo.resources.cs —— 编译中间产物，构建时重新生成
  （改后已自动变为 0.290.7；不该也不必要手改）
- installer-src\\files\\更新日志.txt L507/L539 —— 十五段里的 "0.290.3 beta21.3" 是**历史记录**
- 修改日志.txt / 交接文档.md / 待办.txt —— 历史记录，不改
- installer-src\\install_script*.iss 的 `-InstallerVersion "18.3"`（主 L444 / nogcu L73）
  —— 是传给 GCU 安装脚本的入参，**不是** 0.290.x 体系（当年 set_version_183.py 特意
     把它从 0.290.3 改成 18.3），本次沿用不动，与 0.290.3 那次一致；⚠️ 见"未确定项 1"
- installer-src\\files\\GCU\\** —— 我们的 GCU payload 分流，绝对不动（见【⑤】）
- _upstream\\ / _checkpoints\\ / backup\\ 下的产物 —— 未动（backup\\ 只**新增**备份文件）

【② 打包：复用的既有方法（命令原样贴出）】
1) 编译（0 错误）：
   & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
   -> 已成功生成 / **1197 个警告 / 0 个错误**（警告数与 17:30 合并①~⑥后的基线完全一致）
   输出存档：tools\\build_02907.txt
2) publish 到既有目录 rebuild\\lm_fix_build（参数 = tools\\publish_lm.rsp 的内容）：
   & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' publish 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release `
       -o 'D:\\openLM\\rebuild\\lm_fix_build' '-p:NoWarn=WFO1000%3BCA1416'
   ⚠️ **教训**：`dotnet publish @D:\\openLM\\tools\\publish_lm.rsp` 在本 SDK（10.0.401）上**失败**：
      rsp 里是空格分隔的 `-c Release`，MSBuild 只认 `-c:Release`（MSB1001 未知开关）；
      且 `-p:NoWarn=WFO1000;CA1416` 的分号会被 MSBuild 当成两个属性（MSB1006 属性无效:CA1416），
      加引号也无效，必须用 MSBuild 转义 `%3B`。故改为**把 rsp 内容展开成显式参数**调用。
      输出存档：tools\\publish_02907.txt
   -> 产物：rebuild\\lm_fix_build\\L-Mechrevo.dll 1,946,624 B / FileVer=0.290.7 /
      ProdVer=0.290.7-beta21.7；L-Mechrevo.exe 170,496 B 同样 0.290.7
3) 合并进 installer-src\\files（**沿用既有脚本** tools\\merge_and_clean.py）：
   py -3 'D:\\openLM\\tools\\merge_and_clean.py'            # 先 dry-run
   py -3 'D:\\openLM\\tools\\merge_and_clean.py' --apply
   -> 更新 3 个：L-Mechrevo.dll 1890.0 -> 1901.0 KB、L-Mechrevo.exe（166.5 KB，内容变了）、
      zh-CN\\L-Mechrevo.resources.dll 54.0 -> 55.0 KB；
      未变 3 个：deps.json / runtimeconfig.json / dll.config（逐字节相同，"= 无变化"）
      脚本 ① 段列的 12 个冗余项早已移走（打印"已不存在"），本次**未删除任何东西**
      （仍创建了一个空的 backup\\files_clean_20261005_165135\\ 目录，无内容）
   -> 合并后核验 installer-src\\files\\L-Mechrevo.dll FileVer=0.290.7 / ProdVer=0.290.7-beta21.7 ✅
4) ISCC 打包（复用系统既有的 Inno Setup 6）：
   & 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe' 'D:\\openLM\\installer-src\\install_script.iss'
   -> **Successful compile (18.984 sec)** / **0 个 Warning / 0 个 Error**
   -> 编译成功同时证明「13:25 新加的 [Code] Pascal 逻辑（GFreshInstall / CurUninstallStepChanged /
      UninstallSilent / DelTree / IsFreshInstallFlag）语法正确」——
      这正是 13:25 那条日志特意留下的待验证项（"Inno 的 Pascal 语法错误只会在编译时暴露"）✅
   输出存档：tools\\iscc_02907.txt

【③ 产物】
路径：D:\\openLM\\installer-src\\Output\\LM-0.290.7-beta21.7-setup.exe
大小：52,111,094 字节 = **49.70 MB**
      （0.290.3 = 52,105,661 / 49.69 MB；+5,433 B。
        未压缩增量：主 DLL +11,264 B、zh-CN 卫星 +1,024 B、更新日志 +3,901 B，共 +16,189 B，
        经 LZMA 压缩后净增 5,433 B，量级吻合）
时间：2026-10-05 16:52:27
校验：
  [System.Diagnostics.FileVersionInfo]::GetVersionInfo('D:\\openLM\\installer-src\\Output\\LM-0.290.7-beta21.7-setup.exe')
    -> FileVersion      = **0.290.7**
       ProductVersion   = **0.290.7**
       ProductName      = L-Mechrevo
       FileDescription  = L-Mechrevo Setup
  SHA256 = BEDC8017607E901F2D30C2BC9FBD0E0980FED992F23E051512CD9A45DBCE6773
**未运行、未安装**该 exe；**未触碰** C:\\Program Files\\L-Mechrevo\\ ✅

【④ 包内容反查（innounp 只读解包到临时目录，验完即删）】
  & 'D:\\openLM\\innounp-267\\innounp.exe' -x -q -d<临时目录> <setup.exe>
  -> {app}\\L-Mechrevo.dll   1,946,624 B  FileVer=0.290.7  ProdVer=0.290.7-beta21.7
     {app}\\L-Mechrevo.exe     170,496 B  FileVer=0.290.7  ProdVer=0.290.7-beta21.7
     {app}\\zh-CN\\L-Mechrevo.resources.dll  56,320 B  FileVer=0.290.7
     {app}\\更新日志.txt        37,114 B（含新段"十六、0.290.7 beta21.7"）
  -> 与 installer-src\\files 逐文件 SHA256 **一致** ✅
     （L-Mechrevo.dll DCC9CC9B… / exe 0A1735F7… / 更新日志 1706F420… /
       GCU\\Select-GcuPayload.ps1 B51CFEFC… / payload\\40 的 GCUService.exe A2B7D81E…）
  -> 反查出的 iss 里 [Run] 那条 PowerShell 带 `Check: "IsFreshInstallFlag"` ✅
     （注：innounp 无法还原 [Code] 段的 Pascal 源码文本，故 GFreshInstall / DelTree 等
      标识符搜不到 —— 属**工具限制**，非缺失；证据是上面那条 Check 与 0 错误编译）
  -> 临时目录 D:\\openLM\\tmp_verify_02907 已删除 ✅

【⑤ 我们的 GCU payload 分流**未被改动**（证据）】
  installer-src\\files\\GCU\\Select-GcuPayload.ps1
    改前：17,405 B / SHA256 前16位 B51CFEFC5F0DF79C
    改后：**17,405 B / SHA256 前16位 B51CFEFC5F0DF79C** ✅ 完全一致
      完整 SHA256 = B51CFEFC5F0DF79C5A9D43C8533A6C721A3BC13B0B1495B3C42563BB50558EBF
    包内 {app}\\GCU\\Select-GcuPayload.ps1 同样 B51CFEFC5F0DF79C ✅
  installer-src\\files\\GCU\\payload\\40：4 文件 / 16.55 MB
    （README.txt 1,485 B、AiStoneService\\MyControlCenter\\GCUService.exe 14,447,864 B、
      NVControlSetting.dll 2,775,208 B、UEFI_Firmware.dll 134,312 B）—— 改前改后一致 ✅
    （包内只有 3 个文件：README.txt 是维护说明且 .iss 不引用它，与 0.290.3 行为相同）
  payload 30/40/50/GCU 统计改前改后完全一致：
    30 = 130 文件 / 14.83 MB ；40 = 4 文件 / 16.55 MB ；
    50 = 3 文件 / 19.15 MB  ；GCU = 247 文件 / 34.35 MB ✅
  —— 作者 0.290.6 那份 **15,861 B** 的 Select-GcuPayload.ps1（在 _upstream\\ 下）**从未被复制进来** ✅

【⑥ 更新日志追加】
文件：installer-src\\files\\更新日志.txt（**打包用的就是这一份**，.iss L63 引用它；
      根目录 D:\\openLM\\更新日志.txt 是 10-03 的旧副本，本次未动）
追加前 33,213 B -> 追加后 **37,114 B**（+3,901）；断言既有字节**逐字节未变**（前缀比对通过）✅
备份：backup\\更新日志_20261005_pre_02907.txt
脚本：tools\\append_changelog_02907.py
新增「十六、0.290.7 beta21.7（2026-10-05）」段，5 个小节（用户视角、不贴代码，
排版沿用上一段的 44 个 U+2500 分隔线 + 中文序号）：
  一、核显模式改为「问一次 + 设置里可随时改」：40 系无可靠判据 ->
      首次安装 / 从没问过的旧版本升级时多问一步（有 / 没有 / 不确定，
      不确定或关窗保持显示不误藏）；设置对话框新增「显示核显模式」开关，
      切换后主界面显卡区与托盘菜单立即刷新；50 系与 30 系仍按各自规则自动判定、不受打扰
  二、显卡模式显示与提示修复：解析不再借用「核显省电」字段（修 50 系把核显省电设为
      "自动"时的三档错乱与凭空出现的"自动"模式）、「重启以应用」高亮不再挂错按钮、
      不再弹两个相同的重启提示
  三、显卡模式精简：三档（集显/混合/独显直连）切换路径彻底移除核显省电指令
      （核显省电有独立入口，功能与界面完整保留）、删除「自动切换显卡模式」(AUTO) 死功能及残留文案
  四、合并上游 0.290.6：PCI/SMU 跨进程互斥 + 更严格的初始化与返回值校验、
      独立「错误记录环」、断线退避（0.25 s 起步、最长 30 s）与日志降噪
      （蓝牙关闭不再每 10 s 写日志、探测降到 30 s 一次）、写配置失败可观测、
      「已保存计划不可用」文案、经典电源计划不再被强行塞进列表、
      性能模式快速连切只认最后一次、高 DPI（100/150/200%）与放大字体下界面不裁切
  五、安装器：手动卸载清空用户数据（卸载后重装会再次出现首次引导）、覆盖升级保留设置、
      GCU 分发包选择与载荷精简策略不变

【⑦ NoGCU 变体：**本次未打**】
- 依据：上一次（0.290.3）用户明确要求「NOGCU 不打包」，当时误产出的
  LM-0.290.3-beta21.3-NoGCU-setup.exe 已被移到 backup\\；本次沿用该决定。
- 但 install_script_nogcu.iss 的 6 个版本行**已一并升到 0.290.7 / beta21.7**（与上次做法一致），
  以后要打时直接：
    & 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe' 'D:\\openLM\\installer-src\\install_script_nogcu.iss'
  -> 会产出 installer-src\\Output\\LM-0.290.7-beta21.7-NoGCU-setup.exe（约 19 秒 / 约 18 MB）
- 若需要现在补打，说一声即可。

【未确定项 / 未做项】
1) iss 里 `-InstallerVersion "18.3"`（主 iss L444 / nogcu iss L73）**未改**。
   它不是 0.290.x 体系，而是传给 GCU 安装脚本 Install-Gcu.ps1 的入参；
   当年 set_version_183.py 特意把它从 0.290.3 改成 18.3，故本次沿用不动。
   **如果它其实应该跟随 LM 版本号，需要单独确认**（改它会进入 GCU 安装脚本的版本判断路径）。
2) 产物 exe **未运行、未安装、未实测**（按约束只打包）。
   「卸载全删 / 升级保留 / 全新安装重现首次引导」的实机行为仍未验证 ——
   本次只证明了它**编译通过且被编进安装包**（[Run] 的 Check: IsFreshInstallFlag + 0 错误）。
3) 「核显模式显示策略」的实机观感（问法、单问对话框版面、设置开关位置）**未目视验收**。
4) 合并①~⑥带来的行为变化（SMU HasCompleteOutput 变严格、断线退避最长 30 s、
   模式切换"最后一次为准"、配置写盘返回值等）**未实机回归** ——
   清单见 17:30 那条日志的"下一步（留给主代理）"。
5) 根目录 D:\\openLM\\更新日志.txt（10-03 旧副本）与 待办.txt 里
   「[版本号 0.290.3-beta21.3]（已改、未打包）」**都已过时**，本次未动（不改历史记录）。
6) rebuild\\lm_fix_build\\L-Mechrevo.pdb 已更新（16:51），但 installer-src\\files\\L-Mechrevo.pdb
   仍是 10-04 12:20 的旧文件；merge_and_clean.py 的 WANT 列表里没有 pdb，
   **.iss 也不引用 pdb**（grep 全 iss 无 pdb 行）-> 不进安装包，无影响（与 0.290.3 一致）。
7) 本次**未跑**替换LM.ps1、未部署到安装目录、未动 installer-src 下的 [Files] 清单。

【本次任务完成情况】
① 版本号 0.290.7 / beta21.7 —— 已改（3 个文件 22 处）✅
② 安装程序 —— 已打包 installer-src\\Output\\LM-0.290.7-beta21.7-setup.exe
   （49.70 MB / FileVersion 0.290.7 / SHA256 BEDC8017…）✅
③ 更新日志 —— 已追加「十六、0.290.7 beta21.7」段（+3,901 B）✅
================================================================================
"""


def main():
    raw = io.open(LOG, 'rb').read()
    text = raw.decode('utf-8')
    assert text.endswith('\n'), '修改日志.txt 结尾异常（不是 LF 结尾）'
    assert u'[2026-10-05 18:00] 升版本 0.290.7 beta21.7 并打包' not in text, '该条目已存在，勿重复追加'
    shutil.copy2(LOG, BAK)

    new = text + '\n' + ENTRY
    io.open(LOG, 'w', encoding='utf-8', newline='').write(new)

    back = io.open(LOG, 'rb').read()
    assert back[:len(raw)] == raw, '前缀被改动！'
    body = back.decode('utf-8')
    print('备份        : %s' % BAK)
    print('追加前      : %d 字节' % len(raw))
    print('追加后      : %d 字节（+%d）' % (len(back), len(back) - len(raw)))
    print('标题已追加  : %s' % (u'[2026-10-05 18:00] 升版本 0.290.7 beta21.7 并打包' in body))
    print('前缀未变    : %s' % (back[:len(raw)] == raw))
    print('总行数      : %d' % body.count('\n'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
