using System;
using System.IO;
using System.Linq;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>NUC/UMI 风扇表：16 段温度/占空比 + 模式默认表 + 自定义曲线。</summary>
internal static class NucFanTable
{
    private const int CpuUptBase = 3840;
    private const int CpuDowntBase = 3856;
    private const int CpuDutyBase = 3872;
    private const int GpuUptBase = 3888;
    private const int GpuDowntBase = 3904;
    private const int GpuDutyBase = 3920;
    private const int GpuMax1 = 3933;
    private const int GpuMax2 = 3934;
    private const int TableCtrl = 3935;
    private const int TableLatch = 1990;
    private const int ApOemByte = 1857;
    private const int FanBoostClamp = 1873;
    private const int FanRamp = 1927;
    private const int FanIsolated = 1989;
    private const int TriggerByte = 1895;
    private const int LatchBit = 0x04;

    private sealed class ModeTable
    {
        public required string Name;
        public required byte[] CpuUp;
        public required byte[] CpuDown;
        public required byte[] CpuDuty;
        public required byte[] GpuUp;
        public required byte[] GpuDown;
        public required byte[] GpuDuty;
        public byte MaxDuty => GpuDuty[15];
    }

    // 官方 30 系 NUC/UMI 三档默认表；性能档/平衡档/静音档必须有不同占空比上限。
    private static readonly ModeTable Office = new()
    {
        Name = "Office",
        CpuUp = new byte[] { 0, 55, 57, 59, 61, 63, 65, 67, 69, 255, 255, 255, 255, 255, 255, 255 },
        CpuDown = new byte[] { 48, 58, 60, 62, 64, 66, 68, 70, 255, 255, 255, 255, 255, 255, 255, 255 },
        CpuDuty = new byte[] { 0, 60, 60, 60, 60, 70, 70, 90, 110, 110, 110, 110, 110, 110, 110, 110 },
        GpuUp = new byte[] { 0, 55, 55, 57, 57, 59, 62, 64, 66, 255, 255, 255, 255, 255, 255, 255 },
        GpuDown = new byte[] { 48, 56, 58, 58, 60, 63, 65, 67, 255, 255, 255, 255, 255, 255, 255, 255 },
        GpuDuty = new byte[] { 0, 60, 60, 60, 60, 70, 70, 90, 110, 110, 110, 110, 110, 110, 110, 110 }
    };

    private static readonly ModeTable Gaming = new()
    {
        Name = "Gaming",
        CpuUp = new byte[] { 0, 55, 58, 61, 65, 69, 72, 75, 78, 81, 255, 255, 255, 255, 255, 255 },
        CpuDown = new byte[] { 48, 58, 61, 65, 69, 73, 76, 79, 82, 255, 255, 255, 255, 255, 255, 255 },
        CpuDuty = new byte[] { 0, 60, 70, 80, 100, 110, 120, 130, 150, 160, 160, 160, 160, 160, 160, 160 },
        GpuUp = new byte[] { 0, 55, 58, 61, 65, 69, 72, 75, 78, 81, 255, 255, 255, 255, 255, 255 },
        GpuDown = new byte[] { 48, 58, 61, 65, 69, 73, 76, 79, 82, 255, 255, 255, 255, 255, 255, 255 },
        GpuDuty = new byte[] { 0, 60, 70, 80, 100, 110, 120, 130, 150, 160, 160, 160, 160, 160, 160, 160 }
    };

    private static readonly ModeTable Turbo = new()
    {
        Name = "Turbo",
        CpuUp = new byte[] { 0, 55, 58, 61, 65, 69, 72, 75, 78, 81, 84, 255, 255, 255, 255, 255 },
        CpuDown = new byte[] { 48, 58, 61, 65, 69, 73, 76, 79, 82, 85, 255, 255, 255, 255, 255, 255 },
        CpuDuty = new byte[] { 0, 60, 70, 80, 100, 110, 120, 130, 150, 160, 180, 180, 180, 180, 180, 180 },
        GpuUp = new byte[] { 0, 55, 58, 61, 65, 69, 72, 75, 78, 81, 84, 255, 255, 255, 255, 255 },
        GpuDown = new byte[] { 48, 58, 61, 65, 69, 73, 76, 79, 82, 85, 255, 255, 255, 255, 255, 255 },
        GpuDuty = new byte[] { 0, 60, 70, 80, 100, 110, 120, 130, 150, 160, 180, 180, 180, 180, 180, 180 }
    };

    private static ModeTable _lastDefault = Gaming;

    internal static bool CaptureDefaultOnce()
    {
        _lastDefault ??= Gaming;
        return true;
    }

    /// <summary>自定义曲线退出/恢复默认时，恢复最近一次应用的模式默认表（不是全 0 快照）。</summary>
    internal static bool TryRestoreDefault(out string detail)
    {
        bool ok = ApplyTable(_lastDefault ?? Gaming, out detail);
        bool isolated = TrySetFanIsolated(false, out string isolatedDetail);
        detail += " · " + isolatedDetail;
        return ok && isolated;
    }

    /// <summary>按模式应用官方默认表：静音=Office、平衡=Gaming、狂暴/极致=Turbo。</summary>
    internal static bool TryApplyDefaultMode(int visualMode, out string detail)
    {
        detail = "";
        ModeTable? table = visualMode switch
        {
            2 => Office,
            0 => Gaming,
            1 or 5 => Turbo,
            _ => null
        };
        if (table == null)
        {
            detail = "自定义档不应用默认风扇表";
            return true;
        }
        _lastDefault = table;
        bool ok = ApplyTable(table, out detail);
        // 切回官方默认表时同时交还 FanIsolated，避免上一次自定义曲线留下独立输出位。
        bool isolated = TrySetFanIsolated(false, out string isolatedDetail);
        detail += " · " + isolatedDetail;
        return ok && isolated;
    }

    private static bool ApplyTable(ModeTable table, out string detail)
    {
        detail = "";
        // 强冷开启时不要覆盖风扇表，避免抢固件强冷控制。
        if (TryRead(FanBoostClamp, out int clamp) && (clamp & 0x40) != 0)
        {
            detail = "强冷开启，跳过风扇表写入（保持强冷全速）";
            return true;
        }

        // 官方顺序：APExist → latch 预脉冲 → AP Active → 写表 → 写 max/ctrl → latch 提交。
        if (TryRead(ApOemByte, out int ap))
        {
            TryWrite(ApOemByte, ap | 0x01);
        }
        TryWrite(TableLatch, LatchBit);
        TryWrite(TableLatch, 0x00);
        if (TryRead(ApOemByte, out ap))
        {
            TryWrite(ApOemByte, ap | 0x81);
        }

        bool ok = true;
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuUptBase + i, table.CpuUp[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuDowntBase + i, table.CpuDown[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuDutyBase + i, i == 0 ? 0 : table.CpuDuty[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(GpuUptBase + i, table.GpuUp[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(GpuDowntBase + i, table.GpuDown[i]);
        // GPU 占空比只写 13 段（3920..3932）；3933–3935 是 Max1/Max2/TableCtrl，不能混写。
        for (int i = 0; i < 13; i++) ok &= TryWrite(GpuDutyBase + i, i == 0 ? 0 : table.GpuDuty[i]);

        int maxDuty = table.MaxDuty;
        int ctrl = Math.Max(maxDuty, 160); // 官方活动控制字节至少 0xA0，绝不能写 0/1/2/3。
        ok &= TryWrite(GpuMax1, maxDuty);
        ok &= TryWrite(GpuMax2, maxDuty);
        ok &= TryWrite(TableCtrl, ctrl);
        TryWrite(TableLatch, LatchBit);
        if (TryRead(TriggerByte, out int trigger))
        {
            TryWrite(TriggerByte, (trigger & 0x10) | 0x20);
        }

        bool readMax1 = TryRead(GpuMax1, out int read1);
        bool ctrlReadOk = TryRead(TableCtrl, out int ctrlReadback);
        detail = $"{table.Name} 默认表已应用 · maxDuty={maxDuty} ctrl={ctrl}（回读 {ctrlReadback}）";
        return ok && readMax1 && read1 == maxDuty && ctrlReadOk && ctrlReadback == ctrl;
    }

    /// <summary>风扇 Ramp Rate：1927，0x80|步长 或 0x81（关闭）。</summary>
    internal static bool TrySetFanRamp(bool enabled, int speedMs, out string detail)
    {
        detail = "";
        int value = enabled
            ? 0x80 | (Math.Clamp(speedMs, 100, 12700) / 100)
            : 0x81;
        if (!TryWrite(FanRamp, value))
        {
            detail = "FanRamp 写入失败";
            return false;
        }
        bool ok = TryRead(FanRamp, out int after) && after == value;
        detail = $"FanRamp=0x{value:X2} 回读=0x{after:X2}";
        return ok;
    }

    /// <summary>Fan Isolated：1989 bit7，读改写保留其它位。</summary>
    internal static bool TrySetFanIsolated(bool enabled, out string detail)
    {
        detail = "";
        if (!TryRead(FanIsolated, out int current))
        {
            detail = "FanIsolated 读取失败";
            return false;
        }
        int value = enabled ? (current | 0x80) : (current & ~0x80);
        if (!TryWrite(FanIsolated, value))
        {
            detail = "FanIsolated 写入失败";
            return false;
        }
        bool ok = TryRead(FanIsolated, out int after) && after == value;
        detail = $"FanIsolated=0x{value:X2} 回读=0x{after:X2}";
        return ok;
    }

    internal static bool TryApply(byte[] cpuTemps, int[] cpuDuties, byte[] gpuTemps, int[] gpuDuties, out string detail)
    {
        detail = "";
        if (cpuTemps.Length != 16 || gpuTemps.Length != 16 || cpuDuties.Length != 16 || gpuDuties.Length != 16)
        {
            detail = "风扇曲线必须是 16 段";
            return false;
        }
        if (TryRead(FanBoostClamp, out int clamp) && (clamp & 0x40) != 0)
        {
            detail = "强冷开启，跳过风扇曲线写入";
            return false;
        }

        byte[] cpuDuty = NormalizeDuty(cpuDuties);
        byte[] gpuDuty = NormalizeDuty(gpuDuties);
        byte gpuTail = Math.Max(gpuDuty[12], gpuDuty[15]);
        gpuDuty[13] = Math.Max(gpuDuty[13], gpuTail);
        gpuDuty[14] = Math.Max(gpuDuty[14], gpuTail);
        gpuDuty[15] = gpuTail;
        byte[] cpuDown = BuildDown(cpuTemps);
        byte[] gpuDown = BuildDown(gpuTemps);

        if (TryRead(ApOemByte, out int ap)) TryWrite(ApOemByte, ap | 0x81);
        TryWrite(TableLatch, 0x00);
        for (int i = 0; i < 16; i++) if (!TryWrite(CpuUptBase + i, cpuTemps[i])) { detail = "CPU UpT 写入失败@" + i; return false; }
        for (int i = 0; i < 16; i++) if (!TryWrite(CpuDowntBase + i, cpuDown[i])) { detail = "CPU DownT 写入失败@" + i; return false; }
        for (int i = 0; i < 16; i++) if (!TryWrite(CpuDutyBase + i, i == 0 ? 0 : cpuDuty[i])) { detail = "CPU Duty 写入失败@" + i; return false; }
        for (int i = 0; i < 16; i++) if (!TryWrite(GpuUptBase + i, gpuTemps[i])) { detail = "GPU UpT 写入失败@" + i; return false; }
        for (int i = 0; i < 16; i++) if (!TryWrite(GpuDowntBase + i, gpuDown[i])) { detail = "GPU DownT 写入失败@" + i; return false; }
        for (int i = 0; i < 13; i++) if (!TryWrite(GpuDutyBase + i, i == 0 ? 0 : gpuDuty[i])) { detail = "GPU Duty 写入失败@" + i; return false; }
        int maxDuty = Math.Max(cpuDuty[15], gpuDuty[15]);
        int ctrl = Math.Max(maxDuty, 160);
        TryWrite(GpuMax1, maxDuty);
        TryWrite(GpuMax2, maxDuty);
        TryWrite(TableCtrl, ctrl);
        TryWrite(TableLatch, LatchBit);
        detail = $"CPU={string.Join(',', cpuDuty)} GPU={string.Join(',', gpuDuty)} max={maxDuty} ctrl={ctrl}";
        return true;
    }

    private static byte[] NormalizeDuty(int[] percents) { byte[] result = new byte[16]; int running = 0; for (int i = 0; i < 16; i++) { int raw = Math.Clamp(percents[i], 0, 100) * 2; running = Math.Max(running, raw); result[i] = (byte)running; } result[0] = 0; return result; }
    private static byte[] BuildDown(byte[] upt) { byte[] down = new byte[16]; int previous = 0; for (int i = 1; i < 16; i++) { if (upt[i] == 255) { down[i] = 255; continue; } int hysteresis = upt[i] <= 55 ? 8 : 5; int value = Math.Max(previous, upt[i] - hysteresis); value = Math.Max(0, Math.Min(value, upt[i])); down[i] = (byte)value; previous = value; } return down; }
    private static bool TryRead(int address, out int value) { if (AcpiDriverEc.TryReadByte(address, out byte b)) { value = b; return true; } if (EcPortIo.TryReadByteStable(address, out int legacy, out _)) { value = legacy; return true; } value = -1; return false; }
    private static bool TryWrite(int address, int value) => AcpiDriverEc.TryWriteByte(address, value) || EcPortIo.TryWriteByte(address, value);
}
