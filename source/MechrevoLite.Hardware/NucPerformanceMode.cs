using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// NUC / UMI 原生性能档：直接写 EC 的 PL1/PL2，不再经过 GCU 的模式切换。
/// 只使用已经确认的 EC 地址；风扇/GPU 仍由 EC 现有策略处理。
/// </summary>
public static class NucPerformanceMode
{
    private const int Pl1WriteAddress = 1923; // 0x783
    private const int Pl2WriteAddress = 1924; // 0x784
    private const int Pl4WriteAddress = 1925; // 0x785
    private const int FanModeAddress = 1963;  // 0x7AB · 低 4 位风扇模式
    private const int FanBoostAddress = 1873; // 0x751 · 风扇增强
    private const int DStateAddress = 1931;   // 0x78B · GPU D-State 低 3 位

    // 各档在 EC 中的默认 PL1/PL2 来源地址
    private static (int Pl1, int Pl2) DefaultRegisters(int visualMode) => visualMode switch
    {
        1 => (1959, 1960), // Performance
        2 => (1844, 1845), // BatterySaver
        _ => (1840, 1841)  // Balanced
    };

    private static int Pl4DefaultRegister(int visualMode) => visualMode switch
    {
        1 => 1961, // Performance
        2 => 1846, // BatterySaver
        _ => 1842  // Balanced
    };

    private static (byte Pl1, byte Pl2) Fallback(int visualMode) => visualMode switch
    {
        1 => (65, 90),
        2 => (30, 60),
        _ => (45, 65)
    };

    public static bool TryApply(int visualMode, out string detail)
    {
        detail = "";
        (int pl1Reg, int pl2Reg) = DefaultRegisters(visualMode);
        byte pl1;
        byte pl2;
        if (!TryReadPower(pl1Reg, out pl1) || !TryReadPower(pl2Reg, out pl2))
        {
            (pl1, pl2) = Fallback(visualMode);
            detail = "默认寄存器读取失败，使用回退值 ";
        }
        if (pl1 < 5 || pl1 > 200 || pl2 < 5 || pl2 > 250 || pl1 > pl2)
        {
            detail += $"默认值异常 PL1={pl1} PL2={pl2}";
            return false;
        }

        bool write1 = TryWrite(Pl1WriteAddress, pl1);
        bool write2 = TryWrite(Pl2WriteAddress, pl2);
        int pl4 = TryRead(Pl4DefaultRegister(visualMode), out int pl4Default) && pl4Default > 0
            ? pl4Default
            : 125;
        int minPl4 = Math.Min(250, pl2 + 20);
        pl4 = Math.Clamp(pl4, minPl4, 250);
        bool write3 = TryWrite(Pl4WriteAddress, pl4);
        // 模式切换只写功耗墙；风扇模式/强冷/DState 不在这里自动写，
        // 避免不同 NUC/UMI EC 语义下把风扇推到全速。
        // 性能键/电源灯：1963 低 4 位对应 静音=0、平衡=1、狂暴=2 个灯。
        bool led = TryApplyModeLed(visualMode, out string ledDetail);
        // 非自定义档：清除 Custom Mode 标志，交还 EC 原生模式。
        TryWrite(1798, 64);
        bool read1 = TryRead(Pl1WriteAddress, out int rb1);
        bool read2 = TryRead(Pl2WriteAddress, out int rb2);
        bool read3 = TryRead(Pl4WriteAddress, out int rb3);
        detail += $"PL1={pl1} PL2={pl2} PL4={pl4} 写入={write1 && write2 && write3} 回读={rb1}/{rb2}/{rb3} " + ledDetail;
        return write1 && write2 && write3 && read1 && read2 && read3 && led && rb1 == pl1 && rb2 == pl2 && rb3 == pl4;
    }

    /// <summary>自定义档：PL1/PL2/PL4 直接写自定义 EC 槽（1923/1924/1925）。</summary>
    public static bool TryApplyCustom(int? pl1, int? pl2, int? pl4, int? tcc, out string detail)
    {
        detail = "";
        if (!pl1.HasValue || !pl2.HasValue || !pl4.HasValue)
        {
            detail = "自定义功耗墙不完整";
            return false;
        }
        int p1 = Math.Clamp(pl1.Value, 5, 200);
        int p2 = Math.Clamp(pl2.Value, 5, 250);
        if (p1 > p2)
        {
            (p1, p2) = (p2, p1);
        }
        int p4 = Math.Clamp(pl4.Value, Math.Min(250, p2 + 20), 250);
        bool w1 = TryWrite(Pl1WriteAddress, p1);
        bool w2 = TryWrite(Pl2WriteAddress, p2);
        bool w3 = TryWrite(Pl4WriteAddress, p4);
        bool r1 = TryRead(Pl1WriteAddress, out int rb1);
        bool r2 = TryRead(Pl2WriteAddress, out int rb2);
        bool r3 = TryRead(Pl4WriteAddress, out int rb3);
        // 原厂自定义模式标志：1798=65
        TryWrite(1798, 65);
        if (tcc.HasValue)
        {
            TryWrite(1926, Math.Clamp(tcc.Value, 0, 255));
        }
        // 触发字节 bit5=0x20 让 EC 重新锁存自定义功耗配置；保留 USB 充电 bit4。
        if (TryRead(1895, out int trigger))
        {
            TryWrite(1895, (trigger & 0x10) | 0x20);
        }
        detail = $"自定义 PL1={p1} PL2={p2} PL4={p4} 写入={w1 && w2 && w3} 回读={rb1}/{rb2}/{rb3}";
        return w1 && w2 && w3 && r1 && r2 && r3 && rb1 == p1 && rb2 == p2 && rb3 == p4;
    }

    private static bool TryApplyModeLed(int visualMode, out string detail)
    {
        detail = "";
        if (!TryRead(FanModeAddress, out int current) || current < 0 || current > 255)
        {
            detail = "ModeLED 读取失败";
            return false;
        }
        int ledCount = visualMode switch
        {
            2 => 0, // 静音
            0 => 1, // 平衡
            1 => 2, // 狂暴
            _ => 0
        };
        int value = (current & 0xF0) | ledCount;
        bool write = TryWrite(FanModeAddress, value);
        bool read = TryRead(FanModeAddress, out int after);
        detail = $"ModeLED={ledCount} 写入={write} 回读=0x{after:X2}";
        return write && read && (after & 0x0F) == ledCount;
    }

    private static bool TryApplyFanMode(int visualMode, out string detail)
    {
        detail = "";
        if (!TryRead(FanModeAddress, out int current) || current < 0 || current > 255)
        {
            detail = "风扇模式读取失败";
            return false;
        }
        int wanted = visualMode switch
        {
            1 => 1, // Performance
            2 => 3, // Quiet
            _ => 2  // Standard
        };
        int value = (current & 0xF0) | wanted;
        bool write = TryWrite(FanModeAddress, value);
        bool read = TryRead(FanModeAddress, out int back);
        detail += $"FanMode=0x{value:X2} 写入={write} 回读=0x{back:X2}";
        return write && read && back == value;
    }

    private static bool TryApplyFanBoost(int visualMode, out string detail)
    {
        int value = visualMode == 1 ? 64 : 0;
        bool write = TryWrite(FanBoostAddress, value);
        bool read = TryRead(FanBoostAddress, out int back);
        detail = $"FanBoost={value} 写入={write} 回读={back}";
        return write && read && back == value;
    }

    private static bool TryApplyDState(int visualMode, out string detail)
    {
        detail = "";
        int defaultAddress = visualMode switch { 1 => 1962, 2 => 1847, _ => 1843 };
        if (!TryRead(defaultAddress, out int defaultDState) || defaultDState < 0 || defaultDState > 7)
        {
            detail = "DState 默认值读取失败";
            return false;
        }
        if (!TryRead(DStateAddress, out int current) || current < 0 || current > 255)
        {
            detail = "DState 当前值读取失败";
            return false;
        }
        int value = (current & 0xF8) | defaultDState;
        bool write = TryWrite(DStateAddress, value);
        bool read = TryRead(DStateAddress, out int back);
        detail = $"DState 0x{value:X2} 写入={write} 回读=0x{back:X2}";
        return write && read && back == value;
    }

    private static bool TryRead(int address, out int value)
    {
        if (AcpiDriverEc.TryReadByte(address, out byte byteValue))
        {
            value = byteValue;
            return true;
        }
        if (EcPortIo.TryReadByteStable(address, out int legacy, out _))
        {
            value = legacy;
            return true;
        }
        value = -1;
        return false;
    }

    private static bool TryWrite(int address, int value)
    {
        if (AcpiDriverEc.TryWriteByte(address, value))
        {
            return true;
        }
        return EcPortIo.TryWriteByte(address, value);
    }

    private static bool TryReadPower(int address, out byte value)
    {
        value = 0;
        if (!TryRead(address, out int raw) || raw < 0 || raw > 255)
        {
            return false;
        }
        value = (byte)raw;
        return true;
    }
}
