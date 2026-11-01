# -*- coding: utf-8 -*-
"""SettingsForm.cs 第二处补丁：sleep 开关 handler、周期刷新回读、ReadQuickSwitchState"""
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

# 4) for 循环内：在 gamewhitelist/cpuadvperf 判断前插入 sleep 分支（处理完 continue）
rep(
    """				string text = key2;
				if ((text == "gamewhitelist" || text == "cpuadvperf") ? true : false)""",
    """				if (key2.StartsWith("sleep"))
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							bool requested = cb.Checked;
							_lastQuickSwitchUi = DateTime.Now;
							if (!PowerSleepGuard.SetEnabled(key2, requested))
							{
								ToastForm.ShowFailure(Strings.QuickSwitchFailed);
								RevertCheck(cb, PowerSleepGuard.ReadEnabled(key2) ?? (!requested));
							}
						}
					};
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}
				string text = key2;
				if ((text == "gamewhitelist" || text == "cpuadvperf") ? true : false)""",
    'sleep handler branch'
)

# 5) UpdateQuickSwitches：Personalization 之后插入 sleep 回读分支
rep(
    """					bool? flag = ReadWindowsPersonalizationSwitch(text);
					if (flag.HasValue)
					{
						checkBox.Checked = flag.Value;
					}
					else if (text == "startup")""",
    """					bool? flag = ReadWindowsPersonalizationSwitch(text);
					if (flag.HasValue)
					{
						checkBox.Checked = flag.Value;
					}
					else if (text.StartsWith("sleep"))
					{
						bool? sleepState = PowerSleepGuard.ReadEnabled(text);
						if (sleepState.HasValue)
						{
							checkBox.Checked = sleepState.Value;
						}
					}
					else if (text == "startup")""",
    'UpdateQuickSwitches sleep branch'
)

# 6) ReadQuickSwitchState：startup 前插入 sleep 回读
rep(
    """		bool? result = ReadWindowsPersonalizationSwitch(key);
		if (result.HasValue)
		{
			return result;
		}
		if (key == "startup")
		{
			return Startup.ReadScheduledState();
		}""",
    """		bool? result = ReadWindowsPersonalizationSwitch(key);
		if (result.HasValue)
		{
			return result;
		}
		if (key.StartsWith("sleep"))
		{
			return PowerSleepGuard.ReadEnabled(key);
		}
		if (key == "startup")
		{
			return Startup.ReadScheduledState();
		}""",
    'ReadQuickSwitchState sleep branch'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
