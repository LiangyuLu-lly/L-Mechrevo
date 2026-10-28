# -*- coding: utf-8 -*-
r"""
🎯 真正的 bug：子串匹配写错
================================================================================
实测 text24 = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"
  我判的是 Contains("DIRECT_CONNECT_IGPU")  -> 中间还有 "_TOGGLE_" -> **不匹配** ❌
  正确（与 MechrevoHw.cs:1480 一致）：精确等于 "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"
  或包含 "DIRECT_CONNECT_IGPU"（用于 DIRECT_CONNECT_IGPU 这类变体）
修两处：MechrevoHw.cs（v2/v3 的置位）+ MechrevoDeviceCapabilities.cs（v1 的辅助方法）
"""
import io
import sys

HW = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
CAP = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- A) MechrevoHw：置位块的条件 ----
op(HW,
   '\t\tif (!string.IsNullOrWhiteSpace(text24) && text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase))\n',
   '\t\t// 注意：真实 token 是 DGPU_DIRECT_CONNECT_TOGGLE_IGPU（中间有 _TOGGLE_），\n'
   '\t\t// 只 Contains("DIRECT_CONNECT_IGPU") 匹配不上 —— 与 :1480 的判法保持一致。\n'
   '\t\tif (!string.IsNullOrWhiteSpace(text24)\n'
   '\t\t\t&& (text24.Equals("DGPU_DIRECT_CONNECT_TOGGLE_IGPU", StringComparison.OrdinalIgnoreCase)\n'
   '\t\t\t\t|| text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase)))\n',
   'MechrevoHw 置位条件')

# ---- B) MechrevoDeviceCapabilities：辅助方法 ----
op(CAP,
   '\t\treturn !string.IsNullOrWhiteSpace(status)\n'
   '\t\t\t&& status.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase);\n',
   '\t\treturn !string.IsNullOrWhiteSpace(status)\n'
   '\t\t\t&& (status.Equals("DGPU_DIRECT_CONNECT_TOGGLE_IGPU", StringComparison.OrdinalIgnoreCase)\n'
   '\t\t\t\t|| status.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase));\n',
   'Capabilities 辅助方法')


def main():
    resolved, errors = [], []
    for path, old, new, expect, label in OPS:
        data = io.open(path, 'rb').read()
        if data.count(old) == expect:
            resolved.append((path, old, new, expect, label))
            continue
        if b'\r\n' in old:
            ao, an = old.replace(b'\r\n', b'\n'), new.replace(b'\r\n', b'\n')
        else:
            ao, an = old.replace(b'\n', b'\r\n'), new.replace(b'\n', b'\r\n')
        if data.count(ao) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((path, ao, an, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(ao), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in resolved:
        d = io.open(path, 'rb').read()
        io.open(path, 'wb').write(d.replace(old, new, expect))
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
