# -*- coding: utf-8 -*-
# IccHelper：注册表自洽修复（注册项键名=文件名 -> spool 路径 + DISPLAY 槽），
# 使 Windows 设置-颜色配置文件能解析并显示自定义 ICC
import io

t = io.open(r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs', encoding='utf-8').read()

# 1) using 增加 Microsoft.Win32
old1 = 'using System.IO;\nusing System.Runtime.InteropServices;'
new1 = 'using System.IO;\nusing System.Runtime.InteropServices;\nusing Microsoft.Win32;'
n1 = t.count(old1)
print('[using]', n1)
assert n1 == 1
t = t.replace(old1, new1)

# 2) 常量增加
old2 = 'private const uint WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER = 1;'
new2 = 'private const uint WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER = 1;\n\tprivate const string IcmRoot = @"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ICM";\n\tprivate const string SpoolColorDir = @"C:\\Windows\\System32\\spool\\drivers\\color";'
n2 = t.count(old2)
print('[const]', n2)
assert n2 == 1
t = t.replace(old2, new2)

# 3) ApplyIccCore 增加注册表自洽段
old3 = '''		bool ok = WcsAssociateColorProfileWithDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, profilePath, device);
		if (!ok)'''
new3 = '''		// 注册表自洽：注册项键名=文件名 -> spool 完整路径，并写入传统 DISPLAY 关联槽，
		// 否则 Windows 设置-颜色配置文件按关联的文件名找不到注册项，判定无配置文件
		string fileName = Path.GetFileName(profilePath);
		string spoolPath = Path.Combine(SpoolColorDir, fileName);
		try
		{
			if (!File.Exists(spoolPath) || !string.Equals(Path.GetFullPath(profilePath), spoolPath, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(profilePath, spoolPath, overwrite: true);
			}
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\RegisteredProfiles", writable: true))
			{
				key?.SetValue(fileName, spoolPath, RegistryValueKind.String);
			}
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\DISPLAY", writable: true))
			{
				key?.SetValue("ICMProfile", fileName, RegistryValueKind.String);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("自定义 ICC 注册表写入异常: " + ex.Message);
		}
		bool ok = WcsAssociateColorProfileWithDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, profilePath, device);
		if (!ok)'''
n3 = t.count(old3)
print('[apply-reg]', n3)
assert n3 == 1
t = t.replace(old3, new3)

# 4) ClearAllIccCore 增加反向清理
old4 = '''		string? device = ScreenNative.FindLaptopScreen(log: true);
		if (string.IsNullOrEmpty(device))
		{
			return;
		}
		EnsureCustomIccDir();
		int cleared = 0;'''
new4 = '''		string? device = ScreenNative.FindLaptopScreen(log: true);
		if (string.IsNullOrEmpty(device))
		{
			return;
		}
		EnsureCustomIccDir();
		// 反向清理注册表：解除 DISPLAY 槽 + 移除以文件名为键的注册项
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
		}
		int cleared = 0;'''
n4 = t.count(old4)
print('[clear-reg]', n4)
assert n4 == 1
t = t.replace(old4, new4)

io.open(r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs', 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
