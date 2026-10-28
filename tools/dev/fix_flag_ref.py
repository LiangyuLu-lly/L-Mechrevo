# -*- coding: utf-8 -*-
import re
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()
pat = re.compile(r'^[ \t]*menuEco\.Enabled = !flag;[ \t]*$', re.M)
n = len(pat.findall(t))
assert n == 1, 'matches=' + str(n)
t = pat.sub('\t\t\t\tmenuEco.Enabled = true;', t, count=1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK fixed', n)
i = t.find('menuEco.Enabled')
print(repr(t[i-40:i+40]))
