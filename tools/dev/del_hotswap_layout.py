# -*- coding: utf-8 -*-
"""删除布局层 HotSwap 死代码（保留 Capabilities.GpuHotSwap 供诊断）"""

def sub_file(path, pairs, label):
    t = open(path, encoding='utf-8', errors='replace').read()
    for old, new in pairs:
        if old not in t:
            print(f'[{label}] MISS: {old[:80]!r}')
            return False
        t = t.replace(old, new, 1)
    open(path, 'w', encoding='utf-8', newline='').write(t)
    print(f'[{label}] OK')
    return True

# 1. GpuRowLayout.cs 枚举删 HotSwap
sub_file(r'D:\openLM\source\MechrevoLite.Gpu\GpuRowLayout.cs', [
    ('\tMux3,\n\tHotSwap\n}', '\tMux3\n}'),
], 'GpuRowLayout')

# 2. GpuRowLayouts.cs Resolve 删 hotSwap 参和分支
sub_file(r'D:\openLM\source\MechrevoLite.Gpu\GpuRowLayouts.cs', [
    ('public static GpuRowLayout Resolve(bool nvPreference, bool modeSwitch, bool hotSwap, bool igpuMuxTarget)',
     'public static GpuRowLayout Resolve(bool nvPreference, bool modeSwitch, bool igpuMuxTarget)'),
    ('\t\tif (hotSwap)\n\t\t{\n\t\t\treturn GpuRowLayout.HotSwap;\n\t\t}\n', ''),
], 'GpuRowLayouts')

# 3. MechrevoHw.cs 删 SupportsGpuHotSwap / CanOfferGpuHotSwap / Resolve 第3参
sub_file(r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs', [
    ('''	public bool SupportsGpuHotSwap
	{
		get
		{
			if (Capabilities.GpuHotSwap)
			{
				return SupportsIgpuOnly;
			}
			return false;
		}
	}

''', ''),
    ('''	public bool CanOfferGpuHotSwap
	{
		get
		{
			if (SupportsGpuHotSwap)
			{
				return IsGpuActionAllowedByGeneration("GPU_HOTSWAP_ON");
			}
			return false;
		}
	}

''', ''),
    ('public GpuRowLayout GpuRowLayout => GpuRowLayouts.Resolve(false, CanOfferGpuModeSwitch, CanOfferGpuModeSwitch && CanOfferGpuHotSwap, CanOfferIgpuOnly);',
     'public GpuRowLayout GpuRowLayout => GpuRowLayouts.Resolve(false, CanOfferGpuModeSwitch, CanOfferIgpuOnly);'),
], 'MechrevoHw')

# 4. SettingsForm.cs flag 段 + case HotSwap
sub_file(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', [
    ('''		int num = ResolveHighlightedGpuMode(GPUMode);
		bool flag = _gpuRowLayout == GpuRowLayout.HotSwap && num == 2;
		bool flag2 = !IsReadOnlyDegraded;
		ButtonEnabled(buttonOptimized, flag2);
		ButtonEnabled(buttonEco, flag2 && !flag);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, flag ? Strings.GpuIgpuNeedsStandard : Strings.GpuRouteIgpuTip);''',
     '''		int num = ResolveHighlightedGpuMode(GPUMode);
		bool flag2 = !IsReadOnlyDegraded;
		ButtonEnabled(buttonOptimized, flag2);
		ButtonEnabled(buttonEco, flag2);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, Strings.GpuRouteIgpuTip);'''),
    ('\t\tcase GpuRowLayout.HotSwap:\n', ''),
], 'SettingsForm')
