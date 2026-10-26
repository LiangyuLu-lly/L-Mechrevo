using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 传感器读取（两窗共用同一份快照口径）✅
/// <para>⭐ 解决的原问题：<c>WideSensors()</c> 既读后端又拼中文单位字符串
/// （<c>" ℃" / " RPM" / " W"</c>）⇒ 迷你窗的传感器行只能通过宽屏窗口方法间接拿数据。</para>
/// <para>⛔ 本服务**不格式化**：返回原始数值，显示单位/占位符由各自 View 决定；⛔ 不触发采样
/// （采样仍由既有 2 s 遥测定时器驱动，见 <c>HardwareControl.ReadSensors()</c>）。</para>
/// </summary>
internal interface ISensorService
{
    /// <summary>读一次传感器快照；未连接/异常 ⇒ <see cref="SensorSnapshot.Empty" />。</summary>
    SensorSnapshot Read();
}

/// <inheritdoc />
internal sealed class SensorService : ISensorService
{
    private readonly BackendAccessor _backend;

    internal SensorService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public SensorSnapshot Read()
    {
        MechrevoHw? hw = _backend.Hw;
        bool connected = hw != null && hw.IsConnected;
        try
        {
            // 后端暂时未连上时仍使用本地采样值（LHM/NVML），不要整页归零。
            float? gpuMemoryClock = NvmlHelper.Unavailable ? null : NvmlHelper.GetGpuMemoryClockMhz();
            int? gpuMemoryClockMhz = gpuMemoryClock.HasValue && gpuMemoryClock.Value > 0
                ? (int)Math.Round(gpuMemoryClock.Value)
                : NvidiaSmiSensor.GetMemoryClockMhz();
            float? gpuPower = HardwareControl.gpuPower;
            if (!gpuPower.HasValue && NvmlHelper.Unavailable)
            {
                gpuPower = NvidiaSmiSensor.GetPowerWatts();
            }
            return new SensorSnapshot(
                HardwareAvailable: true,
                CpuTempC: connected ? hw!.CpuTemp : HardwareControl.cpuTemp,
                GpuTempC: connected ? hw!.GpuTemp : HardwareControl.gpuTemp,
                CpuFanRpm: connected ? hw!.CpuFanRpm : HardwareControl.cpuFanRPM,
                GpuFanRpm: connected ? hw!.GpuFanRpm : HardwareControl.gpuFanRPM,
                CpuUsagePercent: connected ? hw!.CpuUsage : (HardwareControl.cpuUsage ?? 0),
                GpuUsagePercent: connected ? hw!.GpuUsage : (HardwareControl.gpuUsage ?? 0),
                CpuFanDutyPercent: connected ? hw!.CpuFanDuty : (HardwareControl.cpuFan ?? 0),
                GpuFanDutyPercent: connected ? hw!.GpuFanDuty : (HardwareControl.gpuFan ?? 0),
                CpuFrequencyMhz: connected ? hw!.CpuFrequency : HardwareControl.cpuFrequency,
                GpuCoreFrequencyMhz: connected ? hw!.GpuCoreFreq : HardwareControl.gpuCoreFreq,
                FanRpmMaxForPercent: connected ? hw!.FanRpmMaxForPercent : 5000,
                CpuPowerWatts: HardwareControl.cpuPower,
                GpuPowerWatts: gpuPower,
                GpuMemoryClockMhz: gpuMemoryClockMhz,
                GpuVramUsagePercent: HardwareControl.vramUsage ?? -1);
        }
        catch
        {
            return SensorSnapshot.Empty;
        }
    }
}
