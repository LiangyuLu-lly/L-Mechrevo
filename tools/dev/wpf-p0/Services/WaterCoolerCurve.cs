using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MechrevoLite;

namespace L_Mechrevo.WpfP0.Services;

internal sealed record WaterCoolerCurvePoint(int Temp, int FanDuty);

/// <summary>水冷风扇曲线：主机侧插值，不写设备曲线表。</summary>
internal static class WaterCoolerCurve
{
    internal const string PointsKey = "lc_curve_points";
    internal const string EnabledKey = "lc_curve_enabled";
    internal const string PumpStartKey = "lc_curve_pump_start";
    internal const string PumpTurboKey = "lc_curve_pump_turbo";

    internal static int PumpStartTemp
    {
        get => Math.Clamp(AppConfig.Get(PumpStartKey, 55), 30, 95);
        set
        {
            AppConfig.Set(PumpStartKey, Math.Clamp(value, 30, 95));
            AppConfig.Flush();
        }
    }

    internal static int PumpTurboTemp
    {
        get => Math.Clamp(AppConfig.Get(PumpTurboKey, 90), 35, 100);
        set
        {
            AppConfig.Set(PumpTurboKey, Math.Clamp(value, 35, 100));
            AppConfig.Flush();
        }
    }

    internal static readonly int[] DefaultTemps = { 45, 55, 65, 80, 90, 95 };
    internal static readonly int[] DefaultDuties = { 0, 35, 50, 60, 75, 100 };

    internal static IReadOnlyList<WaterCoolerCurvePoint> DefaultPoints =>
        DefaultTemps.Select((t, i) => new WaterCoolerCurvePoint(t, DefaultDuties[i])).ToArray();

    internal static IReadOnlyList<WaterCoolerCurvePoint> Load()
    {
        string? raw = AppConfig.GetString(PointsKey, null);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultPoints;
        }
        try
        {
            List<WaterCoolerCurvePoint> points = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    string[] pair = part.Split(':');
                    if (pair.Length != 2
                        || !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int temp)
                        || !int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int duty))
                    {
                        throw new FormatException(part);
                    }
                    return new WaterCoolerCurvePoint(Math.Clamp(temp, 30, 100), Math.Clamp(duty, 0, 100));
                })
                .OrderBy(p => p.Temp)
                .ToList();
            if (points.Count < 2)
            {
                return DefaultPoints;
            }
            return points;
        }
        catch
        {
            return DefaultPoints;
        }
    }

    internal static void Save(IReadOnlyList<WaterCoolerCurvePoint> points)
    {
        string raw = string.Join(';', points
            .OrderBy(p => p.Temp)
            .Select(p => p.Temp.ToString(CultureInfo.InvariantCulture) + ":" + p.FanDuty.ToString(CultureInfo.InvariantCulture)));
        AppConfig.Set(PointsKey, raw);
        AppConfig.Flush();
    }

    internal static bool Enabled
    {
        get => AppConfig.Is(EnabledKey);
        set
        {
            AppConfig.Set(EnabledKey, value ? 1 : 0);
            AppConfig.Flush();
        }
    }

    internal static int Evaluate(IReadOnlyList<WaterCoolerCurvePoint> points, int temperatureC)
    {
        if (points == null || points.Count == 0)
        {
            return 0;
        }
        List<WaterCoolerCurvePoint> sorted = points.OrderBy(p => p.Temp).ToList();
        if (temperatureC <= sorted[0].Temp)
        {
            return Math.Clamp(sorted[0].FanDuty, 0, 100);
        }
        if (temperatureC >= sorted[^1].Temp)
        {
            return Math.Clamp(sorted[^1].FanDuty, 0, 100);
        }
        for (int i = 1; i < sorted.Count; i++)
        {
            WaterCoolerCurvePoint right = sorted[i];
            if (temperatureC > right.Temp)
            {
                continue;
            }
            WaterCoolerCurvePoint left = sorted[i - 1];
            int span = Math.Max(1, right.Temp - left.Temp);
            double ratio = (temperatureC - left.Temp) / (double)span;
            int duty = (int)Math.Round(left.FanDuty + (right.FanDuty - left.FanDuty) * ratio, MidpointRounding.AwayFromZero);
            return Math.Clamp(duty, 0, 100);
        }
        return Math.Clamp(sorted[^1].FanDuty, 0, 100);
    }

    internal static IReadOnlyList<WaterCoolerCurvePoint> Preset(string name)
    {
        return name switch
        {
            "quiet" => new[]
            {
                new WaterCoolerCurvePoint(45, 0), new WaterCoolerCurvePoint(55, 20),
                new WaterCoolerCurvePoint(65, 30), new WaterCoolerCurvePoint(80, 45),
                new WaterCoolerCurvePoint(90, 60), new WaterCoolerCurvePoint(95, 80)
            },
            "aggressive" => new[]
            {
                new WaterCoolerCurvePoint(45, 30), new WaterCoolerCurvePoint(55, 55),
                new WaterCoolerCurvePoint(65, 70), new WaterCoolerCurvePoint(80, 85),
                new WaterCoolerCurvePoint(90, 95), new WaterCoolerCurvePoint(95, 100)
            },
            "linear" => new[]
            {
                new WaterCoolerCurvePoint(45, 0), new WaterCoolerCurvePoint(55, 20),
                new WaterCoolerCurvePoint(65, 40), new WaterCoolerCurvePoint(80, 60),
                new WaterCoolerCurvePoint(90, 80), new WaterCoolerCurvePoint(95, 100)
            },
            _ => DefaultPoints
        };
    }
}
