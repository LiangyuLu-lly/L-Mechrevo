# -*- coding: utf-8 -*-
import re
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()

def rep(old, new, cnt=1):
    global t
    assert t.count(old) == cnt, f'anchor mismatch ({t.count(old)} != {cnt}): {old[:60]!r}'
    t = t.replace(old, new, cnt)

# 1) 开机自启动不再进版本栏
rep('''		panelStartup.Controls.Remove(checkStartup);
		labelVersion.Dock = DockStyle.Left;
		labelVersion.AutoSize = false;
		labelVersion.Size = new Size(140, labelVersion.Height);
		labelVersion.Font = UiVisualStyle.Font(8.5f);
		labelVersion.AutoEllipsis = true;
		checkStartup.Dock = DockStyle.Right;
		panelVersion.Controls.Add(checkStartup);
		panelStartup.Visible = false;''',
'''		panelStartup.Controls.Remove(checkStartup);
		labelVersion.Dock = DockStyle.Left;
		labelVersion.AutoSize = false;
		labelVersion.Size = new Size(140, labelVersion.Height);
		labelVersion.Font = UiVisualStyle.Font(8.5f);
		labelVersion.AutoEllipsis = true;
		// 2026-10-03：开机自启动开关移至设置对话框"系统"标题右侧，不再占用版本栏
		panelStartup.Visible = false;''')

# 2) cpuadvperf 开关 tooltip（4 tab 缩进）
rep('''					bool whitelist = key2 == "gamewhitelist";''',
'''					if (key2 == "cpuadvperf")
					{
						_quickSleepToolTip.SetToolTip(cb, Strings.QuickCpuAdvPerfTip);
					}
					bool whitelist = key2 == "gamewhitelist";''')

# 3) OpenSettingsDialog 传入 checkStartup
rep('''			_settingsDialog = new SettingsDialog(_themeModePanel, _officialConsolePanel, _overdriveChk, _overdriveAvailable, _themeFollowBox);''',
'''			_settingsDialog = new SettingsDialog(_themeModePanel, _officialConsolePanel, _overdriveChk, _overdriveAvailable, _themeFollowBox, checkStartup);''')

# 4a) 官方控制台面板：收窄固定列
rep('''		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(126)));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(150)));''',
'''		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(110)));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(140)));''')

# 4b) _officialConsoleStatus 状态文字禁折行（正则块匹配，属性缩进不预设）
pat = re.compile(r'(_officialConsoleStatus = new Label\s*\{[^{}]*?AutoEllipsis = true,)', re.S)
m = pat.search(t)
assert m, 'officialConsoleStatus block not found'
t = t[:m.end()] + '\n\t\t\t\tWordWrap = false,' + t[m.end():]

open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsForm.cs patched (4 spots)')
