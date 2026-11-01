using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 水冷灯光的 能力判定 / 配置持久化 / 下发封装。
/// 目标机只有 GCU 水冷时只暴露 GCU 能力位；没有灯光能力位时 UI 不显示灯色入口。
/// </summary>
internal sealed record LiquidLightOption(string Profile, string Label, bool IsCustom = false);

internal static class LiquidCoolingLightService
{
    internal const string ProfileKey = "lc_light_profile";
    internal const string ColorKey = "lc_light_color";
    internal const int DefaultColorArgb = unchecked((int)0xFF00FFFF);

    private static readonly LiquidLightOption[] HeadOptions =
    {
        new("cyan_static", WpfStrings.L("青色常亮", "Cyan solid")),
        new("cyan_breath", WpfStrings.L("青色呼吸", "Cyan breathing")),
        new("colorful", WpfStrings.L("七彩", "Colorful")),
        new("colorful_breath", WpfStrings.L("七彩呼吸", "Colorful breathing")),
        new("custom_static", WpfStrings.L("自定义常亮", "Custom solid"), IsCustom: true),
        new("custom_breath", WpfStrings.L("自定义呼吸", "Custom breathing"), IsCustom: true)
    };

    private static readonly LiquidLightOption[] FanOptions =
    {
        new("fan_rotate", WpfStrings.L("风扇旋转", "Fan spin")),
        new("fan_rainbow", WpfStrings.L("风扇彩虹", "Fan rainbow"))
    };

    internal static IReadOnlyList<LiquidLightOption> Options(MechrevoHw? hw)
    {
        List<LiquidLightOption> options = new(HeadOptions);
        if (SupportsFanEffects(hw))
        {
            options.AddRange(FanOptions);
        }
        options.Add(new LiquidLightOption("off", WpfStrings.L("关闭", "Off")));
        return options;
    }

    /// <summary>灯光颜色能力位：GCU 上报过灯光状态/灯头模式/灯色通道任一即可（不硬编码机型）。</summary>
    internal static bool SupportsColor(MechrevoHw? hw)
    {
        if (hw == null || !hw.SupportsLiquidCooling)
        {
            return false;
        }
        if (hw.LcLightingStatusSeen || hw.LcHeadLightMode >= 0 || hw.LcFanLightMode >= 0)
        {
            return true;
        }
        return hw.LcLedRed >= 0 && hw.LcLedGreen >= 0 && hw.LcLedBlue >= 0;
    }

    internal static bool SupportsFanEffects(MechrevoHw? hw) => hw?.LcFanLightingSupported == true;

    internal static string SavedProfile => AppConfig.GetString(ProfileKey, "") ?? "";

    internal static Color SavedColor => Color.FromArgb(AppConfig.Get(ColorKey, DefaultColorArgb));

    internal static void SaveColor(Color color) => AppConfig.Set(ColorKey, color.ToArgb());

    internal static bool IsCustom(string? profile) =>
        string.Equals(profile, "custom_static", StringComparison.Ordinal)
        || string.Equals(profile, "custom_breath", StringComparison.Ordinal);

    internal static bool IsFanEffect(string? profile) =>
        string.Equals(profile, "fan_rotate", StringComparison.Ordinal)
        || string.Equals(profile, "fan_rainbow", StringComparison.Ordinal);

    internal static async Task<bool> ApplyAsync(MechrevoService? service, MechrevoHw? hw, string profile, Color color)
    {
        if (service == null || !SupportsColor(hw))
        {
            return false;
        }
        if (IsFanEffect(profile) && !SupportsFanEffects(hw))
        {
            return false;
        }
        return await service.LcApplyLightProfile(profile, color, persist: true, requireReadback: true);
    }
}
