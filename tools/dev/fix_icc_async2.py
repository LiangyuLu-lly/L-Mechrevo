# -*- coding: utf-8 -*-
import io
t = io.open(r'D:\openLM\source\MechrevoLite\CalibrationStartupForm.cs', encoding='utf-8').read()
old = '\t\t\t\t\t// 关闭自定义：取消所有已应用的 ICC；若主界面正选中自定义则回退默认档位\n\t\t\t\t\tIccHelper.ClearAllIcc();'
new = '\t\t\t\t\t// 关闭自定义：取消所有已应用的 ICC；若主界面正选中自定义则回退默认档位（后台执行，避免 WCS 阻塞 UI）\n\t\t\t\t\tTask.Run(() => IccHelper.ClearAllIcc());'
n = t.count(old)
print('匹配:', n)
if n != 1:
    raise SystemExit('ABORT')
io.open(r'D:\openLM\source\MechrevoLite\CalibrationStartupForm.cs', 'w', encoding='utf-8', newline='').write(t.replace(old, new))
print('OK')
