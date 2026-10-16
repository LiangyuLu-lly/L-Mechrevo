using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

/// <summary>
/// 2026-10-10（深色模式「禁用态文字」可读性）：与全仓其余控件（RButton / RCheckBox / RSlider /
/// RComboBox / RNumericUpDown）**同一口径**的 Label —— 禁用态的文字一律画主题的
/// <see cref="UiVisualStyle.Muted" />（夜 158/158/158、日 78/95/117），不再让系统画"浮雕"禁用色。
///
/// 为什么必须自绘：WinForms 的 <see cref="Label" /> 在 <c>Enabled == false</c> 时走
/// <c>ControlPaint.DrawStringDisabled</c>，它**完全不看 ForeColor**，只拿底色算一个"变暗 2/3"的颜色。
/// 实测（.NET 10 / net10.0-windows，96 DPI，同一段「风扇增强」文字，只改底色与 ForeColor）：
///   底色 40/40/40 → 文字 **13/13/13**（近黑；深色模式的卡片正是 40/40/40 ⇒ 与底色几乎零对比，
///                        这就是用户报的「不可选时字是黑色的」）；
///   底色 54/54/54 → 18/18/18；底色 20/20/20 → 6/6/6；白底 255 → **85/85/85**（日间一直是对的）。
/// 即：**底色越深字越黑**，且改 <c>ForeColor</c> 一点用都没有（ForeColor 换成 100 或 255 结果仍是 13）
/// —— 这是结构性问题，只能在禁用态自己画。
/// ⚠️ 相关规矩（本类兜底的那条路径最好别走到）：子控件自身 <c>Enabled = true</c>、但父容器
/// <c>Enabled = false</c> 时，<c>Control.Enabled</c> 的取值**同样是 false**（getter 会把父链一起 AND），
/// 所以"只禁用值控件、不禁用整行/标题"才是首选做法；本类负责"万一标题所在容器真被禁用"时仍然可读。
///
/// 实现口径：**启用态逐像素走 <c>base.OnPaint</c>**（布局 / 换行 / 省略号 / 助记符 / 透明底一字未动），
/// 只有禁用态自绘：矩形与对齐按基类 Label 自己的文本矩形实测复刻
/// （= <c>Padding</c> 之后的客户区四周各外扩 <see cref="TextRectInsetLogical" />，见其注释），
/// 只把颜色换成主题令牌。<c>–ui-audit</c> 的度量走的是启用态 / 控件自身属性，不受影响。
/// ⚠️ 已知差异（实测，均非本仓在用的对齐）：Left / MiddleLeft / TopLeft 与外扩后的基类几何逐像素一致；
/// Center / Right / Bottom 系列因 GDI 居中取整与基类实现最多差 1~3 设备像素（本仓 row 标题只用
/// MiddleLeft、警告行标题只用默认 TopLeft，都落在"一致"那一档）。
/// </summary>
public class RLabel : Label
{
	/// <summary>
	/// 禁用态自绘矩形相对「Padding 之后的客户区」的外扩量（逻辑像素）。
	/// 依据：基类 Label 的文本矩形实测 = 客户区扣掉 <see cref="Control.Padding" /> 之后**四周再各外扩 2**
	/// （96 DPI，probe 逐像素对账：TopLeft / MiddleLeft 差 0 像素；用"不外扩"或"只外扩上下"都会差 2 像素以上）。
	/// 按控件自身 DPI 缩放后取整。
	/// </summary>
	private const int TextRectInsetLogical = 2;

	protected override void OnPaint(PaintEventArgs e)
	{
		if (Enabled)
		{
			base.OnPaint(e);
			return;
		}
		// 铺底（禁用态自绘整块，故底色仍按基类口径来：不透明底色铺实色，透明/未设底色走基类透明底路径）。
		if (BackColor.A == 255)
		{
			using (SolidBrush brush = new SolidBrush(BackColor))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}
		}
		else
		{
			base.OnPaintBackground(e);
		}
		int inset = (int)Math.Round(TextRectInsetLogical * UiDpi.Paint(this) / 96f);
		Rectangle rect = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
		rect.Inflate(inset, inset);
		TextFormatFlags flags = TextFormatFlags.Default;
		switch (TextAlign)
		{
		case ContentAlignment.TopLeft:
			flags |= TextFormatFlags.Top | TextFormatFlags.Left;
			break;
		case ContentAlignment.TopCenter:
			flags |= TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
			break;
		case ContentAlignment.TopRight:
			flags |= TextFormatFlags.Top | TextFormatFlags.Right;
			break;
		case ContentAlignment.MiddleLeft:
			flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
			break;
		case ContentAlignment.MiddleCenter:
			flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
			break;
		case ContentAlignment.MiddleRight:
			flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
			break;
		case ContentAlignment.BottomLeft:
			flags |= TextFormatFlags.Bottom | TextFormatFlags.Left;
			break;
		case ContentAlignment.BottomCenter:
			flags |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
			break;
		case ContentAlignment.BottomRight:
			flags |= TextFormatFlags.Bottom | TextFormatFlags.Right;
			break;
		}
		if (!UseMnemonic)
		{
			flags |= TextFormatFlags.NoPrefix;
		}
		if (AutoEllipsis)
		{
			flags |= TextFormatFlags.EndEllipsis;
		}
		if (!AutoSize)
		{
			flags |= TextFormatFlags.WordBreak;
		}
		// 唯一的语义差别：禁用态用主题的 Muted（夜 158 灰 / 日 78,95,117 蓝灰），深色底上依然可读。
		TextRenderer.DrawText(e.Graphics, Text, Font, rect, UiVisualStyle.Muted, flags);
	}
}
