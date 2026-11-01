# -*- coding: utf-8 -*-
"""
二期（方案 B 旁路）第 1 半：读取路径
================================================================================
行尾（本补丁关键）：
  VendorModeDefaults.cs  CRLF
  CustomModeForm.cs      **LF**
  SettingsForm.cs        CRLF
故本脚本按文件分别用对应行尾的字节锚点，不做任何行尾归一化。
"""
import io
import os
import sys

F_VMD = r'D:\openLM\source\MechrevoLite.Mode\VendorModeDefaults.cs'
F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ================= 1) VendorModeDefaults.ComposeProfile (CRLF) =================
VMD_ANCHOR = b'\tinternal static PerfModeSettings Compose(PerfModeKind kind, SeedContext ctx, string? profileJson, string? fanTableJson, PlDefaultSet? ecDefaults)\r\n'
VMD_NEW = (b'\t/// <summary>\r\n'
           b'\t/// 2026-10-04: build PerfModeSettings for a *specific* firmware profile\r\n'
           b'\t/// (Mode{N}_Profile{index+1}.json) instead of the one MainOption.json binds.\r\n'
           b'\t/// Used by the 30-series "config edit" editor.\r\n'
           b'\t/// </summary>\r\n'
           b'\tinternal static PerfModeSettings ComposeProfile(PerfModeKind kind, MechrevoHw? hw, int profileIndex)\r\n'
           b'\t{\r\n'
           b'\t\tif (hw == null)\r\n'
           b'\t\t{\r\n'
           b'\t\t\treturn PerfModeSettings.Default;\r\n'
           b'\t\t}\r\n'
           b'\t\tSeedContext ctx = SeedContext.From(hw);\r\n'
           b'\t\tstring text = DataRoot();\r\n'
           b'\t\tstring profileJson = null;\r\n'
           b'\t\tstring fanTableJson = null;\r\n'
           b'\t\tif (text != null)\r\n'
           b'\t\t{\r\n'
           b'\t\t\tint num = Math.Clamp(profileIndex, 0, MaxProfileCount - 1);\r\n'
           b'\t\t\tprofileJson = TryRead(Path.Combine(text, "UserPofiles", $"Mode{VendorModeNumber(kind)}_Profile{num + 1}.json"));\r\n'
           b'\t\t\tfanTableJson = TryRead(Path.Combine(text, "UserFanTables", DefaultFanTableName(kind) + ".json"));\r\n'
           b'\t\t}\r\n'
           b'\t\treturn Compose(kind, ctx, profileJson, fanTableJson, null);\r\n'
           b'\t}\r\n'
           b'\r\n'
           + VMD_ANCHOR)
op(F_VMD, VMD_ANCHOR, VMD_NEW, 'ComposeProfile 新读取入口')

# ================= 2) CustomModeForm 字段 (LF) =================
CMF_ANCHOR = b'\tprivate RComboBox _modeCombo;\n'
CMF_NEW = (CMF_ANCHOR
           + b'\n'
           + b'\t// 2026-10-04: firmware-profile bypass (plain B). When on, the form displays a\r\n'
           + b'\t// 30-series firmware profile instead of an LM custom mode.\r\n'
           + b'\tprivate bool _firmwareMode;\r\n'
           + b'\n'
           + b'\tprivate int _firmwareVisualMode;\r\n'
           + b'\n'
           + b'\tprivate int _firmwareProfileIndex;\r\n')
op(F_CMF, CMF_ANCHOR, CMF_NEW, 'CustomModeForm 新增旁路字段')

# ================= 3) BindFirmwareProfile (LF) =================
CMF2_ANCHOR = b'\tprivate void SetOptionsEnabled()\n'
CMF2_NEW = (b'\t/// <summary>\r\n'
            b'\t/// 2026-10-04: open this editor against a 30-series firmware profile.\r\n'
            b'\t/// visualMode uses LM numbering (0=balanced/gaming, 1=turbo, 2=silent/office).\r\n'
            b'\t/// Read-only for now: commits are suppressed until the MQTT write path lands.\r\n'
            b'\t/// </summary>\r\n'
            b'\tinternal void BindFirmwareProfile(int visualMode, int profileIndex)\r\n'
            b'\t{\r\n'
            b'\t\t_firmwareMode = true;\r\n'
            b'\t\t_firmwareVisualMode = visualMode;\r\n'
            b'\t\t_firmwareProfileIndex = Math.Clamp(profileIndex, 0, VendorModeDefaults.MaxProfileCount - 1);\r\n'
            b'\t\tPerfModeKind kind = visualMode switch\r\n'
            b'\t\t{\r\n'
            b'\t\t\t2 => PerfModeKind.Silent,\r\n'
            b'\t\t\t1 => PerfModeKind.Turbo,\r\n'
            b'\t\t\t_ => PerfModeKind.Balanced,\r\n'
            b'\t\t};\r\n'
            b'\t\tbool syncingModes = _syncingModes;\r\n'
            b'\t\t_syncingModes = true;\r\n'
            b'\t\ttry\r\n'
            b'\t\t{\r\n'
            b'\t\t\tText = Strings.PerfEditorTitle + " \\u00b7 " + PerfModeText.Name(kind) + " \\u00b7 "\r\n'
            b'\t\t\t\t+ Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\r\n'
            b'\t\t\tLoadAppSide(VendorModeDefaults.ComposeProfile(kind, Program.hw, _firmwareProfileIndex));\r\n'
            b'\t\t}\r\n'
            b'\t\tfinally\r\n'
            b'\t\t{\r\n'
            b'\t\t\t_syncingModes = syncingModes;\r\n'
            b'\t\t}\r\n'
            b'\t\t_addButton.Enabled = false;\r\n'
            b'\t\t_renameButton.Enabled = false;\r\n'
            b'\t\t_deleteButton.Enabled = false;\r\n'
            b'\t\t_restoreButton.Enabled = false;\r\n'
            b'\t\t_fanCurveButton.Enabled = false;\r\n'
            b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1) + "\\uff08read-only\\uff09";\r\n'
            b'\t\t_status.ForeColor = UiVisualStyle.Muted;\r\n'
            b'\t\tSetOptionsEnabled();\r\n'
            b'\t\tif (IsHandleCreated)\r\n'
            b'\t\t{\r\n'
            b'\t\t\tResizeToContent();\r\n'
            b'\t\t}\r\n'
            b'\t}\r\n'
            b'\n'
            + CMF2_ANCHOR.replace(b'\r\n', b'\n'))
op(F_CMF, CMF2_ANCHOR, CMF2_NEW, 'BindFirmwareProfile')

# ================= 4) 禁止提交 (LF) =================
CMF3_ANCHOR = b'\tprivate async Task FlushPendingAsync()\n\t{\n'
CMF3_NEW = (b'\tprivate async Task FlushPendingAsync()\n'
            b'\t{\n'
            b'\t\t// 2026-10-04: firmware-profile bypass is read-only for now. Without this guard\r\n'
            b'\t\t// an edit would be committed through PerfModeStore into an LM custom mode.\r\n'
            b'\t\tif (_firmwareMode)\r\n'
            b'\t\t{\r\n'
            b'\t\t\treturn;\r\n'
            b'\t\t}\r\n')
op(F_CMF, CMF3_ANCHOR, CMF3_NEW, '固件模式禁止提交')

# ================= 5) SettingsForm 绑定 (CRLF，唯一锚点) =================
SF_ANCHOR = (b'\t\tif (customModeForm.Visible)\r\n'
             b'\t\t{\r\n'
             b'\t\t\tcustomModeForm.Hide();\r\n'
             b'\t\t\treturn;\r\n'
             b'\t\t}\r\n'
             b'\t\tcustomModeForm.Opacity = 0.01;\r\n')
SF_NEW = (b'\t\tif (customModeForm.Visible)\r\n'
          b'\t\t{\r\n'
          b'\t\t\tcustomModeForm.Hide();\r\n'
          b'\t\t\treturn;\r\n'
          b'\t\t}\r\n'
          b'\t\t// 2026-10-04: bind the editor to (current mode, selected firmware profile).\r\n'
          b'\t\tcustomModeForm.BindFirmwareProfile(visualMode, ModeProfileConfig.Get(visualMode));\r\n'
          b'\t\tcustomModeForm.Opacity = 0.01;\r\n')
op(F_SF, SF_ANCHOR, SF_NEW, 'SettingsForm 绑定固件配置')

# ================= 6) 日志行更新 (CRLF) =================
SF2_ANCHOR = b'\t\tLogger.WriteLine($"OpenPerfConfigEditor: visualMode={visualMode} count={ModeProfileConfig.Count} (data source binding pending)");\r\n'
SF2_NEW = (b'\t\tint profileIndex = ModeProfileConfig.Get(visualMode);\r\n'
           b'\t\tLogger.WriteLine($"OpenPerfConfigEditor: visualMode={visualMode} profile={profileIndex} count={ModeProfileConfig.Count}");\r\n')
op(F_SF, SF2_ANCHOR, SF2_NEW, '日志行更新')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_VMD, F_CMF, F_SF)}
    if b'BindFirmwareProfile' in data[F_CMF]:
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
    for path, blob in data.items():
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
