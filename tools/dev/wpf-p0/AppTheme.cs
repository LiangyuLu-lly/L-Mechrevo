using System.Windows;
using System.Windows.Media;
using MechrevoLite;

namespace L_Mechrevo.WpfP0;

internal static class AppTheme
{
    internal static bool IsDark { get; private set; } = true;

    internal static void Apply(bool dark)
    {
        IsDark = dark;
        ResourceDictionary resources = Application.Current.Resources;
        if (dark)
        {
            resources["WindowBgBrush"] = Brush("#0B0D10");
            resources["CardBrush"] = Brush("#FF161A20");
            resources["DockBrush"] = Brush("#B3161A20");
            resources["CardBorderBrush"] = Brush("#2AFFFFFF");
            resources["ControlBrush"] = Brush("#22FFFFFF");
            resources["ControlHoverBrush"] = Brush("#2AFFFFFF");
            resources["TextBrush"] = Brush("#F3F5F8");
            resources["MutedBrush"] = Brush("#9BA6B5");
            resources["TrackBrush"] = Brush("#35FFFFFF");
            resources["PopupBrush"] = Brush("#F21B2029");
            resources["SegmentCheckedBackgroundBrush"] = Brush("#22FFFFFF");
            resources["SegmentCheckedForegroundBrush"] = Brush("#F3F5F8");
        }
        else
        {
            resources["WindowBgBrush"] = Brush("#F7F8FA");
            resources["CardBrush"] = Brush("#FFFFFFFF");
            resources["DockBrush"] = Brush("#CCFFFFFF");
            resources["CardBorderBrush"] = Brush("#33000000");
            resources["ControlBrush"] = Brush("#1A000000");
            resources["ControlHoverBrush"] = Brush("#26000000");
            resources["TextBrush"] = Brush("#1B2029");
            resources["MutedBrush"] = Brush("#5A6574");
            resources["TrackBrush"] = Brush("#26000000");
            resources["PopupBrush"] = Brush("#FFFFFFFF");
            resources["SegmentCheckedBackgroundBrush"] = Brush("#1A3B82F6");
            resources["SegmentCheckedForegroundBrush"] = Brush("#1B2029");
        }
        ApplyCardOpacity();
    }

    internal static void ApplyCardOpacity()
    {
        try
        {
            if (Application.Current.Resources["CardBrush"] is not SolidColorBrush brush)
            {
                return;
            }
            int opacity = Math.Clamp(AppConfig.Get("ui_card_opacity", 100), 0, 100);
            Color color = brush.Color;
            color.A = (byte)Math.Round(opacity * 255.0 / 100.0);
            Application.Current.Resources["CardBrush"] = new SolidColorBrush(color);
        }
        catch
        {
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
