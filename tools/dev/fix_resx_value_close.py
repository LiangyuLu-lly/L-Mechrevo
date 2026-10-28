# -*- coding: utf-8 -*-
# 修复 resx 缺失的 </value> 闭合
pairs = [
    (r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
     'removes all applied ICC profiles.\n    \n  \n  </data>',
     'removes all applied ICC profiles.</value>\n    \n  \n  </data>'),
    (r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
     '关闭则取消所有已应用的 ICC。\n    \n  \n  </data>',
     '关闭则取消所有已应用的 ICC。</value>\n    \n  \n  </data>'),
]
for p, old, new in pairs:
    raw = open(p, 'rb').read()
    has_bom = raw.startswith(b'\xef\xbb\xbf')
    t = raw.decode('utf-8-sig' if has_bom else 'utf-8')
    n = t.count(old)
    print(p.split('\\')[-1], '匹配:', n)
    if n != 1:
        # 打印损坏段诊断
        i = t.find('<value>When') if 'Strings.resx' in p else t.find('<value>开启')
        print('--- 损坏段 ---')
        print(repr(t[i:i+400]))
        raise SystemExit('ABORT ' + p)
    t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8-sig' if has_bom else 'utf-8'))
    print('OK')
print('DONE')
