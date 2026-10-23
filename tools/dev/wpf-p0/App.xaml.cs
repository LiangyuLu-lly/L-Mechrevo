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
            string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            string regular = Path.Combine(dir, "OPPOSansR.ttf");
            string bold = Path.Combine(dir, "OPPOSansB.ttf");
            if (File.Exists(regular))
            {
                Application.Current.Resources["AppFontFamily"] = new FontFamily(regular + "#OPPOSans");
            }
            if (File.Exists(bold))
            {
                Application.Current.Resources["AppFontBoldFamily"] = new FontFamily(bold + "#OPPOSans B");
            }
        }
        catch
        {
        }
    }
}
