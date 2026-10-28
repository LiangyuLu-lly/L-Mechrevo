# -*- coding: utf-8 -*-
"""
修：改「非当前模式」的配置下拉时，编辑器不该跳到那个模式
================================================================================
用户实测：「我在静音模式 去切平衡模式的下拉列表 编辑器会跳到平衡模式
          （虽然实际性能模式状态不变）」
根因：重绑编辑器的条件里**没有判断"这个下拉是不是当前模式"**
      -> 改平衡的下拉时，编辑器被重绑到平衡（但硬件仍是静音，故"实际状态不变"）
修法：条件里加上 `Program.service != null && Program.service.CurrentMode == modeValue`
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\t\t\t\tif (!_refreshingConfigLabels && customModeForm != null && '
       b'!customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n')

NEW = (b'\t\t\t\t\t// 2026-10-05: only re-bind when the changed combo belongs to the ACTIVE mode.\r\n'
       b'\t\t\t\t\t// Rebinding for a non-active mode made the editor jump to that mode even though\r\n'
       b'\t\t\t\t\t// the machine stayed where it was (reported by the user).\r\n'
       b'\t\t\t\t\tif (!_refreshingConfigLabels && Program.service != null && Program.service.CurrentMode == modeValue && '
       b'customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'belongs to the ACTIVE mode' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
