# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\n')
# 只分析 0-245 行（UTF-8 区，lines 索引 0-245）
report = []
for i, ln in enumerate(lines[:246]):
    raw = ln.rstrip(b'\r')
    try:
        raw.decode('utf-8')
        continue  # 完好行
    except UnicodeDecodeError:
        pass
    # 宽松解码找损坏
    t = raw.decode('utf-8', errors='replace')
    # 找 U+FFFD 与 '?' 位置：用字节扫描定位 0x3F 前是 EF BC 或 E? ??
    j = 0
    while j < len(raw):
        c = raw[j]
        if c == 0x3F and j >= 2 and raw[j-1] in range(0x80, 0xC0) and raw[j-2] >= 0xE0:
            # 损坏点：第三字节被 ? 替换
            ctx_b = raw[max(0,j-8):j+2]
            report.append((i, ctx_b.hex()))
            j += 1
        elif c >= 0xE0:
            j += 3
        elif c >= 0x80:
            j += 2
        else:
            j += 1
print('damaged points in lines 0-245:', len(report))
# 按行聚合
from collections import Counter
c = Counter(r[0] for r in report)
print('affected lines:', len(c))
for ln, n in sorted(c.items()):
    print(f'  line {ln}: {n} points')
# 保存损坏清单供人工修复
with open(r'D:\openLM\tools\damage_report.txt', 'w', encoding='utf-8') as f:
    for ln, hexs in report:
        f.write(f'{ln}\t{hexs}\n')
print('report saved')
