using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class CompactWindow : Window
{
    private bool _dark = true;
    private readonly OriginalStyleWindow? _owner;
    private readonly BackendProbe? _probe;

    internal CompactWindow(OriginalStyleWindow? owner = null, BackendProbe? probe = null)
    {
        InitializeComponent();
        _owner = owner;
        _probe = probe;
        ApplyDarkTitleBar(_dark);
        OpacitySlider.Value = 78;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? "浅色" : "深色";
        StatusText.Text = "紧凑 P0 · " + (_dark ? "深色" : "浅色") + "主题";
    }

    private void WideButton_Click(object sender, RoutedEventArgs e)
    {
        new MainWindow(_owner, _probe).Show();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton button && button.Tag is string text
            && int.TryParse(text, out int mode))
        {
            _owner?.WideSetMode(mode);
            StatusText.Text = "性能模式已请求：" + button.Content;
        }
    }

    private void Gpu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton button && button.Tag is string text
            && int.TryParse(text, out int mode))
        {
            _owner?.WideSetGpuMode(mode);
            StatusText.Text = "显卡模式已请求：" + button.Content;
        }
    }

    private async void QuickSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox box && box.Tag is string key)
        {
            bool ok = _owner != null && await _owner.WideToggleQuickAsync(key);
            StatusText.Text = ok ? "快捷开关已切换：" + key : "快捷开关失败：" + key;
        }
    }

    private void OpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundImage == null)
        {
            return;
        }
        BackgroundImage.Opacity = e.NewValue / 100.0;
        if (OpacityLabel != null)
        {
            OpacityLabel.Text = Math.Round(e.NewValue) + "%";
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
