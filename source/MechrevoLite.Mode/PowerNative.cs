using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace MechrevoLite.Mode;

internal class PowerNative
{
	public enum ACLineStatus : byte
	{
		Offline = 0,
		Online = 1,
		Unknown = byte.MaxValue
	}

	public enum BatteryFlag : byte
	{
		High = 1,
		Low = 2,
		Critical = 4,
		Charging = 8,
		NoSystemBattery = 128,
		Unknown = byte.MaxValue
	}

	[StructLayout(LayoutKind.Sequential)]
	public class SystemPowerStatus
	{
		public ACLineStatus ACLineStatus;

		public BatteryFlag BatteryFlag;

		public byte BatteryLifePercent;

		public byte SystemStatusFlag;

		public int BatteryLifeTime;

		public int BatteryFullLifeTime;
	}

	private static readonly Guid GUID_CPU = new Guid("54533251-82be-4824-96c1-47b60b740d00");

	private static readonly Guid GUID_BOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

	private static Guid GUID_SLEEP_SUBGROUP = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");

	private static Guid GUID_HIBERNATEIDLE = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");

	private static Guid GUID_SYSTEM_BUTTON_SUBGROUP = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");

	private static Guid GUID_LIDACTION = new Guid("5CA83367-6E45-459F-A27B-476B1D01C936");

	private static Guid GUID_SUB_PCIEXPRESS = new Guid("501a4d13-42af-4429-9fd1-a8218c268e20");

	private static Guid GUID_PCI_EXPRESS_ASPM = new Guid("ee12f906-d277-404b-b6da-e5fa1a576df5");

	private const string POWER_SILENT = "961cc777-2547-4f9d-8174-7d86181b8a7a";

	private const string POWER_BALANCED = "00000000-0000-0000-0000-000000000000";

	private const string POWER_TURBO = "ded574b5-45a0-4f42-8737-46345c09c238";

	private const string PLAN_BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e";

	private const string PLAN_HIGH_PERFORMANCE = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

	private static List<string> overlays = new List<string> { "00000000-0000-0000-0000-000000000000", "ded574b5-45a0-4f42-8737-46345c09c238", "961cc777-2547-4f9d-8174-7d86181b8a7a" };

	public static Dictionary<string, string> powerModes = new Dictionary<string, string>
	{
		{ "961cc777-2547-4f9d-8174-7d86181b8a7a", "Best Power Efficiency" },
		{ "00000000-0000-0000-0000-000000000000", "Balanced" },
		{ "ded574b5-45a0-4f42-8737-46345c09c238", "Best Performance" },
		{ "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High Performance Plan" }
	};

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerWriteDCValueIndex(nint RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid, int AcValueIndex);

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerWriteACValueIndex(nint RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid, int AcValueIndex);

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerReadACValueIndex(nint RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid, out nint AcValueIndex);

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerReadDCValueIndex(nint RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid, [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid, out nint AcValueIndex);

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerSetActiveScheme(nint RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid);

	[DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
	private static extern uint PowerGetActiveScheme(nint UserPowerKey, out nint ActivePolicyGuid);

	[DllImport("powrprof.dll")]
	public static extern uint PowerGetActualOverlayScheme(out Guid ActualOverlayGuid);

	[DllImport("powrprof.dll")]
	public static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

	[DllImport("powrprof.dll")]
	public static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

	private static Guid GetActiveScheme()
	{
		PowerGetActiveScheme(IntPtr.Zero, out var ActivePolicyGuid);
		return (Guid)Marshal.PtrToStructure(ActivePolicyGuid, typeof(Guid));
	}

	public static int GetCPUBoost()
	{
		Guid activeScheme = GetActiveScheme();
		PowerReadACValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, out var AcValueIndex);
		return ((IntPtr)AcValueIndex).ToInt32();
	}

	/// <summary>只写电池（DC）侧的处理器性能提升模式。</summary>
	public static void SetCPUBoostDc(int boost)
	{
		Guid activeScheme = GetActiveScheme();
		PowerWriteDCValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, boost);
		PowerSetActiveScheme(IntPtr.Zero, activeScheme);
	}

	/// <summary>读插电（AC）/电池（DC）两套处理器性能提升模式。</summary>
	public static void GetCPUBoostBoth(out int ac, out int dc)
	{
		Guid activeScheme = GetActiveScheme();
		PowerReadACValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, out var acValue);
		PowerReadDCValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, out var dcValue);
		ac = ((IntPtr)acValue).ToInt32();
		dc = ((IntPtr)dcValue).ToInt32();
	}

	public static void SetCPUBoost(int boost = 0)
	{
		Guid activeScheme = GetActiveScheme();
		if (boost != GetCPUBoost())
		{
			PowerWriteACValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, boost);
			PowerSetActiveScheme(IntPtr.Zero, activeScheme);
			PowerWriteDCValueIndex(IntPtr.Zero, activeScheme, GUID_CPU, GUID_BOOST, boost);
			PowerSetActiveScheme(IntPtr.Zero, activeScheme);
			Logger.WriteLine("Boost " + boost);
		}
	}

	public static void SetPowerMode(string scheme)
	{
		if (scheme == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
		{
			SetPowerPlan(scheme);
			return;
		}
		// 2026-10-09（修 B2「强制切回平衡」）：这里原来无条件调 SetPowerPlan(AppConfig.GetModeString("scheme"))，
		// 而 "scheme" 早先既没进 Modes 的 schema、也没有任何来源 ⇒ 恒为 null ⇒ SetPowerPlan(null) 落到
		// 「null 就切 381b4222（平衡）」的兜底，于是**每次切档都把用户/新引擎设好的电源方案抢回平衡**
		// （真机日志签名：Power Plan 89a53c2b-… -> 381b4222-… :OK 紧跟 SetPerformanceMode）。
		// 现在：只有确实配了该档的 scheme_N 才切；没配就一个字节都不动。
		string modeScheme = AppConfig.GetModeString("scheme");
		if (!string.IsNullOrWhiteSpace(modeScheme))
		{
			SetPowerPlan(modeScheme);
		}
		if (overlays.Contains(scheme))
		{
			// 对齐 OpenRevo：直接调用 PowerSetActiveOverlayScheme，不先回读、
			// 不因回读不一致而跳过，也不回退/改切 High Performance 电源计划。
			Guid guid = new Guid(scheme);
			try
			{
				uint num = PowerSetActiveOverlayScheme(guid);
				if (num == 0)
				{
					Logger.WriteLine("Power Mode -> " + scheme + ":OK");
				}
				else
				{
					Logger.WriteError("[power] PowerSetActiveOverlayScheme failed: scheme=" + scheme + " hr=" + num);
				}
			}
			catch (Exception ex)
			{
				// 极老系统没有 PowerSetActiveOverlayScheme：只记日志，不影响切档。
				Logger.WriteError("[power] PowerSetActiveOverlayScheme unavailable: " + ex.GetType().Name);
			}
		}
	}

	public static void SetPowerPlan(string scheme)
	{
		if (!overlays.Contains(scheme))
		{
			// 2026-10-09（修 B2）：原来是「scheme == null ⇒ 切成 381b4222（平衡）」的兜底，正是它把方案
			// 抢回平衡。现在只接受一个合法的方案 GUID；空的 / 不是 GUID 的输入一律不动系统设置
			// （电源方案的权威来源是 PerfModeApplyPlanner 的 ApplyPowerPlan 步与电源计划下拉）。
			if (string.IsNullOrWhiteSpace(scheme) || !Guid.TryParse(scheme, out var result))
			{
				Logger.WriteLine("Power Plan unchanged: invalid/empty scheme '" + (scheme ?? "(null)") + "'");
				return;
			}
			string text = GetActiveScheme().ToString();
			if (!(text == result.ToString()))
			{
				uint num = PowerSetActiveScheme(IntPtr.Zero, result);
				Logger.WriteLine($"Power Plan {text} -> {result} :" + ((num == 0) ? "OK" : ((object)num)));
			}
		}
	}

	public static string GetDefaultPowerMode(int mode)
	{
		return mode switch
		{
			1 => "ded574b5-45a0-4f42-8737-46345c09c238", 
			2 => "961cc777-2547-4f9d-8174-7d86181b8a7a", 
			3 => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", 
			_ => "00000000-0000-0000-0000-000000000000", 
		};
	}

	public static void SetPowerMode(int mode)
	{
		SetPowerMode(GetDefaultPowerMode(mode));
	}

	internal static bool ApplyMechrevoPowerModeAutomation(bool enabled, int operatingMode)
	{
		try
		{
			Guid guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
			string g = ((!enabled) ? "00000000-0000-0000-0000-000000000000" : (operatingMode switch
			{
				0 => "961cc777-2547-4f9d-8174-7d86181b8a7a", 
				2 => "ded574b5-45a0-4f42-8737-46345c09c238", 
				_ => "00000000-0000-0000-0000-000000000000", 
			}));
			Guid guid2 = new Guid(g);
			Guid guid3 = Guid.Empty;
			Guid EffectiveOverlayGuid = Guid.Empty;
			uint num = 0u;
			uint num2 = 0u;
			uint num3 = 0u;
			bool flag = false;
			for (int i = 0; i < 3; i++)
			{
				if (flag)
				{
					break;
				}
				num = ((!(GetActiveScheme() == guid)) ? PowerSetActiveScheme(IntPtr.Zero, guid) : 0u);
				num2 = PowerSetActiveOverlayScheme(guid2);
				num3 = PowerGetEffectiveOverlayScheme(out EffectiveOverlayGuid);
				guid3 = GetActiveScheme();
				flag = num == 0 && num2 == 0 && num3 == 0 && guid3 == guid && EffectiveOverlayGuid == guid2;
				if (!flag && i < 2)
				{
					Thread.Sleep(100);
				}
			}
			Logger.WriteLine($"High performance power automation enabled={enabled} opMode={operatingMode} plan={guid3} overlay={EffectiveOverlayGuid} status={num}/{num2}/{num3} confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("High performance power automation failed: " + ex.Message);
			return false;
		}
	}

	public static int GetASPM()
	{
		Guid activeScheme = GetActiveScheme();
		PowerReadACValueIndex(IntPtr.Zero, activeScheme, GUID_SUB_PCIEXPRESS, GUID_PCI_EXPRESS_ASPM, out var AcValueIndex);
		return ((IntPtr)AcValueIndex).ToInt32();
	}

	public static void SetASPM(int status = 0)
	{
		Guid activeScheme = GetActiveScheme();
		int aSPM = GetASPM();
		if (aSPM != status)
		{
			PowerWriteACValueIndex(IntPtr.Zero, activeScheme, GUID_SUB_PCIEXPRESS, GUID_PCI_EXPRESS_ASPM, status);
			PowerSetActiveScheme(IntPtr.Zero, activeScheme);
			Logger.WriteLine($"Changed AC ASPM {aSPM} -> {status}");
		}
	}

	public static void SetBalancedASPM(int status = 0)
	{
		if (!(GetActiveScheme().ToString() != "381b4222-f694-41f0-9685-ff5bb260df2e"))
		{
			SetASPM(status);
		}
	}

	[DllImport("Kernel32")]
	private static extern bool GetSystemPowerStatus(SystemPowerStatus sps);

	internal static (bool OnAc, bool Charging, int Percent)? GetChargeState()
	{
		try
		{
			SystemPowerStatus systemPowerStatus = new SystemPowerStatus();
			if (!GetSystemPowerStatus(systemPowerStatus))
			{
				return null;
			}
			if (systemPowerStatus.BatteryFlag == BatteryFlag.Unknown || (systemPowerStatus.BatteryFlag & BatteryFlag.NoSystemBattery) != 0)
			{
				return null;
			}
			if (systemPowerStatus.BatteryLifePercent > 100)
			{
				return null;
			}
			return (systemPowerStatus.ACLineStatus == ACLineStatus.Online, (systemPowerStatus.BatteryFlag & BatteryFlag.Charging) != 0, systemPowerStatus.BatteryLifePercent);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't read battery charge state: " + ex.Message);
			return null;
		}
	}

	public static bool GetBatterySaverStatus()
	{
		try
		{
			object value = Registry.GetValue("HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control\\Power", "EnergySaverState", null);
			if (value == null)
			{
				SystemPowerStatus systemPowerStatus = new SystemPowerStatus();
				GetSystemPowerStatus(systemPowerStatus);
				return systemPowerStatus.SystemStatusFlag > 0;
			}
			return (int)value == 1;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't check EnergySaverState" + ex.Message);
			return false;
		}
	}

	internal static bool WouldSilentlyUndo(Guid activePlan, Guid requestedPlan)
	{
		Guid guid = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
		Guid guid2 = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
		if (activePlan == guid)
		{
			return requestedPlan == guid2;
		}
		return false;
	}

	internal static bool? IsOverlayActive(int overlayIndex)
	{
		try
		{
			if ((overlayIndex < 0 || overlayIndex > 2) ? true : false)
			{
				return null;
			}
			Guid guid = new Guid(overlayIndex switch
			{
				0 => "961cc777-2547-4f9d-8174-7d86181b8a7a", 
				2 => "ded574b5-45a0-4f42-8737-46345c09c238", 
				_ => "00000000-0000-0000-0000-000000000000", 
			});
			if (PowerGetEffectiveOverlayScheme(out var EffectiveOverlayGuid) != 0)
			{
				return null;
			}
			return GetActiveScheme() == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e") && EffectiveOverlayGuid == guid;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Power overlay read failed: " + ex.Message);
			return null;
		}
	}

	internal static PowerOverlayResult SetOverlayConfirmed(int overlayIndex)
	{
		try
		{
			if ((overlayIndex < 0 || overlayIndex > 2) ? true : false)
			{
				return PowerOverlayResult.Failed;
			}
			if (GetBatterySaverStatus())
			{
				return PowerOverlayResult.BatterySaver;
			}
			Guid guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
			Guid guid2 = new Guid(overlayIndex switch
			{
				0 => "961cc777-2547-4f9d-8174-7d86181b8a7a", 
				2 => "ded574b5-45a0-4f42-8737-46345c09c238", 
				_ => "00000000-0000-0000-0000-000000000000", 
			});
			for (int i = 0; i < 3; i++)
			{
				Guid activeScheme = GetActiveScheme();
				if (WouldSilentlyUndo(activeScheme, guid))
				{
					return PowerOverlayResult.UltimatePlanKept;
				}
				uint num = ((!(activeScheme == guid)) ? PowerSetActiveScheme(IntPtr.Zero, guid) : 0u);
				uint num2 = PowerSetActiveOverlayScheme(guid2);
				uint num3 = PowerGetEffectiveOverlayScheme(out var EffectiveOverlayGuid);
				bool flag = num == 0 && num2 == 0 && num3 == 0 && GetActiveScheme() == guid && EffectiveOverlayGuid == guid2;
				Logger.WriteLine($"Power overlay -> {guid2}: plan={num} overlay={num2} read={num3}/{EffectiveOverlayGuid} confirmed={flag}");
				if (flag)
				{
					return PowerOverlayResult.Confirmed;
				}
				Thread.Sleep(100);
			}
			return PowerOverlayResult.Failed;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Power overlay apply failed: " + ex.Message);
			return PowerOverlayResult.Failed;
		}
	}
}
