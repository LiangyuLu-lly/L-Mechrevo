using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.Globalization;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Text;
using MechrevoLite.Properties;using System.Threading;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using Windows.Devices.Bluetooth;
using MechrevoLite.Properties;using Windows.Devices.Bluetooth.Advertisement;
using MechrevoLite.Properties;using Windows.Devices.Bluetooth.GenericAttributeProfile;
using MechrevoLite.Properties;using Windows.Devices.Enumeration;
using MechrevoLite.Properties;using Windows.Storage.Streams;
using MechrevoLite.Properties;
namespace MechrevoLite.Hardware;

public class WaterCoolerBle : IDisposable
{
	public static readonly Guid ServiceUuid = new Guid("6e400001-b5a3-f393-e0a9-e50e24dcca9e");

	private static readonly Guid CharTxUuid = new Guid("6e400002-b5a3-f393-e0a9-e50e24dcca9e");

	private static readonly Guid CharRxUuid = new Guid("6e400003-b5a3-f393-e0a9-e50e24dcca9e");

	public const byte CmdFan = 27;

	public const byte CmdPump = 28;

	public const byte CmdHeadRgb = 30;

	public const byte CmdFanRgb = 51;

	public const byte PumpV11 = 0;

	public const byte PumpV12 = 1;

	public const byte PumpV7 = 2;

	public const byte PumpV8 = 3;

	public const byte RgbStatic = 0;

	public const byte RgbBreath = 1;

	public const byte RgbColorful = 2;

	public const byte RgbBreatheColor = 3;

	public const byte FanRgbRotate = 4;

	public const byte FanRgbRainbow = 5;

	public const int ProfileUnset = -2;

	public const int ProfileAutomatic = -1;

	public const int TopPumpProfile = 2;

	public const int TopFanProfile = 3;

	public const string LightCyanStatic = "cyan_static";

	public const string LightCyanBreath = "cyan_breath";

	public const string LightColorful = "colorful";

	public const string LightColorfulBreath = "colorful_breath";

	public const string LightFanRotate = "fan_rotate";

	public const string LightFanRainbow = "fan_rainbow";

	public const string LightOff = "off";

	public const string LightCustomStatic = "custom_static";

	public const string LightCustomBreath = "custom_breath";

	private BluetoothLEAdvertisementWatcher? _watcher;

	private BluetoothLEDevice? _device;

	private GattDeviceService? _service;

	private GattCharacteristic? _txChar;

	private GattCharacteristic? _rxChar;

	private GattSession? _session;

	private TaskCompletionSource<string>? _firmwareReply;

	private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

	private readonly object _lock = new object();

	private int _disposed;

	private int _lastAutomaticPump = -1;

	private int _lastAutomaticFan = -1;

	private DateTime _lastSystemConnectionProbeUtc = DateTime.MinValue;

	private TimeSpan _systemConnectionProbeInterval = TimeSpan.FromSeconds(3L);

	private SystemBluetoothConnectionObservation _lastSystemConnectionObservation;

	private DateTime _connectedAtUtc;

	private int _consecutiveFlowFaults;

	private int _flowNormalSamples;

	public List<(string Name, ulong Address, short Rssi)> Devices { get; } = new List<(string, ulong, short)>();

	public bool IsConnected { get; private set; }

	public string ConnectedName { get; private set; } = "";

	public string FirmwareVersion { get; private set; } = "";

	public bool? IsMeterNormal { get; private set; }

	public bool IsFlowMeterReady
	{
		get
		{
			if (IsConnected)
			{
				return HasReachedFlowMeterReady(_flowNormalSamples, DateTime.UtcNow - _connectedAtUtc);
			}
			return false;
		}
	}

	public bool IsFlowFaultConfirmed => HasConfirmedFlowFault(IsConnected, IsMeterNormal, _consecutiveFlowFaults, DateTime.UtcNow - _connectedAtUtc);

	public bool SupportsFanLed => IsFanLedModel(ConnectedName, FirmwareVersion);

	public int SavedPumpProfile => NormalizeProfile(AppConfig.Get("lc_pump_profile", -2), 2);

	public int SavedFanProfile => NormalizeProfile(AppConfig.Get("lc_fan_profile", -2), 3);

	public string SavedLightProfile => AppConfig.GetString("lc_light_profile", "") ?? "";

	public Color SavedLightColor => Color.FromArgb(AppConfig.Get("lc_light_color", Color.FromArgb(0, 255, 255).ToArgb()));

	public bool HasSavedDevice
	{
		get
		{
			ulong address;
			string name;
			return TryGetSavedDevice(out address, out name);
		}
	}

	public bool HasBoundDevice => TryGetBoundDevice(out _, out _);

	public string BoundDeviceName => TryGetBoundDevice(out _, out string name) ? name : "";

	public ulong BoundAddress => TryGetBoundDevice(out ulong address, out _) ? address : 0uL;

	/// <summary>解除专属绑定；之后才会重新扫描/连接其它水冷。</summary>
	public void UnbindDevice()
	{
		AppConfig.Remove("lc_bound_address");
		AppConfig.Remove("lc_bound_name");
		AppConfig.Flush();
		Logger.WriteLine("BLE 水冷：已解除专属绑定");
	}

	public event Action<bool>? ConnectionChanged;

	internal static bool IsFanLedModel(string? deviceName, string? firmwareVersion)
	{
		if (deviceName == null || !deviceName.Contains("LCT22002", StringComparison.OrdinalIgnoreCase))
		{
			return firmwareVersion?.Contains("LCT22002", StringComparison.OrdinalIgnoreCase) ?? false;
		}
		return true;
	}

	internal static bool IsLikelyWaterCoolerName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		if (!name.Contains("LCT", StringComparison.OrdinalIgnoreCase) && !name.Contains("Oasis", StringComparison.OrdinalIgnoreCase) && !name.Contains("水冷", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("AIO", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public async Task<SystemBluetoothConnectionObservation> ProbeSystemConnectionAsync(IEnumerable<string>? knownAddresses = null, bool force = false)
	{
		if (!force && DateTime.UtcNow - _lastSystemConnectionProbeUtc < _systemConnectionProbeInterval)
		{
			return _lastSystemConnectionObservation;
		}
		try
		{
			List<string> known = new List<string>();
			if (knownAddresses != null)
			{
				known.AddRange(knownAddresses);
			}
			if (TryGetSavedDevice(out ulong address, out string _))
			{
				known.Add(address.ToString("X12", CultureInfo.InvariantCulture));
			}
			DeviceInformationCollection deviceInformationCollection = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), new string[1] { "System.Devices.Aep.DeviceAddress" });
			_systemConnectionProbeInterval = TimeSpan.FromSeconds(3L);
			Logger.ResetChangeTracking("lc-system-bluetooth");
			foreach (DeviceInformation item in deviceInformationCollection)
			{
				string text = item.Name ?? "";
				string address2 = ((!item.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value)) ? "" : (value?.ToString() ?? ""));
				bool flag = IsKnownWaterCoolerAddress(address2, known);
				bool flag2 = text.Contains("LCT", StringComparison.OrdinalIgnoreCase);
				if (flag || flag2)
				{
					_lastSystemConnectionObservation = new SystemBluetoothConnectionObservation(IsConnected: true, text, address2, flag);
					_lastSystemConnectionProbeUtc = DateTime.UtcNow;
					return _lastSystemConnectionObservation;
				}
			}
		}
		catch (Exception ex)
		{
			_systemConnectionProbeInterval = TimeSpan.FromSeconds(30L);
			Logger.WriteLineIfChanged("lc-system-bluetooth", "BLE connected-device probe failed: " + ex.Message);
		}
		_lastSystemConnectionObservation = default;
		_lastSystemConnectionProbeUtc = DateTime.UtcNow;
		return _lastSystemConnectionObservation;
	}

	internal static bool IsKnownWaterCoolerAddress(string? address, IEnumerable<string> knownAddresses)
	{
		string normalized = NormalizeBluetoothAddress(address);
		if (normalized.Length != 12)
		{
			return false;
		}
		return knownAddresses.Any((string known) => string.Equals(normalized, NormalizeBluetoothAddress(known), StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeBluetoothAddress(string? address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return "";
		}
		string text = new string(address.Where(Uri.IsHexDigit).ToArray());
		if (text.Length != 12)
		{
			return "";
		}
		return text.ToUpperInvariant();
	}

	public void ClearDevices()
	{
		lock (_lock)
		{
			Devices.Clear();
		}
	}

	public void StartScan()
	{
		if ((object)_watcher != null)
		{
			return;
		}
		BluetoothLEAdvertisementWatcher bluetoothLEAdvertisementWatcher = new BluetoothLEAdvertisementWatcher
		{
			ScanningMode = BluetoothLEScanningMode.Active,
			SignalStrengthFilter = 
			{
				InRangeThresholdInDBm = (short)(-95)
			}
		};
		bluetoothLEAdvertisementWatcher.Received += OnReceived;
		bluetoothLEAdvertisementWatcher.Stopped += OnWatcherStopped;
		_watcher = bluetoothLEAdvertisementWatcher;
		try
		{
			bluetoothLEAdvertisementWatcher.Start();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("BLE scan start fail: " + ex.Message);
			_watcher = null;
		}
	}

	public void StopScan()
	{
		BluetoothLEAdvertisementWatcher watcher = _watcher;
		_watcher = null;
		if ((object)watcher != null)
		{
			try
			{
				watcher.Stop();
			}
			catch
			{
			}
			watcher.Received -= OnReceived;
			watcher.Stopped -= OnWatcherStopped;
		}
	}

	private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs e)
	{
		Logger.WriteLine("BLE watcher stopped: " + e.Error);
	}

	private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
	{
		string localName = args.Advertisement.LocalName;
		string text = (string.IsNullOrWhiteSpace(localName) ? Strings.UnnamedDevice : localName);
		lock (_lock)
		{
			int num = Devices.FindIndex(((string Name, ulong Address, short Rssi) d) => d.Address == args.BluetoothAddress);
			if (num < 0)
			{
				Devices.Add((text, args.BluetoothAddress, args.RawSignalStrengthInDBm));
			}
			else if (Devices[num].Name != text || Devices[num].Rssi != args.RawSignalStrengthInDBm)
			{
				Devices[num] = (text, args.BluetoothAddress, args.RawSignalStrengthInDBm);
			}
		}
	}

	public async Task<bool> BindDeviceAsync(ulong address, string name)
	{
		if (address == 0)
		{
			return false;
		}
		TryGetBoundDevice(out ulong oldAddress, out string oldName);
		UnbindDevice();
		bool ok = await ConnectAsync(address, name);
		if (!ok && oldAddress != 0)
		{
			AppConfig.Set("lc_bound_address", oldAddress.ToString("X12", CultureInfo.InvariantCulture));
			AppConfig.Set("lc_bound_name", oldName);
		}
		return ok;
	}

	public async Task<bool> AutoConnectAsync()
	{
		ulong savedAddress = 0uL;
		string savedName = "";
		if (TryGetBoundDevice(out ulong boundAddress, out string boundName))
		{
			Logger.WriteLine($"BLE 自动连接：专属绑定 {boundName} ({boundAddress:X12})，不扫描其它水冷");
			return await ConnectAsync(boundAddress, boundName);
		}
		if (TryGetSavedDevice(out savedAddress, out savedName))
		{
			Logger.WriteLine($"BLE 自动连接：先尝试上次设备 {savedName} ({savedAddress:X12})");
			if (await ConnectAsync(savedAddress, savedName))
			{
				return true;
			}
		}
		SystemBluetoothConnectionObservation systemBluetoothConnectionObservation = await ProbeSystemConnectionAsync(null, force: true);
		if (systemBluetoothConnectionObservation.IsConnected && ulong.TryParse(NormalizeBluetoothAddress(systemBluetoothConnectionObservation.Address), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) && result != 0L)
		{
			Logger.WriteLine($"BLE 自动连接：Windows 已连接 {systemBluetoothConnectionObservation.Name} ({result:X12})");
			if (await ConnectAsync(result, string.IsNullOrWhiteSpace(systemBluetoothConnectionObservation.Name) ? savedName : systemBluetoothConnectionObservation.Name))
			{
				return true;
			}
		}
		StartScan();
		try
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(20.0);
			(string Name, ulong Address, short Rssi)? best = null;
			while (DateTime.UtcNow < deadline)
			{
				lock (_lock)
				{
					best = ((IEnumerable<(string, ulong, short)>)(from d in Devices
						where IsLikelyWaterCoolerName(d.Name) || (savedAddress != 0L && d.Address == savedAddress)
						orderby d.Rssi descending
						select d)).Select((Func<(string, ulong, short), (string, ulong, short)?>)(((string Name, ulong Address, short Rssi) d) => (d.Name, d.Address, d.Rssi))).FirstOrDefault();
				}
				if (best.HasValue)
				{
					break;
				}
				await Task.Delay(300);
			}
			if (!best.HasValue)
			{
				Logger.WriteLine("BLE 自动连接：20s 内未发现水冷箱广播");
				return false;
			}
			var (text, num, value) = best.Value;
			Logger.WriteLine($"BLE 自动连接：发现 {text} ({num:X12}) rssi={value}");
			return await ConnectAsync(num, text);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("BLE 自动连接失败: " + ex.Message);
			return false;
		}
		finally
		{
			StopScan();
		}
	}

	public async Task<bool> ConnectAsync(ulong address, string name)
	{
		_ = 7;
		try
		{
			if (TryGetBoundDevice(out ulong bound, out string boundName) && bound != 0 && bound != address)
			{
				Logger.WriteLine($"BLE 水冷：拒绝连接非绑定设备 {name} ({address:X12})，当前绑定 {boundName} ({bound:X12})");
				return false;
			}
			Disconnect();
			BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
			if ((object)device == null)
			{
				Logger.WriteLine("BLE FromBluetoothAddressAsync: null");
				return false;
			}
			GattDeviceServicesResult gattDeviceServicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
			if (gattDeviceServicesResult.Status != GattCommunicationStatus.Success)
			{
				Logger.WriteLine($"BLE GetGattServicesAsync: {gattDeviceServicesResult.Status}");
				try
				{
					device.Dispose();
				}
				catch
				{
				}
				return false;
			}
			GattDeviceService service = gattDeviceServicesResult.Services.FirstOrDefault((GattDeviceService s) => s.Uuid == ServiceUuid);
			if ((object)service == null)
			{
				Logger.WriteLine("BLE NUS service not found");
				try
				{
					device.Dispose();
				}
				catch
				{
				}
				return false;
			}
			GattCharacteristicsResult gattCharacteristicsResult = await service.GetCharacteristicsForUuidAsync(CharTxUuid, BluetoothCacheMode.Uncached);
			if (gattCharacteristicsResult.Status != GattCommunicationStatus.Success)
			{
				Logger.WriteLine($"BLE Get TX characteristic: {gattCharacteristicsResult.Status}");
				try
				{
					service.Dispose();
				}
				catch
				{
				}
				try
				{
					device.Dispose();
				}
				catch
				{
				}
				return false;
			}
			GattCharacteristic gattCharacteristic = gattCharacteristicsResult.Characteristics.FirstOrDefault();
			if ((object)gattCharacteristic == null)
			{
				Logger.WriteLine("BLE TX characteristic not found");
				try
				{
					service.Dispose();
				}
				catch
				{
				}
				try
				{
					device.Dispose();
				}
				catch
				{
				}
				return false;
			}
			_device = device;
			device.ConnectionStatusChanged += OnConnectionStatusChanged;
			_service = service;
			_txChar = gattCharacteristic;
			try
			{
				_session = await GattSession.FromDeviceIdAsync(BluetoothDeviceId.FromId(service.DeviceId));
				if ((object)_session != null)
				{
					_session.MaintainConnection = true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("BLE maintain-connection unavailable: " + ex.Message);
			}
			IsConnected = true;
			ConnectedName = name;
			FirmwareVersion = "";
			IsMeterNormal = null;
			_connectedAtUtc = DateTime.UtcNow;
			_consecutiveFlowFaults = 0;
			_flowNormalSamples = 0;
			GattCharacteristicsResult gattCharacteristicsResult2 = await service.GetCharacteristicsForUuidAsync(CharRxUuid, BluetoothCacheMode.Uncached);
			if (gattCharacteristicsResult2.Status == GattCommunicationStatus.Success)
			{
				GattCharacteristic gattCharacteristic2 = gattCharacteristicsResult2.Characteristics.FirstOrDefault();
				if ((object)gattCharacteristic2 != null)
				{
					_rxChar = gattCharacteristic2;
					gattCharacteristic2.ValueChanged += OnRxValueChanged;
					GattCommunicationStatus gattCommunicationStatus = await gattCharacteristic2.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
					if (gattCommunicationStatus != GattCommunicationStatus.Success)
					{
						Logger.WriteLine($"BLE RX notify enable failed: {gattCommunicationStatus}");
					}
					goto IL_0575;
				}
			}
			Logger.WriteLine($"BLE RX characteristic unavailable: {gattCharacteristicsResult2.Status}");
			goto IL_0575;
			IL_0575:
			TaskCompletionSource<string> firmwareReply = (_firmwareReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
			if (await WriteRawAsync(Encoding.ASCII.GetBytes("sw"), "firmware query") && (object)_rxChar != null && await Task.WhenAny(firmwareReply.Task, Task.Delay(1800)) != firmwareReply.Task)
			{
				Logger.WriteLine("BLE firmware query timed out; continuing in compatibility mode");
			}
			_firmwareReply = null;
			AppConfig.Set("lc_last_address", address.ToString("X12", CultureInfo.InvariantCulture));
			AppConfig.Set("lc_last_name", name);
			if (!TryGetBoundDevice(out _, out _))
			{
				AppConfig.Set("lc_bound_address", address.ToString("X12", CultureInfo.InvariantCulture));
				AppConfig.Set("lc_bound_name", name);
				Logger.WriteLine($"BLE 水冷：首次连接自动绑定 {name} ({address:X12})");
			}
			_lastAutomaticPump = (_lastAutomaticFan = -1);
			Logger.WriteLine($"BLE connected: {name} ({address:X12}) fw={FirmwareVersion} fanLed={SupportsFanLed}");
			ConnectionChanged?.Invoke(obj: true);
			return true;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("BLE connect fail: " + ex2.Message);
			Disconnect();
			return false;
		}
	}

	public void Disconnect()
	{
		bool isConnected = IsConnected;
		_txChar = null;
		if ((object)_rxChar != null)
		{
			_rxChar.ValueChanged -= OnRxValueChanged;
		}
		_rxChar = null;
		_firmwareReply?.TrySetCanceled();
		_firmwareReply = null;
		try
		{
			_service?.Dispose();
		}
		catch
		{
		}
		_service = null;
		try
		{
			_session?.Dispose();
		}
		catch
		{
		}
		_session = null;
		if ((object)_device != null)
		{
			_device.ConnectionStatusChanged -= OnConnectionStatusChanged;
		}
		try
		{
			_device?.Dispose();
		}
		catch
		{
		}
		_device = null;
		IsConnected = false;
		ConnectedName = "";
		FirmwareVersion = "";
		IsMeterNormal = null;
		_connectedAtUtc = default;
		_consecutiveFlowFaults = 0;
		_flowNormalSamples = 0;
		_lastAutomaticPump = (_lastAutomaticFan = -1);
		if (isConnected)
		{
			Logger.WriteLine("BLE disconnected");
			ConnectionChanged?.Invoke(obj: false);
		}
	}

	private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
	{
		if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
		{
			_txChar = null;
			IsConnected = false;
			IsMeterNormal = null;
			_connectedAtUtc = default;
			_consecutiveFlowFaults = 0;
			_flowNormalSamples = 0;
			Logger.WriteLine("BLE physical connection lost: " + ConnectedName);
			ConnectionChanged?.Invoke(obj: false);
		}
	}

	public Task<bool> WriteFan(int dutyPercent)
	{
		return WriteFrame(BuildFanFrame(dutyPercent));
	}

	public Task<bool> WritePump(int dutyPercent, byte voltage)
	{
		return WriteFrame(BuildPumpFrame(dutyPercent, voltage));
	}

	public Task<bool> WriteHeadRgb(byte mode, byte r, byte g, byte b)
	{
		return WriteFrame(BuildRgbFrame(30, on: true, mode, r, g, b));
	}

	public Task<bool> WriteFanRgb(byte mode, byte r = 0, byte g = 0, byte b = 0)
	{
		if (!SupportsFanLed)
		{
			Logger.WriteLine($"BLE fan LED mode {mode} ignored: {ConnectedName} does not expose LCT22002 fan lighting");
			return Task.FromResult(result: false);
		}
		return WriteFrame(BuildRgbFrame(51, on: true, mode, r, g, b));
	}

	public async Task<bool> TurnOffLights()
	{
		bool headOff = await WriteFrame(BuildRgbFrame(30, on: false, 0, 0, 0, 0));
		bool flag = !SupportsFanLed;
		if (!flag)
		{
			flag = await WriteFrame(BuildRgbFrame(51, on: false, 0, 0, 0, 0));
		}
		bool flag2 = flag;
		return headOff & flag2;
	}

	public async Task<bool> ApplyPumpProfileAsync(int profile, int temperatureC, bool persist = true)
	{
		if ((profile < -1 || profile > 3) ? true : false)
		{
			return false;
		}
		int effective;
		if (profile == -1)
		{
			int? num = SelectAutomaticPumpProfileIfUsable(temperatureC, _lastAutomaticPump);
			if (!num.HasValue)
			{
				if (persist)
				{
					AppConfig.Set("lc_pump_profile", profile);
				}
				return true;
			}
			effective = num.Value;
		}
		else
		{
			effective = profile;
		}
		if (profile == -1 && effective == _lastAutomaticPump)
		{
			if (persist)
			{
				AppConfig.Set("lc_pump_profile", profile);
			}
			return true;
		}
		(int, byte) tuple = PumpSetting(effective);
		bool flag = await WritePump(tuple.Item1, tuple.Item2);
		if (flag)
		{
			_lastAutomaticPump = ((profile == -1) ? effective : (-1));
			if (persist)
			{
				AppConfig.Set("lc_pump_profile", profile);
			}
		}
		return flag;
	}

	public async Task<bool> ApplyFanProfileAsync(int profile, int temperatureC, bool persist = true)
	{
		if ((profile < -1 || profile > 4) ? true : false)
		{
			return false;
		}
		int effective;
		if (profile == -1)
		{
			int? num = SelectAutomaticFanProfileIfUsable(temperatureC, _lastAutomaticFan);
			if (!num.HasValue)
			{
				if (persist)
				{
					AppConfig.Set("lc_fan_profile", profile);
				}
				return true;
			}
			effective = num.Value;
		}
		else
		{
			effective = profile;
		}
		if (profile == -1 && effective == _lastAutomaticFan)
		{
			if (persist)
			{
				AppConfig.Set("lc_fan_profile", profile);
			}
			return true;
		}
		bool flag = await WriteFan(FanDuty(effective));
		if (flag)
		{
			_lastAutomaticFan = ((profile == -1) ? effective : (-1));
			if (persist)
			{
				AppConfig.Set("lc_fan_profile", profile);
			}
		}
		return flag;
	}

	public Task<bool> ApplyLightProfileAsync(string profile, bool persist = true)
	{
		return ApplyLightProfileAsync(profile, SavedLightColor, persist);
	}

	public async Task<bool> ApplyLightProfileAsync(string profile, Color color, bool persist = true)
	{
		Color effectiveColor = (color.IsEmpty ? SavedLightColor : color);
		bool flag = profile switch
		{
			"cyan_static" => await WriteHeadRgb(0, 0, byte.MaxValue, byte.MaxValue), 
			"cyan_breath" => await WriteHeadRgb(1, 0, byte.MaxValue, byte.MaxValue), 
			"custom_static" => await WriteHeadRgb(0, effectiveColor.R, effectiveColor.G, effectiveColor.B), 
			"custom_breath" => await WriteHeadRgb(1, effectiveColor.R, effectiveColor.G, effectiveColor.B), 
			"colorful" => await WriteHeadRgb(2, 0, 0, 0), 
			"colorful_breath" => await WriteHeadRgb(3, 0, 0, 0), 
			"fan_rotate" => await WriteFanRgb(4, 0, 0, 0), 
			"fan_rainbow" => await WriteFanRgb(5, 0, 0, 0), 
			"off" => await TurnOffLights(), 
			_ => false, 
		};
		if (flag & persist)
		{
			AppConfig.Set("lc_light_profile", profile);
			string text = profile;
			if ((text == "custom_static" || text == "custom_breath") ? true : false)
			{
				AppConfig.Set("lc_light_color", effectiveColor.ToArgb());
			}
		}
		return flag;
	}

	public async Task RestoreSavedSettingsAsync(int temperatureC)
	{
		int savedPumpProfile = SavedPumpProfile;
		int fan = SavedFanProfile;
		if (savedPumpProfile != -2)
		{
			await ApplyPumpProfileAsync(savedPumpProfile, temperatureC, persist: false);
		}
		if (fan != -2)
		{
			await ApplyFanProfileAsync(fan, temperatureC, persist: false);
		}
		if (!string.IsNullOrWhiteSpace(SavedLightProfile))
		{
			await ApplyLightProfileAsync(SavedLightProfile, persist: false);
		}
	}

	public async Task WaitForStartupStabilizationAsync(TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (IsConnected && !IsFlowMeterReady && DateTime.UtcNow < deadline)
		{
			await Task.Delay(200, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task UpdateAutomaticCoolingAsync(int temperatureC)
	{
		if (IsConnected && temperatureC > 0)
		{
			if (SavedPumpProfile == -1)
			{
				await ApplyPumpProfileAsync(-1, temperatureC, persist: false);
			}
			if (SavedFanProfile == -1)
			{
				await ApplyFanProfileAsync(-1, temperatureC, persist: false);
			}
		}
	}

	internal static int SelectAutomaticPumpProfile(int temperatureC, int current = -1)
	{
		return SelectAutomaticProfile(temperatureC, current, new int[4] { -2147483648, 46, 61, 76 });
	}

	internal static int? SelectAutomaticPumpProfileIfUsable(int temperatureC, int current = -1)
	{
		if (!LiquidCoolingAutoPolicy.TemperatureUsable(temperatureC))
		{
			return null;
		}
		return SelectAutomaticPumpProfile(temperatureC, current);
	}

	internal static int? SelectAutomaticFanProfileIfUsable(int temperatureC, int current = -1)
	{
		if (!LiquidCoolingAutoPolicy.TemperatureUsable(temperatureC))
		{
			return null;
		}
		return SelectAutomaticFanProfile(temperatureC, current);
	}

	internal static bool AutomaticGearAlreadyApplied(int targetGear, int rememberedGear, int reportedGear)
	{
		if (targetGear == rememberedGear)
		{
			return reportedGear == targetGear;
		}
		return false;
	}

	internal static int SelectAutomaticFanProfile(int temperatureC, int current = -1)
	{
		return SelectAutomaticProfile(temperatureC, current, new int[5] { -2147483648, 43, 56, 69, 81 });
	}

	private static int SelectAutomaticProfile(int temperatureC, int current, int[] lowerBounds)
	{
		int num = 0;
		for (int i = 1; i < lowerBounds.Length; i++)
		{
			if (temperatureC >= lowerBounds[i])
			{
				num = i;
			}
		}
		if (current < 0 || current >= lowerBounds.Length || num == current)
		{
			return num;
		}
		if (num > current && temperatureC < lowerBounds[num] + 2)
		{
			return current;
		}
		if (num < current && temperatureC > lowerBounds[current] - 3)
		{
			return current;
		}
		return num;
	}

	internal static (int Duty, byte Voltage) PumpSetting(int profile)
	{
		return profile switch
		{
			0 => (Duty: 45, Voltage: (byte)2), 
			1 => (Duty: 60, Voltage: (byte)3), 
			2 => (Duty: 90, Voltage: (byte)0), 
			3 => (Duty: 100, Voltage: (byte)1), 
			_ => throw new ArgumentOutOfRangeException("profile"), 
		};
	}

	internal static int FanDuty(int profile)
	{
		return profile switch
		{
			0 => 40, 
			1 => 50, 
			2 => 60, 
			3 => 90, 
			4 => 100, 
			_ => throw new ArgumentOutOfRangeException("profile"), 
		};
	}

	internal static int NormalizeProfile(int profile, int maximum)
	{
		if (profile < -1 || profile > maximum)
		{
			return -2;
		}
		return profile;
	}

	internal static bool HasReachedFlowMeterReady(int normalSamples, TimeSpan connectedFor)
	{
		if (normalSamples <= 0)
		{
			return connectedFor >= TimeSpan.FromSeconds(6L);
		}
		return true;
	}

	internal static bool HasConfirmedFlowFault(bool connected, bool? meterNormal, int consecutiveFaults, TimeSpan connectedFor)
	{
		if (connected && meterNormal == false && consecutiveFaults >= 2)
		{
			return connectedFor >= TimeSpan.FromSeconds(8L);
		}
		return false;
	}

	private static bool TryGetBoundDevice(out ulong address, out string name)
	{
		name = AppConfig.GetString("lc_bound_name", Strings.WaterCoolerBox) ?? Strings.WaterCoolerBox;
		if (ulong.TryParse(AppConfig.GetString("lc_bound_address", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
		{
			return address != 0;
		}
		return false;
	}

	private static bool TryGetSavedDevice(out ulong address, out string name)
	{
		name = AppConfig.GetString("lc_last_name", Strings.WaterCoolerBox) ?? Strings.WaterCoolerBox;
		if (ulong.TryParse(AppConfig.GetString("lc_last_address", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
		{
			return address != 0;
		}
		return false;
	}

	internal static byte[] BuildFanFrame(int dutyPercent)
	{
		int num = Math.Clamp(dutyPercent, 0, 100);
		byte[] array = new byte[8] { 254, 27, 0, 0, 0, 0, 0, 239 };
		array[2] = ((num > 0) ? ((byte)1) : ((byte)0));
		array[3] = (byte)num;
		return array;
	}

	internal static byte[] BuildPumpFrame(int dutyPercent, byte voltage)
	{
		if (voltage != 2 && voltage != 3 && voltage != 0 && voltage != 1)
		{
			throw new ArgumentOutOfRangeException("voltage");
		}
		int num = Math.Clamp(dutyPercent, 0, 100);
		byte[] array = new byte[8] { 254, 28, 0, 0, 0, 0, 0, 239 };
		array[2] = ((num > 0) ? ((byte)1) : ((byte)0));
		array[3] = (byte)num;
		array[4] = voltage;
		return array;
	}

	internal static byte[] BuildRgbFrame(byte command, bool on, byte mode, byte r, byte g, byte b)
	{
		if (command != 30 && command != 51)
		{
			throw new ArgumentOutOfRangeException("command");
		}
		return new byte[8]
		{
			254,
			command,
			on ? ((byte)1) : ((byte)0),
			r,
			g,
			b,
			mode,
			239
		};
	}

	private Task<bool> WriteFrame(byte[] frame)
	{
		return WriteRawAsync(frame, $"command 0x{frame[1]:X2}");
	}

	private async Task<bool> WriteRawAsync(byte[] payload, string description)
	{
		bool lockTaken = false;
		try
		{
			if (Volatile.Read(in _disposed) != 0)
			{
				return false;
			}
			await _writeLock.WaitAsync(_disposeCts.Token);
			lockTaken = true;
			if (Volatile.Read(in _disposed) != 0)
			{
				return false;
			}
			GattCharacteristic txChar = _txChar;
			if ((object)txChar == null)
			{
				Logger.WriteLine("BLE write " + description + ": not connected");
				return false;
			}
			GattWriteOption writeOption = (txChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse);
			using DataWriter writer = new DataWriter();
			writer.WriteBytes(payload);
			GattWriteResult gattWriteResult = await txChar.WriteValueWithResultAsync(writer.DetachBuffer(), writeOption);
			if (gattWriteResult.Status != GattCommunicationStatus.Success)
			{
				Logger.WriteLine($"BLE write {description} failed: {gattWriteResult.Status} ({gattWriteResult.ProtocolError})");
				return false;
			}
			Logger.WriteLine("BLE TX " + description + ": " + Convert.ToHexString(payload));
			await Task.Delay(80);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("BLE write " + description + " failed: " + ex.Message);
			return false;
		}
		finally
		{
			if (lockTaken)
			{
				_writeLock.Release();
			}
		}
	}

	private void OnRxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
	{
		try
		{
			using DataReader dataReader = DataReader.FromBuffer(args.CharacteristicValue);
			byte[] array = new byte[dataReader.UnconsumedBufferLength];
			dataReader.ReadBytes(array);
			if (array.Length == 0)
			{
				return;
			}
			if (array[0] == 254)
			{
				bool flag = array.Length >= 4;
				if (flag)
				{
					byte b = array[1];
					bool flag2 = (uint)(b - 49) <= 1u;
					flag = flag2;
				}
				if (flag)
				{
					IsMeterNormal = array[3] == 2;
					if (IsMeterNormal == true)
					{
						_flowNormalSamples++;
						_consecutiveFlowFaults = 0;
					}
					else
					{
						_consecutiveFlowFaults++;
						_flowNormalSamples = 0;
					}
					Logger.WriteLine($"BLE flow meter: state={array[3]} normal={IsMeterNormal}");
				}
			}
			else
			{
				string text = Encoding.UTF8.GetString(array).Trim(new char[4] { '\0', '\r', '\n', ' ' });
				if (text.Length != 0)
				{
					FirmwareVersion = text;
					_firmwareReply?.TrySetResult(text);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("BLE RX parse failed: " + ex.Message);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		_disposeCts.Cancel();
		StopScan();
		bool flag = false;
		try
		{
			flag = _writeLock.Wait(3000);
			Disconnect();
		}
		finally
		{
			if (flag)
			{
				_writeLock.Release();
				_writeLock.Dispose();
			}
			else
			{
				Logger.WriteLine("BLE dispose timed out waiting for an in-flight write; lock left for safe completion.");
			}
			_disposeCts.Dispose();
		}
	}
}
