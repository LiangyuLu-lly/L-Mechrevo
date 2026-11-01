# -*- coding: utf-8 -*-
"""AC/DC 按钮：固定尺寸 56x22，完全一致，不再 AutoSize"""
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
s = io.open(p, encoding='utf-8').read()

old_ac = '''				RButton btnAc = new RButton
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
new_ac = '''				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					Size = new Size(56, 22),
					MinimumSize = new Size(56, 22),
					MaximumSize = new Size(56, 22),
					Padding = Padding.Empty,
					Margin = new Padding(6, 3, 2, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
assert s.count(old_ac) == 1, 'btnAc match failed'
s = s.replace(old_ac, new_ac)
print('OK btnAc fixed')

old_dc = '''				RButton btnDc = new RButton
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
new_dc = '''				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					Size = new Size(56, 22),
					MinimumSize = new Size(56, 22),
					MaximumSize = new Size(56, 22),
					Padding = Padding.Empty,
					Margin = new Padding(2, 3, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};'''
assert s.count(old_dc) == 1, 'btnDc match failed'
s = s.replace(old_dc, new_dc)
print('OK btnDc fixed')

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ALL DONE')
