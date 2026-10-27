using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace MechrevoLite.Gpu;

public static class GpuAdapterReader
{
	public const string DisplayClassPath = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	public static IReadOnlyList<GpuAdapter> ReadFromRegistry(out bool enumerationAvailable)
	{
		List<GpuAdapter> list = new List<GpuAdapter>();
		enumerationAvailable = false;
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}");
			if (registryKey == null)
			{
				return list;
			}
			enumerationAvailable = true;
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string text in subKeyNames)
			{
				if (!text.All(char.IsAsciiDigit))
				{
					continue;
				}
				using RegistryKey registryKey2 = registryKey.OpenSubKey(text);
				if (registryKey2 != null)
				{
					string text2 = registryKey2.GetValue("DriverDesc")?.ToString() ?? "";
					string text3 = ReadHardwareId(registryKey2);
					TryParsePciIds(text3, out string vendorId, out string deviceId);
					if (text2.Length != 0 || text3 != null)
					{
						list.Add(new GpuAdapter(text2, vendorId, deviceId));
					}
				}
			}
			return list;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU adapter enumeration failed: " + ex.Message);
			enumerationAvailable = false;
			return new List<GpuAdapter>();
		}
	}

	private static string? ReadHardwareId(RegistryKey adapterKey)
	{
		string text = adapterKey.GetValue("MatchingDeviceId")?.ToString();
		if (text != null && text.Length > 0)
		{
			return text;
		}
		if (adapterKey.GetValue("HardwareID") is string[] array && array.Length > 0)
		{
			return array[0];
		}
		return null;
	}

	public static bool TryParsePciIds(string? hardwareId, out string? vendorId, out string? deviceId)
	{
		vendorId = ExtractToken(hardwareId, "VEN_");
		deviceId = ExtractToken(hardwareId, "DEV_");
		if (vendorId != null)
		{
			return deviceId != null;
		}
		return false;
	}

	private static string? ExtractToken(string? text, string prefix)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		int num = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return null;
		}
		num += prefix.Length;
		int i;
		for (i = num; i < text.Length && Uri.IsHexDigit(text[i]); i++)
		{
		}
		if (i - num < 4)
		{
			return null;
		}
		return text.Substring(num, 4).ToUpperInvariant();
	}
}
