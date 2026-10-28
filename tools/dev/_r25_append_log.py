# -*- coding: utf-8 -*-
r"""r25：把本轮小节【字节级加到 修改日志.txt 顶部】（倒叙）。

纪律（沿用 r22 / r23）：
  ⛔ 不用 Add-Content；用 AppendAllText 等价物：读全bytes -> 断言 -> 备份 -> 新块 + 旧文
     -> 写 TEMP -> 回读校验 -> 原子替换。
  ⚠️ 原文是**混合行尾**（CRLF + 少量裸 LF）——不规范化、不重写既有部分，只在最前面拼新块。
  ⚠️ 无 BOM，UTF-8。
"""
import hashlib, os, shutil, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

P = r'D:\openLM\修改日志.txt'
BAK = (r'D:\openLM\backup\repack\2026-10\20261013-222735_pre_repack_beta2276_r25'
       r'\version\修改日志.txt')
EXPECT_SHA = 'F5C5A78659E899B0BC173B5BA106B44EED487B80ECE1352A14F57A1E9FD1F6DE'

BLOCK = '''\r
================================================================================\r
r25（2026-10-13）同名重打 0.291.7.6 / beta22.7.6 —— .NET 自动下载静默安装 + 删启动壳 + 剪贴板改 clip.exe\r
================================================================================\r
【用户原话】\r
  ·「打」—— 把今晚（r24 / r25）所有改动打进安装包；版本【保持】0.291.7.6 / 0.291.7.6-beta22.7.6，\r
    【同名重打】（Output 里那份包名复用、直接覆盖）。\r
\r
【本轮进包的改动摘要】\r
  · installer-src\\install_script.iss（FAF4542D…，193,938 B）＝今晚整套 .NET 流程：\r
      缺 .NET 10 桌面运行时 => 安装器【自己下载 + 静默安装】\r
        （/install /repair /passive /norestart；上限 600 s；轮询 2 s；超时按我们的文件名 taskkill）、\r
        下载页在 InitializeWizard 建、Show / Download / Hide；\r
        就绪页提前告知「附加任务：…（10.0.x）—— 安装程序将自动下载并静默安装」（重建了 Inno 默认各段）；\r
        只有【下载 / 安装失败】才弹三按钮「重试下载 / 用浏览器下载 / 稍后」，弹框前直链已进剪贴板；\r
        主直链 aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe\r
          -> 备用 builds.dotnet.microsoft.com/…/windowsdesktop-runtime-10.0.12-win-x64.exe\r
          -> 再退 DownloadTemporaryFile；\r
        Result 恒为 ''（⛔ 绝不中止）；静默安装 => 不下载 / 不页面 / 不弹框；\r
        日志前缀 LM-NET-DL: / LM-NET-CLIP: / LM-NET-BROWSER:。\r
  · 启动壳【彻底移除】：[Files] 里那条 Source 已删、常量 LmLauncherExe 已删、\r
      [Run](:491) 与两条 [Icons](:522 / :527) 全部指向 {app}\\L-Mechrevo.exe；\r
      installer-src\\files\\L-Mechrevo-Launcher.exe 已删\r
      （备份 backup\\iss\\r25_20261013_pre_netdl_launcher_removal\\）。\r
      ⚠️ 源码工程 tools\\LmLauncherStub\\ 仍在，但【不再随包发】=> ⛔ 本轮没有再 build 它。\r
  · C# 侧（源最后改动 21:17:15，之后未再改）：校色开关「关了又自己开」修复 · miniLED 可见性 + 自愈 + 取证日志 ·\r
      核显省电整套（确认循环 + 占用 / 独显唯一显示门 + 强制 / 回滚 + 新增 WmiDisplayRouteProbe.cs /\r
      DgpuApplicationSafety.cs / IgpuOnlySaveModeDecision.cs 三个文件 + 2 条新文案）· .NET 检测放宽 >= 10。\r
\r
【内部值得一提的两个坑】\r
  1. explorer 路径曾写成 System32：r24 之前「打开网址」链里 explorer.exe 取的是 System32 下的路径，\r
     实际应为 %WINDIR%\\explorer.exe —— 提权上下文里那一档会静默失败（不抛异常、什么都不发生）。\r
     r24 修掉（含 {win} 解析 + 路径先校验），本轮进了包。\r
  2. 剪贴板旧实现是【坏的】：旧的外部函数写法在提权上下文里写不进剪贴板（地址「复制」了却粘不出来）。\r
     r25 把旧实现连同 9 个 external 一起删掉，改用 Windows 自带的 clip.exe 写剪贴板\r
     （Exec('{cmd}', '/c clip', …)，日志前缀 LM-NET-CLIP:）。\r
\r
【构建与刷新】\r
  · dotnet build source\\L-Mechrevo.csproj -c Release -t:Rebuild\r
      => 0 个错误 / 1444 个警告（与基线 1444 【完全一致】）。\r
      ⚠️ 1444 是 C# 构建的警告数，与 ISCC 的 0 Error / 0 Warning 不是一回事。\r
  · files\\ 刷新（tools\\dev\\_r25_merge_files.py，逐文件 SHA-256，只覆盖真不同）：\r
      真不同【只有】L-Mechrevo.pdb（654,252 -> 660,308 B）；而 .iss 并不打包 pdb，\r
      所以【没有一行包内内容因此改变】。dll / exe / deps.json / runtimeconfig.json / dll.config /\r
      zh-CN 卫星本来就跟新构建【逐字节相同】（C# 最后一次改动是 21:17:15，r23 那次刷新已经带上）。\r
      文件数 336 -> 336（0 新增 / 0 删除），无 .tmp-copy 残留。\r
  · 壳确认【不在】files\\（递归找 *Launcher* => 0 项）。\r
\r
【备份】\r
  · backup\\repack\\2026-10\\20261013-222735_pre_repack_beta2276_r25\\\r
      Output\\LM-0.291.7.6-beta22.7.6-setup.exe（旧包 00CB6BAE… / 69,630,084 B / 21:18:47）\r
      files\\（336 个文件，刷新【前】的快照）· version\\（AssemblyInfo.cs / install_script.iss / 根 更新日志.txt）\r
\r
【ISCC】\r
  · 先跑 /O 重定向的验证编译（⚠️ 这坑踩过两次：绝不能让 ISCC 默认输出到 Output\\）：\r
      输出到 %TEMP%\\lm_iscc_verify_r25\\ => Successful compile (23.203 sec)，0 Error / 0 Warning；\r
      全程复核 Output\\ 仍是 21:18:47 / 69,630,084 B（没被碰）。跑完即删，⛔ 没留裸 exe。\r
  · 正式出包（排在改完 更新日志【之后】，因为 files\\更新日志.txt 是要进包的）：\r
      Successful compile (27.391 sec)，0 Error / 0 Warning。\r
  · tools\\dev\\check_iss_files.py：A 类仅 usbmmidd_v2\\* 1 项（既有假阳性，预期内）；\r
      B 类 16 项真冗余 —— ⭐ 启动壳【已不在】冗余清单里。\r
\r
【解包手工核对（tools\\dev\\_r25_unpack_verify.py，FAIL 项数 = 0）】\r
  · {app} 根 = 8 个子目录 + 20 个文件：【没有 L-Mechrevo-Launcher.exe】、无散落 txt / md、\r
      无 LICENSE / LICENSE.txt；\r
  · {app}\\LICENSES\\（无 LICENSE.txt）· {app}\\Docs\\（更新日志 + 用前必看）·\r
      {app}\\UWACPIDriver\\ 三件 11,342 / 2,034 / 46,352 B（均 == files 源件）；\r
  · usbmmidd_v2 九件 · 四张收款码 · notices 0 处「18 files」；\r
  · 包内 Docs\\更新日志.txt 顶部第一节 = 0.291.7.6，且与改后的 files\\ / 根 版本【逐字节一致】；\r
  · GCU\\Install-Gcu.ps1 == AABE3771…（88,655 B）；\r
  · [Run] 与两条 [Icons] 都指向 {app}\\L-Mechrevo.exe；仓库 iss 与包内重建 iss 的\r
      [Files] / [Run] / [Icons]【条目行】壳引用 = 0。⚠️ [Files] 里 r25 那段【注释】刻意留了文件名\r
      （写明「已移出 files\\、备份在哪」）—— ⛔ 不是条目行，且 iss 的 SHA 本轮已钉死，故不改 iss。\r
  · 6 核心哈希 == 构建产物 == files\\ · FV / PV = 0.291.7.6 · payload 163 / 4 / 8 / 7 / 97\r
      （磁盘 GCU 实得 94 = 既有去重差）· 无 payload\\1020 · /KEEPVENDORCONSOLE 与 -LmOwnedOnly 均在。\r
  · 哨兵（本轮【没碰任何 .cs】）：case "wifi" / "bt" / "touchpad" / "touchpadtoggle" 各 1；\r
      UiGlyph.Kind.Wifi / Bluetooth / Touchpad / Refresh 实测 1 / 1 / 2 / 1\r
      ⚠️ 与任务里「2 / 2 / 2 / 1」口径不同：差值来自 docs\\交接-WiFi与蓝牙磁贴图标.md 里另有 12 处文字引用；\r
      源码本体自本轮开始到结束【未被改动】（本轮只写 iss / 两份更新日志 / 本文件 / tools\\dev 脚本）。\r
\r
【更新日志】\r
  · 两份 更新日志.txt 就地改写【已有的 0.291.7.6 一节】（倒叙，就在文件最顶部）：\r
      4D1DC868… 106,773 B -> 91666269… 109,569 B，两份【逐字节一致】；\r
      内容 = (a) 缺 .NET 10 自动下载 + 静默安装（就绪页提前告知）(b) 只有失败才弹框：重试 / 用浏览器 / 稍后\r
      + 地址已进剪贴板 (c) 校色开关「关了又自己打开」(d) miniLED 不显示 + 自愈\r
      (e) 核显省电切不过去（真实确认 + 失败提示 + 独显占用可强制，含确认与回滚）\r
      (f) 不再需要单独的启动器；\r
      ⛔ 第一节内已无「启动壳 / 中文提示 / 探针 / .iss 损坏」等说法。\r
      ⚠️ 顺序：files\\更新日志.txt 是要进包的，所以必须【先改日志、后打包】—— 正式出包因此排在改日志之后。\r
\r
【文件 SHA-256】\r
  installer-src\\install_script.iss                    FAF4542D0DAA13D319099D4CA23517F94CA109BFBD85E3245565B6BD8E732D92  193,938 B\r
  installer-src\\files\\L-Mechrevo.dll                 91F81E836EAA993B7CEFC3CAD41BBB6B1BEA1F3B52B38F3F5C9DAD95DF7C84D6  2,706,432 B\r
  installer-src\\files\\L-Mechrevo.exe                 6734558B8C197F96A623242F0830BF517F92451B2BCE3665122E019D48637AB9  170,496 B\r
  installer-src\\files\\L-Mechrevo.deps.json           6BB31346BEB4F9AD0194E905B864A4297D1BA905A87898ABD782DCBE0F808769  5,158 B\r
  installer-src\\files\\L-Mechrevo.runtimeconfig.json  96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3  519 B\r
  installer-src\\files\\L-Mechrevo.dll.config          53E402ADC79797BB1FD0850D5FE84726A3BBCE27E98A4AED1E59FB83415CD24A  175 B\r
  installer-src\\files\\zh-CN\\L-Mechrevo.resources.dll 692C10CAC41E1C82765DF64C67F72577BCEFEB0144615F4D96EF90D644853884  88,064 B\r
  installer-src\\files\\L-Mechrevo.pdb                 F27D4A8FAD9B26081ECD5F5761BCFD3503268BAA42FEA8626A9C70998FD36FAF  660,308 B\r
  installer-src\\files\\GCU\\Install-Gcu.ps1            AABE37714B71E2EF0AE97321185817904E3D78FD03D56DF2A6E705017FDA0F41  88,655 B\r
  更新日志.txt（根 == files\\，改后）                    91666269CC4FE3D2CC4BC88A2C4999FE0FEC47450044AC08AE71E47C0713316C  109,569 B\r
  更新日志.txt（两份，改前）                            4D1DC8683B64555E07ED890BC8C2804C892360288317C2CDF277DD8604CB61D0  106,773 B\r
  source\\Properties\\AssemblyInfo.cs                  2A9CE4F15E588D46199D94E188DC7F6612BDAF1B8FA718D8C57BBA33DBF74255  775 B\r
\r
【包】\r
  路径      D:\\openLM\\installer-src\\Output\\LM-0.291.7.6-beta22.7.6-setup.exe\r
  大小      69,621,868 B\r
  SHA-256   3562386D1844A4378570F62C02E19426FA8732D4CA6964E0F762F4C59494AB6A\r
  时间戳    2026-10-13 22:31:04\r
  FV / PV   0.291.7.6 / 0.291.7.6\r
  ⚠️ 版本【刻意未升】、包名【复用】：Output\\ 里仍是同一个文件名 LM-0.291.7.6-beta22.7.6-setup.exe，\r
     直接覆盖了旧包。旧包 00CB6BAE…（69,630,084 B / 21:18:47，= r23 的构建，不含今晚之后任何修复）\r
     【已被取代】，并已备份到 backup\\repack\\2026-10\\20261013-222735_pre_repack_beta2276_r25\\Output\\。\r
\r
【边界（本轮没做）】\r
  · ⛔ 未生成源码包；⛔ 未运行真正的安装器；⛔ 未发起任何真实下载（.NET 直链只写在代码里，没触网）；\r
  · ⛔ 未执行 tools\\dgpu-silence\\ 下任何脚本；⛔ 未切换本机显卡模式；⛔ 未结束任何进程；\r
  · ⛔ 未写注册表 / EC / NVRAM；⛔ 未装 / 卸驱动；⛔ 未创建 / 删除显示设备；⛔ 未连 13688；\r
  · ⛔ 未碰键盘灯逻辑与作者 HID 机制；⛔ 未手工发任何真实 EC / GCU 命令；\r
  · ⛔ 未动 _upstream\\ / archive\\ / diagnostics\\ / dist\\ / 待办.txt（backup\\ 只新建了备份目录）；\r
  · ⛔ 未动 docs\\交接-WiFi与蓝牙磁贴图标.md；⛔ 未动冻结的 nogcu / LMEXP iss；\r
  · ⛔ 未改版本文件（本来就是 0.291.7.6）；⛔ 未给 nogcu / LMEXP 出包或改其版本字段。\r
'''


def sha(b):
    return hashlib.sha256(b).hexdigest().upper()


def main():
    old = open(P, 'rb').read()
    pre = sha(old)
    print('修改日志.txt 改前 %d B  sha %s' % (len(old), pre))
    assert pre == EXPECT_SHA, '前置 SHA 不符：%s' % pre
    assert old.startswith(b'\r\n===='), '顶部锚点不对'
    assert 'r25（2026-10-13）同名重打' not in old.decode('utf-8'), '已经拼过了'
    assert 'r23（2026-10-13）打包' in old.decode('utf-8'), '找不到上一块（r23），拼错位置'
    blk = BLOCK.encode('utf-8')
    os.makedirs(os.path.dirname(BAK), exist_ok=True)
    if not os.path.exists(BAK):
        shutil.copy2(P, BAK)
    assert sha(open(BAK, 'rb').read()) == pre, '备份不等于改前'
    new = blk + old
    assert new[len(blk):] == old, '旧文被改动'
    assert new[:len(blk)] == blk, '新块没在最顶部'
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_modlog.txt')
    open(tmp, 'wb').write(new)
    back = open(tmp, 'rb').read()
    assert back == new, 'TEMP 回读不一致'
    assert back[:3] != b'\xef\xbb\xbf', '不要 BOM'
    shutil.copyfile(tmp, P)
    fin = open(P, 'rb').read()
    assert fin == new, '落盘后不一致'
    assert fin[len(blk):] == old, '落盘后旧文被改动'
    print('修改日志.txt 改后 %d B  sha %s' % (len(fin), sha(fin)))
    print('新增字节 = %d（%.1f KB）; 旧文 %d B 原样保留（含 %d 个裸 LF）'
          % (len(blk), len(blk) / 1024.0, len(old), old.count(b'\n') - old.count(b'\r\n')))
    print('备份 = %s' % BAK)
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
