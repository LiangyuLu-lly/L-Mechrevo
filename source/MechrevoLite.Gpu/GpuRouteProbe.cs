using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using MechrevoLite.Display;

namespace MechrevoLite.Gpu;

public static class GpuRouteProbe
{
	private const int ErrorInsufficientBuffer = 122;

	private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

	private const uint CmGetIdListFilterPresent = 256u;

	private const uint CmGetIdListFilterClass = 512u;

	private const int CrSuccess = 0;

	private const int CrBufferSmall = 26;

	private const uint DnStarted = 8u;

	private const uint CmProbDisabled = 22u;

	internal static Func<GpuRouteReadback>? Override { get; set; }

	public static GpuRouteReadback Read()
	{
		return Override?.Invoke() ?? new GpuRouteReadback(ReadInternalPanelAdapter(), ReadDgpuPresence());
	}

	public static DgpuPresence ReadPresence()
	{
		return Override?.Invoke().Dgpu ?? ReadDgpuPresence();
	}

	private static PanelAdapterKind ReadInternalPanelAdapter()
	{
		try
		{
			for (int i = 0; i < 3; i++)
			{
				if (DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ALL_PATHS, out var numPathArrayElements, out var numModeInfoArrayElements) != 0)
				{
					return PanelAdapterKind.Unknown;
				}
				DisplayNative.DISPLAYCONFIG_PATH_INFO[] array = new DisplayNative.DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
				DisplayNative.DISPLAYCONFIG_MODE_INFO[] modeInfoArray = new DisplayNative.DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];
				switch (DisplayNative.QueryDisplayConfig(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ALL_PATHS, ref numPathArrayElements, array, ref numModeInfoArrayElements, modeInfoArray, IntPtr.Zero))
				{
				default:
					return PanelAdapterKind.Unknown;
				case 0:
				{
					PanelAdapterKind panelAdapterKind = PanelAdapterKind.Unknown;
					HashSet<(uint, int, uint)> hashSet = new HashSet<(uint, int, uint)>();
					for (int j = 0; j < numPathArrayElements; j++)
					{
						DisplayNative.DISPLAYCONFIG_PATH_TARGET_INFO targetInfo = array[j].targetInfo;
						if (!targetInfo.targetAvailable || !GpuRouteInference.IsInternalOutput(targetInfo.outputTechnology) || !hashSet.Add((targetInfo.adapterId.LowPart, targetInfo.adapterId.HighPart, targetInfo.id)))
						{
							continue;
						}
						DisplayNative.DISPLAYCONFIG_ADAPTER_NAME adapterName = new DisplayNative.DISPLAYCONFIG_ADAPTER_NAME
						{
							header = 
							{
								type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME,
								size = (uint)Marshal.SizeOf<DisplayNative.DISPLAYCONFIG_ADAPTER_NAME>(),
								adapterId = targetInfo.adapterId,
								id = 0u
							}
						};
						if (DisplayNative.DisplayConfigGetDeviceInfo(ref adapterName) != 0)
						{
							continue;
						}
						PanelAdapterKind panelAdapterKind2 = GpuRouteInference.ClassifyAdapterPath(adapterName.adapterDevicePath);
						if (panelAdapterKind2 != PanelAdapterKind.Unknown)
						{
							if (panelAdapterKind == PanelAdapterKind.Unknown)
							{
								panelAdapterKind = panelAdapterKind2;
							}
							else if (panelAdapterKind != panelAdapterKind2)
							{
								return PanelAdapterKind.Unknown;
							}
						}
					}
					return panelAdapterKind;
				}
				case 122:
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("gpu-route-ccd", "GPU route: internal panel query failed: " + ex.Message, 60000);
		}
		return PanelAdapterKind.Unknown;
	}

	private static DgpuPresence ReadDgpuPresence()
	{
		try
		{
			string[] array = (from id in PresentDisplayDeviceIds()
				where id.StartsWith("PCI\\VEN_10DE", StringComparison.OrdinalIgnoreCase)
				select id).ToArray();
			if (array.Length == 0)
			{
				return DgpuPresence.Absent;
			}
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			string[] array2 = array;
			foreach (string deviceId in array2)
			{
				uint status;
				uint problemNumber;
				if (CM_Locate_DevNodeW(out var devInst, deviceId, 0u) != 0)
				{
					flag3 = true;
				}
				else if (CM_Get_DevNode_Status(out status, out problemNumber, devInst, 0u) != 0)
				{
					flag3 = true;
				}
				else if ((status & 8) != 0)
				{
					flag = true;
				}
				else if (problemNumber == 22)
				{
					flag2 = true;
				}
				else
				{
					flag3 = true;
				}
			}
			if (flag)
			{
				return DgpuPresence.Present;
			}
			if (flag2 && !flag3)
			{
				return DgpuPresence.Disabled;
			}
			return DgpuPresence.Unknown;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("gpu-route-cfgmgr", "GPU route: device presence query failed: " + ex.Message, 60000);
			return DgpuPresence.Unknown;
		}
	}

	private static string[] PresentDisplayDeviceIds()
	{
		for (int i = 0; i < 3; i++)
		{
			if (CM_Get_Device_ID_List_SizeW(out var length, "{4d36e968-e325-11ce-bfc1-08002be10318}", 768u) != 0)
			{
				throw new Win32Exception("CM_Get_Device_ID_List_Size failed");
			}
			if (length <= 1)
			{
				return Array.Empty<string>();
			}
			char[] array = new char[length];
			int num = CM_Get_Device_ID_ListW("{4d36e968-e325-11ce-bfc1-08002be10318}", array, length, 768u);
			switch (num)
			{
			case 0:
				return new string(array).Split('\0', StringSplitOptions.RemoveEmptyEntries);
			case 26:
				continue;
			}
			throw new Win32Exception($"CM_Get_Device_ID_List failed: 0x{num:X}");
		}
		throw new Win32Exception("CM_Get_Device_ID_List kept growing");
	}

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint bufferLength, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

	[DllImport("cfgmgr32.dll")]
	private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);
}
