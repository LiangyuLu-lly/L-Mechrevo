# -*- coding: utf-8 -*-
"""
🔴 修：当前模式内换配置时，新的 ProfileIndex 没发出去
================================================================================
用户实测：「我切配置貌似里面参数不刷新」+ 状态栏「参数未应用，已恢复回读值」
日志：
  00:48:25.921 verifying Mode2_Profile1 -> confirmed -> SetCustomDetail PL1=44  ✅
  00:48:43.778 verifying Mode2_Profile2           ← 用户切到「配置 2」
  00:48:46.524 readback ProfileName=Mode2_Profile1 expected=Mode2_Profile2 MISMATCH - write aborted
文件证据（参数确实不同，不是没刷新）：
  Mode2_Profile1 AmdSPL=44 / Mode2_Profile2..5 AmdSPL=35
根因：`SwitchPerformanceModeAsync(modeValue)` 对"切到**已激活**的模式"会**去重跳过**，
      于是新的 ProfileIndex 从未 publish -> 固件仍停在旧配置 ->
      之后的参数写入落到错误配置 -> 被 SelectFirmwareTargetAsync 的安全阀拒绝
      -> 界面显示"已恢复回读值"（旧配置的值），看起来像"参数不刷新"
修法：当前模式内换配置时，额外直接调 `Program.hw.SetMode(modeValue)` ——
      `SetMode` **总是** publish mode + ProfileIndex（经 ResolveModeProfileIndex 取新配置号）。
      模式没变，所以不会造成"乱跳"。
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\t\t\t\tif (Program.service != null && Program.service.CurrentMode == modeValue)\r\n'
       b'\t\t\t\t\t{\r\n'
       b'\t\t\t\t\t\t// \xe8\xaf\xa5\xe6\xa8\xa1\xe5\xbc\x8f\xe6\xad\xa3\xe6\x98\xaf\xe5\xbd\x93\xe5\x89\x8d\xe6\xa8\xa1\xe5\xbc\x8f\xef\xbc\x9a\xe7\xab\x8b\xe5\x8d\xb3\xe6\x8c\x89\xe6\x96\xb0\xe9\x85\x8d\xe7\xbd\xae\xe5\x8f\xb7\xe9\x87\x8d\xe6\x96\xb0\xe4\xb8\x8b\xe5\x8f\x91\r\n')

# 用更稳妥的 ASCII 锚点：只匹配第一行 + 左花括号
OLD_SAFE = (b'\t\t\t\t\tif (Program.service != null && Program.service.CurrentMode == modeValue)\r\n'
            b'\t\t\t\t\t{\r\n')

NEW = (b'\t\t\t\t\tif (Program.service != null && Program.service.CurrentMode == modeValue)\r\n'
       b'\t\t\t\t\t{\r\n'
       b'\t\t\t\t\t\t// 2026-10-05: SwitchPerformanceModeAsync() dedupes a switch to the\r\n'
       b'\t\t\t\t\t\t// already-active mode, so the newly chosen ProfileIndex was never\r\n'
       b'\t\t\t\t\t\t// published -- the firmware stayed on the OLD profile, later writes\r\n'
       b'\t\t\t\t\t\t// landed on the wrong config and were refused ("params reverted").\r\n'
       b'\t\t\t\t\t\t// SetMode() always publishes mode + ProfileIndex. The mode itself\r\n'
       b'\t\t\t\t\t\t// does not change, so this cannot cause an unwanted mode switch.\r\n'
       b'\t\t\t\t\t\tMechrevoHw hwConfigRow = Program.hw;\r\n'
       b'\t\t\t\t\t\tif (hwConfigRow != null && hwConfigRow.IsConnected && hwConfigRow.SupportsModeProfileConfig)\r\n'
       b'\t\t\t\t\t\t{\r\n'
       b'\t\t\t\t\t\t\tawait hwConfigRow.SetMode(modeValue);\r\n'
       b'\t\t\t\t\t\t}\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'hwConfigRow' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD_SAFE)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD_SAFE, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
