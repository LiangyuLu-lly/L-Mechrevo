using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MechrevoLite.Display;

internal static class ScreenNative
{
	public const int ENUM_CURRENT_SETTINGS = -1;

	/// <summary>
	/// 内屏识别可信度（2026-10-09 多显示器刷新率修复）。
	/// ⚠️ 只有 <see cref="F:InternalScreenConfidence.Embedded" /> 才允许被"刷新率校正"这类高风险逻辑使用。
	/// </summary>
	internal enum InternalScreenConfidence
	{
		/// <summary>没识别出内屏，或只能靠不可信的线索（含"退化为 primary"）。</summary>
		None = 0,
		/// <summary>输出技术明确是内屏：INTERNAL / DISPLAYPORT_EMBEDDED（eDP）。最可信。</summary>
		Embedded = 1,
		/// <summary>仅靠"记录的型号名唯一匹配"（老兜底：内屏不以 eDP/INTERNAL 上报的机型）。</summary>
		UniqueName = 2
	}

	/// <summary>
	/// 不具识别力的"通用屏名"：外接屏、虚拟显示适配器也常叫这些名。
	/// ⚠️ 空名/通用名**绝不能再当内屏线索** —— 旧实现把空名当通配符（`名字 == text` 且 `text == ""`），
	/// 在"内屏 + 外接屏 + 虚拟显示适配器"组合下会选到没有型号名的外接屏/虚拟屏。
	/// </summary>
	private static readonly string[] GenericDisplayNames = new string[4] { "Generic PnP Monitor", "Generic Non-PnP Monitor", "通用即插即用监视器", "通用非即插即用监视器" };

	/// <summary>输出技术是否为"物理内嵌屏"（eDP / INTERNAL）。</summary>
	private static bool IsEmbeddedOutput(DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME device)
	{
		if (device.outputTechnology != DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL)
		{
			return device.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED;
		}
		return true;
	}

	/// <summary>记录的"内屏型号名"是否可用于兜底匹配（必须非空、非通用名）。</summary>
	private static bool IsUsableInternalDisplayName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.Trim();
		foreach (string item in GenericDisplayNames)
		{
			if (string.Equals(text, item, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	private readonly struct DisplayCandidate
	{
		public DisplayCandidate(string? friendlyName, string monitorDevicePath, string technology, string? gdiDeviceName, bool embedded, bool edp)
		{
			FriendlyName = friendlyName;
			MonitorDevicePath = monitorDevicePath;
			Technology = technology;
			GdiDeviceName = gdiDeviceName;
			Embedded = embedded;
			Edp = edp;
		}

		public string? FriendlyName { get; }

		public string MonitorDevicePath { get; }

		public string Technology { get; }

		public string? GdiDeviceName { get; }

		public bool Embedded { get; }

		/// <summary>是否为 DISPLAYPORT_EMBEDDED（eDP）——外接屏物理上不可能是 eDP，识别力最强。</summary>
		public bool Edp { get; }
	}

	public static IEnumerable<DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME> GetAllDevices()
	{
		int displayConfigBufferSizes = DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out var numPathArrayElements, out var modeCount);
		if (displayConfigBufferSizes != 0)
		{
			throw new Win32Exception(displayConfigBufferSizes);
		}
		DisplayNative.DISPLAYCONFIG_PATH_INFO[] pathInfoArray = new DisplayNative.DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
		DisplayNative.DISPLAYCONFIG_MODE_INFO[] modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
		displayConfigBufferSizes = DisplayNative.QueryDisplayConfig(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref numPathArrayElements, pathInfoArray, ref modeCount, modes, IntPtr.Zero);
		if (displayConfigBufferSizes != 0)
		{
			throw new Win32Exception(displayConfigBufferSizes);
		}
		for (int i = 0; i < modeCount; i++)
		{
			if (modes[i].infoType == DisplayNative.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
			{
				DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName = default;
				deviceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
				deviceName.header.size = (uint)Marshal.SizeOf(deviceName);
				deviceName.header.adapterId = modes[i].adapterId;
				deviceName.header.id = modes[i].id;
				displayConfigBufferSizes = DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName);
				if (displayConfigBufferSizes == 0)
				{
					yield return deviceName;
				}
				else
				{
					Logger.WriteLine("DisplayConfigGetDeviceInfo error: " + new Win32Exception(displayConfigBufferSizes).Message);
				}
			}
		}
	}

	public static string? FindLaptopScreen(bool log = false)
	{
		return FindLaptopScreen(out _, log);
	}

	/// <summary>
	/// 2026-10-09（多显示器修复）：识别"内屏"并同时给出可信度。
	/// 两遍挑选：
	/// ① 先只认输出技术明确为 INTERNAL / DISPLAYPORT_EMBEDDED（eDP）的屏 —— 最可信，正常机型都走这条；
	/// ② 只有在**完全没有**这类输出时，才退化为"按已记录的型号名匹配"，且要求型号名非空、非通用名、
	///    并且在所有活动屏里**唯一**匹配（否则返回 null，不猜）。
	/// ⚠️ 旧实现（名字 == 记录名，含空名通配）在"内屏 + 外接屏（300Hz）+ 虚拟显示适配器"组合下
	/// 可能选到外接屏/虚拟屏（症状：屏幕标签显示 300Hz，而 GCU 给的内屏档位只有 180）。
	/// </summary>
	public static string? FindLaptopScreen(out InternalScreenConfidence confidence, bool log = false)
	{
		return FindLaptopScreen(out confidence, out _, log);
	}

	/// <summary>
	/// 同 <see cref="M:FindLaptopScreen(System.String@,System.Boolean)" />，另外回传被选中屏的输出技术（仅供日志取证）。
	/// </summary>
	public static string? FindLaptopScreen(out InternalScreenConfidence confidence, out string technology, bool log = false)
	{
		confidence = InternalScreenConfidence.None;
		technology = "(none)";
		try
		{
			int displayConfigBufferSizes = DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out var numPathArrayElements, out var numModeInfoArrayElements);
			if (displayConfigBufferSizes != 0)
			{
				throw new Win32Exception(displayConfigBufferSizes);
			}
			DisplayNative.DISPLAYCONFIG_PATH_INFO[] array = new DisplayNative.DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
			DisplayNative.DISPLAYCONFIG_MODE_INFO[] modeInfoArray = new DisplayNative.DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];
			displayConfigBufferSizes = DisplayNative.QueryDisplayConfig(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref numPathArrayElements, array, ref numModeInfoArrayElements, modeInfoArray, IntPtr.Zero);
			if (displayConfigBufferSizes != 0)
			{
				throw new Win32Exception(displayConfigBufferSizes);
			}
			List<DisplayCandidate> candidates = new List<DisplayCandidate>();
			DisplayNative.DISPLAYCONFIG_PATH_INFO[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				DisplayNative.DISPLAYCONFIG_PATH_INFO dISPLAYCONFIG_PATH_INFO = array2[i];
				DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName = default;
				deviceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
				deviceName.header.size = (uint)Marshal.SizeOf(deviceName);
				deviceName.header.adapterId = dISPLAYCONFIG_PATH_INFO.targetInfo.adapterId;
				deviceName.header.id = dISPLAYCONFIG_PATH_INFO.targetInfo.id;
				if (DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName) != 0)
				{
					continue;
				}
				DisplayNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName2 = default;
				deviceName2.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
				deviceName2.header.size = (uint)Marshal.SizeOf(deviceName2);
				deviceName2.header.adapterId = dISPLAYCONFIG_PATH_INFO.sourceInfo.adapterId;
				deviceName2.header.id = dISPLAYCONFIG_PATH_INFO.sourceInfo.id;
				string? gdi = ((DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName2) == 0) ? ExtractDisplay(deviceName2.viewGdiDeviceName) : null);
				candidates.Add(new DisplayCandidate(deviceName.monitorFriendlyDeviceName, deviceName.monitorDevicePath, deviceName.outputTechnology.ToString(), gdi, IsEmbeddedOutput(deviceName), deviceName.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED));
			}
			// ①-a 先认 eDP（DISPLAYPORT_EMBEDDED）：外接屏物理上不可能是 eDP，最可靠；
			// ①-b 再认 INTERNAL（0x80000000）：部分机型/虚拟显示适配器会用这个值，故排在 eDP 之后。
			foreach (DisplayCandidate item in candidates)
			{
				if (item.Edp)
				{
					(string? gdi2, InternalScreenConfidence level, string tech) = ResolveCandidate(item, InternalScreenConfidence.Embedded, log);
					confidence = level;
					technology = tech;
					return gdi2;
				}
			}
			foreach (DisplayCandidate item in candidates)
			{
				if (item.Embedded)
				{
					(string? gdi4, InternalScreenConfidence level3, string tech2) = ResolveCandidate(item, InternalScreenConfidence.Embedded, log);
					confidence = level3;
					technology = tech2;
					return gdi4;
				}
			}
			string? text = AppConfig.GetString("internal_display");
			if (IsUsableInternalDisplayName(text))
			{
				int num = 0;
				DisplayCandidate displayCandidate = default;
				foreach (DisplayCandidate item2 in candidates)
				{
					if (item2.FriendlyName == text)
					{
						num++;
						displayCandidate = item2;
					}
				}
				if (num == 1)
				{
					(string? gdi3, InternalScreenConfidence level2, string tech3) = ResolveCandidate(displayCandidate, InternalScreenConfidence.UniqueName, log);
					confidence = level2;
					technology = tech3;
					return gdi3;
				}
				if (num > 1)
				{
					Logger.WriteLine($"Internal display name \"{text}\" matches {num} active displays -> ambiguous, not used");
				}
			}
			if (log)
			{
				Logger.WriteLine("Internal screen off");
			}
			return null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
			return null;
		}
	}

	/// <summary>
	/// 把候选屏解析成 GDI 设备名（`\\.\DISPLAYx`），并落盘内屏型号名。
	/// ⚠️ 取不到源设备名时保留旧口径（退化为 primary）供老调用方使用，但**把可信度降为 None**
	/// ⇒ 刷新率校正这类高风险消费者不会据此动手（宁可不动，也不能作用在别的屏上）。
	/// </summary>
	private static (string? GdiDeviceName, InternalScreenConfidence Confidence, string Technology) ResolveCandidate(DisplayCandidate candidate, InternalScreenConfidence confidence, bool log)
	{
		string? text = candidate.GdiDeviceName;
		if (text == null)
		{
			text = Screen.PrimaryScreen?.DeviceName;
			confidence = InternalScreenConfidence.None;
			Logger.WriteLine("Internal screen source name unavailable -> primary fallback (confidence=None)");
		}
		if (log)
		{
			Logger.WriteLine(candidate.MonitorDevicePath + " " + candidate.Technology);
		}
		if (!string.IsNullOrEmpty(candidate.FriendlyName))
		{
			AppConfig.Set("internal_display", candidate.FriendlyName);
		}
		return (text, confidence, candidate.Technology);
	}

	/// <summary>
	/// 重新解析"内屏 + 可信度"，并确认传进来的 GDI 设备名**就是**那块内屏；
	/// 不是（或认不出）一律降为 <see cref="F:InternalScreenConfidence.None" /> ⇒ 调用方不得据此落盘。
	/// </summary>
	internal static InternalScreenConfidence ResolveConfidenceFor(string? laptopScreen)
	{
		if (laptopScreen == null)
		{
			return InternalScreenConfidence.None;
		}
		try
		{
			string? text = FindLaptopScreen(out InternalScreenConfidence confidence, out _, log: false);
			if (string.Equals(text, laptopScreen, StringComparison.OrdinalIgnoreCase))
			{
				return confidence;
			}
		}
		catch
		{
		}
		return InternalScreenConfidence.None;
	}

	/// <summary>
	/// 只测量、**不落盘**的"该设备系统侧最大刷新率"。给纠正侧核对用。
	/// 2026-10-10（无 GCU / 认不出内屏时刷新率仍要可用）：
	/// `laptopScreen == null` 表示"**认不出**哪块是内屏"，**不等于**"读不到刷新率" ——
	/// 此时改为把 null 交给 EnumDisplaySettingsEx（Win32 语义：null = **当前显示设备**）继续测量，
	/// 于是无 GCU / 星耀类机型 / 内屏关闭只剩外接屏时也能量到值。
	/// ⚠️ 代价：量到的可能是**外接屏**的值。所以本方法只允许喂给"显示"用，**绝不作为落盘依据**；
	/// 落盘判据在 <see cref="M:GetMaxRefreshRate(System.String,InternalScreenConfidence)" />（只认 Embedded）。
	/// </summary>
	public static int MeasureMaxRefreshRate(string? laptopScreen)
	{
		// 认不出内屏（含内屏关闭 + 只枚举活动路径时找不到）→ 退化为"当前显示设备"，只读不写。
		if (laptopScreen == null)
		{
			Logger.WriteLineIfChanged("screen-fallback-measure", "screen metrics: 未识别到内屏 -> 退化为当前显示设备测上限（只读，不落盘）");
		}
		DisplayNative.DEVMODE lpDevMode = CreateDevmode();
		int num = -1;
		for (int i = 0; DisplayNative.EnumDisplaySettingsEx(laptopScreen, i, ref lpDevMode) != 0; i++)
		{
			if (lpDevMode.dmDisplayFrequency > num)
			{
				num = lpDevMode.dmDisplayFrequency;
			}
		}
		return num;
	}

	/// <summary>
	/// 2026-10-09（"双屏同开一次后刷新率永久不正确"根因修复 · 写入侧硬口径）：
	/// 原实现只要量到 > 0 就把实测最大值写进 config 的 `screen_max`，**不区分这块屏是不是内屏** ——
	/// 双屏同开时若取屏落到外接屏（极光X 外接 300Hz），就会把 300 当成"本机内屏上限"落盘；
	/// 之后"单屏 → 单屏"的切换不会再走这条写路径，脏值被**永久沿用**
	/// （机主原话：「好像是只要两个屏幕同时开过一次，后面刷新率就识别不到正确的了」）。
	/// 现改为：**只有**输出技术明确是 INTERNAL / DISPLAYPORT_EMBEDDED（eDP，即 confidence == Embedded）
	/// 时才允许落盘；判不出来就**保持 config 原值、不写**（不写空、不写 0），并把原因写进日志。
	/// </summary>
	public static int GetMaxRefreshRate(string? laptopScreen, InternalScreenConfidence confidence)
	{
		if (laptopScreen == null)
		{
			return -1;
		}
		int num = MeasureMaxRefreshRate(laptopScreen);
		int num2 = AppConfig.Get("screen_max");
		if (num > 0 && confidence == InternalScreenConfidence.Embedded)
		{
			// 能确定是 eDP 内屏：实测值可信 → 落盘。config 里若是更大的旧值（被外接屏污染过），这里就是纠正点。
			AppConfig.Set("screen_max", num);
			if (num2 > num)
			{
				Logger.WriteLine($"screen metrics: source=eDP measured_max={num} (旧值 screen_max={num2} 已纠正)");
			}
			else
			{
				Logger.WriteLineIfChanged("screen-max-write", $"screen metrics: source=eDP measured_max={num} -> screen_max 已落盘（旧值 {num2}）");
			}
			return num;
		}
		if (num > 0)
		{
			// 非 eDP（外接屏/虚拟屏/认不出）：绝不落盘，返回 config 原值；原值也没有才回落实测值（仍不写盘）。
			Logger.WriteLineIfChanged("screen-max-skip", $"screen metrics: source={confidence} measured_max={num} 未落盘（非 eDP，screen_max 保持 {num2}）");
			return (num2 > 0) ? num2 : num;
		}
		return num2;
	}

	public static int GetMaxRefreshRate(string? laptopScreen)
	{
		return GetMaxRefreshRate(laptopScreen, ResolveConfidenceFor(laptopScreen));
	}

	/// <summary>
	/// 2026-10-09（多显示器刷新率修复）：枚举「当前活动屏」在系统侧可用的刷新率档位。
	/// 只取与当前显示模式同分辨率（且位深一致，若系统给了位深）的档位，
	/// 避免把 800x600@24Hz 之类的低分档位混进界面。
	/// </summary>
	public static IReadOnlyList<int> GetAvailableRefreshRates(string? laptopScreen)
	{
		return GetAvailableRefreshRates(laptopScreen, allowCurrentDeviceFallback: false);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 「屏幕刷新率没显示」）：
	/// <paramref name="laptopScreen" /> 为 <c>null</c>（**认不出内屏**）时，
	/// 可选地**退化为「当前显示设备」**（Win32 语义：<c>EnumDisplaySettingsEx(null, …)</c> = 当前显示设备）
	/// —— 与 <see cref="GetRefreshRate" /> 自 2026-10-10 起就有的那条兜底**同构**。
	/// <para>⚠️ 为什么需要这个开关（真机诊断包实证 · 蛟龙16Pro / <c>MRID6-23</c>）：</para>
	/// <para>那台机接着 HDMI 外接屏（<c>S2719DGF</c>）时，活动显示路径里**没有内屏**
	/// ⇒ <see cref="FindLaptopScreen" /> 返回 <c>null</c>、confidence=None；</para>
	/// <para>而 <see cref="GetRefreshRate" /> 因已有兜底 ⇒ **能读到 144Hz**；
	/// 本方法却因首句 <c>if (laptopScreen == null) return list;</c> ⇒ **返回空列表**
	/// ⇒ 界面 <c>_hzButtons.Count == 0</c> ⇒ **「刷新率」整行消失**（用户报「宝龙达屏幕刷新率没显示」）。</para>
	/// <para>⚠️⚠️ 默认（<c>false</c>）**保持原行为逐位不变** —— 因为**落盘 / 档位校正**那条路
	/// （<c>MechrevoHw</c> 里 eDP 门槛之后的实测最大值）**绝不能**把外接屏的档位当成内屏的
	/// ⇒ 那边必须先过 <c>confidence == Embedded</c> 才调用本方法（现状已如此，故不受本改动影响）。</para>
	/// <para>⭐ 目前只有**界面**那一处传 <c>true</c>，且只在 **宝龙达** 机型上 ——
	/// 其余机型（GCU / 老接口）行为**逐位不变**。</para>
	/// </summary>
	public static IReadOnlyList<int> GetAvailableRefreshRates(string? laptopScreen, bool allowCurrentDeviceFallback)
	{
		List<int> list = new List<int>();
		if (laptopScreen == null)
		{
			if (!allowCurrentDeviceFallback)
			{
				return list;
			}
			// ⭐ 退化为「当前显示设备」——下面把 null 原样交给 EnumDisplaySettingsEx 即可
			//   （⛔ 不在此处纠偏为某块具体屏：那是「猜」，宁可显示当前活动屏的真档位）。
			Logger.WriteLineIfChanged("hz-rates-current-device-fallback",
				"hz source: 未识别到内屏 -> 档位枚举退化为「当前显示设备」（只用于界面显示与即时切换，⛔ 不落盘）");
		}
		DisplayNative.DEVMODE current = CreateDevmode();
		if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref current) == 0)
		{
			return list;
		}
		DisplayNative.DEVMODE mode = CreateDevmode();
		for (int i = 0; i < 4096; i++)
		{
			if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, i, ref mode) == 0)
			{
				break;
			}
			if (mode.dmPelsWidth != current.dmPelsWidth || mode.dmPelsHeight != current.dmPelsHeight)
			{
				continue;
			}
			if (current.dmBitsPerPel != 0 && mode.dmBitsPerPel != 0 && mode.dmBitsPerPel != current.dmBitsPerPel)
			{
				continue;
			}
			if (mode.dmDisplayFrequency > 0 && !list.Contains(mode.dmDisplayFrequency))
			{
				list.Add(mode.dmDisplayFrequency);
			}
		}
		return list;
	}

	/// <summary>
	/// 读当前刷新率。2026-10-10：`laptopScreen == null`（认不出内屏）时不再直接放弃，
	/// 而是把 null 交给 EnumDisplaySettingsEx（Win32 语义：null = **当前显示设备**）——
	/// 这样"无 GCU / 星耀类 / 内屏关闭"时界面仍能显示真实刷新率并允许按钮操作。
	/// ⚠️ 同样只用于显示与即时切换，**不作为落盘依据**（落盘只认 Embedded）。
	/// </summary>
	public static int GetRefreshRate(string? laptopScreen)
	{
		if (laptopScreen == null)
		{
			Logger.WriteLineIfChanged("screen-fallback-current", "screen metrics: 未识别到内屏 -> 退化为当前显示设备读当前刷新率（只读，不落盘）");
		}
		DisplayNative.DEVMODE lpDevMode = CreateDevmode();
		if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, -1, ref lpDevMode) == 0)
		{
			return -1;
		}
		return lpDevMode.dmDisplayFrequency;
	}

	/// <summary>
	/// 切换刷新率。2026-10-10：参数放宽为 `string?` —— 调用方 <see cref="M:ScreenControl.SetScreen(System.Int32)" />
	/// 传的是 `FindLaptopScreen()` 的结果，认不出内屏时就是 null；null 交给 EnumDisplaySettingsEx
	/// 表示**当前显示设备**，于是"无 GCU / 内屏关闭"时按钮依然能生效。
	/// ⚠️ 本方法**从不落盘**（不碰 AppConfig），只走 ChangeDisplaySettingsEx。
	/// </summary>
	public static int SetRefreshRate(string? laptopScreen, int frequency = 120)
	{
		// 认不出内屏 → 作用在"当前显示设备"上（只读当前模式 + 立即切换，不写 config）。
		if (laptopScreen == null)
		{
			Logger.WriteLineIfChanged("screen-fallback-set", "screen metrics: 未识别到内屏 -> 退化为当前显示设备切换刷新率（不落盘）");
		}
		DisplayNative.DEVMODE lpDevMode = CreateDevmode();
		if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, -1, ref lpDevMode) == 0)
		{
			return 0;
		}
		lpDevMode.dmDisplayFrequency = frequency;
		int num = DisplayNative.ChangeDisplaySettingsEx(laptopScreen, ref lpDevMode, IntPtr.Zero, DisplayNative.DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
		Logger.WriteLine("Screen = " + frequency + "Hz : " + ((num == 0) ? "OK" : ((object)num)));
		return num;
	}

	public static DisplayNative.DEVMODE CreateDevmode()
	{
		DisplayNative.DEVMODE dEVMODE = new DisplayNative.DEVMODE
		{
			dmDeviceName = new string(new char[32]),
			dmFormName = new string(new char[32])
		};
		dEVMODE.dmSize = (short)Marshal.SizeOf(dEVMODE);
		return dEVMODE;
	}

	private static string ExtractDisplay(string input)
	{
		int num = input.IndexOf('\\', 4);
		if (num == -1)
		{
			return input;
		}
		return input.Substring(0, num);
	}
}
