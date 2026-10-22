using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

/// <summary>
/// 2026-10-22（机主：底下边栏改成圆角矩形，整个底色半透明）：
/// 底栏专用面板 —— 自绘「背景图切片 + 半透明窗口色圆角矩形 + 一圈淡边」，子控件画在上面。
/// </summary>
public sealed class RFooterPanel : Panel
{
	public RFooterPanel()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, value: true);
		BackColor = Color.Transparent;
	}

	protected override void OnControlAdded(ControlEventArgs e)
	{
		base.OnControlAdded(e);
		// 2026-10-22（机主：底栏有黑色矩形）：底栏内的容器/标签统一透明，让半透明底透出来。
		MakeTranslucent(e.Control);
	}

	private static void MakeTranslucent(Control control)
	{
		if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is Label || control is TableLayoutPanel)
		{
			control.BackColor = Color.Transparent;
		}
		foreach (Control child in control.Controls)
		{
			MakeTranslucent(child);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		UiVisualStyle.DrawFormBackgroundSlice(graphics, this);
		float scale = UiDpi.Paint(this) / 96f;
		int inset = Math.Max(1, (int)Math.Round(scale));
		int radius = Math.Max(4, (int)Math.Round(10f * scale));
		Rectangle bounds = new Rectangle(inset, inset, Math.Max(2, Width - inset * 2), Math.Max(2, Height - inset * 2));
		using GraphicsPath path = RoundedRect(bounds, radius);
		Color fill = UiVisualStyle.FormHasBackgroundImage(this)
			? Color.FromArgb(150, UiVisualStyle.Window.R, UiVisualStyle.Window.G, UiVisualStyle.Window.B)
			: UiVisualStyle.Window;
		using (SolidBrush brush = new SolidBrush(fill))
		{
			graphics.FillPath(brush, path);
		}
		using (Pen pen = new Pen(UiVisualStyle.TranslucentFor(this, UiVisualStyle.ButtonEdge, 160), Math.Max(1f, scale)))
		{
			graphics.DrawPath(pen, path);
		}
	}

	private static GraphicsPath RoundedRect(Rectangle rect, int radius)
	{
		GraphicsPath path = new GraphicsPath();
		int size = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
		if (size <= 0)
		{
			path.AddRectangle(rect);
			return path;
		}
		path.AddArc(rect.X, rect.Y, size, size, 180f, 90f);
		path.AddArc(rect.Right - size, rect.Y, size, size, 270f, 90f);
		path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0f, 90f);
		path.AddArc(rect.X, rect.Bottom - size, size, size, 90f, 90f);
		path.CloseFigure();
		return path;
	}
}
