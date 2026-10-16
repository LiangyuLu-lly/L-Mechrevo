# -*- coding: utf-8 -*-
"""修复 flowLayoutPanel AutoSize + 增强 toggled 诊断"""
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

# 1) flowLayoutPanel 加 AutoSize（内容跟随，行高稳定）
rep(
    """			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Name = "quickGroup_" + item2[0],
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				AutoScroll = false,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = panel.BackColor
			};""",
    """			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Name = "quickGroup_" + item2[0],
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				AutoScroll = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = panel.BackColor
			};""",
    'flowLayoutPanel AutoSize'
)

# 2) toggled 诊断增强：输出 root 与第 4 组 panel 高度
rep(
    """			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height}");""",
    """			Control? qsRoot = control2.Controls["quickSwitchRoot"];
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];
			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height}");""",
    'toggle log enhanced'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
