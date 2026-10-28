using System;

namespace MechrevoLite.Hardware;

/// <summary>NUC/UMI 三档充电上限，沿用 OR/同方 EC 写法：0x7B9 上限、0x7D0 复充下限、0x770 平台覆盖复位。</summary>
internal static class NucChargeLimit
{
    private const int UpperRegister = 1977;   // 0x7B9
    private const int LowerRegister = 2000;   // 0x7D0
    private const int OverrideRegister = 1904; // 0x770

    internal static bool TrySet(int percent, out string detail)
    {
        detail = "";
        if (percent < 40 || percent > 100)
        {
            detail = "充电上限越界";
            return false;
        }
        try
        {
            // OR 的顺序：先复位平台覆盖，再写上限和复充下限。
            bool ov = AcpiDriverEc.TryWriteByte(OverrideRegister, 0xFF);
            bool up = AcpiDriverEc.TryWriteByte(UpperRegister, (byte)(percent | 0x80));
            int lower = percent > 5 ? percent - 5 : percent;
            bool down = AcpiDriverEc.TryWriteByte(LowerRegister, (byte)lower);
            bool read = AcpiDriverEc.TryReadByte(UpperRegister, out byte after);
            int actual = (after & 0x7F);
            detail = $"上限={percent}% 覆盖复位={ov} 写入上限={up} 写入下限={down} 回读={after:X2}（{actual}%）";
            return up && read && actual == percent;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name;
            return false;
        }
    }
}
