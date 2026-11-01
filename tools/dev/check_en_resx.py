# -*- coding: utf-8 -*-
import re
p = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
t = open(p, encoding='utf-8', errors='replace').read()
for key in ['QuickUsb', 'QuickCpuAdvPerf', 'RunOnStartup', 'DisplayEffect', 'DisplayEffectVibrantMode']:
    m = re.search(r'<data name="' + key + r'" xml:space="preserve">(.*?)</data>', t, re.S)
    if m:
        val = re.search(r'<value>(.*?)</value>', m.group(1), re.S)
        print(key, '=', repr(val.group(1).strip()) if val else 'NO VALUE')
    else:
        print(key, '= NOT FOUND in en resx')
