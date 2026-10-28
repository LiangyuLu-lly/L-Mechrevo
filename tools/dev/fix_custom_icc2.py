# -*- coding: utf-8 -*-
# 屏幕校色自定义 ICC（续）：SettingsForm 剩余 4 处 + Strings + 资源
import io

def edit(path, old, new, tag, enc='utf-8'):
    t = io.open(path, encoding=enc).read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding=enc, newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

# 3) 设置按钮：弹窗关闭后重建下拉
edit(p,
'settingsForm.ShowDialog(this);\n\t\t};',
'settingsForm.ShowDialog(this);\n\t\t\t// 自定义开关状态可能在弹窗内变化：关闭弹窗后重建校色下拉（含/不含“自定义”项）\n\t\t\tRebuildCalibrationItems(_calibCombo);\n\t\t};',
'settings-click')

# 4) ResolveColorCalibrationMode：999 优先
edit(p,
'\tinternal static int ResolveColorCalibrationMode()\n\t{\n\t\tMechrevoHw? hw = Program.hw;',
'\tinternal static int ResolveColorCalibrationMode()\n\t{\n\t\tif (AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999)\n\t\t{\n\t\t\t// 自定义 ICC 档位（不走 GCU）：始终以 LM 记忆为准，不受 GCU 回读影响\n\t\t\treturn 999;\n\t\t}\n\t\tMechrevoHw? hw = Program.hw;',
'resolve-999')

# 5) CalibComboIndexFor：combo 参数 + 999 定位
edit(p,
'\tinternal static int CalibComboIndexFor(int mode)\n\t{\n\t\tint? mqttMaxCalib = Program.hw?.MqttColorCalibrationLevel;',
'\tinternal static int CalibComboIndexFor(int mode, ComboBox? combo = null)\n\t{\n\t\tif (mode == 999)\n\t\t{\n\t\t\t// 自定义 ICC 档位：返回下拉中 value==999 的项索引；找不到回退 0\n\t\t\tif (combo != null)\n\t\t\t{\n\t\t\t\tfor (int i = 0; i < combo.Items.Count; i++)\n\t\t\t\t{\n\t\t\t\t\tif (combo.Items[i] is KeyValuePair<string, int> kv2 && kv2.Value == 999)\n\t\t\t\t\t{\n\t\t\t\t\t\treturn i;\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t\treturn 0;\n\t\t}\n\t\tint? mqttMaxCalib = Program.hw?.MqttColorCalibrationLevel;',
'comboindex-999')

# 6) RebuildCalibrationItems：先加自定义项再选索引
edit(p,
'"AdobeRGB", 4));\n\t\t\t}\n\t\t\tcalibCombo.SelectedIndex = CalibComboIndexFor(currentMode);',
'"AdobeRGB", 4));\n\t\t\t}\n\t\t\tif (AppConfig.Is("calib_custom_on"))\n\t\t\t{\n\t\t\t\t// 自定义校色开关开启时追加“自定义”项（经 Windows ICC，不走 GCU）\n\t\t\t\tcalibCombo.Items.Add(new KeyValuePair<string, int>(Strings.CustomCalibration, 999));\n\t\t\t}\n\t\t\tcalibCombo.SelectedIndex = CalibComboIndexFor(currentMode, calibCombo);',
'rebuild-custom')

# ---------- Strings.cs ----------
ps = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
edit(ps,
'\tinternal static string CalibApplyOnStartupHint => ResourceManager.GetString("CalibApplyOnStartupHint", resourceCulture);',
'\tinternal static string CalibApplyOnStartupHint => ResourceManager.GetString("CalibApplyOnStartupHint", resourceCulture);\n\n\tinternal static string CustomCalibration => ResourceManager.GetString("CustomCalibration", resourceCulture);\n\n\tinternal static string CustomCalibrationHint => ResourceManager.GetString("CustomCalibrationHint", resourceCulture);\n\n\tinternal static string Import => ResourceManager.GetString("Import", resourceCulture);',
'strings-cs')

# ---------- resx ----------
def edit_resx(path, anchor, new_block, tag):
    raw = open(path, 'rb').read()
    has_bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig' if has_bom else 'utf-8')
    n = text.count(anchor)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    text = text.replace(anchor, new_block + anchor)
    out = text.encode('utf-8-sig' if has_bom else 'utf-8')
    open(path, 'wb').write(out)
    print('[' + tag + '] OK')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
'  </data>\n<data name="DisplayEffectCinemaMode" xml:space="preserve">',
'''  </data>
<data name="CustomCalibration" xml:space="preserve">
    
    
    <value>Custom calibration</value>
    
  
  </data>
<data name="CustomCalibrationHint" xml:space="preserve">
    
    
    <value>When enabled, a "Custom" option appears in the calibration list and applies the selected ICC profile via Windows color management (not via GCU). Turning it off removes all applied ICC profiles.</value>
    
  
  </data>
<data name="Import" xml:space="preserve">
    
    
    <value>Import</value>
    
  
  </data>
''',
'resx-en')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
'  </data>\n<data name="DisplayEffectCinemaMode" xml:space="preserve">',
'''  </data>
<data name="CustomCalibration" xml:space="preserve">
    
    
    <value>自定义校色</value>
    
  
  </data>
<data name="CustomCalibrationHint" xml:space="preserve">
    
    
    <value>开启后校色下拉列表将出现“自定义”选项，选择后经 Windows 颜色管理应用所选 ICC 配置文件（不经由 GCU）。关闭将取消所有已应用的 ICC。</value>
    
  
  </data>
<data name="Import" xml:space="preserve">
    
    
    <value>导入</value>
    
  
  </data>
''',
'resx-zh')

print('ALL OK')
