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
        if (hw == null || !hw.IsConnected)
        {
            return SensorSnapshot.Empty;
        }
        try
        {
            // NVML 显存频率：与原窗口 WideSensors 同一来源；⛔ 不可用时返回 null（不编 0）。
            float? gpuMemoryClock = NvmlHelper.GetGpuMemoryClockMhz();
            int? gpuMemoryClockMhz = gpuMemoryClock.HasValue && gpuMemoryClock.Value > 0
                ? (int)Math.Round(gpuMemoryClock.Value)
                : null;
            return new SensorSnapshot(
                HardwareAvailable: true,
                CpuTempC: hw.CpuTemp,
                GpuTempC: hw.GpuTemp,
                CpuFanRpm: hw.CpuFanRpm,
                GpuFanRpm: hw.GpuFanRpm,
                CpuUsagePercent: hw.CpuUsage,
                GpuUsagePercent: hw.GpuUsage,
                CpuFanDutyPercent: hw.CpuFanDuty,
                GpuFanDutyPercent: hw.GpuFanDuty,
                CpuFrequencyMhz: hw.CpuFrequency,
                GpuCoreFrequencyMhz: hw.GpuCoreFreq,
                FanRpmMaxForPercent: hw.FanRpmMaxForPercent,
                CpuPowerWatts: HardwareControl.cpuPower,
                GpuPowerWatts: HardwareControl.gpuPower,
                GpuMemoryClockMhz: gpuMemoryClockMhz,
                GpuVramUsagePercent: HardwareControl.vramUsage ?? -1);
        }
        catch
        {
            return SensorSnapshot.Empty;
        }
    }
}
