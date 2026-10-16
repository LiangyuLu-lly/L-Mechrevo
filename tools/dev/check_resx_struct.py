# -*- coding: utf-8 -*-
"""检查 18.6 与群友版 resx 结构"""
import re
for p in [r'D:\openLM\archive\refs\L-Mechrevo-18.6-source\source\MechrevoLite.Properties.Strings.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx']:
    s = open(p, encoding='utf-8', errors='replace').read()
    print('===', p.split('\\')[-1], 'len', len(s))
    print('xs:schema:', s.count('<xs:schema'), 'resheader:', s.count('<resheader'),
          'data:', s.count('<data '), '</root>:', s.count('</root>'))
    print('head 400:')
    print(s[:400].replace('\n', ' | '))
    print()
