using System;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;

namespace MechrevoLite.Hardware;

/// <summary>NUC/UMI 显卡 cTGP / Dynamic Boost / 驱动超频。</summary>
internal static class NucGpuPower
{
    private const int CtrlAddress = 1859;   // bit2 cTGP, bit1 DB enable, bit0 DB function
    private const int CtgpOffsetAddress = 1860;
    private const int DbTotalAddress = 1861;
    private const int DbMaxAddress = 1862;

    internal static bool TryApply(int ctgp, bool dbOn, int dbWatts, int? coreOffset, int? memoryOffset, out string detail)
    {
        detail = "";
        string probe = Probe();
        Logger.WriteLine("[nuc-gpu-probe] " + probe);
        if (MechrevoLite.AppConfig.Get("nuc_gpu_apply", 0) != 1)
        {
            detail = "GPU参数已保存；硬件下发未启用（当前只有单版本假设，待探针确认）：" + probe;
            return true;
        }
        if (ctgp <= 0 && !dbOn && !coreOffset.HasValue && !memoryOffset.HasValue)
        {
            detail = "无 GPU 参数";
            return false;
        }
        int baseTgp = 115;
        try
        {
            if (NvmlHelper.TryGetPowerLimitBounds(out _, out int maximum, out int current) && current > 0)
            {
                baseTgp = Math.Clamp(current, 60, 250);
                _ = maximum;
            }
        }
        catch { }
        bool ok = true;
        if (ctgp > 0)
        {
            int target = Math.Clamp(ctgp, baseTgp, baseTgp + 20);
            int offset = Math.Max(0, target - baseTgp);
            ok &= WriteAndConfirm(CtgpOffsetAddress, offset, out string ctgpDetail);
            detail += ctgpDetail;
            ok &= SetControlBit(2, true, out string ctrlDetail);
            detail += ctrlDetail;
        }
        if (dbOn)
        {
            int watts = Math.Clamp(dbWatts, 0, 30);
            int total = Math.Clamp((ctgp > 0 ? Math.Clamp(ctgp, baseTgp, baseTgp + 20) : baseTgp) + watts, 0, 255);
            ok &= WriteAndConfirm(DbMaxAddress, watts, out string dbDetail);
            detail += dbDetail;
            ok &= WriteAndConfirm(DbTotalAddress, total, out string totalDetail);
            detail += totalDetail;
            ok &= SetControlBit(0, true, out string fnDetail);
            ok &= SetControlBit(1, true, out string enDetail);
            detail += fnDetail + enDetail;
        }
        else
        {
            ok &= SetControlBit(1, false, out _);
            ok &= SetControlBit(0, false, out _);
            WriteAndConfirm(DbTotalAddress, 0, out _);
            WriteAndConfirm(DbMaxAddress, 0, out _);
        }
        if (coreOffset.HasValue || memoryOffset.HasValue)
        {
            try
            {
                using IGpuOverclockControl? oc = NvidiaGpuControl.TryCreateOverclockControl();
                if (oc == null || !oc.IsAvailable)
                {
                    detail += " GPU超频=驱动不可用";
                    ok = false;
                }
                else
                {
                    bool coreOk = !coreOffset.HasValue || oc.SetCoreOffset(coreOffset.Value);
                    bool memOk = !memoryOffset.HasValue || oc.SetMemoryOffset(memoryOffset.Value);
                    detail += $" GPU超频 core={coreOffset?.ToString() ?? "-"}={coreOk} mem={memoryOffset?.ToString() ?? "-"}={memOk}";
                    ok &= coreOk && memOk;
                }
            }
            catch (Exception ex)
            {
                detail += " GPU超频异常=" + ex.GetType().Name;
                ok = false;
            }
        }
        return ok;
    }

    internal static string Probe()
    {
        static string Hex(int address)
        {
            return AcpiDriverEc.TryReadByte(address, out byte value) ? $"0x{address:X3}=0x{value:X2}" : $"0x{address:X3}=读取失败";
        }
        string nvml = "NVML不可用";
        try
        {
            if (NvmlHelper.TryGetPowerLimitBounds(out int minimum, out int maximum, out int current))
            {
                nvml = $"NVML min={minimum} max={maximum} current={current}";
            }
        }
        catch { }
        return "cTGP/DB " + string.Join(" ", new[]
        {
            Hex(1859), Hex(1860), Hex(1861), Hex(1862),
            Hex(1988), Hex(2004), Hex(2005)
        }) + " · " + nvml;
    }

    private static bool SetControlBit(int bit, bool on, out string detail)
    {
        detail = "";
        if (!AcpiDriverEc.TryReadByte(CtrlAddress, out byte current))
        {
            detail = $" ctrl{bit}=读失败";
            return false;
        }
        int value = on ? (current | (1 << bit)) : (current & ~(1 << bit));
        bool write = AcpiDriverEc.TryWriteByte(CtrlAddress, value);
        bool read = AcpiDriverEc.TryReadByte(CtrlAddress, out byte after);
        bool ok = write && read && (((after >> bit) & 1) == (on ? 1 : 0));
        detail += $" ctrl{bit}={(on ? 1 : 0)}={ok}";
        return ok;
    }

    private static bool WriteAndConfirm(int address, int value, out string detail)
    {
        bool write = AcpiDriverEc.TryWriteByte(address, value);
        bool read = AcpiDriverEc.TryReadByte(address, out byte after);
        bool ok = write && read && after == value;
        detail = $" 0x{address:X3}={value}={(ok ? "ok" : "fail")}";
        return ok;
    }
}
