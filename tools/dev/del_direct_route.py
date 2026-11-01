# -*- coding: utf-8 -*-
"""对齐 beta21：删除 GpuSwitchRoute.Direct，0→1 也走 Restart"""

def sub_file(path, pairs, label):
    t = open(path, encoding='utf-8', errors='replace').read()
    for old, new in pairs:
        if old not in t:
            print(f'[{label}] MISS: {old[:90]!r}')
            return False
        t = t.replace(old, new, 1)
    open(path, 'w', encoding='utf-8', newline='').write(t)
    print(f'[{label}] OK')
    return True

# 1. 枚举删 Direct
sub_file(r'D:\openLM\source\MechrevoLite.Hardware\GpuSwitchRoute.cs', [
    ('internal enum GpuSwitchRoute\n{\n\tNoChange,\n\tDirect,\n\tRestart\n}',
     'internal enum GpuSwitchRoute\n{\n\tNoChange,\n\tRestart\n}'),
], 'GpuSwitchRoute')

# 2. 路由 else → Restart
sub_file(r'D:\openLM\source\MechrevoLite.Hardware\GpuSwitchPolicy.cs', [
    ('''		if (num == 1 && targetMode == 0)
		{
			// 2026-10-03：能切集显的机型（三模/50 系，CanOfferIgpuOnly）统一走重启后应用；
			// 判断依据与核显省电行一致，不再重复传参
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}
		return new GpuSwitchPlan(GpuSwitchRoute.Direct, RequiresDgpuProcessPreflight: false);''',
     '''		if (num == 1 && targetMode == 0)
		{
			// 2026-10-03：能切集显的机型（三模/50 系，CanOfferIgpuOnly）统一走重启后应用；
			// 判断依据与核显省电行一致，不再重复传参
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}
		// 2026-10-03：对齐 beta21 删除 Direct 路由——集显→混合(0→1)同样走重启后应用
		return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);'''),
], 'GpuSwitchPolicy')

# 3. SettingsForm 删 Direct case
sub_file(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', [
    ('''				switch (gpuSwitchPlan.Route)
				{
				case GpuSwitchRoute.Direct:
					await Program.service.SwitchGpuMode(targetMode);
					await RefreshGpuModeUiStateAsync();
					break;
				case GpuSwitchRoute.Restart:''',
     '''				switch (gpuSwitchPlan.Route)
				{
				case GpuSwitchRoute.Restart:'''),
], 'SettingsForm')
