using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class RScrollBar : Control
{
	private int _value;

	private int _max = 1;

	private int _contentHeight = 1;

	private int _viewHeight = 1;

	private bool _dragging;

	private int _dragStartY;

	private int _dragStartValue;

	public int Value
	{
		get
		{
			return _value;
		}
		set
		{
			int num = Math.Clamp(value, 0, Math.Max(0, _contentHeight - _viewHeight));
			if (num != _value)
			{
				_value = num;
				Invalidate();
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public int MaxValue => Math.Max(0, _contentHeight - _viewHeight);

	public int Step => Math.Max(24, (int)Math.Round((double)UiDpi.Paint(this) / 96.0 * 40.0));

	public event EventHandler? ValueChanged;

	public RScrollBar()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Visible = false;
		Cursor = Cursors.Hand;
		TabStop = false;
		BackColor = UiVisualStyle.Window;
	}

	public void SetRange(int contentHeight, int viewHeight)
	{
		_contentHeight = Math.Max(1, contentHeight);
		_viewHeight = Math.Max(1, viewHeight);
		int num = Math.Max(0, _contentHeight - _viewHeight);
		_max = Math.Max(1, num);
		bool flag = num > 0;
		if (Visible != flag)
		{
			Visible = flag;
		}
		int num2 = Math.Clamp(_value, 0, num);
		bool flag2 = num2 != _value;
		_value = num2;
		Invalidate();
		if (flag2)
		{
			ValueChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private int ThumbHeight(int track)
	{
		return Math.Max(track / 10, (int)((long)track * (long)_viewHeight / Math.Max(1, _contentHeight)));
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）—— 本控件只在客户区中间画一条滑钮，
	/// 四周大片是"应当与父容器同色"的区域，最容易被系统用自身 BackColor 擦出异色带。
	/// 其余消息原样交给 <c>base.WndProc</c>（拖拽/滚轮/点击的命中算法只依赖 Width/Height，未动）。
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
		int num2 = Math.Max(1, (int)Math.Round(num * 2f));
		int radius = Math.Max(2, (int)Math.Round(num * 8f));
		int num3 = ThumbHeight(Height);
		int num4 = Math.Max(1, Height - num3);
		int num5 = ((Height > num3) ? ((int)Math.Round((double)num4 * (double)_value / (double)_max)) : 0);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		// 2026-10-22（机主：下拉滑轨贴边）：滑钮左右贴满控件宽度，不再留 2px 内缩。
		using GraphicsPath path = RoundedRect(new Rectangle(0, num5 + num2, Width, Math.Max(4, num3 - num2 * 2)), radius);
		using SolidBrush brush = new SolidBrush(UiVisualStyle.Border);
		e.Graphics.FillPath(brush, path);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		int num = ThumbHeight(Height);
		int num2 = Math.Max(1, Height - num);
		int num3 = ((Height > num) ? ((int)Math.Round((double)num2 * (double)_value / (double)_max)) : 0);
		if (e.Y >= num3 && e.Y <= num3 + num)
		{
			_dragging = true;
			_dragStartY = e.Y;
			_dragStartValue = _value;
		}
		else
		{
			Value = _value + ((e.Y < num3) ? (-_viewHeight) : _viewHeight);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (_dragging)
		{
			int num = ThumbHeight(Height);
			int num2 = Math.Max(1, Height - num);
			Value = _dragStartValue + (int)Math.Round((double)(e.Y - _dragStartY) * (double)_max / (double)num2);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_dragging = false;
		base.OnMouseUp(e);
	}

	private static GraphicsPath RoundedRect(Rectangle rect, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0)
		{
			graphicsPath.AddRectangle(rect);
			return graphicsPath;
		}
		int num = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
		graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
