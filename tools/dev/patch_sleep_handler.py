# -*- coding: utf-8 -*-
"""补 sleep handler 分支（patch2 第4处未生效）+ 增强诊断（sleepPanel.Controls.Count）"""
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

# 1) 补 sleep handler：在 string text = key2; 之前插入独立分支
rep(
    """				}
				string text = key2;
				if ((text == "gamewhitelist" || text == "cpuadvperf") ? true : false)""",
    """				}
				if (key2.StartsWith("sleep"))
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

# 2) 诊断增强：输出 sleepPanel.Controls.Count 与 root 行数
rep(
    """			Control? qsRoot = control2.Controls["quickSwitchRoot"];
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];
			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height}");""",
    """			Control? qsRoot = control2.Controls["quickSwitchRoot"];
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];
			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} rootRows={qsRoot?.RowCount} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height} sleepCount={sleepPanel?.Controls.Count}");""",
    'toggle log enhanced 2'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
