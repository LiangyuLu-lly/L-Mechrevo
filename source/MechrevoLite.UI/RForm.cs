using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RForm : Form
{
	private Size _lastClientSize;

	public static Color colorEco = Color.FromArgb(255, 6, 180, 138);

	public static Color colorStandard = Color.FromArgb(255, 58, 174, 239);

	public static Color colorTurbo = Color.FromArgb(255, 255, 32, 32);

	public static Color colorCustom = Color.FromArgb(255, 255, 128, 0);

	public static Color colorGray = Color.FromArgb(255, 168, 168, 168);

	public static Color buttonMain;

	public static Color buttonSecond;

	public static Color formBack;

	public static Color foreMain;

	public static Color borderMain;

	public static Color borderSecond;

	public static Color chartMain;

	public static Color chartGrid;

	public bool darkTheme;

	private bool themeInitialized;

	internal float AuditLayoutScale { get; set; }

	protected float EffectiveLayoutScale
	{
		get
		{
			if (!(AuditLayoutScale > 0f))
			{
				return UiDpi.LayoutScale(this);
			}
			return AuditLayoutScale;
		}
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.Style |= 33554432;
			createParams.ClassStyle &= -131073;
			return createParams;
		}
	}

	protected RForm()
	{
		AutoScaleMode = AutoScaleMode.Dpi;
		AutoScaleDimensions = new SizeF(96f, 96f);
		Font = UiVisualStyle.Font(9f);
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		ResizeRedraw = false;
	}

	// 2026-10-22（主题自定义）：窗体句柄建好后应用「强调色 + 背景色/背景图」。
	// 2026-10-22（主题背景图）：背景图按 cover 铺满整窗（上下/左右不留空），不再用 WinForms 的 Zoom。
	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (UiVisualStyle.PaintFormBackground(e.Graphics, this))
		{
			return;
		}
		base.OnPaintBackground(e);
	}

	/// <summary>2026-10-22（机主：背景图只给主 UI）：只有主窗口显式打开，其它窗体（设置页/子窗口/浮动窗）不铺背景图。</summary>
	public bool AllowBackgroundImage { get; set; }

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (AllowBackgroundImage)
		{
			UiVisualStyle.ApplyFormBackground(this);
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (!(ClientSize == _lastClientSize))
		{
			_lastClientSize = ClientSize;
			Invalidate();
		}
	}

	[DllImport("UXTheme.dll", EntryPoint = "#138", SetLastError = true)]
	public static extern bool CheckSystemDarkModeStatus();

	[DllImport("UXTheme.dll", EntryPoint = "#135", SetLastError = true)]
	private static extern int SetPreferredAppMode(int preferredAppMode);

	[DllImport("UXTheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

	[DllImport("DwmApi")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, int[] attrValue, int attrSize);

	public static void InitColors(bool darkTheme)
	{
		if (darkTheme)
		{
			// 2026-10-09（UI 打磨⑤，仅夜间）：所有 RButton 的默认底（ControlHelper.AdjustControls 下发）
			// 由 SurfaceRaised 换成新的按钮面 ButtonFace —— 夜间 44/60/86（比卡片与页面底都更浅，浮起），
			// 而日间的 ButtonFace 与 SurfaceRaised 同值（233/239/246），故日间逐像素不变。
			buttonMain = UiVisualStyle.ButtonFace;
			buttonSecond = UiVisualStyle.Surface;
			formBack = UiVisualStyle.Window;
			foreMain = UiVisualStyle.Text;
			borderMain = UiVisualStyle.Border;
			borderSecond = Color.FromArgb(44, 50, 56);
			chartMain = UiVisualStyle.Surface;
			chartGrid = Color.FromArgb(58, 66, 74);
		}
		else
		{
			// 2026-10-09（UI 打磨⑤）：日间 ButtonFace 与 SurfaceRaised 同值（233/239/246）⇒ 本行像素不变。
			buttonMain = UiVisualStyle.ButtonFace;
			buttonSecond = UiVisualStyle.Surface;
			formBack = UiVisualStyle.Window;
			foreMain = UiVisualStyle.Text;
			borderMain = UiVisualStyle.Border;
			borderSecond = Color.FromArgb(196, 206, 216);
			chartMain = UiVisualStyle.Surface;
			chartGrid = Color.FromArgb(202, 212, 221);
		}
	}

	private static bool IsDarkTheme()
	{
		return UiVisualStyle.IsNightMode;
	}

	public bool InitTheme(bool setDPI = false)
	{
		bool flag = IsDarkTheme();
		bool flag2 = darkTheme != flag;
		bool flag3 = !themeInitialized;
		darkTheme = flag;
		themeInitialized = true;
		InitColors(darkTheme);
		if (setDPI)
		{
			ControlHelper.Resize(this);
		}
		if (flag2 | flag3)
		{
			DwmSetWindowAttribute(Handle, 20, new int[1] { darkTheme ? 1 : 0 }, 4);
			SetPreferredAppMode(darkTheme ? 1 : 0);
			SetWindowTheme(Handle, darkTheme ? "DarkMode_Explorer" : "Explorer", null);
			ControlHelper.Adjust(this, flag2);
			Invalidate();
		}
		return flag2;
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：**所有窗体的统一下拉配置入口**（见
	/// <see cref="UiVisualStyle.StyleAllCombos" />）。放在 OnLoad：此刻本窗体的整棵控件树都已经建好
	/// （包括构造末尾才 Add 的那些行），于是 SettingsDialog（「语言」「日志级别」）、RgbForm（键盘灯效
	/// 后建的那几颗）、LightForm、CustomModeForm、FanCurveForm 等**没有显式走 ApplyWindow** 的窗体，
	/// 它们里面的下拉也一定能拿到统一样式 —— 不再依赖"首绘时在 WM_PAINT 里自愈"那条会重建 HWND、
	/// 把我们的圆角整块裁掉的老路。
	/// ⚠️ 只改外观属性，不碰 Items / SelectedIndex / 事件 / 尺寸 / 位置。
	/// </summary>
	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		UiVisualStyle.StyleAllCombos(this);
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		// 2026-10-09（0.291 beta22 补丁）：再补一次 —— 覆盖"OnLoad 之后才创建"的动态下拉
		//（例如按硬件能力/配置现建的行）。本方法是幂等的：已上过色的下拉在 StyleComboBox 里是空操作。
		UiVisualStyle.StyleAllCombos(this);
		ApplyResponsiveBounds();
		if (AllowBackgroundImage)
		{
			// 2026-10-22（机主：每次重启背景不刷新、被黑色挡住）：启动期句柄创建时套背景会被后续初始化覆盖，
			// 这里在窗口真正显示、布局完成后再套，并用一次短延迟定时器兜底（等主题/布局全部落定）。
			UiVisualStyle.ApplyFormBackground(this);
			BeginInvoke((Action)(() =>
			{
				if (!IsDisposed && AllowBackgroundImage)
				{
					UiVisualStyle.ApplyFormBackground(this);
					Invalidate(invalidateChildren: true);
				}
			}));
			Timer startupBackgroundTimer = new Timer
			{
				Interval = 500
			};
			startupBackgroundTimer.Tick += (object sender, EventArgs args) =>
			{
				startupBackgroundTimer.Stop();
				startupBackgroundTimer.Dispose();
				if (!IsDisposed && AllowBackgroundImage)
				{
					UiVisualStyle.ApplyFormBackground(this);
					Invalidate(invalidateChildren: true);
				}
			};
			startupBackgroundTimer.Start();
		}
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		if (IsHandleCreated && !IsDisposed)
		{
			BeginInvoke(CreateResponsiveBoundsCallback(this));
		}
	}

	internal static MethodInvoker CreateResponsiveBoundsCallback(RForm form)
	{
		return () =>
		{
			form.ApplyResponsiveBounds();
		};
	}

	public virtual void ApplyResponsiveBounds(Rectangle? workingArea = null)
	{
		ResponsiveLayout.ConstrainToWorkingArea(this, workingArea);
	}
}
