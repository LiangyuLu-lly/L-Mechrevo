using System.Globalization;
using System.Resources;

namespace L_Mechrevo.WpfP0.Resources;

internal static class WpfStrings
{
    private static readonly ResourceManager Manager =
        new("L_Mechrevo.WpfP0.Resources.WpfStrings", typeof(WpfStrings).Assembly);

    internal static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    internal static string LightingKeyboard => Get("LightingKeyboard");
    internal static string LightingStrip => Get("LightingStrip");
    internal static string LightingLogo => Get("LightingLogo");
    internal static string LightingHinge => Get("LightingHinge");
    internal static string LightingSync => Get("LightingSync");
    internal static string PowerOn => Get("PowerOn");
    internal static string Brightness => Get("Brightness");
    internal static string Speed => Get("Speed");
    internal static string AdjustHint => Get("AdjustHint");
    internal static string LiquidSystem => Get("LiquidSystem");
    internal static string Pump => Get("Pump");
    internal static string Fan => Get("Fan");
    internal static string NotConnected => Get("NotConnected");
    internal static string Connected => Get("Connected");
    internal static string Refresh => Get("Refresh");
}
