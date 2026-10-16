# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\n')
out = []
for i, ln in enumerate(lines[:246]):
    raw = ln.rstrip(b'\r')
    t = raw.decode('utf-8', errors='replace')
    out.append(f'{i}\t{t}')
open(r'D:\openLM\tools\head_dump.txt', 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
# GBK 区 246-343
g = []
for i, ln in enumerate(lines[246:344]):
    raw = ln.rstrip(b'\r')
    if raw == b'':
        g.append(f'{i+246}\t')
    else:
        g.append(f'{i+246}\t{raw.decode("gb18030")}')
open(r'D:\openLM\tools\gbk_dump.txt', 'w', encoding='utf-8', newline='\n').write('\n'.join(g))
print('dumps written; head lines:', len(out), 'gbk lines:', len(g))
