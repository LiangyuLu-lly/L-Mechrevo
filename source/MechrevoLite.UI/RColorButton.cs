using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RColorButton : RButton
{
	private Color? swatchColor;

	private Color? swatchColor2;

	private Rectangle swatch2Rect = Rectangle.Empty;

	private bool pressedSwatch2;

	public Color? SwatchColor
	{
		get
		{
			return swatchColor;
		}
		set
		{
			if (swatchColor != value)
			{
				swatchColor = value;
				Invalidate();
			}
		}
	}

	public Color? SwatchColor2
	{
		get
		{
			return swatchColor2;
		}
		set
		{
			if (swatchColor2 != value)
			{
				swatchColor2 = value;
				Invalidate();
			}
		}
	}

	public event EventHandler? Swatch2Click;

	protected override void OnPaint(PaintEventArgs pevent)
	{
		base.OnPaint(pevent);
		swatch2Rect = Rectangle.Empty;
		if (!swatchColor.HasValue && !swatchColor2.HasValue)
		{
			return;
		}
		Rectangle clientRectangle = ClientRectangle;
		int num = (int)Math.Round((float)clientRectangle.Height * 0.45f);
		int num2 = (clientRectangle.Height - num) / 2;
		if (num > 0)
		{
			// 2026-10-09（UI 打磨⑦，仅夜间）：色块圆角与按钮族一起收紧到小圆角（夜间 min(单位,8) ⇒ 96 DPI 下
			// 4px；日间原样返回 ⇒ 像素不变）。
			// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读绘制 DC 的 Graphics.DpiX。
			int radius = (int)Math.Round(UiDpi.Paint(this) / 192f * (float)UiVisualStyle.EffectiveCornerRadius(BorderRadius), MidpointRounding.AwayFromZero);
			Graphics graphics = pevent.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			int num3 = (int)Math.Round((float)num2 * 0.6f);
			int num4 = clientRectangle.Width - num2 - num;
			if (swatchColor.HasValue && swatchColor.Value.A > 0)
			{
				DrawSwatch(graphics, this, new Rectangle(num4, num2, num, num), radius, swatchColor.Value);
				num4 -= num3 + num;
			}
			if (swatchColor2.HasValue && swatchColor2.Value.A > 0)
			{
				swatch2Rect = new Rectangle(num4, num2, num, num);
				DrawSwatch(graphics, this, swatch2Rect, radius, swatchColor2.Value);
			}
		}
	}

	private static void DrawSwatch(Graphics g, Control owner, Rectangle rect, int radius, Color color)
	{
		int num = radius * 2;
		using GraphicsPath graphicsPath = new GraphicsPath();
		if (num > 0)
		{
			graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
		}
		else
		{
			graphicsPath.AddRectangle(rect);
		}
		using SolidBrush brush = new SolidBrush(color);
		// 2026-10-09（P0.5）：`new Pen(RForm.borderMain)` 的默认线宽＝硬编码 1 逻辑像素 → 唯一工厂
		//（100%→1、150%→2、200%→2 设备像素，与色块/按钮族同源）。
		using Pen pen = UiVisualStyle.OutlinePen(owner, RForm.borderMain, 1f, 1f, inset: false);
		g.FillPath(brush, graphicsPath);
		g.DrawPath(pen, graphicsPath);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		pressedSwatch2 = swatchColor2.HasValue && swatch2Rect.Contains(e.Location);
		base.OnMouseDown(e);
	}

	protected override void OnClick(EventArgs e)
	{
		if (pressedSwatch2)
		{
			pressedSwatch2 = false;
			Swatch2Click?.Invoke(this, e);
		}
		else
		{
			base.OnClick(e);
		}
	}
}
