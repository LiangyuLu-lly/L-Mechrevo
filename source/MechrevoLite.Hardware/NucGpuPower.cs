using System;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>NUC/UMI 显卡 cTGP / Dynamic Boost / 驱动超频。</summary>
internal static class NucGpuPower
{
    private const int CtrlAddress = 1859;   // bit2 cTGP, bit1 DB enable, bit0 DB function
    private const int CtgpOffsetAddress = 1860;
    private const int DbTotalAddress = 1861;
    private const int DbMaxAddress = 1862;

    /// <summary>只做 NVAPI 核心/显存偏移；30 系 legacy 不写 EC cTGP/DB。</summary>
    internal static bool TryApplyOverclock(int? coreOffset, int? memoryOffset, out string detail)
    {
        detail = "";
        if (!coreOffset.HasValue && !memoryOffset.HasValue)
        {
            detail = "未启用 GPU 偏移";
            return true;
        }
        try
        {
            using IGpuOverclockControl? oc = NvidiaGpuControl.TryCreateOverclockControl();
            if (oc == null || !oc.IsAvailable)
            {
                detail = "GPU 超频驱动不可用";
                return false;
            }
            bool coreOk = !coreOffset.HasValue || oc.SetCoreOffset(coreOffset.Value);
            bool memOk = !memoryOffset.HasValue || oc.SetMemoryOffset(memoryOffset.Value);
            oc.Refresh();
            int coreNow = oc.CoreOffset.Current;
            int memNow = oc.MemoryOffset.Current;
            bool coreRead = !coreOffset.HasValue || coreNow == coreOffset.Value;
            bool memRead = !memoryOffset.HasValue || memNow == memoryOffset.Value;
            detail = $"GPU 偏移 core={coreOffset?.ToString() ?? "-"}={coreOk}/回读{coreNow} mem={memoryOffset?.ToString() ?? "-"}={memOk}/回读{memNow}";
            return coreOk && memOk && coreRead && memRead;
        }
        catch (Exception ex)
        {
            detail = "GPU 偏移异常=" + ex.GetType().Name;
            return false;
        }
    }

    internal static bool TryApply(int ctgp, bool dbOn, int dbWatts, int? coreOffset, int? memoryOffset, out string detail, bool force = false)
    {
        detail = "";
        string probe = Probe();
        Logger.WriteLine("[nuc-gpu-probe] " + probe);
        if (!force && MechrevoLite.AppConfig.Get("nuc_gpu_apply", 0) != 1)
        {
            detail = "GPU参数已保存；硬件下发未启用（当前只有单版本假设，待探针确认）：" + probe;
            return true;
        }
        if (ctgp <= 0 && !dbOn && !coreOffset.HasValue && !memoryOffset.HasValue)
        {
            detail = "无 GPU 参数";
            return false;
        }

        int minimum = 0;
        int maximum = 0;
        int current = 0;
        bool hasBounds = false;
        try
        {
            hasBounds = NvmlHelper.TryGetPowerLimitBounds(out minimum, out maximum, out current)
                && minimum > 0 && maximum > 0;
        }
        catch
        {
        }

        // 30 系 NUC 的原厂 TGP 上限通常是 80/105W：差值 <= 25W 视为
        // 固定基础 TGP + 纯 Dynamic Boost；差值更大才允许 cTGP 下调。
        bool fullPower = !hasBounds || (maximum - minimum) <= 25;
        int dbCap = hasBounds ? Math.Clamp(maximum - minimum, 5, 35) : 25;
        int db = Math.Clamp(dbWatts, 0, dbCap);
        bool ok = true;

        int baseTgp;
        int offset;
        if (fullPower)
        {
            // 固定基础 TGP：只动 DB；1861 必须写“基础 TGP + DB 瓦数”，
            // 不能写 0xFF —— 部分固件会把 0xFF 当最大值，导致无论开/关 DB 都跑满。
            baseTgp = Math.Max(1, minimum);
            offset = 0;
        }
        else
        {
            int floor = Math.Max(1, minimum);
            int trueDbCap = dbCap;
            int trueBase = Math.Max(floor, maximum - trueDbCap);
            baseTgp = ctgp > 0 ? Math.Clamp(ctgp, floor, trueBase) : floor;
            offset = Math.Max(0, baseTgp - floor);
        }
        int dbValue = dbOn ? Math.Clamp(db, 0, dbCap) : 0;
        int totalTarget = Math.Clamp(baseTgp + dbValue, 0, 255);
        // 原厂顺序：TGP 偏移 → DB 额度 → DB 总目标 → 控制位。
        ok &= WriteAndConfirm(CtgpOffsetAddress, offset, out string offsetDetail);
        ok &= WriteAndConfirm(DbMaxAddress, dbValue, out string dbDetail);
        ok &= WriteAndConfirm(DbTotalAddress, totalTarget, out string flagDetail);
        bool ctrlRead = AcpiDriverEc.TryReadByte(CtrlAddress, out byte ctrlCurrent);
        byte ctrlBase = ctrlRead ? ctrlCurrent : (byte)0;
        // bit2 = cTGP 功能使能，bit1 = DB 使能，bit0 = DB 功能使能。
        // 自定义 TGP 必须置 bit2，否则 1860/1861 目标不会被固件采用，
        // 表现为 TGP 设 60W、DB 开 5/30W，实际却都跑 145W。
        bool ctgpEnabled = !fullPower && ctgp > 0;
        int ctrlBits = (ctgpEnabled ? 0x04 : 0) | (dbOn ? 0x03 : 0);
        byte ctrl = (byte)((ctrlBase & 0xF8) | ctrlBits);
        ok &= WriteAndConfirm(CtrlAddress, ctrl, out string ctrlDetail);
        detail += (fullPower ? $" 固定基础TGP({baseTgp}W档)" : $" cTGP base={baseTgp}W offset={offset}")
            + $" DB={dbValue}W/{dbCap}W total={totalTarget}W"
            + ctrlDetail + offsetDetail + dbDetail + flagDetail;

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
