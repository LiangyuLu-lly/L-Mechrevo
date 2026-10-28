# -*- coding: utf-8 -*-
"""
30 系「模式 × 配置」—— UI 层：性能按钮下方横排三个配置下拉
================================================================================
用户需求（2026-10-04 原话）：「在性能控制按钮下面横排显示三个下拉列表，
分别是三个模式绑定的配置」。

实现：
  1. 资源新增 ModeConfigShort（zh 配置 / en Config）
  2. SettingsForm 新增字段 perfConfigRow / perfConfigCombos
  3. BuildDashboardLayout：在 panelPerformance.Height = D(66) 之后构建该行
     - FlowLayoutPanel(Dock=Top, Height=D(26))，内含 3 组「模式名 + 配置下拉」
     - 三个模式 = 办公(2) / 游戏(0) / 狂飙(1)，与 ModeProfileConfig 键同号
     - 下拉项 = 配置 1..ModeProfileConfig.Count；初值 = ModeProfileConfig.Get(mode)
     - 选中即 ModeProfileConfig.Set(mode, idx)；若该模式正是当前模式，顺带重新下发
     - SetChildIndex(row, 0) —— Dock=Top 按索引倒序停靠，索引 0 才落在最下方
  4. 新增 ApplyPerfConfigRowVisibility()：仅当 hw.SupportsModeProfileConfig 时显示，
     并把 panelPerformance 高度 +D(28)；在 RefreshDeviceCapabilities 里调用
"""
import io
import os
import sys

SRC = r'D:\openLM\source'
F_RESX_EN = os.path.join(SRC, 'MechrevoLite.Properties.Strings.resx')
F_RESX_ZH = os.path.join(SRC, 'MechrevoLite.Properties.Strings.zh-CN.resx')
F_STRINGS = os.path.join(SRC, 'MechrevoLite.Properties', 'Strings.cs')
F_SF = os.path.join(SRC, 'MechrevoLite', 'SettingsForm.cs')

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


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---------------- 1. 资源 ----------------
op(F_RESX_ZH, '</root>',
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>配置</value>\n  </data>\n</root>',
   'zh 新增 ModeConfigShort')
op(F_RESX_EN, '</root>',
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>Config</value>\n  </data>\n</root>',
   'en 新增 ModeConfigShort')
op(F_STRINGS,
   '\tinternal static string ChargeTaskLocationFailed => ResourceManager.GetString("ChargeTaskLocationFailed", resourceCulture);\n',
   '\tinternal static string ChargeTaskLocationFailed => ResourceManager.GetString("ChargeTaskLocationFailed", resourceCulture);\n'
   '\n'
   '\tinternal static string ModeConfigShort => ResourceManager.GetString("ModeConfigShort", resourceCulture);\n',
   'Strings.cs 新增 ModeConfigShort 访问器')

# ---------------- 2. 字段 ----------------
op(F_SF,
   '\tprivate Panel panelPerformance;\n',
   '\tprivate Panel panelPerformance;\n'
   '\n'
   '\tprivate FlowLayoutPanel? perfConfigRow;\n'
   '\n'
   '\tprivate RComboBox[]? perfConfigCombos;\n',
   'SettingsForm 新增 perfConfigRow / perfConfigCombos 字段')

# ---------------- 3. 构建 ----------------
BUILD = '''\t\tpanelPerformance.Height = D(66);
\t\t// 2026-10-04：30 系「模式 × 配置」—— 性能按钮下方横排三个配置下拉
\t\t// 三个模式 = 办公(2) / 游戏(0) / 狂飙(1)，与 ModeProfileConfig 的键同号
\t\tBuildPerfConfigRow();
'''
op(F_SF, '\t\tpanelPerformance.Height = D(66);\n', BUILD, 'BuildDashboardLayout 调用构建')

BUILD_METHOD = '''\t/// <summary>
\t/// 构建「模式 × 配置」行：性能按钮下方横排三个下拉，分别绑定办公/游戏/狂飙的固件配置号。
\t/// 仅 30 系显示（见 ApplyPerfConfigRowVisibility / MechrevoHw.SupportsModeProfileConfig）。
\t/// </summary>
\tprivate void BuildPerfConfigRow()
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
\t\t\t\t\t\t// （模式 1=狂飙 需带 silentTurbo：0 与 2 传 null，沿用模式按钮的约定）
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
\t\tpanelPerformance.Controls.SetChildIndex(perfConfigRow, 0);
\t\tperfConfigRow.Visible = false;
\t}

\t/// <summary>
\t/// 按机型显示/隐藏「模式 × 配置」行，并相应调整性能面板高度（紧凑布局原本固定 D(66)）。
\t/// </summary>
\tprivate void ApplyPerfConfigRowVisibility()
\t{
\t\tbool flag = Program.hw?.SupportsModeProfileConfig ?? false;
\t\tif (perfConfigRow != null)
\t\t{
\t\t\tperfConfigRow.Visible = flag;
\t\t}
\t\tif (panelPerformance != null)
\t\t{
\t\t\tpanelPerformance.Height = D(66) + (flag ? D(28) : 0);
\t\t}
\t}

'''
op(F_SF, '\tprivate void ReflowPerformanceButtons(bool silentTurbo, bool turbo, bool custom)\n',
   BUILD_METHOD + '\tprivate void ReflowPerformanceButtons(bool silentTurbo, bool turbo, bool custom)\n',
   '新增 BuildPerfConfigRow / ApplyPerfConfigRowVisibility')

# ---------------- 4. 刷新可见性 ----------------
op(F_SF,
   '\t\tReflowPerformanceButtons(flag16, flag15, flag17);\n',
   '\t\tReflowPerformanceButtons(flag16, flag15, flag17);\n\t\tApplyPerfConfigRowVisibility();\n',
   'RefreshDeviceCapabilities 调用可见性刷新')


def main():
    for path, _, _, _, _ in OPS:
        load(path)
    if 'ApplyPerfConfigRowVisibility' in load(F_SF)[0]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        text = load(path)[0]
        n = text.count(old)
        if n != expect:
            errors.append('%-24s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘任何文件 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        text, bom, crlf = load(path)
        CACHE[path] = (text.replace(old, new, expect), bom, crlf)
    for path, (text, bom, crlf) in CACHE.items():
        out = text.replace('\n', '\r\n') if crlf else text
        data = out.encode('utf-8')
        if bom:
            data = b'\xef\xbb\xbf' + data
        io.open(path, 'wb').write(data)
        print('  写入 ' + path)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
