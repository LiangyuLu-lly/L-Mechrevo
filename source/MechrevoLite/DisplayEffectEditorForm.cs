using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.UI;

namespace MechrevoLite
{
	public sealed class DisplayEffectEditorForm : Form
	{
		private RSlider _brightness;
		private RSlider _colorTemp;
		private RSlider _saturation;
		private RSlider _gamma;
		private RSlider _r;
		private RSlider _g;
		private RSlider _b;
		private RSlider _contrast;
		private Label _brightnessValue;
		private Label _colorTempValue;
		private Label _saturationValue;
		private Label _gammaValue;
		private Label _rValue;
		private Label _gValue;
		private Label _bValue;
		private Label _contrastValue;

		private string _mode = "";
		private bool _syncing;
		private DateTime _lastUi = DateTime.MinValue;
		private readonly System.Windows.Forms.Timer _commitTimer = new System.Windows.Forms.Timer { Interval = 250 };
		private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };

		public DisplayEffectEditorForm(string mode)
		{
			_mode = mode ?? "";
			BuildUi();
			_commitTimer.Tick += async (object? _, EventArgs _) =>
			{
				_commitTimer.Stop();
				await CommitAsync();
			};
			_statusTimer.Tick += (object? _, EventArgs _) => SyncFromHardware();
			Load += async (object? _, EventArgs _) =>
			{
				MechrevoService? svc = Program.service;
				if (svc != null)
				{
					await svc.RequestDisplayEffectStatus();
					await Task.Delay(300);
				}
				SyncFromHardware();
				_statusTimer.Start();
			};
			FormClosed += (object? _, FormClosedEventArgs _) => _statusTimer.Stop();
		}

		private void BuildUi()
		{
			Text = "色彩增强";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(470, 560);
			Font = UiVisualStyle.Font(8.5f);
			BackColor = UiVisualStyle.Window;
			ForeColor = UiVisualStyle.Text;

			Label title = new Label
			{
				Text = "色彩增强",
				ForeColor = UiVisualStyle.Text,
				AutoSize = true,
				Location = new Point(24, 14),
				Font = UiVisualStyle.Font(9f, FontStyle.Bold)
			};

			RButton resetBtn = new RButton
			{
				Tag = "mini-chip",
				Text = "恢复默认",
				ForeColor = UiVisualStyle.Muted,
				BackColor = UiVisualStyle.Window,
				BorderColor = UiVisualStyle.Border,
				BorderRadius = 12,
				FlatStyle = FlatStyle.Flat,
				AutoSize = false,
				Size = new Size(76, 22),
				Location = new Point(340, 14),
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiVisualStyle.Font(8f)
			};
			resetBtn.FlatAppearance.BorderSize = 0;
			resetBtn.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
			resetBtn.Click += async (object? _, EventArgs _) =>
			{
				MechrevoService? svc = Program.service;
				if (_mode.Length == 0 || svc == null)
				{
					return;
				}
				resetBtn.Enabled = false;
				if (await svc.ResetDisplayEffectProfile(_mode))
				{
					await svc.RequestDisplayEffectStatus();
					SyncFromHardware();
				}
				resetBtn.Enabled = true;
			};

			BuildSliderRow(24, 56, "亮度", ref _brightness, ref _brightnessValue, 0, 100, 50, static v => v + "%");
			BuildSliderRow(24, 112, "色温", ref _colorTemp, ref _colorTempValue, 30, 100, 65, static v => (v * 100) + "K");
			BuildSliderRow(24, 168, "饱和度", ref _saturation, ref _saturationValue, 5, 25, 15, static v => (v * 0.1).ToString("0.0"));
			BuildSliderRow(24, 224, "伽玛", ref _gamma, ref _gammaValue, 50, 200, 100, static v => (v * 0.01).ToString("0.00"));
			BuildSliderRow(24, 280, "红", ref _r, ref _rValue, 0, 255, 128, static v => v.ToString());
			BuildSliderRow(24, 336, "绿", ref _g, ref _gValue, 0, 255, 128, static v => v.ToString());
			BuildSliderRow(24, 392, "蓝", ref _b, ref _bValue, 0, 255, 128, static v => v.ToString());
			BuildSliderRow(24, 448, "对比度", ref _contrast, ref _contrastValue, 5, 20, 10, static v => (v * 0.1).ToString("0.0"));

			RButton closeBtn = new RButton
			{
				Text = "关闭",
				BackColor = UiVisualStyle.Accent,
				ForeColor = Color.White,
				AutoSize = false,
				Size = new Size(80, 28),
				Location = new Point(195, 512),
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiVisualStyle.Font(8.5f)
			};
			closeBtn.Click += (object? _, EventArgs _) => Close();

			Controls.Add(title);
			Controls.Add(resetBtn);
			Controls.Add(closeBtn);
		}

		private void BuildSliderRow(int x, int y, string text, ref RSlider slider, ref Label value, int min, int max, int def, Func<int, string> format)
		{
			Label lab = new Label
			{
				Text = text,
				ForeColor = UiVisualStyle.Muted,
				AutoSize = false,
				Size = new Size(64, 28),
				Location = new Point(x, y),
				TextAlign = ContentAlignment.MiddleLeft,
				Font = UiVisualStyle.Font(8f)
			};
			slider = new RSlider
			{
				Minimum = min,
				Maximum = max,
				Value = def,
				Size = new Size(260, 26),
				Location = new Point(x + 68, y),
				Margin = Padding.Empty
			};
			value = new Label
			{
				Text = format(def),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = false,
				Size = new Size(56, 28),
				Location = new Point(x + 330, y),
				TextAlign = ContentAlignment.MiddleRight,
				Font = UiVisualStyle.Font(8f)
			};
			RSlider s = slider;
			Label v = value;
			Func<int, string> fmt = format;
			s.ValueChanged += (object? _, EventArgs _) =>
			{
				v.Text = fmt(s.Value);
				_lastUi = DateTime.UtcNow;
				if (!_syncing && !Program.UiAuditMode)
				{
					_commitTimer.Stop();
					_commitTimer.Start();
				}
			};
			Controls.Add(lab);
			Controls.Add(slider);
			Controls.Add(value);
		}

		private DisplayEffectProfile ResolveProfile(string mode)
		{
			MechrevoHw? hw = Program.hw;
			if (hw != null && hw.DisplayEffectProfiles.TryGetValue(mode, out DisplayEffectProfile? p))
			{
				return p;
			}
			return new DisplayEffectProfile
			{
				Name = mode,
				Brightness = 50,
				ColorTemp = 6500,
				ColorR = 128f,
				ColorG = 128f,
				ColorB = 128f,
				VibrantValue = 1f,
				Constrast = 1f,
				Gamma = 1f
			};
		}

		private void ApplyProfileToSliders(DisplayEffectProfile p)
		{
			if (p == null)
			{
				return;
			}
			_syncing = true;
			try
			{
				_brightness.Value = Math.Clamp(p.Brightness, 0, 100);
				_colorTemp.Value = Math.Clamp(p.ColorTemp / 100, 30, 100);
				_r.Value = Math.Clamp((int)Math.Round(p.ColorR), 0, 255);
				_g.Value = Math.Clamp((int)Math.Round(p.ColorG), 0, 255);
				_b.Value = Math.Clamp((int)Math.Round(p.ColorB), 0, 255);
				_saturation.Value = Math.Clamp((int)Math.Round(p.VibrantValue * 10f), 5, 25);
				_contrast.Value = Math.Clamp((int)Math.Round(p.Constrast * 10f), 5, 20);
				_gamma.Value = Math.Clamp((int)Math.Round(p.Gamma * 100f), 50, 200);
				UpdateValueLabels();
			}
			finally
			{
				_syncing = false;
			}
		}

		private void UpdateValueLabels()
		{
			_brightnessValue.Text = _brightness.Value + "%";
			_colorTempValue.Text = (_colorTemp.Value * 100) + "K";
			_saturationValue.Text = (_saturation.Value * 0.1).ToString("0.0");
			_gammaValue.Text = (_gamma.Value * 0.01).ToString("0.00");
			_rValue.Text = _r.Value.ToString();
			_gValue.Text = _g.Value.ToString();
			_bValue.Text = _b.Value.ToString();
			_contrastValue.Text = (_contrast.Value * 0.1).ToString("0.0");
		}

		private async Task CommitAsync()
		{
			MechrevoService? svc = Program.service;
			if (svc == null || _syncing || _mode.Length == 0)
			{
				return;
			}
			DisplayEffectProfile profile = new DisplayEffectProfile
			{
				Name = _mode,
				Brightness = _brightness.Value,
				ColorTemp = _colorTemp.Value * 100,
				ColorR = _r.Value,
				ColorG = _g.Value,
				ColorB = _b.Value,
				VibrantValue = _saturation.Value * 0.1f,
				Constrast = _contrast.Value * 0.1f,
				Gamma = _gamma.Value * 0.01f
			};
			await svc.ApplyDisplayEffectProfile(profile);
		}

		private void SyncFromHardware()
		{
			MechrevoHw? hw = Program.hw;
			if (hw == null)
			{
				return;
			}
			if ((DateTime.UtcNow - _lastUi).TotalSeconds < 3.0)
			{
				return;
			}
			if (_mode.Length == 0 && hw.DisplayEffectMode.Length > 0)
			{
				_mode = hw.DisplayEffectMode;
			}
			if (_mode.Length == 0)
			{
				return;
			}
			_syncing = true;
			try
			{
				if (hw.DisplayEffectProfiles.TryGetValue(_mode, out DisplayEffectProfile? prof))
				{
					ApplyProfileToSliders(prof);
				}
			}
			finally
			{
				_syncing = false;
			}
		}
	}
}
