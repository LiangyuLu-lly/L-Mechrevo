using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal class CustomContextMenu : ContextMenuStrip
{
	public enum DWMWINDOWATTRIBUTE
	{
		DWMWA_WINDOW_CORNER_PREFERENCE = 33
	}

	public enum DWM_WINDOW_CORNER_PREFERENCE
	{
		DWMWA_DEFAULT,
		DWMWCP_DONOTROUND,
		DWMWCP_ROUND,
		DWMWCP_ROUNDSMALL
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 8;
			return createParams;
		}
	}

	[DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern long DwmSetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

	public CustomContextMenu()
	{
		try
		{
			DWM_WINDOW_CORNER_PREFERENCE pvAttribute = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUNDSMALL;
			DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref pvAttribute, 4u);
		}
		catch
		{
		}
	}
}
