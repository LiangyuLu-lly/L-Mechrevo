using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Diagnostics;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using MechrevoLite.Usage;

namespace MechrevoLite;

public sealed class SettingsDialog : RForm
{
	private Control? _displayHeader;

	private Control? _displayRow;

	/// <summary>
	/// 2026-10-09（lightmerge）：设置里「额外灯光项」的三选行；2026-10-09（lightsplit）起细分成**两行**
	/// ——「灯带行显示」（light_strip_ui）与「Logo 灯行显示」（light_logo_ui），两行的可见性都由
	/// <see cref="SetLightExtraRowAvailable"/> 一起下发（恒为 true，见 SettingsForm.LightExtraRowAvailable）。
	/// </summary>
	private Control? _lightExtraRow;

	/// <summary>2026-10-09（lightsplit）：第二行「Logo 灯行显示」三选行（与上面那一行同构、同一套可见性）。</summary>
	private Control? _lightLogoRow;

	private Form? _owner;

	private bool _syncingLanguage;

	private RCheckBox? _telemetryCheck;

	private bool _syncingTelemetry;

	private RComboBox? _logLevelCombo;

	private bool _syncingLogLevel;

	private Label? _logLevelOverrideLabel;

	/// <summary>2026-10-06：环境变量覆盖提示旁边的「清除环境变量覆盖」按钮。</summary>
	private RButton? _logLevelClearOverrideButton;

	/// <summary>2026-10-06：当前进程里 LMECHREVO_LOG_LEVEL 是否真的覆盖着配置（决定提示行与「清除…」按钮是否显示）。</summary>
	private bool _logLevelOverrideActive;

	private readonly ToolTip _toolTip = new ToolTip
	{
		AutoPopDelay = 20000,
		InitialDelay = 400,
		ReshowDelay = 200
	};

	/// <summary>
	/// 2026-10-09（settingswarm）：本窗的**几何签名** —— 只有它变了才值得做整棵树重排。
	/// <para>背景：`ShowAdjacentTo` 里那次 <see cref="ResponsiveLayout.PerformLayoutTree" /> 在本机实测
	/// 每次 1.37~1.45 秒（点开设置的第二、三次也一样），而它要防的是"嵌套 AutoSize/Dock 组合下
	/// .NET 增量布局排不准"；只要**几何输入没变**，上一次的全树结果就仍然有效 ⇒ 这次重排是纯重复劳动。</para>
	/// <para>签名只吃"会改变几何"的输入：客户区尺寸、布局缩放（DPI）、根表的行数/子控件数、
	/// 三处会改行可见性的状态（显示组可用 / 两行灯光 / 日志级别环境变量提示）。⚠️ 主题（日/夜）只改颜色，
	/// 不进签名；语言切换是重启进程，也不进。任何一处状态**真的变化**时由 <see cref="InvalidateLayoutSignature" />
	/// 置脏 ⇒ 下一次打开照旧全树重排（与改前行为一致）。</para>
	/// </summary>
	private string _layoutSignature = "";

	private bool _layoutSignatureDirty = true;

	private bool _displayGroupAvailable;

	private bool _lightExtraRowsAvailable;

	/// <summary>2026-10-09（settingswarm）：预热那一趟"隐形 Show"期间不抢焦点（SW_SHOWNA）。
	/// 真实点击打开时本属性为 false ⇒ 照旧激活、照旧抢焦点，行为一字未改。</summary>
	private bool _preWarmNoActivate;

	protected override bool ShowWithoutActivation => _preWarmNoActivate || base.ShowWithoutActivation;

	/// <summary>2026-10-09（settingswarm）：开始"隐形 Show"（1% 不透明 + 不激活）。</summary>
	internal void BeginPreWarmShow()
	{
		_preWarmNoActivate = true;
		Opacity = 0.01;
	}

	/// <summary>2026-10-09（settingswarm）：结束"隐形 Show" —— 必定藏回去、把 Opacity 恢复成 1.0
	/// （⚠️ 漏恢复会让用户点开设置时看到一个几乎全透明的窗口）。</summary>
	internal void EndPreWarmShow()
	{
		try
		{
			Hide();
		}
		finally
		{
			Opacity = 1.0;
			_preWarmNoActivate = false;
		}
	}

	internal static Action<string>? LanguageChangedOverride { get; set; }

	public SettingsDialog(Control themePanel, Control officialPanel, Control? overdriveChk, bool displayGroupAvailable, Control? followBox, Control? startupCheck = null, Control? igpuOnlyPanel = null, Control? lightExtraPanel = null, Control? lightLogoPanel = null)
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.Settings;
		// 2026-10-12 r6：标题栏左上角 = 底栏「设置」按钮那枚字形（Kind.Gear，⛔ 不另画）。
		UiVisualStyle.ApplyWindowGlyph(this, UiGlyph.Kind.Gear);
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		// 2026-10-13（用户原话「都改掉不要最小化」）：只留【关闭】—— 藏掉最小化按钮。
		// 本窗是 SettingsForm 的 AddOwnedForm 子窗（SettingsForm.cs:15483），"再打开"以 Visible 为闸
		// （:15494/:15496）；而它自己的 FormClosing（本文件 :124-131）对 UserClosing 是 Cancel+Hide
		// ⇒ 最小化时 Visible 仍为 true，点一次只会 Hide，没人把 WindowState 置回 Normal = "唤不回来"。
		// MinimizeBox=false 会连 WS_MINIMIZEBOX 一起去掉（实测样式 0x26CA0000 里的 0x00020000）。
		// ⚠️ FormBorderStyle 保持 FixedSingle：用户只要求藏按钮，没要求改边框样式。
		MinimizeBox = false;
		StartPosition = FormStartPosition.Manual;
		AutoScaleMode = AutoScaleMode.None;
		ClientSize = new Size(ResponsiveLayout.LogicalToDevice(this, 470), ResponsiveLayout.LogicalToDevice(this, 340));
		AutoScroll = true;
		InitTheme(setDPI: true);
		FormClosing += (object? _, FormClosingEventArgs e) =>
		{
			if (e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				Hide();
			}
		};
		BufferedTableLayoutPanel root = new BufferedTableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 1,
			RowCount = 12,
			Padding = new Padding(D(16), D(12), D(16), D(12)),
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		Controls.Add(root);
		int r = 0;
					// 界面标题行（含跟随系统开关）
			BufferedTableLayoutPanel themeHeader = new BufferedTableLayoutPanel
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Top,
				Margin = new Padding(0, 0, 0, D(4)),
				BackColor = UiVisualStyle.Window
			};
			themeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			themeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			themeHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Label themeTitle = new Label
			{
				Text = Strings.UiSection,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Margin = new Padding(0, 0, D(8), 0)
			};
			themeHeader.Controls.Add(themeTitle, 0, 0);
			if (followBox != null)
			{
				followBox.AutoSize = true;
				followBox.Anchor = AnchorStyles.Left;
				followBox.Margin = Padding.Empty;
				if (followBox is CheckBox followCheck)
				{
					followCheck.BackColor = UiVisualStyle.Window;
				}
				themeHeader.Controls.Add(followBox, 1, 0);
			}
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.Controls.Add(themeHeader, 0, r++);
		themePanel.Dock = DockStyle.Top;
		themePanel.Visible = true;
		officialPanel.Visible = true;
		AddRow(themePanel);
		AddRow(BuildLanguageRow(D));
		if (overdriveChk != null)
		{
			_displayHeader = AddHeader(Strings.DisplaySection, 10);
			overdriveChk.Dock = DockStyle.Left;
			AddRow(overdriveChk);
			_displayRow = overdriveChk;
			SetDisplayGroupAvailable(displayGroupAvailable);
		}
		// 系统标题行（含开机自启动 + 强制启用水冷菜单开关，标题右侧同一行：开机自启在左、水冷在右）
		BufferedTableLayoutPanel systemHeader = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(10), 0, D(4)),
			BackColor = UiVisualStyle.Window
		};
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		systemHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label systemTitle = new Label
		{
			Text = Strings.SystemSection,
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, D(8), 0)
		};
		systemHeader.Controls.Add(systemTitle, 0, 0);
		BufferedTableLayoutPanel systemSwitchGroup = new BufferedTableLayoutPanel
		{
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Right,
			BackColor = UiVisualStyle.Window
		};
		systemSwitchGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemSwitchGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		systemSwitchGroup.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		if (startupCheck != null)
		{
			startupCheck.Visible = true;
			startupCheck.BackColor = UiVisualStyle.Window;
			startupCheck.Dock = DockStyle.None;
			startupCheck.Anchor = AnchorStyles.Left;
			startupCheck.Margin = Padding.Empty;
			systemSwitchGroup.Controls.Add(startupCheck, 0, 0);
		}
		RCheckBox waterCoolingCheck = new RCheckBox
		{
			Name = "checkForceWaterCooling",
			Text = Strings.ForceWaterCoolingMenu,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(D(8), 0, 0, 0),
			AccessibleDescription = Strings.ForceWaterCoolingMenuTip
		};
		waterCoolingCheck.Checked = AppConfig.Is("force_liquid_cooling_menu");
		waterCoolingCheck.CheckedChanged += (object? _, EventArgs _) =>
		{
			AppConfig.Set("force_liquid_cooling_menu", waterCoolingCheck.Checked ? 1 : 0);
			AppConfig.Flush();
			if (_owner is SettingsForm settingsForm)
			{
				settingsForm.RefreshDeviceCapabilities();
			}
		};
		_toolTip.SetToolTip(waterCoolingCheck, Strings.ForceWaterCoolingMenuTip);
		systemSwitchGroup.Controls.Add(waterCoolingCheck, 1, 0);
		systemHeader.Controls.Add(systemSwitchGroup, 1, 0);
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(systemHeader, 0, r++);
		officialPanel.Dock = DockStyle.Top;
		AddRow(officialPanel);
		if (igpuOnlyPanel != null)
		{
			// 2026-10-05「核显模式显示策略」③：「显示核显模式」开关
			igpuOnlyPanel.Dock = DockStyle.Top;
			igpuOnlyPanel.Visible = true;
			AddRow(igpuOnlyPanel);
		}
		if (lightExtraPanel != null)
		{
			// 2026-10-09（lightmerge / lightsplit）：灯光区「额外灯光项」按行三选行 ——
			// 「灯带行显示」（light_strip_ui）在前、「Logo 灯行显示」（light_logo_ui）紧随其后；
			// 与上一行（核显模式显示）同一个工厂、同一尺寸/字号/间距，风格一致。
			// 可见性由 SettingsForm.LightExtraRowAvailable() 动态下发（见 SetLightExtraRowAvailable）。
			_lightExtraRow = lightExtraPanel;
			lightExtraPanel.Dock = DockStyle.Top;
			lightExtraPanel.Visible = true;
			AddRow(lightExtraPanel);
		}
		if (lightLogoPanel != null)
		{
			_lightLogoRow = lightLogoPanel;
			lightLogoPanel.Dock = DockStyle.Top;
			lightLogoPanel.Visible = true;
			AddRow(lightLogoPanel);
		}
		// 2026-10-06：新增「日志级别」设置项（关/仅错误/全部），落盘 log_level，下一次写日志即生效。
		AddRow(BuildLogLevelRow(D));
		AddHeader(Strings.SettingsZonePrivacy, 10);
		AddRow(BuildPrivacyRow(D));
		// 2026-10-09（exportperm）：设置的一键导出/导入（自定义档、风扇曲线、灯效等用户设置）。
		AddHeader(Strings.SettingsZoneBackup, 10);
		AddRow(BuildSettingsBackupRow(D));
		Label AddHeader(string text, int topMargin)
		{
			Label label = new Label
			{
				Text = text,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				Margin = new Padding(0, D(topMargin), 0, D(4))
			};
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.Controls.Add(label, 0, r++);
			return label;
		}
		void AddRow(Control control)
		{
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.Controls.Add(control, 0, r++);
		}
		int D(int v)
		{
			return ResponsiveLayout.LogicalToDevice(this, v);
		}
	}

	private TableLayoutPanel BuildPrivacyRow(Func<int, int> D)
	{
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Name = "panelPrivacy",
			ColumnCount = 1,
			RowCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = Padding.Empty,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Percent, 100f)
			}
		};
		_telemetryCheck = new RCheckBox
		{
			Name = "checkUsageTelemetry",
			Text = Strings.UsageTelemetryToggle,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(0, D(2), 0, D(4)),
			AccessibleDescription = Strings.UsageTelemetryTip
		};
		SyncTelemetryCheck();
		_telemetryCheck.CheckedChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingTelemetry && _telemetryCheck != null)
			{
				UsageTelemetry.SetEnabled(_telemetryCheck.Checked);
			}
		};
		_toolTip.SetToolTip(_telemetryCheck, Strings.UsageTelemetryTip);
		VisibleChanged += (object? _, EventArgs _) =>
		{
			if (Visible)
			{
				SyncTelemetryCheck();
			}
		};
		RButton rButton = new RButton
		{
			Name = "buttonFeedback",
			Text = Strings.FeedbackOpen,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, D(2), 0, D(2)),
			Padding = new Padding(D(10), D(2), D(10), D(2)),
			Cursor = Cursors.Hand,
			BorderRadius = 4,
			Secondary = true
		};
		UiVisualStyle.ApplySecondaryButton(rButton);
		rButton.BorderColor = UiVisualStyle.Border;
		rButton.Click += (object? _, EventArgs _) =>
		{
			using FeedbackForm feedbackForm = new FeedbackForm();
			feedbackForm.ShowDialog(this);
		};
		bufferedTableLayoutPanel.Controls.Add(_telemetryCheck, 0, 0);
		bufferedTableLayoutPanel.Controls.Add(rButton, 0, 1);
		return bufferedTableLayoutPanel;
	}

	/// <summary>
	/// 2026-10-09（exportperm）：设置里的「设置备份」一行 —— 一键把用户设置（自定义性能档/风扇曲线、
	/// 灯效 cfg、两处 config.json、lhm_sensors.txt 等）打成一个 zip；导入前会把现有文件自动备份到
	/// %APPDATA%\MechrevoLite\userdata-backup-&lt;时间戳&gt;\，导入后提示"重启 LM 生效"。
	/// 只含用户设置，日志（log.txt / crash.txt）一律不打包。
	/// 按钮沿用 UiVisualStyle.CreateCompactButton（尺寸规范同设置窗既有的紧凑按钮）。
	/// </summary>
	private TableLayoutPanel BuildSettingsBackupRow(Func<int, int> D)
	{
		BufferedTableLayoutPanel panel = new BufferedTableLayoutPanel
		{
			Name = "panelSettingsBackup",
			ColumnCount = 1,
			RowCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = Padding.Empty,
			ColumnStyles =
			{
				new ColumnStyle(SizeType.Percent, 100f)
			},
			RowStyles =
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize)
			}
		};
		FlowLayoutPanel choices = new FlowLayoutPanel
		{
			Name = "flowSettingsBackup",
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		RButton exportButton = UiVisualStyle.CreateCompactButton("buttonSettingsBackupExport", Strings.SettingsBackupExportButton, D, new Padding(0, D(2), D(6), D(2)));
		RButton importButton = UiVisualStyle.CreateCompactButton("buttonSettingsBackupImport", Strings.SettingsBackupImportButton, D, new Padding(0, D(2), 0, D(2)));
		_toolTip.SetToolTip(exportButton, Strings.SettingsBackupExportTip);
		_toolTip.SetToolTip(importButton, Strings.SettingsBackupImportTip);
		exportButton.Click += (object? _, EventArgs _) => ExportSettingsBackup(exportButton);
		importButton.Click += (object? _, EventArgs _) => ImportSettingsBackup(importButton);
		choices.Controls.Add(exportButton);
		choices.Controls.Add(importButton);
		Label hint = new Label
		{
			Name = "labelSettingsBackupHint",
			Text = Strings.SettingsBackupHint,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiVisualStyle.Muted,
			Font = UiVisualStyle.Font(8.5f),
			Margin = new Padding(0, D(2), 0, 0),
			BackColor = UiVisualStyle.Window
		};
		panel.Controls.Add(choices, 0, 0);
		panel.Controls.Add(hint, 0, 1);
		return panel;
	}

	/// <summary>导出：默认落在桌面（可改），文件名 LM-设置备份-&lt;yyyyMMdd-HHmmss&gt;.zip；失败如实报错，不静默。</summary>
	private void ExportSettingsBackup(Control trigger)
	{
		string zipPath;
		using (SaveFileDialog dialog = new SaveFileDialog
		{
			Title = Strings.SettingsBackupExportButton,
			Filter = "Zip 压缩包 (*.zip)|*.zip",
			DefaultExt = "zip",
			AddExtension = true,
			OverwritePrompt = true,
			RestoreDirectory = true,
			FileName = SettingsBackup.BuildFileName(DateTime.Now),
			InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
		})
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			zipPath = dialog.FileName;
		}
		trigger.Enabled = false;
		try
		{
			SettingsBackupExportResult result = SettingsBackup.Export(zipPath, DateTime.Now);
			Logger.WriteLine($"Settings backup exported: {result.ZipPath} ({result.SizeBytes} bytes, {result.Included.Count} files, skipped={result.Skipped.Count})");
			string message = string.Format(Strings.SettingsBackupExportDone, result.ZipPath, result.Included.Count, FormatBackupSize(result.SizeBytes));
			if (result.Skipped.Count > 0)
			{
				message = message + Environment.NewLine + Environment.NewLine + Strings.SettingsBackupExportSkipped + Environment.NewLine + string.Join(Environment.NewLine, result.Skipped);
			}
			if (MessageBox.Show(this, message, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
			{
				DiagnosticPackCommand.RevealInExplorer(result.ZipPath);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Settings backup export failed: " + ex);
			MessageBox.Show(this, string.Format(Strings.SettingsBackupExportFailed, ex.Message), "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			if (!trigger.IsDisposed)
			{
				trigger.Enabled = true;
			}
		}
	}

	/// <summary>
	/// 导入：选 zip → 校验（非法即拒绝并说明）→ 备份现有设置到 userdata-backup-&lt;时间戳&gt;\ → 覆盖两处 → 提示重启生效。
	/// </summary>
	private void ImportSettingsBackup(Control trigger)
	{
		string zipPath;
		using (OpenFileDialog dialog = new OpenFileDialog
		{
			Title = Strings.SettingsBackupImportButton,
			Filter = "L-Mechrevo 设置备份 (*.zip)|*.zip|所有文件 (*.*)|*.*",
			CheckFileExists = true,
			RestoreDirectory = true,
			InitialDirectory = SettingsBackup.ResolveDefaultDirectory()
		})
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			zipPath = dialog.FileName;
		}
		if (MessageBox.Show(this, Strings.SettingsBackupImportConfirm, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
		{
			return;
		}
		trigger.Enabled = false;
		try
		{
			SettingsBackupImportResult result = SettingsBackup.Import(zipPath, DateTime.Now);
			Logger.WriteLine($"Settings backup imported: {result.Restored.Count} restored, {result.Failed.Count} failed, pre-import backup={result.BackupDirectory}");
			string message = string.Format(Strings.SettingsBackupImportDone, result.Restored.Count, result.BackupDirectory);
			MessageBoxIcon icon = MessageBoxIcon.Asterisk;
			if (result.Failed.Count > 0)
			{
				message = message + Environment.NewLine + Environment.NewLine + Strings.SettingsBackupImportPartialFailed + Environment.NewLine + string.Join(Environment.NewLine, result.Failed);
				icon = MessageBoxIcon.Warning;
			}
			MessageBox.Show(this, message, "L-Mechrevo", MessageBoxButtons.OK, icon);
		}
		catch (InvalidDataException ex)
		{
			Logger.WriteLine("Settings backup import rejected: " + ex.Message);
			MessageBox.Show(this, string.Format(Strings.SettingsBackupImportInvalid, ex.Message), "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Settings backup import failed: " + ex2);
			MessageBox.Show(this, string.Format(Strings.SettingsBackupImportFailed, ex2.Message), "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			if (!trigger.IsDisposed)
			{
				trigger.Enabled = true;
			}
		}
	}

	private static string FormatBackupSize(long bytes)
	{
		if (bytes < 1024L)
		{
			return bytes.ToString(CultureInfo.InvariantCulture) + " B";
		}
		if (bytes < 1048576L)
		{
			return ((double)bytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture) + " KB";
		}
		return ((double)bytes / 1024.0 / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
	}

	private void SyncTelemetryCheck()
	{
		if (_telemetryCheck == null)
		{
			return;
		}
		_syncingTelemetry = true;
		try
		{
			_telemetryCheck.Checked = UsageTelemetry.Enabled;
		}
		finally
		{
			_syncingTelemetry = false;
		}
	}

	private TableLayoutPanel BuildLanguageRow(Func<int, int> D)
	{
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Name = "panelLanguage",
			ColumnCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(8), 0, 0)
		};
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		Label control = new Label
		{
			Name = "labelLanguage",
			Text = Strings.Language,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiVisualStyle.Text,
			Font = UiVisualStyle.Font(9f),
			Margin = new Padding(0, D(6), D(8), 0)
		};
		RComboBox combo = new RComboBox
		{
			Name = "comboLanguage",
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = D(140),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left),
			Margin = new Padding(0, D(2), 0, D(2))
		};
		combo.SizeChanged += (object? _, EventArgs _) =>
		{
			bufferedTableLayoutPanel.MinimumSize = new Size(0, combo.Height + combo.Margin.Vertical);
		};
		combo.Items.Add("中文");
		combo.Items.Add("English");
		string code = UiLanguage.Normalize(AppConfig.GetString("language"), Thread.CurrentThread.CurrentUICulture.Name);
		_syncingLanguage = true;
		combo.SelectedIndex = UiLanguage.IndexOf(code);
		_syncingLanguage = false;
		combo.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			OnLanguageSelected(combo);
		};
		bufferedTableLayoutPanel.Controls.Add(control, 0, 0);
		bufferedTableLayoutPanel.Controls.Add(combo, 1, 0);
		return bufferedTableLayoutPanel;
	}

	private void OnLanguageSelected(ComboBox combo)
	{
		if (_syncingLanguage)
		{
			return;
		}
		string text = UiLanguage.CodeFromIndex(combo.SelectedIndex);
		if (!string.Equals(AppConfig.GetString("language"), text, StringComparison.OrdinalIgnoreCase))
		{
			AppConfig.Set("language", text);
			AppConfig.Flush();
			SyncOemRegistryLanguage(text);
			_ = PublishGcuLanguageAsync(text);
			Action<string> languageChangedOverride = LanguageChangedOverride;
			if (languageChangedOverride != null)
			{
				languageChangedOverride(text);
			}
			else if (MessageBox.Show(this, Strings.LanguageRestartPrompt, Strings.LanguageRestartTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--restart") { UseShellExecute = true });
				Environment.Exit(0);
			}
		}
	}

	/// <summary>
	/// 2026-10-06：新增「日志级别」设置项（关闭 / 仅错误 / 全部），与 Logger.LogLevel 取值对齐，
	/// 落盘到既有配置键 log_level。Logger.CurrentLevel 每次写日志时现读配置，所以改动即时生效、不用重启。
	/// 优先级保持 Logger 原有语义：环境变量 LMECHREVO_LOG_LEVEL &gt; 配置 log_level &gt; 默认(Off)；
	/// 环境变量存在时界面改动会被覆盖，因此该行下方显式提示（见 LogLevelEnvOverride）。
	/// </summary>
	private TableLayoutPanel BuildLogLevelRow(Func<int, int> D)
	{
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Name = "panelLogLevel",
			ColumnCount = 2,
			RowCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Top,
			Margin = new Padding(0, D(8), 0, 0)
		};
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label control = new Label
		{
			Name = "labelLogLevel",
			Text = Strings.LogLevel,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiVisualStyle.Text,
			Font = UiVisualStyle.Font(9f),
			Margin = new Padding(0, D(6), D(8), 0)
		};
		RComboBox combo = new RComboBox
		{
			Name = "comboLogLevel",
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = D(140),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left),
			Margin = new Padding(0, D(2), 0, D(2))
		};
		combo.Items.Add(Strings.LogLevelOff);
		combo.Items.Add(Strings.LogLevelErrorOnly);
		combo.Items.Add(Strings.LogLevelAll);
		_syncingLogLevel = true;
		combo.SelectedIndex = ConfiguredLogLevelIndex();
		_syncingLogLevel = false;
		combo.SelectedIndexChanged += (object? _, EventArgs _) => OnLogLevelSelected(combo);
		_toolTip.SetToolTip(combo, Strings.LogLevelTip);
		Label overrideLabel = new Label
		{
			Name = "labelLogLevelOverride",
			Text = Strings.LogLevelEnvOverride,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiVisualStyle.Muted,
			Font = UiVisualStyle.Font(8.5f),
			Margin = new Padding(0, D(2), 0, 0),
			Visible = IsLogLevelOverriddenByEnvironment()
		};
		// 2026-10-06：环境变量覆盖提示旁边的「清除环境变量覆盖」动作 ——
		// 只删**用户级** LMECHREVO_LOG_LEVEL（机器级绝不擅自删），清后把当前下拉选择写进配置并刷新提示行。
		RButton clearOverrideButton = new RButton
		{
			Name = "buttonLogLevelClearOverride",
			Text = Strings.LogLevelClearOverrideButton,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, D(2), 0, 0),
			Padding = new Padding(D(10), D(2), D(10), D(2)),
			Cursor = Cursors.Hand,
			BorderRadius = 4,
			Secondary = true,
			TabStop = false,
			Visible = _logLevelOverrideActive
		};
		UiVisualStyle.ApplySecondaryButton(clearOverrideButton);
		clearOverrideButton.BorderColor = UiVisualStyle.Border;
		_toolTip.SetToolTip(clearOverrideButton, Strings.LogLevelClearOverrideButton);
		clearOverrideButton.Click += (object? _, EventArgs _) =>
		{
			ClearLogLevelEnvironmentOverride();
		};
		_logLevelCombo = combo;
		_logLevelOverrideLabel = overrideLabel;
		_logLevelClearOverrideButton = clearOverrideButton;
		Logger.WriteLine("Log level row: configured=" + (AppConfig.GetString("log_level") ?? "-") + " effective=" + Logger.CurrentLevel + " envOverride=" + (_logLevelOverrideActive ? "yes (LMECHREVO_LOG_LEVEL)" : "no"));
		bufferedTableLayoutPanel.Controls.Add(control, 0, 0);
		bufferedTableLayoutPanel.Controls.Add(combo, 1, 0);
		bufferedTableLayoutPanel.Controls.Add(overrideLabel, 0, 1);
		bufferedTableLayoutPanel.Controls.Add(clearOverrideButton, 1, 1);
		VisibleChanged += (object? _, EventArgs _) =>
		{
			if (Visible)
			{
				SyncLogLevelRow();
			}
		};
		return bufferedTableLayoutPanel;
	}

	/// <summary>环境变量 LMECHREVO_LOG_LEVEL 存在 = 它的优先级高于配置 log_level，界面设置不生效。</summary>
	internal static bool IsLogLevelOverriddenByEnvironment()
	{
		return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LevelOverrideVariable));
	}

	/// <summary>
	/// 覆盖来自哪一级：User（用户级，界面上可以清）/ Machine（系统级，绝不能擅自删）/
	/// Process（本次启动由外部注入，重启 LM 才消失）。进程级优先，其次用户级，最后机器级。
	/// </summary>
	private static EnvironmentVariableTarget? LogLevelOverrideScope()
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LevelOverrideVariable, EnvironmentVariableTarget.Process)))
		{
			if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LevelOverrideVariable, EnvironmentVariableTarget.User)))
			{
				if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LevelOverrideVariable, EnvironmentVariableTarget.Machine)))
				{
					return null;
				}
				return EnvironmentVariableTarget.Machine;
			}
			return EnvironmentVariableTarget.User;
		}
		return EnvironmentVariableTarget.Process;
	}

	/// <summary>
	/// 「清除环境变量覆盖」：二次确认后只删**用户级** LMECHREVO_LOG_LEVEL，
	/// 然后把当前下拉选择写进配置 log_level 并刷新提示行。
	/// 只有机器级时不动它，只提示"需手动处理"。
	/// </summary>
	private void ClearLogLevelEnvironmentOverride()
	{
		EnvironmentVariableTarget? scope = LogLevelOverrideScope();
		if (scope == EnvironmentVariableTarget.Machine)
		{
			MessageBox.Show(this, Strings.LogLevelClearOverrideMachineOnly, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Logger.WriteLine("Log level override: LMECHREVO_LOG_LEVEL is machine-scoped; not touched (user must handle it manually)");
			SyncLogLevelOverrideHint();
			return;
		}
		if (scope == null)
		{
			SyncLogLevelOverrideHint();
			return;
		}
		if (MessageBox.Show(this, Strings.LogLevelClearOverrideConfirm, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		string configured = LogLevelTokenForIndex((_logLevelCombo == null) ? ConfiguredLogLevelIndex() : _logLevelCombo.SelectedIndex);
		bool userCleared = false;
		try
		{
			if (scope == EnvironmentVariableTarget.User)
			{
				// 只删用户级；机器级一律不碰（用 explicit null 删除该作用域的值）
				Environment.SetEnvironmentVariable(Logger.LevelOverrideVariable, null, EnvironmentVariableTarget.User);
				userCleared = true;
			}
			else
			{
				Logger.WriteLine("Log level override: LMECHREVO_LOG_LEVEL is process-scoped; only the copy in this process is cleared");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failed to clear LMECHREVO_LOG_LEVEL (user scope): " + ex.Message);
		}
		if (scope == EnvironmentVariableTarget.User)
		{
			// 本进程里那份是用 SetEnvironmentVariable(name, null) 之外的方式继承来的，同步清掉，
			// 否则界面还会显示"由环境变量决定"（下次重启 LM 自然也没有了）。
			try
			{
				Environment.SetEnvironmentVariable(Logger.LevelOverrideVariable, null, EnvironmentVariableTarget.Process);
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Failed to clear inherited LMECHREVO_LOG_LEVEL (process scope): " + ex2.Message);
			}
		}
		AppConfig.Set("log_level", configured);
		AppConfig.Flush();
		SyncLogLevelOverrideHint();
		Logger.WriteLine("Log level override cleared (user-scope deleted=" + (userCleared ? "yes" : "no") + "): log_level=" + configured + " effective=" + Logger.CurrentLevel + " envOverride=" + (IsLogLevelOverriddenByEnvironment() ? "still present" : "no"));
		if (!IsLogLevelOverriddenByEnvironment())
		{
			MessageBox.Show(this, Strings.LogLevelClearOverrideDone, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}

	/// <summary>下拉项序号 → 配置值（0=关闭 / 1=仅错误 / 2=全部，与 Logger.LogLevel 同序）。</summary>
	private static string LogLevelTokenForIndex(int index)
	{
		return index switch
		{
			2 => "all", 
			1 => "error", 
			_ => "off", 
		};
	}

	/// <summary>配置里 log_level 对应的下拉项（0=关闭 / 1=仅错误 / 2=全部，与 Logger.LogLevel 同序）。</summary>
	internal static int ConfiguredLogLevelIndex()
	{
		switch (Logger.ParseLevel(AppConfig.GetString("log_level")))
		{
		case Logger.LogLevel.All:
			return 2;
		case Logger.LogLevel.ErrorOnly:
			return 1;
		default:
			return 0;
		}
	}

	private void OnLogLevelSelected(ComboBox combo)
	{
		if (_syncingLogLevel)
		{
			return;
		}
		string text = LogLevelTokenForIndex(combo.SelectedIndex);
		if (!string.Equals(AppConfig.GetString("log_level"), text, StringComparison.OrdinalIgnoreCase))
		{
			AppConfig.Set("log_level", text);
			AppConfig.Flush();
			Logger.WriteLine("Log level set from the settings dialog: " + text + " (effective=" + Logger.CurrentLevel + (IsLogLevelOverriddenByEnvironment() ? ", overridden by LMECHREVO_LOG_LEVEL" : "") + ").");
		}
		SyncLogLevelOverrideHint();
	}

	private void SyncLogLevelRow()
	{
		if (_logLevelCombo != null)
		{
			_syncingLogLevel = true;
			try
			{
				_logLevelCombo.SelectedIndex = ConfiguredLogLevelIndex();
			}
			finally
			{
				_syncingLogLevel = false;
			}
		}
		SyncLogLevelOverrideHint();
	}

	private void SyncLogLevelOverrideHint()
	{
		bool wasActive = _logLevelOverrideActive;
		_logLevelOverrideActive = IsLogLevelOverriddenByEnvironment();
		if (wasActive != _logLevelOverrideActive)
		{
			// 提示行/清除按钮的可见性变了 ⇒ 几何变了 ⇒ 下一次打开重排（老行为）
			InvalidateLayoutSignature();
		}
		if (_logLevelOverrideLabel != null)
		{
			_logLevelOverrideLabel.Visible = _logLevelOverrideActive;
		}
		if (_logLevelClearOverrideButton != null)
		{
			_logLevelClearOverrideButton.Visible = _logLevelOverrideActive;
		}
	}

	internal static void SyncOemRegistryLanguage(string langCode)
	{
		try
		{
			string regValue = langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-cn" : "en-us";
			using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey("SOFTWARE\\OEM\\GamingCenter2"))
			{
				key.SetValue("Language", regValue);
			}
			using (Microsoft.Win32.RegistryKey key2 = Microsoft.Win32.Registry.LocalMachine.CreateSubKey("SOFTWARE\\OEM\\GamingCenter2\\TpDetect"))
			{
				key2.SetValue("Language", regValue);
			}
			Logger.WriteLine("OEM registry language synced: " + regValue);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failed to sync OEM registry language: " + ex.Message);
		}
	}

    internal static async Task PublishGcuLanguageAsync(string code)
    {
        try
        {
            string gcuLang = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-cn" : "en-us";
            await Program.hw.Publish("Languages/Control", new Dictionary<string, object> { ["Action"] = "SET", ["Lang"] = gcuLang }).ConfigureAwait(continueOnCapturedContext: false);
            Logger.WriteLine("GCU language published: " + gcuLang);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Failed to publish GCU language: " + ex.Message);
        }
    }

	internal void SetDisplayGroupAvailable(bool available)
	{
		if (_displayGroupAvailable != available)
		{
			_displayGroupAvailable = available;
			InvalidateLayoutSignature();
		}
		if (_displayRow != null)
		{
			_displayRow.Visible = available;
		}
		if (_displayHeader != null)
		{
			_displayHeader.Visible = available;
		}
	}

	/// <summary>
	/// 2026-10-09（lightmerge / lightsplit）：设置里「额外灯光项」那**两行**（灯带行 / Logo 灯行）是否出现
	/// （判定见 SettingsForm.LightExtraRowAvailable —— 恒为 true）。
	/// 与 <see cref="SetDisplayGroupAvailable"/> 同一做法：可见性随时可下发，不受构造期一次性判定限制。
	/// </summary>
	internal void SetLightExtraRowAvailable(bool available)
	{
		if (_lightExtraRowsAvailable != available)
		{
			_lightExtraRowsAvailable = available;
			InvalidateLayoutSignature();
		}
		if (_lightExtraRow != null)
		{
			_lightExtraRow.Visible = available;
		}
		if (_lightLogoRow != null)
		{
			_lightLogoRow.Visible = available;
		}
	}

	/// <summary>2026-10-09（settingswarm）：把"最新几何签名"作废 —— 下一次 <see cref="ShowAdjacentTo" />
	/// 会重新做一次全树重排（＝改前的老行为）。只由"真的改了行可见性/尺寸"的地方调用。</summary>
	private void InvalidateLayoutSignature()
	{
		_layoutSignatureDirty = true;
	}

	/// <summary>2026-10-09（settingswarm）：预热路径用 —— 预热里已经排过整棵树，把签名记为"最新"。</summary>
	internal void MarkLayoutPrimed()
	{
		_layoutSignature = CurrentLayoutSignature();
		_layoutSignatureDirty = false;
	}

	private string CurrentLayoutSignature()
	{
		TableLayoutPanel? root = Controls.OfType<Control>().FirstOrDefault((Control c) => c is TableLayoutPanel) as TableLayoutPanel;
		return string.Concat(ClientSize.Width.ToString(CultureInfo.InvariantCulture), "x", ClientSize.Height.ToString(CultureInfo.InvariantCulture),
			"|", EffectiveLayoutScale.ToString("F3", CultureInfo.InvariantCulture),
			"|", (root?.Controls.Count ?? (-1)).ToString(CultureInfo.InvariantCulture), "/", (root?.RowCount ?? (-1)).ToString(CultureInfo.InvariantCulture),
			"|", _displayGroupAvailable ? "1" : "0", _lightExtraRowsAvailable ? "1" : "0", _logLevelOverrideActive ? "1" : "0");
	}

	internal void ShrinkToContent()
	{
		Control control = Controls.OfType<Control>().FirstOrDefault((Control c) => c is TableLayoutPanel);
		if (control != null && control.Visible)
		{
			Rectangle workingArea = Screen.FromControl(this).WorkingArea;
			int val = Math.Max(240, workingArea.Width - 64);
			int val2 = Math.Max(160, workingArea.Height - 64);
			int num = Math.Min(Math.Max(control.Padding.Horizontal + RequiredRowWidth(control), 320), val);
			int num2 = Math.Min(control.Height, val2);
			if (ClientSize.Width != num || ClientSize.Height != num2)
			{
				ClientSize = new Size(num, num2);
				ResponsiveLayout.PerformLayoutTree(this);
				// 2026-10-09（settingswarm）：这里刚做完一次全树重排，把签名刷新为最新
				_layoutSignature = CurrentLayoutSignature();
				_layoutSignatureDirty = false;
			}
		}
	}

	private static int RequiredRowWidth(Control node)
	{
		int num = 0;
		if (node is TableLayoutPanel tableLayoutPanel)
		{
			int num2 = 0;
			foreach (ColumnStyle item in (IEnumerable)tableLayoutPanel.ColumnStyles)
			{
				if (item.SizeType == SizeType.Absolute)
				{
					num2 += (int)item.Width;
				}
			}
			int num3 = 0;
			foreach (Control control in tableLayoutPanel.Controls)
			{
				int num4 = ((control is TableLayoutPanel node2) ? RequiredRowWidth(node2) : control.PreferredSize.Width);
				if (num4 > num3)
				{
					num3 = num4;
				}
			}
			int num5 = num2 + num3 + tableLayoutPanel.Padding.Horizontal + tableLayoutPanel.Margin.Horizontal;
			if (num5 > num)
			{
				num = num5;
			}
		}
		foreach (Control control2 in node.Controls)
		{
			int num6 = RequiredRowWidth(control2);
			if (num6 > num)
			{
				num = num6;
			}
		}
		return num;
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		ResponsiveLayout.PerformLayoutTree(this);
		ShrinkToContent();
		if (_owner != null)
		{
			ResponsiveLayout.PlaceAdjacent(this, _owner);
		}
	}

	internal void ShowAdjacentTo(Form owner)
	{
		if (Visible)
		{
			Activate();
			return;
		}
		_owner = owner;
		if (IsHandleCreated)
		{
			// 2026-10-09（settingswarm）：几何输入没变就不再重排整棵树 —— 改前这里**每次**都白付
			// 1.37~1.45 秒（本机实测：第二次打开 1657ms 里的 1368ms 就是它，而几何与上一次逐值相同）。
			// ⚠️ 只在"签名没变"时跳过；任何会改几何的东西（客户区尺寸/DPI/根表行数/三处行可见性）变了，
			// 都置脏 ⇒ 下一次照旧全树重排（与改前行为一致）。
			if (_layoutSignatureDirty || CurrentLayoutSignature() != _layoutSignature)
			{
				ResponsiveLayout.PerformLayoutTree(this);
			}
			ShrinkToContent();
			ResponsiveLayout.PlaceAdjacent(this, owner);
			_layoutSignature = CurrentLayoutSignature();
			_layoutSignatureDirty = false;
		}
		Show();
	}
}
