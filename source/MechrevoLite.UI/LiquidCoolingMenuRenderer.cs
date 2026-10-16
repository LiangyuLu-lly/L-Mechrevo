using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class LiquidCoolingMenuRenderer : ToolStripProfessionalRenderer
{
	public LiquidCoolingMenuRenderer()
		: base(new ProfessionalColorTable())
	{
	}

	private static int HalfLogicalPx(Graphics graphics, int logicalPx)
	{
		return (int)Math.Round(UiDpi.Paint(graphics) / 192f * (float)(logicalPx * 2));
	}

	private static GraphicsPath RoundedPath(Rectangle rect, int radius)
	{
		int num = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
		GraphicsPath graphicsPath = new GraphicsPath();
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			return graphicsPath;
		}
		graphicsPath.AddArc(rect.X, rect.Y, num * 2, num * 2, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num * 2, rect.Y, num * 2, num * 2, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num * 2, rect.Bottom - num * 2, num * 2, num * 2, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static bool IsHeader(ToolStripItem item)
	{
		return object.Equals(item.Tag, "lc-header");
	}

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(UiVisualStyle.Input);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(UiVisualStyle.Input);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
		using (SolidBrush brush = new SolidBrush(UiVisualStyle.Input))
		{
			e.Graphics.FillRectangle(brush, rect);
		}
		if (IsHeader(e.Item) || !e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed))
		{
			return;
		}
		Color color = (e.Item.Pressed ? UiVisualStyle.Border : UiVisualStyle.SurfaceRaised);
		using GraphicsPath path = RoundedPath(Rectangle.Inflate(rect, -2, -1), HalfLogicalPx(e.Graphics, 6));
		using SolidBrush brush2 = new SolidBrush(color);
		SmoothingMode smoothingMode = e.Graphics.SmoothingMode;
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.FillPath(brush2, path);
		e.Graphics.SmoothingMode = smoothingMode;
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		if (IsHeader(e.Item))
		{
			e.TextColor = UiVisualStyle.Muted;
			e.TextFont = UiVisualStyle.Font(8.5f);
		}
		else
		{
			e.TextColor = (e.Item.Enabled ? UiVisualStyle.Text : UiVisualStyle.Muted);
		}
		base.OnRenderItemText(e);
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		int num = HalfLogicalPx(e.Graphics, 8);
		int num2 = e.Item.Height / 2;
		// 2026-10-09（P0.5）：`new Pen(color)` 的默认线宽也是硬编码 1 逻辑像素，统一走唯一工厂
		// UiVisualStyle.OutlinePen(Graphics 版) —— 菜单渲染器没有控件实例，故按**菜单的绘制 DC** 的 DPI 取整
		//（100%→1、150%→2、200%→2 设备像素），不再"任何 DPI 下都恒定 1 设备像素"。
		using Pen pen = UiVisualStyle.OutlinePen(e.Graphics, UiVisualStyle.Border, 1f, 1f, inset: false);
		e.Graphics.DrawLine(pen, num, num2, Math.Max(num, e.Item.Width - num), num2);
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
		Rectangle rect = new Rectangle(Point.Empty, e.ToolStrip.Size);
		if (rect.Width <= 2 || rect.Height <= 2)
		{
			return;
		}
		using GraphicsPath path = RoundedPath(Rectangle.Inflate(rect, -1, -1), HalfLogicalPx(e.Graphics, 6));
		// 2026-10-09（P0.5）：同上，默认 1 逻辑像素 → 唯一工厂（按绘制 DC 的 DPI 取整）。
		using Pen pen = UiVisualStyle.OutlinePen(e.Graphics, UiVisualStyle.Border, 1f, 1f, inset: false);
		SmoothingMode smoothingMode = e.Graphics.SmoothingMode;
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.DrawPath(pen, path);
		e.Graphics.SmoothingMode = smoothingMode;
	}
}
