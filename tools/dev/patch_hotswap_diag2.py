# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''			// 2026-10-03：热切换与核显省电为同一命令（IGPU_ONLY_CONNECT_RB_ON/OFF）；原能力位字段 GpuHotSwapSwitchSupport/lgpuHotSwapSwitchStatus
			// 两类 GCU 均不上报（恒 false，诊断恒写"否"），改为与核显省电一致的核显能力判断（iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support=Enable）
			GpuHotSwap = igpuOnly,'''
new = '''			// 2026-10-03（更正）：热切换（免重启切集显）是 50 系专属能力，与核显省电同一命令（IGPU_ONLY_CONNECT_RB_ON/OFF），
			// 判断只认 50 系 GCU 上报的 IGpuOnlyConnectionSwitch_Support=Enable；40 系三模（iGPUModeOnlySupport）为冷切换（重启生效），不计入热切换
			GpuHotSwap = Flag(new string[1] { "IGpuOnlyConnectionSwitch_Support" }),'''
assert t.count(old) == 1, 'not found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK GpuHotSwap = Flag(IGpuOnlyConnectionSwitch_Support)')
