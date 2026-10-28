# -*- coding: utf-8 -*-
"""路由层整理：GpuSwitchPolicy 删 supportsHotSwap 参数，1→0 无条件 Restart"""

# 1. GpuSwitchPolicy.cs
p1 = r'D:\openLM\source\MechrevoLite.Hardware\GpuSwitchPolicy.cs'
t = open(p1, encoding='utf-8', errors='replace').read()

old_sig = '\tinternal static GpuSwitchPlan Resolve(int reportedMode, int automaticRuntime, int targetMode, bool supportsHotSwap, bool currentStateFresh = true)'
new_sig = '\tinternal static GpuSwitchPlan Resolve(int reportedMode, int automaticRuntime, int targetMode, bool currentStateFresh = true)'
assert t.count(old_sig) == 1, 'sig not found'
t = t.replace(old_sig, new_sig, 1)

old_branch = '''		if ((num == 1 && targetMode == 0) & supportsHotSwap)
		{
			// 回退（2026-10-03）：热切换执行与核显省电功能重叠，50 系切集显统一走重启后应用；
			// GpuRowLayout 布局判定与路由回读高亮保留不变
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}'''
new_branch = '''		if (num == 1 && targetMode == 0)
		{
			// 2026-10-03：能切集显的机型（三模/50 系，CanOfferIgpuOnly）统一走重启后应用；
			// 判断依据与核显省电行一致，不再重复传参
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}'''
assert t.count(old_branch) == 1, 'branch not found'
t = t.replace(old_branch, new_branch, 1)
open(p1, 'w', encoding='utf-8', newline='').write(t)
print('GpuSwitchPolicy OK')

# 2. SettingsForm.cs 调用点
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
old_call = 'GpuSwitchPlan gpuSwitchPlan = GpuSwitchPolicy.Resolve(Program.service.CurrentGpuMode, Program.hw.GpuSwitchResult, targetMode, Program.hw.CanOfferIgpuOnly, gpuModeStatusReadback == GpuModeStatusReadback.Fresh);'
new_call = 'GpuSwitchPlan gpuSwitchPlan = GpuSwitchPolicy.Resolve(Program.service.CurrentGpuMode, Program.hw.GpuSwitchResult, targetMode, gpuModeStatusReadback == GpuModeStatusReadback.Fresh);'
assert t2.count(old_call) == 1, 'call not found'
t2 = t2.replace(old_call, new_call, 1)
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('SettingsForm OK')
