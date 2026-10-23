using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MechrevoLite.Hardware;
using System.Windows.Interop;
using System.Windows.Threading;

namespace L_Mechrevo.WpfP0;

public partial class OriginalStyleWindow : Window
{
    private bool _dark = true;
    private BackendProbe? _backendProbe;
    private readonly DispatcherTimer _backendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    public OriginalStyleWindow()
    {
        InitializeComponent();
        ApplyDarkTitleBar(_dark);
        _backendTimer.Tick += (_, _) =>
        {
            if (BackendLine != null && _backendProbe != null)
            {
                BackendLine.Text = _backendProbe.Snapshot();
            }
        };
        Closed += (_, _) =>
        {
            _backendTimer.Stop();
            _backendProbe?.Dispose();
            _backendProbe = null;
        };
        if (ModeGrid != null && ModeGrid.Children.Count > 1 && ModeGrid.Children[1] is ToggleButton defaultMode)
        {
            defaultMode.IsChecked = true;
            UpdateModeBorders(defaultMode);
        }
        MainScroll.SizeChanged += delegate
        {
            UpdateContentWidth();
        };
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(new Action(UpdateContentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    private void UpdateContentWidth()
    {
        double width = MainScroll.ViewportWidth - 22;
        if (width <= 100)
        {
            width = MainScroll.ActualWidth - 30;
        }
        if (width > 100)
        {
            ContentPanel.Width = width;
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (object child in ModeGrid.Children)
        {
            if (child is ToggleButton button)
            {
                button.IsChecked = false;
            }
        }
        if (sender is ToggleButton selected)
        {
            selected.IsChecked = true;
            UpdateModeBorders(selected);
            StatusText.Text = "性能模式：" + selected.Content;
        }
    }

    private static void UpdateModeBorders(ToggleButton selected)
    {
        Brush gray = (Brush)Application.Current.Resources["CardBorderBrush"];
        foreach (object child in ((UniformGrid)selected.Parent).Children)
        {
            if (child is ToggleButton button)
            {
                button.BorderBrush = gray;
                button.BorderThickness = new Thickness(1);
            }
        }
        if (selected.Tag is string hex)
        {
            selected.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            selected.BorderThickness = new Thickness(3);
        }
    }

    private void BackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool on = BackgroundToggle.IsChecked == true;
        BackgroundImage.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ConnectBackend_Click(object sender, RoutedEventArgs e)
    {
        if (_backendProbe != null)
        {
            BackendLine.Text = _backendProbe.Snapshot();
            return;
        }
        ConnectBackendButton.IsEnabled = false;
        BackendStatus.Text = "后端连接中…";
        BackendStatus.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
        BackendLine.Text = "正在创建 MechrevoHw 并 ConnectAsync（最长 15 秒）…";
        BackendProbe probe = new BackendProbe();
        _backendProbe = probe;
        Task<bool> connect = probe.ConnectAsync();
        Task completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(15)));
        if (completed != connect)
        {
            BackendStatus.Text = "后端连接超时";
            BackendStatus.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
            BackendLine.Text = "15 秒内未完成；MechrevoHw 可能在没有 GCU 服务的机器上等待/无权限。";
            probe.Dispose();
            _backendProbe = null;
            ConnectBackendButton.IsEnabled = true;
            return;
        }
        if (await connect)
        {
            BackendStatus.Text = "后端已连接";
            BackendStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
            BackendLine.Text = probe.Snapshot();
            ApplyCapabilities();
            _backendTimer.Start();
        }
        else
        {
            BackendStatus.Text = "后端连接失败";
            BackendStatus.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
            BackendLine.Text = probe.LastError;
            probe.Dispose();
            _backendProbe = null;
        }
        ConnectBackendButton.IsEnabled = true;
    }

    private void ApplyCapabilities()
    {
        if (_backendProbe?.Capabilities is not MechrevoDeviceCapabilities caps)
        {
            return;
        }
        try
        {
            GpuSection.Visibility = (caps.DgpuDirect || caps.IgpuOnly || caps.GpuHotSwap) ? Visibility.Visible : Visibility.Collapsed;
            ScreenSection.Visibility = (caps.DisplayRefresh || caps.ColorCalibration || caps.LcdOverdrive || caps.LocalDimming) ? Visibility.Visible : Visibility.Collapsed;
            LightingSection.Visibility = (caps.Keyboard || caps.Lightbar || caps.LogoLight) ? Visibility.Visible : Visibility.Collapsed;
            FanTile.Visibility = _backendProbe.SupportsQuick("fanboost") ? Visibility.Visible : Visibility.Collapsed;
            CameraTile.Visibility = _backendProbe.SupportsQuick("webcam") ? Visibility.Visible : Visibility.Collapsed;
            MicTile.Visibility = _backendProbe.SupportsQuick("micmute") ? Visibility.Visible : Visibility.Collapsed;
            WinKeyTile.Visibility = _backendProbe.SupportsQuick("winkey") ? Visibility.Visible : Visibility.Collapsed;
            ScreenOffTile.Visibility = _backendProbe.SupportsQuick("monitoroff") ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? "浅色" : "深色";
        StatusText.Text = "0.293.5 / WPF P0 · " + (_dark ? "深色" : "浅色");
    }

    private void WideButton_Click(object sender, RoutedEventArgs e)
    {
        new MainWindow().Show();
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string name)
        {
            StatusText.Text = "P0 · " + name;
        }
    }

    private void ApplyDarkTitleBar(bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int flag = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref flag, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
