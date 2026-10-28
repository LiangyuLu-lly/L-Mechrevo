using System;
using System.Linq;

namespace MechrevoLite.Hardware;

/// <summary>NUC/UMI 自定义风扇曲线：写入 EC RAM 的 16 段温度/占空比表。</summary>
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
    private const int LatchBit = 0x04;

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
        // GPU 尾段必须跟随最大占空比，避免 EC 判定表不完整。
        byte gpuTail = Math.Max(gpuDuty[12], gpuDuty[15]);
        gpuDuty[13] = Math.Max(gpuDuty[13], gpuTail);
        gpuDuty[14] = Math.Max(gpuDuty[14], gpuTail);
        gpuDuty[15] = gpuTail;
        byte[] cpuDown = BuildDown(cpuTemps);
        byte[] gpuDown = BuildDown(gpuTemps);

        // 将 APExist / AP Active 置位，再写入 RAMFan 表。
        if (TryRead(ApOemByte, out int ap))
        {
            TryWrite(ApOemByte, ap | 0x81);
        }
        TryWrite(TableLatch, 0x00);

        for (int i = 0; i < 16; i++)
        {
            if (!TryWrite(CpuUptBase + i, cpuTemps[i])) { detail = "CPU UpT 写入失败@" + i; return false; }
        }
        for (int i = 0; i < 16; i++)
        {
            if (!TryWrite(CpuDowntBase + i, cpuDown[i])) { detail = "CPU DownT 写入失败@" + i; return false; }
        }
        for (int i = 0; i < 16; i++)
        {
            if (!TryWrite(CpuDutyBase + i, i == 0 ? 0 : cpuDuty[i])) { detail = "CPU Duty 写入失败@" + i; return false; }
        }
        for (int i = 0; i < 16; i++)
        {
            if (!TryWrite(GpuUptBase + i, gpuTemps[i])) { detail = "GPU UpT 写入失败@" + i; return false; }
        }
        for (int i = 0; i < 16; i++)
        {
            if (!TryWrite(GpuDowntBase + i, gpuDown[i])) { detail = "GPU DownT 写入失败@" + i; return false; }
        }
        for (int i = 0; i < 13; i++)
        {
            if (!TryWrite(GpuDutyBase + i, i == 0 ? 0 : gpuDuty[i])) { detail = "GPU Duty 写入失败@" + i; return false; }
        }

        int maxGpu = gpuDuty[15];
        int ctrl = Math.Max(160, maxGpu);
        TryWrite(GpuMax1, maxGpu);
        TryWrite(GpuMax2, maxGpu);
        TryWrite(TableCtrl, ctrl);
        if (!TryWrite(TableLatch, LatchBit))
        {
            detail = "风扇表 Latch 提交失败";
            return false;
        }
        detail = $"CPU={string.Join(',', cpuDuty)} GPU={string.Join(',', gpuDuty)} ctrl=0x{ctrl:X2}";
        return true;
    }

    private static byte[] NormalizeDuty(int[] percents)
    {
        byte[] result = new byte[16];
        int running = 0;
        for (int i = 0; i < 16; i++)
        {
            int raw = Math.Clamp(percents[i], 0, 100) * 2;
            running = Math.Max(running, raw);
            result[i] = (byte)running;
        }
        result[0] = 0;
        return result;
    }

    private static byte[] BuildDown(byte[] upt)
    {
        byte[] down = new byte[16];
        int previous = 0;
        for (int i = 1; i < 16; i++)
        {
            if (upt[i] == 255)
            {
                down[i] = 255;
                continue;
            }
            int hysteresis = upt[i] <= 55 ? 8 : 5;
            int value = Math.Max(previous, upt[i] - hysteresis);
            value = Math.Max(0, Math.Min(value, upt[i]));
            down[i] = (byte)value;
            previous = value;
        }
        return down;
    }

    private static bool TryRead(int address, out int value)
    {
        if (AcpiDriverEc.TryReadByte(address, out byte b)) { value = b; return true; }
        if (EcPortIo.TryReadByteStable(address, out int legacy, out _)) { value = legacy; return true; }
        value = -1;
        return false;
    }

    private static bool TryWrite(int address, int value)
    {
        return AcpiDriverEc.TryWriteByte(address, value) || EcPortIo.TryWriteByte(address, value);
    }
}
