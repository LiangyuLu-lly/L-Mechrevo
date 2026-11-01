using System;
using System.Collections.Generic;
using System.Linq;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>设置页/电池/电源相关的中性文案与判定，与旧 UI 同文案同顺序。</summary>
internal static class SettingsText
{
    internal static string PowerSourceHeadline(PowerInputKind kind, int? adapterWatts)
    {
        switch (kind)
        {
            case PowerInputKind.Battery:
                return Strings.PowerSourceBattery;
            case PowerInputKind.Barrel:
            case PowerInputKind.BarrelAndTypeC:
                return adapterWatts.HasValue
                    ? string.Format(Strings.PowerSourceBarrelWatts, PowerInputDecoder.DisplayAdapterWatts(adapterWatts.GetValueOrDefault()))
                    : Strings.PowerSourceBarrel;
            case PowerInputKind.TypeC:
                return Strings.PowerSourceTypeC;
            default:
                return Strings.PowerSourceExternal;
        }
    }

    internal static string BatteryHealthText(MechrevoHw? hw)
    {
        if (hw == null)
        {
            return "";
        }
        List<string> list = new List<string>(2);
        if (hw.BatteryCycleCount >= 0)
        {
            list.Add(string.Format(Strings.BatteryCycles, hw.BatteryCycleCount));
        }
        if (HasMeaningfulCapacity(hw.BatteryCapacityText))
        {
            list.Add(hw.BatteryCapacityText);
        }
        return string.Join(" · ", list);
    }

    internal static bool HasMeaningfulCapacity(string? capacityText)
    {
        if (string.IsNullOrWhiteSpace(capacityText))
        {
            return false;
        }
        string numbers = new string(capacityText.TakeWhile(c => char.IsDigit(c) || char.IsWhiteSpace(c)).Where(char.IsDigit).ToArray());
        return numbers.Length > 0 && long.TryParse(numbers, out long value) && value > 0;
    }
}
