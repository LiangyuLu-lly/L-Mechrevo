# -*- coding: utf-8 -*-
"""
编辑器跟随「模式」与「配置」两种变化
================================================================================
用户实测（截图）：
  截图1：LM 在静音，编辑窗口却显示「平衡 · 配置 1」  -> 没跟随**模式**
  截图2：LM 狂飙选「配置 4」，编辑窗口显示「配置 1」 -> 没跟随**配置**
修法：
  A. CustomModeForm 暴露三个只读属性（IsFirmwareMode / FirmwareVisualMode / FirmwareProfileIndex）
     —— 供 SettingsForm 判断"目标是否真的变了"，避免无谓重绑
  B. SettingsForm：**配置下拉变化**时，若编辑器开着且正是该模式 -> 重新绑定
  C. SettingsForm：`ShowMode(mode)`（LM 刷新性能 UI 时都会调用）里，
     若编辑器开着且目标变了 -> 重新绑定  ← 覆盖**模式变化**
行尾：CustomModeForm.cs=LF，SettingsForm.cs=CRLF
"""
import io
import os
import sys

F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---- A. 暴露只读属性 ----
op(F_CMF, b'\tprivate bool _bindingFirmwareProfile;\n',
   b'\tprivate bool _bindingFirmwareProfile;\n'
   b'\n'
   b'\t/// <summary>2026-10-05: read-only view of the bound firmware target (for SettingsForm).</summary>\n'
   b'\tinternal bool IsFirmwareMode => _firmwareMode;\n'
   b'\n'
   b'\tinternal int FirmwareVisualMode => _firmwareVisualMode;\n'
   b'\n'
   b'\tinternal int FirmwareProfileIndex => _firmwareProfileIndex;\n',
   '暴露只读属性')

# ---- B. 配置下拉变化 -> 重绑编辑器 ----
op(F_SF,
   b'\t\t\t\t\t\tawait SwitchPerformanceModeAsync(modeValue, silentTurbo);\r\n'
   b'\t\t\t\t\t}\r\n',
   b'\t\t\t\t\t\tawait SwitchPerformanceModeAsync(modeValue, silentTurbo);\r\n'
   b'\t\t\t\t\t}\r\n'
   b'\t\t\t\t\t// 2026-10-05: if the firmware-profile editor is open on this mode, re-bind it\r\n'
   b'\t\t\t\t\t// so its config number and parameter values follow the dropdown.\r\n'
   b'\t\t\t\t\tif (customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n'
   b'\t\t\t\t\t{\r\n'
   b'\t\t\t\t\t\tcustomModeForm.BindFirmwareProfile(modeValue, selectedIndex);\r\n'
   b'\t\t\t\t\t}\r\n',
   '配置变化时重绑编辑器')

# ---- C. ShowMode -> 目标变了就重绑（覆盖模式变化） ----
op(F_SF, b'\tpublic void ShowMode(int mode)\r\n\t{\r\n',
   b'\tpublic void ShowMode(int mode)\r\n'
   b'\t{\r\n'
   b'\t\t// 2026-10-05: keep an open firmware-profile editor in sync with the active mode.\r\n'
   b'\t\t// ShowMode() runs on every perf-UI refresh, so this covers mode switches.\r\n'
   b'\t\tif (customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tint num = Program.service?.CurrentMode ?? mode;\r\n'
   b'\t\t\tint num2 = ModeProfileConfig.Get(num);\r\n'
   b'\t\t\tif (customModeForm.FirmwareVisualMode != num || customModeForm.FirmwareProfileIndex != num2)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tcustomModeForm.BindFirmwareProfile(num, num2);\r\n'
   b'\t\t\t}\r\n'
   b'\t\t}\r\n',
   'ShowMode 同步编辑器目标')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_CMF, F_SF)}
    if b'FirmwareVisualMode =>' in data[F_CMF]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = data[path].count(old)
        if n != expect:
            errors.append('%-22s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        data[path] = data[path].replace(old, new, expect)
    data[F_CMF] = data[F_CMF].replace(b'\r\n', b'\n')
    for path, blob in data.items():
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
