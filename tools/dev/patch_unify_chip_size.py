# -*- coding: utf-8 -*-
"""
统一 chip 尺寸：灯光组「编辑」与 屏幕校色「设置」/ 色彩增强「编辑」对齐
================================================================================
实测（源码）：
  calibPanel 列 22/10/56/12，Height=LogicalToDevice(34)，按钮 Margin 上3下3 -> 按钮高 28
  MakeRow    列 22/10/56/12，Height=LogicalToDevice(32)，按钮 Margin 上3下3 -> 按钮高 26
  两者列宽相同，差异在：① 字体 FillRow 用 UiStyleBody()=9f，其余用 Font(8f)  ② 按钮高 28 vs 26
对齐做法：FillRow 按钮 Font 改 Font(8f)；上下 Margin 由 3 改 2 -> 32-2-2 = 28，与其余一致。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\t\tbutton.Font = UiStyleBody();\r\n'
       b'\t\t\tbutton.Margin = new Padding(D(2), D(3), 0, D(3));\r\n')

NEW = (b'\t\t\t// 2026-10-04: match the screen/lighting chips exactly -- Font(8f) like the others,\r\n'
       b'\t\t\t// and vertical inset 2 (this row is 32 tall vs calibPanel 34) so the button\r\n'
       b'\t\t\t// ends up 28px tall, same as the calibration "settings" chip.\r\n'
       b'\t\t\tbutton.Font = UiVisualStyle.Font(8f);\r\n'
       b'\t\t\tbutton.Margin = new Padding(D(2), D(2), 0, D(2));\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'button.Font = UiVisualStyle.Font(8f);' in data:
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
