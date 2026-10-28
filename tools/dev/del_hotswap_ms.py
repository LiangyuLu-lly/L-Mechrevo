# -*- coding: utf-8 -*-
"""MechrevoService 完整清理（3a-3m），修正定位用独立行锚点"""
import os

p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'
t = open(p, encoding='utf-8', errors='replace').read()

def must(tag, cond):
    assert cond, 'FAIL: ' + tag

# 3a. HotSwitchStatusPollLimit 字段
old = '\tinternal static int HotSwitchStatusPollLimit { get; set; } = 61;\n'
must('3a', old in t and t.count(old) == 1)
t = t.replace(old, '', 1)
print('3a OK')

# 3b. ShouldRetryHotSwitchPoll 方法（整方法 + 前后空行规整）
old = '''	internal static bool ShouldRetryHotSwitchPoll(int poll)
	{
		return IgpuOnlySemantics.ShouldResend(poll);
	}

'''
must('3b', old in t)
t = t.replace(old, '', 1)
print('3b OK')

# 3c. 签名
old = '\tpublic async Task<bool> SwitchGpuMode(int mode, bool autoRestart = false, bool? pluggedForAuto = null, bool keepRegisterOnHotSwitchTimeout = false)'
new = '\tpublic async Task<bool> SwitchGpuMode(int mode, bool autoRestart = false, bool? pluggedForAuto = null)'
must('3c', old in t)
t = t.replace(old, new, 1)
print('3c OK')

# 3d. 声明
old = '''		int expectedAutoRuntime;
		bool hotSwitchRequest;
		long gpuModeStatusVersion;
		long hotSwitchResultVersion;
		bool requiresFreshHotSwitchResult;'''
new = '''		int expectedAutoRuntime;
		long gpuModeStatusVersion;
		long hotSwitchResultVersion;'''
must('3d', old in t)
t = t.replace(old, new, 1)
print('3d OK')

# 3e. hotSwitchRequest 赋值 + BuildSwitchCommand 调用
old = '''			// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
			hotSwitchRequest = false;
			bool muxTargetRequest = mode == 0 && _hw.SupportsDgpuDirect && !_hw.SupportsIgpuOnly && !hotSwitchRequest;
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			hotSwitchResultVersion = _hw.GpuSwitchResultVersion;
			requiresFreshHotSwitchResult = hotSwitchRequest && _hw.GpuSwitchResultReported;
			if (hotSwitchRequest)
			{
				Volatile.Write(ref _longRunningSwitchCts, requestCts);
			}
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, _hw.SupportsDgpuDirect, _hw.SupportsIgpuOnly, hotSwitchRequest, _hw.DgpuGeneration);'''
new = '''			// 2026-10-03：热切换执行已删除（与核显省电功能重叠），切集显统一走重启后应用
			bool muxTargetRequest = mode == 0 && _hw.SupportsDgpuDirect && !_hw.SupportsIgpuOnly;
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			hotSwitchResultVersion = _hw.GpuSwitchResultVersion;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, _hw.SupportsDgpuDirect, _hw.SupportsIgpuOnly, _hw.DgpuGeneration);'''
must('3e', old in t)
t = t.replace(old, new, 1)
print('3e OK')

# 3f. 确认前置简化
old = '''			flag = !hotSwitchRequest;
			if (flag)
			{
				flag = await _hw.WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1200L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			bool confirmed = flag;'''
new = '''			bool confirmed = await _hw.WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1200L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);'''
must('3f', old in t)
t = t.replace(old, new, 1)
print('3f OK')

# 3g. confirmationAttempts / delay
old = '''			int confirmationAttempts;
			if (hotSwitchRequest)
			{
				confirmationAttempts = HotSwitchStatusPollLimit;
			}
			else
			{
				confirmationAttempts = ((expectedAutoRuntime >= 0) ? 2 : 7);
			}
			TimeSpan confirmationPollDelay = (hotSwitchRequest ? TimeSpan.FromMilliseconds(2000L) : TimeSpan.FromMilliseconds(1800L));'''
new = '''			int confirmationAttempts = ((expectedAutoRuntime >= 0) ? 2 : 7);
			TimeSpan confirmationPollDelay = TimeSpan.FromMilliseconds(1800L);'''
must('3g', old in t)
t = t.replace(old, new, 1)
print('3g OK')

# 3h. 轮询内分支（用 repr 确认的 2-tab if 开头；结束定位到独立的 'flag = flag2;' 行 + 其 4-tab 闭合行）
start_marker = '\t\tif (hotSwitchRequest)'
i = t.find(start_marker)
must('3h start', i >= 0)
line_start = t.rfind('\n', 0, i) + 1
j = t.find('flag = flag2;', i)
must('3h flag2', j >= 0)
# flag = flag2; 行结束后的闭合行：独立 '\n\t\t\t\t}'（4 tab + } + \n）
k = t.find('\n\t\t\t\t}', j)
must('3h end', k >= 0)
end = k + len('\n\t\t\t\t}')
print('=== 3h 待删块 ===')
print(t[line_start:end])
new3h = '''\t\t\t\tbool flag2;
\t\t\t\tif (expectedAutoRuntime >= 0)
\t\t\t\t{
\t\t\t\t\tflag2 = attempt == 0;
\t\t\t\t}
\t\t\t\telse
\t\t\t\t{
\t\t\t\t\tbool flag3;
\t\t\t\t\tswitch (attempt)
\t\t\t\t\t{
\t\t\t\t\tcase 1:
\t\t\t\t\tcase 3:
\t\t\t\t\tcase 5:
\t\t\t\t\t\tflag3 = true;
\t\t\t\t\t\tbreak;
\t\t\t\t\tdefault:
\t\t\t\t\t\tflag3 = false;
\t\t\t\t\t\tbreak;
\t\t\t\t\t}
\t\t\t\t\tflag2 = flag3;
\t\t\t\t}
\t\t\t\tflag = flag2;
'''
t = t[:line_start] + new3h + t[end:]
print('3h OK')

# 3i. 未确认回滚块（独立行定位：\n\t\t\t}\n）
i = t.find('\n\t\t\tif (!confirmed & hotSwitchRequest)')
must('3i start', i >= 0)
line_start = i + 1
k = t.find('\n\t\t\t}\n', i)
must('3i end', k >= 0)
end = k + len('\n\t\t\t}\n')
print('=== 3i 待删块 ===')
print(t[line_start:end])
t = t[:line_start] + t[end:]
print('3i OK')

# 3j. 日志
old = '			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} hotSwitch={hotSwitchRequest} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");'
new = '			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult}/{expectedAutoRuntime} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");'
must('3j', old in t and t.count(old) == 1)
t = t.replace(old, new, 1)
print('3j OK')

# 3k. TargetReached 简化（独立行定位 return true）
i = t.find('\n\t\t\tif (!hotSwitchRequest && _hw.GpuModeStatusVersion <= gpuModeStatusVersion)')
must('3k start', i >= 0)
line_start = i + 1
k = t.find('\n\t\t\treturn true;\n', i)
must('3k end', k >= 0)
end = k + len('\n\t\t\treturn true;\n')
print('=== 3k 待删块 ===')
print(t[line_start:end])
new3k = '''\t\t\tif (_hw.GpuModeStatusVersion <= gpuModeStatusVersion)
\t\t\t{
\t\t\t\treturn false;
\t\t\t}
\t\t\treturn true;
'''
t = t[:line_start] + new3k + t[end:]
print('3k OK')

# 3l. RollbackFailedHotSwitchAsync 方法删除
i = t.find('\tinternal async Task RollbackFailedHotSwitchAsync(int modeBeforeSwitch, CancellationToken token)')
must('3l start', i >= 0)
line_start = t.rfind('\n', 0, i) + 1
k = t.find('\n\n\tpublic async Task<bool> SwitchGpuPowerSaving', i)
must('3l end', k >= 0)
end = k + 1
print('=== 3l 待删方法 ===')
print(t[line_start:end])
t = t[:line_start] + t[end:]
print('3l OK')

# 3m. CreateGpuSwitchPayload
old = '\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, bool useHotSwitch)'
new = '\tinternal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly)'
must('3m sig', old in t)
t = t.replace(old, new, 1)
old = '\t\tif (!((mode == 0) & supportsDgpuDirect) || useHotSwitch || supportsIgpuOnly)'
new = '\t\tif (!((mode == 0) & supportsDgpuDirect) || supportsIgpuOnly)'
must('3m cond', old in t)
t = t.replace(old, new, 1)
print('3m OK')

open(p, 'w', encoding='utf-8', newline='').write(t)
print('MechrevoService 清理完成，剩余 hotSwitch 引用：', t.count('hotSwitch'), ' hotIgpuSwitch:', t.count('hotIgpuSwitch'))
