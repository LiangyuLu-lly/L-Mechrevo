# -*- coding: utf-8 -*-
"""
1. 下拉项：有自定义名时**只显示名字**（不再「配置 N · 名字」）
2. 刷新优化：`OnPerfActiveChanged` 里 `_pending.Count == 0` 的前置条件会**挡住快速切换**
   的刷新（有未落盘编辑时整段跳过）→ 固件模式下放开该条件
行尾：CustomModeForm.cs 与 SettingsForm.cs 各按自身行尾写锚点
（CustomModeForm=LF，SettingsForm=CRLF）
"""
import io
import os
import sys

F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---- 1) 只显示名字 ----
op(F_SF,
   b'\t\tstring text = Strings.ModeConfigShort + " " + (profileIndex + 1);\r\n'
   b'\t\tstring text2 = VendorModeDefaults.ReadProfileCustomizeName(kind, profileIndex);\r\n'
   b'\t\tif (!string.IsNullOrWhiteSpace(text2))\r\n'
   b'\t\t{\r\n'
   b'\t\t\ttext = text + " \\u00b7 " + text2;\r\n'
   b'\t\t}\r\n'
   b'\t\treturn text;\r\n',
   b'\t\t// 2026-10-05: when a custom name exists, show ONLY the name (user request).\r\n'
   b'\t\tstring text2 = VendorModeDefaults.ReadProfileCustomizeName(kind, profileIndex);\r\n'
   b'\t\tif (!string.IsNullOrWhiteSpace(text2))\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn text2;\r\n'
   b'\t\t}\r\n'
   b'\t\treturn Strings.ModeConfigShort + " " + (profileIndex + 1);\r\n',
   '有名字时只显示名字')

# ---- 2) 放开 _pending 条件 ----
op(F_CMF,
   b'\t\tif (!string.Equals(displayedModeId, _modeId, StringComparison.Ordinal) && _pending.Count == 0)\n',
   b'\t\t// 2026-10-05: in firmware-profile mode do not let a non-empty _pending batch swallow\r\n'
   b'\t\t// the mode-change refresh -- that is what made quick switching feel unresponsive.\r\n'
   b'\t\tif (!string.Equals(displayedModeId, _modeId, StringComparison.Ordinal) && (_pending.Count == 0 || _firmwareMode))\n',
   '固件模式下放开 _pending 条件')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_SF, F_CMF)}
    if b'_pending.Count == 0 || _firmwareMode' in data[F_CMF]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = data[path].count(old)
        if n != expect:
            errors.append('%-22s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        data[path] = data[path].replace(old, new, expect)
    data[F_CMF] = data[F_CMF].replace(b'\r\n', b'\n')
    for path, blob in data.items():
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
