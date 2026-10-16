using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MechrevoLite.UI;

public static class ControlHelper
{
	private static bool _invert = false;

	private static bool _darkMode = false;

	private static float _scale = 1f;

	private const float GradientHeightFraction = 0.3f;

	private const float LightGradientHeightFraction = 0.9f;

	private const int TopFadeAlpha = 64;

	private static readonly ImageAttributes _invertAttributes = CreateInvertAttributes();

	public static float Scale => _scale;

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

	public static void Adjust(RForm container, bool invert = false)
	{
		container.BackColor = RForm.formBack;
		container.ForeColor = RForm.foreMain;
		_invert = invert;
		_darkMode = container.darkTheme;
		AdjustControls(container.Controls);
		_invert = false;
	}

	public static void Resize(RForm container, float baseScale = 2f)
	{
		_scale = GetDpiScale(container).Value / baseScale;
		if ((double)Math.Abs(_scale - 1f) > 0.2)
		{
			ResizeControls(container.Controls);
		}
	}

	private static void ResizeControls(Control.ControlCollection controls)
	{
		foreach (Control control in controls)
		{
			if (control is RButton { Image: not null } rButton)
			{
				rButton.Image = ResizeImage(rButton.Image);
			}
			ResizeControls(control.Controls);
		}
	}

	private static void AdjustControls(Control.ControlCollection controls)
	{
		foreach (Control control in controls)
		{
			AdjustControls(control.Controls);
			if (control is RButton rButton)
			{
				rButton.BackColor = (rButton.Secondary ? RForm.buttonSecond : RForm.buttonMain);
				rButton.ForeColor = RForm.foreMain;
				rButton.FlatStyle = FlatStyle.Flat;
				if (!rButton.Borderless)
				{
					rButton.FlatAppearance.BorderColor = (rButton.Secondary ? RForm.borderSecond : RForm.borderMain);
				}
				if (rButton.Image != null && _invert)
				{
					rButton.Image = AdjustImage(rButton.Image);
				}
			}
			if (control is PictureBox { BackgroundImage: not null } pictureBox && _invert)
			{
				pictureBox.BackgroundImage = AdjustImage(pictureBox.BackgroundImage);
			}
			if (control is RComboBox rComboBox)
			{
				// 2026-10-09（UI 打磨⑥，用户原话「学习一下它的下拉框样式我的明显糙不少」）：
				// 这里原来是**第二条**下拉框上色路径 —— RForm.InitTheme 会在 UiVisualStyle.ApplyTree
				// **之前**跑，把 RComboBox 的底/边/箭头改写成 RForm.buttonMain / borderMain / foreMain
				// 这套旧调色板值，盖掉 ApplyTree 刚下发的 Input / BorderStrong / Muted；两者谁后跑谁生效，
				// 于是界面上出现「有的下拉深色、有的白底」这种不统一。现在 RComboBox 的上色**只**由
				// UiVisualStyle.StyleComboBox 负责（ApplyTree 与 RComboBox 自身的首绘自愈各调一次），
				// 这里只保留「日间/夜间都成立」的两项 ForeColor，不再动底色与边色。
				// ⚠️ 原生 ComboBox 不受影响；RComboBox 的 Items/事件/尺寸/下拉宽度也一概不动。
				rComboBox.ForeColor = RForm.foreMain;
			}
			if (control is RNumericUpDown rNumericUpDown)
			{
				rNumericUpDown.ApplyTheme(_darkMode);
			}
			else if (control is NumericUpDown numericUpDown)
			{
				numericUpDown.ForeColor = RForm.foreMain;
				numericUpDown.BackColor = RForm.buttonMain;
			}
			if (control is RTextBox rTextBox)
			{
				rTextBox.ApplyTheme(_darkMode);
			}
			if (control is GroupBox groupBox)
			{
				groupBox.ForeColor = RForm.foreMain;
			}
			if (control is Panel panel && panel.Name.Contains("Header"))
			{
				panel.BackColor = RForm.buttonSecond;
			}
			if (!(control is CheckBox checkBox))
			{
				continue;
			}
			if (checkBox.BackColor != RForm.formBack)
			{
				checkBox.BackColor = RForm.buttonSecond;
				if (checkBox is RCheckBox)
				{
					checkBox.FlatAppearance.BorderColor = RForm.borderSecond;
				}
			}
			SetWindowTheme(checkBox.Handle, _darkMode ? "DarkMode_Explorer" : "Explorer", null);
		}
	}

	public static Lazy<float> GetDpiScale(Control control)
	{
		return new Lazy<float>(() => (float)UiDpi.Layout(control) / 96f);
	}

	private static Image ResizeImage(Image image)
	{
		return ResizeImage(image, _scale);
	}

	public static Image ResizeImage(Image image, float scale)
	{
		if ((double)Math.Abs(scale - 1f) < 0.1)
		{
			return image;
		}
		Size size = new Size((int)((float)image.Width * scale), (int)((float)image.Height * scale));
		Bitmap bitmap = new Bitmap(size.Width, size.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.DrawImage(image, new Rectangle(default, size));
		return bitmap;
	}

	public static void DrawGradientBorder(Graphics g, Rectangle bounds, Color sideColor, int radius, float strokeWidth = 1f, PenAlignment alignment = PenAlignment.Center, float topLighten = 0.1f)
	{
		Color color = ((!_darkMode && strokeWidth <= 1f) ? Color.FromArgb(64, sideColor) : Color.FromArgb(sideColor.A, (int)((float)(int)sideColor.R + (float)(255 - sideColor.R) * topLighten), (int)((float)(int)sideColor.G + (float)(255 - sideColor.G) * topLighten), (int)((float)(int)sideColor.B + (float)(255 - sideColor.B) * topLighten)));
		float num = Math.Max(1f, strokeWidth);
		float num2 = (float)Math.Round((float)bounds.Height * (_darkMode ? 0.3f : 0.9f));
		float num3 = (float)bounds.Y - strokeWidth;
		float num4 = (float)(bounds.Y + bounds.Height) + strokeWidth;
		float num5 = num4 - num3;
		float num6 = Math.Max(0f, Math.Min(0.98f, (strokeWidth + num) / num5));
		float num7 = Math.Max(num6 + 0.01f, Math.Min(1f, (strokeWidth + num + num2) / num5));
		using GraphicsPath path = RComboBox.RoundedRect(bounds, radius, radius);
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new PointF(0f, num3), new PointF(0f, num4), color, sideColor);
		linearGradientBrush.InterpolationColors = new ColorBlend(4)
		{
			Colors = new Color[4] { color, color, sideColor, sideColor },
			Positions = new float[4] { 0f, num6, num7, 1f }
		};
		using Pen pen = new Pen(linearGradientBrush, strokeWidth)
		{
			Alignment = alignment
		};
		SmoothingMode smoothingMode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.DrawPath(pen, path);
		g.SmoothingMode = smoothingMode;
	}

	private static ImageAttributes CreateInvertAttributes()
	{
		ColorMatrix colorMatrix = new ColorMatrix(new float[5][]
		{
			new float[5] { -1f, 0f, 0f, 0f, 0f },
			new float[5] { 0f, -1f, 0f, 0f, 0f },
			new float[5] { 0f, 0f, -1f, 0f, 0f },
			new float[5] { 0f, 0f, 0f, 1f, 0f },
			new float[5] { 1f, 1f, 1f, 0f, 1f }
		});
		ImageAttributes imageAttributes = new ImageAttributes();
		imageAttributes.SetColorMatrix(colorMatrix);
		return imageAttributes;
	}

	private static Image AdjustImage(Image image)
	{
		Bitmap bitmap = new Bitmap(image.Width, image.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, _invertAttributes);
		return bitmap;
	}

	public static Image TintImage(Image image, Color tintColor)
	{
		Bitmap bitmap = new Bitmap(image);
		for (int i = 0; i <= bitmap.Height - 1; i++)
		{
			for (int j = 0; j <= bitmap.Width - 1; j++)
			{
				bitmap.SetPixel(j, i, Color.FromArgb(bitmap.GetPixel(j, i).A, tintColor.R, tintColor.G, tintColor.B));
			}
		}
		return bitmap;
	}

	public static Image RecolorDarkPixels(Image image, Color targetColor, byte luminanceThreshold = 128)
	{
		Bitmap bitmap = new Bitmap(image);
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				Color pixel = bitmap.GetPixel(j, i);
				if (pixel.A != 0 && (pixel.R + pixel.G + pixel.B) / 3 < luminanceThreshold)
				{
					bitmap.SetPixel(j, i, Color.FromArgb(pixel.A, targetColor));
				}
			}
		}
		return bitmap;
	}

	public static Image OverlayBadge(Image baseImage, Image badge, Color circleColor, float badgeScale = 0.5f, float shiftFraction = 0.18f, int? iconWidth = null, int? iconHeight = null)
	{
		int num = iconWidth ?? baseImage.Width;
		int num2 = iconHeight ?? baseImage.Height;
		int num3 = (int)((float)num * badgeScale);
		int num4 = (int)((float)num3 * shiftFraction);
		int width = Math.Max(baseImage.Width, num + num4);
		int height = Math.Max(baseImage.Height, num2 + num4);
		Bitmap bitmap = new Bitmap(width, height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		using Bitmap image = (Bitmap)RecolorDarkPixels(badge, circleColor, 128);
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.DrawImage(baseImage, 0, 0, baseImage.Width, baseImage.Height);
		int x = num - num3 + num4;
		int y = num2 - num3 + num4;
		graphics.DrawImage(image, x, y, num3, num3);
		return bitmap;
	}

	public static Image OverlayChargeBars(Image baseImage, int level, int max, Color color, int? iconWidth = null, int? iconHeight = null)
	{
		if (max <= 0)
		{
			return baseImage;
		}
		int num = iconWidth ?? baseImage.Width;
		int num2 = iconHeight ?? baseImage.Height;
		float num3 = (float)num / 48f;
		int num4 = Math.Max(2, (int)Math.Round(10f * num3));
		int num5 = Math.Max(1, (int)Math.Round(4f * num3));
		int num6 = Math.Max(1, (int)Math.Round(2f * num3));
		int num7 = num6 * (max - 1);
		int num8 = num5 * max + num7;
		int num9 = (num - num8) / 2;
		int num10 = ((baseImage.Height > num2) ? (baseImage.Height + Math.Max(2, (int)Math.Round(2f * num3))) : (num2 + Math.Max(2, (int)Math.Round(3f * num3))));
		int height = Math.Max(baseImage.Height, num10 + num4);
		Bitmap bitmap = new Bitmap(Math.Max(baseImage.Width, num), height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		using SolidBrush solidBrush = new SolidBrush(color);
		using SolidBrush solidBrush2 = new SolidBrush(Color.FromArgb(72, color));
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.DrawImage(baseImage, 0, 0, baseImage.Width, baseImage.Height);
		for (int i = 0; i < max; i++)
		{
			graphics.FillRectangle(rect: new Rectangle(num9 + i * (num5 + num6), num10, num5, num4), brush: (i < level) ? solidBrush : solidBrush2);
		}
		return bitmap;
	}
}
