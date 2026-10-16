using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public static class ResponsiveLayout
{
	public const int LogicalScreenMargin = 12;

	public static int LogicalToDevice(Control control, int value)
	{
		float num = ((control.FindForm() is RForm { AuditLayoutScale: >0f } rForm) ? rForm.AuditLayoutScale : ((float)UiDpi.Layout(control) / 96f));
		return (int)Math.Round((float)value * num);
	}

	public static void ScaleFrom96(Control control, Control dpiOwner)
	{
		float num = (float)UiDpi.Layout(dpiOwner) / 96f;
		if (Math.Abs(num - 1f) > 0.01f)
		{
			control.Scale(new SizeF(num, num));
		}
		PerformLayoutTree(control);
	}

	public static Rectangle WorkingAreaFor(Control control)
	{
		return Screen.FromControl(control).WorkingArea;
	}

	public static void ConstrainToWorkingArea(Form form, Rectangle? requestedArea = null)
	{
		if (!form.IsDisposed && form.WindowState == FormWindowState.Normal)
		{
			Rectangle rectangle = requestedArea ?? WorkingAreaFor(form);
			int num = LogicalToDevice(form, 12);
			int num2 = Math.Max(0, form.Width - form.ClientSize.Width);
			int num3 = Math.Max(0, form.Height - form.ClientSize.Height);
			// 2026-10-06 E 组（小屏 + 高缩放）：窗口尺寸下限（MinimumSize）本身就可能大于可用工作区
			// —— 这时下面的收敛会被下限顶住，结果窗口必然超出屏幕（1366×768 @200% 上「首次引导」
			// 实测 1064×821 的窗口对 1366×672 的可用区，底部按钮落在屏幕外）。下限在工作区里根本
			// 不可达，所以只在这一种情况下把它放松到刚好装得下；其余情况一个字节都不动。
			int val3 = Math.Max(240, rectangle.Width - num * 2);
			int val4 = Math.Max(200, rectangle.Height - num * 2);
			Size minimumSize = form.MinimumSize;
			if (minimumSize.Width > val3 || minimumSize.Height > val4)
			{
				form.MinimumSize = new Size(Math.Min(minimumSize.Width, val3), Math.Min(minimumSize.Height, val4));
			}
			int val = Math.Max(240, rectangle.Width - num * 2 - num2);
			int val2 = Math.Max(200, rectangle.Height - num * 2 - num3);
			Size clientSize = form.ClientSize;
			Size size = new Size(Math.Min(clientSize.Width, val), Math.Min(clientSize.Height, val2));
			// 2026-10-09（lightspeed，P0）：**尺寸没变就不再全树重排**。
			// PerformLayoutTree 是 root.PerformLayout + 逐子递归（每个子节点前后各再排一次）+ 收尾
			// root.PerformLayout ⇒ 同一个节点被排 2~3 遍，本机实测单次 113~245ms（设置窗按 175% DPI 实测
			// 113~183ms；把 DPI 钉在 125/150/200% 时 166~245ms）；而 ConstrainToWorkingArea 在一次
			// RForm.ApplyResponsiveBounds 里会被调**两次**（RForm.cs:200 一次、PlaceBottomRight 里再一次），
			// 两次的尺寸都不变 ⇒ 开机路径上这一项是**纯重复**：本机开机实测 4 次（构造期 176.7+179.7、
			// ShowAll 期 125.5+113.4 = **595ms**），首次打开设置对话框时那两次更是 1521+1474 = **2996ms**。
			// ⚠️ 只删"尺寸未变时的那一次重排"：尺寸真的变了时行为一字未改（赋值 + PerformLayoutTree 都照旧，
			// 而且 ClientSize 赋值本身就会触发一次 layout）；下面的 Left/Top 夹取是位置变化，不需要重排。
			// ⚠️ 本方法被所有窗体共用（设置窗/键盘灯/风扇曲线/首次引导/设置对话框…），故不在这里改任何几何口径；
			// 改后用"钉住 DPI 125%/150%/200% × 1366×768 / 1920×1080"实测逐项核对：窗口尺寸/位置与改前
			// 逐值相同（549x690 / 654x684 / 864x672 …），越界子控件计数也逐项相同，未引入新的越界。
			if (size != clientSize)
			{
				form.ClientSize = size;
				PerformLayoutTree(form);
			}
			int max = Math.Max(rectangle.Left + num, rectangle.Right - num - form.Width);
			int max2 = Math.Max(rectangle.Top + num, rectangle.Bottom - num - form.Height);
			form.Left = Math.Clamp(form.Left, rectangle.Left + num, max);
			form.Top = Math.Clamp(form.Top, rectangle.Top + num, max2);
		}
	}

	public static void PlaceBottomRight(Form form, Rectangle? requestedArea = null)
	{
		Rectangle value = requestedArea ?? WorkingAreaFor(form);
		ConstrainToWorkingArea(form, value);
		int num = LogicalToDevice(form, 12);
		form.Location = new Point(Math.Max(value.Left + num, value.Right - num - form.Width), Math.Max(value.Top + num, value.Bottom - num - form.Height));
	}

	public static void PlaceAdjacent(Form child, Form owner)
	{
		Rectangle workingArea = Screen.FromControl(owner).WorkingArea;
		ConstrainToWorkingArea(child, workingArea);
		int num = LogicalToDevice(owner, 10);
		int num2 = owner.Left - child.Width - num;
		if (num2 < workingArea.Left)
		{
			num2 = owner.Right + num;
		}
		child.Location = new Point(Math.Clamp(num2, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - child.Width)), Math.Clamp(owner.Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - child.Height)));
	}

	public static void ShowAdjacentTo(Form child, Form owner)
	{
		if (child.Visible)
		{
			child.Activate();
			return;
		}
		if (!child.IsHandleCreated)
		{
			child.CreateControl();
		}
		PerformLayoutTree(child);
		PlaceAdjacent(child, owner);
		child.Show();
	}

	public static int VisibleContentBottom(Control container)
	{
		return (from Control control in container.Controls
			where control.Visible
			select control.Bottom + control.Margin.Bottom).DefaultIfEmpty(0).Max();
	}

	public static void PerformLayoutTree(Control root)
	{
		root.PerformLayout();
		foreach (Control control in root.Controls)
		{
			PerformLayoutTree(control);
		}
		root.PerformLayout();
	}
}
