using System;
using System.IO;
using System.Threading;

namespace PawnIO;

/// <summary>Intel MSR_PKG_POWER_LIMIT (0x610) 读写：PL1/PL2。</summary>
internal static class IntelMsrPowerLimit
{
    private const uint MsrPkgPowerLimit = 0x610;
    private const ulong Pl1Mask = 0x7FFFUL;
    private const ulong Pl2Mask = 0x7FFFUL << 32;
    private const ulong Pl1Enable = 1UL << 15;
    private const ulong Pl2Enable = 1UL << 47;
    private const ulong LockBit = 1UL << 63;
    private const int MinWatts = 5;
    private const int MaxWatts = 200;

    private static readonly object Gate = new();
    private static PawnIOWrapper? _pawn;
    private static bool _loadFailed;

    private static bool Ensure()
    {
        lock (Gate)
        {
            if (_pawn?.IsModuleLoaded == true) return true;
            if (_loadFailed) return false;
            try
            {
                using Stream? stream = MechrevoLite.Hardware.PawnIOModuleLoader.Open("LMechrevo.IntelMSR.bin");
                if (stream == null) { _loadFailed = true; return false; }
                using MemoryStream memory = new();
                stream.CopyTo(memory);
                PawnIOWrapper pawn = new();
                if (pawn.Connect() != PawnIOWrapper.ConnectResult.OK || !pawn.LoadModule(memory.ToArray()))
                {
                    pawn.Dispose();
                    _loadFailed = true;
                    return false;
                }
                _pawn = pawn;
                return true;
            }
            catch
            {
                _loadFailed = true;
                return false;
            }
        }
    }

    private static bool ReadMsr(out ulong value)
    {
        value = 0;
        if (!Ensure()) return false;
        ulong[] output = new ulong[1];
        if (!_pawn!.Execute("ioctl_read_msr", new[] { (ulong)MsrPkgPowerLimit }, output)) return false;
        value = output[0];
        return true;
    }

    private static bool WriteMsr(ulong value)
    {
        return Ensure() && _pawn!.Execute("ioctl_write_msr", new[] { (ulong)MsrPkgPowerLimit, value }, null);
    }

    internal static bool TryRead(out int pl1Watts, out int pl2Watts, out bool locked, out string detail)
    {
        pl1Watts = 0;
        pl2Watts = 0;
        locked = false;
        detail = "";
        if (!ReadMsr(out ulong raw))
        {
            detail = "MSR 0x610 读取失败（PawnIO/IntelMSR 不可用）";
            return false;
        }
        locked = (raw & LockBit) != 0;
        pl1Watts = (int)Math.Round((raw & Pl1Mask) / 8.0);
        pl2Watts = (int)Math.Round(((raw >> 32) & Pl1Mask) / 8.0);
        int pl1Time = (int)((raw >> 17) & 0x7F);
        int pl2Time = (int)((raw >> 49) & 0x7F);
        detail = $"0x610=0x{raw:X16} PL1={pl1Watts}W PL2={pl2Watts}W "
            + $"PL1en={(raw & Pl1Enable) != 0} PL2en={(raw & Pl2Enable) != 0} "
            + $"PL1time={pl1Time} PL2time={pl2Time} lock={locked}";
        return true;
    }

    internal static bool TryApply(int pl1Watts, int pl2Watts, out bool locked, out string detail)
    {
        locked = false;
        detail = "";
        if (pl1Watts <= 0 && pl2Watts <= 0)
        {
            detail = "PL1/PL2 都未设置";
            return false;
        }
        bool has1 = pl1Watts > 0;
        bool has2 = pl2Watts > 0;
        int pl1 = has1 ? Math.Clamp(pl1Watts, MinWatts, MaxWatts) : 0;
        int pl2 = has2 ? Math.Clamp(pl2Watts, MinWatts, MaxWatts) : 0;
        if (!ReadMsr(out ulong raw))
        {
            detail = "MSR 0x610 读取失败（PawnIO/IntelMSR 不可用）";
            return false;
        }
        locked = (raw & LockBit) != 0;
        if (locked)
        {
            detail = "MSR 0x610 lock=1，固件已锁定功耗墙";
            return false;
        }
        ulong value = raw;
        if (has1)
        {
            value = (value & ~Pl1Mask) | ((ulong)(pl1 * 8) & Pl1Mask);
            value |= Pl1Enable;
        }
        if (has2)
        {
            value = (value & ~Pl2Mask) | (((ulong)(pl2 * 8) & Pl1Mask) << 32);
            value |= Pl2Enable;
        }
        if (!WriteMsr(value))
        {
            detail = "MSR 0x610 写入失败";
            return false;
        }
        Thread.Sleep(80);
        if (!ReadMsr(out ulong after))
        {
            detail = "MSR 0x610 写后读取失败";
            return false;
        }
        int got1 = (int)Math.Round((after & Pl1Mask) / 8.0);
        int got2 = (int)Math.Round(((after >> 32) & Pl1Mask) / 8.0);
        bool ok = (!has1 || Math.Abs(got1 - pl1) <= 1)
            && (!has2 || Math.Abs(got2 - pl2) <= 1)
            && (!has1 || (after & Pl1Enable) != 0)
            && (!has2 || (after & Pl2Enable) != 0);
        detail = $"写入 PL1={(has1 ? pl1.ToString() : "保持不变")}W PL2={(has2 ? pl2.ToString() : "保持不变")}W "
            + $"⇒ 回读 PL1={got1}W PL2={got2}W "
            + $"PL1en={(after & Pl1Enable) != 0} PL2en={(after & Pl2Enable) != 0} ok={ok}";
        return ok;
    }
}
