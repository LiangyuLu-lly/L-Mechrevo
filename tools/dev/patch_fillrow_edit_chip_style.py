# -*- coding: utf-8 -*-
"""
修 FillRow「编辑」按钮仍与 屏幕校色「设置」不一致
================================================================================
原因：`FlatStyle.Flat` 时 `FlatAppearance.BorderSize` **默认是 1** → 按钮带边框。
      其余 chip（calibSettingsBtn:1807、effectEditBtn:1960）都**显式**写了
      `FlatAppearance.BorderSize = 0` 并把 hover 设为 SurfaceRaised。
      而 mini-chip 主题本会设这些，但**灯光组这些行动态重建、晚于主题应用**，
      主题没跑到它 → 只保留了我初始化里设的属性。

修法：显式复现 mini-chip 主题的实际结果（与主题时机解耦，保证一致）：
  Surface 底 / Border 描边色 / BorderSize 0 / hover+按下 SurfaceRaised / 文字居中
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\t\tbutton.Margin = new Padding(D(2), D(2), 0, D(2));\r\n'
       b'\t\t\tButton button2 = button;\r\n')

NEW = (b'\t\t\tbutton.Margin = new Padding(D(2), D(2), 0, D(2));\r\n'
       b'\t\t\t// 2026-10-04: FlatStyle.Flat defaults BorderSize to 1, and this row group is\r\n'
       b'\t\t\t// rebuilt after the theme pass, so the "mini-chip" theme never reaches this\r\n'
       b'\t\t\t// button. Replicate that theme explicitly so it matches the calibration /\r\n'
       b'\t\t\t// display-effect chips exactly.\r\n'
       b'\t\t\tbutton.BackColor = UiVisualStyle.Surface;\r\n'
       b'\t\t\tbutton.BorderColor = UiVisualStyle.Border;\r\n'
       b'\t\t\tbutton.FlatAppearance.BorderColor = UiVisualStyle.Border;\r\n'
       b'\t\t\tbutton.FlatAppearance.BorderSize = 0;\r\n'
       b'\t\t\tbutton.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;\r\n'
       b'\t\t\tbutton.FlatAppearance.MouseDownBackColor = UiVisualStyle.SurfaceRaised;\r\n'
       b'\t\t\tbutton.TextAlign = ContentAlignment.MiddleCenter;\r\n'
       b'\t\t\tButton button2 = button;\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'button.BorderColor = UiVisualStyle.Border;' in data:
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
