# -*- coding: utf-8 -*-
"""统一中文术语：集显 → 核显模式（对齐已有的 IgpuMode/SwitchToIgpuMode 文案）"""

p = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'
t = open(p, encoding='utf-8', errors='replace').read()
before = t.count('集显')
repls = [
    ('切换到集显模式</value>', '切换到核显模式</value>'),
    ('无法切换到集显，请先切换到混合模式', '无法切换到核显模式，请先切换到混合模式'),
    ('<value>集显模式</value>', '<value>核显模式</value>'),
    ('<value>集显</value>', '<value>核显模式</value>'),
]
for old, new in repls:
    n = t.count(old)
    assert n >= 1, 'missing: ' + old
    t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('resx: 集显 occurrences', before, '->', t.count('集显'))

# 诊断包术语统一
p2 = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
old2 = 'handler.AppendLiteral("集显模式: ");'
assert t2.count(old2) == 1
t2 = t2.replace(old2, 'handler.AppendLiteral("核显模式: ");')
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('diag ok')
