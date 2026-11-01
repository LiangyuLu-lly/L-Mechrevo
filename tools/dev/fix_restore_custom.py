# -*- coding: utf-8 -*-
# 打开总开关恢复 999（自定义）时走 ApplyIcc，不走 SetColorCalibration(999)（GCU 无效模式导致回弹）
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

old1 = '''				int value = requested ? (savedMode > 0 ? savedMode : (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1)) : 0;
				bool flag4 = await svc.SetColorCalibration(value);
				calibSwitch.Enabled = true;'''
new1 = '''				int value = requested ? (savedMode > 0 ? savedMode : (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1)) : 0;
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
				calibSwitch.Enabled = true;'''
print('[restore-999]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
