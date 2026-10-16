using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MechrevoLite.UI;

/// <summary>
/// 2026-10-10（用户原话：「CPU调节里面负压的文本框框没有了」+ 上一批「这个文本框能不能恢复默认样式
/// 旁边有个小方块」）：本控件夜间那圈圆角描边（<see cref="RNumericUpDown.OnPaint" /> 末尾）**画在哪儿**。
/// 上一批只有"画整个客户区 / 一点都不画"两种可能（bool <c>SuppressInputOutline</c>），
/// 结果是死结：整客户区 ⇒ 被 <c>UpDownBase.PositionControls()</c> 预留的那条 16 设备像素空条被框成
/// "空的小方格"；不画 ⇒ 连编辑框一起没了边框。本枚举把"画哪儿"独立出来，绕开这个死结。
/// </summary>
internal enum InputOutlineMode
{
	/// <summary>原样：描**整个客户区**（<c>Rectangle(0, 0, Width - 1, Height - 1)</c>）。
	/// ⛔ 默认值 ⇒ 未显式设置过的实例逐像素与改动前完全相同。</summary>
	Default,
	/// <summary>只描**内嵌编辑框占的那块矩形**（由 <c>edit.Parent.PointToScreen(edit.Location)</c> 得到，
	/// 走 WinForms 自带的屏幕↔客户区换算 ⇒ 每个 DPI 都按控件自己的逻辑坐标算，⛔ 不硬编码设备像素）。
	/// 右侧那条 16 设备像素空条**落在描边之外** ⇒ 边框可见、又不会被框成空小方格。</summary>
	EditRectOnly,
	/// <summary>不描边（上一批 <c>SuppressInputOutline = true</c> 的语义）。</summary>
	Off
}

public class RNumericUpDown : NumericUpDown
{
	internal bool NativeSpinButtonsHidden { get; private set; }

	/// <summary>
	/// 2026-10-09（自定义界面：「这个框能不能改成纯净的文字框 这个灰色方块删一下」）：
	/// 是否已切到 <see cref="UsePlainTextBox" /> 的"纯净文字框"形态（只读，供取证/审计读）。
	/// </summary>
	internal bool PlainText { get; private set; }

	/// <summary>
	/// 2026-10-09（用户原话：「这个文本框能不能恢复默认样式 旁边有个小方块说是 自定义界面里面的」）：
	/// 兼容入口，语义等价于 <see cref="OutlineMode" /> 的 <see cref="InputOutlineMode.Off" />。
	/// ⚠️ 上一批只有"开/关"两态，且调用点开的是"关"；本批改成三态后**两页都改用
	/// <see cref="InputOutlineMode.EditRectOnly" />**（原因见那里的注释与 2026-10-10 的修订），
	/// 本 bool 因此没有任何调用点，只为"旧语义可读"保留。
	/// </summary>
	internal bool SuppressInputOutline
	{
		get
		{
			return OutlineMode == InputOutlineMode.Off;
		}
		set
		{
			OutlineMode = (value ? InputOutlineMode.Off : InputOutlineMode.Default);
		}
	}

	/// <summary>
	/// 2026-10-10（用户原话：「CPU调节里面负压的文本框框没有了」）：夜间描边画在哪儿 —— 三态，
	/// 默认 <see cref="InputOutlineMode.Default" />（整客户区，逐像素不变）。
	/// ⛔ 只影响"线画在哪个矩形上"：底色填充、内嵌编辑框宽度、取值 / Minimum / Maximum / Increment /
	/// DecimalPlaces / TextAlign / Enabled / 上下键 / 滚轮 / ± 按钮 / 事件 / 绑定一律未动。
	/// </summary>
	internal InputOutlineMode OutlineMode { get; set; } = InputOutlineMode.Default;

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

	public RNumericUpDown()
	{
		BorderStyle = BorderStyle.None;
	}

	// `NumericUpDown.Controls` 的次序由 `UpDownBase` 的构造函数定死：Controls[0] = 原生上下步进按钮
	// （UpDownButtons）、Controls[1] = 内嵌编辑框（UpDownEdit:TextBox）—— 本机 net10 实测：
	// "[0] UpDownButtons visible=True bounds={X=103,...,Width=16} [1] UpDownEdit visible=True bounds={X=2,...}"。
	public void HideNativeSpinButtons()
	{
		if (!NativeSpinButtonsHidden)
		{
			if (Controls.Count > 0)
			{
				Controls[0].Visible = false;
			}
			NativeSpinButtonsHidden = true;
		}
	}

	/// <summary>
	/// 2026-10-09（数值框：「这个框能不能改成纯净的文字框 这个灰色方块删一下」）：
	/// 把本数值框变成"只有数字、没有框"的输入框。两件事都只发生在本控件内部，⛔ 不牵动任何兄弟控件：
	/// <list type="number">
	///   <item>内嵌编辑框底色跟着**所在行的底色**走（<see cref="OnPaint" /> 与 <see cref="ApplyTheme" />
	///     各对一次）：<c>UpDownBase.PositionControls()</c> **每一次布局**都会给
	///     <see cref="HideNativeSpinButtons" /> 里那个**已经隐藏的**原生步进按钮预留 16 设备像素
	///     （本机实测：52 宽的数字框里编辑框只有 36 宽）；那块空条的底色是父容器的 Surface，编辑框底色
	///     是主题的 Input（夜 20/20/20）—— 两个色差一格，观感就是用户截图里"数值框右边那个灰色方块"。
	///     把编辑框底色拉平到行底色，色差没了，方块也就不见了。
	///     ⛔ **不改编辑框宽度**：右对齐的数字**原地不动**（用户 2026-10-09 的选择 —— 只删方块、不让数字
	///     位移）；空条仍在控件内，只是与行底同色后不可见。</item>
	///   <item>夜间那圈圆角描边**保留**（"可输入"的提示不能丢），只把颜色换成最轻的一档边色
	///     <see cref="UiVisualStyle.Border" />（夜 56/56/56；输入族原来的 BorderStrong 是 98/98/98）——
	///     观感是一条很淡的输入线，不再是硬框。描边本来就**只在夜间画**（见 <see cref="OnPaint" /> 的
	///     <c>IsNightMode</c> 分支）⇒ 日间逐像素不变。</item>
	/// </list>
	/// ⚠️ 只影响**显式调用过本方法**的实例。数值校验 / Minimum / Maximum / Value / Increment /
	/// DecimalPlaces / TextAlign / Enabled / 上下键 / 滚轮 / ± 按钮（UpButton / DownButton）/ ToolTip /
	/// BorderStyle=None 全部未动。
	/// </summary>
	internal void UsePlainTextBox()
	{
		if (PlainText)
		{
			return;
		}
		PlainText = true;
		HideNativeSpinButtons();
		SyncEditBackColor();
		Invalidate();
	}

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：接管 <c>WM_ERASEBKGND</c> / 吞掉 <c>WM_NCPAINT</c>
	/// （见 <see cref="UiVisualStyle.HandleOpaqueChrome" />）—— 本控件自绘底色 + 一圈圆角描边，
	/// 系统的擦除/非客户区重画只会在数字框四周留下淡线或异色块。
	/// 其余消息原样交给 <c>base.WndProc</c>（输入/上下键/滚轮/内嵌编辑子控件一律未动）。
	/// </summary>
	protected override void WndProc(ref Message m)
	{
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		base.WndProc(ref m);
	}

	/// <summary>
	/// 内嵌编辑框底色 = 本控件所在位置真正画出来的行底色（见 <see cref="UsePlainTextBox" />）。
	/// ⚠️ 只改内嵌编辑框的 BackColor（离焦/失焦/禁用文字色、命中区域、**编辑框宽度**一律不动）。
	/// 还没有父容器时（<see cref="UsePlainTextBox" /> 是在加入行之前调用的）直接跳过：那时
	/// <see cref="UiVisualStyle.ContextBackColor" /> 只能回退成 Window，是错色；真正的对色由第一次
	/// <see cref="OnPaint" /> 与 <see cref="ApplyTheme" /> 完成。
	/// </summary>
	private void SyncEditBackColor()
	{
		if (!PlainText || Parent == null)
		{
			return;
		}
		Color color = UiVisualStyle.ContextBackColor(this);
		foreach (Control control in Controls)
		{
			if (control is TextBox && control.BackColor != color)
			{
				control.BackColor = color;
			}
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// 2026-10-09（UI 打磨①：从根上消除"整块异色矩形"）：这里原来填的是自己的 BackColor。
		// 主题里 BackColor 被设成 Input（夜 20/20/20、日 224/232/241），而控件往往落在 Surface 卡片
		// （夜 40/40/40）或 Window 行（夜 28/28/28）上 —— 差几个数值，于是数字框四周露出
		// 一圈"假边/异色块"。现在填 UiVisualStyle.ContextBackColor(this)：沿父链取第一个实色
		// 容器的 BackColor，与该位置实际画出来的底色逐分量相等，整块填充因此不可见。
		// ⚠️ 只改填充色：内嵌编辑框（Controls[1]，Controls[0] 是原生步进按钮）仍然按 UiVisualStyle
		// 给的 Input 自己绘制，输入/上下键/滚轮行为、BorderStyle=None、HideNativeSpinButtons 全未动。
		// 2026-10-09（平文模式）：这一行同时把编辑框底色拉回行底色 —— UiVisualStyle.ApplyTree 与
		// ControlHelper 会分别把它写成 Input / RForm.buttonMain，那块与行底不同色的矩形就是"框"的观感来源。
		SyncEditBackColor();
		using SolidBrush brush = new SolidBrush(UiVisualStyle.ContextBackColor(this));
		e.Graphics.FillRectangle(brush, ClientRectangle);
		base.OnPaint(e);
		// 2026-10-09（UI 打磨②，仅夜间）：数字框的 1px 圆角描边（UiVisualStyle.BorderStrong）。
		// 放在 base.OnPaint 之后、只画一圈线：没有底色对比（本控件底色已于①轮改成父色），
		// 全靠这一圈线把「可输入」表达出来。日间直接跳过 —— 日间像素与改动前完全相同。
		// ⚠️ 不设 Region、不改 Bounds/尺寸/布局/命中区域；输入/上下键/滚轮/HideNativeSpinButtons 全未动。
		// 2026-10-09（平文模式，用户 2026-10-09 决定）：描边**保留**（可输入的提示不能丢），但只对平文实例
		// 换成最轻的一档边色 Border（夜 56/56/56，比 BorderStrong 98/98/98 弱一档，观感=一条很淡的输入线）；
		// 非平文实例仍用 BorderStrong，逐像素不变。
		// ⚠️ 描边本来只在夜间画（下面这层 IsNightMode）⇒ 日间两条路径都不画线，日间逐像素不变。
		// 2026-10-09（用户：「这个文本框能不能恢复默认样式 旁边有个小方块」）：新增可选开关
		// <see cref="SuppressInputOutline" />（默认 false）——自定义界面那 9 个平文数值框打开它，
		// 因为被藏起来的原生上下按钮仍被框架预留 16 设备像素，而这圈线画的是整个客户区，
		// 会把那条同色空条框成"一个空的小方格"。关掉描边后输入框只剩编辑框自己那圈原生 1px 方框
		// （ApplyTree 给 TextBoxBase 的 FixedSingle）⇒ 仍是一只正常输入框，空条不可见。
		// ⛔ 未打开该开关的实例（含 CpuTuningForm 的 4 个平文框）本行逐像素与改动前相同。
		// 2026-10-10（用户原话：「CPU调节里面负压的文本框框没有了」）：上一批把这两页的描边都关掉后，
		// CPU 页那 4 个框**只剩数字**（连编辑框的边都看不见）——"关掉"过头了。本轮改成三态
		// <see cref="OutlineMode" />，两页都用 <see cref="InputOutlineMode.EditRectOnly" />：
		// 只描内嵌编辑框那块矩形，空条落在描边之外 ⇒ 框回来了、空小方格也没有。
		// ⛔ Default 分支（整客户区）逐像素与改动前相同，日间两条路径都不画线。
		if (UiVisualStyle.IsNightMode && OutlineMode != InputOutlineMode.Off)
		{
			Color outline = (PlainText ? UiVisualStyle.Border : UiVisualStyle.BorderStrong);
			// 编辑区矩形在**本控件客户坐标**里的位置：拿内嵌编辑框（Controls[1]，UpDownEdit:TextBox）
			// 自己的 Location 经父控件换算过来 —— 屏幕→客户的换算由 WinForms 按当前 DPI 处理，
			// ⛔ 这里不写任何设备像素常量，100/125/150/175% 都跟着控件自己的矩形走。
			Rectangle bounds;
			if (OutlineMode == InputOutlineMode.EditRectOnly && TryGetEditRect(out bounds))
			{
				UiVisualStyle.DrawInputOutline(e.Graphics, bounds, outline, this);
			}
			else
			{
				UiVisualStyle.DrawInputOutline(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), outline, this);
			}
		}
	}

	/// <summary>
	/// 2026-10-10（用户原话：「但是负压里面没有修」——CPU 页每核那 8 个负压框**仍然看不到边框**）：
	/// <b>本方法是那圈描边"看得见"的唯一保证</b>。上一批把描边从"整客户区"改成
	/// <see cref="InputOutlineMode.EditRectOnly" /> 时漏掉了一条实测事实：<b>被描的那块矩形
	/// 完全在 <c>UpDownEdit</c> 这个子窗口底下</b>。
	/// <para>本机实测（探针实例化构建产物里的 <see cref="RNumericUpDown" />、夜间、Width=78，
	/// 走 <c>DrawToBitmap</c> 数"亮于底色的像素"）：<c>UpDownEdit.Bounds = {X=0,Y=0,W=62,H=19}</c>
	/// —— 和描边矩形<b>逐像素相同</b>；WinForms 的子控件窗口在父控件之后绘制，把父级
	/// <c>OnPaint</c> 画的那圈线<b>整条盖掉</b>，实测只剩 7 个残余像素（几乎全黑）。
	/// 反向对照：把 <c>UpDownEdit.Visible</c> 置 false（子窗口不参与绘制）⇒ 同一块矩形上
	/// 立刻回来 377 个描边像素 ⇒ <b>根因就是"描边被内嵌编辑框这个兄弟子窗口盖住"</b>，
	/// 不是 <c>Enabled</c>/<c>Focused</c>/裁剪/条件判断。</para>
	/// <para>因此改用 <b>内嵌编辑框自己的原生 1px 方框</b>（<c>BorderStyle.FixedSingle</c>）当边框：
	/// 它是 <c>UpDownEdit</c> 自己画的，<b>天然画在任何父级描边之上</b>，不可能被盖住。
	/// 本机实测同一实例渲染结果（亮像素 / 四边是否闭合）：
	/// <list type="bullet">
	/// <item><c>BorderStyle.None</c>（改动前）⇒ 7 px，只有孤零零一列 ⇒ <b>看不见边框</b>（用户截图现象）。</item>
	/// <item><c>BorderStyle.FixedSingle</c>、<b>启用</b> ⇒ 107 px，上 0 行 / 下 18 行 / 左 x=0 / 右 x=61 <b>四边闭合</b>。</item>
	/// <item><c>BorderStyle.FixedSingle</c>、<b>禁用</b> ⇒ 211 px，左右两列整列 + 上下两行整行 <b>四边闭合</b>
	/// （GDI 对禁用的编辑框画的是"平色 1px 方框"，实测<b>照画不误</b> —— 这才是用户那 8 个禁用框真正能看见的东西）。</item>
	/// </list></para>
	/// <para>⛔ 描边本身<b>仍然照画</b>（<see cref="InputOutlineMode.EditRectOnly" /> 不回退）：万一将来
	/// <c>UpDownEdit</c> 的几何不再等于描边矩形，那条圆角线还在。⛔ 不改 <c>UpDownBase</c> 的绘制、
	/// ⛔ 不改 <c>±</c> 列宽、⛔ 不改任何 Bounds/布局/命中区域、⛔ 不碰取值/校验/写入路径。</para>
	/// </summary>
	private void ApplyEditOutlineBorder()
	{
		if (OutlineMode != InputOutlineMode.EditRectOnly)
		{
			return;
		}
		try
		{
			foreach (Control control in Controls)
			{
				if (control is TextBox textBox && textBox.BorderStyle != BorderStyle.FixedSingle)
				{
					// ⚠️ UpDownBase 每次 PositionControls() 都会把内嵌编辑框的边框重写回它自己算的那一份，
					// 所以这一句必须"每次布局后重下一次"（由 OnLayout / HandleCreated 兜底），不是一次性设置。
					textBox.BorderStyle = BorderStyle.FixedSingle;
				}
			}
		}
		catch (ObjectDisposedException)
		{
			// 句柄正在重建/销毁：下一次布局会重下，忽略。
		}
	}

	/// <summary>
	/// 2026-10-10：布局一结束就把内嵌编辑框的 1px 方框重下一次（原因见 <see cref="ApplyEditOutlineBorder" />：
	/// <c>UpDownBase.PositionControls()</c> 会把 <c>UpDownEdit.BorderStyle</c> 覆盖掉，一次性设置不可靠）。
	/// ⚠️ 只额外做这一件事，布局本身一个字没改。
	/// </summary>
	protected override void OnLayout(LayoutEventArgs levent)
	{
		base.OnLayout(levent);
		ApplyEditOutlineBorder();
	}

	/// <summary>
	/// 2026-10-10：句柄建好之后再补一次（调用点设置 <see cref="OutlineMode" /> 时本控件常常还没建句柄，
	/// 那一刻内嵌编辑框也还没被 <c>UpDownBase</c> 造出来 —— 只靠布局时序容易漏掉第一帧）。
	/// </summary>
	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyEditOutlineBorder();
	}

	/// <summary>
	/// 2026-10-10：内嵌编辑框在本控件客户坐标里占的那块矩形（<see cref="InputOutlineMode.EditRectOnly" /> 用）。
	/// 取的是 <c>UpDownBase</c> 自己排出来的编辑框（<c>Controls</c> 里的那个 <see cref="TextBox" />）的
	/// <c>Bounds</c> —— 它是**本控件的直接子控件**，坐标系天然就是本控件的客户坐标
	/// （本机实测：52 宽的数字框里 <c>UpDownEdit.Bounds = {X=2, Y=2, Width=36, Height=16}</c>，
	/// 右边 16 设备像素正是被藏起来的原生步进按钮所占的那条空条; 因此这块矩形天然把空条排除在外），
	/// ⛔ 不需要、也不该再叠一层屏幕↔客户换算（高 DPI 下那会多乘一次缩放）。
	/// ⚠️ 只读几何：不改 Bounds / 不设 Region / 不动命中区域。尺寸不合理（还没布局好）时返回 false，
	/// 由调用方退回整客户区描边。
	/// </summary>
	private bool TryGetEditRect(out Rectangle bounds)
	{
		bounds = Rectangle.Empty;
		foreach (Control control in Controls)
		{
			if (control is not TextBox edit)
			{
				continue;
			}
			Rectangle rectangle = edit.Bounds;
			if (rectangle.Width >= 8 && rectangle.Height >= 8)
			{
				bounds = rectangle;
				return true;
			}
			return false;
		}
		return false;
	}

	public void ApplyTheme(bool dark)
	{
		BackColor = (dark ? RForm.buttonMain : SystemColors.Window);
		ForeColor = (dark ? RForm.foreMain : SystemColors.WindowText);
		string pszSubAppName = (dark ? "DarkMode_Explorer" : "Explorer");
		SetWindowTheme(Handle, pszSubAppName, null);
		foreach (Control control in Controls)
		{
			SetWindowTheme(control.Handle, pszSubAppName, null);
		}
		// 2026-10-09（平文模式）：上面那行 BackColor 会把内嵌编辑框刷成 buttonMain（UpDownBase 会转发），
		// 主题重下发后要对回"所在行的底色"，否则平文模式会重新露出一块异色矩形。
		SyncEditBackColor();
	}
}
