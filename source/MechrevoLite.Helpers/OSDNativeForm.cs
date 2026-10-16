using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace MechrevoLite.Helpers;

public class OSDNativeForm : NativeWindow, IDisposable
{
	private bool _disposed;

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
