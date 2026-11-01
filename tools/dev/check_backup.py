# -*- coding: utf-8 -*-
p = r'D:\Downloads\更新日志.txt'
b = open(p, 'rb').read()
print('size:', len(b))
print('head hex:', b[:8].hex())
lines = b.split(b'\n')
print('LF lines:', len(lines))
def dec(ln, enc):
    try:
        return ln.decode(enc), True
    except Exception:
        return '', False
segs = []
cur = None
for i, ln in enumerate(lines):
    raw = ln.rstrip(b'\r')
    u, uok = dec(raw, 'utf-8')
    g, gok = dec(raw, 'gb18030')
    tag = 'U' if (uok and not gok) else ('G' if (gok and not uok) else ('B' if (uok and gok) else 'X'))
    if raw == b'' and cur is not None:
        segs.append((cur[0], i, cur[1], cur[2])); cur = None
        continue
    if cur is None:
        cur = [i, tag, raw[:30]]
    elif tag != cur[1]:
        segs.append((cur[0], i-1, cur[1], cur[2])); cur = [i, tag, raw[:30]]
if cur: segs.append((cur[0], len(lines)-1, cur[1], cur[2]))
for s, e, enc, samp in segs:
    print(f'lines {s}-{e} [{enc}] {samp!r}')
# 查找版本标记，确认覆盖范围
import re
full = b.decode('gb18030', errors='replace')
for key in ['0.289.1.0', '0.289.2.0', '0.289.3.1', '0.289.4.0', '0.289.5', '0.289.6', '十八', '睡眠省电', '问题反馈']:
    print(key, '->', full.count(key))
