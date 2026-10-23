using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyFont();
        base.OnStartup(e);
    }

    private static void ApplyFont()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "OPPOSansVariable.ttf");
            if (File.Exists(path))
            {
                FontFamily family = new FontFamily(path + "#OPPO Sans 4.0");
                Application.Current.Resources["AppFontFamily"] = family;
                Application.Current.Resources["AppFontBoldFamily"] = family;
            }
        }
        catch
        {
        }
    }
}
