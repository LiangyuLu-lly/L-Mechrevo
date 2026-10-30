using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Gpu;

/// <summary>
/// NVIDIA GPU 遥测（nvidia-smi 子进程实现）。
///
/// 不在 LM 进程内 P/Invoke nvml.dll：NVML 句柄/模块映射失效时会触发
/// 0xc0000005（CoreCLR P/Invoke 桩），可能直接带走整个进程。
///
/// 遥测统一走独立进程 nvidia-smi；超时/失败只让本次快照为空，不影响 LM。
/// 功耗上限写入也走 nvidia-smi -pl（需要管理员权限）。
/// </summary>
public static class NvmlHelper
{
    internal const string NvmlModuleName = "nvidia-smi.exe";

    /// <summary>同一份快照在 TTL 内复用，避免 2 秒采样泵把子进程频率放大。</summary>
    internal const int SnapshotTtlMs = 1500;

    internal const int FailureThreshold = 4;

    private static readonly object _lock = new object();

    private static readonly string[] CandidatePaths =
    {
        @"C:\Windows\System32\nvidia-smi.exe",
        @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
        @"C:\Program Files (x86)\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
    };

    private static string? _smiPath;
    private static bool _smiPathResolved;
    private static bool _disabled;
    private static string _disabledReason = "";
    private static bool _unavailable;
    private static long _unavailableUntilTicks;
    private static int _consecutiveFailures;
    private static int _lastError = -1;
    private static int _lastTempError = -1;
    private static int _lastUtilizationError = -1;
    private static long _snapshotTicks = -1;
    private static long _lastQueryTicks = -1;
    private static bool _deviceAlive;
    private static int? _snapshotTempCelsius;
    private static float? _snapshotPower;
    private static long? _snapshotUsedMb;
    private static long? _snapshotTotalMb;
    private static float? _snapshotCoreClockMhz;
    private static float? _snapshotMemoryClockMhz;
    private static int? _snapshotGpuUtilization;
    private static int? _snapshotPowerLimitW;
    private static int? _snapshotPowerMinW;
    private static int? _snapshotPowerMaxW;
    private static int? _snapshotPowerDefaultW;
    private static bool _displaySuspended;
    private static int _nvidiaCapabilityChecked;

    internal static bool Unavailable
    {
        get
        {
            lock (_lock)
            {
                if (_disabled)
                {
                    return true;
                }
                long until = _unavailableUntilTicks;
                return _unavailable && (until == 0 || Environment.TickCount64 < until);
            }
        }
    }

    internal static bool ModuleGone => _disabled;

    internal static bool TransitionGuarded => false;

    internal static int ConsecutiveFailures
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveFailures;
            }
        }
    }

    internal static int LastError
    {
        get
        {
            lock (_lock)
            {
                return _lastError;
            }
        }
    }

    internal static int LastTempError
    {
        get
        {
            lock (_lock)
            {
                return _lastTempError;
            }
        }
    }

    internal static int LastUtilizationError
    {
        get
        {
            lock (_lock)
            {
                return _lastUtilizationError;
            }
        }
    }

    internal static bool DeviceHandleAlive
    {
        get
        {
            lock (_lock)
            {
                return _deviceAlive;
            }
        }
    }

    internal static bool TempUnavailableWhileDeviceAlive
    {
        get
        {
            lock (_lock)
            {
                return _deviceAlive && !_snapshotTempCelsius.HasValue;
            }
        }
    }

    internal static long SnapshotAgeMs
    {
        get
        {
            lock (_lock)
            {
                return _snapshotTicks < 0 ? -1 : Math.Max(0, Environment.TickCount64 - _snapshotTicks);
            }
        }
    }

    internal static string DisabledReason => _disabledReason;

    public static void Init()
    {
        // nvidia-smi 无需初始化；首次查询时解析路径。
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            ClearSnapshotLocked();
        }
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            ClearSnapshotLocked();
            _unavailable = false;
            _unavailableUntilTicks = 0;
            _consecutiveFailures = 0;
            _lastError = -1;
            _lastTempError = -1;
            _lastUtilizationError = -1;
            _deviceAlive = false;
        }
    }

    public static void DisableNativeForProcess(string reason)
    {
        // 保留旧 API：现在没有进程内原生 NVML 可停用；只作为诊断标记。
        lock (_lock)
        {
            _disabled = true;
            _disabledReason = reason ?? "";
            ClearSnapshotLocked();
        }
    }

    internal static void QuarantineAfterNativeHang(string reason)
    {
        lock (_lock)
        {
            _unavailable = true;
            _unavailableUntilTicks = Environment.TickCount64 + 60_000;
            _lastError = -1;
        }
    }

    internal static void SuspendForDisplayOff()
    {
        lock (_lock)
        {
            _displaySuspended = true;
        }
    }

    internal static void ResumeForDisplayOn()
    {
        lock (_lock)
        {
            _displaySuspended = false;
        }
    }

    public static float? GetGpuTemperature(uint gpuIndex = 0u)
    {
        lock (_lock)
        {
            RefreshLocked();
            return _snapshotTempCelsius;
        }
    }

    public static float? GetGpuPower(uint gpuIndex = 0u)
    {
        lock (_lock)
        {
            RefreshLocked();
            return _snapshotPower;
        }
    }

    public static (long usedMb, long totalMb)? GetMemoryInfo(uint gpuIndex = 0u)
    {
        lock (_lock)
        {
            RefreshLocked();
            if (!_snapshotUsedMb.HasValue || !_snapshotTotalMb.HasValue)
            {
                return null;
            }
            return (_snapshotUsedMb.Value, _snapshotTotalMb.Value);
        }
    }

    public static float? GetGpuClockMhz(uint gpuIndex = 0u)
    {
        lock (_lock)
        {
            RefreshLocked();
            return _snapshotCoreClockMhz;
        }
    }

    public static float? GetGpuMemoryClockMhz()
    {
        lock (_lock)
        {
            RefreshLocked();
            return _snapshotMemoryClockMhz;
        }
    }

    internal static int? GetGpuUtilization(uint gpuIndex = 0u)
    {
        lock (_lock)
        {
            RefreshLocked();
            return _snapshotGpuUtilization;
        }
    }

    internal static int? GetGpuMemoryUtilizationForDiagnostics(uint gpuIndex = 0u)
    {
        // nvidia-smi 没有与 NVML memory utilization 完全等价的字段；
        // 不用一个错误含义的值冒充。
        return null;
    }

    internal static bool TryGetPowerLimitBounds(out int minW, out int maxW, out int currentW)
    {
        lock (_lock)
        {
            RefreshLocked();
            minW = _snapshotPowerMinW ?? 0;
            maxW = _snapshotPowerMaxW ?? 0;
            currentW = _snapshotPowerLimitW ?? 0;
            return maxW > 0 && maxW >= minW;
        }
    }

    internal static bool TryGetDefaultPowerLimitWatts(out int watts)
    {
        lock (_lock)
        {
            RefreshLocked();
            watts = _snapshotPowerDefaultW ?? 0;
            return watts > 0;
        }
    }

    internal static bool TrySetPowerLimitWatts(int watts, out int appliedW)
    {
        appliedW = 0;
        int min;
        int max;
        int current;
        if (!TryGetPowerLimitBounds(out min, out max, out current))
        {
            return false;
        }
        int want = Math.Clamp(watts, min, max);
        string? smi = ResolveSmi();
        if (smi == null)
        {
            return false;
        }
        if (!RunSmi(smi, "-i 0 -pl " + want.ToString(CultureInfo.InvariantCulture), 3000, out _))
        {
            return false;
        }
        lock (_lock)
        {
            ClearSnapshotLocked();
        }
        if (!TryGetPowerLimitBounds(out _, out _, out appliedW))
        {
            return false;
        }
        return Math.Abs(appliedW - want) <= 1;
    }

    private static void RefreshLocked()
    {
        if (_disabled || _displaySuspended)
        {
            return;
        }
        if (Volatile.Read(ref _nvidiaCapabilityChecked) == 0)
        {
            if (!HasNvidiaAdapter())
            {
                _disabled = true;
                _disabledReason = "本机没有 NVIDIA 独显：不启动 nvidia-smi";
                ClearSnapshotLocked();
                return;
            }
            Interlocked.Exchange(ref _nvidiaCapabilityChecked, 1);
        }
        long now = Environment.TickCount64;
        if (_snapshotTicks >= 0 && now - _snapshotTicks < SnapshotTtlMs)
        {
            return;
        }
        if (_unavailable && _unavailableUntilTicks != 0 && now < _unavailableUntilTicks)
        {
            return;
        }
        // nvidia-smi 查询本身也要限频：失败后不要在 2s 采样里反复 spawn。
        if (_lastQueryTicks >= 0 && now - _lastQueryTicks < SnapshotTtlMs)
        {
            return;
        }
        _lastQueryTicks = now;
        string? smi = ResolveSmi();
        if (smi == null)
        {
            NoteFailureLocked("nvidia-smi.exe 未找到", -1);
            return;
        }
        const string fields = "index,temperature.gpu,power.draw,memory.used,memory.total,clocks.gr,clocks.mem,utilization.gpu,power.limit,power.min_limit,power.max_limit,power.default_limit";
        if (!RunSmi(smi, "--query-gpu=" + fields + " --format=csv,noheader,nounits", 2000, out string output))
        {
            // 老驱动可能不认识 power.min_limit/max_limit/default_limit：退回基础字段。
            const string basicFields = "index,temperature.gpu,power.draw,memory.used,memory.total,clocks.gr,clocks.mem,utilization.gpu,power.limit";
            if (!RunSmi(smi, "--query-gpu=" + basicFields + " --format=csv,noheader,nounits", 2000, out output))
            {
                NoteFailureLocked("nvidia-smi 查询失败/超时", -1);
                return;
            }
        }
        string? line = null;
        foreach (string candidate in output.Split('\n'))
        {
            string trimmed = candidate.Trim();
            if (trimmed.Length > 0)
            {
                line = trimmed;
                break;
            }
        }
        if (line == null)
        {
            NoteFailureLocked("nvidia-smi 无输出", -1);
            return;
        }
        string[] p = line.Split(',');
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = p[i].Trim();
        }
        int idx;
        if (!int.TryParse(p.Length > 0 ? p[0] : "", NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0)
        {
            NoteFailureLocked("nvidia-smi index 解析失败", -1);
            return;
        }
        float? temp = ParseFloat(p, 1);
        float? power = ParseFloat(p, 2);
        long? used = ParseLong(p, 3);
        long? total = ParseLong(p, 4);
        float? coreClock = ParseFloat(p, 5);
        float? memClock = ParseFloat(p, 6);
        int? utilization = ParseInt(p, 7);
        int? limit = ParseInt(p, 8);
        int? minLimit = ParseInt(p, 9);
        int? maxLimit = ParseInt(p, 10);
        int? defaultLimit = ParseInt(p, 11);

        _snapshotTempCelsius = temp.HasValue && temp.Value > 0 && temp.Value <= 150
            ? (int?)Math.Round(temp.Value, MidpointRounding.AwayFromZero)
            : null;
        _snapshotPower = power.HasValue && power.Value > 0 && power.Value <= 300 ? power : null;
        _snapshotUsedMb = used;
        _snapshotTotalMb = total;
        _snapshotCoreClockMhz = coreClock.HasValue && coreClock.Value > 0 && coreClock.Value <= 5000 ? coreClock : null;
        _snapshotMemoryClockMhz = memClock.HasValue && memClock.Value > 0 && memClock.Value <= 30000 ? memClock : null;
        _snapshotGpuUtilization = utilization.HasValue && utilization.Value >= 0 && utilization.Value <= 100 ? utilization : null;
        _snapshotPowerLimitW = limit;
        _snapshotPowerMinW = minLimit;
        _snapshotPowerMaxW = maxLimit;
        _snapshotPowerDefaultW = defaultLimit;
        _lastTempError = _snapshotTempCelsius.HasValue ? 0 : -2;
        _lastUtilizationError = _snapshotGpuUtilization.HasValue ? 0 : -1;
        _deviceAlive = _snapshotTempCelsius.HasValue || _snapshotPower.HasValue || _snapshotUsedMb.HasValue;
        _snapshotTicks = now;
        _consecutiveFailures = 0;
        _lastError = 0;
        _unavailable = false;
        _unavailableUntilTicks = 0;
    }

    private static void NoteFailureLocked(string reason, int error)
    {
        _consecutiveFailures++;
        _lastError = error;
        _lastTempError = error;
        _lastUtilizationError = error;
        _deviceAlive = false;
        _snapshotTempCelsius = null;
        _snapshotPower = null;
        _snapshotUsedMb = null;
        _snapshotTotalMb = null;
        _snapshotCoreClockMhz = null;
        _snapshotMemoryClockMhz = null;
        _snapshotGpuUtilization = null;
        _snapshotTicks = -1;
        if (_consecutiveFailures >= FailureThreshold)
        {
            _unavailable = true;
            _unavailableUntilTicks = Environment.TickCount64 + 60_000;
        }
    }

    private static void ClearSnapshotLocked()
    {
        _snapshotTicks = -1;
        _snapshotTempCelsius = null;
        _snapshotPower = null;
        _snapshotUsedMb = null;
        _snapshotTotalMb = null;
        _snapshotCoreClockMhz = null;
        _snapshotMemoryClockMhz = null;
        _snapshotGpuUtilization = null;
        _snapshotPowerLimitW = null;
        _snapshotPowerMinW = null;
        _snapshotPowerMaxW = null;
        _snapshotPowerDefaultW = null;
        _deviceAlive = false;
        _lastTempError = -1;
        _lastUtilizationError = -1;
    }

    private static bool HasNvidiaAdapter()
    {
        try
        {
            IReadOnlyList<GpuAdapter> adapters = MechrevoLite.Gpu.GpuAdapterReader.ReadFromRegistry(out bool enumerationAvailable);
            if (!enumerationAvailable)
            {
                // 枚举不可用时不强行判定为“无 N 卡”，避免误伤；
                // nvidia-smi 自己找不到时会自然失败。
                return true;
            }
            return adapters.Any(adapter => adapter.IsNvidia);
        }
        catch
        {
            return true;
        }
    }

    private static string? ResolveSmi()
    {
        if (_smiPathResolved)
        {
            return _smiPath;
        }
        _smiPathResolved = true;
        foreach (string path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                _smiPath = path;
                return _smiPath;
            }
        }
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                    if (File.Exists(candidate))
                    {
                        _smiPath = candidate;
                        return _smiPath;
                    }
                }
                catch
                {
                }
            }
        }
        _smiPath = null;
        return null;
    }

    private static bool RunSmi(string executable, string arguments, int timeoutMs, out string output)
    {
        output = "";
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start())
            {
                return false;
            }
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return false;
            }
            output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static float? ParseFloat(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }
        string text = parts[index].Trim();
        if (text.Length == 0 || text.Contains("N/A", StringComparison.OrdinalIgnoreCase) || text.Contains("[Not Supported]", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;
    }

    private static int? ParseInt(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }
        string text = parts[index].Trim();
        if (text.Length == 0 || text.Contains("N/A", StringComparison.OrdinalIgnoreCase) || text.Contains("[Not Supported]", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private static long? ParseLong(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }
        string text = parts[index].Trim();
        if (text.Length == 0 || text.Contains("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;
    }
}
