# -*- coding: utf-8 -*-
"""Strings.cs 新增 7 个睡眠省电强类型属性"""
import io

PATH = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

def rep(old, new, label):
    global src
    cnt = src.count(old)
    assert cnt == 1, 'expect 1 match for [%s], got %d' % (label, cnt)
    src = src.replace(old, new)
    print('OK:', label)

rep(
    '\tinternal static string QuickGroupPower => ResourceManager.GetString("QuickGroupPower", resourceCulture);',
    '\tinternal static string QuickGroupPower => ResourceManager.GetString("QuickGroupPower", resourceCulture);\n\n'
    '\tinternal static string QuickGroupSleep => ResourceManager.GetString("QuickGroupSleep", resourceCulture);',
    'QuickGroupSleep'
)

rep(
    '\tinternal static string QuickStartup => ResourceManager.GetString("QuickStartup", resourceCulture);',
    '\tinternal static string QuickStartup => ResourceManager.GetString("QuickStartup", resourceCulture);\n\n'
    '\tinternal static string QuickSleepAway => ResourceManager.GetString("QuickSleepAway", resourceCulture);\n\n'
    '\tinternal static string QuickSleepDeep => ResourceManager.GetString("QuickSleepDeep", resourceCulture);\n\n'
    '\tinternal static string QuickSleepHibernate => ResourceManager.GetString("QuickSleepHibernate", resourceCulture);\n\n'
    '\tinternal static string QuickSleepNetOff => ResourceManager.GetString("QuickSleepNetOff", resourceCulture);\n\n'
    '\tinternal static string QuickSleepRtcWake => ResourceManager.GetString("QuickSleepRtcWake", resourceCulture);\n\n'
    '\tinternal static string QuickSleepSysReq => ResourceManager.GetString("QuickSleepSysReq", resourceCulture);',
    'QuickSleep* six'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
