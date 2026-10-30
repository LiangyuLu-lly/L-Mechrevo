using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// Intel GPU 只读遥测：Level Zero Sysman（ze_loader.dll）。
/// 只读取集成 GPU 的频率/温度/功耗；接口不存在或没有权限时静默失败，不写任何设备状态。
/// </summary>
internal static class IntelLevelZeroTelemetry
{
    private static readonly object Gate = new object();

    private static bool _resolved;
    private static bool _available;
    private static string _detail = "(未初始化)";

    private static IntPtr _loader;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesInitDelegate(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesDriverGetDelegate(IntPtr count, IntPtr drivers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesDeviceGetDelegate(IntPtr driver, IntPtr count, IntPtr devices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesDeviceEnumFrequencyDelegate(IntPtr device, IntPtr count, IntPtr domains);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesFrequencyGetStateDelegate(IntPtr frequency, IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesDeviceEnumPowerDelegate(IntPtr device, IntPtr count, IntPtr domains);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesPowerGetEnergyCounterDelegate(IntPtr power, IntPtr counter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesDeviceEnumTemperatureDelegate(IntPtr device, IntPtr count, IntPtr sensors);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZesTemperatureGetStateDelegate(IntPtr sensor, out double temperature);

    private static ZesFrequencyGetStateDelegate? _freqGetState;
    private static ZesPowerGetEnergyCounterDelegate? _powerGetEnergy;
    private static ZesTemperatureGetStateDelegate? _tempGetState;

    private static IntPtr _frequency;
    private static IntPtr _power;
    private static IntPtr _temperature;

    private static ulong _lastEnergy;
    private static ulong _lastEnergyTimestamp;
    private static long _lastEnergyTick;

    internal static string Detail => _detail;

    /// <summary>读取一次 Intel iGPU 遥测；任何一项成功即可返回 true。</summary>
    internal static bool TryRead(out double? frequencyMhz, out double? powerWatts, out double? temperatureC, out string detail)
    {
        frequencyMhz = null;
        powerWatts = null;
        temperatureC = null;
        detail = "";
        if (!EnsureInitialized(out detail))
        {
            return false;
        }

        try
        {
            if (_frequency != IntPtr.Zero && _freqGetState != null)
            {
                IntPtr state = Marshal.AllocHGlobal(512);
                try
                {
                    for (int i = 0; i < 512; i++) Marshal.WriteByte(state, i, 0);
                    int result = _freqGetState(_frequency, state);
                    if (result == 0)
                    {
                        // zes_freq_state_t：stype/pNext 后是 currentVoltage/request/tdp/efficient/actual（double）。
                        frequencyMhz = PickDouble(state, 48, 40, 56, 24, 32);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(state);
                }
            }

            if (_temperature != IntPtr.Zero && _tempGetState != null)
            {
                int result = _tempGetState(_temperature, out double temp);
                if (result == 0 && temp > 0 && temp < 150)
                {
                    temperatureC = temp;
                }
            }

            if (_power != IntPtr.Zero && _powerGetEnergy != null)
            {
                IntPtr counter = Marshal.AllocHGlobal(128);
                try
                {
                    for (int i = 0; i < 128; i++) Marshal.WriteByte(counter, i, 0);
                    int result = _powerGetEnergy(_power, counter);
                    if (result == 0)
                    {
                        ulong energy = (ulong)Marshal.ReadInt64(counter, 16);
                        ulong timestamp = (ulong)Marshal.ReadInt64(counter, 24);
                        long now = Environment.TickCount64;
                        if (_lastEnergyTimestamp != 0 && timestamp > _lastEnergyTimestamp && energy >= _lastEnergy)
                        {
                            ulong deltaEnergy = energy - _lastEnergy;
                            ulong deltaTime = timestamp - _lastEnergyTimestamp;
                            if (deltaTime > 0 && deltaEnergy > 0)
                            {
                                // energy 为微焦，timestamp 为微秒；µJ / µs = W。
                                double watts = (double)deltaEnergy / deltaTime;
                                if (watts >= 0 && watts < 200)
                                {
                                    powerWatts = watts;
                                }
                            }
                        }
                        _lastEnergy = energy;
                        _lastEnergyTimestamp = timestamp;
                        _lastEnergyTick = now;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(counter);
                }
            }
        }
        catch (Exception ex)
        {
            detail = "读取异常：" + ex.GetType().Name + " " + ex.Message;
            return false;
        }

        detail = string.Format(
            "freq={0} temp={1} power={2}",
            frequencyMhz.HasValue ? frequencyMhz.Value.ToString("0") + "MHz" : "-",
            temperatureC.HasValue ? temperatureC.Value.ToString("0.0") + "C" : "-",
            powerWatts.HasValue ? powerWatts.Value.ToString("0.0") + "W" : "-");
        return frequencyMhz.HasValue || temperatureC.HasValue || powerWatts.HasValue;
    }

    private static double? PickDouble(IntPtr buffer, params int[] offsets)
    {
        foreach (int offset in offsets)
        {
            double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer, offset));
            if (value > 100 && value < 5000)
            {
                return value;
            }
        }
        return null;
    }

    private static bool EnsureInitialized(out string detail)
    {
        lock (Gate)
        {
            if (_resolved)
            {
                detail = _detail;
                return _available;
            }
            _resolved = true;
            try
            {
                if (!TryLoadLoader(out _loader, out string loadDetail))
                {
                    _detail = "ze_loader.dll 未找到：" + loadDetail;
                    detail = _detail;
                    Logger.WriteLineThrottled("intel-l0-loader", "[intel-l0] " + _detail, 60000);
                    return false;
                }
                if (!TryGetExport(_loader, "zesInit", out IntPtr initPtr, out _) ||
                    !TryGetExport(_loader, "zesDriverGet", out IntPtr driverGetPtr, out _) ||
                    !TryGetExport(_loader, "zesDeviceGet", out IntPtr deviceGetPtr, out _))
                {
                    _detail = "ze_loader.dll 缺少 Sysman 导出（zesInit/zesDriverGet/zesDeviceGet）";
                    detail = _detail;
                    Logger.WriteLineThrottled("intel-l0-exports", "[intel-l0] " + _detail, 60000);
                    return false;
                }

                ZesInitDelegate zesInit = Marshal.GetDelegateForFunctionPointer<ZesInitDelegate>(initPtr);
                ZesDriverGetDelegate driverGet = Marshal.GetDelegateForFunctionPointer<ZesDriverGetDelegate>(driverGetPtr);
                ZesDeviceGetDelegate deviceGet = Marshal.GetDelegateForFunctionPointer<ZesDeviceGetDelegate>(deviceGetPtr);
                int result = zesInit(0);
                if (result != 0)
                {
                    _detail = "zesInit 返回 0x" + result.ToString("X8");
                    detail = _detail;
                    Logger.WriteLineThrottled("intel-l0-init", "[intel-l0] " + _detail, 60000);
                    return false;
                }

                IntPtr driver = FirstHandle((countPtr, array) => driverGet(countPtr, array), out int driverCount);
                if (driver == IntPtr.Zero)
                {
                    _detail = "zesDriverGet 没有驱动（count=" + driverCount + "）";
                    detail = _detail;
                    Logger.WriteLineThrottled("intel-l0-driver", "[intel-l0] " + _detail, 60000);
                    return false;
                }

                IntPtr device = FirstHandle((countPtr, array) => deviceGet(driver, countPtr, array), out int deviceCount);
                if (device == IntPtr.Zero)
                {
                    _detail = "zesDeviceGet 没有设备（count=" + deviceCount + "）";
                    detail = _detail;
                    Logger.WriteLineThrottled("intel-l0-device", "[intel-l0] " + _detail, 60000);
                    return false;
                }

                ResolveFrequency(device);
                ResolvePower(device);
                ResolveTemperature(device);

                _available = _frequency != IntPtr.Zero || _power != IntPtr.Zero || _temperature != IntPtr.Zero;
                _detail = "Level Zero Sysman 可用：freq=" + (_frequency != IntPtr.Zero)
                    + " power=" + (_power != IntPtr.Zero)
                    + " temp=" + (_temperature != IntPtr.Zero);
                detail = _detail;
                Logger.WriteError("[intel-l0] " + _detail);
                return _available;
            }
            catch (Exception ex)
            {
                _detail = "初始化异常：" + ex.GetType().Name + " " + ex.Message;
                detail = _detail;
                Logger.WriteLineThrottled("intel-l0-ex", "[intel-l0] " + _detail, 60000);
                return false;
            }
        }
    }

    private static void ResolveFrequency(IntPtr device)
    {
        if (!TryGetExport(_loader, "zesDeviceEnumFrequencyDomains", out IntPtr enumPtr, out _) ||
            !TryGetExport(_loader, "zesFrequencyGetState", out IntPtr statePtr, out _))
        {
            return;
        }
        ZesDeviceEnumFrequencyDelegate enumerate = Marshal.GetDelegateForFunctionPointer<ZesDeviceEnumFrequencyDelegate>(enumPtr);
        _freqGetState = Marshal.GetDelegateForFunctionPointer<ZesFrequencyGetStateDelegate>(statePtr);
        _frequency = FirstHandle((countPtr, array) => enumerate(device, countPtr, array), out _);
        if (_frequency == IntPtr.Zero)
        {
            _freqGetState = null;
        }
    }

    private static void ResolvePower(IntPtr device)
    {
        if (!TryGetExport(_loader, "zesDeviceEnumPowerDomains", out IntPtr enumPtr, out _) ||
            !TryGetExport(_loader, "zesPowerGetEnergyCounter", out IntPtr energyPtr, out _))
        {
            return;
        }
        ZesDeviceEnumPowerDelegate enumerate = Marshal.GetDelegateForFunctionPointer<ZesDeviceEnumPowerDelegate>(enumPtr);
        _powerGetEnergy = Marshal.GetDelegateForFunctionPointer<ZesPowerGetEnergyCounterDelegate>(energyPtr);
        _power = FirstHandle((countPtr, array) => enumerate(device, countPtr, array), out _);
        if (_power == IntPtr.Zero)
        {
            _powerGetEnergy = null;
        }
    }

    private static void ResolveTemperature(IntPtr device)
    {
        if (!TryGetExport(_loader, "zesDeviceEnumTemperatureSensors", out IntPtr enumPtr, out _) ||
            !TryGetExport(_loader, "zesTemperatureGetState", out IntPtr statePtr, out _))
        {
            return;
        }
        ZesDeviceEnumTemperatureDelegate enumerate = Marshal.GetDelegateForFunctionPointer<ZesDeviceEnumTemperatureDelegate>(enumPtr);
        _tempGetState = Marshal.GetDelegateForFunctionPointer<ZesTemperatureGetStateDelegate>(statePtr);
        _temperature = FirstHandle((countPtr, array) => enumerate(device, countPtr, array), out _);
        if (_temperature == IntPtr.Zero)
        {
            _tempGetState = null;
        }
    }

    private static IntPtr FirstHandle(Func<IntPtr, IntPtr, int> enumerate, out int count)
    {
        count = 0;
        IntPtr countPtr = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(countPtr, 0);
            int result = enumerate(countPtr, IntPtr.Zero);
            count = Marshal.ReadInt32(countPtr);
            if (result != 0 || count <= 0)
            {
                return IntPtr.Zero;
            }
            IntPtr array = Marshal.AllocHGlobal(IntPtr.Size * count);
            try
            {
                for (int i = 0; i < IntPtr.Size * count; i++) Marshal.WriteByte(array, i, 0);
                result = enumerate(countPtr, array);
                if (result != 0)
                {
                    return IntPtr.Zero;
                }
                return Marshal.ReadIntPtr(array, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(array);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(countPtr);
        }
    }

    private static bool TryLoadLoader(out IntPtr handle, out string detail)
    {
        handle = IntPtr.Zero;
        foreach (string name in new[] { "ze_loader.dll", "ze_loader64.dll" })
        {
            if (NativeLibrary.TryLoad(name, out handle))
            {
                detail = name;
                return true;
            }
        }
        string bin = AppContext.BaseDirectory;
        foreach (string path in new[]
        {
            System.IO.Path.Combine(bin, "ze_loader.dll"),
            System.IO.Path.Combine(bin, "ze_intel_gpu.dll"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ze_loader.dll")
        })
        {
            try
            {
                if (System.IO.File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
                {
                    detail = path;
                    return true;
                }
            }
            catch
            {
            }
        }
        detail = "系统/应用目录均未找到 ze_loader.dll";
        return false;
    }

    private static bool TryGetExport(IntPtr handle, string name, out IntPtr address, out string detail)
    {
        try
        {
            if (NativeLibrary.TryGetExport(handle, name, out address))
            {
                detail = name;
                return true;
            }
        }
        catch
        {
        }
        address = IntPtr.Zero;
        detail = "缺少导出：" + name;
        return false;
    }
}
