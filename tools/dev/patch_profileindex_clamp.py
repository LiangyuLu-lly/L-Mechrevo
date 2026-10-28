# -*- coding: utf-8 -*-
"""
二期第 1 步：修 VendorModeDefaults.ProfileIndexFor 的钳位
原：Math.Clamp(valueOrDefault, 0, 1)  —— 只允许 0/1，但官方每个模式有 5 个配置
                                  （Mode{N}_Profile1..5.json，实测本机 15 个文件齐全）
后果：MainOption.json 里若绑定的是第 3/4/5 个配置，读回来会被钳成 0/1 -> 取到错误的种子默认值
改：钳到 0..(MaxProfileCount-1)，并加具名常量
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite.Mode\VendorModeDefaults.cs'

OLD = b'result = Math.Clamp(valueOrDefault, 0, 1);'
NEW = (b'// 2026-10-04: the official console keeps five profiles per mode\r\n'
       b'\t\t\t\t// (Mode{N}_Profile1..5.json). Clamping to 0..1 silently mapped profile\r\n'
       b'\t\t\t\t// 3/4/5 back onto 1/2 and returned the wrong seed defaults.\r\n'
       b'\t\t\t\tresult = Math.Clamp(valueOrDefault, 0, MaxProfileCount - 1);')

CONST_ANCHOR = b'internal static class VendorModeDefaults\r\n{\r\n'
CONST_NEW = (b'internal static class VendorModeDefaults\r\n'
             b'{\r\n'
             b'\t/// <summary>Official console keeps five profiles per mode (Mode{N}_Profile1..5.json).</summary>\r\n'
             b'\tinternal const int MaxProfileCount = 5;\r\n'
             b'\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'MaxProfileCount' in data:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    if data.count(OLD) != 1:
        errors.append('钳位锚点命中 %d 次(期望 1)' % data.count(OLD))
    if data.count(CONST_ANCHOR) != 1:
        errors.append('常量锚点命中 %d 次(期望 1)' % data.count(CONST_ANCHOR))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（2 项）===')
    if '--check' in sys.argv:
        return 0
    data = data.replace(CONST_ANCHOR, CONST_NEW, 1).replace(OLD, NEW, 1)
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
