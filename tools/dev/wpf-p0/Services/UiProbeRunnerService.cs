using System;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// task-20：旧 WinForms <c>MechrevoLite.UiProbeRunner</c>（`6faf99b^:source/MechrevoLite/UiProbeRunner.cs`）
/// 的 WPF 等价入口 —— **无硬件副作用的 UI 预览（合成能力位，只显示、不下发任何命令）**。
///
/// <para>旧版行为（CLI/环境变量）：</para>
/// <code>
/// L-Mechrevo.exe --ui-probe list
/// L-Mechrevo.exe --ui-probe wujie:settings
/// L-Mechrevo.exe --ui-probe all:custommode
/// 无参数时也读环境变量 LMECHREVO_UI_PROBE=&lt;profile&gt;:&lt;form&gt;。
/// 能力档：all / wujie / baolongda / gen50 / minimal；窗体：settings / custommode / fancurve。
/// 启动时用合成能力位构造 MechrevoHw，并置 Program.UiAuditUseReportedCapabilities=true。
/// </code>
///
/// <para>WPF 等价实现：</para>
/// <list type="number">
/// <item>参数解析、能力档表、帮助文本、退出码（list=0 / 未知=2）与旧版一致；</item>
/// <item><see cref="TryHandleHeadless" /> 处理 `list` / 未知档（可直接退出，不建窗口）；
/// <see cref="TryApplyPreview" /> 对合法档构造合成 <see cref="MechrevoHw" />（internal ctor
/// <c>(publishOverride, capabilities)</c>）挂到 <see cref="WpfBackend" />，并置 <c>Program.UiAuditMode=true</c>
/// —— 等价旧版 <c>UiAuditUseReportedCapabilities=true</c> 的「用上报能力位、不真探查、只显示」语义。之后正常启动
/// 主窗即可看到按合成能力位渲染的 UI。</item>
/// </list>
///
/// <para>⚠️ 与旧版的已知差异（文档 §5 有完整说明）：旧版能直接 <c>new SettingsForm()/CustomModeForm()/FanCurveForm()</c>
/// 单独打开某个窗体；WPF 这三个已重构为 <c>OriginalStyleWindow</c> 内的页面（首页/自定义页），
/// 本服务无法在写作用域外选择页面 ⇒ <c>form</c> 参数只记录日志（保证 `--ui-probe list` / 能力档预览可用），
/// 不改变正常启动流程。</para>
///
/// <para>⚠️ 接线（本任务不改 App.xaml.cs）：在 App.OnStartup **最顶部、单实例互斥体之前**：
/// <code>
/// if (UiProbeRunnerService.TryHandleHeadless(e.Args, out int uiProbeExit))
/// { Environment.ExitCode = uiProbeExit; Shutdown(); return; }
/// </code>
/// 在语言/字体设置之后、创建 OriginalStyleWindow 之前：
/// <code>UiProbeRunnerService.TryApplyPreview(e.Args);</code>
/// </para>
/// </summary>
internal static class UiProbeRunnerService
{
    internal const string CliArgument = "--ui-probe";

    /// <summary>无 CLI 参数时的等价环境变量（与旧版一致）。</summary>
    internal const string EnvironmentVariable = "LMECHREVO_UI_PROBE";

    private static readonly string[] KnownProfiles = { "all", "nuc", "wujie", "baolongda", "gen50", "minimal" };

    private static readonly string[] KnownForms = { "settings", "custommode", "fancurve", "主界面", "自定义模式", "风扇曲线" };

    /// <summary>
    /// 处理无需 UI 的探针路径：<c>list</c>/<c>?</c>/空参数 → 打印帮助并退出 0；未知能力档/窗体 → 退出 2。
    /// 返回 true 表示调用方应设置 ExitCode 并退出；false 表示继续正常启动（可能是合法预览，由
    /// <see cref="TryApplyPreview" /> 应用合成能力位）。
    /// </summary>
    internal static bool TryHandleHeadless(string[] args, out int exitCode)
    {
        exitCode = 0;
        string? value = ResolveProbeValue(args);
        if (value == null)
        {
            return false;
        }
        if (value.Length == 0 || value.Equals("list", StringComparison.OrdinalIgnoreCase) || value == "?")
        {
            PrintHelp();
            return true;
        }
        Parse(value, out string profile, out string form);
        if (!IsKnownProfile(profile))
        {
            WriteBoth("未知能力档：" + profile);
            PrintHelp();
            exitCode = 2;
            return true;
        }
        if (!IsKnownForm(form))
        {
            WriteBoth("未知窗体：" + form);
            PrintHelp();
            exitCode = 2;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 对合法的 <c>--ui-probe &lt;profile&gt;:&lt;form&gt;</c>（或环境变量）应用合成能力位 + 审计预览模式。
    /// 返回 true 表示已应用预览模式（调用方继续正常启动主窗即可）。
    /// </summary>
    internal static bool TryApplyPreview(string[] args)
    {
        string? value = ResolveProbeValue(args);
        if (value == null || value.Length == 0
            || value.Equals("list", StringComparison.OrdinalIgnoreCase) || value == "?")
        {
            return false;
        }
        Parse(value, out string profile, out string form);
        if (!IsKnownProfile(profile) || !IsKnownForm(form))
        {
            // 未知档由 TryHandleHeadless 负责退出；这里不再重复报错 / 不改启动行为。
            return false;
        }
        try
        {
            MechrevoDeviceCapabilities caps = BuildCapabilities(profile);
            if (string.Equals(profile, "nuc", StringComparison.OrdinalIgnoreCase))
            {
                MechrevoLite.Hardware.MachineIdentity identity = new(
                    "LAPKC71E", "ui-probe", false, "Gen30", false,
                    "NUC X15 (probe)", "ui-probe", "", false, false,
                    "LAPKC71E", "ui-probe", "", "", false, "", "NUC X15 (probe)");
                MechrevoLite.Hardware.BackendRouter.SetPreviewDecision(identity);
            }
            MechrevoHw hw = new MechrevoHw(publishOverride: null, capabilities: caps);
            WpfBackend.Attach(hw, null);
            // 等价旧版 Program.UiAuditUseReportedCapabilities = true：
            // 让 WPF 侧能力位门控（PerfModeCapabilities 等）采用“上报能力位/审计”语义，只显示不下发。
            MechrevoLite.Program.UiAuditMode = true;
            WriteBoth("[ui-probe] WPF UI 预览：profile=" + profile + " form=" + form
                + "（合成能力位、无服务连接、仅显示；form 在 WPF 中映射为页面：settings→首页 / custommode→自定义页 / fancurve→自定义页风扇曲线）");
            Logger.WriteLine("[ui-probe] 合成能力位已挂到 WpfBackend；后续 BackendHost 真实连接会替换为真机能力位（预览仅用于样式检查）");
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[ui-probe] 应用预览失败（不影响正常启动）：" + ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    /// <summary>与旧版一致的解析：<c>--ui-probe &lt;value&gt;</c>，或（无参数时）环境变量；否则 null。</summary>
    private static string? ResolveProbeValue(string[] args)
    {
        bool hasCli = args.Length > 0
            && string.Equals(args[0], CliArgument, StringComparison.OrdinalIgnoreCase);
        if (hasCli)
        {
            return args.Length > 1 ? (args[1] ?? string.Empty).Trim() : string.Empty;
        }
        if (args.Length != 0)
        {
            return null;
        }
        try
        {
            string env = (Environment.GetEnvironmentVariable(EnvironmentVariable) ?? string.Empty).Trim();
            return env.Length > 0 ? env : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Parse(string value, out string profile, out string form)
    {
        string text = (value ?? string.Empty).Trim();
        int separator = text.IndexOf(':');
        if (separator < 0)
        {
            profile = text;
            form = "settings";
        }
        else
        {
            profile = text.Substring(0, separator).Trim();
            form = text.Substring(separator + 1).Trim();
            if (form.Length == 0)
            {
                form = "settings";
            }
        }
    }

    private static bool IsKnownProfile(string profile)
    {
        return Array.Exists(KnownProfiles, p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownForm(string form)
    {
        return Array.Exists(KnownForms, f => string.Equals(f, form, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintHelp()
    {
        string[] lines =
        {
            "UI 探针（WPF 版：合成能力位、仅显示、不触硬件）：",
            "  L-Mechrevo.exe --ui-probe list",
            "  L-Mechrevo.exe --ui-probe <能力档>:<窗体>",
            "  能力档：all（全开）· wujie（无界类）· baolongda（宝龙达类）· gen50（50 系 GCU 类）· minimal（极简）",
            "  窗体：settings（主界面）· custommode（自定义模式）· fancurve（风扇曲线）",
            "  例：--ui-probe wujie:settings；无参数时也读环境变量 LMECHREVO_UI_PROBE",
            "  说明：合成能力位 + 无服务连接 = 只检查布局/显隐/文案，不代表真机能力。"
        };
        foreach (string line in lines)
        {
            WriteBoth(line);
        }
    }

    /// <summary>同一行同时写控制台（从终端启动时可见）与日志（GUI 双击启动时唯一可见处）。</summary>
    private static void WriteBoth(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
        }
        try
        {
            Logger.WriteLine("[ui-probe] " + message);
        }
        catch
        {
        }
    }

    /// <summary>与旧版同表的合成能力位（逐字段复制自 UiProbeRunner.BuildCapabilities；只影响显示）。</summary>
    private static MechrevoDeviceCapabilities BuildCapabilities(string name)
    {
        bool all = string.Equals(name, "all", StringComparison.OrdinalIgnoreCase);
        bool wujie = string.Equals(name, "wujie", StringComparison.OrdinalIgnoreCase);
        bool baolongda = string.Equals(name, "baolongda", StringComparison.OrdinalIgnoreCase);
        bool gen50 = string.Equals(name, "gen50", StringComparison.OrdinalIgnoreCase);
        bool nuc = string.Equals(name, "nuc", StringComparison.OrdinalIgnoreCase);
        bool minimal = string.Equals(name, "minimal", StringComparison.OrdinalIgnoreCase);
        bool full = all || gen50 || nuc;
        return new MechrevoDeviceCapabilities
        {
            ProjectId = all ? "UI-PROBE" : (nuc ? "LAPKC71E" : (wujie ? "WUJIE" : (baolongda ? "BAOLONGDA" : (gen50 ? "GEN50" : "MINIMAL")))),
            Manufacturer = "MECHREVO",
            Model = all ? "UI 探针（全开）" : (nuc ? "NUC X15（探针）" : (wujie ? "无界（探针）" : (baolongda ? "宝龙达（探针）" : (gen50 ? "50 系 GCU（探针）" : "极简（探针）")))),
            BaseboardProduct = all ? "UI-PROBE" : (nuc ? "LAPKC71E" : (wujie ? "WUJIE14XA" : (baolongda ? "MRID6" : (gen50 ? "JIAOLONG" : "MINIMAL")))),
            BiosVersion = "UI-PROBE",
            IsMechrevo = true,
            ProfileAvailable = !minimal,
            Keyboard = all || nuc || wujie || baolongda,
            Lightbar = all || nuc || baolongda,
            RgbLightbar = all || nuc || baolongda,
            LogoLight = all || nuc || baolongda,
            DisplayRefresh = all || nuc || gen50 || baolongda,
            ColorCalibration = all || nuc || gen50 || baolongda,
            ColorCalibrationLevel = full ? 4 : (baolongda ? 2 : 0),
            DgpuDirect = all || baolongda || gen50,
            IgpuOnly = full,
            FanBoost = all || nuc || wujie || baolongda || gen50,
            FanSettings = all || nuc || wujie || baolongda,
            OverclockSettings = all || baolongda || gen50,
            CpuPerformanceTuning = all || gen50 || baolongda,
            LiquidCooling = all || gen50,
            LiquidCoolingAutoMode = all || gen50,
            Numpad = full,
            AcRecovery = all || gen50 || baolongda,
            AcRecoveryOn = (all || gen50) ? true : (baolongda ? (bool?)false : null),
            LcdOverdrive = all || gen50,
            LocalDimming = all || gen50,
            TurboMode = all || nuc || gen50,
            TurboSubMode = all || nuc || gen50,
            SmartBalance = all || gen50,
            RamFan15 = all || gen50,
            NvidiaGpu = all || baolongda || gen50,
            GpuHotSwap = all || baolongda,
            AmdPlatform = wujie || gen50,
            IsOldType = false,
            KeyboardType = all ? 2 : (wujie ? 1 : (baolongda ? 2 : 0))
        };
    }
}
