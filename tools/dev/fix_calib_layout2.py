# -*- coding: utf-8 -*-
# 校色设置弹窗布局修复（续）：间距拉开 + 提示换行 + 窗口加高
import io

def edit(path, old, new, tag, enc='utf-8'):
    t = io.open(path, encoding=enc).read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding=enc, newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\source\MechrevoLite\CalibrationStartupForm.cs'

# 1) 开机自动应用开关 + 提示下移
edit(p,
'Checked = AppConfig.Is("calib_apply_on_startup"),\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 62),',
'Checked = AppConfig.Is("calib_apply_on_startup"),\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 66),',
'startup-switch')

edit(p,
'Text = Strings.CalibApplyOnStartupHint,\n\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 96),\n\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'Text = Strings.CalibApplyOnStartupHint,\n\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\tAutoSize = true,\n\t\t\t\tMaximumSize = new Size(430, 0),\n\t\t\t\tLocation = new Point(26, 104),\n\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'startup-hint')

# 2) 自定义校色标题 + 开关 + 下拉 + 导入 + 提示整体下移，间距拉开
edit(p,
'Text = Strings.CustomCalibration,\n\t\t\t\tForeColor = UiVisualStyle.Text,\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(24, 140),',
'Text = Strings.CustomCalibration,\n\t\t\t\tForeColor = UiVisualStyle.Text,\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(24, 158),',
'custom-caption')

edit(p,
'Checked = AppConfig.Is("calib_custom_on"),\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 168),',
'Checked = AppConfig.Is("calib_custom_on"),\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 190),',
'custom-switch')

edit(p,
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tLocation = new Point(110, 166),\n\t\t\t\tSize = new Size(200, 26)',
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\tSize = new Size(200, 26)',
'custom-combo')

edit(p,
'Size = new Size(72, 28),\n\t\t\t\tLocation = new Point(318, 163),',
'Size = new Size(72, 28),\n\t\t\t\tLocation = new Point(318, 185),',
'import-btn')

edit(p,
'Text = Strings.CustomCalibrationHint,\n\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\tAutoSize = true,\n\t\t\t\tLocation = new Point(26, 206),\n\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'Text = Strings.CustomCalibrationHint,\n\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\tAutoSize = true,\n\t\t\t\tMaximumSize = new Size(430, 0),\n\t\t\t\tLocation = new Point(26, 234),\n\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'custom-hint')

# 3) 取消按钮下移
edit(p,
'Size = new Size(80, 28),\n\t\t\t\tLocation = new Point(200, 276),',
'Size = new Size(80, 28),\n\t\t\t\tLocation = new Point(200, 312),',
'close-btn')

print('ALL OK')
