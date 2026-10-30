using System;
using System.Windows;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>按当前工作区限制窗口尺寸，避免高 DPI/小屏下底部按钮不可达。</summary>
internal static class WindowWorkAreaPolicy
{
    internal static void ClampOnLoaded(Window window, double minWidth = 360, double minHeight = 320)
    {
        if (window.IsLoaded)
        {
            Clamp(window, minWidth, minHeight);
            return;
        }
        window.Loaded += (_, _) => Clamp(window, minWidth, minHeight);
    }

    internal static void Clamp(Window window, double minWidth = 360, double minHeight = 320)
    {
        try
        {
            Rect work = SystemParameters.WorkArea;
            double maxWidth = Math.Max(minWidth, work.Width - 32);
            double maxHeight = Math.Max(minHeight, work.Height - 32);
            window.MaxWidth = maxWidth;
            window.MaxHeight = maxHeight;
            if (window.Width > maxWidth) window.Width = maxWidth;
            if (window.Height > maxHeight) window.Height = maxHeight;
            if (window.MinWidth > maxWidth) window.MinWidth = maxWidth;
            if (window.MinHeight > maxHeight) window.MinHeight = maxHeight;
        }
        catch
        {
        }
    }
}
