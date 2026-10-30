using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MechrevoLite;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Resources;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 「更多开关」/快捷磁贴的共享目录 + 状态 + 下发（两窗共用同一把尺子）✅
/// <para>⭐ 解决的原问题：分组/名称表在 <c>OriginalStyleWindow.WideMoreSwitches()</c>（530-575 行）里，
/// 迷你窗只能通过宽屏窗口拿；<c>WideToggleQuickAsync</c> 又直接摸 <c>_backendProbe</c>。</para>
/// <para>⭐ 现在：目录（<see cref="Groups" />）+ 能力判据（<see cref="Supports" />）+ 状态（<see cref="GetState" />）
/// + 下发（<see cref="SetAsync" />）都在服务层，窗口只渲染。</para>
/// <para>⚠️ 迁移点：目录里的中文文案目前仍硬编码在服务层（与原窗口一致）；后续应迁到资源/resx
/// 或由 ViewModel 注入。⛔ 本骨架不改变任何开关行为。</para>
/// </summary>
internal interface IQuickSwitchService
{
    /// <summary>「更多开关」分组目录（顺序 = 渲染顺序）。</summary>
    IReadOnlyList<QuickSwitchGroup> Groups { get; }

    /// <summary>全部开关项（扁平，去分组；供磁贴选择器等使用）。</summary>
    IReadOnlyList<QuickSwitchItem> AllItems { get; }

    /// <summary>本机是否支持该开关（与 WinForms 侧 StaticQuick 对齐）；未连接 ⇒ false。</summary>
    bool Supports(string key);

    /// <summary>读取开关状态；未连接/读不到 ⇒ false。</summary>
    bool GetState(string key);

    /// <summary>下发开关目标值；返回后端确认结果。</summary>
    Task<ServiceResult> SetAsync(string key, bool on);

    /// <summary>翻转（先读当前状态，⛔ 不猜）。</summary>
    Task<ServiceResult> ToggleAsync(string key);
}

/// <inheritdoc />
internal sealed class QuickSwitchService : IQuickSwitchService
{
    private static readonly QuickSwitchGroup[] GroupCatalog =
    {
        new(WpfStrings.QuickGroupInput, new QuickSwitchItem[]
        {
            new("touchpad", WpfStrings.QuickTouchpad),
            new("wifi", "WiFi"),
            new("bt", WpfStrings.QuickBluetooth),
            new("webcam", WpfStrings.QuickWebcam),
            new("micmute", WpfStrings.QuickMicMute),
            new("numpad", WpfStrings.QuickNumpad),
            new("touchpadtoggle", WpfStrings.QuickTouchpadToggle)
        }),
        new(WpfStrings.QuickGroupKeyboard, new QuickSwitchItem[]
        {
            new("winkey", WpfStrings.QuickWinKey),
            new("fnkey", WpfStrings.QuickFnKey),
            new("capslock", WpfStrings.QuickCapsLock),
            new("osd", WpfStrings.QuickOsd),
            new("gcuosd", WpfStrings.QuickGcuOsd),
            new("copilot", WpfStrings.QuickCopilot)
        }),
        new(WpfStrings.QuickGroupPower, new QuickSwitchItem[]
        {
            new("usb", WpfStrings.QuickUsb),
            new("acrecovery", WpfStrings.QuickAcRecovery),
            new("highperf", WpfStrings.QuickHighPerf),
            new("apuboost", WpfStrings.QuickApuBoost),
            new("fanboost", WpfStrings.QuickFanBoost),
            new("deepsleep", WpfStrings.QuickDeepSleep),
            new("gamewhitelist", WpfStrings.QuickGameWhitelist),
            new("cpuadvperf", WpfStrings.QuickCpuAdvPerf),
            new("transparency", WpfStrings.QuickTransparency),
            new("darktheme", WpfStrings.QuickDarkTheme),
            new("monitoroff", WpfStrings.QuickMonitorOff),
            new("winupdate", WpfStrings.QuickWinUpdate)
        }),
        new(WpfStrings.QuickGroupSleep, new QuickSwitchItem[]
        {
            new("sleepnetoff", WpfStrings.QuickSleepNetOff),
            new("sleepaway", WpfStrings.QuickSleepAway),
            new("sleepsysreq", WpfStrings.QuickSleepSysReq),
            new("sleeprtcwake", WpfStrings.QuickSleepRtcWake),
            new("sleepdeep", WpfStrings.QuickSleepDeep),
            new("sleephibernate", WpfStrings.QuickSleepHibernate)
        })
    };

    private static readonly QuickSwitchItem[] FlatCatalog =
        GroupCatalog.SelectMany(static group => group.Items).ToArray();

    private readonly BackendAccessor _backend;

    internal QuickSwitchService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public IReadOnlyList<QuickSwitchGroup> Groups => GroupCatalog;

    public IReadOnlyList<QuickSwitchItem> AllItems => FlatCatalog;

    /// <summary>
    /// ⭐ 按 key 取当前语言的显示名（task-33：英文模式快捷开关/更多开关动态标签收口）。
    /// <para>⚠️ 迷你窗的快捷磁贴目录仍带着旧的中文 name（OriginalStyleWindow.QuickTileCatalog），
    /// 宽屏只拿到 label 文本；这里按 key 重新取资源，保证 en-US 下显示 English ✅。
    /// 未登记的 key 回退调用方给的 label（行为不变）。</para>
    /// </summary>
    /// <summary>按 key 取当前语言的完整显示名（优先使用更多开关目录里的本地化名称）。</summary>
    internal static string DisplayLabel(string key)
    {
        foreach (QuickSwitchGroup group in GroupCatalog)
        {
            foreach (QuickSwitchItem item in group.Items)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Label;
                }
            }
        }
        return Label(key, key);
    }

    internal static string Label(string key, string? fallback = null)
    {
        // ⭐ 中文保持原文：迷你窗目录里的中文短名（强冷/麦克风/息屏…）与「更多开关」里的长名不同，
        //    zh 下必须沿用调用方 label（行为/文案零变化），只有非中文语言才换成资源里的译名 ✅。
        //    ⚠️ 调用方没给 label 时（状态栏里带 key 的旧文案）zh 也继续显示 key，保持原文。
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(fallback) ? (key ?? string.Empty) : fallback!;
        }

        string text = (key ?? string.Empty).ToLowerInvariant() switch
        {
            "touchpad" => WpfStrings.QuickTouchpad,
            "wifi" => WpfStrings.QuickWifi,
            "bt" => WpfStrings.QuickBluetooth,
            "webcam" => WpfStrings.QuickWebcam,
            "micmute" => WpfStrings.QuickMicMute,
            "numpad" => WpfStrings.QuickNumpad,
            "touchpadtoggle" => WpfStrings.QuickTouchpadToggle,
            "winkey" => WpfStrings.QuickWinKey,
            "fnkey" => WpfStrings.QuickFnKey,
            "capslock" => WpfStrings.QuickCapsLock,
            "osd" => WpfStrings.QuickOsd,
            "gcuosd" => WpfStrings.QuickGcuOsd,
            "copilot" => WpfStrings.QuickCopilot,
            "usb" => WpfStrings.QuickUsb,
            "acrecovery" => WpfStrings.QuickAcRecovery,
            "highperf" => WpfStrings.QuickHighPerf,
            "apuboost" => WpfStrings.QuickApuBoost,
            "fanboost" => WpfStrings.QuickFanBoost,
            "deepsleep" => WpfStrings.QuickDeepSleep,
            "gamewhitelist" => WpfStrings.QuickGameWhitelist,
            "cpuadvperf" => WpfStrings.QuickCpuAdvPerf,
            "transparency" => WpfStrings.QuickTransparency,
            "darktheme" => WpfStrings.QuickDarkTheme,
            "monitoroff" => WpfStrings.QuickMonitorOff,
            "winupdate" => WpfStrings.QuickWinUpdate,
            "sleepnetoff" => WpfStrings.QuickSleepNetOff,
            "sleepaway" => WpfStrings.QuickSleepAway,
            "sleepsysreq" => WpfStrings.QuickSleepSysReq,
            "sleeprtcwake" => WpfStrings.QuickSleepRtcWake,
            "sleepdeep" => WpfStrings.QuickSleepDeep,
            "sleephibernate" => WpfStrings.QuickSleepHibernate,
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }
        return string.IsNullOrWhiteSpace(fallback) ? (key ?? string.Empty) : fallback!;
    }

    /// <summary>
    /// ⭐ 分组标题本地化（task-33）：后端未就绪时 <c>WideMoreSwitches()</c> 会回退到硬编码中文组名，
    /// 这里按中文原文翻译；已经是当前语言的标题原样返回（逻辑不变）。
    /// </summary>
    internal static string GroupTitle(string title) => title switch
    {
        "输入设备" => WpfStrings.QuickGroupInput,
        "键盘与热键" => WpfStrings.QuickGroupKeyboard,
        "电源与系统" => WpfStrings.QuickGroupPower,
        "睡眠省电" => WpfStrings.QuickGroupSleep,
        _ => title
    };

    public bool Supports(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        if (string.Equals(key, "osd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return false;
        }
        if (string.Equals(key, "gcuosd", StringComparison.OrdinalIgnoreCase))
        {
            return IsGcuMachine();
        }
        return probe.SupportsQuick(key) || IsBaolongdaFanBoost(key);
    }

    public bool GetState(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        if (string.Equals(key, "osd", StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.Get("lm_osd_enabled", 1) != 0;
        }
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return false;
        }
        if (string.Equals(key, "gcuosd", StringComparison.OrdinalIgnoreCase))
        {
            // GCU-OSD 是 GCU 自己的 OSD，不是 LM OSD。
            return probe.QuickState("gcuosd");
        }
        // 宝龙达强冷状态在 MechrevoHw.FanBoostActive（本机通道），probe.QuickState 读的是 GCU 字段。
        if (IsBaolongdaFanBoost(key))
        {
            return MechrevoHw.FanBoostActive;
        }
        return probe.QuickState(key);
    }

    /// <summary>GCU-OSD 只属于 GCU 机型；宝龙达/无 GCU 机型不显示。</summary>
    private bool IsGcuMachine()
    {
        try
        {
            if (_backend.Hw?.UsesBaolongda == true)
            {
                return false;
            }
            if (_backend.Probe?.IsGcuConnected == true || _backend.Hw?.IsGcuConnected == true)
            {
                return true;
            }
            string variant = MechrevoLite.Hardware.GcuIdentity.Variant ?? "";
            return variant.Length > 0
                && !string.Equals(variant, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(variant, "unknown", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ServiceResult> SetAsync(string key, bool on)
    {
        if (string.Equals(key, "osd", StringComparison.OrdinalIgnoreCase))
        {
            AppConfig.Set("lm_osd_enabled", on ? 1 : 0);
            AppConfig.Flush();
            // 关掉这条路过去没有任何日志：诊断包里只看到“OSD 不弹”，查不到是谁关的。
            Logger.WriteError("[osd] 本地 OSD 总开关=" + (on ? "开" : "关")
                + "（lm_osd_enabled=" + AppConfig.Get("lm_osd_enabled", 1) + "）");
            if (on)
            {
                L_Mechrevo.WpfP0.OsdService.Show("\uE7F4", WpfStrings.QuickOsd, WpfStrings.StateOn);
            }
            return ServiceResult.Success(on ? WpfStrings.QuickResultOn : WpfStrings.QuickResultOff);
        }
        string backendKey = key;
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail(WpfStrings.QuickBackendOffline);
        }
        if (string.Equals(key, "monitoroff", StringComparison.OrdinalIgnoreCase) && on
            && !L_Mechrevo.WpfP0.Services.VirtualDisplayBlankService.IsSingleInternalDisplay(out string monitorReason))
        {
            L_Mechrevo.WpfP0.ToastService.Show(monitorReason, failure: true);
            return ServiceResult.Fail(monitorReason);
        }
        if (!Supports(key))
        {
            return ServiceResult.Fail(WpfStrings.QuickUnsupported + key);
        }
        try
        {
            bool ok;
            if (IsBaolongdaFanBoost(key))
            {
                MechrevoHw? hw = _backend.Hw;
                if (hw == null)
                {
                    return ServiceResult.Fail(WpfStrings.QuickBackendOffline);
                }
                bool handled = hw.TrySetFanBoostViaBackend(on, out bool confirmed);
                if (!handled)
                {
                    return ServiceResult.Fail(WpfStrings.QuickFanBoostBackendUnavailable);
                }
                return confirmed
                    ? ServiceResult.Success(on ? WpfStrings.QuickFanBoostOnConfirmed : WpfStrings.QuickFanBoostOffConfirmed)
                    : ServiceResult.Fail(on ? WpfStrings.QuickFanBoostOnUnconfirmed : WpfStrings.QuickFanBoostOffUnconfirmed);
            }
            ok = await probe.SwitchQuickAsync(backendKey, on).ConfigureAwait(false);
            // Wi-Fi / 蓝牙在部分后端没有可靠回读（命令发出但 confirmed=false）；
            // 用户明确点了开关时仍要弹 OSD，否则表现为“点了没反应”。
            bool forceOsd = string.Equals(key, "wifi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "bt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "touchpad", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "touchpadtoggle", StringComparison.OrdinalIgnoreCase);
            if (ok || forceOsd)
            {
                L_Mechrevo.WpfP0.OsdService.ShowQuickSwitch(key, on);
            }
            return ok
                ? ServiceResult.Success(on ? WpfStrings.QuickResultOn : WpfStrings.QuickResultOff)
                : ServiceResult.Fail(WpfStrings.QuickUnconfirmed + key);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.QuickApplyFailed + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> ToggleAsync(string key)
    {
        return await SetAsync(key, !GetState(key)).ConfigureAwait(false);
    }

    private bool IsBaolongdaFanBoost(string key)
    {
        return string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase) && _backend.IsBaolongda;
    }
}
