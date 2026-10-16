using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class RComboDisplay : Label
{
	public RComboDisplay()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		AutoSize = false;
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）—— 本控件整块自绘，
	/// "系统再擦一遍 / 再描一圈"只会在周边留下淡线/异色块。其余消息原样走 <c>base.WndProc</c>。
	/// </summary>
	protected override void WndProc(ref Message m)
	{
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		base.WndProc(ref m);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读绘制 DC 的 Graphics.DpiX。
		float num = UiDpi.Paint(this) / 192f;
		// 2026-10-09（UI 打磨⑦，仅夜间）：圆角半径统一走共用 UiVisualStyle.CornerRadiusDevicePx
		// （夜间 4 逻辑像素＝"小圆角矩形"，与 RTextBox/RNumericUpDown/RComboBox 同一档；日间仍是 12×Dpi/192
		// ＝ 96 DPI 下 6px，像素不变）。矩形/尺寸/布局/命中区域一字未动。
		// 2026-10-09（P0.5）：改用**控件自己的 DPI** 版重载（与输入族其余控件同源）。
		int val = UiVisualStyle.CornerRadiusDevicePx(this);
		int num2 = (int)Math.Round(num * 16f);
		int num3 = (int)Math.Round(num * 14f);
		int num4 = (int)Math.Round(num * 8f);
		Rectangle clientRectangle = ClientRectangle;
		clientRectangle.Width--;
		clientRectangle.Height--;
		if (clientRectangle.Width <= 4 || clientRectangle.Height <= 4)
		{
			return;
		}
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		int radius = Math.Min(val, Math.Min(clientRectangle.Width, clientRectangle.Height) / 2);
		using (GraphicsPath path = RoundedPath(clientRectangle, radius))
		{
			using (SolidBrush brush = new SolidBrush(UiVisualStyle.Input))
			{
				e.Graphics.FillPath(brush, path);
			}
		}
		// 2026-10-09（UI 打磨②b，仅夜间）：描边改走共用的 UiVisualStyle.DrawInputOutline ——
		// 夜间按 BorderStrong（86/112/158）画 2px（DPI 缩放、Inset 全部落在客户区内），日间仍是 1px 的
		// Border 色（日间 BorderStrong == Border），矩形/半径与上一轮逐像素相同。
		UiVisualStyle.DrawInputOutline(e.Graphics, clientRectangle, UiVisualStyle.BorderStrong, this);
		int num5 = clientRectangle.Right - num2 - num3;
		int num6 = clientRectangle.Top + (clientRectangle.Height - num4) / 2;
		// 2026-10-09（UI 打磨⑦，仅夜间）：箭头与 RComboBox 统一成同一款细线 chevron（两段 AA 线、RoundCap、
		// 半像素对齐）；日间仍是原来的实心三角（FillPolygon），故日间像素不变。
		if (UiVisualStyle.IsNightMode)
		{
			float cx = num5 + num3 / 2f;
			float cy = clientRectangle.Top + clientRectangle.Height / 2f;
			float halfW = Math.Max(1f, num3 / 2f);
			float halfH = Math.Max(1f, num4 / 2f);
			float lx = (float)Math.Round(cx) + 0.5f - halfW;
			float mx = (float)Math.Round(cx) + 0.5f;
			float ty = (float)Math.Round(cy) + 0.5f - halfH / 2f;
			// 2026-10-09（P0.5）：硬编码 1f → 唯一工厂（1 逻辑像素；100%→1、150%→2、200%→2 设备像素），
			// 与 RComboBox.DrawChevron 的细线同族。不设 Inset（chevron 是 RoundCap 折线，Inset 不适用）。
			using Pen pen = UiVisualStyle.OutlinePen(this, UiVisualStyle.Muted, 1f, 1f, inset: false);
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen.LineJoin = LineJoin.Round;
			e.Graphics.DrawLines(pen, new PointF[3]
			{
				new PointF(lx, ty),
				new PointF(mx, ty + halfH),
				new PointF(mx + halfW, ty)
			});
		}
		else
		{
			using SolidBrush brush2 = new SolidBrush(UiVisualStyle.Muted);
			e.Graphics.FillPolygon(brush2, new Point[3]
			{
				new Point(num5, num6),
				new Point(num5 + num3, num6),
				new Point(num5 + num3 / 2, num6 + num4)
			});
		}
		TextRenderer.DrawText(bounds: new Rectangle(clientRectangle.Left + num2, clientRectangle.Top, Math.Max(0, clientRectangle.Width - num2 * 2 - num3), clientRectangle.Height), dc: e.Graphics, text: Text, font: Font, foreColor: ForeColor, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
	}

	private static GraphicsPath RoundedPath(Rectangle rect, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0)
		{
			graphicsPath.AddRectangle(rect);
			return graphicsPath;
		}
		int num = radius * 2;
		graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
