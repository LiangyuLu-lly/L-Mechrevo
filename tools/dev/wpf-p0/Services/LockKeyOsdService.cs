using System;
using System.Threading;
using L_Mechrevo.WpfP0.Services;

namespace L_Mechrevo.WpfP0;

// OSD 交互参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>
/// 系统大小写/小键盘锁定状态监听：轮询 GetKeyState，变化时弹 OSD。
/// 同时让 QuickState 读取真实系统状态，快捷开关会跟随。
/// </summary>
internal static class LockKeyOsdService
{
    /// <summary>物理 CapsLock/NumLock 状态变化（key = capslock/numpad）。</summary>
    internal static event Action<string, bool>? Changed;
    private static readonly object Gate = new();
    private static Timer? _timer;
    private static bool _started;
    private static bool _caps;
    private static bool _num;
    private static bool _scroll;

    internal static void Start()
    {
        if (Interlocked.Exchange(ref _started, true))
        {
            return;
        }
        try
        {
            _caps = MechrevoLite.Helpers.NativeLockKey.IsCapsLockOn();
            _num = MechrevoLite.Helpers.NativeLockKey.IsNumLockOn();
            _scroll = MechrevoLite.Helpers.NativeLockKey.IsScrollLockOn();
            _timer = new Timer(_ => Poll(), null, 200, 200);
            Logger.WriteLine("[osd] 大小写/小键盘锁定监听已启动");
        }
        catch (Exception ex)
        {
            Logger.WriteError("[osd] 锁定键监听启动失败：" + ex.GetType().Name);
        }
    }

    private static void Poll()
    {
        try
        {
            bool caps = MechrevoLite.Helpers.NativeLockKey.IsCapsLockOn();
            bool num = MechrevoLite.Helpers.NativeLockKey.IsNumLockOn();
            bool scroll = MechrevoLite.Helpers.NativeLockKey.IsScrollLockOn();
            bool capsChanged = caps != _caps;
            bool numChanged = num != _num;
            bool scrollChanged = scroll != _scroll;
            _caps = caps;
            _num = num;
            _scroll = scroll;
            if (capsChanged)
            {
                Show("capslock", caps);
                Changed?.Invoke("capslock", caps);
            }
            if (numChanged)
            {
                Show("numpad", num);
                Changed?.Invoke("numpad", num);
            }
            if (scrollChanged)
            {
                // Fn+SCRLK：暂时只显示 ScrollLock OSD，不做九键映射、不切 NumLock。
                Show("numpad", scroll);
                Changed?.Invoke("numpad", scroll);
            }
        }
        catch
        {
        }
    }

    private static void Show(string key, bool on)
    {
        try
        {
            OsdService.Show(
                OriginalStyleWindow.QuickGlyph(key),
                QuickSwitchService.DisplayLabel(key),
                on ? Resources.WpfStrings.StateOn : Resources.WpfStrings.StateOff);
        }
        catch
        {
        }
    }
}
