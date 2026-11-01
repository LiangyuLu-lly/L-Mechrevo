# -*- coding: utf-8 -*-
# 核心修复：ApplyIcc 弃用 WcsAssociate（破坏槽位格式），改直接写 HKCU 关联槽 MultiString 完整文件名；
# ClearAllIcc 弃用 WcsDisassociate，改直接清槽
import io

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'
t = io.open(p, encoding='utf-8').read()

# 1) ApplyIccCore：spool 复制改为"已存在则跳过"（避免被系统占用报错）
old1 = '''			if (!File.Exists(spoolPath) || !string.Equals(Path.GetFullPath(profilePath), spoolPath, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(profilePath, spoolPath, overwrite: true);
			}'''
new1 = '''			if (!File.Exists(spoolPath))
			{
				File.Copy(profilePath, spoolPath, overwrite: false);
			}'''
print('[spool-skip]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) ApplyIccCore：注册表自洽段后，WcsAssociate 替换为直接写 HKCU 关联槽 MultiString
old2 = '''		bool ok = WcsAssociateColorProfileWithDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, profilePath, device);
		if (!ok)
		{
			Logger.WriteLine("WcsAssociateColorProfileWithDevice 失败: " + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
		}
		else
		{
			Logger.WriteLine("自定义 ICC 已应用: " + Path.GetFileName(profilePath));
		}
		return ok;'''
new2 = '''		// 直接写 HKCU 显示器关联槽（MultiString 完整文件名）——设置-颜色管理页显示配置的关键。
		// WcsAssociate 会把槽写成逐字符数组导致设置页丢失配置文件，故弃用。
		try
		{
			using (RegistryKey? pa = Registry.CurrentUser.CreateSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\{4d36e96e-e325-11ce-bfc1-08002be10318}"))
			{
				for (int i = 0; i < 9; i++)
				{
					using (RegistryKey? slot = pa?.CreateSubKey("000" + i))
					{
						slot?.SetValue("ICMProfile", new[] { fileName }, RegistryValueKind.MultiString);
					}
				}
			}
			Logger.WriteLine("自定义 ICC 已应用: " + fileName);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("自定义 ICC 关联槽写入异常: " + ex.Message);
			return false;
		}'''
print('[apply-slot]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

# 3) ClearAllIccCore：WcsDisassociate 循环替换为直接清槽
old3 = '''		int cleared = 0;
		try
		{
			foreach (string file in Directory.EnumerateFiles(CustomIccDir))
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext != ".icc" && ext != ".icm")
				{
					continue;
				}
				if (WcsDisassociateColorProfileFromDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, file, device))
				{
					cleared++;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消 ICC 异常: " + ex.Message);
		}
		Logger.WriteLine("取消自定义 ICC 完成，解除 " + cleared + " 个");'''
new3 = '''		// 直接清空 HKCU 显示器关联槽（替代 WcsDisassociate——它清不干净且语义不清）
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
		}
		Logger.WriteLine("取消自定义 ICC 完成");'''
print('[clear-slot]', t.count(old3))
assert t.count(old3) == 1
t = t.replace(old3, new3)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
