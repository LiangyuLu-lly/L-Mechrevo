# -*- coding: utf-8 -*-
"""阶段4：MechrevoService 3i-3m + GpuRouteCommandLayer + MechrevoHw + GpuSwitchRoute + 文件删除"""
import os

def replace(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new, 1))
    print('OK: ' + tag)

S = r'D:\openLM\source'
ms = os.path.join(S, r'MechrevoLite.Hardware\MechrevoService.cs')
t = open(ms, encoding='utf-8', errors='replace').read()

# 3i. 热切换未确认回滚块（锚点：if (!confirmed & hotSwitchRequest) 整块）
i = t.find('\t\t\tif (!confirmed & hotSwitchRequest)')
assert i >= 0, '3i start not found'
line_start = t.rfind('\n', 0, i) + 1
j = t.find('\t\t\t}', i)  # 块的结束花括号（3 tab）
assert j >= 0, '3i end brace not found'
end = j + len('\t\t\t}')
print('=== 3i 待删块 ===')
print(t[line_start:end])
t = t[:line_start] + t[end:]
print('3i OK')

# 3j. 日志删 hotSwitch 字段
old_log = '			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} hotSwitch={hotSwitchRequest} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");'
new_log = '			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");'
assert old_log in t, '3j not found'
assert t.count(old_log) == 1, '3j not unique'
t = t.replace(old_log, new_log, 1)
print('3j OK')

# 3k. TargetReached 简化（锚点：if (!hotSwitchRequest && ...)
i = t.find('\t\t\t\tif (!hotSwitchRequest && _hw.GpuModeStatusVersion <= gpuModeStatusVersion)')
assert i >= 0, '3k start not found'
line_start = t.rfind('\n', 0, i) + 1
# 找 requiresFreshHotSwitchResult 块结束：从 i 找 'return true;' 行
k = t.find('\t\t\t\treturn true;', i)
assert k >= 0, '3k return true not found'
end = k + len('\t\t\t\treturn true;')
print('=== 3k 待删块 ===')
print(t[line_start:end])
t = t[:line_start] + '''\t\t\t\tif (_hw.GpuModeStatusVersion <= gpuModeStatusVersion)
				{
					return false;
				}
				return true;
''' + t[end:]
print('3k OK')

# 3l. RollbackFailedHotSwitchAsync 方法删除（锚点）
i = t.find('\tinternal async Task RollbackFailedHotSwitchAsync(int modeBeforeSwitch, CancellationToken token)')
assert i >= 0, '3l start not found'
line_start = t.rfind('\n', 0, i) + 1
# 方法结束：从 i 找方法体最后一个 '}'——用 '}\n\n\tpublic async Task<bool> SwitchGpuPowerSaving' 定位
k = t.find('\n\n\tpublic async Task<bool> SwitchGpuPowerSaving', i)
assert k >= 0, '3l end not found'
end = k + 1  # 含方法后第一个空行
print('=== 3l 待删方法 ===')
print(t[line_start:end])
t = t[:line_start] + t[end:]
print('3l OK')

# 3m. CreateGpuSwitchPayload 签名 + 条件
old_sig = '\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch)'
new_sig = '\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly)'
assert old_sig in t, '3m sig not found'
t = t.replace(old_sig, new_sig, 1)
old_cond = '\t\tif (!((mode == 0) & supportsDgpuDirect) || useHotSwitch || supportsIgpuOnly)'
new_cond = '\t\tif (!((mode == 0) & supportsDgpuDirect) || supportsIgpuOnly)'
assert old_cond in t, '3m cond not found'
t = t.replace(old_cond, new_cond, 1)
print('3m OK')

open(ms, 'w', encoding='utf-8', newline='').write(t)

# ============ GpuRouteCommandLayer.cs ============
gr = os.path.join(S, r'MechrevoLite.Hardware\GpuRouteCommandLayer.cs')
replace(gr,
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch, DgpuGenerationKind generation)',
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, DgpuGenerationKind generation)',
'RouteLayer 4a')
replace(gr,
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly, useHotSwitch);',
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly);',
'RouteLayer 4b')

# ============ MechrevoHw.cs ============
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

# ============ GpuSwitchRoute.cs：删 HotSwitch 枚举 ============
gsr = os.path.join(S, r'MechrevoLite.Hardware\GpuSwitchRoute.cs')
t2 = open(gsr, encoding='utf-8', errors='replace').read()
old = '\tHotSwitch,\n'
assert old in t2, 'GpuSwitchRoute HotSwitch not found'
t2 = t2.replace(old, '', 1)
open(gsr, 'w', encoding='utf-8', newline='').write(t2)
print('OK: GpuSwitchRoute HotSwitch 枚举删除')

# ============ 文件删除（无引用） ============
f1 = os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationCoordinator.cs')
f2 = os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationSnapshot.cs')
for f in (f1, f2):
    if os.path.exists(f):
        os.remove(f)
        print('OK: 删除文件 ' + os.path.basename(f))

print('PHASE4 DONE')
