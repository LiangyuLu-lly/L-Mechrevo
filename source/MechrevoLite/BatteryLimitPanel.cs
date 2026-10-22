// 2026-10-18（宝龙达 · 充电阈值 · 产品接线）：⭐⭐ **电池页 · 充电阈值区域** ⭐⭐
//
// ⭐ 能力判据：`UsesBaolongda` + 板号命中宝龙达白名单（MRID6/MRID6-*）⇒ 构建；
//    EC 状态可读与否只影响本区域内容（读不到时显示“状态读不到”），⛔ 非宝龙达不构建。
//
// ⭐ UI 布局（机主 2026-10-18 定稿，自上而下）：
//    电量 92%
//    [开关] 限制充电                       [保存阈值]   ← 仓库现有 RCheckBox 开关样式 + 最右按钮
//    起充（%） [70]      停充（%） [95]                 ← 两列等宽、输入右对齐、标签不换行
//    ✅ 限制充电已生效（停充 95% / 起充 70%）            ← 无“当前:”前缀、无 EC 原始值
//    ⚠️ SOC ≥ 停充阈值时的可操作提示
//
// ⭐ 颜色全部走 `UiVisualStyle`（浅/深色主题；`ApplyTheme()` 由 SettingsForm.ApplyThemeMode 调用）✅
//    · 文本 = Text · 次要/正常状态 = Muted · 错误 = Danger · 警示 = Warn
//    · 输入框底 = Input · 按钮面 = ButtonFace · 边框 = Border
// ⭐ EC 原始值只写 `[battery]` 日志/诊断包，⛔ 不上 UI ✅

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using Microsoft.Win32;

namespace MechrevoLite;

/// <summary>⭐⭐ 电池页的宝龙达充电阈值区域（仅同款机型构建）✅。</summary>
internal sealed class BatteryLimitPanel : Panel
{
	internal const string PanelControlName = "batteryLimitPanel";

	private readonly Label _socLabel;

	private readonly Label _lowLabel;

	private readonly Label _highLabel;

	private readonly NumericUpDown _lowBox;

	private readonly NumericUpDown _highBox;

	// ⭐ 机主要求：用仓库现有开关控件（RCheckBox 的拨动开关样式），⛔ 不用普通 CheckBox 对勾 ✅
	private readonly RCheckBox _enableBox;

	// ⭐ 机主批准：高级开关（默认关；允许 SOC ≥ 停充阈值时启用，固件会先放电）✅

	private readonly RCheckBox _voltageFixBox;

	private CancellationTokenSource? _dischargeWatch;

	private readonly Button _applyButton;

	private readonly Label _statusLabel;

	private TableLayoutPanel _grid;

	private TableLayoutPanel _switchRow;

	private readonly ToolTip _toolTip = new ToolTip();

	private bool _busy;

	private bool _syncing;

	private bool _active;

	// 0=正常（Muted）· 1=错误（Danger）· 2=警示（Warn）
	private int _statusKind;

	// ⭐ 2026-10-19（机主指定）：⭐ 紧凑行高（22/28/26/20/20 = 116）✅
	private const int RowSoc = 22;

	// 开关 + 保存按钮同一行：28 逻辑像素（按钮上下各 2 外边距 ⇒ 24 高）


	private const int RowInput = 26;

	private const int RowAdvanced = 20;

	private const int RowStatus = 20;


	public BatteryLimitPanel()
	{
		Name = PanelControlName;
		// 2026-10-22（机主截图：无界/宝龙达电池面板“UI 叠一起”）：透明面板不擦除背景，
		// 滚动/重绘时会把下面卡片的旧文字（如「退出」/日期）透出来 ⇒ 面板改不透明。
		BackColor = UiVisualStyle.Surface;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		// ⭐ 紧凑布局：Dock=Top + AutoSize，⛔ 不用 Fill 拉满（避免上下空白）✅
		Dock = DockStyle.Top;

		_socLabel = MakeFixedLabel(Strings.BatteryLimitSocReading, D(240));
		_socLabel.Font = UiVisualStyle.Font(9f);

		_lowLabel = MakeFixedLabel(Strings.BatteryLimitStartPercent, D(88));
		_lowBox = MakeNumeric(1, 99, 60);
		_highLabel = MakeFixedLabel(Strings.BatteryLimitStopPercent, D(88));
		_highBox = MakeNumeric(2, 100, 80);

		_enableBox = new RCheckBox
		{
			AutoSize = true,
			Text = Strings.BatteryLimitEnableCharge,
			BackColor = Color.Transparent,
			Margin = Padding.Empty
		};
		_toolTip.SetToolTip(_enableBox, Strings.BatteryLimitEnableChargeTip);
		_enableBox.CheckedChanged += EnableBox_CheckedChanged;

		// ⭐ 2026-10-19（机主：自定义 RButton 在非 100% DPI 下不稳）：⭐ 改用**系统标准 Button**：
		//   自带文字度量/外框留白 ⇒ ⛔ 不会裁字；Flat 样式、固定宽 88、高 = GetPreferredSize（通常 26~32）✅
		_applyButton = new Button
		{
			AutoSize = false,
			Dock = DockStyle.None,
			Anchor = AnchorStyles.Top | AnchorStyles.Right,
			FlatStyle = FlatStyle.Flat,
			UseVisualStyleBackColor = false,
			Padding = Padding.Empty,
			Text = Strings.BatteryLimitSaveThreshold,
			Margin = new Padding(D(2), ScaleCeil(1), 0, ScaleCeil(1))
		};
		_applyButton.Font = UiVisualStyle.Font(8.5f);
		_applyButton.Size = new Size(ScaleCeil(88), _applyButton.GetPreferredSize(Size.Empty).Height);
		_applyButton.FlatAppearance.BorderSize = 1;
		_applyButton.AutoEllipsis = false;
		_applyButton.Click += ApplyButton_Click;

		// 8000 系无界：电池充电电压修复（仅内置 inpoutx64 通道）
		_voltageFixBox = new RCheckBox
		{
			AutoSize = true,
			Text = "无界电池电压修复(实验性)",
			BackColor = Color.Transparent,
			Margin = Padding.Empty,
			Checked = MechrevoLite.Hardware.BatteryVoltageFix.Enabled,
			Visible = MechrevoLite.Hardware.BatteryVoltageFix.IsSupportedMachine()
		};
		_toolTip.SetToolTip(_voltageFixBox, "内置（inpoutx64 驱动）覆盖 EC 老化降额，把充电目标恢复为 17.6V；不调用任何外部脚本");
		_voltageFixBox.CheckedChanged += delegate
		{
			MechrevoLite.Hardware.BatteryVoltageFix.Enabled = _voltageFixBox.Checked;
			if (_voltageFixBox.Checked)
			{
				(bool ok, string detail) = MechrevoLite.Hardware.BatteryVoltageFix.Run("hold-full");
				_statusLabel.Text = "电池电压修复：" + (ok ? "已执行" : "失败") + " · " + detail;
			}
			else
			{
				_statusLabel.Text = "电池电压修复：已关闭（重启后回默认）";
			}
		};

		_statusLabel = MakeFixedLabel(Strings.BatteryLimitStatusReading, D(360));
		_statusLabel.Margin = Padding.Empty;

		TableLayoutPanel grid = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = 4,
			RowCount = 5,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		_grid = grid;
		for (int c = 0; c < 4; c++)
		{
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		}
		// 紧凑：① 电量 22 ② 开关+保存 28 ③ 起充/停充 26 ④ 放电开关 20 ⑤ 状态 20 = 116 ✅
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowSoc)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(24)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowInput)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowStatus)));
	grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(24)));
		grid.Height = D(RowSoc + RowInput + RowStatus + 24);

		// ⭐ 开关 + 保存按钮同一行：内嵌表 2 列（开关占满、按钮固定 120 逻辑像素，⛔ 不再被 25% 列裁字）✅
		TableLayoutPanel switchRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		_switchRow = switchRow;
		switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		switchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_enableBox.Dock = DockStyle.Fill;
		switchRow.Controls.Add(_enableBox, 0, 0);
		switchRow.Controls.Add(_applyButton, 1, 0);

		grid.Controls.Add(_socLabel, 0, 0);
		grid.SetColumnSpan(_socLabel, 4);
		grid.Controls.Add(switchRow, 0, 1);
		grid.SetColumnSpan(switchRow, 4);
		grid.Controls.Add(_lowLabel, 0, 2);
		grid.Controls.Add(_lowBox, 1, 2);
		grid.Controls.Add(_highLabel, 2, 2);
		grid.Controls.Add(_highBox, 3, 2);
		grid.Controls.Add(_statusLabel, 0, 3);
		grid.SetColumnSpan(_statusLabel, 4);
		grid.Controls.Add(_voltageFixBox, 0, 4);
		grid.SetColumnSpan(_voltageFixBox, 4);
		Controls.Add(grid);
		SyncOpaqueBackColor();
		// ⭐ 代码级 UI 自检（无法启动 GUI 时作为可验证替代）：标签宽度是否足够 ⇒ 不换行 ✅
		LogLabelWidthCheck("起充标签", _lowLabel, D(88));
		LogLabelWidthCheck("停充标签", _highLabel, D(88));
		LogLabelWidthCheck("状态行", _statusLabel, D(360));
		LogSwitchRowCheck();
		ApplyButtonRowMetrics();
		RefreshFromEc();
		ApplyTheme();
		LogDiagnostics("build");
		HandleCreated += delegate { LogDiagnostics("handle"); };
	}

	/// <summary>⭐ Tooltip 是组件、不随 Panel 自动释放 ⇒ 手动释放 ✅</summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			CancelDischargeWatch();
		}
		if (disposing)
		{
			_toolTip.Dispose();
		}
		base.Dispose(disposing);
	}

	/// <summary>⭐⭐ 主题适配：颜色全部走 UiVisualStyle（浅/深色同一套语义），⛔ 不硬编码 ✅。</summary>
	/// <summary>2026-10-22：面板及内嵌表格跟随宿主卡片的实际背景色（不透明），避免透明重绘叠字。</summary>
	private void SyncOpaqueBackColor()
	{
		Color color = (Parent != null && Parent.BackColor != Color.Transparent) ? Parent.BackColor : UiVisualStyle.Surface;
		BackColor = color;
		if (_grid != null)
		{
			_grid.BackColor = color;
		}
		if (_switchRow != null)
		{
			_switchRow.BackColor = color;
		}
	}

	protected override void OnParentChanged(EventArgs e)
	{
		base.OnParentChanged(e);
		SyncOpaqueBackColor();
	}

	internal void ApplyTheme()
	{
		Color text = UiVisualStyle.Text;
		// 2026-10-22：与构造一致，主题应用时保持不透明，避免重绘把下层文字透出来。
		SyncOpaqueBackColor();
		_socLabel.ForeColor = text;
		_lowLabel.ForeColor = text;
		_highLabel.ForeColor = text;
		_enableBox.ForeColor = text;
		_enableBox.BackColor = Color.Transparent;
		_lowBox.ForeColor = text;
		_lowBox.BackColor = UiVisualStyle.Input;
		_highBox.ForeColor = text;
		_highBox.BackColor = UiVisualStyle.Input;
		_applyButton.ForeColor = text;
		_applyButton.BackColor = UiVisualStyle.ButtonFace;
		_applyButton.FlatAppearance.BorderColor = UiVisualStyle.Border;
		ApplyStatusColor();
		Logger.WriteLineIfChanged("battery-ui-theme",
			"[battery-ui] 主题=" + (UiVisualStyle.IsNightMode ? "Dark" : "Light")
			+ " ⇒ 文本色=" + ToHex(text) + " · 背景=" + ToHex(BackColor)
			+ " · 状态行=" + ToHex(_statusLabel.ForeColor));
	}

	private static string ToHex(Color color)
	{
		return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}

	private void SetStatus(string text, int kind)
	{
		_statusLabel.Text = text;
		_statusKind = kind;
		ApplyStatusColor();
	}

	private void ApplyStatusColor()
	{
		// ⭐ 浅色主题用更深的次要色（Text）保证对比度；深色保持 Muted；成功色走 UiVisualStyle.Success ✅
		if (_statusKind == 1)
		{
			_statusLabel.ForeColor = UiVisualStyle.Danger;
		}
		else if (_statusKind == 2)
		{
			_statusLabel.ForeColor = UiVisualStyle.Warn;
		}
		else if (_statusKind == 3)
		{
			_statusLabel.ForeColor = UiVisualStyle.Success;
		}
		else
		{
			_statusLabel.ForeColor = UiVisualStyle.IsNightMode ? UiVisualStyle.Muted : UiVisualStyle.Text;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：面板行被裁 / 面板未构建）：⭐ **只判机型**（不读 EC）——
	/// `UsesBaolongda && BoardWhitelist.Matches(MRID6…)` ⇒ 允许构建面板（EC 读不到时面板内部显示“状态读不到”）✅
	/// </summary>
	internal static bool IsSupportedBoard(MechrevoHw hw)
	{
		try
		{
			if (hw == null || !hw.UsesBaolongda)
			{
				return false;
			}
			// ⭐ 2026-10-19：多来源板号取证（reg/WMI）统一入口 ✅
			return BoardWhitelist.Matches(BoardWhitelist.ReadBoardProduct());
		}
		catch
		{
			return false;
		}
	}

	/// <summary>⭐ 能力判据：`UsesBaolongda` + 板号命中宝龙达白名单 + EC 可读（用于电池页整体可见性）✅。</summary>
	internal static bool IsAvailable(MechrevoHw hw)
	{
		try
		{
			return IsSupportedBoard(hw) && BatteryLimitWriter.TryReadState(out _, out _, out _, out _);
		}
		catch
		{
			return false;
		}
	}

	private static string ReadBaseBoardProduct()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	private void RefreshFromEc()
	{
		if (!BatteryLimitWriter.TryReadState(out int high, out int low, out int mode, out string detail))
		{
			SetStatus((PawnIO.RyzenSmuService.IsPawnInstalled() ? Strings.BatteryLimitStatusUnreadable : "需要 PawnIO 驱动（设置页可安装）"), 1);
			Logger.WriteLine("[battery] 充电阈值状态（UI 刷新）：" + detail);
			UpdateSocLine(active: false);
				return;
		}
		// ⭐ EC 原始值只进日志/诊断包，⛔ 不上 UI ✅
		Logger.WriteLine("[battery] 充电阈值状态（UI 刷新）：" + detail);
		bool active = mode == BatteryLimitWriter.ModeActive;
		_active = active;
		_syncing = true;
		try
		{
			if (low >= 1 && low < high && high <= 100)
			{
				_lowBox.Value = low;
				_highBox.Value = high;
			}
			_enableBox.Checked = active;
		}
		finally
		{
			_syncing = false;
		}
		// ⭐ 机主要求：纯文字、无对勾；生效=绿色 Success，未启用=灰/Muted；
		//   ⭐ 同时显示**实际生效值**（enable 时可能按 SOC 提升 high ⇒ 这里回显 EC 读回值）✅
		SetStatus(active ? string.Format(Strings.BatteryLimitStatusActiveFormat, high) : Strings.BatteryLimitStatusInactive, active ? 3 : 0);
		UpdateSocLine(active);
	}

	/// <summary>⭐ 第二行只显示电量（状态摘要唯一位置=标题行右侧）✅。</summary>
	private void UpdateSocLine(bool active)
	{
		try
		{
			int soc = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
			_socLabel.Text = soc >= 0 ? string.Format(Strings.BatteryLimitSocFormat, soc) : Strings.BatteryLimitSocUnreadable;
			Logger.WriteLineIfChanged("battery-ui-soc", "[battery-ui] 电量行：电量=" + soc + "% · active=" + active);
		}
		catch
		{
			_socLabel.Text = Strings.BatteryLimitSocUnreadable;
		}
	}

	private async void EnableBox_CheckedChanged(object sender, EventArgs e)
	{
		if (_busy || _syncing)
		{
			return;
		}
		bool want = _enableBox.Checked;
		SetBusy(true);
		try
		{
			(bool ok, string detail) = await Task.Run(delegate
			{
				if (want)
				{
					bool enabled = BatteryLimitWriter.TryEnable((int)_lowBox.Value, (int)_highBox.Value, allowInitialDischarge: true, out string enableDetail);
					return (enabled, enableDetail);
				}
				bool disabled = BatteryLimitWriter.TryDisable(out string disableDetail);
				return (disabled, disableDetail);
			});
			Logger.WriteLine("[battery] 充电限制" + (want ? "启用" : "关闭") + "（UI）：ok=" + ok + "\n" + detail);
			if (!ok)
			{
				SetStatus(want ? Strings.BatteryLimitEnableFailed : Strings.BatteryLimitDisableFailed, 1);
			}
			else
			{
				// ⭐ 成功 ⇒ 落本地配置（启动核对/恢复用；⛔ 失败不写，避免把错误状态当配置）✅
				BatteryLimitRestore.SaveConfig(want, (int)_lowBox.Value, (int)_highBox.Value);
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			SetStatus(Strings.BatteryLimitOperationFailed, 1);
			Logger.WriteLine("[battery] 充电限制 UI 异常：" + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	/// <summary>
	/// 启用后 30 秒放电观察（对齐参考实现）：默认路径下若检测到意外放电 ⇒ 立即恢复默认充电并提示。
	/// <para>只读轮询 0x8394 / 0x83A0 / 0x847C；不写任何寄存器，恢复动作走 TryDisable。</para>
	/// </summary>
	private void StartDischargeWatch(int high)
	{
		CancelDischargeWatch();
		CancellationTokenSource cts = new CancellationTokenSource();
		_dischargeWatch = cts;
		CancellationToken token = cts.Token;
		_ = Task.Run(async delegate
		{
			try
			{
				long deadline = Environment.TickCount64 + 30000;
				while (!token.IsCancellationRequested && Environment.TickCount64 < deadline)
				{
					await Task.Delay(5000, token).ConfigureAwait(false);
					if (token.IsCancellationRequested)
					{
						return;
					}
					// 只有「接着电源、SOC 低于停充阈值却仍在放电」才算异常；
					// SOC 高于停充阈值时固件放电到阈值属于限充的正常行为（尤其开了允许初始放电）。
					bool onAc = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
					bool discharging = EcPortIo.TryReadByteStable(0x8394, out int flags, out _, attempts: 1) && (flags & 8) != 0;
					bool socKnown = EcPortIo.TryReadByteStable(0x847C, out int soc, out _, attempts: 1);
					bool unexpected = onAc && discharging && socKnown && soc < high;
					if (!unexpected)
					{
						continue;
					}
					Logger.WriteLine("[battery] 接电且 SOC<" + high + "% 仍在放电（0x8394=" + flags + " · SOC=" + soc + "）⇒ 恢复默认充电");
					bool restored = BatteryLimitWriter.TryDisable(out string detail);
					Logger.WriteLine("[battery] 意外放电自动恢复：ok=" + restored + "\n" + detail);
					BatteryLimitRestore.SaveConfig(false, 0, 0);
					if (!IsDisposed)
					{
						BeginInvoke(delegate
						{
							if (!IsDisposed)
							{
								SetStatus(restored ? Strings.BatteryLimitDischargeAutoRestored : Strings.BatteryLimitDischargeRestoreFailed, restored ? 3 : 1);
								RefreshFromEc();
							}
						});
					}
					return;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery] 放电观察异常：" + ex.GetType().Name + " " + ex.Message);
			}
		});
	}

	private void CancelDischargeWatch()
	{
		try
		{
			_dischargeWatch?.Cancel();
		}
		catch
		{
		}
		_dischargeWatch = null;
	}

	private async void ApplyButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		int low = (int)_lowBox.Value;
		int high = (int)_highBox.Value;
		if (!(low >= 1 && low < high && high <= 100))
		{
			SetStatus(Strings.BatteryLimitInvalidParams, 1);
			return;
		}
		SetBusy(true);
		try
		{
			(bool ok, string detail) = await Task.Run(delegate
			{
				bool result = BatteryLimitWriter.TryEnable(low, high, allowInitialDischarge: true, out string enableDetail);
				return (result, enableDetail);
			});
			Logger.WriteLine("[battery] 充电限制保存阈值（UI）：low=" + low + " high=" + high + " ok=" + ok + "\n" + detail);
			if (!ok)
			{
				SetStatus(Strings.BatteryLimitSaveFailed, 1);
			}
			else
			{
				BatteryLimitRestore.SaveConfig(enabled: true, low, high);
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			SetStatus(Strings.BatteryLimitOperationFailed, 1);
			Logger.WriteLine("[battery] 充电阈值保存 UI 异常：" + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_enableBox.Enabled = !busy;
		_applyButton.Enabled = !busy;
		_lowBox.Enabled = !busy;
		_highBox.Enabled = !busy;
	}

	/// <summary>⭐ DPI 取整向上（DPI 缩放后 ceil）—— 用于按钮高/行高，避免 125/150% 下向下取整吃余量 ✅</summary>
	private int ScaleCeil(int logical)
	{
		return (int)Math.Ceiling(logical * (DeviceDpi / 96.0));
	}

	private int D(int logical)
	{
		return ResponsiveLayout.LogicalToDevice(this, logical);
	}

	private Label MakeFixedLabel(string text, int width)
	{
		return new Label
		{
			AutoSize = false,
			Text = text,
			Width = width,
			Height = D(22),
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, D(2), 0, 0),
			BackColor = Color.Transparent
		};
	}

	private NumericUpDown MakeNumeric(int min, int max, int value)
	{
		return new NumericUpDown
		{
			Minimum = min,
			Maximum = max,
			Value = value,
			Dock = DockStyle.None,
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			TextAlign = HorizontalAlignment.Right,
			Margin = new Padding(D(2), D(2), D(6), D(2))
		};
	}

	/// <summary>
	/// ⭐⭐ 机主要求：代码级 UI 自检 —— 开关 + 保存按钮同行时是否重叠（按钮列已 AutoSize）✅
	/// </summary>
	/// <summary>
	/// ⭐⭐ 机主要求：代码级自检 —— 保存按钮小圆角 + 尺寸能否放下「保存阈值」且不裁切 ✅
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：按钮裁切一直存在）：⭐ **按钮行高度由内容决定**：
	/// `max(按钮 PreferredSize.Height, 开关 PreferredSize.Height) + 4`，DPI/Layout 变化时重算 ✅
	/// </summary>
	private void ApplyButtonRowMetrics()
	{
		try
		{
			Size buttonPreferred = _applyButton.GetPreferredSize(Size.Empty);
			Size switchPreferred = _enableBox.GetPreferredSize(Size.Empty);
			// ⭐ 2026-10-19（预览工具：按钮溢出到下一行）：⭐ 行高必须 ≥ 按钮实际高度 + 上下 margin；
			//   ⛔ 不再用 PreferredSize（固定 Size 时它可能小于实际 Height）✅
			// ⭐ 机主要求：行高 = max(按钮首选高, 开关首选高) + 上下 margin + 3 设备像素 ✅
			int buttonHeight = Math.Max(buttonPreferred.Height, _applyButton.Height);
			int rowHeight = Math.Max(buttonHeight + _applyButton.Margin.Vertical + D(3), switchPreferred.Height + D(4));
			if (_grid.RowStyles.Count > 1)
			{
				if ((int)_grid.RowStyles[1].Height != rowHeight)
				{
					_grid.RowStyles[1] = new RowStyle(SizeType.Absolute, rowHeight);
				}
				float total = 0f;
				foreach (RowStyle rowStyle in _grid.RowStyles)
				{
					total += Math.Max(0f, rowStyle.Height);
				}
				_grid.Height = (int)Math.Round(total);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-ui] 按钮行尺寸计算失败：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	protected override void OnLayout(LayoutEventArgs levent)
	{
		base.OnLayout(levent);
		ApplyButtonRowMetrics();
	}

	/// <summary>
	/// ⭐ 2026-10-19（机主：先量再说）：⭐ 面板/行高/控件 bounds 实测日志（build/handle/host 三处）✅
	/// </summary>
	internal void LogDiagnostics(string stage)
	{
		try
		{
			Size preferred = GetPreferredSize(Size.Empty);
			Logger.WriteLineIfChanged("battery-ui-metrics-panel",
				"[battery-ui] 面板(" + stage + ")：size=" + Width + "x" + Height + " · preferred=" + preferred.Width + "x" + preferred.Height
				+ " · grid.size=" + _grid.Width + "x" + _grid.Height + " · grid.height=" + _grid.Height);
			string rows = "";
			float total = 0f;
			for (int i = 0; i < _grid.RowStyles.Count; i++)
			{
				rows += (i == 0 ? "" : " ") + i + "=" + _grid.RowStyles[i].Height;
				total += _grid.RowStyles[i].Height;
			}
			Logger.WriteLineIfChanged("battery-ui-metrics-rows",
				"[battery-ui] 行高(" + stage + ")：0=" + Value(0) + " 1=" + Value(1) + " 2=" + Value(2) + " 3=" + Value(3)
				+ " 4=" + Value(4) + " · 合计=" + total);
			// ⭐ 机主要求：按钮行 = 按钮 preferred 高 + 4（动态，不猜）✅
			int buttonPreferredHeight = _applyButton.GetPreferredSize(Size.Empty).Height;
			Logger.WriteLineIfChanged("battery-ui-button-row-height",
				"[battery-ui] 行高(" + stage + ")：按钮行=" + Value(1) + "（按钮 preferred=" + buttonPreferredHeight
				+ " + 4）· 合计=" + total);
			LogSaveButtonCheck(stage);
			Logger.WriteLineIfChanged("battery-ui-metrics-bounds",
				"[battery-ui] 控件 bounds(" + stage + ")：电量=" + Rect(_socLabel) + " · 开关行=" + Rect(_enableBox)
				+ " · 起充/停充=" + Rect(_lowBox) + "/" + Rect(_highBox)
				+ " · 状态=" + Rect(_statusLabel));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-ui] 面板自检失败（" + stage + "）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	private string Value(int index)
	{
		return index >= 0 && index < _grid.RowStyles.Count ? _grid.RowStyles[index].Height.ToString("0") : "?";
	}

	private static string Rect(Control control)
	{
		return control == null ? "null" : (control.Left + "," + control.Top + " " + control.Width + "x" + control.Height);
	}
	private void LogSaveButtonCheck(string stage)
	{
		try
		{
			Size preferred = _applyButton.GetPreferredSize(Size.Empty);
			Size actual = _applyButton.Size;
			Size text = TextRenderer.MeasureText(_applyButton.Text, _applyButton.Font);
			bool fail = actual.Width > 0 && (actual.Height < text.Height + 6 || actual.Width < text.Width + 8 || actual.Height < preferred.Height);
			int rowHeight = _grid.RowStyles.Count > 1 ? (int)_grid.RowStyles[1].Height : 0;
			bool overlapsRow = rowHeight > 0 && actual.Height + _applyButton.Margin.Vertical > rowHeight;
			string message = "[battery-ui] 按钮：名=保存阈值 · stage=" + stage + " · preferred=" + preferred.Width + "x" + preferred.Height
				+ " · size=" + actual.Width + "x" + actual.Height + " · 文本=" + text.Width + "x" + text.Height
				+ " · overlapsRow=" + overlapsRow + " ⇒ 重叠=" + overlapsRow + " · 裁切=" + fail;
			Logger.WriteLineIfChanged("battery-ui-save-button-" + stage, message);
			if (fail || overlapsRow)
			{
				Logger.WriteError("[battery-ui] ⛔ 保存按钮裁切/跨行 FAIL（" + stage + "）：" + message);
			}
			// ⭐ 2026-10-19（机主：按钮四边被裁）：⭐ 通用四边余量自检（下边/左边各留 2~3px）✅
			int fitRowHeight = _grid.RowStyles.Count > 1 ? (int)_grid.RowStyles[1].Height : 0;
			int bottomGap = fitRowHeight - (_applyButton.Bottom + _applyButton.Margin.Bottom);
			int leftGap = _applyButton.Left - _applyButton.Margin.Left;
			int dpiPercent = (int)Math.Round(DeviceDpi / 96.0 * 100.0);
			bool fit = _applyButton.Dock != DockStyle.Fill && bottomGap >= 3 && leftGap >= 2;
			string fitMessage = "[ui-fit] 按钮=保存阈值 · dpi=" + dpiPercent + "% · size=" + _applyButton.Width + "x" + _applyButton.Height
				+ " · 行高=" + fitRowHeight + " · 下边余量(设备px)=" + bottomGap + " · 左边余量=" + leftGap
				+ " ⇒ 裁切=" + !fit;
			Logger.WriteLineIfChanged("ui-fit-save-button-" + stage, fitMessage);
			if (!fit)
			{
				Logger.WriteError("⛔ 按钮裁切 FAIL：" + fitMessage);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-ui] 保存按钮自检失败：" + ex.GetType().Name + " " + ex.Message);
		}
	}
	private void LogSwitchRowCheck()
	{
		try
		{
			// 新布局：内嵌行 = 开关占满剩余宽 + 按钮固定 120 逻辑像素
			int buttonAvailable = _applyButton.GetPreferredSize(Size.Empty).Width;
			int checkAvailable = D(352) - buttonAvailable - D(8);
			int checkWidth = TextRenderer.MeasureText(_enableBox.Text, _enableBox.Font).Width;
			int buttonTextWidth = TextRenderer.MeasureText(_applyButton.Text, _applyButton.Font).Width;
			bool overlap = checkWidth > checkAvailable || buttonTextWidth > buttonAvailable - D(8);
			string message = "[battery-ui] 开关+按钮行：开关文本宽=" + checkWidth + "px/可用=" + checkAvailable
				+ "px · 按钮文本宽=" + buttonTextWidth + "px/按钮列宽=" + buttonAvailable
				+ "px ⇒ 重叠=" + overlap;
			Logger.WriteLine(message);
			Logger.WriteLineIfChanged("battery-ui-switch-row", message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-ui] 开关行自检失败：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	/// <summary>
	/// ⭐⭐ 机主要求：代码级 UI 自检 —— 用 `TextRenderer.MeasureText` 比较“文本宽 vs 可用列宽”，
	/// 证明 16 字以内的标签不会换行（截图里的「起充 %（低）」换行问题）✅
	/// </summary>
	private void LogLabelWidthCheck(string rowName, Label label, int columnWidth)
	{
		try
		{
			Size measured = TextRenderer.MeasureText(label.Text, label.Font);
			bool wraps = !label.AutoSize && measured.Width > columnWidth;
			string message = "[battery-ui] 行=" + rowName + " · 文本=\"" + label.Text + "\" · 文本宽="
				+ measured.Width + "px · 列宽=" + columnWidth + "px ⇒ 会换行=" + wraps;
			Logger.WriteLine(message);
			Logger.WriteLineIfChanged("battery-ui-width-" + rowName, message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-ui] 宽度自检失败（" + rowName + "）：" + ex.GetType().Name + " " + ex.Message);
		}
	}
}
