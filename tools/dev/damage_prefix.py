# -*- coding: utf-8 -*-
from collections import Counter
lines = open(r'D:\openLM\tools\damage_report.txt', encoding='utf-8').read().splitlines()
pref = Counter()
for ln in lines:
    parts = ln.split('\t')
    h = parts[2] if len(parts) == 3 else parts[1]
    # hex 串：每 2 字符一字节；损坏点 = 最后 3 字节（xx yy 3f），前缀 = 前两字节
    b = bytes.fromhex(h)
    key = b[-3:-1].hex()  # 前两字节（如 efbc / e380 / e4ba）
    pref[key] += 1
print('top 30 damaged prefixes:')
for k, v in pref.most_common(30):
    print(f'  {k}: {v}')
print('total prefixes:', len(pref))
