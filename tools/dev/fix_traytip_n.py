# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8').read()

# 1) GPU 行: "\tnGPU" -> "\nGPU"（n 是 \n 残留，改为真换行；GPU 标签对齐悬浮窗的 "GPU:"）
old1 = 'trayTip = trayTip + "\tnGPU" + gpuTemp + " " + gpuFan;'
new1 = 'trayTip = trayTip + "\\nGPU" + gpuTemp + " " + gpuFan;'
# 2) 电池行: "\tn" -> "\n"
old2 = 'trayTip = trayTip + "\\n" + battery;'
print('GPU行命中:', t.count(old1))
print('电池行新串(上一步已改)命中:', t.count(old2))
if t.count(old1) > 0:
    t = t.replace(old1, new1)
    print('GPU行已替换')
open(p, 'w', encoding='utf-8', newline='').write(t)
# 校验最终状态
t2 = open(p, encoding='utf-8').read()
import re
for m in re.finditer(r'trayTip = trayTip \+ "[^"]*" \+ (?:gpuTemp|battery)[^;]*;', t2):
    print('最终:', repr(m.group(0)[:70]))
