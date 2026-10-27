using System;
using System.Drawing;
using System.Threading;
using Microsoft.Win32;

namespace MechrevoLite.Helpers;

public static class DynamicLightingHelper
{
	public enum DynamicLightingEffect
	{
		Solid = 0,
		Breathing = 1,
		Rainbow = 2,
		Wave = 4,
		Wheel = 5,
		Gradient = 6
	}

	private const string LightingKey = "HKEY_CURRENT_USER\\Software\\Microsoft\\Lighting";

	private const string LightingValue = "AmbientLightingEnabled";

	public static bool IsEnabled()
	{
		if (Environment.OSVersion.Version.Build < 22000)
		{
			return false;
		}
		if (ProcessHelper.IsRunningAsSystem())
		{
			return false;
		}
		Logger.WriteLine("Dynamic lighting status: " + Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Lighting", "AmbientLightingEnabled", 0));
		return (int?)Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Lighting", "AmbientLightingEnabled", 0) > 0;
	}

	public static void SetRegStatus(int status = 1)
	{
		try
		{
			Registry.SetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Lighting", "AmbientLightingEnabled", status, RegistryValueKind.DWord);
			SetDynamicLightingOnAllDevices(status);
			Logger.WriteLine($"Dynamic lighting: {status}");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Error setting value for dynamic lighting: " + ex.Message);
		}
	}

	private static void WriteLightingValue(string name, object value, RegistryValueKind kind)
	{
		Registry.SetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Lighting", name, value, kind);
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Lighting\\Devices", writable: true);
		if (registryKey == null)
		{
			return;
		}
		string[] subKeyNames = registryKey.GetSubKeyNames();
		foreach (string name2 in subKeyNames)
		{
			using RegistryKey registryKey2 = registryKey.OpenSubKey(name2, writable: true);
			registryKey2?.SetValue(name, value, kind);
		}
	}

	private static int ToDlColorDword(Color c)
	{
		return (c.A << 24) | (c.B << 16) | (c.G << 8) | c.R;
	}

	private static void SetDynamicLightingOnAllDevices(int status = 1)
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Lighting\\Devices", writable: true);
		if (registryKey == null)
		{
			return;
		}
		string[] subKeyNames = registryKey.GetSubKeyNames();
		foreach (string name in subKeyNames)
		{
			using RegistryKey registryKey2 = registryKey.OpenSubKey(name, writable: true);
			if (registryKey2 != null && registryKey2.GetValue("AmbientLightingEnabled") != null)
			{
				registryKey2.SetValue("AmbientLightingEnabled", status, RegistryValueKind.DWord);
			}
		}
	}

	public static void Init()
	{
		if (AppConfig.IsDynamicLightingInit() && Environment.OSVersion.Version.Build >= 22000 && !IsEnabled())
		{
			SetRegStatus();
			Thread.Sleep(1000);
			SetRegStatus(0);
		}
	}

	public static void OpenSettings()
	{
		ProcessHelper.RunCMD("explorer", "ms-settings:personalization-lighting");
	}
}
