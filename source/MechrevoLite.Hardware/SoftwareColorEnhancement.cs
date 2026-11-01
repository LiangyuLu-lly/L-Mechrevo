using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

/// <summary>软件色彩增强：gamma ramp + Magnification 5x5 色彩矩阵。</summary>
internal sealed record SoftwareColorPreset(
    string Id,
    int ColorTempK,
    float GainR,
    float GainG,
    float GainB,
    float Saturation,
    float Contrast,
    float Gamma);

internal static class SoftwareColorEnhancement
{
    private static readonly object Gate = new();
    private static bool _magInitialized;
    private static IntPtr _magLibrary;
    private static MagSetEffectDelegate? _magSetEffect;
    private static MagUninitDelegate? _magUninit;

    [StructLayout(LayoutKind.Sequential)]
    private struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MagColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] Transform;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MagSetEffectDelegate(ref MagColorEffect effect);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MagInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MagUninitDelegate();

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

    private static readonly SoftwareColorPreset[] Presets =
    {
        new("VibrantMode", 6500, 1f, 1f, 1f, 1.5f, 1f, 1f),
        new("InternetMode", 6500, 98f/128f, 88f/128f, 114f/128f, 1f, 1f, 1f),
        new("VideoMode", 6500, 110f/128f, 119f/128f, 114f/128f, 1f, 1f, 1f),
        new("LowBlueMode", 5000, 1f, 1f, 1f, 1f, 1f, 1f),
        new("CinemaMode", 6000, 107f/128f, 119f/128f, 116f/128f, 1f, 1f, 1f),
        new("PhotoMode", 6500, 98f/128f, 129f/128f, 114f/128f, 1f, 1f, 1f)
    };

    internal static bool Apply(string? mode)
    {
        lock (Gate)
        {
            SoftwareColorPreset? preset = null;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                foreach (SoftwareColorPreset item in Presets)
                {
                    if (string.Equals(item.Id, mode, StringComparison.OrdinalIgnoreCase))
                    {
                        preset = item;
                        break;
                    }
                }
            }
            preset ??= Presets[0];
            bool gammaOk = ApplyGamma(preset);
            bool matrixOk = ApplyMatrix(preset);
            return gammaOk || matrixOk;
        }
    }

    /// <summary>当前色彩档位的默认编辑参数（用于编辑器“恢复默认”，不是统一回多彩）。</summary>
    internal static DisplayEffectProfile DefaultProfile(string? mode)
    {
        SoftwareColorPreset preset = FindPreset(mode) ?? Presets[0];
        return new DisplayEffectProfile
        {
            Name = preset.Id,
            Brightness = 50,
            ColorTemp = preset.ColorTempK,
            ColorR = (float)Math.Round(preset.GainR * 128f),
            ColorG = (float)Math.Round(preset.GainG * 128f),
            ColorB = (float)Math.Round(preset.GainB * 128f),
            VibrantValue = preset.Saturation,
            Constrast = preset.Contrast,
            Gamma = preset.Gamma
        };
    }

    private static SoftwareColorPreset? FindPreset(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        foreach (SoftwareColorPreset item in Presets)
        {
            if (string.Equals(item.Id, mode, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    internal static bool ApplyProfile(string name, int brightness, int colorTemp, float r, float g, float b, float vibrant, float contrast, float gamma)
    {
        lock (Gate)
        {
            SoftwareColorPreset preset = new(
                name,
                colorTemp <= 0 ? 6500 : colorTemp,
                NormalizeGain(r),
                NormalizeGain(g),
                NormalizeGain(b),
                vibrant <= 0 ? 1f : vibrant,
                contrast <= 0 ? 1f : contrast,
                gamma <= 0 ? 1f : gamma);
            bool gammaOk = ApplyGamma(preset);
            bool matrixOk = ApplyMatrix(preset);
            return gammaOk || matrixOk;
        }
    }

    internal static bool Reset()
    {
        lock (Gate)
        {
            GammaRamp linear = new()
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };
            for (int i = 0; i < 256; i++)
            {
                ushort v = (ushort)Math.Round(i / 255.0 * 65535.0);
                linear.Red[i] = v;
                linear.Green[i] = v;
                linear.Blue[i] = v;
            }
            bool gammaOk = ApplyGammaRamp(linear);
            bool matrixOk = ApplyMatrix(new SoftwareColorPreset("identity", 6500, 1f, 1f, 1f, 1f, 1f, 1f), identity: true);
            return gammaOk || matrixOk;
        }
    }

    private static bool ApplyGamma(SoftwareColorPreset preset)
    {
        (float tr, float tg, float tb) = KelvinToRgb(preset.ColorTempK);
        float fr = Math.Clamp(preset.GainR, 0f, 2f) * tr;
        float fg = Math.Clamp(preset.GainG, 0f, 2f) * tg;
        float fb = Math.Clamp(preset.GainB, 0f, 2f) * tb;
        float gamma = Math.Clamp(preset.Gamma, 0.5f, 2f);
        float contrast = Math.Clamp(preset.Contrast, 0.5f, 2f);
        GammaRamp ramp = new() { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
        for (int i = 0; i < 256; i++)
        {
            float x = i / 255f;
            ushort Calc(float gain)
            {
                float curved = (float)Math.Pow(x, 1.0 / gamma);
                float adjusted = (curved - 0.5f) * contrast + 0.5f;
                float val = Math.Clamp(adjusted * gain, 0f, 1f);
                return (ushort)Math.Round(val * 65535f);
            }
            ramp.Red[i] = Calc(fr);
            ramp.Green[i] = Calc(fg);
            ramp.Blue[i] = Calc(fb);
        }
        return ApplyGammaRamp(ramp);
    }

    private static bool ApplyGammaRamp(GammaRamp ramp)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }
        int size = Marshal.SizeOf<GammaRamp>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(ramp, ptr, false);
            return SetDeviceGammaRamp(hdc, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static float NormalizeGain(float value)
    {
        // 编辑器/GCU 的 RGB 可能是 0..255；软件矩阵内部是 0..2。
        if (value > 2.5f) return Math.Clamp(value / 128f, 0f, 2f);
        return Math.Clamp(value, 0f, 2f);
    }

    private static bool ApplyMatrix(SoftwareColorPreset preset, bool identity = false)
    {
        if (!EnsureMag())
        {
            return false;
        }
        float s = identity ? 1f : Math.Clamp(preset.Saturation, 0f, 2.5f);
        (float tr, float tg, float tb) = KelvinToRgb(preset.ColorTempK);
        float gr = Math.Clamp(preset.GainR, 0f, 2f) * tr;
        float gg = Math.Clamp(preset.GainG, 0f, 2f) * tg;
        float gb = Math.Clamp(preset.GainB, 0f, 2f) * tb;
        float contrast = Math.Clamp(preset.Contrast, 0.5f, 2f);
        const float wr = 0.2126f, wg = 0.7152f, wb = 0.0722f;
        MagColorEffect effect = new() { Transform = new float[25] };
        effect.Transform[0] = ((1f - s) * wr + s) * gr * contrast;
        effect.Transform[5] = ((1f - s) * wg) * gr * contrast;
        effect.Transform[10] = ((1f - s) * wb) * gr * contrast;
        effect.Transform[1] = ((1f - s) * wr) * gg * contrast;
        effect.Transform[6] = ((1f - s) * wg + s) * gg * contrast;
        effect.Transform[11] = ((1f - s) * wb) * gg * contrast;
        effect.Transform[2] = ((1f - s) * wr) * gb * contrast;
        effect.Transform[7] = ((1f - s) * wg) * gb * contrast;
        effect.Transform[12] = ((1f - s) * wb + s) * gb * contrast;
        effect.Transform[18] = 1f;
        float offset = 0.5f * (1f - contrast);
        effect.Transform[20] = offset;
        effect.Transform[21] = offset;
        effect.Transform[22] = offset;
        effect.Transform[24] = 1f;
        return _magSetEffect!(ref effect);
    }

    private static bool EnsureMag()
    {
        if (_magSetEffect != null)
        {
            return true;
        }
        try
        {
            _magLibrary = NativeLibrary.Load("Magnification.dll");
            IntPtr initPtr = NativeLibrary.GetExport(_magLibrary, "MagInitialize");
            IntPtr setPtr = NativeLibrary.GetExport(_magLibrary, "MagSetFullscreenColorEffect");
            IntPtr uninitPtr = NativeLibrary.GetExport(_magLibrary, "MagUninitialize");
            MagInitDelegate init = Marshal.GetDelegateForFunctionPointer<MagInitDelegate>(initPtr);
            _magSetEffect = Marshal.GetDelegateForFunctionPointer<MagSetEffectDelegate>(setPtr);
            _magUninit = Marshal.GetDelegateForFunctionPointer<MagUninitDelegate>(uninitPtr);
            if (!_magInitialized)
            {
                _magInitialized = init();
            }
            return _magSetEffect != null;
        }
        catch
        {
            return false;
        }
    }

    private static (float R, float G, float B) KelvinToRgb(int kelvin)
    {
        if (kelvin == 6500) return (1f, 1f, 1f);
        static (float R, float G, float B) Raw(int k)
        {
            float temp = Math.Clamp(k, 1000, 40000) / 100f;
            float r = temp <= 66f ? 255f : 329.698727446f * (float)Math.Pow(temp - 60f, -0.1332047592);
            float g = temp <= 66f ? 99.4708025861f * (float)Math.Log(temp) - 161.1195681661f
                : 288.1221695283f * (float)Math.Pow(temp - 60f, -0.0755148492);
            float b = temp >= 66f ? 255f : temp <= 19f ? 0f : 138.5177312231f * (float)Math.Log(temp - 10f) - 305.0447927307f;
            return (Math.Clamp(r, 0f, 255f), Math.Clamp(g, 0f, 255f), Math.Clamp(b, 0f, 255f));
        }
        var d65 = Raw(6500);
        var raw = Raw(kelvin);
        return (Math.Clamp(raw.R / d65.R, 0f, 2f), Math.Clamp(raw.G / d65.G, 0f, 2f), Math.Clamp(raw.B / d65.B, 0f, 2f));
    }
}
