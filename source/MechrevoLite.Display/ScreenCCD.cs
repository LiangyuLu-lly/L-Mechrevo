using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MechrevoLite.Display;

public class ScreenCCD
{
	private enum DISPLAYCONFIG_DEVICE_INFO_TYPE
	{
		DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
		DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6,
		DISPLAYCONFIG_DEVICE_INFO_GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
		DISPLAYCONFIG_DEVICE_INFO_SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9,
		DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10,
		DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15
	}

	private enum DISPLAYCONFIG_ADVANCED_COLOR_MODE
	{
		DISPLAYCONFIG_ADVANCED_COLOR_MODE_SDR,
		DISPLAYCONFIG_ADVANCED_COLOR_MODE_WCG,
		DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR
	}

	private enum DISPLAYCONFIG_COLOR_ENCODING
	{
		DISPLAYCONFIG_COLOR_ENCODING_RGB,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR444,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR422,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR420,
		DISPLAYCONFIG_COLOR_ENCODING_INTENSITY
	}

	private enum DISPLAYCONFIG_SCALING
	{
		DISPLAYCONFIG_SCALING_IDENTITY = 1,
		DISPLAYCONFIG_SCALING_CENTERED = 2,
		DISPLAYCONFIG_SCALING_STRETCHED = 3,
		DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4,
		DISPLAYCONFIG_SCALING_CUSTOM = 5,
		DISPLAYCONFIG_SCALING_PREFERRED = 128
	}

	private enum DISPLAYCONFIG_ROTATION
	{
		DISPLAYCONFIG_ROTATION_IDENTITY = 1,
		DISPLAYCONFIG_ROTATION_ROTATE90,
		DISPLAYCONFIG_ROTATION_ROTATE180
	}

	private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY
	{
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = -1,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = int.MinValue
	}

	private enum DISPLAYCONFIG_TOPOLOGY_ID
	{
		DISPLAYCONFIG_TOPOLOGY_INTERNAL = 1,
		DISPLAYCONFIG_TOPOLOGY_CLONE = 2,
		DISPLAYCONFIG_TOPOLOGY_EXTEND = 4,
		DISPLAYCONFIG_TOPOLOGY_EXTERNAL = 8
	}

	private enum DISPLAYCONFIG_PATH
	{
		DISPLAYCONFIG_PATH_ACTIVE = 1,
		DISPLAYCONFIG_PATH_PREFERRED_UNSCALED = 4,
		DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 8
	}

	private enum DISPLAYCONFIG_SOURCE_FLAGS
	{
		DISPLAYCONFIG_SOURCE_IN_USE = 1
	}

	private enum DISPLAYCONFIG_TARGET_FLAGS
	{
		DISPLAYCONFIG_TARGET_IN_USE = 1,
		DISPLAYCONFIG_TARGET_FORCIBLE = 2,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_BOOT = 4,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_PATH = 8,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_SYSTEM = 0x10,
		DISPLAYCONFIG_TARGET_IS_HMD = 0x20
	}

	private enum QDC
	{
		QDC_ALL_PATHS = 1,
		QDC_ONLY_ACTIVE_PATHS = 2,
		QDC_DATABASE_CURRENT = 4,
		QDC_VIRTUAL_MODE_AWARE = 0x10,
		QDC_INCLUDE_HMD = 0x20
	}

	private enum DISPLAYCONFIG_SCANLINE_ORDERING
	{
		DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
		DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3
	}

	private enum DISPLAYCONFIG_PIXELFORMAT
	{
		DISPLAYCONFIG_PIXELFORMAT_8BPP = 1,
		DISPLAYCONFIG_PIXELFORMAT_16BPP,
		DISPLAYCONFIG_PIXELFORMAT_24BPP,
		DISPLAYCONFIG_PIXELFORMAT_32BPP,
		DISPLAYCONFIG_PIXELFORMAT_NONGDI
	}

	private enum DISPLAYCONFIG_MODE_INFO_TYPE
	{
		DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
		DISPLAYCONFIG_MODE_INFO_TYPE_TARGET,
		DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE
	}

	private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public DISPLAYCONFIG_DEVICE_INFO_TYPE type;

		public int size;

		public LUID adapterId;

		public uint id;
	}

	private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		public uint value;

		public DISPLAYCONFIG_COLOR_ENCODING colorEncoding;

		public int bitsPerColorChannel;

		public bool advancedColorSupported => (value & 1) == 1;

		public bool advancedColorEnabled => (value & 2) == 2;

		public bool wideColorEnforced => (value & 4) == 4;

		public bool advancedColorForceDisabled => (value & 8) == 8;
	}

	private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		public uint value;

		public DISPLAYCONFIG_COLOR_ENCODING colorEncoding;

		public int bitsPerColorChannel;

		public DISPLAYCONFIG_ADVANCED_COLOR_MODE activeColorMode;
	}

	private struct POINTL
	{
		public int x;

		public int y;
	}

	private struct LUID
	{
		public uint LowPart;

		public int HighPart;

		public long Value => ((long)HighPart << 32) | LowPart;

		public override string ToString()
		{
			return Value.ToString();
		}
	}

	private struct DISPLAYCONFIG_SOURCE_MODE
	{
		public uint width;

		public uint height;

		public DISPLAYCONFIG_PIXELFORMAT pixelFormat;

		public POINTL position;
	}

	private struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;

		public uint Denominator;

		public override string ToString()
		{
			return Numerator + " / " + Denominator;
		}
	}

	private struct DISPLAYCONFIG_2DREGION
	{
		public uint cx;

		public uint cy;
	}

	private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
	{
		public POINTL PathSourceSize;

		public RECT DesktopImageRegion;

		public RECT DesktopImageClip;
	}

	private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
	{
		public ulong pixelRate;

		public DISPLAYCONFIG_RATIONAL hSyncFreq;

		public DISPLAYCONFIG_RATIONAL vSyncFreq;

		public DISPLAYCONFIG_2DREGION activeSize;

		public DISPLAYCONFIG_2DREGION totalSize;

		public uint videoStandard;

		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
	}

	private struct DISPLAYCONFIG_TARGET_MODE
	{
		public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct DISPLAYCONFIG_MODE_INFO_union
	{
		[FieldOffset(0)]
		public DISPLAYCONFIG_TARGET_MODE targetMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_SOURCE_MODE sourceMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
	}

	private struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;

		public uint id;

		public uint modeInfoIdx;

		public DISPLAYCONFIG_SOURCE_FLAGS statusFlags;
	}

	private struct DISPLAYCONFIG_PATH_TARGET_INFO
	{
		public LUID adapterId;

		public uint id;

		public uint modeInfoIdx;

		public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;

		public DISPLAYCONFIG_ROTATION rotation;

		public DISPLAYCONFIG_SCALING scaling;

		public DISPLAYCONFIG_RATIONAL refreshRate;

		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;

		public bool targetAvailable;

		public DISPLAYCONFIG_TARGET_FLAGS statusFlags;
	}

	private struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;

		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;

		public DISPLAYCONFIG_PATH flags;
	}

	private struct DISPLAYCONFIG_MODE_INFO
	{
		public DISPLAYCONFIG_MODE_INFO_TYPE infoType;

		public uint id;

		public LUID adapterId;

		public DISPLAYCONFIG_MODE_INFO_union info;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
	{
		public uint value;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;

		public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;

		public ushort edidManufactureId;

		public ushort edidProductCodeId;

		public uint connectorInstance;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string monitorFriendlyDeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string monitorDevicePat;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	public static bool GetHDRStatus(bool log = true)
	{
		bool acm;
		return GetHDRStatus(out acm, log);
	}

	public static bool GetHDRStatus(out bool acm, bool log = true)
	{
		acm = false;
		DISPLAYCONFIG_PATH_INFO[] array = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
		DISPLAYCONFIG_MODE_INFO[] array2 = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
		int numPathArrayElements = 0;
		int numModeInfoArrayElements = 0;
		int num = 0;
		for (int i = 0; i < 3; i++)
		{
			num = GetDisplayConfigBufferSizes(QDC.QDC_ONLY_ACTIVE_PATHS, out numPathArrayElements, out numModeInfoArrayElements);
			if (num != 0)
			{
				break;
			}
			array = new DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
			array2 = new DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];
			num = QueryDisplayConfig(QDC.QDC_ONLY_ACTIVE_PATHS, ref numPathArrayElements, array, ref numModeInfoArrayElements, array2, IntPtr.Zero);
			if (num != 122)
			{
				break;
			}
		}
		if (num != 0)
		{
			if (log)
			{
				Logger.WriteLine("HDR Detection Error: " + new Win32Exception(num).Message);
			}
			return false;
		}
		string text = AppConfig.GetString("internal_display");
		bool flag = false;
		bool flag2 = false;
		for (int j = 0; j < numPathArrayElements; j++)
		{
			DISPLAYCONFIG_PATH_INFO dISPLAYCONFIG_PATH_INFO = array[j];
			DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket = new DISPLAYCONFIG_TARGET_DEVICE_NAME
			{
				header = 
				{
					type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
					size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
					adapterId = dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId,
					id = dISPLAYCONFIG_PATH_INFO.targetInfo.id
				}
			};
			num = DisplayConfigGetDeviceInfo(ref requestPacket);
			if (num != 0)
			{
				if (log)
				{
					Logger.WriteLine("Display name query failed: " + new Win32Exception(num).Message);
				}
			}
			else
			{
				if (requestPacket.outputTechnology != DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL && requestPacket.outputTechnology != DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED && (string.IsNullOrEmpty(text) || !(requestPacket.monitorFriendlyDeviceName == text)))
				{
					continue;
				}
				DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
				{
					header = 
					{
						type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
						size = Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(),
						adapterId = dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId,
						id = dISPLAYCONFIG_PATH_INFO.targetInfo.id
					}
				};
				if (DisplayConfigGetDeviceInfo(ref requestPacket2) == 0)
				{
					bool flag3 = requestPacket2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;
					bool flag4 = requestPacket2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE.DISPLAYCONFIG_ADVANCED_COLOR_MODE_WCG;
					flag |= flag3;
					flag2 |= flag4;
					if (log)
					{
						Logger.WriteLine(requestPacket.monitorFriendlyDeviceName + " HDR: " + flag3 + " ACM: " + flag4 + " | mode=" + requestPacket2.activeColorMode.ToString() + " " + requestPacket2.bitsPerColorChannel + "bpc");
					}
					continue;
				}
				DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket3 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
				{
					header = 
					{
						type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
						size = Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
						adapterId = dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId,
						id = dISPLAYCONFIG_PATH_INFO.targetInfo.id
					}
				};
				num = DisplayConfigGetDeviceInfo(ref requestPacket3);
				if (num != 0)
				{
					if (log)
					{
						Logger.WriteLine("Advanced color query failed for " + requestPacket.monitorFriendlyDeviceName + ": " + new Win32Exception(num).Message);
					}
					continue;
				}
				bool flag5 = requestPacket3.advancedColorEnabled && !requestPacket3.wideColorEnforced;
				bool flag6 = requestPacket3.advancedColorEnabled && requestPacket3.wideColorEnforced && requestPacket3.bitsPerColorChannel > 8;
				flag |= flag5;
				flag2 |= flag6;
				if (log)
				{
					Logger.WriteLine(requestPacket.monitorFriendlyDeviceName + " HDR: " + flag5 + " ACM: " + flag6 + " | " + requestPacket3.advancedColorEnabled + " " + requestPacket3.bitsPerColorChannel + " " + requestPacket3.value + " " + requestPacket3.wideColorEnforced);
				}
			}
		}
		acm = flag2;
		return flag;
	}

	[DllImport("user32")]
	private static extern int GetDisplayConfigBufferSizes(QDC flags, out int numPathArrayElements, out int numModeInfoArrayElements);

	[DllImport("user32")]
	private static extern int QueryDisplayConfig(QDC flags, ref int numPathArrayElements, [In][Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref int numModeInfoArrayElements, [In][Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, out DISPLAYCONFIG_TOPOLOGY_ID currentTopologyId);

	[DllImport("user32")]
	private static extern int QueryDisplayConfig(QDC flags, ref int numPathArrayElements, [In][Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref int numModeInfoArrayElements, [In][Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, nint currentTopologyId);

	[DllImport("user32")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

	[DllImport("user32")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);

	[DllImport("user32")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

	[DllImport("user32")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}
