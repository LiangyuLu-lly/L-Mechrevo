# -*- coding: utf-8 -*-
import os
cands = [
    r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite.Properties\Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite\Properties\Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite.Properties\Strings.resx',
    r'D:\openLM\source\MechrevoLite\Properties\Strings.en.resx',
    r'D:\openLM\source\MechrevoLite\Strings.zh-CN.resx',
]
for c in cands:
    print(os.path.exists(c), c)

# 兜底：搜整个 source 树里所有 .resx
hits = []
for root, dirs, files in os.walk(r'D:\openLM\source'):
    for f in files:
        if f.endswith('.resx'):
            hits.append(os.path.join(root, f))
print('ALL RESX:', len(hits))
for h in hits[:20]:
    print('  ', h)
