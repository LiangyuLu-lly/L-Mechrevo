using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;

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

    internal WaterCoolerRoutingService(MechrevoHw? hw, MechrevoService? service)
    {
        _hw = hw;
        _service = service;
    }

    internal LiquidCoolingControlRoute Route { get; private set; }

    internal bool IsGcuRoute => Route == LiquidCoolingControlRoute.Gcu;

    internal bool IsBleRoute => Route == LiquidCoolingControlRoute.DirectBle;

    internal bool IsObservedBle => Route == LiquidCoolingControlRoute.BluetoothObserved;

    internal bool HasBleDevice => _ble != null && (_ble.HasSavedDevice || _ble.IsConnected || _observation.IsConnected);

    internal bool IsBleConnected => _ble?.IsConnected == true;

    internal bool SupportsFanLed => _ble?.SupportsFanLed == true;

    internal int PumpProfile => IsGcuRoute ? (_hw?.LcPumpControl ?? -1) : (_ble?.SavedPumpProfile ?? -1);

    internal int FanProfile => IsGcuRoute ? (_hw?.LcFanControl ?? -1) : (_ble?.SavedFanProfile ?? -1);

    internal string StatusText => Route switch
    {
        LiquidCoolingControlRoute.Gcu => _hw?.LcReportedConnected == true ? "水冷已连接" : "水冷未连接",
        LiquidCoolingControlRoute.DirectBle => "蓝牙水冷已连接",
        LiquidCoolingControlRoute.BluetoothObserved => "检测到蓝牙水冷（未连接）",
        _ => "未检测到水冷"
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
            // GCU 能力位只代表「固件支持水冷」，不等于水冷已经连上。
            // 耀世等机型实际是 BLE 水冷：只要 GCU 没有回报已连接，就继续探测 BLE，
            // 否则会把 BLE 已连接的水冷错误地显示成 GCU「水冷未连接」。
            bool gcuLiquidSupported = _hw?.SupportsLiquidCooling == true;
            if (gcuLiquidSupported && _hw?.LcReportedConnected == true)
            {
                Route = LiquidCoolingControlRoute.Gcu;
                return;
            }
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
            if (gcuLiquidSupported)
            {
                // 没有 BLE 设备时才回落到 GCU（此时 GCU 仍报未连接是真实状态）。
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
                options.Insert(options.Count - 1, new LiquidLightOption("fan_rotate", "风扇旋转"));
                options.Insert(options.Count - 1, new LiquidLightOption("fan_rainbow", "风扇彩虹"));
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
        if (IsGcuRoute)
        {
            return await LiquidCoolingLightService.ApplyAsync(_service, _hw, profile, color).ConfigureAwait(false);
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
        _ble = null;
        Route = LiquidCoolingControlRoute.None;
    }
}
