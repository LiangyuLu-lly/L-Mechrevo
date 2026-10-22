using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
// ⭐ 2026-10-16（0.291.8.1）：单实例互斥体改用带 ACL 的创建（与 ProcessHelper 的全局事件同族写法）
// ⇒ 需要这两个命名空间：MutexSecurity/MutexAccessRule/MutexRights/AccessControlType + WellKnownSidType/SecurityIdentifier。
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using MQTTnet.Protocol;
using MechrevoLite.Battery;
using MechrevoLite.Diagnostics;
using MechrevoLite.Display;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using MechrevoLite.Overlay;
using MechrevoLite.UI;
using MechrevoLite.Update;
using MechrevoLite.Usage;
using Microsoft.Win32;

namespace MechrevoLite;

internal static class Program
{
	public enum PowerSource
	{
		Battery,
		Barrel,
		USBC
	}

	public static NotifyIcon trayIcon;

	/// <summary>
	/// 2026-10-09（批次3 #10「托盘 NotifyIcon 懒加载」）：托盘还没建时先记下的提示文本（见 <see cref="SetTrayTip" />），
	/// 建好那一刻补上 —— 这样"启动早期状态刷新设过的 tooltip"不会因为懒加载而丢。
	/// </summary>
	private static string _pendingTrayTip;

	private static Mutex? _singleInstanceMutex;

	private static EventWaitHandle? _showMainEvent;

	/// <summary>2026-10-09（dupfeedback）：第二个实例敲的"又有人双击了"通知门（见 ctor 附近的单实例分支）。</summary>
	private static EventWaitHandle? _duplicateNotifyEvent;

	public static AsusACPI acpi;

	public static MechrevoHw? hw;

	public static MechrevoService service;

	public static KeyboardRgb rgb;

	public static WaterCoolerBle? ble;

	public static SettingsForm settingsForm;

	public static ModeControl modeControl;

	public static GPUModeControl gpuControl;

	public static ToastForm toast;

	public static HardwareOverlay? hardwareOverlay;

	public static nint unRegPowerNotify;

	public static nint unRegPowerNotifyLid;

	public static nint unRegPowerNotifyEnergy;

	public static nint unRegSuspendResume;

	public static int WM_TASKBARCREATED = 0;

	private static long lastAuto;

	private static readonly object autoLock = new object();

	private static long lastTheme;

	private static int _exitStarted;

	private static int _trayRefreshInFlight;

	private static System.Windows.Forms.Timer? _trayRetryTimer;

	private static bool _showGuideOnFirstRestore;

	private static readonly SemaphoreSlim _hardwareRecoveryLock = new SemaphoreSlim(1, 1);

	private static readonly SemaphoreSlim _keyboardRestoreLock = new SemaphoreSlim(1, 1);

	private static readonly SemaphoreSlim _lightingStateLock = new SemaphoreSlim(1, 1);

	private static readonly LightingRestoreCoordinator _lightingRestoreCoordinator = new LightingRestoreCoordinator();

	private static readonly UiRefreshCoalescer _capabilitiesUiRefresh = new UiRefreshCoalescer();

	private static readonly SemaphoreSlim _officialUiSettleLock = new SemaphoreSlim(1, 1);

	private static int _officialUiSettled;

	private static readonly SemaphoreSlim _telemetryRecoveryLock = new SemaphoreSlim(1, 1);

	private static int _keyboardStatusRecoveryPending;

	private static long _keyboardStatusBaselineVersion;

	private static int _keyboardStatusBaselineBrightness = -1;

	private static string _keyboardStatusBaselineEffect = "";

	private static int _keyboardFirmwareTakeoverGeneration;

	private static int _keyboardEffectProtectArmed;

	private static long _keyboardEffectProtectBaselineVersion;

	private static long _keyboardEffectProtectDeadlineMs;

	private const int KeyboardEffectProtectWindowMs = 3000;

	private static int _resumeKeyboardRestorePending;

	// 2026-10-22（耀世睡眠唤醒键盘灯不重新打开）：睡前记键盘电源，唤醒时即使无已保存灯效配置也恢复键盘灯
	private static int _kbPowerBeforeSuspend = -1;
	// （同方单键 RGB 开机闪）：硬件状态重申的最后一次执行时间（TickCount64；用于 3s 防抖）。
	private static long _lastReassertKeyboardCustomModeTick;

	private static System.Threading.Timer? _lightingIdleTimer;

	private static int _lightingIdleSuspended;
	// （平台互斥）：同方/GCU 的「自动关灯：已启用」日志只打一次（去重；关闭后重置）。
	private static int _lightingAutoOffEnableLogged;

	private static int _keyboardPowerTemporarilySuspended;

	private static int _lightingRestoreRequestId;

	private static readonly ConcurrentDictionary<string, int> _lightingExternalRestoreAppliedIds = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private static int _keyboardRestoreAppliedRequestId = int.MinValue;

	private static int _keyboardFirstHidStartPending = 1;

	private static readonly Dictionary<string, bool> _externalPowerBeforeTemporarySuspend = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	/// <summary>⭐ 启动 Logo 恢复判断只记一次（0=未记 · 1=已记）；⛔ 避免恢复重试时刷屏 ✅</summary>
	private static int _logoRestoreDecisionLogged;

	/// <summary>⭐ Logo powerexplicit 一次性迁移是否已检查（0=未查 · 1=已查）；⛔ 只对宝龙达执行 ✅</summary>
	private static int _logoExplicitMigrationChecked;

	private static readonly TimeSpan TelemetryStaleAfter = TimeSpan.FromSeconds(45L);

	private static readonly TimeSpan TelemetryRecoveryCooldown = TimeSpan.FromSeconds(60L);

	private static System.Threading.Timer? _telemetryRecoveryTimer;

	private static long _lastTelemetryRecoveryRequest;

	private static int _lastRecoveredConnectionGeneration;

	private static int _pendingStartupPerformanceMode = -1;

	private static readonly (string Topic, string SwitchKey, Func<bool> Supported)[] ExternalLightChannels = new (string, string, Func<bool>)[4]
	{
		("HidLightbar/Ctrl", "lightbar", () => hw.SupportsLightbar),
		("HidLightbar_Logo/Ctrl", "logolight", () => hw.SupportsLogoLight),
		("HidLightbar_Hinge/Ctrl", "hingelight", () => hw.SupportsHingeLight),
		("HidLightbar_Sync/Ctrl", "synclight", () => hw.SupportsSyncLight)
	};

	private const int KeyboardPowerSettleMs = 120;

	/// <summary>外部灯光通道当前是否点亮（按界面回读的开关状态）。</summary>
	internal static bool IsExternalLightChannelOn(string topic)
	{
		foreach (var channel in ExternalLightChannels)
		{
			if (string.Equals(channel.Topic, topic, StringComparison.Ordinal))
			{
				return hw != null && hw.QuickSwitches.TryGetValue(channel.SwitchKey, out bool on) && on;
			}
		}
		return false;
	}

	public static PowerSource? lastObservedSource;

	/// <summary>⭐ 2026-10-15（真机回归修复）：**自动刷新率专用的"上次据以应用过的电源来源"**。
	/// <para>⚠️ 不能再拿 <see cref="lastObservedSource"/> 判"来源变了没" —— 它同时被**电源监视器**写
	/// （<c>Program.cs:2785 currentSource = powerSource</c>，而 <c>currentSource</c> 的 setter 就是写这个字段）。
	/// 监视器先跑 ⇒ 转换被"吃掉" ⇒ <c>flag</c> 恒 false ⇒ **离电不切 60Hz**（2026-10-15 用户实测：
	/// 日志 <c>AutoSetting for Offline (source=Battery, changed=False)</c> 且没有 <c>Screen = 60Hz</c>）。</para>
	/// <para>本字段**只由 <see cref="SetAutoModes"/> 写**，语义 = "上一次真正据以做过自动切换的来源"。</para></summary>
	private static PowerSource? lastAutoAppliedSource;

	private static PowerLineStatus lastLineStatus = SystemInformation.PowerStatus.PowerLineStatus;

	private static readonly System.Timers.Timer powerSettleTimer = new System.Timers.Timer
	{
		AutoReset = false
	};

	private static string InformationalVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

	internal static string ReleaseLabel => NormalizeReleaseLabel(InformationalVersion);

	internal static string ReleaseVersion => NormalizeReleaseVersion(InformationalVersion);

	internal static bool UiAuditMode { get; set; }

	internal static bool UiAuditUseReportedCapabilities { get; set; }

	internal static bool KeyboardEffectProtectArmed => Volatile.Read(in _keyboardEffectProtectArmed) != 0;

	internal static bool IsLightingTemporarilySuspended => LightingState.IsTemporarilySuspended(AppConfig.Is("lighting_off_on_battery"), currentSource == PowerSource.Battery, Volatile.Read(in _lightingIdleSuspended) != 0);

	internal static bool LightingIdleSuspended
	{
		get
		{
			return Volatile.Read(in _lightingIdleSuspended) != 0;
		}
		set
		{
			Volatile.Write(ref _lightingIdleSuspended, value ? 1 : 0);
		}
	}

	internal static int LightingRestoreRequestId => Volatile.Read(in _lightingRestoreRequestId);

	public static PowerSource currentSource
	{
		get
		{
			return lastObservedSource ?? ReadPowerSource();
		}
		set
		{
			lastObservedSource = value;
		}
	}

	internal static bool ResumeKeyboardRestorePending => Volatile.Read(in _resumeKeyboardRestorePending) != 0;

	internal static string NormalizeReleaseLabel(string informationalVersion)
	{
		string text = informationalVersion.Split('+')[0];
		int num = text.IndexOf('-');
		if (num < 0)
		{
			return text;
		}
		string text2 = text.Substring(num + 1);
		if (!text2.StartsWith("beta.", StringComparison.OrdinalIgnoreCase))
		{
			return text2;
		}
		return "beta" + text2.Substring(5);
	}

	internal static string NormalizeReleaseVersion(string informationalVersion)
	{
		return informationalVersion.Split('+')[0].Split('-')[0];
	}

	[STAThread]
	public static void Main(string[] args)
	{
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		AppDomain.CurrentDomain.UnhandledException += (object s, UnhandledExceptionEventArgs e) =>
		{
			TryRestoreScreenAfterFailure();
			Logger.WriteLine("Unhandled: " + e.ExceptionObject);
			Logger.FlushCrashBuffer("AppDomain.UnhandledException");
			// 2026-10-06 step8 (upstream A-6): 崩溃现场同步进本地待传证据。
			// 终止性异常只保证证据落盘，不做网络排空（崩溃路径不干重活）。
			UsageTelemetry.PersistCrashForUpload();
			UsageTelemetry.FlushFatalLog();
		};
		TaskScheduler.UnobservedTaskException += (object? s, UnobservedTaskExceptionEventArgs e) =>
		{
			Logger.WriteLine("Unobserved: " + e.Exception);
			Logger.FlushCrashBuffer("TaskScheduler.UnobservedTaskException");
			UsageTelemetry.PersistCrashForUpload();
			e.SetObserved();
		};
		Application.ThreadException += (object _, ThreadExceptionEventArgs e) =>
		{
			TryRestoreScreenAfterFailure();
			Logger.WriteLine("UI thread exception: " + e.Exception);
			Logger.FlushCrashBuffer("Application.ThreadException");
			UsageTelemetry.PersistCrashForUpload();
		};
		string text = "";
		if (args.Length != 0)
		{
			text = args[0];
		}
		if (text == "--inpout-probe")
		{
			// 2026-10-22（机主：先探测 inpoutx64 能不能用）：只读 + 等值写自检，不改变风扇/电池行为。
			string inpoutReport = InpOutProbe.Run((args.Length > 1) ? args[1] : "");
			Console.WriteLine(inpoutReport);
			Logger.WriteLine("[inpout-probe] " + inpoutReport.Replace("\r\n", " | "));
			Logger.Close();
			return;
		}
		// 2026-10-22（机主：UI 探针）：强制启用某机型 UI 只做预览，不连接硬件、不下发命令。
		// 两条入口：命令行 --ui-probe <profile:form>；或环境变量 LMECHREVO_UI_PROBE=<profile:form>
		// （后者便于双击启动：设用户级变量后正常打开即可，不必拼命令行）。
		string uiProbeEnv = "";
		try
		{
			uiProbeEnv = Environment.GetEnvironmentVariable("LMECHREVO_UI_PROBE") ?? "";
		}
		catch
		{
			uiProbeEnv = "";
		}
		string uiProbeArg = (text == "--ui-probe") ? ((args.Length > 1) ? args[1] : "") : ((args.Length == 0 && uiProbeEnv.Trim().Length > 0) ? uiProbeEnv.Trim() : null);
		if (uiProbeArg != null)
		{
			Environment.ExitCode = UiProbeRunner.Run(uiProbeArg);
			Logger.Close();
			return;
		}
		switch (text)
		{
		// （机主要求「直接调 LM 接口」）： 键盘灯 WMI 探针走**主程序自己的通道**
		case "--kbd-probe":
		{
			string probeLog = KeyboardWmiProbe.Run();
			Console.WriteLine(probeLog);
			Logger.WriteLine("[kbd-probe] " + probeLog);
			return;
		}
		// （30 系「亮度只有 FN 能调」只读诊断）
		case "--kbd-fn-probe":
		{
			int fnProbeSeconds = 60;
			if (args.Length > 1 && int.TryParse(args[1], out int parsedFnSeconds))
			{
				fnProbeSeconds = Math.Clamp(parsedFnSeconds, 5, 300);
			}
			string fnProbeLog = KeyboardWmiProbe.RunFnProbe(fnProbeSeconds);
			Console.WriteLine(fnProbeLog);
			Logger.WriteLine("[kbd-probe] fn-probe: " + fnProbeLog);
			Logger.Close();
			return;
		}
		// **只读**扫描 EC RAM（0x0790–0x07FF / 0x1800–0x18FF / 0x8300–0x83BF）
		case "--kbd-ec-probe":
		{
			int ecProbeSeconds = 60;
			bool ecProbeUseLpc = false;
			for (int i = 1; i < args.Length; i++)
			{
				if (string.Equals(args[i], "--lpc", StringComparison.OrdinalIgnoreCase))
				{
					ecProbeUseLpc = true;
				}
				else if (int.TryParse(args[i], out int parsedEcSeconds))
				{
					ecProbeSeconds = Math.Clamp(parsedEcSeconds, 10, 300);
				}
			}
			string ecProbeLog = KeyboardEcProbe.Run(ecProbeSeconds, ecProbeUseLpc);
			Console.WriteLine(ecProbeLog);
			Logger.WriteLine("[kbd-ec-probe] " + ecProbeLog);
			Logger.Close();
			return;
		}
		// 读 P0 基线 → 核心/显存各 +50 → 回读 → 各 +100 → 回读 → 归零 → 回读；
		case "--gpu-oc-probe":
		{
			string probeLog = GpuOcProbe.Run();
			Console.WriteLine(probeLog);
			Logger.WriteLine("[gpu-oc-probe] " + probeLog);
			Logger.Close();
			return;
		}
		// （机主：GCU 最多 -900 显存？NV 范围到底多少）：**只读** 偏移范围探针。
		case "--gpu-offset-probe":
		{
			string offsetPath = GpuOffsetProbe.Run((args.Length > 1) ? args[1] : null);
			Console.WriteLine(offsetPath);
			Logger.WriteLine("[gpu-offset-probe] " + offsetPath);
			Logger.Close();
			return;
		}
		// （30 系 · SMU 只读探针）：先确认 PawnIO/SMU 能否读，再决定 PL/温度墙是否改走 SMU。
		case "--smu-probe":
		{
			string probeLog = SmuProbe.Run();
			Console.WriteLine(probeLog);
			Logger.WriteLine("[smu-probe] " + probeLog);
			Logger.Close();
			return;
		}
		// **只读**探测命令 --wujie-smu-probe（0 写入；桌面报告 + 日志）。
		case "--wujie-smu-probe":
		{
			string probeLog = WujieSmuProbe.Run();
			Console.WriteLine(probeLog);
			Logger.WriteLine("[wujie-smu-probe] " + probeLog);
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 风扇接管接口）：**只读**探针 `--fan-probe`。
		//   ⚠️ 2026-10-18 收紧：写 WMI 20=1 真机会卡死 ⇒ ⛔ 本探针**不再写任何字节**！
		//   ⭐ 内容：EC 0xC83C/0xC83D/0xB20 只读验证 + WMI 13/20/21 原始 hex 只读对照 + 结论。
		//   ⭐ EC 写通路验证请用 `--fan-ec-test`（白名单写 + finally 恢复）。
		//   结果写 %APPDATA%\MechrevoLite\log.txt 与桌面 lm-fan-probe-*.txt。
		case "--fan-probe":
		{
			string probeLog = FanProbe.Run();
			Console.WriteLine(probeLog);
			Logger.WriteLine("[fan-probe] " + probeLog);
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 风扇 EC 写通路）：**最小验证** `--fan-ec-test [--acpi] [--ec81]`。
		//   ⭐ 默认先 LPC 通道（白名单 3+1）写 Fan1 目标+5 → 置 bit1 → 读回；⛔ 失败自动改试 ACPI 0x93 写；
		//   ⭐ `--acpi` 强制只测 ACPI 0x93；⭐ `--ec81` 只做一次 0x81（WR_EC）写实验（仅 reg=0x60 ← 0x1060|0x80）；
		//   ⭐ 转速 ≥ +200 RPM ⇒ 可用；⛔ finally 恢复（目标 0、清位、0x1060 原值）。
		//   ⚠️ 需要管理员（PawnIO 驱动访问）；⛔ 不写 WMI 20/21。
		//   结果写 %APPDATA%\MechrevoLite\log.txt 与桌面 lm-fan-ec-test-*.txt。
		case "--fan-ec-test":
		{
			Environment.ExitCode = FanEcTest.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 风扇写入门面验证/放行）：`--fan-smu-ec-test`。
		//   ⭐ 只读对照（ACPI EC vs LPC vs SmuEcAccess）→ 最小写 0xB20（置 bit1）→ 回读确认；
		//   ⭐ 全部确认 ⇒ 写 `%APPDATA%\MechrevoLite\fan_ec_verified.cfg`（下次启动放开 UI 曲线/强冷）；
		//   ⛔ 不确认 ⇒ 不写配置、UI 保持隐藏；⛔ 不写 0x1060 / WMI 20/21。
		//   结果写 %APPDATA%\MechrevoLite\log.txt 与桌面 lm-fan-smu-ec-test-*.txt。
		case "--fan-smu-ec-test":
		{
			Environment.ExitCode = FanSmuEcTest.Run();
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（风扇闸门排查）：`--fan-gate-status`（⭐ 纯只读）。
		//   ⭐ 打印 FanCurveVerified（读 cfg 的结果）、PreferredChannel、cfg 路径/存在/内容、板号/代际；
		//   ⛔ 不探测 EC、不写任何文件、不发 WMI；⚠️ 改 cfg 后需重启 LM 才生效。
		case "--fan-gate-status":
		{
			Environment.ExitCode = FanGateStatus.Run();
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 充电阈值 · 阶段 1）：`--battery-limit-probe`（⭐ 纯只读）。
		//   ⭐ 读参考机型（蛟龙16 Pro 2023 · 40 系 · 模具 MRID6/BM5235 · 23 款 = MRID6-23）实测地址：
		//      常驻阈值 0x83A1/0x83A2 · 模式 0x83C0 · 命令 0x8151 · 触发 0x8150 · 结果 0x8152 ·
		//      mailbox 参数 0x8153/0x8154（+ 旁证地址/EC 版本）+ 当前电池状态；
		//   ⭐ 是否支持**只看 EC 是否含该布局**（0x83A1/0x83A2 是否为合理 1–100），⛔ 不按 30/40 标签；
		//   ⛔ 不写任何 EC 字节、⛔ 不发 WMI 写；结果写桌面 lm-battery-probe-*.txt。
		case "--battery-limit-probe":
		{
			Environment.ExitCode = BatteryLimitProbe.Run();
			Logger.Close();
			return;
		}
		// 2026-10-20（充电上限 · B 路只读探测）：`--charge-limit-wmi-probe`
		//   只读：查 root\WMI AcpiTest_MULong 是否存在、mailbox 读 0x0740/0x087F/0x07B9/0x07C3/0x0742/0x0770；
		//   不写任何寄存器、不改配置；退出码 0=可用 / 2=不可用 / 1=异常。
		case "--charge-limit-wmi-probe":
		{
			Environment.ExitCode = WmiStoredLimitProbe.Run();
			Logger.Close();
			return;
		}
		// 2026-10-20（充电上限 · B 路写入测试）：`--charge-limit-wmi-test [--value 60] [--restore]`
		//   写 0x087F（40..100）+ 回读校验；--restore 时按旧值还原；结果写桌面报告。
		case "--charge-limit-wmi-test":
		{
			int testValue = 60;
			bool restore = false;
			string[] rest = args.Skip(1).ToArray();
			for (int argIndex = 0; argIndex < rest.Length; argIndex++)
			{
				if (string.Equals(rest[argIndex], "--restore", StringComparison.OrdinalIgnoreCase))
				{
					restore = true;
				}
				else if (string.Equals(rest[argIndex], "--value", StringComparison.OrdinalIgnoreCase) && argIndex + 1 < rest.Length
					&& int.TryParse(rest[argIndex + 1], out int parsed))
				{
					testValue = parsed;
				}
			}
			Environment.ExitCode = WmiStoredLimitProbe.RunWrite(testValue, restore);
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 充电阈值 · 阶段 2）：`--battery-limit-test [--low 60 --high 80] [--disable]`。
		//   ⭐ 白名单写（仅 0x8153/0x8154/0x8151/0x8150）；严格时序；失败不重试、不做清理写；
		//   ⭐ 结果写桌面 lm-battery-limit-test-*.txt + [battery-limit-test] 日志。
		case "--battery-limit-test":
		{
			Environment.ExitCode = BatteryLimitTest.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（宝龙达 · 充电阈值）：`--battery-limit-status`（⭐ 纯只读）。
		//   ⭐ 打印 EC 常驻值 + 本地配置 + 是否一致；⛔ 不写、不触发恢复；平台闸拒绝非宝龙达（0 读 0 写）。
		case "--battery-limit-status":
		{
			Environment.ExitCode = BatteryLimitStatus.Run();
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（SMU-EC 只读核对）：`--smu-ec-probe`。
		//   ⭐ 核对 7.3 `RyzenAccess.Write_EC_EX/Read_EC_EX` 的真实通道（结论：LPC 端口 I/O，无 SMU 命令）；
		//   ⭐ 双通道（ACPI/LPC）只读对照；⛔ 不发 SMU 命令、不写任何 EC 字节。
		case "--smu-ec-probe":
		{
			Environment.ExitCode = SmuEcProbe.Run();
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-18（SMU 只读探索 · 阶段 1）：`--smu-reg-probe`。
		//   ⭐ 只用 `ioctl_read_smu_register` 读少量候选寄存器（间隔 10ms），打印 地址/rc/值/LastError；
		//   ⭐ 与 ACPI EC 只读值对照，看是否有 EC 映射迹象；⛔ 一个写都不发。
		case "--smu-reg-probe":
		{
			Environment.ExitCode = SmuRegisterProbe.Run();
			Logger.Close();
			return;
		}
		// L-Mechrevo.exe --smu-write-test --yes    保守写 + 每次回读 + finally 恢复快照并确认
		case "--smu-write-test":
			Environment.ExitCode = SmuWriteTest.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		// 2026-10-05 step7A (upstream 0.290.6 A-41): capability probe CLI.
		// L-Mechrevo.exe --cpu-tuning-probe [output.json]
		case "--cpu-tuning-probe":
		{
			using RuntimeCpuTuning runtimeCpuTuning = new RuntimeCpuTuning();
			string json = System.Text.Json.JsonSerializer.Serialize(runtimeCpuTuning.Probe(), new System.Text.Json.JsonSerializerOptions
			{
				WriteIndented = true
			});
			if (args.Length > 1)
			{
				string reportPath = Path.GetFullPath(args[1]);
				File.WriteAllText(reportPath, json);
				Console.WriteLine(reportPath);
				Logger.WriteLine("CPU tuning probe written to " + reportPath);
			}
			else
			{
				// WinExe has no attached console, so also drop the JSON somewhere readable.
				string fallbackPath = Path.Combine(Path.GetTempPath(), "lm-cpu-tuning-probe.json");
				try
				{
					File.WriteAllText(fallbackPath, json);
					Logger.WriteLine("CPU tuning probe written to " + fallbackPath);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("CPU tuning probe fallback write failed: " + ex.Message);
				}
				Console.WriteLine(json);
			}
			Logger.Close();
			return;
		}
		// 2026-10-05 coread: 台式机专用 SMU CO 往返验证工具（详见 SmuCoRoundtrip.cs）。
		// L-Mechrevo.exe --smu-co-roundtrip [margin] [--yes] [--out <file>]
		// 默认**只读**；只有带 --yes 才会用现有 SetCoAll 路径写一次全核 CO，并在退出前恢复。
		case "--smu-co-roundtrip":
			Environment.ExitCode = SmuCoRoundtrip.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		// 2026-10-05 percore-product: 「开机自动应用」的手动诊断入口（语义与开机**完全相同**）。
		//   L-Mechrevo.exe --cpu-co-auto-apply            只做检查并打印"开机时会做什么"，**不写**
		//   L-Mechrevo.exe --cpu-co-auto-apply --yes      真的按保存的每核值写（仅白名单代号；默认关）
		// 用途：① 在本机（不支持每核写的 APU）证明"能力检查拦住 -> 一条命令都不发"；
		//       ② 台式机用户重启后想确认自动应用是否生效时，可以直接跑一次看结论，不必重启机器。
		// 2026-10-05 uifix（用户要求④/验收第 5 条）：这里也打印**保底机制**的判定
		// （上次是否正常关机 / 这次会不会跳过），语义与开机完全一致（同一个 ApplySavedOnStartup）。
		case "--cpu-co-auto-apply":
		{
			bool write = args.Any((string a) => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase));
			SmuCoRoundtrip.TryAttachParentConsole();
			CpuTuningResult autoApplyResult = RuntimeCpuTuning.ApplySavedOnStartup(simulate: !write);
			CpuCoShutdownVerdict guard = RuntimeCpuTuning.GuardVerdict;
			string guardLine = $"cpu-co shutdown guard: lastShutdown={CpuCoAutoApplyGuard.Name(guard)} "
				+ $"({CpuCoAutoApplyGuard.Describe(guard)}) wouldSkip={(RuntimeCpuTuning.SkippedByGuard ? "YES" : "no")}";
			string line = $"cpu-co auto-apply: simulated={!write} switch={(RuntimeCpuTuning.PerCoreAutoApplyEnabled ? "ON" : "OFF")} "
				+ $"success={autoApplyResult.Success} verified={autoApplyResult.Verified} detail={autoApplyResult.Detail}";
			Console.WriteLine(guardLine);
			Console.WriteLine(line);
			Logger.WriteLine("[cpu-co] " + guardLine);
			Logger.WriteLine("[cpu-co] " + line);
			Logger.Close();
			Environment.ExitCode = (write && !autoApplyResult.Success) ? 1 : 0;
			return;
		}
		case "--gpu-oc-helper":
			Environment.ExitCode = ElevatedGpuOverclockApplier.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		case "--kill-process":
			Environment.ExitCode = ElevatedProcessKiller.RunHelper(args.Skip(1).ToArray());
			Logger.Close();
			return;
		case "--apply-update":
			Environment.ExitCode = UpdateInstaller.RunUpdater(args.Skip(1).ToArray());
			Logger.Close();
			return;
		case "--updatetest":
			Environment.ExitCode = UpdateSelfTest.Run(args.Skip(1).ToArray());
			Logger.Close();
			return;
		// 2026-10-09（75:00）：诊断包**无界面导出**入口（只读），只为本机实跑验收用：
		//   L-Mechrevo.exe --diag-pack <输出.zip>
		// ⚠️ 不初始化硬件（Program.hw/service 为 null 时各诊断节本来就写明「尚未创建」），
		//    不注册单实例互斥、不建托盘、不联网；采集/打包与 GUI 的「导出诊断包」**同一份实现**。
		//    WinExe 没有控制台，故结论同时写进 Logger（log.txt）并由调用方读文件确认。
		case "--diag-pack":
		{
			string diagZip = (args.Length > 1) ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), DiagnosticPackExporter.BuildFileName(ReleaseVersion, DateTime.Now));
			try
			{
				DiagnosticPackResult diagResult = DiagnosticPackRunner.ExportAsync(diagZip, DiagnosticPackCapture.Capture()).GetAwaiter().GetResult();
				Logger.WriteLine($"[diag-pack] 已导出: {diagResult.ZipPath}（{diagResult.SizeBytes} 字节，{diagResult.Entries.Count} 个条目）");
				Environment.ExitCode = 0;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[diag-pack] 导出失败: " + ex);
				Environment.ExitCode = 1;
			}
			Logger.Close();
			return;
		}
		// 2026-10-09（kbdctrl，用户原话「只搁置用户模式」⇒「给 LM 接通键盘灯」开工）：
		// 键盘灯 Keyboard/Ctrl 的 **离线**自测入口 —— 只把 KeyboardCtrlPayload 组装出来的 JSON 与
		// 2026-10-09 官方 30 系控制台抓包的首个 SetEffectALL 实测帧逐字段对照，**不联网、不连 13688、
		// 不发任何 MQTT 帧、不读写设置、不初始化硬件**：
		//   L-Mechrevo.exe --kbd-ctrl-payload [输出.txt]
		// WinExe 没有控制台，故结论同时写文件（给了路径就用它，否则落 %TEMP%\lm-kbd-ctrl-payload.txt）
		// 与 Logger（log.txt）；退出码 = 0 全一致 / 1 有不一致。
		case "--kbd-ctrl-payload":
		{
			string report = MechrevoLite.Hardware.KeyboardCtrlPayloadSelfTest.Run();
			string reportPath = (args.Length > 1) ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "lm-kbd-ctrl-payload.txt");
			try
			{
				File.WriteAllText(reportPath, report, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[kbd] 自测报告写入失败: " + ex.Message);
			}
			Logger.WriteLine("[kbd] Keyboard/Ctrl 离线自测（不发帧）报告: " + reportPath);
			Logger.WriteLine(report);
			Console.WriteLine(report);
			Environment.ExitCode = report.Contains("FAIL=0") ? 0 : 1;
			Logger.Close();
			return;
		}
		// 2026-10-09（40B≡40，用户原话「对了 LM里面好像有一些GCU判断条件 GCU40和40B应该等价」）：
		// GCU 变体家族（40 与 40B 等价）的**离线**自测入口 —— 只做字符串断言
		//（40/40B/40b/带空白 → 40 家族；30/50/GCU/未知/null/空 → 否；载荷身份 40≠40B 仍分开），
		// **不读注册表、不连 13688、不发任何 MQTT 帧、不读写设置、不初始化硬件**：
		//   L-Mechrevo.exe --gcu-variant-family [输出.txt]
		// WinExe 没有控制台，故结论同时写文件（给了路径就用它，否则落 %TEMP%\lm-gcu-variant-family.txt）
		// 与 Logger（log.txt）；退出码 = 0 全通过 / 1 有失败。
		// 2026-10-21（电池电压修复 · 通道探测）：`--i2ec-port-probe`
		//   只读探测 PawnIO LpcIO 是否放行 I2EC 端口 0x681/0x682/0x683；结果写桌面报告。
		case "--i2ec-port-probe":
		{
			bool allowWrite = args.Skip(1).Any((string a) => string.Equals(a, "--allow-write-probe", StringComparison.OrdinalIgnoreCase));
			Environment.ExitCode = I2ecPortProbe.Run(allowWrite);
			return;
		}
		case "--gcu-variant-family":
		{
			string familyReport = MechrevoLite.Hardware.GcuVariantFamily.RunSelfTest();
			string familyReportPath = (args.Length > 1) ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "lm-gcu-variant-family.txt");
			try
			{
				File.WriteAllText(familyReportPath, familyReport, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[gcu-family] 自测报告写入失败: " + ex.Message);
			}
			Logger.WriteLine("[gcu-family] GCU 变体家族离线自测报告: " + familyReportPath);
			Logger.WriteLine(familyReport);
			Console.WriteLine(familyReport);
			Environment.ExitCode = familyReport.Contains("FAIL=0") ? 0 : 1;
			Logger.Close();
			return;
		}
		}
		if (text == "--restart")
		{
			string selfName = Process.GetCurrentProcess().ProcessName;
			for (int i = 0; i < 50; i++)
			{
				try
				{
					if (Process.GetProcessesByName(selfName).Length <= 1)
					{
						break;
					}
				}
				catch
				{
					break;
				}
				Thread.Sleep(200);
			}
		}
		// ⭐⭐ 2026-10-16（0.291.8.1 · 修「翼龙15Pro 反复崩溃」——根因 + 正解）：
		// **根因**：本机自启任务以 `RunLevel=Highest`（提权）拉起 LM ⇒ 它建的
		//   `Global\L-Mechrevo-SingleInstance` **属于提权上下文**；随后用户**普通双击**时
		//   `new Mutex(…, 同名)` 会因该对象的默认 ACL 而被拒 ⇒
		//   `System.UnauthorizedAccessException: Access to the path 'Global\L-Mechrevo-SingleInstance' is denied.`
		//   ⇒ ⭐ **整个程序在 Main 里直接崩掉**（真机 crash.txt 从 10-13 到 10-15 连崩 5 次：08:44 / 23:52 / 09:54 / 23:17 / 10:20）❌
		// **正解**：给互斥体**显式安全描述符**（允许 Authenticated Users 打开/同步）——
		//   这正是本仓库 `ProcessHelper.CreateOrOpenExitEvent()` 对 `Global\LMechrevoApp-Exit`
		//   与 `Global\L-Mechrevo-ShowMain` 早就用过的同一套写法 ✅，只有这个**互斥体**当年漏了。
		//   ⇒ 两个完整性级别都能打开同一个互斥体 ⇒ ⭐ 单实例语义完整保留，⛔ 不再崩溃。
		//   ⚠️ `MutexAcl.Create` 在 .NET 10 可用（与 EventWaitHandleAcl 同族）。
		//   ⚠️ 万一 ACL 创建本身失败（老系统/受限环境）⇒ 回退到裸 `new Mutex`；
		//      再失败（真的没权限）⇒ ⭐ **不崩**，按"让位给已有实例"安静退出（记录一行）。
		bool createdNew = false;
		try
		{
			MutexSecurity mutexSecurity = new MutexSecurity();
			mutexSecurity.AddAccessRule(new MutexAccessRule(
				new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
				MutexRights.Modify | MutexRights.Synchronize,
				AccessControlType.Allow));
			_singleInstanceMutex = MutexAcl.Create(initiallyOwned: true, @"Global\L-Mechrevo-SingleInstance", out createdNew, mutexSecurity);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("单实例互斥体（带 ACL）创建失败，回退到普通创建：" + ex.Message);
			try
			{
				_singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\L-Mechrevo-SingleInstance", out createdNew);
			}
			catch (Exception ex2)
			{
				// ⭐ 权限不足（跨用户/跨完整性级别且 ACL 也没建成）⇒ 按"已有实例"处理，⛔ 不再让程序崩在 Main 里。
				Logger.WriteLine("单实例互斥体不可用（Global\\L-Mechrevo-SingleInstance）：" + ex2.Message + " ⇒ 按「已有实例」处理，本进程不启动主界面（⛔ 旧版在这里会直接崩溃）");
				Logger.Close();
				return;
			}
		}
		// （机主：休眠/睡眠唤醒后 LM 会自己弹出主界面）
		bool isStartupLaunchArg = IsStartupLaunch(text);
		if (!createdNew)
		{
			if (isStartupLaunchArg)
			{
				Logger.WriteLine("另一个实例已在运行；本次为「开机自启」拉起（startup）⇒ 只让位退出，⛔ 不唤起主界面（修「休眠唤醒后自动弹窗」）");
				Logger.Close();
				return;
			}
			// 2026-10-09（dupfeedback，用户原话「双击没反应」）：本进程只用**唤起 + 敲一下通知门**两件事，
			// 唤起旧窗的既有行为一字不改；气泡由**已有那个实例**用它的托盘图标弹（见 ShowDuplicateInstanceNotice），
			// 这样本进程可以立刻退出 —— 不留"看起来像两个实例"的 4 秒尾巴，也不依赖本进程有没有托盘。
			bool woke = false;
			try
			{
				using EventWaitHandle showEvent = EventWaitHandle.OpenExisting(@"Global\L-Mechrevo-ShowMain");
				showEvent.Set();
				woke = true;
			}
			catch (Exception)
			{
			}
			bool notified = false;
			try
			{
				using EventWaitHandle notifyEvent = EventWaitHandle.OpenExisting(@"Global\L-Mechrevo-NotifyDuplicate");
				notifyEvent.Set();
				notified = true;
			}
			catch (Exception)
			{
			}
			Logger.WriteLine(woke
				? (notified
					? "Another instance is running; requested it to show the main window and to show the tray notice."
					: "Another instance is running; requested it to show the main window (tray notice channel unavailable).")
				: "Another instance is running but its show-main event could not be opened (it may still be starting up); no tray notice was requested.");
			Logger.Close();
			return;
		}
		bool startMinimized = IsStartupLaunch(text);
		_showGuideOnFirstRestore = startMinimized;
		if ((text == "--secure-mqtt" || text == "--secure-mqtt-remove") ? true : false)
		{
			MqttSecurity.OperationResult operationResult = ((text == "--secure-mqtt") ? MqttSecurity.TryCreateInboundBlockRule() : MqttSecurity.TryRemoveInboundBlockRule());
			Logger.WriteLine(operationResult.Message);
			Console.WriteLine(operationResult.Message);
			Environment.ExitCode = ((!operationResult.Success) ? 1 : 0);
			Logger.Close();
			return;
		}
		bool flag;
		switch (text)
		{
		case "--register-autostart":
			if (!ProcessHelper.IsUserAdministrator())
			{
				Logger.WriteLine("Autostart registration requires administrator rights.");
				Environment.ExitCode = 5;
				Logger.Close();
			}
			else
			{
				Environment.ExitCode = ((!Startup.ScheduleUserTaskOnly()) ? 1 : 0);
				Logger.Close();
			}
			return;
		case "--official-isolate":
		case "--official-restore":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (!ProcessHelper.IsUserAdministrator())
			{
				Logger.WriteLine("Official console action requires administrator rights.");
				Environment.ExitCode = 5;
				Logger.Close();
			}
			else
			{
				OfficialConsoleIsolation.OperationResult operationResult2 = ((text == "--official-isolate") ? OfficialConsoleIsolation.Enable() : OfficialConsoleIsolation.Restore());
				Logger.WriteLine(operationResult2.Message);
				Environment.ExitCode = ((!operationResult2.Success) ? 1 : 0);
				Logger.Close();
			}
			return;
		}
		if (text == "--settings-backup-export")
		{
			// 2026-10-09（exportperm）：无界面的设置备份导出，供脚本与验证使用。
			// 实现与【设置】→「设置备份」→「导出设置…」是同一份代码（SettingsBackup.Export）。
			string backupPath = ((args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])) ? Path.GetFullPath(args[1]) : Path.Combine(SettingsBackup.ResolveDefaultDirectory(), SettingsBackup.BuildFileName(DateTime.Now)));
			try
			{
				SettingsBackupExportResult exportResult = SettingsBackup.Export(backupPath, DateTime.Now);
				Logger.WriteLine($"Settings backup exported (headless): {exportResult.ZipPath} ({exportResult.SizeBytes} bytes, {exportResult.Included.Count} files, skipped={exportResult.Skipped.Count})");
				foreach (SettingsBackupEntry entry in exportResult.Included)
				{
					Logger.WriteLine("  + " + entry.EntryName + " (" + entry.Bytes + " bytes)");
				}
				Environment.ExitCode = 0;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Settings backup export failed (headless): " + ex);
				Environment.ExitCode = 1;
			}
			Logger.Close();
			return;
		}
		if (text == "--ui-audit")
		{
			UiAuditMode = true;
			Environment.SetEnvironmentVariable("LMECHREVO_LOG_LEVEL", "all");
			Environment.ExitCode = UiAuditRunner.Run((args.Length > 1) ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "ui-audit"));
			Logger.Close();
			return;
		}
		// ⭐⭐ 2026-10-16（0.291.9 · 宝龙达适配）：⭐ 传感器能力转储（⭐ 落实"LHM 能读温度就优先用它"✅）
		//    ⭐ 用法：⭐ `L-Mechrevo.exe --sensor-dump [输出目录]` ✅
		// ⭐⭐ 2026-10-16（宝龙达 · 风扇前置）：⭐ 只读观察两个风扇通道 ＋ ⭐ CPU/GPU 温度
		//    ⭐ 用法：⭐ `L-Mechrevo.exe --fan-watch [秒数] [输出目录]` ✅
		//    ⭐ 目的：⭐ 确认"⭐ 哪个通道是 CPU、⭐ 哪个是 GPU"⭐（⭐ 搞反会导致散热逻辑全错 ⚠️）
		if (text == "--fan-watch")
		{
			int seconds = 60;
			string watchOut = null;
			if (args.Length > 1 && int.TryParse(args[1], out int parsedSeconds))
			{
				seconds = parsedSeconds;
				if (args.Length > 2)
				{
					watchOut = Path.GetFullPath(args[2]);
				}
			}
			else if (args.Length > 1)
			{
				watchOut = Path.GetFullPath(args[1]);
			}
			Environment.ExitCode = MechrevoLite.Hardware.FanWatchRunner.Run(seconds, watchOut);
			Logger.Close();
			return;
		}		if (text == "--sensor-dump")
		{
			Environment.ExitCode = MechrevoLite.Hardware.SensorDumpRunner.Run((args.Length > 1) ? Path.GetFullPath(args[1]) : null);
			Logger.Close();
			return;
		}
		switch (text)
		{
		case "--lctest":
		case "selftest":
		case "--selftest":
		case "customtest":
		case "--rgbtest":
		case "colortest":
		case "rgbtest":
		case "lctest":
		case "--customtest":
		case "--colortest":
		case "--verify-functions":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			Logger.WriteLine("The in-process hardware test command '" + text + "' is disabled. Rebuild with -p:DefineConstants=HARDWARE_DIAGNOSTICS to enable it, or use the isolated test project with explicit fixtures.");
			Environment.ExitCode = 2;
			Logger.Close();
			return;
		}
		if (text == "charge")
		{
			Environment.ExitCode = ((!ApplyBatteryLimitAtBootAsync().GetAwaiter().GetResult()) ? 1 : 0);
			AppConfig.Flush();
			Logger.Close();
			return;
		}
		if (text == "--wujie-fan-sample")
		{
			// 2026-10-22（无界风扇表会错乱，怎么知道 EC 的温度取值）：只读采样，反推 EC 自动策略曲线。
			int sampleSeconds = 60;
			if (args.Length > 1 && int.TryParse(args[1], out int parsedSeconds))
			{
				sampleSeconds = Math.Clamp(parsedSeconds, 5, 3600);
			}
			string samplePath = (args.Length > 2) ? args[2] : "";
			string sampleReport = MechrevoLite.Hardware.WujieFanMapProbe.Run(sampleSeconds, samplePath);
			Console.WriteLine(sampleReport);
			Logger.WriteLine("[wujie-fan-map] " + sampleReport.Replace("\r\n", " | "));
			Logger.Close();
			return;
		}
		if (text == "batteryfix")
		{
			// 2026-10-22（无界电池电压修复）：SYSTEM 开机任务入口 —— 常驻保持 EC 目标，防止重启/断电后弹回。
			Environment.ExitCode = RunBatteryFixDaemon();
			Logger.Close();
			return;
		}
		string text2 = AppConfig.GetString("language");
		try
		{
			if (text2 != null && text2.Length > 0)
			{
				Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(text2);
			}
			else
			{
				CultureInfo cultureInfo = CultureInfo.CurrentUICulture;
				if (cultureInfo.ToString() == "kr")
				{
					cultureInfo = CultureInfo.GetCultureInfo("ko");
				}
				Thread.CurrentThread.CurrentUICulture = cultureInfo;
			}
		}
		catch
		{
			Logger.WriteLine("Unknown Language: " + text2);
		}
		SettingsDialog.SyncOemRegistryLanguage(UiLanguage.Normalize(AppConfig.GetString("language"), Thread.CurrentThread.CurrentUICulture.Name));
		Logger.WriteLine("----------------------");
		Logger.WriteLine("App launched: " + Assembly.GetExecutingAssembly().GetName().Version.ToString() + CultureInfo.CurrentUICulture?.ToString() + (ProcessHelper.IsUserAdministrator() ? "." : ""));
		// 2026-10-08（QA 开关）：把「UI 是否处于 QA 显示模式」打一行到日志（默认 OFF 也打），
		// 供验收时从日志确认开关当前是否生效。只写日志，不改任何状态。
		UiQaShowAll.LogStartupState();
		// 2026-10-09（实验开关，用户原话「c」）：把「是否已按 30 系样式渲染 UI」打一行到日志
		// （默认 OFF 也打），供验收时从日志确认开关当前是否生效。只写日志，不改任何状态。
		Force30Ui.LogStartupState();
		// 2026-10-09（实验开关，用户原话「先在我这台机器上无条件显示30系性能模式 我看看40A GCU
		// 能不能正确切30系的性能方式」）：完整 30 系性能模式开关的状态行（默认 OFF 也打）。
		// ⚠️ 它和上面的 Force30Ui **不是**同一把开关：这一把会让 MechrevoService.IsGcu30 判真 ⇒
		// 切模式真的下发 ProfileIndex（行为改变，用于本机验证 40A GCU 认不认该字段）。
		// 只写日志，不改任何状态。
		MechrevoLite.Hardware.Force30Perf.LogStartupState();
		if (!ProcessHelper.CheckAlreadyRunning(text))
		{
			Logger.Close();
			return;
		}
		// 2026-10-09（灯带 bisect 开关）：启动时打一条独立日志（Logger.WriteLine，不走去重），
		// 把「补 ID / 灯带去重」两个开关的最终生效值与来源写清楚，便于机主替换后一眼确认；
		// 只写日志，不改任何状态。放在单实例判定之后，避免第二个实例也打一遍。
		LightCompatibilitySwitches.LogStartupState();
		// 2026-10-09（kbdctrl，用户原话「只搁置用户模式」⇒「给 LM 接通键盘灯」开工）：键盘灯
		// Keyboard/Ctrl 下发开关的状态行（**默认 OFF 也打**），便于机主替换后一眼确认"现在会不会发
		// Keyboard/Ctrl 帧"。它只放宽键盘灯这一条判定，灯带/用户模式/其它路径一概不受影响；只写日志。
		MechrevoLite.Hardware.KeyboardCtrlPublishSwitch.LogStartupState();
		// 2026-10-09（kbdctrl②，用户更正「【7 分区】的蛟龙17也是单分区的」）：块数是**可选的诊断注入**，
		// 默认 official=7 段（与官方实测帧逐字节一致）；这行只把"现在按几段发"写进日志，不改任何状态。
		MechrevoLite.Hardware.KeyboardCtrlBlockCountSwitch.LogStartupState();
		// 2026-10-14（用户原话「要在 LM 里加一条【只读日志】打出机型码字段」）：启动时打一次机型码字段
		// （Win32_BaseBoard.Product/Manufacturer/Version、Win32_ComputerSystem.Model/Manufacturer/
		// SystemFamily/SystemSKUNumber、Win32_BIOS.SMBIOSBIOSVersion/Version、
		// Win32_ComputerSystemProduct.Name/Version，以及**复用的** BIOS 项目码 ModelIdentity.BiosProjectId）。
		// ⚠️ 走 Logger.WriteLine（自动分级）：一切正常时是 Info —— 默认级别「仅错误」下**不落盘**；
		// 读不到时会带「不可用/失败」字样自动升级为 Error，默认级别下照样看得见。
		// ⭐ 默认级别下要收这些字段请用诊断包 —— 同一份采集也写进 系统信息.txt 的 [机型码字段（只读）] 一节。
		// ⛔ 严格只读（只查 WMI + 既有 EC 读通道），⛔ 不写注册表/EC/ACPI/NVRAM、不改任何状态、不读序列号。
		MachineCodeProbe.LogStartupState();
		// 2026-10-09（lightsplit，用户原话「额外灯光项应该细分」）：把旧键 light_extra_ui 一次性迁移成
		// 按行两把键（light_strip_ui / light_logo_ui）—— 只在两个新键都缺失时做，做完写 light_extra_ui_v2=1，
		// 旧键保留不删；只需一次且幂等（见 LightExtraRows.MigrateLegacyOnce）。放在单实例判定之后、
		// 创建 SettingsForm 之前，保证第一个界面读到的就是新口径。只写 config，不碰硬件/EC/下发。
		LightExtraRows.MigrateLegacyOnce();
		settingsForm = new SettingsForm();
		settingsForm.AllowBackgroundImage = true;
		modeControl = new ModeControl();
		gpuControl = settingsForm.gpuControl;
		toast = new ToastForm();
		hardwareOverlay = new HardwareOverlay();
		Application.ApplicationExit += OnExit;
		ProcessHelper.SetPriority();
		OfficialConsoleIsolation.StartGuardIfNeeded();
		CleanupLegacyFiles();
		int value = AppConfig.Get("start_count") + 1;
		AppConfig.Set("start_count", value);
		Logger.WriteLine("Start Count: " + value);
		// 2026-10-12（用户原话「我发现虚拟屏幕没有关掉,我开了息屏恢复以后」）：启动后台清一次虚拟显示屏残留。
		// 崩溃 / 被强杀（部署脚本 Process.Kill()、任务管理器结束任务）时 OnExit 根本不会执行，
		// usbmmidd 虚拟屏就一直挂在系统里（用户在「设置 → 屏幕 → 多显示器」看到多出来的显示器）。
		// 只关本程序自己的 usbmmidd 虚拟屏：⛔ 不切显示拓扑、⛔ 不卸驱动/不删设备节点、⛔ 不阻塞启动。
		Task.Run(delegate
		{
			VirtualDisplayBlank.CleanupResidualOnStartup();
		});
		acpi = new AsusACPI();
		hw = new MechrevoHw();
		MechrevoLite.Hardware.GcuIdentity.Prewarm();
		service = new MechrevoService(hw);
		PerfModeService.Create(service, hw);
		hw.BatteryProtectionChanged += () =>
		{
			try
			{
				if (settingsForm != null && !settingsForm.IsDisposed)
				{
					settingsForm.BeginInvoke(settingsForm.RefreshBatteryModeButtons);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Battery protection UI refresh failed: " + ex.Message);
			}
			// 2026-10-10（与 GCU 的次序）：GCU 每次启动会写官方档 Enable BatteryProtection2（EC 0x07A6），
			// 比 LM 晚约 3s，可能顶掉 LM 刚写的限充。本事件 = LM 收到 GCU 上报的 System/BatteryProtection
			// （HealthProtectionStatus），发生在 GCU 自己写完 0x07A6 之后 ⇒ 可靠地"跟在 GCU 动作之后"重放一次。
			// 只在「开关记忆为开 + 档位非长效 + EC 回读确实不一致」时才写 EC；内部 Task.Run，不阻塞 MQTT 线程。
			try
			{
				MechrevoLite.Battery.BatteryControl.ReplayChargeLimitAfterExternalWriter("GCU 上报电池保护状态（启动/档位变更）");
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Charge limit replay failed: " + ex2.Message);
			}
		};
		hw.StateChanged += OnHardwareStateChanged;
		hw.CapabilitiesChanged += () =>
		{
			try
			{
				if (settingsForm != null && !settingsForm.IsDisposed)
				{
					_capabilitiesUiRefresh.Request((Action action) =>
					{
						settingsForm.BeginInvoke(action);
					}, settingsForm.RefreshDeviceCapabilities);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Capability UI refresh failed: " + ex.Message);
			}
		};
		settingsForm.RefreshDeviceCapabilities();
		rgb = new KeyboardRgb();
		rgb.LoadConfig();
		ble = new WaterCoolerBle();
		hw.ConnectionReady += (int generation) =>
		{
			RestoreAfterHardwareConnectionAsync(generation);
		};
		hw.ConnectionReady += (int _) =>
		{
			try
			{
				if (settingsForm != null && !settingsForm.IsDisposed)
				{
					settingsForm.RefreshGcuStatus();
					settingsForm.RefreshDeviceCapabilities();
					SettingsDialog.PublishGcuLanguageAsync(UiLanguage.Normalize(AppConfig.GetString("language"), Thread.CurrentThread.CurrentUICulture.Name));
					// 2026-10-09（warmupreload）：首次装 GCU 有"预热启动"期 —— 上面这两句刷新跑在预热完成**之前**时，
					// ItemSupport 能力位/机型身份还没上报，电池区与快捷开关族会被判"不支持"而隐藏，且不重启不回来。
					// 这里挂一次性的"连上 GCU 满 5 秒后软重载"（GcuWarmupReload 内部自会保证：只连上后计时、
					// 每台机器只做一次、用户正在交互就延后、绝不轮询、全程只读）。不放它进上面的 if —— 它自己有门户。
					GcuWarmupReload.TryTrigger(settingsForm);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("GCU status refresh failed: " + ex.Message);
			}
		};
		StartTelemetryRecoveryMonitor();
		Task.Run(async () =>
		{
			try
			{
				HardwareControl.AttachMechrevoHw(hw);
				service.ModeChanged += (int m) =>
				{
					try
					{
						if (settingsForm != null && !settingsForm.IsDisposed)
						{
							settingsForm.BeginInvoke(() =>
							{
								settingsForm.ShowMode(MechrevoService.ToVisualMode(m));
								settingsForm.SetContextMenu();
							});
						}
					}
					catch (Exception ex2)
					{
						Logger.WriteLine("Mode UI refresh failed: " + ex2.Message);
					}
				};
				service.GpuModeChanged += (int m) =>
				{
					try
					{
						int logicalMode = m;
						AppConfig.Set("gpu_mode", logicalMode);
						// 2026-10-05：自动切换显卡模式功能已删除，清除老配置键，避免幽灵行为
						AppConfig.Remove("gpu_auto");
						if (settingsForm != null && !settingsForm.IsDisposed)
						{
							settingsForm.BeginInvoke(() =>
							{
								settingsForm.VisualiseGPUMode(logicalMode);
								settingsForm.SetContextMenu();
							});
						}
					}
					catch (Exception ex2)
					{
						Logger.WriteLine("GPU UI refresh failed: " + ex2.Message);
					}
				};
				Logger.WriteLine("MechrevoHw connected: " + await hw.ConnectAsync());
			}
			catch (Exception ex)
			{
				Logger.WriteLine("MechrevoHw connect fail: " + ex.Message);
			}
		});
		HardwareControl.RecreateGpuControl();
		if (!MqttSecurity.HasInboundBlockRule())
		{
			Logger.WriteLine(MqttSecurity.MissingRuleWarning());
		}
		WM_TASKBARCREATED = NativeMethods.RegisterWindowMessage("TaskbarCreated");
		// 2026-10-09（批次3 #10「托盘 NotifyIcon 懒加载」）：⭐ 托盘图标与右键菜单**不再**在启动路径上构建
		// （本机实测 `new NotifyIcon(...)` + `SetContextMenu()` 约占 140~180ms），改为**首次真正需要时**构建：
		//   · 主窗口就绪后的第一次 Application.Idle（既有 deferredInitialization，见下方）⇒ 正常启动下图标照旧出现；
		//   · 单实例激活气泡（ShowDuplicateInstanceNotice）要先确保托盘存在再弹（见该方法）；
		//   · WM_TASKBARCREATED（资源管理器重启）⇒ 需要就建、已建就重新 Visible；
		//   · 开机自启注册失败提示气泡同理。
		// ⚠️ 行为不变：图标该出现时还出现（可见性、5 秒重挂、点击/移动、右键菜单项、退出、气泡全照旧），
		// 只是把这段构建从"窗体首帧之前"挪到"首帧之后"。⛔ 不改任何图标内容/菜单项/事件。
		Logger.WriteLine($"Tray Icon: 延迟到首次需要时构建（lazy） | {WM_TASKBARCREATED}");
		Startup.AutostartFailureSink = (string message) =>
		{
			Logger.WriteLine("Autostart registration failed: " + message);
			if (settingsForm.IsHandleCreated)
			{
				settingsForm.BeginInvoke(() =>
				{
					// 2026-10-09（批次3 #10）：气泡要托盘在场 ⇒ 弹之前先确保托盘已建好（首次需要时构建）。
					EnsureTrayIcon()?.ShowBalloonTip(5000, "L-Mechrevo", message, ToolTipIcon.Warning);
				});
			}
		};
		powerSettleTimer.Elapsed += (object? s, ElapsedEventArgs e) =>
		{
			OnSystemEvent(() =>
			{
				OnPowerSettled(s, e);
			});
		};
		SystemEvents.PowerModeChanged += (object s, PowerModeChangedEventArgs e) =>
		{
			OnSystemEvent(() =>
			{
				SystemEvents_PowerModeChanged(s, e);
			});
		};
		SystemEvents.UserPreferenceChanged += (object s, UserPreferenceChangedEventArgs e) =>
		{
			OnSystemEvent(() =>
			{
				SystemEvents_UserPreferenceChanged(s, e);
			});
		};
		SystemEvents.SessionSwitch += (object s, SessionSwitchEventArgs e) =>
		{
			OnSystemEvent(() =>
			{
				SystemEvents_SessionSwitch(s, e);
			});
		};
		SystemEvents.SessionEnding += (object s, SessionEndingEventArgs e) =>
		{
			OnSystemEvent(() =>
			{
				SystemEvents_SessionEnding(s, e);
			});
		};
		unRegPowerNotify = NativeMethods.RegisterPowerSettingNotification(settingsForm.Handle, NativeMethods.PowerSettingGuid.ConsoleDisplayState, 0u);
		unRegPowerNotifyLid = NativeMethods.RegisterPowerSettingNotification(settingsForm.Handle, NativeMethods.PowerSettingGuid.LIDSWITCH_STATE_CHANGE, 0u);
		unRegPowerNotifyEnergy = NativeMethods.RegisterPowerSettingNotification(settingsForm.Handle, NativeMethods.PowerSettingGuid.EnergySaverStatus, 0u);
		unRegSuspendResume = NativeMethods.RegisterSuspendResumeNotification(settingsForm.Handle, 0u);
		_showMainEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, @"Global\L-Mechrevo-ShowMain");
		// 2026-10-09（dupfeedback）：第二个实例敲的"又有人双击了"通知门。⚠️ 名称/语义只加不减：
		// 老的 ShowMain 门与它的处理完全照旧，这里只是**多等一个门**，哪一个被敲都不会改变既有行为。
		_duplicateNotifyEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, @"Global\L-Mechrevo-NotifyDuplicate");
		Thread showMainThread = new Thread(delegate ()
		{
			WaitHandle[] waitHandles = new WaitHandle[2] { _showMainEvent, _duplicateNotifyEvent };
			while (true)
			{
				try
				{
					// 0 = 第一节（双击/托盘唤起：照旧显示主窗）；1 = 第二节（第二个实例刚被挡下：只弹一次气泡）
					if (WaitHandle.WaitAny(waitHandles) == 1)
					{
						ShowDuplicateInstanceNotice();
						continue;
					}
					// （机主：休眠/睡眠唤醒后仍会自己弹主界面）
					if (IsInResumeShowSuppression())
					{
						Logger.WriteLine("唤醒抑制窗口内收到第二实例的唤起请求 ⇒ ⛔ 不显示主界面（用户此前已关闭/最小化）");
						continue;
					}
					SettingsToggle(checkForFocus: true);
				}
				catch (Exception)
				{
					break;
				}
			}
		});
		showMainThread.IsBackground = true;
		showMainThread.Start();
		Task.Run((Action)PeripheralsProvider.DetectAllAsusMice);
		PeripheralsProvider.RegisterForDeviceEvents();
		if (!startMinimized)
		{
			SettingsToggle(checkForFocus: false);
		}
		else
		{
			Logger.WriteLine("Startup task launched minimized to tray.");
		}
		EventHandler deferredInitialization = null;
		deferredInitialization = (object? _, EventArgs _) =>
		{
			Application.Idle -= deferredInitialization;
			// 2026-10-15（D，启动阶段耗时日志）：把"首帧之后的首次空闲"这一刻距进程启动的毫秒数打出来 ——
			// 这是"主界面可见"的近似时刻；配合 [settingswarm] 的三段计时，就能把"卡一会"拆到具体阶段。
			try
			{
				Logger.WriteLine($"[startup-phase] deferred init（首帧后首次空闲 / 主界面可见）: {(long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds}ms since process start");
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[startup-phase] 记录 deferred init 耗时失败（忽略）: " + ex.Message);
			}
			// 2026-10-09（批次3 #10「托盘 NotifyIcon 懒加载」）：⭐ 首帧之后的第一次空闲 = "主窗口已就绪"，
			// 这正是建托盘图标 + 右键菜单的时机（改前是在窗体首帧**之前**建，白占 140~180ms 启动时间）。
			// ⚠️ 放在最前面：与改前的相对顺序一致（改前它在 Application.Run 之前就存在了），
			// 于是即使下面的 .NET 10 提示弹了模态框，托盘也已经可用（行为不变）。
			EnsureTrayIcon();
			// 2026-10-09（net10runtime）：.NET 10 桌面运行时的**一次性**提示（只在标准位置都查不到时弹，
			// 且先落 config 键再弹 ⇒ 只弹一次）。放在最前面且是同步调用 ⇒ 它开着的时候下面那段预热不会跑。
			CheckNetDesktop10RuntimeOnce();
			// 2026-10-09（settingswarm）：把设置对话框外壳的预热排在**首帧之后**、且它自己还会再
			// BeginInvoke 一次 —— 优先级低于主窗口首帧，冷启动到主窗口的数字一字不碰；用户点「设置」时
			// 只剩 Show() + 刷新数据（本机实测首次 5347ms → 见报告）。失败/没做完自动退回按需构建。
			// ⭐⭐ 2026-10-18（机主：取消「启动设置预热」）：⭐ 启动阶段**不再**隐形 Show 整个设置窗；
			//   改成首次真正打开设置时才构建（按需）⇒ 启动少付这一大段 ✅
			Logger.WriteLine("[init] 启动跳过设置预热（懒加载）");
			if (!startMinimized)
			{
				settingsForm.ShowFirstRunGuideIfNeeded();
				// 2026-10-05「核显模式显示策略」⑤：老用户看不到首次引导，单独问一次（igpu_only_asked 门控）
				settingsForm.ShowIgpuOnlyGuideIfNeeded();
			}
			if (startMinimized)
			{
				MemoryHelper.TrimLater(TimeSpan.FromSeconds(45L), () =>
				{
					SettingsForm settingsForm = Program.settingsForm;
					return settingsForm != null && !settingsForm.IsDisposed && !settingsForm.Visible;
				});
			}
			settingsForm.InitAura();
			SetAutoModes(powerChanged: false, init: true);
		// 2026-10-22（机主：无界开机且在自定义上再启用）：开机后按已保存点位自动启动 EC 曲线
		WujieFanCurveAutoApply.Schedule();
			// 2026-10-05 percore-product: 每核 CO「开机自动应用」的唯一调度点（只跑一次、不受节流影响）。
			ScheduleCpuCoAutoApply();
			// 2026-10-10（用户反馈 ③）：显卡超频的「上次非正常关机就自动关闭」—— 开机后独立调度一次；
			// 不动 cpu_co 的那套调度/键/判定，只多一个自己的闸门。
			ScheduleGpuOcShutdownGuard();
			StartLightingIdleMonitor();
			if (AppConfig.IsOverlay())
			{
				hardwareOverlay?.StartOverlay();
			}
			GpuRestartVerifier.RunAfterBootAsync(TimeSpan.FromSeconds(8L));
			// 启动后空闲再跑（单个后台任务、串行、 不阻塞 UI、 不挂在设置窗打开路径）
			ScheduleIdleBackendWarmup();
		};
		Application.Idle += deferredInitialization;
		switch (text)
		{
		case "uv":
			Startup.ReScheduleAdmin();
			modeControl.SetRyzen();
			break;
		case "colors":
			Logger.WriteLine("Ignored legacy 'colors' startup action.");
			break;
		default:
			Task.Run((Action)Startup.StartupCheck);
			break;
		case "cpu":
		case "gpu":
		case "services":
			break;
		}
		Task.Run(() => RestoreLightingWithRetryAsync());
		// 2026-10-22（耀世开机灯带/Logo 亮一会儿自动灭）：GCU 官方启动任务会在开机后改写灯带/Logo 状态，
		// LM 启动时的一次恢复可能被它盖掉 ⇒ 20s/45s 各补一次（仅在用户有已保存灯效意图时；开关为关就恢复成关）。
		_ = Task.Run(async delegate
		{
			foreach (int delaySeconds in new int[2] { 20, 45 })
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(continueOnCapturedContext: false);
					if (Volatile.Read(in _exitStarted) != 0)
					{
						return;
					}
					if (!LightingSettingsStore.HasUserLightingIntent())
					{
						return;
					}
					Logger.WriteLine("[light] 开机灯效补恢复（t=" + delaySeconds + "s，抵消 GCU 启动任务改写）");
					await RestoreLightingWithRetryAsync(force: true).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[light] 开机灯效补恢复失败（已吞掉）：" + ex.Message);
				}
			}
		});
		Task.Run(() => HardwareControl.EnableLocalMonitoring());
		StartSilentUpdateCheck();
		UsageTelemetry.Start();
		Application.Run();
	}

	internal static bool IsStartupLaunch(string? action)
	{
		return action?.Equals("startup", StringComparison.OrdinalIgnoreCase) ?? false;
	}
	// <para> 背景：Windows 在休眠唤醒（S4）/登录恢复时可能通过「登录触发器 / 重启应用」把 LM 再拉起一次
	private static long _resumeShowSuppressUntil;

	/// <summary>⭐ 唤醒后抑制第二实例唤起主窗的时长（秒）。考虑到登录恢复可能晚几秒到几十秒。</summary>
	private const int ResumeShowSuppressSeconds = 90;

	internal static void MarkResumeShowSuppression(string why)
	{
		Interlocked.Exchange(ref _resumeShowSuppressUntil, Environment.TickCount64 + ResumeShowSuppressSeconds * 1000L);
		Logger.WriteLine("[power] 唤醒后 " + ResumeShowSuppressSeconds + " 秒内抑制「第二实例唤起主界面」（来源=" + why + "）");
	}

	private static bool IsInResumeShowSuppression()
	{
		long until = Interlocked.Read(ref _resumeShowSuppressUntil);
		return until != 0L && Environment.TickCount64 < until;
	}

	/// <summary>
	/// 2026-10-09（批次3 #10「托盘 NotifyIcon 懒加载」）：⭐ 托盘的**唯一**构建处 —— 首次调用时建图标、
	/// 挂事件、装右键菜单（<c>settingsForm.SetContextMenu()</c>，与改动前同一处构建）并起既有的 5 秒"重挂一次"计时器；
	/// 之后每次调用直接返回同一个实例（幂等）。⚠️ 必须在 UI 线程调用（NotifyIcon 依赖消息循环）。
	/// <para>
	/// ⛔ 行为不变：图标 Text/Icon/Visible、点击与移动事件、右键菜单内容、气泡、退出全部照旧；
	/// 只是把这段构建从"窗体首帧之前"推迟到"首次真正需要时"（正常启动＝首帧后的第一次 Application.Idle）。
	/// </para>
	/// </summary>
	/// <returns>建好（或已存在）的托盘图标；构建确实不成功时返回 null（调用方按退化路径只记日志）。</returns>
	internal static NotifyIcon EnsureTrayIcon()
	{
		NotifyIcon existing = trayIcon;
		if (existing != null)
		{
			return existing;
		}
		try
		{
			Icon icon;
			try
			{
				icon = new Icon(Path.Combine(AppContext.BaseDirectory, "favicon.ico"));
			}
			catch (Exception ex)
			{
				// ⚠️ 图标文件缺失时用系统图标兜底：懒加载后这段代码跑在消息循环里，抛出会直接崩应用
				//（改前跑在 Main 里，同样会崩；这里只是不再让"图标文件缺失"变成启动崩溃）。
				Logger.WriteLine("Tray icon file load failed, using system icon: " + ex.Message);
				icon = SystemIcons.Application;
			}
			NotifyIcon created = new NotifyIcon
			{
				Text = "L-Mechrevo",
				Icon = icon,
				Visible = true
			};
			// ⚠️ 先登记再装菜单：SetContextMenu 用 ExistingTrayIcon 判定"托盘在不在"，顺序反了菜单就装不上。
			trayIcon = created;
			ApplyPendingTrayTip(created);
			created.MouseClick += TrayIcon_MouseClick;
			created.MouseMove += TrayIcon_MouseMove;
			try
			{
				settingsForm?.SetContextMenu();
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Tray context menu build failed: " + ex2.Message);
			}
			StartTrayRetryTimer(created);
			Logger.WriteLine($"Tray Icon: {created.Visible} (lazy-built) | {WM_TASKBARCREATED}");
			return created;
		}
		catch (Exception ex3)
		{
			Logger.WriteLine("Tray icon creation failed: " + ex3.Message);
			return null;
		}
	}

	/// <summary>
	/// 2026-10-09（批次3 #10）：既有那条"建完 5 秒把 Visible 关一下再打开"的重试计时器（逐字照旧），
	/// 只是改成**跟随托盘创建**启动（改前是随手写死在启动路径上）。⛔ 语义不变：仍然是 5 秒、一次、随后自毁。
	/// </summary>
	private static void StartTrayRetryTimer(NotifyIcon icon)
	{
		_trayRetryTimer = new System.Windows.Forms.Timer
		{
			Interval = 5000
		};
		_trayRetryTimer.Tick += (object? _, EventArgs _) =>
		{
			_trayRetryTimer?.Stop();
			_trayRetryTimer?.Dispose();
			_trayRetryTimer = null;
			icon.Visible = false;
			icon.Visible = true;
		};
		_trayRetryTimer.Start();
	}

	/// <summary>
	/// 2026-10-09（批次3 #10）：**只读**拿托盘实例（不存在就返回 null，⛔ 绝不顺手创建）。
	/// 给"设个文本/换张图标"这类**不该**逼出托盘的地方用（例如设置窗的主题刷新、右键菜单挂载点）。
	/// </summary>
	internal static NotifyIcon ExistingTrayIcon => trayIcon;

	/// <summary>
	/// 2026-10-09（批次3 #10）：托盘提示文本（设置窗的状态刷新会调它）—— 托盘还没建就**只记下**，
	/// 等 <see cref="EnsureTrayIcon" /> 建好时补上；⛔ 不会为了设文本去创建托盘。
	/// </summary>
	internal static void SetTrayTip(string tip)
	{
		_pendingTrayTip = tip;
		NotifyIcon icon = trayIcon;
		if (icon != null && !string.IsNullOrEmpty(tip) && !string.Equals(icon.Text, tip, StringComparison.Ordinal))
		{
			icon.Text = tip;
		}
	}

	private static void ApplyPendingTrayTip(NotifyIcon icon)
	{
		string tip = _pendingTrayTip;
		if (!string.IsNullOrEmpty(tip) && !string.Equals(icon.Text, tip, StringComparison.Ordinal))
		{
			icon.Text = tip;
		}
	}

	/// <summary>
	/// 2026-10-09（批次3 #10）：资源管理器重启（<c>WM_TASKBARCREATED</c>）后把托盘图标挂回去 ——
	/// 已建就重新 <c>Visible</c>（逐字照旧）；还没建就建一个（说明这一刻托盘确实需要它了）。
	/// </summary>
	internal static void OnTaskbarCreated()
	{
		Logger.WriteLine("Taskbar created, re-creating tray icon");
		NotifyIcon icon = trayIcon ?? EnsureTrayIcon();
		if (icon != null)
		{
			icon.Visible = true;
		}
	}

	/// <summary>
	/// 2026-10-09（批次3 #10）：退出路径把托盘收起来 —— ⛔ **绝不**为了隐藏而创建托盘（没建过就什么都不用做）。
	/// </summary>
	internal static void HideTrayIconForExit()
	{
		NotifyIcon icon = trayIcon;
		if (icon != null)
		{
			icon.Visible = false;
		}
	}

	/// <summary>
	/// 2026-10-09（dupfeedback，用户原话「双击没反应」）：第二个实例被单实例门挡下时，由**已有那个实例**
	/// 用托盘图标弹一次气泡 —— 说明"已经在运行了、窗口已经给你打开"，免得用户以为双击没生效。
	/// ⚠️ 托盘不可用（托盘图标没了 / 未创建）时不报错，只记一行日志（退化路径）。
	/// ⚠️ 只读 + 只弹气泡：不碰任何状态、不改窗口行为；真失败也只记日志。
	/// </summary>
	private static void ShowDuplicateInstanceNotice()
	{
		try
		{
			Logger.WriteLine("Duplicate launch detected; showing the tray notice.");
			// 2026-10-09（批次3 #10）：⭐ 气泡需要托盘在场 ⇒ 先确保托盘已建好（首次需要时构建）再弹；
			// 建不出来（拿不到图标文件等）时仍走既有的"退化路径"：只记一行日志，不报错。
			NotifyIcon icon = EnsureTrayIcon();
			if (icon == null || !icon.Visible)
			{
				Logger.WriteLine("Duplicate launch notice skipped: tray icon unavailable.");
				return;
			}
			icon.ShowBalloonTip(5000, Properties.Strings.DuplicateInstanceTitle, Properties.Strings.DuplicateInstanceNotice, ToolTipIcon.Info);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Duplicate launch notice failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-09（net10runtime）：.NET 10 桌面运行时是否在**标准位置**装过。
	/// 判据与 <c>installer-src\install_script.iss</c> 的 <c>IsNetDesktop10Installed()</c> 一致：
	/// ① 注册表 <c>HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App</c>；
	/// ② <c>{ProgramFiles}\dotnet\shared\Microsoft.WindowsDesktop.App</c>；
	/// ③ <c>{ProgramFilesX86}\dotnet\shared\Microsoft.WindowsDesktop.App</c>（＝ Inno 的 <c>{commonpf}</c>）。
	/// ⚠️ 只读注册表/目录存在性，不写任何东西。
	/// </summary>
	internal static bool IsNetDesktop10Installed()
	{
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
			if (key != null)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("net10 runtime check: sharedfx registry read failed: " + ex.Message);
		}
		string[] roots = new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
		};
		foreach (string root in roots)
		{
			try
			{
				if (!string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, "dotnet", "shared", "Microsoft.WindowsDesktop.App")))
				{
					return true;
				}
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("net10 runtime check: probe failed for '" + root + "': " + ex2.Message);
			}
		}
		return false;
	}

	/// <summary>
	/// 2026-10-09（net10runtime）：**只在标准位置都找不到 .NET 10 桌面运行时时**提示一次（独立 config 键
	/// <c>net_desktop10_notice_shown</c>，先写键再弹窗 ⇒ 弹窗抛异常也不会重复弹）。
	/// <para>⚠️ 局限（如实写清）：本方法是**进程内**检查，而"缺 Microsoft.WindowsDesktop.App 时双击毫无反应"
	/// 那种场景下本进程根本起不来 ⇒ 这段代码不会执行，覆盖不到；更靠谱的兜底在安装器/启动器侧
	/// （<c>install_script.iss</c> 已有 IsNetDesktop10Installed + 自动下载安装）。</para>
	/// <para>⚠️ 因此本提示的真实含义是"标准位置没检测到 ⇒ 这次多半用的是非标准位置（用户级/私有目录）的
	/// 运行时"，文案按这个口径写，不说"你不能运行"。</para>
	/// </summary>
	private static void CheckNetDesktop10RuntimeOnce()
	{
		try
		{
			string loaded = typeof(Form).Assembly.Location ?? "-";
			if (IsNetDesktop10Installed())
			{
				Logger.WriteLine("net10 runtime check: Microsoft.WindowsDesktop.App found at a standard location (loaded desktop runtime: " + loaded + ")");
				return;
			}
			Logger.WriteLine("net10 runtime check: Microsoft.WindowsDesktop.App NOT found at the standard locations (registry sharedfx / %ProgramFiles%\\dotnet\\shared); loaded desktop runtime: " + loaded);
			if (AppConfig.Is("net_desktop10_notice_shown"))
			{
				return;
			}
			AppConfig.Set("net_desktop10_notice_shown", 1);
			AppConfig.Flush();
			MessageBox.Show(Properties.Strings.NetDesktopRuntimeNotice, Properties.Strings.NetDesktopRuntimeNoticeTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("net10 runtime check failed (ignored): " + ex.Message);
		}
	}

	private static void StartSilentUpdateCheck()
	{
		if (UiAuditMode || !UpdateChecker.AutoCheckEnabled || UpdateForm.TemporarilyHidden)
		{
			return;
		}
		Task.Run(async () =>
		{
			_ = 1;
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(8L));
				UpdateInfo info = await UpdateChecker.CheckAsync();
				if ((object)info != null && info.UpdateAvailable)
				{
					SettingsForm form = settingsForm;
					if (form != null && !form.IsDisposed)
					{
						form.BeginInvoke(() =>
						{
							form.MarkUpdateAvailable(info);
						});
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("静默更新检测失败：" + ex.Message);
			}
		});
	}

	internal static void RequestShutdownForUpdate()
	{
		try
		{
			AsusLampArray.Release();
			settingsForm?.Close();
			// 2026-10-09（批次3 #10）：退出时收起托盘 ⛔ 不因"隐藏"而创建托盘。
			HideTrayIconForExit();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新退出清理失败：" + ex.Message);
		}
		Application.Exit();
	}

	private static async Task RestoreAfterHardwareConnectionAsync(int generation)
	{
		await _hardwareRecoveryLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (Volatile.Read(in _exitStarted) != 0 || generation <= Volatile.Read(in _lastRecoveredConnectionGeneration))
			{
				return;
			}
			Interlocked.Increment(ref _lightingRestoreRequestId);
			Logger.WriteLine($"硬件连接恢复开始: generation={generation}");
			long refreshStarted = Environment.TickCount64;
			await service.RefreshAll().ConfigureAwait(continueOnCapturedContext: false);
			await Task.Delay(800).ConfigureAwait(continueOnCapturedContext: false);
			if (!hw.HasTelemetrySince(refreshStarted))
			{
				Logger.WriteLine("硬件连接已建立但尚无温度遥测，补发全量状态请求");
				await service.RefreshAll().ConfigureAwait(continueOnCapturedContext: false);
				await Task.Delay(1200).ConfigureAwait(continueOnCapturedContext: false);
			}
			bool value = await RestoreLightingForGenerationAsync(generation, force: false).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteLine($"灯效连接恢复结果: generation={generation}, restored={value}");
			int pendingPerformanceMode = Interlocked.Exchange(ref _pendingStartupPerformanceMode, -1);
			try
			{
				SettingsForm settingsForm = Program.settingsForm;
				if (settingsForm != null && !settingsForm.IsDisposed && Program.settingsForm.IsHandleCreated && Program.settingsForm.InvokeRequired)
				{
					Program.settingsForm.BeginInvoke(ReapplyPerformanceOnUiThread);
				}
				else
				{
					ReapplyPerformanceOnUiThread();
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Performance recovery UI dispatch failed: " + ex.Message);
			}
			service.RestoreCurrentDirectGpuOverclock();
			// 2026-10-09（默认值修正）：只有用户**明确保存过** display_effect=1 才在开机时应用。
			// 旧写法 `AppConfig.Get("display_effect") != 0` 是错的：AppConfig.Get 缺省返回 **-1**
			// （AppConfig.cs:397 `Get(string name, int empty = -1)`），键不存在时 -1 != 0 成立
			// ⇒ 全新配置开机就 SetDisplayEffectSwitch(true) + 应用「多彩」。这就是"多彩默认开"的来源。
			if (AppConfig.Exists("display_effect") && AppConfig.Is("display_effect"))
			{
				try
				{
					bool effectApplied = await service.SetDisplayEffectSwitch(true);
					// 2026-10-09（默认值修正）：档位不再默认「多彩」——只认用户存过的档位，
					// 没存过就用 GCU 回读的当前档位；都没有就只开开关、不应用任何档位。
					string effectMode = AppConfig.GetString("display_effect_mode") ?? hw.DisplayEffectMode;
					if (effectApplied && hw.DisplayEffectProfiles.TryGetValue(effectMode, out DisplayEffectProfile? effectProfile))
					{
						await service.ApplyDisplayEffectProfile(effectProfile);
					}
					_ = service.RequestDisplayEffectStatus();
					Logger.WriteLine($"色彩增强开机应用: mode={effectMode} applied={effectApplied}");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("色彩增强开机应用 fail: " + ex.Message);
				}
			}
			if (AppConfig.Is("calib_apply_on_startup") && AppConfig.Get("calib_mode", 0) > 0)
			{
				try
				{
					int calibMode = AppConfig.Get("calib_mode", 0);
					if (calibMode == 999)
					{
						// 自定义 ICC：经 Windows 颜色管理应用，不走 GCU
						string? iccName = AppConfig.GetString("calib_custom_icc");
						string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
						bool iccApplied = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));
						Logger.WriteLine($"校色开机应用(自定义 ICC): file={iccName ?? "(空)"} applied={iccApplied}");
					}
					else
					{
						// 2026-10-07 返工：不再把 50 系 4 档折成 2 档（上一轮据静态字符串扫描的折叠依据不成立）。
						// 这里只做 0..4 合法性收敛，动作词由 MechrevoService.ColorCalibrationAction 定：
						// 二分设备（40 系 + sRGB 屏，GCU 上报 1 档）发裸 COLOR_CALIBRATION_ON，
						// 多档设备（50 系 P3 屏 4 档 / 广色域屏 2 档）发 COLOR_CALIBRATION_ON_DEFAULT/_SRGB/_P3/_ADOBERGB。
						int calibApplyMode = SettingsForm.MapCalibrationMode(calibMode, 4);
						// 2026-10-10【默认启用】开机应用也算"开"：同设置页口径，
						// 前置快照须在下发之前抓（紧急关闭键 calib_assoc_gcu_icc=0 ⇒ 两个调用都立即返回 null/空结果）。
						// ⭐ 2026-10-10【范围约束】tag 只用于日志：有回读能力的机器（Gen50+已上报档位）在此直接得 null ⇒ 整体跳过。
						MechrevoLite.Display.GcuIccProfileAssociator.Snapshot calibIccBefore = await MechrevoLite.Display.GcuIccProfileAssociator.CaptureAsync("开机");
						bool calibApplied = await service.SetColorCalibration(calibApplyMode);
						Logger.WriteLine($"校色开机应用: mode={calibMode} sent={calibApplyMode} applied={calibApplied}");
						if (calibApplied && calibIccBefore != null)
						{
							// ⭐ 2026-10-10：开机路径同样只改"怎么等待"——即发即忘，⛔ 不阻塞启动流程；
							// 关联结果记在 [icc-assoc] 的"关联结果(开机)"。
							MechrevoLite.Display.GcuIccProfileAssociator.ApplyAfterCalibrationOnDetached(calibIccBefore, "开机");
						}
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("校色开机应用 fail: " + ex.Message);
				}
			}
			try
			{
				if (Program.settingsForm != null && !Program.settingsForm.IsDisposed)
				{
					Program.settingsForm.BeginInvoke(() =>
					{
						Program.settingsForm.RefreshSensors();
						Program.settingsForm.RefreshDeviceCapabilities();
						if (hw.OperatingMode >= 0)
						{
							Program.settingsForm.ShowMode(hw.GHelperMode);
						}
						gpuControl.InitGPUMode();
						Program.settingsForm.SetContextMenu();
					});
				}
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Hardware recovery UI refresh failed: " + ex2.Message);
			}
			Volatile.Write(ref _lastRecoveredConnectionGeneration, generation);
			Logger.WriteLine($"硬件连接恢复完成: generation={generation}, telemetry={hw.HasTelemetrySince(refreshStarted)}");
			void ReapplyPerformanceOnUiThread()
			{
				if (ShouldReapplyPendingPerformanceMode(pendingPerformanceMode, Modes.GetCurrent()))
				{
					Logger.WriteLine($"GCU 连接恢复后重放启动性能档: mode={pendingPerformanceMode}");
					modeControl.AutoPerformance(powerChanged: false, pendingPerformanceMode == 3);
				}
				else if (pendingPerformanceMode < 0 && hw.OperatingMode == 3)
				{
					modeControl.AutoPerformance(powerChanged: false, preserveActiveCustom: true);
				}
			}
		}
		catch (Exception ex3)
		{
			Logger.WriteLine($"硬件连接恢复失败 generation={generation}: {ex3.Message}");
		}
		finally
		{
			_hardwareRecoveryLock.Release();
		}
	}

	private static void StartTelemetryRecoveryMonitor()
	{
		_telemetryRecoveryTimer = new System.Threading.Timer((object? _) =>
		{
			_ = RecoverStaleTelemetryAsync();
		}, null, TelemetryStaleAfter, TimeSpan.FromSeconds(15L));
	}

	private static async Task RecoverStaleTelemetryAsync()
	{
		MechrevoHw mechrevoHw = hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：本函数判的是「GCU 遥测卡住了 ⇒ 补发一次全量状态请求」，
		// 补发的全是 MQTT 话题 ⇒ **真正需要 GCU**，改用 IsGcuConnected（老接口机型不走这条路）。
		if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null || !hw.IsTelemetryStale(TelemetryStaleAfter, -1L) || !(await _telemetryRecoveryLock.WaitAsync(0).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return;
		}
		try
		{
			if (Volatile.Read(in _exitStarted) != 0)
			{
				return;
			}
			mechrevoHw = hw;
			if (mechrevoHw != null && mechrevoHw.IsGcuConnected && hw.IsTelemetryStale(TelemetryStaleAfter, -1L))
			{
				long tickCount = Environment.TickCount64;
				if (!((double)(tickCount - Interlocked.Read(in _lastTelemetryRecoveryRequest)) < TelemetryRecoveryCooldown.TotalMilliseconds))
				{
					Interlocked.Exchange(ref _lastTelemetryRecoveryRequest, tickCount);
					Logger.WriteLine("温度遥测超时，补发全量状态请求");
					await service.RefreshAll().ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("温度遥测恢复请求失败: " + ex.Message);
		}
		finally
		{
			_telemetryRecoveryLock.Release();
		}
	}

	internal static async Task RestoreLightingWithRetryAsync(bool force = false)
	{
		int delayMs = 250;
		for (int attempt = 1; attempt <= 20; attempt++)
		{
			if (Volatile.Read(in _exitStarted) != 0)
			{
				break;
			}
			if (await RestoreLightingForGenerationAsync(hw?.ConnectionGeneration ?? 0, force).ConfigureAwait(continueOnCapturedContext: false))
			{
				return;
			}
			if (attempt == 1 || attempt % 5 == 0)
			{
				// 2026-10-15（C-1 第 2 步分类 ①）：等的是「GCU 的 MQTT 连上」（日志里就写着 mqtt=）⇒ 真正需要 GCU。
				Logger.WriteLine($"灯效开机恢复等待设备: attempt={attempt}, mqtt={hw?.IsGcuConnected ?? false}");
			}
			await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
			delayMs = Math.Min(delayMs * 2, 3000);
		}
		Logger.WriteLine("灯效开机恢复暂未完成，将在硬件连接成功时继续");
	}

	private static async Task<bool> RestoreLightingForGenerationAsync(int generation, bool force)
	{
		int wait = 0;
		while (wait < 30 && Volatile.Read(in _exitStarted) == 0)
		{
			switch (_lightingRestoreCoordinator.TryBegin(generation, force))
			{
			case LightingRestoreDecision.SkipCompleted:
				return true;
			case LightingRestoreDecision.DeferInFlight:
				await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
				wait++;
				continue;
			}
			bool success = false;
			try
			{
				if (!force && Volatile.Read(in _officialUiSettled) == 0)
				{
					await EnsureOfficialUiSettledAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				success = await ReconcileLightingPowerAsync(force).ConfigureAwait(continueOnCapturedContext: false);
				return success;
			}
			finally
			{
				_lightingRestoreCoordinator.Complete(generation, success);
			}
		}
		Logger.WriteLine($"灯效恢复协调器等待超时: generation={generation}, force={force}");
		return false;
	}

	private static async Task EnsureOfficialUiSettledAsync()
	{
		if (Volatile.Read(in _officialUiSettled) != 0)
		{
			return;
		}
		await _officialUiSettleLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (Volatile.Read(in _officialUiSettled) == 0)
			{
				bool value;
				try
				{
					value = await OfficialConsoleIsolation.WaitForUiSettledAsync(TimeSpan.FromSeconds(4L)).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					value = false;
					Logger.WriteLine("官方 UI 安静窗口检查失败: " + ex.Message);
				}
				Volatile.Write(ref _officialUiSettled, 1);
				Logger.WriteLine($"官方 UI 安静窗口完成: settled={value}");
			}
		}
		finally
		{
			_officialUiSettleLock.Release();
		}
	}

	private static void StartLightingIdleMonitor()
	{
		// 不再启动/保留同方厂商空闲计时器（平台判定单一来源 UsesBaolongda）
		if (!UiAuditMode && _lightingIdleTimer == null && hw?.UsesBaolongda != true)
		{
			_lightingIdleTimer = new System.Threading.Timer((object? _) =>
			{
				_ = EvaluateLightingIdleAsync();
			}, null, TimeSpan.FromSeconds(2L), TimeSpan.FromSeconds(2L));
		}
	}
	// 停掉同方/GCU 的 2s 空闲计时器；若它已把灯按"空闲"熄掉过（_lightingIdleSuspended），恢复一次并清标记 。
	internal static void StopVendorLightingIdleMonitorForBaolongda()
	{
		System.Threading.Timer vendorTimer = Interlocked.Exchange(ref _lightingIdleTimer, null);
		vendorTimer?.Dispose();
		Interlocked.Exchange(ref _lightingAutoOffEnableLogged, 0);
		if (Interlocked.Exchange(ref _lightingIdleSuspended, 0) != 0)
		{
			_ = RestoreLightingWithRetryAsync(force: true);
		}
	}

	internal static void MarkKeyboardCustomStatusBaseline()
	{
		if (hw != null)
		{
			Interlocked.Exchange(ref _keyboardStatusBaselineVersion, hw.KeyboardStatusVersion);
			Volatile.Write(ref _keyboardStatusBaselineBrightness, KeyboardRgb.MapReportedHardwareBrightness(hw.KeyboardBrightness, hw.KeyboardLight));
			Interlocked.Exchange(ref _keyboardStatusBaselineEffect, hw.KeyboardEffect ?? "");
		}
	}

	internal static void ArmKeyboardEffectProtect()
	{
		if (hw != null)
		{
			Interlocked.Exchange(ref _keyboardEffectProtectBaselineVersion, hw.KeyboardStatusVersion);
			Volatile.Write(ref _keyboardEffectProtectDeadlineMs, Environment.TickCount64 + 3000);
			Interlocked.Exchange(ref _keyboardEffectProtectArmed, 1);
		}
	}

	internal static bool ShouldReassertProtectedKeyboardCustomMode(bool armed, long statusVersion, long protectBaselineVersion, bool reportedPower, bool expired)
	{
		if ((!armed | expired) || !reportedPower)
		{
			return false;
		}
		return statusVersion > protectBaselineVersion;
	}

	internal static bool ShouldReassertKeyboardCustomMode(long statusVersion, long baselineVersion, int reportedBrightness, int baselineBrightness, string effect, string baselineEffect)
	{
		if (statusVersion <= baselineVersion)
		{
			return false;
		}
		bool num = baselineBrightness >= 0 && baselineBrightness <= 100 && reportedBrightness >= 0 && reportedBrightness <= 100 && reportedBrightness != baselineBrightness;
		bool flag = !string.IsNullOrWhiteSpace(effect) && !string.Equals(effect, baselineEffect, StringComparison.OrdinalIgnoreCase);
		return num | flag;
	}

	/// <summary>
	/// 2026-10-12（机主原话「亮度是0，所以打开关闭没有变化，把亮度拉高就好了」）：**有效键盘灯亮度** ——
	/// UI 滑块 / 配置 / GCU 回读归一到同一个值（<see cref="LightingState.NormalizeKeyboardBrightness" />）：
	/// 有效电源为 On、缓存亮度却是 0 时 ⇒ 抬回**上一次非 0 的亮度**（没历史则 50%），
	/// 于是"开着却不亮"不再可能（⛔ 电源不 On 时一个字节都不动）。
	/// </summary>
	internal static int ResolveEffectiveKbBrightness()
	{
		if (rgb == null)
		{
			return LightingState.KeyboardBrightnessUnknown;
		}
		return LightingState.NormalizeKeyboardBrightness(ResolveEffectiveKbPower(), rgb.Brightness, rgb.KbLastNonZeroBrightness, KeyboardBrightnessZeroIsOff());
	}

	/// <summary>
	/// 2026-10-12：亮度 0 在本机型是否本来就等于"关" —— 单分区键盘（<c>KeyboardType∈{1,2}</c>）的亮度
	/// 就是 0~4 档位，0 是合法的最暗/关 ⇒ ⛔ 不去覆盖那条既有语义；per-key 机型（如机主的 type 7）
	/// 的亮度是 0~100 百分数，0 = **开着但全灭** ⇒ 必须抬起来。判据与通道归属
	/// （<c>RgbForm.IsGcuKeyboardFallback</c>）用的 <c>IsGcuZoneKeyboardPath</c> 同一个，「单分区」在两条路径上含义一致。
	/// </summary>
	private static bool KeyboardBrightnessZeroIsOff()
	{
		return KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(hw?.Capabilities.KeyboardType ?? 0);
	}

	/// <summary>
	/// 2026-10-12（亮度 0 归一化）：**键盘亮着（有效电源 On）却没有可见亮度** ⇒ 需要把亮度抬起来。
	/// ⛔ 这是 <c>OnHardwareStateChanged</c> 的第三条重申判据（原来只有"亮度变了"/"效果变了"/"受保护模式"），
	/// 否则"电源 On + 亮度 0 + 效果没变"这一格谁都不管（正是机主那一幕）。
	/// </summary>
	internal static bool ShouldRestoreKeyboardBrightness()
	{
		return rgb != null && ResolveEffectiveKbPower() && ResolveEffectiveKbBrightness() == 0;
	}

	internal static bool SyncKeyboardBrightnessForFirmwareChange(int reportedBrightness, Func<int> currentHidBrightness, Action<int> setHidBrightness)
	{
		if ((reportedBrightness < 0 || reportedBrightness > 100) ? true : false)
		{
			return false;
		}
		if (currentHidBrightness() == reportedBrightness)
		{
			return false;
		}
		setHidBrightness(reportedBrightness);
		return true;
	}

	internal static bool ReassertKeyboardCustomModeForCurrentGeneration(int generation, Func<int> currentGeneration, Action reassert)
	{
		if (generation != currentGeneration())
		{
			return false;
		}
		reassert();
		return true;
	}
	// （同方单键 RGB 开机闪烁诊断）：开机灯光时序日志（默认可见、带进程启动毫秒数）。
	private static void LogLightBoot(string message)
	{
		try
		{
			long milliseconds = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
			Logger.WriteError($"[light-boot] t={milliseconds}ms {message}");
		}
		catch (Exception ex)
		{
			Logger.WriteError("[light-boot] " + message + "（计时失败：" + ex.Message + "）");
		}
	}

	private static void ReassertKeyboardCustomMode()
	{
		KeyboardRgb keyboardRgb = rgb;
		if (keyboardRgb == null || !keyboardRgb.KbPowerOn)
		{
			return;
		}
		// （同方单键 RGB 开机闪 · 低风险防御）：只对 per-key 键盘生效
		int keyboardType = hw?.Capabilities?.KeyboardType ?? 0;
		if (keyboardType > 0 && !KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(keyboardType))
		{
			long bootMilliseconds;
			try
			{
				bootMilliseconds = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
			}
			catch
			{
				bootMilliseconds = 0;
			}
			// ① 开机宽限 15s：只做数值同步，不重启 HID 帧流 ✅
			if (bootMilliseconds < 15000)
			{
				LogLightBoot($"硬件状态重申：跳过（开机宽限 t={bootMilliseconds}ms）");
				return;
			}
			// ② 防抖：两次重申至少间隔 3s（期间多次状态变化合并；pending 标记由调用方保证）✅
			long now = Environment.TickCount64;
			long last = Interlocked.Read(ref _lastReassertKeyboardCustomModeTick);
			if (last != 0 && now - last < 3000)
			{
				LogLightBoot($"硬件状态重申：跳过（防抖 {now - last}ms < 3000ms）");
				return;
			}
			Interlocked.Exchange(ref _lastReassertKeyboardCustomModeTick, now);
		}
		LogLightBoot($"HID 帧流：ReInitCustomMode + StartMode(mode={keyboardRgb.KbHidMode})（来源=硬件状态重申）");
		keyboardRgb.ReInitCustomMode();
		keyboardRgb.StartMode(keyboardRgb.KbHidMode);
		Logger.WriteLine($"RGB 固件接管，立即重申自定义帧模式：mode={keyboardRgb.KbHidMode}");
	}

	internal static void OnHardwareStateChanged(string topic)
	{
		// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：原来只看 LM 缓存的 KbPowerOn，
		// 灯是第三方通道（Fn 热键 / 官方控制台）点亮、硬件报 On 而 LM 缓存 false 时，这里**直接 return**
		// ⇒ 固件改动永远不被重申。现在把"硬件报的电源"也算进来（用户明确关过的情形由
		// KbPowerExplicitOff 在下方 HasUserLightingIntent / 恢复路径里守住）。
		bool kbHardwareOn = hw != null && hw.KeyboardPower;
		if (!string.Equals(topic, "Keyboard/Status", StringComparison.Ordinal) || Volatile.Read(in _exitStarted) != 0 || rgb == null || (!rgb.KbPowerOn && !kbHardwareOn) || !rgb.IsConnected || hw == null || Volatile.Read(in _lightingIdleSuspended) != 0)
		{
			return;
		}
		// 2026-10-10（①「安装后灯被熄灭」）：情况 B（无已保存灯效配置）⇒ 官方控制台/EC/固件改了键盘灯，
		// LM ⛔ 不去"抢回来"：既不重申自定义 HID 帧模式，也不把固件亮度同步进自己的配置
		//（那条路会 QueueSaveConfig ⇒ 写出 rgb.cfg，反过来污染"是否需要恢复"的判断）。
		if (!LightingSettingsStore.HasUserLightingIntent())
		{
			return;
		}
		int num = KeyboardRgb.MapReportedHardwareBrightness(hw.KeyboardBrightness, hw.KeyboardLight);
		// 2026-10-12（机主原话「亮度是0，所以打开关闭没有变化，把亮度拉高就好了」）：GCU 回读的 0%
		// 是**坏读数**（per-key 机型开着却是 0 ⇒ 什么都看不见）⇒ 先按"有效电源 On + 亮度 0 就抬起来"
		// 归一化，再拿归一化后的值和基线比、也只把归一化后的值写回固件/缓存。
		// ⚠️ 这样三处就对齐了：滑块（读 rgb.Brightness）/ LM 缓存 / 下发给固件的值 = **同一个 num**。
		int num2 = LightingState.NormalizeKeyboardBrightness(ResolveEffectiveKbPower(), num, rgb.KbLastNonZeroBrightness, KeyboardBrightnessZeroIsOff());
		bool flag = ShouldReassertKeyboardCustomMode(hw.KeyboardStatusVersion, Interlocked.Read(in _keyboardStatusBaselineVersion), num2, Volatile.Read(in _keyboardStatusBaselineBrightness), hw.KeyboardEffect ?? "", Volatile.Read(in _keyboardStatusBaselineEffect));
		bool flag2 = ShouldReassertProtectedKeyboardCustomMode(KeyboardEffectProtectArmed, hw.KeyboardStatusVersion, Interlocked.Read(in _keyboardEffectProtectBaselineVersion), hw.KeyboardPower, Environment.TickCount64 > Volatile.Read(in _keyboardEffectProtectDeadlineMs));
		// 第三条判据：亮着但亮度为 0（谁都不管的"电源 On + 亮度 0 + 效果没变"那一格）。
		bool flag3 = ShouldRestoreKeyboardBrightness();
		if (!flag && !flag2)
		{
			if (!flag3)
			{
				return;
			}
			// 只补亮度：把归一化后的值落进缓存 + 走**作者既有的 HID 亮度路**（`ApplyBrightnessToDevice`
			// → `Step3`；效果帧由 `KeyboardRgb.WriteFrameLocked` 按 `Brightness` 现算现缩，见 KeyboardRgb.cs:879-887）
			// ⇒ 帧流不用重启就换到新亮度。⛔ 不动电源、⛔ 不碰 explicitOff、⛔ 不改帧格式/时序。
			if (num2 != rgb.Brightness)
			{
				Logger.WriteLine($"RGB 亮度 0 归一化：GCU 回读 {num}% / 缓存 {rgb.Brightness}% ⇒ 抬到 {num2}%（有效电源 On）");
				int num4 = rgb.Brightness;
				rgb.Brightness = num2;
				rgb.QueueSaveConfig();
				if (!rgb.ApplyBrightnessToDevice())
				{
					Logger.WriteLine($"RGB 亮度 0 归一化：{num2}% 未能在 HID 侧生效（效果帧下帧仍会按新亮度缩放）");
				}
				// 2026-10-12（UI 诚实）：⛔ 不静默 —— 机主就是这样才发现"亮度是 0"的。
				KeyboardLightingDecision.NotifyBrightnessRestored(num2, $"硬件状态回读：{num4}% -> {num2}%");
			}
		}
		if (flag && SyncKeyboardBrightnessForFirmwareChange(num2, () => rgb.Brightness, (int value) =>
		{
			rgb.Brightness = value;
			rgb.QueueSaveConfig();
		}))
		{
			Logger.WriteLine($"RGB 硬件亮度同步：GCU={num}% HID={num}%");
		}
		if (Interlocked.Exchange(ref _keyboardStatusRecoveryPending, 1) != 0)
		{
			return;
		}
		if (flag2)
		{
			Interlocked.Exchange(ref _keyboardEffectProtectArmed, 0);
		}
		int takeoverGeneration = Interlocked.Increment(ref _keyboardFirmwareTakeoverGeneration);
		Task.Run(async () =>
		{
			try
			{
				ReassertKeyboardCustomModeForCurrentGeneration(takeoverGeneration, () => Volatile.Read(in _keyboardFirmwareTakeoverGeneration), ReassertKeyboardCustomMode);
				if (!(await RestoreKeyboardLightingAsync().ConfigureAwait(continueOnCapturedContext: false)))
				{
					Logger.WriteLine("RGB 硬件亮度事件恢复失败，将等待下一次硬件连接恢复");
					RestoreLightingWithRetryAsync(force: true);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("RGB 硬件亮度事件处理失败: " + ex.Message);
			}
			finally
			{
				Volatile.Write(ref _keyboardStatusRecoveryPending, 0);
			}
		});
	}

	internal static void NotifyLightingUserIntent()
	{
		if (Interlocked.Exchange(ref _lightingIdleSuspended, 0) != 0)
		{
			RestoreLightingWithRetryAsync(force: true);
		}
	}

	internal static int ExternalRestoreAppliedRequestId(string topic)
	{
		if (!_lightingExternalRestoreAppliedIds.TryGetValue(topic, out var value))
		{
			return -1;
		}
		return value;
	}

	internal static Task<LightingIdleAction> EvaluateLightingIdleAsync()
	{
		int timeoutSeconds = AppConfig.Get("lighting_idle_seconds", Math.Max(0, rgb?.CloseTimerMinutes ?? 0) * 60);
		// （平台互斥 · 平台判定**单一来源** UsesBaolongda）
		if (hw?.UsesBaolongda == true)
		{
			StopVendorLightingIdleMonitorForBaolongda();
			return Task.FromResult(LightingIdleAction.None);
		}
		// ⭐ 同方/GCU：原有**软件计时**（`lighting_idle_seconds` + GetIdleTime + ReconcileLightingPowerAsync）✅
		//   启用时记一行（去重；关闭后重置）—— 注明平台与来源，机主可据此判断是哪套在计时 ✅
		if (timeoutSeconds <= 0)
		{
			Interlocked.Exchange(ref _lightingAutoOffEnableLogged, 0);
		}
		else if (Interlocked.Exchange(ref _lightingAutoOffEnableLogged, 1) == 0)
		{
			Logger.WriteError("[light] 自动关灯：已启用（" + DescribeAutoOffSeconds(timeoutSeconds) + " · 软件计时 · 平台=同方/GCU）");
		}
		return EvaluateLightingIdleAsync(timeoutSeconds, Math.Max(0L, (long)NativeMethods.GetIdleTime().TotalMilliseconds));
	}
	// <summary> 自动关灯日志用的人读时长（10 秒 / N 分钟 / N 小时）。</summary>
	private static string DescribeAutoOffSeconds(int seconds)
	{
		if (seconds == 10)
		{
			return "10 秒";
		}
		if (seconds >= 3600 && seconds % 3600 == 0)
		{
			return (seconds / 3600) + " 小时";
		}
		if (seconds % 60 == 0)
		{
			return (seconds / 60) + " 分钟";
		}
		return seconds + " 秒";
	}

	internal static async Task<LightingIdleAction> EvaluateLightingIdleAsync(int timeoutSeconds, long idleMilliseconds)
	{
		if (Volatile.Read(in _exitStarted) != 0 || rgb == null)
		{
			return LightingIdleAction.None;
		}
		try
		{
			LightingIdleAction action = LightingState.ResolveIdleAction(timeoutSeconds, idleMilliseconds, Volatile.Read(in _lightingIdleSuspended) != 0);
			switch (action)
			{
			case LightingIdleAction.Suspend:
				if (Interlocked.Exchange(ref _lightingIdleSuspended, 1) == 0)
				{
					Logger.WriteLine($"灯效空闲休眠：{timeoutSeconds} 秒");
					LogLightBoot($"自动关灯：suspend（timeout={timeoutSeconds}s · idle={idleMilliseconds}ms）");
					await ReconcileLightingPowerAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				break;
			case LightingIdleAction.Restore:
				if (Interlocked.Exchange(ref _lightingIdleSuspended, 0) != 0)
				{
					Logger.WriteLine("灯效空闲恢复：检测到用户输入");
					LogLightBoot($"自动关灯：resume（idle={idleMilliseconds}ms）");
					await RestoreLightingWithRetryAsync(force: true).ConfigureAwait(continueOnCapturedContext: false);
				}
				break;
			}
			return action;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("灯效空闲检测失败: " + ex.Message);
			return LightingIdleAction.None;
		}
	}

	internal static async Task<bool> ReconcileLightingPowerAsync(bool forceKeyboardRestore = false)
	{
		await _lightingStateLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (Volatile.Read(in _exitStarted) != 0 || rgb == null)
			{
				return false;
			}
			if (IsLightingTemporarilySuspended)
			{
				await SuspendLightingTemporarilyAsync().ConfigureAwait(continueOnCapturedContext: false);
				return true;
			}
			// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ 宝龙达下**判定为不点亮**时 ⇒
			//   ⛔ 一个灯光字节都不写（Logo 15 / 键盘 16·17·18 全不碰），只回读真实状态 + 同步 UI ✅
			//   ⚠️ 只有「用户显式点开过 Logo」（PowerOn && PowerExplicit）才允许走下面的写入路径 ✅
			MechrevoHw restoreHw = hw;
			if (restoreHw != null && restoreHw.UsesBaolongda && !BaolongdaWantsStartupLightWrite())
			{
				// （机主：关灯后又自己开）：明确记录「恢复判定=只读」
				string decisionMessage = "[light] 恢复判定：BaolongdaWantsStartupLightWrite()=False ⇒ 只读同步（不写）";
				if (Logger.TryMarkChanged("light-restore-readonly-decision", decisionMessage))
				{
					Logger.WriteError(decisionMessage);
				}
				SyncBaolongdaLightStateReadOnly("启动/恢复（判定=不点亮）");
				// ⭐ 启动/恢复只读分支同样尝试恢复软件彩虹（仅硬件亮着 + 保存效果=彩虹；幂等）✅
				TryResumeBaolongdaSoftwareRainbow("启动/恢复判定=只读");
				Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
				return true;
			}
			Task<bool> keyboardTask = RestoreKeyboardLightingAsync(forceKeyboardRestore);
			Task<bool> externalTask = RestoreExternalLightingAsync();
			InlineArray2<Task<bool>> buffer = default;
			buffer[0] = keyboardTask;
			buffer[1] = externalTask;
			await Task.WhenAll<bool>(buffer).ConfigureAwait(continueOnCapturedContext: false);
			return await keyboardTask.ConfigureAwait(continueOnCapturedContext: false) & await externalTask.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("灯效状态恢复失败: " + ex.Message);
			return false;
		}
		finally
		{
			_lightingStateLock.Release();
		}
	}

	/// <summary>2026-10-22（无界白键盘）：键盘灯在 EC 0x78C（不走 GCU）时，自动关灯/恢复也要走 EC。</summary>
	private static bool ShouldUseEcKeyboardPowerChannel()
	{
		MechrevoHw mechrevoHw = hw;
		return mechrevoHw != null && mechrevoHw.WujieSmuApu && mechrevoHw.KeyboardStatusSeen
			&& string.Equals(mechrevoHw.KeyboardSolution, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task SuspendLightingTemporarilyAsync()
	{
		LogLightBoot("自动关灯/临时熄灭：suspend 开始（来源=空闲/离电）");
		Interlocked.Increment(ref _lightingRestoreRequestId);
		Interlocked.Exchange(ref _keyboardEffectProtectArmed, 0);
		rgb.StopCurrentEffect();
		MechrevoHw mechrevoHw = hw;
		// 2026-10-22（无界白键盘）：到点自动关灯要走 EC 0x78C（原来只发 GCU Keyboard/Ctrl，对白键盘无效）。
		bool ecKeyboardSuspend = ShouldUseEcKeyboardPowerChannel();
		if (ecKeyboardSuspend && service != null && ResolveEffectiveKbPower())
		{
			bool ecOff = await service.SetKeyboardPower(false).ConfigureAwait(continueOnCapturedContext: false);
			LogLightBoot($"键盘电源 off 下发（来源=自动关灯 · EC 0x78C）结果={ecOff}");
			Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 1);
		}
		// 2026-10-15（C-1 第 2 步分类 ①）：下面整段都在发 Keyboard/Ctrl 等 MQTT ⇒ 真正需要 GCU。
		if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null)
		{
			Logger.WriteLine("灯效临时熄灭（同一到期，键盘已停）：" + DescribeLightingChannels());
			return;
		}
		CaptureExternalPowerBeforeTemporarySuspend();
		// 2026-10-12（根因①：门控背离）：原来只看 LM 缓存的 KbPowerOn —— 灯是第三方通道点亮、
		// 硬件报 On 而缓存 false 时，这里会**不发**"临时熄灯"，离电/空闲自动熄灯对这条通道失效。
		// 现在改成"有效电源"（与 UI 写入同一份判据）。
		// 2026-10-12（分流泄漏修复）：⛔ 单键 RGB 机型不发这条 GCU 键盘电源 —— 临时熄灯对那种机型
		// 由上面那句 `rgb.StopCurrentEffect()`（HID 帧流停掉）完成，GCU 只管非单键机型。
		bool useGcuKeyboardPowerChannel = !ecKeyboardSuspend && ShouldUseGcuKeyboardPowerChannel();
		bool gcuPowerOffDelivered = false;
		if (useGcuKeyboardPowerChannel && ResolveEffectiveKbPower())
		{
			gcuPowerOffDelivered = await service.IssueLightPower("Keyboard/Ctrl", on: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		// 2026-10-12（窄化 GCU 分流 gate）：这条标记按"这次到底是谁把灯弄灭的"记 —— 单键机型走的是
		// 上面那句 rgb.StopCurrentEffect()（HID 的 512 字节全 0 帧），GCU 分支照旧看 off 是否下发成功。
		// ⛔ 于是「临时熄灯 ⇒ 恢复时强制重申自定义帧模式」对单键机型同样成立，不再只剩 force:true 兜底。
		if (LightingState.ShouldMarkKeyboardTemporarilySuspended(useGcuKeyboardPowerChannel, gcuPowerOffDelivered, rgb.IsConnected))
		{
			Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 1);
		}
		await SetExternalLightingPowerAsync(on: false, restoreEffect: false, useTemporarySnapshot: false).ConfigureAwait(continueOnCapturedContext: false);
		Logger.WriteLine("灯效临时熄灭（同一到期）：" + DescribeLightingChannels());
	}

	/// <summary>
	/// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：**有效键盘灯电源** ——
	/// 「硬件/GCU 当时报的电源」优先于「LM 缓存的电源」，但用户**明确关过**时缓存值赢。
	/// <para>
	/// 与 UI 侧 <c>KeyboardLightingDecision.ShouldWrite</c> 同一份语义
	/// （<see cref="LightingState.ResolveEffectiveKbPower"/>）：被第三方通道（Fn 热键 / 官方控制台）
	/// 点亮的灯，不会再被"LM 缓存里那个过期的 false"挡住。
	/// </para>
	/// </summary>
	private static bool ResolveEffectiveKbPower()
	{
		return LightingState.ResolveEffectiveKbPower(rgb == null || rgb.KbPowerOn, hw != null && hw.IsConnected && hw.KeyboardPower, rgb != null && rgb.KbPowerExplicitOff);
	}

	/// <summary>
	/// 2026-10-12（用户澄清「我加的部分是为非单键RGB键盘的分流」）：**GCU <c>Keyboard/Ctrl</c> 电源通道
	/// 只服务非单键 RGB 机型**（<c>KeyboardType∈{1,2}</c>，与 <c>RgbForm.IsGcuKeyboardFallback</c> /
	/// <c>SettingsForm.ShouldUseGcuKeyboardFallback</c> 同一套判据）。
	/// <para>单键 RGB 机型（耀世 <c>KeyboardType=7</c>）走**作者自制的 HID 自定义帧**那条路 ⇒
	/// ⛔ 恢复/熄灯路径**一个 <c>Keyboard/Ctrl</c> 字节都不发**（原来这里不看键盘类型，
	/// 于是单键机型被 <c>SetLightPower(Keyboard/Ctrl, False)</c> 反复压灭 = 分流泄漏）。</para>
	/// </summary>
	private static bool ShouldUseGcuKeyboardPowerChannel()
	{
		return LightingState.ShouldUseGcuKeyboardPowerChannel(KeyboardBrightnessZeroIsOff(), service != null);
	}

	/// <summary>
	/// 2026-10-12（关灯方向）：**关灯**时该不该走 GCU <c>Keyboard/Ctrl</c> 电源通道。
	/// <para>比 <see cref="ShouldUseGcuKeyboardPowerChannel" /> 只多一条：**HID 控制器不可用**（<c>rgb</c>
	/// 没有帧流）时也走 GCU —— 那时单键机型那条 <c>StopCurrentEffect()</c> 全 0 帧发不出去、
	/// 灯其实熄不掉，只能靠 GCU 切电源轨。HID 正常时单键机型行为一格不变（⛔ 一条 GCU 电源都不发）。</para>
	/// <para>⛔ 只服务关灯方向；开灯方向仍由 <see cref="LightingState.ShouldIssueKeyboardPowerOn" /> 说了算。</para>
	/// </summary>
	private static bool ShouldUseGcuKeyboardOffChannel()
	{
		return LightingState.ShouldUseGcuKeyboardOffChannel(KeyboardBrightnessZeroIsOff(), service != null, rgb != null && rgb.IsConnected);
	}

	/// <summary>
	/// 2026-10-12（根因① 后半句 + 用户拍板「键盘灯光也修复」）：硬件报 On ⇒ **把 LM 状态回写成"开"
	/// 并清掉"用户明确关过"那条陈旧标记**。
	/// <para>语义（⭐ 用户口径）：<c>explicitOff</c> **只在【硬件也报 Off】时才算数** —— 硬件报 On 就是
	/// 用户显然把灯打开了（机主用 Fn 点亮过），那个标记必须让位。⛔ 只有"两边都报 Off"才保持关闭。</para>
	/// <para>⚠️ 为什么顺序改了：原来第一句就是 <c>if (rgb.KbPowerOn || rgb.KbPowerExplicitOff) return false;</c>
	/// ⇒ 陈旧标记（硬件 On + 标记在）**永远提前 return**，回写与清理都到不了。现在先按硬件报 On 清标记，
	/// 再按"缓存已经是不是 On"决定要不要回写。</para>
	/// <para>⚠️ 防抖振（硬件 <c>Off 4~18s ↔ On 1~2s</c> 振荡）：本方法一旦把状态对齐（缓存 On + 标记清）
	/// 之后，被 <c>rgb.KbPowerOn</c> 那条提前 return 挡住 ⇒ 每次振荡最多只对齐**一次**，⛔ 不会每次报 On 都刷帧。
	/// 另一半是 <c>KeyboardRgb.KbPowerSyncFromHardware</c> 自己的幂等短路（值没变就不写、不 QueueSaveConfig）。</para>
	/// </summary>
	/// <returns>本次是否发生了回写或清标记。</returns>
	private static bool SyncKbPowerFromHardwareIfDiverged()
	{
		if (rgb == null || hw == null || !hw.IsConnected)
		{
			return false;
		}
		bool hardwareOn = hw.IsConnected && hw.KeyboardPower;
		bool explicitOffStale = LightingState.ShouldClearExplicitOffOnHardwareOn(hw.IsConnected, hw.KeyboardPower, rgb.KbPowerExplicitOff);
		if (rgb.KbPowerOn && !explicitOffStale)
		{
			// 已经是对齐状态（缓存 On、标记也不陈旧）⇒ 幂等短路，⛔ 不刷帧、⛔ 不写配置。
			return false;
		}
		if (!hardwareOn)
		{
			return false;
		}
		if (!rgb.KbPowerOn)
		{
			rgb.KbPowerSyncFromHardware(on: true);
			Logger.WriteLine("键盘灯电源背离：硬件/GCU 报 On 而 LM 缓存 false（用户没明确关过）⇒ 已把 LM 状态回写成开");
		}
		if (explicitOffStale && rgb.ClearKbPowerExplicitOffFromHardware())
		{
			Logger.WriteLine("键盘灯电源背离：硬件/GCU 报 On ⇒ 已清掉「用户明确关过」的陈旧标记并把状态回写成开");
		}
		return true;
	}

	internal static string DescribeLightingChannels()
	{
		string value2;
		if (hw == null || !hw.QuickSwitches.TryGetValue("lightbar", out var value))
		{
			value2 = "n/a";
		}
		else
		{
			value2 = (value ? "On" : "Off");
		}
		string value4;
		if (hw == null || !hw.QuickSwitches.TryGetValue("logolight", out var value3))
		{
			value4 = "n/a";
		}
		else
		{
			value4 = (value3 ? "On" : "Off");
		}
		string value5 = ((rgb != null && rgb.ActiveMode >= 0) ? $"On(mode={rgb.ActiveMode})" : "Off");
		return $"keyboard={value5} lightbar={value2} logo={value4}";
	}

	private static void CaptureExternalPowerBeforeTemporarySuspend()
	{
		(string, string, Func<bool>)[] externalLightChannels = ExternalLightChannels;
		for (int i = 0; i < externalLightChannels.Length; i++)
		{
			var (key, key2, _) = externalLightChannels[i];
			if (!_externalPowerBeforeTemporarySuspend.ContainsKey(key) && hw.QuickSwitches.TryGetValue(key2, out var value))
			{
				_externalPowerBeforeTemporarySuspend[key] = value;
			}
		}
	}

	private static async Task<bool> RestoreExternalLightingAsync()
	{
		// 2026-10-10（①「安装后灯被熄灭」）：情况 B —— 没有任何"用户明确设置过灯效"的记录
		// （从未配置，或被安装程序删掉了 %APPDATA%\MechrevoLite）⇒ ⛔ 一条灯带/Logo/Hinge/Sync 的
		// SetPower / SetEffectALL 都不发。硬件当前状态可能是官方控制台或 EC/固件设的，LM 不覆盖才是对的。
		// 情况 A（配置存在且是用户设定）/ 情况 C（配置存在且用户明确关灯）不受影响：照旧恢复，C 仍恢复成"关"。
		if (!LightingSettingsStore.HasUserLightingIntent())
		{
			Logger.WriteLine("灯效恢复：无已保存灯效配置，跳过外部灯效（不发灯带/Logo/Hinge/Sync 的 SetPower/SetEffectALL）");
			return true;
		}
		int value = Volatile.Read(in _lightingRestoreRequestId);
		var (flag, flag2, text) = await SetExternalLightingPowerAsync(on: true, restoreEffect: true, useTemporarySnapshot: true, value).ConfigureAwait(continueOnCapturedContext: false);
		if (flag & flag2)
		{
			Logger.WriteLine("灯效恢复（单次下发）：keyboard=" + ((rgb?.KbPowerOn ?? false) ? "On" : "Off") + " " + text);
		}
		return flag;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（宝龙达 Logo 灯「UI 与硬件状态不同步」根因③：恢复后没有回灌界面）：
	/// ⭐ 灯效电源经 **宝龙达 WMI（功能号 15）**下发之后，**立刻**让界面按真实状态收敛一次 ✅
	/// <para>⚠️ 根因（机主诊断包 0.291.9.11-20261018-113538 实证）：启动「灯效恢复（单次下发）」
	/// 把 LOGO 点亮了，硬件回读也确认了，但**没有任何路径通知界面** ⇒「灯光」里的 LOGO 开关一直显示
	/// 关闭 ❌（该开关**不在** `_quickSwitches` 里 ⇒ 那个每 3 s 的快速开关刷新根本不碰它 ⚠️）。</para>
	/// <para>⭐ 本方法只做"切 UI 线程调度"：真正的刷新在
	/// <c>SettingsForm.RefreshQuickSwitchStatesNow()</c>（灯光行立刻收敛；「更多开关」仍守各自的
	/// 12 秒宽限 ⇒ ⛔ 不覆盖用户刚点的值 ✅）。⭐ 任何异常都吞掉 —— 界面刷新失败绝不影响灯效恢复本身 ✅。</para>
	/// </summary>
	private static void ScheduleQuickSwitchUiRefresh(string reason)
	{
		try
		{
			SettingsForm form = settingsForm;
			if (form == null || form.IsDisposed || !form.IsHandleCreated)
			{
				return;
			}
			if (form.InvokeRequired)
			{
				form.BeginInvoke((Action)form.RefreshQuickSwitchStatesNow);
			}
			else
			{
				form.RefreshQuickSwitchStatesNow();
			}
			Logger.WriteLineIfChanged("logo-ui-refresh", $"[logo] 已调度界面按真实状态刷新（触发：{reason}）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[logo] 调度界面刷新失败（已吞掉，不影响灯效）：" + ex.Message);
		}
	}

	internal static async Task<bool> ApplyLightChannelEffectAsync(string topic, LightChannelSettings settings, int delayMs = 120, Func<bool>? shouldApply = null)
	{
		if (delayMs > 0)
		{
			await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (shouldApply != null && !shouldApply())
		{
			return false;
		}
		if (service == null)
		{
			return false;
		}
		// 2026-10-16（0.291.8.1 · 修「logo 灯/灯带效果错乱」）：⭐ 单色系灯效都带已保存的颜色
		//（原来只有 Single 带 ⇒ 呼吸发硬编码七彩 ⇒ 硬件取首段红、混合直接关灯 ❌）。
		return await service.SetLightEffect(topic, settings.Effect, settings.Light, settings.Speed, "None", KeyboardFirmwareEffects.IsSingleColorEffect(settings.Effect) ? new Color?(Color.FromArgb(settings.ColorArgb)) : ((Color?)null), save: false).ConfigureAwait(continueOnCapturedContext: false);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：每次启动仍默认打开 Logo 灯 —— 98a3f70 的 PowerExplicit 闸门没挡住）：
	/// ⭐ **启动 Logo 恢复判断的唯一日志出口**（默认日志级别可见；只记一次，⛔ 恢复重试不刷屏）✅
	/// <para>格式：<c>[logo] 启动恢复判断：PowerOn=… · PowerExplicit=… · 决策=点亮/不点亮（原因=…）</c></para>
	/// </summary>
	private static void LogLogoRestoreDecision(bool hasSaved, LightChannelSettings settings, bool requested, bool canAct)
	{
		if (Interlocked.Exchange(ref _logoRestoreDecisionLogged, 1) != 0)
		{
			return;
		}
		string powerOnText = hasSaved ? (settings.PowerOn ? "1" : "0") : "(无配置)";
		string explicitText = hasSaved ? (settings.PowerExplicit ? "1" : "0") : "(无配置)";
		string reason;
		if (!canAct)
		{
			reason = "无已保存 Logo 配置且无临时快照 ⇒ 跳过（不写硬件、不点亮）";
		}
		else if (requested)
		{
			reason = "用户显式开过（PowerExplicit=1）⇒ 点亮";
		}
		else if (hasSaved)
		{
			reason = "用户未显式开过（旧配置/自动置位，已迁移）⇒ 不点亮";
		}
		else
		{
			reason = "临时快照=关 ⇒ 不点亮";
		}
		Logger.WriteError($"[logo] 启动恢复判断：PowerOn={powerOnText} · PowerExplicit={explicitText} · 决策={(requested ? "点亮" : "不点亮")}（原因={reason}）");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ **宝龙达启动/恢复的只读同步** ——
	/// ⛔ 一个灯光字节都不写（功能号 15 / 16 / 17 / 18 全不碰），只回读真实状态并同步进
	/// <c>QuickSwitches</c> / <c>KeyboardPower</c> / UI ✅。
	/// <para>⭐ 回读判据（机主确认）：Logo = 功能号 15；键盘 = <c>16 != 0 且 18 &gt; 0</c> 才算「亮着」
	/// （⛔ 不用功能号 10 —— 它在宝龙达恒 0）✅。</para>
	/// <para>日志（默认日志级别可见；值变化才再记一次）：
	/// <c>[light] 启动：只读同步（不写硬件）⇒ logo=…（来源=功能号15 回读）· keyboard=亮/灭（16=… · 18=…）</c></para>
	/// </summary>
	private static void SyncBaolongdaLightStateReadOnly(string reason)
	{
		MechrevoHw mechrevoHw = hw;
		if (mechrevoHw == null || !mechrevoHw.UsesBaolongda)
		{
			return;
		}
		try
		{
			bool logoReadable = mechrevoHw.TryReadLogoLightViaBackend(out bool logoOn);
			if (logoReadable)
			{
				// ⭐ 只同步镜像/UI（SyncLogoLightQuickSwitch 只写 QuickSwitches，⛔ 不写硬件）✅
				mechrevoHw.SyncLogoLightQuickSwitch(logoOn, "功能号15 回读");
			}
			bool keyboardReadable = mechrevoHw.TryReadKeyboardLightingViaBackend(out bool keyboardOn,
				out int keyboardMode, out int keyboardBrightness, out string _);
			if (keyboardReadable)
			{
				mechrevoHw.SyncKeyboardPowerFromHardware(keyboardOn, "功能号16/18 回读");
			}
			string logoText = logoReadable ? (logoOn ? "亮" : "灭") : "读不到";
			string keyboardText = keyboardReadable ? (keyboardOn ? "亮" : "灭") : "读不到";
			string modeText = (keyboardMode >= 0) ? keyboardMode.ToString() : "读不到";
			string brightnessText = (keyboardBrightness >= 0) ? keyboardBrightness.ToString() : "读不到";
			string message = $"[light] 启动：只读同步（不写硬件）⇒ logo={logoText}（来源=功能号15 回读）"
				+ $" · keyboard={keyboardText}（16={modeText} · 18={brightnessText}）";
			// ⭐ 默认日志级别是「仅错误」⇒ 用 WriteError 保证可见；值没变不重复刷 ✅
			if (Logger.TryMarkChanged("light-startup-readonly", message))
			{
				Logger.WriteError(message);
			}
			Logger.WriteLineIfChanged("light-startup-readonly-reason", "[light] 启动只读同步触发：" + reason);
			// ⭐ UI 立刻按真实状态收敛（灯光行 +「更多开关」；⛔ 不改任何硬件）✅
			ScheduleQuickSwitchUiRefresh("启动只读同步");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[light] 启动只读同步失败（已吞掉，⛔ 未写硬件）：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ **启动/恢复时是否允许写灯光硬件** ——
	/// 宝龙达下只有「用户显式点开过 Logo 开关」（<c>PowerOn &amp;&amp; PowerExplicit</c>，见 98a3f70 标记）
	/// 才允许发一次点亮命令 ✅；其余（旧配置/未配置/显式关）一律**只读**，⛔ 不写、⛔ 不"恢复成关"。
	/// <para>⚠️ 键盘通道**没有**可靠的「显式开过」标记：<c>rgb.cfg</c> 的 <c>kbPower=1</c> 会被缺省值/退出时
	/// 无条件 SaveConfig 写成 1（只有 <c>kbPower=0</c> 是可靠证据）⇒ 启动键盘一律只读、⛔ 不下发 ✅。</para>
	/// </summary>
	private static bool BaolongdaWantsStartupLightWrite()
	{
		MechrevoHw mechrevoHw = hw;
		if (mechrevoHw == null || !mechrevoHw.UsesBaolongda)
		{
			return false;
		}
		if (LightingSettingsStore.TryLoad(MqttTopics.LogoLightCtrl, out LightChannelSettings logoSettings))
		{
			return logoSettings.PowerOn && logoSettings.PowerExplicit;
		}
		return false;
	}

	private static async Task<(bool AllIssued, bool Attempted, string IssuedSummary)> SetExternalLightingPowerAsync(bool on, bool restoreEffect, bool useTemporarySnapshot, int? restoreRequestId = null)
	{
		MechrevoHw mechrevoHw = hw;
		// ⭐⭐⭐ 2026-10-18（机主回归：每次启动仍默认打开 Logo 灯 —— 98a3f70 的 PowerExplicit 闸门没挡住）：
		//   ⚠️ 根因：`savedPowerOn` 判据只写在**下面 GCU 循环**里；而宝龙达在这段开头就
		//      `TrySetLogoLightViaBackend(on)` **提前 return** ⇒ 闸门**永远不执行** ⇒ 每次启动都点亮 ❌
		//   ⭐ 修法：把**同一份 savedPowerOn 判据**前置到宝龙达分支（与 GCU 循环逐字同口径）：
		//      · 有保存配置 ⇒ `PowerOn && (PowerExplicit || 非宝龙达)`（宝龙达下必须 explicit=1 才点亮）✅
		//      · 无保存配置 ⇒ 只有"空闲熄灯前的临时快照"才算可恢复，否则**跳过、不写硬件** ✅
		//      · 判为"不点亮"但确有保存配置 ⇒ 下发 off 恢复为关（与 GCU 循环一致）✅
		//   ⚠️ 非宝龙达（GCU/同方）**逐位不变**：本分支只认 `UsesBaolongda` ✅
		if (mechrevoHw != null && mechrevoHw.UsesBaolongda)
		{
			// ⭐ 一次性迁移：清掉 Logo 通道历史上被"自动路径/早期版本"置上的 powerexplicit=1
			//   （⛔ 不碰 power 值；⛔ 只对宝龙达执行 —— GCU/同方的恢复判据逐位不变 ✅）
			//   ⚠️ 必须在下面 TryLoad/判定**之前**跑（只查一次；标记 logo_explicit_migrated 落进 config.json）✅
			if (Interlocked.Exchange(ref _logoExplicitMigrationChecked, 1) == 0)
			{
				LightingSettingsStore.MigrateLogoPowerExplicitOnce();
			}
			string logoTopic = MqttTopics.LogoLightCtrl;
			bool logoHasSaved = LightingSettingsStore.TryLoad(logoTopic, out LightChannelSettings logoSettings);
			bool? logoSuspendPower = (_externalPowerBeforeTemporarySuspend.TryGetValue(logoTopic, out bool logoSuspendOn)
				? new bool?(logoSuspendOn)
				: ((bool?)null));
			// ⭐ 与 GCU 循环同口径：on=true 时"没有任何可恢复内容"⇒ 跳过（⛔ 不写硬件）
			bool logoCanAct = !on || logoHasSaved || (useTemporarySnapshot && logoSuspendPower.HasValue);
			bool logoSavedPowerOn = logoHasSaved
				? (logoSettings.PowerOn && (logoSettings.PowerExplicit || !mechrevoHw.UsesBaolongda))
				: (logoSuspendPower == true);
			bool logoRequested = on && logoSavedPowerOn;
			if (on)
			{
				LogLogoRestoreDecision(logoHasSaved, logoSettings, logoRequested, logoCanAct);
			}
			// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ 判定为「不点亮」⇒ **一个字节都不写**
			//   （⛔ 不再"恢复成关"、⛔ 不写功能号 15/16/17/18），只回读真实状态并同步 UI/镜像 ✅
			//   ⚠️ 原实现这里会下发一次 off（机主日志：`on=False（请求 on=True）`）⇒ 已按机主要求去掉 ✅
			if (on && !logoRequested)
			{
				SyncBaolongdaLightStateReadOnly("Logo 恢复（判定=不点亮）");
				return (AllIssued: true, Attempted: true, IssuedSummary: "logo=只读（未写）");
			}
			if (logoCanAct && mechrevoHw.TrySetLogoLightViaBackend(logoRequested, out bool logoConfirmed))
			{
				string logoSummary = $"logo={(logoRequested ? "On" : "Off")}{(logoConfirmed ? "" : "(unconfirmed)")}";
				Logger.WriteLine($"外部灯效电源（⭐ 宝龙达 WMI 路径）：⭐ on={logoRequested}（请求 on={on}）· ⭐ 回读确认={logoConfirmed} · ⭐ {logoSummary}");
				// ⭐⭐ 2026-10-18（「UI 与硬件状态不同步」根因③）：⭐ 真实状态已在 `TrySetLogoLightViaBackend`
				//   里按功能号 15 的回读值回灌 `hw.QuickSwitches` ⇒ ⭐ **立刻**让界面跟上 ✅
				ScheduleQuickSwitchUiRefresh("宝龙达 WMI 灯效电源");
				if (logoConfirmed)
				{
					if (on && useTemporarySnapshot)
					{
						_externalPowerBeforeTemporarySuspend.Remove(logoTopic);
					}
					if (on && restoreRequestId.HasValue)
					{
						_lightingExternalRestoreAppliedIds[logoTopic] = restoreRequestId.Value;
					}
				}
				return (AllIssued: logoConfirmed, Attempted: true, IssuedSummary: logoSummary);
			}
			if (!logoCanAct)
			{
				// ⭐ 防御：on=true 且无任何可恢复内容（上面已提前 return，正常走不到）⇒ 不写硬件
				Logger.WriteError("[logo] 启动恢复：跳过 Logo 电源下发（无已保存配置/无临时快照 ⇒ 不写硬件）");
				return (AllIssued: true, Attempted: true, IssuedSummary: "logo=skip");
			}
			Logger.WriteLine("外部灯效电源（宝龙达 WMI 路径）：⚠️ 门面层未接管（异常路径，继续原路）");
		}
		// 2026-10-15（C-1 第 2 步分类 ①）：函数体全是 service.Issue*(MQTT) ⇒ 真正需要 GCU。
		if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null)
		{
			return (AllIssued: false, Attempted: false, IssuedSummary: "");
		}
		bool allSucceeded = true;
		bool attempted = false;
		List<string> issuedSummary = new List<string>();
		List<Task<bool>> effectTasks = new List<Task<bool>>();
		List<string> effectTopics = new List<string>();
		(string Topic, string SwitchKey, Func<bool> Supported)[] externalLightChannels = ExternalLightChannels;
		int successRequestId = default;
		int i;
		// 2026-10-09（灯带去重）：耀世实测 HidLightbar/Ctrl 与 HidLightbar_Logo/Ctrl 的能力位 / 开关 /
		// 效果表是两套（LightbarEffects 5 项 vs LogoEffects 3 项）、各自还有独立的 saved 设置，但两条
		// Status 的 type/power/light 逐字同值 —— 同一块物理灯带。改前去重不存在，同一物理灯带被两个
		// 逻辑行按各自设置**背靠背反复覆盖**、且双双 confirmed ⇒ 肉眼就是「灯带抽搐闪动」。
		// 去重口径：两行都支持、且最近一次 Status 的客观字段指纹逐字相同 ⇒ 判为同一物理灯带，只发一次。
		// power 取两行的 OR（任一行要开就开，与旧行为在"两行同值时"等价，只在两行设置不一致时取并集）；
		// effect 以灯带行为准。⚠️ UI 两行照旧显示、开关照旧可用（本函数只影响下发次数）。
		// 2026-10-09（灯带 bisect 开关）：整段去重判定外包一层 LightbarDedupEnabled（默认 true）。
		// ⚠️ 为 false 时下面这个 if 整块不进入 ⇒ 三个数组恒为 false/null ⇒ 后面所有去重分支
		// （指纹比对、lastResort、power 取 OR、跳过 Logo 行）全部不参与 ⇒ 四个通道照常各发一次，
		// 与 18.6 的循环体逐字一致。
		bool[] dedupeSuppressed = new bool[externalLightChannels.Length];
		string? dedupePrimaryTopic = null;
		bool[] dedupePower = new bool[externalLightChannels.Length];
		string?[] dedupeEffectTopic = new string?[externalLightChannels.Length];
		if (LightCompatibilitySwitches.LightbarDedupEnabled
			&& ((mechrevoHw.SupportsLightbar && mechrevoHw.SupportsLogoLight) || mechrevoHw.LightbarStatusFingerprintSeen("HidLightbar/Status") || mechrevoHw.LightbarStatusFingerprintSeen("HidLightbar_Logo/Status")))
		{
			string? lightbarFingerprint = mechrevoHw.LightbarStatusFingerprint("HidLightbar/Status");
			string? logoFingerprint = mechrevoHw.LightbarStatusFingerprint("HidLightbar_Logo/Status");
			// 2026-10-15（bugfix-fan-logo bug②，判据收紧）：指纹同值**不再单独**构成"同一物理灯带"。
			// 诊断包实证：一台 mbLogo=Y 的机型两行指纹逐字同值（log.1.txt:8743
			// type=MEZone_Lighbar4|powerStatus=Off|power=-|light=-|brightNess=2）⇒ Logo 行被整行跳过、
			// HidLightbar_Logo/Ctrl 休眠唤醒后从未重发 ⇒ logo 灯不恢复。收紧后的口径：
			// 指纹同值 **且** 该机**没有**独立 Logo 灯能力（SupportsLogoLight == false —— 能力位与
			// LogoLightStatusSeen 双回退都为否）**且** GCU 未报 MbLogoSupport（分体 MBLogo = 两行都真实，
			// 与 LightExtraRows.BaseRowPhantom 的保护闸同一口径）⇒ 才判同一物理灯带、抑制 Logo 行。
			// 有独立 Logo 能力 ⇒ Logo 行照常走各自 saved 设置单独重发（判定不改 power OR 逻辑：两行
			// 都会被发，各按各的值，不会再互相顶掉）。LightbarDedupEnabled 总开关原样保留：
			// LMECHREVO_LIGHTBAR_DEDUP=0 仍整体关掉去重（真·同一灯带机型如再抽搐可用它退回 18.6）。
			bool logoIndependentCapability = mechrevoHw.SupportsLogoLight || mechrevoHw.LightbarMbLogoSupport == true;
			if (lightbarFingerprint != null && logoFingerprint != null && string.Equals(lightbarFingerprint, logoFingerprint, StringComparison.Ordinal)
				&& !string.Equals(lightbarFingerprint, "type=-|powerStatus=-|power=-|light=-|brightNess=-", StringComparison.Ordinal)
				&& !logoIndependentCapability)
			{
				// 只对这四个灯条主题去重（灯带 / Logo / 铰链 / 同步），索引不做假设。
				for (int k = 0; k < externalLightChannels.Length; k++)
				{
					string candidateTopic = externalLightChannels[k].Topic;
					if (!string.Equals(candidateTopic, "HidLightbar/Ctrl", StringComparison.Ordinal) && !string.Equals(candidateTopic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal))
					{
						continue;
					}
					if (dedupePrimaryTopic == null)
					{
						dedupePrimaryTopic = candidateTopic;
					}
					else
					{
						dedupeSuppressed[k] = true;
						dedupePower[k] = true;
						dedupeEffectTopic[k] = dedupePrimaryTopic;
					}
				}
				if (dedupePrimaryTopic != null)
				{
					Logger.WriteLine("灯效去重：HidLightbar/Ctrl 与 HidLightbar_Logo/Ctrl 的 Status 指纹逐字同值（" + lightbarFingerprint + "），已按同一物理灯带去重：只下发一次（effect 以 HidLightbar/Ctrl 为准，power 取两行 OR）");
				}
			}
		}
		bool dedupeLoggedRow = false;
		for (i = 0; i < externalLightChannels.Length; i++)
		{
			(string, string, Func<bool>) tuple = externalLightChannels[i];
			var (topic, _, _) = tuple;
			if (!tuple.Item3())
			{
				continue;
			}
			if (on && restoreRequestId.HasValue)
			{
				int valueOrDefault = restoreRequestId.GetValueOrDefault();
				if (_lightingExternalRestoreAppliedIds.TryGetValue(topic, out var value) && value == valueOrDefault)
				{
					continue;
				}
			}
			bool hasSavedSettings = LightingSettingsStore.TryLoad(topic, out var settings);
			bool? flag = (_externalPowerBeforeTemporarySuspend.TryGetValue(topic, out var value2) ? new bool?(value2) : ((bool?)null));
			if (on && !hasSavedSettings && (!useTemporarySnapshot || !flag.HasValue))
			{
				continue;
			}
			// 去重行（本例=Logo）：本行自身也要满足"有内容可发"的条件，否则不消耗它 ——
			// 下面 lastResort 那条会让主行（灯带）在这一轮重新尝试，避免两行同时落空。
			if (LightCompatibilitySwitches.LightbarDedupEnabled && dedupeSuppressed[i])
			{
				bool suppressedHasContent = on && (hasSavedSettings ? settings.PowerOn : flag == true);
				if (suppressedHasContent && !dedupeLoggedRow)
				{
					dedupeLoggedRow = true;
					Logger.WriteLine("灯效去重：跳过 " + topic + "（与 " + dedupeEffectTopic[i] + " 为同一物理灯带；SupportsLogoLight=" + (mechrevoHw.SupportsLogoLight ? "Y" : "N") + " MbLogoSupport=" + (mechrevoHw.LightbarMbLogoSupport == true ? "Y" : (mechrevoHw.LightbarMbLogoSupport == false ? "N" : "-")) + "）");
				}
				if (suppressedHasContent)
				{
					continue;
				}
			}
			// 去重行（本例=Logo）：本行自身也要满足"有内容可发"的条件，否则不消耗它 ——
			// 上面 lastResort 那条会让主行（灯带）在这一轮重新尝试，避免两行同时落空。
			if (LightCompatibilitySwitches.LightbarDedupEnabled && dedupeSuppressed[i])
			{
				bool suppressedHasContent = on && (hasSavedSettings ? settings.PowerOn : flag == true);
				if (suppressedHasContent && !dedupeLoggedRow)
				{
					dedupeLoggedRow = true;
					Logger.WriteLine("灯效去重：跳过 " + topic + "（与 " + dedupeEffectTopic[i] + " 为同一物理灯带）");
				}
				if (suppressedHasContent)
				{
					continue;
				}
			}
			bool lastResort = false;
			if (LightCompatibilitySwitches.LightbarDedupEnabled)
			{
				for (int r = i + 1; r < dedupeSuppressed.Length; r++)
				{
					if (dedupeSuppressed[r] && !_lightingExternalRestoreAppliedIds.ContainsKey(externalLightChannels[r].Topic))
					{
						lastResort = true;
						break;
					}
				}
			}
			// （PowerExplicit）才在启动/恢复时点亮；旧配置没有该标记 ⇒  不点灯
			bool savedPowerOn = hasSavedSettings
				? (settings.PowerOn && (settings.PowerExplicit || !(Program.hw?.UsesBaolongda ?? false)))
				: (flag == true);
			bool requested = on && savedPowerOn;
			if (LightCompatibilitySwitches.LightbarDedupEnabled && requested && dedupeSuppressed[i] && !lastResort && i > 0)
			{
				continue;
			}
			if (LightCompatibilitySwitches.LightbarDedupEnabled && i > 0 && !dedupeSuppressed[i])
			{
				// 去重时 power 取两行 OR：被抑制行的开关若为 On，本轮发给主行的就是 On
				// （两行同值时与旧行为逐字一致，只在两行设置不一致时取并集）。
				int primary = i - 1;
				if (primary < dedupeSuppressed.Length && dedupeEffectTopic[primary] != null && dedupePower[primary])
				{
					requested = true;
				}
			}
			attempted = true;
			string text = MechrevoService.LightTopicToQuickSwitchKey(topic);
			if (text != null)
			{
				issuedSummary.Add(text + "=" + (requested ? "On" : "Off"));
			}
			bool flag2 = await service.PublishLightPower(topic, requested).ConfigureAwait(continueOnCapturedContext: false);
			if (flag2)
			{
				service.ObserveLightPower(topic, requested);
			}
			bool flag3 = flag2;
			if (flag2 & requested & restoreEffect & hasSavedSettings)
			{
				effectTasks.Add(ApplyLightChannelEffectAsync(topic, settings));
				effectTopics.Add(topic);
			}
			allSucceeded &= flag3;
			if (on & useTemporarySnapshot & flag3)
			{
				_externalPowerBeforeTemporarySuspend.Remove(topic);
			}
			int num;
			if (on)
			{
				if (restoreRequestId.HasValue)
				{
					successRequestId = restoreRequestId.GetValueOrDefault();
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			if (((uint)num & (flag3 ? 1u : 0u)) != 0)
			{
				_lightingExternalRestoreAppliedIds[topic] = successRequestId;
			}
			settings = default;
		}
		i = 0;
		while (i < effectTasks.Count)
		{
			int value3;
			if (!(await effectTasks[i].ConfigureAwait(continueOnCapturedContext: false)))
			{
				allSucceeded = false;
				if (on && restoreRequestId.HasValue)
				{
					_lightingExternalRestoreAppliedIds.TryRemove(effectTopics[i], out value3);
				}
			}
			value3 = i++;
		}
		return (AllIssued: !attempted | allSucceeded, Attempted: attempted, IssuedSummary: string.Join(' ', issuedSummary));
	}
	// （宝龙达：开机/重启后软件彩虹不继续）：**只在硬件当前亮着**时恢复进程内刷色循环 。
	private static void TryResumeBaolongdaSoftwareRainbow(string reason)
	{
		MechrevoHw mechrevoHw = hw;
		if (mechrevoHw == null || !mechrevoHw.UsesBaolongda)
		{
			return;
		}
		if (mechrevoHw.IsKeyboardRainbowRunning)
		{
			return; // ⭐ 幂等：已在跑，不重复启动 ✅
		}
		// ⭐⭐ 机主硬约束：**只有保存效果严格=彩虹才进入本逻辑**；其它效果一律保持原只读行为
		//   （⛔ 不读/不写 16/17/18、⛔ 不启动刷色循环、⛔ 不猜默认值）✅
		if (!LightingSettingsStore.TryLoad("Keyboard/Ctrl", out LightChannelSettings settings))
		{
			LogBaolongdaRainbowSkip(reason, "跳过原因=保存效果读不到（⛔ 不猜）");
			return;
		}
		if (!string.Equals(settings.Effect, "Rainbow", StringComparison.OrdinalIgnoreCase))
		{
			LogBaolongdaRainbowSkip(reason, $"跳过原因=非彩虹（保存效果={settings.Effect}）");
			return;
		}
		KeyboardRgb keyboardRgb = rgb;
		if (keyboardRgb != null && keyboardRgb.KbPowerExplicitOff)
		{
			LogBaolongdaRainbowSkip(reason, "跳过原因=用户显式关灯（kbPower=0）");
			return;
		}
		if (!mechrevoHw.TryReadKeyboardLightingViaBackend(out bool hardwareOn, out int _, out int brightness18, out string _))
		{
			LogBaolongdaRainbowSkip(reason, "跳过原因=硬件状态读不到");
			return;
		}
		if (!hardwareOn)
		{
			LogBaolongdaRainbowSkip(reason, "跳过原因=硬件灭");
			return;
		}
		int brightness = brightness18;
		if (brightness <= 0 && mechrevoHw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30)
		{
			// 30 系状态源是 EC 0x1807；WMI18 不是可靠状态源 ⇒ 用 EC 当前档位 ✅
			KeyboardEcBrightness.TryReadCurrentLevel(out brightness);
		}
		if (brightness <= 0)
		{
			LogBaolongdaRainbowSkip(reason, "读不到亮度");
			return;
		}
		int max = Math.Max(1, MechrevoHw.EffectiveKeyboardBrightnessMax());
		brightness = Math.Clamp(brightness, 1, max);
		mechrevoHw.StartKeyboardRainbow((byte)brightness, CancellationToken.None);
		string message = $"[light] 宝龙达启动：软件彩虹恢复（原因={reason} · 硬件亮着 · 亮度={brightness}）⇒ 已重启刷色循环";
		if (Logger.TryMarkChanged("light-bd-rainbow-resume-ok", message))
		{
			Logger.WriteError(message);
		}
	}

	/// <summary>⭐ 宝龙达彩虹恢复的跳过日志（按原因去重、默认可见）✅。</summary>
	private static void LogBaolongdaRainbowSkip(string reason, string why)
	{
		string message = $"[light] 宝龙达启动：跳过彩虹恢复（原因={reason} · {why}）";
		if (Logger.TryMarkChanged("light-bd-rainbow-skip-" + reason, message))
		{
			Logger.WriteError(message);
		}
	}

	private static async Task<bool> RestoreKeyboardLightingAsync(bool force = false)
	{
		await _keyboardRestoreLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (Volatile.Read(in _exitStarted) != 0 || rgb == null)
			{
				return false;
			}
			LogLightBoot($"键盘恢复开始：KbPowerOn={rgb.KbPowerOn} · KbPowerExplicitOff={rgb.KbPowerExplicitOff}"
				+ $" · hw.KeyboardPower={hw?.KeyboardPower} · hw.IsConnected={hw?.IsConnected}"
				+ $" · rgb.IsConnected={rgb.IsConnected} · force={force}");
			// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ 宝龙达键盘灯启动/恢复**一个字节都不写**
			//   （⛔ 不发"关灯"、⛔ 不写功能号 16/17/18、⛔ 不启动 HID 帧流）⇒ 只回读真实状态 + 同步 UI/镜像 ✅
			//   ⭐ 判据：16 != 0 且 18 > 0 才算「亮着」（⛔ 不用功能号 10）；显式关也只读 ✅
			MechrevoHw keyboardRestoreHw = hw;
			if (keyboardRestoreHw != null && keyboardRestoreHw.UsesBaolongda)
			{
				SyncBaolongdaLightStateReadOnly("键盘恢复（只读）");
				// ⭐ 只读同步之后：仅当硬件亮着且保存效果=彩虹 ⇒ 重启进程内刷色（⛔ 不点亮）✅
				TryResumeBaolongdaSoftwareRainbow("键盘恢复");
				Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
				return true;
			}
			// 2026-10-10（①「安装后灯被熄灭」）：情况 B —— 无已保存的灯效配置（从未配置，或被安装程序
			// 删掉了 %APPDATA%\MechrevoLite 里的 keyboard.cfg 等）⇒ ⛔ 这条老路径"什么都不发"：
			// 不发 Keyboard/Ctrl 的 SetPower（开也发过、关也发过）、不发 PublishKeyboardCloseTimer、
			// 不启动 HID 自定义帧流。⛔ 默认 OFF 的 KeyboardCtrlPublishSwitch 挡不住这条老路径
			// （它只被新链路 KeyboardCtrlPayload 消费），所以门必须落在本函数入口。
			// 情况 C（配置存在、用户明确关过灯）仍会往下走并恢复成"关"；情况 A 照旧恢复。
			if (!LightingSettingsStore.HasUserLightingIntent())
			{
				Logger.WriteLine("灯效恢复：无已保存灯效配置，跳过键盘灯效（不发 Keyboard/Ctrl 电源、不启动 HID 帧流）");
				return true;
			}
			// 用户保存的状态优先：保存为关 ⇒ 不因硬件状态改写保存值（下面走按保存值关闭的分支）
			bool savedPowerOff = rgb.KbPowerExplicitOff || !rgb.KbPowerOn;
			if (savedPowerOff)
			{
				LogLightBoot($"键盘恢复：保存状态为关闭（KbPowerExplicitOff={rgb.KbPowerExplicitOff}）⇒ 按保存值关闭");
			}
			else
			{
				SyncKbPowerFromHardwareIfDiverged();
			}
			bool flag = Volatile.Read(in _keyboardPowerTemporarilySuspended) != 0;
			bool forceEffectRestore = force | flag;
			// 亮度按保存值恢复，不做 0 归一化
			int effectiveBrightness = Math.Clamp(rgb.Brightness, 0, 100);
			MechrevoHw mechrevoHw;
			bool useGcuKeyboardPower = ShouldUseGcuKeyboardPowerChannel();
			if (!rgb.KbPowerOn)
			{
				LogLightBoot("HID 帧流：stop（来源=恢复判定 KbPowerOn=false）");
				rgb.StopCurrentEffect();
				// 2026-10-12（分流泄漏修复）：⛔ 单键 RGB 机型不发这条 GCU 电源 —— 停掉 HID 帧流就是"关"。
				// 2026-10-12（关灯方向）：唯一的例外是 **HID 侧不可用** —— 那时 StopCurrentEffect() 等于
				// 什么都没发（帧流不在就发不出全 0 帧），灯熄不掉 ⇒ 只有这时才退回 GCU 切电源轨。
				if (!ShouldUseGcuKeyboardOffChannel())
				{
					Logger.WriteLine("RGB 自动恢复：单键 RGB 机型不发 Keyboard/Ctrl（GCU 分流只服务非单键机型），已停 HID 帧流");
					Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
					return true;
				}
				// 2026-10-15（C-1 第 2 步分类 ①）：走的是 GCU 键盘电源轨（Keyboard/Ctrl）⇒ 真正需要 GCU。
				mechrevoHw = hw;
				if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null)
				{
					return false;
				}
				LogLightBoot("键盘电源 off 下发（来源=恢复判定关闭 · GCU Keyboard/Ctrl）");
				bool flag2 = await service.IssueLightPower("Keyboard/Ctrl", on: false).ConfigureAwait(continueOnCapturedContext: false);
				LogLightBoot($"键盘电源 off 结果={flag2}");
				if (flag2)
				{
					Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
					Logger.WriteLine("RGB 自动恢复：按用户设置保持关闭");
				}
				return flag2;
			}
			// 2026-10-12（窄化 GCU 分流 gate）：这条"必须靠 GCU 重新上电"的前置检查只属于 GCU 分流族 ——
			// 单键机型从没被 GCU 切过电源轨，它的恢复只靠 HID 帧流，⛔ 不能因为 GCU 通道不可用就整体放弃恢复。
			if (flag && useGcuKeyboardPower)
			{
				// 2026-10-15（C-1 第 2 步分类 ①）：本分支只在 useGcuKeyboardPower 下进（GCU 电源轨）⇒ 真正需要 GCU。
				mechrevoHw = hw;
				if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null)
				{
					return false;
				}
			}
			bool powerJustIssued = false;
			int requestId = Volatile.Read(in _lightingRestoreRequestId);
			bool appliedThisCycle = Volatile.Read(in _keyboardRestoreAppliedRequestId) == requestId;
			// 2026-10-22（无界白键盘）：恢复/启动恢复直接走 EC 0x78C 开灯，不走 GCU Keyboard/Ctrl。
			if (!appliedThisCycle && ShouldUseEcKeyboardPowerChannel() && service != null && ResolveEffectiveKbPower())
			{
				bool ecOn = await service.SetKeyboardPower(true).ConfigureAwait(continueOnCapturedContext: false);
				// 2026-10-22（机主：无界灯光档位要记忆）：开灯后再按上次档位（0=关/1=暗/2=亮）恢复亮度。
				int savedEcLevel = AppConfig.Get("wujie_kbd_level", -1);
				if (savedEcLevel < 1 || savedEcLevel > 2)
				{
					savedEcLevel = Math.Max(1, KeyboardRgb.MapSoftwareBrightnessToHardwareLevel(rgb.Brightness));
				}
				bool ecBright = await service.SetKeyboardBrightnessPreservingEffect(savedEcLevel).ConfigureAwait(continueOnCapturedContext: false);
				LogLightBoot($"键盘亮度记忆恢复：档位={savedEcLevel}（保存亮度={rgb.Brightness}%）结果={ecBright}");
				LogLightBoot($"键盘电源 on 下发（来源=恢复 · EC 0x78C）结果={ecOn}");
				Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
				Volatile.Write(ref _keyboardRestoreAppliedRequestId, requestId);
				return true;
			}
			if (appliedThisCycle && rgb.IsConnected && rgb.ActiveMode == rgb.KbHidMode)
			{
				MarkKeyboardCustomStatusBaseline();
				return true;
			}
			mechrevoHw = hw;
			// 2026-10-12（窄化 GCU 分流 gate）：这道门只管"发不发这条电源"，⛔ 不再整体跳过本段 ——
			// 单键机型 的既有语义只在固件电源报 Off 时补发 on（HID 帧才可见
			// ArmKeyboardEffectProtect() 与 PublishKeyboardCloseTimer(0) 照旧（非单键机型逐格不变）。
			// 2026-10-15（C-1 第 2 步分类 ①）：发 Keyboard/Ctrl 电源（GCU 通道）⇒ 真正需要 GCU。
			if (mechrevoHw != null && mechrevoHw.IsGcuConnected && service != null)
			{
				if (!appliedThisCycle && LightingState.ShouldIssueKeyboardPowerOn(useGcuKeyboardPower, rgb.KbPowerOn, flag, mechrevoHw.KeyboardPower))
				{
					ArmKeyboardEffectProtect();
					LogLightBoot("键盘电源 on 下发（来源=恢复 · GCU Keyboard/Ctrl）");
					bool flag3 = await service.IssueLightPower("Keyboard/Ctrl", on: true).ConfigureAwait(continueOnCapturedContext: false);
					LogLightBoot($"键盘电源 on 结果={flag3}");
					powerJustIssued = flag3;
					if (flag3)
					{
						await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
					}
					else
					{
						Logger.WriteLine("RGB 自动恢复：键盘电源未能下发，继续恢复本地 HID 效果");
					}
				}
				if (!appliedThisCycle)
				{
					await service.PublishKeyboardCloseTimer(0).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			// 2026-10-12（亮度 0 归一化）：**亮度是 HID 层的事**（per-key 机型的效果/亮度都走 HID 帧流），
			// GCU 只管电源/状态 ⇒ 电源下发完成、进 HID 分支之前，把归一化后的亮度交给设备一次。
			// ⚠️ 只在真的非 0 且设备已连接时下发；⛔ 失败只记日志、不改变恢复流程的返回值。
			if (rgb.KbPowerOn && rgb.IsConnected)
			{
				int num3 = ResolveEffectiveKbBrightness();
				if (num3 >= 1 && num3 <= 100 && num3 != rgb.Brightness)
				{
					rgb.Brightness = num3;
				}
				if (num3 >= 1 && num3 <= 100 && !rgb.ApplyBrightnessToDevice())
				{
					Logger.WriteLine($"RGB 自动恢复：亮度 {num3}% 未能在 HID 侧生效（继续恢复效果）");
				}
			}
			if (!forceEffectRestore && rgb.IsConnected && rgb.ActiveMode == rgb.KbHidMode)
			{
				Volatile.Write(ref _keyboardRestoreAppliedRequestId, requestId);
				MarkKeyboardCustomStatusBaseline();
				return true;
			}
			// 2026-10-09（50 系单分区键盘修复 c）：开机灯效恢复的「通道归属」要与 UI 一致 ——
			// RgbForm.IsGcuKeyboardFallback（RgbForm.cs:317）与 SettingsForm.ShouldUseGcuKeyboardFallback
			// （SettingsForm.cs:11062）都对 KeyboardType∈{1,2} 硬判「走 GCU/MQTT」，唯独这里只看控制器
			// 可用性、又硬传 hidBrightnessTookEffect:true，于是单分区机型（Type=1）在 HID 支持且已连时
			// 仍会去连 HID 并 StartMode 持续刷帧，与 UI 归属相反。
			// 本次补上同一个单分区判据 → 单分区键盘走 MQTT 时不再用 HID 帧流驱动它。
			// ⚠️ 判据额外带 50 系门：本机（Gen40，KeyboardType=1、solution=EC）也满足"单分区"，
			// 若无门就属改变 40 系既有行为，故只在 GcuVariant=="50" 时放行（30/40 系原样）。
			// ⚠️ 不动 HID 帧格式；不动下面的 HID 连接/StartMode 逻辑。
			// 回退：MechrevoService.EnableGcu50SingleZoneKeyboardFix=false 即整条失效（回到旧判据）。
			// 2026-10-09（② 的配套）：判据由 IsSingleZoneRgb 换成 IsGcuZoneKeyboardPath（仍 KeyboardType∈{1,2}）——
			// 因为 RgbForm/SettingsForm 的通道归属用的就是这个 {1,2} 判据，本块必须与 UI 归属一致（4 分区机型同样走 MQTT）。
			bool kb50SingleZone = MechrevoService.EnableGcu50SingleZoneKeyboardFix
				&& MechrevoService.IsGcu50
				&& KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(hw?.Capabilities.KeyboardType ?? 0);
			if (kb50SingleZone || KeyboardLightPathPolicy.ShouldUseGcuKeyboardFallback(rgb.ControllerAvailability, rgb.IsConnected, service != null && (hw?.IsConnected ?? false), hidBrightnessTookEffect: true))
			{
				Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
				Logger.WriteLine(kb50SingleZone
					? "RGB 自动恢复：50 系单分区键盘按 UI 归属走 MQTT，HID 分支跳过（官方通道已下发电源）"
					: "RGB 自动恢复：控制器不支持软件灯效，HID 分支跳过（官方通道已下发电源）");
				return true;
			}
			bool bootDelay = Interlocked.Exchange(ref _keyboardFirstHidStartPending, 0) == 1 && !force && !powerJustIssued;
			if (bootDelay)
			{
				LogLightBoot("首次启动：HID 帧流延后 8 秒启动（等固件/GCU 稳定后再接管）");
				await Task.Delay(8000).ConfigureAwait(continueOnCapturedContext: false);
				if (Volatile.Read(in _exitStarted) != 0 || rgb == null)
				{
					return false;
				}
			}
			bool isConnected = rgb.IsConnected;
			LogLightBoot($"HID 帧流准备：IsConnected={isConnected} · forceEffectRestore={forceEffectRestore}"
				+ $" · powerJustIssued={powerJustIssued}（来源=恢复）");
			if (!isConnected)
			{
				if (!rgb.Connect())
				{
					return false;
				}
				// 刚连上 HID 时先进入自定义模式，否则 StartMode 的帧不生效
				LogLightBoot("HID 已连接：进入自定义模式（ReInitCustomMode）");
				rgb.ReInitCustomMode();
			}
			if ((isConnected & forceEffectRestore) && !powerJustIssued && !rgb.ReInitCustomMode() && !rgb.Connect())
			{
				return false;
			}
			if (forceEffectRestore)
			{
				LogLightBoot($"HID 帧流：StartModeForced(mode={rgb.KbHidMode})（来源=恢复 forceEffectRestore）");
				rgb.StartModeForced(rgb.KbHidMode);
			}
			else
			{
				LogLightBoot($"HID 帧流：StartMode(mode={rgb.KbHidMode})（来源=恢复）");
				rgb.StartMode(rgb.KbHidMode);
			}
			MarkKeyboardCustomStatusBaseline();
			Interlocked.Exchange(ref _keyboardPowerTemporarilySuspended, 0);
			Volatile.Write(ref _keyboardRestoreAppliedRequestId, requestId);
			Logger.WriteLine($"RGB 自动恢复：HID 效果 {rgb.KbHidMode} 已运行");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB 自动恢复异常: " + ex.Message);
			return false;
		}
		finally
		{
			_keyboardRestoreLock.Release();
		}
	}

	private static void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
	{
		// 2026-10-05 uifix（用户要求④）：Windows 注销/关机/重启也是"正常退出"，
		// 先把「干净退出」标记写下来 —— 否则下次开机会被保底机制误判成非正常关机。
		// 幂等：OnExit（ApplicationExit）里还会再写一次，重复写没有副作用。
		CpuCoAutoApplyGuard.MarkCleanExit("SessionEnding " + e.Reason);
		// 2026-10-10（用户反馈 ③）：显卡超频那一套也有自己的干净退出标记（独立键，互不影响）。
		GpuOcShutdownGuard.MarkCleanExit("SessionEnding " + e.Reason);
		TryRestoreScreenAfterFailure();
		gpuControl.StandardModeFix();
		BatteryControl.AutoBattery();
		InputDispatcher.ShutdownStatusLed();
	}

	private static void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
	{
		if (e.Reason == SessionSwitchReason.SessionLogon || e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect)
		{
			Logger.WriteLine("Session:" + e.Reason);
			// 休眠恢复 / 登录恢复时 Windows 可能把 LM 再拉起一次 ⇒ 抑制「第二实例唤起主窗」
			MarkResumeShowSuppression("SessionSwitch:" + e.Reason);
			ProcessHelper.KillSmartDisplayControl();
			bool sessionLock = Aura.sessionLock;
			Aura.sessionLock = false;
			// ⭐ 2026-10-15（用户拍板「触发方式改成 ①：只认插拔 + 开机首次」）：**登录/解锁/控制台接入不再应用自动刷新率**。
			// 这里原来会 `ScreenControl.AutoScreen()` ⇒ auto 开着时，解锁一次就把用户手选的中间档（如 120Hz）
			// 顶回 插电=最大档 / 电池=MIN_RATE。按 ① 去掉（auto 关闭时本来就只是空转，去掉无副作用）。
			Aura.ApplyAura();
			if (sessionLock)
			{
				ReapplyCpuTempAfterUnlockAsync();
			}
		}
		if (e.Reason == SessionSwitchReason.SessionLock)
		{
			Logger.WriteLine("Session:" + e.Reason);
			Aura.sessionLock = true;
		}
	}

	private static async Task ReapplyCpuTempAfterUnlockAsync()
	{
		try
		{
			await Task.Delay(2000).ConfigureAwait(continueOnCapturedContext: false);
			if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) >= 10000)
			{
				modeControl.AutoCPUTemp();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Session unlock CPU temperature reapply failed: " + ex.Message);
		}
	}

	private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
	{
		if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastTheme) >= 2000 && e.Category == UserPreferenceCategory.General)
		{
			bool flag = settingsForm.InitTheme();
			settingsForm.InitContextMenuTheme();
			settingsForm.VisualiseIcon(themeChange: true);
			settingsForm.VisualiseFnLock();
			settingsForm.VisualiseBatteryFull();
			if (flag)
			{
				lastTheme = DateTimeOffset.Now.ToUnixTimeMilliseconds();
			}
			if (settingsForm.updatesForm != null && settingsForm.updatesForm.Text != "")
			{
				settingsForm.updatesForm.InitTheme();
			}
		}
	}

	public static bool SetAutoModes(bool powerChanged = false, bool init = false, bool wakeup = false)
	{
		int num;
		if (powerChanged)
		{
			num = 250;
		}
		else
		{
			num = (wakeup ? 10000 : 3000);
		}
		if (init)
		{
			gpuControl.CaptureNvBootState();
		}
		lock (autoLock)
		{
			if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < num)
			{
				return false;
			}
			lastAuto = DateTimeOffset.Now.ToUnixTimeMilliseconds();
		}
		PowerSource powerSource = ReadPowerSource();
		// ⚠️⚠️ 2026-10-15（真机回归修复）：**判据必须用 `lastAutoAppliedSource`，绝不能用 `lastObservedSource`** ——
		// 后者也被电源监视器写（`currentSource = powerSource`），监视器先跑就会把这次转换吃掉 ⇒ flag 恒 false ⇒
		// 自动刷新率在**离电/插电时不切档**（用户实测：「我发现离电不会自动切换了」，日志 changed=False 且无 Screen=60Hz）。
		// `lastObservedSource` 的赋值**保留**（`currentSource` 的 getter 依赖它，别改它的语义）。
		bool flag = lastAutoAppliedSource.HasValue && powerSource != lastAutoAppliedSource.Value;
		lastAutoAppliedSource = powerSource;
		lastObservedSource = powerSource;
		Logger.WriteLine($"AutoSetting for {SystemInformation.PowerStatus.PowerLineStatus} (source={powerSource}, changed={flag})");
		if (init | powerChanged | wakeup)
		{
			MechrevoHw mechrevoHw = hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：这里判的是「GCU 在不在 ⇒ 启动档位是交给 GCU 记忆、还是要本地兜底」，
			// 老接口机型的档位由老接口自己记（启动时 RefreshLegacyModeAsync 读回）⇒ 仍算 GCU 专属。
			if (mechrevoHw == null || !mechrevoHw.IsGcuConnected || service == null)
			{
				int num2 = AppConfig.Get("performance_" + PerformanceKey());
				_pendingStartupPerformanceMode = (Modes.Exists(num2) ? num2 : Modes.GetCurrent());
			}
		}
		BatteryControl.AutoBattery(init);
		if (init)
		{
			InputDispatcher.InitScreenpad();
		}
		DynamicLightingHelper.Init();
		ScreenControl.InitOptimalBrightness();
		if (ShouldReapplyPerformanceMode(powerChanged, wakeup, flag))
		{
			modeControl.AutoPerformance(powerChanged | flag, init | wakeup);
		}
		else
		{
			Logger.WriteLine("Display wake without a power-source change; preserving the active performance mode.");
		}
		InputDispatcher.InitStatusLed();
		if (init)
		{
			NumberPad.Init();
		}
		InputDispatcher.AutoKeyboard();
		// 2026-10-05：自动切换显卡模式（AutoGPUMode）已删除，此处始终刷新显卡模式 UI。
		gpuControl.InitGPUMode();
		// ⭐⭐ 2026-10-15（用户拍板「触发方式改成 ①」）：**只在【真·插拔电源】与【开机首次】时应用**自动刷新率。
		// 原来这里是**无条件** `ScreenControl.AutoScreen()` ⇒ 唤醒 / 会话解锁 / 切换显卡模式（都不是用户主动做的）
		// 也会走到这里，于是"用户手选的中间档（如 120Hz）会被这些非用户动作顶回 插电=最大档 / 电池=MIN_RATE"。
		//   * `flag` 由本方法上方算好 = **电源来源真的变了**（lastObservedSource 与当前不同）；
		//   * `init` = 开机首次 —— 必须保留这一条兜底：否则"关机时插上电源再开机"检测不到插拔，
		//     会停在上一档（例如上次在电池下选的 60）。
		// ⚠️ 唤醒/解锁**不再**应用。若将来发现某机型固件在唤醒时自己改了刷新率，再单独把唤醒那一条加回来。
		if (init || flag)
		{
			ScreenControl.AutoScreen();
		}
		return true;
	}

	// 2026-10-05 percore-product: 每核 CO 的「开机自动应用」（开关默认关）。
	//
	// ⚠️ 为什么挂在"开机 deferred init"里、而**不是**挂在 SetAutoModes 末尾：
	//   SetAutoModes 开头有节流（wakeup=10s / init=3s），实测本机开机的第一个 idle 之前会有一次
	//   "Monitor Power On" 唤醒调用先通过节流，紧接着的 init:true 调用就被节流掉直接 return false
	//   —— 挂在它末尾会导致**开机自动应用有时根本不执行**（实测日志里完全没有 [cpu-co] 行，
	//   已定位并修正）。deferredInitialization 是启动流程里**只跑一次**的入口，不受节流影响。
	//
	// 位置语义：SMU/PawnIO 就绪之后（这条路径在硬件连接完成、SetAutoModes 之后）；
	// 内部再延迟 7 秒，让 ModeControl.AutoRyzen（性能模式自带的 UV 全核写）先跑完，避免它把逐核值
	// 盖掉 —— 之后 RuntimeCpuTuning.OwnsCurve 会阻止模式控制再覆盖。
	// 开关为假 / 能力不满足（本机 APU 就是）/ 代号不在白名单时，RuntimeCpuTuning 内部**直接跳过
	// 并写日志**，不弹窗、不报错；异常全部吞进日志（开机流程里不能让异常冒出去）。
	private static int _cpuCoAutoApplyScheduled;

	// 2026-10-10（用户反馈 ③）：显卡超频的「上次非正常关机 ⇒ 自动关闭」只调度一次。
	private static int _gpuOcShutdownGuardScheduled;

	private static void ScheduleCpuCoAutoApply()
	{
		if (UiAuditMode)
		{
			return;
		}
		if (Interlocked.Exchange(ref _cpuCoAutoApplyScheduled, 1) != 0)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(7L)).ConfigureAwait(continueOnCapturedContext: false);
				RuntimeCpuTuning.ApplySavedOnStartup();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[cpu-co] startup auto-apply scheduling failed: " + ex.Message);
			}
		});
	}

	// 2026-10-10（用户反馈 ③「自定义模式的显卡超频功能如果上次非正常关机应该自动关闭」）：
	//   开机后**独立**调度一次判定（与 ScheduleCpuCoAutoApply 同一套编排：UiAudit 不跑、只跑一次、
	//   Task.Run 不阻塞 UI/初始化）。等硬件（GCU/MQTT）连上再动作，这样下发关闭这一步才发得出去；
	//   即使一直没连上，配置那一半（gpu_oc=0 + 清驱动侧/档位记录）也照做 —— 那才是跨重启生效的部分。
	//   ⚠️ 正常关机 / 正常重启**不会**触发：那两条路都会先写 gpu_oc_clean_exit
	//      （见 SystemEvents_SessionEnding 与 OnExit 两处的 GpuOcShutdownGuard.MarkCleanExit）。
	private static void ScheduleGpuOcShutdownGuard()
	{
		if (UiAuditMode)
		{
			return;
		}
		if (Interlocked.Exchange(ref _gpuOcShutdownGuardScheduled, 1) != 0)
		{
			return;
		}
		Task.Run(async delegate
		{
			try
			{
				Logger.WriteLine("[gpu-oc] 等待硬件连接后做「上次非正常关机 ⇒ 自动关显卡超频」判定（上限 15s）");
				Stopwatch wait = Stopwatch.StartNew();
				while (wait.Elapsed < TimeSpan.FromSeconds(15L))
				{
					MechrevoHw hwNow = hw;
					// 2026-10-15（C-1 第 2 步分类 ①）：等的是 GPU 超频通道的上一位拥有者（GCU）退场 ⇒ 真正需要 GCU。
					if (hwNow != null && hwNow.IsGcuConnected)
					{
						break;
					}
					await Task.Delay(500).ConfigureAwait(continueOnCapturedContext: false);
				}
				await GpuOcShutdownGuard.ApplyIfUncleanShutdownAsync(service).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[gpu-oc] 开机自关判定调度失败: " + ex.Message);
			}
		});
	}
	internal static bool ShouldReapplyPerformanceMode(bool powerChanged, bool wakeup, bool sourceChanged)
	{
		if (!(powerChanged | sourceChanged))
		{
			return !wakeup;
		}
		return true;
	}

	internal static bool ShouldReapplyPendingPerformanceMode(int pendingMode, int selectedMode)
	{
		if (pendingMode >= 0)
		{
			return pendingMode == selectedMode;
		}
		return false;
	}

	public static PowerSource ReadPowerSource()
	{
		if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
		{
			return PowerSource.Battery;
		}
		return PowerSource.Barrel;
	}

	public static int PerformanceKey()
	{
		return (int)SystemInformation.PowerStatus.PowerLineStatus;
	}

	private static void OnSystemEvent(Action action)
	{
		try
		{
			SettingsForm settingsForm = Program.settingsForm;
			if (settingsForm != null && !settingsForm.IsDisposed && Program.settingsForm.InvokeRequired)
			{
				Program.settingsForm.BeginInvoke(action);
			}
			else
			{
				action();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("System event dispatch failed: " + ex.Message);
		}
	}

	public static void SchedulePowerCheck()
	{
		if (!AppConfig.Is("disable_power_event"))
		{
			powerSettleTimer.Interval = Math.Max(AppConfig.Get("charger_delay"), 2000);
			powerSettleTimer.Stop();
			powerSettleTimer.Start();
		}
	}

	private static void OnPowerSettled(object? sender, ElapsedEventArgs e)
	{
		PowerSource powerSource = ReadPowerSource();
		if (powerSource != currentSource)
		{
			Logger.WriteLine($"Power source: {currentSource} -> {powerSource}");
			currentSource = powerSource;
			SetAutoModes(powerChanged: true);
			ReconcileLightingPowerAsync();
		}
	}

	private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
	{
		if (e.Mode == PowerModes.Suspend)
		{
			Logger.WriteLine("Power Mode Changed:" + e.Mode);
			_kbPowerBeforeSuspend = ResolveEffectiveKbPower() ? 1 : 0;
			Logger.WriteLine("[light] 睡眠前键盘灯状态=" + (_kbPowerBeforeSuspend == 1 ? "开" : "关") + "（唤醒按此恢复）");
			gpuControl.StandardModeFix();
			InputDispatcher.ShutdownStatusLed();
			return;
		}
		PowerLineStatus powerLineStatus = SystemInformation.PowerStatus.PowerLineStatus;
		if (powerLineStatus != lastLineStatus)
		{
			lastLineStatus = powerLineStatus;
			Logger.WriteLine($"Power Mode {e.Mode}: {powerLineStatus}");
		}
		if (e.Mode == PowerModes.Resume)
		{
			// 唤醒后一段时间内抑制「第二实例唤起主窗」（修「休眠唤醒自动弹主界面」）
			MarkResumeShowSuppression("PowerModes.Resume");
			ScheduleKeyboardLightingRestoreAfterResume();
		}
		SchedulePowerCheck();
	}

	internal static void ScheduleKeyboardLightingRestoreAfterResume()
	{
		// 2026-10-10（①「安装后灯被熄灭」）：唤醒重放（force:true）与开机恢复同语义 ——
		// 情况 B（无已保存灯效配置）下 ⛔ 整条重放都不做：不发键盘电源、不发外部灯效、不启动 HID 帧流，
		// 硬件保持官方控制台 / EC / 固件给的状态。情况 A/C 照旧（C 恢复成"关"）。
		if (!LightingSettingsStore.HasUserLightingIntent() && _kbPowerBeforeSuspend != 1)
		{
			Logger.WriteLine("RGB 唤醒恢复：无已保存灯效配置且睡前键盘灯是关的，跳过（不重放任何灯光下发）");
			return;
		}
		if (!LightingSettingsStore.HasUserLightingIntent())
		{
			Logger.WriteLine("RGB 唤醒恢复：无已保存灯效配置，但睡前键盘灯是开的 ⇒ 只恢复键盘灯，不动灯带/Logo");
		}
		KeyboardRgb keyboardRgb = rgb;
		if (keyboardRgb == null)
		{
			Logger.WriteLine("RGB 唤醒恢复：键盘对象尚未创建，跳过");
		}
		else
		{
			if (Interlocked.Exchange(ref _resumeKeyboardRestorePending, 1) != 0)
			{
				return;
			}
			// 2026-10-12（根因①）：判据同为"有效电源" —— 灯是第三方通道点亮、硬件报 On 时也要重放，
			// 否则唤醒后 HID 帧流不会重启（用户看到"有时候控制不了"）。⛔ 用户明确关过时不重放（缓存值仍赢）。
			if (KeyboardResumePolicy.ShouldRestoreEffect(ResolveEffectiveKbPower()))
			{
				Interlocked.Increment(ref _lightingRestoreRequestId);
			}
			else
			{
				Logger.WriteLine("RGB 唤醒恢复：用户已关闭键盘灯，不重放效果");
			}
			Task.Run(async () =>
			{
				_ = 1;
				try
				{
					await Task.Delay(1200).ConfigureAwait(continueOnCapturedContext: false);
					await RestoreLightingWithRetryAsync(force: true).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("RGB 唤醒恢复失败: " + ex.Message);
				}
				finally
				{
					Volatile.Write(ref _resumeKeyboardRestorePending, 0);
				_kbPowerBeforeSuspend = -1;
				}
			});
		}
	}

	public static void SettingsToggle(bool checkForFocus = true, bool trayClick = false)
	{
		if (settingsForm.Visible)
		{
			if (checkForFocus && !settingsForm.HasAnyFocus(trayClick))
			{
				settingsForm.ShowAll();
			}
			else
			{
				settingsForm.HideAll();
			}
			return;
		}
		Screen screen = Screen.FromRectangle(settingsForm.Bounds);
		settingsForm.WindowState = FormWindowState.Normal;
		// RunDeferredFanStartupCleanup（风扇闸门只读验证 + 启动风扇清理）**已移出**本方法 ⇒
		long openStartTick = Environment.TickCount64;
		settingsForm.VisualiseGPUMode();
		settingsForm.ShowAll(screen.WorkingArea);
		long openMs = Environment.TickCount64 - openStartTick;
		if (Logger.TryMarkChanged("settings-open-timing", "[init] 设置窗打开耗时=" + openMs + " ms（只含 UI 构建，后端初始化未在此执行）"))
		{
			Logger.WriteError("[init] 设置窗打开耗时=" + openMs + " ms（只含 UI 构建，后端初始化未在此执行）");
		}
		if (_showGuideOnFirstRestore)
		{
			_showGuideOnFirstRestore = false;
			// 2026-10-09（igpuask-once）：这两个 BeginInvoke 会落在同一轮消息泵里，而第一个 ShowDialog 是
			// 嵌套消息循环 —— 第二个委托会在第一个对话框还开着的时候执行。核显模式那一问的"只弹一次"
			// 由 SettingsForm.TryClaimIgpuOnlyGuide（显示前先写 igpu_only_asked=1，内存位 + 落盘）保证。
			settingsForm.BeginInvoke(settingsForm.ShowFirstRunGuideIfNeeded);
			// 2026-10-05「核显模式显示策略」⑤：同上，升级用户单独问一次
			settingsForm.BeginInvoke(settingsForm.ShowIgpuOnlyGuideIfNeeded);
		}
	}

	private static int _backendWarmupScheduled;

	private static readonly CancellationTokenSource _backendWarmupCts = new CancellationTokenSource();
	// <para> 不在 UI 线程同步执行、 不挂在设置窗打开路径；每步独立 try/catch，失败只记日志并**标记失败、不重试** </para>
	internal static void ScheduleIdleBackendWarmup()
	{
		if (Interlocked.Exchange(ref _backendWarmupScheduled, 1) != 0)
		{
			return;
		}
		_ = Task.Run(async () =>
		{
			long warmStart = Environment.TickCount64;
			try
			{
				Logger.WriteError("[init] 启动空闲预热开始（后台，不阻塞 UI）");
				Logger.WriteError("[init] 启动预热顺序：风扇清理 → 摄像头 → 能力刷新 → 模式对账 → 无界重放");
				await Task.Delay(2500, _backendWarmupCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				MechrevoHw warmHw = hw;
				if (warmHw == null)
				{
					return;
				}
				// （门控审计）：启动自检一行，明确本机启用了哪些机型相关分支
				try
				{
					bool gen30 = warmHw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30;
					bool amd = MechrevoLite.Hardware.CpuVendor.IsAmd;
					bool noDgpu = warmHw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.NoDgpu;
					bool nvidia = !noDgpu && warmHw.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.Unknown;
					bool igpuPower = amd && noDgpu;
					// （机主：宝龙达被判成同方）： gate 行加入板号与命中来源
				// 第 4 步（声明式架构）：[gate] 与 [router] 共用同一份 MachineIdentity 事实，避免两套判据不一致。
				MechrevoLite.Hardware.MachineIdentity gateIdentity = MechrevoLite.Hardware.MachineIdentity.Collect();
				MechrevoLite.Hardware.BackendRouteDecision gateRoute = MechrevoLite.Hardware.BackendRouter.Decide(gateIdentity);
				Logger.WriteError("[gate] 机型分类：" + gateIdentity.Summary
					+ " · baolongda=" + warmHw.UsesBaolongda + " gen=" + warmHw.DgpuGeneration
					+ " wujieApu=" + warmHw.WujieSmuApu + " amd=" + amd + " nvidia=" + nvidia
					+ " | 强化机型分流=" + gateRoute.Backend + " rule=#" + gateRoute.RuleIndex
					+ " | 启用分支：kbdEc=" + (warmHw.UsesBaolongda && gen30)
					+ " wujieSmu=" + warmHw.WujieSmuApu
					+ " igpuPower=" + igpuPower
					+ " quickExtra=" + warmHw.WujieSmuApu);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[gate] 机型分类自检失败（已吞掉）：" + ex.Message);
				}
				long fanStart = Environment.TickCount64;
				try
				{
					warmHw.RunDeferredFanStartupCleanup();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[init] 后台初始化：风扇闸门/清理失败（已吞掉，标记失败不重试）：" + ex.Message);
				}
				Logger.WriteError("[init] 后台初始化：风扇闸门 ok=" + warmHw.FanCurveVerified + "（耗时 " + (Environment.TickCount64 - fanStart) + " ms）");
				// ⭐⭐ 2026-10-18（机主：关机后阈值还在不在）：⭐ 启动空闲预热里核对并恢复宝龙达限充配置 ✅
				try
				{
					MechrevoLite.Hardware.BatteryLimitRestore.RunOnce("启动空闲预热");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[init] 后台初始化：电池阈值核对失败（已吞掉，不重试）：" + ex.Message);
				}
				if (_backendWarmupCts.IsCancellationRequested)
				{
					return;
				}
				if (warmHw.UsesBaolongda)
				{
					long camStart = Environment.TickCount64;
					bool cameraFound = false;
					try
					{
						cameraFound = MechrevoLite.Hardware.CameraPnpControl.ProbeHasCamera();
					}
					catch (Exception ex)
					{
						Logger.WriteLine("[init] 后台初始化：摄像头枚举失败（已吞掉，标记失败不重试）：" + ex.Message);
					}
					Logger.WriteError("[init] 后台初始化：摄像头枚举 ok=" + cameraFound + "（耗时 " + (Environment.TickCount64 - camStart) + " ms）");
				}
				// 每步失败只记日志、继续后续；尤其不能因 GCU 未连接跳过重放
				SettingsForm warmForm = settingsForm;
				if (warmForm != null && !warmForm.IsDisposed && warmForm.IsHandleCreated)
				{
					try
					{
						warmForm.Invoke((Action)warmForm.RefreshDeviceCapabilities);
					}
					catch (Exception ex)
					{
						Logger.WriteLine("[init] 后台初始化：能力刷新失败（已吞掉，继续后续）：" + ex.Message);
					}
				}
				PerfModeService warmPerf = PerfModeService.Instance;
				if (warmPerf != null)
				{
					try
					{
						await warmPerf.ReconcileForStartupAsync().ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (Exception ex)
					{
						Logger.WriteLine("[init] 后台初始化：模式对账失败（已吞掉，继续无界重放）：" + ex.Message);
					}
				}
				if ((warmHw.WujieSmuApu || warmHw.UsesBaolongda) && warmPerf != null)
				{
					try
					{
						await warmPerf.ReplayLocalProfileOnStartupAsync().ConfigureAwait(continueOnCapturedContext: false);
			MechrevoLite.Hardware.BatteryVoltageFix.RunOnStartupIfEnabled();
				warmPerf.ApplyApuAntiDegradeOnStartup();
				try
				{
					int warmVisual = MechrevoService.ToVisualMode(Program.service?.CurrentMode ?? 0);
					MechrevoLite.Hardware.GpuDbControl.SyncForVisualMode(warmVisual, "启动同步");
				}
				catch (Exception dbEx)
				{
					Logger.WriteLine("[db] 启动同步失败（已吞掉）：" + dbEx.Message);
				}
					}
					catch (Exception ex)
					{
						Logger.WriteError("[perfmode] 无界 APU 启动恢复异常（已吞掉、不重试）：" + ex.Message);
					}
					// （机主：重启后高亮回平衡）：重放后**显式**刷一次性能模式行/上下文菜单（UI 线程）
					try
					{
						SettingsForm refreshForm = settingsForm;
						if (refreshForm != null && !refreshForm.IsDisposed && refreshForm.IsHandleCreated)
						{
							refreshForm.BeginInvoke((Action)(() =>
							{
								if (!refreshForm.IsDisposed)
								{
									refreshForm.ShowMode(4);
									refreshForm.RefreshQuickSwitchStatesNow();
								}
							}));
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine("[perfmode] 无界 APU 重放后刷新界面失败（已吞掉）：" + ex.Message);
					}
				}

				SettingsForm form = settingsForm;
				if (form != null && !form.IsDisposed && form.IsHandleCreated)
				{
					try
					{
						form.BeginInvoke((Action)(() =>
						{
							if (!form.IsDisposed)
							{
								form.RefreshDeviceCapabilities();
								form.RefreshQuickSwitchStatesNow();
							}
						}));
					}
					catch (Exception ex)
					{
						Logger.WriteLine("[init] 后台初始化后刷新界面失败（已吞掉，不影响 UI）：" + ex.Message);
					}
				}
			}
			catch (OperationCanceledException)
			{
				Logger.WriteLine("[init] 启动空闲预热已取消（退出中）");
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[init] 后台初始化失败（已吞掉，不重试）：" + ex);
			}
			finally
			{
				Logger.WriteError("[init] 启动空闲预热完成（总耗时 " + (Environment.TickCount64 - warmStart) + " ms）");
			}
		});
	}


	private static void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			SettingsToggle(checkForFocus: true, trayClick: true);
		}
	}

	private static void TrayIcon_MouseMove(object? sender, MouseEventArgs e)
	{
		if (Interlocked.Exchange(ref _trayRefreshInFlight, 1) != 0)
		{
			return;
		}
		Task.Run(() =>
		{
			try
			{
				settingsForm?.RefreshSensors();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Tray sensor refresh failed: " + ex.Message);
			}
			finally
			{
				Interlocked.Exchange(ref _trayRefreshInFlight, 0);
			}
		});
	}

	internal static void TryRestoreScreenAfterFailure()
	{
		try
		{
			VirtualDisplayBlank.RestoreImmediate();
		}
		catch (Exception)
		{
		}
	}

	private static void OnExit(object sender, EventArgs e)
	{
		if (Interlocked.Exchange(ref _exitStarted, 1) == 0)
		{
			// （机主定稿 · 启动空闲预热可取消）：退出时取消仍在排队的预热（幂等）
			try
			{
				_backendWarmupCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			TryRestoreScreenAfterFailure();
			_telemetryRecoveryTimer?.Dispose();
			_telemetryRecoveryTimer = null;
			_lightingIdleTimer?.Dispose();
			_lightingIdleTimer = null;
			powerSettleTimer.Stop();
			powerSettleTimer.Dispose();
			_trayRetryTimer?.Stop();
			_trayRetryTimer?.Dispose();
			_trayRetryTimer = null;
			settingsForm.StopRuntimeTimers();
			hardwareOverlay?.StopOverlay();
			hardwareOverlay?.Dispose();
			rgb?.Dispose();
			ble?.Dispose();
			// 2026-10-09（NVML 原生崩溃修复）：退出时走统一入口 —— 先断开 lhm 引用再 Dispose，
			// 并置 LhmMonitor._disposed，保证退出过程中残留的定时器 tick 不会在监控对象释放后继续采样。
			HardwareControl.DisableLocalMonitoring();
			try
			{
				UsageTelemetry.Stop();
			}
			catch
			{
			}
			StopTelemetryBeforeExit();
			if (hw != null)
			{
				hw.StateChanged -= OnHardwareStateChanged;
			}
			hw?.Dispose();
			modeControl?.Dispose();
			RuntimeCpuTuning.Instance.Dispose();
			OfficialConsoleIsolation.StopGuard();
			if (trayIcon != null)
			{
				Icon? icon = trayIcon.Icon;
				trayIcon.Icon = null;
				trayIcon.Visible = false;
				trayIcon.Dispose();
				icon?.Dispose();
			}
			PeripheralsProvider.UnregisterForDeviceEvents();
			NativeMethods.UnregisterPowerSettingNotification(unRegPowerNotify);
			NativeMethods.UnregisterPowerSettingNotification(unRegPowerNotifyLid);
			NativeMethods.UnregisterPowerSettingNotification(unRegPowerNotifyEnergy);
			NativeMethods.UnregisterSuspendResumeNotification(unRegSuspendResume);
			// 2026-10-05 uifix（用户要求④）：正常退出（托盘退出 / 重启软件 / 会话结束走到这里）
			// 写「干净退出」标记，供下次开机的保底机制判定。必须在 AppConfig.Shutdown() 之前，
			// 否则配置已经落盘关闭。崩溃 / 断电 / 被任务管理器结束都走不到这一行 -> 没有标记。
			CpuCoAutoApplyGuard.MarkCleanExit("ApplicationExit");
			// 2026-10-10（用户反馈 ③）：同上 —— 托盘退出/正常重启也算干净退出，不能触发显卡超频自关。
			GpuOcShutdownGuard.MarkCleanExit("ApplicationExit");
			AppConfig.Shutdown();
			Logger.Close();
		}
	}

	private static void StopTelemetryBeforeExit()
	{
		MechrevoHw mechrevoHw = hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：下一行直接 Publish(System/Control) 且**必须送达 GCU**才能恢复厂商自动控制
		// ⇒ 真正需要 GCU（老接口机型不该把这件事误标成"已发出"）。
		if (mechrevoHw == null || !mechrevoHw.IsGcuConnected)
		{
			return;
		}
		try
		{
			Task task = mechrevoHw.Publish("System/Control", new Dictionary<string, object> { ["Action"] = "System_OFF" }, MqttQualityOfServiceLevel.AtMostOnce);
			if (!task.Wait(TimeSpan.FromMilliseconds(700L)))
			{
				Logger.WriteLine("System_OFF timed out on exit; skipping.");
				task.ContinueWith((Task t) => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("System_OFF on exit failed: " + ex.Message);
		}
	}

	/// <summary>2026-10-22（无界电池电压修复）：SYSTEM 开机任务常驻 —— 启动内置保持循环后不退出，断电重启由任务重跑。</summary>
	private static int RunBatteryFixDaemon()
	{
		try
		{
			if (!MechrevoLite.Hardware.BatteryVoltageFix.Enabled)
			{
				Logger.WriteLine("[battery-fix] 开机任务：功能未启用 ⇒ 退出");
				return 0;
			}
			string model = MechrevoDeviceCapabilities.Current.Model ?? "";
			bool wujie = model.IndexOf("WUJIE", StringComparison.OrdinalIgnoreCase) >= 0
				|| model.IndexOf("无界", StringComparison.OrdinalIgnoreCase) >= 0;
			if (!wujie)
			{
				Logger.WriteLine("[battery-fix] 开机任务：非无界机型（model=" + model + "）⇒ 退出");
				return 0;
			}
			while (true)
			{
				MechrevoLite.Hardware.InpOutDriverInstaller.TryEnsureInstalled(out string drvDetail);
				if (MechrevoLite.Hardware.I2ecInpOut.TryStart(out string startDetail))
				{
					Logger.WriteLine("[battery-fix] 开机任务已启动内置保持：" + startDetail + " · 驱动=" + drvDetail);
					break;
				}
				Logger.WriteLine("[battery-fix] 开机任务启动失败：" + startDetail + " ⇒ 10 秒后重试");
				Thread.Sleep(10000);
			}
			// 常驻：I2ecInpOut 内部线程每 2 秒保持；本进程不退出，任务被杀/重启时由下次开机重跑。
			while (true)
			{
				Thread.Sleep(30000);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 开机任务异常：" + ex.GetType().Name + " " + ex.Message);
			return 1;
		}
	}

	private static async Task<bool> ApplyBatteryLimitAtBootAsync()
	{
		try
		{
			// 2026-10-10（限充门控 · 用户原话「1补 2不动 3改完」「另能不能想办法修了」）：本方法是 SYSTEM
			// 计划任务 `charge`（开机 + 内核启动事件触发，见 Startup.ScheduleCharge）的入口 —— 改动前
			// **无条件下发 EC** 0x7B9。判据复用既有单一真源 BatteryControl.ReestablishArmed()
			// （= charge_limit_lock==1 || charge_limit_slider==1；与 EC 侧重确立保护、界面侧
			// ChargeLimitEcPathActive 同源）⇒ 两个开关都关 ⇒ EC 直写方式未生效（充电上限由 GCU 官方路径
			// 负责）⇒ ⛔ 一个字节都不写 EC（TrySet 根本不会被调用 ⇒ 连只读的 0x7D0 观察任务都不会排）。
			// 返回 true（= 退出码 0）：这是「无事可做」的正常跳过，不是失败。
			if (!MechrevoLite.Battery.BatteryControl.ReestablishArmed())
			{
				Logger.WriteLine($"BatteryLimit skipped: EC 限充(charge_limit_lock)与无极限制充电滑块(charge_limit_slider)都是关 ⇒ EC 直写方式未生效，本次不写 EC（0x{EcChargeLimit.UpperRegister:X3} 保持原值）");
				return true;
			}
			int limit = AppConfig.Get("charge_limit", 100);
			if (!EcChargeLimit.IsSupportedLimit(limit))
			{
				Logger.WriteLine($"BatteryLimit skipped: charge_limit={limit} out of range 40-100");
				return false;
			}
			if (!EcChargeLimit.IsAvailableOnThisMachine())
			{
				Logger.WriteLine($"BatteryLimit skipped: EC charge limit unavailable on this machine (charge_limit={limit})");
				return false;
			}
			await Task.Yield();
			// 2026-10-08（限充开关）：统一走 EC 直写通道（0x7B9 上限）。
			// 2026-10-10（对齐上游）：复充下限 0x7D0 **不再写入**（交由固件 store_limit() 推导，只读观察）。
			// 旧实现把百分比粗化成三档（limit<95 ? (limit>=80 ? 1 : 2) : 0）再交给 GCU(MQTT)：
			// 既会把用户设的 72% 悄悄变成 60%，又在 GCU 被接管/禁用（本机现状）时彻底失效。
			if (!EcChargeLimit.TrySet(limit, out int appliedPercent))
			{
				Logger.WriteLine($"BatteryLimit not confirmed: request={limit}% ecReadback={EcChargeLimit.ReadPercent()}%");
				return false;
			}
			Logger.WriteLine($"BatteryLimit confirmed: request={limit}% applied={appliedPercent}% ec=0x{EcChargeLimit.UpperRegister:X3}（0x{EcChargeLimit.LowerRegister:X3} 只读、不写）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("BatteryLimit fail: " + ex.Message);
			return false;
		}
	}

	private static void CleanupLegacyFiles()
	{
		string path = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
		string[] array = new string[2] { "WinRing0x64.sys", "WinRing0x64.dll" };
		foreach (string text in array)
		{
			string path2 = Path.Combine(path, text);
			if (File.Exists(path2))
			{
				try
				{
					File.Delete(path2);
					Logger.WriteLine("Deleted legacy file: " + text);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Failed to delete legacy file " + text + ": " + ex.Message);
				}
			}
		}
	}
}
