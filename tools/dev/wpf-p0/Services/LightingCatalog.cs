using System;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>灯光效果目录（通道效果表 / 颜色数 / 可见 HID 效果），与旧 UI 同序同文案。</summary>
internal static class LightingCatalog
{
    internal static readonly (string Effect, string Name)[] LightbarEffects =
    {
        ("Single", Strings.EffectSingle),
        ("Breathing", Strings.EffectBreathing),
        ("Wave", Strings.EffectWave),
        ("Impact", Strings.EffectImpact),
        ("Raindrop", Strings.EffectMeteor)
    };

    internal static readonly (string Effect, string Name)[] LogoEffects =
    {
        ("Single", Strings.EffectSingle),
        ("Breathing", Strings.EffectBreathing),
        ("Mix", Strings.EffectMix)
    };

    internal static readonly (string Effect, string Name)[] LogoEffectsGen40 =
    {
        ("Single", Strings.EffectSingle),
        ("ColorMarquee", Strings.EffectRainbow),
        ("Breathing", Strings.EffectBreathing),
        ("Wave", Strings.EffectWave),
        ("Impact", Strings.EffectImpact),
        ("Raindrop", Strings.EffectMeteor)
    };

    internal static readonly (string Effect, string Name)[] HingeEffects =
    {
        ("Single", Strings.EffectSingle),
        ("Breathing", Strings.EffectBreathing),
        ("Wave", Strings.EffectWave),
        ("Impact", Strings.EffectImpact),
        ("Raindrop", Strings.EffectMeteor)
    };

    internal static readonly (string Effect, string Name)[] SyncEffects =
    {
        ("Single", Strings.EffectSingle),
        ("Breathing", Strings.EffectBreathing),
        ("Wave", Strings.EffectWave),
        ("Dawn", Strings.LightEffectDawn)
    };

    internal static readonly (int Mode, string Name)[] HidEffects =
    {
        (1, Strings.EffectFlowRainbow),
        (9, Strings.EffectStatic),
        (0, Strings.EffectBreathing),
        (2, Strings.EffectBlink),
        (3, Strings.EffectReactive),
        (4, Strings.EffectRainbowWheel),
        (5, Strings.EffectLightning),
        (6, Strings.EffectFlame),
        (7, Strings.EffectRaindrop),
        (8, Strings.EffectMatrix)
    };

    internal static readonly (int Mode, string Name)[] HidSingleZoneEffects =
    {
        (9, Strings.EffectStatic),
        (0, Strings.EffectBreathing)
    };

    internal static int LightEffectColorMode(string effect)
    {
        if (effect == "Single" || effect == "Raindrop")
        {
            return 1;
        }
        if (effect == "Mix")
        {
            return 1;
        }
        if (effect == "Rainbow")
        {
            return 7;
        }
        if (effect == "Breathing" || effect == "Impact" || effect == "ColorMarquee")
        {
            return 7;
        }
        return 0;
    }

    internal static (string Effect, string Name)[] CurrentLogoEffects()
    {
        MechrevoHw? hw = WpfBackend.Hw;
        bool gen40;
        if (hw != null)
        {
            gen40 = !hw.UsesBaolongda && hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40;
        }
        else
        {
            gen40 = !BaolongdaBackend.IsBaolongdaModelWhitelisted(out _)
                && string.Equals(AppConfig.GetString("dgpu_generation", null), "Gen40", StringComparison.OrdinalIgnoreCase);
        }
        return gen40 ? LogoEffectsGen40 : LogoEffects;
    }

    internal static (int Mode, string Name)[] VisibleHidEffects(int keyboardType)
    {
        return KeyboardFirmwareEffects.IsSingleZoneRgb(keyboardType) ? HidSingleZoneEffects : HidEffects;
    }

    internal static readonly (string Label, int Mins, int IdleSeconds)[] LightingCloseTimerOptions =
    {
        (Strings.Off, 0, 0),
        (Strings.IdleOff10Sec, 0, 10),
        (Strings.IdleOff15Sec, 0, 15),
        (Strings.IdleOff20Sec, 0, 20),
        (Strings.IdleOff30Sec, 0, 30),
        (Strings.IdleOff45Sec, 0, 45),
        (Strings.IdleOff1Min, 1, 60),
        (Strings.IdleOff2Min, 2, 120),
        (Strings.IdleOff5Min, 5, 300),
        (Strings.IdleOff10Min, 10, 600),
        (Strings.IdleOff15Min, 15, 900),
        (Strings.IdleOff20Min, 20, 1200),
        (Strings.IdleOff30Min, 30, 1800),
        (Strings.IdleOff45Min, 45, 2700),
        (Strings.IdleOff1Hour, 60, 3600),
        (Strings.IdleOff2Hour, 120, 7200),
        (Strings.IdleOff3Hour, 180, 10800)
    };

    internal static readonly (string Label, int Seconds)[] KeyboardAutoOffOptions =
    {
        (Strings.Off, 0),
        (Strings.IdleOff10Sec, 10),
        (Strings.IdleOff15Sec, 15),
        (Strings.IdleOff20Sec, 20),
        (Strings.IdleOff30Sec, 30),
        (Strings.IdleOff45Sec, 45),
        (Strings.IdleOff1Min, 60),
        (Strings.IdleOff2Min, 120),
        (Strings.IdleOff5Min, 300),
        (Strings.IdleOff10Min, 600),
        (Strings.IdleOff15Min, 900),
        (Strings.IdleOff20Min, 1200),
        (Strings.IdleOff30Min, 1800),
        (Strings.IdleOff45Min, 2700),
        (Strings.IdleOff1Hour, 3600),
        (Strings.IdleOff2Hour, 7200),
        (Strings.IdleOff3Hour, 10800)
    };
}
