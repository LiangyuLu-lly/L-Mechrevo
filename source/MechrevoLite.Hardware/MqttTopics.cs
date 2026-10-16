namespace MechrevoLite.Hardware;

internal static class MqttTopics
{
	internal const string FanControl = "Fan/Control";

	internal const string FanStatus = "Fan/Status";

	internal const string FanTable = "Fan/Table";

	internal const string FanFilter = "Fan/#";

	internal const string KeyboardCtrl = "Keyboard/Ctrl";

	internal const string KeyboardStatus = "Keyboard/Status";

	internal const string KeyboardPrefix = "Keyboard/";

	internal const string KeyboardFilter = "Keyboard/#";

	internal const string LightbarCtrl = "HidLightbar/Ctrl";

	internal const string LightbarStatus = "HidLightbar/Status";

	internal const string LightbarPrefix = "HidLightbar/";

	internal const string LightbarFilter = "HidLightbar/#";

	internal const string LogoLightCtrl = "HidLightbar_Logo/Ctrl";

	internal const string LogoLightStatus = "HidLightbar_Logo/Status";

	internal const string LogoLightFilter = "HidLightbar_Logo/#";

	internal const string SettingControl = "Setting/Control";

	internal const string SettingStatus = "Setting/Status";

	internal const string SettingsDeviceSwitchItemStatus = "Settings/DeviceSwitchItemStatus";

	internal const string SettingFilter = "Setting/#";

	internal const string SettingsFilter = "Settings/#";

	internal const string SystemControl = "System/Control";

	internal const string SystemCpuInfo = "System/CpuInfo";

	internal const string SystemGpuInfo = "System/GpuInfo";

	internal const string SystemMemoryInfo = "System/MemoryInfo";

	internal const string SystemFanInfo = "System/FanInfo";

	internal const string SystemBatteryInfo = "System/BatteryInfo";

	internal const string SystemNetworkInfo = "System/NetworkInfo";

	internal const string SystemHardwareInfo = "System/HardwareInfo";

	internal const string SystemFanErrorInfo = "System/FanErrorInfo";

	internal const string SystemBatteryProtection = "System/BatteryProtection";

	internal const string SystemFilter = "System/#";

	internal const string BatteryProtectionControl = "BatteryProtection/Control";

	internal const string GpuDeviceStatus = "GPUDevice/Status";

	internal const string GpuDeviceFilter = "GPUDevice/#";

	internal const string BtLcControl = "BT_LC/Control";

	internal const string BtLcStatus = "BT_LC/Status";

	internal const string BtLcFilter = "BT_LC/#";

	internal const string LchwocControl = "LCHWOC/Control";

	internal const string LchwocStatus = "LCHWOC/Status";

	internal const string LchwocFilter = "LCHWOC/#";

	internal const string WhisperModeStatus = "WhisperMode/Status";

	internal const string CustomizeFilter = "Customize/#";
}
