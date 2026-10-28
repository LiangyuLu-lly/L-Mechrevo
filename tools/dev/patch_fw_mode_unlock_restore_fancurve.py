# -*- coding: utf-8 -*-
"""
用户要求：解除「恢复出厂」「风扇曲线」的置灰
================================================================================
注：这两个按钮的置灰**不是** 40 系判断造成的，而是我在 BindFirmwareProfile 里
    无条件禁用了它们（第一批禁用 + SetOptionsEnabled() 之后的重锁各一处）。
    用户要求暂时解除 → 两处都去掉这两行，其余（模式下拉/新建/重命名/删除）保持锁定。
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = [
    # 1) 第一批禁用
    (b'\t\t_deleteButton.Enabled = false;\n'
     b'\t\t_restoreButton.Enabled = false;\n'
     b'\t\t_fanCurveButton.Enabled = false;\n'
     b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n',
     b'\t\t_deleteButton.Enabled = false;\n'
     b'\t\t// 2026-10-04: restore-default / fan-curve left ENABLED per user request.\n'
     b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n',
     '第一批解除 restore/fanCurve'),
    # 2) SetOptionsEnabled() 之后的重锁
    (b'\t\t_deleteButton.Enabled = false;\n'
     b'\t\t_restoreButton.Enabled = false;\n'
     b'\t\t_fanCurveButton.Enabled = false;\n'
     b'\t\tif (IsHandleCreated)\n',
     b'\t\t_deleteButton.Enabled = false;\n'
     b'\t\tif (IsHandleCreated)\n',
     '重锁处解除 restore/fanCurve'),
]


def main():
    data = io.open(F, 'rb').read()
    if data.count(b'_restoreButton.Enabled = false;') == 0:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, label in OPS:
        n = data.count(old)
        if n != 1:
            errors.append('命中 %d 次(期望 1)  [%s]' % (n, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for old, new, label in OPS:
        data = data.replace(old, new, 1)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
