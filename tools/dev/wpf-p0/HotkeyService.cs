using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MechrevoLite;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

/// <summary>全局热键：绑定保存在 config，支持设置页修改。</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    internal readonly record struct HotkeyAction(int Id, string ConfigKey, string Label, uint DefaultModifiers, uint DefaultKey);

    internal static readonly HotkeyAction[] Actions =
    {
        new(1, "mode_silent", WpfStrings.L("切换到静音模式", "Switch to Silent mode"), ModControl | ModAlt, 0x31),
        new(2, "mode_balanced", WpfStrings.L("切换到平衡模式", "Switch to Balanced mode"), ModControl | ModAlt, 0x32),
        new(3, "mode_turbo", WpfStrings.L("切换到狂暴模式", "Switch to Turbo mode"), ModControl | ModAlt, 0x33),
        new(4, "mode_custom", WpfStrings.L("切换到自定义模式", "Switch to Custom mode"), ModControl | ModAlt, 0x34),
        new(5, "overlay", WpfStrings.L("悬浮窗", "Overlay"), ModControl | ModAlt, 0x4F),
        new(6, "settings", WpfStrings.L("设置", "Settings"), ModControl | ModAlt, 0x53),
        new(7, "show_main", WpfStrings.L("显示主界面", "Show main window"), ModControl | ModAlt, 0x4D)
    };

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private readonly HashSet<int> _registered = new();
    private bool _disposed;

    internal static HotkeyService? Current { get; private set; }

    internal event Action<int>? ModeRequested;
    /// <summary>Ctrl+Alt+4：每按一次循环到下一个自定义档（不是固定切到"自定义"）。</summary>
    internal event Action? CustomModeCycleRequested;
    internal event Action? OverlayRequested;
    internal event Action? SettingsRequested;
    internal event Action? ShowRequested;

    internal HotkeyService(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException(WpfStrings.L("HWND 未就绪", "HWND not ready"));
        _hook = WndProc;
        _source.AddHook(_hook);
        Current = this;
        Reload();
    }

    internal void Reload()
    {
        if (_disposed)
        {
            return;
        }
        foreach (int id in _registered)
        {
            UnregisterHotKey(_source.Handle, id);
        }
        _registered.Clear();
        if (AppConfig.Get("hotkey_enabled", 1) == 0)
        {
            return;
        }
        foreach (HotkeyAction action in Actions)
        {
            (uint modifiers, uint key) = ReadGesture(action);
            if (modifiers == 0 || key == 0)
            {
                continue;
            }
            if (RegisterHotKey(_source.Handle, action.Id, modifiers | ModNoRepeat, key))
            {
                _registered.Add(action.Id);
            }
        }
    }

    private static (uint Modifiers, uint Key) ReadGesture(HotkeyAction action)
    {
        string? text = AppConfig.GetString("hotkey_" + action.ConfigKey);
        if (!string.IsNullOrWhiteSpace(text))
        {
            string[] parts = text.Split(',');
            if (parts.Length == 2
                && uint.TryParse(parts[0], out uint modifiers)
                && uint.TryParse(parts[1], out uint key)
                && key != 0)
            {
                return (modifiers, key);
            }
        }
        return (action.DefaultModifiers, action.DefaultKey);
    }

    internal static (uint Modifiers, uint Key) GetGesture(HotkeyAction action) => ReadGesture(action);

    internal static void SetGesture(HotkeyAction action, uint modifiers, uint key)
    {
        AppConfig.Set("hotkey_" + action.ConfigKey, modifiers + "," + key);
        AppConfig.Flush();
        Current?.Reload();
    }

    internal static void ResetGestures()
    {
        foreach (HotkeyAction action in Actions)
        {
            AppConfig.Remove("hotkey_" + action.ConfigKey);
        }
        AppConfig.Set("hotkey_enabled", 1);
        AppConfig.Flush();
        Current?.Reload();
    }

    internal static string Describe(uint modifiers, uint key)
    {
        List<string> parts = new();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        string keyText = key switch
        {
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0x34 => "4",
            0x4F => "O",
            0x53 => "S",
            0x4D => "M",
            _ => ((char)key).ToString()
        };
        parts.Add(keyText);
        return string.Join("+", parts);
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
                case 4: CustomModeCycleRequested?.Invoke(); handled = true; break;
                case 5: OverlayRequested?.Invoke(); handled = true; break;
                case 6: SettingsRequested?.Invoke(); handled = true; break;
                case 7: ShowRequested?.Invoke(); handled = true; break;
            }
        }
        return IntPtr.Zero;
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
            foreach (int id in _registered)
            {
                UnregisterHotKey(_source.Handle, id);
            }
            _registered.Clear();
            _source.RemoveHook(_hook);
        }
        catch
        {
        }
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
