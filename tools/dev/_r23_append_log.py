# -*- coding: utf-8 -*-
r"""r23（0.291.7.6）把本轮小节【字节级追加到 修改日志.txt 顶部】（倒叙）。
   ⛔ 不用 Add-Content；用 AppendAllText 等价物：读全文 -> 断言 -> 新块 + 旧文 -> 写 TEMP -> 校验 -> 原子替换。
   ⚠️ 原文件是混合行尾（CRLF 11979 / 单独 LF 66）——不规范化、不重写既有部分，只在其前面拼新块。"""
import hashlib, os, shutil

P = r'D:\openLM\修改日志.txt'
BAK = r'D:\openLM\backup\repack\2026-10\20261013_2115_pre_repack_beta2276\version_files\修改日志.txt'
os.makedirs(os.path.dirname(BAK), exist_ok=True)

RAWT = open(P, 'rb').read()
OLD = RAWT.decode('utf-8')
assert OLD.startswith('\r\n===='), 'unexpected head'
assert 'r23（2026-10-13）' not in OLD, 'already prepended'
assert '点下载没拉起浏览器' not in OLD, 'already prepended'
shutil.copy2(P, BAK)

BLOCK = '''\r
================================================================================\r
r23（2026-10-13）打包 0.291.7.6 / beta22.7.6（修「点下载没拉起浏览器」）\r
================================================================================\r
【用户原话】\r
  ·「点下载没拉起浏览器」—— 点「下载」之后浏览器没被拉起来（安装器的「下载 / Download」提示框，\r
    以及启动壳缺 .NET 10 时的「打开下载」按钮，两条路都可能）。\r
\r
【根因（⭐ 架构判断 + 本轮逐条核实）】\r
  · 【两条链都跑在提权（管理员）进程里】：\r
      安装器 —— Inno 默认提权；启动壳 —— tools\\LmLauncherStub\\app.manifest:11 = requireAdministrator。\r
  · 原来的兜底链【缺了真正能降权的那一档】：\r
      installer-src\\install_script.iss 旧 OpenNet10DownloadPage() 只有两档\r
        ShellExecAsOriginalUser -> ShellExec；\r
      后者是【以管理员身份】跑的 —— 管理员上下文里常常没有浏览器关联，\r
      一旦第 1 档失败（初始 Setup 进程已退出等），第 2 档基本【静默失败】\r
      => 用户看到的就是「点了没反应」。\r
  · 启动壳旧 TryOpenBrowser() 只有一句 Process.Start(new ProcessStartInfo(url){UseShellExecute=true})：\r
      提权进程里让 shell 打开 URL，常常【不抛异常也不报错、就是什么都不发生】\r
      => 原来的 catch 兜底根本不会被触发，返回值也是无条件的 true（假成功）。\r
  · ⭐⭐ 而且【整条链是静默失败】：安装器只在日志里留一行、用户什么都看不到；\r
      启动壳无论成败都显示同一句「已尝试为你打开浏览器」（假成功）。\r
\r
【修法（降权交给 shell 用户：「打开网址」不再由管理员进程直接做）】\r
  · 安装器 installer-src\\install_script.iss（现 :795-853 OpenNet10DownloadPage）改成三档兜底，\r
    ⭐【每一档都写日志 + ⭐ 都检查返回值】：\r
      第1档 ShellExecAsOriginalUser('open', 直链, …)  —— 以【原登录用户】身份（最理想）\r
      第2档 ★ 新增 ShellExec('open', '{sys}\\explorer.exe', '"' + 直链 + '"', …) —— \r
            explorer 由 shell 用户会话代理执行 => 浏览器以【普通用户】身份打开（本轮修的就是这一档）\r
      第3档 ShellExec('open', 直链, …) —— 提权身份兜底（多数打不开，只当最后一丝机会）\r
    三档全失败 => 返回 False；HandleNet10MissingAnswer 里【补一条可见提示】\r
      （MsgBox：「自动打开浏览器失败…地址【已复制到剪贴板】，请手动粘贴到浏览器地址栏打开」；\r
        ⚠️ 静默安装下不弹：先判 WizardSilent()）。\r
    ⚠️ 顺带把「怎么看安装日志」写进 iss 注释：SetupLogging=yes => %TEMP%\\Setup Log*.txt，\r
       所有 LM-NET / LM-NET-BROWSER 行都在里面。\r
  · 启动壳 tools\\LmLauncherStub\\LmLauncherStub.cs（新增 :516-… internal static class UrlOpener）改成两档：\r
      第1档 ★ explorer.exe "<url>"（explorer.exe 取 Environment.SpecialFolder.System；\r
            ⚠️ 参数用双引号包住，并把 URL 里的引号剔除防参数截断）\r
      第2档 旧路径 Process.Start(UseShellExecute=true) 兜底\r
    ⚠️ 每一档【各自 try/catch】：某一档抛异常不会把后面那档也带走（harness CASE D 抓到的漏洞，已修）；\r
    ⚠️ 打开网址写日志 %TEMP%\\L-Mechrevo-Launcher.log；\r
    ⚠️ 两档全失败 => TryOpen 返回 false => 提示框 status 文案改为可见失败提示\r
      「自动打开浏览器没成功：网址已可一键复制 —— 点「复制地址」，再粘到浏览器地址栏打开即可下载。」\r
    ⚠️ 顺带修掉旧写法的提权句柄泄漏（Process 对象从不解引用）。\r
\r
【验证（⛔ 不能真跑安装器/启动壳）】\r
  · 安装器链 —— installer-src\\_probe\\probe_browserchain.iss：把生产 OpenNet10DownloadPage() 的\r
    【逐字副本】（3381 B，与生产函数 SHA 逐字节相同，已用脚本断言 IDENTICAL）在影子\r
    ShellExec/ShellExecAsOriginalUser 下跑 4 组注入结果，**24/24 PASS**（结果文件\r
    installer-src\\_probe\\probe_browserchain_result.txt）：\r
      A 第1档成功 => 立即 Exit，⛔ 不碰第2/3档；B 第1档失败 => 第2档 explorer 被调用且参数 ==\r
      "带引号的直链"、成功即 Exit；C 第1档抛异常 => 被 except 吃掉并继续第2档；\r
      D 三档全失败 => 返回 False（=> 调用方弹可见提示）+ 三档 errcode 都被读取记录。\r
    ⚠️ 影子声明的可行性先由 installer-src\\_probe\\probe_shadow_probe.iss 单独证明（否则探针不成立）。\r
    ⚠️ 探针编译/运行产物一律落 %TEMP%，跑完即删（⛔ 不留裸 exe）。\r
  · 启动壳链 —— tools\\LmStubUrlHarness\\（新建 csproj + Program.cs；反射加载【真正打进包的那个壳】\r
    L-Mechrevo-Launcher.exe + 换掉 TestHooks.TryRun 替身），**22/22 PASS**：\r
      A 第1档 = System 目录的 explorer.exe、参数 == "直链"、成功即停；\r
      B 第1档失败 => 退到 UseShellExecute 兜底（fileName == null / 裸直链）；\r
      C 两档全失败 => 返回 false；D 第1档抛异常 => 吞掉并继续第2档。\r
    ⚠️ TestHooks 是壳里的 internal 钩子，只在 harness 里被替换；生产路径永远走真实 RunDetached。\r
  · ⛔ 没真机就验不了的（如实列）：① 浏览器到底会不会真的弹出来（explorer 转交之后）；\r
    ② 提权/非提权、UIPI、默认浏览器关联在不同机器上的差异；③ 真实 UAC 提权后的行为；\r
    ④ 安装器弹框三颗按钮的真实点击；⑤ 剪贴板在真实用户会话里的可粘贴性。\r
\r
【版本号（每处改前实测行尾/BOM -> 只做唯一锚点定点替换，写 TEMP -> 校验 -> 替换）】\r
  · source\\Properties\\AssemblyInfo.cs（noBOM / 纯 LF / 775 B 未变）\r
      :13 AssemblyFileVersion          0.291.7.5        -> 0.291.7.6\r
      :14 AssemblyInformationalVersion 0.291.7.5-beta22.7.5 -> 0.291.7.6-beta22.7.6\r
      :19 AssemblyVersion              0.291.7.5        -> 0.291.7.6\r
  · installer-src\\install_script.iss（noBOM / CRLF / 166,497 B 未变）\r
      :7  AppVerName              -> L-Mechrevo 0.291.7.6 beta22.7.6\r
      :9  AppVersion              -> 0.291.7.6\r
      :14 OutputBaseFilename      -> LM-0.291.7.6-beta22.7.6-setup\r
      :17 VersionInfoVersion      -> 0.291.7.6（保持四段纯数字）\r
      :18 VersionInfoProductVersion -> 0.291.7.6（保持四段纯数字）\r
      :19 UninstallDisplayName    -> L-Mechrevo 0.291.7.6 beta22.7.6\r
      替换后全文件「0.291.7.5」命中 0 处、「0.291.7.6」命中 6 处。\r
  · installer-src\\打包须知.txt（noBOM / CRLF / 16,085 B 未变）\r
      :10 主 / GCU 包 = 0.291.7.5-beta22.7.5 -> 0.291.7.6-beta22.7.6\r
  · ⛔ nogcu（0.291.5 / beta22.5）与 LMEXP（0.291.4 / beta22.4）的版本字段一字未动；⛔ 未改历史流水；\r
    ⛔ 未生成源码包。\r
\r
【本轮改动文件的确切 SHA-256 与字节数（按磁盘现行文件实测）】\r
  · installer-src\\install_script.iss                     166,497 B  4D9D3D31DA2EB89E1DEABAFFC9CBBBF0672F8206C4EEC9049E6C7E8BEBA2C1BC\r
  · source\\Properties\\AssemblyInfo.cs                      775 B  2A9CE4F15E588D46199D94E188DC7F6612BDAF1B8FA718D8C57BBA33DBF74255\r
  · installer-src\\打包须知.txt                            16,085 B  39E03D03698D108596124B6E1CC900FE8EBAFA9B70C5F18D17A2E9AE370C931B\r
  · tools\\LmLauncherStub\\LmLauncherStub.cs                31,182 B  88CEFF8A48FE12CAB8F5F81587F489D46B7992D9DCC97D048C5C4E7814585402\r
  · installer-src\\files\\L-Mechrevo-Launcher.exe           23,040 B  D87C7FDD571807902046E9D677FB97FA5FAF194C75465D24292E1CF464C5FE2D\r
  · installer-src\\files\\L-Mechrevo.dll                 2,706,432 B  91F81E836EAA993B7CEFC3CAD41BBB6B1BEA1F3B52B38F3F5C9DAD95DF7C84D6\r
  · installer-src\\files\\L-Mechrevo.exe                   170,496 B  6734558B8C197F96A623242F0830BF517F92451B2BCE3665122E019D48637AB9\r
  · installer-src\\files\\zh-CN\\L-Mechrevo.resources.dll    88,064 B  692C10CAC41E1C82765DF64C67F72577BCEFEB0144615F4D96EF90D644853884\r
  · 更新日志.txt / installer-src\\files\\更新日志.txt（两份逐字节一致） 105,968 B  E853DC669E9E34A6F0127EDF131C246B743406CED67D10593CAEFA3A9D4DDAFB\r
  · ★ 包 installer-src\\Output\\LM-0.291.7.6-beta22.7.6-setup.exe  69,630,084 B  00CB6BAEC694F092780FA818F7C104A3717361F61C4E673FA945B2B77BC65AFC\r
  · ★ 新增（验证用，非产品）：tools\\LmStubUrlHarness\\{LmStubUrlHarness.csproj,Program.cs}、\r
    installer-src\\_probe\\{probe_browserchain.iss,probe_shadow_probe.iss}、\r
    tools\\dev\\_r23_{bump_version,merge_files,changelog,unpack_verify,diag}.py\r
\r
【构建】\r
  · 启动壳（先建）：& 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\tools\\LmLauncherStub\\LmLauncherStub.csproj' -c Release\r
      => 0 错误 / 0 警告；产物直落 installer-src\\files\\L-Mechrevo-Launcher.exe\r
         （19,968 B -> **23,040 B** / D87C7FDD…；bin\\Release 同名产物 SHA 逐字节相同）。\r
  · 主程序：& 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release -t:Rebuild\r
      => **0 错误 / 1444 个警告**，与基线 1444 **完全持平（差额 0，未新增警告）**。\r
      （⚠️ 提示：不带 -t:Rebuild 的增量构建输出是 0 警告；1444 这个基线的口径就是 -t:Rebuild，\r
        本轮 Rebuild 实测 1444，与基线持平。下次比对请同样加 -t:Rebuild，否则会看到 0。）\r
      => 产物 PE 版本实测：FileVersion = 0.291.7.6 / ProductVersion = 0.291.7.6-beta22.7.6。\r
  · publish 到 D:\\openLM\\rebuild\\lm_r23_build（-c Release -p:NoWarn=WFO1000%3BCA1416）供合并用。\r
\r
【installer-src\\files\\ 刷新（逐文件 SHA-256 比对，只覆盖真不同的）】\r
  · 更新 3 个（其余全部 SAME，不动）：\r
      L-Mechrevo.dll                2,706,432 B A1EA4675… -> 2,706,432 B 91F81E83…\r
      L-Mechrevo.exe                  170,496 B 0A28211F… ->   170,496 B 6734558B…\r
      zh-CN\\L-Mechrevo.resources.dll   88,064 B 6DCA37D1… ->    88,064 B 692C10CA…\r
  · 未变（SAME，未覆盖）：L-Mechrevo.deps.json / runtimeconfig.json / dll.config / 启动壳 / GCU 全部 / 字体 / usbmmidd_v2 / Resources。\r
  · 文件数 337 -> 337（0 新增 / 0 删除）；无 .tmp-copy 残留。\r
  · 备份：backup\\repack\\2026-10\\20261013_2115_pre_repack_beta2276\\\r
      ├─ Output\\（改前的包 LM-0.291.7.5-beta22.7.5-setup.exe，69,629,221 B）\r
      ├─ files\\（改前全量 337 个文件）\r
      └─ version_files\\（iss / AssemblyInfo.cs / 打包须知.txt / 两份更新日志 / 修改日志）\r
\r
【ISCC】\r
  · 验证编译（⭐ 重定向 /O%TEMP%\\lm_r23_iscc_out）：=> exit 0，0 Error / 0 Warning，\r
    产物 %TEMP%\\lm_r23_iscc_out\\LM-0.291.7.6-beta22.7.6-setup.exe（69,630,084 B / 00CB6BAE…）。\r
  · 正式出包（最后一步）：ISCC.exe installer-src\\install_script.iss => exit 0，0 Error / 0 Warning；\r
    与上面验证编译的产物 **SHA 逐字节相同**（00CB6BAE…）=> 验证编译确实等价于正式包。\r
  · tools\\dev\\check_iss_files.py：A 类「任一 .iss 引用但磁盘不存在」仍为 1 项 = `usbmmidd_v2\\*`\r
    （通配符 Source 的**既有假阳性**，9 个文件实际都在磁盘且都被 ISCC 编进去了）=> 预期内，非阻塞。\r
    B 类 16 项 / 1.34 MB 为既有冗余（pdb、usbmmidd 安装器 exe/dll、两份旧更新日志 bak、gcu-variants.json 等），本轮未动。\r
\r
【包】\r
  · 路径：installer-src\\Output\\LM-0.291.7.6-beta22.7.6-setup.exe\r
  · 大小：69,630,084 B\r
  · SHA-256：00CB6BAEC694F092780FA818F7C104A3717361F61C4E673FA945B2B77BC65AFC\r
  · 时间戳：2026-10-13 21:18:47\r
  · 包内 PE 版本实测：FileVersion = 0.291.7.6 / ProductVersion = 0.291.7.6（四段纯数字）。\r
\r
【更新日志（两份，倒叙置顶）】\r
  · 根 更新日志.txt 与 installer-src\\files\\更新日志.txt：都在**最顶部第 1 行**插入\r
    「三十、0.291.7.6 beta22.7.6（2026-10-13）」一节，正文只有一句：\r
    「1. 修复点「下载」有时不打开浏览器（现在会以你的身份打开）。」\r
  · 105,682 B -> 105,968 B；SHA-256 两份**逐字节一致** = E853DC669E9E34A6F0127EDF131C246B743406CED67D10593CAEFA3A9D4DDAFB。\r
  · ⛔ 两份都没提 .iss 细节 / 探针 / harness 事故。\r
\r
【解包核对（innounp 2.67.11 解到 D:\\openLM\\work\\2026-10-13\\r23_unpack，逐项断言；脚本 tools\\dev\\_r23_unpack_verify.py）】\r
  · 汇总 **OK 80 项 / FAIL 项数 = 0**。重点：\r
    {app} 根 = 21 个文件 + 8 个子目录，**含启动壳**、无散落 .txt/.md、无 LICENSE / LICENSE.txt；\r
    LICENSES\\ 8 件（含 ,1/,2 两个 THIRD_PARTY_NOTICES，**无 LICENSE.txt**）；Docs\\ = 更新日志.txt + 用前必看.txt；\r
    UWACPIDriver\\ 3 件（11,342 / 2,034 / 46,352 B，且 == files 源件）；usbmmidd_v2 9 件（含 Win32\\ / x64\\）；\r
    四张收款码在 Resources\\ 且 == files 源件；notices 文本 0 处「18 files」；\r
    **包内 Docs\\更新日志.txt 顶部第 1 行 = 「三十、0.291.7.6 beta22.7.6（2026-10-13）」**，0.291.7.5 紧随其后；\r
    6 个核心文件（dll/exe/deps/runtimeconfig/dll.config/zh-CN 卫星）包内 == 构建产物 == files\\；\r
    payload 条目 30=163 / 40=4 / 40B=8 / 50=7 / GCU=97（磁盘实得 94，innounp 同源去重 -3，属文档已注明的既有差异）；**无 payload\\1020**；\r
    iss 内 `/KEEPVENDORCONSOLE` 与 `-LmOwnedOnly` 均在，且 [UninstallRun] 确实把 -LmOwnedOnly 传给 Uninstall-Gcu.ps1；\r
    包内 GCU\\Install-Gcu.ps1 = **88,655 B / AABE37714B71E2EF0AE97321185817904E3D78FD03D56DF2A6E705017FDA0F41**（== files 源件）；\r
    **启动壳** {app}\\L-Mechrevo-Launcher.exe = **23,040 B / D87C7FDD571807902046E9D677FB97FA5FAF194C75465D24292E1CF464C5FE2D**；\r
    **三条指向启动壳**在重建 iss 内 1 处 [Run] + 2 处 [Icons]（开始菜单 / 桌面），逐条断言通过；\r
    重建 iss 内 AppVersion=0.291.7.6、AppVerName 带 beta22.7.6、OutputBaseFilename=LM-0.291.7.6-beta22.7.6-setup，且已无 0.291.7.5。\r
  · ⚠️ 如实标注：安装器那段 [Code] 链**无法**在 setup 二进制里 grep 证明 —— Inno 把 [Code] 编译成压缩字节码，\r
    连【旧包】里确定存在的 "ShellExecAsOriginalUser" / "下载 / Download" 也 utf8/utf16le 双编码 0 命中（本轮实测）。\r
    => 安装器改动的证据链 = ① ISCC 0 Error/0 Warning 编过 ② probe_browserchain 逐字副本 24/24 PASS。\r
\r
【边界确认】\r
  · ⛔ 未生成源码包；⛔ 未给 nogcu / LMEXP 出包、也未改它们的版本字段；⛔ 未改历史流水；\r
  · ⛔ 未改电源计划、未发真实 EC/GCU 命令、未切换显卡模式、未结束任何进程、未装/卸驱动、\r
    未写注册表/EC/NVRAM、未创建/删除显示设备、未连 13688；\r
  · ⛔ 未动 _upstream\\ / archive\\ / diagnostics\\ / dist\\ / 待办.txt / docs\\交接-WiFi与蓝牙磁贴图标.md；\r
    （backup\\ 只新建了 20261013_2115_pre_repack_beta2276\\ 一个备份目录）⛔ 未碰键盘灯逻辑与作者 HID 机制；\r
  · ⛔ 未执行 tools\\dgpu-silence\\ 下任何脚本。\r
\r
'''

OUT = BLOCK + OLD
raw = OUT.encode('utf-8')
tmp = P + '.tmp-write'
open(tmp, 'wb').write(raw)
chk = open(tmp, 'rb').read()
assert chk == raw
# 校验：旧全文仍是新全文的后缀（逐字节）
assert chk.endswith(RAWT), 'old content changed!'
os.replace(tmp, P)
fin = open(P, 'rb').read()
assert fin == raw
print('OK 修改日志.txt %d B -> %d B  sha %s' % (len(RAWT), len(fin), hashlib.sha256(fin).hexdigest().upper()))
print('新增字节数 = %d' % (len(fin) - len(RAWT)))
print('顶部 3 行:')
for l in fin.decode('utf-8').split('\r\n')[:4]:
    print('   ' + l)
print('ALL-DONE')
