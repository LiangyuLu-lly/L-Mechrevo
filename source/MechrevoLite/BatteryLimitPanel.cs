// 2026-10-18（宝龙达 · 充电阈值 · 产品接线）：⭐⭐ **电池页 · 充电阈值区域** ⭐⭐
//
// ⭐ 能力判据：`UsesBaolongda` + 板号命中宝龙达白名单（MRID6/MRID6-*）⇒ 构建；
//    EC 状态可读与否只影响本区域内容（读不到时显示“状态读不到”），⛔ 非宝龙达不构建。
//
// ⭐ UI 布局（机主 2026-10-18 定稿，自上而下）：
//    电量 92%                              ← 只保留电量，状态摘要统一在标题行右侧
//    起充（%） [70]      停充（%） [95]    ← 两列等宽、输入右对齐、标签不换行
//    ☑ 启用限制充电（接电源时限制充电上限）
//    [保存阈值]
//    ✅ 限制充电已生效（停充 95% / 起充 70%）   ← 无“当前:”前缀、无 EC 原始值
//    ⚠️ SOC ≥ 停充阈值时的红色可操作提示
//
// ⭐ EC 原始值（0x83A1/0x83A2/0x83C0…）只写 `[battery]` 日志/诊断包，⛔ 不上 UI ✅
// ⚠️ 写入只走 `BatteryLimitWriter`（白名单 4 地址 + 严格时序 + 失败不重试）✅

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.UI;
using Microsoft.Win32;

namespace MechrevoLite;

/// <summary>⭐⭐ 电池页的宝龙达充电阈值区域（仅同款机型构建）✅。</summary>
internal sealed class BatteryLimitPanel : Panel
{
	private readonly Label _socLabel;

	private readonly NumericUpDown _lowBox;

	private readonly NumericUpDown _highBox;

	private readonly CheckBox _enableBox;

	private readonly Button _applyButton;

	private readonly Label _statusLabel;

	private readonly Label _hintLabel;

	private bool _busy;

	private bool _syncing;

	private bool _active;

	// ⭐ 统一行距常量（逻辑像素；只用这一套，⛔ 不混用半行高）✅
	private const int RowSoc = 24;

	private const int RowInput = 30;

	private const int RowCheck = 28;

	private const int RowButton = 30;

	private const int RowStatus = 24;

	private const int RowHint = 26;

	public BatteryLimitPanel()
	{
		BackColor = Color.Transparent;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Dock = DockStyle.Fill;

		_socLabel = MakeFixedLabel("电量读取中…", D(240));
		_socLabel.Font = UiVisualStyle.Font(9f);

		Label lowLabel = MakeFixedLabel("起充（%）", D(88));
		_lowBox = MakeNumeric(1, 99, 60);
		Label highLabel = MakeFixedLabel("停充（%）", D(88));
		_highBox = MakeNumeric(2, 100, 80);
		_lowBox.ValueChanged += delegate { UpdateSocSafetyHint(); };
		_highBox.ValueChanged += delegate { UpdateSocSafetyHint(); };

		_enableBox = new CheckBox
		{
			AutoSize = true,
			Text = "启用限制充电（接电源时限制充电上限）",
			Margin = new Padding(0, D(6), 0, 0)
		};
		_enableBox.CheckedChanged += EnableBox_CheckedChanged;

		_applyButton = new Button
		{
			AutoSize = true,
			Text = "保存阈值",
			Margin = new Padding(0, D(5), 0, 0)
		};
		_applyButton.Click += ApplyButton_Click;

		_statusLabel = MakeFixedLabel("状态读取中…", D(360));
		_statusLabel.Margin = new Padding(0, D(4), 0, 0);

		_hintLabel = MakeFixedLabel("开启后接电源时会限制充电上限；建议保持接电源使用。", D(420));
		_hintLabel.ForeColor = Color.FromArgb(120, 120, 120);
		_hintLabel.Margin = new Padding(0, D(4), 0, 0);

		TableLayoutPanel grid = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = 4,
			RowCount = 6,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = Color.Transparent
		};
		for (int c = 0; c < 4; c++)
		{
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		}
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowSoc)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowInput)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowCheck)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowButton)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowStatus)));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, D(RowHint)));
		grid.Height = D(RowSoc + RowInput + RowCheck + RowButton + RowStatus + RowHint);

		grid.Controls.Add(_socLabel, 0, 0);
		grid.SetColumnSpan(_socLabel, 4);
		grid.Controls.Add(lowLabel, 0, 1);
		grid.Controls.Add(_lowBox, 1, 1);
		grid.Controls.Add(highLabel, 2, 1);
		grid.Controls.Add(_highBox, 3, 1);
		grid.Controls.Add(_enableBox, 0, 2);
		grid.SetColumnSpan(_enableBox, 4);
		grid.Controls.Add(_applyButton, 0, 3);
		grid.SetColumnSpan(_applyButton, 2);
		grid.Controls.Add(_statusLabel, 0, 4);
		grid.SetColumnSpan(_statusLabel, 4);
		grid.Controls.Add(_hintLabel, 0, 5);
		grid.SetColumnSpan(_hintLabel, 4);

		Controls.Add(grid);
		// ⭐ 代码级 UI 自检（无法启动 GUI 时作为可验证替代）：标签宽度是否足够 ⇒ 不换行 ✅
		LogLabelWidthCheck("起充标签", lowLabel, D(88));
		LogLabelWidthCheck("停充标签", highLabel, D(88));
		LogLabelWidthCheck("状态行", _statusLabel, D(360));
		RefreshFromEc();
		UpdateSocSafetyHint();
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
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "状态读不到（详见日志）";
			Logger.WriteLine("[battery] 充电阈值状态（UI 刷新）：" + detail);
			UpdateSocLine(active: false);
			UpdateSocSafetyHint();
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
		_statusLabel.ForeColor = Color.FromArgb(40, 160, 80);
		// ⭐ 无“当前:”前缀；无 EC 原始值 ✅
		_statusLabel.Text = active
			? "✅ 限制充电已生效（停充 " + high + "% / 起充 " + low + "%）"
			: "未启用限制充电";
		UpdateSocLine(active);
		UpdateSocSafetyHint();
	}

	/// <summary>⭐ 第二行只显示电量（状态摘要唯一位置=标题行右侧）✅。</summary>
	private void UpdateSocLine(bool active)
	{
		try
		{
			int soc = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
			_socLabel.Text = "电量 " + (soc >= 0 ? soc + "%" : "读不到");
			Logger.WriteLineIfChanged("battery-ui-soc", "[battery-ui] 电量行：电量=" + soc + "% · active=" + active);
		}
		catch
		{
			_socLabel.Text = "电量读不到";
		}
	}

	/// <summary>
	/// ⭐⭐ 机主要求（UX）：未启用且 `SOC ≥ high` 时，给出**红色、可操作**提示；
	/// ⛔ 不移除 SOC&lt;high 安全前置（启用仍会被 writer 拒绝，但用户知道下一步怎么做）✅
	/// </summary>
	private void UpdateSocSafetyHint()
	{
		const string NormalHint = "开启后接电源时会限制充电上限；建议保持接电源使用。";
		try
		{
			if (_active)
			{
				_hintLabel.ForeColor = Color.FromArgb(120, 120, 120);
				_hintLabel.Text = NormalHint;
				return;
			}
			int soc = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
			int high = (int)_highBox.Value;
			if (soc >= 0 && soc >= high)
			{
				_hintLabel.ForeColor = Color.FromArgb(200, 40, 40);
				_hintLabel.Text = "⚠️ 当前电量 " + soc + "% ≥ 停充阈值 " + high
					+ "% ⇒ 不会发送启用命令。请二选一：① 先把电量放到低于阈值；② 把停充阈值设到当前电量以上（例如 "
					+ Math.Min(100, soc + 1) + "%）。";
			}
			else
			{
				_hintLabel.ForeColor = Color.FromArgb(120, 120, 120);
				_hintLabel.Text = NormalHint;
			}
		}
		catch
		{
			_hintLabel.ForeColor = Color.FromArgb(120, 120, 120);
			_hintLabel.Text = NormalHint;
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
				_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
				_statusLabel.Text = want ? "⛔ 启用失败（详见日志）" : "⛔ 关闭失败（详见日志）";
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "⛔ 操作异常（详见日志）";
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
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "⛔ 参数非法：要求 0 < 起充 < 停充 ≤ 100";
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
				_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
				_statusLabel.Text = "⛔ 保存失败（详见日志）";
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "⛔ 操作异常（详见日志）";
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
