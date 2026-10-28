# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\r\n')
start = end = None
for i, ln in enumerate(lines):
    try:
        ln.decode('utf-8')
        ok = True
    except UnicodeDecodeError:
        ok = False
    if not ok and start is None:
        start = i
    if ok and start is not None and end is None and '睡眠省电' in ln.decode('utf-8', errors='ignore'):
        end = i
print('mojibake lines range:', start, '-', end, 'of', len(lines))
print('line before:', repr(lines[start-1][:60]))
print('line at end boundary:', repr(lines[end][:60]))
seg = b'\r\n'.join(lines[start:end])
dec = seg.decode('gb18030', errors='replace')
open(r'D:\openLM\tools\mojibake2.txt', 'w', encoding='utf-8').write(dec)
bad = [i for i, ch in enumerate(dec) if ch == '\ufffd']
print('replacement count:', len(bad))
# 分组显示损坏区上下文（每段损坏合并显示）
groups = []
for i in bad:
    if groups and i - groups[-1][-1] <= 3:
        groups[-1].append(i)
    else:
        groups.append([i])
print('damaged groups:', len(groups))
for g in groups[:20]:
    a, z = g[0], g[-1]
    ctx = dec[max(0, a-40):z+40].replace('\ufffd', '□')
    print(f'--- group {a}-{z} ---')
    print(ctx)
