# -*- coding: utf-8 -*-
import re

# 1) SettingsDialog.cs: startupCheck 背景对齐设置对话框原生开关（UiVisualStyle.Window，浅色下为浅灰而非纯白）
p = r'D:\openLM\source\MechrevoLite\SettingsDialog.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}'''
new = '''		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.BackColor = UiVisualStyle.Window;
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}'''
assert t.count(old) == 1, 'startupCheck block not found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsDialog.cs: startupCheck.BackColor = Window')

# 2) SettingsForm.cs: 官方控制台状态 Label 回退为 Dock=Fill + AutoEllipsis（12:08 方案，实机单行完整）
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
pat = re.compile(r'(_officialConsoleStatus = new Label\s*\{)([^{}]*?)(\})', re.S)
m = pat.search(t2)
assert m, 'status label block not found'
block = m.group(2)
# 去掉 AutoSize=true 与 Dock=None/Anchor=Right，恢复 Dock=Fill
block = re.sub(r'[ \t]*AutoSize = true,\r?\n', '', block)
block = re.sub(r'[ \t]*Dock = DockStyle\.None,\r?\n', '', block)
block = re.sub(r'[ \t]*Anchor = AnchorStyles\.Right,\r?\n', '', block)
if 'Dock = DockStyle.Fill' not in block:
    block = block.replace('Text = Strings.Detecting,', 'Text = Strings.Detecting,\n\t\t\t\tDock = DockStyle.Fill,')
t2 = t2[:m.start()] + m.group(1) + block + m.group(3) + t2[m.end():]
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('SettingsForm.cs: status label back to Dock=Fill')
