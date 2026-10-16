using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Display;

internal static class DisplayNative
{
	public enum QUERY_DEVICE_CONFIG_FLAGS : uint
	{
		QDC_ALL_PATHS = 1u,
		QDC_ONLY_ACTIVE_PATHS = 2u,
		QDC_DATABASE_CURRENT = 4u,
		QDC_VIRTUAL_MODE_AWARE = 0x10u,
		QDC_INCLUDE_HMD = 0x20u
	}

	public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
	{
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = uint.MaxValue,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 2147483648u,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32 = DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER
	}

	public enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
	{
		DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0u,
		DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1u,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2u,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3u,
		DISPLAYCONFIG_SCANLINE_ORDERING_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_ROTATION : uint
	{
		DISPLAYCONFIG_ROTATION_IDENTITY = 1u,
		DISPLAYCONFIG_ROTATION_ROTATE90 = 2u,
		DISPLAYCONFIG_ROTATION_ROTATE180 = 3u,
		DISPLAYCONFIG_ROTATION_ROTATE270 = 4u,
		DISPLAYCONFIG_ROTATION_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_SCALING : uint
	{
		DISPLAYCONFIG_SCALING_IDENTITY = 1u,
		DISPLAYCONFIG_SCALING_CENTERED = 2u,
		DISPLAYCONFIG_SCALING_STRETCHED = 3u,
		DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4u,
		DISPLAYCONFIG_SCALING_CUSTOM = 5u,
		DISPLAYCONFIG_SCALING_PREFERRED = 128u,
		DISPLAYCONFIG_SCALING_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_PIXELFORMAT : uint
	{
		DISPLAYCONFIG_PIXELFORMAT_8BPP = 1u,
		DISPLAYCONFIG_PIXELFORMAT_16BPP = 2u,
		DISPLAYCONFIG_PIXELFORMAT_24BPP = 3u,
		DISPLAYCONFIG_PIXELFORMAT_32BPP = 4u,
		DISPLAYCONFIG_PIXELFORMAT_NONGDI = 5u,
		DISPLAYCONFIG_PIXELFORMAT_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
	{
		DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1u,
		DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2u,
		DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3u,
		DISPLAYCONFIG_MODE_INFO_TYPE_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
	{
		DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1u,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2u,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3u,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4u,
		DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5u,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6u,
		DISPLAYCONFIG_DEVICE_INFO_GET_SUPPORT_VIRTUAL_RESOLUTION = 7u,
		DISPLAYCONFIG_DEVICE_INFO_SET_SUPPORT_VIRTUAL_RESOLUTION = 8u,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9u,
		DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10u,
		DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11u,
		DISPLAYCONFIG_DEVICE_INFO_FORCE_UINT32 = uint.MaxValue
	}

	public enum DISPLAYCONFIG_COLOR_ENCODING : uint
	{
		DISPLAYCONFIG_COLOR_ENCODING_RGB,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR444,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR422,
		DISPLAYCONFIG_COLOR_ENCODING_YCBCR420,
		DISPLAYCONFIG_COLOR_ENCODING_INTENSITY
	}

	public enum DISPLAYCONFIG_TOPOLOGY_ID : uint
	{
		DISPLAYCONFIG_TOPOLOGY_INTERNAL = 1u,
		DISPLAYCONFIG_TOPOLOGY_CLONE = 2u,
		DISPLAYCONFIG_TOPOLOGY_EXTEND = 4u,
		DISPLAYCONFIG_TOPOLOGY_EXTERNAL = 8u
	}

	[Flags]
	public enum DISPLAYCONFIG_PATH_FLAGS : uint
	{
		DISPLAYCONFIG_PATH_ACTIVE = 1u,
		DISPLAYCONFIG_PATH_PREFERRED_UNSCALED = 4u,
		DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 8u
	}

	[Flags]
	public enum DISPLAYCONFIG_SOURCE_FLAGS : uint
	{
		DISPLAYCONFIG_SOURCE_IN_USE = 1u
	}

	[Flags]
	public enum DISPLAYCONFIG_TARGET_FLAGS : uint
	{
		DISPLAYCONFIG_TARGET_IN_USE = 1u,
		DISPLAYCONFIG_TARGET_FORCIBLE = 2u,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_BOOT = 4u,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_PATH = 8u,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_SYSTEM = 0x10u,
		DISPLAYCONFIG_TARGET_IS_HMD = 0x20u
	}

	[Flags]
	public enum DisplaySettingsFlags
	{
		CDS_UPDATEREGISTRY = 1,
		CDS_TEST = 2,
		CDS_FULLSCREEN = 4,
		CDS_GLOBAL = 8,
		CDS_SET_PRIMARY = 0x10,
		CDS_RESET = 0x40000000,
		CDS_NORESET = 0x10000000
	}

	public enum COLORPROFILETYPE
	{
		CPT_ICC,
		CPT_DMP,
		CPT_CAMP,
		CPT_GMMP
	}

	public enum COLORPROFILESUBTYPE
	{
		CPST_PERCEPTUAL,
		CPST_RELATIVE_COLORIMETRIC,
		CPST_SATURATION,
		CPST_ABSOLUTE_COLORIMETRIC,
		CPST_NONE,
		CPST_RGB_WORKING_SPACE,
		CPST_CUSTOM_WORKING_SPACE,
		CPST_STANDARD_DISPLAY_COLOR_MODE,
		CPST_EXTENDED_DISPLAY_COLOR_MODE
	}

	public enum WCS_PROFILE_MANAGEMENT_SCOPE
	{
		WCS_PROFILE_MANAGEMENT_SCOPE_SYSTEM_WIDE,
		WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER
	}

	public struct LUID
	{
		public uint LowPart;

		public int HighPart;
	}

	public struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	public struct POINTL
	{
		public int x;

		public int y;
	}

	public struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;

		public uint Denominator;
	}

	public struct DISPLAYCONFIG_2DREGION
	{
		public uint cx;

		public uint cy;
	}

	public struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;

		public uint id;

		public uint modeInfoIdx;

		public DISPLAYCONFIG_SOURCE_FLAGS statusFlags;
	}

	public struct DISPLAYCONFIG_PATH_TARGET_INFO
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

	public struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;

		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;

		public DISPLAYCONFIG_PATH_FLAGS flags;
	}

	public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
	{
		public ulong pixelRate;

		public DISPLAYCONFIG_RATIONAL hSyncFreq;

		public DISPLAYCONFIG_RATIONAL vSyncFreq;

		public DISPLAYCONFIG_2DREGION activeSize;

		public DISPLAYCONFIG_2DREGION totalSize;

		public uint videoStandard;

		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
	}

	public struct DISPLAYCONFIG_TARGET_MODE
	{
		public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
	}

	public struct DISPLAYCONFIG_SOURCE_MODE
	{
		public uint width;

		public uint height;

		public DISPLAYCONFIG_PIXELFORMAT pixelFormat;

		public POINTL position;
	}

	public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
	{
		public POINTL PathSourceSize;

		public RECT DesktopImageRegion;

		public RECT DesktopImageClip;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct DISPLAYCONFIG_MODE_INFO_UNION
	{
		[FieldOffset(0)]
		public DISPLAYCONFIG_TARGET_MODE targetMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_SOURCE_MODE sourceMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
	}

	public struct DISPLAYCONFIG_MODE_INFO
	{
		public DISPLAYCONFIG_MODE_INFO_TYPE infoType;

		public uint id;

		public LUID adapterId;

		public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
	}

	public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public DISPLAYCONFIG_DEVICE_INFO_TYPE type;

		public uint size;

		public LUID adapterId;

		public uint id;
	}

	public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
	{
		public uint value;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
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
		public string monitorDevicePath;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_ADAPTER_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string adapterDevicePath;
	}

	public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
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

	public struct DEVMODE
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string dmDeviceName;

		public short dmSpecVersion;

		public short dmDriverVersion;

		public short dmSize;

		public short dmDriverExtra;

		public int dmFields;

		public int dmPositionX;

		public int dmPositionY;

		public int dmDisplayOrientation;

		public int dmDisplayFixedOutput;

		public short dmColor;

		public short dmDuplex;

		public short dmYResolution;

		public short dmTTOption;

		public short dmCollate;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string dmFormName;

		public short dmLogPixels;

		public short dmBitsPerPel;

		public int dmPelsWidth;

		public int dmPelsHeight;

		public int dmDisplayFlags;

		public int dmDisplayFrequency;

		public int dmICMMethod;

		public int dmICMIntent;

		public int dmMediaType;

		public int dmDitherType;

		public int dmReserved1;

		public int dmReserved2;

		public int dmPanningWidth;

		public int dmPanningHeight;
	}

	[DllImport("user32.dll")]
	public static extern int GetDisplayConfigBufferSizes(QUERY_DEVICE_CONFIG_FLAGS flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

	[DllImport("user32.dll")]
	public static extern int QueryDisplayConfig(QUERY_DEVICE_CONFIG_FLAGS flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, nint currentTopologyId);

	[DllImport("user32.dll")]
	public static extern int QueryDisplayConfig(QUERY_DEVICE_CONFIG_FLAGS flags, ref uint numPathArrayElements, [In][Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [In][Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, out DISPLAYCONFIG_TOPOLOGY_ID currentTopologyId);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_ADAPTER_NAME adapterName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

	[DllImport("user32.dll")]
	public static extern int EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

	[DllImport("user32.dll")]
	public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, nint hwnd, DisplaySettingsFlags dwflags, nint lParam);
}
