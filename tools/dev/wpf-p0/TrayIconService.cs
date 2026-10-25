using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace L_Mechrevo.WpfP0;

internal sealed record TrayCustomItem(string Id, string Label, bool Checked);

internal sealed record TrayMenuState(
    int CurrentMode,
    string CustomRootText,
    IReadOnlyList<TrayCustomItem> CustomItems,
    bool FanBoostVisible,
    bool FanBoostOn,
    bool GpuVisible,
    int GpuMode,
    bool KeyboardLightVisible,
    bool OverlayOn,
    bool SilentTurboVisible,
    bool SilentTurboOn);

internal static class TrayMenuConfig
{
    internal static readonly (string Key, string Label)[] Catalog =
    {
        ("performance", "性能模式"),
        ("custom", "自定义模式"),
        ("gpu", "显卡模式"),
        ("keyboard", "键盘灯光"),
        ("overlay", "悬浮窗"),
        ("main", "打开主界面"),
        ("diagnostics", "导出诊断包"),
        ("exit", "退出")
    };

    private const string ConfigKey = "tray_menu_items";

    internal static HashSet<string> Load()
    {
        string? text = MechrevoLite.AppConfig.GetString(ConfigKey);
        if (text == null)
        {
            return Catalog.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsEnabled(string key) => Load().Contains(key);

    internal static void Save(IEnumerable<string> keys)
    {
        string text = string.Join(",", Catalog.Select(item => item.Key).Where(key => keys.Contains(key, StringComparer.OrdinalIgnoreCase)));
        MechrevoLite.AppConfig.Set(ConfigKey, text);
        MechrevoLite.AppConfig.Flush();
    }
}

/// <summary>托盘图标与菜单。菜单每次打开前按主窗口最新状态重建。</summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private bool _rebuilding;
    private bool _disposed;

    internal Func<TrayMenuState>? StateProvider { get; set; }

    internal event Action? ShowRequested;
    internal event Action? SettingsRequested;
    internal event Action? OverlayRequested;
    internal event Action? ExitRequested;
    internal event Action? KeyboardLightingRequested;
    internal event Action? DiagnosticsRequested;
    internal event Action? FanBoostRequested;
    internal event Action<int>? ModeRequested;
    internal event Action<int>? GpuModeRequested;
    internal event Action<string>? CustomModeRequested;
    internal event Action? SilentTurboRequested;

    internal TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "L-Mechrevo",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
        _menu.Opening += (_, _) => RebuildMenu();
        _menu.ShowCheckMargin = true;
        _menu.ShowImageMargin = false;
        _menu.ImageScalingSize = new Size(16, 16);
        _menu.Padding = new Padding(5);
        _menu.DropShadowEnabled = true;
        // 2026-10-26（P0-5）：托盘菜单渲染器迁到 WPF 项目内（TrayMenuRenderer），不再依赖旧 UI 的渲染器类型。
        _menu.Renderer = new TrayMenuRenderer();
        SetTheme(dark: true);
        RebuildMenu();
    }

    internal void SetTheme(bool dark)
    {
        try
        {
            _menu.BackColor = dark ? Color.FromArgb(22, 27, 35) : Color.FromArgb(248, 249, 251);
            _menu.ForeColor = dark ? Color.FromArgb(243, 245, 248) : Color.FromArgb(28, 32, 38);
            // 2026-10-26（P0-5）：字体由 WPF 项目内的 TrayMenuRenderer 提供（内嵌 HarmonyOS Sans SC，9pt；
            // 加载/回退/缓存口径与迁移前旧 UI 字体工具一致）。
            _menu.Font = TrayMenuRenderer.MenuFont(9f);
            _menu.Renderer = new TrayMenuRenderer();
        }
        catch
        {
        }
    }

    private void RebuildMenu()
    {
        if (_rebuilding || _disposed)
        {
            return;
        }
        _rebuilding = true;
        try
        {
            _menu.Items.Clear();
            TrayMenuState? state = null;
            try
            {
                state = StateProvider?.Invoke();
            }
            catch
            {
            }
            int mode = state?.CurrentMode ?? -1;
            if (TrayMenuConfig.IsEnabled("performance"))
            {
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(DisabledTitle("性能模式"));
                _menu.Items.Add(MenuItem("静音", mode == 2, () => ModeRequested?.Invoke(2)));
                _menu.Items.Add(MenuItem("平衡", mode == 0, () => ModeRequested?.Invoke(0)));
                if (state?.SilentTurboVisible == true)
                {
                    _menu.Items.Add(MenuItem("静音狂暴", mode == 1 && state.SilentTurboOn, () => SilentTurboRequested?.Invoke()));
                }
                _menu.Items.Add(MenuItem("狂暴", mode == 1 && state?.SilentTurboOn != true, () => ModeRequested?.Invoke(1)));
                if (state?.FanBoostVisible == true)
                {
                    _menu.Items.Add(MenuItem("强冷", state.FanBoostOn, () => FanBoostRequested?.Invoke()));
                }
            }

            if (TrayMenuConfig.IsEnabled("custom"))
            {
                ToolStripMenuItem customRoot = new(state?.CustomRootText ?? "自定义")
                {
                    Checked = state?.CustomItems.Any(i => i.Checked) == true
                };
                if (state?.CustomItems != null)
                {
                    foreach (TrayCustomItem item in state.CustomItems)
                    {
                        TrayCustomItem copy = item;
                        ToolStripMenuItem sub = MenuItem(copy.Label, copy.Checked, () => CustomModeRequested?.Invoke(copy.Id));
                        customRoot.DropDownItems.Add(sub);
                    }
                }
                if (customRoot.DropDownItems.Count == 0)
                {
                    ToolStripMenuItem empty = new("暂无自定义档") { Enabled = false };
                    customRoot.DropDownItems.Add(empty);
                }
                _menu.Items.Add(customRoot);
            }

            if (TrayMenuConfig.IsEnabled("gpu") && state?.GpuVisible == true)
            {
                _menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem gpu = new("显卡模式");
                gpu.DropDownItems.Add(MenuItem("核显模式", state.GpuMode == 0, () => GpuModeRequested?.Invoke(0)));
                gpu.DropDownItems.Add(MenuItem("混合模式", state.GpuMode == 1, () => GpuModeRequested?.Invoke(1)));
                gpu.DropDownItems.Add(MenuItem("独显直连", state.GpuMode == 2, () => GpuModeRequested?.Invoke(2)));
                _menu.Items.Add(gpu);
            }
            if (TrayMenuConfig.IsEnabled("keyboard") && state?.KeyboardLightVisible == true)
            {
                _menu.Items.Add(MenuItem("键盘灯光", false, () => KeyboardLightingRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("overlay"))
            {
                _menu.Items.Add(MenuItem("悬浮窗", state?.OverlayOn == true, () => OverlayRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("main"))
            {
                _menu.Items.Add(MenuItem("打开主界面", false, () => ShowRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("diagnostics"))
            {
                _menu.Items.Add(MenuItem("导出诊断包", false, () => DiagnosticsRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("exit"))
            {
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(MenuItem("退出", false, () => ExitRequested?.Invoke()));
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private static ToolStripMenuItem MenuItem(string text, bool isChecked, Action action)
    {
        ToolStripMenuItem item = new(text) { Checked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem DisabledTitle(string text)
    {
        return new ToolStripMenuItem(text) { Enabled = false };
    }

    private static Icon LoadIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Icon? extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
        }
        return SystemIcons.Application;
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
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
        catch
        {
        }
    }
}
