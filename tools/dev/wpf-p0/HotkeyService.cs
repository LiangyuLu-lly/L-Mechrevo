using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 全局热键：Ctrl+Alt+1..4 切性能模式，Ctrl+Alt+O 悬浮窗，
/// Ctrl+Alt+S 设置，Ctrl+Alt+M 显示主界面。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint Vk1 = 0x31;
    private const uint Vk2 = 0x32;
    private const uint Vk3 = 0x33;
    private const uint Vk4 = 0x34;
    private const uint VkO = 0x4F;
    private const uint VkS = 0x53;
    private const uint VkM = 0x4D;

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private readonly uint _modifiers = ModControl | ModAlt | ModNoRepeat;
    private BigIntegerBits _registered;
    private bool _disposed;

    internal event Action<int>? ModeRequested;
    internal event Action? OverlayRequested;
    internal event Action? SettingsRequested;
    internal event Action? ShowRequested;

    internal HotkeyService(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("HWND 未就绪");
        _hook = WndProc;
        _source.AddHook(_hook);
        Register(1, Vk1);
        Register(2, Vk2);
        Register(3, Vk3);
        Register(4, Vk4);
        Register(5, VkO);
        Register(6, VkS);
        Register(7, VkM);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case 1: ModeRequested?.Invoke(2); handled = true; break;
                case 2: ModeRequested?.Invoke(0); handled = true; break;
                case 3: ModeRequested?.Invoke(1); handled = true; break;
                case 4: ModeRequested?.Invoke(3); handled = true; break;
                case 5: OverlayRequested?.Invoke(); handled = true; break;
                case 6: SettingsRequested?.Invoke(); handled = true; break;
                case 7: ShowRequested?.Invoke(); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    private void Register(int id, uint vk)
    {
        if (RegisterHotKey(_source.Handle, id, _modifiers, vk))
        {
            _registered.Set(id);
        }
    }

    private void Unregister(int id)
    {
        if (_registered.Has(id))
        {
            UnregisterHotKey(_source.Handle, id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            for (int id = 1; id <= 7; id++)
            {
                Unregister(id);
            }
            _source.RemoveHook(_hook);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private struct BigIntegerBits
    {
        private int _bits;
        internal void Set(int id) => _bits |= 1 << id;
        internal bool Has(int id) => (_bits & (1 << id)) != 0;
    }
}
