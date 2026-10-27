using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

public static class DeviceHelper
{
	private const string GUID_DEVCLASS_DISPLAY = "{4D36E968-E325-11CE-BFC1-08002BE10318}";

	private const uint CM_GETIDLIST_FILTER_CLASS = 512u;

	private const uint CM_GETIDLIST_FILTER_PRESENT = 256u;

	private const uint DN_HAS_PROBLEM = 1024u;

	private const uint CM_DRP_DEVICEDESC = 1u;

	[DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

	[DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_Device_ID_ListW(string pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

	[DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

	[DllImport("CfgMgr32.dll")]
	private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

	[DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_DevNode_Registry_PropertyW(uint dnDevInst, uint ulProperty, out uint pulRegDataType, char[] Buffer, ref uint pulLength, uint ulFlags);

	private static string ProblemText(uint problem)
	{
		switch (problem)
		{
		case 10u:
			return "Device cannot start";
		case 12u:
			return "Not enough free resources";
		case 22u:
			return "Device is disabled";
		case 28u:
			return "Drivers are not installed";
		case 31u:
		case 39u:
			return "Driver failed to load";
		case 43u:
			return "Device stopped because it reported problems";
		default:
			return "Unknown problem";
		}
	}

	public static string? GetGpuError()
	{
		string result = null;
		uint ulFlags = 768u;
		if (CM_Get_Device_ID_List_SizeW(out var pulLen, "{4D36E968-E325-11CE-BFC1-08002BE10318}", ulFlags) == 0 && pulLen > 1)
		{
			char[] array = new char[pulLen];
			if (CM_Get_Device_ID_ListW("{4D36E968-E325-11CE-BFC1-08002BE10318}", array, pulLen, ulFlags) == 0)
			{
				string[] array2 = new string(array).Split('\0', StringSplitOptions.RemoveEmptyEntries);
				foreach (string text in array2)
				{
					if (CM_Locate_DevNodeW(out var pdnDevInst, text, 0u) == 0 && CM_Get_DevNode_Status(out var pulStatus, out var pulProblemNumber, pdnDevInst, 0u) == 0 && (pulStatus & 0x400) != 0)
					{
						char[] array3 = new char[256];
						uint pulLength = 512u;
						string value = ((CM_Get_DevNode_Registry_PropertyW(pdnDevInst, 1u, out var _, array3, ref pulLength, 0u) == 0) ? new string(array3, 0, (int)pulLength / 2).TrimEnd('\0') : text);
						result = $"{value}\nError {pulProblemNumber}: {ProblemText(pulProblemNumber)}";
					}
				}
			}
		}
		return result;
	}
}
