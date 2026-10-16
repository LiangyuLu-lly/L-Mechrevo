# -*- coding: utf-8 -*-
"""AC/DC 按钮：列改 AutoSize，按钮 AutoSize+MinimumSize(64,24)+Padding，防截断防超高"""
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 列样式
old_cols = '''				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));'''
new_cols = '''				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));'''
assert s.count(old_cols) == 1, 'cols match failed'
s = s.replace(old_cols, new_cols)
print('OK cols AutoSize')

# 2) btnAc
old_ac = '''				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					Dock = DockStyle.Fill,
					Margin = new Padding(6, 2, 2, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
new_ac = '''				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					AutoSize = true,
					MinimumSize = new Size(64, 24),
					MaximumSize = new Size(200, 24),
					Padding = new Padding(8, 0, 8, 0),
					Margin = new Padding(6, 2, 2, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
assert s.count(old_ac) == 1, 'btnAc match failed'
s = s.replace(old_ac, new_ac)
print('OK btnAc')

# 3) btnDc
old_dc = '''				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					Dock = DockStyle.Fill,
					Margin = new Padding(2, 2, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
new_dc = '''				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					AutoSize = true,
					MinimumSize = new Size(64, 24),
					MaximumSize = new Size(200, 24),
					Padding = new Padding(8, 0, 8, 0),
					Margin = new Padding(2, 2, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
assert s.count(old_dc) == 1, 'btnDc match failed'
s = s.replace(old_dc, new_dc)
print('OK btnDc')

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ALL DONE')
