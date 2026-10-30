using System;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>
/// NUC/UMI 电池保护档：官方控制台通过 EC 0x07B9 写充电上限。
/// 三档映射：长效=100%、平衡=80%、工作站=60%；无极限充也走同一寄存器。
/// </summary>
internal static class NucBatteryMode
{
    private const int Address = 1977; // 0x07B9 · charging level / limit
    private const int ReachedBit = 0x80;

    internal static bool TrySet(int mode, out string detail)
    {
        int percent = mode switch { 0 => 100, 1 => 80, 2 => 60, _ => -1 };
        if (percent < 0)
        {
            detail = "档位越界";
            return false;
        }
        return TrySetPercent(percent, out detail);
    }

    internal static bool TrySetPercent(int percent, out string detail)
    {
        detail = "";
        int target = Math.Clamp(percent, 1, 100);
        if (!AcpiDriverEc.TryReadByte(Address, out byte current))
        {
            detail = "读取 0x07B9 失败";
            return false;
        }
        int value = (current & ReachedBit) | target;
        bool write = AcpiDriverEc.TryWriteByte(Address, value);
        bool read = AcpiDriverEc.TryReadByte(Address, out byte after);
        int readback = after & 0x7F;
        detail = $"BatteryLimit={target}% 写入={write} 回读={readback}%";
        return write && read && readback == target;
    }

    internal static bool TryReadPercent(out int percent)
    {
        percent = -1;
        if (!AcpiDriverEc.TryReadByte(Address, out byte value))
        {
            return false;
        }
        int raw = value & 0x7F;
        percent = raw is >= 1 and <= 99 ? raw : 100;
        return true;
    }

    internal static bool TryReadMode(out int mode)
    {
        mode = -1;
        if (!TryReadPercent(out int percent))
        {
            return false;
        }
        mode = percent >= 90 ? 0 : percent >= 70 ? 1 : 2;
        return true;
    }
}
