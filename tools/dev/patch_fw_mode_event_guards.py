# -*- coding: utf-8 -*-
"""
修：固件配置模式下，事件回调把绑定冲掉
================================================================================
现象（用户截图）：标题仍是「性能模式 · 平衡」、新建/重命名/删除仍可用 ——
                  BindFirmwareProfile 的效果被覆盖。
根因：CustomModeForm 订阅了性能模式事件
      OnPerfActiveChanged -> BindMode(displayedModeId)      （覆盖标题 + LoadAppSide 重载自定义档）
      OnPerfModesChanged  -> _addButton.Enabled = CanAdd(..)（重新启用「新建」）
      这两个回调在绑定之后触发，把固件绑定冲掉。
修法：两处各加 `if (_firmwareMode) return;` 守卫。
注意：CustomModeForm.cs 是 **LF**，锚点用 \n。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

GUARD = (b'\t\t// 2026-10-04: firmware-profile mode owns the UI. These perf-mode event callbacks\r\n'
         b'\t\t// fire after BindFirmwareProfile and would otherwise reload the custom mode.\r\n'
         b'\t\tif (_firmwareMode)\r\n'
         b'\t\t{\r\n'
         b'\t\t\treturn;\r\n'
         b'\t\t}\r\n')

OPS = [
    (b'\tinternal void BindMode(string id)\n\t{\n',
     b'\tinternal void BindMode(string id)\n\t{\n' + GUARD,
     'BindMode 守卫'),
    (b'\tprivate void OnPerfModesChanged()\n\t{\n',
     b'\tprivate void OnPerfModesChanged()\n\t{\n' + GUARD,
     'OnPerfModesChanged 守卫'),
]


def main():
    data = io.open(F, 'rb').read()
    if data.count(b'if (_firmwareMode)') >= 3:
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
    # 保持纯 LF
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
