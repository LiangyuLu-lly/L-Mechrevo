using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MechrevoLite;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>自定义/性能模式的可见性判定（与旧 UI 等价）。</summary>
internal static class PerfModeCapabilities
{
    internal static bool IsOffered(PerfModeDefinition mode, MechrevoHw? hw)
    {
        if (L_Mechrevo.WpfP0.Services.WpfBackend.UiAuditMode || hw == null)
        {
            return true;
        }
        if (IsGen40Plus(hw) && mode.Kind != PerfModeKind.Custom)
        {
            return false;
        }
        return mode.Kind switch
        {
            PerfModeKind.SilentTurbo => hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported,
            PerfModeKind.Turbo => hw.Capabilities.TurboMode
                || (hw.FanStatusSeen && hw.DgpuGeneration != DgpuGenerationKind.Gen30),
            _ => true
        };
    }

    internal static bool IsGen40Plus(MechrevoHw? hw)
    {
        try
        {
            string? generation = AppConfig.GetString("dgpu_generation", null);
            if (!string.IsNullOrWhiteSpace(generation))
            {
                Match genMatch = Regex.Match(generation, @"(\d{2})", RegexOptions.IgnoreCase);
                if (genMatch.Success
                    && int.TryParse(genMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int gen)
                    && gen >= 40)
                {
                    return true;
                }
            }
            if (hw != null && (hw.DgpuGeneration == DgpuGenerationKind.Gen40 || hw.DgpuGeneration == DgpuGenerationKind.Gen50))
            {
                return true;
            }
            DgpuGenerationKind detected = GpuGenerationProvider.Current().Generation;
            return detected == DgpuGenerationKind.Gen40 || detected == DgpuGenerationKind.Gen50;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("GPU generation detection failed: " + ex.Message);
        }
        return false;
    }
}
