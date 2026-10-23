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
        ApplyTheme(dark: true);
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

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        ApplyTheme(_dark);
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

    private void ApplyTheme(bool dark)
    {
        ResourceDictionary resources = Application.Current.Resources;
        if (dark)
        {
            resources["WindowBgBrush"] = Brush("#12151B");
            resources["CardBrush"] = Brush("#B81B2029");
            resources["CardBorderBrush"] = Brush("#33FFFFFF");
            resources["ControlBrush"] = Brush("#22FFFFFF");
            resources["ControlHoverBrush"] = Brush("#33FFFFFF");
            resources["TextBrush"] = Brush("#F3F5F8");
            resources["MutedBrush"] = Brush("#9BA6B5");
            resources["TrackBrush"] = Brush("#35FFFFFF");
        }
        else
        {
            resources["WindowBgBrush"] = Brush("#EDF1F6");
            resources["CardBrush"] = Brush("#D9FFFFFF");
            resources["CardBorderBrush"] = Brush("#33000000");
            resources["ControlBrush"] = Brush("#14000000");
            resources["ControlHoverBrush"] = Brush("#22000000");
            resources["TextBrush"] = Brush("#1B2029");
            resources["MutedBrush"] = Brush("#5A6574");
            resources["TrackBrush"] = Brush("#22000000");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
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
