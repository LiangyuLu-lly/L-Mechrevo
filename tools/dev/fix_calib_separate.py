# -*- coding: utf-8 -*-
# 档位与开关分离（修正缩进版）
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

# 1) 开关初始化：用 calib_enabled（兼容旧配置：无键时回退 calib_mode>0）
old1 = '''		int initialCalibMode = ResolveColorCalibrationMode();
		calibSwitch.Checked = initialCalibMode > 0;'''
new1 = '''		int initialCalibMode = ResolveColorCalibrationMode();
		calibSwitch.Checked = AppConfig.Exists("calib_enabled") ? AppConfig.Is("calib_enabled") : initialCalibMode > 0;'''
print('[init-switch]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) handler：删 saved 记忆逻辑，关闭只记 calib_enabled=0、打开用下拉值
old2 = '''				bool requested = calibSwitch.Checked;
				calibSwitch.Enabled = false;
				// 关闭校色时保留用户档位选择（40 系 GCU 无回读，靠记忆恢复）
				if (!requested && AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") > 0)
				{
					AppConfig.Set("calib_mode_saved", AppConfig.Get("calib_mode"));
				}
				if (!requested && ((AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on")))
				{
					// 总开关关闭：清理已应用的自定义 ICC
					try
					{
						await Task.Run(() => IccHelper.ClearAllIcc());

					}
					catch (Exception ex)
					{
						Logger.WriteLine("关闭校色清理 ICC 异常: " + ex.Message);
					}
				}
				int savedMode = AppConfig.Exists("calib_mode_saved") ? AppConfig.Get("calib_mode_saved") : 0;
				if (requested && savedMode > 0)
				{
					// 重新打开：恢复上次档位（消费记忆）
					AppConfig.Set("calib_mode_saved", 0);
					_syncingDisplay = true;
					calibCombo.SelectedIndex = CalibComboIndexFor(savedMode, calibCombo);
					_syncingDisplay = false;
				}
				int value = requested ? (savedMode > 0 ? savedMode : (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1)) : 0;
				bool flag4;
				if (requested && value == 999)
				{
					// 恢复自定义 ICC：不经 GCU，直接经 Windows 颜色管理应用
					string? iccName = AppConfig.GetString("calib_custom_icc");
					string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
					try
					{
						flag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));
					}
					catch (Exception ex)
					{
						Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
						flag4 = false;
					}
				}
				else
				{
					flag4 = await svc.SetColorCalibration(value);
				}
				calibSwitch.Enabled = true;
				if (flag4)
				{
					_lastCalibUi = DateTime.UtcNow;
					AppConfig.Set("calib_mode", value);
				}'''
new2 = '''				bool requested = calibSwitch.Checked;
				calibSwitch.Enabled = false;
				if (!requested && ((AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on")))
				{
					// 总开关关闭：清理已应用的自定义 ICC
					try
					{
						await Task.Run(() => IccHelper.ClearAllIcc());

					}
					catch (Exception ex)
					{
						Logger.WriteLine("关闭校色清理 ICC 异常: " + ex.Message);
					}
				}
				int value = requested ? (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1) : 0;
				bool flag4;
				if (requested && value == 999)
				{
					// 恢复自定义 ICC：不经 GCU，直接经 Windows 颜色管理应用
					string? iccName = AppConfig.GetString("calib_custom_icc");
					string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
					try
					{
						flag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));
					}
					catch (Exception ex)
					{
						Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
						flag4 = false;
					}
				}
				else
				{
					flag4 = await svc.SetColorCalibration(value);
				}
				calibSwitch.Enabled = true;
				if (flag4)
				{
					_lastCalibUi = DateTime.UtcNow;
					// 档位与开关分离：calib_mode 恒存用户档位（关闭时不覆盖，重启后下拉回显）；calib_enabled 存开关
					if (requested)
					{
						AppConfig.Set("calib_mode", value);
						AppConfig.Set("calib_enabled", 1);
					}
					else
					{
						AppConfig.Set("calib_enabled", 0);
					}
				}'''
print('[handler]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

# 3) 下拉切档成功：档位记录 + 隐含打开开关
old3 = '''					if (flag4)
					{
						_lastCalibUi = DateTime.UtcNow;
						AppConfig.Set("calib_mode", value);
						AppConfig.Set("calib_mode_saved", 0);
					}
					else
					{
						_syncingDisplay = true;
						calibCombo.SelectedIndex = acceptedIndex;
						_syncingDisplay = false;
					}'''
new3 = '''					if (flag4)
					{
						_lastCalibUi = DateTime.UtcNow;
						AppConfig.Set("calib_mode", value);
						AppConfig.Set("calib_enabled", 1);
					}
					else
					{
						_syncingDisplay = true;
						calibCombo.SelectedIndex = acceptedIndex;
						_syncingDisplay = false;
					}'''
print('[combo-success]', t.count(old3))
assert t.count(old3) == 1
t = t.replace(old3, new3)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
