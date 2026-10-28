# -*- coding: utf-8 -*-
# 设置中增加"强制启用水冷菜单"开关：打开后强制显示水冷菜单
import io

# ---------- 1) Strings.cs 加属性 ----------
p = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
t = io.open(p, encoding='utf-8').read()
anchor = '''	internal static string ChargeLimitIneffective => ResourceManager.GetString("ChargeLimitIneffective", resourceCulture);'''
new = anchor + '''

	internal static string ForceWaterCoolingMenu => ResourceManager.GetString("ForceWaterCoolingMenu", resourceCulture);

	internal static string ForceWaterCoolingMenuTip => ResourceManager.GetString("ForceWaterCoolingMenuTip", resourceCulture);'''
print('[strings-cs]', t.count(anchor))
assert t.count(anchor) == 1
t = t.replace(anchor, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)

# ---------- 2) Strings.resx 加英文条目 ----------
p = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
t = io.open(p, encoding='utf-8').read()
en = '''<data name="ForceWaterCoolingMenu" xml:space="preserve">
    <value>Force water cooling menu</value>
  </data>
<data name="ForceWaterCoolingMenuTip" xml:space="preserve">
    <value>Always show the water cooling menu, even when no liquid cooler is detected</value>
  </data>
</root>'''
assert t.rstrip().endswith('</root>')
t = t.rstrip()[:-len('</root>')] + en
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('[resx-en] ok')

# ---------- 3) Strings.zh-CN.resx 加中文条目 ----------
p = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'
t = io.open(p, encoding='utf-8').read()
zh = '''<data name="ForceWaterCoolingMenu" xml:space="preserve">
    <value>强制启用水冷菜单</value>
  </data>
<data name="ForceWaterCoolingMenuTip" xml:space="preserve">
    <value>强制显示水冷菜单，即使未检测到液冷设备</value>
  </data>
</root>'''
assert t.rstrip().endswith('</root>')
t = t.rstrip()[:-len('</root>')] + zh
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('[resx-zh] ok')

# ---------- 4) SettingsForm.cs 水冷可见性条件 ----------
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()
old = '''		bool flag14 = Show(caps.LiquidCooling || (hw?.SupportsLiquidCooling ?? false));'''
new = '''		bool flag14 = Show(AppConfig.Is("force_liquid_cooling_menu") || caps.LiquidCooling || (hw?.SupportsLiquidCooling ?? false));'''
print('[settingsform]', t.count(old))
assert t.count(old) == 1
t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)

# ---------- 5) SettingsDialog.cs 系统区加开关 ----------
p = r'D:\openLM\source\MechrevoLite\SettingsDialog.cs'
t = io.open(p, encoding='utf-8').read()
old = '''		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(systemHeader, 0, r++);
		officialPanel.Dock = DockStyle.Top;'''
new = '''		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(systemHeader, 0, r++);
		// 强制启用水冷菜单开关（系统区）
		RCheckBox waterCoolingCheck = new RCheckBox
		{
			Name = "checkForceWaterCooling",
			Text = Strings.ForceWaterCoolingMenu,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(0, D(2), 0, D(4)),
			AccessibleDescription = Strings.ForceWaterCoolingMenuTip
		};
		waterCoolingCheck.Checked = AppConfig.Is("force_liquid_cooling_menu");
		waterCoolingCheck.CheckedChanged += (object? _, EventArgs _) =>
		{
			AppConfig.Set("force_liquid_cooling_menu", waterCoolingCheck.Checked ? 1 : 0);
			AppConfig.Flush();
			if (_owner is SettingsForm settingsForm)
			{
				settingsForm.RefreshDeviceCapabilities();
			}
		};
		_toolTip.SetToolTip(waterCoolingCheck, Strings.ForceWaterCoolingMenuTip);
		AddRow(waterCoolingCheck);
		officialPanel.Dock = DockStyle.Top;'''
print('[settingsdialog]', t.count(old))
assert t.count(old) == 1
t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)

print('ALL WRITTEN')
