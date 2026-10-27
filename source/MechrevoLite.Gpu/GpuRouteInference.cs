using System;
using MechrevoLite.Display;

namespace MechrevoLite.Gpu;

public static class GpuRouteInference
{
	public static GpuRoute Infer(PanelAdapterKind panel, DgpuPresence dgpu)
	{
		switch (panel)
		{
		case PanelAdapterKind.Discrete:
			return GpuRoute.Direct;
		case PanelAdapterKind.Integrated:
		{
			GpuRoute result;
			switch (dgpu)
			{
			case DgpuPresence.Present:
				result = GpuRoute.Hybrid;
				break;
			case DgpuPresence.Disabled:
			case DgpuPresence.Absent:
				result = GpuRoute.IgpuOnly;
				break;
			default:
				result = GpuRoute.Unknown;
				break;
			}
			return result;
		}
		default:
			return GpuRoute.Unknown;
		}
	}

	public static int ToGpuMode(GpuRoute route)
	{
		return route switch
		{
			GpuRoute.IgpuOnly => 0, 
			GpuRoute.Hybrid => 1, 
			GpuRoute.Direct => 2, 
			_ => -1, 
		};
	}

	public static GpuRoute FromGpuMode(int mode)
	{
		return mode switch
		{
			0 => GpuRoute.IgpuOnly, 
			1 => GpuRoute.Hybrid, 
			2 => GpuRoute.Direct, 
			_ => GpuRoute.Unknown, 
		};
	}

	public static PanelAdapterKind ClassifyAdapterPath(string? devicePath)
	{
		if (string.IsNullOrWhiteSpace(devicePath))
		{
			return PanelAdapterKind.Unknown;
		}
		if (devicePath.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
		{
			return PanelAdapterKind.Discrete;
		}
		if (devicePath.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) || devicePath.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase))
		{
			return PanelAdapterKind.Integrated;
		}
		return PanelAdapterKind.Unknown;
	}

	public static GpuRoute? DecodeOemDisplayMode(byte value, bool isAmd)
	{
		if (!isAmd)
		{
			switch (value)
			{
			case 2:
				return GpuRoute.Direct;
			case 4:
				return GpuRoute.Hybrid;
			case 1:
				return GpuRoute.IgpuOnly;
			}
		}
		else
		{
			switch (value)
			{
			case 1:
				return GpuRoute.Direct;
			case 0:
				return GpuRoute.Hybrid;
			case 2:
				return GpuRoute.IgpuOnly;
			}
		}
		return null;
	}

	internal static bool IsInternalOutput(DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology)
	{
		switch (technology)
		{
		case DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL:
		case DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS:
		case DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED:
		case DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED:
			return true;
		default:
			return false;
		}
	}
}
