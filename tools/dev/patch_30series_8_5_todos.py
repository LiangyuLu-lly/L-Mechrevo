# -*- coding: utf-8 -*-
"""
8.5 待办 1 + 待办 2
================================================================================
待办 1：把「配置编辑」按钮挪到「风扇强冷」左边
  BuildHeadRow 的列是**倒序添加**的：visual 顺序 = statusControl3 -> statusControl2 -> statusControl。
  调用 `BuildHeadRow(picturePerf, labelPerf, labelCPUFan, _fanBoostBox, _gpuOcBox)` 对应：
      statusControl3=_gpuOcBox（显卡OC，仅 50 系可见） / statusControl2=_fanBoostBox（风扇强冷）
      statusControl=labelCPUFan（右侧模式名，如「平衡」）
  所以新增一个 extraControl 参数、插在 statusControl3 之后 -> 视觉上正好在
  「显卡OC」与「风扇强冷」之间（30 系显卡OC 恒不显示，不挤占）。

待办 2：修最小宽度下「狂暴」下拉被裁
  FlowLayoutPanel + AutoSize 会按内容撑宽 -> 不换行、横向溢出被裁。
  改为 3 等分 TableLayoutPanel（每列 Percent 33.33），每格内一个「标签 + 下拉」，
  下拉恢复 D(72) 也不会溢出；同时面板高度回到确定的 D(66)+D(28)。
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


# ============ 待办 1 ============
op('\tprivate FlowLayoutPanel? perfConfigRow;\n',
   '\tprivate TableLayoutPanel? perfConfigRow;\n', '字段类型 FlowLayoutPanel -> TableLayoutPanel')

op('\tprivate BufferedTableLayoutPanel BuildHeadRow(Control iconBox, Label nameLabel, Control? statusControl, '
   'Control? statusControl2 = null, Control? statusControl3 = null)\n',
   '\tprivate BufferedTableLayoutPanel BuildHeadRow(Control iconBox, Label nameLabel, Control? statusControl, '
   'Control? statusControl2 = null, Control? statusControl3 = null, Control? extraControl = null)\n',
   'BuildHeadRow 加 extraControl 参数')

op('\t\t\tColumnCount = ((iconBox == null) ? 2 : 3) + ((statusControl2 != null) ? 1 : 0) + '
   '((statusControl3 != null) ? 1 : 0),\n',
   '\t\t\tColumnCount = ((iconBox == null) ? 2 : 3) + ((statusControl2 != null) ? 1 : 0) + '
   '((statusControl3 != null) ? 1 : 0) + ((extraControl != null) ? 1 : 0),\n',
   'ColumnCount 计入 extraControl')

op('\t\t\tbufferedTableLayoutPanel.Controls.Add(statusControl3, num++, 0);\n\t\t}\n',
   '\t\t\tbufferedTableLayoutPanel.Controls.Add(statusControl3, num++, 0);\n'
   '\t\t}\n'
   '\t\t// 2026-10-04：额外按钮列 —— 视觉上落在 statusControl3 与 statusControl2 之间\n'
   '\t\t// （即「显卡OC」与「风扇强冷」之间；30 系显卡OC 恒不显示）\n'
   '\t\tif (extraControl != null)\n'
   '\t\t{\n'
   '\t\t\tbufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));\n'
   '\t\t\textraControl.Margin = new Padding(0, 0, D(4), 0);\n'
   '\t\t\textraControl.Dock = DockStyle.None;\n'
   '\t\t\textraControl.Anchor = AnchorStyles.None;\n'
   '\t\t\tbufferedTableLayoutPanel.Controls.Add(extraControl, num++, 0);\n'
   '\t\t}\n',
   '插入 extraControl 列')

op('\t\tBufferedTableLayoutPanel value = BuildHeadRow(picturePerf, labelPerf, labelCPUFan, _fanBoostBox, _gpuOcBox);\n',
   '\t\t// 2026-10-04：30 系「配置编辑」按钮 —— 先建好，再作为 extraControl 传入 BuildHeadRow，\n'
   '\t\t// 落在「显卡OC」与「风扇强冷」之间（仅 30 系可见）\n'
   '\t\tperfConfigEditButton = new RButton\n'
   '\t\t{\n'
   '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
   '\t\t\tAutoSize = false,\n'
   '\t\t\tWidth = D(72),\n'
   '\t\t\tFlatStyle = FlatStyle.Flat,\n'
   '\t\t\tForeColor = UiVisualStyle.Text,\n'
   '\t\t\tBackColor = UiVisualStyle.SurfaceRaised,\n'
   '\t\t\tVisible = false\n'
   '\t\t};\n'
   '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
   '\t\tperfConfigEditButton.Click += async (object? _, EventArgs _) => await OpenPerfConfigEditorAsync();\n'
   '\t\tBufferedTableLayoutPanel value = BuildHeadRow(picturePerf, labelPerf, labelCPUFan, _fanBoostBox, _gpuOcBox, perfConfigEditButton);\n',
   '按钮建在调用之前并传入')

op('\t\t// 2026-10-04：30 系「配置编辑」按钮 —— 放在「性能模式」标题行右侧\n'
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
   '', '移除旧的 Dock=Right 挂法')

# ============ 待办 2 ============
op('\t\tperfConfigRow = new FlowLayoutPanel\n'
   '\t\t{\n'
   '\t\t\tDock = DockStyle.Top,\n'
   '\t\t\tFlowDirection = FlowDirection.LeftToRight,\n'
   '\t\t\tWrapContents = true,\n'
   '\t\t\tAutoSize = true,\n'
   '\t\t\tAutoSizeMode = AutoSizeMode.GrowAndShrink,\n'
   '\t\t\tMargin = Padding.Empty,\n'
   '\t\t\tBackColor = UiVisualStyle.Surface\n'
   '\t\t};\n',
   '\t\t// 2026-10-04：改为 3 等分 TableLayoutPanel —— FlowLayoutPanel 会按内容撑宽，\n'
   '\t\t// 最小窗口宽度下不换行而是横向溢出被裁（「狂暴」下拉被切）\n'
   '\t\tTableLayoutPanel tableLayoutPanel = new TableLayoutPanel\n'
   '\t\t{\n'
   '\t\t\tDock = DockStyle.Top,\n'
   '\t\t\tColumnCount = 3,\n'
   '\t\t\tRowCount = 1,\n'
   '\t\t\tAutoSize = false,\n'
   '\t\t\tHeight = D(26),\n'
   '\t\t\tMargin = Padding.Empty,\n'
   '\t\t\tBackColor = UiVisualStyle.Surface\n'
   '\t\t};\n'
   '\t\tfor (int c = 0; c < 3; c++)\n'
   '\t\t{\n'
   '\t\t\ttableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));\n'
   '\t\t}\n'
   '\t\tperfConfigRow = tableLayoutPanel;\n',
   '配置行改为 3 等分 TableLayoutPanel')

op('\t\t\tperfConfigCombos[i] = rComboBox;\n'
   '\t\t\tperfConfigRow.Controls.Add(label);\n'
   '\t\t\tperfConfigRow.Controls.Add(rComboBox);\n'
   '\t\t}\n',
   '\t\t\tperfConfigCombos[i] = rComboBox;\n'
   '\t\t\t// 每模式一个等宽单元格（标签 + 下拉），下拉 D(72) 也不会溢出\n'
   '\t\t\tFlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel\n'
   '\t\t\t{\n'
   '\t\t\t\tDock = DockStyle.Fill,\n'
   '\t\t\t\tFlowDirection = FlowDirection.LeftToRight,\n'
   '\t\t\t\tWrapContents = false,\n'
   '\t\t\t\tAutoSize = false,\n'
   '\t\t\t\tMargin = Padding.Empty,\n'
   '\t\t\t\tBackColor = UiVisualStyle.Surface\n'
   '\t\t\t};\n'
   '\t\t\tflowLayoutPanel.Controls.Add(label);\n'
   '\t\t\tflowLayoutPanel.Controls.Add(rComboBox);\n'
   '\t\t\tperfConfigRow.Controls.Add(flowLayoutPanel, i, 0);\n'
   '\t\t}\n',
   '循环内改为按单元格添加')

op('\t\t\t// 行内可能换行，高度按实际 PreferredSize；拿不到时用预存兜底\n'
   '\t\t\tint num = ((perfConfigRow != null && perfConfigRow.PreferredSize.Height > 0) ? perfConfigRow.PreferredSize.Height : _perfRowExtraHeight);\n'
   '\t\t\tpanelPerformance.Height = (flag ? (_perfHeightCompact + num) : _perfHeightCompact);\n',
   '\t\t\t// 行高固定 D(26)（3 等分表格），用确定的预存高度\n'
   '\t\t\tpanelPerformance.Height = (flag ? _perfHeightWithConfig : _perfHeightCompact);\n',
   '面板高度回到确定值')


def main():
    if 'TableLayoutPanel? perfConfigRow;' in load()[0]:
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
