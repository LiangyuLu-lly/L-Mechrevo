# -*- coding: utf-8 -*-
"""
修正：最小宽度下「配置编辑」按钮被裁掉
- 收窄：配置下拉 D(72)->D(50)、按钮 D(84)->D(64)
- 允许换行：WrapContents false->true，AutoSize=true（放不下时换行而非裁掉）
- 面板高度按行实际 PreferredSize 计算（拿不到时用预存的 D(28) 兜底）
"""
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


# 1) 行容器：允许换行 + 高度自适应
op('\t\t\tWrapContents = false,\n\t\t\tAutoSize = false,\n\t\t\tHeight = D(26),\n',
   '\t\t\tWrapContents = true,\n\t\t\tAutoSize = true,\n\t\t\tAutoSizeMode = AutoSizeMode.GrowAndShrink,\n',
   '配置行改为可换行 + 自适应高度')

# 2) 下拉收窄
op('\t\t\t\tWidth = D(72),\n', '\t\t\t\tWidth = D(50),\n', '下拉宽度 72->50')

# 3) 按钮收窄
op('\t\t\tSize = new Size(D(84), D(22)),\n', '\t\t\tSize = new Size(D(64), D(22)),\n', '按钮宽度 84->64')

# 4) 兜底高度字段
op('\tprivate int _perfHeightWithConfig;\n',
   '\tprivate int _perfHeightWithConfig;\n'
   '\n'
   '\tprivate int _perfRowExtraHeight;\n',
   '新增 _perfRowExtraHeight 字段')

op('\tprivate void BuildPerfConfigRow(Func<int, int> D)\n\t{\n',
   '\tprivate void BuildPerfConfigRow(Func<int, int> D)\n\t{\n'
   '\t\t_perfRowExtraHeight = D(28);\n',
   'BuildPerfConfigRow 记录兜底高度')

# 5) 高度按实际 PreferredSize
op('\t\tif (panelPerformance != null && _perfHeightCompact > 0)\n'
   '\t\t{\n'
   '\t\t\tpanelPerformance.Height = (flag ? _perfHeightWithConfig : _perfHeightCompact);\n'
   '\t\t}\n',
   '\t\tif (panelPerformance != null && _perfHeightCompact > 0)\n'
   '\t\t{\n'
   '\t\t\t// 行内可能换行，高度按实际 PreferredSize；拿不到时用预存兜底\n'
   '\t\t\tint num = ((perfConfigRow != null && perfConfigRow.PreferredSize.Height > 0) ? perfConfigRow.PreferredSize.Height : _perfRowExtraHeight);\n'
   '\t\t\tpanelPerformance.Height = (flag ? (_perfHeightCompact + num) : _perfHeightCompact);\n'
   '\t\t}\n',
   '面板高度按行实际高度')


def main():
    if 'WrapContents = true,\n\t\t\tAutoSize = true,' in load()[0]:
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
