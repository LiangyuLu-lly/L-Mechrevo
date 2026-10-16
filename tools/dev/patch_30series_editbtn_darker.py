# -*- coding: utf-8 -*-
"""
「配置编辑」按钮着色：用户反馈 Track 仍然太浅
改为 Muted 实心 chip + AccentText 前景：
  日间：底 (90,107,128) 深灰蓝 / 字 白          —— 明显比背景(242,245,249)深
  夜间：底 (143,163,191) 浅灰蓝 / 字 (8,18,31)  —— 对比度同样足够
（AccentText 在日间是 White、夜间是深色，正好两边都能保证可读）
去掉 hover 覆盖：深色 chip 若 hover 变亮灰会很跳
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = ('\t\t\tFont = UiVisualStyle.Font(8f),\n'
       '\t\t\tForeColor = UiVisualStyle.Text,\n'
       '\t\t\tBackColor = UiVisualStyle.Track,\n'
       '\t\t\tVisible = false\n'
       '\t\t};\n'
       '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
       '\t\tperfConfigEditButton.FlatAppearance.MouseOverBackColor = UiVisualStyle.Border;\n'
       '\t\tperfConfigEditButton.FlatAppearance.MouseDownBackColor = UiVisualStyle.Border;\n')

NEW = ('\t\t\tFont = UiVisualStyle.Font(8f),\n'
       '\t\t\t// 2026-10-04：用户要求底色更深 —— Muted 实心底 + AccentText 前景\n'
       '\t\t\t// （日间 90,107,128 底/白字；夜间 143,163,191 底/深字，两边对比度都够）\n'
       '\t\t\tForeColor = UiVisualStyle.AccentText,\n'
       '\t\t\tBackColor = UiVisualStyle.Muted,\n'
       '\t\t\tVisible = false\n'
       '\t\t};\n'
       '\t\tperfConfigEditButton.FlatAppearance.BorderSize = 0;\n')


def main():
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'BackColor = UiVisualStyle.Muted,' in text:
        print('已经应用过了，无需重复。')
        return 0
    n = text.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    text = text.replace(OLD, NEW, 1)
    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
