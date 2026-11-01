using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

internal class User32
{
	public const uint WS_POPUP = 2147483648u;

	public const int WS_EX_TOPMOST = 8;

	public const int WS_EX_TOOLWINDOW = 128;

	public const int WS_EX_LAYERED = 524288;

	public const int WS_EX_TRANSPARENT = 32;

	public const int WS_EX_NOACTIVATE = 134217728;

	public const int SW_SHOWNOACTIVATE = 4;

	public const int SW_HIDE = 0;

	public const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

	private User32()
	{
	}

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern nint GetDC(nint hWnd);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern short GetAsyncKeyState(int virtKey);

	[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
	public static extern bool GetClientRect(nint hWnd, [In][Out] ref RECT rect);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern int GetWindowLong(nint hWnd, int nIndex);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern nint GetWindow(nint hWnd, int cmd);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern bool GetWindowRect(nint hWnd, ref RECT rect);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern bool PostMessage(nint hWnd, int Msg, uint wParam, uint lParam);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern bool ReleaseCapture();

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern int SetWindowLong(nint hWnd, int nIndex, int newLong);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern int SetWindowPos(nint hWnd, nint hWndAfter, int X, int Y, int Width, int Height, uint flags);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern bool SetCapture(nint hWnd);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern int ShowWindow(nint hWnd, short cmdShow);

	[DllImport("User32.dll", CharSet = CharSet.Auto)]
	internal static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
}
