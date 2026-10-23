using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class MainWindow : Window
{
    private bool _dark = true;

    public MainWindow()
    {
        InitializeComponent();
        AppTheme.Apply(true);
        BackgroundImage.Opacity = 0.78;
        BackgroundScrim.Opacity = 0.35;
        BackgroundOpacitySlider.Value = 78;
        ScrimSlider.Value = 35;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar(_dark);
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e)
    {
        new CompactWindow().Show();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        AppTheme.Apply(_dark);
        ApplyDarkTitleBar(_dark);
        ThemeButton.Content = _dark ? "切换浅色" : "切换深色";
        StatusText.Text = "L-Mechrevo WPF P0 · " + (_dark ? "深色" : "浅色") + "主题 · 资源字典即时切换";
    }

    private void AccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            Color color = (Color)ColorConverter.ConvertFromString(text);
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["AccentColor"] = color;
            StatusText.Text = "强调色已切换：" + text;
        }
    }

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundImage == null)
        {
            return;
        }
        BackgroundImage.Opacity = e.NewValue / 100.0;
        if (BackgroundOpacityLabel != null)
        {
            BackgroundOpacityLabel.Text = Math.Round(e.NewValue) + "%";
        }
    }

    private void ScrimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundScrim == null)
        {
            return;
        }
        BackgroundScrim.Opacity = e.NewValue / 100.0;
        if (ScrimLabel != null)
        {
            ScrimLabel.Text = Math.Round(e.NewValue) + "%";
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
