# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
t = open(p, encoding='utf-8').read()

# 1. SelectModeAsync: 应用成功后刷新硬件状态
old1 = '''	internal async Task<bool> SelectModeAsync(string id)
	{
		_debounce.Stop();
		await FlushPendingAsync();
		BindMode(id);
		return await ActivateModeAsync(id);
	}'''
new1 = '''	internal async Task<bool> SelectModeAsync(string id)
	{
		_debounce.Stop();
		await FlushPendingAsync();
		BindMode(id);
		bool ok = await ActivateModeAsync(id);
		if (ok)
		{
			await RefreshHardwareStateAsync();
		}
		return ok;
	}'''

# 2. OnPerfActiveChanged: 两个分支都补硬件状态刷新（编辑未保存时跳过）
old2 = '''	private void OnPerfActiveChanged()
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(OnPerfActiveChanged);
			return;
		}
		PerfModeService instance = PerfModeService.Instance;
		if (instance == null || !Visible)
		{
			return;
		}
		string displayedModeId = instance.DisplayedModeId;
		if (!string.Equals(displayedModeId, _modeId, StringComparison.Ordinal) && _pending.Count == 0)
		{
			BindMode(displayedModeId);
			return;
		}
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition != null)
		{
			UpdateRouteHint(perfModeDefinition);
		}
	}'''
new2 = '''	private async void OnPerfActiveChanged()
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(OnPerfActiveChanged);
			return;
		}
		PerfModeService instance = PerfModeService.Instance;
		if (instance == null || !Visible)
		{
			return;
		}
		string displayedModeId = instance.DisplayedModeId;
		if (!string.Equals(displayedModeId, _modeId, StringComparison.Ordinal) && _pending.Count == 0)
		{
			BindMode(displayedModeId);
			await RefreshHardwareStateAsync();
			return;
		}
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition != null)
		{
			UpdateRouteHint(perfModeDefinition);
		}
		if (_pending.Count == 0)
		{
			await RefreshHardwareStateAsync();
		}
	}'''

print('old1:', t.count(old1), 'old2:', t.count(old2))
if t.count(old1) != 1 or t.count(old2) != 1:
    print('匹配数异常，中止')
    raise SystemExit(1)
t = t.replace(old1, new1).replace(old2, new2)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK')
