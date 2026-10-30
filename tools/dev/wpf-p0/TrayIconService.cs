using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using L_Mechrevo.WpfP0.Resources;

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
    bool SilentTurboOn,
    bool TurboVisible,
    bool GpuDirectVisible = true,
    bool GpuEcoVisible = true,
    bool OverlayLocked = false);

internal static class TrayMenuConfig
{
    internal static readonly string[] CatalogKeys =
    {
        "performance", "custom", "gpu", "keyboard", "overlay", "main", "diagnostics", "exit"
    };

    internal static IEnumerable<(string Key, string Label)> Catalog =>
        CatalogKeys.Select(key => (key, Label(key)));

    private static string Label(string key) => key switch
    {
        "performance" => WpfStrings.TrayPerformance,
        "custom" => WpfStrings.TrayCustom,
        "gpu" => WpfStrings.TrayGpu,
        "keyboard" => WpfStrings.TrayKeyboard,
        "overlay" => WpfStrings.TrayOverlay,
        "main" => WpfStrings.TrayMain,
        "diagnostics" => WpfStrings.TrayDiagnostics,
        "exit" => WpfStrings.TrayExit,
        _ => key
    };

    private const string ConfigKey = "tray_menu_items";

    internal static HashSet<string> Load()
    {
        string? text = MechrevoLite.AppConfig.GetString(ConfigKey);
        if (text == null)
        {
            return CatalogKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsEnabled(string key) => Load().Contains(key);

    internal static void Save(IEnumerable<string> keys)
    {
        string text = string.Join(",", CatalogKeys.Where(key => keys.Contains(key, StringComparer.OrdinalIgnoreCase)));
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
    private long _lastTrayActionTick;
    private bool _disposed;

    internal Func<TrayMenuState>? StateProvider { get; set; }

    internal event Action? ShowRequested;
    internal event Action? SettingsRequested;
    internal event Action? OverlayRequested;
    internal event Action? OverlayLockRequested;
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
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowRequested?.Invoke();
            }
        };
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

    internal void RefreshMenu() => RebuildMenu();

    internal void SetTooltip(string? text)
    {
        if (_disposed || _icon == null)
        {
            return;
        }
        try
        {
            string value = string.IsNullOrWhiteSpace(text) ? "L-Mechrevo" : text!;
            if (value.Length > 63)
            {
                value = value.Substring(0, 63);
            }
            if (!string.Equals(_icon.Text, value, StringComparison.Ordinal))
            {
                _icon.Text = value;
            }
        }
        catch
        {
        }
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
                _menu.Items.Add(DisabledTitle(WpfStrings.TrayPerformance));
                _menu.Items.Add(MenuItem(WpfStrings.TraySilent, mode == 2, () => ModeRequested?.Invoke(2)));
                _menu.Items.Add(MenuItem(WpfStrings.TrayBalanced, mode == 0, () => ModeRequested?.Invoke(0)));
                if (state?.SilentTurboVisible == true)
                {
                    _menu.Items.Add(MenuItem(WpfStrings.TraySilentTurbo, mode == 1 && state.SilentTurboOn, () => SilentTurboRequested?.Invoke()));
                }
                if (state?.TurboVisible == true)
                {
                    _menu.Items.Add(MenuItem(state?.SilentTurboVisible == true ? WpfStrings.TurboHighEnergy : WpfStrings.TrayTurbo, mode == 1 && state?.SilentTurboOn != true, () => ModeRequested?.Invoke(1)));
                }
                if (state?.FanBoostVisible == true)
                {
                    _menu.Items.Add(MenuItem(WpfStrings.TrayFanBoost, state.FanBoostOn, () => FanBoostRequested?.Invoke()));
                }
            }

            if (TrayMenuConfig.IsEnabled("custom"))
            {
                ToolStripMenuItem customRoot = new(state?.CustomRootText ?? WpfStrings.TrayCustom)
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
                    ToolStripMenuItem empty = new(WpfStrings.TrayNoCustom) { Enabled = false };
                    customRoot.DropDownItems.Add(empty);
                }
                _menu.Items.Add(customRoot);
            }

            if (TrayMenuConfig.IsEnabled("gpu") && state?.GpuVisible == true)
            {
                _menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem gpu = new(WpfStrings.TrayGpu);
                if (state.GpuEcoVisible)
                {
                    gpu.DropDownItems.Add(MenuItem(WpfStrings.TrayGpuIntegrated, state.GpuMode == 0, () => GpuModeRequested?.Invoke(0)));
                }
                gpu.DropDownItems.Add(MenuItem(WpfStrings.TrayGpuHybrid, state.GpuMode == 1, () => GpuModeRequested?.Invoke(1)));
                if (state.GpuDirectVisible)
                {
                    gpu.DropDownItems.Add(MenuItem(WpfStrings.TrayGpuDirect, state.GpuMode == 2, () => GpuModeRequested?.Invoke(2)));
                }
                _menu.Items.Add(gpu);
            }
            if (TrayMenuConfig.IsEnabled("keyboard") && state?.KeyboardLightVisible == true)
            {
                _menu.Items.Add(MenuItem(WpfStrings.TrayKeyboard, false, () => KeyboardLightingRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("overlay"))
            {
                _menu.Items.Add(MenuItem(WpfStrings.TrayOverlay, state?.OverlayOn == true, () => OverlayRequested?.Invoke()));
                _menu.Items.Add(MenuItem(WpfStrings.TrayOverlayLock, state?.OverlayLocked == true, () => OverlayLockRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("main"))
            {
                _menu.Items.Add(MenuItem(WpfStrings.TrayMain, false, () => ShowRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("diagnostics"))
            {
                _menu.Items.Add(MenuItem(WpfStrings.TrayDiagnostics, false, () => DiagnosticsRequested?.Invoke()));
            }
            if (TrayMenuConfig.IsEnabled("exit"))
            {
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(MenuItem(WpfStrings.TrayExit, false, () => ExitRequested?.Invoke()));
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private ToolStripMenuItem MenuItem(string text, bool isChecked, Action action)
    {
        ToolStripMenuItem item = new(text) { Checked = isChecked };
        item.Click += (_, _) =>
        {
            // 托盘动作可能是 async void（切档/开关），同步锁会在 await 前就释放；
            // 用时间窗兜住异步动作，避免连点重复下发。
            long now = Environment.TickCount64;
            if (now - _lastTrayActionTick < 1200) return;
            _lastTrayActionTick = now;
            try { action(); }
            catch (Exception ex) { global::Logger.WriteLine("[tray] 动作失败：" + ex.Message); }
        };
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
