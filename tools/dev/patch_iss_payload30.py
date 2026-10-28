# -*- coding: utf-8 -*-
# 1) 从 install_script.iss 删除已删文件的引用（OTA.exe / GamingMonitor.json / GamingMonitor configs.json）
# 2) 生成 payload\30 全部文件的 Source 行并插入到 payload\50 段之后
import io, os, re

iss_p = r'D:\openLM\installer-src\install_script.iss'
p30 = r'D:\openLM\installer-src\files\GCU\payload\30'

raw = open(iss_p, 'rb').read()
enc = 'utf-8-sig' if raw[:3] == b'\xef\xbb\xbf' else 'utf-8'
s = io.open(iss_p, encoding=enc).read()

# --- 1) 删除三个失效引用行（整行） ---
def drop_line(text, fragment):
    # 匹配包含 fragment 的整行（含行尾换行）
    pat = re.compile(r'^.*' + re.escape(fragment) + r'.*\r?\n', re.M)
    n = len(pat.findall(text))
    assert n == 1, 'fragment %r matched %d lines (want 1)' % (fragment, n)
    return pat.sub('', text, count=1)

s = drop_line(s, 'OTA.exe')
s = drop_line(s, 'AppSettings\\GamingMonitor.json')
s = drop_line(s, 'GamingMonitor\\Config\\configs.json')

# --- 2) 生成 payload\30 Source 段 ---
lines = []
for root, dirs, files in os.walk(p30):
    for f in sorted(files):
        full = os.path.join(root, f)
        rel = os.path.relpath(full, p30).replace('\\', '/')
        # DestDir 不含文件名，只到目录层
        d = os.path.dirname(rel).replace('/', '\\')
        dest = '{app}\\GCU\\payload\\30'
        if d:
            dest += '\\' + d
        lines.append('Source: "files\\GCU\\payload\\30\\%s"; DestDir: "%s"; Flags: ignoreversion' % (rel.replace('/', '\\'), dest))

# --- 3) 插入到 payload\50 最后一条之后 ---
anchor = 'payload\\50\\AiStoneService\\MyControlCenter\\UEFI_Firmware.dll'
pat = re.compile(r'^.*' + re.escape(anchor) + r'.*$', re.M)
m = pat.search(s)
assert m, 'anchor not found'
ins = '\n' + '\n'.join(lines)
s = s[:m.end()] + ins + s[m.end():]

io.open(iss_p, 'w', encoding=enc).write(s)
print('dropped OTA/GamingMonitor refs; inserted %d payload\\30 lines' % len(lines))
