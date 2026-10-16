using System;
using System.Globalization;
using NvAPIWrapper.DRS;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;

namespace MechrevoLite.Gpu;

public static class NvPreferredGpuReader
{
	public const uint ShimRenderingModeId = 284810369u;

	private const uint EnableBit = 1u;

	private const uint AutoSelectBit = 16u;

	internal static Func<NvPreferredGpu>? DriverOverride { get; set; }

	public static NvPreferredGpu Decode(uint? value)
	{
		if (value.HasValue)
		{
			uint valueOrDefault = value.GetValueOrDefault();
			if ((valueOrDefault & 1) != 0)
			{
				return NvPreferredGpu.HighPerformance;
			}
			if ((valueOrDefault & 0x10) != 0)
			{
				return NvPreferredGpu.AutoSelect;
			}
			return NvPreferredGpu.Integrated;
		}
		return NvPreferredGpu.AutoSelect;
	}

	public static NvPreferredGpu FromServiceStatus(string? status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return NvPreferredGpu.Unknown;
		}
		string text = status.Trim().ToUpperInvariant();
		if (text.Contains("NV_CTRL_PANEL_HIGHPERFORMANCE", StringComparison.Ordinal))
		{
			return NvPreferredGpu.HighPerformance;
		}
		if (text.Contains("NV_CTRL_PANEL_AUTOSELECT", StringComparison.Ordinal))
		{
			return NvPreferredGpu.AutoSelect;
		}
		return NvPreferredGpu.Unknown;
	}

	public static NvPreferredGpu ReadFromDriver()
	{
		Func<NvPreferredGpu> driverOverride = DriverOverride;
		if (driverOverride != null)
		{
			return driverOverride();
		}
		try
		{
			using DriverSettingsSession driverSettingsSession = DriverSettingsSession.CreateAndLoad();
			uint? value;
			try
			{
				DriverSettingsProfile currentGlobalProfile = driverSettingsSession.CurrentGlobalProfile;
				if (currentGlobalProfile == null)
				{
					Logger.WriteLineThrottled("nv-preferred-gpu-read", "NVIDIA preferred GPU read: no global DRS profile", 60000);
					return NvPreferredGpu.Unknown;
				}
				object obj = currentGlobalProfile.GetSetting(284810369u)?.CurrentValue;
				value = ((obj != null) ? new uint?(Convert.ToUInt32(obj, CultureInfo.InvariantCulture)) : ((uint?)null));
			}
			catch (NVIDIAApiException ex) when (ex.Status == Status.SettingNotFound)
			{
				value = null;
			}
			NvPreferredGpu nvPreferredGpu = Decode(value);
			Logger.WriteLineIfChanged("nv-preferred-gpu", $"NVIDIA preferred GPU (DRS 0x{284810369u:X8}) = {((!value.HasValue) ? "unset" : ("0x" + value.GetValueOrDefault().ToString("X8")))} -> {nvPreferredGpu}");
			return nvPreferredGpu;
		}
		catch (Exception ex2)
		{
			Logger.WriteLineThrottled("nv-preferred-gpu-read", "NVIDIA preferred GPU read failed: " + ex2.Message, 60000);
			return NvPreferredGpu.Unknown;
		}
	}
}
