using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

internal static class AppTheme
{
    internal static void Apply(bool dark)
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
}
