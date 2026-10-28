# -*- coding: utf-8 -*-
import re
for p in [r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.resx']:
    t = open(p, encoding='utf-8-sig').read()
    print('===', p.split('\\')[-1], '===')
    for m in re.finditer(r'<data name="(CalibApplyOnStartupHint|CustomCalibrationHint)"[^>]*>.*?<value>(.*?)</value>', t, re.S):
        print(m.group(1), '=>', repr(m.group(2)))
