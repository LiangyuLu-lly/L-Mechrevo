# -*- coding: utf-8 -*-
# 1) MechrevoHw: 增加 SupportsGpuHotSwap（只认 IGpuOnlyConnectionSwitch_Support=Enable）
p1 = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
t1 = open(p1, encoding='utf-8', errors='replace').read()
old1 = '''	public bool SupportsIgpuOnly => Capabilities.IgpuOnly;
'''
new1 = '''	public bool SupportsIgpuOnly => Capabilities.IgpuOnly;

	public bool SupportsGpuHotSwap => Capabilities.GpuHotSwap;
'''
assert t1.count(old1) == 1, 'SupportsIgpuOnly anchor'
t1 = t1.replace(old1, new1, 1)
open(p1, 'w', encoding='utf-8', newline='').write(t1)
print('MechrevoHw.SupportsGpuHotSwap added')

# 2) SettingsForm: 核显省电按钮显示/执行条件改为热切换支持
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
def rep(old, new, label):
    global t2
    assert t2.count(old) == 1, f'anchor mismatch: {label}'
    t2 = t2.replace(old, new, 1)

rep('''		bool canIgpuSaving = igpuHw0 != null && igpuHw0.CanOfferIgpuOnly && igpuHw0.GpuMode == 1 && !HasPendingGpuRestart();''',
    '''		bool canIgpuSaving = igpuHw0 != null && igpuHw0.SupportsGpuHotSwap && igpuHw0.GpuMode == 1 && !HasPendingGpuRestart();''',
    'canIgpuSaving (build)')
rep('''		if (svc == null || hw == null || !hw.IsConnected || !hw.CanOfferIgpuOnly)''',
    '''		if (svc == null || hw == null || !hw.IsConnected || !hw.SupportsGpuHotSwap)''',
    'SwitchIgpuSavingAsync guard')
rep('''		bool showIgpuSaving = hw != null && hw.CanOfferIgpuOnly && hw.GpuMode == 1 && !HasPendingGpuRestart();''',
    '''		bool showIgpuSaving = hw != null && hw.SupportsGpuHotSwap && hw.GpuMode == 1 && !HasPendingGpuRestart();''',
    'showIgpuSaving (refresh)')
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('SettingsForm igpu saving -> SupportsGpuHotSwap')
