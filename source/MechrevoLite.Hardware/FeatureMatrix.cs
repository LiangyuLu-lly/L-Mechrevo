using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Hardware;

public sealed class FeatureMatrix
{
	private static readonly FeatureBitDefinition[] Table = new FeatureBitDefinition[23]
	{
		new FeatureBitDefinition(FeatureBit.AcRecoverySwitch, "AcRecoverySwitchSupport", FeatureMissingPolicy.VendorConstantOn),
		new FeatureBitDefinition(FeatureBit.Keyboard, "KeyboardSupport", FeatureMissingPolicy.VendorConstantOn),
		new FeatureBitDefinition(FeatureBit.SystemMonitor, "SystemMonitorSupport", FeatureMissingPolicy.VendorConstantOn),
		new FeatureBitDefinition(FeatureBit.FanSettings, "FanSettingsSupport", FeatureMissingPolicy.VendorConstantNonCommercial),
		new FeatureBitDefinition(FeatureBit.OverclockSettings, "OcSettingsSupport", FeatureMissingPolicy.VendorConstantNonCommercial),
		new FeatureBitDefinition(FeatureBit.AcRecoveryBios, "AcRecoverySwitchBiosSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.ColorCalibration, "ColorCalibrationSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.DgpuDirect, "DGpuDirectConnectionSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.FanBoost, "FanBoostBtnSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.AmdPlatform, "IsAMDPlatform", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.NvidiaGpu, "IsNvGpu", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.Lightbar, "LightbarSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.RgbLightbar, "RGBLightbarSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.Numpad, "NumPadSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.LiquidCooling, "LiquidCoolingSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.LiquidCoolingAutoMode, "LiquidCoolingAutoModeSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.RamFan15, "RamFan1p5Support", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.TurboMode, "TurboModeSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.TypeC, "TypeCSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.Commercial, "IsProjectIdCommercial", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.CommercialHave20Db, "IsProjectIdCommercialHAVE20DB", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.GpuHotSwapSwitch, "GpuHotSwapSwitchSupport", FeatureMissingPolicy.FailClosed),
		new FeatureBitDefinition(FeatureBit.HotSwapStatus, "lgpuHotSwapSwitchStatus", FeatureMissingPolicy.FailClosed)
	};

	private static readonly IReadOnlyDictionary<FeatureBit, FeatureBitDefinition> ByBit = Table.ToDictionary((FeatureBitDefinition definition) => definition.Bit);

	private readonly IReadOnlyDictionary<string, object?> _values;

	public static IReadOnlyList<FeatureBitDefinition> Definitions => Table;

	public bool ProfileAvailable { get; }

	public bool IsCommercial { get; }

	public bool GpuHotSwapAvailable
	{
		get
		{
			if (IsPresent(FeatureBit.GpuHotSwapSwitch) && IsPresent(FeatureBit.HotSwapStatus) && IsSupported(FeatureBit.GpuHotSwapSwitch))
			{
				return IsSupported(FeatureBit.HotSwapStatus);
			}
			return false;
		}
	}

	private FeatureMatrix(IReadOnlyDictionary<string, object?> values)
	{
		_values = values;
		IsCommercial = PresentTruthy("IsProjectIdCommercial");
		ProfileAvailable = values.Count > 0;
	}

	public bool IsPresent(FeatureBit bit)
	{
		return _values.ContainsKey(ByBit[bit].Key);
	}

	public bool IsSupported(FeatureBit bit)
	{
		FeatureBitDefinition featureBitDefinition = ByBit[bit];
		if (_values.TryGetValue(featureBitDefinition.Key, out object value))
		{
			return Truthy(value);
		}
		return featureBitDefinition.Missing switch
		{
			FeatureMissingPolicy.VendorConstantOn => true, 
			FeatureMissingPolicy.VendorConstantNonCommercial => !IsCommercial, 
			_ => false, 
		};
	}

	public static FeatureMatrix FromValues(IReadOnlyDictionary<string, object?> serviceValues)
	{
		ArgumentNullException.ThrowIfNull(serviceValues, "serviceValues");
		return new FeatureMatrix(new Dictionary<string, object>(serviceValues, StringComparer.OrdinalIgnoreCase));
	}

	public static FeatureMatrix Current()
	{
		return FromValues(MechrevoDeviceCapabilities.ReadServiceValues());
	}

	private bool PresentTruthy(string key)
	{
		if (_values.TryGetValue(key, out object value))
		{
			return Truthy(value);
		}
		return false;
	}

	private static bool Truthy(object? value)
	{
		if (value == null)
		{
			return false;
		}
		if (value is bool)
		{
			return (bool)value;
		}
		if (int.TryParse(value.ToString(), out var result))
		{
			return result > 0;
		}
		switch (value.ToString()?.Trim().ToLowerInvariant())
		{
		case "true":
		case "yes":
		case "on":
		case "supported":
		case "enable":
		case "enabled":
			return true;
		default:
			return false;
		}
	}
}
