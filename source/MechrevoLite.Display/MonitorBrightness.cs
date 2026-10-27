// （机主：启动时屏幕亮度滑块没有读取一次系统真实状态）
using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Display;

/// <summary>
/// Win32 DDC/CI 亮度**只读**封装（`dxva2.dll` · `GetMonitorBrightness`）。
/// <para>⚠️ 仅作备选读取源；写亮度仍只走 WMI（<see cref="ScreenBrightness.Set" />）。</para>
/// </summary>
internal static class MonitorBrightness
{
	private const uint MonitorDefaultToPrimary = 1u;

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint minimum, out uint current, out uint maximum);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

	/// <summary>
	/// 读主显示器亮度（百分比 0~100）。⭐ 失败返回 false + <paramref name="reason" />（用于日志）。
	/// <para>⚠️ 只读，⛔ 不写任何东西。</para>
	/// </summary>
	internal static bool TryGet(out int percent, out string reason)
	{
		percent = -1;
		reason = string.Empty;
		try
		{
			IntPtr monitor = MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
			if (monitor == IntPtr.Zero)
			{
				reason = "dxva2：取不到主显示器句柄";
				return false;
			}
			if (!GetMonitorBrightness(monitor, out uint minimum, out uint current, out uint maximum))
			{
				reason = "dxva2 GetMonitorBrightness 失败（err=" + Marshal.GetLastWin32Error() + "）";
				return false;
			}
			if (maximum <= minimum)
			{
				reason = "dxva2 返回无效亮度范围（min=" + minimum + " max=" + maximum + "）";
				return false;
			}
			long pct = ((long)current - minimum) * 100L / ((long)maximum - minimum);
			percent = (int)Math.Clamp(pct, 0L, 100L);
			return true;
		}
		catch (Exception ex)
		{
			percent = -1;
			reason = "dxva2 异常：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}
}
