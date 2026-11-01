# -*- coding: utf-8 -*-
"""阶段3：MechrevoService + GpuRouteCommandLayer + MechrevoHw + GpuSwitchRoute + 文件删除"""
import os

def del_lines(path, start, end, tag):
    lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
    assert 1 <= start <= end <= len(lines), f'{tag}: bad range {start}-{end} len={len(lines)}'
    print(f'--- {tag} 删除 {start}-{end} ---')
    for i in range(start-1, end):
        print(f'{i+1:6d} | {lines[i][:70]}')
    del lines[start-1:end]
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
    print('OK: ' + tag + '\n')

def replace(path, old, new, tag):
    t = open(path, encoding='utf-8', errors='replace').read()
    assert old in t, 'NOT FOUND: ' + tag
    assert t.count(old) == 1, 'NOT UNIQUE: ' + tag
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new, 1))
    print('OK: ' + tag)

S = r'D:\openLM\source'
ms = os.path.join(S, r'MechrevoLite.Hardware\MechrevoService.cs')

# 3a. HotSwitchStatusPollLimit 字段删除
replace(ms, '\tinternal static int HotSwitchStatusPollLimit { get; set; } = 61;\n', '', 'Service 3a')

# 3b. ShouldRetryHotSwitchPoll 方法删除（212-216 含尾空行）
del_lines(ms, 212, 216, 'Service 3b')

# 3c. SwitchGpuMode 签名删 keepRegisterOnHotSwitchTimeout
replace(ms,
'\tpublic async Task<bool> SwitchGpuMode(int mode, bool autoRestart = false, bool? pluggedForAuto = null, bool keepRegisterOnHotSwitchTimeout = false)',
'\tpublic async Task<bool> SwitchGpuMode(int mode, bool autoRestart = false, bool? pluggedForAuto = null)',
'Service 3c')

# 3d. 局部变量声明删 hotSwitchRequest / requiresFreshHotSwitchResult
replace(ms,
'''		int expectedAutoRuntime;
		bool hotSwitchRequest;
		long gpuModeStatusVersion;
		long hotSwitchResultVersion;
		bool requiresFreshHotSwitchResult;''',
'''		int expectedAutoRuntime;
		long gpuModeStatusVersion;
		long hotSwitchResultVersion;''',
'Service 3d')

# 3e. hotSwitchRequest 赋值块 + BuildSwitchCommand 调用（1884-1894）
replace(ms,
'''			// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
			hotSwitchRequest = false;
			bool muxTargetRequest = mode == 0 && _hw.SupportsDgpuDirect && !_hw.SupportsIgpuOnly && !hotSwitchRequest;
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			hotSwitchResultVersion = _hw.GpuSwitchResultVersion;
			requiresFreshHotSwitchResult = hotSwitchRequest && _hw.GpuSwitchResultReported;
			if (hotSwitchRequest)
			{
				Volatile.Write(ref _longRunningSwitchCts, requestCts);
			}
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, _hw.SupportsDgpuDirect, _hw.SupportsIgpuOnly, hotSwitchRequest, _hw.DgpuGeneration);''',
'''			// 2026-10-03：热切换执行已删除（与核显省电功能重叠），切集显统一走重启后应用
			bool muxTargetRequest = mode == 0 && _hw.SupportsDgpuDirect && !_hw.SupportsIgpuOnly;
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			hotSwitchResultVersion = _hw.GpuSwitchResultVersion;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, _hw.SupportsDgpuDirect, _hw.SupportsIgpuOnly, _hw.DgpuGeneration);''',
'Service 3e')

# 3f. 确认轮询前置简化（1927-1932）
replace(ms,
'''			flag = !hotSwitchRequest;
			if (flag)
			{
				flag = await _hw.WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1200L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			bool confirmed = flag;''',
'''			bool confirmed = await _hw.WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1200L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);''',
'Service 3f')

# 3g. confirmationAttempts / confirmationPollDelay 简化（1933-1942）
replace(ms,
'''			int confirmationAttempts;
			if (hotSwitchRequest)
			{
				confirmationAttempts = HotSwitchStatusPollLimit;
			}
			else
			{
				confirmationAttempts = ((expectedAutoRuntime >= 0) ? 2 : 7);
			}
			TimeSpan confirmationPollDelay = (hotSwitchRequest ? TimeSpan.FromMilliseconds(2000L) : TimeSpan.FromMilliseconds(1800L));''',
'''			int confirmationAttempts = ((expectedAutoRuntime >= 0) ? 2 : 7);
			TimeSpan confirmationPollDelay = TimeSpan.FromMilliseconds(1800L);''',
'Service 3g')

# 3h. 轮询内 hotSwitchRequest 分支删除（1952-1980）
replace(ms,
'''					if (hotSwitchRequest)
					{
						flag = ShouldRetryHotSwitchPoll(attempt);
					}
					else
					{
						bool flag2;
						if (expectedAutoRuntime >= 0)
						{
							flag2 = attempt == 0;
						}
						else
						{
							bool flag3;
							switch (attempt)
							{
							case 1:
							case 3:
							case 5:
								flag3 = true;
								break;
							default:
								flag3 = false;
								break;
							}
							flag2 = flag3;
						}
						flag = flag2;
					}''',
'''					bool flag2;
					if (expectedAutoRuntime >= 0)
					{
						flag2 = attempt == 0;
					}
					else
					{
						bool flag3;
						switch (attempt)
						{
						case 1:
						case 3:
						case 5:
							flag3 = true;
							break;
						default:
							flag3 = false;
							break;
						}
						flag2 = flag3;
					}
					flag = flag2;''',
'Service 3h')

# 3i. 热切换未确认回滚块删除（1987-1997）
replace(ms,
'''				if (!confirmed & hotSwitchRequest)
				{
					if (!keepRegisterOnHotSwitchTimeout)
					{
						await RollbackFailedHotSwitchAsync(modeBeforeSwitch, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
					else
					{
						Logger.WriteLine("Hot switch not confirmed; register kept for the reboot-to-apply prompt.");
					}
				}
''',
'',
'Service 3i')

# 3j. 日志删 hotSwitch 字段
replace(ms,
'			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} hotSwitch={hotSwitchRequest} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");',
'			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");',
'Service 3j')

# 3k. TargetReached 里 !hotSwitchRequest 简化
replace(ms,
'''				if (!hotSwitchRequest && _hw.GpuModeStatusVersion <= gpuModeStatusVersion)
				{
					return false;
				}
				if (requiresFreshHotSwitchResult)
				{
					if (_hw.GpuSwitchResultVersion > hotSwitchResultVersion)
					{
						return IgpuOnlySemantics.IsSuccess(_hw.GpuSwitchResult, turningOn: true);
					}
					return false;
				}
				return true;''',
'''				if (_hw.GpuModeStatusVersion <= gpuModeStatusVersion)
				{
					return false;
				}
				return true;''',
'Service 3k')

# 3l. RollbackFailedHotSwitchAsync 方法删除（2073-2096 含尾空行）
del_lines(ms, 2073, 2096, 'Service 3l')

# 3m. CreateGpuSwitchPayload 签名 + 条件简化
replace(ms,
'\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch)',
'\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly)',
'Service 3m')
replace(ms,
'\t\tif (!((mode == 0) & supportsDgpuDirect) || useHotSwitch || supportsIgpuOnly)',
'\t\tif (!((mode == 0) & supportsDgpuDirect) || supportsIgpuOnly)',
'Service 3m2')

# ============ GpuRouteCommandLayer.cs ============
gr = os.path.join(S, r'MechrevoLite.Hardware\GpuRouteCommandLayer.cs')
replace(gr,
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch, DgpuGenerationKind generation)',
'\tpublic static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, DgpuGenerationKind generation)',
'RouteLayer 4a')
replace(gr,
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly, useHotSwitch);',
'\t\tDictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly);',
'RouteLayer 4b')

# ============ MechrevoHw.cs ============
mh = os.path.join(S, r'MechrevoLite.Hardware\MechrevoHw.cs')
replace(mh,
'''			// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
			bool flag = false;
			bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly && !flag;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, SupportsDgpuDirect, SupportsIgpuOnly, flag, DgpuGeneration);''',
'''			// 2026-10-03：热切换执行已删除（与核显省电功能重叠）
			bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, SupportsDgpuDirect, SupportsIgpuOnly, DgpuGeneration);''',
'MechrevoHw 5a')

# ============ GpuSwitchRoute.cs：删 HotSwitch 枚举 ============
gsr = os.path.join(S, r'MechrevoLite.Hardware\GpuSwitchRoute.cs')
t = open(gsr, encoding='utf-8', errors='replace').read()
old = '\tHotSwitch,\n'
assert old in t, 'GpuSwitchRoute HotSwitch not found'
t = t.replace(old, '', 1)
open(gsr, 'w', encoding='utf-8', newline='').write(t)
print('OK: GpuSwitchRoute HotSwitch 枚举删除')

# ============ 文件删除（无引用） ============
f1 = os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationCoordinator.cs')
f2 = os.path.join(S, r'MechrevoLite.Gpu\DgpuApplicationSnapshot.cs')
for f in (f1, f2):
    if os.path.exists(f):
        os.remove(f)
        print('OK: 删除文件 ' + os.path.basename(f))

print('PHASE3 DONE')
