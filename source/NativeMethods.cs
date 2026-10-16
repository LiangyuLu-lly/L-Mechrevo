using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
	internal struct LASTINPUTINFO
	{
		public uint cbSize;

		public uint dwTime;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	internal struct POWERBROADCAST_SETTING
	{
		public Guid PowerSetting;

		public uint DataLength;

		public byte Data;
	}

	public class PowerSettingGuid
	{
		public static Guid LIDSWITCH_STATE_CHANGE = new Guid("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");

		public static Guid ConsoleDisplayState { get; } = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

		public static Guid EnergySaverStatus { get; } = new Guid("550E8400-E29B-41D4-A716-446655440000");
	}

	internal const int FreshInputWindowMs = 500;

	internal const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0u;

	internal const int WM_POWERBROADCAST = 536;

	internal const int PBT_POWERSETTINGCHANGE = 32787;

	internal const int PBT_APMSUSPEND = 4;

	internal const int PBT_APMRESUMEAUTOMATIC = 18;

	internal static Func<TimeSpan>? IdleTimeProvider { get; set; }

	[DllImport("User32.dll")]
	private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

	[DllImport("user32.dll")]
	public static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	public static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll")]
	public static extern bool GetDeviceGammaRamp(nint hDC, nint lpRamp);

	public static string GetGammaRampHash()
	{
		nint dC = GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			return "fail";
		}
		nint num = Marshal.AllocHGlobal(1536);
		try
		{
			if (!GetDeviceGammaRamp(dC, num))
			{
				return "fail";
			}
			long num2 = 0L;
			for (int i = 0; i < 768; i += 8)
			{
				ushort num3 = (ushort)Marshal.ReadInt16(num, i * 2);
				num2 = (num2 * 31 + num3) % 1000000007;
			}
			return num2.ToString();
		}
		finally
		{
			Marshal.FreeHGlobal(num);
			ReleaseDC(IntPtr.Zero, dC);
		}
	}

	public static TimeSpan GetIdleTime()
	{
		LASTINPUTINFO plii = default;
		plii.cbSize = (uint)Marshal.SizeOf(plii);
		GetLastInputInfo(ref plii);
		return TimeSpan.FromMilliseconds((uint)(Environment.TickCount - (int)plii.dwTime));
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern int RegisterWindowMessage(string lpString);

	public static bool HasFreshUserInput(int windowMs = 500)
	{
		TimeSpan timeSpan = IdleTimeProvider?.Invoke() ?? GetIdleTime();
		if (timeSpan >= TimeSpan.Zero)
		{
			return timeSpan <= TimeSpan.FromMilliseconds(windowMs);
		}
		return false;
	}

	[DllImport("User32.dll", SetLastError = true)]
	internal static extern nint RegisterPowerSettingNotification(nint hWnd, [In] Guid PowerSettingGuid, uint Flags);

	[DllImport("User32.dll", SetLastError = true)]
	internal static extern bool UnregisterPowerSettingNotification(nint hWnd);

	[DllImport("User32.dll", SetLastError = true)]
	internal static extern nint RegisterSuspendResumeNotification(nint hRecipient, uint Flags);

	[DllImport("User32.dll", SetLastError = true)]
	internal static extern bool UnregisterSuspendResumeNotification(nint handle);
}
