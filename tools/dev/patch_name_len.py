# -*- coding: utf-8 -*-
"""命名限长 16 -> 12：PerfModeCollection 常量 + CustomModeForm 3 处硬编码"""
import io

# 1) PerfModeCollection 常量
p1 = r'D:\openLM\source\MechrevoLite.Mode\PerfModeCollection.cs'
s1 = io.open(p1, encoding='utf-8').read()
old1 = 'public const int MaxUserNameLength = 16;'
new1 = 'public const int MaxUserNameLength = 12;'
assert s1.count(old1) == 1, 'PerfModeCollection match failed'
io.open(p1, 'w', encoding='utf-8', newline='').write(s1.replace(old1, new1))
print('OK PerfModeCollection: 16 -> 12')

# 2) CustomModeForm 3 处
p2 = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
s2 = io.open(p2, encoding='utf-8').read()

pairs = [
    ('MaxLength = 16,', 'MaxLength = 12,'),
    ('text.Length > 16', 'text.Length > 12'),
    ('Strings.PerfModeNameTooLong, 16', 'Strings.PerfModeNameTooLong, 12'),
]
for old, new in pairs:
    n = s2.count(old)
    assert n == 1, 'CustomModeForm match failed: %r -> %d' % (old, n)
    s2 = s2.replace(old, new)
    print('OK CustomModeForm:', old, '->', new)

io.open(p2, 'w', encoding='utf-8', newline='').write(s2)
print('ALL DONE')
