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
    bool OverlayOn);

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
        RebuildMenu();
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
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(DisabledTitle("性能模式"));
            _menu.Items.Add(MenuItem("静音", mode == 2, () => ModeRequested?.Invoke(2)));
            _menu.Items.Add(MenuItem("平衡", mode == 0, () => ModeRequested?.Invoke(0)));
            _menu.Items.Add(MenuItem("狂暴", mode == 1, () => ModeRequested?.Invoke(1)));

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

            _menu.Items.Add(new ToolStripSeparator());
            if (state?.FanBoostVisible == true)
            {
                _menu.Items.Add(MenuItem("强冷", state.FanBoostOn, () => FanBoostRequested?.Invoke()));
            }
            if (state?.GpuVisible == true)
            {
                ToolStripMenuItem gpu = new("显卡模式");
                gpu.DropDownItems.Add(MenuItem("核显模式", state.GpuMode == 0, () => GpuModeRequested?.Invoke(0)));
                gpu.DropDownItems.Add(MenuItem("混合模式", state.GpuMode == 1, () => GpuModeRequested?.Invoke(1)));
                gpu.DropDownItems.Add(MenuItem("独显直连", state.GpuMode == 2, () => GpuModeRequested?.Invoke(2)));
                _menu.Items.Add(gpu);
            }
            if (state?.KeyboardLightVisible == true)
            {
                _menu.Items.Add(MenuItem("键盘灯光", false, () => KeyboardLightingRequested?.Invoke()));
            }
            _menu.Items.Add(MenuItem("悬浮窗", state?.OverlayOn == true, () => OverlayRequested?.Invoke()));
            _menu.Items.Add(MenuItem("设置", false, () => SettingsRequested?.Invoke()));
            _menu.Items.Add(MenuItem("显示主界面", false, () => ShowRequested?.Invoke()));
            _menu.Items.Add(MenuItem("导出诊断包", false, () => DiagnosticsRequested?.Invoke()));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("退出", false, () => ExitRequested?.Invoke()));
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
