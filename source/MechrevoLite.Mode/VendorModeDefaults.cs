using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;

namespace MechrevoLite.Mode;

internal static class VendorModeDefaults
{
	/// <summary>Official console keeps five profiles per mode (Mode{N}_Profile1..5.json).</summary>
	internal const int MaxProfileCount = 5;

	internal sealed record SeedContext(bool Amd, int Pl4Scale, Func<int, int> TccRawToTarget, bool TgpAdjustable, int TgpMaximum, bool DbAdjustable, int DbMaximum, bool TccAdjustable, bool GpuOverclockSupported, bool FanSwitchSpeedSupported, bool FanCurveSupported)
	{
		public static SeedContext From(MechrevoHw hw)
		{
			return new SeedContext(hw.UsesAmdPowerFields, hw.UsesAmdPowerFields ? 1 : hw.Pl4Scale, hw.TccTargetFromRaw, hw.GpuTgpAdjustable, hw.GpuTgpMaximum, hw.GpuDynamicBoostAdjustable, hw.GpuDbMaximum, hw.TccAdjustable, hw.SupportsGpuOverclock, hw.SupportsFanSwitchSpeed, hw.FanCurveSeen);
		}
	}

	internal sealed record VendorProfile(int? Pl1, int? Pl2, int? Pl4, int? AmdSpl, int? AmdSppt, int? AmdFppt, bool TccSwitch, int? TccOffset, int? AmdTccTarget, bool TgpSwitch, int TgpTarget, bool DbSwitch, int? Db, bool OverClockingEnabled, int CoreOffset, int MemoryOffset, bool FanSwitchSpeedEnabled, int FanSwitchSpeed);

	internal static Func<string?>? DataRootOverride { get; set; }

	internal static string? DataRoot()
	{
		Func<string> dataRootOverride = DataRootOverride;
		if (dataRootOverride != null)
		{
			return dataRootOverride();
		}
		try
		{
			string text = GcuCoexistence.ReadServiceImagePath("GCUBridge");
			string text2 = ((text == null) ? null : Path.GetDirectoryName(text.Trim().Trim('"')));
			if (text2 != null)
			{
				string text3 = Path.Combine(text2, "MyControlCenter");
				if (Directory.Exists(text3))
				{
					return text3;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("VendorModeDefaults: service image path unavailable: " + ex.Message);
		}
		InlineArray5<string> buffer = default;
		buffer[0] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		buffer[1] = "L-Mechrevo";
		buffer[2] = "GCU";
		buffer[3] = "AiStoneService";
		buffer[4] = "MyControlCenter";
		string text4 = Path.Combine(buffer);
		if (!Directory.Exists(text4))
		{
			return null;
		}
		return text4;
	}

	internal static int VendorModeNumber(PerfModeKind kind)
	{
		switch (kind)
		{
		case PerfModeKind.Balanced:
			return 1;
		case PerfModeKind.Silent:
			return 2;
		case PerfModeKind.SilentTurbo:
		case PerfModeKind.Turbo:
			return 3;
		default:
			throw new ArgumentOutOfRangeException("kind", kind, "custom modes have no vendor defaults");
		}
	}

	internal static string DefaultFanTableName(PerfModeKind kind)
	{
		switch (kind)
		{
		case PerfModeKind.Balanced:
			return "DefaultFanTable_Gaming";
		case PerfModeKind.Silent:
			return "DefaultFanTable_Office";
		case PerfModeKind.SilentTurbo:
		case PerfModeKind.Turbo:
			return "DefaultFanTable_Turbo";
		default:
			throw new ArgumentOutOfRangeException("kind", kind, "custom modes have no vendor defaults");
		}
	}

	internal static PlDefaultMode PlModeFor(PerfModeKind kind)
	{
		return kind switch
		{
			PerfModeKind.Balanced => PlDefaultMode.Gaming, 
			PerfModeKind.Silent => PlDefaultMode.Office, 
			_ => PlDefaultMode.Turbo, 
		};
	}

	internal static PerfModeSettings Seed(PerfModeKind kind, MechrevoHw hw)
	{
		ArgumentNullException.ThrowIfNull(hw, "hw");
		if (kind == PerfModeKind.Custom)
		{
			return PerfModeSettings.Default;
		}
		SeedContext ctx = SeedContext.From(hw);
		string text = DataRoot();
		string profileJson = null;
		string fanTableJson = null;
		if (text != null)
		{
			profileJson = TryRead(Path.Combine(text, "UserPofiles", $"Mode{VendorModeNumber(kind)}_Profile{ProfileIndexFor(kind, text) + 1}.json"));
			fanTableJson = TryRead(Path.Combine(text, "UserFanTables", DefaultFanTableName(kind) + ".json"));
		}
		PlDefaultsResult plDefaultsResult = MechrevoService.ReadPlDefaults(PerfModeMapping.ToServiceMode(kind));
		return Compose(kind, ctx, profileJson, fanTableJson, plDefaultsResult.Editable ? plDefaultsResult.Values : null);
	}

	/// <summary>
	/// 2026-10-04: build PerfModeSettings for a *specific* firmware profile
	/// (Mode{N}_Profile{index+1}.json) instead of the one MainOption.json binds.
	/// Used by the 30-series "config edit" editor.
	/// </summary>
	/// <summary>
	/// 2026-10-05: user-facing name of a specific firmware profile, i.e. the
	/// "CustomizeName" field the official console edits (falls back to null).
	/// </summary>
	internal static string? ReadProfileCustomizeName(PerfModeKind kind, int profileIndex)
	{
		string text = DataRoot();
		if (text == null)
		{
			return null;
		}
		string text2 = TryRead(Path.Combine(text, "UserPofiles", $"Mode{VendorModeNumber(kind)}_Profile{Math.Clamp(profileIndex, 0, MaxProfileCount - 1) + 1}.json"));
		if (text2 == null)
		{
			return null;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text2);
			if (jsonDocument.RootElement.TryGetProperty("CustomizeName", out JsonElement value) && value.ValueKind == JsonValueKind.String)
			{
				string text3 = value.GetString();
				// The shipped default is just the profile number -- treat that as "unnamed".
				if (!string.IsNullOrWhiteSpace(text3) && text3 != (Math.Clamp(profileIndex, 0, MaxProfileCount - 1) + 1).ToString())
				{
					return text3;
				}
			}
		}
		catch (JsonException)
		{
		}
		return null;
	}

	internal static PerfModeSettings ComposeProfile(PerfModeKind kind, MechrevoHw? hw, int profileIndex)
	{
		if (hw == null)
		{
			return PerfModeSettings.Default;
		}
		SeedContext ctx = SeedContext.From(hw);
		string text = DataRoot();
		string profileJson = null;
		string fanTableJson = null;
		if (text != null)
		{
			int num = Math.Clamp(profileIndex, 0, MaxProfileCount - 1);
			profileJson = TryRead(Path.Combine(text, "UserPofiles", $"Mode{VendorModeNumber(kind)}_Profile{num + 1}.json"));
			fanTableJson = TryRead(Path.Combine(text, "UserFanTables", DefaultFanTableName(kind) + ".json"));
		}
		return Compose(kind, ctx, profileJson, fanTableJson, null);
	}

	internal static PerfModeSettings Compose(PerfModeKind kind, SeedContext ctx, string? profileJson, string? fanTableJson, PlDefaultSet? ecDefaults)
	{
		ArgumentNullException.ThrowIfNull(ctx, "ctx");
		VendorProfile vendorProfile = ParseProfile(profileJson);
		bool flag = (uint)(kind - 2) <= 1u;
		bool flag2 = flag;
		int? pl = ecDefaults?.Pl1 ?? ((!ctx.Amd) ? vendorProfile?.Pl1 : vendorProfile?.AmdSpl);
		int? pl2 = ecDefaults?.Pl2 ?? ((!ctx.Amd) ? vendorProfile?.Pl2 : vendorProfile?.AmdSppt);
		int? num = ecDefaults?.Pl4 ?? ((!ctx.Amd) ? vendorProfile?.Pl4 : vendorProfile?.AmdFppt);
		int? num2;
		if (num.HasValue)
		{
			int valueOrDefault = num.GetValueOrDefault();
			num2 = (ctx.Amd ? valueOrDefault : (valueOrDefault * Math.Max(1, ctx.Pl4Scale)));
		}
		else
		{
			num2 = null;
		}
		int? pl3 = num2;
		bool? tccOn = null;
		int? tccTarget = null;
		if (ctx.TccAdjustable && (object)vendorProfile != null)
		{
			tccOn = vendorProfile.TccSwitch;
			int? num3 = (ctx.Amd ? vendorProfile.AmdTccTarget : vendorProfile.TccOffset);
			if (num3.HasValue)
			{
				int valueOrDefault2 = num3.GetValueOrDefault();
				tccTarget = (ctx.Amd ? valueOrDefault2 : ctx.TccRawToTarget(valueOrDefault2));
			}
		}
		int? gpuTgp = null;
		if (ctx.TgpAdjustable)
		{
			gpuTgp = (((object)vendorProfile != null && vendorProfile.TgpSwitch && vendorProfile.TgpTarget > 0) ? vendorProfile.TgpTarget : ctx.TgpMaximum);
		}
		bool? gpuDynamicBoostOn = null;
		int? gpuDynamicBoost = null;
		if (ctx.DbAdjustable)
		{
			if (flag2)
			{
				gpuDynamicBoostOn = true;
				gpuDynamicBoost = ctx.DbMaximum;
			}
			else if ((object)vendorProfile != null)
			{
				gpuDynamicBoostOn = vendorProfile.DbSwitch;
				gpuDynamicBoost = vendorProfile.Db;
			}
		}
		bool? gpuOverclockOn = null;
		int? gpuCoreOffset = null;
		int? gpuMemoryOffset = null;
		if (ctx.GpuOverclockSupported && (object)vendorProfile != null)
		{
			bool flag3 = vendorProfile.OverClockingEnabled || (kind == PerfModeKind.Turbo && (vendorProfile.CoreOffset != 0 || vendorProfile.MemoryOffset != 0));
			gpuOverclockOn = flag3;
			gpuCoreOffset = (flag3 ? vendorProfile.CoreOffset : 0);
			gpuMemoryOffset = (flag3 ? vendorProfile.MemoryOffset : 0);
		}
		bool? fanSwitchSpeedOn = null;
		int? fanSwitchSpeedMs = null;
		if (ctx.FanSwitchSpeedSupported && (object)vendorProfile != null)
		{
			fanSwitchSpeedOn = vendorProfile.FanSwitchSpeedEnabled;
			if (vendorProfile.FanSwitchSpeed > 0)
			{
				fanSwitchSpeedMs = vendorProfile.FanSwitchSpeed;
			}
		}
		int[] cpuFanDuty = null;
		int[] gpuFanDuty = null;
		if (ctx.FanCurveSupported)
		{
			cpuFanDuty = ParseFanDuty(fanTableJson, cpu: true);
			gpuFanDuty = ParseFanDuty(fanTableJson, cpu: false);
		}
		return new PerfModeSettings
		{
			Pl1 = pl,
			Pl2 = pl2,
			Pl4 = pl3,
			TccOn = tccOn,
			TccTarget = tccTarget,
			GpuTgp = gpuTgp,
			GpuDynamicBoostOn = gpuDynamicBoostOn,
			GpuDynamicBoost = gpuDynamicBoost,
			GpuOverclockOn = gpuOverclockOn,
			GpuCoreOffset = gpuCoreOffset,
			GpuMemoryOffset = gpuMemoryOffset,
			FanSwitchSpeedOn = fanSwitchSpeedOn,
			FanSwitchSpeedMs = fanSwitchSpeedMs,
			CpuFanDuty = cpuFanDuty,
			GpuFanDuty = gpuFanDuty
		};
	}

	internal static VendorProfile? ParseProfile(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement element = Child(rootElement, "CPU");
			JsonElement element2 = Child(rootElement, "GPU");
			JsonElement element3 = Child(rootElement, "FAN");
			return new VendorProfile(Int(element, "PL1"), Int(element, "PL2"), Int(element, "PL4"), Int(element, "AmdSPL"), Int(element, "AmdSPPT"), Int(element, "AmdFPPT"), Int(element, "TccOffsetSwitch") == 1, Int(element, "TccOffset"), Int(element, "AmdTccTarget"), Int(element2, "ConfigurableTGPSwitch") == 1, Int(element2, "ConfigurableTGPTarget").GetValueOrDefault(), Int(element2, "DynamicBoostSwitch") == 1, Int(element2, "DynamicBoost"), Int(rootElement, "OverClockingEnabled") == 1, Int(element2, "CoreClockOffsetOC").GetValueOrDefault(), Int(element2, "MemoryClockOffsetOC").GetValueOrDefault(), Int(element3, "FanSwitchSpeedEnabled") == 1, Int(element3, "FanSwitchSpeed").GetValueOrDefault());
		}
		catch (JsonException ex)
		{
			Logger.WriteLine("VendorModeDefaults: profile JSON unreadable: " + ex.Message);
			return null;
		}
	}

	internal static int[]? ParseFanDuty(string? json, bool cpu)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (!jsonDocument.RootElement.TryGetProperty(cpu ? "CPU" : "GPU", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 16)
			{
				return null;
			}
			int[] array = new int[16];
			int num = 0;
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (!item.TryGetProperty("Duty", out var value2) || !value2.TryGetInt32(out var value3))
				{
					return null;
				}
				array[num++] = Math.Clamp(value3, 0, 100);
			}
			return array;
		}
		catch (JsonException ex)
		{
			Logger.WriteLine("VendorModeDefaults: fan table JSON unreadable: " + ex.Message);
			return null;
		}
	}

	private static int ProfileIndexFor(PerfModeKind kind, string root)
	{
		string text = TryRead(Path.Combine(root, "UserPofiles", "MainOption.json"));
		if (text == null)
		{
			return 0;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			int? num = Int(jsonDocument.RootElement, kind switch
			{
				PerfModeKind.Balanced => "GamingProfileIndex", 
				PerfModeKind.Silent => "OfficeProfileIndex", 
				_ => "TurboProfileIndex", 
			});
			int result;
			if (num.HasValue)
			{
				int valueOrDefault = num.GetValueOrDefault();
				// 2026-10-04: the official console keeps five profiles per mode
				// (Mode{N}_Profile1..5.json). Clamping to 0..1 silently mapped profile
				// 3/4/5 back onto 1/2 and returned the wrong seed defaults.
				result = Math.Clamp(valueOrDefault, 0, MaxProfileCount - 1);
			}
			else
			{
				result = 0;
			}
			return result;
		}
		catch (JsonException)
		{
			return 0;
		}
	}

	private static string? TryRead(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("VendorModeDefaults: cannot read " + path + ": " + ex.Message);
			return null;
		}
	}

	private static JsonElement Child(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
		{
			return default;
		}
		return value;
	}

	private static int? Int(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
		{
			return null;
		}
		switch (value.ValueKind)
		{
		case JsonValueKind.Number:
		{
			if (value.TryGetInt32(out var value2))
			{
				return value2;
			}
			break;
		}
		case JsonValueKind.True:
			return 1;
		case JsonValueKind.False:
			return 0;
		case JsonValueKind.String:
		{
			if (int.TryParse(value.GetString(), out var result))
			{
				return result;
			}
			break;
		}
		}
		return null;
	}
}
