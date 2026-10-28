using System;

namespace MechrevoLite.Hardware;

/// <summary>NUC/UMI 电池保护档：EC 0x07A6（1958）低 2 位，bit3 保留触摸板。</summary>
internal static class NucBatteryMode
{
    private const int Address = 1958;

    internal static bool TrySet(int mode, out string detail)
    {
        detail = "";
        if (mode < 0 || mode > 2)
        {
            detail = "档位越界";
            return false;
        }
        if (!AcpiDriverEc.TryReadByte(Address, out byte current))
        {
            detail = "读取 0x07A6 失败";
            return false;
        }
        int value = (current & ~0x03) | mode;
        bool write = AcpiDriverEc.TryWriteByte(Address, value);
        bool read = AcpiDriverEc.TryReadByte(Address, out byte after);
        detail = $"BatteryMode={mode} 写入={write} 回读=0x{after:X2}";
        return write && read && (after & 0x03) == mode;
    }
}
