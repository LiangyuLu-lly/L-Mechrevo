using System;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>Windows 电池百分比（优先系统口径，供宝龙达等读不到电池遥测的机型兜底）。</summary>
internal static class WindowsBattery
{
    internal static int ReadPercent()
    {
        try
        {
            System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
            float life = power.BatteryLifePercent;
            if (life >= 0f && life <= 1f)
            {
                return (int)Math.Round(life * 100f);
            }
        }
        catch
        {
        }
        return -1;
    }
}
