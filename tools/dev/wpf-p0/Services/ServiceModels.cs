using System.Collections.Generic;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 服务方法结果（⭐ 统一“成功/失败 + 人话原因”的形状）✅
/// <para>⭐ 为什么不直接返回 <c>bool</c>：原窗口大量 <c>if (ok) StatusText=...</c> 的细节文案散落在事件处理器里；
/// 服务层把**原因**带出来，窗口只负责显示、⛔ 不再各自拼同一句话 ✅。</para>
/// </summary>
internal readonly record struct ServiceResult(bool Ok, string Detail = "")
{
    internal static ServiceResult Success(string detail = "") => new(true, detail);

    internal static ServiceResult Fail(string detail) => new(false, detail);
}

/// <summary>⭐ 传感器快照（纯数据；⛔ 不含任何 WPF 控件或格式化字符串）。</summary>
internal sealed record SensorSnapshot(
    bool HardwareAvailable,
    int CpuTempC,
    int GpuTempC,
    int CpuFanRpm,
    int GpuFanRpm,
    int CpuUsagePercent,
    int GpuUsagePercent,
    int CpuFanDutyPercent,
    int GpuFanDutyPercent,
    int CpuFrequencyMhz,
    int GpuCoreFrequencyMhz,
    int FanRpmMaxForPercent,
    double? CpuPowerWatts,
    double? GpuPowerWatts,
    int? GpuMemoryClockMhz,
    int GpuVramUsagePercent)
{
    /// <summary>未连接/读取异常时的空快照（数值全 0、功耗全 null）。</summary>
    internal static SensorSnapshot Empty { get; } = new(
        HardwareAvailable: false,
        CpuTempC: 0,
        GpuTempC: 0,
        CpuFanRpm: 0,
        GpuFanRpm: 0,
        CpuUsagePercent: 0,
        GpuUsagePercent: 0,
        CpuFanDutyPercent: 0,
        GpuFanDutyPercent: 0,
        CpuFrequencyMhz: 0,
        GpuCoreFrequencyMhz: 0,
        FanRpmMaxForPercent: 0,
        CpuPowerWatts: null,
        GpuPowerWatts: null,
        GpuMemoryClockMhz: null,
        GpuVramUsagePercent: -1);
}

/// <summary>⭐ 电池快照（硬件字段 + 系统供电状态；⛔ 不含 UI 文案）。</summary>
internal sealed record BatterySnapshot(
    bool HardwareAvailable,
    int Percent,
    int ProtectionMode,
    int CycleCount,
    string CapacityText,
    bool Abnormal,
    bool AcOnline,
    bool Charging)
{
    internal static BatterySnapshot Empty { get; } = new(
        HardwareAvailable: false,
        Percent: -1,
        ProtectionMode: -1,
        CycleCount: -1,
        CapacityText: "",
        Abnormal: false,
        AcOnline: false,
        Charging: false);
}

/// <summary>⭐ 显示快照（分辨率/刷新率/亮度/屏幕特性；⛔ 不含 slider/checkbox 状态）。</summary>
internal sealed record DisplaySnapshot(
    int ScreenWidth,
    int ScreenHeight,
    int CurrentRefreshHz,
    int MaxRefreshHz,
    int MinRefreshHz,
    bool AutoRefresh,
    int Brightness,
    bool BrightnessSupported,
    bool LcdOverdriveSupported,
    bool LcdOverdrive,
    bool LocalDimmingSupported,
    bool LocalDimming,
    bool FhdSupported,
    bool FhdOn,
    bool HdrSupported,
    bool HdrOn,
    int ColorCalibrationLevel,
    bool ColorCalibrationSupported);

/// <summary>⭐ 屏幕校色档位（默认/sRGB/P3/AdobeRGB/自定义校色）。</summary>
internal sealed record CalibrationOption(int Mode, string Label, bool Custom);

/// <summary>⭐ 色彩增强档位（后端 profile key + 中文标签）。</summary>
internal sealed record DisplayEffectOption(string Mode, string Label);

/// <summary>⭐ 「更多开关」里的一项：key 与显示名（⛔ UI 只负责渲染）。</summary>
internal sealed record QuickSwitchItem(string Key, string Label);

/// <summary>⭐ 「更多开关」分组（组名 + 组内项；顺序即渲染顺序）。</summary>
internal sealed record QuickSwitchGroup(string Title, IReadOnlyList<QuickSwitchItem> Items);

/// <summary>⭐ 灯光自动关闭选项（Label/分钟/秒；数据源仍是 WinForms SettingsForm 的既有表）。</summary>
internal sealed record LightIdleOption(string Label, int Minutes, int IdleSeconds);

/// <summary>⭐ 托盘菜单项配置（key + 显示名）。</summary>
internal sealed record TrayMenuItem(string Key, string Label);

/// <summary>⭐ 充电上限状态（EC 实际状态 + 本地配置；⛔ 不含 UI 控件状态）。</summary>
internal sealed record ChargeLimitState(
    bool Supported,
    bool Enabled,
    int LowPercent,
    int HighPercent,
    int Mode,
    bool EcDirectWriteDisabled,
    bool SliderEnabled,
    int DisplayLimitPercent,
    int SelectedModePercent);

/// <summary>⭐ 性能模式标识 → 显示名（与原窗口 2739-2746 行同一张表，⛔ 不再各自维护）。</summary>
internal static class PerformanceModeLabel
{
    internal static string Of(int mode) => mode switch
    {
        2 => "静音",
        1 => "狂暴",
        0 => "平衡",
        3 or 4 => "自定义",
        _ => "—"
    };
}
