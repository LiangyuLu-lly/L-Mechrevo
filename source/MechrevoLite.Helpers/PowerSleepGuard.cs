using System;
using System.Runtime.InteropServices;
using MechrevoLite.Properties;
using Microsoft.Win32;

namespace MechrevoLite.Helpers;

/// <summary>
/// 睡眠省电（Sleep Guard）6 个开关。
///
/// 2026-10-09（修「快捷开关设置失败」）：**不再直接写**
/// HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\&lt;方案&gt;\...（旧实现）——
/// 30 系真机实测该写入被系统拒绝（UnauthorizedAccessException：Access to the registry key ... is denied；
/// User\PowerSchemes 根只给 Administrators / BUILTIN\Users GENERIC_READ，FullControl 只有 SYSTEM，
/// 各子方案键 DACL 还不一致），而**同一批设置的读取全部成功**（不是缺数据）。
/// 现在改用官方电源 API：PowerReadAC/DCValueIndex 读、PowerWriteAC/DCValueIndex 写
/// （等价 powercfg /setacvalueindex · /setdcvalueindex；写由 Power 服务（LocalSystem）代执行，
/// 所以本进程一侧的键 ACL 不再是障碍），写完**回读校验**（API 回读，API 读不了才退回只读注册表）。
/// 注册表现在只用于**只读**兜底与写前可写性探测记录，绝不写。
/// </summary>
internal static class PowerSleepGuard
{
	private const string SchemeRoot = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

	/// <summary>NO_SUBGROUP_GUID：无子组设置专用（「待机断开网络」在 PowerSettings 里直接挂在根，
	/// 在方案里直接挂在方案键下，没有子组一级）。只读实测（平衡方案 381b4222）：
	/// PowerReadACValueIndex(scheme, NO_SUBGROUP, f15576e8) = AC2/DC0，与
	/// …\User\PowerSchemes\381b4222…\f15576e8 的 AC/DC 完全一致（默认方案值表里是 AC1/DC2 ⇒ 读的确实是这张覆盖表）；
	/// 改传 Guid.Empty 返回 2（ERROR_FILE_NOT_FOUND）。</summary>
	private static readonly Guid NoSubgroup = new Guid("fea3413e-7e05-4911-9a71-700331f1c294");

	/// <summary>一条睡眠设置：开关键名、界面名、API 子组 GUID、API 设置 GUID、开值、关值。</summary>
	private readonly record struct Option(string Key, string Name, Guid Subgroup, Guid Setting, int OnValue, int OffValue);

	// 设置 GUID ↔ API 映射（SubGroupOfPowerSettingsGuid + PowerSettingGuid；注释里给出旧注册表路径）：
	//   sleepnetoff    待机断开网络     Networking connectivity in Standby   → NO_SUBGROUP                             / f15576e8-98b7-4186-b944-eafa664402d9
	//                  旧路径 <方案>\f15576e8-…（无子组级）                  开=0 关=2
	//   sleepaway      封杀离开模式     Allow Away Mode Policy               → 238c9fa8-0aad-41ed-83f4-97be242c8f20 / 25dfa149-5dd1-4736-b5ab-e8a37b5b8187  开=0 关=1
	//   sleepsysreq    忽略系统所需策略 Allow system required policy         → 238c9fa8-…                            / a4b195f5-8225-47d8-8012-9d41369786e2  开=0 关=1
	//   sleeprtcwake   禁用RTC唤醒定时器 Allow wake timers                   → 238c9fa8-…                            / bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d  开=0 关=2
	//   sleepdeep      启用ACPI深度睡眠 Deep Sleep Enabled/Disabled          → 2e601130-5351-4d9d-8e04-252966bad054 / d502f7ee-1dc7-4efd-a55d-f04b6f5c0545  开=1 关=0
	//   sleephibernate 30分钟转休眠     Hibernate after（秒）                → 238c9fa8-…                            / 9d7815a6-7ee4-497e-8888-515a05f02364  开=1800 关=0
	// 六项**全部**有官方 API 表示（无「隐藏到只能写注册表」的项，只读代入已逐项核对，见 2026-10-09 报告）。
	private static readonly Option[] Options = new Option[]
	{
		new Option("sleepnetoff", Strings.QuickSleepNetOff, NoSubgroup, new Guid("f15576e8-98b7-4186-b944-eafa664402d9"), 0, 2),
		new Option("sleepaway", Strings.QuickSleepAway, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("25dfa149-5dd1-4736-b5ab-e8a37b5b8187"), 0, 1),
		new Option("sleepsysreq", Strings.QuickSleepSysReq, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("a4b195f5-8225-47d8-8012-9d41369786e2"), 0, 1),
		new Option("sleeprtcwake", Strings.QuickSleepRtcWake, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), 0, 2),
		new Option("sleepdeep", Strings.QuickSleepDeep, new Guid("2e601130-5351-4d9d-8e04-252966bad054"), new Guid("d502f7ee-1dc7-4efd-a55d-f04b6f5c0545"), 1, 0),
		new Option("sleephibernate", Strings.QuickSleepHibernate, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("9d7815a6-7ee4-497e-8888-515a05f02364"), 1800, 0)
	};

	[DllImport("powrprof.dll")]
	private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

	[DllImport("powrprof.dll")]
	private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint valueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint valueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint valueIndex);

	[DllImport("powrprof.dll")]
	private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint valueIndex);

	private static string AcDc(bool ac)
	{
		return ac ? Strings.SleepAc : Strings.SleepDc;
	}

	/// <summary>按开关键名取设置定义，未知键返回 null。</summary>
	private static Option? Find(string key)
	{
		foreach (Option o in Options)
		{
			if (o.Key == key)
			{
				return o;
			}
		}
		return null;
	}

	/// <summary>该设置在 User\PowerSchemes 下的注册表路径（只读兜底/日志用；NO_SUBGROUP 时无子组一级）。</summary>
	private static string RegistryPath(Guid scheme, Option o)
	{
		string text = scheme.ToString();
		return o.Subgroup.Equals(NoSubgroup)
			? SchemeRoot + "\\" + text + "\\" + o.Setting
			: SchemeRoot + "\\" + text + "\\" + o.Subgroup + "\\" + o.Setting;
	}

	/// <summary>当前活动电源方案 GUID：官方 API 优先，失败退回只读注册表；都失败返回 null。</summary>
	private static Guid? GetActiveScheme()
	{
		try
		{
			nint ptr = IntPtr.Zero;
			uint num = PowerGetActiveScheme(IntPtr.Zero, out ptr);
			if (num == 0 && ptr != IntPtr.Zero)
			{
				try
				{
					return Marshal.PtrToStructure<Guid>(ptr);
				}
				finally
				{
					Marshal.FreeHGlobal(ptr);
				}
			}
			Logger.WriteLine($"PowerSleepGuard: PowerGetActiveScheme failed status={num}, fallback registry");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PowerSleepGuard: get active scheme failed: " + ex.Message);
		}
		try
		{
			using RegistryKey? registryKey = Registry.LocalMachine.OpenSubKey(SchemeRoot);
			if (Guid.TryParse(registryKey?.GetValue("ActivePowerScheme") as string, out Guid result))
			{
				return result;
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("PowerSleepGuard: read active scheme failed: " + ex2.Message);
		}
		return null;
	}

	/// <summary>官方 API 回读原始索引；返回 null 表示 API 读失败（status 给出 Win32 错误码）。</summary>
	private static int? ReadApiRaw(Guid scheme, Option o, bool ac, out uint status)
	{
		Guid schemeGuid = scheme;
		Guid subgroup = o.Subgroup;
		Guid setting = o.Setting;
		uint valueIndex = 0u;
		status = ac
			? PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subgroup, ref setting, out valueIndex)
			: PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subgroup, ref setting, out valueIndex);
		return (status == 0) ? new int?(unchecked((int)valueIndex)) : null;
	}

	/// <summary>只读兜底：直接读 AC/DCSettingIndex（只读打开，不建键、不写值）。</summary>
	private static int? ReadRegistryRaw(Guid scheme, Option o, bool ac)
	{
		string text = RegistryPath(scheme, o);
		try
		{
			using RegistryKey? registryKey = Registry.LocalMachine.OpenSubKey(text);
			object? value = registryKey?.GetValue(ac ? "ACSettingIndex" : "DCSettingIndex");
			return (value is int) ? new int?((int)value) : null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"PowerSleepGuard: read fallback failed key={o.Key} phase={AcDc(ac)} at {text}: {ex.Message}");
			return null;
		}
	}

	/// <summary>回读开关状态（指定插电/离电档）：官方 API 优先，失败退回只读注册表；未知键/无方案返回 null。</summary>
	public static bool? ReadEnabled(string key, bool ac)
	{
		Option? option = Find(key);
		if (!option.HasValue)
		{
			return null;
		}
		Option value = option.Value;
		Guid? activeScheme = GetActiveScheme();
		if (!activeScheme.HasValue)
		{
			return null;
		}
		try
		{
			uint status;
			int? num = ReadApiRaw(activeScheme.Value, value, ac, out status);
			if (num.HasValue)
			{
				return num.Value == value.OnValue;
			}
			Logger.WriteLine($"PowerSleepGuard: read api failed key={value.Key}({value.Name}) phase={AcDc(ac)} status={status}, fallback registry");
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"PowerSleepGuard: read api error key={value.Key}({value.Name}) phase={AcDc(ac)}: {ex.Message}");
		}
		int? num2 = ReadRegistryRaw(activeScheme.Value, value, ac);
		return num2.HasValue ? new bool?(num2.Value == value.OnValue) : null;
	}

	/// <summary>写入前探测（只以可写方式打开项、不写值）：记录本进程对注册表项的写权限，**仅作日志诊断，
	/// 不作为闸门** —— 官方 API 的写由 Power 服务（LocalSystem）代执行，本进程被 ACL 拒绝
	/// 并不代表 API 写会被拒绝（这正是本次要修的点）。返回 writable/denied/absent/异常类型。</summary>
	private static string ProbeRegistryWritable(Guid scheme, Option o)
	{
		try
		{
			using RegistryKey? registryKey = Registry.LocalMachine.OpenSubKey(RegistryPath(scheme, o), writable: true);
			return (registryKey != null) ? "writable" : "absent";
		}
		catch (UnauthorizedAccessException)
		{
			return "denied";
		}
		catch (Exception ex)
		{
			return ex.GetType().Name;
		}
	}

	/// <summary>写入开关状态（指定插电/离电档）并回读校验。
	/// 成功返回 true；失败时 message 为给用户的**专用提示**（设置名 + 插电/离电 + 原因），
	/// 详细上下文（键名/设置 GUID/子组/status/回读值/ACL 探测/异常文本）写日志。</summary>
	public static bool TrySetEnabled(string key, bool enabled, bool ac, out string message)
	{
		message = "";
		Option? option = Find(key);
		if (!option.HasValue)
		{
			Logger.WriteLine("PowerSleepGuard: unknown sleep key " + key);
			message = Strings.QuickSwitchFailed;
			return false;
		}
		Option value = option.Value;
		string phase = AcDc(ac);
		int num = (enabled ? value.OnValue : value.OffValue);
		Guid? activeScheme = GetActiveScheme();
		if (!activeScheme.HasValue)
		{
			Logger.WriteLine($"PowerSleepGuard: set key={value.Key}({value.Name}) phase={phase} want={num}: no active scheme, aborted");
			message = string.Format(Strings.QuickSleepProtected, value.Name, phase);
			return false;
		}
		string acl = ProbeRegistryWritable(activeScheme.Value, value);
		uint status = uint.MaxValue;
		string error = "";
		try
		{
			Guid schemeGuid = activeScheme.Value;
			Guid subgroup = value.Subgroup;
			Guid setting = value.Setting;
			uint valueIndex = unchecked((uint)num);
			status = (ac
				? PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subgroup, ref setting, valueIndex)
				: PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subgroup, ref setting, valueIndex));
		}
		catch (Exception ex)
		{
			error = ex.GetType().Name + ": " + ex.Message;
		}
		int? readback = null;
		string source = "api";
		try
		{
			readback = ReadApiRaw(activeScheme.Value, value, ac, out uint readStatus);
			if (!readback.HasValue)
			{
				source = "registry";
				Logger.WriteLine($"PowerSleepGuard: readback api failed key={value.Key} phase={phase} status={readStatus}, fallback registry");
			}
		}
		catch (Exception ex2)
		{
			error = (error.Length > 0) ? error : (ex2.GetType().Name + ": " + ex2.Message);
			source = "registry";
		}
		if (!readback.HasValue)
		{
			readback = ReadRegistryRaw(activeScheme.Value, value, ac);
		}
		bool flag = status == 0 && error.Length == 0 && readback.HasValue && readback.Value == num;
		Logger.WriteLine($"PowerSleepGuard: set key={value.Key}({value.Name}) phase={phase} want={num} status={status} readback={(readback.HasValue ? readback.Value.ToString() : "n/a")}({source}) confirmed={flag} scheme={activeScheme.Value} sub={(value.Subgroup.Equals(NoSubgroup) ? "NO_SUBGROUP" : value.Subgroup.ToString())} setting={value.Setting} regAcl={acl} reg={RegistryPath(activeScheme.Value, value)}" + ((error.Length > 0) ? (" ex=" + error) : ""));
		if (flag)
		{
			return true;
		}
		message = ((status == 0) ? string.Format(Strings.QuickSleepVerifyFailed, value.Name, phase) : string.Format(Strings.QuickSleepProtected, value.Name, phase));
		return false;
	}
}
