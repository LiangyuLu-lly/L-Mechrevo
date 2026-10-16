# -*- coding: utf-8 -*-
# 固化：ApplyIcc 补用户配置标记（用户颜色目录 + HKCU RegisteredProfiles 空子键）；
# ClearAllIcc 反向删除标记
import io

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'
t = io.open(p, encoding='utf-8').read()

# 1) 常量增加用户目录
old1 = '\tprivate const string SpoolColorDir = @"C:\\Windows\\System32\\spool\\drivers\\color";'
new1 = old1 + '\n\tprivate static readonly string UserColorDir = Path.Combine(\n\t\tEnvironment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),\n\t\t@"Microsoft\\Windows\\Color");'
print('[const]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) ApplyIccCore：在 spool 复制段后补 用户目录复制 + HKCU 注册子键
old2 = '''		// 注册表自洽：注册项键名=文件名 -> spool 完整路径，并写入传统 DISPLAY 关联槽，
		// 否则 Windows 设置-颜色配置文件按关联的文件名找不到注册项，判定无配置文件
		string fileName = Path.GetFileName(profilePath);'''
new2 = '''		// 用户配置文件标记：复制到用户颜色目录 + HKCU RegisteredProfiles 空子键，
		// 否则 Win11 设置-颜色管理页的配置文件列表为空（找不到配置文件）
		string fileName = Path.GetFileName(profilePath);
		try
		{
			if (!Directory.Exists(UserColorDir))
			{
				Directory.CreateDirectory(UserColorDir);
			}
			string userPath = Path.Combine(UserColorDir, fileName);
			if (!File.Exists(userPath))
			{
				File.Copy(profilePath, userPath, overwrite: false);
			}
			using (RegistryKey? sub = Registry.CurrentUser.CreateSubKey(IcmRoot + @"\\RegisteredProfiles\\" + fileName))
			{
				// 空子键即注册标记
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("自定义 ICC 用户配置标记写入异常: " + ex.Message);
		}
		// 注册表自洽：注册项键名=文件名 -> spool 完整路径，并写入传统 DISPLAY 关联槽，
		// 否则 Windows 设置-颜色配置文件按关联的文件名找不到注册项，判定无配置文件
		string fileName = Path.GetFileName(profilePath);'''
print('[apply-user]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

# 3) ClearAllIccCore：反向清理用户标记（删子键 + 删用户目录文件）
old3 = '''		// 反向清理注册表：解除 DISPLAY 槽 + 移除以文件名为键的注册项
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
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 注册表清理异常: " + ex.Message);
		}'''
new3 = '''		// 反向清理注册表：解除 DISPLAY 槽 + 移除以文件名为键的注册项 + 删除用户配置标记
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
		}'''
print('[clear-user]', t.count(old3))
assert t.count(old3) == 1
t = t.replace(old3, new3)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
