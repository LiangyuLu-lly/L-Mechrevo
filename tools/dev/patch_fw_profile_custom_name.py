# -*- coding: utf-8 -*-
"""
9.3：给每个固件配置自定义名称（对齐官方控制台）
================================================================================
依据：
  * 官方 `Mode{N}_Profile{M}.json` 内有 `"CustomizeName"`（用户可见名）+ `"Name"`（文件式名）
  * LM 已有 `MechrevoService.SetCustomProfileName(name)` :1567 ->
    `Fan/Control {"Action":"SET_CUSTOM_PROFILE_OSD_STRING","ProfileName":name}`
    —— 与参数一样：**写的是当前生效配置**，所以必须先选中目标
  * 表单已有完整重命名 UI：`_renameButton` -> `BeginRename()` -> `_nameBox` -> `SaveNameAsync()`
做法（复用重命名流程，只在固件模式下改走另一条写路径）：
  1. `VendorModeDefaults.ReadProfileCustomizeName(kind, profileIndex)` —— 读指定配置的名字
  2. CustomModeForm 新增 `_firmwareProfileName`、`UpdateFirmwareTitle()`、`SaveFirmwareProfileNameAsync()`
  3. `BindFirmwareProfile` 读名字、标题带上名字、**启用「重命名」**（原先被我禁用）
  4. `BeginRename` / `SaveNameAsync` 各加固件模式分支
行尾：VendorModeDefaults.cs 与 SettingsForm.cs 是 CRLF；CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F_VMD = r'D:\openLM\source\MechrevoLite.Mode\VendorModeDefaults.cs'
F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ============ 1) VendorModeDefaults.ReadProfileCustomizeName (CRLF) ============
op(F_VMD,
   b'\tinternal static PerfModeSettings ComposeProfile(PerfModeKind kind, MechrevoHw? hw, int profileIndex)\r\n',
   b'\t/// <summary>\r\n'
   b'\t/// 2026-10-05: user-facing name of a specific firmware profile, i.e. the\r\n'
   b'\t/// "CustomizeName" field the official console edits (falls back to null).\r\n'
   b'\t/// </summary>\r\n'
   b'\tinternal static string? ReadProfileCustomizeName(PerfModeKind kind, int profileIndex)\r\n'
   b'\t{\r\n'
   b'\t\tstring text = DataRoot();\r\n'
   b'\t\tif (text == null)\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn null;\r\n'
   b'\t\t}\r\n'
   b'\t\tstring text2 = TryRead(Path.Combine(text, "UserPofiles", $"Mode{VendorModeNumber(kind)}_Profile{Math.Clamp(profileIndex, 0, MaxProfileCount - 1) + 1}.json"));\r\n'
   b'\t\tif (text2 == null)\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn null;\r\n'
   b'\t\t}\r\n'
   b'\t\ttry\r\n'
   b'\t\t{\r\n'
   b'\t\t\tusing JsonDocument jsonDocument = JsonDocument.Parse(text2);\r\n'
   b'\t\t\tif (jsonDocument.RootElement.TryGetProperty("CustomizeName", out JsonElement value) && value.ValueKind == JsonValueKind.String)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tstring text3 = value.GetString();\r\n'
   b'\t\t\t\tif (!string.IsNullOrWhiteSpace(text3))\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\treturn text3;\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t}\r\n'
   b'\t\t}\r\n'
   b'\t\tcatch (JsonException)\r\n'
   b'\t\t{\r\n'
   b'\t\t}\r\n'
   b'\t\treturn null;\r\n'
   b'\t}\r\n'
   b'\r\n'
   b'\tinternal static PerfModeSettings ComposeProfile(PerfModeKind kind, MechrevoHw? hw, int profileIndex)\r\n',
   'ReadProfileCustomizeName')

# ============ 2) 字段 ============
op(F_CMF, b'\tprivate int _firmwareProfileIndex;\n',
   b'\tprivate int _firmwareProfileIndex;\n\n\tprivate string? _firmwareProfileName;\n',
   '新增 _firmwareProfileName')

# ============ 3) BindFirmwareProfile：读名字 + 标题 + 启用重命名 ============
op(F_CMF,
   b'\t\t\t// firmware mode has no PerfModeDefinition, so use the plain mode labels\n'
   b'\t\t\tstring modeLabel = ((visualMode == 2) ? Strings.Silent : ((visualMode == 1) ? Strings.Turbo : Strings.Balanced));\n'
   b'\t\t\tText = Strings.PerfEditorTitle + " \\u00b7 " + modeLabel + " \\u00b7 "\n'
   b'\t\t\t\t+ Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n',
   b'\t\t\t// 2026-10-05: also read the profile\'s user-facing name (CustomizeName).\n'
   b'\t\t\t_firmwareProfileName = VendorModeDefaults.ReadProfileCustomizeName(kind, _firmwareProfileIndex);\n'
   b'\t\t\tUpdateFirmwareTitle();\n',
   'BindFirmwareProfile 读名字并更新标题')

# ============ 4) 解除「重命名」禁用（两处） ============
op(F_CMF, b'\t\t_renameButton.Enabled = false;\n', b'', '解除重命名禁用', expect=2)

# ============ 5) 新增 UpdateFirmwareTitle / SaveFirmwareProfileNameAsync ============
op(F_CMF, b'\tprivate void BeginRename()\n\t{\n',
   b'\t/// <summary>2026-10-05: title for the firmware-profile editor, including the custom name.</summary>\n'
   b'\tprivate void UpdateFirmwareTitle()\n'
   b'\t{\n'
   b'\t\tstring text = ((_firmwareVisualMode == 2) ? Strings.Silent : ((_firmwareVisualMode == 1) ? Strings.Turbo : Strings.Balanced));\n'
   b'\t\tstring text2 = Strings.PerfEditorTitle + " \\u00b7 " + text + " \\u00b7 " + Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n'
   b'\t\tif (!string.IsNullOrWhiteSpace(_firmwareProfileName))\n'
   b'\t\t{\n'
   b'\t\t\ttext2 = text2 + " \\u00b7 " + _firmwareProfileName;\n'
   b'\t\t}\n'
   b'\t\tText = text2;\n'
   b'\t}\n'
   b'\n'
   b'\t/// <summary>\n'
   b'\t/// 2026-10-05: save the custom name of the firmware profile being edited.\n'
   b'\t/// Same rule as the parameters: select (mode, profile) first, then write.\n'
   b'\t/// </summary>\n'
   b'\tprivate async Task SaveFirmwareProfileNameAsync()\n'
   b'\t{\n'
   b'\t\tstring text = _nameBox.Text.Trim();\n'
   b'\t\tif (text.Length > 12)\n'
   b'\t\t{\n'
   b'\t\t\t_status.Text = string.Format(Strings.PerfModeNameTooLong, 12);\n'
   b'\t\t\t_status.ForeColor = UiVisualStyle.Danger;\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tif (text.Length == 0)\n'
   b'\t\t{\n'
   b'\t\t\t_status.Text = Strings.ProfileNameNotConfirmed;\n'
   b'\t\t\t_status.ForeColor = UiVisualStyle.Warn;\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tif (!await SelectFirmwareTargetAsync())\n'
   b'\t\t{\n'
   b'\t\t\t_status.Text = Strings.ProfileNameNotConfirmed;\n'
   b'\t\t\t_status.ForeColor = UiVisualStyle.Danger;\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tMechrevoService service = Program.service;\n'
   b'\t\tbool flag = service != null && await service.SetCustomProfileName(text);\n'
   b'\t\t_firmwareProfileName = text;\n'
   b'\t\tEndRename();\n'
   b'\t\tUpdateFirmwareTitle();\n'
   b'\t\t_status.Text = (flag ? Strings.ProfileNameSaved : Strings.ProfileNameNotConfirmed);\n'
   b'\t\t_status.ForeColor = (flag ? UiVisualStyle.Ok : UiVisualStyle.Warn);\n'
   b'\t}\n'
   b'\n'
   b'\tprivate void BeginRename()\n\t{\n',
   'UpdateFirmwareTitle / SaveFirmwareProfileNameAsync')

# ============ 6) BeginRename 固件分支 ============
op(F_CMF,
   b'\tprivate void BeginRename()\n\t{\n'
   b'\t\tPerfModeDefinition perfModeDefinition = FindMode(_modeId);\n',
   b'\tprivate void BeginRename()\n\t{\n'
   b'\t\t// 2026-10-05: firmware profiles are named too (CustomizeName), so allow rename there.\n'
   b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\t_nameBox.Text = (_firmwareProfileName ?? "");\n'
   b'\t\t\t_nameRow.Visible = true;\n'
   b'\t\t\tResizeToContent();\n'
   b'\t\t\t_nameBox.Focus();\n'
   b'\t\t\t_nameBox.SelectAll();\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tPerfModeDefinition perfModeDefinition = FindMode(_modeId);\n',
   'BeginRename 固件分支')

# ============ 7) SaveNameAsync 固件分支 ============
op(F_CMF,
   b'\tprivate async Task SaveNameAsync()\n\t{\n'
   b'\t\tPerfModeService perf = PerfModeService.Instance;\n',
   b'\tprivate async Task SaveNameAsync()\n\t{\n'
   b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\tawait SaveFirmwareProfileNameAsync();\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tPerfModeService perf = PerfModeService.Instance;\n',
   'SaveNameAsync 固件分支')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_VMD, F_CMF)}
    if b'SaveFirmwareProfileNameAsync' in data[F_CMF]:
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
        if path.endswith('CustomModeForm.cs'):
            blob = blob.replace(b'\r\n', b'\n')
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
