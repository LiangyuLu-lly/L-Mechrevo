# -*- coding: utf-8 -*-
"""标题行改为 TableLayoutPanel 三列（62/19/19），按钮 Dock=Fill 等高管控"""
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
s = io.open(p, encoding='utf-8').read()

old = '''				FlowLayoutPanel titleRow = new FlowLayoutPanel
				{
					Name = "quickGroupTitle_sleep",
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = panel.BackColor
				};
				titleRow.Controls.Add(label);
				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					AutoSize = true,
					Margin = new Padding(10, 6, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					AutoSize = true,
					Margin = new Padding(4, 6, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				titleRow.Controls.Add(btnAc);
				titleRow.Controls.Add(btnDc);'''

new = '''				TableLayoutPanel titleRow = new TableLayoutPanel
				{
					Name = "quickGroupTitle_sleep",
					ColumnCount = 3,
					RowCount = 1,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = panel.BackColor
				};
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
				titleRow.Controls.Add(label, 0, 0);
				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					Dock = DockStyle.Fill,
					Margin = new Padding(6, 2, 2, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					Dock = DockStyle.Fill,
					Margin = new Padding(2, 2, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				titleRow.Controls.Add(btnAc, 1, 0);
				titleRow.Controls.Add(btnDc, 2, 0);'''

assert s.count(old) == 1, 'match failed'
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('OK titleRow -> TableLayoutPanel')
