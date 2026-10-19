using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace MechrevoLite.UI;

public class RSlider : Control
{
	private const int TrackThickness = 4;

	private const int ThumbDiameter = 14;

	private const int ThumbRing = 3;

	private const int FocusRadius = 6;

	internal const int MinTrackLogicalWidth = 104;

	private static readonly Color ThumbFill = Color.FromArgb(233, 241, 252);

	private int minimum;

	private int maximum = 100;

	private int currentValue;

	private int smallChange = 1;

	private int largeChange = 10;

	private bool dragging;

	// 2026-10-09（自定义性能模式 GPU 偏移「细拖」）：DragStep > 0 时改为「相对拖动 + 整数刻度」，
	// 供「一拖别跳太多」的两条 GPU 偏移滑块使用；默认 DragStep = 0 ⇒ 本类行为与改动前逐像素一致。
	private bool fineDragging;

	private int dragAnchorX;

	private int dragAnchorValue;

	public int Minimum
	{
		get
		{
			return minimum;
		}
		set
		{
			if (minimum != value)
			{
				minimum = value;
				if (maximum < minimum)
				{
					maximum = minimum;
				}
				ApplyValue(currentValue);
				Invalidate();
			}
		}
	}

	public int Maximum
	{
		get
		{
			return maximum;
		}
		set
		{
			if (maximum != value)
			{
				maximum = value;
				if (minimum > maximum)
				{
					minimum = maximum;
				}
				ApplyValue(currentValue);
				Invalidate();
			}
		}
	}

	public int Value
	{
		get
		{
			return currentValue;
		}
		set
		{
			ApplyValue(value);
		}
	}

	public int SmallChange
	{
		get
		{
			return smallChange;
		}
		set
		{
			smallChange = Math.Max(0, value);
		}
	}

	public int LargeChange
	{
		get
		{
			return largeChange;
		}
		set
		{
			largeChange = Math.Max(0, value);
		}
	}

	/// <summary>
	/// 2026-10-09（GPU 偏移风险告警）：可选的强调色覆盖。为 null（默认）时整条滑轨/滑钮/焦点圈
	/// 仍取 <c>UiVisualStyle.Accent</c>，即**未设置过本属性的滑块（其余全部滑块）改动前后逐像素相同**；
	/// 只有自定义性能模式里的「核心/显存偏移」在超过风险阈值时把本属性设成 <c>UiVisualStyle.Danger</c>。
	/// ⚠️ 只影响绘制用色，不改 Minimum/Maximum/Value/命中算法。
	/// </summary>
	internal Color? AccentOverride { get; set; }

	/// <summary>
	/// 2026-10-09（GPU 偏移「细拖」）：拖动时每 <see cref="DragStepPixelsLogical" /> 个逻辑像素
	/// 变化多少**单位**。0（默认）= 关闭 ⇒ 沿用旧的「绝对定位」拖法（按下即跳到光标处、
	/// 整条量程铺满滑轨），其余滑块全部走这条老路径、行为不变。
	/// </summary>
	internal int DragStep { get; set; }

	/// <summary>2026-10-09（GPU 偏移「细拖」）：一个刻度对应的**逻辑像素**数（DPI 缩放后取整），默认 2。</summary>
	internal int DragStepPixelsLogical { get; set; } = 2;

	public event EventHandler? ValueChanged;

	public RSlider()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		TabStop = true;
		AccessibleRole = AccessibleRole.Slider;
		Height = 28;
		MinimumSize = new Size(104, 20);
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）—— 本控件整块填"父容器真色"后只画中间那条滑轨，
	/// 系统的擦除/NC 重画只会在滑块四周留下淡边。
	/// 其余消息原样交给 <c>base.WndProc</c>（键盘/焦点/拖拽/滚轮的命中算法只依赖 Width/Height，未动）。
	/// </summary>
	protected override void WndProc(ref Message m)
	{
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		base.WndProc(ref m);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys keys = keyData & Keys.KeyCode;
		if ((uint)(keys - 33) <= 7u)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		Invalidate();
		Announce($"{AccessibleName}, slider, {Value}");
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		Invalidate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		switch (e.KeyCode)
		{
		default:
			return;
		case Keys.Left:
		case Keys.Down:
			ApplyValue(currentValue - smallChange);
			break;
		case Keys.Up:
		case Keys.Right:
			ApplyValue(currentValue + smallChange);
			break;
		case Keys.Prior:
			ApplyValue(currentValue + largeChange);
			break;
		case Keys.Next:
			ApplyValue(currentValue - largeChange);
			break;
		case Keys.Home:
			ApplyValue(minimum);
			break;
		case Keys.End:
			ApplyValue(maximum);
			break;
		}
		e.Handled = true;
		e.SuppressKeyPress = true;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			Focus();
			dragging = true;
			Capture = true;
			// 2026-10-09（细拖）：DragStep > 0 时按「按下时的值 = 锚点」，**不再**跳到光标处；
			// 之后每一格位移只增减一个刻度 ⇒ 同一次拖动造成的数值变化远小于旧的绝对定位。
			fineDragging = DragStep > 0;
			if (fineDragging)
			{
				dragAnchorX = e.X;
				dragAnchorValue = currentValue;
			}
			else
			{
				ApplyValueAt(e.X);
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (dragging)
		{
			if (fineDragging)
			{
				ApplyValueByDrag(e.X);
			}
			else
			{
				ApplyValueAt(e.X);
			}
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left && dragging)
		{
			if (fineDragging)
			{
				ApplyValueByDrag(e.X);
			}
			else
			{
				ApplyValueAt(e.X);
			}
			dragging = false;
			fineDragging = false;
			Capture = false;
		}
	}

	protected override void OnMouseCaptureChanged(EventArgs e)
	{
		base.OnMouseCaptureChanged(e);
		if (!Capture)
		{
			dragging = false;
			fineDragging = false;
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (e.Delta != 0)
		{
			ApplyValue(currentValue + ((e.Delta > 0) ? smallChange : (-smallChange)));
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		// 2026-10-09（UI 打磨①：从根上消除"整块异色矩形"）：这里原来填的是 BackColor。
		// BackColor 是"控件自己的底色"，不是"控件所在位置实际被画出来的底色"；两者一旦差几个数值，
		// 整个控件矩形（滑块条其实只占中间 4px 高，四周大片是空的）就会在父容器上显出一块异色矩形，
		// 观感就是滑块周围/上方那条淡淡的"边"。
		// 现在改为填 UiVisualStyle.ContextBackColor(this) —— 沿父链取第一个实色容器的 BackColor，
		// 与所在位置的实际底色逐分量相等，整块填充因此完全不可见。
		// ⚠️ 只是"填充色"变了：滑轨（Track/Accent 圆角条）、滑钮（两个同心圆）的形状与配色、
		// 以及 ApplyValueAt(e.X) 的命中算法（只依赖 Width/Height，与填充无关）全部未动。
		using (SolidBrush brush = new SolidBrush(UiVisualStyle.ContextBackColor(this)))
		{
			graphics.FillRectangle(brush, ClientRectangle);
		}
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读绘制 DC 的 Graphics.DpiX。
		float num = UiDpi.Paint(this) / 96f;
		int num2 = Math.Max(2, (int)Math.Round(4f * num));
		int num3 = Math.Max(6, (int)Math.Round(14f * num));
		int num4 = Math.Max(1, (int)Math.Round(3f * num));
		float num5 = (float)num3 / 2f;
		float num6 = (float)Width - (float)num3 / 2f;
		if (num6 <= num5)
		{
			num6 = num5 + 1f;
		}
		float num7 = ((maximum > minimum) ? ((float)(currentValue - minimum) / (float)(maximum - minimum)) : 0f);
		float num8 = num5 + num7 * (num6 - num5);
		// 2026-10-09（GPU 偏移风险告警）：未设置 AccentOverride 时取 Accent，与改动前逐像素相同；
		// 设成 Danger 的那两条滑块（核心/显存偏移超阈值）滑轨与滑钮变红，仅此一处用色不同。
		Color color = (Enabled ? (AccentOverride ?? UiVisualStyle.Accent) : UiVisualStyle.Muted);
		int num9 = num2 / 2;
		int y = (Height - num2) / 2;
		int x = (int)Math.Round(num5 - (float)num9);
		int width = (int)Math.Round(num6 - num5) + num2;
		using (GraphicsPath path = RComboBox.RoundedRect(new Rectangle(x, y, width, num2), num9, num9))
		{
			using SolidBrush brush2 = new SolidBrush(UiVisualStyle.Track);
			graphics.FillPath(brush2, path);
		}
		int num10 = (int)Math.Round(num8 - num5) + num9;
		if (num10 >= num2)
		{
			using GraphicsPath path2 = RComboBox.RoundedRect(new Rectangle(x, y, num10, num2), num9, num9);
			using SolidBrush brush3 = new SolidBrush(color);
			graphics.FillPath(brush3, path2);
		}
		float num11 = (float)(Height - num3) / 2f;
		using (SolidBrush brush4 = new SolidBrush(color))
		{
			graphics.FillEllipse(brush4, num8 - (float)num3 / 2f, num11, num3, num3);
		}
		int num12 = num3 - 2 * num4;
		if (num12 > 0)
		{
			using SolidBrush brush5 = new SolidBrush(Enabled ? ThumbFill : UiVisualStyle.Surface);
			graphics.FillEllipse(brush5, num8 - (float)num12 / 2f, num11 + (float)num4, num12, num12);
		}
		if (!Focused)
		{
			return;
		}
		int num13 = Math.Max(1, Math.Min((int)Math.Round(6f * num), (Height - 1) / 2));
		using GraphicsPath path3 = RComboBox.RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), num13, num13);
		// 2026-10-09（P0.5）：整类"1 逻辑像素描边"统一走工厂（100%→1、150%→2、200%→2 设备像素）。
		using Pen pen = UiVisualStyle.OutlinePen(this, AccentOverride ?? UiVisualStyle.Accent, 1f, 1f);
		graphics.DrawPath(pen, path3);
	}

	/// <summary>
	/// 2026-10-09（GPU 偏移「细拖」）：DragStep &gt; 0 时的取值 —— 相对按下点（锚点）的**格数 × 刻度**，
	/// 最后仍走 <see cref="ApplyValue" /> 做 [Minimum, Maximum] 夹取并触发 ValueChanged。
	/// 整数除法向零取整 ⇒ 左拖与右拖对称；锚点在整个拖动过程中不变 ⇒ 拖回原处即回到原值。
	/// </summary>
	private void ApplyValueByDrag(int x)
	{
		int stepPixels = Math.Max(1, (int)Math.Round((float)DragStepPixelsLogical * UiDpi.Paint(this) / 96f));
		int steps = (x - dragAnchorX) / stepPixels;
		ApplyValue(dragAnchorValue + steps * DragStep);
	}

	private void ApplyValue(int raw)
	{
		int num = Math.Clamp(raw, minimum, maximum);
		if (currentValue != num)
		{
			currentValue = num;
			Invalidate();
			try
			{
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
			catch (ArgumentOutOfRangeException ex)
			{
				// （crash：数值框上限小于滑块值时反复抛）： 不让 UI 线程异常刷屏/崩溃
				global::Logger.WriteError("[rslider] ⛔ 值超范围（value=" + num + " · range=" + minimum + "~" + maximum + "）：" + ex.Message);
			}
			if (Focused)
			{
				Announce(num.ToString());
			}
		}
	}

	private void ApplyValueAt(int x)
	{
		float num = UiDpi.Paint(this) / 96f;
		float num2 = Math.Max(1f, 14f * num / 2f);
		float num3 = (float)Width - num2;
		float num4 = ((num3 > num2) ? Math.Clamp(((float)x - num2) / (num3 - num2), 0f, 1f) : 0f);
		ApplyValue(minimum + (int)Math.Round(num4 * (float)(maximum - minimum)));
	}

	private void Announce(string text)
	{
		try
		{
			AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Slider accessibility announcement failed: " + ex.GetType().Name + " " + ex.Message);
		}
	}
}
