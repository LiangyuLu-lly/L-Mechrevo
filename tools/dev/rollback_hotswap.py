# -*- coding: utf-8 -*-
"""回退热切换执行路径（保留 GpuRowLayout 布局判定/路由回读高亮/重启确认闭环）"""
import re

def patch(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag + ' count=' + str(t.count(old))
    t = t.replace(old, new, 1)
    open(path, 'w', encoding='utf-8', newline='').write(t)
    print('OK:', tag)

# 1) GpuSwitchPolicy：HotSwitch 路由 → Restart（主界面点集显 → 弹"重启以应用"）
patch(r'D:\openLM\source\MechrevoLite.Hardware\GpuSwitchPolicy.cs',
'''		if ((num == 1 && targetMode == 0) & supportsHotSwap)
		{
			return new GpuSwitchPlan(GpuSwitchRoute.HotSwitch, RequiresDgpuProcessPreflight: false);
		}''',
'''		if ((num == 1 && targetMode == 0) & supportsHotSwap)
		{
			// 回退（2026-10-03）：热切换执行与核显省电功能重叠，50 系切集显统一走重启后应用；
			// GpuRowLayout 布局判定与路由回读高亮保留不变
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}''',
'GpuSwitchPolicy HotSwitch→Restart')

# 2) GPUModeControl：hotIgpuSwitch 恒 false
patch(r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs',
'''				int modeBeforeSwitch = Program.hw.GpuMode;
				bool hotIgpuSwitch = GPUMode == 0 && Program.hw.CanOfferGpuHotSwap && modeBeforeSwitch switch
				{
					3 => Program.hw.GpuSwitchResult == 1, 
					1 => true, 
					_ => false, 
				};''',
'''				int modeBeforeSwitch = Program.hw.GpuMode;
				// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
				bool hotIgpuSwitch = false;''',
'GPUModeControl hotIgpuSwitch=false')

# 3) MechrevoService：hotSwitchRequest 恒 false（服务端不再走热切换语义）
patch(r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs',
'''			hotSwitchRequest = mode == 0 && _hw.SupportsGpuHotSwap && (CurrentGpuMode == 1 || (CurrentGpuMode == 3 && _hw.GpuSwitchResult == 1));''',
'''			// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
			hotSwitchRequest = false;''',
'MechrevoService hotSwitchRequest=false')

# 4) MechrevoHw.SetGpuMode：flag 恒 false
patch(r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs',
'''		bool flag = mode == 0 && SupportsGpuHotSwap;
		bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly && !flag;''',
'''		// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
		bool flag = false;
		bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly && !flag;''',
'MechrevoHw SetGpuMode flag=false')

print('DONE 回退 4 处')
