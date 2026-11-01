# -*- coding: utf-8 -*-
"""
修"先对后反"：重绑编辑器的判断漏了 _refreshingConfigLabels 守卫
================================================================================
日志（用户 00:38 实测）：
  00:38:13.148 BindFirmwareProfile: visualMode=2 current=2 label=静音   ← 正确
  00:38:13.415 BindFirmwareProfile: visualMode=0 current=2 label=平衡   ← 267ms 后变反
  00:38:19.021 BindFirmwareProfile: visualMode=0 current=0 label=平衡   ← 正确
  00:38:19.243 BindFirmwareProfile: visualMode=2 current=0 label=静音   ← 又反
根因：我加的"配置下拉变化 -> 重绑编辑器"写在 `_refreshingConfigLabels` 守卫**之外**。
      `RefreshPerfConfigComboLabels()` 会给三个下拉都设置 SelectedIndex ->
      三个下拉**各触发一次** SelectedIndexChanged -> 每次都用**自己那个模式**重绑编辑器
      -> 最后一个（静音/平衡）胜出，且恰好是"上一个模式"，表现为互换 ✅
修法：重绑条件追加 `&& !_refreshingConfigLabels`
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\t\t\t\tif (customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n'
       b'\t\t\t\t\t{\r\n'
       b'\t\t\t\t\t\tcustomModeForm.BindFirmwareProfile(modeValue, selectedIndex);\r\n'
       b'\t\t\t\t\t}\r\n')

NEW = (b'\t\t\t\t\t// 2026-10-05: MUST also honour _refreshingConfigLabels. Re-labelling the combos\r\n'
       b'\t\t\t\t\t// sets their SelectedIndex, which fires this handler once per combo; without\r\n'
       b'\t\t\t\t\t// this guard the last combo rebound the editor to a different mode\r\n'
       b'\t\t\t\t\t// ("balanced/silent shown swapped").\r\n'
       b'\t\t\t\t\tif (!_refreshingConfigLabels && customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)\r\n'
       b'\t\t\t\t\t{\r\n'
       b'\t\t\t\t\t\tcustomModeForm.BindFirmwareProfile(modeValue, selectedIndex);\r\n'
       b'\t\t\t\t\t}\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'!_refreshingConfigLabels && customModeForm' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
