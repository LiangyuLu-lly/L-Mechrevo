# -*- coding: utf-8 -*-
"""
🔴 真凶：下拉的 SelectedIndexChanged 会真的切换模式
================================================================================
用户实测：「静音模式打开配置给我切平衡」
根因（CustomModeForm.cs:251-257）：
    _modeCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
    {
        if (!_syncingModes && _modeCombo.SelectedItem is ModeItem modeItem)
        {
            await SelectModeAsync(modeItem.Id);     // ← 真的切换模式
        }
    };
而我的 `SyncFirmwareModeCombo()` 调 `SelectModeItem(text)` 时**没有 _syncingModes 守卫**
（它在 BindFirmwareProfile 的 try/finally 之外）-> 触发该回调 -> SelectModeAsync("balanced")
-> 用户被切到平衡。

两层防护：
  1. **回调里加 `_firmwareMode` 守卫** —— 固件模式下该下拉是禁用标签，绝不允许它触发任何切换
  2. `SyncFirmwareModeCombo` 用 `_syncingModes` 包住（即使守卫失效也不会触发）
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# 1) 回调加 _firmwareMode 守卫（决定性）
op(b'\t\t\tif (!_syncingModes && _modeCombo.SelectedItem is ModeItem modeItem)\n',
   b'\t\t\t// 2026-10-05: in firmware-profile mode this combo is a disabled label; it must\r\n'
   b'\t\t\t// never switch modes. Without this the combo sync moved the user to another\r\n'
   b'\t\t\t// mode the moment the editor was opened.\r\n'
   b'\t\t\tif (!_syncingModes && !_firmwareMode && _modeCombo.SelectedItem is ModeItem modeItem)\n',
   '回调加 _firmwareMode 守卫')

# 2) SyncFirmwareModeCombo 用 _syncingModes 包住
op(b'\t\tstring text = PerfModeDefinition.BuiltInId(FirmwareKind());\n'
   b'\t\tif (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem) || modeItem.Id != text))\n'
   b'\t\t{\n'
   b'\t\t\tRebuildModeItems();\n'
   b'\t\t}\n'
   b'\t\tSelectModeItem(text);\n'
   b'\t}\n',
   b'\t\tstring text = PerfModeDefinition.BuiltInId(FirmwareKind());\n'
   b'\t\tbool syncingModes = _syncingModes;\n'
   b'\t\t_syncingModes = true;\n'
   b'\t\ttry\n'
   b'\t\t{\n'
   b'\t\t\tif (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem) || modeItem.Id != text))\n'
   b'\t\t\t{\n'
   b'\t\t\t\tRebuildModeItems();\n'
   b'\t\t\t}\n'
   b'\t\t\tSelectModeItem(text);\n'
   b'\t\t}\n'
   b'\t\tfinally\n'
   b'\t\t{\n'
   b'\t\t\t_syncingModes = syncingModes;\n'
   b'\t\t}\n'
   b'\t}\n',
   'SyncFirmwareModeCombo 加 _syncingModes 守卫')


def main():
    data = io.open(F, 'rb').read()
    if b'! _firmwareMode && _modeCombo.SelectedItem' in data:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, expect, label in OPS:
        n = data.count(old)
        if n != expect:
            errors.append('命中 %d 次(期望 %d)  [%s]' % (n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in OPS:
        data = data.replace(old, new, expect)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
