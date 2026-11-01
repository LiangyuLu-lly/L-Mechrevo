# -*- coding: utf-8 -*-
"""
30 系「模式 × 配置」UI 层（修正版，只改 SettingsForm.cs）
上一版失败原因：D(int) 是 BuildDashboardLayout 内的局部函数，新方法里访问不到。
修正：沿用项目自身惯例（BuildUnsupportedModelRow(Func<int,int> D)）把 D 作为参数传入；
      可见性方法用的高度在建行时预先算好存字段，不再依赖 D。
"""
import io
import os
import sys

F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []
CACHE = {}


def load(path):
    if path not in CACHE:
        raw = io.open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        if crlf:
            text = text.replace('\r\n', '\n')
        CACHE[path] = (text, bom, crlf)
    return CACHE[path]


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


op('\tprivate Panel panelPerformance;\n',
   '\tprivate Panel panelPerformance;\n'
   '\n'
   '\tprivate FlowLayoutPanel? perfConfigRow;\n'
   '\n'
   '\tprivate RComboBox[]? perfConfigCombos;\n'
   '\n'
   '\tprivate int _perfHeightCompact;\n'
   '\n'
   '\tprivate int _perfHeightWithConfig;\n',
   '新增 4 个字段')

op('\t\tpanelPerformance.Height = D(66);\n',
   '\t\tpanelPerformance.Height = D(66);\n'
   '\t\t_perfHeightCompact = D(66);\n'
   '\t\t_perfHeightWithConfig = D(66) + D(28);\n'
   '\t\t// 2026-10-04：30 系「模式 × 配置」—— 性能按钮下方横排三个配置下拉\n'
   '\t\tBuildPerfConfigRow(D);\n',
   'BuildDashboardLayout 构建调用（传 D）')

METHODS = '''\t/// <summary>
\t/// 构建「模式 × 配置」行：性能按钮下方横排三个下拉，分别绑定办公/游戏/狂飙的固件配置号。
\t/// 三个模式号（2=办公 / 0=游戏 / 1=狂飙）与 ModeProfileConfig 的键、SwitchPerformanceModeAsync 参数同号。
\t/// D 由调用方传入（DPI 缩放是 BuildDashboardLayout 的局部函数，项目惯例用 Func&lt;int,int&gt; 传递）。
\t/// 仅 30 系显示，见 ApplyPerfConfigRowVisibility / MechrevoHw.SupportsModeProfileConfig。
\t/// </summary>
\tprivate void BuildPerfConfigRow(Func<int, int> D)
\t{
\t\tperfConfigRow = new FlowLayoutPanel
\t\t{
\t\t\tDock = DockStyle.Top,
\t\t\tFlowDirection = FlowDirection.LeftToRight,
\t\t\tWrapContents = false,
\t\t\tAutoSize = false,
\t\t\tHeight = D(26),
\t\t\tMargin = Padding.Empty,
\t\t\tBackColor = UiVisualStyle.Surface
\t\t};
\t\tint[] array = new int[3] { 2, 0, 1 };
\t\tstring[] array2 = new string[3] { Strings.Silent, Strings.Balanced, Strings.Turbo };
\t\tperfConfigCombos = new RComboBox[3];
\t\tfor (int i = 0; i < 3; i++)
\t\t{
\t\t\tint modeValue = array[i];
\t\t\tLabel label = new Label
\t\t\t{
\t\t\t\tText = array2[i],
\t\t\t\tAutoSize = true,
\t\t\t\tForeColor = UiVisualStyle.Muted,
\t\t\t\tMargin = new Padding(D(6), D(5), D(2), 0)
\t\t\t};
\t\t\tRComboBox rComboBox = new RComboBox
\t\t\t{
\t\t\t\tDropDownStyle = ComboBoxStyle.DropDownList,
\t\t\t\tWidth = D(72),
\t\t\t\tMargin = new Padding(0, D(2), D(8), 0)
\t\t\t};
\t\t\tint num = Math.Max(1, ModeProfileConfig.Count);
\t\t\tfor (int j = 1; j <= num; j++)
\t\t\t{
\t\t\t\trComboBox.Items.Add(Strings.ModeConfigShort + " " + j);
\t\t\t}
\t\t\trComboBox.SelectedIndex = Math.Clamp(ModeProfileConfig.Get(modeValue), 0, num - 1);
\t\t\trComboBox.SelectedIndexChanged += async (object? _, EventArgs _) =>
\t\t\t{
\t\t\t\tint selectedIndex = rComboBox.SelectedIndex;
\t\t\t\tif (selectedIndex >= 0)
\t\t\t\t{
\t\t\t\t\tModeProfileConfig.Set(modeValue, selectedIndex);
\t\t\t\t\tif (Program.service != null && Program.service.CurrentMode == modeValue)
\t\t\t\t\t{
\t\t\t\t\t\t// 该模式正是当前模式：立即按新配置号重新下发
\t\t\t\t\t\t// （模式 1=狂飙 需带 silentTurbo，0 与 2 传 null —— 沿用模式按钮的约定）
\t\t\t\t\t\tbool? silentTurbo = ((modeValue == 1) ? (bool?)MechrevoService.IsSilentTurboActive : ((bool?)null));
\t\t\t\t\t\tawait SwitchPerformanceModeAsync(modeValue, silentTurbo);
\t\t\t\t\t}
\t\t\t\t}
\t\t\t};
\t\t\tperfConfigCombos[i] = rComboBox;
\t\t\tperfConfigRow.Controls.Add(label);
\t\t\tperfConfigRow.Controls.Add(rComboBox);
\t\t}
\t\tpanelPerformance.Controls.Add(perfConfigRow);
\t\t// Dock=Top 按 Controls 索引倒序停靠 —— 索引 0 才落在最下方（tablePerf 之后）
\t\tpanelPerformance.Controls.SetChildIndex(perfConfigRow, 0);
\t\tperfConfigRow.Visible = false;
\t}

\t/// <summary>
\t/// 按机型显示/隐藏「模式 × 配置」行，并相应调整性能面板高度
\t/// （紧凑布局原本固定 D(66)，加一行需 +D(28)）。高度在建行时已算好存字段。
\t/// </summary>
\tprivate void ApplyPerfConfigRowVisibility()
\t{
\t\tbool flag = Program.hw?.SupportsModeProfileConfig ?? false;
\t\tif (perfConfigRow != null)
\t\t{
\t\t\tperfConfigRow.Visible = flag;
\t\t}
\t\tif (panelPerformance != null && _perfHeightCompact > 0)
\t\t{
\t\t\tpanelPerformance.Height = (flag ? _perfHeightWithConfig : _perfHeightCompact);
\t\t}
\t}

'''
op('\tprivate void ReflowPerformanceButtons(bool silentTurbo, bool turbo, bool custom)\n',
   METHODS + '\tprivate void ReflowPerformanceButtons(bool silentTurbo, bool turbo, bool custom)\n',
   '新增 BuildPerfConfigRow / ApplyPerfConfigRowVisibility')

op('\t\tReflowPerformanceButtons(flag16, flag15, flag17);\n',
   '\t\tReflowPerformanceButtons(flag16, flag15, flag17);\n\t\tApplyPerfConfigRowVisibility();\n',
   'RefreshDeviceCapabilities 调用可见性刷新')


def main():
    text0 = load(F_SF)[0]
    if 'ApplyPerfConfigRowVisibility' in text0:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, expect, label in OPS:
        n = load(F_SF)[0].count(old)
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
    text, bom, crlf = load(F_SF)
    for old, new, expect, label in OPS:
        text = text.replace(old, new, expect)
    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(F_SF, 'wb').write(data)
    print('  写入 ' + F_SF)
    return 0


if __name__ == '__main__':
    sys.exit(main())
