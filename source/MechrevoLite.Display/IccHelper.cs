using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MechrevoLite.Display;

/// <summary>自定义校色 ICC：直接经 Windows 颜色管理（WCS）关联/解除，不经由 GCU。</summary>
public static class IccHelper
{
	private const uint WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER = 1;
	private const string IcmRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
	private const string SpoolColorDir = @"C:\Windows\System32\spool\drivers\color";
	private static readonly string UserColorDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		@"Microsoft\Windows\Color");

	[DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool WcsAssociateColorProfileWithDevice(uint scope, string profileName, string deviceName);

	[DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool WcsDisassociateColorProfileFromDevice(uint scope, string profileName, string deviceName);

	/// <summary>自定义 ICC 目录（放在用户数据目录，随设置备份/升级保留）。</summary>
	public static string CustomIccDir => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite", "customICC");

	/// <summary>旧版本放在程序根目录的 customICC（只用于一次性迁移）。</summary>
	private static string LegacyCustomIccDir => Path.Combine(AppContext.BaseDirectory, "customICC");

	public static void EnsureCustomIccDir()
	{
		try
		{
			Directory.CreateDirectory(CustomIccDir);
			MigrateLegacyCustomIccOnce();
		}
		catch
		{
		}
	}
	/// <summary>旧版 customICC（程序目录）里的文件迁移到用户数据目录；只做一次，失败不影响启动。</summary>
	private static void MigrateLegacyCustomIccOnce()
	{
		try
		{
			string legacy = LegacyCustomIccDir;
			if (!Directory.Exists(legacy) || string.Equals(legacy.TrimEnd('\\'), CustomIccDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (Directory.EnumerateFiles(CustomIccDir, "*.ic*").FirstOrDefault() != null || Directory.EnumerateFiles(legacy, "*.ic*").FirstOrDefault() == null)
			{
				return;
			}
			foreach (string file in Directory.EnumerateFiles(legacy))
			{
				string dest = Path.Combine(CustomIccDir, Path.GetFileName(file));
				if (!File.Exists(dest))
				{
					File.Copy(file, dest, overwrite: false);
				}
			}
		}
		catch
		{
		}
	}


	/// <summary>列出 customICC 目录下的全部 .icc/.icm 文件名（排序）。</summary>
	public static List<string> ListCustomIccFiles()
	{
		EnsureCustomIccDir();
		List<string> list = new List<string>();
		try
		{
			foreach (string file in Directory.EnumerateFiles(CustomIccDir, "*.icc"))
			{
				list.Add(Path.GetFileName(file));
			}
			foreach (string file in Directory.EnumerateFiles(CustomIccDir, "*.icm"))
			{
				list.Add(Path.GetFileName(file));
			}
		}
		catch
		{
		}
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	/// <summary>导入 ICC 到 customICC 目录，返回落地后的文件名；同名自动加序号。</summary>
	public static string? ImportIcc(string sourcePath)
	{
		EnsureCustomIccDir();
		string fileName = Path.GetFileName(sourcePath);
		string dest = Path.Combine(CustomIccDir, fileName);
		try
		{
			if (string.Equals(sourcePath, dest, StringComparison.OrdinalIgnoreCase))
			{
				return fileName;
			}
			if (File.Exists(dest))
			{
				string stem = Path.GetFileNameWithoutExtension(fileName);
				string ext = Path.GetExtension(fileName);
				bool placed = false;
				for (int i = 1; i < 1000; i++)
				{
					string candidate = Path.Combine(CustomIccDir, stem + " (" + i + ")" + ext);
					if (!File.Exists(candidate))
					{
						dest = candidate;
						placed = true;
						break;
					}
				}
				if (!placed)
				{
					return null;
				}
			}
			File.Copy(sourcePath, dest, overwrite: false);
			return Path.GetFileName(dest);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ICC 导入失败: " + ex.Message);
			return null;
		}
	}

	/// <summary>把指定 ICC 文件关联到笔记本内屏（不走 GCU）。</summary>
	public static bool ApplyIcc(string profilePath)
	{
		try
		{
			return ApplyIccCore(profilePath);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
			return false;
		}
	}

	private static bool ApplyIccCore(string profilePath)
	{
		string? device = ScreenNative.FindLaptopScreen(log: true);
		if (string.IsNullOrEmpty(device) || !File.Exists(profilePath))
		{
			Logger.WriteLine("ICC 应用失败：设备或文件不可用");
			return false;
		}
		// 用户配置文件标记：复制到用户颜色目录 + HKCU RegisteredProfiles 空子键，
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
			using (RegistryKey? sub = Registry.CurrentUser.CreateSubKey(IcmRoot + @"\RegisteredProfiles\" + fileName))
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
		string spoolPath = Path.Combine(SpoolColorDir, fileName);
		try
		{
			if (!File.Exists(spoolPath))
			{
				File.Copy(profilePath, spoolPath, overwrite: false);
			}
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\RegisteredProfiles", writable: true))
			{
				key?.SetValue(fileName, spoolPath, RegistryValueKind.String);
			}
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\ProfileAssociations\Display\DISPLAY", writable: true))
			{
				key?.SetValue("ICMProfile", fileName, RegistryValueKind.String);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("自定义 ICC 注册表写入异常: " + ex.Message);
		}
		// 直接写 HKCU 显示器关联槽（MultiString 完整文件名）——设置-颜色管理页显示配置的关键。
		// WcsAssociate 会把槽写成逐字符数组导致设置页丢失配置文件，故弃用。
		try
		{
			using (RegistryKey? pa = Registry.CurrentUser.CreateSubKey(IcmRoot + @"\ProfileAssociations\Display\{4d36e96e-e325-11ce-bfc1-08002be10318}"))
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
		}
	}

	/// <summary>取消所有自定义 ICC：对 customICC 目录内每个配置文件逐个解除关联。</summary>
	public static void ClearAllIcc()
	{
		try
		{
			ClearAllIccCore();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("取消自定义 ICC 异常: " + ex.Message);
		}
	}

	private static void ClearAllIccCore()
	{
		string? device = ScreenNative.FindLaptopScreen(log: true);
		if (string.IsNullOrEmpty(device))
		{
			return;
		}
		EnsureCustomIccDir();
		// 不遍历目录：只清理当前应用的自定义 ICC（AppConfig calib_custom_icc 记录文件名）
		string? appliedName = AppConfig.GetString("calib_custom_icc");
		try
		{
			using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\ProfileAssociations\Display\DISPLAY", writable: true))
			{
				key?.SetValue("ICMProfile", "", RegistryValueKind.String);
			}
			if (!string.IsNullOrEmpty(appliedName))
			{
				using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\RegisteredProfiles", writable: true))
				{
					key?.DeleteValue(appliedName, throwOnMissingValue: false);
				}
				Registry.CurrentUser.DeleteSubKey(IcmRoot + @"\RegisteredProfiles\" + appliedName, throwOnMissingSubKey: false);
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
		// 按当前应用文件名精确匹配删除关联槽（不删子树，避免误伤外屏/其他显示器关联）
		try
		{
			using (RegistryKey? pa = Registry.CurrentUser.OpenSubKey(IcmRoot + @"\ProfileAssociations\Display\{4d36e96e-e325-11ce-bfc1-08002be10318}", writable: true))
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
		}
		Logger.WriteLine("取消自定义 ICC 完成");
	}
}
