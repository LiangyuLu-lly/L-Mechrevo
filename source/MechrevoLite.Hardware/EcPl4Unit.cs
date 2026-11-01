using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// Intel 星耀 PL4 单位口径（EC 0x0727 bit7）。
/// <para>bit7=1 ⇒ PL4 按 2W/LSB；bit7=0 ⇒ 1W/LSB。</para>
/// <para>⚠️ 实验性：只改这一个位，保留其它位；写完立即回读。</para>
/// <para>⚠️ PL1/PL2 不受该位影响，恒为 1W/LSB。</para>
/// </summary>
internal static class EcPl4Unit
{
    internal const int Register = 0x0727;
    internal const int Pl4Register = 0x0785;
    internal const byte Bit7 = 0x80;

    internal static bool IsSupported
    {
        get
        {
            try
            {
                return CpuVendor.IsIntel
                    && !XingyaoPmc2.IsXingyaoPlatform()
                    && MechrevoHw.IsGcuServiceInstalled
                    && AcpiDriverEc.TryReadByte(Register, out _);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static bool TryRead(out bool doubleUnit, out byte raw)
    {
        doubleUnit = false;
        raw = 0;
        if (!AcpiDriverEc.TryReadByte(Register, out raw))
        {
            return false;
        }
        doubleUnit = (raw & Bit7) != 0;
        return true;
    }

    internal static bool TryReadPl4(out int watts, out byte raw, out bool doubleUnit)
    {
        watts = 0;
        raw = 0;
        doubleUnit = false;
        if (!TryRead(out doubleUnit, out _))
        {
            return false;
        }
        if (!AcpiDriverEc.TryReadByte(Pl4Register, out raw))
        {
            return false;
        }
        watts = raw * (doubleUnit ? 2 : 1);
        return true;
    }

    internal static bool TrySetDouble(bool doubleUnit, out string detail)
    {
        detail = "";
        if (!AcpiDriverEc.TryReadByte(Register, out byte before))
        {
            detail = "0x0727 读取失败";
            return false;
        }
        bool wasDouble = (before & Bit7) != 0;
        if (wasDouble == doubleUnit)
        {
            detail = "已是" + (doubleUnit ? "2W/LSB" : "1W/LSB") + "，未写入";
            return true;
        }
        byte target = (byte)(doubleUnit ? (before | Bit7) : (before & ~Bit7));
        if (!AcpiDriverEc.TryWriteByte(Register, target))
        {
            detail = "0x0727 写入失败";
            return false;
        }
        if (!AcpiDriverEc.TryReadByte(Register, out byte after))
        {
            detail = "0x0727 写后读取失败";
            return false;
        }
        bool ok = ((after & Bit7) != 0) == doubleUnit;
        detail = "0x0727 " + before.ToString("X2") + "→" + after.ToString("X2")
            + " · PL4单位=" + (ok ? (doubleUnit ? "2W/LSB" : "1W/LSB") : "回读不一致");
        Logger.WriteError("[pl4-unit] " + detail);
        return ok;
    }
}
