# -*- coding: utf-8 -*-
# 屏幕校色自定义 ICC：SettingsForm + Strings + 资源
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

# 1) 初始化：传 combo（999 时定位自定义项）
edit(p,
'''		int initialCalibMode = ResolveColorCalibrationMode();
		calibSwitch.Checked = initialCalibMode > 0;
		calibCombo.Enabled = calibSwitch.Checked;
		calibCombo.SelectedIndex = CalibComboIndexFor(initialCalibMode);''',
'''		int initialCalibMode = ResolveColorCalibrationMode();
		calibSwitch.Checked = initialCalibMode > 0;
		calibCombo.Enabled = calibSwitch.Checked;
		calibCombo.SelectedIndex = CalibComboIndexFor(initialCalibMode, calibCombo);''',
'init-index')

# 2) 下拉切换：999 走 ICC（不走 GCU）
edit(p,
'''					int value = keyValuePair.Value;
					int acceptedIndex = calibCombo.SelectedIndex;
					calibCombo.Enabled = false;
					bool flag4 = await Program.service.SetColorCalibration(value);
					calibCombo.Enabled = true;''',
'''					int value = keyValuePair.Value;
					int acceptedIndex = calibCombo.SelectedIndex;
					calibCombo.Enabled = false;
					bool flag4;
					if (value == 999)
					{
						// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏
						string? iccName = AppConfig.GetString("calib_custom_icc");
						string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
						flag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && IccHelper.ApplyIcc(iccPath);
					}
					else
					{
						flag4 = await Program.service.SetColorCalibration(value);
					}
					calibCombo.Enabled = true;''',
'combo-999')

# 3) 设置按钮：弹窗关闭后重建下拉（自定义开关可能变化）
edit(p,
'''			calibSettingsBtn.Click += (object? _, EventArgs _) =>
			{
				using CalibrationStartupForm settingsForm = new CalibrationStartupForm();
				settingsForm.ShowDialog(this);
			};''',
'''			calibSettingsBtn.Click += (object? _, EventArgs _) =>
			{
				using CalibrationStartupForm settingsForm = new CalibrationStartupForm();
				settingsForm.ShowDialog(this);
				// 自定义开关状态可能在弹窗内变化：关闭弹窗后重建校色下拉（含/不含“自定义”项）
				RebuildCalibrationItems(_calibCombo);
			};''',
'settings-click')

# 4) ResolveColorCalibrationMode：999 优先（不受 GCU 回读影响）
edit(p,
'''	internal static int ResolveColorCalibrationMode()
	{
		MechrevoHw? hw = Program.hw;
		if (hw == null || !hw.ColorCalibrationModeSeen)
		{''',
'''	internal static int ResolveColorCalibrationMode()
	{
		if (AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999)
		{
			// 自定义 ICC 档位（不走 GCU）：始终以 LM 记忆为准，不受 GCU 回读影响
			return 999;
		}
		MechrevoHw? hw = Program.hw;
		if (hw == null || !hw.ColorCalibrationModeSeen)
		{''',
'resolve-999')

# 5) CalibComboIndexFor：加 combo 参数 + 999 定位
edit(p,
'''	internal static int CalibComboIndexFor(int mode)
	{
		int? mqttMaxCalib = Program.hw?.MqttColorCalibrationLevel;
		int max = mqttMaxCalib ?? Program.hw?.Capabilities?.ColorCalibrationLevel ?? 0;
		if (max <= 0)
		{
			max = (DisplayColorGamut.IsSrgbDisplay == true) ? 1 : 2;
		}
		int value = Math.Max(1, Math.Min(mode, max));
		return value - 1;
	}''',
'''	internal static int CalibComboIndexFor(int mode, ComboBox? combo = null)
	{
		if (mode == 999)
		{
			// 自定义 ICC 档位：返回下拉中 value==999 的项索引；找不到回退 0
			if (combo != null)
			{
				for (int i = 0; i < combo.Items.Count; i++)
				{
					if (combo.Items[i] is KeyValuePair<string, int> kv2 && kv2.Value == 999)
					{
						return i;
					}
				}
			}
			return 0;
		}
		int? mqttMaxCalib = Program.hw?.MqttColorCalibrationLevel;
		int max = mqttMaxCalib ?? Program.hw?.Capabilities?.ColorCalibrationLevel ?? 0;
		if (max <= 0)
		{
			max = (DisplayColorGamut.IsSrgbDisplay == true) ? 1 : 2;
		}
		int value = Math.Max(1, Math.Min(mode, max));
		return value - 1;
	}''',
'comboindex-999')

# 6) RebuildCalibrationItems：先加自定义项再选索引
edit(p,
'''			calibCombo.SelectedIndex = CalibComboIndexFor(currentMode);
		}
		finally
		{
			_syncingDisplay = false;
		}
	}
	
	/// <summary>校色配置下拉的当前索引（关闭由开关独立控制，下拉不再含“关闭”项）。</summary>''',
'''			if (AppConfig.Is("calib_custom_on"))
			{
				// 自定义校色开关开启时追加“自定义”项（经 Windows ICC，不走 GCU）
				calibCombo.Items.Add(new KeyValuePair<string, int>(Strings.CustomCalibration, 999));
			}
			calibCombo.SelectedIndex = CalibComboIndexFor(currentMode, calibCombo);
		}
		finally
		{
			_syncingDisplay = false;
		}
	}
	
	/// <summary>校色配置下拉的当前索引（关闭由开关独立控制，下拉不再含“关闭”项）。</summary>''',
'rebuild-custom')

# ---------- Strings.cs ----------
ps = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
edit(ps,
'''	internal static string CalibApplyOnStartupHint => ResourceManager.GetString("CalibApplyOnStartupHint", resourceCulture);''',
'''	internal static string CalibApplyOnStartupHint => ResourceManager.GetString("CalibApplyOnStartupHint", resourceCulture);

	internal static string CustomCalibration => ResourceManager.GetString("CustomCalibration", resourceCulture);

	internal static string CustomCalibrationHint => ResourceManager.GetString("CustomCalibrationHint", resourceCulture);

	internal static string Import => ResourceManager.GetString("Import", resourceCulture);''',
'strings-cs')

# ---------- resx（保持原编码/BOM） ----------
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

# 默认（英文）
edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
'''  </data>
<data name="DisplayEffectCinemaMode" xml:space="preserve">''',
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
<data name="DisplayEffectCinemaMode" xml:space="preserve">''',
'resx-en')

# 中文
edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
'''  </data>
<data name="DisplayEffectCinemaMode" xml:space="preserve">''',
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
<data name="DisplayEffectCinemaMode" xml:space="preserve">''',
'resx-zh')

print('ALL OK')
