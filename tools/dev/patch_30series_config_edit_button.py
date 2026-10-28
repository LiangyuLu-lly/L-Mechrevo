# -*- coding: utf-8 -*-
"""
二期第 1 步：「配置编辑」按钮（用户要求先做出来）
- 位置：性能模式按钮下方的配置行末尾（三个下拉之后）
- 文字：Strings.ModeConfigShort + Strings.Edit = 「配置编辑」（复用现成字符串，不动 resx）
- 点击：复用自定义模式编辑界面（CustomModeForm）
- ⚠️ 一期数据源**尚未绑定到 30 系固件配置**，仍是 LM 自定义档 —— 日志会写明，暂勿在表单内保存
- 仅 30 系显示（随 perfConfigRow 一起显示/隐藏）
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


# 1) 字段
op('\tprivate RComboBox[]? perfConfigCombos;\n',
   '\tprivate RComboBox[]? perfConfigCombos;\n'
   '\n'
   '\tprivate RButton? perfConfigEditButton;\n',
   '新增 perfConfigEditButton 字段')

# 2) 在配置行末尾加按钮
op('\t\tpanelPerformance.Controls.Add(perfConfigRow);\n',
   '\t\tperfConfigEditButton = new RButton\n'
   '\t\t{\n'
   '\t\t\tText = Strings.ModeConfigShort + Strings.Edit,\n'
   '\t\t\tAutoSize = false,\n'
   '\t\t\tSize = new Size(D(84), D(22)),\n'
   '\t\t\tMargin = new Padding(0, D(1), D(4), 0),\n'
   '\t\t\tFlatStyle = FlatStyle.Flat,\n'
   '\t\t\tForeColor = UiVisualStyle.Text,\n'
   '\t\t\tBackColor = UiVisualStyle.SurfaceRaised\n'
   '\t\t};\n'
   '\t\tperfConfigEditButton.FlatAppearance.BorderColor = UiVisualStyle.Border;\n'
   '\t\tperfConfigEditButton.Click += async (object? _, EventArgs _) => await OpenPerfConfigEditorAsync();\n'
   '\t\tperfConfigRow.Controls.Add(perfConfigEditButton);\n'
   '\t\tpanelPerformance.Controls.Add(perfConfigRow);\n',
   '配置行末尾新增「配置编辑」按钮')

# 3) 处理器
HANDLER = '''\t/// <summary>
\t/// 「配置编辑」：复用自定义模式编辑界面。
\t/// ⚠️ 2026-10-04 第一步只把按钮与界面接起来，**数据源尚未绑定到 30 系固件配置**
\t///    （当前仍是 LM 自定义档），因此暂不要在表单内保存 —— 接上固件配置后移除本提示。
\t/// </summary>
\tprivate async Task OpenPerfConfigEditorAsync()
\t{
\t\tint visualMode = Program.service?.CurrentMode ?? 0;
\t\tLogger.WriteLine($"OpenPerfConfigEditor: visualMode={visualMode} count={ModeProfileConfig.Count} (data source binding pending)");
\t\tif (customModeForm == null || customModeForm.IsDisposed)
\t\t{
\t\t\tcustomModeForm = new CustomModeForm();
\t\t\tAddOwnedForm(customModeForm);
\t\t}
\t\tif (customModeForm.Visible)
\t\t{
\t\t\tcustomModeForm.Hide();
\t\t\treturn;
\t\t}
\t\tcustomModeForm.Opacity = 0.01;
\t\tResponsiveLayout.PlaceAdjacent(customModeForm, this);
\t\ttry
\t\t{
\t\t\tcustomModeForm.Show();
\t\t\tcustomModeForm.Update();
\t\t}
\t\tfinally
\t\t{
\t\t\tif (!customModeForm.IsDisposed)
\t\t\t{
\t\t\t\tcustomModeForm.Opacity = 1.0;
\t\t\t}
\t\t}
\t}

'''
op('\tprivate void BuildPerfConfigRow(Func<int, int> D)\n',
   HANDLER + '\tprivate void BuildPerfConfigRow(Func<int, int> D)\n',
   '新增 OpenPerfConfigEditorAsync')


def main():
    if 'OpenPerfConfigEditorAsync' in load()[0]:
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
