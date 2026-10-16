using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MechrevoLite.Properties;

namespace MechrevoLite.UI;

public class RColorPicker : RForm
{
	private class Swatch : Panel
	{
		public bool Selected;

		public float UiScale = 1f;

		private Color color;

		public Color Color
		{
			get
			{
				return color;
			}
			set
			{
				color = value;
				BackColor = value;
				Invalidate();
			}
		}

		public Swatch()
		{
			Cursor = Cursors.Hand;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics graphics = e.Graphics;
			int num = ((!Selected) ? 1 : Math.Max(2, (int)Math.Round(2f * UiScale)));
			Rectangle clientRectangle = ClientRectangle;
			using SolidBrush brush = new SolidBrush(Selected ? UiVisualStyle.Accent : UiVisualStyle.Border);
			graphics.FillRectangle(brush, clientRectangle.X, clientRectangle.Y, clientRectangle.Width, num);
			graphics.FillRectangle(brush, clientRectangle.X, clientRectangle.Bottom - num, clientRectangle.Width, num);
			graphics.FillRectangle(brush, clientRectangle.X, clientRectangle.Y, num, clientRectangle.Height);
			graphics.FillRectangle(brush, clientRectangle.Right - num, clientRectangle.Y, num, clientRectangle.Height);
		}
	}

	private abstract class PickPanel : Panel
	{
		public float UiScale = 1f;

		private Pen? penWhite;

		protected Pen PenWhite => penWhite ?? (penWhite = new Pen(Color.White, 2f * UiScale));

		public event Action? Committed;

		protected PickPanel()
		{
			DoubleBuffered = true;
		}

		protected abstract void Pick(MouseEventArgs e);

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Pick(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (e.Button == MouseButtons.Left)
			{
				Pick(e);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			Committed?.Invoke();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				penWhite?.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	private class SVPanel : PickPanel
	{
		public float Hue;

		public float Sat;

		public float Val;

		private LinearGradientBrush? satBrush;

		private LinearGradientBrush? valBrush;

		private float satHue = -1f;

		public event Action<float, float>? Picked;

		public SVPanel()
		{
			Cursor = Cursors.Cross;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Rectangle clientRectangle = ClientRectangle;
			if (satBrush == null || satHue != Hue)
			{
				satBrush?.Dispose();
				satBrush = new LinearGradientBrush(clientRectangle, Color.White, HsvToRgb(Hue, 1f, 1f), LinearGradientMode.Horizontal);
				satHue = Hue;
			}
			if (valBrush == null)
			{
				valBrush = new LinearGradientBrush(clientRectangle, Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical);
			}
			graphics.FillRectangle(satBrush, clientRectangle);
			graphics.FillRectangle(valBrush, clientRectangle);
			int num = (int)(Sat * (float)clientRectangle.Width);
			int num2 = (int)((1f - Val) * (float)clientRectangle.Height);
			int num3 = (int)(9f * UiScale);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.DrawEllipse(PenWhite, num - num3, num2 - num3, num3 * 2, num3 * 2);
			graphics.DrawEllipse(Pens.Black, num - num3 - 1, num2 - num3 - 1, num3 * 2 + 2, num3 * 2 + 2);
		}

		protected override void Pick(MouseEventArgs e)
		{
			Sat = Math.Clamp((float)e.X / (float)Width, 0f, 1f);
			Val = Math.Clamp(1f - (float)e.Y / (float)Height, 0f, 1f);
			Invalidate();
			Picked?.Invoke(Sat, Val);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				satBrush?.Dispose();
				valBrush?.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	private class HuePanel : PickPanel
	{
		public float Hue;

		private static readonly Color[] Stops = new Color[7]
		{
			Color.Red,
			Color.Yellow,
			Color.Lime,
			Color.Cyan,
			Color.Blue,
			Color.Magenta,
			Color.Red
		};

		private LinearGradientBrush? hueBrush;

		public event Action<float>? Picked;

		public HuePanel()
		{
			Cursor = Cursors.Hand;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Rectangle clientRectangle = ClientRectangle;
			if (hueBrush == null)
			{
				Rectangle rect = clientRectangle;
				rect.Inflate(0, 1);
				hueBrush = new LinearGradientBrush(rect, Color.Black, Color.Black, LinearGradientMode.Vertical);
				float[] array = new float[Stops.Length];
				for (int i = 0; i < Stops.Length; i++)
				{
					array[i] = (float)i / (float)(Stops.Length - 1);
				}
				hueBrush.InterpolationColors = new ColorBlend
				{
					Colors = Stops,
					Positions = array
				};
			}
			graphics.FillRectangle(hueBrush, clientRectangle);
			int num = (int)(Hue / 360f * (float)clientRectangle.Height);
			int num2 = (int)(4f * UiScale);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.DrawRectangle(PenWhite, 1, num - num2, clientRectangle.Width - 3, num2 * 2);
			graphics.DrawRectangle(Pens.Black, 0, num - num2 - 1, clientRectangle.Width - 1, num2 * 2 + 2);
		}

		protected override void Pick(MouseEventArgs e)
		{
			Hue = Math.Clamp((float)e.Y / (float)Height, 0f, 1f) * 360f;
			Invalidate();
			Picked?.Invoke(Hue);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				hueBrush?.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	private const int Cols = 14;

	private const string CustomKey = "aura_color_custom";

	private static readonly Color[] Defaults = new Color[28]
	{
		C(16711680),
		C(16728064),
		C(16744448),
		C(16760576),
		C(16776960),
		C(8453888),
		C(65280),
		C(65408),
		C(65535),
		C(33023),
		C(255),
		C(8388863),
		C(16711935),
		C(16711808),
		C(16777215),
		C(14737632),
		C(12632256),
		C(10526880),
		C(8421504),
		C(6316128),
		C(4210752),
		C(0),
		C(16761024),
		C(16769216),
		C(16777152),
		C(12648384),
		C(12648447),
		C(12632319)
	};

	private readonly Color originalColor;

	private readonly bool allowRandom;

	private float scale;

	private float hue;

	private float sat;

	private float val;

	private SVPanel svPanel;

	private HuePanel huePanel;

	private Panel preview;

	private RTextBox hexBox;

	private Label rgbLabel;

	private Swatch? active;

	private readonly List<Color> customColors = new List<Color>();

	private Swatch[] customSwatches;

	private bool suppressHex;

	public Color Color { get; set; } = Color.Red;

	private int ActiveCustomIndex
	{
		get
		{
			int num = ((active == null) ? (-1) : Array.IndexOf(customSwatches, active));
			if (num >= customColors.Count)
			{
				return -1;
			}
			return num;
		}
	}

	public event Action<Color>? ColorChanged;

	private static Color C(int rgb)
	{
		return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
	}

	private int S(int v)
	{
		return (int)Math.Round((float)v * scale);
	}

	public RColorPicker(Color initial, bool allowRandom = false)
	{
		Color = initial;
		originalColor = initial;
		this.allowRandom = allowRandom;
		(float, float, float) tuple = RgbToHsv(initial);
		hue = tuple.Item1;
		sat = tuple.Item2;
		val = tuple.Item3;
		Font = UiVisualStyle.Font(9f);
		Text = Strings.Color;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		TopMost = true;
		StartPosition = FormStartPosition.CenterParent;
		AutoScaleMode = AutoScaleMode.None;
		AutoScroll = true;
		scale = (float)UiDpi.Layout(this) / 96f;
		LoadCustom();
		BuildLayout();
		InitTheme();
		RefreshCustom();
		UpdateAll(null);
		FormClosing += (object? s, FormClosingEventArgs e) =>
		{
			if (DialogResult != DialogResult.OK && ColorToColorRef(Color) != ColorToColorRef(originalColor))
			{
				ColorChanged?.Invoke(originalColor);
			}
		};
	}

	private void BuildLayout()
	{
		svPanel = new SVPanel
		{
			Location = new Point(S(12), S(12)),
			Size = new Size(S(200), S(200)),
			UiScale = scale
		};
		svPanel.Picked += (float s, float v) =>
		{
			sat = s;
			val = v;
			Color = HsvToRgb(hue, sat, val);
			Preview(svPanel);
		};
		svPanel.Committed += Commit;
		int num = 220;
		huePanel = new HuePanel
		{
			Location = new Point(S(num), S(12)),
			Size = new Size(S(18), S(200)),
			UiScale = scale
		};
		huePanel.Picked += (float h) =>
		{
			hue = h;
			Color = HsvToRgb(hue, sat, val);
			Preview(huePanel);
		};
		huePanel.Committed += Commit;
		int num2 = num + 18 + 16;
		preview = new Swatch
		{
			Location = new Point(S(num2), S(12)),
			Size = new Size(S(150), S(110)),
			UiScale = scale
		};
		Label label = new Label
		{
			Text = Strings.Hex,
			Location = new Point(S(num2), S(130)),
			AutoSize = true
		};
		hexBox = new RTextBox
		{
			Location = new Point(S(num2), S(157)),
			Size = new Size(S(150), S(23)),
			MaxLength = 7
		};
		hexBox.TextChanged += (object? s, EventArgs e) =>
		{
			if (!suppressHex)
			{
				ApplyHex(normalize: false);
			}
		};
		hexBox.Leave += (object? s, EventArgs e) =>
		{
			ApplyHex(normalize: true);
		};
		rgbLabel = new Label
		{
			Location = new Point(S(num2), S(186)),
			AutoSize = true,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Muted,
			Font = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point)
		};
		int num3 = 222;
		for (int num4 = 0; num4 < Defaults.Length; num4++)
		{
			AddSwatch(12 + num4 % 14 * 28, num3 + num4 / 14 * 28, 22, (Swatch x) =>
			{
				SetActive(x);
				Apply(x.Color);
			}).Color = Defaults[num4];
		}
		int num5 = (Defaults.Length + 14 - 1) / 14;
		int num6 = num3 + num5 * 28 + 8;
		customSwatches = new Swatch[14];
		for (int num7 = 0; num7 < 14; num7++)
		{
			int idx = num7;
			customSwatches[num7] = AddSwatch(12 + num7 * 28, num6, 22, (Swatch _) =>
			{
				OnCustomClick(idx);
			});
		}
		int num8 = num2 + 150 + 12;
		int num9 = num6 + 22 + 14;
		RButton rButton = new RButton
		{
			Text = Strings.Ok,
			Size = new Size(S(84), S(28)),
			DialogResult = DialogResult.OK
		};
		RButton rButton2 = new RButton
		{
			Text = Strings.Cancel,
			Size = new Size(S(84), S(28)),
			DialogResult = DialogResult.Cancel
		};
		rButton2.Location = new Point(S(num8 - 12 - 84), S(num9));
		rButton.Location = new Point(S(num8 - 12 - 84 - 8 - 84), S(num9));
		if (allowRandom)
		{
			RButton rButton3 = new RButton
			{
				Text = Strings.AuraRandomColor,
				Size = new Size(S(84), S(28)),
				Location = new Point(S(12), S(num9))
			};
			rButton3.Click += (object? s, EventArgs e) =>
			{
				Apply(Defaults[Random.Shared.Next(Defaults.Length)]);
			};
			Controls.Add(rButton3);
		}
		Controls.AddRange(svPanel, huePanel, preview, label, hexBox, rgbLabel, rButton, rButton2);
		AcceptButton = rButton;
		CancelButton = rButton2;
		ClientSize = new Size(S(num8), S(num9 + 28 + 12));
	}

	private Swatch AddSwatch(int x, int y, int size, Action<Swatch> onClick)
	{
		Swatch swatch = new Swatch
		{
			Location = new Point(S(x), S(y)),
			Size = new Size(S(size), S(size)),
			UiScale = scale
		};
		swatch.Click += (object? s, EventArgs e) =>
		{
			onClick(swatch);
		};
		Controls.Add(swatch);
		return swatch;
	}

	private void SetActive(Swatch sw)
	{
		if (active != null && active != sw)
		{
			active.Selected = false;
			active.Invalidate();
		}
		active = sw;
		sw.Selected = true;
		sw.Invalidate();
	}

	private void LoadCustom()
	{
		for (int i = 0; i < 14; i++)
		{
			customColors.Add(Color.White);
		}
		int num = 0;
		string[] array = (AppConfig.GetString("aura_color_custom", "") ?? "").Split('-');
		foreach (string s in array)
		{
			if (num < 14)
			{
				if (int.TryParse(s, out var result))
				{
					customColors[num++] = ColorRefToColor(result);
				}
				continue;
			}
			break;
		}
	}

	private void OnCustomClick(int idx)
	{
		SetActive(customSwatches[idx]);
		Apply(customColors[idx]);
	}

	private void RefreshCustom()
	{
		for (int i = 0; i < customSwatches.Length; i++)
		{
			customSwatches[i].Color = customColors[i];
		}
	}

	private static int ColorToColorRef(Color c)
	{
		return c.R | (c.G << 8) | (c.B << 16);
	}

	private static Color ColorRefToColor(int v)
	{
		return Color.FromArgb(255, v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF);
	}

	private void Preview(Control source)
	{
		UpdateAll(source);
		int activeCustomIndex = ActiveCustomIndex;
		if (activeCustomIndex >= 0)
		{
			customColors[activeCustomIndex] = Color;
			customSwatches[activeCustomIndex].Color = Color;
		}
		else if (active != null)
		{
			active.Selected = false;
			active.Invalidate();
			active = null;
		}
	}

	private void Commit()
	{
		if (ActiveCustomIndex >= 0)
		{
			AppConfig.Set("aura_color_custom", string.Join("-", customColors.Select(ColorToColorRef)));
		}
		ColorChanged?.Invoke(Color);
	}

	private void Apply(Color c)
	{
		Color = c;
		(hue, sat, val) = RgbToHsv(c);
		UpdateAll(null);
		Commit();
	}

	private void ApplyHex(bool normalize)
	{
		string text = hexBox.Text.Trim().TrimStart('#');
		if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, null, out var result))
		{
			Color = C(result);
			(hue, sat, val) = RgbToHsv(Color);
			hexBox.ForeColor = UiVisualStyle.Text;
			Preview(hexBox);
			Commit();
			return;
		}
		hexBox.ForeColor = UiVisualStyle.Danger;
		if (normalize)
		{
			hexBox.Text = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
		}
	}

	private void UpdateAll(Control? source)
	{
		svPanel.Hue = hue;
		svPanel.Sat = sat;
		svPanel.Val = val;
		huePanel.Hue = hue;
		if (source != svPanel)
		{
			svPanel.Invalidate();
		}
		if (source != huePanel)
		{
			huePanel.Invalidate();
		}
		preview.BackColor = Color;
		preview.Invalidate();
		if (source != hexBox)
		{
			suppressHex = true;
			hexBox.Text = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
			suppressHex = false;
		}
		hexBox.ForeColor = UiVisualStyle.Text;
		rgbLabel.Text = $"R {Color.R}   G {Color.G}   B {Color.B}";
	}

	private static Color HsvToRgb(float h, float s, float v)
	{
		float num = v * s;
		float num2 = num * (1f - Math.Abs(h / 60f % 2f - 1f));
		float num3 = v - num;
		float num4 = 0f;
		float num5 = 0f;
		float num6 = 0f;
		switch ((int)(h / 60f) % 6)
		{
		case 0:
			num4 = num;
			num5 = num2;
			break;
		case 1:
			num4 = num2;
			num5 = num;
			break;
		case 2:
			num5 = num;
			num6 = num2;
			break;
		case 3:
			num5 = num2;
			num6 = num;
			break;
		case 4:
			num4 = num2;
			num6 = num;
			break;
		case 5:
			num4 = num;
			num6 = num2;
			break;
		}
		return Color.FromArgb(255, (int)Math.Round((num4 + num3) * 255f), (int)Math.Round((num5 + num3) * 255f), (int)Math.Round((num6 + num3) * 255f));
	}

	private static (float h, float s, float v) RgbToHsv(Color color)
	{
		float num = (float)(int)color.R / 255f;
		float num2 = (float)(int)color.G / 255f;
		float num3 = (float)(int)color.B / 255f;
		float num4 = Math.Max(num, Math.Max(num2, num3));
		float num5 = Math.Min(num, Math.Min(num2, num3));
		float num6 = num4 - num5;
		float num7 = 0f;
		if (num6 > 0f)
		{
			if (num4 == num)
			{
				num7 = 60f * ((num2 - num3) / num6 % 6f);
			}
			else
			{
				num7 = ((num4 != num2) ? (60f * ((num - num2) / num6 + 4f)) : (60f * ((num3 - num) / num6 + 2f)));
			}
		}
		if (num7 < 0f)
		{
			num7 += 360f;
		}
		return (h: num7, s: (num4 == 0f) ? 0f : (num6 / num4), v: num4);
	}
}
