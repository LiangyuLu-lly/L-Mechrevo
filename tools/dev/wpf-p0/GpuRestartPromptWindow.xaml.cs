using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace L_Mechrevo.WpfP0;

public enum GpuPromptResult
{
    Cancel,
    SwitchOnly,
    SwitchAndRestart
}

public partial class GpuRestartPromptWindow : Window
{
    public GpuPromptResult Result { get; private set; } = GpuPromptResult.Cancel;

    public GpuRestartPromptWindow(string modeName)
    {
        InitializeComponent();
        MessageText.Text =
            "切换到「" + modeName + "」需要重启显卡模式。\n\n" +
            "是：切换并请求重启\n" +
            "否：仅切换，稍后手动重启\n" +
            "取消：不切换";
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Result = GpuPromptResult.SwitchAndRestart;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = GpuPromptResult.SwitchOnly;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = GpuPromptResult.Cancel;
        DialogResult = false;
    }

    private void ApplyDarkTitleBar()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        int enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
