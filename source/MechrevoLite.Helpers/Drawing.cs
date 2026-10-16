using System.Drawing;
using System.Drawing.Drawing2D;

namespace MechrevoLite.Helpers;

internal static class Drawing
{
	public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
	{
		int num = radius * 2;
		Rectangle rect = new Rectangle(size: new Size(num, num), location: bounds.Location);
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius == 0)
		{
			graphicsPath.AddRectangle(bounds);
			return graphicsPath;
		}
		graphicsPath.AddArc(rect, 180f, 90f);
		rect.X = bounds.Right - num;
		graphicsPath.AddArc(rect, 270f, 90f);
		rect.Y = bounds.Bottom - num;
		graphicsPath.AddArc(rect, 0f, 90f);
		rect.X = bounds.Left;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int cornerRadius)
	{
		using GraphicsPath path = RoundedRect(bounds, cornerRadius);
		graphics.FillPath(brush, path);
	}
}
