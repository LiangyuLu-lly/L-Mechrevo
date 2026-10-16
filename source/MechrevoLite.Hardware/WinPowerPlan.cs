using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Runtime.CompilerServices;
using MechrevoLite.Properties;using System.Runtime.InteropServices;
using MechrevoLite.Properties;
namespace MechrevoLite.Hardware;

public static class WinPowerPlan
{
	internal readonly record struct ProfileSettings(string Plan, int Boost);

	private const int ACCESS_SCHEME = 16;

	internal const int CustomProfileCount = 4;

	private static readonly Guid GuidCpuSubgroup = new Guid("54533251-82be-4824-96c1-47b60b740d00");

	private static readonly Guid GuidBoostSetting = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

	public static readonly (string Name, int Value)[] BoostModes = new (string, int)[7]
	{
		(Strings.TurboMode0, 0),
		(Strings.TurboMode1, 1),
		(Strings.TurboMode2, 2),
		(Strings.TurboMode3, 3),
		(Strings.TurboMode4, 4),
		(Strings.TurboMode5, 5),
		(Strings.TurboMode6, 6)
	};

	private static readonly (string Guid, string Name)[] WellKnownPlans = new (string, string)[4]
	{
		("381b4222-f694-41f0-9685-ff5bb260df2e", Strings.PowerPlanBalanced),
		("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Strings.PowerPlanHighPerf),
		("a1841308-3541-4fab-bc81-f71556f20b4a", Strings.PowerPlanEnergySave),
		("e9a42b02-d5df-448d-aa00-03f14749eb61", Strings.PowerPlanUltimate)
	};

	internal static Func<int, bool>? ApplyProfileOverride { get; set; }

	[DllImport("powrprof.dll")]
	private static extern uint PowerEnumerate(nint RootPowerKey, nint SchemeGuid, nint SubGroupOfPowerSettingsGuid, uint AccessFlags, uint Index, nint Buffer, ref uint BufferSize);

	[DllImport("powrprof.dll")]
	private static extern uint PowerSetActiveScheme(nint RootPowerKey, ref Guid SchemeGuid);

	[DllImport("powrprof.dll")]
	private static extern uint PowerGetActiveScheme(nint UserPowerKey, out nint ActivePolicyGuid);

	[DllImport("powrprof.dll")]
	private static extern uint PowerWriteACValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid SettingGuid, int AcValueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerWriteDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid SettingGuid, int AcValueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerReadACValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid SettingGuid, out nint AcValueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerReadDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid SettingGuid, out nint DcValueIndex);

	internal static string GetProfilePlanKey(int index)
	{
		return "custom" + ValidateProfileIndex(index) + "_plan";
	}

	internal static string GetProfileBoostKey(int index)
	{
		return "custom" + ValidateProfileIndex(index) + "_boost";
	}

	/// <summary>2026-10-09（修 B1）：自定义档电源设置的新键 —— 与 PerfModeStore 的档位 id 同源
	/// （PerfModeDefinition.CustomId(n) = "custom" + n，PerfModeStore.FieldKey = "perf_" + id + "_" + field）。
	/// 旧键 customN_plan / customN_boost 已废弃，不再读也不再写。</summary>
	internal static string GetStoredPlanKey(int index)
	{
		return "perf_custom" + (ValidateProfileIndex(index) + 1) + "_plan";
	}

	internal static string GetStoredBoostKey(int index)
	{
		return "perf_custom" + (ValidateProfileIndex(index) + 1) + "_boost";
	}

	/// <summary>
	/// 2026-10-09（修 B1「旧键 split-brain」）：**只读，绝不写**。
	/// 旧实现读旧键 customN_plan，键为空时用「当前活动方案」自动播种并 AppConfig.Flush() 落盘 ——
	/// 于是一次托盘切档就会用过期方案覆盖新引擎刚设好的电源计划，并让 SetBoost 永久改写该方案的
	/// 睿频值（AC/DC）。现在改读新键；键缺失就是「本档没配」，不回读、不播种、不落盘。
	/// Boost = -1 表示未配置（0 是合法睿频档位，不能拿它当"空"）。
	/// </summary>
	internal static ProfileSettings GetOrCreateProfileSettings(int index)
	{
		string text = AppConfig.GetString(GetStoredPlanKey(index)) ?? "";
		int num = AppConfig.Get(GetStoredBoostKey(index), -1);
		if (num < 0 || num >= BoostModes.Length)
		{
			num = -1;
		}
		return new ProfileSettings(text, num);
	}

	internal static void EnsureProfileSettings()
	{
		for (int i = 0; i < 4; i++)
		{
			GetOrCreateProfileSettings(i);
		}
	}

	private static int ValidateProfileIndex(int index)
	{
		if ((index < 0 || index >= 4) ? true : false)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		return index;
	}

	public static List<(string Guid, string Name)> GetPlans()
	{
		List<(string, string)> list = new List<(string, string)>();
		try
		{
			uint num = 0u;
			nint num2;
			while (true)
			{
				uint BufferSize = 16u;
				num2 = Marshal.AllocHGlobal(16);
				if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16u, num, num2, ref BufferSize) != 0)
				{
					break;
				}
				Guid guid = (Guid)Marshal.PtrToStructure(num2, typeof(Guid));
				Marshal.FreeHGlobal(num2);
				(string, string) tuple = WellKnownPlans.FirstOrDefault(((string Guid, string Name) p) => p.Guid.Equals(guid.ToString(), StringComparison.OrdinalIgnoreCase));
				list.Add((guid.ToString(), (tuple.Item1 != null) ? tuple.Item2 : string.Format(Strings.PowerPlanN, num + 1)));
				num++;
			}
			Marshal.FreeHGlobal(num2);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("WinPowerPlan enum fail: " + ex.Message);
		}
		// 2026-10-09（用户拍板「电源计划回退旧行为」）：恢复旧版 18.6 的列表口径 ——
		// PowerEnumerate(ACCESS_SCHEME=16) 只枚举出**系统里真实存在**的方案，而「高性能 / 省电 /
		// 卓越性能」在现代 Windows 上默认是隐藏/不存在的（本项目不创建、不删除、不 duplicatescheme
		// 任何方案）。旧版的做法是：枚举结果之后，把已知方案清单 WellKnownPlans（平衡 / 高性能 /
		// 省电 / 卓越性能 的固定 GUID + 本地化名称）里**尚未出现**的项补进下拉。客户端的纯清单合并，
		// 不需要任何系统能力；代价是选到系统里并不存在的那个方案时 PowerSetActiveScheme 会失败，
		// 于是照旧走「电源计划未确认，未保存」提示（与旧版完全一致）。
		(string Guid, string Name)[] wellKnown = WellKnownPlans;
		for (int i = 0; i < wellKnown.Length; i++)
		{
			(string Guid, string Name) plan = wellKnown[i];
			if (!list.Any(((string Guid, string Name) p) => p.Guid.Equals(plan.Guid, StringComparison.OrdinalIgnoreCase)))
			{
				list.Add(plan);
			}
		}
		return list;
	}

	public static string GetActivePlan()
	{
		if (!TryGetActivePlan(out var plan))
		{
			return "";
		}
		return plan.ToString();
	}

	internal static bool IsPlanConfirmed(string requested, string actual)
	{
		if (Guid.TryParse(requested, out var result) && Guid.TryParse(actual, out var result2))
		{
			return result == result2;
		}
		return false;
	}

	internal static bool IsBoostConfirmed(int requested, int ac, int dc)
	{
		if (requested >= 0 && requested < 7 && ac == requested)
		{
			return dc == requested;
		}
		return false;
	}

	private static bool TryGetActivePlan(out Guid plan)
	{
		plan = Guid.Empty;
		nint ActivePolicyGuid = IntPtr.Zero;
		try
		{
			uint num = PowerGetActiveScheme(IntPtr.Zero, out ActivePolicyGuid);
			if (num != 0 || ActivePolicyGuid == IntPtr.Zero)
			{
				Logger.WriteLine($"WinPowerPlan GetActivePlan failed: {num}");
				return false;
			}
			plan = Marshal.PtrToStructure<Guid>(ActivePolicyGuid);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("WinPowerPlan GetActivePlan fail: " + ex.Message);
			return false;
		}
		finally
		{
			if (ActivePolicyGuid != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(ActivePolicyGuid);
			}
		}
	}

	private static bool TryReadBoost(Guid plan, bool ac, out int value)
	{
		Guid SubGroup = GuidCpuSubgroup;
		Guid SettingGuid = GuidBoostSetting;
		uint num = (ac ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref SubGroup, ref SettingGuid, out var AcValueIndex) : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref SubGroup, ref SettingGuid, out AcValueIndex));
		value = ((num == 0) ? ((IntPtr)AcValueIndex).ToInt32() : (-1));
		return num == 0;
	}

	public static bool SetActivePlan(string guid)
	{
		try
		{
			if (!Guid.TryParse(guid, out var result))
			{
				Logger.WriteLine("WinPowerPlan SetActivePlan rejected invalid GUID: " + guid);
				return false;
			}
			if (TryGetActivePlan(out var plan) && plan == result)
			{
				return true;
			}
			uint num = PowerSetActiveScheme(IntPtr.Zero, ref result);
			bool flag = TryGetActivePlan(out var plan2);
			bool flag2 = ((num == 0) & flag) && plan2 == result;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 4);
			defaultInterpolatedStringHandler.AppendLiteral("WinPowerPlan SetActivePlan ");
			defaultInterpolatedStringHandler.AppendFormatted(guid);
			defaultInterpolatedStringHandler.AppendLiteral(" -> status=");
			defaultInterpolatedStringHandler.AppendFormatted(num);
			defaultInterpolatedStringHandler.AppendLiteral(" actual=");
			defaultInterpolatedStringHandler.AppendFormatted(flag ? ((object)plan2) : "unavailable");
			defaultInterpolatedStringHandler.AppendLiteral(" confirmed=");
			defaultInterpolatedStringHandler.AppendFormatted(flag2);
			Logger.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			return flag2;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("WinPowerPlan SetActivePlan fail: " + ex.Message);
			return false;
		}
	}

	public static bool SetBoost(int boost)
	{
		try
		{
			bool flag = ((boost < 0 || boost >= 7) ? true : false);
			if (flag || !TryGetActivePlan(out var SchemeGuid))
			{
				Logger.WriteLine($"WinPowerPlan SetBoost({boost}) rejected");
				return false;
			}
			Guid SubGroup = GuidCpuSubgroup;
			Guid SettingGuid = GuidBoostSetting;
			uint num = PowerWriteACValueIndex(IntPtr.Zero, ref SchemeGuid, ref SubGroup, ref SettingGuid, boost);
			uint num2 = PowerWriteDCValueIndex(IntPtr.Zero, ref SchemeGuid, ref SubGroup, ref SettingGuid, boost);
			uint num3 = PowerSetActiveScheme(IntPtr.Zero, ref SchemeGuid);
			bool flag2 = TryReadBoost(SchemeGuid, ac: true, out var value);
			bool flag3 = TryReadBoost(SchemeGuid, ac: false, out var value2);
			bool flag4 = ((num == 0 && num2 == 0 && num3 == 0) & flag2 & flag3) && IsBoostConfirmed(boost, value, value2);
			Logger.WriteLine($"WinPowerPlan SetBoost({boost}) AC={num}/{value} DC={num2}/{value2} activate={num3} confirmed={flag4}");
			return flag4;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("WinPowerPlan SetBoost fail: " + ex.Message);
			return false;
		}
	}

	public static int GetBoost()
	{
		try
		{
			Guid plan;
			int value;
			return (TryGetActivePlan(out plan) && TryReadBoost(plan, ac: true, out value)) ? value : 2;
		}
		catch
		{
			return 2;
		}
	}

	internal static bool TryGetBoost(out int ac, out int dc)
	{
		ac = (dc = -1);
		try
		{
			Guid plan;
			return TryGetActivePlan(out plan) && TryReadBoost(plan, ac: true, out ac) && TryReadBoost(plan, ac: false, out dc);
		}
		catch
		{
			return false;
		}
	}

	public static bool ApplyProfile(int index)
	{
		Func<int, bool> applyProfileOverride = ApplyProfileOverride;
		if (applyProfileOverride != null)
		{
			return applyProfileOverride(index);
		}
		// 2026-10-09（修 B1）：托盘切档（SettingsForm）与电源变化重应用（ModeControl）以前经这里读
		// **旧键** customN_plan / customN_boost —— 旧键从不清除、还会被「当前活动方案」自动播种，
		// 等于用过期方案覆盖新引擎刚设好的电源计划（并永久改写该方案的睿频值）。
		// 现在：只读新键 perf_customN_*，没有配置就什么都不做（不再有任何写盘 / 播种）；
		// 方案与睿频都确认才算成功（未配置的那一项视为"无需确认"）。
		ProfileSettings profileSettings = GetOrCreateProfileSettings(index);
		bool planConfirmed = string.IsNullOrWhiteSpace(profileSettings.Plan) || SetActivePlan(profileSettings.Plan);
		bool boostConfirmed = planConfirmed && (profileSettings.Boost < 0 || SetBoost(profileSettings.Boost));
		string planText = (string.IsNullOrWhiteSpace(profileSettings.Plan) ? "(unset)" : profileSettings.Plan);
		string boostText = ((profileSettings.Boost < 0) ? "(unset)" : profileSettings.Boost.ToString());
		Logger.WriteLine($"WinPowerPlan ApplyProfile({index}): plan={planText} boost={boostText} confirmed={boostConfirmed}");
		return boostConfirmed;
	}
}
