using System;
using System.IO;
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
    private const int FanRamp = 1927;
    private const int FanIsolated = 1989;
    private const int LatchBit = 0x04;

    private sealed class Snapshot
    {
        public byte[] CpuUp = Array.Empty<byte>();
        public byte[] CpuDown = Array.Empty<byte>();
        public byte[] CpuDuty = Array.Empty<byte>();
        public byte[] GpuUp = Array.Empty<byte>();
        public byte[] GpuDown = Array.Empty<byte>();
        public byte[] GpuDuty = Array.Empty<byte>();
        public int Max1, Max2, Ctrl, Latch, Ap, Ramp, Isolated;
    }

    private static Snapshot? _defaultTable;

    private static string BackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MechrevoLite", "nuc-fan-default.txt");

    internal static bool CaptureDefaultOnce()
    {
        if (_defaultTable != null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            if (File.Exists(BackupPath))
            {
                Snapshot? loaded = Load(BackupPath);
                if (loaded != null) { _defaultTable = loaded; return true; }
            }
            Snapshot snapshot = ReadSnapshot();
            _defaultTable = snapshot;
            Save(BackupPath, snapshot);
            return true;
        }
        catch { return false; }
    }

    internal static bool TryRestoreDefault(out string detail)
    {
        detail = "";
        if (!CaptureDefaultOnce() || _defaultTable == null)
        {
            detail = "没有可恢复的默认风扇表";
            return false;
        }
        Snapshot d = _defaultTable;
        bool ok = true;
        // 先清 latch，再恢复表，最后恢复原始 latch 状态提交。
        ok &= TryWrite(TableLatch, 0);
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuUptBase + i, d.CpuUp[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuDowntBase + i, d.CpuDown[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(CpuDutyBase + i, d.CpuDuty[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(GpuUptBase + i, d.GpuUp[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(GpuDowntBase + i, d.GpuDown[i]);
        for (int i = 0; i < 16; i++) ok &= TryWrite(GpuDutyBase + i, d.GpuDuty[i]);
        ok &= TryWrite(ApOemByte, d.Ap);
        ok &= TryWrite(TableLatch, d.Latch);
        TryWrite(FanRamp, d.Ramp);
        TryWrite(FanIsolated, d.Isolated);
        detail = "默认风扇表已恢复"
            + (TryRead(TableLatch, out int latch) ? " 回读Latch=0x" + latch.ToString("X2") : "");
        return ok;
    }

    private static Snapshot ReadSnapshot()
    {
        Snapshot s = new()
        {
            CpuUp = ReadBlock(CpuUptBase),
            CpuDown = ReadBlock(CpuDowntBase),
            CpuDuty = ReadBlock(CpuDutyBase),
            GpuUp = ReadBlock(GpuUptBase),
            GpuDown = ReadBlock(GpuDowntBase),
            GpuDuty = ReadBlock(GpuDutyBase)
        };
        TryRead(GpuMax1, out s.Max1);
        TryRead(GpuMax2, out s.Max2);
        TryRead(TableCtrl, out s.Ctrl);
        TryRead(TableLatch, out s.Latch);
        TryRead(ApOemByte, out s.Ap);
        TryRead(FanRamp, out s.Ramp);
        TryRead(FanIsolated, out s.Isolated);
        return s;
    }

    private static byte[] ReadBlock(int address)
    {
        byte[] result = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            if (!TryRead(address + i, out int value)) value = 0;
            result[i] = (byte)Math.Clamp(value, 0, 255);
        }
        return result;
    }

    private static void Save(string path, Snapshot s)
    {
        string[] lines =
        {
            "c0=" + string.Join(',', s.CpuUp),
            "c1=" + string.Join(',', s.CpuDown),
            "c2=" + string.Join(',', s.CpuDuty),
            "g0=" + string.Join(',', s.GpuUp),
            "g1=" + string.Join(',', s.GpuDown),
            "g2=" + string.Join(',', s.GpuDuty),
            $"max1={s.Max1} max2={s.Max2} ctrl={s.Ctrl} latch={s.Latch} ap={s.Ap} ramp={s.Ramp} iso={s.Isolated}"
        };
        File.WriteAllLines(path, lines);
    }

    private static Snapshot? Load(string path)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            Snapshot s = new();
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line[..eq];
                string val = line[(eq + 1)..];
                if (key is "c0" or "c1" or "c2" or "g0" or "g1" or "g2")
                {
                    byte[] arr = val.Split(',').Select(x => byte.TryParse(x, out byte b) ? b : (byte)0).ToArray();
                    if (arr.Length != 16) return null;
                    switch (key)
                    {
                        case "c0": s.CpuUp = arr; break;
                        case "c1": s.CpuDown = arr; break;
                        case "c2": s.CpuDuty = arr; break;
                        case "g0": s.GpuUp = arr; break;
                        case "g1": s.GpuDown = arr; break;
                        case "g2": s.GpuDuty = arr; break;
                    }
                }
                else
                {
                    foreach (string part in val.Split(' '))
                    {
                        int e = part.IndexOf('=');
                        if (e < 0) continue;
                        if (int.TryParse(part[(e + 1)..], out int n))
                        {
                            switch (part[..e])
                            {
                                case "max1": s.Max1 = n; break;
                                case "max2": s.Max2 = n; break;
                                case "ctrl": s.Ctrl = n; break;
                                case "latch": s.Latch = n; break;
                                case "ap": s.Ap = n; break;
                                case "ramp": s.Ramp = n; break;
                                case "iso": s.Isolated = n; break;
                            }
                        }
                    }
                }
            }
            return s.CpuUp.Length == 16 && s.GpuDuty.Length == 16 ? s : null;
        }
        catch { return null; }
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
        CaptureDefaultOnce();
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
        // 官方 NUC 30 的 GPU 表就是 3920..3935 共 16 段；3933–3935 是表内最后三段，
        // 不能当 Max/Ctrl 单独覆盖，否则 GPU 表不完整，第二风扇可能直接掉到 0。
        for (int i = 0; i < 16; i++) if (!TryWrite(GpuDutyBase + i, i == 0 ? 0 : gpuDuty[i])) { detail = "GPU Duty 写入失败@" + i; return false; }
        if (TryRead(TableLatch, out int latch)) TryWrite(TableLatch, latch | LatchBit);
        else TryWrite(TableLatch, LatchBit);
        detail = $"CPU={string.Join(',', cpuDuty)} GPU={string.Join(',', gpuDuty)}";
        return true;
    }

    private static byte[] NormalizeDuty(int[] percents) { byte[] result = new byte[16]; int running = 0; for (int i = 0; i < 16; i++) { int raw = Math.Clamp(percents[i], 0, 100) * 2; running = Math.Max(running, raw); result[i] = (byte)running; } result[0] = 0; return result; }
    private static byte[] BuildDown(byte[] upt) { byte[] down = new byte[16]; int previous = 0; for (int i = 1; i < 16; i++) { if (upt[i] == 255) { down[i] = 255; continue; } int hysteresis = upt[i] <= 55 ? 8 : 5; int value = Math.Max(previous, upt[i] - hysteresis); value = Math.Max(0, Math.Min(value, upt[i])); down[i] = (byte)value; previous = value; } return down; }
    private static bool TryRead(int address, out int value) { if (AcpiDriverEc.TryReadByte(address, out byte b)) { value = b; return true; } if (EcPortIo.TryReadByteStable(address, out int legacy, out _)) { value = legacy; return true; } value = -1; return false; }
    private static bool TryWrite(int address, int value) => AcpiDriverEc.TryWriteByte(address, value) || EcPortIo.TryWriteByte(address, value);
}
