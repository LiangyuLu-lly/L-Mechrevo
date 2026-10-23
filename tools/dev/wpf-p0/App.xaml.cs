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
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            string regular = Path.Combine(dir, "HarmonyOS_Sans_SC_Regular.ttf");
            string bold = Path.Combine(dir, "HarmonyOS_Sans_SC_Bold.ttf");
            bool installed = UserFontInstaller.EnsureInstalled(regular, bold);
            FontFamily regularFamily;
            FontFamily boldFamily;
            if (installed)
            {
                regularFamily = new FontFamily("HarmonyOS Sans SC");
                boldFamily = new FontFamily("HarmonyOS Sans SC");
            }
            else
            {
                regularFamily = File.Exists(regular)
                    ? new FontFamily(regular + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
                boldFamily = File.Exists(bold)
                    ? new FontFamily(bold + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
            }
            Application.Current.Resources["AppFontFamily"] = regularFamily;
            Application.Current.Resources["AppFontBoldFamily"] = boldFamily;
        }
        catch
        {
        }
    }
}
