# -*- coding: utf-8 -*-
from collections import Counter
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
lines = b.split(b'\n')
pref = Counter()
samples = {}
for i, ln in enumerate(lines[:246]):
    raw = ln.rstrip(b'\r')
    j = 0
    while j < len(raw):
        c = raw[j]
        if c < 0x80:
            j += 1
        elif c < 0xE0:
            if j + 1 < len(raw) and 0x80 <= raw[j+1] <= 0xBF:
                j += 2
            else:
                j += 1
        else:
            if j + 2 < len(raw) and 0x80 <= raw[j+1] <= 0xBF and 0x80 <= raw[j+2] <= 0xBF:
                j += 3
            else:
                pref[raw[j:j+2].hex()] += 1
                if raw[j:j+2].hex() not in samples:
                    samples[raw[j:j+2].hex()] = raw[max(0,j-6):j+3]
                j += 1
print('total distinct prefixes:', len(pref))
for k, v in pref.most_common(40):
    s = samples[k]
    print(f'{k}: {v}  ctx={s.hex()}')
