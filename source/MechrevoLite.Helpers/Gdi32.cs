using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

internal class Gdi32
{
	private Gdi32()
	{
	}

	[DllImport("gdi32.dll", CharSet = CharSet.Auto)]
	internal static extern nint CreateCompatibleDC(nint hDC);

	[DllImport("gdi32.dll", CharSet = CharSet.Auto)]
	internal static extern bool DeleteDC(nint hDC);

	[DllImport("gdi32.dll", CharSet = CharSet.Auto)]
	internal static extern nint DeleteObject(nint hObject);

	[DllImport("gdi32.dll", CharSet = CharSet.Auto)]
	internal static extern int SelectClipRgn(nint hDC, nint hRgn);

	[DllImport("gdi32.dll", CharSet = CharSet.Auto)]
	internal static extern nint SelectObject(nint hDC, nint hObject);
}
