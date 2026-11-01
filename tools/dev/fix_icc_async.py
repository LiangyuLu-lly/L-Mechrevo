# -*- coding: utf-8 -*-
# ICC 应用/解除移到后台线程，避免 WCS 调用阻塞 UI 导致未响应
import io

def edit(path, old, new, tag):
    t = io.open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# 1) SettingsForm 999 分支：ApplyIcc 移后台
edit(r'D:\openLM\source\MechrevoLite\SettingsForm.cs',
'flag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && IccHelper.ApplyIcc(iccPath);',
'flag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));',
'settings-999-task')

# 2) CalibrationStartupForm 关闭开关：ClearAllIcc 移后台（不阻塞弹窗）
edit(r'D:\openLM\source\MechrevoLite\CalibrationStartupForm.cs',
'\t\t\t\t\t\t// 关闭自定义：取消所有已应用的 ICC；若主界面正选中自定义则回退默认档位\n\t\t\t\t\t\tIccHelper.ClearAllIcc();',
'\t\t\t\t\t\t// 关闭自定义：取消所有已应用的 ICC；若主界面正选中自定义则回退默认档位（后台执行，避免 WCS 阻塞 UI）\n\t\t\t\t\t\tTask.Run(() => IccHelper.ClearAllIcc());',
'startup-clear-task')

print('ALL OK')
