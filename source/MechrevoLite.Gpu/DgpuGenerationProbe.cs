using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MechrevoLite.Gpu;

public static class DgpuGenerationProbe
{
	public static DgpuIdentity Resolve(IReadOnlyList<GpuAdapter> adapters, bool enumerationAvailable)
	{
		ArgumentNullException.ThrowIfNull(adapters, "adapters");
		if (!enumerationAvailable)
		{
			return DgpuIdentity.Unknown;
		}
		GpuAdapter[] array = adapters.Where((GpuAdapter adapter) => adapter.IsNvidia).ToArray();
		if (array.Length == 0)
		{
			return DgpuIdentity.NoDgpu;
		}
		GpuAdapter[] array2 = array;
		foreach (GpuAdapter gpuAdapter in array2)
		{
			DgpuGenerationKind? dgpuGenerationKind = FromMarketingName(gpuAdapter.Name);
			if (dgpuGenerationKind.HasValue)
			{
				DgpuGenerationKind valueOrDefault = dgpuGenerationKind.GetValueOrDefault();
				return new DgpuIdentity(valueOrDefault, DgpuProbeSource.MarketingName, HasDgpu: true, gpuAdapter.Name, gpuAdapter.DeviceId);
			}
		}
		array2 = array;
		foreach (GpuAdapter gpuAdapter2 in array2)
		{
			DgpuGenerationKind? dgpuGenerationKind = FromDeviceId(gpuAdapter2.DeviceId);
			if (dgpuGenerationKind.HasValue)
			{
				DgpuGenerationKind valueOrDefault2 = dgpuGenerationKind.GetValueOrDefault();
				return new DgpuIdentity(valueOrDefault2, DgpuProbeSource.PciDeviceId, HasDgpu: true, gpuAdapter2.Name, gpuAdapter2.DeviceId);
			}
		}
		GpuAdapter gpuAdapter3 = array[0];
		return new DgpuIdentity(DgpuGenerationKind.Unknown, DgpuProbeSource.None, HasDgpu: true, gpuAdapter3.Name, gpuAdapter3.DeviceId);
	}

	public static DgpuGenerationKind? FromMarketingName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		Match match = Regex.Match(name, "\\bRTX\\s*(\\d{4})\\b", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups[1].Value.Substring(0, 2) switch
		{
			"30" => (DgpuGenerationKind?)DgpuGenerationKind.Gen30, 
			"40" => DgpuGenerationKind.Gen40, 
			"50" => DgpuGenerationKind.Gen50, 
			_ => null, 
		};
	}

	public static DgpuGenerationKind? FromDeviceId(string? deviceId)
	{
		if (!TryParseHex16(deviceId, out var value))
		{
			return null;
		}
		switch ((value >> 8) & 0xFF)
		{
		case 32:
		case 33:
		case 34:
		case 35:
		case 36:
		case 37:
			return DgpuGenerationKind.Gen30;
		case 38:
		case 39:
		case 40:
			return DgpuGenerationKind.Gen40;
		case 43:
		case 44:
		case 45:
		case 46:
		case 47:
		case 48:
			return DgpuGenerationKind.Gen50;
		default:
			return null;
		}
	}

	private static bool TryParseHex16(string? text, out int value)
	{
		value = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		int num = text2.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			text2 = text2.Substring(num + 4);
		}
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring(2);
		}
		text2 = text2.Trim();
		if (text2.Length != 4)
		{
			return false;
		}
		return int.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}
}
