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

	// ⭐ 机主要求：用仓库现有圆角按钮控件 RButton；半径**面板局部**调小（⛔ 不全局改）✅
	private readonly RButton _applyButton;

	private readonly Label _statusLabel;

	private TableLayoutPanel _grid;

	private readonly ToolTip _toolTip = new ToolTip();

	private bool _busy;

	private bool _syncing;

	private bool _active;

	// 0=正常（Muted）· 1=错误（Danger）· 2=警示（Warn）
	private int _statusKind;

	// ⭐ 统一行距常量（逻辑像素；只用这一套，⛔ 不混用半行高）✅
	private const int RowSoc = 24;

	private const int RowInput = 30;

	private const int RowStatus = 24;


	public BatteryLimitPanel()
	{
		Name = PanelControlName;
		BackColor = Color.Transparent;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Dock = DockStyle.Fill;

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
			Margin = new Padding(0, D(4), 0, 0)
		};
		_toolTip.SetToolTip(_enableBox, Strings.BatteryLimitEnableChargeTip);
		_enableBox.CheckedChanged += EnableBox_CheckedChanged;

		_applyButton = new RButton
		{
			AutoSize = false,
			Dock = DockStyle.Fill,
			Text = Strings.BatteryLimitSaveThreshold,
			// ⭐ 机主要求：**小圆角矩形**（≈4 逻辑像素；默认 8/12 太大）✅
			BorderRadius = D(4),
			Margin = new Padding(D(6), D(4), 0, D(4))
		};
		_applyButton.Click += ApplyButton_Click;

		_statusLabel = MakeFixedLabel(Strings.BatteryLimitStatusReading, D(360));
		_statusLabel.Margin = new Padding(0, D(4), 0, 0);

		TableLayoutPanel grid = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = 4,
			RowCount = 4,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = Color.Transparent
		};
		_grid = grid;
		for (int c = 0; c < 4; c++)
		{
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		}
		// ① 电量 ② 开关+保存（按钮最右）③ 起充/停充 ④ 状态 ⑤ 提示 ✅
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowSoc)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowInput)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowInput)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowStatus)));
		grid.Height = D(RowSoc + RowInput + RowInput + RowStatus);

		grid.Controls.Add(_socLabel, 0, 0);
		grid.SetColumnSpan(_socLabel, 4);
		grid.Controls.Add(_enableBox, 0, 1);
		grid.SetColumnSpan(_enableBox, 3);
		grid.Controls.Add(_applyButton, 3, 1);
		grid.Controls.Add(_lowLabel, 0, 2);
		grid.Controls.Add(_lowBox, 1, 2);
		grid.Controls.Add(_highLabel, 2, 2);
		grid.Controls.Add(_highBox, 3, 2);
		grid.Controls.Add(_statusLabel, 0, 3);
		grid.SetColumnSpan(_statusLabel, 4);

		Controls.Add(grid);
		// ⭐ 代码级 UI 自检（无法启动 GUI 时作为可验证替代）：标签宽度是否足够 ⇒ 不换行 ✅
		LogLabelWidthCheck("起充标签", _lowLabel, D(88));
		LogLabelWidthCheck("停充标签", _highLabel, D(88));
		LogLabelWidthCheck("状态行", _statusLabel, D(360));
		LogSwitchRowCheck();
		LogSaveButtonCheck();
		RefreshFromEc();
		ApplyTheme();
	}

	/// <summary>⭐ Tooltip 是组件、不随 Panel 自动释放 ⇒ 手动释放 ✅</summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_toolTip.Dispose();
		}
		base.Dispose(disposing);
	}

	/// <summary>⭐⭐ 主题适配：颜色全部走 UiVisualStyle（浅/深色同一套语义），⛔ 不硬编码 ✅。</summary>
	internal void ApplyTheme()
	{
		Color text = UiVisualStyle.Text;
		BackColor = Color.Transparent;
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
		_applyButton.BorderColor = UiVisualStyle.Border;
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
			return BoardWhitelist.Matches(ReadBaseBoardProduct());
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
			SetStatus(Strings.BatteryLimitStatusUnreadable, 1);
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
					bool enabled = BatteryLimitWriter.TryEnable((int)_lowBox.Value, (int)_highBox.Value, out string enableDetail);
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
				bool result = BatteryLimitWriter.TryEnable(low, high, out string enableDetail);
				return (result, enableDetail);
			});
			Logger.WriteLine("[battery] 充电限制保存阈值（UI）：low=" + low + " high=" + high + " ok=" + ok + "\n" + detail);
			if (!ok)
			{
				SetStatus(Strings.BatteryLimitSaveFailed, 1);
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
			Dock = DockStyle.Fill,
			TextAlign = HorizontalAlignment.Right,
			Margin = new Padding(D(2), D(3), D(10), D(3))
		};
	}

	/// <summary>
	/// ⭐⭐ 机主要求：代码级 UI 自检 —— 开关 + 保存按钮同行时是否重叠：
	/// 开关文本宽 vs 跨 3 列可用宽；按钮宽 vs 第 4 列宽 ✅
	/// </summary>
	/// <summary>
	/// ⭐⭐ 机主要求：代码级自检 —— 保存按钮小圆角 + 尺寸能否放下「保存阈值」且不裁切 ✅
	/// </summary>
	private void LogSaveButtonCheck()
	{
		try
		{
			Size preferred = _applyButton.GetPreferredSize(Size.Empty);
			int textWidth = TextRenderer.MeasureText(_applyButton.Text, _applyButton.Font).Width;
			int radius = _applyButton.BorderRadius;
			bool enough = preferred.Width >= textWidth + radius * 2
				&& preferred.Height >= TextRenderer.MeasureText(_applyButton.Text, _applyButton.Font).Height + D(6);
			string message = "[battery-ui] 保存按钮：圆角半径=" + radius + "px · 尺寸=" + preferred.Width
				+ "x" + preferred.Height + " · 文本宽=" + textWidth + " ⇒ 不裁切=" + enough;
			Logger.WriteLine(message);
			Logger.WriteLineIfChanged("battery-ui-save-button", message);
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
			int column = D(88);
			int checkAvailable = column * 3 - D(8);
			int checkWidth = TextRenderer.MeasureText(_enableBox.Text, _enableBox.Font).Width;
			Size buttonPreferred = _applyButton.GetPreferredSize(Size.Empty);
			int buttonAvailable = column - D(8);
			bool overlap = checkWidth > checkAvailable || buttonPreferred.Width > buttonAvailable;
			string message = "[battery-ui] 开关+按钮行：开关文本宽=" + checkWidth + "px/可用=" + checkAvailable
				+ "px · 按钮宽=" + buttonPreferred.Width + "px/列宽=" + buttonAvailable
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
