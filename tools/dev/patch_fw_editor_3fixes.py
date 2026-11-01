# -*- coding: utf-8 -*-
"""
修用户实测反馈的 3 个问题
================================================================================
1. 绿色状态字把「配置 N」盖没了
   根因：我在 BindFirmwareProfile 里把配置号写进了 `_status`，而 `_status` 是
         **操作结果**标签（"已切换到…/已生效 N 项…"），一有操作就被覆盖。
   修法：配置号改写到 `_routeHint`（那个"官方默认参数。修改功耗墙…"提示标签），
         `_status` 留给操作结果。
2. 配置窗口打开时切换模式不刷新
   根因：`BindMode` 的 `_firmwareMode` 守卫直接 `return`，模式变化被忽略。
   修法：守卫里改为**跟随新模式重新绑定**（BindFirmwareProfile）。
3. 「重命名」是灰的
   根因：它由 `BindMode` 按 `mode.IsCustom` 启用，固件模式下 BindMode 被守卫跳过
         -> 没人把它打开（只对自定义档有意义）。
   修法：BindFirmwareProfile 的锁定块里显式 `_renameButton.Enabled = true;`
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# ---- 1) 配置号从 _status 挪到 _routeHint ----
op(b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n'
   b'\t\t_status.ForeColor = UiVisualStyle.Muted;\n'
   b'\t\tSetOptionsEnabled();\n',
   b'\t\tSetOptionsEnabled();\n',
   '去掉 _status 里的配置号')

op(b'\t\tif (!string.IsNullOrWhiteSpace(_firmwareProfileName))\n'
   b'\t\t{\n'
   b'\t\t\ttext2 = text2 + " \\u00b7 " + _firmwareProfileName;\n'
   b'\t\t}\n'
   b'\t\tText = text2;\n',
   b'\t\tif (!string.IsNullOrWhiteSpace(_firmwareProfileName))\n'
   b'\t\t{\n'
   b'\t\t\ttext2 = text2 + " \\u00b7 " + _firmwareProfileName;\n'
   b'\t\t}\n'
   b'\t\tText = text2;\n'
   b'\t\t// 2026-10-05: the profile line lives in the hint label -- _status belongs to\r\n'
   b'\t\t// operation results and would overwrite it as soon as anything is applied.\r\n'
   b'\t\t_routeHint.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1)\r\n'
   b'\t\t\t+ (string.IsNullOrWhiteSpace(_firmwareProfileName) ? "" : (" \\u00b7 " + _firmwareProfileName))\r\n'
   b'\t\t\t+ " \\u00b7 " + Strings.PerfModeHintVendor;\r\n',
   '配置号写进 _routeHint')

# ---- 2) 跟随模式变化 ----
op(b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tPerfModeDefinition mode = FindMode(id) ?? ModesSnapshot().FirstOrDefault();\n',
   b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\t// 2026-10-05: while the editor is open, follow the newly active mode instead\r\n'
   b'\t\t\t// of ignoring the event (the user reported a stale display).\r\n'
   b'\t\t\tint num = Program.service?.CurrentMode ?? _firmwareVisualMode;\r\n'
   b'\t\t\tif (num != _firmwareVisualMode)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tBindFirmwareProfile(num, ModeProfileConfig.Get(num));\r\n'
   b'\t\t\t}\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\n'
   b'\t\tPerfModeDefinition mode = FindMode(id) ?? ModesSnapshot().FirstOrDefault();\n',
   'BindMode 守卫改为跟随新模式')

# ---- 3) 显式启用「重命名」 ----
op(b'\t\t_addButton.Enabled = false;\n'
   b'\t\t_deleteButton.Enabled = false;\n'
   b'\t\t// 2026-10-05: sync the mode combo LAST',
   b'\t\t_addButton.Enabled = false;\n'
   b'\t\t_deleteButton.Enabled = false;\n'
   b'\t\t// 2026-10-05: rename IS meaningful for a firmware profile (CustomizeName), and\r\n'
   b'\t\t// nothing else enables it in this mode because BindMode is skipped.\r\n'
   b'\t\t_renameButton.Enabled = true;\r\n'
   b'\t\t// 2026-10-05: sync the mode combo LAST',
   '显式启用重命名')


def main():
    data = io.open(F, 'rb').read()
    if b'_renameButton.Enabled = true;' in data:
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
