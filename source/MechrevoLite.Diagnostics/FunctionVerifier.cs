using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using Newtonsoft.Json;

namespace MechrevoLite.Diagnostics;

internal static class FunctionVerifier
{
	private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1600L);

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：强冷同类缺口统一修 · 防回归自检）：
	/// 遍历所有快捷开关键，检查「可见 ⇔ 有处理分支」，不一致 ⇒ 记 issue 并打印清单 ✅
	/// </summary>
	private static void LogQuickSwitchCoverage(MechrevoHw hw)
	{
		List<string> supported = new List<string>();
		List<string> handled = new List<string>();
		List<string> gaps = new List<string>();
		foreach (string key in MechrevoHw.QuickSwitchKeys)
		{
			bool visible;
			try
			{
				visible = hw.SupportsQuickSwitch(key);
			}
			catch
			{
				visible = false;
			}
			if (!visible)
			{
				continue;
			}
			supported.Add(key);
			if (MechrevoHw.HasQuickSwitchDispatch(key))
			{
				handled.Add(key);
			}
			else
			{
				gaps.Add(key);
			}
		}
		Logger.WriteError("[selfcheck] 快捷开关覆盖：支持=" + supported.Count + " · 有处理=" + handled.Count
			+ " · 缺口=[" + string.Join(",", gaps) + "]");
		if (gaps.Count > 0)
		{
			Logger.WriteError("[selfcheck] ⛔ 快捷开关缺口（可见但无下发处理）：" + string.Join(",", gaps));
		}
	}
	public static async Task<int> RunAsync(string outputPath)
	{
		List<VerifyResult> results = new List<VerifyResult>();
		using MechrevoHw hw = new MechrevoHw();
		Logger.WriteLine("VERIFY 开始，连接 GCU…");		LogQuickSwitchCoverage(hw);
		if (!(await hw.ConnectAsync()))
		{
			Logger.WriteLine("VERIFY 连接失败，无法验证。");
			Console.WriteLine("连接 GCU 失败：厂商服务未运行？");
			return 1;
		}
		Dictionary<string, string> rawPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		hw.RawMessageObserved += (string topic, string payload) =>
		{
			bool flag;
			switch (topic)
			{
			case "Setting/Status":
			case "Fan/Status":
			case "Keyboard/Status":
			case "Settings/DeviceSwitchItemStatus":
			case "WhisperMode/Status":
			case "GPUDevice/Status":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				rawPayloads[topic] = payload;
			}
		};
		MechrevoService service = new MechrevoService(hw);
		await service.RefreshAll();
		await Task.Delay(3500);
		Logger.WriteLine("VERIFY 基线能力: " + hw.DescribeResolvedCapabilities());
		await VerifyQuickSwitches(hw, service, results);
		VerifyWindowsPersonalization(results);
		int modeBeforeNumeric = service.CurrentMode;
		bool switchedToCustom = await service.SwitchMode(3) == MechrevoService.ModeSwitchResult.Ok;
		await Task.Delay(Settle);
		results.Add(switchedToCustom ? new VerifyResult("数值设置", "切到自定义模式", VerifyOutcome.Pass, "原模式=" + ModeName(modeBeforeNumeric) + "，已切到自定义以便验证数值项") : new VerifyResult("数值设置", "切到自定义模式", VerifyOutcome.Fail, $"切不到自定义模式（当前={service.CurrentMode}），下面的数值项结果不可信"));
		await VerifyNumericSettings(hw, service, results);
		await service.SwitchMode(modeBeforeNumeric);
		await Task.Delay(Settle);
		await VerifyModes(hw, service, results);
		await VerifyBattery(hw, results);
		await VerifyLighting(hw, service, results);
		await VerifyDisplay(hw, service, results);
		await VerifyLiquidCooling(hw, service, results);
		VerifyGpuModeByDesign(hw, results);
		Report(results, outputPath, rawPayloads);
		return results.Any((VerifyResult r) => r.Outcome == VerifyOutcome.Fail) ? 3 : 0;
	}

	private static async Task VerifyQuickSwitches(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		string[] array = new string[24]
		{
			"touchpad", "wifi", "bt", "webcam", "winkey", "fnkey", "numpad", "osd", "usb", "deepsleep",
			"copilot", "acrecovery", "highperf", "fanboost", "lightbar", "logolight", "touchpadtoggle", "singlecolorkb", "uni", "omni",
			"powerlight", "batterylogo", "gamewhitelist", "cpuadvperf"
		};
		string[] array2 = array;
		foreach (string key in array2)
		{
			if (!SupportsKey(hw, key))
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.Unsupported, "能力判定为不支持，界面上不会出现这一项"));
				continue;
			}
			bool? baseline = Read(hw, key);
			if (!baseline.HasValue)
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.Fail, "声称支持却读不到状态：QuickSwitches 字典里没有这个键"));
				continue;
			}
			bool target = !baseline.Value;
			bool sent = await Apply(service, key, target);
			await Task.Delay(Settle);
			bool? after = Read(hw, key);
			await Apply(service, key, baseline.Value);
			await Task.Delay(Settle);
			bool? flag = Read(hw, key);
			if (flag != baseline)
			{
				await Apply(service, key, baseline.Value);
				await Task.Delay(Settle);
				flag = Read(hw, key);
			}
			string text = $"基线={baseline} 发{target}后回读={Describe(after)} 恢复后={Describe(flag)} 命令确认={sent}";
			if (after == target && flag == baseline)
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.Pass, text));
			}
			else if (key == "deepsleep")
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.RestartRequired, "命令已下发，但这一项要重启才生效，当场回读不会变。" + text));
			}
			else if (after != target)
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.Fail, "下发后回读没有变化，点了等于没反应。" + text));
			}
			else
			{
				results.Add(new VerifyResult("快捷开关", key, VerifyOutcome.Fail, "能改但恢复不回原状，会给用户留下改动。" + text));
			}
		}
	}

	private static bool SupportsKey(MechrevoHw hw, string key)
	{
		return hw.SupportsQuickSwitch(key);
	}

	private static bool? Read(MechrevoHw hw, string key)
	{
		if (!(key == "usb"))
		{
			if (key == "fanboost")
			{
				return hw.SupportsFanBoost ? new bool?(hw.FanBoost) : ((bool?)null);
			}
			bool value;
			return hw.QuickSwitches.TryGetValue(key, out value) ? new bool?(value) : ((bool?)null);
		}
		return hw.UsbChargerSeen ? new bool?(hw.UsbCharger) : ((bool?)null);
	}

	private static string Describe(bool? value)
	{
		return value?.ToString() ?? "无回读";
	}

	private static Task<bool> Apply(MechrevoService service, string key, bool on)
	{
		switch (key)
		{
		case "usb":
			return service.SwitchUsbCharger(on);
		case "fanboost":
			return service.SwitchFanBoost(on);
		case "deepsleep":
			return service.SwitchDeepSleep(on);
		case "gamewhitelist":
			return service.SwitchGameWhitelist(on);
		case "cpuadvperf":
			return service.SwitchCpuAdvancedPerformance(on);
		case "uni":
		case "omni":
			return service.SwitchUniOmni(key, on);
		case "lightbar":
			return service.SetLightPower("HidLightbar/Ctrl", on);
		case "logolight":
			return service.SetLightPower("HidLightbar_Logo/Ctrl", on);
		default:
			return service.SwitchQuick(key, on);
		}
	}

	private static void VerifyWindowsPersonalization(List<VerifyResult> results)
	{
		(string, Func<bool?>, Func<bool, bool>)[] array = new (string, Func<bool?>, Func<bool, bool>)[3]
		{
			("任务栏自动隐藏", ShellPersonalization.IsTaskbarAutoHide, ShellPersonalization.SetTaskbarAutoHide),
			("透明效果", ShellPersonalization.IsTransparencyEnabled, ShellPersonalization.SetTransparencyEnabled),
			("深色主题", ShellPersonalization.IsDarkTheme, ShellPersonalization.SetDarkTheme)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, Func<bool?>, Func<bool, bool>) tuple = array[i];
			string item = tuple.Item1;
			Func<bool?> item2 = tuple.Item2;
			Func<bool, bool> item3 = tuple.Item3;
			bool? flag = item2();
			if (!flag.HasValue)
			{
				results.Add(new VerifyResult("Windows 设置", item, VerifyOutcome.Fail, "读不到当前值（注册表值缺失或互操作签名错误）"));
				continue;
			}
			bool flag2 = !flag.Value;
			bool value = item3(flag2);
			bool? flag3 = item2();
			bool value2 = item3(flag.Value);
			bool? flag4 = item2();
			string text = $"基线={flag} 发{flag2}后回读={Describe(flag3)} 恢复后={Describe(flag4)}";
			results.Add((flag3 == flag2 && flag4 == flag) ? new VerifyResult("Windows 设置", item, VerifyOutcome.Pass, text) : new VerifyResult("Windows 设置", item, VerifyOutcome.Fail, $"写入未生效或恢复失败（write={value}/{value2}）。" + text));
		}
	}

	private static async Task VerifyNumericSettings(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		(string, bool, int, int, int, string)[] array = new (string, bool, int, int, int, string)[6]
		{
			("PL1 功耗墙", hw.Pl1Adjustable, hw.Pl1, hw.Pl1Minimum, hw.Pl1Maximum, "PL1"),
			("PL2 功耗墙", hw.Pl2Adjustable, hw.Pl2, hw.Pl2Minimum, hw.Pl2Maximum, "PL2"),
			("PL4 瞬时功耗墙", hw.Pl4Adjustable, hw.Pl4, hw.Pl4Minimum, hw.Pl4Maximum, "PL4"),
			("CPU 温度墙", hw.TccAdjustable, hw.TccTarget, hw.TccMinimum, hw.TccMaximum, "CpuTccOffset"),
			("GPU TGP", hw.GpuTgpAdjustable, hw.GpuTgp, hw.GpuTgpMinimum, hw.GpuTgpMaximum, "GpuConfigurableTGPTarget"),
			("GPU 动态加速", hw.GpuDynamicBoostAdjustable, hw.GpuDb, hw.GpuDbMinimum, hw.GpuDbMaximum, "GpuDynamicBoost")
		};
		(string Name, bool Adjustable, int Current, int Min, int Max, string Field)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			var (name, flag, current, min, max, field) = array2[i];
			if (!flag)
			{
				results.Add(new VerifyResult("数值设置", name, VerifyOutcome.Unsupported, $"运行时未上报可用范围（current={current} min={min} max={max}）"));
				continue;
			}
			int target = ((current - 5 >= min) ? (current - 5) : Math.Min(max, current + 5));
			if (target == current)
			{
				results.Add(new VerifyResult("数值设置", name, VerifyOutcome.Unsupported, $"范围太窄，挑不出与当前值不同的目标（current={current} [{min},{max}]）"));
				continue;
			}
			int expected = ((field == "PL4") ? hw.Pl4Effective(target) : target);
			bool sent = await service.SetCustomDetail(new Dictionary<string, string> { [field] = target.ToString() });
			await Task.Delay(Settle);
			int after = ReadNumeric(hw, field);
			await service.SetCustomDetail(new Dictionary<string, string> { [field] = current.ToString() });
			await Task.Delay(Settle);
			int num = ReadNumeric(hw, field);
			string value = ((expected == target) ? "" : $"（半瓦机型量化到 {expected}）");
			string text = $"基线={current} 范围[{min},{max}] 发{target}{value}后回读={after} 恢复后={num} 命令确认={sent}";
			results.Add((after == expected && num == current) ? new VerifyResult("数值设置", name, VerifyOutcome.Pass, text) : new VerifyResult("数值设置", name, VerifyOutcome.Fail, ((after != expected) ? "写入后回读没变。" : "恢复不回原值。") + text));
		}
		await VerifyFanSwitchSpeed(hw, service, results);
	}

	private static int ReadNumeric(MechrevoHw hw, string field)
	{
		return field switch
		{
			"PL1" => hw.Pl1, 
			"PL2" => hw.Pl2, 
			"PL4" => hw.Pl4, 
			"CpuTccOffset" => hw.TccTarget, 
			"GpuConfigurableTGPTarget" => hw.GpuTgp, 
			"GpuDynamicBoost" => hw.GpuDb, 
			_ => int.MinValue, 
		};
	}

	private static async Task VerifyFanSwitchSpeed(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		if (!hw.SupportsFanSwitchSpeed)
		{
			results.Add(new VerifyResult("数值设置", "风扇转换灵敏度", VerifyOutcome.Unsupported, "服务端未上报 FAN_FanSwitchSpeed"));
			return;
		}
		int baseline = hw.FanSwitchSpeed;
		bool enabledBaseline = hw.FanSwitchSpeedEnabled;
		int target = ((baseline + 100 <= hw.FanSwitchSpeedMaximum) ? (baseline + 100) : (baseline - 100));
		bool sent = await service.SetCustomDetail(new Dictionary<string, string>
		{
			["FanSwitchSpeedEnabled"] = "1",
			["FanSwitchSpeed"] = target.ToString()
		});
		await Task.Delay(Settle);
		int after = hw.FanSwitchSpeed;
		await service.SetCustomDetail(new Dictionary<string, string>
		{
			["FanSwitchSpeedEnabled"] = (enabledBaseline ? "1" : "0"),
			["FanSwitchSpeed"] = baseline.ToString()
		});
		await Task.Delay(Settle);
		int fanSwitchSpeed = hw.FanSwitchSpeed;
		string text = $"基线={baseline}ms 启用={enabledBaseline} 发{target}后回读={after} 恢复后={fanSwitchSpeed} 命令确认={sent}";
		results.Add((after == target && fanSwitchSpeed == baseline) ? new VerifyResult("数值设置", "风扇转换灵敏度", VerifyOutcome.Pass, text) : new VerifyResult("数值设置", "风扇转换灵敏度", VerifyOutcome.Fail, ((after != target) ? "写入后回读没变。" : "恢复不回原值。") + text));
	}

	private static async Task VerifyModes(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		int baseline = service.CurrentMode;
		int[] array = new int[3] { 2, 0, 1 };
		foreach (int mode in array)
		{
			bool ok = await service.SwitchMode(mode) == MechrevoService.ModeSwitchResult.Ok;
			await Task.Delay(Settle);
			int currentMode = service.CurrentMode;
			results.Add((currentMode == mode) ? new VerifyResult("性能模式", ModeName(mode), VerifyOutcome.Pass, $"切换后回读={currentMode} 命令确认={ok}") : new VerifyResult("性能模式", ModeName(mode), VerifyOutcome.Fail, $"切换后回读={currentMode}，期望 {mode}，命令确认={ok}"));
		}
		await service.SwitchMode(baseline);
		await Task.Delay(Settle);
		bool flag = service.CurrentMode == baseline;
		if (!flag)
		{
			await service.SwitchMode(baseline);
			await Task.Delay(Settle);
			flag = service.CurrentMode == baseline;
		}
		results.Add(flag ? new VerifyResult("性能模式", "恢复原模式", VerifyOutcome.Pass, "已回到 " + ModeName(baseline)) : new VerifyResult("性能模式", "恢复原模式", VerifyOutcome.Fail, $"两次都未回到 {ModeName(baseline)}，当前={service.CurrentMode}"));
		int profileBaseline = hw.CustomProfileIndex;
		if (profileBaseline < 0)
		{
			results.Add(new VerifyResult("性能模式", "自定义档切换", VerifyOutcome.Unsupported, "未上报 CustomProfileIndex"));
			return;
		}
		int profileTarget = ((profileBaseline == 0) ? 1 : 0);
		bool profileOk = await service.SwitchCustomProfile(profileTarget);
		await Task.Delay(Settle);
		int profileAfter = hw.CustomProfileIndex;
		await service.SwitchCustomProfile(profileBaseline);
		await Task.Delay(Settle);
		results.Add((profileAfter == profileTarget && hw.CustomProfileIndex == profileBaseline) ? new VerifyResult("性能模式", "自定义档切换", VerifyOutcome.Pass, $"基线={profileBaseline} 切到{profileTarget}后回读={profileAfter} 已恢复") : new VerifyResult("性能模式", "自定义档切换", VerifyOutcome.Fail, $"基线={profileBaseline} 切到{profileTarget}后回读={profileAfter} 恢复后={hw.CustomProfileIndex} 命令确认={profileOk}"));
	}

	private static string ModeName(int mode)
	{
		return mode switch
		{
			0 => "均衡/游戏", 
			1 => "增强/Turbo", 
			2 => "静音/办公", 
			3 => "自定义", 
			_ => "模式" + mode, 
		};
	}

	private static async Task VerifyBattery(MechrevoHw hw, List<VerifyResult> results)
	{
		// 2026-10-10（EC 传输期间不写 GCU 档位 · 用户原话「另能不能想办法修了」）：本项会依次把 GCU 官方档
		// 0/1/2 写下去（BatteryProtection/Control ⇒ EC 0x07A6）再恢复 baseline —— 在 EC 传输生效时两边
		// 会互相覆盖：GCU 每写一次 0x07A6 都会上报 System/BatteryProtection，而上报又会触发我们的限充
		// 重确立（BatteryControl.ReplayChargeLimitAfterExternalWriter ⇒ 再重写 0x7B9）。
		// 判据复用既有单一真源 BatteryControl.ReestablishArmed()（= charge_limit_lock==1 ||
		// charge_limit_slider==1；两种开关都会让 GCU 侧被要求让位 BatteryProtection=0，见
		// SettingsForm.EnsureGcuStandsDownAsync 的两个调用点）⇒ ⛔ 不新造第三套判据。
		// ⇒ 处理方式选**跳过**（比「校验后恢复」稳）：一个字节都不写 GCU ⇒ 不存在「恢复失败 / 恢复期间
		// GCU 限电档与 EC 限充同时生效」的窗口；baseline 从未被改，也无需恢复。
		if (MechrevoLite.Battery.BatteryControl.ReestablishArmed())
		{
			Logger.WriteLine("VERIFY 电池保护三档：EC 传输生效（charge_limit_lock=1 或 charge_limit_slider=1）⇒ 跳过 GCU 档位校验（⛔ 不写 BatteryProtection/0x07A6，避免与 EC 直写互相覆盖）");
			results.Add(new VerifyResult("电池", "电池保护三档", VerifyOutcome.Unsupported, "EC 传输生效（EC 限充或无极限制充电滑块开着）⇒ 已跳过 GCU 档位校验，不改任何 GCU 档位，避免与 EC 直写互相覆盖"));
			return;
		}
		int baseline = hw.BatteryProtection;
		if (baseline < 0)
		{
			results.Add(new VerifyResult("电池", "电池保护三档", VerifyOutcome.Fail, "读不到 HealthProtectionStatus"));
			return;
		}
		int[] array = new int[3] { 0, 1, 2 };
		for (int i = 0; i < array.Length; i++)
		{
			int mode = array[i];
			bool ok = await hw.SetBatteryProtection(mode);
			await Task.Delay(Settle);
			results.Add((hw.BatteryProtection == mode) ? new VerifyResult("电池", "保护档 " + mode, VerifyOutcome.Pass, $"回读={hw.BatteryProtection} 确认={ok}") : new VerifyResult("电池", "保护档 " + mode, VerifyOutcome.Fail, $"回读={hw.BatteryProtection}，期望 {mode}，确认={ok}"));
		}
		await hw.SetBatteryProtection(baseline);
		await Task.Delay(Settle);
		results.Add((hw.BatteryProtection == baseline) ? new VerifyResult("电池", "恢复原档", VerifyOutcome.Pass, "已回到 " + baseline) : new VerifyResult("电池", "恢复原档", VerifyOutcome.Fail, $"未回到 {baseline}，当前={hw.BatteryProtection}"));
	}

	private static async Task VerifyLighting(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		bool kbBaseline = hw.KeyboardPower;
		bool kbSent = await service.SetKeyboardPower(!kbBaseline);
		await Task.Delay(Settle);
		bool kbAfter = hw.KeyboardPower;
		await service.SetKeyboardPower(kbBaseline);
		await Task.Delay(Settle);
		results.Add((kbAfter != kbBaseline && hw.KeyboardPower == kbBaseline) ? new VerifyResult("灯效", "键盘背光电源", VerifyOutcome.Pass, $"基线={kbBaseline} 发{!kbBaseline}后回读={kbAfter} 已恢复") : new VerifyResult("灯效", "键盘背光电源", VerifyOutcome.Fail, $"基线={kbBaseline} 发{!kbBaseline}后回读={kbAfter} 恢复后={hw.KeyboardPower} 确认={kbSent}"));
		if (!hw.SupportsPowerLightBrightness)
		{
			results.Add(new VerifyResult("灯效", "电源灯亮度", VerifyOutcome.Unsupported, "未上报 PowerLightBrightness"));
			return;
		}
		int baseline = hw.PowerLightBrightness;
		int target = ((baseline >= 40) ? (baseline - 20) : (baseline + 20));
		bool sent = await service.SetPowerLightBrightness(target);
		await Task.Delay(Settle);
		int after = hw.PowerLightBrightness;
		await service.SetPowerLightBrightness(baseline);
		await Task.Delay(Settle);
		results.Add((after == target && hw.PowerLightBrightness == baseline) ? new VerifyResult("灯效", "电源灯亮度", VerifyOutcome.Pass, $"基线={baseline} 发{target}后回读={after} 已恢复") : new VerifyResult("灯效", "电源灯亮度", VerifyOutcome.Fail, $"基线={baseline} 发{target}后回读={after} 恢复后={hw.PowerLightBrightness} 确认={sent}"));
	}

	private static async Task VerifyDisplay(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		// ⭐⭐ 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**）：自检也统一到 Win32 ——
		// 原来读 GCU 的 `HzList`/`CurrentHz` 并用 `service.SwitchRefreshRate`（GCU 的 GPU_HZSETTING）下发，
		// 非 GCU 机型上这段永远判 Unsupported/Fail。现在档位与回读都用 EnumDisplaySettingsEx，
		// 下发用 ScreenControl.SetScreen ⇒ ChangeDisplaySettingsEx（与主界面/性能档完全同一条路）。
		string screenName = MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false);
		int baseline = MechrevoLite.Display.ScreenNative.GetRefreshRate(screenName);
		IReadOnlyList<int> hzRates = MechrevoLite.Display.ScreenNative.GetAvailableRefreshRates(screenName).Distinct().ToArray();
		if (baseline <= 0 || hzRates.Count < 2)
		{
			results.Add(new VerifyResult("显示", "刷新率切换", VerifyOutcome.Unsupported, $"Win32 侧档位不足（当前={baseline}Hz，枚举=[{string.Join(",", hzRates)}]）"));
		}
		else
		{
			int target = hzRates.First((int hz) => Math.Abs(hz - baseline) > 2);
			MechrevoLite.Display.ScreenControl.SetScreen(target);
			await Task.Delay(Settle);
			int after = MechrevoLite.Display.ScreenNative.GetRefreshRate(screenName);
			MechrevoLite.Display.ScreenControl.SetScreen(baseline);
			await Task.Delay(Settle);
			int restored = MechrevoLite.Display.ScreenNative.GetRefreshRate(screenName);
			results.Add((Math.Abs(after - target) <= 2 && Math.Abs(restored - baseline) <= 2) ? new VerifyResult("显示", "刷新率切换", VerifyOutcome.Pass, $"基线={baseline}Hz 切{target}Hz 后回读={after}Hz 已恢复={restored}Hz") : new VerifyResult("显示", "刷新率切换", VerifyOutcome.Fail, $"基线={baseline}Hz 切{target}Hz 后回读={after}Hz 恢复后={restored}Hz"));
		}
		if (!hw.SupportsColorCalibration)
		{
			results.Add(new VerifyResult("显示", "屏幕校色", VerifyOutcome.Unsupported, "未上报校色支持"));
			return;
		}
		int calibBaseline = MechrevoService.GetColorCalibrationMode();
		bool calibOnBaseline = MechrevoService.IsColorCalibrationOn();
		int calibTarget = ((calibBaseline != 1) ? 1 : 2);
		bool calibSent = await service.SetColorCalibration(calibTarget);
		await Task.Delay(TimeSpan.FromSeconds(4L));
		int calibAfter = MechrevoService.GetColorCalibrationMode();
		await service.SetColorCalibration(calibOnBaseline ? Math.Max(1, calibBaseline) : 0);
		await Task.Delay(TimeSpan.FromSeconds(4L));
		results.Add((calibAfter == calibTarget) ? new VerifyResult("显示", "屏幕校色", VerifyOutcome.Pass, $"基线=档{calibBaseline} 开={calibOnBaseline} 发档{calibTarget}后回读=档{calibAfter} 已恢复") : new VerifyResult("显示", "屏幕校色", VerifyOutcome.Fail, $"基线=档{calibBaseline} 发档{calibTarget}后回读=档{calibAfter} 确认={calibSent}"));
	}

	private static async Task VerifyLiquidCooling(MechrevoHw hw, MechrevoService service, List<VerifyResult> results)
	{
		if (!hw.SupportsLiquidCooling || !hw.LcGcuControllable)
		{
			results.Add(new VerifyResult("液冷", "水泵档位", VerifyOutcome.Unsupported, $"未连接或不可控（reported={hw.SupportsLiquidCooling} controllable={hw.LcGcuControllable}）"));
			return;
		}
		int baseline = hw.LcPumpDuty;
		int target = ((baseline >= 60) ? 40 : 60);
		bool sent = await service.SwitchLcPump(target);
		await Task.Delay(TimeSpan.FromSeconds(3L));
		int after = hw.LcPumpDuty;
		await service.SwitchLcPump(baseline);
		await Task.Delay(TimeSpan.FromSeconds(3L));
		results.Add((after == target && hw.LcPumpDuty == baseline) ? new VerifyResult("液冷", "水泵档位", VerifyOutcome.Pass, $"基线={baseline} 发{target}后回读={after} 已恢复") : new VerifyResult("液冷", "水泵档位", VerifyOutcome.Fail, $"基线={baseline} 发{target}后回读={after} 恢复后={hw.LcPumpDuty} 确认={sent}"));
	}

	private static void VerifyGpuModeByDesign(MechrevoHw hw, List<VerifyResult> results)
	{
		results.Add(new VerifyResult("显卡模式", "模式切换", VerifyOutcome.SkippedByDesign, "切换需重启生效且失败可能导致无显示输出，不在自动验证里做。" + $"当前回读 GpuMode={hw.GpuMode} 核显支持={hw.IgpuOnlyStatusSupport} 直连支持={hw.DgpuDirectStatusSupport}"));
	}

	private static void Report(List<VerifyResult> results, string outputPath, Dictionary<string, string> rawPayloads)
	{
		string directoryName = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(outputPath, JsonConvert.SerializeObject(new
		{
			GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			Total = results.Count,
			Pass = Count(VerifyOutcome.Pass),
			Fail = Count(VerifyOutcome.Fail),
			Unsupported = Count(VerifyOutcome.Unsupported),
			SkippedByDesign = Count(VerifyOutcome.SkippedByDesign),
			RestartRequired = Count(VerifyOutcome.RestartRequired),
			Results = results,
			RawPayloads = rawPayloads
		}, Formatting.Indented));
		foreach (VerifyResult result in results)
		{
			Logger.WriteLine($"VERIFY [{result.Outcome}] {result.Group}/{result.Name}: {result.Detail}");
		}
		Console.WriteLine();
		foreach (VerifyResult result2 in results)
		{
			Console.WriteLine($"{result2.Outcome,-16}{result2.Group,-12}{result2.Name,-18}{result2.Detail}");
		}
		Console.WriteLine();
		Console.WriteLine($"合计 {results.Count}: 通过 {Count(VerifyOutcome.Pass)}, 失败 {Count(VerifyOutcome.Fail)}, 不支持 {Count(VerifyOutcome.Unsupported)}, 有意跳过 {Count(VerifyOutcome.SkippedByDesign)}, 需重启 {Count(VerifyOutcome.RestartRequired)}");
		Console.WriteLine("报告: " + outputPath);
		int Count(VerifyOutcome outcome)
		{
			return results.Count((VerifyResult r) => r.Outcome == outcome);
		}
	}
}
