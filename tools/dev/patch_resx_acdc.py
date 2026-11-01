# -*- coding: utf-8 -*-
"""资源补丁：SleepAc/SleepDc + 6 个睡眠开关 ToolTip 文案（中英）"""
import io

EN = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
ZH = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'
CS = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'

en = {
    'SleepAc': 'AC',
    'SleepDc': 'DC',
    'QuickSleepNetOffTip': 'Disconnect from the network during standby to reduce power draw.',
    'QuickSleepAwayTip': 'Prevent apps from using away mode to keep the system awake.',
    'QuickSleepSysReqTip': 'Allow the system to sleep even when apps request the "system required" policy.',
    'QuickSleepRtcWakeTip': 'Disable scheduled tasks from waking the computer at set times.',
    'QuickSleepDeepTip': 'Use ACPI deep sleep in standby for lower power draw; wake-up will be slower.',
    'QuickSleepHibernateTip': 'Automatically hibernate after 30 minutes of standby.',
}
zh = {
    'SleepAc': '插电时',
    'SleepDc': '离电时',
    'QuickSleepNetOffTip': '进入待机时断开网络连接，减少待机耗电。',
    'QuickSleepAwayTip': '阻止应用程序通过“离开模式”使系统保持唤醒。',
    'QuickSleepSysReqTip': '忽略程序提出的“系统所需”请求，允许系统照常进入睡眠。',
    'QuickSleepRtcWakeTip': '禁用定时任务在指定时间唤醒电脑。',
    'QuickSleepDeepTip': '待机时启用深度睡眠以降低功耗，唤醒速度会变慢。',
    'QuickSleepHibernateTip': '系统待机 30 分钟后自动转入休眠。',
}

def patch_resx(path, kv):
    with io.open(path, 'r', encoding='utf-8') as f:
        x = f.read()
    for k, v in kv.items():
        tag = '<data name="%s" xml:space="preserve">' % k
        assert tag not in x, 'key already exists: ' + k
        block = '  ' + tag + '\n    <value>%s</value>\n  </data>\n' % v
        # 在 </root> 前插入
        assert '</root>' in x
        x = x.replace('</root>', block + '</root>')
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(x)
    print('OK resx:', path)

patch_resx(EN, en)
patch_resx(ZH, zh)

with io.open(CS, 'r', encoding='utf-8') as f:
    c = f.read()
for k in list(en) + list(zh):
    if k in c:
        continue
    prop = '\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (k, k)
    # 在类尾大括号前插入（找最后一个属性前的空行模式——简单起见插在文件尾大括号前）
    idx = c.rstrip().rfind('\n}')
    assert idx > 0
    c = c[:idx] + prop + c[idx:]
with io.open(CS, 'w', encoding='utf-8', newline='') as f:
    f.write(c)
print('OK Strings.cs')
