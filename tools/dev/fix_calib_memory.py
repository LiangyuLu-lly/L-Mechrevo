# -*- coding: utf-8 -*-
# 校色档位记忆：关闭总开关时保存档位（calib_mode_saved），打开时恢复（40 系无回读场景）
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

# 1) 关闭分支：清理前保存当前档位
old1 = '''				if (!requested && ((AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on")))
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
				int value = requested ? (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1) : 0;'''
new1 = '''				// 关闭校色时保留用户档位选择（40 系 GCU 无回读，靠记忆恢复）
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
				int value = requested ? (savedMode > 0 ? savedMode : (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1)) : 0;'''
print('[switch-handler]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) 下拉切档成功后消费记忆（清 saved，避免陈旧档位残留）
old2 = '''					if (flag4)
					{
						_lastCalibUi = DateTime.UtcNow;
						AppConfig.Set("calib_mode", value);
					}
					else
					{
						_syncingDisplay = true;
						calibCombo.SelectedIndex = acceptedIndex;
						_syncingDisplay = false;
					}'''
new2 = '''					if (flag4)
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
print('[combo-success]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
