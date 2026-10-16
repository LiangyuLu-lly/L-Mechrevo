# -*- coding: utf-8 -*-
"""
细节修正：官方默认 CustomizeName 就是配置号（实测 15 个文件全是 "1".."5"）
-> 等于配置号时视作"未命名"，不显示，避免标题出现「配置 5 · 5」
行尾：VendorModeDefaults.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite.Mode\VendorModeDefaults.cs'

OLD = (b'\t\t\t\tstring text3 = value.GetString();\r\n'
       b'\t\t\t\tif (!string.IsNullOrWhiteSpace(text3))\r\n'
       b'\t\t\t\t{\r\n'
       b'\t\t\t\t\treturn text3;\r\n'
       b'\t\t\t\t}\r\n')

NEW = (b'\t\t\t\tstring text3 = value.GetString();\r\n'
       b'\t\t\t\t// The shipped default is just the profile number -- treat that as "unnamed".\r\n'
       b'\t\t\t\tif (!string.IsNullOrWhiteSpace(text3) && text3 != (Math.Clamp(profileIndex, 0, MaxProfileCount - 1) + 1).ToString())\r\n'
       b'\t\t\t\t{\r\n'
       b'\t\t\t\t\treturn text3;\r\n'
       b'\t\t\t\t}\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'treat that as "unnamed"' in data:
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
