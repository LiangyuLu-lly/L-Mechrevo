# -*- coding: utf-8 -*-
"""
🔴 修「模式标签翻转」真因：BindMode 守卫不该从传入的 id 反推模式
================================================================================
日志（用户 2026-10-05 00:42 实测）：
  00:41:58.492 BindFirmwareProfile: visualMode=2 current=2 label=静音   ← 对
  00:41:58.733 BindFirmwareProfile: visualMode=0 current=2 label=平衡   ← +241ms 翻成 0
  且**不论实际模式是什么，翻转值永远是 0（平衡）**
根因：守卫里用 `id`（来自 `PerfModeService.DisplayedModeId`）反推模式号，
      而该 id 在机器已切到静音后**仍是 "balanced"**（服务侧模式 id 未同步）
      -> `BindMode("balanced")` -> num=0 -> 重绑到平衡 ❌
修法：改用权威来源 `Program.service.CurrentMode`（日志中 current 字段一直正确）。
      这样 `BindMode("balanced")` 时 num=2 == _firmwareVisualMode -> **不重绑**，翻转消失。
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\t\t// 2026-10-05: follow the newly active mode while the editor is open.\n'
       b'\t\t\t// Derive the visual mode from the id that triggered this call -- reading\n'
       b'\t\t\t// Program.service.CurrentMode here happened too early and nothing refreshed.\n'
       b'\t\t\tint num = (string.Equals(id, PerfModeDefinition.BuiltInId(PerfModeKind.Silent), StringComparison.Ordinal) ? 2 : (string.Equals(id, PerfModeDefinition.BuiltInId(PerfModeKind.Balanced), StringComparison.Ordinal) ? 0 : 1));\n')

NEW = (b'\t\t\t// 2026-10-05: use the authoritative current mode, NOT the id passed in.\n'
       b'\t\t\t// PerfModeService.DisplayedModeId stayed "balanced" while the machine was actually\n'
       b'\t\t\t// in silent mode, so mapping from `id` rebound the editor to the wrong mode about\n'
       b'\t\t\t// 250 ms after every open (the reported "balanced/silent swapped").\n'
       b'\t\t\tint num = Program.service?.CurrentMode ?? _firmwareVisualMode;\n')


def main():
    data = io.open(F, 'rb').read()
    if b'use the authoritative current mode, NOT the id' in data:
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
