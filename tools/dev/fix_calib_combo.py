# -*- coding: utf-8 -*-
# 校色弹窗：下拉加宽显示全名 + 按钮加高修复底部裁切
import io

def edit(path, old, new, tag):
    t = io.open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\source\MechrevoLite\CalibrationStartupForm.cs'

# 窗口再加宽 520 -> 560
edit(p, 'ClientSize = new Size(520, 360);', 'ClientSize = new Size(560, 360);', 'width')

# 下拉列表加宽 240 -> 320，并设展开宽度 420（列表显示全名）
edit(p,
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\tSize = new Size(240, 26)',
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tDropDownWidth = 420,\n\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\tSize = new Size(320, 26)',
'combo-width')

# 导入按钮：随下拉右移、加高 28->30 修复底部裁切
edit(p,
'Size = new Size(72, 28),\n\t\t\t\tLocation = new Point(358, 186),',
'Size = new Size(72, 30),\n\t\t\t\tLocation = new Point(440, 185),',
'import-size')

print('ALL OK')
