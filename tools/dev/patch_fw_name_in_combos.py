# -*- coding: utf-8 -*-
"""
修用户反馈 2 项：
1. 改名后，设置行的三个下拉应显示自定义名字（「配置 N · 名字」）
2. 切「安静」时编辑窗口不刷新 —— 上一版修法无效

问题 2 根因（两个叠加）：
  a. `BindFirmwareProfile` 从未设置 `_modeId` -> `OnPerfActiveChanged` 里
     `displayedModeId != _modeId` 的判断常常不成立 -> `BindMode` 压根没被调用
  b. 我上一版在守卫里用 `Program.service?.CurrentMode` 取模式，**时机太早取不到**
     -> `num == _firmwareVisualMode` -> 不刷新
  修法：
  a. `BindFirmwareProfile` 里 `_modeId = PerfModeDefinition.BuiltInId(kind);`
  b. 守卫里改用**触发回调的 id** 反推视觉模式号（silent->2 / balanced->0 / 其余->1）

问题 1 实现：
  * `BuildPerfConfigRow` 建下拉项时附带该配置的 `CustomizeName`
  * 新增 `RefreshPerfConfigComboLabels()`（编辑窗口关闭时刷新，避免重开 LM）
    以及 `_refreshingConfigLabels` 守卫，防止重建项时误触发 `SelectedIndexChanged` 而重发模式
行尾：CustomModeForm.cs / SettingsForm.cs 都是……CustomModeForm=LF，SettingsForm=CRLF
"""
import io
import os
import sys

F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ================= CustomModeForm =================
# a) 设置 _modeId
op(F_CMF, b'\t\t_firmwareMode = true;\n\t\t_firmwareVisualMode = visualMode;\n',
   b'\t\t_firmwareMode = true;\n'
   b'\t\t_firmwareVisualMode = visualMode;\n'
   b'\t\t// 2026-10-05: track the built-in id too -- OnPerfActiveChanged only calls\r\n'
   b'\t\t// BindMode() when the displayed id differs from _modeId, so leaving _modeId\r\n'
   b'\t\t// stale meant mode switches never reached us.\r\n'
   b'\t\t_modeId = PerfModeDefinition.BuiltInId((visualMode == 2) ? PerfModeKind.Silent : ((visualMode == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));\n',
   'BindFirmwareProfile 设置 _modeId')

# b) 守卫改用 id 反推
op(F_CMF,
   b'\t\t\t// 2026-10-05: while the editor is open, follow the newly active mode instead\n'
   b'\t\t\t// of ignoring the event (the user reported a stale display).\n'
   b'\t\t\tint num = Program.service?.CurrentMode ?? _firmwareVisualMode;\n',
   b'\t\t\t// 2026-10-05: follow the newly active mode while the editor is open.\n'
   b'\t\t\t// Derive the visual mode from the id that triggered this call -- reading\n'
   b'\t\t\t// Program.service.CurrentMode here happened too early and nothing refreshed.\n'
   b'\t\t\tint num = (string.Equals(id, PerfModeDefinition.BuiltInId(PerfModeKind.Silent), StringComparison.Ordinal) ? 2 : (string.Equals(id, PerfModeDefinition.BuiltInId(PerfModeKind.Balanced), StringComparison.Ordinal) ? 0 : 1));\n',
   '守卫改用 id 反推视觉模式号')

# ================= SettingsForm =================
# c) 字段
op(F_SF, b'\tprivate int _perfRowExtraHeight;\r\n',
   b'\tprivate int _perfRowExtraHeight;\r\n'
   b'\r\n'
   b'\t// 2026-10-05: guards RefreshPerfConfigComboLabels() from re-sending the mode\r\n'
   b'\tprivate bool _refreshingConfigLabels;\r\n',
   '新增 _refreshingConfigLabels')

# d) 下拉项带上自定义名字
op(F_SF,
   b'\t\t\tint num = Math.Max(1, ModeProfileConfig.Count);\r\n'
   b'\t\t\tfor (int j = 1; j <= num; j++)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\trComboBox.Items.Add(Strings.ModeConfigShort + " " + j);\r\n'
   b'\t\t\t}\r\n',
   b'\t\t\tint num = Math.Max(1, ModeProfileConfig.Count);\r\n'
   b'\t\t\t// 2026-10-05: show the profile\'s user-facing name (CustomizeName) when set.\r\n'
   b'\t\t\tPerfModeKind perfModeKind = ((modeValue == 2) ? PerfModeKind.Silent : ((modeValue == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));\r\n'
   b'\t\t\tfor (int j = 1; j <= num; j++)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\trComboBox.Items.Add(PerfConfigComboText(perfModeKind, j - 1));\r\n'
   b'\t\t\t}\r\n',
   '下拉项带上自定义名字')

# e) 选中时不要因刷新而重发
op(F_SF, b'\t\t\t\tint selectedIndex = rComboBox.SelectedIndex;\r\n'
          b'\t\t\t\tif (selectedIndex >= 0)\r\n',
   b'\t\t\t\tint selectedIndex = rComboBox.SelectedIndex;\r\n'
   b'\t\t\t\tif (selectedIndex >= 0 && !_refreshingConfigLabels)\r\n',
   '刷新时不重发模式')

# f) 新增 PerfConfigComboText / RefreshPerfConfigComboLabels
op(F_SF, b'\tprivate void BuildPerfConfigRow(Func<int, int> D)\r\n',
   b'\t/// <summary>2026-10-05: "\\u914d\\u7f6e N" plus the profile\'s CustomizeName when it has one.</summary>\r\n'
   b'\tprivate static string PerfConfigComboText(PerfModeKind kind, int profileIndex)\r\n'
   b'\t{\r\n'
   b'\t\tstring text = Strings.ModeConfigShort + " " + (profileIndex + 1);\r\n'
   b'\t\tstring text2 = VendorModeDefaults.ReadProfileCustomizeName(kind, profileIndex);\r\n'
   b'\t\tif (!string.IsNullOrWhiteSpace(text2))\r\n'
   b'\t\t{\r\n'
   b'\t\t\ttext = text + " \\u00b7 " + text2;\r\n'
   b'\t\t}\r\n'
   b'\t\treturn text;\r\n'
   b'\t}\r\n'
   b'\r\n'
   b'\t/// <summary>2026-10-05: re-label the three profile combos (e.g. after a rename).</summary>\r\n'
   b'\tprivate void RefreshPerfConfigComboLabels()\r\n'
   b'\t{\r\n'
   b'\t\tif (perfConfigCombos == null)\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\r\n'
   b'\t\tint[] array = new int[3] { 2, 0, 1 };\r\n'
   b'\t\t_refreshingConfigLabels = true;\r\n'
   b'\t\ttry\r\n'
   b'\t\t{\r\n'
   b'\t\t\tfor (int i = 0; i < perfConfigCombos.Length && i < 3; i++)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tRComboBox rComboBox = perfConfigCombos[i];\r\n'
   b'\t\t\t\tif (rComboBox == null)\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\tcontinue;\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t\tint num = Math.Max(1, ModeProfileConfig.Count);\r\n'
   b'\t\t\t\tPerfModeKind perfModeKind = ((array[i] == 2) ? PerfModeKind.Silent : ((array[i] == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));\r\n'
   b'\t\t\t\tint selectedIndex = rComboBox.SelectedIndex;\r\n'
   b'\t\t\t\trComboBox.BeginUpdate();\r\n'
   b'\t\t\t\trComboBox.Items.Clear();\r\n'
   b'\t\t\t\tfor (int j = 1; j <= num; j++)\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\trComboBox.Items.Add(PerfConfigComboText(perfModeKind, j - 1));\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t\trComboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, num - 1);\r\n'
   b'\t\t\t\trComboBox.EndUpdate();\r\n'
   b'\t\t\t}\r\n'
   b'\t\t}\r\n'
   b'\t\tfinally\r\n'
   b'\t\t{\r\n'
   b'\t\t\t_refreshingConfigLabels = false;\r\n'
   b'\t\t}\r\n'
   b'\t}\r\n'
   b'\r\n'
   b'\tprivate void BuildPerfConfigRow(Func<int, int> D)\r\n',
   '新增 PerfConfigComboText / RefreshPerfConfigComboLabels')

# g) 关闭编辑窗口时刷新下拉标签
op(F_SF, b'\t\tcustomModeForm.Hide();\r\n\t\t\treturn;\r\n',
   b'\t\tcustomModeForm.Hide();\r\n'
   b'\t\t\t// 2026-10-05: the editor may have renamed a profile -- re-label the combos.\r\n'
   b'\t\t\tRefreshPerfConfigComboLabels();\r\n'
   b'\t\t\treturn;\r\n',
   '关闭窗口时刷新下拉标签')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_CMF, F_SF)}
    if b'RefreshPerfConfigComboLabels' in data[F_SF]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = data[path].count(old)
        if n != expect:
            errors.append('%-22s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        data[path] = data[path].replace(old, new, expect)
    data[F_CMF] = data[F_CMF].replace(b'\r\n', b'\n')
    for path, blob in data.items():
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
