# -*- coding: utf-8 -*-
"""
补丁：预设色板（1-29）+ 单色颜色改名「自定义颜色」+ 修复条件收窄到 30 系
================================================================================
用户需求（2026-10-04）：
  1. 键盘灯修复收窄为"只对 30 系 GCU 生效"（排除 50 系，规避苍龙等未验证机型）
  2. 加入 1-29 预设颜色按钮
  3. 「单色颜色」改名为「自定义颜色」

设计：
  A. LightChannelSettings 新增 UsePresetColor（默认 false = 自定义色，向后兼容）
  B. LightingSettingsStore 持久化 usepreset（缺省 0 → 老配置行为完全不变）
  C. MechrevoService.SetLightEffect：MonochromeIndex 由设置解析
     - UsePresetColor 且 1..29 → 直发该索引（预设色板）
     - 否则 → "33"（自定义色，颜色走 ColorBuffer）
     40 系分支同样改用解析值（默认仍是 33，行为不变）
     新补的 30 系分支追加 !IsGcu50 收窄
  D. RgbForm.BuildGcuFirmwareParams：新增「预设颜色」行（29 色块自动换行）
     点预设色 → UsePresetColor=true + MonochromeIndex=N
     点自定义色 → UsePresetColor=false + ColorArgb
  E. 资源：SingleColor 值改「自定义颜色」/「Custom color」；新增 PresetColor 键

安全：自动适配 LF/CRLF 与 BOM；**先全量校验锚点，全部通过才统一写盘**（不会半途写坏）。
"""
import io
import os
import sys

SRC = r'D:\openLM\source'
F_RESX_EN = os.path.join(SRC, 'MechrevoLite.Properties.Strings.resx')
F_RESX_ZH = os.path.join(SRC, 'MechrevoLite.Properties.Strings.zh-CN.resx')
F_STRINGS = os.path.join(SRC, 'MechrevoLite.Properties', 'Strings.cs')
F_LCS = os.path.join(SRC, 'MechrevoLite.Hardware', 'LightChannelSettings.cs')
F_STORE = os.path.join(SRC, 'MechrevoLite.Hardware', 'LightingSettingsStore.cs')
F_RGB = os.path.join(SRC, 'MechrevoLite', 'RgbForm.cs')
F_SVC = os.path.join(SRC, 'MechrevoLite.Hardware', 'MechrevoService.cs')

OPS = []          # (path, old, new, expect, label)
CACHE = {}        # path -> (text_lf, bom, crlf)


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


# ============================================================ 定义全部改动
# ---- E. 资源 ----
op(F_RESX_ZH, '<value>单色颜色</value>', '<value>自定义颜色</value>', 'zh SingleColor 改名')
op(F_RESX_EN, '<value>Steady color</value>', '<value>Custom color</value>', 'en SingleColor 改名')
op(F_RESX_ZH, '</root>',
   '<data name="PresetColor" xml:space="preserve">\n'
   '    <value>预设颜色</value>\n'
   '  </data>\n</root>', 'zh 新增 PresetColor 键')
op(F_RESX_EN, '</root>',
   '<data name="PresetColor" xml:space="preserve">\n'
   '    <value>Preset color</value>\n'
   '  </data>\n</root>', 'en 新增 PresetColor 键')
op(F_STRINGS,
   '\tinternal static string SingleColor => ResourceManager.GetString("SingleColor", resourceCulture);\n',
   '\tinternal static string SingleColor => ResourceManager.GetString("SingleColor", resourceCulture);\n'
   '\n'
   '\tinternal static string PresetColor => ResourceManager.GetString("PresetColor", resourceCulture);\n',
   'Strings.cs 加 PresetColor 访问器')

# ---- A. 记录结构 ----
op(F_LCS,
   'internal readonly record struct LightChannelSettings(string Effect, int Light, int Speed, int ColorArgb, '
   'bool PowerOn, int[]? ManualIndexes = null, int BreathingIndex = 1, int MonochromeIndex = 4)',
   'internal readonly record struct LightChannelSettings(string Effect, int Light, int Speed, int ColorArgb, '
   'bool PowerOn, int[]? ManualIndexes = null, int BreathingIndex = 1, int MonochromeIndex = 4, '
   'bool UsePresetColor = false)',
   'LightChannelSettings 加 UsePresetColor 字段')

# ---- B. 设置存储 ----
op(F_STORE, '\t\tint monoIndex = 4;\n',
   '\t\tint monoIndex = 4;\n\t\tbool usePreset = false;\n', 'ParseLines 加 usePreset 局部变量')
op(F_STORE, '\t\t\tcase "monoindex":\n',
   '\t\t\tcase "usepreset":\n'
   '\t\t\t{\n'
   '\t\t\t\tusePreset = (text2 == "1");\n'
   '\t\t\t\tbreak;\n'
   '\t\t\t}\n'
   '\t\t\tcase "monoindex":\n', 'ParseLines 加 usepreset 解析分支')
op(F_STORE, '\t\treturn new LightChannelSettings(effect, light, speed, colorArgb, powerOn, manualIndexes, '
            'breathingIndex, monoIndex);\n',
   '\t\treturn new LightChannelSettings(effect, light, speed, colorArgb, powerOn, manualIndexes, '
   'breathingIndex, monoIndex, usePreset);\n', 'ParseLines 返回 usePreset')
op(F_STORE, "return string.Join('\\n', new string[9]", "return string.Join('\\n', new string[10]",
   'Serialize 数组 9 -> 10')
op(F_STORE, '\t\t\t"monoindex=" + settings.MonochromeIndex.ToString(CultureInfo.InvariantCulture),\n',
   '\t\t\t"monoindex=" + settings.MonochromeIndex.ToString(CultureInfo.InvariantCulture),\n'
   '\t\t\t"usepreset=" + (settings.UsePresetColor ? "1" : "0"),\n', 'Serialize 输出 usepreset')

# ---- C. 服务层 ----
op(F_SVC, '\t\t\t\tLogger.WriteLine("SetLightEffect gcu40=" + isGcu40Keyboard + ", effect=" + effect);\n',
   '\t\t\t\tLogger.WriteLine("SetLightEffect gcu40=" + isGcu40Keyboard + ", effect=" + effect);\n'
   '\t\t\t\t// 2026-10-04：单色颜色来源 —— 预设色板索引 1..29 直发索引；自定义色发 33，颜色走 ColorBuffer\n'
   '\t\t\t\tstring monoIndexValue = "33";\n'
   '\t\t\t\tif (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var monoPresetSettings)\n'
   '\t\t\t\t\t&& monoPresetSettings.UsePresetColor\n'
   '\t\t\t\t\t&& monoPresetSettings.MonochromeIndex >= 1\n'
   '\t\t\t\t\t&& monoPresetSettings.MonochromeIndex <= 29)\n'
   '\t\t\t\t{\n'
   '\t\t\t\t\tmonoIndexValue = monoPresetSettings.MonochromeIndex.ToString();\n'
   '\t\t\t\t}\n', 'SetLightEffect 解析 monoIndexValue')
op(F_SVC, 'payload["MonochromeIndex"] = "33";', 'payload["MonochromeIndex"] = monoIndexValue;',
   'MonochromeIndex 改用解析值（2 处）', expect=2)
op(F_SVC, 'if (isSingleZoneKeyboard && !isGcu40Keyboard)\n',
   '// 2026-10-04 收窄：排除 50 系（苍龙等单分区机型未实机验证，保持原行为）\n'
   '\t\t\t\tif (isSingleZoneKeyboard && !isGcu40Keyboard && !IsGcu50)\n', '新分支追加 !IsGcu50 收窄')

# ---- D. 界面 ----
OLD_UI = '''\t\t\telse
\t\t\t{
\t\t\t\tRColorButton colorBtn = new RColorButton
\t\t\t{
\t\t\t\tSize = new Size(D(46), D(24)),
\t\t\t\tBackColor = UiVisualStyle.Input,
\t\t\t\tBorderColor = UiVisualStyle.Border,
\t\t\t\tSwatchColor = Color.FromArgb(lightChannelSettings.ColorArgb),
\t\t\t\tCursor = Cursors.Hand
\t\t\t};
\t\t\tcolorBtn.Click += (object? _, EventArgs _) =>
\t\t\t{
\t\t\t\tRColorPicker rColorPicker = new RColorPicker(Color.FromArgb(GcuSettings().ColorArgb));
\t\t\t\trColorPicker.ColorChanged += (Color c) =>
\t\t\t\t{
\t\t\t\t\tLightChannelSettings settings = GcuSettings()with
\t\t\t\t\t{
\t\t\t\t\t\tColorArgb = c.ToArgb()
\t\t\t\t\t};
\t\t\t\t\tLightingSettingsStore.Save("Keyboard/Ctrl", settings);
\t\t\t\t\tcolorBtn.SwatchColor = c;
\t\t\t\t\tif (settings.Effect == "Single")
\t\t\t\t\t{
\t\t\t\t\t\tPublishGcuFirmwareEffect(settings);
\t\t\t\t\t}
\t\t\t\t};
\t\t\t\trColorPicker.ShowDialog(this);
\t\t\t};
\t\t\t\tAddRow(Strings.SingleColor, colorBtn);
\t\t\t}
'''

NEW_UI = '''\t\t\telse
\t\t\t{
\t\t\t\tRColorButton customColorBtn = new RColorButton
\t\t\t{
\t\t\t\tSize = new Size(D(46), D(24)),
\t\t\t\tBackColor = UiVisualStyle.Input,
\t\t\t\tBorderColor = (lightChannelSettings.UsePresetColor ? UiVisualStyle.Border : UiVisualStyle.Accent),
\t\t\t\tSwatchColor = Color.FromArgb(lightChannelSettings.ColorArgb),
\t\t\t\tCursor = Cursors.Hand
\t\t\t};
\t\t\t// 2026-10-04：预设色板 1-29 色块（点选 → MonochromeIndex 直发索引，不走 ColorBuffer）
\t\t\tFlowLayoutPanel presetPanel = new FlowLayoutPanel
\t\t\t{
\t\t\t\tFlowDirection = FlowDirection.LeftToRight,
\t\t\t\tWrapContents = true,
\t\t\t\tAutoSize = true,
\t\t\t\tAutoSizeMode = AutoSizeMode.GrowAndShrink,
\t\t\t\tMaximumSize = new Size(D(268), 0),
\t\t\t\tMargin = Padding.Empty
\t\t\t};
\t\t\tRColorButton[] presetButtons = new RColorButton[29];
\t\t\tint activePreset = (lightChannelSettings.UsePresetColor ? Math.Clamp(lightChannelSettings.MonochromeIndex, 1, 29) : 0);
\t\t\tfor (int pi = 0; pi < 29; pi++)
\t\t\t{
\t\t\t\tint pidx = pi + 1;
\t\t\t\tpresetButtons[pi] = new RColorButton
\t\t\t\t{
\t\t\t\t\tSize = new Size(D(22), D(22)),
\t\t\t\t\tBackColor = UiVisualStyle.Input,
\t\t\t\t\tBorderColor = ((pidx == activePreset) ? UiVisualStyle.Accent : UiVisualStyle.Border),
\t\t\t\t\tSwatchColor = Color.FromArgb(LightChannelSettings.ManualPalette[pi]),
\t\t\t\t\tCursor = Cursors.Hand
\t\t\t\t};
\t\t\t\tpresetButtons[pi].Click += (object? _, EventArgs _) =>
\t\t\t\t{
\t\t\t\t\tLightChannelSettings presetSettings = GcuSettings() with
\t\t\t\t\t{
\t\t\t\t\t\tUsePresetColor = true,
\t\t\t\t\t\tMonochromeIndex = pidx
\t\t\t\t\t};
\t\t\t\t\tLightingSettingsStore.Save("Keyboard/Ctrl", presetSettings);
\t\t\t\t\tfor (int k = 0; k < 29; k++)
\t\t\t\t\t{
\t\t\t\t\t\tpresetButtons[k].BorderColor = ((k + 1 == pidx) ? UiVisualStyle.Accent : UiVisualStyle.Border);
\t\t\t\t\t}
\t\t\t\t\tcustomColorBtn.BorderColor = UiVisualStyle.Border;
\t\t\t\t\tif (presetSettings.Effect == "Single")
\t\t\t\t\t{
\t\t\t\t\t\tPublishGcuFirmwareEffect(presetSettings);
\t\t\t\t\t}
\t\t\t\t};
\t\t\t\tpresetPanel.Controls.Add(presetButtons[pi]);
\t\t\t}
\t\t\tAddRow(Strings.PresetColor, presetPanel);
\t\t\tcustomColorBtn.Click += (object? _, EventArgs _) =>
\t\t\t{
\t\t\t\tRColorPicker rColorPicker = new RColorPicker(Color.FromArgb(GcuSettings().ColorArgb));
\t\t\t\trColorPicker.ColorChanged += (Color c) =>
\t\t\t\t{
\t\t\t\t\tLightChannelSettings settings = GcuSettings()with
\t\t\t\t\t{
\t\t\t\t\t\tColorArgb = c.ToArgb(),
\t\t\t\t\t\tUsePresetColor = false
\t\t\t\t\t};
\t\t\t\t\tLightingSettingsStore.Save("Keyboard/Ctrl", settings);
\t\t\t\t\tcustomColorBtn.SwatchColor = c;
\t\t\t\t\tcustomColorBtn.BorderColor = UiVisualStyle.Accent;
\t\t\t\t\tfor (int k = 0; k < 29; k++)
\t\t\t\t\t{
\t\t\t\t\t\tpresetButtons[k].BorderColor = UiVisualStyle.Border;
\t\t\t\t\t}
\t\t\t\t\tif (settings.Effect == "Single")
\t\t\t\t\t{
\t\t\t\t\t\tPublishGcuFirmwareEffect(settings);
\t\t\t\t\t}
\t\t\t\t};
\t\t\t\trColorPicker.ShowDialog(this);
\t\t\t};
\t\t\t\tAddRow(Strings.SingleColor, customColorBtn);
\t\t\t}
'''
op(F_RGB, OLD_UI, NEW_UI, '单色行 -> 预设色板行 + 自定义色行')


# ============================================================ 校验 + 写盘
def main():
    if '--check' in sys.argv:
        print('（仅校验模式）')
    # 1) 校验
    errors = []
    done_markers = {'PresetColor': F_RESX_ZH, 'usepreset': F_STORE,
                    'monoIndexValue': F_SVC, 'presetButtons': F_RGB}
    for marker, path in done_markers.items():
        text, _, _ = load(path)
        if marker in text:
            print('已经应用过了（发现标记 %s），无需重复。' % marker)
            return 0
    for path, old, new, expect, label in OPS:
        text, _, _ = load(path)
        n = text.count(old)
        if n != expect:
            errors.append('%-28s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘任何文件 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（共 %d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    # 2) 在内存中按文件依次应用
    for path, old, new, expect, label in OPS:
        text, bom, crlf = load(path)
        CACHE[path] = (text.replace(old, new, expect), bom, crlf)
    # 3) 统一写盘（还原原始行尾与 BOM）
    for path, (text, bom, crlf) in CACHE.items():
        if any(p == path for p, _, _, _, _ in OPS):
            out = text.replace('\n', '\r\n') if crlf else text
            data = out.encode('utf-8')
            if bom:
                data = b'\xef\xbb\xbf' + data
            io.open(path, 'wb').write(data)
            print('  写入 %s' % path)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
