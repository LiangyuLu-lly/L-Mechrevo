# -*- coding: utf-8 -*-
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

old = 'new (string, string)[22]'
assert src.count(old) == 1, 'count=%d' % src.count(old)
src = src.replace(old, 'new (string, string)[28]')

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('array2 size patched')
