using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Threading;

namespace MechrevoLite.Helpers;

internal static class ShellPersonalization
{
	private struct APPBARDATA
	{
		public uint cbSize;

		public nint hWnd;

		public uint uCallbackMessage;

		public uint uEdge;

		public RECT rc;

		public nint lParam;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private const string PersonalizeKey = "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";

	private const uint ABM_GETSTATE = 4u;

	private const uint ABM_SETSTATE = 10u;

	private const int ABS_AUTOHIDE = 1;

	private const int ABS_ALWAYSONTOP = 2;

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint FindWindow(string? className, string? windowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

	private static APPBARDATA NewAppBarData()
	{
		return new APPBARDATA
		{
			cbSize = (uint)Marshal.SizeOf<APPBARDATA>()
		};
	}

	private static int GetTaskbarState()
	{
		APPBARDATA pData = NewAppBarData();
		return (int)SHAppBarMessage(4u, ref pData);
	}

	public static bool? IsTaskbarAutoHide()
	{
		try
		{
			return (GetTaskbarState() & 1) != 0;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Taskbar auto-hide read failed: " + ex.Message);
			return null;
		}
	}

	public static bool SetTaskbarAutoHide(bool autoHide)
	{
		try
		{
			int taskbarState = GetTaskbarState();
			int num = (autoHide ? (taskbarState | 1) : (taskbarState & -2));
			if (num == taskbarState)
			{
				return true;
			}
			APPBARDATA pData = NewAppBarData();
			pData.hWnd = FindWindow("Shell_TrayWnd", null);
			pData.lParam = num;
			SHAppBarMessage(10u, ref pData);
			Thread.Sleep(120);
			bool flag = (GetTaskbarState() & 1) != 0 == autoHide;
			if (!flag)
			{
				// Windows 11 有时第一次 ABM_SETSTATE 只改状态不重排任务栏，补发一次。
				SHAppBarMessage(10u, ref pData);
				Thread.Sleep(180);
				flag = (GetTaskbarState() & 1) != 0 == autoHide;
			}
			if (!flag)
			{
				// Windows 11 某些环境（尤其进程已提权）SHAppBarMessage 不生效；
				// 回退写 Explorer\StuckRects3 的 Settings[8] bit0（=ABS_AUTOHIDE），再广播设置变更。
				flag = TryWriteTaskbarAutoHideRegistry(autoHide);
			}
			if (!flag)
			{
				Logger.WriteError($"Taskbar auto-hide not confirmed: requested {autoHide}.");
			}
			else
			{
				Logger.WriteError("Taskbar auto-hide confirmed: " + autoHide);
			}
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Taskbar auto-hide write failed: " + ex.Message);
			return false;
		}
	}

	private static bool TryWriteTaskbarAutoHideRegistry(bool autoHide)
	{
		try
		{
			const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
			object? raw = Registry.CurrentUser.OpenSubKey(path)?.GetValue("Settings");
			if (raw is not byte[] settings || settings.Length <= 8)
			{
				Logger.WriteError("[taskbar] StuckRects3 Settings 不可读");
				return false;
			}
			byte before = settings[8];
			settings[8] = autoHide ? (byte)(before | 0x01) : (byte)(before & ~0x01);
			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
			{
				key?.SetValue("Settings", settings, RegistryValueKind.Binary);
			}
			// 通知 Explorer 重新读取任务栏设置。
			SendMessageTimeout(65535, 26u, 0, "TraySettings", 2u, 100u, out var _);
			Thread.Sleep(120);
			bool now = (GetTaskbarState() & 1) != 0;
			Logger.WriteError($"[taskbar] StuckRects3 bit0 {before:X2} -> {settings[8]:X2} · 回读={now} · 请求={autoHide}");
			return now == autoHide;
		}
		catch (Exception ex)
		{
			Logger.WriteError("[taskbar] StuckRects3 回退失败：" + ex.GetType().Name + " " + ex.Message);
			return false;
		}
	}

	internal static bool IsTaskbarAlwaysOnTop()
	{
		return (GetTaskbarState() & 2) != 0;
	}

	private static int? RegistryGet(string name)
	{
		try
		{
			return Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", name, null) as int?;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Personalize read failed (" + name + "): " + ex.Message);
			return null;
		}
	}

	private static bool RegistrySet(string name, int value)
	{
		try
		{
			Registry.SetValue("HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", name, value, RegistryValueKind.DWord);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Personalize write failed (" + name + "): " + ex.Message);
			return false;
		}
	}

	private static void BroadcastImmersiveColorSet()
	{
		try
		{
			SendMessageTimeout(65535, 26u, 0, "ImmersiveColorSet", 2u, 100u, out var _);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ImmersiveColorSet broadcast failed: " + ex.Message);
		}
	}

	public static bool? IsTransparencyEnabled()
	{
		int? num = RegistryGet("EnableTransparency");
		if (num.HasValue)
		{
			if (num.GetValueOrDefault() == 0)
			{
				return false;
			}
			return true;
		}
		return null;
	}

	public static bool SetTransparencyEnabled(bool enabled)
	{
		if (!RegistrySet("EnableTransparency", enabled ? 1 : 0))
		{
			return false;
		}
		BroadcastImmersiveColorSet();
		return IsTransparencyEnabled() == enabled;
	}

	public static bool? IsDarkTheme()
	{
		int? num = RegistryGet("AppsUseLightTheme");
		if (num.HasValue)
		{
			if (num.GetValueOrDefault() == 0)
			{
				return true;
			}
			return false;
		}
		return null;
	}

	public static bool SetDarkTheme(bool dark)
	{
		int value = ((!dark) ? 1 : 0);
		if (!(RegistrySet("AppsUseLightTheme", value) & RegistrySet("SystemUsesLightTheme", value)))
		{
			return false;
		}
		BroadcastImmersiveColorSet();
		return IsDarkTheme() == dark;
	}
}
