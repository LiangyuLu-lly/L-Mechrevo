# -*- coding: utf-8 -*-
# 修复：校色切离自定义档（下拉或总开关关闭）时自动清理自定义 ICC
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

# 1) 下拉切到非自定义档（else 分支）前清理自定义 ICC
old1 = '''					else
					{
						flag4 = await Program.service.SetColorCalibration(value);
					}'''
new1 = '''					else
					{
						// 切离自定义档：若自定义 ICC 已应用则清理，避免颜色管理页残留
						if (AppConfig.GetBool("calib_custom_on"))
						{
							try
							{
								await Task.Run(() => IccHelper.ClearAllIcc());
								AppConfig.Set("calib_custom_on", false);
							}
							catch (Exception ex)
							{
								Logger.WriteLine("切离自定义档清理 ICC 异常: " + ex.Message);
							}
						}
						flag4 = await Program.service.SetColorCalibration(value);
					}'''
print('[combo-else]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) 总开关关闭（requested=false）时也清理自定义 ICC
old2 = '''				bool requested = calibSwitch.Checked;
				calibSwitch.Enabled = false;
				int value = requested ? (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1) : 0;
				bool flag4 = await svc.SetColorCalibration(value);'''
new2 = '''				bool requested = calibSwitch.Checked;
				calibSwitch.Enabled = false;
				if (!requested && AppConfig.GetBool("calib_custom_on"))
				{
					// 总开关关闭：清理已应用的自定义 ICC
					try
					{
						await Task.Run(() => IccHelper.ClearAllIcc());
						AppConfig.Set("calib_custom_on", false);
					}
					catch (Exception ex)
					{
						Logger.WriteLine("关闭校色清理 ICC 异常: " + ex.Message);
					}
				}
				int value = requested ? (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1) : 0;
				bool flag4 = await svc.SetColorCalibration(value);'''
print('[switch-off]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
