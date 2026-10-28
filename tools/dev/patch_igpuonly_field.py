# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = 'bool igpuOnly = Flag(new string[3] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport" });'
new = '// 50 系 GCU（ConnectionSwitch 系列）用 IGpuOnlyConnectionSwitch_Support=Enable 表达核显能力；40 系 GCU 用 iGPUModeOnlySupport。两者均认。\n\t\tbool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" });'
assert t.count(old) == 1, 'old not unique/found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK - 已修改 IgpuOnly 候选名')
i = t.find('bool igpuOnly')
print(t[i-40:i+160])
