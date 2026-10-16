using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Display;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite
{
	/// <summary>屏幕校色设置弹窗：开机自动应用 + 自定义 ICC。</summary>
	public sealed class CalibrationStartupForm : Form
	{
		private readonly RCheckBox _startupSwitch;
		private readonly RCheckBox _customSwitch;
		private readonly RComboBox _customIccCombo;
		private readonly RButton _importBtn;

		public CalibrationStartupForm()
		{
			Text = Strings.ColorCalibration;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(580, 300);
			Font = UiVisualStyle.Font(8.5f);
			BackColor = UiVisualStyle.Window;
			ForeColor = UiVisualStyle.Text;

			Label title = new Label
			{
				Text = Strings.ColorCalibration,
				ForeColor = UiVisualStyle.Text,
				AutoSize = true,
				Location = new Point(24, 16),
				Font = UiVisualStyle.Font(9f, FontStyle.Bold)
			};

			_startupSwitch = new RCheckBox
			{
				Text = Strings.CalibApplyOnStartup,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window,
				Checked = AppConfig.Is("calib_apply_on_startup"),
				AutoSize = true,
				Location = new Point(26, 66),
				Cursor = Cursors.Hand
			};
			_startupSwitch.CheckedChanged += (object? _, EventArgs _) =>
			{
				AppConfig.Set("calib_apply_on_startup", _startupSwitch.Checked ? 1 : 0);
			};

			Label hint = new Label
			{
				Text = Strings.CalibApplyOnStartupHint,
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				MaximumSize = new Size(430, 0),
				Location = new Point(26, 104),
				Font = UiVisualStyle.Font(7.5f)
			};

			Label customCaption = new Label
			{
				Text = Strings.CustomCalibration,
				ForeColor = UiVisualStyle.Text,
				AutoSize = true,
				Location = new Point(24, 158),
				Font = UiVisualStyle.Font(9f, FontStyle.Bold)
			};

			_customSwitch = new RCheckBox
			{
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window,
				Checked = AppConfig.Is("calib_custom_on"),
				AutoSize = true,
				Location = new Point(26, 190),
				Cursor = Cursors.Hand
			};
			_customSwitch.CheckedChanged += (object? _, EventArgs _) =>
			{
				bool on = _customSwitch.Checked;
				AppConfig.Set("calib_custom_on", on ? 1 : 0);
				_customIccCombo.Enabled = on;
				_importBtn.Enabled = on;
				if (!on)
				{
					// 关闭自定义：取消所有已应用的 ICC；若主界面正选中自定义则回退默认档位（后台执行，避免 WCS 阻塞 UI）
					Task.Run(() => IccHelper.ClearAllIcc());
					if (AppConfig.Get("calib_mode") == 999)
					{
						AppConfig.Set("calib_mode", 1);
					}
				}
			};

			// 2026-10-09（0.291 beta22）：本窗体是 `sealed class ... : Form`（**不是 RForm**），既不跑
			// `RForm.InitTheme` 里的 `ControlHelper.AdjustControls`，也不在 `UiVisualStyle.ApplyTree` 的
			// 覆盖范围内 —— 于是这个下拉在 0.291 之前一直是**原生 ComboBox**：按钮格与展开列表都由系统绘制
			// （浅色/白方块 + 实心三角），是全仓唯一"绕过统一样式"的下拉（见《UI样式规范》§6.4/§7.2 第 9 条）。
			// 本轮改为 RComboBox，并在构造后**显式**走一次唯一入口 `UiVisualStyle.StyleComboBox`
			// （该入口只改外观属性 + SetWindowTheme，不碰 Items/SelectedIndex/事件/DropDownWidth/尺寸）；
			// RComboBox 自身在首绘时还有一次自愈（StyleComboBoxIfStale），因此即便主题后切换也不会漏色。
			_customIccCombo = new RComboBox
			{
				Cursor = Cursors.Hand,
				DropDownStyle = ComboBoxStyle.DropDownList,
				FlatStyle = FlatStyle.Flat,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Input,
				Font = UiVisualStyle.Font(8f),
				DropDownWidth = 420,
				Location = new Point(110, 188),
				Size = new Size(320, 26)
			};
			UiVisualStyle.StyleComboBox(_customIccCombo);
			_customIccCombo.SelectedIndexChanged += (object? _, EventArgs _) =>
			{
				if (_customIccCombo.SelectedItem is string name)
				{
					AppConfig.Set("calib_custom_icc", name);
				}
			};
			RefreshIccList();

			_importBtn = new RButton
			{
				Text = Strings.Import,
				BackColor = UiVisualStyle.Accent,
				ForeColor = Color.White,
				AutoSize = false,
				Size = new Size(72, 32),
				Location = new Point(440, 184),
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiVisualStyle.Font(8f)
			};
			_importBtn.Click += (object? _, EventArgs _) => ImportIccFile();

			Label customHint = new Label
			{
				Text = Strings.CustomCalibrationHint,
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				MaximumSize = new Size(430, 0),
				Location = new Point(26, 234),
				Font = UiVisualStyle.Font(7.5f)
			};

			bool customOn = _customSwitch.Checked;
			_customIccCombo.Enabled = customOn;
			_importBtn.Enabled = customOn;

			Controls.Add(title);
			Controls.Add(_startupSwitch);
			Controls.Add(hint);
			Controls.Add(customCaption);
			Controls.Add(_customSwitch);
			Controls.Add(_customIccCombo);
			Controls.Add(_importBtn);
			Controls.Add(customHint);
		}

		private void RefreshIccList()
		{
			_customIccCombo.Items.Clear();
			foreach (string name in IccHelper.ListCustomIccFiles())
			{
				_customIccCombo.Items.Add(name);
			}
			string saved = AppConfig.GetString("calib_custom_icc") ?? "";
			if (!string.IsNullOrEmpty(saved))
			{
				int idx = _customIccCombo.Items.IndexOf(saved);
				if (idx >= 0)
				{
					_customIccCombo.SelectedIndex = idx;
					return;
				}
			}
			if (_customIccCombo.Items.Count > 0)
			{
				_customIccCombo.SelectedIndex = 0;
			}
		}

		private void ImportIccFile()
		{
			using OpenFileDialog dialog = new OpenFileDialog
			{
				Title = Strings.CustomCalibration,
				Filter = "ICC 配置文件 (*.icc;*.icm)|*.icc;*.icm",
				CheckFileExists = true
			};
			if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.FileName))
			{
				return;
			}
			string? name = IccHelper.ImportIcc(dialog.FileName);
			if (name == null)
			{
				return;
			}
			RefreshIccList();
			AppConfig.Set("calib_custom_icc", name);
		}
	}
}
