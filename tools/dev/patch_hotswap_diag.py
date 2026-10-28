# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''			GpuHotSwap = (Flag(new string[1] { "GpuHotSwapSwitchSupport" }) && Flag(new string[1] { "lgpuHotSwapSwitchStatus" })),'''
new = '''			// 2026-10-03：热切换与核显省电为同一命令（IGPU_ONLY_CONNECT_RB_ON/OFF）；原能力位字段 GpuHotSwapSwitchSupport/lgpuHotSwapSwitchStatus
			// 两类 GCU 均不上报（恒 false，诊断恒写"否"），改为与核显省电一致的核显能力判断（iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support=Enable）
			GpuHotSwap = igpuOnly,'''
assert t.count(old) == 1, 'not found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK GpuHotSwap = igpuOnly')
