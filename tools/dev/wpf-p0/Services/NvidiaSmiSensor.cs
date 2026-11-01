using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 用独立的 nvidia-smi 子进程读取 GPU 功耗/显存频率。
/// 原生 NVML 在部分机型会 AV；子进程崩溃不会带走主进程。
/// </summary>
internal static class NvidiaSmiSensor
{
    private static readonly object Sync = new();
    private static long _lastTick;
    private static float? _powerWatts;
    private static int? _memoryClockMhz;

    internal static float? GetPowerWatts()
    {
        Refresh();
        return _powerWatts;
    }

    internal static int? GetMemoryClockMhz()
    {
        Refresh();
        return _memoryClockMhz;
    }

    private static void Refresh()
    {
        long now = Environment.TickCount64;
        lock (Sync)
        {
            if (now - _lastTick < 2500)
            {
                return;
            }
            _lastTick = now;
        }
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=power.draw,clocks.mem --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            string first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string[] parts = first.Split(',');
            float? power = parts.Length > 0 && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float p) && p > 0 && p < 1000 ? p : null;
            int? clock = parts.Length > 1 && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) && c > 0 ? c : null;
            lock (Sync)
            {
                _powerWatts = power;
                _memoryClockMhz = clock;
            }
        }
        catch
        {
            lock (Sync)
            {
                _powerWatts = null;
                _memoryClockMhz = null;
            }
        }
    }
}
