# -*- coding: utf-8 -*-
# 充电上限恢复机型分流（修正缩进版）：40 系三档；50 系滑块+观察，ineffective 动态回退三档
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

# 1) if(true) 块整体替换（3559-3626，含 Activated 保留在 if 内；VerdictChanged 订阅移外层）
old1 = '''		// [回退] 无极调节（滑块）当前在 50 系/非 50 系机型上均未生效（EC 层未按预期应用），
		// 无条件回退为三档模式（长效 100% / 平衡 80% / 工作站 60%），等待修复后恢复滑块显示。
		// 原条件: if (!IsRtx50SeriesGpu())
		if (true)
		{
			tableLayoutPanel.Controls.Remove(sliderBattery);
			_batteryLimitValue.Visible = false;
			string[] modeLabels = new string[3] { Strings.ChargeModeLongevity, Strings.ChargeModeBalanced, Strings.ChargeModeWorkstation };
			int[] modeLimits = new int[3] { 100, 80, 60 };
			TableLayoutPanel modePanel = new TableLayoutPanel
			{
				ColumnCount = 3,
				RowCount = 1,
				Dock = DockStyle.Fill,
				AutoSize = false,
				Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 20), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 20), ResponsiveLayout.LogicalToDevice(this, 2)),
				Padding = Padding.Empty
			};
			modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
			modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
			modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
			_batteryModeButtons = new RButton[3];
			for (int mi = 0; mi < 3; mi++)
			{
				int mode = mi;
				RButton modeButton = new RButton
				{
					Text = modeLabels[mode],
					Tag = "theme_mode",
					FlatStyle = FlatStyle.Flat,
					ForeColor = UiVisualStyle.Text,
					BackColor = UiVisualStyle.SurfaceRaised,
					Dock = DockStyle.Fill,
					MinimumSize = new Size(ResponsiveLayout.LogicalToDevice(this, 80), ResponsiveLayout.LogicalToDevice(this, 20)),
					Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 3), ResponsiveLayout.LogicalToDevice(this, 6), ResponsiveLayout.LogicalToDevice(this, 3)),
					BorderRadius = 10,
					Cursor = Cursors.Hand
				};
				modeButton.Click += async (object? _, EventArgs _) => await ApplyBatteryLimitModeAsync(mode, modeLimits[mode]);
				_batteryModeButtons[mode] = modeButton;
				modePanel.Controls.Add(modeButton, mi, 0);
			}
			tableLayoutPanel.Controls.Add(modePanel, 1, 0);
			tableLayoutPanel.SetColumnSpan(modePanel, 2);
			RefreshBatteryModeButtons();
			MechrevoHw? hwForBattery = Program.hw;
			if (hwForBattery != null)
			{
				hwForBattery.BatteryProtectionChanged += OnBatteryProtectionChanged;
			ChargeLimitMonitor.VerdictChanged += OnChargeLimitVerdictChanged;
			}
			Activated += async (object? _, EventArgs _) =>
			{
				try
				{
					RefreshThemeButtons();
					MechrevoHw? hwActivated = Program.hw;
					if (hwActivated != null)
					{
						await hwActivated.RequestBatteryProtectionAsync();
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Battery protection read failed: " + ex.Message);
				}
			};
		}'''
new1 = '''		// 40 系：三档模式（长效 100% / 平衡 80% / 工作站 60%）。
		// 50 系：滑块（无极）+ 观察实际充电行为；不受控（ineffective）时自动回退三档。
		if (!IsRtx50SeriesGpu())
		{
			BuildChargeLimitTieredMode(tableLayoutPanel);
			MechrevoHw? hwForBattery = Program.hw;
			if (hwForBattery != null)
			{
				hwForBattery.BatteryProtectionChanged += OnBatteryProtectionChanged;
			}
			Activated += async (object? _, EventArgs _) =>
			{
				try
				{
					RefreshThemeButtons();
					MechrevoHw? hwActivated = Program.hw;
					if (hwActivated != null)
					{
						await hwActivated.RequestBatteryProtectionAsync();
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Battery protection read failed: " + ex.Message);
				}
			};
		}
		ChargeLimitMonitor.VerdictChanged += OnChargeLimitVerdictChanged;'''
print('[if-true]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) 滑块写入后 50 系 Arm 观察
old2 = '''		batteryTimer.Tick += (object? _, EventArgs _) =>
		{
			batteryTimer.Stop();
			BatteryControl.ApplyChargeLimitFromUserGesture(sliderBattery.Value);
		};'''
new2 = '''		batteryTimer.Tick += (object? _, EventArgs _) =>
		{
			batteryTimer.Stop();
			if (BatteryControl.ApplyChargeLimitFromUserGesture(sliderBattery.Value) && IsRtx50SeriesGpu())
			{
				ChargeLimitMonitor.Arm(sliderBattery.Value);
			}
		};'''
print('[tick-arm]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

# 3) OnChargeLimitVerdictChanged(Ineffective) 增加 50 系滑块 -> 动态回退三档
old3 = '''		if (verdict == ChargeLimitVerdict.Ineffective)
		{
			ToastForm.ShowFailure(Strings.ChargeLimitIneffective);
			RefreshBatteryModeButtons();
			return;
		}'''
new3 = '''		if (verdict == ChargeLimitVerdict.Ineffective)
		{
			ToastForm.ShowFailure(Strings.ChargeLimitIneffective);
			// 50 系滑块状态：动态切换为三档模式
			if (_batteryModeButtons == null && sliderBattery.Parent is TableLayoutPanel host)
			{
				BuildChargeLimitTieredMode(host);
			}
			else
			{
				RefreshBatteryModeButtons();
			}
			return;
		}'''
print('[ineffective]', t.count(old3))
assert t.count(old3) == 1
t = t.replace(old3, new3)

# 4) 新增 BuildChargeLimitTieredMode 方法（插在 OnChargeLimitVerdictChanged 前）
old4 = '''	private void OnChargeLimitVerdictChanged(ChargeLimitVerdict verdict)
	{'''
new4 = '''	private void BuildChargeLimitTieredMode(TableLayoutPanel host)
	{
		host.Controls.Remove(sliderBattery);
		_batteryLimitValue.Visible = false;
		string[] modeLabels = new string[3] { Strings.ChargeModeLongevity, Strings.ChargeModeBalanced, Strings.ChargeModeWorkstation };
		int[] modeLimits = new int[3] { 100, 80, 60 };
		TableLayoutPanel modePanel = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 1,
			Dock = DockStyle.Fill,
			AutoSize = false,
			Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 20), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 20), ResponsiveLayout.LogicalToDevice(this, 2)),
			Padding = Padding.Empty
		};
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
		_batteryModeButtons = new RButton[3];
		for (int mi = 0; mi < 3; mi++)
		{
			int mode = mi;
			RButton modeButton = new RButton
			{
				Text = modeLabels[mode],
				Tag = "theme_mode",
				FlatStyle = FlatStyle.Flat,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.SurfaceRaised,
				Dock = DockStyle.Fill,
				MinimumSize = new Size(ResponsiveLayout.LogicalToDevice(this, 80), ResponsiveLayout.LogicalToDevice(this, 20)),
				Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 3), ResponsiveLayout.LogicalToDevice(this, 6), ResponsiveLayout.LogicalToDevice(this, 3)),
				BorderRadius = 10,
				Cursor = Cursors.Hand
			};
			modeButton.Click += async (object? _, EventArgs _) => await ApplyBatteryLimitModeAsync(mode, modeLimits[mode]);
			_batteryModeButtons[mode] = modeButton;
			modePanel.Controls.Add(modeButton, mi, 0);
		}
		host.Controls.Add(modePanel, 1, 0);
		host.SetColumnSpan(modePanel, 2);
		RefreshBatteryModeButtons();
	}

	private void OnChargeLimitVerdictChanged(ChargeLimitVerdict verdict)
	{'''
print('[method-insert]', t.count(old4))
assert t.count(old4) == 1
t = t.replace(old4, new4)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
