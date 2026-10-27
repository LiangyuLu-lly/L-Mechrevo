using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using L_Mechrevo.WpfP0.Services;
using L_Mechrevo.WpfP0.Resources;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 轻量 OSD：性能模式、灯光、屏幕亮度、快捷键锁状态的即时提示。
/// 窗口不激活、不抢焦点，短时自动消失；新提示覆盖旧提示。
/// </summary>
internal static class OsdService
{
    private static Window? _window;
    private static DispatcherTimer? _timer;

    internal static void Show(string glyph, string title, string value)
    {
        if (MechrevoLite.AppConfig.Get("lm_osd_enabled", 1) == 0)
        {
            return;
        }
        Application? app = Application.Current;
        if (app == null)
        {
            return;
        }
        app.Dispatcher.BeginInvoke(new Action(() => ShowCore(glyph, title, value)));
    }

    private static void ShowCore(string glyph, string title, string value)
    {
        try
        {
            _timer?.Stop();
            _window?.Close();

            TextBlock glyphText = new()
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 30,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            TextBlock titleText = new()
            {
                Text = title,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            TextBlock valueText = new()
            {
                Text = value,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel row = new() { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(glyph))
            {
                row.Children.Add(glyphText);
            }
            row.Children.Add(titleText);
            row.Children.Add(valueText);

            Border border = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1B, 0x20, 0x29)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 12, 24, 12),
                Child = row
            };

            Window window = new()
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = border,
                IsHitTestVisible = false
            };
            window.Loaded += (_, _) =>
            {
                Rect area = SystemParameters.WorkArea;
                window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
                window.Top = area.Bottom - window.ActualHeight - 110;
            };
            window.Show();
            _window = window;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.7) };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
                window.Close();
            };
            _timer.Start();
        }
        catch
        {
        }
    }

    internal static void ShowQuickSwitch(string key, bool on)
    {
        string glyph = OriginalStyleWindow.QuickGlyph(key);
        Show(glyph, QuickSwitchService.DisplayLabel(key), on ? WpfStrings.StateOn : WpfStrings.StateOff);
    }
}
