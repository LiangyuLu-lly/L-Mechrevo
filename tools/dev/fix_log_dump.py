# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
t = b.decode('utf-8', errors='replace')
si = t.find('ʮ�ˡ��ڴ�ռ���Ż���')
ei = t.find('十三、睡眠省电', si)
bsi = len(t[:si].encode('utf-8'))
bei = len(t[:ei].encode('utf-8'))
seg = b[bsi:bei]
print('block bytes:', len(seg))
# 统计字节分布：哪些是合法 GBK 双字节、哪些是 ASCII
i = 0
bad = []
ascii_runs = []
run_start = None
while i < len(seg):
    c = seg[i]
    if c < 0x80:
        if run_start is None:
            run_start = i
    else:
        if run_start is not None:
            ascii_runs.append((run_start, i, seg[run_start:i].decode('ascii', errors='replace')))
            run_start = None
        # 尝试 GBK 双字节
        if i + 1 < len(seg) and 0x81 <= c <= 0xFE and 0x40 <= seg[i+1] <= 0xFE and seg[i+1] != 0x7F:
            i += 2
        else:
            bad.append((i, hex(c)))
            i += 1
if run_start is not None:
    ascii_runs.append((run_start, len(seg), seg[run_start:].decode('ascii', errors='replace')))
print('bad bytes count:', len(bad), 'first 20:', bad[:20])
print('ascii runs:', len(ascii_runs))
for s, e, txt in ascii_runs[:30]:
    if len(txt) >= 3:
        print(f'  [{s}-{e}] {txt[:80]!r}')
