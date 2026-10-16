using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MechrevoLite.Hardware;

public class HidDeviceWin : IDisposable
{
	private struct SpDeviceInterfaceData
	{
		public uint cbSize;

		public Guid InterfaceClassGuid;

		public uint Flags;

		public nint Reserved;
	}

	private struct HidpCaps
	{
		public ushort Usage;

		public ushort UsagePage;

		public ushort InputReportByteLength;

		public ushort OutputReportByteLength;

		public ushort FeatureReportByteLength;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public ushort[] Reserved;

		public ushort NumberLinkCollectionNodes;

		public ushort NumberInputButtonCaps;

		public ushort NumberInputValueCaps;

		public ushort NumberInputDataIndices;

		public ushort NumberOutputButtonCaps;

		public ushort NumberOutputValueCaps;

		public ushort NumberOutputDataIndices;

		public ushort NumberFeatureButtonCaps;

		public ushort NumberFeatureValueCaps;

		public ushort NumberFeatureDataIndices;
	}

	private static Guid HidGuid = new Guid(1293833650u, 61807, 4559, 136, 203, 0, 17, 17, 0, 0, 48);

	private const uint DigcfPresent = 2u;

	private const uint DigcfDeviceInterface = 16u;

	private const uint FileShareRead = 1u;

	private const uint FileShareWrite = 2u;

	private const uint GenericRead = 2147483648u;

	private const uint GenericWrite = 1073741824u;

	private const uint OpenExisting = 3u;

	private const int ErrorNoMoreItems = 259;

	private SafeFileHandle? _handle;

	public string Path { get; private set; } = "";

	public ushort UsagePage { get; private set; }

	public ushort Usage { get; private set; }

	public ushort VendorID { get; private set; }

	public ushort ProductID { get; private set; }

	public int InterfaceNumber { get; private set; }

	public ushort OutputReportLength { get; private set; }

	public ushort FeatureReportLength { get; private set; }

	public string DeviceInfo => $"VID={VendorID:X4} PID={ProductID:X4} usage_page=0x{UsagePage:X4} usage=0x{Usage:X4} iface={InterfaceNumber}";

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(nint devInfoSet, nint devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(nint devInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, nint deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, nint deviceInfoData);

	[DllImport("setupapi.dll")]
	private static extern bool SetupDiDestroyDeviceInfoList(nint devInfoSet);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out nint preparsedData);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_FreePreparsedData(nint preparsedData);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidP_GetCaps(nint preparsedData, out HidpCaps capabilities);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, nint lpOverlapped);

	public static List<HidDeviceWin> Enumerate(int vendorIdFilter = 0)
	{
		List<HidDeviceWin> list = new List<HidDeviceWin>();
		nint num = SetupDiGetClassDevs(ref HidGuid, null, IntPtr.Zero, 18u);
		if (num == -1 || num == IntPtr.Zero)
		{
			Logger.WriteLine($"HidEnum: SetupDiGetClassDevs fail err={Marshal.GetLastWin32Error()}");
			return list;
		}
		try
		{
			uint num2 = 0u;
			while (true)
			{
				SpDeviceInterfaceData deviceInterfaceData = new SpDeviceInterfaceData
				{
					cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
				};
				if (!SetupDiEnumDeviceInterfaces(num, IntPtr.Zero, ref HidGuid, num2, ref deviceInterfaceData))
				{
					break;
				}
				if (SetupDiGetDeviceInterfaceDetail(num, ref deviceInterfaceData, IntPtr.Zero, 0u, out var requiredSize, IntPtr.Zero) || Marshal.GetLastWin32Error() == 122)
				{
					nint num3 = Marshal.AllocHGlobal((int)requiredSize);
					try
					{
						Marshal.WriteInt32(num3, 0, (IntPtr.Size == 8) ? 8 : 5);
						if (!SetupDiGetDeviceInterfaceDetail(num, ref deviceInterfaceData, num3, requiredSize, out var _, IntPtr.Zero))
						{
							Logger.WriteLine($"HidEnum: detail fail err={Marshal.GetLastWin32Error()}");
						}
						else
						{
							string path = Marshal.PtrToStringUni(num3 + 4) ?? "";
							HidDeviceWin hidDeviceWin = new HidDeviceWin
							{
								Path = path
							};
							if (hidDeviceWin.Inspect(vendorIdFilter))
							{
								list.Add(hidDeviceWin);
							}
							else
							{
								hidDeviceWin.Dispose();
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal(num3);
					}
				}
				num2++;
			}
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error != 259)
			{
				Logger.WriteLine($"HidEnum: SetupDiEnumDeviceInterfaces({num2}) fail err={lastWin32Error}");
			}
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(num);
		}
		return list;
	}

	private bool Inspect(int vendorIdFilter)
	{
		string[] array = (Path.Split('#').ElementAtOrDefault(1) ?? "").Split('&');
		foreach (string text in array)
		{
			ushort result2;
			int result3;
			if (text.StartsWith("VID_", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(text.Substring(4), NumberStyles.HexNumber, null, out var result))
			{
				VendorID = result;
			}
			else if (text.StartsWith("PID_", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(text.Substring(4), NumberStyles.HexNumber, null, out result2))
			{
				ProductID = result2;
			}
			else if (text.StartsWith("MI_", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.Substring(3), NumberStyles.HexNumber, null, out result3))
			{
				InterfaceNumber = result3;
			}
		}
		if (vendorIdFilter != 0 && VendorID != vendorIdFilter)
		{
			return false;
		}
		int value = 0;
		uint[] array2 = new uint[3] { 3221225472u, 1073741824u, 0u };
		foreach (uint desiredAccess in array2)
		{
			_handle = CreateFile(Path, desiredAccess, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (!_handle.IsInvalid)
			{
				break;
			}
			value = Marshal.GetLastWin32Error();
			_handle.Dispose();
			_handle = null;
		}
		if (_handle == null)
		{
			if (vendorIdFilter == 1165 || VendorID == 1165)
			{
				Logger.WriteLine($"HidEnum: ITE inspect open fail {Path} err={value}");
			}
			return false;
		}
		bool flag = HidD_GetPreparsedData(_handle, out var preparsedData);
		if (flag)
		{
			if (HidP_GetCaps(preparsedData, out var capabilities))
			{
				UsagePage = capabilities.UsagePage;
				Usage = capabilities.Usage;
				OutputReportLength = capabilities.OutputReportByteLength;
				FeatureReportLength = capabilities.FeatureReportByteLength;
			}
			HidD_FreePreparsedData(preparsedData);
		}
		_handle.Dispose();
		_handle = null;
		return flag;
	}

	public virtual bool Open()
	{
		_handle?.Dispose();
		_handle = CreateFile(Path, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		if (_handle.IsInvalid)
		{
			_handle.Dispose();
			_handle = CreateFile(Path, 1073741824u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		}
		if (_handle.IsInvalid)
		{
			_handle.Dispose();
			_handle = null;
			return false;
		}
		return true;
	}

	public virtual bool SetFeature(byte[] report)
	{
		if (_handle != null)
		{
			return HidD_SetFeature(_handle, report, report.Length);
		}
		return false;
	}

	public virtual bool Write(byte[] report)
	{
		if (_handle == null)
		{
			return false;
		}
		if (WriteFile(_handle, report, (uint)report.Length, out var lpNumberOfBytesWritten, IntPtr.Zero))
		{
			return lpNumberOfBytesWritten == report.Length;
		}
		return false;
	}

	public virtual void Dispose()
	{
		_handle?.Dispose();
		_handle = null;
	}
}
