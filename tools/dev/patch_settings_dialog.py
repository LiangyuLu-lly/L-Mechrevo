# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsDialog.cs'
t = open(p, encoding='utf-8', errors='replace').read()

# 检查签名当前状态
import re
m = re.search(r'public SettingsDialog\([^)]*\)', t)
print('current sig:', m.group(0) if m else 'NOT FOUND')

old_hdr = '''		AddHeader(Strings.SystemSection, 10);
		officialPanel.Dock = DockStyle.Top;'''
new_hdr = '''		// 系统标题行（含开机自启动开关，放在标题右侧）
		BufferedTableLayoutPanel systemHeader = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(10), 0, D(4)),
			BackColor = UiVisualStyle.Window
		};
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		systemHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label systemTitle = new Label
		{
			Text = Strings.SystemSection,
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, D(8), 0)
		};
		systemHeader.Controls.Add(systemTitle, 0, 0);
		if (startupCheck != null)
		{
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(systemHeader, 0, r++);
		officialPanel.Dock = DockStyle.Top;'''
assert t.count(old_hdr) == 1, 'header anchor not found'
t = t.replace(old_hdr, new_hdr, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsDialog.cs header patched')
