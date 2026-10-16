using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.UI;

internal sealed class Fixture
{
	public string Name;

	public Func<MechrevoDeviceCapabilities> Caps;

	public Func<Form> Make;

	public bool IsSettings;

	public Action<Form, MechrevoHw> After;
}

internal static class Program
{
	[DllImport("kernel32.dll")]
	private static extern uint SetErrorMode(uint uMode);

	private static readonly StringBuilder Log = new StringBuilder();

	private static readonly List<string[]> Rows = new List<string[]>();

	private static string _outDir;

	private static string _suffix = "egroup";

	private static string _shotsDir;

	private static string[] _fixtureFilter;

	private static string[] _viewportFilter;

	private static bool _shots = true;

	private static Assembly _asm;

	private static Type _auditType;

	private static Type _vpType;

	private static Type _issueType;

	private static Type _probeType;

	private static Type _optionType;

	private static Type _uiDpiType;

	[STAThread]
	private static void Main(string[] args)
	{
		// 2026-10-12（自包含 / 「隐形」）：⛔ 绝不弹模态框。
		//   * SetErrorMode(0x8003) = SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX
		//     ⇒ 任何加载/致命错误都只走返回值，⛔ 不弹系统错误框、⛔ 不进 WER。
		//   * 下面开始的 try/catch/finally 把整个渲染流程包住：异常一律写 stdout/stderr + 退出码。
		try
		{
			SetErrorMode(0x8003u);
			RunMain(args);
		}
		catch (Exception ex)
		{
			// 「隐形」失败路径：报告写 stdout/stderr + 退出码 + 根目录日志，⛔ 不弹任何框、⛔ 不写用户桌面。
			Environment.ExitCode = 1;
			Environment.SetEnvironmentVariable("EGROUPSHOT_FAILED", "1");
			Log.AppendLine();
			Log.AppendLine("FATAL " + ex);
			Fail(ex);
			throw;
		}
		finally
		{
			// 「用完即退」：⛔ 不留在后台、⛔ 不进 WER 等待。
			Flush();
			Environment.Exit(Environment.ExitCode);
		}
	}

	private static void RunMain(string[] args)
	{
		_outDir = ((args.Length > 0) ? args[0] : @"D:\openLM\diagnostics");
		if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
		{
			_suffix = args[1];
		}
		_fixtureFilter = ((args.Length > 2 && !string.IsNullOrEmpty(args[2])) ? args[2].Split(',') : null);
		_viewportFilter = ((args.Length > 3 && !string.IsNullOrEmpty(args[3])) ? args[3].Split(',') : null);
		_shots = !(args.Length > 4 && string.Equals(args[4], "noshots", StringComparison.OrdinalIgnoreCase));
		Directory.CreateDirectory(_outDir);
		_shotsDir = Path.Combine(_outDir, "shots-" + _suffix);
		if (_shots)
		{
			Directory.CreateDirectory(_shotsDir);
		}
		// 配置隔离：全部读写都落在这个临时文件上（绝不碰用户真实配置）。
		string configPath = Path.Combine(_outDir, "tmp-cfg-" + _suffix + ".json");
		if (!File.Exists(configPath))
		{
			File.WriteAllText(configPath, "{\r\n  \"performance_mode\": 0,\r\n  \"igpu_only_asked\": 1,\r\n  \"igpu_only_ui\": \"show\",\r\n  \"igpu_attempt_pending\": -1,\r\n  \"dgpu_generation\": \"Gen40\",\r\n  \"dgpu_generation_source\": \"MarketingName\",\r\n  \"dgpu_has_dgpu\": \"1\"\r\n}\r\n", new UTF8Encoding(false));
		}
		Environment.SetEnvironmentVariable("LMECHREVO_CONFIG_FILE", configPath);
		// 日志同样隔离：产品 Logger 默认写 %APPDATA%\MechrevoLite\log.txt，
		// 不隔离的话渲染真实窗体会把工具自己的行混进 LM 日志（2026-10-06 修）。
		string logDir = Path.Combine(_outDir, "tool-logs-" + _suffix);
		Directory.CreateDirectory(logDir);
		Environment.SetEnvironmentVariable("LMECHREVO_LOG_DIR", logDir);
		Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
		Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		_asm = typeof(MechrevoHw).Assembly;
		_auditType = _asm.GetType("MechrevoLite.UI.UiAuditRunner");
		_vpType = _auditType.GetNestedType("Viewport", BindingFlags.NonPublic);
		_issueType = _auditType.GetNestedType("AuditIssue", BindingFlags.NonPublic);
		_probeType = _asm.GetType("MechrevoLite.Hardware.CpuTuningProbe");
		_optionType = _asm.GetType("MechrevoLite.Hardware.CpuTuningOption");
		_uiDpiType = _asm.GetType("MechrevoLite.UI.UiDpi");
		Log.AppendLine("exe         = " + Environment.ProcessPath);
		Log.AppendLine("suffix      = " + _suffix);
		Log.AppendLine("configFile  = " + configPath);
		Log.AppendLine("auditType   = " + ((_auditType != null) ? "OK" : "MISSING"));
		Log.AppendLine("AuditForm   = " + ((_auditType?.GetMethod("AuditForm", BindingFlags.Static | BindingFlags.NonPublic) != null) ? "OK" : "MISSING"));
		Log.AppendLine("GetClipping = " + ((_auditType?.GetMethod("GetTextClipping", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null) ? "OK" : "MISSING"));
		Log.AppendLine("probeType   = " + ((_probeType != null) ? "OK" : "MISSING"));
		// 只读 + 不碰硬件（与产品 --ui-audit 同一套开关，内部静态 -> 反射置位）
		SetStatic("MechrevoLite.Program", "UiAuditMode", true);
		Log.AppendLine("hostDpi     = " + ReadHostDpi());
		Log.AppendLine();
		Stopwatch stopwatch = Stopwatch.StartNew();
		int combos = 0;
		foreach (ViewportSpec viewport in Viewports())
		{
			if (!Match(viewport.Id, _viewportFilter))
			{
				continue;
			}
			foreach (Fixture fixture in Fixtures())
			{
				if (!Match(fixture.Name, _fixtureFilter))
				{
					continue;
				}
				combos++;
				RunCombo(viewport, fixture);
			}
		}
		Log.AppendLine();
		Log.AppendLine($"=== 合计 {combos} 个 (夹具 × 视口) 组合，用时 {stopwatch.Elapsed.TotalSeconds:F1}s ===");
		WriteSummary();
		Flush();
	}
	// ==========================================================================================
	// 视口矩阵：DPI 100 / 125 / 150 / 200% × (1920x1080, 小屏 1366x768)
	// ==========================================================================================
	private sealed record ViewportSpec(int Width, int Height, int Dpi)
	{
		public string Id => $"{Width}x{Height}-{Dpi * 100 / 96}pct";

		public Rectangle WorkingArea => new Rectangle(0, 0, Width, Height - (int)Math.Round(48f * (float)Dpi / 96f));
	}

	private static ViewportSpec[] Viewports()
	{
		return new ViewportSpec[8]
		{
			new ViewportSpec(1920, 1080, 96),
			new ViewportSpec(1920, 1080, 120),
			new ViewportSpec(1920, 1080, 144),
			new ViewportSpec(1920, 1080, 192),
			new ViewportSpec(1366, 768, 96),
			new ViewportSpec(1366, 768, 120),
			new ViewportSpec(1366, 768, 144),
			new ViewportSpec(1366, 768, 192)
		};
	}

	private static bool Match(string name, string[] filter)
	{
		if (filter == null || filter.Length == 0)
		{
			return true;
		}
		foreach (string text in filter)
		{
			if (!string.IsNullOrWhiteSpace(text) && name.IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	// ==========================================================================================
	// 夹具：能力位 / 探针一律用合成 fixture（不连 GCU、不读 SMU、不碰 PawnIO 驱动）
	// ==========================================================================================
	private static Fixture[] Fixtures()
	{
		return new Fixture[15]
		{
			new Fixture
			{
				// 2026-10-12（本轮 ·「显卡超频」开关取证专用）：本机 GCU 变体 = 40 ⇒ 产品判据
				// `MechrevoService.IsGcu50`（读 HKLM\SOFTWARE\L-Mechrevo\GcuVariant）恒假，
				// 于是 `_gpuOcBox`（SettingsForm.cs:6002 建的 RCheckBox#gpu_oc）**永不出现**。
				// 本夹具在**宿主自己的进程内**把该属性的判定旁路成真（⛔ 不写注册表、⛔ 不动产品 DLL），
				// 并注入一份只读的合成 `Program.service`（OperatingMode=1 ⇒ CurrentMode==1），
				// 让 `Program.service != null && CurrentMode == 1 && hw != null && IsGcu50` 四项全真 ⇒
				// 该开关按产品的**原样**渲染出来（产品一个字节未改，只是它读到的判据在宿主里被改写）。
				Name = "Settings-GpuOc",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsGpuOcAfter,
				Caps = () => RichCaps(igpuOnly: true)
			},
			new Fixture
			{
				Name = "Settings-Full",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsAfter,
				Caps = () => RichCaps(igpuOnly: true)
			},
			new Fixture
			{
				Name = "Settings-Guess",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsAfter,
				Caps = () => RichCaps(igpuOnly: false)
			},
			new Fixture
			{
				Name = "Settings-Minimal",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsAfter,
				Caps = () => new MechrevoDeviceCapabilities
				{
					ProfileAvailable = true,
					TurboMode = false,
					DgpuDirect = false,
					IgpuOnly = false
				}
			},
			new Fixture
			{
				// 2026-10-11 布局取证专用：喂合成传感器值，让「性能模式 / 显卡模式」行右端的
				// CPU/GPU 读数显示**完整数值**（温度 + 功耗 + 转速 + 占空比），供截图与几何核对用。
				Name = "Settings-Telemetry",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsTelemetryAfter,
				Caps = () => RichCaps(igpuOnly: true)
			},
			new Fixture
			{
				// 2026-10-11（快捷磁贴行取证专用）：默认磁贴集里除 micmute 外的四个键
				//（一键强冷 fanboost / 摄像头 webcam / Win键锁 winkey / 息屏 deepsleep）在
				// Settings-Full 那套夹具里都因为"没上报过状态"被判成不支持 ⇒ 磁贴行只剩一格。
				// 本夹具补喂对应的状态报文（Setting/Status + Fan/Status），让这四把开关真正"存在"，
				// 于是磁贴行按默认集渲染出 5 动作 + 「＋」——这才是产品默认形态。
				// ⚠️ 只喂状态报文（= 让能力位为真），⛔ 不写任何硬件/配置。
				// 2026-10-12（用户原话「触摸板切换键是干啥用的 也用触摸板图标吧」）：补喂 <c>TouchpadToggle</c>
				// （⛔ 只加这一个字段，其余一字未改）—— 判据是 <c>MechrevoHw.SupportsQuickSwitch("touchpadtoggle")</c>
				// = <c>TouchpadToggleSeen</c>（MechrevoHw.cs:2055），而它由 **Setting/Status** 的
				// <c>TouchpadToggle</c> 字段置位（MechrevoHw.cs:4027-4032）⇒ 不喂则该磁贴不上屏、拍不到字形。
				Name = "Settings-Tiles",
				IsSettings = true,
				Make = () => new SettingsForm(),
				After = SettingsTilesAfter,
				Caps = () => RichCaps(igpuOnly: true)
			},
			new Fixture
			{
				Name = "SettingsDialog",
				Make = MakeSettingsDialog
			},
			new Fixture
			{
				Name = "CustomMode",
				Caps = () => new MechrevoDeviceCapabilities
				{
					ProfileAvailable = true,
					CpuPerformanceTuning = true,
					OverclockSettings = true,
					FanSettings = true
				},
				Make = () => new CustomModeForm(),
				After = delegate(Form form, MechrevoHw hw)
				{
					SendHw(hw, "Fan/Status", "{\"GPU_CoreClockOffsetMinimumHWOC\":-500,\"GPU_CoreClockOffsetMaximumHWOC\":500,\r\n \"GPU_MemoryClockOffsetMinimumHWOC\":-500,\"GPU_MemoryClockOffsetMaximumHWOC\":500,\r\n \"GPU_CoreClockOffsetOC\":500,\"GPU_MemoryClockOffsetOC\":-500,\"OverClockingSwitch\":1}");
					SendHw(hw, "LCHWOC/Status", "{\"Support\":true,\"Enable\":true}");
				}
			},
			new Fixture
			{
				Name = "CpuTune-AMD-PerCore8",
				Make = () => MakeCpuTuning(MakeProbe("AMD Ryzen 7 8700G (desktop, fixture)", "CpuTuneSmuReady", "Vermeer; SMU 0x545601; CO readback per core [-30, -30, -30, -30, -30, -30, -30, -30] (SMU current state, this boot only)", new object[1]
				{
					MakeOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", -30m, canWrite: true, new int[8] { -30, -30, -30, -30, -30, -30, -30, -30 }, null, perCoreWrite: true)
				}))
			},
			new Fixture
			{
				Name = "CpuTune-AMD-NoPerCore",
				Make = () => MakeCpuTuning(MakeProbe("AMD Ryzen 7 8745H (APU, fixture)", "CpuTuneSmuReady", "Hawk Point; SMU 0x4C0000; CO readback per core [-15, -15, -15, -15, -15, -15, -15, -15]", new object[1]
				{
					MakeOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", -15m, canWrite: true, new int[8] { -15, -15, -15, -15, -15, -15, -15, -15 }, null, perCoreWrite: false)
				}))
			},
			new Fixture
			{
				Name = "CpuTune-AMD-NoReadCmd",
				Make = () => MakeCpuTuning(MakeProbe("AMD Ryzen 7 5800H (fixture)", "CpuTuneSmuReady", "Cezanne; SMU 0x4C0000; CO readback: no read command for this codename", new object[1]
				{
					MakeOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", null, canWrite: true, null, "CpuTuneNoReadCmd", perCoreWrite: false)
				}))
			},
			new Fixture
			{
				Name = "CpuTune-Intel",
				Make = () => MakeCpuTuning(MakeProbe("Intel Core i7-13700H (fixture)", "CpuTuneIntelReady", "domain 0: capabilities=0x540, ratio=40/64", new object[3]
				{
					MakeOption("intel-core", "CpuTuneCore", -150m, 0m, "mV", -50m, canWrite: true, null, null, perCoreWrite: false),
					MakeOption("intel-cache", "CpuTuneCache", -150m, 0m, "mV", -50m, canWrite: true, null, null, perCoreWrite: false),
					MakeOption("intel-ratio", "CpuTuneRatio", 8m, 64m, "x", 40m, canWrite: true, null, null, perCoreWrite: false)
				}))
			},
			new Fixture
			{
				Name = "CpuTune-NoDriver",
				Make = () => MakeCpuTuning(MakeProbe("CPU (fixture)", "CpuTuneDriverMissing", "NotInstalled", Array.Empty<object>()))
			},
			new Fixture
			{
				Name = "FanCurve",
				Caps = () => new MechrevoDeviceCapabilities
				{
					ProfileAvailable = true,
					FanSettings = true
				},
				Make = () => new FanCurveForm(),
				After = delegate(Form form, MechrevoHw hw)
				{
					SendHw(hw, "Fan/Status", "{\"FanControlRespective\":true}");
				}
			},
			new Fixture
			{
				Name = "FirstRunGuide",
				Make = () => new FirstRunGuideForm()
			}
		};
	}

	private static void SettingsAfter(Form form, MechrevoHw hw)
	{
		// 显卡行（含 D-1 的「本机可能不支持，可试一次」提示）平时是 GCU 状态变化时刷新的；
		// fixture 场景没有那些事件，显式调一次同一个公开方法（只读：它只读状态 + 画界面）。
		try
		{
			Invoke(form, "VisualiseGPUMode", -1);
			Application.DoEvents();
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! VisualiseGPUMode failed: " + ex.Message);
		}
	}

	// 2026-10-11 布局取证：把**合成传感器值**喂进 fixture hw，让性能行右端的 CPU/GPU 读数显示
	// 完整数值（温度 + 功耗 + 转速 + 占空比），再让窗体按新值刷新一次读数文本（只读路径：UpdateTelemetryText
	// 只读 hw + 改自己的标签文本，⛔ 不写任何硬件/配置）。
	//   * 默认那一组 = 用户第一张真机截图上的量级（58°C / 12W 2539rpm 35%）。
	//   * `LMECHREVO_SHOT_TELEMETRY=user2` = 用户第二张截图上的那一组（27W 2095rpm / 5W 1857rpm）——
	//     它比默认那组**更宽**（两位数功耗），正是"列宽不够 → 省略号"那一档。
	// ⚠️ 功耗(W)是 HardwareControl 的静态字段、不走 hw 消息，必须直接置位才会出现在读数里
	//（这正是最早一版取证"复现不出真机坏态"的原因之一：旧夹具只喂温度+转速，读数比真机短）。
	// 2026-10-11（快捷磁贴行取证）：把默认磁贴对应的快捷开关"喂成存在"。
	//   * Setting/Status 里的 WinKey / WebCamEnable / DeepSleepSwitch → WinKeySeen/WebcamSeen/DeepSleepSeen；
	//   * Fan/Status 里的 FanBoostEnable → FanBoostSeen（一键强冷）。
	// 喂完之后再让窗体按新的能力组合重算一次可见性（RefreshDeviceCapabilities 是只读路径：
	// 它只读 hw + 改自身 Visible/高度，⛔ 不写硬件/配置）。
	private static void SettingsTilesAfter(Form form, MechrevoHw hw)
	{
		SettingsAfter(form, hw);
		SendHw(hw, "Setting/Status", "{\"WinKey\":\"LOCK\",\"DeepSleepSwitch\":\"ON\",\"FnKey\":\"UNLOCK\",\"NumPad\":\"UNLOCK\",\"OSD\":\"ON\",\"AcRecoverySwitch_Status\":\"ON\",\"TouchpadToggle\":\"ON\"}");
		// 触控板 / WiFi / 蓝牙 / 摄像头走**另一个** topic（OnSettingsDeviceSwitchItemStatus）。
		// 2026-10-12（亮度滑块 0 可达取证）：LMECHREVO_SHOT_BRIGHT=0 时把上报亮度也喂 0，
		// 让"回读同步 + 数值标签"这条路径一起被拍到（⛔ 只动夹具报文）。
		int fixtureBrightness = (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_BRIGHT"), "0", StringComparison.OrdinalIgnoreCase) ? 0 : 100);
		SendHw(hw, "Settings/DeviceSwitchItemStatus", "{\"WebCamEnable\":true,\"Touchpad\":\"ON\",\"WIFIEnable\":true,\"BTEnable\":true,\"ScreenBrightness\":" + fixtureBrightness + "}");
		SendHw(hw, "Fan/Status", "{\"FanBoostEnable\":false,\"IsAC\":false}");
		try
		{
			Invoke(form, "RefreshDeviceCapabilities");
			Application.DoEvents();
			Invoke(form, "RefreshDeviceCapabilities");
			Application.DoEvents();
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! RefreshDeviceCapabilities failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-12（本轮 ·「显卡超频」开关取证）：<c>After</c> 阶段做两件事 ——
	/// ① 掉一次 <c>gpu_oc</c> 的选中态（<c>LMECHREVO_SHOT_GPUOC=1</c> 时置开，否则保持默认关），
	///    并让产品自己那条 <c>CheckedChanged</c> 路径重画（⛔ 不下发 <c>ApplyGpuOverclock</c>：本条被
	///    <c>Program.service/hw.IsConnected</c> 拦着，夹具 hw 从未连接）；
	/// ② 直接读控件本身把外观事实写进日志（供"效果"报告与几何核对）。
	/// </summary>
	private static void SettingsGpuOcAfter(Form form, MechrevoHw hw)
	{
		SettingsAfter(form, hw);
		// ⚠️ 构造期 `_gpuOcBox.Visible` 算的是 `Program.service != null && CurrentMode == 1`，而此时
		//    `Program.service` 还没有（产品里它是启动时造的）⇒ 构造期必然判假。产品的**第二次**显隐机会
		//    在 `VisualiseMode(mode)`（SettingsForm.cs:10789，判据只看 `mode == 1 && hw != null && IsGcu50`）。
		//    `ShowMode(1)` 就是产品自己"切到平衡模式"时走的那个公开入口 ⇒ 用**产品原路径**再刷一次显隐，
		//    ⛔ 不点按钮、⛔ 不下发、⛔ 不改判据（等价于真机上用户切到平衡模式时发生的事）。
		try
		{
			Invoke(form, "ShowMode", 1);
			Application.DoEvents();
			Log.AppendLine($"  [gpuoc] 走产品入口 ShowMode(1)（视觉模式 1 = 平衡）后：_gpuOcBox.Visible={(Find(form, "gpu_oc")?.Visible.ToString() ?? "n/a")}");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] ShowMode(1) 失败: " + ex.Message);
		}
		Control box = Find(form, "gpu_oc");
		if (box == null)
		{
			Log.AppendLine("  !! [gpuoc] 夹具下 gpu_oc 仍不存在 ⇒ 判据未全真，该夹具不出图");
			return;
		}
		bool wantOn = string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_GPUOC"), "1", StringComparison.Ordinal);
		try
		{
			AppConfig.Set("gpu_oc", wantOn ? 1 : 0);
			if (box is CheckBox checkBox && checkBox.Checked != wantOn)
			{
				checkBox.Checked = wantOn;
				Application.DoEvents();
			}
			Log.AppendLine($"  [gpuoc] 夹具选中态 LMECHREVO_SHOT_GPUOC={(wantOn ? "1(开)" : "(未设⇒关)")} ⇒ gpu_oc.Checked={(box as CheckBox)?.Checked}（只写本工具的隔离配置 tmp-cfg-*.json）");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] 设置选中态失败: " + ex.Message);
		}
	}

	private static void SettingsTelemetryAfter(Form form, MechrevoHw hw)
	{
		SettingsAfter(form, hw);
		SendHw(hw, "System/CpuInfo", "{\"CpuTemperature\":58,\"CpuUsage\":21,\"CpuFrequency\":4200}");
		SendHw(hw, "System/GpuInfo", "{\"GpuTemperature\":43,\"GpuUsage\":37,\"GpuCoreFreq\":1800}");
		bool user2 = string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TELEMETRY"), "user2", StringComparison.OrdinalIgnoreCase);
		if (user2)
		{
			SendHw(hw, "System/FanInfo", "{\"CpuFanDuty\":45,\"CpuFanRpm\":2095,\"GpuFanDuty\":45,\"GpuFanRpm\":1857}");
			SetStatic("MechrevoLite.HardwareControl", "cpuPower", 27f);
			SetStatic("MechrevoLite.HardwareControl", "gpuPower", 5f);
			Log.AppendLine("  [fixture] telemetry = user2 (27W 2095rpm / 5W 1857rpm)");
		}
		else
		{
			SendHw(hw, "System/FanInfo", "{\"CpuFanDuty\":35,\"CpuFanRpm\":2539,\"GpuFanDuty\":35,\"GpuFanRpm\":2369}");
			SetStatic("MechrevoLite.HardwareControl", "cpuPower", 12f);
			SetStatic("MechrevoLite.HardwareControl", "gpuPower", 8f);
		}
		try
		{
			Invoke(form, "UpdateTelemetryText");
			Application.DoEvents();
			// 读数文本变化 → LayoutTelemetryPieces 会改列宽并 PerformLayout；再刷一轮让布局收敛后
			// 才做几何断言（单轮可能停在中间态）。
			Invoke(form, "UpdateTelemetryText");
			Application.DoEvents();
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! UpdateTelemetryText failed: " + ex.Message);
		}
	}

	// ==========================================================================================
	// 2026-10-11 布局取证：把「性能行 / 配置下拉行 / 按钮行」的**真实几何**写进日志
	//   * 行序：性能行必须整条在按钮行**上方**（用户原话「把按钮放在性能行下面一行吧」）；
	//   * 30 系：配置下拉行必须夹在性能行与按钮行之间；
	//   * 读数：必须是**单行**、完整可见（实测文本宽 <= 客户区宽，且按控件宽排版不折行）；
	//   * 左组：图标 + 标题 + 三个开关的内容宽不得超出左组可用宽（否则最右那颗被裁）。
	// ==========================================================================================
	private static void DumpTitleRows(Form form, ViewportSpec viewport)
	{
		string[][] pairs = new string[2][]
		{
			new string[2] { "perfHeaderRow", "perfModeRow" },
			new string[2] { "gpuHeaderRow", "gpuModeRow" }
		};
		Log.AppendLine("  [layout] ===== 性能行 / 按钮行几何核对 =====");
		foreach (string[] pair in pairs)
		{
			Control header = Find(form, pair[0]);
			Control modeRow = Find(form, pair[1]);
			string hostName = ((pair[0] == "perfHeaderRow") ? "panelCPUTitle" : "panelGPUTitle");
			Control host = Find(form, hostName);
			Log.AppendLine($"  [layout] {hostName} {Shape(host)}");
			if (header == null || modeRow == null || host == null)
			{
				Log.AppendLine("  [layout] MISSING header/modeRow/host");
				continue;
			}
			// 断言 a：两行矩形**不得相交**（真机坏态就是把读数画到按钮上）
			bool overlap = header.Bounds.IntersectsWith(modeRow.Bounds);
			Log.AppendLine($"  [layout] [row-overlap] 性能行{header.Bounds} ∩ 按钮行{modeRow.Bounds} = {(overlap ? "TRUE（重叠！）" : "FALSE")} ⇒ {(overlap ? "FAIL" : "PASS")}");
			Rows.Add(new string[5] { "LayoutAudit", pair[0], (overlap ? "row-overlap-fail" : "row-overlap-pass"), ControlPath(header), $"header={header.Bounds} intersects modeRow={modeRow.Bounds} -> {overlap}" });
			Log.AppendLine($"  [layout] {pair[0]} {Shape(header)}");
			Log.AppendLine($"  [layout] {pair[1]} {Shape(modeRow)}");
			// 断言 d：按钮行必须整条落在性能行**下方**（用户原话「按钮独占性能行下面那一行」）。
			// ⚠️ 性能卡的按钮行已从 panelCPUTitle 搬到 panelPerformance（与配置下拉行同级），
			//    所以这里比的是**卡片面板坐标系**里的兄弟矩形（两者同父，坐标系一致）。
			Control card = ((pair[0] == "perfHeaderRow") ? Find(form, "panelPerformance") : Find(form, "panelGPU"));
			Control cfgRow = ((pair[0] == "perfHeaderRow") ? Find(form, "perfConfigRow") : null);
			if (card != null)
			{
				List<string> layers = new List<string>();
				foreach (Control child in card.Controls)
				{
					layers.Add($"{child.Name}[y={child.Bounds.Top}..{child.Bounds.Bottom}{(child.Visible ? "" : ",hidden")}]");
				}
				Log.AppendLine($"  [layout] [layers] {card.Name} 高={card.ClientSize.Height}: {string.Join(" -> ", layers)}");
				Rows.Add(new string[5] { "LayoutAudit", card.Name + "-layers", "layers", ControlPath(card), string.Join(" -> ", layers) });
				Control headerInCard = ((pair[0] == "perfHeaderRow") ? Find(card, "panelCPUTitle") : header);
				int headerBottom = ((headerInCard != null) ? headerInCard.Bounds.Bottom : header.Bounds.Bottom);
				bool buttonsBelow = modeRow.Bounds.Top >= headerBottom - 1;
				Log.AppendLine($"  [layout] [buttons-below] 性能行底={headerBottom} <= 按钮行顶={modeRow.Bounds.Top} ⇒ {(buttonsBelow ? "PASS" : "FAIL")}");
				Rows.Add(new string[5] { "LayoutAudit", card.Name + "-buttons-below", (buttonsBelow ? "buttons-below-pass" : "buttons-below-fail"), ControlPath(modeRow), $"header bottom={headerBottom} buttons top={modeRow.Bounds.Top}" });
				if (cfgRow != null && cfgRow.Visible)
				{
					// 30 系：配置下拉行必须夹在性能行与按钮行之间
					bool cfgBetween = cfgRow.Bounds.Top >= headerBottom - 1 && cfgRow.Bounds.Bottom <= modeRow.Bounds.Top + 1;
					Log.AppendLine($"  [layout] [cfg-between] 性能行底={headerBottom} <= 配置行 {cfgRow.Bounds.Top}..{cfgRow.Bounds.Bottom} <= 按钮行顶={modeRow.Bounds.Top} ⇒ {(cfgBetween ? "PASS" : "FAIL")}");
					Rows.Add(new string[5] { "LayoutAudit", "perfConfigRow-between", (cfgBetween ? "cfg-between-pass" : "cfg-between-fail"), ControlPath(cfgRow), $"cfg {cfgRow.Bounds} between header bottom {headerBottom} and buttons top {modeRow.Bounds.Top}" });
				}
			}
			foreach (Control child in header.Controls)
			{
				if (child is TableLayoutPanel inner && inner.Name == pair[0] + "Left")
				{
					Log.AppendLine($"  [layout]   性能行左组 {inner.Name} {inner.Bounds} 列宽=[{string.Join(",", inner.GetColumnWidths())}]");
					int neededRight = 0;
					foreach (Control g in inner.Controls)
					{
						Log.AppendLine($"  [layout]     {g.GetType().Name}#{g.Name} {g.Bounds} vis={g.Visible} 文本='{Oneline((g as Label)?.Text ?? (g as Button)?.Text ?? "")}'");
						if (g.Visible)
						{
							neededRight = Math.Max(neededRight, g.Bounds.Right + g.Margin.Right);
						}
					}
					// 断言 c：左组内容宽 vs 左组可用宽（TableLayoutPanel 的绝对/自动列会先吃满宽度，
					// 左组那个 Percent 列可能被压到装不下内容 ⇒ 最右那颗开关（配置编辑）右缘被裁）。
					bool leftFits = neededRight <= inner.ClientSize.Width + 1;
					// ⚠️ 保真度门槛：本工具在 96 DPI 宿主上跑，多 DPI 档靠 form.Scale() 模拟；
					// 实测 150% 那一档窗体会被撑成 840 逻辑像素（真实 150% 窗宽只有 420*1.5=630），
					// 字体/列宽按真实 DeviceDpi 算 ⇒ 该档的 left-fit 结论**不可比**，只作参考。
					bool dpiComparable = form.DeviceDpi * 100 / 96 == viewport.Dpi * 100 / 96;
					string fitDetail = $"content right={neededRight} vs container width={inner.ClientSize.Width} ({(leftFits ? 0 : neededRight - inner.ClientSize.Width)}px 被裁)" + (dpiComparable ? "" : " [⚠️ 宿主保真度不足：模拟视口 DPI 与窗体真实 DeviceDpi 不一致 ⇒ 本行只作参考]");
					Log.AppendLine($"  [layout]   [left-fit] 左组内容右缘={neededRight} vs 左组宽={inner.ClientSize.Width} ⇒ {(leftFits ? "PASS" : $"FAIL 被裁 {neededRight - inner.ClientSize.Width}px")}{(dpiComparable ? "" : " [保真度不足·参考]")}");
					Rows.Add(new string[5] { "LayoutAudit", pair[0] + "Left", (leftFits ? "left-fit-pass" : (dpiComparable ? "left-fit-fail" : "left-fit-fail-unreliable-viewport")), ControlPath(inner), fitDetail });
				}
				else if (child is Label label)
				{
					// 断言 b：读数必须**单行**且**完整可见**（无折行、无省略号）。
					// 判据与**绘制口径**一致：RTelemetryLabel 自己清空 Padding 并自绘单行（文本矩形 = 整个客户区）
					// ⇒ 用 NoPadding 量；普通 Label 走 Default（含内边距）。
					bool telemetryPiece = label.GetType().Name == "RTelemetryLabel";
					TextFormatFlags measure = (telemetryPiece ? (TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix) : TextFormatFlags.Default);
					Size need = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(int.MaxValue, int.MaxValue), measure);
					bool fits = need.Width <= label.ClientSize.Width + 2 && need.Height <= label.ClientSize.Height + 2;
					bool inside = label.Bounds.Right <= header.ClientSize.Width + 1;
					Size wrapped = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, label.ClientSize.Width), int.MaxValue), measure | TextFormatFlags.WordBreak);
					bool singleLine = wrapped.Height <= label.ClientSize.Height + 2;
					bool fullVisible = need.Width <= label.ClientSize.Width;
					Size rendered = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.Default);
					string verdict = ((fits && inside && singleLine && fullVisible) ? "reading-fit-pass" : "reading-fit-fail");
					Log.AppendLine($"  [layout]   [读数] '{Oneline(label.Text)}' {label.Bounds} 实测文本={need.Width}x{need.Height}({(telemetryPiece ? "NoPadding/自绘" : "Default/基类")}) 渲染实宽={rendered.Width} 按控件宽排版={wrapped.Width}x{wrapped.Height} 字体={label.Font.Name} {label.Font.SizeInPoints}pt AutoSize={label.AutoSize} AutoEllipsis={label.AutoEllipsis} 对齐={label.TextAlign} 放得下={(fits ? "PASS" : "FAIL")} 单行={(singleLine ? "PASS" : "FAIL 折行")} 完整可见={(fullVisible ? "PASS" : "FAIL 会被截断")} 整片在行内={(inside ? "PASS" : "FAIL")}");
					Rows.Add(new string[5] { "LayoutAudit", "telemetry", verdict, ControlPath(label), $"'{Oneline(label.Text)}' need={need.Width}x{need.Height} rendered={rendered.Width} box={label.Bounds} headerW={header.ClientSize.Width} singleLine={singleLine} fullVisible={fullVisible} autoEllipsis={label.AutoEllipsis}" });
					if (!singleLine || !fullVisible)
					{
						Rows.Add(new string[5] { "LayoutAudit", "telemetry-multiline", "reading-multiline-fail", ControlPath(label), $"'{Oneline(label.Text)}' 文本宽={need.Width} vs 控件宽={label.ClientSize.Width} ⇒ 会被折行/截断（渲染实宽 {rendered.Width}）" });
					}
				}
			}
			Log.AppendLine($"  [layout]   按钮行 {modeRow.Name} 子控件={string.Join(";", modeRow.Controls.Cast<Control>().Select((Control x) => $"{x.GetType().Name}#{x.Name}:{x.Bounds}:vis={x.Visible}"))}");
			Control table = Find(modeRow, ((pair[0] == "perfHeaderRow") ? "tablePerf" : "tableGPU"));
			if (table is TableLayoutPanel buttons)
			{
				Log.AppendLine($"  [layout]   [按钮] {buttons.Name} {buttons.Bounds} 列宽=[{string.Join(",", buttons.GetColumnWidths())}] 子={string.Join(";", buttons.Controls.Cast<Control>().Select((Control b) => $"{b.Name}:{b.Bounds}:vis={b.Visible}:txt='{Oneline((b as Button)?.Text ?? "")}'"))}");
			}
		}
	}

	// 2026-10-11 布局取证：把「性能行」左右两组的**宽度预算**直接写出来 ——
	// 用来回答"到底是谁把读数挤到换行的"，以及"要放下必须多宽"。
	private static void DumpRowBudget(Form form)
	{
		string[] hosts = new string[2] { "perfHeaderRowLeft", "gpuHeaderRowLeft" };
		foreach (string h in hosts)
		{
			Control host = Find(form, h);
			if (host == null)
			{
				continue;
			}
			StringBuilder sb = new StringBuilder("  [budget] " + h + " width=" + host.ClientSize.Width + " 明细: ");
			int used = 0;
			foreach (Control c in host.Controls)
			{
				used += (c.Visible ? c.Width : 0) + c.Margin.Horizontal;
				sb.Append($"{c.GetType().Name}#{c.Name}[{c.Width}x{c.Height},vis={c.Visible},txt='{Oneline((c as Label)?.Text ?? (c as Button)?.Text ?? "")}']+");
			}
			sb.Append($"小计(含margin)={used}");
			Log.AppendLine(sb.ToString());
			Control header = host.Parent;
			if (header != null)
			{
				Log.AppendLine($"  [budget] {header.Name} 总宽={header.ClientSize.Width} 左组={host.Width} 读数块={header.ClientSize.Width - host.Width}");
			}
		}
		// 读数片：产品口径（NoPadding + 方向相关余量）vs 基类渲染口径（Default，含内边距）逐片对照
		foreach (Control c in AllControls(form))
		{
			if (c is Label l && string.IsNullOrEmpty(l.Name) && l.Parent != null && (l.Parent.Name == "perfHeaderRow" || l.Parent.Name == "gpuHeaderRow"))
			{
				Size noPad = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(4000, 100), TextFormatFlags.NoPadding);
				Size rendered = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(4000, 100), TextFormatFlags.Default);
				Log.AppendLine($"  [budget] 读数片 '{Oneline(l.Text)}' 控件宽={l.ClientSize.Width} NoPadding={noPad.Width} Default={rendered.Width} 差额={rendered.Width - noPad.Width} 余量={l.ClientSize.Width - noPad.Width} AutoEllipsis={l.AutoEllipsis}");
			}
		}
	}

	/// <summary>
	/// 2026-10-12（修「第三个磁贴点不动」的只读取证 · ⛔ 不点按钮/不下发/不写配置）：
	/// 复现产品自己的那条时序 ——
	/// <list type="number">
	/// <item>把磁贴所引用的那颗 CheckBox 的 <c>Enabled</c> 置 false
	/// （= <c>ApplyQuickSwitchAsync</c> 里 <c>box.Enabled = false</c> 的那一瞬）；</item>
	/// <item>调产品自己的 <c>RefreshQuickTiles()</c>（= <c>ToggleQuickTileAsync</c> 在给 Checked 赋值后
	/// **立刻**调的那一次 —— 也就是旧代码把磁贴锁死的那一次）；</item>
	/// <item>把 <c>Enabled</c> 复位为 true（= 动作结束），**故意不调任何刷新**
	/// （旧代码正是如此：动作结束没有任何一处重算磁贴）；</item>
	/// <item>读磁贴的 <c>Enabled</c>：改前 = False（锁死，只能等别的开关被拨动或 12s 后的定时器）；
	/// 改后 = True（可点性已不再依赖 CheckBox 的瞬时值）。</item>
	/// </list>
	/// 若 DLL 里存在 2026-10-12 新增的 <c>SetQuickSwitchInFlight(CheckBox,bool)</c>，再按**真实路径**
	/// 跑一遍：在途 ⇒ 不可点；解除（含 finally）⇒ 自动恢复可点，⛔ 不需要别的开关来"救"。
	/// </summary>
	private static void RunTileGateProbe(SettingsForm form, string fixtureName, string viewportId)
	{
		try
		{
			Control boxNumpad = form.Controls.Find("quick_numpad", true).FirstOrDefault();
			Control tileNumpad = form.Controls.Find("tile_numpad", true).FirstOrDefault();
			Control tileAdd = form.Controls.Find("tile_add", true).FirstOrDefault();
			if (boxNumpad == null || tileNumpad == null)
			{
				Log.AppendLine($"  [tilegate] {fixtureName}@{viewportId} 跳过：quick_numpad={boxNumpad != null} tile_numpad={tileNumpad != null}（该视口未建出磁贴行）");
				return;
			}
			bool e0 = tileNumpad.Enabled;
			boxNumpad.Enabled = false;
			Invoke(form, "RefreshQuickTiles");
			bool e1 = tileNumpad.Enabled;
			boxNumpad.Enabled = true;
			bool e2 = tileNumpad.Enabled;
			Invoke(form, "RefreshQuickTiles");
			bool e3 = tileNumpad.Enabled;
			Log.AppendLine($"  [tilegate] {fixtureName}@{viewportId} key=numpad 基线Enabled={e0} ①CheckBox忙+Refresh={e1} ②动作结束(不刷新)={e2} ③再Refresh={e3} ⇒ 旧代码锁死判据(②为 False)={(e2 ? "否" : "是")}；tile_add.Enabled={(tileAdd != null ? tileAdd.Enabled.ToString() : "n/a")}");
			// 真实路径 A/B（只有新 DLL 才有 SetQuickSwitchInFlight）
			try
			{
				Type settingsType = form.GetType();
				MethodInfo setter = settingsType.GetMethod("SetQuickSwitchInFlight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (setter == null)
				{
					Log.AppendLine("  [tilegate] 本 DLL 无 SetQuickSwitchInFlight ⇒ 只有上面四步（旧代码口径）");
				}
				else
				{
					setter.Invoke(form, new object[2] { boxNumpad, true });
					Invoke(form, "RefreshQuickTiles");
					bool b1 = tileNumpad.Enabled;
					setter.Invoke(form, new object[2] { boxNumpad, false });
					bool b2 = tileNumpad.Enabled;
					Log.AppendLine($"  [tilegate] 真实路径：在途(SetQuickSwitchInFlight true)={b1}（应为 False=忙碌）；解除(false)={b2}（应为 True=自动恢复，⛔ 无需别的开关）");
				}
			}
			catch (Exception ex)
			{
				Log.AppendLine("  [tilegate] 真实路径异常: " + ex.Message);
			}
			boxNumpad.Enabled = true;
			Invoke(form, "RefreshQuickTiles");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  [tilegate] 探针异常: " + ex);
		}
	}

	// 2026-10-11（本任务取证用）：整棵控件树的几何/字体导出（⛔ 不碰产品代码、不写硬件）。
	// 输出形如：  [geo] RComboBox#_displayEffectCombo {X=..,Y=..,W=..,H=..} client=..x.. dock=None vis=True en=False font=Microsoft YaHei UI 9pt
	private static void DumpGeometry(Control root)
	{
		int count = 0;
		foreach (Control c in AllControls(root))
		{
			count++;
			string text = Oneline((c as Label)?.Text ?? (c as Button)?.Text ?? (c as ComboBox)?.Text ?? "");
			Log.AppendLine($"  [geo] {c.GetType().Name}#{c.Name} {c.Bounds} client={c.ClientSize.Width}x{c.ClientSize.Height} dock={c.Dock} anchor={c.Anchor} vis={c.Visible} en={c.Enabled} font={c.Font.Name} {c.Font.SizeInPoints}pt/ascent{c.Font.Height} autoSize={(c as Label)?.AutoSize.ToString() ?? (c as CheckBox)?.AutoSize.ToString() ?? (c as Button)?.AutoSize.ToString() ?? "-"} text='{text}' path={ControlPath(c)}");
		}
		Log.AppendLine($"  [geo] total-controls={count}");
	}

	private static string Shape(Control c)
	{
		if (c == null)
		{
			return "MISSING";
		}
		string rows = ((c as TableLayoutPanel) != null) ? string.Join(",", ((TableLayoutPanel)c).GetRowHeights()) : "-";
		return $"{c.GetType().Name}#{c.Name} {c.Width}x{c.Height}@{c.Location.X},{c.Location.Y} client={c.ClientSize.Width}x{c.ClientSize.Height} dock={c.Dock} vis={c.Visible} rows=[{rows}]";
	}

	private static MechrevoDeviceCapabilities RichCaps(bool igpuOnly)
	{
		return new MechrevoDeviceCapabilities
		{
			ProfileAvailable = true,
			TurboMode = true,
			TurboSubMode = true,
			CpuPerformanceTuning = true,
			FanSettings = true,
			LiquidCooling = true,
			DisplayRefresh = true,
			ColorCalibration = true,
			LcdOverdrive = true,
			DgpuDirect = true,
			IgpuOnly = igpuOnly,
			Keyboard = true,
			Lightbar = true,
			LogoLight = true
		};
	}

	private static Form MakeSettingsDialog()
	{
		Panel themePanel = new Panel
		{
			Name = "panelThemeMode",
			Height = 54,
			Padding = new Padding(12, 8, 12, 8)
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Name = "labelThemeModeTitle",
			Text = "界面外观",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			AutoEllipsis = true
		}, 0, 0);
		tableLayoutPanel.Controls.Add(new Button
		{
			Name = "buttonDayMode",
			Text = "日间",
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			Cursor = Cursors.Hand
		}, 1, 0);
		tableLayoutPanel.Controls.Add(new Button
		{
			Name = "buttonNightMode",
			Text = "夜间",
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			Cursor = Cursors.Hand
		}, 2, 0);
		themePanel.Controls.Add(tableLayoutPanel);
		Panel officialPanel = new Panel
		{
			Name = "panelOfficialConsole",
			Height = 48,
			Padding = new Padding(12, 8, 12, 8)
		};
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(new Label
		{
			Name = "labelOfficialConsoleTitle",
			Text = "官方控制台",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			AutoEllipsis = true
		}, 0, 0);
		tableLayoutPanel2.Controls.Add(new Label
		{
			Name = "labelOfficialConsoleStatus",
			Text = "正在检测...",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			Margin = Padding.Empty,
			AutoEllipsis = true
		}, 1, 0);
		tableLayoutPanel2.Controls.Add(new Button
		{
			Name = "buttonOfficialConsole",
			Text = "打开官方控制台",
			Dock = DockStyle.Fill,
			Margin = new Padding(8, 0, 0, 0),
			Cursor = Cursors.Hand
		}, 2, 0);
		officialPanel.Controls.Add(tableLayoutPanel2);
		RCheckBox rCheckBox = new RCheckBox
		{
			Name = "checkLcdOverdrive",
			Text = "响应加速",
			AutoSize = true,
			Visible = true
		};
		return new SettingsDialog(themePanel, officialPanel, rCheckBox, true, null, null, null);
	}

	private static object MakeProbe(string cpu, string statusKey, string detail, object[] options)
	{
		Type listType = typeof(List<>).MakeGenericType(_optionType);
		IList list = (IList)Activator.CreateInstance(listType);
		foreach (object option in options)
		{
			list.Add(option);
		}
		return Activator.CreateInstance(_probeType, cpu, statusKey, detail, list);
	}

	private static object MakeOption(string id, string labelKey, decimal min, decimal max, string unit, decimal? current, bool canWrite, int[] coreReadback, string readbackNoteKey, bool perCoreWrite)
	{
		return Activator.CreateInstance(_optionType, id, labelKey, min, max, unit, current, canWrite, coreReadback, readbackNoteKey, perCoreWrite);
	}

	private static Form MakeCpuTuning(object probe)
	{
		ConstructorInfo constructorInfo = _asm.GetType("MechrevoLite.CpuTuningForm").GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { _probeType }, null);
		return (Form)constructorInfo.Invoke(new object[1] { probe });
	}

	// ==========================================================================================
	// 单个 (夹具 × 视口) 组合：与产品 UiAuditRunner 同一套渲染 + 判据
	// ==========================================================================================
	private static void RunCombo(ViewportSpec viewport, Fixture fixture)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Log.AppendLine($"--- {fixture.Name} @ {viewport.Id} (dpi={viewport.Dpi}, work={viewport.WorkingArea.Width}x{viewport.WorkingArea.Height}) ---");
		Form form = null;
		MechrevoHw mechrevoHw = null;
		try
		{
			MechrevoDeviceCapabilities caps = fixture.Caps?.Invoke();
			SetStatic("MechrevoLite.Program", "UiAuditUseReportedCapabilities", caps != null);
			if (caps != null)
			{
				mechrevoHw = MakeHw(caps);
				SetStatic("MechrevoLite.Program", "hw", mechrevoHw);
			}
			// 2026-10-12（「显卡超频」开关取证）：只在 Settings-GpuOc 夹具里、且**必须在窗体构造之前**
			// 完成两件事 —— ① 把 IsGcu50 的判定在宿主进程内旁路为真；② 注入合成 Program.service
			// 并把它的 OperatingMode 喂成 1（⇒ CurrentMode == 1）。两者都是宿主侧只读动作。
			if (fixture.Name == "Settings-GpuOc")
			{
				SetupGpuOcFixture();
			}
			SetStatic(_uiDpiType, "AuditDpi", viewport.Dpi);
			form = fixture.Make();
			form.Name = fixture.Name;
			form.ShowInTaskbar = false;
			form.StartPosition = FormStartPosition.Manual;
			form.Opacity = 0.01;
			form.Location = new Point(20, 20);
			form.Show();
			Application.DoEvents();
			int num = ReadHostDpi(form);
			float num2 = (float)viewport.Dpi / (float)num;
			if (Math.Abs(num2 - 1f) > 0.01f)
			{
				form.Scale(new SizeF(num2, num2));
				ScaleFonts(form, num2);
			}
			SetProperty(form, "AuditLayoutScale", (float)viewport.Dpi / 96f);
			Log.AppendLine($"  pre-bounds  size={form.Size} client={form.ClientSize} min={form.MinimumSize} max={form.MaximumSize} autoscroll={(form as ScrollableControl)?.AutoScroll}");
			Invoke(form, "ApplyResponsiveBounds", viewport.WorkingArea);
			ResponsiveLayout.PerformLayoutTree(form);
			Application.DoEvents();
			Log.AppendLine($"  post-bounds size={form.Size} client={form.ClientSize} min={form.MinimumSize} max={form.MaximumSize}");
			if (fixture.IsSettings && form is SettingsForm settingsForm)
			{
				settingsForm.Hide();
				settingsForm.ShowAll(viewport.WorkingArea);
				Application.DoEvents();
				settingsForm.Hide();
				settingsForm.ShowAll(viewport.WorkingArea);
				Application.DoEvents();
			}
			fixture.After?.Invoke(form, mechrevoHw);
			ResponsiveLayout.PerformLayoutTree(form);
			Application.DoEvents();
			// 2026-10-11（真机布局取证）：默认**就是真实窗宽** —— 窗体自己按应用真实的默认/最小 client 宽度建好。
			// ⚠️ 2026-10-12：最小宽已回到固定 420 逻辑像素（用户答复③撤销抬高），见 MinimumDashboardLogicalClientSize。
			// ⛔ 不再默认拉宽：上一轮把窗体拉到 1000px 再取证，那个窗宽下"读数放得下"，而真机不是 1000px
			// ⇒ 那次 6/6 PASS / 18/18 PASS 就是这个"取证窗宽不对"造成的假 PASS。
			// `LMECHREVO_SHOT_FORMWIDTH` 仍保留：要额外取证"用户把窗口拉宽后"的样子才设它。
			string widthOverride = Environment.GetEnvironmentVariable("LMECHREVO_SHOT_FORMWIDTH");
			if (!string.IsNullOrEmpty(widthOverride) && int.TryParse(widthOverride, out int formWidth) && formWidth > form.Width)
			{
				form.Width = formWidth;
				ResponsiveLayout.PerformLayoutTree(form);
				Application.DoEvents();
				Log.AppendLine($"  form-width-override={formWidth} -> actual {form.Width}");
			}
			else
			{
				Log.AppendLine("  form-width=应用真实默认/最小窗宽（未覆盖；要覆盖设 LMECHREVO_SHOT_FORMWIDTH）");
			}
			Log.AppendLine($"  form={form.Width}x{form.Height} client={form.ClientSize}");
			if (form is SettingsForm settingsForWidth)
			{
				Invoke(settingsForWidth, "RefreshDashboardMinimumWidth");
				Application.DoEvents();
				Log.AppendLine($"  min={settingsForWidth.MinimumSize} 需要客户区宽={Invoke(settingsForWidth, "RequiredDashboardLogicalWidthForAudit")} 明细={Invoke(settingsForWidth, "DescribeRequiredDashboardWidthForAudit")}");
			}
			if (form is SettingsForm)
			{
				DumpTitleRows(form, viewport);
				DumpRowBudget(form);
				// 2026-10-12（用户原话「我发现它是底边和标题对齐的 能不能改成中线的标题对齐」）：
				// 量「性能模式」标题 与 perfShortcuts 里两颗开关的**垂直中线**（同一坐标系），
				// 并顺带把"可见像素行范围"打出来 —— 见 DumpSwitchAlign。
				DumpSwitchAlign(form, fixture.Name, viewport.Id);
			}
			// 2026-10-11（本任务取证用，⛔ 只导出几何/字体，不改产品逻辑）：把**整棵控件树**的
			// 类型/名字/矩形/可见/可用/字体/文本写成行 —— 用来回答"某个自绘控件在同一份产品 DLL +
			// 同一套实例化路径下到底被布局成多高"，而不是凭截图猜。
			DumpGeometry(form);
			object issues = NewIssueList();
			object viewportObj = Activator.CreateInstance(_vpType, viewport.Width, viewport.Height, viewport.Dpi);
			InvokeStatic(_auditType, "AuditForm", fixture.Name, viewportObj, form, issues, new List<string>(), new List<string>());
			DumpIssues(fixture.Name, viewport.Id, issues);
			ExtraChecks(fixture, form, viewport.Id);
			if (_shots)
			{
				ApplyShotTheme(form);
				string path = Path.Combine(_shotsDir, $"shot-{fixture.Name}-{viewport.Id}.png");
				using Bitmap bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height), PixelFormat.Format32bppArgb);
				form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				bitmap.Save(path, ImageFormat.Png);
				Log.AppendLine($"  shot={path}");
			}
			// 2026-10-11（本轮取证需要）：整窗截图只画**可见客户区**，而「灯光」卡片在真机默认窗宽下
			// 排在 dashboardStack 的 y≈582（宿主 568 高的窗体里根本不在画面内）。所以额外提供"把某张卡片
			// 单独出图"的模式：把那颗控件在**窗体客户区坐标**里的矩形算出来，临时把它的父容器往上挪，
			// 让它落到画面顶部的指定位置，再 DrawToBitmap + 只裁该卡片那一段。⛔ 不写任何硬件/配置，
			// 只动一次布局坐标（渲染完立即 Dispose 掉窗体）。
			// 用法：CARD_CROP_PATH=<控件路径子串> CARD_CROP_NAME=<输出名> [CARD_CROP_Y=<客户区 Y，默认 24>]
			CardCrop(form, fixture.Name, viewport.Id);
			if (fixture.Name == "Settings-GpuOc")
			{
				DumpGpuOcBoxProbe(form, fixture.Name, viewport.Id);
				DumpGpuOcBox(form, fixture.Name, viewport.Id);
				SwitchZoom(form, fixture.Name, viewport.Id);
				// 2026-10-12（本轮 · 中线对齐取证）：把「标题 + 开关组」那一片放大 ×3，并在图上
				// **画出两条中线**（标题中线 = 品红、开关中线 = 青）⇒ 对齐与否一眼可判。走
				// `LMECHREVO_SHOT_ALIGNZOOM=1`（⛔ 只读渲染：DrawToBitmap + 画辅助线）。
				AlignZoom(form, fixture.Name, viewport.Id);
			}
		}
		catch (Exception ex)
		{
			Log.AppendLine("  COMBO-EX: " + ex);
			Rows.Add(new string[5] { fixture.Name, viewport.Id, "combo-error", fixture.Name, ex.ToString() });
		}
		finally
		{
			form?.Dispose();
			if (fixture.Name == "Settings-GpuOc")
			{
				RestoreGpuOcFixture();
			}
			SetStatic(_uiDpiType, "AuditDpi", 0);
			if (mechrevoHw != null)
			{
				SetStatic("MechrevoLite.Program", "hw", null);
				mechrevoHw.Dispose();
			}
			SetStatic("MechrevoLite.Program", "UiAuditUseReportedCapabilities", false);
			// 功耗是 HardwareControl 的静态字段，夹具置过位必须复位，否则会串到后面的组合。
			SetStatic("MechrevoLite.HardwareControl", "cpuPower", null);
			SetStatic("MechrevoLite.HardwareControl", "gpuPower", null);
			Application.DoEvents();
		}
		Log.AppendLine($"  elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
	}

	/// <summary>
	/// 2026-10-11（读数配色取证需要）：<c>LMECHREVO_SHOT_THEME=day|light</c> 时，把已建好的 SettingsForm
	/// 切到**浅色主题**再出图（默认不设 = 保持产品审计流程结束时的夜间主题，逐像素与上一轮相同）。
	/// 走产品自己的 <c>SettingsForm.ApplyThemeMode(bool)</c>（反射调用，与 <c>UpdateTelemetryText</c> 同一口径）
	/// ⇒ 主题切换后的读数片前景色正是真机切主题时那条路径（UiVisualStyle.ApplyWindow → ApplyTree →
	/// ResolveLabelForeground）。⛔ 只读渲染：不写配置、不碰硬件。
	/// </summary>
	/// <summary>
	/// 2026-10-12（本轮 ·「显卡超频」开关取证）：在**宿主自己的进程内**把
	/// <c>MechrevoService.IsGcu50</c> 的判定旁路成真，并注入一份只读的合成 <c>Program.service</c>。
	///
	/// <para><b>为什么必须旁路</b>：产品的判据是
	/// <c>Program.service != null &amp;&amp; CurrentMode == 1 &amp;&amp; Program.hw != null &amp;&amp; MechrevoService.IsGcu50</c>
	/// （<c>SettingsForm.cs:6031</c> 与 <c>:10789</c>），而 <c>IsGcu50</c> 只读
	/// <c>HKLM\SOFTWARE\L-Mechrevo\GcuVariant</c>（<c>MechrevoService.cs:3367</c>，只有 <c>"50"</c> 才判真）。
	/// 本机是 Gen40 ⇒ 该开关永不出现，且 B 面上**没有任何** <c>FORCE_*</c> 类钩子可强开它
	/// （<c>LMECHREVO_FORCE_30UI</c> / <c>_30PERF</c> / <c>_CPU_TUNING</c> 都不落在这一条判据上）。</para>
	///
	/// <para><b>做法（全部只读，⛔ 不写注册表、⛔ 不动产品文件）</b>：
	/// ① <see cref="HookIsGcu50True"/> —— 只把 <c>get_IsGcu50</c> 的**判定分支**改成"恒真"，
	///    <c>try/catch</c>、注册表读取、比较与装箱**一律原样保留**（等价于在宿主进程里把
	///    <c>GcuVariant</c> 读成 <c>"50"</c>），并保留原机器码字节以便退出前还原；
	/// ② <see cref="MakeFixtureService"/> + 喂一条合成 <c>Fan/Status</c>（<c>OperatingMode=1</c>）
	///    ⇒ <c>Program.service.CurrentMode == 1</c>。</para>
	/// </summary>
	private static void SetupGpuOcFixture()
	{
		try
		{
			HookIsGcu50True();
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] IsGcu50 旁路失败: " + ex);
		}
		try
		{
			object svc = MakeFixtureService(GetStatic("MechrevoLite.Program", "hw") as MechrevoHw, 1);
			SetStatic("MechrevoLite.Program", "service", svc);
			Log.AppendLine($"  [gpuoc] Program.service={svc?.GetType().Name ?? "null"} CurrentMode={(svc == null ? -1 : (int)svc.GetType().GetProperty("CurrentMode").GetValue(svc))} (期望 1)");
			// 诊断：service._hw 是否就是夹具 hw、它报的 OperatingMode 是几（CurrentMode 的真值来源）。
			object svcHw = svc == null ? null : svc.GetType().GetField("_hw", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(svc);
			int opMode = -1;
			try
			{
				opMode = (int)svcHw.GetType().GetProperty("OperatingMode").GetValue(svcHw);
			}
			catch
			{
			}
			Log.AppendLine($"  [gpuoc] 诊断: service._hw={(svcHw == null ? "null" : (svcHw == GetStatic("MechrevoLite.Program", "hw") ? "== Program.hw" : svcHw.GetType().Name))} OperatingMode={opMode} ⇒ CurrentMode={MechrevoService.ToVisualMode(opMode)}");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] Program.service 注入失败: " + ex);
		}
		Log.AppendLine($"  [gpuoc] MechrevoService.IsGcu50 现值={SafeIsGcu50()} (夹具期望 True)；本机真实注册表判据未被读取/写入");
	}

	/// <summary>退出前还原：把 <c>get_IsGcu50</c> 的原机器码写回，并清掉合成的 <c>Program.service</c>。</summary>
	private static void RestoreGpuOcFixture()
	{
		DumpLive("restore-enter");
		if (_isGcu50PatchTarget != IntPtr.Zero && _isGcu50OriginalBytes != null && _isGcu50PatchLength > 0)
		{
			try
			{
				if (!VirtualProtect(_isGcu50PatchTarget, (IntPtr)_isGcu50PatchLength, 0x40u, out uint oldProtect))
				{
					Log.AppendLine("  !! [gpuoc] 还原前的 VirtualProtect 失败，err=" + Marshal.GetLastWin32Error() + "（跳过还原；本进程即将退出）");
				}
				else
				{
					Marshal.Copy(_isGcu50OriginalBytes, 0, _isGcu50PatchTarget, _isGcu50PatchLength);
					VirtualProtect(_isGcu50PatchTarget, (IntPtr)_isGcu50PatchLength, oldProtect, out _);
					Log.AppendLine("  [gpuoc] IsGcu50 原机器码已写回（宿主进程即将退出，⛔ 产品 DLL 文件从未改动）");
				}
			}
			catch (Exception ex)
			{
				Log.AppendLine("  !! [gpuoc] 还原失败: " + ex.Message);
			}
			_isGcu50PatchTarget = IntPtr.Zero;
			_isGcu50OriginalBytes = null;
			_isGcu50PatchLength = 0;
		}
		SetStatic("MechrevoLite.Program", "service", null);
	}

	private static bool SafeIsGcu50()
	{
		try
		{
			return MechrevoService.IsGcu50;
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] 读 IsGcu50 抛异常: " + ex.Message);
			return false;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool VirtualProtect(IntPtr address, IntPtr size, uint newProtect, out uint oldProtect);

	private static IntPtr _isGcu50PatchTarget = IntPtr.Zero;

	private static byte[] _isGcu50OriginalBytes;

	private static int _isGcu50PatchLength;

	/// <summary>
	/// 2026-10-12（崩溃现场取证用 · ⛔ 不改变任何工具行为）：把当前日志**立刻**落盘一份
	/// <c>egroupshot-&lt;suffix&gt;.live.txt</c>。原生 AV（Fatal error）时托管栈会被直接带走，
	/// <c>finally</c> 里的 Flush 不保证还能跑到 ⇒ 关键节点前后各调用一次，保证崩溃前最后一步可见。
	/// </summary>
	private static void DumpLive(string tag)
	{
		try
		{
			File.WriteAllText(Path.Combine(_outDir, "egroupshot-" + _suffix + ".live.txt"), Log.ToString() + "\r\n[live] " + tag + "\r\n", new UTF8Encoding(true));
		}
		catch
		{
		}
	}

	/// <summary>
	/// 把 <c>MechrevoService.IsGcu50</c> 的 getter **就地**改成"判据恒真"（等价于在宿主进程里把
	/// <c>GcuVariant</c> 读成 <c>"50"</c>）：先用 <c>RuntimeHelpers.PrepareMethod</c> 把方法预编译，
	/// 然后按 <c>MethodHandle.GetFunctionPointer()</c> 取入口 —— 若入口是 <c>FF 25 rel32</c> 跳转桩
	/// （tiered / ReadyToRun 时机下很常见）就顺桩取**真机器码**地址，最后在原位写
	/// <c>B8 01 00 00 00 C3</c>（<c>mov eax,1; ret</c>，6B）。
	/// 为什么会先扫一遍 <c>cmp dword ptr [rax+8], 0x3A</c>（<c>"50".Length</c>）：那是"只改判定指令、
	/// 保留 try/catch 与注册表读取"的更克制写法；但本机实测该 getter 带异常处理、寄存器分配不同，
	/// 那条指令未必出现在可扫范围内 ⇒ 定位不到就退到**整方法桩化**（结果等价，风险最低：
	/// 写回原字节即可完全还原）。⛔ 只改宿主进程内存，⛔ 不改产品 DLL / 不写注册表。
	/// </summary>
	private static void HookIsGcu50True()
	{
		MethodInfo getter = typeof(MechrevoService).GetProperty("IsGcu50", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
		if (getter == null)
		{
			Log.AppendLine("  !! [gpuoc] 找不到 IsGcu50 getter");
			return;
		}
		// ① 首选：托管层"预编译"（打掉 tiering，让下面的机器码改写一次到位、不被后续分层编译覆盖）。
		try
		{
			System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(getter.MethodHandle);
			Log.AppendLine("  [gpuoc] hook=RuntimeHelpers.PrepareMethod（预编译 get_IsGcu50，避免分层编译覆盖下面的改写）");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  [gpuoc] PrepareMethod 失败（" + ex.GetType().Name + ": " + ex.Message + "）⇒ 直接改写机器码");
		}
		byte[] beforeBytes = ReadCode(getter, 32);
		bool before = SafeIsGcu50();
		IntPtr body = _isGcu50CodeBase;
		// tiered/ReadyToRun 时机下，方法入口往往是一条**跳转桩**（`FF 25 <rel32>` = jmp qword ptr [rip+rel]
		// 或 `4C 8B 15 <rel32>` + `FF 25 …` 的两条式）⇒ 真正的 JIT 机器码在桩的目标地址上，
		// 必须在**目标**里找那条判定指令，否则会像第一次那样"找不到"。
		IntPtr direct = ReadJumpTarget(_isGcu50CodeBase);
		if (direct != IntPtr.Zero)
		{
			body = direct;
			Log.AppendLine($"  [gpuoc] getter 入口是跳转桩 @0x{_isGcu50CodeBase.ToInt64():X} ⇒ 真机器码 @0x{direct.ToInt64():X}");
			beforeBytes = ReadBytes(body, 32);
		}
		IntPtr target = FindStringLengthCompare(beforeBytes, body);
		byte[] patch;
		if (target == IntPtr.Zero)
		{
			// 兜底：整个 getter 真机器码前 512B 里再扫一遍（判定可能在更靠前的寄存器搬移之后）。
			byte[] wider = ReadBytes(body, 512);
			target = FindStringLengthCompare(wider, body);
		}
		if (target == IntPtr.Zero)
		{
			// 定位不到那条判定指令 ⇒ 退到**入口 "真机器码" 首指令重定向**：
			//   本机 getter 入口是 `4C 8B 15 rel32`（mov r10, imm64 槽） + `FF 25 rel32`（jmp [槽]）两段式，
			//   真机器码开头就是那条 `mov r10, <真入口>`（10B: 4C 8B 15 …）。
			//   这里把那条 mov 的立即数改成**宿主自己的桩**（VirtualAlloc 出来的 `mov eax,1; ret`）——
			//   jmp [槽] 因此跳到宿主桩，等价于 IsGcu50 恒真。⛔ 不碰原方法的 prologue/栈帧（前两次 AV 就出在这）。
			//   桩只含 mov/ret，不调用任何东西 ⇒ 不需要 GC 转换；还原 = 写回原 10B。
			Log.AppendLine("  [gpuoc] 未定位到 `cmp dword ptr [rax+8], 0x3A` ⇒ 退到入口首指令重定向");
			byte[] head = ReadBytes(body, 48);
			Log.AppendLine("  [gpuoc] 真机器码前 48B = " + ToHex(head));
			if (head[0] != 0x55 || head[1] != 0x48 || head[2] != 0x83 || head[3] != 0xEC)
			{
				Log.AppendLine("  !! [gpuoc] 入口前 4B 不是 `55 48 83 EC`（push rbp; sub rsp,imm8）⇒ 不做机器码改写，旁路放弃");
				return;
			}
			// 本机实测真机器码：入口是 `push rbp ; sub rsp,0x60 ; lea rbp,[rsp+0x60]`，**没有** mov r10/mov r11
			// 那条 ready-to-run 前缀（之前的判断有误）。既然第一件事就是 prologue，就把它整段换成
			// `mov eax,1 ; ret`（6B）——**prologue 不再执行 ⇒ 栈本来就是平的**，那条 ret 直接返回给调用方，
			// 不需要任何拆帧（这正是前两次 AV 的根因：原来还在 `sub rsp` 之后才 ret）。
			target = body;
			patch = new byte[6] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };
			Log.AppendLine("  [gpuoc] 入口 prologue 覆盖为 mov eax,1; ret（6B，栈平）");
		}
		else
		{
			// 只改判定指令的克制写法：cmp dword ptr [rax+8], 0x3A ⇒ cmp dword ptr [rax+8], 0
			//（比较 ecx 那个恒为 0 的 _stringLength 字段自行抵消）⇒ string.Equals 判定为真，栈帧一字不动。
			patch = new byte[4] { 0, 0, 0, 0 };
		}
		_isGcu50PatchTarget = target;
		_isGcu50PatchLength = patch.Length;
		_isGcu50OriginalBytes = ReadBytes(target, patch.Length);
		if (!VirtualProtect(target, (IntPtr)patch.Length, 0x40u, out uint oldProtect))
		{
			Log.AppendLine("  !! [gpuoc] VirtualProtect 失败，err=" + Marshal.GetLastWin32Error());
			_isGcu50PatchTarget = IntPtr.Zero;
			_isGcu50OriginalBytes = null;
			_isGcu50PatchLength = 0;
			return;
		}
		Marshal.Copy(patch, 0, target, patch.Length);
		VirtualProtect(target, (IntPtr)patch.Length, oldProtect, out _);
		DumpLive("after-patch");
		bool after = SafeIsGcu50();
		Log.AppendLine($"  [gpuoc] hook=机器码旁路 @0x{target.ToInt64():X}（写 {patch.Length}B: {ToHex(patch)}）IsGcu50 {before} -> {after}；原前 32B = {ToHex(beforeBytes)}");
		if (!after)
		{
			Log.AppendLine("  !! [gpuoc] 旁路后 IsGcu50 仍为 False ⇒ 该夹具渲染不出「显卡超频」（其它夹具不受影响）");
		}
	}

	/// <summary>
	/// 分配一页可执行内存写入 <c>mov eax,1; ret</c>（<c>B8 01 00 00 00 C3</c>）作为"恒真"宿主桩。
	/// ⚠️ 本机当前产品 DLL 的 <c>get_IsGcu50</c> 真机器码开头就是 prologue（没有 ready-to-run 的
	/// <c>mov r11,[rip+rel]</c> 前缀），所以最终走的是"覆盖 prologue"那条路，本桩**当前未被调用**——
	/// 保留它是为了在别的运行时版本（入口真带 R2R 前缀）下仍有一条不碰栈帧的退路。
	/// </summary>
	private static IntPtr MakeTrueStub()
	{
		IntPtr intPtr = VirtualAlloc(IntPtr.Zero, (IntPtr)64, 0x3000u, 0x40u);
		if (intPtr == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		Marshal.Copy(new byte[6] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 }, 0, intPtr, 6);
		return intPtr;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr VirtualAlloc(IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

	/// <summary>
	/// 读一条可能存在的入口跳转桩，返回它跳往的地址（<c>FF 25 rel32</c> = jmp qword ptr [rip+rel32]；
	/// 也兼容 <c>4C 8B 15 rel32</c> + <c>FF 25 rel32</c> = mov r10,[rip+rel] ; jmp [rip+rel] 两条式）。
	/// 读不出来（入口就是真机器码）⇒ 返回 <see cref="IntPtr.Zero" />。
	/// </summary>
	private static IntPtr ReadJumpTarget(IntPtr entry)
	{
		try
		{
			byte[] head = ReadBytes(entry, 24);
			if (head[0] == 0xFF && head[1] == 0x25)
			{
				int rel = BitConverter.ToInt32(head, 2);
				return Marshal.ReadIntPtr((IntPtr)(entry.ToInt64() + 6 + rel));
			}
			if (head[0] == 0x4C && head[1] == 0x8B && head[2] == 0x15 && head[6] == 0xFF && head[7] == 0x25)
			{
				int rel2 = BitConverter.ToInt32(head, 8);
				return Marshal.ReadIntPtr((IntPtr)(entry.ToInt64() + 12 + rel2));
			}
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc] 读入口跳转桩失败: " + ex.Message);
		}
		return IntPtr.Zero;
	}

	/// <summary>在已 JIT 的 getter 机器码里找 <c>81 78 08 3A 00 00 00</c>（cmp dword ptr [rax+8], 0x3A）的立即数偏移。</summary>
	private static IntPtr FindStringLengthCompare(byte[] code, IntPtr codeBase)
	{
		if (code == null)
		{
			return IntPtr.Zero;
		}
		for (int i = 0; i + 6 < code.Length; i++)
		{
			if (code[i] == 0x81 && code[i + 1] == 0x78 && code[i + 2] == 0x08 && code[i + 3] == 0x3A && code[i + 4] == 0 && code[i + 5] == 0 && code[i + 6] == 0)
			{
				return (IntPtr)(codeBase.ToInt64() + i + 3);
			}
		}
		return IntPtr.Zero;
	}

	private static IntPtr _isGcu50CodeBase = IntPtr.Zero;

	private static byte[] ReadCode(MethodInfo method, int count)
	{
		_isGcu50CodeBase = method.MethodHandle.GetFunctionPointer();
		return ReadBytes(_isGcu50CodeBase, count);
	}

	private static byte[] ReadBytes(IntPtr address, int count)
	{
		byte[] array = new byte[count];
		Marshal.Copy(address, array, 0, count);
		return array;
	}

	private static string ToHex(byte[] bytes)
	{
		if (bytes == null)
		{
			return "(null)";
		}
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 3);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("X2")).Append(' ');
		}
		return stringBuilder.ToString().TrimEnd();
	}

	/// <summary>
	/// 造一份**只读**的合成 <c>MechrevoService</c>：走产品的 5 参数 internal 构造器
	/// （<c>MechrevoService.cs:311</c>），三个回调全部喂常量（⛔ 不读校色状态 / 不碰 HDR / 不写电源计划），
	/// 再把它的 <c>_hw</c> 指向夹具 hw，并喂一条合成 <c>Fan/Status</c> 把 <c>OperatingMode</c> 置成 <paramref name="opMode" />。
	/// ⛔ 不连 MQTT、⛔ 不下发任何命令、⛔ 不写配置。
	/// </summary>
	private static object MakeFixtureService(MechrevoHw hw, int opMode)
	{
		Type type = typeof(MechrevoService);
		Func<bool> readCalibOn = () => false;
		Func<int> readCalibMode = () => 0;
		Func<bool> readHdr = () => false;
		Func<bool, int, bool> syncPower = delegate(bool highPerf, int mode)
		{
			return true;
		};
		ConstructorInfo constructorInfo = null;
		foreach (ConstructorInfo candidate in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			ParameterInfo[] parameters = candidate.GetParameters();
			if (parameters.Length == 5 && parameters[4].ParameterType == typeof(Func<bool, int, bool>))
			{
				constructorInfo = candidate;
				break;
			}
		}
		if (constructorInfo == null)
		{
			Log.AppendLine("  !! [gpuoc] 找不到 MechrevoService 的 5 参数构造器");
			return null;
		}
		object service = constructorInfo.Invoke(new object[5] { hw, readCalibOn, readCalibMode, readHdr, syncPower });
		SetInstanceField(service, "_hw", hw);
		SendHw(hw, "Fan/Status", "{\"OperatingMode\":" + opMode + ",\"IsAC\":false,\"FanBoostEnable\":false}");
		Log.AppendLine($"  [gpuoc] 合成服务: hw.OperatingMode={hw.OperatingMode} (喂 OperatingMode={opMode})");
		return service;
	}

	/// <summary>
	/// 2026-10-12（本轮 ·「显卡超频」开关取证）：把该开关**按产品原样渲染**出来的那一片放大出图
	/// （⛔ 只读渲染：DrawToBitmap + 缩放，⛔ 不点击、⛔ 不写配置/硬件）。走 <c>LMECHREVO_SHOT_SWITCHZOOM=1</c>。
	/// </summary>
	private static void SwitchZoom(Form form, string fixtureName, string viewportId)
	{
		if (!_shots || form == null || !string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_SWITCHZOOM"), "1", StringComparison.Ordinal))
		{
			return;
		}
		try
		{
			Control box = Find(form, "gpu_oc");
			if (box == null)
			{
				Log.AppendLine("  switch-zoom: 未找到 gpu_oc（该夹具下 IsGcu50 未判真）");
				return;
			}
			int zoom = 4;
			if (int.TryParse(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_SWITCHZOOM_SCALE"), out int parsed) && parsed >= 2 && parsed <= 8)
			{
				zoom = parsed;
			}
			// ⚠️ 不用"从整窗位图裁一块"：本机实测窗体位图与客户区坐标在这条路径上差 ~8px
			//（裁出来的那块是纯底色）。改成**直接用控件自己的位图**放大 —— 那正是产品自绘出来的像素
			//（同一次运行里 box.DrawToBitmap 颜色数 = 116，已确认自绘确实落到了位图上）。
			using Bitmap own = new Bitmap(Math.Max(1, box.Width), Math.Max(1, box.Height), PixelFormat.Format32bppArgb);
			box.DrawToBitmap(own, new Rectangle(Point.Empty, own.Size));
			own.Save(Path.Combine(_shotsDir, $"switch-{fixtureName}-{viewportId}.png"), ImageFormat.Png);
			using Bitmap zoomed = new Bitmap(own.Width * zoom, own.Height * zoom, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(zoomed))
			{
				graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
				graphics.PixelOffsetMode = PixelOffsetMode.Half;
				graphics.DrawImage(own, new Rectangle(0, 0, zoomed.Width, zoomed.Height));
			}
			string path = Path.Combine(_shotsDir, $"switchzoom-{fixtureName}-{viewportId}.png");
			zoomed.Save(path, ImageFormat.Png);
			Log.AppendLine($"  switch-zoom={path}（源 = 控件自身 {own.Width}x{own.Height} × {zoom}，控件矩形 {box.Bounds}）");
			// 2026-10-12 追加：**带上下文**的放大（"性能模式"标题 + 该开关）——直接按两个控件的矩形并集，
			// 从**窗体整图**里裁一段再放大。⚠️ 实测窗体整图与客户区坐标有 ~8px 偏移，故四周留 8px 余量。
			Control title = Find(form, "labelPerf");
			if (title != null)
			{
				Rectangle union = Rectangle.Union(new Rectangle(CardFormPoint(form, title), title.Size), new Rectangle(CardFormPoint(form, box), box.Size));
				// 2026-10-12：若同一格里还开着别的开关（`LMECHREVO_FORCE_30UI=1` 时的「配置编辑」），
				// 就把**整个 perfShortcuts 容器**并进来 —— 这样"两者共用同一格、只是不会同时出现"这件事
				// 在一张图里就能看出来（⛔ 只是出图范围，不动任何显隐判据）。
				Control flow2 = Find(form, "perfShortcuts");
				if (flow2 != null && flow2.Visible && flow2.Width > 0)
				{
					union = Rectangle.Union(union, new Rectangle(CardFormPoint(form, flow2), flow2.Size));
				}
				Rectangle region = Rectangle.Intersect(new Rectangle(union.X - 10, union.Y + 26, union.Width + 24, union.Height + 28), new Rectangle(0, 0, form.Width, form.Height));
				if (region.Width > 0 && region.Height > 0)
				{
					using Bitmap whole = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height), PixelFormat.Format32bppArgb);
					form.DrawToBitmap(whole, new Rectangle(Point.Empty, whole.Size));
					int zoom2 = Math.Min(zoom, 3);
					using Bitmap ctx = new Bitmap(region.Width * zoom2, region.Height * zoom2, PixelFormat.Format32bppArgb);
					using (Graphics g2 = Graphics.FromImage(ctx))
					{
						g2.InterpolationMode = InterpolationMode.NearestNeighbor;
						g2.PixelOffsetMode = PixelOffsetMode.Half;
						g2.DrawImage(whole, new Rectangle(0, 0, ctx.Width, ctx.Height), region, GraphicsUnit.Pixel);
					}
					string path2 = Path.Combine(_shotsDir, $"switchctx-{fixtureName}-{viewportId}.png");
					ctx.Save(path2, ImageFormat.Png);
					Log.AppendLine($"  switch-ctx={path2}（标题+开关 并集 {union} 裁 {region} × {zoom2}）");
				}
			}
		}
		catch (Exception ex)
		{
			Log.AppendLine("  switch-zoom 失败: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-12（用户原话「我发现它是底边和标题对齐的 能不能改成中线的标题对齐」）：
	/// 把「性能模式」标题（<c>labelPerf</c>）与 <c>perfShortcuts</c> 里那两颗开关
	/// （<c>gpu_oc</c> / <c>perfConfigEditButton</c>）的**垂直几何**全部换算到**同一坐标系**
	/// （窗体客户区，device px）后写出：y / h / bottom / 中线，外加 Anchor / Margin / 父链；
	/// 并对开关自身位图扫一遍"非底色行范围" —— 那是**看得见的像素**的上下界
	/// （用户说的"底边对齐"看的就是它）。⛔ 只读：只读属性 + <c>DrawToBitmap</c>。
	/// </summary>
	private static void DumpSwitchAlign(Form form, string fixtureName, string viewportId)
	{
		try
		{
			Control title = Find(form, "labelPerf");
			Control flow = Find(form, "perfShortcuts");
			Control row = Find(form, "perfHeaderRow");
			Control left = Find(form, "perfHeaderRowLeft");
			if (title == null || flow == null || row == null)
			{
				Log.AppendLine("  [align] 跳过：labelPerf/perfShortcuts/perfHeaderRow 有缺失");
				return;
			}
			Point tp = CardFormPoint(form, title);
			int titleCenter2 = 2 * tp.Y + title.Height;
			Log.AppendLine("  [align] ===== 「性能模式」标题中线 vs 快捷开关中线（窗体客户区坐标, device px）=====");
			Log.AppendLine($"  [align] 行{AlignShape(form, row)}");
			Log.AppendLine($"  [align] 左组{AlignShape(form, left)}");
			Log.AppendLine($"  [align] 容器{AlignShape(form, flow)} 子={string.Join(";", flow.Controls.Cast<Control>().Select((Control c) => $"{c.Name}[h={c.Height},m={c.Margin},anchor={c.Anchor},vis={c.Visible}]"))}");
			Log.AppendLine($"  [align] 标题{AlignShape(form, title)} text='{Oneline(title.Text)}' font={title.Font.Name} {title.Font.SizeInPoints}pt");
			foreach (string name in new string[2] { "gpu_oc", "perfConfigEditButton" })
			{
				Control c = Find(form, name);
				if (c == null)
				{
					// ⚠️「配置编辑」这颗 RButton **没有 Name**（产品只在字段 perfConfigEditButton 上持有它）
					// ⇒ 按名字找不到时改读私有字段（只读反射，⛔ 不动产品代码）。
					c = FindByField(form, name);
				}
				if (c == null)
				{
					Log.AppendLine($"  [align] {name}: 不存在");
					continue;
				}
				Point cp = CardFormPoint(form, c);
				int center2 = 2 * cp.Y + c.Height;
				int delta2 = titleCenter2 - center2;
				int bottomDelta = (tp.Y + title.Height) - (cp.Y + c.Height);
				string pixels = ((c.Visible && c.Width > 0) ? PixelRowRange(c) : "(不可见/宽 0 ⇒ 不扫像素)");
				string line = $"{name}: Δ中线={delta2 / 2.0:F1}px(2Δ={delta2})  底边差={bottomDelta}px  像素={pixels} || 开关{AlignShape(form, c)}";
				Log.AppendLine("  [align] 开关 " + line);
				Rows.Add(new string[5] { "SwitchAlign", fixtureName + "@" + viewportId, name, ControlPath(c), line });
			}
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [align] " + ex.Message);
		}
	}

	/// <summary>按产品窗体上的私有字段名取控件（只读反射；用于没有 Name 的控件，如 perfConfigEditButton）。</summary>
	private static Control FindByField(Form form, string fieldName)
	{
		try
		{
			FieldInfo f = form.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return f?.GetValue(form) as Control;
		}
		catch
		{
			return null;
		}
	}

	private static string AlignShape(Form form, Control c)
	{
		if (c == null)
		{
			return "MISSING";
		}
		Point p = CardFormPoint(form, c);
		int center2 = 2 * p.Y + c.Height;
		return $"{c.Name} y={p.Y} h={c.Height} bottom={p.Y + c.Height} 中线={center2 / 2.0:F1}(2C={center2}) anchor={c.Anchor} margin={c.Margin} parent={c.Parent?.Name} vis={c.Visible}";
	}

	/// <summary>扫控件自身位图里的"非底色行范围"= 看得见的像素上下界（底色取 (0,0) 的颜色）。</summary>
	private static string PixelRowRange(Control c)
	{
		try
		{
			using Bitmap bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), PixelFormat.Format32bppArgb);
			c.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
			int bg = bmp.GetPixel(0, 0).ToArgb();
			int top = -1;
			int bottom = -1;
			for (int y = 0; y < bmp.Height; y++)
			{
				bool any = false;
				for (int x = 0; x < bmp.Width; x++)
				{
					if (bmp.GetPixel(x, y).ToArgb() != bg)
					{
						any = true;
						break;
					}
				}
				if (any)
				{
					if (top < 0)
					{
						top = y;
					}
					bottom = y;
				}
			}
			return ((top < 0) ? "整块同底色(自绘没落到位图)" : $"可见行={top}..{bottom}(h={bottom - top + 1}, 2C={2 * top + (bottom - top + 1)})");
		}
		catch (Exception ex)
		{
			return "像素扫描失败:" + ex.Message;
		}
	}

	/// <summary>
	/// 2026-10-12（本轮 · 中线对齐出图）：把「标题 + 快捷开关组」那一片从**窗体整图**里裁出来放大 ×N，
	/// 并在放大图上画出**两条中线**：品红 = 标题中线，青 = 开关中线（每个可见开关各一条）
	/// ⇒ "底边对齐 / 中线对齐"一眼可判。走 <c>LMECHREVO_SHOT_ALIGNZOOM=1</c>。
	/// ⚠️ 窗体整图 = 客户区 + 非客户区偏移（frameX/frameY），裁剪矩形必须补上它（本文件 CardCrop 同口径）。
	/// ⛔ 只读渲染：DrawToBitmap + 画辅助线，⛔ 不点任何按钮、⛔ 不写配置/硬件。
	/// </summary>
	private static void AlignZoom(Form form, string fixtureName, string viewportId)
	{
		if (!_shots || form == null || !string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_ALIGNZOOM"), "1", StringComparison.Ordinal))
		{
			return;
		}
		try
		{
			Control title = Find(form, "labelPerf");
			Control flow = Find(form, "perfShortcuts");
			if (title == null || flow == null || flow.Width <= 0)
			{
				Log.AppendLine("  align-zoom: labelPerf/perfShortcuts 不可用（跳过）");
				return;
			}
			int zoom = 3;
			if (int.TryParse(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_ALIGNZOOM_SCALE"), out int parsed) && parsed >= 2 && parsed <= 8)
			{
				zoom = parsed;
			}
			Point tp = CardFormPoint(form, title);
			Point fp = CardFormPoint(form, flow);
			Rectangle union = Rectangle.Union(new Rectangle(tp, title.Size), new Rectangle(fp, flow.Size));
			int frameY = Math.Max(0, form.Height - form.ClientSize.Height);
			int frameX = Math.Max(0, (form.Width - form.ClientSize.Width) / 2);
			Rectangle region = Rectangle.Intersect(new Rectangle(union.X + frameX - 8, union.Y + frameY - 8, union.Width + 16, union.Height + 16), new Rectangle(0, 0, form.Width, form.Height));
			if (region.Width <= 0 || region.Height <= 0)
			{
				Log.AppendLine("  align-zoom: 裁剪区为空（跳过）");
				return;
			}
			using Bitmap whole = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height), PixelFormat.Format32bppArgb);
			form.DrawToBitmap(whole, new Rectangle(Point.Empty, whole.Size));
			using Bitmap zoomed = new Bitmap(region.Width * zoom, region.Height * zoom, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(zoomed))
			{
				g.InterpolationMode = InterpolationMode.NearestNeighbor;
				g.PixelOffsetMode = PixelOffsetMode.Half;
				g.DrawImage(whole, new Rectangle(0, 0, zoomed.Width, zoomed.Height), region, GraphicsUnit.Pixel);
			}
			// 辅助线：品红 = 标题中线；青 = 开关中线（每个可见开关一条，另加左右两端短竖线标出控件横向范围）
			using (Graphics g = Graphics.FromImage(zoomed))
			{
				int titleCenter = (tp.Y + title.Height / 2 + frameY - region.Y) * zoom;
				using Pen titlePen = new Pen(Color.FromArgb(255, 255, 0, 255), 1f);
				g.DrawLine(titlePen, 0, titleCenter, zoomed.Width - 1, titleCenter);
				int titleL = (tp.X + frameX - region.X) * zoom;
				int titleR = (tp.X + title.Width + frameX - region.X) * zoom;
				g.DrawLine(titlePen, titleL, titleCenter - 3, titleL, titleCenter + 3);
				g.DrawLine(titlePen, titleR, titleCenter - 3, titleR, titleCenter + 3);
				using Pen switchPen = new Pen(Color.FromArgb(255, 0, 255, 255), 1f);
				foreach (Control c in flow.Controls.Cast<Control>())
				{
					if (!c.Visible || c.Width <= 0 || c.Height <= 0)
					{
						continue;
					}
					Point cp = CardFormPoint(form, c);
					int cy = (cp.Y + c.Height / 2 + frameY - region.Y) * zoom;
					g.DrawLine(switchPen, 0, cy, zoomed.Width - 1, cy);
					int cl = (cp.X + frameX - region.X) * zoom;
					int cr = (cp.X + c.Width + frameX - region.X) * zoom;
					g.DrawLine(switchPen, cl, cy - 3, cl, cy + 3);
					g.DrawLine(switchPen, cr, cy - 3, cr, cy + 3);
				}
			}
			string path = Path.Combine(_shotsDir, $"alignzoom-{fixtureName}-{viewportId}.png");
			zoomed.Save(path, ImageFormat.Png);
			Point tp2 = CardFormPoint(form, title);
			Point fp2 = CardFormPoint(form, flow);
			Log.AppendLine($"  align-zoom={path}（并集 {union} 裁 {region} × {zoom}；标题中线客户区 y={tp2.Y + title.Height / 2}；容器 {fp2.Y}..{fp2.Y + flow.Height}）");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  align-zoom 失败: " + ex.Message);
		}
	}

	/// <summary>把该开关的**外观事实**（类型/名字/文本/矩形/字号/前景色/选中态/底色）写进日志与汇总表。</summary>
	private static void DumpGpuOcBox(Form form, string fixtureName, string viewportId)
	{
		Control box = Find(form, "gpu_oc");
		if (box == null)
		{
			Log.AppendLine("  [gpuoc-style] 控件 gpu_oc 不存在（IsGcu50 未判真 ⇒ 该夹具渲染不出开关）");
			return;
		}
		Color fore = box.ForeColor;
		Color back = box.BackColor;
		object contextColor = null;
		try
		{
			// UiVisualStyle 是 internal（产品只对 MechrevoLite.Tests 开 InternalsVisibleTo）
			// ⇒ 这里走 product DLL 的反射，⛔ 不给工具加任何产品侧的可见性。
			Type styleType = _asm.GetType("MechrevoLite.UI.UiVisualStyle");
			MethodInfo context = styleType?.GetMethod("ContextBackColor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			contextColor = context?.Invoke(null, new object[1] { box });
		}
		catch
		{
		}
		string detail = $"type={box.GetType().FullName} name={box.Name} text='{box.Text}' bounds={box.Bounds} sizeDevice={box.Width}x{box.Height} visible={box.Visible} enabled={box.Enabled} font={box.Font.Name} {box.Font.SizeInPoints}pt checked={(box as CheckBox)?.CheckState} fore=#{fore.R:X2}{fore.G:X2}{fore.B:X2} back=#{back.R:X2}{back.G:X2}{back.B:X2} parent={box.Parent?.Name} 实际底色={contextColor}";
		Log.AppendLine("  [gpuoc-style] " + detail);
		if (box is RCheckBox rcb)
		{
			string switchDetail = $"SwitchOnRight={rcb.SwitchOnRight} ShowStateText={rcb.ShowStateText} BorderRadius={rcb.BorderRadius} TrackColor={(rcb.TrackColor.HasValue ? rcb.TrackColor.Value.ToString() : "(null⇒用主题 Track/Input)")} AutoSize={rcb.AutoSize} Margin={rcb.Margin}";
			Log.AppendLine("  [gpuoc-style] RCheckBox 属性: " + switchDetail);
			detail += " | " + switchDetail;
		}
		Rows.Add(new string[5] { "GpuOcStyle", fixtureName + "@" + viewportId, "gpuoc-style", ControlPath(box), detail });
	}

	/// <summary>
	/// 2026-10-12（本轮出图自检 · 只读）：回答"那颗开关此刻到底有没有画出像素"——
	/// ① <c>box.DrawToBitmap</c> 的颜色数；② 直接从屏幕抓该控件的矩形。两者都为 1 色 ⇒ 屏幕上真的没有像素。
	/// </summary>
	private static void DumpGpuOcBoxProbe(Form form, string fixtureName, string viewportId)
	{
		try
		{
			Control box = Find(form, "gpu_oc");
			Control flow = Find(form, "perfShortcuts");
			Log.AppendLine("  [gpuoc-probe] gpu_oc=" + ((box != null) ? "命中" : "未命中") + "; perfShortcuts 子=" + string.Join(",", (flow?.Controls.Cast<Control>().Select((Control c) => c.Name + "[vis=" + c.Visible + "," + c.Bounds + "]"))) );
			if (box == null || box.Width <= 0 || box.Height <= 0)
			{
				return;
			}
			using Bitmap own = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
			box.DrawToBitmap(own, new Rectangle(Point.Empty, own.Size));
			Log.AppendLine($"  [gpuoc-probe] box.DrawToBitmap {own.Width}x{own.Height} 颜色数={DistinctColors(own)} (1 = 整块同色 ⇒ 自绘没出来)");
			own.Save(Path.Combine(_shotsDir, $"switchbox-draw-{fixtureName}-{viewportId}.png"), ImageFormat.Png);
			Point onScreen = box.PointToScreen(Point.Empty);
			using Bitmap screen = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(screen))
			{
				graphics.CopyFromScreen(onScreen, Point.Empty, new Size(box.Width, box.Height), CopyPixelOperation.SourceCopy);
			}
			Log.AppendLine($"  [gpuoc-probe] 屏幕抓取 @{onScreen.X},{onScreen.Y} 颜色数={DistinctColors(screen)}");
			screen.Save(Path.Combine(_shotsDir, $"switchbox-screen-{fixtureName}-{viewportId}.png"), ImageFormat.Png);
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! [gpuoc-probe] " + ex.Message);
		}
	}

	/// <summary>数一张小图里出现了多少种颜色（判断"自绘控件到底画出来没有"）。</summary>
	private static int DistinctColors(Bitmap bitmap)
	{
		HashSet<int> hashSet = new HashSet<int>();
		for (int y = 0; y < bitmap.Height; y++)
		{
			for (int x = 0; x < bitmap.Width; x++)
			{
				hashSet.Add(bitmap.GetPixel(x, y).ToArgb());
			}
		}
		return hashSet.Count;
	}

	private static void ApplyShotTheme(Form form)
	{
		string text = Environment.GetEnvironmentVariable("LMECHREVO_SHOT_THEME");
		if (form == null || string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		bool flag = !(text.Equals("day", StringComparison.OrdinalIgnoreCase) || text.Equals("light", StringComparison.OrdinalIgnoreCase));
		Invoke(form, "ApplyThemeMode", flag);
		ResponsiveLayout.PerformLayoutTree(form);
		Application.DoEvents();
		Log.AppendLine($"  [theme] LMECHREVO_SHOT_THEME={text} ⇒ ApplyThemeMode(night: {flag})");
	}

	/// <summary>
	/// 2026-10-11（本轮取证）：把**某一张卡片**单独出图。
	/// 做法：先按控件路径子串找到那颗控件，算出它在窗体客户区里的矩形；把它的父容器（Dashboard 的那条栈）
	/// 临时上移，使该卡片落到客户区 <c>CARD_CROP_Y</c> 处；`PerformLayout` + `DrawToBitmap` 之后再按
	/// **该卡片自己的矩形**裁一段出来存盘。⛔ 只动一次布局坐标、只读渲染，不碰硬件/配置/注册表。
	/// </summary>
	private static void CardCrop(Form form, string fixtureName, string viewportId)
	{
		string pathFilterRaw = Environment.GetEnvironmentVariable("CARD_CROP_PATH");
		if (string.IsNullOrWhiteSpace(pathFilterRaw) || !_shots || form == null)
		{
			return;
		}
		string outName = Environment.GetEnvironmentVariable("CARD_CROP_NAME");
		if (string.IsNullOrWhiteSpace(outName))
		{
			outName = "card";
		}
		// ⚠️ 匹配口径：`ControlPath(control)` **本身就是"从最外层父控件到控件的完整路径"**（已含祖先链，
		// 见该方法实现）—— 所以这里直接用它匹配就行，⛔ 不要再自己拼一层（第一次跑就是这么拼错的）。
		Dictionary<string, Control> byPath = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
		foreach (Control control in AllControls(form))
		{
			string full = ControlPath(control);
			if (!byPath.ContainsKey(full))
			{
				byPath[full] = control;
			}
		}
		int wantY = 24;
		if (int.TryParse(Environment.GetEnvironmentVariable("CARD_CROP_Y"), out int parsedY))
		{
			wantY = parsedY;
		}
		string pathFilter = pathFilterRaw.Trim();
		Control card = null;
		foreach (KeyValuePair<string, Control> pair in byPath)
		{
			if (pair.Key.EndsWith(pathFilter, StringComparison.OrdinalIgnoreCase) || pair.Key.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				card = pair.Value;
				break;
			}
		}
		if (card == null)
		{
			Log.AppendLine($"  card-crop: 未找到路径含 '{pathFilter}' 的控件（跳过）; paths={byPath.Count}; 含 Performance 的路径样本: " + string.Join(" | ", byPath.Keys.Where((string k) => k.IndexOf("erformance", StringComparison.OrdinalIgnoreCase) >= 0).Take(4)));
			return;
		}
		Control mover = card.Parent;
		// 2026-10-12（本轮「睡眠省电」标题行取证）：卡片若排在可视页下方，CardCrop 原来的"挪父容器"
		// 对它无效（父容器只是整页里的一层，睡眠行在 dashboardStack 的 y≈1150）。这里改成**走产品自己的
		// 滚动条**：`dashboardScrollBar.Value = N`（ValueChanged → UpdateDashboardScroll → _dashboardStack.Top = -N）。
		// `CARD_CROP_SCROLL=card` = 自动算出"让卡片正好落进 CARD_CROP_Y"的那个值。
		// ⛔ 只是挪一次滚动位置 + 只读渲染；不写任何配置/硬件。
		bool scrolled = false;
		string scrollEnv = Environment.GetEnvironmentVariable("CARD_CROP_SCROLL");
		bool wantScroll = string.Equals(scrollEnv, "card", StringComparison.OrdinalIgnoreCase) || int.TryParse(scrollEnv, out _);
		if (wantScroll && form is SettingsForm sf)
		{
			int scrollTo = string.Equals(scrollEnv, "card", StringComparison.OrdinalIgnoreCase)
				? CardFormPoint(form, card).Y - wantY
				: int.Parse(scrollEnv);
			try
			{
				FieldInfo sbField = typeof(SettingsForm).GetField("_dashboardScrollBar", BindingFlags.Instance | BindingFlags.NonPublic);
				object sbObj = sbField?.GetValue(sf);
				FieldInfo stackField = typeof(SettingsForm).GetField("_dashboardStack", BindingFlags.Instance | BindingFlags.NonPublic);
				Control stackObj = (Control)stackField?.GetValue(sf);
				if (sbObj != null)
				{
					PropertyInfo valProp = sbObj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					PropertyInfo maxProp = sbObj.GetType().GetProperty("MaxValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					int max = Convert.ToInt32(maxProp.GetValue(sbObj));
					int applied = Math.Max(0, Math.Min(scrollTo, max));
					valProp.SetValue(sbObj, applied);
					// ⚠️ 产品滚动条的范围在 ShowAll 期间可能还没重算（MaxValue < 需要的值）⇒ 再直接挪一次
					//    dashboardStack 到同一个位置（等价于 UpdateDashboardScroll 的 `Top = -Value`），
					//    只为把卡片挪进可视区，⛔ 不改任何产品状态、不写配置。
					if (stackObj != null && applied < scrollTo && scrollTo > max)
					{
						applied = scrollTo;
						stackObj.Top = -scrollTo;
					}
					ResponsiveLayout.PerformLayoutTree(form);
					Application.DoEvents();
					scrolled = true;
					Log.AppendLine($"  card-crop-scroll: 请求 {scrollTo} -> 应用 {applied} (MaxValue={max}, stack.Top={(stackObj?.Top.ToString() ?? "n/a")})；卡片现在客户区 y={CardFormPoint(form, card).Y}（目标 {wantY}）");
				}
			}
			catch (Exception ex)
			{
				Log.AppendLine("  card-crop-scroll 失败: " + ex.Message);
			}
		}
		Point cardInForm = CardFormPoint(form, card);
		if (mover != null && !scrolled)
		{
			int dy = wantY - cardInForm.Y;
			if (dy != 0)
			{
				mover.Top += dy;
				ResponsiveLayout.PerformLayoutTree(form);
				Application.DoEvents();
			}
		}
		// ⚠️ `DrawToBitmap` 画的是**整窗**（含标题栏/边框），而上面算的是**客户区**坐标 ⇒ 必须把
		// 非客户区高度补上（本机 = Form.Height − ClientSize.Height，实测 31），否则卡片会正好被
		// 标题栏顶掉、裁到它的上一行（本轮第二次跑就是这样）。
		int frameY = Math.Max(0, form.Height - form.ClientSize.Height);
		int frameX = Math.Max(0, (form.Width - form.ClientSize.Width) / 2);
		Point finalInForm = CardFormPoint(form, card);
		int clipTop = Math.Min(0, finalInForm.Y);
		int clipLeft = Math.Min(0, finalInForm.X);
		int width = (int)Math.Ceiling((float)card.Width - (float)clipLeft);
		int height = (int)Math.Ceiling((float)card.Height - (float)clipTop);
		// ⚠️ 可选高度覆盖：卡片后面那几行（按钮行 / 其它行）本轮不需要时，可以只出"标题行那一段"。
		if (int.TryParse(Environment.GetEnvironmentVariable("CARD_CROP_H"), out int cropH) && cropH > 0)
		{
			height = Math.Min(height, cropH);
		}
		width = Math.Min(width, Math.Max(1, form.Width));
		height = Math.Min(height, Math.Max(1, form.Height));
		if (width <= 0 || height <= 0)
		{
			Log.AppendLine($"  card-crop: 卡片矩形不可用 {card.Width}x{card.Height}（跳过）");
			return;
		}
		string outPath = Path.Combine(_shotsDir, $"card-{outName}-{fixtureName}-{viewportId}.png");
		using (Bitmap cardBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
		{
			cardBitmap.SetResolution(96f, 96f);
			using (Graphics graphics = Graphics.FromImage(cardBitmap))
			{
				graphics.Clear(Color.FromArgb(32, 32, 32));
				graphics.TranslateTransform(-(finalInForm.X + frameX), -(finalInForm.Y + frameY));
				using Bitmap whole = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height), PixelFormat.Format32bppArgb);
				form.DrawToBitmap(whole, new Rectangle(Point.Empty, whole.Size));
				graphics.DrawImageUnscaled(whole, 0, 0);
			}
			cardBitmap.Save(outPath, ImageFormat.Png);
		}
		Log.AppendLine($"  card-crop={outPath} ({width}x{height}, card in client {finalInForm.X},{finalInForm.Y}, frame {frameX},{frameY})");
	}

	/// <summary>把控件自己的 <c>Location</c> 沿父链一路加到窗体客户区坐标。</summary>
	private static Point CardFormPoint(Form form, Control control)
	{
		int x = 0;
		int y = 0;
		for (Control c = control; c != null && c != form; c = c.Parent)
		{
			x += c.Location.X;
			y += c.Location.Y;
			if (c.Parent == null)
			{
				break;
			}
		}
		return new Point(x, y);
	}

	private static MechrevoHw MakeHw(MechrevoDeviceCapabilities caps)
	{
		foreach (ConstructorInfo constructor in typeof(MechrevoHw).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			ParameterInfo[] parameters = constructor.GetParameters();
			if (parameters.Length == 2 && parameters[1].ParameterType == typeof(MechrevoDeviceCapabilities))
			{
				return (MechrevoHw)constructor.Invoke(new object[2] { null, caps });
			}
		}
		return null;
	}

	private static object NewIssueList()
	{
		return Activator.CreateInstance(typeof(List<>).MakeGenericType(_issueType));
	}

	// 把 GCU 风格的合成状态喂给 fixture hw（MechrevoHw.HandleMessage 是 internal -> 反射）。
	private static void SendHw(MechrevoHw hw, string topic, string payload)
	{
		if (hw == null)
		{
			return;
		}
		try
		{
			Invoke(hw, "HandleMessage", topic, payload);
		}
		catch (Exception ex)
		{
			Log.AppendLine("  !! HandleMessage failed: " + ex.Message);
		}
	}

	private static void DumpIssues(string fixture, string viewportId, object issues)
	{
		int num = 0;
		foreach (object issue in (IEnumerable)issues)
		{
			string kind = Str(issue, "Kind");
			string control = Str(issue, "Control");
			string detail = Str(issue, "Detail");
			Rows.Add(new string[5] { fixture, viewportId, kind, control, detail });
			Log.AppendLine($"  [{kind}] {control}: {detail}");
			num++;
		}
		Log.AppendLine($"  issues={((num == 0) ? "0 (PASS)" : num.ToString())}");
	}

	/// <summary>
	/// 2026-10-12（本轮遗留项④ · 「＋」面板**端到端**只读取证）：
	/// 在宿主**自己的隔离配置**上走用户真机点「＋」→ 勾选 → 确定 时的**同一段产品代码**：
	///   QuickTilePickerForm 真实构造器（可选项来自产品的 QuickSwitchGroups / QuickTileText /
	///   QuickTileSupported）→ 勾选到满 → 再勾第 6 个看上限拦截 → OnConfirm()（「确定」按钮的处理器本体）
	///   → SettingsForm.SaveQuickTileKeys(perf_quick_tiles) → AppConfig.Flush()（产品关机时同一条落盘路径）
	///   → LoadQuickTileKeys 读回 → SettingsForm.ApplyQuickTiles() 重建磁贴行 → 逐项核对 → 恢复原值。
	/// ⛔ 不显示模态窗（不阻塞宿主）、⛔ 不碰用户真实配置（本工具已把 LMECHREVO_CONFIG_FILE 指到 tmp 文件）、
	/// ⛔ 不点任何开关、⛔ 不写硬件/注册表。
	/// </summary>
	private static void RunPickerRoundTrip(Form form)
	{
		Type type = _asm.GetType("MechrevoLite.QuickTilePickerForm");
		Type type2 = _asm.GetType("MechrevoLite.SettingsForm");
		MethodInfo methodInfo = type2.GetMethod("LoadQuickTileKeys", BindingFlags.Static | BindingFlags.NonPublic);
		MethodInfo methodInfo2 = type2.GetMethod("SaveQuickTileKeys", BindingFlags.Static | BindingFlags.NonPublic);
		MethodInfo methodInfo3 = type2.GetMethod("QuickTileText", BindingFlags.Static | BindingFlags.NonPublic);
		MethodInfo methodInfo4 = type2.GetMethod("QuickTileSupported", BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo fieldInfo = type2.GetField("QuickSwitchGroups", BindingFlags.Static | BindingFlags.NonPublic);
		List<string> list = (List<string>)methodInfo.Invoke(null, null);
		Log.AppendLine("  [picker-e2e] before perf_quick_tiles = " + string.Join(",", list));
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		List<bool> list4 = new List<bool>();
		foreach (object item in (Array)fieldInfo.GetValue(null))
		{
			string[] array = (string[])item.GetType().GetField("Item2").GetValue(item);
			if (array.Length == 0 || array[0].StartsWith("sleep", StringComparison.Ordinal))
			{
				continue;
			}
			foreach (string text in array)
			{
				if (!list2.Contains(text))
				{
					list2.Add(text);
					list3.Add((string)methodInfo3.Invoke(null, new object[1] { text }));
					list4.Add((bool)methodInfo4.Invoke(form, new object[1] { text }));
				}
			}
		}
		Log.AppendLine($"  [picker-e2e] options={list2.Count} supported={list4.Count((bool b) => b)}");
		ConstructorInfo constructorInfo = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
		object obj = constructorInfo.Invoke(new object[5] { list2, list3, list4, list, 5 });
		Form form2 = (Form)obj;
		FieldInfo fieldInfo2 = type.GetField("_boxes", BindingFlags.Instance | BindingFlags.NonPublic);
		IList list5 = (IList)fieldInfo2.GetValue(obj);
		foreach (object item2 in list5)
		{
			((CheckBox)item2).Checked = false;
		}
		int num = 0;
		foreach (object item3 in list5)
		{
			CheckBox checkBox = (CheckBox)item3;
			if (checkBox.Enabled && num < 5)
			{
				checkBox.Checked = true;
				num++;
			}
		}
		string text2 = "(无可勾的第 6 项)";
		foreach (object item4 in list5)
		{
			CheckBox checkBox2 = (CheckBox)item4;
			if (checkBox2.Enabled && !checkBox2.Checked)
			{
				checkBox2.Checked = true;
				text2 = $"checkedAfter={checkBox2.Checked}（期望 False = 上限拦截生效）";
				break;
			}
		}
		Log.AppendLine("  [picker-e2e] 第 6 项: " + text2);
		type.GetMethod("OnConfirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
		List<string> list6 = (List<string>)type.GetProperty("SelectedKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
		Log.AppendLine($"  [picker-e2e] confirm: DialogResult={form2.DialogResult} selected={string.Join(",", list6)}");
		form2.Dispose();
		methodInfo2.Invoke(null, new object[1] { list6 });
		AppConfig.Flush();
		List<string> list7 = (List<string>)methodInfo.Invoke(null, null);
		Log.AppendLine("  [picker-e2e] after  perf_quick_tiles = " + string.Join(",", list7) + " roundTrip=" + (string.Join(",", list6) == string.Join(",", list7)));
		string path = Environment.GetEnvironmentVariable("LMECHREVO_CONFIG_FILE");
		string text3 = (File.Exists(path) ? File.ReadAllText(path) : "");
		Log.AppendLine("  [picker-e2e] 落盘文件含 perf_quick_tiles=" + (text3.IndexOf("perf_quick_tiles", StringComparison.Ordinal) >= 0) + " cfg=" + path);
		Invoke(form, "ApplyQuickTiles");
		Application.DoEvents();
		Log.AppendLine("  [picker-e2e] 重建后的磁贴行: " + (string)Invoke(form, "DescribeQuickTileRowForAudit"));
		methodInfo2.Invoke(null, new object[1] { list });
		AppConfig.Flush();
		Invoke(form, "ApplyQuickTiles");
		Application.DoEvents();
		Log.AppendLine("  [picker-e2e] 已恢复原值 + 磁贴行: " + (string)Invoke(form, "DescribeQuickTileRowForAudit"));
	}

	/// <summary>限充三档与 EC 限充开关的启用态（只读读数，⛔ 不改任何状态）。
	/// 2026-10-12（bug ③）：额外读 <c>RButton.Activated</c> —— 它就是三档**当前高亮在哪一颗**，
	/// 用户说的"点下去有没有反应"看的就是它（⛔ 只读，不改）。</summary>
	private static void LogTierStates(Form form, string tag)
	{
		foreach (Control control in AllControls(form))
		{
			if (control is ButtonBase buttonBase && (buttonBase.Text == "长效模式" || buttonBase.Text == "平衡模式" || buttonBase.Text == "工作站模式"))
			{
				bool activated = buttonBase is RButton rbutton && rbutton.Activated;
				Log.AppendLine($"  [charge-limit] {tag} 档位 '{buttonBase.Text}' Enabled={buttonBase.Enabled} Activated={activated} Size={buttonBase.Size}");
			}
		}
		Control control2 = Find(form, "checkChargeLimit");
		if (control2 != null)
		{
			Log.AppendLine($"  [charge-limit] {tag} EC 限充开关 Enabled={control2.Enabled} Checked={(control2 as CheckBox)?.Checked}");
		}
	}

	/// <summary>
	/// 2026-10-12（本轮任务 ①②）：磁贴"最少 0 个" + "可自行调整顺序"的端到端取证，走**产品真代码** ——
	/// QuickTilePickerForm 真实构造器（可选项来自产品的 QuickSwitchGroups / QuickTileText / QuickTileSupported）
	/// → 真的按一下第 1 行的 ▼（<c>RButton.PerformClick</c>）→ 核对 <c>_keys</c> 顺序真的交换了 → 面板出图
	/// → 全部取消勾选 → <c>OnConfirm()</c>（「确定」按钮本体）→ <c>SaveQuickTileKeys</c>（应写哨兵 "-"）
	/// → 落盘文本核对 → <c>LoadQuickTileKeys</c> 读回必须**为空**（⛔ 不得回落到默认集）→ <c>ApplyQuickTiles</c>
	/// → 磁贴行只剩「自定义」并出图 → 最后还原原值再 Apply 一次。
	/// ⛔ 只写本工具自己的隔离配置（<c>LMECHREVO_CONFIG_FILE</c> → tmp-cfg-*.json）；⛔ 不点任何开关、不写 EC/GCU/注册表。
	/// </summary>
	private static void RunTilePickerEvidence(SettingsForm form)
	{
		try
		{
			// 先把宿主切到取证主题（`LMECHREVO_SHOT_THEME`）**再**建面板 —— 面板的颜色是在构造器里
			// 从 UiVisualStyle 取的，主题晚切就永远出不来浅色那张图（也顺带让"只剩自定义"的磁贴行同主题）。
			ApplyShotTheme(form);
			Type settingsType = _asm.GetType("MechrevoLite.SettingsForm");
			Type pickerType = _asm.GetType("MechrevoLite.QuickTilePickerForm");
			BindingFlags staticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
			BindingFlags instNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
			MethodInfo load = settingsType.GetMethod("LoadQuickTileKeys", staticNonPublic);
			MethodInfo save = settingsType.GetMethod("SaveQuickTileKeys", staticNonPublic);
			MethodInfo quickTileText = settingsType.GetMethod("QuickTileText", staticNonPublic);
			MethodInfo quickTileSupported = settingsType.GetMethod("QuickTileSupported", instNonPublic);
			FieldInfo groupsField = settingsType.GetField("QuickSwitchGroups", staticNonPublic);
			List<string> keys = new List<string>();
			List<string> texts = new List<string>();
			List<bool> supported = new List<bool>();
			foreach (object group in (Array)groupsField.GetValue(null))
			{
				string[] groupKeys = (string[])group.GetType().GetField("Item2").GetValue(group);
				if (groupKeys.Length == 0 || groupKeys[0].StartsWith("sleep", StringComparison.Ordinal))
				{
					continue;
				}
				foreach (string key in groupKeys)
				{
					if (keys.Contains(key))
					{
						continue;
					}
					keys.Add(key);
					texts.Add((string)quickTileText.Invoke(null, new object[1] { key }));
					supported.Add((bool)quickTileSupported.Invoke(form, new object[1] { key }));
				}
			}
			List<string> before = (List<string>)load.Invoke(null, null);
			Log.AppendLine($"  [tiles-0] 基线 perf_quick_tiles = {string.Join(",", before)}（可选项 {keys.Count} 个）");
			ConstructorInfo ctor = pickerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
			object picker = ctor.Invoke(new object[5] { keys, texts, supported, before, 5 });
			Form pickerForm = (Form)picker;
			// ⚠️ 必须先 Show()：`Button.PerformClick()` 只在 `CanSelect`（= 可见且在可见父链上、且 Enabled）时才真的触发
			// OnClick —— 面板没上屏时点 ▲/▼ 是**空操作**（上一版探针就踩了这个坑：交换成立=False）。非模态 Show，
			// 出图/点完立刻 Hide + Dispose，⛔ 不进模态消息循环。
			pickerForm.Show();
			Application.DoEvents();
			IList rows = (IList)pickerType.GetField("_rows", instNonPublic).GetValue(picker);
			IList pickerKeys = (IList)pickerType.GetField("_keys", instNonPublic).GetValue(picker);
			Log.AppendLine($"  [tiles-order] 行数={rows.Count} 初始顺序(前 4)={OrderHead(pickerKeys, 4)}");
			string row0 = (string)pickerKeys[0];
			string row1 = (string)pickerKeys[1];
			RButton down = null;
			bool upDisabled = false;
			foreach (Control child in ((Control)rows[0]).Controls)
			{
				if (child is RButton b && b.Name == "buttonTileDown")
				{
					down = b;
				}
				if (child is RButton b2 && b2.Name == "buttonTileUp")
				{
					upDisabled = !b2.Enabled;
				}
			}
			if (down != null)
			{
				down.PerformClick();
				Application.DoEvents();
			}
			string row0After = (string)pickerKeys[0];
			string row1After = (string)pickerKeys[1];
			Log.AppendLine($"  [tiles-order] 首行 ▲ 已置灰={upDisabled}；找到首行 ▼={down != null}；按下后 第1行 {row0}->{row0After}、第2行 {row1}->{row1After} ⇒ 交换成立={row0After == row1 && row1After == row0}；新顺序(前 4)={OrderHead(pickerKeys, 4)}");
			try
			{
				string pickerPng = Path.Combine(_outDir, "picker-reorder.png");
				using (Bitmap bitmap = new Bitmap(Math.Max(1, pickerForm.Width), Math.Max(1, pickerForm.Height), PixelFormat.Format32bppArgb))
				{
					pickerForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
					bitmap.Save(pickerPng, ImageFormat.Png);
				}
				pickerForm.Hide();
				Application.DoEvents();
				Log.AppendLine($"  [tiles-order] 面板出图 = {pickerPng}（{pickerForm.Width}x{pickerForm.Height}）");
			}
			catch (Exception ex)
			{
				Log.AppendLine("  [tiles-order] 面板出图失败: " + ex.Message);
			}
			IList boxes = (IList)pickerType.GetField("_boxes", instNonPublic).GetValue(picker);
			// 顺序持久化：把"面板当前视觉顺序"的前两项按这个顺序写进同一个键，读回必须逐项同序
			// （用户要求「顺序同样持久化进同一个键」；键格式 = 逗号分隔 key 列表，**顺序即磁贴顺序**）。
			List<string> orderProbe = new List<string> { (string)pickerKeys[0], (string)pickerKeys[2] };
			save.Invoke(null, new object[1] { orderProbe });
			AppConfig.Flush();
			List<string> orderRead = (List<string>)load.Invoke(null, null);
			Log.AppendLine($"  [tiles-order] 顺序持久化：写 [{string.Join(",", orderProbe)}] ⇒ 读回 [{string.Join(",", orderRead)}] 顺序一致={string.Join(",", orderProbe) == string.Join(",", orderRead)}");
			foreach (object box in boxes)
			{
				((CheckBox)box).Checked = false;
			}
			pickerType.GetMethod("OnConfirm", instNonPublic).Invoke(picker, null);
			List<string> selected = (List<string>)pickerType.GetProperty("SelectedKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(picker);
			Log.AppendLine($"  [tiles-0] 全部取消勾选 + 「确定」：DialogResult={pickerForm.DialogResult}（期望 OK）SelectedKeys={selected.Count} 个（期望 0）");
			pickerForm.Dispose();
			save.Invoke(null, new object[1] { selected });
			AppConfig.Flush();
			string configPath = Environment.GetEnvironmentVariable("LMECHREVO_CONFIG_FILE");
			string configText = (File.Exists(configPath) ? File.ReadAllText(configPath) : "");
			int at = configText.IndexOf("perf_quick_tiles", StringComparison.Ordinal);
			string snippet = ((at >= 0) ? configText.Substring(at, Math.Min(48, configText.Length - at)) : "(未落盘)");
			snippet = snippet.Replace("\r", " ").Replace("\n", " ");
			List<string> reloaded = (List<string>)load.Invoke(null, null);
			Log.AppendLine($"  [tiles-0] 落盘片段 = {snippet}");
			Log.AppendLine($"  [tiles-0] 读回 {reloaded.Count} 个（期望 0 = 显式空集；⛔ 不得回落到默认集）");
			Invoke(form, "ApplyQuickTiles");
			Application.DoEvents();
			Log.AppendLine("  [tiles-0] 只剩「自定义」的磁贴行 = " + (string)Invoke(form, "DescribeQuickTileRowForAudit"));
			try
			{
				Control tileRow = Find(form, "perfQuickTiles");
				if (tileRow != null && tileRow.Width > 0 && tileRow.Height > 0)
				{
					string rowPng = Path.Combine(_outDir, "tiles-zero-row.png");
					using (Bitmap bitmap2 = new Bitmap(tileRow.Width, tileRow.Height, PixelFormat.Format32bppArgb))
					{
						tileRow.DrawToBitmap(bitmap2, new Rectangle(Point.Empty, bitmap2.Size));
						bitmap2.Save(rowPng, ImageFormat.Png);
					}
					Log.AppendLine($"  [tiles-0] 磁贴行出图 = {rowPng}（row={tileRow.Width}x{tileRow.Height} 子控件={tileRow.Controls.Count}）");
				}
			}
			catch (Exception ex2)
			{
				Log.AppendLine("  [tiles-0] 磁贴行出图失败: " + ex2.Message);
			}
			save.Invoke(null, new object[1] { before });
			AppConfig.Flush();
			Invoke(form, "ApplyQuickTiles");
			Application.DoEvents();
			Log.AppendLine("  [tiles-0] 已还原 perf_quick_tiles = " + string.Join(",", (List<string>)load.Invoke(null, null)) + "；磁贴行 = " + (string)Invoke(form, "DescribeQuickTileRowForAudit"));
		}
		catch (Exception ex3)
		{
			Log.AppendLine("  [tiles-0] 失败: " + ex3);
		}
	}

	/// <summary>
	/// 2026-10-12（用户原话「关闭的开关不能移动到开启的开关的上面」+「还有取消和确定字体粗细不一致」）：
	/// **只读取证**，全部走产品真代码 —— 真实 QuickTilePickerForm 构造器 → 真实 RButton.PerformClick
	/// （面板先 Show()，否则 PerformClick 在不可选项上是空操作）→ 真实 SettingsForm.Load/SaveQuickTileKeys。
	/// 取证三件事：
	/// ① 每一行 ▲/▼ 的 Enabled 是否**只**按"首行 ▲ / 末行 ▼"置灰（与勾选状态无关）；
	/// ② 把一个**未勾选**的行上移到**已勾选**行上面能否成立（逐次打印面板视觉顺序）；
	/// ③ 保存语义：「确定」后的 SelectedKeys = 面板顺序里的已勾选项（未勾选行的位置只改变绝对位置、
	///    不改变已勾选项之间的相对顺序）；用这批键重开面板后顺序 = 已存顺序在前 + 其余按可选项顺序。
	/// 另附：底部「取消/确定」两颗按钮的字体族/字号/字重（本轮第 2 件事，深/浅主题与各 DPI 都会各打一行）。
	/// ⛔ 只写本工具自己的隔离配置（LMECHREVO_CONFIG_FILE → tmp-cfg-*.json）；⛔ 不碰用户配置/硬件。
	/// 夹具：LMECHREVO_SHOT_TILEORDER_SEL（逗号分隔的"已勾选"key，缺省 = 用户截图上的 webcam,touchpad,micmute,fanboost）。
	/// </summary>
	private static void RunTileOrderProbe(SettingsForm form, string viewportId)
	{
		try
		{
			ApplyShotTheme(form);
			Type settingsType = _asm.GetType("MechrevoLite.SettingsForm");
			Type pickerType = _asm.GetType("MechrevoLite.QuickTilePickerForm");
			BindingFlags staticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
			BindingFlags instNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
			MethodInfo quickTileText = settingsType.GetMethod("QuickTileText", staticNonPublic);
			MethodInfo quickTileSupported = settingsType.GetMethod("QuickTileSupported", instNonPublic);
			FieldInfo groupsField = settingsType.GetField("QuickSwitchGroups", staticNonPublic);
			List<string> keys = new List<string>();
			List<string> texts = new List<string>();
			List<bool> supported = new List<bool>();
			foreach (object group in (Array)groupsField.GetValue(null))
			{
				string[] groupKeys = (string[])group.GetType().GetField("Item2").GetValue(group);
				if (groupKeys.Length == 0 || groupKeys[0].StartsWith("sleep", StringComparison.Ordinal))
				{
					continue;
				}
				foreach (string key in groupKeys)
				{
					if (keys.Contains(key))
					{
						continue;
					}
					keys.Add(key);
					texts.Add((string)quickTileText.Invoke(null, new object[1] { key }));
					supported.Add((bool)quickTileSupported.Invoke(form, new object[1] { key }));
				}
			}
			Log.AppendLine($"  [tiles-order2] 产品可选项 {keys.Count} 个（顺序即「其余」的显示顺序）：{string.Join(",", keys)}");
			// ① 父任务给的用例（等价于"摄像头(on) + 触摸板(on) + WiFi(off)，把 WiFi 上移两次"）：
			//    用最小合成列表，避免真实可选项顺序把 WiFi 的起点冲淡。
			RunTileOrderCase(pickerType, new List<string> { "webcam", "touchpad", "wifi" },
				new List<string> { "摄像头", "触摸板", "WiFi" }, new List<bool> { true, true, true },
				new List<string> { "webcam", "touchpad" }, "wifi", 2, null, "用例①合成列表");
			// ② 用户截图上的真实配置：把 WiFi（未勾选）上移 3 格 —— 停在已勾选行之间（上面就是已勾选行），出图。
			string selText = Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TILEORDER_SEL");
			if (string.IsNullOrEmpty(selText))
			{
				selText = "webcam,touchpad,micmute,fanboost";
			}
			List<string> selected = new List<string>();
			foreach (string piece in selText.Split(','))
			{
				if (!string.IsNullOrEmpty(piece))
				{
					selected.Add(piece.Trim());
				}
			}
			RunTileOrderCase(pickerType, keys, texts, supported, selected, "wifi", 3,
				Path.Combine(_outDir, "picker-off-above-on-" + _suffix + "-" + viewportId + ".png"), "用例②用户配置");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  [tiles-order2] 失败: " + ex);
		}
	}

	/// <summary>一个排序用例：建面板 → 打印每行 ▲/▼ 可用性（应只与行位置有关）→ 把 <paramref name="offKey" />
	/// 用真实 ▲ 点击上移 <paramref name="moves" /> 格（每步打印视觉顺序）→ 打印底部两颗按钮的字体 →
	/// 「确定」→ 打印 SelectedKeys → 用这批键重开面板并打印其顺序。<paramref name="png" /> 非空时出整面板图。</summary>
	private static void RunTileOrderCase(Type pickerType, List<string> keys, List<string> texts, List<bool> supported, List<string> selected, string offKey, int moves, string png, string tag)
	{
		BindingFlags instNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
		ConstructorInfo ctor = pickerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
		object picker = ctor.Invoke(new object[5] { keys, texts, supported, selected, 5 });
		Form pickerForm = (Form)picker;
		pickerForm.Show();
		Application.DoEvents();
		IList rows = (IList)pickerType.GetField("_rows", instNonPublic).GetValue(picker);
		IList pickerKeys = (IList)pickerType.GetField("_keys", instNonPublic).GetValue(picker);
		IList boxes = (IList)pickerType.GetField("_boxes", instNonPublic).GetValue(picker);
		TableLayoutPanel list = (TableLayoutPanel)pickerType.GetField("_list", instNonPublic).GetValue(picker);
		Log.AppendLine($"  [tiles-order2] ── {tag}：已勾选=[{string.Join(",", selected)}] 未勾选行 {offKey} 上移 {moves} 格");
		Log.AppendLine($"  [tiles-order2] 初始顺序={VisualOrder(list, rows.Count)} 勾选={string.Join(",", CheckedKeys(boxes, pickerKeys))}");
		string grayed = "";
		bool boundaryOk = true;
		for (int i = 0; i < rows.Count; i++)
		{
			bool upEnabled = false;
			bool downEnabled = false;
			foreach (Control child in ((Control)rows[i]).Controls)
			{
				if (child is RButton b && b.Name == "buttonTileUp")
				{
					upEnabled = b.Enabled;
				}
				if (child is RButton b2 && b2.Name == "buttonTileDown")
				{
					downEnabled = b2.Enabled;
				}
			}
			if (upEnabled != (i > 0) || downEnabled != (i < rows.Count - 1))
			{
				boundaryOk = false;
			}
			if (!upEnabled || !downEnabled)
			{
				grayed += $"{(upEnabled ? "" : "▲")}{(downEnabled ? "" : "▼")}@第{i + 1}行({(string)pickerKeys[i]}) ";
			}
		}
		Log.AppendLine($"  [tiles-order2] 行数={rows.Count} 置灰处={(grayed.Length == 0 ? "(无)" : grayed)}只按「首行▲/末行▼」置灰={boundaryOk}");
		int steps = 0;
		for (int s = 0; s < moves; s++)
		{
			int cur = -1;
			for (int i = 0; i < pickerKeys.Count; i++)
			{
				if (string.Equals((string)pickerKeys[i], offKey, StringComparison.OrdinalIgnoreCase))
				{
					cur = i;
					break;
				}
			}
			if (cur <= 0)
			{
				break;
			}
			RButton up = null;
			foreach (Control child in ((Control)rows[cur]).Controls)
			{
				if (child is RButton b && b.Name == "buttonTileUp")
				{
					up = b;
				}
			}
			if (up == null || !up.Enabled)
			{
				Log.AppendLine($"  [tiles-order2] 第{steps + 1}次上移被拒：{offKey} 已在第{cur + 1}行，▲ Enabled={(up != null && up.Enabled)}");
				break;
			}
			up.PerformClick();
			Application.DoEvents();
			steps++;
			IList rowsNow = (IList)pickerType.GetField("_rows", instNonPublic).GetValue(picker);
			IList keysNow = (IList)pickerType.GetField("_keys", instNonPublic).GetValue(picker);
			Log.AppendLine($"  [tiles-order2] 第{steps}次 ▲ 后：顺序={VisualOrder(list, rowsNow.Count)}（键={string.Join(",", (IEnumerable)keysNow)}）");
		}
		Log.AppendLine($"  [tiles-order2] 实际点上移 {steps} 次；最终顺序={VisualOrder(list, rows.Count)}");
		Control okButton = Find(pickerForm, "buttonQuickTileOk");
		Control cancelButton = Find(pickerForm, "buttonQuickTileCancel");
		if (okButton != null && cancelButton != null)
		{
			Log.AppendLine($"  [tiles-font] 取消={DescribeFont(cancelButton.Font)} 确定={DescribeFont(okButton.Font)} 同族同号同字重={(okButton.Font.Name == cancelButton.Font.Name && okButton.Font.SizeInPoints == cancelButton.Font.SizeInPoints && okButton.Font.Bold == cancelButton.Font.Bold)}");
		}
		if (png != null)
		{
			try
			{
				using (Bitmap bitmap = new Bitmap(Math.Max(1, pickerForm.Width), Math.Max(1, pickerForm.Height), PixelFormat.Format32bppArgb))
				{
					pickerForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
					bitmap.Save(png, ImageFormat.Png);
				}
				Log.AppendLine($"  [tiles-order2] 面板出图 = {png}（{pickerForm.Width}x{pickerForm.Height}）");
			}
			catch (Exception ex)
			{
				Log.AppendLine("  [tiles-order2] 出图失败: " + ex.Message);
			}
		}
		List<string> checkedNow = CheckedKeys(boxes, pickerKeys);
		pickerType.GetMethod("OnConfirm", instNonPublic).Invoke(picker, null);
		List<string> saved = (List<string>)pickerType.GetProperty("SelectedKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(picker);
		Log.AppendLine($"  [tiles-order2] 「确定」后 SelectedKeys（= 将被保存的顺序）= [{string.Join(",", saved)}]；与面板里已勾选项同序={string.Join(",", saved) == string.Join(",", checkedNow)}；未勾选的 {offKey} 不在其中={!saved.Contains(offKey)}");
		pickerForm.Dispose();
		object picker2 = ctor.Invoke(new object[5] { keys, texts, supported, saved, 5 });
		Form pickerForm2 = (Form)picker2;
		pickerForm2.Show();
		Application.DoEvents();
		IList rows2 = (IList)pickerType.GetField("_rows", instNonPublic).GetValue(picker2);
		TableLayoutPanel list2 = (TableLayoutPanel)pickerType.GetField("_list", instNonPublic).GetValue(picker2);
		Log.AppendLine($"  [tiles-order2] 用这批键重开面板：顺序={VisualOrder(list2, rows2.Count)}（未勾选行回到可选项顺序里它该在的位置）");
		pickerForm2.Dispose();
	}

	/// <summary>表格里第 0 列自上而下的行名（= 面板的真实视觉顺序，⛔ 不读 _keys，避免"表与表不同步"看不出来）。</summary>
	private static string VisualOrder(TableLayoutPanel list, int count)
	{
		List<string> names = new List<string>();
		for (int i = 0; i < count; i++)
		{
			Control control = list.GetControlFromPosition(0, i);
			names.Add((control == null) ? "?" : control.Name.Replace("rowTile_", ""));
		}
		return string.Join(",", names);
	}

	/// <summary>当前被勾上的 key（按面板顺序）。</summary>
	private static List<string> CheckedKeys(IList boxes, IList keys)
	{
		List<string> result = new List<string>();
		for (int i = 0; i < boxes.Count; i++)
		{
			if (((CheckBox)boxes[i]).Checked)
			{
				result.Add((string)keys[i]);
			}
		}
		return result;
	}

	/// <summary>字体的一行摘要（族/号/字重），用来核对"取消/确定同字重"。</summary>
	private static string DescribeFont(Font font)
	{
		return $"{font.Name}/{font.SizeInPoints:0.##}pt/{(font.Bold ? "Bold" : "Regular")}";
	}


	/// <summary>
	/// 2026-10-12（bug ③）：用**只读夹具**把"EC 回读到 40–99 的非档位残留值"这一态摆出来，核对三档高亮。
	/// 做法：反射设 <c>EcChargeLimit.ReadPercentOverride = () =&gt; 73</c>（只替换本进程内的读函数，
	/// ⛔ 不开 EC 设备、⛔ 不发任何命令）→ <c>RefreshBatteryModeButtons()</c> → 读三颗的 Activated → 复位 override 再读一次。
	/// 期望（修后）：73 不是任何档位，但两个开关都关（EC 直写未生效）⇒ 高亮兜底到记忆档位（默认 80 = 平衡模式），
	/// ⛔ 不再是"三颗一个都不亮"。
	/// </summary>
	private static void RunTierHighlightProbe(Form form)
	{
		try
		{
			Type ecType = _asm.GetType("MechrevoLite.Hardware.EcChargeLimit");
			PropertyInfo prop = ecType.GetProperty("ReadPercentOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			prop.SetValue(null, (Func<int>)(() => 73));
			Invoke(form, "RefreshBatteryModeButtons");
			Application.DoEvents();
			// A/B 的另一半：把高亮**判据的输入**也打出来 —— 73 ∉ {100,80,60} ⇒ 改前 currentMode 恒为 -1（三颗全不亮），
			// 修后落到记忆档位（默认 80 = 平衡模式）。⛔ 只读调用，不写任何配置/硬件。
			Log.AppendLine($"  [tier-hilite] SelectedModePercent()={ReadStaticInt("MechrevoLite.Battery.BatteryControl", "SelectedModePercent")}（=EC 回读 73）· StoredModePercent()={ReadStaticInt("MechrevoLite.Battery.BatteryControl", "StoredModePercent")} · EC 直写方式生效={ReadStaticBool("MechrevoLite.Battery.BatteryControl", "LockEnabled")}");
			Log.AppendLine("  [tier-hilite] EC 回读夹具=73（40–99 的非档位残留；两个实验开关都关 ⇒ EC 直写方式未生效）");
			LogTierStates(form, "EC回读=73");
			prop.SetValue(null, null);
			Invoke(form, "RefreshBatteryModeButtons");
			Application.DoEvents();
			LogTierStates(form, "override 已复位");
		}
		catch (Exception ex)
		{
			Log.AppendLine("  [tier-hilite] 失败: " + ex);
		}
	}

	/// <summary>只读：调一个 product 静态无参方法并取回 int（取不到返回 <see cref="int.MinValue" />）。</summary>
	private static int ReadStaticInt(string typeName, string method)
	{
		try
		{
			MethodInfo methodInfo = _asm.GetType(typeName)?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return ((methodInfo != null) ? ((int)methodInfo.Invoke(null, null)) : int.MinValue);
		}
		catch (Exception)
		{
			return int.MinValue;
		}
	}

	/// <summary>只读：调一个 product 静态无参方法并取回 bool。</summary>
	private static bool ReadStaticBool(string typeName, string method)
	{
		try
		{
			MethodInfo methodInfo = _asm.GetType(typeName)?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return methodInfo != null && (bool)methodInfo.Invoke(null, null);
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>只读：把列表前 <paramref name="count" /> 项拼成一行（取证日志用）。</summary>
	private static string OrderHead(IList list, int count)
	{
		List<string> head = new List<string>();
		for (int i = 0; i < list.Count && i < count; i++)
		{
			head.Add((list[i] != null) ? list[i].ToString() : "");
		}
		return string.Join(",", head);
	}

	/// <summary>
	/// 2026-10-12（bug ② A/B 取证 · ⛔ 全内存 + 本工具自己的隔离配置）：
	/// 人为把"EC 直写通道判为不可用"这一态置位（= 模拟机型判据降级 / ec_charge_limit=0），
	/// 并把 **EC 限充开关置为开**（写的是 LMECHREVO_CONFIG_FILE 指向的 tmp-cfg-*.json，⛔ 绝不碰用户真实配置）
	/// ⇒ 按用户答复「任一条通路可用三档就可点」，三档应当 Enabled=True。
	/// 改前那份代码里三档与 EC 开关共用 `_chargeLimitStageBaseEnabled && !_chargeSliderEnabled` ⇒ 这里会是 False。
	/// ⛔ 不发任何真实报文（GCU 侧只喂夹具 hw 的内部消息）、⛔ 不写 EC / 注册表。
	/// </summary>
	private static void RunChargeGateProbe(Form form)
	{
		SetInstanceField(form, "_chargeLimitStageBaseEnabled", false);
		SetInstanceField(form, "_chargeSliderEnabled", false);
		string before = AppConfig.GetString("charge_limit_lock");
		AppConfig.Set("charge_limit_lock", 1);
		AppConfig.Flush();
		Invoke(form, "ApplyChargeLimitStageEnablement");
		Application.DoEvents();
		LogTierStates(form, "EC通道判不可用 + EC限充开关=开");
		object hwObj = GetStatic("MechrevoLite.Program", "hw");
		if (hwObj is MechrevoHw mechrevoHw)
		{
			SendHw(mechrevoHw, "System/BatteryProtection", "{\"HealthProtectionStatus\":2}");
			Log.AppendLine($"  [charge-limit] 夹具 GCU 侧（仅信息）：IsConnected={mechrevoHw.IsConnected} BatteryProtection={mechrevoHw.BatteryProtection}");
		}
		AppConfig.Set("charge_limit_lock", before ?? "");
		AppConfig.Flush();
		Invoke(form, "ApplyChargeLimitStageEnablement");
		Application.DoEvents();
		LogTierStates(form, "已复原");
	}

	// ==========================================================================================
	// 本任务特有的额外观察量（只报告，不改产品判据）
	// ==========================================================================================
	private static void ExtraChecks(Fixture fixture, Form form, string viewportId)
	{
		int num = 0;
		// 2026-10-12（本轮任务 · 用户原话「快捷开关最少 0 个（全部不选就只显示自定义）」「还有希望可以自行
		// 调整顺序」）：`LMECHREVO_SHOT_TILEPICKER=1` 时走**真实**「＋」面板 + 真实 Load/Save/Apply 路径取证
		// （排序按钮真的按下去、0 选真的走 OnConfirm + 哨兵落盘 + 读回），并把面板与"只剩「自定义」"的磁贴行
		// 各出一张图。⛔ 只写本工具自己的隔离配置（LMECHREVO_CONFIG_FILE → tmp-cfg-*.json），⛔ 不碰用户配置/硬件。
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TILEPICKER"), "1", StringComparison.OrdinalIgnoreCase) && form is SettingsForm settingsForPicker)
		{
			RunTilePickerEvidence(settingsForPicker);
		}
		// 2026-10-12（用户原话「我发现第三个按钮有概率出现点不动的情况 只有开关第二个按钮才能再次可点击」）：
		// `LMECHREVO_SHOT_TILEGATE=1` 时跑一段**只读**时序复现 + A/B（见 RunTileGateProbe）。
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TILEGATE"), "1", StringComparison.OrdinalIgnoreCase) && form is SettingsForm settingsForGate)
		{
			RunTileGateProbe(settingsForGate, fixture.Name, viewportId);
		}
		// 2026-10-12（用户原话「关闭的开关不能移动到开启的开关的上面」）：`LMECHREVO_SHOT_TILEORDER=1` 时
		// 只读取证"每行 ▲/▼ 可用性 + 未勾选行上移到已勾选行上面 + 保存/重开语义"（见 RunTileOrderProbe）。
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TILEORDER"), "1", StringComparison.OrdinalIgnoreCase) && form is SettingsForm settingsForOrder)
		{
			RunTileOrderProbe(settingsForOrder, viewportId);
		}
		// 2026-10-11（快捷磁贴「＋」面板取证）：本面板是**模态**窗口（ShowDialog 会阻塞宿主），
		// 所以这里只做"能不能构造出来"的构造期验证（走真实构造器 → 真实控件树 → 真实布局代码），
		// 构造完立刻 Dispose，⛔ 不显示、⛔ 不写任何配置。`LMECHREVO_SHOT_PICKER=1` 时才跑。
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_PICKER"), "1", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				Type pickerType = _asm.GetType("MechrevoLite.QuickTilePickerForm");
				ConstructorInfo ctor = pickerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
				object instance = ctor.Invoke(new object[5]
				{
					new List<string> { "fanboost", "webcam", "micmute", "not-a-switch" },
					new List<string> { "风扇强冷", "摄像头", "麦克风", "不存在的开关" },
					new List<bool> { true, true, false, false },
					new List<string> { "fanboost" },
					5
				});
				Form picker = (Form)instance;
				int boxes = 0;
				int disabled = 0;
				foreach (Control c in AllControls(picker))
				{
					if (c is CheckBox cb)
					{
						boxes++;
						if (!cb.Enabled)
						{
							disabled++;
						}
					}
				}
				Log.AppendLine($"  [picker] 构造成功：ClientSize={picker.ClientSize} 顶层控件={picker.Controls.Count} 复选={boxes} 置灰={disabled}");
				picker.Dispose();
			}
			catch (Exception ex)
			{
				Log.AppendLine("  [picker] 构造失败: " + ex);
			}
			// 2026-10-12（本轮遗留项④）：再加一段**端到端**只读取证（见 RunPickerRoundTrip 的说明）。
			try
			{
				RunPickerRoundTrip(form);
			}
			catch (Exception ex2)
			{
				Log.AppendLine("  [picker-e2e] 失败: " + ex2);
			}
		}
		// 2026-10-12（bug ① 亮度滑块量程 · 只读取数）：直接读控件的量程/当前值。
		RSlider brightSlider2 = Find(form, "sliderScreenBrightness") as RSlider;
		if (brightSlider2 != null)
		{
			Log.AppendLine($"  [brightness] slider Minimum={brightSlider2.Minimum} Maximum={brightSlider2.Maximum} Value={brightSlider2.Value} Enabled={brightSlider2.Enabled} size={brightSlider2.Size} type={brightSlider2.GetType().Name}");
			if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_BRIGHT"), "0", StringComparison.OrdinalIgnoreCase) && brightSlider2.Minimum <= 0)
			{
				// ⚠️ 只读：UiAuditMode 下 ValueChanged 不写 WMI（SettingsForm 里有 !Program.UiAuditMode 守卫）
				brightSlider2.Value = 0;
				Application.DoEvents();
				Log.AppendLine($"  [brightness] 已把滑块拉到量程下限取证：Value={brightSlider2.Value}（Minimum={brightSlider2.Minimum}）");
			}
		}
		// 2026-10-12（bug ② 三档启用态）：先记基线，再按需跑 A/B 探针。
		LogTierStates(form, "基线");
		// 2026-10-12（bug ③「三档点下去没反应」）：EC 回读到 40–99 的**非档位**残留值时，改前 `currentMode`
		// 恒为 -1 ⇒ 三颗一个都不高亮 ⇒ 用户主观"点不动"。`LMECHREVO_SHOT_TIERHILITE=1` 时用只读的
		// `EcChargeLimit.ReadPercentOverride` 夹具复现这一态并核对高亮（见 RunTierHighlightProbe）。
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_TIERHILITE"), "1", StringComparison.OrdinalIgnoreCase))
		{
			RunTierHighlightProbe(form);
		}
		if (string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_SHOT_CHARGEGATE"), "1", StringComparison.OrdinalIgnoreCase))
		{
			RunChargeGateProbe(form);
		}
		// 1) 标签真实裁切：按控件实际宽度做 WordBreak 折行量高，量出的高度超过控件高度才算被裁
		//    （产品判据对 AutoSize 标签直接跳过；AutoSize=false 的中文标签产品已用单行宽度判过）。
		foreach (Control item in AllControls(form))
		{
			if (item is Label label && item.Visible && !string.IsNullOrWhiteSpace(label.Text))
			{
				// 读数片（RTelemetryLabel）自绘单行、且文本矩形 = 客户区 ⇒ 按"绘制旗标"量，⛔ 不用
				// WordBreak（那是基类 Label 的折行口径，会把"永远单行"的读数误报成竖排被裁）。
				bool telemetry = label.GetType().Name == "RTelemetryLabel";
				TextFormatFlags flags = (telemetry ? (TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding) : (TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix));
				Size size = TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, label.Width), int.MaxValue), flags);
				if (size.Height > label.Height + 2)
				{
					string detail = $"Label text '{Oneline(label.Text)}' wrapped needs {size.Height}px tall in box {label.Width}x{label.Height} (AutoSize={label.AutoSize}, AutoEllipsis={label.AutoEllipsis}) -> 竖排被裁.";
					Rows.Add(new string[5] { fixture.Name, viewportId, "label-text-height", ControlPath(label), detail });
					Log.AppendLine($"  [label-text-height] {ControlPath(label)}: {detail}");
					num++;
				}
			}
			// 2) 每核输入框（NumericUpDown）文本宽 vs 可用宽（产品判据不查 NumericUpDown）
			if (item is NumericUpDown numericUpDown && item.Visible)
			{
				int num2 = Math.Max(1, numericUpDown.ClientSize.Width - 20);
				Size size2 = TextRenderer.MeasureText(numericUpDown.Text, numericUpDown.Font);
				if (size2.Width > num2 + 2)
				{
					string detail2 = $"Numeric '{numericUpDown.Text}' needs {size2.Width}px, available {num2}px (box {numericUpDown.Size}).";
					Rows.Add(new string[5] { fixture.Name, viewportId, "numeric-text-width", ControlPath(numericUpDown), detail2 });
					Log.AppendLine($"  [numeric-text-width] {ControlPath(numericUpDown)}: {detail2}");
					num++;
				}
			}
		}
		// 3) 每核输入网格：整块所需宽 vs 宿主可用宽（8 框排布是否被压/被裁）
		Control grid = Find(form, "cpuTunePerCoreGrid");
		if (grid is TableLayoutPanel table)
		{
			int[] columnWidths = table.GetColumnWidths();
			int num3 = columnWidths.Sum();
			int num4 = ((grid.Parent != null) ? grid.Parent.ClientSize.Width : num3);
			int num5 = AllControls(grid).Count((Control c) => c is NumericUpDown && c.Visible);
			string detail3 = $"perCoreGrid size={table.Size} cols={table.ColumnCount} rows={table.RowCount} inputs={num5} colWidths=[{string.Join(",", columnWidths)}] sum={num3} hostClient={num4} fit={(num3 <= num4 + 1 ? "PASS" : "FAIL 被裁")}";
			Log.AppendLine("  [percore-grid] " + detail3);
			Rows.Add(new string[5] { fixture.Name, viewportId, "percore-grid", "cpuTunePerCoreGrid", detail3 });
			if (num5 > 0 && num5 < 8)
			{
				Rows.Add(new string[5] { fixture.Name, viewportId, "percore-count", "cpuTunePerCoreGrid", $"visible per-core inputs={num5} (expected 8)" });
				Log.AppendLine($"  [percore-count] visible per-core inputs={num5} (expected 8)");
			}
			if (grid.Bounds.Right > num4 + 1)
			{
				string detail4 = $"perCoreGrid right={grid.Bounds.Right} beyond host client {num4}.";
				Rows.Add(new string[5] { fixture.Name, viewportId, "percore-overflow", ControlPath(grid), detail4 });
				Log.AppendLine("  [percore-overflow] " + detail4);
			}
		}
		// 5) 页脚版本号：栏宽 / 字体 / 文本实测（版本号被 … 截断时用来定位是宽度还是高度不够）
		Control version = Find(form, "labelVersion");
		if (version is Label versionLabel && !string.IsNullOrWhiteSpace(version.Text))
		{
			Size size4 = TextRenderer.MeasureText(version.Text, version.Font);
			string detail6 = $"labelVersion box={version.Size} pad={version.Padding} font={version.Font.SizeInPoints}pt(h={version.Font.Height}) text='{version.Text}' single-line-needs={size4.Width}x{size4.Height} AutoSize={version.AutoSize} AutoEllipsis={versionLabel.AutoEllipsis} Dock={version.Dock} parent={version.Parent?.Name}({version.Parent?.ClientSize})";
			Log.AppendLine("  [version-label] " + detail6);
			Rows.Add(new string[5] { fixture.Name, viewportId, ((size4.Width <= version.Width + 2) ? "version-label-pass" : "version-label-truncated"), ControlPath(version), detail6 });
			// 页脚几何链（定位「版本号为什么只够一行」到底被谁挤的）
			StringBuilder stringBuilder = new StringBuilder("  [version-chain]");
			for (Control control = version; control != null; control = control.Parent)
			{
				stringBuilder.Append($" <- {control.GetType().Name}#{control.Name}{control.Size}dock={control.Dock}pad={control.Padding}min={control.MinimumSize}");
				if (control is TableLayoutPanel tableLayoutPanel)
				{
					stringBuilder.Append($" rows=[{string.Join(",", tableLayoutPanel.GetRowHeights())}] cols=[{string.Join(",", tableLayoutPanel.GetColumnWidths())}]");
				}
			}
			Log.AppendLine(stringBuilder.ToString());
			Rows.Add(new string[5] { fixture.Name, viewportId, "version-chain", ControlPath(version), stringBuilder.ToString() });
		}
		// 4) D-1 提示行（我们新加的「本机可能不支持，可试一次」）
		Control hint = Find(form, "labelGPUFan");
		if (hint != null && !string.IsNullOrWhiteSpace(hint.Text))
		{
			Size size3 = TextRenderer.MeasureText(hint.Text, hint.Font);
			string detail5 = $"hint '{Oneline(hint.Text)}' needs {size3.Width}px, box {hint.Width}px (font {hint.Font.SizeInPoints}pt, AutoSize={hint.AutoSize}) -> {(size3.Width <= hint.Width + 2 ? "PASS 放得下" : "FAIL 会被裁")}";
			Log.AppendLine("  [d1-hint] " + detail5);
			Rows.Add(new string[5] { fixture.Name, viewportId, ((size3.Width <= hint.Width + 2) ? "d1-hint-pass" : "d1-hint-fail"), ControlPath(hint), detail5 });
		}
		Log.AppendLine($"  extra-observations={num}");
	}

	// ==========================================================================================
	// 输出
	// ==========================================================================================
	private static void WriteSummary()
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		Dictionary<string, int> dictionary2 = new Dictionary<string, int>();
		foreach (string[] row in Rows)
		{
			string key = row[2];
			dictionary[key] = (dictionary.TryGetValue(key, out var value) ? value : 0) + 1;
			string key2 = row[0] + " | " + row[2];
			dictionary2[key2] = (dictionary2.TryGetValue(key2, out var value2) ? value2 : 0) + 1;
		}
		Log.AppendLine();
		Log.AppendLine("=== 按 kind 汇总 ===");
		foreach (KeyValuePair<string, int> item in dictionary.OrderByDescending((KeyValuePair<string, int> kv) => kv.Value))
		{
			Log.AppendLine($"  {item.Key} = {item.Value}");
		}
		Log.AppendLine();
		Log.AppendLine("=== 按 (夹具 | kind) 汇总 ===");
		foreach (KeyValuePair<string, int> item2 in dictionary2.OrderBy((KeyValuePair<string, int> kv) => kv.Key, StringComparer.Ordinal))
		{
			Log.AppendLine($"  {item2.Key} = {item2.Value}");
		}
	}

	private static void Flush()
	{
		try
		{
			File.WriteAllText(Path.Combine(_outDir, "egroupshot-" + _suffix + ".txt"), Log.ToString(), new UTF8Encoding(true));
			// 2026-10-12（本轮定位 Fatal error 用）：崩溃（AV）时 finally 里那句 Environment.Exit 之前
			// 控制流**不一定**还能走到 Flush ⇒ 另存一份"增量日志"，每行都落盘，保证崩溃现场可见。
			File.WriteAllText(Path.Combine(_outDir, "egroupshot-" + _suffix + ".live.txt"), Log.ToString(), new UTF8Encoding(true));
			StringBuilder stringBuilder = new StringBuilder("fixture,viewport,kind,control,detail\r\n");
			foreach (string[] row in Rows)
			{
				for (int i = 0; i < row.Length; i++)
				{
					if (i > 0)
					{
						stringBuilder.Append(',');
					}
					stringBuilder.Append('"').Append(row[i].Replace("\"", "\"\"")).Append('"');
				}
				stringBuilder.Append("\r\n");
			}
			File.WriteAllText(Path.Combine(_outDir, "egroupshot-" + _suffix + ".csv"), stringBuilder.ToString(), new UTF8Encoding(true));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("flush failed: " + ex.Message);
		}
	}

	// ==========================================================================================
	// 反射 / 控件树小工具
	// ==========================================================================================
	private static Control Find(Control root, string name)
	{
		if (root == null)
		{
			return null;
		}
		if (root.Name == name)
		{
			return root;
		}
		foreach (Control control in root.Controls)
		{
			Control control2 = Find(control, name);
			if (control2 != null)
			{
				return control2;
			}
		}
		return null;
	}

	private static IEnumerable<Control> AllControls(Control root)
	{
		foreach (Control control in root.Controls)
		{
			yield return control;
			foreach (Control item in AllControls(control))
			{
				yield return item;
			}
		}
	}

	private static string ControlPath(Control control)
	{
		if (control == null)
		{
			return "MISSING";
		}
		StringBuilder stringBuilder = new StringBuilder(control.Name);
		for (Control control2 = control.Parent; control2 != null; control2 = control2.Parent)
		{
			stringBuilder.Insert(0, control2.Name + "/");
		}
		return stringBuilder.ToString().TrimStart('/');
	}

	private static string Oneline(string text)
	{
		return (text ?? "").Replace("\r", " ").Replace("\n", " ");
	}

	private static int ReadHostDpi()
	{
		using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
		return (int)graphics.DpiX;
	}

	private static int ReadHostDpi(Control control)
	{
		return control.DeviceDpi;
	}

	private static void ScaleFonts(Control root, float factor)
	{
		if (!(Math.Abs(factor - 1f) > 0.01f))
		{
			return;
		}
		foreach (Control control in AllControls(root))
		{
			if (control.Font != null)
			{
				control.Font = new Font(control.Font.FontFamily, control.Font.SizeInPoints * factor, control.Font.Style, GraphicsUnit.Point);
			}
		}
	}

	private static string Str(object target, string name)
	{
		return Property(target, name)?.ToString() ?? "";
	}

	private static void SetProperty(object target, string name, object value)
	{
		if (target == null)
		{
			return;
		}
		for (Type type = target.GetType(); type != null; type = type.BaseType)
		{
			PropertyInfo propertyInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (propertyInfo != null)
			{
				propertyInfo.SetValue(target, value);
				return;
			}
		}
		Log.AppendLine("  !! property not found: " + name);
	}

	private static object Property(object target, string name)
	{
		if (target == null)
		{
			return null;
		}
		for (Type type = target.GetType(); type != null; type = type.BaseType)
		{
			PropertyInfo propertyInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (propertyInfo != null)
			{
				return propertyInfo.GetValue(target);
			}
		}
		return null;
	}

	private static void SetStatic(string typeName, string member, object value)
	{
		SetStatic(_asm.GetType(typeName), member, value);
	}

	private static void SetInstanceField(object target, string name, object value)
	{
		if (target == null)
		{
			return;
		}
		FieldInfo fieldInfo = null;
		for (Type type = target.GetType(); type != null && fieldInfo == null; type = type.BaseType)
		{
			fieldInfo = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		}
		if (fieldInfo == null)
		{
			Log.AppendLine($"  !! SetInstanceField 找不到字段 {name}");
			return;
		}
		fieldInfo.SetValue(target, value);
	}

	private static object GetStatic(string typeName, string member)
	{
		Type type = _asm.GetType(typeName);
		if (type == null)
		{
			return null;
		}
		FieldInfo field = type.GetField(member, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
		if (field != null)
		{
			return field.GetValue(null);
		}
		PropertyInfo property = type.GetProperty(member, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
		return ((property != null) ? property.GetValue(null) : null);
	}

	private static void SetStatic(Type type, string member, object value)
	{
		PropertyInfo propertyInfo = type?.GetProperty(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (propertyInfo != null)
		{
			propertyInfo.SetValue(null, value);
			return;
		}
		type?.GetField(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, value);
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		if (target == null)
		{
			return null;
		}
		MethodInfo methodInfo = null;
		foreach (MethodInfo item in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (item.Name == name && item.GetParameters().Length == args.Length)
			{
				methodInfo = item;
				break;
			}
		}
		if (methodInfo == null)
		{
			Log.AppendLine("  !! method not found: " + name);
			return null;
		}
		return methodInfo.Invoke(target, args);
	}

	private static object InvokeStatic(Type type, string name, params object[] args)
	{
		if (type == null)
		{
			return null;
		}
		MethodInfo methodInfo = null;
		foreach (MethodInfo item in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (item.Name == name && item.GetParameters().Length == args.Length)
			{
				methodInfo = item;
				break;
			}
		}
		if (methodInfo == null)
		{
			Log.AppendLine("  !! static method not found: " + name);
			return null;
		}
		return methodInfo.Invoke(null, args);
	}

	/// <summary>
	/// 2026-10-12锛堛€岄殣褰€嶆敹灏撅級锛氬け璐ヤ竴寰嬪啓 stdout/stderr + 閫€鍑虹爜 + 鏍圭洰褰曟棩蹇楋紝鉀?涓嶅脊妯℃€佹銆?	/// 鈿狅笍 鍙啓寮傚父绫诲瀷涓?sha锛屸洈 涓嶆墦鐢ㄦ埛鍚?鏈哄櫒鍚嶏紙瀹夸富璺緞閲屽彲鑳戒細甯︾敤鎴峰悕锛夈€?	/// </summary>
	private static void Fail(Exception ex)
	{
		try
		{
			string dir = Environment.GetEnvironmentVariable("LMECHREVO_LOG_DIR");
			if (string.IsNullOrWhiteSpace(dir))
			{
				dir = Path.GetTempPath();
			}
			Directory.CreateDirectory(dir);
			string kind = ((ex != null) ? ex.GetType().FullName : "(no exception)");
			string sha = ((ex != null) ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + ex.StackTrace))).Substring(0, 16) : "-");
			string file = Path.Combine(dir, "EGROUPSHOT-FAILED.txt");
			File.AppendAllText(file, "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " exit=" + Environment.ExitCode + " sha=" + sha + " ====" + Environment.NewLine + ex + Environment.NewLine, new UTF8Encoding(false));
			try
			{
				Console.Out.Flush();
				Console.Error.WriteLine("EGroupShot FAILED exit=" + Environment.ExitCode + " type=" + kind + " sha=" + sha + " log=" + file);
				Console.Error.Flush();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}}
