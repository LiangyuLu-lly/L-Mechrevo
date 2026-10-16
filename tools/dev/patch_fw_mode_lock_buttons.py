# -*- coding: utf-8 -*-
"""
修 BindFirmwareProfile 里的顺序 bug：禁用按钮后又被 SetOptionsEnabled() 重新启用
另外把「模式」下拉也锁上（固件模式下不该切成自定义1/2）
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\t_status.ForeColor = UiVisualStyle.Muted;\n'
       b'\t\tSetOptionsEnabled();\n'
       b'\t\tif (IsHandleCreated)\n')

NEW = (b'\t\t_status.ForeColor = UiVisualStyle.Muted;\n'
       b'\t\tSetOptionsEnabled();\n'
       b'\t\t// 2026-10-04: SetOptionsEnabled() re-enables these according to the mode kind,\r\n'
       b'\t\t// so re-apply the locks AFTER it -- and lock the mode combo too.\r\n'
       b'\t\t_modeCombo.Enabled = false;\r\n'
       b'\t\t_addButton.Enabled = false;\r\n'
       b'\t\t_renameButton.Enabled = false;\r\n'
       b'\t\t_deleteButton.Enabled = false;\r\n'
       b'\t\t_restoreButton.Enabled = false;\r\n'
       b'\t\t_fanCurveButton.Enabled = false;\r\n'
       b'\t\tif (IsHandleCreated)\n')


def main():
    data = io.open(F, 'rb').read()
    if b'_modeCombo.Enabled = false;' in data:
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
