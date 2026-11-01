# -*- coding: utf-8 -*-
"""修复 panelQuickSwitch 高度：AutoSize 跟随 root，避免 4 组内容被裁剪"""
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

old = """		BufferedPanel panel = new BufferedPanel
		{
			Name = "panelQuickSwitch",
			Dock = DockStyle.Top,
			CardStyle = true,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(16, 12, 16, 12)
		};"""
new = """		BufferedPanel panel = new BufferedPanel
		{
			Name = "panelQuickSwitch",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			CardStyle = true,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(16, 12, 16, 12)
		};"""

cnt = src.count(old)
assert cnt == 1, 'expect 1 match, got %d' % cnt
src = src.replace(old, new)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('panelQuickSwitch AutoSize patched')
