# -*- coding: utf-8 -*-
"""
关键修复：写参数时**不得强切模式**（造成"性能模式乱跳"）
================================================================================
实测日志（用户 00:15 截图对应的日志）：
  00:15:02 FirmwareProfile: selecting Mode1_Profile1 (visualMode=0)   ← 当时在静音，却切到游戏
  00:15:06 FirmwareProfile: selecting Mode2_Profile3 (visualMode=2)
  00:15:16 FirmwareProfile: selecting Mode1_Profile1 (visualMode=0)
根因：`SelectFirmwareTargetAsync` 用 `SetMode` 去"选中配置"，而 `SetMode` **会真的切换工作模式**；
      叠加 BindMode 守卫里的"编辑器跟随模式变化"→ 形成反馈环 → 模式乱跳。
修法：用户早已拍板「下拉跟随当前模式」，即编辑器**只编辑当前模式**。
      故 `SelectFirmwareTargetAsync` 里若目标模式 != 当前模式：
        - **不切换**
        - 改为重新绑定到当前模式
        - 返回 false（调用方据此中止本次写入，避免写进别的配置）
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\tif (hw == null || !hw.IsConnected)\n'
       b'\t\t{\n'
       b'\t\t\treturn false;\n'
       b'\t\t}\n'
       b'\t\tint num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));\n')

NEW = (b'\t\tif (hw == null || !hw.IsConnected)\n'
       b'\t\t{\n'
       b'\t\t\treturn false;\n'
       b'\t\t}\n'
       b'\t\t// 2026-10-05: NEVER switch the working mode here. SetMode() performs a real mode\r\n'
       b'\t\t// switch, and combined with "the editor follows mode changes" that produced a\r\n'
       b'\t\t// feedback loop where the machine appeared to jump between modes. The editor is\r\n'
       b'\t\t// only ever supposed to edit the CURRENT mode (user decision: the profile list\r\n'
       b'\t\t// follows the active mode), so a mismatch means "re-target, do not write".\r\n'
       b'\t\tint num2 = Program.service?.CurrentMode ?? _firmwareVisualMode;\r\n'
       b'\t\tif (num2 != _firmwareVisualMode)\r\n'
       b'\t\t{\r\n'
       b'\t\t\tLogger.WriteLine($"FirmwareProfile: target visualMode={_firmwareVisualMode} != active {num2}; re-targeting instead of switching");\r\n'
       b'\t\t\tBindFirmwareProfile(num2, ModeProfileConfig.Get(num2));\r\n'
       b'\t\t\treturn false;\r\n'
       b'\t\t}\r\n'
       b'\t\tint num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));\n')


def main():
    data = io.open(F, 'rb').read()
    if b're-targeting instead of switching' in data:
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
