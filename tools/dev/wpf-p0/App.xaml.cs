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
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "HarmonyOS_Sans_SC_Regular.ttf");
            if (File.Exists(fontPath))
            {
                Application.Current.Resources["AppFontFamily"] = new FontFamily(fontPath + "#HarmonyOS Sans SC");
            }
        }
        catch
        {
        }
    }
}
