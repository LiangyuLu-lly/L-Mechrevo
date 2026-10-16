# -*- coding: utf-8 -*-
"""「配置编辑」按钮：字号调小 + 颜色调深（用户要求）"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = ('\t\tperfConfigEditButton = new RButton\n'
       '\t\t{\n'
       '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
       '\t\t\tAutoSize = false,\n'
       '\t\t\tWidth = D(72),\n'
       '\t\t\tFlatStyle = FlatStyle.Flat,\n'
       '\t\t\tForeColor = UiVisualStyle.Text,\n'
       '\t\t\tBackColor = UiVisualStyle.SurfaceRaised,\n'
       '\t\t\tVisible = false\n'
       '\t\t};\n'
       '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n')

NEW = ('\t\tperfConfigEditButton = new RButton\n'
       '\t\t{\n'
       '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
       '\t\t\tAutoSize = false,\n'
       '\t\t\tWidth = D(72),\n'
       '\t\t\tFlatStyle = FlatStyle.Flat,\n'
       '\t\t\t// 2026-10-04：字号比相邻标签(8.5)再小一点；底色用 Track（比 SurfaceRaised 深）\n'
       '\t\t\tFont = UiVisualStyle.Font(8f),\n'
       '\t\t\tForeColor = UiVisualStyle.Text,\n'
       '\t\t\tBackColor = UiVisualStyle.Track,\n'
       '\t\t\tVisible = false\n'
       '\t\t};\n'
       '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
       '\t\tperfConfigEditButton.FlatAppearance.MouseOverBackColor = UiVisualStyle.Border;\n'
       '\t\tperfConfigEditButton.FlatAppearance.MouseDownBackColor = UiVisualStyle.Border;\n')


def main():
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'UiVisualStyle.Font(8f)' in text and 'BackColor = UiVisualStyle.Track' in text:
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
