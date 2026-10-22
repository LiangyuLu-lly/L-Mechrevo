using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RCheckBox : CheckBox
{
	private const int TrackWidth = 38;

	private const int TrackHeight = 20;

	private const int InlineTrackWidth = 30;

	private const int InlineTrackHeight = 16;

	private const int KnobDiameter = 12;

	private const int TextGap = 8;

	private const int StateGap = 6;

	private const int FocusRadius = 6;

	private const int SlideMilliseconds = 120;

	private static readonly Color KnobOnColor = Color.White;

	private readonly Timer slideTimer = new Timer
	{
		Interval = 16
	};

	private int borderRadius = 5;

	private bool switchOnRight;

	private bool showStateText;

	private float knobPosition;

	private float slideFrom;

	private long slideStarted;

	private Font? stateFont;

	/// <summary>
	/// 2026-10-06（igpuscope）：关闭态滑轨颜色的**可选**覆盖。null（默认）= 沿用原有配色
	/// （夜间主题 Track / 日间主题 Input），其它开关一律不动。
	/// 加它的原因：日间主题里 Input 与 Surface 都是纯白，放在 Surface 卡片上的开关（设置对话框
	/// 「显示核显模式」那一行）关闭时滑轨会和卡片底色完全同色 —— 只有那个开关需要显式指定 Track。
	/// </summary>
	private Color? trackColor;

	public Color? TrackColor
	{
		get
		{
			return trackColor;
		}
		set
		{
			if (!(trackColor == value))
			{
				trackColor = value;
				Invalidate();
			}
		}
	}

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

	public bool SwitchOnRight
	{
		get
		{
			return switchOnRight;
		}
		set
		{
			if (switchOnRight != value)
			{
				switchOnRight = value;
				ResizeForAutoSize();
				Invalidate();
			}
		}
	}

	public bool ShowStateText
	{
		get
		{
			return showStateText;
		}
		set
		{
			if (showStateText != value)
			{
				showStateText = value;
				ResizeForAutoSize();
				Invalidate();
			}
		}
	}

	private Font StateFont => stateFont ?? (stateFont = new Font("Consolas", Math.Max(6f, Font.Size - 1f)));

	private TextFormatFlags LabelFlags
	{
		get
		{
			TextFormatFlags textFormatFlags = TextFormatFlags.SingleLine;
			if (!UseMnemonic)
			{
				textFormatFlags |= TextFormatFlags.NoPrefix;
			}
			return textFormatFlags;
		}
	}

	public RCheckBox()
	{
		DoubleBuffered = true;
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		// 2026-10-08（用户要求：去掉开关周围那圈淡框线）：底色默认取 Window（窗口/行底色）而不是 Surface。
		// 成因：本控件 OnPaint 第一件事就是 FillRectangle(ClientRectangle, BackColor)，于是整个控件矩形
		// 都会带上 BackColor —— 原来默认 Surface(#111A2B 夜)，而开关所在的标题行/校色行底色是 Window(#0B1220 夜)，
		// 两者差一档，开关四周就出现一圈比背景亮的矩形（观感即"淡淡的框"）。改成 Window 后与所在行同色，
		// 只在真正落在 Surface 卡片上的开关由调用方显式指定底色（见 UiVisualStyle.ApplyTree 的
		// checkBox.BackColor = Parent.BackColor）。
		BackColor = UiVisualStyle.Window;
		knobPosition = (Checked ? 1f : 0f);
		slideTimer.Tick += OnSlideTick;
	}

	public override Size GetPreferredSize(Size proposedSize)
	{
		return MeasurePreferredSize(ResponsiveLayout.LogicalToDevice(this, 96));
	}

	/// <summary>
	/// 2026-10-08（收尾六：从根上消除开关填充矩形边缘的淡线）：控件矩形的填充色。
	/// 本控件 OnPaint 第一件事就是 FillRectangle(ClientRectangle, 填充色)，所以这个颜色就是开关那一整块
	/// 矩形；它只要与**所在行实际被画出来的底色**差一档，那块矩形的上/左边缘就会显成一条淡线。
	///
	/// 旧实现（收尾三）带「分量差都 &lt;= 8 才采用父底色」的容差：差得比 8 多就回退到自己的 BackColor ——
	/// 而 BackColor 往往是更早一轮 UiVisualStyle.ApplyTree 按"当时的父容器"写进来的快照，父容器事后被
	/// ApplyRow/SetContainerColor 改成 Window 之后它就过时了；容差只让 6/6/11 这类小差被吸收（观感"变淡"），
	/// 13/10/6 这类超出容差的仍然照旧填出一块异色矩形（观感"不消失"），这就是上一轮"变淡但不消失"的原因。
	///
	/// 现在改为**无条件**沿父链取第一个实色容器的 BackColor（父链全透明/空才回退 Window）：
	/// 由于 WinForms 的子控件不透明填充仍是在父容器已经把整块客户区画完之后叠上去的，
	/// 「第一个实色父容器的 BackColor」就是该位置实际被画出来的底色，填充色与行底色**逐分量完全相等**，
	/// 矩形的每一条边都不再可见 —— 不是"画一条同色线去盖"，而是"填的颜色本身就是那个底色"。
	/// 顺带：显式把底色设成父色的那种刻意对比（UiVisualStyle.ApplyTree 的 checkBox.BackColor = Parent.BackColor）
	/// 结果不变；若将来真有开关需要与所在卡片不同色的实心底，应把它放进同色容器再设底色，而不是让本控件去猜。
	/// </summary>
	private Color SurfaceColor()
	{
		// 2026-10-09（UI 打磨①）：逻辑一个字没改，只是把这份"沿父链取第一个实色容器底色"
		// 抽到 UiVisualStyle.ContextBackColor（滑块/数字框也走同一份），避免三处各写一遍。
		return UiVisualStyle.ContextBackColor(this);
	}

	protected override void OnPaint(PaintEventArgs pevent)
	{
		if (Appearance != Appearance.Normal)
		{
			base.OnPaint(pevent);
			return;
		}
		Graphics graphics = pevent.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		// 2026-10-22（机主：底色填充删除 + 开关半透明）：背景图模式下不再填整块底色，
		// 滑轨/圆钮用半透明画法直接叠在图片上。
		bool translucent = UiVisualStyle.FormHasBackgroundImage(this);
		if (!translucent)
		{
			using (SolidBrush brush = new SolidBrush(SurfaceColor()))
			{
				graphics.FillRectangle(brush, ClientRectangle);
			}
		}
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读绘制 DC 的 Graphics.DpiX。
		float num = UiDpi.Paint(this) / 96f;
		Size size = TrackSize(num);
		Size size2 = (ShowStateText ? MeasureText("OFF", StateFont) : Size.Empty);
		int num2 = (int)Math.Round(8f * num);
		int num3 = (int)Math.Round(6f * num);
		bool flag = size2.Width > 0;
		int num4 = (SwitchOnRight ? (Width - size.Width) : (flag ? (size2.Width + num3) : 0));
		Rectangle track = new Rectangle(num4, (Height - size.Height) / 2, size.Width, size.Height);
		Rectangle bounds = (flag ? new Rectangle(SwitchOnRight ? (num4 - num3 - size2.Width) : 0, 0, size2.Width, Height) : Rectangle.Empty);
		int num5 = ((!SwitchOnRight) ? (num4 + size.Width + num2) : 0);
		int num6 = (SwitchOnRight ? ((flag ? bounds.Left : num4) - num2) : Width);
		Rectangle bounds2 = new Rectangle(num5, 0, Math.Max(0, num6 - num5), Height);
		DrawText(graphics, bounds2, Text, Font, Enabled ? UiVisualStyle.Text : UiVisualStyle.Muted);
		if (flag)
		{
			DrawText(graphics, bounds, Checked ? "ON" : "OFF", StateFont, (Enabled && Checked) ? UiVisualStyle.Accent : UiVisualStyle.Muted);
		}
		DrawSwitch(graphics, track, translucent ? 0.72f : 1f);
		// 2026-10-08：焦点框整类不画（沿用此前的删除决定）。本控件不是键盘交互目标：
		// LM 里所有开关都是鼠标点击式（点后被点的开关还会把焦点交回父容器，见
		// SettingsForm「显示核显模式」那一段），所以不保留键盘焦点提示，可访问性上没有实际损失。
		// 取舍：若将来有开关需要 Tab 键盘可达，再按 Focused 条件补画一圈强调色描边即可。
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）。
	/// 这正是历史「开关上方偶发淡线」（`UI-淡线问题-存档搁置.md`）**唯一没被试过的共因**：
	/// 该线是"失效域边界"上的旧像素/系统刷子底色（`Control.WndProc` 默认用**本控件自己的 BackColor** 刷子
	/// 去擦那条带），而本控件/所在行的真色是父链第一个实色容器的 BackColor ⇒ 两者差几个数值时，
	/// 那条被擦出来的带就是一条**淡线**（悬停触发完整重绘后消失，与历史签名完全对应）。
	/// 现在那条带被擦成父容器真色 ⇒ 与周围逐分量相等。
	/// 其余消息原样交给 <c>base.WndProc</c>（Checked 变化、点击、空间/回车、禁用态一律未动）。
	/// </summary>
	protected override void WndProc(ref Message m)
	{
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		base.WndProc(ref m);
	}

	protected override void OnCheckedChanged(EventArgs e)
	{
		base.OnCheckedChanged(e);
		StartSlide();
		Invalidate();
	}

	protected override void OnFontChanged(EventArgs e)
	{
		base.OnFontChanged(e);
		stateFont?.Dispose();
		stateFont = null;
		ResizeForAutoSize();
		Invalidate();
	}

	/// <summary>
	/// 2026-10-08（收尾五：重绘残留）：本控件 OnPaint 第一件事就是把 ClientRectangle 整块 Fill 掉
	/// （见 OnPaint），所以只要父容器重新布局把我们挪了位置，就必须自己重绘一次；
	/// 否则控件矩形内会留着上一个位置的像素 —— 观感就是开关上方/周围那条偶发细线，
	/// 直到鼠标悬停触发一次重绘才消失。ControlStyles.ResizeRedraw 只覆盖「窗口尺寸变化」，
	/// 覆盖不到「只挪位置、尺寸没变」这条路径。
	/// </summary>
	protected override void OnLocationChanged(EventArgs e)
	{
		base.OnLocationChanged(e);
		Invalidate();
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		if (Visible)
		{
			// 由隐藏切回显示时，矩形里可能还是隐藏前（或别的控件）的像素。
			Invalidate();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			slideTimer.Stop();
			slideTimer.Dispose();
			stateFont?.Dispose();
		}
		base.Dispose(disposing);
	}

	internal Size MeasurePreferredSize(int dpi)
	{
		float num = (float)dpi / 96f;
		Size size = TrackSize(num);
		Size size2 = MeasureText(Text, Font);
		Size size3 = (ShowStateText ? MeasureText("OFF", StateFont) : Size.Empty);
		int num2 = size.Width + size2.Width;
		if (size2.Width > 0)
		{
			num2 += (int)Math.Round(8f * num);
		}
		int num3 = Math.Max(size.Height, Math.Max(size2.Height, size3.Height));
		if (size3.Width > 0)
		{
			num2 += size3.Width + (int)Math.Round(6f * num);
			num3 = Math.Max(num3, size3.Height);
		}
		return new Size(num2, num3);
	}

	private Size TrackSize(float scale)
	{
		return new Size((int)Math.Round((float)(SwitchOnRight ? 38 : 30) * scale), (int)Math.Round((float)(SwitchOnRight ? 20 : 16) * scale));
	}

	private Size MeasureText(string? text, Font font)
	{
		if (!string.IsNullOrEmpty(text))
		{
			return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), LabelFlags);
		}
		return Size.Empty;
	}

	private void DrawText(Graphics g, Rectangle bounds, string? text, Font font, Color color)
	{
		if (!string.IsNullOrEmpty(text) && bounds.Width > 0)
		{
			TextRenderer.DrawText(g, text, font, bounds, color, LabelFlags | TextFormatFlags.Default | TextFormatFlags.VerticalCenter);
		}
	}

	private static Color WithAlpha(Color color, float alpha)
	{
		if (alpha >= 1f || color.A == 0)
		{
			return color;
		}
		return Color.FromArgb((int)Math.Round(255f * Math.Clamp(alpha, 0f, 1f)), color.R, color.G, color.B);
	}

	private void DrawSwitch(Graphics g, Rectangle track, float alpha = 1f)
	{
		int num = track.Height / 2;
		using (GraphicsPath path = RComboBox.RoundedRect(track, num, num))
		{
			// 2026-10-06（igpuscope）：TrackColor 有值时优先用它（只有需要显式区分卡片的开关会设）。
			Color trackFill = trackColor ?? (UiVisualStyle.IsNightMode ? UiVisualStyle.Track : UiVisualStyle.Input);
		using SolidBrush brush = new SolidBrush(WithAlpha(trackFill, alpha));
			g.FillPath(brush, path);
		}
		int num2 = (int)Math.Round(knobPosition * (float)track.Width);
		if (num2 >= track.Height)
		{
			using GraphicsPath path2 = RComboBox.RoundedRect(new Rectangle(track.X, track.Y, num2, track.Height), num, num);
			using SolidBrush brush2 = new SolidBrush(WithAlpha(Enabled ? UiVisualStyle.Accent : UiVisualStyle.Muted, alpha));
			g.FillPath(brush2, path2);
		}
		// 2026-10-08：滑轨描边整类不画（只 Fill 滑轨与圆钮，不 DrawPath）—— 这是「开关周围那圈淡框线」
		// 的历史来源之一（beta21 原始实现在此 `new Pen(UiVisualStyle.Border)` + DrawPath 描一圈）；
		// 保留 Fill 的滑轨/圆钮绘制，仅去掉描边，滑轨形状与配色不变。
		int num4 = Math.Max(4, (int)Math.Round((float)track.Height * 0.6f));
		int num5 = (track.Height - num4) / 2;
		float num6 = track.Width - 2 * num5 - num4;
		float num7 = (float)(track.X + num5) + (float)num4 / 2f + knobPosition * num6;
		Color color;
		if (Checked)
		{
			color = (Enabled ? KnobOnColor : UiVisualStyle.Surface);
		}
		else
		{
			color = UiVisualStyle.Muted;
		}
		using SolidBrush brush3 = new SolidBrush(WithAlpha(color, Math.Min(1f, alpha + 0.15f)));
		g.FillEllipse(brush3, num7 - (float)num4 / 2f, track.Y + num5, num4, num4);
	}

	private void DrawFocusRing(Graphics g, float scale)
	{
		int num = Math.Max(1, Math.Min((int)Math.Round(6f * scale), (Height - 1) / 2));
		using GraphicsPath path = RComboBox.RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), num, num);
		// 2026-10-09（P0.5）：1 逻辑像素描边统一走工厂（100%→1、150%→2、200%→2 设备像素）。
		// ⚠️ 本方法当前无调用点（焦点框"整类不画"是既有决定，见 OnPaint 末尾注释），保留以备键盘可达时启用。
		using Pen pen = UiVisualStyle.OutlinePen(this, UiVisualStyle.Accent, 1f, 1f);
		g.DrawPath(pen, path);
	}

	private void ResizeForAutoSize()
	{
		if (AutoSize)
		{
			Size = GetPreferredSize(Size.Empty);
		}
	}

	private void StartSlide()
	{
		float num = (Checked ? 1f : 0f);
		if (!IsHandleCreated || !Visible || knobPosition == num)
		{
			slideTimer.Stop();
			knobPosition = num;
			return;
		}
		slideFrom = knobPosition;
		slideStarted = Environment.TickCount64;
		slideTimer.Stop();
		slideTimer.Start();
	}

	private void OnSlideTick(object? sender, EventArgs e)
	{
		float num = Math.Min(1f, (float)(Environment.TickCount64 - slideStarted) / 120f);
		float num2 = 1f - (1f - num) * (1f - num);
		float num3 = (Checked ? 1f : 0f);
		knobPosition = slideFrom + (num3 - slideFrom) * num2;
		if (num >= 1f)
		{
			knobPosition = num3;
			slideTimer.Stop();
		}
		Invalidate();
	}
}
