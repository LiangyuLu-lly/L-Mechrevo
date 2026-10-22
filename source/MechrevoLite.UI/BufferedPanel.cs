using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal class BufferedPanel : Panel
{
	private const int WmEraseBkgnd = 20;

	private int _cardRadius = 8;

	private Size _lastSize;

	public bool CardStyle { get; set; }

	public int CardRadius
	{
		get
		{
			return _cardRadius;
		}
		set
		{
			_cardRadius = value;
			UpdateCardRegion();
			Invalidate();
		}
	}

	public BufferedPanel()
	{
		// 2026-10-08（收尾五：重绘残留）：ResizeRedraw 由显式 false 改为 true。
		// 之前关掉它、只靠 OnResize 里的 Invalidate() 兜底；但「自身尺寸没变、子控件被重新停靠/移动」
		// 这条路径（配置行 D(34)→D(26)、Padding 变化、Visible 切换）既不触发 OnResize，
		// 当时也没有 OnLayout 兜底 —— 被腾出来的那条 1px 缝留在原地（属于本容器的客户区），
		// 保留着上一次绘制的像素，观感就是「偶发细线」，直到鼠标悬停触发一次重绘才被擦掉。
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
		DoubleBuffered = true;
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 20)
		{
			m.Result = IntPtr.Zero;
		}
		else
		{
			base.WndProc(ref m);
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		UpdateCardRegion();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (!(Size == _lastSize))
		{
			_lastSize = Size;
			UpdateCardRegion();
		}
		// 2026-10-08：尺寸变化一律整块重绘（不再只在「尺寸与上次不同」时才 Invalidate）——
		// 句柄未创建时 Invalidate() 会被丢弃，而 _lastSize 却已记账，那次失效就永久丢了。
		Invalidate();
	}

	/// <summary>
	/// 2026-10-08（收尾五：重绘残留）：子控件被重新停靠/移动/显隐（Dock 顺序、行高常量、Visible）
	/// 之后，本容器被腾出来的那些条带属于**自己的客户区**，必须自己整块重绘；否则旧像素留在原地，
	/// 表现为偶发细线/残影，鼠标悬停触发别的控件重绘后才消失。布局变化很稀疏，双缓冲下代价可忽略。
	/// </summary>
	protected override void OnPaintBackground(PaintEventArgs e)
	{
		// 2026-10-22（机主选 B：自绘透底）：实验模式下本容器自己画「底图切片 + 半透明底色」，
		// 不再依赖 Color.Transparent —— 容器仍是实窗口、能够正常滚动/双缓冲，减少残影。
		if (UiVisualStyle.IsOverlayTranslucent(this))
		{
			UiVisualStyle.DrawFormBackgroundSlice(e.Graphics, this);
			using SolidBrush overlayBrush = new SolidBrush(UiVisualStyle.TranslucentFor(this, BackColor, 235));
			e.Graphics.FillRectangle(overlayBrush, ClientRectangle);
			return;
		}
		base.OnPaintBackground(e);
	}

	protected override void OnLayout(LayoutEventArgs e)
	{
		base.OnLayout(e);
		Invalidate();
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		if (Visible)
		{
			// 由隐藏切回显示时，客户区里可能还是隐藏前（或别的控件）的像素。
			Invalidate();
		}
	}

	private void UpdateCardRegion()
	{
		if (!CardStyle || Width <= 0 || Height <= 0)
		{
			Region = null;
			return;
		}
		int num = Math.Max(1, (int)Math.Round((double)CardRadius * ((double)DeviceDpi / 96.0))) * 2;
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(0, 0, num, num, 180f, 90f);
		graphicsPath.AddArc(Width - num - 1, 0, num, num, 270f, 90f);
		graphicsPath.AddArc(Width - num - 1, Height - num - 1, num, num, 0f, 90f);
		graphicsPath.AddArc(0, Height - num - 1, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		Region = new Region(graphicsPath);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (!CardStyle || Width <= 0 || Height <= 0)
		{
			return;
		}
		int num = Math.Max(1, (int)Math.Round((double)CardRadius * ((double)DeviceDpi / 96.0))) * 2;
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(0, 0, num, num, 180f, 90f);
		graphicsPath.AddArc(Width - num - 1, 0, num, num, 270f, 90f);
		graphicsPath.AddArc(Width - num - 1, Height - num - 1, num, num, 0f, 90f);
		graphicsPath.AddArc(0, Height - num - 1, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		// 卡片边框已删除（用户要求无边框）
	}
}
