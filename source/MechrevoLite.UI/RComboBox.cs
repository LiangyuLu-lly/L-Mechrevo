using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MechrevoLite.UI;

/// <summary>
/// 2026-10-09（UI 打磨⑥ 起，0.291 beta22 收口；用户原话「学习一下它的下拉框样式我的明显糙不少」
/// → 本轮「下拉框好像没改过来 还是旧版的」）：LM 的下拉框**统一**到这一个控件上
/// （原生 <see cref="ComboBox" /> 实例已全部改成本类，含 0.291 接管的 CalibrationStartupForm 那一处），
/// 夜间与日间共用同一套几何、只换令牌颜色：
///   底   = 整块扁平的圆角实心（= UiVisualStyle.Input，比所在卡片更深 = 凹陷级）；
///   箭头格 = 与输入区**同底**，仅一条极淡竖分隔线区分（悬停/展开才轻微变一档）；
///   边   = 1px 细边（BorderStrong），**不再用 DrawGradientBorder 的渐变边**；
///   箭头 = 细线 chevron（两段 AA 线，与正文同色系 ArrowColor），**不再画实心三角块**；
///   下拉列表 = WM_CTLCOLORLISTBOX 交主题刷子 + OnDrawItem 整行铺满（夜间不再白底、日间不再系统色）。
///
/// 为什么整块自绘：原生 ComboBox 的下拉按钮（箭头那一格）由 CBT 的主题引擎绘制，BackColor/
/// FlatStyle 都管不到它 —— 这就是"黑底 + 白色方块 + 黑三角"的来源。本类**先 base.WndProc 铺底、
/// 再把整个客户区重画一遍**（箭头格 + chevron + 边框全由我们自己画），于是系统画的那一格被完全覆盖。
///
/// ⚠️ 只改外观：不改 Items / SelectedIndex / 事件 / 下拉宽度 / 尺寸 / 命中区域。
/// </summary>
public class RComboBox : ComboBox
{
	public struct RECT
	{
		public int L;

		public int T;

		public int R;

		public int B;
	}

	public struct PAINTSTRUCT
	{
		public nint hdc;

		public bool fErase;

		public int rcPaint_left;

		public int rcPaint_top;

		public int rcPaint_right;

		public int rcPaint_bottom;

		public bool fRestore;

		public bool fIncUpdate;

		public int reserved1;

		public int reserved2;

		public int reserved3;

		public int reserved4;

		public int reserved5;

		public int reserved6;

		public int reserved7;

		public int reserved8;
	}

	private const int TextLeftPad = 8;

	public bool UseCustomTextPadding = true;

	private Color borderColor = Color.Gray;

	private Color buttonColor = Color.FromArgb(255, 255, 255, 255);

	private Color arrowColor = Color.Black;

	private const int WM_PAINT = 15;

	private const int WM_CTLCOLORLISTBOX = 308;

	private const int WM_CTLCOLOREDIT = 307;

	private const int WM_CTLCOLORSTATIC = 312;

	/// <summary>滚轮消息（<see cref="ForwardWheelToOpenList" /> 用）。</summary>
	private const int WM_MOUSEWHEEL = 522;

	/// <summary>获得/失去焦点（<see cref="RepaintAfterNativeFieldPaint" /> 的触发消息之一）。</summary>
	private const int WM_SETFOCUS = 7;

	/// <summary>失去焦点（同上）。</summary>
	private const int WM_KILLFOCUS = 8;

	/// <summary>UI 状态变化请求（同上；窗口激活/停用时 `DefWindowProc` 会向上抛给顶层窗体）。</summary>
	private const int WM_CHANGEUISTATE = 295;

	/// <summary>
	/// UI 状态变化下发（同上；顶层窗体收到 <see cref="WM_CHANGEUISTATE" /> 后会**逐子窗口下发**本消息，
	/// 各控件据此重画自己的焦点提示 —— 这就是"设置窗口被激活/选中"时那颗下拉被原生重铺的入口）。
	/// </summary>
	private const int WM_UPDATEUISTATE = 296;

	/// <summary>原生"把当前选择设为第 N 项"（<c>ComboBox.SelectedIndex</c> setter 走的就是它）。</summary>
	private const int CB_SETCURSEL = 334;

	/// <summary>原生"清空列表"（<c>Items.Clear()</c> 走的就是它）。</summary>
	private const int CB_RESETCONTENT = 331;

	/// <summary><c>SetWindowPos</c> 标志：只挪位置（<see cref="SyncDropDownList" /> 用）。</summary>
	private const uint SwpNoSize = 1u;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoActivate = 16u;

	private const uint SwpNoOwnerZOrder = 512u;

	/// <summary>
	/// 2026-10-09（用户原话「依旧没跟着走」/「直接漂移到左上角了」）：展开列表的**显式跟随**状态。
	/// ⚠️ `_dropFollowOffset`（列表屏幕坐标 − 下拉框屏幕坐标）**只在确认列表已被系统摆到"紧贴下拉框"的
	/// 正常位置之后**才记录 —— 见 <see cref="SyncDropDownList" /> 里"采样窗口"的说明：
	/// `OnDropDown` 是"即将展开"的通知，那一刻系统还没给列表定位，采样到的会是垃圾（实测贴近屏幕原点）。
	/// </summary>
	private System.Windows.Forms.Timer? _dropFollowTimer;

	private Point _dropFollowOffset;

	private bool _dropFollowOffsetKnown;

	/// <summary>展开期间宿主（祖先容器/本控件）是否已经发生过移动 —— 用于"偏移还没采样到就先动了"时直接收起。</summary>
	private bool _dropFollowHostMoved;

	private int _dropFollowAttempts;

	private bool _dropFollowHooked;

	/// <summary>
	/// 2026-10-09（UI 打磨⑥）：项目 50 系分支等用的是「下拉列表底色」——这里固定取 Input
	/// （夜间 13/13/13 = 最暗的凹陷级），不跟随 BackColor（BackColor 承载的是**闭合态**的底色）。
	/// 由 WM_CTLCOLORLISTBOX / WM_CTLCOLOREDIT 返回给系统，是"下拉列表不再白底"的兜底。
	/// </summary>
	private nint _listBrush;

	private Color _listBrushColor = Color.Empty;

	/// <summary>
	/// 2026-10-09（0.291 beta22）：箭头格宽度的**唯一**来源 —— 「约 24~28」取中值 26 逻辑像素，
	/// 按 DPI 缩放后取整（见 <see cref="ArrowCellRectangle" />，且不超过客户区一半）。
	/// 改前这里是"夜间用 26、日间用系统滚动条箭头宽"两条路，本轮合并成一条。
	/// 2026-10-10（配置号下拉裁字）：值本身成为**默认值**，逐控件可覆盖（见
	/// <see cref="ArrowCellLogicalPx" /> / <see cref="TextPadLogical" />）—— 只有窄到必须省那几像素的
	/// 「模式×配置」三颗下拉才覆盖，其余下拉逐像素不变。
	/// </summary>
	private const int ArrowCellLogicalDefault = 26;

	/// <summary>2026-10-09（0.291 beta22）：闭合态文本左边距的默认值（逻辑像素，见 <see cref="TextPadLogical" />）。</summary>
	private const int TextPadLogicalDefault = 8;

	/// <summary>
	/// 2026-10-10（用户原话「暴露一个问题 配置号被裁切了」）：**箭头格宽度覆盖值**（逻辑像素，DPI 感知）。
	/// 默认 = <see cref="ArrowCellLogicalDefault" />（26），即不设时与既有完全一致；
	/// 只给「模式 × 配置」那一行三颗一等分格里的窄下拉调小（26 → 24），腾出的宽度让全称「配置 N」放得下。
	/// ⚠️ 只影响闭合态自绘的箭头格与文本矩形，不改高度/字号/项/事件/下发。
	/// </summary>
	public int ArrowCellLogicalPx = ArrowCellLogicalDefault;

	/// <summary>
	/// 2026-10-10（同上）：**闭合态文本左右内边距覆盖值**（逻辑像素，DPI 感知）。
	/// 默认 = <see cref="TextPadLogicalDefault" />（8），不设时与既有完全一致；
	/// 同上只给那三颗窄下拉调小（8 → 5）。⚠️ 展开列表的行内边距（<c>OnDrawItem</c>）**不动**。
	/// </summary>
	public int TextPadLogical = TextPadLogicalDefault;

	/// <summary>2026-10-09（UI 打磨⑦；0.291 起日间同样启用）：鼠标是否停在箭头格上（只驱动换填充色 + 重绘，不改任何行为）。</summary>
	private bool _arrowHot;

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑥；用户原话「我觉得Ghelper的那个控件其实很舒服」）：
	/// **展开列表改回系统原生 LISTBOX**（G-Helper 的做法），不再使用我们自己的弹层
	/// <c>RComboPopup</c>（该文件已删除，`WndProc` 里"吞掉鼠标/F4/Alt+↓"的死路径也一并移除）。
	/// 原生列表由系统负责滚轮/键盘/滚动条/贴屏边/点击外收起 —— 这正是用户觉得"舒服"的部分；
	/// 主题适配仍按 G-Helper 那套：`SetWindowTheme(handle,"DarkMode_CFD"/"CFD")`（见
	/// <see cref="UiVisualStyle.StyleComboBox" /> 的 `ApplyDarkComboTheme`）
	/// ＋ 本类 `WM_CTLCOLORLISTBOX` 刷底（`Input`）＋ `OnDrawItem` 整行铺满（选中行 `ButtonFaceSelected`）。
	///
	/// 2026-10-09（P0 收尾「删掉临时取证仪表」）：原有三套临时仪表**已全部删除** ——
	/// ① 逐帧证据行（`_paintSeqLogged`/`_paintSeqTotal`，`[diag] paint#N`）；
	/// ② 一次性像素回读 + run-length 扫描（`_paintDiagLogged`/`_paintDiagCount`、`RgbOf`、`ScanRuns`、
	///   `GetPixel`/`GetDC`/`ReleaseDC` 三个 P/Invoke）；
	/// ③ NC 区一次性证据行（`_ncDiagLogged`/`_ncDiagCount`，`[diag] WM_NCPAINT`）。
	/// **刻意保留的**只有下面这一个 `_buildDiagLogged`（见 <see cref="PaintFlat" />）：它是"安装态到底是不是
	/// 这一版构建"的**唯一运行时凭据**（用户明确要求保留构建标识），每进程只写一行、稳态零开销。
	/// </summary>
	private static bool _buildDiagLogged;

	/// <summary>
	/// 2026-10-09（补丁⑬）：一次性的"套样式时刻"日志开关 —— 证明**统一样式是在第一次绘制之前**套上的
	/// （见 <see cref="OnHandleCreated" />）。与已清理掉的逐帧仪表无关，同进程只写一行。
	/// </summary>
	private static bool _styleDiagLogged;

	public bool NativeHeight { get; set; }

	[DefaultValue(typeof(Color), "Gray")]
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
				borderColor = value;
				Invalidate();
			}
		}
	}

	[DefaultValue(typeof(Color), "255, 255, 255")]
	public Color ButtonColor
	{
		get
		{
			return buttonColor;
		}
		set
		{
			if (buttonColor != value)
			{
				buttonColor = value;
				Invalidate();
			}
		}
	}

	[DefaultValue(typeof(Color), "Black")]
	public Color ArrowColor
	{
		get
		{
			return arrowColor;
		}
		set
		{
			if (arrowColor != value)
			{
				arrowColor = value;
				Invalidate();
			}
		}
	}

	public RComboBox()
	{
		// 2026-10-09（0.291 beta22 补丁；用户原话「下拉依旧是旧版直角」）：**在句柄创建之前**就把
		// FlatStyle 定成 Flat。根因：此前只有 UiVisualStyle.StyleComboBox 会设它，而
		// ①「构建晚于 ApplyWindow 的控件」（灯效那几行、运行时动态创建的下拉）与
		// ②「从不走 ApplyWindow 的次级窗体」（SettingsDialog 的「语言」「日志级别」、RgbForm 后建的几颗、
		//   CalibrationStartupForm 等）
		// 里的下拉只能等首绘时的自愈（UiVisualStyle.StyleComboBoxIfStale）——而那次自愈发生在
		// WM_PAINT **内部**：改 FlatStyle 会让 ComboBox **重建 HWND** ⇒ 本帧 BeginPaint 拿到的裁剪区
		// 随即失效 ⇒ 我们画的圆角整块被裁掉，屏幕上只剩系统原生绘制（**方角 + 浅色渐变箭头格**，
		// 即用户看到的"旧版下拉"）。构造函数里先定好 ⇒ 自愈时 FlatStyle 已是同值（WinForms 里
		// `if (_flatStyle != value)` 短路）⇒ 不再有任何句柄重建，圆角自绘完整生效。
		// ⚠️ 只定"画法开关"，颜色仍由 UiVisualStyle.StyleComboBox 统一下发（BackColor/BorderColor/ArrowColor）。
		FlatStyle = FlatStyle.Flat;
		DrawMode = (UseCustomTextPadding ? DrawMode.OwnerDrawFixed : DrawMode.Normal);
		Cursor = Cursors.Hand;
	}

	/// <summary>2026-10-09（UI 打磨⑥）：chevron 的外接尺寸（逻辑像素），底盘圆的中心放它。</summary>
	private const int ChevronHalfWidth = 4;

	private const int ChevronHalfHeight = 2;

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		HandledMouseEventArgs e2 = (HandledMouseEventArgs)e;
		if (!DroppedDown)
		{
			e2.Handled = true;
		}
		else
		{
			// 2026-10-09（用户原话「依旧没跟着走」；⭐ 这是"页面还能被滚走"的**第二条**路径）：
			// 展开态下原实现走 `base.OnMouseWheel(e)`（Handled 保持 false）⇒ 消息交回 `DefWndProc` ⇒
			// 原生组合框先把滚轮转给它的列表；**列表已经滚不动时**（例如键盘灯效那种 4 项全部可见）
			// 这条消息就继续沿父链冒泡 ⇒ **页面被滚走**，而展开列表是桌面级弹窗（ComboLBox，
			// `GetParent`=桌面、`GW_OWNER`=NULL，实测，见 `AnyDropDownListCovers` 注释）留在原地
			// ⇒ 用户截图那一幕。现在：一律**自己把滚轮转投给原生列表**（= 与系统同一次滚动，实测 3 项/格），
			// 并把消息标记为已处理 ⇒ 展开态下滚轮**永远不会**冒泡到页面；列表能滚就滚、滚到头就什么都不做
			//（与原生语义一致），页面背景上的滚轮不受影响（那本来就不归本控件管）。
			ForwardWheelToOpenList(e);
			e2.Handled = true;
		}
	}

	/// <summary>
	/// 2026-10-09（用户原话「依旧没跟着走」）：把落在**下拉框本体**上的滚轮转投给它自己展开的**原生列表**，
	/// 让"展开态下的滚轮"与系统原生完全同义（列表滚 3 项/格、滚到头不动），同时由调用方
	/// （<see cref="OnMouseWheel" />）把消息标记为已处理 ⇒ 不会再冒泡去滚页面。
	/// 拿不到列表句柄就什么都不做（调用方仍然吞掉消息：宁可不动，也不能把页面滚走）。
	/// </summary>
	private void ForwardWheelToOpenList(MouseEventArgs e)
	{
		if (!TryGetDropDownListHandle(out nint list) || list == IntPtr.Zero)
		{
			return;
		}
		int keys = 0;
		if ((ModifierKeys & Keys.Shift) != 0)
		{
			keys |= 0x10000; // MK_SHIFT
		}
		if ((ModifierKeys & Keys.Control) != 0)
		{
			keys |= 0x80000; // MK_CONTROL
		}
		int wparam = keys | (e.Delta << 16);
		Point screen = PointToScreen(new Point(e.X, e.Y));
		nint lparam = (nint)((screen.Y << 16) | (screen.X & 0xFFFF));
		SendMessage(list, WM_MOUSEWHEEL, (nint)wparam, lparam);
	}

	/// <summary>展开中时取原生列表窗口句柄（= 系统的 <c>hwndList</c>）。</summary>
	private bool TryGetDropDownListHandle(out nint list)
	{
		list = IntPtr.Zero;
		if (!IsHandleCreated)
		{
			return false;
		}
		COMBOBOXINFO info = default;
		info.cbSize = Marshal.SizeOf<COMBOBOXINFO>();
		if (!GetComboBoxInfo(Handle, ref info) || info.hwndList == IntPtr.Zero)
		{
			return false;
		}
		list = info.hwndList;
		return true;
	}

	/// <summary>
	/// 2026-10-09（用户原话「如果打开下拉列表 鼠标滚轮上下滑动 列表会错位」）：⭐ **"这个滚轮是不是落在一个
	/// 展开中的下拉列表上"的唯一判据** —— 供页面级的滚轮消息过滤器使用（见下方
	/// <see cref="AnyDropDownListCovers" /> 与 `SettingsForm.DashboardWheelFilter`）。
	///
	/// 为什么必须问这个问题：展开后的下拉列表是**系统原生的桌面级弹窗**（窗口类 `ComboLBox`，父窗口是桌面，
	/// 既不在页面面板的子控件树里、`Control.GetChildAtPoint` 也命中不到它）。主设置页的滚轮过滤器按
	/// "鼠标点落在哪个子控件上"决定要不要替页面消化滚轮 ⇒ 鼠标移到刚弹出的列表上时它命中不到任何下拉框，
	/// 于是**把滚轮吞掉去滚页面**：页面带着下拉框本体从弹出列表底下滑走，而弹出列表钉在原地不动 ⇒
	/// 实测（175%）滚一格页面（RScrollBar.Step=70 设备像素）列表就相对下拉框错位 70 像素，且列表自己
	/// 一格都没滚。本方法返回 true 时，过滤器应当放过这个滚轮 ⇒ 由系统按原生语义把它交给列表（列表项
	/// 没超出可见行时系统本来也不滚，符合直觉）。
	/// ⚠️ 只读判定：不移动任何窗口、不碰 Items/索引/尺寸。
	/// </summary>
	internal static bool AnyDropDownListCovers(Control root, Point screenPoint)
	{
		if (root == null)
		{
			return false;
		}
		if (root is RComboBox combo && combo.IsScreenPointOverOpenList(screenPoint))
		{
			return true;
		}
		foreach (Control child in root.Controls)
		{
			if (AnyDropDownListCovers(child, screenPoint))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>本下拉展开时，那个原生列表窗口的**屏幕矩形**是否包含给定屏幕点（见 <see cref="AnyDropDownListCovers" />）。</summary>
	private bool IsScreenPointOverOpenList(Point screenPoint)
	{
		if (!DroppedDown || !TryGetDropDownListHandle(out nint list))
		{
			return false;
		}
		if (!GetWindowRect(list, out RECT rect))
		{
			return false;
		}
		return new Rectangle(rect.L, rect.T, Math.Max(0, rect.R - rect.L), Math.Max(0, rect.B - rect.T)).Contains(screenPoint);
	}

	/// <summary>2026-10-09（修列表滚轮错位）：原生下拉列表窗口的句柄/矩形查询（<c>ComboBox</c> 的 <c>hwndList</c> 不对外暴露）。</summary>
	private struct COMBOBOXINFO
	{
		public int cbSize;

		public RECT rcItem;

		public RECT rcButton;

		public int stateButton;

		public nint hwndCombo;

		public nint hwndItem;

		public nint hwndList;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		CalibrateItemHeight();
		// 2026-10-09（0.291 beta22 补丁⑬；用户原话「依旧 第一张图是状态为刷新 第二张是刷新的」——
		// 即"屏幕校色那颗**刷新前**是细边、**刷新后**才变粗"，与前一轮"打开对、刷新刷掉"正好相反）：
		// ⭐ **句柄一建好就把统一样式套上**。原因：像「屏幕校色」这种**启动后才填 Items**（`RebuildCalibrationItems`）
		// 的下拉，构造/`ApplyWindow` 时可能还没轮到 `ApplyTree`；如果等到它**第一次绘制**才靠 `PaintFlat` 里的
		// 自愈（`StyleComboBoxIfStale`）补样式，那一帧会被"刚改过样式 ⇒ 本帧放弃绘制"的分支吃掉
		//（`Invalidate(); return;`）⇒ **首绘什么都没画**（屏幕上留着旧像素＝用户看到的"细边"），
		// 第二帧才走完整路径 ⇒ 变粗 ⇒ "刷新前细、刷新后粗"。
		// `OnHandleCreated` 在**第一次绘制之前**必然发生（Win32 语义），在这里套样式 ⇒ **首绘就已经是最终外观**，
		// 自愈分支在稳态下永远不触发。⚠️ 只改外观属性（与 `StyleComboBox` 同一入口），不碰 Items/事件/尺寸/命中区域。
		UiVisualStyle.StyleComboBox(this);
		if (!_styleDiagLogged)
		{
			_styleDiagLogged = true;
			Logger.WriteLine($"[diag] RComboBox {Name}#{GetHashCode()} styled@OnHandleCreated → 首绘之前已套样式（同进程仅一条）");
		}
	}

	protected override void OnDpiChangedAfterParent(EventArgs e)
	{
		base.OnDpiChangedAfterParent(e);
		CalibrateItemHeight();
	}

	protected override void OnFontChanged(EventArgs e)
	{
		base.OnFontChanged(e);
		CalibrateItemHeight();
	}

	/// <summary>
	/// 2026-10-09（用户原话「如果打开下拉列表 鼠标滚轮上下滑动 列表会错位」）：⭐ <b>行高的"边框开销"必须
	/// 实测一次就记住，不能每次拿 `PreferredHeight - ItemHeight` 现算</b>。
	///
	/// 实测（175% DPI / DeviceDpi=168，本机 9pt Microsoft YaHei UI，探针数值）：
	/// `ComboBox.PreferredHeight` 在 WinForms 里是**句柄创建时算好并缓存**的值 —— 把 `ItemHeight` 依次设成
	/// 20/26/30/32/40/46，`PreferredHeight` **恒为 36**（不是 `ItemHeight + 常数`）。于是原实现里
	/// `num = PreferredHeight - ItemHeight` 不是"边框开销"，而是**跟着上一次行高一起变的量**：
	///   `ItemHeight(新) = num3 - (36 - ItemHeight(旧)) = ItemHeight(旧) + (num3 - 36)`
	/// ⇒ 本方法**每被调用一次行高就 +2 设备像素**（`OnHandleCreated`/`OnFontChanged`/`OnDpiChangedAfterParent`
	/// 都会调它）。实测（175%）：31→33→35→37→39（连续 5 次）、32→34→36→38→40→42→44（另一轮连续 6 次），
	/// 无上限；96 DPI 下按同一算式是每调用一次 +1（未实测）。
	///
	/// 这正是"两套行高"的来源：原生下拉列表框的**内部行高恒等于 `ItemHeight`**（探针实测 `LB_GETITEMHEIGHT`
	/// 始终跟随 `CB_SETITEMHEIGHT`，行矩形也始终落在 `ItemHeight` 的整行网格上），而"列表窗口的几何/滚动
	/// 网格/已画好的行"是按**上一次**的 `ItemHeight` 排布的 ⇒ 用滚轮一滚（滚动＝按网格重画/搬运），
	/// 两套值就以 (Δ 行高) 的整数倍错位。
	/// 现在：开销只在**句柄建好后的第一次**实测一次，行高变成**幂等**的确定值 ⇒ 只有一套行高。
	/// -1 ＝ 尚未实测。
	/// </summary>
	private int _borderOverheadDevice = -1;

	private void CalibrateItemHeight()
	{
		if (DrawMode == DrawMode.OwnerDrawFixed)
		{
			// 2026-10-09（修列表滚轮错位）：⭐ 开销**实测一次**（`PreferredHeight` 是缓存值，见
			// `_borderOverheadDevice` 的说明；句柄没建好时 PreferredHeight 还不可信，故只在句柄已建好时量）。
			if (_borderOverheadDevice < 0)
			{
				if (!IsHandleCreated)
				{
					ApplyFamilyHeight();
					return;
				}
				_borderOverheadDevice = Math.Max(0, PreferredHeight - ItemHeight);
			}
			int num = _borderOverheadDevice;
			// 2026-10-09（0.291 beta22 补丁⑥；用户原话「顺便把这个10分钟的高度改小一点」）：
			// **用"族参考字体"（9pt，与 UiStyleBody/ApplyWindow 一致）算项高**，而不是用本控件**继承来的**字体。
			// 这是"某颗下拉比同族高一档"的**真正根因**：灯效「10 分钟」那颗的父面板字体更大 ⇒ 原先这里的
			// `Font.GetHeight(...)` 更大 ⇒ `ItemHeight` 更大 ⇒ `ComboBox.PreferredHeight` 更大（175% 实测 48，
			// 而同族 40），而 `ComboBox.SetBoundsCore` 内部会**把高度强制回 `PreferredHeight`** ⇒ 单靠在
			// `SetBoundsCore`/`MaximumSize` 里改高度压不住。改完这一处，全族 `ItemHeight` 同源 ⇒ 高度自然一致。
			int num2 = FamilyFontHeight(UiDpi.Layout(this));
			// 2026-10-09（修列表滚轮错位）：⭐ **总高口径统一到唯一来源** `UiVisualStyle.ComboHeightLogical`
			//（＝与 `ApplyFamilyHeight`/`SetBoundsCore` 钉住的族高度同一个常量），不再另写一个
			// `44 * dpi / 192`（＝22 逻辑像素，与族高度 23 逻辑像素**差 1 逻辑像素**，175% 下就是 38 vs 40：
			// 又一处"两套值"）。行为：`ItemHeight = 族高度 - 边框开销`，与原生"闭合高 = 行高 + 边框"同构。
			int num3 = (NativeHeight ? (num2 + num) : ((int)Math.Round(UiVisualStyle.ComboHeightLogical * UiDpi.Layout(this) / 96f)));
			ItemHeight = Math.Max(num2, num3 - num);
		}
		// 2026-10-09（0.291 beta22 补丁⑥；用户原话「顺便把这个10分钟的高度改小一点」）：
		// **下拉族的统一高度**（真正的落点在 <see cref="SetBoundsCore" /> —— 布局稍后还会再设一次高度，
		// 只在 HandleCreated 里改会被覆盖掉；这里保留一次"尽早生效"的写入，最终以 SetBoundsCore 的钳制为准）。
		ApplyFamilyHeight();
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑥）：**族参考字体的行高**（9pt 与 `UiStyleBody()`/`ApplyWindow` 同档）
	/// —— 见 <see cref="CalibrateItemHeight" />：用它替代"本控件继承来的字体"来算 `ItemHeight`，
	/// 使"继承了更大父字体"的下拉不会比同族高一档（用户报的灯效「10 分钟」48 vs 同族 40）。
	/// </summary>
	private static int FamilyFontHeight(int dpi)
	{
		using Font reference = UiVisualStyle.Font(9f);
		return (int)Math.Ceiling(reference.GetHeight(dpi)) + 2;
	}

	/// <summary>把本控件的高度钳到下拉族的族标准（见 <see cref="UiVisualStyle.ComboHeightLogical" />）。</summary>
	private void ApplyFamilyHeight()
	{
		if (!UseFamilyHeight || Dock != DockStyle.None || IsDisposed)
		{
			return;
		}
		int familyHeight = (int)Math.Round(UiVisualStyle.ComboHeightLogical * UiDpi.Paint(this) / 96f);
		if (familyHeight <= 0 || MinimumSize.Height > familyHeight)
		{
			return;
		}
		// ⚠️ 2026-10-09（补丁⑥）：**光在 SetBoundsCore 里改高度不够** —— `TableLayoutPanel` 会把
		// `Anchor=Left|Right` 的子控件**纵向拉伸填满单元格**（实测灯效「10 分钟」那颗在 D(32) 的行里
		// 被拉到 48 设备像素、而同族是 40）。因此在这里同时把 **`MaximumSize.Height` 钉到族标准**：
		// WinForms 的 `SetBoundsCore` 会把任何超过 `MaximumSize` 的取值夹回去 ⇒ **布局引擎再拉伸也超不过**
		//（宽度上限保持 0＝不限宽）。
		if (MaximumSize.Height != familyHeight)
		{
			MaximumSize = new Size(MaximumSize.Width, familyHeight);
		}
		if (Height != familyHeight)
		{
			Height = familyHeight;
		}
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑥）：**下拉族高度在这里统一** —— 所有对 Bounds 的写入（构造、
	/// 布局引擎、父容器、DPI 变化）都会经过本方法，于是"某颗下拉比同族高一档"这种情况从结构上不可能再出现。
	/// 依据：灯效「10 分钟」那颗继承了更大的父面板字体 ⇒ `PreferredHeight` 是 48 设备像素，而同族是 40
	///（175% DPI `[diag]` 实测）。⚠️ 只改 `height` 一个分量；`Dock != None`（由父容器决定高度）、
	/// 或 `MinimumSize` 高于族标准时一律不碰。
	/// </summary>
	protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
	{
		if (UseFamilyHeight && Dock == DockStyle.None && height > 0 && !IsDisposed)
		{
			int familyHeight = (int)Math.Round(UiVisualStyle.ComboHeightLogical * UiDpi.Paint(this) / 96f);
			if (familyHeight > 0 && MinimumSize.Height <= familyHeight)
			{
				height = familyHeight;
			}
		}
		base.SetBoundsCore(x, y, width, height, specified);
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑥）：是否把本下拉的高度统一到下拉族的族标准
	/// （见 <see cref="CalibrateItemHeight" />）。默认开启；个别语义上必须更高的下拉可置 false。
	/// </summary>
	public bool UseFamilyHeight = true;

	/// <summary>
	/// 2026-10-09（补丁⑫）：闭合态要显示的文字 —— **两个来源依次回退**，保证"文字永不丢失"：
	/// ① 首选 `ComboBox.Text`（`DropDownList` 正常时的选中项显示文本）；
	/// ② 若为空但 `SelectedIndex` 有效，则回退到 `GetItemText(Items[SelectedIndex])`。
	/// 为什么需要回退：启动后的校色/能力状态刷新会 `Items.Clear()` + 重新 `Add` + 重设 `SelectedIndex`，
	/// 期间 `Text` 可能瞬时/持续为空（尤其 `DisplayMember` 生效时机晚于 `SelectedIndex`）——
	/// 这正是用户截图里「屏幕校色」那颗**文字为空**的原因。
	/// </summary>
	private string ClosedStateText()
	{
		string label = GetItemText(Text);
		if (string.IsNullOrEmpty(label) && SelectedIndex >= 0 && SelectedIndex < Items.Count)
		{
			label = GetItemText(Items[SelectedIndex]);
		}
		return label ?? string.Empty;
	}

	/// <summary>
	/// 2026-10-09（批次3 #8「--ui-audit 下拉框截断盲区」）：**闭合态文本矩形**的唯一算式 ——
	/// 真实绘制（<see cref="DrawClosedStateText" /> 与 <see cref="PaintSplit" />）与 --ui-audit 的度量
	/// 现在共用这一处，⛔ 不再各算一遍（审计报"装不下"就等于绘制会打省略号）。
	/// 几何逐字照旧：左内边距 <see cref="TextPadLogical" />（DPI 缩放）、右侧让出箭头格
	/// <see cref="ArrowCellLogicalPx" />（DPI 缩放，且不超过客户区一半）、再扣右侧同样一份内边距。
	/// ⚠️ 只做只读计算，不改 Bounds/Region/任何状态。
	/// </summary>
	private Rectangle ClosedStateTextRectangle()
	{
		Rectangle client = ClientRectangle;
		float scale = UiDpi.Paint(this) / 96f;
		int pad = (int)Math.Round((float)TextPadLogical * scale);
		int cellWidth = Math.Min(Math.Max(1, (int)Math.Round((float)ArrowCellLogicalPx * scale)), Math.Max(1, client.Width / 2));
		return new Rectangle(client.Left + pad, client.Top, Math.Max(1, client.Width - cellWidth - pad * 2), client.Height);
	}

	/// <summary>
	/// 2026-10-09（批次3 #8）：**--ui-audit 专用**只读入口 —— 闭合态要画的文字与它的可用矩形
	/// （与真实绘制同一来源，见 <see cref="ClosedStateTextRectangle" />）。⛔ 不改任何绘制行为。
	/// </summary>
	internal string ClosedStateTextForAudit()
	{
		return ClosedStateText();
	}

	/// <summary>2026-10-09（批次3 #8）：**--ui-audit 专用**只读入口 —— 闭合态文本矩形（绘制用的那一个）。</summary>
	internal Rectangle ClosedStateTextRectangleForAudit()
	{
		return ClosedStateTextRectangle();
	}

	/// <summary>闭合态文字的统一绘制（`OnDrawItem` 的 `ComboBoxEdit` 分支与 `PaintSplit` 共用一个来源）。</summary>
	private void DrawClosedStateText(Graphics g)
	{
		TextRenderer.DrawText(g, ClosedStateText(), Font, ClosedStateTextRectangle(), Enabled ? ForeColor : UiVisualStyle.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
	}

	protected override void OnDrawItem(DrawItemEventArgs e)
	{
		// 2026-10-09（0.291 beta22 补丁⑩；用户原话「依旧不是 但是只要你开关 或者调整窗口大小等它就能正确绘制了」）：
		// ⭐ **闭合态（"选择字段"本体）的 owner-draw 通知必须跳过** —— 这就是"首绘不对、任何强制重绘后就正确"的根因。
		// 机制：`CBS_OWNERDRAWFIXED` 的 DropDownList 在绘制**选择字段**时，Windows 也会发一次 `WM_DRAWITEM`
		//（`e.State` 带 `DrawItemState.ComboBoxEdit`），其 `e.Bounds` **覆盖整个客户区**；原实现无条件
		// `FillRectangle(BackColor)` ⇒ 把 `PaintFlat` 刚画好的**2 逻辑像素圆角描边整片盖成平的 Input**
		//（只留下最外那一圈 AA 像素 ⇒ 用户看到的"细线/不对"）。
		// 而 `WM_DRAWITEM` 只在选择字段需要重画时下发，**强制重绘（拨开关/改窗口大小/切主题）不会再发**它
		// ⇒ 我们的描边保住 ⇒ 用户观察到的"一操作就正确"。
		// 判据用的是既有语义 `DrawItemState.ComboBoxEdit`（不是猜测、不做文字匹配）。
		// 2026-10-09（补丁⑫；用户原话「它刚刚打开是对的 但是它会被启动后一次状态刷新刷掉」＋截图里
		// 「屏幕校色」那颗**文字为空**）：⚠️ 这一支**原来顺带画了闭合态的文字**（用 `Items[e.Index]`），
		// 上一版早退之后就只剩 `PaintSplit` 里那句 `GetItemText(Text)` 了 —— 而启动后那次**校色状态刷新**
		//（`RebuildCalibrationItems(calibCombo)`：`Items.Clear()` → 重新 `Add` → 重设 `SelectedIndex`）
		// 会让 `Text` 短暂/持续为空（`SelectedIndex == -1` 或 `DisplayMember` 尚未生效）⇒ **文字消失**。
		// 现在这一支**只画文字、绝不铺底**：既不盖我们刚画的 2 逻辑像素圆角描边，文字也不会丢。
		if ((e.State & DrawItemState.ComboBoxEdit) != 0)
		{
			DrawClosedStateText(e.Graphics);
			return;
		}
		if (!UseCustomTextPadding || e.Index < 0)
		{
			base.OnDrawItem(e);
			return;
		}
		// 2026-10-09（0.291 beta22）：展开列表的整行铺满 —— 未选中行＝输入区底（Input），选中/悬停行＝
		// ButtonFaceSelected（夜间 86/86/86、日间 209/219/233，两主题同一套"选中＝另起一档底"的规则），
		// 文字一律 ForeColor（＝Text，夜浅日深，对两种底都 ≥4.5 AA）。
		// ⚠️ 只换颜色，不改项内容/顺序/索引。
		bool selected = (e.State & DrawItemState.Selected) != 0;
		Color back = (selected ? UiVisualStyle.ButtonFaceSelected : ((BackColor == Color.Empty) ? UiVisualStyle.Input : BackColor));
		using (SolidBrush brush = new SolidBrush(back))
		{
			e.Graphics.FillRectangle(brush, e.Bounds);
		}
		// 2026-10-09（P0.5）：DPI 统一到**控件自己的 DPI**（Control.DeviceDpi），不再读 e.Graphics 的 DpiX
		//（展开列表行的文字内边距，几何只差 DPI 来源；100% 下数值不变）。
		int num = (int)Math.Round(8f * UiDpi.Paint(this) / 96f);
		Rectangle bounds = new Rectangle(e.Bounds.X + num, e.Bounds.Y, Math.Max(1, e.Bounds.Width - num), e.Bounds.Height);
		Color fore = (Enabled ? ForeColor : UiVisualStyle.Muted);
		TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), e.Font, bounds, fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
	}

	public static GraphicsPath RoundedRect(Rectangle bounds, int radiusL, int radiusR)
	{
		int num = radiusL * 2;
		int num2 = radiusR * 2;
		Size size = new Size(num, num);
		Size size2 = new Size(num2, num2);
		Rectangle rect = new Rectangle(bounds.Location, size);
		Rectangle rect2 = new Rectangle(bounds.Location, size2);
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(rect, 180f, 90f);
		rect2.X = bounds.Right - num2;
		rect2.Y = bounds.Top;
		graphicsPath.AddArc(rect2, 270f, 90f);
		rect2.Y = bounds.Bottom - num2;
		rect2.X = bounds.Right - num2;
		graphicsPath.AddArc(rect2, 0f, 90f);
		rect.X = bounds.Left;
		rect.Y = bounds.Bottom - num;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑥）：**浮点**圆角路径 —— 控件边和填充共用同一条曲线，半径按
	/// UiDpi.Paint/192*12 缩放（96 DPI 下 6px、150% 下 9px）；用 <see cref="PenAlignment.Inset" />
	/// 让整条线宽都落在矩形内，故路径本身贴在客户区边界（不做半像素外移），填充与描边严丝合缝。
	/// </summary>
	private static GraphicsPath FlatRoundedPath(Rectangle bounds, float radius)
	{
		GraphicsPath path = new GraphicsPath();
		float r = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
		if (r <= 0.5f)
		{
			path.AddRectangle(bounds);
			return path;
		}
		float d = r * 2f;
		path.AddArc(bounds.Left, bounds.Top, d, d, 180f, 90f);
		path.AddArc(bounds.Right - d, bounds.Top, d, d, 270f, 90f);
		path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
		path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90f, 90f);
		path.CloseFigure();
		return path;
	}

	public static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int cornerRadiusL = 5, int cornerRadiusR = 5)
	{
		if (graphics == null)
		{
			throw new ArgumentNullException("graphics");
		}
		if (pen == null)
		{
			throw new ArgumentNullException("pen");
		}
		using GraphicsPath path = RoundedRect(bounds, cornerRadiusL, cornerRadiusR);
		graphics.DrawPath(pen, path);
	}

	public static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int cornerRadiusL = 5, int cornerRadiusR = 5)
	{
		if (graphics == null)
		{
			throw new ArgumentNullException("graphics");
		}
		if (brush == null)
		{
			throw new ArgumentNullException("brush");
		}
		using GraphicsPath path = RoundedRect(bounds, cornerRadiusL, cornerRadiusR);
		graphics.FillPath(brush, path);
	}

	protected override void WndProc(ref Message m)
	{
		// 2026-10-09（0.291 beta22 补丁⑥ 收口；用户原话「边框先一开始是粗线 马上被刷新成细线 …
		// 我打开校色开关才是又变成粗线」）：**接管擦背景 + 吞掉 WM_NCPAINT**。
		// 2026-10-09（P0）：这两条改走**全仓共用入口** <see cref="UiVisualStyle.HandleOpaqueChrome" />
		//（与 RButton / RCheckBox / RSlider / RNumericUpDown / RComboDisplay / RScrollBar 同一份逻辑，不再各写一遍）：
		//   · `WM_ERASEBKGND`：整块铺**父容器真色**（`ContextBackColor`）并返回 1 —— 原生 COMBOBOX 在某些重绘
		//     时序（主题/按钮格重绘、窗体激活）里会用**系统窗口刷子（白）**在我们绘制之后再擦一次，
		//     那次擦除不经过 `WM_PAINT`（拦不到），于是客户区右半＋角部留下白块，
		//     与我们的填充之间形成一道硬边界 —— 正是"很怪的内部边线"。现在系统那个白刷子永远不会再碰这块客户区。
		//   · `WM_NCPAINT`：**不再交给原生处理**（这是"原生再画一圈方框"的唯一入口）。NC 区实测为 0
		//     （`size == client`，证据行已按"用完即撤"删除）⇒ 本来也没有属于它的像素要画。
		if (UiVisualStyle.HandleOpaqueChrome(this, ref m))
		{
			return;
		}
		// 2026-10-09（0.291 beta22 补丁⑥；用户原话「我觉得Ghelper的那个控件其实很舒服」）：
		// **恢复默认的下拉行为** —— 这里原来吞掉 `WM_LBUTTONDOWN`（0x0201）与 F4 / Alt+↓，
		// 目的是"让原生 LISTBOX 永远打不开、改用我们自己的弹层 `RComboPopup`"。用户明确说 G-Helper
		// 那个控件舒服，而 G-Helper 的做法正是"闭合态自己画 + **展开用系统原生列表**（只套深色主题）"
		// ⇒ 那几处拦截**全部删除**，原生列表恢复；滚轮/键盘/滚动条/贴屏边/点击外收起由系统负责。
		// 原生列表的主题适配保持原样（都是既有代码，未改）：
		//   · `UiVisualStyle.StyleComboBox` → `ApplyDarkComboTheme` → `SetWindowTheme(handle, "DarkMode_CFD"/"CFD")`；
		//   · 本类的 `WM_CTLCOLORLISTBOX` → 返回 `Input` 实色刷子；
		//   · 本类的 `OnDrawItem`（`DrawMode=OwnerDrawFixed`）→ 整行铺满、选中/悬停行 `ButtonFaceSelected`、文字 `ForeColor`。
		if ((m.Msg == WM_CTLCOLORLISTBOX || m.Msg == WM_CTLCOLOREDIT) && DropDownStyle != ComboBoxStyle.Simple)
		{
			nint brush = ListBackgroundBrush();
			if (brush != IntPtr.Zero)
			{
				m.Result = brush;
				return;
			}
		}
		// 2026-10-13 logdefault-error / combofocus（用户原话「设置窗口选中时候下拉框会变成细线状态 鼠标移动上去
		// 才恢复」）：⭐ **原生"选择字段"被重铺之后，必须由我们补一帧整块自绘**。
		// 机制（沿用本文件既有结论，不是新猜测）：user32 的组合框在这些消息里会**自己**把"选择字段"
		// （客户区向内缩约 2 设备像素的那块）用 <c>BackColor</c> 重铺一遍 —— 那一步**不经过本控件的
		// `WM_PAINT` 拦截**（2026-10-09 逐消息探针实测：`WM_PAINT`/`WM_ERASEBKGND`/`WM_DRAWITEM` 全 0 次、
		// `GetUpdateRect` 为空），于是 2 逻辑像素圆角描边的**内半被削掉**、屏幕上只剩一道"细线"；
		// 又因为**没有任何人失效本控件**，这个状态会一直停到鼠标悬停（`UpdateArrowHot` → `Invalidate()`）。
		// 现在：这几类消息**先交 base 处理**（原生的重铺就在里面发生），**再**由我们整块重画 ⇒
		// "我们画的"永远落在"原生刚铺的"之后，与激活前后、焦点来回无关。
		// ⚠️ 只补重绘，不改 Items / SelectedIndex / 事件 / 尺寸 / 命中区域；`Simple` 样式仍需原生编辑子窗口，不拦。
		if ((m.Msg == WM_SETFOCUS || m.Msg == WM_KILLFOCUS || m.Msg == WM_UPDATEUISTATE || m.Msg == WM_CHANGEUISTATE || m.Msg == CB_SETCURSEL || m.Msg == CB_RESETCONTENT) && DropDownStyle != ComboBoxStyle.Simple)
		{
			base.WndProc(ref m);
			RepaintAfterNativeFieldPaint();
			return;
		}
		if (m.Msg == WM_PAINT && DropDownStyle != ComboBoxStyle.Simple)
		{
			PaintFlat(ref m);
			return;
		}
		base.WndProc(ref m);
	}

	/// <summary>
	/// 2026-10-13：原生组合框刚刚**绕过本控件**重铺过"选择字段"时的补偿重绘（见 <see cref="WndProc" />
	/// 那一支的说明）。三拍的理由：
	///   ① <c>Invalidate()</c> + <c>Update()</c> —— 立刻同步画一帧，把整块自绘（圆角填充 + 2 逻辑像素描边
	///      + chevron + 文字）盖在原生那次重铺之上；
	///   ② 末尾再 <c>Invalidate()</c> 留一拍 —— 万一原生那次重铺实际排在**本消息之后**（它是控件内部的
	///      子窗口/原生路径，`Update()` 只处理本窗口的失效区），这一拍保证最终仍是我们的整块自绘。
	/// ⚠️ 不做任何状态修改：不碰 Bounds / Region / Items / 索引 / 事件；句柄未建/不可见时 <c>Update()</c> 自身短路。
	/// </summary>
	private void RepaintAfterNativeFieldPaint()
	{
		if (!IsHandleCreated || IsDisposed)
		{
			return;
		}
		Invalidate();
		Update();
		Invalidate();
	}

	private nint ListBackgroundBrush()
	{
		Color wanted = UiVisualStyle.Input;
		if (_listBrush != IntPtr.Zero && _listBrushColor == wanted)
		{
			return _listBrush;
		}
		if (_listBrush != IntPtr.Zero)
		{
			DeleteObject(_listBrush);
			_listBrush = IntPtr.Zero;
		}
		_listBrush = CreateSolidBrush(ColorTranslator.ToWin32(wanted));
		_listBrushColor = wanted;
		return _listBrush;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			EndDropDownFollow();
			_dropFollowTimer?.Dispose();
			_dropFollowTimer = null;
			if (_listBrush != IntPtr.Zero)
			{
				DeleteObject(_listBrush);
				_listBrush = IntPtr.Zero;
			}
		}
		base.Dispose(disposing);
	}

	private void PaintFlat(ref Message m)
	{
		// 2026-10-09（UI 打磨⑥）：主题下发之后再动态创建的下拉框（灯效组里的「单色」等）也被补上配色。
		// 2026-10-09（0.291 beta22 补丁）：⚠️ 首绘自愈若**真的改过**外观（FlatStyle / 底 / 边 / 箭头 /
		// 窗口主题），本帧一律**不画**并重绘一次：那些 setter 可能重建 HWND 或改窗口主题，本帧的裁剪区
		// 已经不可信 —— 硬画下去会被整块裁掉，屏幕上只剩系统原生绘制（"旧版方角下拉"）。
		// 构造函数已把 FlatStyle 定成 Flat，稳态下自愈恒返回 false，这一支永不进入。
		if (UiVisualStyle.StyleComboBoxIfStale(this) && m.WParam == IntPtr.Zero)
		{
			Invalidate();
			return;
		}
		Rectangle client = ClientRectangle;
		if (client.Width <= 2 || client.Height <= 2)
		{
			base.WndProc(ref m);
			return;
		}
		// 2026-10-09（0.291 beta22 补丁⑥）：**构建标识**（一次性）—— 把正在运行的 DLL 的版本/大小/写入时间
		// 打出来，方便与产物 SHA256 对账，直接回答"用户看到的到底是不是这一版构建"。
		// ⚠️ **保留**（用户明确要求"保留构建标识"）：它只在**每个进程第一次绘制下拉框**时写一行，
		// 稳态零开销，且是"安装态到底是不是这一版"的唯一运行时凭据。
		if (!_buildDiagLogged)
		{
			_buildDiagLogged = true;
			try
			{
				string loc = typeof(RComboBox).Assembly.Location;
				FileInfo fi = new FileInfo(loc);
				Logger.WriteLine($"[diag] RComboBox build={System.Diagnostics.FileVersionInfo.GetVersionInfo(loc).FileVersion} size={fi.Length} mtime={fi.LastWriteTime:yyyy-MM-dd HH:mm:ss} path={loc}");
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[diag] RComboBox build=unknown " + ex.GetType().Name);
			}
		}
		PAINTSTRUCT lpPaint = default;
		bool ownsHdc = false;
		nint hdc;
		if (m.WParam == IntPtr.Zero)
		{
			hdc = (m.WParam = BeginPaint(Handle, ref lpPaint));
			ownsHdc = true;
		}
		else
		{
			hdc = m.WParam;
		}
		Color context = UiVisualStyle.ContextBackColor(this);
		Color outline = BorderColor;
		Color arrow = (Enabled ? ArrowColor : UiVisualStyle.Muted);
		// 2026-10-09（0.291 beta22 补丁⑦ 终收口；用户原话「什么答辩石山代码 就不能直接干掉 直接每次就是我们要的
		// 盖来盖去的」）：**本方法现在是唯一且完整的绘制过程 —— 一次画完最终结果，不再有"先画系统、再擦掉、
		// 再盖一层"的任何步骤**：
		//   ① `BeginPaint`（由 `WndProc` 的入口完成）→ ② 整块客户区铺**父容器真色**（`ContextBackColor`：
		//   圆角路径之外那几颗像素与父容器逐分量相同 ⇒ 看不出边界、也不会有"整块异色矩形"假边）
		//   → ③ `PaintSplit` 一次画出：圆角填充 + 2 逻辑像素圆角描边（`ComboOutlineWidth`，同一个
		//   `FlatRoundedPath`）+ chevron + **最后**补文字 → ④ `EndPaint`。
		// ⚠️ 原生 COMBOBOX 的客户端绘制**从不被调用**（`DropDownList` 分支不调 `DefWndProc`），
		// 系统也没有其它入口能再描一圈：`WM_NCPAINT` 被吞（见 `WndProc`）、`WM_ERASEBKGND` 由我们
		// 自己铺色并返回 1。因此"首帧 2px、下一帧被刷成 1px"这种现象在机制上不可能再出现。
		// ⚠️ 唯一的例外（有意保留）：可编辑的 `DropDown`/`Simple` 样式仍需原生绘制其**编辑子窗口**，
		// 才调用 `DefWndProc`；全仓所有下拉都是 `DropDownList`，走不到那一支。
		using (Graphics g = Graphics.FromHdc(hdc))
		{
			SmoothingMode smoothing = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.None;
			using (SolidBrush ctx = new SolidBrush(context))
			{
				g.FillRectangle(ctx, client);
			}
			g.SmoothingMode = smoothing;
		}
		if (DropDownStyle != ComboBoxStyle.DropDownList)
		{
			// 仅可编辑样式：让原生编辑子窗口有机会绘制（本仓无此类下拉）。
			DefWndProc(ref m);
		}
		// 2026-10-09（0.291 beta22 补丁⑥；用户原话「很奇怪」＋放大图"左侧出现双层边/一道细缝"）：
		// **不再给下拉窗口设圆角 `Region`** —— 这是"缝"的终结修法。
		// 根因：`SetWindowRgn` 的裁剪边界比我们画的圆角路径**向外多出 2 设备像素**（为了让 AA 羽化不被切），
		// 于是"路径 → 窗口裁剪边界"之间夹出一圈**只有父容器底色**的环；只要 `ContextBackColor` 与该位置
		// 父容器**实际画出来的**颜色有一点点差（卡片/行/页面底三层里任何一层不一致），这圈就成了一道可见的
		// **"双层边/细缝"**；而裁剪本身又是二值的 ⇒ 圆角处还会剩阶梯。
		// 现在的做法（与按钮族同源、且不依赖任何窗口裁剪）：
		//   ① 先把**整个客户区**铺成该位置真实的父容器底色（`ContextBackColor`，`PaintFlat` 里铺一次）；
		//   ② 再用 **`SmoothingMode.AntiAlias`** 画"圆角填充 + 圆角描边"（同一个 `FlatRoundedPath`）。
		//   ⇒ 圆角路径之外那几颗像素＝父容器同色（看不出来），**不存在任何缝/双层边**，圆角拿到**完整抗锯齿**；
		//     窗口不裁剪 ⇒ 也不会再有"被二值裁剪切掉羽化"的阶梯。
		// ⚠️ 前提：这些下拉所在的父容器在角部是**纯色**（本文件的父链是"行面板/卡片/页面底"，都是实色 `BackColor`，
		//    由 `ApplyRow`/`ApplySection` 显式下发；`[diag]` 里同时打印 `contextFill` 供对账）。
		// ⚠️ 只影响外观：不设 Region、不改 Bounds/Margin/命中区域；文字仍在 `PaintSplit` 的最后补画。
		using (Graphics g = Graphics.FromHdc(hdc))
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			// 2026-10-09（0.291 beta22）：**不再分日/夜两套画法** —— 夜间与日间共用同一段
			// 「箭头格与输入区同底 + 细 chevron + 整块 1px Inset 描边」的几何，
			// 只由 UiVisualStyle 的令牌（BorderStrong、ComboCornerDevicePx）给出各主题的颜色与半径。
			PaintSplit(g, client, outline, arrow);
		}
		if (ownsHdc)
		{
			EndPaint(Handle, ref lpPaint);
		}
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑦ 夜间分体式；0.291 beta22 起**日间同构、并去掉"箭头格亮一档"**）：
	/// 我们自己的下拉框画法 —— 输入区 + 一条 1px 极淡竖分隔线 + 右侧一格同底的箭头格。
	///
	///   ① **输入区**：整块外圆角矩形（<see cref="UiVisualStyle.CornerRadiusDevicePx" />＝4 逻辑像素，
	///      与按钮族同一个半径），底 = <c>BackColor</c>（= Input，夜间 20/20/20、日间 224/232/241，
	///      都是"凹陷/可输入"那一档，语义不变）；
	///   ② **箭头格**：右侧 <see cref="ArrowCellLogical" /> = 26 逻辑像素（按 DPI 缩放，且不超过半宽）的独立一格，
	///      **右侧两角圆、左侧两角方**（与整块的外圆角同心）。⚠️ 0.291 beta22 起它的**静止底与输入区完全相同**
	///      （改前是夜间 ButtonFace 62 画在 Input 20 上，Δ42 ⇒ 像老式下拉按钮）—— 默认状态只有一条极淡的
	///      竖线把它和输入区分开；**只有悬停/展开时才轻微变一档**（<see cref="UiVisualStyle.ComboCellHover" />
	///      / <see cref="UiVisualStyle.ComboCellActive" />，对输入区 1.12~1.29）；
	///   ③ **1px 竖分隔线**：画在 <c>cell.Left + 0.5f</c>（整数像素列 ⇒ 纯色硬线，不跨半像素），颜色 =
	///      <see cref="UiVisualStyle.ComboSeparator" />（Input 与 BorderStrong 的中间色 ⇒ 比整块边框淡得多）；
	///   ④ **整块 1px 描边**：颜色 = BorderColor（BorderStrong，夜间 74/74/74、日间 158/175/196），
	///      两主题一律 <c>PenAlignment.Inset</c> ⇒ 线宽整条落在最外一列/行像素内（硬边，不发虚）；
	///      圆角处仍由 AntiAlias 平滑 ⇒ 也不锯齿；
	///   ⑤ **文字在所有这些铺底之后补画**（上一轮"文字被盖"的回归点），文本矩形仍在输入区内
	///      （右边界止于箭头格左沿，不会压到箭头格）；
	///   ⑥ 六条硬约束（《UI样式规范》§7.3）全部保持：铺底在文字之前、不用 Widen 的环形做 Region
	///      （本控件不设 Region）、圆角外填 <see cref="UiVisualStyle.ContextBackColor" />、1px 线落在整数像素列。
	///
	/// ⚠️ 只改外观：不改 Items / SelectedIndex / 事件 / DropDownWidth / 尺寸 / 命中区域。
	/// 展开列表仍走既有的 DarkMode_CFD(夜)/CFD(日) + WM_CTLCOLORLISTBOX(Input 刷) + OnDrawItem 整行铺满 ⇒ 与主题一致。
	/// </summary>
	private void PaintSplit(Graphics g, Rectangle client, Color outline, Color arrow)
	{
		// 2026-10-09（0.291 beta22 补丁）：**一律用控件自己的 DPI**（`UiDpi.Paint(this)` = DeviceDpi），
		// 不再用绘制 DC 的 `g.DpiX`。原因：窗口 DC 上的 DpiX 取的是设备能力里的 LOGPIXELSX，在
		// PerMonitorV2 / "系统 DPI ≠ 显示器 DPI" 下可能回 96 —— 4 逻辑像素的圆角就被算成 4 **设备**像素
		//（175% 屏上只等于约 2.3 逻辑像素）⇒ 看上去就是直角；同理 chevron 与文字内边距也会偏小。
		// `ArrowCellRectangle()` 本来就是按控件 DPI 算的，本行改完两者终于同源。
		float scale = UiDpi.Paint(this) / 96f;
		int radius = UiVisualStyle.ComboCornerDevicePx(this);
		Rectangle cell = ArrowCellRectangle();
		Rectangle field = new Rectangle(client.Left, client.Top, Math.Max(1, cell.Left - client.Left), client.Height);
		Color fieldColor = ((BackColor == Color.Empty) ? ButtonColor : BackColor);
		if (fieldColor.A == 0)
		{
			fieldColor = UiVisualStyle.Input;
		}
		// 2026-10-09（0.291 beta22 补丁②；用户原话「这个竖块还在」）：**箭头格底色恒等于输入区底色**
		//（逐分量相同 ⇒ **任何状态下都不会出现"竖块/色块"**）。上一轮只删了竖分隔线、却仍保留了
		// 「悬停/展开时箭头格变一档」，而用户截图时鼠标正停在下拉上 ⇒ 那块 46 设备像素宽的
		// `ComboCellHover`（日间 215,224,235 比输入区 224,232,241 略深）就是他说"还在"的竖块。
		// 现在把"悬停/展开"的反馈**只落到 chevron 的颜色**上（`UiVisualStyle.Accent` 一档，见下），
		// 填充面永远与输入区零差异。
		Color cellColor = fieldColor;
		bool open = DroppedDown;
		using GraphicsPath shape = FlatRoundedPath(client, radius);
		using (SolidBrush brush = new SolidBrush(fieldColor))
		{
			g.FillPath(brush, shape);
		}
		if (cellColor != fieldColor)
		{
			using GraphicsPath cellPath = FlatRoundedPathCorners(cell, radius, roundLeft: false, roundRight: true);
			using SolidBrush brush2 = new SolidBrush(cellColor);
			g.FillPath(brush2, cellPath);
		}
		// 2026-10-09（0.291 beta22 补丁；用户原话「箭头左边还有奇怪深色矩形也删除」）：**不画分隔线**。
		// ⚠️ 2026-10-09（补丁②）：**不再画任何分隔线**，箭头格与输入区静止时同底 ⇒ 两者连成一块，观感干净。
		// （`UiVisualStyle.ComboSeparator` 令牌保留但已无调用点，作为"想加回一条线"的回退旋钮。）
		// 2026-10-09（补丁②）：悬停/展开的反馈改由 chevron 承担（只改颜色，不改任何填充面）。
		Color chevronColor = ((open || (_arrowHot && Enabled)) ? UiVisualStyle.Accent : arrow);
		DrawChevron(g, cell, chevronColor);
		// ⚠️ 文字必须在所有铺底之后补画（历史回归："文字消失了"）。
		if (UseCustomTextPadding && DrawMode != DrawMode.Normal)
		{
			// 2026-10-09（批次3 #8）：文本矩形改走**唯一来源** <see cref="ClosedStateTextRectangle" />（与审计同源；
			// 算式与改动前逐字相同：左 pad、右侧让出箭头格再扣一份 pad）。
			TextRenderer.DrawText(g, ClosedStateText(), Font, ClosedStateTextRectangle(), Enabled ? ForeColor : UiVisualStyle.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
		}
		// 2026-10-09（0.291 beta22 补丁⑤；用户原话「我发现是圆角边框太细而且有锯齿导致的」）：
		// 描边由**恒定 1 设备像素**加粗到与按钮族选中态**同一档 2 逻辑像素**
		//（`ComboOutlineWidth`：100%→2、175%→4 设备像素）；`PenAlignment.Inset` ⇒ 2px **整条落在客户区内**、
		// 且外沿就是路径本身（整数坐标）⇒ 不会把 2px 摊成三条灰边；`SmoothingMode.AntiAlias`（调用方已设）
		// ⇒ 圆角处是平滑的，不再有"阶梯"。文字内边距 `pad = 8×scale` 远大于 2px，不会被压到或裁切。
		// 2026-10-09（P0.5/P2）：线宽改走**唯一工厂** <see cref="UiVisualStyle.OutlinePen(Control, Color, float, float, bool)" />
		// —— 按控件自己的 DPI + **整数**设备像素 + Inset，数值与改动前逐 DPI 相同（100%→2、125%→3、
		// 150%→3、175%→4、200%→4），与按钮族选中边 `<c>ModeOutlineLogical</c>` 同源。
		using Pen pen = UiVisualStyle.OutlinePen(this, outline, UiVisualStyle.ModeOutlineLogical, 2f);
		g.DrawPath(pen, shape);
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑦，仅夜间）：四角可分别取舍的圆角路径（浮点）—— 箭头格要「右角圆、左角方」，
	/// 才能与整块外圆角同心、又与输入区严丝合缝地拼在一起。半径仍被夹到「不大于半高/半宽」。
	/// </summary>
	private static GraphicsPath FlatRoundedPathCorners(Rectangle bounds, float radius, bool roundLeft, bool roundRight)
	{
		GraphicsPath path = new GraphicsPath();
		float r = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
		if (r <= 0.5f)
		{
			path.AddRectangle(bounds);
			return path;
		}
		float d = r * 2f;
		if (roundLeft)
		{
			path.AddArc(bounds.Left, bounds.Top, d, d, 180f, 90f);
		}
		else
		{
			path.AddLine(bounds.Left, bounds.Top, bounds.Left, bounds.Top);
		}
		if (roundRight)
		{
			path.AddArc(bounds.Right - d, bounds.Top, d, d, 270f, 90f);
		}
		else
		{
			path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Top);
		}
		if (roundRight)
		{
			path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
		}
		else
		{
			path.AddLine(bounds.Right, bounds.Bottom, bounds.Right, bounds.Bottom);
		}
		if (roundLeft)
		{
			path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90f, 90f);
		}
		else
		{
			path.AddLine(bounds.Left, bounds.Bottom, bounds.Left, bounds.Bottom);
		}
		path.CloseFigure();
		return path;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑦，仅夜间）：箭头格的矩形（与 <see cref="PaintNightSplit" /> 用同一套算式，
	/// 供鼠标命中使用）。宽 = <see cref="ArrowCellLogical" /> 逻辑像素 × DPI，且不超过客户区一半
	/// （窄下拉框上不会把输入区挤没）。只做只读计算，不写 Bounds/Region。
	/// </summary>
	private Rectangle ArrowCellRectangle()
	{
		Rectangle client = ClientRectangle;
		float scale = UiDpi.Paint(this) / 96f;
		int width = Math.Min(Math.Max(1, (int)Math.Round((float)ArrowCellLogicalPx * scale)), Math.Max(1, client.Width / 2));
		return new Rectangle(client.Right - width, client.Top, width, client.Height);
	}

	private void UpdateArrowHot(bool hot)
	{
		if (_arrowHot == hot)
		{
			return;
		}
		_arrowHot = hot;
		// 2026-10-09（0.291 beta22）：日间同样有箭头格反馈（改前只有夜间重绘）。
		Invalidate();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		UpdateArrowHot(ArrowCellRectangle().Contains(e.Location));
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		UpdateArrowHot(false);
	}

	protected override void OnDropDown(EventArgs e)
	{
		base.OnDropDown(e);
		BeginDropDownFollow();
		Invalidate();
	}

	protected override void OnDropDownClosed(EventArgs e)
	{
		base.OnDropDownClosed(e);
		EndDropDownFollow();
		Invalidate();
	}

	/// <summary>
	/// 2026-10-09（用户原话「依旧没跟着走」＋「对的 (A) 让列表跟随」）：**展开列表的显式跟随** ——
	/// 让原生列表窗口永远钉在"下拉框本体 + 展开那一刻的相对偏移"上。
	///
	/// 为什么必须显式跟随（实测，不是推测）：下拉列表窗口是原生 `ComboLBox`，**父窗口是桌面**
	/// （`GetParent(list)=0x1000C`=桌面 `#32769`）、**没有 owner**（`GW_OWNER(list)=NULL`）、
	/// `GA_ROOT(list)` 就是它自己 —— 这三点在一颗**完全没被我们碰过的纯 WinForms ComboBox** 上
	/// 实测与"我们的样式（Flat＋OwnerDrawFixed＋DarkMode_CFD＋整块自绘＋ItemHeight）"**逐字相同**
	/// ⇒ 这是 Windows 组合框的原生设计，不是被 `SetWindowTheme`/自绘/owner-draw 破坏的。
	/// 而 owned window 本来就**不随 owner 移动**（owner 只决定 Z 序与销毁），且实测：
	/// ① 移动**祖先容器**（= LM 的滚动机制 `_dashboardStack.Location = -Value`）→ 列表 Δy=0（不跟随）；
	/// ② 移动**下拉框本体**→ 列表 Δy=0（不跟随）。⇒ 原生根本不会自己跟，只能我们跟。
	/// </summary>
	private void BeginDropDownFollow()
	{
		EndDropDownFollow();
		if (!IsHandleCreated)
		{
			return;
		}
		// ⚠️ 2026-10-09（用户原话「直接漂移到左上角了」）：这里**绝不能**记录"列表 − 下拉框"偏移 ——
		// `OnDropDown` 是"下拉**即将**展开"的通知（原生 CBN_DROPDOWN），此刻系统**还没**给列表定位，
		// `GetWindowRect(hwndList)` 拿到的是上一次留下的**停放位置**（实测经常就贴在屏幕原点/旧位置）
		// ⇒ 把这个垃圾差值当偏移，跟随第一拍就会把列表钉到屏幕左上角。
		// 现在改成"延迟采样"：展开消息处理完（系统已摆好列表）后立刻采样一次，并且**只在
		// 列表确实紧贴下拉框时**才认这个偏移（见 SyncDropDownList / IsPlausibleListRect）。
		_dropFollowOffsetKnown = false;
		_dropFollowHostMoved = false;
		_dropFollowAttempts = 0;
		// ① 事件驱动（立即）：LM 的页面滚动就是祖先容器 `LocationChanged`；
		// ② 50ms 定时器兜底：触摸板/键盘翻页/任何我们没挂上事件的移动路径。
		for (Control host = Parent; host != null; host = host.Parent)
		{
			host.LocationChanged += DropDownHostMoved;
			host.Resize += DropDownHostMoved;
			if (host is ScrollableControl scrollable)
			{
				scrollable.Scroll += DropDownHostMoved;
			}
		}
		LocationChanged += DropDownHostMoved;
		_dropFollowHooked = true;
		_dropFollowTimer ??= new System.Windows.Forms.Timer
		{
			Interval = 50
		};
		_dropFollowTimer.Tick -= DropDownFollowTick;
		_dropFollowTimer.Tick += DropDownFollowTick;
		_dropFollowTimer.Start();
		// 展开这条消息处理完之后立刻采样一次（`BeginInvoke` 的回调排在当前消息之后 ⇒ 系统已给列表定位）。
		// 采样那一拍**不移动任何窗口**；采样失败也不会乱动（见 SyncDropDownList）。
		try
		{
			BeginInvoke(new Action(SyncDropDownList));
		}
		catch (InvalidOperationException)
		{
			// 句柄刚好没了：什么都不做（跟随本来就没意义）
		}
	}

	private void EndDropDownFollow()
	{
		if (_dropFollowTimer != null)
		{
			_dropFollowTimer.Stop();
			_dropFollowTimer.Tick -= DropDownFollowTick;
		}
		if (_dropFollowHooked)
		{
			for (Control host = Parent; host != null; host = host.Parent)
			{
				host.LocationChanged -= DropDownHostMoved;
				host.Resize -= DropDownHostMoved;
				if (host is ScrollableControl scrollable)
				{
					scrollable.Scroll -= DropDownHostMoved;
				}
			}
			LocationChanged -= DropDownHostMoved;
			_dropFollowHooked = false;
		}
		_dropFollowOffsetKnown = false;
		_dropFollowHostMoved = false;
		_dropFollowAttempts = 0;
	}

	private void DropDownHostMoved(object? sender, EventArgs e)
	{
		_dropFollowHostMoved = true;
		SyncDropDownList();
	}

	private void DropDownFollowTick(object? sender, EventArgs e)
	{
		SyncDropDownList();
	}

	/// <summary>收起下拉，且**不让收起草身把异常抛回去**（跟随路径上的兜底动作）。</summary>
	private void CloseDropDownQuietly()
	{
		try
		{
			DroppedDown = false;
		}
		catch (Exception)
		{
			// 已经在销毁/句柄失效的路上：忽略（跟随本来也要结束了）
		}
	}

	/// <summary>
	/// 把展开的原生列表窗口重新钉回"下拉框 + 展开后**实测**的相对偏移"（幂等：位置已经正确时一次
	/// P/Invoke 都不发；偏移尚未可信时**一个像素都不动**）。
	///
	/// 2026-10-09（用户原话「直接漂移到左上角了」＋「不合理直接收起」）：⭐ 三条铁律 ——
	/// ① **偏移只在"列表确实紧贴下拉框"时采样**（`IsPlausibleListRect`），采样那一拍不移动窗口；
	/// ② 每次移动前，**算出来的目标矩形**同样要过这个窄口径校验，不通过就**收起**；
	/// ③ 拿不到 `hwndList`/几何、`SetWindowPos` 失败、或过程中抛任何异常 ⇒ **收起**。
	/// ⇒ 结构上不可能出现"把列表钉到屏幕角落"（角落位置与下拉框既无水平交集、纵向也差着整屏）。
	///
	/// 坐标口径：`GetWindowRect` 与 `SetWindowPos` **都是屏幕坐标**；`x/y` 一律由
	/// `下拉框屏幕矩形 + 偏移` 得出，从不使用 `Location`/`PointToClient` 这类父窗口相对值。
	/// </summary>
	private void SyncDropDownList()
	{
		if (!DroppedDown || IsDisposed || !IsHandleCreated)
		{
			EndDropDownFollow();
			return;
		}
		try
		{
			if (!TryGetDropDownGeometry(out nint list, out Rectangle listRect, out Rectangle comboRect))
			{
				// 几何拿不到：展开后极短时间内允许再等（列表窗口刚建/刚销毁的瞬时），
				// 已经采样过（说明确实展开过）或宿主已动过或等太久 ⇒ 收起。
				if (_dropFollowOffsetKnown || _dropFollowHostMoved || ++_dropFollowAttempts > 40)
				{
					CloseDropDownQuietly();
				}
				return;
			}
			if (IsFullyClipped(comboRect))
			{
				CloseDropDownQuietly();
				return;
			}
			if (!_dropFollowOffsetKnown)
			{
				// ⭐ 采样窗口：此刻列表必须已经"紧贴"下拉框（系统摆放实测为 0 间隙；停放位置远在天边）。
				if (!IsPlausibleListRect(listRect, comboRect))
				{
					// 还没摆好 ⇒ 这一拍什么都不做；但若期间宿主已经动过（列表已经被落下）或等太久 ⇒ 收起
					if (_dropFollowHostMoved || ++_dropFollowAttempts > 40)
					{
						CloseDropDownQuietly();
					}
					return;
				}
				_dropFollowOffset = new Point(listRect.Left - comboRect.Left, listRect.Top - comboRect.Top);
				_dropFollowOffsetKnown = true;
				return; // 采样这一拍不移动任何窗口
			}
			int x = comboRect.Left + _dropFollowOffset.X;
			int y = comboRect.Top + _dropFollowOffset.Y;
			if (listRect.Left == x && listRect.Top == y)
			{
				return; // 已经在正确位置（页面没动过）⇒ 零开销
			}
			if (!IsPlausibleListRect(new Rectangle(x, y, listRect.Width, listRect.Height), comboRect))
			{
				// ⭐ 目标不合理（例如算到屏幕角落）⇒ 宁可收起，绝不钉到错位置
				CloseDropDownQuietly();
				return;
			}
			if (!SetWindowPos(list, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder))
			{
				CloseDropDownQuietly();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[combofollow] 跟随展开列表失败（已收起下拉）：" + ex.GetType().Name + " " + ex.Message);
			CloseDropDownQuietly();
		}
	}

	/// <summary>
	/// **窄口径**"合理性"判定（2026-10-09；用户原话「不合理直接收起」，同时要求"正常场景不许被误收起"）：
	///   ① 两个矩形都要有正的宽高；
	///   ② 列表与下拉框**水平必须有交集**（正常摆放：左沿对齐、放宽的 `DropDownWidth` 也仍是左对齐；
	///      系统贴屏边挪动也只是左右微调，仍必然与下拉框有交集）；
	///   ③ 列表**纵向紧贴**下拉框：在下方时 `list.Top - combo.Bottom ∈ [-8, 24]`，翻到上方时
	///      `combo.Top - list.Bottom ∈ [-8, 24]`（实测系统摆放值 = 0，留 24 设备像素余量足够容忍
	///      主题/边框差异，而异常位移的量级是"整格 40~70 设备像素"或"整屏"，必然被挡下）。
	/// 屏幕角落的停放位置（如 (0,0)）与本组合框既无水平交集、纵向也差整屏 ⇒ 判定必然不通过。
	/// ⚠️ 正常场景（列表紧贴下方、页面滚动导致的 Δy 跟随）永远满足 ①②③ ⇒ 不会被误伤收起。
	/// </summary>
	private static bool IsPlausibleListRect(Rectangle listRect, Rectangle comboRect)
	{
		if (listRect.Width <= 0 || listRect.Height <= 0 || comboRect.Width <= 0 || comboRect.Height <= 0)
		{
			return false;
		}
		if (listRect.Left >= comboRect.Right || listRect.Right <= comboRect.Left)
		{
			return false;
		}
		int below = listRect.Top - comboRect.Bottom;
		int above = comboRect.Top - listRect.Bottom;
		return (below >= -8 && below <= 24) || (above >= -8 && above <= 24);
	}

	/// <summary>下拉框（屏幕矩形）是否已被**任何一个祖先的客户区**整块裁掉（＝页面滚过头、根本看不见它了）。</summary>
	private bool IsFullyClipped(Rectangle comboRect)
	{
		Rectangle visible = comboRect;
		for (Control host = Parent; host != null; host = host.Parent)
		{
			if (!host.IsHandleCreated)
			{
				continue;
			}
			visible = Rectangle.Intersect(visible, host.RectangleToScreen(host.ClientRectangle));
			if (visible.Height <= 0 || visible.Width <= 0)
			{
				return true;
			}
		}
		Form form = FindForm();
		if (form != null && form.IsHandleCreated)
		{
			return !form.RectangleToScreen(form.ClientRectangle).IntersectsWith(comboRect);
		}
		return false;
	}

	/// <summary>取原生列表窗口 + 列表与下拉框各自的**屏幕矩形**（<c>GetComboBoxInfo</c> 的 <c>hwndList</c>，只读）。</summary>
	private bool TryGetDropDownGeometry(out nint list, out Rectangle listRect, out Rectangle comboRect)
	{
		list = IntPtr.Zero;
		listRect = Rectangle.Empty;
		comboRect = Rectangle.Empty;
		if (!IsHandleCreated || !TryGetDropDownListHandle(out list))
		{
			return false;
		}
		if (!GetWindowRect(list, out RECT lr) || !GetWindowRect(Handle, out RECT cr))
		{
			return false;
		}
		listRect = new Rectangle(lr.L, lr.T, Math.Max(0, lr.R - lr.L), Math.Max(0, lr.B - lr.T));
		comboRect = new Rectangle(cr.L, cr.T, Math.Max(0, cr.R - cr.L), Math.Max(0, cr.B - cr.T));
		return true;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑥）：统一箭头 —— 两段细线 chevron（宽 8 逻辑像素、高 4、线宽 1.5 逻辑像素），
	/// 与正文同色系（ArrowColor / 失效时 Muted），半像素对齐 + AntiAlias，故不会有实心三角那种硬边。
	/// </summary>
	private void DrawChevron(Graphics g, Rectangle button, Color color)
	{
		// 2026-10-09（0.291 beta22 补丁）：与 PaintSplit 同源 —— 用**控件自己的 DPI**，不用绘制 DC 的
		// `g.DpiX`（后者在 PerMonitorV2 下可能回 96，会把这个 chevron 画小一号，看着像"原生小箭头"）。
		float scale = UiDpi.Paint(this) / 96f;
		int halfW = (int)Math.Round(ChevronHalfWidth * scale);
		int halfH = (int)Math.Round(ChevronHalfHeight * scale);
		float cx = button.Left + button.Width / 2f;
		float cy = button.Top + button.Height / 2f;
		// 线宽按 DPI 取整到 1–2px；半像素对齐（x.5 的厚度为偶数时中心落在整数格）让 chevron 不糊。
		float width = Math.Max(1f, Math.Min(2f, (float)Math.Round(scale)));
		float align = ((int)width % 2 == 0) ? 0f : 0.5f;
		float lx = (float)Math.Round(cx) + align - halfW;
		float mx = (float)Math.Round(cx) + align;
		float rx = mx + halfW;
		float ty = (float)Math.Round(cy) + align - halfH / 2f;
		float by = ty + halfH;
		using Pen pen = new Pen(color, width);
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		pen.LineJoin = LineJoin.Round;
		g.DrawLines(pen, new PointF[3]
		{
			new PointF(lx, ty),
			new PointF(mx, by),
			new PointF(rx, ty)
		});
	}

	[DllImport("user32.dll")]
	private static extern nint BeginPaint(nint hWnd, [In][Out] ref PAINTSTRUCT lpPaint);

	[DllImport("user32.dll")]
	private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

	[DllImport("gdi32.dll")]
	internal static extern bool DeleteObject(nint hObject);

	[DllImport("gdi32.dll")]
	private static extern nint CreateSolidBrush(int color);

	/// <summary>2026-10-09（修列表滚轮错位）：取原生下拉列表窗口句柄（见 <see cref="IsScreenPointOverOpenList" />）。</summary>
	[DllImport("user32.dll")]
	private static extern bool GetComboBoxInfo(nint hWnd, ref COMBOBOXINFO info);

	/// <summary>2026-10-09（修列表滚轮错位）：取原生下拉列表窗口的屏幕矩形。</summary>
	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out RECT rect);

	/// <summary>2026-10-09（用户原话「依旧没跟着走」）：把展开的原生列表窗口挪到指定屏幕位置（只挪位置）。</summary>
	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	/// <summary>2026-10-09（用户原话「依旧没跟着走」）：把落在下拉框本体上的滚轮转投给原生列表。</summary>
	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
