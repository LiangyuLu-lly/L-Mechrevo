using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RBadgeButton : RButton
{
	private int badge;

	public int Badge
	{
		get
		{
			return badge;
		}
		set
		{
			if (badge != value)
			{
				badge = value;
				Invalidate();
			}
		}
	}

	protected override void OnPaint(PaintEventArgs pevent)
	{
		base.OnPaint(pevent);
		if (badge <= 0)
		{
			return;
		}
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读绘制 DC 的 Graphics.DpiX。
		float num = UiDpi.Paint(this) / 192f;
		Rectangle clientRectangle = ClientRectangle;
		using Brush brush = new SolidBrush(BorderColor);
		float num2 = num * 14f;
		RectangleF rect = new RectangleF((float)clientRectangle.Width - (float)clientRectangle.Height / 2f - num2, (float)clientRectangle.Height / 2f - num2, num2 + num2, num2 + num2);
		pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		pevent.Graphics.FillEllipse(brush, rect);
		using GraphicsPath graphicsPath = new GraphicsPath();
		using FontFamily family = new FontFamily("Segoe UI");
		using StringFormat format = StringFormat.GenericTypographic;
		graphicsPath.AddString(badge.ToString(), family, 1, 100f, PointF.Empty, format);
		graphicsPath.Flatten();
		RectangleF bounds = graphicsPath.GetBounds();
		float num3 = num2 * 1.1f / bounds.Height;
		float num4 = bounds.X + bounds.Width / 2f + ((badge == 1) ? (bounds.Width * 0.1f) : 0f);
		using (Matrix matrix = new Matrix())
		{
			matrix.Translate(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
			matrix.Scale(num3, num3);
			matrix.Translate(0f - num4, 0f - (bounds.Y + bounds.Height / 2f));
			graphicsPath.Transform(matrix);
		}
		pevent.Graphics.FillPath(Brushes.White, graphicsPath);
	}
}
