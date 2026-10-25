using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class MainWindow : Window
{
    private bool _dark = true;
    private string _activePage = "home";

    public MainWindow()
    {
        InitializeComponent();
        AppTheme.Apply(true);
        BackgroundImage.Opacity = 0.78;
        BackgroundScrim.Opacity = 0.35;
        BuildMoreSwitchTiles();
        ApplyPage("home");
        StatusText.Text = "就绪 · 宽屏四页布局";
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
        StatusText.Text = "L-Mechrevo · " + (_dark ? "深色" : "浅色") + "主题";
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

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
        {
            ApplyPage(page);
        }
    }

    private void ApplyPage(string page)
    {
        _activePage = page;
        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        LightingPage.Visibility = page == "lighting" ? Visibility.Visible : Visibility.Collapsed;
        CustomPage.Visibility = page == "custom" ? Visibility.Visible : Visibility.Collapsed;
        SwitchesPage.Visibility = page == "switches" ? Visibility.Visible : Visibility.Collapsed;

        Button[] navs = { NavHome, NavLighting, NavCustom, NavSwitches };
        foreach (Button nav in navs)
        {
            bool active = string.Equals(nav.Tag as string, page, StringComparison.Ordinal);
            nav.Background = active
                ? (Brush)FindResource("ControlBrush")
                : Brushes.Transparent;
            nav.Foreground = active
                ? (Brush)FindResource("TextBrush")
                : (Brush)FindResource("MutedBrush");
        }
        StatusText.Text = page switch
        {
            "home" => "首页 · 传感器 / 性能 / 显卡 / 显示",
            "lighting" => "灯光 · 键盘 / 灯带 / Logo / 铰链 / 同步",
            "custom" => "自定义 · 自定义模式 / 风扇曲线",
            "switches" => "更多开关 · 磁贴布局",
            _ => "就绪"
        };
    }

    private void QuickTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string key)
        {
            StatusText.Text = "快捷磁贴：" + key + "（等待接入硬件开关）";
        }
    }

    private void MoreSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string label)
        {
            StatusText.Text = "更多开关：" + label + "（等待接入硬件开关）";
        }
    }

    private void BuildMoreSwitchTiles()
    {
        (string Key, string Label)[] switches =
        {
            ("touchpad", "触摸板"),
            ("wifi", "WiFi"),
            ("bt", "蓝牙"),
            ("webcam", "摄像头"),
            ("micmute", "麦克风静音"),
            ("numpad", "小键盘锁"),
            ("touchpadtoggle", "触摸板切换键")
        };
        foreach ((string key, string label) in switches)
        {
            Button tile = new()
            {
                Style = (Style)FindResource("TileButton"),
                Tag = label,
                Margin = new Thickness(5)
            };
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = key, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 6, 0, 0) });
            tile.Content = content;
            tile.Click += MoreSwitch_Click;
            MoreSwitchGrid.Children.Add(tile);
        }
    }

    private void LightEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string topic)
        {
            return;
        }
        if (MechrevoLite.Program.hw == null || MechrevoLite.Program.service == null)
        {
            StatusText.Text = "后端未连接，请从主界面打开灯光";
            return;
        }
        try
        {
            (string Effect, string Name)[] effects = topic switch
            {
                "Keyboard/Ctrl" => MechrevoLite.Hardware.KeyboardFirmwareEffects
                    .Visible(MechrevoLite.Program.hw.Capabilities.KeyboardType)
                    .Select(static item => (item.Id, item.Label)).ToArray(),
                "HidLightbar/Ctrl" => MechrevoLite.LightForm.LightbarEffects,
                "HidLightbar_Logo/Ctrl" => MechrevoLite.Program.hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40
                    ? MechrevoLite.LightForm.LogoEffectsGen40
                    : MechrevoLite.LightForm.CurrentLogoEffects(),
                "HidLightbar_Hinge/Ctrl" => MechrevoLite.LightForm.HingeEffects,
                "HidLightbar_Sync/Ctrl" => MechrevoLite.LightForm.SyncEffects,
                _ => Array.Empty<(string, string)>()
            };
            string title = topic switch
            {
                "Keyboard/Ctrl" => "键盘灯效",
                "HidLightbar/Ctrl" => "灯带灯光",
                "HidLightbar_Logo/Ctrl" => "Logo 灯光",
                "HidLightbar_Hinge/Ctrl" => "铰链灯光",
                "HidLightbar_Sync/Ctrl" => "同步灯光",
                _ => "灯光"
            };
            new LightEditorWindow(topic, title, effects) { Owner = this }.ShowDialog();
            StatusText.Text = "灯光设置已关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开灯光设置失败：" + ex.Message;
        }
    }

    private void CustomMode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new CustomModeWindow().ShowDialog();
            StatusText.Text = "自定义模式已关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开自定义模式失败：" + ex.Message;
        }
    }

    private void FanCurve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FanCurveWindow("balanced").ShowDialog();
            StatusText.Text = "风扇曲线已关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开风扇曲线失败：" + ex.Message;
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
