# -*- coding: utf-8 -*-
"""
修：固件配置模式下「模式」下拉不跟随（标题写平衡、下拉显示静音）
BindFirmwareProfile 只改了标题，没同步 _modeCombo -> 下拉保留上次的选中项。
用 PerfModeDefinition.BuiltInId(kind) 取该模式对应的预设档 id，再 SelectModeItem。
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\t\t// firmware mode has no PerfModeDefinition, so use the plain mode labels\n'
       b'\t\t\tstring modeLabel = ((visualMode == 2) ? Strings.Silent : ((visualMode == 1) ? Strings.Turbo : Strings.Balanced));\n')

NEW = (b'\t\t\t// 2026-10-05: keep the mode combo in sync with the firmware mode being edited\n'
       b'\t\t\t// (it used to keep whatever was selected before, e.g. title "balanced" but combo "silent").\n'
       b'\t\t\tstring text2 = PerfModeDefinition.BuiltInId(kind);\n'
       b'\t\t\tif (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem2) || modeItem2.Id != text2))\n'
       b'\t\t\t{\n'
       b'\t\t\t\tRebuildModeItems();\n'
       b'\t\t\t}\n'
       b'\t\t\tSelectModeItem(text2);\n'
       b'\t\t\t// firmware mode has no PerfModeDefinition, so use the plain mode labels\n'
       b'\t\t\tstring modeLabel = ((visualMode == 2) ? Strings.Silent : ((visualMode == 1) ? Strings.Turbo : Strings.Balanced));\n')


def main():
    data = io.open(F, 'rb').read()
    if b'BuiltInId(kind)' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    data = data.replace(OLD, NEW, 1).replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
