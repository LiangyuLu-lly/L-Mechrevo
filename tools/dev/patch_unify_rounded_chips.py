# -*- coding: utf-8 -*-
"""
统一为圆角矩形 chip（用户要求）
================================================================================
用户反馈：「太深/不喜欢 能不能顺手改一下屏幕和灯光的设置和编辑按钮 统一成圆角矩形」

两处：
1. 「配置编辑」不再用深色 solid-chip —— 改回与「设置/编辑」一致的 mini-chip（圆角 12）
2. FillRow 里的「编辑」按钮：**普通 Button → RButton**
   （普通 Button 没有 BorderRadius，所以它是直角；mini-chip 的按钮都是 RButton + BorderRadius 12）
   该按钮被灯光组的键盘/灯条/Logo/Hinge/Sync 各行共用，改一处全部统一

注意：SettingsForm.cs 是 CRLF，故用字节级替换，锚点带 \r\n。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = [
    # 1) 配置编辑：深色 -> 统一 mini-chip 圆角
    (b'\t\t\tTag = "solid-chip",\r\n',
     b'\t\t\tTag = "mini-chip",\r\n\t\t\tBorderRadius = 12,\r\n',
     '配置编辑改用 mini-chip + 圆角'),
    (b'\t\t\tForeColor = UiVisualStyle.AccentText,\r\n',
     b'\t\t\tForeColor = UiVisualStyle.Text,\r\n',
     '配置编辑前景改回 Text'),
    (b'\t\t\tBackColor = UiVisualStyle.Muted,\r\n',
     b'',
     '配置编辑去掉深色底（交给 mini-chip 主题）'),
    # 2) FillRow 的「编辑」按钮：Button -> RButton + mini-chip 圆角
    (b'\t\t\tButton button = new Button();\r\n'
     b'\t\t\tbutton.Text = Strings.Edit;\r\n'
     b'\t\t\tbutton.FlatStyle = FlatStyle.Flat;\r\n'
     b'\t\t\tbutton.Cursor = Cursors.Hand;\r\n'
     b'\t\t\tbutton.ForeColor = UiVisualStyle.Text;\r\n'
     b'\t\t\tbutton.BackColor = UiVisualStyle.Window;\r\n'
     b'\t\t\tbutton.FlatAppearance.BorderColor = UiVisualStyle.Border;\r\n'
     b'\t\t\tbutton.FlatAppearance.BorderSize = 1;\r\n',
     b'\t\t\t// 2026-10-04: use RButton + the "mini-chip" theme so this Edit button matches\r\n'
     b'\t\t\t// the rounded chips used by the screen/lighting rows (plain Button has no BorderRadius).\r\n'
     b'\t\t\tRButton button = new RButton\r\n'
     b'\t\t\t{\r\n'
     b'\t\t\t\tTag = "mini-chip",\r\n'
     b'\t\t\t\tBorderRadius = 12,\r\n'
     b'\t\t\t\tText = Strings.Edit,\r\n'
     b'\t\t\t\tFlatStyle = FlatStyle.Flat,\r\n'
     b'\t\t\t\tCursor = Cursors.Hand,\r\n'
     b'\t\t\t\tForeColor = UiVisualStyle.Text,\r\n'
     b'\t\t\t\tBackColor = UiVisualStyle.Window\r\n'
     b'\t\t\t};\r\n',
     'FillRow 编辑按钮改 RButton + 圆角'),
]


def main():
    data = io.open(F, 'rb').read()
    if b'2026-10-04: use RButton' in data:
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
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
