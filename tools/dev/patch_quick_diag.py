# -*- coding: utf-8 -*-
"""临时诊断：输出更多开关相关容器高度到 Logger"""
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

def rep(old, new, label):
    global src
    cnt = src.count(old)
    assert cnt == 1, 'expect 1 match for [%s], got %d' % (label, cnt)
    src = src.replace(old, new)
    print('OK:', label)

# 1) BuildQuickSwitchPanel 末尾
rep(
    """		_quickSwitches = list.ToArray();
		bool fittingQuickGroups = false;""",
    """		_quickSwitches = list.ToArray();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;""",
    'build log'
)

# 2) 更多开关 Toggled
rep(
    """		_quickGroup.Toggled += (object? _, EventArgs _) =>
		{
			UpdateDashboardWindowHeight();
			if (_quickGroup.Expanded)
			{
				_fitQuickCards?.Invoke();
			}
			UpdateDashboardScroll();
		};""",
    """		_quickGroup.Toggled += (object? _, EventArgs _) =>
		{
			UpdateDashboardWindowHeight();
			if (_quickGroup.Expanded)
			{
				_fitQuickCards?.Invoke();
			}
			UpdateDashboardScroll();
			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height}");
		};""",
    'toggle log'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
