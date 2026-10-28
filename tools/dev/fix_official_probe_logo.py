# -*- coding: utf-8 -*-
# 1) 共存检查：目录存在性 -> 官方判定（Inno 卸载键 / Appx 包注册状态）
# 2) Logo type 硬条件放宽（有 power/light 证据即可）
import io

def edit(path, old, new, tag):
    t = open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# ---- 1) ProbeOfficialUiInstalled ----
p1 = r'D:\openLM\source\MechrevoLite.Helpers\OfficialConsoleIsolation.cs'
old1 = '''	private static bool ProbeOfficialUiInstalled()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
			if (Directory.Exists(path) && Directory.EnumerateDirectories(path, "CCU.WinUI_*").Any())
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM");
			return Directory.Exists(path2) && Directory.EnumerateFiles(path2, "ControlCenterU.exe", SearchOption.AllDirectories).Any();
		}
		catch
		{
			return false;
		}
	}'''
new1 = '''	private static bool ProbeOfficialUiInstalled()
	{
		// 官方判定：安装注册状态，而不是文件/目录残留。
		// 1) 官方 Inno Setup 卸载键（与安装器 UninstallOfficialConsole 的口径一致）
		try
		{
			string[] uninstallRoots = new string[2]
			{
				@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + OfficialAppId,
				@"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + OfficialAppId
			};
			foreach (string subKey in uninstallRoots)
			{
				if (Registry.LocalMachine.OpenSubKey(subKey) != null)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		// 2) MSIX 包注册状态（AppxAllUserStore 只在包注册时写入，卸载后清除）
		try
		{
			using (RegistryKey? appxRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Appx\\AppxAllUserStore\\Applications"))
			{
				if (appxRoot != null && appxRoot.GetSubKeyNames().Any((string name) => name.StartsWith("CCU.WinUI", StringComparison.OrdinalIgnoreCase)))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		// 3) 传统 OEM 路径兜底（非 WindowsApps 残留目录）
		try
		{
			string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM");
			return Directory.Exists(path2) && Directory.EnumerateFiles(path2, "ControlCenterU.exe", SearchOption.AllDirectories).Any();
		}
		catch
		{
			return false;
		}
	}'''
edit(p1, old1, new1, 'probe-official-ui')

# 追加 OfficialAppId 常量（与安装器 install_script.iss 同 id）
old_const = '''	private const string PackageAumid = "CCU.WinUI_wrbgcf7aesyd8!App";'''
new_const = '''	private const string OfficialAppId = "{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1";

	private const string PackageAumid = "CCU.WinUI_wrbgcf7aesyd8!App";'''
edit(p1, old_const, new_const, 'official-appid-const')

# ---- 2) Logo type 硬条件放宽 ----
p2 = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
old2 = '''			LogoLightStatusSeen |= (flag && !string.IsNullOrWhiteSpace(o["type"]?.ToString()) && (!string.IsNullOrWhiteSpace(o["powerStatus"]?.ToString()) || !string.IsNullOrWhiteSpace(o["power"]?.ToString()) || !string.IsNullOrWhiteSpace(o["light"]?.ToString())));'''
new2 = '''			LogoLightStatusSeen |= (flag && (!string.IsNullOrWhiteSpace(o["powerStatus"]?.ToString()) || !string.IsNullOrWhiteSpace(o["power"]?.ToString()) || !string.IsNullOrWhiteSpace(o["light"]?.ToString())));'''
edit(p2, old2, new2, 'logo-type-relax')

print('ALL OK')
