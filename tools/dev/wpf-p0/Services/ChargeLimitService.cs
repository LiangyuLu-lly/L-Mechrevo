using MechrevoLite;
using MechrevoLite.Battery;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 充电上限域（EC 阈值 / 三档 / 无级滑块 / EC 直写开关；两窗共享）✅
/// <para>⭐ 解决的原问题：<c>WideChargeLimit()</c>/<c>WideBatteryLimitSlider()</c> 返回控件文本与
/// Toggle 状态，<c>WideApplyChargeLimit</c> 直接触发宽屏窗口的 <c>ChargeLimitApply_Click</c>。</para>
/// <para>⭐ 本服务只做**后端/配置**：EC 读写仍走既有 <see cref="BatteryLimitWriter" /> /
/// <see cref="BatteryLimitRestore" /> / <see cref="BatteryControl" />，⛔ 不迁移它们的算法。</para>
/// <para>⚠️ 迁移点（UI 行为，刻意留在窗口）：关闭“无极限充滑块”后的**写回满充**
/// （原窗口 <c>WriteBackFullChargeAfterSliderOffAsync</c>）、滑块/三档按钮的置灰与显隐。</para>
/// </summary>
internal interface IChargeLimitService
{
    /// <summary>本机是否支持充电阈值（板型白名单）。</summary>
    bool Supported { get; }

    /// <summary>充电上限状态（EC 实际状态 + 本地配置）。</summary>
    ChargeLimitState Read();

    /// <summary>保存并启用阈值（起充 <paramref name="lowPercent" /> / 停充 <paramref name="highPercent" />）。</summary>
    ServiceResult SaveThreshold(int lowPercent, int highPercent);

    /// <summary>Raw EC write for threshold (no config save; caller keeps BatteryLimitRestore.SaveConfig order).</summary>
    ServiceResult EnableThresholdRaw(int lowPercent, int highPercent);

    /// <summary>Raw EC write to disable threshold (no config save; caller keeps SaveConfig order).</summary>
    ServiceResult DisableThresholdRaw();

    /// <summary>启用阈值（等价于“保存并启用”）。</summary>
    ServiceResult Enable(int lowPercent, int highPercent);

    /// <summary>关闭阈值（EC 恢复默认）。</summary>
    ServiceResult Disable();

    /// <summary>无极限充滑块开关（只落盘；关闭后的“写回满充”仍在窗口）。</summary>
    ServiceResult SetSliderEnabled(bool on);

    /// <summary>EC 直写开关（<c>on=true</c> ⇒ 关闭 EC 直写，config 记 <c>ec_charge_limit="0"</c>）。</summary>
    ServiceResult SetEcDirectWrite(bool on);
}

/// <inheritdoc />
internal sealed class ChargeLimitService : IChargeLimitService
{
    private const string EcDirectWriteKey = "ec_charge_limit";
    private const string SliderKey = "charge_limit_slider";

    private readonly BackendAccessor _backend;

    internal ChargeLimitService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public bool Supported => _backend.Hw is { } hw && BatteryLimitPanel.IsSupportedBoard(hw);

    public ChargeLimitState Read()
    {
        bool supported = Supported;
        bool enabled = false;
        int low = 60;
        int high = 80;
        int mode = 0;
        bool ecRead = false;
        try
        {
            ecRead = BatteryLimitWriter.TryReadState(out int ecHigh, out int ecLow, out int ecMode, out _);
            if (ecRead)
            {
                high = ecHigh;
                low = ecLow;
                mode = ecMode;
                enabled = ecMode == BatteryLimitWriter.ModeActive;
            }
        }
        catch
        {
        }
        try
        {
            if (BatteryLimitRestore.TryLoad(out bool configEnabled, out int configLow, out int configHigh))
            {
                enabled |= configEnabled;
                if (!ecRead)
                {
                    low = configLow;
                    high = configHigh;
                }
            }
        }
        catch
        {
        }
        return new ChargeLimitState(
            Supported: supported,
            Enabled: enabled,
            LowPercent: low,
            HighPercent: high,
            Mode: mode,
            EcDirectWriteDisabled: string.Equals(AppConfig.GetString(EcDirectWriteKey), "0", StringComparison.Ordinal),
            SliderEnabled: AppConfig.Is(SliderKey),
            DisplayLimitPercent: SafeCall(BatteryControl.ResolveDisplayLimitPercent, 100),
            SelectedModePercent: SafeCall(BatteryControl.SelectedModePercent, 80));
    }

    public ServiceResult EnableThresholdRaw(int lowPercent, int highPercent)
    {
        try
        {
            bool ok = BatteryLimitWriter.TryEnable(lowPercent, highPercent, allowInitialDischarge: true, out string detail);
            return ok ? ServiceResult.Success(detail) : ServiceResult.Fail(detail);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ex.GetType().Name);
        }
    }

    public ServiceResult DisableThresholdRaw()
    {
        try
        {
            bool ok = BatteryLimitWriter.TryDisable(out string detail);
            return ok ? ServiceResult.Success(detail) : ServiceResult.Fail(detail);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ex.GetType().Name);
        }
    }

    public ServiceResult SaveThreshold(int lowPercent, int highPercent)
    {
        if (!Supported)
        {
            return ServiceResult.Fail(WpfStrings.ChargeUnsupported);
        }
        if (lowPercent is < 1 or > 99 || highPercent is < 1 or > 99)
        {
            return ServiceResult.Fail(WpfStrings.ChargeRangeInvalid);
        }
        if (lowPercent >= highPercent)
        {
            return ServiceResult.Fail(WpfStrings.ChargeOrderInvalid);
        }
        try
        {
            bool ok = BatteryLimitWriter.TryEnable(lowPercent, highPercent, allowInitialDischarge: true, out string detail);
            if (!ok)
            {
                return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_19 + detail);
            }
            BatteryLimitRestore.SaveConfig(true, lowPercent, highPercent);
            return ServiceResult.Success(WpfStrings.OriginalStyleWindow_xaml_Text_23 + highPercent + WpfStrings.ChargeStartSeparator + lowPercent + "%" + WpfStrings.ClosingParen);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.ChargeEnableExceptionPrefix + ex.GetType().Name);
        }
    }

    public ServiceResult Enable(int lowPercent, int highPercent) => SaveThreshold(lowPercent, highPercent);

    public ServiceResult Disable()
    {
        if (!Supported)
        {
            return ServiceResult.Fail(WpfStrings.ChargeUnsupported);
        }
        try
        {
            bool ok = BatteryLimitWriter.TryDisable(out string detail);
            if (!ok)
            {
                return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_21 + detail);
            }
            BatteryLimitRestore.SaveConfig(false, 0, 0);
            return ServiceResult.Success(WpfStrings.OriginalStyleWindow_xaml_Text_20);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.ChargeDisableExceptionPrefix + ex.GetType().Name);
        }
    }

    public ServiceResult SetSliderEnabled(bool on)
    {
        AppConfig.Set(SliderKey, on ? 1 : 0);
        return ServiceResult.Success(on ? WpfStrings.ChargeSliderOn : WpfStrings.ChargeSliderOff);
    }

    public ServiceResult SetEcDirectWrite(bool on)
    {
        if (on)
        {
            AppConfig.Set(EcDirectWriteKey, "0");
            return ServiceResult.Success(WpfStrings.ChargeEcDirectOff);
        }
        AppConfig.Remove(EcDirectWriteKey);
        return ServiceResult.Success(WpfStrings.ChargeEcAuto);
    }

    private static int SafeCall(Func<int> read, int fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }
}
