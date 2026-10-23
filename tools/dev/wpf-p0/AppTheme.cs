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
            resources["WindowBgBrush"] = Brush("#0B0D10");
            resources["CardBrush"] = Brush("#FF161A20");
            resources["CardBorderBrush"] = Brush("#2AFFFFFF");
            resources["ControlBrush"] = Brush("#22FFFFFF");
            resources["ControlHoverBrush"] = Brush("#2AFFFFFF");
            resources["TextBrush"] = Brush("#F3F5F8");
            resources["MutedBrush"] = Brush("#9BA6B5");
            resources["TrackBrush"] = Brush("#35FFFFFF");
        }
        else
        {
            resources["WindowBgBrush"] = Brush("#F7F8FA");
            resources["CardBrush"] = Brush("#FFFFFFFF");
            resources["CardBorderBrush"] = Brush("#26000000");
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
