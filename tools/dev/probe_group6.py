# -*- coding: utf-8 -*-
"""查 Program.cs using 头、Strings.cs 结构、resx 键样例"""
import re

# Program.cs 头
s = open(r'D:\openLM\source\MechrevoLite\Program.cs', encoding='utf-8', errors='replace').read()
print('=== Program.cs 头 40 行 ===')
print('\n'.join(s.split('\n')[:40]))

# Strings.cs
try:
    sc = open(r'D:\openLM\source\MechrevoLite.Properties\Strings.cs', encoding='utf-8', errors='replace').read()
    print('=== Strings.cs len', len(sc), '===')
    print(sc[:1500])
except Exception as e:
    print('Strings.cs ERR', e)

# resx 键样例
r = open(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', encoding='utf-8', errors='replace').read()
print('=== resx 键样例（前 10）===')
print('\n'.join(re.findall(r'<data name="([^"]+)"', r)[:10]))
print('total keys:', len(re.findall(r'<data name="([^"]+)"', r)))
