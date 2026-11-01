# -*- coding: utf-8 -*-
r"""
r25 补丁 3：文案与打包须知（⛔ 不打包，只把「启动壳」相关说法改成 r25 的事实）。

改动：
  1) 更新日志.txt（根）+ installer-src\files\更新日志.txt（两者原本逐字节相同）：
     四、安装器 里的 3 条（原第 1 / 2 / 5 条）改成 r25 的口径：
       · 安装器自己下载 + 静默安装（可见进度页 / 上限 10 分钟 / 备用直链 / 失败才弹框 / 重试）
       · 静默安装不下载不弹框；缺运行时兜底改成 apphost 自带提示（⛔ 启动壳已删）
       · 就绪页新增「附加任务」一行；检测判据只剩安装器一处（大版本 >= 10）
  2) installer-src\打包须知.txt 第六节（「必须先构建启动壳」）改成「⛔ 不再需要启动壳」。

纪律：读 -> 断言（SHA + 行首锚点唯一）-> 写 TEMP -> 校验 -> 替换。
"""
import hashlib
import io
import os
import shutil
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT_LOG = r'D:\openLM\更新日志.txt'
FILES_LOG = r'D:\openLM\installer-src\files\更新日志.txt'
NOTICE = r'D:\openLM\installer-src\打包须知.txt'
CRLF = '\r\n'
EXPECT_LOG_SHA = 'E853DC669E9E34A6F0127EDF131C246B743406CED67D10593CAEFA3A9D4DDAFB'
EXPECT_FILES_LOG_SHA = EXPECT_LOG_SHA
EXPECT_NOTICE_SHA = None  # 建立基线：只在第一次运行时校验（这里只做「行首锚点唯一」+ 明文断言）
POST_LOG_SHA = '4D1DC8683B64555E07ED890BC8C2804C892360288317C2CDF277DD8604CB61D0'


def sha256_of(p):
    with open(p, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest().upper()


def replace_line(src, prefix, newline, label):
    n = src.count(prefix)
    assert n == 1, '[%s] 行首锚点命中 %d 次（必须 1 次）' % (label, n)
    i = src.index(prefix)
    assert i == 0 or src[i - 1] == '\n', '[%s] 不在行首' % label
    j = src.index(CRLF, i)
    return src[:i] + newline + src[j:]


L37 = (
    '1. **缺 .NET 10 桌面运行时改成「安装器自己下载并静默安装」**：点「安装」那一刻（`PrepareToInstall`）'
    '先由安装程序**自己下载**运行时安装包（给出**可见的下载进度页**，可点「停止下载」）并**静默安装**'
    '（`/install /repair /passive /norestart`；等待上限 **10 分钟**，超时会结束安装器进程并判失败）；'
    '主直链 `https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`（用户实测可正常下载），'
    '失败会自动换**备用直链**（`https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/'
    'windowsdesktop-runtime-10.0.12-win-x64.exe`）再试一次。**只有下载或安装失败**时才弹选择框，'
    '三颗按钮 = **「重试下载 / Retry」**（再自动下载 + 静默安装一次）、'
    '**「用浏览器下载 / Open in browser」**（用你自己的浏览器打开官方直链、直接下载运行时安装包，'
    '随后立即继续安装）、**「稍后 / Later」**（直接继续）。要装的是 '
    '**`Microsoft Windows Desktop Runtime 10.0.x（x64）`**（本程序 runtimeconfig 要求 '
    '`Microsoft.WindowsDesktop.App ≥ 10.0.0`）。⚠️ 提示框正文**没法用鼠标框选**（早前说「正文可选中」不实），'
    '所以那条直链在**弹框之前**就会被自动复制到**剪贴板**，直接粘贴即可。')

L38 = (
    '2. **无论运行时有没有装上，安装都照常继续**：点「稍后 / Later」、下载失败、安装失败、'
    '**等待超时（10 分钟）**、取消，都**不会中止安装**；静默安装（`/SILENT`、`/VERYSILENT`）'
    '**不下载、不显示下载页、一个框都不弹**，直接继续。若运行时最终仍缺失（下载失败后又选了「稍后」），'
    '双击 `L-Mechrevo.exe` 时由 **.NET 自带的 apphost 提示**兜底（微软那句英文提示焊在 exe 里，改不了）。'
    '⚠️ 本版**删除了原先的「启动壳」（`L-Mechrevo-Launcher.exe`）**：安装阶段已经能自动补齐运行时，'
    '壳属于冗余，而且它自身还带一类缺陷（explorer 错误路径）；源码工程 `tools\\LmLauncherStub\\` 保留，'
    '只是不再随包发布。')

L41 = (
    '5. **安装向导不再一启动就弹缺失提示，改成先在就绪页告知**：先正常打开安装向导；只要检测到缺运行时，'
    '**「准备安装」就绪页**上就会多一行「**附加任务：Microsoft Windows Desktop Runtime 10.0.x（x64）'
    '—— 安装程序将自动下载并静默安装**」，让用户在点「安装」之前就知道会发生什么；真正动手是在'
    '**点「安装」那一步**（`PrepareToInstall`）。**检测判据 = 「大版本 ≥ 10」**（现在只有安装器一处判定，'
    '⛔ 启动壳已删除）：程序 runtimeconfig 要求 `Microsoft.WindowsDesktop.App ≥ 10.0.0` 且未写 rollForward'
    '（.NET 缺省 = Minor，允许向上扩到更高主版本）=> 装了 10.x / 11.x 都**不再提示**；'
    '⚠️ 只装 6.x/7.x/8.x/9.x 仍会提示（net10.0 程序装 8.x 起不来）。')


def patch_log(path, expect_sha):
    pre = sha256_of(path)
    print('%s  pre sha256=%s size=%d' % (os.path.basename(path), pre, os.path.getsize(path)))
    if pre == POST_LOG_SHA:
        print('   已是 r25 文案（跳过）')
        return
    assert pre == expect_sha, '前置 SHA 不符：%s != %s' % (pre, expect_sha)
    src = io.open(path, encoding='utf-8', newline='').read()
    src = replace_line(src, '1. **缺 .NET 10 运行时改为', L37, 'log L37')
    src = replace_line(src, '2. **点「稍后 / Later」、超时或取消', L38, 'log L38')
    src = replace_line(src, '5. **安装向导不再一启动就弹缺失提示', L41, 'log L41')
    assert '启动壳再给一次中文提示' not in src
    assert '检测判据同步放宽为「大版本 ≥ 10」（安装器与启动壳两处一致）' not in src
    assert '安装器自己下载并静默安装' in src and '重试下载 / Retry' in src
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_log_' + os.path.basename(path))
    with io.open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(src)
    back = io.open(tmp, encoding='utf-8', newline='').read()
    assert back == src, 'TEMP 回读不一致'
    assert back.count('\n') == back.count(CRLF), '出现裸 LF'
    shutil.copyfile(tmp, path)
    print('   post sha256=%s size=%d' % (sha256_of(path), os.path.getsize(path)))


NOTICE_NEW = CRLF.join([
    '【六、（2026-10-13 r25 更新）⛔ 不再需要启动壳】',
    '- r25 起安装包**不再包含**启动壳：`install_script.iss` 的 `[Files]` 里那行',
    '  `Source: "files\\L-Mechrevo-Launcher.exe"` 已删除，`[Run]` 与两条 `[Icons]`（开始菜单 / 桌面）',
    '  都直接指向 `{app}\\L-Mechrevo.exe`。',
    '- 原因：① 缺 .NET 10 桌面运行时由**安装阶段自动下载 + 静默安装**解决（失败才弹选择框）；',
    '  ② 万一装完仍缺，双击程序时 `.NET apphost` 自带的英文提示能兜底；',
    '  ③ 壳自身还有一类缺陷（它那侧 explorer 的错误路径，r24 才修）=> 删掉更干净、少一类维护面。',
    '  ⚠️ 源码工程 `tools\\LmLauncherStub\\` **保留**（只是不再随包发布、不再需要 `dotnet build`）；',
    '  已构建的 `installer-src\\files\\L-Mechrevo-Launcher.exe` 也已从 `files\\` 移出',
    '  （备份在 `backup\\iss\\r25_20261013_pre_netdl_launcher_removal\\`）。',
    '- ⛔ 打包流程里**删掉**下面这一步（留着也无害：不会再被任何 iss 引用）：',
    "    & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\tools\\LmLauncherStub\\LmLauncherStub.csproj' -c Release",
])


def patch_notice(path):
    pre = sha256_of(path)
    print('%s  pre sha256=%s size=%d' % (os.path.basename(path), pre, os.path.getsize(path)))
    src = io.open(path, encoding='utf-8', newline='').read()
    i = src.index('【六、打包前置（2026-10-13 新增）：必须先构建启动壳】')
    assert src.count('【六、打包前置') == 1
    # 该节一直占到文件末尾（第六节是最后一节）
    assert src[i:].count('\r\n【') == 0, '第六节后面还有别的小节，改用更细的锚点'
    src = src[:i] + NOTICE_NEW + CRLF
    assert '[Files] 现在多了一行' not in src, '旧的「必须构建启动壳」说明还在'
    assert '不再需要启动壳' in src
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_notice.txt')
    with io.open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(src)
    back = io.open(tmp, encoding='utf-8', newline='').read()
    assert back == src, 'TEMP 回读不一致'
    assert back.count('\n') == back.count(CRLF), '出现裸 LF'
    shutil.copyfile(tmp, path)
    print('   post sha256=%s size=%d' % (sha256_of(path), os.path.getsize(path)))


def main():
    patch_log(ROOT_LOG, EXPECT_LOG_SHA)
    patch_log(FILES_LOG, EXPECT_FILES_LOG_SHA)
    a, b = sha256_of(ROOT_LOG), sha256_of(FILES_LOG)
    assert a == b, '两份更新日志没保持一致：%s != %s' % (a, b)
    print('两份更新日志逐字节一致 = %s' % a)
    patch_notice(NOTICE)
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
