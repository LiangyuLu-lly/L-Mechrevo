using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MechrevoLite.Mode;

public static class PerfModeStore
{
	internal const string CustomListKey = "perf_customs";

	internal const string ActiveKey = "perf_active";

	internal const string LastCustomKey = "perf_last_custom";

	internal const string SchemaKey = "perf_schema";

	internal const int CurrentSchema = 1;

	public static string ActiveModeId
	{
		get
		{
			string text = AppConfig.GetString("perf_active");
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			return PerfModeDefinition.BuiltInId(PerfModeKind.Balanced);
		}
		set
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				AppConfig.Set("perf_active", value);
				AppConfig.Flush();
			}
		}
	}

	public static bool HasStoredActive => !string.IsNullOrWhiteSpace(AppConfig.GetString("perf_active"));

	public static string? LastCustomId
	{
		get
		{
			string text = AppConfig.GetString("perf_last_custom");
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			return null;
		}
		set
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				AppConfig.Remove("perf_last_custom");
			}
			else
			{
				AppConfig.Set("perf_last_custom", value);
			}
			AppConfig.Flush();
		}
	}

	private static string NameKey(string id)
	{
		return "perf_name_" + id;
	}

	private static string FieldKey(string id, string field)
	{
		return "perf_" + id + "_" + field;
	}

	private static int? GetInt(string id, string field)
	{
		string name = FieldKey(id, field);
		if (!AppConfig.Exists(name))
		{
			return null;
		}
		return AppConfig.Get(name);
	}

	private static bool? GetBool(string id, string field)
	{
		string name = FieldKey(id, field);
		if (!AppConfig.Exists(name))
		{
			return null;
		}
		return AppConfig.Get(name) != 0;
	}

	private static int[]? GetCurve(string id, string field)
	{
		string text = AppConfig.GetString(FieldKey(id, field));
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0)
		{
			return null;
		}
		int[] array2 = new int[16];
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = ((i < array.Length && int.TryParse(array[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) ? Math.Clamp(result, 0, 100) : 0);
		}
		return array2;
	}

	private static void PutInt(string id, string field, int? value)
	{
		string name = FieldKey(id, field);
		if (value.HasValue)
		{
			AppConfig.Set(name, value.Value);
		}
		else
		{
			AppConfig.Remove(name);
		}
	}

	private static void PutBool(string id, string field, bool? value)
	{
		string name = FieldKey(id, field);
		if (value.HasValue)
		{
			AppConfig.Set(name, value.Value ? 1 : 0);
		}
		else
		{
			AppConfig.Remove(name);
		}
	}

	private static void PutCurve(string id, string field, int[]? value)
	{
		string name = FieldKey(id, field);
		if (value != null && value.Length > 0)
		{
			AppConfig.Set(name, string.Join(',', value));
		}
		else
		{
			AppConfig.Remove(name);
		}
	}

	public static PerfModeSettings LoadSettings(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id, "id");
		string text = AppConfig.GetString(FieldKey(id, "plan"));
		return new PerfModeSettings
		{
			Pl1 = GetInt(id, "pl1"),
			Pl2 = GetInt(id, "pl2"),
			Pl4 = GetInt(id, "pl4"),
			GpuTgp = GetInt(id, "tgp"),
			GpuDynamicBoostOn = GetBool(id, "dbon"),
			GpuDynamicBoost = GetInt(id, "db"),
			CpuFanDuty = GetCurve(id, "fancpu"),
			GpuFanDuty = GetCurve(id, "fangpu"),
			TccOn = GetBool(id, "tccon"),
			TccTarget = GetInt(id, "tcc"),
			// （30 系 · SMU 功耗时间窗）：mode_tune_* 新键（秒）；缺失 = null（不显示该行）。
			StapmTimeSeconds = GetInt(id, "stapmtime"),
			GpuOverclockOn = GetBool(id, "ocon"),
			GpuCoreOffset = GetInt(id, "occore"),
			GpuMemoryOffset = GetInt(id, "ocmem"),
			FanBoost = GetBool(id, "fanboost"),
			FanSwitchSpeedOn = GetBool(id, "fsson"),
			FanSwitchSpeedMs = GetInt(id, "fss"),
			WindowsPowerMode = GetInt(id, "power"),
			CpuBoost = GetInt(id, "boost"),
			PowerPlanGuid = (string.IsNullOrWhiteSpace(text) ? null : text),
			RefreshHz = GetInt(id, "hz")
		};
	}

	public static void SaveSettings(string id, PerfModeSettings settings)
	{
		ArgumentException.ThrowIfNullOrEmpty(id, "id");
		ArgumentNullException.ThrowIfNull(settings, "settings");
		PutInt(id, "pl1", settings.Pl1);
		PutInt(id, "pl2", settings.Pl2);
		PutInt(id, "pl4", settings.Pl4);
		PutInt(id, "tgp", settings.GpuTgp);
		PutBool(id, "dbon", settings.GpuDynamicBoostOn);
		PutInt(id, "db", settings.GpuDynamicBoost);
		PutCurve(id, "fancpu", settings.CpuFanDuty);
		PutCurve(id, "fangpu", settings.GpuFanDuty);
		PutBool(id, "tccon", settings.TccOn);
		PutInt(id, "tcc", settings.TccTarget);
		// （30 系 · SMU 功耗时间窗）：按档保存（秒）；null ⇒ 删除该键（旧配置自然保持兼容）。
		PutInt(id, "stapmtime", settings.StapmTimeSeconds);
		PutBool(id, "ocon", settings.GpuOverclockOn);
		PutInt(id, "occore", settings.GpuCoreOffset);
		PutInt(id, "ocmem", settings.GpuMemoryOffset);
		PutBool(id, "fanboost", settings.FanBoost);
		PutBool(id, "fsson", settings.FanSwitchSpeedOn);
		PutInt(id, "fss", settings.FanSwitchSpeedMs);
		PutInt(id, "power", settings.WindowsPowerMode);
		PutInt(id, "boost", settings.CpuBoost);
		string name = FieldKey(id, "plan");
		if (string.IsNullOrWhiteSpace(settings.PowerPlanGuid))
		{
			AppConfig.Remove(name);
		}
		else
		{
			AppConfig.Set(name, settings.PowerPlanGuid);
		}
		PutInt(id, "hz", settings.RefreshHz);
		AppConfig.Flush();
	}

	public static void ResetSettings(string id)
	{
		SaveSettings(id, PerfModeSettings.Default);
	}

	internal static List<int> LoadCustomOrdinals()
	{
		string text = AppConfig.GetString("perf_customs");
		if (string.IsNullOrWhiteSpace(text))
		{
			return new List<int> { 1 };
		}
		List<int> list = new List<int>();
		string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			if (int.TryParse(array[i], NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0 && result <= 12 && !list.Contains(result))
			{
				list.Add(result);
			}
		}
		if (list.Count <= 0)
		{
			return new List<int> { 1 };
		}
		return list;
	}

	private static void SaveCustomOrdinals(IEnumerable<int> ordinals)
	{
		AppConfig.Set("perf_customs", string.Join(',', ordinals));
		AppConfig.Flush();
	}

	public static IReadOnlyList<PerfModeDefinition> Load()
	{
		List<PerfModeDefinition> list = new List<PerfModeDefinition>(8);
		PerfModeKind[] builtInOrder = PerfModeCollection.BuiltInOrder;
		foreach (PerfModeKind kind in builtInOrder)
		{
			string id = PerfModeDefinition.BuiltInId(kind);
			list.Add(new PerfModeDefinition(id, kind, LoadName(id), LoadSettings(id)));
		}
		foreach (int item in LoadCustomOrdinals())
		{
			string id2 = PerfModeDefinition.CustomId(item);
			list.Add(new PerfModeDefinition(id2, PerfModeKind.Custom, LoadName(id2), LoadSettings(id2)));
		}
		return list;
	}

	private static string? LoadName(string id)
	{
		return PerfModeCollection.SanitizeUserName(AppConfig.GetString(NameKey(id)));
	}

	public static void SaveName(string id, string? name)
	{
		ArgumentException.ThrowIfNullOrEmpty(id, "id");
		string text = PerfModeCollection.SanitizeUserName(name);
		if (text == null)
		{
			AppConfig.Remove(NameKey(id));
		}
		else
		{
			AppConfig.Set(NameKey(id), text);
		}
		AppConfig.Flush();
	}

	public static PerfModeDefinition? AddCustom(string? seedFromId = null)
	{
		IReadOnlyList<PerfModeDefinition> existing = Load();
		if (!PerfModeCollection.CanAdd(existing))
		{
			return null;
		}
		int num = PerfModeCollection.NextCustomOrdinal(existing);
		if (num < 0)
		{
			return null;
		}
		string id = PerfModeDefinition.CustomId(num);
		PerfModeSettings settings = (string.IsNullOrEmpty(seedFromId) ? PerfModeSettings.Default : LoadSettings(seedFromId));
		SaveSettings(id, settings);
		List<int> list = LoadCustomOrdinals();
		if (!list.Contains(num))
		{
			list.Add(num);
		}
		SaveCustomOrdinals(list);
		return new PerfModeDefinition(id, PerfModeKind.Custom, null, settings);
	}

	public static bool RemoveCustom(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id, "id");
		if (!PerfModeCollection.CanRemove(Load(), id))
		{
			return false;
		}
		if (!PerfModeCollection.TryParseCustomOrdinal(id, out var ordinal))
		{
			return false;
		}
		List<int> list = LoadCustomOrdinals();
		list.Remove(ordinal);
		SaveCustomOrdinals(list);
		ResetSettings(id);
		AppConfig.Remove(NameKey(id));
		SaveSlots(Release(LoadSlots(5), id));
		if (string.Equals(ActiveModeId, id, StringComparison.Ordinal))
		{
			ActiveModeId = PerfModeDefinition.BuiltInId(PerfModeKind.Balanced);
		}
		AppConfig.Flush();
		return true;
	}

	private static IReadOnlyList<FirmwareSlotState> Release(IReadOnlyList<FirmwareSlotState> slots, string id)
	{
		return FirmwareSlotPlanner.Release(slots, id);
	}

	private static string SourceKey(int powerLineStatus)
	{
		return "perf_active_src" + powerLineStatus.ToString(CultureInfo.InvariantCulture);
	}

	public static string? ActiveFor(int powerLineStatus)
	{
		string text = AppConfig.GetString(SourceKey(powerLineStatus));
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	public static void SetActiveFor(int powerLineStatus, string id)
	{
		if (!string.IsNullOrWhiteSpace(id))
		{
			AppConfig.Set(SourceKey(powerLineStatus), id);
			AppConfig.Flush();
		}
	}

	public static void MigrateLegacy()
	{
		if (AppConfig.Get("perf_schema", 0) >= 1)
		{
			return;
		}
		(string, PerfModeKind)[] array = new (string, PerfModeKind)[4]
		{
			("office", PerfModeKind.Silent),
			("gaming", PerfModeKind.Balanced),
			("turbo", PerfModeKind.Turbo),
			("silentturbo", PerfModeKind.SilentTurbo)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, PerfModeKind) tuple = array[i];
			string item = tuple.Item1;
			PerfModeKind item2 = tuple.Item2;
			string text = "mode_tune_" + item + "_";
			string id = PerfModeDefinition.BuiltInId(item2);
			PerfModeSettings perfModeSettings = LoadSettings(id);
			int num = AppConfig.Get(text + "power");
			int num2 = AppConfig.Get(text + "boost");
			int num3 = AppConfig.Get(text + "fanboost");
			int num4 = AppConfig.Get(text + "hz", 0);
			bool flag = item2 == PerfModeKind.Turbo && AppConfig.Exists(text + "autooc") && AppConfig.Get(text + "autooc", 1) == 0;
			PerfModeSettings perfModeSettings2 = perfModeSettings with
			{
				WindowsPowerMode = ((num >= 0 && num <= 2) ? new int?(num) : perfModeSettings.WindowsPowerMode),
				CpuBoost = ((num2 >= 0 && num2 <= 6) ? new int?(num2) : perfModeSettings.CpuBoost)
			};
			PerfModeSettings perfModeSettings3 = perfModeSettings2;
			bool flag2 = (uint)num3 <= 1u;
			perfModeSettings3.FanBoost = (flag2 ? new bool?(num3 == 1) : perfModeSettings.FanBoost);
			perfModeSettings2.RefreshHz = ((num4 > 0) ? new int?(num4) : perfModeSettings.RefreshHz);
			perfModeSettings2.GpuOverclockOn = (flag ? new bool?(false) : perfModeSettings.GpuOverclockOn);
			perfModeSettings2.GpuCoreOffset = (flag ? new int?(0) : perfModeSettings.GpuCoreOffset);
			perfModeSettings2.GpuMemoryOffset = (flag ? new int?(0) : perfModeSettings.GpuMemoryOffset);
			perfModeSettings = perfModeSettings2;
			SaveSettings(id, perfModeSettings);
			string[] array2 = new string[5] { "power", "boost", "fanboost", "hz", "autooc" };
			foreach (string text2 in array2)
			{
				AppConfig.Remove(text + text2);
			}
		}
		int num5 = Math.Clamp(AppConfig.Get("custom_last_profile", 0), 0, 4);
		string text3 = PerfModeDefinition.CustomId(1);
		PerfModeSettings perfModeSettings4 = LoadSettings(text3);
		if (num5 <= 3)
		{
			string input = AppConfig.GetString("custom" + num5.ToString(CultureInfo.InvariantCulture) + "_plan");
			int num6 = AppConfig.Get("custom" + num5.ToString(CultureInfo.InvariantCulture) + "_boost");
			perfModeSettings4 = perfModeSettings4 with
			{
				PowerPlanGuid = (Guid.TryParse(input, out var result) ? result.ToString() : perfModeSettings4.PowerPlanGuid),
				CpuBoost = ((num6 >= 0 && num6 <= 6) ? new int?(num6) : perfModeSettings4.CpuBoost)
			};
			SaveSettings(text3, perfModeSettings4);
		}
		if (!AppConfig.Exists("perf_customs"))
		{
			SaveCustomOrdinals(new int[1] { 1 });
		}
		IReadOnlyList<FirmwareSlotState> readOnlyList = LoadSlots(5);
		if (readOnlyList.All((FirmwareSlotState s) => s.IsFree))
		{
			SaveSlots(FirmwareSlotPlanner.Assign(readOnlyList, num5, text3, perfModeSettings4.FirmwareSignature(), NowStamp()));
		}
		if (LastCustomId == null)
		{
			LastCustomId = text3;
		}
		if (!HasStoredActive && AppConfig.Exists("performance_mode"))
		{
			string text4 = AppConfig.Get("performance_mode") switch
			{
				0 => PerfModeDefinition.BuiltInId(PerfModeKind.Balanced), 
				1 => PerfModeDefinition.BuiltInId(PerfModeKind.Turbo), 
				2 => PerfModeDefinition.BuiltInId(PerfModeKind.Silent), 
				3 => text3, 
				_ => null, 
			};
			if (text4 != null)
			{
				ActiveModeId = text4;
			}
		}
		AppConfig.Set("perf_schema", 1);
		AppConfig.Flush();
	}

	public static IReadOnlyList<FirmwareSlotState> LoadSlots(int slotCount)
	{
		int num = Math.Clamp(slotCount, 1, 5);
		List<FirmwareSlotState> list = new List<FirmwareSlotState>(num);
		for (int i = 0; i < num; i++)
		{
			string text = AppConfig.GetString(FirmwareSlotPlanner.OwnerKey(i));
			string signature = AppConfig.GetString(FirmwareSlotPlanner.SignatureKey(i));
			long lastUsedTicks = AppConfig.Get(FirmwareSlotPlanner.LastUsedKey(i), 0);
			list.Add(new FirmwareSlotState(i, string.IsNullOrWhiteSpace(text) ? null : text, signature, lastUsedTicks));
		}
		return list;
	}

	public static void SaveSlots(IReadOnlyList<FirmwareSlotState> slots)
	{
		ArgumentNullException.ThrowIfNull(slots, "slots");
		foreach (FirmwareSlotState slot in slots)
		{
			string name = FirmwareSlotPlanner.OwnerKey(slot.Index);
			string name2 = FirmwareSlotPlanner.SignatureKey(slot.Index);
			if (slot.IsFree)
			{
				AppConfig.Remove(name);
				AppConfig.Remove(name2);
			}
			else
			{
				AppConfig.Set(name, slot.OwnerModeId);
				AppConfig.Set(name2, slot.Signature ?? "");
			}
			AppConfig.Set(FirmwareSlotPlanner.LastUsedKey(slot.Index), (int)Math.Clamp(slot.LastUsedTicks, 0L, 2147483647L));
		}
		AppConfig.Flush();
	}

	public static long NowStamp()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1700000000;
	}
}
