# -*- coding: utf-8 -*-
r"""
r25 定点补丁（install_script.iss）
================================================================================
本轮两件事（用户口径）：
  A. 【自动下载 + 静默安装运行时】「不过我觉得可以加,下载失败再弹选择」
     => PrepareToInstall/PromptNet10Missing 里：先自己下载（可见进度页）+ 静默安装；
        只有下载/安装失败才弹 r24 那个选择框（按钮改成 重试下载/用浏览器下载/稍后）；
        静默安装时 不下载/不显示页面/不弹框；等待安装器有上限（600s）。
  B. 【删启动壳】「然后我们启动壳删除吧…删了干净一点，也不会引入新的bug」
     => [Run] + 两条 [Icons] 指回 {app}\L-Mechrevo.exe；[Files] 不再打包壳；
        源码工程 tools\LmLauncherStub\ 保留；常量 LmLauncherExe 移除。
  C. 【就绪页加一行】「附加任务：Microsoft Windows Desktop Runtime 10.0.x（x64）—— 安装程序将自动下载并安装」
     => UpdateReadyMemo + [CustomMessages] RuntimeReadyMemo。

纪律：读 -> 断言（SHA + 每处锚点唯一）-> 备份（已由外部脚本做）-> 定点拼接
      -> 写 TEMP -> 校验 TEMP -> 替换正式文件。⛔ 绝不手算偏移。
"""
import hashlib
import io
import os
import shutil
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ISS = r'D:\openLM\installer-src\install_script.iss'
EXPECT_SHA = '7B40DF43221ACFFB5A4BC505A9F3D0DA50577B55FBB92D91B5D9530C1B0BD4D6'
CRLF = '\r\n'


def sha256_of(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest().upper()


def join(*lines):
    return CRLF.join(lines)


def rep(src, old, new, label):
    n = src.count(old)
    assert n == 1, '[%s] 锚点命中 %d 次（必须 1 次）' % (label, n)
    return src.replace(old, new, 1)


def replace_line(src, prefix, newline, label):
    """整行替换：以 prefix 定位行首，替换到该行行尾（⛔ 不依赖行长/尾随空格）。"""
    n = src.count(prefix)
    assert n == 1, '[%s] 行首锚点命中 %d 次（必须 1 次）' % (label, n)
    i = src.index(prefix)
    assert i == 0 or src[i - 1] == '\n', '[%s] 锚点不在行首' % label
    j = src.index(CRLF, i)
    return src[:i] + newline + src[j:]


def main():
    print('== r25 patch: install_script.iss ==')
    pre_sha = sha256_of(ISS)
    print('pre  sha256 = %s  size=%d' % (pre_sha, os.path.getsize(ISS)))
    assert pre_sha == EXPECT_SHA, '前置 SHA 不符（文件已被改过？）: %s' % pre_sha

    with io.open(ISS, encoding='utf-8', newline='') as f:
        src = f.read()
    orig = src

    # ---------------------------------------------------------------- A/B：删启动壳
    src = rep(src, join(
        '; 2026-10-13：启动壳（跑在 .NET Framework 4.8.1 上的极小 exe，19968 B）——开始菜单/桌面快捷方式与',
        ';   [Run] 现在都指向它。它负责「缺 .NET 10 桌面运行时」时的【中文】提示 + 可复制的下载地址；',
        ';   运行时在 = 原样透传命令行参数并拉起同目录的 L-Mechrevo.exe。真正的程序是 .NET 10 的 apphost，',
        ';   缺运行时时它自己根本起不来、弹的是焊死在 exe 里的英文窗（改不了），只能由壳挡在前面。',
        ';   源码工程：tools\\LmLauncherStub\\（不参与 source\\L-Mechrevo.csproj 的构建）。'),
        join(
            '; 2026-10-13（r25）：⛔ 【不再打包启动壳】。用户原话：',
            ';   「然后我们启动壳删除吧,想想微软自带的那个下载已经有了 删了干净一点，也不会引入新的bug」',
            ';   理由（⭐ 也供将来写更新日志）：',
            ';     ① 缺 .NET 10 桌面运行时这件事，安装阶段已改成【安装器自己下载 + 静默安装】',
            ';        （[Code] 的 NetDlTryInstallRuntime / NetDlDownloadToTmp / NetDlRunInstaller；',
            ';         只有下载或安装失败才弹那个选择框）=> 壳不再是「唯一的提示来源」；',
            ';     ② 万一装完运行时仍缺失（用户跳过了 / 下载失败又选了「稍后」），双击 L-Mechrevo.exe 时',
            ';        apphost 会弹微软自带那句英文提示（焊在 exe 里，改不了）=> 覆盖面已经够了；',
            ';     ③ 壳自身还带一类 bug（它那侧 explorer 的错误路径，r24 才修掉）=> 删掉更干净、少一类维护面。',
            ';   ⚠️ 源码工程 tools\\LmLauncherStub\\【保留】（不再随包发；要恢复只改 [Files]/[Icons]/[Run] 三处指向）。',
            ';   ⚠️ 已构建的 installer-src\\files\\L-Mechrevo-Launcher.exe 已移出 files\\（备份在',
            ';      backup\\iss\\r25_20261013_pre_netdl_launcher_removal\\）=> check_iss_files.py 不再把它算冗余。'),
        'A1 启动壳 [Files] 注释')

    src = rep(src,
              'Source: "files\\L-Mechrevo-Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion ' + CRLF
              + 'Source: "files\\L-Mechrevo.runtimeconfig.json"',
              'Source: "files\\L-Mechrevo.runtimeconfig.json"',
              'A2 启动壳 [Files] 条目')

    src = rep(src,
              ';    现在启动的是启动壳（不是 apphost）：缺运行时它弹【中文】提示 + 可复制下载地址；有运行时它拉起' + CRLF
              + ';    真正的 L-Mechrevo.exe。',
              ';    r25：直接启动程序本体（.NET 10 的 apphost）。缺运行时这件事现在由【安装阶段自动下载+静默安装】' + CRLF
              + ';    解决（见 [Code] 的 NetDlTryInstallRuntime；失败才弹选择框）；万一装完仍缺运行时，apphost 会弹' + CRLF
              + ';    微软自带那句英文提示 —— ⛔ 启动壳已在 r25 移除，不再有「壳的中文提示」这一层。',
              'A3 [Run] 注释')

    src = rep(src,
              'Filename: "{app}\\L-Mechrevo-Launcher.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall skipifsilent nowait',
              'Filename: "{app}\\L-Mechrevo.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall skipifsilent nowait',
              'A4 [Run] 目标')

    src = rep(src,
              '; 2026-10-13：快捷方式一律指向启动壳（壳再拉起 L-Mechrevo.exe）——缺 .NET 10 时才提示得出来。',
              '; 2026-10-13（r25）：快捷方式指回程序本体 —— ⛔ 启动壳已移除；缺 .NET 10 运行时由【安装阶段'
              + CRLF + ';   自动下载 + 静默安装】解决（失败才弹选择框）。',
              'A5 [Icons] 注释')

    src = rep(src,
              'Name: "{group}\\L-Mechrevo"; Filename: "{app}\\L-Mechrevo-Launcher.exe"; WorkingDir: "{app}";',
              'Name: "{group}\\L-Mechrevo"; Filename: "{app}\\L-Mechrevo.exe"; WorkingDir: "{app}";',
              'A6 开始菜单快捷方式')

    src = rep(src,
              'Name: "{autodesktop}\\L-Mechrevo"; Filename: "{app}\\L-Mechrevo-Launcher.exe"; WorkingDir: "{app}"; Tasks: desktopicon;',
              'Name: "{autodesktop}\\L-Mechrevo"; Filename: "{app}\\L-Mechrevo.exe"; WorkingDir: "{app}"; Tasks: desktopicon;',
              'A7 桌面快捷方式')

    src = rep(src,
              '  // 2026-10-13：启动壳文件名（[Files]/[Icons]/[Run] 三处共用；改名只改这一行）。' + CRLF
              + "  LmLauncherExe = 'L-Mechrevo-Launcher.exe';",
              '  // 2026-10-13（r25）：启动壳常量 LmLauncherExe 已随启动壳一起移除（⛔ 包内不再有壳；'
              + CRLF + '  //   程序本体 = {app}\\L-Mechrevo.exe）。',
              'A8 LmLauncherExe 常量')

    src = rep(src,
              '  // ③ 启动壳（{app}\\L-Mechrevo-Launcher.exe）在 LM 启动时的中文提示保持不变。',
              '  // ③ r25：启动壳已移除 —— 缺运行时由安装阶段自己下载+静默安装；万一仍缺，'
              + CRLF + '  //    apphost 会弹微软自带那句英文提示（⛔ 不再有壳的中文提示）。',
              'A9 InitializeSetup 注释')

    src = rep(src,
              '    // ⚠️ 写死 True：[Run] 执行的是启动壳（不是 apphost），缺运行时由启动壳在 LM 启动时给中文提示。',
              '    // ⚠️ 写死 True：[Run] 执行的是程序本体；若运行时最终没装上（下载失败又选了「稍后」），'
              + CRLF + '    //    启动时由 apphost 自己弹微软那句英文提示（r25 起不再有启动壳）。',
              'A10 PrepareToInstall 注释1')

    src = rep(src,
              "    Log('LM-NET: 写入前 .NET 10 检测/提示段异常 -> 已吞掉，照常安装；启动时由启动壳给中文提示');",
              ";     Log('LM-NET: 写入前 .NET 10 检测/提示/自动下载段异常 -> 已吞掉，照常安装；启动时由 apphost 自己提示');",
              'A11 PrepareToInstall 注释2')

    # ---------------------------------------------------------------- C：新常量
    src = rep(src,
              "  Net10DownloadDirectUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';",
              join(
                  "  Net10DownloadDirectUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';",
                  '  // 2026-10-13（r25）：安装器【自己下载】时用的【备用直链】= 上面那条 aka.ms 短链的 302 终址',
                  '  //   （r24 只读 HEAD 实测：200 + application/octet-stream + 60,032,984 B；版本 10.0.12）。',
                  '  //   用途：aka.ms 跳转失败（短链服务抖动 / 被墙 / 被安全软件拦）时自动改用这条重试一次。',
                  "  Net10DownloadFallbackUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe';",
                  '  // 下载落到 {tmp} 时用的文件名（⚠️ 同时也是超时 taskkill 的镜像名 => 取足够独特的名字，',
                  '  //   ⛔ 不照抄 LLT 的 dotnet8.exe —— 我们这套一律 .NET 10）。',
                  "  NetDlBaseName = 'lm-net10-windowsdesktop-runtime-x64.exe';",
                  '  // 运行时安装器的静默参数（与 LLT / 微软官方口径一致；⚠️ 与版本无关）。',
                  "  NetDlInstallArgs = '/install /repair /passive /norestart';",
                  '  // 等安装器结束的【上限】（秒）：运行时安装可能较久 => 取宽裕但有限的值（10 分钟）。',
                  '  NetDlMaxWaitSeconds = 600;',
                  '  // 轮询间隔（毫秒）：每 2 秒看一次「退出码标记文件」有没有出现。',
                  '  NetDlPollMs = 2000;',
                  '  // 标记文件名：cmd 包一层，把安装器真实退出码写进 {tmp}\\<这个名字>。',
                  "  NetDlDoneFileName = 'lm-net10-install-rc.txt';"),
              'C1 新常量')

    # ---------------------------------------------------------------- C：就绪页那一行
    src = replace_line(src, 'chinesesimplified.RuntimeRequired=',
                       'chinesesimplified.RuntimeRequired=本程序需要 .NET 10 桌面运行时（x64），当前未安装，'
                       '安装程序已经尝试自动下载并静默安装，但没有成功。{break}{break}需要安装：%1{break}需要版本：%2'
                       '{break}官方直链（短，不折行）：{break}%3{break}{break}'
                       '· 这条直链已自动复制到剪贴板，可直接粘贴到浏览器，打开即开始下载；{break}'
                       '· 「重试下载 / Retry」= 再自动下载并静默安装一次（网络抖动时先试这个）；{break}'
                       '· 「用浏览器下载 / Open in browser」= 现在用浏览器打开上面的直链，【直接下载】运行时安装包'
                       '（装好后回到本向导，安装自动继续）；{break}'
                       '· 「稍后 / Later」= 不重试、不打开，直接继续安装。{break}{break}'
                       '⚠️ 本提示框的文字没法用鼠标框选，地址请用剪贴板取（弹框前已写好）。{break}'
                       '无论选哪个，安装都【不会】中止。',
                       'C2 RuntimeRequired 中文')
    src = replace_line(src, 'english.RuntimeRequired=',
                       'english.RuntimeRequired=This app needs the .NET 10 Desktop Runtime (x64), which is not '
                       'installed; Setup already tried to download and silently install it, but that did not work.'
                       '{break}{break}Install: %1{break}Version: %2{break}Official direct link (short, single line):'
                       '{break}%3{break}{break}'
                       '- This link has ALREADY been copied to your clipboard: paste it into your browser and the '
                       'download starts;{break}'
                       '- Retry = download and silently install it again (try this first if the network hiccuped);'
                       '{break}'
                       '- Open in browser = open the link above now and download the runtime installer right away '
                       '(then come back; setup continues automatically);{break}'
                       '- Later = do not retry and do not open it; continue the installation right away.{break}{break}'
                       'NOTE: this dialog text cannot be selected with the mouse, so use the clipboard (already '
                       'filled before this dialog).{break}Either way, setup will NOT be aborted.',
                       'C3 RuntimeRequired 英文')

    src = rep(src, 'chinesesimplified.RuntimeRequired=',
              join('chinesesimplified.RuntimeReadyMemo=附加任务：%1 —— 安装程序将自动下载并静默安装（%2）。{break}'
                   '若自动下载 / 安装失败，安装过程中会再问你是否改用浏览器下载；无论装没装上，安装都【不会】中止。',
                   'chinesesimplified.RuntimeRequired='),
              'C4 RuntimeReadyMemo 中文')
    src = rep(src, 'english.RuntimeRequired=',
              join('english.RuntimeReadyMemo=Additional task: %1 - Setup will download and silently install it for '
                   'you (%2).{break}If that does not work, Setup will ask whether to download it in your browser '
                   'instead; either way setup will NOT be aborted.',
                   'english.RuntimeRequired='),
              'C5 RuntimeReadyMemo 英文')

    # ---------------------------------------------------------------- C：UpdateReadyMemo
    src = rep(src,
              join('  if not Result then',
                   '    Result := NetTenOrNewerInSharedFxKey();',
                   'end;'),
              join('  if not Result then',
                   '    Result := NetTenOrNewerInSharedFxKey();',
                   'end;',
                   '',
                   '// ---- r25：就绪页（Ready Memo）提前告知「安装程序会自己下载并静默安装运行时」----',
                   '//   用户口径（2026-10-13，参照 LLT 会往就绪页追加一行「附加任务：<运行时> <版本>」）：',
                   '//     仅在【检测到缺运行时】时追加一行，让用户在点「安装」之前就知道会发生什么，',
                   '//     ⛔ 不会被突然出现的下载进度页吓到。⚠️ 版本串一律 .NET 10，⛔ 不是 LLT 的 8：',
                   '//       %1 = Net10RuntimeName（Microsoft Windows Desktop Runtime 10.0 (x64)）',
                   '//       %2 = Net10RuntimeVerHint（10.0.x …，取自程序自带 L-Mechrevo.runtimeconfig.json）',
                   '//   ⚠️ 定义了 UpdateReadyMemo 就必须自己重建默认内容（下面这套拼接就是 Inno 官方文档给的写法，',
                   '//      ⛔ 少一段就会丢掉就绪页原有信息）。',
                   '//   ⚠️ 检测用 IsNetDesktop10Installed()：只读注册表/目录，⛔ 无 UI、⛔ 无下载、⛔ 无副作用；',
                   '//      任何异常 => 不追加那一行（⛔ 绝不让就绪页因为这一段打不开）。',
                   '//   ⚠️ 静默安装下本函数根本不会被调用（没有向导页）=> 无需额外判据。',
                   'function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,',
                   '  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;',
                   'var',
                   '  S, Extra: String;',
                   '  Missing: Boolean;',
                   'begin',
                   "  S := '';",
                   "  if MemoUserInfoInfo <> '' then",
                   '    S := S + MemoUserInfoInfo + NewLine + NewLine;',
                   "  if MemoDirInfo <> '' then",
                   '    S := S + MemoDirInfo + NewLine + NewLine;',
                   "  if MemoTypeInfo <> '' then",
                   '    S := S + MemoTypeInfo + NewLine + NewLine;',
                   "  if MemoComponentsInfo <> '' then",
                   '    S := S + MemoComponentsInfo + NewLine + NewLine;',
                   "  if MemoGroupInfo <> '' then",
                   '    S := S + MemoGroupInfo + NewLine + NewLine;',
                   "  if MemoTasksInfo <> '' then",
                   '    S := S + MemoTasksInfo + NewLine + NewLine;',
                   '  Missing := False;',
                   '  try',
                   '    Missing := not IsNetDesktop10Installed();',
                   '  except',
                   '    Missing := False;',
                   "    Log('LM-NET-DL: 就绪页运行时检测异常 -> 不追加「附加任务」那一行（继续显示默认内容）');",
                   '  end;',
                   '  if Missing then',
                   '  begin',
                   "    Extra := ExpandMsg('RuntimeReadyMemo');",
                   "    StringChange(Extra, '%1', Net10RuntimeName);",
                   "    StringChange(Extra, '%2', Net10RuntimeVerHint);",
                   '    S := S + Extra + NewLine;',
                   "    Log('LM-NET-DL: 就绪页追加「附加任务」行：' + Net10RuntimeName + ' / ' + Net10RuntimeVerHint);",
                   '  end;',
                   '  Result := S;',
                   'end;'),
              'C6 UpdateReadyMemo')

    # ---------------------------------------------------------------- A：新代码块（下载/安装/弹框包装）
    new_block = join(
        '// ================================================================================================',
        '// r25：安装器【自己】下载 + 静默安装 .NET 10 桌面运行时（x64）',
        '// ================================================================================================',
        '// 用户口径（2026-10-13）：「不过我觉得可以加,下载失败再弹选择」',
        '//   => 点「安装」那一刻（PrepareToInstall -> PromptNet10Missing）里：',
        '//      (a) 先【自己下载 + 静默安装】：可见进度页（CreateDownloadPage）+ `/install /repair /passive /norestart`；',
        '//      (b) 只有【下载或安装失败】才弹 r24 那个选择框（直链 + 剪贴板 + 五档开浏览器链）；',
        '//      (c) 任何情况 PrepareToInstall 的 Result 恒为 \'\'（⛔ 绝不中止安装）；',
        '//      (d) 静默安装（/SILENT、/VERYSILENT）：⛔ 不下载、⛔ 不显示进度页、⛔ 不弹框，直接继续；',
        '//      (e) 重试两层：① 自动 = 主直链失败后换【备用直链】再试一次；',
        '//          ② 用户 = 选择框第 1 颗按钮「重试下载 / Retry」再走一遍 (a)。',
        '//   ⚠️ 版本相关一律 .NET 10（⛔ 不是 LLT 的 8）：要装的运行时名字/版本 =',
        '//      Net10RuntimeName / Net10RuntimeVerHint（取自程序自带 L-Mechrevo.runtimeconfig.json 的',
        '//      Microsoft.WindowsDesktop.App = 10.0.0 => 对外显示 10.0.x）；主直链 = aka.ms/dotnet/10.0/…',
        '//      （302 终址解析到 10.0.12）；备用直链 = 该终址（builds.…windowsdesktop-runtime-10.0.12-win-x64.exe）。',
        '//   ⚠️ 不校验 SHA-256：aka.ms 是 evergreen 短链（版本随上游滚动）=> 写死哈希会误判失败；',
        '//      这也是 Inno 官方例子对「下载运行时安装器」的常规做法（Add/DownloadTemporaryFile 的哈希参数传 \'\'）。',
        '//   ⚠️ 本段【只】在检测到 .NET 10（>= 10）缺失时才会被调用。',
        '',
        '// 探针钩子：WizardSilent() 在 Inno 6.7.1 里【不能被同名影子声明】（r24 实测），',
        '//   所以把「是不是静默安装」包成一层普通函数 => 探针可以影子掉它，从而断言 (d) 静默分支。',
        'function IsNetDlSilentMode(): Boolean;',
        'begin',
        '  Result := WizardSilent();',
        'end;',
        '',
        '// 下载页对象（可见进度页）：只在这里用；⚠️ 由 InitializeWizard 创建。',
        '//   ⚠️ Inno 的进度页【不占向导页序】（官方文档原文：progress pages are not displayed as part of the',
        '//      normal page sequence）=> 建了不显示也不影响正常安装流程，只有 Show 它时用户才看得到。',
        'var',
        '  NetDownloadPage: TDownloadWizardPage;',
        '  NetDlMaxWaitSec: Integer;   // 等待安装器的上限（秒）；0 = 还没初始化（见 NetDlRunInstaller）',
        '',
        'procedure InitializeWizard();',
        'begin',
        '  try',
        "    NetDownloadPage := CreateDownloadPage('正在准备安装 / Preparing to Install',",
        "      '正在下载附加文件（.NET 10 桌面运行时 x64）/ Downloading additional files', nil);",
        '    NetDownloadPage.ShowBaseNameInsteadOfUrl := True;',
        "    Log('LM-NET-DL: 下载页已创建（只有自动下载运行时的时候才会 Show 它）');",
        '  except',
        '    NetDownloadPage := nil;',
        "    Log('LM-NET-DL: 下载页创建失败 -> 自动下载退化为无页面下载 errmsg=' + GetExceptionMessage);",
        '  end;',
        'end;',
        '',
        '// (a-1) 把运行时安装包下载到 {tmp}。优先用【可见进度页】；⛔ 失败只返回 False（不把异常抛给调用方）。',
        '//   ⚠️ 这是全脚本【唯一】动 TDownloadWizardPage 的地方 => 探针把整个函数换成自己的替身',
        '//      （TDownloadWizardPage 是 Inno 内建对象：脚本里既构造不出替身对象，也影子不掉它的方法）。',
        '//   ErrMsg 出参只给日志/失败分支用，⛔ 本函数不弹任何框。',
        'function NetDlDownloadToTmp(const Url, BaseName: String; var ErrMsg: String): Boolean;',
        'var',
        '  Sz: Int64;',
        '  TmpPath: String;',
        'begin',
        '  Result := False;',
        "  ErrMsg := '';",
        "  TmpPath := ExpandConstant('{tmp}\\' + BaseName);",
        '  if not IsNetDlSilentMode() then',
        '  begin',
        '    // ① 首选：可见下载页（标题「正在准备安装」+ 内容「正在下载附加文件…」+ 停止下载按钮，Inno 标准样式）',
        '    if NetDownloadPage <> nil then',
        '    begin',
        '      try',
        '        NetDownloadPage.ShowBaseNameInsteadOfUrl := True;',
        '        NetDownloadPage.Clear;',
        "        NetDownloadPage.Add(Url, BaseName, '');   // 第 3 参 = RequiredSHA256：'' = 不校验（evergreen 直链）",
        '        NetDownloadPage.Show;',
        '        try',
        '          NetDownloadPage.Download;',
        '          Result := FileExists(TmpPath);',
        '        finally',
        '          NetDownloadPage.Hide;',
        '        end;',
        '        if not Result then',
        "          ErrMsg := '下载页返回后文件不存在：' + TmpPath;",
        '      except',
        '        Result := False;',
        "        ErrMsg := '下载页异常：' + GetExceptionMessage;",
        '        if NetDownloadPage.AbortedByUser then',
        "          ErrMsg := ErrMsg + '（用户点了「停止下载」）';",
        '      end;',
        '      if Result then',
        "        Log('LM-NET-DL: 下载成功（下载页）url=' + Url + ' -> ' + TmpPath)",
        '      else',
        "        Log('LM-NET-DL: 下载失败（下载页）url=' + Url + ' errmsg=' + ErrMsg);",
        '      if Result then',
        '        Exit;',
        '    end;',
        '  end;',
        '  // ② 退化：没有下载页对象（静默模式理论上走不到这里；或 CreateDownloadPage / Add 抛异常）',
        '  //    => 无界面下载。⚠️ DownloadTemporaryFile 失败会【抛异常】，这里吞掉并返回 False。',
        '  try',
        "    Sz := DownloadTemporaryFile(Url, BaseName, '', nil);",
        '    Result := FileExists(TmpPath);',
        '    if Result then',
        "      Log('LM-NET-DL: 下载成功（无页面降级）url=' + Url + ' bytes=' + IntToStr(Sz) + ' -> ' + TmpPath)",
        '    else',
        '    begin',
        "      ErrMsg := '下载后文件不存在：' + TmpPath;",
        "      Log('LM-NET-DL: 下载失败（无页面降级）url=' + Url + ' errmsg=' + ErrMsg);",
        '    end;',
        '  except',
        '    Result := False;',
        "    ErrMsg := '下载异常：' + GetExceptionMessage;",
        "    Log('LM-NET-DL: 下载失败（无页面降级）url=' + Url + ' errmsg=' + ErrMsg);",
        '  end;',
        'end;',
        '',
        '// (a-2) 静默安装下载下来的运行时安装包，并且【等待有上限】。',
        '//   为什么用 cmd.exe 包一层：Inno 6.7.1 的 Exec 没有超时参数（ewWaitUntilTerminated = 无限等），',
        '//   而运行时安装可能要几分钟 => 用',
        '//     cmd /v:on /c ""<exe>" <args> & echo !ERRORLEVEL! > <标记文件>"',
        '//   + 轮询标记文件：① 拿到真实退出码；② 超过上限就 taskkill 掉安装器并判失败。',
        '//   ⚠️ 必须 /v:on + !ERRORLEVEL!（延迟展开）：`cmd /c "app & echo %ERRORLEVEL%"` 里的 %ERRORLEVEL%',
        '//      在解析整行时就展开了 => 拿到的是【上一行】的退出码，恒为错值。',
        '//   返回 True = 退出码 0（成功）或 3010/1641（成功但需重启）；False = 启动不了 / 超时 / 非零。',
        'function NetDlRunInstaller(const SetupExe: String; var Rc: Integer; var ErrMsg: String): Boolean;',
        'var',
        '  DoneFile, CmdLine, S: String;',
        '  SA: AnsiString;   // ⚠️ LoadStringFromFile 只认 AnsiString（Inno 6.7.1 实测：String 报 Type mismatch）',
        '  StartRc, Waited: Integer;',
        'begin',
        '  Result := False;',
        '  Rc := -1;',
        "  ErrMsg := '';",
        '  // 上限的来源 NetDlMaxWaitSeconds（600s = 10 分钟）；这里是【惰性初始化】：',
        '  //   ⚠️ 写成变量是为了让探针能把它调小（否则超时用例要真等 10 分钟）。',
        '  if NetDlMaxWaitSec <= 0 then',
        '    NetDlMaxWaitSec := NetDlMaxWaitSeconds;',
        "  DoneFile := ExpandConstant('{tmp}\\' + NetDlDoneFileName);",
        '  DeleteFile(DoneFile);',
        '  CmdLine := \'/v:on /c ""\' + SetupExe + \'" \' + NetDlInstallArgs',
        '    + \' & echo !ERRORLEVEL! > "\' + DoneFile + \'""\';',
        "  Log('LM-NET-DL: 安装开始 exe=' + SetupExe + ' args=' + NetDlInstallArgs",
        "    + ' 等待上限=' + IntToStr(NetDlMaxWaitSec) + 's');",
        "  if not Exec(ExpandConstant('{cmd}'), CmdLine, '', SW_SHOW, ewNoWait, StartRc) then",
        '  begin',
        "    ErrMsg := 'Exec 启动安装器失败 rc=' + IntToStr(StartRc);",
        "    Log('LM-NET-DL: 安装失败（启动不了）errmsg=' + ErrMsg);",
        '    Exit;',
        '  end;',
        '  Waited := 0;',
        '  while (Waited < NetDlMaxWaitSec) and (not FileExists(DoneFile)) do',
        '  begin',
        '    Sleep(NetDlPollMs);',
        '    Waited := Waited + (NetDlPollMs div 1000);',
        '  end;',
        '  if not FileExists(DoneFile) then',
        '  begin',
        "    ErrMsg := '安装超时（>' + IntToStr(NetDlMaxWaitSec) + 's）';",
        "    Log('LM-NET-DL: 安装失败（超时）-> taskkill 安装器 image=' + NetDlBaseName);",
        "    Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ' + NetDlBaseName + ' /F /T', '',",
        '      SW_HIDE, ewWaitUntilTerminated, StartRc);',
        '    Exit;',
        '  end;',
        '  // 读退出码（cmd 已经把 !ERRORLEVEL! 写进标记文件）',
        "  SA := '';",
        '  if LoadStringFromFile(DoneFile, SA) then',
        '  begin',
        '    S := SA;',
        '    S := Trim(S);',
        '    Rc := StrToIntDef(S, -1);',
        '  end',
        '  else',
        "    ErrMsg := '读不到退出码文件：' + DoneFile;",
        "  Log('LM-NET-DL: 安装结束 rc=' + IntToStr(Rc));",
        '  if (Rc = 0) or (Rc = 3010) or (Rc = 1641) then',
        '    Result := True',
        '  else',
        "    ErrMsg := '安装器退出码 ' + IntToStr(Rc);",
        'end;',
        '',
        '// (a) 走完「下载（主直链 -> 备用直链）+ 静默安装」，成功返回 True。',
        '//   返回 False => 调用方去弹选择框（r24 那个）；⛔ 本函数不弹任何框、⛔ 不抛异常、⛔ 不中止安装。',
        'function NetDlTryInstallRuntime(): Boolean;',
        'var',
        '  Attempt, Rc: Integer;',
        '  Url, ErrMsg: String;',
        'begin',
        '  Result := False;',
        '  if IsNetDlSilentMode() then',
        '  begin',
        "    Log('LM-NET-DL: 静默安装 -> 跳过自动下载/安装（⛔ 不下载、⛔ 不显示下载页、⛔ 不弹框），直接继续安装');",
        '    Exit;',
        '  end;',
        '  Rc := -1;',
        '  for Attempt := 1 to 2 do',
        '  begin',
        '    if Attempt = 1 then',
        '      Url := Net10DownloadDirectUrl   // 主：aka.ms 短链（用户实测可下载）',
        '    else',
        '      Url := Net10DownloadFallbackUrl; // 备用：302 终址（builds.…10.0.12…）',
        "    Log('LM-NET-DL: 开始第 ' + IntToStr(Attempt) + '/2 次下载 url=' + Url);",
        "    DeleteFile(ExpandConstant('{tmp}\\' + NetDlBaseName));",
        '    if not NetDlDownloadToTmp(Url, NetDlBaseName, ErrMsg) then',
        "      Log('LM-NET-DL: 第 ' + IntToStr(Attempt) + ' 次下载失败 errmsg=' + ErrMsg)",
        '    else',
        '    begin',
        "      Log('LM-NET-DL: 第 ' + IntToStr(Attempt) + ' 次下载成功 -> 进入静默安装');",
        "      if NetDlRunInstaller(ExpandConstant('{tmp}\\' + NetDlBaseName), Rc, ErrMsg) then",
        '      begin',
        '        Result := True;',
        "        Log('LM-NET-DL: 运行时静默安装成功 rc=' + IntToStr(Rc) + ' -> 不再弹选择框');",
        '        Exit;',
        '      end;',
        "      Log('LM-NET-DL: 静默安装失败 errmsg=' + ErrMsg);",
        '      // 安装失败 => 换备用直链重下一次也没意义（同一个包）=> 直接交给选择框（用户可点「重试下载」）',
        '      Break;',
        '    end;',
        '  end;',
        "  Log('LM-NET-DL: 自动下载/安装这条路没成功 -> 交给选择框（r24 五档链不变）');",
        'end;',
        '',
        '// (b) 失败时的选择框：只负责弹框 + 返回按钮答案（⛔ 不做别的）。',
        '//   ⚠️ 单独拆一个函数：TaskDialogMsgBox 在 Inno 6.7.1 里不能被同名影子声明（r24 实测），',
        '//      探针只能影子【这个包装函数】=> 才能断言「自动这条路失败后确实走到了选择框」。',
        '//   三颗按钮（TaskDialogMsgBox 只认 MB_YESNOCANCEL 这组返回码）：',
        '//     第1颗 = IDYES    「重试下载 / Retry」          -> 调用方再走一遍自动下载 + 静默安装（对齐 LLT 的重试）',
        '//     第2颗 = IDNO     「用浏览器下载 / Open in browser」-> r24 五档链打开官方直链',
        '//     第3颗 = IDCANCEL 「稍后 / Later」              -> 什么都不做，直接继续安装',
        '//   ⚠️ 直链在弹框【之前】就写进剪贴板（见 PromptNet10Missing）=> 文案里「已复制」成立；',
        '//      本框正文【不可框选】（r24 用户实测）=> 取地址就靠「弹框前那次剪贴板写入」+ 打开失败时的可见提示。',
        'function AskNet10MissingDialog(): Integer;',
        'var',
        '  Labels: TArrayOfString;',
        '  HelpText: String;',
        'begin',
        "  HelpText := ExpandMsg('RuntimeRequired');",
        "  StringChange(HelpText, '%1', Net10RuntimeName);",
        "  StringChange(HelpText, '%2', Net10RuntimeVerHint);",
        "  StringChange(HelpText, '%3', Net10DownloadDirectUrl);",
        '  SetArrayLength(Labels, 3);',
        "  Labels[0] := '重试下载 / Retry';",
        "  Labels[1] := '用浏览器下载 / Open in browser';",
        "  Labels[2] := '稍后 / Later';",
        "  Result := TaskDialogMsgBox('缺少 .NET 10 桌面运行时（x64） / .NET 10 Desktop Runtime (x64) required',",
        '    HelpText, mbInformation, MB_YESNOCANCEL, Labels, IDYES);',
        'end;',
        '')

    src = rep(src,
              '// 「弹框拿到答案之后干什么」拆成独立函数（⛔ 与弹框调用解耦，好在探针里逐条喂答案）：',
              new_block
              + '// 「弹框拿到答案之后干什么」拆成独立函数（⛔ 与弹框调用解耦，好在探针里逐条喂答案）：',
              'A12 新代码块插入')

    # ---------------------------------------------------------------- A：Handle 改造
    src = rep(src,
              join('//   IDYES    = 「下载 / Download」=> 打开官方直链（第1档 系统 shell 关联（主档）-> 第2档 原用户身份 ->',
                   '//              第3档 explorer.exe 代转（路径先校验）-> 第4档 显式默认浏览器 -> 第5档 剪贴板 + 可见提示，ewNoWait）；',
                   '//              全部档位失败 => 补一条【可见提示】（原来只写日志 = 用户看到的就是"点了没反应"）',
                   '//   IDNO     = 「复制链接 / Copy link」=> 只再写一次剪贴板 + 一句确认，⛔ 不开浏览器',
                   '//   其它     = 「稍后 / Later」/ Esc / 关窗 => 什么都不做'),
              join('//   IDYES    = 「重试下载 / Retry」=> ⛔ 不在本函数里执行（调用方 PromptNet10Missing 再走一遍自动下载+安装）',
                   '//   IDNO     = 「用浏览器下载 / Open in browser」=> 打开官方直链（第1档 系统 shell 关联（主档）-> 第2档 原用户身份 ->',
                   '//              第3档 explorer.exe 代转（路径先校验）-> 第4档 显式默认浏览器 -> 第5档 剪贴板 + 可见提示，ewNoWait）；',
                   '//              全部档位失败 => 补一条【可见提示】（原来只写日志 = 用户看到的就是"点了没反应"）',
                   '//   其它     = 「稍后 / Later」/ Esc / 关窗 => 什么都不做'),
              'A13 Handle 注释头')

    src = rep(src,
              join('function HandleNet10MissingAnswer(const Answer: Integer): Boolean;',
                   'var',
                   '  Copied: Boolean;',
                   'begin',
                   "  Copied := CopyNet10UrlToClipboard('弹框之前');",
                   '  if Answer = IDYES then',
                   '  begin',
                   "    Log('LM-NET: 用户点了「下载 / Download」-> 打开官方直链，然后立即继续安装');"),
              join('//   ⚠️ r25：答案码的含义变了（按钮从「下载/复制链接/稍后」改成「重试下载/用浏览器下载/稍后」）：',
                   '//      IDYES = 重试（由调用方执行）· IDNO = 用浏览器下载（五档链）· 其它 = 稍后。',
                   '//      ⚠️ 剪贴板那一次写入搬到调用方（弹框【之前】写，文案里的「已复制」才成立）=> 本函数只收结果。',
                   'function HandleNet10MissingAnswer(const Answer: Integer; const CopiedIn: Boolean): Boolean;',
                   'var',
                   '  Copied: Boolean;',
                   'begin',
                   '  Copied := CopiedIn;',
                   '  if Answer = IDNO then',
                   '  begin',
                   "    Log('LM-NET: 用户点了「用浏览器下载 / Open in browser」-> 打开官方直链，然后立即继续安装');"),
              'A14 Handle 入口')

    src = rep(src,
              join('  end',
                   '  else if Answer = IDNO then',
                   '  begin',
                   "    Log('LM-NET: 用户点了「复制链接 / Copy link」-> 直接继续安装（直链在弹框前那次复制里已经进了剪贴板）');",
                   '    if Copied then',
                   "      MsgBox('直链已复制到剪贴板，可直接粘贴到浏览器地址栏（打开即开始下载）：' + #13#10",
                   '        + Net10DownloadDirectUrl, mbInformation, MB_OK)',
                   '    else',
                   "      MsgBox('复制到剪贴板失败（可能被别的程序占用），请稍后再点一次「复制链接」。', mbInformation, MB_OK);",
                   '  end',
                   '  else',
                   "    Log('LM-NET: 用户点了「稍后 / Later」-> 不开浏览器，直接【继续安装】');"),
              join('  end',
                   '  else if Answer = IDYES then',
                   '    // 「重试下载」的执行在调用方 PromptNet10Missing 里（要循环重试 + 需要知道成功与否）',
                   "    Log('LM-NET: 用户点了「重试下载 / Retry」-> 重试动作由调用方执行（再走一遍自动下载 + 静默安装）')",
                   '  else',
                   "    Log('LM-NET: 用户点了「稍后 / Later」-> 不开浏览器，直接【继续安装】');"),
              'A15 Handle 尾部分支')

    src = rep(src,
              join('      //      这里再加一道 WizardSilent() 判据，防止将来有人改调用顺序时把窗弹进静默流程。',
                   '      if not WizardSilent() then'),
              join('      //      这里再加一道静默判据（IsNetDlSilentMode = 对 WizardSilent 的探针可影子包装），',
                   '      //      防止将来有人改调用顺序时把窗弹进静默流程。',
                   '      if not IsNetDlSilentMode() then'),
              'A16 Handle 静默判据')

    # ---------------------------------------------------------------- A：PromptNet10Missing 改造
    src = rep(src,
              join('//   ⛔ 本过程【没有任何】下载文件 / 执行运行时安装器的动作 —— 写剪贴板 + 提示 + 打开直链就是 .NET 的全部动作。',
                   'procedure PromptNet10Missing();',
                   'var',
                   '  Answer: Integer;',
                   '  Labels: TArrayOfString;',
                   '  HelpText: String;',
                   '  Copied: Boolean;',
                   'begin'),
              join('//   ⚠️ 2026-10-13（r25）：本过程现在【有】下载文件 / 执行运行时安装器的动作了（用户口径',
                   '//      「不过我觉得可以加,下载失败再弹选择」）—— 顺序：',
                   '//        ① 静默安装（/SILENT、/VERYSILENT）=> ⛔ 不下载、⛔ 不显示下载页、⛔ 不弹框，直接继续；',
                   '//        ② 先【自己下载 + 静默安装】（NetDlTryInstallRuntime；成功 => ⛔ 不弹任何框）；',
                   '//        ③ 只有失败才弹选择框（AskNet10MissingDialog）+ 逐条执行按钮动作；',
                   '//           「重试下载」=> 回到 ②（用户可重复点；点「稍后」/「用浏览器下载」则离开循环）。',
                   '//        ④ ⛔ 任何分支都不中止安装（PrepareToInstall 的 Result 恒为 \\\'\\\'）。',
                   'procedure PromptNet10Missing();',
                   'var',
                   '  Answer: Integer;',
                   '  Ready, Copied: Boolean;',
                   'begin'),
              'A17 Prompt 头')

    src = rep(src,
              join('  if WizardSilent() then',
                   '  begin',
                   "    Log('LM-NET: 静默安装 -> 不弹框、不开浏览器，直接【继续安装】');",
                   '    Exit;',
                   '  end;',
                   "  HelpText := ExpandMsg('RuntimeRequired');",
                   "  StringChange(HelpText, '%1', Net10RuntimeName);",
                   "  StringChange(HelpText, '%2', Net10RuntimeVerHint);",
                   "  StringChange(HelpText, '%3', Net10DownloadDirectUrl);",
                   '  SetArrayLength(Labels, 3);',
                   "  Labels[0] := '下载 / Download';",
                   "  Labels[1] := '复制链接 / Copy link';",
                   "  Labels[2] := '稍后 / Later';",
                   "  Answer := TaskDialogMsgBox('缺少 .NET 10 桌面运行时（x64） / .NET 10 Desktop Runtime (x64) required',",
                   '    HelpText, mbInformation, MB_YESNOCANCEL, Labels, IDYES);',
                   '  Copied := HandleNet10MissingAnswer(Answer);',
                   '  if not Copied then',
                   "    Log('LM-NET-CLIP: 本次没能把直链放进剪贴板 -> 文案里的「已复制」对该用户不成立，靠「复制链接」按钮兜底');",
                   "  Log('LM-NET: 提示流程结束（结果一律不返回非空 = 安装继续）');"),
              join('  if IsNetDlSilentMode() then',
                   '  begin',
                   "    Log('LM-NET: 静默安装 -> 不下载、不显示下载页、不弹框，直接【继续安装】');",
                   '    Exit;',
                   '  end;',
                   '  // ② 先自己下载 + 静默安装（失败才会弹选择框）',
                   "  Log('LM-NET-DL: 自动下载 + 静默安装开始（失败才会弹选择框）');",
                   '  Ready := NetDlTryInstallRuntime();',
                   '  // ③ 失败才弹选择框；「重试下载」=> 回到 ②（循环由用户按键驱动，⛔ 不是死循环）',
                   '  while not Ready do',
                   '  begin',
                   "    Copied := CopyNet10UrlToClipboard('弹框之前（自动下载/安装失败）');",
                   '    if not Copied then',
                   "      Log('LM-NET-CLIP: 直链没能放进剪贴板（弹框前）-> 文案里的「已复制」对该用户不成立');",
                   '    Answer := AskNet10MissingDialog();',
                   '    if Answer = IDYES then',
                   '    begin',
                   "      Log('LM-NET: 用户点了「重试下载 / Retry」-> 再走一遍自动下载 + 静默安装');",
                   '      Ready := NetDlTryInstallRuntime();',
                   '      if not Ready then',
                   "        Log('LM-NET-DL: 用户重试后仍然失败 -> 再弹一次选择框（可继续重试，或改用浏览器/稍后）');",
                   '    end',
                   '    else',
                   '    begin',
                   '      HandleNet10MissingAnswer(Answer, Copied);',
                   "      Log('LM-NET: 选择框流程结束（结果一律不返回非空 = 安装继续）');",
                   '      Break;',
                   '    end;',
                   '  end;',
                   '  if Ready then',
                   "    Log('LM-NET-DL: 运行时已由安装器自动装好（或用户重试成功）-> 安装继续');"),
              'A18 Prompt 体')

    # ---------------------------------------------------------------- 最终断言（结构哨兵）
    # 启动壳：⛔ 不再有任何【功能性】引用（剩下的只允许出现在说明性注释里）
    assert 'Source: "files\\L-Mechrevo-Launcher.exe"' not in src, '还有 [Files] 启动壳条目'
    assert 'Filename: "{app}\\L-Mechrevo-Launcher.exe"' not in src, '还有 [Run]/[Icons] 启动壳指向'
    assert "LmLauncherExe = " not in src, 'LmLauncherExe 常量声明残留'
    idx = 0
    occ = 0
    while True:
        idx = src.find('L-Mechrevo-Launcher.exe', idx)
        if idx < 0:
            break
        occ += 1
        ls = src.rfind('\n', 0, idx) + 1
        line = src[ls:src.find('\n', idx)].strip()
        assert line.startswith(';') or line.startswith('//'), '启动壳引用出现在非注释行：%s' % line[:80]
        idx += 1
    print('启动壳残留提及（仅注释/说明）= %d 处' % occ)
    for must in ('function IsNetDlSilentMode(): Boolean;',
                 'function NetDlTryInstallRuntime(): Boolean;',
                 'function AskNet10MissingDialog(): Integer;',
                 'function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,',
                 'procedure InitializeWizard();',
                 "Labels[0] := '重试下载 / Retry';",
                 'NetDlMaxWaitSeconds = 600;',
                 'Ready := NetDlTryInstallRuntime();',
                 "Filename: \"{app}\\L-Mechrevo.exe\"; Description: \"{cm:LaunchApp}\""):
        assert must in src, '缺少：%s' % must
    # PrepareToInstall 仍然「恒不中止」：函数里不得出现非空 Result 赋值
    i = src.index('function PrepareToInstall(var NeedsRestart: Boolean): String;')
    body = src[i:src.index('\r\nend;', i)]
    assert 'Result := \'\';' in body, 'PrepareToInstall 丢了 Result := \'\''
    assert body.count('Result :=') == 1, 'PrepareToInstall 里出现了第 2 处 Result 赋值（必须恒为 \'\'）'
    # 观测量
    assert src.count("Log('LM-NET-DL:") >= 20, "LM-NET-DL 日志条数偏少：%d" % src.count("Log('LM-NET-DL:")
    print('LM-NET-DL log statements = %d' % src.count("Log('LM-NET-DL:"))

    # ---------------------------------------------------------------- 写 TEMP -> 校验 -> 替换
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_install_script.iss')
    with io.open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(src)
    with io.open(tmp, encoding='utf-8', newline='') as f:
        back = f.read()
    assert back == src, 'TEMP 回读不一致'
    assert back.count(CRLF) == src.count(CRLF)
    assert back.count('\n') == src.count('\n'), '换行数不一致'
    assert back.count('\n') == back.count(CRLF), '出现裸 LF（必须全 CRLF）'
    print('temp verified: %s  size=%d  lines=%d' % (tmp, len(back.encode('utf-8')), back.count(CRLF) + 1))

    shutil.copyfile(tmp, ISS)
    post_sha = sha256_of(ISS)
    print('post sha256 = %s  size=%d' % (post_sha, os.path.getsize(ISS)))
    print('pre size    = %d' % len(orig.encode('utf-8')))
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
