using System;
using System.Drawing;
using System.Windows.Forms;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 托盘图标与菜单。只负责壳和点击转发，模式/设置逻辑仍由主窗口处理。
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _silentItem;
    private readonly ToolStripMenuItem _balancedItem;
    private readonly ToolStripMenuItem _turboItem;
    private readonly ToolStripMenuItem _customItem;
    private bool _disposed;

    internal event Action? ShowRequested;
    internal event Action? SettingsRequested;
    internal event Action? OverlayRequested;
    internal event Action? ExitRequested;
    internal event Action<int>? ModeRequested;

    internal TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "L-Mechrevo",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();

        ContextMenuStrip menu = new();
        menu.Items.Add(new ToolStripMenuItem("显示主界面", null, (_, _) => ShowRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());

        _silentItem = new ToolStripMenuItem("静音", null, (_, _) => ModeRequested?.Invoke(2));
        _balancedItem = new ToolStripMenuItem("平衡", null, (_, _) => ModeRequested?.Invoke(0));
        _turboItem = new ToolStripMenuItem("狂暴", null, (_, _) => ModeRequested?.Invoke(1));
        _customItem = new ToolStripMenuItem("自定义", null, (_, _) => ModeRequested?.Invoke(3));
        menu.Items.Add(_silentItem);
        menu.Items.Add(_balancedItem);
        menu.Items.Add(_turboItem);
        menu.Items.Add(_customItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("悬浮窗", null, (_, _) => OverlayRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("设置", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke()));

        _icon.ContextMenuStrip = menu;
    }

    internal void SetMode(int visualMode)
    {
        _silentItem.Checked = visualMode == 2;
        _balancedItem.Checked = visualMode == 0;
        _turboItem.Checked = visualMode == 1;
        _customItem.Checked = visualMode >= 3;
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
        }
        catch
        {
        }
    }
}
