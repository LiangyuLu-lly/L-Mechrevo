# -*- coding: utf-8 -*-
"""
30 系：隐藏「自定义」性能模式按钮（用户拍板）
理由：官方 30 系控制台只有办公/游戏/狂飙三个模式，没有自定义模式；
      该机型也不上报 CustomProfileIndex，自定义档无法回读确认。
做法：RefreshDeviceCapabilities 里把 buttonCustomMode 的可见性与 ReflowPerformanceButtons 的
      custom 参数同时乘上 !SupportsModeProfileConfig。40/50 系不受影响。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = ('\t\tif (buttonCustomMode != null)\n'
       '\t\t{\n'
       '\t\t\tbuttonCustomMode.Visible = flag17;\n'
       '\t\t}\n'
       '\t\tReflowPerformanceButtons(flag16, flag15, flag17);\n')

NEW = ('\t\t// 2026-10-04：30 系「模式 × 配置」机型隐藏「自定义」按钮\n'
       '\t\t// （官方 30 系控制台无自定义模式，且该机型不上报 CustomProfileIndex，无法回读确认）\n'
       '\t\tbool flagCustomMode = flag17 && !(Program.hw?.SupportsModeProfileConfig ?? false);\n'
       '\t\tif (buttonCustomMode != null)\n'
       '\t\t{\n'
       '\t\t\tbuttonCustomMode.Visible = flagCustomMode;\n'
       '\t\t}\n'
       '\t\tReflowPerformanceButtons(flag16, flag15, flagCustomMode);\n')


def main():
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'flagCustomMode' in text:
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
