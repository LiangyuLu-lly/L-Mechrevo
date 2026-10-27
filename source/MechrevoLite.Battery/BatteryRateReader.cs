using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MechrevoLite.Battery;

internal static class BatteryRateReader
{
	private struct BatteryWaitStatus
	{
		public uint BatteryTag;

		public uint Timeout;

		public uint PowerState;

		public uint LowCapacity;

		public uint HighCapacity;
	}

	private struct BatteryStatus
	{
		public uint PowerState;

		public uint Capacity;

		public uint Voltage;

		public int Rate;
	}

	private const uint IoctlBatteryQueryTag = 2703424u;

	private const uint IoctlBatteryQueryStatus = 2703436u;

	private const int UnknownRateMilliwatts = int.MinValue;

	private const uint GenericRead = 2147483648u;

	private const uint GenericWrite = 1073741824u;

	private const uint FileShareReadWrite = 3u;

	private const uint OpenExisting = 3u;

	private static readonly Guid BatteryInterfaceClass = new Guid("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

	internal static decimal? ReadWatts()
	{
		try
		{
			Task<int?> task = Task.Run((Func<int?>)QueryRateMilliwatts);
			return task.Wait(1000) ? FromMilliwatts(task.Result) : ((decimal?)null);
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("battery-rate", "电池功率读取失败：" + ex.Message);
			return null;
		}
	}

	internal static BatteryStatusReading? ReadStatus()
	{
		try
		{
			Task<BatteryStatusReading> task = Task.Run((Func<BatteryStatusReading>)QueryStatus);
			return task.Wait(1000) ? task.Result : null;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("battery-rate", "电池状态读取失败：" + ex.Message);
			return null;
		}
	}

	internal static BatteryStatusReading FromRaw(uint powerState, uint capacity, uint voltage, int rate)
	{
		return new BatteryStatusReading(powerState, (rate == int.MinValue) ? ((int?)null) : new int?(rate), capacity, voltage);
	}

	private static BatteryStatusReading? QueryStatus()
	{
		BatteryStatusReading? result = null;
		string[] array = BatteryInterfacePaths();
		for (int i = 0; i < array.Length; i++)
		{
			BatteryStatusReading? reading = QueryStatus(array[i]);
			if (reading != null)
			{
				if (reading.RateMilliwatts.HasValue)
				{
					return reading;
				}
				if (result == null)
				{
					result = reading;
				}
			}
		}
		return result;
	}

	private static BatteryStatusReading? QueryStatus(string devicePath)
	{
		nint num = CreateFileW(devicePath, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		if (num == new IntPtr(-1))
		{
			return null;
		}
		try
		{
			uint inBuffer = 0u;
			if (!DeviceIoControl(num, 2703424u, ref inBuffer, 4u, out var outBuffer, 4u, out var bytesReturned, IntPtr.Zero) || outBuffer == 0)
			{
				return null;
			}
			BatteryWaitStatus inBuffer2 = new BatteryWaitStatus
			{
				BatteryTag = outBuffer
			};
			if (!DeviceIoControlStatus(num, 2703436u, ref inBuffer2, (uint)Marshal.SizeOf<BatteryWaitStatus>(), out var outBuffer2, (uint)Marshal.SizeOf<BatteryStatus>(), out bytesReturned, IntPtr.Zero))
			{
				return null;
			}
			return FromRaw(outBuffer2.PowerState, outBuffer2.Capacity, outBuffer2.Voltage, outBuffer2.Rate);
		}
		finally
		{
			CloseHandle(num);
		}
	}

	internal static decimal? FromMilliwatts(int? milliwatts)
	{
		if (milliwatts.HasValue && milliwatts != int.MinValue)
		{
			return (decimal)milliwatts.Value / 1000m;
		}
		return null;
	}

	private static int? QueryRateMilliwatts()
	{
		string[] array = BatteryInterfacePaths();
		for (int i = 0; i < array.Length; i++)
		{
			int? num = QueryRateMilliwatts(array[i]);
			if (num.HasValue && num != int.MinValue)
			{
				return num;
			}
		}
		return null;
	}

	private static int? QueryRateMilliwatts(string devicePath)
	{
		nint num = CreateFileW(devicePath, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		if (num == new IntPtr(-1))
		{
			return null;
		}
		try
		{
			uint inBuffer = 0u;
			if (!DeviceIoControl(num, 2703424u, ref inBuffer, 4u, out var outBuffer, 4u, out var bytesReturned, IntPtr.Zero) || outBuffer == 0)
			{
				return null;
			}
			BatteryWaitStatus inBuffer2 = new BatteryWaitStatus
			{
				BatteryTag = outBuffer
			};
			if (!DeviceIoControlStatus(num, 2703436u, ref inBuffer2, (uint)Marshal.SizeOf<BatteryWaitStatus>(), out var outBuffer2, (uint)Marshal.SizeOf<BatteryStatus>(), out bytesReturned, IntPtr.Zero))
			{
				return null;
			}
			return outBuffer2.Rate;
		}
		finally
		{
			CloseHandle(num);
		}
	}

	private static string[] BatteryInterfacePaths()
	{
		Guid interfaceClassGuid = BatteryInterfaceClass;
		if (CmGetDeviceInterfaceListSize(out var length, ref interfaceClassGuid, null, 0u) != 0 || length == 0)
		{
			return Array.Empty<string>();
		}
		char[] array = new char[length];
		if (CmGetDeviceInterfaceList(ref interfaceClassGuid, null, array, length, 0u) != 0)
		{
			return Array.Empty<string>();
		}
		return new string(array).Split('\0', StringSplitOptions.RemoveEmptyEntries);
	}

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
	private static extern int CmGetDeviceInterfaceListSize(out uint length, ref Guid interfaceClassGuid, string? deviceId, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_ListW")]
	private static extern int CmGetDeviceInterfaceList(ref Guid interfaceClassGuid, string? deviceId, [Out] char[] buffer, uint bufferLength, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint device, uint controlCode, ref uint inBuffer, uint inBufferSize, out uint outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);

	[DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
	private static extern bool DeviceIoControlStatus(nint device, uint controlCode, ref BatteryWaitStatus inBuffer, uint inBufferSize, out BatteryStatus outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);
}
