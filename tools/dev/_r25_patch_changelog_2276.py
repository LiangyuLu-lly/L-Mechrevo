# -*- coding: utf-8 -*-
r"""r25：就地改写两份 更新日志.txt 里【已有的 0.291.7.6 一节】（倒叙，就在文件最顶部）。

约束：
  - 只动「三十、0.291.7.6 beta22.7.6」这一节；其后 0.291.7.5 / 0.291.7.4 … 是已发布历史，一字不动；
  - ⛔ 不出现「启动壳」「启动时中文提示」「中文提示」等说法（壳已删）；
  - ⛔ 不提 .iss 损坏/恢复、⛔ 不提探针事故；
  - 两份必须逐字节一致。

纪律：读字节 -> 断言（前置 SHA + 行首锚点唯一）-> 写 TEMP -> 回读校验 -> os.replace。
⚠️ 无 BOM、纯 CRLF，编码保持原样。
"""
import hashlib, io, os, shutil, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT_LOG = r'D:\openLM\更新日志.txt'
FILES_LOG = r'D:\openLM\installer-src\files\更新日志.txt'
CRLF = '\r\n'

EXPECT_SHA = '4D1DC8683B64555E07ED890BC8C2804C892360288317C2CDF277DD8604CB61D0'
HEADER = '三十、0.291.7.6 beta22.7.6（2026-10-13）'
NEXT = '二十九、0.291.7.5 beta22.7.5（2026-10-13）'

BODY = [
    '一、安装器（.NET 10 运行时）',
    '1. **缺 .NET 10 桌面运行时改成「安装器自己下载 + 静默安装」**：安装向导照常先打开；只要检测到缺运行时，'
    '**「准备安装」就绪页**上就会多一行「**附加任务：Microsoft Windows Desktop Runtime 10.0.x（x64）'
    '—— 安装程序将自动下载并静默安装**」，让用户在点「安装」之前就知道会发生什么。真正动手是在'
    '**点「安装」那一刻**：安装程序**自己下载**运行时安装包并**静默安装**'
    '（`/install /repair /passive /norestart`；等待上限 **10 分钟**、每 2 秒轮询一次，超时会按程序自己的'
    '文件名结束安装器进程并判失败）。**装上了就不打扰你**，安装照常往下走。主直链 '
    '`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`，失败自动换**备用直链**'
    '（`https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/'
    'windowsdesktop-runtime-10.0.12-win-x64.exe`）再试一次，仍拿不到就退回到临时文件下载兜底。'
    '**检测判据 = 「大版本 ≥ 10」**：装了 10.x / 11.x 都**不再提示**；⚠️ 只装 6.x/7.x/8.x/9.x 仍会提示'
    '（net10.0 程序装 8.x 起不来）。',
    '2. **只有下载或安装失败才会弹框**，三颗按钮 = **「重试下载 / Retry」**（再自动下载 + 静默安装一次）、'
    '**「用浏览器下载 / Open in browser」**（用你自己的浏览器打开官方直链、直接下载运行时安装包，'
    '随后立即继续安装）、**「稍后 / Later」**（直接继续装下去）。⚠️ 弹框正文**没法用鼠标框选**，'
    '所以那条直链在**弹框之前**就已经自动复制到**剪贴板**，直接粘贴即可。',
    '3. **无论运行时有没有装上，安装都照常继续**：点「稍后 / Later」、下载失败、安装失败、**等待超时**、'
    '取消，都**不会中止安装**；静默安装（`/SILENT`、`/VERYSILENT`）**不下载、不显示下载页、一个框都不弹**，'
    '直接继续。',
    '4. 修复点「下载」有时不打开浏览器（现在会以你的身份打开）。',
    '二、修复',
    '1. 修复**校色开关「关了又自己打开」**：开关状态现在按实际保存的设置显示，不再由档位反推。',
    '2. 修复**MiniLED（单分区 / 多分区）开关在支持的机型上不显示**，并加了自愈，偶发读不到时自动重试。',
    '3. 修复**核显省电切不过去**：切换后会真实确认结果，失败会明确提示（不再「看起来切了其实没切」）；'
    '独显有程序占用时可选择强制切换，带确认与失败回滚。',
    '三、安装包',
    '1. **不再需要单独的「启动器」**：缺 .NET 10 桌面运行时由**安装阶段自动下载 + 静默安装**处理（见上）；'
    '万一装完仍缺，双击程序时由 **.NET 自带的英文提示**兜底。本版安装包里**已经不再包含**启动器程序。',
]

FORBIDDEN = ['启动壳', '中文提示', '探针', '.iss 损坏', '恢复 .iss']


def sha(b):
    return hashlib.sha256(b).hexdigest().upper()


def build_new(src_text):
    assert src_text.startswith(HEADER), '顶部不是 0.291.7.6 那一节'
    i = src_text.index(NEXT)
    assert src_text.count(NEXT) == 1, '下一节锚点不唯一'
    old_head = src_text[:i]
    nl = old_head.index(CRLF)
    sep = old_head[nl + 2: old_head.index(CRLF, nl + 2)]
    assert sep and set(sep) == {'─'}, '分隔线不是纯 U+2500'
    # 旧节恰好 5 个 CRLF：标题 / 分隔线 / 「一、修复」/ 那一条 / 结尾空行
    assert old_head.count('\r\n') == 5, '旧节行数与预期不符：%d' % old_head.count('\r\n')
    assert old_head.endswith(CRLF + CRLF), '旧节尾部不是空行'
    new = HEADER + CRLF + sep + CRLF + (CRLF.join(BODY)) + CRLF + CRLF
    return new, src_text[i:]


def patch(path):
    raw = open(path, 'rb').read()
    pre = sha(raw)
    print('%-52s %8d B  sha %s' % (os.path.basename(path), len(raw), pre))
    assert pre == EXPECT_SHA, '前置 SHA 不符：%s' % pre
    txt = raw.decode('utf-8')
    assert txt.count('\n') == txt.count(CRLF), '存在裸 LF'
    new, rest = build_new(txt)
    out_txt = new + rest
    for f in FORBIDDEN:
        assert f not in new, '新节里出现了禁用说法：%s' % f
    assert '重试下载 / Retry' in new and '自动下载' in new and '剪贴板' in new
    assert '不再需要单独的「启动器」' in new
    out = out_txt.encode('utf-8')
    assert out[:3] != b'\xef\xbb\xbf', '不要写 BOM'
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_2276_' + os.path.basename(path))
    open(tmp, 'wb').write(out)
    back = open(tmp, 'rb').read()
    assert back == out, 'TEMP 回读不一致'
    assert sha(back) != pre, '内容没变'
    shutil.copyfile(tmp, path)
    fin = open(path, 'rb').read()
    assert fin == out, '落盘后不一致'
    print('%-52s %8d B  sha %s' % ('   -> ' + os.path.basename(path), len(fin), sha(fin)))
    return sha(fin)


def main():
    a0 = open(ROOT_LOG, 'rb').read()
    b0 = open(FILES_LOG, 'rb').read()
    assert a0 == b0, '两份改前不是逐字节一致'
    assert sha(a0) == EXPECT_SHA
    h1 = patch(ROOT_LOG)
    h2 = patch(FILES_LOG)
    a = open(ROOT_LOG, 'rb').read()
    b = open(FILES_LOG, 'rb').read()
    assert a == b, '两份改后不是逐字节一致'
    assert sha(a) == h1 == h2
    t = a.decode('utf-8')
    assert t.startswith(HEADER), '0.291.7.6 不在最顶部'
    assert t.index(NEXT) > 0 and t.index(NEXT) < 3000, '下一节不在其后'
    assert t.count(HEADER) == 1
    print('两份逐字节一致 %d B / sha %s' % (len(a), sha(a)))
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
