# -*- coding: utf-8 -*-
"""
诊断：编辑器模式显示可能"平衡/静音互换" —— 把三个真值打进日志
用户在 静音 / 平衡 各打开一次编辑器，即可对照确定是哪一环错了。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\tPerfModeKind kind = visualMode switch\n'
       b'\t\t{\n'
       b'\t\t\t2 => PerfModeKind.Silent,\n'
       b'\t\t\t1 => PerfModeKind.Turbo,\n'
       b'\t\t\t_ => PerfModeKind.Balanced,\n'
       b'\t\t};\n')

NEW = (OLD +
       b'\t\t// 2026-10-05: diagnostics for the reported "balanced/silent shown swapped" issue.\n'
       b'\t\tLogger.WriteLine($"BindFirmwareProfile: visualMode={visualMode} kind={kind} " +\n'
       b'\t\t\t$"current={Program.service?.CurrentMode} comboId={PerfModeDefinition.BuiltInId(kind)} " +\n'
       b'\t\t\t$"label={(visualMode == 2 ? Strings.Silent : (visualMode == 1 ? Strings.Turbo : Strings.Balanced))}");\n')


def main():
    data = io.open(F, 'rb').read()
    if b'balanced/silent shown swapped' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    data = data.replace(OLD, NEW, 1).replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
