# -*- coding: utf-8 -*-
"""
修写入路径的竞态：目标切换未确认就写参数 -> 值写进了【错误的配置】
================================================================================
实测日志（2026-10-04 23:53）：
    23:53:03.513 FirmwareProfile: selecting Mode1_Profile5
    23:53:03.683 readback ProfileName=Mode1_Profile2 expected=Mode1_Profile5 MISMATCH
    23:53:04.281 SetCustomDetail confirmed=True: PL1=66      ← 写进了 Profile2（错误目标）
  （5.98s 后 debounce 重试才成功，所以最终结果是对的，但中间污染了别的配置）
根因：SelectFirmwareTargetAsync 里只 `Task.Delay(150)` 就返回，切换完成时间不定。
修法：
  1. 改为**轮询**等待 ProfileName 命中（最多 ~2s，每 100ms 一次），返回 bool
  2. 调用方：选中失败就**不写**（清空待写字段 + 状态栏报错），避免污染错误配置
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = [
    # 1) 调用方：失败就不写
    (b'\t\tif (_firmwareMode)\n'
     b'\t\t{\n'
     b'\t\t\tawait SelectFirmwareTargetAsync();\n'
     b'\t\t}\n',
     b'\t\tif (_firmwareMode && !await SelectFirmwareTargetAsync())\n'
     b'\t\t{\n'
     b'\t\t\t// Target profile could not be confirmed -- writing now would land in whatever\n'
     b'\t\t\t// profile happens to be active. Drop the batch and tell the user.\n'
     b'\t\t\t_pending.Clear();\n'
     b'\t\t\t_status.Text = Strings.ParamsReverted;\n'
     b'\t\t\t_status.ForeColor = UiVisualStyle.Danger;\n'
     b'\t\t\treturn;\n'
     b'\t\t}\n',
     '选中失败则不写'),
    # 2) 轮询等待 + 返回 bool
    (b'\tprivate async Task SelectFirmwareTargetAsync()\n'
     b'\t{\n',
     b'\tprivate async Task<bool> SelectFirmwareTargetAsync()\n'
     b'\t{\n',
     '签名改为返回 bool'),
    (b'\t\tif (hw == null || !hw.IsConnected)\n'
     b'\t\t{\n'
     b'\t\t\treturn;\n'
     b'\t\t}\n'
     b'\t\tint num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));\n'
     b'\t\tstring text = "Mode" + num + "_Profile" + (_firmwareProfileIndex + 1);\n'
     b'\t\tLogger.WriteLine($"FirmwareProfile: selecting {text} (visualMode={_firmwareVisualMode})");\n'
     b'\t\tawait hw.SetMode(_firmwareVisualMode);\n'
     b'\t\tawait Task.Delay(150);\n'
     b'\t\tLogger.WriteLine($"FirmwareProfile: readback ProfileName={hw.ProfileName} expected={text}" + ((hw.ProfileName == text) ? " OK" : " MISMATCH"));\n'
     b'\t}\n',
     b'\t\tif (hw == null || !hw.IsConnected)\n'
     b'\t\t{\n'
     b'\t\t\treturn false;\n'
     b'\t\t}\n'
     b'\t\tint num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));\n'
     b'\t\tstring text = "Mode" + num + "_Profile" + (_firmwareProfileIndex + 1);\n'
     b'\t\tLogger.WriteLine($"FirmwareProfile: selecting {text} (visualMode={_firmwareVisualMode})");\n'
     b'\t\tawait hw.SetMode(_firmwareVisualMode);\n'
     b'\t\t// Poll instead of a fixed delay: the firmware takes an unpredictable time to\r\n'
     b'\t\t// load the profile, and writing before that lands in the wrong profile.\r\n'
     b'\t\tfor (int i = 0; i < 20; i++)\r\n'
     b'\t\t{\r\n'
     b'\t\t\tif (hw.ProfileName == text)\r\n'
     b'\t\t\t{\r\n'
     b'\t\t\t\tLogger.WriteLine($"FirmwareProfile: confirmed {text} after {i * 100}ms");\r\n'
     b'\t\t\t\treturn true;\r\n'
     b'\t\t\t}\r\n'
     b'\t\t\tawait Task.Delay(100);\r\n'
     b'\t\t}\r\n'
     b'\t\tLogger.WriteLine($"FirmwareProfile: readback ProfileName={hw.ProfileName} expected={text} MISMATCH - write aborted");\r\n'
     b'\t\treturn false;\r\n'
     b'\t}\n',
     '轮询等待实现'),
]


def main():
    data = io.open(F, 'rb').read()
    if b'write aborted' in data:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, label in OPS:
        n = data.count(old)
        if n != 1:
            errors.append('命中 %d 次(期望 1)  [%s]' % (n, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for old, new, label in OPS:
        data = data.replace(old, new, 1)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
