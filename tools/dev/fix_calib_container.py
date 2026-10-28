# -*- coding: utf-8 -*-
# 校色弹窗：容器再加宽加高 + 导入按钮微调
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

# 容器加宽 560 -> 580、加高 360 -> 380
edit(p, 'ClientSize = new Size(560, 360);', 'ClientSize = new Size(580, 380);', 'container')

# 导入按钮加高 30 -> 32，上移 1px 保持与下拉垂直居中
edit(p,
'Size = new Size(72, 30),\n\t\t\t\tLocation = new Point(440, 185),',
'Size = new Size(72, 32),\n\t\t\t\tLocation = new Point(440, 184),',
'import-btn')

# 取消按钮下移（随容器加高）
edit(p,
'Size = new Size(80, 28),\n\t\t\t\tLocation = new Point(200, 312),',
'Size = new Size(80, 28),\n\t\t\t\tLocation = new Point(200, 332),',
'close-btn')

print('ALL OK')
