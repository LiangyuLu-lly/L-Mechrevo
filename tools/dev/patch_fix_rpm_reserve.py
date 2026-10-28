# -*- coding: utf-8 -*-
r"""
修上一步的编译错误：TelemetryRestReserveWidth() 用了方法内局部函数 D()，不可见
改用 TextRenderer 按当前字体实测宽度（无需 D，且 DPI/字体变化都自动正确）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = ('\tprivate int TelemetryRestReserveWidth()\r\n'
       '\t{\r\n'
       '\t\treturn _telemetryRpmSeen ? D(84) : D(34);\r\n'
       '\t}\r\n').encode('utf-8')

NEW = ('\tprivate int TelemetryRestReserveWidth()\r\n'
       '\t{\r\n'
       '\t\t// 按当前字体实测："1234rpm 100%"（有转速）或 "100%"（无转速）。\r\n'
       '\t\t// 不用 D()：它是构建方法里的局部函数，这里不可见；实测宽度也天然跟随 DPI 与字体。\r\n'
       '\t\tstring sample = _telemetryRpmSeen ? "1234rpm 100%" : "100%";\r\n'
       '\t\tFont font = _telemetryCpuRest?.Font ?? _telemetryCpuName?.Font ?? Font;\r\n'
       '\t\treturn TextRenderer.MeasureText(sample, font, new Size(1000, 100), TextFormatFlags.NoPadding).Width;\r\n'
       '\t}\r\n').encode('utf-8')


def main():
    data = io.open(F, 'rb').read()
    if b'"1234rpm 100%" : "100%"' in data:
        print('已经修过了。')
        return 0
    if data.count(OLD) != 1:
        alt = OLD.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            io.open(F, 'wb').write(data.replace(alt, NEW.replace(b'\r\n', b'\n'), 1))
            print('  写入 ' + F)
            return 0
        print('=== 校验失败：命中 %d 次，未写盘 ===' % data.count(OLD))
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
