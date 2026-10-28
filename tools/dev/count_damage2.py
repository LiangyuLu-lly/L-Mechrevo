# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\n')
report = []
for i, ln in enumerate(lines[:246]):
    raw = ln.rstrip(b'\r')
    j = 0
    while j < len(raw):
        c = raw[j]
        if c < 0x80:
            j += 1
        elif c < 0xE0:  # 2-byte
            if j + 1 < len(raw) and 0x80 <= raw[j+1] <= 0xBF:
                j += 2
            else:
                report.append((i, j, raw[max(0,j-4):j+2].hex()))
                j += 1
        else:  # 3-byte
            if (j + 2 < len(raw) and 0x80 <= raw[j+1] <= 0xBF and 0x80 <= raw[j+2] <= 0xBF):
                j += 3
            else:
                report.append((i, j, raw[max(0,j-4):j+2].hex()))
                j += 1
print('damaged points in lines 0-245:', len(report))
from collections import Counter
c = Counter(r[0] for r in report)
print('affected lines:', len(c))
for ln, n in sorted(c.items()):
    print(f'  line {ln}: {n}')
with open(r'D:\openLM\tools\damage_report.txt', 'w', encoding='utf-8') as f:
    for ln, off, hexs in report:
        f.write(f'{ln}\t{off}\t{hexs}\n')
print('saved')
