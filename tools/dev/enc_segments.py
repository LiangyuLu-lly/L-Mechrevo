# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\n')
print('LF lines:', len(lines))
def dec(ln, enc):
    try:
        return ln.decode(enc), True
    except Exception:
        return '', False
segs = []  # (start_line, end_line, enc, sample)
cur = None
for i, ln in enumerate(lines):
    raw = ln.rstrip(b'\r')
    u, uok = dec(raw, 'utf-8')
    g, gok = dec(raw, 'gb18030')
    tag = 'U' if (uok and not gok) else ('G' if (gok and not uok) else ('B' if (uok and gok) else 'X'))
    # 空行归入当前段
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
