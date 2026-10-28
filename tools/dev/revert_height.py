# -*- coding: utf-8 -*-
"""回退容器高度相关修改：panelQuickSwitch AutoSize、flowLayoutPanel AutoSize、强制 PerformLayout"""
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

def rep(old, new, label, cnt=1):
    global src
    n = src.count(old)
    assert n == cnt, 'expect %d match for [%s], got %d' % (cnt, label, n)
    src = src.replace(old, new)
    print('OK:', label)

# 1) panelQuickSwitch 去掉 AutoSize（恢复原版）
rep(
    """		BufferedPanel panel = new BufferedPanel
		{
			Name = "panelQuickSwitch",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			CardStyle = true,""",
    """		BufferedPanel panel = new BufferedPanel
		{
			Name = "panelQuickSwitch",
			Dock = DockStyle.Top,
			CardStyle = true,""",
    'panelQuickSwitch no AutoSize'
)

# 2) flowLayoutPanel 去掉 AutoSize（恢复原版）
rep(
    """			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Name = "quickGroup_" + item2[0],
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				AutoScroll = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Margin = Padding.Empty,""",
    """			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Name = "quickGroup_" + item2[0],
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				AutoScroll = false,
				Margin = Padding.Empty,""",
    'flowLayoutPanel no AutoSize'
)

# 3) 移除强制 PerformLayout
rep(
    """		_quickSwitches = list.ToArray();
		root.PerformLayout();
		panel.PerformLayout();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;""",
    """		_quickSwitches = list.ToArray();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;""",
    'remove forced layout'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
