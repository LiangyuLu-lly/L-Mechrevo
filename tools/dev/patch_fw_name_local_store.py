# -*- coding: utf-8 -*-
"""
重命名改为 **LM 自存**（用户决策：名字不在 GCU 域里，LM 是前台 UI，自己实现）
================================================================================
依据（2026-10-05 抓包 + 官方控制台截图）：
  * 官方每档名字（ZZTEST / 114514 / Profile N）**不走 MQTT**，也不在任何
    `Mode*_Profile*.json` 的 `CustomizeName`（那里全是默认数字）、注册表、Packages 目录
  * LM 用的 `SET_CUSTOM_PROFILE_OSD_STRING` 对预设档不落盘（`SetCustomProfileName` 也无条件 return true）
  -> 结论：预设档名字不是 GCU 的能力。LM 作为前台 UI，自己存即可。
做法：
  1. `ModeProfileConfig` 增加名称存取（键 `mode_name_<gaming|turbo|office>_<idx>`，走 AppConfig）
  2. `CustomModeForm`：读名字改从 ModeProfileConfig；保存改为写本地（**不再调 GCU**、
     也不再要求先选中目标 —— 名称与硬件无关）
  3. `SettingsForm.PerfConfigComboText`：改读本地名称
行尾：ModeProfileConfig.cs 未知（脚本按 \n 试）、CustomModeForm.cs=LF、SettingsForm.cs=CRLF
"""
import io
import os
import sys

F_MPC = r'D:\openLM\source\MechrevoLite.Mode\ModeProfileConfig.cs'
F_CMF = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---------- 1) ModeProfileConfig：名称存取 ----------
op(F_MPC,
   b'\t\tAppConfig.Set(text, num);\n'
   b'\t\tLogger.WriteLine($"ModeProfileConfig: visualMode={visualMode} profile={num} count={Count}");\n'
   b'\t}\n'
   b'}\n',
   b'\t\tAppConfig.Set(text, num);\n'
   b'\t\tLogger.WriteLine($"ModeProfileConfig: visualMode={visualMode} profile={num} count={Count}");\n'
   b'\t}\n'
   b'\n'
   b'\t/// <summary>\n'
   b'\t/// 2026-10-05: per-config custom name, stored locally by LM.\n'
   b'\t/// The GCU has no name for *preset* profiles (SET_CUSTOM_PROFILE_OSD_STRING does not\r\n'
   b'\t/// persist one -- verified by capture: the official console\'s own names never reach\r\n'
   b'\t/// MQTT, the profile JSON, the registry or the package data), so LM keeps its own.\r\n'
   b'\t/// </summary>\n'
   b'\tinternal static string? GetName(int visualMode, int profileIndex)\n'
   b'\t{\n'
   b'\t\tstring? text = NameKeyFor(visualMode, profileIndex);\n'
   b'\t\tif (text == null)\n'
   b'\t\t{\n'
   b'\t\t\treturn null;\n'
   b'\t\t}\n'
   b'\t\tstring text2 = AppConfig.Get(text, "");\n'
   b'\t\treturn string.IsNullOrWhiteSpace(text2) ? null : text2;\n'
   b'\t}\n'
   b'\n'
   b'\t/// <summary>Save the custom name for this config; null/blank clears it.</summary>\n'
   b'\tinternal static void SetName(int visualMode, int profileIndex, string? name)\n'
   b'\t{\n'
   b'\t\tstring? text = NameKeyFor(visualMode, profileIndex);\n'
   b'\t\tif (text == null)\n'
   b'\t\t{\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n'
   b'\t\tstring text2 = (name ?? "").Trim();\n'
   b'\t\tAppConfig.Set(text, text2);\n'
   b'\t\tLogger.WriteLine($"ModeProfileConfig: name visualMode={visualMode} profile={profileIndex} name={text2}");\n'
   b'\t}\n'
   b'\n'
   b'\tprivate static string? NameKeyFor(int visualMode, int profileIndex)\n'
   b'\t{\n'
   b'\t\tstring? text = KeyFor(visualMode);\n'
   b'\t\tif (text == null)\n'
   b'\t\t{\n'
   b'\t\t\treturn null;\n'
   b'\t\t}\n'
   b'\t\treturn text.Replace("mode_cfg_", "mode_name_") + "_" + Math.Clamp(profileIndex, 0, Count - 1);\n'
   b'\t}\n'
   b'}\n',
   'ModeProfileConfig 名称存取')

# ---------- 2) CustomModeForm：读名字改用本地 ----------
op(F_CMF,
   b'\t\t\t_firmwareProfileName = VendorModeDefaults.ReadProfileCustomizeName(kind, _firmwareProfileIndex);\n',
   b'\t\t\t// 2026-10-05: name comes from LM\'s own store, not from the profile JSON.\n'
   b'\t\t\t_firmwareProfileName = ModeProfileConfig.GetName(_firmwareVisualMode, _firmwareProfileIndex);\n',
   '读名字改用本地')

# ---------- 3) CustomModeForm：去掉"先选目标"（名称与硬件无关） ----------
op(F_CMF,
   b'\t\tif (!await SelectFirmwareTargetAsync())\n'
   b'\t\t{\n'
   b'\t\t\t_status.Text = Strings.ProfileNameNotConfirmed;\n'
   b'\t\t\t_status.ForeColor = UiVisualStyle.Danger;\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n',
   b'',
   '去掉名称保存前的目标选中')

# ---------- 4) CustomModeForm：保存改本地 ----------
op(F_CMF,
   b'\t\tMechrevoService service = Program.service;\n'
   b'\t\tbool flag = service != null && await service.SetCustomProfileName(text);\n',
   b'\t\t// 2026-10-05: the GCU cannot persist a name for a preset profile, so store it in LM.\n'
   b'\t\tModeProfileConfig.SetName(_firmwareVisualMode, _firmwareProfileIndex, text);\n'
   b'\t\tbool flag = true;\n',
   '名称保存改本地')

# ---------- 5) SettingsForm：下拉标签读本地 ----------
op(F_SF,
   b'\t\t// 2026-10-05: when a custom name exists, show ONLY the name (user request).\r\n'
   b'\t\tstring text2 = VendorModeDefaults.ReadProfileCustomizeName(kind, profileIndex);\r\n',
   b'\t\t// 2026-10-05: when a custom name exists, show ONLY the name (user request).\r\n'
   b'\t\t// Names are LM-local (the GCU has none for preset profiles).\r\n'
   b'\t\tint num = ((kind == PerfModeKind.Silent) ? 2 : ((kind == PerfModeKind.Turbo) ? 1 : 0));\r\n'
   b'\t\tstring text2 = ModeProfileConfig.GetName(num, profileIndex);\r\n',
   '下拉标签读本地')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_MPC, F_CMF, F_SF)}
    if b'mode_name_' in data[F_MPC]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = data[path].count(old)
        if n != expect:
            n_crlf = data[path].count(old.replace(b'\n', b'\r\n'))
            errors.append('%-22s 命中 %d 次(期望 %d)  [%s]  (CRLF 变体命中 %d)' % (os.path.basename(path), n, expect, label, n_crlf))
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
