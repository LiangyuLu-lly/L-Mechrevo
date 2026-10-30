using System;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

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

    /// <summary>NUC 原厂 Benchmark/极致狂暴档；EC 风扇模式与狂暴同为性能表，靠本标志区分。</summary>
    public const int ExtremeVisualMode = 5;

    private static int _lastAppliedVisualMode = -1;

    /// <summary>当前是否由本进程切到极致狂暴（EC 1963 与普通狂暴同为 1，无法靠回读区分）。</summary>
    public static bool IsExtremeActive => _lastAppliedVisualMode == ExtremeVisualMode;

    internal static void ClearExtremeMarker()
    {
        if (_lastAppliedVisualMode == ExtremeVisualMode) _lastAppliedVisualMode = -1;
    }

    // 各档在 EC 中的默认 PL1/PL2 来源地址
    private static (int Pl1, int Pl2) DefaultRegisters(int visualMode) => visualMode switch
    {
        1 => (1959, 1960), // Performance
        2 => (1844, 1845), // BatterySaver
        _ => (1840, 1841)  // Balanced
    };

    private static int Pl4DefaultRegister(int visualMode) => visualMode switch
    {
        1 or 5 => 1961, // Performance / Benchmark
        2 => 1846,     // BatterySaver
        _ => 1842     // Balanced
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
        _lastAppliedVisualMode = visualMode;
        byte pl1;
        byte pl2;
        bool extreme = visualMode == ExtremeVisualMode;
        if (extreme)
        {
            // 原厂 Benchmark 档：固定 PL1=100W、PL2=110W，CPU 不再按普通狂暴的 EC 默认值。
            pl1 = 100;
            pl2 = 110;
            detail = "极致狂暴 PL1=100 PL2=110 ";
        }
        else
        {
            (int pl1Reg, int pl2Reg) = DefaultRegisters(visualMode);
            if (!TryReadPower(pl1Reg, out pl1) || !TryReadPower(pl2Reg, out pl2))
            {
                (pl1, pl2) = Fallback(visualMode);
                detail = "默认寄存器读取失败，使用回退值 ";
            }
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
        // 按官方控制台顺序：功耗墙 → GPU DState → 模式字节 1963。
        // 模式灯由固件按模式字节接管，不再手动写 1957。
        bool fanMode = TryApplyFanMode(visualMode, out string fanDetail);
        bool fanTable = TryApplyFanTableForMode(visualMode, out string fanTableDetail);
        bool dstate = TryApplyDState(visualMode, out string dstateDetail);
        // 非自定义档：清除 Custom Mode 标志，交还 EC 原生模式。
        TryWrite(1798, 64);
        bool read1 = TryRead(Pl1WriteAddress, out int rb1);
        bool read2 = TryRead(Pl2WriteAddress, out int rb2);
        bool read3 = TryRead(Pl4WriteAddress, out int rb3);
        detail += $"PL1={pl1} PL2={pl2} PL4={pl4} 写入={write1 && write2 && write3} 回读={rb1}/{rb2}/{rb3} "
            + fanDetail + " " + fanTableDetail + " " + dstateDetail;
        // 风扇表失败不判整个模式失败，PL/模式字节仍按官方路径生效。
        return write1 && write2 && write3 && read1 && read2 && read3
            && fanMode && dstate && rb1 == pl1 && rb2 == pl2 && rb3 == pl4;
    }

    /// <summary>自定义档：PL1/PL2/PL4 直接写自定义 EC 槽（1923/1924/1925）。</summary>
    public static bool TryApplyCustom(int? pl1, int? pl2, int? pl4, int? tcc, out string detail)
    {
        detail = "";
        _lastAppliedVisualMode = -1;
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
        // CPU 温度墙：Intel 30 系 TjMax=100℃，EC 写的是 TCC offset：
        //   启用 = 0x80 | (100 - target)
        //   关闭 = 清除 bit7，保留/回落到安全 offset（95℃）
        bool tccOk = true;
        int tccRaw = 0;
        if (CpuVendor.IsIntel)
        {
            if (tcc.HasValue)
            {
                // 11800H TjMax = 100℃；EC 存 TCC offset。
                int target = Math.Clamp(tcc.Value, 60, 100);
                tccRaw = 0x80 | ((100 - target) & 0x7F);
            }
            else
            {
                int current = 0;
                bool read = TryRead(1926, out current);
                int factoryOffset = read ? (current & 0x7F) : -1;
                // 0 是合法值（默认温度墙 100℃），不能把 0 当成无效值改成 5。
                tccRaw = (factoryOffset is >= 0 and <= 30) ? factoryOffset : 5;
            }
        }
        else
        {
            // AMD 口径：EC 接受字面摄氏度，bit7 为启用位。
            if (tcc.HasValue)
            {
                tccRaw = 0x80 | (Math.Clamp(tcc.Value, 60, 105) & 0x7F);
            }
            else
            {
                int current = 0;
                bool read = TryRead(1926, out current);
                int factoryCelsius = read ? (current & 0x7F) : -1;
                tccRaw = (factoryCelsius is >= 60 and <= 105) ? factoryCelsius : 95;
            }
        }
        tccOk = TryWrite(1926, tccRaw);
        // 触发字节 bit5=0x20 让 EC 重新锁存自定义功耗配置；保留 USB 充电 bit4。
        if (TryRead(1895, out int trigger))
        {
            TryWrite(1895, (trigger & 0x10) | 0x20);
        }
        bool tccRead = TryRead(1926, out int tccBack);
        // 官方只要求写入成功；EC 对 TCC 的回读可能是掩码后的值，不能因此判整个自定义档失败。
        tccOk = tccOk && tccRead;
        detail = $"自定义 PL1={p1} PL2={p2} PL4={p4} 写入={w1 && w2 && w3} 回读={rb1}/{rb2}/{rb3} · TCC=0x{tccRaw:X2}({(tcc.HasValue ? "开" : "关")}) 写入={tccOk} 回读=0x{tccBack:X2}";
        return w1 && w2 && w3 && r1 && r2 && r3 && tccOk && rb1 == p1 && rb2 == p2 && rb3 == p4;
    }

    private static bool TryApplyModeLed(int visualMode, out string detail)
    {
        detail = "";
        // 原厂 Power LED 状态在 1957 低 2 位，不是 1963。
        const int LedAddress = 1957;
        if (!TryRead(LedAddress, out int current) || current < 0 || current > 255)
        {
            detail = "ModeLED 读取失败";
            return false;
        }
        int mode = visualMode switch
        {
            0 => 2, // 平衡
            1 => 1, // 狂暴
            2 => 3, // 静音
            _ => 0
        };
        int ledBits = mode switch
        {
            1 => 1,
            2 => 0,
            3 => 2,
            _ => 0
        };
        int value = (current & 0xFC) | ledBits;
        bool write = TryWrite(LedAddress, value);
        bool read = TryRead(LedAddress, out int after);
        detail = $"ModeLED mode={mode} bits={ledBits} 写入={write} 回读=0x{after:X2}";
        return write && read && (after & 0x03) == ledBits;
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
            1 or 5 => 1, // Performance / Benchmark（原厂 Benchmark 也写性能风扇表）
            2 => 3,      // Quiet
            _ => 2       // Standard
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
        int defaultAddress = visualMode switch { 1 or 5 => 1962, 2 => 1847, _ => 1843 };
        int defaultDState;
        if (visualMode == ExtremeVisualMode)
        {
            // 原厂 Benchmark profile 固定 GPU DState=1。
            defaultDState = 1;
        }
        else if (!TryRead(defaultAddress, out defaultDState) || defaultDState < 0 || defaultDState > 7)
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

    /// <summary>读 EC 1963 低 4 位，换算成 LM 视觉模式：1=狂暴、2=平衡、3=静音。</summary>
    private static bool TryApplyFanTableForMode(int visualMode, out string detail)
    {
        detail = "";
        try
        {
            bool ok = NucFanTable.TryApplyDefaultMode(visualMode, out detail);
            Logger.WriteLine("[nuc-mode] 默认风扇表 mode=" + visualMode + " ok=" + ok + " · " + detail);
            return ok;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name + " " + ex.Message;
            return false;
        }
    }

    public static bool TryReadVisualMode(out int visualMode)
    {
        visualMode = -1;
        if (!TryRead(FanModeAddress, out int raw) || raw < 0) return false;
        visualMode = (raw & 0x0F) switch
        {
            1 => 1,
            2 => 0,
            3 => 2,
            _ => -1
        };
        return visualMode >= 0;
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
