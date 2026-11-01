# -*- coding: utf-8 -*-
"""Strings.resx / Strings.zh-CN.resx 新增睡眠省电 7 个文案键"""
import io

def patch(path, group_val, startup_val, new_group, new_switches):
    with io.open(path, 'r', encoding='utf-8') as f:
        src = f.read()

    # 1) QuickGroupPower 块后插入 QuickGroupSleep
    old_g = '  <data name="QuickGroupPower" xml:space="preserve">\n    <value>%s</value>\n  </data>' % group_val
    assert src.count(old_g) == 1, 'group anchor not unique in ' + path
    src = src.replace(old_g, old_g + '\n' + new_group)

    # 2) QuickStartup 块后插入 6 个 QuickSleep*
    old_s = '  <data name="QuickStartup" xml:space="preserve">\n    <value>%s</value>\n  </data>' % startup_val
    assert src.count(old_s) == 1, 'startup anchor not unique in ' + path
    src = src.replace(old_s, old_s + '\n' + new_switches)

    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(src)
    print('patched', path)

def block(name, value):
    return '  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>' % (name, value)

# 英文
new_group_en = block('QuickGroupSleep', 'Sleep saver')
new_switches_en = '\n'.join([
    block('QuickSleepAway', 'Block away mode'),
    block('QuickSleepDeep', 'Enable ACPI deep sleep'),
    block('QuickSleepHibernate', 'Hibernate after 30 min'),
    block('QuickSleepNetOff', 'Disconnect network on standby'),
    block('QuickSleepRtcWake', 'Disable RTC wake timers'),
    block('QuickSleepSysReq', 'Ignore system required policy'),
])
patch(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
      'Power &amp; system', 'Startup', new_group_en, new_switches_en)

# 中文
new_group_zh = block('QuickGroupSleep', '睡眠省电')
new_switches_zh = '\n'.join([
    block('QuickSleepAway', '封杀离开模式'),
    block('QuickSleepDeep', '启用ACPI深度睡眠'),
    block('QuickSleepHibernate', '30分钟转休眠'),
    block('QuickSleepNetOff', '待机断开网络'),
    block('QuickSleepRtcWake', '禁用RTC唤醒定时器'),
    block('QuickSleepSysReq', '忽略系统所需策略'),
])
patch(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
      '电源与系统', '开机自启动', new_group_zh, new_switches_zh)
print('ALL DONE')
