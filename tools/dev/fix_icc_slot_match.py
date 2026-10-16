# -*- coding: utf-8 -*-
# ClearAllIcc 槽清理：DeleteSubKeyTree 会误伤外屏 -> 改为按 appliedName 精确匹配删除
import io

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'
t = io.open(p, encoding='utf-8').read()

old1 = '''		// 直接删除当前显示器关联槽子树（不遍历；下次应用时 CreateSubKey 重建）
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
new1 = '''		// 按当前应用文件名精确匹配删除关联槽（不删子树，避免误伤外屏/其他显示器关联）
		try
		{
			using (RegistryKey? pa = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\\ProfileAssociations\\Display\\{4d36e96e-e325-11ce-bfc1-08002be10318}", writable: true))
			{
				if (pa != null && !string.IsNullOrEmpty(appliedName))
				{
					foreach (string sub in pa.GetSubKeyNames())
					{
						if (!sub.StartsWith("000"))
						{
							continue;
						}
						using (RegistryKey? slot = pa.OpenSubKey(sub, writable: true))
						{
							if (slot == null)
							{
								continue;
							}
							object? v = slot.GetValue("ICMProfile");
							string? first = v is string[] arr && arr.Length > 0 ? arr[0] : v as string;
							if (!string.IsNullOrEmpty(first) && string.Equals(first, appliedName, StringComparison.OrdinalIgnoreCase))
							{
								slot.DeleteValue("ICMProfile");
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
print('[match-slot]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
