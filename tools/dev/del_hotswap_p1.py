# -*- coding: utf-8 -*-
"""删除热切换执行/确认/回滚死代码 v2（行号区间删除 + 精确替换）"""
import os

def del_lines(path, start, end, tag):
    """删除 [start, end] 行（1-based 含端点），从高到低调用"""
    lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
    assert 1 <= start <= end <= len(lines), f'{tag}: bad range {start}-{end} len={len(lines)}'
    # 打印核对
    print(f'--- {tag} 删除 {start}-{end} (共 {end-start+1} 行) ---')
    for i in range(start-1, end):
        print(f'{i+1:6d} | {lines[i][:80]}')
    del lines[start-1:end]
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
    print(f'OK: {tag}\n')

def replace(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag + ' count=' + str(t.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new, 1))
    print('OK: ' + tag)

S = r'D:\openLM\source'

# ============ GPUModeControl.cs ============
gp = os.path.join(S, r'MechrevoLite.Gpu\GPUModeControl.cs')

# 先删 1c+1d+1e 整块（461-577：NotifyAutomaticHotSwitchBlocked + 空行 + CloseDgpuApplicationsForManualSwitchAsync + 空行 + OfferRebootFallbackAsync）
del_lines(gp, 461, 577, 'GPUModeControl 1cde')

# 1a. hotIgpuSwitch 判定 + 预检分支 + 调用改 3 参（150-173）
replace(gp,
'''				int modeBeforeSwitch = Program.hw.GpuMode;
				// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
				bool hotIgpuSwitch = false;
				bool flag = hotIgpuSwitch;
				if (flag)
				{
					flag = !(await CloseDgpuApplicationsForManualSwitchAsync().ConfigureAwait(continueOnCapturedContext: false));
				}
				if (flag)
				{
					settings.VisualiseGPUMode(modeBeforeSwitch);
					return;
				}
				Interlocked.Exchange(ref _manualGpuSwitchInFlight, 1);
				bool flag2;
				try
				{
					flag = ((Program.service == null) ? (await Program.hw.SetGpuMode(GPUMode)) : (await Program.service.SwitchGpuMode(GPUMode, autoRestart: false, null, hotIgpuSwitch)));
					flag2 = flag;
				}
				finally
				{
					Interlocked.Exchange(ref _manualGpuSwitchInFlight, 0);
				}''',
'''				int modeBeforeSwitch = Program.hw.GpuMode;
				// 2026-10-03：热切换执行已删除（与核显省电功能重叠），切集显统一走重启后应用
				Interlocked.Exchange(ref _manualGpuSwitchInFlight, 1);
				bool flag2;
				try
				{
					flag2 = ((Program.service == null) ? (await Program.hw.SetGpuMode(GPUMode)) : (await Program.service.SwitchGpuMode(GPUMode, autoRestart: false)));
				}
				finally
				{
					Interlocked.Exchange(ref _manualGpuSwitchInFlight, 0);
				}''',
'GPUModeControl 1a')

# 1b. 热切换失败分支删除（181-186）
replace(gp,
'''					if (hotIgpuSwitch)
					{
						Logger.WriteLine($"Manual hot switch to iGPU not confirmed; offering reboot-to-apply (expected={GPUMode} actual={Program.hw.GpuMode})");
						await OfferRebootFallbackAsync(modeBeforeSwitch, GPUMode).ConfigureAwait(continueOnCapturedContext: false);
						return;
					}
					settings.VisualiseGPUMode(Program.hw.GpuMode);''',
'''					settings.VisualiseGPUMode(Program.hw.GpuMode);''',
'GPUModeControl 1b')

# ============ SettingsForm.cs ============
sf = os.path.join(S, r'MechrevoLite\SettingsForm.cs')

# 2c. PrepareDgpuApplicationsForHotSwitchAsync + DescribeDgpuApplications（6362-6425，含尾部空行）
del_lines(sf, 6362, 6425, 'SettingsForm 2c')

# 2a. DgpuPreflightResult 枚举（29-35，含空行）
del_lines(sf, 29, 35, 'SettingsForm 2a')

# 2b. case GpuSwitchRoute.HotSwitch 标签删除
replace(sf,
'''				case GpuSwitchRoute.Direct:
				case GpuSwitchRoute.HotSwitch:
					await Program.service.SwitchGpuMode(targetMode);''',
'''				case GpuSwitchRoute.Direct:
					await Program.service.SwitchGpuMode(targetMode);''',
'SettingsForm 2b')

print('PHASE1 DONE')
