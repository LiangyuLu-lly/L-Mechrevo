# -*- coding: utf-8 -*-
for p in [r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx']:
    t = open(p, encoding='utf-8', errors='replace').read()
    old = '<data name="QuickCpuAdvPerfQuickCpuAdvPerfTip"'
    assert old in t, 'bad key not found in ' + p
    t = t.replace(old, '<data name="QuickCpuAdvPerfTip"')
    open(p, 'w', encoding='utf-8', newline='').write(t)
    print(p.split('\\')[-1], 'fixed')
