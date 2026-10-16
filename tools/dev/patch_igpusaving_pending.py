# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()

def rep(old, new, cnt=1, label=''):
    global t
    assert t.count(old) == cnt, f'anchor mismatch ({t.count(old)} != {cnt}): {label or old[:70]!r}'
    t = t.replace(old, new, cnt)

# 运行时刷新（VisualiseIgpuSaving）：待重启时隐藏核显省电
rep('''		bool showIgpuSaving = hw != null && hw.CanOfferIgpuOnly && hw.GpuMode == 1;''',
    '''		bool showIgpuSaving = hw != null && hw.CanOfferIgpuOnly && hw.GpuMode == 1 && !HasPendingGpuRestart();''', 1, 'showIgpuSaving')

# 构建时（BuildDashboardLayout）：一致性，同样排除待重启
rep('''		bool canIgpuSaving = igpuHw0 != null && igpuHw0.CanOfferIgpuOnly && igpuHw0.GpuMode == 1;''',
    '''		bool canIgpuSaving = igpuHw0 != null && igpuHw0.CanOfferIgpuOnly && igpuHw0.GpuMode == 1 && !HasPendingGpuRestart();''', 1, 'canIgpuSaving')

open(p, 'w', encoding='utf-8', newline='').write(t)
print('igpu saving pending guard patched')
