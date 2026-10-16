using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class CustomMenuRenderer : ToolStripProfessionalRenderer
{
	public CustomMenuRenderer()
		: base(new ProfessionalColorTable())
	{
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		Rectangle rect = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
		Color color = e.Item.Owner?.BackColor ?? SystemColors.Menu;
		if (e.Item.Selected && e.Item.Enabled)
		{
			using (SolidBrush brush = new SolidBrush(SystemColors.Highlight))
			{
				e.Graphics.FillRectangle(brush, rect);
				return;
			}
		}
		using SolidBrush brush2 = new SolidBrush(color);
		e.Graphics.FillRectangle(brush2, rect);
	}

	protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
	{
		if (e.Item is ToolStripMenuItem { Checked: not false })
		{
			Color foreColor = (e.Item.Selected ? SystemColors.HighlightText : (e.Item.Owner?.ForeColor ?? SystemColors.MenuText));
			Rectangle bounds = new Rectangle(0, 0, e.ImageRectangle.Right + e.ImageRectangle.X, e.Item.Height);
			TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, bounds, foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		if (!e.Item.Enabled)
		{
			e.TextColor = Color.FromArgb(130, e.Item.Owner?.ForeColor ?? SystemColors.MenuText);
		}
		else if (e.Item.Selected)
		{
			e.TextColor = SystemColors.HighlightText;
		}
		else
		{
			e.TextColor = e.Item.Owner?.ForeColor ?? SystemColors.MenuText;
		}
		base.OnRenderItemText(e);
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		int num = e.Item.Height / 2;
		Color baseColor = e.ToolStrip?.ForeColor ?? SystemColors.MenuText;
		// 2026-10-09（P0.5）：`new Pen(color)` 的默认线宽＝硬编码 1 逻辑像素，统一走唯一工厂
		// UiVisualStyle.OutlinePen(Graphics 版)（渲染器无控件实例，按绘制 DC 的 DPI 取整）。
		using Pen pen = UiVisualStyle.OutlinePen(e.Graphics, Color.FromArgb(50, baseColor), 1f, 1f, inset: false);
		e.Graphics.DrawLine(pen, 0, num, e.Item.Width, num);
	}

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(e.ToolStrip.BackColor);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(e.ToolStrip.BackColor);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
	}
}
