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
    private const int FanModeAddress = 1963;  // 0x7AB · 低 4 位风扇模式

    // 各档在 EC 中的默认 PL1/PL2 来源地址
    private static (int Pl1, int Pl2) DefaultRegisters(int visualMode) => visualMode switch
    {
        1 => (1959, 1960), // Performance
        2 => (1844, 1845), // BatterySaver
        _ => (1840, 1841)  // Balanced
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

        bool write1 = EcPortIo.TryWriteByte(Pl1WriteAddress, pl1);
        bool write2 = EcPortIo.TryWriteByte(Pl2WriteAddress, pl2);
        bool fan = TryApplyFanMode(visualMode, out string fanDetail);
        detail += " " + fanDetail;
        bool read1 = EcPortIo.TryReadByteStable(Pl1WriteAddress, out int rb1, out _);
        bool read2 = EcPortIo.TryReadByteStable(Pl2WriteAddress, out int rb2, out _);
        detail += $"PL1={pl1} PL2={pl2} 写入={write1 && write2} 回读={rb1}/{rb2}";
        return write1 && write2 && read1 && read2 && fan && rb1 == pl1 && rb2 == pl2;
    }

    private static bool TryApplyFanMode(int visualMode, out string detail)
    {
        detail = "";
        if (!EcPortIo.TryReadByteStable(FanModeAddress, out int current, out _) || current < 0 || current > 255)
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
        bool write = EcPortIo.TryWriteByte(FanModeAddress, value);
        bool read = EcPortIo.TryReadByteStable(FanModeAddress, out int back, out _);
        detail += $"FanMode=0x{value:X2} 写入={write} 回读=0x{back:X2}";
        return write && read && back == value;
    }

    private static bool TryReadPower(int address, out byte value)
    {
        value = 0;
        if (!EcPortIo.TryReadByteStable(address, out int raw, out _) || raw < 0 || raw > 255)
        {
            return false;
        }
        value = (byte)raw;
        return true;
    }
}
