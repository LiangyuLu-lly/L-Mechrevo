# -*- coding: utf-8 -*-
"""阶段2：SettingsForm + GpuSwitchRoute + 文件删除"""
import os

def del_lines(path, start, end, tag):
    lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
    assert 1 <= start <= end <= len(lines), f'{tag}: bad range {start}-{end} len={len(lines)}'
    print(f'--- {tag} 删除 {start}-{end} ---')
    for i in range(start-1, end):
        print(f'{i+1:6d} | {lines[i][:70]}')
    del lines[start-1:end]
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
    print('OK: ' + tag + '\n')

def replace(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new, 1))
    print('OK: ' + tag)

S = r'D:\openLM\source'
sf = os.path.join(S, r'MechrevoLite\SettingsForm.cs')

# 2c. PrepareDgpuApplicationsForHotSwitchAsync + DescribeDgpuApplications（6362-6425 含尾空行）
del_lines(sf, 6362, 6425, 'SettingsForm 2c')

# 2a. DgpuPreflightResult 枚举（29-35 含空行）
del_lines(sf, 29, 35, 'SettingsForm 2a')

# 2b. case GpuSwitchRoute.HotSwitch 标签删除
replace(sf,
'''				case GpuSwitchRoute.Direct:
				case GpuSwitchRoute.HotSwitch:
					await Program.service.SwitchGpuMode(targetMode);''',
'''				case GpuSwitchRoute.Direct:
					await Program.service.SwitchGpuMode(targetMode);''',
'SettingsForm 2b')

# ============ GpuSwitchRoute.cs：删 HotSwitch 枚举 ============
gsr = os.path.join(S, r'MechrevoLite.Hardware\GpuSwitchRoute.cs')
t = open(gsr, encoding='utf-8', errors='replace').read()
print('--- GpuSwitchRoute.cs 原文 ---')
print(t)
print('--- 原文结束 ---')
