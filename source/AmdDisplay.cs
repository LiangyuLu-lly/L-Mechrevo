using System;
using System.Diagnostics;
using System.IO;
using MechrevoLite;
using Microsoft.Win32;
using PawnIO;

public static class AmdDisplay
{
	private const string DisplayPath0 = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0001\\DAL2_DATA__2_0\\DisplayPath_0";

	private static string _cachedAdjustmentPath;

	private static bool _isPathSearched;

	private static string GetAdjustmentPath()
	{
		if (_isPathSearched)
		{
			return _cachedAdjustmentPath;
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0001\\DAL2_DATA__2_0\\DisplayPath_0", writable: false);
			if (registryKey != null)
			{
				string text = Array.Find(registryKey.GetSubKeyNames(), (string name) => name.StartsWith("EDID_", StringComparison.OrdinalIgnoreCase));
				if (text != null)
				{
					_cachedAdjustmentPath = $"HKEY_LOCAL_MACHINE\\{"SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0001\\DAL2_DATA__2_0\\DisplayPath_0"}\\{text}\\Adjustment";
				}
			}
		}
		catch
		{
		}
		finally
		{
			_isPathSearched = true;
		}
		return _cachedAdjustmentPath;
	}

	public static bool IsOledPowerOptimization()
	{
		if (!AppConfig.IsOLED() || !CpuInfo.IsAMD)
		{
			return false;
		}
		try
		{
			string adjustmentPath = GetAdjustmentPath();
			if (adjustmentPath == null)
			{
				return false;
			}
			if (Registry.GetValue(adjustmentPath, "DAL_SCE_Settings", null) is byte[] array && array.Length >= 5)
			{
				return (array[4] & 2) != 0;
			}
		}
		catch
		{
			Logger.WriteLine("Can't check AMD OLED Optimization flag");
		}
		return false;
	}

	public static void RunAdrenaline()
	{
		string text = "C:\\Program Files\\AMD\\CNext\\CNext\\RadeonSoftware.exe";
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages\\AdvancedMicroDevicesInc-2.AMDRadeonSoftware_0a9344xs7nr4m");
		if (File.Exists(text))
		{
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true,
				Verb = "runas"
			});
		}
		else if (Directory.Exists(path))
		{
			string arguments = "shell:AppsFolder\\AdvancedMicroDevicesInc-2.AMDRadeonSoftware_0a9344xs7nr4m!App";
			Process.Start(new ProcessStartInfo("explorer.exe", arguments)
			{
				UseShellExecute = true
			});
		}
		else
		{
			Logger.WriteLine("AMD Radeon Software is not installed.");
		}
	}
}
