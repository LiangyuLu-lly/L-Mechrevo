# -*- coding: utf-8 -*-
# ClearAllIcc 改为不遍历：只清当前应用的 ICC（AppConfig calib_custom_icc）+ 整个删 {4d36e96e} 槽子树
import io

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'
t = io.open(p, encoding='utf-8').read()

old1 = '''		// 反向清理注册表：解除 DISPLAY 槽 + 移除以文件名为键的注册项 + 删除用户配置标记
		try
		{
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\DISPLAY", writable: true))
			{
				key?.SetValue("ICMProfile", "", RegistryValueKind.String);
			}
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\RegisteredProfiles", writable: true))
			{
				if (key != null)
				{
					foreach (string file in Directory.EnumerateFiles(CustomIccDir))
					{
						string name = Path.GetFileName(file);
						if (key.GetValue(name) != null)
						{
							key.DeleteValue(name);
						}
						using (RegistryKey? sub = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\RegisteredProfiles\\" + name))
						{
							if (sub != null)
							{
								Registry.CurrentUser.DeleteSubKey(IcmRoot + @"\\RegisteredProfiles\\" + name, throwOnMissingSubKey: false);
							}
						}
						try
						{
							string userPath = Path.Combine(UserColorDir, name);
							if (File.Exists(userPath))
							{
								File.Delete(userPath);
							}
						}
						catch
						{
							// 用户目录文件删除失败不阻断（可能被系统占用）
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 注册表清理异常: " + ex.Message);
		}
		// 直接清空 HKCU 显示器关联槽（替代 WcsDisassociate——它清不干净且语义不清）
		try
		{
			using (RegistryKey? pa = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\{4d36e96e-e325-11ce-bfc1-08002be10318}", writable: true))
			{
				if (pa != null)
				{
					foreach (string sub in pa.GetSubKeyNames())
					{
						if (sub.StartsWith("000"))
						{
							using (RegistryKey? slot = pa.OpenSubKey(sub, writable: true))
							{
								if (slot != null && slot.GetValue("ICMProfile") != null)
								{
									slot.DeleteValue("ICMProfile");
								}
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 关联槽清理异常: " + ex.Message);
		}'''
new1 = '''		// 不遍历目录：只清理当前应用的自定义 ICC（AppConfig calib_custom_icc 记录文件名）
		string? appliedName = AppConfig.GetString("calib_custom_icc");
		try
		{
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\DISPLAY", writable: true))
			{
				key?.SetValue("ICMProfile", "", RegistryValueKind.String);
			}
			if (!string.IsNullOrEmpty(appliedName))
			{
				using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\RegisteredProfiles", writable: true))
				{
					key?.DeleteValue(appliedName, throwOnMissingValue: false);
				}
				Registry.CurrentUser.DeleteSubKey(IcmRoot + @"\\RegisteredProfiles\\" + appliedName, throwOnMissingSubKey: false);
				try
				{
					string userPath = Path.Combine(UserColorDir, appliedName);
					if (File.Exists(userPath))
					{
						File.Delete(userPath);
					}
				}
				catch
				{
					// 用户目录文件删除失败不阻断（可能被系统占用）
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 注册表清理异常: " + ex.Message);
		}
		// 直接删除当前显示器关联槽子树（不遍历；下次应用时 CreateSubKey 重建）
		try
		{
			using (RegistryKey? display = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display", writable: true))
			{
				display?.DeleteSubKeyTree("{4d36e96e-e325-11ce-bfc1-08002be10318}", throwOnMissingSubKey: false);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 关联槽清理异常: " + ex.Message);
		}'''
print('[clear-no-traverse]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
