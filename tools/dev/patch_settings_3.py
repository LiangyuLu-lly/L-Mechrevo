# -*- coding: utf-8 -*-
"""补齐 patch 1 未生效的两处：QuickSwitchGroups 第4组 + array2 初始化器 6 项"""
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

# 1) QuickSwitchGroups：3 -> 4 组
rep(
    """\tprivate static readonly (string Title, string[] Keys)[] QuickSwitchGroups = new (string, string[])[3]
\t{
\t\t(Strings.QuickGroupInput, new string[6] { "touchpad", "touchpadtoggle", "wifi", "bt", "webcam", "numpad" }),
\t\t(Strings.QuickGroupKeyboard, new string[4] { "winkey", "fnkey", "copilot", "osd" }),
\t\t(Strings.QuickGroupPower, new string[12]
\t\t{
\t\t\t"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
\t\t\t"monitoroff", "startup"
\t\t})
\t};""",
    """\tprivate static readonly (string Title, string[] Keys)[] QuickSwitchGroups = new (string, string[])[4]
\t{
\t\t(Strings.QuickGroupInput, new string[6] { "touchpad", "touchpadtoggle", "wifi", "bt", "webcam", "numpad" }),
\t\t(Strings.QuickGroupKeyboard, new string[4] { "winkey", "fnkey", "copilot", "osd" }),
\t\t(Strings.QuickGroupPower, new string[12]
\t\t{
\t\t\t"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
\t\t\t"monitoroff", "startup"
\t\t}),
\t\t(Strings.QuickGroupSleep, new string[6] { "sleepnetoff", "sleepaway", "sleepsysreq", "sleeprtcwake", "sleepdeep", "sleephibernate" })
\t};""",
    'QuickSwitchGroups 4th group'
)

# 2) array2 初始化器：monitoroff/startup 后追加 6 项
rep(
    """\t\t\t("monitoroff", Strings.QuickMonitorOff),
\t\t\t("startup", Strings.QuickStartup)
\t\t};""",
    """\t\t\t("monitoroff", Strings.QuickMonitorOff),
\t\t\t("startup", Strings.QuickStartup),
\t\t\t("sleepnetoff", Strings.QuickSleepNetOff),
\t\t\t("sleepaway", Strings.QuickSleepAway),
\t\t\t("sleepsysreq", Strings.QuickSleepSysReq),
\t\t\t("sleeprtcwake", Strings.QuickSleepRtcWake),
\t\t\t("sleepdeep", Strings.QuickSleepDeep),
\t\t\t("sleephibernate", Strings.QuickSleepHibernate)
\t\t};""",
    'array2 6 sleep items'
)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('ALL DONE')
