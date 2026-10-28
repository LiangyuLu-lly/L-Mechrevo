# -*- coding: utf-8 -*-
"""SettingsForm.cs 第二处补丁修正版：只做第 5、6 处（缩进 4/2 tab）"""
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

def rep(old, new, label):
    global src
    cnt = src.count(old)
    assert cnt == 1, 'expect 1 match for [%s], got %d' % (label, cnt)
    src = src.replace(old, new)
    print('OK:', label)

# 5) UpdateQuickSwitches：4 tab 缩进
rep(
    """\t\t\t\tbool? flag = ReadWindowsPersonalizationSwitch(text);
\t\t\t\tif (flag.HasValue)
\t\t\t\t{
\t\t\t\t\tcheckBox.Checked = flag.Value;
\t\t\t\t}
\t\t\t\telse if (text == "startup")""",
    """\t\t\t\tbool? flag = ReadWindowsPersonalizationSwitch(text);
\t\t\t\tif (flag.HasValue)
\t\t\t\t{
\t\t\t\t\tcheckBox.Checked = flag.Value;
\t\t\t\t}
\t\t\t\telse if (text.StartsWith("sleep"))
\t\t\t\t{
\t\t\t\t\tbool? sleepState = PowerSleepGuard.ReadEnabled(text);
\t\t\t\t\tif (sleepState.HasValue)
\t\t\t\t\t{
\t\t\t\t\t\tcheckBox.Checked = sleepState.Value;
\t\t\t\t\t}
\t\t\t\t}
\t\t\t\telse if (text == "startup")""",
    'UpdateQuickSwitches sleep branch'
)

# 6) ReadQuickSwitchState：2 tab 缩进
rep(
    """\t\tbool? result = ReadWindowsPersonalizationSwitch(key);
\t\tif (result.HasValue)
\t\t{
\t\t\treturn result;
\t\t}
\t\tif (key == "startup")
\t\t{
\t\t\treturn Startup.ReadScheduledState();
\t\t}""",
    """\t\tbool? result = ReadWindowsPersonalizationSwitch(key);
\t\tif (result.HasValue)
\t\t{
\t\t\treturn result;
\t\t}
\t\tif (key.StartsWith("sleep"))
\t\t{
\t\t\treturn PowerSleepGuard.ReadEnabled(key);
\t\t}
\t\tif (key == "startup")
\t\t{
\t\t\treturn Startup.ReadScheduledState();
\t\t}""",
    'ReadQuickSwitchState sleep branch'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
