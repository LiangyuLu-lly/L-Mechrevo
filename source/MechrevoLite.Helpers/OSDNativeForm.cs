using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace MechrevoLite.Helpers;

public class OSDNativeForm : NativeWindow, IDisposable
{
	private bool _disposed;

	private bool _topMost;

	private byte _alpha = 250;

	private Size _size = new Size(350, 50);

	private Point _location = new Point(50, 50);

	private readonly object _paintLock = new object();

	protected object PaintSync => _paintLock;

	public virtual Point Location
	{
		get
		{
			return _location;
		}
		set
		{
			if (Handle != IntPtr.Zero)
			{
				SetBoundsCore(value.X, value.Y, _size.Width, _size.Height);
				RECT rect = default;
				User32.GetWindowRect(Handle, ref rect);
				_location = new Point(rect.left, rect.top);
				UpdateLayeredWindow();
			}
			else
			{
				_location = value;
			}
		}
	}

	public virtual Size Size
	{
		get
		{
			return _size;
		}
		set
		{
			if (Handle != IntPtr.Zero)
			{
				SetBoundsCore(_location.X, _location.Y, value.Width, value.Height);
				RECT rect = default;
				User32.GetWindowRect(Handle, ref rect);
				_size = new Size(rect.right - rect.left, rect.bottom - rect.top);
				UpdateLayeredWindow();
			}
			else
			{
				_size = value;
			}
		}
	}

	public int Height
	{
		get
		{
			return _size.Height;
		}
		set
		{
			_size = new Size(_size.Width, value);
		}
	}

	public int Width
	{
		get
		{
			return _size.Width;
		}
		set
		{
			_size = new Size(value, _size.Height);
		}
	}

	public int X
	{
		get
		{
			return _location.X;
		}
		set
		{
			Location = new Point(value, Location.Y);
		}
	}

	public int Y
	{
		get
		{
			return _location.Y;
		}
		set
		{
			Location = new Point(Location.X, value);
		}
	}

	public Rectangle Bound => new Rectangle(new Point(0, 0), _size);

	public byte Alpha
	{
		get
		{
			return _alpha;
		}
		set
		{
			if (_alpha != value)
			{
				_alpha = value;
				UpdateLayeredWindow();
			}
		}
	}

	// ⭐⭐ 2026-10-18（机主反馈「悬浮窗没有置顶、会被其他窗口盖住」）：⭐ 显式置顶用的 Win32 常量 ——
	//   ⚠️ 本类是 `NativeWindow`（⛔ 不是 `Form`）⇒ 没有 `Form.TopMost` 可用 ⚠️，必须自己调 `SetWindowPos` ✅。
	private static readonly nint HWND_TOPMOST = new IntPtr(-1);

	private static readonly nint HWND_NOTOPMOST = new IntPtr(-2);

	private const uint SWP_NOSIZE = 1u;

	private const uint SWP_NOMOVE = 2u;

	private const uint SWP_NOACTIVATE = 16u;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主反馈「悬浮窗没有置顶、会被其他窗口盖住」）：⭐ <b>窗口是否置顶</b> ✅
	/// <para>⚠️ <b>为什么必须显式做</b>：⭐ 本窗口是 <see cref="NativeWindow" />（⛔ 不是 <c>Form</c>）⇒
	/// ⚠️ 没有 <c>Form.TopMost</c> 可用；⭐ 而 <c>CreateParams.ExStyle</c> 里虽然带了 <c>WS_EX_TOPMOST</c> 位，
	/// ⭐ 那个位**不是可靠的创建入参开关**（⭐ 真正的置顶要由 <c>SetWindowPos(HWND_TOPMOST)</c> 施加 ✅）
	/// ⇒ ⭐ 以前只在那个位"丢失"时才补 ⚠️ ⇒ ⭐ 它从来没丢过、也就**从来没真正置顶过** ❌（= 机主看到的现象）。</para>
	/// <para>⭐ 现在的口径：⭐ ① 赋值时立刻 <c>SetWindowPos</c>；② <see cref="Show" />（含从托盘重新打开）后再补一次；
	/// ③ 悬浮窗每帧还会调 <see cref="AssertTopMost" /> 重申 ✅。</para>
	/// <para>⚠️ 所有置顶动作都带 <c>SWP_NOACTIVATE</c> ＋ 窗口本身有 <c>WS_EX_NOACTIVATE</c>
	/// ⇒ ⛔ **不抢焦点**（⭐ 与既有"OSD 不抢焦点"的口径一致 ✅）。</para>
	/// </summary>
	public bool TopMost
	{
		get
		{
			return _topMost;
		}
		set
		{
			_topMost = value;
			if (Handle != IntPtr.Zero)
			{
				ApplyTopMost();
			}
		}
	}

	/// <summary>
	/// ⭐ 2026-10-18：⭐ <b>把窗口重新放回最上层</b> ✅（⭐ 只在 <see cref="TopMost" /> 为真时动作）。
	/// <para>⚠️ 三个 flag：<c>SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE</c> —— ⭐ 不改大小/位置、⛔ 不抢焦点 ✅。</para>
	/// </summary>
	internal void AssertTopMost()
	{
		if (_topMost && Handle != IntPtr.Zero)
		{
			ApplyTopMost();
		}
	}

	private void ApplyTopMost()
	{
		User32.SetWindowPos(Handle, _topMost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
	}

	protected virtual void PerformPaint(PaintEventArgs e)
	{
	}

	protected internal void Invalidate()
	{
		UpdateLayeredWindow();
	}

	private void UpdateLayeredWindow()
	{
		if (!Monitor.TryEnter(_paintLock))
		{
			return;
		}
		try
		{
			using Bitmap bitmap = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppArgb);
			using Graphics graphics = Graphics.FromImage(bitmap);
			Rectangle clipRect = new Rectangle(0, 0, Size.Width, Size.Height);
			using (PaintEventArgs e = new PaintEventArgs(graphics, clipRect))
			{
				PerformPaint(e);
			}
			nint num = IntPtr.Zero;
			nint num2 = IntPtr.Zero;
			nint num3 = IntPtr.Zero;
			nint num4 = IntPtr.Zero;
			try
			{
				num = User32.GetDC(IntPtr.Zero);
				if (num != IntPtr.Zero)
				{
					num2 = Gdi32.CreateCompatibleDC(num);
					if (num2 != IntPtr.Zero)
					{
						num3 = bitmap.GetHbitmap(Color.FromArgb(0));
						num4 = Gdi32.SelectObject(num2, num3);
						SIZE psize = default;
						psize.cx = Size.Width;
						psize.cy = Size.Height;
						POINT pptDst = default;
						pptDst.x = Location.X;
						pptDst.y = Location.Y;
						POINT pprSrc = default;
						pprSrc.x = 0;
						pprSrc.y = 0;
						BLENDFUNCTION pblend = new BLENDFUNCTION
						{
							BlendOp = 0,
							BlendFlags = 0,
							SourceConstantAlpha = _alpha,
							AlphaFormat = 1
						};
						User32.UpdateLayeredWindow(Handle, num, ref pptDst, ref psize, num2, ref pprSrc, 0, ref pblend, 2);
					}
				}
			}
			finally
			{
				if (num4 != IntPtr.Zero && num2 != IntPtr.Zero)
				{
					Gdi32.SelectObject(num2, num4);
				}
				if (num3 != IntPtr.Zero)
				{
					Gdi32.DeleteObject(num3);
				}
				if (num2 != IntPtr.Zero)
				{
					Gdi32.DeleteDC(num2);
				}
				if (num != IntPtr.Zero)
				{
					User32.ReleaseDC(IntPtr.Zero, num);
				}
			}
		}
		finally
		{
			Monitor.Exit(_paintLock);
		}
	}

	public virtual void Show()
	{
		if (Handle == IntPtr.Zero)
		{
			CreateWindowOnly();
		}
		User32.ShowWindow(Handle, 4);
		// ⭐ 2026-10-18（机主：悬浮窗没有置顶）：⭐ 从托盘重新打开时（⚠️ `Hide()` 会销毁句柄 ⇒ 这里重建 ✅）
		//   再补一次置顶 —— ⛔ 仍是 SW_SHOWNOACTIVATE＋SWP_NOACTIVATE，不抢焦点 ✅
		AssertTopMost();
	}

	public virtual void Hide()
	{
		if (Handle != IntPtr.Zero)
		{
			User32.ShowWindow(Handle, 0);
			DestroyHandle();
		}
	}

	public virtual void Close()
	{
		Hide();
		Dispose();
	}

	private void CreateWindowOnly()
	{
		CreateParams createParams = new CreateParams();
		createParams.Caption = "FloatingNativeWindow";
		int num = _location.X;
		int num2 = _location.Y;
		Screen screen = Screen.FromHandle(Handle);
		if (num + _size.Width > screen.Bounds.Width)
		{
			num = screen.Bounds.Width - _size.Width;
		}
		if (num2 + _size.Height > screen.Bounds.Height)
		{
			num2 = screen.Bounds.Height - _size.Height;
		}
		_location = new Point(num, num2);
		Size size = _size;
		_ = _location;
		createParams.X = num;
		createParams.Y = num2;
		createParams.Height = size.Height;
		createParams.Width = size.Width;
		createParams.Parent = IntPtr.Zero;
		uint style = 2147483648u;
		createParams.Style = (int)style;
		createParams.ExStyle = 134742184;
		CreateHandle(createParams);
		// ⭐ 2026-10-18（机主：悬浮窗没有置顶）：⭐ 窗口一建好就**真正**调 SetWindowPos(HWND_TOPMOST) ✅
		//   ⚠️ 光靠上面 CreateParams.ExStyle 里的 WS_EX_TOPMOST 位**不可靠** ⚠️（⭐ 那个位创建时就在、
		//   但 z-order 并不会因此进入置顶带 ⇒ 机主看到"会被其他窗口盖住" ❌）。
		if (_topMost)
		{
			ApplyTopMost();
		}
		UpdateLayeredWindow();
	}

	protected virtual void SetBoundsCore(int x, int y, int width, int height)
	{
		if (X == x && Y == y && Width == width && Height == height)
		{
			return;
		}
		if (Handle != IntPtr.Zero)
		{
			int num = 20;
			if (X == x && Y == y)
			{
				num |= 2;
			}
			if (Width == width && Height == height)
			{
				num |= 1;
			}
			User32.SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, (uint)num);
		}
		else
		{
			Location = new Point(x, y);
			Size = new Size(width, height);
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposed)
		{
			DestroyHandle();
			_disposed = true;
		}
	}
}
