# -*- coding: utf-8 -*-
"""
紧急修复：LM 卡死（未响应）—— 我引入的重入死循环
================================================================================
环路：
  FlushPendingAsync -> SelectFirmwareTargetAsync
    -> BindFirmwareProfile            ← 00:17 那次修复新加的链接
      -> LoadAppSide                  （改控件值）
        -> 控件事件 -> Queue -> _debounce -> FlushPendingAsync  → 回到起点
修法（两头都堵）：
  1. `SelectFirmwareTargetAsync` 的目标不匹配分支**不再重新绑定**，只记日志 + 返回 false
     （去掉环路里最新加的那条边）
  2. `BindFirmwareProfile` 增加**重入守卫** `_bindingFirmwareProfile`
     （即使将来再引入环路也不会无限递归）
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# 1) 字段
op(b'\tprivate string? _firmwareProfileName;\n',
   b'\tprivate string? _firmwareProfileName;\n'
   b'\n'
   b'\t// 2026-10-05: re-entrancy guard. BindFirmwareProfile -> LoadAppSide -> control events\r\n'
   b'\t// -> Queue -> FlushPendingAsync -> SelectFirmwareTargetAsync could loop back here and\r\n'
   b'\t// froze the whole UI. Never allow a nested bind.\r\n'
   b'\tprivate bool _bindingFirmwareProfile;\n',
   '新增重入守卫字段')

# 2) 方法开头加守卫
op(b'\tinternal void BindFirmwareProfile(int visualMode, int profileIndex)\n\t{\n',
   b'\tinternal void BindFirmwareProfile(int visualMode, int profileIndex)\n'
   b'\t{\n'
   b'\t\tif (_bindingFirmwareProfile)\n'
   b'\t\t{\n'
   b'\t\t\tLogger.WriteLine("BindFirmwareProfile: re-entrant call ignored");\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\r\n'
   b'\t\t_bindingFirmwareProfile = true;\r\n',
   '方法开头加重入守卫')

# 3) 方法结尾清标志
op(b'\t\tSyncFirmwareModeCombo();\n'
   b'\t\tif (IsHandleCreated)\n'
   b'\t\t{\n'
   b'\t\t\tResizeToContent();\n'
   b'\t\t}\n'
   b'\t}\n',
   b'\t\tSyncFirmwareModeCombo();\n'
   b'\t\tif (IsHandleCreated)\n'
   b'\t\t{\n'
   b'\t\t\tResizeToContent();\n'
   b'\t\t}\n'
   b'\t\t_bindingFirmwareProfile = false;\n'
   b'\t}\n',
   '方法结尾清守卫')

# 4) 去掉环路里那条边
op(b'\t\t\tLogger.WriteLine($"FirmwareProfile: target visualMode={_firmwareVisualMode} != active {num2}; re-targeting instead of switching");\n'
   b'\t\t\tBindFirmwareProfile(num2, ModeProfileConfig.Get(num2));\n'
   b'\t\t\treturn false;\n',
   b'\t\t\t// Do NOT call BindFirmwareProfile() here: that closed a feedback loop\r\n'
   b'\t\t\t// (see the re-entrancy guard above). The editor follows mode changes through\r\n'
   b'\t\t\t// BindMode instead; this path just refuses to write into a foreign profile.\r\n'
   b'\t\t\tLogger.WriteLine($"FirmwareProfile: target visualMode={_firmwareVisualMode} != active {num2}; write skipped");\n'
   b'\t\t\treturn false;\n',
   '去掉重新绑定这条边')


def main():
    data = io.open(F, 'rb').read()
    if b'_bindingFirmwareProfile' in data:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, expect, label in OPS:
        n = data.count(old)
        if n != expect:
            errors.append('命中 %d 次(期望 %d)  [%s]' % (n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in OPS:
        data = data.replace(old, new, expect)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
