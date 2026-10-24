using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using WinForms = System.Windows.Forms;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 用 WPF 窗口承载原版 WinForms 编辑器。编辑器自身的业务逻辑保持原样，
/// 只替换外层窗口壳，避免重写曲线/SMU/灯效逻辑造成回归。
/// </summary>
internal sealed class OriginalFormHostWindow : Window
{
    private readonly WinForms.Form _form;
    private readonly WindowsFormsHost _host;

    internal OriginalFormHostWindow(WinForms.Form form, string? title = null)
    {
        _form = form;
        Title = title ?? form.Text;
        Width = Math.Max(form.Width + 16, 480);
        Height = Math.Max(form.Height + 40, 320);
        MinWidth = Math.Max(form.MinimumSize.Width, 360);
        MinHeight = Math.Max(form.MinimumSize.Height, 240);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1B));

        _host = new WindowsFormsHost();
        Content = _host;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Loaded += (_, _) => AttachForm();
        Closed += (_, _) => DetachForm();
    }

    private void AttachForm()
    {
        if (_host.Child != null)
        {
            return;
        }
        _form.TopLevel = false;
        _form.FormBorderStyle = WinForms.FormBorderStyle.None;
        _form.Dock = WinForms.DockStyle.Fill;
        _host.Child = _form;
        _form.Show();
    }

    private void DetachForm()
    {
        try
        {
            if (_host.Child != null)
            {
                _host.Child = null;
            }
            if (!_form.IsDisposed)
            {
                _form.Close();
                _form.Dispose();
            }
        }
        catch
        {
        }
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
