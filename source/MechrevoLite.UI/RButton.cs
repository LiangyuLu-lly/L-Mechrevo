using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RButton : Button
{
	private const float HoverShiftAmount = 0.06f;

	private int borderSize = 3;

	private int borderRadius = 8;

	private Color borderColor = Color.Transparent;

	private bool activated;

	private RSegmentPosition segmentPosition;

	private bool secondary;

	private bool _hover;

	public int BorderRadius
	{
		get
		{
			return borderRadius;
		}
		set
		{
			borderRadius = value;
		}
	}

	public Color BorderColor
	{
		get
		{
			return borderColor;
		}
		set
		{
			if (borderColor != value)
			{
				// 2026-10-09（UI 反馈①，用户原话「点击新颜色旧颜色高亮不消失」）：**这里原来没有 Invalidate()**。
				// 换边色（选中态那一圈）却不重绘 ⇒ 只有被点中的那颗会因自己的点击/悬停重画，上一颗的高亮
				// 留在屏幕上是**旧像素**（要等主题切换/移动窗口等外部重绘才消失）。这就是"旧高亮不消失"的根因。
				borderColor = value;
				Invalidate();
			}
		}
	}

	public bool Activated
	{
		get
		{
			return activated;
		}
		set
		{
			if (activated != value)
			{
				Invalidate();
			}
			activated = value;
			AccessibleDescription = (activated ? "Active" : null);
		}
	}

	private Color activatedColor = Color.Empty;

	/// <summary>
	/// 2026-10-09（UI 打磨②b，仅夜间）：段式按钮的「选中态画法」开关。
	/// false（默认）＝ 沿用既有「实心填充 activatedColor」；true ＝ 夜间改为「彩色描边(2px, DPI 缩放) + 深底
	/// （SurfaceRaised 混入该强调色 18%）+ 浅色文字」。日间该标志被 OnPaint 忽略，仍是实心填充，像素不变。
	/// 只由 SettingsForm.SetPerfModeAccent 对五个性能模式按钮开启（GPU 模式行「待生效＝棕」等未开）。
	/// </summary>
	private bool activatedOutline;

	public bool ActivatedOutline
	{
		get
		{
			return activatedOutline;
		}
		set
		{
			if (activatedOutline != value)
			{
				Invalidate();
			}
			activatedOutline = value;
		}
	}

	/// <summary>
	/// 2026-10-09（UI 反馈①，仅夜间；用户原话「除了性能模式以外一律使用灰色高亮」）：
	/// 选中态那圈描边是否使用本按钮被下发过的 <see cref="ActivatedColor" />。
	/// <list type="bullet">
	/// <item>true ＝ 用该色：**只有**「性能模式」五个按钮（<c>SettingsForm.SetPerfModeAccent</c> 下发模式色：
	/// 静音绿/平衡蓝/静音狂暴浅红/高能狂暴大红/自定义浅蓝）与显卡模式行的「待生效＝重启后生效」棕色
	/// （<c>VisualiseGPUMode</c> 在下发 <c>Warn</c> 时置位）—— 语义色不变。</item>
	/// <item>false（默认）＝ **强调蓝高亮**（2026-10-09 UI 反馈② 起；此前是中性灰）：其余全部选中态按钮组
	/// （显卡模式行非待生效/屏幕刷新率行/电池档位行/核显省电行/屏幕行的 MiniLED/FHD/HDR），底仍 =
	/// <c>NeutralActivatedFill</c>(ButtonFace 62，**不浅填充**)、边 = <c>AccentSelectionOutline</c>
	/// (Accent 77/163/255，对底 WCAG 5.92；上一轮那圈 <c>Muted</c> 158 已不再被选中态使用)。</item>
	/// </list>
	/// 判据是代码里的可靠依据（本标志只由上面两条路径置位），不做任何文字匹配。
	/// 2026-10-09（0.291 beta22 补丁）：本标志只决定"**描边**用不用该语义色"；
	/// "**浅填充**只给性能模式五行"由下面独立的 <see cref="SelectionUsesModeFill" /> 决定 —— 两者分开，
	/// 因为显卡行的「待生效＝棕」要用棕色描边（true）但**不该**拿到浅填充（false）。
	/// </summary>
	private bool selectionUsesAssignedAccent;

	public bool SelectionUsesAssignedAccent
	{
		get
		{
			return selectionUsesAssignedAccent;
		}
		set
		{
			if (selectionUsesAssignedAccent != value)
			{
				Invalidate();
			}
			selectionUsesAssignedAccent = value;
		}
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁；用户原话「除了性能模式都不要浅填充」）：选中态的**底**是否使用
	/// "该模式色的淡调浅填充"（<see cref="UiVisualStyle.ModeActivatedFill" />）。
	/// <list type="bullet">
	/// <item>true ＝ **只有「性能模式」五个按钮**（由 <c>SettingsForm.SetPerfModeAccent</c> 置位，与
	/// <see cref="SelectionUsesAssignedAccent" /> 同一处）—— 夜＝深底混 12% 模式色、日＝朝白基色混 15%
	/// 模式色（用户明确要"性能模式浅色填充"）。</item>
	/// <item>false（默认）＝ 底取 <see cref="UiVisualStyle.NeutralActivatedFill" />（该主题下**与未选中
	/// 同级的中性面**，即**不加任何浅填充**）：显卡模式行（含"待生效＝棕"）、屏幕刷新率行、电池三档、
	/// 核显省电、MiniLED/FHD/HDR 全部走这一档，选中只由那圈 2 设备像素描边表达。</item>
	/// </list>
	/// ⚠️ 判据同样是代码标志（只由 <c>SetPerfModeAccent</c> 置 true），**不做任何文字/名称匹配**；
	/// 两个主题都生效（夜间该标志为 false 时结果与改动前逐像素相同，因为夜间 ModeActivatedFill 与
	/// NeutralActivatedFill 本来就同源）。
	/// </summary>
	private bool selectionUsesModeFill;

	public bool SelectionUsesModeFill
	{
		get
		{
			return selectionUsesModeFill;
		}
		set
		{
			if (selectionUsesModeFill != value)
			{
				Invalidate();
			}
			selectionUsesModeFill = value;
		}
	}

	// 2026-10-09（UI 打磨④）：③ 曾在这里加过一个 `SelectedOutlineInset`（默认 8，矮行置 0）来控制
	// 「彩边 + 深底」相对客户区的内缩。它就是用户看到的「高亮比整个框小」的根因：同一套画法在不同行里
	// 一个 8 一个 0（性能模式行/显卡模式行用的是默认 8 ⇒ 内缩成一个小药丸），而按钮自身那圈边框照画
	// ⇒ 「外框 + 内缩彩框 + 深底」三层视觉。现删除该旋钮：夜间选中态**一律**把深底铺满整个客户区、
	// 2px 彩边画在按钮自己的圆角路径上（见 DrawActivatedOutline），不再有任何按行不同的内缩值。
	// 日间/legacy 的实心强调色填充仍用②b前的硬编码内缩 D(8)（见 OnPaint 的 num5），像素不变。

	public Color ActivatedColor
	{
		get
		{
			return activatedColor;
		}
		set
		{
			if (activatedColor != value)
			{
				Invalidate();
			}
			activatedColor = value;
		}
	}

	/// <summary>2026-10-08：激活态文字色。Empty = 沿用 UiVisualStyle.AccentText（既有行为，未动）。</summary>
	/// <remarks>
	/// 性能模式高亮按模式上色后，浅红/浅蓝这类浅底需要深色字才看得清（夜间尤其），
	/// 故激活态文字色随 ActivatedColor 一起成对下发；未设置时行为与改动前完全一致。
	/// </remarks>
	private Color activatedTextColor = Color.Empty;

	public Color ActivatedTextColor
	{
		get
		{
			return activatedTextColor;
		}
		set
		{
			if (activatedTextColor != value)
			{
				Invalidate();
			}
			activatedTextColor = value;
		}
	}

	public RSegmentPosition SegmentPosition
	{
		get
		{
			return segmentPosition;
		}
		set
		{
			if (segmentPosition != value)
			{
				Invalidate();
			}
			segmentPosition = value;
		}
	}

	public bool Secondary
	{
		get
		{
			return secondary;
		}
		set
		{
			secondary = value;
		}
	}

	public bool Borderless { get; set; }

	protected override bool ShowFocusCues => false;

	public RButton()
	{
		Cursor = Cursors.Hand;
		DoubleBuffered = true;
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		BackColorChanged += (object? s, EventArgs e) =>
		{
			UpdateHoverColor();
		};
		UpdateHoverColor();
	}

	private void UpdateHoverColor()
	{
		Color target = (((BackColor.R * 30 + BackColor.G * 59 + BackColor.B * 11) / 100 > 128) ? Color.Black : Color.White);
		FlatAppearance.MouseOverBackColor = Shift(BackColor, target, 0.16f);
		FlatAppearance.MouseDownBackColor = Shift(BackColor, target, 0.24f);
	}

	private static Color Shift(Color from, Color target, float amount)
	{
		return Color.FromArgb(from.A, (int)((float)(int)from.R + (float)(target.R - from.R) * amount), (int)((float)(int)from.G + (float)(target.G - from.G) * amount), (int)((float)(int)from.B + (float)(target.B - from.B) * amount));
	}

	private static GraphicsPath GetFigurePath(Rectangle rect, int radius, RSegmentPosition position)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Max(1f, (float)radius * 2f);
		bool flag = (((uint)position <= 1u || position == RSegmentPosition.Single) ? true : false);
		bool flag2 = flag;
		flag = ((position == RSegmentPosition.None || (uint)(position - 3) <= 1u) ? true : false);
		bool flag3 = flag;
		graphicsPath.StartFigure();
		if (flag2)
		{
			graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		}
		else
		{
			graphicsPath.AddLine(rect.X, rect.Y, rect.X, rect.Y);
		}
		graphicsPath.AddLine(flag2 ? (rect.X + (int)(num / 2f)) : rect.X, rect.Y, rect.Right - (flag3 ? ((int)(num / 2f)) : 0), rect.Y);
		if (flag3)
		{
			graphicsPath.AddArc((float)rect.Right - num, rect.Y, num, num, 270f, 90f);
		}
		graphicsPath.AddLine(rect.Right, flag3 ? (rect.Y + (int)(num / 2f)) : rect.Y, rect.Right, rect.Bottom - (flag3 ? ((int)(num / 2f)) : 0));
		if (flag3)
		{
			graphicsPath.AddArc((float)rect.Right - num, (float)rect.Bottom - num, num, num, 0f, 90f);
		}
		graphicsPath.AddLine(rect.Right - (flag3 ? ((int)(num / 2f)) : 0), rect.Bottom, rect.X + (flag2 ? ((int)(num / 2f)) : 0), rect.Bottom);
		if (flag2)
		{
			graphicsPath.AddArc(rect.X, (float)rect.Bottom - num, num, num, 90f, 90f);
		}
		graphicsPath.AddLine(rect.X, flag2 ? (rect.Bottom - (int)(num / 2f)) : rect.Bottom, rect.X, flag2 ? (rect.Y + (int)(num / 2f)) : rect.Y);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨④，仅夜间）：「选中态＝彩边 + 深底」的几何，分段行（Single）与独立按钮（None）
	/// 共用同一份代码。③ 的版本是「把 bounds 向内缩 inset（默认 D(8)）再填 + 画一圈 2px 描边」，于是它和
	/// 按钮**自己的边框**（分段行走 DrawSegmentBorder、独立按钮走 BorderColor 那圈）同时出现 —— 用户原话
	/// 「高亮比整个框小，而且整个框有很怪的边线」（外框 + 内缩彩框 + 深底）。
	/// ④ 的修正：
	/// ① **深底铺满整颗**：填充矩形 = 传入的 bounds = RButton.ClientRectangle，一次都不内缩（radius 也传
	///    按钮自身的圆角值，故选中态的深底 + 圆角与按钮的可见形状完全重合）；
	/// ② **只有一层边**：那圈 2px 彩边就画在**按钮自己的圆角路径上**（调用方传入的 radius 已是按钮自身
	///    路径用的 num3 + num2），并用 PenAlignment.Inset ⇒ 全部落在控件内、贴着按钮边界，与本文件
	///    其它 1px 边框同一套画法（此处不能用 Outset：路径本身已在客户区边界上，半线宽会被客户区裁掉）；
	///    同时调用方在选中态**抑制按钮自身那圈边框**（分段行不调 DrawSegmentBorder、独立按钮不画
	///    BorderColor），故整个按钮只有这一层彩边。
	/// 只画、不改 Bounds/Margin/Region，故不影响布局与命中区域。
	///
	/// 2026-10-09（UI 反馈①，仅夜间）：**选中态的配色分两档**（判据＝<see cref="SelectionUsesAssignedAccent" />，
	/// 不看文字、不改任何语义）：
	///   ① 性能模式行（模式色）与显卡行的「待生效＝棕」⇒ 底 = <c>ModeActivatedFill(该色)</c>
	///      （ButtonFace 62 混 12% 该色 = 与未选中同级，**没有浅色填充**）、边 = <c>SelectionOutlineColor(该色)</c>；
	///   ② 其余所有选中态按钮组 ⇒ **强调蓝高亮**（本轮起）：底 = <c>NeutralActivatedFill</c>（= ButtonFace 62，
	///      **仍与未选中同级、绝不浅填充**）、边 = <c>AccentSelectionOutline</c>（= 强调蓝 Accent 77/163/255）。
	/// 用户原话（反馈②）「我觉得其他的可以改成蓝色高亮但是不填充浅色」—— 本轮**只换边色**，
	/// 填充那条路（<c>NeutralActivatedFill</c>）一个字节都没动；上一轮的中性灰边色
	/// <c>NeutralSelectionOutline</c>(Muted 158) 保留但不再被使用（回退＝把下面 else 分支那个属性名换回去）。
	/// 线宽由 <c>ModeOutlineWidth</c> 给出（整数设备像素，100% DPI 下 2px）——用户原话
	/// 「它的高亮粗且明显 而且没有锯齿」。
	/// </summary>
	private void DrawActivatedOutline(Graphics graphics, Rectangle bounds, int radius)
	{
		if (bounds.Width <= 4 || bounds.Height <= 4)
		{
			return;
		}
		bool useAssignedAccent = selectionUsesAssignedAccent && !activatedColor.IsEmpty;
		// 2026-10-09（UI 反馈②）：**描边**分两档（模式色/待生效棕 用下发色；其余一律强调蓝）。
		Color outline = (useAssignedAccent ? UiVisualStyle.SelectionOutlineColor(activatedColor) : UiVisualStyle.AccentSelectionOutline);
		// 2026-10-09（0.291 beta22 补丁；用户原话「除了性能模式都不要浅填充」）：**底**分两档 ——
		// 只有性能模式五行（selectionUsesModeFill=true）拿"模式色淡调浅填充"；其余全部（含显卡行的
		// 「待生效＝棕」）取中性面 ＝ **与未选中同级、不加浅填充**。判据是代码标志，不做文字匹配。
		Color fill = ((useAssignedAccent && selectionUsesModeFill) ? UiVisualStyle.ModeActivatedFill(activatedColor) : UiVisualStyle.NeutralActivatedFill);
		using GraphicsPath path = GetFigurePath(bounds, radius, RSegmentPosition.Single);
		using (SolidBrush brush = new SolidBrush(fill))
		{
			graphics.FillPath(brush, path);
		}
		using Pen pen = UiVisualStyle.OutlinePen(this, outline, UiVisualStyle.ModeOutlineLogical, 2f);
		pen.Alignment = PenAlignment.Inset;
		graphics.DrawPath(pen, path);
	}

	/// <summary>
	/// 2026-10-09（fix，用户原话「上边框非常细」）：本按钮那圈 **1 逻辑像素**边框的**实际设备像素宽** ——
	/// 算式与 <see cref="UiVisualStyle.OutlinePen(Control, Color, float, float, bool)" /> 完全一致
	/// （<c>max(1, round(1 × DeviceDpi / 96))</c>）：100%→1、125%→1、150%→2、175%→2、200%→2。
	/// <para>为什么需要它：顶部高光（<see cref="DrawTopHighlight" />）自己也走同一条 1 逻辑像素的
	/// <see cref="UiVisualStyle.OutlinePen(Control, Color, float, float, bool)" /> ⇒ 在 ≥150% DPI 下它是一条
	/// **2 设备像素**宽的线。inset 若仍按"边框只有 1px"写死成 1（夜间分支原来就是 1），这条 2px 高光
	/// 会以 y=1.5 为中心落在**第 1、2 行**，正好把边框的第 2 行换成亮色 ⇒ 上边看起来只剩 1px，
	/// 而左/右/下三边仍是 2px。inset 改为本值后，高光整体落到边框内侧（≥150% 为第 3、4 行），四边一致。</para>
	/// </summary>
	private int EdgeDevicePx()
	{
		return Math.Max(1, (int)Math.Round(UiDpi.Paint(this) / 96f, MidpointRounding.AwayFromZero));
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑤，仅夜间）：未选中按钮面顶部的 <b>1px</b> 高光，模拟「浮起」（比 ButtonFace 提亮 16%）。
	/// ⚠️ 这是**有意画的一条线**，不是一块异色矩形 —— 与「整块异色矩形」那条教训的区别在于：
	/// ① 只发一条 <see cref="Graphics.DrawLine" />，不 Fill 任何区域、不设任何 Region；
	/// ② 线的左右端点各内缩一个圆角半径（不碰圆角、更不可能越出按钮边界），y 落在按钮自身 1px 边框的内侧一行；
	/// ③ RButton 的 Region 早已是这条圆角路径，画到客户区外的部分会被裁剪 ⇒ 不可能在按钮之外形成"假边"；
	/// ④ 只在「底色属于本轮新增的按钮面家族」时才画：继承父色（＝页面底）的按钮不会被画上一条孤立亮线。
	/// 日间（IsNightMode=false）整个方法直接返回，日间像素不变。
	/// </summary>
	/// <summary>
	/// 2026-10-09（P0.5）：**已收敛到唯一工厂** <see cref="UiVisualStyle.OutlinePen(Control, Color, float, float, bool)" />。
	/// 原来这里有 4 处硬编码 <c>new Pen(..., 1f)</c>（顶部高光 / 日间内缩边 / 夜间硬边 / 分段边）——
	/// 它们的**数值**在 96 DPI 下与改动前完全相同（1 设备像素），但在 125%~200% 下会跟着 DPI 一起变粗
	/// （1 逻辑像素：125%→1、150%→2、175%→2、200%→2），不再出现"同一张界面上有的边随 DPI 变、有的不变"。
	/// </summary>
	private void DrawTopHighlight(Graphics graphics, Rectangle bounds, int radius, Color face, int inset)
	{
		if (!UiVisualStyle.IsNightMode || !UiVisualStyle.IsButtonFace(face))
		{
			return;
		}
		if (bounds.Width <= 4 || bounds.Height <= 4)
		{
			return;
		}
		int r = Math.Min(Math.Max(radius, 0), Math.Min(bounds.Width, bounds.Height) / 2);
		int x1 = bounds.Left + r;
		int x2 = bounds.Right - r;
		if (x2 - x1 < 2 || bounds.Top + inset + 1 >= bounds.Bottom)
		{
			return;
		}
		float y = (float)bounds.Top + (float)inset + 0.5f;
		// 2026-10-09（P0.5）：1 逻辑像素 = 1 设备像素@100%（与改动前相同）；不设 Inset。
		using Pen pen = UiVisualStyle.OutlinePen(this, UiVisualStyle.ButtonFaceHighlight, 1f, 1f, inset: false);
		graphics.DrawLine(pen, x1, y, x2 - 1, y);
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑤ 夜间；0.291 beta22 起**日间同构**）：按钮面悬停色。
	/// 底色属于"按钮面家族"时统一取 <see cref="UiVisualStyle.ButtonFaceHover" />
	/// （夜间 72/72/72、日间 218/227/238，都是各自主题里"比未选中再明确一档"的悬停底）；
	/// 其它底色（自定义 chip 色、语义色底等）仍沿用既有「按底色明度朝黑或白推 16%」的算法。
	/// ⚠️ 改前 0.291 之前日间按钮面是纯白 Input，走的是 16% 那条算法（悬停＝214,214,214，过重）；
	/// 本轮日间按钮面改回同族的 ButtonFace，悬停因此也收敛到 1.12 这一档（与夜间 1.169 同向）。
	/// </summary>
	private static Color HoverFace(Color face)
	{
		if (UiVisualStyle.IsButtonFace(face))
		{
			return UiVisualStyle.ButtonFaceHover;
		}
		int lum = (face.R * 30 + face.G * 59 + face.B * 11) / 100;
		return Shift(face, (lum > 128) ? Color.Black : Color.White, 0.16f);
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑦，仅夜间）：**边缘清晰化**的唯一落点。
	///
	/// 诊断（"为什么原来的边发虚"）：未选中按钮那圈 1px 边框是 <c>PenAlignment.Center</c> 画在
	/// 「向内 1px 的整数坐标路径」上的 —— 1px 线以路径为中心 ⇒ 中心线落在**两列像素的交界**上，
	/// 抗锯齿把一条实线摊成「两列各 ~50% 覆盖」的灰边（观感发虚、且与底色对比再被稀释一半）。
	/// 这也解释了同界面里的不一致：分段按钮走 DrawSegmentBorder 用的是 <c>PenAlignment.Inset</c>，
	/// 反而清晰；而 RComboBox 与这里的独立按钮都用 Center。
	///
	/// 改法（不引入锯齿）：夜间改为**画在按钮自身外沿路径上 + PenAlignment.Inset** —— 1px 整条落在
	/// 最外那一列/行像素内（纯色、无半像素混色）⇒ 直线段是硬边；**圆角处仍由 AntiAlias 平滑**，
	/// 与 Region（并集后外沿同样在 path 上、向外 1 设备像素）严丝合缝、不会被裁。
	/// 颜色：调用方给的边框色若正是普通的 <see cref="UiVisualStyle.Border" />（夜间 56/56/56 画在
	/// ButtonFace 62 上只有 Δ6，几乎看不见），夜间换成 <see cref="UiVisualStyle.ButtonEdge" />
	/// （82/82/82，Δ20，对页面底 28 的对比 3.1）；**语义色一律原样使用**（性能模式行的
	/// colorEco 绿 / colorStandard 蓝 / colorTurbo 红 / colorGray 灰、mini-chip 的自定义色），
	/// 故"待生效＝棕"等语义不受影响。
	/// 日间仍走 else（内缩 num4 + Center，几何与改动前逐像素相同）——0.291 beta22 补丁起**只有边色**
	/// 由 Border 换成 ButtonEdge（见方法体第一行）。
	/// </summary>
	private static void DrawCrispEdge(Graphics g, Control owner, GraphicsPath outerPath, Rectangle bounds, int radius, int borderWidth, Color color)
	{
		// 2026-10-09（0.291 beta22 补丁）：**两主题共用**这一步 —— "恰好等于普通 Border 的边色"一律换成
		// ButtonEdge。日间 Border(201,213,227) 对新按钮面 ButtonFace(217,224,234) 只有 1.12
		//（用户口径「浅色模式按钮快淡到看不见了」），换 ButtonEdge(168,183,201) 后 1.537；
		// 夜间同理（Border 56 画在 62 上 Δ6 → ButtonEdge 82 Δ20）。**语义色一律原样使用**
		//（mini-chip 的自定义色、colorEco/Standard/Turbo 等），故"待生效＝棕"等语义与触发条件不受影响。
		Color resolved = (UiVisualStyle.SameRgb(color, UiVisualStyle.Border) ? UiVisualStyle.ButtonEdge : color);
		if (!UiVisualStyle.IsNightMode)
		{
			int num = borderWidth / 2 + 1;
			if (bounds.Width - 2 * num <= 4 || bounds.Height - 2 * num <= 4)
			{
				return;
			}
			// 2026-10-09（P0.5）：硬编码 1f → 唯一工厂（1 逻辑像素、1 设备像素@100%、**Center 对齐**）。
			using GraphicsPath path = GetFigurePath(new Rectangle(num, num, bounds.Width - 2 * num, bounds.Height - 2 * num), radius, RSegmentPosition.None);
			using Pen pen = UiVisualStyle.OutlinePen(owner, resolved, 1f, 1f, inset: false);
			g.DrawPath(pen, path);
			return;
		}
		// 2026-10-09（P0.5）：硬编码 1f → 唯一工厂（Inset ⇒ 直线段为实心硬边，圆角仍由 AA 平滑）。
		using Pen crisp = UiVisualStyle.OutlinePen(owner, resolved, 1f, 1f);
		g.DrawPath(crisp, outerPath);
	}

	private static void DrawSegmentBorder(Graphics g, Control owner, Rectangle rect, int radius, Color color, RSegmentPosition position, int separatorInset, bool suppressSeparator)
	{
		// 2026-10-09（P0.5）：硬编码 1f → 唯一工厂（逻辑 1 像素 + Inset）。
		using Pen pen = UiVisualStyle.OutlinePen(owner, color, 1f, 1f);
		int num = rect.Width - 1;
		int num2 = rect.Height - 1;
		int num3 = Math.Min(radius, Math.Min(num, num2) / 2);
		switch (position)
		{
		case RSegmentPosition.First:
			g.DrawLine(pen, rect.X, rect.Y + num3, rect.X, rect.Bottom - num3);
			g.DrawLine(pen, rect.X + num3, rect.Y, rect.Right - 1, rect.Y);
			g.DrawLine(pen, rect.X + num3, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
			g.DrawArc(pen, rect.X, rect.Y, num3 * 2, num3 * 2, 180, 90);
			g.DrawArc(pen, rect.X, rect.Bottom - 1 - num3 * 2, num3 * 2, num3 * 2, 90, 90);
			break;
		case RSegmentPosition.Middle:
			g.DrawLine(pen, rect.X, rect.Y, rect.Right - 1, rect.Y);
			g.DrawLine(pen, rect.X, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
			if (!suppressSeparator)
			{
				g.DrawLine(pen, rect.X, rect.Y + separatorInset, rect.X, rect.Bottom - separatorInset);
			}
			break;
		case RSegmentPosition.Last:
			g.DrawLine(pen, rect.Right - 1, rect.Y + num3, rect.Right - 1, rect.Bottom - num3);
			g.DrawLine(pen, rect.X, rect.Y, rect.Right - num3, rect.Y);
			g.DrawLine(pen, rect.X, rect.Bottom - 1, rect.Right - num3, rect.Bottom - 1);
			g.DrawArc(pen, rect.Right - 1 - num3 * 2, rect.Y, num3 * 2, num3 * 2, 270, 90);
			g.DrawArc(pen, rect.Right - 1 - num3 * 2, rect.Bottom - 1 - num3 * 2, num3 * 2, num3 * 2, 0, 90);
			break;
		default:
		{
			using GraphicsPath path = GetFigurePath(new Rectangle(rect.X, rect.Y, num, num2), num3, RSegmentPosition.None);
			g.DrawPath(pen, path);
			break;
		}
		}
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）—— 让"系统在我们之外再画一遍"这条路径彻底消失。
	/// 只处理这两条消息，其余**原样**交给 <c>base.WndProc</c>（按下/抬起状态、OwnerDraw、快捷键、
	/// Default/Cancel 语义、禁用态、点击与事件一律未动）。
	/// </summary>
	protected override void WndProc(ref Message m)
	{
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		base.WndProc(ref m);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hover = true;
		if (segmentPosition != RSegmentPosition.None)
		{
			Invalidate();
		}
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hover = false;
		if (segmentPosition != RSegmentPosition.None)
		{
			Invalidate();
		}
		base.OnMouseLeave(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		if (segmentPosition != RSegmentPosition.None)
		{
			Invalidate();
		}
		base.OnEnabledChanged(e);
	}

	protected override void OnPaint(PaintEventArgs pevent)
	{
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（UiDpi.Paint(this) = Control.DeviceDpi），
		// 不再读绘制 DC 的 Graphics.DpiX（后者在 PerMonitorV2 下可能回 96，会让圆角/边宽偏小且每台机器不同）。
		// 该值只用于"逻辑像素 × DPI"的换算，几何算式一个都没动；96 DPI 下与改动前逐像素相同。
		float num = UiDpi.Paint(this) / 192f;
		int num2 = (int)Math.Round((num * (float)borderSize - 1f) / 2f) * 2 + 1;
		// 2026-10-09（UI 打磨⑦，仅夜间）：圆角半径统一收紧到"小圆角"（4 设备像素 @96DPI）。
		// 原来这里直接读 BorderRadius（调用方给 16/12/10 ⇒ 96 DPI 下 8/6/5px，观感偏圆）；
		// 现走 UiVisualStyle.EffectiveCornerRadius —— 夜间 min(配置值, 8)，**只收不放**，
		// 日间原样返回（日间仍是 16/12/10，像素与改动前逐点相同）。
		// ⚠️ 半径仍是「num × 单位」的整数结果，且被 GetFigurePath 的 num=max(1,radius*2) 与
		//    取整半径共同约束在小值（4~5px @96DPI），不可能超过行高一半。
		int num3 = (int)Math.Round(num * (float)UiVisualStyle.EffectiveCornerRadius(borderRadius), MidpointRounding.AwayFromZero);
		Rectangle clientRectangle = ClientRectangle;
		// 2026-10-09（UI 反馈①-B）：本条绘制链所有"仅夜间"的新画法共用一个开关（日间一个都不走 ⇒ 像素不变）。
		bool nightPaint = UiVisualStyle.IsNightMode;
		if (segmentPosition == RSegmentPosition.None)
		{
			Color bgInput = BackColor;
			// 2026-10-09（UI 打磨⑤，仅夜间）：悬停色改走 HoverFace（按钮面家族 → ButtonFaceHover；
			// 其余底色仍是原来的 Shift 0.16，日间逐像素不变）。
			Color bgColor = ((!activated && _hover && Enabled) ? HoverFace(bgInput) : bgInput);
			// 2026-10-09（UI 打磨③ 仅夜间；0.291 beta22 起**日间同构**，用户追加口径
			// 「边框高亮 和性能模式浅色填充依旧没有」）：独立按钮（SegmentPosition=None，如「刷新率」行、
			// 「核显省电」OFF/ON/AUTO、「MiniLED/FHD/HDR」三颗）的选中态也走「彩边 + 底」那一套 ——
			// **不再按主题门控**：夜间是"彩边 + 与未选中同级的深底"，日间是"彩边 + 中性底
			// （非模式色组）/ 模式色浅填充（性能模式行）"，两者共用同一段几何（铺满整颗 + 2 设备像素
			// 描边画在按钮自身圆角路径上 + 单层边）。未选中/悬停两主题都不进本分支。
			bool outlineSelectedNone = activated && activatedOutline;
			// 2026-10-09（P1；原「UI 反馈①-B，仅夜间」）：圆角外那圈（含 Region 外扩的 2 设备像素）统一铺成
			// **父容器在该位置真正画出来的底色**（父链第一个实色容器的 BackColor，UiVisualStyle.ContextBackColor）。
			// **P1 起日间也取本值**（原来日间用 `Parent?.BackColor`）—— 父链上任何一层是过时快照/透传容器时，
			// 旧写法会在圆角外露出一圈异色（发丝状"假边"），而锯齿感有一部分就来自"被二值 Region 切掉的
			// 抗锯齿羽化边 + 异色环"。日间只在**颜色取法**上向夜间对齐，几何/线宽除 Region 外扩量外未动。
			Color ringColor = UiVisualStyle.ContextBackColor(this);
			using (GraphicsPath path = GetFigurePath(clientRectangle, num3 + num2, segmentPosition))
			using (Pen pen = new Pen(ringColor, num2))
			{
				pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				// 2026-10-09（P3）：**不设 PixelOffsetMode**（本处从未设置过，P3 把这条口径提升为全仓统一口径）——
				// GDI+ 的 HighQuality/Half 会把采样网格整体偏移半个像素，对我们这种"整数坐标 + Inset 的整数设备像素边"
				// 正好是把一条硬边摊成两列各 50%（＝发虚）。全仓唯一口径 = **不设**（GDI+ 默认 None），见 RButton
				// 文件头与《UI 样式规范》；HardwareOverlay 里原先显式设的 HighQuality 已按同一口径删除。
				// 2026-10-09（UI 打磨⑥）：裁剪区向外让出设备像素（见 UiVisualStyle.SetRoundedRegion）。
				// 原来的 `Region = new Region(path)` 是二值掩码，会把抗锯齿画出来的那一条羽化边整条切掉 ⇒ 锯齿感。
				// 2026-10-09（P1）：外扩量**两个主题统一为 2 设备像素**（RegionOutsetDevicePx）——
				// 原来日间传 Dpi/96（1 设备像素）、夜间传 2：同一种按钮在日间只让出 1 ⇒ 圆角处 AA 羽化仍被啃掉
				// 一条，日间圆角就是比夜间"方一点"。统一到 2 之后日间圆角的平滑度与夜间一致，
				// 多让出的那一圈仍由上面 ringColor（父容器真色）铺满 ⇒ 不可见（**不是**靠"凑容差"）。
				UiVisualStyle.SetRoundedRegion(this, path, UiVisualStyle.RegionOutsetDevicePx);
				// 2026-10-09（UI 反馈①-B 起，P1 扩展为两主题）：整块客户区先铺父容器底色 —— 圆角之外（以及
				// Region 外扩出来的 2 设备像素）与父容器逐分量一致 ⇒ 外扩不会露出发丝状异色环，抗锯齿羽化边
				// 也不再被啃掉。**P1 起日间也铺**（日间外扩量已从 1 提到 2，不铺就一定会露出一圈环）。
				// 只填底色，随后的圆角面/边照旧覆盖中间区域；颜色与 WM_ERASEBKGND 的擦除色逐分量相同
				//（都是 UiVisualStyle.ContextBackColor(this)），不改任何 Bounds/Margin/Region 之外的东西。
				if (UiVisualStyle.FormHasBackgroundImage(this))
		{
			UiVisualStyle.DrawFormBackgroundSlice(pevent.Graphics, this);
		}
		using (SolidBrush contextBrush = new SolidBrush(ringColor))
				{
					pevent.Graphics.FillRectangle(contextBrush, clientRectangle);
				}
				pevent.Graphics.DrawPath(pen, path);
				using (SolidBrush brush = new SolidBrush(UiVisualStyle.TranslucentFor(this, bgColor, 190)))
				{
					pevent.Graphics.FillPath(brush, path);
				}
				// 2026-10-09（UI 打磨⑦，仅夜间）：边缘清晰化。
				// ① 有自己的边框色 ⇒ 照旧用它（语义色不动），但夜间改由 DrawCrispEdge 画在外沿路径 + Inset；
				// ② 没有边框色、而底色确实属于「按钮面」家族（浮起）且未选中 ⇒ 夜间补一圈 ButtonEdge 1px 硬边，
				//    让未选中态也有明确轮廓（与 G-Helper 的方硬按键一致）；日间不补（像素不变）。
				// ⚠️ ④ 的"选中态抑制自身边框"仍原样保留（`!outlineSelectedNone`）：选中时那圈 2px 彩边
				//    就是本按钮唯一的边，不会并排出现第二圈。
				Color? edgeColor = null;
				if (Enabled && !Borderless)
				{
					if (borderColor.A > 0)
					{
						edgeColor = borderColor;
					}
					else if (!activated && UiVisualStyle.IsButtonFace(bgColor))
					{
						// 2026-10-09（0.291 beta22 补丁）：**不再按主题门控** —— 日间未选中按钮原先那圈
						// UiVisualStyle.Border(201,213,227) 画在新按钮面 217,224,234 上只有 1.12，
						// 用户口径「浅色模式按钮快淡到看不见了」就有它一份；两主题统一用 ButtonEdge
						//（日 168,183,201 / 夜 82,82,82），对各自按钮面的对比 1.537 / 1.369。
						edgeColor = UiVisualStyle.ButtonEdge;
					}
				}
				if (edgeColor.HasValue && !outlineSelectedNone)
				{
					DrawCrispEdge(pevent.Graphics, this, path, clientRectangle, num3, num2, edgeColor.Value);
				}
				if (outlineSelectedNone)
				{
					// ④：深底铺满整颗（填充矩形＝ClientRectangle，不再向内缩），彩边画在按钮自身圆角路径
					//（radius = num3 + num2，与上面画本按钮 Region/外沿的那条 path 完全相同）上。
					// 2026-10-09（UI 反馈①）：颜色分档（模式色 / 灰或蓝底边）由本方法按 SelectionUsesAssignedAccent 决定。
					// 2026-10-09（UI 反馈②）：非模式色那一档的**边**已由中性灰改为强调蓝（底未动）。
					DrawActivatedOutline(pevent.Graphics, clientRectangle, num3 + num2);
				}
				else if (!activated)
				{
					// 2026-10-09（UI 打磨⑤，仅夜间）：未选中/悬停的独立按钮画 1px 顶部高光（浮起感）。
					// 有那圈 BorderColor 时高光落在它内侧一行，没有边框时贴着第一行；选中态不画（选中由彩边表达）。
					// 2026-10-09（UI 打磨⑦）：夜间那圈边固定落在最外一行（Inset），故高光也固定落在它内侧的第 2 行，
					// 与分段分支（DrawSegmentBorder 画在最外行 + 高光 inset=1）保持同一种「边 + 紧贴其下一条高光」。
					// 2026-10-09（fix「上边框非常细」）：inset 必须跟着**边框的实际设备像素宽**走（EdgeDevicePx），
					// 否则 ≥150% DPI 下这条同样 2px 宽的高光会压掉边框的第 2 行，把上边削成 1px。
					// 100% DPI 下 EdgeDevicePx=1 ⇒ 与改动前逐像素相同。
					int edgeDevicePx = EdgeDevicePx();
					bool hasOwnEdge = Enabled && !Borderless && (borderColor.A > 0 || (!activated && UiVisualStyle.IsButtonFace(bgColor)));
					int highlightInset = (hasOwnEdge ? Math.Max(num2 / 2 + 2, edgeDevicePx) : 1);
					if (UiVisualStyle.IsNightMode)
					{
						highlightInset = (hasOwnEdge ? edgeDevicePx : 1);
					}
					DrawTopHighlight(pevent.Graphics, clientRectangle, num3 + num2, bgColor, highlightInset);
				}
			}
			Rectangle textRect = clientRectangle;
			if (Image != null)
			{
				int iconSide = Image.Width;
				int iconX = (clientRectangle.Width - iconSide) / 2;
				int iconY = clientRectangle.Top + (int)Math.Round(num * 6f);
				pevent.Graphics.DrawImage(Image, iconX, iconY, iconSide, iconSide);
				textRect = new Rectangle(clientRectangle.X, iconY + iconSide, clientRectangle.Width, clientRectangle.Bottom - iconY - iconSide);
			}
			// 深底必须配浅字（Text），否则「AccentText 深字」会糊在深底上；未选中/日间仍是原 ForeColor。
			TextRenderer.DrawText(pevent.Graphics, Text, Font, textRect, Enabled ? (outlineSelectedNone ? UiVisualStyle.Text : ForeColor) : UiVisualStyle.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			return;
		}
		// 2026-10-09（UI 打磨④）：这个 D(8) 内缩**只**用于日间/legacy 的实心强调色药丸（②b 之前就是
		// `num * 8f`，逐像素相同）；夜间选中态的「彩边 + 深底」不再读任何内缩值 —— 一律铺满整颗按钮。
		int num5 = (int)Math.Round(num * 8f);
		int separatorInset = (int)Math.Round(num * 10f);
		// 2026-10-09（UI 打磨⑦，仅夜间）：日间/legacy 实心强调色药丸的半径同样统一收紧（12 单位 → 8 单位，
		// 96 DPI 下 6px → 4px）；日间 EffectiveCornerRadius 原样返回 12 ⇒ 日间像素不变。
		int radius = (int)Math.Round(num * (float)UiVisualStyle.EffectiveCornerRadius(12));
		bool flag = false;
		if (Parent is TableLayoutPanel tableLayoutPanel)
		{
			TableLayoutPanelCellPosition positionFromControl = tableLayoutPanel.GetPositionFromControl(this);
			if (positionFromControl.Column > 0)
			{
				flag = (tableLayoutPanel.GetControlFromPosition(positionFromControl.Column - 1, positionFromControl.Row) as RButton)?.Activated ?? false;
			}
		}
		// 2026-10-09（UI 打磨⑤ 夜间；0.291 beta22 起**日间同构**；用户原话「它的按钮颜色是更浅 背景更深 我们是反的」）：
		// 分段/独立按钮面原先恒取 UiVisualStyle.Input（夜间比页面底 Window 还暗 ⇒「凹下去」；
		// 日间是纯白 ⇒ 与纯白卡片同色、且与普通按钮面 ButtonFace 形成"两套按钮面"）。
		// 本轮两个主题统一取 ButtonFace：夜间 62/62/62（比卡片与页面底都更浅＝浮起）、
		// 日间 233/239/246（浅色主题里"比纯白卡片深一档的浅灰面 + 一圈硬边"就是浮起的唯一可行表达）。
		Color input2 = UiVisualStyle.ButtonFace;
		Color color = ((!activated && _hover && Enabled) ? HoverFace(input2) : input2);
		// 2026-10-09（UI 打磨②b 仅夜间；0.291 beta22 起**日间同构**）：选中态改「彩边 + 底」时 —— 
		// ⚠️ 这里**不再按主题门控**（改前是 `&& UiVisualStyle.IsNightMode`，于是日间永远走"实心强调色药丸"，
		// 就是用户截图里"平衡 / 独显直连 / 180 都是实心饱和蓝块"的来源）。两主题现在共用同一套几何：
		// 铺满整颗客户区 + 2 设备像素描边画在按钮自身圆角路径上 + 选中时抑制自身那圈普通边（单层边）。
		// 差别只在配色（见 DrawActivatedOutline / UiVisualStyle.ModeActivatedFill / SelectionOutlineColor）：
		// 夜间＝"只描边、不做浅填充"，日间＝"描边 + 中性底（非模式色组）或模式色浅填充（性能模式行）"。
		// 文字：选中态一律用 Text（未选中/悬停/禁用不变）。
		bool outlineSelected = activated && activatedOutline;
		Color foreColor;
		if (Enabled)
		{
			foreColor = ((activated && !outlineSelected) ? (activatedTextColor.IsEmpty ? UiVisualStyle.AccentText : activatedTextColor) : UiVisualStyle.Text);
		}
		else
		{
			foreColor = UiVisualStyle.Muted;
		}
		using (GraphicsPath path3 = GetFigurePath(clientRectangle, num3 + num2, segmentPosition))
		{
			// 2026-10-09（P1）：分段按钮的 ringColor 也与独立按钮统一 —— 两主题都取"父链第一个实色容器的底色"。
			Color ringColor3 = UiVisualStyle.ContextBackColor(this);
			using Pen pen3 = new Pen(ringColor3, num2);
			pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			// 2026-10-09（P3）：**不设 PixelOffsetMode**（全仓统一口径 = 不设）。
			// 2026-10-09（P1）：与独立按钮（None）同一套 —— Region 外扩 **2 设备像素**（两主题一致）+
			// 圆角外整块铺父容器真色。
			UiVisualStyle.SetRoundedRegion(this, path3, UiVisualStyle.RegionOutsetDevicePx);
			if (UiVisualStyle.FormHasBackgroundImage(this))
		{
			UiVisualStyle.DrawFormBackgroundSlice(pevent.Graphics, this);
		}
		using (SolidBrush contextBrush3 = new SolidBrush(ringColor3))
			{
				pevent.Graphics.FillRectangle(contextBrush3, clientRectangle);
			}
			pevent.Graphics.DrawPath(pen3, path3);
			using (Brush brush3 = new SolidBrush(UiVisualStyle.TranslucentFor(this, color, 190)))
			{
				pevent.Graphics.FillPath(brush3, path3);
			}
			// 2026-10-09（UI 打磨④）：选中态抑制按钮自身那圈 UiVisualStyle.Border 边框（Single 走
			// DrawSegmentBorder 的 default 分支画一整圈圆角描边）——选中时只保留那圈 2px 彩边，避免双层边。
			// 未选中/悬停照旧：这一圈就是按钮唯一的一层普通边框。
			if (!outlineSelected)
			{
				// 2026-10-09（UI 打磨⑦ 仅夜间；0.291 beta22 补丁起**日间也用 ButtonEdge**）：边色与独立按钮
				// 统一 —— 夜间取 ButtonEdge（82/82/82，比 Border 56 清晰）；日间同样取 ButtonEdge
				//（168,183,201），因为日间 Border(201,213,227) 画在新按钮面 217,224,234 上只有 1.12
				//（用户口径「浅色模式按钮快淡到看不见了」），ButtonEdge 是 1.537。
				// DrawSegmentBorder 内部本来就是 PenAlignment.Inset ⇒ 已经是硬边，无需改画法。
				DrawSegmentBorder(pevent.Graphics, this, clientRectangle, num3, UiVisualStyle.ButtonEdge, segmentPosition, separatorInset, activated | flag);
			}
			// 2026-10-09（UI 打磨⑤，仅夜间）：未选中/悬停的分段按钮画 1px 顶部高光（浮起感）。
			// 位置固定在最外那圈 1px Border 的内侧一行；选中态不画（选中由 2px 彩边 + ButtonFaceSelected 底表达）。
			// 2026-10-09（fix「上边框非常细」）：同一个 inset 口径 —— 内侧一行 = **边框的实际设备像素宽**
			// （EdgeDevicePx：100%→1；150%/175%/200%→2），否则 2px 宽的高光会压掉分段边上边（DrawSegmentBorder
			// 在 ≥150% 下也是 2 设备像素）的第 2 行，上边比左/右/下细一半。100% DPI 下与改动前逐像素相同。
			if (!activated)
			{
				DrawTopHighlight(pevent.Graphics, clientRectangle, num3 + num2, color, EdgeDevicePx());
			}
		}
		if (activated)
		{
			Color accentColor = (activatedColor.IsEmpty ? UiVisualStyle.Accent : activatedColor);
			// 2026-10-09（UI 打磨②b，仅夜间）：选中态＝「彩色描边(2px, DPI 缩放, Inset 全落在控件内) +
			// 深底（SurfaceRaised 混 18% 该模式色）」。模式色由描边承载，故静音绿/平衡蓝/静音狂暴浅红/
			// 高能狂暴大红/自定义浅蓝与 GPU 行的「待生效＝棕」语义都还在。
			// 2026-10-09（UI 打磨⑤）：基色改为 ButtonFaceSelected（见 UiVisualStyle.ModeActivatedFill），
			// 描边改为该强调色提亮 12%（UiVisualStyle.SelectionOutlineColor）——「更浅的底 + 更亮的边」。
			// 日间（IsNightMode=false）走 else 分支，与改动前逐像素相同。
			// 2026-10-09（UI 打磨③）：这段几何抽成 DrawActivatedOutline，与独立按钮（None 分支）共用同一份代码。
			// 2026-10-09（UI 打磨④）：深底铺满整颗按钮（填充矩形＝ClientRectangle，不再内缩），彩边画在
			// 按钮自身圆角路径上（radius = num3 + num2，与上面 pen3/Region 用的那条 path 完全相同）。
			if (outlineSelected)
			{
				// 2026-10-09（UI 反馈①）：颜色分档由本方法按 SelectionUsesAssignedAccent 决定，
				// 不再由调用方在这里兜底取 Accent（那会让"没给语义色"的行借到强调蓝）。
				// 2026-10-09（UI 反馈②）：非模式色那一档的边已由中性灰(Muted 158)改为强调蓝(Accent 77/163/255)，
				// 底仍是 ButtonFace 62（不浅填充）；模式色行与「待生效＝棕」一字未改。
				DrawActivatedOutline(pevent.Graphics, clientRectangle, num3 + num2);
			}
			else
			{
				Rectangle rect = clientRectangle;
				rect.Inflate(-num5, -num5);
				if (rect.Width > 4 && rect.Height > 4)
				{
					using GraphicsPath path4 = GetFigurePath(rect, radius, RSegmentPosition.Single);
					using SolidBrush brush4 = new SolidBrush(accentColor);
					pevent.Graphics.FillPath(brush4, path4);
				}
			}
		}
		Font font = (activated ? UiVisualStyle.TempFont(Font.Size, FontStyle.Bold) : Font);
		TextRenderer.DrawText(pevent.Graphics, Text, font, clientRectangle, foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		if (activated && font != Font)
		{
			font.Dispose();
		}
	}
}
