# -*- coding: utf-8 -*-
"""
去掉 SelectFirmwareTargetAsync 里的 SetMode —— 它会真的切换工作模式
================================================================================
用户实测：「我静音模式打开配置给我切平衡去了」
根因：`SelectFirmwareTargetAsync` 里调 `await hw.SetMode(_firmwareVisualMode)`，
      而 `SetMode` **会真的切换工作模式**；一旦 `_firmwareVisualMode` 与实际模式不一致
      （例如编辑器沿用旧值），用户就被切走了。
设计上它本就不需要：
  * 编辑器只编辑**当前模式**
  * 当前生效的配置**本来就等于下拉所选那个** —— 因为用户在下拉里换配置时，
    SettingsForm 已调用 SwitchPerformanceModeAsync（带 ProfileIndex）完成了切换 ✅
修法：删掉 SetMode 与那 150ms 延时，只做**只读回读确认**；
      确认不上就返回 false（调用方中止写入），**绝不切换模式**。
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\t/**/\n'
       b'\t\tLogger.WriteLine($"FirmwareProfile: selecting {text} (visualMode={_firmwareVisualMode})");\n'
       b'\t\tawait hw.SetMode(_firmwareVisualMode);\n')

# 实际文本可能没有 /**/ 那行，备用锚点
OLD2 = (b'\t\tLogger.WriteLine($"FirmwareProfile: selecting {text} (visualMode={_firmwareVisualMode})");\n'
        b'\t\tawait hw.SetMode(_firmwareVisualMode);\n')

NEW = (b'\t\t// 2026-10-05: NO SetMode() here. It performs a REAL mode switch, and when the\n'
       b'\t\t// editor target and the active mode disagreed the user got moved to another mode\n'
       b'\t\t// ("I opened the config editor in silent mode and it switched me to balanced").\n'
       b'\t\t// The editor only edits the active mode, and the active profile already equals the\n'
       b'\t\t// dropdown selection (choosing a config switches to it). Read-only verify below.\n'
       b'\t\tLogger.WriteLine($"FirmwareProfile: verifying {text} (visualMode={_firmwareVisualMode})");\n')


def main():
    data = io.open(F, 'rb').read()
    if b'verifying {text}' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD) + data.count(OLD2)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    if data.count(OLD) == 1:
        data = data.replace(OLD, NEW, 1)
    else:
        data = data.replace(OLD2, NEW, 1)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
