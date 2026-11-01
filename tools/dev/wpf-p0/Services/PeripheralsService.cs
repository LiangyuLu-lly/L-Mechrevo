using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Resources;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 外设电量域（⛔ 不是笔记本电池）：读取已连接的蓝牙（BLE GATT Battery Service）与
/// HID 键鼠（HID Feature Report 里的 Battery Strength）的剩余电量。
/// <para>⭐ 基准对照：旧版 <c>SettingsForm.RefreshPeripheralsBattery()</c>（旧 WinForms 源码
/// SettingsForm.cs:12217）只调了 <c>PeripheralsProvider.RefreshBatteryForAllDevices(force:true)</c>，
/// 而旧 <c>PeripheralsProvider</c> 在本仓库基线里是**空实现**（历史包/解包/checkpoint 均相同），
/// 所以这里按“等价行为 + 真正可用”补：⚠️ 不新增高频轮询，只在设置页打开后延迟一次扫描（带 60s 缓存）。</para>
/// <para>⛔ 失败/无设备一律返回空列表：调用方按“列表为空 ⇒ 隐藏卡片”处理，不弹错、不阻塞 UI。</para>
/// </summary>
internal interface IPeripheralsService
{
    /// <summary>读取当前已连接外设的电池信息（一次扫描，内部有 60s 缓存）。</summary>
    Task<IReadOnlyList<PeripheralBatteryInfo>> GetAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class PeripheralsService : IPeripheralsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BluetoothTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HidTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private IReadOnlyList<PeripheralBatteryInfo> _cache = Array.Empty<PeripheralBatteryInfo>();
    private long _cacheTick = long.MinValue;
    private bool _hasCache;

    public async Task<IReadOnlyList<PeripheralBatteryInfo>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (HasFreshCache())
        {
            return _cache;
        }

        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双检：等锁期间可能已被另一个调用刷新
            if (HasFreshCache())
            {
                return _cache;
            }

            IReadOnlyList<PeripheralBatteryInfo> result = await ScanAsync(cancellationToken).ConfigureAwait(false);
            _cache = result;
            _cacheTick = Environment.TickCount64;
            _hasCache = true;
            return result;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private bool HasFreshCache()
    {
        return _hasCache && Environment.TickCount64 - _cacheTick < (long)CacheTtl.TotalMilliseconds;
    }

    private static async Task<IReadOnlyList<PeripheralBatteryInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        List<PeripheralBatteryInfo> items = new();

        // ① 蓝牙（BLE GATT Battery Service 0x180F / 0x2A19）—— 系统设置同源
        try
        {
            IReadOnlyList<PeripheralBatteryInfo> bluetooth = await RunWithTimeoutAsync(
                ScanBluetoothAsync(cancellationToken), BluetoothTimeout, cancellationToken).ConfigureAwait(false);
            if (bluetooth != null)
            {
                items.AddRange(bluetooth);
            }
        }
        catch
        {
            // 无蓝牙适配器/无已连接设备/权限受限：忽略
        }

        // ② HID 键鼠（HID Feature Report 的 Battery Strength，usage page 0x06 / usage 0x20）
        if (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<PeripheralBatteryInfo> hid = await RunWithTimeoutAsync(
                    Task.Run(() => ScanHid(cancellationToken), cancellationToken), HidTimeout, cancellationToken).ConfigureAwait(false);
                if (hid != null)
                {
                    items.AddRange(hid);
                }
            }
            catch
            {
                // HID 枚举失败/无设备：忽略
            }
        }

        // 同一外设可能同时被 BLE 与 HID 读到（蓝牙鼠标）：按名称去重，保留先到的 BLE 结果
        Dictionary<string, PeripheralBatteryInfo> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (PeripheralBatteryInfo item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Percent < 0)
            {
                continue;
            }
            string key = item.Name.Trim();
            if (!unique.ContainsKey(key))
            {
                unique[key] = item;
            }
        }

        return unique.Values
            .OrderBy(item => IsBluetoothSource(item.Source) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .Take(6)
            .ToArray();
    }

    private static async Task<T> RunWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (completed != task)
        {
            // 超时/取消：观察异常，避免 UnobservedTaskException（结果丢弃）
            _ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return default!;
        }
        return await task.ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // 蓝牙：BLE GATT Battery Service
    // ------------------------------------------------------------------
    private static readonly Guid BatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = new("00002a19-0000-1000-8000-00805f9b34fb");

    private static async Task<IReadOnlyList<PeripheralBatteryInfo>> ScanBluetoothAsync(CancellationToken cancellationToken)
    {
        List<PeripheralBatteryInfo> result = new();
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            new[] { "System.Devices.Aep.DeviceAddress" }).AsTask(cancellationToken).ConfigureAwait(false);

        foreach (DeviceInformation deviceInfo in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseBluetoothAddress(deviceInfo, out ulong address))
            {
                continue;
            }

            BluetoothLEDevice? device = null;
            GattDeviceService? service = null;
            try
            {
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken).ConfigureAwait(false);
                if (device == null)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(deviceInfo.Name) ? device.Name : deviceInfo.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = WpfStrings.PeripheralBluetoothDevice;
                }

                GattDeviceServicesResult services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (services.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                service = services.Services.FirstOrDefault(s => s.Uuid == BatteryServiceUuid);
                if (service == null)
                {
                    continue;
                }

                GattCharacteristicsResult characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (characteristics.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                GattCharacteristic? level = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == BatteryLevelUuid);
                if (level == null)
                {
                    continue;
                }

                GattReadResult read = await level.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false);
                if (read.Status != GattCommunicationStatus.Success || read.Value == null || read.Value.Length < 1)
                {
                    continue;
                }

                int percent;
                using (DataReader reader = DataReader.FromBuffer(read.Value))
                {
                    percent = reader.ReadByte();
                }
                if (percent is >= 0 and <= 100)
                {
                    result.Add(new PeripheralBatteryInfo(name.Trim(), WpfStrings.PeripheralSourceBluetooth, percent));
                }
            }
            catch
            {
                // 单个设备失败不影响其它设备
            }
            finally
            {
                service?.Dispose();
                device?.Dispose();
            }
        }

        return result;
    }

    private static bool TryParseBluetoothAddress(DeviceInformation deviceInfo, out ulong address)
    {
        address = 0;
        try
        {
            if (!deviceInfo.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object? value))
            {
                return false;
            }
            string text = value?.ToString() ?? "";
            text = text.Replace(":", "").Replace("-", "").Trim();
            return text.Length == 12 && ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out address);
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // HID：HID Feature Report 的 Battery Strength（usage page 0x06 / usage 0x20）
    // ------------------------------------------------------------------
    private static bool IsBluetoothSource(string source) =>
        string.Equals(source, WpfStrings.PeripheralSourceBluetooth, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, "蓝牙", StringComparison.Ordinal);

    private const ushort BatteryUsagePage = 0x0006;
    private const ushort BatteryStrengthUsage = 0x0020;
    private const ushort HidpFeatureReportType = 2;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    private static IReadOnlyList<PeripheralBatteryInfo> ScanHid(CancellationToken cancellationToken)
    {
        List<PeripheralBatteryInfo> result = new();
        List<HidDeviceWin> devices;
        try
        {
            devices = HidDeviceWin.Enumerate();
        }
        catch
        {
            return result;
        }

        try
        {
            foreach (HidDeviceWin device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (device.FeatureReportLength == 0 || device.FeatureReportLength > 512)
                    {
                        continue;
                    }
                    PeripheralBatteryInfo? info = TryReadHidBattery(device);
                    if (info != null)
                    {
                        result.Add(info);
                    }
                }
                catch
                {
                    // 单个 HID 设备失败不影响其它设备
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        finally
        {
            foreach (HidDeviceWin device in devices)
            {
                device.Dispose();
            }
        }

        return result;
    }

    private static PeripheralBatteryInfo? TryReadHidBattery(HidDeviceWin device)
    {
        using SafeFileHandle handle = CreateFile(device.Path, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
        {
            return null;
        }

        try
        {
            if (!TryFindBatteryStrengthCaps(preparsed, out HidpValueCaps caps))
            {
                return null;
            }

            int reportLength = device.FeatureReportLength;
            if (reportLength <= 0 || reportLength > 512)
            {
                return null;
            }

            byte[] report = new byte[reportLength];
            if (caps.ReportID != 0)
            {
                report[0] = caps.ReportID;
            }
            if (!HidD_GetFeature(handle, report, report.Length))
            {
                return null;
            }

            if (!TryReadBatteryValue(report, caps, out int percent))
            {
                return null;
            }

            string name = ReadHidProductName(handle);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.Format(WpfStrings.PeripheralHidDeviceFormat, device.VendorID.ToString("X4"), device.ProductID.ToString("X4"));
            }
            return new PeripheralBatteryInfo(name.Trim(), "HID", percent);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static string ReadHidProductName(SafeFileHandle handle)
    {
        try
        {
            char[] buffer = new char[256];
            if (HidD_GetProductString(handle, buffer, buffer.Length * sizeof(char)))
            {
                string text = new string(buffer);
                int end = text.IndexOf('\0');
                return end >= 0 ? text[..end] : text;
            }
        }
        catch
        {
        }
        return "";
    }

    private static bool TryFindBatteryStrengthCaps(IntPtr preparsed, out HidpValueCaps batteryCaps)
    {
        batteryCaps = default;
        if (Marshal.SizeOf<HidpValueCaps>() != 72)
        {
            // ⚠️ 结构必须与 HIDP_VALUE_CAPS（x64=72 字节）一致，不一致就整条 HID 路径放弃（安全优先）
            return false;
        }

        ushort count = 0;
        _ = HidP_GetValueCaps(HidpFeatureReportType, IntPtr.Zero, ref count, preparsed);
        if (count == 0 || count > 512)
        {
            return false;
        }

        int capsSize = Marshal.SizeOf<HidpValueCaps>();
        IntPtr buffer = Marshal.AllocHGlobal(capsSize * count);
        try
        {
            ushort length = count;
            if (HidP_GetValueCaps(HidpFeatureReportType, buffer, ref length, preparsed) < 0 || length == 0)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                HidpValueCaps caps = Marshal.PtrToStructure<HidpValueCaps>(buffer + i * capsSize);
                if (caps.UsagePage != BatteryUsagePage)
                {
                    continue;
                }
                bool matches = caps.IsRange
                    ? caps.UsageMin <= BatteryStrengthUsage && BatteryStrengthUsage <= caps.UsageMax
                    : caps.UsageMin == BatteryStrengthUsage;
                if (matches && caps.BitSize is > 0 and <= 32)
                {
                    batteryCaps = caps;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryReadBatteryValue(byte[] report, HidpValueCaps caps, out int percent)
    {
        percent = -1;
        int dataOffset = caps.ReportID == 0 ? 0 : 1;
        int bits = caps.BitSize;
        if (bits <= 0 || bits > 32)
        {
            return false;
        }

        ulong raw = 0;
        for (int bit = 0; bit < bits; bit++)
        {
            int byteIndex = dataOffset + bit / 8;
            if (byteIndex >= report.Length)
            {
                return false;
            }
            if ((report[byteIndex] & (1 << (bit % 8))) != 0)
            {
                raw |= 1UL << bit;
            }
        }

        long value = (long)raw;
        // 有符号逻辑范围（少见）：Battery Strength 一律按无符号处理
        if (value < caps.LogicalMin || value > caps.LogicalMax)
        {
            return false;
        }

        if (caps.LogicalMax <= 100)
        {
            percent = (int)value;
        }
        else if (caps.LogicalMax <= 255)
        {
            percent = (int)Math.Round(value * 100.0 / caps.LogicalMax);
        }
        else
        {
            return false;
        }

        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportID;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public bool IsRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)] public bool HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public ushort Reserved2_0;
        public ushort Reserved2_1;
        public ushort Reserved2_2;
        public ushort Reserved2_3;
        public ushort Reserved2_4;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        // union：IsRange 时用 Range 分支，否则用 NotRange 分支（前 2 字节同为 Usage）
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetValueCaps(ushort reportType, IntPtr valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, [Out] char[] buffer, int bufferLength);
}
