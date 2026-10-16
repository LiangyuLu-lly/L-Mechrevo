using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Update;
using PawnIO;

namespace MechrevoLite.UI;

internal static class UiAuditRunner
{
	private sealed record Viewport(int Width, int Height, int Dpi)
	{
		public string Id => $"{Width}x{Height}-{Dpi * 100 / 96}pct";

		public Rectangle WorkingArea => new Rectangle(0, 0, Width, Height - (int)Math.Round((float)(48 * Dpi) / 96f));
	}

	private sealed record AuditIssue(string Form, string Viewport, string Kind, string Control, string Detail);

	private static readonly Viewport[] Viewports = new Viewport[16]
	{
		new Viewport(1280, 720, 96),
		new Viewport(1280, 720, 144),
		new Viewport(1280, 720, 192),
		new Viewport(1366, 768, 120),
		new Viewport(1366, 768, 192),
		new Viewport(1600, 900, 120),
		new Viewport(1600, 900, 144),
		new Viewport(1920, 1080, 96),
		new Viewport(1920, 1080, 120),
		new Viewport(1920, 1080, 144),
		new Viewport(1920, 1080, 168),
		new Viewport(1920, 1080, 192),
		new Viewport(2560, 1440, 144),
		new Viewport(2560, 1440, 192),
		new Viewport(3840, 2160, 168),
		new Viewport(3840, 2160, 192)
	};

	public static int Run(string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		Program.UiAuditMode = true;
		int hostDpi = ReadHostDpi();
		Logger.WriteLine($"UI audit host DPI = {hostDpi} (viewport DPI is pinned via UiDpi.AuditDpi).");
		(string, Func<Form>, MechrevoDeviceCapabilities)[] array = new (string, Func<Form>, MechrevoDeviceCapabilities)[24]
		{
			("Settings", () => new SettingsForm(), null),
			("Settings-NoIGpu", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = true,
				TurboSubMode = false,
				CpuPerformanceTuning = true,
				DgpuDirect = true,
				IgpuOnly = false
			}),
			("Settings-HotSwap", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = true,
				CpuPerformanceTuning = true,
				DgpuDirect = true,
				IgpuOnly = true,
				GpuHotSwap = true
			}),
			("Settings-CustomMode", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = true,
				CpuPerformanceTuning = true,
				FanSettings = true,
				LiquidCooling = true,
				DgpuDirect = true,
				IgpuOnly = true
			}),
			("Settings-Minimal", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = false,
				TurboSubMode = false,
				DgpuDirect = false,
				IgpuOnly = false
			}),
			("Settings-NoLighting", () => new SettingsForm(), new MechrevoDeviceCapabilities
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
				IgpuOnly = true,
				Keyboard = false,
				Lightbar = false,
				LogoLight = false
			}),
			("Settings-NoSilentTurbo", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = true,
				TurboSubMode = false,
				CpuPerformanceTuning = true,
				FanSettings = true,
				LiquidCooling = true,
				DisplayRefresh = true,
				ColorCalibration = true,
				LcdOverdrive = true,
				DgpuDirect = true,
				IgpuOnly = true,
				Keyboard = true,
				Lightbar = true,
				LogoLight = true
			}),
			("Settings-NoLiquidCooling", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = true,
				TurboSubMode = true,
				CpuPerformanceTuning = true,
				FanSettings = true,
				LiquidCooling = false,
				DisplayRefresh = true,
				ColorCalibration = true,
				LcdOverdrive = true,
				DgpuDirect = true,
				IgpuOnly = true,
				Keyboard = true,
				Lightbar = true,
				LogoLight = true
			}),
			("Settings-MinimalDisplay", () => new SettingsForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				TurboMode = false,
				TurboSubMode = false,
				CpuPerformanceTuning = false,
				FanSettings = false,
				LiquidCooling = false,
				DisplayRefresh = false,
				ColorCalibration = false,
				LcdOverdrive = false,
				DgpuDirect = false,
				IgpuOnly = false,
				Keyboard = false,
				Lightbar = false,
				LogoLight = false
			}),
			("CustomMode", () => new CustomModeForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				CpuPerformanceTuning = true,
				OverclockSettings = true
			}),
			// 2026-10-05 step7A (upstream 0.290.6 A-27): CPU tuning fixtures.
			// 2026-10-05 coread: AMD fixture 补上"逐核回读值"，并新增"该平台没有回读命令"的
			// fixture —— 让 ui-audit 也能覆盖"回读: -30 CO" / "不可回读（该平台没有回读命令）"
			// 这两条新文案（写入路径不变：CanWrite 仍由探针决定）。
			// 2026-10-10 ui-audit-intel（本次「Intel 界面截图」任务）：该 fixture 早于 ⑦B 的
			//   「Intel 调校合并」，Detail 只有半句、Current 全为 null —— 那样拍出来的 Intel 形态
			//   与真机不符（回读行会显示"不可回读"，而 RuntimeCpuTuning.Probe 的 Intel 分支其实
			//   对 domain 0/2 各读过一次命令 16，一定带回 offset mV / ratio）。故按**探测代码真的会
			//   产出的形状**补齐，⛔ 只动合成数据、不触硬件、不改任何判定：
			//   * Detail = 逐字对齐 RuntimeCpuTuning.Probe() Intel 段的 domain 行 + hybrid 行格式；
			//   * Current = core/cache 的 offset（0.000 mV，未降压的出厂态）+ ratio 的 40x（非混合架构）；
			//   * 选项集合 / CanWrite / 范围 一字未改（core/cache -150..0 mV，ratio 8..64 x）。
			//   * ⚠️ 另加 ApplyIntelPlatformFixtureOverlay —— 平台显隐读的是**真 CPUID**（本机 AMD），
			//     只喂夹具仍会按 AMD 分支摆放控件，故在脚手架里逐条复刻 Intel 分支的可见性结果
			//     （理由与逐条对应见该方法的注释）。
			("CpuTune-Intel", () =>
			{
				CpuTuningForm intelFixtureForm = new CpuTuningForm(new CpuTuningProbe("Intel CPU", "CpuTuneIntelReady", "domain 0: capabilities=0x540, ratio=40/64, offset=0.000 mV, voltageSupported=True, ratioSupported=True" + Environment.NewLine + "domain 2: capabilities=0x540, ratio=0/0, offset=0.000 mV, voltageSupported=True, ratioSupported=False" + Environment.NewLine + "hybrid architecture: False (ratio option is offered only when false)", new CpuTuningOption[3]
				{
					new CpuTuningOption("intel-core", "CpuTuneCore", -150m, 0m, "mV", 0m, CanWrite: true),
					new CpuTuningOption("intel-cache", "CpuTuneCache", -150m, 0m, "mV", 0m, CanWrite: true),
					new CpuTuningOption("intel-ratio", "CpuTuneRatio", 8m, 64m, "x", 40, CanWrite: true)
				}));
				ApplyIntelPlatformFixtureOverlay(intelFixtureForm);
				return intelFixtureForm;
			}, null),
			// 2026-10-10 intel-fix（用户追问「还有英特尔也有限制频率吗」）：第二个 Intel 夹具 =
			// **混合架构（P 核 + E 核）**机型（例如 Intel Core Ultra 7 255HX）会看到的形态。
			// 与上面那个夹具的**唯一**差别：探针在 `IsHybrid() == true` 时**不给 intel-ratio**
			//   （RuntimeCpuTuning.cs:353 `if (domain == 0 && !IsHybrid() && intelDomain.RatioSupported)`，
			//    与上游 0.290.6 RuntimeCpuTuning.cs:109 逐字同源）⇒ 选项只剩 intel-core + intel-cache，
			//    两条都是**电压偏移 mV** —— 这台机器上 Intel 通路**没有任何"频率/倍频"项**。
			// Detail 第三行按探针真实的 hybrid 行格式写成 True，供摘要/技术详情核对。
			// ⚠️ 仍是合成数据：⛔ 不触硬件、不改任何判定；平台显隐仍靠 ApplyIntelPlatformFixtureOverlay。
			("CpuTune-Intel-Hybrid", () =>
			{
				CpuTuningForm intelHybridFixtureForm = new CpuTuningForm(new CpuTuningProbe("Intel Core Ultra 7 255HX (fixture)", "CpuTuneIntelReady", "domain 0: capabilities=0x540, ratio=45/0, offset=0.000 mV, voltageSupported=True, ratioSupported=True" + Environment.NewLine + "domain 2: capabilities=0x540, ratio=0/0, offset=0.000 mV, voltageSupported=True, ratioSupported=False" + Environment.NewLine + "hybrid architecture: True (ratio option is offered only when false)", new CpuTuningOption[2]
				{
					new CpuTuningOption("intel-core", "CpuTuneCore", -150m, 0m, "mV", 0m, CanWrite: true),
					new CpuTuningOption("intel-cache", "CpuTuneCache", -150m, 0m, "mV", 0m, CanWrite: true)
				}));
				ApplyIntelPlatformFixtureOverlay(intelHybridFixtureForm);
				return intelHybridFixtureForm;
			}, null),
			// 2026-10-10 fmax: 两个 AMD fixture 各补一条 FMax 结论，好让 ui-audit 也覆盖
			// 「FMax 可读写（显示回读值）」与「FMax 只读（写入禁用但仍显示回读值）」两种新文案。
			// ⚠️ 只给**展示**用：FMax 的写入永远只发生在用户点「应用」时，审计模式（UiAuditMode）
			//    下 ApplyFMaxAsync 会直接被拦下，一条命令都不发。
			// 2026-10-10 flat-rows（用户原话：「FMax和全核频率也是重复的」）：AMD 夹具补上 **amd-clock** ——
			// 真实探针在本机（Hawk Point / Phoenix，`ClockCommand` 有值）**一定**会给出这一条
			// （RuntimeCpuTuning.cs:268-273 `SupportsClock` ⇒ CanWrite:true，范围 1000..MaximumClock()），
			// 改前夹具里没有它 ⇒ 拍不出"固定全核频率行 + FMax 行同时在页面上"这个新形态。
			// ⚠️ 只补合成数据（范围取探针同源的 1000..4500、回读说明键用既有的 CpuTuneClockNoReadback）：
			//    ⛔ 不触硬件、⛔ 不改任何判定、⛔ 不新增写入路径。
			("CpuTune-AMD", () => new CpuTuningForm(new CpuTuningProbe("AMD CPU", "CpuTuneSmuReady", "DragonRange; SMU 0x545601; CO readback per core [-30, -30, -30, -30, -30, -30, -30, -30] (SMU current state, this boot only); fixed/limited clock is still available for this SKU", new CpuTuningOption[2]
			{
				// 2026-10-10 percorenolog：「全核负压框」已按用户要求删除 ⇒ AMD 夹具必须给出
				//   探针现在真正会产出的形状（PerCoreWrite: true），否则拍出来的每核表会恒为灰、
				//   "每核表 = CO 唯一输入口"这个新形态就拍不出来。⛔ 只改合成数据。
				new CpuTuningOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", -30m, CanWrite: true, new int[8] { -30, -30, -30, -30, -30, -30, -30, -30 }, null, PerCoreWrite: true),
				new CpuTuningOption("amd-clock", "CpuTuneClock", 1000m, 4500m, "MHz", null, CanWrite: true, null, "CpuTuneClockNoReadback")
			}, new CpuFMaxProbe(FMaxCapability.ReadWrite, 5850m, 0x000016DAu, "DragonRange fixture: FMax read OK (Get 0x6E), capability ReadWrite"))), null),
			("CpuTune-AMD-NoReadCmd", () =>
			{
				CpuTuningForm noReadCmdForm = new CpuTuningForm(new CpuTuningProbe("AMD CPU", "CpuTuneSmuReady", "Renoir; SMU 0x4C0000; CO readback: no read command for this codename", new CpuTuningOption[1]
				{
					new CpuTuningOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", null, CanWrite: true, null, "CpuTuneNoReadCmd")
				}, new CpuFMaxProbe(FMaxCapability.ReadOnly, 4200m, 0x00001068u, "Renoir fixture: FMax read OK (Get 0x42), capability ReadOnly")));
				return noReadCmdForm;
			}, null),
			("CpuTune-NoDriver", () => new CpuTuningForm(new CpuTuningProbe("CPU", "CpuTuneDriverMissing", "NotInstalled", Array.Empty<CpuTuningOption>())), null),
			("FanCurve", () => new FanCurveForm(), new MechrevoDeviceCapabilities
			{
				ProfileAvailable = true,
				FanSettings = true
			}),
			("FirstRunGuide", () => new FirstRunGuideForm(), null),
			("KeyboardRgb", () => new RgbForm(new KeyboardRgb()), null),
			("Lightbar", () => new LightForm("HidLightbar/Ctrl", "灯条灯效", LightForm.LightbarEffects), null),
			("LogoLight", () => new LightForm("HidLightbar_Logo/Ctrl", "Logo灯效", LightForm.LogoEffects), null),
			("Donate", () => new DonateForm(), null),
			("ColorPicker", () => new RColorPicker(Color.FromArgb(50, 219, 190), allowRandom: true), null),
			("SettingsDialog", () =>
			{
				BufferedPanel bufferedPanel = new BufferedPanel
				{
					Name = "panelThemeMode",
					CardStyle = true,
					Height = D(54),
					Padding = new Padding(D(12), D(8), D(12), D(8)),
					BackColor = UiVisualStyle.Surface
				};
				TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
				{
					Dock = DockStyle.Fill,
					ColumnCount = 3,
					RowCount = 1,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = bufferedPanel.BackColor
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
				bufferedPanel.Controls.Add(tableLayoutPanel);
				BufferedPanel bufferedPanel2 = new BufferedPanel
				{
					Name = "panelOfficialConsole",
					CardStyle = true,
					Height = D(48),
					Padding = new Padding(D(12), D(8), D(12), D(8)),
					BackColor = UiVisualStyle.Surface
				};
				TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
				{
					Dock = DockStyle.Fill,
					ColumnCount = 3,
					RowCount = 1,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = bufferedPanel2.BackColor
				};
				tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(126)));
				tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(150)));
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
					Margin = new Padding(D(8), 0, 0, 0),
					Cursor = Cursors.Hand
				}, 2, 0);
				bufferedPanel2.Controls.Add(tableLayoutPanel2);
				RCheckBox overdriveChk = new RCheckBox
				{
					Name = "checkLcdOverdrive",
					Text = "响应加速",
					AutoSize = true,
					Visible = true,
					ForeColor = UiVisualStyle.Text
				};
				return new SettingsDialog(bufferedPanel, bufferedPanel2, overdriveChk, displayGroupAvailable: true, null);
			}, null),
			("UpdateForm", () => new UpdateForm(new UpdateInfo("0.0.0-audit", "9.9.9-audit", "audit", ChannelFallback: false, UpdateAvailable: true, "2026-09-13", "审计合成数据（离线，不触网）。", null, null, null, null, "https://example.invalid/audit")), null)
		};
		// ==========================================================================================
		// 2026-10-10 ui-audit-intel：**可选**的"只跑子集"开关（默认两个环境变量都不设 ⇒ 行为与改前逐字
		// 一致：仍跑满 16 视口 × 全部窗体）。加它的原因：只想给某一两个窗体（例如「CPU 调节」的
		// Intel / AMD 形态）拍图做人工检查时，没必要花几分钟跑满整个矩阵、写掉几百 MB PNG。
		//   * LMECHREVO_UI_AUDIT_FORMS    = 逗号分隔的夹具名（前缀匹配，如 `CpuTune-`）；
		//   * LMECHREVO_UI_AUDIT_VIEWPORTS = 逗号分隔的视口 Id（如 `1920x1080-150pct`）。
		// ⛔ 只筛"跑哪些"，⛔ 不改任何 fixture 内容 / 判定 / 截图与报告逻辑。
		// ==========================================================================================
		string[] formFilter = SplitAuditFilter(Environment.GetEnvironmentVariable("LMECHREVO_UI_AUDIT_FORMS"));
		string[] viewportFilter = SplitAuditFilter(Environment.GetEnvironmentVariable("LMECHREVO_UI_AUDIT_VIEWPORTS"));
		if (formFilter.Length > 0)
		{
			array = array.Where((f) => formFilter.Any((p) => f.Item1.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToArray();
		}
		Viewport[] viewports = ((viewportFilter.Length > 0) ? Viewports.Where((v) => viewportFilter.Contains(v.Id, StringComparer.OrdinalIgnoreCase)).ToArray() : Viewports);
		Logger.WriteLine($"UI audit filter: forms=[{string.Join(",", array.Select((f) => f.Item1))}] viewports=[{string.Join(",", viewports.Select((v) => v.Id))}]");
		List<AuditIssue> issues = new List<AuditIssue>();
		List<string> list = new List<string>();
		List<string> rowGaps = new List<string>();
		List<string> checkLog = new List<string>();
		string activeFactory = "unknown";
		string activeViewport = "unknown";
		ThreadExceptionEventHandler value = (object _, ThreadExceptionEventArgs args) =>
		{
			issues.Add(new AuditIssue(activeFactory, activeViewport, "ui-thread-exception", activeFactory, args.Exception.ToString()));
			Logger.WriteLine($"UI audit thread exception ({activeFactory}/{activeViewport}): {args.Exception}");
		};
		Application.ThreadException += value;
		foreach (Viewport viewport in viewports)
		{
			(string, Func<Form>, MechrevoDeviceCapabilities)[] array2 = array;
			for (int num2 = 0; num2 < array2.Length; num2++)
			{
				(string, Func<Form>, MechrevoDeviceCapabilities) tuple = array2[num2];
				UiVisualStyle.SetAuditNightMode(true);
				activeFactory = tuple.Item1;
				activeViewport = viewport.Id;
				Form form = null;
				MechrevoHw mechrevoHw = null;
				try
				{
					UiDpi.AuditDpi = viewport.Dpi;
					Program.UiAuditUseReportedCapabilities = tuple.Item3 != null;
					if (tuple.Item3 != null)
					{
						mechrevoHw = new MechrevoHw(null, tuple.Item3);
						if (tuple.Item1 == "CustomMode")
						{
							mechrevoHw.HandleMessage("Fan/Status", "{\"GPU_CoreClockOffsetMinimumHWOC\":-500,\"GPU_CoreClockOffsetMaximumHWOC\":500,\r\n \"GPU_MemoryClockOffsetMinimumHWOC\":-500,\"GPU_MemoryClockOffsetMaximumHWOC\":500,\r\n \"GPU_CoreClockOffsetOC\":500,\"GPU_MemoryClockOffsetOC\":-500,\"OverClockingSwitch\":1}");
							mechrevoHw.HandleMessage("LCHWOC/Status", "{\"Support\":true,\"Enable\":true}");
						}
						if (tuple.Item1 == "FanCurve")
						{
							mechrevoHw.HandleMessage("Fan/Status", "{\"FanControlRespective\":true}");
						}
						Program.hw = mechrevoHw;
					}
					form = tuple.Item2();
					Form form2 = form;
					string name;
					if (string.IsNullOrWhiteSpace(form.Name))
					{
						(name, _, _) = tuple;
					}
					else
					{
						name = form.Name;
					}
					form2.Name = name;
					if (form is SettingsForm)
					{
						string[] array3 = (Program.UiAuditUseReportedCapabilities ? Array.Empty<string>() : new string[1] { "checkLcdOverdrive" });
						foreach (string key in array3)
						{
							Control control = form.Controls.Find(key, searchAllChildren: true).FirstOrDefault();
							if (control != null)
							{
								control.Visible = true;
							}
						}
						if (form.Controls.Find("buttonBatteryMode0", searchAllChildren: true).FirstOrDefault() is Button button)
						{
							button.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 80);
							button.FlatAppearance.BorderSize = 2;
						}
						if (tuple.Item1 == "Settings-CustomMode")
						{
							((SettingsForm)form).ShowMode(MechrevoService.ToVisualMode(3));
						}
					}
					form.ShowInTaskbar = false;
					form.StartPosition = FormStartPosition.Manual;
					form.Opacity = 0.01;
					form.Location = new Point(20, 20);
					form.Show();
					Application.DoEvents();
					int num4 = UiDpi.Layout(form);
					float num5 = (float)viewport.Dpi / (float)num4;
					if (Math.Abs(num5 - 1f) > 0.01f)
					{
						form.Scale(new SizeF(num5, num5));
						ScaleFonts(form, num5);
					}
					if (form is RForm rForm)
					{
						rForm.AuditLayoutScale = (float)viewport.Dpi / 96f;
						rForm.ApplyResponsiveBounds(viewport.WorkingArea);
					}
					else
					{
						ResponsiveLayout.ConstrainToWorkingArea(form, viewport.WorkingArea);
					}
					ResponsiveLayout.PerformLayoutTree(form);
					Application.DoEvents();
					if (form is SettingsForm { Handle: var handle } settingsForm)
					{
						settingsForm.Hide();
						settingsForm.ShowAll(viewport.WorkingArea);
						Application.DoEvents();
						settingsForm.Hide();
						settingsForm.ShowAll(viewport.WorkingArea);
						Application.DoEvents();
						if (!settingsForm.Visible || settingsForm.Opacity < 0.99 || settingsForm.Handle != handle)
						{
							issues.Add(new AuditIssue("Settings-TrayRestore", viewport.Id, "restore-state", settingsForm.Name, $"Visible={settingsForm.Visible}, Opacity={settingsForm.Opacity}, HandleRecreated={settingsForm.Handle != handle}."));
						}
						for (int num6 = 0; num6 < settingsForm.DashboardPageCount; num6++)
						{
							settingsForm.SelectDashboardPageForAudit(num6);
							ResponsiveLayout.PerformLayoutTree(settingsForm);
							Application.DoEvents();
							string formName = tuple.Item1 + "-" + settingsForm.DashboardPageAuditName(num6);
							foreach (Control item in AllControls(settingsForm))
							{
								PictureBox pictureBox = item as PictureBox;
								bool flag;
								if (pictureBox != null)
								{
									string name2 = item.Name;
									if (name2 == "picturePerf" || name2 == "pictureBattery")
									{
										flag = true;
										goto IL_0b31;
									}
								}
								flag = false;
								goto IL_0b31;
								IL_0b31:
								if (flag)
								{
									Logger.WriteLine($"[diag] at-capture form={settingsForm.GetHashCode()} {pictureBox.Name}#{pictureBox.GetHashCode()} vis={pictureBox.Visible} tag={pictureBox.Tag}");
								}
							}
							Control control2 = AllControls(settingsForm).FirstOrDefault((Control c) => c.Name == "panelCPUTitle");
							if (control2 != null)
							{
								Control control3 = control2;
								if (Program.UiAuditMode && viewport.Dpi == 96)
								{
									Control control4 = AllControls(settingsForm).FirstOrDefault((Control c) => c.Name == "panelPerformance");
									Logger.WriteLine($"[diag] cpuperf panel={control4?.Width}x{control4?.Height} title={control3.Width}x{control3.Height}@{control3.Location} children={string.Join(";", from Control c in control3.Controls
										select $"{c.Name}:{c.Width}x{c.Height}@{c.Location.X},{c.Location.Y} vis={c.Visible}")}");
								}
							}
							Control control5 = AllControls(settingsForm).FirstOrDefault((Control c2) => c2.Name == "panelBrightness");
							if (control5 != null && viewport.Dpi == 96)
							{
								Logger.WriteLine($"[diag] screen: bounds={control5.Bounds} vis={control5.Visible} children={string.Join(";", from Control c3 in control5.Controls
									select $"{c3.Name}:{c3.Width}x{c3.Height}@{c3.Location.X},{c3.Location.Y} vis={c3.Visible}")}");
							}
							string[] array3 = new string[3] { "panelVersion", "panelStartup", "checkStartup" };
							foreach (string probe in array3)
							{
								Control control6 = AllControls(settingsForm).FirstOrDefault((Control c) => c.Name == probe);
								if (control6 != null)
								{
									Logger.WriteLine($"[diag] deferred {control6.Name}: bounds={control6.Bounds} vis={control6.Visible} parent={control6.Parent?.Name ?? "null"} parentVis={control6.Parent?.Visible}");
								}
							}
							AuditForm(formName, viewport, settingsForm, issues, rowGaps, checkLog);
							foreach (string item2 in CapturePages(outputDirectory, formName, viewport, settingsForm))
							{
								list.Add(item2);
							}
						}
						settingsForm.ApplyThemeMode(night: false);
						settingsForm.SelectDashboardPageForAudit(2);
						ResponsiveLayout.PerformLayoutTree(settingsForm);
						Application.DoEvents();
						AuditForm(tuple.Item1 + "-Day-System", viewport, settingsForm, issues, rowGaps, checkLog);
						foreach (string item3 in CapturePages(outputDirectory, tuple.Item1 + "-Day-System", viewport, settingsForm))
						{
							list.Add(item3);
						}
						settingsForm.ApplyThemeMode(night: true);
						settingsForm.SelectDashboardPageForAudit(0);
						ResponsiveLayout.PerformLayoutTree(settingsForm);
						Application.DoEvents();
						AuditForm(tuple.Item1 + "-Night-Common", viewport, settingsForm, issues, rowGaps, checkLog);
						foreach (string item4 in CapturePages(outputDirectory, tuple.Item1 + "-Night-Common", viewport, settingsForm))
						{
							list.Add(item4);
						}
						AuditBrightnessIsolation(viewport, settingsForm, issues);
						continue;
					}
					AuditForm(tuple.Item1, viewport, form, issues, rowGaps, checkLog);
					foreach (string item5 in CapturePages(outputDirectory, tuple.Item1, viewport, form))
					{
						list.Add(item5);
					}
				}
				catch (Exception ex)
				{
					issues.Add(new AuditIssue(tuple.Item1, viewport.Id, "render-error", tuple.Item1, ex.ToString()));
				}
				finally
				{
					form?.Dispose();
					UiDpi.AuditDpi = 0;
					UiVisualStyle.SetAuditNightMode(null);
					if (mechrevoHw != null)
					{
						Program.hw = null;
						mechrevoHw.Dispose();
					}
					Program.UiAuditUseReportedCapabilities = false;
					Application.DoEvents();
				}
			}
		}
		Application.ThreadException -= value;
		WriteJson(outputDirectory, viewports, list.Count, issues, hostDpi, rowGaps, checkLog);
		WriteMarkdown(outputDirectory, viewports, list, issues, rowGaps, checkLog);
		return (issues.Count != 0) ? 1 : 0;
		int D(int num7)
		{
			return (int)Math.Round((float)(num7 * hostDpi) / 96f);
		}
	}

	// 2026-10-10 ui-audit-intel：把 `LMECHREVO_UI_AUDIT_FORMS` / `LMECHREVO_UI_AUDIT_VIEWPORTS`
	// 解析成前缀列表；空 / 未设 ⇒ 返回空数组（= 不过滤，跑满矩阵）。
	// ⚠️ 2026-10-10 intel-fix：去掉 `string?` 的 `?`（本工程未开 <Nullable>，只多一条 CS8632；语义不变）。
	private static string[] SplitAuditFilter(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<string>();
		}
		return raw.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	// ==========================================================================================
	// 2026-10-10 ui-audit-intel：**把「Intel 平台」那一套显隐照搬到合成夹具上**。
	//
	// 为什么非这样不可：`CpuTuningForm` 的平台判据读的是**真实 CPUID** ——
	//   AmdTuningPlatform()  = CpuInfo.IsAMD（PawnIO\CpuInfo.cs:17，CPUID 厂商串 AuthenticAMD）
	//   IntelTuningPlatform() = !CpuInfo.IsAMD && RuntimeCpuTuning.IsIntel()（CPUID 串 GenuineIntel）
	// 本机是 AMD 8745H ⇒ 两个判据恒为 (true, false)：**哪怕喂 Intel 夹具**，UpdateSelection 也只会按
	// AMD 分支摆放控件（实测第一版截图：Intel 夹具下出现了 FMax 行、开机自动应用开关、AMD 版顶部文案）。
	// 仓库里**没有**任何 CPU 厂商的审计开关（CpuInfo / RuntimeCpuTuning 全仓库无 UiAudit* 分支），
	// 而本任务⛔不许改生产逻辑（CpuTuningForm / CpuInfo / RuntimeCpuTuning），故只能在审计脚手架里
	// **逐条复刻 Intel 平台上 UpdateSelection 的结果**：⛔ 只改 Visible 与顶部那一行文案，
	// ⛔ 不碰任何 Enabled / 取值 / 事件 / 范围 / 写入路径 / 布局参数。
	//
	// 逐条对应（行号会随修改变动，这里只写字段名与判据）：
	//   amdTuning=false 会隐藏：_autoApply、_autoApplyWarning、_fmaxRow、_fmaxNote、
	//     _snapshotRow；（`_clockRow` 与 `_freqNote` 现在还多一条"探针给不给 amd-clock"的前提，
	//     而 Intel 夹具根本没有 amd-clock ⇒ 它们自己就收起了，不需要复刻。）
	//     ⚠️ 2026-10-10 percorenolog：`_coAllRow`（"全核降压值 (CO)"那一行）**已按用户要求删除**
	//     （用户原话「全核负压框删掉」），所以不再出现在这份复刻名单里。
	//   intelTuning=true 会做：_resetButton.Visible=true、_scopeNotice.Text=Localized("CpuTuneIntelReady")。
	// ⭐ 2026-10-10 flat-rows 起，**Intel 的每一个选项行都是数据驱动的**（`SyncOptionRows` 按探针给的
	//    选项集合建行、`_ratioRow` 按 `_ratioOption != null` 显隐）⇒ 这些行**不再需要平台复刻**：
	//    夹具喂什么就画什么（Intel 夹具 = intel-core + intel-cache 两行 + 倍频行；混合架构夹具 =
	//    只有两个电压行、**没有**倍频行）。这也是本轮把上一版那三条复刻（`_clockRow` 显示 /
	//    `_clockApply` 收起 / 值行标签）**全部删掉**的原因 —— 那一版是"下拉 + 复用时钟行"时代的产物，
	//    那个设计已经不存在了。
	// ⚠️ 审计模式下 `idle=false`（CpuTuningForm.UpdateSelection 第一行）⇒ 每一行的「应用」与数值框
	//    **本来就恒为灰**（判据 `idle && <该行选项>.CanWrite`），所以截图只能证明"可见性/布局/文案"，
	//    ⛔ 不能证明真机上的可点性 —— 真机判据见 CpuTuningForm.UpdateSelection 的静态核对。
	// ==========================================================================================
	private static void ApplyIntelPlatformFixtureOverlay(CpuTuningForm form)
	{
		foreach (string hidden in new string[5] { "_autoApply", "_autoApplyWarning", "_fmaxRow", "_fmaxNote", "_snapshotRow" })
		{
			SetControlVisible(form, hidden, visible: false);
		}
		SetControlVisible(form, "_resetButton", visible: true);
		FieldInfo? scopeNotice = form.GetType().GetField("_scopeNotice", BindingFlags.Instance | BindingFlags.NonPublic);
		if (scopeNotice?.GetValue(form) is Label label)
		{
			label.Text = CpuTuningForm.Localized("CpuTuneIntelReady");
		}
	}

	private static void SetControlVisible(Form form, string fieldName, bool visible)
	{
		FieldInfo? field = form.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		if (field?.GetValue(form) is Control control)
		{
			control.Visible = visible;
		}
		else
		{
			Logger.WriteLine($"[ui-audit] Intel platform overlay: field '{fieldName}' not found / not a Control (fixture would render AMD gating).");
		}
	}

	private static int ReadHostDpi()
	{
		try
		{
			using Form control = new Form();
			return UiDpi.Layout(control);
		}
		catch
		{
			return 96;
		}
	}

	private static void WriteJson(string outputDirectory, Viewport[] viewports, int screenshotCount, IReadOnlyCollection<AuditIssue> issues, int hostDpi, IReadOnlyCollection<string> rowGaps, IReadOnlyCollection<string> checkLog)
	{
		using FileStream utf8Json = File.Create(Path.Combine(outputDirectory, "ui-audit.json"));
		using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(utf8Json, new JsonWriterOptions
		{
			Indented = true
		});
		utf8JsonWriter.WriteStartObject();
		utf8JsonWriter.WriteString("GeneratedAt", DateTimeOffset.Now);
		utf8JsonWriter.WriteNumber("HostDpi", hostDpi);
		utf8JsonWriter.WritePropertyName("Viewports");
		utf8JsonWriter.WriteStartArray();
		foreach (Viewport viewport in viewports)
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteNumber("Width", viewport.Width);
			utf8JsonWriter.WriteNumber("Height", viewport.Height);
			utf8JsonWriter.WriteNumber("Dpi", viewport.Dpi);
			utf8JsonWriter.WriteString("Id", viewport.Id);
			utf8JsonWriter.WriteEndObject();
		}
		utf8JsonWriter.WriteEndArray();
		utf8JsonWriter.WriteNumber("ScreenshotCount", screenshotCount);
		utf8JsonWriter.WriteNumber("IssueCount", issues.Count);
		utf8JsonWriter.WritePropertyName("Issues");
		utf8JsonWriter.WriteStartArray();
		foreach (AuditIssue issue in issues)
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteString("Form", issue.Form);
			utf8JsonWriter.WriteString("Viewport", issue.Viewport);
			utf8JsonWriter.WriteString("Kind", issue.Kind);
			utf8JsonWriter.WriteString("Control", issue.Control);
			utf8JsonWriter.WriteString("Detail", issue.Detail);
			utf8JsonWriter.WriteEndObject();
		}
		utf8JsonWriter.WriteEndArray();
		utf8JsonWriter.WritePropertyName("RowGaps");
		utf8JsonWriter.WriteStartArray();
		foreach (string rowGap in rowGaps)
		{
			utf8JsonWriter.WriteStringValue(rowGap);
		}
		utf8JsonWriter.WriteEndArray();
		utf8JsonWriter.WritePropertyName("Checks");
		utf8JsonWriter.WriteStartArray();
		foreach (string item in checkLog)
		{
			utf8JsonWriter.WriteStringValue(item);
		}
		utf8JsonWriter.WriteEndArray();
		utf8JsonWriter.WriteEndObject();
	}

	private static void ScaleFonts(Control root, float ratio)
	{
		(Control, Font)[] array = (from control in Flatten(root)
			select (Control: control, Font: control.Font)).ToArray();
		for (int num = 0; num < array.Length; num++)
		{
			(Control, Font) tuple = array[num];
			Font item = tuple.Item2;
			float emSize = Math.Max(1f, item.SizeInPoints * ratio);
			tuple.Item1.Font = new Font(item.FontFamily, emSize, item.Style, GraphicsUnit.Point, item.GdiCharSet, item.GdiVerticalFont);
		}
	}

	private static void AuditForm(string formName, Viewport viewport, Form form, List<AuditIssue> issues, List<string> rowGaps = null, List<string> checkLog = null)
	{
		if (form.Width > viewport.WorkingArea.Width || form.Height > viewport.WorkingArea.Height)
		{
			issues.Add(new AuditIssue(formName, viewport.Id, "window-overflow", form.Name, $"Window {form.Size} exceeds working area {viewport.WorkingArea.Size}."));
		}
		foreach (Control item in from control in Flatten(form)
			where control.Visible
			select control)
		{
			if (item is ScrollableControl scrollableControl && scrollableControl.HorizontalScroll.Visible)
			{
				issues.Add(new AuditIssue(formName, viewport.Id, "horizontal-scroll", ControlPath(item), $"Horizontal scrollbar is visible; display {scrollableControl.DisplayRectangle.Size}, client {scrollableControl.ClientSize}."));
			}
			if (item.Width <= 0 || item.Height <= 0)
			{
				issues.Add(new AuditIssue(formName, viewport.Id, "zero-size", ControlPath(item), item.Size.ToString()));
				continue;
			}
			Control parent = item.Parent;
			if (parent != null && !(parent is ScrollableControl { AutoScroll: not false }) && parent.Name != "dashboardPageHost")
			{
				Rectangle clientRectangle = parent.ClientRectangle;
				if (item.Left < clientRectangle.Left - 1 || item.Top < clientRectangle.Top - 1 || item.Right > clientRectangle.Right + 1 || item.Bottom > clientRectangle.Bottom + 1)
				{
					issues.Add(new AuditIssue(formName, viewport.Id, "parent-overflow", ControlPath(item), $"Bounds {item.Bounds} outside parent client {clientRectangle}."));
				}
			}
			string textClipping = GetTextClipping(item);
			if (textClipping != null)
			{
				issues.Add(new AuditIssue(formName, viewport.Id, "text-clipping", ControlPath(item), textClipping));
			}
		}
		foreach (var item2 in CheckClipping(form))
		{
			issues.Add(new AuditIssue(formName, viewport.Id, item2.Kind, item2.Control, item2.Detail));
		}
		foreach (var item3 in CheckEmptyBands(form))
		{
			issues.Add(new AuditIssue(formName, viewport.Id, item3.Kind, item3.Control, item3.Detail));
		}
		if (rowGaps != null)
		{
			foreach (string item4 in DescribeRowGaps(form))
			{
				rowGaps.Add($"{formName}/{viewport.Id}: {item4}");
			}
		}
		foreach (var item5 in CheckSiblingOverlaps(form))
		{
			issues.Add(new AuditIssue(formName, viewport.Id, item5.Kind, item5.Control, item5.Detail));
		}
		List<(string, string, string)> list = CheckFooterOcclusion(form);
		foreach (var item6 in list)
		{
			issues.Add(new AuditIssue(formName, viewport.Id, item6.Item1, item6.Item2, item6.Item3));
		}
		checkLog?.Add($"footer-occlusion {formName}/{viewport.Id}: " + ((!HasFooter(form)) ? "N/A(no footer)" : ((list.Count == 0) ? "PASS" : $"FAIL({list.Count})")));
	}

	internal static List<(string Kind, string Control, string Detail)> CheckSiblingOverlaps(Form form)
	{
		List<(string, string, string)> list = new List<(string, string, string)>();
		foreach (Control item in from control in Flatten(form)
			where control.Visible && !(control is TableLayoutPanel) && !(control is FlowLayoutPanel)
			select control)
		{
			Control[] array = (from Control control in item.Controls
				where control.Visible && control.Controls.Count == 0 && control.Width > 2 && control.Height > 2
				select control).ToArray();
			for (int num = 0; num < array.Length; num++)
			{
				for (int num2 = num + 1; num2 < array.Length; num2++)
				{
					Rectangle rectangle = Rectangle.Intersect(array[num].Bounds, array[num2].Bounds);
					if (rectangle.Width > 3 && rectangle.Height > 3)
					{
						list.Add(("sibling-overlap", ControlPath(item), $"{DisplayName(array[num])} {array[num].Bounds} overlaps {DisplayName(array[num2])} {array[num2].Bounds}."));
					}
				}
			}
		}
		return list;
	}

	internal static List<(string Kind, string Control, string Detail)> CheckFooterOcclusion(Form form)
	{
		List<(string, string, string)> list = new List<(string, string, string)>();
		Control control = Flatten(form).FirstOrDefault((Control control2) => control2.Visible && control2.Name == "panelFooter");
		if (control == null)
		{
			return list;
		}
		Control? root = Flatten(form).FirstOrDefault((Control control2) => control2.Name == "dashboardPageHost") ?? form;
		RScrollBar rScrollBar = Flatten(form).OfType<RScrollBar>().FirstOrDefault((RScrollBar bar) => bar.Name == "dashboardScrollBar");
		int num = ((rScrollBar != null) ? (-(rScrollBar.MaxValue - rScrollBar.Value)) : 0);
		int num2 = RelativeTop(control, form);
		foreach (Control item in Flatten(root))
		{
			if (item.Visible && item.Controls.Count <= 0 && item.CanSelect && item.Width > 2 && item.Height > 2)
			{
				int num3 = RelativeTop(item, form) + item.Height + num;
				if (num3 > num2 + 2)
				{
					list.Add(("footer-occlusion", ControlPath(item), $"{DisplayName(item)} bottom {num3} is below panelFooter top {num2} at max scroll."));
				}
			}
		}
		return list;
	}

	private static int RelativeTop(Control control, Control ancestor)
	{
		int num = 0;
		Control control2 = control;
		while (control2 != null && control2 != ancestor)
		{
			num += control2.Top;
			control2 = control2.Parent;
		}
		return num;
	}

	internal static bool HasFooter(Form form)
	{
		return Flatten(form).Any((Control control) => control.Visible && control.Name == "panelFooter");
	}

	internal static List<(string Kind, string Control, string Detail)> CheckEmptyBands(Form form)
	{
		List<(string, string, string)> list = new List<(string, string, string)>();
		foreach (Control item in from c in Flatten(form)
			where c.Visible
			select c)
		{
			TableLayoutPanel tlp = item as TableLayoutPanel;
			if (tlp != null && tlp.RowCount > 0 && tlp.Height > 2)
			{
				int[] rowHeights = tlp.GetRowHeights();
				int row;
				for (row = 0; row < rowHeights.Length; row++)
				{
					if (rowHeights[row] > 2 && !tlp.Controls.Cast<Control>().Any((Control child) => tlp.GetRow(child) == row && child.Visible))
					{
						list.Add(("empty-band", ControlPath(tlp), $"Row {row} is {rowHeights[row]}px tall with no visible child."));
					}
				}
			}
			else if (!(item is TableLayoutPanel) && item.Controls.Count > 0 && item.Height > 2 && item.Controls.Cast<Control>().All((Control child) => !child.Visible))
			{
				list.Add(("empty-band", ControlPath(item), $"Container {item.Size} has {item.Controls.Count} children, none visible."));
			}
		}
		return list;
	}

	internal static List<string> DescribeRowGaps(Form form)
	{
		List<string> list = new List<string>();
		foreach (Control item in from c in Flatten(form)
			where c.Visible && c is TableLayoutPanel { RowCount: >1 } tableLayoutPanel && tableLayoutPanel.Height > 2
			select c)
		{
			TableLayoutPanel tlp = (TableLayoutPanel)item;
			int[] rowHeights = tlp.GetRowHeights();
			List<(int, int, int)> list2 = new List<(int, int, int)>();
			int row;
			for (row = 0; row < rowHeights.Length; row++)
			{
				if (rowHeights[row] > 2 && tlp.Controls.Cast<Control>().Any((Control child) => tlp.GetRow(child) == row && child.Visible))
				{
					list2.Add((row, 0, 0));
				}
			}
			if (list2.Count >= 2)
			{
				List<int> list3 = new List<int>();
				for (int num = 1; num < list2.Count; num++)
				{
					list3.Add((rowHeights[list2[num - 1].Item1] >= 0) ? GapBetween(tlp, list2[num - 1].Item1, list2[num].Item1) : 0);
				}
				list.Add($"{ControlPath(tlp)} rows=[{string.Join(',', list2.Select(((int Row, int Top, int Bottom) o) => o.Row))}] gaps=[{string.Join(',', list3)}] heights=[{string.Join(',', rowHeights)}]");
			}
		}
		return list;
		static int GapBetween(TableLayoutPanel tableLayoutPanel, int upperRow, int lowerRow)
		{
			Control control = (from Control c in tableLayoutPanel.Controls
				where tableLayoutPanel.GetRow(c) == upperRow && c.Visible
				orderby c.Bottom
				select c).FirstOrDefault();
			Control control2 = (from Control c in tableLayoutPanel.Controls
				where tableLayoutPanel.GetRow(c) == lowerRow && c.Visible
				orderby c.Top
				select c).FirstOrDefault();
			if (control != null && control2 != null)
			{
				return control2.Top - control.Bottom;
			}
			return -1;
		}
	}

	internal static string GetTextClipping(Control control)
	{
		if (control is RCheckBox { Appearance: Appearance.Normal } rCheckBox)
		{
			Size value = rCheckBox.MeasurePreferredSize((UiDpi.AuditDpi > 0) ? UiDpi.AuditDpi : UiDpi.Layout(rCheckBox));
			if (rCheckBox.Width < value.Width || rCheckBox.Height < value.Height)
			{
				return $"Switch text '{rCheckBox.Text}' needs {value}, available {rCheckBox.Size}.";
			}
			return null;
		}
		// 2026-10-09（批次3 #8「--ui-audit 下拉框截断盲区」）：⭐ 我们自己的下拉（RComboBox）**闭合态**走**自己的几何**，
		// 旧审计却按系统 ComboBox 估算（`HorizontalScrollBarArrowWidth + 8`）——真实绘制还要再让出
		// 「箭头格 ArrowCellLogicalPx（默认 26 逻辑像素）+ 左右两份 TextPadLogical」，
		// 于是旧可用宽度比真实可用宽度**宽 ≈17 逻辑像素（100% DPI）~25（150%）**：
		// 真机上「配置 1」被画成「配...」时，审计因为"估算够宽"一声不响（这就是盲区）。
		// 现在直接问控件要**绘制用的那个矩形**与**绘制用的那段文字**（同一来源，DPI/箭头格/内边距全含在内），
		// 用与绘制一致的字体与度量标志算实际渲染宽度：`实际渲染宽度 > 可用宽度` ⇒ 报出（此时绘制必然打省略号）。
		if (control is RComboBox rCombo)
		{
			string closed = rCombo.ClosedStateTextForAudit();
			if (string.IsNullOrWhiteSpace(closed))
			{
				return null;
			}
			Rectangle textRect = rCombo.ClosedStateTextRectangleForAudit();
			TextFormatFlags closedFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
			Size closedNeeded = TextRenderer.MeasureText(closed, rCombo.Font, new Size(int.MaxValue, int.MaxValue), closedFlags);
			if (closedNeeded.Width > textRect.Width)
			{
				return $"Closed-state text '{closed}' needs {closedNeeded.Width}px, available {textRect.Width}px (arrow cell {rCombo.ArrowCellLogicalPx} + text pad {rCombo.TextPadLogical} logical px already deducted; drawn with EndEllipsis).";
			}
			return null;
		}
		// 2026-10-11（真机「读数换行」修复配套）：读数片 RTelemetryLabel **自己清空 Padding 并自绘单行**
		// （文本矩形 = 整个客户区，见该类的 OnPaint），旧审计却按"基类 Label 带内边距 + 会按词折行"估算
		// （`ClientSize.Width - Padding.Horizontal - 8`，并用 WordBreak 量高）⇒ 会把"实际画得下、且永远
		// 单行"的读数误报成 text-clipping（实测 100% DPI 下误报 6 条/视口）。这里与 RComboBox 同一做法：
		// 用**绘制用的旗标**量（SingleLine | NoPadding），与它真实画出来的字一一对应。
		if (control is RTelemetryLabel rTelemetry)
		{
			if (string.IsNullOrWhiteSpace(rTelemetry.Text))
			{
				return null;
			}
			// ⚠️ 必须带 NoPadding：读数片自己 Padding=Empty 且自绘时不留内边距，
			// 而 TextRenderer 的 Default 口径会额外算上 Label 默认内边距（实测 +7px @96DPI）⇒
			// 只写 SingleLine 会把"实际画得下"的读数全部误报。
			TextFormatFlags telemetryFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
			Size telemetryNeeded = TextRenderer.MeasureText(rTelemetry.Text, rTelemetry.Font, new Size(int.MaxValue, int.MaxValue), telemetryFlags);
			if (telemetryNeeded.Width > rTelemetry.ClientSize.Width || telemetryNeeded.Height > rTelemetry.ClientSize.Height)
			{
				return $"Telemetry reading '{rTelemetry.Text}' needs {telemetryNeeded}, available {rTelemetry.ClientSize} (drawn single-line, no padding; EndEllipsis).";
			}
			return null;
		}
		ComboBox combo = control as ComboBox;
		if (combo != null)
		{
			int num = SystemInformation.HorizontalScrollBarArrowWidth + 8;
			int num2 = Math.Max(1, combo.ClientSize.Width - combo.Padding.Horizontal - num);
			int num3 = Math.Max(1, combo.ClientSize.Height - combo.Padding.Vertical - 4);
			TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
			IEnumerable<string?> enumerable2;
			if (combo.Items.Count <= 0)
			{
				IEnumerable<string> enumerable = (string.IsNullOrWhiteSpace(combo.Text) ? Array.Empty<string>() : new string[1] { combo.Text });
				enumerable2 = enumerable;
			}
			else
			{
				enumerable2 = from object item in combo.Items
					select combo.GetItemText(item) ?? string.Empty;
			}
			foreach (string item in enumerable2)
			{
				if (!string.IsNullOrWhiteSpace(item))
				{
					Size value = TextRenderer.MeasureText(item, combo.Font, new Size(num2, int.MaxValue), flags);
					if (value.Width > num2 + 2 || value.Height > num3 + 2)
					{
						return $"Text '{item.Replace(Environment.NewLine, " / ")}' needs {value}, available {num2}x{num3}.";
					}
				}
			}
			return null;
		}
		if (string.IsNullOrWhiteSpace(control.Text))
		{
			return null;
		}
		if (control is Label { AutoSize: not false } || control is CheckBox { AutoSize: not false } || control is RadioButton { AutoSize: not false })
		{
			return null;
		}
		if (!(control is Label) && !(control is ButtonBase))
		{
			return null;
		}
		bool flag = ((control is CheckBox || control is RadioButton) ? true : false);
		int num4 = (flag ? (SystemInformation.MenuCheckSize.Width + 8) : 8);
		int num5 = Math.Max(1, control.ClientSize.Width - control.Padding.Horizontal - num4);
		int num6 = Math.Max(1, control.ClientSize.Height - control.Padding.Vertical - 4);
		bool flag2 = control.Text.Any((char ch) => ch >= '⺀' && ch <= '鿿');
		TextFormatFlags textFormatFlags = (TextFormatFlags)(0x800 | ((!flag2) ? 268435456 : 0));
		textFormatFlags = ((!(control is Label { AutoSize: false }) || flag2) ? (textFormatFlags | TextFormatFlags.SingleLine) : (textFormatFlags | TextFormatFlags.WordBreak));
		Size proposedSize = (flag2 ? new Size(int.MaxValue, int.MaxValue) : new Size(num5, int.MaxValue));
		Size value2 = TextRenderer.MeasureText(control.Text, control.Font, proposedSize, textFormatFlags);
		int num7 = ((!flag2) ? 2 : 0);
		if (value2.Width > num5 + num7 || value2.Height > num6 + num7)
		{
			return $"Text '{control.Text.Replace(Environment.NewLine, " / ")}' needs {value2}, available {num5}x{num6}.";
		}
		return null;
	}

	private static void AuditBrightnessIsolation(Viewport viewport, SettingsForm form, List<AuditIssue> issues)
	{
		form.SelectDashboardPageForAudit(0);
		Control control = form.Controls.Find("panelPerformance", searchAllChildren: true).FirstOrDefault();
		Control control2 = form.Controls.Find("panelGPU", searchAllChildren: true).FirstOrDefault();
		TrackBar trackBar = form.Controls.Find("sliderScreenBrightness", searchAllChildren: true).FirstOrDefault() as TrackBar;
		if (control != null && control2 != null && trackBar != null)
		{
			Application.DoEvents();
			form.Update();
			control.Update();
			control2.Update();
			byte[] array = RenderSignature(control);
			byte[] array2 = RenderSignature(control2);
			int value = trackBar.Value;
			int[] array3 = new int[5] { 20, 80, 35, 70, 0 };
			array3[4] = value;
			int[] array4 = array3;
			foreach (int value2 in array4)
			{
				trackBar.Value = Math.Clamp(value2, trackBar.Minimum, trackBar.Maximum);
				Application.DoEvents();
			}
			if (!array.SequenceEqual(RenderSignature(control)))
			{
				issues.Add(new AuditIssue("Settings-Common", viewport.Id, "cross-control-repaint", ControlPath(control), "Performance controls changed while only brightness was adjusted."));
			}
			if (!array2.SequenceEqual(RenderSignature(control2)))
			{
				issues.Add(new AuditIssue("Settings-Common", viewport.Id, "cross-control-repaint", ControlPath(control2), "GPU controls changed while only brightness was adjusted."));
			}
		}
	}

	private static byte[] RenderSignature(Control control)
	{
		using Bitmap bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height), PixelFormat.Format32bppArgb);
		control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return SHA256.HashData(memoryStream.GetBuffer().AsSpan(0, checked((int)memoryStream.Length)));
	}

	private static IEnumerable<string> CapturePages(string outputDirectory, string formName, Viewport viewport, Form form)
	{
		ScrollableControl scrollable = Flatten(form).OfType<ScrollableControl>().FirstOrDefault((ScrollableControl control) => control.Visible && control.AutoScroll && control.DisplayRectangle.Height > control.ClientSize.Height + 4);
		// ======================================================================================
		// 2026-10-10 ui-audit-intel：**可选**的"整页一张图"（`LMECHREVO_UI_AUDIT_FULLPAGE=1`）。
		// 加它的原因：内容比窗口高的窗体本来只出 top/middle/bottom 三张（同一块内容会重叠），
		// 人工核对"有没有空白带 / 错位 / 该显示的没显示"时不如**一张整页图**直观。
		// 做法：把窗体临时撑到"内容全高"→ 截一张 → 立刻还原尺寸；⛔ 默认（不设该环境变量）行为与
		// 改前逐字一致（仍只出 top/middle/bottom）。⚠️ 撑高只发生在**审计判定之后**（AuditForm 已跑完），
		// 所以不会影响任何 overflow / empty-band 结论。
		// ======================================================================================
		if (scrollable != null && string.Equals(Environment.GetEnvironmentVariable("LMECHREVO_UI_AUDIT_FULLPAGE"), "1", StringComparison.Ordinal))
		{
			int delta = Math.Max(0, scrollable.DisplayRectangle.Height - scrollable.ClientSize.Height);
			Size original = form.Size;
			form.Size = new Size(form.Width, form.Height + delta);
			form.PerformLayout();
			scrollable.PerformLayout();
			// 2026-10-10 flat-rows：整页图**必须先拉回顶部** —— 去掉选项下拉之后，"第一个可获得焦点的
			// 控件"从页首的下拉变成了页面中部的数值框，WinForms 在 Show 时会把面板滚到那条去
			// （整页图因此丢了最上面一行，AMD 形态实测）。⛔ 只影响这张截图的取景：
			// `AuditForm` 的几何判定在这之前就已经跑完了。
			scrollable.AutoScrollPosition = new Point(0, 0);
			Application.DoEvents();
			try
			{
				using Bitmap full = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height), PixelFormat.Format32bppArgb);
				form.DrawToBitmap(full, new Rectangle(Point.Empty, full.Size));
				string text = $"{formName}-{viewport.Id}-fullpage.png";
				full.Save(Path.Combine(outputDirectory, text), ImageFormat.Png);
				yield return text;
			}
			finally
			{
				form.Size = original;
				form.PerformLayout();
				Application.DoEvents();
			}
		}
		(string, int)[] source = ((scrollable != null) ? new (string, int)[3]
		{
			("top", 0),
			("middle", Math.Max(0, (scrollable.DisplayRectangle.Height - scrollable.ClientSize.Height) / 2)),
			("bottom", Math.Max(0, scrollable.DisplayRectangle.Height - scrollable.ClientSize.Height))
		} : new (string, int)[1] { ("full", 0) });
		foreach (var item in source.Distinct())
		{
			if (scrollable != null)
			{
				form.ActiveControl = null;
				scrollable.AutoScrollPosition = new Point(0, item.Item2);
				scrollable.PerformLayout();
				Application.DoEvents();
			}
			int width = Math.Max(1, form.Width);
			int height = Math.Max(1, form.Height);
			using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
			string text = $"{formName}-{viewport.Id}-{item.Item1}.png";
			string filename = Path.Combine(outputDirectory, text);
			bitmap.Save(filename, ImageFormat.Png);
			yield return text;
		}
	}

	private static IEnumerable<Control> Flatten(Control root)
	{
		yield return root;
		foreach (Control control in root.Controls)
		{
			foreach (Control item in Flatten(control))
			{
				yield return item;
			}
		}
	}

	private static string DisplayName(Control control)
	{
		if (!string.IsNullOrWhiteSpace(control.Name))
		{
			return control.Name;
		}
		return control.GetType().Name;
	}

	internal static List<(string Kind, string Control, string Detail)> CheckClipping(Form form)
	{
		List<(string, string, string)> list = new List<(string, string, string)>();
		foreach (Control item3 in from Control control in form.Controls
			where control.Visible && (control.Dock == DockStyle.Top || control.Dock == DockStyle.Fill)
			select control)
		{
			(int Width, int Height) tuple = RequiredContentSize(item3);
			int item = tuple.Width;
			int item2 = tuple.Height;
			bool num = item2 > form.ClientSize.Height + 2 && !HasUsableVerticalScroll(form, item3);
			bool flag = item > form.ClientSize.Width + 2 && !HasUsableHorizontalScroll(form, item3);
			if (num || flag)
			{
				list.Add(("docked-root-overflow", ControlPath(item3), $"{DisplayName(item3)} requires {item}x{item2}, client {form.ClientSize.Width}x{form.ClientSize.Height} without a usable scrollbar."));
			}
		}
		foreach (TableLayoutPanel item4 in from control in Flatten(form)
			where control.Visible && control is TableLayoutPanel tableLayoutPanel2 && control.Width > 0 && tableLayoutPanel2.ClientSize.Width > 0
			select control)
		{
			int num2 = 0;
			foreach (ColumnStyle item5 in (IEnumerable)item4.ColumnStyles)
			{
				if (item5.SizeType == SizeType.Absolute)
				{
					num2 += (int)Math.Ceiling(item5.Width);
				}
			}
			int num3 = item4.ClientSize.Width - item4.Padding.Horizontal;
			if (num3 > 0 && num2 > num3)
			{
				list.Add(("absolute-column-overflow", ControlPath(item4), $"Absolute columns sum {num2}px > inner width {num3}px."));
			}
		}
		return list;
	}

	private static (int Width, int Height) RequiredContentSize(Control root)
	{
		if (!(root is TableLayoutPanel { Padding: { Horizontal: var num }, Padding: { Vertical: var num2 } } tableLayoutPanel))
		{
			return (Width: root.PreferredSize.Width, Height: root.PreferredSize.Height);
		}
		int[] columnWidths = tableLayoutPanel.GetColumnWidths();
		int[] rowHeights = tableLayoutPanel.GetRowHeights();
		for (int i = 0; i < columnWidths.Length; i++)
		{
			if (i >= tableLayoutPanel.ColumnStyles.Count || tableLayoutPanel.ColumnStyles[i].SizeType != SizeType.Percent)
			{
				num += columnWidths[i];
			}
		}
		for (int j = 0; j < rowHeights.Length; j++)
		{
			if (j >= tableLayoutPanel.RowStyles.Count || tableLayoutPanel.RowStyles[j].SizeType != SizeType.Percent)
			{
				num2 += rowHeights[j];
			}
		}
		if (tableLayoutPanel.ColumnStyles.Count == 0)
		{
			num = root.PreferredSize.Width;
		}
		if (tableLayoutPanel.RowStyles.Count == 0)
		{
			num2 = root.PreferredSize.Height;
		}
		return (Width: num, Height: num2);
	}

	private static bool HasUsableVerticalScroll(Form form, Control root)
	{
		if (form.VerticalScroll.Visible)
		{
			return true;
		}
		if (root is ScrollableControl { AutoScroll: not false } scrollableControl && scrollableControl.VerticalScroll.Visible)
		{
			return true;
		}
		return Flatten(form).OfType<RScrollBar>().Any((RScrollBar bar) => bar.Visible);
	}

	private static bool HasUsableHorizontalScroll(Form form, Control root)
	{
		if (form.HorizontalScroll.Visible)
		{
			return true;
		}
		if (root is ScrollableControl { AutoScroll: not false } scrollableControl)
		{
			return scrollableControl.HorizontalScroll.Visible;
		}
		return false;
	}

	private static string ControlPath(Control control)
	{
		Stack<string> stack = new Stack<string>();
		for (Control control2 = control; control2 != null; control2 = control2.Parent)
		{
			stack.Push(DisplayName(control2));
		}
		return string.Join("/", stack);
	}

	private static void WriteMarkdown(string outputDirectory, Viewport[] viewports, IReadOnlyCollection<string> screenshots, IReadOnlyCollection<AuditIssue> issues, IReadOnlyCollection<string> rowGaps, IReadOnlyCollection<string> checkLog)
	{
		StringBuilder stringBuilder = new StringBuilder().AppendLine("# L-Mechrevo UI audit").AppendLine();
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
		handler.AppendLiteral("- Screenshots: ");
		handler.AppendFormatted(screenshots.Count);
		StringBuilder stringBuilder2 = stringBuilder.AppendLine(ref handler);
		StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler2.AppendLiteral("- Geometry issues: ");
		handler2.AppendFormatted(issues.Count);
		StringBuilder stringBuilder3 = stringBuilder2.AppendLine(ref handler2);
		StringBuilder stringBuilder4 = stringBuilder3;
		StringBuilder.AppendInterpolatedStringHandler handler3 = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder3);
		handler3.AppendLiteral("- Matrix: ");
		handler3.AppendFormatted(string.Join(", ", viewports.Select((Viewport viewport) => viewport.Id)));
		StringBuilder stringBuilder5 = stringBuilder4.AppendLine(ref handler3).AppendLine();
		foreach (AuditIssue item in issues.Take(500))
		{
			stringBuilder3 = stringBuilder5;
			StringBuilder stringBuilder6 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 5, stringBuilder3);
			handler.AppendLiteral("- [");
			handler.AppendFormatted(item.Kind);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(item.Form);
			handler.AppendLiteral("/");
			handler.AppendFormatted(item.Viewport);
			handler.AppendLiteral(" `");
			handler.AppendFormatted(item.Control);
			handler.AppendLiteral("`: ");
			handler.AppendFormatted(item.Detail);
			stringBuilder6.AppendLine(ref handler);
		}
		stringBuilder5.AppendLine();
		stringBuilder5.AppendLine("## Checks (per form/viewport)");
		stringBuilder5.AppendLine();
		foreach (string item2 in checkLog)
		{
			stringBuilder3 = stringBuilder5;
			StringBuilder stringBuilder7 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder3);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(item2);
			stringBuilder7.AppendLine(ref handler);
		}
		stringBuilder5.AppendLine();
		stringBuilder5.AppendLine("## Row gaps (diagnostic)");
		stringBuilder5.AppendLine();
		foreach (string item3 in rowGaps.Take(2000))
		{
			stringBuilder3 = stringBuilder5;
			StringBuilder stringBuilder8 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder3);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(item3);
			stringBuilder8.AppendLine(ref handler);
		}
		File.WriteAllText(Path.Combine(outputDirectory, "ui-audit.md"), stringBuilder5.ToString(), Encoding.UTF8);
	}

	private static IEnumerable<Control> AllControls(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control item in AllControls(child))
			{
				yield return item;
			}
		}
	}
}
