using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Display;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Resources;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 显示/屏幕域（刷新率、亮度、校色、色彩增强；两窗共享）✅
/// <para>⭐ 解决的原问题：<c>WideDisplay()</c>/<c>WideSetRefresh()</c>/<c>WideSetBrightness()</c>
/// /<c>WideCalibration()</c>/<c>WideDisplayEffect()</c> 把“后端下发 + AppConfig 落盘 + 控件状态”
/// 混在一起，迷你窗只能间接调用。</para>
/// <para>⚠️ 迁移点（本骨架刻意不做）：MiniLED/FHD/HDR 的**切换**仍留在窗口 —— FHD 会弹
/// “需要重启”的确认框、MiniLED 切换后要刷新整页；先只暴露只读状态，避免把 UI 决策搬进服务。</para>
/// </summary>
internal interface IDisplayService
{
    /// <summary>显示状态快照（只读；未连接也尽量给系统层面的值）。</summary>
    DisplaySnapshot Read();

    /// <summary>设置亮度（0~100；走 <see cref="ScreenBrightness.TrySet" />）。</summary>
    ServiceResult SetBrightness(int percent);

    /// <summary>手动设刷新率（≥ 面板最大值时按最大值下发；成功后关闭“自动刷新率”）。</summary>
    Task<ServiceResult> SetRefreshRateAsync(int hertz);

    /// <summary>开关“自动刷新率”（后端 + 落盘）。</summary>
    Task<ServiceResult> SetAutoRefreshAsync(bool on);

    /// <summary>LCD 过驱开关（<c>SwitchLcdOverdrive</c>）。</summary>
    Task<ServiceResult> SetLcdOverdriveAsync(bool on);

    /// <summary>屏幕校色可选档位（按能力位 + 自定义 ICC 是否可用裁剪）。</summary>
    IReadOnlyList<CalibrationOption> CalibrationOptions();

    /// <summary>当前校色档位（config <c>calib_mode</c>，缺省 1）。</summary>
    int CurrentCalibrationMode { get; }

    /// <summary>校色是否启用（config <c>calib_enabled</c>）。</summary>
    bool CalibrationEnabled { get; }

    /// <summary>开/关校色（含“自定义 ICC”分支；与原窗口 3608-3658 行同一套语义）。</summary>
    Task<ServiceResult> ApplyCalibrationAsync(bool on, int mode);

    /// <summary>色彩增强档位表（后端 profile key + 中文标签）。</summary>
    IReadOnlyList<DisplayEffectOption> DisplayEffectModes();

    /// <summary>当前色彩增强档位（config <c>display_effect_mode</c>）。</summary>
    string CurrentDisplayEffectMode { get; }

    /// <summary>色彩增强是否开启（config <c>display_effect</c>）。</summary>
    bool DisplayEffectOn { get; }

    /// <summary>开/关色彩增强；开启时按 <paramref name="mode" />（null = 用已保存档位）应用一次。</summary>
    Task<ServiceResult> ApplyDisplayEffectAsync(bool on, string? mode);

    /// <summary>切换色彩增强档位（已开启时立即应用；未开启只落盘）。</summary>
    Task<ServiceResult> SetDisplayEffectModeAsync(string mode);
}

/// <inheritdoc />
internal sealed class DisplayService : IDisplayService
{
    private static readonly DisplayEffectOption[] EffectModes =
    {
        new("VibrantMode", WpfStrings.DisplayVibrant),
        new("InternetMode", WpfStrings.DisplayInternet),
        new("VideoMode", WpfStrings.DisplayVideo),
        new("LowBlueMode", WpfStrings.DisplayLowBlue),
        new("CinemaMode", WpfStrings.DisplayCinema),
        new("PhotoMode", WpfStrings.DisplayPhoto)
    };

    private readonly BackendAccessor _backend;

    internal DisplayService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public DisplaySnapshot Read()
    {
        MechrevoHw? hw = _backend.Hw;
        BackendProbe? probe = _backend.Probe;
        int width = 0;
        int height = 0;
        try
        {
            System.Drawing.Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? default;
            width = bounds.Width;
            height = bounds.Height;
        }
        catch
        {
        }
        string? laptopScreen = SafeString(static () => ScreenNative.FindLaptopScreen(false));
        int currentHz = SafeCount(() => ScreenNative.GetRefreshRate(laptopScreen));
        if (currentHz < 0)
        {
            currentHz = SafeCount(static () => ScreenNative.GetRefreshRate(null));
        }
        int maxHz = SafeCount(() => ScreenControl.GetMaxRate(laptopScreen));
        int brightness = ScreenBrightness.TryGet(out int value) ? value : 100;
        return new DisplaySnapshot(
            ScreenWidth: width,
            ScreenHeight: height,
            CurrentRefreshHz: currentHz,
            MaxRefreshHz: maxHz,
            MinRefreshHz: ScreenControl.MIN_RATE,
            AutoRefresh: AppConfig.Is("screen_auto"),
            Brightness: brightness,
            BrightnessSupported: ScreenBrightness.TryGet(out _),
            LcdOverdriveSupported: hw?.SupportsLcdOverdrive == true,
            LcdOverdrive: hw?.LcdOverdrive == true,
            LocalDimmingSupported: hw?.SupportsLocalDimming == true,
            LocalDimming: hw?.LocalDimming == true,
            FhdSupported: L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327708u) >= 0,
            FhdOn: L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327708u) > 0,
            HdrSupported: L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327793u) >= 0,
            HdrOn: L_Mechrevo.WpfP0.Services.WpfBackend.acpi?.DeviceGet(327793u) > 0,
            ColorCalibrationLevel: probe?.ColorCalibrationLevel ?? 1,
            ColorCalibrationSupported: probe?.SupportsColorCalibration == true);
    }

    public ServiceResult SetBrightness(int percent)
    {
        int target = Math.Clamp(percent, 0, 100);
        try
        {
            return ScreenBrightness.TrySet(target)
                ? ServiceResult.Success(WpfStrings.DisplayBrightnessSet)
                : ServiceResult.Fail(WpfStrings.DisplayBrightnessUnconfirmed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayBrightnessExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetRefreshRateAsync(int hertz)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SwitchRefreshRateAsync(hertz).ConfigureAwait(false);
            // 与原窗口 RefreshRate_Click 一致：手选刷新率 ⇒ 自动刷新率关闭并落盘。
            AppConfig.Set("screen_auto", 0);
            ScreenControl.SetAutoRefresh(0);
            AppConfig.Flush();
            return ok ? ServiceResult.Success(WpfStrings.DisplayRefreshSent) : ServiceResult.Fail(WpfStrings.DisplayRefreshFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayRefreshExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetAutoRefreshAsync(bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SwitchAutoRefreshAsync(on).ConfigureAwait(false);
            // 与原窗口 AutoRefresh_Click 一致：无论后端回执如何都落盘（下一次启动仍按用户选择）。
            AppConfig.Set("screen_auto", on ? 1 : 0);
            AppConfig.Flush();
            return ok ? ServiceResult.Success(WpfStrings.DisplayAutoRefreshSent) : ServiceResult.Fail(WpfStrings.DisplayAutoRefreshFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayAutoRefreshExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetLcdOverdriveAsync(bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok = await probe.SetLcdOverdriveAsync(on).ConfigureAwait(false);
            return ok ? ServiceResult.Success(on ? WpfStrings.DisplayOverdriveOn : WpfStrings.DisplayOverdriveOff) : ServiceResult.Fail(WpfStrings.DisplayOverdriveFailed);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayOverdriveExceptionPrefix + ex.GetType().Name);
        }
    }

    public IReadOnlyList<CalibrationOption> CalibrationOptions()
    {
        int level = _backend.Probe?.ColorCalibrationLevel ?? 1;
        bool nuc = _backend.Hw?.IsIntelNucMachine == true || _backend.Hw?.IsUmiMachine == true;
        List<CalibrationOption> options = new()
        {
            new(1, WpfStrings.OriginalStyleWindow_Content_9, false)
        };
        if (!nuc && level >= 2)
        {
            options.Add(new(2, "sRGB", false));
        }
        if (!nuc && level >= 3)
        {
            options.Add(new(3, "P3", false));
        }
        if (!nuc && level >= 4)
        {
            options.Add(new(4, "AdobeRGB", false));
        }
        if (CustomCalibrationAvailable())
        {
            options.Add(new(999, WpfStrings.CustomSwitch_Content, true));
        }
        return options;
    }

    public int CurrentCalibrationMode => AppConfig.Exists("calib_mode") ? AppConfig.Get("calib_mode") : 1;

    public bool CalibrationEnabled => AppConfig.Exists("calib_enabled") && AppConfig.Is("calib_enabled");

    public async Task<ServiceResult> ApplyCalibrationAsync(bool on, int mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            bool ok;
            bool nuc = _backend.Hw?.IsIntelNucMachine == true || _backend.Hw?.IsUmiMachine == true;
            if (on && mode == 999)
            {
                ok = await ApplyCustomIccAsync().ConfigureAwait(false);
            }
            else if (!on && (AppConfig.Get("calib_mode") == 999 || nuc))
            {
                await Task.Run(IccHelper.ClearAllIcc).ConfigureAwait(false);
                ok = true;
            }
            else if (nuc && on && mode == 1)
            {
                // NUC 原生档 = 清除 ICC，不做 GCU 下发。
                await Task.Run(IccHelper.ClearAllIcc).ConfigureAwait(false);
                ok = true;
            }
            else
            {
                ok = await probe.SetColorCalibrationAsync(on ? mode : 0).ConfigureAwait(false);
            }
            if (!ok)
            {
                return ServiceResult.Fail(mode == 999 ? WpfStrings.DisplayIccMissing : WpfStrings.DisplayCalibrationFailed);
            }
            AppConfig.Set("calib_mode", mode);
            AppConfig.Set("calib_enabled", on ? 1 : 0);
            AppConfig.Flush();
            return ServiceResult.Success(on ? WpfStrings.DisplayCalibrationOn : WpfStrings.DisplayCalibrationOff);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayCalibrationExceptionPrefix + ex.GetType().Name);
        }
    }

    public IReadOnlyList<DisplayEffectOption> DisplayEffectModes() => EffectModes;

    public string CurrentDisplayEffectMode => AppConfig.GetString("display_effect_mode") ?? "";

    public bool DisplayEffectOn => AppConfig.Is("display_effect");

    public async Task<ServiceResult> ApplyDisplayEffectAsync(bool on, string? mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        try
        {
            string target = string.IsNullOrWhiteSpace(mode) ? CurrentDisplayEffectMode : mode;
            bool ok = await probe.ApplyDisplayEffectAsync(on, target).ConfigureAwait(false);
            if (!ok)
            {
                return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_61);
            }
            AppConfig.Set("display_effect", on ? 1 : 0);
            if (!string.IsNullOrWhiteSpace(target))
            {
                AppConfig.Set("display_effect_mode", target);
            }
            AppConfig.Flush();
            return ServiceResult.Success(on ? WpfStrings.DisplayColorEnhancementOn : WpfStrings.DisplayColorEnhancementOff);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayColorEnhancementExceptionPrefix + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> SetDisplayEffectModeAsync(string mode)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_51);
        }
        if (string.IsNullOrWhiteSpace(mode))
        {
            return ServiceResult.Fail(WpfStrings.DisplayColorProfileEmpty);
        }
        try
        {
            bool ok = await probe.ApplyDisplayEffectProfileAsync(mode).ConfigureAwait(false);
            if (!ok)
            {
                return ServiceResult.Fail(WpfStrings.OriginalStyleWindow_xaml_Text_63);
            }
            AppConfig.Set("display_effect_mode", mode);
            AppConfig.Flush();
            await probe.RequestDisplayEffectStatusAsync().ConfigureAwait(false);
            return ServiceResult.Success(WpfStrings.DisplayColorProfileSwitched);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.DisplayColorProfileExceptionPrefix + ex.GetType().Name);
        }
    }

    private static bool CustomCalibrationAvailable()
    {
        try
        {
            return AppConfig.Is("calib_custom_on") || IccHelper.ListCustomIccFiles().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ApplyCustomIccAsync()
    {
        string iccName = AppConfig.GetString("calib_custom_icc") ?? "";
        string iccPath = string.IsNullOrWhiteSpace(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
        if (string.IsNullOrWhiteSpace(iccPath) || !File.Exists(iccPath))
        {
            return false;
        }
        return await Task.Run(() => IccHelper.ApplyIcc(iccPath)).ConfigureAwait(false);
    }

    private static string? SafeString(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static int SafeCount(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return -1;
        }
    }
}
