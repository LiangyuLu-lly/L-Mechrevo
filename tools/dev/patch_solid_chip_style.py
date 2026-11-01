# -*- coding: utf-8 -*-
"""
「配置编辑」按钮底色被主题覆盖的真正修法
================================================================================
根因：UiVisualStyle.ApplyTree -> ApplySecondaryButton 对**无 Tag** 的按钮强制
      `BackColor = SurfaceRaised`，所以 SettingsForm 初始化里设什么都会被覆盖
      （apply 由 ApplyWindow/ApplySection/ApplyRow 触发，日夜切换时还会再跑一遍）。

修法：新增一个主题样式标签 `solid-chip`（深色实心 chip），在 ApplyTree 里处理 ——
      这样初始渲染与日夜重绘都走同一处，不会被覆盖。
      UiVisualStyle.cs 是 CRLF（且少量 LF 混排），故用**字节级替换**，不动其他行尾。
"""
import io
import os
import sys

F_STYLE = r'D:\openLM\source\MechrevoLite.UI\UiVisualStyle.cs'
F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

T8 = b'\t' * 8
T9 = b'\t' * 9
T10 = b'\t' * 10
CRLF = b'\r\n'

ANCHOR_STYLE = (T8 + b'else if (object.Equals(buttonBase.Tag, "mini-chip"))' + CRLF
                + T8 + b'{' + CRLF
                + T9 + b'buttonBase.BackColor = Surface;' + CRLF)

NEW_STYLE = (T8 + b'else if (object.Equals(buttonBase.Tag, "solid-chip"))' + CRLF
             + T8 + b'{' + CRLF
             + T9 + b'// 2026-10-04: dark solid chip (deeper than the background, for emphasis' + CRLF
             + T9 + b'// buttons such as the 30-series "config edit" chip). Muted bg + AccentText fg' + CRLF
             + T9 + b'// keeps contrast in both day and night palettes.' + CRLF
             + T9 + b'buttonBase.BackColor = Muted;' + CRLF
             + T9 + b'buttonBase.ForeColor = AccentText;' + CRLF
             + T9 + b'buttonBase.FlatAppearance.BorderSize = 0;' + CRLF
             + T9 + b'buttonBase.FlatAppearance.MouseOverBackColor = Muted;' + CRLF
             + T9 + b'buttonBase.FlatAppearance.MouseDownBackColor = Muted;' + CRLF
             + T9 + b'if (buttonBase is RButton rButtonSolid)' + CRLF
             + T9 + b'{' + CRLF
             + T10 + b'rButtonSolid.BorderColor = Muted;' + CRLF
             + T9 + b'}' + CRLF
             + T8 + b'}' + CRLF
             + ANCHOR_STYLE)

ANCHOR_SF = (b'\t\tperfConfigEditButton = new RButton\r\n'
             b'\t\t{\r\n'
             b'\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\r\n')

NEW_SF = (b'\t\tperfConfigEditButton = new RButton\r\n'
          b'\t\t{\r\n'
          b'\t\t\tTag = "solid-chip",\r\n'
          b'\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\r\n')


def main():
    style = io.open(F_STYLE, 'rb').read()
    sf = io.open(F_SF, 'rb').read()
    if b'solid-chip' in style and b'Tag = "solid-chip"' in sf:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    if style.count(ANCHOR_STYLE) != 1:
        errors.append('UiVisualStyle.cs 锚点命中 %d 次(期望 1)' % style.count(ANCHOR_STYLE))
    if sf.count(ANCHOR_SF) != 1:
        errors.append('SettingsForm.cs 锚点命中 %d 次(期望 1)' % sf.count(ANCHOR_SF))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（2 项）===')
    if '--check' in sys.argv:
        return 0
    io.open(F_STYLE, 'wb').write(style.replace(ANCHOR_STYLE, NEW_STYLE, 1))
    print('  写入 ' + F_STYLE)
    io.open(F_SF, 'wb').write(sf.replace(ANCHOR_SF, NEW_SF, 1))
    print('  写入 ' + F_SF)
    return 0


if __name__ == '__main__':
    sys.exit(main())
