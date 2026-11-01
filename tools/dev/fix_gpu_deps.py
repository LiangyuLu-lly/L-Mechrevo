# -*- coding: utf-8 -*-
"""修复：ToastForm.ShowNotice + DisplayNative.DISPLAYCONFIG_ADAPTER_NAME"""
import re

# ---------- ToastForm ----------
p = r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''	internal static void ShowFailure(string message)
	{
		FailureCallback?.Invoke(message);
		try
		{
			Program.toast?.RunToast(message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failure toast failed: " + ex.Message);
		}
	}'''
new = '''	internal static void ShowFailure(string message)
	{
		FailureCallback?.Invoke(message);
		try
		{
			Program.toast?.RunToast(message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failure toast failed: " + ex.Message);
		}
	}

	internal static void ShowNotice(string message)
	{
		NoticeCallback?.Invoke(message);
		try
		{
			Program.toast?.RunToast(message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Notice toast failed: " + ex.Message);
		}
	}'''
assert old in t and t.count(old) == 1, 'ToastForm anchor'
t = t.replace(old, new, 1)

# 同时加 NoticeCallback 字段（若不存在）
if 'NoticeCallback' not in t:
    old2 = 'internal static event Action<string>? FailureCallback;'
    new2 = 'internal static event Action<string>? FailureCallback;\n\n	internal static event Action<string>? NoticeCallback;'
    if old2 in t:
        t = t.replace(old2, new2, 1)
        print('OK: ToastForm NoticeCallback added')
    else:
        # 找 FailureCallback 声明位置（可能不同写法）
        m = re.search(r'[^\n]*FailureCallback[^\n]*;', t)
        print('FailureCallback decl:', m.group(0) if m else 'NOT FOUND')
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK: ToastForm.ShowNotice')

# ---------- DisplayNative ----------
p2 = r'D:\openLM\source\MechrevoLite.Display\DisplayNative.cs'
d = open(p2, encoding='utf-8', errors='replace').read()
old = '''	public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO'''
new = '''	public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_ADAPTER_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string adapterDevicePath;
	}

	public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO'''
assert old in d and d.count(old) == 1, 'DisplayNative struct anchor'
d = d.replace(old, new, 1)

old = '''	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);'''
new = '''	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_ADAPTER_NAME adapterName);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);'''
assert old in d and d.count(old) == 1, 'DisplayNative p/invoke anchor'
d = d.replace(old, new, 1)
open(p2, 'w', encoding='utf-8', newline='').write(d)
print('OK: DisplayNative ADAPTER_NAME')
print('DONE')
