using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

public sealed class MechrevoDeviceCapabilities
{
	private static readonly string[] ItemSupportPaths = new string[3] { "SOFTWARE\\OEM\\GamingCenter2\\ItemSupport", "SOFTWARE\\OEM\\GamingCenter\\ItemSupport", "SOFTWARE\\OEM\\ControlCenter\\ItemSupport" };

	private static readonly string[] GpuConfigPaths = new string[3] { "SOFTWARE\\OEM\\GamingCenter2\\MySetting\\GpuConfig", "SOFTWARE\\OEM\\GamingCenter\\MySetting\\GpuConfig", "SOFTWARE\\OEM\\ControlCenter\\MySetting\\GpuConfig" };

	private static readonly string[] RgbKeyboardPaths = new string[3] { "SOFTWARE\\OEM\\GamingCenter2\\RGBKeyboard", "SOFTWARE\\OEM\\GamingCenter\\RGBKeyboard", "SOFTWARE\\OEM\\ControlCenter\\RGBKeyboard" };

	private static readonly object CurrentLock = new object();

	private static MechrevoDeviceCapabilities? _current;

	private static long _snapshotRevision;

	internal static Func<MechrevoDeviceCapabilities>? SnapshotFactoryOverride;

	internal const string ModelOverrideVariable = "LMECHREVO_MODEL_OVERRIDE";

	public string ProjectId { get; init; } = "";

	public string Manufacturer { get; init; } = "";

	public string Model { get; init; } = "";

	public string SystemFamily { get; init; } = "";

	public string SystemSku { get; init; } = "";

	public string BaseboardProduct { get; init; } = "";

	public string BiosVersion { get; init; } = "";

	public bool IsMechrevo { get; init; }

	public bool ProfileAvailable { get; init; }

	public bool Keyboard { get; init; }

	public bool Lightbar { get; init; }

	public bool RgbLightbar { get; init; }

	public bool LogoLight { get; init; }

	/// <summary>
	/// 2026-10-09（lightmerge）：注册表里存在 **logo 专属布局**（<c>Lightbar_logo_PowerSwitch / ACLight / DCLight</c>
	/// 之类的值，或名字里同时含 lightbar+lighbar 与 logo 的子键）。**纯诊断透出**。
	/// ⚠️ 本项**不参与** <see cref="LogoLight"/> 判定（见下面 2026-10-06 的修回归注记：GCU 给所有机型
	/// 都写这套默认值，恒真不可信）；2026-10-09（用户裁定）它也**不再**参与「灯光额外汇是否自动合并」的
	/// 排除判定 —— 同一条"恒真"理由会把 auto 写死成 no-op，那一条排除项已删除。
	/// </summary>
	public bool LogoLightingRegistryLayout { get; init; }

	public bool DisplayRefresh { get; init; }

	public bool ColorCalibration { get; init; }

	/// <summary>校色档位能力：1=仅默认档，2=默认+sRGB，3=+P3，4=+AdobeRGB；0=未知。</summary>
	public int ColorCalibrationLevel { get; init; }

	public bool DgpuDirect { get; init; }

	public bool IgpuOnly { get; init; }

	public bool FanBoost { get; init; }

	public bool FanSettings { get; init; }

	public bool OverclockSettings { get; init; }

	public bool CpuPerformanceTuning { get; init; }

	public bool LiquidCooling { get; init; }

	public bool LiquidCoolingAutoMode { get; init; }

	public bool Numpad { get; init; }

	public bool AcRecovery { get; init; }

	public bool? AcRecoveryOn { get; init; }

	public bool LcdOverdrive { get; init; }

	public bool LocalDimming { get; init; }

	public bool TurboMode { get; init; }

	public bool TurboSubMode { get; init; }

	internal FeatureAvailability SilentTurboAvailability
	{
		get
		{
			if (!TurboSubMode)
			{
				if (!ProfileAvailable)
				{
					return FeatureAvailability.Unknown;
				}
				return FeatureAvailability.Unsupported;
			}
			return FeatureAvailability.Supported;
		}
	}

	public bool SmartBalance { get; init; }

	public bool RamFan15 { get; init; }

	public bool NvidiaGpu { get; init; }

	public bool GpuHotSwap { get; init; }

	public bool AmdPlatform { get; init; }

	public bool IsOldType { get; init; }

	public int KeyboardType { get; init; }

	public int DisplayRefreshLevel { get; init; }

	public string IdentitySummary => $"manufacturer={Manufacturer}, model={Model}, family={SystemFamily}, sku={SystemSku}, board={BaseboardProduct}, bios={BiosVersion}, project={ProjectId}";

	public static long SnapshotRevision
	{
		get
		{
			lock (CurrentLock)
			{
				return _snapshotRevision;
			}
		}
	}

	public static MechrevoDeviceCapabilities Current
	{
		get
		{
			lock (CurrentLock)
			{
				return _current ?? (_current = BuildLocked());
			}
		}
	}

	internal static string? ModelOverride
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable("LMECHREVO_MODEL_OVERRIDE");
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return environmentVariable.Trim();
			}
			return null;
		}
	}

	private static MechrevoDeviceCapabilities BuildLocked()
	{
		Func<MechrevoDeviceCapabilities> snapshotFactoryOverride = SnapshotFactoryOverride;
		MechrevoDeviceCapabilities result = ((snapshotFactoryOverride != null) ? snapshotFactoryOverride() : Load());
		_snapshotRevision++;
		return result;
	}

	public static void Invalidate()
	{
		lock (CurrentLock)
		{
			_current = null;
		}
	}

	public static MechrevoDeviceCapabilities Refresh()
	{
		Invalidate();
		return Current;
	}

	internal static void OverrideCurrent(MechrevoDeviceCapabilities? capabilities)
	{
		lock (CurrentLock)
		{
			_current = capabilities;
		}
	}

	public static MechrevoDeviceCapabilities Load()
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, object> dictionary2 = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		try
		{
			ReadMachineIdentity(dictionary2);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Cannot read machine identity: " + ex.Message);
		}
		ReadItemSupportInto(dictionary);
		string modelOverride = ModelOverride;
		if (modelOverride != null)
		{
			dictionary["BIOS_PROJECT_ID"] = modelOverride;
			dictionary2["SystemProductName"] = modelOverride;
		}
		// 2026-10-06（修回归）：注：logoRegistry 只打印、不参与 LogoLight（GCU 给所有机型写 Lightbar_logo_* 默认值，恒真不可信）。
		bool logoRegistry = DetectLogoLightingRegistry();
		MechrevoDeviceCapabilities mechrevoDeviceCapabilities = FromValues(dictionary, dictionary2, logoRegistry);
		Logger.WriteLine("Device capabilities: " + mechrevoDeviceCapabilities.IdentitySummary + $", officialProfile={mechrevoDeviceCapabilities.ProfileAvailable}, logo={mechrevoDeviceCapabilities.LogoLight}, logoRegistry={logoRegistry}");
		return mechrevoDeviceCapabilities;
	}

	internal static IReadOnlyDictionary<string, object?> ReadServiceValues()
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		ReadItemSupportInto(dictionary);
		string modelOverride = ModelOverride;
		if (modelOverride != null)
		{
			dictionary["BIOS_PROJECT_ID"] = modelOverride;
		}
		return dictionary;
	}

	internal static bool HasItemSupportContent()
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		ReadItemSupportInto(dictionary);
		return dictionary.Count > 0;
	}

	private static void ReadItemSupportInto(IDictionary<string, object?> values)
	{
		foreach (string item in ItemSupportPaths.Concat(GpuConfigPaths))
		{
			RegistryView[] array = new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			foreach (RegistryView registryView in array)
			{
				try
				{
					using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
					using RegistryKey registryKey2 = registryKey.OpenSubKey(item);
					if (registryKey2 != null)
					{
						string[] valueNames = registryKey2.GetValueNames();
						foreach (string text in valueNames)
						{
							values.TryAdd(text, registryKey2.GetValue(text));
						}
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine($"Cannot read device capabilities from {item} ({registryView}): {ex.Message}");
				}
			}
		}
	}

	internal static MechrevoDeviceCapabilities FromValues(IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?>? identity = null, bool logoRegistryDetected = false)
	{
		int num = Number(new string[3] { "KeyboardType", "RGBKeyboardType", "KeyboardLightType" });
		int num2 = Number(new string[5] { "DispalyRefresh", "DisplayRefresh", "DisplayRefreshSupport", "DisplayRefreshRateSupport", "RefreshRateSupport" });
		int num3 = Number(new string[4] { "SupportColorCalibration", "ColorCalibrationSupport", "DisplayColorCalibrationSupport", "ColorCalibration" });
		bool nvidiaGpu = Flag(new string[2] { "IsNvGpu", "IsNvidiaGpu" });
		// 50 系 GCU（ConnectionSwitch 系列）用 IGpuOnlyConnectionSwitch_Support=Enable 表达核显能力；40 系 GCU 用 iGPUModeOnlySupport。两者均认。
		bool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" });
		string text = Identity(new string[3] { "SystemManufacturer", "Manufacturer", "Vendor" });
		string value = Identity(new string[2] { "BaseBoardManufacturer", "BaseboardManufacturer" });
		return new MechrevoDeviceCapabilities
		{
			ProjectId = (Value(new string[5] { "BIOS_PROJECT_ID", "BIOSProjectID", "BIOSProjectId", "ProjectID", "ProjectId" })?.ToString() ?? ""),
			Manufacturer = text,
			Model = Identity(new string[3] { "SystemProductName", "Model", "ProductName" }),
			SystemFamily = Identity(new string[2] { "SystemFamily", "Family" }),
			SystemSku = Identity(new string[4] { "SystemSKU", "SystemSku", "SKU", "SkuNumber" }),
			BaseboardProduct = Identity(new string[3] { "BaseBoardProduct", "BaseboardProduct", "BoardProduct" }),
			BiosVersion = Identity(new string[3] { "BIOSVersion", "BiosVersion", "SMBIOSBIOSVersion" }),
			IsMechrevo = (IsMechrevoName(text) || IsMechrevoName(value)),
			ProfileAvailable = (values.Count > 0),
			Keyboard = (Flag(new string[5] { "KeyboardSupport", "RGBKeyboardSupport", "SingleColorKeyboardSupport", "KeyboardLightSupport", "KeyboardBacklightSupport" }) || num > 0),
			Lightbar = Flag(new string[5] { "LightbarSupport", "LightBarSupport", "HidLightbarSupport", "IsLightbarSupport", "Lightbar_Support" }),
			RgbLightbar = Flag(new string[3] { "RGBLightbarSupport", "RgbLightbarSupport", "IsRGBLightbarSupport" }),
			// 2026-10-06（修回归）：**不再**把 DetectLogoLightingRegistry() 的结果 OR 进来。
			// 实测（翼龙15Pro GM5HG0A，物理无 Logo 灯）：GCU 会在 HKLM\SOFTWARE\OEM\GamingCenter2\RGBKeyboard
			// 给**所有机型**写上通用默认值 Lightbar_logo_PowerSwitch/ACLight/DCLight，于是该注册表判据恒为真，
			// 灯区凭空多出「Logo 灯」通道（1 通道 -> 2 通道）。该注册表布局只能说明"GCU 支持 Logo 概念"，
			// 不能说明"本机装了 Logo 灯"，故降级为纯诊断日志，不参与 LogoLight 判定。
			// 现有两个判据仍保留且够用：ItemSupport 的 logo* 别名（下列 Flag）、
			// HidLightbar_Logo/Status 实测字段（MechrevoHw.LogoLightStatusSeen，见 HasLogoLightEvidence）。
			LogoLight = Flag(new string[10] { "LogoLightSupport", "LogoLightbarSupport", "LightbarLogoSupport", "HidLightbarLogoSupport", "RGBLogoLightSupport", "LogoSupport", "MBLogoSupport", "MBlogoSupport", "NewlogoSupport", "NewLogoSupport" }),
			// 2026-10-09（lightmerge）：只把注册表布局透出来给"额外汇是否自动合并"当保守排除项，不参与 LogoLight。
			LogoLightingRegistryLayout = logoRegistryDetected,
			DisplayRefresh = (num2 > 0),
			ColorCalibration = (num3 > 0),
			ColorCalibrationLevel = num3,
			DgpuDirect = Flag(new string[3] { "DGpuDirectConnectionSupport", "DiscreteGpuDirectConnectionSupport", "MuxSwitchSupport" }),
			IgpuOnly = igpuOnly,
			FanBoost = Flag(new string[2] { "FanBoostBtnSupport", "FanBoostSupport" }),
			FanSettings = Flag(new string[3] { "FanSettingsSupport", "FanSettingSupport", "CustomFanSupport" }),
			OverclockSettings = Flag(new string[3] { "OcSettingsSupport", "OCSettingsSupport", "GpuOverclockSupport" }),
			CpuPerformanceTuning = Flag(new string[2] { "CPUPerformanceAndOverClockMenuSupport", "CpuPerformanceTuningSupport" }),
			LiquidCooling = Flag(new string[2] { "LiquidCoolingSupport", "WaterCoolingSupport" }),
			LiquidCoolingAutoMode = Flag(new string[2] { "LiquidCoolingAutoModeSupport", "WaterCoolingAutoModeSupport" }),
			Numpad = Flag(new string[2] { "NumPadSupport", "NumpadSupport" }),
			AcRecovery = (Flag(new string[1] { "AcRecoverySwitchSupport" }) || Flag(new string[1] { "AcRecoverySwitchBiosSupport" })),
			AcRecoveryOn = AcRecoveryState(Value(new string[1] { "AcRecoverySwitch_Status" }), Value(new string[1] { "ACRecoveryStatus" })),
			LcdOverdrive = Flag(new string[1] { "LCDOverdriveSupport" }),
			LocalDimming = Flag(new string[1] { "LocalDimmingSupport" }),
			TurboMode = Flag(new string[1] { "TurboModeSupport" }),
			TurboSubMode = Flag(new string[1] { "IsTurboSubModeSupport" }),
			SmartBalance = Flag(new string[1] { "IsSmartBalanceSupport" }),
			RamFan15 = Flag(new string[1] { "RamFan1p5Support" }),
			NvidiaGpu = nvidiaGpu,
			// 2026-10-03（更正）：热切换（免重启切集显）是 50 系专属能力，与核显省电同一命令（IGPU_ONLY_CONNECT_RB_ON/OFF），
			// 判断只认 50 系 GCU 上报的 IGpuOnlyConnectionSwitch_Support=Enable；40 系三模（iGPUModeOnlySupport）为冷切换（重启生效），不计入热切换
			GpuHotSwap = Flag(new string[1] { "IGpuOnlyConnectionSwitch_Support" }),
			AmdPlatform = Flag(new string[2] { "IsAMDPlatform", "IsAmdPlatform" }),
			IsOldType = Flag(new string[1] { "IsOldType" }),
			KeyboardType = num,
			DisplayRefreshLevel = num2
		};
		bool Flag(params string[] names)
		{
			return Number(names) > 0;
		}
		string Identity(params string[] names)
		{
			if (identity != null)
			{
				foreach (string key in names)
				{
					if (identity.TryGetValue(key, out object value2) && value2 != null)
					{
						string text2 = value2.ToString()?.Trim() ?? "";
						if (!string.IsNullOrWhiteSpace(text2))
						{
							return text2;
						}
					}
				}
			}
			return Value(names)?.ToString()?.Trim() ?? "";
		}
		int Number(params string[] names)
		{
			object obj = Value(names);
			if (obj == null)
			{
				return 0;
			}
			if (obj is bool result)
			{
				return result ? 1 : 0;
			}
			if (int.TryParse(obj.ToString(), out var result2))
			{
				return result2;
			}
			switch (obj.ToString()?.Trim().ToLowerInvariant())
			{
			case "true":
			case "yes":
			case "on":
			case "supported":
			case "enable":
			case "enabled":
				return 1;
			default:
				return 0;
			}
		}
		object? Value(params string[] names)
		{
			foreach (string key in names)
			{
				if (values.TryGetValue(key, out object value2) && value2 != null)
				{
					return value2;
				}
			}
			return null;
		}
	}

	internal static bool? AcRecoveryState(object? statusString, object? statusByte)
	{
		string text = statusString?.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return !text.Contains("OFF", StringComparison.OrdinalIgnoreCase);
		}
		string text2 = statusByte?.ToString()?.Trim();
		if (!string.IsNullOrWhiteSpace(text2) && int.TryParse(text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text2.Substring(2) : text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
		{
			return result != 0;
		}
		return null;
	}

	internal static bool HasLogoLightingRegistryLayout(IEnumerable<string> valueNames, IEnumerable<string> subKeyNames)
	{
		if (!valueNames.Any((string name) => name.Contains("Lightbar_logo_", StringComparison.OrdinalIgnoreCase)))
		{
			return subKeyNames.Any((string name) => (name.Contains("lightbar", StringComparison.OrdinalIgnoreCase) || name.Contains("lighbar", StringComparison.OrdinalIgnoreCase)) && name.Contains("logo", StringComparison.OrdinalIgnoreCase));
		}
		return true;
	}

	private static bool DetectLogoLightingRegistry()
	{
		string[] rgbKeyboardPaths = RgbKeyboardPaths;
		foreach (string text in rgbKeyboardPaths)
		{
			RegistryView[] array = new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			foreach (RegistryView registryView in array)
			{
				try
				{
					using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
					using RegistryKey registryKey2 = registryKey.OpenSubKey(text);
					if (registryKey2 != null && HasLogoLightingRegistryLayout(registryKey2.GetValueNames(), registryKey2.GetSubKeyNames()))
					{
						return true;
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine($"Cannot inspect Logo lighting registry at {text} ({registryView}): {ex.Message}");
				}
			}
		}
		return false;
	}

	private static void ReadMachineIdentity(IDictionary<string, object?> identity)
	{
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS");
		if (registryKey == null)
		{
			return;
		}
		string[] array = new string[8] { "SystemManufacturer", "SystemProductName", "SystemFamily", "SystemSKU", "BaseBoardManufacturer", "BaseBoardProduct", "BIOSVendor", "BIOSVersion" };
		foreach (string text in array)
		{
			object value = registryKey.GetValue(text);
			if (value != null)
			{
				identity[text] = value;
			}
		}
	}

	private static bool IsMechrevoName(string value)
	{
		if (!value.Contains("MECHREVO", StringComparison.OrdinalIgnoreCase))
		{
			return value.Contains("MECH REV", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
