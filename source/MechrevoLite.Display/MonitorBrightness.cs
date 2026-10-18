// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
//   **只读备选来源** —— Win32 `dxva2.dll` 的 `GetMonitorBrightness`。
//
// ⭐ 为什么需要它：首选来源是 WMI `root\wmi:WmiMonitorBrightness.CurrentBrightness`
//    （见 `ScreenBrightness.TryGet`，笔记本内屏通用）；个别驱动/机型上 WMI 读不到、
//    但 DDC/CI 亮度接口可用 ⇒ 用它补一条只读路径（读不到就是 false，⛔ 不猜）。
//
// ⭐ 诚实边界：
//   · 本类**只读**：⛔ 不调用 `SetMonitorBrightness`、⛔ 不写注册表/EC/驱动；
//   · 只作"显示系统真实亮度"用 —— 写入永远只走现有 `ScreenBrightness.Set`（WMI），
//     ⛔ 不会因为本类可读就改用别的写入通道；
//   · 无内屏 / 外接屏 / 驱动不支持时 `GetMonitorBrightness` 直接返回 false ⇒ 调用方按"读不到"处理。
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
