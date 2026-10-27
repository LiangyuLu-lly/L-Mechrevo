using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

/// <summary>WPF 版轻提示，接原版 ToastForm 的通知/失败回调。</summary>
internal static class ToastService
{
    private static Window? _current;
    private static DispatcherTimer? _timer;

    internal static void Show(string text, bool failure = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        Application? app = Application.Current;
        if (app == null)
        {
            return;
        }
        app.Dispatcher.BeginInvoke(() => ShowCore(text, failure));
    }

    private static void ShowCore(string text, bool failure)
    {
        try
        {
            _timer?.Stop();
            _current?.Close();

            Border border = new()
            {
                Background = new SolidColorBrush(failure ? Color.FromArgb(0xE6, 0x7A, 0x1F, 0x1F) : Color.FromArgb(0xE6, 0x1B, 0x20, 0x29)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 10, 18, 10),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                }
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
                Content = border
            };
            window.Show();
            Rect area = SystemParameters.WorkArea;
            window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
            window.Top = area.Bottom - window.ActualHeight - 90;

            _current = window;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(failure ? 4 : 2.2) };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                if (ReferenceEquals(_current, window))
                {
                    _current = null;
                }
                window.Close();
            };
            _timer.Start();
        }
        catch
        {
        }
    }
}
