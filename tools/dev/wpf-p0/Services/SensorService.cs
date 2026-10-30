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

    private static int Prefer(int backend, int local)
    {
        // 30/50 GCU 某些遥测字段为 0/未就绪；旧版直接读 LHM/NVML 本地值，
        // 这里在 backend 值为空时回退到本地采样，避免整页显示 --。
        return backend > 0 ? backend : local;
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
            int umiMain = -1;
            int umiSecond = -1;
            int umiMainDuty = -1;
            int umiSecondDuty = -1;
            bool umiFan = hw != null && hw.IsIntelNucMachine
                && UmiFanReader.TryReadFanRpm(out umiMain, out umiSecond);
            bool umiDuty = hw != null && hw.IsIntelNucMachine
                && UmiFanReader.TryReadFanDuty(out umiMainDuty, out umiSecondDuty);
            if (umiFan)
            {
                hw!.ApplyIntelNucFanSpeeds(umiMain, umiSecond);
            }
            if (umiDuty)
            {
                hw!.ApplyIntelNucFanDuty(umiMainDuty, umiSecondDuty);
            }
            float? gpuPower = HardwareControl.gpuPower;
            if (!gpuPower.HasValue && NvmlHelper.Unavailable)
            {
                gpuPower = NvidiaSmiSensor.GetPowerWatts();
            }
            bool preferLocalTemp = hw?.SmuCustomApu == true || hw?.WujieSmuApu == true
                || hw?.IsIntelNucMachine == true || hw?.IsUmiMachine == true;
            int cpuTemp = preferLocalTemp && HardwareControl.cpuTemp > 0
                ? HardwareControl.cpuTemp
                : Prefer(connected ? hw!.CpuTemp : -1, HardwareControl.cpuTemp);
            int gpuTemp = preferLocalTemp && HardwareControl.gpuTemp > 0
                ? HardwareControl.gpuTemp
                : Prefer(connected ? hw!.GpuTemp : -1, HardwareControl.gpuTemp);
            int cpuFanRpm = umiFan && umiMain >= 0 ? umiMain : Prefer(connected ? hw!.CpuFanRpm : -1, HardwareControl.cpuFanRPM);
            int gpuFanRpm = umiFan && umiSecond >= 0 ? umiSecond : Prefer(connected ? hw!.GpuFanRpm : -1, HardwareControl.gpuFanRPM);
            if (cpuFanRpm <= 0 || gpuFanRpm <= 0)
            {
                try
                {
                    if (MechrevoLite.Hardware.XingyaoFanReader.TryReadFanRpm(out int xyMain, out int xySecond))
                    {
                        if (cpuFanRpm <= 0 && xyMain > 0) cpuFanRpm = xyMain;
                        if (gpuFanRpm <= 0 && xySecond > 0) gpuFanRpm = xySecond;
                        hw?.ApplyXingyaoFanSpeeds(xyMain, xySecond);
                    }
                }
                catch
                {
                }
            }
            return new SensorSnapshot(
                HardwareAvailable: true,
                CpuTempC: cpuTemp,
                GpuTempC: gpuTemp,
                CpuFanRpm: cpuFanRpm,
                GpuFanRpm: gpuFanRpm,
                CpuUsagePercent: Prefer(connected ? hw!.CpuUsage : -1, HardwareControl.cpuUsage ?? 0),
                GpuUsagePercent: Prefer(connected ? hw!.GpuUsage : -1, HardwareControl.gpuUsage ?? 0),
                CpuFanDutyPercent: umiDuty && umiMainDuty >= 0 ? umiMainDuty : Prefer(connected ? hw!.CpuFanDuty : -1, HardwareControl.cpuFan ?? 0),
                GpuFanDutyPercent: umiDuty && umiSecondDuty >= 0 ? umiSecondDuty : Prefer(connected ? hw!.GpuFanDuty : -1, HardwareControl.gpuFan ?? 0),
                CpuFrequencyMhz: Prefer(connected ? hw!.CpuFrequency : -1, HardwareControl.cpuFrequency),
                GpuCoreFrequencyMhz: Prefer(connected ? hw!.GpuCoreFreq : -1, HardwareControl.gpuCoreFreq),
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
