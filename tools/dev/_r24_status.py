# -*- coding: utf-8 -*-
r"""r24 status: annotate the frozen r23 probe (so nobody copies its bad path again),
then print every before/after SHA needed for the report. Read-only except the one annotation."""
import hashlib, io, os, re, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = r'D:\openLM'
OLD_PROBE = os.path.join(ROOT, r'installer-src\_probe\probe_browserchain.iss')
BAK = os.path.join(ROOT, r'backup\repack\2026-10\20261013_r24_pre_fix')

WARN = ('; ⚠️⚠️ 2026-10-13 r24 起本文件【只是 r23 的历史证据】，⛔ 不要再照抄里面的写法：\r\n'
        ';    它第 2 档用的是 {sys}\\explorer.exe = C:\\Windows\\System32\\explorer.exe —— 那个文件\n'
        ';    在 64 位 Windows 上【不存在】，Windows 会弹「找不到文件」的系统错误框（用户实测截图）。\n'
        ';    已修版本 + 新断言见 installer-src\\_probe\\probe_browserchain_r24.iss\n'
        ';    （正确路径 = {win}\\explorer.exe = C:\\Windows\\explorer.exe）。\n')


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''):
            h.update(b)
    return h.hexdigest().upper()


print('=== (1) 冻结 r23 探针：加一条「别再抄」的警示头（只加注释，正文一字未动）===')
raw = open(OLD_PROBE, 'rb').read()
if b'r24 \xe8\xb5\xb7\xe6\x9c\xac\xe6\x96\x87\xe4\xbb\xb6' not in raw:
    out = WARN.encode('utf-8') + raw
    tmp = OLD_PROBE + '.tmp-write'
    open(tmp, 'wb').write(out)
    assert open(tmp, 'rb').read() == out
    os.replace(tmp, OLD_PROBE)
    print('  annotated: %d -> %d B  sha %s' % (len(raw), len(out), sha(OLD_PROBE)))
else:
    print('  already annotated')

print('\n=== (2) 本轮改动文件的 SHA（改前 / 改后）===')
pairs = [
    (r'installer-src\install_script.iss', 'install_script.iss'),
    (r'tools\LmLauncherStub\LmLauncherStub.cs', 'LmLauncherStub.cs'),
    (r'tools\LmLauncherStub\app.manifest', 'app.manifest'),
]
for rel, bakname in pairs:
    p = os.path.join(ROOT, rel)
    b = os.path.join(BAK, bakname)
    print('  %-42s %8d B %s' % (rel, os.path.getsize(p), sha(p)))
    print('  %-42s %8d B %s   <- 改前（备份）' % ('   backup\\...r24_pre_fix\\' + bakname,
                                                 os.path.getsize(b), sha(b)))

print('\n=== (3) 启动壳 exe（files\\ 里那个真的会被打包的）===')
stub = os.path.join(ROOT, r'installer-src\files\L-Mechrevo-Launcher.exe')
binr = os.path.join(ROOT, r'tools\LmLauncherStub\bin\Release\L-Mechrevo-Launcher.exe')
print('  files\\  %8d B  %s' % (os.path.getsize(stub), sha(stub)))
print('  bin\\    %8d B  %s  (identical=%s)' % (os.path.getsize(binr), sha(binr), sha(stub) == sha(binr)))
print('  改前    %8d B  D87C7FDD571807902046E9D677FB97FA5FAF194C75465D24292E1CF464C5FE2D' % 23040)
sb = open(stub, 'rb').read()


def u16(s):
    return s.encode('utf-16-le')


print('\n=== (4) 壳二进制的字符串检查（廉价实证：错路径相关字符串没了 / 新档位在）===')
checks = [
    ('新档位 第1档 shell 关联', u16('第1档 shell 关联')),
    ('新档位 第2档 默认浏览器', u16('第2档 默认浏览器')),
    ('explorer 候选校验', u16('explorer 候选')),
    ('旧文案 第1档 explorer.exe 已接受网址（必须为 False）', u16('第1档 explorer.exe 已接受网址')),
    ('SpecialFolder.System 相关文案没了', u16('System32')),
]
for name, pat in checks:
    print('  %-52s utf16=%s' % (name, pat in sb))
print('  manifest 级别属性: level="asInvoker" = %s ; level="requireAdministrator" = %s'
      % (b'level="asInvoker"' in sb, b'level="requireAdministrator"' in sb))
print('  （注：注释里仍会出现 requireAdministrator 字样，所以只认 level="..." 属性本身）')

print('\n=== (5) 6 个核心文件：files\\ == 构建产物（本轮未改主程序）===')
BUILD = os.path.join(ROOT, r'source\bin\Release\net10.0-windows10.0.19041.0')
FILES = os.path.join(ROOT, r'installer-src\files')
for rel in ['L-Mechrevo.dll', 'L-Mechrevo.exe', 'L-Mechrevo.deps.json',
            'L-Mechrevo.runtimeconfig.json', 'L-Mechrevo.dll.config',
            r'zh-CN\L-Mechrevo.resources.dll']:
    a, b = os.path.join(BUILD, rel), os.path.join(FILES, rel)
    ok = os.path.isfile(a) and os.path.isfile(b) and sha(a) == sha(b)
    print('  %-40s ==%s  %d B  %s' % (rel, ok, os.path.getsize(b) if os.path.isfile(b) else -1,
                                      sha(b)[:24] if os.path.isfile(b) else 'MISSING'))

print('\n=== (6) iss 里新档位 / 已修路径的行号 ===')
t = io.open(os.path.join(ROOT, r'installer-src\install_script.iss'), encoding='utf-8', newline='').read().split('\r\n')
pats = ["ShellExec('open', Net10DownloadDirectUrl", "ShellExecAsOriginalUser('open', Net10DownloadDirectUrl",
        "Candidates[0] := ExpandConstant('{win}\\explorer.exe')", "Candidates[2] := ExpandConstant('{sys}\\explorer.exe')",
        'function NetResolveExplorerExe', 'function NetResolveDefaultBrowserExe', 'function OpenNet10DownloadPage',
        "ShellExec('open', ExplorerExe", "ShellExecAsOriginalUser('open', BrowserExe", "ShellExec('open', BrowserExe",
        'NetBrowserTierReport', '自动打开浏览器没有成功']
for p in pats:
    hits = [i + 1 for i, l in enumerate(t) if p in l]
    print('  %-58s line %s' % (p[:56], hits[:4]))
print('  总行数 = %d ; 大小 = %d B' % (len(t), os.path.getsize(os.path.join(ROOT, r'installer-src\install_script.iss'))))
print('ALL-DONE')
