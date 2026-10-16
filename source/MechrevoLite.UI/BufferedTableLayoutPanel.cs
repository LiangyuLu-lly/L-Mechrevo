using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
	private const int WmEraseBkgnd = 20;

	public BufferedTableLayoutPanel()
	{
		// 2026-10-08（收尾五：重绘残留）：ResizeRedraw 由显式 false 改为 true。
		// 本类（表头行/模式按钮行/配置行/仪表盘栈）里子控件的行高、停靠顺序、Visible 都是运行期变的，
		// 只靠 OnResize 兜底覆盖不到「自己尺寸没变、子控件被重新摆放」这条路 —— 见下面的 OnLayout。
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

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		// 2026-10-08：尺寸变化一律整块重绘（不再只在「尺寸与上次不同」时才 Invalidate）。
		Invalidate();
	}

	/// <summary>
	/// 2026-10-08（收尾五：重绘残留）：行高/列宽/子控件显隐变化会让本面板重新摆放子控件，
	/// 被腾出来的条带属于本面板自己的客户区，必须自己重绘；否则残留旧像素（偶发细线），
	/// 鼠标悬停触发别的控件重绘时才消失。
	/// </summary>
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
			Invalidate();
		}
	}
}
