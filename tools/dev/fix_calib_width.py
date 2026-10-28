# -*- coding: utf-8 -*-
# 校色弹窗：窗口加宽 + 下拉拉长 + 导入按钮与下拉垂直居中对齐
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

# 窗口加宽 480 -> 520
edit(p, 'ClientSize = new Size(480, 360);', 'ClientSize = new Size(520, 360);', 'width')

# 下拉列表拉长 200 -> 240
edit(p,
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\tSize = new Size(200, 26)',
'Font = UiVisualStyle.Font(8f),\n\t\t\t\tLocation = new Point(110, 188),\n\t\t\t\tSize = new Size(240, 26)',
'combo-width')

# 导入按钮右移并与下拉垂直居中（combo 右缘 350 + 8 = 358；按钮 y=186 使中心与 combo 对齐）
edit(p,
'Size = new Size(72, 28),\n\t\t\t\tLocation = new Point(318, 185),',
'Size = new Size(72, 28),\n\t\t\t\tLocation = new Point(358, 186),',
'import-align')

print('ALL OK')
