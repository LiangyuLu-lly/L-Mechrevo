# -*- coding: utf-8 -*-
# 开机自启动 + 强制启用水冷菜单 放到系统标题行同一行（开机自启在左、水冷在右）
import io

p = r'D:\openLM\source\MechrevoLite\SettingsDialog.cs'
t = io.open(p, encoding='utf-8').read()

old = '''		// 系统标题行（含开机自启动开关，放在标题右侧）
		BufferedTableLayoutPanel systemHeader = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(10), 0, D(4)),
			BackColor = UiVisualStyle.Window
		};
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		systemHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label systemTitle = new Label
		{
			Text = Strings.SystemSection,
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, D(8), 0)
		};
		systemHeader.Controls.Add(systemTitle, 0, 0);
		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.BackColor = UiVisualStyle.Window;
			startupCheck.Dock = DockStyle.Right;
			startupCheck.Anchor = AnchorStyles.Right;
			startupCheck.Margin = Padding.Empty;
			systemHeader.Controls.Add(startupCheck, 1, 0);
		}
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

new = '''		// 系统标题行（含开机自启动 + 强制启用水冷菜单开关，标题右侧同一行：开机自启在左、水冷在右）
		BufferedTableLayoutPanel systemHeader = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(10), 0, D(4)),
			BackColor = UiVisualStyle.Window
		};
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		systemHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label systemTitle = new Label
		{
			Text = Strings.SystemSection,
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, D(8), 0)
		};
		systemHeader.Controls.Add(systemTitle, 0, 0);
		BufferedTableLayoutPanel systemSwitchGroup = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Right,
			BackColor = UiVisualStyle.Window
		};
		systemSwitchGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemSwitchGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemSwitchGroup.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.BackColor = UiVisualStyle.Window;
			startupCheck.Dock = DockStyle.None;
			startupCheck.Anchor = AnchorStyles.Left;
			startupCheck.Margin = Padding.Empty;
			systemSwitchGroup.Controls.Add(startupCheck, 0, 0);
		}
		RCheckBox waterCoolingCheck = new RCheckBox
		{
			Name = "checkForceWaterCooling",
			Text = Strings.ForceWaterCoolingMenu,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(D(8), 0, 0, 0),
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
		systemSwitchGroup.Controls.Add(waterCoolingCheck, 1, 0);
		systemHeader.Controls.Add(systemSwitchGroup, 1, 0);
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(systemHeader, 0, r++);
		officialPanel.Dock = DockStyle.Top;'''

print('[layout]', t.count(old))
assert t.count(old) == 1
t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
