# -*- coding: utf-8 -*-
"""
修 9.2：编辑器标题与「模式」下拉不一致
================================================================================
根因（读源码确认）：
  1. `RebuildModeItems()` :1071 结尾是 `SelectModeItem(_modeId)` —— 会把下拉选中项
     **拉回旧的 `_modeId`**（当前 LM 模式，如"静音"）。我的同步在它之前跑，被它覆盖。
  2. 同方法内 `if (IsOffered(item, hw) || item.Id == _modeId)` 会按 `IsOffered` 过滤，
     而 `IsOffered` 对 40+ 机型的非自定义档返回 false -> 固件模式对应的内建档可能不在列表里。
修法：
  1. `RebuildModeItems` 过滤条件加 `_firmwareMode ||`
  2. `RebuildModeItems` 结尾改为按固件模式选：
     `SelectModeItem(_firmwareMode ? PerfModeDefinition.BuiltInId(FirmwareKind()) : _modeId)`
  3. 新增 `FirmwareKind()` 与 `SyncFirmwareModeCombo()`；
     `BindFirmwareProfile` 里把下拉同步**挪到最后**（SetOptionsEnabled/重锁之后）执行
  4. `BindFirmwareProfile` 里原先那段行内同步删掉（改由 SyncFirmwareModeCombo 统一做）
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# 1) 删掉 BindFirmwareProfile 里的行内同步
op(b'\t\t\t// 2026-10-05: keep the mode combo in sync with the firmware mode being edited\n'
   b'\t\t\t// (it used to keep whatever was selected before, e.g. title "balanced" but combo "silent").\n'
   b'\t\t\tstring text2 = PerfModeDefinition.BuiltInId(kind);\n'
   b'\t\t\tif (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem2) || modeItem2.Id != text2))\n'
   b'\t\t\t{\n'
   b'\t\t\t\tRebuildModeItems();\n'
   b'\t\t\t}\n'
   b'\t\t\tSelectModeItem(text2);\n',
   b'',
   '删除行内同步')

# 2) 同步挪到最后
op(b'\t\t_modeCombo.Enabled = false;\n'
   b'\t\t_addButton.Enabled = false;\n'
   b'\t\t_renameButton.Enabled = false;\n'
   b'\t\t_deleteButton.Enabled = false;\n'
   b'\t\tif (IsHandleCreated)\n',
   b'\t\t_modeCombo.Enabled = false;\n'
   b'\t\t_addButton.Enabled = false;\n'
   b'\t\t_renameButton.Enabled = false;\n'
   b'\t\t_deleteButton.Enabled = false;\n'
   b'\t\t// 2026-10-05: sync the mode combo LAST -- RebuildModeItems() runs in between and\n'
   b'\t\t\t// would otherwise leave the combo on the previously selected mode.\n'
   b'\t\tSyncFirmwareModeCombo();\n'
   b'\t\tif (IsHandleCreated)\n',
   '同步挪到最后')

# 3) 新增 FirmwareKind / SyncFirmwareModeCombo
op(b'\tprivate void RebuildModeItems()\n\t{\n',
   b'\t/// <summary>2026-10-05: the perf-mode kind backing the firmware profile being edited.</summary>\n'
   b'\tprivate PerfModeKind FirmwareKind()\n'
   b'\t{\n'
   b'\t\treturn ((_firmwareVisualMode == 2) ? PerfModeKind.Silent : ((_firmwareVisualMode == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));\n'
   b'\t}\n'
   b'\n'
   b'\t/// <summary>\n'
   b'\t/// 2026-10-05: point the (disabled) mode combo at the firmware mode being edited.\n'
   b'\t/// Must run after RebuildModeItems(), which re-selects the old _modeId at its end.\n'
   b'\t/// </summary>\n'
   b'\tprivate void SyncFirmwareModeCombo()\n'
   b'\t{\n'
   b'\t\tif (!_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tstring text = PerfModeDefinition.BuiltInId(FirmwareKind());\n'
   b'\t\tif (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem) || modeItem.Id != text))\n'
   b'\t\t{\n'
   b'\t\t\tRebuildModeItems();\n'
   b'\t\t}\n'
   b'\t\tSelectModeItem(text);\n'
   b'\t}\n'
   b'\n'
   b'\tprivate void RebuildModeItems()\n\t{\n',
   'FirmwareKind / SyncFirmwareModeCombo')

# 4) 过滤条件 + 结尾选中
op(b'\t\t\t\tif (IsOffered(item, hw) || string.Equals(item.Id, _modeId, StringComparison.Ordinal))\n',
   b'\t\t\t\tif (_firmwareMode || IsOffered(item, hw) || string.Equals(item.Id, _modeId, StringComparison.Ordinal))\n',
   '过滤条件放开内建档')

op(b'\t\t\tSelectModeItem(_modeId);\n\t\t\t_modeCombo.EndUpdate();\n',
   b'\t\t\tSelectModeItem(_firmwareMode ? PerfModeDefinition.BuiltInId(FirmwareKind()) : _modeId);\n'
   b'\t\t\t_modeCombo.EndUpdate();\n',
   '结尾按固件模式选中')


def main():
    data = io.open(F, 'rb').read()
    if b'SyncFirmwareModeCombo' in data:
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
