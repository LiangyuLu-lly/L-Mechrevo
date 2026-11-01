# -*- coding: utf-8 -*-
# 校色设置弹窗布局修复：间距拉开 + 提示换行 + 窗口加高
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

# 1) 窗口加高 330 -> 360
edit(p, 'ClientSize = new Size(480, 330);', 'ClientSize = new Size(480, 360);', 'client-size')

# 2) 开机自动应用开关 + 提示下移（拉开与标题间距）
edit(p,
'Checked = AppConfig.Is("calib_apply_on_startup"),\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 62),',
'Checked = AppConfig.Is("calib_apply_on_startup"),\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 66),',
'startup-switch')

edit(p,
'Text = Strings.CalibApplyOnStartupHint,\n\t\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 96),\n\t\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'Text = Strings.CalibApplyOnStartupHint,\n\t\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tMaximumSize = new Size(430, 0),\n\t\t\t\t\tLocation = new Point(26, 104),\n\t\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'startup-hint')

# 3) 自定义校色标题 + 开关 + 下拉 + 导入 + 提示整体下移，间距拉开
edit(p,
'Text = Strings.CustomCalibration,\n\t\t\t\t\tForeColor = UiVisualStyle.Text,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(24, 140),',
'Text = Strings.CustomCalibration,\n\t\t\t\t\tForeColor = UiVisualStyle.Text,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(24, 158),',
'custom-caption')

edit(p,
'Checked = AppConfig.Is("calib_custom_on"),\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 168),',
'Checked = AppConfig.Is("calib_custom_on"),\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 190),',
'custom-switch')

edit(p,
'Font = UiVisualStyle.Font(8f),\n\t\t\t\t\tLocation = new Point(110, 166),\n\t\t\t\t\tSize = new Size(200, 26)',
'Font = UiVisualStyle.Font(8f),\n\t\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\t\tSize = new Size(200, 26)',
'custom-combo')

edit(p,
'Size = new Size(72, 28),\n\t\t\t\t\tLocation = new Point(318, 163),',
'Size = new Size(72, 28),\n\t\t\t\t\tLocation = new Point(318, 185),',
'import-btn')

edit(p,
'Text = Strings.CustomCalibrationHint,\n\t\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tLocation = new Point(26, 206),\n\t\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'Text = Strings.CustomCalibrationHint,\n\t\t\t\t\tForeColor = UiVisualStyle.Muted,\n\t\t\t\t\tAutoSize = true,\n\t\t\t\t\tMaximumSize = new Size(430, 0),\n\t\t\t\t\tLocation = new Point(26, 234),\n\t\t\t\t\tFont = UiVisualStyle.Font(7.5f)',
'custom-hint')

# 4) 取消按钮下移
edit(p,
'Size = new Size(80, 28),\n\t\t\t\t\tLocation = new Point(200, 276),',
'Size = new Size(80, 28),\n\t\t\t\t\tLocation = new Point(200, 312),',
'close-btn')

# ---------- resx：精简提示为两行 ----------
def edit_resx(path, name, new_value, tag):
    raw = open(path, 'rb').read()
    has_bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig' if has_bom else 'utf-8')
    old = '<data name="' + name + '" xml:space="preserve">'
    i = text.find(old)
    if i < 0:
        raise SystemExit('ABORT ' + tag + ': data 标签未找到')
    j = text.find('<value>', i)
    k = text.find('</value>', j)
    if j < 0 or k < 0:
        raise SystemExit('ABORT ' + tag + ': value 未找到')
    text = text[:j] + '<value>' + new_value + text[k + len('</value>'):]
    open(path, 'wb').write(text.encode('utf-8-sig' if has_bom else 'utf-8'))
    print('[' + tag + '] OK')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', 'CustomCalibrationHint',
          '开启后，校色下拉列表将出现“自定义”选项，\n经 Windows 颜色管理应用所选 ICC（不经由 GCU）；关闭则取消所有已应用的 ICC。',
          'resx-zh-hint')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', 'CustomCalibrationHint',
          'When enabled, a "Custom" option appears in the calibration list.\nIt applies the selected ICC via Windows color management (not via GCU); turning it off removes all applied ICC profiles.',
          'resx-en-hint')

print('ALL OK')
