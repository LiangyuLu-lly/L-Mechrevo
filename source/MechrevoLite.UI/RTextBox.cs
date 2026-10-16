using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MechrevoLite.UI;

public class RTextBox : TextBox
{
	private const int WM_PAINT = 15;

	private string _placeholder = "";

	public new string PlaceholderText
	{
		get
		{
			return _placeholder;
		}
		set
		{
			_placeholder = value ?? "";
			Invalidate();
		}
	}

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

	public RTextBox()
	{
		BorderStyle = BorderStyle.FixedSingle;
	}

	public void ApplyTheme(bool dark)
	{
		BackColor = (dark ? RForm.buttonMain : SystemColors.Window);
		ForeColor = (dark ? RForm.foreMain : SystemColors.WindowText);
		SetWindowTheme(Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
	}

	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);
		if (m.Msg == 15 && !Focused && string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(_placeholder))
		{
			using (Graphics dc = Graphics.FromHwnd(Handle))
			{
				Color foreColor = Color.FromArgb((BackColor.R + ForeColor.R) / 2, (BackColor.G + ForeColor.G) / 2, (BackColor.B + ForeColor.B) / 2);
				Rectangle clientRectangle = ClientRectangle;
				clientRectangle.X++;
				TextRenderer.DrawText(dc, _placeholder, Font, clientRectangle, foreColor, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}
		// 2026-10-09（UI 打磨②，仅夜间）：1px 圆角描边（UiVisualStyle.BorderStrong）。
		// 夜间在 UiVisualStyle.ApplyTree 里本控件 BorderStyle 已被置为 None，所以这一圈就是它唯一的边框；
		// 日间 BorderStyle 仍是 FixedSingle 且这里直接跳过，像素与改动前完全相同。
		// ⚠️ 只在客户区描 1px 线：不改 Bounds/尺寸/布局/命中区域，也不动输入、光标、占位符、ApplyTheme 逻辑。
		if (m.Msg == WM_PAINT && UiVisualStyle.IsNightMode && Width > 4 && Height > 4)
		{
			using Graphics dc = Graphics.FromHwnd(Handle);
			UiVisualStyle.DrawInputOutline(dc, new Rectangle(0, 0, Width - 1, Height - 1), UiVisualStyle.BorderStrong, this);
		}
	}
}
