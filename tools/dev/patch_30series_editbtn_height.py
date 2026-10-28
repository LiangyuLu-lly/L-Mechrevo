# -*- coding: utf-8 -*-
"""「配置编辑」按钮：补明确高度（只设了 Width，默认高度不够导致文字被压裁）"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = ('\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
       '\t\t\tAutoSize = false,\n'
       '\t\t\tWidth = D(72),\n')

NEW = ('\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
       '\t\t\tAutoSize = false,\n'
       '\t\t\t// 2026-10-04：必须给明确高度 —— 只设 Width 时用 RButton 默认高度，8f 文字会被压裁\n'
       '\t\t\t// 标题行高 D(26)，按钮取 D(24) 留上下各 1px\n'
       '\t\t\tSize = new Size(D(72), D(24)),\n')


def main():
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'Size = new Size(D(72), D(24)),' in text:
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
