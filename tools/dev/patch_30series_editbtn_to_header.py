# -*- coding: utf-8 -*-
"""「配置编辑」按钮挪到「性能模式」标题行右侧 + 恢复下拉宽度 D(50)->D(72)"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
OPS = []
CACHE = {}


def load():
    if F not in CACHE:
        raw = io.open(F, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        if crlf:
            text = text.replace('\r\n', '\n')
        CACHE[F] = (text, bom, crlf)
    return CACHE[F]


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# 1) 恢复下拉宽度
op('\t\t\t\tWidth = D(50),\n', '\t\t\t\tWidth = D(72),\n', '恢复下拉宽度 50->72')

# 2) 从配置行移除按钮
op('\t\tperfConfigEditButton = new RButton\n'
   '\t\t{\n'
   '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
   '\t\t\tAutoSize = false,\n'
   '\t\t\tSize = new Size(D(64), D(22)),\n'
   '\t\t\tMargin = new Padding(0, D(1), D(4), 0),\n'
   '\t\t\tFlatStyle = FlatStyle.Flat,\n'
   '\t\t\tForeColor = UiVisualStyle.Text,\n'
   '\t\t\tBackColor = UiVisualStyle.SurfaceRaised\n'
   '\t\t};\n'
   '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
   '\t\tperfConfigEditButton.Click += async (object? _, EventArgs _) => await OpenPerfConfigEditorAsync();\n'
   '\t\tperfConfigRow.Controls.Add(perfConfigEditButton);\n',
   '', '从配置行移除按钮')

# 3) 放到「性能模式」标题行右侧（Dock=Right，索引在后 -> 先停靠，占右侧）
op('\t\tpanelCPUTitle.Controls.Clear();\n\t\tpanelCPUTitle.Controls.Add(value);\n',
   '\t\tpanelCPUTitle.Controls.Clear();\n'
   '\t\tpanelCPUTitle.Controls.Add(value);\n'
   '\t\t// 2026-10-04：30 系「配置编辑」按钮 —— 放在「性能模式」标题行右侧\n'
   '\t\t// （Dock=Right 且索引在 value 之后 -> 先停靠，占右侧；value 填充其余）\n'
   '\t\tperfConfigEditButton = new RButton\n'
   '\t\t{\n'
   '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
   '\t\t\tAutoSize = false,\n'
   '\t\t\tWidth = D(72),\n'
   '\t\t\tDock = DockStyle.Right,\n'
   '\t\t\tFlatStyle = FlatStyle.Flat,\n'
   '\t\t\tForeColor = UiVisualStyle.Text,\n'
   '\t\t\tBackColor = UiVisualStyle.SurfaceRaised,\n'
   '\t\t\tVisible = false\n'
   '\t\t};\n'
   '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
   '\t\tperfConfigEditButton.Click += async (object? _, EventArgs _) => await OpenPerfConfigEditorAsync();\n'
   '\t\tpanelCPUTitle.Controls.Add(perfConfigEditButton);\n',
   '标题行右侧加入按钮')

# 4) 可见性跟随判据
op('\t\tif (perfConfigRow != null)\n\t\t{\n\t\t\tperfConfigRow.Visible = flag;\n\t\t}\n',
   '\t\tif (perfConfigRow != null)\n\t\t{\n\t\t\tperfConfigRow.Visible = flag;\n\t\t}\n'
   '\t\tif (perfConfigEditButton != null)\n\t\t{\n\t\t\tperfConfigEditButton.Visible = flag;\n\t\t}\n',
   '按钮可见性跟随判据')


def main():
    if '放在「性能模式」标题行右侧' in load()[0]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, expect, label in OPS:
        n = load()[0].count(old)
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
    text, bom, crlf = load()
    for old, new, expect, label in OPS:
        text = text.replace(old, new, expect)
    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
