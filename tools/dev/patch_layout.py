# -*- coding: utf-8 -*-
"""构建末尾强制布局（修复 AutoSize 时序）+ 诊断行高/cb 信息"""
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

# 1) 构建末尾：强制布局链
rep(
    """		_quickSwitches = list.ToArray();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;""",
    """		_quickSwitches = list.ToArray();
		root.PerformLayout();
		panel.PerformLayout();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;""",
    'force layout after build'
)

# 2) toggled 诊断：行高 + 第一个 sleep cb 信息
rep(
    """			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} rootRows={qsRoot?.RowCount} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height} sleepCount={sleepPanel?.Controls.Count}");""",
    """			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} rootRows={qsRoot?.RowCount} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height} sleepCount={sleepPanel?.Controls.Count}");
			if (qsRoot != null)
			{
				Logger.WriteLine("[QS] rowheights=" + string.Join("/", qsRoot.GetRowHeights()));
			}
			if (sleepPanel is FlowLayoutPanel sleepFlp && sleepFlp.Controls.Count > 0 && sleepFlp.Controls[0] is RCheckBox firstSleep)
			{
				Logger.WriteLine($"[QS] firstSleep text=[{firstSleep.Text}] size={firstSleep.Size} pref={firstSleep.GetPreferredSize(Size.Empty)} vis={firstSleep.Visible}");
			}""",
    'toggle diag rows + cb'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
