using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MechrevoLite.Helpers;

namespace L_Mechrevo.WpfP0;

// OSD 交互参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>
/// NUC 没有独立数字小键盘：Fn+SCRLK 打开 ScrollLock 后，
/// 用主键盘 m/j/k/l/u/i/o/7/8/9 模拟小键盘 0..9。
/// 只在 ScrollLock 开启时生效，不改变普通键盘输入。
/// </summary>
internal static class NumpadOverlayService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyeventfKeyup = 0x0002;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static readonly Dictionary<int, int> Map = new()
    {
        [0x4D] = 0x60, // M -> Num0
        [0x4A] = 0x61, // J -> Num1
        [0x4B] = 0x62, // K -> Num2
        [0x4C] = 0x63, // L -> Num3
        [0x55] = 0x64, // U -> Num4
        [0x49] = 0x65, // I -> Num5
        [0x4F] = 0x66, // O -> Num6
        [0x37] = 0x67, // 7 -> Num7
        [0x38] = 0x68, // 8 -> Num8
        [0x39] = 0x69  // 9 -> Num9
    };

    private static LowLevelKeyboardProc? _proc;
    private static IntPtr _hook;
    private static int _started;

    internal static void Start()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }
        try
        {
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Logger.WriteError("[numpad-overlay] 键盘钩子安装失败：" + Marshal.GetLastWin32Error());
            }
            else
            {
                Logger.WriteError("[numpad-overlay] 已启动：ScrollLock 开启时 m/j/k/l/u/i/o/7/8/9 → 小键盘 0..9");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteError("[numpad-overlay] 启动失败：" + ex.Message);
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                KbdLlHookStruct info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                bool injected = (info.Flags & LlkhfInjected) != 0;
                int message = wParam.ToInt32();
                bool down = message == WmKeyDown || message == WmSysKeyDown;
                bool up = message == WmKeyUp || message == WmSysKeyUp;
                if (!injected && (down || up) && Map.TryGetValue((int)info.VkCode, out int numpadVk)
                    && NativeLockKey.IsScrollLockOn())
                {
                    keybd_event((byte)numpadVk, 0, up ? KeyeventfKeyup : 0, UIntPtr.Zero);
                    return (IntPtr)1;
                }
            }
        }
        catch
        {
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
