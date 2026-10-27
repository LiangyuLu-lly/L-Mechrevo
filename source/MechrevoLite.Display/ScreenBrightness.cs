using System;
using System.Management;

namespace MechrevoLite.Display;

public static class ScreenBrightness
{
	internal static Func<int?>? ReadOverride { get; set; }

	internal static Action<int>? WriteOverride { get; set; }

	public static bool TryGet(out int brightness)
	{
		Func<int?> readOverride = ReadOverride;
		if (readOverride != null)
		{
			int? num = readOverride();
			brightness = num.GetValueOrDefault();
			return num.HasValue;
		}
		using ManagementClass managementClass = new ManagementClass("WmiMonitorBrightness")
		{
			Scope = new ManagementScope("\\\\.\\root\\wmi")
		};
		using ManagementObjectCollection managementObjectCollection = managementClass.GetInstances();
		using (ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator())
		{
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				brightness = (byte)managementObject.GetPropertyValue("CurrentBrightness");
				return true;
			}
		}
		brightness = 0;
		return false;
	}

	public static int Get()
	{
		TryGet(out var brightness);
		return brightness;
	}

	public static void Set(int brightness)
	{
		Action<int> writeOverride = WriteOverride;
		if (writeOverride != null)
		{
			writeOverride(brightness);
			return;
		}
		using ManagementClass managementClass = new ManagementClass("WmiMonitorBrightnessMethods")
		{
			Scope = new ManagementScope("\\\\.\\root\\wmi")
		};
		using ManagementObjectCollection managementObjectCollection = managementClass.GetInstances();
		object[] args = new object[2] { 1, brightness };
		foreach (ManagementObject item in managementObjectCollection)
		{
			item.InvokeMethod("WmiSetBrightness", args);
		}
	}

	public static bool TrySet(int brightness)
	{
		Action<int> writeOverride = WriteOverride;
		if (writeOverride != null)
		{
			try
			{
				writeOverride(brightness);
				return true;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Screen brightness write failed: " + ex.Message);
				return false;
			}
		}
		try
		{
			using ManagementClass managementClass = new ManagementClass("WmiMonitorBrightnessMethods")
			{
				Scope = new ManagementScope("\\\\.\\root\\wmi")
			};
			using ManagementObjectCollection managementObjectCollection = managementClass.GetInstances();
			object[] args = new object[2] { 1, brightness };
			bool result = false;
			foreach (ManagementObject item in managementObjectCollection)
			{
				item.InvokeMethod("WmiSetBrightness", args);
				result = true;
			}
			return result;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Screen brightness write failed: " + ex2.Message);
			return false;
		}
	}

	public static int Adjust(int delta)
	{
		int num = Get();
		num = Math.Min(100, Math.Max(0, num + delta));
		Set(num);
		return num;
	}
}
