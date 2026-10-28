using System;

namespace MechrevoLite.Hardware;

/// <summary>非 10/16/20/30 系的 EC 三档充电上限，按 OR 平台门控顺序写入。</summary>
internal static class EcChargeLimitOverride
{
    private const int UpperRegister = 1977;   // 0x7B9
    private const int LowerRegister = 2000;   // 0x7D0
    private const int OverrideRegister = 1904; // 0x770
    private const int PlatformIdRegister = 1987; // 0x7C3
    private const int ProtectionModeRegister = 1958; // 0x7A6
    private const int TouchpadBit = 0x08;

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
            int mode = percent >= 95 ? 0x01 : percent <= 65 ? 0x21 : 0x11;
            bool platform = AcpiDriverEc.TryWriteByte(PlatformIdRegister,
                percent < 95 ? (byte)0x04 : (byte)0x07);
            bool ov = AcpiDriverEc.TryWriteByte(OverrideRegister, 0xFF);
            bool up = AcpiDriverEc.TryWriteByte(UpperRegister, (byte)percent);
            int lower = percent > 5 ? percent - 5 : percent;
            bool down = AcpiDriverEc.TryWriteByte(LowerRegister, (byte)lower);
            bool modeOk = false;
            if (AcpiDriverEc.TryReadByte(ProtectionModeRegister, out byte protection))
            {
                modeOk = AcpiDriverEc.TryWriteByte(ProtectionModeRegister,
                    (byte)((protection & TouchpadBit) | mode));
            }
            bool read = AcpiDriverEc.TryReadByte(UpperRegister, out byte after);
            int actual = after & 0x7F;
            detail = $"上限={percent}% 平台ID={platform} 覆盖复位={ov} 上限写入={up} 下限写入={down} 模式=0x{mode:X2}({modeOk}) 回读={after:X2}（{actual}%）";
            return platform && up && down && modeOk && read && actual == percent;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name;
            return false;
        }
    }
}
