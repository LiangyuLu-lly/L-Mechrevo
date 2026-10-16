# -*- coding: utf-8 -*-
"""阶段5：GpuRouteCommandLayer + GpuSwitchRoute + MechrevoService 常量 + 文件删除"""
import os

def replace(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new, 1))
    print('OK: ' + tag)

S = r'D:\openLM\source'

# GpuRouteCommandLayer.cs
gr = os.path.join(S, r'MechrevoLite.Hardware\GpuRouteCommandLayer.cs')
replace(gr,
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch, DgpuGenerationKind generation)',
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, DgpuGenerationKind generation)',
'RouteLayer 4a')
replace(gr,
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly, useHotSwitch);',
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly);',
'RouteLayer 4b')

# GpuSwitchRoute.cs：删 HotSwitch 枚举
gsr = os.path.join(S, r'MechrevoLite.Hardware\GpuSwitchRoute.cs')
t = open(gsr, encoding='utf-8', errors='replace').read()
old = '\tHotSwitch,\n'
assert old in t, 'GpuSwitchRoute HotSwitch not found'
t = t.replace(old, '', 1)
open(gsr, 'w', encoding='utf-8', newline='').write(t)
print('OK: GpuSwitchRoute HotSwitch 枚举删除')

# MechrevoHw.cs
mh = os.path.join(S, r'MechrevoLite.Hardware\MechrevoHw.cs')
replace(mh,
'''			// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
			bool flag = false;
			bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly && !flag;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, SupportsDgpuDirect, SupportsIgpuOnly, flag, DgpuGeneration);''',
'''			// 2026-10-03：热切换执行已删除（与核显省电功能重叠）
			bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, SupportsDgpuDirect, SupportsIgpuOnly, DgpuGeneration);''',
'MechrevoHw 5a')

# MechrevoService.cs：删 HotSwitchStatusRetryEveryPolls 常量（59）
ms = os.path.join(S, r'MechrevoLite.Hardware\MechrevoService.cs')
t = open(ms, encoding='utf-8', errors='replace').read()
old = '\tinternal const int HotSwitchStatusRetryEveryPolls = 4;\n'
assert old in t, 'HotSwitchStatusRetryEveryPolls not found'
t = t.replace(old, '', 1)
open(ms, 'w', encoding='utf-8', newline='').write(t)
print('OK: MechrevoService HotSwitchStatusRetryEveryPolls 常量删除')

# 文件删除（无引用）
for f in (os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationCoordinator.cs'),
          os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationSnapshot.cs')):
    if os.path.exists(f):
        os.remove(f)
        print('OK: 删除文件 ' + os.path.basename(f))

print('PHASE5 DONE')
