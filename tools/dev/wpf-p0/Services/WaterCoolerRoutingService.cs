using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 水冷控制路由：GCU 可用时仍走 GCU（行为不变）；GCU 不可用且检测到 BLE 水冷时走旧 WaterCoolerBle。
/// 没有已保存/已观察到的 BLE 设备时不扫描、不显示 BLE 控件。
/// </summary>
internal sealed class WaterCoolerRoutingService : IDisposable
{
    private readonly MechrevoHw? _hw;
    private readonly MechrevoService? _service;
    private WaterCoolerBle? _ble;
    private int _initStarted;
    private int _connecting;
    private SystemBluetoothConnectionObservation _observation;
    private int _disposed;
    private Timer? _curveTimer;
    private int _curveBusy;
    private int _lastCurveDuty = -1;
    private int _lastCurvePumpProfile = -2;

    internal WaterCoolerRoutingService(MechrevoHw? hw, MechrevoService? service)
    {
        _hw = hw;
        _service = service;
        _curveTimer = new Timer(_ => CurveTick(), null, 2000, 2000);
    }

    internal bool CurveEnabled => MechrevoLite.AppConfig.Is(WaterCoolerCurve.EnabledKey)
        && IsBleRoute
        && _ble?.IsConnected == true;

    internal IReadOnlyList<WaterCoolerCurvePoint> CurvePoints => WaterCoolerCurve.Load();

    internal Task<bool> SetCurveAsync(IReadOnlyList<WaterCoolerCurvePoint> points, int? pumpStart = null, int? pumpTurbo = null)
    {
        if (!IsBleRoute || _ble?.IsConnected != true || points == null || points.Count < 2)
        {
            return Task.FromResult(false);
        }
        if (pumpStart.HasValue)
        {
            WaterCoolerCurve.PumpStartTemp = Math.Clamp(pumpStart.Value, 30, 95);
        }
        if (pumpTurbo.HasValue)
        {
            int start = WaterCoolerCurve.PumpStartTemp;
            WaterCoolerCurve.PumpTurboTemp = Math.Max(start + 5, Math.Clamp(pumpTurbo.Value, 35, 100));
        }
        WaterCoolerCurve.Save(points);
        MechrevoLite.AppConfig.Set(WaterCoolerCurve.EnabledKey, 1);
        MechrevoLite.AppConfig.Flush();
        _lastCurveDuty = -1;
        _lastCurvePumpProfile = -2;
        return ApplyCurveNowAsync();
    }

    internal void DisableCurve()
    {
        MechrevoLite.AppConfig.Set(WaterCoolerCurve.EnabledKey, 0);
        MechrevoLite.AppConfig.Flush();
        _lastCurveDuty = -1;
    }

    private async void CurveTick()
    {
        if (!CurveEnabled || Interlocked.Exchange(ref _curveBusy, 1) != 0 || _ble?.IsConnected != true)
        {
            return;
        }
        try
        {
            int temp = CurrentTemperature();
            int duty = WaterCoolerCurve.Evaluate(CurvePoints, temp);
            if (duty != _lastCurveDuty)
            {
                if (await _ble.WriteFan(duty).ConfigureAwait(false))
                {
                    _lastCurveDuty = duty;
                    Logger.WriteLine($"[water-curve] temp={temp}°C fan={duty}%");
                }
            }
            await ApplyCurvePumpAsync(temp).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[water-curve] failed: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _curveBusy, 0);
        }
    }

    private async Task<bool> ApplyCurveNowAsync()
    {
        if (_ble?.IsConnected != true)
        {
            return false;
        }
        int temp = CurrentTemperature();
        int duty = WaterCoolerCurve.Evaluate(CurvePoints, temp);
        bool fanOk = await _ble.WriteFan(duty).ConfigureAwait(false);
        if (fanOk)
        {
            _lastCurveDuty = duty;
        }
        await ApplyCurvePumpAsync(temp).ConfigureAwait(false);
        return fanOk;
    }

    private async Task ApplyCurvePumpAsync(int temperatureC)
    {
        if (_ble?.IsConnected != true)
        {
            return;
        }
        int start = WaterCoolerCurve.PumpStartTemp;
        int turbo = Math.Max(start + 5, WaterCoolerCurve.PumpTurboTemp);
        int profile = temperatureC < start ? -1 : (temperatureC >= turbo ? 2 : 0);
        if (profile == _lastCurvePumpProfile)
        {
            return;
        }
        if (profile < 0)
        {
            if (await _ble.WritePump(0, 0).ConfigureAwait(false))
            {
                _lastCurvePumpProfile = -1;
            }
            return;
        }
        (int duty, byte voltage) = WaterCoolerBle.PumpSetting(profile);
        if (await _ble.WritePump(duty, voltage).ConfigureAwait(false))
        {
            _lastCurvePumpProfile = profile;
            Logger.WriteLine($"[water-curve] temp={temperatureC}°C pump={profile} ({duty}%, v={voltage})");
        }
    }

    private int CurrentTemperature()
    {
        try
        {
            return Math.Max(0, Math.Max(_hw?.CpuTemp ?? 0, _hw?.GpuTemp ?? 0));
        }
        catch
        {
            return 0;
        }
    }

    internal LiquidCoolingControlRoute Route { get; private set; }

    internal bool IsGcuRoute => Route == LiquidCoolingControlRoute.Gcu;

    internal bool IsBleRoute => Route == LiquidCoolingControlRoute.DirectBle;

    internal bool IsObservedBle => Route == LiquidCoolingControlRoute.BluetoothObserved;

    internal bool HasBleDevice => _ble != null && (_ble.HasSavedDevice || _ble.IsConnected || _observation.IsConnected);

    internal bool IsBleConnected => _ble?.IsConnected == true;

    internal bool IsBoundDevice => _ble?.HasBoundDevice == true;

    internal string BoundDeviceName => _ble?.BoundDeviceName ?? "";

    internal void UnbindDevice()
    {
        _ble?.UnbindDevice();
        RouteChanged?.Invoke();
    }

    internal bool HasBleBackend => _ble != null;

    internal ulong BoundAddress => _ble?.BoundAddress ?? 0uL;

    internal IReadOnlyList<(string Name, ulong Address, short Rssi)> DevicesSnapshot()
    {
        if (_ble == null)
        {
            return Array.Empty<(string, ulong, short)>();
        }
        return _ble.Devices.ToArray();
    }

    internal void ClearDevices() => _ble?.ClearDevices();

    internal void StartPairingScan()
    {
        try
        {
            _ble?.StartScan();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("BLE 配对扫描启动失败：" + ex.Message);
        }
    }

    internal void StopPairingScan() => _ble?.StopScan();

    internal async Task<bool> BindDeviceAsync(ulong address, string name)
    {
        if (_ble == null)
        {
            return false;
        }
        bool ok = await _ble.BindDeviceAsync(address, name).ConfigureAwait(false);
        if (ok)
        {
            Route = LiquidCoolingControlRoute.DirectBle;
        }
        RouteChanged?.Invoke();
        return ok;
    }

    internal bool SupportsFanLed => _ble?.SupportsFanLed == true;

    internal int PumpProfile
    {
        get
        {
            int value = IsGcuRoute ? (_hw?.LcPumpControl ?? -1) : (_ble?.SavedPumpProfile ?? -1);
            return value < -1 ? -1 : value;
        }
    }

    internal int FanProfile
    {
        get
        {
            if (CurveEnabled)
            {
                return -3;
            }
            int value = IsGcuRoute ? (_hw?.LcFanControl ?? -1) : (_ble?.SavedFanProfile ?? -1);
            return value < -1 ? -1 : value;
        }
    }

    internal string StatusText => Route switch
    {
        LiquidCoolingControlRoute.Gcu => _hw?.LcReportedConnected == true ? WpfStrings.L("水冷已连接", "Liquid cooler connected") : WpfStrings.L("水冷未连接", "Liquid cooler disconnected"),
        LiquidCoolingControlRoute.DirectBle => WpfStrings.L("蓝牙水冷已连接", "Bluetooth liquid cooler connected"),
        LiquidCoolingControlRoute.BluetoothObserved => WpfStrings.L("检测到蓝牙水冷（未连接）", "Bluetooth liquid cooler detected (not connected)"),
        _ => WpfStrings.L("未检测到水冷", "No liquid cooler detected")
    };

    internal event Action? RouteChanged;

    internal async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _initStarted, 1) != 0)
        {
            return;
        }
        try
        {
            // 与旧版 GetLiquidCoolingRoute 对齐：GCU 必须“可控制且状态新鲜”，
            // 单看 SupportsLiquidCooling / LcReportedConnected 都不够。
            bool gcuControllable = _hw?.LcGcuControllable == true
                && _hw.IsLcStatusFresh(TimeSpan.FromSeconds(15));
            _ble = new WaterCoolerBle();
            _ble.ConnectionChanged += _ =>
            {
                if (_ble?.IsConnected == true)
                {
                    Route = LiquidCoolingControlRoute.DirectBle;
                }
                RouteChanged?.Invoke();
            };
            try
            {
                _observation = await _ble.ProbeSystemConnectionAsync(_hw?.LcDeviceMacs, force: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("BLE 水冷：系统蓝牙探测失败（保持隐藏）：" + ex.Message);
                Route = LiquidCoolingControlRoute.None;
                return;
            }
            if (_ble.IsConnected)
            {
                Route = LiquidCoolingControlRoute.DirectBle;
                return;
            }
            if (_ble.HasSavedDevice)
            {
                // 只有已保存的 BLE 设备才允许发起自动连接（内部才会扫描）；未知广播不扫描。
                Route = LiquidCoolingControlRoute.BluetoothObserved;
                _ = ConnectSavedAsync();
                return;
            }
            if (_observation.IsConnected)
            {
                Route = LiquidCoolingControlRoute.BluetoothObserved;
                return;
            }
            if (gcuControllable)
            {
                Route = LiquidCoolingControlRoute.Gcu;
                return;
            }
            Route = LiquidCoolingControlRoute.None;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("BLE 水冷路由初始化失败（忽略，GCU/其它功能不受影响）：" + ex.Message);
            Route = LiquidCoolingControlRoute.None;
        }
        finally
        {
            RouteChanged?.Invoke();
        }
    }

    private async Task ConnectSavedAsync()
    {
        if (_ble == null || Interlocked.Exchange(ref _connecting, 1) != 0 || Volatile.Read(in _disposed) != 0)
        {
            return;
        }
        try
        {
            bool ok = await _ble.AutoConnectAsync().ConfigureAwait(false);
            Route = ok && _ble.IsConnected
                ? LiquidCoolingControlRoute.DirectBle
                : LiquidCoolingControlRoute.BluetoothObserved;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("BLE 水冷自动连接失败（保持隐藏/仅状态）：" + ex.Message);
            Route = LiquidCoolingControlRoute.BluetoothObserved;
        }
        finally
        {
            Interlocked.Exchange(ref _connecting, 0);
            RouteChanged?.Invoke();
        }
    }

    internal async Task<bool> RefreshAsync()
    {
        if (IsGcuRoute)
        {
            if (_service == null)
            {
                return false;
            }
            return await _service.RefreshLiquidCoolingStatus().ConfigureAwait(false);
        }
        if (_ble != null)
        {
            try
            {
                _observation = await _ble.ProbeSystemConnectionAsync(_hw?.LcDeviceMacs, force: true).ConfigureAwait(false);
                if (_ble.IsConnected)
                {
                    Route = LiquidCoolingControlRoute.DirectBle;
                }
                else if (_ble.HasSavedDevice)
                {
                    Route = LiquidCoolingControlRoute.BluetoothObserved;
                    _ = ConnectSavedAsync();
                }
                else
                {
                    Route = _observation.IsConnected
                        ? LiquidCoolingControlRoute.BluetoothObserved
                        : LiquidCoolingControlRoute.None;
                }
                return _observation.IsConnected || Route != LiquidCoolingControlRoute.None;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    internal void Disconnect()
    {
        if (_ble == null)
        {
            return;
        }
        try
        {
            _ble.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("BLE 水冷断开失败：" + ex.Message);
        }
        Route = _ble.HasSavedDevice ? LiquidCoolingControlRoute.BluetoothObserved : LiquidCoolingControlRoute.None;
        RouteChanged?.Invoke();
    }

    internal async Task<bool> SetPumpAsync(int profile)
    {
        if (IsGcuRoute)
        {
            if (_service == null)
            {
                return false;
            }
            return profile < 0
                ? await _service.SwitchLcFanAuto().ConfigureAwait(false)
                : await _service.SwitchLcPump(profile).ConfigureAwait(false);
        }
        if (IsBleRoute && _ble != null)
        {
            return await _ble.ApplyPumpProfileAsync(profile, Temperature()).ConfigureAwait(false);
        }
        return false;
    }

    internal async Task<bool> SetFanAsync(int profile)
    {
        if (profile >= 0)
        {
            DisableCurve();
        }
        if (IsGcuRoute)
        {
            if (_service == null)
            {
                return false;
            }
            return profile < 0
                ? await _service.SwitchLcFanAuto().ConfigureAwait(false)
                : await _service.SwitchLcFan(profile).ConfigureAwait(false);
        }
        if (IsBleRoute && _ble != null)
        {
            return await _ble.ApplyFanProfileAsync(profile, Temperature()).ConfigureAwait(false);
        }
        return false;
    }

    internal bool SupportsLight => IsGcuRoute
        ? LiquidCoolingLightService.SupportsColor(_hw)
        : IsBleRoute && _ble?.IsConnected == true;

    internal IReadOnlyList<LiquidLightOption> LightOptions
    {
        get
        {
            if (IsGcuRoute)
            {
                return LiquidCoolingLightService.Options(_hw);
            }
            List<LiquidLightOption> options = LiquidCoolingLightService.Options(null).ToList();
            if (SupportsFanLed)
            {
                options.Insert(options.Count - 1, new LiquidLightOption("fan_rotate", WpfStrings.L("风扇旋转", "Fan spin")));
                options.Insert(options.Count - 1, new LiquidLightOption("fan_rainbow", WpfStrings.L("风扇彩虹", "Fan rainbow")));
            }
            return options;
        }
    }

    internal Color LightColor => IsGcuRoute
        ? LiquidCoolingLightService.SavedColor
        : (_ble?.SavedLightColor ?? LiquidCoolingLightService.SavedColor);

    internal string LightProfile => IsGcuRoute
        ? LiquidCoolingLightService.SavedProfile
        : (_ble?.SavedLightProfile ?? LiquidCoolingLightService.SavedProfile);

    internal async Task<bool> ApplyLightAsync(string profile, Color color)
    {
        // 与旧版一致：GCU 路由才走 GCU 灯效；BLE 路由走 BLE，不互相兜底。
        if (IsGcuRoute)
        {
            bool ok = await LiquidCoolingLightService.ApplyAsync(_service, _hw, profile, color).ConfigureAwait(false);
            if (ok && _service != null)
            {
                _ = _service.RefreshLiquidCoolingStatus();
            }
            return ok;
        }
        if (IsBleRoute && _ble != null)
        {
            return await _ble.ApplyLightProfileAsync(profile, color).ConfigureAwait(false);
        }
        return false;
    }

    internal void SaveLightColor(Color color) => LiquidCoolingLightService.SaveColor(color);

    private int Temperature()
    {
        try
        {
            return Math.Max(0, _hw?.CpuTemp ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            _ble?.Dispose();
        }
        catch
        {
        }
        _curveTimer?.Dispose();
        _curveTimer = null;
        _ble = null;
        Route = LiquidCoolingControlRoute.None;
    }
}
