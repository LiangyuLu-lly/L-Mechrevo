# -*- coding: utf-8 -*-
"""验证重建 resx"""
import re
for p in [r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx']:
    s = open(p, encoding='utf-8', errors='replace').read()
    keys = re.findall(r'<data\s+name="([^"]+)"', s)
    gpu = [k for k in keys if k.startswith('GpuRoute') or k.startswith('GpuPref')
           or k.startswith('GpuRestart') or k == 'GpuIgpuNeedsStandard']
    print(p.split('\\')[-1], 'keys:', len(keys), 'new-gpu:', len(gpu))
    assert s.count('</root>') == 1, 'root count'
    assert '<xs:schema' in s, 'schema missing'
print('OK')
