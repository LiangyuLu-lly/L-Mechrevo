using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

/// <summary>
/// 2026-10-11（真机「叠在一起了」修复）：标题行右端的**读数片**专用 Label —— 保证读数**永远单行**。
///
/// 为什么必须有这个类：<see cref="Label" /> 的默认绘制旗标是
/// <c>TextFormatFlags.Default</c>（Left | Top，**不带** SingleLine），宽度不够时 GDI 会按空格折行，
/// 于是 "12W 2539rpm 35%" 变成两行（第二行 "35%"），控件高 14 的标签被撑到 28 高的内容 ⇒
/// 内容越过只有 26 高（逻辑 D(26)）的标题行、压到下面按 Dock=Top 停靠的按钮行上 —— 用户截图上
/// 「CPU 58°C / 12W 2539rpm / 35%」正压在「狂暴 / 自定义」那一行就是这个形状。
///
/// 修法分两层（本类只负责第二层）：
///   ① 宽度层（<c>SettingsForm.TelemetryTextWidth</c>）：按**真实渲染口径**（含 Label 内边距）
///      实测文本宽并留安全余量，列宽/控件宽都按它给 ⇒ 正常情况下一步到位、不折行、不裁字；
///   ② 兜底层（本类）：即便宽度层因字体替换 / 渲染器取整 / 未来更长的读数而算少几个像素，
///      这里也**只画单行**（<c>TextFormatFlags.SingleLine | NoPrefix</c>），横向按对齐裁剪 ——
///      ⛔ 绝不折行、绝不把内容画到行外去压按钮。
///
/// 只改**绘制**（OnPaint 自绘文本），不改尺寸 / 对齐语义：<c>AutoSize</c> 仍为 false、
/// <c>MinimumSize.Width</c> 仍是布局用的固定宽，<c>TextAlign</c> 仍决定左/右对齐。
/// 与全仓既有做法一致（RLabel / RButton / RCheckBox 等同样是"自绘文字"口径）。
/// ⚠️ 垂直居中：读数原本是 <c>MiddleLeft / MiddleRight</c>，这里显式沿用"垂直居中"，
/// 与基类 Label 在"控件高 = 字体高"时的表现一致（读数片高度就是 PreferredHeight）。
/// </summary>
internal sealed class RTelemetryLabel : Label
{
	protected override void OnPaint(PaintEventArgs e)
	{
		// 基类 Label 在 UserPaint 下不铺底 ⇒ 这里自己铺（不透明底色直接 FILL，透明底走基类透明路径），
		// 与全仓其它自绘控件的口径一致（RLabel / RButton 同做法）。
		if (BackColor.A == 255)
		{
			using SolidBrush brush = new SolidBrush(BackColor);
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}
		else
		{
			base.OnPaintBackground(e);
		}
		// 2026-10-11（真机「读数仍被省略号截断」· 根因修复）：必须带 **NoPadding**！
		// 文本宽是按 NoPadding 口径实测的（SettingsForm.TelemetryTextWidth），而 TextRenderer.DrawText
		// 默认会再加一份"文字内边距"（本机 96 DPI 实测 +7px，真机 175% DPI 下 +12px 左右）——
		// 于是"控件宽 == 实测文本宽"的读数片在绘制时仍差 7~12px，**EndEllipsis 就把尾巴吃掉**
		// （真机日志实证：w == want == 35/46/134 却仍显示 "… 5… 20W 2145rpm…"；宿主 96 DPI 下差额小，
		//   所以这一条在宿主里几乎看不出来，正是"宿主复现不出真机坏态"的最后一层）。
		// 本类的文本矩形就是整个客户区（Control.Padding 已被清空）⇒ 加 NoPadding 后
		// 「量多宽 = 画多宽」，与 TelemetryTextWidth 的口径完全一致。
		TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
		switch (TextAlign)
		{
		case ContentAlignment.TopLeft:
		case ContentAlignment.TopCenter:
		case ContentAlignment.TopRight:
			flags |= TextFormatFlags.Top;
			break;
		case ContentAlignment.BottomLeft:
		case ContentAlignment.BottomCenter:
		case ContentAlignment.BottomRight:
			flags |= TextFormatFlags.Bottom;
			break;
		default:
			flags |= TextFormatFlags.VerticalCenter;
			break;
		}
		switch (TextAlign)
		{
		case ContentAlignment.TopCenter:
		case ContentAlignment.MiddleCenter:
		case ContentAlignment.BottomCenter:
			flags |= TextFormatFlags.HorizontalCenter;
			break;
		case ContentAlignment.TopRight:
		case ContentAlignment.MiddleRight:
		case ContentAlignment.BottomRight:
			flags |= TextFormatFlags.Right;
			break;
		default:
			flags |= TextFormatFlags.Left;
			break;
		}
		if (AutoEllipsis)
		{
			flags |= TextFormatFlags.EndEllipsis;
		}
		Rectangle rect = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical));
		TextRenderer.DrawText(e.Graphics, Text, Font, rect, Enabled ? ForeColor : UiVisualStyle.Muted, flags);
	}
}
