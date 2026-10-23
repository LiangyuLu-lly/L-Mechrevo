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
            bool installed = Fonts.SystemFontFamilies.Any(f =>
                f.Source.IndexOf("HarmonyOS Sans SC", StringComparison.OrdinalIgnoreCase) >= 0);
            FontFamily regularFamily = installed || !File.Exists(regular)
                ? new FontFamily("HarmonyOS Sans SC")
                : new FontFamily(regular + "#HarmonyOS Sans SC");
            FontFamily boldFamily = installed || !File.Exists(bold)
                ? new FontFamily("HarmonyOS Sans SC")
                : new FontFamily(bold + "#HarmonyOS Sans SC");
            Application.Current.Resources["AppFontFamily"] = regularFamily;
            Application.Current.Resources["AppFontBoldFamily"] = boldFamily;
        }
        catch
        {
        }
    }
}
