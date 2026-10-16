using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;
using Microsoft.Win32;

namespace MechrevoLite.Display;

public static class ScreenControl
{
	public const int MAX_REFRESH = 1000;

	/// <summary>⭐ 2026-10-15：**我们最近一次真正下发刷新率**的时间戳（<see cref="Environment.TickCount64"/>）。
	/// 用途：区分"档位变化是我们做的"还是"外部改的"。实证场景 —— 手选 100Hz 后 `System Suspend → Resume`，
	/// 我们的门控关着（日志 `AutoSetting … changed=False`），但档位被 GCU 固件的 `GPU_DC_HZ` 顶回 180Hz；
	/// <see cref="MechrevoLite.SettingsForm"/> 的周期回读据此判断"外部改动"并弹提示（用户要求「加个提示」）。
	/// 只有**真的下发**（<c>SetRefreshRate</c> 被调用）才写，读/未变更不写。</summary>
	public static long LastApplyTick;

	public static int MIN_RATE = AppConfig.Get("min_rate", 60);

	public static int MAX_RATE = AppConfig.Get("max_rate");

	public static int GetMaxRate(string? laptopScreen)
	{
		if (MAX_RATE > 0)
		{
			return MAX_RATE;
		}
		return ScreenNative.GetMaxRefreshRate(laptopScreen);
	}

	/// <summary>⭐ 2026-10-15（用户口径修正）：「**离电都是 60**，最大帧率可能不一样」——
	/// 所以离电目标**恒为 <see cref="MIN_RATE"/>（默认 60，可由 config 的 <c>min_rate</c> 覆盖）**，
	/// 会因屏幕不同而变化的是**最高档**（那是 <see cref="GetMaxRate"/>，取自 <c>max_rate</c> 配置或
	/// <see cref="ScreenNative.GetMaxRefreshRate"/> 的**面板实测值**，本来就没有写死）。
	/// <para>⚠️ 曾短暂改成"按面板可用档推导离电档"，已按用户口径回退 —— 不要再改回去。</para></summary>
	public static void AutoScreen(bool force = false)
	{
		if (force || AppConfig.Is("screen_auto"))
		{
			if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
			{
				SetScreen(1000);
			}
			else
			{
				SetScreen(MIN_RATE);
			}
		}
		else
		{
			SetScreen();
		}
	}

	public static void SetAutoRefresh(int auto)
	{
		AppConfig.Set("screen_auto", auto);
		if (auto == 0)
		{
			SetAsusRefreshFlag(0);
		}
	}

	public static void SetAsusRefreshFlag(int value)
	{
		if (!ProcessHelper.IsUserAdministrator())
		{
			return;
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{8714A8D1-0F08-4681-9DF6-A8C4607A58B4}", writable: true);
			registryKey.SetValue("RefreshFlag", value, RegistryValueKind.DWord);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failed to set RefreshFlag: " + ex.Message);
		}
	}

	public static void SetScreen(int frequency = -1)
	{
		string laptopScreen = ScreenNative.FindLaptopScreen(log: true);
		int refreshRate = ScreenNative.GetRefreshRate(laptopScreen);
		if (refreshRate >= 0)
		{
			if (frequency >= 1000)
			{
				frequency = GetMaxRate(laptopScreen);
			}
			if (frequency > 0 && frequency != refreshRate)
			{
				ScreenNative.SetRefreshRate(laptopScreen, frequency);
				// ⭐ 2026-10-15：记下"我们下发的时刻" —— SettingsForm 的周期回读用它区分"外部改档"（见字段注释）。
				LastApplyTick = Environment.TickCount64;
			}
			InitScreen();
		}
	}

	public static void InitOptimalBrightness()
	{
		int num = AppConfig.Get("optimal_brightness");
		if (num >= 0)
		{
			SetOptimalBrightness(num);
		}
	}

	public static void SetOptimalBrightness(int status)
	{
		AppConfig.Set("optimal_brightness", status);
	}

	public static void ToogleFHD()
	{
		int value = Program.acpi.DeviceGet(327708u);
		Logger.WriteLine($"FHD Toggle: {value}");
		if (MessageBox.Show("Changing display mode requires reboot", Strings.AlertUltimateTitle, MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			SystemRestart.CaptureUserConfirmation();
			SystemRestart.RequestRestart("FHD toggle", "/r /t 1");
		}
	}

	public static void SetHDRControl(int status = -1)
	{
		if (status >= 0)
		{
			AppConfig.Set("hdr_control", status);
		}
	}

	public static void ToogleHDRControl()
	{
		int num = Program.acpi.DeviceGet(327793u);
		Logger.WriteLine($"HDR Control Toggle: {num}");
		SetHDRControl((num == 1) ? 1 : 0);
		InitScreen();
	}

	public static string ToogleMiniled()
	{
		int num = Program.acpi.DeviceGet(327710u);
		int num2 = Program.acpi.DeviceGet(327726u);
		Logger.WriteLine($"MiniledToggle: {num} {num2}");
		int value;
		string result;
		if (num >= 0)
		{
			if (num == 1)
			{
				value = 0;
				result = Strings.OneZone;
			}
			else
			{
				value = 1;
				result = Strings.Multizone;
			}
		}
		else
		{
			switch (num2)
			{
			case 1:
				value = 2;
				result = Strings.OneZone;
				break;
			case 2:
				value = 0;
				result = Strings.Multizone;
				break;
			default:
				value = 1;
				result = Strings.MultizoneStrong;
				break;
			}
		}
		AppConfig.Set("miniled", value);
		SetScreen();
		return result;
	}

	/// <summary>
	/// 2026-10-09（"双屏同开一次后刷新率永久不正确"根因修复 · 纠正侧）。
	/// 机主原话：「好像是只要两个屏幕同时开过一次，后面刷新率就识别不到正确的了」
	/// ⇒ 脏值一旦落盘就被**永久沿用**（单屏 ↔ 单屏切换不会再触发写，所以不会自愈）。
	/// 本方法在**启动**与**显示配置变化**两个时机主动核对 config 里已落盘的三个值：
	///   · `screen_max` / `max_frequency` 比"当前 eDP 内屏实测 max"还大 ⇒ 物理不可能，纠正回实测值；
	///   · `frequency` 比实测 max 还大 ⇒ 来自别的屏，纠正回当前内屏的真实刷新率。
	/// ⚠️ **只有在能确定 eDP 内屏**（<see cref="F:ScreenNative.InternalScreenConfidence.Embedded" />）时才纠正；
	/// 不确定则**一动不动**，只写一条可诊断日志（不写空、不写 0、不放宽任何策略）。
	/// </summary>
	internal static bool ReconcileScreenMetrics(string reason)
	{
		try
		{
			string? laptopScreen = ScreenNative.FindLaptopScreen(out ScreenNative.InternalScreenConfidence confidence, out string technology, log: false);
			int num = AppConfig.Get("screen_max");
			int num2 = AppConfig.Get("max_frequency");
			int num3 = AppConfig.Get("frequency");
			if (laptopScreen == null || confidence != ScreenNative.InternalScreenConfidence.Embedded)
			{
				Logger.WriteLineIfChanged("screen-metrics-unconfirmed:" + reason, $"screen metrics: source={confidence} tech={technology} 无法确认 eDP 内屏 -> 保持 config 原值不纠正（screen_max={num} max_frequency={num2} frequency={num3}）");
				return false;
			}
			int num4 = ScreenNative.MeasureMaxRefreshRate(laptopScreen);
			int refreshRate = ScreenNative.GetRefreshRate(laptopScreen);
			if (num4 <= 0)
			{
				Logger.WriteLineIfChanged("screen-metrics-nomeasure:" + reason, $"screen metrics: source=eDP 实测档位为空 -> 保持 config 原值不纠正（screen_max={num} max_frequency={num2} frequency={num3}）");
				return false;
			}
			List<string> list = new List<string>();
			if (num > num4)
			{
				AppConfig.Set("screen_max", num4);
				list.Add($"screen_max={num}->{num4}");
			}
			if (num2 > num4)
			{
				AppConfig.Set("max_frequency", num4);
				list.Add($"max_frequency={num2}->{num4}");
			}
			if (num3 > num4 && refreshRate > 0)
			{
				AppConfig.Set("frequency", refreshRate);
				list.Add($"frequency={num3}->{refreshRate}");
			}
			if (list.Count > 0)
			{
				Logger.WriteLine($"screen metrics: source=eDP measured_max={num4} real={refreshRate} (旧值 {string.Join(", ", list)} 已纠正, 触发={reason})");
				return true;
			}
			Logger.WriteLineIfChanged("screen-metrics-ok:" + reason, $"screen metrics: source=eDP measured_max={num4} real={refreshRate} 无脏值（screen_max={num} max_frequency={num2} frequency={num3}）");
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("screen metrics: 核对失败 " + ex.Message);
			return false;
		}
	}

	public static void InitScreen()
	{
		// 2026-10-09（写入侧硬口径）：取屏结果同时带回可信度 —— 只有 eDP 内屏（Embedded）才允许把实测值落盘。
		string laptopScreen = ScreenNative.FindLaptopScreen(out ScreenNative.InternalScreenConfidence screenConfidence, out _, log: false);
		bool flag = screenConfidence == ScreenNative.InternalScreenConfidence.Embedded;
		// 先核对已落盘的旧值（纠正"双屏同开一次"留下的脏值），再做下面的写入。
		ReconcileScreenMetrics("InitScreen");
		int frequency = ScreenNative.GetRefreshRate(laptopScreen);
		int maxFrequency = GetMaxRate(laptopScreen);
		if (flag && maxFrequency > 0)
		{
			AppConfig.Set("max_frequency", maxFrequency);
		}
		else
		{
			if (maxFrequency > 0)
			{
				Logger.WriteLineIfChanged("screen-metrics-skip", $"screen metrics: source={screenConfidence} measured={maxFrequency} 未落盘（非 eDP，max_frequency 保持 {AppConfig.Get("max_frequency")}）");
			}
			else
			{
				maxFrequency = AppConfig.Get("max_frequency");
			}
		}
		bool screenAuto = AppConfig.Is("screen_auto");
		bool overdriveSetting = Program.acpi.IsOverdriveSupported() && !AppConfig.IsNoOverdrive();
		int overdrive = ((!AppConfig.IsNoOverdrive()) ? Program.acpi.DeviceGet(327705u) : 0);
		int miniled1 = Program.acpi.DeviceGet(327710u);
		int miniled2 = Program.acpi.DeviceGet(327726u);
		int num = ((miniled1 >= 0) ? miniled1 : miniled2);
		bool hdr = false;
		bool acm = false;
		if (num >= 0)
		{
			Logger.WriteLine($"Miniled: {miniled1} {miniled2}");
			AppConfig.Set("miniled", num);
		}
		try
		{
			hdr = ScreenCCD.GetHDRStatus(out acm);
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
		}
		bool screenEnabled = frequency >= 0;
		int fhd = -1;
		int hdrControl = Program.acpi.DeviceGet(327793u);
		if (hdrControl >= 0)
		{
			Logger.WriteLine($"HDR Control Status: {hdrControl}");
		}
		// 2026-10-09（写入侧硬口径）：frequency 同样只在屏幕确认为 eDP 内屏时落盘；否则保持原值不写。
		if (flag && frequency >= 0)
		{
			AppConfig.Set("frequency", frequency);
		}
		else if (frequency >= 0)
		{
			Logger.WriteLineIfChanged("screen-metrics-skip-frequency", $"screen metrics: source={screenConfidence} measured={frequency} 未落盘（非 eDP，frequency 保持 {AppConfig.Get("frequency")}）");
		}
		AppConfig.Set("overdrive", overdrive);
		// 2026-10-12 screen-diag: 与 SettingsForm.VisualiseScreen 入口那行**配对**，用于对照两端，
		// 看清 maxFrequency / screenEnabled 传到 UI 时到底是几（无 GCU 时刷新率整块消失的定位用）。
		Logger.WriteLine($"InitScreen -> screen={laptopScreen ?? "(null)"} confidence={screenConfidence} freq={frequency} maxFreq={maxFrequency} screenEnabled={screenEnabled} MIN_RATE={MIN_RATE} MAX_RATE={MAX_RATE} cfgMaxFreq={AppConfig.Get("max_frequency")}");
		Program.settingsForm.BeginInvoke(() =>
		{
			Program.settingsForm.VisualiseScreen(screenEnabled, screenAuto, frequency, maxFrequency, overdrive, overdriveSetting, miniled1, miniled2, hdr, acm, fhd, hdrControl);
		});
	}
}
