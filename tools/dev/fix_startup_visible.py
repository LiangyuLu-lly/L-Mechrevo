# -*- coding: utf-8 -*-
import re

# 1) SettingsForm.cs: 删除 checkStartup.Visible = false（旧版本栏隐藏逻辑残留）
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''		checkStartup.Visible = false;
'''
assert t.count(old) == 1, 'visible=false line not found'
t = t.replace(old, '', 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsForm.cs: removed checkStartup.Visible=false')

# 2) SettingsDialog.cs: startupCheck 加入系统标题行前确保可见
p2 = r'D:\openLM\source\MechrevoLite\SettingsDialog.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
old2 = '''		if (startupCheck != null)
		{
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}'''
new2 = '''		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}'''
assert t2.count(old2) == 1, 'startupCheck block not found'
t2 = t2.replace(old2, new2, 1)
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('SettingsDialog.cs: startupCheck.Visible=true')
