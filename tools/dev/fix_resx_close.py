# -*- coding: utf-8 -*-
# 修复 resx 双闭合 </data>
import io

for p in [r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
          r'D:\openLM\source\MechrevoLite.Properties.Strings.resx']:
    raw = open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    t = raw.decode('utf-8-sig' if bom else 'utf-8')
    o = '</data>\n  </data>\n<data name="DisplayEffectCinemaMode"'
    n = t.count(o)
    print(p.split('\\')[-1], '双闭合:', n)
    if n:
        t = t.replace(o, '</data>\n<data name="DisplayEffectCinemaMode"')
        open(p, 'wb').write(t.encode('utf-8-sig' if bom else 'utf-8'))
print('DONE')
