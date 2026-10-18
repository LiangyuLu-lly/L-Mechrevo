// 2026-10-18（宝龙达 · 充电阈值 · 产品接线）：⭐⭐ **电池页 · 充电阈值区域** ⭐⭐
//
// ⭐ 能力判据（机主定）：`UsesBaolongda` + 板号以 `MRID6` 开头 + EC 可读 ⇒ 才显示本区域；
//    ⛔ 非宝龙达 / 其它板号 / EC 读不到 ⇒ 不构建（SettingsForm 调用前先 IsAvailable）。
//
// ⭐ 交互：
//    · 起充 %（低）· 停充 %（高）两个数值框，约束 0 < low < high <= 100，默认 60/80；
//    · 开关「启用限制充电」：勾上 = 启用（写 EC）；取消 = 恢复默认充电（关闭）；
//    · 「保存阈值」：在已启用状态下改阈值（内部先关闭再启用，严格时序）；
//    · 状态行显示：生效中/未启用 + 当前阈值（来源 EC 0x83A1/0x83A2/0x83C0）；
//    · 所有 EC 读写放后台线程；失败如实显示，⛔ 不假装成功。
//
// ⚠️ 写入只走 `BatteryLimitWriter`（白名单 4 地址 + 严格时序 + 失败不重试）✅

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using Microsoft.Win32;

namespace MechrevoLite;

/// <summary>⭐⭐ 电池页的宝龙达充电阈值区域（仅同款机型构建）✅。</summary>
internal sealed class BatteryLimitPanel : Panel
{
	private readonly NumericUpDown _lowBox;

	private readonly NumericUpDown _highBox;

	private readonly CheckBox _enableBox;

	private readonly Button _applyButton;

	private readonly Label _statusLabel;

	private readonly Label _hintLabel;

	private bool _busy;

	private bool _syncing;

	// ⭐ 当前是否“限充生效中”（来自 EC）；用于 SOC 安全提示（未启用时才提示 SOC≥high）✅
	private bool _active;

	public BatteryLimitPanel()
	{
		BackColor = Color.Transparent;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Dock = DockStyle.Fill;

		TableLayoutPanel grid = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 4,
			RowCount = 0,
			Margin = new Padding(0, 6, 0, 0)
		};
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));

		Label lowLabel = MakeLabel("起充 %（低）");
		_lowBox = MakeNumeric(1, 99, 60);
		Label highLabel = MakeLabel("停充 %（高）");
		_highBox = MakeNumeric(2, 100, 80);
		// ⭐ 机主要求：SOC ≥ high 时给**红色可操作提示**（开关/按钮仍可用，不是静默失败）✅
		_lowBox.ValueChanged += delegate { UpdateSocSafetyHint(); };
		_highBox.ValueChanged += delegate { UpdateSocSafetyHint(); };

		grid.RowCount = 4;
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.Controls.Add(lowLabel, 0, 0);
		grid.Controls.Add(_lowBox, 1, 0);
		grid.Controls.Add(highLabel, 2, 0);
		grid.Controls.Add(_highBox, 3, 0);

		_enableBox = new CheckBox
		{
			AutoSize = true,
			Text = "启用限制充电（接电源时限制充电上限）",
			Margin = new Padding(0, 8, 0, 0)
		};
		_enableBox.CheckedChanged += EnableBox_CheckedChanged;
		grid.Controls.Add(_enableBox, 0, 1);
		grid.SetColumnSpan(_enableBox, 4);

		_applyButton = new Button
		{
			AutoSize = true,
			Text = "保存阈值",
			Margin = new Padding(0, 8, 0, 0)
		};
		_applyButton.Click += ApplyButton_Click;
		grid.Controls.Add(_applyButton, 0, 2);
		grid.SetColumnSpan(_applyButton, 2);

		_statusLabel = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(520, 0),
			Margin = new Padding(0, 8, 0, 0),
			Text = "状态读取中…"
		};
		grid.Controls.Add(_statusLabel, 0, 3);
		grid.SetColumnSpan(_statusLabel, 4);

		_hintLabel = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(560, 0),
			ForeColor = Color.FromArgb(120, 120, 120),
			Margin = new Padding(0, 6, 0, 0),
			Text = "开启后接电源时会限制充电上限；建议保持接电源使用。"
		};

		Controls.Add(_hintLabel);
		Controls.Add(grid);
		_hintLabel.BringToFront();
		grid.BringToFront();
		RefreshFromEc();
		UpdateSocSafetyHint();
	}

	/// <summary>⭐ 能力判据：`UsesBaolongda` + 板号命中宝龙达白名单（MRID6/MRID6-*）+ EC 可读 ✅。</summary>
	internal static bool IsAvailable(MechrevoHw hw)
	{
		try
		{
			if (hw == null || !hw.UsesBaolongda)
			{
				return false;
			}
			// ⭐ 机主要求“板号以 MRID6 开头”；这里用同一份白名单纯函数（更严：排除 MRID60 之类假板号）✅
			if (!BoardWhitelist.Matches(ReadBaseBoardProduct()))
			{
				return false;
			}
			return BatteryLimitWriter.TryReadState(out _, out _, out _, out _);
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
			_statusLabel.Text = "状态读不到：" + detail + "（可稍后点「保存阈值」重试）";
			UpdateSocSafetyHint();
			return;
		}
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
		_statusLabel.ForeColor = SystemColors.ControlText;
		_statusLabel.Text = (active
			? "当前：✅ 限制充电生效中（停充 " + high + "% · 起充 " + low + "%）"
			: "当前：未启用（正常充电）")
			+ " · 来源 EC " + detail;
		UpdateSocSafetyHint();
	}

	/// <summary>
	/// ⭐⭐ 机主要求（UX）：未启用且 `SOC ≥ high` 时，给出**红色、可操作**提示；
	/// ⛔ 不静默失败、⛔ 不移除 SOC&lt;high 安全前置（启用仍会被 writer 拒绝，但用户知道下一步怎么做）✅
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
			Logger.WriteLine("[battery] 充电限制" + (want ? "启用" : "关闭") + "（UI）：ok=" + ok + " · " + detail);
			if (!ok)
			{
				_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
				_statusLabel.Text = "⛔ " + (want ? "启用失败（未重试）：" : "关闭失败（未重试）：") + Tail(detail);
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "⛔ 操作异常：" + ex.GetType().Name + " " + ex.Message;
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
			_statusLabel.Text = "⛔ 参数非法：要求 0 < 起充 < 停充 <= 100（当前 " + low + "/" + high + "）";
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
			Logger.WriteLine("[battery] 充电限制保存阈值（UI）：low=" + low + " high=" + high + " ok=" + ok + " · " + detail);
			if (!ok)
			{
				_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
				_statusLabel.Text = "⛔ 保存失败（未重试）：" + Tail(detail);
			}
			RefreshFromEc();
		}
		catch (Exception ex)
		{
			_statusLabel.ForeColor = Color.FromArgb(200, 40, 40);
			_statusLabel.Text = "⛔ 操作异常：" + ex.GetType().Name + " " + ex.Message;
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

	private static Label MakeLabel(string text)
	{
		return new Label
		{
			AutoSize = true,
			Text = text,
			Margin = new Padding(0, 4, 0, 0)
		};
	}

	private static NumericUpDown MakeNumeric(int min, int max, int value)
	{
		return new NumericUpDown
		{
			Minimum = min,
			Maximum = max,
			Value = value,
			Width = 72,
			Margin = new Padding(0, 4, 8, 0)
		};
	}

	private static string Tail(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "（无详情）";
		}
		string[] lines = text.Replace("\r", "").Split('\n');
		for (int i = lines.Length - 1; i >= 0; i--)
		{
			if (!string.IsNullOrWhiteSpace(lines[i]))
			{
				return lines[i].Trim();
			}
		}
		return text.Trim();
	}
}
