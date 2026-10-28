# -*- coding: utf-8 -*-
"""SettingsForm.cs 修改：新增"睡眠省电"快速开关组（睡眠守护 6 项）"""
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

# 1) QuickSwitchGroups 数组：3 组 -> 4 组（新增睡眠省电组）
rep(
    """	private static readonly (string Title, string[] Keys)[] QuickSwitchGroups = new (string, string[])[3]
	{
		(Strings.QuickGroupInput, new string[6] { "touchpad", "touchpadtoggle", "wifi", "bt", "webcam", "numpad" }),
		(Strings.QuickGroupKeyboard, new string[4] { "winkey", "fnkey", "copilot", "osd" }),
		(Strings.QuickGroupPower, new string[12]
		{
			"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
			"monitoroff", "startup"
		})
	};""",
    """	private static readonly (string Title, string[] Keys)[] QuickSwitchGroups = new (string, string[])[4]
	{
		(Strings.QuickGroupInput, new string[6] { "touchpad", "touchpadtoggle", "wifi", "bt", "webcam", "numpad" }),
		(Strings.QuickGroupKeyboard, new string[4] { "winkey", "fnkey", "copilot", "osd" }),
		(Strings.QuickGroupPower, new string[12]
		{
			"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
			"monitoroff", "startup"
		}),
		(Strings.QuickGroupSleep, new string[6] { "sleepnetoff", "sleepaway", "sleepsysreq", "sleeprtcwake", "sleepdeep", "sleephibernate" })
	};""",
    'QuickSwitchGroups 4th group'
)

# 2) 文案字典 array2：22 项 -> 28 项（新增 6 个睡眠开关）
rep(
    """			("monitoroff", Strings.QuickMonitorOff),
			("startup", Strings.QuickStartup)
		};""",
    """			("monitoroff", Strings.QuickMonitorOff),
			("startup", Strings.QuickStartup),
			("sleepnetoff", Strings.QuickSleepNetOff),
			("sleepaway", Strings.QuickSleepAway),
			("sleepsysreq", Strings.QuickSleepSysReq),
			("sleeprtcwake", Strings.QuickSleepRtcWake),
			("sleepdeep", Strings.QuickSleepDeep),
			("sleephibernate", Strings.QuickSleepHibernate)
		};""",
    'label dictionary'
)

# 3) 数组长度声明 (string, string)[] array2 = new (string, string)[22] -> [28]
rep(
    """			(string, string)[] array2 = new (string, string)[22]""",
    """			(string, string)[] array2 = new (string, string)[28]""",
    'array2 size'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
