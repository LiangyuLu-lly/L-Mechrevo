# -*- coding: utf-8 -*-
# 修复：自定义校色 ICC 文件缺失（重装后 customICC 被清）时，
# 打开总开关/切换下拉回退默认档，避免开关卡死打不开。
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
s = io.open(p, encoding='utf-8-sig').read()

# ---- 修改 1：总开关打开（requested && value == 999）----
old1 = r'''				if (requested && value == 999)
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
				}'''

new1 = r'''				if (requested && value == 999)
				{
					// 恢复自定义 ICC：不经 GCU，直接经 Windows 颜色管理应用
					string? iccName = AppConfig.GetString("calib_custom_icc");
					string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
					if (!string.IsNullOrEmpty(iccPath) && File.Exists(iccPath))
					{
						try
						{
							flag4 = await Task.Run(() => IccHelper.ApplyIcc(iccPath));
						}
						catch (Exception ex)
						{
							Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
							flag4 = false;
						}
					}
					else
					{
						// 自定义 ICC 文件缺失（如重装后被清理）：回退默认档，避免开关无法打开
						Logger.WriteLine("自定义 ICC 文件缺失，打开校色回退默认档: " + (iccName ?? "(空)"));
						value = 1;
						_syncingDisplay = true;
						calibCombo.SelectedIndex = CalibComboIndexFor(1, calibCombo);
						_syncingDisplay = false;
						flag4 = await svc.SetColorCalibration(1);
					}
				}
				else
				{
					flag4 = await svc.SetColorCalibration(value);
				}'''

# ---- 修改 2：下拉切换（value == 999）----
old2 = r'''					if (value == 999)
					{
						// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏
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
					}'''

new2 = r'''					if (value == 999)
					{
						// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏
						string? iccName = AppConfig.GetString("calib_custom_icc");
						string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
						if (!string.IsNullOrEmpty(iccPath) && File.Exists(iccPath))
						{
							try
							{
								flag4 = await Task.Run(() => IccHelper.ApplyIcc(iccPath));
							}
							catch (Exception ex)
							{
								Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
								flag4 = false;
							}
						}
						else
						{
							// 自定义 ICC 文件缺失（如重装后被清理）：回退默认档并弹回下拉
							Logger.WriteLine("自定义 ICC 文件缺失，切换校色回退默认档: " + (iccName ?? "(空)"));
							value = 1;
							flag4 = await Program.service.SetColorCalibration(1);
							if (flag4)
							{
								acceptedIndex = CalibComboIndexFor(1, calibCombo);
							}
						}
					}'''

c1 = s.count(old1)
c2 = s.count(old2)
s = s.replace(old1, new1).replace(old2, new2)
io.open(p, 'w', encoding='utf-8-sig').write(s)
print('patch1 matched:', c1, ' patch2 matched:', c2)
