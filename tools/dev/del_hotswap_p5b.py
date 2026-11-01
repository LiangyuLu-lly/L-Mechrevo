# -*- coding: utf-8 -*-
"""阶段5b：MechrevoService 常量 + 文件删除"""
import os
S = r'D:\openLM\source'

ms = os.path.join(S, r'MechrevoLite.Hardware\MechrevoService.cs')
t = open(ms, encoding='utf-8', errors='replace').read()
old = '\tinternal const int HotSwitchStatusRetryEveryPolls = 4;\n'
assert old in t, 'HotSwitchStatusRetryEveryPolls not found'
t = t.replace(old, '', 1)
open(ms, 'w', encoding='utf-8', newline='').write(t)
print('OK: HotSwitchStatusRetryEveryPolls 常量删除')

for f in (os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationCoordinator.cs'),
          os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationSnapshot.cs')):
    if os.path.exists(f):
        os.remove(f)
        print('OK: 删除文件 ' + os.path.basename(f))
print('PHASE5b DONE')
