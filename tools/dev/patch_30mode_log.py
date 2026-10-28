# -*- coding: utf-8 -*-
"""在 ResolveModeProfileIndex 的新分支加一行日志 —— 让「模式 × 配置」判据在日志里可验证。"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD = ('\t\tif (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(consoleMode))\n'
       '\t\t{\n'
       '\t\t\treturn ModeProfileConfig.Get(consoleMode);\n'
       '\t\t}\n')

NEW = ('\t\tif (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(consoleMode))\n'
       '\t\t{\n'
       '\t\t\tint num = ModeProfileConfig.Get(consoleMode);\n'
       '\t\t\tLogger.WriteLine($"ModeProfileConfig: visualMode={visualMode} consoleMode={consoleMode} profile={num} count={ModeProfileConfig.Count}");\n'
       '\t\t\treturn num;\n'
       '\t\t}\n')


def main():
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'ModeProfileConfig: visualMode=' in text:
        print('已经应用过了。')
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
